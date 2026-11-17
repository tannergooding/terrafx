// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Animation;
using TerraFX.Assets;
using TerraFX.Numerics;

namespace TerraFX.Runtime.Tests.Scenes;

internal sealed partial class SceneAssetStoreTests
{
    private static EbinRecord CreateAnimationControllerRecord(
        uint clipAssetId,
        uint skeletonAssetId,
        Guid clipSlotId,
        byte rootMotionMode,
        Guid? motionActorKey)
    {
        var clipSlot = new EbinRecord(
            new ContentSchemaIdentityAssignment("native/animation-clip-slot").SchemaId,
            1,
            0,
            [
                EbinPrimitives.Guid(1, clipSlotId),
                EbinPrimitives.Record(2, CreateAssetReferenceRecord(
                    clipAssetId,
                    new ContentSchemaIdentityAssignment("native/clip").SchemaId,
                    schemaMajor: 1)),
            ]);
        var clipWeight = new EbinRecord(
            new ContentSchemaIdentityAssignment("native/animation-clip-weight").SchemaId,
            1,
            0,
            [
                EbinPrimitives.Guid(1, clipSlotId),
                EbinPrimitives.Binary32(2, 0.0f),
            ]);
        var layer = new EbinRecord(
            new ContentSchemaIdentityAssignment("native/animation-layer").SchemaId,
            1,
            0,
            [
                EbinPrimitives.UInt8(1, 0),
                EbinPrimitives.UInt8(2, 0),
                EbinPrimitives.Binary32(3, 1.0f),
                CreateFloatFixedArray(4, 1.0f),
                EbinPrimitives.RecordArray(5, []),
                EbinPrimitives.RecordArray(6, [clipWeight]),
                EbinPrimitives.UInt8(7, 0),
            ]);
        var condition = new EbinRecord(
            new ContentSchemaIdentityAssignment("native/animation-condition-manual").SchemaId,
            1,
            0,
            []);
        var action = new EbinRecord(
            new ContentSchemaIdentityAssignment("native/animation-action-layer-weight").SchemaId,
            1,
            0,
            [
                EbinPrimitives.UInt8(1, 0),
                EbinPrimitives.Binary32(2, 0.5f),
            ]);
        var transition = new EbinRecord(
            new ContentSchemaIdentityAssignment("native/animation-transition").SchemaId,
            1,
            0,
            [
                EbinPrimitives.Guid(1, new Guid("50000000-0000-4000-8000-000000000003")),
                EbinPrimitives.UInt8(2, 0),
                EbinPrimitives.Guid(4, clipSlotId),
                EbinPrimitives.Int64(5, 2_000_000),
                EbinPrimitives.Int32(6, -3),
                EbinPrimitives.Record(7, condition),
                EbinPrimitives.UInt8(8, 0),
                EbinPrimitives.Record(9, action),
                EbinPrimitives.UInt8(10, 1),
            ]);
        var fields = new System.Collections.Generic.List<EbinField> {
            CreateOptionalRecordField(
                1,
                CreateAssetReferenceRecord(
                    skeletonAssetId,
                    new ContentSchemaIdentityAssignment("native/skeleton").SchemaId,
                    schemaMajor: 1)),
            EbinPrimitives.RecordArray(3, [clipSlot]),
            EbinPrimitives.RecordArray(4, [layer]),
            EbinPrimitives.RecordArray(5, [transition]),
            EbinPrimitives.UInt8(6, rootMotionMode),
        };

        if (motionActorKey is Guid selectedActorKey)
        {
            fields.Add(EbinPrimitives.Guid(7, selectedActorKey));
        }

        return new EbinRecord(
            new ContentSchemaIdentityAssignment("native/animation-controller").SchemaId,
            1,
            0,
            [.. fields]);
    }

    private static EbinField CreateFloatFixedArray(uint tag, params float[] values)
    {
        var elements = new byte[checked(values.Length * sizeof(float))];

        for (var index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                elements.AsSpan(index * sizeof(float)),
                BitConverter.SingleToInt32Bits(values[index]));
        }

        Span<byte> destination = stackalloc byte[128];
        var status = EbinPrimitives.WriteFixedArray(
            elementKind: 10,
            count: checked((ulong)values.Length),
            elements,
            destination,
            out var written,
            out var diagnostic);

        if (status is not EbinStatus.Complete)
        {
            throw new InvalidOperationException($"The test FixedArray could not be encoded: {diagnostic.Category}.");
        }

