// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Describes one semantic leaf in a component's managed storage layout.</summary>
public sealed class ComponentStorageLeaf
{
    /// <summary>Initializes a component storage leaf.</summary>
    /// <param name="path">The exact semantic storage path.</param>
    /// <param name="kind">The managed leaf representation.</param>
    /// <param name="byteOffset">The byte offset from the component value start.</param>
    /// <param name="byteExtent">The occupied byte extent.</param>
    /// <param name="inlineCount">The inline element count, or zero for non-inline leaves.</param>
    /// <param name="inlineStride">The inline element stride, or zero for non-inline leaves.</param>
    /// <param name="purpose">The leaf's schema or runtime-only purpose.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A kind, range, or inline extent is invalid.</exception>
    public ComponentStorageLeaf(
        ComponentStoragePath path,
        ComponentStorageKind kind,
        int byteOffset,
        int byteExtent,
        int inlineCount,
        int inlineStride,
        ComponentStoragePurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (kind is < ComponentStorageKind.Value or > ComponentStorageKind.TransientNativeValue)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(byteOffset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteExtent);

        if (purpose is not ComponentStoragePurpose.SchemaValue and not ComponentStoragePurpose.DeclaredRuntimeOnly)
        {
            throw new ArgumentOutOfRangeException(nameof(purpose));
        }

        if (kind == ComponentStorageKind.InlineArray)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inlineCount);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inlineStride);

            if (checked(inlineCount * inlineStride) != byteExtent)
            {
                throw new ArgumentOutOfRangeException(nameof(byteExtent), "Inline count and stride must exactly cover the leaf extent.");
            }
        }
        else if ((inlineCount != 0) || (inlineStride != 0))
        {
            throw new ArgumentException("Only an inline-array leaf has inline count and stride.", nameof(inlineCount));
        }

        Path = path;
        Kind = kind;
        ByteOffset = byteOffset;
        ByteExtent = byteExtent;
        InlineCount = inlineCount;
        InlineStride = inlineStride;
        Purpose = purpose;
    }

    /// <summary>Gets the occupied byte extent.</summary>
    public int ByteExtent { get; }

    /// <summary>Gets the starting byte offset.</summary>
    public int ByteOffset { get; }

    /// <summary>Gets the inline element count, or zero for a non-inline leaf.</summary>
    public int InlineCount { get; }

    /// <summary>Gets the inline element stride, or zero for a non-inline leaf.</summary>
    public int InlineStride { get; }

    /// <summary>Gets the managed leaf representation.</summary>
    public ComponentStorageKind Kind { get; }

    /// <summary>Gets the exact semantic storage path.</summary>
    public ComponentStoragePath Path { get; }

    /// <summary>Gets the schema or runtime-only purpose.</summary>
    public ComponentStoragePurpose Purpose { get; }
}
