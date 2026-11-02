// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;

namespace TerraFX.Graphics;

/// <summary>Contains copied, canonically ordered graph-resource and sampler bindings.</summary>
public sealed class BindingSet
{
    private readonly ImmutableArray<BufferBinding> _buffers;
    private readonly ImmutableArray<SamplerBinding> _samplers;
    private readonly ImmutableArray<TextureBinding> _textures;

    /// <summary>Initializes an immutable binding set.</summary>
    /// <param name="buffers">The initialized buffer bindings.</param>
    /// <param name="textures">The initialized texture bindings.</param>
    /// <param name="samplers">The initialized sampler bindings.</param>
    /// <exception cref="ArgumentException">An array is default, contains null values, or repeats a group/resource-class/binding identity.</exception>
    public BindingSet(
        ImmutableArray<BufferBinding> buffers,
        ImmutableArray<TextureBinding> textures,
        ImmutableArray<SamplerBinding> samplers)
    {
        if (buffers.IsDefault || textures.IsDefault || samplers.IsDefault)
        {
            throw new ArgumentException("Binding-set arrays must be initialized.");
        }

        var sortedBuffers = buffers.ToArray();

        for (var index = 0; index < sortedBuffers.Length; index++)
        {
            if (sortedBuffers[index] is null)
            {
                throw new ArgumentException("Buffer bindings cannot contain null values.", nameof(buffers));
            }
        }

        Array.Sort(sortedBuffers, static (left, right) => {
            var groupComparison = left.Group.CompareTo(right.Group);

            if (groupComparison != 0)
            {
                return groupComparison;
            }

            var kindComparison = left.Kind.CompareTo(right.Kind);
            return (kindComparison != 0)
                ? kindComparison
                : left.Binding.CompareTo(right.Binding);
        });

        for (var index = 1; index < sortedBuffers.Length; index++)
        {
            var previous = sortedBuffers[index - 1];
            var current = sortedBuffers[index];

            if ((current.Group == previous.Group)
                && (current.Kind == previous.Kind)
                && (current.Binding == previous.Binding))
            {
                throw new ArgumentException("Buffer binding identities must be unique.", nameof(buffers));
            }
        }

        var sortedTextures = textures.ToArray();

        for (var index = 0; index < sortedTextures.Length; index++)
        {
            if (sortedTextures[index] is null)
            {
                throw new ArgumentException("Texture bindings cannot contain null values.", nameof(textures));
            }
        }

        Array.Sort(sortedTextures, static (left, right) => {
            var groupComparison = left.Group.CompareTo(right.Group);

            if (groupComparison != 0)
            {
                return groupComparison;
            }

            var kindComparison = left.Kind.CompareTo(right.Kind);
            return (kindComparison != 0)
                ? kindComparison
                : left.Binding.CompareTo(right.Binding);
        });

        for (var index = 1; index < sortedTextures.Length; index++)
        {
            var previous = sortedTextures[index - 1];
            var current = sortedTextures[index];

            if ((current.Group == previous.Group)
                && (current.Kind == previous.Kind)
                && (current.Binding == previous.Binding))
            {
                throw new ArgumentException("Texture binding identities must be unique.", nameof(textures));
            }
        }

        var sortedSamplers = samplers.ToArray();

        for (var index = 0; index < sortedSamplers.Length; index++)
        {
            if (sortedSamplers[index] is null)
            {
                throw new ArgumentException("Sampler bindings cannot contain null values.", nameof(samplers));
            }
        }

        Array.Sort(sortedSamplers, static (left, right) => {
            var groupComparison = left.Group.CompareTo(right.Group);
            return (groupComparison != 0)
                ? groupComparison
                : left.Binding.CompareTo(right.Binding);
        });

        for (var index = 1; index < sortedSamplers.Length; index++)
        {
            var previous = sortedSamplers[index - 1];
            var current = sortedSamplers[index];

            if ((current.Group == previous.Group) && (current.Binding == previous.Binding))
            {
                throw new ArgumentException("Sampler binding identities must be unique.", nameof(samplers));
            }
        }

        _buffers = [.. sortedBuffers];
        _samplers = [.. sortedSamplers];
        _textures = [.. sortedTextures];
    }

    /// <summary>Gets the copied buffer bindings in group/resource-class/binding order.</summary>
    public ImmutableArray<BufferBinding> Buffers => _buffers;

    /// <summary>Gets the copied sampler bindings in group/binding order.</summary>
    public ImmutableArray<SamplerBinding> Samplers => _samplers;

    /// <summary>Gets the copied texture bindings in group/resource-class/binding order.</summary>
    public ImmutableArray<TextureBinding> Textures => _textures;
}
