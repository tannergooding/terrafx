// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using NUnit.Framework;
using TerraFX.Graphics;
using TerraFX.Graphics.Vulkan;
using TerraFX.Interop.Vulkan;

namespace TerraFX.Runtime.Tests.Hosting;

[TestFixture]
internal sealed class VulkanSurfaceFormatSelectorTests
{
    private static ImmutableArray<GraphicsFormatCapabilities> CreateFormatCapabilities(bool includeBgraSrgb = true)
    {
        var formats = ImmutableArray.CreateBuilder<GraphicsFormatCapabilities>(includeBgraSrgb ? 4 : 3);
        formats.Add(new GraphicsFormatCapabilities(
            TextureFormat.Rgba8Srgb,
            TextureUsage.ColorAttachment | TextureUsage.CopySource,
            [1]));
        formats.Add(new GraphicsFormatCapabilities(
            TextureFormat.Rgba8Unorm,
            TextureUsage.CopyDestination,
            [1]));
        formats.Add(new GraphicsFormatCapabilities(
            TextureFormat.Bgra8Unorm,
            TextureUsage.CopyDestination,
            [1]));

        if (includeBgraSrgb)
        {
            formats.Add(new GraphicsFormatCapabilities(
                TextureFormat.Bgra8Srgb,
                TextureUsage.ColorAttachment | TextureUsage.CopySource,
                [1]));
        }

        return formats.MoveToImmutable();
    }

    [Test]
    public void RejectsUnormSurfaceWithoutMatchingSrgbSource()
    {
        var formats = new[] {
            new VkSurfaceFormatKHR {
                format = VkFormat.VK_FORMAT_B8G8R8A8_UNORM,
                colorSpace = VkColorSpaceKHR.VK_COLOR_SPACE_SRGB_NONLINEAR_KHR,
            },
        };

        _ = Assert.Throws<NotSupportedException>(() =>
            VulkanSurfaceFormatSelector.SelectFormat(
                CreateFormatCapabilities(includeBgraSrgb: false),
                formats));
    }

    [Test]
    public void SelectsSrgbSurfaceBeforeUnormFallback()
    {
        var formats = new[] {
            new VkSurfaceFormatKHR {
                format = VkFormat.VK_FORMAT_R8G8B8A8_UNORM,
                colorSpace = VkColorSpaceKHR.VK_COLOR_SPACE_SRGB_NONLINEAR_KHR,
            },
            new VkSurfaceFormatKHR {
                format = VkFormat.VK_FORMAT_R8G8B8A8_SRGB,
                colorSpace = VkColorSpaceKHR.VK_COLOR_SPACE_SRGB_NONLINEAR_KHR,
            },
        };

        var (nativeFormat, _, colorFormat) = VulkanSurfaceFormatSelector.SelectFormat(
            CreateFormatCapabilities(),
            formats);

        Assert.That(nativeFormat, Is.EqualTo(VkFormat.VK_FORMAT_R8G8B8A8_SRGB));
        Assert.That(colorFormat, Is.EqualTo(TextureFormat.Rgba8Srgb));
    }

    [Test]
    public void SelectsUnormSurfaceWithSrgbSourceWhenNoSrgbSurfacePairExists()
    {
        var formats = new[] {
            new VkSurfaceFormatKHR {
                format = VkFormat.VK_FORMAT_R8G8B8A8_UNORM,
                colorSpace = VkColorSpaceKHR.VK_COLOR_SPACE_SRGB_NONLINEAR_KHR,
            },
        };

        var (nativeFormat, _, colorFormat) = VulkanSurfaceFormatSelector.SelectFormat(
            CreateFormatCapabilities(),
            formats);

        Assert.That(nativeFormat, Is.EqualTo(VkFormat.VK_FORMAT_R8G8B8A8_UNORM));
        Assert.That(colorFormat, Is.EqualTo(TextureFormat.Rgba8Unorm));
        Assert.That(
            SurfaceFormatCompatibility.GetSrgbPresentationSourceFormat(colorFormat),
            Is.EqualTo(TextureFormat.Rgba8Srgb));
    }
}
