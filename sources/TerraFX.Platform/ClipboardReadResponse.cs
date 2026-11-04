// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Platform;

/// <summary>Contains the complete outcome of one clipboard text read.</summary>
public sealed class ClipboardReadResponse
{
    internal ClipboardReadResponse(ClipboardStatus status, string? text, Diagnostic? diagnostic)
    {
        ArgumentOutOfRangeException.ThrowIfNegative((int)status);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((int)status, (int)ClipboardStatus.Canceled);

        if ((status is ClipboardStatus.Completed) != (text is not null))
        {
            throw new ArgumentException("Clipboard text is present exactly when the read completed.", nameof(text));
        }

        Status = status;
        Text = text;
        Diagnostic = diagnostic;
    }

    /// <summary>Gets the diagnostic for an expected native selection result.</summary>
    public Diagnostic? Diagnostic { get; }

    /// <summary>Gets the complete read outcome.</summary>
    public ClipboardStatus Status { get; }

    /// <summary>Gets the complete text, including an empty string for a genuinely empty selection.</summary>
    public string? Text { get; }
}
