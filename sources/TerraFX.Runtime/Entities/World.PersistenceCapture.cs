// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

public sealed partial class World
{
    private long _activePersistenceCaptureLeaseSequence;
    private bool _isPersistenceCaptureLeaseActive;
    private long _nextPersistenceCaptureLeaseSequence;

    internal WorldPersistenceCaptureLease AcquirePersistenceCaptureLease()
    {
        ThrowIfAttachmentCallbackActive();
        ThrowIfSystemInvocationActive();
        ThrowIfDisposed();
        ThrowIfFaulted();
        EnsureOwnerThread();

        if (Interlocked.CompareExchange(ref _isStructuralOperationActive, 1, 0) != 0)
        {
            throw new InvalidOperationException("The World cannot begin persistence capture during another operation.");
        }

        _isPersistenceCaptureLeaseActive = true;

        try
        {
            var publication = Volatile.Read(ref _committedPublication)
                ?? throw new InvalidOperationException("Persistence capture requires a committed World publication.");

            if (_canonicalMutationRevision != _committedMutationRevision)
            {
                throw new InvalidOperationException("Persistence capture requires the current canonical mutation revision to be committed.");
            }

            var sequence = checked(_nextPersistenceCaptureLeaseSequence + 1);
            _nextPersistenceCaptureLeaseSequence = sequence;
            _activePersistenceCaptureLeaseSequence = sequence;
            var frameCohort = _currentFrameCohort;

            return new WorldPersistenceCaptureLease(
                this,
                sequence,
                frameCohort.CohortGeneration,
                _canonicalMutationRevision,
                _committedMutationRevision,
                publication.PublicationSequence,
                publication.CompletedTick,
                publication.NextTickIndex,
                _worldGuid,
                frameCohort.Frame3,
                frameCohort.Frame2);
        }
        catch
        {
            _isPersistenceCaptureLeaseActive = false;
            _activePersistenceCaptureLeaseSequence = 0;
            _ = Interlocked.Exchange(ref _isStructuralOperationActive, 0);
            throw;
        }
    }

    internal nint CountComponentStateRecord(
        WorldPersistenceCaptureLease lease,
        Entity entity,
        ContentSchemaReference schema,
        CancellationToken cancellationToken)
    {
        lease.ValidateCurrent();

        if (!lease.IsBoundTo(this))
        {
            throw new ArgumentException("The persistence capture lease belongs to another World.", nameof(lease));
        }

        var registration = GetComponentCaptureRegistration(
            entity,
            schema,
            out var archetype,
            out var componentIndex,
            out var chunkIndex,
            out var row);
        var context = new ComponentStateCaptureContext(this, registration);

        try
        {
            var count = archetype.Storage.CountStateRecord(
                componentIndex,
                chunkIndex,
                row,
                context,
                cancellationToken);
            lease.ValidateCurrent();
            return count;
        }
        finally
        {
            context.Revoke();
        }
    }

    internal nint CountResourceStateRecord(
        WorldPersistenceCaptureLease lease,
        int resourceIndex,
        CancellationToken cancellationToken)
    {
        lease.ValidateCurrent();

        if (!lease.IsBoundTo(this))
        {
            throw new ArgumentException("The persistence capture lease belongs to another World.", nameof(lease));
        }

        var registration = GetResourceCaptureRegistration(resourceIndex, out var storage);
        var count = registration.CountStateRecord(storage, cancellationToken);
        lease.ValidateCurrent();
        return count;
    }

    internal ComponentStateCaptureEnumerator CreateComponentStateEnumerator(WorldPersistenceCaptureLease lease)
    {
        lease.ValidateCurrent();

        if (!lease.IsBoundTo(this))
        {
            throw new ArgumentException("The persistence capture lease belongs to another World.", nameof(lease));
        }

        return new ComponentStateCaptureEnumerator(this, lease);
    }

    internal ResourceStateCaptureEnumerator CreateResourceStateEnumerator(WorldPersistenceCaptureLease lease)
    {
        lease.ValidateCurrent();

        if (!lease.IsBoundTo(this))
        {
            throw new ArgumentException("The persistence capture lease belongs to another World.", nameof(lease));
        }

        return new ResourceStateCaptureEnumerator(this, lease);
    }

    internal void ReleasePersistenceCaptureLease(WorldPersistenceCaptureLease lease)
    {
        ValidatePersistenceCaptureLease(lease);
        _activePersistenceCaptureLeaseSequence = 0;
        _isPersistenceCaptureLeaseActive = false;
        _ = Interlocked.Exchange(ref _isStructuralOperationActive, 0);
    }

