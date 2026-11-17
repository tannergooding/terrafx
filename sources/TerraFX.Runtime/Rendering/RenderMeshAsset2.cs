// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Assets;

namespace TerraFX.Rendering;

internal sealed class RenderMeshAsset2
{
    internal RenderMeshAsset2(
        AssetId assetId,
        int vertexCount,
        RenderMeshVertexStreams vertices,
        ImmutableArray<RenderMeshLodLevel> levels,
        ImmutableArray<float> thresholds,
        ImmutableArray<RenderMeshMaterialSlot> materialSlots,
        AssetId? skeletonAssetId,
        RenderMeshBounds bounds,
        ImmutableArray<byte> encodedRecord)
    {
        if (!assetId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(assetId));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(vertexCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(vertexCount, 262_144);

        if (levels.IsDefaultOrEmpty || (levels.Length > 8)
            || thresholds.IsDefault || (thresholds.Length != levels.Length - 1)
            || materialSlots.IsDefaultOrEmpty || encodedRecord.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A Mesh 2.0 value requires initialized levels, thresholds, references, and encoded backing.");
        }

        AssetId = assetId;
        VertexCount = vertexCount;
        Vertices = vertices;
        Levels = levels;
        Thresholds = thresholds;
        MaterialSlots = materialSlots;
        SkeletonAssetId = skeletonAssetId;
        Bounds = bounds;
        EncodedRecord = encodedRecord;
    }

    internal AssetId AssetId { get; }

    internal RenderMeshBounds Bounds { get; }

    internal ImmutableArray<byte> EncodedRecord { get; }

    internal ImmutableArray<RenderMeshLodLevel> Levels { get; }

    internal ImmutableArray<RenderMeshMaterialSlot> MaterialSlots { get; }

    internal AssetId? SkeletonAssetId { get; }

    internal ImmutableArray<float> Thresholds { get; }

    internal int VertexCount { get; }

    internal RenderMeshVertexStreams Vertices { get; }
}
