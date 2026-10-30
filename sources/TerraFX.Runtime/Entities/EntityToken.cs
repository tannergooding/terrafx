// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Identifies one temporary Entity creation admitted by a command writer.</summary>
public readonly struct EntityToken : IEquatable<EntityToken>
{
    private readonly long _localSequence;
    private readonly Guid _writerPrefixId;

    internal EntityToken(Guid writerPrefixId, long localSequence)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(writerPrefixId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(localSequence);
        _writerPrefixId = writerPrefixId;
        _localSequence = localSequence;
    }

    /// <summary>Gets whether this token was issued by a command writer.</summary>
    public bool IsValid => (_writerPrefixId != Guid.Empty) && (_localSequence >= 0);

    internal long LocalSequence => _localSequence;

    internal Guid WriterPrefixId => _writerPrefixId;

    /// <summary>Determines whether two command tokens identify different admitted creations.</summary>
    public static bool operator !=(EntityToken left, EntityToken right) => !left.Equals(right);

    /// <summary>Determines whether two command tokens identify the same admitted creation.</summary>
    public static bool operator ==(EntityToken left, EntityToken right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(EntityToken other)
        => (_writerPrefixId == other._writerPrefixId) && (_localSequence == other._localSequence);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is EntityToken other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_writerPrefixId, _localSequence);
}
