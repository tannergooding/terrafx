// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Physics;

/// <summary>Reports whether primitive geometry passed admission.</summary>
public enum ShapeValidationStatus
{
    /// <summary>No validation result.</summary>
    None = 0,

    /// <summary>The geometry passed the configured bounds.</summary>
    Valid = 1,

    /// <summary>The geometry violated a configured bound.</summary>
    Rejected = 2
}
