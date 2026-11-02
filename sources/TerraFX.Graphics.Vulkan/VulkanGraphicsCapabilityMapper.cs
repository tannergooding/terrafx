// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Graphics;
using TerraFX.Interop.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal static class VulkanGraphicsCapabilityMapper
{
    internal static GraphicsLimits CreateLimits(
        VkPhysicalDeviceLimits limits,
        ulong maximumBufferByteLength,
        bool samplerAnisotropySupported)
    {
        return new GraphicsLimits(
            ConvertPositiveInt(limits.maxImageDimension2D, nameof(limits.maxImageDimension2D)),
            ConvertPositiveInt(limits.maxImageDimension3D, nameof(limits.maxImageDimension3D)),
            ConvertPositiveInt(limits.maxImageArrayLayers, nameof(limits.maxImageArrayLayers)),
            ConvertPositiveInt(limits.maxColorAttachments, nameof(limits.maxColorAttachments)),
            ConvertPositiveInt(limits.maxPerStageDescriptorSampledImages, nameof(limits.maxPerStageDescriptorSampledImages)),
            ConvertPositiveInt(limits.maxPerStageDescriptorSamplers, nameof(limits.maxPerStageDescriptorSamplers)),
            samplerAnisotropySupported ? limits.maxSamplerAnisotropy : 1.0f,
            ConvertPositiveInt(limits.maxPerStageDescriptorUniformBuffers, nameof(limits.maxPerStageDescriptorUniformBuffers)),
            limits.maxUniformBufferRange,
            ConvertPositiveLong(limits.minUniformBufferOffsetAlignment, nameof(limits.minUniformBufferOffsetAlignment)),
            ConvertPositiveLong(limits.minStorageBufferOffsetAlignment, nameof(limits.minStorageBufferOffsetAlignment)),
            ConvertPositiveLong(limits.optimalBufferCopyOffsetAlignment, nameof(limits.optimalBufferCopyOffsetAlignment)),
            ConvertPositiveLong(limits.optimalBufferCopyRowPitchAlignment, nameof(limits.optimalBufferCopyRowPitchAlignment)),
            ConvertPositiveLong(limits.optimalBufferCopyOffsetAlignment, nameof(limits.optimalBufferCopyOffsetAlignment)),
            ConvertPositiveInt(limits.maxComputeWorkGroupSize[0], nameof(limits.maxComputeWorkGroupSize)),
            ConvertPositiveInt(limits.maxComputeWorkGroupSize[1], nameof(limits.maxComputeWorkGroupSize)),
            ConvertPositiveInt(limits.maxComputeWorkGroupSize[2], nameof(limits.maxComputeWorkGroupSize)),
            ConvertPositiveInt(limits.maxComputeWorkGroupInvocations, nameof(limits.maxComputeWorkGroupInvocations)),
            ConvertPositiveInt(limits.maxVertexInputAttributes, nameof(limits.maxVertexInputAttributes)),
            ConvertPositiveInt(limits.maxVertexInputBindings, nameof(limits.maxVertexInputBindings)),
            ConvertPositiveLong(maximumBufferByteLength, nameof(maximumBufferByteLength)));
    }

    internal static ImmutableArray<int> GetFormatSampleCounts(VkSampleCountFlags supportedSampleCounts)
    {
        var sampleCounts = ImmutableArray.CreateBuilder<int>(7);

        if ((supportedSampleCounts & VkSampleCountFlags.VK_SAMPLE_COUNT_1_BIT) != 0)
        {
            sampleCounts.Add(1);
        }

        if ((supportedSampleCounts & VkSampleCountFlags.VK_SAMPLE_COUNT_2_BIT) != 0)
        {
            sampleCounts.Add(2);
        }

        if ((supportedSampleCounts & VkSampleCountFlags.VK_SAMPLE_COUNT_4_BIT) != 0)
        {
            sampleCounts.Add(4);
        }

        if ((supportedSampleCounts & VkSampleCountFlags.VK_SAMPLE_COUNT_8_BIT) != 0)
        {
            sampleCounts.Add(8);
        }

        if ((supportedSampleCounts & VkSampleCountFlags.VK_SAMPLE_COUNT_16_BIT) != 0)
        {
            sampleCounts.Add(16);
        }

        if ((supportedSampleCounts & VkSampleCountFlags.VK_SAMPLE_COUNT_32_BIT) != 0)
        {
            sampleCounts.Add(32);
        }

        if ((supportedSampleCounts & VkSampleCountFlags.VK_SAMPLE_COUNT_64_BIT) != 0)
        {
            sampleCounts.Add(64);
        }

        return sampleCounts.ToImmutable();
    }

    internal static TextureUsage GetSupportedTextureUsages(VkFormatFeatureFlags optimalTilingFeatures)
    {
        var supportedUsages = TextureUsage.None;

        if ((optimalTilingFeatures & VkFormatFeatureFlags.VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT) != 0)
        {
            supportedUsages |= TextureUsage.Sampled;
        }

        if ((optimalTilingFeatures & VkFormatFeatureFlags.VK_FORMAT_FEATURE_STORAGE_IMAGE_BIT) != 0)
        {
            supportedUsages |= TextureUsage.Storage;
        }

        if ((optimalTilingFeatures & VkFormatFeatureFlags.VK_FORMAT_FEATURE_COLOR_ATTACHMENT_BIT) != 0)
        {
            supportedUsages |= TextureUsage.ColorAttachment;
        }

        if ((optimalTilingFeatures & VkFormatFeatureFlags.VK_FORMAT_FEATURE_DEPTH_STENCIL_ATTACHMENT_BIT) != 0)
        {
            supportedUsages |= TextureUsage.DepthAttachment;
        }

        if ((optimalTilingFeatures & VkFormatFeatureFlags.VK_FORMAT_FEATURE_TRANSFER_SRC_BIT) != 0)
        {
            supportedUsages |= TextureUsage.CopySource;
        }

        if ((optimalTilingFeatures & VkFormatFeatureFlags.VK_FORMAT_FEATURE_TRANSFER_DST_BIT) != 0)
        {
            supportedUsages |= TextureUsage.CopyDestination;
        }

        return supportedUsages;
    }

    private static int ConvertPositiveInt(uint value, string parameterName)
    {
        // Vulkan uses UINT32_MAX for effectively unbounded descriptor limits; GraphicsLimits uses Int32.
        var converted = value > int.MaxValue ? int.MaxValue : checked((int)value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(converted, parameterName);
        return converted;
    }

    private static long ConvertPositiveLong(ulong value, string parameterName)
    {
        var converted = checked((long)value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(converted, parameterName);
        return converted;
    }
}
