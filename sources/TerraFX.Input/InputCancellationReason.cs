// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

/// <summary>Identifies why an active action was canceled rather than physically released.</summary>
public enum InputCancellationReason
{
    /// <summary>The assigned device generation was removed.</summary>
    DeviceRemoved = 0,

    /// <summary>The native window lost focus.</summary>
    FocusLost = 1,

    /// <summary>A higher-priority context masked the binding.</summary>
    ContextMasked = 2,

    /// <summary>A higher-priority context consumed the physical event.</summary>
    InputConsumed = 3,

    /// <summary>The action map revision rebound the action.</summary>
    Rebound = 4,

    /// <summary>The recipient lost input state because its bounded inbox overflowed.</summary>
    InputOverflow = 5,

    /// <summary>The simulation timeline was paused.</summary>
    Paused = 6,

    /// <summary>The input recipient was disposed.</summary>
    RecipientDisposed = 7,
}
