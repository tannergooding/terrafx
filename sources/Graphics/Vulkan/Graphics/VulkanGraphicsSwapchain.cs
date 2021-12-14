// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using TerraFX.Interop.Vulkan;
using TerraFX.Numerics;
using TerraFX.Threading;
using static TerraFX.Interop.Vulkan.VkCompositeAlphaFlagsKHR;
using static TerraFX.Interop.Vulkan.VkImageUsageFlags;
using static TerraFX.Interop.Vulkan.VkPresentModeKHR;
using static TerraFX.Interop.Vulkan.VkStructureType;
using static TerraFX.Interop.Vulkan.VkSurfaceTransformFlagsKHR;
using static TerraFX.Interop.Vulkan.Vulkan;
using static TerraFX.Threading.VolatileState;
using static TerraFX.Utilities.AssertionUtilities;
using static TerraFX.Utilities.ExceptionUtilities;
using static TerraFX.Utilities.UnsafeUtilities;
using static TerraFX.Utilities.VulkanUtilities;

namespace TerraFX.Graphics;

/// <inheritdoc />
public sealed unsafe class VulkanGraphicsSwapchain : GraphicsSwapchain
{
    private readonly GraphicsFormat _renderTargetFormat;
    private readonly VkSurfaceKHR _vkSurface;

    private VulkanGraphicsRenderTarget[] _renderTargets;
    private UnmanagedArray<VkImage> _vkSwapchainImages;
    private VkSwapchainKHR _vkSwapchain;

    private uint _renderTargetIndex;

    private VolatileState _state;

    internal VulkanGraphicsSwapchain(VulkanGraphicsRenderPass renderPass, IGraphicsSurface surface, GraphicsFormat renderTargetFormat, uint minimumRenderTargetCount = 0)
        : base(renderPass, surface)
    {
        var renderTargetCount = minimumRenderTargetCount;
        var device = renderPass.Device;

        var vkSurface = CreateVkSurface(device, surface);
        _vkSurface = vkSurface;

        var vkSwapchain = CreateVkSwapchain(device, vkSurface, surface, ref renderTargetCount, renderTargetFormat);
        _vkSwapchain = vkSwapchain;

        var vkSwapchainImages = GetVkSwapchainImages(device, vkSwapchain, ref renderTargetCount);
        _vkSwapchainImages = vkSwapchainImages;

        _renderTargets = new VulkanGraphicsRenderTarget[renderTargetCount];
        _renderTargetFormat = renderTargetFormat;
        _renderTargetIndex = GetRenderTargetIndex(device, vkSwapchain, Fence);

        _ = _state.Transition(to: Initialized);

        InitializeRenderTargets(this, _renderTargets);
        Surface.SizeChanged += OnGraphicsSurfaceSizeChanged;

        static VkSurfaceKHR CreateVkSurface(VulkanGraphicsDevice device, IGraphicsSurface surface)
        {
            VkSurfaceKHR vkSurface;
            var vkInstance = device.Service.VkInstance;

            switch (surface.Kind)
            {
                case GraphicsSurfaceKind.Win32:
                {
                    var vkSurfaceCreateInfo = new VkWin32SurfaceCreateInfoKHR {
                        sType = VK_STRUCTURE_TYPE_WIN32_SURFACE_CREATE_INFO_KHR,
                        hinstance = surface.ContextHandle,
                        hwnd = surface.Handle,
                    };

                    ThrowExternalExceptionIfNotSuccess(vkCreateWin32SurfaceKHR(vkInstance, &vkSurfaceCreateInfo, pAllocator: null, &vkSurface));
                    break;
                }

                case GraphicsSurfaceKind.Xlib:
                {
                    var vkSurfaceCreateInfo = new VkXlibSurfaceCreateInfoKHR {
                        sType = VK_STRUCTURE_TYPE_XLIB_SURFACE_CREATE_INFO_KHR,
                        dpy = surface.ContextHandle,
                        window = (nuint)(nint)surface.Handle,
                    };

                    ThrowExternalExceptionIfNotSuccess(vkCreateXlibSurfaceKHR(vkInstance, &vkSurfaceCreateInfo, pAllocator: null, &vkSurface));
                    break;
                }

                default:
                {
                    ThrowForInvalidKind(surface.Kind);
                    vkSurface = VkSurfaceKHR.NULL;
                    break;
                }
            }

            VkBool32 supported;
            ThrowExternalExceptionIfNotSuccess(vkGetPhysicalDeviceSurfaceSupportKHR(device.Adapter.VkPhysicalDevice, device.VkCommandQueueFamilyIndex, vkSurface, &supported));

            if (!supported)
            {
                ThrowForMissingFeature();
            }
            return vkSurface;
        }
    }

