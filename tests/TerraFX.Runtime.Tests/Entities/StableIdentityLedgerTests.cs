// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests;

[TestFixture]
internal sealed class StableIdentityLedgerTests
{
    private const long SharedBytes = 2L * 1024 * 1024 * 1024;
    private static readonly Guid s_namespace = new("01234567-89ab-4cde-8f01-23456789abcd");

    private static IdentityAssignment Automatic(uint locator, IdentityKind kind = IdentityKind.Entity, int branch = 0, Guid? issuanceNamespace = null)
        => new(kind, IdentityOrigin.Automatic, locator, branch, issuanceNamespace: issuanceNamespace ?? s_namespace);

    private static void Commit(StableIdentityLedger ledger, IdentityAssignment[] assignments, uint[]? retirements = null)
    {
        using var change = ledger.Prepare(assignments, retirements ?? [], SharedBytes);
        change.Commit();
    }

    private static Guid CreateId(int value) => new(value, 0, 0x4000, 0x80, 0, 0, 0, 0, 0, 0, 1);

    private static IdentityAssignment Explicit(Guid id, uint locator, IdentityKind kind = IdentityKind.Entity, int branch = 0)
        => new(kind, IdentityOrigin.Explicit, locator, branch, id);

    private static byte[] GetRecord(StableIdentityLedger ledger, uint ordinal)
    {
        var result = new byte[StableIdentityLedger.RecordSize];
        ledger.CopyRecord(ordinal, result);
        return result;
    }

    private static byte[][] GetTreePages(StableIdentityLedger ledger)
    {
        var result = new byte[ledger.IndexPageCount][];

        for (var index = 0; index < result.Length; index++)
        {
            result[index] = new byte[IdentityGuidIndex.PageSize];
            ledger.CopyIndexPage(index, result[index]);
        }

        return result;
    }

    private static void VerifyTree(StableIdentityLedger ledger, int expectedCount)
    {
        var pages = GetTreePages(ledger);
        var visited = new HashSet<int>();
        var ordered = new List<uint>(expectedCount);
        _ = VisitTree(ledger, pages, ledger.IndexRoot, isRoot: true, visited, ordered);
        Assert.That(visited.Count, Is.EqualTo(pages.Length));
        Assert.That(ordered.Count, Is.EqualTo(expectedCount));
        var previous = new byte[16];
        var record = new byte[24];

        for (var index = 0; index < ordered.Count; index++)
        {
            ledger.CopyRecord(ordered[index], record);

            if (index != 0)
            {
                Assert.That(previous.AsSpan().SequenceCompareTo(record.AsSpan(0, 16)), Is.LessThan(0));
            }

            record.AsSpan(0, 16).CopyTo(previous);
        }

        Assert.That(ledger.IndexPageCount, Is.LessThanOrEqualTo(IdentityGuidIndex.GetMaximumPages(expectedCount)));
    }

    private static uint VisitTree(StableIdentityLedger ledger, byte[][] pages, int pageIndex, bool isRoot, HashSet<int> visited, List<uint> ordered)
    {
        Assert.That(visited.Add(pageIndex), Is.True);
        var page = pages[pageIndex];
        var count = BinaryPrimitives.ReadInt32LittleEndian(page);
        var level = BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(4));
        var stride = level == 0 ? 4 : 8;
        Assert.That(page.AsSpan(8, IdentityGuidIndex.HeaderSize - 8).IndexOfAnyExcept((byte)0), Is.EqualTo(-1));
        Assert.That(page.AsSpan(IdentityGuidIndex.HeaderSize + (count * stride)).IndexOfAnyExcept((byte)0), Is.EqualTo(-1));
        Assert.That(count, Is.InRange(isRoot ? (level == 0 ? 1 : 2) : (level == 0 ? 508 : 254), level == 0 ? 1016 : 508));
        uint first = 0;

        for (var index = 0; index < count; index++)
        {
            var entry = page.AsSpan(IdentityGuidIndex.HeaderSize + (index * stride), stride);
            var ordinal = BinaryPrimitives.ReadUInt32LittleEndian(entry);

            if (index == 0)
            {
                first = ordinal;
            }

            if (level == 0)
            {
                ordered.Add(ordinal);
            }
            else
            {
                var childIndex = BinaryPrimitives.ReadInt32LittleEndian(entry[4..]);
                Assert.That(BinaryPrimitives.ReadInt32LittleEndian(pages[childIndex].AsSpan(4)), Is.EqualTo(level - 1));
                Assert.That(VisitTree(ledger, pages, childIndex, isRoot: false, visited, ordered), Is.EqualTo(ordinal));
            }
        }

