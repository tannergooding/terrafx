// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

/// <summary>Identifies one kind of normalized physical input event.</summary>
public enum PhysicalInputEventKind
{
    /// <summary>Invalid event kind.</summary>
    None = 0,

    /// <summary>A keyboard control became down.</summary>
    KeyDown = 1,

    /// <summary>A keyboard control became up.</summary>
    KeyUp = 2,

    /// <summary>The operating system repeated a held keyboard control.</summary>
    KeyRepeat = 3,

    /// <summary>A pointer button became down.</summary>
    PointerButtonDown = 4,

    /// <summary>A pointer button became up.</summary>
    PointerButtonUp = 5,

    /// <summary>An absolute window-local pointer position.</summary>
    PointerAbsoluteMotion = 6,

    /// <summary>A relative pointer displacement.</summary>
    PointerRelativeMotion = 7,

    /// <summary>Vertical pointer scroll.</summary>
    VerticalScroll = 8,

    /// <summary>Horizontal pointer scroll.</summary>
    HorizontalScroll = 9,

    /// <summary>A gamepad button became down.</summary>
    GamepadButtonDown = 10,

    /// <summary>A gamepad button became up.</summary>
    GamepadButtonUp = 11,

    /// <summary>A gamepad axis value changed.</summary>
    GamepadAxis = 12,

    /// <summary>A gamepad stick value changed.</summary>
    GamepadStick = 13,

    /// <summary>A gamepad trigger value changed.</summary>
    GamepadTrigger = 14,

    /// <summary>A window gained focus.</summary>
    FocusGained = 15,

    /// <summary>A window lost focus.</summary>
    FocusLost = 16,

    /// <summary>The pointer entered a window.</summary>
    WindowEntered = 17,

    /// <summary>The pointer left a window.</summary>
    WindowLeft = 18,

    /// <summary>A physical device was added.</summary>
    DeviceAdded = 19,

    /// <summary>A physical device was removed.</summary>
    DeviceRemoved = 20,

    /// <summary>A physical device's player assignment changed.</summary>
    DeviceAssigned = 21,

    /// <summary>The complete current state of one input source was observed.</summary>
    StateReconciliation = 22,
}
