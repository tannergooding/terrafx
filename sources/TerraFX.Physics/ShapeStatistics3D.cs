// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX.Numerics;

namespace TerraFX.Physics;

/// <summary>Reports exact-formula 3D primitive statistics and their validation provenance.</summary>
public sealed class ShapeStatistics3D
{
    internal ShapeStatistics3D(SpatialSettings spatialSettings,
        int? vertexCount, int? edgeCount, int? faceCount, ShapeMeasure signedVolume,
        Vector3 localMinimum, Vector3 localMaximum, float enclosingRadius, int connectedComponentCount,
        int? weldedVertexCount, int? patchCount, float? maximumSnapDisplacement)
    {
        SpatialSettings = spatialSettings;
        VertexCount = vertexCount;
        EdgeCount = edgeCount;
        FaceCount = faceCount;
        SignedVolume = signedVolume;
        LocalMinimum = localMinimum;
        LocalMaximum = localMaximum;
        EnclosingRadius = enclosingRadius;
        ConnectedComponentCount = connectedComponentCount;
        WeldedVertexCount = weldedVertexCount;
        PatchCount = patchCount;
        MaximumSnapDisplacement = maximumSnapDisplacement;
    }

    /// <summary>Gets the connected-component count when defined by the primitive formula.</summary>
    public int ConnectedComponentCount { get; }

    /// <summary>Gets the analytic edge count, or <see langword="null"/> when undefined.</summary>
    public int? EdgeCount { get; }

    /// <summary>Gets the conservatively rounded radius enclosing the local bounds.</summary>
    public float EnclosingRadius { get; }

    /// <summary>Gets the analytic face count, or <see langword="null"/> when undefined.</summary>
    public int? FaceCount { get; }

    /// <summary>Gets the local-space maximum bounds.</summary>
    public Vector3 LocalMaximum { get; }

    /// <summary>Gets the local-space minimum bounds.</summary>
    public Vector3 LocalMinimum { get; }

    /// <summary>Gets the mesh-processing displacement, or <see langword="null"/> when inapplicable.</summary>
    public float? MaximumSnapDisplacement { get; }

    /// <summary>Gets the patch count, or <see langword="null"/> when inapplicable.</summary>
    public int? PatchCount { get; }

    /// <summary>Gets the positive volume represented by a scaled exact-formula pair.</summary>
    public ShapeMeasure SignedVolume { get; }

    /// <summary>Gets the spatial settings under which the statistics were computed.</summary>
    public SpatialSettings SpatialSettings { get; }

    /// <summary>Gets the analytic vertex count, or <see langword="null"/> when undefined.</summary>
    public int? VertexCount { get; }

    /// <summary>Gets the welded vertex count, or <see langword="null"/> when inapplicable.</summary>
    public int? WeldedVertexCount { get; }
}
