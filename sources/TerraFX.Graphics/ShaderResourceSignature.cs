// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Describes one reflected shader resource binding.</summary>
public readonly struct ShaderResourceSignature : IEquatable<ShaderResourceSignature>
{
    /// <summary>Initializes a reflected shader resource binding with explicit access capability.</summary>
    /// <param name="group">The portable resource group.</param>
    /// <param name="binding">The register index within the resource class.</param>
    /// <param name="kind">The resource register class.</param>
    /// <param name="arrayCount">The positive fixed descriptor-array count.</param>
    /// <param name="access">The declared read/write capability.</param>
    /// <exception cref="ArgumentOutOfRangeException">An identity, count, access value, or storage-image format is invalid.</exception>
    /// <exception cref="ArgumentException">A non-storage resource declares an access other than <see cref="ShaderResourceAccess.Read"/>.</exception>
    public ShaderResourceSignature(
        ShaderBindingGroup group,
        int binding,
        ShaderResourceKind kind,
        int arrayCount,
        ShaderResourceAccess access)
        : this(
            group,
            binding,
            kind,
            arrayCount,
            access,
            kind == ShaderResourceKind.StorageImage
                ? ShaderStorageImageFormat.R32UInt
                : ShaderStorageImageFormat.None)
    {
    }

    /// <summary>Initializes a reflected shader resource binding with explicit access capability.</summary>
    /// <param name="group">The portable resource group.</param>
    /// <param name="binding">The register index within the resource class.</param>
    /// <param name="kind">The resource register class.</param>
    /// <param name="arrayCount">The positive fixed descriptor-array count.</param>
    /// <param name="access">The declared read/write capability.</param>
    /// <param name="storageImageFormat">The reflected image format for a storage-image binding.</param>
    /// <exception cref="ArgumentOutOfRangeException">An identity, count, access value, or storage-image format is invalid.</exception>
    /// <exception cref="ArgumentException">A non-storage resource declares a storage-image format or an access other than <see cref="ShaderResourceAccess.Read"/>.</exception>
    public ShaderResourceSignature(
        ShaderBindingGroup group,
        int binding,
        ShaderResourceKind kind,
        int arrayCount,
        ShaderResourceAccess access,
        ShaderStorageImageFormat storageImageFormat)
        : this(
            group,
            binding,
            kind,
            arrayCount,
            access,
            storageImageFormat,
            kind is ShaderResourceKind.SampledImage or ShaderResourceKind.StorageImage
                ? TextureDimension.Dimension2D
                : TextureDimension.None)
    {
    }

    /// <summary>Initializes a reflected shader resource binding with explicit image dimensionality.</summary>
    /// <param name="group">The portable resource group.</param>
    /// <param name="binding">The register index within the resource class.</param>
    /// <param name="kind">The resource register class.</param>
    /// <param name="arrayCount">The positive fixed descriptor-array count.</param>
    /// <param name="access">The declared read/write capability.</param>
    /// <param name="storageImageFormat">The reflected image format for a storage-image binding.</param>
    /// <param name="imageDimension">The reflected dimension for a sampled or storage image.</param>
    /// <exception cref="ArgumentOutOfRangeException">An identity, count, access value, or image dimension is invalid.</exception>
    /// <exception cref="ArgumentException">A non-image resource declares image metadata, a non-storage resource declares a storage-image format, or an access other than <see cref="ShaderResourceAccess.Read"/>.</exception>
    public ShaderResourceSignature(
        ShaderBindingGroup group,
        int binding,
        ShaderResourceKind kind,
        int arrayCount,
        ShaderResourceAccess access,
        ShaderStorageImageFormat storageImageFormat,
        TextureDimension imageDimension)
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

        if ((access == ShaderResourceAccess.None) || !Enum.IsDefined(access))
        {
            throw new ArgumentOutOfRangeException(nameof(access));
        }

        if (kind is ShaderResourceKind.UniformBuffer or ShaderResourceKind.SampledImage or ShaderResourceKind.Sampler)
        {
            if (access != ShaderResourceAccess.Read)
            {
                throw new ArgumentException("Uniform, sampled-image, and sampler bindings are read-only.", nameof(access));
            }
        }

        if (kind == ShaderResourceKind.StorageImage)
        {
            if ((storageImageFormat == ShaderStorageImageFormat.None) || !Enum.IsDefined(storageImageFormat))
            {
                throw new ArgumentOutOfRangeException(nameof(storageImageFormat));
            }

            if (imageDimension is not (TextureDimension.Dimension2D or TextureDimension.Dimension3D))
            {
                throw new ArgumentOutOfRangeException(nameof(imageDimension));
            }
        }
        else if (kind == ShaderResourceKind.SampledImage)
        {
            if (storageImageFormat != ShaderStorageImageFormat.None)
            {
                throw new ArgumentException("Only a storage-image binding may declare a storage-image format.", nameof(storageImageFormat));
            }

            if (imageDimension != TextureDimension.Dimension2D)
            {
                throw new ArgumentOutOfRangeException(nameof(imageDimension));
            }
        }
        else if ((storageImageFormat != ShaderStorageImageFormat.None)
            || (imageDimension != TextureDimension.None))
        {
            throw new ArgumentException("Only an image binding may declare an image format or dimension.", nameof(imageDimension));
        }

        var maximumBinding = kind switch {
            ShaderResourceKind.UniformBuffer => 3,
            ShaderResourceKind.SampledImage => 15,
            ShaderResourceKind.StorageImage or ShaderResourceKind.StorageBuffer => 7,
            ShaderResourceKind.Sampler => 7,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        ArgumentOutOfRangeException.ThrowIfGreaterThan(binding, maximumBinding);

        ArrayCount = arrayCount;
        Access = access;
        Binding = binding;
        Group = group;
        ImageDimension = imageDimension;
        Kind = kind;
        StorageImageFormat = storageImageFormat;
    }

    /// <summary>Initializes a reflected shader resource binding.</summary>
    /// <param name="group">The portable resource group.</param>
    /// <param name="binding">The register index within the resource class.</param>
    /// <param name="kind">The resource register class.</param>
    /// <param name="arrayCount">The positive fixed descriptor-array count.</param>
    /// <exception cref="ArgumentOutOfRangeException">The group, binding, kind, or array count is invalid.</exception>
    public ShaderResourceSignature(ShaderBindingGroup group, int binding, ShaderResourceKind kind, int arrayCount)
        : this(
            group,
            binding,
            kind,
            arrayCount,
            kind is ShaderResourceKind.StorageBuffer or ShaderResourceKind.StorageImage
                ? ShaderResourceAccess.ReadWrite
                : ShaderResourceAccess.Read)
    {
    }

    /// <summary>Gets the declared shader read/write capability.</summary>
    public ShaderResourceAccess Access { get; }

    /// <summary>Gets the positive fixed descriptor-array count.</summary>
    public int ArrayCount { get; }

    /// <summary>Gets the register index within the resource class.</summary>
    public int Binding { get; }

    /// <summary>Gets the portable resource group.</summary>
    public ShaderBindingGroup Group { get; }

    /// <summary>Gets the reflected image dimension, or <see cref="TextureDimension.None"/> for non-image resources.</summary>
    public TextureDimension ImageDimension { get; }

    /// <summary>Gets the resource register class.</summary>
    public ShaderResourceKind Kind { get; }

    /// <summary>Gets the reflected storage-image format, or <see cref="ShaderStorageImageFormat.None"/> for other resource kinds.</summary>
    public ShaderStorageImageFormat StorageImageFormat { get; }

    /// <summary>Negates resource signature equality.</summary>
    public static bool operator !=(ShaderResourceSignature left, ShaderResourceSignature right) => !left.Equals(right);

    /// <summary>Compares resource signature values.</summary>
    public static bool operator ==(ShaderResourceSignature left, ShaderResourceSignature right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(ShaderResourceSignature other)
        => (Group == other.Group)
        && (Binding == other.Binding)
        && (Kind == other.Kind)
        && (ArrayCount == other.ArrayCount)
        && (Access == other.Access)
        && (StorageImageFormat == other.StorageImageFormat)
        && (ImageDimension == other.ImageDimension);

    /// <inheritdoc />
    public override bool Equals(object? obj) => (obj is ShaderResourceSignature other) && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Group, Binding, Kind, ArrayCount, Access, StorageImageFormat, ImageDimension);
}
