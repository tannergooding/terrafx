// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Identifies a supported native presentation mode.</summary>
public enum GraphicsPresentMode
{
    /// <summary>No selected presentation mode.</summary>
    None = 0,

    /// <summary>FIFO presentation.</summary>
    Fifo = 1,

    /// <summary>Mailbox presentation.</summary>
    Mailbox = 2,

    /// <summary>Immediate presentation.</summary>
    Immediate = 3,
}
