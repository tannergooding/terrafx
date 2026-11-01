// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace TerraFX.Platform;

internal sealed partial class WindowsPlatformOwnerInitialization : IDisposable
{
    private const uint CoInitializeApartmentThreaded = 0x2;
    private static readonly nint s_dpiAwarenessContextPerMonitorAwareV2 = new(-4);
    private int _isDisposed;
    private readonly int _ownerThreadId;
    private readonly nint _previousThreadDpiAwarenessContext;

    private WindowsPlatformOwnerInitialization(int ownerThreadId, nint previousThreadDpiAwarenessContext)
    {
        _ownerThreadId = ownerThreadId;
        _previousThreadDpiAwarenessContext = previousThreadDpiAwarenessContext;
    }

    internal int OwnerThreadId => _ownerThreadId;

    internal static WindowsPlatformOwnerInitialization Initialize()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
            || (RuntimeInformation.ProcessArchitecture != Architecture.X64))
        {
            throw new PlatformNotSupportedException("The Windows Platform backend requires Windows 11 x64.");
        }

        var thread = Thread.CurrentThread;

        if (thread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException("The Windows Platform owner thread must be initialized as STA.");
        }

        var previousThreadContext = SetPerMonitorV2DpiAwareness();
        var comResult = CoInitializeExNative(0, CoInitializeApartmentThreaded);

        if (comResult < 0)
        {
            var initializationException = new InvalidOperationException(
                $"The Windows Platform owner could not initialize COM as STA (HRESULT 0x{unchecked((uint)comResult):X8}).");

            if (SetThreadDpiAwarenessContextNative(previousThreadContext) == 0)
            {
                throw new AggregateException(
                    initializationException,
                    new Win32Exception(Marshal.GetLastPInvokeError(), "The Windows Platform owner could not restore its thread DPI context."));
            }

            throw initializationException;
        }

        return new WindowsPlatformOwnerInitialization(thread.ManagedThreadId, previousThreadContext);
    }

    [LibraryImport("user32.dll", EntryPoint = "AreDpiAwarenessContextsEqual")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AreDpiAwarenessContextsEqualNative(nint dpiContextA, nint dpiContextB);

    [LibraryImport("ole32.dll", EntryPoint = "CoInitializeEx")]
    private static partial int CoInitializeExNative(nint reserved, uint coInit);

    [LibraryImport("ole32.dll", EntryPoint = "CoUninitialize")]
    private static partial void CoUninitializeNative();

    [LibraryImport("kernel32.dll", EntryPoint = "GetCurrentProcess")]
    private static partial nint GetCurrentProcessNative();

    [LibraryImport("user32.dll", EntryPoint = "GetDpiAwarenessContextForProcess", SetLastError = true)]
    private static partial nint GetDpiAwarenessContextForProcessNative(nint process);

    [LibraryImport("user32.dll", EntryPoint = "GetThreadDpiAwarenessContext", SetLastError = true)]
    private static partial nint GetThreadDpiAwarenessContextNative();

    private static nint SetPerMonitorV2DpiAwareness()
    {
        var processContext = GetDpiAwarenessContextForProcessNative(GetCurrentProcessNative());

        if (processContext == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The process DPI awareness context could not be read.");
        }

        if (!AreDpiAwarenessContextsEqualNative(processContext, s_dpiAwarenessContextPerMonitorAwareV2)
            && !SetProcessDpiAwarenessContextNative(s_dpiAwarenessContextPerMonitorAwareV2))
        {
            var nativeResult = Marshal.GetLastPInvokeError();
            processContext = GetDpiAwarenessContextForProcessNative(GetCurrentProcessNative());

            if ((processContext == 0)
                || !AreDpiAwarenessContextsEqualNative(processContext, s_dpiAwarenessContextPerMonitorAwareV2))
            {
                throw new Win32Exception(nativeResult, "The process could not enter Per-Monitor-V2 DPI awareness before window creation.");
            }
        }

        processContext = GetDpiAwarenessContextForProcessNative(GetCurrentProcessNative());

        if ((processContext == 0)
            || !AreDpiAwarenessContextsEqualNative(processContext, s_dpiAwarenessContextPerMonitorAwareV2))
        {
            throw new PlatformNotSupportedException("The process DPI awareness context is not Per-Monitor-V2.");
        }

        var previousThreadContext = SetThreadDpiAwarenessContextNative(s_dpiAwarenessContextPerMonitorAwareV2);

        if (previousThreadContext == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The Windows Platform owner thread could not enter Per-Monitor-V2 DPI awareness.");
        }

        var currentThreadContext = GetThreadDpiAwarenessContextNative();

        if ((currentThreadContext == 0)
            || !AreDpiAwarenessContextsEqualNative(currentThreadContext, s_dpiAwarenessContextPerMonitorAwareV2))
        {
            if (SetThreadDpiAwarenessContextNative(previousThreadContext) == 0)
            {
                throw new AggregateException(
                    new PlatformNotSupportedException("The Windows Platform owner thread did not enter Per-Monitor-V2 DPI awareness."),
                    new Win32Exception(Marshal.GetLastPInvokeError(), "The Windows Platform owner could not restore its thread DPI context."));
            }

            throw new PlatformNotSupportedException("The Windows Platform owner thread did not enter Per-Monitor-V2 DPI awareness.");
        }

        return previousThreadContext;
    }

    [LibraryImport("user32.dll", EntryPoint = "SetProcessDpiAwarenessContext", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessDpiAwarenessContextNative(nint dpiContext);

    [LibraryImport("user32.dll", EntryPoint = "SetThreadDpiAwarenessContext", SetLastError = true)]
    private static partial nint SetThreadDpiAwarenessContextNative(nint dpiContext);

    public void Dispose()
    {
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            return;
        }

        EnsureOwnerThread();
        _ = Interlocked.Exchange(ref _isDisposed, 1);
        CoUninitializeNative();

        if (SetThreadDpiAwarenessContextNative(_previousThreadDpiAwarenessContext) == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The Windows Platform owner could not restore its thread DPI context.");
        }
    }

    private void EnsureOwnerThread()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

        if (_ownerThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("Windows Platform initialization must be disposed on its owner thread.");
        }
    }
}
