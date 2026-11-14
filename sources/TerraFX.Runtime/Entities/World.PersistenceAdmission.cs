// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace TerraFX.Entities;

public sealed partial class World
{
    private static long CalculateRestoreWorldOwnedBytes(
        WorldOptions options,
        ResolvedWorldOptions resolvedOptions,
        WorldRestoreIdentityState restoreIdentityState)
    {
        var archetypeDirectoryBytes = checked(2 * ArrayBytes(options.MaximumArchetypes, IntPtr.Size));
        var resourceBytes = 0L;
        var systemBytes = checked(
            options.Registration.SystemInstanceStorageByteReservation
            + CalculateSystemInvocationStorageByteReservation(resolvedOptions.CpuWorkerCount));

        foreach (var resource in options.Registration.Resources)
        {
            resourceBytes = checked(resourceBytes + resource.GetStorageByteReservation());
        }

        var resourceAndSystemStateBytes = checked(resourceBytes + systemBytes);

        if (resourceAndSystemStateBytes > options.SchedulingLimits.MaximumResourceAndSystemStateBytes)
        {
            throw new InvalidOperationException("The selected resource partitions and system instances exceed the resource and system state budget.");
        }

        var sceneSupportBytes = options.Registration.SceneSupport?.GetWorldStorageByteReservation(options) ?? 0;
        var cpuExtractionSupportBytes =
            options.Registration.CpuExtractionSupport?.GetWorldStorageByteReservation(options) ?? 0;
        var ownerControlQueueBytes = ArrayBytes(options.MaximumPendingOwnerRequests, IntPtr.Size);
        return checked(
            FreshWorldControlBytes
            + archetypeDirectoryBytes
            + ownerControlQueueBytes
            + resourceAndSystemStateBytes
            + sceneSupportBytes
            + cpuExtractionSupportBytes
            + CalculateSubscriptionStorageByteReservation()
            + restoreIdentityState.IdentityLedger.OwnedByteReservation
            + restoreIdentityState.EntitySlots.OwnedByteReservation);
    }

    private static WorldOptions CreateRestoreOptions(
        World targetWorld,
        WorldRestoreIdentityState restoreIdentityState,
        WorldPersistenceConfiguration configuration,
        int tickRate,
        float timeScale)
        => new(targetWorld._options.Registration) {
            AdmissionDomain = targetWorld._admissionDomain,
            CpuWorkerCount = targetWorld._workerPool.WorkerCount,
            Enable2DFrame = targetWorld._options.Enable2DFrame,
            FrameExtent2 = targetWorld._options.FrameExtent2,
            FrameExtent3 = targetWorld._options.FrameExtent3,
            InitialReplayLineageId = restoreIdentityState.ReplayLineageId,
            InitialTimeScale = timeScale,
            MaximumActiveSceneInstances = configuration.MaximumActiveSceneInstances,
            MaximumArchetypes = configuration.MaximumArchetypes,
            MaximumComponentTypes = configuration.MaximumComponentTypes,
            MaximumDeferredCommands = configuration.MaximumDeferredCommands,
            MaximumEntityReferencePositions = configuration.MaximumEntityReferencePositions,
            MaximumEntitySlots = configuration.MaximumEntitySlots,
            MaximumIdentityAssignments = configuration.MaximumIdentityAssignments,
            MaximumIdentityBranches = configuration.MaximumIdentityBranches,
            MaximumLiveEntities = configuration.MaximumLiveEntities,
            MaximumPendingOwnerRequests = configuration.MaximumPendingOwnerRequests,
            MaximumPersistenceAdmissionBytes = configuration.MaximumPersistenceAdmissionBytes,
            MaximumRecordedControls = configuration.MaximumRecordedControls,
            MaximumSceneOccurrences = configuration.MaximumSceneOccurrences,
            MaximumSnapshotBytes = configuration.MaximumSnapshotBytes,
            Profile = targetWorld.Profile,
            SchedulingLimits = configuration.SchedulingLimits,
            SpatialProfileId = targetWorld._spatialProfileId,
            SpatialSettings = targetWorld._spatialSettings,
            TickRate = tickRate,
            WorldGuid = restoreIdentityState.WorldGuid,
        };

