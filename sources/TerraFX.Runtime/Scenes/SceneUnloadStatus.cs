// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Scenes;

/// <summary>Reports the result of one queued Scene unload.</summary>
public enum SceneUnloadStatus
{
    /// <summary>The Scene and its instance-owned package leases were retired.</summary>
    Unloaded,

    /// <summary>The Scene instance was already unloaded or revoked.</summary>
    AlreadyUnloaded,

    /// <summary>The unload request was refused without changing the World.</summary>
    Rejected,
}
