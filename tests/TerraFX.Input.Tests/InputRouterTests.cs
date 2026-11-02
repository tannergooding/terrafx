// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Input;
using TerraFX.Numerics;

namespace TerraFX.Input.Tests;

[TestFixture]
internal sealed class InputRouterTests
{
    private static InputActionMapV1 CreateMap(Guid mapId, long revision, params ActionDefinition[] actions)
    {
        var contexts = ImmutableArray.Create(new ContextDefinition(Guid.NewGuid(), 0));
        return new InputActionMapV1(1, mapId, revision, 1, [.. actions], contexts, []);
    }

    private static InputActionMapV1 CreateMapWithContext(Guid mapId, long revision, ContextDefinition context, params ActionDefinition[] actions)
        => new InputActionMapV1(1, mapId, revision, 1, [.. actions], [context], []);

    private static InputActionMapV1 CreateMapWithContexts(Guid mapId, long revision, ImmutableArray<ContextDefinition> contexts, ulong initialActiveContextMask)
        => new InputActionMapV1(1, mapId, revision, initialActiveContextMask, [], contexts, []);

    private static InputRouter CreateRouter(InputRecipientKind recipientKind = InputRecipientKind.HostLiveUI, uint recipientId = 1)
        => new InputRouter(
            recipientKind,
            recipientId,
            Environment.CurrentManagedThreadId,
            new HostEventSequenceLease(),
            recipientKind == InputRecipientKind.HostLiveUI ? new WindowHandle(1, 0, 1) : null);

    [Test]
    public void AppliesSharedHeadlessCaptureToEachRecipientAtItsOwnAssignedTick()
    {
        var firstRouter = CreateRouter(InputRecipientKind.SimulationTimeline, recipientId: 3);
        var secondRouter = CreateRouter(InputRecipientKind.SimulationTimeline, recipientId: 4);
        var mapId = Guid.NewGuid();
        var action = new ActionDefinition(Guid.NewGuid(), "Move", InputActionKind.Digital);
        var context = new ContextDefinition(Guid.NewGuid(), 0);
        var binding = new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.Button) {
            Control = new DigitalControl(new KeyboardControl(7, 4)),
        };
        var map = new InputActionMapV1(1, mapId, 1, 1, [action], [context], [binding]);
        var keyDown = new InputInjection(new KeyboardControl(7, 4), InputKeyEventKind.Down);
        var keyUp = new InputInjection(new KeyboardControl(7, 4), InputKeyEventKind.Up);

