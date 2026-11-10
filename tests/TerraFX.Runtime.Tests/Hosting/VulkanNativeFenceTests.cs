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
}
