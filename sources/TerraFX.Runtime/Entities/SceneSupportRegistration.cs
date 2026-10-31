// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Captures built-in Scene component registration data for one World registration.</summary>
public sealed class SceneSupportRegistration
{
    private readonly IWorldSceneSupportRegistration _registration;

    internal SceneSupportRegistration(IWorldSceneSupportRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _registration = registration;
    }

    internal void RegisterComponents(WorldRegistrationBuilder builder, CompiledContentSchemaRegistry schemas)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(schemas);
        _registration.RegisterComponents(builder, schemas);
    }
}
