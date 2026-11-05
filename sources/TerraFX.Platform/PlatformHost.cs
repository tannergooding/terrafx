// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;
using TerraFX.Input;

namespace TerraFX.Platform;

/// <summary>Provides owner-thread platform services owned by an engine host.</summary>
public sealed class PlatformHost
{
    internal const int MaximumWindowCount = 32;
    private readonly Func<InputDeviceHandle, uint, ushort, CancellationToken, Task<InputRoutingReceipt>>? _assignDeviceAsync;
    private readonly IPlatformBackend _backend;
    private readonly ClipboardServiceStatus _clipboardStatus;
    private readonly ClipboardTextService? _clipboardText;
    private readonly Dictionary<WindowHandle, WindowMetrics> _windowMetrics = [];

    /// <summary>Creates one platform owner from a Runtime-issued identity and explicit service policy.</summary>
    /// <param name="ownerId">The nonzero identity issued by the Runtime host owner.</param>
    /// <param name="backend">The explicitly selected platform backend.</param>
    /// <param name="clipboardPolicy">The host's clipboard service admission policy.</param>
    /// <param name="timeProvider">The host's private monotonic timestamp source.</param>
    /// <param name="clipboardAllowed">Whether this host profile permits clipboard text.</param>
    /// <param name="assignDeviceAsync">The Runtime-owned route for host-bound gamepad assignment.</param>
    internal PlatformHost(
        uint ownerId,
        PlatformBackend backend,
        ClipboardServicePolicy clipboardPolicy,
        TimeProvider? timeProvider = null,
        bool clipboardAllowed = true,
        Func<InputDeviceHandle, uint, ushort, CancellationToken, Task<InputRoutingReceipt>>? assignDeviceAsync = null)
    {
        if (!clipboardAllowed && (clipboardPolicy is ClipboardServicePolicy.Required))
        {
            throw new NotSupportedException("The selected host profile does not provide clipboard text.");
        }

        var backendClipboardPolicy = clipboardAllowed ? clipboardPolicy : ClipboardServicePolicy.NotRequested;
        _backend = PlatformBackendFactory.Create(backend, ownerId, backendClipboardPolicy, timeProvider);
        _clipboardStatus = (clipboardPolicy is ClipboardServicePolicy.NotRequested)
            ? ClipboardServiceStatus.NotRequested
            : (clipboardAllowed ? _backend.ClipboardStatus : ClipboardServiceStatus.Unsupported);
        _clipboardText = (clipboardPolicy is ClipboardServicePolicy.NotRequested)
            ? null
            : new ClipboardTextService(_backend, _clipboardStatus);
        _assignDeviceAsync = assignDeviceAsync;
    }

    /// <summary>Gets the owner's clipboard capability state.</summary>
    public ClipboardServiceStatus ClipboardStatus => _clipboardStatus;

    /// <summary>Gets the optional asynchronous clipboard text service.</summary>
    public ClipboardTextService? ClipboardText => _clipboardText;

    internal PlatformServiceStatus GamepadStatus => _backend.GamepadStatus;

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

    internal void CloseWindow(WindowHandle window)
    {
        if (!_backend.TryGetNativeWindowBinding(window, out _))
        {
            throw new InvalidOperationException("The window handle is stale or no longer active.");
        }

        _ = _windowMetrics.Remove(window);
        _backend.CloseWindow(window);
    }

    internal void CreateWindow(WindowHandle window, WindowOptions options, nint nativeMonitorHandle)
        => _backend.CreateWindow(window, options, nativeMonitorHandle);

    internal void DisposeFromOwner()
    {
        _clipboardText?.DisposeFromOwner();
        _backend.Dispose();
        _windowMetrics.Clear();
    }

    internal PlatformInputSampleBatch DrainInputSamples()
        => _backend.DrainInputSamples();

    internal ImmutableArray<WindowHandle> DrainWindowCloseRequests()
        => _backend.DrainWindowCloseRequests();

    internal ImmutableArray<PlatformMonitorInfo> GetMonitorInfo()
        => _backend.GetMonitorInfo();

    internal PlatformNativeWindowMetrics GetNativeWindowMetrics(WindowHandle window)
        => _backend.GetNativeWindowMetrics(window);

    internal WindowMetrics? GetWindowMetricsSnapshot(WindowHandle window)
        => _windowMetrics.GetValueOrDefault(window);

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

    internal bool TakeMonitorTopologyChanged()
        => _backend.TakeMonitorTopologyChanged();

    internal bool TryGetNativeWindowBinding(WindowHandle window, out PlatformNativeWindowBinding binding)
        => _backend.TryGetNativeWindowBinding(window, out binding);
}
