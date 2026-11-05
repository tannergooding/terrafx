// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Platform;

internal readonly record struct PlatformGamepadState(
    uint Buttons,
    float LeftTrigger,
    float RightTrigger,
    float LeftThumbstickX,
    float LeftThumbstickY,
    float RightThumbstickX,
    float RightThumbstickY);
