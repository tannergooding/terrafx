// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.ExceptionServices;
using TerraFX.Entities;

namespace TerraFX.Scenes;

internal abstract class SceneOwnerRequest
{
    internal virtual Exception? RetainedCleanupFailure => null;

    internal virtual void Admit()
    {
    }

    internal abstract void CancelFromRegistryDisposal();

    internal abstract void Execute(World world, SceneWorldRegistry registry);

    internal virtual ExceptionDispatchInfo GetCleanupDispatch(Exception exception)
        => ExceptionDispatchInfo.Capture(exception);
}
