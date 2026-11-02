// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using NUnit.Framework;
using TerraFX;
using TerraFX.Input;

namespace TerraFX.Input.Tests;

[TestFixture]
internal sealed class InputRouterUiDispositionTests
{
    private static PhysicalInputEvent CreateEvent(
        PhysicalInputEventKind kind,
        long hostTimestamp,
        long hostSequence,
        KeyboardControl key,
        bool isDown)
        => new PhysicalInputEvent(
            kind,
            hostTimestamp,
            hostSequence,
            device: new InputDeviceHandle(71, 0, 1),
            window: new WindowHandle(71, 0, 1),
            value: new PhysicalInputValue(new DigitalControl(key), isDown));

    [Test]
    public void CapturedHeldControlCancelsUntilItsNeutralEvent()
    {
        var sequenceLease = new HostEventSequenceLease();
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            71,
            Environment.CurrentManagedThreadId,
            sequenceLease);
        var key = new KeyboardControl(7, 4);
        var action = new ActionDefinition(Guid.NewGuid(), "Move", InputActionKind.Digital);
        var context = new ContextDefinition(Guid.NewGuid(), 0);
        var map = new InputActionMapV1(
            1,
            Guid.NewGuid(),
            1,
            1,
            [action],
            [context],
            [
                new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.Button) {
                    Control = new DigitalControl(key),
                },
            ]);

        Assert.That(router.RegisterMap(map).Status, Is.EqualTo(InputMapStatus.Registered));
        var segmentSequence = sequenceLease.Allocate();
        var segment = new SimulationClockSegment(1, 0, segmentSequence, 0, 0.0, 60, 0, 1.0f, isPaused: false);
        router.SetSimulationClockSegment(in segment);

        var keyDown = CreateEvent(PhysicalInputEventKind.KeyDown, 166_666, sequenceLease.Allocate(), key, isDown: true);
        Assert.That(router.EnqueuePhysicalEvent(keyDown, null, InputClass.None, out _).Status, Is.EqualTo(InputRoutingStatus.Applied));
        Assert.That(router.PublishSimulationActionSnapshot(0).Actions[0].IsDownAtEnd, Is.True);

        var capturedRepeat = CreateEvent(PhysicalInputEventKind.KeyRepeat, 333_332, sequenceLease.Allocate(), key, isDown: true);
        Assert.That(router.EnqueuePhysicalEvent(capturedRepeat, null, InputClass.Keyboard, out _).Status, Is.EqualTo(InputRoutingStatus.Applied));
        var canceled = router.PublishSimulationActionSnapshot(1).Actions[0];
        Assert.That(canceled.IsDownAtEnd, Is.False);
        Assert.That(canceled.Transitions, Has.Length.EqualTo(1));
        Assert.That(canceled.Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Canceled));
        Assert.That(canceled.Transitions[0].CancellationReason, Is.EqualTo(InputCancellationReason.InputConsumed));

        var keyUp = CreateEvent(PhysicalInputEventKind.KeyUp, 500_000, sequenceLease.Allocate(), key, isDown: false);
        Assert.That(router.EnqueuePhysicalEvent(keyUp, null, InputClass.None, out _).Status, Is.EqualTo(InputRoutingStatus.Applied));
        var neutral = router.PublishSimulationActionSnapshot(2).Actions[0];
        Assert.That(neutral.IsDownAtEnd, Is.False);
        Assert.That(neutral.Transitions, Is.Empty);

        var rearmedDown = CreateEvent(PhysicalInputEventKind.KeyDown, 666_664, sequenceLease.Allocate(), key, isDown: true);
        Assert.That(router.EnqueuePhysicalEvent(rearmedDown, null, InputClass.None, out _).Status, Is.EqualTo(InputRoutingStatus.Applied));
        var rearmed = router.PublishSimulationActionSnapshot(3).Actions[0];
        Assert.That(rearmed.IsDownAtEnd, Is.True);
        Assert.That(rearmed.Transitions, Has.Length.EqualTo(1));
        Assert.That(rearmed.Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Pressed));

        router.Dispose();
    }

    [Test]
    public void RejectsUnknownCapturedInputClasses()
    {
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            72,
            Environment.CurrentManagedThreadId,
            new HostEventSequenceLease());
        var inputEvent = CreateEvent(
            PhysicalInputEventKind.KeyDown,
            1,
            1,
            new KeyboardControl(7, 4),
            isDown: true);

        var receipt = router.EnqueuePhysicalEvent(
            inputEvent,
            playerSlot: null,
            (InputClass)8,
            discardedInputCount: out var discardedInputCount);

        Assert.That(receipt.Status, Is.EqualTo(InputRoutingStatus.Rejected));
        Assert.That(receipt.Rejection?.Code, Is.EqualTo("InvalidUiDisposition"));
        Assert.That(discardedInputCount, Is.Zero);
        router.Dispose();
    }
}
