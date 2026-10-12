// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace TerraFX.Entities;

internal sealed class SystemExecutionSchedule
{
    // The packed ScheduleAccessRecord stores a Guid, three byte flags, one bool, and three int-sized values.
    private const int AccessRecordSize = 32;
    private const int MaximumAccessCount = 65_536;
    private const int MaximumDependencyCount = 65_536;
    private const int MaximumResourceCount = 4096;
    private const int MaximumSystemCount = 4096;
    private const int StageCount = (int)SimulationStage.Extract + 1;

    private readonly ImmutableArray<int> _executionOrder;
    private readonly ImmutableArray<int> _stageOffsets;
    private readonly ImmutableArray<SystemDescription> _systems;

    private SystemExecutionSchedule(
        ImmutableArray<SystemDescription> systems,
        ImmutableArray<int> executionOrder,
        ImmutableArray<int> stageOffsets)
    {
        _systems = systems;
        _executionOrder = executionOrder;
        _stageOffsets = stageOffsets;
    }

    internal int ExecutionCount => _executionOrder.Length;

    internal static SystemExecutionSchedule Compile(
        ImmutableArray<SystemDescription> descriptions,
        ImmutableArray<SystemResourceDescription> resourceDescriptions,
        WorldSchedulingLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        if (descriptions.IsDefault)
        {
            throw new ArgumentException("The system descriptions cannot be default.", nameof(descriptions));
        }

        if (resourceDescriptions.IsDefault)
        {
            throw new ArgumentException("The resource descriptions cannot be default.", nameof(resourceDescriptions));
        }

        var systemCount = descriptions.Length;
        var resourceCount = resourceDescriptions.Length;

        if (systemCount > MaximumSystemCount)
        {
            throw new SystemScheduleException(SystemScheduleFailure.SystemCountExceeded, systemCount, MaximumSystemCount);
        }

        if (resourceCount > MaximumResourceCount)
        {
            throw new SystemScheduleException(SystemScheduleFailure.ResourceCountExceeded, resourceCount, MaximumResourceCount);
        }

        for (var index = 0; index < resourceCount; index++)
        {
            ArgumentNullException.ThrowIfNull(resourceDescriptions[index], nameof(resourceDescriptions));
        }

        long accessCount = 0;
        long dependencyCount = 0;

        for (var index = 0; index < systemCount; index++)
        {
            var description = descriptions[index];

            ArgumentNullException.ThrowIfNull(description, nameof(descriptions));

            accessCount = checked(accessCount + description.Access.Length);
            dependencyCount = checked(dependencyCount + description.Before.Length + description.After.Length);
        }

        if (accessCount > MaximumAccessCount)
        {
            throw new SystemScheduleException(SystemScheduleFailure.AccessCountExceeded, accessCount, MaximumAccessCount);
        }

        if (dependencyCount > MaximumDependencyCount)
        {
            throw new SystemScheduleException(SystemScheduleFailure.DependencyCountExceeded, dependencyCount, MaximumDependencyCount);
        }

        var requiredGraphBytes = CalculateGraphBytes(systemCount, resourceCount, (int)accessCount);

        if (requiredGraphBytes > limits.MaximumGraphBytes)
        {
            throw new SystemScheduleException(
                SystemScheduleFailure.GraphBudgetExceeded,
                requiredGraphBytes,
                limits.MaximumGraphBytes);
        }

        var systems = new SystemDescription[systemCount];
        var systemsById = new int[systemCount];
        var systemsByOrder = new int[systemCount];
        var resourcesById = new int[resourceCount];
        var accessRecords = new ScheduleAccessRecord[(int)accessCount];
        var wordsPerRow = (systemCount + 63) / 64;
        var adjacency = new ulong[checked(systemCount * wordsPerRow)];
        var indegrees = new int[systemCount];
        var readyHeap = new int[systemCount];
        var executionOrder = new int[systemCount];
        var visitState = new byte[systemCount];
        var visitStack = new int[systemCount];
        var nextVisitIndex = new int[systemCount];
        var cyclePath = new Guid[systemCount + 1];
        var stageOffsets = new int[StageCount + 1];

        for (var index = 0; index < systemCount; index++)
        {
            systems[index] = descriptions[index];
            systemsById[index] = index;
            systemsByOrder[index] = index;
        }

        for (var index = 0; index < resourceCount; index++)
        {
            resourcesById[index] = index;
        }

        Array.Sort(systemsById, new SystemIdComparer(systems));

        for (var index = 1; index < systemCount; index++)
        {
            var previous = systems[systemsById[index - 1]].SystemId;
            var current = systems[systemsById[index]].SystemId;

            if (previous == current)
            {
                throw new SystemScheduleException(SystemScheduleFailure.DuplicateSystemId, current);
            }
        }

        Array.Sort(resourcesById, new ResourceIdComparer(resourceDescriptions));

        for (var index = 1; index < resourceCount; index++)
        {
            var previous = resourceDescriptions[resourcesById[index - 1]].ResourceId;
            var current = resourceDescriptions[resourcesById[index]].ResourceId;

            if (previous == current)
            {
                throw new SystemScheduleException(SystemScheduleFailure.DuplicateResourceId, Guid.Empty, Guid.Empty, current);
            }
        }

        Array.Sort(systemsByOrder, new SystemOrderComparer(systems));

        for (var index = 1; index < systemCount; index++)
        {
            var previous = systems[systemsByOrder[index - 1]].Order;
            var current = systems[systemsByOrder[index]].Order;

            if (previous == current)
            {
                throw new SystemScheduleException(
                    SystemScheduleFailure.DuplicateSystemOrder,
                    systems[systemsByOrder[index]].SystemId);
            }
        }

        var accessRecordIndex = 0;

        for (var systemIndex = 0; systemIndex < systemCount; systemIndex++)
        {
            var description = systems[systemIndex];

            foreach (var access in description.Access)
            {
                if (access.Kind == SystemResourceKind.Resource)
                {
                    var resourceIndex = FindResourceById(access.ResourceId, resourceDescriptions, resourcesById);

                    if (resourceIndex < 0)
                    {
                        throw new SystemScheduleException(
                            SystemScheduleFailure.UnknownResource,
                            description.SystemId,
                            Guid.Empty,
                            access.ResourceId);
                    }

                    var resource = resourceDescriptions[resourceIndex];

                    if (access.Range is { } range && (range.EndExclusive > resource.PartitionCount))
                    {
                        throw new SystemScheduleException(
                            SystemScheduleFailure.ResourcePartitionRangeExceeded,
                            description.SystemId,
                            Guid.Empty,
                            access.ResourceId);
                    }

                    if ((description.ThreadMode == SystemThreadMode.Parallel) && !resource.AllowParallelAccess)
                    {
                        throw new SystemScheduleException(
                            SystemScheduleFailure.ParallelResourceAccessDenied,
                            description.SystemId,
                            Guid.Empty,
                            access.ResourceId);
                    }
                }

                accessRecords[accessRecordIndex++] = new ScheduleAccessRecord(systemIndex, description.Stage, access);
            }
        }

        Array.Sort(accessRecords, new AccessRecordComparer(systems));

        for (var systemIndex = 0; systemIndex < systemCount; systemIndex++)
        {
            var description = systems[systemIndex];

            foreach (var dependency in description.Before)
            {
                var dependencyIndex = FindSystemById(dependency, systems, systemsById);

                if (dependencyIndex < 0)
                {
                    throw new SystemScheduleException(
                        SystemScheduleFailure.UnknownDependency,
                        description.SystemId,
                        dependency);
                }

                AddDependencyEdge(systemIndex, dependencyIndex, systems, adjacency, indegrees, wordsPerRow);
            }

            foreach (var dependency in description.After)
            {
                var dependencyIndex = FindSystemById(dependency, systems, systemsById);

                if (dependencyIndex < 0)
                {
                    throw new SystemScheduleException(
                        SystemScheduleFailure.UnknownDependency,
                        description.SystemId,
                        dependency);
                }

                AddDependencyEdge(dependencyIndex, systemIndex, systems, adjacency, indegrees, wordsPerRow);
            }
        }

        var requiredConflictComparisons = CountConflictComparisons(accessRecords);

        if (requiredConflictComparisons > limits.MaximumConflictComparisons)
        {
            throw new SystemScheduleException(
                SystemScheduleFailure.ConflictBudgetExceeded,
                requiredConflictComparisons,
                limits.MaximumConflictComparisons);
        }

        AddConflictEdges(accessRecords, systems, adjacency, indegrees, wordsPerRow);
        CreateExecutionOrder(
            systems,
            systemsByOrder,
            adjacency,
            indegrees,
            wordsPerRow,
            readyHeap,
            executionOrder,
            visitState,
            visitStack,
            nextVisitIndex,
            cyclePath,
            stageOffsets);

        return new SystemExecutionSchedule(
            System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(systems),
            System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(executionOrder),
            System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(stageOffsets));
    }

