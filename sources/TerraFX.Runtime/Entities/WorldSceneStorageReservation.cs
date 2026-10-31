// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldSceneStorageReservation : IDisposable
{
    private readonly long _bytes;
    private World? _world;

    internal WorldSceneStorageReservation(World world, long bytes)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        _world = world;
        _bytes = bytes;
    }

    internal long Bytes => _bytes;

    public void Dispose()
        => Interlocked.Exchange(ref _world, null)?.ReleaseSceneStorageReservation(_bytes);
}
