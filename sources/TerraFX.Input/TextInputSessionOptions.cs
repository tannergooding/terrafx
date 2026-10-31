// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Numerics;

namespace TerraFX.Input;

/// <summary>Copies and validates the UI-owned text snapshot and caret for one window session.</summary>
public sealed class TextInputSessionOptions
{
    private TextInputHints _hints;

    /// <summary>Initializes a complete text-session snapshot.</summary>
    /// <param name="window">The exact engine-issued window identity.</param>
    /// <param name="purpose">The requested text purpose.</param>
    /// <param name="documentRevision">The positive UI document revision.</param>
    /// <param name="surroundingText">The optional valid UTF-16 snapshot.</param>
    /// <param name="selectionStart">The UTF-16 selection start within the snapshot.</param>
    /// <param name="selectionLength">The UTF-16 selection length.</param>
    /// <param name="caretPosition">The finite window-local logical caret position.</param>
    /// <param name="caretSize">The finite, nonnegative window-local logical caret size.</param>
    /// <exception cref="ArgumentException">The window, purpose, text, selection, or caret is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The document revision or selection extent is invalid.</exception>
    public TextInputSessionOptions(
        WindowHandle window,
        TextInputPurpose purpose,
        long documentRevision,
        string? surroundingText,
        int selectionStart,
        int selectionLength,
        Vector2 caretPosition,
        Vector2 caretSize)
    {
        if (!window.IsValid)
        {
            throw new ArgumentException("A valid window identity is required.", nameof(window));
        }

        if (!Enum.IsDefined(purpose))
        {
            throw new ArgumentOutOfRangeException(nameof(purpose));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(documentRevision);
        ArgumentOutOfRangeException.ThrowIfNegative(selectionStart);
        ArgumentOutOfRangeException.ThrowIfNegative(selectionLength);

        if (surroundingText is null)
        {
            if ((selectionStart != 0) || (selectionLength != 0))
            {
                throw new ArgumentException("A missing surrounding-text snapshot requires an empty selection.", nameof(selectionStart));
            }
        }
        else
        {
            if (surroundingText.Length > 65_536)
            {
                throw new ArgumentOutOfRangeException(nameof(surroundingText), "The text snapshot cannot exceed 65,536 UTF-16 code units.");
            }

            if (!IsValidUtf16(surroundingText))
            {
                throw new ArgumentException("The text snapshot must contain valid UTF-16.", nameof(surroundingText));
            }

            if ((selectionStart > surroundingText.Length)
                || (selectionLength > surroundingText.Length - selectionStart))
            {
                throw new ArgumentOutOfRangeException(nameof(selectionLength), "The selection must be within the text snapshot.");
            }

            var selectionEnd = selectionStart + selectionLength;

            if (SplitsSurrogatePair(surroundingText, selectionStart)
                || SplitsSurrogatePair(surroundingText, selectionEnd))
            {
                throw new ArgumentException("Selection endpoints cannot split a surrogate pair.", nameof(selectionStart));
            }
        }

        if (!float.IsFinite(caretPosition.X) || !float.IsFinite(caretPosition.Y))
        {
            throw new ArgumentException("The caret position must be finite.", nameof(caretPosition));
        }

        if (!float.IsFinite(caretSize.X)
            || !float.IsFinite(caretSize.Y)
            || (caretSize.X < 0.0f)
            || (caretSize.Y < 0.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(caretSize), "The caret size must be finite and nonnegative.");
        }

        Window = window;
        Purpose = purpose;
        DocumentRevision = documentRevision;
        SurroundingText = surroundingText;
        SelectionStart = selectionStart;
        SelectionLength = selectionLength;
        CaretPosition = caretPosition;
        CaretSize = caretSize;
    }

    /// <summary>Gets the window-local logical caret position.</summary>
    public Vector2 CaretPosition { get; }

    /// <summary>Gets the window-local logical caret size.</summary>
    public Vector2 CaretSize { get; }

    /// <summary>Gets the UI document revision represented by the snapshot.</summary>
    public long DocumentRevision { get; }

    /// <summary>Gets the immutable native text-entry hints.</summary>
    public TextInputHints Hints
    {
        get
        {
            return _hints;
        }

        init
        {
            if ((((uint)value) & ~((uint)TextInputHints.SpellCheck
                | (uint)TextInputHints.AutoCorrect
                | (uint)TextInputHints.Multiline
                | (uint)TextInputHints.Sensitive)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(Hints));
            }

            _hints = value;
        }
    }

    /// <summary>Gets the requested text purpose.</summary>
    public TextInputPurpose Purpose { get; }

    /// <summary>Gets the UTF-16 selection length.</summary>
    public int SelectionLength { get; }

    /// <summary>Gets the UTF-16 selection start.</summary>
    public int SelectionStart { get; }

    /// <summary>Gets the optional copied UI text snapshot.</summary>
    public string? SurroundingText { get; }

    /// <summary>Gets the exact engine-issued window identity.</summary>
    public WindowHandle Window { get; }

    private static bool IsValidUtf16(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]))
            {
                if ((index + 1 >= text.Length) || !char.IsLowSurrogate(text[index + 1]))
                {
                    return false;
                }

                index++;
            }
            else if (char.IsLowSurrogate(text[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SplitsSurrogatePair(string text, int index)
        => (index > 0)
            && (index < text.Length)
            && char.IsHighSurrogate(text[index - 1])
            && char.IsLowSurrogate(text[index]);
}
