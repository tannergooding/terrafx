// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX;

/// <summary>Identifies a material property that can receive an authoring default.</summary>
public enum ContentMaterialDefaultProperty
{
    /// <summary>The alpha mode.</summary>
    AlphaMode = 0,

    /// <summary>The depth-test setting.</summary>
    DepthTest = 1,

    /// <summary>The depth-write setting.</summary>
    DepthWrite = 2,

    /// <summary>The cull mode.</summary>
    CullMode = 3,

    /// <summary>The output-alpha setting.</summary>
    OutputAlpha = 4,

    /// <summary>The capability mask.</summary>
    Capabilities = 5,
}
