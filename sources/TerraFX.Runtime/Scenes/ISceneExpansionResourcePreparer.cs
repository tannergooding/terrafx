// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Entities;

namespace TerraFX.Scenes;

internal interface ISceneExpansionResourcePreparer
{
    Task<IDisposable?> PrepareAsync(
        SceneExpansionPlan plan,
        Func<long, WorldSceneStorageReservation?> reserveStorage,
        CancellationToken cancellationToken);
}
