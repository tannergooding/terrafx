// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX;

/// <summary>Identifies one kind of physical schema path segment.</summary>
public enum BinarySchemaPathSegmentKind
{
    /// <summary>No valid path segment kind.</summary>
    None = 0,

    /// <summary>A field tag.</summary>
    FieldTag = 1,

    /// <summary>A fixed-array index.</summary>
    ArrayIndex = 2,
}
