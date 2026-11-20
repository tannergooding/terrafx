// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Animation;
using TerraFX.Assets;
using TerraFX.Graphics;
using TerraFX.Numerics;
using TerraFX.Rendering;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

internal sealed partial class SceneAssetStoreTests
{
    private static CompiledContentSchemaRegistry CreateAnimationAssetRegistry()
        => new([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
            .. RenderingSchemaCatalogue.Schemas,
            .. AnimationSchemaCatalogue.Schemas,
            .. AnimationSchemaCatalogue.PlaybackSchemas,
        ]);

    private static EbinRecord CreateAnimationClipRecord(
        EbinRecord[] tracks,
        EbinRecord[] events,
        EbinField? skeletonReference = null,
        EbinField? rootMotion = null,
        EbinField? sceneReference = null,
        uint schemaMinor = 0,
        long durationTicks = 100)
    {
        var fields = new List<EbinField>
        {
            EbinPrimitives.Int64(1, durationTicks),
            EbinPrimitives.RecordArray(2, tracks),
            EbinPrimitives.RecordArray(3, events),
            EbinPrimitives.UInt8(4, 0),
        };

        if (skeletonReference is { } selectedSkeletonReference)
        {
            fields.Add(selectedSkeletonReference);
        }

        if (rootMotion is { } selectedRootMotion)
        {
            fields.Add(selectedRootMotion);
        }

        fields.Add(EbinPrimitives.UInt8(8, 0));
        fields.Add(EbinPrimitives.Binary32(9, 0.0f));

        if (sceneReference is { } selectedSceneReference)
        {
            fields.Add(selectedSceneReference);
        }

        return new EbinRecord(
            new ContentSchemaIdentityAssignment("native/clip").SchemaId,
            1,
            schemaMinor,
            fields.ToArray());
    }

    private static EbinRecord CreateAssetReferenceRecord(uint assetId, Guid schemaId, uint schemaMajor)
        => new(
            new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId,
            1,
            0,
            [
                EbinPrimitives.UInt32(1, assetId),
                EbinPrimitives.Guid(2, schemaId),
                EbinPrimitives.UInt32(3, schemaMajor),
            ]);

    private static EbinRecord CreateAtlasRecord(uint textureAssetId, Guid frameId)
    {
        var frame = new EbinRecord(
            new ContentSchemaIdentityAssignment("native/atlas-frame").SchemaId,
            1,
            0,
            [
                EbinPrimitives.Guid(1, frameId),
                EbinPrimitives.UInt32(2, 0),
                EbinPrimitives.UInt32(3, 2),
                EbinPrimitives.UInt32(4, 2),
                EbinPrimitives.UInt32(5, 1),
                EbinPrimitives.UInt32(6, 1),
                EbinPrimitives.UInt8(7, 2),
            ]);
        return new EbinRecord(
            new ContentSchemaIdentityAssignment("native/atlas").SchemaId,
            1,
            0,
            [
                EbinPrimitives.RecordArray(1, [
                    CreateAssetReferenceRecord(
                        textureAssetId,
                        new ContentSchemaIdentityAssignment("native/texture").SchemaId,
                        schemaMajor: 1),
                ]),
                EbinPrimitives.RecordArray(2, [frame]),
            ]);
    }

    private static EbinRecord CreateClipEvent(Guid eventId, long timeTicks)
    {
        var payload = new EbinRecord(
            new ContentSchemaIdentityAssignment("native/event-marker").SchemaId,
            1,
            0,
            [new EbinField(1, 12, flags: 1, count: 5, "event"u8)]);
        return new EbinRecord(
            new ContentSchemaIdentityAssignment("native/clip-event").SchemaId,
            1,
            0,
            [
                EbinPrimitives.Guid(1, eventId),
                EbinPrimitives.Int64(2, timeTicks),
                EbinPrimitives.Record(3, payload),
            ]);
    }

    private static EbinRecord CreateJointRecord(
        Guid jointId,
        int parentIndex,
        Vector3 bindTranslation,
        Vector3 bindScale,
        Matrix4x4 inverseBind)
    {
        var localTransform = new EbinRecord(
            new ContentSchemaIdentityAssignment("scene/local-transform").SchemaId,
            1,
            0,
            [
                EbinPrimitives.Geometry(1, bindTranslation),
                EbinPrimitives.Geometry(2, Quaternion.Identity),
                EbinPrimitives.Geometry(3, bindScale),
            ]);
        return new EbinRecord(
            new ContentSchemaIdentityAssignment("native/joint").SchemaId,
            1,
            0,
            [
                EbinPrimitives.Guid(1, jointId),
                EbinPrimitives.Int32(2, parentIndex),
                EbinPrimitives.Record(3, localTransform),
                EbinPrimitives.Geometry(4, inverseBind),
            ]);
    }

    private static EbinField CreateOptionalRecordField(uint tag, EbinRecord record, byte flags = 0)
        => new(tag, 22, flags, count: 1, EbinPrimitives.Encode(record));

    private static EbinRecord CreateRootMotionRecord(Guid rootJointId)
        => new(
            new ContentSchemaIdentityAssignment("native/root-motion").SchemaId,
            1,
            0,
            [
                EbinPrimitives.Guid(1, rootJointId),
                EbinPrimitives.UInt8(2, 0),
            ]);

