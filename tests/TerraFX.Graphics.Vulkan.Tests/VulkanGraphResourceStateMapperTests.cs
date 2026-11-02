// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Graphics;
using TerraFX.Graphics.Vulkan;
using TerraFX.Interop.Vulkan;

namespace TerraFX.Graphics.Vulkan.Tests;

[TestFixture]
internal sealed class VulkanGraphResourceStateMapperTests
{
    [TestCase(GraphResourceState.Undefined, VkAccessFlags2.VK_ACCESS_2_NONE)]
    [TestCase(GraphResourceState.Present, VkAccessFlags2.VK_ACCESS_2_NONE)]
    [TestCase(GraphResourceState.CopySource, VkAccessFlags2.VK_ACCESS_2_TRANSFER_READ_BIT)]
    [TestCase(GraphResourceState.CopyDestination, VkAccessFlags2.VK_ACCESS_2_TRANSFER_WRITE_BIT)]
    [TestCase(GraphResourceState.VertexRead, VkAccessFlags2.VK_ACCESS_2_VERTEX_ATTRIBUTE_READ_BIT)]
    [TestCase(GraphResourceState.IndexRead, VkAccessFlags2.VK_ACCESS_2_INDEX_READ_BIT)]
    [TestCase(GraphResourceState.UniformRead, VkAccessFlags2.VK_ACCESS_2_UNIFORM_READ_BIT)]
    [TestCase(GraphResourceState.ShaderSampledRead, VkAccessFlags2.VK_ACCESS_2_SHADER_SAMPLED_READ_BIT)]
    [TestCase(GraphResourceState.StorageRead, VkAccessFlags2.VK_ACCESS_2_SHADER_STORAGE_READ_BIT)]
    [TestCase(GraphResourceState.StorageWrite, VkAccessFlags2.VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT)]
    [TestCase(
        GraphResourceState.ColorAttachment,
        VkAccessFlags2.VK_ACCESS_2_COLOR_ATTACHMENT_READ_BIT | VkAccessFlags2.VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT)]
    [TestCase(GraphResourceState.DepthRead, VkAccessFlags2.VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT)]
    [TestCase(
        GraphResourceState.DepthWrite,
        VkAccessFlags2.VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT | VkAccessFlags2.VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT)]
    public void AccessMasksMapToSynchronization2Access(
        GraphResourceState state,
        VkAccessFlags2 expectedAccess)
        => Assert.That(VulkanGraphResourceStateMapper.GetAccessMask(state), Is.EqualTo(expectedAccess));

    [Test]
    public void ImageLayoutRejectsBufferOnlyStates()
        => _ = Assert.Throws<ArgumentException>(
            () => VulkanGraphResourceStateMapper.GetImageLayout(GraphResourceState.VertexRead));

    [TestCase(GraphResourceState.Undefined, VkImageLayout.VK_IMAGE_LAYOUT_UNDEFINED)]
    [TestCase(GraphResourceState.General, VkImageLayout.VK_IMAGE_LAYOUT_GENERAL)]
    [TestCase(GraphResourceState.CopySource, VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL)]
    [TestCase(GraphResourceState.CopyDestination, VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL)]
    [TestCase(GraphResourceState.ShaderSampledRead, VkImageLayout.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL)]
    [TestCase(GraphResourceState.StorageRead, VkImageLayout.VK_IMAGE_LAYOUT_GENERAL)]
    [TestCase(GraphResourceState.StorageWrite, VkImageLayout.VK_IMAGE_LAYOUT_GENERAL)]
    [TestCase(GraphResourceState.ColorAttachment, VkImageLayout.VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL)]
    [TestCase(GraphResourceState.DepthRead, VkImageLayout.VK_IMAGE_LAYOUT_DEPTH_READ_ONLY_OPTIMAL)]
    [TestCase(GraphResourceState.DepthWrite, VkImageLayout.VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL)]
    [TestCase(GraphResourceState.Present, VkImageLayout.VK_IMAGE_LAYOUT_PRESENT_SRC_KHR)]
    public void ImageStatesMapToLayouts(GraphResourceState state, VkImageLayout expectedLayout)
        => Assert.That(VulkanGraphResourceStateMapper.GetImageLayout(state), Is.EqualTo(expectedLayout));

    [Test]
    public void NeutralStateRequiresActualOwnerScope()
    {
        _ = Assert.Throws<InvalidOperationException>(
            () => VulkanGraphResourceStateMapper.GetStageMask(GraphResourceState.General));
        _ = Assert.Throws<InvalidOperationException>(
            () => VulkanGraphResourceStateMapper.GetAccessMask(GraphResourceState.General));
    }

    [Test]
    public void ShaderResourceStagesRejectMissingOrUnsupportedValues()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => VulkanGraphResourceStateMapper.GetStageMask(GraphResourceState.ShaderSampledRead));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => VulkanGraphResourceStateMapper.GetStageMask(
                GraphResourceState.StorageRead,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT));
        _ = Assert.Throws<ArgumentException>(
            () => VulkanGraphResourceStateMapper.GetStageMask(
                GraphResourceState.CopySource,
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT));
    }

    [TestCase(GraphResourceState.UniformRead, VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_VERTEX_SHADER_BIT)]
    [TestCase(GraphResourceState.ShaderSampledRead, VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT)]
    [TestCase(GraphResourceState.StorageRead, VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT)]
    [TestCase(GraphResourceState.StorageWrite, VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT)]
    public void ShaderResourceStatesRequireTheActualShaderStages(
        GraphResourceState state,
        VkPipelineStageFlags2 shaderStages)
        => Assert.That(VulkanGraphResourceStateMapper.GetStageMask(state, shaderStages), Is.EqualTo(shaderStages));

    [Test]
    public void TransferAndAttachmentStatesUsePrecisePipelineStages()
    {
        Assert.That(
            VulkanGraphResourceStateMapper.GetStageMask(GraphResourceState.CopySource),
            Is.EqualTo(VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COPY_BIT));
        Assert.That(
            VulkanGraphResourceStateMapper.GetStageMask(GraphResourceState.VertexRead),
            Is.EqualTo(VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_VERTEX_INPUT_BIT));
        Assert.That(
            VulkanGraphResourceStateMapper.GetStageMask(GraphResourceState.ColorAttachment),
            Is.EqualTo(VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT));
        Assert.That(
            VulkanGraphResourceStateMapper.GetStageMask(GraphResourceState.DepthWrite),
            Is.EqualTo(
                VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT
                | VkPipelineStageFlags2.VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT));
    }
}
