// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX;

/// <summary>Identifies the component storage policy for a schema.</summary>
public enum ContentSchemaStorage
{
    /// <summary>The schema does not define a component.</summary>
    NotComponent = 0,

    /// <summary>The component is authoritative and persistent.</summary>
    Persistent = 1,

    /// <summary>The component is transient.</summary>
    Transient = 2,
}
