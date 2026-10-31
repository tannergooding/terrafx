// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

/// <summary>Identifies one immutable text-session change record.</summary>
public enum TextInputChangeKind
{
    /// <summary>An invalid default value that is never admitted in a batch.</summary>
    Invalid = 0,

    /// <summary>A text composition began.</summary>
    CompositionStarted = 1,

    /// <summary>The replacement preedit string or selection changed.</summary>
    CompositionUpdated = 2,

    /// <summary>The UI should delete the selected surrounding-text range.</summary>
    TextEditRequested = 3,

    /// <summary>Nonempty committed Unicode text is available.</summary>
    TextCommitted = 4,

    /// <summary>The current text composition ended normally.</summary>
    CompositionEnded = 5,

    /// <summary>The current text composition was canceled.</summary>
    CompositionCanceled = 6,

    /// <summary>A native text edit could not be applied to the supplied snapshot.</summary>
    TextEditRejected = 7,

    /// <summary>The native mirror no longer matches an authoritative UI snapshot.</summary>
    StaleMirror = 8
}
