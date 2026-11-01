// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Buffers;

namespace TerraFX.Graphics;

internal interface IGraphicsBackendDevice : IDisposable
{
    GraphicsCapabilities Capabilities { get; }

    IGraphicsBackendBuffer CreateBuffer(BufferDescription description);

    IGraphicsBackendPipeline CreatePipeline(
        PipelineDescription description,
        ReadOnlySpan<BufferReadLease<byte>> bytecodeLeases);

    IGraphicsBackendTexture CreateTexture(TextureDescription description);

    GraphicsResourceAllocationInfo GetBufferAllocationInfo(BufferDescription description);

    GraphicsResourceAllocationInfo GetTextureAllocationInfo(TextureDescription description);
}
