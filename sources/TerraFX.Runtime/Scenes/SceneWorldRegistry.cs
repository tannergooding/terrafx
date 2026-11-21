// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading;
using TerraFX.Entities;
using TerraFX.Numerics;

namespace TerraFX.Scenes;

internal sealed class SceneWorldRegistry : IWorldSceneParticipant
{
    private const int DisposeStateComplete = 2;
    private const int DisposeStateDraining = 1;
    private const int DisposeStateFailed = 3;
    private const int DisposeStateOpen = 0;

    private const long ObjectStorageByteReservation = 512;

    private int _activeInstanceCount;
    private readonly SceneInstance?[] _activeInstancesByFirstEntityOrdinal;
    private int _activeOccurrenceCount;
    private int _activeOrderedInstanceCount;
    private int _additionalCleanupFailureCount;
    private int _cleanupInvokerThreadId;
    private int _cleanupPendingInstanceCount;
    private int _cleanupPendingOccurrenceCount;
    private ExceptionDispatchInfo? _disposeFailure;
    private int _disposeState;
    private readonly Lock _instanceGate = new();
    private bool _isPersistenceCaptureActive;
    private readonly int _maximumActiveSceneInstances;
    private readonly int _maximumPendingOwnerRequests;
    private readonly int _maximumSceneOccurrences;
    private readonly SceneInstance?[] _ownedInstances;
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
        _ownedInstances = new SceneInstance[options.MaximumActiveSceneInstances];
        _activeInstancesByFirstEntityOrdinal = new SceneInstance[options.MaximumActiveSceneInstances];
        _reservedInstanceSlots = new bool[options.MaximumActiveSceneInstances];
    }

    public ExceptionDispatchInfo? CleanupFailure => Volatile.Read(ref _disposeFailure);

    public bool HasRetainedCleanup => (Volatile.Read(ref _cleanupPendingInstanceCount) != 0)
        || (CleanupFailure is not null);

    public Type HierarchyLocalTransformComponentType => typeof(SceneComponentDefinitions.LocalTransformComponent);

    public Type HierarchyRootPositionComponentType => typeof(SceneComponentDefinitions.RootPositionComponent);

    public bool IsCleanupInvocationActive => Volatile.Read(ref _cleanupInvokerThreadId) != 0;

    public long OwnedByteReservation => GetStorageByteReservation(
        _maximumActiveSceneInstances,
        _maximumPendingOwnerRequests);

    internal int AdditionalCleanupFailureCount => _additionalCleanupFailureCount;

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

            for (var index = 0; index < _ownedInstances.Length; index++)
            {
                if (!_reservedInstanceSlots[index] && (_ownedInstances[index] is { IsActive: true } instance))
                {
                    assetIdCount = checked(assetIdCount + instance.GetPersistenceAssetIdCount());
                }
            }

            if (destination.Length != assetIdCount)
            {
                throw new ArgumentException("The Scene AssetId destination must match the exact active occurrence count.", nameof(destination));
            }

            var destinationOffset = 0;

            for (var index = 0; index < _ownedInstances.Length; index++)
            {
                if (!_reservedInstanceSlots[index] && (_ownedInstances[index] is { IsActive: true } instance))
                {
                    var occurrenceCount = instance.GetPersistenceAssetIdCount();
                    instance.CopyPersistenceAssetIds(destination.Slice(destinationOffset, occurrenceCount));
                    destinationOffset = checked(destinationOffset + occurrenceCount);
                }
            }
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "The fixed request and instance slots retain failures while every independent selected sibling is attempted.")]
    public void Dispose()
    {
        var threadId = Environment.CurrentManagedThreadId;
        var spinner = new SpinWait();

        while (true)
        {
            if (Volatile.Read(ref _cleanupInvokerThreadId) == threadId)
            {
                throw new InvalidOperationException("Scene cleanup cannot recursively dispose its registry.");
            }

            var state = Volatile.Read(ref _disposeState);

            if (state == DisposeStateComplete)
            {
                return;
            }

            if (state == DisposeStateFailed)
            {
                (CleanupFailure
                    ?? throw new InvalidOperationException("The failed Scene registry cleanup lost its original failure."))
                    .Throw();
                return;
            }

            if (state == DisposeStateDraining)
            {
                spinner.SpinOnce();
                continue;
            }

            _world.ValidateSceneCleanupThread();

            lock (_requestGate)
            {
                if (_disposeState != DisposeStateOpen)
                {
                    continue;
                }

                if (Interlocked.CompareExchange(ref _cleanupInvokerThreadId, threadId, 0) != 0)
                {
                    throw new InvalidOperationException("The Scene registry already has a cleanup invoker.");
                }

                Volatile.Write(ref _disposeState, DisposeStateDraining);
                break;
            }
        }

        ExceptionDispatchInfo? firstFailure = null;
        var additionalFailures = 0;

        for (var index = 0; index < _ownedInstances.Length; index++)
        {
            try
            {
                lock (_instanceGate)
                {
                    DeactivateOwnedInstance(index);
                }
            }
            catch (Exception exception)
            {
                RecordFailure(ExceptionDispatchInfo.Capture(exception));
            }
        }

        int requestCount;
        int requestHead;

        lock (_requestGate)
        {
            requestCount = _pendingRequestCount;
            requestHead = _pendingRequestHead;
        }

        var retainedRequestCount = 0;

        for (var offset = 0; offset < requestCount; offset++)
        {
            var slot = (requestHead + offset) % _pendingRequests.Length;
            var request = _pendingRequests[slot];
            var returned = false;

            try
            {
                request?.CancelFromRegistryDisposal();

                if (request?.RetainedCleanupFailure is { } exception)
                {
                    RecordFailure(request.GetCleanupDispatch(exception));
                }
                else
                {
                    returned = true;
                }
            }
            catch (Exception exception)
            {
                RecordFailure(request?.GetCleanupDispatch(exception) ?? ExceptionDispatchInfo.Capture(exception));
            }

            if (returned)
            {
                _pendingRequests[slot] = null;
            }
            else
            {
                retainedRequestCount++;
            }
        }

        lock (_requestGate)
        {
            _pendingRequestHead = (requestHead + requestCount) % _pendingRequests.Length;
            _pendingRequestCount = retainedRequestCount;
        }

        for (var index = 0; index < _ownedInstances.Length; index++)
        {
            SceneInstance? instance;

            lock (_instanceGate)
            {
                instance = _ownedInstances[index];
            }

            try
            {
                instance?.RevokeFromWorld();
            }
            catch (Exception exception)
            {
                RecordFailure(instance?.CleanupFailure ?? ExceptionDispatchInfo.Capture(exception));
            }

            if (instance is not null)
            {
                FinalizeInstanceCleanup(instance);
            }
        }

        _additionalCleanupFailureCount = additionalFailures;
        Volatile.Write(ref _disposeFailure, firstFailure);
        Volatile.Write(ref _cleanupInvokerThreadId, 0);
        Volatile.Write(ref _disposeState, firstFailure is null ? DisposeStateComplete : DisposeStateFailed);
        firstFailure?.Throw();

        void RecordFailure(ExceptionDispatchInfo failure)
        {
            if (firstFailure is null)
            {
                firstFailure = failure;
            }
            else
            {
                additionalFailures = checked(additionalFailures + 1);
            }
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

            for (var index = 0; index < _ownedInstances.Length; index++)
            {
                if (!_reservedInstanceSlots[index] && (_ownedInstances[index] is { IsActive: true } instance))
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != DisposeStateOpen, this);

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

            if (request is SceneUnloadRequest unload)
            {
                unload.EndExecution(this);
            }
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != DisposeStateOpen, this);

        lock (_requestGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != DisposeStateOpen, this);

            if (_isPersistenceCaptureActive
                || (_pendingRequestCount != 0)
                || IsCleanupInvocationActive
                || HasRetainedCleanup)
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

    internal void AdoptReservation(SceneInstanceReservation reservation, SceneInstance instance)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(instance);

        lock (_instanceGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != DisposeStateOpen, this);

            if (!ReferenceEquals(reservation.Registry, this)
                || !_reservedInstanceSlots[reservation.Slot]
                || (_ownedInstances[reservation.Slot] is not null)
                || !instance.IsBoundTo(_world, this)
                || (instance.OccurrenceCount != reservation.OccurrenceCount))
            {
                throw new InvalidOperationException("The Scene reservation cannot adopt this exact candidate.");
            }

            _ownedInstances[reservation.Slot] = instance;
        }
    }

    internal void BeginUnloadCleanup(
        SceneInstance instance,
        World.WorldSceneControlAppend controlAppend,
        SceneUnloadRequest request,
        ExceptionDispatchInfo? failure)
    {
        lock (_instanceGate)
        {
            for (var index = 0; index < _ownedInstances.Length; index++)
            {
                if (ReferenceEquals(_ownedInstances[index], instance)
                    && !_reservedInstanceSlots[index]
                    && instance.IsActive)
                {
                    instance.TakeUnloadCleanupOwnership(controlAppend, request, failure);
                    DeactivateOwnedInstance(index);
                    return;
                }
            }
        }

        throw new InvalidOperationException("The retired Scene lost its exact active registry owner.");
    }

    internal void CleanupInstance(SceneInstance instance)
    {
        _world.ValidateSceneCleanupThread();

        if (Interlocked.CompareExchange(
                ref _cleanupInvokerThreadId,
                Environment.CurrentManagedThreadId,
                0) != 0)
        {
            throw new InvalidOperationException("The Scene registry already has a cleanup invoker.");
        }

        try
        {
            instance.RevokeFromWorld();
        }
        finally
        {
            Volatile.Write(ref _cleanupInvokerThreadId, 0);
            FinalizeInstanceCleanup(instance);
        }
    }

    internal void CommitReservation(SceneInstanceReservation reservation, SceneInstance instance)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(instance);

        lock (_instanceGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != DisposeStateOpen, this);

            if (!ReferenceEquals(reservation.Registry, this)
                || !_reservedInstanceSlots[reservation.Slot]
                || !ReferenceEquals(_ownedInstances[reservation.Slot], instance)
                || (instance.OccurrenceCount != reservation.OccurrenceCount)
                || (instance.SceneInstanceId == Guid.Empty)
                || !instance.IsActivating)
            {
                throw new InvalidOperationException("The Scene instance reservation no longer matches its prepared publication.");
            }

            var orderedInsertionIndex = (instance.EntityCount == 0)
                ? -1
                : FindActiveInstanceInsertionIndex(instance);

            _reservedInstanceSlots[reservation.Slot] = false;
            _reservedInstanceCount--;
            _reservedOccurrenceCount -= reservation.OccurrenceCount;
            instance.PublishActivation();
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
            for (var index = 0; index < _ownedInstances.Length; index++)
            {
                if (ReferenceEquals(_ownedInstances[index], instance)
                    && !_reservedInstanceSlots[index]
                    && instance.IsActive)
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal void CopyPersistenceInstances(
        WorldPersistenceCaptureLease lease,
        scoped Span<SceneInstance> destination)
    {
        ValidatePersistenceCaptureLease(lease);

        lock (_instanceGate)
        {
            if (destination.Length != _activeInstanceCount)
            {
                throw new ArgumentException("The Scene instance destination must match the exact active instance count.", nameof(destination));
            }

            var destinationIndex = 0;

            for (var index = 0; index < _ownedInstances.Length; index++)
            {
                if (!_reservedInstanceSlots[index] && (_ownedInstances[index] is { IsActive: true } instance))
                {
                    if (destinationIndex >= destination.Length)
                    {
                        throw new InvalidOperationException("The active Scene instance table contains more entries than its reported count.");
                    }

                    destination[destinationIndex++] = instance;
                }
            }

            if (destinationIndex != destination.Length)
            {
                throw new InvalidOperationException("The active Scene instance table does not contain every reported instance.");
            }
        }
    }

    internal SceneActivationEnqueueStatus EnqueueActivation(SceneActivationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_requestGate)
        {
            if (Volatile.Read(ref _disposeState) != DisposeStateOpen)
            {
                return SceneActivationEnqueueStatus.RegistryDisposed;
            }

            if (_isPersistenceCaptureActive || (_pendingRequestCount == _pendingRequests.Length))
            {
                return SceneActivationEnqueueStatus.QueueFull;
            }

            var access = request.AdmitPreparationBody();

            if (access is not ScenePreparationAccess.Available)
            {
                return access switch {
                    ScenePreparationAccess.NotAvailable => SceneActivationEnqueueStatus.PreparedNotAvailable,
                    ScenePreparationAccess.AlreadyAdmitted => SceneActivationEnqueueStatus.AlreadyAdmitted,
                    ScenePreparationAccess.WrongWorld => SceneActivationEnqueueStatus.WrongWorld,
                    ScenePreparationAccess.WrongAssetStore => SceneActivationEnqueueStatus.WrongAssetStore,
                    _ => SceneActivationEnqueueStatus.PolicyMismatch,
                };
            }

            var tail = (_pendingRequestHead + _pendingRequestCount) % _pendingRequests.Length;
            _pendingRequests[tail] = request;
            _pendingRequestCount++;
            return SceneActivationEnqueueStatus.Queued;
        }
    }

    internal void EnsureBoundTo(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != DisposeStateOpen, this);

        if (!ReferenceEquals(world, _world))
        {
            throw new ArgumentException("The Scene registry belongs to another exact World binding.", nameof(world));
        }
    }

    internal void FinalizeInstanceCleanup(SceneInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        lock (_instanceGate)
        {
            for (var index = 0; index < _ownedInstances.Length; index++)
            {
                if (!ReferenceEquals(_ownedInstances[index], instance)
                    || !instance.IsCleanupComplete
                    || instance.IsUnloadInvocationActive)
                {
                    continue;
                }

                _ownedInstances[index] = null;
                _cleanupPendingInstanceCount--;
                _cleanupPendingOccurrenceCount -= instance.OccurrenceCount;
                return;
            }
        }
    }

    internal int GetPersistenceInstanceCount(WorldPersistenceCaptureLease lease)
    {
        ValidatePersistenceCaptureLease(lease);

        lock (_instanceGate)
        {
            return _activeInstanceCount;
        }
    }

    internal void ReleaseReservation(SceneInstanceReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        SceneInstance? instance;

        lock (_instanceGate)
        {
            if (!ReferenceEquals(reservation.Registry, this)
                || !_reservedInstanceSlots[reservation.Slot])
            {
                return;
            }

            instance = _ownedInstances[reservation.Slot];

            if (instance is null)
            {
                _reservedInstanceSlots[reservation.Slot] = false;
                _reservedInstanceCount--;
                _reservedOccurrenceCount -= reservation.OccurrenceCount;
            }
            else
            {
                DeactivateOwnedInstance(reservation.Slot);
            }
        }

        if (instance is not null)
        {
            CleanupInstance(instance);
        }
    }

    internal bool TryEnqueue(SceneOwnerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != DisposeStateOpen, this);

        lock (_requestGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != DisposeStateOpen, this);

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
            for (var index = 0; index < _ownedInstances.Length; index++)
            {
                var candidate = _ownedInstances[index];

                if (!_reservedInstanceSlots[index]
                    && (candidate is { IsActive: true })
                    && (candidate.SceneInstanceId == sceneInstanceId))
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

                if (candidate.IsActive && (entityOrdinalOffset < (uint)candidate.EntityCount))
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != DisposeStateOpen, this);

        lock (_instanceGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != DisposeStateOpen, this);

            if ((_activeInstanceCount + _cleanupPendingInstanceCount + _reservedInstanceCount) >= _maximumActiveSceneInstances
                || occurrenceCount > (_maximumSceneOccurrences
                    - _activeOccurrenceCount
                    - _cleanupPendingOccurrenceCount
                    - _reservedOccurrenceCount))
            {
                reservation = null;
                return false;
            }

            for (var index = 0; index < _ownedInstances.Length; index++)
            {
                if ((_ownedInstances[index] is not null) || _reservedInstanceSlots[index])
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

    private void DeactivateOwnedInstance(int index)
    {
        if (_ownedInstances[index] is not { } instance)
        {
            return;
        }

        if (_reservedInstanceSlots[index])
        {
            _reservedInstanceSlots[index] = false;
            _reservedInstanceCount--;
            _reservedOccurrenceCount -= instance.OccurrenceCount;
        }
        else if (instance.IsActive)
        {
            if (instance.EntityCount != 0)
            {
                RemoveActiveInstanceByOrdinal(instance);
            }

            _activeInstanceCount--;
            _activeOccurrenceCount -= instance.OccurrenceCount;
        }
        else
        {
            return;
        }

        _ = instance.Deactivate();
        _cleanupPendingInstanceCount++;
        _cleanupPendingOccurrenceCount = checked(_cleanupPendingOccurrenceCount + instance.OccurrenceCount);
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

    private void ValidatePersistenceCaptureLease(WorldPersistenceCaptureLease lease)
    {
        lease.ValidateCurrent();

        if (!lease.IsBoundTo(_world))
        {
            throw new ArgumentException("The capture lease belongs to another World.", nameof(lease));
        }

        lock (_requestGate)
        {
            if (!_isPersistenceCaptureActive)
            {
                throw new InvalidOperationException("Scene instance enumeration requires an active persistence capture.");
            }
        }
    }
}
