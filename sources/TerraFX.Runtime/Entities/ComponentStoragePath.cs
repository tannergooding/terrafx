// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Entities;

/// <summary>Describes one exact semantic path through a managed component layout.</summary>
public sealed class ComponentStoragePath : IEquatable<ComponentStoragePath>
{
    private const int MaximumSegmentCount = 64;

    /// <summary>Initializes an immutable component storage path.</summary>
    /// <param name="segments">The nonempty ordered storage segments.</param>
    /// <exception cref="ArgumentException"><paramref name="segments"/> is default, empty, or contains an invalid segment.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="segments"/> exceeds the supported path length.</exception>
    public ComponentStoragePath(ImmutableArray<ComponentStorageSegment> segments)
    {
        if (segments.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A component storage path must be initialized and nonempty.", nameof(segments));
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(segments.Length, MaximumSegmentCount, nameof(segments));

        foreach (var segment in segments)
        {
            if (!segment.IsValid)
            {
                throw new ArgumentException("A component storage path cannot contain an invalid segment.", nameof(segments));
            }
        }

        Segments = ImmutableArrayCopy.Create(segments);
    }

    /// <summary>Gets the immutable ordered storage segments.</summary>
    public ImmutableArray<ComponentStorageSegment> Segments { get; }

    /// <summary>Compares the complete ordered storage path.</summary>
    public bool Equals(ComponentStoragePath? other)
        => (other is not null) && Segments.AsSpan().SequenceEqual(other.Segments.AsSpan());

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ComponentStoragePath other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hashCode = new HashCode();

        foreach (var segment in Segments)
        {
            hashCode.Add(segment);
        }

        return hashCode.ToHashCode();
    }
}
