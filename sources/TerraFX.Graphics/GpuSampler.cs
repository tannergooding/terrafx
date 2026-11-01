// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Owns one device-generation GPU sampler descriptor.</summary>
public sealed class GpuSampler : IDisposable
{
    private readonly uint _deviceEpoch;
    private readonly GpuSamplerHandle _handle;
    private readonly GraphicsResourceOwner _resourceOwner;

    internal GpuSampler(GpuSamplerHandle handle, uint deviceEpoch, GraphicsResourceOwner resourceOwner)
    {
        ArgumentOutOfRangeException.ThrowIfZero(deviceEpoch);
        ArgumentNullException.ThrowIfNull(resourceOwner);

        if (!handle.IsValid
            || (handle.OwnerId != resourceOwner.OwnerId)
            || (handle.DeviceEpoch != deviceEpoch)
            || (resourceOwner.DeviceEpoch != deviceEpoch))
        {
            throw new ArgumentException("The sampler handle must identify this device generation.", nameof(handle));
        }

        _deviceEpoch = deviceEpoch;
        _handle = handle;
        _resourceOwner = resourceOwner;
    }

    /// <summary>Gets the device generation.</summary>
    public uint DeviceEpoch => _deviceEpoch;

    /// <summary>Gets the engine-issued sampler identity.</summary>
    public GpuSamplerHandle Handle => _handle;

    internal GraphicsResourceOwner ResourceOwner => _resourceOwner;

    /// <summary>Revokes this sampler for new work and queues its retirement on the graphics owner.</summary>
    public void Dispose()
        => _resourceOwner.Dispose();
}
