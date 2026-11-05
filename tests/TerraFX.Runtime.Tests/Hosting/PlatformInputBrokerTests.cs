// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;
using NUnit.Framework;
using TerraFX.Hosting;
using TerraFX.Input;
using TerraFX.Numerics;
using TerraFX.Platform;

namespace TerraFX.Runtime.Tests.Hosting;

[TestFixture]
internal sealed class PlatformInputBrokerTests
{
    [Test]
    public void IssuesAndRetiresHostDeviceGenerationsWithStableEventIdentity()
    {
        var broker = new PlatformInputBroker(17, new IncrementingTimeProvider());
        var nativeDevice = (nint)71;
        var samples = ImmutableArray.Create(
            new PlatformInputSample(
                PhysicalInputEventKind.DeviceAdded,
                null,
                nativeDevice,
                PlatformInputDeviceKind.Unknown,
                NativeTimestamp: 10,
                NativeClockDomain: "test-clock",
                MonotonicTimestamp: 2),
            new PlatformInputSample(
                PhysicalInputEventKind.DeviceRemoved,
                null,
                nativeDevice,
                PlatformInputDeviceKind.Unknown,
                NativeTimestamp: 11,
                NativeClockDomain: "test-clock",
                MonotonicTimestamp: 2),
            new PlatformInputSample(
                PhysicalInputEventKind.DeviceAdded,
                null,
                nativeDevice,
                PlatformInputDeviceKind.Unknown,
                NativeTimestamp: 12,
                NativeClockDomain: "test-clock",
                MonotonicTimestamp: 3));

        var captured = broker.Capture(new PlatformInputSampleBatch(samples, DiscardedSampleCount: 2), static _ => true);

        Assert.That(captured.Events, Has.Length.EqualTo(3));
        Assert.That(captured.DiscardedSampleCount, Is.EqualTo(2));
        Assert.That(captured.Overflowed, Is.True);

        var added = captured.Events[0];
        var removed = captured.Events[1];
        var replacement = captured.Events[2];

        Assert.That(added.Kind, Is.EqualTo(PhysicalInputEventKind.DeviceAdded));
        Assert.That(removed.Kind, Is.EqualTo(PhysicalInputEventKind.DeviceRemoved));
        Assert.That(replacement.Kind, Is.EqualTo(PhysicalInputEventKind.DeviceAdded));
        Assert.That(added.Device, Is.EqualTo(removed.Device));
        Assert.That(replacement.Device?.OwnerId, Is.EqualTo(17));
        Assert.That(replacement.Device?.Index, Is.EqualTo(added.Device?.Index));
        Assert.That(replacement.Device?.Generation, Is.EqualTo(added.Device?.Generation + 1));
        Assert.That(added.HostSequence, Is.LessThan(removed.HostSequence));
        Assert.That(removed.HostSequence, Is.LessThan(replacement.HostSequence));
        Assert.That(added.HostTimestamp, Is.EqualTo(removed.HostTimestamp));
        Assert.That(removed.HostTimestamp, Is.LessThan(replacement.HostTimestamp));
        Assert.That(added.HostTimestamp, Is.EqualTo(1));
        Assert.That(removed.HostTimestamp, Is.EqualTo(1));
        Assert.That(replacement.HostTimestamp, Is.EqualTo(2));
        Assert.That(added.NativeTimestamp, Is.EqualTo(10));
        Assert.That(removed.NativeTimestamp, Is.EqualTo(11));
        broker.Retire();
    }

