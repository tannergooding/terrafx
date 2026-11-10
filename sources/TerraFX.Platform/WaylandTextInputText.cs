// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Text;
using TerraFX.Input;

namespace TerraFX.Platform;

internal static class WaylandTextInputText
{
    internal const int MaximumSurroundingTextBytes = 4000;

    private static readonly UTF8Encoding s_utf8Encoding = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal static ImmutableArray<TextInputChange> CreateCompositionChanges(
        bool compositionActive,
        string? committedText,
        bool hasPreedit,
        string? preeditText,
        int cursorBegin,
        int cursorEnd,
        out bool resultingCompositionActive)
    {
        var changes = ImmutableArray.CreateBuilder<TextInputChange>(4);
        var currentPreedit = hasPreedit ? preeditText ?? string.Empty : string.Empty;

        if (!string.IsNullOrEmpty(committedText))
        {
            changes.Add(new TextInputChange(TextInputChangeKind.TextCommitted, committedText));
        }

        if (compositionActive
            && ((!string.IsNullOrEmpty(committedText))
                || (hasPreedit && (currentPreedit.Length == 0))))
        {
            changes.Add(new TextInputChange(TextInputChangeKind.CompositionEnded));
            compositionActive = false;
        }

        if (hasPreedit && (currentPreedit.Length > 0))
        {
            if (!compositionActive)
            {
                changes.Add(new TextInputChange(TextInputChangeKind.CompositionStarted));
                compositionActive = true;
            }

            if (!TryGetPreeditSelection(currentPreedit, cursorBegin, cursorEnd, out var selectionStart, out var selectionLength))
            {
                throw new InvalidOperationException("The Wayland compositor returned an invalid UTF-8 preedit selection.");
            }

            changes.Add(new TextInputChange(
                TextInputChangeKind.CompositionUpdated,
                currentPreedit,
                selectionStart,
                selectionLength));
        }

        resultingCompositionActive = compositionActive;
        return changes.ToImmutable();
    }

