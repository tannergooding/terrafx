// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
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

    internal nint CountAttachmentStateRecord(
        WorldPersistenceCaptureLease lease,
        int attachmentIndex,
        CancellationToken cancellationToken)
    {
        lease.ValidateCurrent();

        if (!lease.IsBoundTo(this))
        {
            throw new ArgumentException("The persistence capture lease belongs to another World.", nameof(lease));
        }

        var entry = GetAttachmentCaptureEntry(attachmentIndex);
        var registration = entry.Registration
            ?? throw new InvalidOperationException("The transferred attachment has no captured state provider.");
        var context = new AttachmentStateCaptureContext(this, registration.Descriptor);
        var binding = new WorldAttachmentCaptureBinding(context);

        try
        {
            var count = RunAttachmentCaptureCount(registration, entry, binding, cancellationToken);
            lease.ValidateCurrent();
            return count;
        }
        finally
        {
            binding.Revoke();
            context.Revoke();
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

    internal nint CountSystemStateRecord(
        WorldPersistenceCaptureLease lease,
        int systemIndex,
        CancellationToken cancellationToken)
    {
        lease.ValidateCurrent();

        if (!lease.IsBoundTo(this))
        {
            throw new ArgumentException("The persistence capture lease belongs to another World.", nameof(lease));
        }

        var registration = GetSystemCaptureRegistration(systemIndex, out var system, out var dispatch);
        var context = new SystemStateCaptureContext(this, registration);
        var binding = new WorldSystemCaptureBinding(context);

        try
        {
            var count = dispatch.Count(system, registration.Description.SystemId, binding, cancellationToken);
            lease.ValidateCurrent();
            return count;
        }
        finally
        {
            binding.Revoke();
            context.Revoke();
        }
    }

    internal AttachmentStateCaptureEnumerator CreateAttachmentStateEnumerator(WorldPersistenceCaptureLease lease)
    {
        lease.ValidateCurrent();

        if (!lease.IsBoundTo(this))
        {
            throw new ArgumentException("The persistence capture lease belongs to another World.", nameof(lease));
        }

        return new AttachmentStateCaptureEnumerator(this, lease);
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

    internal EntityStateCaptureEnumerator CreateEntityStateEnumerator(WorldPersistenceCaptureLease lease)
    {
        lease.ValidateCurrent();

        if (!lease.IsBoundTo(this))
        {
            throw new ArgumentException("The persistence capture lease belongs to another World.", nameof(lease));
        }

        return new EntityStateCaptureEnumerator(this, lease);
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

    internal SystemStateCaptureEnumerator CreateSystemStateEnumerator(WorldPersistenceCaptureLease lease)
    {
        lease.ValidateCurrent();

        if (!lease.IsBoundTo(this))
        {
            throw new ArgumentException("The persistence capture lease belongs to another World.", nameof(lease));
        }

        return new SystemStateCaptureEnumerator(this, lease);
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

    internal nint WriteAttachmentStateRecord(
        WorldPersistenceCaptureLease lease,
        int attachmentIndex,
        scoped Span<byte> destination,
        CancellationToken cancellationToken)
    {
        lease.ValidateCurrent();

        if (!lease.IsBoundTo(this))
        {
            throw new ArgumentException("The persistence capture lease belongs to another World.", nameof(lease));
        }

        var entry = GetAttachmentCaptureEntry(attachmentIndex);
        var registration = entry.Registration
            ?? throw new InvalidOperationException("The transferred attachment has no captured state provider.");
        var context = new AttachmentStateCaptureContext(this, registration.Descriptor);
        var binding = new WorldAttachmentCaptureBinding(context);

        try
        {
            var written = RunAttachmentCaptureWrite(registration, entry, destination, binding, cancellationToken);
            lease.ValidateCurrent();
            return written;
        }
        finally
        {
            binding.Revoke();
            context.Revoke();
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

    internal nint WriteSystemStateRecord(
        WorldPersistenceCaptureLease lease,
        int systemIndex,
        scoped Span<byte> destination,
        CancellationToken cancellationToken)
    {
        lease.ValidateCurrent();

        if (!lease.IsBoundTo(this))
        {
            throw new ArgumentException("The persistence capture lease belongs to another World.", nameof(lease));
        }

        var registration = GetSystemCaptureRegistration(systemIndex, out var system, out var dispatch);
        var context = new SystemStateCaptureContext(this, registration);
        var binding = new WorldSystemCaptureBinding(context);

        try
        {
            var written = dispatch.Write(
                system,
                registration.Description.SystemId,
                destination,
                binding,
                cancellationToken);
            lease.ValidateCurrent();
            return written;
        }
        finally
        {
            binding.Revoke();
            context.Revoke();
        }
    }

    private WorldAttachmentTable.AttachmentEntry GetAttachmentCaptureEntry(int attachmentIndex)
    {
        var entry = _attachments.GetEntry(attachmentIndex);

        if (entry.Ownership != AttachmentOwnership.Transferred)
        {
            throw new ArgumentException("Only transferred authoritative attachments can be captured.", nameof(attachmentIndex));
        }

        var registration = entry.Registration
            ?? throw new InvalidOperationException("The transferred attachment has no captured state provider.");
        var value = entry.Value
            ?? throw new InvalidOperationException("The transferred attachment has no value.");
        var valueType = entry.ValueType
            ?? throw new InvalidOperationException("The transferred attachment has no CLR type key.");

        if ((registration.ValueType != valueType)
            || !valueType.IsInstanceOfType(value)
            || registration.Scope == AttachmentScope.World != (entry.OwnerEntityId is null)
            || (entry.OwnerEntityId == Guid.Empty)
            || !Registration.TryGetAttachment(registration.Scope, valueType, out var registered)
            || !ReferenceEquals(registered, registration))
        {
            throw new InvalidOperationException("The attachment table entry does not match its frozen World registration.");
        }

        if ((entry.OwnerEntityId is { } ownerEntityId) && !TryGetEntity(ownerEntityId, out _))
        {
            throw new InvalidOperationException("An Entity-scoped attachment owner is not live in this World.");
        }

        return entry;
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

    private SystemRegistration GetSystemCaptureRegistration(
        int systemIndex,
        out IWorldSystem system,
        out IWorldSystemCaptureDispatch dispatch)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(systemIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(systemIndex, Registration.Systems.Length);
        var registration = Registration.Systems[systemIndex];

        if (registration.Factory.Descriptor.StateSchema is null)
        {
            throw new ArgumentException("Only stateful system registrations have capture records.", nameof(systemIndex));
        }

        dispatch = registration.Factory.Dispatch as IWorldSystemCaptureDispatch
            ?? throw new InvalidOperationException("The stateful system registration has no capture codec dispatch.");
        system = _systemInstances[systemIndex]
            ?? throw new InvalidOperationException("The stateful system registration has no live World instance.");
        return registration;
    }

    [SuppressMessage("Usage", "CA1031:Do not catch general exception types", Justification = "The capture wrapper must preserve the first sticky reentrancy failure and revoke the attachment callback boundary.")]
    private nint RunAttachmentCaptureCount(
        IWorldAttachmentRegistration registration,
        WorldAttachmentTable.AttachmentEntry entry,
        WorldAttachmentCaptureBinding binding,
        CancellationToken cancellationToken)
    {
        StartAttachmentCallback();
        nint count = 0;
        Exception? callbackFailure = null;

        try
        {
            count = registration.CountStateRecord(
                entry.Value ?? throw new InvalidOperationException("The transferred attachment has no value."),
                entry.OwnerEntityId,
                binding,
                cancellationToken);
        }
        catch (Exception exception)
        {
            callbackFailure = exception;
        }
        finally
        {
            StopAttachmentCallback();
        }

        var stickyFailure = Interlocked.Exchange(ref _attachmentCallbackStickyFailure, null);

        if (stickyFailure is not null)
        {
            throw stickyFailure;
        }

        if (callbackFailure is not null)
        {
            throw callbackFailure;
        }

        return count;
    }

    [SuppressMessage("Usage", "CA1031:Do not catch general exception types", Justification = "The capture wrapper must preserve the first sticky reentrancy failure and revoke the attachment callback boundary.")]
    private nint RunAttachmentCaptureWrite(
        IWorldAttachmentRegistration registration,
        WorldAttachmentTable.AttachmentEntry entry,
        scoped Span<byte> destination,
        WorldAttachmentCaptureBinding binding,
        CancellationToken cancellationToken)
    {
        StartAttachmentCallback();
        nint written = 0;
        Exception? callbackFailure = null;

        try
        {
            written = registration.WriteStateRecord(
                entry.Value ?? throw new InvalidOperationException("The transferred attachment has no value."),
                entry.OwnerEntityId,
                destination,
                binding,
                cancellationToken);
        }
        catch (Exception exception)
        {
            callbackFailure = exception;
        }
        finally
        {
            StopAttachmentCallback();
        }

        var stickyFailure = Interlocked.Exchange(ref _attachmentCallbackStickyFailure, null);

        if (stickyFailure is not null)
        {
            throw stickyFailure;
        }

        if (callbackFailure is not null)
        {
            throw callbackFailure;
        }

        return written;
    }

    internal sealed class AttachmentStateCaptureContext : IWorldAttachmentCaptureContext
    {
        private readonly WorldAttachmentFactoryDescriptor _descriptor;
        private Exception? _failure;
        private bool _isActive = true;
        private World? _world;

        internal AttachmentStateCaptureContext(World world, WorldAttachmentFactoryDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(descriptor);
            _world = world;
            _descriptor = descriptor;
            OwnerThreadId = world._ownerThreadId;
        }

        public WorldAttachmentFactoryDescriptor Descriptor => _descriptor;

        public Exception? Failure => _failure;

        public bool IsActive => _isActive && (_world is not null);

        public int OwnerThreadId { get; }

        public Guid GetEntityId(WorldAttachmentFieldDescriptor referenceField, Entity entity)
        {
            EnsureActive();
            ArgumentNullException.ThrowIfNull(referenceField);

            if (!IsRegisteredEntityReferenceField(referenceField) || !entity.IsValid)
            {
                throw RecordFailure(new ArgumentException(
                    "The selected field or Entity does not match this attachment capture operation.",
                    nameof(referenceField)));
            }

            var world = _world
                ?? throw RecordFailure(new InvalidOperationException("The attachment capture context has expired."));
            _ = world.GetLiveArchetype(entity, out _, out _);
            var entityId = world._identityLedger.GetAssignedId(IdentityKind.Entity, entity.Index);

            if (entityId == Guid.Empty)
            {
                throw RecordFailure(new InvalidOperationException("The attachment capture context returned an empty Entity identity."));
            }

            return entityId;
        }

        public Exception RecordFailure(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            _failure ??= exception;
            return _failure;
        }

        internal void Revoke()
        {
            _world = null;
            _isActive = false;
        }

        private void EnsureActive()
        {
            var world = _world
                ?? throw RecordFailure(new InvalidOperationException("The attachment capture context has expired."));

            if (!_isActive || (Environment.CurrentManagedThreadId != OwnerThreadId))
            {
                throw RecordFailure(new InvalidOperationException("The attachment capture context has expired or is restricted to its issuing thread."));
            }

            if (Volatile.Read(ref world._isAttachmentCallbackActive) == 0)
            {
                throw RecordFailure(new InvalidOperationException("The attachment capture context is not within its provider callback."));
            }
        }

        private bool IsRegisteredEntityReferenceField(WorldAttachmentFieldDescriptor referenceField)
        {
            if ((referenceField.Schema != _descriptor.Schema)
                || (referenceField.Field.WireKind != 11)
                || (referenceField.Field.Reference != ContentReferenceKind.Entity))
            {
                return false;
            }

            foreach (var field in _descriptor.Fields)
            {
                if (ReferenceEquals(field, referenceField))
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal ref struct AttachmentStateCaptureEnumerator
    {
        private readonly World _world;
        private readonly WorldPersistenceCaptureLease _lease;
        private int _entryIndex;
        private bool _hasCurrent;
        private WorldAttachmentTable.AttachmentEntry _currentEntry;

        internal AttachmentStateCaptureEnumerator(World world, WorldPersistenceCaptureLease lease)
        {
            _world = world;
            _lease = lease;
            _entryIndex = -1;
            _hasCurrent = false;
            _currentEntry = default;
        }

        internal readonly int CurrentIndex
        {
            get
            {
                ValidateCurrent();
                return _entryIndex;
            }
        }

        internal readonly Guid? CurrentOwnerEntityId
        {
            get
            {
                ValidateCurrent();
                return _currentEntry.OwnerEntityId;
            }
        }

        internal readonly ContentSchemaReference CurrentSchema
        {
            get
            {
                ValidateCurrent();
                return _currentEntry.Registration?.Descriptor.Schema
                    ?? throw new InvalidOperationException("The current attachment capture record has no registration.");
            }
        }

        internal readonly AttachmentScope CurrentScope
        {
            get
            {
                ValidateCurrent();
                return _currentEntry.Registration?.Scope
                    ?? throw new InvalidOperationException("The current attachment capture record has no registration.");
            }
        }

        internal bool MoveNext()
        {
            _lease.ValidateCurrent();
            _hasCurrent = false;
            _currentEntry = default;

            while (++_entryIndex < _world._attachments.Count)
            {
                var entry = _world._attachments.GetEntry(_entryIndex);

                if (entry.Ownership != AttachmentOwnership.Transferred)
                {
                    continue;
                }

                _currentEntry = _world.GetAttachmentCaptureEntry(_entryIndex);
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
                throw new InvalidOperationException("The attachment capture enumerator has no current record.");
            }
        }
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

    internal sealed class SystemStateCaptureContext : IWorldSystemCaptureContext
    {
        private readonly WorldSystemFactoryDescriptor _descriptor;
        private Exception? _failure;
        private bool _isActive = true;
        private World? _world;

        internal SystemStateCaptureContext(World world, SystemRegistration registration)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(registration);

            if (registration.Factory.Descriptor.StateSchema is null)
            {
                throw new ArgumentException("A system capture context requires a stateful registration.", nameof(registration));
            }

            _world = world;
            _descriptor = registration.Factory.Descriptor;
            OwnerThreadId = world._ownerThreadId;
            SystemId = registration.Description.SystemId;
        }

        public Exception? Failure => _failure;

        public bool IsActive => _isActive && (_world is not null);

        public int OwnerThreadId { get; }

        public ContentSchemaReference Schema => _descriptor.StateSchema
            ?? throw new InvalidOperationException("A stateless system has no capture schema.");

        public Guid SystemId { get; }

        public Guid GetEntityId(WorldSystemFieldDescriptor referenceField, Entity entity)
        {
            EnsureActive();
            ArgumentNullException.ThrowIfNull(referenceField);

            if (!IsRegisteredEntityReferenceField(referenceField) || !entity.IsValid)
            {
                throw RecordFailure(new ArgumentException(
                    "The selected field or Entity does not match this system capture operation.",
                    nameof(referenceField)));
            }

            var world = _world
                ?? throw RecordFailure(new InvalidOperationException("The system capture context has expired."));
            var entityId = world.GetEntityId(entity);

            if (entityId == Guid.Empty)
            {
                throw RecordFailure(new InvalidOperationException("The system capture context returned an empty Entity identity."));
            }

            return entityId;
        }

        public Exception RecordFailure(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            _failure ??= exception;
            return _failure;
        }

        internal void Revoke()
        {
            _world = null;
            _isActive = false;
        }

        private void EnsureActive()
        {
            if (!IsActive)
            {
                throw RecordFailure(new InvalidOperationException("The system capture context has expired."));
            }

            if (Environment.CurrentManagedThreadId != OwnerThreadId)
            {
                throw RecordFailure(new InvalidOperationException("The system capture context is restricted to its issuing thread."));
            }
        }

        private bool IsRegisteredEntityReferenceField(WorldSystemFieldDescriptor referenceField)
        {
            if ((referenceField.StateSchema != Schema)
                || (referenceField.Field.WireKind != 11)
                || (referenceField.Field.Reference != ContentReferenceKind.Entity))
            {
                return false;
            }

            foreach (var field in _descriptor.Fields)
            {
                if (ReferenceEquals(field, referenceField))
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal ref struct SystemStateCaptureEnumerator
    {
        private readonly World _world;
        private readonly WorldPersistenceCaptureLease _lease;
        private int _systemIndex;
        private bool _hasCurrent;

        internal SystemStateCaptureEnumerator(World world, WorldPersistenceCaptureLease lease)
        {
            _world = world;
            _lease = lease;
            _systemIndex = -1;
            _hasCurrent = false;
        }

        internal readonly int CurrentIndex
        {
            get
            {
                ValidateCurrent();
                return _systemIndex;
            }
        }

        internal readonly ContentSchemaReference CurrentSchema
        {
            get
            {
                ValidateCurrent();
                return _world.Registration.Systems[_systemIndex].Factory.Descriptor.StateSchema
                    ?? throw new InvalidOperationException("The current system capture record has no schema.");
            }
        }

        internal readonly Guid CurrentSystemId
        {
            get
            {
                ValidateCurrent();
                return _world.Registration.Systems[_systemIndex].Description.SystemId;
            }
        }

        internal bool MoveNext()
        {
            _lease.ValidateCurrent();
            _hasCurrent = false;

            while (++_systemIndex < _world.Registration.Systems.Length)
            {
                if (_world.Registration.Systems[_systemIndex].Factory.Descriptor.StateSchema is not null)
                {
                    _hasCurrent = true;
                    return true;
                }
            }

            return false;
        }

        private readonly void ValidateCurrent()
        {
            _lease.ValidateCurrent();

            if (!_hasCurrent)
            {
                throw new InvalidOperationException("The system capture enumerator has no current record.");
            }
        }
    }
}
