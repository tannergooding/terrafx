// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Assets;

namespace TerraFX.Rendering;

public sealed partial class Renderer
{
    /// <summary>Registers the exact versioned asset codecs selected by the M05 CPU feature set.</summary>
    /// <param name="builder">The registration builder that owns the codecs.</param>
    /// <param name="features">The selected M05 CPU feature families.</param>
    /// <returns>The same builder for registration composition.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="features"/> contains unknown bits.</exception>
    public static AssetStoreRegistrationBuilder RegisterM05AssetCodecs(
        AssetStoreRegistrationBuilder builder,
        RendererCpuFeatures features)
    {
        ArgumentNullException.ThrowIfNull(builder);

        const RendererCpuFeatures knownFeatures = RendererCpuFeatures.Animation
            | RendererCpuFeatures.CosmeticParticles
            | RendererCpuFeatures.MeshLod
            | RendererCpuFeatures.Picking;

        if ((features & ~knownFeatures) != RendererCpuFeatures.Static)
        {
            throw new ArgumentOutOfRangeException(nameof(features));
        }

        if (features is RendererCpuFeatures.Static)
        {
            return builder;
        }

        _ = RegisterMeshLodAssetCodecs(builder);

        if ((features & RendererCpuFeatures.CosmeticParticles) != 0)
        {
            _ = RegisterCosmeticParticleAssetCodecs(builder);
        }

        return builder;
    }
}