    private static EbinField CreateSceneReference(uint sceneAssetId)
        => CreateOptionalRecordField(
            10,
            CreateAssetReferenceRecord(
                sceneAssetId,
                new ContentSchemaIdentityAssignment("native/scene").SchemaId,
                schemaMajor: 2));

    private static byte[] CreateSkeletonRecord(EbinRecord[] joints, Guid? rootMotionJointId = null)
    {
        var fields = new List<EbinField>
        {
            EbinPrimitives.RecordArray(1, joints),
        };

        if (rootMotionJointId is { } selectedRootMotionJointId)
        {
            var rootMotionField = EbinPrimitives.Guid(2, selectedRootMotionJointId);
            fields.Add(new EbinField(2, 11, flags: 0, count: 1, rootMotionField.Payload));
        }

        return EbinPrimitives.Encode(new EbinRecord(
            new ContentSchemaIdentityAssignment("native/skeleton").SchemaId,
            1,
            0,
            fields.ToArray()));
    }

    private static byte[] CreateSkeletonRecord(Guid jointId, int parentIndex, Matrix4x4 inverseBind)
        => CreateSkeletonRecord([CreateJointRecord(jointId, parentIndex, Vector3.Zero, Vector3.One, inverseBind)]);

    private static EbinRecord CreateSpriteKey(uint atlasAssetId, Guid frameId)
        => new(
            new ContentSchemaIdentityAssignment("native/key-sprite").SchemaId,
            1,
            0,
            [
                EbinPrimitives.Int64(1, 0),
                EbinPrimitives.Record(2, CreateAssetReferenceRecord(
                    atlasAssetId,
                    new ContentSchemaIdentityAssignment("native/atlas").SchemaId,
                    schemaMajor: 1)),
                EbinPrimitives.Guid(3, frameId),
            ]);

    private static EbinRecord CreateSpriteTrack(Guid targetId, EbinRecord[] keys)
        => new(
            new ContentSchemaIdentityAssignment("native/sprite-track").SchemaId,
            1,
            0,
            [
                EbinPrimitives.Guid(1, targetId),
                EbinPrimitives.RecordArray(2, keys),
            ]);

    private static EbinRecord CreateTextureRecord()
    {
        var mip = new EbinRecord(
            new ContentSchemaIdentityAssignment("native/texture-mip").SchemaId,
            1,
            0,
            [
                EbinPrimitives.UInt32(1, 0),
                EbinPrimitives.UInt32(2, 8),
                EbinPrimitives.UInt32(3, 8),
                EbinPrimitives.Bytes(4, new byte[8 * 8 * 4]),
            ]);
        return new EbinRecord(
            new ContentSchemaIdentityAssignment("native/texture").SchemaId,
            1,
            0,
            [
                EbinPrimitives.UInt32(1, 8),
                EbinPrimitives.UInt32(2, 8),
                EbinPrimitives.UInt8(3, 0),
                EbinPrimitives.UInt8(4, 0),
                EbinPrimitives.UInt8(5, 1),
                EbinPrimitives.UInt8(6, 0),
                EbinPrimitives.UInt8(7, 0),
                EbinPrimitives.RecordArray(8, [mip]),
            ]);
    }

    private static EbinRecord CreateTransformTrack(
        Guid targetId,
        byte channel = 0,
        byte interpolation = 1,
        EbinRecord[]? keys = null,
        byte targetDomain = 1)
        => new(
            new ContentSchemaIdentityAssignment("native/transform-track").SchemaId,
            1,
            0,
            [
                EbinPrimitives.Guid(1, targetId),
                EbinPrimitives.UInt8(2, targetDomain),
                EbinPrimitives.UInt8(3, channel),
                EbinPrimitives.UInt8(4, interpolation),
                EbinPrimitives.RecordArray(5, keys ?? [CreateVector3Key(0, Vector3.Zero)]),
            ]);

    private static EbinRecord CreateVector3Key(
        long timeTicks,
        Vector3 value,
        Vector3? inTangent = null,
        Vector3? outTangent = null)
    {
        var fields = new List<EbinField>
        {
            EbinPrimitives.Int64(1, timeTicks),
            EbinPrimitives.Geometry(2, value),
        };

        if (inTangent is { } selectedInTangent)
        {
            var inTangentField = EbinPrimitives.Geometry(3, selectedInTangent);
            fields.Add(new EbinField(3, 15, flags: 0, count: 1, inTangentField.Payload));
        }

        if (outTangent is { } selectedOutTangent)
        {
            var outTangentField = EbinPrimitives.Geometry(4, selectedOutTangent);
            fields.Add(new EbinField(4, 15, flags: 0, count: 1, outTangentField.Payload));
        }

        return new EbinRecord(
            new ContentSchemaIdentityAssignment("native/key-vector3").SchemaId,
            1,
            0,
            fields.ToArray());
    }

