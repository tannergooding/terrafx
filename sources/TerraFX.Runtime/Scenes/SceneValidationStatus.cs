// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Scenes;

/// <summary>Identifies whether a SceneTransaction preview is valid.</summary>
public enum SceneValidationStatus
{
    /// <summary>All staged operations are currently valid.</summary>
    Valid,

    /// <summary>At least one staged operation is invalid in the current World state.</summary>
    Rejected,
}
