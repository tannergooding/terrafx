// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using TerraFX.Buffers;
using TerraFX.Graphics;
using TerraFX.Interop.Vulkan;
using VulkanApi = TerraFX.Interop.Vulkan.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal sealed unsafe partial class VulkanBackendDevice
{
    private const int MaximumActiveBufferReadbackCount = 256;
    private const ulong TransferWaitTimeoutNanoseconds = 30_000_000_000;

    private readonly List<VulkanBufferReadback> _activeBufferReadbacks = [];
    private bool _hasUncertainSubmission;
    private ulong _timelineValue;
    private readonly List<VulkanBackendBuffer> _uncertainBufferOwners = [];
    private readonly List<VulkanBackendDescriptorSet> _uncertainGraphDescriptorSets = [];
    private readonly List<VkImageView> _uncertainGraphImageViews = [];
    private readonly List<VulkanBackendTexture> _uncertainTextureOwners = [];

    private static void CopyMappedStorageToLease(byte* source, nint byteLength, BufferWriteLease<byte> destination)
    {
        nint copiedBytes = 0;

        while (copiedBytes < byteLength)
        {
            var remainingBytes = byteLength - copiedBytes;
            var copyLength = remainingBytes > int.MaxValue ? int.MaxValue : checked((int)remainingBytes);
            var action = new ReadbackCopyAction(source + copiedBytes);
            destination.UseWindow(copiedBytes, copyLength, ref action);
            copiedBytes = checked(copiedBytes + copyLength);
        }
    }

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

    private static void RecordBufferBarrier(
        VulkanBackendBuffer buffer,
        long offset,
        nint byteLength,
        VkPipelineStageFlags2 sourceStage,
        VkAccessFlags2 sourceAccess,
        VkPipelineStageFlags2 destinationStage,
        VkAccessFlags2 destinationAccess,
        VkCommandBuffer commandBuffer)
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
        VulkanApi.vkCmdPipelineBarrier2(commandBuffer, &dependencyInfo);
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
        Justification = "Staging memory and command storage are intentionally retained when transfer completion is uncertain.")]
    internal VulkanBufferReadback BeginBufferReadback(
        VulkanBackendBuffer source,
        GraphResourceState sourceState,
        long sourceOffset,
        nint byteLength)
    {
        ValidateTransferBuffer(source, BufferUsage.CopySource, nameof(source));
        ValidateTransferRange(source.Description.ByteLength, sourceOffset, byteLength, nameof(sourceOffset), nameof(byteLength));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            _activeBufferReadbacks.Count,
            MaximumActiveBufferReadbackCount);
        _ = _activeBufferReadbacks.EnsureCapacity(_activeBufferReadbacks.Count + 1);
        var staging = CreateStagingBuffer(new BufferDescription(
            checked((long)byteLength),
            BufferUsage.CopyDestination,
            GpuMemoryKind.Readback,
            structuredStride: null,
            debugName: "Vulkan buffer readback staging"));
        VkCommandBuffer commandBuffer = default;
        VulkanBufferReadback? readback = null;
        var readbackRegistered = false;
        var isSubmitted = false;

        try
        {
            readback = new VulkanBufferReadback(this, staging, byteLength);
            _activeBufferReadbacks.Add(readback);
            readbackRegistered = true;
            commandBuffer = AllocateTransferCommandBuffer();
            readback.SetCommandBuffer(commandBuffer);
            BeginOneTimeCommands(commandBuffer);
            var sourceStage = source.LastStageMask;
            var sourceAccess = source.LastAccessMask;

            if (sourceState == GraphResourceState.CopySource)
            {
                sourceStage = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT;
                sourceAccess = VkAccessFlags2.VK_ACCESS_2_TRANSFER_READ_BIT;
            }

            RecordBufferBarrier(
                source,
                sourceOffset,
                byteLength,
                sourceStage,
                sourceAccess,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT,
                VkAccessFlags2.VK_ACCESS_2_TRANSFER_READ_BIT,
                commandBuffer);
            var copy = new VkBufferCopy {
                srcOffset = checked((ulong)sourceOffset),
                dstOffset = 0,
                size = checked((ulong)byteLength),
            };
            VulkanApi.vkCmdCopyBuffer(commandBuffer, source.Buffer, staging.Buffer, 1, &copy);
            RecordBufferBarrier(
                staging,
                0,
                byteLength,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT,
                VkAccessFlags2.VK_ACCESS_2_TRANSFER_WRITE_BIT,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_HOST_BIT,
                VkAccessFlags2.VK_ACCESS_2_HOST_READ_BIT,
                commandBuffer);
            var completionFence = SubmitBufferReadbackWithoutWait(commandBuffer);
            isSubmitted = true;
            readback.MarkSubmitted(completionFence);
            source.SetLastAccessScope(
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT,
                VkAccessFlags2.VK_ACCESS_2_TRANSFER_READ_BIT);
            return readback;
        }
        catch
        {
            if (readback is not null)
            {
                if (_hasUncertainSubmission || isSubmitted)
                {
                    _hasUncertainSubmission = true;
                }
                else
                {
                    readback.AbortUnsubmitted(readbackRegistered);
                }
            }
            else if (_hasUncertainSubmission)
            {
                RetainUncertainBuffer(staging);
            }
            else
            {
                staging.Dispose();

                if (commandBuffer.Value is not null)
                {
                    VulkanApi.vkFreeCommandBuffers(_device, _commandPool, 1, &commandBuffer);
                }
            }

            throw;
        }
    }

    internal long GetCompletedGraphFence()
    {
        EnsureOwnerThread();
        ulong completedValue = 0;
        ThrowIfFailed(
            VulkanApi.vkGetSemaphoreCounterValue(_device, _timelineSemaphore, &completedValue),
            "vkGetSemaphoreCounterValue");
        var completedFence = checked((long)completedValue);
        var reportedFence = _completedGraphFenceValueOverride?.Invoke(completedFence) ?? completedFence;
        ArgumentOutOfRangeException.ThrowIfNegative(reportedFence);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(reportedFence, completedFence);
        RetireCompletedGraphFrameSlots(checked((ulong)reportedFence));
        return reportedFence;
    }

    internal long SubmitGraphWithoutWait(
        VkSemaphore waitSemaphore,
        VkPipelineStageFlags2 waitStageMask,
        VkSemaphore signalSemaphore)
    {
        EnsureActive();

        var endResult = VulkanApi.vkEndCommandBuffer(_commandBuffer);

        if (endResult != VkResult.VK_SUCCESS)
        {
            _hasUncertainSubmission = true;
            ThrowIfFailed(endResult, "vkEndCommandBuffer");
        }

        if (_timelineValue >= long.MaxValue)
        {
            throw new InvalidOperationException("The Vulkan graph timeline value space is exhausted.");
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
        var waitInfo = default(VkSemaphoreSubmitInfo);

        if (waitSemaphore.Value == 0 != (signalSemaphore.Value == 0))
        {
            throw new ArgumentException("Presentation submissions must provide both an acquire wait and a render-finished signal.");
        }

        if (waitSemaphore.Value != 0)
        {
            if ((waitStageMask == VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE)
                || (signalSemaphore.Value == 0))
            {
                throw new ArgumentException("A presentation submission requires acquire and render-finished semaphores.");
            }

            waitInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_SEMAPHORE_SUBMIT_INFO;
            waitInfo.semaphore = waitSemaphore;
            waitInfo.stageMask = waitStageMask;
        }

        var signalInfos = stackalloc VkSemaphoreSubmitInfo[2];
        signalInfos[0] = signalInfo;
        var signalCount = 1u;

        if (signalSemaphore.Value != 0)
        {
            var renderFinishedInfo = default(VkSemaphoreSubmitInfo);
            renderFinishedInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_SEMAPHORE_SUBMIT_INFO;
            renderFinishedInfo.semaphore = signalSemaphore;
            renderFinishedInfo.stageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT;
            signalInfos[signalCount++] = renderFinishedInfo;
        }

        var submitInfo = default(VkSubmitInfo2);
        submitInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_SUBMIT_INFO_2;
        submitInfo.commandBufferInfoCount = 1;
        submitInfo.pCommandBufferInfos = &commandBufferInfo;
        submitInfo.waitSemaphoreInfoCount = (waitSemaphore.Value == 0) ? 0u : 1u;
        submitInfo.pWaitSemaphoreInfos = (waitSemaphore.Value == 0) ? null : &waitInfo;
        submitInfo.signalSemaphoreInfoCount = signalCount;
        submitInfo.pSignalSemaphoreInfos = signalInfos;
        _hasUncertainSubmission = true;
        ThrowIfFailed(VulkanApi.vkQueueSubmit2(_queue, 1, &submitInfo, default), "vkQueueSubmit2");
        _timelineValue = signalValue;
        _hasUncertainSubmission = false;
        return checked((long)signalValue);
    }

    internal long SubmitGraphWithoutWait()
        => SubmitGraphWithoutWait(default, VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE, default);

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
        var (staging, isDedicatedStaging, stagingOffset, stagingReservation) = GetUploadStagingBuffer(
            checked((long)byteLength),
            Math.Max(4, Capabilities.Limits.BufferCopyOffsetAlignment));
        long? completedFenceValue = null;

        try
        {
            WriteMappedBuffer(staging, source, sourceOffset, byteLength, stagingOffset);
            BeginOneTimeCommands();
            RecordBufferBarrier(
                staging,
                stagingOffset,
                byteLength,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_HOST_BIT,
                VkAccessFlags2.VK_ACCESS_2_HOST_WRITE_BIT,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT,
                VkAccessFlags2.VK_ACCESS_2_TRANSFER_READ_BIT);
            RecordBufferBarrier(
                destination,
                destinationOffset,
                byteLength,
                destination.LastStageMask,
                destination.LastAccessMask,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT,
                VkAccessFlags2.VK_ACCESS_2_TRANSFER_WRITE_BIT);
            var copy = new VkBufferCopy {
                srcOffset = checked((ulong)stagingOffset),
                dstOffset = checked((ulong)destinationOffset),
                size = checked((ulong)byteLength),
            };
            VulkanApi.vkCmdCopyBuffer(_commandBuffer, staging.Buffer, destination.Buffer, 1, &copy);
            var completedFence = SubmitAndWait(
                destination,
                stagingReservation.IsValid
                    ? fenceValue => _uploadStagingAllocator.MarkSubmitted(stagingReservation, fenceValue)
                    : null);
            completedFenceValue = completedFence;
            destination.SetLastAccessScope(
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT,
                VkAccessFlags2.VK_ACCESS_2_TRANSFER_WRITE_BIT);
            return completedFence;
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

            if (stagingReservation.IsValid)
            {
                ReleaseStagingRange(
                    _uploadStagingAllocator,
                    stagingReservation,
                    completedFenceValue,
                    _hasUncertainSubmission);
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
        var (staging, isDedicatedStaging, stagingOffset, stagingReservation) = GetUploadStagingBuffer(
            source.Length,
            Math.Max(4, Capabilities.Limits.BufferCopyOffsetAlignment));
        long? completedFenceValue = null;

        try
        {
            WriteMappedBuffer(staging, source, stagingOffset);
            BeginOneTimeCommands();
            RecordBufferBarrier(
                staging,
                stagingOffset,
                source.Length,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_HOST_BIT,
                VkAccessFlags2.VK_ACCESS_2_HOST_WRITE_BIT,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT,
                VkAccessFlags2.VK_ACCESS_2_TRANSFER_READ_BIT);
            RecordBufferBarrier(
                destination,
                destinationOffset,
                source.Length,
                destination.LastStageMask,
                destination.LastAccessMask,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT,
                VkAccessFlags2.VK_ACCESS_2_TRANSFER_WRITE_BIT);
            var copy = new VkBufferCopy {
                srcOffset = checked((ulong)stagingOffset),
                dstOffset = checked((ulong)destinationOffset),
                size = checked((ulong)source.Length),
            };
            VulkanApi.vkCmdCopyBuffer(_commandBuffer, staging.Buffer, destination.Buffer, 1, &copy);
            var completedFence = SubmitAndWait(
                destination,
                stagingReservation.IsValid
                    ? fenceValue => _uploadStagingAllocator.MarkSubmitted(stagingReservation, fenceValue)
                    : null);
            completedFenceValue = completedFence;
            destination.SetLastAccessScope(
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT,
                VkAccessFlags2.VK_ACCESS_2_TRANSFER_WRITE_BIT);
            return completedFence;
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

            if (stagingReservation.IsValid)
            {
                ReleaseStagingRange(
                    _uploadStagingAllocator,
                    stagingReservation,
                    completedFenceValue,
                    _hasUncertainSubmission);
            }
        }
    }

    internal void WaitForGraphFence(long fenceValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fenceValue);
        _graphFenceWaitObserver?.Invoke(fenceValue);
        var semaphore = _timelineSemaphore;
        var value = checked((ulong)fenceValue);
        var waitInfo = default(VkSemaphoreWaitInfo);
        waitInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_SEMAPHORE_WAIT_INFO;
        waitInfo.semaphoreCount = 1;
        waitInfo.pSemaphores = &semaphore;
        waitInfo.pValues = &value;
        var result = VulkanApi.vkWaitSemaphores(_device, &waitInfo, TransferWaitTimeoutNanoseconds);
        result = _graphFenceWaitResultOverride?.Invoke(fenceValue, result) ?? result;
        ThrowIfFailed(result, "vkWaitSemaphores");
    }

    internal void WriteUploadBuffer(
        VulkanBackendBuffer destination,
        long destinationOffset,
        ReadOnlySpan<byte> source)
    {
        ArgumentNullException.ThrowIfNull(destination);
        EnsureOwnerThread();
        ValidateTransferBuffer(destination, BufferUsage.CopySource, nameof(destination));

        if (destination.Description.MemoryKind != GpuMemoryKind.Upload)
        {
            throw new ArgumentException("The buffer does not use host-visible upload memory.", nameof(destination));
        }

        ValidateTransferRange(
            destination.Description.ByteLength,
            destinationOffset,
            checked((nint)source.Length),
            nameof(destinationOffset),
            nameof(source));
        var mapped = MapHostVisibleMemory(destination);

        try
        {
            source.CopyTo(new Span<byte>(mapped + checked((nint)destinationOffset), source.Length));
            FlushMappedMemory(destination);
            destination.SetLastAccessScope(
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_HOST_BIT,
                VkAccessFlags2.VK_ACCESS_2_HOST_WRITE_BIT);
        }
        finally
        {
            VulkanApi.vkUnmapMemory(_device, destination.Memory);
        }
    }

    private VkCommandBuffer AllocateTransferCommandBuffer()
    {
        EnsureActive();
        var allocateInfo = default(VkCommandBufferAllocateInfo);
        allocateInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
        allocateInfo.commandPool = _commandPool;
        allocateInfo.level = VkCommandBufferLevel.VK_COMMAND_BUFFER_LEVEL_PRIMARY;
        allocateInfo.commandBufferCount = 1;
        VkCommandBuffer commandBuffer = default;
        ThrowIfFailed(
            VulkanApi.vkAllocateCommandBuffers(_device, &allocateInfo, &commandBuffer),
            "vkAllocateCommandBuffers");

        if (commandBuffer.Value is null)
        {
            throw new InvalidOperationException("Vulkan returned no buffer-readback command buffer.");
        }

        return commandBuffer;
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

    private void BeginOneTimeCommands(VkCommandBuffer commandBuffer)
    {
        EnsureActive();
        var beginInfo = default(VkCommandBufferBeginInfo);
        beginInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
        beginInfo.flags = VkCommandBufferUsageFlags.VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
        ThrowIfFailed(
            VulkanApi.vkBeginCommandBuffer(commandBuffer, &beginInfo),
            "vkBeginCommandBuffer");
    }

    private void CopyMappedBufferToLease(VulkanBackendBuffer buffer, nint byteLength, BufferWriteLease<byte> destination)
    {
        var mapped = MapHostVisibleMemory(buffer);

        try
        {
            if ((buffer.MemoryProperties & VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT) == 0)
            {
                var range = default(VkMappedMemoryRange);
                range.sType = VkStructureType.VK_STRUCTURE_TYPE_MAPPED_MEMORY_RANGE;
                range.memory = buffer.Memory;
                range.size = ulong.MaxValue;
                ThrowIfFailed(VulkanApi.vkInvalidateMappedMemoryRanges(_device, 1, &range), "vkInvalidateMappedMemoryRanges");
            }

            CopyMappedStorageToLease(mapped, byteLength, destination);
        }
        finally
        {
            VulkanApi.vkUnmapMemory(_device, buffer.Memory);
        }
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
        => RecordBufferBarrier(
            buffer,
            offset,
            byteLength,
            sourceStage,
            sourceAccess,
            destinationStage,
            destinationAccess,
            _commandBuffer);

    private void RetainUncertainBuffer(VulkanBackendBuffer buffer)
        => _uncertainBufferOwners.Add(buffer);

    private void RetainUncertainSubmissionOwners(
        VulkanBackendBuffer? retainedBuffer,
        VulkanBackendTexture? retainedTexture)
    {
        if (retainedBuffer is not null)
        {
            RetainUncertainBuffer(retainedBuffer);
        }

        if (retainedTexture is not null)
        {
            RetainUncertainTexture(retainedTexture);
        }
    }

    private void RetainUncertainTexture(VulkanBackendTexture texture)
        => _uncertainTextureOwners.Add(texture);

    private void StopRecording()
        => ThrowIfFailed(VulkanApi.vkResetCommandBuffer(_commandBuffer, default), "vkResetCommandBuffer");

    private long SubmitAndWait(VulkanBackendBuffer retainedBuffer, Action<long>? onSubmitted = null)
        => SubmitAndWaitCore(retainedBuffer, retainedTexture: null, onSubmitted: onSubmitted);

    private long SubmitAndWait(VulkanBackendTexture retainedTexture, Action<long>? onSubmitted = null)
        => SubmitAndWaitCore(retainedBuffer: null, retainedTexture: retainedTexture, onSubmitted: onSubmitted);

    private long SubmitAndWaitCore(
        VulkanBackendBuffer? retainedBuffer,
        VulkanBackendTexture? retainedTexture,
        Action<long>? onSubmitted = null)
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
            onSubmitted?.Invoke(checked((long)signalValue));
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
            RetainUncertainSubmissionOwners(retainedBuffer, retainedTexture);
            throw;
        }
        catch (GraphicsDeviceLostException) when (_hasUncertainSubmission)
        {
            RetainUncertainSubmissionOwners(retainedBuffer, retainedTexture);
            throw;
        }
    }

    private long SubmitBufferReadbackWithoutWait(VkCommandBuffer commandBuffer)
    {
        EnsureActive();

        var endResult = VulkanApi.vkEndCommandBuffer(commandBuffer);

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
        commandBufferInfo.commandBuffer = commandBuffer;
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
        _hasUncertainSubmission = false;
        return checked((long)signalValue);
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
        nint byteLength,
        long destinationOffset)
    {
        var mapped = MapHostVisibleMemory(destination);

        try
        {
            CopySourceLeaseToMappedStorage(
                source,
                sourceOffset,
                byteLength,
                mapped + checked((nint)destinationOffset));
            FlushMappedMemory(destination);
        }
        finally
        {
            VulkanApi.vkUnmapMemory(_device, destination.Memory);
        }
    }

    private void WriteMappedBuffer(VulkanBackendBuffer destination, ReadOnlySpan<byte> source, long destinationOffset)
    {
        var mapped = MapHostVisibleMemory(destination);

        try
        {
            source.CopyTo(new Span<byte>(mapped + checked((nint)destinationOffset), source.Length));
            FlushMappedMemory(destination);
        }
        finally
        {
            VulkanApi.vkUnmapMemory(_device, destination.Memory);
        }
    }

    internal sealed class VulkanBufferReadback : IGraphicsBackendBufferReadback
    {
        private readonly nint _byteLength;
        private VkCommandBuffer _commandBuffer;
        private long _completionFence;
        private readonly VulkanBackendDevice _device;
        private VulkanBackendBuffer? _staging;
        private int _state;

        internal VulkanBufferReadback(
            VulkanBackendDevice device,
            VulkanBackendBuffer staging,
            nint byteLength)
        {
            _byteLength = byteLength;
            _device = device;
            _staging = staging;
        }

        public long CompletionFence => Volatile.Read(ref _completionFence);

        public void CopyCompleted(BufferWriteLease<byte> destination)
        {
            ArgumentNullException.ThrowIfNull(destination);
            _device.EnsureOwnerThread();
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(CompletionFence);

            if (destination.Length != _byteLength)
            {
                throw new ArgumentException("The readback destination has an unexpected byte extent.", nameof(destination));
            }

            if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            {
                throw new InvalidOperationException("The Vulkan buffer readback has already been copied or disposed.");
            }

            _device.CopyMappedBufferToLease(
                _staging ?? throw new ObjectDisposedException(nameof(VulkanBufferReadback)),
                _byteLength,
                destination);
        }

        public void Dispose()
        {
            _device.EnsureOwnerThread();

            if (Volatile.Read(ref _state) == 2)
            {
                return;
            }

            if (Volatile.Read(ref _state) != 1)
            {
                throw new InvalidOperationException("The Vulkan buffer readback cannot be disposed before completion is copied.");
            }

            ReleaseNativeStorage();

            if (!_device._activeBufferReadbacks.Remove(this))
            {
                throw new InvalidOperationException("The Vulkan buffer-readback owner was not registered.");
            }

            Volatile.Write(ref _state, 2);
        }

        internal void AbortUnsubmitted(bool registered)
        {
            _device.EnsureOwnerThread();

            if ((Volatile.Read(ref _state) != 0) || (CompletionFence != 0))
            {
                throw new InvalidOperationException("Only an unsubmitted Vulkan buffer readback can be aborted.");
            }

            try
            {
                ReleaseNativeStorage();

                if (registered && !_device._activeBufferReadbacks.Remove(this))
                {
                    throw new InvalidOperationException("The Vulkan buffer-readback owner was not registered.");
                }

                Volatile.Write(ref _state, 2);
            }
            catch
            {
                _device._hasUncertainSubmission = true;
                throw;
            }
        }

        internal void MarkSubmitted(long completionFence)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(completionFence);
            Volatile.Write(ref _completionFence, completionFence);
        }

        internal void SetCommandBuffer(VkCommandBuffer commandBuffer)
            => _commandBuffer = commandBuffer;

        private void ReleaseNativeStorage()
        {
            var commandBuffer = _commandBuffer;
            _commandBuffer = default;

            if (commandBuffer.Value is not null)
            {
                VulkanApi.vkFreeCommandBuffers(_device._device, _device._commandPool, 1, &commandBuffer);
            }

            var staging = _staging;
            staging?.Dispose();
            _staging = null;
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

    private readonly struct ReadbackCopyAction(byte* source) : IAction<Span<byte>>
    {
        private readonly byte* _source = source;

        public void Invoke(scoped Span<byte> argument)
            => new ReadOnlySpan<byte>(_source, argument.Length).CopyTo(argument);
    }
}
