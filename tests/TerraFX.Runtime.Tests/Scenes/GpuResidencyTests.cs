// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX.Assets;
using TerraFX.Graphics;
using TerraFX.Graphics.Vulkan;
using TerraFX.Hosting;
using TerraFX.Platform;
using TerraFX.Rendering;
using TerraFX.Runtime.Tests.Hosting;

namespace TerraFX.Runtime.Tests.Scenes;

internal sealed partial class SceneAssetStoreTests
{
    [Test]
    public async Task GpuResidencyUploadsPackageTextureMipsAndRetainsItsAssetLease()
    {
        var schemas = CreateRendererRegistry();
        var assetRegistrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = Renderer.RegisterAssetCodecs(assetRegistrationBuilder);
        var basePixels = new byte[32];
        var secondMipPixels = new byte[8];
        var finalMipPixels = new byte[4];

        for (var index = 0; index < basePixels.Length; index++)
        {
            basePixels[index] = unchecked((byte)((index * 17) + 3));
        }

        for (var index = 0; index < secondMipPixels.Length; index++)
        {
            secondMipPixels[index] = unchecked((byte)((index * 29) + 5));
        }

        finalMipPixels[0] = 71;
        finalMipPixels[1] = 83;
        finalMipPixels[2] = 97;
        finalMipPixels[3] = 109;
        const int TextureAssetId = 71;
        var textureRecord = CreateRenderTextureAssetRecord(
            4,
            2,
            RenderTextureColorSpace.Srgb,
            RenderTextureAlphaMode.Premultiplied,
            RenderTextureSemantic.Color,
            RenderTextureMipPolicy.FullChain,
            CreateRenderTextureMipRecord(0, 4, 2, basePixels),
            CreateRenderTextureMipRecord(1, 2, 1, secondMipPixels),
            CreateRenderTextureMipRecord(2, 1, 1, finalMipPixels));
        var packageBytes = Package.CreatePackage(
            new Package.Entry(TextureAssetId, Package.CreateEnvelope(TextureAssetId, textureRecord)));
        var packagePath = Package.WritePackage(packageBytes);
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

            using var assetLease = await engineHost.Assets.LoadAsync<RenderTextureAsset>(
                new AssetId(TextureAssetId)).ConfigureAwait(false);
            var residencyLease = await residencyService.EnsureGpuResidencyAsync(
                assetLease,
                new ResidencyOptions(TextureUsage.Sampled | TextureUsage.CopySource)).ConfigureAwait(false);

            try
            {
                assetLease.Dispose();
                Assert.That(residencyLease.AssetId, Is.EqualTo(new AssetId(TextureAssetId)));
                Assert.That(residencyLease.DeviceEpoch, Is.EqualTo(graphicsDevice.DeviceEpoch));
                Assert.That(residencyLease.Asset.Mips[0].Pixels[0], Is.EqualTo(basePixels[0]));
                Assert.That(residencyLease.Asset.Mips[^1].Pixels[^1], Is.EqualTo(finalMipPixels[^1]));
                Assert.That(residencyLease.Texture.Description.Format, Is.EqualTo(TextureFormat.Rgba8Srgb));
                Assert.That(
                    residencyLease.Texture.Description.Usage,
                    Is.EqualTo(TextureUsage.Sampled | TextureUsage.CopySource | TextureUsage.CopyDestination));
                Assert.That(residencyLease.Texture.Description.MipLevels, Is.EqualTo(3));

                var expectedMips = new[] { basePixels, secondMipPixels, finalMipPixels };
                var mipWidths = new[] { 4, 2, 1 };
                var mipHeights = new[] { 2, 1, 1 };

                for (var index = 0; index < expectedMips.Length; index++)
                {
                    using var readback = await graphicsDevice.ReadbackTextureAsync(
                        residencyLease.Texture,
                        new TextureSubresource(index, 0, TextureAspect.Color)).ConfigureAwait(false);
                    Assert.That(readback.Format, Is.EqualTo(TextureFormat.Rgba8Srgb));
                    Assert.That(readback.Width, Is.EqualTo(mipWidths[index]));
                    Assert.That(readback.Height, Is.EqualTo(mipHeights[index]));

                    var padded = new byte[checked((int)readback.ByteLength)];

                    using (var readLease = readback.AcquireReadLease())
                    {
                        readLease.CopyWindow(0, padded);
                    }

                    var rowLength = checked(mipWidths[index] * 4);
                    var actual = new byte[expectedMips[index].Length];
                    var rowPitch = checked((int)readback.RowPitch);

                    for (var row = 0; row < mipHeights[index]; row++)
                    {
                        padded.AsSpan(checked(row * rowPitch), rowLength)
                            .CopyTo(actual.AsSpan(checked(row * rowLength), rowLength));
                    }

                    Assert.That(actual, Is.EqualTo(expectedMips[index]), $"Mip {index} did not preserve its package bytes.");
                }
            }
            finally
            {
                residencyLease.Dispose();
            }

            await engineHost.StopAsync().ConfigureAwait(false);
            _ = Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await residencyService.EnsureGpuResidencyAsync(
                    assetLease,
                    new ResidencyOptions()).ConfigureAwait(false));
        }
        finally
        {
            if (host is not null)
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }

            File.Delete(packagePath);
        }
    }

    [Test]
    public void ResidencyOptionsRequireSamplingAndRejectUnadmittedTextureUses()
    {
        Assert.That(new ResidencyOptions().Usage, Is.EqualTo(TextureUsage.Sampled));
        Assert.That(
            new ResidencyOptions(TextureUsage.Sampled | TextureUsage.CopySource).Usage,
            Is.EqualTo(TextureUsage.Sampled | TextureUsage.CopySource));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new ResidencyOptions(TextureUsage.None));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new ResidencyOptions(TextureUsage.Storage));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ResidencyOptions(TextureUsage.Sampled | TextureUsage.CopyDestination));
    }
}
