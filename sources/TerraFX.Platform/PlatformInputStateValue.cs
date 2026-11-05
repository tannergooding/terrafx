// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX.Input;
using TerraFX.Numerics;

namespace TerraFX.Platform;

internal readonly record struct PlatformInputStateValue(
    PlatformInputStateValueKind Kind,
    GamepadButtonValue? GamepadButton = null,
    bool? IsDown = null,
    GamepadAxis? Axis = null,
    float? AxisValue = null,
    GamepadStick? Stick = null,
    Vector2? StickValue = null,
    GamepadTrigger? Trigger = null,
    float? TriggerValue = null);
