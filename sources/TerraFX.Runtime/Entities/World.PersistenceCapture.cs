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

    private sealed class ComponentStateCaptureContext : IComponentCaptureContext
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
}
