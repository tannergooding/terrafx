// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Graphics;
using TerraFX.Scenes;

namespace TerraFX.Rendering;

internal static class BoundedStaticMeshAssetRegistrationFactory
{
    private const int SchemaCount = 81;

    internal static AssetStoreRegistration Create()
    {
        var schemas = new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
            .. RenderingSchemaCatalogue.Schemas,
        ]);

        if ((schemas.Schemas.Length != SchemaCount) || !schemas.Profiles.IsEmpty)
        {
            throw new InvalidOperationException("The bounded static-Mesh schema set changed from its reviewed membership.");
        }

        var builder = new AssetStoreRegistrationBuilder(schemas);
        _ = builder.RegisterCodec(new RenderMeshAssetCodec());
        _ = builder.RegisterCodec(new RenderMaterialAssetCodec());
        return builder.BuildBoundedStaticMeshValidationV1();
    }
}
