// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Identifies one kind of semantic component property-path segment.</summary>
public enum ComponentPropertySegmentKind
{
    /// <summary>An invalid default segment kind.</summary>
    None = 0,

    /// <summary>Selects a positive EBIN field tag.</summary>
    FieldTag = 1,

    /// <summary>Selects a zero-based FixedArray element.</summary>
    FixedArrayIndex = 2,
}
