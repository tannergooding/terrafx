// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Hosting;
using TerraFX.Input;
using TerraFX.Numerics;
using TerraFX.Platform;

namespace TerraFX.Runtime.Tests.Hosting;

[TestFixture]
internal sealed class PlatformInputBrokerTests
{
    [Test]
    public void CapturesWindowPointerBoundaryEventsWithoutDeviceIdentity()
    {
        var broker = new PlatformInputBroker(17, new IncrementingTimeProvider());
        var window = new WindowHandle(17, 0, 1);
        var samples = ImmutableArray.Create(
            new PlatformInputSample(
                PhysicalInputEventKind.WindowEntered,
                window,
                NativeDeviceIdentity: 0,
                PlatformInputDeviceKind.Unknown,
                MonotonicTimestamp: 1),
            new PlatformInputSample(
                PhysicalInputEventKind.WindowLeft,
                window,
                NativeDeviceIdentity: 0,
                PlatformInputDeviceKind.Unknown,
                MonotonicTimestamp: 2));

        var captured = broker.Capture(new PlatformInputSampleBatch(samples, DiscardedSampleCount: 0), static _ => true);

        Assert.That(captured.Events, Has.Length.EqualTo(2));
        Assert.That(captured.Events[0].Kind, Is.EqualTo(PhysicalInputEventKind.WindowEntered));
        Assert.That(captured.Events[1].Kind, Is.EqualTo(PhysicalInputEventKind.WindowLeft));
        Assert.That(captured.Events.All(inputEvent => inputEvent.Window == window), Is.True);
        Assert.That(captured.Events.All(static inputEvent => inputEvent.Device is null), Is.True);
        Assert.That(captured.Events[0].HostSequence, Is.LessThan(captured.Events[1].HostSequence));
        broker.Retire();
    }

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
    public void IssuesSeparateHandlesForAggregateKeyboardAndPointerSources()
    {
        var broker = new PlatformInputBroker(17, new IncrementingTimeProvider());
        var window = new WindowHandle(17, 0, 1);
        var samples = ImmutableArray.Create(
            new PlatformInputSample(
                PhysicalInputEventKind.KeyDown,
                window,
                (nint)(-1),
                PlatformInputDeviceKind.Keyboard,
                NativeUsagePage: 0x0007,
                NativeUsage: 0x0004,
                IsDown: true,
                MonotonicTimestamp: 1),
            new PlatformInputSample(
                PhysicalInputEventKind.PointerButtonDown,
                window,
                (nint)(-2),
                PlatformInputDeviceKind.Pointer,
                MouseButton: MouseButtonValue.Primary,
                IsDown: true,
                MonotonicTimestamp: 2));

        var captured = broker.Capture(new PlatformInputSampleBatch(samples, DiscardedSampleCount: 0), static _ => true);

        Assert.That(captured.Events, Has.Length.EqualTo(4));
        Assert.That(captured.Events[0].Kind, Is.EqualTo(PhysicalInputEventKind.DeviceAdded));
        Assert.That(captured.Events[1].Kind, Is.EqualTo(PhysicalInputEventKind.KeyDown));
        Assert.That(captured.Events[2].Kind, Is.EqualTo(PhysicalInputEventKind.DeviceAdded));
        Assert.That(captured.Events[3].Kind, Is.EqualTo(PhysicalInputEventKind.PointerButtonDown));
        Assert.That(captured.Events[0].Device, Is.EqualTo(captured.Events[1].Device));
        Assert.That(captured.Events[2].Device, Is.EqualTo(captured.Events[3].Device));
        Assert.That(captured.Events[0].Device, Is.Not.EqualTo(captured.Events[2].Device));
        broker.Retire();
    }

