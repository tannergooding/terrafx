// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal sealed class WorldSystemQuarantineReservation
{
    private bool _domainReservationRetained;
    private bool _domainReservationSlotReserved;
    private bool _isReleased;
    private int _remainingSlots;

    internal WorldSystemQuarantineReservation(int slotCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slotCount);
        _remainingSlots = slotCount;
        _domainReservationSlotReserved = slotCount != 0;
    }

    internal bool RetainsDomainReservation => _domainReservationRetained;

    internal void Release()
        => WorldSystemInstanceRegistry.ReleaseQuarantineReservation(this);

    internal (int Slots, bool DomainReservationSlot) ReleaseUnusedCapacity()
    {
        if (_isReleased)
        {
            return (0, false);
        }

        var result = (_remainingSlots, _domainReservationSlotReserved);
        _remainingSlots = 0;
        _domainReservationSlotReserved = false;
        _isReleased = true;
        return result;
    }

    internal bool RetainDomainReservation()
    {
        if (_isReleased)
        {
            throw new InvalidOperationException("The system quarantine reservation has already been released.");
        }

        if (_domainReservationRetained)
        {
            return false;
        }

        if (!_domainReservationSlotReserved)
        {
            throw new InvalidOperationException("The system quarantine reservation has no domain-retention capacity.");
        }

        _domainReservationSlotReserved = false;
        _domainReservationRetained = true;
        return true;
    }

    internal void UseSlot()
    {
        if (_isReleased || (_remainingSlots <= 0))
        {
            throw new InvalidOperationException("The system quarantine reservation has no remaining instance capacity.");
        }

        _remainingSlots--;
    }
}
