// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Owns one device-generation GPU texture resource.</summary>
public sealed class GpuTexture : IDisposable
{
    private readonly TextureDescription _description;
    private readonly uint _deviceEpoch;
    private readonly GpuTextureHandle _handle;
    private readonly GraphicsResourceOwner _resourceOwner;

    internal GpuTexture(
        GpuTextureHandle handle,
        TextureDescription description,
        uint deviceEpoch,
        GraphicsResourceOwner resourceOwner)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentOutOfRangeException.ThrowIfZero(deviceEpoch);
        ArgumentNullException.ThrowIfNull(resourceOwner);

        if (!handle.IsValid
            || (handle.OwnerId != resourceOwner.OwnerId)
            || (handle.DeviceEpoch != deviceEpoch)
            || (resourceOwner.DeviceEpoch != deviceEpoch))
        {
            throw new ArgumentException("The texture handle must identify this device generation.", nameof(handle));
        }

        _description = description;
        _deviceEpoch = deviceEpoch;
        _handle = handle;
        _resourceOwner = resourceOwner;
    }

    /// <summary>Gets the immutable texture description.</summary>
    public TextureDescription Description => _description;

    /// <summary>Gets the device generation.</summary>
    public uint DeviceEpoch => _deviceEpoch;

    /// <summary>Gets the engine-issued texture identity.</summary>
    public GpuTextureHandle Handle => _handle;

    internal GraphicsResourceOwner ResourceOwner => _resourceOwner;

    /// <summary>Revokes this texture for new work and queues its retirement on the graphics owner.</summary>
    public void Dispose()
        => _resourceOwner.Dispose();
}
