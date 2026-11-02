// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Defines how a graph attachment initializes its written range.</summary>
public enum AttachmentLoadAction
{
    /// <summary>Initialize the range with the declared clear value.</summary>
    Clear = 0,

    /// <summary>Read the previous version's contents before writing.</summary>
    Load = 1,

    /// <summary>Begin writing without preserving prior contents.</summary>
    DontCare = 2,
}
