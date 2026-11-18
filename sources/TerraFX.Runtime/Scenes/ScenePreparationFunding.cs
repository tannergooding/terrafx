// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Entities;

namespace TerraFX.Scenes;

internal readonly struct ScenePreparationFunding
{
    private readonly IWorldSceneParticipant? _participant;
    private readonly PreparedScene? _preparation;

    internal ScenePreparationFunding(PreparedScene preparation, IWorldSceneParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(participant);
        _preparation = preparation;
        _participant = participant;
    }

    internal bool IsInitialized => (_preparation is not null) && (_participant is not null);

    internal PreparedScene Preparation
        => _preparation ?? throw new InvalidOperationException("The Scene preparation funding value is uninitialized.");

    internal bool TryClaimWorkspace(long bytes)
        => Preparation.TryClaimProducerWorkspace(bytes);

    internal WorldSceneStorageReservation? TryReserveStorage(
        IDisposable expectedOwner,
        long dataBytes,
        long retainedDataClaimBytes)
    {
        var preparation = Preparation;
        var participant = _participant
            ?? throw new InvalidOperationException("The Scene preparation funding value is uninitialized.");
        return preparation.TryReserveProducerStorage(
            expectedOwner,
            participant,
            dataBytes,
            retainedDataClaimBytes);
    }

    internal bool TryReturnWorkspaceClaim(long bytes)
        => Preparation.TryReturnProducerWorkspaceClaim(bytes);
}
