// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Graphics;
using TerraFX.Graphics.D3D12;
using TerraFX.Hosting;
using TerraFX.Platform;
using TerraFX.Numerics;

namespace TerraFX.Runtime.Tests.Hosting;

internal sealed partial class EngineHostOptionsTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    public void D3D12BackendCreatesWin32FlipDiscardSurface()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 surface witness requires Windows 11.");
            return;
        }

        var registration = D3D12GraphicsBackend.CreateRegistration();
        var options = new EngineHostOptions(
            HostProfile.Interactive,
            PlatformBackend.WindowsNative,
            GraphicsBackend.D3D12,
            registration,
            CreateAssetStoreOptions()) {
            CpuWorkerCount = 0,
            GraphicsFrameSlotCount = 2,
            PersistenceMetadata = CreatePersistenceMetadata(),
        };
        var platformOwner = PlatformHostOwner.Create(options);
        IGraphicsBackendDevice? backendDevice = null;
        IGraphicsBackendSurface? surface = null;

        try
        {
            var window = platformOwner.CreateWindow(new WindowOptions("D3D12 surface witness") {
                ClientSize = new Vector2(640, 360),
                Visible = false,
            });
            var binding = platformOwner.GetGraphicsNativeSurfaceBinding(window, surfaceEpoch: 1);

            try
            {
                backendDevice = registration.Factory.Create(
                    platformOwner.OwnerId,
                    GraphicsDeviceProfile.Interactive,
                    GraphicsPlatformKind.Win32,
                    options.GraphicsFrameSlotCount,
                    CancellationToken.None);
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No D3D12 adapter satisfies Interactive presentation: {exception.Message}");
                return;
            }

            surface = backendDevice.CreateSurface(binding, new PixelSize(640, 360));

            Assert.That(surface.ClientPixelSize, Is.EqualTo(new PixelSize(640, 360)));
            Assert.That(surface.Capabilities.SurfaceEpoch, Is.EqualTo(1));
            Assert.That(surface.Capabilities.SwapchainEpoch, Is.EqualTo(1));
            Assert.That(surface.Capabilities.PresentModes, Does.Contain(GraphicsPresentMode.Fifo));
            Assert.That(
                surface.Capabilities.PresentModes.Contains(GraphicsPresentMode.Immediate),
                Is.EqualTo(surface.Capabilities.SupportsTearing));
            Assert.That(surface.Capabilities.Formats, Has.Length.EqualTo(1));
            Assert.That(surface.Capabilities.Formats[0].Format, Is.EqualTo(surface.ColorFormat));

            var resizedCapabilities = surface.Resize(new PixelSize(800, 600));

            Assert.That(surface.ClientPixelSize, Is.EqualTo(new PixelSize(800, 600)));
            Assert.That(resizedCapabilities.SurfaceEpoch, Is.EqualTo(1));
            Assert.That(resizedCapabilities.SwapchainEpoch, Is.EqualTo(2));

            var suspendedCapabilities = surface.Resize(new PixelSize(0, 0));

            Assert.That(surface.ClientPixelSize, Is.EqualTo(new PixelSize(0, 0)));
            Assert.That(suspendedCapabilities.SurfaceEpoch, Is.EqualTo(1));
            Assert.That(suspendedCapabilities.SwapchainEpoch, Is.Zero);

            var restoredCapabilities = surface.Resize(new PixelSize(1024, 768));

            Assert.That(surface.ClientPixelSize, Is.EqualTo(new PixelSize(1024, 768)));
            Assert.That(restoredCapabilities.SurfaceEpoch, Is.EqualTo(1));
            Assert.That(restoredCapabilities.SwapchainEpoch, Is.EqualTo(3));
        }
        finally
        {
            try
            {
                surface?.Dispose();
            }
            finally
            {
                try
                {
                    backendDevice?.Dispose();
                }
                finally
                {
                    platformOwner.DisposeFromOwner();
                }
            }
        }
    }
}
