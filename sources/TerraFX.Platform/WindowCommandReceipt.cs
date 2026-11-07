// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Platform;

/// <summary>Describes the owner-thread application outcome of a window command.</summary>
public sealed class WindowCommandReceipt
{
    /// <summary>Initializes an owner-thread window command result.</summary>
    /// <param name="operation">The command operation.</param>
    /// <param name="window">The command's target window.</param>
    /// <param name="status">The command's application status.</param>
    /// <param name="ownerSequence">The positive host sequence, or zero when no command was applied.</param>
    /// <param name="diagnostic">The optional native or domain diagnostic.</param>
    /// <exception cref="ArgumentOutOfRangeException">The operation, status, or owner sequence is invalid.</exception>
    /// <exception cref="ArgumentException"><paramref name="window"/> is invalid.</exception>
    internal WindowCommandReceipt(
        WindowCommandKind operation,
        WindowHandle window,
        WindowCommandStatus status,
        long ownerSequence,
        Diagnostic? diagnostic)
    {
        if (!Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        if (!window.IsValid)
        {
            throw new ArgumentException("The window handle is invalid.", nameof(window));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(ownerSequence);

        if ((status is WindowCommandStatus.QueueFull) != (ownerSequence == 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(ownerSequence),
                ownerSequence,
                "Only a queue-full result has no owner sequence.");
        }

        Operation = operation;
        Window = window;
        Status = status;
        OwnerSequence = ownerSequence;
        Diagnostic = diagnostic;
    }

    /// <summary>Gets the optional native or domain diagnostic.</summary>
    public Diagnostic? Diagnostic { get; }

    /// <summary>Gets the requested operation.</summary>
    public WindowCommandKind Operation { get; }

    /// <summary>Gets the positive host sequence, or zero if the command was not admitted.</summary>
    public long OwnerSequence { get; }

    /// <summary>Gets the application status.</summary>
    public WindowCommandStatus Status { get; }

    /// <summary>Gets the target window.</summary>
    public WindowHandle Window { get; }
}
