// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

/// <summary>Identifies one generation of a monitor owned by a platform host.</summary>
public readonly struct MonitorHandle : IEquatable<MonitorHandle>
{
    /// <summary>Initializes an engine-issued monitor identity.</summary>
    /// <param name="ownerId">The nonzero platform-host owner identity.</param>
    /// <param name="index">The host-local monitor slot.</param>
    /// <param name="generation">The nonzero generation of the slot.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ownerId"/> or <paramref name="generation"/> is zero.</exception>
    internal MonitorHandle(uint ownerId, uint index, uint generation)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ownerId);
        ArgumentOutOfRangeException.ThrowIfZero(generation);
        OwnerId = ownerId;
        Index = index;
        Generation = generation;
    }

    /// <summary>Gets the slot generation, or zero for the invalid default value.</summary>
    public uint Generation { get; }

    /// <summary>Gets the host-local monitor slot.</summary>
    public uint Index { get; }

    /// <summary>Gets whether this value carries a nonzero owner and generation.</summary>
    public bool IsValid => (OwnerId != 0) && (Generation != 0);

    /// <summary>Gets the platform-host owner identity.</summary>
    public uint OwnerId { get; }

    /// <summary>Negates monitor-identity equality.</summary>
    public static bool operator !=(MonitorHandle left, MonitorHandle right) => !left.Equals(right);

    /// <summary>Compares monitor identities including their slot generation.</summary>
    public static bool operator ==(MonitorHandle left, MonitorHandle right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(MonitorHandle other)
        => (OwnerId == other.OwnerId) && (Index == other.Index) && (Generation == other.Generation);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is MonitorHandle other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(OwnerId, Index, Generation);
}
