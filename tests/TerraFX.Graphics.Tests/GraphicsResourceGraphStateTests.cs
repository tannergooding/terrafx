// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using NUnit.Framework;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class GraphicsResourceGraphStateTests
{
    [Test]
    public void BufferMemoryKindsMapToTrackedInitialStateWithoutInitializedContents()
    {
        var deviceLocalDescription = new BufferDescription(
            64,
            BufferUsage.Storage,
            GpuMemoryKind.DeviceLocal,
            structuredStride: null,
            debugName: "Device-local graph state");
        var uploadDescription = new BufferDescription(
            64,
            BufferUsage.Vertex | BufferUsage.CopySource,
            GpuMemoryKind.Upload,
            structuredStride: null,
            debugName: "Upload graph state");
        var readbackDescription = new BufferDescription(
            64,
            BufferUsage.CopyDestination,
            GpuMemoryKind.Readback,
            structuredStride: null,
            debugName: "Readback graph state");
        var range = new GraphBufferRange(0, 64);
        var deviceLocal = new GraphicsResourceGraphState(deviceLocalDescription, textureDescription: null);
        var upload = new GraphicsResourceGraphState(uploadDescription, textureDescription: null);
        var readback = new GraphicsResourceGraphState(readbackDescription, textureDescription: null);

        (var deviceLocalState, var deviceLocalContents) = deviceLocal.GetBufferState(range);
        (var uploadState, var uploadContents) = upload.GetBufferState(range);
        (var readbackState, var readbackContents) = readback.GetBufferState(range);

        Assert.That(deviceLocalState, Is.EqualTo(GraphResourceState.Undefined));
        Assert.That(uploadState, Is.EqualTo(GraphResourceState.General));
        Assert.That(readbackState, Is.EqualTo(GraphResourceState.CopyDestination));
        Assert.That(deviceLocalContents, Is.False);
        Assert.That(uploadContents, Is.False);
        Assert.That(readbackContents, Is.False);
    }

    [Test]
    public void TextureStatePartitionsRejectMixedImportsAndKeepContentValiditySeparate()
    {
        var description = new TextureDescription(
            TextureDimension.Dimension2D,
            16,
            16,
            1,
            2,
            5,
            TextureFormat.Rgba8Unorm,
            TextureUsage.CopySource | TextureUsage.CopyDestination,
            string.Empty);
        var state = new GraphicsResourceGraphState(bufferDescription: null, textureDescription: description);
        var uploadedSubresource = new TextureSubresource(0, 1, TextureAspect.Color);
        state.RecordTextureUpload(uploadedSubresource);

        (var uploadedState, var uploadedContents) = state.GetTextureState(new GraphTextureRange(0, 1, 1, 1, TextureAspect.Color));
        (var untouchedState, var untouchedContents) = state.GetTextureState(new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color));

        Assert.That(uploadedState, Is.EqualTo(GraphResourceState.General));
        Assert.That(uploadedContents, Is.True);
        Assert.That(untouchedState, Is.EqualTo(GraphResourceState.Undefined));
        Assert.That(untouchedContents, Is.False);
        _ = Assert.Throws<System.InvalidOperationException>(
            () => state.GetTextureState(new GraphTextureRange(0, 1, 0, 2, TextureAspect.Color)));

        state.RecordTextureReadback(uploadedSubresource);
        (var readbackState, var readbackContents) = state.GetTextureState(new GraphTextureRange(0, 1, 1, 1, TextureAspect.Color));
        Assert.That(readbackState, Is.EqualTo(GraphResourceState.General));
        Assert.That(readbackContents, Is.True);
    }
}
