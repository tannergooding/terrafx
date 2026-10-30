// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

/// <summary>Identifies the comparison used by an axis-threshold binding.</summary>
public enum AxisThresholdDirection
{
    /// <summary>Compare the negative component magnitude.</summary>
    Negative = 0,

    /// <summary>Compare the positive component magnitude.</summary>
    Positive = 1,

    /// <summary>Compare the absolute component magnitude.</summary>
    Magnitude = 2,
}
