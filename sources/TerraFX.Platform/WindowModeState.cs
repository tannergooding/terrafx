// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Platform;

/// <summary>Contains the latest requested and natively observed window presentation modes.</summary>
public sealed class WindowModeState
{
    /// <summary>Initializes an immutable window mode state snapshot.</summary>
    /// <param name="window">The owning host's window identity.</param>
    /// <param name="requestedMode">The latest admitted mode request.</param>
    /// <param name="observedMode">The latest native observation, or null until one is available.</param>
    /// <exception cref="ArgumentException"><paramref name="window"/> is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A mode value is invalid.</exception>
    internal WindowModeState(WindowHandle window, WindowMode requestedMode, WindowMode? observedMode)
    {
        if (!window.IsValid)
        {
            throw new ArgumentException("The window handle is invalid.", nameof(window));
        }

        if (!Enum.IsDefined(requestedMode))
        {
            throw new ArgumentOutOfRangeException(nameof(requestedMode));
        }

        if (observedMode is { } mode && !Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(observedMode));
        }

        Window = window;
        RequestedMode = requestedMode;
        ObservedMode = observedMode;
    }

    /// <summary>Gets whether the latest native observation matches the latest admitted request.</summary>
    public bool IsConfirmed => ObservedMode == RequestedMode;

    /// <summary>Gets the latest mode confirmed by the native platform, or null if unobserved.</summary>
    public WindowMode? ObservedMode { get; }

    /// <summary>Gets the latest admitted mode request.</summary>
    public WindowMode RequestedMode { get; }

    /// <summary>Gets the owning host's window identity.</summary>
    public WindowHandle Window { get; }
}
