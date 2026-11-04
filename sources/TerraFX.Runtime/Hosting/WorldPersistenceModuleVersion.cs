// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Hosting;

/// <summary>Identifies one application module and its positive version.</summary>
public readonly record struct WorldPersistenceModuleVersion
{
    /// <summary>Initializes one module version.</summary>
    /// <param name="moduleId">The non-empty stable module identifier.</param>
    /// <param name="version">The positive module version.</param>
    /// <exception cref="ArgumentException"><paramref name="moduleId"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is zero.</exception>
    public WorldPersistenceModuleVersion(Guid moduleId, uint version)
    {
        if (moduleId == Guid.Empty)
        {
            throw new ArgumentException("The module ID must not be empty.", nameof(moduleId));
        }

        ArgumentOutOfRangeException.ThrowIfZero(version);
        ModuleId = moduleId;
        Version = version;
    }

    /// <summary>Gets the stable module identifier.</summary>
    public Guid ModuleId { get; }

    /// <summary>Gets the positive module version.</summary>
    public uint Version { get; }
}
