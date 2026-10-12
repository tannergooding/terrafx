// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Physics;

/// <summary>Describes why a primitive was rejected by geometry-only validation.</summary>
public enum ShapeValidationFaultCode
{
    /// <summary>No validation fault.</summary>
    None = 0,

    /// <summary>A radius or box half-extent is below the configured minimum.</summary>
    PrimitiveExtentBelowMinimum = 1,

    /// <summary>A primitive exceeds the configured local-radius limit.</summary>
    LocalExtentExceeded = 2
}
