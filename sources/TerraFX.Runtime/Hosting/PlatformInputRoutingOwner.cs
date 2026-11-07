// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using TerraFX;
using TerraFX.Entities;
using TerraFX.Integration;
using TerraFX.Input;
using TerraFX.Platform;
using TerraFX.UI;

namespace TerraFX.Hosting;

internal sealed class PlatformInputRoutingOwner
{
    private readonly Dictionary<InputDeviceHandle, (InputRouter Router, ushort PlayerSlot)> _deviceAssignments = [];
    private readonly Dictionary<(uint RecipientId, ushort PlayerSlot), InputDeviceHandle> _devicesByTimelinePlayer = [];
    private readonly DiagnosticsService _diagnostics;
    private readonly Func<uint, InputRouter?> _findSimulationTimelineRouter;
    private readonly HashSet<InputRouter> _inputRouters;
    private readonly Dictionary<InputRouter, WorldInputTimelineAdapter> _inputTimelineByRouter;
    private readonly int _ownerThreadId;
    private readonly IPlatformInputRoutingHost _platformOwner;
    private readonly HostProfile _profile;
    private readonly HostEventSequenceLease _sequenceLease;
    private readonly Dictionary<WindowHandle, PlatformWindowInputRoute> _windowInputRoutes = [];

    internal PlatformInputRoutingOwner(
        HostProfile profile,
        IPlatformInputRoutingHost platformOwner,
        HostEventSequenceLease sequenceLease,
        DiagnosticsService diagnostics,
        int ownerThreadId,
        HashSet<InputRouter> inputRouters,
        Dictionary<InputRouter, WorldInputTimelineAdapter> inputTimelineByRouter,
        Func<uint, InputRouter?> findSimulationTimelineRouter)
    {
        ArgumentNullException.ThrowIfNull(platformOwner);
        ArgumentNullException.ThrowIfNull(sequenceLease);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentOutOfRangeException.ThrowIfLessThan(ownerThreadId, 1);
        ArgumentNullException.ThrowIfNull(inputRouters);
        ArgumentNullException.ThrowIfNull(inputTimelineByRouter);
        ArgumentNullException.ThrowIfNull(findSimulationTimelineRouter);

        if (!Enum.IsDefined(profile))
        {
            throw new ArgumentOutOfRangeException(nameof(profile));
        }

        _diagnostics = diagnostics;
        _findSimulationTimelineRouter = findSimulationTimelineRouter;
        _inputRouters = inputRouters;
        _inputTimelineByRouter = inputTimelineByRouter;
        _ownerThreadId = ownerThreadId;
        _platformOwner = platformOwner;
        _profile = profile;
        _sequenceLease = sequenceLease;
    }

    private static InputRoutingReceipt CreateDeviceAssignmentRejection(uint recipientId, string code, string path)
        => new(
            InputRoutingStatus.Rejected,
            recipientId,
            hostSequence: null,
            assignedTick: null,
            new ValidationIssue(code, path));

    private static InputBindingReceipt CreateRejectedInputBindingReceipt(
        WindowHandle window,
        World world,
        string code,
        string message)
    {
        var diagnostic = new Diagnostic(
            code,
            DiagnosticSeverity.Error,
            System.Diagnostics.Stopwatch.GetTimestamp(),
            hostId: null,
            worldGuid: world.WorldGuid,
            deviceId: null,
            surfaceId: null,
            deviceEpoch: null,
            surfaceEpoch: null,
            swapchainEpoch: null,
            tick: null,
            systemId: null,
            passId: null,
            assetId: null,
            commandIdentity: null,
            operation: "BindGameplayInput",
            nativeResult: null,
            message,
            context: []);
        return new InputBindingReceipt(InputBindingStatus.Rejected, router: null, window, world.WorldGuid, diagnostic);
    }

