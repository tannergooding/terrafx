// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Describes one reflected shader resource binding.</summary>
public readonly struct ShaderResourceSignature : IEquatable<ShaderResourceSignature>
{
    /// <summary>Initializes a reflected shader resource binding.</summary>
    /// <param name="group">The portable resource group.</param>
    /// <param name="binding">The register index within the resource class.</param>
    /// <param name="kind">The resource register class.</param>
    /// <param name="arrayCount">The positive fixed descriptor-array count.</param>
    /// <exception cref="ArgumentOutOfRangeException">The group, binding, kind, or array count is invalid.</exception>
    public ShaderResourceSignature(ShaderBindingGroup group, int binding, ShaderResourceKind kind, int arrayCount)
    {
        if ((group == ShaderBindingGroup.None) || !Enum.IsDefined(group))
        {
            throw new ArgumentOutOfRangeException(nameof(group));
        }

        if ((kind == ShaderResourceKind.None) || !Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(binding);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(arrayCount);

        var maximumBinding = kind switch {
            ShaderResourceKind.UniformBuffer => 3,
            ShaderResourceKind.SampledImage => 15,
            ShaderResourceKind.StorageImage or ShaderResourceKind.StorageBuffer => 7,
            ShaderResourceKind.Sampler => 7,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        ArgumentOutOfRangeException.ThrowIfGreaterThan(binding, maximumBinding);

        ArrayCount = arrayCount;
        Binding = binding;
        Group = group;
        Kind = kind;
    }

    /// <summary>Gets the positive fixed descriptor-array count.</summary>
    public int ArrayCount { get; }

    /// <summary>Gets the register index within the resource class.</summary>
    public int Binding { get; }

    /// <summary>Gets the portable resource group.</summary>
    public ShaderBindingGroup Group { get; }

    /// <summary>Gets the resource register class.</summary>
    public ShaderResourceKind Kind { get; }

    /// <summary>Negates resource signature equality.</summary>
    public static bool operator !=(ShaderResourceSignature left, ShaderResourceSignature right) => !left.Equals(right);

    /// <summary>Compares resource signature values.</summary>
    public static bool operator ==(ShaderResourceSignature left, ShaderResourceSignature right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(ShaderResourceSignature other)
        => (Group == other.Group)
        && (Binding == other.Binding)
        && (Kind == other.Kind)
        && (ArrayCount == other.ArrayCount);

    /// <inheritdoc />
    public override bool Equals(object? obj) => (obj is ShaderResourceSignature other) && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Group, Binding, Kind, ArrayCount);
}
