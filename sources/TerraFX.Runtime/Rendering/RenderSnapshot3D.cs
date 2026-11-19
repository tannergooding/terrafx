// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using TerraFX;

namespace TerraFX.Rendering;

/// <summary>Contains copied render inputs bound to one exact 3D frame.</summary>
public sealed class RenderSnapshot3D : RenderSnapshot
{
    private readonly SpatialFrame3 _frame;
    private readonly ImmutableArray<RenderMeshCameraOrder> _meshCameraOrders;
    private readonly ImmutableArray<RenderMeshSnapshot> _meshDraws;
    private readonly ImmutableArray<RenderMeshPalette> _meshPalettes;

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
        WorldDiagnosticAttributionAccount diagnosticAttributionAccount,
        PixelSize targetExtent,
        SpatialFrame3 frame,
        ImmutableArray<RenderMeshSnapshot> meshDraws,
        ImmutableArray<RenderMeshCameraOrder> meshCameraOrders,
        ImmutableArray<RenderMeshPalette> meshPalettes)
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
            diagnosticAttributionAccount,
            targetExtent)
    {
        RenderExtractionValidation.ValidateFrame(frame, nameof(frame));

        if (cameras.Length != 0
            && ((cameras[0].WorldGuid != worldGuid)
                || !RenderExtractionValidation.MatchesFrame(cameras[0].Frame3, frame)))
        {
            throw new ArgumentException("The 3D snapshot cameras must belong to its exact World frame.", nameof(cameras));
        }

        if (meshDraws.IsDefault || meshCameraOrders.IsDefault || meshPalettes.IsDefault
            || (meshCameraOrders.Length != cameras.Length))
        {
            throw new ArgumentException("The 3D Mesh snapshot arrays must be initialized and camera-aligned.");
        }

        var drawIds = new HashSet<Guid>();

        foreach (var sprite in spriteDraws)
        {
            if (!drawIds.Add(sprite.DrawId))
            {
                throw new ArgumentException("Render draw identities must be unique across Sprites and Mesh groups.", nameof(spriteDraws));
            }
        }

        var paletteEntityIds = new HashSet<Guid>();

        for (var index = 0; index < meshPalettes.Length; index++)
        {
            var palette = meshPalettes[index];

            if ((palette.EntityId == Guid.Empty)
                || !paletteEntityIds.Add(palette.EntityId)
                || (palette.MeshAssetId <= 0)
                || (palette.SkeletonAssetId <= 0)
                || palette.SkinMatrices.IsDefaultOrEmpty)
            {
                throw new ArgumentException("The Mesh bind-pose palettes must be initialized and unique per Entity.", nameof(meshPalettes));
            }

            foreach (var skinMatrix in palette.SkinMatrices)
            {
                if (!skinMatrix.IsFinite().All())
                {
                    throw new ArgumentException("A Mesh bind-pose palette contains a nonfinite matrix.", nameof(meshPalettes));
                }
            }
        }

        var seenPaletteIndices = new bool[meshPalettes.Length];

        for (var index = 0; index < meshDraws.Length; index++)
        {
            var meshDraw = meshDraws[index];
            var expectedDrawId = FoundationNameUuid.Create(
                meshDraw.EntityId,
                "mesh-group/" + meshDraw.GroupIndex.ToString("x8", CultureInfo.InvariantCulture));

            if ((meshDraw.SnapshotId != snapshotId)
                || (meshDraw.WorldGuid != worldGuid)
                || (meshDraw.SceneInstanceId == Guid.Empty)
                || (meshDraw.DrawId == Guid.Empty)
                || (meshDraw.EntityId == Guid.Empty)
                || (meshDraw.MeshAssetId <= 0)
                || (meshDraw.GroupIndex < 0)
                || (meshDraw.MaterialSlot < 0)
                || (meshDraw.MaterialAssetId <= 0)
                || (meshDraw.DrawId != expectedDrawId)
                || (meshDraw.PaletteIndex < -1)
                || (meshDraw.PaletteIndex >= meshPalettes.Length)
                || (meshDraw.PaletteIndex >= 0
                    && ((meshPalettes[meshDraw.PaletteIndex].EntityId != meshDraw.EntityId)
                        || (meshPalettes[meshDraw.PaletteIndex].MeshAssetId != meshDraw.MeshAssetId)))
                || !meshDraw.LinearTransform.IsFinite().All()
                || !RenderExtractionValidation.MatchesFrame(meshDraw.Frame3, frame)
                || !drawIds.Add(meshDraw.DrawId))
            {
                throw new ArgumentException("A Mesh draw does not match the captured snapshot identity or frame.", nameof(meshDraws));
            }

            if (assetPins is null)
            {
                throw new ArgumentException("A Mesh draw has no retained Scene asset pins.", nameof(assetPins));
            }

            var sceneAssets = assetPins.GetSceneAssets(meshDraw.SceneInstanceId);
            var mesh = sceneAssets.GetMesh(meshDraw.MeshAssetId);

            if ((meshDraw.GroupIndex >= mesh.Groups.Length)
                || (meshDraw.MaterialSlot >= mesh.MaterialSlots.Length)
                || (mesh.Groups[meshDraw.GroupIndex].MaterialSlot != (uint)meshDraw.MaterialSlot)
                || (mesh.MaterialSlots[meshDraw.MaterialSlot].Value != meshDraw.MaterialAssetId))
            {
                throw new ArgumentException("A Mesh draw does not match its retained Mesh group and Material.", nameof(meshDraws));
            }

            var skeleton = sceneAssets.GetSkeleton(meshDraw.MeshAssetId);

            if (meshDraw.PaletteIndex < 0)
            {
                if (skeleton is not null)
                {
                    throw new ArgumentException("A skinned Mesh draw omitted its bind-pose palette.", nameof(meshDraws));
                }
            }
            else if ((skeleton is null)
                || (meshPalettes[meshDraw.PaletteIndex].SkeletonAssetId != mesh.SkeletonAssetId?.Value)
                || (meshPalettes[meshDraw.PaletteIndex].SkinMatrices.Length != skeleton.Joints.Length))
            {
                throw new ArgumentException("A Mesh draw's bind-pose palette does not match its selected Skeleton.", nameof(meshDraws));
            }
            else
            {
                seenPaletteIndices[meshDraw.PaletteIndex] = true;
            }

            _ = sceneAssets.GetMaterial(meshDraw.MaterialAssetId);
        }

        for (var paletteIndex = 0; paletteIndex < seenPaletteIndices.Length; paletteIndex++)
        {
            if (!seenPaletteIndices[paletteIndex])
            {
                throw new ArgumentException("A Mesh bind-pose palette is not referenced by any draw.", nameof(meshPalettes));
            }
        }

        for (var cameraIndex = 0; cameraIndex < meshCameraOrders.Length; cameraIndex++)
        {
            var meshCameraOrder = meshCameraOrders[cameraIndex];

            if ((meshCameraOrder.CameraId != cameraIds[cameraIndex]) || meshCameraOrder.DrawIndices.IsDefault)
            {
                throw new ArgumentException("A Mesh camera order does not match its camera or initialized draw set.", nameof(meshCameraOrders));
            }

            var seenIndices = new bool[meshDraws.Length];
            var expectedCount = 0;

            for (var drawIndex = 0; drawIndex < meshDraws.Length; drawIndex++)
            {
                if ((meshDraws[drawIndex].LayerMask & cameras[cameraIndex].LayerMask) != 0)
                {
                    expectedCount++;
                }
            }

            if (meshCameraOrder.DrawIndices.Length != expectedCount)
            {
                throw new ArgumentException("A Mesh camera order must contain every selected draw exactly once.", nameof(meshCameraOrders));
            }

            foreach (var drawIndex in meshCameraOrder.DrawIndices)
            {
                if ((drawIndex < 0)
                    || (drawIndex >= meshDraws.Length)
                    || seenIndices[drawIndex]
                    || ((meshDraws[drawIndex].LayerMask & cameras[cameraIndex].LayerMask) == 0))
                {
                    throw new ArgumentException("A Mesh camera order contains a duplicate, unselected, or out-of-range draw index.", nameof(meshCameraOrders));
                }

                seenIndices[drawIndex] = true;
            }
        }

        _frame = frame;
        _meshCameraOrders = [.. meshCameraOrders];
        _meshDraws = [.. meshDraws];
        _meshPalettes = [.. meshPalettes];
    }

    /// <summary>Gets the exact 3D frame represented by this snapshot.</summary>
    public SpatialFrame3 Frame => _frame;

    /// <inheritdoc/>
    public override RenderFrameDimension FrameDimension => RenderFrameDimension.ThreeDimensional;

    /// <inheritdoc/>
    public override long FrameRevision => _frame.Revision;

    /// <inheritdoc/>
    public override SpatialProfileId ProfileId => _frame.ProfileId;

    internal ImmutableArray<RenderMeshCameraOrder> MeshCameraOrders => _meshCameraOrders;

    internal ImmutableArray<RenderMeshSnapshot> MeshDraws => _meshDraws;

    internal ImmutableArray<RenderMeshPalette> MeshPalettes => _meshPalettes;
}
