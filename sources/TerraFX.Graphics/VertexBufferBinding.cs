// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Defines one bounded vertex-buffer range and its input slot stride.</summary>
public readonly struct VertexBufferBinding : IEquatable<VertexBufferBinding>
{
    /// <summary>Initializes a validated vertex-buffer binding.</summary>
    /// <param name="slot">The nonnegative pipeline input slot.</param>
    /// <param name="buffer">The graph-owned vertex buffer version.</param>
    /// <param name="range">The nonempty byte range within that version.</param>
    /// <param name="stride">The positive vertex-record stride.</param>
    /// <exception cref="ArgumentException">The buffer does not permit vertex use or the range escapes its version.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="buffer"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The slot or stride is invalid.</exception>
    public VertexBufferBinding(int slot, GraphBufferVersion buffer, GraphBufferRange range, int stride)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stride);

        if ((buffer.Description.Usage & BufferUsage.Vertex) == 0)
        {
            throw new ArgumentException("The graph buffer description does not permit vertex use.", nameof(buffer));
        }

        if (!range.IsWithin(buffer.Range))
        {
            throw new ArgumentException("The vertex-buffer range must fit its graph version.", nameof(range));
        }

        Buffer = buffer;
        Range = range;
        Slot = slot;
        Stride = stride;
    }

    /// <summary>Gets the graph-owned vertex-buffer version.</summary>
    public GraphBufferVersion Buffer { get; }

    /// <summary>Gets the nonempty bound byte range.</summary>
    public GraphBufferRange Range { get; }

    /// <summary>Gets the vertex-buffer input slot.</summary>
    public int Slot { get; }

    /// <summary>Gets the positive byte stride between vertex records.</summary>
    public int Stride { get; }

    /// <summary>Negates vertex-buffer binding equality.</summary>
    public static bool operator !=(VertexBufferBinding left, VertexBufferBinding right) => !left.Equals(right);

    /// <summary>Compares vertex-buffer binding values.</summary>
    public static bool operator ==(VertexBufferBinding left, VertexBufferBinding right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(VertexBufferBinding other)
        => ReferenceEquals(Buffer, other.Buffer)
            && (Range == other.Range)
            && (Slot == other.Slot)
            && (Stride == other.Stride);

    /// <inheritdoc />
    public override bool Equals(object? obj) => (obj is VertexBufferBinding other) && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Buffer, Range, Slot, Stride);
}