    [Test]
    public void PreservesCommittedWaylandTextAfterItsPhysicalKeySequence()
    {
        var broker = new PlatformInputBroker(17, new IncrementingTimeProvider());
        var window = new WindowHandle(17, 0, 1);
        var samples = ImmutableArray.Create(
            new PlatformInputSample(
                PhysicalInputEventKind.KeyDown,
                window,
                NativeDeviceIdentity: -1,
                PlatformInputDeviceKind.Keyboard,
                NativeUsagePage: 0x0007,
                NativeUsage: 0x0008,
                IsDown: true,
                MonotonicTimestamp: 1,
                CaptureOrder: 1),
            new PlatformInputSample(
                PhysicalInputEventKind.KeyDown,
                window,
                NativeDeviceIdentity: -1,
                PlatformInputDeviceKind.Keyboard,
                NativeUsagePage: 0x0007,
                NativeUsage: 0x0009,
                IsDown: true,
                MonotonicTimestamp: 2,
                CaptureOrder: 3));

        var textSamples = ImmutableArray.Create(
            new PlatformTextInputSample(
                window,
                [new TextInputChange(TextInputChangeKind.TextCommitted, "é")],
                MonotonicTimestamp: 1,
                CaptureOrder: 2,
                GroupWithOtherCommits: true),
            new PlatformTextInputSample(
                window,
                [new TextInputChange(TextInputChangeKind.TextCommitted, "ß")],
                MonotonicTimestamp: 2,
                CaptureOrder: 4,
                GroupWithOtherCommits: true));

        var captured = broker.Capture(
            new PlatformInputSampleBatch(samples, DiscardedSampleCount: 0, TextSamples: textSamples),
            static _ => true);
        var keyDowns = captured.Events.Where(inputEvent => inputEvent.Kind == PhysicalInputEventKind.KeyDown).ToArray();

        Assert.That(captured.TextEvents, Has.Length.EqualTo(1));
        Assert.That(captured.TextEvents[0].Window, Is.EqualTo(window));
        Assert.That(
            captured.TextEvents[0].Changes,
            Is.EqualTo(ImmutableArray.Create(
                new TextInputChange(TextInputChangeKind.TextCommitted, "é"),
                new TextInputChange(TextInputChangeKind.TextCommitted, "ß"))));
        Assert.That(captured.TextEvents[0].HostTimestamp, Is.GreaterThanOrEqualTo(keyDowns[^1].HostTimestamp));
        Assert.That(captured.TextEvents[0].HostSequence, Is.EqualTo(keyDowns[^1].HostSequence + 1));
        broker.Retire();
    }

    [Test]
    public void PreservesCommittedWaylandTextBeforeFocusTransitionsWithinCaptureDrain()
    {
        var broker = new PlatformInputBroker(17, new IncrementingTimeProvider());
        var window = new WindowHandle(17, 0, 1);
        var samples = ImmutableArray.Create(
            new PlatformInputSample(
                PhysicalInputEventKind.KeyDown,
                window,
                NativeDeviceIdentity: -1,
                PlatformInputDeviceKind.Keyboard,
                NativeUsagePage: 0x0007,
                NativeUsage: 0x0008,
                IsDown: true,
                MonotonicTimestamp: 1,
                CaptureOrder: 1),
            new PlatformInputSample(
                PhysicalInputEventKind.FocusLost,
                window,
                NativeDeviceIdentity: 0,
                PlatformInputDeviceKind.Unknown,
                MonotonicTimestamp: 2,
                CaptureOrder: 3),
            new PlatformInputSample(
                PhysicalInputEventKind.FocusGained,
                window,
                NativeDeviceIdentity: 0,
                PlatformInputDeviceKind.Unknown,
                MonotonicTimestamp: 3,
                CaptureOrder: 4),
            new PlatformInputSample(
                PhysicalInputEventKind.KeyDown,
                window,
                NativeDeviceIdentity: -1,
                PlatformInputDeviceKind.Keyboard,
                NativeUsagePage: 0x0007,
                NativeUsage: 0x0009,
                IsDown: true,
                MonotonicTimestamp: 4,
                CaptureOrder: 5));

        var textSamples = ImmutableArray.Create(
            new PlatformTextInputSample(
                window,
                [new TextInputChange(TextInputChangeKind.TextCommitted, "é")],
                MonotonicTimestamp: 1,
                CaptureOrder: 2,
                GroupWithOtherCommits: true),
            new PlatformTextInputSample(
                window,
                [new TextInputChange(TextInputChangeKind.TextCommitted, "ß")],
                MonotonicTimestamp: 4,
                CaptureOrder: 6,
                GroupWithOtherCommits: true));

        var captured = broker.Capture(
            new PlatformInputSampleBatch(samples, DiscardedSampleCount: 0, TextSamples: textSamples),
            static _ => true);
        var focusLost = captured.Events.Single(inputEvent => inputEvent.Kind == PhysicalInputEventKind.FocusLost);
        var lastKeyDown = captured.Events.Last(inputEvent => inputEvent.Kind == PhysicalInputEventKind.KeyDown);

        Assert.That(captured.TextEvents, Has.Length.EqualTo(2));
        Assert.That(captured.TextEvents[0].Changes[0].Text, Is.EqualTo("é"));
        Assert.That(captured.TextEvents[0].HostSequence, Is.LessThan(focusLost.HostSequence));
        Assert.That(captured.TextEvents[1].Changes[0].Text, Is.EqualTo("ß"));
        Assert.That(captured.TextEvents[1].HostSequence, Is.GreaterThan(lastKeyDown.HostSequence));
        broker.Retire();
    }

