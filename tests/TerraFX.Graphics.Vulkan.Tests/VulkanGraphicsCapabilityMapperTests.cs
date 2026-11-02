// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Graphics;
using TerraFX.Graphics.Vulkan;
using TerraFX.Interop.Vulkan;

namespace TerraFX.Graphics.Vulkan.Tests;

[TestFixture]
internal sealed class VulkanGraphicsCapabilityMapperTests
{
    private static VkPhysicalDeviceLimits CreateValidLimits()
    {
        var limits = default(VkPhysicalDeviceLimits);
        limits.maxImageDimension2D = 8192;
        limits.maxImageDimension3D = 256;
        limits.maxImageArrayLayers = 256;
        limits.maxColorAttachments = 4;
        limits.maxPerStageDescriptorSampledImages = 16;
        limits.maxPerStageDescriptorSamplers = 8;
        limits.maxSamplerAnisotropy = 8.0f;
        limits.maxPerStageDescriptorUniformBuffers = 8;
        limits.maxUniformBufferRange = 16384;
        limits.minUniformBufferOffsetAlignment = 256;
        limits.minStorageBufferOffsetAlignment = 16;
        limits.optimalBufferCopyOffsetAlignment = 512;
        limits.optimalBufferCopyRowPitchAlignment = 256;
        limits.maxComputeWorkGroupSize[0] = 1024;
        limits.maxComputeWorkGroupSize[1] = 1024;
        limits.maxComputeWorkGroupSize[2] = 64;
        limits.maxComputeWorkGroupInvocations = 1024;
        limits.maxVertexInputAttributes = 32;
        limits.maxVertexInputBindings = 32;
        return limits;
    }

    [Test]
    public void FormatFeatureBitsMapToIndependentTextureUsages()
    {
        const VkFormatFeatureFlags features =
            VkFormatFeatureFlags.VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT
            | VkFormatFeatureFlags.VK_FORMAT_FEATURE_STORAGE_IMAGE_BIT
            | VkFormatFeatureFlags.VK_FORMAT_FEATURE_COLOR_ATTACHMENT_BIT
            | VkFormatFeatureFlags.VK_FORMAT_FEATURE_TRANSFER_SRC_BIT
            | VkFormatFeatureFlags.VK_FORMAT_FEATURE_TRANSFER_DST_BIT;

        Assert.That(
            VulkanGraphicsCapabilityMapper.GetSupportedTextureUsages(features),
            Is.EqualTo(
                TextureUsage.Sampled
                | TextureUsage.Storage
                | TextureUsage.ColorAttachment
                | TextureUsage.CopySource
                | TextureUsage.CopyDestination));
    }

    [Test]
    public void LimitsMapNativeAlignmentsAndWorkGroupExtents()
    {
        var limits = CreateValidLimits();
        var mapped = VulkanGraphicsCapabilityMapper.CreateLimits(
            limits,
            maximumBufferByteLength: 1UL << 30,
            samplerAnisotropySupported: true);

        Assert.That(mapped.MaximumTextureDimension2D, Is.EqualTo(8192));
        Assert.That(mapped.MaximumTextureDimension3D, Is.EqualTo(256));
        Assert.That(mapped.MaximumArrayLayers, Is.EqualTo(256));
        Assert.That(mapped.MaximumColorAttachments, Is.EqualTo(4));
        Assert.That(mapped.MaximumSamplerAnisotropy, Is.EqualTo(8.0f));
        Assert.That(mapped.UniformBufferOffsetAlignment, Is.EqualTo(256));
        Assert.That(mapped.StorageBufferOffsetAlignment, Is.EqualTo(16));
        Assert.That(mapped.BufferCopyOffsetAlignment, Is.EqualTo(512));
        Assert.That(mapped.TexturePlacementAlignment, Is.EqualTo(512));
        Assert.That(mapped.TextureRowPitchAlignment, Is.EqualTo(256));
        Assert.That(mapped.MaximumComputeWorkGroupSizeX, Is.EqualTo(1024));
        Assert.That(mapped.MaximumComputeWorkGroupSizeY, Is.EqualTo(1024));
        Assert.That(mapped.MaximumComputeWorkGroupSizeZ, Is.EqualTo(64));
        Assert.That(mapped.MaximumComputeWorkGroupInvocations, Is.EqualTo(1024));
        Assert.That(mapped.MaximumVertexInputAttributes, Is.EqualTo(32));
        Assert.That(mapped.MaximumVertexInputBindings, Is.EqualTo(32));
        Assert.That(mapped.MaximumBufferByteLength, Is.EqualTo(1L << 30));
    }

    [Test]
    public void LimitsRejectNonpositiveNativeValues()
    {
        var limits = CreateValidLimits();
        limits.maxImageDimension2D = 0;

        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => VulkanGraphicsCapabilityMapper.CreateLimits(
                limits,
                maximumBufferByteLength: 1UL << 30,
                samplerAnisotropySupported: false));
    }

    [Test]
    public void SampleCountBitsMapToAscendingCounts()
    {
        const VkSampleCountFlags sampleCounts =
            VkSampleCountFlags.VK_SAMPLE_COUNT_8_BIT
            | VkSampleCountFlags.VK_SAMPLE_COUNT_1_BIT
            | VkSampleCountFlags.VK_SAMPLE_COUNT_4_BIT;

        int[] expectedCounts = [1, 4, 8];
        Assert.That(VulkanGraphicsCapabilityMapper.GetFormatSampleCounts(sampleCounts), Is.EqualTo(expectedCounts));
    }

    [Test]
    public void UnsupportedAnisotropyReportsIsotropicLimit()
    {
        var mapped = VulkanGraphicsCapabilityMapper.CreateLimits(
            CreateValidLimits(),
            maximumBufferByteLength: 1UL << 30,
            samplerAnisotropySupported: false);

        Assert.That(mapped.MaximumSamplerAnisotropy, Is.EqualTo(1.0f));
    }
}