    internal static SystemExecutionSchedule Compile(
        ImmutableArray<SystemDescription> descriptions,
        WorldSchedulingLimits limits)
        => Compile(descriptions, [], limits);

    private static void AddConflictEdges(
        ScheduleAccessRecord[] accessRecords,
        SystemDescription[] systems,
        ulong[] adjacency,
        int[] indegrees,
        int wordsPerRow)
    {
        var start = 0;

        while (start < accessRecords.Length)
        {
            var end = start + 1;

            while ((end < accessRecords.Length) && HaveSameGroup(accessRecords[start], accessRecords[end]))
            {
                end++;
            }

            for (var writerIndex = start; writerIndex < end; writerIndex++)
            {
                var writer = accessRecords[writerIndex];

                if (writer.Mode != (byte)SystemAccessMode.Write)
                {
                    continue;
                }

                for (var otherIndex = start; otherIndex < writerIndex; otherIndex++)
                {
                    var other = accessRecords[otherIndex];

                    if ((other.Mode == (byte)SystemAccessMode.Read) && HasOverlappingRange(writer, other))
                    {
                        AddEdge(other.SystemIndex, writer.SystemIndex, systems, adjacency, indegrees, wordsPerRow);
                    }
                }

                for (var otherIndex = writerIndex + 1; otherIndex < end; otherIndex++)
                {
                    var other = accessRecords[otherIndex];

                    if (HasOverlappingRange(writer, other))
                    {
                        AddEdge(writer.SystemIndex, other.SystemIndex, systems, adjacency, indegrees, wordsPerRow);
                    }
                }
            }

            start = end;
        }
    }

