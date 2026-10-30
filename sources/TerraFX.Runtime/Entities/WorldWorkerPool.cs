// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Immutable;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldWorkerPool : IDisposable
{
    [ThreadStatic]
    private static int s_currentWorkerIndex;

    [ThreadStatic]
    private static WorldWorkerPool? s_currentWorkerPool;

    private readonly Lock _gate = new();
    private bool _isBatchActive;
    private bool _isDisposed;
    private bool _isStarted;
    private readonly BlockingCollection<WorkBatch>[] _queues;
    private readonly Thread[] _workers;

    [SuppressMessage("Style", "IDE0028", Justification = "The worker and queue arrays are allocated to the exact frozen worker count.")]
    internal WorldWorkerPool(int workerCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workerCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(workerCount, 64);
        WorkerCount = workerCount;
        _queues = new BlockingCollection<WorkBatch>[workerCount];
        _workers = new Thread[workerCount];

        for (var index = 0; index < workerCount; index++)
        {
            _queues[index] = new BlockingCollection<WorkBatch>(1);
        }
    }

    internal int WorkerCount { get; }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            if (_isBatchActive)
            {
                throw new InvalidOperationException("A World worker pool cannot be disposed during an active batch.");
            }

            if (_workers.AsSpan().Contains(Thread.CurrentThread))
            {
                throw new InvalidOperationException("A World worker cannot dispose its own worker pool.");
            }

            _isDisposed = true;

            if (_isStarted)
            {
                foreach (var queue in _queues)
                {
                    queue.CompleteAdding();
                }
            }
        }

        for (var index = 0; index < _workers.Length; index++)
        {
            _workers[index]?.Join();
            _queues[index].Dispose();
        }
    }

    internal void RunBatch(ImmutableArray<Action> callbacks)
    {
        if (callbacks.IsDefault)
        {
            throw new ArgumentException("The World worker callback batch cannot be default.", nameof(callbacks));
        }

        foreach (var callback in callbacks)
        {
            ArgumentNullException.ThrowIfNull(callback);
        }

        if (callbacks.IsEmpty)
        {
            return;
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (_isBatchActive)
            {
                throw new InvalidOperationException("A World worker batch is already active.");
            }

            _isBatchActive = true;

            if (WorkerCount != 0)
            {
                try
                {
                    StartWorkers();
                }
                catch
                {
                    _isBatchActive = false;
                    throw;
                }
            }
        }

        try
        {
            if (WorkerCount == 0)
            {
                foreach (var callback in callbacks)
                {
                    callback();
                }

                return;
            }

            var actions = callbacks.AsSpan().ToArray();
            var batch = new WorkBatch(actions, WorkerCount);

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);

                for (var index = 0; index < WorkerCount; index++)
                {
                    _queues[index].Add(batch);
                }
            }

            try
            {
                batch.Wait();
            }
            finally
            {
                batch.Dispose();
            }

            var failures = new List<Exception>(actions.Length);

            foreach (var exception in batch.Exceptions)
            {
                if (exception is not null)
                {
                    failures.Add(exception);
                }
            }

            if (failures.Count == 1)
            {
                throw failures[0];
            }

            if (failures.Count > 1)
            {
                throw new AggregateException(failures);
            }
        }
        finally
        {
            lock (_gate)
            {
                _isBatchActive = false;
            }
        }
    }

    internal bool TryGetCurrentWorkerIndex(out int workerIndex)
    {
        if (ReferenceEquals(s_currentWorkerPool, this))
        {
            workerIndex = s_currentWorkerIndex;
            return true;
        }

        workerIndex = -1;
        return false;
    }

    private void StartWorkers()
    {
        if (_isStarted)
        {
            return;
        }

        _isStarted = true;
        var startedCount = 0;

        try
        {
            for (var index = 0; index < WorkerCount; index++)
            {
                var workerIndex = index;
                var worker = new Thread(() => WorkerLoop(workerIndex)) {
                    IsBackground = true,
                    Name = $"TerraFX.WorldWorker.{workerIndex}",
                };
                _workers[index] = worker;
                worker.Start();
                startedCount++;
            }
        }
        catch
        {
            _isDisposed = true;

            foreach (var queue in _queues)
            {
                queue.CompleteAdding();
            }

            for (var index = 0; index < startedCount; index++)
            {
                _workers[index].Join();
            }

            foreach (var queue in _queues)
            {
                queue.Dispose();
            }

            throw;
        }
    }

    private void WorkerLoop(int workerIndex)
    {
        var previousPool = s_currentWorkerPool;
        var previousWorkerIndex = s_currentWorkerIndex;
        s_currentWorkerPool = this;
        s_currentWorkerIndex = workerIndex;

        try
        {
            foreach (var batch in _queues[workerIndex].GetConsumingEnumerable())
            {
                batch.Execute(workerIndex);
            }
        }
        finally
        {
            s_currentWorkerPool = previousPool;
            s_currentWorkerIndex = previousWorkerIndex;
        }
    }

    private sealed class WorkBatch : IDisposable
    {
        private readonly Action[] _actions;
        private readonly CountdownEvent _completion;
        private readonly Exception?[] _exceptions;
        private int _failed;

        internal WorkBatch(Action[] actions, int workerCount)
        {
            _actions = actions;
            _completion = new CountdownEvent(workerCount);
            _exceptions = new Exception?[actions.Length];
        }

        internal Exception?[] Exceptions => _exceptions;

        public void Dispose()
            => _completion.Dispose();

        [SuppressMessage("Usage", "CA1031:Do not catch general exception types", Justification = "Worker callbacks must retain their exact ordinary exceptions for synchronous delivery after every worker joins.")]
        internal void Execute(int workerIndex)
        {
            try
            {
                for (var index = workerIndex; index < _actions.Length; index += _completion.InitialCount)
                {
                    if (Volatile.Read(ref _failed) != 0)
                    {
                        break;
                    }

                    try
                    {
                        _actions[index]();
                    }
                    catch (Exception exception)
                    {
                        _exceptions[index] = exception;
                        _ = Interlocked.Exchange(ref _failed, 1);
                    }
                }
            }
            finally
            {
                _ = _completion.Signal();
            }
        }

        internal void Wait()
            => _completion.Wait();
    }
}
