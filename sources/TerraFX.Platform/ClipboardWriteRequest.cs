// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Platform;

/// <summary>Contains one asynchronous native clipboard-text write request.</summary>
public sealed class ClipboardWriteRequest
{
    /// <summary>Initializes an immutable bounded clipboard write request.</summary>
    /// <param name="window">The live target window owned by the platform host.</param>
    /// <param name="text">The complete text to publish.</param>
    /// <param name="maximumUtf8Bytes">The maximum UTF-8-equivalent payload size, from one byte to one MiB.</param>
    /// <param name="gestureToken">The optional user-gesture authorization for platforms that require it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumUtf8Bytes"/> is outside its supported range.</exception>
    /// <exception cref="ArgumentException"><paramref name="window"/> is invalid or the gesture belongs to another window.</exception>
    public ClipboardWriteRequest(
        WindowHandle window,
        string text,
        int maximumUtf8Bytes,
        UserGestureToken? gestureToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!window.IsValid)
        {
            throw new ArgumentException("Clipboard access requires a valid target window.", nameof(window));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumUtf8Bytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumUtf8Bytes, ClipboardTextService.MaximumUtf8Bytes);
        ClipboardTextService.ValidateGestureWindow(window, gestureToken);
        Window = window;
        Text = text;
        MaximumUtf8Bytes = maximumUtf8Bytes;
        GestureToken = gestureToken;
    }

    /// <summary>Gets the optional user-gesture authorization.</summary>
    public UserGestureToken? GestureToken { get; }

    /// <summary>Gets the maximum UTF-8-equivalent payload size.</summary>
    public int MaximumUtf8Bytes { get; }

    /// <summary>Gets the complete text to publish.</summary>
    public string Text { get; }

    /// <summary>Gets the target window.</summary>
    public WindowHandle Window { get; }
}
