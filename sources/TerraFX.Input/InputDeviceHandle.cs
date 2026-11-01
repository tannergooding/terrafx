// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Input;

/// <summary>Identifies one generation of a physical input device owned by a host.</summary>
public readonly struct InputDeviceHandle : IEquatable<InputDeviceHandle>
{
    /// <summary>Initializes a host-issued physical input device identity.</summary>
    /// <param name="ownerId">The nonzero host owner identity.</param>
    /// <param name="index">The host-local device slot.</param>
    /// <param name="generation">The nonzero generation of the slot.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ownerId"/> or <paramref name="generation"/> is zero.</exception>
    internal InputDeviceHandle(uint ownerId, uint index, uint generation)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ownerId);
        ArgumentOutOfRangeException.ThrowIfZero(generation);
        OwnerId = ownerId;
        Index = index;
        Generation = generation;
    }

    /// <summary>Gets the slot generation, or zero for the invalid default value.</summary>
    public uint Generation { get; }

    /// <summary>Gets the host-local device slot.</summary>
    public uint Index { get; }

    /// <summary>Gets whether this value carries a nonzero owner and generation.</summary>
    public bool IsValid => (OwnerId != 0) && (Generation != 0);

    /// <summary>Gets the host owner identity.</summary>
    public uint OwnerId { get; }

    /// <summary>Negates device-identity equality.</summary>
    public static bool operator !=(InputDeviceHandle left, InputDeviceHandle right) => !left.Equals(right);

    /// <summary>Compares device identities including their slot generation.</summary>
    public static bool operator ==(InputDeviceHandle left, InputDeviceHandle right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(InputDeviceHandle other)
        => (OwnerId == other.OwnerId) && (Index == other.Index) && (Generation == other.Generation);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is InputDeviceHandle other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(OwnerId, Index, Generation);
}
