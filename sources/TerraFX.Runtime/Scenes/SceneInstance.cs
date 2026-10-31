// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;

namespace TerraFX.Scenes;

/// <summary>Identifies one activated Scene within its exact World.</summary>
public sealed class SceneInstance
{
    private uint _firstEntityIdentityOrdinal;
    private int _isActive;
    private readonly int _occurrenceCount;
    private SceneExpansionPlan? _plan;
    private readonly SceneWorldRegistry _registry;
    private Guid _sceneInstanceId;
    private readonly World _world;

    internal SceneInstance(World world, SceneWorldRegistry registry, SceneExpansionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(plan);
        _world = world;
        _registry = registry;
        _plan = plan;
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

        var assignmentOrdinal = checked(
            _firstEntityIdentityOrdinal
            + (uint)plan.GetEntityAssignmentOrdinal(expandedEntityIndex));

        if (!_world.TryGetEntityByIdentityOrdinal(assignmentOrdinal, out entity))
        {
            return false;
        }

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

    internal void RevokeFromWorld()
    {
        _ = Interlocked.Exchange(ref _isActive, 0);
        Interlocked.Exchange(ref _plan, null)?.Dispose();
    }
}
