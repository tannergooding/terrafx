// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX;
using TerraFX.Input;
using TerraFX.Numerics;

namespace TerraFX.Platform;

internal readonly record struct PlatformInputSample(
    PhysicalInputEventKind Kind,
    WindowHandle? Window,
    nint NativeDeviceIdentity,
    PlatformInputDeviceKind DeviceKind,
    ushort? NativeUsagePage = null,
    ushort? NativeUsage = null,
    uint? NativeScanCode = null,
    uint? NativeVirtualKey = null,
    MouseButtonValue? MouseButton = null,
    bool? IsDown = null,
    Vector2? PointerPosition = null,
    Vector2? PointerDelta = null,
    Vector2? PointerContentScale = null,
    float? ScrollValue = null,
    PointerScrollUnit? ScrollUnit = null,
    long? NativeTimestamp = null,
    string? NativeClockDomain = null,
    PhysicalInputEventDiagnostic Diagnostics = PhysicalInputEventDiagnostic.None,
    long MonotonicTimestamp = 0);
