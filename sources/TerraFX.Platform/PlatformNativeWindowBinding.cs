// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX;

namespace TerraFX.Platform;

internal readonly record struct PlatformNativeWindowBinding(
    WindowHandle Window,
    PlatformNativeSurfaceKind Kind,
    nint NativeDisplayOrInstance,
    nint NativeWindowOrSurface);
