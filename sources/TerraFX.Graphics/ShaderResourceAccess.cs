// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Describes the access capability declared by one reflected shader resource.</summary>
public enum ShaderResourceAccess
{
    /// <summary>No valid access capability.</summary>
    None = 0,

    /// <summary>The shader resource is read-only.</summary>
    Read = 1,

    /// <summary>The shader resource is write-only.</summary>
    Write = 2,

    /// <summary>The shader resource can be read and written.</summary>
    ReadWrite = 3,
}
