// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace TerraFX.Entities;

public sealed partial class World
{
    private static bool HasAuthoritativeState(SystemRegistration registration)
    {
        foreach (var leaf in registration.Factory.Descriptor.SourceState.Leaves)
        {
            if (leaf.Purpose == WorldSystemSourcePurpose.Authoritative)
            {
                return true;
            }
        }

        return false;
    }

    private static void Rethrow(Exception exception)
    {
        ExceptionDispatchInfo.Capture(exception).Throw();
        throw new InvalidOperationException("Exception dispatch unexpectedly returned.");
    }

    /// <summary>Executes and commits exactly one fixed simulation tick.</summary>
    /// <returns>The immutable committed publication and its completed delivery report.</returns>
    /// <exception cref="InvalidOperationException">The World is unavailable, faulted, busy, or not accessed on its owner thread.</exception>
    /// <exception cref="WorldStepException">A scheduled system or prepublication operation failed.</exception>
    public WorldStepReceipt Step()
        => ExecuteTick();

    internal WorldStepReceipt ExecuteTick()
    {
        ThrowIfAttachmentCallbackActive();
        ThrowIfSystemInvocationActive();
        ThrowIfDisposed();
        ThrowIfFaulted();
        EnsureOwnerThread();
        PumpControlBoundary();

        if (Interlocked.CompareExchange(ref _isStructuralOperationActive, 1, 0) != 0)
        {
            throw new InvalidOperationException("The World already has an active operation.");
        }

        if (Interlocked.CompareExchange(ref _isTickActive, 1, 0) != 0)
        {
            _ = Interlocked.Exchange(ref _isStructuralOperationActive, 0);
            throw new InvalidOperationException("The World already has an active tick.");
        }

        var tick = _nextTickIndex;
        var publicationCommitted = false;

        try
        {
            var nextTickIndex = checked(tick + 1);
            var nextPublicationSequence = checked(_publicationSequence + 1);
            var schedule = Registration.Schedule;
            _ = checked(_nextSystemInvocationEpoch + schedule.ExecutionCount);
            _ = checked(_canonicalMutationRevision + CountAuthoritativeSystemInvocations());
            var step = new SimulationStepContext(tick, _tickRate);
            using var commandStaging = new WorldCommandStaging(
                this,
                _maximumDeferredCommands,
                _maximumCommandStagingBytes);
            var commandReceipts = ImmutableArray<CommandReceipt>.Empty;

            var batchIndex = 0;

            for (var stageIndex = (int)SimulationStage.Begin; stageIndex <= (int)SimulationStage.Extract; stageIndex++)
            {
                while (batchIndex < schedule.ExecutionPlanBatchCount)
                {
                    var batchStart = schedule.GetExecutionPlanBatchStart(batchIndex);
                    var system = schedule.GetSystemAtExecutionPlanPosition(batchStart);

                    if ((int)system.Stage != stageIndex)
                    {
                        break;
                    }

                    ExecuteSystemBatch(
                        batchStart,
                        schedule.GetExecutionPlanBatchEnd(batchIndex),
                        step,
                        commandStaging);
                    batchIndex++;
                }

                if (stageIndex == (int)SimulationStage.Structural)
                {
                    commandReceipts = CommitCommandBatch(commandStaging);
                    ValidateWorldEntityReferences();
                }
            }

            ValidateWorldEntityReferences();

            var frameCohort = _currentFrameCohort;
            var publication = new CommittedWorldPublication(
                _executionSessionId,
                _ownerId,
                nextPublicationSequence,
                _worldGuid,
                tick,
                nextTickIndex,
                frameCohort.Frame3,
                frameCohort.Frame2,
                commandReceipts,
                []);
            var capturedSubscriptions = CaptureSubscriptions();
            var results = new SubscriberDeliveryResult[capturedSubscriptions.Length];
            var failureDiagnostic = (capturedSubscriptions.Length == 0)
                ? null
                : CreateSubscriberDeliveryDiagnostic(publication);
            var failures = new SubscriberDeliveryFailure?[capturedSubscriptions.Length];

            for (var index = 0; index < capturedSubscriptions.Length; index++)
            {
                var subscriptionId = capturedSubscriptions[index].SubscriptionId;
                results[index] = new SubscriberDeliveryResult(
                    publication,
                    subscriptionId,
                    SubscriberDeliveryStatus.Delivered,
                    null);
                failures[index] = (failureDiagnostic is null)
                    ? throw new InvalidOperationException("The subscriber failure diagnostic was not prepared.")
                    : new SubscriberDeliveryFailure(failureDiagnostic);
            }

            var deliveryReport = new PublicationDeliveryReport(publication, results, failures);
            var receipt = new WorldStepReceipt(publication, deliveryReport);

            _nextTickIndex = nextTickIndex;
            _publicationSequence = nextPublicationSequence;
            _committedMutationRevision = Volatile.Read(ref _canonicalMutationRevision);
            Volatile.Write(ref _committedPublication, publication);
            publicationCommitted = true;
            DeliverPublication(publication, capturedSubscriptions, deliveryReport);
            return receipt;
        }
        catch (Exception exception)
        {
            if (publicationCommitted)
            {
                throw;
            }

            FaultWorld(null, exception, "WORLD_STEP_FAILED", "World.Step");
            var diagnostic = _lastFault
                ?? throw new InvalidOperationException("The failed World tick did not retain its fault diagnostic.", exception);
            throw new WorldStepException(tick, diagnostic, exception);
        }
        finally
        {
            Volatile.Write(ref _isTickActive, 0);
            _ = Interlocked.Exchange(ref _isStructuralOperationActive, 0);
        }
    }

