// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX;

/// <summary>Identifies the subsystem that owns a compiled content schema.</summary>
public enum ContentSchemaOwner
{
    /// <summary>The Core foundation.</summary>
    Core = 0,

    /// <summary>The Assets subsystem.</summary>
    Assets = 1,

    /// <summary>The Entities subsystem.</summary>
    Entities = 2,

    /// <summary>The Scenes subsystem.</summary>
    Scenes = 3,

    /// <summary>The Rendering subsystem.</summary>
    Rendering = 4,

    /// <summary>The Animation subsystem.</summary>
    Animation = 5,

    /// <summary>The Physics subsystem.</summary>
    Physics = 6,

    /// <summary>The offline content tools.</summary>
    ContentTools = 7,

    /// <summary>The Hosting subsystem.</summary>
    Hosting = 8,

    /// <summary>The application.</summary>
    Application = 9,
}
