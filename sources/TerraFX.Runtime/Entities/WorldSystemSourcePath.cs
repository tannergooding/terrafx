// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Entities;

/// <summary>Describes one exact member path in a closed World system value.</summary>
public sealed class WorldSystemSourcePath : IEquatable<WorldSystemSourcePath>
{
    private const int MaximumMemberCount = 64;

    /// <summary>Initializes an immutable system source path.</summary>
    /// <param name="members">The nonempty ordered member names.</param>
    /// <exception cref="ArgumentException"><paramref name="members"/> is default, empty, or contains an invalid name.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="members"/> exceeds the supported path depth.</exception>
    public WorldSystemSourcePath(ImmutableArray<string> members)
    {
        if (members.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A system source path must be initialized and nonempty.", nameof(members));
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(members.Length, MaximumMemberCount, nameof(members));

        foreach (var member in members)
        {
            if (string.IsNullOrWhiteSpace(member))
            {
                throw new ArgumentException("A system source path cannot contain an empty member name.", nameof(members));
            }
        }

        Members = ImmutableArrayCopy.Create(members);
    }

    /// <summary>Gets the immutable ordered member names.</summary>
    public ImmutableArray<string> Members { get; }

    /// <inheritdoc />
    public bool Equals(WorldSystemSourcePath? other)
        => (other is not null) && Members.AsSpan().SequenceEqual(other.Members.AsSpan(), StringComparer.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is WorldSystemSourcePath other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hashCode = new HashCode();

        foreach (var member in Members)
        {
            hashCode.Add(member, StringComparer.Ordinal);
        }

        return hashCode.ToHashCode();
    }
}
