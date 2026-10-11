// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

/// <summary>Identifies an immutable coordinate, geometry, spatial, and unit declaration.</summary>
public readonly struct SpatialProfileId : IEquatable<SpatialProfileId>, IFormattable
{
    /// <summary>Initializes a nonempty profile identity.</summary>
    /// <param name="value">The nonempty identity.</param>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    public SpatialProfileId(Guid value)
    {
        if (value == Guid.Empty)
        {
            ThrowHelper.ThrowArgumentException("A spatial profile identity must be nonempty.", nameof(value));
        }

        Value = value;
    }

    /// <summary>Gets the fixed FloatChunk32V2 default profile identity.</summary>
    public static SpatialProfileId Default => new SpatialProfileId(new Guid("c2ddc5d8-9ee6-8676-bdef-dffd36a56d89"));

    /// <summary>Gets whether this identity is nonempty, independently of registration.</summary>
    public bool IsValid => Value != Guid.Empty;

    /// <summary>Gets the stored identity.</summary>
    public Guid Value { get; }

    /// <summary>Negates stored-identity equality.</summary>
    public static bool operator !=(SpatialProfileId left, SpatialProfileId right) => !(left == right);

    /// <summary>Compares stored identities.</summary>
    public static bool operator ==(SpatialProfileId left, SpatialProfileId right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(SpatialProfileId other) => Value.Equals(other.Value);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SpatialProfileId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => ToString(null, null);

    /// <inheritdoc />
    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);
}