    [Test]
    public async Task AnimationAssetBinderValidatesExpandedSceneKeysAndAtlasFrames()
    {
        var schemas = CreateAnimationAssetRegistry();
        var registrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = SceneSupport.RegisterAssetCodecs(registrationBuilder);
        _ = Renderer.RegisterAssetCodecs(registrationBuilder);
        _ = AnimationSupport.RegisterAssetCodecs(registrationBuilder);
        const uint sceneAssetId = 900;
        const uint textureAssetId = 901;
        const uint atlasAssetId = 902;
        const uint validClipAssetId = 903;
        const uint invalidSceneClipAssetId = 904;
        const uint invalidAtlasClipAssetId = 905;
        const uint mismatchedSceneAssetId = 906;
        const uint mismatchedSceneClipAssetId = 907;
        var sceneRecord = SceneAssetReaderTests.Fixtures.CreateScene(new AssetId(checked((int)sceneAssetId)));
        var initialPackageBytes = Package.CreatePackage(new Package.Entry(
            sceneAssetId,
            Package.CreateEnvelope(sceneAssetId, sceneRecord)));
        var initialPackagePath = Package.WritePackage(initialPackageBytes);
        AssetStore? initialStore = null;
        SceneExpansionPlan? earlierScenePlan = null;
        AssetStore? assets = null;
        string? packagePath = null;
        Guid expandedEntityKey;

        try
        {
            initialStore = await AssetStore.CreateAsync(new AssetStoreOptions(
                initialPackagePath,
                SHA256.HashData(initialPackageBytes),
                registrationBuilder.Build())).ConfigureAwait(false);
            using var initialSceneLease = await initialStore.LoadAsync<SceneAsset>(
                new AssetId(checked((int)sceneAssetId))).ConfigureAwait(false);
            earlierScenePlan = await ScenePrefabExpansionPlanner.PrepareTakingLeaseAsync(
                initialSceneLease.Retain(),
                SceneExpansionLimits.Default).ConfigureAwait(false);
            expandedEntityKey = earlierScenePlan.Entities[0].ExpandedEntityKey;

            var frameId = new Guid("80000000-0000-4000-8000-000000000001");
            var validTracks = new[] {
                CreateTransformTrack(expandedEntityKey, targetDomain: 0),
                CreateSpriteTrack(expandedEntityKey, [CreateSpriteKey(atlasAssetId, frameId)]),
            };
            var validClip = CreateAnimationClipRecord(validTracks, [], sceneReference: CreateSceneReference(sceneAssetId));
            var invalidSceneClip = CreateAnimationClipRecord(
                [CreateTransformTrack(new Guid("90000000-0000-4000-8000-000000000001"), targetDomain: 0)],
                [],
                sceneReference: CreateSceneReference(sceneAssetId));
            var invalidAtlasClip = CreateAnimationClipRecord(
                [
                    CreateTransformTrack(expandedEntityKey, targetDomain: 0),
                    CreateSpriteTrack(
                        expandedEntityKey,
                        [CreateSpriteKey(atlasAssetId, new Guid("80000000-0000-4000-8000-000000000002"))]),
                ],
                [],
                sceneReference: CreateSceneReference(sceneAssetId));
            var mismatchedSceneClip = CreateAnimationClipRecord(
                [CreateTransformTrack(expandedEntityKey, targetDomain: 0)],
                [],
                sceneReference: CreateSceneReference(mismatchedSceneAssetId));
            var atlasRecord = CreateAtlasRecord(textureAssetId, frameId);
            var textureRecord = CreateTextureRecord();
            var packageBytes = Package.CreatePackage(
                new Package.Entry(sceneAssetId, Package.CreateEnvelope(sceneAssetId, sceneRecord)),
                new Package.Entry(textureAssetId, Package.CreateEnvelope(textureAssetId, EbinPrimitives.Encode(textureRecord))),
                new Package.Entry(atlasAssetId, Package.CreateEnvelope(atlasAssetId, EbinPrimitives.Encode(atlasRecord))),
                new Package.Entry(validClipAssetId, Package.CreateEnvelope(validClipAssetId, EbinPrimitives.Encode(validClip))),
                new Package.Entry(invalidSceneClipAssetId, Package.CreateEnvelope(invalidSceneClipAssetId, EbinPrimitives.Encode(invalidSceneClip))),
                new Package.Entry(invalidAtlasClipAssetId, Package.CreateEnvelope(invalidAtlasClipAssetId, EbinPrimitives.Encode(invalidAtlasClip))),
                new Package.Entry(
                    mismatchedSceneAssetId,
                    Package.CreateEnvelope(
                        mismatchedSceneAssetId,
                        SceneAssetReaderTests.Fixtures.CreateScene(new AssetId(checked((int)mismatchedSceneAssetId))))),
                new Package.Entry(mismatchedSceneClipAssetId, Package.CreateEnvelope(mismatchedSceneClipAssetId, EbinPrimitives.Encode(mismatchedSceneClip))));
            packagePath = Package.WritePackage(packageBytes);
            assets = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(packageBytes), registrationBuilder.Build()))
                .ConfigureAwait(false);
            using var sceneLease = await assets.LoadAsync<SceneAsset>(new AssetId(checked((int)sceneAssetId)))
                .ConfigureAwait(false);
            using var scenePlan = await ScenePrefabExpansionPlanner.PrepareTakingLeaseAsync(
                sceneLease.Retain(),
                SceneExpansionLimits.Default).ConfigureAwait(false);
            using var validClipLease = await assets.LoadAsync<AnimationClipAsset>(new AssetId(checked((int)validClipAssetId)))
                .ConfigureAwait(false);
            await AnimationClipAssetBinding.ValidatePackageBindingsAsync(validClipLease, scenePlan).ConfigureAwait(false);
            await AnimationClipAssetBinding.ValidatePackageBindingsAsync(validClipLease).ConfigureAwait(false);

