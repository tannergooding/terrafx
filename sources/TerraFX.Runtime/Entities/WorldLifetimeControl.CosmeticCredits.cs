// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal sealed partial class WorldLifetimeControl
{
    internal void AcceptCosmeticCreditReturn(in WorldCosmeticCreditHandback handback)
    {
        var owner = handback.Credit.Owner
            ?? throw new InvalidOperationException("The cosmetic credit handback is unissued.");
        ValidateCosmeticBinding(owner);
        owner.AcceptCreditReturn(this, handback);
    }

    internal void AcceptCosmeticOwnerReturn(in WorldCosmeticOwnerHandback handback)
    {
        var owner = handback.Owner
            ?? throw new InvalidOperationException("The cosmetic owner handback is unissued.");
        ValidateCosmeticBinding(owner);
        owner.AcceptOwnerReturn(this, handback);
    }

    internal WorldGuidUseControl AcquireCosmeticUse(
        WorldCosmeticCreditOwner owner,
        in WorldCosmeticCredit credit,
        long workspaceLimit)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ValidateCosmeticBinding(owner);
        var journal = owner.BeginAcquisition(this, credit, workspaceLimit);

        try
        {
            var use = AcquireUseCore(workspaceLimit, journal);
            return use.Control;
        }
        catch (Exception failure)
        {
            journal.RecordAcquisitionFailure(failure);
            throw;
        }
        finally
        {
            // The core has returned/unwound. This caller's remaining suffix is
            // still standing-owned; this record does not refund the credit.
            journal.RecordAcquisitionReturned();
        }
    }

    internal bool TryReturnCosmeticCredit(WorldCosmeticCreditOwner owner, in WorldCosmeticCredit credit)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ValidateCosmeticBinding(owner);

        if (!owner.TryPrepareCreditReturn(credit, out var handback))
        {
            return false;
        }

        AcceptCosmeticCreditReturn(handback);
        return true;
    }

    internal bool TryReturnCosmeticOwner(WorldCosmeticCreditOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ValidateCosmeticBinding(owner);

        if (!owner.TryPrepareOwnerReturn(out var handback))
        {
            return false;
        }

        AcceptCosmeticOwnerReturn(handback);
        return true;
    }

    private WorldGuidUse AcquireUseCore(
        long workspaceLimit,
        in WorldCosmeticCreditOwner.AcquisitionJournal journal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workspaceLimit);
        WorldAdmissionDomain domain;
        WorldDomainReservation reservation;
        WorldLineageGrant lineageGrant;
        WorldGuidGrant worldGuidGrant;

        lock (_gate)
        {
            if ((_state != StateActive) || !_registry.IsActive(_worldGuidGrant))
            {
                throw new ObjectDisposedException(nameof(World), "The exact WorldGuid binding is no longer active.");
            }

            domain = _admissionDomain;
            reservation = _domainReservation
                ?? throw new InvalidOperationException("The active World lifetime has no domain reservation.");
            lineageGrant = _lineageGrant;
            worldGuidGrant = _worldGuidGrant;
        }

        journal.ValidateBinding(this, domain, reservation, worldGuidGrant);

        if (!domain.TryReserveWorldGuidUseRaw(
                this,
                reservation,
                workspaceLimit,
                out var rawTicketGeneration,
                out var admittedWorkspaceLimit,
                journal))
        {
            throw new InvalidOperationException("The World admission domain cannot reserve an independent active-use credit.");
        }

        WorldGuidUseControl? control = null;
        var rawLinked = false;

        try
        {
            control = new WorldGuidUseControl(
                _registry,
                domain,
                reservation,
                this,
                worldGuidGrant,
                rawTicketGeneration,
                admittedWorkspaceLimit);
            journal.RecordControlConstructed(control);

            if (!domain.LinkWorldGuidUseRaw(rawTicketGeneration, control, journal))
            {
                throw new InvalidOperationException("The independent use's raw admission ticket could not be linked.");
            }

            rawLinked = true;

            if (!domain.TryIssueWorldGuidUseInterests(
                control,
                lineageGrant,
                reservation,
                out var admissionRefusal,
                journal))
            {
                throw new InvalidOperationException(
                    $"The independent use could not retain its exact World lineage: {admissionRefusal}.");
            }

            if (!_registry.TryAcquireIndependentUse(control, out var registryRefusal, journal))
            {
                throw new InvalidOperationException(
                    $"The exact WorldGuid binding could not issue an independent use: {registryRefusal}.");
            }

            var use = control.CreateCarrier();
            journal.RecordIssued();
            return use;
        }
        catch (Exception failure)
        {
            journal.RecordAcquisitionFailure(failure);

            try
            {
                if (control is null)
                {
                    domain.RollbackWorldGuidUseRaw(rawTicketGeneration, journal);
                }
                else if (rawLinked)
                {
                    if (control.UseCellIndex >= 0)
                    {
                        control.Release(control.CarrierGeneration, journal);
                    }
                    else if (!domain.RollbackUnissuedWorldGuidUse(control, journal))
                    {
                        throw new InvalidOperationException("The failed independent use could not be rolled back.");
                    }
                }
                else
                {
                    domain.RollbackWorldGuidUseRaw(rawTicketGeneration, journal);
                }
            }
            catch (Exception cleanupFailure)
            {
                journal.RecordCleanupFailure(cleanupFailure);
                throw;
            }

            throw;
        }
    }

    private void ValidateCosmeticBinding(WorldCosmeticCreditOwner owner)
    {
        lock (_gate)
        {
            if ((_domainReservation is not { } reservation)
                || !owner.IsBoundTo(this, _admissionDomain, reservation, _worldGuidGrant))
            {
                throw new InvalidOperationException("The cosmetic credit owner does not belong to this exact retained World lifetime.");
            }
        }
    }
}
