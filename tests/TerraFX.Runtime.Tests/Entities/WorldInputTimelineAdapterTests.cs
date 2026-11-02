// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Entities;
using TerraFX.Hosting;
using TerraFX.Input;
using TerraFX.Integration;
using TerraFX.Platform;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldInputTimelineAdapterTests
{
    private static InputActionMapV1 CreateDigitalMap(KeyboardControl key)
    {
        var action = new ActionDefinition(Guid.NewGuid(), "Move", InputActionKind.Digital);
        var context = new ContextDefinition(Guid.NewGuid(), 0);
        var binding = new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.Button) {
            Control = new DigitalControl(key),
        };
        return new InputActionMapV1(1, Guid.NewGuid(), 1, 1, [action], [context], [binding]);
    }

    private static World CreateWorld(float initialTimeScale)
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        return World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            InitialTimeScale = initialTimeScale,
            TickRate = 60,
            WorldGuid = Guid.NewGuid(),
        });
    }

    [Test]
    public void PauseAndResumeHeadlessTimelineAtPumpBoundaries()
    {
        using var world = CreateWorld(initialTimeScale: 1.0f);
        var adapter = new WorldInputTimelineAdapter(world);

        try
        {
            var key = new KeyboardControl(7, 4);
            Assert.That(adapter.Router.RegisterMap(CreateDigitalMap(key)).Status, Is.EqualTo(InputMapStatus.Registered));
            _ = adapter.BeginPumpBoundary(hostTimestamp: 0, isPaused: false);
            Assert.That(
                adapter.InjectHeadlessInput(new InputInjection(key, InputKeyEventKind.Down), 166_666).Status,
                Is.EqualTo(InputRoutingStatus.Applied));
            Assert.That(adapter.PublishInputStage(0).Actions[0].IsDownAtEnd, Is.True);
            Assert.That(world.Step().CompletedTick, Is.Zero);

            var pause = adapter.PauseHeadlessInput(
                333_332,
                out var discardedInputCount,
                out var pauseTransitions);
            Assert.That(pause.Status, Is.EqualTo(InputRoutingStatus.Applied));
            Assert.That(discardedInputCount, Is.Zero);
            Assert.That(pauseTransitions, Has.Length.EqualTo(1));
            Assert.That(pauseTransitions[0].Kind, Is.EqualTo(InputTransitionKind.Canceled));
            Assert.That(pauseTransitions[0].CancellationReason, Is.EqualTo(InputCancellationReason.Paused));
            Assert.That(adapter.CurrentSegment.IsPaused, Is.True);

            var resume = adapter.ReconcileHeadlessInputAtBoundary(
                [new InputInjection(key, InputKeyEventKind.Down)],
                333_332,
                out discardedInputCount,
                out var resumeTransitions);
            Assert.That(resume.Status, Is.EqualTo(InputRoutingStatus.Applied));
            Assert.That(discardedInputCount, Is.Zero);
            Assert.That(resumeTransitions, Is.Empty);
            Assert.That(adapter.CurrentSegment.IsPaused, Is.False);

            var resumedSnapshot = adapter.PublishInputStage(1);
            Assert.That(resumedSnapshot.Actions[0].IsDownAtEnd, Is.False);
            Assert.That(resumedSnapshot.Actions[0].Transitions, Is.Empty);
        }
        finally
        {
            _ = adapter.DisposeAndGetTransitions();
        }
    }

    [Test]
    public void RebasePreservesWorldClockAndPublishesInputTicks()
    {
        using var world = CreateWorld(initialTimeScale: 0.5f);
        var adapter = new WorldInputTimelineAdapter(world);

        try
        {
            var key = new KeyboardControl(7, 4);
            Assert.That(adapter.Router.RegisterMap(CreateDigitalMap(key)).Status, Is.EqualTo(InputMapStatus.Registered));

            var firstSegment = adapter.BeginPumpBoundary(hostTimestamp: 0, isPaused: false);
            Assert.That(firstSegment.Revision, Is.EqualTo(1));
            Assert.That(firstSegment.TickRate, Is.EqualTo(world.TickRate));
            Assert.That(firstSegment.NextOpenTick, Is.Zero);
            Assert.That(firstSegment.TimeScale, Is.EqualTo(0.5f));

            var down = adapter.InjectHeadlessInput(new InputInjection(key, InputKeyEventKind.Down), 166_666);
            Assert.That(down.Status, Is.EqualTo(InputRoutingStatus.Applied));
            Assert.That(down.AssignedTick, Is.Zero);
            Assert.That(down.Late, Is.False);

            var firstSnapshot = adapter.PublishInputStage(0);
            Assert.That(firstSnapshot.AssignedTick, Is.Zero);
            Assert.That(firstSnapshot.Actions[0].IsDownAtEnd, Is.True);
            Assert.That(firstSnapshot.Actions[0].PressedThisTick, Is.True);

            Assert.That(world.Step().CompletedTick, Is.Zero);
            world.SetTimeScale(1.0f);

            var secondSegment = adapter.BeginPumpBoundary(hostTimestamp: 333_333, isPaused: false);
            Assert.That(secondSegment.Revision, Is.EqualTo(2));
            Assert.That(secondSegment.NextOpenTick, Is.EqualTo(1));
            Assert.That(secondSegment.TimeScale, Is.EqualTo(1.0f));
            Assert.That(secondSegment.SimulationElapsedStartTicks, Is.EqualTo(166_666));
            Assert.That(secondSegment.FractionalSimulationTicks, Is.EqualTo(0.5));

            var up = adapter.InjectHeadlessInput(new InputInjection(key, InputKeyEventKind.Up), 333_333);
            Assert.That(up.Status, Is.EqualTo(InputRoutingStatus.Applied));
            Assert.That(up.AssignedTick, Is.EqualTo(1));
            Assert.That(up.Late, Is.True);

            var secondSnapshot = adapter.PublishInputStage(1);
            Assert.That(secondSnapshot.AssignedTick, Is.EqualTo(1));
            Assert.That(secondSnapshot.Actions[0].IsDownAtEnd, Is.False);
            Assert.That(secondSnapshot.Actions[0].ReleasedThisTick, Is.True);
        }
        finally
        {
            _ = adapter.DisposeAndGetTransitions();
        }
    }

    [Test]
    public void RejectsWorldAndRecipientTickDrift()
    {
        using var world = CreateWorld(initialTimeScale: 1.0f);
        var adapter = new WorldInputTimelineAdapter(world);

        try
        {
            _ = adapter.BeginPumpBoundary(hostTimestamp: 0, isPaused: false);
            _ = world.Step();

            var exception = Assert.Throws<InvalidOperationException>(
                () => adapter.BeginPumpBoundary(hostTimestamp: 1, isPaused: false));
            Assert.That(exception!.Message, Is.EqualTo("The World and input recipient next-open ticks are not synchronized."));
        }
        finally
        {
            _ = adapter.DisposeAndGetTransitions();
        }
    }

    [Test]
    public void RequiresPauseReconciliationBeforeInstallingSegment()
    {
        using var world = CreateWorld(initialTimeScale: 1.0f);
        var adapter = new WorldInputTimelineAdapter(world);

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => adapter.BeginPumpBoundary(hostTimestamp: 0, isPaused: true));
            Assert.That(
                exception!.Message,
                Is.EqualTo("The input recipient pause state must be reconciled before installing its clock segment."));

            var segment = adapter.BeginPumpBoundary(hostTimestamp: 0, isPaused: false);
            Assert.That(segment.Revision, Is.EqualTo(1));
        }
        finally
        {
            _ = adapter.DisposeAndGetTransitions();
        }
    }

    [Test]
    public void RoutesCapturedPhysicalUiDispositionToTimelineAtRecipientTick()
    {
        using var world = CreateWorld(initialTimeScale: 1.0f);
        var adapter = new WorldInputTimelineAdapter(world);
        var broker = new PlatformInputBroker(17, new IncrementingTimeProvider());

        try
        {
            var action = new ActionDefinition(Guid.NewGuid(), "Jump", InputActionKind.Digital);
            var context = new ContextDefinition(Guid.NewGuid(), 0);
            var binding = new BindingDefinition(Guid.NewGuid(), action.ActionId, 1, BindingKind.Button) {
                Control = new DigitalControl(playerSlot: 1, GamepadButtonValue.South),
            };
            var map = new InputActionMapV1(1, Guid.NewGuid(), 1, 1, [action], [context], [binding]);
            Assert.That(adapter.Router.RegisterMap(map).Status, Is.EqualTo(InputMapStatus.Registered));
            _ = adapter.BeginPumpBoundary(hostTimestamp: 0, isPaused: false);

            var nativeDevice = (nint)71;
            var capture = broker.Capture(
                new PlatformInputSampleBatch(
                    [
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
                            MonotonicTimestamp: 2),
                        new PlatformInputSample(
                            PhysicalInputEventKind.GamepadButtonUp,
                            null,
                            nativeDevice,
                            PlatformInputDeviceKind.Gamepad,
                            GamepadButton: GamepadButtonValue.South,
                            IsDown: false,
                            MonotonicTimestamp: 2),
                    ],
                    DiscardedSampleCount: 0),
                static _ => true);
            Assert.That(capture.Events, Has.Length.EqualTo(3));

            var down = adapter.EnqueueCapturedPhysicalEventWithUiDisposition(
                capture.Events[1],
                1,
                InputClass.Gamepad,
                out var downDiscarded);
            var up = adapter.EnqueueCapturedPhysicalEventWithUiDisposition(
                capture.Events[2],
                1,
                InputClass.None,
                out var upDiscarded);

            Assert.That(down.Status, Is.EqualTo(InputRoutingStatus.Applied));
            Assert.That(up.Status, Is.EqualTo(InputRoutingStatus.Applied));
            Assert.That(down.AssignedTick, Is.Zero);
            Assert.That(up.AssignedTick, Is.Zero);
            Assert.That(downDiscarded, Is.Zero);
            Assert.That(upDiscarded, Is.Zero);

            var snapshot = adapter.PublishInputStage(0);
            Assert.That(snapshot.AssignedTick, Is.Zero);
            Assert.That(snapshot.Actions[0].IsDownAtEnd, Is.False);
            Assert.That(snapshot.Actions[0].PressedThisTick, Is.False);
            Assert.That(snapshot.Actions[0].ReleasedThisTick, Is.False);
            Assert.That(snapshot.Actions[0].Transitions, Is.Empty);
        }
        finally
        {
            broker.Retire();
            _ = adapter.DisposeAndGetTransitions();
        }
    }

    [Test]
    public void RoutesSharedCaptureThroughRecipientClocks()
    {
        using var firstWorld = CreateWorld(initialTimeScale: 1.0f);
        using var secondWorld = CreateWorld(initialTimeScale: 0.5f);
        var firstAdapter = new WorldInputTimelineAdapter(firstWorld);
        var secondAdapter = new WorldInputTimelineAdapter(secondWorld);

        try
        {
            var key = new KeyboardControl(7, 4);
            var map = CreateDigitalMap(key);
            Assert.That(firstAdapter.Router.RegisterMap(map).Status, Is.EqualTo(InputMapStatus.Registered));
            Assert.That(secondAdapter.Router.RegisterMap(map).Status, Is.EqualTo(InputMapStatus.Registered));
            _ = firstAdapter.BeginPumpBoundary(hostTimestamp: 0, isPaused: false);
            _ = secondAdapter.BeginPumpBoundary(hostTimestamp: 0, isPaused: false);

            var injection = new InputInjection(key, InputKeyEventKind.Down);
            var firstReceipt = firstAdapter.InjectHeadlessInput(injection, 250_000);
            var secondReceipt = secondAdapter.EnqueueCapturedInput(
                injection,
                250_000,
                firstReceipt.HostSequence.GetValueOrDefault());

            Assert.That(firstReceipt.Status, Is.EqualTo(InputRoutingStatus.Applied));
            Assert.That(secondReceipt.Status, Is.EqualTo(InputRoutingStatus.Applied));
            Assert.That(secondReceipt.HostSequence, Is.EqualTo(firstReceipt.HostSequence));
            Assert.That(firstReceipt.AssignedTick, Is.EqualTo(1));
            Assert.That(secondReceipt.AssignedTick, Is.Zero);

            var firstSnapshot = firstAdapter.PublishInputStage(0);
            var secondSnapshot = secondAdapter.PublishInputStage(0);
            Assert.That(firstSnapshot.Actions[0].IsDownAtEnd, Is.False);
            Assert.That(secondSnapshot.Actions[0].IsDownAtEnd, Is.True);
        }
        finally
        {
            _ = firstAdapter.DisposeAndGetTransitions();
            _ = secondAdapter.DisposeAndGetTransitions();
        }
    }

    private sealed class IncrementingTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => ++_timestamp;
    }
}
