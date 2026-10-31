// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Captures built-in Scene component and per-World participant registration data.</summary>
public sealed class SceneSupportRegistration
{
    private readonly IWorldSceneSupportRegistration _registration;

    internal SceneSupportRegistration(IWorldSceneSupportRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _registration = registration;
    }

    internal IWorldSceneParticipant CreateParticipant(World world, WorldOptions options)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(options);
        return _registration.CreateParticipant(world, options);
    }

    internal IWorldSceneParticipant GetParticipant(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.GetSceneParticipant(this);
    }

    internal long GetWorldStorageByteReservation(WorldOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return _registration.GetWorldStorageByteReservation(options);
    }

    internal void RegisterComponents(WorldRegistrationBuilder builder, CompiledContentSchemaRegistry schemas)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(schemas);
        _registration.RegisterComponents(builder, schemas);
    }
}
