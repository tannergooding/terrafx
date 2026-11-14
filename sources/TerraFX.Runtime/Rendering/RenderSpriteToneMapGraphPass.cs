// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

internal sealed class RenderSpriteToneMapGraphPass
{
    internal RenderSpriteToneMapGraphPass(
        GraphTextureVersion source,
        GraphBufferVersion frameBuffer,
        GraphBufferVersion objectBuffer,
        GraphBufferVersion vertexBuffer,
        GraphBufferVersion indexBuffer)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(frameBuffer);
        ArgumentNullException.ThrowIfNull(objectBuffer);
        ArgumentNullException.ThrowIfNull(vertexBuffer);
        ArgumentNullException.ThrowIfNull(indexBuffer);
        Source = source;
        FrameBuffer = frameBuffer;
        ObjectBuffer = objectBuffer;
        VertexBuffer = vertexBuffer;
        IndexBuffer = indexBuffer;
    }

    internal GraphBufferVersion FrameBuffer { get; }

    internal GraphBufferVersion IndexBuffer { get; }

    internal GraphBufferVersion ObjectBuffer { get; }

    internal GraphTextureVersion Source { get; }

    internal GraphBufferVersion VertexBuffer { get; }

    internal void Record(scoped ref CommandRecorder recorder, RenderSpriteC0SubmissionResources resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        recorder.SetPipeline(resources.ToneMapPipeline.Handle);
        recorder.SetVertexBuffer(new VertexBufferBinding(
            0,
            VertexBuffer,
            new GraphBufferRange(0, VertexBuffer.Description.ByteLength),
            RenderSpriteVertexBufferBuilder.VertexStride));
        recorder.SetIndexBuffer(new IndexBufferBinding(
            IndexBuffer,
            new GraphBufferRange(0, IndexBuffer.Description.ByteLength),
            IndexFormat.UInt16));
        recorder.SetBindings(new BindingSet(
            [
                new BufferBinding(
                    ShaderBindingGroup.Frame,
                    0,
                    ShaderResourceKind.UniformBuffer,
                    FrameBuffer,
                    new GraphBufferRange(0, RenderCameraGpuData.SizeInBytes)),
                new BufferBinding(
                    ShaderBindingGroup.ObjectData,
                    0,
                    ShaderResourceKind.UniformBuffer,
                    ObjectBuffer,
                    new GraphBufferRange(0, RenderSpriteGpuData.SizeInBytes)),
            ],
            [
                new TextureBinding(
                    ShaderBindingGroup.Material,
                    0,
                    ShaderResourceKind.SampledImage,
                    Source,
                    new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color)),
            ],
            [
                new SamplerBinding(
                    ShaderBindingGroup.Material,
                    0,
                    resources.Sampler),
            ]));
        recorder.DrawIndexed(new DrawIndexedArguments(RenderSpriteGeometry.Indices.Length, 1, 0, 0, 0));
    }
}
