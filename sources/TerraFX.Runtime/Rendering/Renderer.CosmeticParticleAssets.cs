// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Assets;

namespace TerraFX.Rendering;

public sealed partial class Renderer
{
    private static AssetStoreRegistrationBuilder RegisterCosmeticParticleAssetCodecs(AssetStoreRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.RegisterCodec(new CosmeticParticleEmitterAssetCodec());
    }
}
