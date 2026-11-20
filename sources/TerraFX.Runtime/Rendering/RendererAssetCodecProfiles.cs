// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Assets;
using TerraFX.Graphics;
using TerraFX.Scenes;

namespace TerraFX.Rendering;

internal static class RendererAssetCodecProfiles
{
    private static readonly object s_atlasV1Origin = typeof(AtlasV1CodecOrigin);
    private static readonly object s_materialV1Origin = typeof(MaterialV1CodecOrigin);
    private static readonly object s_meshV1Origin = typeof(MeshV1CodecOrigin);
    private static readonly object s_textureV1Origin = typeof(TextureV1CodecOrigin);

    internal static object AtlasV1Origin => s_atlasV1Origin;

    internal static object MaterialV1Origin => s_materialV1Origin;

    internal static object MeshV1Origin => s_meshV1Origin;

    internal static object TextureV1Origin => s_textureV1Origin;

    internal static AssetOwnedReadDescriptor DescribeAtlasRead(
        SceneExpansionPlan plan,
        AssetId assetId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.DescribeOwnedRead<RenderAtlasAsset>(assetId, s_atlasV1Origin);
    }

    internal static AssetOwnedReadDescriptor DescribeMaterialRead(
        SceneExpansionPlan plan,
        AssetId assetId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.DescribeOwnedRead<RenderMaterialAsset>(assetId, s_materialV1Origin);
    }

    internal static AssetOwnedReadDescriptor DescribeMeshRead(
        SceneExpansionPlan plan,
        AssetId assetId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.DescribeOwnedRead<RenderMeshAsset>(assetId, s_meshV1Origin);
    }

    internal static AssetOwnedReadDescriptor DescribeTextureRead<TSource>(
        AssetLease<TSource> source,
        AssetId assetId)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.DescribeOwnedRead<RenderTextureAsset>(assetId, s_textureV1Origin);
    }

    internal static bool HasBuiltInStaticAssetCodecReceipts(AssetStoreRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        return HasReceipt<RenderMeshAsset>(registration, s_meshV1Origin, "native/mesh")
            && HasReceipt<RenderTextureAsset>(registration, s_textureV1Origin, "native/texture")
            && HasReceipt<RenderAtlasAsset>(registration, s_atlasV1Origin, "native/atlas")
            && HasReceipt<RenderMaterialAsset>(registration, s_materialV1Origin, "native/material");
    }

    private static bool HasReceipt<T>(
        AssetStoreRegistration registration,
        object profileOrigin,
        string schemaName)
    {
        if (!registration.TryGetProfiledCodecReceipt<T>(profileOrigin, out var receipt)
            || (receipt.RequiredPackageCodec != AssetPackagePayloadCodec.None))
        {
            return false;
        }

        if (TryGetSchema(schemaName, RenderingSchemaCatalogue.Schemas, out var renderingSchema)
            || TryGetSchema(schemaName, NativeAssetSchemaCatalogue.Schemas, out renderingSchema))
        {
            return receipt.Schema == renderingSchema;
        }

        return false;
    }

    private static bool TryGetSchema(
        string schemaName,
        ImmutableArray<DataSchema> schemas,
        out ContentSchemaReference schemaReference)
    {
        foreach (var schema in schemas)
        {
            if (StringComparer.Ordinal.Equals(schema.IdentityAssignment.Name, schemaName))
            {
                schemaReference = schema.Schema;
                return true;
            }
        }

        schemaReference = default;
        return false;
    }

    private sealed class AtlasV1CodecOrigin
    {
    }

    private sealed class MaterialV1CodecOrigin
    {
    }

    private sealed class MeshV1CodecOrigin
    {
    }

    private sealed class TextureV1CodecOrigin
    {
    }
}