    private static void AddDependencyEdge(
        int from,
        int to,
        SystemDescription[] systems,
        ulong[] adjacency,
        int[] indegrees,
        int wordsPerRow)
    {
        var fromStage = systems[from].Stage;
        var toStage = systems[to].Stage;

        if ((int)fromStage > (int)toStage)
        {
            throw new SystemScheduleException(
                SystemScheduleFailure.BackwardStageDependency,
                systems[from].SystemId,
                systems[to].SystemId);
        }

        if (fromStage == toStage)
        {
            AddEdge(from, to, systems, adjacency, indegrees, wordsPerRow);
        }
    }

    private static void AddEdge(
        int from,
        int to,
        SystemDescription[] systems,
        ulong[] adjacency,
        int[] indegrees,
        int wordsPerRow)
    {
        if (systems[from].Stage != systems[to].Stage)
        {
            throw new InvalidOperationException("A scheduler edge cannot cross a fixed stage boundary.");
        }

        var wordIndex = checked((from * wordsPerRow) + (to >> 6));
        var mask = 1UL << (to & 63);

        if ((adjacency[wordIndex] & mask) != 0)
        {
            return;
        }

        adjacency[wordIndex] |= mask;
        indegrees[to] = checked(indegrees[to] + 1);
    }

    private static long CalculateGraphBytes(int systemCount, int resourceCount, int accessCount)
    {
        checked
        {
            var wordsPerRow = (systemCount + 63L) / 64;
            var adjacencyWords = systemCount * wordsPerRow;
            var arrayHeaderBytes = 3L * IntPtr.Size;
            var arrayHeaderCount = 14; // Every owned graph, index, ordering, traversal, and result array.
            var arrayPaddingBytes = arrayHeaderCount * (IntPtr.Size - 1L);
            var auxiliaryObjectBytes = (4L * arrayHeaderBytes) + (5L * IntPtr.Size); // Four comparers and the schedule.
            var intArrayBytes = (systemCount * (7L * sizeof(int)))
                + (resourceCount * (long)sizeof(int))
                + ((StageCount + 1L) * sizeof(int));

            return (arrayHeaderCount * arrayHeaderBytes)
                + arrayPaddingBytes
                + auxiliaryObjectBytes
                + (systemCount * IntPtr.Size)
                + intArrayBytes
                + systemCount
                + ((systemCount + 1L) * 16)
                + (accessCount * AccessRecordSize)
                + (adjacencyWords * sizeof(ulong));
        }
    }

