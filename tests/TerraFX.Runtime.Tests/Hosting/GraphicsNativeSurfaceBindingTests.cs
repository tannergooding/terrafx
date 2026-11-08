// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Graphics;
using TerraFX.Hosting;
using TerraFX.Platform;

namespace TerraFX.Runtime.Tests.Hosting;

internal sealed class GraphicsNativeSurfaceBindingTests
{
    [TestCase(PlatformNativeSurfaceKind.Win32, GraphicsPlatformKind.Win32)]
    [TestCase(PlatformNativeSurfaceKind.Wayland, GraphicsPlatformKind.Wayland)]
    [TestCase(PlatformNativeSurfaceKind.X11, GraphicsPlatformKind.X11)]
    public void CopiesLivePlatformBindingToGraphicsSurfaceBinding(
        PlatformNativeSurfaceKind platformKind,
        GraphicsPlatformKind expectedGraphicsPlatform)
    {
        var window = new WindowHandle(ownerId: 7, index: 3, generation: 2);
        var platformBinding = new PlatformNativeWindowBinding(
            window,
            platformKind,
            (nint)0x1234,
            (nint)0x5678);

        var graphicsBinding = PlatformHostOwner.CreateGraphicsNativeSurfaceBinding(platformBinding, surfaceEpoch: 5);

        Assert.That(graphicsBinding.PlatformKind, Is.EqualTo(expectedGraphicsPlatform));
        Assert.That(graphicsBinding.NativeDisplayOrInstance, Is.EqualTo((nint)0x1234));
        Assert.That(graphicsBinding.NativeWindowOrSurface, Is.EqualTo((nint)0x5678));
        Assert.That(graphicsBinding.SurfaceEpoch, Is.EqualTo(5));
    }

    [Test]
    public void RejectsInvalidWindowAndSurfaceEpoch()
    {
        var invalidWindowBinding = new PlatformNativeWindowBinding(
            default,
            PlatformNativeSurfaceKind.Win32,
            (nint)0x1234,
            (nint)0x5678);

        _ = Assert.Throws<ArgumentException>(() => PlatformHostOwner.CreateGraphicsNativeSurfaceBinding(invalidWindowBinding, surfaceEpoch: 1));

        var windowBinding = new PlatformNativeWindowBinding(
            new WindowHandle(ownerId: 7, index: 3, generation: 2),
            PlatformNativeSurfaceKind.Win32,
            (nint)0x1234,
            (nint)0x5678);

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => PlatformHostOwner.CreateGraphicsNativeSurfaceBinding(windowBinding, surfaceEpoch: 0));
    }

    [Test]
    public void RejectsMissingNativeHandles()
    {
        _ = Assert.Throws<ArgumentException>(() => new GraphicsNativeSurfaceBinding(
            GraphicsPlatformKind.Win32,
            nativeDisplayOrInstance: 0,
            nativeWindowOrSurface: (nint)0x5678,
            surfaceEpoch: 1));
        _ = Assert.Throws<ArgumentException>(() => new GraphicsNativeSurfaceBinding(
            GraphicsPlatformKind.Win32,
            nativeDisplayOrInstance: (nint)0x1234,
            nativeWindowOrSurface: 0,
            surfaceEpoch: 1));
    }
}
