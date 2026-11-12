// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Platform;

[SupportedOSPlatform("windows")]
internal sealed class WindowsGameInputCapture : IDisposable
{
    private readonly BlockingCollection<Action<WindowsGameInputCaptureWorker>> _commands = [];
    private readonly TaskCompletionSource<PlatformServiceStatus> _initialization = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _isDisposed;
    private readonly int _ownerThreadId;
    private int _pendingSampleCount;
    private ExceptionDispatchInfo? _workerFailure;
    private readonly ConcurrentQueue<PlatformInputSample> _pendingSamples = new();
    private readonly PlatformInputSampleQueue _samples;
    private readonly TaskCompletionSource _workerExited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread? _workerThread;

    internal WindowsGameInputCapture(
        PlatformInputSampleQueue samples,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _ownerThreadId = Environment.CurrentManagedThreadId;
        _samples = samples;

        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            GamepadStatus = PlatformServiceStatus.Unavailable;
            _commands.CompleteAdding();
            return;
        }

        var workerThread = new Thread(() => RunWorker(timeProvider)) {
            IsBackground = true,
            Name = "TerraFX GameInput host",
        };
        _workerThread = workerThread;

        try
        {
            workerThread.SetApartmentState(ApartmentState.MTA);
            workerThread.Start();
            GamepadStatus = _initialization.Task.GetAwaiter().GetResult();
        }
        catch
        {
            _commands.CompleteAdding();

            if (workerThread.IsAlive)
            {
                workerThread.Join();
            }

            _commands.Dispose();
            throw;
        }
    }

    internal PlatformServiceStatus GamepadStatus { get; }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Each MTA command must transfer its exact failure to the waiting Platform owner without terminating the dispatcher.")]
    private static void ExecuteCommand(
        WindowsGameInputCaptureWorker worker,
        Action<WindowsGameInputCaptureWorker> action,
        TaskCompletionSource completion)
    {
        try
        {
            action(worker);
            _ = completion.TrySetResult();
        }
        catch (Exception exception)
        {
            _ = completion.TrySetException(exception);
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA1065:Do not raise exceptions in unexpected locations",
        Justification = "Disposal synchronously joins the MTA GameInput owner and reports failures instead of leaking native ownership.")]
    public void Dispose()
    {
        EnsureOwnerThread();

        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        if (_workerThread is { } workerThread)
        {
            _commands.CompleteAdding();
            workerThread.Join();
        }

        _commands.Dispose();
        Volatile.Read(ref _workerFailure)?.Throw();
    }

    internal void Pump()
    {
        InvokeWorker(static worker => worker.Pump());
        DrainPendingSamples();
    }

    internal void RequestDeviceReconciliation(nint nativeIdentity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero((long)nativeIdentity);
        InvokeWorker(worker => worker.RequestDeviceReconciliation(nativeIdentity));
        DrainPendingSamples();
    }

    internal void RequestReconciliation()
    {
        InvokeWorker(static worker => worker.RequestReconciliation());
        DrainPendingSamples();
    }

    private void DrainPendingSamples()
    {
        EnsureOwnerThread();

        while (_pendingSamples.TryPeek(out var sample))
        {
            if (!_samples.HasCapacity || !_samples.TryEnqueue(in sample))
            {
                return;
            }

            if (!_pendingSamples.TryDequeue(out _))
            {
                throw new InvalidOperationException("The GameInput sample queue changed before owner-thread retirement.");
            }

            _ = Interlocked.Decrement(ref _pendingSampleCount);
        }
    }

    private void EnsureActiveOwnerThread()
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
    }

    private void EnsureOwnerThread()
    {
        if (_ownerThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("Windows GameInput capture admission is restricted to the Platform owner thread.");
        }
    }

    private void InvokeWorker(Action<WindowsGameInputCaptureWorker> action)
    {
        EnsureActiveOwnerThread();
        ArgumentNullException.ThrowIfNull(action);

        if (GamepadStatus is not PlatformServiceStatus.Available)
        {
            return;
        }

        var workerThread = _workerThread
            ?? throw new InvalidOperationException("The GameInput MTA worker was not initialized.");

        if (!workerThread.IsAlive)
        {
            Volatile.Read(ref _workerFailure)?.Throw();
            throw new InvalidOperationException("The GameInput MTA worker exited unexpectedly.");
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _commands.Add(worker => ExecuteCommand(worker, action, completion));
        var completedTask = Task.WhenAny(completion.Task, _workerExited.Task).GetAwaiter().GetResult();

        if (!ReferenceEquals(completedTask, completion.Task))
        {
            Volatile.Read(ref _workerFailure)?.Throw();
            throw new InvalidOperationException("The GameInput MTA worker exited before completing an owner request.");
        }

        completion.Task.GetAwaiter().GetResult();
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "MTA initialization and disposal failures must be retained and transferred to the Platform owner.")]
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The MTA worker exclusively owns the capture object and disposes it on the same thread before exit.")]
    private void RunWorker(TimeProvider timeProvider)
    {
        WindowsGameInputCaptureWorker? worker = null;

        try
        {
            worker = new WindowsGameInputCaptureWorker(TryEnqueueSample, timeProvider);
            _ = _initialization.TrySetResult(worker.GamepadStatus);

            foreach (var command in _commands.GetConsumingEnumerable())
            {
                command(worker);
            }
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _workerFailure, ExceptionDispatchInfo.Capture(exception));
            _ = _initialization.TrySetException(exception);
        }
        finally
        {
            if (worker is not null)
            {
                try
                {
                    worker.Dispose();
                }
                catch (Exception exception)
                {
                    Volatile.Write(ref _workerFailure, ExceptionDispatchInfo.Capture(exception));
                }
            }

            _ = _workerExited.TrySetResult();
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

    private bool TryEnqueueSample(PlatformInputSample sample)
    {
        if (Interlocked.Increment(ref _pendingSampleCount) > PlatformInputSampleQueue.DefaultCapacity)
        {
            _ = Interlocked.Decrement(ref _pendingSampleCount);
            return false;
        }

        _pendingSamples.Enqueue(sample);
        return true;
    }
}
