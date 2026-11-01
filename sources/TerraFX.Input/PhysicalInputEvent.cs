// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Text;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Input;

/// <summary>Stores one immutable host-captured physical input event.</summary>
public sealed class PhysicalInputEvent
{
    internal PhysicalInputEvent(
        PhysicalInputEventKind kind,
        long hostTimestamp,
        long hostSequence,
        InputDeviceHandle? device = null,
        WindowHandle? window = null,
        PhysicalInputValue? value = null,
        Vector2? pointerPosition = null,
        Vector2? pointerDelta = null,
        Vector2? pointerContentScale = null,
        float? scrollValue = null,
        PointerScrollUnit? scrollUnit = null,
        ushort? playerSlot = null,
        ushort? nativeUsagePage = null,
        ushort? nativeUsage = null,
        uint? nativeScanCode = null,
        uint? nativeVirtualKey = null,
        long? nativeTimestamp = null,
        string? nativeClockDomain = null,
        PhysicalInputEventDiagnostic flags = PhysicalInputEventDiagnostic.None,
        ImmutableArray<PhysicalInputValue> reconciliationState = default)
    {
        if (!Enum.IsDefined(kind) || (kind == PhysicalInputEventKind.None))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(hostTimestamp);
        ArgumentOutOfRangeException.ThrowIfLessThan(hostSequence, 1);

        if ((device is { IsValid: false }) || (window is { IsValid: false }))
        {
            throw new ArgumentException("Physical input identities must be valid.");
        }

        if ((device is { } sourceDevice) && (window is { } sourceWindow) && (sourceDevice.OwnerId != sourceWindow.OwnerId))
        {
            throw new ArgumentException("The event device and window must belong to the same host.");
        }

        if (((flags & ~PhysicalInputEventDiagnostic.TimestampUncertain) != 0)
            || !Enum.IsDefined(flags))
        {
            throw new ArgumentOutOfRangeException(nameof(flags));
        }

        if ((nativeTimestamp is null) != (nativeClockDomain is null)
            || ((nativeClockDomain is not null)
                && (string.IsNullOrWhiteSpace(nativeClockDomain)
                    || nativeClockDomain.Contains('\0', StringComparison.Ordinal)
                    || (Encoding.UTF8.GetByteCount(nativeClockDomain) > 64))))
        {
            throw new ArgumentException("A native timestamp requires a bounded clock-domain label.", nameof(nativeClockDomain));
        }

        if ((nativeUsagePage is null) != (nativeUsage is null))
        {
            throw new ArgumentException("A native HID usage requires both a usage page and usage.", nameof(nativeUsage));
        }

        ValidatePayload(
            kind,
            device,
            window,
            value,
            pointerPosition,
            pointerDelta,
            pointerContentScale,
            scrollValue,
            scrollUnit,
            playerSlot,
            nativeUsagePage,
            nativeUsage,
            nativeScanCode,
            nativeVirtualKey,
            reconciliationState);

        Kind = kind;
        HostSequence = hostSequence;
        HostTimestamp = hostTimestamp;
        Device = device;
        Window = window;
        Value = value;
        PointerPosition = pointerPosition;
        PointerDelta = pointerDelta;
        PointerContentScale = pointerContentScale;
        ScrollValue = scrollValue;
        ScrollUnit = scrollUnit;
        PlayerSlot = playerSlot;
        NativeUsagePage = nativeUsagePage;
        NativeUsage = nativeUsage;
        NativeScanCode = nativeScanCode;
        NativeVirtualKey = nativeVirtualKey;
        NativeTimestamp = nativeTimestamp;
        NativeClockDomain = nativeClockDomain;
        Flags = flags;
        ReconciliationState = reconciliationState;
    }

    /// <summary>Gets the physical device identity, when the event is device-scoped.</summary>
    public InputDeviceHandle? Device { get; }

    /// <summary>Gets diagnostic flags for this capture.</summary>
    public PhysicalInputEventDiagnostic Flags { get; }

    /// <summary>Gets the process-wide capture sequence assigned by the Runtime host broker.</summary>
    public long HostSequence { get; }

    /// <summary>Gets the nonnegative host observation timestamp in TimeSpan ticks.</summary>
    public long HostTimestamp { get; }

    /// <summary>Gets the kind of physical event.</summary>
    public PhysicalInputEventKind Kind { get; }

    /// <summary>Gets the optional native clock-domain label, limited to 64 UTF-8 bytes.</summary>
    public string? NativeClockDomain { get; }

    /// <summary>Gets the optional native keyboard scan code.</summary>
    public uint? NativeScanCode { get; }

    /// <summary>Gets the optional native timestamp, retained for diagnostics only.</summary>
    public long? NativeTimestamp { get; }

    /// <summary>Gets the optional native HID usage.</summary>
    public ushort? NativeUsage { get; }

    /// <summary>Gets the optional native HID usage page.</summary>
    public ushort? NativeUsagePage { get; }

