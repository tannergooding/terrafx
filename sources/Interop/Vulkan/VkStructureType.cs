// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// Ported from src\spec\vk.xml in the Vulkan-Docs repository for tag v1.0.51-core
// Original source is Copyright © 2015-2017 The Khronos Group Inc.

namespace TerraFX.Interop
{
    public enum VkStructureType
    {
        VK_STRUCTURE_TYPE_APPLICATION_INFO = 0,

        VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO = 1,

        VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO = 2,

        VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO = 3,

        VK_STRUCTURE_TYPE_SUBMIT_INFO = 4,

        VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO = 5,

        VK_STRUCTURE_TYPE_MAPPED_MEMORY_RANGE = 6,

        VK_STRUCTURE_TYPE_BIND_SPARSE_INFO = 7,

        VK_STRUCTURE_TYPE_FENCE_CREATE_INFO = 8,

        VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO = 9,

        VK_STRUCTURE_TYPE_EVENT_CREATE_INFO = 10,

        VK_STRUCTURE_TYPE_QUERY_POOL_CREATE_INFO = 11,

        VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO = 12,

        VK_STRUCTURE_TYPE_BUFFER_VIEW_CREATE_INFO = 13,

        VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO = 14,

        VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO = 15,

        VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO = 16,

        VK_STRUCTURE_TYPE_PIPELINE_CACHE_CREATE_INFO = 17,

        VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO = 18,

        VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO = 19,

        VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO = 20,

        VK_STRUCTURE_TYPE_PIPELINE_TESSELLATION_STATE_CREATE_INFO = 21,

        VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO = 22,

        VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO = 23,

        VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO = 24,

        VK_STRUCTURE_TYPE_PIPELINE_DEPTH_STENCIL_STATE_CREATE_INFO = 25,

        VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO = 26,

        VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO = 27,

        VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO = 28,

        VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO = 29,

        VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO = 30,

        VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO = 31,

        VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO = 32,

        VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO = 33,

        VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO = 34,

        VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET = 35,

        VK_STRUCTURE_TYPE_COPY_DESCRIPTOR_SET = 36,

        VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO = 37,

        VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO = 38,

        VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO = 39,

        VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO = 40,

        VK_STRUCTURE_TYPE_COMMAND_BUFFER_INHERITANCE_INFO = 41,

        VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO = 42,

        VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO = 43,

        VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER = 44,

        VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER = 45,

        VK_STRUCTURE_TYPE_MEMORY_BARRIER = 46,

        VK_STRUCTURE_TYPE_LOADER_INSTANCE_CREATE_INFO = 47,

        VK_STRUCTURE_TYPE_LOADER_DEVICE_CREATE_INFO = 48,

        VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR = 1000001000,

        VK_STRUCTURE_TYPE_PRESENT_INFO_KHR = 1000001001,

        VK_STRUCTURE_TYPE_DISPLAY_MODE_CREATE_INFO_KHR = 1000002000,

        VK_STRUCTURE_TYPE_DISPLAY_SURFACE_CREATE_INFO_KHR = 1000002001,

        VK_STRUCTURE_TYPE_DISPLAY_PRESENT_INFO_KHR = 1000003000,

        VK_STRUCTURE_TYPE_XLIB_SURFACE_CREATE_INFO_KHR = 1000004000,

        VK_STRUCTURE_TYPE_XCB_SURFACE_CREATE_INFO_KHR = 1000005000,

        VK_STRUCTURE_TYPE_WAYLAND_SURFACE_CREATE_INFO_KHR = 1000006000,

        VK_STRUCTURE_TYPE_MIR_SURFACE_CREATE_INFO_KHR = 1000007000,

        VK_STRUCTURE_TYPE_ANDROID_SURFACE_CREATE_INFO_KHR = 1000008000,

        VK_STRUCTURE_TYPE_WIN32_SURFACE_CREATE_INFO_KHR = 1000009000,

        VK_STRUCTURE_TYPE_DEBUG_REPORT_CALLBACK_CREATE_INFO_EXT = 1000011000,

        VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_RASTERIZATION_ORDER_AMD = 1000018000,

        VK_STRUCTURE_TYPE_DEBUG_MARKER_OBJECT_NAME_INFO_EXT = 1000022000,

        VK_STRUCTURE_TYPE_DEBUG_MARKER_OBJECT_TAG_INFO_EXT = 1000022001,

        VK_STRUCTURE_TYPE_DEBUG_MARKER_MARKER_INFO_EXT = 1000022002,

        VK_STRUCTURE_TYPE_DEDICATED_ALLOCATION_IMAGE_CREATE_INFO_NV = 1000026000,

        VK_STRUCTURE_TYPE_DEDICATED_ALLOCATION_BUFFER_CREATE_INFO_NV = 1000026001,

        VK_STRUCTURE_TYPE_DEDICATED_ALLOCATION_MEMORY_ALLOCATE_INFO_NV = 1000026002,

