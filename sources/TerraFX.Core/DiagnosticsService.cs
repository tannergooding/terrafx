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
    private const string ActiveSubscriptionsContextKey = "ActiveSubscriptions";
    private const string CompletedFenceContextKey = "CompletedFence";
    private const string DegenerateVertexCountContextKey = "DegenerateVertexCount";
    private const string DiagnosticCode = "DegenerateSkinnedNormal";
    private const string DiagnosticMessage = "Skinned mesh vertices required the dominant-bone normal fallback.";
    private const long DiagnosticObjectBytes = 64 + ((176 + (5 * 8) + 15) & ~15);
    private const string DiagnosticOperation = "Renderer.SubmitAsync";
    private const long DiagnosticRetentionControlBytes = 128;
    private const string DrawIdContextKey = "DrawId";
    private const int MaximumRecords = 8192;
    private const long MaximumServiceBytes = 8L * 1024 * 1024;
    private const string MaximumSubscriptionsContextKey = "MaximumSubscriptions";
    private const string OccurrenceCountKey = "OccurrenceCount";
    private const long ServiceControlBytes = 256;
    private const string SnapshotIdContextKey = "SnapshotId";
    private const string SubmissionSequenceContextKey = "SubmissionSequence";
    private const string SubscriptionCountText = "64";
    private const string TestedVertexCountContextKey = "TestedVertexCount";
    private const string WorldSubscriptionCapacityCode = "WorldSubscriptionCapacityExceeded";
    private const string WorldSubscriptionCapacityMessage = "World subscription capacity (64) reached; subscription was not admitted.";
    private const string WorldSubscriptionCapacityOperation = "TerraFX.Entities.World.TrySubscribe";

    private long _coalescedCount;
    private int _count;
    private long _droppedCount;
    private ReentrantGate _gate;
    private int _head;
    private DiagnosticRetention? _latestFault;
    private readonly long _maximumBytes;
    private readonly int _maximumRecords;
    private long _preparingPayloadBytes;
    private long _preparingSnapshotPayloadBytes;
    private readonly long _protectedSnapshotBytes;
    private DiagnosticEntry[]? _records;
    private long _retainedPayloadBytes;

    /// <summary>Initializes an empty diagnostic service.</summary>
    internal DiagnosticsService()
        : this(MaximumRecords, MaximumServiceBytes)
    {
    }

    internal DiagnosticsService(int maximumRecords)
        : this(maximumRecords, MaximumServiceBytes)
    {
    }

    internal DiagnosticsService(int maximumRecords, long maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRecords);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumRecords, MaximumRecords);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, GetMinimumServiceByteReservation(maximumRecords));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumBytes, MaximumServiceBytes);

        DiagnosticClockDomain.EnsureInitialized();
        _maximumRecords = maximumRecords;
        _maximumBytes = maximumBytes;
        _protectedSnapshotBytes = CalculateSnapshotConstructionBytes(maximumRecords);
        _records = new DiagnosticEntry[maximumRecords];
    }

    private static long MinimumWarningPayloadBytes
        => checked(
            DiagnosticObjectBytes
            + DiagnosticRetentionControlBytes
            + ArrayCharge(1, Unsafe.SizeOf<DiagnosticContextEntry>())
            + (3 * StringCharge("x"))
            + StringCharge(OccurrenceCountKey)
            + StringCharge("1"));

    internal long OwnedByteReservation => GetOwnedByteReservation(_maximumRecords);

    internal static int GetMaximumRecordCount(long maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, GetMinimumServiceByteReservation());
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumBytes, 8L * 1024 * 1024);

        for (var count = MaximumRecords; count > 0; count--)
        {
            if (GetMinimumServiceByteReservation(count) <= maximumBytes)
            {
                return count;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(maximumBytes), "The diagnostic byte budget cannot retain one record.");
    }

    internal static long GetMinimumServiceByteReservation()
        => GetMinimumServiceByteReservation(1);

    internal static long GetNoncoalescingWarningAdmissionBytes(
        int codeLength,
        int operationLength,
        int messageLength,
        int commandIdentityLength,
        ReadOnlySpan<int> contextStringLengths)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(codeLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(operationLength);
        ArgumentOutOfRangeException.ThrowIfNegative(messageLength);
        ArgumentOutOfRangeException.ThrowIfNegative(commandIdentityLength);

        if ((contextStringLengths.Length & 1) != 0)
        {
            throw new ArgumentException("Diagnostic context lengths must contain key/value pairs.", nameof(contextStringLengths));
        }

        var contextCount = contextStringLengths.Length / 2;
        var payloadBytes = checked(
            DiagnosticObjectBytes
            + DiagnosticRetentionControlBytes
            + ArrayCharge(checked((long)contextCount + 1), Unsafe.SizeOf<DiagnosticContextEntry>())
            + StringCharge(codeLength)
            + StringCharge(operationLength)
            + StringCharge(messageLength)
            + StringCharge(OccurrenceCountKey)
            + StringCharge("1"));
        var sourceDiagnosticBytes = checked(
            DiagnosticObjectBytes
            + (2 * ArrayCharge(contextCount, Unsafe.SizeOf<DiagnosticContextEntry>())));

        if (commandIdentityLength != 0)
        {
            payloadBytes = checked(payloadBytes + StringCharge(commandIdentityLength));
        }

        foreach (var stringLength in contextStringLengths)
        {
            payloadBytes = checked(payloadBytes + StringCharge(stringLength));
        }

        return checked(
            payloadBytes
            + sourceDiagnosticBytes
            + CalculateAppendWarningWorkspaceBytes(contextCount));
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

        return source.WithOwnedContext(
            [.. source.Context, new DiagnosticContextEntry(OccurrenceCountKey, occurrenceCount.ToString(CultureInfo.InvariantCulture))]);
    }

    private static long ArrayCharge(int capacity, int stride)
    {
        var payloadBytes = checked((long)capacity * stride);
        var roundedBytes = checked((payloadBytes + 15L) & ~15L);
        return checked(64L + roundedBytes);
    }

    private static long ArrayCharge(long capacity, int stride)
    {
        var payloadBytes = checked(capacity * stride);
        var roundedBytes = checked((payloadBytes + 15L) & ~15L);
        return checked(64L + roundedBytes);
    }

    private static long CalculateAppendWarningWorkspaceBytes(int contextCount)
        => checked(
            ObjectCharge(8)
            + (2 * ArrayCharge(checked((long)contextCount + 1), Unsafe.SizeOf<DiagnosticContextEntry>())));

    private static long CalculatePayloadByteCharge(Diagnostic diagnostic)
    {
        var bytes = checked(
            DiagnosticObjectBytes
            + DiagnosticRetentionControlBytes
            + ArrayCharge(diagnostic.Context.Length, Unsafe.SizeOf<DiagnosticContextEntry>())
            + StringCharge(diagnostic.Code)
            + StringCharge(diagnostic.Operation)
            + StringCharge(diagnostic.Message));

        if (diagnostic.CommandIdentity is string commandIdentity)
        {
            bytes = checked(bytes + StringCharge(commandIdentity));
        }

        foreach (var context in diagnostic.Context)
        {
            bytes = checked(bytes + StringCharge(context.Key) + StringCharge(context.Value));
        }

        return bytes;
    }

    private static long CalculateSnapshotConstructionBytes(int recordCount)
        => checked(
            ArrayCharge(recordCount, IntPtr.Size)
            + ObjectCharge(8)
            + ObjectCharge(4));

    private static long CalculateSubscriptionCapacityWarningAdmissionBytes()
    {
        const int contextCount = 2;
        var sourceDiagnosticBytes = checked(
            DiagnosticObjectBytes
            + (2 * ArrayCharge(contextCount, Unsafe.SizeOf<DiagnosticContextEntry>())));

        return checked(
            CalculateSubscriptionCapacityWarningPayloadBytes()
            + sourceDiagnosticBytes
            + CalculateAppendWarningWorkspaceBytes(contextCount));
    }

    private static long CalculateSubscriptionCapacityWarningPayloadBytes()
    {
        const int contextCount = 2;
        return checked(
            DiagnosticObjectBytes
            + DiagnosticRetentionControlBytes
            + ArrayCharge(contextCount + 1, Unsafe.SizeOf<DiagnosticContextEntry>())
            + StringCharge(WorldSubscriptionCapacityCode)
            + StringCharge(WorldSubscriptionCapacityOperation)
            + StringCharge(WorldSubscriptionCapacityMessage)
            + StringCharge(ActiveSubscriptionsContextKey)
            + StringCharge(SubscriptionCountText)
            + StringCharge(MaximumSubscriptionsContextKey)
            + StringCharge(SubscriptionCountText)
            + StringCharge(OccurrenceCountKey)
            + StringCharge("1"));
    }

    private static long CalculateUpdateWarningWorkspaceBytes(int contextCount)
        => checked(
            ObjectCharge(8)
            + (2 * ArrayCharge(contextCount, Unsafe.SizeOf<DiagnosticContextEntry>())));

    private static long CalculateWarningPayloadByteCharge(Diagnostic source, long occurrenceCount)
    {
        var bytes = checked(
            DiagnosticObjectBytes
            + DiagnosticRetentionControlBytes
            + ArrayCharge(checked(source.Context.Length + 1), Unsafe.SizeOf<DiagnosticContextEntry>())
            + StringCharge(source.Code)
            + StringCharge(source.Operation)
            + StringCharge(source.Message)
            + StringCharge(OccurrenceCountKey)
            + StringCharge(GetPositiveDecimalDigitCount(occurrenceCount)));

        if (source.CommandIdentity is string commandIdentity)
        {
            bytes = checked(bytes + StringCharge(commandIdentity));
        }

        foreach (var context in source.Context)
        {
            bytes = checked(bytes + StringCharge(context.Key) + StringCharge(context.Value));
        }

        return bytes;
    }

    private static int FindCoalescedIndex(
        DiagnosticEntry[] records,
        int head,
        int count,
        string code,
        Guid? executionSessionId,
        uint? worldOwnerId,
        WorldDiagnosticAttributionAccount? attributionAccount,
        int maximumRecords)
    {
        for (var index = 0; index < count; index++)
        {
            var recordIndex = (head + index) % maximumRecords;
            var entry = records[recordIndex];

            if ((entry.Retention is { } retention)
                && (retention.Record.Severity == DiagnosticSeverity.Warning)
                && (entry.ExecutionSessionId == executionSessionId)
                && (entry.WorldOwnerId == worldOwnerId)
                && ReferenceEquals(retention.Account, attributionAccount)
                && StringComparer.Ordinal.Equals(retention.Record.Code, code))
            {
                return recordIndex;
            }
        }

        return -1;
    }

    private static int FindRetentionIndex(
        DiagnosticEntry[] records,
        int head,
        int count,
        DiagnosticRetention retention,
        int maximumRecords)
    {
        for (var index = 0; index < count; index++)
        {
            var recordIndex = (head + index) % maximumRecords;

            if (ReferenceEquals(records[recordIndex].Retention, retention))
            {
                return recordIndex;
            }
        }

        return -1;
    }

    private static long GetMinimumServiceByteReservation(int maximumRecords)
        => checked(
            GetOwnedByteReservation(maximumRecords)
            + MinimumWarningPayloadBytes
            + DiagnosticObjectBytes
            + (2 * ArrayCharge(0, Unsafe.SizeOf<DiagnosticContextEntry>()))
            + CalculateAppendWarningWorkspaceBytes(contextCount: 0)
            + CalculateSnapshotConstructionBytes(maximumRecords));

    private static int GetPositiveDecimalDigitCount(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        var digitCount = 1;

        while (value >= 10)
        {
            value /= 10;
            digitCount++;
        }

        return digitCount;
    }

    private static bool Matches(Diagnostic diagnostic, DiagnosticQuery query)
        => (diagnostic.Severity >= query.MinimumSeverity)
        && (!query.WorldGuid.HasValue || (diagnostic.WorldGuid == query.WorldGuid))
        && (!query.MinimumTick.HasValue
            || (diagnostic.Tick.HasValue && (diagnostic.Tick.Value >= query.MinimumTick.Value)));

    private static long ObjectCharge(int pointerWidthSlots)
    {
        var payloadBytes = checked(pointerWidthSlots * (long)IntPtr.Size);
        var roundedBytes = checked((payloadBytes + 15L) & ~15L);
        return checked(64L + roundedBytes);
    }

    private static long SaturatingAdd(long value, long increment)
        => (increment > (long.MaxValue - value)) ? long.MaxValue : value + increment;

    private static long SaturatingIncrement(long value) => (value == long.MaxValue) ? value : value + 1;

    private static long StringCharge(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return ArrayCharge(checked((long)length + 1), sizeof(char));
    }

    private static long StringCharge(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return StringCharge(value.Length);
    }

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
        return source.WithOwnedContext(context.MoveToImmutable());
    }

    private static void ValidateAttributionAccount(
        Diagnostic diagnostic,
        Guid? executionSessionId,
        uint? worldOwnerId,
        WorldDiagnosticAttributionAccount? attributionAccount)
    {
        if ((attributionAccount is not null)
            && ((diagnostic.WorldGuid != attributionAccount.WorldGuid)
                || (executionSessionId != attributionAccount.ExecutionSessionId)
                || (worldOwnerId != attributionAccount.WorldOwnerId)))
        {
            throw new ArgumentException("The diagnostic identities do not match the source attribution account.", nameof(attributionAccount));
        }
    }

    private static void ValidateIdentities(Diagnostic diagnostic, Guid? executionSessionId, uint? worldOwnerId)
    {
        if (executionSessionId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(executionSessionId));
        }

        if (worldOwnerId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(worldOwnerId));
        }
    }

    private static void ValidateNoOccurrenceCount(Diagnostic diagnostic)
    {
        foreach (var context in diagnostic.Context)
        {
            if (StringComparer.Ordinal.Equals(context.Key, OccurrenceCountKey))
            {
                throw new ArgumentException("OccurrenceCount is reserved for the diagnostic service.", nameof(diagnostic));
            }
        }
    }

    private static void ValidateSkinnedNormalDiagnostic(
        Diagnostic diagnostic,
        WorldDiagnosticAttributionAccount account)
    {
        static bool IsCanonicalPositiveLong(string value)
        {
            if ((value.Length is < 1 or > 19) || (value[0] is < '1' or > '9'))
            {
                return false;
            }

            foreach (var character in value)
            {
                if (character is < '0' or > '9')
                {
                    return false;
                }
            }

            return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                && (parsed > 0);
        }

        static bool IsCanonicalPositiveCount(string value, out int parsed)
        {
            if ((value.Length is < 1 or > 6) || (value[0] is < '1' or > '9'))
            {
                parsed = 0;
                return false;
            }

            foreach (var character in value)
            {
                if (character is < '0' or > '9')
                {
                    parsed = 0;
                    return false;
                }
            }

            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed)
                && (parsed is > 0 and <= 262144);
        }

        static bool IsLowercaseGuid32(string value)
        {
            if (value.Length != 32)
            {
                return false;
            }

            foreach (var character in value)
            {
                if (character is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
                {
                    return false;
                }
            }

            return Guid.TryParseExact(value, "N", out var parsed)
                && (parsed != Guid.Empty);
        }

        static bool MatchesCommandIdentity(
            string value,
            string submissionSequence,
            string snapshotId,
            string drawId)
        {
            if (value.Length > 110)
            {
                return false;
            }

            var command = value.AsSpan();
            ReadOnlySpan<char> submissionPrefix = "submission/";
            ReadOnlySpan<char> snapshotSeparator = "/snapshot/";
            ReadOnlySpan<char> drawSeparator = "/draw/";

            if (!command.StartsWith(submissionPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var sequenceStart = submissionPrefix.Length;
            var snapshotOffset = command[sequenceStart..].IndexOf(snapshotSeparator);

            if ((snapshotOffset <= 0)
                || !command.Slice(sequenceStart, snapshotOffset).SequenceEqual(submissionSequence.AsSpan()))
            {
                return false;
            }

            var snapshotStart = sequenceStart + snapshotOffset + snapshotSeparator.Length;

            if ((command.Length < snapshotStart + 32)
                || !command.Slice(snapshotStart, 32).SequenceEqual(snapshotId.AsSpan()))
            {
                return false;
            }

            var drawSeparatorStart = snapshotStart + 32;

            if (!command[drawSeparatorStart..].StartsWith(drawSeparator, StringComparison.Ordinal))
            {
                return false;
            }

            var drawStart = drawSeparatorStart + drawSeparator.Length;
            return (command.Length == (drawStart + 32))
                && command[drawStart..].SequenceEqual(drawId.AsSpan());
        }

        if ((diagnostic.Code != DiagnosticCode)
            || (diagnostic.Severity != DiagnosticSeverity.Warning)
            || (diagnostic.Operation != DiagnosticOperation)
            || (diagnostic.Message != DiagnosticMessage)
            || (diagnostic.HostId is not uint hostId)
            || (hostId == 0)
            || (diagnostic.DeviceId is not uint deviceId)
            || (deviceId == 0)
            || !diagnostic.DeviceEpoch.HasValue
            || !diagnostic.Tick.HasValue
            || (diagnostic.WorldGuid != account.WorldGuid)
            || diagnostic.SurfaceId.HasValue
            || diagnostic.SystemId.HasValue
            || diagnostic.PassId.HasValue
            || diagnostic.AssetId.HasValue
            || diagnostic.NativeResult.HasValue
            || (diagnostic.CommandIdentity is null)
            || (diagnostic.SurfaceEpoch.HasValue != diagnostic.SwapchainEpoch.HasValue)
            || (diagnostic.Context.Length != 6))
        {
            throw new ArgumentException("The noncoalescing warning is outside the closed skinned-normal diagnostic format.", nameof(diagnostic));
        }

        var context = diagnostic.Context;

        if (!StringComparer.Ordinal.Equals(context[0].Key, SnapshotIdContextKey)
            || !IsLowercaseGuid32(context[0].Value)
            || !StringComparer.Ordinal.Equals(context[1].Key, DrawIdContextKey)
            || !IsLowercaseGuid32(context[1].Value)
            || !StringComparer.Ordinal.Equals(context[2].Key, SubmissionSequenceContextKey)
            || !IsCanonicalPositiveLong(context[2].Value)
            || !StringComparer.Ordinal.Equals(context[3].Key, CompletedFenceContextKey)
            || !IsCanonicalPositiveLong(context[3].Value)
            || !StringComparer.Ordinal.Equals(context[4].Key, TestedVertexCountContextKey)
            || !IsCanonicalPositiveCount(context[4].Value, out var testedVertexCount)
            || !StringComparer.Ordinal.Equals(context[5].Key, DegenerateVertexCountContextKey)
            || !IsCanonicalPositiveCount(context[5].Value, out var degenerateVertexCount)
            || (degenerateVertexCount > testedVertexCount)
            || !MatchesCommandIdentity(
                diagnostic.CommandIdentity!,
                context[2].Value,
                context[0].Value,
                context[1].Value))
        {
            throw new ArgumentException("The noncoalescing warning context or submission identity is invalid.", nameof(diagnostic));
        }
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

                if (entry.Retention is { } retention && Matches(retention.Record, query))
                {
                    matchingCount++;
                }
            }

            var returnedCount = Math.Min(matchingCount, query.MaximumRecords);
            var skippedCount = matchingCount - returnedCount;
            var constructionBytes = CalculateSnapshotConstructionBytes(returnedCount);

            if (!TryReserveSnapshotPreparationBytes(constructionBytes))
            {
                throw new InvalidOperationException("The diagnostic service cannot admit the bounded snapshot construction.");
            }

            try
            {
                var records = ImmutableArray.CreateBuilder<Diagnostic>(returnedCount);

                for (var index = 0; index < _count; index++)
                {
                    var entry = serviceRecords[GetRingIndex(index)];

                    if ((entry.Retention is not { } retention) || !Matches(retention.Record, query))
                    {
                        continue;
                    }

                    if (skippedCount > 0)
                    {
                        skippedCount--;
                        continue;
                    }

                    records.Add(retention.Record);
                }

                return DiagnosticSnapshot.CreateOwned(records.MoveToImmutable(), _droppedCount, _coalescedCount);
            }
            finally
            {
                _preparingPayloadBytes -= constructionBytes;
                _preparingSnapshotPayloadBytes -= constructionBytes;
            }
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
            diagnostic = _latestFault?.Record;
            return diagnostic is not null;
        }
    }

    internal void Close()
    {
        DiagnosticEntry[]? records;
        DiagnosticRetention? latestFault;

        using (ReentrantGate.EnterScope(ref _gate))
        {
            records = _records;
            _records = null;
            _head = 0;
            _count = 0;
            latestFault = _latestFault;
            _latestFault = null;
        }

        if (records is not null)
        {
            for (var index = 0; index < records.Length; index++)
            {
                var retention = records[index].Retention;
                records[index] = default;

                if (retention is not null)
                {
                    ReleaseRetention(retention);
                }
            }
        }

        if (latestFault is not null)
        {
            ReleaseRetention(latestFault);
        }
    }

    internal void CommitPreclaimedServiceBytes(long reservedBytes, long retainedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(reservedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(retainedBytes);

        if (retainedBytes > reservedBytes)
        {
            throw new InvalidOperationException("The diagnostic service retained more bytes than were preclaimed.");
        }

        using (ReentrantGate.EnterScope(ref _gate))
        {
            ObjectDisposedException.ThrowIf(_records is null, this);

            if (reservedBytes > _preparingPayloadBytes)
            {
                throw new InvalidOperationException("The diagnostic service commit exceeds its preclaimed preparation bytes.");
            }

            _preparingPayloadBytes -= retainedBytes;
            _retainedPayloadBytes = checked(_retainedPayloadBytes + retainedBytes);
        }
    }

    internal WorldDiagnosticsCapability CreateBorrowedWorldCapability(
        Guid worldGuid,
        Guid executionSessionId,
        long maximumDiagnosticBytes = 8L * 1024 * 1024)
    {
        ObjectDisposedException.ThrowIf(_records is null, this);
        return new WorldDiagnosticsCapability(
            this,
            worldGuid,
            executionSessionId,
            ownsDiagnosticsService: false,
            maximumDiagnosticBytes: maximumDiagnosticBytes);
    }

    internal WorldDiagnosticsCapability CreateWorldCapability(
        Guid worldGuid,
        Guid executionSessionId,
        long maximumDiagnosticBytes = 8L * 1024 * 1024)
    {
        ObjectDisposedException.ThrowIf(_records is null, this);
        return new WorldDiagnosticsCapability(
            this,
            worldGuid,
            executionSessionId,
            ownsDiagnosticsService: true,
            maximumDiagnosticBytes: maximumDiagnosticBytes);
    }

    internal long GetTimestamp()
    {
        using (ReentrantGate.EnterScope(ref _gate))
        {
            ObjectDisposedException.ThrowIf(_records is null, this);
            return DiagnosticClockDomain.GetElapsedTimestamp();
        }
    }

    internal void RecordDropped()
    {
        using (ReentrantGate.EnterScope(ref _gate))
        {
            ObjectDisposedException.ThrowIf(_records is null, this);
            _droppedCount = SaturatingIncrement(_droppedCount);
        }
    }

    internal void ReleasePreclaimedServiceBytes(long reservedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(reservedBytes);

        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (reservedBytes > _preparingPayloadBytes)
            {
                throw new InvalidOperationException("The diagnostic service release exceeds its preclaimed preparation bytes.");
            }

            _preparingPayloadBytes -= reservedBytes;
        }
    }

    internal bool TryReserveAttributionPayload(
        WorldDiagnosticAttributionAccount account,
        long bytes,
        out DiagnosticAttributionReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        reservation = default;

        using (ReentrantGate.EnterScope(ref _gate))
        {
            var records = _records ?? throw new ObjectDisposedException(nameof(DiagnosticsService));

            if (bytes > account.MaximumRetainedBytes)
            {
                _droppedCount = SaturatingIncrement(_droppedCount);
                return false;
            }

            if (!TryReserveAccountBytes(account, bytes, protectedRetention: null, out reservation))
            {
                _droppedCount = SaturatingIncrement(_droppedCount);
                return false;
            }

            if (!TryMakeServiceRoom(records, bytes, protectedRetention: null, requireRingSlot: false))
            {
                reservation.Dispose();
                _droppedCount = SaturatingIncrement(_droppedCount);
                return false;
            }

            _preparingPayloadBytes = checked(_preparingPayloadBytes + bytes);
            reservation.AttachServiceReservation(this, bytes);
            return true;
        }
    }

    internal void Write(
        Diagnostic diagnostic,
        Guid? executionSessionId,
        uint? worldOwnerId,
        WorldDiagnosticAttributionAccount? attributionAccount)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        var reservation = default(DiagnosticAttributionReservation);
        _ = WriteCore(
            diagnostic,
            executionSessionId,
            worldOwnerId,
            attributionAccount,
            coalesceWarnings: true,
            preclaimed: false,
            ref reservation);
    }

    internal void Write(Diagnostic diagnostic, Guid? executionSessionId, uint? worldOwnerId)
        => Write(diagnostic, executionSessionId, worldOwnerId, attributionAccount: null);

    internal bool WriteNoncoalescingWarning(
        Diagnostic diagnostic,
        ref DiagnosticAttributionReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        if (diagnostic.Severity != DiagnosticSeverity.Warning)
        {
            throw new ArgumentException("A noncoalescing diagnostic record must be a Warning.", nameof(diagnostic));
        }

        var account = reservation.Account;

        if (!ReferenceEquals(reservation.Diagnostics, this)
            || (reservation.DiagnosticsReservedBytes <= 0)
            || (diagnostic.WorldGuid != account.WorldGuid)
            || (account.WorldOwnerId == 0))
        {
            throw new ArgumentException("The diagnostic reservation does not match this service and source World.", nameof(reservation));
        }

        ValidateSkinnedNormalDiagnostic(diagnostic, account);

        return WriteCore(
            diagnostic,
            account.ExecutionSessionId,
            account.WorldOwnerId,
            account,
            coalesceWarnings: false,
            preclaimed: true,
            ref reservation);
    }

    internal void WriteSubscriptionCapacityWarning(WorldDiagnosticAttributionAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        if ((account.WorldOwnerId == 0)
            || (account.WorldGuid == Guid.Empty)
            || (account.ExecutionSessionId == Guid.Empty))
        {
            throw new ArgumentException("The subscription warning requires a bound World attribution account.", nameof(account));
        }

        using (ReentrantGate.EnterScope(ref _gate))
        {
            var records = _records ?? throw new ObjectDisposedException(nameof(DiagnosticsService));
            var coalescedIndex = FindCoalescedIndex(
                records,
                _head,
                _count,
                WorldSubscriptionCapacityCode,
                account.ExecutionSessionId,
                account.WorldOwnerId,
                account,
                _maximumRecords);

            if (coalescedIndex >= 0)
            {
                var previousEntry = records[coalescedIndex];
                var oldRetention = previousEntry.Retention
                    ?? throw new InvalidOperationException("The occupied diagnostic ring slot has no retained record.");
                var occurrenceCount = SaturatingIncrement(previousEntry.OccurrenceCount);
                var retainedPayloadBytes = CalculateWarningPayloadByteCharge(oldRetention.Record, occurrenceCount);
                var workspaceBytes = CalculateUpdateWarningWorkspaceBytes(oldRetention.Record.Context.Length);
                var peakBytes = checked(retainedPayloadBytes + workspaceBytes);

                if (!TryReserveAccountBytes(account, peakBytes, oldRetention, out var reservation))
                {
                    _droppedCount = SaturatingIncrement(_droppedCount);
                    return;
                }

                if (!TryMakeServiceRoom(records, peakBytes, oldRetention, requireRingSlot: false))
                {
                    reservation.Dispose();
                    _droppedCount = SaturatingIncrement(_droppedCount);
                    return;
                }

                var currentIndex = FindRetentionIndex(records, _head, _count, oldRetention, _maximumRecords);

                if (currentIndex < 0)
                {
                    reservation.Dispose();
                    throw new InvalidOperationException("The protected subscription warning was lost during admission.");
                }

                _preparingPayloadBytes = checked(_preparingPayloadBytes + peakBytes);
                var preparationReleased = false;
                ref var entry = ref records[currentIndex];

                try
                {
                    var updatedRecord = UpdateOccurrenceCount(oldRetention.Record, occurrenceCount);
                    var updatedRetention = new DiagnosticRetention(updatedRecord, account, retainedPayloadBytes);

                    reservation.TransferToService(retainedPayloadBytes);
                    _retainedPayloadBytes = checked(_retainedPayloadBytes + retainedPayloadBytes);
                    _preparingPayloadBytes -= peakBytes;
                    preparationReleased = true;
                    entry.Retention = updatedRetention;
                    entry.OccurrenceCount = occurrenceCount;
                    ReleaseRetention(oldRetention);
                    _coalescedCount = SaturatingIncrement(_coalescedCount);
                }
                catch
                {
                    if (!preparationReleased)
                    {
                        _preparingPayloadBytes -= peakBytes;
                    }

                    reservation.Dispose();
                    throw;
                }

                reservation.Dispose();
                return;
            }

            var warningPayloadBytes = CalculateSubscriptionCapacityWarningPayloadBytes();
            var newRecordPeakBytes = CalculateSubscriptionCapacityWarningAdmissionBytes();

            if (!TryReserveAccountBytes(account, newRecordPeakBytes, protectedRetention: null, out var newRecordReservation))
            {
                _droppedCount = SaturatingIncrement(_droppedCount);
                return;
            }

            if (!TryMakeServiceRoom(records, newRecordPeakBytes, protectedRetention: null, requireRingSlot: true))
            {
                newRecordReservation.Dispose();
                _droppedCount = SaturatingIncrement(_droppedCount);
                return;
            }

            _preparingPayloadBytes = checked(_preparingPayloadBytes + newRecordPeakBytes);
            var newRecordPreparationReleased = false;

            try
            {
                var sourceDiagnostic = new Diagnostic(
                    WorldSubscriptionCapacityCode,
                    DiagnosticSeverity.Warning,
                    DiagnosticClockDomain.GetElapsedTimestamp(),
                    hostId: null,
                    worldGuid: account.WorldGuid,
                    deviceId: null,
                    surfaceId: null,
                    deviceEpoch: null,
                    surfaceEpoch: null,
                    swapchainEpoch: null,
                    tick: null,
                    systemId: null,
                    passId: null,
                    assetId: null,
                    commandIdentity: null,
                    operation: WorldSubscriptionCapacityOperation,
                    nativeResult: null,
                    message: WorldSubscriptionCapacityMessage,
                    context: [
                        new DiagnosticContextEntry(ActiveSubscriptionsContextKey, SubscriptionCountText),
                        new DiagnosticContextEntry(MaximumSubscriptionsContextKey, SubscriptionCountText),
                    ]);
                var retainedDiagnostic = AppendOccurrenceCount(sourceDiagnostic, occurrenceCount: 1);
                var retention = new DiagnosticRetention(retainedDiagnostic, account, warningPayloadBytes);

                newRecordReservation.TransferToService(warningPayloadBytes);
                _retainedPayloadBytes = checked(_retainedPayloadBytes + warningPayloadBytes);
                _preparingPayloadBytes -= newRecordPeakBytes;
                newRecordPreparationReleased = true;
                records[GetRingIndex(_count)] = new DiagnosticEntry(
                    retention,
                    account.ExecutionSessionId,
                    account.WorldOwnerId,
                    occurrenceCount: 1);
                _count++;
            }
            catch
            {
                if (!newRecordPreparationReleased)
                {
                    _preparingPayloadBytes -= newRecordPeakBytes;
                }

                newRecordReservation.Dispose();
                throw;
            }

            newRecordReservation.Dispose();
        }
    }

    private void EvictRingEntry(DiagnosticEntry[] records, int recordIndex)
    {
        var logicalIndex = recordIndex - _head;

        if (logicalIndex < 0)
        {
            logicalIndex += _maximumRecords;
        }

        if ((logicalIndex < 0) || (logicalIndex >= _count))
        {
            throw new InvalidOperationException("The diagnostic eviction index is outside the live ring.");
        }

        var evictedRetention = records[recordIndex].Retention
            ?? throw new InvalidOperationException("The occupied diagnostic ring slot has no retained payload.");
        var droppedOccurrences = (evictedRetention.Record.Severity == DiagnosticSeverity.Warning)
            ? records[recordIndex].OccurrenceCount
            : 1;

        if (droppedOccurrences <= 0)
        {
            throw new InvalidOperationException("An evicted warning has no retained occurrence count.");
        }

        for (var index = logicalIndex; index < (_count - 1); index++)
        {
            records[GetRingIndex(index)] = records[GetRingIndex(index + 1)];
        }

        records[GetRingIndex(_count - 1)] = default;
        _count--;
        _droppedCount = SaturatingAdd(_droppedCount, droppedOccurrences);
        ReleaseRetention(evictedRetention);
    }

    private int FindOldestAttributionEntry(
        DiagnosticEntry[] records,
        WorldDiagnosticAttributionAccount? account,
        DiagnosticRetention? protectedRetention)
    {
        for (var index = 0; index < _count; index++)
        {
            var recordIndex = GetRingIndex(index);
            var retention = records[recordIndex].Retention;

            if ((retention is not null)
                && !ReferenceEquals(retention, protectedRetention)
                && ((account is null) || ReferenceEquals(retention.Account, account)))
            {
                return recordIndex;
            }
        }

        return -1;
    }

    private int GetRingIndex(int chronologicalIndex) => (_head + chronologicalIndex) % _maximumRecords;

    private void ReleaseRetention(DiagnosticRetention retention)
    {
        retention.ServiceReferenceCount--;

        if (retention.ServiceReferenceCount < 0)
        {
            throw new InvalidOperationException("The diagnostic service released a retained payload more than once.");
        }

        if (retention.ServiceReferenceCount == 0)
        {
            var payloadBytes = retention.PayloadBytes;
            var account = retention.Account;
            retention.ReleasePayload();
            _retainedPayloadBytes -= payloadBytes;
            account?.Release(payloadBytes);
        }
    }

    private bool TryMakeServiceRoom(
        DiagnosticEntry[] records,
        long bytes,
        DiagnosticRetention? protectedRetention,
        bool requireRingSlot)
    {
        var protectedSnapshotBytes = Math.Max(0, _protectedSnapshotBytes - _preparingSnapshotPayloadBytes);
        var maximumPayloadBytes = _maximumBytes - OwnedByteReservation - protectedSnapshotBytes;

        if (bytes > maximumPayloadBytes)
        {
            return false;
        }

        while ((requireRingSlot && (_count >= _maximumRecords))
            || (bytes > (maximumPayloadBytes - _retainedPayloadBytes - _preparingPayloadBytes)))
        {
            var recordIndex = FindOldestAttributionEntry(records, account: null, protectedRetention);

            if (recordIndex < 0)
            {
                return false;
            }

            EvictRingEntry(records, recordIndex);
        }

        return true;
    }

    private bool TryReserveAccountBytes(
        WorldDiagnosticAttributionAccount? account,
        long bytes,
        DiagnosticRetention? protectedRetention,
        out DiagnosticAttributionReservation reservation)
    {
        reservation = default;

        if (account is null)
        {
            return true;
        }

        if (bytes > account.MaximumRetainedBytes)
        {
            return false;
        }

        while (!account.TryReserve(bytes, out reservation))
        {
            var recordIndex = FindOldestAttributionEntry(_records!, account, protectedRetention);

            if (recordIndex < 0)
            {
                return false;
            }

            EvictRingEntry(_records!, recordIndex);
        }

        return true;
    }

    private bool TryReserveSnapshotPreparationBytes(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        var remainingProtectedBytes = _protectedSnapshotBytes - _preparingSnapshotPayloadBytes;

        if (bytes > remainingProtectedBytes)
        {
            return false;
        }

        var occupiedBytes = checked(OwnedByteReservation + _retainedPayloadBytes + _preparingPayloadBytes);

        if (bytes > (_maximumBytes - occupiedBytes))
        {
            return false;
        }

        _preparingPayloadBytes = checked(_preparingPayloadBytes + bytes);
        _preparingSnapshotPayloadBytes = checked(_preparingSnapshotPayloadBytes + bytes);
        return true;
    }

    private bool WriteCore(
        Diagnostic diagnostic,
        Guid? executionSessionId,
        uint? worldOwnerId,
        WorldDiagnosticAttributionAccount? attributionAccount,
        bool coalesceWarnings,
        bool preclaimed,
        ref DiagnosticAttributionReservation reservation)
    {
        ValidateIdentities(diagnostic, executionSessionId, worldOwnerId);
        ValidateAttributionAccount(diagnostic, executionSessionId, worldOwnerId, attributionAccount);

        if (diagnostic.Severity == DiagnosticSeverity.Warning)
        {
            ValidateNoOccurrenceCount(diagnostic);
        }

        if (preclaimed
            && (!ReferenceEquals(reservation.Account, attributionAccount)
                || !ReferenceEquals(reservation.Diagnostics, this)))
        {
            throw new ArgumentException("The diagnostic reservation does not match the selected source account.", nameof(reservation));
        }

        var reservationTransferred = false;

        try
        {
            using (ReentrantGate.EnterScope(ref _gate))
            {
                var records = _records ?? throw new ObjectDisposedException(nameof(DiagnosticsService));
                var coalescedIndex = (coalesceWarnings && (diagnostic.Severity == DiagnosticSeverity.Warning))
                    ? FindCoalescedIndex(
                        records,
                        _head,
                        _count,
                        diagnostic.Code,
                        executionSessionId,
                        worldOwnerId,
                        attributionAccount,
                        _maximumRecords)
                    : -1;

                if (coalescedIndex >= 0)
                {
                    var previousEntry = records[coalescedIndex];
                    var oldRetention = previousEntry.Retention
                        ?? throw new InvalidOperationException("The occupied diagnostic ring slot has no retained record.");
                    var occurrenceCount = SaturatingIncrement(previousEntry.OccurrenceCount);
                    var replacementPayloadBytes = CalculateWarningPayloadByteCharge(oldRetention.Record, occurrenceCount);
                    var replacementWorkspaceBytes = CalculateUpdateWarningWorkspaceBytes(oldRetention.Record.Context.Length);
                    var replacementPeakBytes = checked(replacementPayloadBytes + replacementWorkspaceBytes);

                    if (preclaimed)
                    {
                        throw new InvalidOperationException("A preclaimed diagnostic warning cannot enter the coalescing path.");
                    }

                    if (!TryReserveAccountBytes(attributionAccount, replacementPeakBytes, oldRetention, out reservation))
                    {
                        _droppedCount = SaturatingIncrement(_droppedCount);
                        return false;
                    }

                    if (!TryMakeServiceRoom(records, replacementPeakBytes, oldRetention, requireRingSlot: false))
                    {
                        reservation.Dispose();
                        _droppedCount = SaturatingIncrement(_droppedCount);
                        return false;
                    }

                    var currentIndex = FindRetentionIndex(records, _head, _count, oldRetention, _maximumRecords);

                    if (currentIndex < 0)
                    {
                        throw new InvalidOperationException("The protected warning was lost during admission.");
                    }

                    _preparingPayloadBytes = checked(_preparingPayloadBytes + replacementPeakBytes);
                    ref var entry = ref records[currentIndex];

                    try
                    {
                        var updatedRecord = UpdateOccurrenceCount(oldRetention.Record, occurrenceCount);
                        var updatedRetention = new DiagnosticRetention(updatedRecord, attributionAccount, replacementPayloadBytes);

                        if (attributionAccount is not null)
                        {
                            reservation.TransferToService(replacementPayloadBytes);
                            reservationTransferred = true;
                        }

                        _retainedPayloadBytes = checked(_retainedPayloadBytes + replacementPayloadBytes);
                        _preparingPayloadBytes -= replacementPeakBytes;
                        entry.Retention = updatedRetention;
                        entry.OccurrenceCount = occurrenceCount;
                        ReleaseRetention(oldRetention);
                        _coalescedCount = SaturatingIncrement(_coalescedCount);
                        return true;
                    }
                    catch
                    {
                        _preparingPayloadBytes -= replacementPeakBytes;
                        throw;
                    }
                }

                var retainedOccurrenceCount = (diagnostic.Severity == DiagnosticSeverity.Warning) ? 1L : 0L;
                var retainedPayloadBytes = (diagnostic.Severity == DiagnosticSeverity.Warning)
                    ? CalculateWarningPayloadByteCharge(diagnostic, retainedOccurrenceCount)
                    : CalculatePayloadByteCharge(diagnostic);
                var workspaceBytes = (diagnostic.Severity == DiagnosticSeverity.Warning)
                    ? CalculateAppendWarningWorkspaceBytes(diagnostic.Context.Length)
                    : 0;
                var peakBytes = checked(retainedPayloadBytes + workspaceBytes);

                if (preclaimed)
                {
                    var sourceDiagnosticBytes = checked(
                        DiagnosticObjectBytes
                        + (2 * ArrayCharge(diagnostic.Context.Length, Unsafe.SizeOf<DiagnosticContextEntry>())));
                    var completePeakBytes = checked(peakBytes + sourceDiagnosticBytes);

                    if ((reservation.ReservedBytes < completePeakBytes)
                        || (reservation.DiagnosticsReservedBytes < completePeakBytes))
                    {
                        throw new InvalidOperationException("The diagnostic record exceeds its preclaimed source-account or service bytes.");
                    }

                    if (!TryMakeServiceRoom(records, bytes: 0, protectedRetention: null, requireRingSlot: true))
                    {
                        _droppedCount = SaturatingIncrement(_droppedCount);
                        return false;
                    }
                }
                else if (!TryReserveAccountBytes(attributionAccount, peakBytes, protectedRetention: null, out reservation))
                {
                    _droppedCount = SaturatingIncrement(_droppedCount);
                    return false;
                }

                if (!preclaimed && !TryMakeServiceRoom(records, peakBytes, protectedRetention: null, requireRingSlot: true))
                {
                    reservation.Dispose();
                    _droppedCount = SaturatingIncrement(_droppedCount);
                    return false;
                }

                if (!preclaimed)
                {
                    _preparingPayloadBytes = checked(_preparingPayloadBytes + peakBytes);
                }

                var preparationReleased = false;

                try
                {
                    var retainedDiagnostic = (diagnostic.Severity == DiagnosticSeverity.Warning)
                        ? AppendOccurrenceCount(diagnostic, retainedOccurrenceCount)
                        : diagnostic;
                    var retention = new DiagnosticRetention(retainedDiagnostic, attributionAccount, retainedPayloadBytes);

                    if (preclaimed || (attributionAccount is not null))
                    {
                        reservation.TransferToService(retainedPayloadBytes);
                        reservationTransferred = true;
                    }

                    if (!preclaimed)
                    {
                        _retainedPayloadBytes = checked(_retainedPayloadBytes + retainedPayloadBytes);
                        _preparingPayloadBytes -= peakBytes;
                    }

                    preparationReleased = true;
                    var insertIndex = GetRingIndex(_count);
                    records[insertIndex] = new DiagnosticEntry(
                        retention,
                        executionSessionId,
                        worldOwnerId,
                        retainedOccurrenceCount);
                    _count++;

                    if (diagnostic.Severity == DiagnosticSeverity.Critical)
                    {
                        retention.AddServiceReference();
                        var previousFault = _latestFault;
                        _latestFault = retention;

                        if (previousFault is not null)
                        {
                            ReleaseRetention(previousFault);
                        }
                    }

                    return true;
                }
                catch
                {
                    if (!preclaimed && !preparationReleased)
                    {
                        _preparingPayloadBytes -= peakBytes;
                    }

                    throw;
                }
            }
        }
        finally
        {
            if (!reservationTransferred)
            {
                reservation.Dispose();
            }
        }
    }

    private struct DiagnosticEntry
    {
        internal Guid? ExecutionSessionId;

        internal long OccurrenceCount;

        internal DiagnosticRetention? Retention;

        internal uint? WorldOwnerId;

        internal DiagnosticEntry(
            DiagnosticRetention retention,
            Guid? executionSessionId,
            uint? worldOwnerId,
            long occurrenceCount)
        {
            Retention = retention;
            ExecutionSessionId = executionSessionId;
            WorldOwnerId = worldOwnerId;
            OccurrenceCount = occurrenceCount;
        }
    }

    private sealed class DiagnosticRetention
    {
        private WorldDiagnosticAttributionAccount? _account;
        private long _payloadBytes;
        private Diagnostic? _record;

        internal DiagnosticRetention(
            Diagnostic record,
            WorldDiagnosticAttributionAccount? account,
            long payloadBytes)
        {
            _record = record;
            _account = account;
            _payloadBytes = payloadBytes;
            ServiceReferenceCount = 1;
        }

        internal WorldDiagnosticAttributionAccount? Account => _account;

        internal long PayloadBytes => _payloadBytes;

        internal Diagnostic Record
            => _record
                ?? throw new ObjectDisposedException(nameof(DiagnosticRetention));

        internal int ServiceReferenceCount { get; set; }

        internal void AddServiceReference()
        {
            if (ServiceReferenceCount <= 0)
            {
                throw new InvalidOperationException("A released diagnostic payload cannot acquire another service root.");
            }

            ServiceReferenceCount = checked(ServiceReferenceCount + 1);
        }

        internal void ReleasePayload()
        {
            _account = null;
            _payloadBytes = 0;
            _record = null;
        }
    }
}
