// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
    private readonly HostEventSequenceLease _sequenceLease;
    private readonly TimeProvider _timeProvider;

    internal PlatformInputBroker(uint ownerId, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ownerId);

        _ownerId = ownerId;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _originTimestamp = _timeProvider.GetTimestamp();
        _sequenceLease = new HostEventSequenceLease();
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

            if ((sample.Kind is PhysicalInputEventKind.DeviceAdded or PhysicalInputEventKind.DeviceRemoved)
                ? (sample.Window is not null)
                : (sample.Window is null))
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

            if (sample.Kind == PhysicalInputEventKind.DeviceAdded)
            {
                if (isNewDevice)
                {
                    events.Add(CreatePhysicalInputEvent(sample, device, hostTimestamp));
                }

                continue;
            }

            if (sample.Kind == PhysicalInputEventKind.DeviceRemoved)
            {
                if (device is not { } removedDevice)
                {
                    throw new InvalidOperationException("A device-removal sample has no issued device handle.");
                }

                events.Add(CreatePhysicalInputEvent(sample, removedDevice, hostTimestamp));
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

            events.Add(CreatePhysicalInputEvent(sample, device, hostTimestamp));
        }

        return new PlatformInputCaptureBatch(events.ToImmutable(), discardedSampleCount);
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
        _sequenceLease.Revoke();
        _isDisposed = true;
    }

    private PhysicalInputEvent CreatePhysicalInputEvent(
        PlatformInputSample sample,
        InputDeviceHandle? device,
        long hostTimestamp)
    {
        PhysicalInputValue? value = null;

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
            flags: sample.Diagnostics);
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
