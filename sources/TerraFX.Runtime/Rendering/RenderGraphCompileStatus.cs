// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Rendering;

/// <summary>Identifies whether graph compilation produced a plan.</summary>
public enum RenderGraphCompileStatus
{
    /// <summary>Compilation produced a plan with no rejection diagnostics.</summary>
    Compiled = 0,

    /// <summary>Compilation was rejected and produced diagnostics instead of a plan.</summary>
    Rejected = 1,
}
