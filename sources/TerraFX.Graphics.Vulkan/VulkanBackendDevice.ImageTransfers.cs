// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using TerraFX.Buffers;
using TerraFX.Graphics;
using TerraFX.Interop.Vulkan;
using VulkanApi = TerraFX.Interop.Vulkan.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal sealed unsafe partial class VulkanBackendDevice
{
    private static VkBufferImageCopy CreateBufferImageCopy(
        TextureSubresource subresource,
        (int Width, int Height, int Depth) extent)
    {
        var copy = default(VkBufferImageCopy);
        copy.imageSubresource.aspectMask = subresource.Aspect == TextureAspect.Depth
            ? VkImageAspectFlags.VK_IMAGE_ASPECT_DEPTH_BIT
            : VkImageAspectFlags.VK_IMAGE_ASPECT_COLOR_BIT;
        copy.imageSubresource.mipLevel = checked((uint)subresource.MipLevel);
        copy.imageSubresource.baseArrayLayer = checked((uint)subresource.ArrayLayer);
        copy.imageSubresource.layerCount = 1;
        copy.imageExtent.width = checked((uint)extent.Width);
        copy.imageExtent.height = checked((uint)extent.Height);
        copy.imageExtent.depth = checked((uint)extent.Depth);
        return copy;
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Staging memory is intentionally retained when transfer completion is uncertain.")]
    internal TextureReadback ReadbackTexture(VulkanBackendTexture source, TextureSubresource subresource)
    {
        ValidateTransferTexture(source, TextureUsage.CopySource, subresource, nameof(source));
        var extent = GraphicsDevice.GetTextureSubresourceExtent(source.Description, subresource);

        if (!source.IsInitialized(subresource))
        {
            throw new InvalidOperationException("Vulkan texture readback cannot expose an uninitialized subresource.");
        }

        var rowPitch = checked((nint)extent.Width * TextureReadback.GetBytesPerTexel(source.Description.Format));
        var slicePitch = checked(rowPitch * extent.Height);
        var byteLength = checked(slicePitch * extent.Depth);
        var staging = CreateBuffer(new BufferDescription(
            checked((long)byteLength),
            BufferUsage.CopyDestination,
            GpuMemoryKind.Readback,
            structuredStride: null,
            debugName: "Vulkan texture readback staging"));

        try
        {
            BeginOneTimeCommands();
            var oldLayout = source.GetLayout(subresource);
            RecordImageBarrier(
                source,
                subresource,
                oldLayout,
                VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL);
            var copy = CreateBufferImageCopy(subresource, extent);
            VulkanApi.vkCmdCopyImageToBuffer(
                _commandBuffer,
                source.Image,
                VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                staging.Buffer,
                1,
                &copy);
            RecordImageBarrier(
                source,
                subresource,
                VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                VkImageLayout.VK_IMAGE_LAYOUT_GENERAL);
            RecordBufferBarrier(
                staging,
                0,
                byteLength,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT,
                VkAccessFlags2.VK_ACCESS_2_TRANSFER_WRITE_BIT,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_HOST_BIT,
                VkAccessFlags2.VK_ACCESS_2_HOST_READ_BIT);
            _ = SubmitAndWait(source);
            source.SetLayout(subresource, VkImageLayout.VK_IMAGE_LAYOUT_GENERAL);
            var storage = ReadMappedBuffer(staging, byteLength);

            try
            {
                return new TextureReadback(
                    source.Description.Format,
                    subresource,
                    extent.Width,
                    extent.Height,
                    extent.Depth,
                    rowPitch,
                    slicePitch,
                    storage);
            }
            catch
            {
                storage.Dispose();
                throw;
            }
        }
        finally
        {
            if (_hasUncertainSubmission)
            {
                RetainUncertainBuffer(staging);
            }
            else
            {
                staging.Dispose();
            }
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Staging memory is intentionally retained when transfer completion is uncertain.")]
    internal long UploadTexture(
        VulkanBackendTexture destination,
        TextureSubresource subresource,
        BufferReadLease<byte> source,
        nint sourceOffset,
        TextureDataLayout sourceLayout)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateTransferTexture(destination, TextureUsage.CopyDestination, subresource, nameof(destination));
        var extent = GraphicsDevice.GetTextureSubresourceExtent(destination.Description, subresource);
        _ = GraphicsDevice.ValidateTextureUploadSource(
            source,
            sourceOffset,
            sourceLayout,
            extent.Width,
            extent.Height,
            extent.Depth,
            destination.Description.Format);
        var rowByteLength = checked((nint)extent.Width * TextureReadback.GetBytesPerTexel(destination.Description.Format));
        var slicePitch = checked(rowByteLength * extent.Height);
        var byteLength = checked(slicePitch * extent.Depth);
        var staging = CreateBuffer(new BufferDescription(
            checked((long)byteLength),
            BufferUsage.CopySource,
            GpuMemoryKind.Upload,
            structuredStride: null,
            debugName: "Vulkan texture upload staging"));

        try
        {
            WriteTextureSourceToMappedBuffer(
                staging,
                source,
                sourceOffset,
                sourceLayout,
                extent,
                rowByteLength);
            BeginOneTimeCommands();
            var oldLayout = destination.GetLayout(subresource);
            RecordImageBarrier(
                destination,
                subresource,
                oldLayout,
                VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL);
            RecordBufferBarrier(
                staging,
                0,
                byteLength,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_HOST_BIT,
                VkAccessFlags2.VK_ACCESS_2_HOST_WRITE_BIT,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT,
                VkAccessFlags2.VK_ACCESS_2_TRANSFER_READ_BIT);
            var copy = CreateBufferImageCopy(subresource, extent);
            VulkanApi.vkCmdCopyBufferToImage(
                _commandBuffer,
                staging.Buffer,
                destination.Image,
                VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                1,
                &copy);
            RecordImageBarrier(
                destination,
                subresource,
                VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                VkImageLayout.VK_IMAGE_LAYOUT_GENERAL);
            var completionValue = SubmitAndWait(destination);
            destination.SetLayout(subresource, VkImageLayout.VK_IMAGE_LAYOUT_GENERAL);
            destination.MarkInitialized(subresource);
            return completionValue;
        }
        finally
        {
            if (_hasUncertainSubmission)
            {
                RetainUncertainBuffer(staging);
            }
            else
            {
                staging.Dispose();
            }
        }
    }

    private void RecordImageBarrier(
        VulkanBackendTexture texture,
        TextureSubresource subresource,
        VkImageLayout oldLayout,
        VkImageLayout newLayout)
    {
        var barrier = default(VkImageMemoryBarrier2);
        barrier.sType = VkStructureType.VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;

        switch (oldLayout)
        {
            case VkImageLayout.VK_IMAGE_LAYOUT_UNDEFINED:
            {
                barrier.srcStageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE;
                barrier.srcAccessMask = VkAccessFlags2.VK_ACCESS_2_NONE;
                break;
            }

            case VkImageLayout.VK_IMAGE_LAYOUT_GENERAL:
            {
                barrier.srcStageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT;
                barrier.srcAccessMask =
                    VkAccessFlags2.VK_ACCESS_2_MEMORY_READ_BIT | VkAccessFlags2.VK_ACCESS_2_MEMORY_WRITE_BIT;
                break;
            }

            case VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL:
            {
                barrier.srcStageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT;
                barrier.srcAccessMask = VkAccessFlags2.VK_ACCESS_2_TRANSFER_READ_BIT;
                break;
            }

            case VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL:
            {
                barrier.srcStageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT;
                barrier.srcAccessMask = VkAccessFlags2.VK_ACCESS_2_TRANSFER_WRITE_BIT;
                break;
            }

            default:
            {
                throw new InvalidOperationException($"Vulkan texture layout {oldLayout} has no transfer transition.");
            }
        }

        switch (newLayout)
        {
            case VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL:
            {
                barrier.dstStageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT;
                barrier.dstAccessMask = VkAccessFlags2.VK_ACCESS_2_TRANSFER_READ_BIT;
                break;
            }

            case VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL:
            {
                barrier.dstStageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT;
                barrier.dstAccessMask = VkAccessFlags2.VK_ACCESS_2_TRANSFER_WRITE_BIT;
                break;
            }

            case VkImageLayout.VK_IMAGE_LAYOUT_GENERAL:
            {
                barrier.dstStageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT;
                barrier.dstAccessMask =
                    VkAccessFlags2.VK_ACCESS_2_MEMORY_READ_BIT | VkAccessFlags2.VK_ACCESS_2_MEMORY_WRITE_BIT;
                break;
            }

            default:
            {
                throw new ArgumentOutOfRangeException(nameof(newLayout));
            }
        }

        barrier.oldLayout = oldLayout;
        barrier.newLayout = newLayout;
        barrier.srcQueueFamilyIndex = uint.MaxValue;
        barrier.dstQueueFamilyIndex = uint.MaxValue;
        barrier.image = texture.Image;
        barrier.subresourceRange.aspectMask = subresource.Aspect == TextureAspect.Depth
            ? VkImageAspectFlags.VK_IMAGE_ASPECT_DEPTH_BIT
            : VkImageAspectFlags.VK_IMAGE_ASPECT_COLOR_BIT;
        barrier.subresourceRange.baseMipLevel = checked((uint)subresource.MipLevel);
        barrier.subresourceRange.levelCount = 1;
        barrier.subresourceRange.baseArrayLayer = checked((uint)subresource.ArrayLayer);
        barrier.subresourceRange.layerCount = 1;
        var dependencyInfo = default(VkDependencyInfo);
        dependencyInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
        dependencyInfo.imageMemoryBarrierCount = 1;
        dependencyInfo.pImageMemoryBarriers = &barrier;
        VulkanApi.vkCmdPipelineBarrier2(_commandBuffer, &dependencyInfo);
    }

    private void ValidateTransferTexture(
        VulkanBackendTexture texture,
        TextureUsage requiredUsage,
        TextureSubresource subresource,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(texture, parameterName);
        EnsureActive();

        if (!ReferenceEquals(texture.Owner, this))
        {
            throw new ArgumentException("The texture belongs to a different Vulkan device.", parameterName);
        }

        ObjectDisposedException.ThrowIf(texture.IsDisposed, parameterName);
        _ = GraphicsDevice.GetTextureSubresourceExtent(texture.Description, subresource);

        if ((texture.Description.Usage & requiredUsage) != requiredUsage)
        {
            throw new ArgumentException($"The texture does not permit {requiredUsage} usage.", parameterName);
        }
    }

    private void WriteTextureSourceToMappedBuffer(
        VulkanBackendBuffer destination,
        BufferReadLease<byte> source,
        nint sourceOffset,
        TextureDataLayout sourceLayout,
        (int Width, int Height, int Depth) extent,
        nint rowByteLength)
    {
        var mapped = MapHostVisibleMemory(destination);

        try
        {
            for (var depth = 0; depth < extent.Depth; depth++)
            {
                for (var row = 0; row < extent.Height; row++)
                {
                    var sourceRowOffset = checked(
                        sourceOffset
                        + (checked((nint)depth) * sourceLayout.SlicePitch)
                        + (checked((nint)row) * sourceLayout.RowPitch));
                    var destinationRowOffset = checked(
                        (checked((nint)depth) * extent.Height * rowByteLength)
                        + (checked((nint)row) * rowByteLength));
                    var copyLength = checked((int)rowByteLength);
                    var copyAction = new BufferLeaseCopyAction(mapped + destinationRowOffset);
                    source.UseWindow(sourceRowOffset, copyLength, ref copyAction);
                }
            }

            FlushMappedMemory(destination);
        }
        finally
        {
            VulkanApi.vkUnmapMemory(_device, destination.Memory);
        }
    }
}
