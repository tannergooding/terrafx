// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Platform;

internal static class ClipboardTextPayload
{
    internal static ClipboardReadResponse Read(
        WindowHandle window,
        ReadOnlySpan<char> characters,
        int maximumUtf8Bytes,
        bool hasUnscannedData)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumUtf8Bytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumUtf8Bytes, ClipboardTextService.MaximumUtf8Bytes);
        var textCharacterCount = 0;
        var utf8ByteCount = 0;
        var terminated = false;

        while (textCharacterCount < characters.Length)
        {
            var current = characters[textCharacterCount];

            if (current is '\0')
            {
                terminated = true;
                break;
            }

            if (char.IsHighSurrogate(current))
            {
                var nextCharacterIndex = textCharacterCount + 1;

                if (nextCharacterIndex >= characters.Length)
                {
                    break;
                }

                if (!char.IsLowSurrogate(characters[nextCharacterIndex]))
                {
                    return CreateInvalidDataResponse(window, "The clipboard text contains an unpaired UTF-16 surrogate.");
                }

                utf8ByteCount = checked(utf8ByteCount + 4);
                textCharacterCount += 2;
            }
            else if (char.IsLowSurrogate(current))
            {
                return CreateInvalidDataResponse(window, "The clipboard text contains an unpaired UTF-16 surrogate.");
            }
            else
            {
                utf8ByteCount = checked(utf8ByteCount + (current <= 0x7F ? 1 : current <= 0x7FF ? 2 : 3));
                textCharacterCount++;
            }

            if (utf8ByteCount > maximumUtf8Bytes)
            {
                return CreateTooLargeResponse(window);
            }
        }

        if (!terminated)
        {
            return hasUnscannedData
                ? CreateTooLargeResponse(window)
                : CreateInvalidDataResponse(window, "The clipboard text is not NUL terminated.");
        }

        return new ClipboardReadResponse(
            ClipboardStatus.Completed,
            new string(characters[..textCharacterCount]),
            diagnostic: null);
    }

    private static ClipboardReadResponse CreateInvalidDataResponse(WindowHandle window, string message)
        => new(
            ClipboardStatus.InvalidData,
            text: null,
            ClipboardTextService.CreateDiagnostic(
                "ClipboardInvalidData",
                window,
                "ClipboardText.ReadTextAsync",
                nativeResult: null,
                message));

    private static ClipboardReadResponse CreateTooLargeResponse(WindowHandle window)
        => new(
            ClipboardStatus.TooLarge,
            text: null,
            ClipboardTextService.CreateDiagnostic(
                "ClipboardTooLarge",
                window,
                "ClipboardText.ReadTextAsync",
                nativeResult: null,
                "The complete clipboard text exceeds the request byte limit."));
}
