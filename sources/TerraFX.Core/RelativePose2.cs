// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using TerraFX.Numerics;

namespace TerraFX;

/// <summary>Pairs a finite two-dimensional local pose with its calculation frame.</summary>
[SuppressMessage("Performance", "CA1815", Justification = "Pose equality is not part of the accepted public surface.")]
public readonly struct RelativePose2
{
    /// <summary>Initializes a finite local position and radian angle in a valid frame.</summary>
    /// <param name="localPosition">The finite local position.</param>
    /// <param name="angle">The finite radian angle.</param>
    /// <param name="frame">The configured dimensional frame.</param>
    /// <exception cref="ArgumentException">A local value or the frame is invalid.</exception>
    public RelativePose2(Vector2 localPosition, float angle, SpatialFrame2 frame)
    {
        SpatialKernel.ValidateFrame(frame);
        ThrowHelper.ThrowIfNotFinite(localPosition);
        ThrowHelper.ThrowIfNotFinite(angle);
        LocalPosition = localPosition;
        Angle = angle;
        Frame = frame;
    }

    /// <summary>Gets the radian angle.</summary>
    public float Angle { get; }

    /// <summary>Gets the exact dimensional frame.</summary>
    public SpatialFrame2 Frame { get; }

    /// <summary>Gets the local position.</summary>
    public Vector2 LocalPosition { get; }
}
