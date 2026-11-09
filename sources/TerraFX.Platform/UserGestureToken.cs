// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Platform;

/// <summary>Identifies one input event that may authorize a platform operation.</summary>
public readonly record struct UserGestureToken
{
    private readonly uint _nativeInputSerial;

    /// <summary>Initializes an event-scoped token issued by the platform owner.</summary>
    /// <param name="eventSequence">The positive sequence of the originating input event.</param>
    /// <param name="window">The exact window that received the originating input event.</param>
    /// <param name="nativeInputSerial">The nonzero native input serial for the originating event.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="eventSequence"/> is not positive or <paramref name="nativeInputSerial"/> is zero.</exception>
    /// <exception cref="ArgumentException"><paramref name="window"/> is invalid.</exception>
    internal UserGestureToken(long eventSequence, WindowHandle window, uint nativeInputSerial)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(eventSequence);
        ArgumentOutOfRangeException.ThrowIfZero(nativeInputSerial);

        if (!window.IsValid)
        {
            throw new ArgumentException("A user gesture requires a valid originating window.", nameof(window));
        }

        EventSequence = eventSequence;
        Window = window;
        _nativeInputSerial = nativeInputSerial;
    }

    /// <summary>Gets the sequence of the originating input event.</summary>
    public long EventSequence { get; }

    /// <summary>Gets the exact window that received the originating input event.</summary>
    public WindowHandle Window { get; }

    internal uint NativeInputSerial => _nativeInputSerial;
}
