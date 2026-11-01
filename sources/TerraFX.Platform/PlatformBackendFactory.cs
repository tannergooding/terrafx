// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Platform;

internal static class PlatformBackendFactory
{
    internal static IPlatformBackend Create(
        PlatformBackend backend,
        uint ownerId,
        ClipboardServicePolicy clipboardPolicy)
        => backend switch {
            PlatformBackend.Headless => new HeadlessPlatformBackend(ownerId, clipboardPolicy),
            PlatformBackend.WindowsNative
                or PlatformBackend.WaylandOnly
                or PlatformBackend.WaylandThenX11
                or PlatformBackend.X11Only
                => throw new PlatformNotSupportedException($"The platform backend {backend} has no implementation."),
            _ => throw new ArgumentOutOfRangeException(nameof(backend)),
        };
}
