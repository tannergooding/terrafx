// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Platform;

/// <summary>Describes a requested or observed window mode state change.</summary>
public sealed class WindowModeChangedEventArgs : EventArgs
{
    /// <summary>Initializes a window mode change notification.</summary>
    /// <param name="state">The latest requested/observed state.</param>
    /// <param name="hostSequence">The positive host event sequence.</param>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="hostSequence"/> is not positive.</exception>
    internal WindowModeChangedEventArgs(WindowModeState state, long hostSequence)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hostSequence);
        HostSequence = hostSequence;
        State = state;
    }

    /// <summary>Gets the host event sequence for the state change.</summary>
    public long HostSequence { get; }

    /// <summary>Gets the latest requested/observed mode state.</summary>
    public WindowModeState State { get; }
}
