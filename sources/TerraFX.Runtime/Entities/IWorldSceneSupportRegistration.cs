// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal interface IWorldSceneSupportRegistration
{
    IWorldSceneParticipant CreateParticipant(World world, WorldOptions options);

    long GetWorldStorageByteReservation(WorldOptions options);

    void RegisterComponents(WorldRegistrationBuilder builder, CompiledContentSchemaRegistry schemas);
}
