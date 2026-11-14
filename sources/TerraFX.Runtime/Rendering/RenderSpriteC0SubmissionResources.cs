// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

internal sealed class RenderSpriteC0SubmissionResources
{
    internal RenderSpriteC0SubmissionResources(
        GpuPipeline pipeline,
        GpuPipeline toneMapPipeline,
        GpuSampler sampler)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(toneMapPipeline);
        ArgumentNullException.ThrowIfNull(sampler);
        Pipeline = pipeline;
        ToneMapPipeline = toneMapPipeline;
        Sampler = sampler;
    }

    internal GpuPipeline Pipeline { get; }

    internal GpuSampler Sampler { get; }

    internal GpuPipeline ToneMapPipeline { get; }
}
