// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal readonly struct WorldPersistenceStrictLayoutChunkOrdinalEntry : IComparable<WorldPersistenceStrictLayoutChunkOrdinalEntry>
{
    internal WorldPersistenceStrictLayoutChunkOrdinalEntry(long ordinal, int archetypeIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentOutOfRangeException.ThrowIfNegative(archetypeIndex);
        Ordinal = ordinal;
        ArchetypeIndex = archetypeIndex;
    }

    internal int ArchetypeIndex { get; }

    internal long Ordinal { get; }

    public int CompareTo(WorldPersistenceStrictLayoutChunkOrdinalEntry other)
        => Ordinal.CompareTo(other.Ordinal);
}
