// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;

namespace TerraFX.Entities;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct WorldPersistenceIdentityScratchEntry : IComparable<WorldPersistenceIdentityScratchEntry>
{
    private readonly Guid _identity;
    private readonly byte _kind;

    internal WorldPersistenceIdentityScratchEntry(Guid identity, byte kind)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(identity, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(kind, (byte)2);
        ArgumentOutOfRangeException.ThrowIfZero(kind);
        _identity = identity;
        _kind = kind;
    }

    internal byte Kind => _kind;

    internal Guid StableId => _identity;

    internal static bool ContainsEntity(scoped ReadOnlySpan<WorldPersistenceIdentityScratchEntry> entries, Guid identity)
    {
        var low = 0;
        var high = entries.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var entry = entries[middle];
            var comparison = entry.StableId.CompareTo(identity);

            if (comparison == 0)
            {
                return entry.Kind == 1;
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return false;
    }

    public int CompareTo(WorldPersistenceIdentityScratchEntry other)
        => _identity.CompareTo(other._identity);
}