    [Test]
    public void PreservesNormalizedGamepadValuesWithoutWindowOrPlayerAssignment()
    {
        var broker = new PlatformInputBroker(17, new IncrementingTimeProvider());
        var nativeDevice = (nint)71;
        var samples = ImmutableArray.Create(
            new PlatformInputSample(
                PhysicalInputEventKind.DeviceAdded,
                null,
                nativeDevice,
                PlatformInputDeviceKind.Gamepad,
                MonotonicTimestamp: 2),
            new PlatformInputSample(
                PhysicalInputEventKind.GamepadButtonDown,
                null,
                nativeDevice,
                PlatformInputDeviceKind.Gamepad,
                GamepadButton: GamepadButtonValue.South,
                IsDown: true,
                NativeTimestamp: 10,
                NativeClockDomain: "gamepad-clock",
                MonotonicTimestamp: 2),
            new PlatformInputSample(
                PhysicalInputEventKind.GamepadAxis,
                null,
                nativeDevice,
                PlatformInputDeviceKind.Gamepad,
                Axis: GamepadAxis.LeftX,
                AxisValue: -0.75f,
                MonotonicTimestamp: 2),
            new PlatformInputSample(
                PhysicalInputEventKind.GamepadStick,
                null,
                nativeDevice,
                PlatformInputDeviceKind.Gamepad,
                Stick: GamepadStick.Left,
                StickValue: new Vector2(0.6f, 0.8f),
                MonotonicTimestamp: 2),
            new PlatformInputSample(
                PhysicalInputEventKind.GamepadTrigger,
                null,
                nativeDevice,
                PlatformInputDeviceKind.Gamepad,
                Trigger: GamepadTrigger.Right,
                TriggerValue: 0.5f,
                MonotonicTimestamp: 2),
            new PlatformInputSample(
                PhysicalInputEventKind.GamepadButtonUp,
                null,
                nativeDevice,
                PlatformInputDeviceKind.Gamepad,
                GamepadButton: GamepadButtonValue.South,
                IsDown: false,
                MonotonicTimestamp: 2),
            new PlatformInputSample(
                PhysicalInputEventKind.DeviceRemoved,
                null,
                nativeDevice,
                PlatformInputDeviceKind.Gamepad,
                MonotonicTimestamp: 2));

        var captured = broker.Capture(new PlatformInputSampleBatch(samples, DiscardedSampleCount: 0), static _ => true);

        Assert.That(captured.Events, Has.Length.EqualTo(samples.Length));
        Assert.That(captured.DiscardedSampleCount, Is.Zero);

        for (var index = 0; index < captured.Events.Length; index++)
        {
            Assert.That(captured.Events[index].HostTimestamp, Is.EqualTo(1));
            Assert.That(captured.Events[index].Device, Is.EqualTo(captured.Events[0].Device));
            Assert.That(captured.Events[index].Window, Is.Null);
            Assert.That(captured.Events[index].PlayerSlot, Is.Null);
        }

        for (var index = 1; index < captured.Events.Length; index++)
        {
            Assert.That(captured.Events[index].HostSequence, Is.GreaterThan(captured.Events[index - 1].HostSequence));
        }

        Assert.That(captured.Events[1].Value?.Kind, Is.EqualTo(PhysicalInputValueKind.GamepadButton));
        Assert.That(captured.Events[1].Value?.GamepadButton, Is.EqualTo(GamepadButtonValue.South));
        Assert.That(captured.Events[1].Value?.IsDown, Is.True);
        Assert.That(captured.Events[2].Value?.Kind, Is.EqualTo(PhysicalInputValueKind.GamepadAxis));
        Assert.That(captured.Events[2].Value?.Axis, Is.EqualTo(GamepadAxis.LeftX));
        Assert.That(captured.Events[2].Value?.AxisValue, Is.EqualTo(-0.75f));
        Assert.That(captured.Events[3].Value?.Kind, Is.EqualTo(PhysicalInputValueKind.GamepadStick));
        Assert.That(captured.Events[3].Value?.Stick, Is.EqualTo(GamepadStick.Left));
        Assert.That(captured.Events[3].Value?.StickValue, Is.EqualTo(new Vector2(0.6f, 0.8f)));
        Assert.That(captured.Events[4].Value?.Kind, Is.EqualTo(PhysicalInputValueKind.GamepadTrigger));
        Assert.That(captured.Events[4].Value?.Trigger, Is.EqualTo(GamepadTrigger.Right));
        Assert.That(captured.Events[4].Value?.TriggerValue, Is.EqualTo(0.5f));
        Assert.That(captured.Events[5].Value?.IsDown, Is.False);
        Assert.That(captured.Events[6].Kind, Is.EqualTo(PhysicalInputEventKind.DeviceRemoved));
        broker.Retire();
    }

