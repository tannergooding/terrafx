// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Identifies the semantic purpose of one component storage leaf.</summary>
public enum ComponentStoragePurpose
{
    /// <summary>An invalid default storage purpose.</summary>
    None = 0,

    /// <summary>Stores a value declared by the component schema.</summary>
    SchemaValue = 1,

    /// <summary>Stores an explicitly declared runtime-only value.</summary>
    DeclaredRuntimeOnly = 2,
}
