// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal interface IWorldCpuExtractionParticipant
{
    long OwnedByteReservation { get; }

    WorldFrameReadLease AcquireFrameReadLease(IWorldSceneParticipant sceneParticipant);

    void Dispose();
}
