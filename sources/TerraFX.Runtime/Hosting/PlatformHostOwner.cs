// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using TerraFX;
using TerraFX.Platform;

namespace TerraFX.Hosting;

internal sealed class PlatformHostOwner
{
    private Dictionary<string, MonitorHandle> _activeMonitorHandles;
    private readonly HashSet<WindowHandle> _activeWindowHandles;
    private bool _isDisposed;
    private bool _isStopping;
    private Dictionary<MonitorHandle, nint> _nativeMonitorHandles;
    private ulong _nextMonitorIndex;
    private ulong _nextWindowIndex;
    private readonly uint _ownerId;
    private readonly int _ownerThreadId;
    private readonly PlatformHost _platform;
    private readonly HostProfile _profile;

    private PlatformHostOwner(uint ownerId, HostProfile profile, PlatformHost platform)
    {
        _ownerId = ownerId;
        _platform = platform;
        _profile = profile;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _activeMonitorHandles = [];
        _activeWindowHandles = [];
        _nativeMonitorHandles = [];
    }

    internal uint OwnerId => _ownerId;

    internal PlatformHost Platform
    {
        get
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            return _platform;
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A monitor-snapshot startup failure must dispose the already-created Platform owner.")]
    internal static PlatformHostOwner Create(EngineHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var ownerId = OwnerIdAllocator.Allocate();
        var platform = new PlatformHost(ownerId, options.Platform, options.ClipboardServicePolicy);
        var owner = new PlatformHostOwner(ownerId, options.Profile, platform);

        try
        {
            owner.RefreshMonitorSnapshot();
            return owner;
        }
        catch (Exception exception)
        {
            try
            {
                owner.DisposeFromOwner();
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(exception, cleanupException);
            }

            throw;
        }
    }

    internal void CloseWindow(WindowHandle window)
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        ThrowIfStopping();

        if (!window.IsValid || (window.OwnerId != _ownerId))
        {
            throw new ArgumentException("The window handle belongs to another platform owner.", nameof(window));
        }

        if (!_activeWindowHandles.Remove(window))
        {
            throw new InvalidOperationException("The window handle is stale or no longer active.");
        }

        _platform.CloseWindow(window);
    }

    internal WindowHandle CreateWindow(WindowOptions options)
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        ThrowIfStopping();
        ArgumentNullException.ThrowIfNull(options);

        if (_profile != HostProfile.Interactive)
        {
            throw new NotSupportedException("Headless and OffscreenNative hosts do not create windows.");
        }

        if (_activeWindowHandles.Count >= PlatformHost.MaximumWindowCount)
        {
            throw new InvalidOperationException($"A platform host supports at most {PlatformHost.MaximumWindowCount} windows.");
        }

        var nativeMonitorHandle = options.Monitor is MonitorHandle monitor
            ? GetNativeMonitorHandle(monitor)
            : 0;

        if (_nextWindowIndex > uint.MaxValue)
        {
            throw new InvalidOperationException("The PlatformHost exhausted its window handle slots.");
        }

        var window = new WindowHandle(_ownerId, checked((uint)_nextWindowIndex++), generation: 1);

        if (!_activeWindowHandles.Add(window))
        {
            throw new InvalidOperationException("The PlatformHost issued a duplicate window identity.");
        }

        var created = false;