    [Test]
    public void PreservesStateReconciliationForGamepadControls()
    {
        var broker = new PlatformInputBroker(17, new IncrementingTimeProvider());
        var nativeDevice = (nint)71;
        var reconciliationState = ImmutableArray.Create(
            new PlatformInputStateValue(
                PlatformInputStateValueKind.GamepadButton,
                GamepadButton: GamepadButtonValue.South,
                IsDown: true),
            new PlatformInputStateValue(
                PlatformInputStateValueKind.GamepadAxis,
                Axis: GamepadAxis.LeftX,
                AxisValue: -0.5f),
            new PlatformInputStateValue(
                PlatformInputStateValueKind.GamepadTrigger,
                Trigger: GamepadTrigger.Right,
                TriggerValue: 0.75f));
        var sample = new PlatformInputSample(
            PhysicalInputEventKind.StateReconciliation,
            null,
            nativeDevice,
            PlatformInputDeviceKind.Gamepad,
            MonotonicTimestamp: 2,
            ReconciliationState: reconciliationState);

        var captured = broker.Capture(new PlatformInputSampleBatch([sample], DiscardedSampleCount: 0), static _ => true);

        Assert.That(captured.Events, Has.Length.EqualTo(2));
        Assert.That(captured.Events[0].Kind, Is.EqualTo(PhysicalInputEventKind.DeviceAdded));
        Assert.That(captured.Events[1].Kind, Is.EqualTo(PhysicalInputEventKind.StateReconciliation));
        Assert.That(captured.Events[1].ReconciliationState, Has.Length.EqualTo(3));
        Assert.That(captured.Events[1].ReconciliationState[0].GamepadButton, Is.EqualTo(GamepadButtonValue.South));
        Assert.That(captured.Events[1].ReconciliationState[0].IsDown, Is.True);
        Assert.That(captured.Events[1].ReconciliationState[1].AxisValue, Is.EqualTo(-0.5f));
        Assert.That(captured.Events[1].ReconciliationState[2].TriggerValue, Is.EqualTo(0.75f));
        broker.Retire();
    }

    [Test]
    public void RejectsInvalidGamepadSampleBeforeIssuingDeviceGeneration()
    {
        var broker = new PlatformInputBroker(17, new IncrementingTimeProvider());
        var nativeDevice = (nint)71;
        var invalidSample = new PlatformInputSample(
            PhysicalInputEventKind.GamepadAxis,
            null,
            nativeDevice,
            PlatformInputDeviceKind.Gamepad,
            Axis: GamepadAxis.LeftX,
            AxisValue: 1.5f,
            MonotonicTimestamp: 2);

        Assert.That(
            () => broker.Capture(
                new PlatformInputSampleBatch([invalidSample], DiscardedSampleCount: 0),
                static _ => true),
            Throws.TypeOf<ArgumentOutOfRangeException>());

        var validCapture = broker.Capture(
            new PlatformInputSampleBatch(
                [invalidSample with { AxisValue = 0.5f }],
                DiscardedSampleCount: 0),
            static _ => true);

        Assert.That(validCapture.Events, Has.Length.EqualTo(2));
        Assert.That(validCapture.Events[0].Kind, Is.EqualTo(PhysicalInputEventKind.DeviceAdded));
        Assert.That(validCapture.Events[1].Kind, Is.EqualTo(PhysicalInputEventKind.GamepadAxis));
        Assert.That(validCapture.Events[0].Device, Is.EqualTo(validCapture.Events[1].Device));
        broker.Retire();
    }

    private sealed class IncrementingTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Increment(ref _timestamp);
    }
}