    /// <summary>Finalizes an instance of the <see cref="VulkanGraphicsSwapchain" /> class.</summary>
    ~VulkanGraphicsSwapchain() => Dispose(isDisposing: false);

    /// <inheritdoc cref="GraphicsRenderPassObject.Adapter" />
    public new VulkanGraphicsAdapter Adapter => base.Adapter.As<VulkanGraphicsAdapter>();

    /// <inheritdoc cref="GraphicsRenderPassObject.Device" />
    public new VulkanGraphicsDevice Device => base.Device.As<VulkanGraphicsDevice>();

    /// <inheritdoc cref="GraphicsSwapchain.Fence" />
    public new VulkanGraphicsFence Fence => base.Fence.As<VulkanGraphicsFence>();

    /// <inheritdoc cref="GraphicsRenderPassObject.RenderPass" />
    public new VulkanGraphicsRenderPass RenderPass => base.RenderPass.As<VulkanGraphicsRenderPass>();

    /// <inheritdoc />
    public override VulkanGraphicsRenderTarget RenderTarget => _renderTargets.GetReference(_renderTargetIndex);

    /// <inheritdoc />
    public override uint RenderTargetCount => (uint)_renderTargets.Length;

    /// <inheritdoc />
    public override GraphicsFormat RenderTargetFormat => _renderTargetFormat;

    /// <inheritdoc />
    public override uint RenderTargetIndex => _renderTargetIndex;

    /// <inheritdoc cref="GraphicsRenderPassObject.Service" />
    public new VulkanGraphicsService Service => base.Service.As<VulkanGraphicsService>();

    /// <summary>Gets the <see cref="VkSurfaceKHR" /> used by the device.</summary>
    public VkSurfaceKHR VkSurface
    {
        get
        {
            AssertNotDisposedOrDisposing(_state);
            return _vkSurface;
        }
    }

    /// <summary>Gets the <see cref="VkSwapchainKHR" /> used by the device.</summary>
    public VkSwapchainKHR VkSwapchain
    {
        get
        {
            AssertNotDisposedOrDisposing(_state);
            return _vkSwapchain;
        }
    }

    /// <summary>Gets a readonly span of the <see cref="VkImage" />s used by <see cref="VkSwapchain" />.</summary>
    public UnmanagedReadOnlySpan<VkImage> VkSwapchainImages
    {
        get
        {
            AssertNotDisposedOrDisposing(_state);
            return _vkSwapchainImages;
        }
    }

    private static void CleanupRenderTargets(VulkanGraphicsRenderTarget[] renderTargets)
    {
        for (var index = 0; index < renderTargets.Length; index++)
        {
            renderTargets[index].Dispose();
            renderTargets[index] = null!;
        }
    }

    private static void CleanupVkSwapchain(VkDevice vkDevice, VkSwapchainKHR vkSwapchain)
    {
        if (vkSwapchain != VkSwapchainKHR.NULL)
        {
            vkDestroySwapchainKHR(vkDevice, vkSwapchain, pAllocator: null);
        }
    }

