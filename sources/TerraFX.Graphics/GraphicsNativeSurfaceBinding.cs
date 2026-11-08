// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

internal readonly record struct GraphicsNativeSurfaceBinding
{
    internal GraphicsNativeSurfaceBinding(
        GraphicsPlatformKind platformKind,
        nint nativeDisplayOrInstance,
        nint nativeWindowOrSurface,
        uint surfaceEpoch)
    {
        if ((platformKind == GraphicsPlatformKind.None) || !Enum.IsDefined(platformKind))
        {
            throw new ArgumentOutOfRangeException(nameof(platformKind));
        }

        if (nativeDisplayOrInstance == 0)
        {
            throw new ArgumentException("A native display or instance handle is required.", nameof(nativeDisplayOrInstance));
        }

        if (nativeWindowOrSurface == 0)
        {
            throw new ArgumentException("A native window or surface handle is required.", nameof(nativeWindowOrSurface));
        }

        ArgumentOutOfRangeException.ThrowIfZero(surfaceEpoch);

        PlatformKind = platformKind;
        NativeDisplayOrInstance = nativeDisplayOrInstance;
        NativeWindowOrSurface = nativeWindowOrSurface;
        SurfaceEpoch = surfaceEpoch;
    }

    internal nint NativeDisplayOrInstance { get; }

    internal nint NativeWindowOrSurface { get; }

    internal GraphicsPlatformKind PlatformKind { get; }

    internal uint SurfaceEpoch { get; }
}
