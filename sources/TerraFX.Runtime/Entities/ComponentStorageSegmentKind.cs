// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Identifies one kind of physical component storage-path segment.</summary>
public enum ComponentStorageSegmentKind
{
    /// <summary>An invalid default segment kind.</summary>
    None = 0,

    /// <summary>Selects an exact source member spelling.</summary>
    Member = 1,

    /// <summary>Selects a zero-based inline-array element.</summary>
    InlineIndex = 2,
}
