// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX;

internal sealed class WorldDiagnosticAttributionAccount
{
    internal const long ControlByteCharge = 128;

    private readonly Guid _executionSessionId;
    private readonly long _maximumBytes;
    private long _reservedBytes;
    private readonly Guid _worldGuid;
    private long _worldOwnerId;

    internal WorldDiagnosticAttributionAccount(
        Guid worldGuid,
        Guid executionSessionId,
        long maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(worldGuid, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(executionSessionId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, ControlByteCharge);

        _worldGuid = worldGuid;
        _executionSessionId = executionSessionId;
        _maximumBytes = maximumBytes;
    }

    internal Guid ExecutionSessionId => _executionSessionId;

    internal long MaximumRetainedBytes => _maximumBytes - ControlByteCharge;

    internal long ReservedBytes => Interlocked.Read(ref _reservedBytes);

    internal Guid WorldGuid => _worldGuid;

    internal uint WorldOwnerId => checked((uint)Volatile.Read(ref _worldOwnerId));

    internal void BindOwner(uint worldOwnerId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(worldOwnerId);

        if (Interlocked.CompareExchange(ref _worldOwnerId, worldOwnerId, 0) != 0)
        {
            throw new InvalidOperationException("The diagnostic attribution account is already bound to a World owner.");
        }
    }

    internal void Release(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        var observed = Interlocked.Read(ref _reservedBytes);

        while (true)
        {
            if (bytes > observed)
            {
                throw new InvalidOperationException("The diagnostic attribution release exceeds its retained reservation.");
            }

            var replacement = observed - bytes;
            var previous = Interlocked.CompareExchange(ref _reservedBytes, replacement, observed);

            if (previous == observed)
            {
                return;
            }

            observed = previous;
        }
    }

    internal bool TryReserve(long bytes, out DiagnosticAttributionReservation reservation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

        if (Volatile.Read(ref _worldOwnerId) == 0)
        {
            throw new InvalidOperationException("The diagnostic attribution account is not bound to a World owner.");
        }

        var observed = Interlocked.Read(ref _reservedBytes);

        while (true)
        {
            if (bytes > (MaximumRetainedBytes - observed))
            {
                reservation = default;
                return false;
            }

            var replacement = checked(observed + bytes);
            var previous = Interlocked.CompareExchange(ref _reservedBytes, replacement, observed);

            if (previous == observed)
            {
                reservation = new DiagnosticAttributionReservation(this, bytes);
                return true;
            }

            observed = previous;
        }
    }
}
