// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

internal sealed class RenderMeshC0GraphPass
{
    internal RenderMeshC0GraphPass(
        int drawIndex,
        RenderMeshGroup group,
        RenderMeshIndexFormat indexFormat,
        GraphBufferVersion frameBuffer,
        GraphBufferVersion pointLightBuffer,
        int frameStride,
        int cameraIndex,
        GraphBufferVersion objectBuffer,
        int objectStride,
        GraphBufferVersion materialBuffer,
        int materialStride,
        GraphBufferVersion vertexBuffer,
        GraphBufferVersion indexBuffer,
        GraphTextureVersion? baseColorTexture,
        GraphTextureVersion? emissiveTexture,
        GraphTextureVersion? packedMetalRoughOcclusionTexture,
        bool alphaMask,
        bool alphaBlend)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(drawIndex);

        if (alphaMask && alphaBlend)
        {
            throw new ArgumentException("A Mesh C0 pass cannot combine alpha masking and blending.");
        }

        ArgumentNullException.ThrowIfNull(frameBuffer);
        ArgumentNullException.ThrowIfNull(pointLightBuffer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameStride);
        ArgumentOutOfRangeException.ThrowIfNegative(cameraIndex);
        ArgumentNullException.ThrowIfNull(objectBuffer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(objectStride);
        ArgumentNullException.ThrowIfNull(materialBuffer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(materialStride);
        ArgumentNullException.ThrowIfNull(vertexBuffer);
        ArgumentNullException.ThrowIfNull(indexBuffer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(group.IndexCount);

        DrawIndex = drawIndex;
        Group = group;
        MeshIndexFormat = indexFormat;
        FrameBuffer = frameBuffer;
        PointLightBuffer = pointLightBuffer;
        FrameStride = frameStride;
        CameraIndex = cameraIndex;
        ObjectBuffer = objectBuffer;
        ObjectStride = objectStride;
        MaterialBuffer = materialBuffer;
        MaterialStride = materialStride;
        VertexBuffer = vertexBuffer;
        IndexBuffer = indexBuffer;
        BaseColorTexture = baseColorTexture;
        EmissiveTexture = emissiveTexture;
        PackedMetalRoughOcclusionTexture = packedMetalRoughOcclusionTexture;
        AlphaMask = alphaMask;
        AlphaBlend = alphaBlend;
    }

    internal bool AlphaBlend { get; }

    internal bool AlphaMask { get; }

    internal GraphTextureVersion? BaseColorTexture { get; }

    internal int CameraIndex { get; }

    internal int DrawIndex { get; }

    internal GraphTextureVersion? EmissiveTexture { get; }

    internal GraphBufferVersion FrameBuffer { get; }

    internal int FrameStride { get; }

    internal RenderMeshGroup Group { get; }

    internal GraphBufferVersion IndexBuffer { get; }

    internal GraphBufferVersion MaterialBuffer { get; }

    internal int MaterialStride { get; }

    internal RenderMeshIndexFormat MeshIndexFormat { get; }

    internal GraphBufferVersion ObjectBuffer { get; }

    internal int ObjectStride { get; }

    internal GraphTextureVersion? PackedMetalRoughOcclusionTexture { get; }

    internal GraphBufferVersion PointLightBuffer { get; }

    internal GraphBufferVersion VertexBuffer { get; }

    internal void Record(scoped ref CommandRecorder recorder, RenderBuiltInSubmissionResources resources)
    {
        var hasTexture = (BaseColorTexture is not null)
            || (EmissiveTexture is not null)
            || (PackedMetalRoughOcclusionTexture is not null);
        var pipeline = (hasTexture, AlphaMask, AlphaBlend) switch {
            (false, false, false) => resources.MeshPipeline,
            (false, true, false) => resources.MeshAlphaMaskPipeline,
            (false, false, true) => resources.MeshBlendPipeline,
            (true, false, false) => resources.TexturedMeshPipeline,
            (true, true, false) => resources.TexturedMeshAlphaMaskPipeline,
            (true, false, true) => resources.TexturedMeshBlendPipeline,
            _ => throw new InvalidOperationException("The built-in Mesh pass has an invalid alpha mode."),
        }
            ?? throw new InvalidOperationException("The built-in Mesh pass has no Mesh pipeline.");
        recorder.SetPipeline(pipeline.Handle);
        recorder.SetVertexBuffer(new VertexBufferBinding(
            0,
            VertexBuffer,
            new GraphBufferRange(0, VertexBuffer.Description.ByteLength),
            RenderMeshC0VertexBufferBuilder.VertexStride));
        recorder.SetIndexBuffer(new IndexBufferBinding(
            IndexBuffer,
            new GraphBufferRange(0, IndexBuffer.Description.ByteLength),
            MeshIndexFormat == RenderMeshIndexFormat.UInt16 ? TerraFX.Graphics.IndexFormat.UInt16 : TerraFX.Graphics.IndexFormat.UInt32));
        var sharedTexture = BaseColorTexture ?? EmissiveTexture ?? PackedMetalRoughOcclusionTexture;
        ImmutableArray<TextureBinding> textures = hasTexture
            ? [
                new TextureBinding(
                    ShaderBindingGroup.Material,
                    0,
                    ShaderResourceKind.SampledImage,
                    BaseColorTexture ?? sharedTexture
                        ?? throw new InvalidOperationException("The textured Mesh pass has no base-color binding."),
                    new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color)),
                new TextureBinding(
                    ShaderBindingGroup.Material,
                    1,
                    ShaderResourceKind.SampledImage,
                    EmissiveTexture ?? sharedTexture
                        ?? throw new InvalidOperationException("The textured Mesh pass has no emissive binding."),
                    new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color)),
                new TextureBinding(
                    ShaderBindingGroup.Material,
                    2,
                    ShaderResourceKind.SampledImage,
                    PackedMetalRoughOcclusionTexture ?? sharedTexture
                        ?? throw new InvalidOperationException("The textured Mesh pass has no packed data binding."),
                    new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color)),
            ]
            : [];
        ImmutableArray<SamplerBinding> samplers = hasTexture
            ? [
                new SamplerBinding(
                    ShaderBindingGroup.Material,
                    0,
                    resources.Sampler
                        ?? throw new InvalidOperationException("The textured Mesh pass has no default sampler.")),
                new SamplerBinding(
                    ShaderBindingGroup.Material,
                    1,
                    resources.Sampler
                        ?? throw new InvalidOperationException("The textured Mesh pass has no default sampler.")),
                new SamplerBinding(
                    ShaderBindingGroup.Material,
                    2,
                    resources.Sampler
                        ?? throw new InvalidOperationException("The textured Mesh pass has no default sampler.")),
            ]
            : [];
        var bindings = new BindingSet(
            [
                new BufferBinding(
                    ShaderBindingGroup.Frame,
                    0,
                    ShaderResourceKind.UniformBuffer,
                    FrameBuffer,
                    new GraphBufferRange(
                        checked((long)CameraIndex * FrameStride),
                        RenderMeshC0FrameData.SizeInBytes)),
                new BufferBinding(
                    ShaderBindingGroup.Frame,
                    1,
                    ShaderResourceKind.UniformBuffer,
                    PointLightBuffer,
                    new GraphBufferRange(0, PointLightBuffer.Description.ByteLength)),
                new BufferBinding(
                    ShaderBindingGroup.ObjectData,
                    0,
                    ShaderResourceKind.UniformBuffer,
                    ObjectBuffer,
                    new GraphBufferRange(
                        checked((long)DrawIndex * ObjectStride),
                        ObjectGpuData.SizeInBytes)),
                new BufferBinding(
                    ShaderBindingGroup.Material,
                    0,
                    ShaderResourceKind.UniformBuffer,
                    MaterialBuffer,
                    new GraphBufferRange(
                        checked((long)DrawIndex * MaterialStride),
                        RenderMeshC0MaterialData.SizeInBytes)),
            ],
            textures,
            samplers);
        recorder.SetBindings(bindings);
        recorder.DrawIndexed(new DrawIndexedArguments(
            Group.IndexCount,
            instanceCount: 1,
            firstIndex: Group.FirstIndex,
            baseVertex: 0,
            firstInstance: 0));
    }
}
