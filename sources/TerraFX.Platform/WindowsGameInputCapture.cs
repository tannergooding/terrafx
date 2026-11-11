// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using GameInputSharp.Abstractions;
using GameInputSharp.Devices;

namespace TerraFX.Platform;

[SupportedOSPlatform("windows")]
internal sealed partial class WindowsGameInputCapture : IDisposable
{
    private const ulong DispatchQuotaMicroseconds = 1000;
    private const int MaximumGamepadDevices = 256;
    private const int MaximumPendingRemovals = MaximumGamepadDevices;
    // Keep the process-wide GameInput singleton and its creating MTA alive across host lifetimes.
    private static GameInputManager? s_processGameInputAnchor;
    private static readonly Lazy<bool> s_processGameInputLifetime = new(
        InitializeProcessGameInputLifetime,
        LazyThreadSafetyMode.ExecutionAndPublication);
    private static Thread? s_processGameInputThread;
    private readonly Dictionary<nint, GamepadCapture> _devicesByNativeIdentity = [];
    private readonly Dictionary<string, GamepadCapture> _gamepadsById = [];
    private readonly GameInputManager _manager;
    private readonly long _ownerThreadId;
    private readonly Queue<PlatformInputSample> _pendingRemovals = new();
    private ExceptionDispatchInfo? _callbackFailure;
    private readonly PlatformInputSampleQueue _samples;
    private readonly TimeProvider _timeProvider;
    private bool _deviceListDirty = true;
    private bool _isDisposed;
    private long _nextNativeIdentity = 1;

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A failed GameInput owner initialization must release its manager before the Platform owner unwinds.")]
    internal WindowsGameInputCapture(
        PlatformInputSampleQueue samples,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (RuntimeFeature.IsDynamicCodeSupported)
        {
            _ = s_processGameInputLifetime.Value;
        }

        _samples = samples;
        _timeProvider = timeProvider;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _manager = new GameInputManager(logger: null, loadOnlyFromSystem32: true);

        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            GamepadStatus = PlatformServiceStatus.Unavailable;
            return;
        }

        try
        {
            var (initSucceeded, _) = _manager.GetDiagnostics();
            GamepadStatus = initSucceeded
                ? PlatformServiceStatus.Available
                : PlatformServiceStatus.Unavailable;
            _manager.DeviceCallback += OnDeviceChanged;
            _manager.ReadingCallback += OnReading;
        }
        catch (Exception initializationException)
        {
            try
            {
                _manager.Dispose();
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(initializationException, cleanupException);
            }

            throw;
        }
    }

    internal PlatformServiceStatus GamepadStatus { get; private set; }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Transfer process-lifetime GameInput initialization failures to the platform owner instead of terminating the process.")]
    private static bool InitializeProcessGameInputLifetime()
    {
        var initialized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => {
            try
            {
                var manager = new GameInputManager(logger: null, loadOnlyFromSystem32: true);
                _ = manager.GetCurrentTimestamp();
                s_processGameInputAnchor = manager;
                initialized.SetResult();
            }
            catch (Exception exception)
            {
                initialized.SetException(exception);
                return;
            }

            Thread.Sleep(Timeout.Infinite);
        }) {
            IsBackground = true,
            Name = "TerraFX GameInput",
        };
        thread.SetApartmentState(ApartmentState.MTA);
        s_processGameInputThread = thread;
        thread.Start();
        initialized.Task.GetAwaiter().GetResult();

        if ((s_processGameInputAnchor is null) || (s_processGameInputThread is not { IsAlive: true }))
        {
            throw new InvalidOperationException("The process-lifetime GameInput owner did not retain its native manager thread.");
        }

        return true;
    }

    public void Dispose()
    {
        EnsureOwnerThread();

        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _manager.DeviceCallback -= OnDeviceChanged;
        _manager.ReadingCallback -= OnReading;

        try
        {
            _manager.Dispose();
        }
        finally
        {
            foreach (var capture in _gamepadsById.Values)
            {
                capture.Device.Dispose();
            }

            _devicesByNativeIdentity.Clear();
            _gamepadsById.Clear();
            _pendingRemovals.Clear();
        }
    }

    internal void Pump()
    {
        EnsureOwnerThread();
        ThrowIfDisposed();

        if (GamepadStatus is not PlatformServiceStatus.Available)
        {
            return;
        }

        RetryPendingRemovals();
        _manager.DispatchCallbacks(DispatchQuotaMicroseconds);
        ThrowCallbackFailure();

        if (_deviceListDirty)
        {
            SynchronizeGamepads();
        }

        foreach (var capture in _gamepadsById.Values)
        {
            if (capture.NeedsAnnouncement || capture.NeedsReconciliation)
            {
                ReconcileCurrentState(capture, GetNativeTimestamp());
            }
        }
    }

    internal void RequestDeviceReconciliation(nint nativeIdentity)
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero((long)nativeIdentity);

        if (GamepadStatus is PlatformServiceStatus.Available
            && _devicesByNativeIdentity.TryGetValue(nativeIdentity, out var capture))
        {
            capture.NeedsReconciliation = true;
            ReconcileCurrentState(capture, GetNativeTimestamp());
        }
    }

    internal void RequestReconciliation()
    {
        EnsureOwnerThread();
        ThrowIfDisposed();

        if (GamepadStatus is not PlatformServiceStatus.Available)
        {
            return;
        }

        Pump();

        foreach (var capture in _gamepadsById.Values)
        {
            capture.NeedsReconciliation = true;
            ReconcileCurrentState(capture, GetNativeTimestamp());
        }
    }

    private sealed class GamepadCapture(GamepadDevice device, nint nativeIdentity, ulong readingCallbackToken)
    {
        internal readonly GamepadDevice Device = device;
        internal readonly string DeviceId = device.DeviceId;
        internal PlatformGamepadState LastState;
        internal readonly nint NativeIdentity = nativeIdentity;
        internal bool NeedsAnnouncement = true;
        internal bool NeedsReconciliation = true;
        internal uint ReportedUnmappedButtonMask;
        internal readonly ulong ReadingCallbackToken = readingCallbackToken;
    }
}
