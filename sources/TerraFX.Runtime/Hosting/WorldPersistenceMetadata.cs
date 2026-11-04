// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Hosting;

/// <summary>Pairs explicit application identity with the active runtime profile for State2 persistence.</summary>
public sealed class WorldPersistenceMetadata
{
    /// <summary>Initializes immutable metadata for one persistence service.</summary>
    /// <param name="application">The application identity and application-owned modules.</param>
    /// <param name="runtimeProfile">The active artifact, runtime-module, and numerics identity.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public WorldPersistenceMetadata(
        WorldPersistenceApplication application,
        WorldPersistenceRuntimeProfile runtimeProfile)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(runtimeProfile);
        var records = WorldPersistenceMetadataRecordWriter.CreateRecords(application, runtimeProfile);
        Application = application;
        RuntimeProfile = runtimeProfile;
        ApplicationRecord = records.ApplicationRecord;
        RuntimeProfileRecord = records.RuntimeProfileRecord;
    }

    /// <summary>Gets the explicit application identity.</summary>
    public WorldPersistenceApplication Application { get; }

    /// <summary>Gets the explicit active runtime profile.</summary>
    public WorldPersistenceRuntimeProfile RuntimeProfile { get; }

    internal ImmutableArray<byte> ApplicationRecord { get; }

    internal ImmutableArray<byte> RuntimeProfileRecord { get; }
}