    internal void ValidatePersistenceCaptureLease(WorldPersistenceCaptureLease lease)
    {
        ThrowIfDisposed();
        ThrowIfFaulted();
        EnsureOwnerThread();

        if ((Volatile.Read(ref _isStructuralOperationActive) == 0)
            || !_isPersistenceCaptureLeaseActive
            || (lease.Sequence == 0)
            || (lease.Sequence != _activePersistenceCaptureLeaseSequence)
            || (lease.CanonicalMutationRevision != _canonicalMutationRevision)
            || (lease.CommittedMutationRevision != _committedMutationRevision)
            || (_canonicalMutationRevision != _committedMutationRevision)
            || (lease.CohortGeneration != _currentFrameCohort.CohortGeneration)
            || (lease.WorldGuid != _worldGuid))
        {
            throw new InvalidOperationException("The persistence capture lease is stale or no longer active.");
        }

        var publication = Volatile.Read(ref _committedPublication);

        if ((publication is null)
            || (publication.PublicationSequence != lease.PublicationSequence)
            || (publication.CompletedTick != lease.CompletedTick)
            || (publication.NextTickIndex != lease.NextTickIndex))
        {
            throw new InvalidOperationException("The committed publication changed during persistence capture.");
        }
    }

    internal nint WriteComponentStateRecord(
        WorldPersistenceCaptureLease lease,
        Entity entity,
        ContentSchemaReference schema,
        scoped Span<byte> destination,
        CancellationToken cancellationToken)
    {
        lease.ValidateCurrent();

        if (!lease.IsBoundTo(this))
        {
            throw new ArgumentException("The persistence capture lease belongs to another World.", nameof(lease));
        }

        var registration = GetComponentCaptureRegistration(
            entity,
            schema,
            out var archetype,
            out var componentIndex,
            out var chunkIndex,
            out var row);
        var context = new ComponentStateCaptureContext(this, registration);

        try
        {
            var written = archetype.Storage.WriteStateRecord(
                componentIndex,
                chunkIndex,
                row,
                destination,
                context,
                cancellationToken);
            lease.ValidateCurrent();
            return written;
        }
        finally
        {
            context.Revoke();
        }
    }

    internal nint WriteResourceStateRecord(
        WorldPersistenceCaptureLease lease,
        int resourceIndex,
        scoped Span<byte> destination,
        CancellationToken cancellationToken)
    {
        lease.ValidateCurrent();

        if (!lease.IsBoundTo(this))
        {
            throw new ArgumentException("The persistence capture lease belongs to another World.", nameof(lease));
        }

        var registration = GetResourceCaptureRegistration(resourceIndex, out var storage);
        var written = registration.WriteStateRecord(storage, destination, cancellationToken);
        lease.ValidateCurrent();
        return written;
    }

    private IComponentCaptureRegistration GetComponentCaptureRegistration(
        Entity entity,
        ContentSchemaReference schema,
        out WorldArchetype archetype,
        out int componentIndex,
        out int chunkIndex,
        out int row)
    {
        var component = Registration.GetComponentRegistration(schema);

        if (component.Descriptor.Flags != ComponentStorageFlags.Persistent)
        {
            throw new ArgumentException("Only persistent component records can be captured.", nameof(schema));
        }

        var captureRegistration = component as IComponentCaptureRegistration
            ?? throw new InvalidOperationException("The component registration has no state-capture codec root.");
        archetype = GetLiveArchetype(entity, out chunkIndex, out row);
        componentIndex = archetype.GetComponentIndex(component.ComponentType);

        if (componentIndex < 0)
        {
            throw new ArgumentException("The Entity does not contain the selected component.", nameof(entity));
        }

        return captureRegistration;
    }

    private IWorldResourceRegistration GetResourceCaptureRegistration(int resourceIndex, out object storage)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(resourceIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(resourceIndex, Registration.Resources.Length);
        storage = _resourceStorage[resourceIndex];
        return Registration.Resources[resourceIndex];
    }

    internal sealed class ComponentStateCaptureContext : IComponentCaptureContext
    {
        private IComponentCaptureRegistration? _registration;
        private World? _world;

        internal ComponentStateCaptureContext(World world, IComponentCaptureRegistration registration)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(registration);
            _world = world;
            _registration = registration;
            OwnerThreadId = world._ownerThreadId;
        }

        public bool IsActive => (_world is not null) && (_registration is not null);

        public int OwnerThreadId { get; }

