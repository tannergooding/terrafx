// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;
using TerraFX.Input;
using TerraFX.Numerics;

namespace TerraFX.Platform;

/// <summary>Provides owner-thread platform services owned by an engine host.</summary>
public sealed class PlatformHost
{
    internal const int MaximumWindowCount = 32;
    /// <summary>Occurs when a window's requested or natively observed mode changes.</summary>
    public event EventHandler<WindowModeChangedEventArgs>? WindowModeChanged;
    private readonly Func<InputDeviceHandle, uint, ushort, CancellationToken, Task<InputRoutingReceipt>>? _assignDeviceAsync;
    private readonly IPlatformBackend _backend;
    private readonly ClipboardServiceStatus _clipboardStatus;
    private readonly ClipboardTextService? _clipboardText;
    private readonly Func<WindowHandle, CancellationToken, Task<WindowCommandReceipt>>? _closeWindowAsync;
    private readonly List<Diagnostic> _pendingModeDiagnostics = [];
    private readonly List<Diagnostic> _pendingStartupDiagnostics = [];
    private readonly Func<WindowHandle, Vector2, CancellationToken, Task<WindowCommandReceipt>>? _setClientSizeAsync;
    private readonly Func<WindowHandle, WindowMode, CancellationToken, Task<WindowCommandReceipt>>? _setModeAsync;
    private readonly Func<WindowHandle, string, CancellationToken, Task<WindowCommandReceipt>>? _setTitleAsync;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<WindowHandle, WindowMetrics> _windowMetrics = [];
    private readonly Dictionary<WindowHandle, WindowModeState> _windowModeStates = [];

    /// <summary>Creates one platform owner from a Runtime-issued identity and explicit service policy.</summary>
    /// <param name="ownerId">The nonzero identity issued by the Runtime host owner.</param>
    /// <param name="backend">The explicitly selected platform backend.</param>
    /// <param name="clipboardPolicy">The host's clipboard service admission policy.</param>
    /// <param name="timeProvider">The host's private monotonic timestamp source.</param>
    /// <param name="clipboardAllowed">Whether this host profile permits clipboard text.</param>
    /// <param name="assignDeviceAsync">The Runtime-owned route for host-bound gamepad assignment.</param>
    /// <param name="setClientSizeAsync">The Runtime-owned route for window client-size commands.</param>
    /// <param name="setTitleAsync">The Runtime-owned route for window title commands.</param>
    /// <param name="closeWindowAsync">The Runtime-owned route for window close commands.</param>
    /// <param name="setModeAsync">The Runtime-owned route for window presentation-mode commands.</param>
    internal PlatformHost(
        uint ownerId,
        PlatformBackend backend,
        ClipboardServicePolicy clipboardPolicy,
        TimeProvider? timeProvider = null,
        bool clipboardAllowed = true,
        Func<InputDeviceHandle, uint, ushort, CancellationToken, Task<InputRoutingReceipt>>? assignDeviceAsync = null,
        Func<WindowHandle, Vector2, CancellationToken, Task<WindowCommandReceipt>>? setClientSizeAsync = null,
        Func<WindowHandle, string, CancellationToken, Task<WindowCommandReceipt>>? setTitleAsync = null,
        Func<WindowHandle, CancellationToken, Task<WindowCommandReceipt>>? closeWindowAsync = null,
        Func<WindowHandle, WindowMode, CancellationToken, Task<WindowCommandReceipt>>? setModeAsync = null)
    {
        if (!clipboardAllowed && (clipboardPolicy is ClipboardServicePolicy.Required))
        {
            throw new NotSupportedException("The selected host profile does not provide clipboard text.");
        }

        var backendClipboardPolicy = clipboardAllowed ? clipboardPolicy : ClipboardServicePolicy.NotRequested;
        _backend = PlatformBackendFactory.Create(
            backend,
            ownerId,
            backendClipboardPolicy,
            out var startupDiagnostic,
            timeProvider);

        if (startupDiagnostic is not null)
        {
            _pendingStartupDiagnostics.Add(startupDiagnostic);
        }

        _timeProvider = timeProvider ?? TimeProvider.System;
        _clipboardStatus = (clipboardPolicy is ClipboardServicePolicy.NotRequested)
            ? ClipboardServiceStatus.NotRequested
            : (clipboardAllowed ? _backend.ClipboardStatus : ClipboardServiceStatus.Unsupported);
        _clipboardText = (clipboardPolicy is ClipboardServicePolicy.NotRequested)
            ? null
            : new ClipboardTextService(_backend, _clipboardStatus);
        _assignDeviceAsync = assignDeviceAsync;
        _closeWindowAsync = closeWindowAsync;
        _setClientSizeAsync = setClientSizeAsync;
        _setModeAsync = setModeAsync;
        _setTitleAsync = setTitleAsync;
    }