    internal InputRoutingReceipt AssignDevice(
        InputDeviceHandle device,
        uint simulationRecipientId,
        ushort playerSlot)
    {
        EnsureOwnerThread();

        if (_profile != HostProfile.Interactive)
        {
            return CreateDeviceAssignmentRejection(
                simulationRecipientId,
                "InteractiveInputRequiresInteractiveHost",
                "/RecipientKind");
        }

        if (!device.IsValid || (device.OwnerId != _platformOwner.OwnerId))
        {
            return CreateDeviceAssignmentRejection(simulationRecipientId, "InvalidDevice", "/Device");
        }

        if (!_platformOwner.IsActiveGamepadDevice(device))
        {
            return CreateDeviceAssignmentRejection(simulationRecipientId, "DeviceUnavailable", "/Device");
        }

        var targetRouter = _findSimulationTimelineRouter(simulationRecipientId);

        if (targetRouter is null)
        {
            return CreateDeviceAssignmentRejection(simulationRecipientId, "SimulationRecipientNotFound", "/RecipientId");
        }

        var targetRoute = (simulationRecipientId, playerSlot);

        if (_devicesByTimelinePlayer.TryGetValue(targetRoute, out var slotDevice) && (slotDevice != device))
        {
            return CreateDeviceAssignmentRejection(simulationRecipientId, "PlayerSlotAlreadyAssigned", "/PlayerSlot");
        }

        var hasPreviousAssignment = _deviceAssignments.TryGetValue(device, out var previousAssignment);

        var platformOwner = _platformOwner;
        var hostTimestamp = platformOwner.GetHostTimestamp();
        var isSameAssignment = hasPreviousAssignment
            && ReferenceEquals(previousAssignment.Router, targetRouter)
            && (previousAssignment.PlayerSlot == playerSlot);
        var hasPreviousControl = hasPreviousAssignment && !isSameAssignment && !previousAssignment.Router.IsPaused;
        var previousSequence = hasPreviousControl ? _sequenceLease.Allocate() : (long?)null;
        var assignmentSequence = _sequenceLease.Allocate();
        var sameTimeline = hasPreviousControl && ReferenceEquals(previousAssignment.Router, targetRouter);
        var requiredCommandCount = sameTimeline ? 2 : 1;

        if (hasPreviousControl)
        {
            var previousAdmission = previousAssignment.Router.ValidateHostControlBoundaryAdmission(
                hostTimestamp,
                previousSequence.GetValueOrDefault(),
                requiredCommandCount);

            if (previousAdmission.Status != InputRoutingStatus.Applied)
            {
                return previousAdmission;
            }
        }

        var assignmentAdmission = targetRouter.ValidateHostControlBoundaryAdmission(
            hostTimestamp,
            assignmentSequence,
            requiredCommandCount);

        if (assignmentAdmission.Status != InputRoutingStatus.Applied)
        {
            return assignmentAdmission;
        }

        var capturedInput = _platformOwner.RequestGamepadStateReconciliation(device);

        if (hasPreviousControl)
        {
            var removalReceipt = previousAssignment.Router.CancelInputState(
                InputCancellationReason.DeviceRemoved,
                InputClass.Gamepad,
                previousAssignment.PlayerSlot,
                hostTimestamp,
                previousSequence.GetValueOrDefault(),
                out _);

            if (removalReceipt.Status != InputRoutingStatus.Applied)
            {
                throw new InvalidOperationException("A prevalidated previous device route could not be retired.");
            }
        }

        var assignmentReceipt = targetRouter.CancelInputState(
            InputCancellationReason.DeviceRemoved,
            InputClass.Gamepad,
            playerSlot,
            hostTimestamp,
            assignmentSequence,
            out _);

        if (assignmentReceipt.Status != InputRoutingStatus.Applied)
        {
            throw new InvalidOperationException("A prevalidated device assignment could not be admitted.");
        }

        if (hasPreviousAssignment)
        {
            _ = _deviceAssignments.Remove(device);
            _ = _devicesByTimelinePlayer.Remove((previousAssignment.Router.RecipientId, previousAssignment.PlayerSlot));
        }

        _deviceAssignments.Add(device, (targetRouter, playerSlot));
        _devicesByTimelinePlayer.Add(targetRoute, device);
        RouteCapturedInput(capturedInput);
        return assignmentReceipt;
    }

    internal void AttachSimulationTimelineToWindow(WindowHandle window, InputRouter router)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(router);

        if (_profile != HostProfile.Interactive)
        {
            throw new NotSupportedException("A native window route requires an Interactive EngineHost.");
        }

        if ((router.RecipientKind != InputRecipientKind.SimulationTimeline) || !_inputRouters.Contains(router))
        {
            throw new ArgumentException("The input router must be a same-host SimulationTimeline recipient.", nameof(router));
        }

