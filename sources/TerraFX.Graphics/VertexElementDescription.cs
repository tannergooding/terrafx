// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Describes one vertex-buffer element consumed by a reflected vertex shader input.</summary>
public readonly struct VertexElementDescription : IEquatable<VertexElementDescription>
{
    /// <summary>Initializes one vertex element.</summary>
    /// <param name="location">The nonnegative shader input location.</param>
    /// <param name="format">The vertex storage format.</param>
    /// <param name="byteOffset">The nonnegative byte offset within the vertex record.</param>
    /// <exception cref="ArgumentOutOfRangeException">A location, format, or byte offset is invalid.</exception>
    public VertexElementDescription(int location, VertexElementFormat format, int byteOffset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(location);

        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(byteOffset);

        ByteOffset = byteOffset;
        Format = format;
        Location = location;
    }

    /// <summary>Gets the byte offset within the vertex record.</summary>
    public int ByteOffset { get; }

    /// <summary>Gets the vertex storage format.</summary>
    public VertexElementFormat Format { get; }

    /// <summary>Gets the shader input location.</summary>
    public int Location { get; }

    /// <summary>Negates vertex element equality.</summary>
    public static bool operator !=(VertexElementDescription left, VertexElementDescription right) => !left.Equals(right);

    /// <summary>Compares vertex element values.</summary>
    public static bool operator ==(VertexElementDescription left, VertexElementDescription right) => left.Equals(right);

    internal static int GetByteLength(VertexElementFormat format)
        => format switch {
            VertexElementFormat.Float2 => 2 * sizeof(float),
            VertexElementFormat.Float3 => 3 * sizeof(float),
            VertexElementFormat.Float4 => 4 * sizeof(float),
            VertexElementFormat.Half2 => 2 * sizeof(ushort),
            VertexElementFormat.Half4 => 4 * sizeof(ushort),
            VertexElementFormat.UNorm8x4 => 4 * sizeof(byte),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

    internal static ShaderValueType GetShaderValueType(VertexElementFormat format)
        => format switch {
            VertexElementFormat.Float2 or VertexElementFormat.Half2 => ShaderValueType.Float2,
            VertexElementFormat.Float3 => ShaderValueType.Float3,
            VertexElementFormat.Float4 or VertexElementFormat.Half4 or VertexElementFormat.UNorm8x4 => ShaderValueType.Float4,
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

    /// <inheritdoc />
    public bool Equals(VertexElementDescription other)
        => (Location == other.Location) && (Format == other.Format) && (ByteOffset == other.ByteOffset);

    /// <inheritdoc />
    public override bool Equals(object? obj) => (obj is VertexElementDescription other) && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Location, Format, ByteOffset);
}