    internal static WaylandTextInputSurroundingText? CreateSurroundingText(
        string? text,
        int selectionStart,
        int selectionLength)
    {
        if (text is null)
        {
            return null;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(selectionStart);
        ArgumentOutOfRangeException.ThrowIfNegative(selectionLength);

        if ((selectionStart > text.Length)
            || (selectionLength > text.Length - selectionStart)
            || SplitsSurrogatePair(text, selectionStart)
            || SplitsSurrogatePair(text, selectionStart + selectionLength))
        {
            throw new ArgumentOutOfRangeException(nameof(selectionStart));
        }

        var selectionStartByte = s_utf8Encoding.GetByteCount(text.AsSpan(0, selectionStart));
        var selectionEndByte = s_utf8Encoding.GetByteCount(text.AsSpan(0, selectionStart + selectionLength));
        var totalBytes = s_utf8Encoding.GetByteCount(text);

        if (totalBytes <= MaximumSurroundingTextBytes)
        {
            return new WaylandTextInputSurroundingText(text, 0, selectionStartByte, selectionEndByte);
        }

        var selectionBytes = selectionEndByte - selectionStartByte;

        if (selectionBytes > MaximumSurroundingTextBytes)
        {
            return null;
        }

        var availableContextBytes = MaximumSurroundingTextBytes - selectionBytes;
        var targetStartByte = Math.Max(0, selectionStartByte - (availableContextBytes / 2));
        var minimumStartByte = Math.Max(0, selectionEndByte - MaximumSurroundingTextBytes);
        var start = FindByteBoundaryAtOrBefore(text, targetStartByte);

        if (start.ByteOffset < minimumStartByte)
        {
            start = FindByteBoundaryAtOrAfter(text, minimumStartByte);
        }

        var maximumEndByte = checked(start.ByteOffset + MaximumSurroundingTextBytes);
        var end = FindByteBoundaryAtOrBefore(text, maximumEndByte);

        if (end.ByteOffset < selectionEndByte)
        {
            start = new Utf8Boundary(selectionStart, selectionStartByte);
            end = new Utf8Boundary(selectionStart + selectionLength, selectionEndByte);
        }

        var surroundingText = text[start.Utf16Offset..end.Utf16Offset];
        var cursorByteOffset = selectionStartByte - start.ByteOffset;
        var anchorByteOffset = selectionEndByte - start.ByteOffset;

        return new WaylandTextInputSurroundingText(
            surroundingText,
            start.Utf16Offset,
            cursorByteOffset,
            anchorByteOffset);
    }

    internal static string DecodeUtf8(ReadOnlySpan<byte> bytes)
        => s_utf8Encoding.GetString(bytes);

    internal static string? GetNativeSurroundingText(TextInputSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return (options.Purpose is TextInputPurpose.Password)
            || ((options.Hints & TextInputHints.Sensitive) is TextInputHints.Sensitive)
                ? null
                : options.SurroundingText;
    }

    internal static bool TryGetPreeditSelection(
        string text,
        int cursorBegin,
        int cursorEnd,
        out int selectionStart,
        out int selectionLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        selectionStart = 0;
        selectionLength = 0;

        if ((cursorBegin == -1) && (cursorEnd == -1))
        {
            selectionStart = text.Length;
            return true;
        }

        if ((cursorBegin < 0) || (cursorEnd < 0)
            || !TryGetUtf16Offset(text, cursorBegin, out var begin)
            || !TryGetUtf16Offset(text, cursorEnd, out var end))
        {
            return false;
        }

        selectionStart = Math.Min(begin, end);
        selectionLength = Math.Abs(end - begin);
        return true;
    }

    internal static bool TryMapDelete(
        int snapshotTextLength,
        WaylandTextInputSurroundingText surroundingText,
        uint beforeLength,
        uint afterLength,
        out int editStart,
        out int deleteLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(snapshotTextLength);
        editStart = 0;
        deleteLength = 0;

        if ((beforeLength > int.MaxValue) || (afterLength > int.MaxValue))
        {
            return false;
        }

        var startByte = (long)surroundingText.CursorByteOffset - beforeLength;
        var endByte = (long)surroundingText.AnchorByteOffset + afterLength;

        if ((startByte < 0) || (endByte > s_utf8Encoding.GetByteCount(surroundingText.Text))
            || (startByte >= endByte)
            || !TryGetUtf16Offset(surroundingText.Text, checked((int)startByte), out var relativeStart)
            || !TryGetUtf16Offset(surroundingText.Text, checked((int)endByte), out var relativeEnd))
        {
            return false;
        }

        editStart = checked(surroundingText.SnapshotUtf16Offset + relativeStart);
        var editEnd = checked(surroundingText.SnapshotUtf16Offset + relativeEnd);

        if (editEnd > snapshotTextLength)
        {
            return false;
        }

        deleteLength = editEnd - editStart;
        return deleteLength > 0;
    }

    private static Utf8Boundary FindByteBoundaryAtOrAfter(string text, int byteOffset)
    {
        var current = new Utf8Boundary(0, 0);

        foreach (var rune in text.EnumerateRunes())
        {
            if (current.ByteOffset >= byteOffset)
            {
                return current;
            }

            current = new Utf8Boundary(
                checked(current.Utf16Offset + rune.Utf16SequenceLength),
                checked(current.ByteOffset + rune.Utf8SequenceLength));
        }

        return current;
    }

    private static Utf8Boundary FindByteBoundaryAtOrBefore(string text, int byteOffset)
    {
        var current = new Utf8Boundary(0, 0);

        foreach (var rune in text.EnumerateRunes())
        {
            var next = new Utf8Boundary(
                checked(current.Utf16Offset + rune.Utf16SequenceLength),
                checked(current.ByteOffset + rune.Utf8SequenceLength));

            if (next.ByteOffset > byteOffset)
            {
                break;
            }

            current = next;
        }

        return current;
    }

    private static bool SplitsSurrogatePair(string text, int index)
        => (index > 0)
            && (index < text.Length)
            && char.IsHighSurrogate(text[index - 1])
            && char.IsLowSurrogate(text[index]);

    private static bool TryGetUtf16Offset(string text, int byteOffset, out int utf16Offset)
    {
        utf16Offset = 0;
        var currentByteOffset = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            if (currentByteOffset == byteOffset)
            {
                return true;
            }

            currentByteOffset = checked(currentByteOffset + rune.Utf8SequenceLength);

            if (currentByteOffset > byteOffset)
            {
                return false;
            }

            utf16Offset = checked(utf16Offset + rune.Utf16SequenceLength);
        }

        return currentByteOffset == byteOffset;
    }

    private readonly record struct Utf8Boundary(int Utf16Offset, int ByteOffset);
}
