// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using ClipboardTextResult = TerraFX.Platform.ClipboardStatus;

namespace TerraFX.Platform;

internal sealed unsafe partial class WindowsPlatformBackend
{
    [LibraryImport("user32.dll", EntryPoint = "CloseClipboard", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboardNative();

    private static Exception CombineFailures(Exception? failure, Exception cleanupFailure)
        => failure is null ? cleanupFailure : new AggregateException(failure, cleanupFailure);

    [LibraryImport("user32.dll", EntryPoint = "EmptyClipboard", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboardNative();

    [LibraryImport("user32.dll", EntryPoint = "GetClipboardData", SetLastError = true)]
    private static partial nint GetClipboardDataNative(uint format);

    [LibraryImport("kernel32.dll", EntryPoint = "GlobalAlloc", SetLastError = true)]
    private static partial nint GlobalAllocNative(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll", EntryPoint = "GlobalFree", SetLastError = true)]
    private static partial nint GlobalFreeNative(nint memory);

    [LibraryImport("kernel32.dll", EntryPoint = "GlobalLock", SetLastError = true)]
    private static partial nint GlobalLockNative(nint memory);

    [LibraryImport("kernel32.dll", EntryPoint = "GlobalSize", SetLastError = true)]
    private static partial nuint GlobalSizeNative(nint memory);

    [LibraryImport("kernel32.dll", EntryPoint = "GlobalUnlock", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlockNative(nint memory);

    [LibraryImport("user32.dll", EntryPoint = "IsClipboardFormatAvailable", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailableNative(uint format);

    [LibraryImport("user32.dll", EntryPoint = "OpenClipboard", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboardNative(nint window);

    [LibraryImport("user32.dll", EntryPoint = "SetClipboardData", SetLastError = true)]
    private static partial nint SetClipboardDataNative(uint format, nint memory);

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "The open clipboard is always closed and operation failures remain observable to the queued request.")]
    private static ClipboardWriteResponse WriteClipboardTextCore(
        ClipboardWriteRequest request,
        nint nativeWindow,
        nint globalMemory,
        out bool memoryTransferred)
    {
        memoryTransferred = false;
        var memory = GlobalLockNative(globalMemory);

        if (memory == 0)
        {
            var error = Marshal.GetLastPInvokeError();
            return new ClipboardWriteResponse(
                ClipboardTextResult.PermissionDenied,
                ClipboardTextService.CreateDiagnostic(
                    "ClipboardLockFailed",
                    request.Window,
                    "ClipboardText.WriteTextAsync",
                    error,
                    new Win32Exception(error).Message));
        }

        try
        {
            request.Text.AsSpan().CopyTo(new Span<char>((char*)memory, request.Text.Length));
            ((char*)memory)[request.Text.Length] = '\0';
        }
        finally
        {
            _ = GlobalUnlockNative(globalMemory);
        }

        if (!OpenClipboardNative(nativeWindow))
        {
            var error = Marshal.GetLastPInvokeError();
            return new ClipboardWriteResponse(
                ClipboardTextResult.PermissionDenied,
                ClipboardTextService.CreateDiagnostic(
                    "ClipboardOpenFailed",
                    request.Window,
                    "ClipboardText.WriteTextAsync",
                    error,
                    new Win32Exception(error).Message));
        }

        ClipboardWriteResponse? response = null;
        Exception? failure = null;

        try
        {
            if (!EmptyClipboardNative())
            {
                var error = Marshal.GetLastPInvokeError();
                response = new ClipboardWriteResponse(
                    ClipboardTextResult.PermissionDenied,
                    ClipboardTextService.CreateDiagnostic(
                        "ClipboardEmptyFailed",
                        request.Window,
                        "ClipboardText.WriteTextAsync",
                        error,
                        new Win32Exception(error).Message));
            }
            else if (SetClipboardDataNative(ClipboardFormatUnicodeText, globalMemory) == 0)
            {
                var error = Marshal.GetLastPInvokeError();
                response = new ClipboardWriteResponse(
                    ClipboardTextResult.PermissionDenied,
                    ClipboardTextService.CreateDiagnostic(
                        "ClipboardTransferFailed",
                        request.Window,
                        "ClipboardText.WriteTextAsync",
                        error,
                        new Win32Exception(error).Message));
            }
            else
            {
                memoryTransferred = true;
                response = new ClipboardWriteResponse(ClipboardTextResult.Completed, diagnostic: null);
            }
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

        return response ?? throw new InvalidOperationException("The Windows clipboard write produced no response.");
    }
}
