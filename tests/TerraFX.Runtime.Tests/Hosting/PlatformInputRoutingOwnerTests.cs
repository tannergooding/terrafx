// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;
using TerraFX.Hosting;
using TerraFX.Integration;
using TerraFX.Input;
using TerraFX.Numerics;
using TerraFX.Platform;

namespace TerraFX.Runtime.Tests.Hosting;

[TestFixture]
internal sealed partial class PlatformInputRoutingOwnerTests
{
    private static PhysicalInputEvent CreateGamepadEvent(
        PhysicalInputEventKind kind,
        long hostTimestamp,
        long hostSequence,
        InputDeviceHandle device,
        bool isDown)
        => new(
            kind,
            hostTimestamp,
            hostSequence,
            device,
            value: new PhysicalInputValue(GamepadButtonValue.South, isDown));

    private static PhysicalInputEvent CreateKeyboardEvent(
        PhysicalInputEventKind kind,
        long hostTimestamp,
        long hostSequence,
        InputDeviceHandle device,
        WindowHandle window,
        KeyboardControl key,
        bool isDown)
        => new(
            kind,
            hostTimestamp,
            hostSequence,
            device,
            window,
            value: new PhysicalInputValue(new DigitalControl(key), isDown));

    private static InputActionMapV1 CreateKeyboardMap(KeyboardControl key)
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

