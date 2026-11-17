// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Rendering;

internal sealed partial class RenderMaterialAssetCodec
{
    internal static RenderMaterialAssetData ReadSharedData(EbinRecordReadView record)
        => ReadMaterialData(record);
}
