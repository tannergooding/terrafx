// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

/// <summary>Identifies one generation of a window owned by a platform host.</summary>
public readonly struct WindowHandle : IEquatable<WindowHandle>
{
    /// <summary>Initializes an engine-issued window identity.</summary>
    /// <param name="ownerId">The nonzero platform-host owner identity.</param>
    /// <param name="index">The host-local window slot.</param>
    /// <param name="generation">The nonzero generation of the slot.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ownerId"/> or <paramref name="generation"/> is zero.</exception>
    internal WindowHandle(uint ownerId, uint index, uint generation)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ownerId);
        ArgumentOutOfRangeException.ThrowIfZero(generation);
        OwnerId = ownerId;
        Index = index;
        Generation = generation;
    }

    /// <summary>Gets the slot generation, or zero for the invalid default value.</summary>
    public uint Generation { get; }

    /// <summary>Gets the host-local window slot.</summary>
    public uint Index { get; }

    /// <summary>Gets whether this value carries a nonzero owner and generation.</summary>
    public bool IsValid => (OwnerId != 0) && (Generation != 0);

    /// <summary>Gets the platform-host owner identity.</summary>
    public uint OwnerId { get; }

    /// <summary>Negates window-identity equality.</summary>
    public static bool operator !=(WindowHandle left, WindowHandle right) => !left.Equals(right);

    /// <summary>Compares window identities including their slot generation.</summary>
    public static bool operator ==(WindowHandle left, WindowHandle right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(WindowHandle other)
        => (OwnerId == other.OwnerId) && (Index == other.Index) && (Generation == other.Generation);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is WindowHandle other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(OwnerId, Index, Generation);
}
