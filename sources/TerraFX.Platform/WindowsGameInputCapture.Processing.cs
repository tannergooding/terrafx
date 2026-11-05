// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using GameInputSharp.Abstractions;
using GameInputSharp.Devices;
using TerraFX.Input;

namespace TerraFX.Platform;

internal sealed partial class WindowsGameInputCapture
{
    private void AddGamepad(GamepadDevice gamepad)
    {
        if (_gamepadsById.Count >= MaximumGamepadDevices)
        {
            gamepad.Dispose();
            throw new InvalidOperationException($"GameInput exposed more than {MaximumGamepadDevices} simultaneous gamepads.");
        }

        var identity = AllocateNativeIdentity(gamepad.DeviceId);

        if (!_manager.RegisterReadingCallback(gamepad, GameInputKinds.Gamepad, out var callbackToken))
        {
            gamepad.Dispose();
            throw new InvalidOperationException($"GameInput could not register readings for device {gamepad.DeviceId}.");
        }

        var capture = new GamepadCapture(gamepad, identity, callbackToken);

        if (!_gamepadsById.TryAdd(gamepad.DeviceId, capture))
        {
            _ = _manager.UnregisterCallback(callbackToken);
            gamepad.Dispose();
            throw new InvalidOperationException("The GameInput device identity was duplicated during admission.");
        }

        if (!_devicesByNativeIdentity.TryAdd(identity, capture))
        {
            _ = _gamepadsById.Remove(gamepad.DeviceId);
            _ = _manager.UnregisterCallback(callbackToken);
            gamepad.Dispose();
            throw new InvalidOperationException("The GameInput native identity was duplicated during admission.");
        }

        ReconcileCurrentState(capture, GetNativeTimestamp());
    }

    private nint AllocateNativeIdentity(string deviceId)
    {
        foreach (var capture in _devicesByNativeIdentity.Values)
        {
            if (StringComparer.Ordinal.Equals(capture.DeviceId, deviceId))
            {
                throw new InvalidOperationException("A GameInput device identity is already active.");
            }
        }

        if (_nextNativeIdentity <= 0)
        {
            throw new InvalidOperationException("The GameInput native identity space is exhausted.");
        }

        return checked((nint)_nextNativeIdentity++);
    }

