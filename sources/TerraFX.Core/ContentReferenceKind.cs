// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX;

/// <summary>Identifies the semantic reference annotation of a content field.</summary>
public enum ContentReferenceKind
{
    /// <summary>No reference semantics are assigned.</summary>
    None = 0,

    /// <summary>References a schema.</summary>
    Schema = 1,

    /// <summary>References an asset.</summary>
    Asset = 2,

    /// <summary>References an entity.</summary>
    Entity = 3,

    /// <summary>References a joint.</summary>
    Joint = 4,

    /// <summary>References a World.</summary>
    World = 5,

    /// <summary>References a scene instance.</summary>
    SceneInstance = 6,

    /// <summary>References a recipe.</summary>
    Recipe = 7,

    /// <summary>References a system.</summary>
    System = 8,

    /// <summary>References a profile.</summary>
    Profile = 9,

    /// <summary>References an event.</summary>
    Event = 10,

    /// <summary>References a stream.</summary>
    Stream = 11,

    /// <summary>References a module.</summary>
    Module = 12,

    /// <summary>References a material.</summary>
    Material = 13,

    /// <summary>References a factory.</summary>
    Factory = 14,

    /// <summary>References an external scene binding.</summary>
    ExternalSceneBinding = 15,

    /// <summary>References an entity or joint.</summary>
    EntityOrJoint = 16,

    /// <summary>Identifies a historical entity.</summary>
    HistoricalEntity = 17,

    /// <summary>Identifies a historical scene instance.</summary>
    HistoricalSceneInstance = 18,

    /// <summary>References a resource or schema.</summary>
    ResourceOrSchema = 19,
}
