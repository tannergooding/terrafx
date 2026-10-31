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
        => new InputRouter(recipientKind, recipientId, Environment.CurrentManagedThreadId, new HostEventSequenceLease());

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
    public void DisposeIsIdempotentAndClosesRouterOperations()
    {
        var router = CreateRouter();

        router.Dispose();
        router.Dispose();

        _ = Assert.Throws<ObjectDisposedException>(() => router.GetHostActionSnapshot());
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
