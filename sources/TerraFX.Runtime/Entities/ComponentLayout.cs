// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Describes the physical layout of one component value type.</summary>
public readonly struct ComponentLayout : IEquatable<ComponentLayout>
{
    /// <summary>Initializes a component layout.</summary>
    /// <param name="layoutId">The nonempty identity of the physical layout.</param>
    /// <param name="logicalBytes">The managed value size in bytes.</param>
    /// <exception cref="ArgumentException"><paramref name="layoutId"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="logicalBytes"/> is outside the supported component range.</exception>
    public ComponentLayout(Guid layoutId, int logicalBytes)
    {
        if (layoutId == Guid.Empty)
        {
            throw new ArgumentException("A component layout identity must be nonempty.", nameof(layoutId));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(logicalBytes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(logicalBytes, 32756);

        LayoutId = layoutId;
        LogicalBytes = logicalBytes;
    }

    /// <summary>Gets the physical layout identity.</summary>
    public Guid LayoutId { get; }

    /// <summary>Gets the managed value size in bytes.</summary>
    public int LogicalBytes { get; }

    /// <summary>Negates equality of the complete component layout.</summary>
    public static bool operator !=(ComponentLayout left, ComponentLayout right) => !(left == right);

    /// <summary>Compares the complete component layout.</summary>
    public static bool operator ==(ComponentLayout left, ComponentLayout right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(ComponentLayout other) => LayoutId == other.LayoutId && LogicalBytes == other.LogicalBytes;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ComponentLayout other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(LayoutId, LogicalBytes);
}
