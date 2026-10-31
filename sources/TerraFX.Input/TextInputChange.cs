// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Text;
using TerraFX;

namespace TerraFX.Input;

/// <summary>Stores one immutable composition, edit, commit, or rejection record.</summary>
public readonly struct TextInputChange : IEquatable<TextInputChange>
{
    private const int MaximumTextBytes = 4096;

    internal TextInputChange(
        TextInputChangeKind kind,
        string? text = null,
        int? selectionStart = null,
        int? selectionLength = null,
        int? editStart = null,
        int? deleteLength = null,
        TextCompositionCancelReason? cancellationReason = null,
        Diagnostic? diagnostic = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (text is not null)
        {
            if (!IsValidUtf16(text))
            {
                throw new ArgumentException("Text changes must contain valid UTF-16.", nameof(text));
            }

            var textBytes = Encoding.UTF8.GetByteCount(text);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(textBytes, MaximumTextBytes, nameof(text));
        }

        switch (kind)
        {
            case TextInputChangeKind.CompositionStarted:
            case TextInputChangeKind.CompositionEnded:
            {
                EnsureNoPayload(text, selectionStart, selectionLength, editStart, deleteLength, cancellationReason, diagnostic);
                break;
            }

            case TextInputChangeKind.CompositionUpdated:
            {
                if ((text is null)
                    || (selectionStart is null)
                    || (selectionLength is null)
                    || (editStart is not null)
                    || (deleteLength is not null)
                    || (cancellationReason is not null)
                    || (diagnostic is not null))
                {
                    throw new ArgumentException("A composition update requires text and its UTF-16 selection only.");
                }

                ArgumentOutOfRangeException.ThrowIfNegative(selectionStart.Value);
                ArgumentOutOfRangeException.ThrowIfNegative(selectionLength.Value);

                if ((selectionStart.Value > text.Length)
                    || (selectionLength.Value > text.Length - selectionStart.Value)
                    || SplitsSurrogatePair(text, selectionStart.Value)
                    || SplitsSurrogatePair(text, selectionStart.Value + selectionLength.Value))
                {
                    throw new ArgumentOutOfRangeException(nameof(selectionStart), "The preedit selection must be in range and cannot split a surrogate pair.");
                }

                break;
            }

            case TextInputChangeKind.TextEditRequested:
            {
                if ((text is not null)
                    || (selectionStart is not null)
                    || (selectionLength is not null)
                    || (editStart is null)
                    || (deleteLength is null)
                    || (cancellationReason is not null)
                    || (diagnostic is not null))
                {
                    throw new ArgumentException("A text edit request requires only its deletion range.");
                }

                ArgumentOutOfRangeException.ThrowIfNegative(editStart.Value);
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deleteLength.Value);
                break;
            }

            case TextInputChangeKind.TextCommitted:
            {
                if (string.IsNullOrEmpty(text)
                    || (selectionStart is not null)
                    || (selectionLength is not null)
                    || (cancellationReason is not null)
                    || (diagnostic is not null)
                    || ((editStart is null) != (deleteLength is null)))
                {
                    throw new ArgumentException("A text commit requires nonempty text and an optional complete replacement range.");
                }

                if (editStart is { } commitStart)
                {
                    ArgumentOutOfRangeException.ThrowIfNegative(commitStart);
                    ArgumentOutOfRangeException.ThrowIfNegative(deleteLength.GetValueOrDefault());
                }

                break;
            }

            case TextInputChangeKind.CompositionCanceled:
            {
                if ((text is not null)
                    || (selectionStart is not null)
                    || (selectionLength is not null)
                    || (editStart is not null)
                    || (deleteLength is not null)
                    || (cancellationReason is null)
                    || !Enum.IsDefined(cancellationReason.Value)
                    || (diagnostic is not null))
                {
                    throw new ArgumentException("A composition cancellation requires only a defined reason.");
                }

                break;
            }

            case TextInputChangeKind.TextEditRejected:
            case TextInputChangeKind.StaleMirror:
            {
                if ((text is not null)
                    || (selectionStart is not null)
                    || (selectionLength is not null)
                    || (editStart is not null)
                    || (deleteLength is not null)
                    || (cancellationReason is not null)
                    || (diagnostic is null))
                {
                    throw new ArgumentException("A text rejection requires only its diagnostic.");
                }

                break;
            }

            default:
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        Kind = kind;
        Text = text;
        SelectionStart = selectionStart;
        SelectionLength = selectionLength;
        EditStart = editStart;
        DeleteLength = deleteLength;
        CancellationReason = cancellationReason;
        Diagnostic = diagnostic;
    }

    /// <summary>Gets the optional composition cancellation reason.</summary>
    public TextCompositionCancelReason? CancellationReason { get; }

    /// <summary>Gets the optional edit deletion length.</summary>
    public int? DeleteLength { get; }

    /// <summary>Gets the optional rejection diagnostic.</summary>
    public Diagnostic? Diagnostic { get; }

    /// <summary>Gets the optional edit start within the surrounding-text snapshot.</summary>
    public int? EditStart { get; }

    /// <summary>Gets the change kind.</summary>
    public TextInputChangeKind Kind { get; }

    /// <summary>Gets the optional UTF-16 composition selection length.</summary>
    public int? SelectionLength { get; }

    /// <summary>Gets the optional UTF-16 composition selection start.</summary>
    public int? SelectionStart { get; }

    /// <summary>Gets the optional valid UTF-16 text payload.</summary>
    public string? Text { get; }

    internal bool ChangesDocument
        => Kind is TextInputChangeKind.TextEditRequested or TextInputChangeKind.TextCommitted;

    internal int Utf8TextBytes => (Text is null) ? 0 : Encoding.UTF8.GetByteCount(Text);

    /// <summary>Negates text-change equality.</summary>
    public static bool operator !=(TextInputChange left, TextInputChange right) => !left.Equals(right);

    /// <summary>Compares all immutable text-change fields.</summary>
    public static bool operator ==(TextInputChange left, TextInputChange right) => left.Equals(right);

    private static void EnsureNoPayload(
        string? text,
        int? selectionStart,
        int? selectionLength,
        int? editStart,
        int? deleteLength,
        TextCompositionCancelReason? cancellationReason,
        Diagnostic? diagnostic)
    {
        if ((text is not null)
            || (selectionStart is not null)
            || (selectionLength is not null)
            || (editStart is not null)
            || (deleteLength is not null)
            || (cancellationReason is not null)
            || (diagnostic is not null))
        {
            throw new ArgumentException("This text change kind does not carry payload fields.");
        }
    }

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

    /// <inheritdoc />
    public bool Equals(TextInputChange other)
        => (Kind == other.Kind)
            && StringComparer.Ordinal.Equals(Text, other.Text)
            && (SelectionStart == other.SelectionStart)
            && (SelectionLength == other.SelectionLength)
            && (EditStart == other.EditStart)
            && (DeleteLength == other.DeleteLength)
            && (CancellationReason == other.CancellationReason)
            && ReferenceEquals(Diagnostic, other.Diagnostic);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TextInputChange other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(Kind, Text, SelectionStart, SelectionLength, EditStart, DeleteLength, CancellationReason, Diagnostic);
}
