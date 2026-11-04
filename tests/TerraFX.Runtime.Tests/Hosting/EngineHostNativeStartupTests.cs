// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX.Assets;
using TerraFX.Graphics;
using TerraFX.Graphics.D3D12;
using TerraFX.Hosting;
using TerraFX.Platform;
using TerraFX.Runtime.Tests.Scenes;

namespace TerraFX.Runtime.Tests.Hosting;

internal sealed partial class EngineHostOptionsTests
{
    [Test]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The test awaits host startup and ordered shutdown before disposing its package file.")]
    public async Task StartsOffscreenNativeEngineHostWithD3D12DeviceAsync()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 host path targets Windows 11.");
            return;
        }

        var packageBytes = SceneAssetStoreTests.Package.CreatePackage();
        var packagePath = SceneAssetStoreTests.Package.WritePackage(packageBytes);
        var assetStoreOptions = new AssetStoreOptions(
            packagePath,
            SHA256.HashData(packageBytes),
            CreateAssetStoreOptions().Registration);
        var options = new EngineHostOptions(
            HostProfile.OffscreenNative,
            PlatformBackend.Headless,
            GraphicsBackend.D3D12,
            D3D12GraphicsBackend.CreateRegistration(),
            assetStoreOptions) {
            CpuWorkerCount = 0,
            PersistenceMetadata = CreatePersistenceMetadata(),
        };
        EngineHost? host = null;

        try
        {
            try
            {
                host = await EngineHost.StartAsync(options).ConfigureAwait(false);
            }
            catch (DllNotFoundException exception)
            {
                Assert.Ignore($"A required D3D12 system library is unavailable: {exception.Message}");
            }
            catch (EntryPointNotFoundException exception)
            {
                Assert.Ignore($"A required D3D12 system entry point is unavailable: {exception.Message}");
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No available D3D12 device satisfies OffscreenNative: {exception.Message}");
            }

            var graphicsDevice = host?.GraphicsDevice
                ?? throw new InvalidOperationException("OffscreenNative startup returned no GraphicsDevice.");

            Assert.Multiple(() => {
                Assert.That(graphicsDevice.Status, Is.EqualTo(GraphicsDeviceStatus.Ready));
                Assert.That(graphicsDevice.OwnerId, Is.GreaterThan(0));
                Assert.That(graphicsDevice.Capabilities.Backend, Is.EqualTo(GraphicsBackend.D3D12));
                Assert.That(graphicsDevice.Capabilities.Profile, Is.EqualTo(GraphicsDeviceProfile.OffscreenNative));
                Assert.That(graphicsDevice.Capabilities.AdapterName, Is.Not.Empty);
            });

            await host.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (host is not null)
                {
                    await host.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                File.Delete(packagePath);
            }
        }
    }
}
