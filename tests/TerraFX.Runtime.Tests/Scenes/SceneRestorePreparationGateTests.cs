// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Rendering;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

internal sealed partial class SceneAssetStoreTests
{
    [TestCase(0)]
    [TestCase(1)]
    public void RestoreHierarchyReservationMatchesItsDataAndReceiverControl(int nodeCount)
    {
        var dataBytes = ScenePersistenceRestorer.MeasureHierarchyRestoreWorkspaceBytes(nodeCount);
        var expectedReservationBytes = (dataBytes == 0)
            ? 0
            : WorldSceneStorageReservation.GetRequiredBytes(dataBytes);

        Assert.That(
            ScenePersistenceRestorer.MeasureHierarchyRestoreReservationBytes(nodeCount),
            Is.EqualTo(expectedReservationBytes));
    }

    [Test]
    public async Task RestorePlanWithCpuExtractionRefusesBeforePackageExpansion()
    {
        var schemas = CreateRendererRegistry();
        var assetRegistrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = SceneSupport.RegisterAssetCodecs(assetRegistrationBuilder);
        _ = Renderer.RegisterAssetCodecs(assetRegistrationBuilder);
        var assetRegistration = assetRegistrationBuilder.Build();
        var package = Package.CreatePackage(
            new Package.Entry(1, Package.CreateEnvelope(1, CreateRenderAtlasPageTextureRecord(17))));
        var packagePath = Package.WritePackage(package);
        var assets = await AssetStore.CreateAsync(
            new AssetStoreOptions(packagePath, SHA256.HashData(package), assetRegistration)).ConfigureAwait(false);

        try
        {
            var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
            _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
            _ = worldRegistrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration());
            using var world = World.Create(new WorldOptions(worldRegistrationBuilder.Build()));
            using var packageLease = assets.AcquirePackageLease();
            var loader = new SceneLoader(world, assets);
            var options = new SceneLoadOptions().ValidateAndFreeze();

            _ = Assert.ThrowsAsync<NotSupportedException>(
                async () => await loader.PrepareRestorePlanAsync(
                    packageLease,
                    new AssetId(1),
                    options,
                    CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await assets.DisposeAsync().ConfigureAwait(false);
            File.Delete(packagePath);
        }
    }
}
