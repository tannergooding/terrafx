// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using TerraFX;

namespace TerraFX.Platform;

[SupportedOSPlatform("windows10.0.22000")]
internal sealed unsafe partial class WindowsPlatformBackend : IPlatformBackend
{
    private const uint PeekRemove = 0x0001;
    private const int SystemMetricMonitorCount = 80;
    private const uint WindowMessageQuit = 0x0012;
    private static readonly Lock s_admissionGate = new();
    private static bool s_isAdmissionReserved;
    private static WindowsPlatformBackend? s_quarantinedBackend;
    private readonly ClipboardServiceStatus _clipboardStatus;
    private PlatformServiceStatus _displayStatus = PlatformServiceStatus.Unavailable;
    private int _isDisposed;
    private int _lastMonitorCount;
    private ImmutableArray<MonitorHandle> _monitorHandles = [];
    private int _monitorTopologyChanged;
    private readonly uint _ownerId;
    private readonly int _ownerThreadId;
    private readonly WindowsPlatformOwnerInitialization _platformOwner;

    internal WindowsPlatformBackend(uint ownerId, ClipboardServicePolicy clipboardPolicy)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ownerId);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((int)clipboardPolicy, (int)ClipboardServicePolicy.Required);
        ArgumentOutOfRangeException.ThrowIfNegative((int)clipboardPolicy);

        if (clipboardPolicy == ClipboardServicePolicy.Required)
        {
            throw new NotSupportedException("The Windows Platform backend does not yet provide the required clipboard service.");
        }

        lock (s_admissionGate)
        {
            if (s_isAdmissionReserved || (s_quarantinedBackend is not null))
            {
                throw new NativeHostAlreadyActiveException();
            }

            s_isAdmissionReserved = true;
        }

        try
        {
            _platformOwner = WindowsPlatformOwnerInitialization.Initialize();
        }
        catch
        {
            lock (s_admissionGate)
            {
                s_isAdmissionReserved = false;
            }

            throw;
        }

        _ownerId = ownerId;
        _ownerThreadId = _platformOwner.OwnerThreadId;
        _clipboardStatus = clipboardPolicy == ClipboardServicePolicy.Optional
            ? ClipboardServiceStatus.Unsupported
            : ClipboardServiceStatus.NotRequested;
        _lastMonitorCount = GetSystemMetricsNative(SystemMetricMonitorCount);
    }

    public PlatformServiceStatus AudioStatus
    {
        get
        {
            EnsureOwnerThread();
            return PlatformServiceStatus.NotRequested;
        }
    }

    public PlatformBackend Backend
    {
        get
        {
            EnsureOwnerThread();
            return PlatformBackend.WindowsNative;
        }
    }

    public ClipboardServiceStatus ClipboardStatus
    {
        get
        {
            EnsureOwnerThread();
            return _clipboardStatus;
        }
    }

    public PlatformServiceStatus DisplayStatus
    {
        get
        {
            EnsureOwnerThread();
            return _displayStatus;
        }
    }

    public uint OwnerId => _ownerId;

    public PlatformServiceStatus PhysicalInputStatus
    {
        get
        {
            EnsureOwnerThread();
            return PlatformServiceStatus.Unsupported;
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial nint DispatchMessageNative(NativeMessage* message);

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplayMonitors", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplayMonitorsNative(
        nint deviceContext,
        NativeRect* clipRect,
        delegate* unmanaged[Stdcall]<nint, nint, NativeRect*, nint, int> monitorEnumerator,
        nint userData);

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must be captured and rethrown after the unmanaged monitor-enumeration callback returns.")]
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int EnumerateMonitor(nint monitor, nint deviceContext, NativeRect* monitorRect, nint userData)
    {
        _ = deviceContext;
        _ = monitorRect;

        if (GCHandle.FromIntPtr(userData).Target is not MonitorEnumerationContext context)
        {
            return 0;
        }

        try
        {
            var info = default(MonitorInfoEx);
            info.Size = checked((uint)sizeof(MonitorInfoEx));

            if (!GetMonitorInfoNative(monitor, &info))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Windows monitor information query failed.");
            }

            context.Monitors.Add(new PlatformMonitorInfo(info.GetDeviceName(), monitor));
            return 1;
        }
        catch (Exception exception)
        {
            context.Failure = exception;
            return 0;
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoNative(nint monitor, MonitorInfoEx* monitorInfo);

    [LibraryImport("user32.dll", EntryPoint = "GetSystemMetrics")]
    private static partial int GetSystemMetricsNative(int index);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessageNative(
        NativeMessage* message,
        nint window,
        uint minimumMessage,
        uint maximumMessage,
        uint removeMessage);

    [LibraryImport("user32.dll", EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessageNative(NativeMessage* message);

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A failed Windows owner teardown quarantines native-host admission rather than releasing an uncertain owner.")]
    public void Dispose()
    {
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            return;
        }

        EnsureOwnerThread();

        try
        {
            _platformOwner.Dispose();
        }
        catch (Exception)
        {
            _ = Interlocked.Exchange(ref _isDisposed, 1);
            lock (s_admissionGate)
            {
                s_quarantinedBackend = this;
            }

            throw;
        }

        _ = Interlocked.Exchange(ref _isDisposed, 1);

        lock (s_admissionGate)
        {
            s_isAdmissionReserved = false;
            s_quarantinedBackend = null;
        }
    }

    public ImmutableArray<MonitorHandle> GetMonitorHandles()
    {
        EnsureOwnerThread();
        return _monitorHandles;
    }

    public ImmutableArray<PlatformMonitorInfo> GetMonitorInfo()
    {
        EnsureOwnerThread();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var monitorCountBefore = GetSystemMetricsNative(SystemMetricMonitorCount);
            var context = new MonitorEnumerationContext();
            var contextHandle = GCHandle.Alloc(context);
            bool succeeded;

            try
            {
                succeeded = EnumDisplayMonitorsNative(
                    0,
                    null,
                    (delegate* unmanaged[Stdcall]<nint, nint, NativeRect*, nint, int>)&EnumerateMonitor,
                    GCHandle.ToIntPtr(contextHandle));
            }
            finally
            {
                contextHandle.Free();
            }

            if (context.Failure is Exception failure)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }

            if (!succeeded)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Windows monitor enumeration failed.");
            }

            var monitorCountAfter = GetSystemMetricsNative(SystemMetricMonitorCount);

            if ((context.Monitors.Count == monitorCountBefore) && (monitorCountBefore == monitorCountAfter))
            {
                return [.. context.Monitors];
            }
        }

        _ = Interlocked.Exchange(ref _monitorTopologyChanged, 1);
        throw new InvalidOperationException("The Windows monitor topology changed repeatedly during enumeration.");
    }

    public void Pump()
    {
        EnsureOwnerThread();
        var message = default(NativeMessage);

        while (PeekMessageNative(&message, 0, 0, 0, PeekRemove))
        {
            if (message.Id == WindowMessageQuit)
            {
                throw new InvalidOperationException("The Windows Platform message queue received WM_QUIT before host shutdown.");
            }

            _ = TranslateMessageNative(&message);
            _ = DispatchMessageNative(&message);
        }

        var monitorCount = GetSystemMetricsNative(SystemMetricMonitorCount);

        if (monitorCount != _lastMonitorCount)
        {
            _lastMonitorCount = monitorCount;
            _ = Interlocked.Exchange(ref _monitorTopologyChanged, 1);
        }
    }

    public void SetMonitorHandles(ImmutableArray<MonitorHandle> monitorHandles)
    {
        EnsureOwnerThread();

        if (monitorHandles.IsDefault)
        {
            throw new ArgumentException("The monitor-handle snapshot must be initialized.", nameof(monitorHandles));
        }

        var uniqueHandles = new HashSet<MonitorHandle>();

        foreach (var monitorHandle in monitorHandles)
        {
            if (!monitorHandle.IsValid || (monitorHandle.OwnerId != _ownerId) || !uniqueHandles.Add(monitorHandle))
            {
                throw new ArgumentException("The monitor-handle snapshot must contain unique handles from this owner.", nameof(monitorHandles));
            }
        }

        _monitorHandles = [.. monitorHandles];
        _displayStatus = _monitorHandles.IsEmpty
            ? PlatformServiceStatus.Unavailable
            : PlatformServiceStatus.Available;
    }

    public bool TakeMonitorTopologyChanged()
    {
        EnsureOwnerThread();
        return Interlocked.Exchange(ref _monitorTopologyChanged, 0) != 0;
    }

    private void EnsureOwnerThread()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

        if (_ownerThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("The Windows Platform backend must be accessed on its host-owner thread.");
        }

        _platformOwner.EnsureOwnerThread();
    }

    private sealed class MonitorEnumerationContext
    {
        internal List<PlatformMonitorInfo> Monitors { get; } = [];

        internal Exception? Failure { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct MonitorInfoEx
    {
        internal uint Size;
        internal NativeRect Monitor;
        internal NativeRect Work;
        internal uint Flags;
        internal fixed char DeviceName[32];

        internal string GetDeviceName()
        {
            fixed (char* deviceName = DeviceName)
            {
                var length = 0;

                while ((length < 32) && (deviceName[length] != '\0'))
                {
                    length++;
                }

                if (length == 0)
                {
                    throw new InvalidOperationException("Windows returned an empty monitor device name.");
                }

                return new string(deviceName, 0, length);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        internal nint Window;
        internal uint Id;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal NativePoint Point;
        internal uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }
}
