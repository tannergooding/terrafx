// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Graphics;
using TerraFX.Graphics.Vulkan;
using TerraFX.Interop.Vulkan;

namespace TerraFX.Graphics.Vulkan.Tests;

[TestFixture]
internal sealed class VulkanNativeTypeMapperTests
{
    [Test]
    public void AdapterIdRejectsInvalidUuidRepresentations()
    {
        _ = Assert.Throws<ArgumentException>(() => VulkanNativeTypeMapper.GetNativeAdapterId(new byte[15]));
        _ = Assert.Throws<ArgumentException>(() => VulkanNativeTypeMapper.GetNativeAdapterId(new byte[16]));
    }

    [Test]
    public void AdapterIdUsesBigEndianUuidBytes()
    {
        byte[] deviceUuid = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];
        var adapterId = VulkanNativeTypeMapper.GetNativeAdapterId(deviceUuid);

        Assert.That(adapterId.Backend, Is.EqualTo(GraphicsBackend.Vulkan));
        Assert.That(adapterId.NativeIdentifier, Is.EqualTo(deviceUuid));
        Assert.That(new Guid(adapterId.NativeIdentifier.AsSpan(), bigEndian: true), Is.EqualTo(new Guid(deviceUuid, bigEndian: true)));
    }

    [Test]
    public void ApiVersionDecodingPreservesVulkanVariantAndComponents()
    {
        const uint nativeVersion = (2u << 29) | (1u << 22) | (3u << 12) | 47u;
        var version = VulkanNativeTypeMapper.GetGraphicsApiVersion(nativeVersion);

        Assert.That(version.Major, Is.EqualTo(1));
        Assert.That(version.Minor, Is.EqualTo(3));
        Assert.That(version.Patch, Is.EqualTo(47));
        Assert.That(version.Variant, Is.EqualTo(2));
    }

    [TestCase(TextureFormat.R8Unorm, VkFormat.VK_FORMAT_R8_UNORM)]
    [TestCase(TextureFormat.Rgba8Unorm, VkFormat.VK_FORMAT_R8G8B8A8_UNORM)]
    [TestCase(TextureFormat.Rgba8Srgb, VkFormat.VK_FORMAT_R8G8B8A8_SRGB)]
    [TestCase(TextureFormat.Bgra8Unorm, VkFormat.VK_FORMAT_B8G8R8A8_UNORM)]
    [TestCase(TextureFormat.Bgra8Srgb, VkFormat.VK_FORMAT_B8G8R8A8_SRGB)]
    [TestCase(TextureFormat.Rgba16Float, VkFormat.VK_FORMAT_R16G16B16A16_SFLOAT)]
    [TestCase(TextureFormat.R32Float, VkFormat.VK_FORMAT_R32_SFLOAT)]
    [TestCase(TextureFormat.R32UInt, VkFormat.VK_FORMAT_R32_UINT)]
    [TestCase(TextureFormat.D32Float, VkFormat.VK_FORMAT_D32_SFLOAT)]
    public void TextureFormatsMapToTheExactVkFormats(TextureFormat format, VkFormat nativeFormat)
    {
        Assert.That(VulkanNativeTypeMapper.GetVkFormat(format), Is.EqualTo(nativeFormat));
        Assert.That(VulkanNativeTypeMapper.GetTextureFormat(nativeFormat), Is.EqualTo(format));
    }
}
