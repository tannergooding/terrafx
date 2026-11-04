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

    private readonly Dictionary<Guid, uint> _identities = [];
    private readonly long _maximumCount;

    internal WorldPersistenceLiveIdentitySet(long maximumCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCount);
        _maximumCount = maximumCount;
    }

    internal void Add(Guid identity, uint identityKind, string parameterName)
    {
        if (((identityKind != EntityIdentityKind) && (identityKind != SceneInstanceIdentityKind))
            || (_identities.Count >= _maximumCount)
            || !_identities.TryAdd(identity, identityKind))
        {
            throw new ArgumentException("The state/identity-history live identities exceed their bound or contain a duplicate.", parameterName);
        }
    }

    internal void ClaimSceneEntity(Guid identity, string parameterName)
    {
        if (!_identities.TryGetValue(identity, out var value)
            || ((value & (MatchedFlag | EntityIdentityKind | EntitySceneOwnershipFlag)) != (MatchedFlag | EntityIdentityKind)))
        {
            throw new ArgumentException("A Scene entity is not a unique live ECS Entity.", parameterName);
        }

        _identities[identity] = value | EntitySceneOwnershipFlag;
    }

    internal bool IsMatchedEntity(Guid identity)
        => _identities.TryGetValue(identity, out var value)
        && ((value & (MatchedFlag | EntityIdentityKind)) == (MatchedFlag | EntityIdentityKind));

    internal void MatchEntity(Guid identity, string parameterName)
        => Match(identity, EntityIdentityKind, parameterName);

    internal void MatchSceneInstance(Guid identity, string parameterName)
        => Match(identity, SceneInstanceIdentityKind, parameterName);

    internal void ThrowIfNotEmpty(string parameterName)
    {
        foreach (var value in _identities.Values)
        {
            if ((value & MatchedFlag) == 0)
            {
                throw new ArgumentException("The state/identity-history live identities do not match the ECS and Scene records.", parameterName);
            }
        }
    }

    private void Match(Guid identity, uint expectedKind, string parameterName)
    {
        if (!_identities.TryGetValue(identity, out var identityKind)
            || (identityKind != expectedKind))
        {
            throw new ArgumentException("An ECS or Scene identity does not match its live identity-history assignment.", parameterName);
        }

        _identities[identity] = identityKind | MatchedFlag;
    }
}
