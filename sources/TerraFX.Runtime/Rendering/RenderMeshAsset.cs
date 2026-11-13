// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Assets;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

/// <summary>Represents one immutable package-backed native Mesh asset.</summary>
internal sealed class RenderMeshAsset
{
    internal RenderMeshAsset(
        AssetId assetId,
        int vertexCount,
        ImmutableArray<Vector3> positions,
        ImmutableArray<Vector3> normals,
        ImmutableArray<Vector4> tangents,
        ImmutableArray<Vector2> uv0,
        ImmutableArray<Vector2> uv1,
        ImmutableArray<Vector4> colors,
        ImmutableArray<ushort> jointIndices,
        ImmutableArray<Vector4> weights,
        RenderMeshIndexFormat indexFormat,
        ImmutableArray<uint> indices,
        ImmutableArray<RenderMeshGroup> groups,
        ImmutableArray<AssetId> materialSlots,
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

        if (positions.IsDefaultOrEmpty || (positions.Length != vertexCount))
        {
            throw new ArgumentException("The Mesh Position stream must cover every vertex.", nameof(positions));
        }

        if ((!normals.IsDefault && (normals.Length != vertexCount))
            || (!tangents.IsDefault && (tangents.Length != vertexCount))
            || (!uv0.IsDefault && (uv0.Length != vertexCount))
            || (!uv1.IsDefault && (uv1.Length != vertexCount))
            || (!colors.IsDefault && (colors.Length != vertexCount))
            || (!weights.IsDefault && (weights.Length != vertexCount))
            || (!jointIndices.IsDefault && (jointIndices.Length != checked(vertexCount * 4))))
        {
            throw new ArgumentException("Every present Mesh vertex stream must match the declared vertex count.");
        }

        if (groups.IsDefaultOrEmpty || materialSlots.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A Mesh requires initialized groups and material slots.");
        }

        if (!Enum.IsDefined(indexFormat) || indices.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A Mesh requires one initialized index stream matching its index format.");
        }

        if ((jointIndices.IsDefault != weights.IsDefault) || (jointIndices.IsDefault != !skeletonAssetId.HasValue))
        {
            throw new ArgumentException("Mesh joint, weight, and Skeleton streams must be present together.");
        }

        if (encodedRecord.IsDefaultOrEmpty)
        {
            throw new ArgumentException("The Mesh record must be initialized and nonempty.", nameof(encodedRecord));
        }

        AssetId = assetId;
        VertexCount = vertexCount;
        Positions = positions;
        Normals = normals;
        Tangents = tangents;
        Uv0 = uv0;
        Uv1 = uv1;
        Colors = colors;
        JointIndices = jointIndices;
        Weights = weights;
        IndexFormat = indexFormat;
        Indices = indices;
        Groups = groups;
        MaterialSlots = materialSlots;
        SkeletonAssetId = skeletonAssetId;
        Bounds = bounds;
        EncodedRecord = encodedRecord;
    }

    internal AssetId AssetId { get; }

    internal RenderMeshBounds Bounds { get; }

    internal ImmutableArray<Vector4> Colors { get; }

    internal ImmutableArray<byte> EncodedRecord { get; }

    internal ImmutableArray<RenderMeshGroup> Groups { get; }

    internal RenderMeshIndexFormat IndexFormat { get; }

    internal ImmutableArray<uint> Indices { get; }

    internal ImmutableArray<ushort> JointIndices { get; }

    internal ImmutableArray<AssetId> MaterialSlots { get; }

    internal ImmutableArray<Vector3> Normals { get; }

    internal ImmutableArray<Vector3> Positions { get; }

    internal AssetId? SkeletonAssetId { get; }

    internal ImmutableArray<Vector4> Tangents { get; }

    internal ImmutableArray<Vector2> Uv0 { get; }

    internal ImmutableArray<Vector2> Uv1 { get; }

    internal int VertexCount { get; }

    internal ImmutableArray<Vector4> Weights { get; }
}
