// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Buffers;
using TerraFX.Graphics;

namespace TerraFX.Graphics.Vulkan;

internal sealed unsafe partial class VulkanBackendDevice
{
    bool IGraphicsBackendDevice.HasUncertainSubmission => _hasUncertainSubmission;

    IGraphicsBackendBuffer IGraphicsBackendDevice.CreateBuffer(
        BufferDescription description,
        IGraphicsBackendBufferAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(allocation);

        if (allocation is not VulkanBackendBufferAllocation selectedAllocation)
        {
            throw new ArgumentException("The transient buffer allocation belongs to a different Vulkan backend.", nameof(allocation));
        }

        return CreateBuffer(description, selectedAllocation);
    }

    IGraphicsBackendBuffer IGraphicsBackendDevice.CreateBuffer(BufferDescription description)
        => CreateBuffer(description);

    IGraphicsBackendBufferAllocation IGraphicsBackendDevice.CreateBufferAllocation(
        BufferDescription description,
        GraphicsResourceAllocationInfo allocationInfo)
        => CreateBufferAllocation(description, allocationInfo);

    IGraphicsBackendPipeline IGraphicsBackendDevice.CreatePipeline(
        PipelineDescription description,
        ReadOnlySpan<BufferReadLease<byte>> bytecodeLeases)
        => CreatePipeline(description, bytecodeLeases);

    IGraphicsBackendSampler IGraphicsBackendDevice.CreateSampler(
        SamplerDescription description,
        float effectiveMaximumAnisotropy)
        => CreateSampler(description, effectiveMaximumAnisotropy);

    IGraphicsBackendTexture IGraphicsBackendDevice.CreateTexture(
        TextureDescription description,
        IGraphicsBackendTextureAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(allocation);

        if (allocation is not VulkanBackendTextureAllocation selectedAllocation)
        {
            throw new ArgumentException("The transient texture allocation belongs to a different Vulkan backend.", nameof(allocation));
        }

        return CreateTexture(description, selectedAllocation);
    }

    IGraphicsBackendTexture IGraphicsBackendDevice.CreateTexture(TextureDescription description)
        => CreateTexture(description);

    IGraphicsBackendTextureAllocation IGraphicsBackendDevice.CreateTextureAllocation(
        TextureDescription description,
        GraphicsResourceAllocationInfo allocationInfo)
        => CreateTextureAllocation(description, allocationInfo);

    GraphicsResourceAllocationInfo IGraphicsBackendDevice.GetBufferAllocationInfo(BufferDescription description)
        => GetBufferAllocationInfo(description);

    GraphicsResourceAllocationInfo IGraphicsBackendDevice.GetTextureAllocationInfo(TextureDescription description)
        => GetTextureAllocationInfo(description);

    GraphicsResourceAllocationInfo IGraphicsBackendDevice.GetTransientTextureAllocationInfo(TextureDescription description)
        => GetTransientTextureAllocationInfo(description);

    OwnedBuffer<byte> IGraphicsBackendDevice.ReadbackBuffer(
        IGraphicsBackendBuffer source,
        GraphResourceState sourceState,
        long sourceOffset,
        nint byteLength)
    {
        _ = sourceState;
        return ReadbackBuffer(RequireBuffer(source, nameof(source)), sourceOffset, byteLength);
    }

    TextureReadback IGraphicsBackendDevice.ReadbackTexture(
        IGraphicsBackendTexture source,
        GraphResourceState sourceState,
        TextureSubresource subresource)
    {
        _ = sourceState;
        return ReadbackTexture(RequireTexture(source, nameof(source)), subresource);
    }

    private VulkanBackendBuffer RequireBuffer(IGraphicsBackendBuffer resource, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(resource, parameterName);

        if (resource is not VulkanBackendBuffer buffer || !ReferenceEquals(buffer.Owner, this))
        {
            throw new ArgumentException("The buffer belongs to a different Vulkan device.", parameterName);
        }

        return buffer;
    }

    private VulkanBackendTexture RequireTexture(IGraphicsBackendTexture resource, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(resource, parameterName);

        if (resource is not VulkanBackendTexture texture || !ReferenceEquals(texture.Owner, this))
        {
            throw new ArgumentException("The texture belongs to a different Vulkan device.", parameterName);
        }

        return texture;
    }

    long IGraphicsBackendDevice.SubmitGraph(GraphicsGraphSubmission submission)
        => SubmitGraph(submission);

    long IGraphicsBackendDevice.UploadBuffer(
        IGraphicsBackendBuffer destination,
        GraphResourceState destinationState,
        long destinationOffset,
        BufferReadLease<byte> source,
        nint sourceOffset,
        nint byteLength)
    {
        _ = destinationState;
        return UploadBuffer(
            RequireBuffer(destination, nameof(destination)),
            destinationOffset,
            source,
            sourceOffset,
            byteLength);
    }

    long IGraphicsBackendDevice.UploadBuffer(
        IGraphicsBackendBuffer destination,
        GraphResourceState destinationState,
        long destinationOffset,
        ReadOnlySpan<byte> source)
    {
        _ = destinationState;
        return UploadBuffer(RequireBuffer(destination, nameof(destination)), destinationOffset, source);
    }

    long IGraphicsBackendDevice.UploadTexture(
        IGraphicsBackendTexture destination,
        GraphResourceState destinationState,
        TextureSubresource subresource,
        BufferReadLease<byte> source,
        nint sourceOffset,
        TextureDataLayout sourceLayout)
    {
        _ = destinationState;
        return UploadTexture(
            RequireTexture(destination, nameof(destination)),
            subresource,
            source,
            sourceOffset,
            sourceLayout);
    }
}
