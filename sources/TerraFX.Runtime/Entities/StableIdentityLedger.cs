// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;

namespace TerraFX.Entities;

// Owner-serialized in-memory storage, not a World, membership authority, or persistence codec.
// World supplies admitted branch indices/namespaces and projected locators in canonical assignment
// order, and separately reserves its structural storage, references, cleanup and events.
// Operational slot allocation (including permanent retirement), branch admission, reference
// positions and occurrence counts stay with World; a nonzero locator is not a slot allocator.
// Retired Entity reverse keys keep allocated-slot credits; scene keys count only live instances.
internal sealed class StableIdentityLedger
{
    internal const int MaximumAssignments = 33_554_432;
    internal const int MaximumBranches = 1024;
    internal const int MaximumEntitySlots = 2_097_152;
    internal const int MaximumSceneInstances = 65_536;
    internal const int RecordSize = 24;
    internal const int RecordsPerPage = 4096;
    private const int LiveMask = 1 << 14;
    private static readonly State s_emptyState = new(
        [],
        [],
        [],
        new IdentityGuidIndex([], [], 0, -1),
        0,
        0);

    private readonly int _maximumAssignments;
    private readonly int _maximumEntitySlots;
    private readonly int _maximumSceneInstances;
    private State _state;

    internal StableIdentityLedger()
        : this(MaximumAssignments, MaximumEntitySlots, MaximumSceneInstances)
    {
    }

    private StableIdentityLedger(int maximumAssignments, int maximumEntitySlots, int maximumSceneInstances)
    {
        _maximumAssignments = maximumAssignments;
        _maximumEntitySlots = maximumEntitySlots;
        _maximumSceneInstances = maximumSceneInstances;
        _state = new State([], [], [], new IdentityGuidIndex([], [], 0, -1), 0, 0);
    }

    internal int AssignmentCount => _state.AssignmentCount;

    internal int EntityCount => _state.EntityCount;

    internal int EntitySlotCount => _state.EntityReverse.Count;

    internal int IndexHeight => _state.Index.Height;

    internal int IndexPageCount => _state.Index.PageCount;

    internal int IndexRoot => _state.Index.Root;

    internal int LedgerPageCount => _state.LedgerPages.Length;

    internal long OwnedByteReservation => _state.OwnedByteReservation;

    internal int SceneInstanceCount => _state.SceneReverse.Count;

