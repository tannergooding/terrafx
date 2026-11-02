// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using TerraFX;

namespace TerraFX.Platform;

/// <summary>Provides owner-thread platform services owned by an engine host.</summary>
public sealed class PlatformHost
{
    internal const int MaximumWindowCount = 32;
    private readonly IPlatformBackend _backend;
    private readonly Dictionary<WindowHandle, WindowMetrics> _windowMetrics = [];

    /// <summary>Creates one platform owner from a Runtime-issued identity and explicit service policy.</summary>
    /// <param name="ownerId">The nonzero identity issued by the Runtime host owner.</param>
    /// <param name="backend">The explicitly selected platform backend.</param>
    /// <param name="clipboardPolicy">The host's clipboard service admission policy.</param>
    /// <param name="timeProvider">The host's private monotonic timestamp source.</param>
    internal PlatformHost(
        uint ownerId,
        PlatformBackend backend,
        ClipboardServicePolicy clipboardPolicy,
        TimeProvider? timeProvider = null)
    {
        _backend = PlatformBackendFactory.Create(backend, ownerId, clipboardPolicy, timeProvider);
    }

    /// <summary>Gets the owner's clipboard capability state.</summary>
    public ClipboardServiceStatus ClipboardStatus => _backend.ClipboardStatus;

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

    internal void Pump() => _backend.Pump();

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
