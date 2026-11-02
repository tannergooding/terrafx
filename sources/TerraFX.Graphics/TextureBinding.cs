// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Describes one graph-texture binding for a reflected shader resource.</summary>
public sealed class TextureBinding
{
    /// <summary>Initializes a typed graph-texture binding.</summary>
    /// <param name="group">The portable shader resource group.</param>
    /// <param name="binding">The index within the selected resource class.</param>
    /// <param name="kind">The texture resource class.</param>
    /// <param name="resource">The immutable graph texture version.</param>
    /// <param name="range">The bound subresource range within the graph texture version.</param>
    /// <exception cref="ArgumentException">The range is outside the graph version or the texture usage does not support <paramref name="kind"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The group, binding, or resource kind is invalid.</exception>
    public TextureBinding(
        ShaderBindingGroup group,
        int binding,
        ShaderResourceKind kind,
        GraphTextureVersion resource,
        GraphTextureRange range)
    {
        if (kind is not (ShaderResourceKind.SampledImage or ShaderResourceKind.StorageImage))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        _ = new ShaderResourceSignature(group, binding, kind, arrayCount: 1);
        ArgumentNullException.ThrowIfNull(resource);

        if (!range.IsWithin(resource.Range))
        {
            throw new ArgumentException("The bound texture range must fit the graph texture version.", nameof(range));
        }

        var requiredUsage = (kind == ShaderResourceKind.SampledImage)
            ? TextureUsage.Sampled
            : TextureUsage.Storage;

        if ((resource.Description.Usage & requiredUsage) != requiredUsage)
        {
            throw new ArgumentException($"The graph texture description does not declare {requiredUsage} usage.", nameof(resource));
        }

        Binding = binding;
        Group = group;
        Kind = kind;
        Range = range;
        Resource = resource;
    }

    /// <summary>Gets the index within the selected resource class.</summary>
    public int Binding { get; }

    /// <summary>Gets the portable shader resource group.</summary>
    public ShaderBindingGroup Group { get; }

    /// <summary>Gets the texture resource class.</summary>
    public ShaderResourceKind Kind { get; }

    /// <summary>Gets the bound subresource range.</summary>
    public GraphTextureRange Range { get; }

    /// <summary>Gets the immutable graph texture version.</summary>
    public GraphTextureVersion Resource { get; }
}
