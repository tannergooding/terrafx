// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Scenes;

internal enum SceneActivationEnqueueStatus
{
    Queued,
    QueueFull,
    RegistryDisposed,
    PreparedNotAvailable,
    AlreadyAdmitted,
    WrongWorld,
    WrongAssetStore,
    PolicyMismatch,
}
