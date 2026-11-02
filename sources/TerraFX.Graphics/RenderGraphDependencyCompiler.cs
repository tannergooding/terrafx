// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace TerraFX.Graphics;

internal static class RenderGraphDependencyCompiler
{
    internal static bool TryOrder(
        ImmutableArray<RenderGraphPassNode> passes,
        ImmutableArray<GraphResourceAccess> accesses,
        out ImmutableArray<RenderPassId> orderedPasses)
    {
        if (passes.IsDefault)
        {
            throw new ArgumentException("The pass list must be initialized.", nameof(passes));
        }

        if (accesses.IsDefault)
        {
            throw new ArgumentException("The resource access list must be initialized.", nameof(accesses));
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

        var dependencies = new HashSet<(int Predecessor, int Dependent)>();

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

                _ = dependencies.Add((predecessorIndex, index));
            }
        }

        AddResourceDependencies(accesses, passIndices, graphOwnerId, dependencies);
        var outgoing = new List<int>[passes.Length];
        var incomingCounts = new int[passes.Length];

        for (var index = 0; index < passes.Length; index++)
        {
            outgoing[index] = [];
        }

        foreach (var (predecessorIndex, dependentIndex) in dependencies)
        {
            outgoing[predecessorIndex].Add(dependentIndex);
            incomingCounts[dependentIndex] = checked(incomingCounts[dependentIndex] + 1);
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

    private static void AddDependency(
        RenderPassId predecessor,
        RenderPassId dependent,
        Dictionary<RenderPassId, int> passIndices,
        HashSet<(int Predecessor, int Dependent)> dependencies)
        => _ = dependencies.Add((passIndices[predecessor], passIndices[dependent]));

    private static void AddResourceDependencies(
        ImmutableArray<GraphResourceAccess> accesses,
        Dictionary<RenderPassId, int> passIndices,
        uint graphOwnerId,
        HashSet<(int Predecessor, int Dependent)> dependencies)
    {
        var accessesByResource = new Dictionary<(uint DeviceEpoch, ulong ResourceId, bool IsTexture), List<GraphResourceAccess>>();
        var deviceEpoch = 0u;

        foreach (var access in accesses)
        {
            if (!access.PassId.IsValid || (access.PassId.GraphOwnerId != graphOwnerId))
            {
                throw new ArgumentException("Every resource access must name a pass in the compiled graph.", nameof(accesses));
            }

            if (!passIndices.ContainsKey(access.PassId))
            {
                throw new ArgumentException("Every resource access must name a pass in the compiled graph.", nameof(accesses));
            }

            if (deviceEpoch == 0)
            {
                deviceEpoch = access.DeviceEpoch;
            }
            else if (access.DeviceEpoch != deviceEpoch)
            {
                throw new ArgumentException("Every graph resource access must use the same device epoch.", nameof(accesses));
            }

            var resourceKey = (access.DeviceEpoch, access.ResourceId, access.IsTexture);

            if (!accessesByResource.TryGetValue(resourceKey, out var resourceAccesses))
            {
                resourceAccesses = [];
                accessesByResource.Add(resourceKey, resourceAccesses);
            }

            resourceAccesses.Add(access);
        }

        foreach (var resourceAccesses in accessesByResource.Values)
        {
            for (var leftIndex = 0; leftIndex < resourceAccesses.Count; leftIndex++)
            {
                var left = resourceAccesses[leftIndex];

                for (var rightIndex = leftIndex + 1; rightIndex < resourceAccesses.Count; rightIndex++)
                {
                    var right = resourceAccesses[rightIndex];

                    if ((left.PassId == right.PassId)
                        || (!left.Overlaps(right))
                        || (!left.IsWrite && !right.IsWrite))
                    {
                        continue;
                    }

                    if (left.IsWrite && right.IsWrite)
                    {
                        if (left.Version == right.Version)
                        {
                            throw new ArgumentException("An overlapping graph resource version cannot have writes from different passes.", nameof(accesses));
                        }

                        AddDependency(left.Version < right.Version ? left.PassId : right.PassId,
                            left.Version < right.Version ? right.PassId : left.PassId,
                            passIndices,
                            dependencies);
                    }
                    else
                    {
                        var writer = left.IsWrite ? left : right;
                        var reader = left.IsWrite ? right : left;
                        AddDependency(
                            (writer.Version <= reader.Version) ? writer.PassId : reader.PassId,
                            (writer.Version <= reader.Version) ? reader.PassId : writer.PassId,
                            passIndices,
                            dependencies);
                    }
                }
            }
        }
    }
}
