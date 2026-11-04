// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Platform;

/// <summary>Contains the complete outcome of one clipboard text write.</summary>
public sealed class ClipboardWriteResponse
{
    internal ClipboardWriteResponse(ClipboardStatus status, Diagnostic? diagnostic)
    {
        ArgumentOutOfRangeException.ThrowIfNegative((int)status);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((int)status, (int)ClipboardStatus.Canceled);
        Status = status;
        Diagnostic = diagnostic;
    }

    /// <summary>Gets the diagnostic for an expected native selection result.</summary>
    public Diagnostic? Diagnostic { get; }

    /// <summary>Gets the complete write outcome.</summary>
    public ClipboardStatus Status { get; }
}
