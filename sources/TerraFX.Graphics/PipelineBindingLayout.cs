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

    internal DistinctResourceEnumerator EnumerateDistinctResources()
        => new(ShaderReflections.AsSpan());

    internal ref struct DistinctResourceEnumerator
    {
        private ShaderResourceSignature _current;
        private int _reflectionIndex;
        private readonly ReadOnlySpan<ShaderReflection> _reflections;
        private int _resourceIndex;

        internal DistinctResourceEnumerator(ReadOnlySpan<ShaderReflection> reflections)
        {
            _current = default;
            _reflectionIndex = 0;
            _reflections = reflections;
            _resourceIndex = 0;
        }

        public readonly ShaderResourceSignature Current => _current;

        public readonly DistinctResourceEnumerator GetEnumerator()
            => new(_reflections);

        public bool MoveNext()
        {
            while (_reflectionIndex < _reflections.Length)
            {
                var resources = _reflections[_reflectionIndex].Resources;

                if (_resourceIndex == resources.Length)
                {
                    _reflectionIndex++;
                    _resourceIndex = 0;
                    continue;
                }

                var resource = resources[_resourceIndex++];

                if (HasEarlierReflectionResource(resource))
                {
                    continue;
                }

                _current = resource;
                return true;
            }

            _current = default;
            return false;
        }

        private readonly bool HasEarlierReflectionResource(ShaderResourceSignature resource)
        {
            // Each reflection rejects repeated register identities; only earlier
            // reflections can contain a signature already yielded by this view.
            for (var index = 0; index < _reflectionIndex; index++)
            {
                var resources = _reflections[index].Resources;
                var first = 0;
                var last = resources.Length - 1;

                while (first <= last)
                {
                    var middle = first + ((last - first) / 2);
                    var previous = resources[middle];
                    var comparison = ShaderReflection.CompareResourceIdentity(previous, resource);

                    if (comparison < 0)
                    {
                        first = middle + 1;
                    }
                    else if (comparison > 0)
                    {
                        last = middle - 1;
                    }
                    else
                    {
                        if (previous == resource)
                        {
                            return true;
                        }

                        break;
                    }
                }
            }

            return false;
        }
    }
}
