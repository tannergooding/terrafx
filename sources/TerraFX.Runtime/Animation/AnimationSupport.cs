// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Assets;
using TerraFX.Entities;

namespace TerraFX.Animation;

/// <summary>Registers native Animation asset codecs for one compiled schema registry.</summary>
public static class AnimationSupport
{
    private static readonly object s_skeletonV1CodecOrigin = typeof(SkeletonV1CodecOrigin);

    /// <summary>Registers the native Skeleton and Animation Clip asset codecs.</summary>
    /// <param name="builder">The asset-store builder whose registry contains the Animation schemas.</param>
    /// <returns>The builder with both Animation asset codecs registered.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    /// <exception cref="ArgumentException">The builder does not contain the exact Animation schema tuples or a registration conflicts.</exception>
    public static AssetStoreRegistrationBuilder RegisterAssetCodecs(AssetStoreRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.RegisterProfiledCodec(
            new SkeletonAssetCodec(),
            s_skeletonV1CodecOrigin,
            AssetPackagePayloadCodec.None);
        return builder.RegisterCodec(new AnimationClipAssetCodec());
    }

    /// <summary>Registers the typed Animation Controller asset codec.</summary>
    /// <param name="builder">The asset-store builder whose registry contains the Animation playback schemas.</param>
    /// <returns>The builder with the Animation Controller codec registered.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    /// <exception cref="ArgumentException">The builder does not contain the exact Animation Controller schema tuple or a registration conflicts.</exception>
    public static AssetStoreRegistrationBuilder RegisterPlaybackAssetCodecs(AssetStoreRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.RegisterCodec(new AnimationControllerAssetCodec());
    }

    internal static AssetOwnedReadDescriptor DescribeSkeletonRead<TSource>(
        AssetLease<TSource> source,
        AssetId assetId)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.DescribeOwnedRead<SkeletonAsset>(assetId, s_skeletonV1CodecOrigin);
    }

    internal static bool HasBuiltInSkeletonAssetCodecReceipt(AssetStoreRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        return registration.TryGetProfiledCodecReceipt<SkeletonAsset>(
                s_skeletonV1CodecOrigin,
                out var receipt)
            && (receipt.Schema.Major == 1)
            && (receipt.Schema.Minor == 0)
            && (receipt.Schema.Scope == ContentSchemaScope.Content)
            && (receipt.RequiredPackageCodec == AssetPackagePayloadCodec.None);
    }

    internal static WorldRegistrationBuilder RegisterPlayerComponent(
        WorldRegistrationBuilder builder,
        CompiledContentSchemaRegistry schemas)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(schemas);
        return AnimationComponentDefinitions.RegisterPlayer(builder, schemas);
    }

    private sealed class SkeletonV1CodecOrigin
    {
    }
}
