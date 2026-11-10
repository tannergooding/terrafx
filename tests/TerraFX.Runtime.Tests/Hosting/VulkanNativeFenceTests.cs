// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Graphics;
using TerraFX.Graphics.Vulkan;
using TerraFX.Interop.Vulkan;
using VulkanApi = TerraFX.Interop.Vulkan.Vulkan;

namespace TerraFX.Runtime.Tests.Hosting;

internal sealed partial class EngineHostOptionsTests
{
    private static VulkanBackendDevice CreateVulkanDeviceOrIgnore()
    {
        try
        {
            return VulkanBackendDevice.Create(GraphicsDeviceProfile.OffscreenNative, default);
        }
        catch (DllNotFoundException exception)
        {
            Assert.Ignore($"No Vulkan loader is installed: {exception.Message}");
        }
        catch (EntryPointNotFoundException exception)
        {
            Assert.Ignore($"The installed Vulkan loader lacks a required entry point: {exception.Message}");
        }
        catch (NotSupportedException exception)
        {
            Assert.Ignore($"No Vulkan device satisfies OffscreenNative: {exception.Message}");
        }

        throw new InvalidOperationException("Vulkan device setup was ignored without terminating the test.");
    }

    [Test]
    public unsafe void VulkanDriverReturnsTimeoutForNeverSubmittedFence()
    {
        using var device = CreateVulkanDeviceOrIgnore();
        var capabilities = device.Capabilities;
        TestContext.WriteLine(
            $"Backend={capabilities.Backend}; Adapter={capabilities.AdapterName}; Driver={capabilities.DriverName} {capabilities.DriverVersion}; "
            + $"API={capabilities.ApiVersion.Major}.{capabilities.ApiVersion.Minor}.{capabilities.ApiVersion.Patch}");

        var createInfo = default(VkFenceCreateInfo);
        createInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
        var fence = default(VkFence);
        var createResult = VulkanApi.vkCreateFence(device.DeviceHandle, &createInfo, null, &fence);

        if (createResult != VkResult.VK_SUCCESS)
        {
            throw new InvalidOperationException($"vkCreateFence failed with {createResult}.");
        }

        try
        {
            var waitResult = VulkanApi.vkWaitForFences(
                device.DeviceHandle,
                fenceCount: 1,
                &fence,
                waitAll: 1,
                timeout: 1);
            Assert.That(waitResult, Is.EqualTo(VkResult.VK_TIMEOUT));
            TestContext.WriteLine("The Vulkan driver returned VK_TIMEOUT for a never-submitted unsignaled fence.");
        }
        finally
        {
            VulkanApi.vkDestroyFence(device.DeviceHandle, fence, null);
        }
    }

