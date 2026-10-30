// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX;

/// <summary>Describes the outcome of reading or validating an EBIN record.</summary>
public enum EbinReadStatus
{
    /// <summary>The record was completely read or validated.</summary>
    Complete = 0,

    /// <summary>The available prefix is valid but incomplete.</summary>
    NeedMoreData = 1,

    /// <summary>The record violates the EBIN structural contract.</summary>
    InvalidRecord = 2,

    /// <summary>The record uses an unsupported container version or required kind.</summary>
    UnsupportedSchema = 3,
}
