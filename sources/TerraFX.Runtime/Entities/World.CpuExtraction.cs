// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

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

    internal void ReleaseFrameReadLease(WorldFrameReadLease lease)
    {
        ValidateFrameReadLease(lease);
        _activeFrameReadLeaseSequence = 0;
        _isFrameReadLeaseActive = false;
        _ = Interlocked.Exchange(ref _isStructuralOperationActive, 0);
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
}
