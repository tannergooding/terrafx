// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Input;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class LinuxEvdevGamepadEventDecoderTests
{
    [Test]
    public void DecodesButtonAndAxisChangesWithObservedCaptureTime()
    {
        var state = new LinuxEvdevGamepadState([
            (0x0000, new LinuxEvdevAxisInfo(-100, 100, Fuzz: 0, Flat: 0), 0),
        ]);
        var buttonEvent = new LinuxEvdevInputEvent(Seconds: 12, Microseconds: 345_678, Type: 0x0001, Code: 0x0130, Value: 1);
        var buttonSamples = LinuxEvdevGamepadEventDecoder.Decode(
            state,
            nativeDeviceIdentity: 42,
            buttonEvent,
            monotonicClockRequested: true,
            monotonicTimestamp: 99);

        Assert.That(buttonSamples, Has.Length.EqualTo(1));
        Assert.That(buttonSamples[0].Kind, Is.EqualTo(PhysicalInputEventKind.GamepadButtonDown));
        Assert.That(buttonSamples[0].GamepadButton, Is.EqualTo(GamepadButtonValue.South));
        Assert.That(buttonSamples[0].NativeDeviceIdentity, Is.EqualTo((nint)42));
        Assert.That(buttonSamples[0].NativeTimestamp, Is.EqualTo(12_345_678_000));
        Assert.That(buttonSamples[0].NativeClockDomain, Is.EqualTo("CLOCK_MONOTONIC"));
        Assert.That(buttonSamples[0].MonotonicTimestamp, Is.EqualTo(99));
        Assert.That(buttonSamples[0].Diagnostics, Is.EqualTo(PhysicalInputEventDiagnostic.None));

        var axisEvent = new LinuxEvdevInputEvent(Seconds: 12, Microseconds: 345_679, Type: 0x0003, Code: 0x0000, Value: 100);
        var axisSamples = LinuxEvdevGamepadEventDecoder.Decode(
            state,
            nativeDeviceIdentity: 42,
            axisEvent,
            monotonicClockRequested: true,
            monotonicTimestamp: 100);

        Assert.That(axisSamples, Has.Length.EqualTo(2));
        Assert.That(axisSamples[0].Kind, Is.EqualTo(PhysicalInputEventKind.GamepadAxis));
        Assert.That(axisSamples[0].Axis, Is.EqualTo(GamepadAxis.LeftX));
        Assert.That(axisSamples[1].Kind, Is.EqualTo(PhysicalInputEventKind.GamepadStick));
        Assert.That(axisSamples[1].Stick, Is.EqualTo(GamepadStick.Left));
        Assert.That(axisSamples[0].NativeTimestamp, Is.EqualTo(axisSamples[1].NativeTimestamp));
        Assert.That(axisSamples[0].MonotonicTimestamp, Is.EqualTo(axisSamples[1].MonotonicTimestamp));
    }

    [Test]
    public void MarksDefaultClockAndInvalidNativeTimesUncertain()
    {
        var state = new LinuxEvdevGamepadState([]);
        var inputEvent = new LinuxEvdevInputEvent(Seconds: 1, Microseconds: 2, Type: 0x0001, Code: 0x0130, Value: 1);
        var defaultClockSample = LinuxEvdevGamepadEventDecoder.Decode(
            state,
            nativeDeviceIdentity: 7,
            inputEvent,
            monotonicClockRequested: false,
            monotonicTimestamp: 5);

        Assert.That(defaultClockSample, Has.Length.EqualTo(1));
        Assert.That(defaultClockSample[0].NativeClockDomain, Is.EqualTo("evdev-default"));
        Assert.That(defaultClockSample[0].Diagnostics, Is.EqualTo(PhysicalInputEventDiagnostic.TimestampUncertain));

        var invalidTimeEvent = new LinuxEvdevInputEvent(
            Seconds: 1,
            Microseconds: 1_000_000,
            Type: 0x0001,
            Code: 0x0131,
            Value: 1);
        var invalidTimeSample = LinuxEvdevGamepadEventDecoder.Decode(
            state,
            nativeDeviceIdentity: 7,
            invalidTimeEvent,
            monotonicClockRequested: true,
            monotonicTimestamp: 6);

        Assert.That(invalidTimeSample, Has.Length.EqualTo(1));
        Assert.That(invalidTimeSample[0].NativeTimestamp, Is.Null);
        Assert.That(invalidTimeSample[0].NativeClockDomain, Is.Null);
        Assert.That(invalidTimeSample[0].Diagnostics, Is.EqualTo(PhysicalInputEventDiagnostic.TimestampUncertain));
    }

    [Test]
    public void RejectsInvalidObservedTimeAndIgnoresNonInputEvents()
    {
        var state = new LinuxEvdevGamepadState([]);
        var inputEvent = new LinuxEvdevInputEvent(Seconds: 0, Microseconds: 0, Type: 0x0001, Code: 0x0130, Value: 1);

        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => LinuxEvdevGamepadEventDecoder.Decode(
                state,
                nativeDeviceIdentity: 1,
                inputEvent,
                monotonicClockRequested: true,
                monotonicTimestamp: -1));

        var synchronizationEvent = new LinuxEvdevInputEvent(Seconds: 0, Microseconds: 0, Type: 0x0000, Code: 0x0000, Value: 0);
        Assert.That(
            LinuxEvdevGamepadEventDecoder.Decode(
                state,
                nativeDeviceIdentity: 1,
                synchronizationEvent,
                monotonicClockRequested: true,
                monotonicTimestamp: 1),
            Is.Empty);
    }
}