        GetOrCreateWindowInputRoute(window).AttachSimulationTimeline(router);
    }

    internal bool BeginPhysicalInputReconciliationAtBoundary(
        WorldInputTimelineAdapter adapter,
        long hostTimestamp)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(adapter);

        var router = adapter.Router;
        var windowRoute = FindWindowInputRoute(router);
        var hasAssignedGamepad = _deviceAssignments.Values.Any(
            assignment => ReferenceEquals(assignment.Router, router));

        if ((windowRoute is null) && !hasAssignedGamepad)
        {
            return false;
        }

        adapter.BeginPhysicalInputReconciliationAtBoundary(hostTimestamp);
        RequestPhysicalInputReconciliation(adapter, windowRoute, hostTimestamp);
        return true;
    }

    internal InputBindingReceipt BindGameplayInput(WindowHandle window, World world, bool worldIsHosted)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(world);

        if (_profile != HostProfile.Interactive)
        {
            return CreateRejectedInputBindingReceipt(
                window,
                world,
                "InteractiveInputRequiresInteractiveHost",
                "Window-bound gameplay input requires an Interactive EngineHost.");
        }

        if (!window.IsValid || (window.OwnerId != _platformOwner.OwnerId))
        {
            return CreateRejectedInputBindingReceipt(
                window,
                world,
                "WindowNotOwnedByHost",
                "The requested input window is not owned by this EngineHost.");
        }

        try
        {
            _ = _platformOwner.GetNativeWindowBinding(window);
        }
        catch (InvalidOperationException)
        {
            return CreateRejectedInputBindingReceipt(
                window,
                world,
                "WindowNotActive",
                "The requested input window is no longer active.");
        }

        if (!worldIsHosted)
        {
            return CreateRejectedInputBindingReceipt(
                window,
                world,
                "WorldNotOwnedByHost",
                "The World is not owned by this EngineHost.");
        }

        if (_inputTimelineByRouter.Values.Any(adapter => ReferenceEquals(adapter.World, world)))
        {
            return CreateRejectedInputBindingReceipt(
                window,
                world,
                "InputRouteAlreadyBound",
                "The World is already bound to a SimulationTimeline.");
        }

        var route = GetOrCreateWindowInputRoute(window);

        if (route.HasSimulationTimeline)
        {
            return CreateRejectedInputBindingReceipt(
                window,
                world,
                "InputRouteAlreadyBound",
                "The window already has a SimulationTimeline recipient.");
        }

        var router = CreateInputRouter(InputRecipientKind.SimulationTimeline, window: null);
        WorldInputTimelineAdapter? adapter = null;
        var routeAttached = false;

        try
        {
            adapter = new WorldInputTimelineAdapter(world, router, _sequenceLease);
            _ = adapter.BeginPumpBoundary(_platformOwner.GetHostTimestamp(), isPaused: false);
            _inputTimelineByRouter.Add(router, adapter);
            AttachSimulationTimelineToWindow(window, router);
            routeAttached = true;
            return new InputBindingReceipt(
                InputBindingStatus.Bound,
                router,
                window,
                world.WorldGuid,
                rejection: null);
        }
        catch (Exception exception)
        {
            if (routeAttached)
            {
                route.DetachSimulationTimeline(router);
            }

            _ = _inputTimelineByRouter.Remove(router);
            _ = _inputRouters.Remove(router);

            try
            {
                if (adapter is null)
                {
                    _ = router.DisposeAndGetTransitions();
                }
                else
                {
                    _ = adapter.DisposeAndGetTransitions();
                }
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(exception, cleanupException);
            }

            throw;
        }
    }

    internal void ClearRoutes()
    {
        EnsureOwnerThread();
        _windowInputRoutes.Clear();
        _deviceAssignments.Clear();
        _devicesByTimelinePlayer.Clear();
    }

    internal void ContinuePhysicalInputReconciliationAtBoundary(
        WorldInputTimelineAdapter adapter,
        long hostTimestamp)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(adapter);
        adapter.BeginPhysicalInputReconciliationPumpBoundary(hostTimestamp);
        RequestPhysicalInputReconciliation(adapter, FindWindowInputRoute(adapter.Router), hostTimestamp);
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A router whose host registration fails is disposed before the allocation failure is rethrown.")]
    internal InputRouter CreateInputRouter(InputRecipientKind recipientKind, WindowHandle? window)
    {
        EnsureOwnerThread();

        if (!Enum.IsDefined(recipientKind))
        {
            throw new ArgumentOutOfRangeException(nameof(recipientKind));
        }

        if (recipientKind is InputRecipientKind.HostLiveUI)
        {
            if (window is null)
            {
                throw new ArgumentException("A HostLiveUI recipient requires its exact host window.", nameof(window));
            }

            if (_profile != HostProfile.Interactive)
            {
                throw new NotSupportedException("A HostLiveUI recipient requires an Interactive EngineHost.");
            }

            var route = GetOrCreateWindowInputRoute(window.Value);
            return route.HostLiveUiRouter ?? CreateHostLiveUiRouter(route);
        }

        if (window.HasValue)
        {
            throw new ArgumentException("A SimulationTimeline recipient is not constructed with a physical window.", nameof(window));
        }

        var router = new InputRouter(
            InputRecipientKind.SimulationTimeline,
            OwnerIdAllocator.Allocate(),
            _ownerThreadId,
            _sequenceLease);

        try
        {
            _ = _inputRouters.Add(router);
            return router;
        }
        catch (Exception)
        {
            _ = router.DisposeAndGetTransitions();
            throw;
        }
    }

    internal ImmutableArray<InputActionTransition> DisposeInputRouter(InputRouter router)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(router);

        if (!_inputRouters.Contains(router))
        {
            throw new ArgumentException("The input router is not a live recipient owned by this host.", nameof(router));
        }

        var hostTimestamp = _platformOwner.GetHostTimestamp();
        router.UpdateHostControlTimestamp(hostTimestamp);

        if (router.RecipientKind is InputRecipientKind.HostLiveUI)
        {
            var window = router.Window
                ?? throw new InvalidOperationException("The HostLiveUI router has no window route.");

            if (!_windowInputRoutes.TryGetValue(window, out var route)
                || !ReferenceEquals(route.HostLiveUiRouter, router))
            {
                throw new InvalidOperationException("The HostLiveUI router is not attached to its window route.");
            }

            if (route.SimulationTimelineRouter is not null)
            {
                ResetUiDispositionForWindow(route, hostTimestamp);
            }

            var transitions = router.DisposeAndGetTransitions();
            route.DetachHostLiveUiRouter(router);

            if (route.IsEmpty)
            {
                _ = _windowInputRoutes.Remove(window);
            }

            _ = _inputRouters.Remove(router);
            return transitions;
        }

        var windowRoute = FindWindowInputRoute(router);
        var assignedDevices = GetDeviceAssignments(router);
        var disposedTransitions = _inputTimelineByRouter.TryGetValue(router, out var adapter)
            ? adapter.DisposeAndGetTransitions()
            : router.DisposeAndGetTransitions();
        windowRoute?.DetachSimulationTimeline(router);

        if (windowRoute is { IsEmpty: true } emptyRoute)
        {
            _ = _windowInputRoutes.Remove(emptyRoute.Window);
        }

        RemoveDeviceAssignments(router, assignedDevices);
        _ = _inputTimelineByRouter.Remove(router);
        _ = _inputRouters.Remove(router);
        return disposedTransitions;
    }

    internal void ResetUiDispositionForWindow(PlatformWindowInputRoute route, long hostTimestamp)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(route);

        if (route.SimulationTimelineRouter is not { } simulationRouter)
        {
            return;
        }

        var hostSequence = _sequenceLease.Allocate();
        var resetReceipt = simulationRouter.EnqueueUiDispositionReset(hostTimestamp, hostSequence);

        if (resetReceipt.Status == InputRoutingStatus.CapacityExceeded)
        {
            simulationRouter.LatchPlatformInputOverflow(hostTimestamp, hostSequence);
        }

        if (resetReceipt.Status != InputRoutingStatus.Applied)
        {
            WriteInputRoutingDiagnostic(route.Window, resetReceipt);
        }
    }

    internal void RouteCapturedInput(PlatformInputCaptureBatch capturedInput)
    {
        EnsureOwnerThread();

        if (_profile != HostProfile.Interactive)
        {
            if (!capturedInput.Events.IsEmpty || (capturedInput.DiscardedSampleCount != 0))
            {
                throw new InvalidOperationException("The selected non-interactive platform unexpectedly produced physical input.");
            }

            return;
        }

        if (capturedInput.DiscardedSampleCount != 0)
        {
            WritePlatformInputSampleDropDiagnostic(capturedInput.DiscardedSampleCount);
        }

        List<(WindowHandle Window, long HostSequence)>? focusReconciliationRequests = null;
        Dictionary<WindowHandle, long>? reconciledWindowSequences = null;

        foreach (var inputEvent in capturedInput.Events)
        {
            if (RouteDeviceInput(inputEvent))
            {
                continue;
            }

            if (inputEvent.Window is not { } window)
            {
                continue;
            }

            var route = GetOrCreateWindowInputRoute(window);
            var result = route.Route(inputEvent);

            if (result.HostRecipient is { Status: not InputRoutingStatus.Applied } hostReceipt)
            {
                WriteInputRoutingDiagnostic(window, hostReceipt);
            }

            if ((result.HostDiscardedInputCount != 0) && (result.HostRecipient is { } hostDiscardReceipt))
            {
                WriteDiscardedInputDiagnostic(
                    window.OwnerId,
                    window,
                    device: null,
                    hostDiscardReceipt.RecipientId,
                    inputEvent.HostSequence,
                    result.HostDiscardedInputCount,
                    inputEvent.Kind.ToString());
            }

            if (result.SimulationRecipient is { Status: not InputRoutingStatus.Applied } simulationReceipt)
            {
                WriteInputRoutingDiagnostic(window, simulationReceipt);
            }

            if ((result.SimulationDiscardedInputCount != 0) && (result.SimulationRecipient is { } simulationDiscardReceipt))
            {
                WriteDiscardedInputDiagnostic(
                    window.OwnerId,
                    window,
                    device: null,
                    simulationDiscardReceipt.RecipientId,
                    inputEvent.HostSequence,
                    result.SimulationDiscardedInputCount,
                    inputEvent.Kind.ToString());
            }

            if ((inputEvent.Kind == PhysicalInputEventKind.StateReconciliation)
                && (result.SimulationRecipient is { Status: InputRoutingStatus.Applied })
                && route.HasSimulationTimeline)
            {
                reconciledWindowSequences ??= [];
                reconciledWindowSequences[window] = inputEvent.HostSequence;
            }

            if (result.RequiresStateReconciliation && route.HasSimulationTimeline)
            {
                focusReconciliationRequests ??= [];
                focusReconciliationRequests.Add((window, inputEvent.HostSequence));
            }
        }

        if (focusReconciliationRequests is not null)
        {
            foreach (var (window, hostSequence) in focusReconciliationRequests)
            {
                if ((reconciledWindowSequences is null)
                    || !reconciledWindowSequences.TryGetValue(window, out var reconciliationSequence)
                    || (reconciliationSequence <= hostSequence))
                {
                    WriteInputDiagnostic(
                        "input-state-reconciliation-required",
                        DiagnosticSeverity.Warning,
                        window,
                        hostSequence,
                        "Focus returned to the window, but no current physical-state reconciliation sample is available. Held controls remain disarmed until a later neutral/up event.");
                }
            }
        }

        if (capturedInput.DiscardedSampleCount != 0)
        {
            LatchPlatformInputSampleLoss();
        }
    }

    internal void ServiceSimulationInputRoutes(WorldInputTimelineAdapter? adapter = null)
    {
        EnsureOwnerThread();

        if (adapter is not null)
        {
            ServiceSimulationInputRoute(adapter);
            return;
        }

        foreach (var timeline in _inputTimelineByRouter.Values)
        {
            ServiceSimulationInputRoute(timeline);
        }
    }

    internal void ServiceWindowInputRoutes(long hostTimestamp)
    {
        EnsureOwnerThread();

        foreach (var route in _windowInputRoutes.Values)
        {
            if ((route.HostLiveUiRouter is { } hostLiveUiRouter)
                && (route.ServiceHostControlBoundary(hostTimestamp) is { } overflow))
            {
                WriteInputOverflowDiagnostic(route.Window, hostLiveUiRouter.RecipientId, overflow);
                ResetUiDispositionForWindow(route, hostTimestamp);
            }
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A HostLiveUI router must be disposed if its window-route registration fails.")]
    private InputRouter CreateHostLiveUiRouter(PlatformWindowInputRoute route)
    {
        var router = new InputRouter(
            InputRecipientKind.HostLiveUI,
            OwnerIdAllocator.Allocate(),
            _ownerThreadId,
            _sequenceLease,
            route.Window);

        try
        {
            _ = _inputRouters.Add(router);
            route.AttachHostLiveUiRouter(router);
            return router;
        }
        catch (Exception)
        {
            _ = _inputRouters.Remove(router);
            _ = router.DisposeAndGetTransitions();
            throw;
        }
    }

    private void EnsureOwnerThread()
    {
        if (_ownerThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("Platform input routing is restricted to the EngineHost owner thread.");
        }
    }

    private PlatformWindowInputRoute? FindWindowInputRoute(InputRouter router)
    {
        foreach (var route in _windowInputRoutes.Values)
        {
            if (ReferenceEquals(route.SimulationTimelineRouter, router))
            {
                return route;
            }
        }

        return null;
    }

    private List<InputDeviceHandle> GetDeviceAssignments(InputRouter router)
    {
        var devices = new List<InputDeviceHandle>();

        foreach (var (device, assignment) in _deviceAssignments)
        {
            if (ReferenceEquals(assignment.Router, router))
            {
                devices.Add(device);
            }
        }

        return devices;
    }

    private PlatformWindowInputRoute GetOrCreateWindowInputRoute(WindowHandle window)
    {
        if (_windowInputRoutes.TryGetValue(window, out var existingRoute))
        {
            return existingRoute;
        }

        _ = _platformOwner.GetNativeWindowBinding(window);
        var route = new PlatformWindowInputRoute(window, hostLiveUiRouter: null);
        _windowInputRoutes.Add(window, route);
        return route;
    }

    private void LatchPlatformInputSampleLoss()
    {
        var recipients = new HashSet<InputRouter>();

        foreach (var route in _windowInputRoutes.Values)
        {
            if (route.HostLiveUiRouter is { } hostLiveUiRouter)
            {
                _ = recipients.Add(hostLiveUiRouter);
            }
        }

        foreach (var router in _inputTimelineByRouter.Keys)
        {
            _ = recipients.Add(router);
        }

        foreach (var (router, _) in _deviceAssignments.Values)
        {
            _ = recipients.Add(router);
        }

        if (recipients.Count == 0)
        {
            return;
        }

        var hostTimestamp = _platformOwner.GetHostTimestamp();
        var hostSequence = _sequenceLease.Allocate();

        foreach (var router in recipients)
        {
            router.LatchPlatformInputOverflow(hostTimestamp, hostSequence);
        }
    }

    private void ReconcileUnassignedGamepadPlayerSlot(
        WorldInputTimelineAdapter adapter,
        WindowHandle? window,
        ushort playerSlot,
        long hostTimestamp)
    {
        var receipt = adapter.ReconcileUnassignedGamepadPlayerSlotAtBoundary(
            playerSlot,
            hostTimestamp,
            out var discardedInputCount);
        ReportReconciliationAdmission(adapter, window, receipt, discardedInputCount, "unassigned gamepad slot");
    }

    private void ReconcileUnassignedWindowInput(WorldInputTimelineAdapter adapter, long hostTimestamp)
    {
        var receipt = adapter.ReconcileUnassignedWindowInputAtBoundary(
            hostTimestamp,
            out var discardedInputCount);
        ReportReconciliationAdmission(adapter, window: null, receipt, discardedInputCount, "unassigned window input");
    }

    private void RemoveDeviceAssignments(InputRouter router, List<InputDeviceHandle> devices)
    {
        foreach (var device in devices)
        {
            if (!_deviceAssignments.TryGetValue(device, out var assignment)
                || !ReferenceEquals(assignment.Router, router))
            {
                continue;
            }

            _ = _deviceAssignments.Remove(device);
            _ = _devicesByTimelinePlayer.Remove((router.RecipientId, assignment.PlayerSlot));
        }
    }

    private void ReportReconciliationAdmission(
        WorldInputTimelineAdapter adapter,
        WindowHandle? window,
        InputRoutingReceipt receipt,
        long discardedInputCount,
        string scope)
    {
        if (receipt.Status != InputRoutingStatus.Applied)
        {
            var severity = (receipt.Status == InputRoutingStatus.CapacityExceeded)
                ? DiagnosticSeverity.Warning
                : DiagnosticSeverity.Error;
            var reason = receipt.Rejection?.Code ?? receipt.Status.ToString();
            var world = adapter.World;
            _diagnostics.Write(
                new Diagnostic(
                    "input-state-reconciliation-rejected",
                    severity,
                    System.Diagnostics.Stopwatch.GetTimestamp(),
                    _platformOwner.OwnerId,
                    world.WorldGuid,
                    deviceId: null,
                    deviceEpoch: null,
                    surfaceId: null,
                    surfaceEpoch: null,
                    swapchainEpoch: null,
                    tick: null,
                    systemId: null,
                    passId: null,
                    assetId: null,
                    commandIdentity: receipt.HostSequence?.ToString(CultureInfo.InvariantCulture),
                    operation: "PlatformInputRoute.Reconcile",
                    nativeResult: null,
                    message: $"The {scope} reconciliation was not admitted: {reason}.",
                    context: [
                        new DiagnosticContextEntry("RecipientId", receipt.RecipientId.ToString(CultureInfo.InvariantCulture)),
                        new DiagnosticContextEntry("Reason", reason),
                    ]),
                executionSessionId: null,
                worldOwnerId: world.OwnerId);
            return;
        }

        if (discardedInputCount != 0)
        {
            WriteDiscardedInputDiagnostic(
                _platformOwner.OwnerId,
                window,
                device: null,
                receipt.RecipientId,
                receipt.HostSequence.GetValueOrDefault(),
                discardedInputCount,
                scope);
        }
    }

    private void RequestPhysicalInputReconciliation(
        WorldInputTimelineAdapter adapter,
        PlatformWindowInputRoute? windowRoute,
        long hostTimestamp)
    {
        var router = adapter.Router;

        if (windowRoute is null)
        {
            ReconcileUnassignedWindowInput(adapter, hostTimestamp);
        }

        foreach (var playerSlot in router.GetUnreconciledGamepadPlayerSlots())
        {
            if (!_devicesByTimelinePlayer.ContainsKey((router.RecipientId, playerSlot)))
            {
                ReconcileUnassignedGamepadPlayerSlot(adapter, windowRoute?.Window, playerSlot, hostTimestamp);
            }
        }

        var capturedInput = _platformOwner.RequestInputStateReconciliation(windowRoute?.Window);
        RouteCapturedInput(capturedInput);
    }

    private bool RouteDeviceInput(PhysicalInputEvent inputEvent)
    {
        if (inputEvent.Kind == PhysicalInputEventKind.DeviceAdded)
        {
            return true;
        }

        if (inputEvent.Device is not { } device)
        {
            return false;
        }

        if (inputEvent.Kind == PhysicalInputEventKind.DeviceRemoved)
        {
            if (_deviceAssignments.Remove(device, out var assignment))
            {
                _ = _devicesByTimelinePlayer.Remove((assignment.Router.RecipientId, assignment.PlayerSlot));

                if (!assignment.Router.IsPaused)
                {
                    var receipt = assignment.Router.EnqueuePhysicalEvent(
                        inputEvent,
                        assignment.PlayerSlot,
                        out var removedDiscardedInputCount);

                    if (receipt.Status != InputRoutingStatus.Applied)
                    {
                        if (receipt.Status == InputRoutingStatus.CapacityExceeded)
                        {
                            assignment.Router.LatchPlatformInputOverflow(inputEvent.HostTimestamp, inputEvent.HostSequence);
                        }

                        WriteDeviceInputRoutingDiagnostic(device, receipt);
                    }

                    if (removedDiscardedInputCount != 0)
                    {
                        WriteDiscardedInputDiagnostic(
                            device.OwnerId,
                            window: null,
                            device,
                            assignment.Router.RecipientId,
                            inputEvent.HostSequence,
                            removedDiscardedInputCount,
                            inputEvent.Kind.ToString());
                    }
                }
            }

            return true;
        }

        if (inputEvent.Kind is not PhysicalInputEventKind.GamepadButtonDown
            and not PhysicalInputEventKind.GamepadButtonUp
            and not PhysicalInputEventKind.GamepadAxis
            and not PhysicalInputEventKind.GamepadStick
            and not PhysicalInputEventKind.GamepadTrigger
            and not PhysicalInputEventKind.StateReconciliation)
        {
            return false;
        }

        if (!_deviceAssignments.TryGetValue(device, out var assignedRoute))
        {
            return true;
        }

        var routingReceipt = assignedRoute.Router.EnqueuePhysicalEvent(
            inputEvent,
            assignedRoute.PlayerSlot,
            InputClass.None,
            out var discardedInputCount);

        if (routingReceipt.Status != InputRoutingStatus.Applied)
        {
            WriteDeviceInputRoutingDiagnostic(device, routingReceipt);
        }

        if (discardedInputCount != 0)
        {
            WriteDiscardedInputDiagnostic(
                device.OwnerId,
                window: null,
                device,
                assignedRoute.Router.RecipientId,
                inputEvent.HostSequence,
                discardedInputCount,
                inputEvent.Kind.ToString());
        }

        return true;
    }

    private void ServiceSimulationInputRoute(WorldInputTimelineAdapter adapter)
    {
        if (adapter.ServiceHostControlBoundary() is { } overflow)
        {
            WriteSimulationInputOverflowDiagnostic(adapter, overflow);
        }
    }

    private void WriteDeviceInputRoutingDiagnostic(InputDeviceHandle device, InputRoutingReceipt receipt)
    {
        var code = (receipt.Status == InputRoutingStatus.CapacityExceeded)
            ? "input-overflow"
            : "input-route-rejected";
        var severity = (receipt.Status == InputRoutingStatus.CapacityExceeded)
            ? DiagnosticSeverity.Warning
            : DiagnosticSeverity.Error;
        var reason = receipt.Rejection?.Code ?? receipt.Status.ToString();
        _diagnostics.Write(
            new Diagnostic(
                code,
                severity,
                System.Diagnostics.Stopwatch.GetTimestamp(),
                device.OwnerId,
                worldGuid: null,
                deviceId: null,
                deviceEpoch: device.Generation,
                surfaceId: null,
                surfaceEpoch: null,
                swapchainEpoch: null,
                tick: receipt.AssignedTick,
                systemId: null,
                passId: null,
                assetId: null,
                commandIdentity: receipt.HostSequence?.ToString(CultureInfo.InvariantCulture),
                operation: "PlatformInputRoute.Gamepad",
                nativeResult: null,
                message: $"The {receipt.Status} result while routing device input: {reason}.",
                context: [
                    new DiagnosticContextEntry("DeviceIndex", device.Index.ToString(CultureInfo.InvariantCulture)),
                    new DiagnosticContextEntry("RecipientId", receipt.RecipientId.ToString(CultureInfo.InvariantCulture)),
                    new DiagnosticContextEntry("Reason", reason),
                ]),
            executionSessionId: null,
            worldOwnerId: null);
    }

    private void WriteDiscardedInputDiagnostic(
        uint hostId,
        WindowHandle? window,
        InputDeviceHandle? device,
        uint recipientId,
        long hostSequence,
        long discardedInputCount,
        string reason)
    {
        var context = ImmutableArray.CreateBuilder<DiagnosticContextEntry>();
        context.Add(new DiagnosticContextEntry("RecipientId", recipientId.ToString(CultureInfo.InvariantCulture)));
        context.Add(new DiagnosticContextEntry("DiscardedInputCount", discardedInputCount.ToString(CultureInfo.InvariantCulture)));

        if (window is { } windowHandle)
        {
            context.Add(new DiagnosticContextEntry("WindowIndex", windowHandle.Index.ToString(CultureInfo.InvariantCulture)));
            context.Add(new DiagnosticContextEntry("WindowGeneration", windowHandle.Generation.ToString(CultureInfo.InvariantCulture)));
        }

        if (device is { } deviceHandle)
        {
            context.Add(new DiagnosticContextEntry("DeviceIndex", deviceHandle.Index.ToString(CultureInfo.InvariantCulture)));
            context.Add(new DiagnosticContextEntry("DeviceGeneration", deviceHandle.Generation.ToString(CultureInfo.InvariantCulture)));
        }

        _diagnostics.Write(
            new Diagnostic(
                "platform-input-copies-discarded",
                DiagnosticSeverity.Warning,
                System.Diagnostics.Stopwatch.GetTimestamp(),
                hostId,
                worldGuid: null,
                deviceId: null,
                deviceEpoch: device?.Generation,
                surfaceId: null,
                surfaceEpoch: null,
                swapchainEpoch: null,
                tick: null,
                systemId: null,
                passId: null,
                assetId: null,
                commandIdentity: hostSequence.ToString(CultureInfo.InvariantCulture),
                operation: "PlatformInputRoute",
                nativeResult: null,
                message: $"{discardedInputCount} recipient-local input copies were discarded after {reason}.",
                context: context.ToImmutable()),
            executionSessionId: null,
            worldOwnerId: null);
    }

    private void WriteInputDiagnostic(
        string code,
        DiagnosticSeverity severity,
        WindowHandle window,
        long? hostSequence,
        string message,
        ImmutableArray<DiagnosticContextEntry> context = default)
    {
        var entries = context;

        if (entries.IsDefault)
        {
            entries = [
                new DiagnosticContextEntry("WindowIndex", window.Index.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticContextEntry("WindowGeneration", window.Generation.ToString(CultureInfo.InvariantCulture)),
            ];
        }

        _diagnostics.Write(
            new Diagnostic(
                code,
                severity,
                System.Diagnostics.Stopwatch.GetTimestamp(),
                window.OwnerId,
                worldGuid: null,
                deviceId: null,
                surfaceId: null,
                deviceEpoch: null,
                surfaceEpoch: null,
                swapchainEpoch: null,
                tick: null,
                systemId: null,
                passId: null,
                assetId: null,
                commandIdentity: hostSequence?.ToString(CultureInfo.InvariantCulture),
                operation: "PlatformInputRoute",
                nativeResult: null,
                message,
                entries),
            executionSessionId: null,
            worldOwnerId: null);
    }

    private void WriteInputOverflowDiagnostic(
        WindowHandle window,
        uint recipientId,
        InputRouter.InputOverflowResult overflow)
        => WriteInputDiagnostic(
            "input-overflow",
            DiagnosticSeverity.Warning,
            window,
            overflow.LastUnacceptedHostSequence,
            $"The HostLiveUI input recipient overflowed for host sequences {overflow.FirstUnacceptedHostSequence} through {overflow.LastUnacceptedHostSequence}; a fresh physical-state reconciliation is required.",
            [
                new DiagnosticContextEntry("RecipientId", recipientId.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticContextEntry("FirstUnacceptedHostSequence", overflow.FirstUnacceptedHostSequence.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticContextEntry("LastUnacceptedHostSequence", overflow.LastUnacceptedHostSequence.ToString(CultureInfo.InvariantCulture)),
            ]);

    private void WriteInputRoutingDiagnostic(WindowHandle window, InputRoutingReceipt receipt)
    {
        var code = (receipt.Status == InputRoutingStatus.CapacityExceeded)
            ? "input-overflow"
            : "input-route-rejected";
        var severity = (receipt.Status == InputRoutingStatus.CapacityExceeded)
            ? DiagnosticSeverity.Warning
            : DiagnosticSeverity.Error;
        var reason = receipt.Rejection?.Code ?? receipt.Status.ToString();
        var context = new DiagnosticContextEntry[] {
            new("RecipientId", receipt.RecipientId.ToString(CultureInfo.InvariantCulture)),
            new("HostSequence", receipt.HostSequence?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
            new("Reason", reason),
        };
        WriteInputDiagnostic(
            code,
            severity,
            window,
            receipt.HostSequence,
            $"The {receipt.Status} result while routing physical input: {reason}.",
            [.. context]);
    }

    private void WritePlatformInputSampleDropDiagnostic(long discardedSampleCount)
    {
        _diagnostics.Write(
            new Diagnostic(
                "platform-input-samples-discarded",
                DiagnosticSeverity.Warning,
                System.Diagnostics.Stopwatch.GetTimestamp(),
                _platformOwner.OwnerId,
                worldGuid: null,
                deviceId: null,
                surfaceId: null,
                deviceEpoch: null,
                surfaceEpoch: null,
                swapchainEpoch: null,
                tick: null,
                systemId: null,
                passId: null,
                assetId: null,
                commandIdentity: null,
                operation: "PlatformInputBroker.Capture",
                nativeResult: null,
                message: "Platform input samples were discarded before recipient routing.",
                context: [new DiagnosticContextEntry("DiscardedSampleCount", discardedSampleCount.ToString(CultureInfo.InvariantCulture))]),
            executionSessionId: null,
            worldOwnerId: null);
    }

    private void WriteSimulationInputOverflowDiagnostic(
        WorldInputTimelineAdapter adapter,
        InputRouter.InputOverflowResult overflow)
    {
        var world = adapter.World;
        _diagnostics.Write(
            new Diagnostic(
                "input-overflow",
                DiagnosticSeverity.Warning,
                System.Diagnostics.Stopwatch.GetTimestamp(),
                _platformOwner.OwnerId,
                world.WorldGuid,
                deviceId: null,
                deviceEpoch: null,
                surfaceId: null,
                surfaceEpoch: null,
                swapchainEpoch: null,
                tick: null,
                systemId: null,
                passId: null,
                assetId: null,
                commandIdentity: overflow.LastUnacceptedHostSequence.ToString(CultureInfo.InvariantCulture),
                operation: "InputRouter.ServiceHostControlBoundary",
                nativeResult: null,
                message: $"The SimulationTimeline input recipient overflowed for host sequences {overflow.FirstUnacceptedHostSequence} through {overflow.LastUnacceptedHostSequence}; a fresh physical-state reconciliation is required.",
                context: [
                    new DiagnosticContextEntry("RecipientId", adapter.Router.RecipientId.ToString(CultureInfo.InvariantCulture)),
                    new DiagnosticContextEntry("FirstUnacceptedHostSequence", overflow.FirstUnacceptedHostSequence.ToString(CultureInfo.InvariantCulture)),
                    new DiagnosticContextEntry("LastUnacceptedHostSequence", overflow.LastUnacceptedHostSequence.ToString(CultureInfo.InvariantCulture)),
                ]),
            executionSessionId: null,
            worldOwnerId: world.OwnerId);
    }
}
