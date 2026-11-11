// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using NUnit.Framework;
using TerraFX.Graphics;
using TerraFX.Hosting;
using TerraFX.Numerics;
using TerraFX.Platform;

namespace TerraFX.Runtime.Tests.Hosting;

internal sealed partial class EngineHostOptionsTests
{
    private static void RunVulkanSurfaceResizeStress(PlatformBackend platformBackend)
    {
        var supportedPlatform = platformBackend switch {
            PlatformBackend.WindowsNative => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000),
            PlatformBackend.WaylandOnly or PlatformBackend.X11Only => OperatingSystem.IsLinux(),
            _ => false,
        };

        if (!supportedPlatform)
        {
            Assert.Ignore($"The Vulkan {platformBackend} resize witness is not supported on this operating system.");
            return;
        }

        if ((platformBackend is PlatformBackend.WaylandOnly)
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            Assert.Ignore("The Vulkan resize witness requires a Linux Wayland display.");
            return;
        }

        if ((platformBackend is PlatformBackend.X11Only)
            && (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"))
                || (Environment.GetEnvironmentVariable("TERRAFX_TEST_ISOLATED_X11") != "1")))
        {
            Assert.Ignore("The Vulkan resize witness requires an isolated Linux X11 display.");
            return;
        }

        var (options, registration, _) = CreateVulkanSurfaceTestSetup(platformBackend);
        var platformOwner = PlatformHostOwner.Create(options);
        GraphicsDevice? graphicsDevice = null;
        Surface? surface = null;
        WindowHandle? window = null;

        try
        {
            window = platformOwner.CreateWindow(new WindowOptions("Vulkan resize stress witness") {
                ClientSize = new Vector2(640.0f, 360.0f),
                Visible = false,
            });
            var clientPixelSize = platformOwner.Platform.GetMetrics(window.Value).ClientPixelSize;

            if ((clientPixelSize.Width <= 0) || (clientPixelSize.Height <= 0))
            {
                throw new InvalidOperationException("The Vulkan resize witness window has no positive client extent.");
            }

            var binding = platformOwner.GetGraphicsNativeSurfaceBinding(window.Value, surfaceEpoch: 1);

            try
            {
                graphicsDevice = GraphicsDevice.CreateAsync(
                    platformOwner.OwnerId,
                    registration,
                    GraphicsDeviceProfile.Interactive,
                    options.GraphicsFrameSlotCount,
                    platformOwner.GraphicsPlatformKind,
                    binding,
                    CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No Vulkan adapter satisfies Interactive presentation on {platformBackend}: {exception.Message}");
                return;
            }

            surface = graphicsDevice.CreateSurfaceAsync(binding, clientPixelSize, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            var activeSurface = surface
                ?? throw new InvalidOperationException("The Vulkan resize witness created no Surface.");
            var expectedSwapchainEpoch = activeSurface.Capabilities.SwapchainEpoch;

            Assert.That(expectedSwapchainEpoch, Is.EqualTo(1));

            for (var index = 0; index < 100; index++)
            {
                switch (index % 3)
                {
                    case 0:
                    {
                        var resizeReceipt = activeSurface.ResizeAsync(clientPixelSize).GetAwaiter().GetResult();
                        expectedSwapchainEpoch = checked(expectedSwapchainEpoch + 1);
                        Assert.That(resizeReceipt.Status, Is.EqualTo(SurfaceResizeStatus.Resized));
                        Assert.That(resizeReceipt.SwapchainEpoch, Is.EqualTo(expectedSwapchainEpoch));
                        break;
                    }

                    case 1:
                    {
                        var minimizeReceipt = activeSurface.ResizeAsync(new PixelSize(0, 0)).GetAwaiter().GetResult();
                        Assert.That(minimizeReceipt.Status, Is.EqualTo(SurfaceResizeStatus.Suspended));
                        Assert.That(minimizeReceipt.SwapchainEpoch, Is.Zero);
                        break;
                    }

                    default:
                    {
                        var restoreReceipt = activeSurface.ResizeAsync(clientPixelSize).GetAwaiter().GetResult();
                        expectedSwapchainEpoch = checked(expectedSwapchainEpoch + 1);
                        Assert.That(restoreReceipt.Status, Is.EqualTo(SurfaceResizeStatus.Resized));
                        Assert.That(restoreReceipt.SwapchainEpoch, Is.EqualTo(expectedSwapchainEpoch));
                        break;
                    }
                }
            }
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
                    if (window is WindowHandle activeWindow)
                    {
                        platformOwner.CloseWindow(activeWindow);
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
    }

    [TestCase(PlatformBackend.WaylandOnly)]
    [TestCase(PlatformBackend.X11Only)]
    public void VulkanBackendRunsOneHundredResizeMinimizeRestoreOperationsOnLinux(PlatformBackend platformBackend)
        => RunVulkanSurfaceResizeStress(platformBackend);

    [Test]
    [Platform(Include = "Win")]
    [Apartment(ApartmentState.STA)]
    public void VulkanBackendRunsOneHundredResizeMinimizeRestoreOperationsOnWindows()
        => RunVulkanSurfaceResizeStress(PlatformBackend.WindowsNative);
}
