// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using TerraFX;
using TerraFX.Hosting;
using TerraFX.Input;
using TerraFX.Numerics;
using TerraFX.UI;

namespace TerraFX.Runtime.Tests.Hosting;

[TestFixture]
internal sealed class PlatformWindowInputRouteTests
{
    private static InputActionMapV1 CreateMap(KeyboardControl key)
    {
        var action = new ActionDefinition(Guid.NewGuid(), "Move", InputActionKind.Digital);
        var context = new ContextDefinition(Guid.NewGuid(), 0);
        return new InputActionMapV1(
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
    }

    private static PhysicalInputEvent CreateWindowKeyEvent(
        PhysicalInputEventKind kind,
        long hostTimestamp,
        long hostSequence,
        WindowHandle window,
        KeyboardControl key,
        bool isDown)
        => new(
            kind,
            hostTimestamp,
            hostSequence,
            device: new InputDeviceHandle(window.OwnerId, 0, 1),
            window,
            value: new PhysicalInputValue(new DigitalControl(key), isDown));

    [Test]
    public void RoutesUiDispositionBeforeTheMatchingGameplayCopy()
    {
        var ownerThreadId = Environment.CurrentManagedThreadId;
        var sequenceLease = new HostEventSequenceLease();
        var window = new WindowHandle(71, 0, 1);
        var key = new KeyboardControl(7, 4);
        var hostLiveUiRouter = new InputRouter(
            InputRecipientKind.HostLiveUI,
            71,
            ownerThreadId,
            sequenceLease,
            window);
        var simulationRouter = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            72,
            ownerThreadId,
            sequenceLease);
        var map = CreateMap(key);
        Assert.That(simulationRouter.RegisterMap(map).Status, Is.EqualTo(InputMapStatus.Registered));
        var segmentSequence = sequenceLease.Allocate();
        var segment = new SimulationClockSegment(1, 0, segmentSequence, 0, 0.0, 60, 0, 1.0f, isPaused: false);
        simulationRouter.SetSimulationClockSegment(in segment);
        var routedSequences = new List<long>();
        var route = new PlatformWindowInputRoute(
            window,
            hostLiveUiRouter,
            inputEvent => {
                routedSequences.Add(inputEvent.HostSequence);

                if (inputEvent.Kind == PhysicalInputEventKind.KeyRepeat)
                {
                    Assert.That(simulationRouter.GetSimulationActionSnapshot(0).Actions[0].IsDownAtEnd, Is.True);
                    return new UiInputDisposition(InputClass.Keyboard);
                }

                return default;
            });
        route.AttachSimulationTimeline(simulationRouter);

        var keyDown = CreateWindowKeyEvent(
            PhysicalInputEventKind.KeyDown,
            166_666,
            sequenceLease.Allocate(),
            window,
            key,
            isDown: true);
        var keyDownResult = route.Route(keyDown);

        Assert.Multiple(() => {
            Assert.That(keyDownResult.UiDisposition.CapturedClasses, Is.EqualTo(InputClass.None));
            Assert.That(keyDownResult.HostRecipient?.Status, Is.EqualTo(InputRoutingStatus.Applied));
            Assert.That(keyDownResult.SimulationRecipient?.Status, Is.EqualTo(InputRoutingStatus.Applied));
            Assert.That(keyDownResult.HostRecipient?.HostSequence, Is.EqualTo(keyDown.HostSequence));
            Assert.That(keyDownResult.SimulationRecipient?.HostSequence, Is.EqualTo(keyDown.HostSequence));
        });
        _ = simulationRouter.PublishSimulationActionSnapshot(0);

        var capturedRepeat = CreateWindowKeyEvent(
            PhysicalInputEventKind.KeyRepeat,
            333_332,
            sequenceLease.Allocate(),
            window,
            key,
            isDown: true);
        var capturedResult = route.Route(capturedRepeat);

        Assert.Multiple(() => {
            Assert.That(capturedResult.UiDisposition.CapturedClasses, Is.EqualTo(InputClass.Keyboard));
            Assert.That(capturedResult.HostRecipient?.HostSequence, Is.EqualTo(capturedRepeat.HostSequence));
            Assert.That(capturedResult.SimulationRecipient?.HostSequence, Is.EqualTo(capturedRepeat.HostSequence));
        });
        var canceled = simulationRouter.PublishSimulationActionSnapshot(1).Actions[0];
        Assert.That(canceled.IsDownAtEnd, Is.False);
        Assert.That(canceled.Transitions, Has.Length.EqualTo(1));
        Assert.That(canceled.Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Canceled));
        Assert.That(canceled.Transitions[0].CancellationReason, Is.EqualTo(InputCancellationReason.InputConsumed));

