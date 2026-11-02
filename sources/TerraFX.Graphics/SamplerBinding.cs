// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Describes one sampler binding for a reflected shader resource.</summary>
public sealed class SamplerBinding
{
    /// <summary>Initializes a sampler binding.</summary>
    /// <param name="group">The portable shader resource group.</param>
    /// <param name="binding">The index within the sampler resource class.</param>
    /// <param name="sampler">The owned GPU sampler.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sampler"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The group or binding is invalid.</exception>
    public SamplerBinding(ShaderBindingGroup group, int binding, GpuSampler sampler)
    {
        _ = new ShaderResourceSignature(group, binding, ShaderResourceKind.Sampler, arrayCount: 1);
        ArgumentNullException.ThrowIfNull(sampler);

        Binding = binding;
        Group = group;
        Sampler = sampler;
    }

    /// <summary>Gets the index within the sampler resource class.</summary>
    public int Binding { get; }

    /// <summary>Gets the portable shader resource group.</summary>
    public ShaderBindingGroup Group { get; }

    /// <summary>Gets the owned GPU sampler.</summary>
    public GpuSampler Sampler { get; }
}