    /// <summary>Gets the owner's clipboard capability state.</summary>
    public ClipboardServiceStatus ClipboardStatus
    {
        get
        {
            if (_clipboardStatus is ClipboardServiceStatus.NotRequested)
            {
                return _clipboardStatus;
            }

            var backendStatus = _backend.ClipboardStatus;
            return (backendStatus is ClipboardServiceStatus.NotRequested)
                ? ClipboardServiceStatus.Unsupported
                : backendStatus;
        }
    }

    /// <summary>Gets the optional asynchronous clipboard text service.</summary>
    public ClipboardTextService? ClipboardText => _clipboardText;

    internal PlatformServiceStatus GamepadStatus => _backend.GamepadStatus;

    internal uint OwnerId => _backend.OwnerId;

    internal PlatformBackend SelectedBackend => _backend.Backend;

    /// <summary>Assigns one connected physical gamepad to a host-owned simulation timeline and player slot.</summary>
    /// <param name="device">The current host-issued gamepad identity.</param>
    /// <param name="simulationRecipientId">The same-host SimulationTimeline recipient.</param>
    /// <param name="playerSlot">The player slot that receives the gamepad.</param>
    /// <param name="cancellationToken">The token that cancels the command before owner-thread application.</param>
    /// <returns>The recipient-local assignment result and its host sequence/tick when applied.</returns>
    /// <exception cref="ArgumentException"><paramref name="device"/> is invalid or belongs to another host.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="simulationRecipientId"/> is zero.</exception>
    /// <exception cref="InvalidOperationException">The PlatformHost has no Runtime assignment command owner.</exception>
    /// <exception cref="ObjectDisposedException">The owning EngineHost is stopping or has stopped.</exception>
    public Task<InputRoutingReceipt> AssignDeviceAsync(
        InputDeviceHandle device,
        uint simulationRecipientId,
        ushort playerSlot,
        CancellationToken cancellationToken = default)
    {
        if (!device.IsValid || (device.OwnerId != _backend.OwnerId))
        {
            throw new ArgumentException("The device handle is invalid or belongs to another platform owner.", nameof(device));
        }

        ArgumentOutOfRangeException.ThrowIfZero(simulationRecipientId);
        var assignDeviceAsync = _assignDeviceAsync
            ?? throw new InvalidOperationException("The PlatformHost has no Runtime assignment command owner.");
        return assignDeviceAsync(device, simulationRecipientId, playerSlot, cancellationToken);
    }

    /// <summary>Requests closure of a window on the platform owner.</summary>
    /// <param name="window">The live window owned by this PlatformHost.</param>
    /// <param name="cancellationToken">The token that cancels the command before owner-thread application.</param>
    /// <returns>The owner-thread command result.</returns>
    /// <exception cref="ArgumentException"><paramref name="window"/> belongs to another platform owner.</exception>
    /// <exception cref="InvalidOperationException">The PlatformHost has no Runtime window-command owner.</exception>
    /// <exception cref="ObjectDisposedException">The owning EngineHost is stopping or has stopped.</exception>
    public Task<WindowCommandReceipt> CloseWindowAsync(WindowHandle window, CancellationToken cancellationToken = default)
    {
        if (!window.IsValid || (window.OwnerId != _backend.OwnerId))
        {
            throw new ArgumentException("The window handle belongs to another platform owner.", nameof(window));
        }

        var closeWindowAsync = _closeWindowAsync
            ?? throw new InvalidOperationException("The PlatformHost has no Runtime window-command owner.");
        return closeWindowAsync(window, cancellationToken);
    }

