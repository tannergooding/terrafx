// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using TerraFX.Input;

namespace TerraFX.Platform;

internal sealed class LinuxEvdevGamepadCapture : IDisposable
{
    private const int MaximumGamepadDevices = 256;
    private const int MaximumPendingRemovals = MaximumGamepadDevices;
    private const ushort SynchronizationDroppedCode = 0x0003;
    private const ushort SynchronizationEventType = 0x0000;
    private const ushort SynchronizationReportCode = 0x0000;

    private readonly Dictionary<nint, GamepadCapture> _devicesByNativeIdentity;
    private readonly Dictionary<string, GamepadCapture> _devicesBySysPath;
    private bool _isDisposed;
    private readonly LinuxEvdevGamepadMonitor _monitor;
    private long _nextNativeIdentity;
    private readonly int _ownerThreadId;
    private readonly Dictionary<string, LinuxEvdevGamepadDevice> _pendingOpenDevices;
    private readonly Queue<PlatformInputSample> _pendingRemovals;
    private readonly HashSet<string> _permissionDeniedDevices;
    private readonly PlatformInputSampleQueue _samples;
    private readonly TimeProvider _timeProvider;
    private readonly List<(string SysPath, bool PermissionDenied)> _unavailableDevices;

    internal LinuxEvdevGamepadCapture(
        PlatformInputSampleQueue samples,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _devicesByNativeIdentity = [];
        _devicesBySysPath = [];
        _monitor = new LinuxEvdevGamepadMonitor();
        _nextNativeIdentity = 1;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _pendingOpenDevices = [];
        _pendingRemovals = [];
        _permissionDeniedDevices = [];
        _samples = samples;
        _timeProvider = timeProvider;
        _unavailableDevices = [];
        GamepadStatus = PlatformServiceStatus.Available;

        try
        {
            foreach (var device in _monitor.InitialDevices)
            {
                AddDevice(device);
            }
        }
        catch
        {
            foreach (var capture in _devicesBySysPath.Values)
            {
                capture.Dispose();
            }

            _devicesByNativeIdentity.Clear();
            _devicesBySysPath.Clear();
            _pendingOpenDevices.Clear();
            _monitor.Dispose();
            throw;
        }
    }

    internal PlatformServiceStatus GamepadStatus { get; private set; }

    private static GamepadCapture CreateCapture(
        LinuxEvdevGamepadDevice device,
        nint nativeIdentity)
        => GamepadCapture.Open(device, nativeIdentity);

    private static PlatformInputSample CreateLifecycleSample(
        PhysicalInputEventKind kind,
        nint nativeIdentity,
        long monotonicTimestamp)
        => new(
            kind,
            Window: null,
            nativeIdentity,
            PlatformInputDeviceKind.Gamepad,
            Diagnostics: PhysicalInputEventDiagnostic.TimestampUncertain,
            MonotonicTimestamp: monotonicTimestamp);

    private static PlatformInputSample CreateReconciliationSample(
        nint nativeIdentity,
        ImmutableArray<PlatformInputStateValue> state,
        long monotonicTimestamp)
        => new(
            PhysicalInputEventKind.StateReconciliation,
            Window: null,
            nativeIdentity,
            PlatformInputDeviceKind.Gamepad,
            Diagnostics: PhysicalInputEventDiagnostic.TimestampUncertain,
            MonotonicTimestamp: monotonicTimestamp,
            ReconciliationState: state);

    private static bool IsDeviceUnavailable(IOException exception)
        => (exception is EndOfStreamException)
            || (exception.InnerException is System.ComponentModel.Win32Exception nativeException
                && (nativeException.NativeErrorCode is 2 or 6 or 19));

    private static bool IsPermissionDenied(IOException exception)
        => (exception.InnerException is System.ComponentModel.Win32Exception nativeException)
            && (nativeException.NativeErrorCode == 13);

    public void Dispose()
    {
        EnsureOwnerThread();

        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        foreach (var capture in _devicesBySysPath.Values)
        {
            capture.Dispose();
        }

        _devicesByNativeIdentity.Clear();
        _devicesBySysPath.Clear();
        _pendingOpenDevices.Clear();
        _pendingRemovals.Clear();
        _permissionDeniedDevices.Clear();
        _monitor.Dispose();
    }

    internal void Pump()
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        RetryPendingRemovals();

        foreach (var change in _monitor.Pump())
        {
            if (change.Kind is PhysicalInputEventKind.DeviceAdded)
            {
                AddDevice(change.Device);
            }
            else if (change.Kind is PhysicalInputEventKind.DeviceRemoved)
            {
                RemoveDevice(change.Device.SysPath);
            }
            else
            {
                throw new InvalidDataException("The evdev monitor returned an unsupported device-change kind.");
            }
        }

