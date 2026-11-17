// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using TerraFX.Buffers;

namespace TerraFX.Graphics;

internal sealed class GraphicsBufferReadbackStorage : IDisposable
{
    private readonly BufferCompletionAdmission? _admission;
    private readonly GraphicsDevice _device;
    private readonly uint _deviceEpoch;
    // Prepared, request-owned, transferred to the result, or safely released.
    private int _ownershipState;
    private UnmanagedBuffer<byte>? _storage;
    private BufferWriteLease<byte>? _writeLease;

    private GraphicsBufferReadbackStorage(
        GraphicsDevice device,
        nint byteLength,
        BufferCompletionAdmission? admission)
    {
        _admission = admission;
        _device = device;
        _deviceEpoch = device.DeviceEpoch;
        ByteLength = byteLength;
    }

    internal BufferCompletionAdmission? Admission => _admission;

    internal nint ByteLength { get; }

    internal BufferWriteLease<byte> WriteLease
        => _writeLease ?? throw new InvalidOperationException("The readback has no admitted CPU write lease.");

    internal static GraphicsBufferReadbackStorage Create(
        GraphicsDevice device,
        nint byteLength,
        long? maximumCoreCpuBytes = null,
        nint alignment = 1)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLength);

        if (device.Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice is not accepting CPU readback destinations.");
        }

        var admission = maximumCoreCpuBytes.HasValue
            ? BufferCompletionAdmission.Create(byteLength, alignment, maximumCoreCpuBytes.Value)
            : null;
        var footprint = admission?.Footprint ?? new BufferCompletionFootprint(byteLength, alignment);
        var result = new GraphicsBufferReadbackStorage(device, byteLength, admission);

        try
        {
            var nativeExtent = footprint.NativeAllocationBytes;
            result._storage = UnmanagedBuffer<byte>.Allocate(
                byteLength,
                new BufferAllocationOptions(nativeExtent, alignment),
                NativeMemoryOperations.Instance,
                admission);
            result._writeLease = result._storage.AcquireWrite(0, byteLength);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _ownershipState, 3, 0) == 0)
        {
            ReleaseStorage();
        }
    }

    internal OwnedBuffer<byte> Complete()
    {
        if (Volatile.Read(ref _ownershipState) != 1)
        {
            throw new InvalidOperationException("Only the admitted request can finalize its CPU readback.");
        }

        var storage = _storage
            ?? throw new InvalidOperationException("The readback has no preallocated CPU storage.");
        _writeLease?.Dispose();
        _writeLease = null;
        var result = storage.TransferToOwnedBuffer();
        _storage = null;
        Volatile.Write(ref _ownershipState, 2);
        return result;
    }

    internal void ReleaseAfterFailure()
    {
        if (Interlocked.CompareExchange(ref _ownershipState, 3, 1) == 1)
        {
            ReleaseStorage();
        }
    }

    internal void TransferToRequest(GraphicsDevice device, nint byteLength)
    {
        if (!ReferenceEquals(device, _device)
            || (device.DeviceEpoch != _deviceEpoch)
            || (byteLength != ByteLength))
        {
            throw new ArgumentException("The completion destination belongs to another device, epoch, or byte extent.");
        }

        if (Interlocked.CompareExchange(ref _ownershipState, 1, 0) != 0)
        {
            throw new InvalidOperationException("The completion destination has already been consumed or disposed.");
        }
    }

    private void ReleaseStorage()
    {
        var writeLease = _writeLease;
        _writeLease = null;
        var storage = _storage;
        _storage = null;

        try
        {
            writeLease?.Dispose();
        }
        finally
        {
            storage?.Dispose();
        }
    }
}
