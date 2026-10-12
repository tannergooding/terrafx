// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX;

/// <summary>Identifies the registry that owns a content schema reference.</summary>
public enum ContentSchemaScope
{
    /// <summary>The schema is declared by a content catalogue.</summary>
    Content = 0,

    /// <summary>The schema is declared by the M02 foundation catalogue.</summary>
    Foundation = 1
}
