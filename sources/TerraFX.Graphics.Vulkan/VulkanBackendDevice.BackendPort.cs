// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Buffers;
using TerraFX.Graphics;

namespace TerraFX.Graphics.Vulkan;

internal sealed unsafe partial class VulkanBackendDevice
{
    IGraphicsBackendBuffer IGraphicsBackendDevice.CreateBuffer(BufferDescription description)
        => CreateBuffer(description);

    IGraphicsBackendPipeline IGraphicsBackendDevice.CreatePipeline(
        PipelineDescription description,
        ReadOnlySpan<BufferReadLease<byte>> bytecodeLeases)
        => CreatePipeline(description, bytecodeLeases);

    IGraphicsBackendSampler IGraphicsBackendDevice.CreateSampler(
        SamplerDescription description,
        float effectiveMaximumAnisotropy)
        => CreateSampler(description, effectiveMaximumAnisotropy);

    IGraphicsBackendTexture IGraphicsBackendDevice.CreateTexture(TextureDescription description)
        => CreateTexture(description);

    GraphicsResourceAllocationInfo IGraphicsBackendDevice.GetBufferAllocationInfo(BufferDescription description)
        => GetBufferAllocationInfo(description);

    GraphicsResourceAllocationInfo IGraphicsBackendDevice.GetTextureAllocationInfo(TextureDescription description)
        => GetTextureAllocationInfo(description);

    OwnedBuffer<byte> IGraphicsBackendDevice.ReadbackBuffer(
        IGraphicsBackendBuffer source,
        long sourceOffset,
        nint byteLength)
        => ReadbackBuffer(RequireBuffer(source, nameof(source)), sourceOffset, byteLength);

    TextureReadback IGraphicsBackendDevice.ReadbackTexture(
        IGraphicsBackendTexture source,
        TextureSubresource subresource)
        => ReadbackTexture(RequireTexture(source, nameof(source)), subresource);

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

    long IGraphicsBackendDevice.UploadBuffer(
        IGraphicsBackendBuffer destination,
        long destinationOffset,
        BufferReadLease<byte> source,
        nint sourceOffset,
        nint byteLength)
        => UploadBuffer(
            RequireBuffer(destination, nameof(destination)),
            destinationOffset,
            source,
            sourceOffset,
            byteLength);

    long IGraphicsBackendDevice.UploadBuffer(
        IGraphicsBackendBuffer destination,
        long destinationOffset,
        ReadOnlySpan<byte> source)
        => UploadBuffer(RequireBuffer(destination, nameof(destination)), destinationOffset, source);

    long IGraphicsBackendDevice.UploadTexture(
        IGraphicsBackendTexture destination,
        TextureSubresource subresource,
        BufferReadLease<byte> source,
        nint sourceOffset,
        TextureDataLayout sourceLayout)
        => UploadTexture(
            RequireTexture(destination, nameof(destination)),
            subresource,
            source,
            sourceOffset,
            sourceLayout);
}
