// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Qualifies a stable Entity identity by its World, creation lineage, and issuance branch.</summary>
public readonly struct WorldEntityIdentity : IEquatable<WorldEntityIdentity>
{
    /// <summary>Initializes a qualified stable Entity identity.</summary>
    /// <param name="worldGuid">The nonempty logical World identity.</param>
    /// <param name="creationLineageId">The nonempty creation lineage identity.</param>
    /// <param name="creationBranchId">The nonempty creation branch identity.</param>
    /// <param name="entityId">The nonempty stable Entity identity.</param>
    /// <exception cref="ArgumentException">An identity component is empty.</exception>
    internal WorldEntityIdentity(Guid worldGuid, Guid creationLineageId, Guid creationBranchId, Guid entityId)
    {
        if (worldGuid == Guid.Empty)
        {
            throw new ArgumentException("A World identity cannot be empty.", nameof(worldGuid));
        }

        if (creationLineageId == Guid.Empty)
        {
            throw new ArgumentException("A creation lineage identity cannot be empty.", nameof(creationLineageId));
        }

        if (creationBranchId == Guid.Empty)
        {
            throw new ArgumentException("A creation branch identity cannot be empty.", nameof(creationBranchId));
        }

        if (entityId == Guid.Empty)
        {
            throw new ArgumentException("An Entity identity cannot be empty.", nameof(entityId));
        }

        WorldGuid = worldGuid;
        CreationLineageId = creationLineageId;
        CreationBranchId = creationBranchId;
        EntityId = entityId;
    }

    /// <summary>Gets the identity branch in which the Entity was created.</summary>
    public Guid CreationBranchId { get; }

    /// <summary>Gets the replay lineage in which the Entity was created.</summary>
    public Guid CreationLineageId { get; }

    /// <summary>Gets the stable identity of the Entity.</summary>
    public Guid EntityId { get; }

    /// <summary>Gets whether every identity component is nonempty.</summary>
    public bool IsValid
        => (WorldGuid != Guid.Empty)
        && (CreationLineageId != Guid.Empty)
        && (CreationBranchId != Guid.Empty)
        && (EntityId != Guid.Empty);

    /// <summary>Gets the logical World identity.</summary>
    public Guid WorldGuid { get; }

    /// <summary>Negates qualified identity equality.</summary>
    public static bool operator !=(WorldEntityIdentity left, WorldEntityIdentity right) => !(left == right);

    /// <summary>Compares all World, lineage, branch, and Entity identity components.</summary>
    public static bool operator ==(WorldEntityIdentity left, WorldEntityIdentity right) => left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(WorldEntityIdentity other)
        => (WorldGuid == other.WorldGuid)
        && (CreationLineageId == other.CreationLineageId)
        && (CreationBranchId == other.CreationBranchId)
        && (EntityId == other.EntityId);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is WorldEntityIdentity other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(WorldGuid, CreationLineageId, CreationBranchId, EntityId);
}
