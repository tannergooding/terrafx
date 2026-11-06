// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Graphics.Vulkan;
using TerraFX.Hosting;
using TerraFX.Platform;
using TerraFX.Rendering;
using TerraFX.Runtime.Tests.Hosting;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

internal sealed partial class SceneAssetStoreTests
{
    [Test]
    public async Task SnapshotPinnedAtlasPageBecomesResidentAfterSceneAndSnapshotRetireAsync()
    {
        const int TextureAssetId = 1;
        const int AtlasAssetId = 2;
        const int MaterialAssetId = 3;
        const int SceneAssetId = 4;
        var schemas = CreateRendererRegistry();
        var frameId = new Guid("30000000-0000-4000-8000-000000000001");
        var textureRecord = CreateRenderAtlasPageTextureRecord(17);
        var atlasRecord = CreateRenderAtlasRecord(
            TextureAssetId,
            CreateRenderAtlasFrameRecord(frameId, page: 0, x: 4, y: 4, width: 4, height: 4));
        var materialRecord = CreateRenderMaterialAssetRecord(
            isSprite: true,
            CreateRenderMaterialSpriteParametersRecord(),
            alphaMode: 2,
            doubleSided: false,
            depthTest: false,
            depthWrite: false,
            cullMode: 0,
            outputAlpha: 1,
            requiredCapabilities: 0x009);
        var sceneBytes = SceneAssetReaderTests.Fixtures.CreateScene(
            new AssetId(SceneAssetId),
            additionalEntityComponents: [
                CreateCameraRecord(order: 0),
                CreateRenderSpriteRecord(AtlasAssetId, frameId, materialAssetId: MaterialAssetId),
            ]);
        var packageBytes = Package.CreatePackage(
            new Package.Entry(TextureAssetId, Package.CreateEnvelope(TextureAssetId, textureRecord)),
            new Package.Entry(AtlasAssetId, Package.CreateEnvelope(AtlasAssetId, atlasRecord)),
            new Package.Entry(MaterialAssetId, Package.CreateEnvelope(MaterialAssetId, materialRecord)),
            new Package.Entry(SceneAssetId, Package.CreateEnvelope(SceneAssetId, sceneBytes)));
        var packagePath = Package.WritePackage(packageBytes);
        var assetRegistrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = SceneSupport.RegisterAssetCodecs(assetRegistrationBuilder);
        _ = Renderer.RegisterAssetCodecs(assetRegistrationBuilder);
        var hostOptions = new EngineHostOptions(
            HostProfile.OffscreenNative,
            PlatformBackend.Headless,
            GraphicsBackend.Vulkan,
            VulkanGraphicsBackend.CreateRegistration(),
            new AssetStoreOptions(
                packagePath,
                SHA256.HashData(packageBytes),
                assetRegistrationBuilder.Build())) {
            CpuWorkerCount = 0,
            PersistenceMetadata = WorldPersistenceHeaderWriterTests.CreateMetadata(),
        };
        EngineHost? host = null;
        RenderSnapshot3D? snapshot = null;
        GpuResidencyLease? residencyLease = null;

        try
        {
            try
            {
                host = await EngineHost.StartAsync(hostOptions).ConfigureAwait(false);
            }
            catch (DllNotFoundException exception)
            {
                Assert.Ignore($"Vulkan system library is unavailable: {exception.Message}");
                return;
            }
            catch (EntryPointNotFoundException exception)
            {
                Assert.Ignore($"Vulkan system entry point is unavailable: {exception.Message}");
                return;
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No Vulkan device satisfies OffscreenNative: {exception.Message}");
                return;
            }

            var engineHost = host
                ?? throw new InvalidOperationException("Vulkan EngineHost startup returned no host.");
            var graphicsDevice = engineHost.GraphicsDevice
                ?? throw new InvalidOperationException("OffscreenNative Vulkan startup returned no GraphicsDevice.");
            var residencyService = engineHost.GpuResidency
                ?? throw new InvalidOperationException("OffscreenNative Vulkan startup returned no GPU residency service.");
            var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
            _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
            _ = worldRegistrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration());
            var worldRegistration = worldRegistrationBuilder.Build();
            World? world = null;
            SceneLoader? loader = null;

            await engineHost.InvokeAsync(context => {
                world = context.CreateWorld(new WorldOptions(worldRegistration) {
                    CpuWorkerCount = 0,
                    InitialReplayLineageId = Guid.NewGuid(),
                    WorldGuid = Guid.NewGuid(),
                });
                loader = new SceneLoader(world, engineHost.Assets);
            }).ConfigureAwait(false);

            using var prepared = await loader!.PrepareAsync(
                new AssetId(SceneAssetId),
                new SceneLoadOptions()).ConfigureAwait(false);
            var activation = await loader.ActivateAsync(prepared)
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(activation.Status, Is.EqualTo(SceneActivationStatus.Activated));
            var sceneInstance = activation.Instance
                ?? throw new InvalidOperationException("The activated Sprite Scene omitted its instance.");
            var cameraId = Guid.Empty;

            await engineHost.InvokeAsync(_ => {
                if (!sceneInstance.TryGetEntity(SceneAssetReaderTests.Fixtures.RootEntityId, out var cameraEntity))
                {
                    throw new InvalidOperationException("The activated Sprite Scene did not retain its camera Entity.");
                }

                cameraId = world!.GetEntityId(cameraEntity);
                var extraction = engineHost.Renderer.Extract(
                    world,
                    new RenderExtractionOptions3D(
                        world.CurrentFrame3,
                        new PixelSize(32, 32),
                        [cameraId]));

                if (extraction.Status != RenderExtractionStatus.Extracted)
                {
                    throw new InvalidOperationException(
                        $"The Sprite snapshot extraction failed: {string.Join("; ", extraction.Diagnostics)}");
                }

                snapshot = extraction.Snapshot as RenderSnapshot3D
                    ?? throw new InvalidOperationException("The successful Sprite extraction omitted its 3D snapshot.");
            }).ConfigureAwait(false);

            var spriteSnapshot = snapshot
                ?? throw new InvalidOperationException("The host renderer omitted its Sprite snapshot.");
            Assert.That(spriteSnapshot.SpriteDraws, Has.Length.EqualTo(1));
            var assetPins = spriteSnapshot.AssetPins
                ?? throw new InvalidOperationException("The Sprite snapshot did not pin its package assets.");
            var sceneAssets = assetPins.GetSceneAssets(sceneInstance.SceneInstanceId);
            using var pageAssetLease = sceneAssets
                .GetAtlasPageSet(AtlasAssetId)
                .RetainPageLease(pageIndex: 0);
            residencyLease = await residencyService.EnsureGpuResidencyAsync(
                pageAssetLease,
                new ResidencyOptions(TextureUsage.Sampled | TextureUsage.CopySource)).ConfigureAwait(false);
            pageAssetLease.Dispose();

            var unload = await sceneInstance.UnloadAsync(HierarchyDestroyMode.DestroySubtree)
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(unload.Status, Is.EqualTo(SceneUnloadStatus.Unloaded));
            spriteSnapshot.Dispose();
            Assert.That(spriteSnapshot.AssetPins, Is.Null);
            snapshot = null;

            var residentTexture = residencyLease.Texture;
            var expectedMip = residencyLease.Asset.Mips[0];
            Assert.That(expectedMip.Pixels[0], Is.EqualTo(17));
            Assert.That(residencyLease.DeviceEpoch, Is.EqualTo(graphicsDevice.DeviceEpoch));
            using var readback = await graphicsDevice.ReadbackTextureAsync(
                residentTexture,
                new TextureSubresource(0, 0, TextureAspect.Color)).ConfigureAwait(false);
            Assert.That(readback.Format, Is.EqualTo(residentTexture.Description.Format));
            Assert.That(readback.Width, Is.EqualTo(expectedMip.Width));
            Assert.That(readback.Height, Is.EqualTo(expectedMip.Height));

            var expected = new byte[expectedMip.Pixels.Length];
            var actual = new byte[expected.Length];

            for (var index = 0; index < expected.Length; index++)
            {
                expected[index] = expectedMip.Pixels[index];
            }

            using (var readLease = readback.AcquireReadLease())
            {
                var rowLength = checked(readback.Width * 4);

                for (var row = 0; row < readback.Height; row++)
                {
                    readLease.CopyWindow(
                        checked((nint)row * readback.RowPitch),
                        actual.AsSpan(checked(row * rowLength), rowLength));
                }
            }

            Assert.That(actual, Is.EqualTo(expected));
            residencyLease.Dispose();
            residencyLease = null;
            await engineHost.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            snapshot?.Dispose();
            residencyLease?.Dispose();

            if (host is not null)
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }

            File.Delete(packagePath);
        }
    }
}
