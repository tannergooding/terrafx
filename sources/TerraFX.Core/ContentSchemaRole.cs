// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX;

/// <summary>Identifies the semantic role assigned to a compiled content schema.</summary>
public enum ContentSchemaRole
{
    /// <summary>No schema role; invalid in a compiled schema descriptor.</summary>
    None = 0,

    /// <summary>Describes another schema.</summary>
    Descriptor = 1,

    /// <summary>Defines a native asset payload.</summary>
    NativeAsset = 2,

    /// <summary>Defines an entity component.</summary>
    Component = 3,

    /// <summary>Defines an entity tag.</summary>
    EntityTag = 4,

    /// <summary>Defines authoritative system state.</summary>
    SystemState = 5,

    /// <summary>Defines authoritative attachment state.</summary>
    AttachmentState = 6,

    /// <summary>Defines a committed checkpoint.</summary>
    Checkpoint = 7,

    /// <summary>Defines a committed event payload.</summary>
    EventPayload = 8,

    /// <summary>Defines a control payload.</summary>
    ControlPayload = 9,

    /// <summary>Defines offline cook data.</summary>
    Cook = 10,

    /// <summary>Defines auxiliary data.</summary>
    Auxiliary = 11,

    /// <summary>Defines authoritative resource state.</summary>
    ResourceState = 12,

    /// <summary>Defines typed settings.</summary>
    TypedSettings = 13,
}
