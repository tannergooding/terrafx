// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Identifies the result of reading one World-system state value.</summary>
public enum WorldSystemReadStatus
{
    /// <summary>No valid read result is selected.</summary>
    None = 0,

    /// <summary>The source was decoded to one complete author value.</summary>
    Read = 1,

    /// <summary>The source was rejected as malformed or unsupported.</summary>
    Rejected = 2,
}
