// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal readonly struct WorldGuidSlabGrant
{
    internal readonly int _category;
    internal readonly long _chargedBytes;
    internal readonly int _pageOrdinal;
    internal readonly WorldGuidRegistry? _registry;
    internal readonly uint _ticketGeneration;
    internal readonly int _ticketIndex;

    internal WorldGuidSlabGrant(
        WorldGuidRegistry registry,
        int category,
        int pageOrdinal,
        int ticketIndex,
        uint ticketGeneration,
        long chargedBytes)
    {
        _registry = registry;
        _category = category;
        _pageOrdinal = pageOrdinal;
        _ticketIndex = ticketIndex;
        _ticketGeneration = ticketGeneration;
        _chargedBytes = chargedBytes;
    }

    internal bool IsValid
        => (_registry is not null)
        && (_category >= 0)
        && (_pageOrdinal >= 0)
        && (_ticketIndex >= 0)
        && (_ticketGeneration != 0)
        && (_chargedBytes > 0);
}
