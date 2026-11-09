// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TerraFX.Graphics;
using TerraFX.Interop.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal static class VulkanSurfaceFormatSelector
{
    internal static (VkFormat NativeFormat, VkColorSpaceKHR ColorSpace, TextureFormat ColorFormat) SelectFormat(
        ImmutableArray<GraphicsFormatCapabilities> deviceFormatCapabilities,
        VkSurfaceFormatKHR[] formats)
    {
        var publicFormats = new List<(VkFormat NativeFormat, VkColorSpaceKHR ColorSpace, TextureFormat Format)>(formats.Length);
        var hasUndefinedFormat = formats.Length == 1
            && (formats[0].format == VkFormat.VK_FORMAT_UNDEFINED);

        if (hasUndefinedFormat)
        {
            var colorSpace = formats[0].colorSpace;
            AddFormat(VkFormat.VK_FORMAT_B8G8R8A8_SRGB, colorSpace);
            AddFormat(VkFormat.VK_FORMAT_R8G8B8A8_SRGB, colorSpace);
        }
        else
        {
            foreach (var format in formats)
            {
                AddFormat(format.format, format.colorSpace);
            }
        }

        var selected = FindFormat(TextureFormat.Bgra8Srgb);
        selected = selected.Format == TextureFormat.None ? FindFormat(TextureFormat.Rgba8Srgb) : selected;
        selected = selected.Format == TextureFormat.None ? FindFormat(TextureFormat.Bgra8Unorm) : selected;
        selected = selected.Format == TextureFormat.None ? FindFormat(TextureFormat.Rgba8Unorm) : selected;

        if (selected.Format == TextureFormat.None)
        {
            throw new NotSupportedException("The Vulkan surface has no sRGB-source-compatible format in the sRGB nonlinear color space.");
        }

        return (selected.NativeFormat, selected.ColorSpace, selected.Format);

        (VkFormat NativeFormat, VkColorSpaceKHR ColorSpace, TextureFormat Format) FindFormat(TextureFormat format)
        {
            return publicFormats.Find(entry =>
                (entry.Format == format)
                && (entry.ColorSpace == VkColorSpaceKHR.VK_COLOR_SPACE_SRGB_NONLINEAR_KHR));
        }

        void AddFormat(VkFormat nativeFormat, VkColorSpaceKHR colorSpace)
        {
            if (colorSpace != VkColorSpaceKHR.VK_COLOR_SPACE_SRGB_NONLINEAR_KHR
                || !TryGetTextureFormat(nativeFormat, out var surfaceFormat)
                || !SurfaceFormatCompatibility.TryGetSrgbPresentationSourceFormat(surfaceFormat, out var sourceFormat))
            {
                return;
            }

            foreach (var capability in deviceFormatCapabilities)
            {
                if ((capability.Format == sourceFormat)
                    && ((capability.SupportedUsages & (TextureUsage.ColorAttachment | TextureUsage.CopySource))
                        == (TextureUsage.ColorAttachment | TextureUsage.CopySource)))
                {
                    publicFormats.Add((nativeFormat, colorSpace, surfaceFormat));
                    break;
                }
            }
        }
    }

    internal static bool TryGetTextureFormat(VkFormat nativeFormat, out TextureFormat format)
    {
        format = nativeFormat switch {
            VkFormat.VK_FORMAT_R8G8B8A8_UNORM => TextureFormat.Rgba8Unorm,
            VkFormat.VK_FORMAT_R8G8B8A8_SRGB => TextureFormat.Rgba8Srgb,
            VkFormat.VK_FORMAT_B8G8R8A8_UNORM => TextureFormat.Bgra8Unorm,
            VkFormat.VK_FORMAT_B8G8R8A8_SRGB => TextureFormat.Bgra8Srgb,
            _ => TextureFormat.None,
        };
        return format != TextureFormat.None;
    }
}
