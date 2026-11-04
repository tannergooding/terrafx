// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ClipboardTextResult = TerraFX.Platform.ClipboardStatus;

namespace TerraFX.Platform;

[SupportedOSPlatform("windows10.0.22000")]
internal sealed unsafe partial class WindowsPlatformBackend
{
    private static ClipboardReadResponse ReadOpenedClipboard(ClipboardReadRequest request)
    {
        if (!IsClipboardFormatAvailableNative(ClipboardFormatUnicodeText))
        {
            return new ClipboardReadResponse(
                ClipboardTextResult.NoSelection,
                text: null,
                ClipboardTextService.CreateDiagnostic(
                    "ClipboardNoSelection",
                    request.Window,
                    "ClipboardText.ReadTextAsync",
                    nativeResult: null,
                    "The clipboard does not currently contain Unicode text."));
        }

        var globalMemory = GetClipboardDataNative(ClipboardFormatUnicodeText);

        if (globalMemory == 0)
        {
            var error = Marshal.GetLastPInvokeError();
            return new ClipboardReadResponse(
                ClipboardTextResult.PermissionDenied,
                text: null,
                ClipboardTextService.CreateDiagnostic(
                    "ClipboardReadFailed",
                    request.Window,
                    "ClipboardText.ReadTextAsync",
                    error,
                    new Win32Exception(error).Message));
        }

        var byteCount = GlobalSizeNative(globalMemory);

        if ((byteCount < sizeof(char)) || ((byteCount % sizeof(char)) != 0))
        {
            return new ClipboardReadResponse(
                ClipboardTextResult.InvalidData,
                text: null,
                ClipboardTextService.CreateDiagnostic(
                    "ClipboardInvalidData",
                    request.Window,
                    "ClipboardText.ReadTextAsync",
                    nativeResult: null,
                    "The Windows clipboard returned an invalid UTF-16 allocation size."));
        }

        var memory = GlobalLockNative(globalMemory);

        if (memory == 0)
        {
            var error = Marshal.GetLastPInvokeError();
            return new ClipboardReadResponse(
                ClipboardTextResult.PermissionDenied,
                text: null,
                ClipboardTextService.CreateDiagnostic(
                    "ClipboardLockFailed",
                    request.Window,
                    "ClipboardText.ReadTextAsync",
                    error,
                    new Win32Exception(error).Message));
        }

        try
        {
            var totalCharacterCount = checked((ulong)(byteCount / sizeof(char)));
            var maximumScannedCharacterCount = checked((ulong)request.MaximumUtf8Bytes + 1);
            var scanCharacterCount = checked((int)Math.Min(totalCharacterCount, maximumScannedCharacterCount));
            return ClipboardTextPayload.Read(
                request.Window,
                new ReadOnlySpan<char>((char*)memory, scanCharacterCount),
                request.MaximumUtf8Bytes,
                totalCharacterCount > (ulong)scanCharacterCount);
        }
        finally
        {
            _ = GlobalUnlockNative(globalMemory);
        }
    }
}
