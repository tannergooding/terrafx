// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Describes one graph-buffer binding for a reflected shader resource.</summary>
public sealed class BufferBinding
{
    /// <summary>Initializes a typed graph-buffer binding.</summary>
    /// <param name="group">The portable shader resource group.</param>
    /// <param name="binding">The index within the selected resource class.</param>
    /// <param name="kind">The buffer resource class.</param>
    /// <param name="resource">The immutable graph buffer version.</param>
    /// <param name="range">The bound byte range within the graph buffer version.</param>
    /// <exception cref="ArgumentException">The range is outside the graph version or the buffer usage does not support <paramref name="kind"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The group, binding, or resource kind is invalid.</exception>
    public BufferBinding(
        ShaderBindingGroup group,
        int binding,
        ShaderResourceKind kind,
        GraphBufferVersion resource,
        GraphBufferRange range)
    {
        if (kind is not (ShaderResourceKind.UniformBuffer or ShaderResourceKind.StorageBuffer))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        _ = new ShaderResourceSignature(group, binding, kind, arrayCount: 1);
        ArgumentNullException.ThrowIfNull(resource);

        if (!range.IsWithin(resource.Range))
        {
            throw new ArgumentException("The bound buffer range must fit the graph buffer version.", nameof(range));
        }

        var requiredUsage = (kind == ShaderResourceKind.UniformBuffer)
            ? BufferUsage.Uniform
            : BufferUsage.Storage;

        if ((resource.Description.Usage & requiredUsage) != requiredUsage)
        {
            throw new ArgumentException($"The graph buffer description does not declare {requiredUsage} usage.", nameof(resource));
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

    /// <summary>Gets the buffer resource class.</summary>
    public ShaderResourceKind Kind { get; }

    /// <summary>Gets the bound byte range.</summary>
    public GraphBufferRange Range { get; }

    /// <summary>Gets the immutable graph buffer version.</summary>
    public GraphBufferVersion Resource { get; }
}
