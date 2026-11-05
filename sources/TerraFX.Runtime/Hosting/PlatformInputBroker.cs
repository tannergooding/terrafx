// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using TerraFX;
using TerraFX.Input;
using TerraFX.Numerics;
using TerraFX.Platform;

namespace TerraFX.Hosting;

internal sealed class PlatformInputBroker
{
    private readonly Dictionary<uint, uint> _deviceGenerations;
    private readonly Dictionary<InputDeviceHandle, PlatformInputDeviceKind> _deviceKinds;
    private readonly Dictionary<nint, InputDeviceHandle> _devicesByNativeIdentity;
    private readonly SortedSet<uint> _freeDeviceIndices;
    private bool _isDisposed;
    private long _lastHostTimestamp;
    private ulong _nextDeviceIndex;
    private readonly long _originTimestamp;
    private readonly uint _ownerId;
    private readonly int _ownerThreadId;
    private readonly bool _ownsSequenceLease;
    private readonly HostEventSequenceLease _sequenceLease;
    private readonly TimeProvider _timeProvider;

    internal PlatformInputBroker(
        uint ownerId,
        TimeProvider? timeProvider = null,
        HostEventSequenceLease? sequenceLease = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ownerId);

        _ownerId = ownerId;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _originTimestamp = _timeProvider.GetTimestamp();
        _ownsSequenceLease = sequenceLease is null;
        _sequenceLease = sequenceLease ?? new HostEventSequenceLease();
        _deviceGenerations = [];
        _devicesByNativeIdentity = [];
        _deviceKinds = [];
        _freeDeviceIndices = [];
    }

    private static PlatformInputSample CreateDeviceLifecycleSample(
        PlatformInputSample sample,
        PhysicalInputEventKind kind)
        => new PlatformInputSample(
            kind,
            Window: null,
            sample.NativeDeviceIdentity,
            sample.DeviceKind,
            NativeTimestamp: sample.NativeTimestamp,
            NativeClockDomain: sample.NativeClockDomain,
            Diagnostics: sample.Diagnostics,
            MonotonicTimestamp: sample.MonotonicTimestamp);

    private static PhysicalInputValue CreateGamepadValue(PlatformInputSample sample)
    {
        switch (sample.Kind)
        {
            case PhysicalInputEventKind.GamepadButtonDown:
            case PhysicalInputEventKind.GamepadButtonUp:
            {
                var isDown = sample.Kind == PhysicalInputEventKind.GamepadButtonDown;

                if ((sample.GamepadButton is not { } button)
                    || (sample.IsDown != isDown)
                    || !Enum.IsDefined(button))
                {
                    throw new InvalidOperationException("A platform gamepad-button sample has an invalid control state.");
                }

                return new PhysicalInputValue(button, isDown);
            }

            case PhysicalInputEventKind.GamepadAxis:
            {
                if ((sample.Axis is not { } axis) || (sample.AxisValue is not { } axisValue))
                {
                    throw new InvalidOperationException("A platform gamepad-axis sample is incomplete.");
                }

                return new PhysicalInputValue(axis, axisValue);
            }

            case PhysicalInputEventKind.GamepadStick:
            {
                if ((sample.Stick is not { } stick) || (sample.StickValue is not { } stickValue))
                {
                    throw new InvalidOperationException("A platform gamepad-stick sample is incomplete.");
                }

                return new PhysicalInputValue(stick, stickValue);
            }

            case PhysicalInputEventKind.GamepadTrigger:
            {
                if ((sample.Trigger is not { } trigger) || (sample.TriggerValue is not { } triggerValue))
                {
                    throw new InvalidOperationException("A platform gamepad-trigger sample is incomplete.");
                }

                return new PhysicalInputValue(trigger, triggerValue);
            }

            default:
            {
                throw new ArgumentOutOfRangeException(nameof(sample));
            }
        }
    }

    private static PhysicalInputValue CreateReconciliationValue(PlatformInputStateValue value)
    {
        switch (value.Kind)
        {
            case PlatformInputStateValueKind.GamepadButton:
            {
                if ((value.GamepadButton is not { } button)
                    || (value.IsDown is not { } isDown)
                    || !Enum.IsDefined(button))
                {
                    throw new InvalidOperationException("A gamepad reconciliation button is incomplete.");
                }

                return new PhysicalInputValue(button, isDown);
            }

            case PlatformInputStateValueKind.GamepadAxis:
            {
                if ((value.Axis is not { } axis)
                    || (value.AxisValue is not { } axisValue)
                    || !Enum.IsDefined(axis))
                {
                    throw new InvalidOperationException("A gamepad reconciliation axis is incomplete.");
                }

                return new PhysicalInputValue(axis, axisValue);
            }

            case PlatformInputStateValueKind.GamepadStick:
            {
                if ((value.Stick is not { } stick)
                    || (value.StickValue is not { } stickValue)
                    || !Enum.IsDefined(stick))
                {
                    throw new InvalidOperationException("A gamepad reconciliation stick is incomplete.");
                }

                return new PhysicalInputValue(stick, stickValue);
            }

            case PlatformInputStateValueKind.GamepadTrigger:
            {
                if ((value.Trigger is not { } trigger)
                    || (value.TriggerValue is not { } triggerValue)
                    || !Enum.IsDefined(trigger))
                {
                    throw new InvalidOperationException("A gamepad reconciliation trigger is incomplete.");
                }

                return new PhysicalInputValue(trigger, triggerValue);
            }

            default:
            {
                throw new InvalidOperationException("A gamepad reconciliation state contains an unknown value kind.");
            }
        }
    }

    private static Diagnostic CreateUnmappedGamepadButtonsDiagnostic(
        PlatformInputSample sample,
        InputDeviceHandle device)
        => new(
            "platform-gamepad-unmapped-buttons",
            DiagnosticSeverity.Warning,
            sample.MonotonicTimestamp,
            hostId: device.OwnerId,
            worldGuid: null,
            deviceId: null,
            surfaceId: null,
            deviceEpoch: device.Generation,
            surfaceEpoch: null,
            swapchainEpoch: null,
            tick: null,
            systemId: null,
            passId: null,
            assetId: null,
            commandIdentity: null,
            operation: "PlatformInputBroker.GameInput",
            nativeResult: null,
            message: $"The GameInput device reported unmapped button bits 0x{sample.UnmappedGamepadButtonMask:X8}; the controls were not guessed.",
            context: [
                new DiagnosticContextEntry(
                    "deviceIndex",
                    device.Index.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticContextEntry(
                    "deviceGeneration",
                    device.Generation.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticContextEntry(
                    "unmappedButtonMask",
                    $"0x{sample.UnmappedGamepadButtonMask:X8}"),
            ]);

    private static bool IsDeviceScoped(PhysicalInputEventKind kind)
        => kind is PhysicalInputEventKind.KeyDown
            or PhysicalInputEventKind.KeyRepeat
            or PhysicalInputEventKind.KeyUp
            or PhysicalInputEventKind.PointerButtonDown
            or PhysicalInputEventKind.PointerButtonUp
            or PhysicalInputEventKind.PointerAbsoluteMotion
            or PhysicalInputEventKind.PointerRelativeMotion
            or PhysicalInputEventKind.VerticalScroll
            or PhysicalInputEventKind.HorizontalScroll
            or PhysicalInputEventKind.GamepadButtonDown
            or PhysicalInputEventKind.GamepadButtonUp
            or PhysicalInputEventKind.GamepadAxis
            or PhysicalInputEventKind.GamepadStick
            or PhysicalInputEventKind.GamepadTrigger
            or PhysicalInputEventKind.StateReconciliation
            or PhysicalInputEventKind.DeviceAdded
            or PhysicalInputEventKind.DeviceRemoved;

    private static bool IsGamepadInput(PhysicalInputEventKind kind)
        => kind is PhysicalInputEventKind.GamepadButtonDown
            or PhysicalInputEventKind.GamepadButtonUp
            or PhysicalInputEventKind.GamepadAxis
            or PhysicalInputEventKind.GamepadStick
            or PhysicalInputEventKind.GamepadTrigger;

    private static bool IsWindowless(PhysicalInputEventKind kind)
        => kind is PhysicalInputEventKind.GamepadButtonDown
            or PhysicalInputEventKind.GamepadButtonUp
            or PhysicalInputEventKind.GamepadAxis
            or PhysicalInputEventKind.GamepadStick
            or PhysicalInputEventKind.GamepadTrigger
            or PhysicalInputEventKind.StateReconciliation
            or PhysicalInputEventKind.DeviceAdded
            or PhysicalInputEventKind.DeviceRemoved;

    internal PlatformInputCaptureBatch Capture(
        PlatformInputSampleBatch sampleBatch,
        Func<WindowHandle, bool> isWindowActive)
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(isWindowActive);

        if (sampleBatch.Samples.IsDefault || (sampleBatch.DiscardedSampleCount < 0))
        {
            throw new ArgumentException("The platform input sample batch is invalid.", nameof(sampleBatch));
        }

        var events = ImmutableArray.CreateBuilder<PhysicalInputEvent>(sampleBatch.Samples.Length);
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var discardedSampleCount = sampleBatch.DiscardedSampleCount;

        foreach (var sample in sampleBatch.Samples)
        {
            if (!IsDeviceScoped(sample.Kind)
                && (sample.Kind is not PhysicalInputEventKind.FocusGained and not PhysicalInputEventKind.FocusLost))
            {
                throw new InvalidOperationException($"The platform input sample kind {sample.Kind} is not supported by the Runtime capture broker.");
            }

            if (IsDeviceScoped(sample.Kind) != (sample.NativeDeviceIdentity != 0))
            {
                throw new InvalidOperationException("The platform input sample has an invalid device source.");
            }

            if (IsWindowless(sample.Kind) ? (sample.Window is not null) : (sample.Window is null))
            {
                throw new InvalidOperationException("The platform input sample has an invalid window source.");
            }

            if ((sample.Kind is PhysicalInputEventKind.KeyDown or PhysicalInputEventKind.KeyRepeat or PhysicalInputEventKind.KeyUp)
                && (sample.DeviceKind != PlatformInputDeviceKind.Keyboard))
            {
                throw new InvalidOperationException("A keyboard input sample has a non-keyboard device class.");
            }

            if ((sample.Kind is PhysicalInputEventKind.PointerButtonDown
                    or PhysicalInputEventKind.PointerButtonUp
                    or PhysicalInputEventKind.PointerAbsoluteMotion
                    or PhysicalInputEventKind.PointerRelativeMotion
                    or PhysicalInputEventKind.VerticalScroll
                    or PhysicalInputEventKind.HorizontalScroll)
                && (sample.DeviceKind != PlatformInputDeviceKind.Pointer))
            {
                throw new InvalidOperationException("A pointer input sample has a non-pointer device class.");
            }

            if (IsGamepadInput(sample.Kind) && (sample.DeviceKind != PlatformInputDeviceKind.Gamepad))
            {
                throw new InvalidOperationException("A gamepad input sample has a non-gamepad device class.");
            }

            if (sample.Kind is PhysicalInputEventKind.StateReconciliation)
            {
                if ((sample.DeviceKind != PlatformInputDeviceKind.Gamepad)
                    || sample.ReconciliationState.IsDefault
                    || (sample.Axis is not null)
                    || (sample.AxisValue is not null)
                    || (sample.GamepadButton is not null)
                    || (sample.IsDown is not null)
                    || (sample.Stick is not null)
                    || (sample.StickValue is not null)
                    || (sample.Trigger is not null)
                    || (sample.TriggerValue is not null))
                {
                    throw new InvalidOperationException("A platform state-reconciliation sample has an invalid source or payload.");
                }
            }
            else if (!sample.ReconciliationState.IsDefault)
            {
                throw new InvalidOperationException("A platform sample carries reconciliation state for a non-reconciliation event.");
            }

            if ((sample.UnmappedGamepadButtonMask != 0)
                && (sample.Kind != PhysicalInputEventKind.StateReconciliation))
            {
                throw new InvalidOperationException("A platform sample carries unmapped gamepad buttons outside reconciliation.");
            }

            var hostTimestamp = GetHostTimestamp(sample.MonotonicTimestamp);

            if (sample.Window is { } window)
            {
                if (!window.IsValid || (window.OwnerId != _ownerId))
                {
                    throw new InvalidOperationException("A platform input sample belongs to another host window.");
                }

                if (!isWindowActive(window))
                {
                    discardedSampleCount = checked(discardedSampleCount + 1);
                    continue;
                }
            }

            PhysicalInputValue? gamepadValue = IsGamepadInput(sample.Kind)
                ? CreateGamepadValue(sample)
                : null;
            var device = (InputDeviceHandle?)null;
            var isNewDevice = false;

            if (sample.Kind == PhysicalInputEventKind.DeviceRemoved)
            {
                if (!_devicesByNativeIdentity.TryGetValue(sample.NativeDeviceIdentity, out var existingDevice))
                {
                    discardedSampleCount = checked(discardedSampleCount + 1);
                    continue;
                }

                device = existingDevice;
            }
            else if (sample.NativeDeviceIdentity != 0)
            {
                device = GetDeviceHandle(sample.NativeDeviceIdentity, sample.DeviceKind, out isNewDevice);
            }
            else if (IsDeviceScoped(sample.Kind))
            {
                throw new InvalidOperationException("A device-scoped platform input sample has no native identity.");
            }

            if (sample.UnmappedGamepadButtonMask != 0)
            {
                diagnostics.Add(CreateUnmappedGamepadButtonsDiagnostic(
                    sample,
                    device ?? throw new InvalidOperationException("An unmapped gamepad diagnostic has no device identity.")));
            }

            if (sample.Kind == PhysicalInputEventKind.DeviceAdded)
            {
                if (isNewDevice)
                {
                    events.Add(CreatePhysicalInputEvent(sample, device, hostTimestamp, gamepadValue));
                }

                continue;
            }

            if (sample.Kind == PhysicalInputEventKind.DeviceRemoved)
            {
                if (device is not { } removedDevice)
                {
                    throw new InvalidOperationException("A device-removal sample has no issued device handle.");
                }

                events.Add(CreatePhysicalInputEvent(sample, removedDevice, hostTimestamp, gamepadValue));
                RemoveDevice(sample.NativeDeviceIdentity, removedDevice);
                continue;
            }

            if (isNewDevice)
            {
                if (device is not { } addedDevice)
                {
                    throw new InvalidOperationException("A native input sample has no issued device handle.");
                }

                events.Add(CreatePhysicalInputEvent(
                    CreateDeviceLifecycleSample(sample, PhysicalInputEventKind.DeviceAdded),
                    addedDevice,
                    hostTimestamp));
            }

            events.Add(CreatePhysicalInputEvent(sample, device, hostTimestamp, gamepadValue));
        }

        return new PlatformInputCaptureBatch(
            events.ToImmutable(),
            discardedSampleCount,
            diagnostics.ToImmutable());
    }

    internal long GetCurrentHostTimestamp()
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        var timestamp = _timeProvider.GetElapsedTime(_originTimestamp, _timeProvider.GetTimestamp()).Ticks;
        return Math.Max(timestamp, _lastHostTimestamp);
    }

    internal void Retire()
    {
        EnsureOwnerThread();

        if (_isDisposed)
        {
            return;
        }

        _deviceGenerations.Clear();
        _devicesByNativeIdentity.Clear();
        _deviceKinds.Clear();
        _freeDeviceIndices.Clear();

        if (_ownsSequenceLease)
        {
            _sequenceLease.Revoke();
        }

        _isDisposed = true;
    }

    private PhysicalInputEvent CreatePhysicalInputEvent(
        PlatformInputSample sample,
        InputDeviceHandle? device,
        long hostTimestamp,
        PhysicalInputValue? gamepadValue = null)
    {
        PhysicalInputValue? value = null;
        var reconciliationState = default(ImmutableArray<PhysicalInputValue>);

        switch (sample.Kind)
        {
            case PhysicalInputEventKind.KeyDown:
            case PhysicalInputEventKind.KeyRepeat:
            case PhysicalInputEventKind.KeyUp:
            {
                var isDown = sample.Kind != PhysicalInputEventKind.KeyUp;

                if (sample.IsDown != isDown)
                {
                    throw new InvalidOperationException("A platform keyboard sample has an inconsistent key state.");
                }

                if (sample.NativeUsagePage.HasValue != sample.NativeUsage.HasValue)
                {
                    throw new InvalidOperationException("A platform keyboard sample has an incomplete HID usage.");
                }

                if (sample.NativeUsagePage is { } mappedUsagePage
                    && sample.NativeUsage is { } mappedUsage)
                {
                    if (mappedUsagePage != 0x0007)
                    {
                        throw new InvalidOperationException("A platform keyboard sample uses an unsupported HID usage page.");
                    }

                    value = new PhysicalInputValue(
                        new DigitalControl(new KeyboardControl(mappedUsagePage, mappedUsage)),
                        isDown);
                }
                else if ((sample.NativeScanCode is null) && (sample.NativeVirtualKey is null))
                {
                    throw new InvalidOperationException("An unmapped platform key sample has no native key code.");
                }

                break;
            }

            case PhysicalInputEventKind.PointerButtonDown:
            case PhysicalInputEventKind.PointerButtonUp:
            {
                var isDown = sample.Kind == PhysicalInputEventKind.PointerButtonDown;

                if ((sample.MouseButton is not { } button)
                    || (sample.IsDown != isDown)
                    || !Enum.IsDefined(button))
                {
                    throw new InvalidOperationException("A platform pointer-button sample has an invalid control state.");
                }

                value = new PhysicalInputValue(new DigitalControl(button), isDown);
                break;
            }

            case PhysicalInputEventKind.GamepadButtonDown:
            case PhysicalInputEventKind.GamepadButtonUp:
            case PhysicalInputEventKind.GamepadAxis:
            case PhysicalInputEventKind.GamepadStick:
            case PhysicalInputEventKind.GamepadTrigger:
            {
                value = gamepadValue ?? CreateGamepadValue(sample);
                break;
            }

            case PhysicalInputEventKind.StateReconciliation:
            {
                if (sample.ReconciliationState.IsDefault)
                {
                    throw new InvalidOperationException("A platform state-reconciliation sample omitted its complete source state.");
                }

                var state = ImmutableArray.CreateBuilder<PhysicalInputValue>(sample.ReconciliationState.Length);

                for (var index = 0; index < sample.ReconciliationState.Length; index++)
                {
                    state.Add(CreateReconciliationValue(sample.ReconciliationState[index]));
                }

                reconciliationState = state.MoveToImmutable();
                break;
            }

            case PhysicalInputEventKind.PointerAbsoluteMotion:
            case PhysicalInputEventKind.PointerRelativeMotion:
            case PhysicalInputEventKind.VerticalScroll:
            case PhysicalInputEventKind.HorizontalScroll:
            case PhysicalInputEventKind.FocusGained:
            case PhysicalInputEventKind.FocusLost:
            case PhysicalInputEventKind.DeviceAdded:
            case PhysicalInputEventKind.DeviceRemoved:
            {
                break;
            }

            default:
            {
                throw new InvalidOperationException($"The platform input sample kind {sample.Kind} is not supported by the Runtime capture broker.");
            }
        }

        var hostSequence = _sequenceLease.Allocate();

        return new PhysicalInputEvent(
            sample.Kind,
            hostTimestamp,
            hostSequence,
            device,
            sample.Window,
            value,
            sample.PointerPosition,
            sample.PointerDelta,
            sample.PointerContentScale,
            sample.ScrollValue,
            sample.ScrollUnit,
            nativeUsagePage: sample.NativeUsagePage,
            nativeUsage: sample.NativeUsage,
            nativeScanCode: sample.NativeScanCode,
            nativeVirtualKey: sample.NativeVirtualKey,
            nativeTimestamp: sample.NativeTimestamp,
            nativeClockDomain: sample.NativeClockDomain,
            flags: sample.Diagnostics,
            reconciliationState: reconciliationState);
    }

    private void EnsureOwnerThread()
    {
        if (_ownerThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("The PlatformInputBroker is restricted to its host-owner thread.");
        }
    }

    private InputDeviceHandle GetDeviceHandle(
        nint nativeIdentity,
        PlatformInputDeviceKind deviceKind,
        out bool created)
    {
        ArgumentOutOfRangeException.ThrowIfZero(nativeIdentity);

        if (_devicesByNativeIdentity.TryGetValue(nativeIdentity, out var currentHandle))
        {
            var currentKind = _deviceKinds[currentHandle];

            if ((deviceKind != PlatformInputDeviceKind.Unknown)
                && (currentKind != PlatformInputDeviceKind.Unknown)
                && (deviceKind != currentKind))
            {
                throw new InvalidOperationException("A live native device identity changed its device class.");
            }

            if (currentKind == PlatformInputDeviceKind.Unknown)
            {
                _deviceKinds[currentHandle] = deviceKind;
            }

            created = false;
            return currentHandle;
        }

        uint index = 0;
        uint generation = 0;

        while ((_freeDeviceIndices.Count != 0) && (generation == 0))
        {
            index = _freeDeviceIndices.Min;
            _ = _freeDeviceIndices.Remove(index);
            var previousGeneration = _deviceGenerations[index];

            if (previousGeneration != uint.MaxValue)
            {
                generation = previousGeneration + 1;
            }
        }

        if (generation == 0)
        {
            if (_nextDeviceIndex > uint.MaxValue)
            {
                throw new InvalidOperationException("The Runtime exhausted its input-device handle slots.");
            }

            index = checked((uint)_nextDeviceIndex++);
            generation = 1;
        }

        _deviceGenerations[index] = generation;
        var handle = new InputDeviceHandle(_ownerId, index, generation);
        _devicesByNativeIdentity.Add(nativeIdentity, handle);
        _deviceKinds.Add(handle, deviceKind);
        created = true;
        return handle;
    }

    private long GetHostTimestamp(long monotonicTimestamp)
    {
        var timestamp = _timeProvider.GetElapsedTime(_originTimestamp, monotonicTimestamp).Ticks;

        if (timestamp < _lastHostTimestamp)
        {
            throw new InvalidOperationException("The host monotonic input timestamp regressed.");
        }

        _lastHostTimestamp = timestamp;
        return timestamp;
    }

    private void RemoveDevice(nint nativeIdentity, InputDeviceHandle expectedHandle)
    {
        if (!_devicesByNativeIdentity.Remove(nativeIdentity, out var currentHandle)
            || (currentHandle != expectedHandle)
            || !_deviceKinds.Remove(expectedHandle)
            || !_freeDeviceIndices.Add(expectedHandle.Index))
        {
            throw new InvalidOperationException("The Runtime input-device generation changed before removal.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