    private void CaptureCallbackFailure(Exception exception)
    {
        var existing = _callbackFailure;
        _callbackFailure = (existing is null)
            ? ExceptionDispatchInfo.Capture(exception)
            : ExceptionDispatchInfo.Capture(new AggregateException(existing.SourceException, exception));
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "The third-party GameInput dispatcher swallows callback exceptions; retain them and rethrow after it returns.")]
    private void CaptureReading(object? sender, ReadingCallbackEventArgs arguments)
    {
        _ = sender;
        var nativeDevice = IntPtr.Zero;

        try
        {
            nativeDevice = _manager.GetDeviceFromReading(arguments.Reading);

            if (nativeDevice == IntPtr.Zero)
            {
                _deviceListDirty = true;
                return;
            }

            GamepadCapture? capture = null;

            foreach (var candidate in _devicesByNativeIdentity.Values)
            {
                if (candidate.Device.GetDevicePointer() == nativeDevice)
                {
                    capture = candidate;
                    break;
                }
            }

            if (capture is null)
            {
                _deviceListDirty = true;
                return;
            }

            if (_manager.GetGamepadStateFromReading(arguments.Reading) is not { } nativeState)
            {
                throw new InvalidDataException($"GameInput returned no gamepad state for device {capture.DeviceId}.");
            }

            var state = WindowsGamepadStateMapper.FromGameInput(nativeState);
            var nativeTimestamp = WindowsGamepadStateMapper.ConvertNativeTimestamp(
                _manager.GetReadingTimestamp(arguments.Reading));
            var monotonicTimestamp = _timeProvider.GetTimestamp();

            if (capture.NeedsAnnouncement || capture.NeedsReconciliation || arguments.HasOverrunOccurred)
            {
                EmitReconciliation(capture, state, nativeTimestamp, monotonicTimestamp);
            }
            else
            {
                EmitChanges(capture, state, nativeTimestamp, monotonicTimestamp);
            }

            capture.LastState = state;
        }
        catch (Exception exception)
        {
            CaptureCallbackFailure(exception);
        }
        finally
        {
            if (nativeDevice != IntPtr.Zero)
            {
                _ = Marshal.Release(nativeDevice);
            }

            arguments.Reading.Dispose();
        }
    }

    private void EmitChanges(
        GamepadCapture capture,
        in PlatformGamepadState state,
        long nativeTimestamp,
        long monotonicTimestamp)
    {
        var changes = WindowsGamepadStateMapper.GetChanges(capture.LastState, state);

        for (var index = 0; index < changes.Length; index++)
        {
            var sample = WindowsGamepadStateMapper.CreateStateSample(
                capture.NativeIdentity,
                changes[index],
                nativeTimestamp,
                monotonicTimestamp);

            if (!_samples.TryEnqueue(in sample))
            {
                capture.NeedsReconciliation = true;
                return;
            }
        }

        capture.NeedsReconciliation = false;
    }

    private void EmitReconciliation(
        GamepadCapture capture,
        in PlatformGamepadState state,
        long nativeTimestamp,
        long monotonicTimestamp)
    {
        if (capture.NeedsAnnouncement)
        {
            var deviceAdded = WindowsGamepadStateMapper.CreateLifecycleSample(
                PhysicalInputEventKind.DeviceAdded,
                capture.NativeIdentity,
                nativeTimestamp,
                monotonicTimestamp);

            if (!_samples.TryEnqueue(in deviceAdded))
            {
                capture.NeedsReconciliation = true;
                return;
            }

            capture.NeedsAnnouncement = false;
        }

        var reconciliation = new PlatformInputSample(
            PhysicalInputEventKind.StateReconciliation,
            Window: null,
            capture.NativeIdentity,
            PlatformInputDeviceKind.Gamepad,
            NativeTimestamp: nativeTimestamp,
            NativeClockDomain: "GameInput",
            MonotonicTimestamp: monotonicTimestamp,
            ReconciliationState: WindowsGamepadStateMapper.CreateState(state));

        if (!_samples.TryEnqueue(in reconciliation))
        {
            capture.NeedsReconciliation = true;
            return;
        }

        capture.NeedsReconciliation = false;
    }

    private void EnsureOwnerThread()
    {
        if (_ownerThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("Windows GameInput capture is restricted to its host-owner thread.");
        }
    }

    private long GetNativeTimestamp()
        => WindowsGamepadStateMapper.ConvertNativeTimestamp(_manager.GetCurrentTimestamp());

    private void OnDeviceChanged(object? sender, DeviceCallbackEventArgs arguments)
    {
        _ = sender;
        _ = arguments;
        _deviceListDirty = true;
    }

    private void OnReading(object? sender, ReadingCallbackEventArgs arguments)
        => CaptureReading(sender, arguments);

    private void ReconcileCurrentState(GamepadCapture capture, long nativeTimestamp)
    {
        if (_manager.GetCurrentGamepadState(capture.Device) is not { } nativeState)
        {
            return;
        }

        var current = WindowsGamepadStateMapper.FromGameInput(nativeState);
        var monotonicTimestamp = _timeProvider.GetTimestamp();

        if (capture.NeedsAnnouncement || capture.NeedsReconciliation)
        {
            EmitReconciliation(capture, current, nativeTimestamp, monotonicTimestamp);
        }

        capture.LastState = current;
    }

    private void RemoveGamepad(GamepadCapture capture, long nativeTimestamp, long monotonicTimestamp)
    {
        if (!_manager.UnregisterCallback(capture.ReadingCallbackToken))
        {
            throw new InvalidOperationException($"GameInput stopped tracking device {capture.DeviceId} before owner retirement.");
        }

        _ = _gamepadsById.Remove(capture.DeviceId);
        _ = _devicesByNativeIdentity.Remove(capture.NativeIdentity);
        capture.Device.Dispose();
        var deviceRemoved = WindowsGamepadStateMapper.CreateLifecycleSample(
            PhysicalInputEventKind.DeviceRemoved,
            capture.NativeIdentity,
            nativeTimestamp,
            monotonicTimestamp);

        if (!_samples.TryEnqueue(in deviceRemoved))
        {
            if (_pendingRemovals.Count >= MaximumPendingRemovals)
            {
                throw new InvalidOperationException("The bounded GameInput removal queue is exhausted.");
            }

            _pendingRemovals.Enqueue(deviceRemoved);
        }
    }

    private void RetryPendingRemovals()
    {
        while (_pendingRemovals.TryPeek(out var sample))
        {
            if (!_samples.TryEnqueue(in sample))
            {
                return;
            }

            _ = _pendingRemovals.Dequeue();
        }
    }

    private void SynchronizeGamepads()
    {
        _deviceListDirty = false;
        var devices = _manager.GetDevices();
        var discoveredIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var inputDevice in devices)
        {
            if (inputDevice is not GamepadDevice gamepad)
            {
                inputDevice.Dispose();
                continue;
            }

            var deviceId = gamepad.DeviceId;

            if (string.IsNullOrWhiteSpace(deviceId) || !discoveredIds.Add(deviceId))
            {
                gamepad.Dispose();
                throw new InvalidDataException("GameInput returned an empty or duplicate gamepad device identity.");
            }

            if (_gamepadsById.TryGetValue(deviceId, out var current)
                && (current.Device.GetDevicePointer() == gamepad.GetDevicePointer()))
            {
                gamepad.Dispose();
                continue;
            }

            if (current is not null)
            {
                RemoveGamepad(current, GetNativeTimestamp(), _timeProvider.GetTimestamp());
            }

            AddGamepad(gamepad);
        }

        foreach (var capture in new List<GamepadCapture>(_gamepadsById.Values))
        {
            if (!discoveredIds.Contains(capture.DeviceId))
            {
                RemoveGamepad(capture, GetNativeTimestamp(), _timeProvider.GetTimestamp());
            }
        }
    }

    private void ThrowCallbackFailure()
    {
        var failure = _callbackFailure;
        _callbackFailure = null;
        failure?.Throw();
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_isDisposed, this);
}
