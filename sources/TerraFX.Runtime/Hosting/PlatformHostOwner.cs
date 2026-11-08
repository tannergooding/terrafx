// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;
using TerraFX.Graphics;
using TerraFX.Input;
using TerraFX.Platform;
using TerraFX.Numerics;

namespace TerraFX.Hosting;

internal sealed class PlatformHostOwner
{
    private Dictionary<string, MonitorHandle> _activeMonitorHandles;
    private readonly HashSet<WindowHandle> _activeWindowHandles;
    private readonly PlatformInputBroker _inputBroker;
    private readonly Dictionary<WindowHandle, PendingWindowModeTransition> _pendingWindowModeTransitions = [];
    private bool _isDisposed;
    private bool _isStopping;
    private Dictionary<MonitorHandle, nint> _nativeMonitorHandles;
    private ulong _nextMonitorIndex;
    private ulong _nextWindowIndex;
    private readonly uint _ownerId;
    private readonly int _ownerThreadId;
    private readonly PlatformHost _platform;
    private readonly HostProfile _profile;
    private readonly HostEventSequenceLease _sequenceLease;
    private readonly TimeProvider _timeProvider;

    private PlatformHostOwner(
        uint ownerId,
        HostProfile profile,
        PlatformHost platform,
        TimeProvider timeProvider,
        HostEventSequenceLease sequenceLease)
    {
        _ownerId = ownerId;
        _platform = platform;
        _profile = profile;
        _timeProvider = timeProvider;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _sequenceLease = sequenceLease;
        _inputBroker = new PlatformInputBroker(ownerId, timeProvider, sequenceLease);
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
    internal static PlatformHostOwner Create(
        EngineHostOptions options,
        HostEventSequenceLease? sequenceLease = null,
        Func<InputDeviceHandle, uint, ushort, CancellationToken, Task<InputRoutingReceipt>>? assignDeviceAsync = null,
        Func<WindowHandle, Vector2, CancellationToken, Task<WindowCommandReceipt>>? setClientSizeAsync = null,
        Func<WindowHandle, string, CancellationToken, Task<WindowCommandReceipt>>? setTitleAsync = null,
        Func<WindowHandle, CancellationToken, Task<WindowCommandReceipt>>? closeWindowAsync = null,
        Func<WindowHandle, WindowMode, CancellationToken, Task<WindowCommandReceipt>>? setModeAsync = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var ownerId = OwnerIdAllocator.Allocate();
        var timeProvider = TimeProvider.System;
        sequenceLease ??= new HostEventSequenceLease();
        var platform = new PlatformHost(
            ownerId,
            options.Platform,
            options.ClipboardServicePolicy,
            timeProvider,
            clipboardAllowed: options.Profile is HostProfile.Interactive,
            assignDeviceAsync,
            setClientSizeAsync,
            setTitleAsync,
            closeWindowAsync,
            setModeAsync);
        var owner = new PlatformHostOwner(ownerId, options.Profile, platform, timeProvider, sequenceLease);

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

    internal static GraphicsNativeSurfaceBinding CreateGraphicsNativeSurfaceBinding(
        PlatformNativeWindowBinding binding,
        uint surfaceEpoch)
    {
        if (!binding.Window.IsValid)
        {
            throw new ArgumentException("The platform binding does not identify a live window.", nameof(binding));
        }

        var platformKind = binding.Kind switch {
            PlatformNativeSurfaceKind.Win32 => GraphicsPlatformKind.Win32,
            PlatformNativeSurfaceKind.Wayland => GraphicsPlatformKind.Wayland,
            PlatformNativeSurfaceKind.X11 => GraphicsPlatformKind.X11,
            _ => throw new ArgumentOutOfRangeException(nameof(binding)),
        };
        return new GraphicsNativeSurfaceBinding(
            platformKind,
            binding.NativeDisplayOrInstance,
            binding.NativeWindowOrSurface,
            surfaceEpoch);
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

        CompletePendingModeTransition(window, WindowCommandStatus.Disposed);
        _platform.CloseWindow(window);
    }

    internal WindowCommandReceipt CloseWindowCommand(WindowHandle window)
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        ThrowIfStopping();

        if (!window.IsValid || (window.OwnerId != _ownerId))
        {
            throw new ArgumentException("The window handle belongs to another platform owner.", nameof(window));
        }

        var ownerSequence = _sequenceLease.Allocate();

        if (!_activeWindowHandles.Remove(window))
        {
            return new WindowCommandReceipt(
                WindowCommandKind.Close,
                window,
                WindowCommandStatus.Disposed,
                ownerSequence,
                diagnostic: null);
        }

        _platform.CloseWindow(window);
        return new WindowCommandReceipt(
            WindowCommandKind.Close,
            window,
            WindowCommandStatus.Applied,
            ownerSequence,
            diagnostic: null);
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A created native window must be retired if its first owner-thread metrics snapshot cannot be published.")]
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

        var nativeWindowCreated = false;
        var creationCompleted = false;

        try
        {
            _platform.CreateWindow(window, options, nativeMonitorHandle);
            nativeWindowCreated = true;
            RefreshWindowMetrics(window);
            RefreshWindowModeState(window);
            creationCompleted = true;
            return window;
        }
        catch (Exception creationException)
        {
            if (nativeWindowCreated)
            {
                try
                {
                    _platform.CloseWindow(window);
                }
                catch (Exception cleanupException)
                {
                    throw new AggregateException(creationException, cleanupException);
                }
            }

            throw;
        }
        finally
        {
            if (!creationCompleted)
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

        while (_pendingWindowModeTransitions.Count > 0)
        {
            var window = default(WindowHandle);

            foreach (var pendingWindow in _pendingWindowModeTransitions.Keys)
            {
                window = pendingWindow;
                break;
            }

            CompletePendingModeTransition(window, WindowCommandStatus.Disposed);
        }

        _activeWindowHandles.Clear();
        _inputBroker.Retire();
        _platform.DisposeFromOwner();
        _isDisposed = true;
    }

    internal ImmutableArray<Diagnostic> DrainModeDiagnostics()
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        return _platform.DrainModeDiagnostics();
    }

    internal ImmutableArray<Diagnostic> DrainStartupDiagnostics()
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        return _platform.DrainStartupDiagnostics();
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

    internal GraphicsNativeSurfaceBinding GetGraphicsNativeSurfaceBinding(WindowHandle window, uint surfaceEpoch)
        => CreateGraphicsNativeSurfaceBinding(GetNativeWindowBinding(window), surfaceEpoch);

    internal long GetHostTimestamp()
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        return _inputBroker.GetCurrentHostTimestamp();
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

    internal bool IsActiveGamepadDevice(InputDeviceHandle device)
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        return _inputBroker.IsActiveGamepadDevice(device);
    }