    // Only the friend test assembly needs lowered count limits to exercise exhaustion.
    internal static StableIdentityLedger CreateForTesting(int maximumAssignments, int maximumEntitySlots, int maximumSceneInstances)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumAssignments);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumAssignments, MaximumAssignments);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumEntitySlots);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumEntitySlots, MaximumEntitySlots);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumSceneInstances);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumSceneInstances, MaximumSceneInstances);
        return new StableIdentityLedger(maximumAssignments, maximumEntitySlots, maximumSceneInstances);
    }

    internal static StableIdentityLedger CreateForWorld(int maximumAssignments, int maximumEntitySlots, int maximumSceneInstances)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumAssignments);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumAssignments, MaximumAssignments);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntitySlots);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumEntitySlots, MaximumEntitySlots);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSceneInstances);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumSceneInstances, MaximumSceneInstances);
        return new StableIdentityLedger(maximumAssignments, maximumEntitySlots, maximumSceneInstances);
    }

    // Conservative managed reservations: 64 bytes per array header/alignment, eight-byte
    // payload alignment, and explicit object/scratch headroom. These are accounting credits,
    // not a measured footprint or workload qualification on every CLR.
    private static long ArrayBytes(long count, int elementSize) => checked(64 + ((checked(count * elementSize) + 7) & ~7L));

    private static Dictionary<uint, uint> CopyReverse(Dictionary<uint, uint> source, int capacity, uint[] retirements, bool retainSlots)
    {
        var result = new Dictionary<uint, uint>(capacity);

        foreach (var entry in source)
        {
            var retired = Array.BinarySearch(retirements, entry.Value) >= 0;

            if (!retired || retainSlots)
            {
                result.Add(entry.Key, retired ? 0 : entry.Value);
            }
        }

        return result;
    }

    // UInt32 -> UInt32 dictionaries have four-byte buckets and 16-byte entries. The
    // preallocation grant uses at most twice the requested count plus three for prime
    // rounding; resident accounting uses the actual capacity, including both headers.
    private static long DictionaryBytes(int capacity) => checked(ArrayBytes(capacity, 4) + ArrayBytes(capacity, 16) + 64);

    private static IdentityKind GetKind(ReadOnlySpan<byte> record)
        => (IdentityKind)((BinaryPrimitives.ReadUInt32LittleEndian(record[20..]) >> 10) & 3);

    private static int GetLedgerPageCount(int assignmentCount) => (assignmentCount + RecordsPerPage - 1) / RecordsPerPage;

    private static ReadOnlySpan<byte> GetRecord(byte[][] pages, uint ordinal)
    {
        var index = checked((int)ordinal - 1);
        return pages[index / RecordsPerPage].AsSpan(index % RecordsPerPage * RecordSize, RecordSize);
    }

    private static byte[] GetWritablePage(byte[]?[] pages, bool[] owned, int index, int pageSize)
    {
        if (!owned[index])
        {
            pages[index] = pages[index] is byte[] page ? (byte[])page.Clone() : new byte[pageSize];
            owned[index] = true;
        }

        return pages[index] ?? throw new InvalidOperationException("The writable identity page is missing.");
    }

    private static Span<byte> GetWritableRecord(byte[][] pages, bool[] owned, uint ordinal)
    {
        var index = checked((int)ordinal - 1);
        return GetWritablePage(pages, owned, index / RecordsPerPage, RecordSize * RecordsPerPage)
            .AsSpan(index % RecordsPerPage * RecordSize, RecordSize);
    }

    private static void ValidateKind(IdentityKind kind)
    {
        if (kind is not (IdentityKind.Entity or IdentityKind.SceneInstance))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static void ValidateLocator(IdentityKind kind, uint locator)
    {
        ValidateKind(kind);
        ArgumentOutOfRangeException.ThrowIfZero(locator);
    }

    private static void ValidateOrdinal(uint ordinal, int assignmentCount)
    {
        if (ordinal == 0 || ordinal > assignmentCount)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }
    }

    private static int ValidateRetirements(State source, uint[] retirements)
    {
        var sceneCount = 0;

        for (var index = 0; index < retirements.Length; index++)
        {
            var ordinal = retirements[index];
            ValidateOrdinal(ordinal, source.AssignmentCount);
            var record = GetRecord(source.LedgerPages, ordinal);

            if ((index != 0 && ordinal == retirements[index - 1]) || BinaryPrimitives.ReadUInt32LittleEndian(record[16..]) == 0)
            {
                throw new ArgumentException("The identity retirement must name a distinct live assignment.", nameof(retirements));
            }

            sceneCount += GetKind(record) == IdentityKind.SceneInstance ? 1 : 0;
        }

        return sceneCount;
    }

    internal void CopyIndexPage(int pageIndex, Span<byte> destination) => _state.Index.CopyPage(pageIndex, destination);

    internal void CopyRecord(uint ordinal, Span<byte> destination)
    {
        ValidateOrdinal(ordinal, _state.AssignmentCount);
        GetRecord(_state.LedgerPages, ordinal).CopyTo(destination);
    }

    internal Guid GetAssignedId(IdentityKind kind, uint locator)
    {
        var ordinal = GetLiveOrdinal(kind, locator);

        if (ordinal == 0)
        {
            throw new ArgumentException("The identity locator is not live.", nameof(locator));
        }

        return new Guid(GetRecord(_state.LedgerPages, ordinal)[..16], bigEndian: true);
    }

    internal int GetBranchIndexForLiveLocator(IdentityKind kind, uint locator)
    {
        var ordinal = GetLiveOrdinal(kind, locator);

        if (ordinal == 0)
        {
            throw new ArgumentException("The identity locator is not live.", nameof(locator));
        }

        return GetBranchIndexForOrdinal(ordinal);
    }

    internal int GetBranchIndexForOrdinal(uint ordinal)
    {
        ValidateOrdinal(ordinal, _state.AssignmentCount);
        var metadata = BinaryPrimitives.ReadUInt32LittleEndian(GetRecord(_state.LedgerPages, ordinal)[20..]);
        return (int)(metadata & ((1u << 10) - 1));
    }

    internal uint GetLiveOrdinal(IdentityKind kind, uint locator)
    {
        ValidateLocator(kind, locator);
        var reverse = kind == IdentityKind.Entity ? _state.EntityReverse : _state.SceneReverse;
        return reverse.TryGetValue(locator, out var ordinal) ? ordinal : 0;
    }

    // The grant includes current ledger ownership and the entire conservative preparation peak.
    // It is not an independent 2-GiB allowance: World must subtract every other shared owner and
    // retain credits for outstanding preparations. No external reservation or callback runs here.
    internal long GetPreparationByteReservation(int assignmentCount, int retirementCount)
    {
        ValidateCounts(assignmentCount, retirementCount);
        var total = checked(_state.AssignmentCount + assignmentCount);
        var ledgerPages = GetLedgerPageCount(total);
        var treePages = GetTreeDirectoryCapacity(total, assignmentCount);
        var treeCopies = Math.Min(_state.Index.PageCount, (long)assignmentCount * 3);
        var ledgerCopies = Math.Min(_state.LedgerPages.Length, (long)retirementCount + (assignmentCount == 0 ? 0 : 1));
        var entityCapacity = Math.Min(_maximumEntitySlots, checked((long)_state.EntityReverse.Count + assignmentCount));
        var sceneCapacity = Math.Min(_maximumSceneInstances, checked((long)_state.SceneReverse.Count + assignmentCount));
        var pageBytes = checked(
            ((treeCopies + treePages - _state.Index.PageCount) * ArrayBytes(IdentityGuidIndex.PageSize, 1))
            + ((ledgerCopies + ledgerPages - _state.LedgerPages.Length) * ArrayBytes(RecordSize * RecordsPerPage, 1)));
        var directoryBytes = checked(
            ArrayBytes(ledgerPages, IntPtr.Size) + ArrayBytes(ledgerPages, 1)
            + ArrayBytes(treePages, IntPtr.Size) + ArrayBytes(treePages, 1)
            + DictionaryBytes(checked((int)((entityCapacity * 2) + 3)))
            + DictionaryBytes(checked((int)((sceneCapacity * 2) + 3))));
        var scratchBytes = checked(
            ArrayBytes(checked((long)assignmentCount * RecordSize), 1)
            + ArrayBytes(assignmentCount, sizeof(int))
            + ArrayBytes(retirementCount, sizeof(uint))
            + ((long)assignmentCount * 512));
        return checked(_state.OwnedByteReservation + pageBytes + directoryBytes + scratchBytes + 4096);
    }

    internal PreparedChange Prepare(
        scoped ReadOnlySpan<IdentityAssignment> assignments,
        scoped ReadOnlySpan<uint> retirements,
        long maximumOwnedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumOwnedBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumOwnedBytes, WorldSchedulingLimits.Default.MaximumOwnedStateBytes);
        var reservation = GetPreparationByteReservation(assignments.Length, retirements.Length);

        if (reservation > maximumOwnedBytes)
        {
            throw new InvalidOperationException($"Identity preparation requires {reservation} shared-state bytes; the grant is {maximumOwnedBytes}.");
        }

        foreach (var assignment in assignments)
        {
            ValidateAssignment(assignment);
        }

        var source = _state;
        var records = new byte[checked(assignments.Length * RecordSize)];
        var order = new int[assignments.Length];
        var sortedRetirements = retirements.ToArray();
        Array.Sort(sortedRetirements);
        var retiredScenes = ValidateRetirements(source, sortedRetirements);
        var assignedScenes = 0;

        for (var index = 0; index < assignments.Length; index++)
        {
            var assignment = assignments[index];
            var ordinal = checked((uint)(source.AssignmentCount + index + 1));
            var id = assignment.StableId;

            if (assignment.Origin == IdentityOrigin.Automatic)
            {
                var prefix = assignment.Kind == IdentityKind.Entity ? "world/entity/v1/" : "world/scene-instance/v1/";
                id = FoundationNameUuid.Create(
                    assignment.IssuanceNamespace,
                    string.Create(CultureInfo.InvariantCulture, $"{prefix}{ordinal:x16}"));
            }

            var record = records.AsSpan(index * RecordSize, RecordSize);
            _ = id.TryWriteBytes(record[..16], bigEndian: true, out _);
            BinaryPrimitives.WriteUInt32LittleEndian(record[16..], assignment.Locator);
            BinaryPrimitives.WriteUInt32LittleEndian(record[20..],
                (uint)(assignment.BranchIndex | ((int)assignment.Kind << 10) | ((int)assignment.Origin << 12) | LiveMask));
            order[index] = index;
            assignedScenes += assignment.Kind == IdentityKind.SceneInstance ? 1 : 0;
        }

        var sceneCount = checked(source.SceneReverse.Count - retiredScenes + assignedScenes);
        var entityCount = checked(source.EntityCount - (retirements.Length - retiredScenes) + assignments.Length - assignedScenes);

        if (sceneCount > _maximumSceneInstances)
        {
            throw new InvalidOperationException("The active scene-instance limit was exceeded.");
        }

        Array.Sort(order, new AssignmentComparer(records, compareLocators: false));

        for (var index = 0; index < order.Length; index++)
        {
            var id = records.AsSpan(order[index] * RecordSize, 16);

            if ((index != 0 && id.SequenceEqual(records.AsSpan(order[index - 1] * RecordSize, 16)))
                || source.Index.Find(id) != 0)
            {
                throw new ArgumentException("The stable identity has already been assigned.", nameof(assignments));
            }
        }

        Array.Sort(order, new AssignmentComparer(records, compareLocators: true));
        var entitySlots = source.EntityReverse.Count;

        for (var index = 0; index < order.Length; index++)
        {
            var record = records.AsSpan(order[index] * RecordSize, RecordSize);
            var kind = GetKind(record);
            var locator = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
            var reverse = kind == IdentityKind.Entity ? source.EntityReverse : source.SceneReverse;
            _ = reverse.TryGetValue(locator, out var existing);

            if ((index != 0 && AssignmentComparer.CompareLocators(record, records.AsSpan(order[index - 1] * RecordSize, RecordSize)) == 0)
                || (existing != 0 && Array.BinarySearch(sortedRetirements, existing) < 0))
            {
                throw new ArgumentException("The projected live identity locator is already occupied.", nameof(assignments));
            }

            if (kind == IdentityKind.Entity && !reverse.ContainsKey(locator))
            {
                entitySlots++;
            }
        }

        if (entitySlots > _maximumEntitySlots)
        {
            throw new InvalidOperationException("The allocated Entity slot limit was exceeded.");
        }

        var total = checked(source.AssignmentCount + assignments.Length);
        var ledgerPages = new byte[GetLedgerPageCount(total)][];
        Array.Copy(source.LedgerPages, ledgerPages, source.LedgerPages.Length);
        var ledgerOwned = new bool[ledgerPages.Length];
        var entityReverse = CopyReverse(source.EntityReverse, entitySlots, sortedRetirements, retainSlots: true);
        var sceneReverse = CopyReverse(source.SceneReverse, sceneCount, sortedRetirements, retainSlots: false);

        foreach (var ordinal in sortedRetirements)
        {
            var record = GetWritableRecord(ledgerPages, ledgerOwned, ordinal);
            BinaryPrimitives.WriteUInt32LittleEndian(record[16..], 0);
            BinaryPrimitives.WriteUInt32LittleEndian(record[20..], BinaryPrimitives.ReadUInt32LittleEndian(record[20..]) & ~(uint)LiveMask);
        }

        var indexBuilder = new IdentityGuidIndex.Builder(source.Index, ledgerPages, GetTreeDirectoryCapacity(total, assignments.Length));

        for (var index = 0; index < assignments.Length; index++)
        {
            var ordinal = checked((uint)(source.AssignmentCount + index + 1));
            records.AsSpan(index * RecordSize, RecordSize).CopyTo(GetWritableRecord(ledgerPages, ledgerOwned, ordinal));
            var assignment = assignments[index];
            (assignment.Kind == IdentityKind.Entity ? entityReverse : sceneReverse)[assignment.Locator] = ordinal;
            indexBuilder.Insert(ordinal);
        }

        var candidate = new State(ledgerPages, entityReverse, sceneReverse, indexBuilder.Finish(), total, entityCount);
        return new PreparedChange(this, source, candidate, reservation);
    }

    internal void ReleaseOwnedStorage()
        => _state = s_emptyState;

    internal bool TryFind(Guid stableId, out uint ordinal)
    {
        if (stableId == Guid.Empty)
        {
            throw new ArgumentException("The stable identity cannot be empty.", nameof(stableId));
        }

        Span<byte> bytes = stackalloc byte[16];
        _ = stableId.TryWriteBytes(bytes, bigEndian: true, out _);
        ordinal = _state.Index.Find(bytes);
        return ordinal != 0;
    }

    internal bool TryGetLiveLocator(Guid stableId, IdentityKind kind, out uint locator)
    {
        ValidateKind(kind);
        locator = 0;

        if (TryFind(stableId, out var ordinal))
        {
            var record = GetRecord(_state.LedgerPages, ordinal);

            if (GetKind(record) == kind)
            {
                locator = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
            }
        }

        return locator != 0;
    }

    private int GetTreeDirectoryCapacity(int total, int assignmentCount)
        => (int)Math.Min(IdentityGuidIndex.GetMaximumPages(total), _state.Index.PageCount + ((long)assignmentCount * 4));

    private void ValidateAssignment(IdentityAssignment assignment)
    {
        ValidateLocator(assignment.Kind, assignment.Locator);
        ArgumentOutOfRangeException.ThrowIfNegative(assignment.BranchIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(assignment.BranchIndex, MaximumBranches);

        if (assignment.Origin == IdentityOrigin.Automatic)
        {
            if (assignment.IssuanceNamespace == Guid.Empty || assignment.StableId != Guid.Empty)
            {
                throw new ArgumentException("Automatic assignment requires an admitted namespace and no supplied stable ID.", nameof(assignment));
            }
        }
        else if (assignment.Origin is IdentityOrigin.Explicit or IdentityOrigin.ForkImported)
        {
            if (assignment.StableId == Guid.Empty || assignment.IssuanceNamespace != Guid.Empty)
            {
                throw new ArgumentException("Supplied assignment requires a stable ID and no issuance namespace.", nameof(assignment));
            }

            if (assignment.Origin == IdentityOrigin.Explicit)
            {
                Span<byte> bytes = stackalloc byte[16];
                _ = assignment.StableId.TryWriteBytes(bytes, bigEndian: true, out _);

                if ((bytes[6] & 0xF0) == 0x80 && (bytes[8] & 0xC0) == 0x80)
                {
                    throw new ArgumentException("Explicit identity requests cannot use the engine-reserved RFC UUIDv8 domain.", nameof(assignment));
                }
            }
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(assignment));
        }
    }

    private void ValidateCounts(int assignmentCount, int retirementCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(assignmentCount);
        ArgumentOutOfRangeException.ThrowIfNegative(retirementCount);

        if (assignmentCount > _maximumAssignments - _state.AssignmentCount)
        {
            throw new InvalidOperationException("The cumulative identity assignment limit was exceeded.");
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(retirementCount, _state.AssignmentCount);
    }

    internal sealed class PreparedChange : IDisposable
    {
        private State? _candidate;
        private StableIdentityLedger? _owner;
        private State? _source;

        internal PreparedChange(StableIdentityLedger owner, State source, State candidate, long reservedBytes)
        {
            _owner = owner;
            _source = source;
            _candidate = candidate;
            ReservedBytes = reservedBytes;
        }

        internal long CandidateOwnedByteReservation
            => _candidate?.OwnedByteReservation ?? throw new ObjectDisposedException(nameof(PreparedChange));

        internal long ReservedBytes { get; }

        public void Dispose()
        {
            _candidate = null;
            _owner = null;
            _source = null;
        }

        internal void Commit()
        {
            ValidateForCommit();
            var owner = _owner ?? throw new ObjectDisposedException(nameof(PreparedChange));
            owner._state = _candidate ?? throw new ObjectDisposedException(nameof(PreparedChange));
            Dispose();
        }

        internal Guid GetAssignedId(int assignmentIndex)
        {
            var ordinal = GetAssignedOrdinal(assignmentIndex);
            var candidate = _candidate ?? throw new ObjectDisposedException(nameof(PreparedChange));
            return new Guid(GetRecord(candidate.LedgerPages, ordinal)[..16], bigEndian: true);
        }

        internal uint GetAssignedOrdinal(int assignmentIndex)
        {
            ObjectDisposedException.ThrowIf(_source is null || _candidate is null, this);
            ArgumentOutOfRangeException.ThrowIfNegative(assignmentIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(assignmentIndex, _candidate.AssignmentCount - _source.AssignmentCount);
            return checked((uint)(_source.AssignmentCount + assignmentIndex + 1));
        }

        internal void ValidateForCommit()
        {
            ObjectDisposedException.ThrowIf(_owner is null, this);

            if (!ReferenceEquals(_owner._state, _source))
            {
                throw new InvalidOperationException("The prepared identity change is stale.");
            }
        }
    }

    internal sealed class State
    {
        internal State(byte[][] ledgerPages, Dictionary<uint, uint> entityReverse, Dictionary<uint, uint> sceneReverse, IdentityGuidIndex index, int assignmentCount, int entityCount)
        {
            LedgerPages = ledgerPages;
            EntityReverse = entityReverse;
            SceneReverse = sceneReverse;
            Index = index;
            AssignmentCount = assignmentCount;
            EntityCount = entityCount;
            var bytes = checked(
                ArrayBytes(ledgerPages.Length, IntPtr.Size)
                + DictionaryBytes(entityReverse.EnsureCapacity(0))
                + DictionaryBytes(sceneReverse.EnsureCapacity(0))
                + ArrayBytes(index.DirectoryCapacity, IntPtr.Size)
                + (ledgerPages.Length * ArrayBytes(RecordSize * RecordsPerPage, 1))
                + (index.PageCount * ArrayBytes(IdentityGuidIndex.PageSize, 1)) + 512);

            OwnedByteReservation = bytes;
        }

        internal int AssignmentCount { get; }

        internal int EntityCount { get; }

        internal Dictionary<uint, uint> EntityReverse { get; }

        internal IdentityGuidIndex Index { get; }

        internal byte[][] LedgerPages { get; }

        internal long OwnedByteReservation { get; }

        internal Dictionary<uint, uint> SceneReverse { get; }
    }

    private sealed class AssignmentComparer : IComparer<int>
    {
        private readonly bool _compareLocators;
        private readonly byte[] _records;

        internal AssignmentComparer(byte[] records, bool compareLocators)
        {
            _records = records;
            _compareLocators = compareLocators;
        }

        internal static int CompareLocators(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        {
            var comparison = ((int)GetKind(left)).CompareTo((int)GetKind(right));
            return comparison != 0 ? comparison
                : BinaryPrimitives.ReadUInt32LittleEndian(left[16..]).CompareTo(BinaryPrimitives.ReadUInt32LittleEndian(right[16..]));
        }

        public int Compare(int x, int y)
        {
            var left = _records.AsSpan(x * RecordSize, RecordSize);
            var right = _records.AsSpan(y * RecordSize, RecordSize);
            return _compareLocators ? CompareLocators(left, right) : left[..16].SequenceCompareTo(right[..16]);
        }
    }
}
