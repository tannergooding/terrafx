// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Input;
using TerraFX.Numerics;

namespace TerraFX.Input.Tests;

[TestFixture]
internal sealed class PhysicalInputEventTests
{
    [Test]
    public void EventKindsAndDiagnosticsHaveStableValues()
    {
        Assert.That((int)PhysicalInputEventKind.None, Is.EqualTo(0));
        Assert.That((int)PhysicalInputEventKind.KeyDown, Is.EqualTo(1));
        Assert.That((int)PhysicalInputEventKind.KeyUp, Is.EqualTo(2));
        Assert.That((int)PhysicalInputEventKind.KeyRepeat, Is.EqualTo(3));
        Assert.That((int)PhysicalInputEventKind.PointerButtonDown, Is.EqualTo(4));
        Assert.That((int)PhysicalInputEventKind.PointerButtonUp, Is.EqualTo(5));
        Assert.That((int)PhysicalInputEventKind.PointerAbsoluteMotion, Is.EqualTo(6));
        Assert.That((int)PhysicalInputEventKind.PointerRelativeMotion, Is.EqualTo(7));
        Assert.That((int)PhysicalInputEventKind.VerticalScroll, Is.EqualTo(8));
        Assert.That((int)PhysicalInputEventKind.HorizontalScroll, Is.EqualTo(9));
        Assert.That((int)PhysicalInputEventKind.GamepadButtonDown, Is.EqualTo(10));
        Assert.That((int)PhysicalInputEventKind.GamepadButtonUp, Is.EqualTo(11));
        Assert.That((int)PhysicalInputEventKind.GamepadAxis, Is.EqualTo(12));
        Assert.That((int)PhysicalInputEventKind.GamepadStick, Is.EqualTo(13));
        Assert.That((int)PhysicalInputEventKind.GamepadTrigger, Is.EqualTo(14));
        Assert.That((int)PhysicalInputEventKind.FocusGained, Is.EqualTo(15));
        Assert.That((int)PhysicalInputEventKind.FocusLost, Is.EqualTo(16));
        Assert.That((int)PhysicalInputEventKind.WindowEntered, Is.EqualTo(17));
        Assert.That((int)PhysicalInputEventKind.WindowLeft, Is.EqualTo(18));
        Assert.That((int)PhysicalInputEventKind.DeviceAdded, Is.EqualTo(19));
        Assert.That((int)PhysicalInputEventKind.DeviceRemoved, Is.EqualTo(20));
        Assert.That((int)PhysicalInputEventKind.DeviceAssigned, Is.EqualTo(21));
        Assert.That((int)PhysicalInputEventKind.StateReconciliation, Is.EqualTo(22));
        Assert.That((int)PhysicalInputEventDiagnostic.None, Is.EqualTo(0));
        Assert.That((int)PhysicalInputEventDiagnostic.TimestampUncertain, Is.EqualTo(1));
        Assert.That((int)PointerScrollUnit.None, Is.EqualTo(0));
        Assert.That((int)PointerScrollUnit.Detents, Is.EqualTo(1));
        Assert.That((int)PointerScrollUnit.SurfaceScale, Is.EqualTo(2));
    }

    [Test]
    public void GamepadAndDeviceEventsPreserveAssignedValues()
    {
        var device = new InputDeviceHandle(3, 0, 1);
        var axis = new PhysicalInputEvent(
            PhysicalInputEventKind.GamepadAxis,
            4,
            5,
            device,
            value: new PhysicalInputValue(GamepadAxis.LeftX, 0.75f));
        var assignment = new PhysicalInputEvent(
            PhysicalInputEventKind.DeviceAssigned,
            6,
            7,
            device,
            playerSlot: 2);

        Assert.That(axis.Value?.Axis, Is.EqualTo(GamepadAxis.LeftX));
        Assert.That(axis.Value?.AxisValue, Is.EqualTo(0.75f));
        Assert.That(assignment.Device, Is.EqualTo(device));
        Assert.That(assignment.PlayerSlot, Is.EqualTo(2));
    }

    [Test]
    public void KeyboardEventRetainsCaptureIdentityAndNativeDiagnostics()
    {
        var device = new InputDeviceHandle(1, 0, 1);
        var window = new WindowHandle(1, 2, 1);
        var control = new DigitalControl(new KeyboardControl(7, 80));
        var value = new PhysicalInputValue(control, isDown: true);
        var input = new PhysicalInputEvent(
            PhysicalInputEventKind.KeyDown,
            hostTimestamp: 42,
            hostSequence: 17,
            device,
            window,
            value,
            nativeUsagePage: 7,
            nativeUsage: 80,
            nativeScanCode: 0x1E,
            nativeVirtualKey: 0x41,
            nativeTimestamp: 123,
            nativeClockDomain: "win32-qpc");

        Assert.That(input.Kind, Is.EqualTo(PhysicalInputEventKind.KeyDown));
        Assert.That(input.HostTimestamp, Is.EqualTo(42));
        Assert.That(input.HostSequence, Is.EqualTo(17));
        Assert.That(input.Device, Is.EqualTo(device));
        Assert.That(input.Window, Is.EqualTo(window));
        Assert.That(input.Value, Is.EqualTo(value));
        Assert.That(input.NativeUsagePage, Is.EqualTo(7));
        Assert.That(input.NativeUsage, Is.EqualTo(80));
        Assert.That(input.NativeScanCode, Is.EqualTo(0x1E));
        Assert.That(input.NativeVirtualKey, Is.EqualTo(0x41));
        Assert.That(input.NativeTimestamp, Is.EqualTo(123));
        Assert.That(input.NativeClockDomain, Is.EqualTo("win32-qpc"));
    }

