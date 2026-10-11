// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Numerics;

namespace TerraFX;

/// <summary>Stores a three-dimensional chunk and raw finite offset without spatial metadata.</summary>
public readonly struct SpatialPosition3 : IEquatable<SpatialPosition3>
{
    /// <summary>Copies the supplied chunk and finite offset without normalization.</summary>
    /// <param name="chunk">The signed chunk identity.</param>
    /// <param name="offset">The raw finite offset.</param>
    /// <exception cref="ArgumentException"><paramref name="offset"/> is nonfinite.</exception>
    public SpatialPosition3(ChunkIndex3 chunk, Vector3 offset)
    {
        ThrowHelper.ThrowIfNotFinite(offset);
        Chunk = chunk;
        Offset = offset;
    }

    /// <summary>Gets the stored chunk identity.</summary>
    public ChunkIndex3 Chunk { get; }

    /// <summary>Gets the stored raw offset.</summary>
    public Vector3 Offset { get; }

    /// <summary>Negates stored-field equality.</summary>
    public static bool operator !=(SpatialPosition3 left, SpatialPosition3 right) => !(left == right);

    /// <summary>Compares stored fields without normalizing or inferring settings.</summary>
    public static bool operator ==(SpatialPosition3 left, SpatialPosition3 right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(SpatialPosition3 other) => Chunk.Equals(other.Chunk) && Offset.Equals(other.Offset);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SpatialPosition3 other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Chunk, Offset);
}
