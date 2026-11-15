// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldGuidRetirementPermit
{
    private readonly WorldGuidGrant _grant;
    private int _isConsumed;
    private readonly WorldLifetimeControl _owner;
    private readonly WorldGuidRegistry _registry;

    internal WorldGuidRetirementPermit(
        WorldLifetimeControl owner,
        WorldGuidRegistry registry,
        in WorldGuidGrant grant)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(registry);

        if (!grant.IsOwnedBy(registry))
        {
            throw new ArgumentException("The retirement permit requires the exact WorldGuid registry binding.", nameof(grant));
        }

        _owner = owner;
        _registry = registry;
        _grant = grant;
    }

    internal bool TryConsume(WorldGuidRegistry registry, out WorldGuidGrant grant)
    {
        ArgumentNullException.ThrowIfNull(registry);

        if (!ReferenceEquals(_registry, registry)
            || !ReferenceEquals(_owner.Registry, registry)
            || !_owner.WorldGuidGrant.Equals(_grant)
            || !_owner.IsRetirementPermitCurrent(_grant)
            || !_grant.IsOwnedBy(registry)
            || (Interlocked.CompareExchange(ref _isConsumed, 1, 0) != 0))
        {
            grant = default;
            return false;
        }

        grant = _grant;
        return true;
    }
}
