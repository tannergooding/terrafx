// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Identifies a live entity within one World owner.</summary>
public readonly struct Entity : IEquatable<Entity>
{
    /// <summary>Initializes an entity identity issued by its owning World.</summary>
    /// <param name="ownerId">The nonzero World owner identity.</param>
    /// <param name="index">The nonzero entity slot index.</param>
    /// <param name="generation">The nonzero slot generation.</param>
    /// <exception cref="ArgumentOutOfRangeException">An identity component is zero.</exception>
    internal Entity(uint ownerId, uint index, uint generation)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ownerId);
        ArgumentOutOfRangeException.ThrowIfZero(index);
        ArgumentOutOfRangeException.ThrowIfZero(generation);

        OwnerId = ownerId;
        Index = index;
        Generation = generation;
    }

    /// <summary>Gets the entity slot generation.</summary>
    public uint Generation { get; }

    /// <summary>Gets the entity slot index.</summary>
    public uint Index { get; }

    /// <summary>Gets whether this value identifies a nonzero owner, slot, and generation.</summary>
    public bool IsValid => OwnerId != 0 && Index != 0 && Generation != 0;

    /// <summary>Gets the owning World identity.</summary>
    public uint OwnerId { get; }

    /// <summary>Negates entity identity equality.</summary>
    public static bool operator !=(Entity left, Entity right) => !(left == right);

    /// <summary>Compares entity identity tuples.</summary>
    public static bool operator ==(Entity left, Entity right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(Entity other) => (OwnerId == other.OwnerId) && (Index == other.Index) && (Generation == other.Generation);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Entity other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(OwnerId, Index, Generation);
}