        VK_STRUCTURE_TYPE_RENDER_PASS_MULTIVIEW_CREATE_INFO_KHX = 1000053000,

        VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_MULTIVIEW_FEATURES_KHX = 1000053001,

        VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_MULTIVIEW_PROPERTIES_KHX = 1000053002,

        VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO_NV = 1000056000,

        VK_STRUCTURE_TYPE_EXPORT_MEMORY_ALLOCATE_INFO_NV = 1000056001,

        VK_STRUCTURE_TYPE_IMPORT_MEMORY_WIN32_HANDLE_INFO_NV = 1000057000,

        VK_STRUCTURE_TYPE_EXPORT_MEMORY_WIN32_HANDLE_INFO_NV = 1000057001,

        VK_STRUCTURE_TYPE_WIN32_KEYED_MUTEX_ACQUIRE_RELEASE_INFO_NV = 1000058000,

        VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2_KHR = 1000059000,

        VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PROPERTIES_2_KHR = 1000059001,

        VK_STRUCTURE_TYPE_FORMAT_PROPERTIES_2_KHR = 1000059002,

        VK_STRUCTURE_TYPE_IMAGE_FORMAT_PROPERTIES_2_KHR = 1000059003,

        VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_IMAGE_FORMAT_INFO_2_KHR = 1000059004,

        VK_STRUCTURE_TYPE_QUEUE_FAMILY_PROPERTIES_2_KHR = 1000059005,

        VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_MEMORY_PROPERTIES_2_KHR = 1000059006,

        VK_STRUCTURE_TYPE_SPARSE_IMAGE_FORMAT_PROPERTIES_2_KHR = 1000059007,

        VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SPARSE_IMAGE_FORMAT_INFO_2_KHR = 1000059008,

        VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_FLAGS_INFO_KHX = 1000060000,

        VK_STRUCTURE_TYPE_BIND_BUFFER_MEMORY_INFO_KHX = 1000060001,

        VK_STRUCTURE_TYPE_BIND_IMAGE_MEMORY_INFO_KHX = 1000060002,

        VK_STRUCTURE_TYPE_DEVICE_GROUP_RENDER_PASS_BEGIN_INFO_KHX = 1000060003,

        VK_STRUCTURE_TYPE_DEVICE_GROUP_COMMAND_BUFFER_BEGIN_INFO_KHX = 1000060004,

        VK_STRUCTURE_TYPE_DEVICE_GROUP_SUBMIT_INFO_KHX = 1000060005,

        VK_STRUCTURE_TYPE_DEVICE_GROUP_BIND_SPARSE_INFO_KHX = 1000060006,

        VK_STRUCTURE_TYPE_DEVICE_GROUP_PRESENT_CAPABILITIES_KHX = 1000060007,

        VK_STRUCTURE_TYPE_IMAGE_SWAPCHAIN_CREATE_INFO_KHX = 1000060008,

        VK_STRUCTURE_TYPE_BIND_IMAGE_MEMORY_SWAPCHAIN_INFO_KHX = 1000060009,

        VK_STRUCTURE_TYPE_ACQUIRE_NEXT_IMAGE_INFO_KHX = 1000060010,

        VK_STRUCTURE_TYPE_DEVICE_GROUP_PRESENT_INFO_KHX = 1000060011,

        VK_STRUCTURE_TYPE_DEVICE_GROUP_SWAPCHAIN_CREATE_INFO_KHX = 1000060012,

        VK_STRUCTURE_TYPE_VALIDATION_FLAGS_EXT = 1000061000,

        VK_STRUCTURE_TYPE_VI_SURFACE_CREATE_INFO_NN = 1000062000,

        VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_GROUP_PROPERTIES_KHX = 1000070000,

        VK_STRUCTURE_TYPE_DEVICE_GROUP_DEVICE_CREATE_INFO_KHX = 1000070001,

        VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_EXTERNAL_IMAGE_FORMAT_INFO_KHX = 1000071000,

        VK_STRUCTURE_TYPE_EXTERNAL_IMAGE_FORMAT_PROPERTIES_KHX = 1000071001,

        VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_EXTERNAL_BUFFER_INFO_KHX = 1000071002,

        VK_STRUCTURE_TYPE_EXTERNAL_BUFFER_PROPERTIES_KHX = 1000071003,

        VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_ID_PROPERTIES_KHX = 1000071004,

        VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_BUFFER_CREATE_INFO_KHX = 1000072000,

        VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO_KHX = 1000072001,

        VK_STRUCTURE_TYPE_EXPORT_MEMORY_ALLOCATE_INFO_KHX = 1000072002,

        VK_STRUCTURE_TYPE_IMPORT_MEMORY_WIN32_HANDLE_INFO_KHX = 1000073000,

        VK_STRUCTURE_TYPE_EXPORT_MEMORY_WIN32_HANDLE_INFO_KHX = 1000073001,

        VK_STRUCTURE_TYPE_MEMORY_WIN32_HANDLE_PROPERTIES_KHX = 1000073002,

