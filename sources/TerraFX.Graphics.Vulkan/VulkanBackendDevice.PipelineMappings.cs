// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Buffers;
using TerraFX.Graphics;
using TerraFX.Interop.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal sealed unsafe partial class VulkanBackendDevice
{
    private static VkPipelineColorBlendAttachmentState CreateColorBlendAttachment(
        PipelineBlendMode blendMode,
        TextureFormat format)
    {
        var attachment = default(VkPipelineColorBlendAttachmentState);
        attachment.colorWriteMask =
            VkColorComponentFlags.VK_COLOR_COMPONENT_R_BIT
            | VkColorComponentFlags.VK_COLOR_COMPONENT_G_BIT
            | VkColorComponentFlags.VK_COLOR_COMPONENT_B_BIT
            | VkColorComponentFlags.VK_COLOR_COMPONENT_A_BIT;

        if (blendMode == PipelineBlendMode.PremultipliedAlpha)
        {
            if (format == TextureFormat.R32UInt)
            {
                throw new NotSupportedException("Vulkan blending is not supported for the unsigned integer target format.");
            }

            attachment.blendEnable = 1;
            attachment.srcColorBlendFactor = VkBlendFactor.VK_BLEND_FACTOR_ONE;
            attachment.dstColorBlendFactor = VkBlendFactor.VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
            attachment.colorBlendOp = VkBlendOp.VK_BLEND_OP_ADD;
            attachment.srcAlphaBlendFactor = VkBlendFactor.VK_BLEND_FACTOR_ONE;
            attachment.dstAlphaBlendFactor = VkBlendFactor.VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
            attachment.alphaBlendOp = VkBlendOp.VK_BLEND_OP_ADD;
        }

        return attachment;
    }

    private static VkCullModeFlags GetNativeCullMode(PipelineCullMode cullMode)
        => cullMode switch {
            PipelineCullMode.None => default,
            PipelineCullMode.Front => VkCullModeFlags.VK_CULL_MODE_FRONT_BIT,
            PipelineCullMode.Back => VkCullModeFlags.VK_CULL_MODE_BACK_BIT,
            _ => throw new ArgumentOutOfRangeException(nameof(cullMode)),
        };

    private static uint GetNativeDescriptorBinding(ShaderResourceSignature resource)
    {
        var bindingClassOffset = resource.Kind switch {
            ShaderResourceKind.UniformBuffer => 0,
            ShaderResourceKind.SampledImage => 16,
            ShaderResourceKind.StorageImage or ShaderResourceKind.StorageBuffer => 32,
            ShaderResourceKind.Sampler => 40,
            _ => throw new ArgumentOutOfRangeException(nameof(resource)),
        };
        return checked((uint)(bindingClassOffset + resource.Binding));
    }

    private static uint GetNativeDescriptorSetIndex(ShaderBindingGroup group)
        => group switch {
            ShaderBindingGroup.Frame => 0,
            ShaderBindingGroup.Material => 1,
            ShaderBindingGroup.ObjectData => 2,
            ShaderBindingGroup.Pass => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(group)),
        };

    private static VkDescriptorType GetNativeDescriptorType(ShaderResourceKind kind)
        => kind switch {
            ShaderResourceKind.UniformBuffer => VkDescriptorType.VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER,
            ShaderResourceKind.SampledImage => VkDescriptorType.VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE,
            ShaderResourceKind.StorageImage => VkDescriptorType.VK_DESCRIPTOR_TYPE_STORAGE_IMAGE,
            ShaderResourceKind.StorageBuffer => VkDescriptorType.VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,
            ShaderResourceKind.Sampler => VkDescriptorType.VK_DESCRIPTOR_TYPE_SAMPLER,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static VkShaderStageFlags GetNativeShaderStage(ShaderStage stage)
        => stage switch {
            ShaderStage.Vertex => VkShaderStageFlags.VK_SHADER_STAGE_VERTEX_BIT,
            ShaderStage.Pixel => VkShaderStageFlags.VK_SHADER_STAGE_FRAGMENT_BIT,
            ShaderStage.Compute => VkShaderStageFlags.VK_SHADER_STAGE_COMPUTE_BIT,
            _ => throw new ArgumentOutOfRangeException(nameof(stage)),
        };

    private static VkFormat GetNativeVertexFormat(VertexElementFormat format)
        => format switch {
            VertexElementFormat.Float2 => VkFormat.VK_FORMAT_R32G32_SFLOAT,
            VertexElementFormat.Float3 => VkFormat.VK_FORMAT_R32G32B32_SFLOAT,
            VertexElementFormat.Float4 => VkFormat.VK_FORMAT_R32G32B32A32_SFLOAT,
            VertexElementFormat.Half2 => VkFormat.VK_FORMAT_R16G16_SFLOAT,
            VertexElementFormat.Half4 => VkFormat.VK_FORMAT_R16G16B16A16_SFLOAT,
            VertexElementFormat.UNorm8x4 => VkFormat.VK_FORMAT_R8G8B8A8_UNORM,
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

    private static ImmutableArray<ShaderBytecodeInput> GetPipelineShaderInputs(PipelineDescription description)
        => description switch {
            ComputePipelineDescription computeDescription => [computeDescription.Shader],
            GraphicsPipelineDescription graphicsDescription => graphicsDescription.Shaders,
            _ => throw new ArgumentOutOfRangeException(nameof(description)),
        };

    private static void ValidateShaderInput(ShaderBytecodeInput shader, BufferReadLease<byte> bytecode, int index)
    {
        ArgumentNullException.ThrowIfNull(shader);
        ArgumentNullException.ThrowIfNull(bytecode);

        if (shader.Format != ShaderBinaryFormat.SpirV)
        {
            throw new NotSupportedException("The Vulkan backend accepts only SPIR-V shader binaries.");
        }

        if (shader.Reflection.Stage != shader.Stage)
        {
            throw new ArgumentException($"The Vulkan shader input at index {index} is inconsistent.", nameof(shader));
        }

        if (!shader.Reflection.RequiredCapabilities.IsEmpty)
        {
            throw new NotSupportedException("The Vulkan pipeline baseline does not implement nonbaseline shader capabilities.");
        }

        if (shader.Reflection.EntryPoint.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("Shader entry-point names cannot contain null characters.", nameof(shader));
        }

        if ((bytecode.Length <= 0) || ((bytecode.Length % sizeof(uint)) != 0))
        {
            throw new ArgumentException("SPIR-V bytecode length must be a positive multiple of four.", nameof(bytecode));
        }
    }
}