        var firstMapResponse = firstRouter.RegisterMap(map);
        var secondMapResponse = secondRouter.RegisterMap(map);
        Assert.That(firstMapResponse.Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(secondMapResponse.Status, Is.EqualTo(InputMapStatus.Registered));
        var firstCaptureSequence = Math.Max(
            firstMapResponse.HostSequence.GetValueOrDefault(),
            secondMapResponse.HostSequence.GetValueOrDefault()) + 1;
        var secondCaptureSequence = firstCaptureSequence + 1;

        Assert.That(firstRouter.EnqueueHeadlessInjection(keyDown, 166_666, firstCaptureSequence, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(secondRouter.EnqueueHeadlessInjection(keyDown, 166_666, firstCaptureSequence, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(firstRouter.EnqueueHeadlessInjection(keyUp, 333_332, secondCaptureSequence, 1).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(secondRouter.EnqueueHeadlessInjection(keyUp, 333_332, secondCaptureSequence, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));

        var firstTick = firstRouter.PublishSimulationActionSnapshot(0);
        var secondTick = secondRouter.PublishSimulationActionSnapshot(0);

        Assert.That(firstTick.Actions[0].IsDownAtEnd, Is.True);
        Assert.That(firstTick.Actions[0].PressedThisTick, Is.True);
        Assert.That(firstTick.Actions[0].ReleasedThisTick, Is.False);
        Assert.That(firstTick.Actions[0].Transitions, Has.Length.EqualTo(1));
        Assert.That(firstTick.Actions[0].Transitions[0].HostSequence, Is.EqualTo(firstCaptureSequence));
        Assert.That(firstTick.Actions[0].Transitions[0].AssignedTick, Is.EqualTo(0));

        Assert.That(secondTick.Actions[0].IsDownAtEnd, Is.False);
        Assert.That(secondTick.Actions[0].PressedThisTick, Is.True);
        Assert.That(secondTick.Actions[0].ReleasedThisTick, Is.True);
        Assert.That(secondTick.Actions[0].Transitions, Has.Length.EqualTo(2));
        Assert.That(secondTick.Actions[0].Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Pressed));
        Assert.That(secondTick.Actions[0].Transitions[0].HostSequence, Is.EqualTo(firstCaptureSequence));
        Assert.That(secondTick.Actions[0].Transitions[1].Kind, Is.EqualTo(InputTransitionKind.Released));
        Assert.That(secondTick.Actions[0].Transitions[1].HostSequence, Is.EqualTo(secondCaptureSequence));

        var nextFirstTick = firstRouter.PublishSimulationActionSnapshot(1);
        Assert.That(nextFirstTick.Actions[0].IsDownAtEnd, Is.False);
        Assert.That(nextFirstTick.Actions[0].PressedThisTick, Is.False);
        Assert.That(nextFirstTick.Actions[0].ReleasedThisTick, Is.True);
        Assert.That(nextFirstTick.Actions[0].Transitions, Has.Length.EqualTo(1));
        Assert.That(nextFirstTick.Actions[0].Transitions[0].AssignedTick, Is.EqualTo(1));

        firstRouter.Dispose();
        secondRouter.Dispose();
    }

    [Test]
    public void AppliesSharedPhysicalKeyboardCaptureAtRecipientLocalTicks()
    {
        var firstRouter = CreateRouter(InputRecipientKind.SimulationTimeline, recipientId: 31);
        var secondRouter = CreateRouter(InputRecipientKind.SimulationTimeline, recipientId: 32);
        var mapId = Guid.NewGuid();
        var action = new ActionDefinition(Guid.NewGuid(), "Move", InputActionKind.Digital);
        var context = new ContextDefinition(Guid.NewGuid(), 0);
        var binding = new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.Button) {
            Control = new DigitalControl(new KeyboardControl(7, 4)),
        };
        var map = new InputActionMapV1(1, mapId, 1, 1, [action], [context], [binding]);

        var firstMapResponse = firstRouter.RegisterMap(map);
        var secondMapResponse = secondRouter.RegisterMap(map);
        Assert.That(firstMapResponse.Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(secondMapResponse.Status, Is.EqualTo(InputMapStatus.Registered));
        var segmentSequence = Math.Max(
            firstMapResponse.HostSequence.GetValueOrDefault(),
            secondMapResponse.HostSequence.GetValueOrDefault()) + 1;
        var firstSegment = new SimulationClockSegment(1, 0, segmentSequence, 0, 0.0, 60, 0, 1.0f, isPaused: false);
        var secondSegment = new SimulationClockSegment(1, 0, segmentSequence, 0, 0.0, 60, 1, 1.0f, isPaused: false);
        firstRouter.SetSimulationClockSegment(in firstSegment);
        secondRouter.SetSimulationClockSegment(in secondSegment);

        var inputEvent = new PhysicalInputEvent(
            PhysicalInputEventKind.KeyDown,
            166_666,
            segmentSequence,
            device: new InputDeviceHandle(1, 0, 1),
            window: new WindowHandle(1, 0, 1),
            value: new PhysicalInputValue(new DigitalControl(new KeyboardControl(7, 4)), isDown: true));

        var firstReceipt = firstRouter.EnqueuePhysicalEvent(inputEvent, playerSlot: null, discardedInputCount: out _);
        var secondReceipt = secondRouter.EnqueuePhysicalEvent(inputEvent, playerSlot: null, discardedInputCount: out _);

        Assert.That(firstReceipt.Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(firstReceipt.AssignedTick, Is.EqualTo(0));
        Assert.That(firstReceipt.Late, Is.False);
        Assert.That(secondReceipt.Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(secondReceipt.AssignedTick, Is.EqualTo(1));
        Assert.That(secondReceipt.Late, Is.True);

        var firstSnapshot = firstRouter.PublishSimulationActionSnapshot(0);
        var secondSnapshot = secondRouter.PublishSimulationActionSnapshot(1);
        Assert.That(firstSnapshot.Actions[0].IsDownAtEnd, Is.True);
        Assert.That(firstSnapshot.Actions[0].Transitions[0].HostSequence, Is.EqualTo(inputEvent.HostSequence));
        Assert.That(firstSnapshot.Actions[0].Transitions[0].AssignedTick, Is.EqualTo(0));
        Assert.That(secondSnapshot.Actions[0].IsDownAtEnd, Is.True);
        Assert.That(secondSnapshot.Actions[0].Transitions[0].HostSequence, Is.EqualTo(inputEvent.HostSequence));
        Assert.That(secondSnapshot.Actions[0].Transitions[0].AssignedTick, Is.EqualTo(1));

        firstRouter.Dispose();
        secondRouter.Dispose();
    }

    [Test]
    public void AppliesTimelineClockSegmentsAndReportsLateAssignment()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            37,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        var key = new KeyboardControl(7, 4);
        var action = new ActionDefinition(Guid.NewGuid(), "Move", InputActionKind.Digital);
        var context = new ContextDefinition(Guid.NewGuid(), 0);
        var binding = new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.Button) {
            Control = new DigitalControl(key),
        };
        var map = CreateMapWithContext(Guid.NewGuid(), 1, context, action);
        var mapResponse = router.RegisterMap(new InputActionMapV1(
            map.SchemaVersion,
            map.MapId,
            map.Revision,
            map.InitialActiveContextMask,
            map.Actions,
            map.Contexts,
            [binding]));
        Assert.That(mapResponse.Status, Is.EqualTo(InputMapStatus.Registered));

        var unavailableSequence = sequenceLease.Allocate();
        var unavailableReceipt = router.EnqueueHeadlessInjection(
            new InputInjection(key, InputKeyEventKind.Down),
            1,
            unavailableSequence);
        Assert.That(unavailableReceipt.Status, Is.EqualTo(InputRoutingStatus.Rejected));
        Assert.That(unavailableReceipt.Rejection?.Code, Is.EqualTo("ClockSegmentUnavailable"));

        var firstSequence = sequenceLease.Allocate();
        var segment = new SimulationClockSegment(
            1,
            0,
            firstSequence,
            0,
            0.0,
            60,
            0,
            1.0f,
            isPaused: false);
        router.SetSimulationClockSegment(in segment);

        var earlyReceipt = router.EnqueueHeadlessInjection(
            new InputInjection(key, InputKeyEventKind.Down),
            0,
            firstSequence - 1);
        Assert.That(earlyReceipt.Status, Is.EqualTo(InputRoutingStatus.Rejected));
        Assert.That(earlyReceipt.Rejection?.Code, Is.EqualTo("InvalidEventOrder"));

        var downReceipt = router.EnqueueHeadlessInjection(
            new InputInjection(key, InputKeyEventKind.Down),
            166_666,
            firstSequence);
        Assert.That(downReceipt.Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(downReceipt.HostSequence, Is.EqualTo(firstSequence));
        Assert.That(downReceipt.AssignedTick, Is.EqualTo(0));
        Assert.That(downReceipt.Late, Is.False);

        _ = router.PublishSimulationActionSnapshot(0);
        _ = router.PublishSimulationActionSnapshot(1);

        var lateSequence = sequenceLease.Allocate();
        var lateReceipt = router.EnqueueHeadlessInjection(
            new InputInjection(key, InputKeyEventKind.Up),
            166_667,
            lateSequence);
        Assert.That(lateReceipt.Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(lateReceipt.HostSequence, Is.EqualTo(lateSequence));
        Assert.That(lateReceipt.AssignedTick, Is.EqualTo(2));
        Assert.That(lateReceipt.Late, Is.True);

        var snapshot = router.PublishSimulationActionSnapshot(2);
        Assert.That(snapshot.Actions, Has.Length.EqualTo(1));
        Assert.That(snapshot.Actions[0].Transitions, Has.Length.EqualTo(1));
        Assert.That(snapshot.Actions[0].Transitions[0].HostSequence, Is.EqualTo(lateSequence));
        Assert.That(snapshot.Actions[0].Transitions[0].AssignedTick, Is.EqualTo(2));

        router.Dispose();
    }

    [Test]
    public void AppliesTimelineClockSegmentsWithoutEligibleTicksAsRejections()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            38,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        var sequence = sequenceLease.Allocate();
        var pausedSegment = new SimulationClockSegment(
            1,
            0,
            sequence,
            0,
            0.0,
            60,
            0,
            0.0f,
            isPaused: false);
        router.SetSimulationClockSegment(in pausedSegment);

        var receipt = router.EnqueueHeadlessInjection(
            new InputInjection(new KeyboardControl(7, 4), InputKeyEventKind.Down),
            0,
            sequence);
        Assert.That(receipt.Status, Is.EqualTo(InputRoutingStatus.Rejected));
        Assert.That(receipt.Rejection?.Code, Is.EqualTo("NoEligibleTick"));
        Assert.That(receipt.HostSequence, Is.Null);
        Assert.That(receipt.AssignedTick, Is.Null);
        Assert.That(receipt.Late, Is.False);

        router.Dispose();
    }

    [Test]
    public void AssignsSharedPhysicalGamepadCaptureToEachRecipientsOwnPlayerSlot()
    {
        var firstRouter = CreateRouter(InputRecipientKind.SimulationTimeline, recipientId: 41);
        var secondRouter = CreateRouter(InputRecipientKind.SimulationTimeline, recipientId: 42);
        var mapId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var context = new ContextDefinition(Guid.NewGuid(), 0);
        var action = new ActionDefinition(actionId, "Jump", InputActionKind.Digital);
        var firstBinding = new BindingDefinition(Guid.NewGuid(), actionId, 1, BindingKind.Button) {
            Control = new DigitalControl(playerSlot: 3, GamepadButtonValue.South),
        };
        var secondBinding = new BindingDefinition(Guid.NewGuid(), actionId, 1, BindingKind.Button) {
            Control = new DigitalControl(playerSlot: 5, GamepadButtonValue.South),
        };
        var firstMap = new InputActionMapV1(1, mapId, 1, 1, [action], [context], [firstBinding]);
        var secondMap = new InputActionMapV1(1, mapId, 1, 1, [action], [context], [secondBinding]);

        var firstMapResponse = firstRouter.RegisterMap(firstMap);
        var secondMapResponse = secondRouter.RegisterMap(secondMap);
        Assert.That(firstMapResponse.Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(secondMapResponse.Status, Is.EqualTo(InputMapStatus.Registered));
        var segmentSequence = Math.Max(
            firstMapResponse.HostSequence.GetValueOrDefault(),
            secondMapResponse.HostSequence.GetValueOrDefault()) + 1;
        var segment = new SimulationClockSegment(1, 0, segmentSequence, 0, 0.0, 60, 0, 1.0f, isPaused: false);
        firstRouter.SetSimulationClockSegment(in segment);
        secondRouter.SetSimulationClockSegment(in segment);

        var inputEvent = new PhysicalInputEvent(
            PhysicalInputEventKind.GamepadButtonDown,
            166_666,
            segmentSequence,
            device: new InputDeviceHandle(1, 0, 1),
            value: new PhysicalInputValue(GamepadButtonValue.South, isDown: true));

        var firstReceipt = firstRouter.EnqueuePhysicalEvent(inputEvent, playerSlot: 3, discardedInputCount: out _);
        var secondReceipt = secondRouter.EnqueuePhysicalEvent(inputEvent, playerSlot: 5, discardedInputCount: out _);
        Assert.That(firstReceipt.Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(secondReceipt.Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(firstReceipt.HostSequence, Is.EqualTo(secondReceipt.HostSequence));

        Assert.That(firstRouter.PublishSimulationActionSnapshot(0).Actions[0].IsDownAtEnd, Is.True);
        Assert.That(secondRouter.PublishSimulationActionSnapshot(0).Actions[0].IsDownAtEnd, Is.True);

        firstRouter.Dispose();
        secondRouter.Dispose();
    }

    [Test]
    public void CancelsFocusScopedInputWithoutClearingGamepadState()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            24,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        var keyboardAction = new ActionDefinition(new Guid("20000000-0000-0000-0000-000000000101"), "Keyboard", InputActionKind.Digital);
        var combinedAction = new ActionDefinition(new Guid("20000000-0000-0000-0000-000000000102"), "Combined", InputActionKind.Digital);
        var context = new ContextDefinition(new Guid("30000000-0000-0000-0000-000000000101"), 0);
        var key = new KeyboardControl(7, 4);
        var map = new InputActionMapV1(
            1,
            new Guid("10000000-0000-0000-0000-000000000101"),
            1,
            1,
            [keyboardAction, combinedAction],
            [context],
            [
                new BindingDefinition(Guid.NewGuid(), keyboardAction.ActionId, 1, BindingKind.Button) {
                    Control = new DigitalControl(key),
                },
                new BindingDefinition(Guid.NewGuid(), combinedAction.ActionId, 1, BindingKind.Button) {
                    Control = new DigitalControl(key),
                },
                new BindingDefinition(Guid.NewGuid(), combinedAction.ActionId, 1, BindingKind.Button) {
                    Control = new DigitalControl(1, GamepadButtonValue.South),
                },
            ]);

        Assert.That(router.RegisterMap(map).Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(key, InputKeyEventKind.Down), 1, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(1, GamepadButtonValue.South, true), 2, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        var active = router.PublishSimulationActionSnapshot(0);
        Assert.That(active.Actions[0].IsDownAtEnd, Is.True);
        Assert.That(active.Actions[1].IsDownAtEnd, Is.True);

        router.BeginHostControlBoundary(1, 3);
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(key, InputKeyEventKind.Down), 3, sequenceLease.Allocate(), 1).Status, Is.EqualTo(InputRoutingStatus.Applied));
        var cancellation = router.EnqueuePhysicalEvent(
            new PhysicalInputEvent(
                PhysicalInputEventKind.FocusLost,
                3,
                sequenceLease.Allocate(),
                window: new WindowHandle(1, 0, 1)),
            playerSlot: null,
            discardedInputCount: out var discardedInputCount);
        Assert.That(cancellation.Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(discardedInputCount, Is.EqualTo(1));

        var boundaryTransitions = router.ServiceInputControlBoundary();
        Assert.That(boundaryTransitions, Has.Length.EqualTo(1));
        Assert.That(boundaryTransitions[0].ActionId, Is.EqualTo(keyboardAction.ActionId));
        Assert.That(boundaryTransitions[0].Kind, Is.EqualTo(InputTransitionKind.Canceled));
        Assert.That(boundaryTransitions[0].CancellationReason, Is.EqualTo(InputCancellationReason.FocusLost));

        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(key, InputKeyEventKind.Down), 4, sequenceLease.Allocate(), 1).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(1, GamepadButtonValue.South, false), 5, sequenceLease.Allocate(), 1).Status, Is.EqualTo(InputRoutingStatus.Applied));
        var afterFocusLoss = router.PublishSimulationActionSnapshot(1);
        Assert.That(afterFocusLoss.Actions[0].IsDownAtEnd, Is.False);
        Assert.That(afterFocusLoss.Actions[0].Transitions, Is.Empty);
        Assert.That(afterFocusLoss.Actions[1].IsDownAtEnd, Is.False);
        Assert.That(afterFocusLoss.Actions[1].Transitions, Has.Length.EqualTo(1));
        Assert.That(afterFocusLoss.Actions[1].Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Released));

        router.BeginHostControlBoundary(2, 6);
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(key, InputKeyEventKind.Up), 6, sequenceLease.Allocate(), 2).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.PublishSimulationActionSnapshot(2).Actions[0].Transitions, Is.Empty);
        router.BeginHostControlBoundary(3, 7);
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(key, InputKeyEventKind.Down), 7, sequenceLease.Allocate(), 3).Status, Is.EqualTo(InputRoutingStatus.Applied));
        var rearmed = router.PublishSimulationActionSnapshot(3);
        Assert.That(rearmed.Actions[0].IsDownAtEnd, Is.True);
        Assert.That(rearmed.Actions[0].Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Pressed));

        router.Dispose();
    }

    [Test]
    public void CancelsOnlyTheRemovedGamepadPlayerSlot()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            25,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        var removedAction = new ActionDefinition(new Guid("20000000-0000-0000-0000-000000000111"), "Removed", InputActionKind.Digital);
        var retainedAction = new ActionDefinition(new Guid("20000000-0000-0000-0000-000000000112"), "Retained", InputActionKind.Digital);
        var combinedAction = new ActionDefinition(new Guid("20000000-0000-0000-0000-000000000113"), "Combined", InputActionKind.Digital);
        var context = new ContextDefinition(new Guid("30000000-0000-0000-0000-000000000111"), 0);
        var slotOneButton = new DigitalControl(1, GamepadButtonValue.South);
        var slotTwoButton = new DigitalControl(2, GamepadButtonValue.East);
        var map = new InputActionMapV1(
            1,
            new Guid("10000000-0000-0000-0000-000000000111"),
            1,
            1,
            [removedAction, retainedAction, combinedAction],
            [context],
            [
                new BindingDefinition(Guid.NewGuid(), removedAction.ActionId, 1, BindingKind.Button) {
                    Control = slotOneButton,
                },
                new BindingDefinition(Guid.NewGuid(), retainedAction.ActionId, 1, BindingKind.Button) {
                    Control = slotTwoButton,
                },
                new BindingDefinition(Guid.NewGuid(), combinedAction.ActionId, 1, BindingKind.Button) {
                    Control = slotOneButton,
                },
                new BindingDefinition(Guid.NewGuid(), combinedAction.ActionId, 1, BindingKind.Button) {
                    Control = slotTwoButton,
                },
            ]);

        Assert.That(router.RegisterMap(map).Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(1, GamepadButtonValue.South, true), 1, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(2, GamepadButtonValue.East, true), 2, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        var active = router.PublishSimulationActionSnapshot(0);
        Assert.That(active.Actions[0].IsDownAtEnd, Is.True);
        Assert.That(active.Actions[1].IsDownAtEnd, Is.True);
        Assert.That(active.Actions[2].IsDownAtEnd, Is.True);

        router.BeginHostControlBoundary(1, 3);
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(1, GamepadButtonValue.South, true), 3, sequenceLease.Allocate(), 1).Status, Is.EqualTo(InputRoutingStatus.Applied));
        var cancellation = router.EnqueuePhysicalEvent(
            new PhysicalInputEvent(
                PhysicalInputEventKind.DeviceRemoved,
                3,
                sequenceLease.Allocate(),
                device: new InputDeviceHandle(1, 0, 1)),
            playerSlot: 1,
            discardedInputCount: out var discardedInputCount);
        Assert.That(cancellation.Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(discardedInputCount, Is.EqualTo(1));

        var transitions = router.ServiceInputControlBoundary();
        Assert.That(transitions, Has.Length.EqualTo(1));
        Assert.That(transitions[0].ActionId, Is.EqualTo(removedAction.ActionId));
        Assert.That(transitions[0].CancellationReason, Is.EqualTo(InputCancellationReason.DeviceRemoved));

        var reconciliation = router.ReconcileInputState(
            [new InputInjection(1, GamepadButtonValue.South, true)],
            InputClass.Gamepad,
            playerSlot: 1,
            hostTimestamp: 4,
            hostSequence: sequenceLease.Allocate(),
            out discardedInputCount);
        Assert.That(reconciliation.Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(discardedInputCount, Is.Zero);
        Assert.That(router.ServiceInputControlBoundary(), Is.Empty);

        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(1, GamepadButtonValue.South, false), 5, sequenceLease.Allocate(), 1).Status, Is.EqualTo(InputRoutingStatus.Applied));
        var neutral = router.PublishSimulationActionSnapshot(1);
        Assert.That(neutral.Actions[0].IsDownAtEnd, Is.False);
        Assert.That(neutral.Actions[1].IsDownAtEnd, Is.True);
        Assert.That(neutral.Actions[2].IsDownAtEnd, Is.True);

        router.BeginHostControlBoundary(2, 6);
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(1, GamepadButtonValue.South, true), 6, sequenceLease.Allocate(), 2).Status, Is.EqualTo(InputRoutingStatus.Applied));
        var rearmed = router.PublishSimulationActionSnapshot(2);
        Assert.That(rearmed.Actions[0].IsDownAtEnd, Is.True);
        Assert.That(rearmed.Actions[0].Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Pressed));

        router.Dispose();
    }

    [Test]
    public void CapturesKeyboardFromAHigherPriorityContextAcrossMaps()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            14,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        var action = new ActionDefinition(Guid.NewGuid(), "Move", InputActionKind.Digital);
        var lowContext = new ContextDefinition(Guid.NewGuid(), 0, priority: 0);
        var key = new KeyboardControl(7, 4);
        var binding = new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.Button) {
            Control = new DigitalControl(key),
        };
        var lowerMap = new InputActionMapV1(
            1,
            Guid.NewGuid(),
            1,
            1,
            [action],
            [lowContext],
            [binding]);
        var lowerRegistration = router.RegisterMap(lowerMap);
        Assert.That(lowerRegistration.Status, Is.EqualTo(InputMapStatus.Registered));

        var keyDownSequence = sequenceLease.Allocate();
        Assert.That(
            router.EnqueueHeadlessInjection(
                new InputInjection(key, InputKeyEventKind.Down),
                1,
                keyDownSequence,
                0).Status,
            Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.PublishSimulationActionSnapshot(0).Actions[0].IsDownAtEnd, Is.True);

        var highContext = new ContextDefinition(Guid.NewGuid(), 0, priority: 10, captureClasses: InputClass.Keyboard);
        var highMap = CreateMapWithContext(Guid.NewGuid(), 1, highContext);
        Assert.That(router.RegisterMap(highMap).Status, Is.EqualTo(InputMapStatus.Registered));

        var captured = router.PublishSimulationActionSnapshot(1);
        Assert.That(captured.Actions, Has.Length.EqualTo(1));
        Assert.That(captured.Actions[0].IsDownAtEnd, Is.False);
        Assert.That(captured.Actions[0].ReleasedThisTick, Is.False);
        Assert.That(captured.Actions[0].Transitions, Has.Length.EqualTo(1));
        Assert.That(captured.Actions[0].Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Canceled));
        Assert.That(captured.Actions[0].Transitions[0].CancellationReason, Is.EqualTo(InputCancellationReason.ContextMasked));

        router.Dispose();
    }

    [Test]
    public void CoalescesAdjacentAnalogEventsToTheLatestValueAndSequence()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            29,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        var action = new ActionDefinition(Guid.NewGuid(), "Move", InputActionKind.Axis1);
        var context = new ContextDefinition(Guid.NewGuid(), 0);
        var map = new InputActionMapV1(
            1,
            Guid.NewGuid(),
            1,
            1,
            [action],
            [context],
            [
                new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.GamepadAxis1) {
                    Axis1Source = new GamepadAxis1Source(1, GamepadAxis.RightX, deadzone: 0.0f),
                },
            ]);
        Assert.That(router.RegisterMap(map).Status, Is.EqualTo(InputMapStatus.Registered));
        var firstSequence = sequenceLease.Allocate();
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(1, GamepadAxis.RightX, 0.1f), 1, firstSequence, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        var lastSequence = sequenceLease.Allocate();
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(1, GamepadAxis.RightX, 0.2f), 2, lastSequence, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));

        for (var index = 0; index < 4096; index++)
        {
            lastSequence = sequenceLease.Allocate();
            var value = 0.2f + (index * 0.00001f);
            Assert.That(router.EnqueueHeadlessInjection(new InputInjection(1, GamepadAxis.RightX, value), index + 3, lastSequence, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        }

        var snapshot = router.PublishSimulationActionSnapshot(0);
        Assert.That(snapshot.Actions[0].AxisX, Is.EqualTo(0.24095f).Within(1e-6f));
        Assert.That(snapshot.Actions[0].Transitions, Has.Length.EqualTo(1));
        Assert.That(snapshot.Actions[0].Transitions[0].Kind, Is.EqualTo(InputTransitionKind.ValueChanged));
        Assert.That(snapshot.Actions[0].Transitions[0].HostSequence, Is.EqualTo(lastSequence));
        Assert.That(snapshot.Actions[0].IsDownAtEnd, Is.False);

        router.Dispose();
    }

    [Test]
    public void CombinesDigitalBindingsWithOrAcrossOneTick()
    {
        var router = CreateRouter(InputRecipientKind.SimulationTimeline, recipientId: 8);
        var action = new ActionDefinition(Guid.NewGuid(), "Jump", InputActionKind.Digital);
        var context = new ContextDefinition(Guid.NewGuid(), 0);
        var keyboard = new KeyboardControl(7, 4);
        var map = new InputActionMapV1(
            1,
            Guid.NewGuid(),
            1,
            1,
            [action],
            [context],
            [
                new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.Button) {
                    Control = new DigitalControl(keyboard),
                },
                new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.Button) {
                    Control = new DigitalControl(1, GamepadButtonValue.South),
                },
            ]);
        var registration = router.RegisterMap(map);
        var sequence = registration.HostSequence.GetValueOrDefault() + 1;

        Assert.That(registration.Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(keyboard, InputKeyEventKind.Down), 1, sequence++, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(1, GamepadButtonValue.South, true), 2, sequence++, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(keyboard, InputKeyEventKind.Up), 3, sequence++, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(1, GamepadButtonValue.South, false), 4, sequence, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));

        var snapshot = router.PublishSimulationActionSnapshot(0);

        Assert.That(snapshot.Actions[0].IsDownAtEnd, Is.False);
        Assert.That(snapshot.Actions[0].PressedThisTick, Is.True);
        Assert.That(snapshot.Actions[0].ReleasedThisTick, Is.True);
        Assert.That(snapshot.Actions[0].Transitions, Has.Length.EqualTo(2));
        Assert.That(snapshot.Actions[0].Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Pressed));
        Assert.That(snapshot.Actions[0].Transitions[0].HostSequence, Is.EqualTo(sequence - 3));
        Assert.That(snapshot.Actions[0].Transitions[1].Kind, Is.EqualTo(InputTransitionKind.Released));
        Assert.That(snapshot.Actions[0].Transitions[1].HostSequence, Is.EqualTo(sequence));

        router.Dispose();
    }

    [Test]
    public void ConsumesChordMembersUntilEveryMemberIsNeutral()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            15,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        var lowerAction = new ActionDefinition(Guid.NewGuid(), "Lower", InputActionKind.Digital);
        var consumerAction = new ActionDefinition(Guid.NewGuid(), "Consumer", InputActionKind.Digital);
        var lowerContext = new ContextDefinition(Guid.NewGuid(), 0, priority: 0);
        var consumerContext = new ContextDefinition(Guid.NewGuid(), 0, priority: 10);
        var firstKey = new KeyboardControl(7, 4);
        var secondKey = new KeyboardControl(7, 5);
        var lowerMap = new InputActionMapV1(
            1,
            new Guid("10000000-0000-0000-0000-000000000061"),
            1,
            1,
            [lowerAction],
            [lowerContext],
            [
                new BindingDefinition(Guid.NewGuid(), lowerAction.ActionId, 1, BindingKind.Button) {
                    Control = new DigitalControl(firstKey),
                },
            ]);
        var consumerMap = new InputActionMapV1(
            1,
            new Guid("10000000-0000-0000-0000-000000000062"),
            1,
            1,
            [consumerAction],
            [consumerContext],
            [
                new BindingDefinition(Guid.NewGuid(), consumerAction.ActionId, 1, BindingKind.Chord) {
                    Controls = [new DigitalControl(firstKey), new DigitalControl(secondKey)],
                    Flags = InputBindingBehavior.ConsumePhysicalEvent,
                },
            ]);

        Assert.That(router.RegisterMap(lowerMap).Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(router.RegisterMap(consumerMap).Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(firstKey, InputKeyEventKind.Down), 1, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(secondKey, InputKeyEventKind.Down), 2, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(firstKey, InputKeyEventKind.Up), 3, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(firstKey, InputKeyEventKind.Down), 4, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(secondKey, InputKeyEventKind.Up), 5, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(firstKey, InputKeyEventKind.Up), 6, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));

        router.BeginHostControlBoundary(0, 7);
        Assert.That(router.SetActiveContextMask(consumerMap.MapId, 1, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(firstKey, InputKeyEventKind.Down), 8, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));

        var snapshot = router.PublishSimulationActionSnapshot(0);
        Assert.That(snapshot.Actions[0].ActionId, Is.EqualTo(lowerAction.ActionId));
        Assert.That(snapshot.Actions[0].IsDownAtEnd, Is.True);
        Assert.That(snapshot.Actions[0].Transitions, Has.Length.EqualTo(1));
        Assert.That(snapshot.Actions[0].Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Pressed));

        router.Dispose();
    }

    [TestCase(BindingKind.Axis1Keys, InputActionKind.Axis1)]
    [TestCase(BindingKind.Axis2Keys, InputActionKind.Axis2)]
    public void ConsumesKeyAxisBindings(BindingKind bindingKind, InputActionKind actionKind)
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            17,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        var lowerAction = new ActionDefinition(new Guid("20000000-0000-0000-0000-000000000041"), "Lower", InputActionKind.Digital);
        var consumerAction = new ActionDefinition(new Guid("20000000-0000-0000-0000-000000000042"), "Consumer", actionKind);
        var lowerContext = new ContextDefinition(Guid.NewGuid(), 0, priority: 0);
        var consumerContext = new ContextDefinition(Guid.NewGuid(), 0, priority: 10);
        var heldKey = new KeyboardControl(7, 4);
        var consumedKey = new KeyboardControl(7, 5);
        var unusedKey = new KeyboardControl(7, 6);
        var lowerMap = new InputActionMapV1(
            1,
            new Guid("10000000-0000-0000-0000-000000000041"),
            1,
            1,
            [lowerAction],
            [lowerContext],
            [
                new BindingDefinition(Guid.NewGuid(), lowerAction.ActionId, 1, BindingKind.Button) {
                    Control = new DigitalControl(heldKey),
                },
                new BindingDefinition(Guid.NewGuid(), lowerAction.ActionId, 1, BindingKind.Button) {
                    Control = new DigitalControl(consumedKey),
                },
            ]);
        var consumerBinding = bindingKind switch {
            BindingKind.Axis1Keys => new BindingDefinition(Guid.NewGuid(), consumerAction.ActionId, 1, bindingKind) {
                Negative = unusedKey,
                Positive = consumedKey,
                Flags = InputBindingBehavior.ConsumePhysicalEvent,
            },
            BindingKind.Axis2Keys => new BindingDefinition(Guid.NewGuid(), consumerAction.ActionId, 1, bindingKind) {
                Left = unusedKey,
                Right = consumedKey,
                Down = new KeyboardControl(7, 7),
                Up = new KeyboardControl(7, 8),
                Flags = InputBindingBehavior.ConsumePhysicalEvent,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(bindingKind)),
        };
        var consumerMap = new InputActionMapV1(
            1,
            new Guid("10000000-0000-0000-0000-000000000042"),
            1,
            0,
            [consumerAction],
            [consumerContext],
            [consumerBinding]);

        Assert.That(router.RegisterMap(lowerMap).Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(router.RegisterMap(consumerMap).Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(heldKey, InputKeyEventKind.Down), 1, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        router.BeginHostControlBoundary(0, 2);
        Assert.That(router.SetActiveContextMask(consumerMap.MapId, 1, 1).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(consumedKey, InputKeyEventKind.Down), 3, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(heldKey, InputKeyEventKind.Up), 4, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));

        var snapshot = router.PublishSimulationActionSnapshot(0);
        Assert.That(snapshot.Actions[0].ActionId, Is.EqualTo(lowerAction.ActionId));
        Assert.That(snapshot.Actions[0].IsDownAtEnd, Is.False);
        Assert.That(snapshot.Actions[0].Transitions, Has.Length.EqualTo(2));
        Assert.That(snapshot.Actions[0].Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Pressed));
        Assert.That(snapshot.Actions[0].Transitions[1].Kind, Is.EqualTo(InputTransitionKind.Canceled));
        Assert.That(snapshot.Actions[0].Transitions[1].CancellationReason, Is.EqualTo(InputCancellationReason.InputConsumed));
        Assert.That(snapshot.Actions[1].ActionId, Is.EqualTo(consumerAction.ActionId));
        Assert.That(snapshot.Actions[1].IsDownAtEnd, Is.True);

        router.Dispose();
    }

    [Test]
    public void ConsumesPhysicalEventsUntilNeutralAndCancelsLowerActions()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            16,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        var lowerAction = new ActionDefinition(new Guid("20000000-0000-0000-0000-000000000031"), "Lower", InputActionKind.Digital);
        var consumerAction = new ActionDefinition(new Guid("20000000-0000-0000-0000-000000000032"), "Consumer", InputActionKind.Digital);
        var lowerContext = new ContextDefinition(Guid.NewGuid(), 0, priority: 0);
        var consumerContext = new ContextDefinition(Guid.NewGuid(), 0, priority: 10);
        var firstKey = new KeyboardControl(7, 4);
        var consumedKey = new KeyboardControl(7, 5);
        var lowerMap = new InputActionMapV1(
            1,
            new Guid("10000000-0000-0000-0000-000000000031"),
            1,
            1,
            [lowerAction],
            [lowerContext],
            [
                new BindingDefinition(Guid.NewGuid(), lowerAction.ActionId, 1, BindingKind.Button) {
                    Control = new DigitalControl(firstKey),
                },
                new BindingDefinition(Guid.NewGuid(), lowerAction.ActionId, 1, BindingKind.Button) {
                    Control = new DigitalControl(consumedKey),
                },
            ]);
        var consumerMap = new InputActionMapV1(
            1,
            new Guid("10000000-0000-0000-0000-000000000032"),
            1,
            0,
            [consumerAction],
            [consumerContext],
            [
                new BindingDefinition(Guid.NewGuid(), consumerAction.ActionId, 1, BindingKind.Button) {
                    Control = new DigitalControl(consumedKey),
                    Flags = InputBindingBehavior.ConsumePhysicalEvent,
                },
            ]);

        Assert.That(router.RegisterMap(lowerMap).Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(router.RegisterMap(consumerMap).Status, Is.EqualTo(InputMapStatus.Registered));

        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(firstKey, InputKeyEventKind.Down), 1, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(consumedKey, InputKeyEventKind.Down), 2, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        router.BeginHostControlBoundary(0, 3);
        Assert.That(router.SetActiveContextMask(consumerMap.MapId, 1, 1).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(consumedKey, InputKeyEventKind.Up), 4, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(consumedKey, InputKeyEventKind.Down), 5, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(firstKey, InputKeyEventKind.Up), 6, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));

        var snapshot = router.PublishSimulationActionSnapshot(0);
        Assert.That(snapshot.Actions[0].ActionId, Is.EqualTo(lowerAction.ActionId));
        Assert.That(snapshot.Actions[0].IsDownAtEnd, Is.False);
        Assert.That(snapshot.Actions[0].PressedThisTick, Is.True);
        Assert.That(snapshot.Actions[0].ReleasedThisTick, Is.False);
        Assert.That(snapshot.Actions[0].Transitions, Has.Length.EqualTo(2));
        Assert.That(snapshot.Actions[0].Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Pressed));
        Assert.That(snapshot.Actions[0].Transitions[1].Kind, Is.EqualTo(InputTransitionKind.Canceled));
        Assert.That(snapshot.Actions[0].Transitions[1].CancellationReason, Is.EqualTo(InputCancellationReason.InputConsumed));
        Assert.That(snapshot.Actions[1].ActionId, Is.EqualTo(consumerAction.ActionId));
        Assert.That(snapshot.Actions[1].IsDownAtEnd, Is.True);

        router.Dispose();
    }

    [Test]
    public void ConsumesThresholdBindingsOnlyWhileAboveTheirReleaseValue()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            18,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        var lowerAction = new ActionDefinition(Guid.NewGuid(), "Lower", InputActionKind.Digital);
        var consumerAction = new ActionDefinition(Guid.NewGuid(), "Consumer", InputActionKind.Digital);
        var lowerContext = new ContextDefinition(Guid.NewGuid(), 0, priority: 0);
        var consumerContext = new ContextDefinition(Guid.NewGuid(), 0, priority: 10);
        var lowerBinding = new BindingDefinition(Guid.NewGuid(), lowerAction.ActionId, 1, BindingKind.AxisThreshold) {
            ThresholdAxisSource = new GamepadAxis1Source(1, GamepadAxis.RightX, deadzone: 0.0f),
            ThresholdDirection = AxisThresholdDirection.Positive,
            PressThreshold = 0.55f,
            ReleaseThreshold = 0.45f,
        };
        var consumerBinding = new BindingDefinition(Guid.NewGuid(), consumerAction.ActionId, 1, BindingKind.AxisThreshold) {
            ThresholdAxisSource = new GamepadAxis1Source(1, GamepadAxis.RightX, deadzone: 0.0f),
            ThresholdDirection = AxisThresholdDirection.Positive,
            PressThreshold = 0.75f,
            ReleaseThreshold = 0.5f,
            Flags = InputBindingBehavior.ConsumePhysicalEvent,
        };
        var lowerMap = new InputActionMapV1(
            1,
            new Guid("10000000-0000-0000-0000-000000000071"),
            1,
            1,
            [lowerAction],
            [lowerContext],
            [lowerBinding]);
        var consumerMap = new InputActionMapV1(
            1,
            new Guid("10000000-0000-0000-0000-000000000072"),
            1,
            1,
            [consumerAction],
            [consumerContext],
            [consumerBinding]);
        Assert.That(router.RegisterMap(lowerMap).Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(router.RegisterMap(consumerMap).Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(1, GamepadAxis.RightX, 0.6f), 1, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(1, GamepadAxis.RightX, 0.75f), 2, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(1, GamepadAxis.RightX, 0.4f), 3, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(1, GamepadAxis.RightX, 0.6f), 4, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));

        var snapshot = router.PublishSimulationActionSnapshot(0);
        Assert.That(snapshot.Actions[0].ActionId, Is.EqualTo(lowerAction.ActionId));
        Assert.That(snapshot.Actions[0].IsDownAtEnd, Is.True);
        Assert.That(snapshot.Actions[0].ReleasedThisTick, Is.False);
        Assert.That(snapshot.Actions[0].Transitions, Has.Length.EqualTo(3));
        Assert.That(snapshot.Actions[0].Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Pressed));
        Assert.That(snapshot.Actions[0].Transitions[1].Kind, Is.EqualTo(InputTransitionKind.Canceled));
        Assert.That(snapshot.Actions[0].Transitions[1].CancellationReason, Is.EqualTo(InputCancellationReason.InputConsumed));
        Assert.That(snapshot.Actions[0].Transitions[2].Kind, Is.EqualTo(InputTransitionKind.Pressed));
        Assert.That(snapshot.Actions[1].Transitions, Has.Length.EqualTo(2));
        Assert.That(snapshot.Actions[1].Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Pressed));
        Assert.That(snapshot.Actions[1].Transitions[1].Kind, Is.EqualTo(InputTransitionKind.Released));

        router.Dispose();
    }

    [TestCase(BindingKind.GamepadAxis1, InputActionKind.Axis1)]
    [TestCase(BindingKind.GamepadStick2, InputActionKind.Axis2)]
    [TestCase(BindingKind.GamepadTrigger, InputActionKind.Axis1)]
    public void ConsumesTypedAnalogBindingsAcrossSourceKinds(BindingKind bindingKind, InputActionKind actionKind)
    {
        static BindingDefinition CreateBinding(BindingKind kind, Guid actionId, bool fallback)
        {
            return kind switch {
                BindingKind.GamepadAxis1 => new BindingDefinition(Guid.NewGuid(), actionId, 1, kind) {
                    Axis1Source = new GamepadAxis1Source(
                        fallback ? (ushort)2 : (ushort)1,
                        fallback ? GamepadAxis.LeftX : GamepadAxis.RightX,
                        deadzone: 0.0f),
                    Flags = fallback ? InputBindingBehavior.None : InputBindingBehavior.ConsumePhysicalEvent,
                },
                BindingKind.GamepadStick2 => new BindingDefinition(Guid.NewGuid(), actionId, 1, kind) {
                    Stick2Source = new GamepadStick2Source(
                        fallback ? (ushort)2 : (ushort)1,
                        fallback ? GamepadStick.Right : GamepadStick.Left,
                        radialDeadzone: 0.0f),
                    Flags = fallback ? InputBindingBehavior.None : InputBindingBehavior.ConsumePhysicalEvent,
                },
                BindingKind.GamepadTrigger => new BindingDefinition(Guid.NewGuid(), actionId, 1, kind) {
                    TriggerSource = new GamepadTriggerSource(
                        fallback ? (ushort)2 : (ushort)1,
                        fallback ? GamepadTrigger.Left : GamepadTrigger.Right,
                        deadzone: 0.0f),
                    Flags = fallback ? InputBindingBehavior.None : InputBindingBehavior.ConsumePhysicalEvent,
                },
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
        }

        static InputInjection CreateInjection(BindingKind kind, bool fallback, float value)
        {
            return kind switch {
                BindingKind.GamepadAxis1 => new InputInjection(
                    fallback ? (ushort)2 : (ushort)1,
                    fallback ? GamepadAxis.LeftX : GamepadAxis.RightX,
                    value),
                BindingKind.GamepadStick2 => new InputInjection(
                    fallback ? (ushort)2 : (ushort)1,
                    fallback ? GamepadStick.Right : GamepadStick.Left,
                    new Vector2(value, 0.0f)),
                BindingKind.GamepadTrigger => new InputInjection(
                    fallback ? (ushort)2 : (ushort)1,
                    fallback ? GamepadTrigger.Left : GamepadTrigger.Right,
                    value),
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
        }

        var sequenceLease = new HostEventSequenceLease();
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            19,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        var lowerAction = new ActionDefinition(new Guid("20000000-0000-0000-0000-000000000051"), "Lower", actionKind);
        var consumerAction = new ActionDefinition(new Guid("20000000-0000-0000-0000-000000000052"), "Consumer", actionKind);
        var lowerContext = new ContextDefinition(Guid.NewGuid(), 0, priority: 0);
        var consumerContext = new ContextDefinition(Guid.NewGuid(), 0, priority: 10);
        var lowerMap = new InputActionMapV1(
            1,
            new Guid("10000000-0000-0000-0000-000000000051"),
            1,
            1,
            [lowerAction],
            [lowerContext],
            [CreateBinding(bindingKind, lowerAction.ActionId, fallback: true), CreateBinding(bindingKind, lowerAction.ActionId, fallback: false)]);
        var consumerMap = new InputActionMapV1(
            1,
            new Guid("10000000-0000-0000-0000-000000000052"),
            1,
            0,
            [consumerAction],
            [consumerContext],
            [CreateBinding(bindingKind, consumerAction.ActionId, fallback: false)]);

        Assert.That(router.RegisterMap(lowerMap).Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(router.RegisterMap(consumerMap).Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(router.EnqueueHeadlessInjection(CreateInjection(bindingKind, fallback: true, 0.8f), 1, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        router.BeginHostControlBoundary(0, 2);
        Assert.That(router.SetActiveContextMask(consumerMap.MapId, 1, 1).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(CreateInjection(bindingKind, fallback: false, 0.7f), 3, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(CreateInjection(bindingKind, fallback: true, 0.0f), 4, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));

        var snapshot = router.PublishSimulationActionSnapshot(0);
        Assert.That(snapshot.Actions[0].ActionId, Is.EqualTo(lowerAction.ActionId));
        Assert.That(snapshot.Actions[0].IsDownAtEnd, Is.False);
        Assert.That(snapshot.Actions[0].ReleasedThisTick, Is.False);
        Assert.That(snapshot.Actions[0].Transitions, Has.Length.EqualTo(4));
        Assert.That(snapshot.Actions[0].Transitions[0].Kind, Is.EqualTo(InputTransitionKind.ValueChanged));
        Assert.That(snapshot.Actions[0].Transitions[1].Kind, Is.EqualTo(InputTransitionKind.Pressed));
        Assert.That(snapshot.Actions[0].Transitions[2].Kind, Is.EqualTo(InputTransitionKind.ValueChanged));
        Assert.That(snapshot.Actions[0].Transitions[3].Kind, Is.EqualTo(InputTransitionKind.Canceled));
        Assert.That(snapshot.Actions[0].Transitions[3].CancellationReason, Is.EqualTo(InputCancellationReason.InputConsumed));
        Assert.That(snapshot.Actions[1].IsDownAtEnd, Is.True);

        router.Dispose();
    }

    [Test]
    public void DisposeIsIdempotentAndClosesRouterOperations()
    {
        var router = CreateRouter();

        router.Dispose();
        router.Dispose();

        _ = Assert.Throws<ObjectDisposedException>(() => router.GetHostActionSnapshot());
    }

    [Test]
    public void DisposeReturnsRecipientDisposedCancellationsForActiveActions()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            27,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        var action = new ActionDefinition(Guid.NewGuid(), "Move", InputActionKind.Digital);
        var context = new ContextDefinition(Guid.NewGuid(), 0);
        var key = new KeyboardControl(7, 4);
        var binding = new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.Button) {
            Control = new DigitalControl(key),
        };
        var map = new InputActionMapV1(1, Guid.NewGuid(), 1, 1, [action], [context], [binding]);
        Assert.That(router.RegisterMap(map).Status, Is.EqualTo(InputMapStatus.Registered));
        var downSequence = sequenceLease.Allocate();
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(key, InputKeyEventKind.Down), 1, downSequence, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.PublishSimulationActionSnapshot(0).Actions[0].IsDownAtEnd, Is.True);

        var transitions = router.DisposeAndGetTransitions();

        Assert.That(transitions, Has.Length.EqualTo(1));
        Assert.That(transitions[0].ActionId, Is.EqualTo(action.ActionId));
        Assert.That(transitions[0].Kind, Is.EqualTo(InputTransitionKind.Canceled));
        Assert.That(transitions[0].CancellationReason, Is.EqualTo(InputCancellationReason.RecipientDisposed));
        Assert.That(transitions[0].HostSequence, Is.GreaterThan(downSequence));
        Assert.That(transitions[0].AssignedTick, Is.Null);
        Assert.That(router.DisposeAndGetTransitions(), Is.Empty);
    }

    [Test]
    public void DoesNotCoalesceAdjacentAnalogEventsWithDifferentLateMetadata()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            39,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        var action = new ActionDefinition(Guid.NewGuid(), "Move", InputActionKind.Axis1);
        var context = new ContextDefinition(Guid.NewGuid(), 0);
        var map = new InputActionMapV1(
            1,
            Guid.NewGuid(),
            1,
            1,
            [action],
            [context],
            [
                new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.GamepadAxis1) {
                    Axis1Source = new GamepadAxis1Source(1, GamepadAxis.RightX, deadzone: 0.0f),
                },
            ]);
        Assert.That(router.RegisterMap(map).Status, Is.EqualTo(InputMapStatus.Registered));

        var firstSequence = sequenceLease.Allocate();
        var segment = new SimulationClockSegment(
            1,
            0,
            firstSequence,
            0,
            0.0,
            60,
            2,
            1.0f,
            isPaused: false);
        router.SetSimulationClockSegment(in segment);

        var lateReceipt = router.EnqueueHeadlessInjection(
            new InputInjection(1, GamepadAxis.RightX, 0.1f),
            0,
            firstSequence);
        var onTimeSequence = sequenceLease.Allocate();
        var onTimeReceipt = router.EnqueueHeadlessInjection(
            new InputInjection(1, GamepadAxis.RightX, 0.2f),
            500_000,
            onTimeSequence);
        Assert.That(lateReceipt.AssignedTick, Is.EqualTo(2));
        Assert.That(lateReceipt.Late, Is.True);
        Assert.That(onTimeReceipt.AssignedTick, Is.EqualTo(2));
        Assert.That(onTimeReceipt.Late, Is.False);

        var snapshot = router.PublishSimulationActionSnapshot(2);
        Assert.That(snapshot.Actions, Has.Length.EqualTo(1));
        Assert.That(snapshot.Actions[0].Transitions, Has.Length.EqualTo(2));
        Assert.That(snapshot.Actions[0].Transitions[0].HostSequence, Is.EqualTo(firstSequence));
        Assert.That(snapshot.Actions[0].Transitions[1].HostSequence, Is.EqualTo(onTimeSequence));
        Assert.That(snapshot.Actions[0].AxisX, Is.EqualTo(0.2f));

        router.Dispose();
    }

    [Test]
    public void DoesNotCoalesceAnalogEventsAcrossDiscreteOrControlBoundaries()
    {
        static InputRouter CreateAxisRouter(HostEventSequenceLease sequenceLease, Guid mapId, bool withContextControl)
        {
            var router = new InputRouter(
                InputRecipientKind.SimulationTimeline,
                withContextControl ? 31U : 30U,
                Environment.CurrentManagedThreadId,
                sequenceLease);
            var action = new ActionDefinition(Guid.NewGuid(), "Move", InputActionKind.Axis1);
            ImmutableArray<ContextDefinition> contexts = withContextControl
                ? [
                    new ContextDefinition(new Guid("30000000-0000-0000-0000-000000000131"), 0),
                    new ContextDefinition(new Guid("30000000-0000-0000-0000-000000000132"), 1),
                ]
                : [new ContextDefinition(Guid.NewGuid(), 0)];
            var map = new InputActionMapV1(
                1,
                mapId,
                1,
                1,
                [action],
                contexts,
                [
                    new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.GamepadAxis1) {
                        Axis1Source = new GamepadAxis1Source(1, GamepadAxis.RightX, deadzone: 0.0f),
                    },
                ]);
            Assert.That(router.RegisterMap(map).Status, Is.EqualTo(InputMapStatus.Registered));
            return router;
        }

        var discreteSequenceLease = new HostEventSequenceLease();
        var discreteRouter = CreateAxisRouter(
            discreteSequenceLease,
            new Guid("10000000-0000-0000-0000-000000000131"),
            withContextControl: false);
        var firstDiscreteAxisSequence = discreteSequenceLease.Allocate();
        Assert.That(discreteRouter.EnqueueHeadlessInjection(new InputInjection(1, GamepadAxis.RightX, 0.1f), 1, firstDiscreteAxisSequence, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(discreteRouter.EnqueueHeadlessInjection(new InputInjection(new KeyboardControl(7, 4), InputKeyEventKind.Down), 2, discreteSequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        var lastDiscreteAxisSequence = discreteSequenceLease.Allocate();
        Assert.That(discreteRouter.EnqueueHeadlessInjection(new InputInjection(1, GamepadAxis.RightX, 0.8f), 3, lastDiscreteAxisSequence, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));

        var discreteSnapshot = discreteRouter.PublishSimulationActionSnapshot(0);
        Assert.That(discreteSnapshot.Actions[0].Transitions, Has.Length.EqualTo(3));
        Assert.That(discreteSnapshot.Actions[0].Transitions[0].HostSequence, Is.EqualTo(firstDiscreteAxisSequence));
        Assert.That(discreteSnapshot.Actions[0].Transitions[1].HostSequence, Is.EqualTo(lastDiscreteAxisSequence));
        Assert.That(discreteSnapshot.Actions[0].Transitions[2].HostSequence, Is.EqualTo(lastDiscreteAxisSequence));
        discreteRouter.Dispose();

        var controlSequenceLease = new HostEventSequenceLease();
        var controlRouter = CreateAxisRouter(
            controlSequenceLease,
            new Guid("10000000-0000-0000-0000-000000000132"),
            withContextControl: true);
        var firstControlAxisSequence = controlSequenceLease.Allocate();
        Assert.That(controlRouter.EnqueueHeadlessInjection(new InputInjection(1, GamepadAxis.RightX, 0.1f), 1, firstControlAxisSequence, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        controlRouter.BeginHostControlBoundary(0, 2);
        Assert.That(controlRouter.SetActiveContextMask(new Guid("10000000-0000-0000-0000-000000000132"), 1, 3).Status, Is.EqualTo(InputRoutingStatus.Applied));
        var lastControlAxisSequence = controlSequenceLease.Allocate();
        Assert.That(controlRouter.EnqueueHeadlessInjection(new InputInjection(1, GamepadAxis.RightX, 0.8f), 3, lastControlAxisSequence, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));

        var controlSnapshot = controlRouter.PublishSimulationActionSnapshot(0);
        Assert.That(controlSnapshot.Actions[0].Transitions, Has.Length.EqualTo(3));
        Assert.That(controlSnapshot.Actions[0].Transitions[0].HostSequence, Is.EqualTo(firstControlAxisSequence));
        Assert.That(controlSnapshot.Actions[0].Transitions[1].HostSequence, Is.EqualTo(lastControlAxisSequence));
        Assert.That(controlSnapshot.Actions[0].Transitions[2].HostSequence, Is.EqualTo(lastControlAxisSequence));
        controlRouter.Dispose();
    }

    [Test]
    public void EnforcesOwnerThreadForMapMutations()
    {
        var router = CreateRouter();
        var map = CreateMap(Guid.NewGuid(), 1);
        var task = Task.Run(() => router.RegisterMap(map));

        _ = Assert.ThrowsAsync<InvalidOperationException>(async () => await task.ConfigureAwait(false));
        router.Dispose();
    }

    [Test]
    public void LatchesOverflowUntilASequencedHeadlessReconciliation()
    {
        var router = CreateRouter(InputRecipientKind.SimulationTimeline, recipientId: 5);
        var action = new ActionDefinition(Guid.NewGuid(), "Move", InputActionKind.Digital);
        var context = new ContextDefinition(Guid.NewGuid(), 0);
        var binding = new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.Button) {
            Control = new DigitalControl(new KeyboardControl(7, 4)),
        };
        var map = new InputActionMapV1(1, Guid.NewGuid(), 1, 1, [action], [context], [binding]);
        var keyboard = new KeyboardControl(7, 4);
        var keyDown = new InputInjection(keyboard, InputKeyEventKind.Down);

        var mapResponse = router.RegisterMap(map);
        Assert.That(mapResponse.Status, Is.EqualTo(InputMapStatus.Registered));
        var firstCaptureSequence = mapResponse.HostSequence.GetValueOrDefault() + 1;
        Assert.That(router.EnqueueHeadlessInjection(keyDown, 1, firstCaptureSequence, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.PublishSimulationActionSnapshot(0).Actions[0].IsDownAtEnd, Is.True);

        for (var sequence = firstCaptureSequence + 1; sequence <= firstCaptureSequence + 4096; sequence++)
        {
            var repeat = new InputInjection(keyboard, InputKeyEventKind.Repeat);
            Assert.That(
                router.EnqueueHeadlessInjection(repeat, sequence, sequence, 1).Status,
                Is.EqualTo(InputRoutingStatus.Applied));
        }

        var overflowSequence = firstCaptureSequence + 4097;
        var overflow = router.EnqueueHeadlessInjection(
            new InputInjection(keyboard, InputKeyEventKind.Repeat),
            overflowSequence,
            overflowSequence,
            1);
        Assert.That(overflow.Status, Is.EqualTo(InputRoutingStatus.CapacityExceeded));
        Assert.That(overflow.RecipientId, Is.EqualTo(5));

        var overflowResult = router.ServiceHostControlBoundary();
        Assert.That(overflowResult, Is.Not.Null);
        Assert.That(overflowResult!.FirstUnacceptedHostSequence, Is.EqualTo(overflowSequence));
        Assert.That(overflowResult.LastUnacceptedHostSequence, Is.EqualTo(overflowSequence));
        Assert.That(overflowResult.Transitions, Has.Length.EqualTo(1));
        Assert.That(overflowResult.Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Canceled));
        Assert.That(overflowResult.Transitions[0].CancellationReason, Is.EqualTo(InputCancellationReason.InputOverflow));
        Assert.That(overflowResult.Transitions[0].HostSequence, Is.EqualTo(overflowSequence));
        Assert.That(overflowResult.Transitions[0].AssignedTick, Is.Null);

        var reconciliationSequence = overflowSequence + 1;
        var reconciliation = router.ReconcileHeadlessInputState([], reconciliationSequence, reconciliationSequence);
        Assert.That(reconciliation.Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(reconciliation.AssignedTick, Is.Null);

        Assert.That(router.EnqueueHeadlessInjection(keyDown, reconciliationSequence + 1, reconciliationSequence + 1, 1).Status, Is.EqualTo(InputRoutingStatus.Applied));
        var recoveredTick = router.PublishSimulationActionSnapshot(1);
        Assert.That(recoveredTick.Actions[0].IsDownAtEnd, Is.True);
        Assert.That(recoveredTick.Actions[0].Transitions, Has.Length.EqualTo(1));
        Assert.That(recoveredTick.Actions[0].Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Pressed));

        router.Dispose();
    }

    [Test]
    public void OrdersContextActivationsAcrossCancellationControls()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            28,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        var action = new ActionDefinition(new Guid("20000000-0000-0000-0000-000000000121"), "Move", InputActionKind.Digital);
        var lowContext = new ContextDefinition(new Guid("30000000-0000-0000-0000-000000000121"), 0);
        var key = new KeyboardControl(7, 4);
        var lowMap = new InputActionMapV1(
            1,
            new Guid("10000000-0000-0000-0000-000000000122"),
            1,
            1,
            [action],
            [lowContext],
            [
                new BindingDefinition(new Guid("40000000-0000-0000-0000-000000000121"), action.ActionId, 1, BindingKind.Button) {
                    Control = new DigitalControl(key),
                },
            ]);
        var middleMap = new InputActionMapV1(
            1,
            new Guid("10000000-0000-0000-0000-000000000121"),
            1,
            0,
            [],
            [new ContextDefinition(new Guid("30000000-0000-0000-0000-000000000122"), 0)],
            []);
        var highestMap = new InputActionMapV1(
            1,
            new Guid("10000000-0000-0000-0000-000000000123"),
            1,
            0,
            [],
            [new ContextDefinition(
                new Guid("30000000-0000-0000-0000-000000000123"),
                0,
                captureClasses: InputClass.Keyboard)],
            []);

        Assert.That(router.RegisterMap(lowMap).Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(router.RegisterMap(middleMap).Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(router.RegisterMap(highestMap).Status, Is.EqualTo(InputMapStatus.Registered));
        router.BeginHostControlBoundary(0, 1);
        Assert.That(router.SetActiveContextMask(middleMap.MapId, 1, 1).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(
            router.CancelInputState(
                InputCancellationReason.DeviceRemoved,
                InputClass.Gamepad,
                playerSlot: 1,
                hostTimestamp: 1,
                hostSequence: sequenceLease.Allocate(),
                out _).Status,
            Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.SetActiveContextMask(highestMap.MapId, 1, 1).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.ServiceInputControlBoundary(), Is.Empty);

        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(key, InputKeyEventKind.Down), 2, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        var snapshot = router.PublishSimulationActionSnapshot(0);
        Assert.That(snapshot.Actions, Has.Length.EqualTo(1));
        Assert.That(snapshot.Actions[0].IsDownAtEnd, Is.False);
        Assert.That(snapshot.Actions[0].Transitions, Is.Empty);

        router.Dispose();
    }

    [Test]
    public void OrdersContextMaskChangesWithPhysicalInputByTimestampAndSequence()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            12,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        var action = new ActionDefinition(Guid.NewGuid(), "Move", InputActionKind.Digital);
        var lowerContext = new ContextDefinition(Guid.NewGuid(), 0);
        var otherContext = new ContextDefinition(Guid.NewGuid(), 1);
        var key = new KeyboardControl(7, 4);
        var binding = new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.Button) {
            Control = new DigitalControl(key),
        };
        var map = new InputActionMapV1(
            1,
            Guid.NewGuid(),
            1,
            1,
            [action],
            [lowerContext, otherContext],
            [binding]);

        Assert.That(router.RegisterMap(map).Status, Is.EqualTo(InputMapStatus.Registered));
        router.BeginHostControlBoundary(0, 100);
        var downSequence = sequenceLease.Allocate();
        Assert.That(
            router.EnqueueHeadlessInjection(
                new InputInjection(key, InputKeyEventKind.Down),
                100,
                downSequence,
                0).Status,
            Is.EqualTo(InputRoutingStatus.Applied));

        var contextChange = router.SetActiveContextMask(map.MapId, 1, 2);
        Assert.That(contextChange.Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(contextChange.HostSequence, Is.GreaterThan(downSequence));
        Assert.That(contextChange.AssignedTick, Is.Zero);

        var snapshot = router.PublishSimulationActionSnapshot(0);
        Assert.That(snapshot.Actions[0].IsDownAtEnd, Is.False);
        Assert.That(snapshot.Actions[0].PressedThisTick, Is.True);
        Assert.That(snapshot.Actions[0].ReleasedThisTick, Is.False);
        Assert.That(snapshot.Actions[0].Transitions, Has.Length.EqualTo(2));
        Assert.That(snapshot.Actions[0].Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Pressed));
        Assert.That(snapshot.Actions[0].Transitions[0].HostSequence, Is.EqualTo(downSequence));
        Assert.That(snapshot.Actions[0].Transitions[1].Kind, Is.EqualTo(InputTransitionKind.Canceled));
        Assert.That(snapshot.Actions[0].Transitions[1].CancellationReason, Is.EqualTo(InputCancellationReason.ContextMasked));
        Assert.That(snapshot.Actions[0].Transitions[1].HostSequence, Is.EqualTo(contextChange.HostSequence));

        router.BeginHostControlBoundary(1, 101);
        var reactivate = router.SetActiveContextMask(map.MapId, 1, 1);
        Assert.That(reactivate.Status, Is.EqualTo(InputRoutingStatus.Applied));
        var stillHeld = router.PublishSimulationActionSnapshot(1);
        Assert.That(stillHeld.Actions[0].IsDownAtEnd, Is.False);
        Assert.That(stillHeld.Actions[0].Transitions, Is.Empty);

        var upSequence = sequenceLease.Allocate();
        Assert.That(
            router.EnqueueHeadlessInjection(
                new InputInjection(key, InputKeyEventKind.Up),
                102,
                upSequence,
                2).Status,
            Is.EqualTo(InputRoutingStatus.Applied));
        var released = router.PublishSimulationActionSnapshot(2);
        Assert.That(released.Actions[0].IsDownAtEnd, Is.False);
        Assert.That(released.Actions[0].Transitions, Is.Empty);

        var repressSequence = sequenceLease.Allocate();
        Assert.That(
            router.EnqueueHeadlessInjection(
                new InputInjection(key, InputKeyEventKind.Down),
                103,
                repressSequence,
                3).Status,
            Is.EqualTo(InputRoutingStatus.Applied));
        var repressed = router.PublishSimulationActionSnapshot(3);
        Assert.That(repressed.Actions[0].IsDownAtEnd, Is.True);
        Assert.That(repressed.Actions[0].PressedThisTick, Is.True);

        router.Dispose();
    }

    [Test]
    public void OrdersEqualPriorityContextsByRecentActivation()
    {
        var router = CreateRouter(InputRecipientKind.SimulationTimeline, recipientId: 15);
        var action = new ActionDefinition(Guid.NewGuid(), "Move", InputActionKind.Digital);
        var olderContext = new ContextDefinition(
            new Guid("10000000-0000-0000-0000-000000000001"),
            0,
            priority: 5);
        var newerContext = new ContextDefinition(
            new Guid("20000000-0000-0000-0000-000000000001"),
            1,
            priority: 5,
            captureClasses: InputClass.Keyboard);
        var key = new KeyboardControl(7, 4);
        var binding = new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.Button) {
            Control = new DigitalControl(key),
        };
        var map = new InputActionMapV1(
            1,
            Guid.NewGuid(),
            1,
            3,
            [action],
            [olderContext, newerContext],
            [binding]);
        var registration = router.RegisterMap(map);
        Assert.That(registration.Status, Is.EqualTo(InputMapStatus.Registered));
        var sequence = registration.HostSequence.GetValueOrDefault() + 1;

        Assert.That(
            router.EnqueueHeadlessInjection(new InputInjection(key, InputKeyEventKind.Down), 1, sequence, 0).Status,
            Is.EqualTo(InputRoutingStatus.Applied));

        var snapshot = router.PublishSimulationActionSnapshot(0);
        Assert.That(snapshot.Actions[0].IsDownAtEnd, Is.False);
        Assert.That(snapshot.Actions[0].Transitions, Is.Empty);

        router.Dispose();
    }

    [Test]
    public void PausesAndReconcilesTimelineInputWithoutSyntheticPress()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            26,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        var action = new ActionDefinition(Guid.NewGuid(), "Move", InputActionKind.Digital);
        var context = new ContextDefinition(Guid.NewGuid(), 0);
        var key = new KeyboardControl(7, 4);
        var binding = new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.Button) {
            Control = new DigitalControl(key),
        };
        var map = new InputActionMapV1(1, Guid.NewGuid(), 1, 1, [action], [context], [binding]);

        Assert.That(router.RegisterMap(map).Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(key, InputKeyEventKind.Down), 1, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.PublishSimulationActionSnapshot(0).Actions[0].IsDownAtEnd, Is.True);

        router.BeginHostControlBoundary(1, 2);
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(key, InputKeyEventKind.Down), 2, sequenceLease.Allocate(), 1).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(1, GamepadButtonValue.South, true), 2, sequenceLease.Allocate(), 1).Status, Is.EqualTo(InputRoutingStatus.Applied));
        var pause = router.CancelInputState(
            InputCancellationReason.Paused,
            InputClass.Keyboard | InputClass.Pointing | InputClass.Gamepad,
            playerSlot: null,
            hostTimestamp: 2,
            hostSequence: sequenceLease.Allocate(),
            out var discardedInputCount);
        Assert.That(pause.Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(discardedInputCount, Is.EqualTo(2));

        var rejectedWhilePaused = router.EnqueueHeadlessInjection(
            new InputInjection(key, InputKeyEventKind.Up),
            3,
            sequenceLease.Allocate(),
            1);
        Assert.That(rejectedWhilePaused.Status, Is.EqualTo(InputRoutingStatus.Rejected));
        Assert.That(rejectedWhilePaused.Rejection?.Code, Is.EqualTo("Paused"));

        var pauseTransitions = router.ServiceInputControlBoundary();
        Assert.That(pauseTransitions, Has.Length.EqualTo(1));
        Assert.That(pauseTransitions[0].Kind, Is.EqualTo(InputTransitionKind.Canceled));
        Assert.That(pauseTransitions[0].CancellationReason, Is.EqualTo(InputCancellationReason.Paused));

        var resumed = router.ReconcileInputState(
            [new InputInjection(key, InputKeyEventKind.Down)],
            InputClass.Keyboard | InputClass.Pointing | InputClass.Gamepad,
            playerSlot: null,
            hostTimestamp: 3,
            hostSequence: sequenceLease.Allocate(),
            out discardedInputCount);
        Assert.That(resumed.Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(discardedInputCount, Is.Zero);
        Assert.That(router.ServiceInputControlBoundary(), Is.Empty);

        var heldSnapshot = router.PublishSimulationActionSnapshot(1);
        Assert.That(heldSnapshot.Actions[0].IsDownAtEnd, Is.False);
        Assert.That(heldSnapshot.Actions[0].Transitions, Is.Empty);

        router.BeginHostControlBoundary(2, 4);
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(key, InputKeyEventKind.Up), 4, sequenceLease.Allocate(), 2).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.PublishSimulationActionSnapshot(2).Actions[0].Transitions, Is.Empty);
        router.BeginHostControlBoundary(3, 5);
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(key, InputKeyEventKind.Down), 5, sequenceLease.Allocate(), 3).Status, Is.EqualTo(InputRoutingStatus.Applied));
        var rearmed = router.PublishSimulationActionSnapshot(3).Actions[0];
        Assert.That(rearmed.IsDownAtEnd, Is.True);
        Assert.That(rearmed.Transitions, Has.Length.EqualTo(1));
        Assert.That(rearmed.Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Pressed));

        router.Dispose();
    }

    [Test]
    public void ProcessesAnalogBindingsAndThresholdHysteresis()
    {
        var router = CreateRouter(InputRecipientKind.SimulationTimeline, recipientId: 6);
        var axis1Action = new ActionDefinition(new Guid("20000000-0000-0000-0000-000000000001"), "Axis", InputActionKind.Axis1);
        var axis2Action = new ActionDefinition(new Guid("20000000-0000-0000-0000-000000000002"), "Stick", InputActionKind.Axis2);
        var triggerAction = new ActionDefinition(new Guid("20000000-0000-0000-0000-000000000003"), "Trigger", InputActionKind.Axis1);
        var thresholdAction = new ActionDefinition(new Guid("20000000-0000-0000-0000-000000000004"), "Threshold", InputActionKind.Digital);
        var context = new ContextDefinition(Guid.NewGuid(), 0);
        var map = new InputActionMapV1(
            1,
            Guid.NewGuid(),
            1,
            1,
            [axis1Action, axis2Action, triggerAction, thresholdAction],
            [context],
            [
                new BindingDefinition(Guid.NewGuid(), axis1Action.ActionId, 1, BindingKind.GamepadAxis1) {
                    Axis1Source = new GamepadAxis1Source(1, GamepadAxis.LeftX, deadzone: 0.15f),
                },
                new BindingDefinition(Guid.NewGuid(), axis2Action.ActionId, 1, BindingKind.GamepadStick2) {
                    Stick2Source = new GamepadStick2Source(2, GamepadStick.Left, radialDeadzone: 0.15f),
                },
                new BindingDefinition(Guid.NewGuid(), triggerAction.ActionId, 1, BindingKind.GamepadTrigger) {
                    TriggerSource = new GamepadTriggerSource(3, GamepadTrigger.Right, deadzone: 0.05f),
                },
                new BindingDefinition(Guid.NewGuid(), thresholdAction.ActionId, 1, BindingKind.AxisThreshold) {
                    ThresholdAxisSource = new GamepadAxis1Source(4, GamepadAxis.LeftX, deadzone: 0.0f),
                    ThresholdDirection = AxisThresholdDirection.Positive,
                    PressThreshold = 0.55f,
                    ReleaseThreshold = 0.45f,
                },
            ]);
        var registration = router.RegisterMap(map);
        Assert.That(registration.Status, Is.EqualTo(InputMapStatus.Registered));
        var sequence = registration.HostSequence.GetValueOrDefault() + 1;

        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(1, GamepadAxis.LeftX, 0.575f), 1, sequence++, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(2, GamepadStick.Left, new Vector2(0.575f, 0.0f)), 2, sequence++, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(3, GamepadTrigger.Right, 0.525f), 3, sequence++, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(4, GamepadAxis.LeftX, 0.55f), 4, sequence++, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(4, GamepadAxis.LeftX, 0.50f), 5, sequence++, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(4, GamepadAxis.LeftX, 0.45f), 6, sequence, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));

        var snapshot = router.PublishSimulationActionSnapshot(0);

        Assert.That(snapshot.Actions, Has.Length.EqualTo(4));
        Assert.That(snapshot.Actions[0].ActionId, Is.EqualTo(axis1Action.ActionId));
        Assert.That(snapshot.Actions[0].AxisX, Is.EqualTo(0.5f).Within(1e-6f));
        Assert.That(snapshot.Actions[0].IsDownAtEnd, Is.False);
        Assert.That(snapshot.Actions[0].ValueChangedThisTick, Is.True);
        Assert.That(snapshot.Actions[1].ActionId, Is.EqualTo(axis2Action.ActionId));
        Assert.That(snapshot.Actions[1].AxisX, Is.EqualTo(0.5f).Within(1e-6f));
        Assert.That(snapshot.Actions[1].AxisY, Is.Zero);
        Assert.That(snapshot.Actions[2].ActionId, Is.EqualTo(triggerAction.ActionId));
        Assert.That(snapshot.Actions[2].AxisX, Is.EqualTo(0.5f).Within(1e-6f));
        Assert.That(snapshot.Actions[3].ActionId, Is.EqualTo(thresholdAction.ActionId));
        Assert.That(snapshot.Actions[3].IsDownAtEnd, Is.False);
        Assert.That(snapshot.Actions[3].PressedThisTick, Is.True);
        Assert.That(snapshot.Actions[3].ReleasedThisTick, Is.True);
        Assert.That(snapshot.Actions[3].Transitions, Has.Length.EqualTo(2));
        Assert.That(snapshot.Actions[3].Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Pressed));
        Assert.That(snapshot.Actions[3].Transitions[1].Kind, Is.EqualTo(InputTransitionKind.Released));

        router.Dispose();
    }

    [Test]
    public void ProcessesAxisKeyBindingsAndNormalizesDiagonalVectors()
    {
        var router = CreateRouter(InputRecipientKind.SimulationTimeline, recipientId: 9);
        var axis1Action = new ActionDefinition(new Guid("20000000-0000-0000-0000-000000000011"), "Axis1", InputActionKind.Axis1);
        var axis2Action = new ActionDefinition(new Guid("20000000-0000-0000-0000-000000000012"), "Axis2", InputActionKind.Axis2);
        var context = new ContextDefinition(Guid.NewGuid(), 0);
        var left = new KeyboardControl(7, 4);
        var right = new KeyboardControl(7, 7);
        var down = new KeyboardControl(7, 22);
        var up = new KeyboardControl(7, 26);
        var map = new InputActionMapV1(
            1,
            Guid.NewGuid(),
            1,
            1,
            [axis1Action, axis2Action],
            [context],
            [
                new BindingDefinition(Guid.NewGuid(), axis1Action.ActionId, 1, BindingKind.Axis1Keys) {
                    Negative = left,
                    Positive = right,
                },
                new BindingDefinition(Guid.NewGuid(), axis2Action.ActionId, 1, BindingKind.Axis2Keys) {
                    Left = left,
                    Right = right,
                    Down = down,
                    Up = up,
                },
            ]);
        var registration = router.RegisterMap(map);
        Assert.That(registration.Status, Is.EqualTo(InputMapStatus.Registered));
        var sequence = registration.HostSequence.GetValueOrDefault() + 1;

        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(right, InputKeyEventKind.Down), 1, sequence++, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(up, InputKeyEventKind.Down), 2, sequence++, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));

        var diagonal = router.PublishSimulationActionSnapshot(0);
        Assert.That(diagonal.Actions[0].ActionId, Is.EqualTo(axis1Action.ActionId));
        Assert.That(diagonal.Actions[0].AxisX, Is.EqualTo(1.0f));
        Assert.That(diagonal.Actions[1].ActionId, Is.EqualTo(axis2Action.ActionId));
        Assert.That(diagonal.Actions[1].AxisX, Is.EqualTo(1.0f / MathF.Sqrt(2.0f)).Within(1e-6f));
        Assert.That(diagonal.Actions[1].AxisY, Is.EqualTo(1.0f / MathF.Sqrt(2.0f)).Within(1e-6f));

        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(left, InputKeyEventKind.Down), 3, sequence, 1).Status, Is.EqualTo(InputRoutingStatus.Applied));
        var opposingKeys = router.PublishSimulationActionSnapshot(1);
        Assert.That(opposingKeys.Actions[0].AxisX, Is.Zero);
        Assert.That(opposingKeys.Actions[0].ReleasedThisTick, Is.True);
        Assert.That(opposingKeys.Actions[0].Transitions[0].Kind, Is.EqualTo(InputTransitionKind.ValueChanged));
        Assert.That(opposingKeys.Actions[0].Transitions[1].Kind, Is.EqualTo(InputTransitionKind.Released));
        Assert.That(opposingKeys.Actions[1].AxisX, Is.Zero);
        Assert.That(opposingKeys.Actions[1].AxisY, Is.EqualTo(1.0f));
        Assert.That(opposingKeys.Actions[1].IsDownAtEnd, Is.True);

        router.Dispose();
    }

    [Test]
    public void ProcessesChordsAtTheInclusiveCompletionBoundary()
    {
        var router = CreateRouter(InputRecipientKind.SimulationTimeline, recipientId: 7);
        var action = new ActionDefinition(Guid.NewGuid(), "Chord", InputActionKind.Digital);
        var context = new ContextDefinition(Guid.NewGuid(), 0);
        var firstKey = new KeyboardControl(7, 4);
        var secondKey = new KeyboardControl(7, 5);
        var binding = new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.Chord) {
            Controls = [new DigitalControl(firstKey), new DigitalControl(secondKey)],
            CompletionWindowMilliseconds = 150,
        };
        var map = new InputActionMapV1(1, Guid.NewGuid(), 1, 1, [action], [context], [binding]);
        var registration = router.RegisterMap(map);
        Assert.That(registration.Status, Is.EqualTo(InputMapStatus.Registered));
        var sequence = registration.HostSequence.GetValueOrDefault() + 1;
        var startTimestamp = 100L;
        var deadlineTimestamp = startTimestamp + (150 * TimeSpan.TicksPerMillisecond);

        Assert.That(
            router.EnqueueHeadlessInjection(new InputInjection(firstKey, InputKeyEventKind.Down), startTimestamp, sequence++, 0).Status,
            Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(
            router.EnqueueHeadlessInjection(new InputInjection(secondKey, InputKeyEventKind.Down), deadlineTimestamp, sequence++, 0).Status,
            Is.EqualTo(InputRoutingStatus.Applied));

        var completed = router.PublishSimulationActionSnapshot(0);
        Assert.That(completed.Actions[0].IsDownAtEnd, Is.True);
        Assert.That(completed.Actions[0].PressedThisTick, Is.True);
        Assert.That(completed.Actions[0].Transitions, Has.Length.EqualTo(1));
        Assert.That(completed.Actions[0].Transitions[0].HostSequence, Is.EqualTo(sequence - 1));

        Assert.That(
            router.EnqueueHeadlessInjection(new InputInjection(firstKey, InputKeyEventKind.Up), deadlineTimestamp + 1, sequence, 1).Status,
            Is.EqualTo(InputRoutingStatus.Applied));
        var released = router.PublishSimulationActionSnapshot(1);
        Assert.That(released.Actions[0].IsDownAtEnd, Is.False);
        Assert.That(released.Actions[0].ReleasedThisTick, Is.True);
        Assert.That(released.Actions[0].Transitions, Has.Length.EqualTo(1));
        Assert.That(released.Actions[0].Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Released));

        router.Dispose();
    }

    [Test]
    public void RegistersMapsAndKeepsPreviouslyReturnedSnapshotsImmutable()
    {
        var router = CreateRouter();
        var firstMapId = Guid.NewGuid();
        var firstAction = new ActionDefinition(Guid.NewGuid(), "First", InputActionKind.Digital);
        var response = router.RegisterMap(CreateMap(firstMapId, 1, firstAction));

        Assert.That(response.Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(response.HostSequence, Is.GreaterThan(0));
        Assert.That(response.PublishedRevision, Is.EqualTo(1));

        var firstSnapshot = router.GetHostActionSnapshot();
        Assert.That(firstSnapshot.Actions, Has.Length.EqualTo(1));
        Assert.That(firstSnapshot.Actions[0].MapId, Is.EqualTo(firstMapId));
        Assert.That(firstSnapshot.Actions[0].ActionId, Is.EqualTo(firstAction.ActionId));
        Assert.That(firstSnapshot.Actions[0].IsDownAtEnd, Is.False);
        Assert.That(firstSnapshot.Actions[0].Transitions, Is.Empty);

        var secondAction = new ActionDefinition(Guid.NewGuid(), "Second", InputActionKind.Axis1);
        var secondResponse = router.RegisterMap(CreateMap(Guid.NewGuid(), 1, secondAction));
        var secondSnapshot = router.GetHostActionSnapshot();

        Assert.That(secondResponse.Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(secondSnapshot.Actions, Has.Length.EqualTo(2));
        Assert.That(firstSnapshot.Actions, Has.Length.EqualTo(1));
        router.Dispose();
    }

    [Test]
    public void RejectsExpiredChordUntilEveryMemberIsReleased()
    {
        var router = CreateRouter(InputRecipientKind.SimulationTimeline, recipientId: 11);
        var action = new ActionDefinition(Guid.NewGuid(), "Chord", InputActionKind.Digital);
        var context = new ContextDefinition(Guid.NewGuid(), 0);
        var firstKey = new KeyboardControl(7, 4);
        var secondKey = new KeyboardControl(7, 5);
        var binding = new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.Chord) {
            Controls = [new DigitalControl(firstKey), new DigitalControl(secondKey)],
            CompletionWindowMilliseconds = 1,
        };
        var map = new InputActionMapV1(1, Guid.NewGuid(), 1, 1, [action], [context], [binding]);
        var registration = router.RegisterMap(map);
        Assert.That(registration.Status, Is.EqualTo(InputMapStatus.Registered));
        var sequence = registration.HostSequence.GetValueOrDefault() + 1;
        var firstTimestamp = 100L;
        var expiredTimestamp = firstTimestamp + TimeSpan.TicksPerMillisecond + 1;

        Assert.That(
            router.EnqueueHeadlessInjection(new InputInjection(firstKey, InputKeyEventKind.Down), firstTimestamp, sequence++, 0).Status,
            Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(
            router.EnqueueHeadlessInjection(new InputInjection(secondKey, InputKeyEventKind.Down), expiredTimestamp, sequence++, 0).Status,
            Is.EqualTo(InputRoutingStatus.Applied));

        var expired = router.PublishSimulationActionSnapshot(0);
        Assert.That(expired.Actions[0].IsDownAtEnd, Is.False);
        Assert.That(expired.Actions[0].Transitions, Is.Empty);

        Assert.That(
            router.EnqueueHeadlessInjection(new InputInjection(firstKey, InputKeyEventKind.Up), expiredTimestamp + 1, sequence++, 1).Status,
            Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(
            router.EnqueueHeadlessInjection(new InputInjection(secondKey, InputKeyEventKind.Up), expiredTimestamp + 2, sequence++, 1).Status,
            Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(
            router.EnqueueHeadlessInjection(new InputInjection(firstKey, InputKeyEventKind.Down), expiredTimestamp + 3, sequence++, 1).Status,
            Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(
            router.EnqueueHeadlessInjection(
                new InputInjection(secondKey, InputKeyEventKind.Down),
                expiredTimestamp + 3 + TimeSpan.TicksPerMillisecond,
                sequence,
                1).Status,
            Is.EqualTo(InputRoutingStatus.Applied));

        var rearmed = router.PublishSimulationActionSnapshot(1);
        Assert.That(rearmed.Actions[0].IsDownAtEnd, Is.True);
        Assert.That(rearmed.Actions[0].PressedThisTick, Is.True);
        Assert.That(rearmed.Actions[0].Transitions, Has.Length.EqualTo(1));

        router.Dispose();
    }

    [Test]
    public void RejectsInvalidDuplicateAndOverCapacityMapsWithoutSequence()
    {
        var router = CreateRouter();
        var mapId = Guid.NewGuid();
        var invalidMap = new InputActionMapV1(
            2,
            mapId,
            1,
            1,
            [],
            [new ContextDefinition(Guid.NewGuid(), 0)],
            []);

        var invalidResponse = router.RegisterMap(invalidMap);
        Assert.That(invalidResponse.Status, Is.EqualTo(InputMapStatus.Rejected));
        Assert.That(invalidResponse.HostSequence, Is.Null);
        Assert.That(invalidResponse.Rejection?.Code, Is.EqualTo("UnknownSchemaVersion"));

        var firstResponse = router.RegisterMap(CreateMap(mapId, 1));
        var duplicateResponse = router.RegisterMap(CreateMap(mapId, 2));
        Assert.That(duplicateResponse.Status, Is.EqualTo(InputMapStatus.Rejected));
        Assert.That(duplicateResponse.HostSequence, Is.Null);
        Assert.That(duplicateResponse.Rejection?.Code, Is.EqualTo("DuplicateId"));

        for (var index = 1; index < 16; index++)
        {
            Assert.That(router.RegisterMap(CreateMap(Guid.NewGuid(), 1)).Status, Is.EqualTo(InputMapStatus.Registered));
        }

        var fullResponse = router.RegisterMap(CreateMap(Guid.NewGuid(), 1));
        Assert.That(fullResponse.Status, Is.EqualTo(InputMapStatus.CapacityExceeded));
        Assert.That(fullResponse.HostSequence, Is.Null);
        Assert.That(firstResponse.HostSequence, Is.GreaterThan(0));
        router.Dispose();
    }

    [Test]
    public void RejectsMapThatWouldExceedRouterWideActiveContextCapacity()
    {
        var router = CreateRouter();

        for (var mapIndex = 0; mapIndex < 4; mapIndex++)
        {
            var contexts = ImmutableArray.CreateBuilder<ContextDefinition>(64);

            for (var bit = 0; bit < 64; bit++)
            {
                contexts.Add(new ContextDefinition(Guid.NewGuid(), checked((byte)bit)));
            }

            var map = CreateMapWithContexts(Guid.NewGuid(), 1, contexts.MoveToImmutable(), ulong.MaxValue);
            Assert.That(router.RegisterMap(map).Status, Is.EqualTo(InputMapStatus.Registered));
        }

        var overCapacityContexts = ImmutableArray.CreateBuilder<ContextDefinition>(64);

        for (var bit = 0; bit < 64; bit++)
        {
            overCapacityContexts.Add(new ContextDefinition(Guid.NewGuid(), checked((byte)bit)));
        }

        var overCapacityMap = CreateMapWithContexts(Guid.NewGuid(), 1, overCapacityContexts.MoveToImmutable(), ulong.MaxValue);
        var response = router.RegisterMap(overCapacityMap);

        Assert.That(response.Status, Is.EqualTo(InputMapStatus.CapacityExceeded));
        Assert.That(response.HostSequence, Is.Null);
        router.Dispose();
    }

    [Test]
    public void RejectsPhysicalEventsWithInvalidRecipientSlotAssignments()
    {
        var router = CreateRouter(InputRecipientKind.SimulationTimeline, recipientId: 51);
        var gamepadEvent = new PhysicalInputEvent(
            PhysicalInputEventKind.GamepadButtonDown,
            0,
            1,
            device: new InputDeviceHandle(1, 0, 1),
            value: new PhysicalInputValue(GamepadButtonValue.South, isDown: true));
        var gamepadReceipt = router.EnqueuePhysicalEvent(gamepadEvent, playerSlot: null, discardedInputCount: out _);
        Assert.That(gamepadReceipt.Status, Is.EqualTo(InputRoutingStatus.Rejected));
        Assert.That(gamepadReceipt.Rejection?.Code, Is.EqualTo("UnassignedDevice"));
        Assert.That(gamepadReceipt.HostSequence, Is.Null);

        var keyEvent = new PhysicalInputEvent(
            PhysicalInputEventKind.KeyDown,
            0,
            1,
            device: new InputDeviceHandle(1, 0, 1),
            window: new WindowHandle(1, 0, 1),
            value: new PhysicalInputValue(
                new DigitalControl(new KeyboardControl(7, 4)),
                isDown: true));
        var keyReceipt = router.EnqueuePhysicalEvent(keyEvent, playerSlot: 3, discardedInputCount: out _);
        Assert.That(keyReceipt.Status, Is.EqualTo(InputRoutingStatus.Rejected));
        Assert.That(keyReceipt.Rejection?.Code, Is.EqualTo("InvalidPlayerSlot"));
        Assert.That(keyReceipt.HostSequence, Is.Null);

        router.Dispose();
    }

    [Test]
    public void ReplacesActiveActionWithReboundCancellationAndRearmsHeldControls()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            20,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        var mapId = new Guid("10000000-0000-0000-0000-000000000061");
        var actionId = new Guid("20000000-0000-0000-0000-000000000061");
        var context = new ContextDefinition(new Guid("30000000-0000-0000-0000-000000000061"), 0);
        var key = new KeyboardControl(7, 4);
        var originalAction = new ActionDefinition(actionId, "Move", InputActionKind.Digital);
        var originalBinding = new BindingDefinition(new Guid("40000000-0000-0000-0000-000000000061"), actionId, 1, BindingKind.Button) {
            Control = new DigitalControl(key),
        };
        var originalMap = new InputActionMapV1(1, mapId, 1, 1, [originalAction], [context], [originalBinding]);
        var replacementAction = new ActionDefinition(actionId, "Move", InputActionKind.Digital);
        var replacementBinding = new BindingDefinition(new Guid("40000000-0000-0000-0000-000000000062"), actionId, 1, BindingKind.Button) {
            Control = new DigitalControl(key),
        };
        var replacementMap = new InputActionMapV1(1, mapId, 2, 1, [replacementAction], [context], [replacementBinding]);

        Assert.That(router.RegisterMap(originalMap).Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(key, InputKeyEventKind.Down), 1, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.PublishSimulationActionSnapshot(0).Actions[0].IsDownAtEnd, Is.True);

        router.BeginHostControlBoundary(1, 2);
        var replacementResponse = router.ReplaceMap(replacementMap, expectedRevision: 1);
        Assert.That(replacementResponse.Status, Is.EqualTo(InputMapStatus.Replaced));

        var rebound = router.PublishSimulationActionSnapshot(1).Actions[0];
        Assert.That(rebound.MapRevision, Is.EqualTo(2));
        Assert.That(rebound.IsDownAtEnd, Is.False);
        Assert.That(rebound.ReleasedThisTick, Is.False);
        Assert.That(rebound.Transitions, Has.Length.EqualTo(1));
        Assert.That(rebound.Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Canceled));
        Assert.That(rebound.Transitions[0].CancellationReason, Is.EqualTo(InputCancellationReason.Rebound));
        Assert.That(rebound.Transitions[0].HostSequence, Is.EqualTo(replacementResponse.HostSequence));
        Assert.That(rebound.Transitions[0].AssignedTick, Is.EqualTo(1));

        var unchanged = router.PublishSimulationActionSnapshot(2).Actions[0];
        Assert.That(unchanged.Transitions, Is.Empty);
        Assert.That(unchanged.IsDownAtEnd, Is.False);

        router.BeginHostControlBoundary(3, 3);
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(key, InputKeyEventKind.Up), 3, sequenceLease.Allocate(), 3).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.PublishSimulationActionSnapshot(3).Actions[0].Transitions, Is.Empty);
        router.BeginHostControlBoundary(4, 4);
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(key, InputKeyEventKind.Down), 4, sequenceLease.Allocate(), 4).Status, Is.EqualTo(InputRoutingStatus.Applied));
        var rearmed = router.PublishSimulationActionSnapshot(4).Actions[0];
        Assert.That(rearmed.IsDownAtEnd, Is.True);
        Assert.That(rearmed.Transitions, Has.Length.EqualTo(1));
        Assert.That(rearmed.Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Pressed));

        router.Dispose();
    }

    [Test]
    public void ReplacesMapWhileContextCancellationIsPending()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            23,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        var mapId = new Guid("10000000-0000-0000-0000-000000000091");
        var actionId = new Guid("20000000-0000-0000-0000-000000000091");
        var context = new ContextDefinition(new Guid("30000000-0000-0000-0000-000000000091"), 0);
        var key = new KeyboardControl(7, 4);
        var action = new ActionDefinition(actionId, "Move", InputActionKind.Digital);
        var binding = new BindingDefinition(new Guid("40000000-0000-0000-0000-000000000091"), actionId, 1, BindingKind.Button) {
            Control = new DigitalControl(key),
        };
        var map = new InputActionMapV1(1, mapId, 1, 1, [action], [context], [binding]);
        var replacementBinding = new BindingDefinition(new Guid("40000000-0000-0000-0000-000000000092"), actionId, 1, BindingKind.Button) {
            Control = new DigitalControl(key),
        };
        var replacement = new InputActionMapV1(1, mapId, 2, 1, [action], [context], [replacementBinding]);

        Assert.That(router.RegisterMap(map).Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(key, InputKeyEventKind.Down), 1, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.PublishSimulationActionSnapshot(0).Actions[0].IsDownAtEnd, Is.True);

        var maskingContext = new ContextDefinition(
            new Guid("30000000-0000-0000-0000-000000000092"),
            0,
            priority: 10,
            captureClasses: InputClass.Keyboard);
        var maskingMap = new InputActionMapV1(
            1,
            new Guid("10000000-0000-0000-0000-000000000092"),
            1,
            1,
            [],
            [maskingContext],
            []);
        Assert.That(router.RegisterMap(maskingMap).Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(router.ReplaceMap(replacement, expectedRevision: 1).Status, Is.EqualTo(InputMapStatus.Replaced));

        var snapshot = router.PublishSimulationActionSnapshot(1);
        var replacedAction = snapshot.Actions[0];
        Assert.That(replacedAction.MapRevision, Is.EqualTo(2));
        Assert.That(replacedAction.IsDownAtEnd, Is.False);
        Assert.That(replacedAction.Transitions, Has.Length.EqualTo(1));
        Assert.That(replacedAction.Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Canceled));
        Assert.That(replacedAction.Transitions[0].CancellationReason, Is.EqualTo(InputCancellationReason.ContextMasked));

        router.Dispose();
    }

    [Test]
    public void ReplacesOnlyTheExpectedMapRevision()
    {
        var router = CreateRouter();
        var mapId = Guid.NewGuid();
        var context = new ContextDefinition(Guid.NewGuid(), 0);
        var firstAction = new ActionDefinition(Guid.NewGuid(), "First", InputActionKind.Digital);
        var secondAction = new ActionDefinition(Guid.NewGuid(), "Second", InputActionKind.Axis1);
        var firstResponse = router.RegisterMap(CreateMapWithContext(mapId, 1, context, firstAction));
        var previousSnapshot = router.GetHostActionSnapshot();
        var replacement = CreateMapWithContext(mapId, 2, context, secondAction);

        var staleResponse = router.ReplaceMap(replacement, expectedRevision: 0);
        Assert.That(staleResponse.Status, Is.EqualTo(InputMapStatus.Rejected));
        Assert.That(staleResponse.HostSequence, Is.Null);
        Assert.That(router.GetHostActionSnapshot().Actions[0].MapRevision, Is.EqualTo(1));

        var replacementResponse = router.ReplaceMap(replacement, expectedRevision: 1);
        var replacementSnapshot = router.GetHostActionSnapshot();

        Assert.That(replacementResponse.Status, Is.EqualTo(InputMapStatus.Replaced));
        Assert.That(replacementResponse.HostSequence, Is.Not.Null);
        Assert.That(firstResponse.HostSequence, Is.Not.Null);
        Assert.That(
            replacementResponse.HostSequence.GetValueOrDefault(),
            Is.GreaterThan(firstResponse.HostSequence.GetValueOrDefault()));
        Assert.That(replacementResponse.PublishedRevision, Is.EqualTo(2));
        Assert.That(replacementSnapshot.Actions[0].ActionId, Is.EqualTo(secondAction.ActionId));
        Assert.That(replacementSnapshot.Actions[0].MapRevision, Is.EqualTo(2));
        Assert.That(previousSnapshot.Actions[0].ActionId, Is.EqualTo(firstAction.ActionId));
        Assert.That(previousSnapshot.Actions[0].MapRevision, Is.EqualTo(1));
        router.Dispose();
    }

    [Test]
    public void ReplacesRemovedActiveActionWithOneSnapshotTerminalState()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            21,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        var mapId = new Guid("10000000-0000-0000-0000-000000000071");
        var oldActionId = new Guid("20000000-0000-0000-0000-000000000071");
        var newActionId = new Guid("20000000-0000-0000-0000-000000000072");
        var context = new ContextDefinition(new Guid("30000000-0000-0000-0000-000000000071"), 0);
        var key = new KeyboardControl(7, 4);
        var originalAction = new ActionDefinition(oldActionId, "Old", InputActionKind.Digital);
        var originalBinding = new BindingDefinition(new Guid("40000000-0000-0000-0000-000000000071"), oldActionId, 1, BindingKind.Button) {
            Control = new DigitalControl(key),
        };
        var originalMap = new InputActionMapV1(1, mapId, 1, 1, [originalAction], [context], [originalBinding]);
        var replacementAction = new ActionDefinition(newActionId, "New", InputActionKind.Digital);
        var replacementBinding = new BindingDefinition(new Guid("40000000-0000-0000-0000-000000000072"), newActionId, 1, BindingKind.Button) {
            Control = new DigitalControl(key),
        };
        var replacementMap = new InputActionMapV1(1, mapId, 2, 1, [replacementAction], [context], [replacementBinding]);

        Assert.That(router.RegisterMap(originalMap).Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(key, InputKeyEventKind.Down), 1, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.PublishSimulationActionSnapshot(0).Actions[0].IsDownAtEnd, Is.True);

        router.BeginHostControlBoundary(1, 2);
        Assert.That(router.ReplaceMap(replacementMap, expectedRevision: 1).Status, Is.EqualTo(InputMapStatus.Replaced));

        var reboundSnapshot = router.PublishSimulationActionSnapshot(1);
        Assert.That(reboundSnapshot.Actions, Has.Length.EqualTo(2));
        Assert.That(reboundSnapshot.Actions[0].ActionId, Is.EqualTo(oldActionId));
        Assert.That(reboundSnapshot.Actions[0].MapRevision, Is.EqualTo(1));
        Assert.That(reboundSnapshot.Actions[0].IsDownAtEnd, Is.False);
        Assert.That(reboundSnapshot.Actions[0].PressedThisTick, Is.False);
        Assert.That(reboundSnapshot.Actions[0].ReleasedThisTick, Is.False);
        Assert.That(reboundSnapshot.Actions[0].ValueChangedThisTick, Is.False);
        Assert.That(reboundSnapshot.Actions[0].Transitions, Has.Length.EqualTo(1));
        Assert.That(reboundSnapshot.Actions[0].Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Canceled));
        Assert.That(reboundSnapshot.Actions[0].Transitions[0].CancellationReason, Is.EqualTo(InputCancellationReason.Rebound));
        Assert.That(reboundSnapshot.Actions[0].Transitions[0].AssignedTick, Is.EqualTo(1));
        Assert.That(reboundSnapshot.Actions[1].ActionId, Is.EqualTo(newActionId));
        Assert.That(reboundSnapshot.Actions[1].MapRevision, Is.EqualTo(2));
        Assert.That(reboundSnapshot.Actions[1].Transitions, Is.Empty);

        var followingSnapshot = router.PublishSimulationActionSnapshot(2);
        Assert.That(followingSnapshot.Actions, Has.Length.EqualTo(1));
        Assert.That(followingSnapshot.Actions[0].ActionId, Is.EqualTo(newActionId));

        router.Dispose();
    }

    [Test]
    public void ReplacesRemovedAndReintroducedActionWithoutDuplicateState()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            22,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        var mapId = new Guid("10000000-0000-0000-0000-000000000081");
        var originalActionId = new Guid("20000000-0000-0000-0000-000000000081");
        var interimActionId = new Guid("20000000-0000-0000-0000-000000000082");
        var context = new ContextDefinition(new Guid("30000000-0000-0000-0000-000000000081"), 0);
        var key = new KeyboardControl(7, 4);
        var originalAction = new ActionDefinition(originalActionId, "Original", InputActionKind.Digital);
        var originalBinding = new BindingDefinition(new Guid("40000000-0000-0000-0000-000000000081"), originalActionId, 1, BindingKind.Button) {
            Control = new DigitalControl(key),
        };
        var originalMap = new InputActionMapV1(1, mapId, 1, 1, [originalAction], [context], [originalBinding]);
        var interimAction = new ActionDefinition(interimActionId, "Interim", InputActionKind.Digital);
        var interimBinding = new BindingDefinition(new Guid("40000000-0000-0000-0000-000000000082"), interimActionId, 1, BindingKind.Button) {
            Control = new DigitalControl(key),
        };
        var interimMap = new InputActionMapV1(1, mapId, 2, 1, [interimAction], [context], [interimBinding]);
        var replacementAction = new ActionDefinition(originalActionId, "Original", InputActionKind.Digital);
        var replacementBinding = new BindingDefinition(new Guid("40000000-0000-0000-0000-000000000083"), originalActionId, 1, BindingKind.Button) {
            Control = new DigitalControl(key),
        };
        var replacementMap = new InputActionMapV1(1, mapId, 3, 1, [replacementAction], [context], [replacementBinding]);

        Assert.That(router.RegisterMap(originalMap).Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(key, InputKeyEventKind.Down), 1, sequenceLease.Allocate(), 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.PublishSimulationActionSnapshot(0).Actions[0].IsDownAtEnd, Is.True);

        router.BeginHostControlBoundary(1, 2);
        var firstReplacement = router.ReplaceMap(interimMap, expectedRevision: 1);
        Assert.That(firstReplacement.Status, Is.EqualTo(InputMapStatus.Replaced));
        Assert.That(router.ReplaceMap(replacementMap, expectedRevision: 2).Status, Is.EqualTo(InputMapStatus.Replaced));

        var snapshot = router.PublishSimulationActionSnapshot(1);
        Assert.That(snapshot.Actions, Has.Length.EqualTo(1));
        Assert.That(snapshot.Actions[0].ActionId, Is.EqualTo(originalActionId));
        Assert.That(snapshot.Actions[0].MapRevision, Is.EqualTo(3));
        Assert.That(snapshot.Actions[0].Transitions, Has.Length.EqualTo(1));
        Assert.That(snapshot.Actions[0].Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Canceled));
        Assert.That(snapshot.Actions[0].Transitions[0].CancellationReason, Is.EqualTo(InputCancellationReason.Rebound));
        Assert.That(snapshot.Actions[0].Transitions[0].HostSequence, Is.EqualTo(firstReplacement.HostSequence));
        Assert.That(snapshot.Actions[0].Transitions[0].AssignedTick, Is.EqualTo(1));

        router.Dispose();
    }

    [Test]
    public void ReplacesTimelineClockSegmentAtOpenBoundaryWithoutResettingTick()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            40,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        var key = new KeyboardControl(7, 4);
        var action = new ActionDefinition(Guid.NewGuid(), "Move", InputActionKind.Digital);
        var context = new ContextDefinition(Guid.NewGuid(), 0);
        var binding = new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.Button) {
            Control = new DigitalControl(key),
        };
        var map = CreateMapWithContext(Guid.NewGuid(), 1, context, action);
        map = new InputActionMapV1(
            map.SchemaVersion,
            map.MapId,
            map.Revision,
            map.InitialActiveContextMask,
            map.Actions,
            map.Contexts,
            [binding]);
        Assert.That(router.RegisterMap(map).Status, Is.EqualTo(InputMapStatus.Registered));

        var firstSequence = sequenceLease.Allocate();
        var firstSegment = new SimulationClockSegment(
            1,
            0,
            firstSequence,
            0,
            0.0,
            60,
            0,
            1.0f,
            isPaused: false);
        router.SetSimulationClockSegment(in firstSegment);

        var downReceipt = router.EnqueueHeadlessInjection(
            new InputInjection(key, InputKeyEventKind.Down),
            166_666,
            firstSequence);
        Assert.That(downReceipt.AssignedTick, Is.EqualTo(0));
        _ = router.PublishSimulationActionSnapshot(0);

        var secondSequence = sequenceLease.Allocate();
        router.BeginHostControlBoundary(1, 166_666);
        var secondSegment = new SimulationClockSegment(
            2,
            166_666,
            secondSequence,
            166_666,
            0.0,
            60,
            1,
            1.0f,
            isPaused: false);
        router.SetSimulationClockSegment(in secondSegment);

        var upReceipt = router.EnqueueHeadlessInjection(
            new InputInjection(key, InputKeyEventKind.Up),
            333_333,
            secondSequence);
        Assert.That(upReceipt.Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(upReceipt.AssignedTick, Is.EqualTo(1));
        Assert.That(upReceipt.Late, Is.False);

        var snapshot = router.PublishSimulationActionSnapshot(1);
        Assert.That(snapshot.Actions[0].Transitions, Has.Length.EqualTo(1));
        Assert.That(snapshot.Actions[0].Transitions[0].HostSequence, Is.EqualTo(secondSequence));
        Assert.That(snapshot.Actions[0].Transitions[0].AssignedTick, Is.EqualTo(1));
        router.Dispose();
    }

    [Test]
    public void SelectsAxisWinnersByPriorityThenCanonicalBindingId()
    {
        var router = CreateRouter(InputRecipientKind.SimulationTimeline, recipientId: 10);
        var priorityAction = new ActionDefinition(new Guid("20000000-0000-0000-0000-000000000021"), "Priority", InputActionKind.Axis1);
        var idAction = new ActionDefinition(new Guid("20000000-0000-0000-0000-000000000022"), "Binding ID", InputActionKind.Axis1);
        var context = new ContextDefinition(Guid.NewGuid(), 0);
        var map = new InputActionMapV1(
            1,
            Guid.NewGuid(),
            1,
            1,
            [priorityAction, idAction],
            [context],
            [
                new BindingDefinition(new Guid("40000000-0000-0000-0000-000000000011"), priorityAction.ActionId, 1, BindingKind.GamepadAxis1) {
                    Axis1Source = new GamepadAxis1Source(1, GamepadAxis.RightX, deadzone: 0.0f),
                    Priority = 5,
                },
                new BindingDefinition(new Guid("40000000-0000-0000-0000-000000000012"), priorityAction.ActionId, 1, BindingKind.GamepadAxis1) {
                    Axis1Source = new GamepadAxis1Source(2, GamepadAxis.LeftX, deadzone: 0.0f),
                    Priority = 1,
                },
                new BindingDefinition(new Guid("40000000-0000-0000-0000-000000000020"), idAction.ActionId, 1, BindingKind.GamepadAxis1) {
                    Axis1Source = new GamepadAxis1Source(3, GamepadAxis.RightX, deadzone: 0.0f),
                    Priority = 1,
                },
                new BindingDefinition(new Guid("40000000-0000-0000-0000-000000000019"), idAction.ActionId, 1, BindingKind.GamepadAxis1) {
                    Axis1Source = new GamepadAxis1Source(4, GamepadAxis.LeftX, deadzone: 0.0f),
                    Priority = 1,
                },
            ]);
        var registration = router.RegisterMap(map);
        Assert.That(registration.Status, Is.EqualTo(InputMapStatus.Registered));
        var sequence = registration.HostSequence.GetValueOrDefault() + 1;

        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(1, GamepadAxis.RightX, 0.7f), 1, sequence++, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(2, GamepadAxis.LeftX, -0.7f), 2, sequence++, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(3, GamepadAxis.RightX, 0.6f), 3, sequence++, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.EnqueueHeadlessInjection(new InputInjection(4, GamepadAxis.LeftX, -0.6f), 4, sequence, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));

        var snapshot = router.PublishSimulationActionSnapshot(0);
        Assert.That(snapshot.Actions[0].AxisX, Is.EqualTo(-0.7f));
        Assert.That(snapshot.Actions[1].AxisX, Is.EqualTo(-0.6f));

        router.Dispose();
    }

    [Test]
    public void ServicesContextCancellationWithoutAWorldTick()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            13,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        var action = new ActionDefinition(Guid.NewGuid(), "Move", InputActionKind.Digital);
        var context = new ContextDefinition(Guid.NewGuid(), 0);
        var key = new KeyboardControl(7, 4);
        var binding = new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.Button) {
            Control = new DigitalControl(key),
        };
        var map = new InputActionMapV1(1, Guid.NewGuid(), 1, 1, [action], [context], [binding]);
        var registration = router.RegisterMap(map);
        Assert.That(registration.Status, Is.EqualTo(InputMapStatus.Registered));

        router.BeginHostControlBoundary(0, 100);
        var downSequence = sequenceLease.Allocate();
        Assert.That(
            router.EnqueueHeadlessInjection(
                new InputInjection(key, InputKeyEventKind.Down),
                100,
                downSequence,
                0).Status,
            Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.PublishSimulationActionSnapshot(0).Actions[0].IsDownAtEnd, Is.True);

        router.BeginHostControlBoundary(1, 101);
        Assert.That(router.SetActiveContextMask(map.MapId, 1, 0).Status, Is.EqualTo(InputRoutingStatus.Applied));

        var boundaryTransitions = router.ServiceInputControlBoundary();
        Assert.That(boundaryTransitions, Has.Length.EqualTo(1));
        Assert.That(boundaryTransitions[0].Kind, Is.EqualTo(InputTransitionKind.Canceled));
        Assert.That(boundaryTransitions[0].CancellationReason, Is.EqualTo(InputCancellationReason.ContextMasked));
        Assert.That(boundaryTransitions[0].AssignedTick, Is.Null);

        router.Dispose();
    }

    [Test]
    public void SetActiveContextMaskUsesHostSequenceAndBoundaryCapacity()
    {
        var router = CreateRouter(InputRecipientKind.SimulationTimeline, recipientId: 2);
        var mapId = Guid.NewGuid();
        var mapResponse = router.RegisterMap(CreateMap(mapId, 1));
        router.BeginHostControlBoundary(7, 0);

        var response = router.SetActiveContextMask(mapId, 1, 0);

        Assert.That(response.Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(response.RecipientId, Is.EqualTo(2));
        Assert.That(response.HostSequence, Is.Not.Null);
        Assert.That(mapResponse.HostSequence, Is.Not.Null);
        Assert.That(response.HostSequence.GetValueOrDefault(), Is.GreaterThan(mapResponse.HostSequence.GetValueOrDefault()));
        Assert.That(response.AssignedTick, Is.EqualTo(7));

        var staleResponse = router.SetActiveContextMask(mapId, 2, 1);
        Assert.That(staleResponse.Status, Is.EqualTo(InputRoutingStatus.Rejected));
        Assert.That(staleResponse.HostSequence, Is.Null);

        for (var index = 0; index < 255; index++)
        {
            var mask = (index & 1) == 0 ? 1UL : 0UL;
            Assert.That(router.SetActiveContextMask(mapId, 1, mask).Status, Is.EqualTo(InputRoutingStatus.Applied));
        }

        var capacityResponse = router.SetActiveContextMask(mapId, 1, 0);
        Assert.That(capacityResponse.Status, Is.EqualTo(InputRoutingStatus.CapacityExceeded));
        Assert.That(capacityResponse.HostSequence, Is.Null);

        router.BeginHostControlBoundary(8, 1);
        _ = router.ServiceInputControlBoundary();
        Assert.That(router.SetActiveContextMask(mapId, 1, 1).Status, Is.EqualTo(InputRoutingStatus.Applied));
        router.Dispose();
    }

    [Test]
    public void SimulationSnapshotsArePublishedForExactTicks()
    {
        var router = CreateRouter(InputRecipientKind.SimulationTimeline, recipientId: 3);
        var action = new ActionDefinition(Guid.NewGuid(), "Move", InputActionKind.Axis1);
        _ = router.RegisterMap(CreateMap(Guid.NewGuid(), 1, action));

        _ = Assert.Throws<InvalidOperationException>(() => router.GetSimulationActionSnapshot(0));
        _ = router.PublishSimulationActionSnapshot(0);

        var snapshot = router.GetSimulationActionSnapshot(0);
        Assert.That(snapshot.RecipientKind, Is.EqualTo(InputRecipientKind.SimulationTimeline));
        Assert.That(snapshot.RecipientId, Is.EqualTo(3));
        Assert.That(snapshot.AssignedTick, Is.Zero);
        Assert.That(snapshot.Actions, Has.Length.EqualTo(1));
        Assert.That(snapshot.Actions[0].Kind, Is.EqualTo(InputActionKind.Axis1));
        router.Dispose();
    }
}
