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
    private readonly DiagnosticsService _diagnostics;
    private readonly HashSet<InputRouter> _inputRouters;
    private readonly Dictionary<InputRouter, WorldInputTimelineAdapter> _inputTimelineByRouter;
    private readonly int _ownerThreadId;
    private readonly PlatformHostOwner _platformOwner;
    private readonly HostProfile _profile;
    private readonly HostEventSequenceLease _sequenceLease;
    private readonly Dictionary<WindowHandle, PlatformWindowInputRoute> _windowInputRoutes = [];

    internal PlatformInputRoutingOwner(
        HostProfile profile,
        PlatformHostOwner platformOwner,
        HostEventSequenceLease sequenceLease,
        DiagnosticsService diagnostics,
        int ownerThreadId,
        HashSet<InputRouter> inputRouters,
        Dictionary<InputRouter, WorldInputTimelineAdapter> inputTimelineByRouter)
    {
        ArgumentNullException.ThrowIfNull(platformOwner);
        ArgumentNullException.ThrowIfNull(sequenceLease);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentOutOfRangeException.ThrowIfLessThan(ownerThreadId, 1);
        ArgumentNullException.ThrowIfNull(inputRouters);
        ArgumentNullException.ThrowIfNull(inputTimelineByRouter);

        if (!Enum.IsDefined(profile))
        {
            throw new ArgumentOutOfRangeException(nameof(profile));
        }

        _diagnostics = diagnostics;
        _inputRouters = inputRouters;
        _inputTimelineByRouter = inputTimelineByRouter;
        _ownerThreadId = ownerThreadId;
        _platformOwner = platformOwner;
        _profile = profile;
        _sequenceLease = sequenceLease;
    }

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
            route.AttachSimulationTimeline(router);
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

            return GetOrCreateWindowInputRoute(window.Value).HostLiveUiRouter;
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

        foreach (var inputEvent in capturedInput.Events)
        {
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

            if (result.SimulationRecipient is { Status: not InputRoutingStatus.Applied } simulationReceipt)
            {
                WriteInputRoutingDiagnostic(window, simulationReceipt);
            }

            if (result.RequiresStateReconciliation && route.HasSimulationTimeline)
            {
                WriteInputDiagnostic(
                    "input-state-reconciliation-required",
                    DiagnosticSeverity.Warning,
                    window,
                    inputEvent.HostSequence,
                    "Focus returned to the window, but no current physical-state reconciliation sample is available. Held controls remain disarmed until a later neutral/up event.");
            }
        }
    }

    internal void ServiceWindowInputRoutes(long hostTimestamp)
    {
        EnsureOwnerThread();

        foreach (var route in _windowInputRoutes.Values)
        {
            if (route.ServiceHostControlBoundary(hostTimestamp) is { } overflow)
            {
                WriteInputOverflowDiagnostic(route.Window, route.HostLiveUiRouter.RecipientId, overflow);
            }
        }
    }

    private void EnsureOwnerThread()
    {
        if (_ownerThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("Platform input routing is restricted to the EngineHost owner thread.");
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A HostLiveUI router must be disposed if its window-route registration fails.")]
    private PlatformWindowInputRoute GetOrCreateWindowInputRoute(WindowHandle window)
    {
        if (_windowInputRoutes.TryGetValue(window, out var existingRoute))
        {
            return existingRoute;
        }

        _ = _platformOwner.GetNativeWindowBinding(window);
        var router = new InputRouter(
            InputRecipientKind.HostLiveUI,
            OwnerIdAllocator.Allocate(),
            _ownerThreadId,
            _sequenceLease,
            window);

        try
        {
            _ = _inputRouters.Add(router);
            var route = new PlatformWindowInputRoute(window, router);
            _windowInputRoutes.Add(window, route);
            return route;
        }
        catch (Exception)
        {
            _ = _inputRouters.Remove(router);
            _ = router.DisposeAndGetTransitions();
            throw;
        }
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
}
