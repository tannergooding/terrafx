// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Graphics;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class GraphicsResourceHandleTests
{
    [Test]
    public void GpuHandleConstructorsRejectZeroOwnerEpochOrGeneration()
    {
        Action<uint, uint, uint, uint>[] constructors = [
            static (ownerId, deviceEpoch, index, generation) => _ = new GpuBufferHandle(ownerId, deviceEpoch, index, generation),
            static (ownerId, deviceEpoch, index, generation) => _ = new GpuTextureHandle(ownerId, deviceEpoch, index, generation),
            static (ownerId, deviceEpoch, index, generation) => _ = new GpuSamplerHandle(ownerId, deviceEpoch, index, generation),
            static (ownerId, deviceEpoch, index, generation) => _ = new GpuPipelineHandle(ownerId, deviceEpoch, index, generation),
        ];

        foreach (var constructor in constructors)
        {
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => constructor(0, 1, 0, 1));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => constructor(1, 0, 0, 1));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => constructor(1, 1, 0, 0));
        }
    }

    [Test]
    public void GpuHandlesAreTypedAndCompareAllIdentityFields()
    {
        var buffer = new GpuBufferHandle(7, 3, 11, 2);
        var texture = new GpuTextureHandle(7, 3, 11, 2);
        var sampler = new GpuSamplerHandle(7, 3, 11, 2);
        var pipeline = new GpuPipelineHandle(7, 3, 11, 2);

        Assert.That(buffer.IsValid, Is.True);
        Assert.That(buffer, Is.EqualTo(new GpuBufferHandle(7, 3, 11, 2)));
        Assert.That(buffer, Is.Not.EqualTo(new GpuBufferHandle(7, 4, 11, 2)));
        Assert.That(buffer, Is.Not.EqualTo(new GpuBufferHandle(8, 3, 11, 2)));
        Assert.That(buffer, Is.Not.EqualTo(new GpuBufferHandle(7, 3, 12, 2)));
        Assert.That(buffer, Is.Not.EqualTo(new GpuBufferHandle(7, 3, 11, 3)));

        Assert.That(texture.IsValid, Is.True);
        Assert.That(texture, Is.EqualTo(new GpuTextureHandle(7, 3, 11, 2)));
        Assert.That(texture, Is.Not.EqualTo(new GpuTextureHandle(7, 3, 11, 3)));

        Assert.That(sampler.IsValid, Is.True);
        Assert.That(sampler, Is.EqualTo(new GpuSamplerHandle(7, 3, 11, 2)));
        Assert.That(sampler, Is.Not.EqualTo(new GpuSamplerHandle(7, 4, 11, 2)));

        Assert.That(pipeline.IsValid, Is.True);
        Assert.That(pipeline, Is.EqualTo(new GpuPipelineHandle(7, 3, 11, 2)));
        Assert.That(pipeline, Is.Not.EqualTo(new GpuPipelineHandle(7, 3, 12, 2)));

        Assert.That(default(GpuBufferHandle).IsValid, Is.False);
        Assert.That(default(GpuTextureHandle).IsValid, Is.False);
        Assert.That(default(GpuSamplerHandle).IsValid, Is.False);
        Assert.That(default(GpuPipelineHandle).IsValid, Is.False);
    }
}
