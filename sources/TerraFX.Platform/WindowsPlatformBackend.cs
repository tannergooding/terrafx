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
using TerraFX.Input;

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
    private bool _hasNativeMessageTimestamp;
    private readonly PlatformInputSampleQueue _inputSamples;
    private int _isDisposed;
    private readonly Dictionary<nint, PlatformInputDeviceKind> _knownInputDevices;
    private int _lastMonitorCount;
    private uint _lastNativeMessageTimestamp;
    private ImmutableArray<MonitorHandle> _monitorHandles = [];
    private int _monitorTopologyChanged;
    private ulong _nativeMessageTimestampEpoch;
    private readonly uint _ownerId;
    private readonly int _ownerThreadId;
    private readonly WindowsPlatformOwnerInitialization _platformOwner;
    private readonly WindowsRawInputRegistration _rawInputRegistration;
    private readonly TimeProvider _timeProvider;

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A partially initialized Windows input owner must release or quarantine its process-wide native admission.")]
    internal WindowsPlatformBackend(
        uint ownerId,
        ClipboardServicePolicy clipboardPolicy,
        TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ownerId);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((int)clipboardPolicy, (int)ClipboardServicePolicy.Required);
        ArgumentOutOfRangeException.ThrowIfNegative((int)clipboardPolicy);

        var windowClassName = $"TerraFX.Platform.Window.{ownerId:X8}";
        var windows = new WindowState?[PlatformHost.MaximumWindowCount];

        lock (s_admissionGate)
        {
            if (s_isAdmissionReserved || (s_quarantinedBackend is not null))
            {
                throw new NativeHostAlreadyActiveException();
            }

            s_isAdmissionReserved = true;
        }

        WindowsPlatformOwnerInitialization? platformOwner = null;
        WindowsRawInputRegistration? rawInputRegistration = null;

        try
        {
            platformOwner = WindowsPlatformOwnerInitialization.Initialize();
            rawInputRegistration = WindowsRawInputRegistration.Register(platformOwner);
        }
        catch (Exception initializationException)
        {
            try
            {
                rawInputRegistration?.Dispose();
                platformOwner?.Dispose();
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(initializationException, cleanupException);
            }

            lock (s_admissionGate)
            {
                s_isAdmissionReserved = false;
            }

            throw;
        }

        _platformOwner = platformOwner
            ?? throw new InvalidOperationException("The Windows Platform owner was not initialized.");
        _rawInputRegistration = rawInputRegistration
            ?? throw new InvalidOperationException("The Windows Raw Input registration was not initialized.");
        _timeProvider = timeProvider ?? TimeProvider.System;
        _ownerId = ownerId;
        _ownerThreadId = _platformOwner.OwnerThreadId;
        _windowClassName = windowClassName;
        _windows = windows;
        _inputSamples = new PlatformInputSampleQueue(_ownerThreadId);
        _knownInputDevices = [];
        _clipboardStatus = clipboardPolicy is ClipboardServicePolicy.NotRequested
            ? ClipboardServiceStatus.NotRequested
            : ClipboardServiceStatus.Ready;
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
            return PlatformServiceStatus.Available;
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
            DisposeWindows();
            _rawInputRegistration.Dispose();
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

    public PlatformInputSampleBatch DrainInputSamples()
    {
        EnsureOwnerThread();
        return _inputSamples.Drain();
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

        ApplyDeferredWindowUpdates();
        ThrowCallbackFailures();

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

    private void CaptureNativeInputSample(PlatformInputSample sample)
        => _ = _inputSamples.TryEnqueue(in sample);

    private void EnsureOwnerThread()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

        if (_ownerThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("The Windows Platform backend must be accessed on its host-owner thread.");
        }

        _platformOwner.EnsureOwnerThread();
    }

    private (long Timestamp, PhysicalInputEventDiagnostic Diagnostics) GetNativeMessageTimestamp()
    {
        var currentTimestamp = unchecked((uint)GetMessageTimeNative());
        var diagnostics = PhysicalInputEventDiagnostic.None;

        if (_hasNativeMessageTimestamp)
        {
            if (currentTimestamp < _lastNativeMessageTimestamp)
            {
                var decrease = _lastNativeMessageTimestamp - currentTimestamp;

                if (decrease > 0x80000000u)
                {
                    _nativeMessageTimestampEpoch = checked(_nativeMessageTimestampEpoch + (1UL << 32));
                }
                else
                {
                    diagnostics = PhysicalInputEventDiagnostic.TimestampUncertain;
                }
            }
            else if ((currentTimestamp - _lastNativeMessageTimestamp) > 0x80000000u)
            {
                diagnostics = PhysicalInputEventDiagnostic.TimestampUncertain;
            }
        }

        _hasNativeMessageTimestamp = true;
        _lastNativeMessageTimestamp = currentTimestamp;
        return (checked((long)(_nativeMessageTimestampEpoch + currentTimestamp)), diagnostics);
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
