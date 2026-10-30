// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Identifies how one semantic component storage leaf is represented.</summary>
public enum ComponentStorageKind
{
    /// <summary>An invalid default storage kind.</summary>
    None = 0,

    /// <summary>A required schema value.</summary>
    Value = 1,

    /// <summary>An optional schema value.</summary>
    OptionalValue = 2,

    /// <summary>A mapped Entity handle.</summary>
    EntityHandle = 3,

    /// <summary>An optional mapped Entity handle.</summary>
    OptionalEntityHandle = 4,

    /// <summary>A bounded inline array.</summary>
    InlineArray = 5,

    /// <summary>A composite schema value.</summary>
    Composite = 6,

    /// <summary>A declared runtime-only transient value.</summary>
    TransientNativeValue = 7,
}
