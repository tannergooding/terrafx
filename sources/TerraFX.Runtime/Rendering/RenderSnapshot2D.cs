// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;

namespace TerraFX.Rendering;

/// <summary>Contains copied render inputs bound to one exact 2D frame.</summary>
public sealed class RenderSnapshot2D : RenderSnapshot
{
    private readonly SpatialFrame2 _frame;

    internal RenderSnapshot2D(
        Guid snapshotId,
        Guid worldGuid,
        long tickIndex,
        ImmutableArray<Guid> cameraIds,
        ImmutableArray<CameraSnapshot> cameras,
        PixelSize targetExtent,
        SpatialFrame2 frame)
        : base(snapshotId, worldGuid, tickIndex, cameraIds, cameras, targetExtent)
    {
        RenderExtractionValidation.ValidateFrame(frame, nameof(frame));

        if (cameras.Length != 0 && (cameras[0].WorldGuid != worldGuid))
        {
            throw new ArgumentException("The 2D snapshot cameras must belong to its exact World.", nameof(cameras));
        }

        _frame = frame;
    }

    /// <summary>Gets the exact 2D frame represented by this snapshot.</summary>
    public SpatialFrame2 Frame => _frame;

    /// <inheritdoc/>
    public override RenderFrameDimension FrameDimension => RenderFrameDimension.TwoDimensional;

    /// <inheritdoc/>
    public override long FrameRevision => _frame.Revision;

    /// <inheritdoc/>
    public override SpatialProfileId ProfileId => _frame.ProfileId;
}
