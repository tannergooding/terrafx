// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Graphics;

/// <summary>Identifies the element width of one index-buffer binding.</summary>
public enum IndexFormat
{
    /// <summary>Unsigned 16-bit indices.</summary>
    [SuppressMessage(
        "Naming",
        "CA1720:Identifier contains type name",
        Justification = "The public format name preserves the reviewed native index-width contract.")]
    UInt16 = 0,

    /// <summary>Unsigned 32-bit indices.</summary>
    [SuppressMessage(
        "Naming",
        "CA1720:Identifier contains type name",
        Justification = "The public format name preserves the reviewed native index-width contract.")]
    UInt32 = 1,
}
