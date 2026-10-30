// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal sealed class SystemRegistration
{
    internal SystemRegistration(SystemDescription description, WorldSystemFactoryBinding factory)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(factory);
        Description = description;
        Factory = factory;
    }

    internal SystemDescription Description { get; }

    internal WorldSystemFactoryBinding Factory { get; }
}
