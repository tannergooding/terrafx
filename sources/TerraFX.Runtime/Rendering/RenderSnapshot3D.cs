// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;

namespace TerraFX.Rendering;

/// <summary>Contains copied render inputs bound to one exact 3D frame.</summary>
public sealed class RenderSnapshot3D : RenderSnapshot
{
    private readonly SpatialFrame3 _frame;

    internal RenderSnapshot3D(
        Guid snapshotId,
        Guid worldGuid,
        long tickIndex,
        ImmutableArray<Guid> cameraIds,
        ImmutableArray<CameraSnapshot> cameras,
        PixelSize targetExtent,
        SpatialFrame3 frame)
        : base(snapshotId, worldGuid, tickIndex, cameraIds, cameras, targetExtent)
    {
        RenderExtractionValidation.ValidateFrame(frame, nameof(frame));

        if (cameras.Length != 0 && (cameras[0].WorldGuid != worldGuid))
        {
            throw new ArgumentException("The 3D snapshot cameras must belong to its exact World.", nameof(cameras));
        }

        _frame = frame;
    }

    /// <summary>Gets the exact 3D frame represented by this snapshot.</summary>
    public SpatialFrame3 Frame => _frame;

    /// <inheritdoc/>
    public override RenderFrameDimension FrameDimension => RenderFrameDimension.ThreeDimensional;

    /// <inheritdoc/>
    public override long FrameRevision => _frame.Revision;

    /// <inheritdoc/>
    public override SpatialProfileId ProfileId => _frame.ProfileId;
}
