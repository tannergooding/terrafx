// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal readonly struct WorldLineageGrant
{
    private readonly WorldAdmissionDomain? _domain;
    private readonly Guid _identityBranchId;
    private readonly Guid _lineageId;
    private readonly WorldDomainReservation? _reservation;
    private readonly Guid _worldGuid;

    internal WorldLineageGrant(
        WorldAdmissionDomain domain,
        WorldDomainReservation reservation,
        int index,
        uint generation,
        Guid worldGuid,
        Guid lineageId,
        Guid identityBranchId)
    {
        _domain = domain;
        _identityBranchId = identityBranchId;
        _lineageId = lineageId;
        _reservation = reservation;
        _worldGuid = worldGuid;
        Index = index;
        Generation = generation;
    }

    internal uint Generation { get; }

    internal Guid IdentityBranchId => _identityBranchId;

    internal int Index { get; }

    internal bool IsValid
        => (_domain is not null)
        && (_identityBranchId != Guid.Empty)
        && (_lineageId != Guid.Empty)
        && (_reservation is not null)
        && (Generation != 0)
        && (_worldGuid != Guid.Empty);

    internal Guid LineageId => _lineageId;

    internal WorldDomainReservation? Reservation => _reservation;

    internal Guid WorldGuid => _worldGuid;

    internal bool IsOwnedBy(WorldAdmissionDomain domain)
        => IsValid && ReferenceEquals(_domain, domain);
}
