// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Owns one device-generation GPU buffer resource.</summary>
public sealed class GpuBuffer : IDisposable
{
    private readonly long _byteLength;
    private readonly uint _deviceEpoch;
    private readonly GpuBufferHandle _handle;
    private readonly GraphicsResourceOwner _resourceOwner;

    internal GpuBuffer(
        GpuBufferHandle handle,
        long byteLength,
        uint deviceEpoch,
        GraphicsResourceOwner resourceOwner)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLength);
        ArgumentOutOfRangeException.ThrowIfZero(deviceEpoch);
        ArgumentNullException.ThrowIfNull(resourceOwner);

        if (!handle.IsValid
            || (handle.OwnerId != resourceOwner.OwnerId)
            || (handle.DeviceEpoch != deviceEpoch)
            || (resourceOwner.DeviceEpoch != deviceEpoch))
        {
            throw new ArgumentException("The buffer handle must identify this device generation.", nameof(handle));
        }

        _byteLength = byteLength;
        _deviceEpoch = deviceEpoch;
        _handle = handle;
        _resourceOwner = resourceOwner;
    }

    /// <summary>Gets the logical byte length.</summary>
    public long ByteLength => _byteLength;

    /// <summary>Gets the device generation.</summary>
    public uint DeviceEpoch => _deviceEpoch;

    /// <summary>Gets the engine-issued buffer identity.</summary>
    public GpuBufferHandle Handle => _handle;

    internal GraphicsResourceOwner ResourceOwner => _resourceOwner;

    /// <summary>Revokes this buffer for new work and queues its retirement on the graphics owner.</summary>
    public void Dispose()
        => _resourceOwner.Dispose();
}
