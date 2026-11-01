// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Rendering;

/// <summary>Identifies the result of one CPU render extraction.</summary>
public enum RenderExtractionStatus
{
    /// <summary>A complete immutable snapshot was extracted.</summary>
    Extracted,

    /// <summary>A declared snapshot capacity was exceeded and no partial snapshot was returned.</summary>
    CapacityExceeded,
}