    private static VkSwapchainKHR CreateVkSwapchain(VulkanGraphicsDevice device, VkSurfaceKHR vkSurface, IGraphicsSurface surface, ref uint renderTargetCount, GraphicsFormat renderTargetFormat)
    {
        VkSwapchainKHR vkSwapchain;
        var vkPhysicalDevice = device.Adapter.VkPhysicalDevice;

        VkSurfaceCapabilitiesKHR vkSurfaceCapabilities;
        ThrowExternalExceptionIfNotSuccess(vkGetPhysicalDeviceSurfaceCapabilitiesKHR(vkPhysicalDevice, vkSurface, &vkSurfaceCapabilities));

        uint vkPresentModeCount;
        ThrowExternalExceptionIfNotSuccess(vkGetPhysicalDeviceSurfacePresentModesKHR(vkPhysicalDevice, vkSurface, &vkPresentModeCount, pPresentModes: null));

        var vkPresentModes = stackalloc VkPresentModeKHR[(int)vkPresentModeCount];
        ThrowExternalExceptionIfNotSuccess(vkGetPhysicalDeviceSurfacePresentModesKHR(vkPhysicalDevice, vkSurface, &vkPresentModeCount, vkPresentModes));

        if (renderTargetCount < vkSurfaceCapabilities.minImageCount)
        {
            renderTargetCount = vkSurfaceCapabilities.minImageCount;
        }

        if (vkSurfaceCapabilities.maxImageCount != 0)
        {
            ThrowIfNotInInsertBounds(renderTargetCount, vkSurfaceCapabilities.maxImageCount);
        }

        var vkSwapchainCreateInfo = new VkSwapchainCreateInfoKHR {
            sType = VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR,
            surface = vkSurface,
            minImageCount = renderTargetCount,
            imageExtent = new VkExtent2D {
                width = (uint)surface.Width,
                height = (uint)surface.Height,
            },
            imageArrayLayers = 1,
            imageUsage = VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT,
            preTransform = VK_SURFACE_TRANSFORM_IDENTITY_BIT_KHR,
            compositeAlpha = VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR,
            presentMode = VK_PRESENT_MODE_FIFO_KHR,
            clipped = VK_TRUE,
        };

        if ((vkSurfaceCapabilities.supportedTransforms & VK_SURFACE_TRANSFORM_IDENTITY_BIT_KHR) == 0)
        {
            vkSwapchainCreateInfo.preTransform = vkSurfaceCapabilities.currentTransform;
        }

        uint vkSurfaceFormatCount;
        ThrowExternalExceptionIfNotSuccess(vkGetPhysicalDeviceSurfaceFormatsKHR(vkPhysicalDevice, vkSurface, &vkSurfaceFormatCount, pSurfaceFormats: null));

        var vkSurfaceFormats = stackalloc VkSurfaceFormatKHR[(int)vkSurfaceFormatCount];
        ThrowExternalExceptionIfNotSuccess(vkGetPhysicalDeviceSurfaceFormatsKHR(vkPhysicalDevice, vkSurface, &vkSurfaceFormatCount, vkSurfaceFormats));

        var vkFormat = renderTargetFormat.AsVkFormat();

        for (uint i = 0; i < vkSurfaceFormatCount; i++)
        {
            if (vkSurfaceFormats[i].format == vkFormat)
            {
                vkSwapchainCreateInfo.imageFormat = vkSurfaceFormats[i].format;
                vkSwapchainCreateInfo.imageColorSpace = vkSurfaceFormats[i].colorSpace;
                break;
            }
        }

        ThrowExternalExceptionIfNotSuccess(vkCreateSwapchainKHR(device.VkDevice, &vkSwapchainCreateInfo, pAllocator: null, &vkSwapchain));
        return vkSwapchain;
    }

    private static uint GetRenderTargetIndex(VulkanGraphicsDevice device, VkSwapchainKHR vkSwapchain, VulkanGraphicsFence fence)
    {
        uint renderTargetIndex;
        ThrowExternalExceptionIfNotSuccess(vkAcquireNextImageKHR(device.VkDevice, vkSwapchain, timeout: ulong.MaxValue, VkSemaphore.NULL, fence.VkFence, &renderTargetIndex));
        return renderTargetIndex;
    }

    private static UnmanagedArray<VkImage> GetVkSwapchainImages(VulkanGraphicsDevice device, VkSwapchainKHR vkSwapchain, ref uint renderTargetCount)
    {
        var vkDevice = device.VkDevice;

        uint vkSwapchainImageCount;
        ThrowExternalExceptionIfNotSuccess(vkGetSwapchainImagesKHR(vkDevice, vkSwapchain, &vkSwapchainImageCount, pSwapchainImages: null));

        var vkSwapchainImages = new UnmanagedArray<VkImage>(vkSwapchainImageCount);
        ThrowExternalExceptionIfNotSuccess(vkGetSwapchainImagesKHR(vkDevice, vkSwapchain, &vkSwapchainImageCount, vkSwapchainImages.GetPointerUnsafe(0)));

        renderTargetCount = vkSwapchainImageCount;
        return vkSwapchainImages;
    }

