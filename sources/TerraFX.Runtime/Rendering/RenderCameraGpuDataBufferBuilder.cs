// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

internal static class RenderCameraGpuDataBufferBuilder
{
    internal static byte[] CreateFrameBuffer(
        ReadOnlySpan<CameraSnapshot> cameras,
        int vulkanAlignment,
        out int uniformStride)
    {
        uniformStride = RenderCameraGpuData.GetUniformAllocationStride(vulkanAlignment);
        var bufferByteLength = checked(cameras.Length * uniformStride);
        var buffer = new byte[bufferByteLength];

        for (var index = 0; index < cameras.Length; index++)
        {
            var camera = cameras[index];
            var area = RenderCameraViewport.CreateArea(camera.Viewport, camera.TargetExtent);
            var data = RenderCameraGpuData.Create(camera, area);
            data.WriteTo(buffer.AsSpan(checked(index * uniformStride), RenderCameraGpuData.SizeInBytes));
        }

        return buffer;
    }

    internal static byte[] CreateToneMapFrameBuffer(
        RenderArea area,
        int vulkanAlignment,
        out int uniformStride)
    {
        uniformStride = RenderCameraGpuData.GetUniformAllocationStride(vulkanAlignment);
        var buffer = new byte[uniformStride];
        RenderCameraGpuData.CreateToneMap(area).WriteTo(buffer.AsSpan(0, RenderCameraGpuData.SizeInBytes));
        return buffer;
    }
}
