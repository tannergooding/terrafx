// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Rendering;

/// <summary>Identifies the bounded render snapshot capacity that was exceeded.</summary>
public enum RenderExtractionLimitKind
{
    /// <summary>The draw-instance capacity.</summary>
    DrawInstances,

    /// <summary>The unique-light capacity.</summary>
    Lights,

    /// <summary>The bone-palette-entry capacity.</summary>
    BonePaletteEntries,
}
