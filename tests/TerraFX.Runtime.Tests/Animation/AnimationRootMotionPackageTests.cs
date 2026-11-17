// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX.Animation;
using TerraFX.Assets;
using TerraFX.Numerics;
using TerraFX.Rendering;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

internal sealed partial class SceneAssetStoreTests
{
    [Test]
    public async Task PackageBackedRootMotionKernelUsesTheRetainedClipAndSkeleton()
    {
        var schemas = CreateAnimationAssetRegistry();
        var registrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = SceneSupport.RegisterAssetCodecs(registrationBuilder);
        _ = Renderer.RegisterAssetCodecs(registrationBuilder);
        _ = AnimationSupport.RegisterAssetCodecs(registrationBuilder);
        const uint skeletonAssetId = 1;
        const uint clipAssetId = 2;
        var rootJointId = new Guid("30000000-0000-4000-8000-000000000022");
        var skeletonRecord = CreateSkeletonRecord(
            [CreateJointRecord(rootJointId, parentIndex: -1, Vector3.Zero, Vector3.One, Matrix4x4.Identity)],
            rootJointId);
        var clipRecord = EbinPrimitives.Encode(CreateAnimationClipRecord(
            [
                CreateTransformTrack(
                    rootJointId,
                    channel: 0,
                    interpolation: 1,
                    keys:
                    [
                        CreateVector3Key(0, Vector3.Zero),
                        CreateVector3Key(100, Vector3.UnitX),
                    ]),
            ],
            [],
            CreateOptionalRecordField(
                5,
                CreateAssetReferenceRecord(
                    skeletonAssetId,
                    new ContentSchemaIdentityAssignment("native/skeleton").SchemaId,
                    schemaMajor: 1)),
            CreateOptionalRecordField(6, CreateRootMotionRecord(rootJointId))));
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
            using var skeletonLease = await AnimationClipAssetBinding.LoadSkeletonAsync(clipLease)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("The root-motion Clip omitted its selected Skeleton.");

            var delta = AnimationRootMotionKernels.CalculateDelta(
                clipLease.Value,
                skeletonLease.Value,
                firstLoopOrdinal: 0,
                firstTimeTicks: 0,
                secondLoopOrdinal: 0,
                secondTimeTicks: 100);

            Assert.Multiple(() => {
                Assert.That(delta.Row4.X, Is.EqualTo(1.0f).Within(1.0e-6f));
                Assert.That(delta.Row4.Y, Is.Zero);
                Assert.That(delta.Row4.Z, Is.Zero);
            });
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
                System.IO.File.Delete(packagePath);
            }
        }
    }
}
