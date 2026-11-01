// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using TerraFX.Numerics;

namespace TerraFX.Entities;

public sealed partial class World
{
    private long _activeFrameReadLeaseSequence;
    private IWorldCpuExtractionParticipant? _cpuExtractionParticipant;
    private readonly long _cpuExtractionSupportByteReservation;
    private bool _isFrameReadLeaseActive;
    private long _nextFrameReadLeaseSequence;

    internal WorldFrameReadLease AcquireFrameReadLease(
        IWorldCpuExtractionParticipant participant,
        IWorldSceneParticipant sceneParticipant)
    {
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(sceneParticipant);
        ThrowIfAttachmentCallbackActive();
        ThrowIfSystemInvocationActive();
        ThrowIfDisposed();
        ThrowIfFaulted();
        EnsureOwnerThread();

        if (!ReferenceEquals(participant, _cpuExtractionParticipant)
            || !ReferenceEquals(sceneParticipant, _sceneParticipant))
        {
            throw new ArgumentException("The CPU extraction support does not belong to this exact World and Scene participant.");
        }

        if (Interlocked.CompareExchange(ref _isStructuralOperationActive, 1, 0) != 0)
        {
            throw new InvalidOperationException("The World cannot begin a frame read lease during another operation.");
        }

        _isFrameReadLeaseActive = true;

        try
        {
            var publication = Volatile.Read(ref _committedPublication)
                ?? throw new InvalidOperationException("CPU extraction requires a committed World publication.");

            if (_canonicalMutationRevision != _committedMutationRevision)
            {
                throw new InvalidOperationException("CPU extraction requires the current canonical mutation revision to be committed.");
            }

            var sequence = checked(_nextFrameReadLeaseSequence + 1);
            _nextFrameReadLeaseSequence = sequence;
            _activeFrameReadLeaseSequence = sequence;
            var frameCohort = _currentFrameCohort;

            return new WorldFrameReadLease(
                this,
                sequence,
                frameCohort.CohortGeneration,
                _canonicalMutationRevision,
                _committedMutationRevision,
                publication.PublicationSequence,
                publication.CompletedTick,
                _worldGuid,
                frameCohort.Frame3,
                frameCohort.Frame2);
        }
        catch
        {
            _isFrameReadLeaseActive = false;
            _ = Interlocked.Exchange(ref _isStructuralOperationActive, 0);
            throw;
        }
    }

    internal WorldFrameReadLease AcquireFrameReadLease()
    {
        var participant = _cpuExtractionParticipant
            ?? throw new InvalidOperationException("CPU extraction support was not registered before World construction.");
        var sceneParticipant = _sceneParticipant
            ?? throw new InvalidOperationException("CPU extraction support has no initialized Scene participant.");
        return participant.AcquireFrameReadLease(sceneParticipant);
    }

    internal TParticipant? GetCpuExtractionParticipant<TParticipant>()
        where TParticipant : class
        => Volatile.Read(ref _cpuExtractionParticipant) as TParticipant;

    internal ComponentSnapshotEnumerator<T> ReadComponentSnapshot<T>(in WorldFrameReadLease lease)
        where T : unmanaged
    {
        if (!lease.IsBoundTo(this))
        {
            throw new ArgumentException("The frame read lease belongs to another World.", nameof(lease));
        }

        lease.ValidateCurrent();
        return new ComponentSnapshotEnumerator<T>(this, in lease);
    }

    internal void ReleaseFrameReadLease(WorldFrameReadLease lease)
    {
        ValidateFrameReadLease(lease);
        _activeFrameReadLeaseSequence = 0;
        _isFrameReadLeaseActive = false;
        _ = Interlocked.Exchange(ref _isStructuralOperationActive, 0);
    }

    internal (SpatialPosition3 Position, Quaternion Rotation) ResolveSceneEntityPose(
        in WorldFrameReadLease lease,
        Entity entity)
    {
        ValidateSceneReadLease(in lease);
        var participant = _sceneParticipant
            ?? throw new InvalidOperationException("The World Scene participant has not been initialized.");
        return participant.ResolveEntityPose(this, lease.Frame3, entity);
    }

    internal (SpatialPosition3 Position, Matrix4x4 LinearTransform, Quaternion Rotation) ResolveSceneEntityTransform(
        in WorldFrameReadLease lease,
        Entity entity)
    {
        ValidateSceneReadLease(in lease);
        var participant = _sceneParticipant
            ?? throw new InvalidOperationException("The World Scene participant has not been initialized.");
        return participant.ResolveEntityTransform(this, lease.Frame3, entity);
    }

    internal bool TryGetCameraData(
        in WorldFrameReadLease lease,
        Entity entity,
        out WorldCameraData camera)
    {
        ValidateSceneReadLease(in lease);
        return (_sceneParticipant
            ?? throw new InvalidOperationException("The World Scene participant has not been initialized."))
            .TryGetCameraData(this, entity, out camera);
    }