    private int CountAuthoritativeSystemInvocations()
    {
        var count = 0;

        foreach (var registration in Registration.Systems)
        {
            if (HasAuthoritativeState(registration))
            {
                count++;
            }
        }

        return count;
    }

    [SuppressMessage("Usage", "CA1031:Do not catch general exception types", Justification = "The worker batch joins all admitted callbacks before selecting an exact system failure in deterministic plan order.")]
    private void ExecuteSystemBatch(
        int start,
        int end,
        SimulationStepContext step,
        WorldCommandStaging commandStaging)
    {
        var invocationEpochStart = checked(_nextSystemInvocationEpoch + 1);
        var batchLength = end - start;
        _nextSystemInvocationEpoch = checked(_nextSystemInvocationEpoch + batchLength);

        for (var planPosition = start; planPosition < end; planPosition++)
        {
            var registrationIndex = _scheduledSystemRegistrationIndices[planPosition];
            var registration = Registration.Systems[registrationIndex];

            if (HasAuthoritativeState(registration))
            {
                _ = AdvanceCanonicalMutationRevision();
            }
        }

        for (var planPosition = start; planPosition < end; planPosition++)
        {
            var registrationIndex = _scheduledSystemRegistrationIndices[planPosition];
            var registration = Registration.Systems[registrationIndex];

            if (registration.Description.ThreadMode != SystemThreadMode.OwnerThread)
            {
                continue;
            }

            var system = _systemInstances[registrationIndex]
                ?? throw new InvalidOperationException("A scheduled system has no claimed World instance.");
            var invocationEpoch = checked(invocationEpochStart + planPosition - start);
            var failure = RunRegisteredSystem(registration, system, step, invocationEpoch, commandStaging);

            if (failure is not null)
            {
                Rethrow(failure);
            }
        }

        var parallelCount = 0;

        for (var planPosition = start; planPosition < end; planPosition++)
        {
            var registrationIndex = _scheduledSystemRegistrationIndices[planPosition];

            if (Registration.Systems[registrationIndex].Description.ThreadMode == SystemThreadMode.Parallel)
            {
                parallelCount++;
            }
        }

        if (parallelCount == 0)
        {
            return;
        }

        var callbacks = ImmutableArray.CreateBuilder<Action>(parallelCount);
        var callbackFailures = new Exception?[parallelCount];
        var callbackIndex = 0;

        for (var planPosition = start; planPosition < end; planPosition++)
        {
            var registrationIndex = _scheduledSystemRegistrationIndices[planPosition];
            var registration = Registration.Systems[registrationIndex];

            if (registration.Description.ThreadMode != SystemThreadMode.Parallel)
            {
                continue;
            }

            var system = _systemInstances[registrationIndex]
                ?? throw new InvalidOperationException("A scheduled system has no claimed World instance.");
            var invocationEpoch = checked(invocationEpochStart + planPosition - start);
            var capturedCallbackIndex = callbackIndex++;
            callbacks.Add(() => {
                var failure = RunRegisteredSystem(registration, system, step, invocationEpoch, commandStaging);

                if (failure is not null)
                {
                    callbackFailures[capturedCallbackIndex] = failure;
                    Rethrow(failure);
                }
            });
        }

        Exception? workerFailure = null;

        try
        {
            _workerPool.RunBatch(callbacks.MoveToImmutable());
        }
        catch (Exception exception)
        {
            workerFailure = exception;
        }

        foreach (var callbackFailure in callbackFailures)
        {
            if (callbackFailure is not null)
            {
                Rethrow(callbackFailure);
            }
        }

        if (workerFailure is not null)
        {
            Rethrow(workerFailure);
        }
    }

    [SuppressMessage("Usage", "CA1031:Do not catch general exception types", Justification = "System Execute, revocation, and state validation must preserve the first exact invocation failure while always completing scope cleanup.")]
    private Exception? RunRegisteredSystem(
        SystemRegistration registration,
        IWorldSystem system,
        SimulationStepContext step,
        long invocationEpoch,
        WorldCommandStaging commandStaging)
    {
        var dispatch = new WorldSystemInvocationDispatch(this, registration.Description, invocationEpoch, commandStaging);
        Exception? invocationFailure = null;

        try
        {
            dispatch.Start();
            var commandWriter = new CommandWriter(dispatch);
            var context = new WorldSystemContext(step, commandWriter);
            system.Execute(context);
        }
        catch (Exception exception)
        {
            invocationFailure = dispatch.RecordFailure(exception);
        }
        finally
        {
            dispatch.Close();
        }

        try
        {
            registration.Factory.ValidationControl.Validate(system, registration.Description.SystemId);
        }
        catch (Exception exception)
        {
            invocationFailure ??= exception;
        }
        finally
        {
            try
            {
                dispatch.Revoke();
            }
            catch (Exception exception)
            {
                invocationFailure ??= dispatch.RecordFailure(exception);
            }
        }

        return dispatch.Failure ?? invocationFailure;
    }
}
