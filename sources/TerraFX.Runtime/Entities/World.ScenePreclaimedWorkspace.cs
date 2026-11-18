// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace TerraFX.Entities;

public sealed partial class World
{
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The domain constructor part transfers the reservation to the caller on completion and marks it released on cancellation.")]
    internal WorldSceneStorageReservation? TryReserveSceneWorkspaceFromPreclaimed(
        IWorldSceneParticipant participant,
        WorldGuidUseControl useControl,
        long dataBytes)
    {
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(useControl);
        var bytes = WorldSceneStorageReservation.GetRequiredBytes(dataBytes);
        var domain = _admissionDomain;
        var parent = _domainReservation;

        lock (_sceneStorageGate)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return null;
            }

            if (!ReferenceEquals(_sceneParticipant, participant))
            {
                throw new ArgumentException("The Scene participant is not owned by this exact World.", nameof(participant));
            }

            if (!domain.TryClaimSceneReservationConstructionFromPreclaimedWorkspace(parent, useControl, bytes))
            {
                return null;
            }
        }

        WorldSceneStorageReservation? reservation = null;
        var completed = false;

        try
        {
            reservation = new WorldSceneStorageReservation(parent, bytes, useControl);
            completed = domain.TryCompleteSceneReservationConstructionFromPreclaimedWorkspace(
                parent,
                reservation,
                useControl);
            return completed ? reservation : null;
        }
        finally
        {
            if (!completed)
            {
                domain.CancelSceneReservationConstructionFromPreclaimedWorkspace(
                    parent,
                    reservation,
                    useControl,
                    bytes);
            }
        }
    }
}
