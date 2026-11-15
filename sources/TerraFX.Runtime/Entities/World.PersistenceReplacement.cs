// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

public sealed partial class World
{
    private IDisposable? _restoreLifetimeLease;

    internal bool IsHostedWorld => !_ownsWorkerPool;

    internal void AdoptRestoreLifetimeLease(IDisposable lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ThrowIfDisposed();
        EnsureOwnerThread();

        if (!_isRestoreCandidate || !_isCandidateInitialized || (_committedPublication is not null) || (_restoreLifetimeLease is not null))
        {
            throw new InvalidOperationException("The unpublished restore candidate cannot adopt this lifetime lease.");
        }

        _restoreLifetimeLease = lease;
    }

    internal void EnsureLoadCommitOwnerThread()
        => EnsureOwnerThread();

    internal CommittedWorldPublication PrepareRestoredPublication()
    {
        ThrowIfDisposed();
        ThrowIfFaulted();
        EnsureOwnerThread();

        if (!_isRestoreCandidate
            || !_isCandidateInitialized
            || !_isRestoreEcsRowsRestored
            || !_isRestoreHierarchyInstalled
            || !_isRestoreClockStateRestored
            || (_committedPublication is not null)
            || (_nextTickIndex < 1))
        {
            throw new InvalidOperationException("Only a complete unpublished restore candidate can prepare its restored marker.");
        }

        var frameCohort = _currentFrameCohort;
        return new CommittedWorldPublication(
            _executionSessionId,
            _ownerId,
            _publicationSequence,
            _worldGuid,
            checked(_nextTickIndex - 1),
            _nextTickIndex,
            frameCohort.Frame3,
            frameCohort.Frame2,
            [],
            []);
    }

    internal bool TryCaptureLoadReplacement(
        WorldGuidReplacementPreparation preparation,
        out WorldDomainOperationCreditBundle? operationCredits,
        out WorldGuidRefusal bindingRefusal,
        out WorldAdmissionRefusal admissionRefusal)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        operationCredits = null;

        lock (_sceneStorageGate)
        {
            lock (_persistenceAdmissionGate)
            {
                lock (_ownerControlGate)
                {
                    if (Volatile.Read(ref _disposed) != 0
                        || !preparation.IsBoundTo(_registry, _worldGuidGrant))
                    {
                        bindingRefusal = new WorldGuidRefusal(WorldGuidRefusal.TargetBindingStale);
                        admissionRefusal = WorldAdmissionRefusal.None;
                        return false;
                    }

                    if (!preparation.TryCaptureCommit(out bindingRefusal, out admissionRefusal))
                    {
                        return false;
                    }

                    operationCredits = preparation.TransferCapturedCredits();
                    return true;
                }
            }
        }
    }

    internal bool TryCommitLoadReplacement(
        World replacementWorld,
        CommittedWorldPublication restoredPublication,
        WorldGuidReplacementPreparation preparation,
        WorldDomainOperationCreditBundle operationCredits,
        out WorldGuidRefusal bindingRefusal)
    {
        ArgumentNullException.ThrowIfNull(replacementWorld);
        ArgumentNullException.ThrowIfNull(restoredPublication);
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(operationCredits);

        lock (_sceneStorageGate)
        {
            lock (_persistenceAdmissionGate)
            {
                lock (_ownerControlGate)
                {
                    if (Volatile.Read(ref _disposed) != 0
                        || !preparation.IsBoundTo(_registry, _worldGuidGrant))
                    {
                        bindingRefusal = new WorldGuidRefusal(WorldGuidRefusal.TargetBindingStale);
                        return false;
                    }

                    if (ReferenceEquals(this, replacementWorld)
                        || !ReferenceEquals(_admissionDomain, replacementWorld._admissionDomain)
                        || (replacementWorld._ownerThreadId != _ownerThreadId)
                        || (Volatile.Read(ref replacementWorld._disposed) != 0)
                        || !replacementWorld._isRestoreCandidate
                        || !replacementWorld._isRestoreEcsRowsRestored
                        || !replacementWorld._isRestoreHierarchyInstalled
                        || !replacementWorld._isRestoreClockStateRestored
                        || (replacementWorld._committedPublication is not null)
                        || replacementWorld._ownsAdmissionDomain
                        || (restoredPublication.WorldGuid != replacementWorld._worldGuid)
                        || (restoredPublication.WorldOwnerId != replacementWorld._ownerId)
                        || (restoredPublication.ExecutionSessionId != replacementWorld._executionSessionId)
                        || (restoredPublication.PublicationSequence != replacementWorld._publicationSequence)
                        || (restoredPublication.CompletedTick != checked(replacementWorld._nextTickIndex - 1))
                        || (restoredPublication.NextTickIndex != replacementWorld._nextTickIndex)
                        || !restoredPublication.Frame3.Equals(replacementWorld._currentFrameCohort.Frame3)
                        || !Nullable.Equals(restoredPublication.Frame2, replacementWorld._currentFrameCohort.Frame2))
                    {
                        throw new InvalidOperationException("The candidate, marker, or admission-domain handoff is not ready for replacement.");
                    }

                    if (!preparation.TryCommitWithCredits(
                            operationCredits,
                            out _,
                            replacementWorld._lineageGrant))
                    {
                        bindingRefusal = new WorldGuidRefusal(WorldGuidRefusal.TargetBindingStale);
                        return false;
                    }

                    if (_ownsAdmissionDomain)
                    {
                        _ownsAdmissionDomain = false;
                        replacementWorld._ownsAdmissionDomain = true;
                    }

                    replacementWorld._committedMutationRevision =
                        Volatile.Read(ref replacementWorld._canonicalMutationRevision);
                    replacementWorld._committedOwnerRequests = replacementWorld._completedOwnerRequests;
                    Volatile.Write(ref replacementWorld._committedPublication, restoredPublication);
                    bindingRefusal = default;
                    return true;
                }
            }
        }
    }
}
