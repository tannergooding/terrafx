// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Graphics;
using TerraFX.Interop.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal static class VulkanGraphResourceStateMapper
{
    private const VkPipelineStageFlags2 SupportedShaderStages =
        VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_VERTEX_SHADER_BIT
        | VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT
        | VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT;

    internal static VkAccessFlags2 GetAccessMask(GraphResourceState state)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        return state switch {
            GraphResourceState.Undefined or GraphResourceState.Present => VkAccessFlags2.VK_ACCESS_2_NONE,
            GraphResourceState.CopySource => VkAccessFlags2.VK_ACCESS_2_TRANSFER_READ_BIT,
            GraphResourceState.CopyDestination => VkAccessFlags2.VK_ACCESS_2_TRANSFER_WRITE_BIT,
            GraphResourceState.VertexRead => VkAccessFlags2.VK_ACCESS_2_VERTEX_ATTRIBUTE_READ_BIT,
            GraphResourceState.IndexRead => VkAccessFlags2.VK_ACCESS_2_INDEX_READ_BIT,
            GraphResourceState.UniformRead => VkAccessFlags2.VK_ACCESS_2_UNIFORM_READ_BIT,
            GraphResourceState.ShaderSampledRead => VkAccessFlags2.VK_ACCESS_2_SHADER_SAMPLED_READ_BIT,
            GraphResourceState.StorageRead => VkAccessFlags2.VK_ACCESS_2_SHADER_STORAGE_READ_BIT,
            GraphResourceState.StorageWrite => VkAccessFlags2.VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT,
            GraphResourceState.ColorAttachment
                => VkAccessFlags2.VK_ACCESS_2_COLOR_ATTACHMENT_READ_BIT
                    | VkAccessFlags2.VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT,
            GraphResourceState.DepthRead => VkAccessFlags2.VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT,
            GraphResourceState.DepthWrite
                => VkAccessFlags2.VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT
                    | VkAccessFlags2.VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT,
            GraphResourceState.General
                => throw new InvalidOperationException("The neutral state requires the resource owner's actual access scope."),
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
    }

    internal static VkImageLayout GetImageLayout(GraphResourceState state)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        return state switch {
            GraphResourceState.Undefined => VkImageLayout.VK_IMAGE_LAYOUT_UNDEFINED,
            GraphResourceState.CopySource => VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
            GraphResourceState.CopyDestination => VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
            GraphResourceState.ShaderSampledRead => VkImageLayout.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
            GraphResourceState.StorageRead or GraphResourceState.StorageWrite or GraphResourceState.General
                => VkImageLayout.VK_IMAGE_LAYOUT_GENERAL,
            GraphResourceState.ColorAttachment => VkImageLayout.VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,
            GraphResourceState.DepthRead => VkImageLayout.VK_IMAGE_LAYOUT_DEPTH_READ_ONLY_OPTIMAL,
            GraphResourceState.DepthWrite => VkImageLayout.VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL,
            GraphResourceState.Present => VkImageLayout.VK_IMAGE_LAYOUT_PRESENT_SRC_KHR,
            GraphResourceState.VertexRead or GraphResourceState.IndexRead or GraphResourceState.UniformRead
                => throw new ArgumentException("The selected graph state is buffer-only.", nameof(state)),
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
    }

    internal static VkPipelineStageFlags2 GetStageMask(
        GraphResourceState state,
        VkPipelineStageFlags2 shaderStages = VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        if (state is GraphResourceState.UniformRead
            or GraphResourceState.ShaderSampledRead
            or GraphResourceState.StorageRead
            or GraphResourceState.StorageWrite)
        {
            if ((shaderStages == VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE)
                || ((shaderStages & ~SupportedShaderStages) != VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE))
            {
                throw new ArgumentOutOfRangeException(nameof(shaderStages));
            }

            return shaderStages;
        }

        if (shaderStages != VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE)
        {
            throw new ArgumentException("Shader stages are only valid for shader resource accesses.", nameof(shaderStages));
        }

        return state switch {
            GraphResourceState.Undefined or GraphResourceState.Present
                => VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_NONE,
            GraphResourceState.CopySource or GraphResourceState.CopyDestination
                => VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT,
            GraphResourceState.VertexRead or GraphResourceState.IndexRead
                => VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_VERTEX_INPUT_BIT,
            GraphResourceState.ColorAttachment
                => VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT,
            GraphResourceState.DepthRead or GraphResourceState.DepthWrite
                => VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT
                    | VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT,
            GraphResourceState.General
                => throw new InvalidOperationException("The neutral state requires the resource owner's actual stage scope."),
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
    }
}
