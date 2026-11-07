// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using TerraFX.Input;

namespace TerraFX.Platform;

internal sealed class LinuxEvdevGamepadMonitor : IDisposable
{
    private const int EAgain = 11;
    private const int EInterrupted = 4;
    private const int MaximumEventsPerPump = 64;

    private readonly Dictionary<string, LinuxEvdevGamepadDevice> _devices = [];
    private bool _isDisposed;
    private readonly nint _monitor;
    private readonly int _ownerThreadId;

    internal LinuxEvdevGamepadMonitor()
    {
        _ownerThreadId = Environment.CurrentManagedThreadId;

        var result = LinuxEvdevGamepadDiscovery.SystemdNative.DeviceMonitorNew(out var monitor);
        LinuxEvdevGamepadDiscovery.EnsureSuccess(result, "create a device monitor");

        if (monitor == 0)
        {
            throw new InvalidOperationException("systemd returned no device monitor.");
        }

        _monitor = monitor;

        try
        {
            LinuxEvdevGamepadDiscovery.EnsureSuccess(
                LinuxEvdevGamepadDiscovery.SystemdNative.DeviceMonitorFilterAddMatchSubsystemDevType(
                    _monitor,
                    "input",
                    devtype: null),
                "filter input-device events");
            LinuxEvdevGamepadDiscovery.EnsureSuccess(
                LinuxEvdevGamepadDiscovery.SystemdNative.DeviceMonitorFilterUpdate(_monitor),
                "install the input-device event filter");
            InitialDevices = LinuxEvdevGamepadDiscovery.Enumerate();

            foreach (var device in InitialDevices)
            {
                if (!_devices.TryAdd(device.SysPath, device))
                {
                    throw new InvalidDataException("systemd returned duplicate input-device sysfs paths.");
                }
            }
        }
        catch
        {
            _ = LinuxEvdevGamepadDiscovery.SystemdNative.DeviceMonitorUnref(_monitor);
            throw;
        }
    }

    internal ImmutableArray<LinuxEvdevGamepadDevice> InitialDevices { get; }

    private static DeviceChange CreateChange(
        PhysicalInputEventKind kind,
        LinuxEvdevGamepadDevice device)
        => new(kind, device);

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        EnsureOwnerThread();
        _isDisposed = true;
        _devices.Clear();
        _ = LinuxEvdevGamepadDiscovery.SystemdNative.DeviceMonitorUnref(_monitor);
    }

    internal ImmutableArray<DeviceChange> Pump()
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        var changes = ImmutableArray.CreateBuilder<DeviceChange>();

        for (var eventIndex = 0; eventIndex < MaximumEventsPerPump; eventIndex++)
        {
            var result = LinuxEvdevGamepadDiscovery.SystemdNative.DeviceMonitorReceive(
                _monitor,
                out var device);

            if (result == -EAgain)
            {
                break;
            }

            if (result == -EInterrupted)
            {
                break;
            }

            if (result < 0)
            {
                throw new IOException($"systemd could not receive a device event; status {result}.");
            }

            if (result == 0)
            {
                continue;
            }

            if (device == 0)
            {
                throw new InvalidDataException("systemd returned a device event without a device.");
            }

            try
            {
                ProcessDevice(device, changes);
            }
            finally
            {
                _ = LinuxEvdevGamepadDiscovery.SystemdNative.DeviceUnref(device);
            }
        }

        return changes.ToImmutable();
    }

    private void EnsureOwnerThread()
    {
        if (_ownerThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("The evdev gamepad monitor is restricted to its creating thread.");
        }
    }

    private void ProcessDevice(
        nint device,
        ImmutableArray<DeviceChange>.Builder changes)
    {
        var sysPathResult = LinuxEvdevGamepadDiscovery.SystemdNative.DeviceGetSysPath(device, out var sysPath);
        LinuxEvdevGamepadDiscovery.EnsureSuccess(sysPathResult, "read the device-event sysfs path");
        var sysPathText = Marshal.PtrToStringUTF8(sysPath)
            ?? throw new InvalidDataException("systemd returned an invalid device-event sysfs path.");
        var actionResult = LinuxEvdevGamepadDiscovery.SystemdNative.DeviceGetPropertyValue(device, "ACTION", out var action);

        if (actionResult == -2)
        {
            return;
        }

        LinuxEvdevGamepadDiscovery.EnsureSuccess(actionResult, "read the device-event action");
        var actionText = Marshal.PtrToStringUTF8(action)
            ?? throw new InvalidDataException("systemd returned an invalid device-event action.");

        if (actionText is "remove" or "unbind" or "offline")
        {
            if (_devices.Remove(sysPathText, out var removedDevice))
            {
                changes.Add(CreateChange(PhysicalInputEventKind.DeviceRemoved, removedDevice));
            }

            return;
        }

        if (actionText is not ("add" or "bind" or "change" or "move" or "online"))
        {
            return;
        }

        if (!LinuxEvdevGamepadDiscovery.TryGetGamepadNode(device, out var currentDevice))
        {
            if (_devices.Remove(sysPathText, out var removedDevice))
            {
                changes.Add(CreateChange(PhysicalInputEventKind.DeviceRemoved, removedDevice));
            }

            return;
        }

        if (_devices.TryGetValue(sysPathText, out var previousDevice))
        {
            if (string.Equals(previousDevice.DeviceNode, currentDevice.DeviceNode, StringComparison.Ordinal))
            {
                return;
            }

            _ = _devices.Remove(sysPathText);
            changes.Add(CreateChange(PhysicalInputEventKind.DeviceRemoved, previousDevice));
        }

        _devices.Add(sysPathText, currentDevice);
        changes.Add(CreateChange(PhysicalInputEventKind.DeviceAdded, currentDevice));
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_isDisposed, this);

    internal readonly record struct DeviceChange(
        PhysicalInputEventKind Kind,
        LinuxEvdevGamepadDevice Device);
}
