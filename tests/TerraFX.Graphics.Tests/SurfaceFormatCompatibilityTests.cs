// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using NUnit.Framework;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class SurfaceFormatCompatibilityTests
{
    [TestCase(TextureFormat.Rgba8Srgb, TextureFormat.Rgba8Srgb)]
    [TestCase(TextureFormat.Rgba8Unorm, TextureFormat.Rgba8Srgb)]
    [TestCase(TextureFormat.Bgra8Srgb, TextureFormat.Bgra8Srgb)]
    [TestCase(TextureFormat.Bgra8Unorm, TextureFormat.Bgra8Srgb)]
    public void GetsMatchingSrgbPresentationSourceFormat(
        TextureFormat surfaceFormat,
        TextureFormat expectedSourceFormat)
    {
        Assert.That(
            SurfaceFormatCompatibility.GetSrgbPresentationSourceFormat(surfaceFormat),
            Is.EqualTo(expectedSourceFormat));
    }

    [TestCase(TextureFormat.Rgba8Srgb, TextureFormat.Rgba8Srgb, true)]
    [TestCase(TextureFormat.Rgba8Srgb, TextureFormat.Rgba8Unorm, true)]
    [TestCase(TextureFormat.Bgra8Srgb, TextureFormat.Bgra8Unorm, true)]
    [TestCase(TextureFormat.Rgba8Srgb, TextureFormat.Bgra8Unorm, false)]
    [TestCase(TextureFormat.Rgba8Unorm, TextureFormat.Rgba8Unorm, false)]
    public void ReportsSrgbCopyCompatibilityForMatchingChannelOrder(
        TextureFormat sourceFormat,
        TextureFormat surfaceFormat,
        bool expectedCompatibility)
    {
        Assert.That(
            SurfaceFormatCompatibility.IsSrgbPresentationSourceCompatible(sourceFormat, surfaceFormat),
            Is.EqualTo(expectedCompatibility));
    }
}
