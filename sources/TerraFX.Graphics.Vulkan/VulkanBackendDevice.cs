// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using TerraFX.Graphics;
using TerraFX.Interop.Vulkan;
using VulkanApi = TerraFX.Interop.Vulkan.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal sealed unsafe partial class VulkanBackendDevice : IGraphicsBackendDevice
{
    private const uint RequestedApiVersion = (1u << 22) | (3u << 12);

    private readonly VkCommandBuffer _commandBuffer;
    private readonly VkCommandPool _commandPool;
    private readonly VkDevice _device;
    private readonly VkInstance _instance;
    private bool _isDisposed;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly VkPhysicalDevice _physicalDevice;
    private readonly VkPhysicalDeviceMemoryProperties _physicalDeviceMemoryProperties;
    private readonly VkQueue _queue;
    private readonly uint _queueFamilyIndex;
    private readonly VkSemaphore _timelineSemaphore;
    private readonly Func<VkResult, VkResult>? _transferWaitResultOverride;

    private VulkanBackendDevice(
        VkInstance instance,
        VkPhysicalDevice physicalDevice,
        VkDevice device,
        VkQueue queue,
        uint queueFamilyIndex,
        VkPhysicalDeviceMemoryProperties physicalDeviceMemoryProperties,
        VkCommandPool commandPool,
        VkCommandBuffer commandBuffer,
        VkSemaphore timelineSemaphore,
        GraphicsCapabilities capabilities,
        Func<VkResult, VkResult>? transferWaitResultOverride)
    {
        _instance = instance;
        _physicalDevice = physicalDevice;
        _device = device;
        _queue = queue;
        _queueFamilyIndex = queueFamilyIndex;
        _physicalDeviceMemoryProperties = physicalDeviceMemoryProperties;
        _commandPool = commandPool;
        _commandBuffer = commandBuffer;
        _timelineSemaphore = timelineSemaphore;
        _transferWaitResultOverride = transferWaitResultOverride;
        Capabilities = capabilities;
    }

    public GraphicsCapabilities Capabilities { get; }

    internal static VulkanBackendDevice Create(
        GraphicsDeviceProfile profile,
        CancellationToken cancellationToken)
        => Create(profile, GraphicsPlatformKind.None, cancellationToken);

    internal static VulkanBackendDevice Create(
        GraphicsDeviceProfile profile,
        Func<VkResult, VkResult>? transferWaitResultOverride,
        Action<VulkanBackendStartupStage>? afterNativeObjectAcquired,
        CancellationToken cancellationToken)
        => Create(
            profile,
            GraphicsPlatformKind.None,
            transferWaitResultOverride,
            afterNativeObjectAcquired,
            cancellationToken);

    internal static VulkanBackendDevice Create(
        GraphicsDeviceProfile profile,
        Func<VkResult, VkResult>? transferWaitResultOverride,
        CancellationToken cancellationToken)
        => Create(
            profile,
            GraphicsPlatformKind.None,
            transferWaitResultOverride,
            afterNativeObjectAcquired: null,
            cancellationToken);

    internal static VulkanBackendDevice Create(
        GraphicsDeviceProfile profile,
        GraphicsPlatformKind platformKind,
        CancellationToken cancellationToken)
        => Create(
            profile,
            platformKind,
            transferWaitResultOverride: null,
            afterNativeObjectAcquired: null,
            cancellationToken);

    internal static VulkanBackendDevice Create(
        GraphicsDeviceProfile profile,
        GraphicsPlatformKind platformKind,
        Func<VkResult, VkResult>? transferWaitResultOverride,
        Action<VulkanBackendStartupStage>? afterNativeObjectAcquired,
        CancellationToken cancellationToken)
    {
        GraphicsDevice.ValidateStartupProfile(GraphicsBackend.Vulkan, profile, platformKind);
        cancellationToken.ThrowIfCancellationRequested();

        if (profile != GraphicsDeviceProfile.OffscreenNative)
        {
            throw new PlatformNotSupportedException("Interactive Vulkan requires the unimplemented surface and WSI ownership path.");
        }

        var instance = CreateInstance(cancellationToken);
        var hasInstance = true;
        var device = default(VkDevice);
        var hasDevice = false;
        var commandPool = default(VkCommandPool);
        var hasCommandPool = false;
        var timelineSemaphore = default(VkSemaphore);
        var hasTimelineSemaphore = false;
        var transferred = false;

        try
        {
            afterNativeObjectAcquired?.Invoke(VulkanBackendStartupStage.InstanceCreated);
            cancellationToken.ThrowIfCancellationRequested();

            var physicalDevices = EnumeratePhysicalDevices(instance);

            if (physicalDevices.Length == 0)
            {
                throw new NotSupportedException("The Vulkan loader reported no physical devices.");
            }

            var failures = new List<string>();

            foreach (var physicalDevice in physicalDevices)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!TryCreateCandidate(
                    physicalDevice,
                    profile,
                    out var capabilities,
                    out var memoryProperties,
                    out var queueFamilyIndex,
                    out var rejection))
                {
                    failures.Add(rejection);
                    continue;
                }

                var missingRequirements = GraphicsCapabilityAdmission.GetMissingRequirements(capabilities, profile);

                if (!missingRequirements.IsEmpty)
                {
                    failures.Add($"{capabilities.AdapterName}: {string.Join("; ", missingRequirements)}");
                    continue;
                }

                device = CreateLogicalDevice(physicalDevice, queueFamilyIndex, cancellationToken);
                hasDevice = true;
                afterNativeObjectAcquired?.Invoke(VulkanBackendStartupStage.LogicalDeviceCreated);
                cancellationToken.ThrowIfCancellationRequested();

                var queue = default(VkQueue);
                VulkanApi.vkGetDeviceQueue(device, queueFamilyIndex, 0, &queue);
                var commandBuffer = CreateCommandBuffer(
                    device,
                    queueFamilyIndex,
                    afterNativeObjectAcquired,
                    cancellationToken,
                    out commandPool);
                hasCommandPool = true;
                afterNativeObjectAcquired?.Invoke(VulkanBackendStartupStage.CommandBufferCreated);
                cancellationToken.ThrowIfCancellationRequested();

                timelineSemaphore = CreateTimelineSemaphore(device);
                hasTimelineSemaphore = true;
                afterNativeObjectAcquired?.Invoke(VulkanBackendStartupStage.TimelineSemaphoreCreated);
                cancellationToken.ThrowIfCancellationRequested();

                var backend = new VulkanBackendDevice(
                    instance,
                    physicalDevice,
                    device,
                    queue,
                    queueFamilyIndex,
                    memoryProperties,
                    commandPool,
                    commandBuffer,
                    timelineSemaphore,
                    capabilities,
                    transferWaitResultOverride);
                transferred = true;
                hasInstance = false;
                hasDevice = false;
                hasCommandPool = false;
                hasTimelineSemaphore = false;
                return backend;
            }

            throw new NotSupportedException(
                failures.Count == 0
                    ? $"No Vulkan physical device satisfies the {profile} profile."
                    : $"No Vulkan physical device satisfies the {profile} profile: {string.Join(" | ", failures)}");
        }
        catch
        {
            if (!transferred)
            {
                if (hasDevice)
                {
                    if (hasTimelineSemaphore)
                    {
                        VulkanApi.vkDestroySemaphore(device, timelineSemaphore, null);
                    }

                    if (hasCommandPool)
                    {
                        VulkanApi.vkDestroyCommandPool(device, commandPool, null);
                    }

                    VulkanApi.vkDestroyDevice(device, null);
                }

                if (hasInstance)
                {
                    VulkanApi.vkDestroyInstance(instance, null);
                }
            }

            throw;
        }
    }

    internal static VulkanBackendDevice Create(
        GraphicsDeviceProfile profile,
        GraphicsPlatformKind platformKind,
        Func<VkResult, VkResult>? transferWaitResultOverride,
        CancellationToken cancellationToken)
        => Create(
            profile,
            platformKind,
            transferWaitResultOverride,
            afterNativeObjectAcquired: null,
            cancellationToken);

    private static long ConvertPositiveLong(ulong value, string parameterName)
    {
        var converted = checked((long)value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(converted, parameterName);
        return converted;
    }

    private static VkCommandBuffer CreateCommandBuffer(
        VkDevice device,
        uint queueFamilyIndex,
        Action<VulkanBackendStartupStage>? afterNativeObjectAcquired,
        CancellationToken cancellationToken,
        out VkCommandPool commandPool)
    {
        var poolCreateInfo = default(VkCommandPoolCreateInfo);
        poolCreateInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
        poolCreateInfo.flags = VkCommandPoolCreateFlags.VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
        poolCreateInfo.queueFamilyIndex = queueFamilyIndex;
        commandPool = default;
        var createdCommandPool = default(VkCommandPool);
        ThrowIfFailed(VulkanApi.vkCreateCommandPool(device, &poolCreateInfo, null, &createdCommandPool), "vkCreateCommandPool");
        commandPool = createdCommandPool;

        try
        {
            afterNativeObjectAcquired?.Invoke(VulkanBackendStartupStage.CommandPoolCreated);
            cancellationToken.ThrowIfCancellationRequested();

            var allocateInfo = default(VkCommandBufferAllocateInfo);
            allocateInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
            allocateInfo.commandPool = commandPool;
            allocateInfo.level = VkCommandBufferLevel.VK_COMMAND_BUFFER_LEVEL_PRIMARY;
            allocateInfo.commandBufferCount = 1;
            VkCommandBuffer commandBuffer = default;
            ThrowIfFailed(VulkanApi.vkAllocateCommandBuffers(device, &allocateInfo, &commandBuffer), "vkAllocateCommandBuffers");
            afterNativeObjectAcquired?.Invoke(VulkanBackendStartupStage.CommandBufferAllocated);
            cancellationToken.ThrowIfCancellationRequested();
            return commandBuffer;
        }
        catch
        {
            VulkanApi.vkDestroyCommandPool(device, commandPool, null);
            commandPool = default;
            throw;
        }
    }

    private static VkInstance CreateInstance(CancellationToken cancellationToken)
    {
        uint loaderVersion = 0;
        ThrowIfFailed(VulkanApi.vkEnumerateInstanceVersion(&loaderVersion), "vkEnumerateInstanceVersion");

        if (VulkanNativeTypeMapper.GetGraphicsApiVersion(loaderVersion).Major < 1
            || ((VulkanNativeTypeMapper.GetGraphicsApiVersion(loaderVersion).Major == 1)
                && (VulkanNativeTypeMapper.GetGraphicsApiVersion(loaderVersion).Minor < 3)))
        {
            throw new NotSupportedException("The Vulkan loader does not support API 1.3 or later.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        VkInstance instance = default;
        var applicationName = "TerraFX.Graphics.Vulkan\0"u8;
        var engineName = "TerraFX\0"u8;
        var applicationInfo = default(VkApplicationInfo);
        applicationInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_APPLICATION_INFO;
        applicationInfo.applicationVersion = 1;
        applicationInfo.engineVersion = 1;
        applicationInfo.apiVersion = RequestedApiVersion;

        fixed (byte* applicationNamePointer = applicationName)
        fixed (byte* engineNamePointer = engineName)
        {
            applicationInfo.pApplicationName = (sbyte*)applicationNamePointer;
            applicationInfo.pEngineName = (sbyte*)engineNamePointer;
            var createInfo = default(VkInstanceCreateInfo);
            createInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO;
            createInfo.pApplicationInfo = &applicationInfo;
            ThrowIfFailed(VulkanApi.vkCreateInstance(&createInfo, null, &instance), "vkCreateInstance");
        }

        return instance;
    }

    private static VkDevice CreateLogicalDevice(
        VkPhysicalDevice physicalDevice,
        uint queueFamilyIndex,
        CancellationToken cancellationToken)
    {
        var priority = 1.0f;
        var queueCreateInfo = default(VkDeviceQueueCreateInfo);
        queueCreateInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO;
        queueCreateInfo.queueFamilyIndex = queueFamilyIndex;
        queueCreateInfo.queueCount = 1;
        queueCreateInfo.pQueuePriorities = &priority;

        var features13 = default(VkPhysicalDeviceVulkan13Features);
        features13.sType = VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_FEATURES;
        features13.synchronization2 = 1;
        features13.dynamicRendering = 1;
        var features12 = default(VkPhysicalDeviceVulkan12Features);
        features12.sType = VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES;
        features12.pNext = &features13;
        features12.timelineSemaphore = 1;
        var createInfo = default(VkDeviceCreateInfo);
        createInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO;
        createInfo.pNext = &features12;
        createInfo.queueCreateInfoCount = 1;
        createInfo.pQueueCreateInfos = &queueCreateInfo;
        VkDevice device = default;
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfFailed(VulkanApi.vkCreateDevice(physicalDevice, &createInfo, null, &device), "vkCreateDevice");
        return device;
    }

    private static VkSemaphore CreateTimelineSemaphore(VkDevice device)
    {
        var typeInfo = default(VkSemaphoreTypeCreateInfo);
        typeInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_SEMAPHORE_TYPE_CREATE_INFO;
        typeInfo.semaphoreType = VkSemaphoreType.VK_SEMAPHORE_TYPE_TIMELINE;
        var createInfo = default(VkSemaphoreCreateInfo);
        createInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO;
        createInfo.pNext = &typeInfo;
        VkSemaphore semaphore = default;
        ThrowIfFailed(VulkanApi.vkCreateSemaphore(device, &createInfo, null, &semaphore), "vkCreateSemaphore");
        return semaphore;
    }

    private static VkPhysicalDevice[] EnumeratePhysicalDevices(VkInstance instance)
    {
        uint count = 0;
        ThrowIfFailed(VulkanApi.vkEnumeratePhysicalDevices(instance, &count, null), "vkEnumeratePhysicalDevices");

        if (count == 0)
        {
            return [];
        }

        var physicalDevices = new VkPhysicalDevice[checked((int)count)];
        fixed (VkPhysicalDevice* physicalDevicesPointer = physicalDevices)
        {
            ThrowIfFailed(
                VulkanApi.vkEnumeratePhysicalDevices(instance, &count, physicalDevicesPointer),
                "vkEnumeratePhysicalDevices");
        }

        if (count != physicalDevices.Length)
        {
            Array.Resize(ref physicalDevices, checked((int)count));
        }

        return physicalDevices;
    }

    private static GraphicsAdapterId GetAdapterId(VkPhysicalDeviceIDProperties idProperties)
    {
        var uuidPointer = (byte*)&idProperties
            + checked((int)Marshal.OffsetOf<VkPhysicalDeviceIDProperties>("deviceUUID"));
        Span<byte> uuid = stackalloc byte[16];
        new ReadOnlySpan<byte>(uuidPointer, uuid.Length).CopyTo(uuid);
        return VulkanNativeTypeMapper.GetNativeAdapterId(uuid);
    }

    private static ImmutableArray<int> GetCommonImageSampleCounts(
        VkPhysicalDevice physicalDevice,
        VkFormat format,
        ref TextureUsage supportedUsages)
    {
        var usages = new[]
        {
            TextureUsage.Sampled,
            TextureUsage.Storage,
            TextureUsage.ColorAttachment,
            TextureUsage.DepthAttachment,
            TextureUsage.CopySource,
            TextureUsage.CopyDestination,
        };
        var commonSampleCounts = default(VkSampleCountFlags);
        var hasSupportedUsage = false;

        foreach (var usage in usages)
        {
            if ((supportedUsages & usage) == 0)
            {
                continue;
            }

            var imageFormatProperties = default(VkImageFormatProperties);
            var imageUsage = GetNativeImageUsage(usage);
            var result = VulkanApi.vkGetPhysicalDeviceImageFormatProperties(
                physicalDevice,
                format,
                VkImageType.VK_IMAGE_TYPE_2D,
                VkImageTiling.VK_IMAGE_TILING_OPTIMAL,
                imageUsage,
                default,
                &imageFormatProperties);

            if (result == VkResult.VK_ERROR_FORMAT_NOT_SUPPORTED)
            {
                supportedUsages &= ~usage;
                continue;
            }

            ThrowIfFailed(result, "vkGetPhysicalDeviceImageFormatProperties");
            commonSampleCounts = hasSupportedUsage
                ? commonSampleCounts & imageFormatProperties.sampleCounts
                : imageFormatProperties.sampleCounts;
            hasSupportedUsage = true;
        }

        return hasSupportedUsage
            ? VulkanGraphicsCapabilityMapper.GetFormatSampleCounts(commonSampleCounts)
            : [];
    }

    private static ImmutableArray<GraphicsFormatCapabilities> GetFormatCapabilities(VkPhysicalDevice physicalDevice)
    {
        var formats = ImmutableArray.CreateBuilder<GraphicsFormatCapabilities>(9);
        ReadOnlySpan<TextureFormat> supportedFormats = [
            TextureFormat.R8Unorm,
            TextureFormat.Rgba8Unorm,
            TextureFormat.Rgba8Srgb,
            TextureFormat.Bgra8Unorm,
            TextureFormat.Bgra8Srgb,
            TextureFormat.Rgba16Float,
            TextureFormat.R32Float,
            TextureFormat.R32UInt,
            TextureFormat.D32Float,
        ];

        foreach (var format in supportedFormats)
        {
            var properties = default(VkFormatProperties);
            var nativeFormat = VulkanNativeTypeMapper.GetVkFormat(format);
            VulkanApi.vkGetPhysicalDeviceFormatProperties(physicalDevice, nativeFormat, &properties);
            var usages = VulkanGraphicsCapabilityMapper.GetSupportedTextureUsages(properties.optimalTilingFeatures);
            var sampleCounts = GetCommonImageSampleCounts(physicalDevice, nativeFormat, ref usages);

            if ((usages != TextureUsage.None) && !sampleCounts.IsDefaultOrEmpty)
            {
                formats.Add(new GraphicsFormatCapabilities(format, usages, sampleCounts));
            }
        }

        return formats.ToImmutable();
    }

    private static ImmutableArray<GraphicsMemoryHeapCapabilities> GetMemoryHeapCapabilities(
        VkPhysicalDeviceMemoryProperties memoryProperties)
    {
        var heaps = ImmutableArray.CreateBuilder<GraphicsMemoryHeapCapabilities>(
            checked((int)memoryProperties.memoryHeapCount));
        var memoryTypes = GetMemoryTypePointer(&memoryProperties);
        var memoryHeaps = GetMemoryHeapPointer(&memoryProperties);

        for (var heapIndex = 0; heapIndex < memoryProperties.memoryHeapCount; heapIndex++)
        {
            var properties = GraphicsMemoryHeapProperties.None;

            if ((memoryHeaps[heapIndex].flags & VkMemoryHeapFlags.VK_MEMORY_HEAP_DEVICE_LOCAL_BIT) != 0)
            {
                properties |= GraphicsMemoryHeapProperties.DeviceLocal;
            }

            for (var typeIndex = 0; typeIndex < memoryProperties.memoryTypeCount; typeIndex++)
            {
                if (memoryTypes[typeIndex].heapIndex != heapIndex)
                {
                    continue;
                }

                var typeProperties = memoryTypes[typeIndex].propertyFlags;

                if ((typeProperties & VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT) != 0)
                {
                    properties |= GraphicsMemoryHeapProperties.HostVisible;
                }

                if ((typeProperties & VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT) != 0)
                {
                    properties |= GraphicsMemoryHeapProperties.HostCoherent;
                }

                if ((typeProperties & VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_HOST_CACHED_BIT) != 0)
                {
                    properties |= GraphicsMemoryHeapProperties.HostCached;
                }
            }

            if (properties == GraphicsMemoryHeapProperties.None)
            {
                throw new NotSupportedException($"Vulkan memory heap {heapIndex} has no supported memory properties.");
            }

            heaps.Add(new GraphicsMemoryHeapCapabilities(
                checked((int)heapIndex),
                properties,
                ConvertPositiveLong(memoryHeaps[heapIndex].size, nameof(memoryHeaps)),
                budgetBytes: null));
        }

        return heaps.ToImmutable();
    }

    private static VkMemoryHeap* GetMemoryHeapPointer(VkPhysicalDeviceMemoryProperties* memoryProperties)
        => (VkMemoryHeap*)((byte*)memoryProperties
            + checked((int)Marshal.OffsetOf<VkPhysicalDeviceMemoryProperties>("memoryHeaps")));

    private static VkMemoryType* GetMemoryTypePointer(VkPhysicalDeviceMemoryProperties* memoryProperties)
        => (VkMemoryType*)((byte*)memoryProperties
            + checked((int)Marshal.OffsetOf<VkPhysicalDeviceMemoryProperties>("memoryTypes")));

    private static VkImageUsageFlags GetNativeImageUsage(TextureUsage usage)
    {
        const TextureUsage knownUsages =
            TextureUsage.Sampled
            | TextureUsage.Storage
            | TextureUsage.ColorAttachment
            | TextureUsage.DepthAttachment
            | TextureUsage.CopySource
            | TextureUsage.CopyDestination;

        if ((usage == TextureUsage.None) || ((usage & ~knownUsages) != 0))
        {
            throw new ArgumentOutOfRangeException(nameof(usage));
        }

        var nativeUsages = default(VkImageUsageFlags);

        if ((usage & TextureUsage.Sampled) != 0)
        {
            nativeUsages |= VkImageUsageFlags.VK_IMAGE_USAGE_SAMPLED_BIT;
        }

        if ((usage & TextureUsage.Storage) != 0)
        {
            nativeUsages |= VkImageUsageFlags.VK_IMAGE_USAGE_STORAGE_BIT;
        }

        if ((usage & TextureUsage.ColorAttachment) != 0)
        {
            nativeUsages |= VkImageUsageFlags.VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;
        }

        if ((usage & TextureUsage.DepthAttachment) != 0)
        {
            nativeUsages |= VkImageUsageFlags.VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT;
        }

        if ((usage & TextureUsage.CopySource) != 0)
        {
            nativeUsages |= VkImageUsageFlags.VK_IMAGE_USAGE_TRANSFER_SRC_BIT;
        }

        if ((usage & TextureUsage.CopyDestination) != 0)
        {
            nativeUsages |= VkImageUsageFlags.VK_IMAGE_USAGE_TRANSFER_DST_BIT;
        }

        return nativeUsages;
    }

    private static string ReadDeviceName(VkPhysicalDeviceProperties properties)
    {
        var namePointer = (sbyte*)&properties
            + checked((int)Marshal.OffsetOf<VkPhysicalDeviceProperties>("deviceName"));
        return ReadUtf8(namePointer, 256);
    }

    private static string ReadDriverString<TBuffer>(TBuffer buffer, int byteLength)
        where TBuffer : unmanaged
    {
        var bufferPointer = (sbyte*)&buffer;
        return ReadUtf8(bufferPointer, byteLength);
    }

    private static string ReadUtf8(sbyte* source, int byteLength)
    {
        var bytes = stackalloc byte[byteLength];
        var length = 0;

        while ((length < byteLength) && (source[length] != 0))
        {
            bytes[length] = unchecked((byte)source[length]);
            length++;
        }

        return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(bytes, length));
    }

    private static void ThrowIfFailed(VkResult result, string operation)
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

    private static bool TryCreateCandidate(
        VkPhysicalDevice physicalDevice,
        GraphicsDeviceProfile profile,
        out GraphicsCapabilities capabilities,
        out VkPhysicalDeviceMemoryProperties memoryProperties,
        out uint queueFamilyIndex,
        out string rejection)
    {
        var properties13 = default(VkPhysicalDeviceVulkan13Properties);
        properties13.sType = VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_PROPERTIES;
        var driverProperties = default(VkPhysicalDeviceDriverProperties);
        driverProperties.sType = VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DRIVER_PROPERTIES;
        driverProperties.pNext = &properties13;
        var idProperties = default(VkPhysicalDeviceIDProperties);
        idProperties.sType = VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_ID_PROPERTIES;
        idProperties.pNext = &driverProperties;
        var properties2 = default(VkPhysicalDeviceProperties2);
        properties2.sType = VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PROPERTIES_2;
        properties2.pNext = &idProperties;
        VulkanApi.vkGetPhysicalDeviceProperties2(physicalDevice, &properties2);
        var apiVersion = VulkanNativeTypeMapper.GetGraphicsApiVersion(properties2.properties.apiVersion);

        if ((apiVersion.Major < 1) || ((apiVersion.Major == 1) && (apiVersion.Minor < 3)))
        {
            capabilities = null!;
            memoryProperties = default;
            queueFamilyIndex = 0;
            rejection = $"{ReadDeviceName(properties2.properties)}: Vulkan API 1.3 or later (reported {apiVersion.Major}.{apiVersion.Minor})";
            return false;
        }

        var features13 = default(VkPhysicalDeviceVulkan13Features);
        features13.sType = VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_FEATURES;
        var features12 = default(VkPhysicalDeviceVulkan12Features);
        features12.sType = VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES;
        features12.pNext = &features13;
        var features2 = default(VkPhysicalDeviceFeatures2);
        features2.sType = VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2;
        features2.pNext = &features12;
        VulkanApi.vkGetPhysicalDeviceFeatures2(physicalDevice, &features2);

        if ((features12.timelineSemaphore == 0)
            || (features13.synchronization2 == 0)
            || (features13.dynamicRendering == 0))
        {
            capabilities = null!;
            memoryProperties = default;
            queueFamilyIndex = 0;
            rejection = $"{ReadDeviceName(properties2.properties)}: required Vulkan timeline/synchronization2/dynamic-rendering features";
            return false;
        }

        if (!TryGetGraphicsComputeQueueFamily(physicalDevice, out queueFamilyIndex))
        {
            capabilities = null!;
            memoryProperties = default;
            rejection = $"{ReadDeviceName(properties2.properties)}: graphics and compute queue family";
            return false;
        }

        var physicalDeviceMemoryProperties = default(VkPhysicalDeviceMemoryProperties);
        VulkanApi.vkGetPhysicalDeviceMemoryProperties(physicalDevice, &physicalDeviceMemoryProperties);
        memoryProperties = physicalDeviceMemoryProperties;
        var adapterId = GetAdapterId(idProperties);
        var hardwareFeatures = ImmutableArray.CreateBuilder<GraphicsFeature>(4);
        var implementedFeatures = ImmutableArray.CreateBuilder<GraphicsFeature>(3);
        hardwareFeatures.Add(GraphicsFeature.TimelineSemaphore);
        hardwareFeatures.Add(GraphicsFeature.Synchronization2);
        hardwareFeatures.Add(GraphicsFeature.DynamicRendering);
        implementedFeatures.Add(GraphicsFeature.TimelineSemaphore);
        implementedFeatures.Add(GraphicsFeature.Synchronization2);
        implementedFeatures.Add(GraphicsFeature.DynamicRendering);

        if (features2.features.samplerAnisotropy != 0)
        {
            hardwareFeatures.Add(GraphicsFeature.SamplerAnisotropy);
        }

        var limits = VulkanGraphicsCapabilityMapper.CreateLimits(
            properties2.properties.limits,
            properties13.maxBufferSize,
            samplerAnisotropySupported: false);
        capabilities = new GraphicsCapabilities(
            GraphicsBackend.Vulkan,
            profile,
            adapterId,
            properties2.properties.vendorID,
            properties2.properties.deviceID,
            ReadDeviceName(properties2.properties),
            ReadDriverString(driverProperties.driverName, 256),
            ReadDriverString(driverProperties.driverInfo, 256),
            apiVersion,
            hardwareFeatures.MoveToImmutable(),
            implementedFeatures.MoveToImmutable(),
            limits,
            GetFormatCapabilities(physicalDevice),
            GetMemoryHeapCapabilities(memoryProperties));
        rejection = string.Empty;
        return true;
    }

    private static bool TryGetGraphicsComputeQueueFamily(
        VkPhysicalDevice physicalDevice,
        out uint queueFamilyIndex)
    {
        uint count = 0;
        VulkanApi.vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, &count, null);

        if (count == 0)
        {
            queueFamilyIndex = 0;
            return false;
        }

        var queueFamilies = stackalloc VkQueueFamilyProperties[checked((int)count)];
        VulkanApi.vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, &count, queueFamilies);

        for (uint index = 0; index < count; index++)
        {
            var requiredFlags = VkQueueFlags.VK_QUEUE_GRAPHICS_BIT | VkQueueFlags.VK_QUEUE_COMPUTE_BIT;

            if ((queueFamilies[index].queueCount > 0)
                && ((queueFamilies[index].queueFlags & requiredFlags) == requiredFlags))
            {
                queueFamilyIndex = index;
                return true;
            }
        }

        queueFamilyIndex = 0;
        return false;
    }

    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("Vulkan device operations must run on the creating owner thread.");
        }

        if (_isDisposed)
        {
            return;
        }

        if (_hasUncertainSubmission)
        {
            throw new InvalidOperationException("The Vulkan device cannot release storage while submission completion is uncertain.");
        }

        if (_liveDescriptorSetCount != 0)
        {
            throw new InvalidOperationException("The Vulkan device still has live descriptor sets.");
        }

        var uploadStagingRing = _uploadStagingRing;

        if (uploadStagingRing is not null)
        {
            _uploadStagingRing = null;
            uploadStagingRing.Dispose();
        }

        if (_liveBufferCount != 0)
        {
            throw new InvalidOperationException("The Vulkan device still has live buffer owners.");
        }

        if (_liveStagingBytes != 0)
        {
            throw new InvalidOperationException("The Vulkan device still has staging allocation reservations.");
        }

        if (_liveTextureCount != 0)
        {
            throw new InvalidOperationException("The Vulkan device still has live texture owners.");
        }

        if (_liveSamplerCount != 0)
        {
            throw new InvalidOperationException("The Vulkan device still has live sampler owners.");
        }

        if (_livePipelineCount != 0)
        {
            throw new InvalidOperationException("The Vulkan device still has live pipeline owners.");
        }

        if (_liveDescriptorSetLayoutCount != 0)
        {
            throw new InvalidOperationException("The Vulkan device still has live descriptor set layouts.");
        }

        ThrowIfFailed(VulkanApi.vkDeviceWaitIdle(_device), "vkDeviceWaitIdle");
        VulkanApi.vkDestroySemaphore(_device, _timelineSemaphore, null);
        VulkanApi.vkDestroyCommandPool(_device, _commandPool, null);
        VulkanApi.vkDestroyDevice(_device, null);
        VulkanApi.vkDestroyInstance(_instance, null);
        _isDisposed = true;
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("Vulkan device operations must run on the creating owner thread.");
        }
    }
}
