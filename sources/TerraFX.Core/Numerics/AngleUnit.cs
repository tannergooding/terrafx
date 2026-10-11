// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Numerics;

/// <summary>Identifies the unit retained by an <see cref="Angle"/> definition.</summary>
public enum AngleUnit
{
    /// <summary>Degrees, where one full turn is 360 degrees.</summary>
    Degrees = 0,

    /// <summary>Turns, where one full turn is one turn.</summary>
    Turns = 1,

    /// <summary>Radians, where one full turn is 2π radians.</summary>
    Radians = 2,
}
