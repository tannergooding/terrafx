// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Graphics;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class GraphicsDeviceStartupTests
{
    [Test]
    public void ValidateStartupProfileAdmitsBackendAndPlatformPairs()
    {
        GraphicsDevice.ValidateStartupProfile(
            GraphicsBackend.D3D12,
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None);
        GraphicsDevice.ValidateStartupProfile(
            GraphicsBackend.Vulkan,
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None);
        GraphicsDevice.ValidateStartupProfile(
            GraphicsBackend.D3D12,
            GraphicsDeviceProfile.Interactive,
            GraphicsPlatformKind.Win32);
        GraphicsDevice.ValidateStartupProfile(
            GraphicsBackend.Vulkan,
            GraphicsDeviceProfile.Interactive,
            GraphicsPlatformKind.Win32);
        GraphicsDevice.ValidateStartupProfile(
            GraphicsBackend.Vulkan,
            GraphicsDeviceProfile.Interactive,
            GraphicsPlatformKind.Wayland);
        GraphicsDevice.ValidateStartupProfile(
            GraphicsBackend.Vulkan,
            GraphicsDeviceProfile.Interactive,
            GraphicsPlatformKind.X11);
    }

    [Test]
    public void ValidateStartupProfileRejectsInvalidBackendAndPlatformPairs()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => GraphicsDevice.ValidateStartupProfile(
            GraphicsBackend.None,
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => GraphicsDevice.ValidateStartupProfile(
            GraphicsBackend.D3D12,
            GraphicsDeviceProfile.None,
            GraphicsPlatformKind.None));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => GraphicsDevice.ValidateStartupProfile(
            GraphicsBackend.D3D12,
            GraphicsDeviceProfile.OffscreenNative,
            (GraphicsPlatformKind)99));
        _ = Assert.Throws<ArgumentException>(() => GraphicsDevice.ValidateStartupProfile(
            GraphicsBackend.Vulkan,
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.Win32));
        _ = Assert.Throws<ArgumentException>(() => GraphicsDevice.ValidateStartupProfile(
            GraphicsBackend.Vulkan,
            GraphicsDeviceProfile.Interactive,
            GraphicsPlatformKind.None));
        _ = Assert.Throws<PlatformNotSupportedException>(() => GraphicsDevice.ValidateStartupProfile(
            GraphicsBackend.D3D12,
            GraphicsDeviceProfile.Interactive,
            GraphicsPlatformKind.Wayland));
        _ = Assert.Throws<PlatformNotSupportedException>(() => GraphicsDevice.ValidateStartupProfile(
            GraphicsBackend.D3D12,
            GraphicsDeviceProfile.Interactive,
            GraphicsPlatformKind.X11));
    }
}
