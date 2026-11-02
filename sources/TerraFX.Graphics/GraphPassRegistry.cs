// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace TerraFX.Graphics;

internal sealed class GraphPassRegistry
{
    private readonly uint _graphOwnerId;
    private readonly int _ownerThreadId;
    private readonly List<RenderGraphPassNode> _passes = [];

    internal GraphPassRegistry(uint graphOwnerId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(graphOwnerId);
        _graphOwnerId = graphOwnerId;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    internal uint GraphOwnerId => _graphOwnerId;

    internal ImmutableArray<RenderGraphPassNode> Passes
    {
        get
        {
            EnsureOwnerThread();
            return [.. _passes];
        }
    }

    internal RenderPassId AddPass(RenderPassDescription description)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(description);

        foreach (var predecessor in description.Predecessors)
        {
            if (predecessor.GraphOwnerId != _graphOwnerId)
            {
                throw new ArgumentException("Every predecessor must belong to this graph.", nameof(description));
            }

            var predecessorFound = false;

            foreach (var pass in _passes)
            {
                if (pass.Id == predecessor)
                {
                    predecessorFound = true;
                    break;
                }
            }

            if (!predecessorFound)
            {
                throw new ArgumentException("Every predecessor must already be registered in this graph.", nameof(description));
            }
        }

        var passId = new RenderPassId(_graphOwnerId, checked(_passes.Count));
        _passes.Add(new RenderGraphPassNode(passId, description));
        return passId;
    }

    internal bool TryOrder(
        ImmutableArray<GraphResourceAccess> accesses,
        out ImmutableArray<RenderPassId> orderedPasses)
    {
        EnsureOwnerThread();
        return RenderGraphDependencyCompiler.TryOrder(Passes, accesses, out orderedPasses);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("Graph pass declarations are restricted to their creating thread.");
        }
    }
}
