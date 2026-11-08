// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Linq;
using NUnit.Framework;
using TerraFX;
using TerraFX.Input;
using TerraFX.Numerics;

namespace TerraFX.Input.Tests;

[TestFixture]
internal sealed class InputRecordingTests
{
    private static InputRouter CreateHostRouter(
        out HostEventSequenceLease sequenceLease,
        out WindowHandle window,
        out KeyboardControl key)
    {
        sequenceLease = new HostEventSequenceLease();
        window = new WindowHandle(1, 0, 1);
        key = new KeyboardControl(7, 4);
        return new InputRouter(
            InputRecipientKind.HostLiveUI,
            7,
            Environment.CurrentManagedThreadId,
            sequenceLease,
            window);
    }

    private static PhysicalInputEvent CreateKeyEvent(
        PhysicalInputEventKind kind,
        WindowHandle window,
        InputDeviceHandle device,
        KeyboardControl key,
        long hostTimestamp,
        long hostSequence,
        bool isDown)
        => new(
            kind,
            hostTimestamp,
            hostSequence,
            device,
            window,
            new PhysicalInputValue(new DigitalControl(key), isDown));

    private static InputActionMapV1 CreateMap(KeyboardControl key)
    {
        var action = new ActionDefinition(Guid.NewGuid(), "Jump", InputActionKind.Digital);
        var context = new ContextDefinition(Guid.NewGuid(), 0);
        var binding = new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.Button) {
            Control = new DigitalControl(key),
        };
        return new InputActionMapV1(1, Guid.NewGuid(), 1, 1, [action], [context], [binding]);
    }

    [Test]
    public void RecordsAcceptedRecipientCopiesAndBoundaryOrdering()
    {
        var router = CreateHostRouter(out var sequenceLease, out var window, out var key);
        var mapResponse = router.RegisterMap(CreateMap(key));
        var recorder = router.StartRecording();
        var device = new InputDeviceHandle(window.OwnerId, 1, 1);
        var downSequence = sequenceLease.Allocate();
        var down = CreateKeyEvent(
            PhysicalInputEventKind.KeyDown,
            window,
            device,
            key,
            hostTimestamp: 10,
            downSequence,
            isDown: true);

        Assert.That(router.EnqueueHostPhysicalEvent(down, out _).Status, Is.EqualTo(InputRoutingStatus.Applied));
        _ = router.ServiceInputControlBoundary();

        var upSequence = sequenceLease.Allocate();
        var up = CreateKeyEvent(
            PhysicalInputEventKind.KeyUp,
            window,
            device,
            key,
            hostTimestamp: 11,
            upSequence,
            isDown: false);
        Assert.That(router.EnqueueHostPhysicalEvent(up, out _).Status, Is.EqualTo(InputRoutingStatus.Applied));
        _ = router.ServiceInputControlBoundary();

        var recording = recorder.Complete();
        Assert.That(mapResponse.Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(recording.Version, Is.EqualTo(1));
        Assert.That(recording.RecipientKind, Is.EqualTo(InputRecipientKind.HostLiveUI));
        Assert.That(recording.RecipientId, Is.EqualTo(router.RecipientId));
        Assert.That(recording.Window, Is.EqualTo(window));
        Assert.That(recording.IsComplete, Is.True);
        Assert.That(recording.Baseline.InitialMaps, Has.Length.EqualTo(1));
        Assert.That(recording.Entries.Count(static entry => entry.Kind == InputRecordingEntryKind.PhysicalInput), Is.EqualTo(2));
        Assert.That(recording.Entries.Where(static entry => entry.Kind == InputRecordingEntryKind.PhysicalInput)
            .Select(static entry => entry.PhysicalEvent?.HostSequence), Is.EqualTo(new long?[] { downSequence, upSequence }));
        Assert.That(recording.Entries.Count(static entry => entry.Kind == InputRecordingEntryKind.HostControlBoundary), Is.EqualTo(2));
        Assert.That(recording.Entries[^1].BoundaryOrdinal, Is.EqualTo(2));

        var replay = InputReplay.Play(recording);
        Assert.That(replay.IsComplete, Is.True);
        Assert.That(replay.Snapshots, Has.Length.EqualTo(2));
        Assert.That(replay.Snapshots[0].Actions[0].IsDownAtEnd, Is.True);
        Assert.That(replay.Snapshots[0].Actions[0].Transitions[0].HostSequence, Is.EqualTo(downSequence));
        Assert.That(replay.Snapshots[1].Actions[0].IsDownAtEnd, Is.False);
        Assert.That(replay.Snapshots[1].Actions[0].Transitions[0].HostSequence, Is.EqualTo(upSequence));

        router.Dispose();
    }

    [Test]
    public void RecordsIndependentRecipientsWithoutSiblingCopies()
    {
        var sequenceLease = new HostEventSequenceLease();
        var firstWindow = new WindowHandle(1, 0, 1);
        var secondWindow = new WindowHandle(1, 1, 1);
        var firstKey = new KeyboardControl(7, 4);
        var secondKey = new KeyboardControl(7, 4);
        var firstRouter = new InputRouter(
            InputRecipientKind.HostLiveUI,
            7,
            Environment.CurrentManagedThreadId,
            sequenceLease,
            firstWindow);
        var secondRouter = new InputRouter(
            InputRecipientKind.HostLiveUI,
            8,
            Environment.CurrentManagedThreadId,
            sequenceLease,
            secondWindow);
        _ = firstRouter.RegisterMap(CreateMap(firstKey));
        _ = secondRouter.RegisterMap(CreateMap(secondKey));
        var firstRecorder = firstRouter.StartRecording();
        var secondRecorder = secondRouter.StartRecording();
        var device = new InputDeviceHandle(firstWindow.OwnerId, 1, 1);
        var sequence = sequenceLease.Allocate();
        var inputEvent = CreateKeyEvent(
            PhysicalInputEventKind.KeyDown,
            firstWindow,
            device,
            firstKey,
            hostTimestamp: 10,
            sequence,
            isDown: true);

        Assert.That(firstRouter.EnqueueHostPhysicalEvent(inputEvent, out _).Status, Is.EqualTo(InputRoutingStatus.Applied));
        _ = firstRouter.ServiceInputControlBoundary();
        _ = secondRouter.ServiceInputControlBoundary();

        var firstRecording = firstRecorder.Complete();
        var secondRecording = secondRecorder.Complete();
        var firstReplay = InputReplay.Play(firstRecording);
        var secondReplay = InputReplay.Play(secondRecording);
        Assert.That(firstRecording.Entries.Count(static entry => entry.Kind == InputRecordingEntryKind.PhysicalInput), Is.EqualTo(1));
        Assert.That(secondRecording.Entries.Count(static entry => entry.Kind == InputRecordingEntryKind.PhysicalInput), Is.Zero);
        Assert.That(firstReplay.Snapshots.Single().Actions[0].IsDownAtEnd, Is.True);
        Assert.That(secondReplay.Snapshots.Single().Actions[0].IsDownAtEnd, Is.False);

        firstRouter.Dispose();
        secondRouter.Dispose();
    }

    [Test]
    public void RecordsInputOverflowAsRecipientLocalDiscontinuity()
    {
        var router = CreateHostRouter(out var sequenceLease, out var window, out var key);
        _ = router.RegisterMap(CreateMap(key));
        var recorder = router.StartRecording();
        var device = new InputDeviceHandle(window.OwnerId, 1, 1);
        long lastSequence = 0;
        var lastStatus = InputRoutingStatus.Applied;

        for (var index = 0; index <= 4096; index++)
        {
            lastSequence = sequenceLease.Allocate();
            var inputEvent = CreateKeyEvent(
                PhysicalInputEventKind.KeyDown,
                window,
                device,
                key,
                index,
                lastSequence,
                isDown: true);
            lastStatus = router.EnqueueHostPhysicalEvent(inputEvent, out _).Status;
        }

        Assert.That(lastStatus, Is.EqualTo(InputRoutingStatus.CapacityExceeded));
        router.Dispose();
        var recording = recorder.Complete();
        Assert.That(recording.IsComplete, Is.False);
        Assert.That(recording.FirstMissingHostSequence, Is.EqualTo(lastSequence));
        Assert.That(recording.LastMissingHostSequence, Is.EqualTo(lastSequence));
        Assert.That(recording.Entries.Length, Is.EqualTo(4096));

        var siblingWindow = new WindowHandle(1, 1, 1);
        var siblingRouter = new InputRouter(
            InputRecipientKind.HostLiveUI,
            8,
            Environment.CurrentManagedThreadId,
            sequenceLease,
            siblingWindow);
        var siblingKey = new KeyboardControl(7, 4);
        _ = siblingRouter.RegisterMap(CreateMap(siblingKey));
        var siblingRecorder = siblingRouter.StartRecording();
        var siblingEvent = CreateKeyEvent(
            PhysicalInputEventKind.KeyDown,
            siblingWindow,
            new InputDeviceHandle(siblingWindow.OwnerId, 2, 1),
            siblingKey,
            hostTimestamp: 5000,
            hostSequence: sequenceLease.Allocate(),
            isDown: true);
        Assert.That(siblingRouter.EnqueueHostPhysicalEvent(siblingEvent, out _).Status, Is.EqualTo(InputRoutingStatus.Applied));
        _ = siblingRouter.ServiceInputControlBoundary();
        Assert.That(siblingRecorder.Complete().IsComplete, Is.True);

        siblingRouter.Dispose();
    }

    [Test]
    public void RecordsSimulationEventsThatHaveNoEligibleTickWithoutInventingAStage()
    {
        var sequenceLease = new HostEventSequenceLease();
        var key = new KeyboardControl(7, 4);
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            7,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        _ = router.RegisterMap(CreateMap(key));
        var recorder = router.StartRecording();
        var segmentSequence = sequenceLease.Allocate();
        var pausedSegment = new SimulationClockSegment(
            revision: 1,
            hostTimestampStartTicks: 0,
            hostSequenceStart: segmentSequence,
            simulationElapsedStartTicks: 0,
            fractionalSimulationTicks: 0.0,
            tickRate: 60,
            nextOpenTick: 0,
            timeScale: 0.0f,
            isPaused: false);
        router.SetSimulationClockSegment(in pausedSegment);
        var device = new InputDeviceHandle(1, 1, 1);
        var inputSequence = sequenceLease.Allocate();
        var inputEvent = CreateKeyEvent(
            PhysicalInputEventKind.KeyDown,
            new WindowHandle(1, 2, 1),
            device,
            key,
            hostTimestamp: 10,
            inputSequence,
            isDown: true);

        Assert.That(
            router.EnqueuePhysicalEvent(inputEvent, playerSlot: null, out _).Rejection?.Code,
            Is.EqualTo("NoEligibleTick"));

        var recording = recorder.Complete();
        Assert.That(recording.IsComplete, Is.True);
        Assert.That(recording.Entries.Single(static entry => entry.Kind == InputRecordingEntryKind.NoEligibleTickDiscard)
            .HostSequence, Is.EqualTo(inputSequence));
        var replay = InputReplay.Play(recording);
        Assert.That(replay.IsComplete, Is.True);
        Assert.That(replay.Snapshots, Is.Empty);

        router.Dispose();
    }

    [Test]
    public void RecordsTextOmissionWhenTextCaptureWasNotEnabled()
    {
        var router = CreateHostRouter(out var sequenceLease, out var window, out _);
        var recorder = router.StartRecording();
        var session = router.BeginTextSession(new TextInputSessionOptions(
            window,
            TextInputPurpose.FreeForm,
            documentRevision: 1,
            surroundingText: string.Empty,
            selectionStart: 0,
            selectionLength: 0,
            caretPosition: Vector2.Zero,
            caretSize: Vector2.Zero));
        var publishStatus = session.PublishNativeBatch(
            hostTimestamp: 1,
            hostSequence: sequenceLease.Allocate(),
            baseDocumentRevision: 1,
            [new TextInputChange(TextInputChangeKind.TextCommitted, "not recorded")]);

        Assert.That(publishStatus, Is.EqualTo(TextInputBatchPublishStatus.Published));
        var recording = recorder.Complete();
        Assert.That(recording.HasTextOmission, Is.True);
        Assert.That(recording.IsComplete, Is.False);
        Assert.That(recording.Entries.Any(static entry => entry.Kind == InputRecordingEntryKind.TextOmitted), Is.True);
        Assert.That(InputReplay.Play(recording).TextBatches, Is.Empty);

        session.Dispose();
        router.Dispose();
    }

    [Test]
    public void RecordsUnreconciledRecipientEndingsAsIncomplete()
    {
        var router = CreateHostRouter(out var sequenceLease, out var window, out var key);
        _ = router.RegisterMap(CreateMap(key));
        var recorder = router.StartRecording();
        var device = new InputDeviceHandle(window.OwnerId, 1, 1);
        var keyDown = CreateKeyEvent(
            PhysicalInputEventKind.KeyDown,
            window,
            device,
            key,
            hostTimestamp: 10,
            hostSequence: sequenceLease.Allocate(),
            isDown: true);
        Assert.That(router.EnqueueHostPhysicalEvent(keyDown, out _).Status, Is.EqualTo(InputRoutingStatus.Applied));
        _ = router.ServiceInputControlBoundary();

        var focusLost = new PhysicalInputEvent(
            PhysicalInputEventKind.FocusLost,
            hostTimestamp: 11,
            hostSequence: sequenceLease.Allocate(),
            window: window);
        Assert.That(router.EnqueueHostPhysicalEvent(focusLost, out _).Status, Is.EqualTo(InputRoutingStatus.Applied));
        _ = router.ServiceInputControlBoundary();

        var recording = recorder.Complete();
        Assert.That(recording.IsComplete, Is.False);
        Assert.That(recording.FirstMissingHostSequence, Is.EqualTo(focusLost.HostSequence));
        Assert.That(InputReplay.Play(recording).IsComplete, Is.False);

        router.Dispose();
    }

    [Test]
    public void RedactsPasswordTextAndMarksDependentReplayIncomplete()
    {
        var router = CreateHostRouter(out var sequenceLease, out var window, out _);
        var recorder = router.StartRecording(recordTextBatches: true);
        var session = router.BeginTextSession(new TextInputSessionOptions(
            window,
            TextInputPurpose.Password,
            documentRevision: 1,
            surroundingText: string.Empty,
            selectionStart: 0,
            selectionLength: 0,
            caretPosition: Vector2.Zero,
            caretSize: Vector2.Zero));
        var secret = "sensitive password text";
        var status = session.PublishNativeBatch(
            hostTimestamp: 1,
            hostSequence: sequenceLease.Allocate(),
            baseDocumentRevision: 1,
            [new TextInputChange(TextInputChangeKind.TextCommitted, secret)]);

        Assert.That(status, Is.EqualTo(TextInputBatchPublishStatus.Published));
        var recording = recorder.Complete();
        Assert.That(recording.HasSensitiveTextRedaction, Is.True);
        Assert.That(recording.IsComplete, Is.False);
        Assert.That(recording.Entries.Any(static entry => entry.Kind == InputRecordingEntryKind.TextBatch), Is.False);
        Assert.That(recording.Entries.Any(static entry => entry.Kind == InputRecordingEntryKind.SensitiveTextRedacted), Is.True);
        Assert.That(recording.Entries.Any(entry => entry.TextBatch?.Changes.Any(change => change.Text == secret) == true), Is.False);

        var replay = InputReplay.Play(recording);
        Assert.That(replay.IsComplete, Is.False);
        Assert.That(replay.TextBatches, Is.Empty);

        session.Dispose();
        router.Dispose();
    }

    [Test]
    public void RejectsUnsupportedAndMalformedRecordingVersionsBeforeReplay()
    {
        var baseline = new InputRecordingBaseline(
            7,
            new WindowHandle(1, 0, 1),
            [],
            0,
            0,
            -1,
            default,
            0,
            0,
            0,
            0,
            0);
        var unsupportedVersion = new InputRecording(
            InputRecipientKind.HostLiveUI,
            baseline,
            [],
            isComplete: true,
            hasTextOmission: false,
            hasSensitiveTextRedaction: false,
            firstMissingHostSequence: null,
            lastMissingHostSequence: null,
            version: InputRecording.CurrentVersion + 1);
        Assert.That(
            Assert.Throws<System.IO.InvalidDataException>(() => _ = InputReplay.Play(unsupportedVersion))?.Message,
            Does.Contain("not supported"));

        var malformedEntry = new InputRecordingEntry(
            (InputRecordingEntryKind)int.MaxValue,
            hostTimestamp: 0,
            hostSequence: 1,
            assignedTick: null,
            boundaryOrdinal: 0);
        var malformedRecording = new InputRecording(
            InputRecipientKind.HostLiveUI,
            baseline,
            [malformedEntry],
            isComplete: false,
            hasTextOmission: false,
            hasSensitiveTextRedaction: false,
            firstMissingHostSequence: 1,
            lastMissingHostSequence: 1);
        Assert.That(
            Assert.Throws<System.IO.InvalidDataException>(() => _ = InputReplay.Play(malformedRecording))?.Message,
            Does.Contain("invalid kind"));
    }

    [Test]
    public void ReplaysContextControlsAndMapRevisionsAtTheirRecipientBoundaries()
    {
        var router = CreateHostRouter(out _, out _, out var key);
        var initialMap = CreateMap(key);
        _ = router.RegisterMap(initialMap);
        var recorder = router.StartRecording();

        var contextMaskReceipt = router.SetActiveContextMask(initialMap.MapId, initialMap.Revision, newMask: 0);
        Assert.That(contextMaskReceipt.Status, Is.EqualTo(InputRoutingStatus.Applied));
        _ = router.ServiceInputControlBoundary();

        var replacement = new InputActionMapV1(
            initialMap.SchemaVersion,
            initialMap.MapId,
            revision: 2,
            initialMap.InitialActiveContextMask,
            initialMap.Actions,
            initialMap.Contexts,
            initialMap.Bindings);
        var replacementResponse = router.ReplaceMap(replacement, expectedRevision: 1);
        Assert.That(replacementResponse.Status, Is.EqualTo(InputMapStatus.Replaced));
        _ = router.ServiceInputControlBoundary();

        var recording = recorder.Complete();
        var replay = InputReplay.Play(recording);
        Assert.That(replay.IsComplete, Is.True);
        Assert.That(recording.Entries.Any(static entry => entry.Kind == InputRecordingEntryKind.Control
            && entry.ControlKind == InputRecordingControlKind.SetActiveContextMask), Is.True);
        Assert.That(recording.Entries.Any(static entry => entry.Kind == InputRecordingEntryKind.MapReplaced), Is.True);
        Assert.That(replay.Snapshots, Has.Length.EqualTo(2));
        Assert.That(replay.Snapshots.All(static snapshot => snapshot.Actions.IsEmpty), Is.False);
        Assert.That(replay.Snapshots.All(static snapshot => !snapshot.Actions[0].IsDownAtEnd), Is.True);

        router.Dispose();
    }

    [Test]
    public void ReplaysEventTimeUiDispositionAndNeutralRearming()
    {
        var sequenceLease = new HostEventSequenceLease();
        var key = new KeyboardControl(7, 4);
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            7,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        _ = router.RegisterMap(CreateMap(key));
        var recorder = router.StartRecording();
        var segmentSequence = sequenceLease.Allocate();
        var segment = new SimulationClockSegment(
            revision: 1,
            hostTimestampStartTicks: 0,
            hostSequenceStart: segmentSequence,
            simulationElapsedStartTicks: 0,
            fractionalSimulationTicks: 0.0,
            tickRate: 60,
            nextOpenTick: 0,
            timeScale: 1.0f,
            isPaused: false);
        router.SetSimulationClockSegment(in segment);
        var window = new WindowHandle(1, 2, 1);
        var device = new InputDeviceHandle(window.OwnerId, 1, 1);

        router.BeginHostControlBoundary(0, 166_666);
        var initialDown = CreateKeyEvent(
            PhysicalInputEventKind.KeyDown,
            window,
            device,
            key,
            hostTimestamp: 166_666,
            hostSequence: sequenceLease.Allocate(),
            isDown: true);
        Assert.That(router.EnqueuePhysicalEvent(initialDown, null, InputClass.None, out _).Status, Is.EqualTo(InputRoutingStatus.Applied));
        _ = router.ServiceInputControlBoundary();
        _ = router.PublishSimulationActionSnapshot(0);

        router.BeginHostControlBoundary(1, 333_332);
        var capturedRepeat = CreateKeyEvent(
            PhysicalInputEventKind.KeyRepeat,
            window,
            device,
            key,
            hostTimestamp: 333_332,
            hostSequence: sequenceLease.Allocate(),
            isDown: true);
        Assert.That(router.EnqueuePhysicalEvent(capturedRepeat, null, InputClass.Keyboard, out _).Status, Is.EqualTo(InputRoutingStatus.Applied));
        _ = router.ServiceInputControlBoundary();
        _ = router.PublishSimulationActionSnapshot(1);

        router.BeginHostControlBoundary(2, 500_000);
        var neutralUp = CreateKeyEvent(
            PhysicalInputEventKind.KeyUp,
            window,
            device,
            key,
            hostTimestamp: 500_000,
            hostSequence: sequenceLease.Allocate(),
            isDown: false);
        Assert.That(router.EnqueuePhysicalEvent(neutralUp, null, InputClass.None, out _).Status, Is.EqualTo(InputRoutingStatus.Applied));
        _ = router.ServiceInputControlBoundary();
        _ = router.PublishSimulationActionSnapshot(2);

        router.BeginHostControlBoundary(3, 666_664);
        var rearmedDown = CreateKeyEvent(
            PhysicalInputEventKind.KeyDown,
            window,
            device,
            key,
            hostTimestamp: 666_664,
            hostSequence: sequenceLease.Allocate(),
            isDown: true);
        Assert.That(router.EnqueuePhysicalEvent(rearmedDown, null, InputClass.None, out _).Status, Is.EqualTo(InputRoutingStatus.Applied));
        _ = router.ServiceInputControlBoundary();
        _ = router.PublishSimulationActionSnapshot(3);

        var recording = recorder.Complete();
        var replay = InputReplay.Play(recording);
        Assert.That(replay.IsComplete, Is.True);
        Assert.That(replay.Snapshots, Has.Length.EqualTo(4));
        Assert.That(replay.Snapshots[0].Actions[0].IsDownAtEnd, Is.True);
        Assert.That(replay.Snapshots[1].Actions[0].IsDownAtEnd, Is.False);
        Assert.That(replay.Snapshots[1].Actions[0].Transitions[0].CancellationReason, Is.EqualTo(InputCancellationReason.InputConsumed));
        Assert.That(replay.Snapshots[2].Actions[0].Transitions, Is.Empty);
        Assert.That(replay.Snapshots[3].Actions[0].IsDownAtEnd, Is.True);
        Assert.That(recording.Entries.Single(static entry =>
            entry.PhysicalEvent?.Kind == PhysicalInputEventKind.KeyRepeat).UiCapturedClasses, Is.EqualTo(InputClass.Keyboard));

        router.Dispose();
    }

    [Test]
    public void ReplaysGamepadCopiesWithTheRecordedRecipientPlayerSlot()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            7,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        var action = new ActionDefinition(Guid.NewGuid(), "Confirm", InputActionKind.Digital);
        var context = new ContextDefinition(Guid.NewGuid(), 0);
        var binding = new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.Button) {
            Control = new DigitalControl(3, GamepadButtonValue.South),
        };
        _ = router.RegisterMap(new InputActionMapV1(1, Guid.NewGuid(), 1, 1, [action], [context], [binding]));
        var recorder = router.StartRecording();
        var device = new InputDeviceHandle(1, 1, 1);
        var assignmentSequence = sequenceLease.Allocate();
        router.RecordDeviceAssignment(device, 3, isAssigned: true, hostTimestamp: 0, hostSequence: assignmentSequence);
        var segmentSequence = sequenceLease.Allocate();
        var segment = new SimulationClockSegment(
            revision: 1,
            hostTimestampStartTicks: 0,
            hostSequenceStart: segmentSequence,
            simulationElapsedStartTicks: 0,
            fractionalSimulationTicks: 0.0,
            tickRate: 60,
            nextOpenTick: 0,
            timeScale: 1.0f,
            isPaused: false);
        router.SetSimulationClockSegment(in segment);
        var downSequence = sequenceLease.Allocate();
        var down = new PhysicalInputEvent(
            PhysicalInputEventKind.GamepadButtonDown,
            hostTimestamp: 166_666,
            downSequence,
            device,
            value: new PhysicalInputValue(GamepadButtonValue.South, isDown: true));

        var receipt = router.EnqueuePhysicalEvent(down, playerSlot: 3, out _);
        Assert.That(receipt.AssignedTick, Is.EqualTo(0));
        router.BeginHostControlBoundary(0, 166_666);
        _ = router.ServiceInputControlBoundary();
        Assert.That(router.PublishSimulationActionSnapshot(0).Actions[0].IsDownAtEnd, Is.True);

        var recording = recorder.Complete();
        var replay = InputReplay.Play(recording);
        Assert.That(replay.IsComplete, Is.True);
        Assert.That(replay.Snapshots, Has.Length.EqualTo(1));
        Assert.That(replay.Snapshots[0].Actions[0].IsDownAtEnd, Is.True);
        Assert.That(replay.Snapshots[0].Actions[0].Transitions[0].HostSequence, Is.EqualTo(downSequence));
        Assert.That(replay.Snapshots[0].Actions[0].Transitions[0].AssignedTick, Is.EqualTo(0));
        Assert.That(recording.Entries.Single(static entry => entry.Kind == InputRecordingEntryKind.PhysicalInput).PlayerSlot, Is.EqualTo(3));
        Assert.That(recording.Entries.Single(static entry => entry.Kind == InputRecordingEntryKind.DeviceAssignment).Device, Is.EqualTo(device));

        router.Dispose();
    }

    [Test]
    public void ReplaysNonsensitiveTextOnlyWhenRecordingWasOptedIn()
    {
        var router = CreateHostRouter(out var sequenceLease, out var window, out _);
        var recorder = router.StartRecording(recordTextBatches: true);
        var session = router.BeginTextSession(new TextInputSessionOptions(
            window,
            TextInputPurpose.FreeForm,
            documentRevision: 1,
            surroundingText: string.Empty,
            selectionStart: 0,
            selectionLength: 0,
            caretPosition: Vector2.Zero,
            caretSize: Vector2.Zero));
        const string committedText = "safe text";
        var publishStatus = session.PublishNativeBatch(
            hostTimestamp: 1,
            hostSequence: sequenceLease.Allocate(),
            baseDocumentRevision: 1,
            [new TextInputChange(TextInputChangeKind.TextCommitted, committedText)]);
        Assert.That(publishStatus, Is.EqualTo(TextInputBatchPublishStatus.Published));

        var recording = recorder.Complete();
        var replay = InputReplay.Play(recording);
        Assert.That(recording.IsComplete, Is.True);
        Assert.That(replay.IsComplete, Is.True);
        Assert.That(replay.TextBatches, Has.Length.EqualTo(1));
        Assert.That(replay.TextBatches[0].Changes[0].Text, Is.EqualTo(committedText));

        session.Dispose();
        router.Dispose();
    }

    [Test]
    public void ReplaysOneCaptureWithIndependentTimelineTicks()
    {
        var sequenceLease = new HostEventSequenceLease();
        var key = new KeyboardControl(7, 4);
        var map = CreateMap(key);
        var firstRouter = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            7,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        var secondRouter = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            8,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        _ = firstRouter.RegisterMap(map);
        _ = secondRouter.RegisterMap(map);
        var firstRecorder = firstRouter.StartRecording();
        var secondRecorder = secondRouter.StartRecording();
        var segmentSequence = sequenceLease.Allocate();
        var firstSegment = new SimulationClockSegment(1, 0, segmentSequence, 0, 0.0, 60, 0, 1.0f, isPaused: false);
        var secondSegment = new SimulationClockSegment(1, 0, segmentSequence, 0, 0.0, 60, 0, 0.5f, isPaused: false);
        firstRouter.SetSimulationClockSegment(in firstSegment);
        secondRouter.SetSimulationClockSegment(in secondSegment);
        var sharedSequence = sequenceLease.Allocate();
        var sharedEvent = CreateKeyEvent(
            PhysicalInputEventKind.KeyDown,
            new WindowHandle(1, 0, 1),
            new InputDeviceHandle(1, 1, 1),
            key,
            hostTimestamp: 333_332,
            sharedSequence,
            isDown: true);

        firstRouter.BeginHostControlBoundary(0, 333_332);
        secondRouter.BeginHostControlBoundary(0, 333_332);
        var firstReceipt = firstRouter.EnqueuePhysicalEvent(sharedEvent, null, InputClass.None, out _);
        var secondReceipt = secondRouter.EnqueuePhysicalEvent(sharedEvent, null, InputClass.None, out _);
        Assert.That(firstReceipt.AssignedTick, Is.EqualTo(1));
        Assert.That(secondReceipt.AssignedTick, Is.EqualTo(0));
        _ = firstRouter.ServiceInputControlBoundary();
        _ = secondRouter.ServiceInputControlBoundary();
        _ = firstRouter.PublishSimulationActionSnapshot(0);
        _ = firstRouter.PublishSimulationActionSnapshot(1);
        _ = secondRouter.PublishSimulationActionSnapshot(0);

        var firstRecording = firstRecorder.Complete();
        var secondRecording = secondRecorder.Complete();
        var firstEvent = firstRecording.Entries.Single(static entry => entry.Kind == InputRecordingEntryKind.PhysicalInput);
        var secondEvent = secondRecording.Entries.Single(static entry => entry.Kind == InputRecordingEntryKind.PhysicalInput);
        Assert.That(firstEvent.HostSequence, Is.EqualTo(secondEvent.HostSequence));
        Assert.That(firstEvent.AssignedTick, Is.EqualTo(1));
        Assert.That(secondEvent.AssignedTick, Is.EqualTo(0));

        var firstReplay = InputReplay.Play(firstRecording);
        var secondReplay = InputReplay.Play(secondRecording);
        Assert.That(firstReplay.Snapshots.Select(static snapshot => snapshot.AssignedTick), Is.EqualTo(new long?[] { 0, 1 }));
        Assert.That(firstReplay.Snapshots[0].Actions[0].IsDownAtEnd, Is.False);
        Assert.That(firstReplay.Snapshots[1].Actions[0].IsDownAtEnd, Is.True);
        Assert.That(secondReplay.Snapshots.Select(static snapshot => snapshot.AssignedTick), Is.EqualTo(new long?[] { 0 }));
        Assert.That(secondReplay.Snapshots[0].Actions[0].IsDownAtEnd, Is.True);

        firstRouter.Dispose();
        secondRouter.Dispose();
    }

    [Test]
    public void ReplaysPausedCopiesAsIntentionalRecipientDiscards()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            7,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        var recorder = router.StartRecording();
        router.BeginHostControlBoundary(0, 100);
        var pauseSequence = sequenceLease.Allocate();
        Assert.That(router.CancelInputState(
            InputCancellationReason.Paused,
            InputClass.Keyboard | InputClass.Pointing | InputClass.Gamepad,
            playerSlot: null,
            hostTimestamp: 100,
            hostSequence: pauseSequence,
            out _).Status, Is.EqualTo(InputRoutingStatus.Applied));
        _ = router.ServiceInputControlBoundary();

        var pausedSequence = sequenceLease.Allocate();
        var pausedReceipt = router.EnqueueHeadlessInjection(
            new InputInjection(new KeyboardControl(7, 4), InputKeyEventKind.Down),
            hostTimestamp: 101,
            hostSequence: pausedSequence,
            assignedTick: 0);
        Assert.That(pausedReceipt.Status, Is.EqualTo(InputRoutingStatus.Rejected));

        router.BeginHostControlBoundary(0, 102);
        var reconciliationSequence = sequenceLease.Allocate();
        var reconciliationReceipt = router.ReconcileInputState(
            [],
            InputClass.Keyboard | InputClass.Pointing | InputClass.Gamepad,
            playerSlot: null,
            hostTimestamp: 102,
            hostSequence: reconciliationSequence,
            out _,
            physicalInputs: []);
        Assert.That(reconciliationReceipt.Status, Is.EqualTo(InputRoutingStatus.Applied));
        _ = router.ServiceInputControlBoundary();
        Assert.That(router.IsPaused, Is.False);
        _ = router.PublishSimulationActionSnapshot(0);

        var recording = recorder.Complete();
        Assert.That(recording.IsComplete, Is.True);
        Assert.That(recording.Entries.Any(entry => entry.Kind == InputRecordingEntryKind.PausedInputDiscard
            && entry.HostSequence == pausedSequence), Is.True);
        var replay = InputReplay.Play(recording);
        Assert.That(replay.IsComplete, Is.True);
        Assert.That(replay.Snapshots, Has.Length.EqualTo(1));
        Assert.That(replay.Snapshots[0].AssignedTick, Is.EqualTo(0));

        router.Dispose();
    }

    [Test]
    public void ReplaysTimelineClockAssignmentsAndCopiedUiDisposition()
    {
        var sequenceLease = new HostEventSequenceLease();
        var key = new KeyboardControl(7, 4);
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            7,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        _ = router.RegisterMap(CreateMap(key));
        var recorder = router.StartRecording();
        var segmentSequence = sequenceLease.Allocate();
        var segment = new SimulationClockSegment(
            revision: 1,
            hostTimestampStartTicks: 0,
            hostSequenceStart: segmentSequence,
            simulationElapsedStartTicks: 0,
            fractionalSimulationTicks: 0.0,
            tickRate: 60,
            nextOpenTick: 0,
            timeScale: 1.0f,
            isPaused: false);
        router.SetSimulationClockSegment(in segment);
        var window = new WindowHandle(1, 2, 1);
        var device = new InputDeviceHandle(window.OwnerId, 1, 1);

        var downSequence = sequenceLease.Allocate();
        var down = CreateKeyEvent(
            PhysicalInputEventKind.KeyDown,
            window,
            device,
            key,
            hostTimestamp: 166_666,
            downSequence,
            isDown: true);
        var downReceipt = router.EnqueuePhysicalEvent(
            down,
            playerSlot: null,
            InputClass.Keyboard,
            out _);
        Assert.That(downReceipt.AssignedTick, Is.EqualTo(0));
        router.BeginHostControlBoundary(0, 166_666);
        _ = router.ServiceInputControlBoundary();
        _ = router.PublishSimulationActionSnapshot(0);

        var upSequence = sequenceLease.Allocate();
        var up = CreateKeyEvent(
            PhysicalInputEventKind.KeyUp,
            window,
            device,
            key,
            hostTimestamp: 333_332,
            upSequence,
            isDown: false);
        var upReceipt = router.EnqueuePhysicalEvent(
            up,
            playerSlot: null,
            InputClass.Keyboard,
            out _);
        Assert.That(upReceipt.AssignedTick, Is.EqualTo(1));
        router.BeginHostControlBoundary(1, 333_332);
        _ = router.ServiceInputControlBoundary();
        _ = router.PublishSimulationActionSnapshot(1);

        var recording = recorder.Complete();
        var replay = InputReplay.Play(recording);
        Assert.That(replay.IsComplete, Is.True);
        Assert.That(replay.Snapshots, Has.Length.EqualTo(2));
        Assert.That(replay.Snapshots[0].AssignedTick, Is.EqualTo(0));
        Assert.That(replay.Snapshots[1].AssignedTick, Is.EqualTo(1));
        Assert.That(replay.Snapshots.All(static snapshot => !snapshot.Actions[0].IsDownAtEnd), Is.True);
        Assert.That(replay.Snapshots.All(static snapshot => snapshot.Actions[0].Transitions.IsEmpty), Is.True);

        var copiedEvents = recording.Entries
            .Where(static entry => entry.Kind == InputRecordingEntryKind.PhysicalInput)
            .ToArray();
        Assert.That(copiedEvents, Has.Length.EqualTo(2));
        Assert.That(copiedEvents.All(static entry => entry.UiCapturedClasses == InputClass.Keyboard), Is.True);
        Assert.That(copiedEvents.Select(static entry => entry.AssignedTick), Is.EqualTo(new long?[] { 0, 1 }));

        router.Dispose();
    }
}