    private static long CountConflictComparisons(ScheduleAccessRecord[] accessRecords)
    {
        long total = 0;
        var start = 0;

        while (start < accessRecords.Length)
        {
            var end = start + 1;

            while ((end < accessRecords.Length) && HaveSameGroup(accessRecords[start], accessRecords[end]))
            {
                end++;
            }

            long writers = 0;
            long readers = 0;

            for (var index = start; index < end; index++)
            {
                if (accessRecords[index].Mode == (byte)SystemAccessMode.Write)
                {
                    writers++;
                }
                else
                {
                    readers++;
                }
            }

            total = checked(total + checked((writers * (writers - 1) / 2) + (writers * readers)));
            start = end;
        }

        return total;
    }

    private static void CreateExecutionOrder(
        SystemDescription[] systems,
        int[] systemsByOrder,
        ulong[] adjacency,
        int[] indegrees,
        int wordsPerRow,
        int[] readyHeap,
        int[] executionOrder,
        byte[] visitState,
        int[] visitStack,
        int[] nextVisitIndex,
        Guid[] cyclePath,
        int[] stageOffsets)
    {
        var executionIndex = 0;

        for (var stageIndex = 0; stageIndex < StageCount; stageIndex++)
        {
            stageOffsets[stageIndex] = executionIndex;

            var stageSystemCount = 0;
            var readyCount = 0;

            foreach (var systemIndex in systemsByOrder)
            {
                if ((int)systems[systemIndex].Stage != stageIndex)
                {
                    continue;
                }

                stageSystemCount++;

                if (indegrees[systemIndex] == 0)
                {
                    HeapAdd(readyHeap, ref readyCount, systemIndex, systems);
                }
            }

            var stageExecutionStart = executionIndex;

            while (readyCount != 0)
            {
                var current = HeapRemoveMin(readyHeap, ref readyCount, systems);
                executionOrder[executionIndex++] = current;

                for (var target = 0; target < systems.Length; target++)
                {
                    if (!HasEdge(current, target, adjacency, wordsPerRow))
                    {
                        continue;
                    }

                    indegrees[target]--;

                    if (indegrees[target] == 0)
                    {
                        HeapAdd(readyHeap, ref readyCount, target, systems);
                    }
                }
            }

            if ((executionIndex - stageExecutionStart) != stageSystemCount)
            {
                ThrowCycle(
                    stageIndex,
                    systems,
                    systemsByOrder,
                    adjacency,
                    wordsPerRow,
                    visitState,
                    visitStack,
                    nextVisitIndex,
                    cyclePath);
            }

            stageOffsets[stageIndex + 1] = executionIndex;
        }
    }

    private static int FindResourceById(
        Guid resourceId,
        ImmutableArray<SystemResourceDescription> resources,
        int[] resourcesById)
    {
        var minimum = 0;
        var maximum = resourcesById.Length - 1;

        while (minimum <= maximum)
        {
            var midpoint = minimum + ((maximum - minimum) / 2);
            var resourceIndex = resourcesById[midpoint];
            var comparison = resources[resourceIndex].ResourceId.CompareTo(resourceId);

            if (comparison == 0)
            {
                return resourceIndex;
            }

            if (comparison < 0)
            {
                minimum = midpoint + 1;
            }
            else
            {
                maximum = midpoint - 1;
            }
        }

        return -1;
    }

