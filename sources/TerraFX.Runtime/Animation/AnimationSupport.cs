// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Assets;

namespace TerraFX.Animation;

/// <summary>Registers native Animation asset codecs for one compiled schema registry.</summary>
public static class AnimationSupport
{
    /// <summary>Registers the native Skeleton and Animation Clip asset codecs.</summary>
    /// <param name="builder">The asset-store builder whose registry contains the Animation schemas.</param>
    /// <returns>The builder with both Animation asset codecs registered.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    /// <exception cref="ArgumentException">The builder does not contain the exact Animation schema tuples or a registration conflicts.</exception>
    public static AssetStoreRegistrationBuilder RegisterAssetCodecs(AssetStoreRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.RegisterCodec(new SkeletonAssetCodec());
        return builder.RegisterCodec(new AnimationClipAssetCodec());
    }
}
