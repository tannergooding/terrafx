// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Assets;

namespace TerraFX.Rendering;

public sealed partial class Renderer
{
    private static AssetStoreRegistrationBuilder RegisterMeshLodAssetCodecs(AssetStoreRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.RegisterCodec(new RenderMeshAsset2Codec());
        return builder.RegisterCodec(new RenderMaterialAsset2Codec());
    }
}
