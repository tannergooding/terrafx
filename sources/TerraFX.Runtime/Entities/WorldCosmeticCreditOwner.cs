// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal sealed class WorldCosmeticCreditOwner
{
    internal const int MaximumCredits = 128;
    internal const long WorkspaceBytes = 65536;

    private readonly Cell[] _cells;
    private readonly WorldAdmissionDomain _domain;
    private readonly WorldGuidGrant _grant;
    private readonly WorldLifetimeControl _lifetime;
    private int _occupiedCount;
    private readonly int _ownerThreadId;
    private readonly WorldGuidRegistry _registry;
    private readonly WorldDomainReservation _reservation;
    private OwnerState _state;

    private WorldCosmeticCreditOwner(
        WorldLifetimeControl lifetime,
        WorldAdmissionDomain domain,
        WorldDomainReservation reservation,
        int ownerThreadId)
    {
        _lifetime = lifetime;
        _domain = domain;
        _reservation = reservation;
        _registry = lifetime.Registry;
        _grant = lifetime.WorldGuidGrant;
        _ownerThreadId = ownerThreadId;
        _cells = new Cell[MaximumCredits];
    }

    // No live World owns/charges this graph yet. Changing this gate also requires
    // the actual lifetime retention edge and its funded construction/return paths.
    private static bool HasQualifiedStandingOwner => false;

    internal static WorldCosmeticCreditOwner CreateCandidate(
        WorldLifetimeControl lifetime,
        WorldAdmissionDomain domain,
        WorldDomainReservation reservation,
        int ownerThreadId)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ownerThreadId);

        if (!HasQualifiedStandingOwner)
        {
            throw new InvalidOperationException(
                "Cosmetic credit storage is a compile-only candidate until standing funding, lifetime retention and caller-return qualification are supplied.");
        }

        return new WorldCosmeticCreditOwner(lifetime, domain, reservation, ownerThreadId);
    }

    internal void AcceptCreditReturn(WorldLifetimeControl lifetime, in WorldCosmeticCreditHandback handback)
    {
        EnsureEntry();
        EnsureReturningOwner(lifetime);
        var credit = handback.Credit;
        ref var cell = ref GetCell(credit);

        if ((cell.State != CellState.ReturnReady)
            || (cell.Debt != DebtKind.None)
            || (_occupiedCount <= 0))
        {
            throw new InvalidOperationException("The cosmetic credit handback is not current and return-ready.");
        }

        var generation = cell.Generation;
        _occupiedCount--;
        cell = new Cell {
            Generation = generation,
            State = (generation == uint.MaxValue) ? CellState.Retired : CellState.Free,
        };
    }

    internal void AcceptOwnerReturn(WorldLifetimeControl lifetime, in WorldCosmeticOwnerHandback handback)
    {
        EnsureEntry();

        if (!ReferenceEquals(lifetime, _lifetime)
            || !ReferenceEquals(handback.Lifetime, lifetime)
            || !ReferenceEquals(handback.Owner, this)
            || (_state != OwnerState.ReturnReady)
            || (_occupiedCount != 0))
        {
            throw new InvalidOperationException("The cosmetic owner handback is not current and return-ready.");
        }

        // This consumes logical ownership only. Its storage remains under the
        // lifetime caller's standing claim through the eventual World close tail.
        _state = OwnerState.Returned;
    }

    internal AcquisitionJournal BeginAcquisition(
        WorldLifetimeControl lifetime,
        in WorldCosmeticCredit credit,
        long workspaceLimit)
    {
        EnsureEntry();

        if (!ReferenceEquals(lifetime, _lifetime)
            || (_state != OwnerState.Open)
            || (workspaceLimit != WorkspaceBytes))
        {
            throw new InvalidOperationException("The cosmetic acquisition requires its open owner and selected workspace request.");
        }

        ref var cell = ref GetCell(credit);

        if (cell.State != CellState.Reserved)
        {
            throw new InvalidOperationException("The cosmetic credit already started an acquisition.");
        }

        cell.State = CellState.Acquiring;
        return new AcquisitionJournal(this, credit.Index, credit.Generation);
    }

    internal void BeginClose()
    {
        EnsureEntry();

        if (_state == OwnerState.Open)
        {
            _state = OwnerState.Closing;
        }
    }

    internal bool IsBoundTo(
        WorldLifetimeControl lifetime,
        WorldAdmissionDomain domain,
        WorldDomainReservation reservation,
        in WorldGuidGrant grant)
        => ReferenceEquals(_lifetime, lifetime)
            && ReferenceEquals(_domain, domain)
            && ReferenceEquals(_reservation, reservation)
            && ReferenceEquals(_registry, lifetime.Registry)
            && _grant.Equals(grant);

    internal bool TryPrepareCreditReturn(in WorldCosmeticCredit credit, out WorldCosmeticCreditHandback handback)
    {
        EnsureEntry();
        EnsureReturningOwner(_lifetime);
        handback = default;
        ref var cell = ref GetCell(credit);

        if (cell.State is CellState.Acquiring or CellState.Returning or CellState.ReturnReady)
        {
            throw new InvalidOperationException("The cosmetic credit is already in an acquisition or return operation.");
        }

        if (cell.State == CellState.Retained)
        {
            return false;
        }

        if (cell.State == CellState.Acquired)
        {
            var control = cell.Control
                ?? throw new InvalidOperationException("The acquired cosmetic credit has no actual control.");
            var anchor = control.Anchor;

            // This candidate has no body/receiver/publisher binding. Do not treat
            // an external retention or workspace transfer as a no-work return.
            if ((anchor.RetainedOwner is not null)
                || (anchor.ActiveWorkspaceBytes != 0)
                || (anchor.OperationBytes != (WorldGuidUseControl.ControlByteCharge + WorkspaceBytes))
                || (control.WorkspaceLimit != WorkspaceBytes))
            {
                return false;
            }

            cell.State = CellState.Returning;
            var journal = new AcquisitionJournal(this, credit.Index, credit.Generation);

            try
            {
                control.Release(cell.CarrierGeneration, journal);
            }
            catch (Exception failure)
            {
                cell.CleanupFailure ??= failure;
                cell.State = CellState.Retained;
                throw;
            }

            if (!control.IsReleased || (cell.Debt != DebtKind.None))
            {
                cell.State = CellState.Retained;
                return false;
            }
        }
        else if (cell.Debt != DebtKind.None)
        {
            return false;
        }

        cell.State = CellState.ReturnReady;
        handback = new WorldCosmeticCreditHandback(credit);
        return true;
    }

    internal bool TryPrepareOwnerReturn(out WorldCosmeticOwnerHandback handback)
    {
        EnsureEntry();
        handback = default;

        if (_state != OwnerState.Closing)
        {
            throw new InvalidOperationException("The cosmetic owner is not awaiting its close handback.");
        }

        if (_occupiedCount != 0)
        {
            return false;
        }

        _state = OwnerState.ReturnReady;
        handback = new WorldCosmeticOwnerHandback(this, _lifetime);
        return true;
    }

    internal bool TryReserve(out WorldCosmeticCredit credit)
    {
        EnsureEntry();
        credit = default;

        if (_state != OwnerState.Open)
        {
            throw new InvalidOperationException("The cosmetic credit owner is closed to admission.");
        }

        for (var index = 0; index < _cells.Length; index++)
        {
            ref var cell = ref _cells[index];

            if (cell.State != CellState.Free)
            {
                continue;
            }

            if (cell.Generation == uint.MaxValue)
            {
                cell.State = CellState.Retired;
                continue;
            }

            var generation = checked(cell.Generation + 1U);
            cell = new Cell { Generation = generation, State = CellState.Reserved };
            _occupiedCount++;
            credit = new WorldCosmeticCredit(this, index, generation);
            return true;
        }

        return false;
    }

    private void EnsureEntry()
    {
        EnsureThread();

        foreach (ref readonly var cell in _cells.AsSpan())
        {
            if (cell.State is CellState.Acquiring or CellState.Returning)
            {
                throw new InvalidOperationException("The cosmetic credit owner does not permit reentrant acquisition or release.");
            }
        }
    }

    private void EnsureReturningOwner(WorldLifetimeControl lifetime)
    {
        if (!ReferenceEquals(lifetime, _lifetime)
            || (_state is not (OwnerState.Open or OwnerState.Closing)))
        {
            throw new InvalidOperationException("The cosmetic credit owner no longer accepts this binding's returns.");
        }
    }

    private void EnsureThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("The cosmetic credit owner requires its World owner thread.");
        }
    }

    private ref Cell GetCell(in WorldCosmeticCredit credit)
    {
        if (!ReferenceEquals(credit.Owner, this)
            || ((uint)credit.Index >= MaximumCredits)
            || (credit.Generation == 0))
        {
            throw new InvalidOperationException("The cosmetic credit is default or belongs to another owner.");
        }

        ref var cell = ref _cells[credit.Index];

        if ((cell.Generation != credit.Generation)
            || (cell.State is CellState.Free or CellState.Retired))
        {
            throw new InvalidOperationException("The cosmetic credit generation is stale or already returned.");
        }

        return ref cell;
    }

    internal readonly struct AcquisitionJournal
    {
        private readonly WorldCosmeticCreditOwner? _owner;
        private readonly int _index;
        private readonly uint _generation;

        internal AcquisitionJournal(WorldCosmeticCreditOwner owner, int index, uint generation)
        {
            _owner = owner;
            _index = index;
            _generation = generation;
        }

        internal void RecordAcquisitionFailure(Exception failure)
        {
            if (_owner is { } owner)
            {
                owner._cells[_index].AcquisitionFailure ??= failure;
            }
        }

        internal void RecordAcquisitionReturned()
        {
            if (_owner is { } owner)
            {
                ref var cell = ref owner._cells[_index];

                if (cell.Issued)
                {
                    cell.State = CellState.Acquired;
                }
                else if ((cell.Debt == DebtKind.None)
                    && (cell.CleanupFailure is null)
                    && (cell.Control?.IsQuarantined != true))
                {
                    cell.State = CellState.AcquisitionFailed;
                }
                else
                {
                    cell.State = CellState.Retained;
                }
            }
        }

        internal void RecordCleanupFailure(Exception failure)
        {
            if (_owner is { } owner)
            {
                owner._cells[_index].CleanupFailure ??= failure;
            }
        }

        internal void RecordControlConstructed(WorldGuidUseControl control)
        {
            if (_owner is { } owner)
            {
                ref var cell = ref owner._cells[_index];
                cell.Control = control;
                cell.CarrierGeneration = control.CarrierGeneration;
            }
        }

        internal void RecordDomainIssued(
            int rootIndex,
            uint rootGeneration,
            int historyIndex,
            uint historyGeneration,
            int operationIndex,
            uint operationGeneration)
        {
            if (_owner is { } owner)
            {
                ref var cell = ref owner._cells[_index];
                cell.HistoryRootIndex = rootIndex;
                cell.HistoryRootGeneration = rootGeneration;
                cell.HistoryInterestIndex = historyIndex;
                cell.HistoryInterestGeneration = historyGeneration;
                cell.OperationInterestIndex = operationIndex;
                cell.OperationInterestGeneration = operationGeneration;
            }
        }

        internal void RecordDomainReleased()
        {
            if (_owner is { } owner)
            {
                owner._cells[_index].Debt &= ~DebtKind.Domain;
            }
        }

        internal void RecordIssued()
        {
            if (_owner is { } owner)
            {
                owner._cells[_index].Issued = true;
            }
        }

        internal void RecordProcessIssued(int index, uint generation)
        {
            if (_owner is { } owner)
            {
                ref var cell = ref owner._cells[_index];
                cell.ProcessUseIndex = index;
                cell.ProcessUseGeneration = generation;
                cell.Debt |= DebtKind.Process;
            }
        }

        internal void RecordProcessReleased()
        {
            if (_owner is { } owner)
            {
                owner._cells[_index].Debt &= ~DebtKind.Process;
            }
        }

        internal void RecordRawLinked()
        {
            if (_owner is { } owner)
            {
                owner._cells[_index].Debt &= ~DebtKind.Raw;
            }
        }

        internal void RecordRawReserved(uint generation)
        {
            if (_owner is { } owner)
            {
                ref var cell = ref owner._cells[_index];
                cell.RawGeneration = generation;
                cell.Debt = DebtKind.Raw | DebtKind.Domain;
            }
        }

        internal void RecordRawRolledBack()
        {
            if (_owner is { } owner)
            {
                owner._cells[_index].Debt &= ~(DebtKind.Raw | DebtKind.Domain);
            }
        }

        internal void ValidateBinding(
            WorldLifetimeControl lifetime,
            WorldAdmissionDomain domain,
            WorldDomainReservation reservation,
            in WorldGuidGrant grant)
        {
            if (_owner is { } owner)
            {
                ValidateCell();

                if (!owner.IsBoundTo(lifetime, domain, reservation, grant))
                {
                    throw new InvalidOperationException("The cosmetic journal does not belong to this acquisition binding.");
                }
            }
        }

        internal void ValidateControl(WorldGuidUseControl control)
        {
            if (_owner is { } owner)
            {
                ValidateBinding(control.Lifetime, control.Domain, control.DomainReservation, control.Grant);

                if (!ReferenceEquals(owner._cells[_index].Control, control))
                {
                    throw new InvalidOperationException("The cosmetic journal does not retain this exact control.");
                }
            }
        }

        internal void ValidateRaw(WorldAdmissionDomain domain, uint generation)
        {
            if (_owner is { } owner)
            {
                ValidateCell();
                ref var cell = ref owner._cells[_index];

                if (!ReferenceEquals(owner._domain, domain)
                    || (cell.RawGeneration != generation)
                    || ((cell.Debt & DebtKind.Raw) == 0))
                {
                    throw new InvalidOperationException("The cosmetic journal does not retain this exact raw ticket.");
                }
            }
        }

        private void ValidateCell()
        {
            var owner = _owner
                ?? throw new InvalidOperationException("The cosmetic acquisition journal is unissued.");
            owner.EnsureThread();
            var credit = new WorldCosmeticCredit(owner, _index, _generation);
            ref var cell = ref owner.GetCell(credit);

            if (cell.State is not (CellState.Acquiring or CellState.Returning))
            {
                throw new InvalidOperationException("The cosmetic journal has no active acquisition or release caller.");
            }
        }
    }

    private struct Cell
    {
        internal uint Generation;
        internal CellState State;
        internal DebtKind Debt;
        internal uint RawGeneration;
        internal WorldGuidUseControl? Control;
        internal uint CarrierGeneration;
        internal int HistoryRootIndex;
        internal uint HistoryRootGeneration;
        internal int HistoryInterestIndex;
        internal uint HistoryInterestGeneration;
        internal int OperationInterestIndex;
        internal uint OperationInterestGeneration;
        internal int ProcessUseIndex;
        internal uint ProcessUseGeneration;
        internal bool Issued;
        internal Exception? AcquisitionFailure;
        internal Exception? CleanupFailure;
    }

    private enum CellState
    {
        Free,
        Reserved,
        Acquiring,
        Acquired,
        AcquisitionFailed,
        Returning,
        ReturnReady,
        Retained,
        Retired,
    }

    [Flags]
    private enum DebtKind
    {
        None = 0,
        Raw = 1,
        Domain = 2,
        Process = 4,
    }

    private enum OwnerState
    {
        Open,
        Closing,
        ReturnReady,
        Returned,
    }
}
