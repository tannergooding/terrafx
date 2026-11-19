// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Entities;
using TerraFX.Integration;

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

    internal WorldReplacementAdoptionPlan PrepareWorldReplacementAdoption(
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
        var scheduleIndex = -1;

        for (var index = 0; index < _worlds.Count; index++)
        {
            var candidate = _worlds[index];

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
                scheduleIndex = index;
            }
        }

        if (schedule is null)
        {
            throw new InvalidOperationException("The previous World is not scheduled by this host.");
        }

        schedule.ValidateAdoption(previousWorld, replacementWorld, hostTimestamp);

        var adapters = new List<WorldInputTimelineAdapter>();

        foreach (var adapter in _inputTimelineByRouter.Values)
        {
            if (ReferenceEquals(adapter.World, previousWorld))
            {
                adapter.ValidateAdoption(previousWorld, replacementWorld, hostTimestamp, schedule.IsPaused);
                adapters.Add(adapter);
            }
        }

        var scheduleSegment = schedule.PrepareWorldReplacementAdoption(replacementWorld, hostTimestamp);
        var adapterAdoptions = new WorldInputTimelineAdapter.WorldReplacementAdoption[adapters.Count];

        for (var index = 0; index < adapters.Count; index++)
        {
            adapterAdoptions[index] = adapters[index].PrepareWorldReplacementAdoption(
                previousWorld,
                replacementWorld,
                hostTimestamp,
                schedule.IsPaused);
        }

        return new WorldReplacementAdoptionPlan(
            this,
            scheduleIndex,
            previousWorld,
            replacementWorld,
            hostTimestamp,
            replacementWorld.TimeScaleSegmentSequence,
            scheduleSegment,
            adapterAdoptions);
    }

    internal bool TryEnqueueWorldReplacement(
        WorldLoadExecution execution,
        out Task<WorldLoadReceipt>? completion)
    {
        ArgumentNullException.ThrowIfNull(execution);
        var workerPool = _workerPool
            ?? throw new InvalidOperationException("The EngineHost worker pool is not initialized.");
        var diagnostics = _diagnostics
            ?? throw new InvalidOperationException("The EngineHost diagnostics service is not initialized.");
        var admissionDomain = _admissionDomain
            ?? throw new InvalidOperationException("The EngineHost admission domain is not initialized.");

        if (!ReferenceEquals(execution.Persistence.Host, this)
            || !execution.TargetWorld.IsHostedBy(workerPool, diagnostics, admissionDomain))
        {
            throw new InvalidOperationException("The World load execution is not bound to this exact EngineHost.");
        }

        var request = new HostWorldReplacementRequest(execution);

        if (!TryEnqueue(request))
        {
            completion = null;
            return false;
        }

        completion = request.Result;
        return true;
    }

    private sealed class HostWorldReplacementRequest : HostActionRequest
    {
        private readonly WorldLoadExecution _execution;

        internal HostWorldReplacementRequest(WorldLoadExecution execution)
        {
            ArgumentNullException.ThrowIfNull(execution);
            _execution = execution;
        }

        internal override Task Completion => _execution.Completion;

        internal Task<WorldLoadReceipt> Result => _execution.Completion;

        [SuppressMessage(
            "Usage",
            "CA1031:Do not catch general exception types",
            Justification = "A captured replacement failure is delivered through its typed task without terminating the EngineHost owner loop.")]
        internal override void Execute(EngineHost host)
        {
            try
            {
                var receipt = _execution.Persistence.CommitLoadAtOwnerBoundary(_execution);

                if (receipt.Status is WorldLoadStatus.Loaded)
                {
                    try
                    {
                        host.AdoptWorldReplacement(
                            _execution.Adoption
                                ?? throw new InvalidOperationException("The hosted replacement omitted its prepared schedule adoption."));
                    }
                    catch (Exception exception)
                    {
                        host.FailPendingRequests(exception);
                        _execution.FailAfterOwnerBoundary(exception);
                        return;
                    }
                }

                _execution.CompleteAfterOwnerBoundary(receipt);
            }
            catch (Exception exception)
            {
                _execution.FailAfterOwnerBoundary(exception);
            }
        }

        internal override void Fail(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            _execution.FailBeforeOwnerBoundary(exception);
        }
    }
}
