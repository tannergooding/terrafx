// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldGuidRetirementPermit
{
    private const int StateAvailable = 0;
    private const int StateQuarantined = 2;
    private const int StateRetired = 1;

    private readonly WorldGuidGrant _grant;
    private readonly WorldLifetimeControl _owner;
    private readonly WorldGuidRegistry _registry;
    private int _state;

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
            || (Interlocked.CompareExchange(ref _state, StateRetired, StateAvailable) != StateAvailable))
        {
            grant = default;
            return false;
        }

        grant = _grant;
        return true;
    }

    internal bool TryConsumeForQuarantine(WorldGuidRegistry registry, in WorldGuidGrant grant)
    {
        ArgumentNullException.ThrowIfNull(registry);

        return ReferenceEquals(_registry, registry)
            && ReferenceEquals(_owner.Registry, registry)
            && _owner.WorldGuidGrant.Equals(_grant)
            && _grant.Equals(grant)
            && _owner.IsQuarantinePermitCurrent(_grant)
            && _grant.IsOwnedBy(registry)
            && (Interlocked.CompareExchange(ref _state, StateQuarantined, StateAvailable) == StateAvailable);
    }

    internal bool TryGetQuarantineGrant(WorldGuidRegistry registry, out WorldGuidGrant grant)
    {
        ArgumentNullException.ThrowIfNull(registry);

        if (ReferenceEquals(_registry, registry)
            && ReferenceEquals(_owner.Registry, registry)
            && _owner.WorldGuidGrant.Equals(_grant)
            && _owner.IsQuarantinePermitCurrent(_grant)
            && _grant.IsOwnedBy(registry)
            && (Volatile.Read(ref _state) == StateAvailable))
        {
            grant = _grant;
            return true;
        }

        grant = default;
        return false;
    }
}
