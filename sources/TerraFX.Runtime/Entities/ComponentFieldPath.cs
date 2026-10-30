// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Entities;

/// <summary>Describes an exact semantic path through component fields and FixedArrays.</summary>
public sealed class ComponentFieldPath
{
    private const int MaximumSegmentCount = 64;

    /// <summary>Initializes an immutable component field path.</summary>
    /// <param name="segments">The nonempty ordered field-tag and FixedArray-index segments.</param>
    /// <exception cref="ArgumentException"><paramref name="segments"/> is default, empty, or contains an invalid segment.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="segments"/> exceeds the supported path length.</exception>
    public ComponentFieldPath(ImmutableArray<ComponentPropertySegment> segments)
    {
        if (segments.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A component field path must be initialized and nonempty.", nameof(segments));
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(segments.Length, MaximumSegmentCount, nameof(segments));

        for (var index = 0; index < segments.Length; index++)
        {
            if (!segments[index].IsValid)
            {
                throw new ArgumentException("A component field path cannot contain an invalid segment.", nameof(segments));
            }

            if ((index == 0) && (segments[index].Kind != ComponentPropertySegmentKind.FieldTag))
            {
                throw new ArgumentException("A component field path must begin with a field tag.", nameof(segments));
            }
        }

        Segments = ImmutableArrayCopy.Create(segments);
    }

    /// <summary>Gets the immutable ordered path segments.</summary>
    public ImmutableArray<ComponentPropertySegment> Segments { get; }
}
