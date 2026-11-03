// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;
using TerraFX.Entities;
using TerraFX.Numerics;

namespace TerraFX.Scenes;

internal sealed class SceneWorldRegistry : IWorldSceneParticipant
{
    private const long ObjectStorageByteReservation = 512;

    private int _activeInstanceCount;
    private readonly SceneInstance?[] _activeInstances;
    private readonly SceneInstance?[] _activeInstancesByFirstEntityOrdinal;
    private int _activeOccurrenceCount;
    private int _activeOrderedInstanceCount;
    private readonly Lock _instanceGate = new();
    private int _isDisposed;
    private bool _isPersistenceCaptureActive;
    private readonly int _maximumActiveSceneInstances;
    private readonly int _maximumPendingOwnerRequests;
    private readonly int _maximumSceneOccurrences;
    private int _pendingRequestCount;
    private int _pendingRequestHead;
    private readonly SceneOwnerRequest?[] _pendingRequests;
    private readonly Lock _requestGate = new();
    private int _reservedInstanceCount;
    private readonly bool[] _reservedInstanceSlots;
    private int _reservedOccurrenceCount;
    private readonly World _world;

    internal SceneWorldRegistry(World world, WorldOptions options)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(options);
        _world = world;
        _maximumActiveSceneInstances = options.MaximumActiveSceneInstances;
        _maximumPendingOwnerRequests = options.MaximumPendingOwnerRequests;
        _maximumSceneOccurrences = options.MaximumSceneOccurrences;
        _pendingRequests = new SceneOwnerRequest[options.MaximumPendingOwnerRequests];
        _activeInstances = new SceneInstance[options.MaximumActiveSceneInstances];
        _activeInstancesByFirstEntityOrdinal = new SceneInstance[options.MaximumActiveSceneInstances];
        _reservedInstanceSlots = new bool[options.MaximumActiveSceneInstances];
    }

    public Type HierarchyLocalTransformComponentType => typeof(SceneComponentDefinitions.LocalTransformComponent);

    public Type HierarchyRootPositionComponentType => typeof(SceneComponentDefinitions.RootPositionComponent);

    public long OwnedByteReservation => GetStorageByteReservation(
        _maximumActiveSceneInstances,
        _maximumPendingOwnerRequests);

    internal static long GetWorldStorageByteReservation(WorldOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return GetStorageByteReservation(
            options.MaximumActiveSceneInstances,
            options.MaximumPendingOwnerRequests);
    }

    private static long ArrayBytes(int count, int elementSize)
    {
        var payloadBytes = checked((long)count * elementSize);
        return checked(64L + ((payloadBytes + 7L) & ~7L));
    }

    private static long GetStorageByteReservation(
        int maximumActiveSceneInstances,
        int maximumPendingOwnerRequests)
    {
        var activeInstanceBytes = ArrayBytes(maximumActiveSceneInstances, IntPtr.Size);
        var activeInstanceOrderBytes = ArrayBytes(maximumActiveSceneInstances, IntPtr.Size);
        var activeInstanceObjectBytes = ArrayBytes(maximumActiveSceneInstances, 256);
        var reservedSlotBytes = ArrayBytes(maximumActiveSceneInstances, sizeof(bool));
        var pendingRequestBytes = ArrayBytes(maximumPendingOwnerRequests, IntPtr.Size);
        var pendingRequestObjectBytes = ArrayBytes(maximumPendingOwnerRequests, 256);
        return checked(
            ObjectStorageByteReservation
            + activeInstanceBytes
            + activeInstanceOrderBytes
            + activeInstanceObjectBytes
            + reservedSlotBytes
            + pendingRequestBytes
            + pendingRequestObjectBytes);
    }

    public void CopySceneAssetIds(Span<uint> destination)
    {
        lock (_instanceGate)
        {
            var assetIdCount = 0;

            for (var index = 0; index < _activeInstances.Length; index++)
            {
                if (_activeInstances[index] is { } instance)
                {
                    assetIdCount = checked(assetIdCount + instance.GetPersistenceAssetIdCount());
                }
            }

            if (destination.Length != assetIdCount)
            {
                throw new ArgumentException("The Scene AssetId destination must match the exact active occurrence count.", nameof(destination));
            }

            var destinationOffset = 0;

            for (var index = 0; index < _activeInstances.Length; index++)
            {
                if (_activeInstances[index] is { } instance)
                {
                    var occurrenceCount = instance.GetPersistenceAssetIdCount();
                    instance.CopyPersistenceAssetIds(destination.Slice(destinationOffset, occurrenceCount));
                    destinationOffset = checked(destinationOffset + occurrenceCount);
                }
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        while (true)
        {
            SceneOwnerRequest? request;

            lock (_requestGate)
            {
                if (_pendingRequestCount == 0)
                {
                    break;
                }

                request = DequeueRequest();
            }

            request?.CancelFromRegistryDisposal();
        }

        for (var index = 0; index < _activeInstances.Length; index++)
        {
            SceneInstance? instance;

            lock (_instanceGate)
            {
                instance = _activeInstances[index];
                _activeInstances[index] = null;

                if (instance is { EntityCount: > 0 })
                {
                    RemoveActiveInstanceByOrdinal(instance);
                }
            }

            instance?.RevokeFromWorld();
        }
    }

    public void EndPersistenceCapture()
    {
        lock (_requestGate)
        {
            _isPersistenceCaptureActive = false;
        }
    }

    public int GetSceneAssetIdCount()
    {
        lock (_instanceGate)
        {
            var count = 0;

            for (var index = 0; index < _activeInstances.Length; index++)
            {
                if (_activeInstances[index] is { } instance)
                {
                    count = checked(count + instance.GetPersistenceAssetIdCount());
                }
            }

            return count;
        }
    }

    public ImmutableArray<WorldHierarchyDetachUpdate> PrepareHierarchyDetachment(
        int ancestorNodeCount,
        ReadOnlyMemory<WorldHierarchyNodeSnapshot> nodes)
        => SceneHierarchyTransform.PrepareHierarchyDetachment(
            _world,
            _world.GetCurrentSceneHierarchyFrame(this),
            ancestorNodeCount,
            nodes);

    public void PumpControlBoundary()
    {
        while (true)
        {
            SceneOwnerRequest? request;

            lock (_requestGate)
            {
                if (_pendingRequestCount == 0)
                {
                    return;
                }

                request = DequeueRequest();
            }

            request?.Execute(_world, this);
        }
    }

    public (SpatialPosition3 Position, Quaternion Rotation) ResolveEntityPose(
        World world,
        SpatialFrame3 frame,
        Entity entity)
    {
        EnsureBoundTo(world);
        return SceneHierarchyTransform.ResolveCameraPose(world, frame, entity);
    }

    public (SpatialPosition3 Position, Matrix4x4 LinearTransform, Quaternion Rotation) ResolveEntityTransform(
        World world,
        SpatialFrame3 frame,
        Entity entity)
    {
        EnsureBoundTo(world);
        return SceneHierarchyTransform.ResolveEntityTransform(world, frame, entity);
    }

    public bool TryBeginPersistenceCapture()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

        lock (_requestGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

            if (_isPersistenceCaptureActive || (_pendingRequestCount != 0))
            {
                return false;
            }

            _isPersistenceCaptureActive = true;
            return true;
        }
    }

    public bool TryGetCameraData(World world, Entity entity, out WorldCameraData camera)
    {
        EnsureBoundTo(world);

        if (!world.TryGet<SceneComponentDefinitions.CameraComponent>(entity, out var component))
        {
            camera = default;
            return false;
        }

        camera = new WorldCameraData(
            component.ProjectionKind,
            component.VerticalFieldOfView,
            component.OrthographicHeight,
            component.Near,
            component.FarMode,
            component.Far.HasValue ? component.Far.Value : null,
            component.AspectRatioOverride.HasValue ? component.AspectRatioOverride.Value : null,
            component.Viewport,
            component.ClearFlags,
            component.ClearColor,
            component.LayerMask,
            component.Order);
        return true;
    }

    public bool TryGetSceneInstanceId(World world, Entity entity, out Guid sceneInstanceId)
    {
        if (TryGetInstanceForEntity(world, entity, out var instance) && (instance is not null))
        {
            sceneInstanceId = instance.SceneInstanceId;
            return true;
        }

        sceneInstanceId = Guid.Empty;
        return false;
    }

    internal void CommitReservation(SceneInstanceReservation reservation, SceneInstance instance)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(instance);

        lock (_instanceGate)
        {
            if (!ReferenceEquals(reservation.Registry, this)
                || !_reservedInstanceSlots[reservation.Slot]
                || (_activeInstances[reservation.Slot] is not null)
                || (instance.OccurrenceCount != reservation.OccurrenceCount)
                || (instance.SceneInstanceId == Guid.Empty))
            {
                throw new InvalidOperationException("The Scene instance reservation no longer matches its prepared publication.");
            }

            var orderedInsertionIndex = (instance.EntityCount == 0)
                ? -1
                : FindActiveInstanceInsertionIndex(instance);

            _reservedInstanceSlots[reservation.Slot] = false;
            _reservedInstanceCount--;
            _reservedOccurrenceCount -= reservation.OccurrenceCount;
            _activeInstances[reservation.Slot] = instance;
            _activeInstanceCount++;
            _activeOccurrenceCount = checked(_activeOccurrenceCount + reservation.OccurrenceCount);

            if (orderedInsertionIndex >= 0)
            {
                InsertActiveInstanceByOrdinal(instance, orderedInsertionIndex);
            }
        }
    }

    internal bool ContainsInstance(SceneInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        lock (_instanceGate)
        {
            for (var index = 0; index < _activeInstances.Length; index++)
            {
                if (ReferenceEquals(_activeInstances[index], instance))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal void EnsureBoundTo(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

        if (!ReferenceEquals(world, _world))
        {
            throw new ArgumentException("The Scene registry belongs to another exact World binding.", nameof(world));
        }
    }

    internal void ReleaseReservation(SceneInstanceReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        lock (_instanceGate)
        {
            if (!ReferenceEquals(reservation.Registry, this)
                || !_reservedInstanceSlots[reservation.Slot])
            {
                return;
            }

            _reservedInstanceSlots[reservation.Slot] = false;
            _reservedInstanceCount--;
            _reservedOccurrenceCount -= reservation.OccurrenceCount;
        }
    }

    internal void RemoveInstance(SceneInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        lock (_instanceGate)
        {
            for (var index = 0; index < _activeInstances.Length; index++)
            {
                if (!ReferenceEquals(_activeInstances[index], instance))
                {
                    continue;
                }

                if (instance.EntityCount != 0)
                {
                    RemoveActiveInstanceByOrdinal(instance);
                }

                _activeInstances[index] = null;
                _activeInstanceCount--;
                _activeOccurrenceCount -= instance.OccurrenceCount;
                return;
            }
        }
    }

    internal bool TryEnqueue(SceneOwnerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

        lock (_requestGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

            if (_isPersistenceCaptureActive || (_pendingRequestCount == _pendingRequests.Length))
            {
                return false;
            }

            request.Admit();
            var tail = (_pendingRequestHead + _pendingRequestCount) % _pendingRequests.Length;
            _pendingRequests[tail] = request;
            _pendingRequestCount++;
            return true;
        }
    }

    internal bool TryGetInstance(Guid sceneInstanceId, out SceneInstance? instance)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sceneInstanceId, Guid.Empty);

        lock (_instanceGate)
        {
            for (var index = 0; index < _activeInstances.Length; index++)
            {
                var candidate = _activeInstances[index];

                if (candidate?.SceneInstanceId == sceneInstanceId)
                {
                    instance = candidate;
                    return true;
                }
            }
        }

        instance = null;
        return false;
    }

    internal bool TryGetInstanceForEntity(World world, Entity entity, out SceneInstance? instance)
    {
        EnsureBoundTo(world);

        if (!_world.TryGetLiveEntityIdentityOrdinal(entity, out var identityOrdinal))
        {
            instance = null;
            return false;
        }

        lock (_instanceGate)
        {
            var low = 0;
            var high = _activeOrderedInstanceCount;

            while (low < high)
            {
                var middle = low + ((high - low) / 2);
                var candidate = _activeInstancesByFirstEntityOrdinal[middle]
                    ?? throw new InvalidOperationException("The ordered Scene instance index contains an empty entry.");

                if (candidate.FirstEntityIdentityOrdinal <= identityOrdinal)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            if (low > 0)
            {
                var candidate = _activeInstancesByFirstEntityOrdinal[low - 1]
                    ?? throw new InvalidOperationException("The ordered Scene instance index contains an empty entry.");
                var entityOrdinalOffset = identityOrdinal - candidate.FirstEntityIdentityOrdinal;

                if (entityOrdinalOffset < (uint)candidate.EntityCount)
                {
                    instance = candidate;
                    return true;
                }
            }
        }

        instance = null;
        return false;
    }

    internal bool TryReserveInstance(int occurrenceCount, out SceneInstanceReservation? reservation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(occurrenceCount);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

        lock (_instanceGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

            if ((_activeInstanceCount + _reservedInstanceCount) >= _maximumActiveSceneInstances
                || occurrenceCount > (_maximumSceneOccurrences - _activeOccurrenceCount - _reservedOccurrenceCount))
            {
                reservation = null;
                return false;
            }

            for (var index = 0; index < _activeInstances.Length; index++)
            {
                if ((_activeInstances[index] is not null) || _reservedInstanceSlots[index])
                {
                    continue;
                }

                _reservedInstanceSlots[index] = true;
                _reservedInstanceCount++;
                _reservedOccurrenceCount = checked(_reservedOccurrenceCount + occurrenceCount);
                reservation = new SceneInstanceReservation(this, index, occurrenceCount);
                return true;
            }
        }

        throw new InvalidOperationException("The Scene registry has no free slot despite its active-instance capacity check.");
    }

    private SceneOwnerRequest? DequeueRequest()
    {
        var request = _pendingRequests[_pendingRequestHead];
        _pendingRequests[_pendingRequestHead] = null;
        _pendingRequestHead = (_pendingRequestHead + 1) % _pendingRequests.Length;
        _pendingRequestCount--;
        return request;
    }

    private int FindActiveInstanceInsertionIndex(SceneInstance instance)
    {
        var firstEntityIdentityOrdinal = instance.FirstEntityIdentityOrdinal;
        var low = 0;
        var high = _activeOrderedInstanceCount;

        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            var candidate = _activeInstancesByFirstEntityOrdinal[middle]
                ?? throw new InvalidOperationException("The ordered Scene instance index contains an empty entry.");

            if (candidate.FirstEntityIdentityOrdinal < firstEntityIdentityOrdinal)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        if ((low < _activeOrderedInstanceCount)
            && (_activeInstancesByFirstEntityOrdinal[low]?.FirstEntityIdentityOrdinal == firstEntityIdentityOrdinal))
        {
            throw new InvalidOperationException("Two active Scene instances share an Entity identity ordinal range.");
        }

        return low;
    }

    private void InsertActiveInstanceByOrdinal(SceneInstance instance, int index)
    {
        Array.Copy(
            _activeInstancesByFirstEntityOrdinal,
            index,
            _activeInstancesByFirstEntityOrdinal,
            index + 1,
            _activeOrderedInstanceCount - index);
        _activeInstancesByFirstEntityOrdinal[index] = instance;
        _activeOrderedInstanceCount++;
    }

    private void RemoveActiveInstanceByOrdinal(SceneInstance instance)
    {
        var firstEntityIdentityOrdinal = instance.FirstEntityIdentityOrdinal;
        var low = 0;
        var high = _activeOrderedInstanceCount;

        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            var candidate = _activeInstancesByFirstEntityOrdinal[middle]
                ?? throw new InvalidOperationException("The ordered Scene instance index contains an empty entry.");

            if (candidate.FirstEntityIdentityOrdinal < firstEntityIdentityOrdinal)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        if ((low >= _activeOrderedInstanceCount)
            || !ReferenceEquals(_activeInstancesByFirstEntityOrdinal[low], instance))
        {
            throw new InvalidOperationException("The ordered Scene instance index lost an active instance.");
        }

        Array.Copy(
            _activeInstancesByFirstEntityOrdinal,
            low + 1,
            _activeInstancesByFirstEntityOrdinal,
            low,
            _activeOrderedInstanceCount - low - 1);
        _activeInstancesByFirstEntityOrdinal[--_activeOrderedInstanceCount] = null;
    }
}
