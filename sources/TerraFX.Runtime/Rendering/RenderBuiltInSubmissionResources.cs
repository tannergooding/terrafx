// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX.Graphics;

namespace TerraFX.Rendering;

internal sealed class RenderBuiltInSubmissionResources
{
    internal RenderBuiltInSubmissionResources(
        GpuPipeline? spritePipeline,
        GpuPipeline? meshPipeline,
        GpuPipeline? toneMapPipeline,
        GpuSampler? sampler)
    {
        MeshPipeline = meshPipeline;
        Sampler = sampler;
        SpritePipeline = spritePipeline;
        ToneMapPipeline = toneMapPipeline;
    }

    internal GpuPipeline? MeshPipeline { get; }

    internal GpuSampler? Sampler { get; }

    internal GpuPipeline? SpritePipeline { get; }

    internal GpuPipeline? ToneMapPipeline { get; }
}
