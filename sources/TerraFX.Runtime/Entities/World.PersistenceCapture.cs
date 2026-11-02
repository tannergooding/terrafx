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
}
