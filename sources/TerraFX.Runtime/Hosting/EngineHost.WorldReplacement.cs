// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Entities;

namespace TerraFX.Hosting;

public sealed partial class EngineHost
{
    internal long GetWorldReplacementHostTimestamp()
    {
        EnsureOwnerThread();
        return (_platformOwner
            ?? throw new InvalidOperationException("The EngineHost platform owner is not initialized."))
            .GetHostTimestamp();
    }

    internal bool TryEnqueueWorldReplacement(
        WorldReplacementOperation operation,
        out Task<WorldLoadReceipt>? completion)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var workerPool = _workerPool
            ?? throw new InvalidOperationException("The EngineHost worker pool is not initialized.");
        var diagnostics = _diagnostics
            ?? throw new InvalidOperationException("The EngineHost diagnostics service is not initialized.");
        var admissionDomain = _admissionDomain
            ?? throw new InvalidOperationException("The EngineHost admission domain is not initialized.");

        if (!operation.TargetWorld.IsHostedBy(workerPool, diagnostics, admissionDomain))
        {
            throw new InvalidOperationException("The load target is not bound to this exact EngineHost.");
        }

        var request = new HostWorldReplacementRequest(operation);

        if (!TryEnqueue(request))
        {
            completion = null;
            return false;
        }

        completion = request.Result;
        return true;
    }

    internal void ValidateWorldReplacementAdoption(
        World previousWorld,
        World replacementWorld,
        long hostTimestamp)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(previousWorld);
        ArgumentNullException.ThrowIfNull(replacementWorld);

        if (ReferenceEquals(previousWorld, replacementWorld))
        {
            throw new ArgumentException("A replacement World must be a distinct object.", nameof(replacementWorld));
        }

        var workerPool = _workerPool
            ?? throw new InvalidOperationException("The EngineHost worker pool is not initialized.");
        var diagnostics = _diagnostics
            ?? throw new InvalidOperationException("The EngineHost diagnostics service is not initialized.");
        var admissionDomain = _admissionDomain
            ?? throw new InvalidOperationException("The EngineHost admission domain is not initialized.");

        if (!replacementWorld.IsHostedBy(workerPool, diagnostics, admissionDomain))
        {
            throw new InvalidOperationException("The replacement World was not prepared for this exact EngineHost.");
        }

        HostedWorldSchedule? schedule = null;

        foreach (var candidate in _worlds)
        {
            if (ReferenceEquals(candidate.World, replacementWorld))
            {
                throw new InvalidOperationException("The replacement World is already scheduled by this host.");
            }

            if (ReferenceEquals(candidate.World, previousWorld))
            {
                if (schedule is not null)
                {
                    throw new InvalidOperationException("The previous World has more than one host schedule.");
                }

                schedule = candidate;
            }
        }

        if (schedule is null)
        {
            throw new InvalidOperationException("The previous World is not scheduled by this host.");
        }

        schedule.ValidateAdoption(previousWorld, replacementWorld, hostTimestamp);

        foreach (var adapter in _inputTimelineByRouter.Values)
        {
            if (ReferenceEquals(adapter.World, previousWorld))
            {
                adapter.ValidateAdoption(previousWorld, replacementWorld, hostTimestamp, schedule.IsPaused);
            }
        }
    }

    private sealed class HostWorldReplacementRequest : HostActionRequest
    {
        private readonly TaskCompletionSource<WorldLoadReceipt> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly WorldReplacementOperation _operation;

        internal HostWorldReplacementRequest(WorldReplacementOperation operation)
        {
            ArgumentNullException.ThrowIfNull(operation);
            _operation = operation;
        }

        internal override Task Completion => _completion.Task;

        internal Task<WorldLoadReceipt> Result => _completion.Task;

        [SuppressMessage(
            "Usage",
            "CA1031:Do not catch general exception types",
            Justification = "A captured replacement failure is delivered through its typed task without terminating the EngineHost owner loop.")]
        internal override void Execute(EngineHost host)
        {
            try
            {
                _ = _completion.TrySetResult(_operation.ExecuteAtOwnerBoundary(host));
            }
            catch (Exception exception)
            {
                _operation.AbandonCapturedState();
                _ = _completion.TrySetException(exception);
            }
            finally
            {
                _operation.EndLoadOperation();
            }
        }

        internal override void Fail(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            _operation.AbandonCapturedState();
            _operation.EndLoadOperation();
            _ = _completion.TrySetException(exception);
        }
    }
}
