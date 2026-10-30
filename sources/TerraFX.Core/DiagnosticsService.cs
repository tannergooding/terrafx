// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace TerraFX;

/// <summary>Provides bounded, copied diagnostic polling and independent fault retention.</summary>
public sealed class DiagnosticsService
{
    private const int MaximumRecords = 8192;
    private const string OccurrenceCountKey = "OccurrenceCount";
    private const long ServiceControlBytes = 256;

    private long _coalescedCount;
    private int _count;
    private long _droppedCount;
    private ReentrantGate _gate;
    private int _head;
    private Diagnostic? _latestFault;
    private readonly int _maximumRecords;
    private DiagnosticEntry[]? _records;

    /// <summary>Initializes an empty diagnostic service.</summary>
    internal DiagnosticsService()
        : this(MaximumRecords)
    {
    }

    internal DiagnosticsService(int maximumRecords)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRecords);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumRecords, MaximumRecords);

        DiagnosticClockDomain.EnsureInitialized();
        _maximumRecords = maximumRecords;
        _records = new DiagnosticEntry[maximumRecords];
    }

    internal long OwnedByteReservation => GetOwnedByteReservation(_maximumRecords);

    internal static int GetMaximumRecordCount(long maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 4096);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumBytes, 8L * 1024 * 1024);

        for (var count = MaximumRecords; count > 0; count--)
        {
            if (GetOwnedByteReservation(count) <= maximumBytes)
            {
                return count;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(maximumBytes), "The diagnostic byte budget cannot retain one record.");
    }

    internal static long GetOwnedByteReservation(int maximumRecords)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRecords);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumRecords, MaximumRecords);
        return checked(ServiceControlBytes + ArrayCharge(maximumRecords, Unsafe.SizeOf<DiagnosticEntry>()));
    }

    private static Diagnostic AppendOccurrenceCount(Diagnostic source, long occurrenceCount)
    {
        foreach (var context in source.Context)
        {
            if (StringComparer.Ordinal.Equals(context.Key, OccurrenceCountKey))
            {
                throw new ArgumentException("OccurrenceCount is reserved for the diagnostic service.", nameof(source));
            }
        }

        return CopyDiagnostic(source, [.. source.Context, new DiagnosticContextEntry(OccurrenceCountKey, occurrenceCount.ToString(CultureInfo.InvariantCulture))]);
    }

    private static long ArrayCharge(int capacity, int stride)
    {
        var payloadBytes = checked((long)capacity * stride);
        var roundedBytes = checked((payloadBytes + 15L) & ~15L);
        return checked(64L + roundedBytes);
    }

    private static Diagnostic CopyDiagnostic(Diagnostic source, ImmutableArray<DiagnosticContextEntry> context)
        => new(
            source.Code,
            source.Severity,
            source.MonotonicTimestamp,
            source.HostId,
            source.WorldGuid,
            source.DeviceId,
            source.SurfaceId,
            source.DeviceEpoch,
            source.SurfaceEpoch,
            source.SwapchainEpoch,
            source.Tick,
            source.SystemId,
            source.PassId,
            source.AssetId,
            source.CommandIdentity,
            source.Operation,
            source.NativeResult,
            source.Message,
            context);

    private static int FindCoalescedIndex(
        DiagnosticEntry[] records,
        int head,
        int count,
        string code,
        Guid? executionSessionId,
        uint? worldOwnerId,
        int maximumRecords)
    {
        for (var index = 0; index < count; index++)
        {
            var recordIndex = (head + index) % maximumRecords;
            var entry = records[recordIndex];

            if ((entry.Record is not null)
                && (entry.Record.Severity == DiagnosticSeverity.Warning)
                && (entry.ExecutionSessionId == executionSessionId)
                && (entry.WorldOwnerId == worldOwnerId)
                && StringComparer.Ordinal.Equals(entry.Record.Code, code))
            {
                return recordIndex;
            }
        }

        return -1;
    }

    private static bool Matches(Diagnostic diagnostic, DiagnosticQuery query)
        => (diagnostic.Severity >= query.MinimumSeverity)
        && (!query.WorldGuid.HasValue || (diagnostic.WorldGuid == query.WorldGuid))
        && (!query.MinimumTick.HasValue
            || (diagnostic.Tick.HasValue && (diagnostic.Tick.Value >= query.MinimumTick.Value)));

    private static long SaturatingIncrement(long value) => (value == long.MaxValue) ? value : value + 1;

    private static Diagnostic UpdateOccurrenceCount(Diagnostic source, long occurrenceCount)
    {
        var context = ImmutableArray.CreateBuilder<DiagnosticContextEntry>(source.Context.Length);

        for (var index = 0; index < source.Context.Length - 1; index++)
        {
            context.Add(source.Context[index]);
        }

        if (source.Context.IsEmpty || !StringComparer.Ordinal.Equals(source.Context[^1].Key, OccurrenceCountKey))
        {
            throw new InvalidOperationException("A coalesced warning has no service-owned OccurrenceCount entry.");
        }

        context.Add(new DiagnosticContextEntry(OccurrenceCountKey, occurrenceCount.ToString(CultureInfo.InvariantCulture)));
        return CopyDiagnostic(source, context.MoveToImmutable());
    }

    /// <summary>Gets a bounded, nondestructive snapshot of matching diagnostics.</summary>
    /// <param name="query">The explicit severity, World, tick, and record-count filters.</param>
    /// <returns>A copied immutable snapshot in stable admission order.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="query"/> is default or invalid.</exception>
    public DiagnosticSnapshot GetSnapshot(DiagnosticQuery query)
    {
        if ((query.MaximumRecords is < 1 or > MaximumRecords)
            || (query.MinimumSeverity is < DiagnosticSeverity.Trace or > DiagnosticSeverity.Critical)
            || (query.WorldGuid == Guid.Empty)
            || (query.MinimumTick is < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(query));
        }

        using (ReentrantGate.EnterScope(ref _gate))
        {
            var serviceRecords = _records ?? throw new ObjectDisposedException(nameof(DiagnosticsService));
            var matchingCount = 0;

            for (var index = 0; index < _count; index++)
            {
                var entry = serviceRecords[GetRingIndex(index)];

                if (entry.Record is { } record && Matches(record, query))
                {
                    matchingCount++;
                }
            }

            var returnedCount = Math.Min(matchingCount, query.MaximumRecords);
            var skippedCount = matchingCount - returnedCount;
            var records = ImmutableArray.CreateBuilder<Diagnostic>(returnedCount);

            for (var index = 0; index < _count; index++)
            {
                var entry = serviceRecords[GetRingIndex(index)];

                if ((entry.Record is not { } record) || !Matches(record, query))
                {
                    continue;
                }

                if (skippedCount > 0)
                {
                    skippedCount--;
                    continue;
                }

                records.Add(record);
            }

            return new DiagnosticSnapshot(records.MoveToImmutable(), _droppedCount, _coalescedCount);
        }
    }

    /// <summary>Attempts to retrieve the latest independently retained critical fault.</summary>
    /// <param name="diagnostic">The latest critical diagnostic, or null when none is retained.</param>
    /// <returns><see langword="true"/> when a critical fault is retained.</returns>
    public bool TryGetLatestFault([NotNullWhen(true)] out Diagnostic? diagnostic)
    {
        using (ReentrantGate.EnterScope(ref _gate))
        {
            ObjectDisposedException.ThrowIf(_records is null, this);
            diagnostic = _latestFault;
            return diagnostic is not null;
        }
    }

    internal void Close()
    {
        DiagnosticEntry[]? records;

        using (ReentrantGate.EnterScope(ref _gate))
        {
            records = _records;
            _records = null;
            _head = 0;
            _count = 0;
            _latestFault = null;
        }

        if (records is not null)
        {
            Array.Clear(records);
        }
    }

    internal WorldDiagnosticsCapability CreateWorldCapability(Guid worldGuid, Guid executionSessionId)
    {
        ObjectDisposedException.ThrowIf(_records is null, this);
        return new WorldDiagnosticsCapability(this, worldGuid, executionSessionId);
    }

    internal void RecordDropped()
    {
        using (ReentrantGate.EnterScope(ref _gate))
        {
            ObjectDisposedException.ThrowIf(_records is null, this);
            _droppedCount = SaturatingIncrement(_droppedCount);
        }
    }

    internal void Write(Diagnostic diagnostic, Guid? executionSessionId, uint? worldOwnerId)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        if (executionSessionId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(executionSessionId));
        }

        if (worldOwnerId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(worldOwnerId));
        }

        if (diagnostic.Severity == DiagnosticSeverity.Warning)
        {
            foreach (var context in diagnostic.Context)
            {
                if (StringComparer.Ordinal.Equals(context.Key, OccurrenceCountKey))
                {
                    throw new ArgumentException("OccurrenceCount is reserved for the diagnostic service.", nameof(diagnostic));
                }
            }
        }

        using (ReentrantGate.EnterScope(ref _gate))
        {
            var records = _records ?? throw new ObjectDisposedException(nameof(DiagnosticsService));

            if (diagnostic.Severity == DiagnosticSeverity.Critical)
            {
                _latestFault = diagnostic;
            }

            var coalescedIndex = (diagnostic.Severity == DiagnosticSeverity.Warning)
                ? FindCoalescedIndex(records, _head, _count, diagnostic.Code, executionSessionId, worldOwnerId, _maximumRecords)
                : -1;

            if (coalescedIndex >= 0)
            {
                ref var entry = ref records[coalescedIndex];
                var occurrenceCount = SaturatingIncrement(entry.OccurrenceCount);
                entry.Record = UpdateOccurrenceCount(entry.Record!, occurrenceCount);
                entry.OccurrenceCount = occurrenceCount;
                _coalescedCount = SaturatingIncrement(_coalescedCount);
                return;
            }

            var retainedDiagnostic = diagnostic;
            var retainedOccurrenceCount = 0L;

            if (diagnostic.Severity == DiagnosticSeverity.Warning)
            {
                retainedOccurrenceCount = 1;
                retainedDiagnostic = AppendOccurrenceCount(diagnostic, retainedOccurrenceCount);
            }

            if (_count == _maximumRecords)
            {
                records[_head] = default;
                _head = (_head + 1) % _maximumRecords;
                _count--;
                _droppedCount = SaturatingIncrement(_droppedCount);
            }

            var insertIndex = GetRingIndex(_count);
            records[insertIndex] = new DiagnosticEntry(retainedDiagnostic, executionSessionId, worldOwnerId, retainedOccurrenceCount);
            _count++;
        }
    }

    private int GetRingIndex(int chronologicalIndex) => (_head + chronologicalIndex) % _maximumRecords;

    private struct DiagnosticEntry
    {
        internal Guid? ExecutionSessionId;

        internal long OccurrenceCount;

        internal Diagnostic? Record;

        internal uint? WorldOwnerId;

        internal DiagnosticEntry(Diagnostic record, Guid? executionSessionId, uint? worldOwnerId, long occurrenceCount)
        {
            Record = record;
            ExecutionSessionId = executionSessionId;
            WorldOwnerId = worldOwnerId;
            OccurrenceCount = occurrenceCount;
        }
    }
}
