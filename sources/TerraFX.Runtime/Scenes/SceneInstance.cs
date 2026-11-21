// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;

namespace TerraFX.Scenes;

/// <summary>Identifies one activated Scene within its exact World.</summary>
public sealed class SceneInstance
{
    private const int LifecycleActivating = 1;
    private const int LifecycleActive = 2;
    private const int LifecycleCleaning = 4;
    private const int LifecycleCleanupComplete = 5;
    private const int LifecycleCleanupFailed = 6;
    private const int LifecycleConstructed = 0;
    private const int LifecycleInactivePending = 3;

    private World.WorldSceneControlAppend? _cleanupControlAppend;
    private ExceptionDispatchInfo? _cleanupFailure;
    private SceneUnloadRequest? _cleanupRequest;
    private readonly int _entityCount;
    private SceneExternalBindingCaptureRow[]? _externalBindings;
    private uint _firstEntityIdentityOrdinal;
    private int _lifecycle;
    private readonly int _occurrenceCount;
    private SceneExpansionPlan? _plan;
    private readonly SceneWorldRegistry _registry;
    private Guid _sceneInstanceId;
    private readonly World _world;

    internal SceneInstance(
        World world,
        SceneWorldRegistry registry,
        SceneExpansionPlan plan,
        SceneExternalBindingCaptureRow[] externalBindings)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(externalBindings);
        _world = world;
        _registry = registry;
        _plan = plan;
        _externalBindings = externalBindings;
        _entityCount = plan.Entities.Length;
        _occurrenceCount = plan.Occurrences.Length;
        AssetId = plan.SceneAssetId;
    }

    /// <summary>Gets the root Scene asset identity.</summary>
    public AssetId AssetId { get; }

    /// <summary>Gets the stable identity assigned to this Scene instance.</summary>
    public Guid SceneInstanceId => _sceneInstanceId;

    internal ExceptionDispatchInfo? CleanupFailure => Volatile.Read(ref _cleanupFailure);

    internal int EntityCount => _entityCount;

    internal uint FirstEntityIdentityOrdinal => _firstEntityIdentityOrdinal;

    internal bool IsActivating => Volatile.Read(ref _lifecycle) == LifecycleActivating;

    internal bool IsActive => Volatile.Read(ref _lifecycle) == LifecycleActive;

    internal bool IsCleanupComplete => Volatile.Read(ref _lifecycle) == LifecycleCleanupComplete;

    internal bool IsUnloadInvocationActive => Volatile.Read(ref _cleanupRequest) is not null;

    internal int OccurrenceCount => _occurrenceCount;

    internal static SceneExternalBindingCaptureRow[] CaptureExternalBindings(
        World world,
        ImmutableArray<SceneExternalBinding> externalBindings)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (externalBindings.IsDefault)
        {
            throw new ArgumentException("The external-binding array must be initialized.", nameof(externalBindings));
        }

        var captureRows = new SceneExternalBindingCaptureRow[externalBindings.Length];

        for (var index = 0; index < externalBindings.Length; index++)
        {
            var binding = externalBindings[index]
                ?? throw new ArgumentException("The external-binding array cannot contain null.", nameof(externalBindings));
            Guid? targetEntityId = binding.Target is Entity target ? world.GetEntityId(target) : null;
            captureRows[index] = new SceneExternalBindingCaptureRow(binding.SlotId, targetEntityId);
        }

        Array.Sort(
            captureRows,
            static (left, right) => FoundationCatalogue.CompareRfc(left.BindingId, right.BindingId));
        return captureRows;
    }

    /// <summary>Begins a typed hierarchy transaction scoped to this exact Scene instance.</summary>
    /// <returns>The unsubmitted transaction.</returns>
    /// <exception cref="InvalidOperationException">This Scene instance is not active in its World.</exception>
    public SceneTransaction BeginTransaction()
    {
        var plan = Volatile.Read(ref _plan);

        if (!IsActive || (plan is null))
        {
            throw new InvalidOperationException("A Scene transaction requires an active Scene instance.");
        }

        _registry.EnsureBoundTo(_world);

        if (!_registry.ContainsInstance(this))
        {
            throw new InvalidOperationException("A Scene transaction requires an active Scene instance.");
        }

        return new SceneTransaction(
            this,
            _world,
            _registry,
            _sceneInstanceId,
            _firstEntityIdentityOrdinal,
            plan.Entities.Length);
    }

    /// <summary>Looks up a live root-authored Entity by its stable authored identity.</summary>
    /// <param name="stableEntityId">The nonempty authored Entity identity.</param>
    /// <param name="entity">The live Entity, or default when the identity is absent or no longer live.</param>
    /// <returns><see langword="true"/> when the root Scene contains that live Entity.</returns>
    /// <exception cref="ArgumentException"><paramref name="stableEntityId"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread.</exception>
    public bool TryGetEntity(Guid stableEntityId, out Entity entity)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(stableEntityId, Guid.Empty);

        if (!IsActive
            || (_plan is not { } plan)
            || !plan.TryResolveRootEntity(stableEntityId, out var expandedEntityIndex))
        {
            entity = default;
            return false;
        }

        return TryGetLiveEntity(plan, expandedEntityIndex, out entity);
    }

    /// <summary>Looks up a live Entity by an exact root-relative placement address.</summary>
    /// <param name="address">The bounded placement path and authored Entity identity.</param>
    /// <param name="entity">The live Entity, or default when the address does not exist or is no longer live.</param>
    /// <returns><see langword="true"/> when the addressed Entity is live in this instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="address"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread.</exception>
    public bool TryGetEntity(SceneEntityAddress address, out Entity entity)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (!IsActive || (_plan is not { } plan))
        {
            entity = default;
            return false;
        }

        try
        {
            var expandedEntityIndex = plan.ResolveEntity(
                new SceneEntitySelector(
                    SceneEntitySelectorScope.OccurrenceRelative,
                    address.PlacementPath,
                    address.AuthoredEntityId),
                relativeAnchorOccurrenceIndex: 0);
            return TryGetLiveEntity(plan, expandedEntityIndex, out entity);
        }
        catch (System.IO.InvalidDataException)
        {
            entity = default;
            return false;
        }
    }

    /// <summary>Gets immutable provenance only for an Entity that is live in this exact Scene instance.</summary>
    /// <param name="entity">The candidate Entity handle.</param>
    /// <param name="provenance">The copied provenance, or null when the Entity is not a live member.</param>
    /// <returns><see langword="true"/> when the Entity is a live member of this Scene instance.</returns>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread.</exception>
    public bool TryGetProvenance(Entity entity, [NotNullWhen(true)] out SceneEntityProvenance? provenance)
    {
        if (!IsActive
            || (_plan is not { } plan)
            || !_world.TryGetLiveEntityIdentityOrdinal(entity, out var identityOrdinal)
            || (identityOrdinal < _firstEntityIdentityOrdinal))
        {
            provenance = null;
            return false;
        }

        var assignmentIndex = identityOrdinal - _firstEntityIdentityOrdinal;

        if (assignmentIndex >= (uint)plan.Entities.Length)
        {
            provenance = null;
            return false;
        }

        var expandedEntityIndex = plan.EntityIndicesByAssignmentOrdinal[checked((int)assignmentIndex)];
        var expandedEntity = plan.Entities[expandedEntityIndex];
        var occurrence = plan.Occurrences[expandedEntity.OccurrenceIndex];
        var placementPath = plan.GetOccurrencePlacementPath(expandedEntity.OccurrenceIndex);
        provenance = new SceneEntityProvenance(
            _sceneInstanceId,
            occurrence.OccurrenceId,
            occurrence.AssetId,
            expandedEntity.AuthoredEntityId,
            expandedEntity.ExpandedEntityKey,
            placementPath,
            _world.GetEntityIdentity(entity));
        return true;
    }

    /// <summary>Queues atomic destruction of this Scene instance at the World owner boundary.</summary>
    /// <param name="mode">The explicit hierarchy-destruction policy applied to its live entities.</param>
    /// <param name="cancellationToken">The token that cancels the request before owner-boundary application.</param>
    /// <returns>The unload result, including whether the instance was already unloaded.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is undefined.</exception>
    public Task<SceneUnloadReceipt> UnloadAsync(
        HierarchyDestroyMode mode,
        CancellationToken cancellationToken = default)
    {
        if (mode is not HierarchyDestroyMode.DetachChildrenKeepWorld and not HierarchyDestroyMode.DestroySubtree)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<SceneUnloadReceipt>(cancellationToken);
        }

        var request = new SceneUnloadRequest(this, mode, cancellationToken);

        if (!_registry.TryEnqueue(request))
        {
            request.Reject(_world.CreateDiagnostic(
                "SceneUnload.OwnerQueueFull",
                DiagnosticSeverity.Warning,
                "SceneInstance.UnloadAsync",
                "The target World owner-control queue is full.",
                AssetId.Value));
        }

        return request.Task;
    }

    internal void Activate(Guid sceneInstanceId, uint firstEntityIdentityOrdinal)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sceneInstanceId, Guid.Empty);

        if ((_occurrenceCount == 0) || (_plan is null))
        {
            throw new InvalidOperationException("A Scene instance requires its prepared plan and root occurrence.");
        }

        if ((_entityCount != 0) && (firstEntityIdentityOrdinal == 0))
        {
            throw new ArgumentOutOfRangeException(nameof(firstEntityIdentityOrdinal));
        }

        if (Interlocked.CompareExchange(ref _lifecycle, LifecycleActivating, LifecycleConstructed) != LifecycleConstructed)
        {
            throw new InvalidOperationException("A Scene instance can be activated only once.");
        }

        _sceneInstanceId = sceneInstanceId;
        _firstEntityIdentityOrdinal = firstEntityIdentityOrdinal;
    }

    internal void CopyPersistenceAssetIds(Span<uint> destination)
    {
        var plan = GetActivePlan();
        var occurrences = plan.Occurrences;

        if (destination.Length != occurrences.Length)
        {
            throw new ArgumentException("The Scene AssetId destination must match the exact occurrence count.", nameof(destination));
        }

        for (var index = 0; index < occurrences.Length; index++)
        {
            destination[index] = checked((uint)occurrences[index].AssetId.Value);
        }
    }

    internal void CopyPersistenceEntityIds(WorldPersistenceCaptureLease lease, scoped Span<Guid> destination)
    {
        var plan = GetPersistencePlan(lease);
        var liveEntityCount = GetPersistenceEntityCount(lease);

        if (destination.Length != liveEntityCount)
        {
            throw new ArgumentException("The Scene EntityId destination must match the exact live expanded Entity count.", nameof(destination));
        }

        var destinationIndex = 0;

        foreach (var expandedEntityIndex in plan.EntityIndicesByAssignmentOrdinal)
        {
            if (TryGetLiveEntity(plan, expandedEntityIndex, out var entity))
            {
                destination[destinationIndex++] = _world.GetEntityId(entity);
            }
        }

        if (destinationIndex != destination.Length)
        {
            throw new InvalidOperationException("The live Scene Entity set changed during capture.");
        }
    }

    internal bool Deactivate()
    {
        var state = Volatile.Read(ref _lifecycle);

        if (state is LifecycleConstructed or LifecycleActivating or LifecycleActive)
        {
            Volatile.Write(ref _lifecycle, LifecycleInactivePending);
        }

        return state == LifecycleActive;
    }

    internal void EndUnloadInvocation(SceneUnloadRequest request)
    {
        if (ReferenceEquals(Volatile.Read(ref _cleanupRequest), request))
        {
            Volatile.Write(ref _cleanupRequest, null);
        }
    }

    internal ReadOnlySpan<SceneExternalBindingCaptureRow> GetExternalBindingCaptureRows(WorldPersistenceCaptureLease lease)
    {
        ValidatePersistenceLease(lease);

        if (!IsActive)
        {
            throw new InvalidOperationException("Scene external-binding capture requires an active Scene instance.");
        }

        return Volatile.Read(ref _externalBindings)
            ?? throw new ObjectDisposedException(nameof(SceneInstance));
    }

    internal int GetPersistenceAssetIdCount()
        => GetActivePlan().Occurrences.Length;

    internal uint GetPersistenceAssignmentOrdinal(WorldPersistenceCaptureLease lease)
    {
        ValidatePersistenceLease(lease);
        _ = GetActivePlan();

        if (!_world.IdentityLedger.TryFind(_sceneInstanceId, out var assignmentOrdinal)
            || (assignmentOrdinal == 0))
        {
            throw new InvalidOperationException("The active SceneInstance identity has no live assignment ordinal.");
        }

        return assignmentOrdinal;
    }

    internal int GetPersistenceEntityCount(WorldPersistenceCaptureLease lease)
    {
        var plan = GetPersistencePlan(lease);
        var liveEntityCount = 0;

        foreach (var expandedEntityIndex in plan.EntityIndicesByAssignmentOrdinal)
        {
            if (TryGetLiveEntity(plan, expandedEntityIndex, out _))
            {
                liveEntityCount = checked(liveEntityCount + 1);
            }
        }

        return liveEntityCount;
    }

    internal SceneExpansionPlan GetPersistencePlan(WorldPersistenceCaptureLease lease)
    {
        ValidatePersistenceLease(lease);
        return GetActivePlan();
    }

    internal TResource? GetPreparedAssets<TResource>()
        where TResource : class
    {
        var plan = Volatile.Read(ref _plan);
        return IsActive ? plan?.GetPreparedAssets<TResource>() : null;
    }

    internal bool IsBoundTo(World world, SceneWorldRegistry registry)
        => ReferenceEquals(_world, world) && ReferenceEquals(_registry, registry);

    internal EbinWriteStatus MeasurePersistenceEntityBindingRecord(
        WorldPersistenceCaptureLease lease,
        int expandedEntityIndex,
        out int recordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        lease.ValidateCurrent();

        if (!lease.IsBoundTo(_world))
        {
            throw new ArgumentException("The capture lease belongs to another World.", nameof(lease));
        }

        var plan = GetActivePlan();

        if (!TryGetLiveEntity(plan, expandedEntityIndex, out var entity))
        {
            throw new InvalidOperationException("The captured Scene Entity is no longer live in its World.");
        }

        return ScenePersistenceEntityBindingWriter.Measure(
            plan,
            expandedEntityIndex,
            _world.GetEntityId(entity),
            WorldPersistenceEbinLimits.Create(_world.Options),
            out recordBytes,
            out diagnostic);
    }

    internal EbinWriteStatus MeasurePersistenceOccurrenceRecord(
        WorldPersistenceCaptureLease lease,
        int occurrenceIndex,
        out int recordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        lease.ValidateCurrent();

        if (!lease.IsBoundTo(_world))
        {
            throw new ArgumentException("The capture lease belongs to another World.", nameof(lease));
        }

        return ScenePersistenceOccurrenceWriter.Measure(
            GetActivePlan(),
            occurrenceIndex,
            WorldPersistenceEbinLimits.Create(_world.Options),
            out recordBytes,
            out diagnostic);
    }

    internal void PublishActivation()
    {
        if (!IsActivating)
        {
            throw new InvalidOperationException("The Scene instance is not prepared for activation.");
        }

        Volatile.Write(ref _lifecycle, LifecycleActive);
    }

    internal void RetainControlReturnFailure(
        World.WorldSceneControlAppend controlAppend,
        SceneUnloadRequest request,
        ExceptionDispatchInfo failure)
    {
        _cleanupControlAppend = controlAppend;
        _cleanupRequest = request;
        _cleanupFailure ??= failure;
    }

    internal void RetainUncommittedFailure(ExceptionDispatchInfo failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        _cleanupFailure ??= failure;
        Volatile.Write(ref _lifecycle, LifecycleCleanupFailed);
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "The inactive instance retains each failed owner while attempting independent plan and control returns once.")]
    internal void RevokeFromWorld()
    {
        var state = Interlocked.CompareExchange(
            ref _lifecycle,
            LifecycleCleaning,
            LifecycleInactivePending);

        if (state == LifecycleCleanupComplete)
        {
            return;
        }

        if (state == LifecycleCleanupFailed)
        {
            (CleanupFailure
                ?? throw new InvalidOperationException("The failed Scene cleanup lost its original failure."))
                .Throw();
            return;
        }

        if (state != LifecycleInactivePending)
        {
            throw new InvalidOperationException("Scene cleanup requires its sole inactive owner.");
        }

        _externalBindings = null;
        var controlAppend = _cleanupControlAppend;
        Exception? invariantFailure = null;

        if (controlAppend is { IsCommitted: false, CleanupFailure: null })
        {
            try
            {
                controlAppend.Install();
            }
            catch (Exception exception)
            {
                _cleanupFailure ??= ExceptionDispatchInfo.Capture(exception);
                invariantFailure = exception;
            }
        }

        var plan = _plan;

        if (plan is not null)
        {
            try
            {
                if (plan.IsCleanupRunning)
                {
                    invariantFailure ??= new InvalidOperationException("The active Scene plan has a competing cleanup invoker.");
                    throw invariantFailure;
                }

                plan.Dispose();

                if (!plan.IsCleanupComplete)
                {
                    invariantFailure ??= new InvalidOperationException("The Scene plan cleanup has not completed.");
                    throw invariantFailure;
                }

                _plan = null;
            }
            catch (Exception exception)
            {
                _cleanupFailure ??= plan.CleanupFailure ?? ExceptionDispatchInfo.Capture(exception);
            }
        }

        if (controlAppend is not null)
        {
            try
            {
                controlAppend.Dispose();

                if (!controlAppend.IsCleanupComplete)
                {
                    throw new InvalidOperationException("The Scene control preparation return has not completed.");
                }

                _cleanupControlAppend = null;
            }
            catch (Exception exception)
            {
                _cleanupFailure ??= controlAppend.CleanupFailure ?? ExceptionDispatchInfo.Capture(exception);
                invariantFailure ??= exception;
            }
        }

        if (CleanupFailure is { } failure)
        {
            Volatile.Write(ref _lifecycle, LifecycleCleanupFailed);

            if (invariantFailure is not null)
            {
                _world.FaultSceneCleanupInvariant(invariantFailure);
            }

            failure.Throw();
        }

        Volatile.Write(ref _lifecycle, LifecycleCleanupComplete);
    }

    internal void TakeUnloadCleanupOwnership(
        World.WorldSceneControlAppend controlAppend,
        SceneUnloadRequest request,
        ExceptionDispatchInfo? failure)
    {
        if ((_cleanupControlAppend is not null) || (_cleanupRequest is not null))
        {
            throw new InvalidOperationException("The Scene instance already owns an unload return tail.");
        }

        _cleanupControlAppend = controlAppend;
        _cleanupRequest = request;
        _cleanupFailure ??= failure;
    }

    internal bool TryGetPersistenceEntityId(
        WorldPersistenceCaptureLease lease,
        int expandedEntityIndex,
        out Guid entityId)
    {
        var plan = GetPersistencePlan(lease);

        if (!TryGetLiveEntity(plan, expandedEntityIndex, out var entity))
        {
            entityId = Guid.Empty;
            return false;
        }

        entityId = _world.GetEntityId(entity);
        return true;
    }

    internal EbinWriteStatus WritePersistenceEntityBindingRecord(
        WorldPersistenceCaptureLease lease,
        int expandedEntityIndex,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        lease.ValidateCurrent();

        if (!lease.IsBoundTo(_world))
        {
            throw new ArgumentException("The capture lease belongs to another World.", nameof(lease));
        }

        var plan = GetActivePlan();

        if (!TryGetLiveEntity(plan, expandedEntityIndex, out var entity))
        {
            throw new InvalidOperationException("The captured Scene Entity is no longer live in its World.");
        }

        return ScenePersistenceEntityBindingWriter.Write(
            plan,
            expandedEntityIndex,
            _world.GetEntityId(entity),
            WorldPersistenceEbinLimits.Create(_world.Options),
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    internal EbinWriteStatus WritePersistenceOccurrenceRecord(
        WorldPersistenceCaptureLease lease,
        int occurrenceIndex,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        lease.ValidateCurrent();

        if (!lease.IsBoundTo(_world))
        {
            throw new ArgumentException("The capture lease belongs to another World.", nameof(lease));
        }

        return ScenePersistenceOccurrenceWriter.Write(
            GetActivePlan(),
            occurrenceIndex,
            WorldPersistenceEbinLimits.Create(_world.Options),
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    private SceneExpansionPlan GetActivePlan()
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("Scene persistence capture requires an active Scene instance.");
        }

        return Volatile.Read(ref _plan)
            ?? throw new ObjectDisposedException(nameof(SceneInstance));
    }

    private bool TryGetLiveEntity(SceneExpansionPlan plan, int expandedEntityIndex, out Entity entity)
    {
        var assignmentOrdinal = checked(
            _firstEntityIdentityOrdinal
            + (uint)plan.GetEntityAssignmentOrdinal(expandedEntityIndex));
        return _world.TryGetEntityByIdentityOrdinal(assignmentOrdinal, out entity);
    }

    private void ValidatePersistenceLease(WorldPersistenceCaptureLease lease)
    {
        lease.ValidateCurrent();

        if (!lease.IsBoundTo(_world))
        {
            throw new ArgumentException("The capture lease belongs to another World.", nameof(lease));
        }
    }
}
