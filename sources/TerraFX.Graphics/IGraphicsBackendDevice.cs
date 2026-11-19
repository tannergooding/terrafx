// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;
using TerraFX.Buffers;

namespace TerraFX.Graphics;

internal interface IGraphicsBackendDevice : IDisposable
{
    GraphicsCapabilities Capabilities { get; }

    bool HasUncertainSubmission => false;

    IGraphicsBackendBufferReadback BeginBufferReadback(
        IGraphicsBackendBuffer source,
        GraphResourceState sourceState,
        long sourceOffset,
        nint byteLength)
        => throw new NotSupportedException("The selected backend does not support asynchronous buffer readback.");

    IGraphicsBackendBuffer CreateBuffer(
        BufferDescription description,
        IGraphicsBackendBufferAllocation allocation)
        => throw new NotSupportedException("The selected backend does not support placed transient buffers.");

    IGraphicsBackendBuffer CreateBuffer(BufferDescription description);

    IGraphicsBackendBufferAllocation CreateBufferAllocation(
        BufferDescription description,
        GraphicsResourceAllocationInfo allocationInfo)
        => throw new NotSupportedException("The selected backend does not support shared transient buffer allocations.");

    IGraphicsBackendGraphSubmissionStorage CreateGraphSubmissionStorage(GraphicsGraphSubmission submission)
        => throw new NotSupportedException("The selected backend does not support preallocated graph submission storage.");

    IGraphicsBackendPipeline CreatePipeline(
        PipelineDescription description,
        ReadOnlySpan<BufferReadLease<byte>> bytecodeLeases);

    IGraphicsBackendSampler CreateSampler(SamplerDescription description, float effectiveMaximumAnisotropy);

    IGraphicsBackendSurface CreateSurface(GraphicsNativeSurfaceBinding binding, PixelSize clientPixelSize)
        => throw new PlatformNotSupportedException("The selected backend does not support native presentation surfaces.");

    IGraphicsBackendTexture CreateTexture(
        TextureDescription description,
        IGraphicsBackendTextureAllocation allocation)
        => throw new NotSupportedException("The selected backend does not support placed transient textures.");

    IGraphicsBackendTexture CreateTexture(TextureDescription description);

    IGraphicsBackendTextureAllocation CreateTextureAllocation(
        TextureDescription description,
        GraphicsResourceAllocationInfo allocationInfo)
        => throw new NotSupportedException("The selected backend does not support shared transient texture allocations.");

    GraphicsResourceAllocationInfo GetBufferAllocationInfo(BufferDescription description);

    long GetCompletedGraphFence() => long.MaxValue;

    GraphicsResourceAllocationInfo GetTextureAllocationInfo(TextureDescription description);

    GraphicsResourceAllocationInfo GetTransientTextureAllocationInfo(TextureDescription description)
        => GetTextureAllocationInfo(description);

    TextureReadback ReadbackTexture(
        IGraphicsBackendTexture source,
        GraphResourceState sourceState,
        TextureSubresource subresource);

    long SubmitGraph(GraphicsGraphSubmission submission);

    long UploadBuffer(
        IGraphicsBackendBuffer destination,
        GraphResourceState destinationState,
        long destinationOffset,
        BufferReadLease<byte> source,
        nint sourceOffset,
        nint byteLength);

    long UploadBuffer(
        IGraphicsBackendBuffer destination,
        GraphResourceState destinationState,
        long destinationOffset,
        ReadOnlySpan<byte> source);

    long UploadTexture(
        IGraphicsBackendTexture destination,
        GraphResourceState destinationState,
        TextureSubresource subresource,
        BufferReadLease<byte> source,
        nint sourceOffset,
        TextureDataLayout sourceLayout);

    void WaitForGraphFence(long fenceValue)
    {
    }

    void WriteUploadBuffer(
        IGraphicsBackendBuffer destination,
        long destinationOffset,
        ReadOnlySpan<byte> source)
        => throw new NotSupportedException("The selected backend does not support host-written upload buffers.");
}
