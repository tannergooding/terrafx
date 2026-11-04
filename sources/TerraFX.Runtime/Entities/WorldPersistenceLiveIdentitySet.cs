// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;

namespace TerraFX.Entities;

internal sealed class WorldPersistenceLiveIdentitySet
{
    private const uint EntityIdentityKind = 1;
    private const uint EntitySceneOwnershipFlag = 1U << 30;
    private const uint MatchedFlag = 1U << 31;
    private const uint SceneInstanceIdentityKind = 2;

    private readonly Dictionary<Guid, (uint state, long ordinal)> _identities = [];
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
            || !_identities.TryAdd(identity, (identityKind, assignmentOrdinal)))
        {
            throw new ArgumentException("The state/identity-history live identities exceed their bound or contain a duplicate.", parameterName);
        }
    }

    internal void ClaimSceneEntity(Guid identity, string parameterName)
    {
        if (!_identities.TryGetValue(identity, out var value)
            || (value.state != (MatchedFlag | EntityIdentityKind)))
        {
            throw new ArgumentException("A Scene entity is not a unique live ECS Entity.", parameterName);
        }

        _identities[identity] = (value.state | EntitySceneOwnershipFlag, value.ordinal);
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

    internal bool IsMatchedEntity(Guid identity)
        => _identities.TryGetValue(identity, out var value)
        && ((value.state & (MatchedFlag | EntityIdentityKind)) == (MatchedFlag | EntityIdentityKind));

    internal void MatchEntity(Guid identity, string parameterName)
        => Match(identity, EntityIdentityKind, parameterName);

    internal void MatchSceneInstance(Guid identity, string parameterName)
        => Match(identity, SceneInstanceIdentityKind, parameterName);

    internal void ThrowIfNotEmpty(string parameterName)
    {
        foreach (var (state, ordinal) in _identities.Values)
        {
            if ((state & MatchedFlag) == 0)
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

        _identities[identity] = (value.state | MatchedFlag, value.ordinal);
    }
}