            using var invalidSceneClipLease = await assets.LoadAsync<AnimationClipAsset>(new AssetId(checked((int)invalidSceneClipAssetId)))
                .ConfigureAwait(false);
            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await AnimationClipAssetBinding.ValidatePackageBindingsAsync(invalidSceneClipLease, scenePlan).ConfigureAwait(false));

            using var invalidAtlasClipLease = await assets.LoadAsync<AnimationClipAsset>(new AssetId(checked((int)invalidAtlasClipAssetId)))
                .ConfigureAwait(false);
            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await AnimationClipAssetBinding.ValidatePackageBindingsAsync(invalidAtlasClipLease, scenePlan).ConfigureAwait(false));

            using var mismatchedSceneClipLease = await assets.LoadAsync<AnimationClipAsset>(
                new AssetId(checked((int)mismatchedSceneClipAssetId))).ConfigureAwait(false);
            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await AnimationClipAssetBinding.ValidatePackageBindingsAsync(
                    mismatchedSceneClipLease,
                    scenePlan).ConfigureAwait(false));

            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await AnimationClipAssetBinding.ValidatePackageBindingsAsync(
                    validClipLease,
                    earlierScenePlan).ConfigureAwait(false));
        }
        finally
        {
            try
            {
                earlierScenePlan?.Dispose();
            }
            finally
            {
                try
                {
                    if (assets is { } selectedAssets)
                    {
                        await selectedAssets.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    try
                    {
                        if (initialStore is { } selectedInitialStore)
                        {
                            await selectedInitialStore.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        try
                        {
                            if (packagePath is not null)
                            {
                                File.Delete(packagePath);
                            }
                        }
                        finally
                        {
                            File.Delete(initialPackagePath);
                        }
                    }
                }
            }
        }
    }

    [Test]
    public async Task AnimationAssetBinderValidatesPackageBackedSkeletonBindings()
    {
        var schemas = CreateAnimationAssetRegistry();
        var registrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = AnimationSupport.RegisterAssetCodecs(registrationBuilder);
        const uint skeletonAssetId = 86;
        const uint validClipAssetId = 87;
        const uint unknownJointClipAssetId = 88;
        const uint mismatchedRootMotionClipAssetId = 89;
        const uint nonUnitRootScaleClipAssetId = 90;
        const uint cubicRootScaleTangentClipAssetId = 91;
        var rootJointId = new Guid("30000000-0000-4000-8000-00000000000b");
        var unknownJointId = new Guid("40000000-0000-4000-8000-00000000000c");
        var skeletonRecord = CreateSkeletonRecord(
            [CreateJointRecord(rootJointId, -1, Vector3.Zero, Vector3.One, Matrix4x4.Identity)],
            rootJointId);
        var skeletonReference = CreateOptionalRecordField(5, CreateAssetReferenceRecord(
            skeletonAssetId,
            new ContentSchemaIdentityAssignment("native/skeleton").SchemaId,
            schemaMajor: 1));
        var rootMotionField = CreateOptionalRecordField(6, CreateRootMotionRecord(rootJointId));
        var validClip = CreateAnimationClipRecord(
            [CreateTransformTrack(rootJointId)],
            [],
            skeletonReference,
            rootMotionField);
        var unknownJointClip = CreateAnimationClipRecord(
            [CreateTransformTrack(unknownJointId)],
            [],
            skeletonReference);
        var mismatchedRootMotionClip = CreateAnimationClipRecord(
            [],
            [],
            skeletonReference,
            CreateOptionalRecordField(6, CreateRootMotionRecord(unknownJointId)));
        var nonUnitRootScaleClip = CreateAnimationClipRecord(
            [CreateTransformTrack(rootJointId, channel: 2, keys: [CreateVector3Key(0, new Vector3(2.0f, 1.0f, 1.0f))])],
            [],
            skeletonReference,
            rootMotionField);
        var cubicRootScaleTangentClip = CreateAnimationClipRecord(
            [
                CreateTransformTrack(
                    rootJointId,
                    channel: 2,
                    interpolation: 2,
                    keys: [CreateVector3Key(0, Vector3.One, new Vector3(1.0f, 0.0f, 0.0f), Vector3.Zero)]),
            ],
            [],
            skeletonReference,
            rootMotionField);
        var packageBytes = Package.CreatePackage(
            new Package.Entry(skeletonAssetId, Package.CreateEnvelope(skeletonAssetId, skeletonRecord)),
            new Package.Entry(validClipAssetId, Package.CreateEnvelope(validClipAssetId, EbinPrimitives.Encode(validClip))),
            new Package.Entry(unknownJointClipAssetId, Package.CreateEnvelope(unknownJointClipAssetId, EbinPrimitives.Encode(unknownJointClip))),
            new Package.Entry(mismatchedRootMotionClipAssetId, Package.CreateEnvelope(mismatchedRootMotionClipAssetId, EbinPrimitives.Encode(mismatchedRootMotionClip))),
            new Package.Entry(nonUnitRootScaleClipAssetId, Package.CreateEnvelope(nonUnitRootScaleClipAssetId, EbinPrimitives.Encode(nonUnitRootScaleClip))),
            new Package.Entry(cubicRootScaleTangentClipAssetId, Package.CreateEnvelope(cubicRootScaleTangentClipAssetId, EbinPrimitives.Encode(cubicRootScaleTangentClip))));
        var packagePath = Package.WritePackage(packageBytes);
        AssetStore? assets = null;

        try
        {
            assets = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(packageBytes), registrationBuilder.Build()))
                .ConfigureAwait(false);
            using var clipLease = await assets.LoadAsync<AnimationClipAsset>(new AssetId(checked((int)validClipAssetId)))
                .ConfigureAwait(false);
            using var skeletonLease = await AnimationClipAssetBinding.LoadSkeletonAsync(clipLease).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The selected Clip should retain its Skeleton binding.");

            Assert.That(skeletonLease.Value.Joints[0].JointId, Is.EqualTo(rootJointId));
            Assert.That(skeletonLease.Value.RootMotionJointId, Is.EqualTo(rootJointId));

            foreach (var invalidClipAssetId in new[]
                {
                    unknownJointClipAssetId,
                    mismatchedRootMotionClipAssetId,
                    nonUnitRootScaleClipAssetId,
                    cubicRootScaleTangentClipAssetId,
                })
            {
                using var invalidClipLease = await assets.LoadAsync<AnimationClipAsset>(new AssetId(checked((int)invalidClipAssetId)))
                    .ConfigureAwait(false);
                _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                    await AnimationClipAssetBinding.LoadSkeletonAsync(invalidClipLease).ConfigureAwait(false));
            }
        }
        finally
        {
            try
            {
                if (assets is { } selectedAssets)
                {
                    await selectedAssets.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                File.Delete(packagePath);
            }
        }
    }

    [Test]
    public async Task AnimationAssetCodecRejectsUnorderedTracksEventsAndRequiredOptionalReference()
    {
        var schemas = CreateAnimationAssetRegistry();
        var registrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = AnimationSupport.RegisterAssetCodecs(registrationBuilder);
        const uint skeletonAssetId = 80;
        const uint unorderedTracksAssetId = 81;
        const uint unorderedEventsAssetId = 82;
        const uint requiredReferenceAssetId = 83;
        const uint unsupportedVersionAssetId = 84;
        var firstJointId = new Guid("30000000-0000-4000-8000-000000000008");
        var secondJointId = new Guid("40000000-0000-4000-8000-000000000009");
        var skeletonRecord = CreateSkeletonRecord(firstJointId, parentIndex: -1, Matrix4x4.Identity);
        var skeletonReference = CreateOptionalRecordField(5, CreateAssetReferenceRecord(
            skeletonAssetId,
            new ContentSchemaIdentityAssignment("native/skeleton").SchemaId,
            schemaMajor: 1));
        var unorderedTracks = CreateAnimationClipRecord(
            [CreateTransformTrack(secondJointId), CreateTransformTrack(firstJointId)],
            [],
            skeletonReference);
        var unorderedEvents = CreateAnimationClipRecord(
            [],
            [
                CreateClipEvent(new Guid("50000000-0000-4000-8000-00000000000b"), 20),
                CreateClipEvent(new Guid("50000000-0000-4000-8000-00000000000c"), 10),
            ]);
        var requiredOptionalReference = CreateAnimationClipRecord(
            [CreateTransformTrack(firstJointId)],
            [],
            CreateOptionalRecordField(5, CreateAssetReferenceRecord(
                skeletonAssetId,
                new ContentSchemaIdentityAssignment("native/skeleton").SchemaId,
                schemaMajor: 1),
                flags: 1));
        var unsupportedVersion = CreateAnimationClipRecord([], [], schemaMinor: 1);
        var packageBytes = Package.CreatePackage(
            new Package.Entry(skeletonAssetId, Package.CreateEnvelope(skeletonAssetId, skeletonRecord)),
            new Package.Entry(unorderedTracksAssetId, Package.CreateEnvelope(unorderedTracksAssetId, EbinPrimitives.Encode(unorderedTracks))),
            new Package.Entry(unorderedEventsAssetId, Package.CreateEnvelope(unorderedEventsAssetId, EbinPrimitives.Encode(unorderedEvents))),
            new Package.Entry(requiredReferenceAssetId, Package.CreateEnvelope(requiredReferenceAssetId, EbinPrimitives.Encode(requiredOptionalReference))),
            new Package.Entry(unsupportedVersionAssetId, Package.CreateEnvelope(unsupportedVersionAssetId, EbinPrimitives.Encode(unsupportedVersion))));
        var packagePath = Package.WritePackage(packageBytes);
        AssetStore? assets = null;

        try
        {
            assets = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(packageBytes), registrationBuilder.Build()))
                .ConfigureAwait(false);

            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await assets.LoadAsync<AnimationClipAsset>(new AssetId(checked((int)unorderedTracksAssetId))).ConfigureAwait(false));
            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await assets.LoadAsync<AnimationClipAsset>(new AssetId(checked((int)unorderedEventsAssetId))).ConfigureAwait(false));
            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await assets.LoadAsync<AnimationClipAsset>(new AssetId(checked((int)requiredReferenceAssetId))).ConfigureAwait(false));
            _ = Assert.ThrowsAsync<NotSupportedException>(async () =>
                await assets.LoadAsync<AnimationClipAsset>(new AssetId(checked((int)unsupportedVersionAssetId))).ConfigureAwait(false));
        }
        finally
        {
            try
            {
                if (assets is { } selectedAssets)
                {
                    await selectedAssets.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                File.Delete(packagePath);
            }
        }
    }

    [Test]
    public async Task AnimationClipCodecLoadsOrderedJointTracksThroughTypedPackageRoot()
    {
        var schemas = CreateAnimationAssetRegistry();
        var registrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = AnimationSupport.RegisterAssetCodecs(registrationBuilder);
        const uint skeletonAssetId = 73;
        const uint clipAssetId = 74;
        var jointId = new Guid("30000000-0000-4000-8000-000000000003");
        var skeletonRecord = CreateSkeletonRecord(jointId, parentIndex: -1, Matrix4x4.Identity);
        var firstKey = new EbinRecord(
            new ContentSchemaIdentityAssignment("native/key-vector3").SchemaId,
            1,
            0,
            [
                EbinPrimitives.Int64(1, 0),
                    EbinPrimitives.Geometry(2, Vector3.Zero),
            ]);
        var lastKey = new EbinRecord(
            new ContentSchemaIdentityAssignment("native/key-vector3").SchemaId,
            1,
            0,
            [
                EbinPrimitives.Int64(1, 100),
                    EbinPrimitives.Geometry(2, new Vector3(1.0f, 0.0f, 0.0f)),
            ]);
        var track = new EbinRecord(
            new ContentSchemaIdentityAssignment("native/transform-track").SchemaId,
            1,
            0,
            [
                EbinPrimitives.Guid(1, jointId),
                    EbinPrimitives.UInt8(2, 1),
                    EbinPrimitives.UInt8(3, 0),
                    EbinPrimitives.UInt8(4, 1),
                    EbinPrimitives.RecordArray(5, [firstKey, lastKey]),
            ]);
        var clipRecord = EbinPrimitives.Encode(new EbinRecord(
            new ContentSchemaIdentityAssignment("native/clip").SchemaId,
            1,
            0,
            [
                EbinPrimitives.Int64(1, 100),
                    EbinPrimitives.RecordArray(2, [track]),
                    EbinPrimitives.RecordArray(3, []),
                    EbinPrimitives.UInt8(4, 0),
                    CreateOptionalRecordField(5, CreateAssetReferenceRecord(
                        skeletonAssetId,
                        new ContentSchemaIdentityAssignment("native/skeleton").SchemaId,
                        schemaMajor: 1)),
                    EbinPrimitives.UInt8(8, 0),
                    EbinPrimitives.Binary32(9, 0.0f),
            ]));
        var packageBytes = Package.CreatePackage(
            new Package.Entry(
                skeletonAssetId,
                Package.CreateEnvelope(skeletonAssetId, skeletonRecord)),
            new Package.Entry(
                clipAssetId,
                Package.CreateEnvelope(clipAssetId, clipRecord)));
        var packagePath = Package.WritePackage(packageBytes);
        AssetStore? assets = null;

        try
        {
            assets = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(packageBytes), registrationBuilder.Build()))
                .ConfigureAwait(false);
            using var clipLease = await assets.LoadAsync<AnimationClipAsset>(new AssetId(checked((int)clipAssetId)))
                .ConfigureAwait(false);

            Assert.That(clipLease.Value.DurationTicks, Is.EqualTo(100));
            Assert.That(clipLease.Value.SkeletonAssetId, Is.EqualTo(new AssetId(checked((int)skeletonAssetId))));
            Assert.That(clipLease.Value.SceneAssetId, Is.Null);
            Assert.That(clipLease.Value.Tracks, Has.Length.EqualTo(1));
            Assert.That(clipLease.Value.Tracks[0].TargetId, Is.EqualTo(jointId));
            Assert.That(clipLease.Value.Tracks[0].TargetDomain, Is.EqualTo(1));
            Assert.That(clipLease.Value.Tracks[0].Channel, Is.Zero);
            Assert.That(clipLease.Value.Tracks[0].Vector3Keys, Has.Length.EqualTo(2));
            Assert.That(clipLease.Value.Tracks[0].Vector3Keys[1].Value, Is.EqualTo(new Vector3(1.0f, 0.0f, 0.0f)));
        }
        finally
        {
            try
            {
                if (assets is { } selectedAssets)
                {
                    await selectedAssets.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                File.Delete(packagePath);
            }
        }
    }

    [Test]
    public async Task AnimationClipCodecLoadsScaleAndRejectsInvalidInterpolatedValue()
    {
        var schemas = CreateAnimationAssetRegistry();
        var registrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = SceneSupport.RegisterAssetCodecs(registrationBuilder);
        _ = AnimationSupport.RegisterAssetCodecs(registrationBuilder);
        const uint clipAssetId = 92;
        const uint sceneAssetId = 93;
        var targetId = new Guid("30000000-0000-4000-8000-000000000092");
        var clipRecord = EbinPrimitives.Encode(CreateAnimationClipRecord(
            [
                CreateTransformTrack(
                    targetId,
                    channel: 2,
                    interpolation: 2,
                    keys:
                    [
                        CreateVector3Key(
                            0,
                            Vector3.One,
                            inTangent: Vector3.Zero,
                            outTangent: new Vector3(-4.0f, 0.0f, 0.0f)),
                        CreateVector3Key(
                            TimeSpan.TicksPerSecond,
                            Vector3.One,
                            inTangent: new Vector3(4.0f, 0.0f, 0.0f),
                            outTangent: Vector3.Zero),
                    ],
                    targetDomain: 0),
            ],
            [],
            sceneReference: CreateSceneReference(sceneAssetId),
            durationTicks: TimeSpan.TicksPerSecond));
        var sceneRecord = SceneAssetReaderTests.Fixtures.CreateScene(new AssetId(checked((int)sceneAssetId)));
        var packageBytes = Package.CreatePackage(
            new Package.Entry(clipAssetId, Package.CreateEnvelope(clipAssetId, clipRecord)),
            new Package.Entry(sceneAssetId, Package.CreateEnvelope(sceneAssetId, sceneRecord)));
        var packagePath = Package.WritePackage(packageBytes);
        AssetStore? assets = null;

        try
        {
            assets = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(packageBytes), registrationBuilder.Build()))
                .ConfigureAwait(false);
            using var clipLease = await assets.LoadAsync<AnimationClipAsset>(new AssetId(checked((int)clipAssetId)))
                .ConfigureAwait(false);

            Assert.That(clipLease.Value.Tracks, Has.Length.EqualTo(1));
            Assert.That(clipLease.Value.Tracks[0].Vector3Keys, Has.Length.EqualTo(2));
            _ = Assert.Throws<InvalidDataException>(() => AnimationClipSamplingKernels.SampleVector3(
                clipLease.Value,
                clipLease.Value.Tracks[0],
                TimeSpan.TicksPerSecond / 2));
        }
        finally
        {
            try
            {
                if (assets is { } selectedAssets)
                {
                    await selectedAssets.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                File.Delete(packagePath);
            }
        }
    }

    [Test]
    public async Task AnimationClipCodecRejectsCubicKeysWithoutBothTangents()
    {
        var schemas = CreateAnimationAssetRegistry();
        var registrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = AnimationSupport.RegisterAssetCodecs(registrationBuilder);
        const uint skeletonAssetId = 75;
        const uint clipAssetId = 76;
        var jointId = new Guid("30000000-0000-4000-8000-000000000004");
        var skeletonRecord = CreateSkeletonRecord(jointId, parentIndex: -1, Matrix4x4.Identity);
        var key = new EbinRecord(
            new ContentSchemaIdentityAssignment("native/key-vector3").SchemaId,
            1,
            0,
            [
                EbinPrimitives.Int64(1, 0),
                    EbinPrimitives.Geometry(2, Vector3.Zero),
            ]);
        var track = new EbinRecord(
            new ContentSchemaIdentityAssignment("native/transform-track").SchemaId,
            1,
            0,
            [
                EbinPrimitives.Guid(1, jointId),
                    EbinPrimitives.UInt8(2, 1),
                    EbinPrimitives.UInt8(3, 0),
                    EbinPrimitives.UInt8(4, 2),
                    EbinPrimitives.RecordArray(5, [key]),
            ]);
        var clipRecord = EbinPrimitives.Encode(new EbinRecord(
            new ContentSchemaIdentityAssignment("native/clip").SchemaId,
            1,
            0,
            [
                EbinPrimitives.Int64(1, 100),
                    EbinPrimitives.RecordArray(2, [track]),
                    EbinPrimitives.RecordArray(3, []),
                    EbinPrimitives.UInt8(4, 0),
                    CreateOptionalRecordField(5, CreateAssetReferenceRecord(
                        skeletonAssetId,
                        new ContentSchemaIdentityAssignment("native/skeleton").SchemaId,
                        schemaMajor: 1)),
                    EbinPrimitives.UInt8(8, 0),
                    EbinPrimitives.Binary32(9, 0.0f),
            ]));
        var packageBytes = Package.CreatePackage(
            new Package.Entry(
                skeletonAssetId,
                Package.CreateEnvelope(skeletonAssetId, skeletonRecord)),
            new Package.Entry(
                clipAssetId,
                Package.CreateEnvelope(clipAssetId, clipRecord)));
        var packagePath = Package.WritePackage(packageBytes);
        AssetStore? assets = null;

        try
        {
            assets = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(packageBytes), registrationBuilder.Build()))
                .ConfigureAwait(false);
            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await assets.LoadAsync<AnimationClipAsset>(new AssetId(checked((int)clipAssetId))).ConfigureAwait(false));
        }
        finally
        {
            try
            {
                if (assets is { } selectedAssets)
                {
                    await selectedAssets.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                File.Delete(packagePath);
            }
        }
    }

    [Test]
    public async Task SkeletonAssetCodecLoadsBindPoseThroughTypedPackageRoot()
    {
        var schemas = CreateAnimationAssetRegistry();
        var registrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = AnimationSupport.RegisterAssetCodecs(registrationBuilder);
        const uint skeletonAssetId = 71;
        var jointId = new Guid("30000000-0000-4000-8000-000000000001");
        var skeletonRecord = CreateSkeletonRecord(jointId, parentIndex: -1, Matrix4x4.Identity);
        var packageBytes = Package.CreatePackage(new Package.Entry(
            skeletonAssetId,
            Package.CreateEnvelope(skeletonAssetId, skeletonRecord)));
        var packagePath = Package.WritePackage(packageBytes);

        AssetStore? assets = null;

        try
        {
            assets = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(packageBytes), registrationBuilder.Build()))
                .ConfigureAwait(false);
            using var skeletonLease = await assets.LoadAsync<SkeletonAsset>(new AssetId(checked((int)skeletonAssetId)))
                .ConfigureAwait(false);

            Assert.That(skeletonLease.AssetId, Is.EqualTo(new AssetId(checked((int)skeletonAssetId))));
            Assert.That(skeletonLease.Value.Joints, Has.Length.EqualTo(1));
            Assert.That(skeletonLease.Value.Joints[0].JointId, Is.EqualTo(jointId));
            Assert.That(skeletonLease.Value.Joints[0].ParentIndex, Is.EqualTo(-1));
            Assert.That(skeletonLease.Value.Joints[0].BindRotation, Is.EqualTo(Quaternion.Identity));
            Assert.That(skeletonLease.Value.Joints[0].InverseBind, Is.EqualTo(Matrix4x4.Identity));
        }
        finally
        {
            try
            {
                if (assets is { } selectedAssets)
                {
                    await selectedAssets.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                File.Delete(packagePath);
            }
        }
    }

    [Test]
    public async Task SkeletonAssetCodecRejectsNonInvertibleBindPose()
    {
        var schemas = CreateAnimationAssetRegistry();
        var registrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = AnimationSupport.RegisterAssetCodecs(registrationBuilder);
        const uint skeletonAssetId = 72;
        var jointId = new Guid("30000000-0000-4000-8000-000000000002");
        var skeletonRecord = CreateSkeletonRecord(
            jointId,
            parentIndex: -1,
            Matrix4x4.CreateTranslation(new Vector3(1.0f, 0.0f, 0.0f)));
        var packageBytes = Package.CreatePackage(new Package.Entry(
            skeletonAssetId,
            Package.CreateEnvelope(skeletonAssetId, skeletonRecord)));
        var packagePath = Package.WritePackage(packageBytes);

        AssetStore? assets = null;

        try
        {
            assets = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(packageBytes), registrationBuilder.Build()))
                .ConfigureAwait(false);

            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await assets.LoadAsync<SkeletonAsset>(new AssetId(checked((int)skeletonAssetId))).ConfigureAwait(false));
        }
        finally
        {
            try
            {
                if (assets is { } selectedAssets)
                {
                    await selectedAssets.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                File.Delete(packagePath);
            }
        }
    }

    [Test]
    public async Task SkeletonAssetCodecValidatesComposedParentAndChildBindTransforms()
    {
        var schemas = CreateAnimationAssetRegistry();
        var registrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = AnimationSupport.RegisterAssetCodecs(registrationBuilder);
        const uint skeletonAssetId = 78;
        var rootJointId = new Guid("30000000-0000-4000-8000-000000000005");
        var childJointId = new Guid("30000000-0000-4000-8000-000000000006");
        var skeletonRecord = CreateSkeletonRecord(
        [
            CreateJointRecord(rootJointId, -1, new Vector3(1.0f, 0.0f, 0.0f), Vector3.One, Matrix4x4.CreateTranslation(new Vector3(-1.0f, 0.0f, 0.0f))),
                CreateJointRecord(childJointId, 0, new Vector3(0.0f, 2.0f, 0.0f), Vector3.One, Matrix4x4.CreateTranslation(new Vector3(-1.0f, -2.0f, 0.0f))),
            ]);
        var packageBytes = Package.CreatePackage(new Package.Entry(
            skeletonAssetId,
            Package.CreateEnvelope(skeletonAssetId, skeletonRecord)));
        var packagePath = Package.WritePackage(packageBytes);
        AssetStore? assets = null;

        try
        {
            assets = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(packageBytes), registrationBuilder.Build()))
                .ConfigureAwait(false);
            using var skeletonLease = await assets.LoadAsync<SkeletonAsset>(new AssetId(checked((int)skeletonAssetId)))
                .ConfigureAwait(false);

            Assert.That(skeletonLease.Value.Joints, Has.Length.EqualTo(2));
            Assert.That(skeletonLease.Value.Joints[1].ParentIndex, Is.Zero);
            Assert.That(skeletonLease.Value.Joints[1].InverseBind, Is.EqualTo(Matrix4x4.CreateTranslation(new Vector3(-1.0f, -2.0f, 0.0f))));
        }
        finally
        {
            try
            {
                if (assets is { } selectedAssets)
                {
                    await selectedAssets.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                File.Delete(packagePath);
            }
        }
    }

    [Test]
    public async Task SkeletonAssetCodecValidatesRootMotionJointBindScale()
    {
        var schemas = CreateAnimationAssetRegistry();
        var registrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = AnimationSupport.RegisterAssetCodecs(registrationBuilder);
        const uint skeletonAssetId = 79;
        var jointId = new Guid("30000000-0000-4000-8000-000000000007");
        var skeletonRecord = CreateSkeletonRecord(
        [
            CreateJointRecord(jointId, -1, Vector3.Zero, new Vector3(2.0f, 1.0f, 1.0f), Matrix4x4.CreateScale(new Vector3(0.5f, 1.0f, 1.0f))),
            ],
        jointId);
        var packageBytes = Package.CreatePackage(new Package.Entry(
            skeletonAssetId,
            Package.CreateEnvelope(skeletonAssetId, skeletonRecord)));
        var packagePath = Package.WritePackage(packageBytes);
        AssetStore? assets = null;

        try
        {
            assets = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(packageBytes), registrationBuilder.Build()))
                .ConfigureAwait(false);
            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await assets.LoadAsync<SkeletonAsset>(new AssetId(checked((int)skeletonAssetId))).ConfigureAwait(false));
        }
        finally
        {
            try
            {
                if (assets is { } selectedAssets)
                {
                    await selectedAssets.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                File.Delete(packagePath);
            }
        }
    }
}
