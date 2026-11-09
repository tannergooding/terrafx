// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TerraFX.Platform.Tests;

[SupportedOSPlatform("windows10.0.22000")]
internal sealed unsafe partial class WindowsClipboardTestWindowStation : IDisposable
{
    private const uint DesktopAllAccess = 0x000F01FF;
    private const uint UserObjectName = 2;
    private const uint WindowStationClipboardAccess = 0x0000003F;

    private nint _desktop;
    private string _desktopName = string.Empty;
    private bool _isDisposed;
    private readonly nint _originalWindowStation;
    private bool _processStationSwitched;
    private readonly nint _windowStation;
    private string _windowStationName = string.Empty;

    private WindowsClipboardTestWindowStation(
        nint originalWindowStation,
        nint windowStation)
    {
        _originalWindowStation = originalWindowStation;
        _windowStation = windowStation;
    }

    internal string DesktopName => _desktopName;

    internal string WindowStationName => _windowStationName;

    public static WindowsClipboardTestWindowStation? TryCreate(out string? failureMessage)
    {
        failureMessage = null;
        var originalWindowStation = GetProcessWindowStationNative();

        if (originalWindowStation == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The current Windows station could not be queried.");
        }

        var desktopName = $"TerraFX.Clipboard.Desktop.{Guid.NewGuid():N}";
        var windowStation = CreateWindowStationNative(
            windowStation: null,
            flags: 0,
            WindowStationClipboardAccess,
            securityAttributes: 0);

        if (windowStation == 0)
        {
            failureMessage = $"A private Windows window station could not be created: {LastErrorMessage()}";
            return null;
        }

        var scope = new WindowsClipboardTestWindowStation(
            originalWindowStation,
            windowStation) {
            _windowStationName = GetUserObjectName(windowStation),
            _desktopName = desktopName,
        };

        if (!SetProcessWindowStationNative(windowStation))
        {
            failureMessage = $"The test process could not select its private window station: {LastErrorMessage()}";
            scope.Dispose();
            return null;
        }

        scope._processStationSwitched = true;
        scope._desktop = CreateDesktopNative(
            desktopName,
            device: 0,
            devMode: 0,
            flags: 0,
            desiredAccess: DesktopAllAccess,
            securityAttributes: 0);

        if (scope._desktop == 0)
        {
            failureMessage = $"A private Windows desktop could not be created: {LastErrorMessage()}";
            scope.Dispose();
            return null;
        }

        if (!SetProcessWindowStationNative(originalWindowStation))
        {
            var error = Marshal.GetLastPInvokeError();
            scope.Dispose();
            throw new Win32Exception(error, "The original Windows process station could not be restored after creating the test desktop.");
        }

        scope._processStationSwitched = false;
        return scope;
    }

    internal static void VerifyCurrentStation(string windowStationName, string desktopName)
    {
        if (string.Equals(windowStationName, "WinSta0", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The child clipboard witness cannot run on the interactive WinSta0 station.");
        }

        var windowStation = GetProcessWindowStationNative();
        var desktop = GetThreadDesktopNative(GetCurrentThreadIdNative());

        if ((windowStation == 0) || (desktop == 0))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The child process station or desktop could not be queried.");
        }

        if ((GetUserObjectName(windowStation) != windowStationName)
            || (GetUserObjectName(desktop) != desktopName))
        {
            throw new InvalidOperationException("The child process did not start on the test-owned clipboard station and desktop.");
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "CloseDesktop", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseDesktopNative(nint desktop);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "CloseWindowStation", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseWindowStationNative(nint windowStation);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "CreateDesktopW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateDesktopNative(
        string desktop,
        nint device,
        nint devMode,
        uint flags,
        uint desiredAccess,
        nint securityAttributes);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "CreateWindowStationW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateWindowStationNative(
        string? windowStation,
        uint flags,
        uint desiredAccess,
        nint securityAttributes);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")]
    private static partial uint GetCurrentThreadIdNative();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "GetProcessWindowStation")]
    private static partial nint GetProcessWindowStationNative();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "GetThreadDesktop", SetLastError = true)]
    private static partial nint GetThreadDesktopNative(uint threadId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "GetUserObjectInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetUserObjectInformationNative(
        nint handle,
        uint index,
        char* information,
        uint informationLength,
        uint* requiredLength);

    private static string GetUserObjectName(nint handle)
    {
        var buffer = new char[256];
        var requiredLength = 0u;

        fixed (char* bufferPointer = buffer)
        {
            if (!GetUserObjectInformationNative(
                handle,
                UserObjectName,
                bufferPointer,
                checked((uint)(buffer.Length * sizeof(char))),
                &requiredLength))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "The private Windows window station name could not be read.");
            }
        }

        var terminator = Array.IndexOf(buffer, '\0');

        if (terminator < 0)
        {
            throw new InvalidOperationException("The private Windows window station name was not terminated.");
        }

        return new string(buffer, 0, terminator);
    }

    private static string LastErrorMessage()
        => new Win32Exception(Marshal.GetLastPInvokeError()).Message;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "SetProcessWindowStation", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessWindowStationNative(nint windowStation);

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        var failures = new List<Exception>();
        var processStationRestored = !_processStationSwitched;

        if (_processStationSwitched)
        {
            if (SetProcessWindowStationNative(_originalWindowStation))
            {
                processStationRestored = true;
            }
            else
            {
                var error = Marshal.GetLastPInvokeError();
                failures.Add(new Win32Exception(error, "The original Windows process station could not be restored."));
            }
        }

        if ((_desktop != 0)
            && processStationRestored
            && !CloseDesktopNative(_desktop))
        {
            var error = Marshal.GetLastPInvokeError();
            failures.Add(new Win32Exception(error, "The test-owned Windows desktop could not be closed."));
        }

        if ((_windowStation != 0)
            && processStationRestored
            && !CloseWindowStationNative(_windowStation))
        {
            var error = Marshal.GetLastPInvokeError();
            failures.Add(new Win32Exception(error, "The test-owned Windows station could not be closed."));
        }

        if (failures.Count > 0)
        {
            throw new AggregateException("The isolated Windows clipboard station could not be retired.", failures);
        }
    }
}
