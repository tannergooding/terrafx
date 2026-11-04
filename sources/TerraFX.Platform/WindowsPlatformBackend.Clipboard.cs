// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using TerraFX;
using ClipboardTextResult = TerraFX.Platform.ClipboardStatus;

namespace TerraFX.Platform;

[SupportedOSPlatform("windows10.0.22000")]
internal sealed unsafe partial class WindowsPlatformBackend
{
    private const uint ClipboardFormatUnicodeText = 13;
    private const uint GlobalMemoryMoveable = 0x0002;
    private static readonly UTF8Encoding s_strictUtf8Encoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Clipboard ownership is always released and operation failures remain observable to the queued request.")]
    public ClipboardReadResponse ReadClipboardText(ClipboardReadRequest request)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(request);

        if (_clipboardStatus is not ClipboardServiceStatus.Ready)
        {
            return new ClipboardReadResponse(
                ClipboardTextResult.Unsupported,
                text: null,
                ClipboardTextService.CreateDiagnostic(
                    "ClipboardUnsupported",
                    request.Window,
                    "ClipboardText.ReadTextAsync",
                    nativeResult: null,
                    "The Windows clipboard service was not requested."));
        }

        if (!TryGetNativeWindowBinding(request.Window, out var binding))
        {
            return new ClipboardReadResponse(
                ClipboardTextResult.SelectionExpired,
                text: null,
                ClipboardTextService.CreateDiagnostic(
                    "ClipboardWindowUnavailable",
                    request.Window,
                    "ClipboardText.ReadTextAsync",
                    nativeResult: null,
                    "The target window is no longer active."));
        }

        if (!OpenClipboardNative(binding.NativeWindowOrSurface))
        {
            var error = Marshal.GetLastPInvokeError();
            return new ClipboardReadResponse(
                ClipboardTextResult.PermissionDenied,
                text: null,
                ClipboardTextService.CreateDiagnostic(
                    "ClipboardOpenFailed",
                    request.Window,
                    "ClipboardText.ReadTextAsync",
                    error,
                    new Win32Exception(error).Message));
        }

        ClipboardReadResponse? response = null;
        Exception? failure = null;

        try
        {
            response = ReadOpenedClipboard(request);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (!CloseClipboardNative())
        {
            var error = Marshal.GetLastPInvokeError();
            failure = CombineFailures(failure, new Win32Exception(error, "Closing the Windows clipboard failed."));
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return response ?? throw new InvalidOperationException("The Windows clipboard read produced no response.");
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Untransferred global memory is always released and operation failures remain observable to the queued request.")]
    public ClipboardWriteResponse WriteClipboardText(ClipboardWriteRequest request)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(request);

        if (_clipboardStatus is not ClipboardServiceStatus.Ready)
        {
            return new ClipboardWriteResponse(
                ClipboardTextResult.Unsupported,
                ClipboardTextService.CreateDiagnostic(
                    "ClipboardUnsupported",
                    request.Window,
                    "ClipboardText.WriteTextAsync",
                    nativeResult: null,
                    "The Windows clipboard service was not requested."));
        }

        if (!TryGetNativeWindowBinding(request.Window, out var binding))
        {
            return new ClipboardWriteResponse(
                ClipboardTextResult.SelectionExpired,
                ClipboardTextService.CreateDiagnostic(
                    "ClipboardWindowUnavailable",
                    request.Window,
                    "ClipboardText.WriteTextAsync",
                    nativeResult: null,
                    "The target window is no longer active."));
        }

        if (request.Text.Contains('\0', StringComparison.Ordinal))
        {
            return new ClipboardWriteResponse(
                ClipboardTextResult.InvalidData,
                ClipboardTextService.CreateDiagnostic(
                    "ClipboardInvalidData",
                    request.Window,
                    "ClipboardText.WriteTextAsync",
                    nativeResult: null,
                    "Windows CF_UNICODETEXT cannot represent embedded NUL characters."));
        }

        int utf8ByteCount;

        try
        {
            utf8ByteCount = s_strictUtf8Encoding.GetByteCount(request.Text);
        }
        catch (EncoderFallbackException)
        {
            return new ClipboardWriteResponse(
                ClipboardTextResult.InvalidData,
                ClipboardTextService.CreateDiagnostic(
                    "ClipboardInvalidData",
                    request.Window,
                    "ClipboardText.WriteTextAsync",
                    nativeResult: null,
                    "The requested clipboard text contains an unpaired UTF-16 surrogate."));
        }

        if (utf8ByteCount > request.MaximumUtf8Bytes)
        {
            return new ClipboardWriteResponse(
                ClipboardTextResult.TooLarge,
                ClipboardTextService.CreateDiagnostic(
                    "ClipboardTooLarge",
                    request.Window,
                    "ClipboardText.WriteTextAsync",
                    nativeResult: null,
                    "The complete clipboard text exceeds the request byte limit."));
        }

        var byteCount = checked((nuint)(request.Text.Length + 1) * sizeof(char));
        var globalMemory = GlobalAllocNative(GlobalMemoryMoveable, byteCount);

        if (globalMemory == 0)
        {
            var error = Marshal.GetLastPInvokeError();
            return new ClipboardWriteResponse(
                ClipboardTextResult.PermissionDenied,
                ClipboardTextService.CreateDiagnostic(
                    "ClipboardAllocationFailed",
                    request.Window,
                    "ClipboardText.WriteTextAsync",
                    error,
                    new Win32Exception(error).Message));
        }

        var memoryTransferred = false;
        ClipboardWriteResponse? response = null;
        Exception? failure = null;

        try
        {
            response = WriteClipboardTextCore(request, binding.NativeWindowOrSurface, globalMemory, out memoryTransferred);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (!memoryTransferred && (GlobalFreeNative(globalMemory) != 0))
        {
            var error = Marshal.GetLastPInvokeError();
            failure = CombineFailures(failure, new Win32Exception(error, "Releasing the untransferred Windows clipboard memory failed."));
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return response ?? throw new InvalidOperationException("The Windows clipboard write produced no response.");
    }
}
