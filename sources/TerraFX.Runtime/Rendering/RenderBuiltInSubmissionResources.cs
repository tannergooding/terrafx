// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX.Graphics;

namespace TerraFX.Rendering;

internal sealed class RenderBuiltInSubmissionResources
{
    internal RenderBuiltInSubmissionResources(
        GpuPipeline? meshAlphaMaskPipeline,
        GpuPipeline? spritePipeline,
        GpuPipeline? meshPipeline,
        GpuPipeline? skinnedMeshPipeline,
        GpuPipeline? skinnedMeshAlphaMaskPipeline,
        GpuPipeline? skinnedMeshBlendPipeline,
        GpuPipeline? meshBlendPipeline,
        GpuPipeline? normalMappedMeshPipeline,
        GpuPipeline? skinnedNormalMappedMeshPipeline,
        GpuPipeline? texturedMeshAlphaMaskPipeline,
        GpuPipeline? skinnedTexturedMeshAlphaMaskPipeline,
        GpuPipeline? texturedMeshPipeline,
        GpuPipeline? skinnedTexturedMeshPipeline,
        GpuPipeline? texturedMeshBlendPipeline,
        GpuPipeline? skinnedTexturedMeshBlendPipeline,
        GpuPipeline? toneMapPipeline,
        GpuSampler? sampler)
    {
        MeshAlphaMaskPipeline = meshAlphaMaskPipeline;
        MeshPipeline = meshPipeline;
        SkinnedMeshPipeline = skinnedMeshPipeline;
        SkinnedMeshAlphaMaskPipeline = skinnedMeshAlphaMaskPipeline;
        SkinnedMeshBlendPipeline = skinnedMeshBlendPipeline;
        MeshBlendPipeline = meshBlendPipeline;
        NormalMappedMeshPipeline = normalMappedMeshPipeline;
        SkinnedNormalMappedMeshPipeline = skinnedNormalMappedMeshPipeline;
        TexturedMeshAlphaMaskPipeline = texturedMeshAlphaMaskPipeline;
        SkinnedTexturedMeshAlphaMaskPipeline = skinnedTexturedMeshAlphaMaskPipeline;
        TexturedMeshPipeline = texturedMeshPipeline;
        SkinnedTexturedMeshPipeline = skinnedTexturedMeshPipeline;
        TexturedMeshBlendPipeline = texturedMeshBlendPipeline;
        SkinnedTexturedMeshBlendPipeline = skinnedTexturedMeshBlendPipeline;
        Sampler = sampler;
        SpritePipeline = spritePipeline;
        ToneMapPipeline = toneMapPipeline;
    }

    internal GpuPipeline? MeshAlphaMaskPipeline { get; }

    internal GpuPipeline? MeshBlendPipeline { get; }

    internal GpuPipeline? MeshPipeline { get; }

    internal GpuPipeline? NormalMappedMeshPipeline { get; }

    internal GpuSampler? Sampler { get; }

    internal GpuPipeline? SkinnedMeshAlphaMaskPipeline { get; }

    internal GpuPipeline? SkinnedMeshBlendPipeline { get; }

    internal GpuPipeline? SkinnedMeshPipeline { get; }

    internal GpuPipeline? SkinnedNormalMappedMeshPipeline { get; }

    internal GpuPipeline? SkinnedTexturedMeshAlphaMaskPipeline { get; }

    internal GpuPipeline? SkinnedTexturedMeshBlendPipeline { get; }

    internal GpuPipeline? SkinnedTexturedMeshPipeline { get; }

    internal GpuPipeline? SpritePipeline { get; }

    internal GpuPipeline? TexturedMeshAlphaMaskPipeline { get; }

    internal GpuPipeline? TexturedMeshBlendPipeline { get; }

    internal GpuPipeline? TexturedMeshPipeline { get; }

    internal GpuPipeline? ToneMapPipeline { get; }
}
