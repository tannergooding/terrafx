// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Identifies one ordinary malformed attachment state read failure.</summary>
public enum WorldAttachmentReadFailureKind
{
    /// <summary>No valid failure kind is selected.</summary>
    None = 0,

    /// <summary>The source record is malformed.</summary>
    MalformedRecord = 1,

    /// <summary>The source schema does not match the selected attachment schema.</summary>
    SchemaMismatch = 2,

    /// <summary>A selected schema field is invalid.</summary>
    InvalidField = 3,

    /// <summary>A source field value is invalid.</summary>
    InvalidValue = 4,

    /// <summary>The decoder did not consume the exact source extent.</summary>
    ConsumptionMismatch = 5,
}
