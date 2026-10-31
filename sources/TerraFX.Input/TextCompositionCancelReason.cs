// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

/// <summary>Identifies why a text composition was canceled.</summary>
public enum TextCompositionCancelReason
{
    /// <summary>The window lost focus.</summary>
    FocusLost = 0,

    /// <summary>The native text source canceled the composition.</summary>
    NativeCanceled = 1,

    /// <summary>The UI disposed the text session.</summary>
    SessionDisposed = 2,

    /// <summary>The text session was replaced.</summary>
    SessionReplaced = 3,

    /// <summary>The text recipient exceeded its bounded output budget.</summary>
    Overflow = 4
}
