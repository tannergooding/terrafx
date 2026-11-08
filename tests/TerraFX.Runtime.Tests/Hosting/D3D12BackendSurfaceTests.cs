// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Graphics;
using TerraFX.Graphics.D3D12;
using TerraFX.Hosting;
using TerraFX.Platform;
using TerraFX.Numerics;
using TerraFX.Runtime.Tests.Scenes;

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
        GraphicsDevice? graphicsDevice = null;
        Surface? surface = null;

        try
        {
            var window = platformOwner.CreateWindow(new WindowOptions("D3D12 surface witness") {
                ClientSize = new Vector2(640, 360),
                Visible = false,
            });
            var binding = platformOwner.GetGraphicsNativeSurfaceBinding(window, surfaceEpoch: 1);

            try
            {
                graphicsDevice = GraphicsDevice.CreateAsync(
                    platformOwner.OwnerId,
                    registration,
                    GraphicsDeviceProfile.Interactive,
                    options.GraphicsFrameSlotCount,
                    GraphicsPlatformKind.Win32,
                    CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No D3D12 adapter satisfies Interactive presentation: {exception.Message}");
                return;
            }

            surface = graphicsDevice.CreateSurfaceAsync(binding, new PixelSize(640, 360), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(surface.ClientPixelSize, Is.EqualTo(new PixelSize(640, 360)));
            Assert.That(surface.Capabilities.SurfaceEpoch, Is.EqualTo(1));
            Assert.That(surface.Capabilities.SwapchainEpoch, Is.EqualTo(1));
            Assert.That(surface.Capabilities.PresentModes, Does.Contain(GraphicsPresentMode.Fifo));
            Assert.That(
                surface.Capabilities.PresentModes.Contains(GraphicsPresentMode.Immediate),
                Is.EqualTo(surface.Capabilities.SupportsTearing));
            Assert.That(surface.Capabilities.Formats, Has.Length.EqualTo(1));
            Assert.That(surface.Capabilities.Formats[0].Format, Is.EqualTo(surface.ColorFormat));

            var resizedReceipt = surface.ResizeAsync(new PixelSize(800, 600)).GetAwaiter().GetResult();

            Assert.That(surface.ClientPixelSize, Is.EqualTo(new PixelSize(800, 600)));
            Assert.That(resizedReceipt.Status, Is.EqualTo(SurfaceResizeStatus.Resized));
            Assert.That(resizedReceipt.SurfaceEpoch, Is.EqualTo(1));
            Assert.That(resizedReceipt.SwapchainEpoch, Is.EqualTo(2));

            var suspendedReceipt = surface.ResizeAsync(new PixelSize(0, 0)).GetAwaiter().GetResult();

            Assert.That(surface.ClientPixelSize, Is.EqualTo(new PixelSize(0, 0)));
            Assert.That(suspendedReceipt.Status, Is.EqualTo(SurfaceResizeStatus.Suspended));
            Assert.That(suspendedReceipt.SurfaceEpoch, Is.EqualTo(1));
            Assert.That(suspendedReceipt.SwapchainEpoch, Is.Zero);

            var restoredReceipt = surface.ResizeAsync(new PixelSize(1024, 768)).GetAwaiter().GetResult();

            Assert.That(surface.ClientPixelSize, Is.EqualTo(new PixelSize(1024, 768)));
            Assert.That(restoredReceipt.Status, Is.EqualTo(SurfaceResizeStatus.Resized));
            Assert.That(restoredReceipt.SurfaceEpoch, Is.EqualTo(1));
            Assert.That(restoredReceipt.SwapchainEpoch, Is.EqualTo(3));
        }
        finally
        {
            try
            {
                if ((graphicsDevice is not null) && (surface is not null))
                {
                    graphicsDevice.RetireSurfaceAsync(surface).GetAwaiter().GetResult();
                }
            }
            finally
            {
                try
                {
                    graphicsDevice?.ShutdownAsync().GetAwaiter().GetResult();
                }
                finally
                {
                    platformOwner.DisposeFromOwner();
                }
            }
        }
    }

    [Test]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The EngineHost is disposed in the enclosing finally block after native window closure.")]
    public void EngineHostInteractiveStartupMapsSurfaceBeforeWindowReceipt()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The Interactive EngineHost witness requires Windows 11.");
            return;
        }

        var packageBytes = SceneAssetStoreTests.Package.CreatePackage();
        var packagePath = SceneAssetStoreTests.Package.WritePackage(packageBytes);
        var assetStoreOptions = new AssetStoreOptions(
            packagePath,
            SHA256.HashData(packageBytes),
            CreateAssetStoreOptions().Registration);
        var options = new EngineHostOptions(
            HostProfile.Interactive,
            PlatformBackend.WindowsNative,
            GraphicsBackend.D3D12,
            D3D12GraphicsBackend.CreateRegistration(),
            assetStoreOptions) {
            CpuWorkerCount = 0,
            GraphicsFrameSlotCount = 2,
            PersistenceMetadata = CreatePersistenceMetadata(),
        };
        EngineHost? host = null;

        try
        {
            try
            {
                host = EngineHost.StartAsync(options).GetAwaiter().GetResult();
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No D3D12 adapter satisfies Interactive hosting: {exception.Message}");
                return;
            }

            var runningHost = host
                ?? throw new InvalidOperationException("Interactive EngineHost startup returned no host.");
            var receipt = runningHost.CreateWindowAsync(new WindowOptions("Interactive surface witness") {
                ClientSize = new Vector2(640, 360),
                Visible = false,
            }).GetAwaiter().GetResult();
            Assert.That(receipt.Status, Is.EqualTo(WindowCreationStatus.Created));
            var window = receipt.Window
                ?? throw new InvalidOperationException("Interactive window creation returned no WindowHandle.");
            _ = Assert.Throws<ArgumentException>(() => runningHost.GetSurface(default));
            _ = Assert.Throws<InvalidOperationException>(() => runningHost.GetSurface(window));
            Surface? firstSurface = null;
            Surface? secondSurface = null;
            runningHost.InvokeAsync(_ => firstSurface = runningHost.GetSurface(window)).GetAwaiter().GetResult();
            runningHost.InvokeAsync(_ => secondSurface = runningHost.GetSurface(window)).GetAwaiter().GetResult();
            Assert.That(secondSurface, Is.SameAs(firstSurface));
            var surface = secondSurface
                ?? throw new InvalidOperationException("EngineHost.GetSurface returned no Surface.");

            var resizeReceipt = surface.ResizeAsync(new PixelSize(800, 600)).GetAwaiter().GetResult();
            Assert.That(resizeReceipt.Status, Is.EqualTo(SurfaceResizeStatus.Resized));
            Assert.That(resizeReceipt.SwapchainEpoch, Is.EqualTo(2));

            var closeReceipt = runningHost.Platform.CloseWindowAsync(window).GetAwaiter().GetResult();
            Assert.That(closeReceipt.Status, Is.EqualTo(WindowCommandStatus.Applied));
            _ = Assert.Throws<InvalidOperationException>(
                () => runningHost.InvokeAsync(_ => runningHost.GetSurface(window)).GetAwaiter().GetResult());

            var unavailableMonitorReceipt = runningHost.CreateWindowAsync(new WindowOptions("stale monitor witness") {
                Monitor = new MonitorHandle(runningHost.Platform.OwnerId, uint.MaxValue, generation: 1),
                Visible = false,
            }).GetAwaiter().GetResult();
            Assert.That(unavailableMonitorReceipt.Status, Is.EqualTo(WindowCreationStatus.Rejected));
            Assert.That(unavailableMonitorReceipt.Window, Is.Null);
            Assert.That(unavailableMonitorReceipt.Rejection?.Code, Is.EqualTo("MonitorUnavailable"));
        }
        finally
        {
            try
            {
                host?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            finally
            {
                File.Delete(packagePath);
            }
        }
    }
}