        VK_STRUCTURE_TYPE_IMPORT_MEMORY_FD_INFO_KHX = 1000074000,

        VK_STRUCTURE_TYPE_MEMORY_FD_PROPERTIES_KHX = 1000074001,

        VK_STRUCTURE_TYPE_WIN32_KEYED_MUTEX_ACQUIRE_RELEASE_INFO_KHX = 1000075000,

        VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_EXTERNAL_SEMAPHORE_INFO_KHX = 1000076000,

        VK_STRUCTURE_TYPE_EXTERNAL_SEMAPHORE_PROPERTIES_KHX = 1000076001,

        VK_STRUCTURE_TYPE_EXPORT_SEMAPHORE_CREATE_INFO_KHX = 1000077000,

        VK_STRUCTURE_TYPE_IMPORT_SEMAPHORE_WIN32_HANDLE_INFO_KHX = 1000078000,

        VK_STRUCTURE_TYPE_EXPORT_SEMAPHORE_WIN32_HANDLE_INFO_KHX = 1000078001,

        VK_STRUCTURE_TYPE_D3D12_FENCE_SUBMIT_INFO_KHX = 1000078002,

        VK_STRUCTURE_TYPE_IMPORT_SEMAPHORE_FD_INFO_KHX = 1000079000,

        VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PUSH_DESCRIPTOR_PROPERTIES_KHR = 1000080000,

        VK_STRUCTURE_TYPE_PRESENT_REGIONS_KHR = 1000084000,

        VK_STRUCTURE_TYPE_DESCRIPTOR_UPDATE_TEMPLATE_CREATE_INFO_KHR = 1000085000,

        VK_STRUCTURE_TYPE_OBJECT_TABLE_CREATE_INFO_NVX = 1000086000,

        VK_STRUCTURE_TYPE_INDIRECT_COMMANDS_LAYOUT_CREATE_INFO_NVX = 1000086001,

        VK_STRUCTURE_TYPE_CMD_PROCESS_COMMANDS_INFO_NVX = 1000086002,

        VK_STRUCTURE_TYPE_CMD_RESERVE_SPACE_FOR_COMMANDS_INFO_NVX = 1000086003,

        VK_STRUCTURE_TYPE_DEVICE_GENERATED_COMMANDS_LIMITS_NVX = 1000086004,

        VK_STRUCTURE_TYPE_DEVICE_GENERATED_COMMANDS_FEATURES_NVX = 1000086005,

        VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_W_SCALING_STATE_CREATE_INFO_NV = 1000087000,

        VK_STRUCTURE_TYPE_SURFACE_CAPABILITIES2_EXT = 1000090000,

        VK_STRUCTURE_TYPE_DISPLAY_POWER_INFO_EXT = 1000091000,

        VK_STRUCTURE_TYPE_DEVICE_EVENT_INFO_EXT = 1000091001,

        VK_STRUCTURE_TYPE_DISPLAY_EVENT_INFO_EXT = 1000091002,

        VK_STRUCTURE_TYPE_SWAPCHAIN_COUNTER_CREATE_INFO_EXT = 1000091003,

        VK_STRUCTURE_TYPE_PRESENT_TIMES_INFO_GOOGLE = 1000092000,

        VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_MULTIVIEW_PER_VIEW_ATTRIBUTES_PROPERTIES_NVX = 1000097000,

        VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_SWIZZLE_STATE_CREATE_INFO_NV = 1000098000,

        VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DISCARD_RECTANGLE_PROPERTIES_EXT = 1000099000,

        VK_STRUCTURE_TYPE_PIPELINE_DISCARD_RECTANGLE_STATE_CREATE_INFO_EXT = 1000099001,

        VK_STRUCTURE_TYPE_HDR_METADATA_EXT = 1000105000,

        VK_STRUCTURE_TYPE_SHARED_PRESENT_SURFACE_CAPABILITIES_KHR = 1000111000,

        VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SURFACE_INFO_2_KHR = 1000119000,

        VK_STRUCTURE_TYPE_SURFACE_CAPABILITIES_2_KHR = 1000119001,

        VK_STRUCTURE_TYPE_SURFACE_FORMAT_2_KHR = 1000119002,

        VK_STRUCTURE_TYPE_IOS_SURFACE_CREATE_INFO_MVK = 1000122000,

        VK_STRUCTURE_TYPE_MACOS_SURFACE_CREATE_INFO_MVK = 1000123000,

        VK_STRUCTURE_TYPE_BEGIN_RANGE = VK_STRUCTURE_TYPE_APPLICATION_INFO,

        VK_STRUCTURE_TYPE_END_RANGE = VK_STRUCTURE_TYPE_LOADER_DEVICE_CREATE_INFO,

        VK_STRUCTURE_TYPE_RANGE_SIZE = VK_STRUCTURE_TYPE_LOADER_DEVICE_CREATE_INFO - VK_STRUCTURE_TYPE_APPLICATION_INFO + 1,

        VK_STRUCTURE_TYPE_MAX_ENUM = 0x7FFFFFFF
    }
}
