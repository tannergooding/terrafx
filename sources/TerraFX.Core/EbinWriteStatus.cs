// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX;

/// <summary>Describes the outcome of an EBIN structural write.</summary>
public enum EbinWriteStatus
{
    /// <summary>The candidate was prepared or written successfully.</summary>
    Complete = 0,

    /// <summary>The described record violates the structural contract.</summary>
    InvalidRecord = 1,

    /// <summary>The record requires an unsupported wire kind.</summary>
    UnsupportedSchema = 2,

    /// <summary>The caller-provided candidate workspace is too small.</summary>
    WorkspaceTooSmall = 3,

    /// <summary>The final destination is too small.</summary>
    DestinationTooSmall = 4,
}
