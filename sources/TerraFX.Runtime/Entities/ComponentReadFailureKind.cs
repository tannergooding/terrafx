// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Identifies one owner-level component read failure.</summary>
public enum ComponentReadFailureKind
{
    /// <summary>No valid failure kind is selected.</summary>
    None = 0,

    /// <summary>The source record is malformed.</summary>
    MalformedRecord = 1,

    /// <summary>The record schema does not match the selected component schema.</summary>
    SchemaMismatch = 2,

    /// <summary>A declared field is invalid.</summary>
    InvalidField = 3,

    /// <summary>A field value violates its schema-version contract.</summary>
    InvalidValue = 4,

    /// <summary>The provider reported a consumed extent other than the complete record.</summary>
    ConsumptionMismatch = 5,
}
