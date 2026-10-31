// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX.Entities;

namespace TerraFX.Scenes;

/// <summary>Creates the built-in Scene support registration for a World.</summary>
public static class SceneSupport
{
    /// <summary>Creates the opaque registration for persistent Scene transform, root-position, and camera components.</summary>
    /// <returns>The immutable Scene support registration.</returns>
    public static SceneSupportRegistration CreateRegistration()
        => new SceneSupportRegistration(new BuiltInSceneComponentRegistration());

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
