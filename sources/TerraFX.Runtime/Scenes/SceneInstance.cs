// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;

namespace TerraFX.Scenes;

/// <summary>Identifies one activated Scene within its exact World.</summary>
public sealed class SceneInstance
{
    private SceneExternalBindingCaptureRow[]? _externalBindings;
    private uint _firstEntityIdentityOrdinal;
    private int _isActive;
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
        _occurrenceCount = plan.Occurrences.Length;
        AssetId = plan.SceneAssetId;
    }

    /// <summary>Gets the root Scene asset identity.</summary>
    public AssetId AssetId { get; }

    /// <summary>Gets the stable identity assigned to this Scene instance.</summary>
    public Guid SceneInstanceId => _sceneInstanceId;

    internal int EntityCount => _plan?.Entities.Length ?? 0;

    internal uint FirstEntityIdentityOrdinal => _firstEntityIdentityOrdinal;

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

        if ((Volatile.Read(ref _isActive) != 1) || (plan is null))
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

        if ((Volatile.Read(ref _isActive) != 1)
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

        if ((Volatile.Read(ref _isActive) != 1) || (_plan is not { } plan))
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
        if ((Volatile.Read(ref _isActive) != 1)
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

        if ((_plan.Entities.Length != 0) && (firstEntityIdentityOrdinal == 0))
        {
            throw new ArgumentOutOfRangeException(nameof(firstEntityIdentityOrdinal));
        }

        if (Interlocked.CompareExchange(ref _isActive, -1, 0) != 0)
        {
            throw new InvalidOperationException("A Scene instance can be activated only once.");
        }

        _sceneInstanceId = sceneInstanceId;
        _firstEntityIdentityOrdinal = firstEntityIdentityOrdinal;
        Volatile.Write(ref _isActive, 1);
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

    internal ReadOnlySpan<SceneExternalBindingCaptureRow> GetExternalBindingCaptureRows()
    {
        if (Volatile.Read(ref _isActive) != 1)
        {
            throw new InvalidOperationException("Scene external-binding capture requires an active Scene instance.");
        }

        return Volatile.Read(ref _externalBindings)
            ?? throw new ObjectDisposedException(nameof(SceneInstance));
    }

    internal int GetPersistenceAssetIdCount()
        => GetActivePlan().Occurrences.Length;

    internal TResource? GetPreparedAssets<TResource>()
        where TResource : class
    {
        var plan = Volatile.Read(ref _plan);
        return (Volatile.Read(ref _isActive) == 1) ? plan?.GetPreparedAssets<TResource>() : null;
    }

    internal void RevokeFromWorld()
    {
        _ = Interlocked.Exchange(ref _isActive, 0);
        _ = Interlocked.Exchange(ref _externalBindings, null);
        Interlocked.Exchange(ref _plan, null)?.Dispose();
    }

    private SceneExpansionPlan GetActivePlan()
    {
        if (Volatile.Read(ref _isActive) != 1)
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
}
