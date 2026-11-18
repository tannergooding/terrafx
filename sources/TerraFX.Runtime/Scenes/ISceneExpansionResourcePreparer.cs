// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Entities;

namespace TerraFX.Scenes;

internal interface ISceneExpansionResourcePreparer
{
    Task<long> MeasureStorageByteReservationAsync(
        SceneExpansionPlan plan,
        CancellationToken cancellationToken);

    Task<IDisposable?> PrepareAsync(
        SceneExpansionPlan plan,
        Func<long, WorldSceneStorageReservation?> reserveStorage,
        Func<long, WorldSceneStorageReservation?> reserveWorkspace,
        CancellationToken cancellationToken);

    Task<IDisposable?> PrepareOwnedAsync(
        SceneExpansionPlan plan,
        ScenePreparationFunding funding,
        CancellationToken cancellationToken);
}
