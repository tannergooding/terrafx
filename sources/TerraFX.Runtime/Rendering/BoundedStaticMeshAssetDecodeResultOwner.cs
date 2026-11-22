// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Assets;

namespace TerraFX.Rendering;

internal ref struct BoundedStaticMeshAssetDecodeResultOwner
{
    private readonly RenderMaterialAsset? _material;
    private readonly RenderMeshAsset? _mesh;
    private readonly int _meshReferenceCount;
    private AssetBoundedOperationClaim _operationClaim;

    internal BoundedStaticMeshAssetDecodeResultOwner(
        RenderMeshAsset mesh,
        RenderMaterialAsset material,
        int meshReferenceCount,
        AssetBoundedOperationClaim operationClaim)
    {
        _mesh = mesh;
        _material = material;
        _meshReferenceCount = meshReferenceCount;
        _operationClaim = operationClaim;
    }

    internal readonly bool IsActive
        => _operationClaim.IsActive && (_mesh is not null) && (_material is not null);

    internal readonly RenderMaterialAsset Material
        => _material
            ?? throw new InvalidOperationException("The bounded typed result has been returned.");

    internal readonly RenderMeshAsset Mesh
        => _mesh
            ?? throw new InvalidOperationException("The bounded typed result has been returned.");

    internal readonly int MeshReferenceCount
        => IsActive
            ? _meshReferenceCount
            : throw new InvalidOperationException("The bounded typed result has been returned.");

    internal void Return(out AssetBoundedOperationClaim operationClaim)
    {
        if (!IsActive)
        {
            operationClaim = default;
            throw new InvalidOperationException("The bounded typed result has already been returned.");
        }

        operationClaim = _operationClaim.Detach();
        this = default;
    }
}
