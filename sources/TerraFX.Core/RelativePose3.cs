// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using TerraFX.Numerics;

namespace TerraFX;

/// <summary>Pairs a finite three-dimensional local pose with its calculation frame.</summary>
[SuppressMessage("Performance", "CA1815", Justification = "Pose equality is not part of the accepted public surface.")]
public readonly struct RelativePose3
{
    /// <summary>Initializes a finite local position and admits a rotation under the scene pose rules.</summary>
    /// <param name="localPosition">The finite local position.</param>
    /// <param name="rotation">The finite quaternion with finite squared norm greater than 1e-12; normalized on assignment.</param>
    /// <param name="frame">The configured dimensional frame.</param>
    /// <exception cref="ArgumentException">A local value, rotation, or frame is invalid.</exception>
    public RelativePose3(Vector3 localPosition, Quaternion rotation, SpatialFrame3 frame)
    {
        SpatialKernel.ValidateFrame(frame);
        ThrowHelper.ThrowIfNotFinite(localPosition);
        ThrowHelper.ThrowIfNotFinite(rotation);
        var lengthSquared = Quaternion.Dot(rotation, rotation);

        if (!float.IsFinite(lengthSquared) || lengthSquared <= 1.0e-12f)
        {
            ThrowHelper.ThrowArgumentException("The rotation must have a finite squared norm greater than 1e-12.", nameof(rotation));
        }

        LocalPosition = localPosition;
        Rotation = Quaternion.Normalize(rotation);
        Frame = frame;
    }

    /// <summary>Gets the exact dimensional frame.</summary>
    public SpatialFrame3 Frame { get; }

    /// <summary>Gets the local position.</summary>
    public Vector3 LocalPosition { get; }

    /// <summary>Gets the normalized rotation without quaternion-sign canonicalization.</summary>
    public Quaternion Rotation { get; }
}
