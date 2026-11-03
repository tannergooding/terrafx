// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using TerraFX.Buffers;
using TerraFX.Graphics;
using TerraFX.Interop.Vulkan;
using VulkanApi = TerraFX.Interop.Vulkan.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal sealed unsafe partial class VulkanBackendDevice
{
    private const ulong TransferWaitTimeoutNanoseconds = 30_000_000_000;

    private bool _hasUncertainSubmission;
    private ulong _timelineValue;
    private readonly List<VulkanBackendBuffer> _uncertainBufferOwners = [];
    private readonly List<VulkanBackendTexture> _uncertainTextureOwners = [];

    private static void CopyMappedStorageToOwnedBuffer(byte* source, nint byteLength, UnmanagedBuffer<byte> destination)
    {
        nint copiedBytes = 0;

        while (copiedBytes < byteLength)
        {
            var remainingBytes = byteLength - copiedBytes;
            var copyLength = remainingBytes > int.MaxValue
                ? int.MaxValue
                : checked((int)remainingBytes);
            destination.CopyFrom(new ReadOnlySpan<byte>(source + copiedBytes, copyLength), copiedBytes);
            copiedBytes = checked(copiedBytes + copyLength);
        }
    }

    private static void CopySourceLeaseToMappedStorage(
        BufferReadLease<byte> source,
        nint sourceOffset,
        nint byteLength,
        byte* destination)
    {
        nint copiedBytes = 0;

        while (copiedBytes < byteLength)
        {
            var remainingBytes = byteLength - copiedBytes;
            var copyLength = remainingBytes > int.MaxValue
                ? int.MaxValue
                : checked((int)remainingBytes);
            var copyAction = new BufferLeaseCopyAction(destination + copiedBytes);
            source.UseWindow(checked(sourceOffset + copiedBytes), copyLength, ref copyAction);
            copiedBytes = checked(copiedBytes + copyLength);
        }
    }

    private static void ValidateTransferRange(
        long bufferByteLength,
        long offset,
        nint byteLength,
        string offsetName,
        string byteLengthName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset, offsetName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLength, byteLengthName);
        var checkedByteLength = checked((long)byteLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, bufferByteLength, offsetName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(checkedByteLength, bufferByteLength - offset, byteLengthName);
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Staging memory is intentionally retained when transfer completion is uncertain.")]
    internal OwnedBuffer<byte> ReadbackBuffer(VulkanBackendBuffer source, long sourceOffset, nint byteLength)
    {
        ValidateTransferBuffer(source, BufferUsage.CopySource, nameof(source));
        ValidateTransferRange(source.Description.ByteLength, sourceOffset, byteLength, nameof(sourceOffset), nameof(byteLength));
        var staging = CreateStagingBuffer(new BufferDescription(
            checked((long)byteLength),
            BufferUsage.CopyDestination,
            GpuMemoryKind.Readback,
            structuredStride: null,
            debugName: "Vulkan buffer readback staging"));

        try
        {
            BeginOneTimeCommands();
            RecordBufferBarrier(
                source,
                sourceOffset,
                byteLength,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,
                VkAccessFlags2.VK_ACCESS_2_MEMORY_READ_BIT | VkAccessFlags2.VK_ACCESS_2_MEMORY_WRITE_BIT,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT,
                VkAccessFlags2.VK_ACCESS_2_TRANSFER_READ_BIT);
            var copy = new VkBufferCopy {
                srcOffset = checked((ulong)sourceOffset),
                dstOffset = 0,
                size = checked((ulong)byteLength),
            };
            VulkanApi.vkCmdCopyBuffer(_commandBuffer, source.Buffer, staging.Buffer, 1, &copy);
            RecordBufferBarrier(
                source,
                sourceOffset,
                byteLength,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT,
                VkAccessFlags2.VK_ACCESS_2_TRANSFER_READ_BIT,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,
                VkAccessFlags2.VK_ACCESS_2_MEMORY_READ_BIT | VkAccessFlags2.VK_ACCESS_2_MEMORY_WRITE_BIT);
            RecordBufferBarrier(
                staging,
                0,
                byteLength,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT,
                VkAccessFlags2.VK_ACCESS_2_TRANSFER_WRITE_BIT,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_HOST_BIT,
                VkAccessFlags2.VK_ACCESS_2_HOST_READ_BIT);
            _ = SubmitAndWait(source);
            return ReadMappedBuffer(staging, byteLength);
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
    internal long UploadBuffer(
        VulkanBackendBuffer destination,
        long destinationOffset,
        BufferReadLease<byte> source,
        nint sourceOffset,
        nint byteLength)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceOffset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sourceOffset, source.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(byteLength, source.Length - sourceOffset, nameof(source));
        ValidateTransferBuffer(destination, BufferUsage.CopyDestination, nameof(destination));

        if (destination.Description.MemoryKind != GpuMemoryKind.DeviceLocal)
        {
            throw new NotSupportedException("Vulkan buffer uploads require a device-local destination.");
        }

        ValidateTransferRange(
            destination.Description.ByteLength,
            destinationOffset,
            byteLength,
            nameof(destinationOffset),
            nameof(byteLength));
        var (staging, isDedicatedStaging) = GetUploadStagingBuffer(checked((long)byteLength));

        try
        {
            WriteMappedBuffer(staging, source, sourceOffset, byteLength);
            BeginOneTimeCommands();
            RecordBufferBarrier(
                staging,
                0,
                byteLength,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_HOST_BIT,
                VkAccessFlags2.VK_ACCESS_2_HOST_WRITE_BIT,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT,
                VkAccessFlags2.VK_ACCESS_2_TRANSFER_READ_BIT);
            RecordBufferBarrier(
                destination,
                destinationOffset,
                byteLength,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,
                VkAccessFlags2.VK_ACCESS_2_MEMORY_READ_BIT | VkAccessFlags2.VK_ACCESS_2_MEMORY_WRITE_BIT,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT,
                VkAccessFlags2.VK_ACCESS_2_TRANSFER_WRITE_BIT);
            var copy = new VkBufferCopy {
                srcOffset = 0,
                dstOffset = checked((ulong)destinationOffset),
                size = checked((ulong)byteLength),
            };
            VulkanApi.vkCmdCopyBuffer(_commandBuffer, staging.Buffer, destination.Buffer, 1, &copy);
            RecordBufferBarrier(
                destination,
                destinationOffset,
                byteLength,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT,
                VkAccessFlags2.VK_ACCESS_2_TRANSFER_WRITE_BIT,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,
                VkAccessFlags2.VK_ACCESS_2_MEMORY_READ_BIT | VkAccessFlags2.VK_ACCESS_2_MEMORY_WRITE_BIT);
            return SubmitAndWait(destination);
        }
        finally
        {
            if (_hasUncertainSubmission)
            {
                RetainUncertainBuffer(staging);
            }
            else if (isDedicatedStaging)
            {
                staging.Dispose();
            }
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Staging memory is intentionally retained when transfer completion is uncertain.")]
    internal long UploadBuffer(VulkanBackendBuffer destination, long destinationOffset, ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        ValidateTransferBuffer(destination, BufferUsage.CopyDestination, nameof(destination));

        if (destination.Description.MemoryKind != GpuMemoryKind.DeviceLocal)
        {
            throw new NotSupportedException("Vulkan buffer uploads require a device-local destination.");
        }

        ValidateTransferRange(
            destination.Description.ByteLength,
            destinationOffset,
            source.Length,
            nameof(destinationOffset),
            nameof(source));
        var (staging, isDedicatedStaging) = GetUploadStagingBuffer(source.Length);

        try
        {
            WriteMappedBuffer(staging, source);
            BeginOneTimeCommands();
            RecordBufferBarrier(
                staging,
                0,
                source.Length,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_HOST_BIT,
                VkAccessFlags2.VK_ACCESS_2_HOST_WRITE_BIT,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT,
                VkAccessFlags2.VK_ACCESS_2_TRANSFER_READ_BIT);
            RecordBufferBarrier(
                destination,
                destinationOffset,
                source.Length,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,
                VkAccessFlags2.VK_ACCESS_2_MEMORY_READ_BIT | VkAccessFlags2.VK_ACCESS_2_MEMORY_WRITE_BIT,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT,
                VkAccessFlags2.VK_ACCESS_2_TRANSFER_WRITE_BIT);
            var copy = new VkBufferCopy {
                srcOffset = 0,
                dstOffset = checked((ulong)destinationOffset),
                size = checked((ulong)source.Length),
            };
            VulkanApi.vkCmdCopyBuffer(_commandBuffer, staging.Buffer, destination.Buffer, 1, &copy);
            RecordBufferBarrier(
                destination,
                destinationOffset,
                source.Length,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT,
                VkAccessFlags2.VK_ACCESS_2_TRANSFER_WRITE_BIT,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT,
                VkAccessFlags2.VK_ACCESS_2_MEMORY_READ_BIT | VkAccessFlags2.VK_ACCESS_2_MEMORY_WRITE_BIT);
            return SubmitAndWait(destination);
        }
        finally
        {
            if (_hasUncertainSubmission)
            {
                RetainUncertainBuffer(staging);
            }
            else if (isDedicatedStaging)
            {
                staging.Dispose();
            }
        }
    }

    private void BeginOneTimeCommands()
    {
        EnsureActive();
        ThrowIfFailed(VulkanApi.vkResetCommandBuffer(_commandBuffer, default), "vkResetCommandBuffer");
        var beginInfo = default(VkCommandBufferBeginInfo);
        beginInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
        beginInfo.flags = VkCommandBufferUsageFlags.VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
        ThrowIfFailed(VulkanApi.vkBeginCommandBuffer(_commandBuffer, &beginInfo), "vkBeginCommandBuffer");
    }

    private void FlushMappedMemory(VulkanBackendBuffer buffer)
    {
        if ((buffer.MemoryProperties & VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT) != 0)
        {
            return;
        }

        var range = default(VkMappedMemoryRange);
        range.sType = VkStructureType.VK_STRUCTURE_TYPE_MAPPED_MEMORY_RANGE;
        range.memory = buffer.Memory;
        range.size = ulong.MaxValue;
        ThrowIfFailed(VulkanApi.vkFlushMappedMemoryRanges(_device, 1, &range), "vkFlushMappedMemoryRanges");
    }

    private byte* MapHostVisibleMemory(VulkanBackendBuffer buffer)
    {
        if ((buffer.MemoryProperties & VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT) == 0)
        {
            throw new InvalidOperationException("Vulkan buffer memory is not host visible.");
        }

        void* mapped = null;
        ThrowIfFailed(
            VulkanApi.vkMapMemory(_device, buffer.Memory, 0, ulong.MaxValue, default, &mapped),
            "vkMapMemory");

        if (mapped is null)
        {
            VulkanApi.vkUnmapMemory(_device, buffer.Memory);
            throw new InvalidOperationException("Vulkan mapped buffer memory returned no address.");
        }

        return (byte*)mapped;
    }

    private OwnedBuffer<byte> ReadMappedBuffer(VulkanBackendBuffer buffer, nint byteLength)
    {
        var storage = UnmanagedBuffer<byte>.Allocate(
            byteLength,
            new BufferAllocationOptions(byteLength, alignment: 1));
        var transferred = false;
        byte* mapped = null;

        try
        {
            mapped = MapHostVisibleMemory(buffer);

            if ((buffer.MemoryProperties & VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT) == 0)
            {
                var range = default(VkMappedMemoryRange);
                range.sType = VkStructureType.VK_STRUCTURE_TYPE_MAPPED_MEMORY_RANGE;
                range.memory = buffer.Memory;
                range.size = ulong.MaxValue;
                ThrowIfFailed(VulkanApi.vkInvalidateMappedMemoryRanges(_device, 1, &range), "vkInvalidateMappedMemoryRanges");
            }

            CopyMappedStorageToOwnedBuffer(mapped, byteLength, storage);
            var ownedStorage = storage.TransferToOwnedBuffer();
            transferred = true;
            return ownedStorage;
        }
        finally
        {
            if (mapped is not null)
            {
                VulkanApi.vkUnmapMemory(_device, buffer.Memory);
            }

            if (!transferred)
            {
                storage.Dispose();
            }
        }
    }

    private void RecordBufferBarrier(
        VulkanBackendBuffer buffer,
        long offset,
        nint byteLength,
        VkPipelineStageFlags2 sourceStage,
        VkAccessFlags2 sourceAccess,
        VkPipelineStageFlags2 destinationStage,
        VkAccessFlags2 destinationAccess)
    {
        var barrier = default(VkBufferMemoryBarrier2);
        barrier.sType = VkStructureType.VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER_2;
        barrier.srcStageMask = sourceStage;
        barrier.srcAccessMask = sourceAccess;
        barrier.dstStageMask = destinationStage;
        barrier.dstAccessMask = destinationAccess;
        barrier.srcQueueFamilyIndex = uint.MaxValue;
        barrier.dstQueueFamilyIndex = uint.MaxValue;
        barrier.buffer = buffer.Buffer;
        barrier.offset = checked((ulong)offset);
        barrier.size = checked((ulong)byteLength);
        var dependencyInfo = default(VkDependencyInfo);
        dependencyInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
        dependencyInfo.bufferMemoryBarrierCount = 1;
        dependencyInfo.pBufferMemoryBarriers = &barrier;
        VulkanApi.vkCmdPipelineBarrier2(_commandBuffer, &dependencyInfo);
    }

    private void RetainUncertainBuffer(VulkanBackendBuffer buffer)
        => _uncertainBufferOwners.Add(buffer);

    private void RetainUncertainTexture(VulkanBackendTexture texture)
        => _uncertainTextureOwners.Add(texture);

    private long SubmitAndWait(VulkanBackendBuffer retainedBuffer)
        => SubmitAndWaitCore(retainedBuffer, retainedTexture: null);

    private long SubmitAndWait(VulkanBackendTexture retainedTexture)
        => SubmitAndWaitCore(retainedBuffer: null, retainedTexture: retainedTexture);

    private long SubmitAndWaitCore(VulkanBackendBuffer? retainedBuffer, VulkanBackendTexture? retainedTexture)
    {
        EnsureActive();

        try
        {
            var endResult = VulkanApi.vkEndCommandBuffer(_commandBuffer);

            if (endResult != VkResult.VK_SUCCESS)
            {
                _hasUncertainSubmission = true;
                ThrowIfFailed(endResult, "vkEndCommandBuffer");
            }

            if (_timelineValue >= long.MaxValue)
            {
                throw new InvalidOperationException("The Vulkan transfer timeline value space is exhausted.");
            }

            var signalValue = _timelineValue + 1;
            var commandBufferInfo = default(VkCommandBufferSubmitInfo);
            commandBufferInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_COMMAND_BUFFER_SUBMIT_INFO;
            commandBufferInfo.commandBuffer = _commandBuffer;
            var signalInfo = default(VkSemaphoreSubmitInfo);
            signalInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_SEMAPHORE_SUBMIT_INFO;
            signalInfo.semaphore = _timelineSemaphore;
            signalInfo.value = signalValue;
            signalInfo.stageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT;
            var submitInfo = default(VkSubmitInfo2);
            submitInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_SUBMIT_INFO_2;
            submitInfo.commandBufferInfoCount = 1;
            submitInfo.pCommandBufferInfos = &commandBufferInfo;
            submitInfo.signalSemaphoreInfoCount = 1;
            submitInfo.pSignalSemaphoreInfos = &signalInfo;
            _hasUncertainSubmission = true;
            ThrowIfFailed(VulkanApi.vkQueueSubmit2(_queue, 1, &submitInfo, default), "vkQueueSubmit2");
            _timelineValue = signalValue;
            var waitInfo = default(VkSemaphoreWaitInfo);
            waitInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_SEMAPHORE_WAIT_INFO;
            waitInfo.semaphoreCount = 1;
            var semaphore = _timelineSemaphore;
            waitInfo.pSemaphores = &semaphore;
            waitInfo.pValues = &signalValue;
            var waitResult = VulkanApi.vkWaitSemaphores(_device, &waitInfo, TransferWaitTimeoutNanoseconds);

            if (_transferWaitResultOverride is not null)
            {
                waitResult = _transferWaitResultOverride(waitResult);
            }

            ThrowIfFailed(waitResult, "vkWaitSemaphores");
            _hasUncertainSubmission = false;
            return checked((long)signalValue);
        }
        catch (InvalidOperationException) when (_hasUncertainSubmission)
        {
            if (retainedBuffer is not null)
            {
                RetainUncertainBuffer(retainedBuffer);
            }

            if (retainedTexture is not null)
            {
                RetainUncertainTexture(retainedTexture);
            }

            throw;
        }
    }

    private void ValidateTransferBuffer(VulkanBackendBuffer buffer, BufferUsage requiredUsage, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(buffer, parameterName);
        EnsureActive();

        if (!ReferenceEquals(buffer.Owner, this))
        {
            throw new ArgumentException("The buffer belongs to a different Vulkan device.", parameterName);
        }

        ObjectDisposedException.ThrowIf(buffer.IsDisposed, parameterName);

        if ((buffer.Description.Usage & requiredUsage) != requiredUsage)
        {
            throw new ArgumentException($"The buffer does not permit {requiredUsage} usage.", parameterName);
        }
    }

    private void WriteMappedBuffer(
        VulkanBackendBuffer destination,
        BufferReadLease<byte> source,
        nint sourceOffset,
        nint byteLength)
    {
        var mapped = MapHostVisibleMemory(destination);

        try
        {
            CopySourceLeaseToMappedStorage(source, sourceOffset, byteLength, mapped);
            FlushMappedMemory(destination);
        }
        finally
        {
            VulkanApi.vkUnmapMemory(_device, destination.Memory);
        }
    }

    private void WriteMappedBuffer(VulkanBackendBuffer destination, ReadOnlySpan<byte> source)
    {
        var mapped = MapHostVisibleMemory(destination);

        try
        {
            source.CopyTo(new Span<byte>(mapped, source.Length));
            FlushMappedMemory(destination);
        }
        finally
        {
            VulkanApi.vkUnmapMemory(_device, destination.Memory);
        }
    }

    private readonly unsafe struct BufferLeaseCopyAction : IAction<ReadOnlySpan<byte>>
    {
        private readonly byte* _destination;

        internal BufferLeaseCopyAction(byte* destination)
        {
            _destination = destination;
        }

        public readonly void Invoke(scoped ReadOnlySpan<byte> argument)
            => argument.CopyTo(new Span<byte>(_destination, argument.Length));
    }
}
