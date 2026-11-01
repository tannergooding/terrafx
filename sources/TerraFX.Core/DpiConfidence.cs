// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX;

/// <summary>Defines how a window's DPI conversion value was obtained.</summary>
public enum DpiConfidence
{
    /// <summary>No confidence value is assigned; this is not valid in a metrics snapshot.</summary>
    None = 0,

    /// <summary>The platform reported a conversion value; this does not assert physical monitor measurement.</summary>
    Reported = 1,

    /// <summary>The conversion uses the assumed 96-DPI baseline because no source was available.</summary>
    Assumed96Dpi = 2,
}
