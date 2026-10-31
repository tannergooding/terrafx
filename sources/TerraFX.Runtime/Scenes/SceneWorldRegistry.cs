// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using TerraFX.Entities;

namespace TerraFX.Scenes;

internal sealed class SceneWorldRegistry : IWorldSceneParticipant
{
    private const long StorageByteReservation = 256;

    private int _isDisposed;
    private readonly int _maximumActiveSceneInstances;
    private readonly int _maximumPendingOwnerRequests;
    private readonly int _maximumSceneOccurrences;
    private readonly World _world;

    internal SceneWorldRegistry(World world, WorldOptions options)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(options);
        _world = world;
        _maximumActiveSceneInstances = options.MaximumActiveSceneInstances;
        _maximumPendingOwnerRequests = options.MaximumPendingOwnerRequests;
        _maximumSceneOccurrences = options.MaximumSceneOccurrences;
    }

    public long OwnedByteReservation => StorageByteReservation;

    internal int MaximumActiveSceneInstances => _maximumActiveSceneInstances;

    internal int MaximumPendingOwnerRequests => _maximumPendingOwnerRequests;

    internal int MaximumSceneOccurrences => _maximumSceneOccurrences;

    internal static long GetWorldStorageByteReservation(WorldOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return StorageByteReservation;
    }

    public void Dispose()
        => _ = Interlocked.Exchange(ref _isDisposed, 1);

    internal void EnsureBoundTo(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

        if (!ReferenceEquals(world, _world))
        {
            throw new ArgumentException("The Scene registry belongs to another exact World binding.", nameof(world));
        }
    }
}