        var keyUp = CreateWindowKeyEvent(
            PhysicalInputEventKind.KeyUp,
            500_000,
            sequenceLease.Allocate(),
            window,
            key,
            isDown: false);
        _ = route.Route(keyUp);
        Assert.That(simulationRouter.PublishSimulationActionSnapshot(2).Actions[0].Transitions, Is.Empty);

        var rearmedDown = CreateWindowKeyEvent(
            PhysicalInputEventKind.KeyDown,
            666_664,
            sequenceLease.Allocate(),
            window,
            key,
            isDown: true);
        _ = route.Route(rearmedDown);
        var rearmed = simulationRouter.PublishSimulationActionSnapshot(3).Actions[0];

        Assert.Multiple(() => {
            Assert.That(rearmed.IsDownAtEnd, Is.True);
            Assert.That(rearmed.Transitions, Has.Length.EqualTo(1));
            Assert.That(rearmed.Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Pressed));
            Assert.That(routedSequences, Is.EqualTo([
                keyDown.HostSequence,
                capturedRepeat.HostSequence,
                keyUp.HostSequence,
                rearmedDown.HostSequence,
            ]));
        });

        hostLiveUiRouter.Dispose();
        simulationRouter.Dispose();
    }

    [Test]
    public void RoutesUiNonActionWindowSamplesAndRequestsFocusReconciliation()
    {
        var ownerThreadId = Environment.CurrentManagedThreadId;
        var sequenceLease = new HostEventSequenceLease();
        var window = new WindowHandle(73, 1, 1);
        var hostLiveUiRouter = new InputRouter(
            InputRecipientKind.HostLiveUI,
            73,
            ownerThreadId,
            sequenceLease,
            window);
        var routedKinds = new List<PhysicalInputEventKind>();
        var route = new PlatformWindowInputRoute(
            window,
            hostLiveUiRouter,
            inputEvent => {
                routedKinds.Add(inputEvent.Kind);
                return new UiInputDisposition(InputClass.Pointing);
            });
        var pointerMove = new PhysicalInputEvent(
            PhysicalInputEventKind.PointerAbsoluteMotion,
            hostTimestamp: 1,
            hostSequence: sequenceLease.Allocate(),
            device: new InputDeviceHandle(window.OwnerId, 0, 1),
            window,
            pointerPosition: Vector2.Zero,
            pointerContentScale: Vector2.One);
        var pointerResult = route.Route(pointerMove);
        var focusGained = new PhysicalInputEvent(
            PhysicalInputEventKind.FocusGained,
            hostTimestamp: 2,
            hostSequence: sequenceLease.Allocate(),
            window: window);
        var focusResult = route.Route(focusGained);

        Assert.Multiple(() => {
            Assert.That(pointerResult.UiDisposition.CapturedClasses, Is.EqualTo(InputClass.Pointing));
            Assert.That(pointerResult.HostRecipient, Is.Null);
            Assert.That(pointerResult.SimulationRecipient, Is.Null);
            Assert.That(focusResult.RequiresStateReconciliation, Is.True);
            Assert.That(routedKinds, Is.EqualTo([
                PhysicalInputEventKind.PointerAbsoluteMotion,
                PhysicalInputEventKind.FocusGained,
            ]));
        });

        hostLiveUiRouter.Dispose();
    }
}
