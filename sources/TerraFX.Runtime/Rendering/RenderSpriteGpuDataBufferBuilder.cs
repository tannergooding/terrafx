// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Rendering;

internal static class RenderSpriteGpuDataBufferBuilder
{
    internal static byte[] CreateObjectBuffer(
        ReadOnlySpan<RenderSpriteSnapshot> sprites,
        int vulkanAlignment,
        out int uniformStride)
    {
        uniformStride = RenderSpriteGpuData.GetUniformAllocationStride(vulkanAlignment);
        var bufferByteLength = checked(sprites.Length * uniformStride);
        var buffer = new byte[bufferByteLength];

        for (var index = 0; index < sprites.Length; index++)
        {
            var data = RenderSpriteGpuData.Create(sprites[index]);
            data.WriteTo(buffer.AsSpan(checked(index * uniformStride), RenderSpriteGpuData.SizeInBytes));
        }

        return buffer;
    }
}
