// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Graphics;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class CommandArgumentTests
{
    private static GraphBufferVersion CreateBuffer(BufferUsage usage)
    {
        var description = new BufferDescription(
            64,
            usage,
            GpuMemoryKind.DeviceLocal,
            structuredStride: null,
            debugName: "Command argument test");
        return new GraphBufferVersion(
            deviceEpoch: 1,
            resourceId: 1,
            version: 0,
            description,
            new GraphBufferRange(0, description.ByteLength));
    }

    [Test]
    public void DispatchAndDrawArgumentsAdmitZeroWorkAndRejectNegativeCounts()
    {
        var dispatch = new DispatchSize(0, 3, 1);
        Assert.That(dispatch.GroupCountX, Is.Zero);
        Assert.That(dispatch.GroupCountY, Is.EqualTo(3));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new DispatchSize(-1, 1, 1));

        var draw = new DrawArguments(0, 2, 0, 1);
        Assert.That(draw.VertexCount, Is.Zero);
        Assert.That(draw.InstanceCount, Is.EqualTo(2));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new DrawArguments(1, -1, 0, 0));

        var indexed = new DrawIndexedArguments(4, 1, 0, -2, 0);
        Assert.That(indexed.BaseVertex, Is.EqualTo(-2));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new DrawIndexedArguments(1, 1, -1, 0, 0));
    }

    [Test]
    public void IndexBufferBindingRequiresIndexUsageAndAlignedContainedRange()
    {
        var buffer = CreateBuffer(BufferUsage.Index);
        var binding = new IndexBufferBinding(
            buffer,
            new GraphBufferRange(2, 30),
            IndexFormat.UInt16);

        Assert.That(binding.Range.ByteOffset, Is.EqualTo(2));
        Assert.That(binding.Format, Is.EqualTo(IndexFormat.UInt16));
        _ = Assert.Throws<ArgumentException>(
            () => new IndexBufferBinding(buffer, new GraphBufferRange(2, 32), IndexFormat.UInt32));
        _ = Assert.Throws<ArgumentException>(
            () => new IndexBufferBinding(buffer, new GraphBufferRange(0, 63), IndexFormat.UInt16));
        _ = Assert.Throws<ArgumentException>(
            () => new IndexBufferBinding(CreateBuffer(BufferUsage.Vertex), new GraphBufferRange(0, 16), IndexFormat.UInt16));
    }

    [Test]
    public void VertexBufferBindingRequiresVertexUsageAndContainedRange()
    {
        var buffer = CreateBuffer(BufferUsage.Vertex);
        var binding = new VertexBufferBinding(
            slot: 2,
            buffer,
            new GraphBufferRange(16, 32),
            stride: 16);

        Assert.That(binding.Slot, Is.EqualTo(2));
        Assert.That(binding.Stride, Is.EqualTo(16));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => new VertexBufferBinding(-1, buffer, new GraphBufferRange(0, 16), 16));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => new VertexBufferBinding(0, buffer, new GraphBufferRange(0, 16), 0));
        _ = Assert.Throws<ArgumentException>(
            () => new VertexBufferBinding(0, buffer, new GraphBufferRange(64, 16), 16));
        _ = Assert.Throws<ArgumentException>(
            () => new VertexBufferBinding(0, CreateBuffer(BufferUsage.Index), new GraphBufferRange(0, 16), 16));
    }
}
