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

    private VkCommandBuffer _commandBuffer;
    private readonly VkCommandPool _commandPool;
    private readonly Func<long, long>? _completedGraphFenceValueOverride;
    private readonly VkDevice _device;
    private readonly ImmutableHashSet<string> _enabledSpirvExtensions;
    private readonly bool _fragmentStoresAndAtomicsEnabled;
    private readonly Action<long>? _graphFenceWaitObserver;
    private readonly int _graphFrameSlotCount;
    private readonly VulkanGraphFrameSlot[] _graphFrameSlots;
    private readonly Action<GraphicsGraphSubmission>? _graphSubmissionObserver;
    private readonly VkInstance _instance;
    private bool _isDisposed;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly VkPhysicalDevice _physicalDevice;
    private readonly VkPhysicalDeviceMemoryProperties _physicalDeviceMemoryProperties;
    private readonly GraphicsPlatformKind _platformKind;
    private readonly VkQueue _queue;
    private readonly uint _queueFamilyIndex;
    private readonly uint[] _queueFamilyIndices;
    private readonly VkQueue[] _queues;
    private readonly Action? _surfaceImageAcquiredObserver;
    private readonly string? _swapchainMaintenanceExtensionName;
    private readonly VkSemaphore _timelineSemaphore;
    private readonly VkCommandBuffer _transferCommandBuffer;
    private readonly Func<VkResult, VkResult>? _transferWaitResultOverride;

    private VulkanBackendDevice(
        VkInstance instance,
        VkPhysicalDevice physicalDevice,
        VkDevice device,
        VkQueue queue,
        uint queueFamilyIndex,
        uint[] queueFamilyIndices,
        VkQueue[] queues,
        VkPhysicalDeviceMemoryProperties physicalDeviceMemoryProperties,
        VkCommandPool commandPool,
        VkCommandBuffer[] commandBuffers,
        VkSemaphore timelineSemaphore,
        GraphicsCapabilities capabilities,
        ImmutableHashSet<string> enabledSpirvExtensions,
        bool fragmentStoresAndAtomicsEnabled,
        string? swapchainMaintenanceExtensionName,
        GraphicsPlatformKind platformKind,
        Func<VkResult, VkResult>? transferWaitResultOverride,
        int graphFrameSlotCount,
        Action<long>? graphFenceWaitObserver,
        Func<long, long>? completedGraphFenceValueOverride,
        Action<GraphicsGraphSubmission>? graphSubmissionObserver,
        Action? surfaceImageAcquiredObserver)
    {
        ArgumentNullException.ThrowIfNull(enabledSpirvExtensions);
        ArgumentNullException.ThrowIfNull(commandBuffers);
        ArgumentNullException.ThrowIfNull(queueFamilyIndices);
        ArgumentNullException.ThrowIfNull(queues);

        if (commandBuffers.Length != checked(graphFrameSlotCount + 1))
        {
            throw new ArgumentException("Vulkan command-buffer storage does not match the configured graph frame-slot count.", nameof(commandBuffers));
        }

        if (queueFamilyIndices.Length != queues.Length)
        {
            throw new ArgumentException("Vulkan queue handles do not match the enabled queue families.", nameof(queues));
        }

        _instance = instance;
        _physicalDevice = physicalDevice;
        _device = device;
        _enabledSpirvExtensions = enabledSpirvExtensions;
        _fragmentStoresAndAtomicsEnabled = fragmentStoresAndAtomicsEnabled;
        _queue = queue;
        _queueFamilyIndex = queueFamilyIndex;
        _queueFamilyIndices = queueFamilyIndices;
        _queues = queues;
        _swapchainMaintenanceExtensionName = swapchainMaintenanceExtensionName;
        _platformKind = platformKind;
        _physicalDeviceMemoryProperties = physicalDeviceMemoryProperties;
        _commandPool = commandPool;
        _commandBuffer = commandBuffers[0];
        _transferCommandBuffer = commandBuffers[0];
        _timelineSemaphore = timelineSemaphore;
        _transferWaitResultOverride = transferWaitResultOverride;
        _graphFenceWaitObserver = graphFenceWaitObserver;
        _completedGraphFenceValueOverride = completedGraphFenceValueOverride;
        _graphSubmissionObserver = graphSubmissionObserver;
        _surfaceImageAcquiredObserver = surfaceImageAcquiredObserver;
        _graphFrameSlotCount = graphFrameSlotCount;
        _graphFrameSlots = new VulkanGraphFrameSlot[graphFrameSlotCount];

        for (var index = 0; index < _graphFrameSlots.Length; index++)
        {
            _graphFrameSlots[index] = new VulkanGraphFrameSlot(commandBuffers[index + 1]);
        }

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
        Action<VulkanBackendStartupStage>? afterNativeObjectAcquired,
        Func<VulkanBackendStartupStage, VkResult, VkResult>? startupNativeResultOverride,
        CancellationToken cancellationToken)
        => Create(
            profile,
            GraphicsPlatformKind.None,
            transferWaitResultOverride,
            afterNativeObjectAcquired,
            startupNativeResultOverride,
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
            startupNativeResultOverride: null,
            cancellationToken);

    internal static VulkanBackendDevice Create(
        GraphicsDeviceProfile profile,
        GraphicsPlatformKind platformKind,
        Func<VkResult, VkResult>? transferWaitResultOverride,
        Action<VulkanBackendStartupStage>? afterNativeObjectAcquired,
        CancellationToken cancellationToken)
        => Create(
            profile,
            platformKind,
            transferWaitResultOverride,
            afterNativeObjectAcquired,
            startupNativeResultOverride: null,
            cancellationToken);

    internal static VulkanBackendDevice Create(
        GraphicsDeviceProfile profile,
        GraphicsPlatformKind platformKind,
        Func<VkResult, VkResult>? transferWaitResultOverride,
        Action<VulkanBackendStartupStage>? afterNativeObjectAcquired,
        Func<VulkanBackendStartupStage, VkResult, VkResult>? startupNativeResultOverride,
        CancellationToken cancellationToken,
        int graphFrameSlotCount = 3,
        Action<long>? graphFenceWaitObserver = null,
        Func<long, long>? completedGraphFenceValueOverride = null,
        Action<GraphicsGraphSubmission>? graphSubmissionObserver = null,
        GraphicsNativeSurfaceBinding? presentationProbeBinding = null,
        Action? surfaceImageAcquiredObserver = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(graphFrameSlotCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(graphFrameSlotCount, 4);
        GraphicsDevice.ValidateStartupProfile(GraphicsBackend.Vulkan, profile, platformKind);
        cancellationToken.ThrowIfCancellationRequested();

        var interactiveProbeBinding = default(GraphicsNativeSurfaceBinding);

        if (profile == GraphicsDeviceProfile.Interactive)
        {
            if (presentationProbeBinding is not GraphicsNativeSurfaceBinding binding)
            {
                throw new ArgumentNullException(nameof(presentationProbeBinding));
            }

            if (binding.PlatformKind != platformKind)
            {
                throw new ArgumentException("The startup presentation surface does not match the initialized platform.", nameof(presentationProbeBinding));
            }

            interactiveProbeBinding = binding;
        }
        else if (presentationProbeBinding is not null)
        {
            throw new ArgumentException("OffscreenNative Vulkan devices cannot receive a native presentation surface.", nameof(presentationProbeBinding));
        }

        var instance = CreateInstance(profile, platformKind, cancellationToken);
        var hasInstance = true;
        var probeSurface = default(VkSurfaceKHR);
        var hasProbeSurface = false;
        var device = default(VkDevice);
        var hasDevice = false;
        var commandPool = default(VkCommandPool);
        var hasCommandPool = false;
        var timelineSemaphore = default(VkSemaphore);
        var hasTimelineSemaphore = false;
        var transferred = false;

        try
        {
            if (profile == GraphicsDeviceProfile.Interactive)
            {
                probeSurface = VulkanBackendSurface.CreateNativeSurface(
                    instance,
                    interactiveProbeBinding);
                hasProbeSurface = true;
            }

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
                    probeSurface,
                    out var capabilities,
                    out var memoryProperties,
                    out var queueFamilyIndex,
                    out var presentQueueFamilyIndex,
                    out var swapchainMaintenanceExtensionName,
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

                var (
                    logicalDevice,
                    enabledSpirvExtensions,
                    fragmentStoresAndAtomicsEnabled,
                    queueFamilyIndices,
                    queues) = CreateLogicalDevice(
                    physicalDevice,
                    queueFamilyIndex,
                    presentQueueFamilyIndex,
                    profile,
                    swapchainMaintenanceExtensionName,
                    cancellationToken);
                device = logicalDevice;
                hasDevice = true;
                afterNativeObjectAcquired?.Invoke(VulkanBackendStartupStage.LogicalDeviceCreated);
                cancellationToken.ThrowIfCancellationRequested();

                var queueIndex = Array.IndexOf(queueFamilyIndices, queueFamilyIndex);

                if (queueIndex < 0)
                {
                    throw new InvalidOperationException("Vulkan device creation omitted its graphics/compute queue family.");
                }

                var queue = queues[queueIndex];
                var commandBuffers = CreateCommandBuffers(
                    device,
                    queueFamilyIndex,
                    graphFrameSlotCount,
                    afterNativeObjectAcquired,
                    startupNativeResultOverride,
                    cancellationToken,
                    out commandPool);
                hasCommandPool = true;
                afterNativeObjectAcquired?.Invoke(VulkanBackendStartupStage.CommandBufferCreated);
                cancellationToken.ThrowIfCancellationRequested();

                timelineSemaphore = CreateTimelineSemaphore(device);
                hasTimelineSemaphore = true;
                afterNativeObjectAcquired?.Invoke(VulkanBackendStartupStage.TimelineSemaphoreCreated);
                cancellationToken.ThrowIfCancellationRequested();

                if (hasProbeSurface)
                {
                    VulkanApi.vkDestroySurfaceKHR(instance, probeSurface, null);
                    probeSurface = default;
                    hasProbeSurface = false;
                }

                var backend = new VulkanBackendDevice(
                    instance,
                    physicalDevice,
                    device,
                    queue,
                    queueFamilyIndex,
                    queueFamilyIndices,
                    queues,
                    memoryProperties,
                    commandPool,
                    commandBuffers,
                    timelineSemaphore,
                    capabilities,
                    enabledSpirvExtensions,
                    fragmentStoresAndAtomicsEnabled,
                    swapchainMaintenanceExtensionName,
                    platformKind,
                    transferWaitResultOverride,
                    graphFrameSlotCount,
                    graphFenceWaitObserver,
                    completedGraphFenceValueOverride,
                    graphSubmissionObserver,
                    surfaceImageAcquiredObserver);
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
                if (hasProbeSurface)
                {
                    VulkanApi.vkDestroySurfaceKHR(instance, probeSurface, null);
                }

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

    private static VkCommandBuffer[] CreateCommandBuffers(
        VkDevice device,
        uint queueFamilyIndex,
        int graphFrameSlotCount,
        Action<VulkanBackendStartupStage>? afterNativeObjectAcquired,
        Func<VulkanBackendStartupStage, VkResult, VkResult>? startupNativeResultOverride,
        CancellationToken cancellationToken,
        out VkCommandPool commandPool)
    {
        var poolCreateInfo = default(VkCommandPoolCreateInfo);
        poolCreateInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
        poolCreateInfo.flags = VkCommandPoolCreateFlags.VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
        poolCreateInfo.queueFamilyIndex = queueFamilyIndex;
        commandPool = default;
        var createdCommandPool = default(VkCommandPool);
        var nativeCommandPoolResult = VulkanApi.vkCreateCommandPool(device, &poolCreateInfo, null, &createdCommandPool);
        var commandPoolResult = nativeCommandPoolResult;

        if ((nativeCommandPoolResult == VkResult.VK_SUCCESS) && (startupNativeResultOverride is not null))
        {
            commandPoolResult = startupNativeResultOverride(
                VulkanBackendStartupStage.CommandPoolCreated,
                nativeCommandPoolResult);
        }

        if (commandPoolResult != VkResult.VK_SUCCESS)
        {
            if (nativeCommandPoolResult == VkResult.VK_SUCCESS)
            {
                VulkanApi.vkDestroyCommandPool(device, createdCommandPool, null);
            }

            ThrowIfFailed(commandPoolResult, "vkCreateCommandPool");
        }

        commandPool = createdCommandPool;

        try
        {
            afterNativeObjectAcquired?.Invoke(VulkanBackendStartupStage.CommandPoolCreated);
            cancellationToken.ThrowIfCancellationRequested();

            var allocateInfo = default(VkCommandBufferAllocateInfo);
            allocateInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
            allocateInfo.commandPool = commandPool;
            allocateInfo.level = VkCommandBufferLevel.VK_COMMAND_BUFFER_LEVEL_PRIMARY;
            var commandBuffers = new VkCommandBuffer[checked(graphFrameSlotCount + 1)];
            allocateInfo.commandBufferCount = checked((uint)commandBuffers.Length);
            VkResult allocationResult;

            fixed (VkCommandBuffer* commandBuffersPointer = commandBuffers)
            {
                allocationResult = VulkanApi.vkAllocateCommandBuffers(device, &allocateInfo, commandBuffersPointer);
            }

            if ((allocationResult == VkResult.VK_SUCCESS) && (startupNativeResultOverride is not null))
            {
                allocationResult = startupNativeResultOverride(
                    VulkanBackendStartupStage.CommandBufferAllocated,
                    allocationResult);
            }

            ThrowIfFailed(allocationResult, "vkAllocateCommandBuffers");
            afterNativeObjectAcquired?.Invoke(VulkanBackendStartupStage.CommandBufferAllocated);
            cancellationToken.ThrowIfCancellationRequested();
            return commandBuffers;
        }
        catch
        {
            VulkanApi.vkDestroyCommandPool(device, commandPool, null);
            commandPool = default;
            throw;
        }
    }

    private static VkInstance CreateInstance(
        GraphicsDeviceProfile profile,
        GraphicsPlatformKind platformKind,
        CancellationToken cancellationToken)
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
        var supportedExtensions = EnumerateInstanceExtensions();
        var enabledExtensions = new List<string>(4);

        if (profile == GraphicsDeviceProfile.Interactive)
        {
            var platformExtensionName = platformKind switch {
                GraphicsPlatformKind.Win32 => "VK_KHR_win32_surface",
                GraphicsPlatformKind.Wayland => "VK_KHR_wayland_surface",
                GraphicsPlatformKind.X11 => "VK_KHR_xlib_surface",
                _ => throw new PlatformNotSupportedException("The selected platform has no Vulkan WSI integration."),
            };
            var missingExtensions = new List<string>(4);
            AddRequiredInstanceExtension("VK_KHR_surface");
            AddRequiredInstanceExtension(platformExtensionName);
            AddRequiredInstanceExtension("VK_KHR_get_surface_capabilities2");
            var surfaceMaintenanceExtensionName = SelectExtensionAlias(
                supportedExtensions,
                "VK_KHR_surface_maintenance1",
                "VK_EXT_surface_maintenance1");

            if (surfaceMaintenanceExtensionName is null)
            {
                missingExtensions.Add("VK_KHR_surface_maintenance1 or VK_EXT_surface_maintenance1");
            }
            else
            {
                enabledExtensions.Add(surfaceMaintenanceExtensionName);
            }

            if (missingExtensions.Count != 0)
            {
                throw new NotSupportedException(
                    $"The Vulkan loader is missing required interactive instance extensions: {string.Join(", ", missingExtensions)}.");
            }

            void AddRequiredInstanceExtension(string extensionName)
            {
                if (!supportedExtensions.Contains(extensionName))
                {
                    missingExtensions.Add(extensionName);
                }
                else
                {
                    enabledExtensions.Add(extensionName);
                }
            }
        }

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

            if (enabledExtensions.Count != 0)
            {
                var nameStorageLength = 0;

                foreach (var extensionName in enabledExtensions)
                {
                    nameStorageLength = checked(nameStorageLength + Encoding.UTF8.GetByteCount(extensionName) + 1);
                }

                var nameStorage = stackalloc byte[nameStorageLength];
                var extensionNamePointers = stackalloc sbyte*[enabledExtensions.Count];
                var nameOffset = 0;

                for (var index = 0; index < enabledExtensions.Count; index++)
                {
                    var extensionName = enabledExtensions[index];
                    var written = Encoding.UTF8.GetBytes(
                        extensionName.AsSpan(),
                        new Span<byte>(nameStorage + nameOffset, nameStorageLength - nameOffset));
                    extensionNamePointers[index] = (sbyte*)(nameStorage + nameOffset);
                    nameOffset = checked(nameOffset + written);
                    nameStorage[nameOffset++] = 0;
                }

                createInfo.enabledExtensionCount = checked((uint)enabledExtensions.Count);
                createInfo.ppEnabledExtensionNames = extensionNamePointers;
            }

            ThrowIfFailed(VulkanApi.vkCreateInstance(&createInfo, null, &instance), "vkCreateInstance");
        }

        if (profile == GraphicsDeviceProfile.Interactive)
        {
            try
            {
                VulkanBackendSurface.ValidateInstanceWsiEntrypoints(instance, platformKind);
            }
            catch
            {
                VulkanApi.vkDestroyInstance(instance, null);
                throw;
            }
        }

        return instance;
    }

    private static (
        VkDevice Device,
        ImmutableHashSet<string> EnabledSpirvExtensions,
        bool FragmentStoresAndAtomicsEnabled,
        uint[] QueueFamilyIndices,
        VkQueue[] Queues) CreateLogicalDevice(
        VkPhysicalDevice physicalDevice,
        uint queueFamilyIndex,
        uint presentQueueFamilyIndex,
        GraphicsDeviceProfile profile,
        string? swapchainMaintenanceExtensionName,
        CancellationToken cancellationToken)
    {
        var queueFamilyIndices = GetEnabledQueueFamilyIndices(
            profile,
            queueFamilyIndex,
            presentQueueFamilyIndex);

        if (Array.IndexOf(queueFamilyIndices, queueFamilyIndex) < 0)
        {
            throw new InvalidOperationException("Vulkan logical-device queue families omitted the graphics/compute family.");
        }

        var priority = 1.0f;
        var queueCreateInfos = new VkDeviceQueueCreateInfo[queueFamilyIndices.Length];

        for (var index = 0; index < queueCreateInfos.Length; index++)
        {
            queueCreateInfos[index].sType = VkStructureType.VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO;
            queueCreateInfos[index].queueFamilyIndex = queueFamilyIndices[index];
            queueCreateInfos[index].queueCount = 1;
            queueCreateInfos[index].pQueuePriorities = &priority;
        }

        var features13 = default(VkPhysicalDeviceVulkan13Features);
        features13.sType = VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_FEATURES;
        features13.synchronization2 = 1;
        features13.dynamicRendering = 1;
        var swapchainMaintenanceFeatures = default(VkPhysicalDeviceSwapchainMaintenance1FeaturesKHR);

        if (profile == GraphicsDeviceProfile.Interactive)
        {
            swapchainMaintenanceFeatures.sType = (swapchainMaintenanceExtensionName == "VK_KHR_swapchain_maintenance1")
                ? VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SWAPCHAIN_MAINTENANCE_1_FEATURES_KHR
                : VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SWAPCHAIN_MAINTENANCE_1_FEATURES_EXT;
            swapchainMaintenanceFeatures.swapchainMaintenance1 = 1;
            features13.pNext = &swapchainMaintenanceFeatures;
        }

        var features12 = default(VkPhysicalDeviceVulkan12Features);
        features12.sType = VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES;
        features12.pNext = &features13;
        features12.timelineSemaphore = 1;
        var supportedFeatures = default(VkPhysicalDeviceFeatures);
        VulkanApi.vkGetPhysicalDeviceFeatures(physicalDevice, &supportedFeatures);
        var enabledFeatures = default(VkPhysicalDeviceFeatures);
        enabledFeatures.fragmentStoresAndAtomics = supportedFeatures.fragmentStoresAndAtomics;
        var supportedExtensions = EnumerateDeviceExtensions(physicalDevice);
        var enabledSpirvExtensions = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        var enabledExtensionNames = new List<string>(4);
        VkDevice device = default;
        cancellationToken.ThrowIfCancellationRequested();

        if (profile == GraphicsDeviceProfile.Interactive)
        {
            enabledExtensionNames.Add("VK_KHR_swapchain");
            enabledExtensionNames.Add(
                swapchainMaintenanceExtensionName
                    ?? throw new InvalidOperationException("Interactive Vulkan device admission has no maintenance extension."));
        }

        if (supportedExtensions.Contains("VK_GOOGLE_hlsl_functionality1"))
        {
            enabledExtensionNames.Add("VK_GOOGLE_hlsl_functionality1");
            _ = enabledSpirvExtensions.Add("SPV_GOOGLE_hlsl_functionality1");
        }

        if (supportedExtensions.Contains("VK_GOOGLE_user_type"))
        {
            enabledExtensionNames.Add("VK_GOOGLE_user_type");
            _ = enabledSpirvExtensions.Add("SPV_GOOGLE_user_type");
        }

        var extensionNameStorageLength = 0;

        foreach (var extensionName in enabledExtensionNames)
        {
            extensionNameStorageLength = checked(extensionNameStorageLength + Encoding.UTF8.GetByteCount(extensionName) + 1);
        }

        var extensionNameStorage = stackalloc byte[Math.Max(1, extensionNameStorageLength)];
        var extensionNamePointerStorage = stackalloc nint[Math.Max(1, enabledExtensionNames.Count)];
        var extensionNamePointers = (sbyte**)extensionNamePointerStorage;
        var extensionNameOffset = 0;

        for (var index = 0; index < enabledExtensionNames.Count; index++)
        {
            var extensionName = enabledExtensionNames[index];
            var written = Encoding.UTF8.GetBytes(
                extensionName.AsSpan(),
                new Span<byte>(extensionNameStorage + extensionNameOffset, extensionNameStorageLength - extensionNameOffset));
            extensionNamePointers[index] = (sbyte*)(extensionNameStorage + extensionNameOffset);
            extensionNameOffset = checked(extensionNameOffset + written);
            extensionNameStorage[extensionNameOffset++] = 0;
        }

        VkDeviceCreateInfo createInfo = default;
        createInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO;
        createInfo.pNext = &features12;
        createInfo.pEnabledFeatures = &enabledFeatures;
        createInfo.queueCreateInfoCount = checked((uint)queueCreateInfos.Length);
        createInfo.enabledExtensionCount = checked((uint)enabledExtensionNames.Count);
        createInfo.ppEnabledExtensionNames = (enabledExtensionNames.Count == 0) ? null : extensionNamePointers;
        VkQueue[] queues;

        fixed (VkDeviceQueueCreateInfo* queueCreateInfosPointer = queueCreateInfos)
        {
            createInfo.pQueueCreateInfos = queueCreateInfosPointer;
            ThrowIfFailed(VulkanApi.vkCreateDevice(physicalDevice, &createInfo, null, &device), "vkCreateDevice");
        }

        if (profile == GraphicsDeviceProfile.Interactive)
        {
            try
            {
                VulkanBackendSurface.ValidateDeviceWsiEntrypoints(device, swapchainMaintenanceExtensionName);
            }
            catch
            {
                VulkanApi.vkDestroyDevice(device, null);
                throw;
            }
        }

        queues = new VkQueue[queueFamilyIndices.Length];

        fixed (VkQueue* queuePointers = queues)
        {
            for (var index = 0; index < queues.Length; index++)
            {
                VulkanApi.vkGetDeviceQueue(device, queueFamilyIndices[index], 0, &queuePointers[index]);

            }
        }

        return (
            device,
            enabledSpirvExtensions.ToImmutable(),
            enabledFeatures.fragmentStoresAndAtomics != 0,
            queueFamilyIndices,
            queues);
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

    private static HashSet<string> EnumerateDeviceExtensions(VkPhysicalDevice physicalDevice)
    {
        uint count = 0;
        ThrowIfFailed(
            VulkanApi.vkEnumerateDeviceExtensionProperties(physicalDevice, null, &count, null),
            "vkEnumerateDeviceExtensionProperties");

        if (count == 0)
        {
            return [];
        }

        var properties = new VkExtensionProperties[checked((int)count)];
        var extensionNames = new HashSet<string>();

        fixed (VkExtensionProperties* propertiesPointer = properties)
        {
            ThrowIfFailed(
                VulkanApi.vkEnumerateDeviceExtensionProperties(
                    physicalDevice,
                    null,
                    &count,
                    propertiesPointer),
                "vkEnumerateDeviceExtensionProperties");

            for (var index = 0; index < checked((int)count); index++)
            {
                var extensionNamePointer = (byte*)&propertiesPointer[index]
                    + checked((int)Marshal.OffsetOf<VkExtensionProperties>("extensionName"));
                var extensionNameBytes = new ReadOnlySpan<byte>(extensionNamePointer, 256);
                var terminator = extensionNameBytes.IndexOf((byte)0);

                if (terminator < 0)
                {
                    throw new InvalidOperationException("Vulkan returned a device-extension name without a null terminator.");
                }

                var extensionName = Encoding.UTF8.GetString(extensionNameBytes[..terminator]);

                if (!extensionNames.Add(extensionName))
                {
                    throw new InvalidOperationException("Vulkan returned a duplicate device-extension name.");
                }
            }
        }

        return extensionNames;
    }

    private static HashSet<string> EnumerateInstanceExtensions()
    {
        uint count = 0;
        ThrowIfFailed(
            VulkanApi.vkEnumerateInstanceExtensionProperties(null, &count, null),
            "vkEnumerateInstanceExtensionProperties");

        if (count == 0)
        {
            return [];
        }

        var properties = new VkExtensionProperties[checked((int)count)];
        var extensionNames = new HashSet<string>(StringComparer.Ordinal);

        fixed (VkExtensionProperties* propertiesPointer = properties)
        {
            ThrowIfFailed(
                VulkanApi.vkEnumerateInstanceExtensionProperties(null, &count, propertiesPointer),
                "vkEnumerateInstanceExtensionProperties");

            for (var index = 0; index < checked((int)count); index++)
            {
                var extensionNamePointer = (byte*)&propertiesPointer[index]
                    + checked((int)Marshal.OffsetOf<VkExtensionProperties>("extensionName"));
                var extensionNameBytes = new ReadOnlySpan<byte>(extensionNamePointer, 256);
                var terminator = extensionNameBytes.IndexOf((byte)0);

                if (terminator < 0)
                {
                    throw new InvalidOperationException("Vulkan returned an instance-extension name without a null terminator.");
                }

                var extensionName = Encoding.UTF8.GetString(extensionNameBytes[..terminator]);

                if (!extensionNames.Add(extensionName))
                {
                    throw new InvalidOperationException("Vulkan returned a duplicate instance-extension name.");
                }
            }
        }

        return extensionNames;
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

    private static uint[] GetEnabledQueueFamilyIndices(
        GraphicsDeviceProfile profile,
        uint graphicsQueueFamilyIndex,
        uint presentQueueFamilyIndex)
    {
        if ((profile == GraphicsDeviceProfile.Interactive)
            && (presentQueueFamilyIndex != graphicsQueueFamilyIndex))
        {
            return [graphicsQueueFamilyIndex, presentQueueFamilyIndex];
        }

        return [graphicsQueueFamilyIndex];
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

    private static string? SelectExtensionAlias(
        HashSet<string> supportedExtensions,
        string preferredExtension,
        string fallbackExtension)
        => supportedExtensions.Contains(preferredExtension)
            ? preferredExtension
            : supportedExtensions.Contains(fallbackExtension)
                ? fallbackExtension
                : null;

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
        VkSurfaceKHR presentationSurface,
        out GraphicsCapabilities capabilities,
        out VkPhysicalDeviceMemoryProperties memoryProperties,
        out uint queueFamilyIndex,
        out uint presentQueueFamilyIndex,
        out string? swapchainMaintenanceExtensionName,
        out string rejection)
    {
        presentQueueFamilyIndex = 0;
        swapchainMaintenanceExtensionName = null;
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

        if (profile == GraphicsDeviceProfile.Interactive)
        {
            var supportedExtensions = EnumerateDeviceExtensions(physicalDevice);

            if (!supportedExtensions.Contains("VK_KHR_swapchain"))
            {
                capabilities = null!;
                memoryProperties = default;
                queueFamilyIndex = 0;
                rejection = $"{ReadDeviceName(properties2.properties)}: VK_KHR_swapchain device extension";
                return false;
            }

            swapchainMaintenanceExtensionName = SelectExtensionAlias(
                supportedExtensions,
                "VK_KHR_swapchain_maintenance1",
                "VK_EXT_swapchain_maintenance1");

            if (swapchainMaintenanceExtensionName is null)
            {
                capabilities = null!;
                memoryProperties = default;
                queueFamilyIndex = 0;
                rejection = $"{ReadDeviceName(properties2.properties)}: VK_KHR_swapchain_maintenance1 or VK_EXT_swapchain_maintenance1 device extension";
                return false;
            }
        }

        var features13 = default(VkPhysicalDeviceVulkan13Features);
        features13.sType = VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_FEATURES;
        var features12 = default(VkPhysicalDeviceVulkan12Features);
        features12.sType = VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES;
        features12.pNext = &features13;
        var swapchainMaintenanceFeatures = default(VkPhysicalDeviceSwapchainMaintenance1FeaturesKHR);

        if (swapchainMaintenanceExtensionName is not null)
        {
            swapchainMaintenanceFeatures.sType = (swapchainMaintenanceExtensionName == "VK_KHR_swapchain_maintenance1")
                ? VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SWAPCHAIN_MAINTENANCE_1_FEATURES_KHR
                : VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SWAPCHAIN_MAINTENANCE_1_FEATURES_EXT;
            features13.pNext = &swapchainMaintenanceFeatures;
        }

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

        if ((profile == GraphicsDeviceProfile.Interactive)
            && (swapchainMaintenanceFeatures.swapchainMaintenance1 == 0))
        {
            capabilities = null!;
            memoryProperties = default;
            queueFamilyIndex = 0;
            rejection = $"{ReadDeviceName(properties2.properties)}: swapchain maintenance1 feature";
            return false;
        }

        if (!TryGetGraphicsComputeQueueFamily(physicalDevice, out queueFamilyIndex))
        {
            capabilities = null!;
            memoryProperties = default;
            rejection = $"{ReadDeviceName(properties2.properties)}: graphics and compute queue family";
            return false;
        }

        presentQueueFamilyIndex = queueFamilyIndex;
        var physicalDeviceMemoryProperties = default(VkPhysicalDeviceMemoryProperties);
        VulkanApi.vkGetPhysicalDeviceMemoryProperties(physicalDevice, &physicalDeviceMemoryProperties);
        memoryProperties = physicalDeviceMemoryProperties;
        var adapterId = GetAdapterId(idProperties);
        var hardwareFeatures = ImmutableArray.CreateBuilder<GraphicsFeature>(5);
        var implementedFeatures = ImmutableArray.CreateBuilder<GraphicsFeature>(4);
        hardwareFeatures.Add(GraphicsFeature.TimelineSemaphore);
        hardwareFeatures.Add(GraphicsFeature.Synchronization2);
        hardwareFeatures.Add(GraphicsFeature.DynamicRendering);
        implementedFeatures.Add(GraphicsFeature.TimelineSemaphore);
        implementedFeatures.Add(GraphicsFeature.Synchronization2);
        implementedFeatures.Add(GraphicsFeature.DynamicRendering);

        if (profile == GraphicsDeviceProfile.Interactive)
        {
            hardwareFeatures.Add(GraphicsFeature.SwapchainMaintenance1);
            implementedFeatures.Add(GraphicsFeature.SwapchainMaintenance1);
        }

        if (features2.features.samplerAnisotropy != 0)
        {
            hardwareFeatures.Add(GraphicsFeature.SamplerAnisotropy);
        }

        if (features2.features.fragmentStoresAndAtomics != 0)
        {
            hardwareFeatures.Add(GraphicsFeature.FragmentStoresAndAtomics);
            implementedFeatures.Add(GraphicsFeature.FragmentStoresAndAtomics);
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
            hardwareFeatures.ToImmutable(),
            implementedFeatures.ToImmutable(),
            limits,
            GetFormatCapabilities(physicalDevice),
            GetMemoryHeapCapabilities(memoryProperties));

        if ((profile == GraphicsDeviceProfile.Interactive)
            && !VulkanBackendSurface.TryValidatePhysicalDeviceSurface(
                physicalDevice,
                presentationSurface,
                capabilities,
                queueFamilyIndex,
                out presentQueueFamilyIndex,
                out var surfaceRejection))
        {
            capabilities = null!;
            memoryProperties = default;
            queueFamilyIndex = 0;
            rejection = $"{ReadDeviceName(properties2.properties)}: {surfaceRejection}";
            return false;
        }

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

        if (_liveSurfaceCount != 0)
        {
            throw new InvalidOperationException("The Vulkan device still has live presentation surfaces.");
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

    private sealed class VulkanGraphFrameSlot
    {
        internal readonly VkCommandBuffer CommandBuffer;
        internal List<VulkanBackendDescriptorSet>? DescriptorSets;
        internal ulong FenceValue;
        internal VkImageView ImageView;

        internal VulkanGraphFrameSlot(VkCommandBuffer commandBuffer)
        {
            CommandBuffer = commandBuffer;
        }
    }
}