        RetryPendingRemovals();
        RetryPendingOpenDevices();

        if (_pendingRemovals.Count != 0)
        {
            return;
        }

        _unavailableDevices.Clear();

        foreach (var capture in _devicesByNativeIdentity.Values)
        {
            try
            {
                if (capture.NeedsAnnouncement || capture.NeedsReconciliation)
                {
                    ReconcileCurrentState(capture);
                }

                if (!capture.NeedsAnnouncement && !capture.NeedsReconciliation)
                {
                    PumpDevice(capture);
                }
            }
            catch (IOException exception) when (IsDeviceUnavailable(exception) || IsPermissionDenied(exception))
            {
                _unavailableDevices.Add((capture.SysPath, IsPermissionDenied(exception)));
            }
        }

        foreach (var (sysPath, permissionDenied) in _unavailableDevices)
        {
            RemoveDevice(sysPath);

            if (permissionDenied)
            {
                MarkPermissionDenied(sysPath);
            }
        }
    }

    internal void RequestDeviceReconciliation(nint nativeIdentity)
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero((long)nativeIdentity);

        if (_devicesByNativeIdentity.TryGetValue(nativeIdentity, out var capture))
        {
            capture.NeedsReconciliation = true;
            ReconcileCurrentState(capture);
        }
    }

    private void AddDevice(LinuxEvdevGamepadDevice device)
    {
        if (_devicesBySysPath.ContainsKey(device.SysPath)
            || _pendingOpenDevices.ContainsKey(device.SysPath))
        {
            return;
        }

        _ = _permissionDeniedDevices.Remove(device.SysPath);

        if ((_devicesBySysPath.Count + _pendingOpenDevices.Count + _permissionDeniedDevices.Count) >= MaximumGamepadDevices)
        {
            throw new InvalidOperationException("The bounded evdev gamepad device table is full.");
        }

        try
        {
            var nativeIdentity = checked((nint)_nextNativeIdentity);
            var capture = CreateCapture(device, nativeIdentity);
            _nextNativeIdentity = checked(_nextNativeIdentity + 1);

            try
            {
                if (!_devicesBySysPath.TryAdd(device.SysPath, capture))
                {
                    throw new InvalidDataException("The evdev gamepad capture contains a duplicate device sysfs path.");
                }

                if (!_devicesByNativeIdentity.TryAdd(nativeIdentity, capture))
                {
                    _ = _devicesBySysPath.Remove(device.SysPath);
                    throw new InvalidDataException("The evdev gamepad capture contains a duplicate device identity.");
                }
            }
            catch
            {
                capture.Dispose();
                throw;
            }
        }
        catch (UnauthorizedAccessException)
        {
            MarkPermissionDenied(device.SysPath);
            return;
        }
        catch (FileNotFoundException)
        {
            _pendingOpenDevices.Add(device.SysPath, device);
            return;
        }
        catch (DirectoryNotFoundException)
        {
            _pendingOpenDevices.Add(device.SysPath, device);
            return;
        }
        catch (IOException exception) when (IsDeviceUnavailable(exception))
        {
            _pendingOpenDevices.Add(device.SysPath, device);
            return;
        }
        catch (IOException exception) when (IsPermissionDenied(exception))
        {
            MarkPermissionDenied(device.SysPath);
            return;
        }

        _ = _permissionDeniedDevices.Remove(device.SysPath);
        GamepadStatus = (_permissionDeniedDevices.Count == 0)
            ? PlatformServiceStatus.Available
            : PlatformServiceStatus.PermissionDenied;
    }

    private void EnsureOwnerThread()
    {
        if (_ownerThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("Linux evdev gamepad capture is restricted to its host-owner thread.");
        }
    }

    private void MarkPermissionDenied(string sysPath)
    {
        _ = _permissionDeniedDevices.Add(sysPath);
        GamepadStatus = PlatformServiceStatus.PermissionDenied;
    }

    private void PumpDevice(GamepadCapture capture)
    {
        foreach (var inputEvent in capture.Reader.ReadAvailable())
        {
            if (capture.IsResynchronizing)
            {
                if ((inputEvent.Type == SynchronizationEventType)
                    && (inputEvent.Code == SynchronizationReportCode))
                {
                    capture.State = capture.Reader.ReadGamepadState();
                    capture.IsResynchronizing = false;
                    capture.NeedsReconciliation = true;
                }

                if (capture.NeedsReconciliation)
                {
                    break;
                }

                continue;
            }

            if ((inputEvent.Type == SynchronizationEventType)
                && (inputEvent.Code == SynchronizationDroppedCode))
            {
                capture.IsResynchronizing = true;
                continue;
            }

            var samples = LinuxEvdevGamepadEventDecoder.Decode(
                capture.State,
                capture.NativeIdentity,
                inputEvent,
                capture.Reader.UsesMonotonicClock,
                _timeProvider.GetTimestamp());

            foreach (var sample in samples)
            {
                if (!_samples.TryEnqueue(in sample))
                {
                    capture.NeedsReconciliation = true;
                    break;
                }
            }

            if (capture.NeedsReconciliation)
            {
                break;
            }
        }
    }

    private void ReconcileCurrentState(GamepadCapture capture)
    {
        if ((_pendingRemovals.Count != 0) || !_samples.HasCapacity)
        {
            return;
        }

        if (capture.NeedsAnnouncement)
        {
            var deviceAdded = CreateLifecycleSample(
                PhysicalInputEventKind.DeviceAdded,
                capture.NativeIdentity,
                _timeProvider.GetTimestamp());

            if (!_samples.TryEnqueue(in deviceAdded))
            {
                return;
            }

            capture.NeedsAnnouncement = false;
        }

        if (!_samples.HasCapacity)
        {
            capture.NeedsReconciliation = true;
            return;
        }

        capture.State = capture.Reader.ReadGamepadState();
        var reconciliation = CreateReconciliationSample(
            capture.NativeIdentity,
            capture.State.GetCurrentState(),
            _timeProvider.GetTimestamp());

        if (_samples.TryEnqueue(in reconciliation))
        {
            capture.NeedsReconciliation = false;
        }
    }

    private void RemoveDevice(string sysPath)
    {
        _ = _pendingOpenDevices.Remove(sysPath);

        if (_permissionDeniedDevices.Remove(sysPath))
        {
            GamepadStatus = (_permissionDeniedDevices.Count == 0)
                ? PlatformServiceStatus.Available
                : PlatformServiceStatus.PermissionDenied;
        }

        if (!_devicesBySysPath.Remove(sysPath, out var capture))
        {
            return;
        }

        _ = _devicesByNativeIdentity.Remove(capture.NativeIdentity);
        capture.Dispose();
        var deviceRemoved = CreateLifecycleSample(
            PhysicalInputEventKind.DeviceRemoved,
            capture.NativeIdentity,
            _timeProvider.GetTimestamp());

        if (!_samples.HasCapacity)
        {
            if (_pendingRemovals.Count >= MaximumPendingRemovals)
            {
                throw new InvalidOperationException("The bounded evdev gamepad removal queue is exhausted.");
            }

            _pendingRemovals.Enqueue(deviceRemoved);
            return;
        }

        _ = _samples.TryEnqueue(in deviceRemoved);
    }

    private void RetryPendingOpenDevices()
    {
        foreach (var device in _pendingOpenDevices.Values.ToArray())
        {
            _ = _pendingOpenDevices.Remove(device.SysPath);
            AddDevice(device);
        }
    }

    private void RetryPendingRemovals()
    {
        while (_pendingRemovals.TryPeek(out var sample))
        {
            if (!_samples.HasCapacity || !_samples.TryEnqueue(in sample))
            {
                return;
            }

            _ = _pendingRemovals.Dequeue();
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_isDisposed, this);

    private sealed class GamepadCapture(
        LinuxEvdevInputEventReader reader,
        LinuxEvdevGamepadState state,
        string sysPath,
        nint nativeIdentity) : IDisposable
    {
        internal bool IsResynchronizing;

        internal readonly nint NativeIdentity = nativeIdentity;

        internal bool NeedsAnnouncement = true;

        internal bool NeedsReconciliation = true;

        internal readonly LinuxEvdevInputEventReader Reader = reader;

        internal LinuxEvdevGamepadState State = state;

        internal readonly string SysPath = sysPath;

        internal static GamepadCapture Open(LinuxEvdevGamepadDevice device, nint nativeIdentity)
        {
            var reader = (LinuxEvdevInputEventReader?)LinuxEvdevInputEventReader.Open(device.DeviceNode);

            try
            {
                var activeReader = reader
                    ?? throw new InvalidOperationException("The evdev input reader was not initialized.");
                var capture = new GamepadCapture(
                    activeReader,
                    activeReader.ReadGamepadState(),
                    device.SysPath,
                    nativeIdentity);
                reader = null;
                return capture;
            }
            finally
            {
                reader?.Dispose();
            }
        }

        public void Dispose()
            => Reader.Dispose();
    }
}
