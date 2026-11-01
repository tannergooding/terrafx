// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;

namespace TerraFX.Rendering;

/// <summary>Names the exact 2D frame, physical target extent, and cameras for one CPU extraction.</summary>
public sealed class RenderExtractionOptions2D
{
    /// <summary>Initializes an immutable 2D extraction request.</summary>
    /// <param name="frame">The exact current 2D World frame.</param>
    /// <param name="targetExtent">The positive physical target extent.</param>
    /// <param name="cameraIds">The unique live camera identities to include.</param>
    /// <exception cref="ArgumentException">The frame or camera identity array is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The extent or camera count is outside its supported range.</exception>
    public RenderExtractionOptions2D(
        SpatialFrame2 frame,
        PixelSize targetExtent,
        ImmutableArray<Guid> cameraIds)
    {
        RenderExtractionValidation.ValidateFrame(frame, nameof(frame));
        RenderExtractionValidation.ValidateTargetExtent(targetExtent, nameof(targetExtent));
        Frame = frame;
        TargetExtent = targetExtent;
        CameraIds = RenderExtractionValidation.CopyCameraIds(cameraIds, nameof(cameraIds));
    }

    /// <summary>Gets the copied camera identities in caller selection order.</summary>
    public ImmutableArray<Guid> CameraIds { get; }

    /// <summary>Gets the exact 2D frame selected for extraction.</summary>
    public SpatialFrame2 Frame { get; }

    /// <summary>Gets the physical target extent.</summary>
    public PixelSize TargetExtent { get; }
}