    internal void ReleasePersistenceAdmissionBytes(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

        lock (_persistenceAdmissionGate)
        {
            if (bytes > _activePersistenceAdmissionBytes)
            {
                throw new InvalidOperationException("The released persistence admission bytes exceed the live reservation.");
            }

            _activePersistenceAdmissionBytes -= bytes;
        }
    }

    internal bool TryGrowPersistenceAdmissionBytes(WorldDomainReservation domainReservation, long bytes)
    {
        ArgumentNullException.ThrowIfNull(domainReservation);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

        lock (_persistenceAdmissionGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            if (bytes > _maximumPersistenceAdmissionBytes - _activePersistenceAdmissionBytes)
            {
                return false;
            }

            _activePersistenceAdmissionBytes += bytes;
        }

        if (domainReservation.TryReserveAdditional(bytes))
        {
            return true;
        }

        ReleasePersistenceAdmissionBytes(bytes);
        return false;
    }

    internal bool TryPrepareGuidReplacement(
        out WorldGuidReplacementPreparation? preparation,
        out WorldGuidRefusal refusal)
    {
        lock (_sceneStorageGate)
        {
            lock (_persistenceAdmissionGate)
            {
                lock (_ownerControlGate)
                {
                    if (Volatile.Read(ref _disposed) != 0)
                    {
                        preparation = null;
                        refusal = new WorldGuidRefusal(WorldGuidRefusal.TargetBindingStale);
                        return false;
                    }

                    return _registry.TryPrepareReplacement(_worldGuidGrant, out preparation, out refusal);
                }
            }
        }
    }

    internal bool TryPrepareLoadReplacement(
        Guid replacementWorldGuid,
        long sourceBytes,
        long targetBytes,
        long dataBytes,
        long cleanupBytes,
        out WorldGuidReplacementPreparation? preparation,
        out WorldGuidRefusal bindingRefusal,
        out WorldAdmissionRefusal operationCreditRefusal)
    {
        if (replacementWorldGuid == Guid.Empty)
        {
            throw new ArgumentException("The replacement WorldGuid cannot be empty.", nameof(replacementWorldGuid));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dataBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cleanupBytes);
        _ = checked(sourceBytes + targetBytes + dataBytes + cleanupBytes);

        lock (_sceneStorageGate)
        {
            lock (_persistenceAdmissionGate)
            {
                lock (_ownerControlGate)
                {
                    if (Volatile.Read(ref _disposed) != 0)
                    {
                        preparation = null;
                        bindingRefusal = new WorldGuidRefusal(WorldGuidRefusal.TargetBindingStale);
                        operationCreditRefusal = WorldAdmissionRefusal.None;
                        return false;
                    }

                    if (!_registry.TryReserveReplacement(
                            _worldGuidGrant,
                            replacementWorldGuid,
                            out var replacementGrant,
                            out bindingRefusal))
                    {
                        preparation = null;
                        operationCreditRefusal = WorldAdmissionRefusal.None;
                        return false;
                    }

                    if (!_admissionDomain.TryReserveLoadOperationCredits(
                        sourceBytes,
                        targetBytes,
                        dataBytes,
                        cleanupBytes,
                        out var credits,
                        out operationCreditRefusal))
                    {
                        if (!_registry.TryReleaseFresh(replacementGrant))
                        {
                            throw new InvalidOperationException("The refused load admission could not release its successor binding.");
                        }

                        preparation = null;
                        bindingRefusal = default;
                        return false;
                    }

                    if (credits is null)
                    {
                        if (!_registry.TryReleaseFresh(replacementGrant))
                        {
                            throw new InvalidOperationException("The inconsistent load admission could not release its successor binding.");
                        }

                        throw new InvalidOperationException("The successful load-credit reservation did not produce its bundle.");
                    }

                    try
                    {
                        preparation = new WorldGuidReplacementPreparation(
                            _registry,
                            _worldGuidGrant,
                            replacementGrant,
                            credits);
                        bindingRefusal = default;
                        operationCreditRefusal = WorldAdmissionRefusal.None;
                        return true;
                    }
                    catch (OutOfMemoryException exception)
                    {
                        Exception? cleanupFailure = null;

                        try
                        {
                            credits.Dispose();
                        }
                        catch (InvalidOperationException releaseException)
                        {
                            cleanupFailure = releaseException;
                        }

                        if (!_registry.TryReleaseFresh(replacementGrant))
                        {
                            var bindingReleaseFailure = new InvalidOperationException(
                                "The failed load admission could not release its successor binding.");
                            cleanupFailure = (cleanupFailure is null)
                                ? bindingReleaseFailure
                                : new AggregateException(cleanupFailure, bindingReleaseFailure);
                        }

                        if (cleanupFailure is not null)
                        {
                            throw new AggregateException(exception, cleanupFailure);
                        }

                        throw;
                    }
                }
            }
        }
    }

