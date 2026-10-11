// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Diagnostics.CodeAnalysis;

namespace TerraFX;

/// <summary>Retains closed two-dimensional spatial endpoints under explicit settings.</summary>
[SuppressMessage("Performance", "CA1815", Justification = "Bounds equality is not part of the accepted public surface.")]
public readonly struct SpatialBounds2
{
    /// <summary>Initializes componentwise ordered bounds without normalizing closed endpoints.</summary>
    /// <param name="minimum">The closed minimum.</param>
    /// <param name="maximum">The closed maximum.</param>
    /// <param name="settings">The valid spatial declaration.</param>
    /// <exception cref="System.ArgumentException">The settings or endpoint ordering are invalid.</exception>
    public SpatialBounds2(SpatialPosition2 minimum, SpatialPosition2 maximum, SpatialSettings settings)
    {
        SpatialKernel.ValidateSettings(settings);
        ThrowHelper.ThrowIfNotFinite(minimum.Offset, nameof(minimum));
        ThrowHelper.ThrowIfNotFinite(maximum.Offset, nameof(maximum));

        if (SpatialKernel.CompareAxis(minimum.Chunk.X, minimum.Offset.X, maximum.Chunk.X, maximum.Offset.X, settings.ChunkSizeExponent) < 0
            || SpatialKernel.CompareAxis(minimum.Chunk.Y, minimum.Offset.Y, maximum.Chunk.Y, maximum.Offset.Y, settings.ChunkSizeExponent) < 0)
        {
            ThrowHelper.ThrowArgumentException("Spatial bounds must be ordered on every axis.", nameof(maximum));
        }

        Minimum = minimum;
        Maximum = maximum;
        Settings = settings;
    }

    /// <summary>Gets the retained closed maximum.</summary>
    public SpatialPosition2 Maximum { get; }

    /// <summary>Gets the retained closed minimum.</summary>
    public SpatialPosition2 Minimum { get; }

    /// <summary>Gets the spatial declaration.</summary>
    public SpatialSettings Settings { get; }
}
