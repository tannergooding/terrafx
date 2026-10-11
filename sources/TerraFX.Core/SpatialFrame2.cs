// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using TerraFX.Numerics;

namespace TerraFX;

/// <summary>Declares a two-dimensional relative calculation frame and its identity.</summary>
[SuppressMessage("Performance", "CA1815", Justification = "Frame equality is not part of the accepted public surface.")]
public readonly struct SpatialFrame2
{
    /// <summary>Initializes a registered frame with a canonical origin and finite ordered extents.</summary>
    /// <param name="ownerId">The nonzero owner identity.</param>
    /// <param name="profileId">The registered profile identity.</param>
    /// <param name="settings">The matching spatial declaration.</param>
    /// <param name="origin">The canonical spatial origin.</param>
    /// <param name="revision">The positive frame revision.</param>
    /// <param name="minimum">The closed relative minimum.</param>
    /// <param name="maximum">The closed relative maximum.</param>
    /// <exception cref="ArgumentException">The profile, origin, or extents are invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Owner or revision is outside its range.</exception>
    public SpatialFrame2(uint ownerId, SpatialProfileId profileId, SpatialSettings settings, SpatialPosition2 origin, long revision, Vector2 minimum, Vector2 maximum)
    {
        SpatialKernel.ValidateFrame(ownerId, profileId, settings, origin, revision, minimum, maximum);
        OwnerId = ownerId;
        ProfileId = profileId;
        Settings = settings;
        Origin = origin;
        Revision = revision;
        Minimum = minimum;
        Maximum = maximum;
    }

    /// <summary>Gets the closed relative maximum.</summary>
    public Vector2 Maximum { get; }

    /// <summary>Gets the closed relative minimum.</summary>
    public Vector2 Minimum { get; }

    /// <summary>Gets the canonical spatial origin.</summary>
    public SpatialPosition2 Origin { get; }

    /// <summary>Gets the owner identity.</summary>
    public uint OwnerId { get; }

    /// <summary>Gets the registered profile identity.</summary>
    public SpatialProfileId ProfileId { get; }

    /// <summary>Gets the positive frame revision.</summary>
    public long Revision { get; }

    /// <summary>Gets the spatial declaration.</summary>
    public SpatialSettings Settings { get; }
}
