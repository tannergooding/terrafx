// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal sealed class WorldAttachmentQuarantineReservation
{
    private bool _isReleased;
    private int _remainingSlots;

    internal WorldAttachmentQuarantineReservation(int slotCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slotCount);
        _remainingSlots = slotCount;
    }

    internal void Release()
        => WorldAttachmentInstanceRegistry.ReleaseQuarantineReservation(this);

    internal int ReleaseUnusedCapacity()
    {
        if (_isReleased)
        {
            return 0;
        }

        var result = _remainingSlots;
        _remainingSlots = 0;
        _isReleased = true;
        return result;
    }

    internal void UseSlot()
    {
        if (_isReleased || (_remainingSlots <= 0))
        {
            throw new InvalidOperationException("The attachment quarantine reservation has no remaining capacity.");
        }

        _remainingSlots--;
    }
}