    /// <summary>Gets the optional native keyboard virtual key.</summary>
    public uint? NativeVirtualKey { get; }

    /// <summary>Gets the optional assigned player slot for a device assignment event.</summary>
    public ushort? PlayerSlot { get; }

    /// <summary>Gets the content scale associated with an absolute pointer position.</summary>
    public Vector2? PointerContentScale { get; }

    /// <summary>Gets the relative pointer displacement in physical pixels.</summary>
    public Vector2? PointerDelta { get; }

    /// <summary>Gets the absolute window-local pointer position in physical pixels.</summary>
    public Vector2? PointerPosition { get; }

    /// <summary>Gets the complete source state for a reconciliation event.</summary>
    public ImmutableArray<PhysicalInputValue> ReconciliationState { get; }

    /// <summary>Gets the unit associated with <see cref="ScrollValue"/>.</summary>
    public PointerScrollUnit? ScrollUnit { get; }

    /// <summary>Gets the normalized vertical or horizontal scroll value.</summary>
    public float? ScrollValue { get; }

    /// <summary>Gets the normalized control value, when this event carries one.</summary>
    public PhysicalInputValue? Value { get; }

    /// <summary>Gets the window identity, when the event is window-scoped.</summary>
    public WindowHandle? Window { get; }

    private static void ValidatePayload(
        PhysicalInputEventKind kind,
        InputDeviceHandle? device,
        WindowHandle? window,
        PhysicalInputValue? value,
        Vector2? pointerPosition,
        Vector2? pointerDelta,
        Vector2? pointerContentScale,
        float? scrollValue,
        PointerScrollUnit? scrollUnit,
        ushort? playerSlot,
        ushort? nativeUsagePage,
        ushort? nativeUsage,
        uint? nativeScanCode,
        uint? nativeVirtualKey,
        ImmutableArray<PhysicalInputValue> reconciliationState)
    {
        static void Ensure(bool condition, PhysicalInputEventKind eventKind)
        {
            if (!condition)
            {
                throw new ArgumentException($"The payload is invalid for physical input event kind {eventKind}.");
            }
        }

        static void EnsureNoOtherPayload(
            Vector2? pointerPosition,
            Vector2? pointerDelta,
            Vector2? pointerContentScale,
            float? scrollValue,
            PointerScrollUnit? scrollUnit,
            ushort? playerSlot,
            ImmutableArray<PhysicalInputValue> reconciliationState,
            PhysicalInputEventKind eventKind)
        {
            Ensure(pointerPosition is null
                && pointerDelta is null
                && pointerContentScale is null
                && scrollValue is null
                && scrollUnit is null
                && playerSlot is null
                && reconciliationState.IsDefault, eventKind);
        }

        var hasNativeKeyboardCode = nativeUsagePage is not null || nativeScanCode is not null || nativeVirtualKey is not null;

        switch (kind)
        {
            case PhysicalInputEventKind.KeyDown:
            case PhysicalInputEventKind.KeyUp:
            case PhysicalInputEventKind.KeyRepeat:
            {
                Ensure(device is not null && window is not null, kind);

                if (value is { } keyValue)
                {
                    Ensure(keyValue.Kind == PhysicalInputValueKind.Digital
                        && keyValue.DigitalControl is { Kind: DigitalControlKind.KeyboardHid or DigitalControlKind.LogicalKey }
                        && (keyValue.IsDown == (kind != PhysicalInputEventKind.KeyUp)), kind);

                    if ((keyValue.DigitalControl?.Keyboard is { Kind: KeyboardControlKind.KeyboardHid } keyboard)
                        && (nativeUsagePage is { } usagePage)
                        && (nativeUsage is { } usage))
                    {
                        Ensure((keyboard.UsagePage == usagePage) && (keyboard.Usage == usage), kind);
                    }
                }
                else
                {
                    Ensure(hasNativeKeyboardCode, kind);
                }

                EnsureNoOtherPayload(pointerPosition, pointerDelta, pointerContentScale, scrollValue, scrollUnit, playerSlot, reconciliationState, kind);
                break;
            }

            case PhysicalInputEventKind.PointerButtonDown:
            case PhysicalInputEventKind.PointerButtonUp:
            {
                Ensure(device is not null && window is not null, kind);
                Ensure(value is { Kind: PhysicalInputValueKind.Digital }
                    && value.Value.DigitalControl is { Kind: DigitalControlKind.MouseButton }
                    && (value.Value.IsDown == (kind == PhysicalInputEventKind.PointerButtonDown)), kind);
                Ensure(!hasNativeKeyboardCode, kind);
                EnsureNoOtherPayload(pointerPosition, pointerDelta, pointerContentScale, scrollValue, scrollUnit, playerSlot, reconciliationState, kind);
                break;
            }

            case PhysicalInputEventKind.GamepadButtonDown:
            case PhysicalInputEventKind.GamepadButtonUp:
            {
                Ensure(device is not null && window is null, kind);
                Ensure(value is { Kind: PhysicalInputValueKind.Digital }
                    && value.Value.DigitalControl is { Kind: DigitalControlKind.GamepadButton }
                    && (value.Value.IsDown == (kind == PhysicalInputEventKind.GamepadButtonDown)), kind);
                Ensure(!hasNativeKeyboardCode, kind);
                EnsureNoOtherPayload(pointerPosition, pointerDelta, pointerContentScale, scrollValue, scrollUnit, playerSlot, reconciliationState, kind);
                break;
            }

            case PhysicalInputEventKind.GamepadAxis:
            case PhysicalInputEventKind.GamepadStick:
            case PhysicalInputEventKind.GamepadTrigger:
            {
                Ensure(device is not null && window is null, kind);
                var requiredKind = kind switch {
                    PhysicalInputEventKind.GamepadAxis => PhysicalInputValueKind.GamepadAxis,
                    PhysicalInputEventKind.GamepadStick => PhysicalInputValueKind.GamepadStick,
                    _ => PhysicalInputValueKind.GamepadTrigger,
                };
                Ensure(value is { } physicalValue && (physicalValue.Kind == requiredKind), kind);
                Ensure(!hasNativeKeyboardCode, kind);
                EnsureNoOtherPayload(pointerPosition, pointerDelta, pointerContentScale, scrollValue, scrollUnit, playerSlot, reconciliationState, kind);
                break;
            }

            case PhysicalInputEventKind.PointerAbsoluteMotion:
            {
                Ensure(device is not null && window is not null, kind);
                Ensure(pointerPosition is { } position && position.IsFinite().All(), kind);
                Ensure(pointerContentScale is { } scale
                    && scale.IsFinite().All()
                    && (scale.X > 0.0f)
                    && (scale.Y > 0.0f), kind);
                Ensure(value is null && pointerDelta is null && scrollValue is null && scrollUnit is null, kind);
                Ensure(playerSlot is null && !hasNativeKeyboardCode && reconciliationState.IsDefault, kind);
                break;
            }

            case PhysicalInputEventKind.PointerRelativeMotion:
            {
                Ensure(device is not null && window is not null, kind);
                Ensure(pointerDelta is { } delta && delta.IsFinite().All(), kind);
                Ensure(value is null && pointerPosition is null && pointerContentScale is null && scrollValue is null && scrollUnit is null, kind);
                Ensure(playerSlot is null && !hasNativeKeyboardCode && reconciliationState.IsDefault, kind);
                break;
            }

            case PhysicalInputEventKind.VerticalScroll:
            case PhysicalInputEventKind.HorizontalScroll:
            {
                Ensure(device is not null && window is not null, kind);
                Ensure(scrollValue is { } scroll && float.IsFinite(scroll), kind);
                Ensure(scrollUnit is { } unit && Enum.IsDefined(unit) && (unit != PointerScrollUnit.None), kind);
                Ensure(value is null && pointerPosition is null && pointerDelta is null && pointerContentScale is null, kind);
                Ensure(playerSlot is null && !hasNativeKeyboardCode && reconciliationState.IsDefault, kind);
                break;
            }

            case PhysicalInputEventKind.FocusGained:
            case PhysicalInputEventKind.FocusLost:
            case PhysicalInputEventKind.WindowEntered:
            case PhysicalInputEventKind.WindowLeft:
            {
                Ensure(window is not null && device is null, kind);
                EnsureNoOtherPayload(pointerPosition, pointerDelta, pointerContentScale, scrollValue, scrollUnit, playerSlot, reconciliationState, kind);
                Ensure(value is null && !hasNativeKeyboardCode, kind);
                break;
            }

            case PhysicalInputEventKind.DeviceAdded:
            case PhysicalInputEventKind.DeviceRemoved:
            case PhysicalInputEventKind.DeviceAssigned:
            {
                Ensure(device is not null && window is null, kind);
                Ensure((kind == PhysicalInputEventKind.DeviceAssigned) || (playerSlot is null), kind);
                EnsureNoOtherPayload(pointerPosition, pointerDelta, pointerContentScale, scrollValue, scrollUnit, null, reconciliationState, kind);
                Ensure(value is null && !hasNativeKeyboardCode, kind);
                break;
            }

            case PhysicalInputEventKind.StateReconciliation:
            {
                Ensure((device is not null) || (window is not null), kind);
                Ensure(reconciliationState.IsDefault == false, kind);
                Ensure(value is null && pointerPosition is null && pointerDelta is null && pointerContentScale is null
                    && scrollValue is null && scrollUnit is null && playerSlot is null && !hasNativeKeyboardCode, kind);

                foreach (var state in reconciliationState)
                {
                    Ensure(state.Kind != PhysicalInputValueKind.None, kind);
                }

                break;
            }
        }
    }
}
