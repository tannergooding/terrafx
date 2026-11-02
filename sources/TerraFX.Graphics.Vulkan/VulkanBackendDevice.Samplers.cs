// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Graphics;
using TerraFX.Interop.Vulkan;
using VulkanApi = TerraFX.Interop.Vulkan.Vulkan;

namespace TerraFX.Graphics.Vulkan;

internal sealed unsafe partial class VulkanBackendDevice
{
    private int _liveSamplerCount;

    private static VkSamplerAddressMode GetNativeSamplerAddressMode(SamplerAddressMode addressMode)
        => addressMode switch {
            SamplerAddressMode.Clamp => VkSamplerAddressMode.VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE,
            SamplerAddressMode.Repeat => VkSamplerAddressMode.VK_SAMPLER_ADDRESS_MODE_REPEAT,
            _ => throw new ArgumentOutOfRangeException(nameof(addressMode)),
        };

    private static VkFilter GetNativeSamplerFilter(SamplerFilter filter)
        => filter switch {
            SamplerFilter.Nearest => VkFilter.VK_FILTER_NEAREST,
            SamplerFilter.Linear => VkFilter.VK_FILTER_LINEAR,
            _ => throw new ArgumentOutOfRangeException(nameof(filter)),
        };

    internal VulkanBackendSampler CreateSampler(SamplerDescription description, float effectiveMaximumAnisotropy)
    {
        ArgumentNullException.ThrowIfNull(description);
        EnsureActive();

        if (!float.IsFinite(effectiveMaximumAnisotropy)
            || (effectiveMaximumAnisotropy < 1.0f)
            || (effectiveMaximumAnisotropy > Capabilities.Limits.MaximumSamplerAnisotropy))
        {
            throw new ArgumentOutOfRangeException(nameof(effectiveMaximumAnisotropy));
        }

        if (effectiveMaximumAnisotropy > 1.0f)
        {
            throw new NotSupportedException("Vulkan sampler anisotropy is not implemented.");
        }

        var createInfo = default(VkSamplerCreateInfo);
        createInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO;
        createInfo.magFilter = GetNativeSamplerFilter(description.Magnification);
        createInfo.minFilter = GetNativeSamplerFilter(description.Minification);
        createInfo.mipmapMode = description.Mip == SamplerFilter.Linear
            ? VkSamplerMipmapMode.VK_SAMPLER_MIPMAP_MODE_LINEAR
            : VkSamplerMipmapMode.VK_SAMPLER_MIPMAP_MODE_NEAREST;
        createInfo.addressModeU = GetNativeSamplerAddressMode(description.AddressU);
        createInfo.addressModeV = GetNativeSamplerAddressMode(description.AddressV);
        createInfo.addressModeW = GetNativeSamplerAddressMode(description.AddressW);
        createInfo.maxAnisotropy = effectiveMaximumAnisotropy;
        createInfo.compareOp = VkCompareOp.VK_COMPARE_OP_ALWAYS;
        createInfo.maxLod = description.Mip == SamplerFilter.None ? 0.0f : 1000.0f;
        createInfo.borderColor = VkBorderColor.VK_BORDER_COLOR_FLOAT_TRANSPARENT_BLACK;
        VkSampler sampler = default;
        ThrowIfFailed(VulkanApi.vkCreateSampler(_device, &createInfo, null, &sampler), "vkCreateSampler");
        var ownsSampler = true;

        try
        {
            var result = new VulkanBackendSampler(this, sampler);
            _liveSamplerCount = checked(_liveSamplerCount + 1);
            ownsSampler = false;
            return result;
        }
        finally
        {
            if (ownsSampler)
            {
                VulkanApi.vkDestroySampler(_device, sampler, null);
            }
        }
    }

    internal void DestroySampler(VulkanBackendSampler sampler)
    {
        ArgumentNullException.ThrowIfNull(sampler);
        EnsureOwnerThread();

        if (!ReferenceEquals(sampler.Owner, this))
        {
            throw new ArgumentException("The sampler belongs to a different Vulkan device.", nameof(sampler));
        }

        if (sampler.IsDisposed)
        {
            return;
        }

        if (sampler.DescriptorSetReferenceCount != 0)
        {
            throw new InvalidOperationException("Vulkan sampler retirement cannot release storage while descriptor sets remain live.");
        }

        if (_hasUncertainSubmission)
        {
            throw new InvalidOperationException("Vulkan sampler retirement cannot release storage while submission completion is uncertain.");
        }

        ObjectDisposedException.ThrowIf(_isDisposed, this);
        VulkanApi.vkDestroySampler(_device, sampler.Sampler, null);
        sampler.MarkDisposed();
        _liveSamplerCount = checked(_liveSamplerCount - 1);
    }
}