    /// <summary>Gets the latest immutable metrics snapshot for a live window.</summary>
    /// <param name="window">The live window owned by this platform host.</param>
    /// <exception cref="ArgumentException">The window handle belongs to another platform owner.</exception>
    /// <exception cref="InvalidOperationException">The window is stale or no metrics snapshot has been published.</exception>
    public WindowMetrics GetMetrics(WindowHandle window)
    {
        if (!window.IsValid || (window.OwnerId != _backend.OwnerId))
        {
            throw new ArgumentException("The window handle belongs to another platform owner.", nameof(window));
        }

        if (!_backend.TryGetNativeWindowBinding(window, out _))
        {
            throw new InvalidOperationException("The window handle is stale or no longer active.");
        }

        if (!_windowMetrics.TryGetValue(window, out var metrics))
        {
            throw new InvalidOperationException("Window metrics have not been published for this window.");
        }

        return metrics;
    }

    /// <summary>Gets an immutable owner-thread snapshot of monitors in stable discovery order.</summary>
    public ImmutableArray<MonitorHandle> GetMonitorHandles()
        => _backend.GetMonitorHandles();

    /// <summary>Gets the latest requested and natively observed mode for a live window.</summary>
    /// <param name="window">The live window owned by this platform host.</param>
    /// <exception cref="ArgumentException">The window handle belongs to another platform owner.</exception>
    /// <exception cref="InvalidOperationException">The window is stale or no mode snapshot has been published.</exception>
    public WindowModeState GetWindowModeState(WindowHandle window)
    {
        if (!window.IsValid || (window.OwnerId != _backend.OwnerId))
        {
            throw new ArgumentException("The window handle belongs to another platform owner.", nameof(window));
        }

        if (!_backend.TryGetNativeWindowBinding(window, out _))
        {
            throw new InvalidOperationException("The window handle is stale or no longer active.");
        }

        if (!_windowModeStates.TryGetValue(window, out var state))
        {
            throw new InvalidOperationException("Window mode state has not been published for this window.");
        }

        return state;
    }

    /// <summary>Requests a logical client-size change on the platform owner.</summary>
    /// <param name="window">The live window owned by this PlatformHost.</param>
    /// <param name="logicalClientSize">The finite positive client extent in 1/96-inch units.</param>
    /// <param name="cancellationToken">The token that cancels the command before owner-thread application.</param>
    /// <returns>The owner-thread command result; <see cref="WindowMetrics"/> reports the observed extent.</returns>
    /// <exception cref="ArgumentException"><paramref name="window"/> belongs to another platform owner.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="logicalClientSize"/> is non-finite or non-positive.</exception>
    /// <exception cref="InvalidOperationException">The PlatformHost has no Runtime window-command owner.</exception>
    /// <exception cref="ObjectDisposedException">The owning EngineHost is stopping or has stopped.</exception>
    public Task<WindowCommandReceipt> SetClientSizeAsync(
        WindowHandle window,
        Vector2 logicalClientSize,
        CancellationToken cancellationToken = default)
    {
        if (!window.IsValid || (window.OwnerId != _backend.OwnerId))
        {
            throw new ArgumentException("The window handle belongs to another platform owner.", nameof(window));
        }

        logicalClientSize = WindowOptions.ValidateClientSize(logicalClientSize, nameof(logicalClientSize));
        var setClientSizeAsync = _setClientSizeAsync
            ?? throw new InvalidOperationException("The PlatformHost has no Runtime window-command owner.");
        return setClientSizeAsync(window, logicalClientSize, cancellationToken);
    }