    [Test]
    public unsafe void VulkanSubmittedQueueWaitReturnsTimeoutUntilHostSignal()
    {
        using var device = CreateVulkanDeviceOrIgnore();
        var capabilities = device.Capabilities;
        TestContext.WriteLine(
            $"Backend={capabilities.Backend}; Adapter={capabilities.AdapterName}; Driver={capabilities.DriverName} {capabilities.DriverVersion}; "
            + $"API={capabilities.ApiVersion.Major}.{capabilities.ApiVersion.Minor}.{capabilities.ApiVersion.Patch}");

        var semaphore = default(VkSemaphore);
        var fence = default(VkFence);
        var hasSemaphore = false;
        var hasFence = false;
        var submissionAccepted = false;
        var gateReleased = false;
        var pendingWaitResult = VkResult.VK_SUCCESS;
        var hostSignalResult = VkResult.VK_SUCCESS;
        var cleanupSignalResult = VkResult.VK_SUCCESS;
        var drainResult = VkResult.VK_SUCCESS;

        try
        {
            var semaphoreTypeInfo = default(VkSemaphoreTypeCreateInfo);
            semaphoreTypeInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_SEMAPHORE_TYPE_CREATE_INFO;
            semaphoreTypeInfo.semaphoreType = VkSemaphoreType.VK_SEMAPHORE_TYPE_TIMELINE;
            var semaphoreCreateInfo = default(VkSemaphoreCreateInfo);
            semaphoreCreateInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO;
            semaphoreCreateInfo.pNext = &semaphoreTypeInfo;
            var semaphoreCreateResult = VulkanApi.vkCreateSemaphore(
                device.DeviceHandle,
                &semaphoreCreateInfo,
                null,
                &semaphore);

            if (semaphoreCreateResult != VkResult.VK_SUCCESS)
            {
                throw new InvalidOperationException($"vkCreateSemaphore failed with {semaphoreCreateResult}.");
            }

            hasSemaphore = true;

            var fenceCreateInfo = default(VkFenceCreateInfo);
            fenceCreateInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
            var fenceCreateResult = VulkanApi.vkCreateFence(device.DeviceHandle, &fenceCreateInfo, null, &fence);

            if (fenceCreateResult != VkResult.VK_SUCCESS)
            {
                throw new InvalidOperationException($"vkCreateFence failed with {fenceCreateResult}.");
            }

            hasFence = true;

            var waitInfo = default(VkSemaphoreSubmitInfo);
            waitInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_SEMAPHORE_SUBMIT_INFO;
            waitInfo.semaphore = semaphore;
            waitInfo.value = 1;
            waitInfo.stageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT;
            var submitInfo = default(VkSubmitInfo2);
            submitInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_SUBMIT_INFO_2;
            submitInfo.waitSemaphoreInfoCount = 1;
            submitInfo.pWaitSemaphoreInfos = &waitInfo;
            var submitResult = VulkanApi.vkQueueSubmit2(device.GraphicsQueue, 1, &submitInfo, fence);

            if (submitResult != VkResult.VK_SUCCESS)
            {
                throw new InvalidOperationException($"vkQueueSubmit2 failed with {submitResult}.");
            }

            submissionAccepted = true;
            pendingWaitResult = VulkanApi.vkWaitForFences(
                device.DeviceHandle,
                fenceCount: 1,
                &fence,
                waitAll: 1,
                timeout: 1);

            var signalInfo = default(VkSemaphoreSignalInfo);
            signalInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_SEMAPHORE_SIGNAL_INFO;
            signalInfo.semaphore = semaphore;
            signalInfo.value = 1;
            hostSignalResult = VulkanApi.vkSignalSemaphore(device.DeviceHandle, &signalInfo);
            gateReleased = hostSignalResult == VkResult.VK_SUCCESS;
        }
        finally
        {
            if (submissionAccepted && !gateReleased)
            {
                var signalInfo = default(VkSemaphoreSignalInfo);
                signalInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_SEMAPHORE_SIGNAL_INFO;
                signalInfo.semaphore = semaphore;
                signalInfo.value = 1;
                cleanupSignalResult = VulkanApi.vkSignalSemaphore(device.DeviceHandle, &signalInfo);
                gateReleased = cleanupSignalResult == VkResult.VK_SUCCESS;
            }

            if (submissionAccepted && gateReleased)
            {
                drainResult = VulkanApi.vkWaitForFences(
                    device.DeviceHandle,
                    fenceCount: 1,
                    &fence,
                    waitAll: 1,
                    timeout: 1_000_000_000);
            }

            if (!submissionAccepted || (gateReleased && (drainResult == VkResult.VK_SUCCESS)))
            {
                if (hasFence)
                {
                    VulkanApi.vkDestroyFence(device.DeviceHandle, fence, null);
                }

                if (hasSemaphore)
                {
                    VulkanApi.vkDestroySemaphore(device.DeviceHandle, semaphore, null);
                }
            }
        }

        Assert.That(pendingWaitResult, Is.EqualTo(VkResult.VK_TIMEOUT));
        Assert.That(hostSignalResult, Is.EqualTo(VkResult.VK_SUCCESS));
        Assert.That(cleanupSignalResult, Is.EqualTo(VkResult.VK_SUCCESS));
        Assert.That(drainResult, Is.EqualTo(VkResult.VK_SUCCESS));
        TestContext.WriteLine("The host-gated Vulkan queue submission returned VK_TIMEOUT while blocked and completed after host signal.");
    }
}
