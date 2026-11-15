// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

internal sealed class RenderMeshC0ShadowGraphPass
{
    private const int Matrix4x4SizeInBytes = 16 * sizeof(float);
    private bool _bonePaletteWriteVersionSet;

    internal RenderMeshC0ShadowGraphPass(
        int drawIndex,
        RenderMeshGroup group,
        RenderMeshIndexFormat indexFormat,
        GraphBufferVersion frameBuffer,
        long frameOffset,
        GraphBufferVersion objectBuffer,
        int objectStride,
        GraphBufferVersion materialBuffer,
        int materialStride,
        GraphBufferVersion vertexBuffer,
        GraphBufferVersion indexBuffer,
        GraphBufferVersion? bonePaletteBuffer,
        bool skinned,
        GraphTextureVersion? baseColorTexture)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(drawIndex);
        ArgumentNullException.ThrowIfNull(frameBuffer);
        ArgumentOutOfRangeException.ThrowIfNegative(frameOffset);
        ArgumentNullException.ThrowIfNull(objectBuffer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(objectStride);
        ArgumentNullException.ThrowIfNull(materialBuffer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(materialStride);
        ArgumentNullException.ThrowIfNull(vertexBuffer);
        ArgumentNullException.ThrowIfNull(indexBuffer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(group.IndexCount);

        if (skinned)
        {
            ArgumentNullException.ThrowIfNull(bonePaletteBuffer);
        }

        DrawIndex = drawIndex;
        Group = group;
        MeshIndexFormat = indexFormat;
        FrameBuffer = frameBuffer;
        FrameOffset = frameOffset;
        ObjectBuffer = objectBuffer;
        ObjectStride = objectStride;
        MaterialBuffer = materialBuffer;
        MaterialStride = materialStride;
        VertexBuffer = vertexBuffer;
        IndexBuffer = indexBuffer;
        BonePaletteBuffer = bonePaletteBuffer;
        Skinned = skinned;
        BaseColorTexture = baseColorTexture;
    }

    internal GraphTextureVersion? BaseColorTexture { get; }

    internal GraphBufferVersion? BonePaletteBuffer { get; private set; }

    internal int DrawIndex { get; }

    internal GraphBufferVersion FrameBuffer { get; }

    internal long FrameOffset { get; }

    internal RenderMeshGroup Group { get; }

    internal GraphBufferVersion IndexBuffer { get; }

    internal GraphBufferVersion MaterialBuffer { get; }

    internal int MaterialStride { get; }

    internal RenderMeshIndexFormat MeshIndexFormat { get; }

    internal GraphBufferVersion ObjectBuffer { get; }

    internal int ObjectStride { get; }

    internal bool Skinned { get; }

    internal GraphBufferVersion VertexBuffer { get; }

    internal void Record(scoped ref CommandRecorder recorder, RenderBuiltInSubmissionResources resources)
    {
        var pipeline = (Skinned, BaseColorTexture is not null) switch {
            (false, false) => resources.MeshShadowPipeline,
            (false, true) => resources.TexturedMeshShadowPipeline,
            (true, false) => resources.SkinnedMeshShadowPipeline,
            (true, true) => resources.SkinnedTexturedMeshShadowPipeline,
        } ?? throw new InvalidOperationException("The built-in directional shadow pass has no Mesh pipeline.");

        recorder.SetPipeline(pipeline.Handle);
        recorder.SetVertexBuffer(new VertexBufferBinding(
            0,
            VertexBuffer,
            new GraphBufferRange(0, VertexBuffer.Description.ByteLength),
            RenderMeshC0VertexBufferBuilder.VertexStride));
        recorder.SetIndexBuffer(new IndexBufferBinding(
            IndexBuffer,
            new GraphBufferRange(0, IndexBuffer.Description.ByteLength),
            MeshIndexFormat == RenderMeshIndexFormat.UInt16 ? IndexFormat.UInt16 : IndexFormat.UInt32));

        var bufferBindings = ImmutableArray.CreateBuilder<BufferBinding>(Skinned ? 4 : 3);
        bufferBindings.Add(new BufferBinding(
            ShaderBindingGroup.Frame,
            0,
            ShaderResourceKind.UniformBuffer,
            FrameBuffer,
            new GraphBufferRange(
                FrameOffset,
                Matrix4x4SizeInBytes)));
        bufferBindings.Add(new BufferBinding(
            ShaderBindingGroup.ObjectData,
            0,
            ShaderResourceKind.UniformBuffer,
            ObjectBuffer,
            new GraphBufferRange(
                checked((long)DrawIndex * ObjectStride),
                ObjectGpuData.SizeInBytes)));
        bufferBindings.Add(new BufferBinding(
            ShaderBindingGroup.Material,
            0,
            ShaderResourceKind.UniformBuffer,
            MaterialBuffer,
            new GraphBufferRange(
                checked((long)DrawIndex * MaterialStride),
                RenderMeshC0MaterialData.SizeInBytes)));

        if (Skinned)
        {
            var bonePaletteBuffer = BonePaletteBuffer
                ?? throw new InvalidOperationException("A skinned directional shadow pass has no bone-palette buffer.");
            bufferBindings.Add(new BufferBinding(
                ShaderBindingGroup.ObjectData,
                0,
                ShaderResourceKind.StorageBuffer,
                bonePaletteBuffer,
                new GraphBufferRange(0, bonePaletteBuffer.Description.ByteLength)));
        }

        var baseColorTexture = BaseColorTexture;
        ImmutableArray<TextureBinding> textures = baseColorTexture is GraphTextureVersion texture
            ? [
                new TextureBinding(
                ShaderBindingGroup.Material,
                0,
                ShaderResourceKind.SampledImage,
                texture,
                new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color)),
            ]
            : [];
        ImmutableArray<SamplerBinding> samplers = baseColorTexture is not null
            ? [
                new SamplerBinding(
                ShaderBindingGroup.Material,
                0,
                resources.Sampler
                    ?? throw new InvalidOperationException("The textured directional shadow pass has no sampler.")),
            ]
            : [];

        recorder.SetBindings(new BindingSet(bufferBindings.MoveToImmutable(), textures, samplers));
        recorder.DrawIndexed(new DrawIndexedArguments(
            Group.IndexCount,
            instanceCount: 1,
            firstIndex: Group.FirstIndex,
            baseVertex: 0,
            firstInstance: 0));
    }

    internal void SetBonePaletteWriteVersion(GraphBufferVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        var bonePaletteBuffer = BonePaletteBuffer
            ?? throw new InvalidOperationException("A skinned directional shadow pass has no bone-palette source buffer.");

        if (_bonePaletteWriteVersionSet || (version.ResourceId != bonePaletteBuffer.ResourceId))
        {
            throw new InvalidOperationException("The directional shadow bone-palette version does not match its source buffer.");
        }

        BonePaletteBuffer = version;
        _bonePaletteWriteVersionSet = true;
    }
}
