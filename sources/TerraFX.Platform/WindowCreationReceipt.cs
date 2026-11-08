// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Platform;

/// <summary>Contains the result of an owner-applied window creation request.</summary>
public sealed class WindowCreationReceipt
{
    internal WindowCreationReceipt(WindowCreationStatus status, WindowHandle? window, Diagnostic? rejection)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (status == WindowCreationStatus.Created)
        {
            if ((window is not WindowHandle createdWindow)
                || !createdWindow.IsValid
                || (rejection is not null))
            {
                throw new ArgumentException("A created receipt requires a valid window and no rejection.");
            }
        }
        else if ((window is not null) || (rejection is null))
        {
            throw new ArgumentException("A rejected receipt requires a diagnostic and no window.");
        }

        Status = status;
        Window = window;
        Rejection = rejection;
    }

    /// <summary>Gets the diagnostic for a rejected request, or null when the window was created.</summary>
    public Diagnostic? Rejection { get; }

    /// <summary>Gets the creation result.</summary>
    public WindowCreationStatus Status { get; }

    /// <summary>Gets the created window, or null when the request was rejected.</summary>
    public WindowHandle? Window { get; }
}
