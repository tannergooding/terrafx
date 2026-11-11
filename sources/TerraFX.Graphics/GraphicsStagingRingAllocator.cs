// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;

namespace TerraFX.Graphics;

internal sealed class GraphicsStagingRingAllocator
{
    private long _nextOffset;
    private long _nextReservationId;
    private readonly List<ReservationState> _reservations = [];

    internal GraphicsStagingRingAllocator(long capacityBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacityBytes);
        CapacityBytes = capacityBytes;
    }

    internal long CapacityBytes { get; }

    internal long? OldestFenceValue
    {
        get
        {
            long? oldestFenceValue = null;

            foreach (var reservation in _reservations)
            {
                if ((reservation.FenceValue is long fenceValue)
                    && ((oldestFenceValue is null) || (fenceValue < oldestFenceValue.Value)))
                {
                    oldestFenceValue = fenceValue;
                }
            }

            return oldestFenceValue;
        }
    }

    private static long Align(long value, long alignment)
    {
        var remainder = value % alignment;
        return remainder == 0 ? value : checked(value + (alignment - remainder));
    }

    internal void Cancel(Reservation reservation)
    {
        var index = FindReservation(reservation);
        var state = _reservations[index];

        if (state.FenceValue is not null)
        {
            throw new InvalidOperationException("A submitted graphics staging range cannot be canceled.");
        }

        _reservations.RemoveAt(index);
    }

    internal void MarkSubmitted(Reservation reservation, long fenceValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fenceValue);
        var index = FindReservation(reservation);
        var state = _reservations[index];

        if (state.FenceValue is not null)
        {
            throw new InvalidOperationException("The graphics staging range was already submitted.");
        }

        _reservations[index] = state with { FenceValue = fenceValue };
    }

    internal void RetireCompleted(Reservation reservation, long completedFenceValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(completedFenceValue);
        var index = FindReservation(reservation);
        var state = _reservations[index];

        if ((state.FenceValue is not long fenceValue) || (fenceValue > completedFenceValue))
        {
            throw new InvalidOperationException("The graphics staging range fence has not completed.");
        }

        _reservations.RemoveAt(index);
    }

    internal void RetireCompleted(long completedFenceValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(completedFenceValue);

        for (var index = _reservations.Count - 1; index >= 0; index--)
        {
            if (_reservations[index].FenceValue is long fenceValue && (fenceValue <= completedFenceValue))
            {
                _reservations.RemoveAt(index);
            }
        }
    }

    internal bool TryReserve(long byteLength, long alignment, out Reservation reservation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(alignment);
        reservation = default;

        if (byteLength > CapacityBytes)
        {
            return false;
        }

        var offset = FindAvailableOffset(_nextOffset, CapacityBytes, byteLength, alignment)
            ?? FindAvailableOffset(0, _nextOffset, byteLength, alignment);

        if (offset is null)
        {
            return false;
        }

        if (_nextReservationId == long.MaxValue)
        {
            throw new InvalidOperationException("The graphics staging reservation identifier space is exhausted.");
        }

        reservation = new Reservation(++_nextReservationId, offset.Value, byteLength);
        _reservations.Add(new ReservationState(reservation));
        _nextOffset = checked(offset.Value + byteLength);

        if (_nextOffset == CapacityBytes)
        {
            _nextOffset = 0;
        }

        return true;
    }

    private long? FindAvailableOffset(long startOffset, long endOffset, long byteLength, long alignment)
    {
        var candidate = Align(startOffset, alignment);

        while ((candidate <= endOffset) && (byteLength <= (endOffset - candidate)))
        {
            var candidateEnd = checked(candidate + byteLength);
            long? nextCandidate = null;

            foreach (var reservation in _reservations)
            {
                var reservationEnd = checked(reservation.Reservation.Offset + reservation.Reservation.ByteLength);

                if ((candidate < reservationEnd)
                    && (reservation.Reservation.Offset < candidateEnd)
                    && ((nextCandidate is null) || (reservationEnd > nextCandidate.Value)))
                {
                    nextCandidate = reservationEnd;
                }
            }

            if (nextCandidate is null)
            {
                return candidate;
            }

            candidate = Align(nextCandidate.Value, alignment);
        }

        return null;
    }

    private int FindReservation(Reservation reservation)
    {
        if (!reservation.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(reservation));
        }

        for (var index = 0; index < _reservations.Count; index++)
        {
            if (_reservations[index].Reservation == reservation)
            {
                return index;
            }
        }

        throw new InvalidOperationException("The graphics staging reservation is no longer live.");
    }

    internal readonly struct Reservation : IEquatable<Reservation>
    {
        internal Reservation(long id, long offset, long byteLength)
        {
            Id = id;
            Offset = offset;
            ByteLength = byteLength;
        }

        internal long ByteLength { get; }

        internal long Id { get; }

        internal bool IsValid => Id > 0;

        internal long Offset { get; }

        public static bool operator !=(Reservation left, Reservation right)
            => !left.Equals(right);

        public static bool operator ==(Reservation left, Reservation right)
            => left.Equals(right);

        public readonly bool Equals(Reservation other)
            => (Id == other.Id)
                && (Offset == other.Offset)
                && (ByteLength == other.ByteLength);

        public override readonly bool Equals(object? obj)
            => (obj is Reservation other) && Equals(other);

        public override readonly int GetHashCode()
            => HashCode.Combine(Id, Offset, ByteLength);
    }

    private readonly record struct ReservationState(Reservation Reservation, long? FenceValue = null);
}
