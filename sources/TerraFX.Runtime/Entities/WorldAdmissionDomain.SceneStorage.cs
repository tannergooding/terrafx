// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

public sealed partial class WorldAdmissionDomain
{
    private const long WorldCloseFailed = 8;
    private const long WorldCloseRequested = 1;
    private const long WorldCloseReturned = 2;
    private const long WorldCloseSucceeded = 4;
    private const long WorldLineageReleased = 32;
    // Phase flags occupy the low word; the high word binds the elected return thread.
    private const long WorldRetirementElected = 16;

    internal void BeginWorldClose(WorldDomainReservation reservation)
    {
        var accepted = false;

        using (ReentrantGate.EnterScope(ref _growthGate))
        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (IsActiveWorldReservation(reservation))
            {
                ref var interest = ref _interests[reservation.InterestIndex];

                if ((interest.WorldLifetime is not null) && (interest.WorldCloseProgress == 0))
                {
                    interest.WorldCloseProgress = WorldCloseRequested;
                    accepted = true;
                }
            }
        }

        if (!accepted)
        {
            throw new InvalidOperationException("The exact World reservation is not open for closure.");
        }
    }

    internal void CancelSceneReservationConstruction(
        WorldDomainReservation parent,
        WorldSceneStorageReservation? reservation,
        WorldGuidUseControl? useControl,
        long bytes,
        bool restoredCapacity)
    {
        var index = parent.InterestIndex;
        var generation = parent.InterestGeneration;
        var returned = false;

        using (ReentrantGate.EnterScope(ref _growthGate))
        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (IsActiveWorldReservation(parent))
            {
                ref var interest = ref _interests[index];

                if ((interest.SceneConstructorCount > 0)
                    && ((reservation is null)
                        || (ReferenceEquals(reservation.Parent, parent)
                            && reservation.IsConstructing
                            && (reservation.Bytes == bytes))))
                {
                    var returnParts = checked(interest.WorldReturnPartCount + 1);

                    if (useControl is not null)
                    {
                        returned = ReferenceEquals(useControl.DomainReservation, parent)
                            && TryReleaseWorldGuidUseWorkspace(useControl, bytes, reservation);
                    }
                    else if ((interest.PendingSceneConstructionBytes >= bytes)
                        && (interest.SceneConstructionBytes >= bytes))
                    {
                        reservation?.MarkReleased();

                        if (!restoredCapacity)
                        {
                            interest.SceneConstructionBytes -= bytes;
                            interest.Bytes -= bytes;
                            _worldBytes -= bytes;
                            parent.RemoveBytes(bytes);
                        }

                        interest.PendingSceneConstructionBytes -= bytes;
                        returned = true;
                    }

                    if (returned)
                    {
                        interest.SceneConstructorCount--;
                        interest.WorldReturnPartCount = returnParts;
                    }
                }
            }
        }

        if (!returned)
        {
            throw new InvalidOperationException("The uninstalled Scene reservation retains its original construction credit.");
        }

        EndWorldReturnPart(index, generation);
    }

    internal void CompleteWorldClose(WorldDomainReservation parent, bool succeeded)
    {
        var index = parent.InterestIndex;
        var generation = parent.InterestGeneration;
        var accepted = false;

        using (ReentrantGate.EnterScope(ref _growthGate))
        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (IsActiveWorldReservation(parent))
            {
                ref var interest = ref _interests[index];

                if (((interest.WorldCloseProgress & WorldCloseRequested) != 0)
                    && ((interest.WorldCloseProgress & WorldCloseReturned) == 0))
                {
                    var returnParts = checked(interest.WorldReturnPartCount + 1);
                    interest.WorldCloseProgress |= WorldCloseReturned
                        | (succeeded ? WorldCloseSucceeded : WorldCloseFailed);
                    interest.WorldReturnPartCount = returnParts;

                    accepted = true;
                }
            }
        }

        if (!accepted)
        {
            throw new InvalidOperationException("The exact World close invocation was not awaiting its final return.");
        }

        EndWorldReturnPart(index, generation);
    }

    internal void CompleteWorldReturnPart(WorldDomainReservation parent)
        => EndWorldReturnPart(parent.InterestIndex, parent.InterestGeneration);

    internal void ConfigureWorldOwnedStateBudget(
        WorldDomainReservation parent,
        long maximumOwnedStateBytes,
        long otherOwnedStateBytes)
    {
        using (ReentrantGate.EnterScope(ref _growthGate))
        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (IsOpenWorldReservation(parent))
            {
                ref var interest = ref _interests[parent.InterestIndex];
                var sceneBytes = checked(interest.SceneStorageBytes + interest.SceneConstructionBytes);
                var remainingBytes = Math.Max(0L, maximumOwnedStateBytes - otherOwnedStateBytes - sceneBytes);
                parent.SetMaximumAdditionalBytes(checked(parent.Bytes - parent.InitialBytes + remainingBytes));
            }
        }
    }

    internal void EnrollWorld(
        WorldDomainReservation parent,
        WorldLifetimeControl lifetime,
        World world,
        long sceneConstructionBytes,
        in WorldLineageGrant lineage)
    {
        var enrolled = false;

        using (ReentrantGate.EnterScope(ref _growthGate))
        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (IsActiveWorldReservation(parent)
                && ReferenceEquals(lineage.Reservation, parent)
                && lineage.IsOwnedBy(this)
                && ((uint)lineage.Index < (uint)_lineages.Length))
            {
                ref var interest = ref _interests[parent.InterestIndex];
                ref var root = ref _lineages[lineage.Index];

                if ((interest.WorldLifetime is null)
                    && (sceneConstructionBytes >= 0)
                    && (sceneConstructionBytes < interest.Bytes)
                    && (root.Generation == lineage.Generation)
                    && (root.State is StateReserved or StateCommitted)
                    && (root.WorldGuid == lineage.WorldGuid)
                    && (root.IdentityBranchId == lineage.IdentityBranchId)
                    && (root.IssuanceNamespace == lineage.IssuanceNamespace)
                    && (_lineageKeys[lineage.Index * FreshRootLineageKeyCount] == lineage.LineageId)
                    && (root.WorldInterestIndexPlusOne == 0))
                {
                    interest.WorldLifetime = lifetime;
                    interest.WorldCloseCarrier = world;
                    interest.SceneConstructionBytes = sceneConstructionBytes;
                    interest.LineageRootIndex = lineage.Index;
                    interest.LineageRootGeneration = lineage.Generation;
                    root.WorldInterestIndexPlusOne = parent.InterestIndex + 1;
                    root.WorldInterestGeneration = parent.InterestGeneration;
                    enrolled = true;
                }
            }
        }

        if (!enrolled)
        {
            throw new InvalidOperationException("The World cannot enroll its exact original reservation and lifetime.");
        }
    }

    internal (long ConstructionBytes, long StorageBytes) GetSceneReservationBytes(WorldDomainReservation parent)
    {
        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (!IsActiveWorldReservation(parent))
            {
                return default;
            }

            ref var interest = ref _interests[parent.InterestIndex];
            return (interest.SceneConstructionBytes, interest.SceneStorageBytes);
        }
    }

    internal void InitializeSceneConstructionBytes(WorldDomainReservation parent, long bytes)
    {
        var initialized = false;

        using (ReentrantGate.EnterScope(ref _growthGate))
        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (IsOpenWorldReservation(parent))
            {
                ref var interest = ref _interests[parent.InterestIndex];

                if ((interest.WorldLifetime is null)
                    && (interest.SceneConstructionBytes == 0)
                    && (bytes >= 0)
                    && (bytes < interest.Bytes))
                {
                    interest.SceneConstructionBytes = bytes;
                    initialized = true;
                }
            }
        }

        if (!initialized)
        {
            throw new InvalidOperationException("The World construction capacity does not match its original admission.");
        }
    }

    internal bool IsEnrolledWorld(WorldDomainReservation parent)
    {
        using (ReentrantGate.EnterScope(ref _gate))
        {
            return IsActiveWorldReservation(parent)
                && (_interests[parent.InterestIndex].WorldLifetime is not null);
        }
    }

    internal bool IsWorldRetirementElected(WorldDomainReservation parent, WorldLifetimeControl lifetime)
    {
        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (!IsActiveWorldReservation(parent))
            {
                return false;
            }

            ref var interest = ref _interests[parent.InterestIndex];
            return ReferenceEquals(interest.WorldLifetime, lifetime)
                && ((interest.WorldCloseProgress & WorldRetirementElected) != 0)
                && ((int)(interest.WorldCloseProgress >> 32) == Environment.CurrentManagedThreadId);
        }
    }

    internal void ReleaseSceneReservation(WorldDomainReservation parent, WorldSceneStorageReservation reservation)
    {
        var index = parent.InterestIndex;
        var generation = parent.InterestGeneration;
        var returned = false;

        using (ReentrantGate.EnterScope(ref _growthGate))
        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (reservation.IsDisposed)
            {
                return;
            }

            if (IsActiveWorldReservation(parent) && ReferenceEquals(reservation.Parent, parent))
            {
                ref var interest = ref _interests[index];
                var bytes = reservation.Bytes;
                var returnParts = checked(interest.WorldReturnPartCount + 1);

                if (reservation.IsReturningWorkspace && (reservation.WorkspaceUseControl is { } useControl))
                {
                    if (ReferenceEquals(useControl.DomainReservation, parent)
                        && TryReleaseWorldGuidUseWorkspace(useControl, bytes, reservation))
                    {
                        returned = true;
                    }
                }
                else if (reservation.IsReturningStorage
                    && (interest.SceneStorageBytes >= bytes)
                    && (interest.Bytes > bytes)
                    && (_worldBytes >= bytes))
                {
                    var previous = reservation.Previous;
                    var next = reservation.Next;

                    if (((previous is null)
                            ? ReferenceEquals(interest.SceneStorageHead, reservation)
                            : ReferenceEquals(previous.Next, reservation))
                        && ((next is null) || ReferenceEquals(next.Previous, reservation)))
                    {
                        if (previous is null)
                        {
                            interest.SceneStorageHead = next;
                        }
                        else
                        {
                            previous.SetNext(next);
                        }

                        next?.SetPrevious(previous);
                        reservation.MarkReleased();
                        interest.SceneStorageBytes -= bytes;
                        interest.Bytes -= bytes;
                        _worldBytes -= bytes;
                        parent.RemoveBytes(bytes);
                        returned = true;
                    }
                }

                if (returned)
                {
                    interest.WorldReturnPartCount = returnParts;
                }
            }
        }

        if (!returned)
        {
            throw new InvalidOperationException("The Scene receiver could not return its exact original reservation.");
        }

        EndWorldReturnPart(index, generation);
    }

    internal void SetSceneReservationOwner(
        WorldDomainReservation parent,
        WorldSceneStorageReservation reservation,
        object owner)
    {
        var retained = false;

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (IsActiveWorldReservation(parent)
                && ReferenceEquals(reservation.Parent, parent)
                && (reservation.IsStorage || reservation.IsWorkspace))
            {
                reservation.SetRetainedOwnerCore(owner);
                retained = true;
            }
        }

        if (!retained)
        {
            throw new InvalidOperationException("The Scene receiver no longer owns its original storage credit.");
        }
    }

    internal bool TryClaimSceneReservationConstruction(
        WorldDomainReservation parent,
        WorldGuidUseControl? useControl,
        long bytes,
        bool useRestoredCapacity)
    {
        using (ReentrantGate.EnterScope(ref _growthGate))
        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (!IsOpenWorldReservation(parent))
            {
                return false;
            }

            ref var interest = ref _interests[parent.InterestIndex];

            if ((interest.WorldLifetime is null) || (interest.SceneConstructorCount == int.MaxValue))
            {
                return false;
            }

            if (useControl is not null)
            {
                if (!ReferenceEquals(useControl.Lifetime, interest.WorldLifetime)
                    || !ReferenceEquals(useControl.DomainReservation, parent)
                    || !TryReserveWorldGuidUseWorkspace(useControl, bytes))
                {
                    return false;
                }
            }
            else
            {
                if (useRestoredCapacity)
                {
                    if (bytes > interest.SceneConstructionBytes - interest.PendingSceneConstructionBytes)
                    {
                        return false;
                    }
                }
                else
                {
                    if (!TryGrowWorldReservation(parent, bytes))
                    {
                        return false;
                    }

                    interest.SceneConstructionBytes = checked(interest.SceneConstructionBytes + bytes);
                }

                interest.PendingSceneConstructionBytes = checked(interest.PendingSceneConstructionBytes + bytes);
            }

            interest.SceneConstructorCount++;
            return true;
        }
    }

    internal bool TryCompleteSceneReservationConstruction(
        WorldDomainReservation parent,
        WorldSceneStorageReservation reservation,
        bool workspace)
    {
        using (ReentrantGate.EnterScope(ref _growthGate))
        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (!workspace)
            {
                return TryInstallSceneStorage(parent, reservation, constructing: true);
            }

            if (!IsOpenWorldReservation(parent)
                || !ReferenceEquals(reservation.Parent, parent)
                || !reservation.IsConstructing
                || (reservation.WorkspaceUseControl is null))
            {
                return false;
            }

            ref var interest = ref _interests[parent.InterestIndex];

            if (interest.SceneConstructorCount <= 0)
            {
                return false;
            }

            var constructors = checked(interest.SceneConstructorCount - 1);
            interest.SceneConstructorCount = constructors;
            reservation.MarkWorkspace();
            return true;
        }
    }

    internal bool TryCompleteWorldLineage(
        WorldDomainReservation parent,
        WorldLifetimeControl lifetime,
        in WorldLineageGrant grant)
    {
        using (ReentrantGate.EnterScope(ref _growthGate))
        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (!IsWorldRetirementElected(parent, lifetime))
            {
                return false;
            }

            ref var interest = ref _interests[parent.InterestIndex];

            if ((interest.WorldCloseProgress & WorldLineageReleased) != 0)
            {
                return true;
            }

            if (!TryRollbackFreshLineage(grant) && !TryReleaseFreshLineage(grant))
            {
                return false;
            }

            interest.WorldCloseProgress |= WorldLineageReleased;
            return true;
        }
    }

    internal bool TryInstallSceneWorkspace(WorldDomainReservation parent, WorldSceneStorageReservation reservation)
    {
        using (ReentrantGate.EnterScope(ref _growthGate))
        using (ReentrantGate.EnterScope(ref _gate))
        {
            return TryInstallSceneStorage(parent, reservation, constructing: false);
        }
    }

    internal bool TryReleaseUnpublishedWorldLineage(
        WorldDomainReservation parent,
        WorldLifetimeControl lifetime,
        in WorldLineageGrant lineage)
    {
        using (ReentrantGate.EnterScope(ref _growthGate))
        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (!IsActiveWorldReservation(parent)
                || lifetime.HasCommittedHistory
                || !ReferenceEquals(lineage.Reservation, parent)
                || ((uint)lineage.Index >= (uint)_lineages.Length)
                || (_lineages[lineage.Index].State != StateReserved))
            {
                return false;
            }

            ref var interest = ref _interests[parent.InterestIndex];

            if (!ReferenceEquals(interest.WorldLifetime, lifetime)
                || (interest.WorldCloseProgress != WorldCloseRequested)
                || (interest.SceneConstructionBytes != 0)
                || (interest.SceneStorageBytes != 0)
                || (interest.PendingSceneConstructionBytes != 0)
                || (interest.SceneConstructorCount != 0)
                || (interest.WorldReturnPartCount != 0)
                || (parent.IndependentUseBytes != 0))
            {
                return false;
            }

            interest.WorldCloseProgress |= WorldRetirementElected | ((long)Environment.CurrentManagedThreadId << 32);

            try
            {
                var released = TryReleaseFreshLineage(lineage);
                interest.WorldCloseProgress = WorldCloseRequested | (released ? WorldLineageReleased : 0);
                return released;
            }
            catch
            {
                interest.WorldCloseProgress = WorldCloseRequested | WorldCloseFailed;
                throw;
            }
        }
    }

    internal bool TryReopenWorldClose(WorldDomainReservation parent)
    {
        using (ReentrantGate.EnterScope(ref _growthGate))
        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (!IsActiveWorldReservation(parent))
            {
                return false;
            }

            ref var interest = ref _interests[parent.InterestIndex];

            if (interest.WorldCloseProgress != WorldCloseRequested)
            {
                return false;
            }

            interest.WorldCloseProgress = 0;
            return true;
        }
    }

    internal bool TryRetainWorldGuidUseReturnPart(
        WorldGuidUseControl control,
        out WorldDomainReservation? returnParent)
    {
        returnParent = null;

        using (ReentrantGate.EnterScope(ref _growthGate))
        using (ReentrantGate.EnterScope(ref _gate))
        {
            var anchor = control.Anchor;
            var parent = control.DomainReservation;

            if (!ReferenceEquals(control.Domain, this)
                || !control.IsReleasing
                || ((uint)anchor.OperationInterestIndex >= (uint)_interests.Length)
                || (anchor.OperationInterestGeneration == 0)
                || (anchor.OperationBytes < WorldGuidUseControl.ControlByteCharge)
                || (parent.IndependentUseBytes < anchor.OperationBytes))
            {
                return false;
            }

            ref var operation = ref _interests[anchor.OperationInterestIndex];

            if ((operation.Generation != anchor.OperationInterestGeneration)
                || (operation.State != StateOccupied)
                || (operation.Kind != InterestKindUseOperation)
                || (operation.Bytes != anchor.OperationBytes)
                || !ReferenceEquals(operation.Reservation, parent)
                || !ReferenceEquals(operation.UseControl, control))
            {
                return false;
            }

            if (!IsActiveWorldReservation(parent))
            {
                return ((uint)anchor.HistoryRootIndex < (uint)_lineages.Length)
                    && (_lineages[anchor.HistoryRootIndex].Generation == anchor.HistoryRootGeneration)
                    && (_lineages[anchor.HistoryRootIndex].WorldInterestIndexPlusOne == 0);
            }

            ref var world = ref _interests[parent.InterestIndex];

            if (world.WorldLifetime is null)
            {
                return true;
            }

            if (!ReferenceEquals(world.WorldLifetime, control.Lifetime)
                || (operation.LineageRootIndex != world.LineageRootIndex)
                || (operation.LineageRootGeneration != world.LineageRootGeneration)
                || (world.WorldReturnPartCount == int.MaxValue))
            {
                return false;
            }

            world.WorldReturnPartCount++;
            returnParent = parent;
            return true;
        }
    }

    internal bool TryRetainWorldReturnPart(WorldDomainReservation parent)
    {
        using (ReentrantGate.EnterScope(ref _growthGate))
        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (!IsOpenWorldReservation(parent))
            {
                return false;
            }

            ref var interest = ref _interests[parent.InterestIndex];

            if ((interest.WorldLifetime is null) || (interest.WorldReturnPartCount == int.MaxValue))
            {
                return false;
            }

            interest.WorldReturnPartCount++;
            return true;
        }
    }

    private bool BeginUseReturnPart(WorldDomainReservation parent, out int index, out uint generation)
    {
        index = parent.InterestIndex;
        generation = parent.InterestGeneration;

        if (!IsActiveWorldReservation(parent) || (_interests[index].WorldLifetime is null))
        {
            return false;
        }

        _interests[index].WorldReturnPartCount = checked(_interests[index].WorldReturnPartCount + 1);
        return true;
    }

    private void EndWorldReturnPart(int index, uint generation)
    {
        WorldDomainReservation? parent = null;
        WorldLifetimeControl? lifetime = null;
        var ended = false;

        using (ReentrantGate.EnterScope(ref _growthGate))
        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (((uint)index < (uint)_interests.Length)
                && (_interests[index].Generation == generation)
                && (_interests[index].State == StateOccupied)
                && (_interests[index].Kind == InterestKindWorld)
                && (_interests[index].WorldReturnPartCount > 0))
            {
                ref var interest = ref _interests[index];
                interest.WorldReturnPartCount--;
                ended = true;

                if (((interest.WorldCloseProgress & ~WorldLineageReleased) == (WorldCloseRequested | WorldCloseReturned | WorldCloseSucceeded))
                    && (interest.SceneConstructorCount == 0)
                    && (interest.PendingSceneConstructionBytes == 0)
                    && (interest.Reservation?.IndependentUseBytes == 0))
                {
                    // With no constructing caller or original use left, only unused
                    // prospective restore capacity remains. Keep its bytes in B.
                    interest.SceneConstructionBytes = 0;
                }

                if ((interest.WorldReturnPartCount == 0)
                    && (interest.SceneConstructorCount == 0)
                    && (interest.SceneConstructionBytes == 0)
                    && (interest.SceneStorageBytes == 0)
                    && ((interest.WorldCloseProgress & ~WorldLineageReleased) == (WorldCloseRequested | WorldCloseReturned | WorldCloseSucceeded))
                    && (interest.Reservation is { } reservation)
                    && (reservation.IndependentUseBytes == 0))
                {
                    interest.WorldCloseProgress |= WorldRetirementElected | ((long)Environment.CurrentManagedThreadId << 32);
                    parent = reservation;
                    lifetime = interest.WorldLifetime;
                }
            }
        }

        if (!ended)
        {
            throw new InvalidOperationException("The exact World return part was already ended or belongs to another interest.");
        }

        if ((parent is not null) && (lifetime is not null))
        {
            FinishWorldRetirement(parent, lifetime);
        }
    }

    private void FinishWorldRetirement(WorldDomainReservation parent, WorldLifetimeControl lifetime)
    {
        if (!lifetime.CompleteRetirement() || !lifetime.IsRetired)
        {
            using (ReentrantGate.EnterScope(ref _gate))
            {
                _interests[parent.InterestIndex].WorldCloseProgress |= WorldCloseFailed;
            }

            throw new InvalidOperationException("The original World retirement retains its unresolved reservation and owner.");
        }

        var completeRetirement = false;
        var released = false;

        using (ReentrantGate.EnterScope(ref _growthGate))
        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (IsWorldRetirementElected(parent, lifetime))
            {
                ref var interest = ref _interests[parent.InterestIndex];
                var bytes = interest.Bytes;

                if (((interest.WorldCloseProgress & (WorldCloseFailed | WorldLineageReleased)) == WorldLineageReleased)
                    && (interest.SceneConstructionBytes == 0)
                    && (interest.SceneStorageBytes == 0)
                    && (interest.SceneConstructorCount == 0)
                    && (interest.PendingSceneConstructionBytes == 0)
                    && (interest.WorldReturnPartCount == 0)
                    && (parent.IndependentUseBytes == 0)
                    && (bytes > 0)
                    && (_worldBytes >= bytes)
                    && (_activeInterestCount > 0))
                {
                    parent.MarkReleased(this);
                    ReleaseInterestEntry(ref interest);
                    _worldBytes -= bytes;
                    _activeInterestCount--;
                    released = true;
                    completeRetirement = _isDisposed && (_activeInterestCount == 0) && (_activeOperationCreditCount == 0);

                    if (completeRetirement)
                    {
                        ReleaseRegistry();
                    }
                }
                else
                {
                    interest.WorldCloseProgress |= WorldCloseFailed;
                }
            }
        }

        if (!released)
        {
            throw new InvalidOperationException("The original World reservation retains unresolved final-return ownership.");
        }

        if (completeRetirement)
        {
            PublishRetirement();
        }
    }

    private bool IsOpenWorldReservation(WorldDomainReservation parent)
        => IsActiveWorldReservation(parent)
            && (_terminalAnchorState == TerminalAnchorStateNone)
            && (_interests[parent.InterestIndex].WorldCloseProgress == 0);

    private bool MayReleaseWorldLineage(in WorldLineageGrant grant)
    {
        if ((uint)grant.Index >= (uint)_lineages.Length)
        {
            return false;
        }

        ref var root = ref _lineages[grant.Index];

        if (root.WorldInterestIndexPlusOne == 0)
        {
            return true;
        }

        var parent = grant.Reservation;
        return (parent is not null)
            && IsActiveWorldReservation(parent)
            && (root.WorldInterestIndexPlusOne == parent.InterestIndex + 1)
            && (root.WorldInterestGeneration == parent.InterestGeneration)
            && (_interests[parent.InterestIndex].LineageRootIndex == grant.Index)
            && (_interests[parent.InterestIndex].LineageRootGeneration == grant.Generation)
            && (_interests[parent.InterestIndex].WorldLifetime is { } lifetime)
            && IsWorldRetirementElected(parent, lifetime);
    }

    private bool TryInstallSceneStorage(
        WorldDomainReservation parent,
        WorldSceneStorageReservation reservation,
        bool constructing)
    {
        if (!IsOpenWorldReservation(parent)
            || !ReferenceEquals(reservation.Parent, parent)
            || (constructing ? !reservation.IsConstructing : !reservation.IsWorkspace)
            || (reservation.Previous is not null)
            || (reservation.Next is not null))
        {
            return false;
        }

        ref var world = ref _interests[parent.InterestIndex];
        ref var operation = ref world;
        var bytes = reservation.Bytes;
        var parentBytes = parent.Bytes;
        var independentUseBytes = parent.IndependentUseBytes;
        var constructors = world.SceneConstructorCount;
        var constructionBytes = world.SceneConstructionBytes;
        var pendingConstructionBytes = world.PendingSceneConstructionBytes;
        var storageBytes = world.SceneStorageBytes;
        var next = world.SceneStorageHead;
        var useControl = reservation.WorkspaceUseControl;
        var activeWorkspaceBytes = 0L;
        var operationBytes = 0L;

        if ((world.WorldLifetime is null)
            || (bytes <= WorldSceneStorageReservation.ControlByteCharge)
            || (world.Bytes != parentBytes)
            || (storageBytes < 0)
            || (constructionBytes < 0)
            || (pendingConstructionBytes < 0)
            || (pendingConstructionBytes > constructionBytes)
            || (checked(storageBytes + constructionBytes) > parentBytes)
            || (independentUseBytes < 0)
            || (checked(parentBytes + independentUseBytes) > _worldBytes)
            || ((next is not null)
                && (!ReferenceEquals(next.Parent, parent)
                    || (next.Previous is not null)
                    || !(next.IsStorage || next.IsReturningStorage))))
        {
            return false;
        }

        if (constructing)
        {
            if (constructors <= 0)
            {
                return false;
            }

            constructors = checked(constructors - 1);
        }

        storageBytes = checked(storageBytes + bytes);

        if (useControl is not null)
        {
            var anchor = useControl.Anchor;

            if (!ReferenceEquals(useControl.Domain, this)
                || !ReferenceEquals(useControl.DomainReservation, parent)
                || !ReferenceEquals(useControl.Lifetime, world.WorldLifetime)
                || !useControl.IsActive
                || ((uint)anchor.OperationInterestIndex >= (uint)_interests.Length)
                || (anchor.OperationInterestGeneration == 0)
                || (anchor.ActiveWorkspaceBytes < bytes)
                || (anchor.WorkspaceLimit < 0)
                || (anchor.OperationBytes != checked(WorldGuidUseControl.ControlByteCharge + anchor.WorkspaceLimit + anchor.ActiveWorkspaceBytes))
                || (independentUseBytes < anchor.OperationBytes))
            {
                return false;
            }

            operation = ref _interests[anchor.OperationInterestIndex];

            if ((operation.Generation != anchor.OperationInterestGeneration)
                || (operation.State != StateOccupied)
                || (operation.Kind != InterestKindUseOperation)
                || (operation.Bytes != anchor.OperationBytes)
                || !ReferenceEquals(operation.Reservation, parent)
                || !ReferenceEquals(operation.UseControl, useControl)
                || (operation.LineageRootIndex != world.LineageRootIndex)
                || (operation.LineageRootGeneration != world.LineageRootGeneration))
            {
                return false;
            }

            parentBytes = checked(parentBytes + bytes);
            independentUseBytes = checked(independentUseBytes - bytes);
            activeWorkspaceBytes = checked(anchor.ActiveWorkspaceBytes - bytes);
            operationBytes = checked(anchor.OperationBytes - bytes);
        }
        else
        {
            if (!constructing
                || (constructionBytes < bytes)
                || (pendingConstructionBytes < bytes))
            {
                return false;
            }

            constructionBytes = checked(constructionBytes - bytes);
            pendingConstructionBytes = checked(pendingConstructionBytes - bytes);
        }

        if (!reservation.TryBeginStorageInstallation(constructing))
        {
            return false;
        }

        // Both gates remain held. All checks/arithmetic precede the election;
        // these commits are only precomputed field stores and atomic intrinsics.
        if (useControl is not null)
        {
            parent.CommitSceneWorkspaceTransfer(parentBytes, independentUseBytes);
            useControl.CommitSceneWorkspaceTransfer(activeWorkspaceBytes, operationBytes);
            operation.Bytes = operationBytes;
        }

        world.Bytes = parentBytes;
        world.SceneConstructionBytes = constructionBytes;
        world.PendingSceneConstructionBytes = pendingConstructionBytes;
        world.SceneStorageBytes = storageBytes;
        world.SceneConstructorCount = constructors;
        next?.SetPrevious(reservation);
        world.SceneStorageHead = reservation;
        reservation.MarkStorage(next);
        return true;
    }
}