    private static int FindSystemById(Guid systemId, SystemDescription[] systems, int[] systemsById)
    {
        var minimum = 0;
        var maximum = systemsById.Length - 1;

        while (minimum <= maximum)
        {
            var midpoint = minimum + ((maximum - minimum) / 2);
            var systemIndex = systemsById[midpoint];
            var comparison = systems[systemIndex].SystemId.CompareTo(systemId);

            if (comparison == 0)
            {
                return systemIndex;
            }

            if (comparison < 0)
            {
                minimum = midpoint + 1;
            }
            else
            {
                maximum = midpoint - 1;
            }
        }

        return -1;
    }

    private static int GetSystemComparison(int left, int right, SystemDescription[] systems)
    {
        var orderComparison = systems[left].Order.CompareTo(systems[right].Order);

        return (orderComparison != 0)
            ? orderComparison
            : systems[left].SystemId.CompareTo(systems[right].SystemId);
    }

    private static bool HasEdge(int from, int to, ulong[] adjacency, int wordsPerRow)
    {
        var wordIndex = (from * wordsPerRow) + (to >> 6);
        var mask = 1UL << (to & 63);

        return (adjacency[wordIndex] & mask) != 0;
    }

    private static bool HasOverlappingRange(ScheduleAccessRecord left, ScheduleAccessRecord right)
        => !left.HasRange
        || !right.HasRange
        || ((left.RangeStart < right.RangeEnd) && (right.RangeStart < left.RangeEnd));

    private static bool HaveSameGroup(ScheduleAccessRecord left, ScheduleAccessRecord right)
        => (left.Stage == right.Stage)
        && (left.Kind == right.Kind)
        && (left.ResourceId == right.ResourceId);

    private static void HeapAdd(int[] heap, ref int count, int systemIndex, SystemDescription[] systems)
    {
        var index = count++;

        while (index > 0)
        {
            var parent = (index - 1) >> 1;

            if (GetSystemComparison(heap[parent], systemIndex, systems) <= 0)
            {
                break;
            }

            heap[index] = heap[parent];
            index = parent;
        }

        heap[index] = systemIndex;
    }

    private static int HeapRemoveMin(int[] heap, ref int count, SystemDescription[] systems)
    {
        var result = heap[0];
        var replacement = heap[--count];

        if (count != 0)
        {
            var index = 0;

            while (true)
            {
                var left = (index * 2) + 1;

                if (left >= count)
                {
                    break;
                }

                var right = left + 1;
                var child = (right < count) && (GetSystemComparison(heap[right], heap[left], systems) < 0)
                    ? right
                    : left;

                if (GetSystemComparison(replacement, heap[child], systems) <= 0)
                {
                    break;
                }

                heap[index] = heap[child];
                index = child;
            }

            heap[index] = replacement;
        }

        return result;
    }

    private static void ThrowCycle(
        int stageIndex,
        SystemDescription[] systems,
        int[] systemsByOrder,
        ulong[] adjacency,
        int wordsPerRow,
        byte[] visitState,
        int[] visitStack,
        int[] nextVisitIndex,
        Guid[] cyclePath)
    {
        for (var orderIndex = 0; orderIndex < systemsByOrder.Length; orderIndex++)
        {
            var start = systemsByOrder[orderIndex];

            if (((int)systems[start].Stage != stageIndex) || (visitState[start] != 0))
            {
                continue;
            }

            var depth = 0;
            visitStack[depth] = start;
            nextVisitIndex[depth] = 0;
            visitState[start] = 1;

            while (depth >= 0)
            {
                var current = visitStack[depth];
                var descended = false;

                while (nextVisitIndex[depth] < systemsByOrder.Length)
                {
                    var target = systemsByOrder[nextVisitIndex[depth]++];

                    if (((int)systems[target].Stage != stageIndex) || !HasEdge(current, target, adjacency, wordsPerRow))
                    {
                        continue;
                    }

                    if (visitState[target] == 1)
                    {
                        var cycleStart = 0;

                        while (visitStack[cycleStart] != target)
                        {
                            cycleStart++;
                        }

                        var cycleLength = depth - cycleStart + 1;

                        for (var index = 0; index < cycleLength; index++)
                        {
                            cyclePath[index] = systems[visitStack[cycleStart + index]].SystemId;
                        }

                        cyclePath[cycleLength] = systems[target].SystemId;

                        throw new SystemScheduleException(
                            SystemScheduleFailure.DependencyCycle,
                            stageIndex,
                            cyclePath,
                            cycleLength + 1);
                    }

                    if (visitState[target] == 0)
                    {
                        visitState[target] = 1;
                        depth++;
                        visitStack[depth] = target;
                        nextVisitIndex[depth] = 0;
                        descended = true;
                        break;
                    }
                }

                if (!descended)
                {
                    visitState[current] = 2;
                    depth--;
                }
            }
        }

        throw new InvalidOperationException("The scheduler could not identify its dependency cycle.");
    }