        return new EbinField(tag, 23, flags: 1, count: checked((ulong)values.Length), destination[..written]);
    }

    [Test]
    public async Task AnimationControllerAssetCodecLoadsTypedDefinitionAndRejectsMismatchedMotionModeAsync()
    {
        var schemas = CreateAnimationAssetRegistry();
        var registrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = AnimationSupport.RegisterAssetCodecs(registrationBuilder);
        _ = AnimationSupport.RegisterPlaybackAssetCodecs(registrationBuilder);
        const uint controllerAssetId = 95;
        const uint clipAssetId = 96;
        const uint skeletonAssetId = 97;
        const uint invalidControllerAssetId = 98;
        var clipSlotId = new Guid("50000000-0000-4000-8000-000000000001");
        var rootJointId = new Guid("50000000-0000-4000-8000-000000000004");
        var validController = CreateAnimationControllerRecord(
            clipAssetId,
            skeletonAssetId,
            clipSlotId,
            rootMotionMode: 0,
            motionActorKey: null);
        var invalidController = CreateAnimationControllerRecord(
            clipAssetId,
            skeletonAssetId,
            new Guid("50000000-0000-4000-8000-000000000002"),
            rootMotionMode: 1,
            motionActorKey: null);
        var skeletonRecord = CreateSkeletonRecord(
            rootJointId,
            parentIndex: -1,
            Matrix4x4.Identity);
        var skeletonReference = CreateOptionalRecordField(
            5,
            CreateAssetReferenceRecord(
                skeletonAssetId,
                new ContentSchemaIdentityAssignment("native/skeleton").SchemaId,
                schemaMajor: 1));
        var clipRecord = CreateAnimationClipRecord(
            [CreateTransformTrack(rootJointId, targetDomain: 1)],
            [],
            skeletonReference: skeletonReference);
        var packageBytes = Package.CreatePackage(
            new Package.Entry(
                controllerAssetId,
                Package.CreateEnvelope(controllerAssetId, EbinPrimitives.Encode(validController))),
            new Package.Entry(
                clipAssetId,
                Package.CreateEnvelope(clipAssetId, EbinPrimitives.Encode(clipRecord))),
            new Package.Entry(
                skeletonAssetId,
                Package.CreateEnvelope(skeletonAssetId, skeletonRecord)),
            new Package.Entry(
                invalidControllerAssetId,
                Package.CreateEnvelope(invalidControllerAssetId, EbinPrimitives.Encode(invalidController))));
        var packagePath = Package.WritePackage(packageBytes);
        AssetStore? assets = null;

        try
        {
            assets = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(packageBytes), registrationBuilder.Build()))
                .ConfigureAwait(false);
            using var controllerLease = await assets.LoadAsync<AnimationControllerAsset>(new AssetId(checked((int)controllerAssetId)))
                .ConfigureAwait(false);
            var controller = controllerLease.Value;

            Assert.Multiple(() => {
                Assert.That(controller.AssetId, Is.EqualTo(new AssetId(checked((int)controllerAssetId))));
                Assert.That(controller.SkeletonAssetId, Is.EqualTo(new AssetId(checked((int)skeletonAssetId))));
                Assert.That(controller.SceneAssetId, Is.Null);
                Assert.That(controller.RootMotionMode, Is.Zero);
                Assert.That(controller.MotionActorKey, Is.Null);
                Assert.That(controller.ClipSlots, Has.Length.EqualTo(1));
                Assert.That(controller.ClipSlots[0].ClipSlotId, Is.EqualTo(clipSlotId));
                Assert.That(controller.ClipSlots[0].ClipAssetId, Is.EqualTo(new AssetId(checked((int)clipAssetId))));
                Assert.That(controller.Layers, Has.Length.EqualTo(1));
                Assert.That(controller.Layers[0].Index, Is.Zero);
                Assert.That(controller.Layers[0].Mask, Is.EqualTo([1.0f]));
                Assert.That(controller.Layers[0].ReferencePose, Is.Empty);
                Assert.That(controller.Layers[0].InitialClips, Has.Length.EqualTo(1));
                Assert.That(controller.Layers[0].InitialClips[0].Weight, Is.Zero);
                Assert.That(controller.Transitions, Has.Length.EqualTo(1));
                Assert.That(controller.Transitions[0].TransitionId, Is.EqualTo(new Guid("50000000-0000-4000-8000-000000000003")));
                Assert.That(controller.Transitions[0].SourceClipSlotId, Is.Null);
                Assert.That(controller.Transitions[0].TargetClipSlotId, Is.EqualTo(clipSlotId));
                Assert.That(controller.Transitions[0].Priority, Is.EqualTo(-3));
                Assert.That(controller.Transitions[0].Condition.Kind, Is.Zero);
                Assert.That(controller.Transitions[0].Action.Kind, Is.EqualTo(1));
                Assert.That(controller.Transitions[0].Action.LayerIndex, Is.EqualTo((byte)0));
                Assert.That(controller.Transitions[0].Action.Weight, Is.EqualTo(0.5f));
            });

            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await assets.LoadAsync<AnimationControllerAsset>(new AssetId(checked((int)invalidControllerAssetId))).ConfigureAwait(false));
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
