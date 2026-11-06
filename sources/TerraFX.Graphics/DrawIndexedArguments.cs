// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Contains one nonnegative indexed draw range and native signed base-vertex adjustment.</summary>
public readonly struct DrawIndexedArguments : IEquatable<DrawIndexedArguments>
{
    /// <summary>Initializes one indexed draw argument record.</summary>
    /// <param name="indexCount">The nonnegative index count; zero records a no-op draw.</param>
    /// <param name="instanceCount">The nonnegative instance count; zero records a no-op draw.</param>
    /// <param name="firstIndex">The nonnegative first index.</param>
    /// <param name="baseVertex">The signed native vertex adjustment.</param>
    /// <param name="firstInstance">The nonnegative first instance.</param>
    /// <exception cref="ArgumentOutOfRangeException">A count or first-element index is negative.</exception>
    public DrawIndexedArguments(int indexCount, int instanceCount, int firstIndex, int baseVertex, int firstInstance)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(indexCount);
        ArgumentOutOfRangeException.ThrowIfNegative(instanceCount);
        ArgumentOutOfRangeException.ThrowIfNegative(firstIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(firstInstance);

        BaseVertex = baseVertex;
        FirstIndex = firstIndex;
        FirstInstance = firstInstance;
        IndexCount = indexCount;
        InstanceCount = instanceCount;
    }

    /// <summary>Gets the signed native vertex adjustment.</summary>
    public int BaseVertex { get; }

    /// <summary>Gets the first index.</summary>
    public int FirstIndex { get; }

    /// <summary>Gets the first instance.</summary>
    public int FirstInstance { get; }

    /// <summary>Gets the nonnegative index count.</summary>
    public int IndexCount { get; }

    /// <summary>Gets the nonnegative instance count.</summary>
    public int InstanceCount { get; }

    /// <summary>Negates indexed draw argument equality.</summary>
    public static bool operator !=(DrawIndexedArguments left, DrawIndexedArguments right) => !left.Equals(right);

    /// <summary>Compares indexed draw argument values.</summary>
    public static bool operator ==(DrawIndexedArguments left, DrawIndexedArguments right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(DrawIndexedArguments other)
        => (BaseVertex == other.BaseVertex)
            && (FirstIndex == other.FirstIndex)
            && (FirstInstance == other.FirstInstance)
            && (IndexCount == other.IndexCount)
            && (InstanceCount == other.InstanceCount);

    /// <inheritdoc />
    public override bool Equals(object? obj) => (obj is DrawIndexedArguments other) && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(BaseVertex, FirstIndex, FirstInstance, IndexCount, InstanceCount);
}
