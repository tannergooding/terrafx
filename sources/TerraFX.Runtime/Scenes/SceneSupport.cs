// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Assets;
using TerraFX.Entities;

namespace TerraFX.Scenes;

/// <summary>Creates the built-in Scene support registration for a World.</summary>
public static class SceneSupport
{
    /// <summary>Creates the opaque registration for persistent Scene transform, root-position, and camera components.</summary>
    /// <returns>The immutable Scene support registration.</returns>
    public static SceneSupportRegistration CreateRegistration()
        => new SceneSupportRegistration(new BuiltInSceneComponentRegistration());

    /// <summary>Registers the typed Scene and Prefab codecs with an asset-store builder.</summary>
    /// <param name="builder">The asset-store builder whose schema registry contains <see cref="SceneSchemaCatalogue.Schemas"/>.</param>
    /// <returns>The builder with both Scene codecs registered.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    /// <exception cref="ArgumentException">The builder does not contain the exact compiled Scene and Prefab schemas or the registrations conflict.</exception>
    public static AssetStoreRegistrationBuilder RegisterAssetCodecs(AssetStoreRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.RegisterProfiledCodec(
            new SceneAssetCodec(),
            typeof(SceneCodecV2Origin),
            AssetPackagePayloadCodec.None);
        return builder.RegisterProfiledCodec(
            new ScenePrefabAssetCodec(),
            typeof(ScenePrefabCodecV2Origin),
            AssetPackagePayloadCodec.None);
    }

    internal static bool HasBuiltInAssetCodecReceipts(AssetStoreRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        return registration.TryGetProfiledCodecReceipt<SceneAsset>(typeof(SceneCodecV2Origin), out var sceneReceipt)
                && (sceneReceipt.Schema == SceneAssetCodec.Schema)
                && (sceneReceipt.RequiredPackageCodec == AssetPackagePayloadCodec.None)
                && registration.TryGetProfiledCodecReceipt<ScenePrefabAsset>(
                    typeof(ScenePrefabCodecV2Origin),
                    out var prefabReceipt)
                && (prefabReceipt.Schema == ScenePrefabAssetCodec.Schema)
                && (prefabReceipt.RequiredPackageCodec == AssetPackagePayloadCodec.None);
    }

    internal static bool TryDescribePrefabAssetRead(
        AssetLease<SceneAsset> sceneLease,
        AssetId assetId,
        out AssetOwnedReadDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(sceneLease);

        if (sceneLease.TryDescribeOwnedRead<ScenePrefabAsset>(
                assetId,
                typeof(ScenePrefabCodecV2Origin),
                out descriptor)
            && (descriptor.CodecReceipt.Schema == ScenePrefabAssetCodec.Schema))
        {
            return true;
        }

        descriptor = default;
        return false;
    }

    internal static bool TryDescribePrefabAssetRead(
        AssetPackageLease packageLease,
        AssetId assetId,
        out AssetOwnedReadDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(packageLease);

        if (packageLease.TryDescribeOwnedRead<ScenePrefabAsset>(
                assetId,
                typeof(ScenePrefabCodecV2Origin),
                out descriptor)
            && (descriptor.CodecReceipt.Schema == ScenePrefabAssetCodec.Schema))
        {
            return true;
        }

        descriptor = default;
        return false;
    }

    internal static bool TryDescribeSceneAssetRead(
        AssetPackageLease packageLease,
        AssetId assetId,
        out AssetOwnedReadDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(packageLease);

        if (packageLease.TryDescribeOwnedRead<SceneAsset>(
                assetId,
                typeof(SceneCodecV2Origin),
                out descriptor)
            && (descriptor.CodecReceipt.Schema == SceneAssetCodec.Schema))
        {
            return true;
        }

        descriptor = default;
        return false;
    }

    internal static bool TryDescribeSceneAssetRead(
        AssetStore assets,
        AssetId assetId,
        out AssetOwnedReadDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(assets);

        if (assets.TryDescribeOwnedRead<SceneAsset>(assetId, typeof(SceneCodecV2Origin), out descriptor)
            && (descriptor.CodecReceipt.Schema == SceneAssetCodec.Schema))
        {
            return true;
        }

        descriptor = default;
        return false;
    }

    private sealed class BuiltInSceneComponentRegistration : IWorldSceneSupportRegistration
    {
        public IWorldSceneParticipant CreateParticipant(World world, WorldOptions options)
            => new SceneWorldRegistry(world, options);

        public long GetWorldStorageByteReservation(WorldOptions options)
            => SceneWorldRegistry.GetWorldStorageByteReservation(options);

        public void RegisterComponents(WorldRegistrationBuilder builder, CompiledContentSchemaRegistry schemas)
        {
            _ = SceneComponentDefinitions.RegisterLocalTransform(builder, schemas);
            _ = SceneComponentDefinitions.RegisterRootPosition(builder, schemas);
            _ = SceneComponentDefinitions.RegisterCamera(builder, schemas);
        }
    }

    private sealed class SceneCodecV2Origin
    {
    }

    private sealed class ScenePrefabCodecV2Origin
    {
    }
}
