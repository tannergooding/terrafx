// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
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
    private Guid _sceneInstanceId;
    private readonly World _world;

    internal SceneInstance(World world, SceneExpansionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(plan);
        _world = world;
        _plan = plan;
        _occurrenceCount = plan.Occurrences.Length;
        AssetId = plan.SceneAssetId;
    }

    /// <summary>Gets the root Scene asset identity.</summary>
    public AssetId AssetId { get; }

    /// <summary>Gets the stable identity assigned to this Scene instance.</summary>
    public Guid SceneInstanceId => _sceneInstanceId;

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

        if ((Volatile.Read(ref _isActive) == 0)
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
