// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Graphics;

/// <summary>Owns one device-generation GPU buffer resource.</summary>
public sealed class GpuBuffer : IDisposable
{
    private readonly GraphicsResourceAllocationInfo _allocationInfo;
    private readonly IGraphicsBackendBuffer _backendBuffer;
    private readonly long _byteLength;
    private readonly GraphicsDevice _device;
    private readonly uint _deviceEpoch;
    private readonly GpuBufferHandle _handle;
    private readonly GraphicsOwnerRequestQueue.GraphicsRetirementReservation _retirementReservation;
    // 0 is live, 1 is retirement requested, 2 is retiring, and 3 is retired or quarantined.
    private int _retirementState;

    internal GpuBuffer(
        GraphicsDevice device,
        GpuBufferHandle handle,
        long byteLength,
        uint deviceEpoch,
        GraphicsResourceAllocationInfo allocationInfo,
        IGraphicsBackendBuffer backendBuffer,
        GraphicsOwnerRequestQueue.GraphicsRetirementReservation retirementReservation)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLength);
        ArgumentOutOfRangeException.ThrowIfZero(deviceEpoch);
        ArgumentNullException.ThrowIfNull(backendBuffer);
        ArgumentNullException.ThrowIfNull(retirementReservation);

        if (!handle.IsValid
            || (handle.OwnerId != device.OwnerId)
            || (handle.DeviceEpoch != deviceEpoch)
            || (device.DeviceEpoch != deviceEpoch))
        {
            throw new ArgumentException("The buffer handle must identify this device generation.", nameof(handle));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(allocationInfo.SizeBytes, byteLength);

        _allocationInfo = allocationInfo;
        _backendBuffer = backendBuffer;
        _byteLength = byteLength;
        _device = device;
        _deviceEpoch = deviceEpoch;
        _handle = handle;
        _retirementReservation = retirementReservation;
    }

    /// <summary>Gets the logical byte length.</summary>
    public long ByteLength => _byteLength;

    /// <summary>Gets the device generation.</summary>
    public uint DeviceEpoch => _deviceEpoch;

    /// <summary>Gets the engine-issued buffer identity.</summary>
    public GpuBufferHandle Handle => _handle;

    internal GraphicsResourceAllocationInfo AllocationInfo => _allocationInfo;

    internal IGraphicsBackendBuffer BackendBuffer => _backendBuffer;

    internal GraphicsOwnerRequestQueue.GraphicsRetirementReservation RetirementReservation => _retirementReservation;

    /// <summary>Revokes this buffer for new work and queues its retirement on the graphics owner.</summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _retirementState, 1, 0) == 0)
        {
            _device.EnqueueBufferRetirement(this);
        }
    }

    internal void MarkQuarantined()
        => Volatile.Write(ref _retirementState, 3);

    internal void MarkRetired()
        => Volatile.Write(ref _retirementState, 3);

    internal bool TryBeginRetirement()
    {
        while (true)
        {
            var currentState = Volatile.Read(ref _retirementState);

            if (currentState >= 2)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _retirementState, 2, currentState) == currentState)
            {
                return true;
            }
        }
    }
}
