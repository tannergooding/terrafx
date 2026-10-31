// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Scenes;

/// <summary>Reports the result of one queued Scene activation.</summary>
public enum SceneActivationStatus
{
    /// <summary>The Scene and all of its expanded entities were committed atomically.</summary>
    Activated,

    /// <summary>The Scene was refused without publishing an instance or consuming identities.</summary>
    Rejected,
}
