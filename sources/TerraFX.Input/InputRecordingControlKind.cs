// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

/// <summary>Identifies an input control operation stored in a recording.</summary>
public enum InputRecordingControlKind
{
    /// <summary>Cancels or pauses selected recipient input classes.</summary>
    CancelInput = 0,

    /// <summary>Reconciles recipient input state from a new-time snapshot.</summary>
    ReconcileInput = 1,

    /// <summary>Clears UI-disposition state for the recipient.</summary>
    ResetUiDisposition = 2,

    /// <summary>Removes one physical device generation from the recipient.</summary>
    RemovePhysicalDevice = 3,

    /// <summary>Changes one action map's active context mask.</summary>
    SetActiveContextMask = 4,
}
