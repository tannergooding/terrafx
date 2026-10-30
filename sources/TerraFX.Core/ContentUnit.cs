// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX;

/// <summary>Identifies the unit annotation of a content field.</summary>
public enum ContentUnit
{
    /// <summary>No unit is assigned.</summary>
    None = 0,

    /// <summary>Engine distance units.</summary>
    EngineDistance = 1,

    /// <summary>Meters.</summary>
    Meters = 2,

    /// <summary>Seconds.</summary>
    Seconds = 3,

    /// <summary>Radians.</summary>
    Radians = 4,

    /// <summary>Linear color.</summary>
    LinearColor = 5,

    /// <summary>Square meters.</summary>
    SquareMeters = 6,

    /// <summary>Cubic meters.</summary>
    CubicMeters = 7,

    /// <summary>TimeSpan ticks.</summary>
    TimeSpanTicks = 8,

    /// <summary>Engine area units.</summary>
    EngineArea = 9,

    /// <summary>Engine volume units.</summary>
    EngineVolume = 10,

    /// <summary>Lux.</summary>
    Lux = 11,

    /// <summary>Lumens.</summary>
    Lumens = 12,
}
