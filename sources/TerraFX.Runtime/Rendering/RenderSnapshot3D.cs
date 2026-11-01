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
        ImmutableArray<Guid> directionalLightIds,
        ImmutableArray<RenderDirectionalLightSnapshot> directionalLights,
        ImmutableArray<Guid> pointLightIds,
        ImmutableArray<RenderPointLightSnapshot> pointLights,
        ImmutableArray<Guid> spotLightIds,
        ImmutableArray<RenderSpotLightSnapshot> spotLights,
        ImmutableArray<RenderSpriteSnapshot> spriteDraws,
        ImmutableArray<RenderSpriteCameraOrder> spriteCameraOrders,
        RenderSnapshotAssetPins? assetPins,
        PixelSize targetExtent,
        SpatialFrame3 frame)
        : base(
            snapshotId,
            worldGuid,
            tickIndex,
            cameraIds,
            cameras,
            directionalLightIds,
            directionalLights,
            pointLightIds,
            pointLights,
            spotLightIds,
            spotLights,
            spriteDraws,
            spriteCameraOrders,
            assetPins,
            targetExtent)
    {
        RenderExtractionValidation.ValidateFrame(frame, nameof(frame));

        if (cameras.Length != 0
            && ((cameras[0].WorldGuid != worldGuid)
                || !RenderExtractionValidation.MatchesFrame(cameras[0].Frame3, frame)))
        {
            throw new ArgumentException("The 3D snapshot cameras must belong to its exact World frame.", nameof(cameras));
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
