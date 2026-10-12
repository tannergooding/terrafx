// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Identifies the access mode declared for a system resource.</summary>
public enum SystemAccessMode
{
    /// <summary>No access mode; valid only as a default value.</summary>
    None = 0,

    /// <summary>Read-only access.</summary>
    Read = 1,

    /// <summary>Read and write access.</summary>
    Write = 2,
}
