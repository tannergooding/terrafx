// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

public sealed partial class World
{
    private static void EnsureMembershipReadAccess(SystemDescription description)
    {
        foreach (var access in description.Access)
        {
            if ((access.Kind == SystemResourceKind.EntityMembership)
                && (access.ResourceId == WorldSystemResources.EntityMembershipId)
                && (access.Mode == SystemAccessMode.Read)
                && (access.Range is null))
            {
                return;
            }
        }

        throw new InvalidOperationException("The system did not declare World-wide Entity-membership read access.");
    }

    private static SystemResourceAccess FindSystemAccess(
        SystemDescription description,
        SystemResourceKind kind,
        Guid resourceId)
    {
        foreach (var access in description.Access)
        {
            if ((access.Kind == kind) && (access.ResourceId == resourceId))
            {
                return access;
            }
        }

        throw new InvalidOperationException("The system context did not declare access to the selected resource.");
    }

    private static void ValidateDeclaredRange(SystemResourceAccess access, SystemPartitionRange requested)
    {
        if (access.Range is { } declared
            && ((requested.Start < declared.Start)
                || (requested.EndExclusive > declared.EndExclusive)))
        {
            throw new InvalidOperationException("The requested resource range exceeds the system's declared access.");
        }
    }

    private static void ValidateSystemQueryAccess(SystemDescription description, EntityQueryDescription query)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(query);

        foreach (var requested in query.Access)
        {
            var declared = FindSystemAccess(description, requested.Kind, requested.ResourceId);

            if ((requested.Mode == SystemAccessMode.Write)
                && (declared.Mode != SystemAccessMode.Write))
            {
                throw new InvalidOperationException("The query requests Write access beyond the system's declared grant.");
            }

            if (declared.Range is { } declaredRange)
            {
                if (requested.Range is not { } requestedRange
                    || (requestedRange.Start < declaredRange.Start)
                    || (requestedRange.EndExclusive > declaredRange.EndExclusive)
                    || (query.Range is not { } queryRange)
                    || (queryRange.Start < declaredRange.Start)
                    || (queryRange.EndExclusive > declaredRange.EndExclusive))
                {
                    throw new InvalidOperationException("The query range exceeds the system's declared access.");
                }
            }
        }
    }

    private long AdvanceCanonicalMutationRevision()
    {
        while (true)
        {
            var current = Volatile.Read(ref _canonicalMutationRevision);
            var next = checked(current + 1);

            if (Interlocked.CompareExchange(ref _canonicalMutationRevision, next, current) == current)
            {
                return next;
            }
        }
    }

    private int GetSystemInvocationSlot(int threadId)
    {
        if (_workerPool.TryGetCurrentWorkerIndex(out var workerIndex))
        {
            return workerIndex;
        }

        return (threadId == _ownerThreadId) ? _workerPool.WorkerCount : -1;
    }

    private void MarkSystemResourceWrite(WorldSystemInvocationDispatch dispatch)
    {
        dispatch.EnsureActive();
        _ = AdvanceCanonicalMutationRevision();
    }

    private long NextQueryEpoch()
    {
        while (true)
        {
            var current = Volatile.Read(ref _nextQueryEpoch);
            var next = checked(current + 1);

            if (Interlocked.CompareExchange(ref _nextQueryEpoch, next, current) == current)
            {
                return next;
            }
        }
    }

    private void ThrowIfSystemInvocationActive()
    {
        var slot = GetSystemInvocationSlot(Environment.CurrentManagedThreadId);
        var dispatch = (slot < 0) ? null : Volatile.Read(ref _activeSystemInvocations[slot]);

        if (dispatch is not null)
        {
            throw dispatch.RecordFailure(new InvalidOperationException("Independent World operations are unavailable during a system invocation."));
        }

        var isTickActive = Volatile.Read(ref _isTickActive) != 0;
        var isPublicationDeliveryActive = Volatile.Read(ref _isPublicationDeliveryActive) != 0;

        if ((isTickActive && !isPublicationDeliveryActive)
            || (Volatile.Read(ref _activeSystemInvocationCount) != 0))
        {
            throw new InvalidOperationException("Independent World operations are unavailable during an active tick.");
        }
    }

    private sealed class SystemResourcePartitionAccess<T> : IWorldResourcePartitionAccess<T>
        where T : unmanaged
    {
        private readonly WorldSystemInvocationDispatch _dispatch;
        private readonly T[] _partitions;
        private readonly int _rangeEnd;
        private readonly int _rangeStart;
        private readonly World _world;

        internal SystemResourcePartitionAccess(
            World world,
            WorldSystemInvocationDispatch dispatch,
            T[] partitions,
            SystemPartitionRange range)
        {
            _world = world;
            _dispatch = dispatch;
            _partitions = partitions;
            _rangeStart = range.Start;
            _rangeEnd = range.EndExclusive;
        }

        public T Read(int partitionIndex)
        {
            EnsureIndex(partitionIndex);
            return _partitions[partitionIndex];
        }

        public void Write(int partitionIndex, in T value)
        {
            EnsureIndex(partitionIndex);
            _world.MarkSystemResourceWrite(_dispatch);
            _partitions[partitionIndex] = value;
        }

        private void EnsureIndex(int partitionIndex)
        {
            _dispatch.EnsureActive();

            if ((partitionIndex < _rangeStart) || (partitionIndex >= _rangeEnd))
            {
                throw new InvalidOperationException("The resource view partition is outside the admitted invocation range.");
            }
        }
    }
}
