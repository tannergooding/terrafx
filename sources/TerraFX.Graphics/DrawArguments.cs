// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Contains one nonnegative nonindexed draw range.</summary>
public readonly struct DrawArguments : IEquatable<DrawArguments>
{
    /// <summary>Initializes one draw argument record.</summary>
    /// <param name="vertexCount">The nonnegative vertex count; zero records a no-op draw.</param>
    /// <param name="instanceCount">The nonnegative instance count; zero records a no-op draw.</param>
    /// <param name="firstVertex">The nonnegative first vertex.</param>
    /// <param name="firstInstance">The nonnegative first instance.</param>
    /// <exception cref="ArgumentOutOfRangeException">A count or first-element index is negative.</exception>
    public DrawArguments(int vertexCount, int instanceCount, int firstVertex, int firstInstance)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(vertexCount);
        ArgumentOutOfRangeException.ThrowIfNegative(instanceCount);
        ArgumentOutOfRangeException.ThrowIfNegative(firstVertex);
        ArgumentOutOfRangeException.ThrowIfNegative(firstInstance);

        FirstInstance = firstInstance;
        FirstVertex = firstVertex;
        InstanceCount = instanceCount;
        VertexCount = vertexCount;
    }

    /// <summary>Gets the first instance.</summary>
    public int FirstInstance { get; }

    /// <summary>Gets the first vertex.</summary>
    public int FirstVertex { get; }

    /// <summary>Gets the nonnegative instance count.</summary>
    public int InstanceCount { get; }

    /// <summary>Gets the nonnegative vertex count.</summary>
    public int VertexCount { get; }

    /// <summary>Negates draw argument equality.</summary>
    public static bool operator !=(DrawArguments left, DrawArguments right) => !left.Equals(right);

    /// <summary>Compares draw argument values.</summary>
    public static bool operator ==(DrawArguments left, DrawArguments right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(DrawArguments other)
        => (FirstInstance == other.FirstInstance)
            && (FirstVertex == other.FirstVertex)
            && (InstanceCount == other.InstanceCount)
            && (VertexCount == other.VertexCount);

    /// <inheritdoc />
    public override bool Equals(object? obj) => (obj is DrawArguments other) && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(FirstInstance, FirstVertex, InstanceCount, VertexCount);
}