        try
        {
            _platform.CreateWindow(window, options, nativeMonitorHandle);
            created = true;
            return window;
        }
        finally
        {
            if (!created)
            {
                _ = _activeWindowHandles.Remove(window);
            }
        }
    }

    internal void DisposeFromOwner()
    {
        EnsureOwnerThread();

        if (_isDisposed)
        {
            return;
        }

        _isStopping = true;
        _activeWindowHandles.Clear();
        _platform.DisposeFromOwner();
        _isDisposed = true;
    }

    internal PlatformInputSampleBatch DrainInputSamples()
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        ThrowIfStopping();
        return _platform.DrainInputSamples();
    }

    internal ImmutableArray<WindowHandle> DrainWindowCloseRequests()
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        ThrowIfStopping();
        var requestedWindows = _platform.DrainWindowCloseRequests();
        var activeRequests = ImmutableArray.CreateBuilder<WindowHandle>(requestedWindows.Length);

        foreach (var window in requestedWindows)
        {
            if (_activeWindowHandles.Contains(window))
            {
                activeRequests.Add(window);
            }
        }

        return activeRequests.MoveToImmutable();
    }

    internal nint GetNativeMonitorHandle(MonitorHandle monitor)
    {
        EnsureOwnerThread();
        ThrowIfDisposed();

        if (!monitor.IsValid || (monitor.OwnerId != _ownerId))
        {
            throw new ArgumentException("The monitor handle belongs to another platform owner.", nameof(monitor));
        }

        if (!_nativeMonitorHandles.TryGetValue(monitor, out var nativeHandle))
        {
            throw new InvalidOperationException("The monitor handle is stale or no longer enumerated.");
        }

        return nativeHandle;
    }

    internal PlatformNativeWindowBinding GetNativeWindowBinding(WindowHandle window)
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        ThrowIfStopping();

        if (!window.IsValid || (window.OwnerId != _ownerId))
        {
            throw new ArgumentException("The window handle belongs to another platform owner.", nameof(window));
        }

        if (!_activeWindowHandles.Contains(window))
        {
            throw new InvalidOperationException("The window handle is stale or no longer active.");
        }

        if (!_platform.TryGetNativeWindowBinding(window, out var binding))
        {
            throw new InvalidOperationException("The platform has no native binding for the live window.");
        }

        return binding;
    }

    internal void Pump()
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        ThrowIfStopping();
        _platform.Pump();

        if (_platform.TakeMonitorTopologyChanged())
        {
            RefreshMonitorSnapshot();
        }
    }

    private void EnsureOwnerThread()
    {
        if (_ownerThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("The PlatformHost owner is restricted to its issuing thread.");
        }
    }

    private MonitorHandle IssueMonitorHandle()
    {
        if (_nextMonitorIndex > uint.MaxValue)
        {
            throw new InvalidOperationException("The PlatformHost exhausted its monitor handle slots.");
        }

        var index = checked((uint)_nextMonitorIndex++);
        return new MonitorHandle(_ownerId, index, generation: 1);
    }

    private void RefreshMonitorSnapshot()
    {
        EnsureOwnerThread();
        var monitors = _platform.GetMonitorInfo();
        var activeMonitorHandles = new Dictionary<string, MonitorHandle>(monitors.Length, StringComparer.Ordinal);
        var nativeMonitorHandles = new Dictionary<MonitorHandle, nint>(monitors.Length);
        var handles = ImmutableArray.CreateBuilder<MonitorHandle>(monitors.Length);

        foreach (var monitor in monitors)
        {
            if (string.IsNullOrWhiteSpace(monitor.DeviceName) || (monitor.NativeHandle == 0))
            {
                throw new InvalidOperationException("The platform returned an invalid native monitor identity.");
            }

            var handle = _activeMonitorHandles.TryGetValue(monitor.DeviceName, out var currentHandle)
                ? currentHandle
                : IssueMonitorHandle();

            if (!activeMonitorHandles.TryAdd(monitor.DeviceName, handle)
                || !nativeMonitorHandles.TryAdd(handle, monitor.NativeHandle))
            {
                throw new InvalidOperationException("The platform returned duplicate monitor identities.");
            }

            handles.Add(handle);
        }

        var immutableHandles = handles.MoveToImmutable();
        _platform.SetMonitorHandles(immutableHandles);
        _activeMonitorHandles = activeMonitorHandles;
        _nativeMonitorHandles = nativeMonitorHandles;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    private void ThrowIfStopping()
    {
        if (_isStopping)
        {
            throw new InvalidOperationException("The PlatformHost owner is stopping.");
        }
    }
}
