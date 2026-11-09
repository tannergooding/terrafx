// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using TerraFX;
using TerraFX.Graphics;
using TerraFX.Interop.Vulkan;
using VulkanApi = TerraFX.Interop.Vulkan.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal sealed unsafe class VulkanBackendSurface : IGraphicsBackendSurface
{
    private const ulong AcquireTimeoutNanoseconds = 5_000_000_000;
    private const ulong FenceWaitSliceNanoseconds = 16_000_000;

    private readonly VkSemaphore[] _acquireSemaphores;
    private readonly GraphicsNativeSurfaceBinding _binding;
    private SurfaceCapabilities _capabilities;
    private PixelSize _clientPixelSize;
    private readonly TextureFormat _colorFormat;
    private readonly VkColorSpaceKHR _colorSpace;
    private readonly VulkanBackendDevice _device;
    private readonly VkFormat _nativeFormat;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly VkQueue _presentQueue;
    private readonly uint _presentQueueFamilyIndex;
    private readonly VkSurfaceKHR _surface;
    private readonly Lock _presentationGate = new();
    private readonly Dictionary<long, PendingPresentation> _pendingPresentations = [];
    private readonly List<VkFence> _uncertainPresentFences = [];
    private readonly List<(
        long FirstSequence,
        long LastSequence,
        uint SurfaceEpoch,
        uint SwapchainEpoch,
        PresentationStatus Status)> _presentationHistory = [];
    private bool[] _acquiredImages = [];
    private VkImage[] _images = [];
    private bool[] _imageHasPresented = [];
    private long[] _imagePresentationSequences = [];
    private bool _isDisposed;
    private bool _isSurfaceLost;
    private uint _lastSwapchainEpoch;
    private bool _needsSwapchainRecreate;
    private long _nextPresentationSequence;
    private VkSemaphore[] _renderFinishedSemaphores;
    private VkSwapchainKHR _swapchain;

    private VulkanBackendSurface(
        VulkanBackendDevice device,
        GraphicsNativeSurfaceBinding binding,
        VkSurfaceKHR surface,
        VkQueue presentQueue,
        uint presentQueueFamilyIndex,
        VkFormat nativeFormat,
        VkColorSpaceKHR colorSpace,
        TextureFormat colorFormat,
        SurfaceCapabilities capabilities,
        PixelSize clientPixelSize,
        VkSemaphore[] acquireSemaphores,
        VkSwapchainKHR swapchain,
        VkImage[] images,
        VkSemaphore[] renderFinishedSemaphores)
    {
        _device = device;
        _binding = binding;
        _surface = surface;
        _presentQueue = presentQueue;
        _presentQueueFamilyIndex = presentQueueFamilyIndex;
        _nativeFormat = nativeFormat;
        _colorSpace = colorSpace;
        _colorFormat = colorFormat;
        _capabilities = capabilities;
        _clientPixelSize = clientPixelSize;
        _acquireSemaphores = acquireSemaphores;
        _swapchain = swapchain;
        _images = images;
        _acquiredImages = new bool[images.Length];
        _imageHasPresented = new bool[images.Length];
        _renderFinishedSemaphores = renderFinishedSemaphores;
        _imagePresentationSequences = new long[images.Length];
        _lastSwapchainEpoch = capabilities.SwapchainEpoch;
    }

    public SurfaceCapabilities Capabilities => _capabilities;

    public PixelSize ClientPixelSize => _clientPixelSize;

    public TextureFormat ColorFormat => _colorFormat;

    internal static VulkanBackendSurface Create(
        VulkanBackendDevice device,
        GraphicsNativeSurfaceBinding binding,
        PixelSize clientPixelSize)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            clientPixelSize.Width,
            device.Capabilities.Limits.MaximumTextureDimension2D,
            nameof(clientPixelSize));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            clientPixelSize.Height,
            device.Capabilities.Limits.MaximumTextureDimension2D,
            nameof(clientPixelSize));

        var surface = CreateNativeSurface(device.InstanceHandle, binding);
        VkSemaphore[] acquireSemaphores = [];
        VkSwapchainKHR swapchain = default;
        VkImage[] images = [];
        VkSemaphore[] renderFinishedSemaphores = [];

        try
        {
            var presentQueue = device.SelectPresentQueue(surface, out var presentQueueFamilyIndex);
            var nativeCapabilities = QueryNativeCapabilities(device.PhysicalDeviceHandle, surface);
            var (nativeFormat, colorSpace, colorFormat) = SelectFormat(device.Capabilities, nativeCapabilities.Formats);
            var epoch = IsSuspended(clientPixelSize) ? 0u : 1u;
            var capabilities = CreateCapabilities(
                binding.SurfaceEpoch,
                epoch,
                nativeCapabilities);
            acquireSemaphores = CreateSemaphores(device.DeviceHandle, checked((int)device.GraphFrameSlotCount));

            if (epoch != 0)
            {
                var swapchainData = CreateSwapchain(
                    device,
                    surface,
                    clientPixelSize,
                    nativeCapabilities,
                    nativeFormat,
                    colorSpace,
                    presentQueueFamilyIndex,
                    oldSwapchain: default);
                swapchain = swapchainData.Swapchain;
                images = swapchainData.Images;
                renderFinishedSemaphores = swapchainData.RenderFinishedSemaphores;
            }

            var backendSurface = new VulkanBackendSurface(
                device,
                binding,
                surface,
                presentQueue,
                presentQueueFamilyIndex,
                nativeFormat,
                colorSpace,
                colorFormat,
                capabilities,
                clientPixelSize,
                acquireSemaphores,
                swapchain,
                images,
                renderFinishedSemaphores);
            surface = default;
            acquireSemaphores = [];
            swapchain = default;
            images = [];
            renderFinishedSemaphores = [];
            return backendSurface;
        }
        catch
        {
            DestroySemaphores(device.DeviceHandle, renderFinishedSemaphores);
            DestroySwapchain(device.DeviceHandle, swapchain);
            DestroySemaphores(device.DeviceHandle, acquireSemaphores);
            VulkanApi.vkDestroySurfaceKHR(device.InstanceHandle, surface, null);
            throw;
        }
    }

    internal static VkSurfaceKHR CreateNativeSurface(
        VkInstance instance,
        GraphicsNativeSurfaceBinding binding)
        => CreateNativeSurfaceCore(instance, binding);

    internal static bool TryValidatePhysicalDeviceSurface(
        VkPhysicalDevice physicalDevice,
        VkSurfaceKHR surface,
        GraphicsCapabilities deviceCapabilities,
        uint graphicsQueueFamilyIndex,
        out uint presentQueueFamilyIndex,
        out string rejection)
        => TryValidatePhysicalDeviceSurfaceCore(
            physicalDevice,
            surface,
            deviceCapabilities,
            graphicsQueueFamilyIndex,
            out presentQueueFamilyIndex,
            out rejection);

    internal static void ValidateDeviceWsiEntrypoints(
        VkDevice device,
        string? swapchainMaintenanceExtensionName)
    {
        var releaseImagesName = swapchainMaintenanceExtensionName switch {
            "VK_KHR_swapchain_maintenance1" => "vkReleaseSwapchainImagesKHR",
            "VK_EXT_swapchain_maintenance1" => "vkReleaseSwapchainImagesEXT",
            _ => throw new InvalidOperationException("Vulkan device admission has no swapchain maintenance extension."),
        };
        string[] requiredEntrypoints = [
            "vkCreateSwapchainKHR",
            "vkDestroySwapchainKHR",
            "vkGetSwapchainImagesKHR",
            "vkAcquireNextImageKHR",
            "vkQueuePresentKHR",
            releaseImagesName,
        ];

        foreach (var entrypoint in requiredEntrypoints)
        {
            if (GetDeviceProc(device, entrypoint) == null)
            {
                throw new NotSupportedException($"The Vulkan device did not expose required WSI entrypoint {entrypoint}.");
            }
        }
    }

    internal static void ValidateInstanceWsiEntrypoints(
        VkInstance instance,
        GraphicsPlatformKind platformKind)
    {
        var createSurfaceName = platformKind switch {
            GraphicsPlatformKind.Win32 => "vkCreateWin32SurfaceKHR",
            GraphicsPlatformKind.Wayland => "vkCreateWaylandSurfaceKHR",
            GraphicsPlatformKind.X11 => "vkCreateXlibSurfaceKHR",
            _ => throw new PlatformNotSupportedException("The selected platform has no Vulkan WSI integration."),
        };
        string[] requiredEntrypoints = [
            createSurfaceName,
            "vkDestroySurfaceKHR",
            "vkGetPhysicalDeviceSurfaceSupportKHR",
            "vkGetPhysicalDeviceSurfaceCapabilitiesKHR",
            "vkGetPhysicalDeviceSurfaceFormatsKHR",
            "vkGetPhysicalDeviceSurfacePresentModesKHR",
            "vkGetPhysicalDeviceSurfaceCapabilities2KHR",
            "vkGetPhysicalDeviceSurfaceFormats2KHR",
        ];

        foreach (var entrypoint in requiredEntrypoints)
        {
            if (GetInstanceProc(instance, entrypoint) == null)
            {
                throw new NotSupportedException($"The Vulkan loader did not expose required WSI entrypoint {entrypoint}.");
            }
        }
    }

    private static SurfaceCapabilities CreateCapabilities(
        uint surfaceEpoch,
        uint swapchainEpoch,
        VulkanSurfaceNativeCapabilities nativeCapabilities)
    {
        var formats = ImmutableArray.CreateBuilder<SurfaceFormat>(nativeCapabilities.Formats.Length);

        foreach (var nativeFormat in nativeCapabilities.Formats)
        {
            if (nativeFormat.colorSpace != VkColorSpaceKHR.VK_COLOR_SPACE_SRGB_NONLINEAR_KHR)
            {
                continue;
            }

            if (nativeFormat.format == VkFormat.VK_FORMAT_UNDEFINED)
            {
                formats.Add(new SurfaceFormat(TextureFormat.Bgra8Srgb, SurfaceColorSpace.SrgbNonlinear));
                continue;
            }

            if (TryGetTextureFormat(nativeFormat.format, out var format))
            {
                formats.Add(new SurfaceFormat(format, SurfaceColorSpace.SrgbNonlinear));
            }
        }

        var presentModes = ImmutableArray.CreateBuilder<GraphicsPresentMode>(nativeCapabilities.PresentModes.Length);

        foreach (var nativePresentMode in nativeCapabilities.PresentModes)
        {
            var presentMode = nativePresentMode switch {
                VkPresentModeKHR.VK_PRESENT_MODE_FIFO_KHR => GraphicsPresentMode.Fifo,
                VkPresentModeKHR.VK_PRESENT_MODE_MAILBOX_KHR => GraphicsPresentMode.Mailbox,
                VkPresentModeKHR.VK_PRESENT_MODE_IMMEDIATE_KHR => GraphicsPresentMode.Immediate,
                _ => GraphicsPresentMode.None,
            };

            if ((presentMode != GraphicsPresentMode.None) && !presentModes.Contains(presentMode))
            {
                presentModes.Add(presentMode);
            }
        }

        if (formats.Count == 0)
        {
            throw new NotSupportedException("The Vulkan surface exposes no format representable by the current Graphics API.");
        }

        if (presentModes.Count == 0)
        {
            throw new NotSupportedException("The Vulkan surface exposes no present mode representable by the current Graphics API.");
        }

        var surfaceCapabilities = nativeCapabilities.Capabilities;
        return new SurfaceCapabilities(
            formats.ToImmutable(),
            presentModes.ToImmutable(),
            new PixelSize(
                checked((int)surfaceCapabilities.minImageExtent.width),
                checked((int)surfaceCapabilities.minImageExtent.height)),
            new PixelSize(
                checked((int)surfaceCapabilities.maxImageExtent.width),
                checked((int)surfaceCapabilities.maxImageExtent.height)),
            checked((int)surfaceCapabilities.minImageCount),
            surfaceCapabilities.maxImageCount == 0 ? null : checked((int)surfaceCapabilities.maxImageCount),
            presentModes.Contains(GraphicsPresentMode.Immediate),
            surfaceEpoch,
            swapchainEpoch);
    }

    private static VkSurfaceKHR CreateNativeSurfaceCore(
        VkInstance instance,
        GraphicsNativeSurfaceBinding binding)
    {
        var surface = default(VkSurfaceKHR);

        switch (binding.PlatformKind)
        {
            case GraphicsPlatformKind.Win32:
            {
                var createInfo = default(VkWin32SurfaceCreateInfoKHR);
                createInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_WIN32_SURFACE_CREATE_INFO_KHR;
                createInfo.hinstance = (void*)binding.NativeDisplayOrInstance;
                createInfo.hwnd = (void*)binding.NativeWindowOrSurface;
                var createSurface = (delegate* unmanaged<
                    VkInstance,
                    VkWin32SurfaceCreateInfoKHR*,
                    VkAllocationCallbacks*,
                    VkSurfaceKHR*,
                    VkResult>)GetInstanceProc(instance, "vkCreateWin32SurfaceKHR");

                if (createSurface == null)
                {
                    throw new NotSupportedException("The Vulkan loader did not expose vkCreateWin32SurfaceKHR.");
                }

                EnsureSuccess(createSurface(instance, &createInfo, null, &surface), "vkCreateWin32SurfaceKHR");
                break;
            }

            case GraphicsPlatformKind.Wayland:
            {
                var createInfo = default(VkWaylandSurfaceCreateInfoKHR);
                createInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_WAYLAND_SURFACE_CREATE_INFO_KHR;
                createInfo.display = (void*)binding.NativeDisplayOrInstance;
                createInfo.surface = (void*)binding.NativeWindowOrSurface;
                var createSurface = (delegate* unmanaged<
                    VkInstance,
                    VkWaylandSurfaceCreateInfoKHR*,
                    VkAllocationCallbacks*,
                    VkSurfaceKHR*,
                    VkResult>)GetInstanceProc(instance, "vkCreateWaylandSurfaceKHR");

                if (createSurface == null)
                {
                    throw new NotSupportedException("The Vulkan loader did not expose vkCreateWaylandSurfaceKHR.");
                }

                EnsureSuccess(createSurface(instance, &createInfo, null, &surface), "vkCreateWaylandSurfaceKHR");
                break;
            }

            case GraphicsPlatformKind.X11:
            {
                var createInfo = default(VkXlibSurfaceCreateInfoKHR);
                createInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_XLIB_SURFACE_CREATE_INFO_KHR;
                createInfo.dpy = (void*)binding.NativeDisplayOrInstance;
                createInfo.window = unchecked((nuint)binding.NativeWindowOrSurface);
                var createSurface = (delegate* unmanaged<
                    VkInstance,
                    VkXlibSurfaceCreateInfoKHR*,
                    VkAllocationCallbacks*,
                    VkSurfaceKHR*,
                    VkResult>)GetInstanceProc(instance, "vkCreateXlibSurfaceKHR");

                if (createSurface == null)
                {
                    throw new NotSupportedException("The Vulkan loader did not expose vkCreateXlibSurfaceKHR.");
                }

                EnsureSuccess(createSurface(instance, &createInfo, null, &surface), "vkCreateXlibSurfaceKHR");
                break;
            }

            default:
            {
                throw new PlatformNotSupportedException("The native binding has no Vulkan WSI surface implementation.");
            }
        }

        return surface;
    }

    private static Diagnostic? CreatePresentationDiagnostic(
        PresentationStatus status,
        uint deviceEpoch,
        uint surfaceEpoch,
        uint swapchainEpoch)
    {
        if (status is PresentationStatus.Presented or PresentationStatus.Occluded or PresentationStatus.Suspended)
        {
            return null;
        }

        var code = status switch {
            PresentationStatus.DeviceLost => "graphics.presentation.device-lost",
            PresentationStatus.SurfaceLost => "graphics.presentation.surface-lost",
            PresentationStatus.SwapchainLost => "graphics.presentation.swapchain-lost",
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
        return new Diagnostic(
            code,
            DiagnosticSeverity.Error,
            Stopwatch.GetTimestamp(),
            hostId: null,
            worldGuid: null,
            deviceId: null,
            surfaceId: null,
            deviceEpoch,
            surfaceEpoch,
            swapchainEpoch,
            tick: null,
            systemId: null,
            passId: null,
            assetId: null,
            commandIdentity: null,
            operation: "Surface.WaitForPresentation",
            nativeResult: null,
            message: $"Vulkan presentation ended with {status}.",
            context: []);
    }

    private static VkSemaphore[] CreateSemaphores(VkDevice device, int count)
    {
        var semaphores = new VkSemaphore[count];
        var createInfo = default(VkSemaphoreCreateInfo);
        createInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO;

        try
        {
            fixed (VkSemaphore* semaphoresPointer = semaphores)
            {
                for (var index = 0; index < semaphores.Length; index++)
                {
                    EnsureSuccess(
                        VulkanApi.vkCreateSemaphore(device, &createInfo, null, &semaphoresPointer[index]),
                        "vkCreateSemaphore");
                }
            }

            return semaphores;
        }
        catch
        {
            DestroySemaphores(device, semaphores);
            throw;
        }
    }

    private static VulkanSwapchainData CreateSwapchain(
        VulkanBackendDevice device,
        VkSurfaceKHR surface,
        PixelSize clientPixelSize,
        VulkanSurfaceNativeCapabilities nativeCapabilities,
        VkFormat nativeFormat,
        VkColorSpaceKHR colorSpace,
        uint presentQueueFamilyIndex,
        VkSwapchainKHR oldSwapchain)
    {
        var capabilities = nativeCapabilities.Capabilities;

        if ((capabilities.supportedUsageFlags & VkImageUsageFlags.VK_IMAGE_USAGE_TRANSFER_DST_BIT) == 0)
        {
            throw new NotSupportedException("The Vulkan surface swapchain images do not support transfer-destination usage.");
        }

        var imageExtent = GetSwapchainExtent(capabilities, clientPixelSize);

        if ((imageExtent.width == 0) || (imageExtent.height == 0))
        {
            throw new InvalidOperationException("Vulkan cannot create a swapchain for a zero physical extent.");
        }

        if ((imageExtent.width != clientPixelSize.Width)
            || (imageExtent.height != clientPixelSize.Height))
        {
            throw new NotSupportedException("The Vulkan surface extent does not match the observed physical client extent.");
        }

        var imageCount = checked(capabilities.minImageCount + 1);

        if ((capabilities.maxImageCount != 0) && (imageCount > capabilities.maxImageCount))
        {
            imageCount = capabilities.maxImageCount;
        }

        var queueFamilyIndices = stackalloc uint[2];
        queueFamilyIndices[0] = device.QueueFamilyIndex;
        queueFamilyIndices[1] = presentQueueFamilyIndex;
        var createInfo = default(VkSwapchainCreateInfoKHR);
        createInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR;
        createInfo.surface = surface;
        createInfo.minImageCount = imageCount;
        createInfo.imageFormat = nativeFormat;
        createInfo.imageColorSpace = colorSpace;
        createInfo.imageExtent = imageExtent;
        createInfo.imageArrayLayers = 1;
        createInfo.imageUsage = VkImageUsageFlags.VK_IMAGE_USAGE_TRANSFER_DST_BIT;
        createInfo.imageSharingMode = (presentQueueFamilyIndex == device.QueueFamilyIndex)
            ? VkSharingMode.VK_SHARING_MODE_EXCLUSIVE
            : VkSharingMode.VK_SHARING_MODE_CONCURRENT;
        createInfo.queueFamilyIndexCount = (presentQueueFamilyIndex == device.QueueFamilyIndex) ? 0u : 2u;
        createInfo.pQueueFamilyIndices = (presentQueueFamilyIndex == device.QueueFamilyIndex) ? null : queueFamilyIndices;
        createInfo.preTransform = capabilities.currentTransform;
        createInfo.compositeAlpha = SelectCompositeAlpha(capabilities.supportedCompositeAlpha);
        createInfo.presentMode = VkPresentModeKHR.VK_PRESENT_MODE_FIFO_KHR;
        createInfo.clipped = 1;
        createInfo.oldSwapchain = oldSwapchain;
        var swapchain = default(VkSwapchainKHR);
        EnsureSuccess(
            VulkanApi.vkCreateSwapchainKHR(device.DeviceHandle, &createInfo, null, &swapchain),
            "vkCreateSwapchainKHR");

        try
        {
            uint imageCountActual = 0;
            EnsureSuccess(
                VulkanApi.vkGetSwapchainImagesKHR(device.DeviceHandle, swapchain, &imageCountActual, null),
                "vkGetSwapchainImagesKHR");

            if ((imageCountActual < capabilities.minImageCount)
                || (imageCountActual == 0)
                || ((capabilities.maxImageCount != 0) && (imageCountActual > capabilities.maxImageCount)))
            {
                throw new InvalidOperationException("Vulkan returned a swapchain image count outside the queried surface limits.");
            }

            var images = new VkImage[checked((int)imageCountActual)];

            fixed (VkImage* imagesPointer = images)
            {
                EnsureSuccess(
                    VulkanApi.vkGetSwapchainImagesKHR(
                        device.DeviceHandle,
                        swapchain,
                        &imageCountActual,
                        imagesPointer),
                    "vkGetSwapchainImagesKHR");
            }

            if (imageCountActual != images.Length)
            {
                throw new InvalidOperationException("The Vulkan swapchain image count changed during enumeration.");
            }

            var renderFinishedSemaphores = CreateSemaphores(device.DeviceHandle, images.Length);
            return new VulkanSwapchainData(swapchain, images, renderFinishedSemaphores);
        }
        catch
        {
            VulkanApi.vkDestroySwapchainKHR(device.DeviceHandle, swapchain, null);

            if (oldSwapchain.Value != 0)
            {
                device.MarkUncertainWsiOperation();
            }

            throw;
        }
    }

    private static void DestroySemaphores(VkDevice device, VkSemaphore[] semaphores)
    {
        foreach (var semaphore in semaphores)
        {
            if (semaphore.Value != 0)
            {
                VulkanApi.vkDestroySemaphore(device, semaphore, null);
            }
        }
    }

    private static void DestroySwapchain(VkDevice device, VkSwapchainKHR swapchain)
    {
        if (swapchain.Value != 0)
        {
            VulkanApi.vkDestroySwapchainKHR(device, swapchain, null);
        }
    }

    private static void DestroySwapchainState(
        VkDevice device,
        VkSwapchainKHR swapchain,
        VkSemaphore[] renderFinishedSemaphores)
    {
        DestroySemaphores(device, renderFinishedSemaphores);
        DestroySwapchain(device, swapchain);
    }

    private static void EnsureSuccess(VkResult result, string operation)
    {
        if (result != VkResult.VK_SUCCESS)
        {
            if (result == VkResult.VK_ERROR_DEVICE_LOST)
            {
                throw new GraphicsDeviceLostException((int)result);
            }

            throw new InvalidOperationException($"{operation} failed with VkResult {result} ({(int)result}).");
        }
    }

    private static delegate* unmanaged<void> GetDeviceProc(VkDevice device, string name)
    {
        var nameLength = checked(Encoding.UTF8.GetByteCount(name) + 1);
        var nameStorage = stackalloc byte[nameLength];
        var written = Encoding.UTF8.GetBytes(name.AsSpan(), new Span<byte>(nameStorage, nameLength));
        nameStorage[written] = 0;
        return VulkanApi.vkGetDeviceProcAddr(device, (sbyte*)nameStorage);
    }

    private static delegate* unmanaged<void> GetInstanceProc(VkInstance instance, string name)
    {
        var nameLength = checked(Encoding.UTF8.GetByteCount(name) + 1);
        var nameStorage = stackalloc byte[nameLength];
        var written = Encoding.UTF8.GetBytes(name.AsSpan(), new Span<byte>(nameStorage, nameLength));
        nameStorage[written] = 0;
        return VulkanApi.vkGetInstanceProcAddr(instance, (sbyte*)nameStorage);
    }

    private static VkExtent2D GetSwapchainExtent(
        VkSurfaceCapabilitiesKHR capabilities,
        PixelSize clientPixelSize)
    {
        if ((capabilities.currentExtent.width != uint.MaxValue)
            && (capabilities.currentExtent.height != uint.MaxValue))
        {
            return capabilities.currentExtent;
        }

        return new VkExtent2D {
            width = checked((uint)Math.Clamp(
                clientPixelSize.Width,
                checked((int)capabilities.minImageExtent.width),
                checked((int)capabilities.maxImageExtent.width))),
            height = checked((uint)Math.Clamp(
                clientPixelSize.Height,
                checked((int)capabilities.minImageExtent.height),
                checked((int)capabilities.maxImageExtent.height))),
        };
    }

    private static bool IsSuspended(PixelSize clientPixelSize)
        => (clientPixelSize.Width == 0) || (clientPixelSize.Height == 0);

    private static VulkanSurfaceNativeCapabilities QueryNativeCapabilities(
        VkPhysicalDevice physicalDevice,
        VkSurfaceKHR surface)
    {
        var capabilities = default(VkSurfaceCapabilitiesKHR);
        EnsureSuccess(
            VulkanApi.vkGetPhysicalDeviceSurfaceCapabilitiesKHR(
                physicalDevice,
                surface,
                &capabilities),
            "vkGetPhysicalDeviceSurfaceCapabilitiesKHR");
        uint formatCount = 0;
        EnsureSuccess(
            VulkanApi.vkGetPhysicalDeviceSurfaceFormatsKHR(
                physicalDevice,
                surface,
                &formatCount,
                null),
            "vkGetPhysicalDeviceSurfaceFormatsKHR");

        if (formatCount == 0)
        {
            throw new NotSupportedException("The Vulkan surface reports no presentation formats.");
        }

        var formats = new VkSurfaceFormatKHR[checked((int)formatCount)];

        fixed (VkSurfaceFormatKHR* formatsPointer = formats)
        {
            EnsureSuccess(
                VulkanApi.vkGetPhysicalDeviceSurfaceFormatsKHR(
                    physicalDevice,
                    surface,
                    &formatCount,
                    formatsPointer),
                "vkGetPhysicalDeviceSurfaceFormatsKHR");
        }

        if (formatCount != formats.Length)
        {
            Array.Resize(ref formats, checked((int)formatCount));
        }

        uint presentModeCount = 0;
        EnsureSuccess(
            VulkanApi.vkGetPhysicalDeviceSurfacePresentModesKHR(
                physicalDevice,
                surface,
                &presentModeCount,
                null),
            "vkGetPhysicalDeviceSurfacePresentModesKHR");

        if (presentModeCount == 0)
        {
            throw new NotSupportedException("The Vulkan surface reports no presentation modes.");
        }

        var presentModes = new VkPresentModeKHR[checked((int)presentModeCount)];

        fixed (VkPresentModeKHR* presentModesPointer = presentModes)
        {
            EnsureSuccess(
                VulkanApi.vkGetPhysicalDeviceSurfacePresentModesKHR(
                    physicalDevice,
                    surface,
                    &presentModeCount,
                    presentModesPointer),
                "vkGetPhysicalDeviceSurfacePresentModesKHR");
        }

        if (presentModeCount != presentModes.Length)
        {
            Array.Resize(ref presentModes, checked((int)presentModeCount));
        }

        return new VulkanSurfaceNativeCapabilities(capabilities, formats, presentModes);
    }

    private static VkCompositeAlphaFlagsKHR SelectCompositeAlpha(VkCompositeAlphaFlagsKHR supportedCompositeAlpha)
    {
        const VkCompositeAlphaFlagsKHR opaque = VkCompositeAlphaFlagsKHR.VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR;

        if ((supportedCompositeAlpha & opaque) != 0)
        {
            return opaque;
        }

        var supportedBits = (uint)supportedCompositeAlpha;

        if (supportedBits == 0)
        {
            throw new NotSupportedException("The Vulkan surface exposes no supported composite-alpha mode.");
        }

        var selectedBit = 1u;

        while ((supportedBits & selectedBit) == 0)
        {
            selectedBit <<= 1;
        }

        return (VkCompositeAlphaFlagsKHR)selectedBit;
    }

    private static (
        VkFormat NativeFormat,
        VkColorSpaceKHR ColorSpace,
        TextureFormat ColorFormat) SelectFormat(
            GraphicsCapabilities deviceCapabilities,
            VkSurfaceFormatKHR[] formats)
    {
        var publicFormats = new List<(VkFormat NativeFormat, VkColorSpaceKHR ColorSpace, TextureFormat Format)>(formats.Length);
        var hasUndefinedFormat = formats.Length == 1
            && (formats[0].format == VkFormat.VK_FORMAT_UNDEFINED);

        if (hasUndefinedFormat)
        {
            var colorSpace = formats[0].colorSpace;
            AddFormat(VkFormat.VK_FORMAT_B8G8R8A8_SRGB, colorSpace);
            AddFormat(VkFormat.VK_FORMAT_R8G8B8A8_SRGB, colorSpace);
        }
        else
        {
            foreach (var format in formats)
            {
                AddFormat(format.format, format.colorSpace);
            }
        }

        var selected = publicFormats.Find(static entry =>
            (entry.Format == TextureFormat.Bgra8Srgb)
            && (entry.ColorSpace == VkColorSpaceKHR.VK_COLOR_SPACE_SRGB_NONLINEAR_KHR));

        if (selected.Format == TextureFormat.None)
        {
            selected = publicFormats.Find(static entry =>
                (entry.Format == TextureFormat.Rgba8Srgb)
                && (entry.ColorSpace == VkColorSpaceKHR.VK_COLOR_SPACE_SRGB_NONLINEAR_KHR));
        }

        if (selected.Format == TextureFormat.None)
        {
            throw new NotSupportedException("The Vulkan surface has no sRGB format supported for color attachment and copy-source output.");
        }

        return (selected.NativeFormat, selected.ColorSpace, selected.Format);

        void AddFormat(VkFormat nativeFormat, VkColorSpaceKHR colorSpace)
        {
            if (colorSpace != VkColorSpaceKHR.VK_COLOR_SPACE_SRGB_NONLINEAR_KHR
                || !TryGetTextureFormat(nativeFormat, out var textureFormat))
            {
                return;
            }

            foreach (var capability in deviceCapabilities.Formats)
            {
                if ((capability.Format == textureFormat)
                    && ((capability.SupportedUsages & (TextureUsage.ColorAttachment | TextureUsage.CopySource))
                        == (TextureUsage.ColorAttachment | TextureUsage.CopySource)))
                {
                    publicFormats.Add((nativeFormat, colorSpace, textureFormat));
                    break;
                }
            }
        }
    }

    private static bool TryGetTextureFormat(VkFormat nativeFormat, out TextureFormat format)
    {
        format = nativeFormat switch {
            VkFormat.VK_FORMAT_R8G8B8A8_UNORM => TextureFormat.Rgba8Unorm,
            VkFormat.VK_FORMAT_R8G8B8A8_SRGB => TextureFormat.Rgba8Srgb,
            VkFormat.VK_FORMAT_B8G8R8A8_UNORM => TextureFormat.Bgra8Unorm,
            VkFormat.VK_FORMAT_B8G8R8A8_SRGB => TextureFormat.Bgra8Srgb,
            _ => TextureFormat.None,
        };
        return format != TextureFormat.None;
    }

    private static bool TryValidatePhysicalDeviceSurfaceCore(
        VkPhysicalDevice physicalDevice,
        VkSurfaceKHR surface,
        GraphicsCapabilities deviceCapabilities,
        uint graphicsQueueFamilyIndex,
        out uint presentQueueFamilyIndex,
        out string rejection)
    {
        uint count = 0;
        VulkanApi.vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, &count, null);
        var queueFamilies = stackalloc VkQueueFamilyProperties[checked((int)count)];
        VulkanApi.vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, &count, queueFamilies);
        var hasPresentationQueue = false;
        presentQueueFamilyIndex = 0;

        for (uint index = 0; index < count; index++)
        {
            if (queueFamilies[index].queueCount == 0)
            {
                continue;
            }

            uint supported = 0;
            EnsureSuccess(
                VulkanApi.vkGetPhysicalDeviceSurfaceSupportKHR(
                    physicalDevice,
                    index,
                    surface,
                    &supported),
                "vkGetPhysicalDeviceSurfaceSupportKHR");

            if (supported != 0)
            {
                if (!hasPresentationQueue || (index == graphicsQueueFamilyIndex))
                {
                    presentQueueFamilyIndex = index;
                    hasPresentationQueue = true;
                }

                if (index == graphicsQueueFamilyIndex)
                {
                    break;
                }
            }
        }

        if (!hasPresentationQueue)
        {
            rejection = "no queue family supports presentation to the startup surface";
            return false;
        }

        var nativeCapabilities = QueryNativeCapabilities(physicalDevice, surface);

        if ((nativeCapabilities.Capabilities.supportedUsageFlags & VkImageUsageFlags.VK_IMAGE_USAGE_TRANSFER_DST_BIT) == 0)
        {
            rejection = "the startup surface does not support transfer-destination swapchain images";
            return false;
        }

        if (!nativeCapabilities.PresentModes.Contains(VkPresentModeKHR.VK_PRESENT_MODE_FIFO_KHR))
        {
            rejection = "the startup surface does not support mandatory FIFO presentation";
            return false;
        }

        try
        {
            _ = SelectFormat(deviceCapabilities, nativeCapabilities.Formats);
        }
        catch (NotSupportedException exception)
        {
            rejection = exception.Message;
            return false;
        }

        rejection = string.Empty;
        return true;
    }

    public void Dispose()
    {
        EnsureOwnerThread();

        if (_isDisposed)
        {
            return;
        }

        if (_uncertainPresentFences.Count != 0)
        {
            throw new InvalidOperationException("The Vulkan surface cannot retire presentation fences with uncertain ownership.");
        }

        _ = DrainPendingPresentations();
        DestroySwapchainState(_device.DeviceHandle, _swapchain, _renderFinishedSemaphores);
        _renderFinishedSemaphores = [];
        DestroySemaphores(_device.DeviceHandle, _acquireSemaphores);
        VulkanApi.vkDestroySurfaceKHR(_device.InstanceHandle, _surface, null);
        _swapchain = default;
        _images = [];
        _acquiredImages = [];
        _imageHasPresented = [];
        _imagePresentationSequences = [];
        _isDisposed = true;
        _device.RetireSurface();
    }

    public SurfaceCapabilities Resize(PixelSize clientPixelSize)
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_isSurfaceLost)
        {
            throw new InvalidOperationException("The Vulkan native surface was lost and must be retired.");
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            clientPixelSize.Width,
            _device.Capabilities.Limits.MaximumTextureDimension2D,
            nameof(clientPixelSize));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            clientPixelSize.Height,
            _device.Capabilities.Limits.MaximumTextureDimension2D,
            nameof(clientPixelSize));
        _ = DrainPendingPresentations();
        var nativeCapabilities = QueryNativeCapabilities(_device.PhysicalDeviceHandle, _surface);
        var (nativeFormat, colorSpace, colorFormat) = SelectFormat(_device.Capabilities, nativeCapabilities.Formats);

        if ((nativeFormat != _nativeFormat)
            || (colorSpace != _colorSpace)
            || (colorFormat != _colorFormat))
        {
            throw new NotSupportedException("The Vulkan surface changed its selected output format during resize.");
        }

        if (IsSuspended(clientPixelSize))
        {
            var suspendedCapabilities = CreateCapabilities(
                _binding.SurfaceEpoch,
                swapchainEpoch: 0,
                nativeCapabilities);
            var suspendedSwapchain = _swapchain;
            var suspendedSemaphores = _renderFinishedSemaphores;
            _swapchain = default;
            _images = [];
            _acquiredImages = [];
            _imageHasPresented = [];
            _imagePresentationSequences = [];
            _renderFinishedSemaphores = [];
            _capabilities = suspendedCapabilities;
            _clientPixelSize = clientPixelSize;
            DestroySemaphores(_device.DeviceHandle, suspendedSemaphores);
            DestroySwapchain(_device.DeviceHandle, suspendedSwapchain);
            return _capabilities;
        }

        var nextSwapchainEpoch = checked(_lastSwapchainEpoch + 1);
        var nextCapabilities = CreateCapabilities(
            _binding.SurfaceEpoch,
            nextSwapchainEpoch,
            nativeCapabilities);
        var swapchainData = CreateSwapchain(
            _device,
            _surface,
            clientPixelSize,
            nativeCapabilities,
            _nativeFormat,
            _colorSpace,
            _presentQueueFamilyIndex,
            _swapchain);
        bool[] acquiredImages;
        bool[] imageHasPresented;
        long[] imagePresentationSequences;

        try
        {
            acquiredImages = new bool[swapchainData.Images.Length];
            imageHasPresented = new bool[swapchainData.Images.Length];
            imagePresentationSequences = new long[swapchainData.Images.Length];
        }
        catch
        {
            DestroySwapchainState(
                _device.DeviceHandle,
                swapchainData.Swapchain,
                swapchainData.RenderFinishedSemaphores);

            if (_swapchain.Value != 0)
            {
                _device.MarkUncertainWsiOperation();
            }

            throw;
        }

        var previousSwapchain = _swapchain;
        var previousRenderFinishedSemaphores = _renderFinishedSemaphores;
        _swapchain = swapchainData.Swapchain;
        _images = swapchainData.Images;
        _acquiredImages = acquiredImages;
        _imageHasPresented = imageHasPresented;
        _renderFinishedSemaphores = swapchainData.RenderFinishedSemaphores;
        _imagePresentationSequences = imagePresentationSequences;
        _lastSwapchainEpoch = nextSwapchainEpoch;
        _capabilities = nextCapabilities;
        _clientPixelSize = clientPixelSize;
        _needsSwapchainRecreate = false;
        DestroySemaphores(_device.DeviceHandle, previousRenderFinishedSemaphores);
        DestroySwapchain(_device.DeviceHandle, previousSwapchain);
        return _capabilities;
    }

    public bool TryGetCompletedPresentation(
        long presentationSequence,
        uint deviceEpoch,
        out PresentationReceipt? receipt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(presentationSequence);
        ArgumentOutOfRangeException.ThrowIfZero(deviceEpoch);

        lock (_presentationGate)
        {
            foreach (var (firstSequence, lastSequence, surfaceEpoch, swapchainEpoch, status) in _presentationHistory)
            {
                if ((presentationSequence < firstSequence)
                    || (presentationSequence > lastSequence))
                {
                    continue;
                }

                receipt = new PresentationReceipt(
                    status,
                    presentationSequence,
                    deviceEpoch,
                    surfaceEpoch,
                    swapchainEpoch,
                    CreatePresentationDiagnostic(status, deviceEpoch, surfaceEpoch, swapchainEpoch));
                return true;
            }
        }

        receipt = null;
        return false;
    }

    public PresentationReceipt WaitForPresentation(
        long presentationSequence,
        uint deviceEpoch,
        CancellationToken cancellationToken)
    {
        EnsureOwnerThread();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(presentationSequence);
        ArgumentOutOfRangeException.ThrowIfZero(deviceEpoch);
        cancellationToken.ThrowIfCancellationRequested();

        if (TryGetCompletedPresentation(presentationSequence, deviceEpoch, out var completedReceipt))
        {
            return completedReceipt
                ?? throw new InvalidOperationException("The Vulkan presentation history returned no receipt.");
        }

        if (!_pendingPresentations.TryGetValue(presentationSequence, out var presentation))
        {
            throw new InvalidOperationException("The Vulkan presentation sequence is not pending or complete.");
        }

        if (presentation.DeviceEpoch != deviceEpoch)
        {
            throw new ArgumentException("The presentation belongs to a different GraphicsDevice generation.", nameof(deviceEpoch));
        }

        WaitForFence(presentation.PresentFence, cancellationToken);
        _ = _pendingPresentations.Remove(presentationSequence);
        VulkanApi.vkDestroyFence(_device.DeviceHandle, presentation.PresentFence, null);
        var receipt = new PresentationReceipt(
            presentation.Status,
            presentationSequence,
            deviceEpoch,
            presentation.SurfaceEpoch,
            presentation.SwapchainEpoch,
            CreatePresentationDiagnostic(
                presentation.Status,
                deviceEpoch,
                presentation.SurfaceEpoch,
                presentation.SwapchainEpoch));
        AddPresentationHistory(receipt);
        return receipt;
    }

    public ImmutableArray<PresentationReceipt> WaitForPresentations(uint deviceEpoch)
    {
        EnsureOwnerThread();
        ArgumentOutOfRangeException.ThrowIfZero(deviceEpoch);
        return DrainPendingPresentations(deviceEpoch);
    }

    internal void AcquireNextImage(int frameSlotIndex, out uint imageIndex)
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_isSurfaceLost)
        {
            throw new InvalidOperationException("The Vulkan native surface was lost and must be retired.");
        }

        ObjectDisposedException.ThrowIf(_swapchain.Value == 0, this);
        ArgumentOutOfRangeException.ThrowIfNegative(frameSlotIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(frameSlotIndex, _acquireSemaphores.Length);

        if (_needsSwapchainRecreate)
        {
            _ = Resize(_clientPixelSize);
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var nativeImageIndex = 0u;
            VkResult result;
            var acquireResultOverride = _device.SurfaceAcquireResultOverride?.Invoke();

            if (acquireResultOverride is VkResult overriddenResult)
            {
                if (overriddenResult is VkResult.VK_SUCCESS or VkResult.VK_SUBOPTIMAL_KHR)
                {
                    throw new InvalidOperationException("The Vulkan acquire-result override must report a non-success result.");
                }

                result = overriddenResult;
            }
            else
            {
                result = VulkanApi.vkAcquireNextImageKHR(
                    _device.DeviceHandle,
                    _swapchain,
                    AcquireTimeoutNanoseconds,
                    _acquireSemaphores[frameSlotIndex],
                    default,
                    &nativeImageIndex);
            }

            if (result == VkResult.VK_ERROR_OUT_OF_DATE_KHR)
            {
                _ = Resize(_clientPixelSize);
                continue;
            }

            if (result == VkResult.VK_ERROR_SURFACE_LOST_KHR)
            {
                _isSurfaceLost = true;
                throw new InvalidOperationException("The Vulkan native surface was lost during image acquisition.");
            }

            if (result is not (VkResult.VK_SUCCESS or VkResult.VK_SUBOPTIMAL_KHR))
            {
                if (result is VkResult.VK_TIMEOUT or VkResult.VK_NOT_READY)
                {
                    throw new TimeoutException("Vulkan did not provide a swapchain image within the acquire timeout.");
                }

                EnsureSuccess(result, "vkAcquireNextImageKHR");
            }

            imageIndex = nativeImageIndex;

            if (imageIndex >= _images.Length)
            {
                _device.MarkUncertainWsiOperation();
                throw new InvalidOperationException("Vulkan acquired an image outside the current swapchain image set.");
            }

            _acquiredImages[imageIndex] = true;

            try
            {
                RetireImagePresentation(imageIndex);
            }
            catch
            {
                ReleaseAcquiredImage(frameSlotIndex, imageIndex);
                throw;
            }

            if (result == VkResult.VK_SUBOPTIMAL_KHR)
            {
                _needsSwapchainRecreate = true;
            }

            return;
        }

        throw new InvalidOperationException("Vulkan could not acquire an image after recreating an out-of-date swapchain.");
    }

    internal VkSemaphore GetAcquireSemaphore(int frameSlotIndex)
    {
        EnsureOwnerThread();
        ArgumentOutOfRangeException.ThrowIfNegative(frameSlotIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(frameSlotIndex, _acquireSemaphores.Length);
        return _acquireSemaphores[frameSlotIndex];
    }

    internal VkSemaphore GetRenderFinishedSemaphore(uint imageIndex)
    {
        EnsureOwnerThread();
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(imageIndex, checked((uint)_renderFinishedSemaphores.Length));
        return _renderFinishedSemaphores[imageIndex];
    }

    internal VkImage GetSwapchainImage(uint imageIndex)
    {
        EnsureOwnerThread();
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(imageIndex, checked((uint)_images.Length));
        return _images[imageIndex];
    }

    internal bool HasImageBeenPresented(uint imageIndex)
    {
        EnsureOwnerThread();
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(imageIndex, checked((uint)_imageHasPresented.Length));
        return _imageHasPresented[imageIndex];
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A result override runs after vkQueuePresentKHR may enqueue native work; failures must retain its fence.")]
    internal long Present(uint frameSlotIndex, uint imageIndex, uint deviceEpoch)
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if ((imageIndex >= _images.Length)
            || (imageIndex >= _renderFinishedSemaphores.Length)
            || (frameSlotIndex >= _acquireSemaphores.Length))
        {
            throw new ArgumentOutOfRangeException(nameof(imageIndex));
        }

        if (!_acquiredImages[imageIndex])
        {
            throw new InvalidOperationException("The Vulkan swapchain image has not been acquired for this presentation.");
        }

        _ = _pendingPresentations.EnsureCapacity(checked(_pendingPresentations.Count + 1));
        _ = _uncertainPresentFences.EnsureCapacity(checked(_uncertainPresentFences.Count + 1));
        var sequence = checked(_nextPresentationSequence + 1);
        var fenceCreateInfo = default(VkFenceCreateInfo);
        fenceCreateInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
        var presentFence = default(VkFence);
        EnsureSuccess(VulkanApi.vkCreateFence(_device.DeviceHandle, &fenceCreateInfo, null, &presentFence), "vkCreateFence");
        var presentFenceInfo = default(VkSwapchainPresentFenceInfoKHR);
        presentFenceInfo.sType = (_device.SwapchainMaintenanceExtensionName == "VK_KHR_swapchain_maintenance1")
            ? VkStructureType.VK_STRUCTURE_TYPE_SWAPCHAIN_PRESENT_FENCE_INFO_KHR
            : VkStructureType.VK_STRUCTURE_TYPE_SWAPCHAIN_PRESENT_FENCE_INFO_EXT;
        presentFenceInfo.swapchainCount = 1;
        presentFenceInfo.pFences = &presentFence;
        var waitSemaphore = _renderFinishedSemaphores[imageIndex];
        var swapchain = _swapchain;
        var presentInfo = default(VkPresentInfoKHR);
        presentInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_PRESENT_INFO_KHR;
        presentInfo.pNext = &presentFenceInfo;
        presentInfo.waitSemaphoreCount = 1;
        presentInfo.pWaitSemaphores = &waitSemaphore;
        presentInfo.swapchainCount = 1;
        presentInfo.pSwapchains = &swapchain;
        presentInfo.pImageIndices = &imageIndex;
        var nativeResult = VulkanApi.vkQueuePresentKHR(_presentQueue, &presentInfo);
        VkResult result;

        try
        {
            result = _device.SurfacePresentResultOverride?.Invoke(nativeResult) ?? nativeResult;
        }
        catch
        {
            _device.MarkUncertainWsiOperation();
            _uncertainPresentFences.Add(presentFence);
            throw;
        }

        PresentationStatus status;

        switch (result)
        {
            case VkResult.VK_SUCCESS:
            {
                status = PresentationStatus.Presented;
                break;
            }

            case VkResult.VK_SUBOPTIMAL_KHR:
            {
                status = PresentationStatus.Presented;
                _needsSwapchainRecreate = true;
                break;
            }

            case VkResult.VK_ERROR_OUT_OF_DATE_KHR:
            {
                status = PresentationStatus.SwapchainLost;
                _needsSwapchainRecreate = true;
                break;
            }

            case VkResult.VK_ERROR_SURFACE_LOST_KHR:
            {
                status = PresentationStatus.SurfaceLost;
                _isSurfaceLost = true;
                break;
            }

            default:
            {
                _device.MarkUncertainWsiOperation();
                _uncertainPresentFences.Add(presentFence);
                EnsureSuccess(result, "vkQueuePresentKHR");
                throw new InvalidOperationException("Vulkan presentation returned an unsupported terminal result.");
            }
        }

        _nextPresentationSequence = sequence;
        _pendingPresentations.Add(
            sequence,
            new PendingPresentation(
                presentFence,
                deviceEpoch,
                _binding.SurfaceEpoch,
                _capabilities.SwapchainEpoch,
                checked((int)imageIndex),
                status));
        _imagePresentationSequences[imageIndex] = sequence;
        _imageHasPresented[imageIndex] = status == PresentationStatus.Presented;
        _acquiredImages[imageIndex] = false;

        return sequence;
    }

    internal void ReleaseAcquiredImage(int frameSlotIndex, uint imageIndex)
    {
        EnsureOwnerThread();

        if (imageIndex >= _images.Length)
        {
            _device.MarkUncertainWsiOperation();
            throw new ArgumentOutOfRangeException(nameof(imageIndex));
        }

        var fenceCreateInfo = default(VkFenceCreateInfo);
        fenceCreateInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
        var fence = default(VkFence);
        EnsureSuccess(VulkanApi.vkCreateFence(_device.DeviceHandle, &fenceCreateInfo, null, &fence), "vkCreateFence");
        var waitInfo = default(VkSemaphoreSubmitInfo);
        waitInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_SEMAPHORE_SUBMIT_INFO;
        waitInfo.semaphore = GetAcquireSemaphore(frameSlotIndex);
        waitInfo.stageMask = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT;
        var submitInfo = default(VkSubmitInfo2);
        submitInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_SUBMIT_INFO_2;
        submitInfo.waitSemaphoreInfoCount = 1;
        submitInfo.pWaitSemaphoreInfos = &waitInfo;

        try
        {
            var submitResult = VulkanApi.vkQueueSubmit2(
                _device.GraphicsQueue,
                1,
                &submitInfo,
                fence);

            if (submitResult != VkResult.VK_SUCCESS)
            {
                _device.MarkUncertainWsiOperation();
                EnsureSuccess(submitResult, "vkQueueSubmit2(acquire cleanup)");
            }

            WaitForFence(fence, CancellationToken.None);
            var releaseFunctionName = (_device.SwapchainMaintenanceExtensionName == "VK_KHR_swapchain_maintenance1")
                ? "vkReleaseSwapchainImagesKHR"
                : "vkReleaseSwapchainImagesEXT";
            var releaseImages = (delegate* unmanaged<
                VkDevice,
                VkReleaseSwapchainImagesInfoKHR*,
                VkAllocationCallbacks*,
                VkResult>)GetDeviceProc(_device.DeviceHandle, releaseFunctionName);

            if (releaseImages == null)
            {
                _device.MarkUncertainWsiOperation();
                throw new NotSupportedException($"The Vulkan loader did not expose {releaseFunctionName}.");
            }

            var imageIndexValue = imageIndex;
            var releaseInfo = default(VkReleaseSwapchainImagesInfoKHR);
            releaseInfo.sType = (_device.SwapchainMaintenanceExtensionName == "VK_KHR_swapchain_maintenance1")
                ? VkStructureType.VK_STRUCTURE_TYPE_RELEASE_SWAPCHAIN_IMAGES_INFO_KHR
                : VkStructureType.VK_STRUCTURE_TYPE_RELEASE_SWAPCHAIN_IMAGES_INFO_EXT;
            releaseInfo.swapchain = _swapchain;
            releaseInfo.imageIndexCount = 1;
            releaseInfo.pImageIndices = &imageIndexValue;
            var releaseResult = releaseImages(_device.DeviceHandle, &releaseInfo, null);

            if (releaseResult != VkResult.VK_SUCCESS)
            {
                _device.MarkUncertainWsiOperation();
                EnsureSuccess(releaseResult, releaseFunctionName);
            }

            _acquiredImages[imageIndex] = false;
        }
        catch
        {
            _device.MarkUncertainWsiOperation();
            throw;
        }
        finally
        {
            if (!_device.HasUncertainSubmission)
            {
                VulkanApi.vkDestroyFence(_device.DeviceHandle, fence, null);
            }
        }
    }

    private void AddPresentationHistory(PresentationReceipt receipt)
    {
        lock (_presentationGate)
        {
            if (_presentationHistory.Count != 0)
            {
                var lastIndex = _presentationHistory.Count - 1;
                var (firstSequence, lastSequence, surfaceEpoch, swapchainEpoch, status) = _presentationHistory[lastIndex];

                if ((lastSequence == receipt.PresentationSequence - 1)
                    && (surfaceEpoch == receipt.SurfaceEpoch)
                    && (swapchainEpoch == receipt.SwapchainEpoch)
                    && (status == receipt.Status))
                {
                    _presentationHistory[lastIndex] = (
                        firstSequence,
                        receipt.PresentationSequence,
                        surfaceEpoch,
                        swapchainEpoch,
                        status);
                    return;
                }
            }

            _presentationHistory.Add((
                receipt.PresentationSequence,
                receipt.PresentationSequence,
                receipt.SurfaceEpoch,
                receipt.SwapchainEpoch,
                receipt.Status));
        }
    }

    private ImmutableArray<PresentationReceipt> DrainPendingPresentations(uint? expectedDeviceEpoch = null)
    {
        var sequences = new long[_pendingPresentations.Count];
        _pendingPresentations.Keys.CopyTo(sequences, 0);
        var receipts = ImmutableArray.CreateBuilder<PresentationReceipt>(sequences.Length);

        foreach (var sequence in sequences)
        {
            var presentation = _pendingPresentations[sequence];

            if ((expectedDeviceEpoch is uint deviceEpoch) && (presentation.DeviceEpoch != deviceEpoch))
            {
                throw new InvalidOperationException("The Vulkan Surface contains a presentation from another device generation.");
            }

            receipts.Add(WaitForPresentation(sequence, presentation.DeviceEpoch, CancellationToken.None));
        }

        return receipts.MoveToImmutable();
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("Vulkan surface operations must run on the creating graphics owner thread.");
        }
    }

    private void RetireImagePresentation(uint imageIndex)
    {
        var sequence = _imagePresentationSequences[imageIndex];

        if (sequence == 0)
        {
            return;
        }

        if (!_pendingPresentations.TryGetValue(sequence, out var presentation))
        {
            throw new InvalidOperationException("A swapchain image refers to a presentation missing from its pending set.");
        }

        _ = WaitForPresentation(sequence, presentation.DeviceEpoch, CancellationToken.None);
        _imagePresentationSequences[imageIndex] = 0;
    }

    private void WaitForFence(VkFence fence, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = VulkanApi.vkWaitForFences(
                _device.DeviceHandle,
                1,
                &fence,
                waitAll: 1,
                FenceWaitSliceNanoseconds);
            result = _device.SurfaceFenceWaitResultOverride?.Invoke(result) ?? result;

            if (result == VkResult.VK_SUCCESS)
            {
                return;
            }

            if (result is not VkResult.VK_TIMEOUT)
            {
                _device.MarkUncertainWsiOperation();
                EnsureSuccess(result, "vkWaitForFences(presentation)");
            }

            if (timer.Elapsed.TotalMilliseconds
                >= _device.SurfaceFenceWaitTimeoutNanoseconds / 1_000_000.0)
            {
                _device.MarkUncertainWsiOperation();
                throw new TimeoutException("Vulkan did not signal the presentation fence within the retirement timeout.");
            }
        }
    }

    private readonly record struct PendingPresentation(
        VkFence PresentFence,
        uint DeviceEpoch,
        uint SurfaceEpoch,
        uint SwapchainEpoch,
        int ImageIndex,
        PresentationStatus Status);

    private readonly record struct VulkanSurfaceNativeCapabilities(
        VkSurfaceCapabilitiesKHR Capabilities,
        VkSurfaceFormatKHR[] Formats,
        VkPresentModeKHR[] PresentModes);

    private readonly record struct VulkanSwapchainData(
        VkSwapchainKHR Swapchain,
        VkImage[] Images,
        VkSemaphore[] RenderFinishedSemaphores);
}
