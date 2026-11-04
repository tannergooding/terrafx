// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;

namespace TerraFX.Entities;

internal sealed class WorldPersistenceLiveIdentitySet
{
    private const uint EntityIdentityKind = 1;
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
        if ((_identities.Count >= _maximumCount)
            || !_identities.TryAdd(identity, identityKind))
        {
            throw new ArgumentException("The state/identity-history live identities exceed their bound or contain a duplicate.", parameterName);
        }
    }

    internal void MatchEntity(Guid identity, string parameterName)
        => Match(identity, EntityIdentityKind, parameterName);

    internal void MatchSceneInstance(Guid identity, string parameterName)
        => Match(identity, SceneInstanceIdentityKind, parameterName);

    internal void ThrowIfNotEmpty(string parameterName)
    {
        if (_identities.Count != 0)
        {
            throw new ArgumentException("The state/identity-history live identities do not match the ECS and Scene records.", parameterName);
        }
    }

    private void Match(Guid identity, uint expectedKind, string parameterName)
    {
        if (!_identities.Remove(identity, out var identityKind)
            || (identityKind != expectedKind))
        {
            throw new ArgumentException("An ECS or Scene identity does not match its live identity-history assignment.", parameterName);
        }
    }
}
