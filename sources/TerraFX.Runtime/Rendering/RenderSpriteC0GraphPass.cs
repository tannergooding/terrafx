// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

internal sealed class RenderSpriteC0GraphPass
{
    internal RenderSpriteC0GraphPass(
        RenderSpriteBatch batch,
        GraphTextureVersion atlasPage,
        GraphBufferVersion frameBuffer,
        int frameStride,
        int cameraIndex,
        GraphBufferVersion objectBuffer,
        int objectStride,
        GraphBufferVersion vertexBuffer,
        GraphBufferVersion indexBuffer)
    {
        ArgumentNullException.ThrowIfNull(atlasPage);
        ArgumentNullException.ThrowIfNull(frameBuffer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameStride);
        ArgumentOutOfRangeException.ThrowIfNegative(cameraIndex);
        ArgumentNullException.ThrowIfNull(objectBuffer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(objectStride);
        ArgumentNullException.ThrowIfNull(vertexBuffer);
        ArgumentNullException.ThrowIfNull(indexBuffer);

        Batch = batch;
        AtlasPage = atlasPage;
        FrameBuffer = frameBuffer;
        FrameStride = frameStride;
        CameraIndex = cameraIndex;
        ObjectBuffer = objectBuffer;
        ObjectStride = objectStride;
        VertexBuffer = vertexBuffer;
        IndexBuffer = indexBuffer;
    }

    internal GraphTextureVersion AtlasPage { get; }

    internal RenderSpriteBatch Batch { get; }

    internal int CameraIndex { get; }

    internal GraphBufferVersion FrameBuffer { get; }

    internal int FrameStride { get; }

    internal GraphBufferVersion IndexBuffer { get; }

    internal GraphBufferVersion ObjectBuffer { get; }

    internal int ObjectStride { get; }

    internal GraphBufferVersion VertexBuffer { get; }

    internal void Record(scoped ref CommandRecorder recorder, RenderBuiltInSubmissionResources resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var pipeline = resources.SpritePipeline
            ?? throw new InvalidOperationException("The built-in Sprite pass has no Sprite pipeline.");
        var sampler = resources.Sampler
            ?? throw new InvalidOperationException("The built-in Sprite pass has no sampler.");
        recorder.SetPipeline(pipeline.Handle);
        recorder.SetVertexBuffer(new VertexBufferBinding(
            0,
            VertexBuffer,
            new GraphBufferRange(0, VertexBuffer.Description.ByteLength),
            RenderSpriteVertexBufferBuilder.VertexStride));
        recorder.SetIndexBuffer(new IndexBufferBinding(
            IndexBuffer,
            new GraphBufferRange(0, IndexBuffer.Description.ByteLength),
            IndexFormat.UInt16));

        var frameRange = new GraphBufferRange(
            checked((long)CameraIndex * FrameStride),
            RenderCameraGpuData.SizeInBytes);
        var sampleRange = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);

        foreach (var drawIndex in Batch.DrawIndices)
        {
            var objectRange = new GraphBufferRange(
                checked((long)drawIndex * ObjectStride),
                RenderSpriteGpuData.SizeInBytes);
            var bindings = new BindingSet(
                [
                    new BufferBinding(
                        ShaderBindingGroup.Frame,
                        0,
                        ShaderResourceKind.UniformBuffer,
                        FrameBuffer,
                        frameRange),
                    new BufferBinding(
                        ShaderBindingGroup.ObjectData,
                        0,
                        ShaderResourceKind.UniformBuffer,
                        ObjectBuffer,
                        objectRange),
                ],
                [
                    new TextureBinding(
                        ShaderBindingGroup.Material,
                        0,
                        ShaderResourceKind.SampledImage,
                        AtlasPage,
                        sampleRange),
                ],
                [
                    new SamplerBinding(
                        ShaderBindingGroup.Material,
                        0,
                        sampler),
                ]);
            recorder.SetBindings(bindings);
            recorder.DrawIndexed(new DrawIndexedArguments(
                RenderSpriteGeometry.Indices.Length,
                instanceCount: 1,
                firstIndex: 0,
                baseVertex: checked(drawIndex * 4),
                firstInstance: 0));
        }
    }
}
