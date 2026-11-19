// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldResourceOperationReservation : IDisposable
{
    private readonly long _bytes;
    private readonly bool _ownsDomainCredit;
    private World? _world;

    internal WorldResourceOperationReservation(World world, long bytes, bool ownsDomainCredit = true)
    {
        _world = world;
        _bytes = bytes;
        _ownsDomainCredit = ownsDomainCredit;
    }

    internal long Bytes => _bytes;

    public void Dispose()
    {
        var bytes = _bytes;
        var ownsDomainCredit = _ownsDomainCredit;
        var world = Interlocked.Exchange(ref _world, null);

        if (world is not null)
        {
            if (ownsDomainCredit)
            {
                world.ReleaseResourceOperationBytes(bytes);
            }
            else
            {
                world.ReleasePreparedRestoreSourceBytes(bytes);
            }

            world.CompleteOperationReturnPart();
        }
    }

    internal bool IsBoundTo(World world)
        => ReferenceEquals(_world, world);
}
