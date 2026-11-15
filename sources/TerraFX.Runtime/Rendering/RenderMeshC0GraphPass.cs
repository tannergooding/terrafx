// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

internal sealed class RenderMeshC0GraphPass
{
    private bool _bonePaletteWriteVersionSet;

    internal RenderMeshC0GraphPass(
        int drawIndex,
        RenderMeshGroup group,
        RenderMeshIndexFormat indexFormat,
        GraphBufferVersion frameBuffer,
        GraphBufferVersion pointLightBuffer,
        GraphBufferVersion directionalLightBuffer,
        GraphTextureVersion shadowAtlas,
        int frameStride,
        int cameraIndex,
        GraphBufferVersion objectBuffer,
        int objectStride,
        GraphBufferVersion materialBuffer,
        int materialStride,
        GraphBufferVersion vertexBuffer,
        GraphBufferVersion indexBuffer,
        GraphBufferVersion? bonePaletteBuffer,
        bool skinned,
        GraphTextureVersion? baseColorTexture,
        GraphTextureVersion? emissiveTexture,
        GraphTextureVersion? packedMetalRoughOcclusionTexture,
        GraphTextureVersion? normalTexture,
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
        ArgumentNullException.ThrowIfNull(directionalLightBuffer);
        ArgumentNullException.ThrowIfNull(shadowAtlas);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameStride);
        ArgumentOutOfRangeException.ThrowIfNegative(cameraIndex);
        ArgumentNullException.ThrowIfNull(objectBuffer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(objectStride);
        ArgumentNullException.ThrowIfNull(materialBuffer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(materialStride);
        ArgumentNullException.ThrowIfNull(vertexBuffer);
        ArgumentNullException.ThrowIfNull(indexBuffer);

        if (skinned)
        {
            ArgumentNullException.ThrowIfNull(bonePaletteBuffer);
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(group.IndexCount);

        DrawIndex = drawIndex;
        Group = group;
        MeshIndexFormat = indexFormat;
        FrameBuffer = frameBuffer;
        PointLightBuffer = pointLightBuffer;
        DirectionalLightBuffer = directionalLightBuffer;
        ShadowAtlas = shadowAtlas;
        FrameStride = frameStride;
        CameraIndex = cameraIndex;
        ObjectBuffer = objectBuffer;
        ObjectStride = objectStride;
        MaterialBuffer = materialBuffer;
        MaterialStride = materialStride;
        VertexBuffer = vertexBuffer;
        IndexBuffer = indexBuffer;
        BonePaletteBuffer = bonePaletteBuffer;
        Skinned = skinned;
        BaseColorTexture = baseColorTexture;
        EmissiveTexture = emissiveTexture;
        PackedMetalRoughOcclusionTexture = packedMetalRoughOcclusionTexture;
        NormalTexture = normalTexture;
        AlphaMask = alphaMask;
        AlphaBlend = alphaBlend;
    }

    internal bool AlphaBlend { get; }

    internal bool AlphaMask { get; }

    internal GraphTextureVersion? BaseColorTexture { get; }

    internal GraphBufferVersion? BonePaletteBuffer { get; private set; }

    internal int CameraIndex { get; }

    internal GraphBufferVersion DirectionalLightBuffer { get; }

    internal int DrawIndex { get; }

    internal GraphTextureVersion? EmissiveTexture { get; }

    internal GraphBufferVersion FrameBuffer { get; }

    internal int FrameStride { get; }

    internal RenderMeshGroup Group { get; }

    internal GraphBufferVersion IndexBuffer { get; }

    internal GraphBufferVersion MaterialBuffer { get; }

    internal int MaterialStride { get; }

    internal RenderMeshIndexFormat MeshIndexFormat { get; }

    internal GraphTextureVersion? NormalTexture { get; }

    internal GraphBufferVersion ObjectBuffer { get; }

    internal int ObjectStride { get; }

    internal GraphTextureVersion? PackedMetalRoughOcclusionTexture { get; }

    internal GraphBufferVersion PointLightBuffer { get; }

    internal GraphTextureVersion ShadowAtlas { get; }

    internal bool Skinned { get; }

    internal GraphBufferVersion VertexBuffer { get; }

    internal void Record(scoped ref CommandRecorder recorder, RenderBuiltInSubmissionResources resources)
    {
        var hasTexture = (BaseColorTexture is not null)
            || (EmissiveTexture is not null)
            || (PackedMetalRoughOcclusionTexture is not null)
            || (NormalTexture is not null);
        var selectedPipeline = (Skinned, NormalTexture is not null, hasTexture, AlphaMask, AlphaBlend) switch {
            (true, true, _, false, false) => resources.SkinnedNormalMappedMeshPipeline,
            (true, true, _, _, _) => throw new InvalidOperationException("The built-in skinned normal-mapped Mesh pass has an invalid alpha mode."),
            (true, false, false, false, false) => resources.SkinnedMeshPipeline,
            (true, false, false, true, false) => resources.SkinnedMeshAlphaMaskPipeline,
            (true, false, false, false, true) => resources.SkinnedMeshBlendPipeline,
            (true, false, true, false, false) => resources.SkinnedTexturedMeshPipeline,
            (true, false, true, true, false) => resources.SkinnedTexturedMeshAlphaMaskPipeline,
            (true, false, true, false, true) => resources.SkinnedTexturedMeshBlendPipeline,
            (false, true, _, false, false) => resources.NormalMappedMeshPipeline,
            (false, true, _, _, _) => throw new InvalidOperationException("The built-in normal-mapped Mesh pass has an invalid alpha mode."),
            (false, false, false, false, false) => resources.MeshPipeline,
            (false, false, false, true, false) => resources.MeshAlphaMaskPipeline,
            (false, false, false, false, true) => resources.MeshBlendPipeline,
            (false, false, true, false, false) => resources.TexturedMeshPipeline,
            (false, false, true, true, false) => resources.TexturedMeshAlphaMaskPipeline,
            (false, false, true, false, true) => resources.TexturedMeshBlendPipeline,
            _ => throw new InvalidOperationException("The built-in Mesh pass has an invalid alpha mode."),
        };

        var pipeline = selectedPipeline
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
        var sharedTexture = BaseColorTexture ?? EmissiveTexture ?? PackedMetalRoughOcclusionTexture ?? NormalTexture;
        var texturesBuilder = ImmutableArray.CreateBuilder<TextureBinding>(hasTexture ? 5 : 1);
        texturesBuilder.Add(new TextureBinding(
            ShaderBindingGroup.Frame,
            0,
            ShaderResourceKind.SampledImage,
            ShadowAtlas,
            new GraphTextureRange(0, 1, 0, 1, TextureAspect.Depth)));

        if (hasTexture)
        {
            texturesBuilder.Add(new TextureBinding(
                ShaderBindingGroup.Material,
                0,
                ShaderResourceKind.SampledImage,
                BaseColorTexture ?? sharedTexture
                    ?? throw new InvalidOperationException("The textured Mesh pass has no base-color binding."),
                new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color)));
            texturesBuilder.Add(new TextureBinding(
                ShaderBindingGroup.Material,
                1,
                ShaderResourceKind.SampledImage,
                EmissiveTexture ?? sharedTexture
                    ?? throw new InvalidOperationException("The textured Mesh pass has no emissive binding."),
                new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color)));
            texturesBuilder.Add(new TextureBinding(
                ShaderBindingGroup.Material,
                2,
                ShaderResourceKind.SampledImage,
                PackedMetalRoughOcclusionTexture ?? sharedTexture
                    ?? throw new InvalidOperationException("The textured Mesh pass has no packed data binding."),
                new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color)));
            texturesBuilder.Add(new TextureBinding(
                ShaderBindingGroup.Material,
                3,
                ShaderResourceKind.SampledImage,
                NormalTexture ?? sharedTexture
                    ?? throw new InvalidOperationException("The textured Mesh pass has no normal binding."),
                new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color)));
        }

        var textures = texturesBuilder.MoveToImmutable();
        var samplersBuilder = ImmutableArray.CreateBuilder<SamplerBinding>(hasTexture ? 5 : 1);
        samplersBuilder.Add(new SamplerBinding(
            ShaderBindingGroup.Frame,
            0,
            resources.ShadowSampler
                ?? throw new InvalidOperationException("The Mesh pass has no directional shadow sampler.")));

        if (hasTexture)
        {
            samplersBuilder.Add(new SamplerBinding(
                ShaderBindingGroup.Material,
                0,
                resources.Sampler
                    ?? throw new InvalidOperationException("The textured Mesh pass has no default sampler.")));
            samplersBuilder.Add(new SamplerBinding(
                ShaderBindingGroup.Material,
                1,
                resources.Sampler
                    ?? throw new InvalidOperationException("The textured Mesh pass has no default sampler.")));
            samplersBuilder.Add(new SamplerBinding(
                ShaderBindingGroup.Material,
                2,
                resources.Sampler
                    ?? throw new InvalidOperationException("The textured Mesh pass has no default sampler.")));
            samplersBuilder.Add(new SamplerBinding(
                ShaderBindingGroup.Material,
                3,
                resources.Sampler
                    ?? throw new InvalidOperationException("The textured Mesh pass has no default sampler.")));
        }

        var samplers = samplersBuilder.MoveToImmutable();
        var bufferBindings = ImmutableArray.CreateBuilder<BufferBinding>(Skinned ? 6 : 5);
        bufferBindings.Add(new BufferBinding(
            ShaderBindingGroup.Frame,
            0,
            ShaderResourceKind.UniformBuffer,
            FrameBuffer,
            new GraphBufferRange(
                checked((long)CameraIndex * FrameStride),
                RenderMeshC0FrameData.SizeInBytes)));
        bufferBindings.Add(new BufferBinding(
            ShaderBindingGroup.Frame,
            1,
            ShaderResourceKind.UniformBuffer,
            PointLightBuffer,
            new GraphBufferRange(0, PointLightBuffer.Description.ByteLength)));
        bufferBindings.Add(new BufferBinding(
            ShaderBindingGroup.Frame,
            2,
            ShaderResourceKind.UniformBuffer,
            DirectionalLightBuffer,
            new GraphBufferRange(0, DirectionalLightBuffer.Description.ByteLength)));
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
                ?? throw new InvalidOperationException("A skinned Mesh pass has no bone-palette buffer.");
            bufferBindings.Add(new BufferBinding(
                ShaderBindingGroup.ObjectData,
                0,
                ShaderResourceKind.StorageBuffer,
                bonePaletteBuffer,
                new GraphBufferRange(0, bonePaletteBuffer.Description.ByteLength)));
        }

        var bindings = new BindingSet(bufferBindings.MoveToImmutable(), textures, samplers);
        recorder.SetBindings(bindings);
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
            ?? throw new InvalidOperationException("A skinned Mesh pass has no bone-palette source buffer.");

        if (_bonePaletteWriteVersionSet || (version.ResourceId != bonePaletteBuffer.ResourceId))
        {
            throw new InvalidOperationException("The Mesh bone-palette write version does not match its source buffer.");
        }

        BonePaletteBuffer = version;
        _bonePaletteWriteVersionSet = true;
    }
}
