// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX;

/// <summary>Identifies a supported content-field authoring default form.</summary>
public enum ContentFieldDefaultKind
{
    /// <summary>No default is present.</summary>
    None = 0,

    /// <summary>A canonical primitive payload value.</summary>
    Literal = 1,

    /// <summary>An exact rational distance value converted to the declared unit.</summary>
    Meters = 2,

    /// <summary>A selected material property value.</summary>
    MaterialProperty = 3,
}
