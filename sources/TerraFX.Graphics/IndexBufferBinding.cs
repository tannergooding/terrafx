// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Defines one bounded index-buffer range and its element format.</summary>
public readonly struct IndexBufferBinding : IEquatable<IndexBufferBinding>
{
    /// <summary>Initializes a validated index-buffer binding.</summary>
    /// <param name="buffer">The graph-owned index buffer version.</param>
    /// <param name="range">The nonempty byte range within that version.</param>
    /// <param name="format">The 16-bit or 32-bit index format.</param>
    /// <exception cref="ArgumentException">The buffer does not permit index use or the range is out of bounds or misaligned.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="buffer"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is undefined.</exception>
    public IndexBufferBinding(GraphBufferVersion buffer, GraphBufferRange range, IndexFormat format)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }

        if ((buffer.Description.Usage & BufferUsage.Index) == 0)
        {
            throw new ArgumentException("The graph buffer description does not permit index use.", nameof(buffer));
        }

        var elementSize = format switch {
            IndexFormat.UInt16 => sizeof(ushort),
            IndexFormat.UInt32 => sizeof(uint),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

        if (!range.IsWithin(buffer.Range)
            || ((range.ByteOffset % elementSize) != 0)
            || ((range.ByteLength % elementSize) != 0))
        {
            throw new ArgumentException("The index-buffer range must fit its graph version and align to the selected element size.", nameof(range));
        }

        Buffer = buffer;
        Format = format;
        Range = range;
    }

    /// <summary>Gets the graph-owned index-buffer version.</summary>
    public GraphBufferVersion Buffer { get; }

    /// <summary>Gets the index element format.</summary>
    public IndexFormat Format { get; }

    /// <summary>Gets the nonempty aligned byte range.</summary>
    public GraphBufferRange Range { get; }

    /// <summary>Negates index-buffer binding equality.</summary>
    public static bool operator !=(IndexBufferBinding left, IndexBufferBinding right) => !left.Equals(right);

    /// <summary>Compares index-buffer binding values.</summary>
    public static bool operator ==(IndexBufferBinding left, IndexBufferBinding right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(IndexBufferBinding other)
        => ReferenceEquals(Buffer, other.Buffer)
            && (Format == other.Format)
            && (Range == other.Range);

    /// <inheritdoc />
    public override bool Equals(object? obj) => (obj is IndexBufferBinding other) && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Buffer, Format, Range);
}
