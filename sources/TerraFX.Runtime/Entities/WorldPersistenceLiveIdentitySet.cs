// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;

namespace TerraFX.Entities;

internal sealed class WorldPersistenceLiveIdentitySet
{
    private const uint EntityIdentityKind = 1;
    private const uint EntitySceneOwnershipFlag = 1U << 30;
    private const uint HierarchyNodeFlag = 1U << 28;
    private const uint LocalTransformFlag = 1U << 29;
    private const uint MatchedFlag = 1U << 31;
    private const uint SceneInstanceIdentityKind = 2;

    private readonly Dictionary<Guid, (uint state, long ordinal, Guid sceneOwnerId)> _identities = [];
    private readonly long _maximumCount;

    internal WorldPersistenceLiveIdentitySet(long maximumCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCount);
        _maximumCount = maximumCount;
    }

    internal void Add(Guid identity, uint identityKind, long assignmentOrdinal, string parameterName)
    {
        if (((identityKind != EntityIdentityKind) && (identityKind != SceneInstanceIdentityKind))
            || (_identities.Count >= _maximumCount)
            || (assignmentOrdinal < 1)
            || !_identities.TryAdd(identity, (identityKind, assignmentOrdinal, Guid.Empty)))
        {
            throw new ArgumentException("The state/identity-history live identities exceed their bound or contain a duplicate.", parameterName);
        }
    }

    internal void ClaimSceneEntity(Guid identity, Guid sceneInstanceId, string parameterName)
    {
        if (!_identities.TryGetValue(identity, out var value)
            || ((value.state & (MatchedFlag | 3)) != (MatchedFlag | EntityIdentityKind))
            || ((value.state & EntitySceneOwnershipFlag) != 0)
            || (value.sceneOwnerId != sceneInstanceId))
        {
            throw new ArgumentException("A Scene entity is not a unique live ECS Entity.", parameterName);
        }

        _identities[identity] = (value.state | EntitySceneOwnershipFlag, value.ordinal, value.sceneOwnerId);
    }

    internal long GetAssignmentOrdinal(Guid identity, uint expectedKind, string parameterName)
    {
        if (!_identities.TryGetValue(identity, out var value)
            || ((value.state & 3) != expectedKind))
        {
            throw new ArgumentException("An identity does not match the expected live identity-history kind.", parameterName);
        }

        return value.ordinal;
    }

    internal bool IsLiveEntityIdentity(Guid identity)
        => _identities.TryGetValue(identity, out var value)
        && ((value.state & 3) == EntityIdentityKind);

    internal bool IsLiveSceneInstanceIdentity(Guid identity)
        => _identities.TryGetValue(identity, out var value)
        && ((value.state & 3) == SceneInstanceIdentityKind);

    internal bool IsMatchedEntity(Guid identity)
        => _identities.TryGetValue(identity, out var value)
        && ((value.state & (MatchedFlag | EntityIdentityKind)) == (MatchedFlag | EntityIdentityKind));

    internal void MarkHierarchyNode(Guid identity, string parameterName)
    {
        if (!_identities.TryGetValue(identity, out var entity)
            || ((entity.state & (MatchedFlag | 3)) != (MatchedFlag | EntityIdentityKind))
            || ((entity.state & HierarchyNodeFlag) != 0))
        {
            throw new ArgumentException("A hierarchy node does not identify a unique live ECS Entity.", parameterName);
        }

        _identities[identity] = (entity.state | HierarchyNodeFlag, entity.ordinal, entity.sceneOwnerId);
    }

    internal void MarkLocalTransform(Guid identity, string parameterName)
    {
        if (!_identities.TryGetValue(identity, out var entity)
            || ((entity.state & (MatchedFlag | 3)) != (MatchedFlag | EntityIdentityKind))
            || ((entity.state & LocalTransformFlag) != 0))
        {
            throw new ArgumentException("A LocalTransform component does not identify a unique live ECS Entity.", parameterName);
        }

        _identities[identity] = (entity.state | LocalTransformFlag, entity.ordinal, entity.sceneOwnerId);
    }

    internal void MatchEntity(Guid identity, string parameterName)
        => Match(identity, EntityIdentityKind, parameterName);

    internal void MatchSceneInstance(Guid identity, string parameterName)
        => Match(identity, SceneInstanceIdentityKind, parameterName);

    internal void SetSceneOwner(Guid identity, Guid sceneInstanceId, string parameterName)
    {
        if (!_identities.TryGetValue(identity, out var entity)
            || ((entity.state & (MatchedFlag | 3)) != (MatchedFlag | EntityIdentityKind))
            || (entity.sceneOwnerId != Guid.Empty)
            || (sceneInstanceId == Guid.Empty)
            || !_identities.TryGetValue(sceneInstanceId, out var sceneInstance)
            || ((sceneInstance.state & 3) != SceneInstanceIdentityKind))
        {
            throw new ArgumentException("A state/entity SceneInstanceId does not match one unique live SceneInstance.", parameterName);
        }

        _identities[identity] = (entity.state, entity.ordinal, sceneInstanceId);
    }

    internal void ThrowIfNotEmpty(string parameterName)
    {
        foreach (var (state, ordinal, sceneOwnerId) in _identities.Values)
        {
            var isSceneOwned = sceneOwnerId != Guid.Empty;
            var hasSceneClaim = (state & EntitySceneOwnershipFlag) != 0;
            var hasLocalTransform = (state & LocalTransformFlag) != 0;
            var hasHierarchyNode = (state & HierarchyNodeFlag) != 0;

            if (((state & MatchedFlag) == 0)
                || (((state & 3) == EntityIdentityKind)
                    && ((isSceneOwned != hasSceneClaim) || (hasLocalTransform && !hasHierarchyNode))))
            {
                throw new ArgumentException("The state/identity-history live identities do not match the ECS and Scene records.", parameterName);
            }
        }
    }

    private void Match(Guid identity, uint expectedKind, string parameterName)
    {
        if (!_identities.TryGetValue(identity, out var value)
            || (value.state != expectedKind))
        {
            throw new ArgumentException("An ECS or Scene identity does not match its live identity-history assignment.", parameterName);
        }

        _identities[identity] = (value.state | MatchedFlag, value.ordinal, value.sceneOwnerId);
    }
}
