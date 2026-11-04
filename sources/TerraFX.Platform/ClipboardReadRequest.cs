// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Platform;

/// <summary>Contains one asynchronous native clipboard-text read request.</summary>
public sealed class ClipboardReadRequest
{
    /// <summary>Initializes an immutable bounded clipboard read request.</summary>
    /// <param name="window">The live target window owned by the platform host.</param>
    /// <param name="maximumUtf8Bytes">The maximum UTF-8-equivalent result size, from one byte to one MiB.</param>
    /// <param name="gestureToken">The optional user-gesture authorization for platforms that require it.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumUtf8Bytes"/> is outside its supported range.</exception>
    /// <exception cref="ArgumentException"><paramref name="window"/> is invalid or the gesture belongs to another window.</exception>
    public ClipboardReadRequest(WindowHandle window, int maximumUtf8Bytes, UserGestureToken? gestureToken)
    {
        if (!window.IsValid)
        {
            throw new ArgumentException("Clipboard access requires a valid target window.", nameof(window));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumUtf8Bytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumUtf8Bytes, ClipboardTextService.MaximumUtf8Bytes);
        ClipboardTextService.ValidateGestureWindow(window, gestureToken);
        Window = window;
        MaximumUtf8Bytes = maximumUtf8Bytes;
        GestureToken = gestureToken;
    }

    /// <summary>Gets the optional user-gesture authorization.</summary>
    public UserGestureToken? GestureToken { get; }

    /// <summary>Gets the maximum UTF-8-equivalent result size.</summary>
    public int MaximumUtf8Bytes { get; }

    /// <summary>Gets the target window.</summary>
    public WindowHandle Window { get; }
}
