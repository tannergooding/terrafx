// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Captures the headless-capable CPU extraction participant before World construction.</summary>
public sealed class CpuExtractionSupportRegistration
{
    private readonly IWorldCpuExtractionSupportRegistration _registration;

    internal CpuExtractionSupportRegistration(IWorldCpuExtractionSupportRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _registration = registration;
    }

    internal IWorldCpuExtractionParticipant CreateParticipant(
        World world,
        WorldOptions options,
        IWorldSceneParticipant sceneParticipant)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sceneParticipant);
        return _registration.CreateParticipant(world, options, sceneParticipant);
    }

    internal long GetWorldStorageByteReservation(WorldOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return _registration.GetWorldStorageByteReservation(options);
    }
}
