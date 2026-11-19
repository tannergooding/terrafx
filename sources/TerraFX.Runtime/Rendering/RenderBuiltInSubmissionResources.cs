// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX.Graphics;

namespace TerraFX.Rendering;

internal sealed class RenderBuiltInSubmissionResources
{
    internal RenderBuiltInSubmissionResources(
        GpuPipeline? meshAlphaMaskPipeline,
        GpuPipeline? spritePipeline,
        GpuPipeline? meshPipeline,
        GpuPipeline? meshShadowPipeline,
        GpuPipeline? skinnedMeshPipeline,
        GpuPipeline? skinnedMeshShadowPipeline,
        GpuPipeline? skinnedMeshAlphaMaskPipeline,
        GpuPipeline? skinnedMeshBlendPipeline,
        GpuPipeline? skinnedNormalDiagnosticComputePipeline,
        GpuPipeline? meshBlendPipeline,
        GpuPipeline? normalMappedMeshPipeline,
        GpuPipeline? skinnedNormalMappedMeshPipeline,
        GpuPipeline? texturedMeshAlphaMaskPipeline,
        GpuPipeline? skinnedTexturedMeshAlphaMaskPipeline,
        GpuPipeline? texturedMeshPipeline,
        GpuPipeline? texturedMeshShadowPipeline,
        GpuPipeline? skinnedTexturedMeshPipeline,
        GpuPipeline? skinnedTexturedMeshShadowPipeline,
        GpuPipeline? texturedMeshBlendPipeline,
        GpuPipeline? skinnedTexturedMeshBlendPipeline,
        GpuPipeline? toneMapPipeline,
        GpuSampler? sampler,
        GpuSampler? shadowSampler)
    {
        MeshAlphaMaskPipeline = meshAlphaMaskPipeline;
        MeshPipeline = meshPipeline;
        MeshShadowPipeline = meshShadowPipeline;
        SkinnedMeshPipeline = skinnedMeshPipeline;
        SkinnedMeshShadowPipeline = skinnedMeshShadowPipeline;
        SkinnedMeshAlphaMaskPipeline = skinnedMeshAlphaMaskPipeline;
        SkinnedMeshBlendPipeline = skinnedMeshBlendPipeline;
        SkinnedNormalDiagnosticComputePipeline = skinnedNormalDiagnosticComputePipeline;
        MeshBlendPipeline = meshBlendPipeline;
        NormalMappedMeshPipeline = normalMappedMeshPipeline;
        SkinnedNormalMappedMeshPipeline = skinnedNormalMappedMeshPipeline;
        TexturedMeshAlphaMaskPipeline = texturedMeshAlphaMaskPipeline;
        SkinnedTexturedMeshAlphaMaskPipeline = skinnedTexturedMeshAlphaMaskPipeline;
        TexturedMeshPipeline = texturedMeshPipeline;
        TexturedMeshShadowPipeline = texturedMeshShadowPipeline;
        SkinnedTexturedMeshPipeline = skinnedTexturedMeshPipeline;
        SkinnedTexturedMeshShadowPipeline = skinnedTexturedMeshShadowPipeline;
        TexturedMeshBlendPipeline = texturedMeshBlendPipeline;
        SkinnedTexturedMeshBlendPipeline = skinnedTexturedMeshBlendPipeline;
        Sampler = sampler;
        SpritePipeline = spritePipeline;
        ToneMapPipeline = toneMapPipeline;
        ShadowSampler = shadowSampler;
    }

    internal GpuPipeline? MeshAlphaMaskPipeline { get; }

    internal GpuPipeline? MeshBlendPipeline { get; }

    internal GpuPipeline? MeshPipeline { get; }

    internal GpuPipeline? MeshShadowPipeline { get; }

    internal GpuPipeline? NormalMappedMeshPipeline { get; }

    internal GpuSampler? Sampler { get; }

    internal GpuSampler? ShadowSampler { get; }

    internal GpuPipeline? SkinnedMeshAlphaMaskPipeline { get; }

    internal GpuPipeline? SkinnedMeshBlendPipeline { get; }

    internal GpuPipeline? SkinnedMeshPipeline { get; }

    internal GpuPipeline? SkinnedMeshShadowPipeline { get; }

    internal GpuPipeline? SkinnedNormalDiagnosticComputePipeline { get; }

    internal GpuPipeline? SkinnedNormalMappedMeshPipeline { get; }

    internal GpuPipeline? SkinnedTexturedMeshAlphaMaskPipeline { get; }

    internal GpuPipeline? SkinnedTexturedMeshBlendPipeline { get; }

    internal GpuPipeline? SkinnedTexturedMeshPipeline { get; }

    internal GpuPipeline? SkinnedTexturedMeshShadowPipeline { get; }

    internal GpuPipeline? SpritePipeline { get; }

    internal GpuPipeline? TexturedMeshAlphaMaskPipeline { get; }

    internal GpuPipeline? TexturedMeshBlendPipeline { get; }

    internal GpuPipeline? TexturedMeshPipeline { get; }

    internal GpuPipeline? TexturedMeshShadowPipeline { get; }

    internal GpuPipeline? ToneMapPipeline { get; }
}
