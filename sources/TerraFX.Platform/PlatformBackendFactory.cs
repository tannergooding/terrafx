// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Platform;

internal static class PlatformBackendFactory
{
    internal static IPlatformBackend Create(
        PlatformBackend backend,
        uint ownerId,
        ClipboardServicePolicy clipboardPolicy,
        TimeProvider? timeProvider = null)
        => backend switch {
            PlatformBackend.Headless => new HeadlessPlatformBackend(ownerId, clipboardPolicy),
            PlatformBackend.WindowsNative when OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
                => new WindowsPlatformBackend(ownerId, clipboardPolicy, timeProvider),
            PlatformBackend.WindowsNative
                => throw new PlatformNotSupportedException("The Windows Platform backend requires Windows 11."),
            PlatformBackend.WaylandOnly
                or PlatformBackend.WaylandThenX11
                or PlatformBackend.X11Only
                => throw new PlatformNotSupportedException($"The platform backend {backend} has no implementation."),
            _ => throw new ArgumentOutOfRangeException(nameof(backend)),
        };
}
