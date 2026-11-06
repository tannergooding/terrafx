// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Contains one nonnegative compute work-group dispatch extent.</summary>
public readonly struct DispatchSize : IEquatable<DispatchSize>
{
    /// <summary>Initializes one compute dispatch extent.</summary>
    /// <param name="groupCountX">The nonnegative X work-group count; zero records no work on that axis.</param>
    /// <param name="groupCountY">The nonnegative Y work-group count; zero records no work on that axis.</param>
    /// <param name="groupCountZ">The nonnegative Z work-group count; zero records no work on that axis.</param>
    /// <exception cref="ArgumentOutOfRangeException">A work-group count is negative.</exception>
    public DispatchSize(int groupCountX, int groupCountY, int groupCountZ)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(groupCountX);
        ArgumentOutOfRangeException.ThrowIfNegative(groupCountY);
        ArgumentOutOfRangeException.ThrowIfNegative(groupCountZ);

        GroupCountX = groupCountX;
        GroupCountY = groupCountY;
        GroupCountZ = groupCountZ;
    }

    /// <summary>Gets the nonnegative X work-group count.</summary>
    public int GroupCountX { get; }

    /// <summary>Gets the nonnegative Y work-group count.</summary>
    public int GroupCountY { get; }

    /// <summary>Gets the nonnegative Z work-group count.</summary>
    public int GroupCountZ { get; }

    /// <summary>Negates dispatch-size equality.</summary>
    public static bool operator !=(DispatchSize left, DispatchSize right) => !left.Equals(right);

    /// <summary>Compares dispatch-size values.</summary>
    public static bool operator ==(DispatchSize left, DispatchSize right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(DispatchSize other)
        => (GroupCountX == other.GroupCountX)
            && (GroupCountY == other.GroupCountY)
            && (GroupCountZ == other.GroupCountZ);

    /// <inheritdoc />
    public override bool Equals(object? obj) => (obj is DispatchSize other) && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(GroupCountX, GroupCountY, GroupCountZ);
}