    private static void ValidateStage(SimulationStage stage)
    {
        ArgumentOutOfRangeException.ThrowIfNegative((int)stage);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((int)stage, (int)SimulationStage.Extract);
    }

    internal int GetStageEnd(SimulationStage stage)
    {
        ValidateStage(stage);
        return _stageOffsets[(int)stage + 1];
    }

    internal int GetStageStart(SimulationStage stage)
    {
        ValidateStage(stage);
        return _stageOffsets[(int)stage];
    }

    internal SystemDescription GetSystemAtExecutionIndex(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _executionOrder.Length);

        return _systems[_executionOrder[index]];
    }

    private sealed class AccessRecordComparer : IComparer<ScheduleAccessRecord>
    {
        private readonly SystemDescription[] _systems;

        internal AccessRecordComparer(SystemDescription[] systems)
        {
            _systems = systems;
        }

        public int Compare(ScheduleAccessRecord left, ScheduleAccessRecord right)
        {
            var result = left.Stage.CompareTo(right.Stage);

            if (result == 0)
            {
                result = left.Kind.CompareTo(right.Kind);
            }

            if (result == 0)
            {
                result = left.ResourceId.CompareTo(right.ResourceId);
            }

            if (result == 0)
            {
                result = GetSystemComparison(left.SystemIndex, right.SystemIndex, _systems);
            }

            return result;
        }
    }

    private sealed class ResourceIdComparer : IComparer<int>
    {
        private readonly ImmutableArray<SystemResourceDescription> _resources;

        internal ResourceIdComparer(ImmutableArray<SystemResourceDescription> resources)
        {
            _resources = resources;
        }

        public int Compare(int left, int right)
            => _resources[left].ResourceId.CompareTo(_resources[right].ResourceId);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
    private readonly struct ScheduleAccessRecord
    {
        internal ScheduleAccessRecord(int systemIndex, SimulationStage stage, SystemResourceAccess access)
        {
            ResourceId = access.ResourceId;
            SystemIndex = systemIndex;
            Stage = checked((byte)stage);
            Kind = checked((byte)access.Kind);
            Mode = checked((byte)access.Mode);

            var range = access.Range;
            HasRange = range.HasValue;
            RangeStart = range.GetValueOrDefault().Start;
            RangeEnd = range.GetValueOrDefault().EndExclusive;
        }

        internal bool HasRange { get; }

        internal byte Kind { get; }

        internal byte Mode { get; }

        internal int RangeEnd { get; }

        internal int RangeStart { get; }

        internal Guid ResourceId { get; }

        internal byte Stage { get; }

        internal int SystemIndex { get; }
    }

    private sealed class SystemIdComparer : IComparer<int>
    {
        private readonly SystemDescription[] _systems;

        internal SystemIdComparer(SystemDescription[] systems)
        {
            _systems = systems;
        }

        public int Compare(int left, int right)
            => _systems[left].SystemId.CompareTo(_systems[right].SystemId);
    }

    private sealed class SystemOrderComparer : IComparer<int>
    {
        private readonly SystemDescription[] _systems;

        internal SystemOrderComparer(SystemDescription[] systems)
        {
            _systems = systems;
        }

        public int Compare(int left, int right)
            => GetSystemComparison(left, right, _systems);
    }
}
