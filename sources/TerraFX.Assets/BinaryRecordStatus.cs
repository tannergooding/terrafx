// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Assets;

/// <summary>Describes a typed binary-record codec result.</summary>
public enum BinaryRecordStatus
{
    /// <summary>The complete record was read or written.</summary>
    Complete,

    /// <summary>The source does not contain a complete record yet.</summary>
    NeedMoreData,

    /// <summary>The destination cannot contain the complete record.</summary>
    DestinationTooSmall,

    /// <summary>The record is malformed or fails its selected schema contract.</summary>
    InvalidRecord,

    /// <summary>The schema tuple is not supported by the selected registration.</summary>
    UnsupportedSchema,
}
