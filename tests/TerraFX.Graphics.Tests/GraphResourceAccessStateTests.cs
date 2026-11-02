// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using NUnit.Framework;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class GraphResourceAccessStateTests
{
    [TestCase(TextureFormat.Rgba8Unorm, TextureUsage.ColorAttachment, GraphResourceState.ColorAttachment)]
    [TestCase(TextureFormat.D32Float, TextureUsage.DepthAttachment, GraphResourceState.DepthWrite)]
    public void AttachmentUsagesMapToRequiredStates(
        TextureFormat format,
        TextureUsage attachmentUsage,
        GraphResourceState expectedState)
    {
        var description = new TextureDescription(
            TextureDimension.Dimension2D,
            4,
            4,
            1,
            1,
            1,
            format,
            attachmentUsage,
            string.Empty);
        var aspect = (format == TextureFormat.D32Float) ? TextureAspect.Depth : TextureAspect.Color;
        var range = new GraphTextureRange(0, 1, 0, 1, aspect);
        var version = new GraphTextureVersion(1, 1, 0, description, range);
        var access = new GraphResourceAccess(
            new RenderPassId(7, 0),
            version,
            range,
            attachmentUsage,
            AttachmentLoadAction.Clear,
            AttachmentStoreAction.Store,
            isWrite: true,
            contentsValid: true);

        Assert.That(access.RequiredState, Is.EqualTo(expectedState));
    }

    [TestCase(GraphBufferUsage.VertexRead, GraphResourceState.VertexRead)]
    [TestCase(GraphBufferUsage.IndexRead, GraphResourceState.IndexRead)]
    [TestCase(GraphBufferUsage.UniformRead, GraphResourceState.UniformRead)]
    [TestCase(GraphBufferUsage.StorageRead, GraphResourceState.StorageRead)]
    [TestCase(GraphBufferUsage.StorageWrite, GraphResourceState.StorageWrite)]
    [TestCase(GraphBufferUsage.CopySource, GraphResourceState.CopySource)]
    [TestCase(GraphBufferUsage.CopyDestination, GraphResourceState.CopyDestination)]
    public void BufferUsagesMapToRequiredStates(GraphBufferUsage usage, GraphResourceState expectedState)
    {
        var description = new BufferDescription(
            64,
            BufferUsage.Vertex
                | BufferUsage.Index
                | BufferUsage.Uniform
                | BufferUsage.Storage
                | BufferUsage.CopySource
                | BufferUsage.CopyDestination,
            GpuMemoryKind.DeviceLocal,
            structuredStride: null,
            debugName: string.Empty);
        var range = new GraphBufferRange(0, 64);
        var version = new GraphBufferVersion(1, 1, 0, description, range);
        var isWrite = usage is GraphBufferUsage.StorageWrite or GraphBufferUsage.CopyDestination;
        var access = new GraphResourceAccess(new RenderPassId(7, 0), version, range, usage, isWrite);

        Assert.That(access.RequiredState, Is.EqualTo(expectedState));
    }

    [TestCase(GraphTextureUsage.SampledRead, GraphResourceState.ShaderSampledRead)]
    [TestCase(GraphTextureUsage.StorageRead, GraphResourceState.StorageRead)]
    [TestCase(GraphTextureUsage.StorageWrite, GraphResourceState.StorageWrite)]
    [TestCase(GraphTextureUsage.CopySource, GraphResourceState.CopySource)]
    [TestCase(GraphTextureUsage.CopyDestination, GraphResourceState.CopyDestination)]
    public void TextureUsagesMapToRequiredStates(GraphTextureUsage usage, GraphResourceState expectedState)
    {
        var description = new TextureDescription(
            TextureDimension.Dimension2D,
            4,
            4,
            1,
            1,
            1,
            TextureFormat.Rgba16Float,
            TextureUsage.Sampled | TextureUsage.Storage | TextureUsage.CopySource | TextureUsage.CopyDestination,
            string.Empty);
        var range = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);
        var version = new GraphTextureVersion(1, 1, 0, description, range);
        var isWrite = usage is GraphTextureUsage.StorageWrite or GraphTextureUsage.CopyDestination;
        var access = new GraphResourceAccess(new RenderPassId(7, 0), version, range, usage, isWrite);

        Assert.That(access.RequiredState, Is.EqualTo(expectedState));
    }
}
