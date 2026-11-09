// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

internal static class SurfaceFormatCompatibility
{
    internal static TextureFormat GetSrgbPresentationSourceFormat(TextureFormat surfaceFormat)
        => TryGetSrgbPresentationSourceFormat(surfaceFormat, out var sourceFormat)
            ? sourceFormat
            : throw new NotSupportedException("The selected Surface format has no admitted sRGB presentation source format.");

    internal static TextureFormat GetSrgbSurfaceStorageFormat(TextureFormat presentationSourceFormat)
        => presentationSourceFormat switch {
            TextureFormat.Rgba8Srgb => TextureFormat.Rgba8Unorm,
            TextureFormat.Bgra8Srgb => TextureFormat.Bgra8Unorm,
            _ => throw new NotSupportedException("The selected presentation source has no admitted UNorm surface format."),
        };

    internal static bool IsSrgbPresentationSourceCompatible(TextureFormat sourceFormat, TextureFormat surfaceFormat)
        => TryGetSrgbPresentationSourceFormat(surfaceFormat, out var expectedSourceFormat)
            && (sourceFormat == expectedSourceFormat);

    internal static bool TryGetSrgbPresentationSourceFormat(TextureFormat surfaceFormat, out TextureFormat sourceFormat)
    {
        // UNorm surfaces in the sRGB nonlinear color space receive bytes encoded by the matching sRGB attachment.
        sourceFormat = surfaceFormat switch {
            TextureFormat.Rgba8Srgb or TextureFormat.Rgba8Unorm => TextureFormat.Rgba8Srgb,
            TextureFormat.Bgra8Srgb or TextureFormat.Bgra8Unorm => TextureFormat.Bgra8Srgb,
            _ => TextureFormat.None,
        };
        return sourceFormat != TextureFormat.None;
    }
}
