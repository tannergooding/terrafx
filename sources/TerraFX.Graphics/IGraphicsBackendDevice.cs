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

    OwnedBuffer<byte> ReadbackBuffer(IGraphicsBackendBuffer source, long sourceOffset, nint byteLength);

    TextureReadback ReadbackTexture(IGraphicsBackendTexture source, TextureSubresource subresource);

    long UploadBuffer(
        IGraphicsBackendBuffer destination,
        long destinationOffset,
        BufferReadLease<byte> source,
        nint sourceOffset,
        nint byteLength);

    long UploadBuffer(IGraphicsBackendBuffer destination, long destinationOffset, ReadOnlySpan<byte> source);

    long UploadTexture(
        IGraphicsBackendTexture destination,
        TextureSubresource subresource,
        BufferReadLease<byte> source,
        nint sourceOffset,
        TextureDataLayout sourceLayout);
}
