// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Scenes;

/// <summary>Defines how reparenting preserves an Entity's transform.</summary>
public enum ReparentMode
{
    /// <summary>Preserves the local pose, converting translation representation at root boundaries.</summary>
    KeepLocal,

    /// <summary>Recomputes the local pose to preserve the current world pose within the admitted residual.</summary>
    KeepWorld,
}
