// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;
using TerraFX.Input;
using TerraFX.UI;

namespace TerraFX.Hosting;

internal sealed class PlatformWindowInputRoute
{
    private readonly InputRouter _hostLiveUiRouter;
    private readonly int _ownerThreadId;
    private readonly Func<PhysicalInputEvent, UiInputDisposition>? _routeUiInput;
    private InputRouter? _simulationTimelineRouter;
    private readonly WindowHandle _window;

    internal PlatformWindowInputRoute(
        WindowHandle window,
        InputRouter hostLiveUiRouter,
        Func<PhysicalInputEvent, UiInputDisposition>? routeUiInput = null)
    {
        if (!window.IsValid)
        {
            throw new ArgumentException("The window route must have a valid host-issued identity.", nameof(window));
        }

        ArgumentNullException.ThrowIfNull(hostLiveUiRouter);

        if ((hostLiveUiRouter.RecipientKind != InputRecipientKind.HostLiveUI)
            || (hostLiveUiRouter.Window != window))
        {
            throw new ArgumentException("The input router must be the HostLiveUI recipient for this exact window.", nameof(hostLiveUiRouter));
        }

        _ownerThreadId = Environment.CurrentManagedThreadId;
        _hostLiveUiRouter = hostLiveUiRouter;
        _routeUiInput = routeUiInput;
        _window = window;
    }

    internal bool HasSimulationTimeline
    {
        get
        {
            EnsureOwnerThread();
            return _simulationTimelineRouter is not null;
        }
    }

    internal InputRouter HostLiveUiRouter
    {
        get
        {
            EnsureOwnerThread();
            return _hostLiveUiRouter;
        }
    }

    internal WindowHandle Window
    {
        get
        {
            EnsureOwnerThread();
            return _window;
        }
    }

    private static bool IsHostActionInput(PhysicalInputEventKind kind)
        => kind is PhysicalInputEventKind.KeyDown
            or PhysicalInputEventKind.KeyRepeat
            or PhysicalInputEventKind.KeyUp
            or PhysicalInputEventKind.PointerButtonDown
            or PhysicalInputEventKind.PointerButtonUp
            or PhysicalInputEventKind.FocusLost;

    private static bool IsSimulationActionInput(PhysicalInputEventKind kind)
        => IsHostActionInput(kind) || (kind == PhysicalInputEventKind.StateReconciliation);

    internal void AttachSimulationTimeline(InputRouter router)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(router);

        if (router.RecipientKind != InputRecipientKind.SimulationTimeline)
        {
            throw new ArgumentException("The input router must be a SimulationTimeline recipient.", nameof(router));
        }

        if (_simulationTimelineRouter is not null)
        {
            throw new InvalidOperationException("The window already has a SimulationTimeline recipient.");
        }

        _simulationTimelineRouter = router;
    }

    internal void DetachSimulationTimeline(InputRouter router)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(router);

        if (!ReferenceEquals(_simulationTimelineRouter, router))
        {
            throw new InvalidOperationException("The SimulationTimeline router is not attached to this window.");
        }

        _simulationTimelineRouter = null;
    }

    internal PlatformWindowInputRoutingResult Route(PhysicalInputEvent inputEvent)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(inputEvent);

        if (inputEvent.Window != _window)
        {
            throw new ArgumentException("The physical input event belongs to another window route.", nameof(inputEvent));
        }

        var uiDisposition = _routeUiInput?.Invoke(inputEvent) ?? default;
        InputRoutingReceipt? hostReceipt = null;
        InputRoutingReceipt? simulationReceipt = null;
        var hostDiscardedInputCount = 0L;
        var simulationDiscardedInputCount = 0L;

        if (IsHostActionInput(inputEvent.Kind))
        {
            hostReceipt = _hostLiveUiRouter.EnqueueHostPhysicalEvent(inputEvent, out hostDiscardedInputCount);
        }

        if ((_simulationTimelineRouter is not null) && IsSimulationActionInput(inputEvent.Kind))
        {
            simulationReceipt = _simulationTimelineRouter.EnqueuePhysicalEvent(
                inputEvent,
                playerSlot: null,
                uiDisposition.CapturedClasses,
                out simulationDiscardedInputCount);
        }

        return new PlatformWindowInputRoutingResult(
            uiDisposition,
            hostReceipt,
            simulationReceipt,
            hostDiscardedInputCount,
            simulationDiscardedInputCount,
            inputEvent.Kind == PhysicalInputEventKind.FocusGained);
    }

    internal InputRouter.InputOverflowResult? ServiceHostControlBoundary(long hostTimestamp)
    {
        EnsureOwnerThread();
        _hostLiveUiRouter.UpdateHostControlTimestamp(hostTimestamp);
        var overflow = _hostLiveUiRouter.ServiceHostControlBoundary();
        _ = _hostLiveUiRouter.ServiceInputControlBoundary();
        return overflow;
    }

    private void EnsureOwnerThread()
    {
        if (_ownerThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("Window input routing is restricted to its host owner thread.");
        }
    }
}