    [Test]
    public void PreservesNativeTextCompositionTransactionAndSessionRevision()
    {
        var broker = new PlatformInputBroker(17, new IncrementingTimeProvider());
        var window = new WindowHandle(17, 0, 1);
        var changes = ImmutableArray.Create(
            new TextInputChange(TextInputChangeKind.CompositionStarted),
            new TextInputChange(
                TextInputChangeKind.CompositionUpdated,
                "かな",
                selectionStart: 1,
                selectionLength: 0));
        var textSample = new PlatformTextInputSample(
            window,
            changes,
            MonotonicTimestamp: 1,
            CaptureOrder: 1,
            SessionId: 42,
            BaseDocumentRevision: 7);

        var captured = broker.Capture(
            new PlatformInputSampleBatch([], DiscardedSampleCount: 0, TextSamples: [textSample]),
            static _ => true);

        Assert.That(captured.TextEvents, Has.Length.EqualTo(1));
        Assert.That(captured.TextEvents[0].Changes, Is.EqualTo(changes));
        Assert.That(captured.TextEvents[0].SessionId, Is.EqualTo(42));
        Assert.That(captured.TextEvents[0].BaseDocumentRevision, Is.EqualTo(7));
        broker.Retire();
    }

    [Test]
    public void PreservesNativeWaylandGestureSerialOnCapturedKeyEvents()
    {
        var broker = new PlatformInputBroker(17, new IncrementingTimeProvider());
        var window = new WindowHandle(17, 0, 1);
        var sample = new PlatformInputSample(
            PhysicalInputEventKind.KeyDown,
            window,
            (nint)(-1),
            PlatformInputDeviceKind.Keyboard,
            NativeUsagePage: 0x0007,
            NativeUsage: 0x0004,
            IsDown: true,
            MonotonicTimestamp: 1,
            NativeInputSerial: 81);

        var captured = broker.Capture(new PlatformInputSampleBatch([sample], DiscardedSampleCount: 0), static _ => true);
        var keyEvent = captured.Events.Single(static inputEvent => inputEvent.Kind is PhysicalInputEventKind.KeyDown);

        Assert.That(keyEvent.NativeInputSerial, Is.EqualTo(81));
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
    public void PreservesWindowKeyboardAndPointerStateReconciliation()
    {
        var broker = new PlatformInputBroker(17, new IncrementingTimeProvider());
        var window = new WindowHandle(17, 0, 1);
        var key = new KeyboardControl(7, 4);
        var reconciliationState = ImmutableArray.Create(
            new PlatformInputStateValue(
                PlatformInputStateValueKind.Digital,
                DigitalControl: new DigitalControl(key),
                IsDown: true),
            new PlatformInputStateValue(
                PlatformInputStateValueKind.Digital,
                DigitalControl: new DigitalControl(MouseButtonValue.Primary),
                IsDown: true));
        var sample = new PlatformInputSample(
            PhysicalInputEventKind.StateReconciliation,
            window,
            NativeDeviceIdentity: 0,
            PlatformInputDeviceKind.Unknown,
            MonotonicTimestamp: 2,
            ReconciliationState: reconciliationState);

        var captured = broker.Capture(new PlatformInputSampleBatch([sample], DiscardedSampleCount: 0), static _ => true);
        Assert.That(captured.Events, Has.Length.EqualTo(1));
        var inputEvent = captured.Events[0];

        Assert.Multiple(() => {
            Assert.That(inputEvent.Kind, Is.EqualTo(PhysicalInputEventKind.StateReconciliation));
            Assert.That(inputEvent.Window, Is.EqualTo(window));
            Assert.That(inputEvent.Device, Is.Null);
            Assert.That(inputEvent.ReconciliationState, Has.Length.EqualTo(2));
            Assert.That(inputEvent.ReconciliationState[0].DigitalControl?.Kind, Is.EqualTo(DigitalControlKind.KeyboardHid));
            Assert.That(inputEvent.ReconciliationState[0].DigitalControl?.Keyboard?.UsagePage, Is.EqualTo(key.UsagePage));
            Assert.That(inputEvent.ReconciliationState[0].DigitalControl?.Keyboard?.Usage, Is.EqualTo(key.Usage));
            Assert.That(inputEvent.ReconciliationState[0].IsDown, Is.True);
            Assert.That(inputEvent.ReconciliationState[1].DigitalControl?.Kind, Is.EqualTo(DigitalControlKind.MouseButton));
            Assert.That(inputEvent.ReconciliationState[1].DigitalControl?.MouseButton, Is.EqualTo(MouseButtonValue.Primary));
            Assert.That(inputEvent.ReconciliationState[1].IsDown, Is.True);
        });

        broker.Retire();
    }

    [Test]
    public void RecordsUnmappedGamepadButtonsAgainstTheIssuedDeviceGeneration()
    {
        var broker = new PlatformInputBroker(17, new IncrementingTimeProvider());
        var sample = new PlatformInputSample(
            PhysicalInputEventKind.StateReconciliation,
            null,
            (nint)71,
            PlatformInputDeviceKind.Gamepad,
            MonotonicTimestamp: 2,
            ReconciliationState: [],
            UnmappedGamepadButtonMask: 0x8000_0000);

        var captured = broker.Capture(new PlatformInputSampleBatch([sample], DiscardedSampleCount: 0), static _ => true);

        Assert.That(captured.Events, Has.Length.EqualTo(2));
        Assert.That(captured.Events[0].Kind, Is.EqualTo(PhysicalInputEventKind.DeviceAdded));
        Assert.That(captured.Diagnostics, Has.Length.EqualTo(1));
        Assert.That(captured.Diagnostics[0].Code, Is.EqualTo("platform-gamepad-unmapped-buttons"));
        Assert.That(captured.Diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(captured.Diagnostics[0].DeviceEpoch, Is.EqualTo(captured.Events[0].Device!.Value.Generation));
        Assert.That(captured.Diagnostics[0].Message, Does.Contain("0x80000000"));
        Assert.That(captured.Diagnostics[0].Context[0].Key, Is.EqualTo("deviceIndex"));
        Assert.That(captured.Diagnostics[0].Context[0].Value, Is.EqualTo("0"));
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

    [Test]
    public void ReportsNativeTextOverflowForItsSession()
    {
        var broker = new PlatformInputBroker(17, new IncrementingTimeProvider());
        var window = new WindowHandle(17, 0, 1);
        var batch = new PlatformInputSampleBatch(
            [],
            DiscardedSampleCount: 0,
            TextOverflows: [new PlatformTextInputOverflow(window, SessionId: 42, DiscardedSampleCount: 2)],
            DiscardedTextSampleCount: 2);

        var captured = broker.Capture(batch, static _ => true);

        Assert.That(captured.TextOverflows, Has.Length.EqualTo(1));
        Assert.That(captured.TextOverflows[0].Window, Is.EqualTo(window));
        Assert.That(captured.TextOverflows[0].SessionId, Is.EqualTo(42));
        Assert.That(captured.TextOverflows[0].DiscardedSampleCount, Is.EqualTo(2));
        Assert.That(captured.Diagnostics, Has.Length.EqualTo(1));
        broker.Retire();
    }

    private sealed class IncrementingTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Increment(ref _timestamp);
    }
}