    [Test]
    public void PhysicalValuesCompareDigitalControlsByIdentity()
    {
        var first = new PhysicalInputValue(new DigitalControl(new KeyboardControl(7, 80)), isDown: true);
        var same = new PhysicalInputValue(new DigitalControl(new KeyboardControl(7, 80)), isDown: true);
        var different = new PhysicalInputValue(new DigitalControl(new KeyboardControl(7, 81)), isDown: true);

        Assert.That(first, Is.EqualTo(same));
        Assert.That(first.GetHashCode(), Is.EqualTo(same.GetHashCode()));
        Assert.That(first, Is.Not.EqualTo(different));
    }

    [Test]
    public void PointerMotionAndScrollPreserveUnitsAndScale()
    {
        var device = new InputDeviceHandle(1, 0, 1);
        var window = new WindowHandle(1, 2, 1);
        var motion = new PhysicalInputEvent(
            PhysicalInputEventKind.PointerAbsoluteMotion,
            1,
            1,
            device,
            window,
            pointerPosition: new Vector2(192, 96),
            pointerContentScale: new Vector2(2));
        var scroll = new PhysicalInputEvent(
            PhysicalInputEventKind.VerticalScroll,
            2,
            2,
            device,
            window,
            scrollValue: 1.0f,
            scrollUnit: PointerScrollUnit.Detents);

        Assert.That(motion.PointerPosition, Is.EqualTo(new Vector2(192, 96)));
        Assert.That(motion.PointerContentScale, Is.EqualTo(new Vector2(2)));
        Assert.That(scroll.ScrollValue, Is.EqualTo(1.0f));
        Assert.That(scroll.ScrollUnit, Is.EqualTo(PointerScrollUnit.Detents));
    }

    [Test]
    public void RejectsMismatchedPayloadsAndIncompleteNativeDiagnostics()
    {
        var device = new InputDeviceHandle(1, 0, 1);
        var window = new WindowHandle(1, 2, 1);
        var control = new DigitalControl(new KeyboardControl(7, 80));
        var upValue = new PhysicalInputValue(control, isDown: false);

        _ = Assert.Throws<ArgumentException>(() => new PhysicalInputEvent(
            PhysicalInputEventKind.KeyDown,
            1,
            1,
            device,
            window,
            upValue));
        _ = Assert.Throws<ArgumentException>(() => new PhysicalInputEvent(
            PhysicalInputEventKind.KeyDown,
            1,
            1,
            device,
            window,
            nativeTimestamp: 1));
        _ = Assert.Throws<ArgumentException>(() => new PhysicalInputEvent(
            PhysicalInputEventKind.StateReconciliation,
            1,
            1,
            device,
            reconciliationState: default));
        _ = Assert.Throws<ArgumentException>(() => new PhysicalInputEvent(
            PhysicalInputEventKind.KeyDown,
            1,
            1,
            device,
            new WindowHandle(2, 2, 1),
            new PhysicalInputValue(new DigitalControl(new KeyboardControl(7, 80)), isDown: true)));
        _ = Assert.Throws<ArgumentException>(() => new PhysicalInputEvent(
            PhysicalInputEventKind.KeyDown,
            1,
            1,
            device,
            window,
            new PhysicalInputValue(new DigitalControl(new KeyboardControl(7, 80)), isDown: true),
            nativeUsagePage: 7,
            nativeUsage: 81));

        var reconciliation = new PhysicalInputEvent(
            PhysicalInputEventKind.StateReconciliation,
            1,
            1,
            device,
            reconciliationState: []);
        Assert.That(reconciliation.ReconciliationState, Is.Empty);
    }

    [Test]
    public void UnknownKeyRetainsOpaqueNativeCodesWithoutFabricatingAControl()
    {
        var input = new PhysicalInputEvent(
            PhysicalInputEventKind.KeyDown,
            0,
            1,
            new InputDeviceHandle(1, 0, 1),
            new WindowHandle(1, 2, 1),
            nativeUsagePage: 0xFF00,
            nativeUsage: 0x0001,
            nativeScanCode: 0x1E,
            flags: PhysicalInputEventDiagnostic.TimestampUncertain);

        Assert.That(input.Value, Is.Null);
        Assert.That(input.NativeUsagePage, Is.EqualTo(0xFF00));
        Assert.That(input.NativeUsage, Is.EqualTo(0x0001));
        Assert.That(input.Flags, Is.EqualTo(PhysicalInputEventDiagnostic.TimestampUncertain));
    }
}