    internal PlatformInputCaptureBatch Pump()
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        ThrowIfStopping();
        _platform.Pump();

        if (_platform.TakeMonitorTopologyChanged())
        {
            RefreshMonitorSnapshot();
        }

        foreach (var window in _activeWindowHandles)
        {
            RefreshWindowMetrics(window);
            RefreshWindowModeState(window);
        }

        CompleteObservedModeTransitions();
        return _inputBroker.Capture(_platform.DrainInputSamples(), _activeWindowHandles.Contains);
    }

    internal PlatformInputCaptureBatch RequestGamepadStateReconciliation(InputDeviceHandle device)
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        ThrowIfStopping();

        if (!_inputBroker.IsActiveGamepadDevice(device))
        {
            throw new InvalidOperationException("The gamepad device handle is stale or no longer active.");
        }

        var nativeDeviceIdentity = _inputBroker.GetNativeDeviceIdentity(device);
        _platform.RequestGamepadStateReconciliation(nativeDeviceIdentity);
        return _inputBroker.Capture(_platform.DrainInputSamples(), _activeWindowHandles.Contains);
    }

    internal PlatformInputCaptureBatch RequestInputStateReconciliation(WindowHandle? window)
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        ThrowIfStopping();

        if (window is { } windowHandle)
        {
            if (!windowHandle.IsValid || (windowHandle.OwnerId != _ownerId))
            {
                throw new ArgumentException("The window handle belongs to another platform owner.", nameof(window));
            }

            if (!_activeWindowHandles.Contains(windowHandle))
            {
                throw new InvalidOperationException("The window handle is stale or no longer active.");
            }
        }

        _platform.RequestInputStateReconciliation(window);
        return _inputBroker.Capture(_platform.DrainInputSamples(), _activeWindowHandles.Contains);
    }

    internal WindowCommandReceipt SetClientSize(WindowHandle window, Vector2 logicalClientSize)
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        ThrowIfStopping();

        if (!window.IsValid || (window.OwnerId != _ownerId))
        {
            throw new ArgumentException("The window handle belongs to another platform owner.", nameof(window));
        }

        logicalClientSize = WindowOptions.ValidateClientSize(logicalClientSize, nameof(logicalClientSize));
        var ownerSequence = _sequenceLease.Allocate();

        if (!_activeWindowHandles.Contains(window))
        {
            return new WindowCommandReceipt(
                WindowCommandKind.SetClientSize,
                window,
                WindowCommandStatus.Disposed,
                ownerSequence,
                diagnostic: null);
        }

        var status = _platform.SetWindowClientSize(window, logicalClientSize);

        if (status is WindowCommandStatus.Applied)
        {
            RefreshWindowMetrics(window);
        }

        return new WindowCommandReceipt(
            WindowCommandKind.SetClientSize,
            window,
            status,
            ownerSequence,
            diagnostic: null);
    }

    internal void SetModeCommand(
        WindowHandle window,
        WindowMode mode,
        Action<WindowCommandReceipt> complete)
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        ThrowIfStopping();
        ArgumentNullException.ThrowIfNull(complete);

        if (!window.IsValid || (window.OwnerId != _ownerId))
        {
            throw new ArgumentException("The window handle belongs to another platform owner.", nameof(window));
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (_pendingWindowModeTransitions.ContainsKey(window))
        {
            complete(new WindowCommandReceipt(
                WindowCommandKind.SetMode,
                window,
                WindowCommandStatus.QueueFull,
                ownerSequence: 0,
                diagnostic: null));
            return;
        }

        var ownerSequence = _sequenceLease.Allocate();

        if (!_activeWindowHandles.Contains(window))
        {
            complete(new WindowCommandReceipt(
                WindowCommandKind.SetMode,
                window,
                WindowCommandStatus.Disposed,
                ownerSequence,
                diagnostic: null));
            return;
        }

        var status = _platform.SetWindowMode(window, mode);

        if (status is not WindowCommandStatus.Applied)
        {
            var diagnostic = status switch {
                WindowCommandStatus.Unsupported => CreateWindowModeDiagnostic(
                    "platform-window-mode-unsupported",
                    window,
                    mode,
                    "The selected platform does not support the requested window mode."),
                WindowCommandStatus.PermissionDenied => CreateWindowModeDiagnostic(
                    "platform-window-mode-denied",
                    window,
                    mode,
                    "The native platform denied the requested window mode."),
                _ => null,
            };
            complete(new WindowCommandReceipt(WindowCommandKind.SetMode, window, status, ownerSequence, diagnostic));
            return;
        }

        RefreshWindowModeState(window, ownerSequence);
        var modeState = _platform.GetWindowModeStateSnapshot(window)
            ?? throw new InvalidOperationException("The PlatformHost did not publish mode state for an active window.");

        if ((modeState.RequestedMode == mode) && (modeState.ObservedMode == mode))
        {
            complete(new WindowCommandReceipt(
                WindowCommandKind.SetMode,
                window,
                WindowCommandStatus.Applied,
                ownerSequence,
                diagnostic: null));
            return;
        }

        _pendingWindowModeTransitions.Add(
            window,
            new PendingWindowModeTransition(
                mode,
                ownerSequence,
                _timeProvider.GetTimestamp(),
                complete));
    }

    internal WindowCommandReceipt SetTitle(WindowHandle window, string title)
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        ThrowIfStopping();
        ArgumentNullException.ThrowIfNull(title);

        if (!window.IsValid || (window.OwnerId != _ownerId))
        {
            throw new ArgumentException("The window handle belongs to another platform owner.", nameof(window));
        }

        if (title.AsSpan().Contains('\0'))
        {
            throw new ArgumentException("A window title cannot contain an embedded null character.", nameof(title));
        }

        var ownerSequence = _sequenceLease.Allocate();
        var status = _activeWindowHandles.Contains(window)
            ? _platform.SetWindowTitle(window, title)
            : WindowCommandStatus.Disposed;
        return new WindowCommandReceipt(
            WindowCommandKind.SetTitle,
            window,
            status,
            ownerSequence,
            diagnostic: null);
    }

    private void CompleteObservedModeTransitions()
    {
        if (_pendingWindowModeTransitions.Count == 0)
        {
            return;
        }

        var completed = new List<WindowHandle>();
        var now = _timeProvider.GetTimestamp();

        foreach (var (window, transition) in _pendingWindowModeTransitions)
        {
            var modeState = _platform.GetWindowModeStateSnapshot(window);

            if ((modeState is not null)
                && (modeState.RequestedMode == transition.RequestedMode)
                && (modeState.ObservedMode == transition.RequestedMode))
            {
                transition.Complete(new WindowCommandReceipt(
                    WindowCommandKind.SetMode,
                    window,
                    WindowCommandStatus.Applied,
                    transition.OwnerSequence,
                    diagnostic: null));
                completed.Add(window);
            }
            else if (_timeProvider.GetElapsedTime(transition.StartedTimestamp, now) >= TimeSpan.FromSeconds(5))
            {
                var diagnostic = CreateWindowModeDiagnostic(
                    "platform-window-mode-unconfirmed",
                    window,
                    transition.RequestedMode,
                    "The native platform did not confirm the requested window mode within five host seconds.");
                transition.Complete(new WindowCommandReceipt(
                    WindowCommandKind.SetMode,
                    window,
                    WindowCommandStatus.Unconfirmed,
                    transition.OwnerSequence,
                    diagnostic));
                completed.Add(window);
            }
        }

        foreach (var window in completed)
        {
            _ = _pendingWindowModeTransitions.Remove(window);
        }
    }

    private void CompletePendingModeTransition(WindowHandle window, WindowCommandStatus status)
    {
        if (_pendingWindowModeTransitions.Remove(window, out var transition))
        {
            transition.Complete(new WindowCommandReceipt(
                WindowCommandKind.SetMode,
                window,
                status,
                transition.OwnerSequence,
                diagnostic: null));
        }
    }

    private Diagnostic CreateWindowModeDiagnostic(string code, WindowHandle window, WindowMode mode, string message)
        => new(
            code,
            DiagnosticSeverity.Warning,
            _timeProvider.GetTimestamp(),
            _ownerId,
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
            operation: "PlatformHost.SetModeAsync",
            nativeResult: null,
            message,
            context: [
                new DiagnosticContextEntry("WindowIndex", window.Index.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticContextEntry("WindowGeneration", window.Generation.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticContextEntry("RequestedMode", mode.ToString()),
            ]);

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

    private void RefreshWindowMetrics(WindowHandle window)
    {
        var nativeMetrics = _platform.GetNativeWindowMetrics(window);
        var previousMetrics = _platform.GetWindowMetricsSnapshot(window);

        if (previousMetrics is not null
            && (previousMetrics.ClientPixelSize == nativeMetrics.ClientPixelSize)
            && previousMetrics.LogicalClientSize.Equals(nativeMetrics.LogicalClientSize)
            && previousMetrics.ContentScale.Equals(nativeMetrics.ContentScale)
            && (previousMetrics.EffectiveDpi == nativeMetrics.EffectiveDpi)
            && (previousMetrics.DpiSource == nativeMetrics.DpiSource)
            && (previousMetrics.DpiConfidence == nativeMetrics.DpiConfidence))
        {
            return;
        }

        var metrics = new WindowMetrics(
            nativeMetrics.LogicalClientSize,
            nativeMetrics.ClientPixelSize,
            nativeMetrics.ContentScale,
            nativeMetrics.EffectiveDpi,
            nativeMetrics.DpiSource,
            nativeMetrics.DpiConfidence,
            previousMetrics?.SurfaceEpoch ?? 0,
            previousMetrics?.SwapchainEpoch ?? 0);

        _platform.SetWindowMetrics(window, metrics);
    }

    private void RefreshWindowModeState(WindowHandle window, long? ownerSequence = null)
    {
        var nativeModeState = _platform.GetNativeWindowModeState(window);
        var previousModeState = _platform.GetWindowModeStateSnapshot(window);

        if ((previousModeState is not null)
            && (previousModeState.RequestedMode == nativeModeState.RequestedMode)
            && (previousModeState.ObservedMode == nativeModeState.ObservedMode))
        {
            return;
        }

        _ = _platform.SetWindowModeState(
            window,
            nativeModeState,
            ownerSequence ?? _sequenceLease.Allocate());
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

    private sealed record PendingWindowModeTransition(
        WindowMode RequestedMode,
        long OwnerSequence,
        long StartedTimestamp,
        Action<WindowCommandReceipt> Complete);
}
