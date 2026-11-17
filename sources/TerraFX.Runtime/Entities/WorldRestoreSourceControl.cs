// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldRestoreSourceControl
{
    private static readonly List<WorldRestoreSourceControl> s_anchors = [];
    private static readonly Lock s_anchorGate = new();

    private readonly IWorldAttachmentRegistration? _attachment;
    private bool _attachmentRead;
    private readonly Action _cleanup;
    private bool _cleanupStarted;
    private ReadOnlyMemory<byte> _ecsRecord;
    private Exception? _failure;
    private readonly Lock _gate = new();
    private uint[]? _historicalOrdinals;
    private ValidatedWorldIdentityHistory? _history;
    private bool _isClosed;
    private readonly IWorldRestoreSourceOwner _owner;
    private WorldRestoreSourceLease? _providerLease;
    private readonly IWorldResourceRegistration? _resource;
    private bool _resourceRead;
    private ReadOnlyMemory<byte> _scenesRecord;
    private readonly WorldPreparedSourceStorage _storage;
    private int _uses;

    internal WorldRestoreSourceControl(
        IWorldRestoreSourceOwner owner,
        ReadOnlyMemory<byte> ecsRecord,
        ReadOnlyMemory<byte> scenesRecord,
        IWorldResourceRegistration? resource,
        IWorldAttachmentRegistration? attachment,
        WorldPreparedSourceStorage storage,
        Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(cleanup);
        _owner = owner;
        _history = owner.IdentityHistory;
        _ecsRecord = ecsRecord;
        _scenesRecord = scenesRecord;
        _resource = resource;
        _attachment = attachment;
        _storage = storage;
        _cleanup = cleanup;

        lock (s_anchorGate)
        {
            s_anchors.Add(this);
        }
    }

    internal IWorldRestoreSourceOwner Owner => _owner;

    internal long StorageByteReservation => _storage.OwnedByteReservation;

    internal IDisposable AcquireUse()
    {
        lock (_gate)
        {
            ThrowIfFailed();
            ObjectDisposedException.ThrowIf(_isClosed, this);

            if (_providerLease is not null)
            {
                throw RecordFailure(new InvalidOperationException("A prepared source provider cannot acquire or reenter its source operation."));
            }

            // One source use includes its synchronous span lease and its release control.
            _storage.ReserveControl(256);
            SourceUse use;

            try
            {
                use = new SourceUse(this);
            }
            catch
            {
                _storage.ReleaseControl(256);
                throw;
            }

            _uses = checked(_uses + 1);
            return use;
        }
    }

    internal void ClaimAttachment(IWorldAttachmentRegistration registration)
    {
        lock (_gate)
        {
            ThrowIfFailed();

            if (!ReferenceEquals(registration, _attachment) || _attachmentRead || (_providerLease is not null))
            {
                throw RecordFailure(new InvalidOperationException("The selected World attachment source can be decoded only once by its captured registration."));
            }

            _attachmentRead = true;
        }
    }

    internal void ClaimResource(IWorldResourceRegistration registration)
    {
        lock (_gate)
        {
            ThrowIfFailed();

            if (!ReferenceEquals(registration, _resource) || _resourceRead || (_providerLease is not null))
            {
                throw RecordFailure(new InvalidOperationException("The selected resource source can be decoded only once by its captured registration."));
            }

            _resourceRead = true;
        }
    }

    internal void Close()
    {
        lock (_gate)
        {
            _isClosed = true;
        }

        TryCleanup();
    }

    internal void EnterProviderRead(WorldRestoreSourceLease lease)
    {
        lock (_gate)
        {
            ThrowIfFailed();

            if (_providerLease is not null)
            {
                throw RecordFailure(new InvalidOperationException("The prepared source provider scope is nonreentrant."));
            }

            _providerLease = lease;
        }
    }

    internal void ExitProviderRead(WorldRestoreSourceLease lease)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_providerLease, lease))
            {
                throw RecordFailure(new InvalidOperationException("The prepared source provider scope has a different issuer."));
            }

            _providerLease = null;
        }
    }

    internal Exception RecordFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        lock (_gate)
        {
            _failure ??= exception;
            return _failure;
        }
    }

    internal IDisposable RetainStorage()
    {
        var sourceUse = AcquireUse();

        try
        {
            return _storage.Retain(sourceUse);
        }
        catch
        {
            sourceUse.Dispose();
            throw;
        }
    }

    internal void ThrowIfFailed()
    {
        lock (_gate)
        {
            if (_failure is { } failure)
            {
                throw failure;
            }
        }
    }

    internal bool TryResolveHistoricalEntity(
        Guid entityId, Guid creationLineageId, Guid creationBranchId, out WorldEntityIdentity identity)
    {
        lock (_gate)
        {
            ThrowIfFailed();
            var history = _history
                ?? throw RecordFailure(new InvalidOperationException("The prepared source history has expired."));
            var assignments = history.AssignmentsByOrdinal;

            if (_historicalOrdinals is null)
            {
                var ordinals = new uint[assignments.Length];

                for (var index = 0; index < ordinals.Length; index++)
                {
                    ordinals[index] = checked((uint)index);
                }

                Array.Sort(ordinals, (left, right) =>
                    assignments[checked((int)left)].StableId.CompareTo(assignments[checked((int)right)].StableId));
                _historicalOrdinals = ordinals;
            }

            var low = 0;
            var high = _historicalOrdinals.Length - 1;

            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                var assignment = assignments[checked((int)_historicalOrdinals[middle])];
                var comparison = assignment.StableId.CompareTo(entityId);

                if (comparison == 0)
                {
                    var branch = history.Branches[assignment.BranchIndex];

                    if ((assignment.Kind == 1) && (branch.BranchId == creationBranchId)
                        && (branch.CreationLineageId == creationLineageId))
                    {
                        identity = new WorldEntityIdentity(history.WorldGuid, creationLineageId, creationBranchId, entityId);
                        return true;
                    }

                    break;
                }

                if (comparison < 0)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            identity = default;
            return false;
        }
    }

    internal void ValidatePreparedSources(
        ImmutableArray<IWorldPreparedResourceSource> resources,
        ImmutableArray<IWorldPreparedAttachmentSource> attachments)
    {
        ThrowIfFailed();

        if (resources.IsDefault || attachments.IsDefault
            || (resources.Length != (_resource is null ? 0 : 1))
            || (attachments.Length != (_attachment is null ? 0 : 1))
            || ((_resource is not null)
                && (!_resourceRead || (resources[0] is null)
                    || (resources[0].ResourceId != _resource.ResourceId)
                    || (resources[0].Schema != _resource.Schema)
                    || (resources[0].PartitionCount != _resource.PartitionCount)))
            || ((_attachment is not null)
                && (!_attachmentRead || (attachments[0] is null)
                    || (attachments[0].Scope != AttachmentScope.World)
                    || (attachments[0].Schema != _attachment.Descriptor.Schema))))
        {
            throw new InvalidOperationException("The prepared source set does not match the exact selected resource and World attachment occurrences.");
        }

        if (_attachment is not null)
        {
            _ = attachments[0].GetAuthor(_attachment, _owner);
        }
    }

    internal void ValidateSource(
        IWorldRestoreSourceOwner owner,
        ReadOnlyMemory<byte> ecsRecord,
        ReadOnlyMemory<byte> scenesRecord)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(owner, _owner) || !_ecsRecord.Equals(ecsRecord) || !_scenesRecord.Equals(scenesRecord))
            {
                throw RecordFailure(new InvalidOperationException("The source lease does not borrow its issuer's exact retained projection."));
            }
        }
    }

    private void ReleaseUse()
    {
        lock (_gate)
        {
            if (_uses <= 0)
            {
                throw new InvalidOperationException("The prepared source has no admitted use to release.");
            }

            _uses--;
        }

        TryCleanup();
    }

    private void TryCleanup()
    {
        lock (_gate)
        {
            if (!_isClosed || (_uses != 0) || _cleanupStarted)
            {
                return;
            }

            _cleanupStarted = true;
            _ecsRecord = default;
            _scenesRecord = default;
            _historicalOrdinals = null;
            _history = null;
        }

        _cleanup();

        lock (s_anchorGate)
        {
            _ = s_anchors.Remove(this);
        }
    }

    internal sealed class SourceUse : IDisposable
    {
        private readonly WorldRestoreSourceControl _control;
        private bool _isDisposed;

        internal SourceUse(WorldRestoreSourceControl control)
        {
            _control = control;
        }

        internal WorldRestoreSourceControl Control
        {
            get
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                return _control;
            }
        }

        public void Dispose()
        {
            lock (_control._gate)
            {
                if (_isDisposed)
                {
                    return;
                }

                _isDisposed = true;
                _control._storage.ReleaseControl(256);
            }

            _control.ReleaseUse();
        }
    }
}
