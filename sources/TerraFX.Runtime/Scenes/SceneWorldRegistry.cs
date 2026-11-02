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
    private int _activeOccurrenceCount;
    private readonly Lock _instanceGate = new();
    private int _isDisposed;
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
        var activeInstanceObjectBytes = ArrayBytes(maximumActiveSceneInstances, 256);
        var reservedSlotBytes = ArrayBytes(maximumActiveSceneInstances, sizeof(bool));
        var pendingRequestBytes = ArrayBytes(maximumPendingOwnerRequests, IntPtr.Size);
        var pendingRequestObjectBytes = ArrayBytes(maximumPendingOwnerRequests, 256);
        return checked(
            ObjectStorageByteReservation
            + activeInstanceBytes
            + activeInstanceObjectBytes
            + reservedSlotBytes
            + pendingRequestBytes
            + pendingRequestObjectBytes);
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
            }

            instance?.RevokeFromWorld();
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

            _reservedInstanceSlots[reservation.Slot] = false;
            _reservedInstanceCount--;
            _reservedOccurrenceCount -= reservation.OccurrenceCount;
            _activeInstances[reservation.Slot] = instance;
            _activeInstanceCount++;
            _activeOccurrenceCount = checked(_activeOccurrenceCount + reservation.OccurrenceCount);
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

            if (_pendingRequestCount == _pendingRequests.Length)
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
            foreach (var candidate in _activeInstances)
            {
                if (candidate is null)
                {
                    continue;
                }

                var firstEntityIdentityOrdinal = candidate.FirstEntityIdentityOrdinal;

                if ((candidate.EntityCount > 0)
                    && (identityOrdinal >= firstEntityIdentityOrdinal)
                    && ((identityOrdinal - firstEntityIdentityOrdinal) < (uint)candidate.EntityCount))
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
}
