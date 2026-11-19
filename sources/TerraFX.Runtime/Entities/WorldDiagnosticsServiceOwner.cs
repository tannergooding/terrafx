// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using TerraFX;

namespace TerraFX.Entities;

internal sealed class WorldDiagnosticsServiceOwner
{
    private const int ArrayAlignmentBytes = 16;
    private const long ArrayHeaderBytes = 64;
    private const long ControlByteReservation = 160;
    private const int HostOwnerReferenceCount = 4;

    private AttributionCell[]? _attachments;
    private WorldDiagnosticFundingControl? _baselineFundingControl;
    private readonly DiagnosticsService _diagnostics;
    private readonly Lock _gate = new();
    private bool _isClosed;
    private bool _isClosing;
    private readonly int _maximumPendingAttributionReservations;
    private readonly bool _ownsService;
    private int _pendingAttributionReservations;
    private int _worldUseCount;

    internal WorldDiagnosticsServiceOwner(
        DiagnosticsService diagnostics,
        int maximumRecords,
        int maximumPendingAttributionReservations,
        bool ownsService)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRecords);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumPendingAttributionReservations);

        if (diagnostics.ConfiguredRecordCapacity != maximumRecords)
        {
            throw new ArgumentException("The diagnostic service record capacity does not match its Runtime attribution owner.", nameof(maximumRecords));
        }

        var attachmentCount = checked(maximumRecords + 2 + maximumPendingAttributionReservations);
        var ownedByteReservation = GetOwnedByteReservation(maximumRecords, maximumPendingAttributionReservations);

        if (diagnostics.OwnedByteReservation > diagnostics.MaximumByteReservation)
        {
            throw new ArgumentException("The diagnostic service does not leave room for its bounded Runtime attribution owner.", nameof(diagnostics));
        }

        var attachments = new AttributionCell[attachmentCount];
        _diagnostics = diagnostics;
        _maximumPendingAttributionReservations = maximumPendingAttributionReservations;
        _ownsService = ownsService;
        _attachments = attachments;
        MaximumRecords = maximumRecords;
        OwnedByteReservation = ownedByteReservation;
    }

    internal DiagnosticsService Diagnostics => _diagnostics;

    internal int MaximumRecords { get; }

    internal long OwnedByteReservation { get; }

    internal static ServiceConfiguration GetConfiguration(long maximumServiceBytes, int maximumPendingAttributionReservations)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumPendingAttributionReservations);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumServiceBytes, DiagnosticsService.MaximumServiceByteCapacity);

        var maximumRecords = DiagnosticsService.GetMaximumRecordCount(maximumServiceBytes);

        for (var recordCount = maximumRecords; recordCount > 0; recordCount--)
        {
            var ownerBytes = GetOwnedByteReservation(recordCount, maximumPendingAttributionReservations);
            var diagnosticsMaximumBytes = maximumServiceBytes - ownerBytes;

            if (diagnosticsMaximumBytes < DiagnosticsService.GetMinimumServiceByteReservationForRecordCount(recordCount))
            {
                continue;
            }

            if (DiagnosticsService.GetMaximumRecordCount(diagnosticsMaximumBytes) >= recordCount)
            {
                return new ServiceConfiguration(recordCount, diagnosticsMaximumBytes, ownerBytes);
            }
        }

        throw new InvalidOperationException("The diagnostic service budget cannot fit its bounded Runtime attribution owner and Core storage.");
    }

    internal static long GetOwnedByteReservation(int maximumRecords, int maximumPendingAttributionReservations)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRecords);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumPendingAttributionReservations);

        var attachmentCount = checked((long)maximumRecords + 2 + maximumPendingAttributionReservations);
        var payloadBytes = checked(attachmentCount * Unsafe.SizeOf<AttributionCell>());
        var roundedBytes = checked((payloadBytes + (ArrayAlignmentBytes - 1)) & ~(ArrayAlignmentBytes - 1));
        var hostReferenceBytes = (maximumPendingAttributionReservations == 0)
            ? 0
            : checked(HostOwnerReferenceCount * IntPtr.Size);
        return checked(ControlByteReservation + hostReferenceBytes + ArrayHeaderBytes + roundedBytes);
    }

    private static int FindAttachment(AttributionCell[] attachments, WorldDiagnosticAttributionAccount account)
    {
        for (var index = 0; index < attachments.Length; index++)
        {
            if (ReferenceEquals(attachments[index].Account, account))
            {
                return index;
            }
        }

        return -1;
    }

    internal void CloseCapability(WorldDiagnosticsCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);

        try
        {
            lock (_gate)
            {
                EnsureCapability(capability, fundingControl: null);

                if (_isClosed)
                {
                    capability.Close();
                    return;
                }

                EnsureOpen();
                capability.Close();
            }
        }
        finally
        {
            ReconcileAttachments();
        }
    }

    internal void CloseService()
        => CloseService(closingClaimed: false);

    internal void CompleteAttributionReservation(WorldDiagnosticFundingControl fundingControl)
    {
        ArgumentNullException.ThrowIfNull(fundingControl);

        lock (_gate)
        {
            var attachments = _attachments
                ?? throw new ObjectDisposedException(nameof(WorldDiagnosticsServiceOwner));
            var cellIndex = FindAttachment(attachments, fundingControl.Account);

            if (cellIndex < 0)
            {
                throw new InvalidOperationException("The completed diagnostic preclaim lost its source attachment.");
            }

            ref var cell = ref attachments[cellIndex];

            if ((cell.PendingReservations <= 0) || (_pendingAttributionReservations <= 0))
            {
                throw new InvalidOperationException("The diagnostic service owner completed an inactive source preclaim.");
            }

            cell.PendingReservations--;
            _pendingAttributionReservations--;
        }

        ReconcileAttachments();
    }

    internal void RecordDropped(WorldDiagnosticsCapability capability)
    {
        try
        {
            lock (_gate)
            {
                EnsureOpen();
                EnsureCapability(capability, fundingControl: null);
                capability.RecordDropped();
            }
        }
        finally
        {
            ReconcileAttachments();
        }
    }

    internal void ReleaseWorldUse()
    {
        bool closeService;

        lock (_gate)
        {
            if (_worldUseCount <= 0)
            {
                throw new InvalidOperationException("The diagnostic service owner released more World uses than it acquired.");
            }

            _worldUseCount--;
            closeService = _ownsService && (_worldUseCount == 0);

            if (closeService)
            {
                _isClosing = true;
            }
        }

        if (closeService)
        {
            CloseService(closingClaimed: true);
        }
    }

    internal void RetainWorldUse()
    {
        lock (_gate)
        {
            EnsureOpen();
            _worldUseCount = checked(_worldUseCount + 1);
        }
    }

    internal void SetBaselineFundingControl(WorldDiagnosticFundingControl fundingControl)
    {
        ArgumentNullException.ThrowIfNull(fundingControl);
        fundingControl.RetainUse();

        try
        {
            lock (_gate)
            {
                EnsureOpen();

                if (!_ownsService || (_baselineFundingControl is not null))
                {
                    throw new InvalidOperationException("Only a standalone diagnostic service can acquire one baseline source-funding use.");
                }

                _baselineFundingControl = fundingControl;
            }
        }
        catch
        {
            fundingControl.ReleaseUse();
            throw;
        }
    }

    internal bool TryReserveAttributionPayload(
        WorldDiagnosticFundingControl fundingControl,
        long bytes,
        out DiagnosticAttributionReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(fundingControl);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        reservation = default;
        var pending = false;
        var reserved = false;

        ReconcileAttachments();

        try
        {
            lock (_gate)
            {
                EnsureOpen();

                if (_pendingAttributionReservations >= _maximumPendingAttributionReservations)
                {
                    throw new InvalidOperationException("The diagnostic service exceeded its admitted concurrent attribution-preclaim count.");
                }

                var cellIndex = GetOrCreateAttachment(fundingControl);
                ref var cell = ref (_attachments
                    ?? throw new ObjectDisposedException(nameof(WorldDiagnosticsServiceOwner)))[cellIndex];
                cell.PendingReservations = checked(cell.PendingReservations + 1);
                _pendingAttributionReservations = checked(_pendingAttributionReservations + 1);
                pending = true;
                reserved = _diagnostics.TryReserveAttributionPayload(fundingControl.Account, bytes, out reservation);
            }

            if (!reserved)
            {
                try
                {
                    CompleteAttributionReservation(fundingControl);
                }
                finally
                {
                    pending = false;
                }
            }

            return reserved;
        }
        catch
        {
            if (pending)
            {
                CompleteAttributionReservation(fundingControl);
            }

            throw;
        }
    }

    internal void WriteAttributed(
        WorldDiagnosticsCapability capability,
        WorldDiagnosticFundingControl fundingControl,
        Diagnostic diagnostic)
    {
        ReconcileAttachments();

        try
        {
            lock (_gate)
            {
                EnsureOpen();
                EnsureCapability(capability, fundingControl);
                _ = GetOrCreateAttachment(fundingControl);
                capability.Write(diagnostic);
            }
        }
        finally
        {
            ReconcileAttachments();
        }
    }

    internal bool WriteNoncoalescingWarning(
        WorldDiagnosticFundingControl fundingControl,
        Diagnostic diagnostic,
        ref DiagnosticAttributionReservation reservation)
    {
        try
        {
            lock (_gate)
            {
                EnsureOpen();

                if (!ReferenceEquals(reservation.Account, fundingControl.Account))
                {
                    throw new InvalidOperationException("The diagnostic reservation belongs to another source funding account.");
                }

                return _diagnostics.WriteNoncoalescingWarning(diagnostic, ref reservation);
            }
        }
        finally
        {
            ReconcileAttachments();
        }
    }

    internal void WriteSubscriptionCapacityWarning(
        WorldDiagnosticsCapability capability,
        WorldDiagnosticFundingControl fundingControl)
    {
        ReconcileAttachments();

        try
        {
            lock (_gate)
            {
                EnsureOpen();
                EnsureCapability(capability, fundingControl);
                _ = GetOrCreateAttachment(fundingControl);
                capability.WriteSubscriptionCapacityWarning();
            }
        }
        finally
        {
            ReconcileAttachments();
        }
    }

    internal void WriteUnattributed(Diagnostic diagnostic, Guid? executionSessionId, uint? worldOwnerId)
    {
        try
        {
            lock (_gate)
            {
                EnsureOpen();
                _diagnostics.Write(diagnostic, executionSessionId, worldOwnerId);
            }
        }
        finally
        {
            ReconcileAttachments();
        }
    }

    private void CloseService(bool closingClaimed)
    {
        lock (_gate)
        {
            if (_isClosed)
            {
                return;
            }

            if (_isClosing && !closingClaimed)
            {
                throw new InvalidOperationException("The diagnostic service is already closing.");
            }

            if (_worldUseCount != 0)
            {
                throw new InvalidOperationException("A diagnostic service cannot close while a World still owns a use.");
            }

            if (_pendingAttributionReservations != 0)
            {
                throw new InvalidOperationException("A diagnostic service cannot close while an attribution preclaim is active.");
            }

            _isClosing = true;
        }

        _diagnostics.Close();
        ReconcileAttachments(allowClosing: true);

        WorldDiagnosticFundingControl? baselineFundingControl;

        lock (_gate)
        {
            var attachments = _attachments
                ?? throw new InvalidOperationException("The diagnostic service owner lost its attachment backing.");

            if (_pendingAttributionReservations != 0)
            {
                throw new InvalidOperationException("The diagnostic service closed with active attribution preclaims.");
            }

            for (var index = 0; index < attachments.Length; index++)
            {
                if (attachments[index].FundingControl is not null)
                {
                    throw new InvalidOperationException("The diagnostic service closed with a retained source attachment.");
                }
            }

            _isClosed = true;
            _isClosing = false;
            _attachments = null;
            baselineFundingControl = _baselineFundingControl;
            _baselineFundingControl = null;
        }

        baselineFundingControl?.ReleaseUse();
    }

    private void EnsureCapability(
        WorldDiagnosticsCapability capability,
        WorldDiagnosticFundingControl? fundingControl)
    {
        ArgumentNullException.ThrowIfNull(capability);

        if (!ReferenceEquals(capability.Diagnostics, _diagnostics))
        {
            throw new ArgumentException("The diagnostics capability belongs to another service owner.", nameof(capability));
        }

        if ((fundingControl is not null)
            && !ReferenceEquals(capability.AttributionAccount, fundingControl.Account))
        {
            throw new ArgumentException("The diagnostics capability and funding control identify different source accounts.", nameof(fundingControl));
        }
    }

    private void EnsureOpen()
        => ObjectDisposedException.ThrowIf(_isClosed || _isClosing, this);

    private int GetOrCreateAttachment(WorldDiagnosticFundingControl fundingControl)
    {
        ArgumentNullException.ThrowIfNull(fundingControl);
        var attachments = _attachments
            ?? throw new ObjectDisposedException(nameof(WorldDiagnosticsServiceOwner));
        var account = fundingControl.Account;
        var existingIndex = FindAttachment(attachments, account);

        if (existingIndex >= 0)
        {
            if (!ReferenceEquals(attachments[existingIndex].FundingControl, fundingControl))
            {
                throw new InvalidOperationException("One diagnostic attribution account was paired with multiple funding controls.");
            }

            return existingIndex;
        }

        for (var index = 0; index < attachments.Length; index++)
        {
            if (attachments[index].FundingControl is null)
            {
                fundingControl.RetainUse();
                attachments[index] = new AttributionCell(account, fundingControl);
                return index;
            }
        }

        throw new InvalidOperationException("The admitted diagnostic source-attachment capacity was exhausted.");
    }

    private void ReconcileAttachments(bool allowClosing = false)
    {
        var attachments = Volatile.Read(ref _attachments);

        if (attachments is null)
        {
            return;
        }

        for (var index = 0; index < attachments.Length; index++)
        {
            WorldDiagnosticFundingControl? releasedFundingControl = null;

            lock (_gate)
            {
                if (_isClosing && !allowClosing)
                {
                    return;
                }

                attachments = _attachments;

                if (attachments is null)
                {
                    return;
                }

                ref var cell = ref attachments[index];

                if ((cell.FundingControl is not null)
                    && (cell.PendingReservations == 0)
                    && !_diagnostics.RetainsAttributionAccount(cell.Account
                        ?? throw new InvalidOperationException("A live source attachment has no attribution account.")))
                {
                    releasedFundingControl = cell.FundingControl;
                    cell = default;
                }
            }

            releasedFundingControl?.ReleaseUse();
        }
    }

    internal readonly record struct ServiceConfiguration(
        int MaximumRecords,
        long DiagnosticsMaximumBytes,
        long OwnerByteReservation);

    private struct AttributionCell
    {
        internal WorldDiagnosticAttributionAccount? Account;

        internal WorldDiagnosticFundingControl? FundingControl;

        internal int PendingReservations;

        internal AttributionCell(
            WorldDiagnosticAttributionAccount account,
            WorldDiagnosticFundingControl fundingControl)
        {
            Account = account;
            FundingControl = fundingControl;
            PendingReservations = 0;
        }
    }
}