        return first;
    }

    [Test]
    public void AllocatedEntitySlotLimitRetainsRetiredSlotsAndAllowsOnlyExistingSlotReuse()
    {
        var ledger = StableIdentityLedger.CreateForTesting(8, 2, 2);
        Commit(ledger, [Automatic(10), Automatic(20)]);
        Assert.That(ledger.EntitySlotCount, Is.EqualTo(2));
        Commit(ledger, [], [1, 2]);
        Assert.That(ledger.EntityCount, Is.Zero);
        Assert.That(ledger.EntitySlotCount, Is.EqualTo(2));
        _ = Assert.Throws<InvalidOperationException>(() => ledger.Prepare([Automatic(30)], [], SharedBytes));
        Assert.That(ledger.AssignmentCount, Is.EqualTo(2));
        Commit(ledger, [Automatic(10)]);
        Assert.That(ledger.EntitySlotCount, Is.EqualTo(2));
        Assert.That(ledger.EntityCount, Is.EqualTo(1));
        Assert.That(ledger.GetLiveOrdinal(IdentityKind.Entity, 10), Is.EqualTo(3));
        Assert.That(ledger.GetLiveOrdinal(IdentityKind.Entity, 20), Is.Zero);
        Assert.That(ledger.GetAssignedId(IdentityKind.Entity, 10),
            Is.EqualTo(new Guid("2f8db826-3b88-86f0-8f13-e85196da136f")));
    }

    [Test]
    public void AutomaticAndExplicitAssignmentsShareOneCommittedCursor()
    {
        var ledger = new StableIdentityLedger();
        var explicitId = CreateId(70);
        Commit(ledger, [Automatic(1), Explicit(explicitId, 2), Automatic(1, IdentityKind.SceneInstance), Automatic(3)]);
        Assert.That(ledger.AssignmentCount, Is.EqualTo(4));
        Assert.That(ledger.GetAssignedId(IdentityKind.Entity, 1),
            Is.EqualTo(new Guid("bf19cbe6-fbca-8286-862f-e1de769cec9c")));
        Assert.That(ledger.GetAssignedId(IdentityKind.Entity, 2), Is.EqualTo(explicitId));
        Assert.That(ledger.GetAssignedId(IdentityKind.SceneInstance, 1),
            Is.EqualTo(new Guid("1d204792-01ae-81cf-918c-1536551506d9")));
        Assert.That(ledger.GetAssignedId(IdentityKind.Entity, 3),
            Is.EqualTo(new Guid("d30b697d-5729-8f64-b5f3-a23e4d838f29")));
        Assert.That(ledger.EntityCount, Is.EqualTo(3));
        Assert.That(ledger.SceneInstanceCount, Is.EqualTo(1));
    }

    [Test]
    public void AutomaticNamesUseInvariantSixteenLowercaseHexDigits()
    {
        var oldCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            var assignments = new IdentityAssignment[16];

            for (var index = 0; index < assignments.Length; index++)
            {
                assignments[index] = Automatic((uint)index + 1);
            }

            var ledger = new StableIdentityLedger();
            Commit(ledger, assignments);
            Assert.That(ledger.GetAssignedId(IdentityKind.Entity, 10),
                Is.EqualTo(new Guid("12c8eae4-a1f6-8a05-bb8b-5fec82358b19")));
            Assert.That(ledger.GetAssignedId(IdentityKind.Entity, 16),
                Is.EqualTo(new Guid("7c0ed2fd-4100-8b7d-ab3f-f9c6049d4c99")));
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
        }
    }

    [TestCase(1016, 1, 1)]
    [TestCase(1017, 3, 2)]
    [TestCase(4096, 9, 2)]
    [TestCase(4097, 9, 2)]
    [TestCase(258_572, 509, 2)]
    [TestCase(258_573, 512, 3)]
    public void BPlusTreeSplitsAndGrowsRootAtExactBoundaries(int count, int expectedPages, int expectedHeight)
    {
        var ledger = new StableIdentityLedger();
        var assignments = new IdentityAssignment[count];

        for (var index = 0; index < count; index++)
        {
            assignments[index] = Explicit(CreateId(index + 1), (uint)index + 1);
        }

        Commit(ledger, assignments);
        Assert.That(ledger.IndexPageCount, Is.EqualTo(expectedPages));
        Assert.That(ledger.IndexHeight, Is.EqualTo(expectedHeight));
        Assert.That(ledger.LedgerPageCount, Is.EqualTo((count + 4095) / 4096));
        VerifyTree(ledger, count);

        for (var index = 0; index < count; index++)
        {
            Assert.That(ledger.TryFind(assignments[index].StableId, out var ordinal), Is.True);
            Assert.That(ordinal, Is.EqualTo(index + 1));
        }
    }

    [Test]
    public void ConfiguredCountLimitsAreExactAndRetirementRemainsPossibleAtExhaustion()
    {
        var ledger = StableIdentityLedger.CreateForTesting(4, 2, 2);
        Commit(ledger, [Automatic(1), Automatic(2), Automatic(1, IdentityKind.SceneInstance), Automatic(2, IdentityKind.SceneInstance)]);
        Assert.That(ledger.AssignmentCount, Is.EqualTo(4));
        _ = Assert.Throws<InvalidOperationException>(() => ledger.Prepare([Automatic(3)], [], SharedBytes));
        Commit(ledger, [], [1, 2, 3, 4]);
        Assert.That(ledger.AssignmentCount, Is.EqualTo(4));
        Assert.That(ledger.EntityCount, Is.Zero);
        Assert.That(ledger.SceneInstanceCount, Is.Zero);
        _ = Assert.Throws<InvalidOperationException>(() => ledger.Prepare([Automatic(3)], [], SharedBytes));
    }

    [Test]
    public void DiscardAndStalePreparationConsumeNothing()
    {
        var ledger = new StableIdentityLedger();
        using (var discarded = ledger.Prepare([Automatic(1)], [], SharedBytes))
        {
            Assert.That(ledger.AssignmentCount, Is.Zero);
        }

        using var stale = ledger.Prepare([Automatic(2)], [], SharedBytes);
        Commit(ledger, [Automatic(1)]);
        var before = GetRecord(ledger, 1);
        _ = Assert.Throws<InvalidOperationException>(stale.ValidateForCommit);
        _ = Assert.Throws<InvalidOperationException>(stale.Commit);
        Assert.That(GetRecord(ledger, 1), Is.EqualTo(before));
        Assert.That(ledger.AssignmentCount, Is.EqualTo(1));
        Assert.That(ledger.GetLiveOrdinal(IdentityKind.Entity, 2), Is.Zero);
    }

    [TestCase(IdentityKind.Entity)]
    [TestCase(IdentityKind.SceneInstance)]
    public void DuplicateGuidIncludingRetiredHistoryRejectsWithoutMutation(IdentityKind kind)
    {
        var ledger = new StableIdentityLedger();
        var id = CreateId(50);
        Commit(ledger, [Explicit(id, 1, kind)]);
        _ = Assert.Throws<ArgumentException>(() => ledger.Prepare([Explicit(id, 2)], [], SharedBytes));
        Commit(ledger, [], [1]);
        var before = GetRecord(ledger, 1);
        _ = Assert.Throws<ArgumentException>(() => ledger.Prepare([Automatic(3), Explicit(id, 2)], [], SharedBytes));
        Assert.That(GetRecord(ledger, 1), Is.EqualTo(before));
        Assert.That(ledger.AssignmentCount, Is.EqualTo(1));
        Assert.That(ledger.TryFind(id, out var ordinal), Is.True);
        Assert.That(ordinal, Is.EqualTo(1));
        Assert.That(ledger.TryGetLiveLocator(id, kind, out var locator), Is.False);
        Assert.That(locator, Is.Zero);
        Commit(ledger, [Automatic(2)]);
        Assert.That(ledger.GetAssignedId(IdentityKind.Entity, 2),
            Is.EqualTo(new Guid("fb8d9322-d125-8eac-a4f6-263bcca2f93d")));
    }

    [Test]
    public void DuplicateGuidWithinBatchAndAutomaticCollisionReject()
    {
        var ledger = new StableIdentityLedger();
        var id = CreateId(1);
        _ = Assert.Throws<ArgumentException>(() => ledger.Prepare([Explicit(id, 1), Explicit(id, 2, IdentityKind.SceneInstance)], [], SharedBytes));
        var generated = new Guid("bf19cbe6-fbca-8286-862f-e1de769cec9c");
        var imported = new IdentityAssignment(IdentityKind.SceneInstance, IdentityOrigin.ForkImported, 2, 0, generated);
        _ = Assert.Throws<ArgumentException>(() => ledger.Prepare([Automatic(1), imported], [], SharedBytes));
        Assert.That(ledger.AssignmentCount, Is.Zero);
        Assert.That(ledger.IndexPageCount, Is.Zero);
        Assert.That(ledger.IndexRoot, Is.EqualTo(-1));
        Commit(ledger, [Automatic(1)]);
        Assert.That(ledger.GetAssignedId(IdentityKind.Entity, 1), Is.EqualTo(generated));
    }

    [TestCase(IdentityKind.Entity)]
    [TestCase(IdentityKind.SceneInstance)]
    public void ExactLiveCountLimitAndSameBatchReuse(IdentityKind kind)
    {
        var ledger = StableIdentityLedger.CreateForTesting(10, 2, 2);
        Commit(ledger, [Automatic(1, kind), Automatic(2, kind)]);
        var before = GetRecord(ledger, 1);
        _ = Assert.Throws<InvalidOperationException>(() => ledger.Prepare([Automatic(3, kind)], [], SharedBytes));
        Assert.That(GetRecord(ledger, 1), Is.EqualTo(before));
        Commit(ledger, [Automatic(1, kind)], [1]);
        Assert.That(ledger.AssignmentCount, Is.EqualTo(3));
        Assert.That(ledger.GetLiveOrdinal(kind, 1), Is.EqualTo(3));
        Commit(ledger, [], [2, 3]);
        Assert.That(ledger.GetLiveOrdinal(kind, 1), Is.Zero);
    }

    [TestCase("00112233-4455-8677-8899-aabbccddeeff", false)]
    [TestCase("00112233-4455-8677-b899-aabbccddeeff", false)]
    [TestCase("00112233-4455-8677-7899-aabbccddeeff", true)]
    [TestCase("00112233-4455-8677-c899-aabbccddeeff", true)]
    [TestCase("00112233-4455-7677-8899-aabbccddeeff", true)]
    [TestCase("00112233-4455-9677-8899-aabbccddeeff", true)]
    public void ExplicitRestrictionsUseExactlyRfcVariantUuidV8(string text, bool accepted)
    {
        var ledger = new StableIdentityLedger();
        var assignment = Explicit(new Guid(text), 1);

        if (accepted)
        {
            Commit(ledger, [assignment]);
            Assert.That(ledger.AssignmentCount, Is.EqualTo(1));
        }
        else
        {
            _ = Assert.Throws<ArgumentException>(() => ledger.Prepare([assignment], [], SharedBytes));
            Assert.That(ledger.AssignmentCount, Is.Zero);
        }
    }

    [Test]
    public void FailedPreparationPreservesEveryCommittedPageAndReverseLookup()
    {
        var ledger = new StableIdentityLedger();
        var assignments = new IdentityAssignment[5000];

        for (var index = 0; index < assignments.Length; index++)
        {
            assignments[index] = Explicit(CreateId(index + 1), (uint)index + 1);
        }

        Commit(ledger, assignments);
        var tree = GetTreePages(ledger);
        var records = new byte[assignments.Length][];

        for (var index = 0; index < records.Length; index++)
        {
            records[index] = GetRecord(ledger, (uint)index + 1);
        }

        var root = ledger.IndexRoot;
        var ownedBytes = ledger.OwnedByteReservation;
        var grant = ledger.GetPreparationByteReservation(1, 2);
        _ = Assert.Throws<InvalidOperationException>(() => ledger.Prepare([Automatic(5001)], [1, 4097], grant - 1));
        _ = Assert.Throws<ArgumentException>(() => ledger.Prepare([Explicit(CreateId(4097), 5001)], [1, 4097], SharedBytes));
        _ = Assert.Throws<ArgumentException>(() => ledger.Prepare([Automatic(5001)], [1, 1], SharedBytes));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => ledger.Prepare([Automatic(5001)], [1, 5001], SharedBytes));
        _ = Assert.Throws<ArgumentException>(() => ledger.Prepare([Automatic(2)], [1], SharedBytes));
        Assert.That(GetTreePages(ledger), Is.EqualTo(tree));
        Assert.That(ledger.IndexRoot, Is.EqualTo(root));
        Assert.That(ledger.OwnedByteReservation, Is.EqualTo(ownedBytes));

        for (var index = 0; index < records.Length; index++)
        {
            var ordinal = (uint)index + 1;
            Assert.That(GetRecord(ledger, ordinal), Is.EqualTo(records[index]));
            Assert.That(ledger.GetLiveOrdinal(IdentityKind.Entity, ordinal), Is.EqualTo(ordinal));
        }

        Commit(ledger, [Automatic(5001)], [1, 4097]);
        Assert.That(ledger.AssignmentCount, Is.EqualTo(5001));
        Assert.That(ledger.GetAssignedId(IdentityKind.Entity, 5001),
            Is.EqualTo(new Guid("dae5d0fa-daae-82a7-b710-859212784b46")));
    }

    [Test]
    public void ForkImportedAssignmentsRetainSuppliedUuidAndConsumeCursor()
    {
        var id = new Guid("f07365df-3292-8169-9795-04bc631a1574");
        var ledger = new StableIdentityLedger();
        Commit(ledger, [new IdentityAssignment(IdentityKind.Entity, IdentityOrigin.ForkImported, 1, 1023, id), Automatic(2)]);
        var record = GetRecord(ledger, 1);
        Assert.That(new Guid(record.AsSpan(0, 16), bigEndian: true), Is.EqualTo(id));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(20)), Is.EqualTo(0x77FF));
        Assert.That(ledger.GetAssignedId(IdentityKind.Entity, 2),
            Is.EqualTo(new Guid("fb8d9322-d125-8eac-a4f6-263bcca2f93d")));
    }

    [Test]
    public void IndependentOwnersAndPreparedInputCopiesCannotAlias()
    {
        var first = new StableIdentityLedger();
        var second = new StableIdentityLedger();
        var requests = new IdentityAssignment[] { Automatic(1) };
        using var prepared = first.Prepare(requests, [], SharedBytes);
        requests[0] = Explicit(CreateId(100), 2);
        prepared.Commit();
        Commit(second, [Automatic(1)]);
        Assert.That(first.GetAssignedId(IdentityKind.Entity, 1), Is.EqualTo(second.GetAssignedId(IdentityKind.Entity, 1)));
        Commit(first, [], [1]);
        Assert.That(first.GetLiveOrdinal(IdentityKind.Entity, 1), Is.Zero);
        Assert.That(first.GetLiveOrdinal(IdentityKind.Entity, 2), Is.Zero);
        Assert.That(second.GetLiveOrdinal(IdentityKind.Entity, 1), Is.EqualTo(1));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void InvalidLookupRepresentationsThrow(int value)
    {
        var ledger = new StableIdentityLedger();
        _ = Assert.Throws<ArgumentException>(() => ledger.TryFind(Guid.Empty, out _));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => ledger.GetLiveOrdinal((IdentityKind)value, 1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => ledger.GetLiveOrdinal(IdentityKind.Entity, 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => ledger.CopyRecord(0, new byte[24]));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => ledger.CopyRecord(1, new byte[24]));
        _ = Assert.Throws<ArgumentException>(() => ledger.GetAssignedId(IdentityKind.Entity, 1));
        Assert.That(ledger.TryFind(CreateId(1), out var ordinal), Is.False);
        Assert.That(ordinal, Is.Zero);
    }

    [Test]
    public void LookupAndPreparedCommitAllocateNoManagedBytes()
    {
        var ledger = new StableIdentityLedger();
        var assignments = new IdentityAssignment[8192];

        for (var index = 0; index < assignments.Length; index++)
        {
            assignments[index] = Explicit(CreateId(index + 1), (uint)index + 1);
        }

        Commit(ledger, assignments);
        var id = assignments[4096].StableId;
        var absent = CreateId(9000);
        var warmup = ledger.Prepare([], [], SharedBytes);
        warmup.Commit();
        using var change = ledger.Prepare([Explicit(CreateId(9001), 9001)], [], SharedBytes);

        for (var index = 0; index < 1000; index++)
        {
            _ = ledger.TryFind(id, out _);
            _ = ledger.TryFind(absent, out _);
            _ = ledger.TryGetLiveLocator(id, IdentityKind.Entity, out _);
            _ = ledger.TryGetLiveLocator(id, IdentityKind.SceneInstance, out _);
            _ = ledger.GetAssignedId(IdentityKind.Entity, 4097);
            _ = ledger.GetLiveOrdinal(IdentityKind.Entity, 4097);
            change.ValidateForCommit();
        }

        long checksum = 0;
        var watch = Stopwatch.StartNew();
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 100_000; index++)
        {
            checksum += ledger.TryFind(id, out var ordinal) ? ordinal : 0;
            checksum += ledger.TryFind(absent, out _) ? 1 : 0;
            checksum += ledger.TryGetLiveLocator(id, IdentityKind.Entity, out var locator) ? locator : 0;
            checksum += ledger.TryGetLiveLocator(id, IdentityKind.SceneInstance, out _) ? 1 : 0;
            checksum += ledger.GetLiveOrdinal(IdentityKind.Entity, 4097);
            checksum += ledger.GetAssignedId(IdentityKind.Entity, 4097) == id ? 1 : 0;
        }

        var lookupBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        watch.Stop();
        before = GC.GetAllocatedBytesForCurrentThread();
        change.Commit();
        var commitBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(checksum, Is.EqualTo(1_229_200_000));
        Assert.That(lookupBytes, Is.Zero);
        Assert.That(commitBytes, Is.Zero);
        TestContext.Out.WriteLine($"Runtime={RuntimeInformation.FrameworkDescription}; Architecture={RuntimeInformation.ProcessArchitecture}; Records=8192; LookupCalls=600000; AllocatedBytes={lookupBytes}; ElapsedMs={watch.Elapsed.TotalMilliseconds:F3}; CommitAllocatedBytes={commitBytes}");
    }

    [Test]
    public void NamespacesAndCreationBranchIndicesAreInputsNotInventedLifecycle()
    {
        var ledger = new StableIdentityLedger();
        var divergentNamespace = CreateId(600);
        Commit(ledger, [Automatic(1, branch: 0), Automatic(2, branch: 1023, issuanceNamespace: divergentNamespace)]);
        Assert.That(ledger.GetAssignedId(IdentityKind.Entity, 1),
            Is.EqualTo(new Guid("bf19cbe6-fbca-8286-862f-e1de769cec9c")));
        Assert.That(ledger.GetAssignedId(IdentityKind.Entity, 2),
            Is.EqualTo(new Guid("b22c699f-f0b3-891d-896d-a3e6ec8dcd8a")));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => ledger.Prepare([Automatic(3, branch: -1)], [], SharedBytes));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => ledger.Prepare([Automatic(3, branch: 1024)], [], SharedBytes));
    }

    [Test]
    public void PreparationAllocationFitsItsReservationAndReportsResidentAccounting()
    {
        var ledger = new StableIdentityLedger();
        var assignments = new IdentityAssignment[8192];

        for (var index = 0; index < assignments.Length; index++)
        {
            assignments[index] = Automatic((uint)index + 1);
        }

        using (var warmup = ledger.Prepare([Automatic(1)], [], SharedBytes))
        {
            warmup.ValidateForCommit();
        }

        var reserved = ledger.GetPreparationByteReservation(assignments.Length, 0);
        var before = GC.GetAllocatedBytesForCurrentThread();
        using var change = ledger.Prepare(assignments, [], reserved);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        change.Commit();
        Assert.That(allocated, Is.LessThanOrEqualTo(reserved));
        Assert.That(ledger.OwnedByteReservation, Is.LessThan(reserved));
        TestContext.Out.WriteLine($"Records={ledger.AssignmentCount}; LedgerPayloadBytes={ledger.LedgerPageCount * 4096L * 24}; IndexPageBytes={ledger.IndexPageCount * 4096L}; OwnedByteReservation={ledger.OwnedByteReservation}; PreparationPeakReservation={reserved}; PrepareAllocatedBytes={allocated}");
    }

    [Test]
    public void PreparedChangeIsSingleUseAndDisposedChangeCannotCommit()
    {
        var ledger = new StableIdentityLedger();
        var change = ledger.Prepare([Automatic(1)], [], SharedBytes);
        change.Commit();
        _ = Assert.Throws<ObjectDisposedException>(change.Commit);
        _ = Assert.Throws<ObjectDisposedException>(change.ValidateForCommit);
        var disposed = ledger.Prepare([Automatic(2)], [], SharedBytes);
        disposed.Dispose();
        disposed.Dispose();
        _ = Assert.Throws<ObjectDisposedException>(disposed.Commit);
        Assert.That(ledger.AssignmentCount, Is.EqualTo(1));
    }

    [Test]
    public void PreparedIdsAreReadableForExternalPreparationWithoutPublishing()
    {
        var ledger = new StableIdentityLedger();
        Commit(ledger, [Automatic(1)]);
        using var change = ledger.Prepare([Explicit(CreateId(2), 2), Automatic(3)], [1], SharedBytes);
        Assert.That(change.GetAssignedOrdinal(0), Is.EqualTo(2));
        Assert.That(change.GetAssignedOrdinal(1), Is.EqualTo(3));
        Assert.That(change.GetAssignedId(0), Is.EqualTo(CreateId(2)));
        Assert.That(change.GetAssignedId(1), Is.EqualTo(new Guid("2f8db826-3b88-86f0-8f13-e85196da136f")));
        Assert.That(ledger.AssignmentCount, Is.EqualTo(1));
        Assert.That(ledger.GetLiveOrdinal(IdentityKind.Entity, 1), Is.EqualTo(1));
        Assert.That(ledger.TryFind(change.GetAssignedId(1), out _), Is.False);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => change.GetAssignedOrdinal(-1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => change.GetAssignedId(2));
        change.Commit();
        Assert.That(ledger.AssignmentCount, Is.EqualTo(3));
        Assert.That(ledger.GetLiveOrdinal(IdentityKind.Entity, 1), Is.Zero);
        _ = Assert.Throws<ObjectDisposedException>(() => change.GetAssignedId(0));
    }

    [Test]
    public void ProductionLimitsAndMaximumTreeBoundAreNotLoweredForTests()
    {
        Assert.That(StableIdentityLedger.MaximumAssignments, Is.EqualTo(33_554_432));
        Assert.That(StableIdentityLedger.MaximumBranches, Is.EqualTo(1024));
        Assert.That(StableIdentityLedger.MaximumEntitySlots, Is.EqualTo(2_097_152));
        Assert.That(StableIdentityLedger.MaximumSceneInstances, Is.EqualTo(65_536));
        Assert.That(IdentityGuidIndex.MaximumPageCount, Is.EqualTo(66_315));
        Assert.That(IdentityGuidIndex.GetMaximumPages(StableIdentityLedger.MaximumAssignments), Is.EqualTo(66_315));
        var ledger = new StableIdentityLedger();
        Assert.DoesNotThrow(() => ledger.GetPreparationByteReservation(33_554_432, 0));
        _ = Assert.Throws<InvalidOperationException>(() => ledger.GetPreparationByteReservation(33_554_433, 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => StableIdentityLedger.CreateForTesting(33_554_433, 1, 1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => StableIdentityLedger.CreateForTesting(1, 2_097_153, 1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => StableIdentityLedger.CreateForTesting(1, 1, 65_537));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => StableIdentityLedger.CreateForTesting(-1, 1, 1));
    }

    [Test]
    public void ProductionSceneInstanceLimitAdmitsExactCeilingAndRejectsOneBeyond()
    {
        var ledger = new StableIdentityLedger();
        var assignments = new IdentityAssignment[65_536];

        for (var index = 0; index < assignments.Length; index++)
        {
            assignments[index] = Explicit(CreateId(index + 1), (uint)index + 1, IdentityKind.SceneInstance);
        }

        Commit(ledger, assignments);
        Assert.That(ledger.SceneInstanceCount, Is.EqualTo(65_536));
        _ = Assert.Throws<InvalidOperationException>(() =>
            ledger.Prepare([Explicit(CreateId(65_537), 65_537, IdentityKind.SceneInstance)], [], SharedBytes));
        Assert.That(ledger.AssignmentCount, Is.EqualTo(65_536));
        Commit(ledger, [Explicit(CreateId(65_537), 65_537, IdentityKind.SceneInstance)], [1]);
        Assert.That(ledger.SceneInstanceCount, Is.EqualTo(65_536));
        Assert.That(ledger.GetLiveOrdinal(IdentityKind.SceneInstance, 1), Is.Zero);
        Assert.That(ledger.GetLiveOrdinal(IdentityKind.SceneInstance, 65_537), Is.EqualTo(65_537));
    }

    [TestCase(IdentityKind.Entity, IdentityOrigin.Automatic, 0, 0x5400U)]
    [TestCase(IdentityKind.Entity, IdentityOrigin.Explicit, 1023, 0x67FFU)]
    [TestCase(IdentityKind.Entity, IdentityOrigin.ForkImported, 1023, 0x77FFU)]
    [TestCase(IdentityKind.SceneInstance, IdentityOrigin.Automatic, 1023, 0x5BFFU)]
    [TestCase(IdentityKind.SceneInstance, IdentityOrigin.Explicit, 0, 0x6800U)]
    [TestCase(IdentityKind.SceneInstance, IdentityOrigin.ForkImported, 1023, 0x7BFFU)]
    public void RecordEncodingIsExactlyTwentyFourBytes(IdentityKind kind, IdentityOrigin origin, int branch, uint metadata)
    {
        var ledger = new StableIdentityLedger();
        var id = new Guid("00112233-4455-4677-8899-aabbccddeeff");
        var assignment = origin == IdentityOrigin.Automatic
            ? Automatic(0x01020304, kind, branch)
            : new IdentityAssignment(kind, origin, 0x01020304, branch, id);
        Commit(ledger, [assignment]);
        var bytes = GetRecord(ledger, 1);
        var expected = new byte[24];
        var actualId = origin == IdentityOrigin.Automatic
            ? new Guid(kind == IdentityKind.Entity ? "bf19cbe6-fbca-8286-862f-e1de769cec9c" : "1dd643db-ccd3-865c-a6cc-9e816eb558a9")
            : id;
        _ = actualId.TryWriteBytes(expected.AsSpan(0, 16), bigEndian: true, out _);
        expected[16] = 4;
        expected[17] = 3;
        expected[18] = 2;
        expected[19] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(20), metadata);
        Assert.That(bytes, Is.EqualTo(expected));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20)) & 0xFFFF8000, Is.Zero);
        Assert.That(ledger.GetLiveOrdinal(kind, 0x01020304), Is.EqualTo(1));

        if (origin != IdentityOrigin.Automatic)
        {
            byte[] rfc = [0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x46, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF];
            Assert.That(bytes.AsSpan(0, 16).ToArray(), Is.EqualTo(rfc));
        }

        Commit(ledger, [], [1]);
        expected.AsSpan(16, 4).Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(20), metadata & ~(1U << 14));
        Assert.That(GetRecord(ledger, 1), Is.EqualTo(expected));
    }

    [TestCase(IdentityKind.Entity)]
    [TestCase(IdentityKind.SceneInstance)]
    public void RetirementClearsOnlyLocatorAndLiveMetadataAndPreservesIndex(IdentityKind kind)
    {
        var ledger = new StableIdentityLedger();
        Commit(ledger, [Explicit(CreateId(1), uint.MaxValue, kind, 1023)]);
        var before = GetRecord(ledger, 1);
        var pages = GetTreePages(ledger);
        Commit(ledger, [], [1]);
        var after = GetRecord(ledger, 1);
        var expected = (byte[])before.Clone();
        expected.AsSpan(16, 4).Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(20), BinaryPrimitives.ReadUInt32LittleEndian(expected.AsSpan(20)) & ~(1U << 14));
        Assert.That(after, Is.EqualTo(expected));
        Assert.That(GetTreePages(ledger), Is.EqualTo(pages));
        Assert.That(ledger.GetLiveOrdinal(kind, uint.MaxValue), Is.Zero);
        Assert.That(ledger.AssignmentCount, Is.EqualTo(1));
        _ = Assert.Throws<ArgumentException>(() => ledger.Prepare([], [1], SharedBytes));
        Commit(ledger, [Automatic(uint.MaxValue, kind)]);
        Assert.That(ledger.GetLiveOrdinal(kind, uint.MaxValue), Is.EqualTo(2));
    }

    [Test]
    public void ReverseLocatorsAreUniqueWithinKindNotAcrossKinds()
    {
        var ledger = new StableIdentityLedger();
        _ = Assert.Throws<ArgumentException>(() => ledger.Prepare([Automatic(1), Automatic(1)], [], SharedBytes));
        Commit(ledger, [Automatic(1), Automatic(1, IdentityKind.SceneInstance)]);
        Assert.That(ledger.GetLiveOrdinal(IdentityKind.Entity, 1), Is.EqualTo(1));
        Assert.That(ledger.GetLiveOrdinal(IdentityKind.SceneInstance, 1), Is.EqualTo(2));
        Assert.That(ledger.TryGetLiveLocator(ledger.GetAssignedId(IdentityKind.Entity, 1), IdentityKind.SceneInstance, out var locator), Is.False);
        Assert.That(locator, Is.Zero);
    }

    [Test]
    public void RfcOrderingUsesAllSixteenBytesNotClrMixedEndianOrPrefixes()
    {
        var ids = new Guid[]
        {
            new("01000000-0000-4000-8000-000000000001"),
            new("00000001-0000-4000-8000-000000000001"),
            new("00000001-0000-4000-8000-000000000002"),
            new("00000001-0100-4000-8000-000000000001"),
            new("00000001-0001-4000-8000-000000000001"),
            new("00000001-0001-4001-8000-000000000001"),
            new("00000001-0001-4001-8100-000000000001"),
        };
        var ledger = new StableIdentityLedger();
        var assignments = new IdentityAssignment[ids.Length];

        for (var index = 0; index < ids.Length; index++)
        {
            assignments[index] = Explicit(ids[index], (uint)index + 1);
        }

        Commit(ledger, assignments);
        VerifyTree(ledger, ids.Length);
        var page = new byte[4096];
        ledger.CopyIndexPage(ledger.IndexRoot, page);
        uint[] expected = [2, 3, 5, 6, 7, 4, 1];

        for (var index = 0; index < expected.Length; index++)
        {
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(32 + (index * 4))), Is.EqualTo(expected[index]));
            Assert.That(ledger.TryFind(ids[index], out var ordinal), Is.True);
            Assert.That(ordinal, Is.EqualTo(index + 1));
        }
    }

    [TestCase(1)]
    [TestCase(2718)]
    [TestCase(31415)]
    public void ShuffledAndIncrementalInsertionsKeepTreeDeterministicAndCorrect(int seed)
    {
        const int Count = 12_000;
        var random = new Random(seed);
        var ids = new int[Count];

        for (var index = 0; index < Count; index++)
        {
            ids[index] = index + 1;
        }

        random.Shuffle(ids);
        var all = new IdentityAssignment[Count];
        var single = new StableIdentityLedger();
        var incremental = new StableIdentityLedger();

        for (var index = 0; index < Count; index++)
        {
            all[index] = Explicit(CreateId(ids[index]), (uint)index + 1);
        }

        Commit(single, all);

        for (var start = 0; start < Count; start += 137)
        {
            Commit(incremental, all.AsSpan(start, Math.Min(137, Count - start)).ToArray());
        }

        VerifyTree(incremental, Count);
        Assert.That(incremental.IndexRoot, Is.EqualTo(single.IndexRoot));
        Assert.That(GetTreePages(incremental), Is.EqualTo(GetTreePages(single)));

        for (var index = 0; index < Count; index++)
        {
            Assert.That(incremental.TryFind(all[index].StableId, out var ordinal), Is.True);
            Assert.That(ordinal, Is.EqualTo(index + 1));
            Assert.That(GetRecord(incremental, ordinal), Is.EqualTo(GetRecord(single, ordinal)));
        }
    }

    [Test]
    public void ValidationRejectsMalformedRequestsAndCountsBeforeMutation()
    {
        var ledger = new StableIdentityLedger();
        IdentityAssignment[] invalid =
        [
            default,
            new(IdentityKind.Entity, IdentityOrigin.Automatic, 1, 0),
            new(IdentityKind.Entity, IdentityOrigin.Automatic, 1, 0, CreateId(1), s_namespace),
            new(IdentityKind.Entity, IdentityOrigin.Explicit, 1, 0),
            new(IdentityKind.Entity, IdentityOrigin.Explicit, 1, 0, CreateId(1), s_namespace),
            new(IdentityKind.Entity, 0, 1, 0, CreateId(1)),
            new(IdentityKind.Entity, (IdentityOrigin)4, 1, 0, CreateId(1)),
            new((IdentityKind)3, IdentityOrigin.Explicit, 1, 0, CreateId(1)),
            new(IdentityKind.SceneInstance, IdentityOrigin.Explicit, 0, 0, CreateId(1)),
        ];

        foreach (var assignment in invalid)
        {
            _ = Assert.Catch<ArgumentException>(() => ledger.Prepare([assignment], [], SharedBytes));
            Assert.That(ledger.AssignmentCount, Is.Zero);
            Assert.That(ledger.IndexPageCount, Is.Zero);
            Assert.That(ledger.LedgerPageCount, Is.Zero);
        }

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => ledger.GetPreparationByteReservation(-1, 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => ledger.GetPreparationByteReservation(0, -1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => ledger.Prepare([], [0], SharedBytes));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => ledger.Prepare([], [], -1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => ledger.Prepare([], [], SharedBytes + 1));
    }

    [Test]
    public void ZeroConfiguredLimitsAdmitNoCreationButAnEmptyPreparation()
    {
        var ledger = StableIdentityLedger.CreateForTesting(0, 0, 0);
        Commit(ledger, []);
        _ = Assert.Throws<InvalidOperationException>(() => ledger.Prepare([Automatic(1)], [], SharedBytes));
        Assert.That(ledger.AssignmentCount, Is.Zero);
        Assert.That(ledger.IndexHeight, Is.Zero);
        Assert.That(ledger.IndexRoot, Is.EqualTo(-1));
    }
}
