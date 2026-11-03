// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldPersistenceAdmissionReservation : IDisposable
{
    private readonly long _bytes;
    private readonly Lock _disposeGate = new();
    private readonly WorldDomainReservation _domainReservation;
    private bool _isDisposed;
    private readonly World _world;

    internal WorldPersistenceAdmissionReservation(
        World world,
        WorldDomainReservation domainReservation,
        long bytes)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(domainReservation);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        _bytes = bytes;
        _domainReservation = domainReservation;
        _world = world;
    }

    public void Dispose()
    {
        lock (_disposeGate)
        {
            if (_isDisposed)
            {
                return;
            }

            _domainReservation.Dispose();
            _world.ReleasePersistenceAdmissionBytes(_bytes);
            _isDisposed = true;
        }
    }
}
