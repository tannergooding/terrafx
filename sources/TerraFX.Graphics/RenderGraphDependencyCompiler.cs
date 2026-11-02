// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace TerraFX.Graphics;

internal static class RenderGraphDependencyCompiler
{
    internal static bool TryOrder(
        ImmutableArray<RenderGraphPassNode> passes,
        out ImmutableArray<RenderPassId> orderedPasses)
    {
        if (passes.IsDefault)
        {
            throw new ArgumentException("The pass list must be initialized.", nameof(passes));
        }

        var passIndices = new Dictionary<RenderPassId, int>(passes.Length);
        var graphOwnerId = 0u;

        for (var index = 0; index < passes.Length; index++)
        {
            var pass = passes[index];

            if (!pass.Id.IsValid)
            {
                throw new ArgumentException("Every graph pass ID must be valid.", nameof(passes));
            }

            if (pass.Description is null)
            {
                throw new ArgumentException("Every graph pass must have a description.", nameof(passes));
            }

            if (graphOwnerId == 0)
            {
                graphOwnerId = pass.Id.GraphOwnerId;
            }
            else if (pass.Id.GraphOwnerId != graphOwnerId)
            {
                throw new ArgumentException("Every pass in the dependency graph must belong to the same graph.", nameof(passes));
            }

            if (!passIndices.TryAdd(pass.Id, index))
            {
                throw new ArgumentException("Graph pass IDs must be unique.", nameof(passes));
            }
        }

        var outgoing = new List<int>[passes.Length];
        var incomingCounts = new int[passes.Length];

        for (var index = 0; index < passes.Length; index++)
        {
            outgoing[index] = [];
        }

        for (var index = 0; index < passes.Length; index++)
        {
            var predecessors = passes[index].Description.Predecessors;
            var uniquePredecessors = new HashSet<RenderPassId>();

            foreach (var predecessor in predecessors)
            {
                if (!uniquePredecessors.Add(predecessor))
                {
                    throw new ArgumentException("A pass cannot declare the same predecessor more than once.", nameof(passes));
                }

                if (!passIndices.TryGetValue(predecessor, out var predecessorIndex))
                {
                    throw new ArgumentException("Every predecessor must belong to the compiled graph.", nameof(passes));
                }

                outgoing[predecessorIndex].Add(index);
                incomingCounts[index] = checked(incomingCounts[index] + 1);
            }
        }

        var ready = new SortedSet<int>(Comparer<int>.Create((left, right) => {
            if (left == right)
            {
                return 0;
            }

            var comparison = passes[left].Description.Order.CompareTo(passes[right].Description.Order);

            if (comparison == 0)
            {
                comparison = StringComparer.Ordinal.Compare(
                    passes[left].Description.Name,
                    passes[right].Description.Name);
            }

            return (comparison != 0)
                ? comparison
                : passes[left].Id.Value.CompareTo(passes[right].Id.Value);
        }));

        for (var index = 0; index < incomingCounts.Length; index++)
        {
            if (incomingCounts[index] == 0)
            {
                _ = ready.Add(index);
            }
        }

        var result = ImmutableArray.CreateBuilder<RenderPassId>(passes.Length);

        while (ready.Count != 0)
        {
            var index = ready.Min;
            _ = ready.Remove(index);
            result.Add(passes[index].Id);

            foreach (var dependentIndex in outgoing[index])
            {
                incomingCounts[dependentIndex]--;

                if (incomingCounts[dependentIndex] == 0)
                {
                    _ = ready.Add(dependentIndex);
                }
            }
        }

        if (result.Count != passes.Length)
        {
            orderedPasses = [];
            return false;
        }

        orderedPasses = result.MoveToImmutable();
        return true;
    }
}
