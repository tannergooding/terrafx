// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using TerraFX.Buffers;
using TerraFX.Graphics;
using TerraFX.Interop.Vulkan;
using VulkanApi = TerraFX.Interop.Vulkan.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal sealed unsafe partial class VulkanBackendDevice
{
    private int _livePipelineCount;

    private static ImmutableArray<ShaderResourceSignature> GetPipelineResources(
        ImmutableArray<ShaderBytecodeInput> shaderInputs)
    {
        var resources = ImmutableArray.CreateBuilder<ShaderResourceSignature>();
        var uniqueResources = new HashSet<ShaderResourceSignature>();

        for (var shaderIndex = 0; shaderIndex < shaderInputs.Length; shaderIndex++)
        {
            var shaderResources = shaderInputs[shaderIndex].Reflection.Resources;

            for (var resourceIndex = 0; resourceIndex < shaderResources.Length; resourceIndex++)
            {
                var resource = shaderResources[resourceIndex];

                if (uniqueResources.Add(resource))
                {
                    resources.Add(resource);
                }
            }
        }

        return resources.ToImmutable();
    }

    internal VulkanBackendPipeline CreatePipeline(
        PipelineDescription description,
        ReadOnlySpan<BufferReadLease<byte>> bytecodeLeases)
    {
        ArgumentNullException.ThrowIfNull(description);
        EnsureActive();
        var shaderInputs = GetPipelineShaderInputs(description);

        if (shaderInputs.Length != bytecodeLeases.Length)
        {
            throw new ArgumentException("The pipeline bytecode lease count does not match its shader stages.", nameof(bytecodeLeases));
        }

        for (var index = 0; index < shaderInputs.Length; index++)
        {
            ValidateShaderInput(shaderInputs[index], bytecodeLeases[index], index);
        }

        switch (description)
        {
            case ComputePipelineDescription computeDescription:
            {
                ValidateComputePipeline(computeDescription);
                break;
            }

            case GraphicsPipelineDescription graphicsDescription:
            {
                ValidateGraphicsPipeline(graphicsDescription);
                break;
            }

            default:
            {
                throw new ArgumentOutOfRangeException(nameof(description));
            }
        }

        var shaderModules = new VkShaderModule[shaderInputs.Length];
        var (pipelineLayout, descriptorSetLayouts) = CreatePipelineLayout(shaderInputs);
        var ownsPipelineLayout = true;
        var ownsDescriptorSetLayouts = true;
        var createdShaderModuleCount = 0;
        VkPipeline pipeline = default;
        var ownsPipeline = false;

        try
        {
            for (var index = 0; index < shaderInputs.Length; index++)
            {
                shaderModules[index] = CreateShaderModule(bytecodeLeases[index]);
                createdShaderModuleCount++;
            }

            pipeline = description switch {
                ComputePipelineDescription computeDescription => CreateComputePipeline(
                    computeDescription,
                    shaderModules[0],
                    pipelineLayout),
                GraphicsPipelineDescription graphicsDescription => CreateGraphicsPipeline(
                    graphicsDescription,
                    shaderModules[0],
                    shaderModules[1],
                    pipelineLayout),
                _ => throw new ArgumentOutOfRangeException(nameof(description)),
            };
            ownsPipeline = true;
            var result = new VulkanBackendPipeline(
                this,
                pipeline,
                pipelineLayout,
                descriptorSetLayouts,
                GetPipelineResources(shaderInputs));
            _livePipelineCount = checked(_livePipelineCount + 1);
            ownsPipeline = false;
            ownsPipelineLayout = false;
            ownsDescriptorSetLayouts = false;
            return result;
        }
        finally
        {
            for (var index = 0; index < createdShaderModuleCount; index++)
            {
                VulkanApi.vkDestroyShaderModule(_device, shaderModules[index], null);
            }

            if (ownsPipeline)
            {
                VulkanApi.vkDestroyPipeline(_device, pipeline, null);
            }

            if (ownsPipelineLayout)
            {
                VulkanApi.vkDestroyPipelineLayout(_device, pipelineLayout, null);
            }

            if (ownsDescriptorSetLayouts)
            {
                DestroyDescriptorSetLayouts(descriptorSetLayouts);
            }
        }
    }

    internal void DestroyPipeline(VulkanBackendPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        EnsureOwnerThread();

        if (!ReferenceEquals(pipeline.Owner, this))
        {
            throw new ArgumentException("The pipeline belongs to a different Vulkan device.", nameof(pipeline));
        }

        if (pipeline.IsDisposed)
        {
            return;
        }

        if (_hasUncertainSubmission)
        {
            throw new InvalidOperationException("Vulkan pipeline retirement cannot release storage while submission completion is uncertain.");
        }

        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (pipeline.DescriptorSetReferenceCount != 0)
        {
            throw new InvalidOperationException("Vulkan pipeline retirement cannot release layouts while descriptor sets remain live.");
        }

        VulkanApi.vkDestroyPipeline(_device, pipeline.Pipeline, null);
        VulkanApi.vkDestroyPipelineLayout(_device, pipeline.PipelineLayout, null);
        DestroyDescriptorSetLayouts(pipeline.DescriptorSetLayouts);
        pipeline.MarkDisposed();
        _livePipelineCount = checked(_livePipelineCount - 1);
    }

    private VkPipeline CreateComputePipeline(
        ComputePipelineDescription description,
        VkShaderModule shaderModule,
        VkPipelineLayout pipelineLayout)
    {
        var entryPoint = Encoding.UTF8.GetBytes(description.Shader.Reflection.EntryPoint + "\0");

        fixed (byte* entryPointPointer = entryPoint)
        {
            var stage = default(VkPipelineShaderStageCreateInfo);
            stage.sType = VkStructureType.VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
            stage.stage = GetNativeShaderStage(ShaderStage.Compute);
            stage.module = shaderModule;
            stage.pName = (sbyte*)entryPointPointer;
            var createInfo = default(VkComputePipelineCreateInfo);
            createInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO;
            createInfo.stage = stage;
            createInfo.layout = pipelineLayout;
            createInfo.basePipelineIndex = -1;
            VkPipeline pipeline = default;
            ThrowIfFailed(
                VulkanApi.vkCreateComputePipelines(_device, default, 1, &createInfo, null, &pipeline),
                "vkCreateComputePipelines");
            return pipeline;
        }
    }

    private VkPipeline CreateGraphicsPipeline(
        GraphicsPipelineDescription description,
        VkShaderModule vertexShader,
        VkShaderModule pixelShader,
        VkPipelineLayout pipelineLayout)
    {
        var vertexEntryPoint = Encoding.UTF8.GetBytes(description.Shaders[0].Reflection.EntryPoint + "\0");
        var pixelEntryPoint = Encoding.UTF8.GetBytes(description.Shaders[1].Reflection.EntryPoint + "\0");

        fixed (byte* vertexEntryPointPointer = vertexEntryPoint)
        fixed (byte* pixelEntryPointPointer = pixelEntryPoint)
        {
            var shaderStages = stackalloc VkPipelineShaderStageCreateInfo[2];
            shaderStages[0] = default;
            shaderStages[1] = default;
            shaderStages[0].sType = VkStructureType.VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
            shaderStages[0].stage = GetNativeShaderStage(ShaderStage.Vertex);
            shaderStages[0].module = vertexShader;
            shaderStages[0].pName = (sbyte*)vertexEntryPointPointer;
            shaderStages[1].sType = VkStructureType.VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
            shaderStages[1].stage = GetNativeShaderStage(ShaderStage.Pixel);
            shaderStages[1].module = pixelShader;
            shaderStages[1].pName = (sbyte*)pixelEntryPointPointer;

            var vertexElementCount = description.VertexElements.Length;
            var vertexBindings = stackalloc VkVertexInputBindingDescription[1];
            var vertexAttributes =
                stackalloc VkVertexInputAttributeDescription[Math.Max(vertexElementCount, 1)];
            var vertexInput = default(VkPipelineVertexInputStateCreateInfo);
            vertexInput.sType = VkStructureType.VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;

            if (vertexElementCount > 0)
            {
                vertexBindings[0].binding = 0;
                vertexBindings[0].stride = checked((uint)description.VertexStride);
                vertexBindings[0].inputRate = VkVertexInputRate.VK_VERTEX_INPUT_RATE_VERTEX;
                vertexInput.vertexBindingDescriptionCount = 1;
                vertexInput.pVertexBindingDescriptions = vertexBindings;
                vertexInput.vertexAttributeDescriptionCount = checked((uint)vertexElementCount);
                vertexInput.pVertexAttributeDescriptions = vertexAttributes;

                for (var index = 0; index < vertexElementCount; index++)
                {
                    var element = description.VertexElements[index];
                    vertexAttributes[index].location = checked((uint)element.Location);
                    vertexAttributes[index].binding = 0;
                    vertexAttributes[index].format = GetNativeVertexFormat(element.Format);
                    vertexAttributes[index].offset = checked((uint)element.ByteOffset);
                }
            }

            var inputAssembly = default(VkPipelineInputAssemblyStateCreateInfo);
            inputAssembly.sType = VkStructureType.VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
            inputAssembly.topology = description.Topology switch {
                PrimitiveTopology.TriangleList => VkPrimitiveTopology.VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST,
                _ => throw new ArgumentOutOfRangeException(nameof(description)),
            };
            var viewport = default(VkPipelineViewportStateCreateInfo);
            viewport.sType = VkStructureType.VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
            viewport.viewportCount = 1;
            viewport.scissorCount = 1;
            var rasterization = default(VkPipelineRasterizationStateCreateInfo);
            rasterization.sType = VkStructureType.VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
            rasterization.polygonMode = VkPolygonMode.VK_POLYGON_MODE_FILL;
            rasterization.cullMode = GetNativeCullMode(description.CullMode);
            rasterization.frontFace = description.FrontFaceClockwise
                ? VkFrontFace.VK_FRONT_FACE_CLOCKWISE
                : VkFrontFace.VK_FRONT_FACE_COUNTER_CLOCKWISE;
            rasterization.lineWidth = 1.0f;
            var multisample = default(VkPipelineMultisampleStateCreateInfo);
            multisample.sType = VkStructureType.VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
            multisample.rasterizationSamples = VkSampleCountFlags.VK_SAMPLE_COUNT_1_BIT;
            var depthStencil = default(VkPipelineDepthStencilStateCreateInfo);
            depthStencil.sType = VkStructureType.VK_STRUCTURE_TYPE_PIPELINE_DEPTH_STENCIL_STATE_CREATE_INFO;
            depthStencil.depthTestEnable = description.DepthMode == PipelineDepthMode.Disabled ? 0u : 1u;
            depthStencil.depthWriteEnable = description.DepthMode == PipelineDepthMode.ReversedGreaterEqualWrite ? 1u : 0u;
            depthStencil.depthCompareOp = description.DepthMode == PipelineDepthMode.Disabled
                ? VkCompareOp.VK_COMPARE_OP_ALWAYS
                : VkCompareOp.VK_COMPARE_OP_GREATER_OR_EQUAL;
            depthStencil.maxDepthBounds = 1.0f;
            var attachmentCount = description.ColorTargetFormats.Length;
            var colorFormats = stackalloc VkFormat[Math.Max(attachmentCount, 1)];
            var blendAttachments =
                stackalloc VkPipelineColorBlendAttachmentState[Math.Max(attachmentCount, 1)];

            for (var index = 0; index < attachmentCount; index++)
            {
                var format = description.ColorTargetFormats[index];
                colorFormats[index] = VulkanNativeTypeMapper.GetVkFormat(format);
                blendAttachments[index] = CreateColorBlendAttachment(description.BlendMode, format);
            }

            var colorBlend = default(VkPipelineColorBlendStateCreateInfo);
            colorBlend.sType = VkStructureType.VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
            colorBlend.attachmentCount = checked((uint)attachmentCount);
            colorBlend.pAttachments = blendAttachments;
            var dynamicStates = stackalloc VkDynamicState[2];
            dynamicStates[0] = VkDynamicState.VK_DYNAMIC_STATE_VIEWPORT;
            dynamicStates[1] = VkDynamicState.VK_DYNAMIC_STATE_SCISSOR;
            var dynamicState = default(VkPipelineDynamicStateCreateInfo);
            dynamicState.sType = VkStructureType.VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO;
            dynamicState.dynamicStateCount = 2;
            dynamicState.pDynamicStates = dynamicStates;
            var rendering = default(VkPipelineRenderingCreateInfo);
            rendering.sType = VkStructureType.VK_STRUCTURE_TYPE_PIPELINE_RENDERING_CREATE_INFO;
            rendering.colorAttachmentCount = checked((uint)attachmentCount);
            rendering.pColorAttachmentFormats = colorFormats;
            rendering.depthAttachmentFormat = description.DepthTargetFormat is TextureFormat depthFormat
                ? VulkanNativeTypeMapper.GetVkFormat(depthFormat)
                : VkFormat.VK_FORMAT_UNDEFINED;
            var createInfo = default(VkGraphicsPipelineCreateInfo);
            createInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
            createInfo.pNext = &rendering;
            createInfo.stageCount = 2;
            createInfo.pStages = shaderStages;
            createInfo.pVertexInputState = &vertexInput;
            createInfo.pInputAssemblyState = &inputAssembly;
            createInfo.pViewportState = &viewport;
            createInfo.pRasterizationState = &rasterization;
            createInfo.pMultisampleState = &multisample;
            createInfo.pDepthStencilState = &depthStencil;
            createInfo.pColorBlendState = &colorBlend;
            createInfo.pDynamicState = &dynamicState;
            createInfo.layout = pipelineLayout;
            createInfo.basePipelineIndex = -1;
            VkPipeline pipeline = default;
            ThrowIfFailed(
                VulkanApi.vkCreateGraphicsPipelines(_device, default, 1, &createInfo, null, &pipeline),
                "vkCreateGraphicsPipelines");
            return pipeline;
        }
    }

    [SuppressMessage(
        "Usage",
        "CA2201:Do not raise reserved exception types",
        Justification = "Native shader-module bytecode allocation failure is reported as an allocation failure, not successful pipeline creation.")]
    private VkShaderModule CreateShaderModule(BufferReadLease<byte> bytecode)
    {
        var byteLength = bytecode.Length;

        if ((byteLength <= 0) || ((byteLength % sizeof(uint)) != 0))
        {
            throw new ArgumentException("SPIR-V bytecode length must be a positive multiple of four.", nameof(bytecode));
        }

        var nativeByteLength = checked((nuint)byteLength);
        var bytecodeCopy = (byte*)NativeMemory.Alloc(nativeByteLength);

        if (bytecodeCopy is null)
        {
            throw new OutOfMemoryException("Vulkan shader-module bytecode storage could not be allocated.");
        }

        try
        {
            nint copiedBytes = 0;

            while (copiedBytes < byteLength)
            {
                var remainingBytes = byteLength - copiedBytes;
                var copyLength = remainingBytes > int.MaxValue
                    ? int.MaxValue
                    : checked((int)remainingBytes);
                var copyAction = new BufferLeaseCopyAction(bytecodeCopy + copiedBytes);
                bytecode.UseWindow(copiedBytes, copyLength, ref copyAction);
                copiedBytes = checked(copiedBytes + copyLength);
            }

            if (BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(bytecodeCopy, sizeof(uint))) != 0x07230203)
            {
                throw new ArgumentException("The selected bytecode does not have the SPIR-V magic word.", nameof(bytecode));
            }

            var createInfo = default(VkShaderModuleCreateInfo);
            createInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
            createInfo.codeSize = nativeByteLength;
            createInfo.pCode = (uint*)bytecodeCopy;
            VkShaderModule shaderModule = default;
            ThrowIfFailed(VulkanApi.vkCreateShaderModule(_device, &createInfo, null, &shaderModule), "vkCreateShaderModule");
            return shaderModule;
        }
        finally
        {
            NativeMemory.Free(bytecodeCopy);
        }
    }

    private GraphicsFormatCapabilities? FindFormatCapabilities(TextureFormat format)
    {
        foreach (var capabilities in Capabilities.Formats)
        {
            if (capabilities.Format == format)
            {
                return capabilities;
            }
        }

        return null;
    }

    private void ValidateComputePipeline(ComputePipelineDescription description)
    {
        var reflection = description.Shader.Reflection;

        if ((reflection.GroupSizeX > Capabilities.Limits.MaximumComputeWorkGroupSizeX)
            || (reflection.GroupSizeY > Capabilities.Limits.MaximumComputeWorkGroupSizeY)
            || (reflection.GroupSizeZ > Capabilities.Limits.MaximumComputeWorkGroupSizeZ)
            || ((long)reflection.GroupSizeX * reflection.GroupSizeY * reflection.GroupSizeZ
                > Capabilities.Limits.MaximumComputeWorkGroupInvocations))
        {
            throw new NotSupportedException("The compute shader work-group size exceeds the Vulkan device limits.");
        }
    }

    private void ValidateGraphicsPipeline(GraphicsPipelineDescription description)
    {
        if (description.VertexElements.Length > Capabilities.Limits.MaximumVertexInputAttributes)
        {
            throw new NotSupportedException("The vertex element count exceeds the Vulkan device limit.");
        }

        if (description.ColorTargetFormats.Length > Capabilities.Limits.MaximumColorAttachments)
        {
            throw new NotSupportedException("The color attachment count exceeds the Vulkan device limit.");
        }

        foreach (var format in description.ColorTargetFormats)
        {
            var capabilities = FindFormatCapabilities(format);

            if ((capabilities is null) || ((capabilities.SupportedUsages & TextureUsage.ColorAttachment) == 0))
            {
                throw new NotSupportedException($"Vulkan does not support color attachment format {format}.");
            }

            if ((description.BlendMode == PipelineBlendMode.PremultipliedAlpha) && (format == TextureFormat.R32UInt))
            {
                throw new NotSupportedException("Vulkan blending is not supported for the unsigned integer target format.");
            }

            if (description.BlendMode == PipelineBlendMode.PremultipliedAlpha)
            {
                var nativeFormat = VulkanNativeTypeMapper.GetVkFormat(format);
                var formatProperties = default(VkFormatProperties);
                VulkanApi.vkGetPhysicalDeviceFormatProperties(_physicalDevice, nativeFormat, &formatProperties);

                if ((formatProperties.optimalTilingFeatures
                    & VkFormatFeatureFlags.VK_FORMAT_FEATURE_COLOR_ATTACHMENT_BLEND_BIT) == 0)
                {
                    throw new NotSupportedException($"Vulkan does not support color blending for attachment format {format}.");
                }
            }
        }

        if (description.DepthTargetFormat is TextureFormat depthFormat)
        {
            var capabilities = FindFormatCapabilities(depthFormat);

            if ((capabilities is null) || ((capabilities.SupportedUsages & TextureUsage.DepthAttachment) == 0))
            {
                throw new NotSupportedException($"Vulkan does not support depth attachment format {depthFormat}.");
            }
        }
    }
}
