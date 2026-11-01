// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Describes one reflected shader stage input or output.</summary>
public readonly struct ShaderIoSignature : IEquatable<ShaderIoSignature>
{
    /// <summary>Initializes a reflected shader stage input or output.</summary>
    /// <param name="semanticName">The nonempty source semantic.</param>
    /// <param name="semanticIndex">The nonnegative semantic index.</param>
    /// <param name="location">The nonnegative user location, or null for a system-value semantic.</param>
    /// <param name="valueType">The reflected shader value type.</param>
    /// <param name="interpolation">The reflected interpolation mode.</param>
    /// <exception cref="ArgumentException">A null location is used for a non-system semantic.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An index or enum value is invalid.</exception>
    public ShaderIoSignature(
        string semanticName,
        int semanticIndex,
        int? location,
        ShaderValueType valueType,
        ShaderInterpolation interpolation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(semanticName);
        ArgumentOutOfRangeException.ThrowIfNegative(semanticIndex);

        if (location is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(location));
        }

        if ((location is null) && !semanticName.StartsWith("SV_", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only a system-value semantic may omit its user location.", nameof(location));
        }

        if ((valueType == ShaderValueType.None) || !Enum.IsDefined(valueType))
        {
            throw new ArgumentOutOfRangeException(nameof(valueType));
        }

        if ((interpolation == ShaderInterpolation.None) || !Enum.IsDefined(interpolation))
        {
            throw new ArgumentOutOfRangeException(nameof(interpolation));
        }

        SemanticName = semanticName;
        SemanticIndex = semanticIndex;
        Location = location;
        ValueType = valueType;
        Interpolation = interpolation;
    }

    /// <summary>Gets the interpolation mode.</summary>
    public ShaderInterpolation Interpolation { get; }

    /// <summary>Gets the user location, or null for a system-value semantic.</summary>
    public int? Location { get; }

    /// <summary>Gets the source semantic name.</summary>
    /// <summary>Gets the source semantic index.</summary>
    public int SemanticIndex { get; }

    /// <summary>Gets the source semantic name.</summary>
    public string SemanticName { get; }

    /// <summary>Gets the reflected shader value type.</summary>
    public ShaderValueType ValueType { get; }

    /// <summary>Negates stage I/O signature equality.</summary>
    public static bool operator !=(ShaderIoSignature left, ShaderIoSignature right) => !left.Equals(right);

    /// <summary>Compares stage I/O signature values.</summary>
    public static bool operator ==(ShaderIoSignature left, ShaderIoSignature right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(ShaderIoSignature other)
        => StringComparer.Ordinal.Equals(SemanticName, other.SemanticName)
        && (SemanticIndex == other.SemanticIndex)
        && (Location == other.Location)
        && (ValueType == other.ValueType)
        && (Interpolation == other.Interpolation);

    /// <inheritdoc />
    public override bool Equals(object? obj) => (obj is ShaderIoSignature other) && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(SemanticName, SemanticIndex, Location, ValueType, Interpolation);
}
