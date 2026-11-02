// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Identifies one render pass within its owning graph.</summary>
public readonly struct RenderPassId : IEquatable<RenderPassId>
{
    private readonly uint _graphOwnerId;
    private readonly int _value;

    internal RenderPassId(uint graphOwnerId, int value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(graphOwnerId);
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        _graphOwnerId = graphOwnerId;
        _value = value;
    }

    /// <summary>Gets whether this value identifies a pass in a graph.</summary>
    public bool IsValid => (_graphOwnerId != 0) && (_value >= 0);

    /// <summary>Gets the zero-based pass ordinal.</summary>
    public int Value => _value;

    /// <summary>Negates pass-identity equality.</summary>
    public static bool operator !=(RenderPassId left, RenderPassId right) => !left.Equals(right);

    /// <summary>Compares graph-scoped pass identities.</summary>
    public static bool operator ==(RenderPassId left, RenderPassId right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(RenderPassId other)
        => (_graphOwnerId == other._graphOwnerId) && (_value == other._value);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RenderPassId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_graphOwnerId, _value);
}