    private static void InitializeRenderTargets(VulkanGraphicsSwapchain swapchain, VulkanGraphicsRenderTarget[] renderTargets)
    {
        for (var index = 0; index < renderTargets.Length; index++)
        {
            renderTargets[index] = new VulkanGraphicsRenderTarget(swapchain, (uint)index);
        }
    }

    /// <inheritdoc />
    public override VulkanGraphicsRenderTarget GetRenderTarget(uint index)
    {
        ThrowIfNotInBounds(index, (uint)_renderTargets.Length);
        return _renderTargets[index];
    }

    /// <inheritdoc />
    public override void Present()
    {
        ThrowIfDisposedOrDisposing(_state, nameof(VulkanGraphicsSwapchain));

        var fence = Fence;
        fence.Wait();
        fence.Reset();

        var device = Device;

        var renderTargetIndex = RenderTargetIndex;
        var vkSwapchain = VkSwapchain;

        var vkPresentInfo = new VkPresentInfoKHR {
            sType = VK_STRUCTURE_TYPE_PRESENT_INFO_KHR,
            swapchainCount = 1,
            pSwapchains = &vkSwapchain,
            pImageIndices = &renderTargetIndex,
        };
        ThrowExternalExceptionIfNotSuccess(vkQueuePresentKHR(device.VkCommandQueue, &vkPresentInfo));

        ThrowExternalExceptionIfNotSuccess(vkAcquireNextImageKHR(device.VkDevice, vkSwapchain, timeout: ulong.MaxValue, VkSemaphore.NULL, fence.VkFence, &renderTargetIndex));
        _renderTargetIndex = renderTargetIndex;
    }

    /// <inheritdoc />
    protected override void Dispose(bool isDisposing)
    {
        var priorState = _state.BeginDispose();

        if (priorState < Disposing)
        {
            var fence = Fence;
            fence.Wait();
            fence.Reset();

            CleanupRenderTargets(_renderTargets);

            CleanupVkSwapchain(Device.VkDevice, _vkSwapchain);
            DisposeVkSurface(Service.VkInstance, _vkSurface);

            if (isDisposing)
            {
                Fence?.Dispose();
            }
        }

        _state.EndDispose();

        static void DisposeVkSurface(VkInstance vkInstance, VkSurfaceKHR vkSurface)
        {
            if (vkSurface != VkSurfaceKHR.NULL)
            {
                vkDestroySurfaceKHR(vkInstance, vkSurface, pAllocator: null);
            }
        }
    }

    private void OnGraphicsSurfaceSizeChanged(object? sender, PropertyChangedEventArgs<Vector2> eventArgs)
    {
        var fence = Fence;
        fence.Wait();
        fence.Reset();

        var device = Device;
        var vkDevice = device.VkDevice;

        var renderTargets = _renderTargets;
        CleanupRenderTargets(renderTargets);

        CleanupVkSwapchain(vkDevice, _vkSwapchain);

        var surface = Surface;

        var renderTargetCount = (uint)_renderTargets.Length;
        var renderTargetFormat = _renderTargetFormat;

        var vkSwapchain = CreateVkSwapchain(device, VkSurface, surface, ref renderTargetCount, renderTargetFormat);
        _vkSwapchain = vkSwapchain;

        var vkSwapchainImages = GetVkSwapchainImages(device, vkSwapchain, ref renderTargetCount);
        _vkSwapchainImages = vkSwapchainImages;

        if (renderTargetCount != (uint)_renderTargets.Length)
        {
            _renderTargets = new VulkanGraphicsRenderTarget[renderTargetCount];
        }
        _renderTargetIndex = GetRenderTargetIndex(device, vkSwapchain, Fence);

        InitializeRenderTargets(this, _renderTargets);
    }
}