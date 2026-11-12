// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Rendering;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

internal sealed partial class SceneAssetStoreTests
{
    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The owner boundary completes activation and unload requests before the prepared assets are released.")]
    public void PreparedAssetLeaseRetainsBundleAcrossActivationUnloadAndDispose()
    {
        var schemas = CreateRendererRegistry();
        var frameId = new Guid("30000000-0000-4000-8000-000000000001");
        var textureRecord = CreateRenderAtlasPageTextureRecord(17);
        var atlasRecord = CreateRenderAtlasRecord(
            1,
            CreateRenderAtlasFrameRecord(frameId, page: 0, x: 4, y: 4, width: 4, height: 4));
        var sceneRecord = SceneAssetReaderTests.Fixtures.CreateScene(
            new AssetId(3),
            additionalEntityComponents: [CreateRenderSpriteRecord(2, frameId, materialAssetId: null)]);
        Package.Entry[] packageEntries = [
            new(1, Package.CreateEnvelope(1, textureRecord)),
            new(2, Package.CreateEnvelope(2, atlasRecord)),
            new(3, Package.CreateEnvelope(3, sceneRecord)),
        ];
        var package = Package.CreatePackage(packageEntries);
        var packagePath = Package.WritePackage(package);
        var assetRegistrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = SceneSupport.RegisterAssetCodecs(assetRegistrationBuilder);
        _ = Renderer.RegisterAssetCodecs(assetRegistrationBuilder);

        AssetStore? assets = null;

        try
        {
            assets = AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), assetRegistrationBuilder.Build()))
                .GetAwaiter()
                .GetResult();
            var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
            _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
            _ = worldRegistrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration());
            using var world = World.Create(new WorldOptions(worldRegistrationBuilder.Build()));
            var loader = new SceneLoader(world, assets);

            using var prepared = loader.PrepareAsync(new AssetId(3), new SceneLoadOptions()).GetAwaiter().GetResult();
            using var retainedAssets = prepared.AcquirePreparedAssets()
                ?? throw new InvalidOperationException("The prepared Sprite Scene omitted its asset bundle.");
            var bundle = retainedAssets.Value as RenderSceneAssetBundle
                ?? throw new InvalidOperationException("The prepared Sprite Scene retained an unexpected asset type.");

            var activation = loader.ActivateAsync(prepared);
            world.PumpControlBoundary();
            var activationReceipt = activation.GetAwaiter().GetResult();
            Assert.That(activationReceipt.Status, Is.EqualTo(SceneActivationStatus.Activated));
            var instance = activationReceipt.Instance
                ?? throw new InvalidOperationException("The successful Scene activation omitted its instance.");
            Assert.That(
                () => prepared.AcquirePreparedAssets(),
                Throws.TypeOf<InvalidOperationException>());

            var unload = instance.UnloadAsync(HierarchyDestroyMode.DestroySubtree);
            world.PumpControlBoundary();
            Assert.That(unload.GetAwaiter().GetResult().Status, Is.EqualTo(SceneUnloadStatus.Unloaded));
            Assert.That(
                bundle.GetAtlasPageSet(2).GetPage(0).Mips[0].Pixels[0],
                Is.EqualTo(17));
            retainedAssets.Dispose();
            Assert.That(() => bundle.GetAtlasPageSet(2), Throws.TypeOf<ObjectDisposedException>());

            using var disposedPrepared = loader.PrepareAsync(new AssetId(3), new SceneLoadOptions()).GetAwaiter().GetResult();
            using var disposedAssets = disposedPrepared.AcquirePreparedAssets()
                ?? throw new InvalidOperationException("The prepared Sprite Scene omitted its asset bundle.");
            var disposedBundle = disposedAssets.Value as RenderSceneAssetBundle
                ?? throw new InvalidOperationException("The prepared Sprite Scene retained an unexpected asset type.");

            disposedPrepared.Dispose();
            Assert.That(
                disposedBundle.GetAtlasPageSet(2).GetPage(0).Mips[0].Pixels[0],
                Is.EqualTo(17));
            disposedAssets.Dispose();
            Assert.That(() => disposedBundle.GetAtlasPageSet(2), Throws.TypeOf<ObjectDisposedException>());
        }
        finally
        {
            try
            {
                assets?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            finally
            {
                File.Delete(packagePath);
            }
        }
    }
}
