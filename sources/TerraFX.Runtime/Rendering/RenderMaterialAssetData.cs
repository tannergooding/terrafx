// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Rendering;

internal readonly record struct RenderMaterialAssetData(
    Guid ProfileId,
    bool IsSprite,
    Guid ShaderId,
    Guid PassId,
    Guid LayoutId,
    RenderMaterialAsset.ParameterValues Parameters,
    RenderMaterialAsset.MaterialAlphaMode AlphaMode,
    float AlphaCutoff,
    bool DoubleSided,
    bool DepthTest,
    bool DepthWrite,
    RenderMaterialAsset.MaterialCullMode CullMode,
    RenderMaterialAsset.MaterialOutputAlpha OutputAlpha,
    ulong RequiredCapabilities);
