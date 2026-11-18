// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

public sealed partial class WorldAdmissionDomain
{
    internal void CancelSceneReservationConstructionFromPreclaimedWorkspace(
        WorldDomainReservation parent,
        WorldSceneStorageReservation? reservation,
        WorldGuidUseControl useControl,
        long bytes)
    {
        var index = parent.InterestIndex;
        var generation = parent.InterestGeneration;
        var canceled = false;

        using (ReentrantGate.EnterScope(ref _growthGate))
        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (IsActiveWorldReservation(parent))
            {
                ref var interest = ref _interests[index];

                if ((interest.WorldLifetime is not null)
                    && (interest.SceneConstructorCount > 0)
                    && ((reservation is null)
                        || (ReferenceEquals(reservation.Parent, parent)
                            && ReferenceEquals(reservation.WorkspaceUseControl, useControl)
                            && (reservation.Bytes == bytes)
                            && (reservation.IsConstructing || reservation.IsWorkspace))))
                {
                    var returnParts = checked(interest.WorldReturnPartCount + 1);
                    reservation?.MarkReleased();
                    interest.SceneConstructorCount--;
                    interest.WorldReturnPartCount = returnParts;
                    canceled = true;
                }
            }
        }

        if (!canceled)
        {
            throw new InvalidOperationException("The preclaimed Scene construction no longer owns its original constructor part.");
        }

        EndWorldReturnPart(index, generation);
    }

    internal bool TryClaimSceneReservationConstructionFromPreclaimedWorkspace(
        WorldDomainReservation parent,
        WorldGuidUseControl useControl,
        long bytes)
    {
        using (ReentrantGate.EnterScope(ref _growthGate))
        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (!IsOpenWorldReservation(parent))
            {
                return false;
            }

            ref var interest = ref _interests[parent.InterestIndex];

            if ((interest.WorldLifetime is null)
                || (interest.SceneConstructorCount == int.MaxValue)
                || !HasPreclaimedSceneWorkspace(parent, useControl, bytes))
            {
                return false;
            }

            interest.SceneConstructorCount++;
            return true;
        }
    }

    internal bool TryCompleteSceneReservationConstructionFromPreclaimedWorkspace(
        WorldDomainReservation parent,
        WorldSceneStorageReservation reservation,
        WorldGuidUseControl useControl)
    {
        using (ReentrantGate.EnterScope(ref _growthGate))
        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (!IsOpenWorldReservation(parent)
                || !ReferenceEquals(reservation.Parent, parent)
                || !ReferenceEquals(reservation.WorkspaceUseControl, useControl)
                || !reservation.IsConstructing
                || !HasPreclaimedSceneWorkspace(parent, useControl, reservation.Bytes))
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

    private bool HasPreclaimedSceneWorkspace(
        WorldDomainReservation parent,
        WorldGuidUseControl useControl,
        long bytes)
    {
        if (!ReferenceEquals(useControl.Domain, this)
            || !ReferenceEquals(useControl.DomainReservation, parent)
            || !useControl.IsActive)
        {
            return false;
        }

        var anchor = useControl.Anchor;

        if (((uint)anchor.OperationInterestIndex >= (uint)_interests.Length)
            || (anchor.OperationInterestGeneration == 0)
            || (anchor.OperationBytes < WorldGuidUseControl.ControlByteCharge)
            || (anchor.ActiveWorkspaceBytes < bytes)
            || (anchor.OperationBytes != checked(
                WorldGuidUseControl.ControlByteCharge + anchor.WorkspaceLimit + anchor.ActiveWorkspaceBytes))
            || (parent.IndependentUseBytes < anchor.OperationBytes))
        {
            return false;
        }

        ref var world = ref _interests[parent.InterestIndex];

        if (!ReferenceEquals(world.WorldLifetime, useControl.Lifetime)
            || (world.Bytes != parent.Bytes)
            || (checked(parent.Bytes + parent.IndependentUseBytes) > _worldBytes))
        {
            return false;
        }

        ref var operation = ref _interests[anchor.OperationInterestIndex];

        return (operation.Generation == anchor.OperationInterestGeneration)
            && (operation.State == StateOccupied)
            && (operation.Kind == InterestKindUseOperation)
            && (operation.Bytes == anchor.OperationBytes)
            && ReferenceEquals(operation.Reservation, parent)
            && ReferenceEquals(operation.UseControl, useControl)
            && (operation.LineageRootIndex == world.LineageRootIndex)
            && (operation.LineageRootGeneration == world.LineageRootGeneration);
    }
}