    internal void ValidateFrameReadLease(WorldFrameReadLease lease)
    {
        ThrowIfDisposed();
        ThrowIfFaulted();
        EnsureOwnerThread();

        if ((Volatile.Read(ref _isStructuralOperationActive) == 0)
            || !_isFrameReadLeaseActive
            || (lease.Sequence == 0)
            || (lease.Sequence != _activeFrameReadLeaseSequence)
            || (lease.CanonicalMutationRevision != _canonicalMutationRevision)
            || (lease.CommittedMutationRevision != _committedMutationRevision)
            || (_canonicalMutationRevision != _committedMutationRevision)
            || (lease.CohortGeneration != _currentFrameCohort.CohortGeneration)
            || (lease.WorldGuid != _worldGuid))
        {
            throw new InvalidOperationException("The CPU extraction frame read lease is stale or no longer active.");
        }

        var publication = Volatile.Read(ref _committedPublication);

        if ((publication is null)
            || (publication.PublicationSequence != lease.PublicationSequence)
            || (publication.CompletedTick != lease.CompletedTick))
        {
            throw new InvalidOperationException("The CPU extraction publication changed during the frame read lease.");
        }
    }

    private void InitializeCpuExtractionSupport(WorldOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var support = Registration.CpuExtractionSupport;

        if (support is null)
        {
            return;
        }

        var sceneParticipant = _sceneParticipant
            ?? throw new InvalidOperationException("CPU extraction support requires an initialized Scene participant.");
        var participant = support.CreateParticipant(this, options, sceneParticipant);

        if (participant.OwnedByteReservation != _cpuExtractionSupportByteReservation)
        {
            participant.Dispose();
            throw new InvalidOperationException("The CPU extraction participant storage reservation does not match World admission.");
        }

        _cpuExtractionParticipant = participant;
    }

    private void ValidateSceneReadLease(in WorldFrameReadLease lease)
    {
        if (!lease.IsBoundTo(this))
        {
            throw new ArgumentException("The frame read lease belongs to another World.", nameof(lease));
        }

        lease.ValidateCurrent();
    }

    internal ref struct ComponentSnapshotEnumerator<T>
        where T : unmanaged
    {
        private readonly World _world;
        private readonly WorldFrameReadLease _lease;
        private readonly Type _componentType;
        private WorldArchetype? _currentArchetype;
        private int _archetypeIndex;
        private int _chunkIndex;
        private int _componentIndex;
        private ReadOnlySpan<Entity> _entities;
        private ReadOnlySpan<T> _values;
        private int _rowIndex;
        private bool _hasCurrent;
        private Entity _currentEntity;
        private T _currentValue;

        internal ComponentSnapshotEnumerator(World world, in WorldFrameReadLease lease)
        {
            ArgumentNullException.ThrowIfNull(world);
            _world = world;
            _lease = lease;
            _componentType = world.GetComponentRegistration<T>().ComponentType;
            _currentArchetype = null;
            _archetypeIndex = -1;
            _chunkIndex = -1;
            _componentIndex = -1;
            _entities = default;
            _values = default;
            _rowIndex = 0;
            _hasCurrent = false;
            _currentEntity = default;
            _currentValue = default;
        }

        internal readonly Entity CurrentEntity
        {
            get
            {
                _lease.ValidateCurrent();
                return _hasCurrent
                    ? _currentEntity
                    : throw new InvalidOperationException("The component snapshot enumerator has no current Entity.");
            }
        }

        internal readonly T CurrentValue
        {
            get
            {
                _lease.ValidateCurrent();
                return _hasCurrent
                    ? _currentValue
                    : throw new InvalidOperationException("The component snapshot enumerator has no current value.");
            }
        }

        internal bool MoveNext()
        {
            _lease.ValidateCurrent();
            _hasCurrent = false;

            while (true)
            {
                if (_rowIndex < _entities.Length)
                {
                    _currentEntity = _entities[_rowIndex];
                    _currentValue = _values[_rowIndex];
                    _rowIndex++;
                    _hasCurrent = true;
                    return true;
                }

                _rowIndex = 0;

                if (_currentArchetype is not null)
                {
                    var nextChunkIndex = _chunkIndex + 1;

                    if (nextChunkIndex < _currentArchetype.Storage.ChunkCount)
                    {
                        _chunkIndex = nextChunkIndex;
                        LoadChunk();
                        continue;
                    }

                    _currentArchetype = null;
                }

                while (++_archetypeIndex < _world._archetypeCount)
                {
                    var archetype = _world._archetypes[_archetypeIndex]
                        ?? throw new InvalidOperationException("The World archetype directory contains an empty active slot.");
                    var componentIndex = archetype.GetComponentIndex(_componentType);

                    if ((componentIndex < 0) || (archetype.Storage.ChunkCount == 0))
                    {
                        continue;
                    }

                    _currentArchetype = archetype;
                    _componentIndex = componentIndex;
                    _chunkIndex = 0;
                    LoadChunk();
                    break;
                }

                if (_currentArchetype is null)
                {
                    return false;
                }
            }
        }

        private void LoadChunk()
        {
            _entities = _currentArchetype!.Storage.GetEntities(_chunkIndex);
            _values = _currentArchetype.Storage.GetReadOnlyColumnSpan<T>(
                _componentIndex,
                _chunkIndex,
                0,
                _entities.Length);
            _rowIndex = 0;
        }
    }
}
