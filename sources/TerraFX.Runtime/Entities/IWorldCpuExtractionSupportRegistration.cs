// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal interface IWorldCpuExtractionSupportRegistration
{
    IWorldCpuExtractionParticipant CreateParticipant(
        World world,
        WorldOptions options,
        IWorldSceneParticipant sceneParticipant);

    long GetWorldStorageByteReservation(WorldOptions options);
}
