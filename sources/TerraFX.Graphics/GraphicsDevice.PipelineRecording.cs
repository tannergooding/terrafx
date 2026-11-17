// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Concurrent;

namespace TerraFX.Graphics;

public sealed partial class GraphicsDevice
{
    private readonly ConcurrentDictionary<GpuPipelineHandle, GpuPipeline> _pipelinesByHandle = new();

    internal (GpuPipeline Pipeline, PipelineBindingLayout Layout, GraphicsResourceOwner.UseLease UseLease) AcquirePipelineForRecording(
        GpuPipelineHandle pipelineHandle,
        GraphicsRecordingUseAdmission? recordingAdmission = null)
    {
        if (!pipelineHandle.IsValid
            || (pipelineHandle.OwnerId != _ownerId)
            || (pipelineHandle.DeviceEpoch != DeviceEpoch))
        {
            throw new ArgumentException("The pipeline handle does not identify this GraphicsDevice generation.", nameof(pipelineHandle));
        }

        if (!IsOwnerThread)
        {
            throw new InvalidOperationException("Pipeline recording access is available only on the graphics owner thread.");
        }

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice is not accepting new recording work.");
        }

        if (!_pipelinesByHandle.TryGetValue(pipelineHandle, out var pipeline))
        {
            throw new ObjectDisposedException(nameof(GpuPipeline), "The pipeline handle is not live for new recording work.");
        }

        var useLease = pipeline.ResourceOwner.AcquireUse(nameof(pipelineHandle), recordingAdmission);
        return (pipeline, pipeline.BindingLayout, useLease);
    }

    internal void RevokePipeline(GpuPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        if (_pipelinesByHandle.TryGetValue(pipeline.Handle, out var registeredPipeline)
            && ReferenceEquals(registeredPipeline, pipeline))
        {
            _ = _pipelinesByHandle.TryRemove(pipeline.Handle, out _);
        }
    }
}
