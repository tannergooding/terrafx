// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Declares a system's access to one World resource or engine-defined capability.</summary>
public readonly struct SystemResourceAccess : IEquatable<SystemResourceAccess>
{
    /// <summary>Initializes an access declaration without an explicit partition range.</summary>
    /// <param name="kind">The kind of resource.</param>
    /// <param name="resourceId">The nonempty resource identity.</param>
    /// <param name="mode">The declared access mode.</param>
    /// <exception cref="ArgumentException"><paramref name="resourceId"/> is empty, or the access combination is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> or <paramref name="mode"/> is not a supported value.</exception>
    public SystemResourceAccess(SystemResourceKind kind, Guid resourceId, SystemAccessMode mode)
    {
        Validate(kind, resourceId, mode);

        Kind = kind;
        ResourceId = resourceId;
        Mode = mode;
        Range = null;
    }

    /// <summary>Initializes an access declaration constrained to a partition range.</summary>
    /// <param name="kind">The kind of resource.</param>
    /// <param name="resourceId">The nonempty resource identity.</param>
    /// <param name="mode">The declared access mode.</param>
    /// <param name="range">The nonempty half-open partition range.</param>
    /// <exception cref="ArgumentException"><paramref name="resourceId"/> is empty, or the access combination is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/>, <paramref name="mode"/>, or <paramref name="range"/> is invalid.</exception>
    public SystemResourceAccess(SystemResourceKind kind, Guid resourceId, SystemAccessMode mode, SystemPartitionRange range)
    {
        Validate(kind, resourceId, mode);

        if (!range.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(range));
        }

        if (kind == SystemResourceKind.EntityMembership)
        {
            throw new ArgumentException("Entity-membership access cannot be partition-ranged.", nameof(range));
        }

        Kind = kind;
        ResourceId = resourceId;
        Mode = mode;
        Range = range;
    }

    /// <summary>Gets whether this value has a valid intrinsic representation.</summary>
    public bool IsValid
        => (Kind is SystemResourceKind.Component or SystemResourceKind.Resource or SystemResourceKind.Tag or SystemResourceKind.EntityMembership)
        && (ResourceId != Guid.Empty)
        && (Mode is SystemAccessMode.Read or SystemAccessMode.Write)
        && ((Kind != SystemResourceKind.Tag) || (Mode == SystemAccessMode.Read))
        && ((Kind != SystemResourceKind.EntityMembership)
            || ((ResourceId == WorldSystemResources.EntityMembershipId) && (Mode == SystemAccessMode.Read) && (Range is null)))
        && (Range is null || Range.Value.IsValid);

    /// <summary>Gets the accessed resource kind.</summary>
    public SystemResourceKind Kind { get; }

    /// <summary>Gets the declared access mode.</summary>
    public SystemAccessMode Mode { get; }

    /// <summary>Gets the optional partition range.</summary>
    public SystemPartitionRange? Range { get; }

    /// <summary>Gets the identity of the accessed resource.</summary>
    public Guid ResourceId { get; }

    /// <summary>Negates access declaration equality.</summary>
    public static bool operator !=(SystemResourceAccess left, SystemResourceAccess right) => !(left == right);

    /// <summary>Compares all access identity, mode, and range-presence values.</summary>
    public static bool operator ==(SystemResourceAccess left, SystemResourceAccess right) => left.Equals(right);

    private static void Validate(SystemResourceKind kind, Guid resourceId, SystemAccessMode mode)
    {
        if (resourceId == Guid.Empty)
        {
            throw new ArgumentException("A resource identity cannot be empty.", nameof(resourceId));
        }

        if (kind is not (SystemResourceKind.Component or SystemResourceKind.Resource or SystemResourceKind.Tag or SystemResourceKind.EntityMembership))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (mode is not (SystemAccessMode.Read or SystemAccessMode.Write))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (kind == SystemResourceKind.EntityMembership)
        {
            if (resourceId != WorldSystemResources.EntityMembershipId)
            {
                throw new ArgumentException("Entity-membership access must use its fixed identity.", nameof(resourceId));
            }
        }

        if ((kind is SystemResourceKind.Tag or SystemResourceKind.EntityMembership) && (mode == SystemAccessMode.Write))
        {
            throw new ArgumentException(
                kind == SystemResourceKind.Tag ? "Tag access is read-only." : "Entity-membership access is read-only.",
                nameof(mode));
        }
    }

    /// <inheritdoc />
    public bool Equals(SystemResourceAccess other)
        => (Kind == other.Kind)
        && (ResourceId == other.ResourceId)
        && (Mode == other.Mode)
        && (Range == other.Range);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SystemResourceAccess other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, ResourceId, Mode, Range);
}
