// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace TerraFX.Buffers;

internal sealed class BufferCompletionAdmission
{
    private int _allocationClaimed;
    private int _finalizedOwnerClaimed;
    private readonly BufferCompletionFootprint _footprint;
    private int _nativeStorageReleased;
    private int _readLeaseClaimed;
    private int _writeLeaseClaimed;

    private BufferCompletionAdmission(BufferCompletionFootprint footprint)
    {
        _footprint = footprint;
    }

    internal BufferCompletionFootprint Footprint => _footprint;

    internal long ManagedByteCharge => checked(_footprint.TotalByteAllowance - _footprint.NativeAllocationBytes);

    internal long NativeByteCharge
        => (Volatile.Read(ref _nativeStorageReleased) == 0) ? _footprint.NativeAllocationBytes : 0;

    internal long TotalByteCharge => checked(ManagedByteCharge + NativeByteCharge);

    internal static BufferCompletionAdmission Create(nint byteLength, nint alignment, long maximumCpuBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCpuBytes);
        BufferCompletionFootprint footprint;

        try
        {
            footprint = new BufferCompletionFootprint(byteLength, alignment);

            if (!BufferCompletionFootprint.IsSupportedProfile || (footprint.TotalByteAllowance > maximumCpuBytes))
            {
                ThrowAllowanceExceeded();
            }
        }
        catch (OverflowException exception)
        {
            ThrowAllowanceExceeded(exception);
            throw;
        }

        return new BufferCompletionAdmission(footprint);
    }

    [DoesNotReturn]
    [SuppressMessage(
        "Usage",
        "CA2201:Do not raise reserved exception types",
        Justification = "An unavailable or exhausted private completion footprint is an allocation-budget refusal.")]
    private static void ThrowAllowanceExceeded(Exception? innerException = null)
        => throw new OutOfMemoryException("The Core completion storage has no qualified CPU allowance for this profile or operation.", innerException);

    internal void ClaimAllocation(nint byteLength, nint nativeAllocationBytes, nint alignment)
    {
        if ((byteLength != _footprint.ByteLength)
            || (nativeAllocationBytes != _footprint.NativeAllocationBytes)
            || (alignment != _footprint.Alignment)
            || (Interlocked.CompareExchange(ref _allocationClaimed, 1, 0) != 0))
        {
            throw new InvalidOperationException("The completion allocation does not match its one-shot Core admission.");
        }
    }

    internal void ClaimFinalizedOwner()
    {
        if (Interlocked.CompareExchange(ref _finalizedOwnerClaimed, 1, 0) != 0)
        {
            ThrowAllowanceExceeded();
        }
    }

    internal void ClaimLease(BufferAccess access, nint offset, nint length, nint ownerLength)
    {
        if ((offset != 0) || (length != ownerLength))
        {
            throw new InvalidOperationException("A completion admission permits only its exact full-range leases.");
        }

        var alreadyClaimed = (access == BufferAccess.Read)
            ? Interlocked.CompareExchange(ref _readLeaseClaimed, 1, 0)
            : Interlocked.CompareExchange(ref _writeLeaseClaimed, 1, 0);

        if (alreadyClaimed != 0)
        {
            ThrowAllowanceExceeded();
        }
    }

    internal void ReleaseNativeStorage()
        => _ = Interlocked.Exchange(ref _nativeStorageReleased, 1);

}
