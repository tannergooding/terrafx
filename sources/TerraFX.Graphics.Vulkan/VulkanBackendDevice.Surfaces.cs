// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;
using TerraFX.Graphics;
using TerraFX.Interop.Vulkan;
using VulkanApi = TerraFX.Interop.Vulkan.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal sealed unsafe partial class VulkanBackendDevice
{
    private int _liveSurfaceCount;

    internal VkDevice DeviceHandle => _device;

    internal uint GraphFrameSlotCount => checked((uint)_graphFrameSlotCount);

    internal VkQueue GraphicsQueue => _queue;

    internal bool HasUncertainSubmission => _hasUncertainSubmission;

    internal VkInstance InstanceHandle => _instance;

    internal VkPhysicalDevice PhysicalDeviceHandle => _physicalDevice;

    internal GraphicsPlatformKind PlatformKind => _platformKind;

    internal uint QueueFamilyIndex => _queueFamilyIndex;

    internal Func<VkResult?>? SurfaceAcquireResultOverride => _surfaceAcquireResultOverride;

    internal Func<VkResult, VkResult>? SurfacePresentResultOverride => _surfacePresentResultOverride;

    internal string? SwapchainMaintenanceExtensionName => _swapchainMaintenanceExtensionName;

    internal IGraphicsBackendSurface CreateSurface(GraphicsNativeSurfaceBinding binding, PixelSize clientPixelSize)
    {
        EnsureOwnerThread();
        EnsureActive();

        if (Capabilities.Profile != GraphicsDeviceProfile.Interactive)
        {
            throw new InvalidOperationException("OffscreenNative Vulkan devices do not create presentation surfaces.");
        }

        if (binding.PlatformKind != _platformKind)
        {
            throw new ArgumentException("The native binding does not match the initialized Vulkan WSI platform.", nameof(binding));
        }

        var surface = VulkanBackendSurface.Create(this, binding, clientPixelSize);
        _liveSurfaceCount = checked(_liveSurfaceCount + 1);
        return surface;
    }

    internal void MarkUncertainWsiOperation()
        => _hasUncertainSubmission = true;

    internal void RetireSurface()
    {
        EnsureOwnerThread();

        if (_liveSurfaceCount <= 0)
        {
            throw new InvalidOperationException("Vulkan surface ownership is unbalanced.");
        }

        _liveSurfaceCount--;
    }

    internal VkQueue SelectPresentQueue(VkSurfaceKHR surface, out uint queueFamilyIndex)
    {
        EnsureOwnerThread();

        for (var index = 0; index < _queueFamilyIndices.Length; index++)
        {
            uint supported = 0;
            ThrowIfFailed(
                VulkanApi.vkGetPhysicalDeviceSurfaceSupportKHR(
                    _physicalDevice,
                    _queueFamilyIndices[index],
                    surface,
                    &supported),
                "vkGetPhysicalDeviceSurfaceSupportKHR");

            if (supported != 0)
            {
                queueFamilyIndex = _queueFamilyIndices[index];
                return _queues[index];
            }
        }

        throw new NotSupportedException("No enabled Vulkan queue family supports presentation to the native surface.");
    }

    IGraphicsBackendSurface IGraphicsBackendDevice.CreateSurface(
        GraphicsNativeSurfaceBinding binding,
        PixelSize clientPixelSize)
        => CreateSurface(binding, clientPixelSize);
}
