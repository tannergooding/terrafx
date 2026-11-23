// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Entities;

namespace TerraFX.Scenes;

internal readonly struct SceneExpansionReservationSource
{
    private const byte Prepared = 2;
    private const byte Restored = 3;
    private const byte World = 1;

    private readonly byte _kind;
    private readonly SceneWorldRegistry? _registry;
    private readonly WorldGuidUseControl? _useControl;
    private readonly TerraFX.Entities.World? _world;

    private SceneExpansionReservationSource(
        TerraFX.Entities.World world,
        SceneWorldRegistry registry,
        WorldGuidUseControl? useControl,
        byte kind)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(registry);

        if ((kind is Prepared) != (useControl is not null)
            || (kind is not (World or Prepared or Restored)))
        {
            throw new ArgumentException("The Scene reservation source is inconsistent.", nameof(useControl));
        }

        _kind = kind;
        _registry = registry;
        _useControl = useControl;
        _world = world;
    }

    internal bool HasStorageReservation => _kind is World or Prepared or Restored;

    internal bool HasWorkspaceReservation => _kind is Prepared;

    private TerraFX.Entities.World OwnerWorld
        => _world ?? throw new InvalidOperationException("The Scene expansion lost its reservation World.");

    private WorldGuidUseControl PreparationUseControl
        => _useControl ?? throw new InvalidOperationException("The prepared Scene expansion lost its World-use control.");

    private SceneWorldRegistry SceneRegistry
        => _registry ?? throw new InvalidOperationException("The Scene expansion lost its reservation registry.");

    internal static SceneExpansionReservationSource ForWorld(
        TerraFX.Entities.World world,
        SceneWorldRegistry registry,
        WorldGuidUseControl? useControl,
        bool useRestoredStorage)
    {
        if (useRestoredStorage && (useControl is not null))
        {
            throw new ArgumentException("Restored Scene storage cannot use a preparation control.", nameof(useControl));
        }

        return useRestoredStorage
            ? new SceneExpansionReservationSource(world, registry, null, Restored)
            : new SceneExpansionReservationSource(
                world,
                registry,
                useControl,
                (useControl is null) ? World : Prepared);
    }

    internal WorldSceneStorageReservation? ReserveStorage(long bytes)
        => _kind switch {
            World => OwnerWorld.TryReserveSceneStorage(SceneRegistry, bytes, null),
            Prepared => OwnerWorld.TryReserveSceneStorage(SceneRegistry, bytes, PreparationUseControl),
            Restored => OwnerWorld.TryReserveRestoredSceneStorage(SceneRegistry, bytes),
            _ => throw new InvalidOperationException("The Scene expansion has no storage reservation source."),
        };

    internal WorldSceneStorageReservation? ReserveWorkspace(long bytes)
        => (_kind is Prepared)
            ? OwnerWorld.TryReserveSceneWorkspace(SceneRegistry, PreparationUseControl, bytes)
            : throw new InvalidOperationException("The Scene expansion has no workspace reservation source.");
}
