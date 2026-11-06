// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Graphics;

internal sealed class PipelineBindingLayout
{
    internal PipelineBindingLayout(PipelineDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        BindingLayoutVersion = description.BindingLayoutVersion;

        switch (description)
        {
            case GraphicsPipelineDescription graphicsDescription:
            {
                var reflections = ImmutableArray.CreateBuilder<ShaderReflection>(graphicsDescription.Shaders.Length);

                foreach (var shader in graphicsDescription.Shaders)
                {
                    reflections.Add(shader.Reflection);
                }

                ColorTargetFormats = graphicsDescription.ColorTargetFormats;
                DepthTargetFormat = graphicsDescription.DepthTargetFormat;
                IsCompute = false;
                ShaderReflections = reflections.MoveToImmutable();
                Topology = graphicsDescription.Topology;
                VertexElements = graphicsDescription.VertexElements;
                VertexStride = graphicsDescription.VertexStride;
                break;
            }

            case ComputePipelineDescription computeDescription:
            {
                ColorTargetFormats = [];
                DepthTargetFormat = null;
                IsCompute = true;
                ShaderReflections = [computeDescription.Shader.Reflection];
                Topology = null;
                VertexElements = [];
                VertexStride = 0;
                break;
            }

            default:
            {
                throw new ArgumentOutOfRangeException(nameof(description));
            }
        }
    }

    internal int BindingLayoutVersion { get; }

    internal ImmutableArray<TextureFormat> ColorTargetFormats { get; }

    internal TextureFormat? DepthTargetFormat { get; }

    internal bool IsCompute { get; }

    internal ImmutableArray<ShaderReflection> ShaderReflections { get; }

    internal PrimitiveTopology? Topology { get; }

    internal ImmutableArray<VertexElementDescription> VertexElements { get; }

    internal int VertexStride { get; }
}
