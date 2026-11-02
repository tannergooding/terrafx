// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Defines one checked mip/layer/aspect interval in a graph texture version.</summary>
public readonly struct GraphTextureRange : IEquatable<GraphTextureRange>
{
    /// <summary>Initializes a nonempty subresource interval.</summary>
    /// <param name="firstMip">The first nonnegative mip level.</param>
    /// <param name="mipCount">The positive mip count.</param>
    /// <param name="firstLayer">The first nonnegative array layer.</param>
    /// <param name="layerCount">The positive layer count.</param>
    /// <param name="aspect">The selected texture aspect.</param>
    /// <exception cref="ArgumentOutOfRangeException">An extent is invalid or its end overflows.</exception>
    public GraphTextureRange(int firstMip, int mipCount, int firstLayer, int layerCount, TextureAspect aspect)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstMip);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mipCount);
        ArgumentOutOfRangeException.ThrowIfNegative(firstLayer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(layerCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(firstMip, int.MaxValue - mipCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(firstLayer, int.MaxValue - layerCount);

        if (!Enum.IsDefined(aspect))
        {
            throw new ArgumentOutOfRangeException(nameof(aspect));
        }

        Aspect = aspect;
        FirstLayer = firstLayer;
        FirstMip = firstMip;
        LayerCount = layerCount;
        MipCount = mipCount;
    }

    /// <summary>Gets the selected texture aspect.</summary>
    public TextureAspect Aspect { get; }

    /// <summary>Gets the first array layer.</summary>
    public int FirstLayer { get; }

    /// <summary>Gets the first mip level.</summary>
    public int FirstMip { get; }

    /// <summary>Gets the positive array-layer count.</summary>
    public int LayerCount { get; }

    /// <summary>Gets the positive mip count.</summary>
    public int MipCount { get; }

    internal readonly bool IsValid
        => (FirstMip >= 0)
            && (MipCount > 0)
            && (FirstLayer >= 0)
            && (LayerCount > 0)
            && Enum.IsDefined(Aspect)
            && (FirstMip <= int.MaxValue - MipCount)
            && (FirstLayer <= int.MaxValue - LayerCount);

    internal readonly int LayerEndExclusive => checked(FirstLayer + LayerCount);

    internal readonly int MipEndExclusive => checked(FirstMip + MipCount);

    /// <summary>Negates range equality.</summary>
    public static bool operator !=(GraphTextureRange left, GraphTextureRange right) => !left.Equals(right);

    /// <summary>Compares texture subresource intervals.</summary>
    public static bool operator ==(GraphTextureRange left, GraphTextureRange right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(GraphTextureRange other)
        => (Aspect == other.Aspect)
            && (FirstLayer == other.FirstLayer)
            && (FirstMip == other.FirstMip)
            && (LayerCount == other.LayerCount)
            && (MipCount == other.MipCount);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GraphTextureRange other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(Aspect, FirstLayer, FirstMip, LayerCount, MipCount);

    internal readonly bool IsWithin(GraphTextureRange enclosing)
        => IsValid
            && enclosing.IsValid
            && (Aspect == enclosing.Aspect)
            && (FirstMip >= enclosing.FirstMip)
            && (MipEndExclusive <= enclosing.MipEndExclusive)
            && (FirstLayer >= enclosing.FirstLayer)
            && (LayerEndExclusive <= enclosing.LayerEndExclusive);

    internal readonly bool Overlaps(GraphTextureRange other)
        => IsValid
            && other.IsValid
            && (Aspect == other.Aspect)
            && (FirstMip < other.MipEndExclusive)
            && (other.FirstMip < MipEndExclusive)
            && (FirstLayer < other.LayerEndExclusive)
            && (other.FirstLayer < LayerEndExclusive);

}
