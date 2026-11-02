// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class BindingSetTests
{
    private static BufferBinding CreateBufferBinding(
        ShaderBindingGroup group,
        int binding,
        ShaderResourceKind kind,
        ulong resourceId)
    {
        var usage = (kind == ShaderResourceKind.UniformBuffer)
            ? BufferUsage.Uniform
            : BufferUsage.Storage;
        var resource = CreateBufferVersion(usage, resourceId);
        return new BufferBinding(group, binding, kind, resource, new GraphBufferRange(16, 64));
    }

    private static GraphBufferVersion CreateBufferVersion(BufferUsage usage, ulong resourceId)
    {
        var description = new BufferDescription(
            256,
            usage,
            GpuMemoryKind.DeviceLocal,
            structuredStride: null,
            debugName: "binding test");
        return new GraphBufferVersion(
            1,
            resourceId,
            0,
            description,
            new GraphBufferRange(0, description.ByteLength));
    }

    private static TextureBinding CreateTextureBinding(
        ShaderBindingGroup group,
        int binding,
        ShaderResourceKind kind,
        ulong resourceId)
    {
        var usage = (kind == ShaderResourceKind.SampledImage)
            ? TextureUsage.Sampled
            : TextureUsage.Storage;
        var resource = CreateTextureVersion(usage, resourceId);
        return new TextureBinding(
            group,
            binding,
            kind,
            resource,
            new GraphTextureRange(1, 1, 0, 1, TextureAspect.Color));
    }

    private static GraphTextureVersion CreateTextureVersion(TextureUsage usage, ulong resourceId)
    {
        var description = new TextureDescription(
            TextureDimension.Dimension2D,
            8,
            8,
            1,
            1,
            2,
            TextureFormat.Rgba8Unorm,
            usage,
            "binding test");
        return new GraphTextureVersion(
            1,
            resourceId,
            0,
            description,
            new GraphTextureRange(0, 2, 0, 1, TextureAspect.Color));
    }

    [Test]
    public void BindingSetCopiesAndSortsBindingsByGroupKindAndLocalIndex()
    {
        var frameStorage = CreateBufferBinding(ShaderBindingGroup.Frame, 0, ShaderResourceKind.StorageBuffer, 1);
        var materialUniform = CreateBufferBinding(ShaderBindingGroup.Material, 2, ShaderResourceKind.UniformBuffer, 2);
        var frameUniform = CreateBufferBinding(ShaderBindingGroup.Frame, 0, ShaderResourceKind.UniformBuffer, 3);
        var frameStorageImage = CreateTextureBinding(ShaderBindingGroup.Frame, 1, ShaderResourceKind.StorageImage, 4);
        var frameSampledImage = CreateTextureBinding(ShaderBindingGroup.Frame, 1, ShaderResourceKind.SampledImage, 5);
        var bindingSet = new BindingSet(
            [materialUniform, frameStorage, frameUniform],
            [frameStorageImage, frameSampledImage],
            []);

        Assert.That(bindingSet.Buffers.Length, Is.EqualTo(3));
        Assert.That(bindingSet.Buffers[0].Group, Is.EqualTo(ShaderBindingGroup.Frame));
        Assert.That(bindingSet.Buffers[0].Kind, Is.EqualTo(ShaderResourceKind.UniformBuffer));
        Assert.That(bindingSet.Buffers[1].Group, Is.EqualTo(ShaderBindingGroup.Frame));
        Assert.That(bindingSet.Buffers[1].Kind, Is.EqualTo(ShaderResourceKind.StorageBuffer));
        Assert.That(bindingSet.Buffers[2].Group, Is.EqualTo(ShaderBindingGroup.Material));
        Assert.That(bindingSet.Textures[0].Kind, Is.EqualTo(ShaderResourceKind.SampledImage));
        Assert.That(bindingSet.Textures[1].Kind, Is.EqualTo(ShaderResourceKind.StorageImage));
    }

    [Test]
    public void BindingSetRejectsDefaultArrays()
    {
        _ = Assert.Throws<ArgumentException>(() => new BindingSet(
            default,
            [],
            []));
        _ = Assert.Throws<ArgumentException>(() => new BindingSet(
            [],
            default,
            []));
        _ = Assert.Throws<ArgumentException>(() => new BindingSet(
            [],
            [],
            default));
    }

    [Test]
    public void BindingSetRejectsDuplicateTypedSlots()
    {
        var first = CreateBufferBinding(ShaderBindingGroup.Frame, 0, ShaderResourceKind.UniformBuffer, 1);
        var second = CreateBufferBinding(ShaderBindingGroup.Frame, 0, ShaderResourceKind.UniformBuffer, 2);

        _ = Assert.Throws<ArgumentException>(() => new BindingSet(
            [first, second],
            [],
            []));
    }

    [Test]
    public void BufferBindingRequiresMatchingKindUsageAndSubrange()
    {
        var storageBuffer = CreateBufferVersion(BufferUsage.Storage, resourceId: 1);
        _ = Assert.Throws<ArgumentException>(() => new BufferBinding(
            ShaderBindingGroup.Frame,
            0,
            ShaderResourceKind.UniformBuffer,
            storageBuffer,
            new GraphBufferRange(0, 64)));
        _ = Assert.Throws<ArgumentException>(() => new BufferBinding(
            ShaderBindingGroup.Frame,
            0,
            ShaderResourceKind.StorageBuffer,
            storageBuffer,
            new GraphBufferRange(240, 32)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new BufferBinding(
            ShaderBindingGroup.Frame,
            8,
            ShaderResourceKind.StorageBuffer,
            storageBuffer,
            new GraphBufferRange(0, 64)));
    }

    [Test]
    public void TextureBindingRequiresMatchingKindUsageAndSubrange()
    {
        var sampledTexture = CreateTextureVersion(TextureUsage.Sampled, resourceId: 1);
        _ = Assert.Throws<ArgumentException>(() => new TextureBinding(
            ShaderBindingGroup.Material,
            0,
            ShaderResourceKind.StorageImage,
            sampledTexture,
            new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color)));
        _ = Assert.Throws<ArgumentException>(() => new TextureBinding(
            ShaderBindingGroup.Material,
            0,
            ShaderResourceKind.SampledImage,
            sampledTexture,
            new GraphTextureRange(2, 1, 0, 1, TextureAspect.Color)));
    }
}
