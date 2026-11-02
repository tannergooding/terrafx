// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Graphics;

/// <summary>Describes stable ordering and explicit predecessors for one graph pass.</summary>
public sealed class RenderPassDescription
{
    /// <summary>Initializes one immutable pass description.</summary>
    /// <param name="name">The nonempty stable pass name.</param>
    /// <param name="order">The caller-declared topological ordering key.</param>
    /// <param name="predecessors">The initialized explicit predecessor list.</param>
    /// <param name="hasExternalSideEffects">Whether the pass has effects outside graph resources.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or <paramref name="predecessors"/> is default or contains an invalid pass.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public RenderPassDescription(
        string name,
        int order,
        ImmutableArray<RenderPassId> predecessors,
        bool hasExternalSideEffects)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length == 0)
        {
            throw new ArgumentException("A render pass name cannot be empty.", nameof(name));
        }

        if (predecessors.IsDefault)
        {
            throw new ArgumentException("The predecessor list must be initialized.", nameof(predecessors));
        }

        foreach (var predecessor in predecessors)
        {
            if (!predecessor.IsValid)
            {
                throw new ArgumentException("Every predecessor must identify a graph pass.", nameof(predecessors));
            }
        }

        HasExternalSideEffects = hasExternalSideEffects;
        Name = name;
        Order = order;
        Predecessors = predecessors;
    }

    /// <summary>Gets whether the pass has effects outside graph resources.</summary>
    public bool HasExternalSideEffects { get; }

    /// <summary>Gets the stable pass name.</summary>
    public string Name { get; }

    /// <summary>Gets the caller-declared topological ordering key.</summary>
    public int Order { get; }

    /// <summary>Gets the explicit predecessor passes.</summary>
    public ImmutableArray<RenderPassId> Predecessors { get; }
}