    /// <summary>Requests a window presentation mode on the platform owner.</summary>
    /// <param name="window">The live window owned by this platform host.</param>
    /// <param name="mode">The requested presentation mode.</param>
    /// <param name="cancellationToken">The token that cancels the command before owner-thread application.</param>
    /// <returns>The owner-thread result; native confirmation is reported separately through <see cref="WindowModeChanged"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="window"/> belongs to another platform owner.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is invalid.</exception>
    /// <exception cref="InvalidOperationException">The PlatformHost has no Runtime window-mode command owner.</exception>
    /// <exception cref="ObjectDisposedException">The owning EngineHost is stopping or has stopped.</exception>
    public Task<WindowCommandReceipt> SetModeAsync(
        WindowHandle window,
        WindowMode mode,
        CancellationToken cancellationToken = default)
    {
        if (!window.IsValid || (window.OwnerId != _backend.OwnerId))
        {
            throw new ArgumentException("The window handle belongs to another platform owner.", nameof(window));
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var setModeAsync = _setModeAsync
            ?? throw new InvalidOperationException("The PlatformHost has no Runtime window-mode command owner.");
        return setModeAsync(window, mode, cancellationToken);
    }

    /// <summary>Requests a presentation-title change on the platform owner.</summary>
    /// <param name="window">The live window owned by this PlatformHost.</param>
    /// <param name="title">The UTF-16 title without embedded null characters.</param>
    /// <param name="cancellationToken">The token that cancels the command before owner-thread application.</param>
    /// <returns>The owner-thread command result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="title"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="window"/> belongs to another platform owner, or <paramref name="title"/> contains an embedded null character.</exception>
    /// <exception cref="InvalidOperationException">The PlatformHost has no Runtime window-command owner.</exception>
    /// <exception cref="ObjectDisposedException">The owning EngineHost is stopping or has stopped.</exception>
    public Task<WindowCommandReceipt> SetTitleAsync(
        WindowHandle window,
        string title,
        CancellationToken cancellationToken = default)
    {
        if (!window.IsValid || (window.OwnerId != _backend.OwnerId))
        {
            throw new ArgumentException("The window handle belongs to another platform owner.", nameof(window));
        }

        ArgumentNullException.ThrowIfNull(title);

        if (title.AsSpan().Contains('\0'))
        {
            throw new ArgumentException("A window title cannot contain an embedded null character.", nameof(title));
        }

        var setTitleAsync = _setTitleAsync
            ?? throw new InvalidOperationException("The PlatformHost has no Runtime window-command owner.");
        return setTitleAsync(window, title, cancellationToken);
    }

    internal void CloseWindow(WindowHandle window)
    {
        if (!_backend.TryGetNativeWindowBinding(window, out _))
        {
            throw new InvalidOperationException("The window handle is stale or no longer active.");
        }

        _ = _windowMetrics.Remove(window);
        _ = _windowModeStates.Remove(window);
        _backend.CloseWindow(window);
    }

    internal void CreateWindow(WindowHandle window, WindowOptions options, nint nativeMonitorHandle)
        => _backend.CreateWindow(window, options, nativeMonitorHandle);

    internal void DisposeFromOwner()
    {
        _clipboardText?.DisposeFromOwner();
        _backend.Dispose();
        _windowMetrics.Clear();
        _windowModeStates.Clear();
        _pendingModeDiagnostics.Clear();
        _pendingStartupDiagnostics.Clear();
    }

    internal PlatformInputSampleBatch DrainInputSamples()
        => _backend.DrainInputSamples();

    internal ImmutableArray<Diagnostic> DrainModeDiagnostics()
    {
        var diagnostics = ImmutableArray.CreateRange(_pendingModeDiagnostics);
        _pendingModeDiagnostics.Clear();
        return diagnostics;
    }

    internal ImmutableArray<Diagnostic> DrainStartupDiagnostics()
    {
        var diagnostics = ImmutableArray.CreateRange(_pendingStartupDiagnostics);
        _pendingStartupDiagnostics.Clear();
        return diagnostics;
    }

    internal ImmutableArray<WindowHandle> DrainWindowCloseRequests()
        => _backend.DrainWindowCloseRequests();

    internal ImmutableArray<PlatformMonitorInfo> GetMonitorInfo()
        => _backend.GetMonitorInfo();

    internal PlatformNativeWindowMetrics GetNativeWindowMetrics(WindowHandle window)
        => _backend.GetNativeWindowMetrics(window);

    internal PlatformNativeWindowModeState GetNativeWindowModeState(WindowHandle window)
        => _backend.GetNativeWindowModeState(window);

    internal WindowMetrics? GetWindowMetricsSnapshot(WindowHandle window)
        => _windowMetrics.GetValueOrDefault(window);

    internal WindowModeState? GetWindowModeStateSnapshot(WindowHandle window)
        => _windowModeStates.GetValueOrDefault(window);

    internal void Pump()
    {
        _backend.Pump();
        _clipboardText?.Pump();
    }

    internal void RequestGamepadStateReconciliation(nint nativeDeviceIdentity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero((long)nativeDeviceIdentity);
        _backend.RequestGamepadStateReconciliation(nativeDeviceIdentity);
    }

    internal void RequestInputStateReconciliation(WindowHandle? window)
    {
        if (window is { } windowHandle)
        {
            if (!windowHandle.IsValid || (windowHandle.OwnerId != _backend.OwnerId))
            {
                throw new ArgumentException("The window handle belongs to another platform owner.", nameof(window));
            }

            if (!_backend.TryGetNativeWindowBinding(windowHandle, out _))
            {
                throw new InvalidOperationException("The window handle is stale or no longer active.");
            }
        }

        _backend.RequestInputStateReconciliation(window);
    }

    internal void SetMonitorHandles(ImmutableArray<MonitorHandle> monitorHandles)
        => _backend.SetMonitorHandles(monitorHandles);

    internal WindowCommandStatus SetWindowClientSize(WindowHandle window, Vector2 logicalClientSize)
        => _backend.SetWindowClientSize(window, logicalClientSize);

    internal void SetWindowMetrics(WindowHandle window, WindowMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        if (!window.IsValid || (window.OwnerId != _backend.OwnerId))
        {
            throw new ArgumentException("The window handle belongs to another platform owner.", nameof(window));
        }

        if (!_backend.TryGetNativeWindowBinding(window, out _))
        {
            throw new InvalidOperationException("The window handle is stale or no longer active.");
        }

        _windowMetrics[window] = metrics;
    }

    internal WindowCommandStatus SetWindowMode(WindowHandle window, WindowMode mode)
        => _backend.SetWindowMode(window, mode);

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Each mode-state subscriber is isolated so one callback cannot suppress later subscribers or unwind native dispatch.")]
    internal WindowModeState SetWindowModeState(
        WindowHandle window,
        PlatformNativeWindowModeState nativeState,
        long hostSequence)
    {
        if (!window.IsValid || (window.OwnerId != _backend.OwnerId))
        {
            throw new ArgumentException("The window handle belongs to another platform owner.", nameof(window));
        }

        if (!_backend.TryGetNativeWindowBinding(window, out _))
        {
            throw new InvalidOperationException("The window handle is stale or no longer active.");
        }

        var state = new WindowModeState(window, nativeState.RequestedMode, nativeState.ObservedMode);

        if (_windowModeStates.TryGetValue(window, out var previousState)
            && (previousState.RequestedMode == state.RequestedMode)
            && (previousState.ObservedMode == state.ObservedMode))
        {
            return previousState;
        }

        _windowModeStates[window] = state;
        var handlers = WindowModeChanged;

        if (handlers is null)
        {
            return state;
        }

        var eventArgs = new WindowModeChangedEventArgs(state, hostSequence);

        foreach (EventHandler<WindowModeChangedEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, eventArgs);
            }
            catch (Exception exception)
            {
                _pendingModeDiagnostics.Add(new Diagnostic(
                    "SubscriberDeliveryFailed",
                    DiagnosticSeverity.Error,
                    _timeProvider.GetTimestamp(),
                    _backend.OwnerId,
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
                    operation: "PlatformHost.WindowModeChanged",
                    nativeResult: null,
                    message: "A window mode state subscriber failed.",
                    context: [
                        new DiagnosticContextEntry("HostSequence", hostSequence.ToString(CultureInfo.InvariantCulture)),
                        new DiagnosticContextEntry("Exception", exception.ToString()),
                    ]));
            }
        }

        return state;
    }

    internal WindowCommandStatus SetWindowTitle(WindowHandle window, string title)
        => _backend.SetWindowTitle(window, title);

    internal bool TakeMonitorTopologyChanged()
        => _backend.TakeMonitorTopologyChanged();

    internal bool TryGetNativeWindowBinding(WindowHandle window, out PlatformNativeWindowBinding binding)
        => _backend.TryGetNativeWindowBinding(window, out binding);

    internal void UpdateTextInputSession(WindowHandle window, TextInputSessionSnapshot? snapshot)
    {
        if (!window.IsValid || (window.OwnerId != _backend.OwnerId))
        {
            throw new ArgumentException("The text input session must target a live window from this platform owner.", nameof(window));
        }

        if (snapshot is { } session
            && ((session.SessionId <= 0) || (session.Window != window)))
        {
            throw new ArgumentException("The text input snapshot must name its exact session and window.", nameof(snapshot));
        }

        _backend.UpdateTextInputSession(window, snapshot);
    }
}
