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
        _ = builder.RegisterCodec(new SceneAssetCodec());
        return builder.RegisterCodec(new ScenePrefabAssetCodec());
    }

    private sealed class BuiltInSceneComponentRegistration : IWorldSceneSupportRegistration
    {
        public void RegisterComponents(WorldRegistrationBuilder builder, CompiledContentSchemaRegistry schemas)
        {
            _ = SceneComponentDefinitions.RegisterLocalTransform(builder, schemas);
            _ = SceneComponentDefinitions.RegisterRootPosition(builder, schemas);
            _ = SceneComponentDefinitions.RegisterCamera(builder, schemas);
        }
    }
}