        public Guid GetEntityId(ComponentFieldDescriptor referenceField, Entity entity)
        {
            var world = _world
                ?? throw new InvalidOperationException("The component state capture context has expired.");
            var registration = _registration
                ?? throw new InvalidOperationException("The component state capture context has no active registration.");

            if (!registration.IsEntityReferenceField(referenceField))
            {
                throw new ArgumentException("The selected field is not an Entity reference in this component registration.", nameof(referenceField));
            }

            return world.GetEntityId(entity);
        }

        internal void Revoke()
        {
            _world = null;
            _registration = null;
        }
    }

    internal ref struct ComponentStateCaptureEnumerator
    {
        private readonly World _world;
        private readonly WorldPersistenceCaptureLease _lease;
        private int _componentIndex;
        private Entity _currentEntity;
        private ContentSchemaReference _currentSchema;
        private int _rowIndex;
        private int _workIndex;
        private bool _hasCurrent;

        internal ComponentStateCaptureEnumerator(World world, WorldPersistenceCaptureLease lease)
        {
            _world = world;
            _lease = lease;
            _componentIndex = 0;
            _currentEntity = default;
            _currentSchema = default;
            _rowIndex = 0;
            _workIndex = 0;
            _hasCurrent = false;
        }

        internal readonly Entity CurrentEntity
        {
            get
            {
                ValidateCurrent();
                return _currentEntity;
            }
        }

        internal readonly ContentSchemaReference CurrentSchema
        {
            get
            {
                ValidateCurrent();
                return _currentSchema;
            }
        }

        internal bool MoveNext()
        {
            _lease.ValidateCurrent();
            _hasCurrent = false;

            while (_workIndex < _world._componentTagWorkPlanCount)
            {
                var work = _world._componentTagWorkPlan[_workIndex];
                var archetype = _world._queryArchetypes[work.ArchetypeOrdinal]
                    ?? throw new InvalidOperationException("The component/tag work plan references an empty archetype.");

                if (_rowIndex >= work.Length)
                {
                    _workIndex++;
                    _rowIndex = 0;
                    _componentIndex = 0;
                    continue;
                }

                if (_componentIndex >= archetype.Components.Length)
                {
                    _rowIndex++;
                    _componentIndex = 0;
                    continue;
                }

                var registration = archetype.Components[_componentIndex++];

                if (registration.Descriptor.Flags != ComponentStorageFlags.Persistent)
                {
                    continue;
                }

                var row = work.StartRow + _rowIndex;
                _currentEntity = archetype.Storage.GetEntities(work.ChunkIndex, row, 1)[0];
                _currentSchema = registration.SchemaReference;
                _hasCurrent = true;
                return true;
            }

            return false;
        }

        private readonly void ValidateCurrent()
        {
            _lease.ValidateCurrent();

            if (!_hasCurrent)
            {
                throw new InvalidOperationException("The component capture enumerator has no current record.");
            }
        }
    }

    internal ref struct ResourceStateCaptureEnumerator
    {
        private readonly World _world;
        private readonly WorldPersistenceCaptureLease _lease;
        private int _resourceIndex;
        private bool _hasCurrent;

        internal ResourceStateCaptureEnumerator(World world, WorldPersistenceCaptureLease lease)
        {
            _world = world;
            _lease = lease;
            _resourceIndex = -1;
            _hasCurrent = false;
        }

        internal readonly int CurrentIndex
        {
            get
            {
                ValidateCurrent();
                return _resourceIndex;
            }
        }

        internal readonly Guid CurrentResourceId
        {
            get
            {
                ValidateCurrent();
                return _world.Registration.Resources[_resourceIndex].ResourceId;
            }
        }

        internal readonly ContentSchemaReference CurrentSchema
        {
            get
            {
                ValidateCurrent();
                return _world.Registration.Resources[_resourceIndex].Schema;
            }
        }

        internal bool MoveNext()
        {
            _lease.ValidateCurrent();
            _hasCurrent = false;
            var nextResourceIndex = _resourceIndex + 1;

            if (nextResourceIndex >= _world.Registration.Resources.Length)
            {
                _resourceIndex = _world.Registration.Resources.Length;
                return false;
            }

            _resourceIndex = nextResourceIndex;
            _hasCurrent = true;
            return true;
        }

        private readonly void ValidateCurrent()
        {
            _lease.ValidateCurrent();

            if (!_hasCurrent)
            {
                throw new InvalidOperationException("The resource capture enumerator has no current record.");
            }
        }
    }
}