    private static InputActionMapV1 CreateMap(ushort playerSlot)
    {
        var action = new ActionDefinition(Guid.NewGuid(), "Confirm", InputActionKind.Digital);
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
                    Control = new DigitalControl(playerSlot, GamepadButtonValue.South),
                },
            ]);
    }

    private static World CreateWorld()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        return World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            TickRate = 60,
            WorldGuid = Guid.NewGuid(),
        });
    }

    [Test]
    public void AssignsOneGamepadPerPlayerSlotAndRearmsAfterNeutralInput()
    {
        var ownerThreadId = Environment.CurrentManagedThreadId;
        var ownerId = OwnerIdAllocator.Allocate();
        var platformOwner = new TestPlatformInputRoutingHost(ownerId);
        var sequenceLease = new HostEventSequenceLease();
        platformOwner.SequenceLease = sequenceLease;
        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            OwnerIdAllocator.Allocate(),
            ownerThreadId,
            sequenceLease);
        var actionMap = CreateMap(playerSlot: 2);
        Assert.That(router.RegisterMap(actionMap).Status, Is.EqualTo(InputMapStatus.Registered));
        var segmentSequence = sequenceLease.Allocate();
        var segment = new SimulationClockSegment(1, 0, segmentSequence, 0, 0.0, 60, 0, 1.0f, isPaused: false);
        router.SetSimulationClockSegment(in segment);
        var routers = new HashSet<InputRouter> { router };
        var timelineRouters = new Dictionary<InputRouter, WorldInputTimelineAdapter>();
        var routingOwner = new PlatformInputRoutingOwner(
            HostProfile.Interactive,
            platformOwner,
            sequenceLease,
            new DiagnosticsService(),
            ownerThreadId,
            routers,
            timelineRouters,
            recipientId => (recipientId == router.RecipientId) ? router : null);
        var assignedDevice = new InputDeviceHandle(ownerId, 1, 1);
        var unassignedDevice = new InputDeviceHandle(ownerId, 2, 1);

        try
        {
            var assignment = routingOwner.AssignDevice(assignedDevice, router.RecipientId, playerSlot: 2);
            var occupiedSlot = routingOwner.AssignDevice(unassignedDevice, router.RecipientId, playerSlot: 2);
            Assert.That(assignment.Status, Is.EqualTo(InputRoutingStatus.Applied));
            Assert.That(occupiedSlot.Status, Is.EqualTo(InputRoutingStatus.Rejected));
            Assert.That(occupiedSlot.Rejection?.Code, Is.EqualTo("PlayerSlotAlreadyAssigned"));
            Assert.That(platformOwner.GamepadReconciliationRequests, Is.EqualTo([assignedDevice]));

            var unassignedDown = CreateGamepadEvent(
                PhysicalInputEventKind.GamepadButtonDown,
                166_666,
                sequenceLease.Allocate(),
                unassignedDevice,
                isDown: true);
            var assignedDown = CreateGamepadEvent(
                PhysicalInputEventKind.GamepadButtonDown,
                166_666,
                sequenceLease.Allocate(),
                assignedDevice,
                isDown: true);
            routingOwner.RouteCapturedInput(new PlatformInputCaptureBatch(
                [unassignedDown, assignedDown],
                DiscardedSampleCount: 0,
                Diagnostics: []));
            var disarmed = router.PublishSimulationActionSnapshot(0).Actions[0];
            Assert.That(disarmed.IsDownAtEnd, Is.False);
            Assert.That(disarmed.Transitions, Is.Empty);

            var assignedUp = CreateGamepadEvent(
                PhysicalInputEventKind.GamepadButtonUp,
                333_332,
                sequenceLease.Allocate(),
                assignedDevice,
                isDown: false);
            routingOwner.RouteCapturedInput(new PlatformInputCaptureBatch([assignedUp], 0, []));
            Assert.That(router.PublishSimulationActionSnapshot(1).Actions[0].IsDownAtEnd, Is.False);

            var rearmedDown = CreateGamepadEvent(
                PhysicalInputEventKind.GamepadButtonDown,
                500_000,
                sequenceLease.Allocate(),
                assignedDevice,
                isDown: true);
            routingOwner.RouteCapturedInput(new PlatformInputCaptureBatch([rearmedDown], 0, []));
            var rearmed = router.PublishSimulationActionSnapshot(2).Actions[0];
            Assert.Multiple(() => {
                Assert.That(rearmed.IsDownAtEnd, Is.True);
                Assert.That(rearmed.Transitions, Has.Length.EqualTo(1));
                Assert.That(rearmed.Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Pressed));
            });

            platformOwner.HostTimestamp = 666_664;
            routingOwner.RouteCapturedInput(new PlatformInputCaptureBatch([], 1, []));
            var inputLoss = router.ServiceHostControlBoundary();
            Assert.Multiple(() => {
                Assert.That(inputLoss, Is.Not.Null);
                Assert.That(inputLoss!.FirstUnacceptedHostSequence, Is.GreaterThan(rearmedDown.HostSequence));
                Assert.That(inputLoss.LastUnacceptedHostSequence, Is.EqualTo(inputLoss.FirstUnacceptedHostSequence));
            });
        }
        finally
        {
            routingOwner.ClearRoutes();
            _ = router.DisposeAndGetTransitions();
        }
    }

    [Test]
    public void BindGameplayInputDoesNotCreateHiddenHostLiveUiRecipient()
    {
        var ownerThreadId = Environment.CurrentManagedThreadId;
        var ownerId = OwnerIdAllocator.Allocate();
        var sequenceLease = new HostEventSequenceLease();
        var platformOwner = new TestPlatformInputRoutingHost(ownerId) {
            SequenceLease = sequenceLease,
        };
        var window = new WindowHandle(ownerId, 0, 1);
        var inputRouters = new HashSet<InputRouter>();
        var timelineRouters = new Dictionary<InputRouter, WorldInputTimelineAdapter>();
        var world = CreateWorld();
        var routingOwner = new PlatformInputRoutingOwner(
            HostProfile.Interactive,
            platformOwner,
            sequenceLease,
            new DiagnosticsService(),
            ownerThreadId,
            inputRouters,
            timelineRouters,
            recipientId => timelineRouters.Keys.FirstOrDefault(router => router.RecipientId == recipientId));

        try
        {
            var binding = routingOwner.BindGameplayInput(window, world, worldIsHosted: true);
            Assert.That(binding.Status, Is.EqualTo(InputBindingStatus.Bound));
            var boundRouter = binding.Router
                ?? throw new InvalidOperationException("The successful input binding omitted its recipient.");
            Assert.That(inputRouters, Has.Count.EqualTo(1));
            Assert.That(timelineRouters.ContainsKey(boundRouter), Is.True);

            var hostLiveUiRouter = routingOwner.CreateInputRouter(InputRecipientKind.HostLiveUI, window);
            Assert.Multiple(() => {
                Assert.That(hostLiveUiRouter.RecipientKind, Is.EqualTo(InputRecipientKind.HostLiveUI));
                Assert.That(inputRouters, Has.Count.EqualTo(2));
            });
        }
        finally
        {
            routingOwner.ClearRoutes();

            foreach (var adapter in timelineRouters.Values)
            {
                _ = adapter.DisposeAndGetTransitions();
            }

            foreach (var router in inputRouters)
            {
                _ = router.DisposeAndGetTransitions();
            }

            world.Dispose();
        }
    }

    [Test]
    public void DisposesHostLiveUiRecipientWithoutDetachingGameplayTimeline()
    {
        var ownerThreadId = Environment.CurrentManagedThreadId;
        var ownerId = OwnerIdAllocator.Allocate();
        var sequenceLease = new HostEventSequenceLease();
        var platformOwner = new TestPlatformInputRoutingHost(ownerId) {
            HostTimestamp = 0,
            SequenceLease = sequenceLease,
        };
        var window = new WindowHandle(ownerId, 0, 1);
        var device = new InputDeviceHandle(ownerId, 3, 1);
        var key = new KeyboardControl(7, 4);
        HashSet<InputRouter> inputRouters = [];
        InputRouter? simulationRouter = null;
        var routingOwner = new PlatformInputRoutingOwner(
            HostProfile.Interactive,
            platformOwner,
            sequenceLease,
            new DiagnosticsService(),
            ownerThreadId,
            inputRouters,
            [],
            recipientId => (simulationRouter?.RecipientId == recipientId) ? simulationRouter : null);
        var hostRouter = routingOwner.CreateInputRouter(InputRecipientKind.HostLiveUI, window);
        simulationRouter = routingOwner.CreateInputRouter(InputRecipientKind.SimulationTimeline, window: null);
        _ = simulationRouter.RegisterMap(CreateKeyboardMap(key));
        var segment = new SimulationClockSegment(
            1,
            0,
            sequenceLease.Allocate(),
            0,
            0.0,
            60,
            0,
            1.0f,
            isPaused: false);
        simulationRouter.SetSimulationClockSegment(in segment);
        routingOwner.AttachSimulationTimelineToWindow(window, simulationRouter);

        try
        {
            var disposedTransitions = routingOwner.DisposeInputRouter(hostRouter);
            Assert.That(simulationRouter.ServiceInputControlBoundary(), Is.Empty);

            var keyUp = CreateKeyboardEvent(
                PhysicalInputEventKind.KeyUp,
                166_666,
                sequenceLease.Allocate(),
                device,
                window,
                key,
                isDown: false);
            routingOwner.RouteCapturedInput(new PlatformInputCaptureBatch([keyUp], 0, []));
            var neutralSnapshot = simulationRouter.PublishSimulationActionSnapshot(0);

            var keyDown = CreateKeyboardEvent(
                PhysicalInputEventKind.KeyDown,
                333_332,
                sequenceLease.Allocate(),
                device,
                window,
                key,
                isDown: true);
            routingOwner.RouteCapturedInput(new PlatformInputCaptureBatch([keyDown], 0, []));
            var resumedSnapshot = simulationRouter.PublishSimulationActionSnapshot(1);

            Assert.Multiple(() => {
                Assert.That(disposedTransitions, Is.Empty);
                Assert.That(neutralSnapshot.Actions[0].IsDownAtEnd, Is.False);
                Assert.That(neutralSnapshot.Actions[0].Transitions, Is.Empty);
                Assert.That(resumedSnapshot.Actions[0].IsDownAtEnd, Is.True);
                Assert.That(resumedSnapshot.Actions[0].Transitions, Has.Length.EqualTo(1));
                Assert.That(resumedSnapshot.Actions[0].Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Pressed));
                Assert.That(() => hostRouter.GetHostActionSnapshot(), Throws.TypeOf<ObjectDisposedException>());
            });
        }
        finally
        {
            routingOwner.ClearRoutes();

            foreach (var router in inputRouters)
            {
                _ = router.DisposeAndGetTransitions();
            }
        }
    }

    [Test]
    public void DisposesSimulationTimelineWithTerminalCancellationOnlyForThatRecipient()
    {
        var ownerThreadId = Environment.CurrentManagedThreadId;
        var ownerId = OwnerIdAllocator.Allocate();
        var sequenceLease = new HostEventSequenceLease();
        var platformOwner = new TestPlatformInputRoutingHost(ownerId) {
            HostTimestamp = 0,
            SequenceLease = sequenceLease,
        };
        var window = new WindowHandle(ownerId, 0, 1);
        var device = new InputDeviceHandle(ownerId, 3, 1);
        var key = new KeyboardControl(7, 4);
        HashSet<InputRouter> inputRouters = [];
        InputRouter? simulationRouter = null;
        var routingOwner = new PlatformInputRoutingOwner(
            HostProfile.Interactive,
            platformOwner,
            sequenceLease,
            new DiagnosticsService(),
            ownerThreadId,
            inputRouters,
            [],
            recipientId => (simulationRouter?.RecipientId == recipientId) ? simulationRouter : null);
        var hostRouter = routingOwner.CreateInputRouter(InputRecipientKind.HostLiveUI, window);
        simulationRouter = routingOwner.CreateInputRouter(InputRecipientKind.SimulationTimeline, window: null);
        Assert.That(simulationRouter.RegisterMap(CreateKeyboardMap(key)).Status, Is.EqualTo(InputMapStatus.Registered));
        var segment = new SimulationClockSegment(
            1,
            0,
            sequenceLease.Allocate(),
            0,
            0.0,
            60,
            0,
            1.0f,
            isPaused: false);
        simulationRouter.SetSimulationClockSegment(in segment);
        routingOwner.AttachSimulationTimelineToWindow(window, simulationRouter);

        try
        {
            var keyDown = CreateKeyboardEvent(
                PhysicalInputEventKind.KeyDown,
                166_666,
                sequenceLease.Allocate(),
                device,
                window,
                key,
                isDown: true);
            routingOwner.RouteCapturedInput(new PlatformInputCaptureBatch([keyDown], 0, []));
            Assert.That(simulationRouter.PublishSimulationActionSnapshot(0).Actions[0].IsDownAtEnd, Is.True);

            platformOwner.HostTimestamp = 333_332;
            var disposedTransitions = routingOwner.DisposeInputRouter(simulationRouter);

            Assert.Multiple(() => {
                Assert.That(disposedTransitions, Has.Length.EqualTo(1));
                Assert.That(disposedTransitions[0].Kind, Is.EqualTo(InputTransitionKind.Canceled));
                Assert.That(disposedTransitions[0].CancellationReason, Is.EqualTo(InputCancellationReason.RecipientDisposed));
                Assert.That(() => simulationRouter.GetSimulationActionSnapshot(0), Throws.TypeOf<ObjectDisposedException>());
                Assert.That(() => hostRouter.GetHostActionSnapshot(), Throws.Nothing);
            });
        }
        finally
        {
            routingOwner.ClearRoutes();

            foreach (var router in inputRouters)
            {
                _ = router.DisposeAndGetTransitions();
            }
        }
    }

    [Test]
    public void FocusRegainWarningIsSuppressedOnlyByANewerAcceptedSnapshot()
    {
        var ownerThreadId = Environment.CurrentManagedThreadId;
        var ownerId = OwnerIdAllocator.Allocate();
        var platformOwner = new TestPlatformInputRoutingHost(ownerId);
        var sequenceLease = new HostEventSequenceLease();
        var inputRouters = new HashSet<InputRouter>();
        InputRouter? simulationRouter = null;
        var diagnostics = new DiagnosticsService();
        var routingOwner = new PlatformInputRoutingOwner(
            HostProfile.Interactive,
            platformOwner,
            sequenceLease,
            diagnostics,
            ownerThreadId,
            inputRouters,
            [],
            recipientId => (simulationRouter?.RecipientId == recipientId) ? simulationRouter : null);
        var window = new WindowHandle(ownerId, 0, 1);
        _ = routingOwner.CreateInputRouter(InputRecipientKind.HostLiveUI, window);
        simulationRouter = routingOwner.CreateInputRouter(InputRecipientKind.SimulationTimeline, window: null);
        routingOwner.AttachSimulationTimelineToWindow(window, simulationRouter);

        try
        {
            var focusGained = new PhysicalInputEvent(
                PhysicalInputEventKind.FocusGained,
                hostTimestamp: 10,
                hostSequence: sequenceLease.Allocate(),
                window: window);
            var reconciliation = new PhysicalInputEvent(
                PhysicalInputEventKind.StateReconciliation,
                hostTimestamp: 10,
                hostSequence: sequenceLease.Allocate(),
                window: window,
                reconciliationState: []);
            routingOwner.RouteCapturedInput(new PlatformInputCaptureBatch([focusGained, reconciliation], 0, []));

            var reconciledDiagnostics = diagnostics.GetSnapshot(
                new DiagnosticQuery(128, DiagnosticSeverity.Warning, worldGuid: null, minimumTick: null));
            Assert.That(
                reconciledDiagnostics.Records.Any(static diagnostic => diagnostic.Code == "input-state-reconciliation-required"),
                Is.False);

            var missingSnapshotFocus = new PhysicalInputEvent(
                PhysicalInputEventKind.FocusGained,
                hostTimestamp: 20,
                hostSequence: sequenceLease.Allocate(),
                window: window);
            routingOwner.RouteCapturedInput(new PlatformInputCaptureBatch([missingSnapshotFocus], 0, []));
            var missingDiagnostics = diagnostics.GetSnapshot(
                new DiagnosticQuery(128, DiagnosticSeverity.Warning, worldGuid: null, minimumTick: null));
            Assert.That(
                missingDiagnostics.Records.Any(static diagnostic => diagnostic.Code == "input-state-reconciliation-required"),
                Is.True);
        }
        finally
        {
            routingOwner.ClearRoutes();

            foreach (var router in inputRouters)
            {
                _ = router.DisposeAndGetTransitions();
            }
        }
    }

    [Test]
    public void ReassignsGamepadWithFreshStateOnlyForTheNewTimeline()
    {
        var ownerThreadId = Environment.CurrentManagedThreadId;
        var ownerId = OwnerIdAllocator.Allocate();
        var sequenceLease = new HostEventSequenceLease();
        var platformOwner = new TestPlatformInputRoutingHost(ownerId) {
            SequenceLease = sequenceLease,
        };
        var firstRouter = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            OwnerIdAllocator.Allocate(),
            ownerThreadId,
            sequenceLease);
        var secondRouter = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            OwnerIdAllocator.Allocate(),
            ownerThreadId,
            sequenceLease);
        var firstSegment = new SimulationClockSegment(1, 0, sequenceLease.Allocate(), 0, 0.0, 60, 0, 1.0f, isPaused: false);
        var secondSegment = new SimulationClockSegment(1, 0, sequenceLease.Allocate(), 0, 0.0, 60, 0, 1.0f, isPaused: false);
        firstRouter.SetSimulationClockSegment(in firstSegment);
        secondRouter.SetSimulationClockSegment(in secondSegment);
        Assert.That(firstRouter.RegisterMap(CreateMap(playerSlot: 2)).Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(secondRouter.RegisterMap(CreateMap(playerSlot: 2)).Status, Is.EqualTo(InputMapStatus.Registered));
        var routersById = new Dictionary<uint, InputRouter> {
            [firstRouter.RecipientId] = firstRouter,
            [secondRouter.RecipientId] = secondRouter,
        };
        var inputRouters = new HashSet<InputRouter> { firstRouter, secondRouter };
        var routingOwner = new PlatformInputRoutingOwner(
            HostProfile.Interactive,
            platformOwner,
            sequenceLease,
            new DiagnosticsService(),
            ownerThreadId,
            inputRouters,
            [],
            recipientId => routersById.GetValueOrDefault(recipientId));
        var device = new InputDeviceHandle(ownerId, 4, 1);

        try
        {
            Assert.That(routingOwner.AssignDevice(device, firstRouter.RecipientId, playerSlot: 2).Status, Is.EqualTo(InputRoutingStatus.Applied));
            Assert.That(firstRouter.PublishSimulationActionSnapshot(0).Actions[0].Transitions, Is.Empty);

            var release = CreateGamepadEvent(
                PhysicalInputEventKind.GamepadButtonUp,
                10,
                sequenceLease.Allocate(),
                device,
                isDown: false);
            routingOwner.RouteCapturedInput(new PlatformInputCaptureBatch([release], 0, []));
            Assert.That(firstRouter.PublishSimulationActionSnapshot(1).Actions[0].IsDownAtEnd, Is.False);

            platformOwner.HostTimestamp = 20;
            var press = CreateGamepadEvent(
                PhysicalInputEventKind.GamepadButtonDown,
                20,
                sequenceLease.Allocate(),
                device,
                isDown: true);
            routingOwner.RouteCapturedInput(new PlatformInputCaptureBatch([press], 0, []));
            Assert.That(firstRouter.PublishSimulationActionSnapshot(2).Actions[0].IsDownAtEnd, Is.True);

            platformOwner.HostTimestamp = 30;
            Assert.That(routingOwner.AssignDevice(device, secondRouter.RecipientId, playerSlot: 2).Status, Is.EqualTo(InputRoutingStatus.Applied));
            var firstTimeline = firstRouter.PublishSimulationActionSnapshot(3).Actions[0];
            var secondTimeline = secondRouter.PublishSimulationActionSnapshot(0).Actions[0];

            Assert.Multiple(() => {
                Assert.That(firstTimeline.IsDownAtEnd, Is.False);
                Assert.That(firstTimeline.Transitions, Has.Length.EqualTo(1));
                Assert.That(firstTimeline.Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Canceled));
                Assert.That(firstTimeline.Transitions[0].CancellationReason, Is.EqualTo(InputCancellationReason.DeviceRemoved));
                Assert.That(secondTimeline.IsDownAtEnd, Is.False);
                Assert.That(secondTimeline.Transitions, Is.Empty);
            });
        }
        finally
        {
            routingOwner.ClearRoutes();
            _ = firstRouter.DisposeAndGetTransitions();
            _ = secondRouter.DisposeAndGetTransitions();
        }
    }

    [Test]
    public void ReconcilesWindowAndUnassignedGamepadScopesBeforeResuming()
    {
        using var world = CreateWorld();
        var ownerThreadId = Environment.CurrentManagedThreadId;
        var ownerId = OwnerIdAllocator.Allocate();
        var sequenceLease = new HostEventSequenceLease();
        var platformOwner = new TestPlatformInputRoutingHost(ownerId) {
            HostTimestamp = 0,
            SequenceLease = sequenceLease,
        };
        var inputRouters = new HashSet<InputRouter>();
        var timelineRouters = new Dictionary<InputRouter, WorldInputTimelineAdapter>();
        InputRouter? simulationRouter = null;
        var routingOwner = new PlatformInputRoutingOwner(
            HostProfile.Interactive,
            platformOwner,
            sequenceLease,
            new DiagnosticsService(),
            ownerThreadId,
            inputRouters,
            timelineRouters,
            recipientId => (simulationRouter?.RecipientId == recipientId) ? simulationRouter : null);
        var window = new WindowHandle(ownerId, 0, 1);
        var binding = routingOwner.BindGameplayInput(window, world, worldIsHosted: true);
        var boundRouter = binding.Router
            ?? throw new InvalidOperationException("The gameplay input route did not return its timeline router.");
        simulationRouter = boundRouter;
        Assert.That(binding.Status, Is.EqualTo(InputBindingStatus.Bound));
        Assert.That(boundRouter.RegisterMap(CreateKeyboardMap(new KeyboardControl(7, 4))).Status, Is.EqualTo(InputMapStatus.Registered));
        Assert.That(boundRouter.RegisterMap(CreateMap(playerSlot: 2)).Status, Is.EqualTo(InputMapStatus.Registered));

        var adapter = timelineRouters[boundRouter];

        try
        {
            Assert.That(adapter.PauseHeadlessInput(10, out _, out _).Status, Is.EqualTo(InputRoutingStatus.Applied));
            platformOwner.HostTimestamp = 20;
            Assert.That(routingOwner.BeginPhysicalInputReconciliationAtBoundary(adapter, 20), Is.True);

            routingOwner.ServiceSimulationInputRoutes(adapter);

            Assert.That(boundRouter.IsPaused, Is.False);
            Assert.That(boundRouter.GetUnreconciledGamepadPlayerSlots(), Is.Empty);
            Assert.That(adapter.CompletePhysicalInputReconciliationAtBoundary(20), Is.True);
            Assert.That(platformOwner.ReconciliationRequests, Is.EqualTo(new WindowHandle?[] { window }));
        }
        finally
        {
            routingOwner.ClearRoutes();

            foreach (var timelineAdapter in timelineRouters.Values)
            {
                _ = timelineAdapter.DisposeAndGetTransitions();
            }

            foreach (var router in inputRouters)
            {
                _ = router.DisposeAndGetTransitions();
            }
        }
    }

    [Test]
    public void ResetsTheGameplayDispositionAfterHostUiOverflow()
    {
        var ownerThreadId = Environment.CurrentManagedThreadId;
        var ownerId = OwnerIdAllocator.Allocate();
        var platformOwner = new TestPlatformInputRoutingHost(ownerId);
        var sequenceLease = new HostEventSequenceLease();
        var window = new WindowHandle(ownerId, 0, 1);
        var device = new InputDeviceHandle(ownerId, 3, 1);
        var key = new KeyboardControl(7, 4);
        HashSet<InputRouter> inputRouters = [];
        var routingOwner = new PlatformInputRoutingOwner(
            HostProfile.Interactive,
            platformOwner,
            sequenceLease,
            new DiagnosticsService(),
            ownerThreadId,
            inputRouters,
            [],
            static _ => null);
        var hostRouter = routingOwner.CreateInputRouter(InputRecipientKind.HostLiveUI, window);
        var simulationRouter = routingOwner.CreateInputRouter(InputRecipientKind.SimulationTimeline, window: null);
        var actionMap = CreateKeyboardMap(key);
        Assert.That(simulationRouter.RegisterMap(actionMap).Status, Is.EqualTo(InputMapStatus.Registered));
        var segmentSequence = sequenceLease.Allocate();
        var segment = new SimulationClockSegment(1, 0, segmentSequence, 0, 0.0, 60, 0, 1.0f, isPaused: false);
        simulationRouter.SetSimulationClockSegment(in segment);
        routingOwner.AttachSimulationTimelineToWindow(window, simulationRouter);

        try
        {
            var keyDown = CreateKeyboardEvent(
                PhysicalInputEventKind.KeyDown,
                166_666,
                sequenceLease.Allocate(),
                device,
                window,
                key,
                isDown: true);
            Assert.That(simulationRouter.EnqueuePhysicalEvent(keyDown, null, InputClass.None, out _).Status, Is.EqualTo(InputRoutingStatus.Applied));
            Assert.That(simulationRouter.PublishSimulationActionSnapshot(0).Actions[0].IsDownAtEnd, Is.True);

            var capturedRepeat = CreateKeyboardEvent(
                PhysicalInputEventKind.KeyRepeat,
                333_332,
                sequenceLease.Allocate(),
                device,
                window,
                key,
                isDown: true);
            Assert.That(simulationRouter.EnqueuePhysicalEvent(capturedRepeat, null, InputClass.Keyboard, out _).Status, Is.EqualTo(InputRoutingStatus.Applied));
            Assert.That(simulationRouter.PublishSimulationActionSnapshot(1).Actions[0].IsDownAtEnd, Is.False);

            var overflowed = false;

            for (var index = 0; index <= 4096; index++)
            {
                var hostEvent = CreateKeyboardEvent(
                    PhysicalInputEventKind.KeyDown,
                    400_000,
                    sequenceLease.Allocate(),
                    device,
                    window,
                    key,
                    isDown: true);
                var receipt = hostRouter.EnqueueHostPhysicalEvent(hostEvent, out _);

                if (receipt.Status == InputRoutingStatus.CapacityExceeded)
                {
                    overflowed = true;
                    break;
                }
            }

            Assert.That(overflowed, Is.True);
            routingOwner.ServiceWindowInputRoutes(500_000);
            Assert.That(simulationRouter.ServiceInputControlBoundary(), Is.Empty);

            var keyUp = CreateKeyboardEvent(
                PhysicalInputEventKind.KeyUp,
                500_000,
                sequenceLease.Allocate(),
                device,
                window,
                key,
                isDown: false);
            Assert.That(simulationRouter.EnqueuePhysicalEvent(keyUp, null, InputClass.None, out _).Status, Is.EqualTo(InputRoutingStatus.Applied));
            Assert.That(simulationRouter.PublishSimulationActionSnapshot(2).Actions[0].Transitions, Is.Empty);

            var rearmedDown = CreateKeyboardEvent(
                PhysicalInputEventKind.KeyDown,
                666_664,
                sequenceLease.Allocate(),
                device,
                window,
                key,
                isDown: true);
            Assert.That(simulationRouter.EnqueuePhysicalEvent(rearmedDown, null, InputClass.None, out _).Status, Is.EqualTo(InputRoutingStatus.Applied));
            var rearmed = simulationRouter.PublishSimulationActionSnapshot(3).Actions[0];
            Assert.That(rearmed.IsDownAtEnd, Is.True);
            Assert.That(rearmed.Transitions, Has.Length.EqualTo(1));
            Assert.That(rearmed.Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Pressed));
        }
        finally
        {
            routingOwner.ClearRoutes();
            _ = hostRouter.DisposeAndGetTransitions();
            _ = simulationRouter.DisposeAndGetTransitions();
        }
    }

    [Test]
    public void RoutesCommittedPlatformTextToActiveHostLiveUiSession()
    {
        var ownerThreadId = Environment.CurrentManagedThreadId;
        var ownerId = OwnerIdAllocator.Allocate();
        var sequenceLease = new HostEventSequenceLease();
        var platformOwner = new TestPlatformInputRoutingHost(ownerId) {
            SequenceLease = sequenceLease,
        };
        var window = new WindowHandle(ownerId, 0, 1);
        var inputRouters = new HashSet<InputRouter>();
        var timelineRouters = new Dictionary<InputRouter, WorldInputTimelineAdapter>();
        var routingOwner = new PlatformInputRoutingOwner(
            HostProfile.Interactive,
            platformOwner,
            sequenceLease,
            new DiagnosticsService(),
            ownerThreadId,
            inputRouters,
            timelineRouters,
            recipientId => timelineRouters.Keys.FirstOrDefault(router => router.RecipientId == recipientId));
        TextInputSession? session = null;

        try
        {
            var hostRouter = routingOwner.CreateInputRouter(InputRecipientKind.HostLiveUI, window);
            session = hostRouter.BeginTextSession(new TextInputSessionOptions(
                window,
                TextInputPurpose.FreeForm,
                documentRevision: 1,
                surroundingText: null,
                selectionStart: 0,
                selectionLength: 0,
                caretPosition: default,
                caretSize: default));
            var device = new InputDeviceHandle(ownerId, 1, 1);
            var key = new KeyboardControl(usagePage: 0x0007, usage: 0x0008);
            var keyDown = CreateKeyboardEvent(
                PhysicalInputEventKind.KeyDown,
                hostTimestamp: 10,
                hostSequence: sequenceLease.Allocate(),
                device,
                window,
                key,
                isDown: true);
            var keyRepeat = CreateKeyboardEvent(
                PhysicalInputEventKind.KeyRepeat,
                hostTimestamp: 11,
                hostSequence: sequenceLease.Allocate(),
                device,
                window,
                key,
                isDown: true);
            var textSequence = sequenceLease.Allocate();

            routingOwner.RouteCapturedInput(new PlatformInputCaptureBatch(
                [keyDown, keyRepeat],
                DiscardedSampleCount: 0,
                Diagnostics: [],
                TextEvents: [
                    new PlatformTextInputEvent(window, 11, textSequence, ["é", "ß"]),
                ]));

            Assert.That(session.TryReadBatch(out var batch), Is.True);
            Assert.That(batch!.HostSequence, Is.EqualTo(textSequence));
            Assert.That(batch.BaseDocumentRevision, Is.EqualTo(1));
            Assert.That(batch.ResultDocumentRevision, Is.EqualTo(2));
            Assert.That(batch.Changes, Has.Length.EqualTo(2));
            Assert.That(batch.Changes[0].Kind, Is.EqualTo(TextInputChangeKind.TextCommitted));
            Assert.That(batch.Changes[0].Text, Is.EqualTo("é"));
            Assert.That(batch.Changes[1].Kind, Is.EqualTo(TextInputChangeKind.TextCommitted));
            Assert.That(batch.Changes[1].Text, Is.EqualTo("ß"));
        }
        finally
        {
            session?.Dispose();
            routingOwner.ClearRoutes();

            foreach (var router in inputRouters)
            {
                _ = router.DisposeAndGetTransitions();
            }

            sequenceLease.Revoke();
        }
    }

    [Test]
    public void RoutesNativeX11CaptureToHostLiveUiAndBoundGameplayTimeline()
    {
        if (!OperatingSystem.IsLinux()
            || (Environment.GetEnvironmentVariable("TERRAFX_TEST_ISOLATED_X11") != "1")
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            Assert.Ignore("The X11 input-fanout witness requires an isolated Linux X11 display.");
            return;
        }

        if (!NativeLibrary.TryLoad("libXtst.so.6", out var xtstLibrary))
        {
            Assert.Ignore("The isolated X11 input-fanout witness requires libXtst.");
            return;
        }

        NativeLibrary.Free(xtstLibrary);

        var ownerThreadId = Environment.CurrentManagedThreadId;
        var ownerId = OwnerIdAllocator.Allocate();
        var sequenceLease = new HostEventSequenceLease();
        var window = new WindowHandle(ownerId, 0, 1);
        NativeX11InputRoutingHost? platformHost = null;
        PlatformInputRoutingOwner? routingOwner = null;
        World? world = null;
        InputRouter? hostLiveUiRouter = null;
        HashSet<InputRouter> inputRouters = [];
        Dictionary<InputRouter, WorldInputTimelineAdapter> timelineRouters = [];

        try
        {
            try
            {
                platformHost = new NativeX11InputRoutingHost(ownerId, sequenceLease);
            }
            catch (DllNotFoundException exception)
            {
                Assert.Ignore($"The X11/XTest input libraries are unavailable: {exception.Message}");
                return;
            }
            catch (PlatformNotSupportedException exception)
            {
                Assert.Ignore($"The isolated X11 display is unavailable: {exception.Message}");
                return;
            }

            platformHost.CreateWindow(window);
            var diagnostics = new DiagnosticsService();
            routingOwner = new PlatformInputRoutingOwner(
                HostProfile.Interactive,
                platformHost,
                sequenceLease,
                diagnostics,
                ownerThreadId,
                inputRouters,
                timelineRouters,
                recipientId => timelineRouters.Keys.FirstOrDefault(router => router.RecipientId == recipientId));
            world = World.Create(new WorldOptions(
                new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build()) {
                CpuWorkerCount = 0,
                InitialReplayLineageId = Guid.NewGuid(),
                TickRate = 1,
                WorldGuid = Guid.NewGuid(),
            });

            var binding = routingOwner.BindGameplayInput(window, world, worldIsHosted: true);
            Assert.That(binding.Status, Is.EqualTo(InputBindingStatus.Bound));
            var simulationRouter = binding.Router
                ?? throw new InvalidOperationException("The native-window input binding omitted its SimulationTimeline router.");
            var key = new KeyboardControl(usagePage: 0x0007, usage: 0x0004);
            Assert.That(simulationRouter.RegisterMap(CreateKeyboardMap(key)).Status, Is.EqualTo(InputMapStatus.Registered));
            hostLiveUiRouter = routingOwner.CreateInputRouter(InputRecipientKind.HostLiveUI, window);
            var hostMapResponse = hostLiveUiRouter.RegisterMap(CreateKeyboardMap(key));
            Assert.That(hostMapResponse.Status, Is.EqualTo(InputMapStatus.Registered));
            var timelineAdapter = timelineRouters[simulationRouter];

            IsolatedX11Input.SetInputFocus(platformHost.NativeDisplay, platformHost.NativeWindow);
            routingOwner.RouteCapturedInput(platformHost.PumpAndCapture());

            IsolatedX11Input.SendKey(platformHost.NativeDisplay, keycode: 38, isDown: true);
            var initialKeyDownCapture = platformHost.PumpAndCapture();
            var initialKeyDown = initialKeyDownCapture.Events.Single(inputEvent => inputEvent.Kind == PhysicalInputEventKind.KeyDown);
            Assert.That(initialKeyDown.Window, Is.EqualTo(window));
            Assert.That(initialKeyDown.Value?.DigitalControl?.Keyboard?.Kind, Is.EqualTo(KeyboardControlKind.KeyboardHid));
            Assert.That(initialKeyDown.Value?.DigitalControl?.Keyboard?.UsagePage, Is.EqualTo(key.UsagePage));
            Assert.That(initialKeyDown.Value?.DigitalControl?.Keyboard?.Usage, Is.EqualTo(key.Usage));
            routingOwner.RouteCapturedInput(initialKeyDownCapture);
            routingOwner.ServiceWindowInputRoutes(platformHost.GetHostTimestamp());
            var initialHostSnapshot = hostLiveUiRouter.GetHostActionSnapshot();
            Assert.That(initialHostSnapshot.Actions[0].IsDownAtEnd, Is.True);

            IsolatedX11Input.SetInputFocus(platformHost.NativeDisplay, IsolatedX11Input.GetRootWindow(platformHost.NativeDisplay));
            var focusLostCapture = platformHost.PumpAndCapture();
            Assert.That(
                focusLostCapture.Events.Any(inputEvent => inputEvent.Kind == PhysicalInputEventKind.FocusLost),
                Is.True);
            routingOwner.RouteCapturedInput(focusLostCapture);
            routingOwner.ServiceWindowInputRoutes(platformHost.GetHostTimestamp());
            var focusLostHostSnapshot = hostLiveUiRouter.GetHostActionSnapshot();
            Assert.That(focusLostHostSnapshot.Actions[0].IsDownAtEnd, Is.False);
            Assert.That(
                focusLostHostSnapshot.Actions[0].Transitions.Any(transition => transition.Kind == InputTransitionKind.Canceled),
                Is.True);

            IsolatedX11Input.SetInputFocus(platformHost.NativeDisplay, platformHost.NativeWindow);
            var focusRegainedCapture = platformHost.PumpAndCapture();
            Assert.That(
                focusRegainedCapture.Events.Any(inputEvent => inputEvent.Kind == PhysicalInputEventKind.FocusGained),
                Is.True);
            Assert.That(
                focusRegainedCapture.Events.Any(inputEvent => inputEvent.Kind == PhysicalInputEventKind.StateReconciliation),
                Is.True);
            var reconciliation = focusRegainedCapture.Events.Single(
                inputEvent => inputEvent.Kind == PhysicalInputEventKind.StateReconciliation);
            Assert.That(
                reconciliation.ReconciliationState.Any(value =>
                    (value.IsDown == true)
                    && (value.DigitalControl?.Keyboard?.UsagePage == key.UsagePage)
                    && (value.DigitalControl.Keyboard.Usage == key.Usage)),
                Is.True);
            routingOwner.RouteCapturedInput(focusRegainedCapture);
            routingOwner.ServiceWindowInputRoutes(platformHost.GetHostTimestamp());
            var heldReconciliationHostSnapshot = hostLiveUiRouter.GetHostActionSnapshot();
            Assert.That(heldReconciliationHostSnapshot.Actions[0].IsDownAtEnd, Is.False);
            Assert.That(heldReconciliationHostSnapshot.Actions[0].Transitions, Is.Empty);

            IsolatedX11Input.SendKey(platformHost.NativeDisplay, keycode: 38, isDown: false);
            var keyUpCapture = platformHost.PumpAndCapture();
            Assert.That(keyUpCapture.Events.Any(inputEvent => inputEvent.Kind == PhysicalInputEventKind.KeyUp), Is.True);
            routingOwner.RouteCapturedInput(keyUpCapture);
            routingOwner.ServiceWindowInputRoutes(platformHost.GetHostTimestamp());
            Assert.That(hostLiveUiRouter.GetHostActionSnapshot().Actions[0].IsDownAtEnd, Is.False);

            IsolatedX11Input.SendKey(platformHost.NativeDisplay, keycode: 38, isDown: true);
            var keyDownCapture = platformHost.PumpAndCapture();
            var keyDown = keyDownCapture.Events.Single(inputEvent => inputEvent.Kind == PhysicalInputEventKind.KeyDown);
            Assert.That(keyDown.Window, Is.EqualTo(window));
            routingOwner.RouteCapturedInput(keyDownCapture);
            routingOwner.ServiceWindowInputRoutes(platformHost.GetHostTimestamp());
            routingOwner.ServiceSimulationInputRoutes(timelineAdapter);
            var simulationSnapshot = timelineAdapter.PublishInputStage(world.NextTickIndex);
            var hostSnapshot = hostLiveUiRouter.GetHostActionSnapshot();

            Assert.Multiple(() => {
                Assert.That(hostSnapshot.Actions, Has.Length.EqualTo(1));
                Assert.That(hostSnapshot.Actions[0].IsDownAtEnd, Is.True);
                Assert.That(hostSnapshot.Actions[0].Transitions, Has.Length.EqualTo(1));
                Assert.That(hostSnapshot.Actions[0].Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Pressed));
                Assert.That(simulationSnapshot.Actions, Has.Length.EqualTo(1));
                Assert.That(simulationSnapshot.Actions[0].IsDownAtEnd, Is.True);
                Assert.That(simulationSnapshot.Actions[0].Transitions, Has.Length.EqualTo(1));
                Assert.That(simulationSnapshot.Actions[0].Transitions[0].Kind, Is.EqualTo(InputTransitionKind.Pressed));
            });

            IsolatedX11Input.SendKey(platformHost.NativeDisplay, keycode: 38, isDown: false);
            _ = platformHost.PumpAndCapture();
        }
        finally
        {
            routingOwner?.ClearRoutes();

            foreach (var adapter in timelineRouters.Values)
            {
                _ = adapter.DisposeAndGetTransitions();
            }

            foreach (var router in inputRouters)
            {
                _ = router.DisposeAndGetTransitions();
            }

            _ = hostLiveUiRouter?.DisposeAndGetTransitions();
            world?.Dispose();
            platformHost?.Dispose();
            sequenceLease.Revoke();
        }
    }

    private static partial class IsolatedX11Input
    {
        internal static nint GetRootWindow(nint display)
            => checked((nint)XRootWindow(display, XDefaultScreen(display)));

        internal static void SendKey(nint display, uint keycode, bool isDown)
        {
            if (XTestFakeKeyEvent(display, keycode, isDown ? 1 : 0, delay: 0) == 0)
            {
                throw new InvalidOperationException("The isolated X11 server rejected a synthetic key transition.");
            }

            _ = XSync(display, discard: 0);
        }

        internal static void SetInputFocus(nint display, nint window)
        {
            if (XSetInputFocus(display, checked((nuint)window), revertTo: 2, time: 0) == 0)
            {
                throw new InvalidOperationException("The isolated X11 server rejected the input-focus request.");
            }

            _ = XSync(display, discard: 0);
        }

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport("libX11.so.6", EntryPoint = "XDefaultScreen")]
        private static partial int XDefaultScreen(nint display);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport("libX11.so.6", EntryPoint = "XRootWindow")]
        private static partial nuint XRootWindow(nint display, int screen);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport("libX11.so.6", EntryPoint = "XSetInputFocus")]
        private static partial int XSetInputFocus(nint display, nuint focus, int revertTo, nuint time);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport("libX11.so.6", EntryPoint = "XSync")]
        private static partial int XSync(nint display, int discard);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport("libXtst.so.6", EntryPoint = "XTestFakeKeyEvent")]
        private static partial int XTestFakeKeyEvent(nint display, uint keycode, int isPress, nuint delay);
    }

    private sealed class NativeX11InputRoutingHost : IPlatformInputRoutingHost, IDisposable
    {
        private readonly IPlatformBackend _backend;
        private readonly PlatformInputBroker _inputBroker;
        private readonly HashSet<WindowHandle> _windows = [];

        internal NativeX11InputRoutingHost(uint ownerId, HostEventSequenceLease sequenceLease)
        {
            ArgumentOutOfRangeException.ThrowIfZero(ownerId);
            ArgumentNullException.ThrowIfNull(sequenceLease);
            OwnerId = ownerId;
            _backend = PlatformBackendFactory.Create(
                PlatformBackend.X11Only,
                ownerId,
                ClipboardServicePolicy.NotRequested);
            _inputBroker = new PlatformInputBroker(ownerId, sequenceLease: sequenceLease);
        }

        public uint OwnerId { get; }

        internal nint NativeDisplay
            => GetNativeWindowBinding(_windows.Single()).NativeDisplayOrInstance;

        internal nint NativeWindow
            => GetNativeWindowBinding(_windows.Single()).NativeWindowOrSurface;

        public void Dispose()
        {
            foreach (var window in _windows)
            {
                _backend.CloseWindow(window);
            }

            _windows.Clear();
            _backend.Dispose();
        }

        public long GetHostTimestamp()
            => _inputBroker.GetCurrentHostTimestamp();

        public PlatformNativeWindowBinding GetNativeWindowBinding(WindowHandle window)
        {
            if (!_backend.TryGetNativeWindowBinding(window, out var binding))
            {
                throw new InvalidOperationException("The isolated X11 host has no native binding for the window.");
            }

            return binding;
        }

        public bool IsActiveGamepadDevice(InputDeviceHandle device)
            => false;

        public PlatformInputCaptureBatch RequestGamepadStateReconciliation(InputDeviceHandle device)
        {
            _ = device;
            throw new NotSupportedException("The isolated X11 input-fanout witness does not provide gamepad capture.");
        }

        public PlatformInputCaptureBatch RequestInputStateReconciliation(WindowHandle? window)
        {
            _backend.RequestInputStateReconciliation(window);
            return CaptureDrainedSamples();
        }

        internal void CreateWindow(WindowHandle window)
        {
            _backend.CreateWindow(
                window,
                new WindowOptions("TerraFX isolated X11 input fanout") {
                    Visible = true,
                },
                nativeMonitorHandle: 0);

            if (!_windows.Add(window))
            {
                throw new InvalidOperationException("The isolated X11 host registered a duplicate window.");
            }
        }

        internal PlatformInputCaptureBatch PumpAndCapture()
        {
            _backend.Pump();
            return CaptureDrainedSamples();
        }

        private PlatformInputCaptureBatch CaptureDrainedSamples()
            => _inputBroker.Capture(
                _backend.DrainInputSamples(),
                _windows.Contains);
    }

    private sealed class TestPlatformInputRoutingHost(uint ownerId) : IPlatformInputRoutingHost
    {
        public uint OwnerId { get; } = ownerId;

        internal List<InputDeviceHandle> GamepadReconciliationRequests { get; } = [];

        internal long HostTimestamp { get; set; }

        internal List<WindowHandle?> ReconciliationRequests { get; } = [];

        internal HostEventSequenceLease? SequenceLease { get; set; }

        public long GetHostTimestamp() => HostTimestamp;

        public PlatformNativeWindowBinding GetNativeWindowBinding(WindowHandle window)
            => default;

        public bool IsActiveGamepadDevice(InputDeviceHandle device)
            => (device.OwnerId == OwnerId) && device.IsValid;

        public PlatformInputCaptureBatch RequestGamepadStateReconciliation(InputDeviceHandle device)
        {
            GamepadReconciliationRequests.Add(device);

            if (SequenceLease is not { } sequenceLease)
            {
                return new PlatformInputCaptureBatch([], 0, []);
            }

            return new PlatformInputCaptureBatch(
                [
                    new PhysicalInputEvent(
                        PhysicalInputEventKind.StateReconciliation,
                        HostTimestamp,
                        sequenceLease.Allocate(),
                        device: device,
                        reconciliationState: [new PhysicalInputValue(GamepadButtonValue.South, isDown: true)]),
                ],
                0,
                []);
        }

        public PlatformInputCaptureBatch RequestInputStateReconciliation(WindowHandle? window)
        {
            ReconciliationRequests.Add(window);

            if ((window is not { } windowHandle) || (SequenceLease is not { } sequenceLease))
            {
                return new PlatformInputCaptureBatch([], 0, []);
            }

            return new PlatformInputCaptureBatch(
                [
                    new PhysicalInputEvent(
                        PhysicalInputEventKind.StateReconciliation,
                        HostTimestamp,
                        sequenceLease.Allocate(),
                        window: windowHandle,
                        reconciliationState: []),
                ],
                0,
                []);
        }
    }
}
