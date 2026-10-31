// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Assets;

/// <summary>Identifies one asset within a selected content set.</summary>
public readonly struct AssetId : IEquatable<AssetId>, IFormattable
{
    private readonly int _value;

    /// <summary>Initializes a positive content-set asset identity.</summary>
    /// <param name="value">The positive signed 32-bit identity.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is not positive.</exception>
    public AssetId(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        _value = value;
    }

    /// <summary>Gets a value indicating whether this is a valid asset identity.</summary>
    public bool IsValid => _value > 0;

    /// <summary>Gets the positive asset identity.</summary>
    public int Value => _value;

    /// <summary>Negates asset identity equality.</summary>
    public static bool operator !=(AssetId left, AssetId right) => !left.Equals(right);

    /// <summary>Compares two asset identities.</summary>
    public static bool operator ==(AssetId left, AssetId right) => left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(AssetId other) => _value == other._value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is AssetId other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _value;

    /// <inheritdoc/>
    public override string ToString() => _value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public string ToString(string? format, IFormatProvider? formatProvider) => _value.ToString(format, formatProvider);
}
