// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Owns one device-generation GPU pipeline.</summary>
public sealed class GpuPipeline : IDisposable
{
    private readonly PipelineBindingLayout _bindingLayout;
    private readonly uint _deviceEpoch;
    private readonly GpuPipelineHandle _handle;
    private readonly GraphicsResourceOwner _resourceOwner;

    internal GpuPipeline(
        GpuPipelineHandle handle,
        uint deviceEpoch,
        GraphicsResourceOwner resourceOwner,
        PipelineBindingLayout bindingLayout)
    {
        ArgumentOutOfRangeException.ThrowIfZero(deviceEpoch);
        ArgumentNullException.ThrowIfNull(resourceOwner);
        ArgumentNullException.ThrowIfNull(bindingLayout);

        if (!handle.IsValid
            || (handle.OwnerId != resourceOwner.OwnerId)
            || (handle.DeviceEpoch != deviceEpoch)
            || (resourceOwner.DeviceEpoch != deviceEpoch))
        {
            throw new ArgumentException("The pipeline handle must identify this device generation.", nameof(handle));
        }

        _deviceEpoch = deviceEpoch;
        _handle = handle;
        _resourceOwner = resourceOwner;
        _bindingLayout = bindingLayout;
    }

    /// <summary>Gets the device generation.</summary>
    public uint DeviceEpoch => _deviceEpoch;

    /// <summary>Gets the engine-issued pipeline identity.</summary>
    public GpuPipelineHandle Handle => _handle;

    internal PipelineBindingLayout BindingLayout => _bindingLayout;

    internal GraphicsResourceOwner ResourceOwner => _resourceOwner;

    /// <summary>Revokes this pipeline for new work and queues its retirement on the graphics owner.</summary>
    public void Dispose()
    {
        _resourceOwner.Device.RevokePipeline(this);
        _resourceOwner.Dispose();
    }
}