    internal bool TryPrepareLoadReplacement(
        long sourceBytes,
        long targetBytes,
        long dataBytes,
        long cleanupBytes,
        out WorldGuidReplacementPreparation? preparation,
        out WorldGuidRefusal bindingRefusal,
        out WorldAdmissionRefusal operationCreditRefusal)
        => TryPrepareLoadReplacement(
            _worldGuid,
            sourceBytes,
            targetBytes,
            dataBytes,
            cleanupBytes,
            out preparation,
            out bindingRefusal,
            out operationCreditRefusal);

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "A successful result transfers the exact domain reservation into the sealed restore construction plan; the failure path releases it.")]
    internal bool TryPrepareRestoreConstructionPlan(
        WorldRestoreIdentityState restoreIdentityState,
        WorldPersistenceConfiguration configuration,
        WorldGuidReplacementPreparation replacementPreparation,
        int tickRate,
        float timeScale,
        out WorldConstructionPlan? constructionPlan,
        out WorldGuidRefusal bindingRefusal,
        out WorldAdmissionRefusal admissionRefusal)
    {
        ArgumentNullException.ThrowIfNull(restoreIdentityState);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(replacementPreparation);
        ArgumentOutOfRangeException.ThrowIfLessThan(tickRate, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tickRate, 1000);

        if (!float.IsFinite(timeScale))
        {
            throw new ArgumentOutOfRangeException(nameof(timeScale));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(timeScale, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(timeScale, 4);
        constructionPlan = null;
        bindingRefusal = default;
        admissionRefusal = WorldAdmissionRefusal.None;

        if (!replacementPreparation.IsBoundTo(_registry, _worldGuidGrant))
        {
            bindingRefusal = new WorldGuidRefusal(WorldGuidRefusal.TargetBindingStale);
            return false;
        }

        if (!replacementPreparation.TryGetReplacementGrant(out var replacementGrant))
        {
            bindingRefusal = new WorldGuidRefusal(WorldGuidRefusal.TargetBindingStale);
            return false;
        }

        var options = CreateRestoreOptions(this, restoreIdentityState, configuration, tickRate, timeScale);
        var resolvedOptions = options.ValidateAndResolve();
        var worldOwnedBytes = CalculateRestoreWorldOwnedBytes(options, resolvedOptions, restoreIdentityState);
        WorldDomainReservation? domainReservation = null;
        WorldSystemQuarantineReservation? quarantineReservation = null;
        WorldDiagnosticsCapability? diagnosticsCapability = null;
        var lineageGrant = default(WorldLineageGrant);
        var lineageReserved = false;
        var planCreated = false;

        void ReleaseResources()
        {
            try
            {
                if (lineageReserved && !_admissionDomain.TryReleaseFreshLineage(lineageGrant))
                {
                    throw new InvalidOperationException("The refused restore plan could not release its retained lineage reservation.");
                }
            }
            finally
            {
                try
                {
                    quarantineReservation?.Release();
                }
                finally
                {
                    try
                    {
                        diagnosticsCapability?.Close();
                    }
                    finally
                    {
                        domainReservation?.Dispose();
                    }
                }
            }
        }

        try
        {
            if (!_admissionDomain.TryReserveWorld(worldOwnedBytes, out domainReservation)
                || (domainReservation is null))
            {
                admissionRefusal = WorldAdmissionRefusal.OperationCapacity;
                return false;
            }

            if (!_admissionDomain.TryReserveRetainedLineageHistoryForBranch(
                    domainReservation,
                    restoreIdentityState.LineageMembership,
                    restoreIdentityState.ActiveBranchId,
                    restoreIdentityState.IssuanceNamespace,
                    out lineageGrant,
                    out admissionRefusal))
            {
                return false;
            }

            lineageReserved = true;
            diagnosticsCapability = _diagnosticsCapability.Diagnostics.CreateBorrowedWorldCapability(
                restoreIdentityState.WorldGuid,
                _executionSessionId);
            quarantineReservation = WorldSystemInstanceRegistry.ReserveQuarantineCapacity(options.Registration.Systems.Length);

            if (!replacementPreparation.IsTargetCurrent())
            {
                bindingRefusal = new WorldGuidRefusal(WorldGuidRefusal.TargetBindingStale);
                return false;
            }

            constructionPlan = new WorldConstructionPlan(
                resolvedOptions,
                restoreIdentityState.WorldGuid,
                restoreIdentityState.ReplayLineageId,
                restoreIdentityState.ActiveBranchId,
                _executionSessionId,
                _admissionDomain,
                ownsAdmissionDomain: false,
                ownsWorkerPool: false,
                domainReservation,
                lineageGrant,
                _registry,
                replacementGrant,
                diagnosticsCapability,
                restoreIdentityState.IdentityLedger,
                quarantineReservation,
                _workerPool,
                restoreIdentityState,
                replacementPreparation);
            domainReservation = null;
            planCreated = true;
            return true;
        }
        finally
        {
            if (!planCreated)
            {
                ReleaseResources();
            }
        }
    }

    internal bool TryReserveLoadOperationCredits(
        long sourceBytes,
        long targetBytes,
        long dataBytes,
        long cleanupBytes,
        out WorldDomainOperationCreditBundle? credits,
        out WorldAdmissionRefusal refusal)
    {
        lock (_sceneStorageGate)
        {
            lock (_persistenceAdmissionGate)
            {
                lock (_ownerControlGate)
                {
                    if (Volatile.Read(ref _disposed) != 0)
                    {
                        credits = null;
                        refusal = WorldAdmissionRefusal.DomainClosed;
                        return false;
                    }

                    return _admissionDomain.TryReserveLoadOperationCredits(
                        sourceBytes,
                        targetBytes,
                        dataBytes,
                        cleanupBytes,
                        out credits,
                        out refusal);
                }
            }
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "A successful result transfers the exact domain reservation to the returned persistence reservation; the finally path releases it when admission fails.")]
    internal bool TryReservePersistenceAdmissionBytes(
        long bytes,
        long maximumAdditionalBytes,
        out WorldPersistenceAdmissionReservation? reservation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumAdditionalBytes);
        reservation = null;

        lock (_persistenceAdmissionGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            if (bytes > _maximumPersistenceAdmissionBytes - _activePersistenceAdmissionBytes)
            {
                return false;
            }

            _activePersistenceAdmissionBytes += bytes;
        }

        WorldDomainReservation? domainReservation = null;
        var reservationCreated = false;

        try
        {
            if (!_admissionDomain.TryReserveWorld(bytes, maximumAdditionalBytes, out domainReservation)
                || (domainReservation is null)
                || (Volatile.Read(ref _disposed) != 0))
            {
                return false;
            }

            reservation = new WorldPersistenceAdmissionReservation(this, domainReservation, bytes);
            domainReservation = null;
            reservationCreated = true;
            return true;
        }
        finally
        {
            if (!reservationCreated)
            {
                domainReservation?.Dispose();
                ReleasePersistenceAdmissionBytes(bytes);
            }
        }
    }

    internal bool TryReservePersistenceAdmissionBytes(long bytes, out WorldPersistenceAdmissionReservation? reservation)
        => TryReservePersistenceAdmissionBytes(bytes, maximumAdditionalBytes: 0, out reservation);
}
