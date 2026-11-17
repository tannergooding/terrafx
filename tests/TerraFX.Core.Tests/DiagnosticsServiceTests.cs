// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class DiagnosticsServiceTests
{
    private static Diagnostic CreateDiagnostic(
        string code,
        DiagnosticSeverity severity,
        long timestamp,
        Guid? worldGuid = null,
        ImmutableArray<DiagnosticContextEntry> context = default)
        => new(
            code,
            severity,
            timestamp,
            null,
            worldGuid,
            null,
            null,
            null,
            null,
            null,
            timestamp,
            null,
            null,
            null,
            null,
            "TestOperation",
            null,
            "Test diagnostic.",
            context.IsDefault ? [] : context);

    [Test]
    public void BorrowedWorldCapabilityCloseRetainsHostService()
    {
        var service = new DiagnosticsService(2);
        var capability = service.CreateBorrowedWorldCapability(Guid.NewGuid(), Guid.NewGuid());
        capability.BindOwner(1);

        capability.Close();

        Assert.That(service.TryGetLatestFault(out var latestFault), Is.False);
        Assert.That(latestFault, Is.Null);
        _ = Assert.Throws<ObjectDisposedException>(() => capability.GetTimestamp());
        service.Close();
    }

    [Test]
    public void CloseRetiresServiceStorageAndRejectsFutureUse()
    {
        var service = new DiagnosticsService(2);
        service.Write(CreateDiagnostic("retained", DiagnosticSeverity.Information, 1), null, null);
        service.Close();

        _ = Assert.Throws<ObjectDisposedException>(() =>
            service.GetSnapshot(new DiagnosticQuery(1, DiagnosticSeverity.Trace, null, null)));
        _ = Assert.Throws<ObjectDisposedException>(() => service.TryGetLatestFault(out _));
        _ = Assert.Throws<ObjectDisposedException>(() =>
            service.Write(CreateDiagnostic("closed", DiagnosticSeverity.Critical, 2), null, null));
    }

    [Test]
    public void ConfiguredCapacityRetainsOnlyItsAdmittedRecords()
    {
        var maximumRecords = DiagnosticsService.GetMaximumRecordCount(4096);
        var service = new DiagnosticsService(2);
        Assert.That(service.OwnedByteReservation, Is.LessThanOrEqualTo(4096));
        Assert.That(maximumRecords, Is.GreaterThanOrEqualTo(2));

        service.Write(CreateDiagnostic("first", DiagnosticSeverity.Information, 1), null, null);
        service.Write(CreateDiagnostic("second", DiagnosticSeverity.Information, 2), null, null);
        service.Write(CreateDiagnostic("third", DiagnosticSeverity.Information, 3), null, null);

        var snapshot = service.GetSnapshot(new DiagnosticQuery(8192, DiagnosticSeverity.Information, null, null));
        Assert.That(snapshot.Records, Has.Length.EqualTo(2));
        Assert.That(snapshot.Records[0].Code, Is.EqualTo("second"));
        Assert.That(snapshot.Records[1].Code, Is.EqualTo("third"));
        Assert.That(snapshot.DroppedCount, Is.EqualTo(1));
    }

    [Test]
    public void CriticalFaultSurvivesRingEvictionAndFilteredSnapshots()
    {
        var service = new DiagnosticsService();

        for (var index = 0; index < 8192; index++)
        {
            service.Write(CreateDiagnostic($"record-{index}", DiagnosticSeverity.Information, index), null, null);
        }

        var fault = CreateDiagnostic("critical-fault", DiagnosticSeverity.Critical, 8192);
        service.Write(fault, null, null);
        var snapshot = service.GetSnapshot(new DiagnosticQuery(8192, DiagnosticSeverity.Information, null, null));

        Assert.That(snapshot.Records, Has.Length.EqualTo(8192));
        Assert.That(snapshot.Records[0].MonotonicTimestamp, Is.EqualTo(1));
        Assert.That(snapshot.Records[^1], Is.SameAs(fault));
        Assert.That(snapshot.DroppedCount, Is.EqualTo(1));
        Assert.That(snapshot.CoalescedCount, Is.Zero);
        Assert.That(service.TryGetLatestFault(out var latestFault), Is.True);
        Assert.That(ReferenceEquals(latestFault, fault), Is.True);

        var newest = service.GetSnapshot(new DiagnosticQuery(2, DiagnosticSeverity.Information, null, null));
        Assert.That(newest.Records[0].Code, Is.EqualTo("record-8191"));
        Assert.That(newest.Records[1].Code, Is.EqualTo("critical-fault"));
    }

    [Test]
    public void DiagnosticClockUsesNonnegativeMonotonicHundredNanosecondTicks()
    {
        DiagnosticClockDomain.EnsureInitialized();
        var first = DiagnosticClockDomain.GetElapsedTimestamp();
        var second = DiagnosticClockDomain.GetElapsedTimestamp();

        Assert.That(DiagnosticClockDomain.TimestampFrequency, Is.EqualTo(10_000_000));
        Assert.That(first, Is.GreaterThanOrEqualTo(0));
        Assert.That(second, Is.GreaterThanOrEqualTo(first));
    }

    [Test]
    public void EvictingCoalescedWarningCountsEveryRepresentedOccurrence()
    {
        var service = new DiagnosticsService(2);
        var sessionId = new Guid("00112233-4455-4677-8899-aabbccddeeff");
        const uint worldOwnerId = 17;

        service.Write(CreateDiagnostic("warning-a", DiagnosticSeverity.Warning, 1), sessionId, worldOwnerId);
        service.Write(CreateDiagnostic("warning-a", DiagnosticSeverity.Warning, 2), sessionId, worldOwnerId);
        service.Write(CreateDiagnostic("warning-b", DiagnosticSeverity.Warning, 3), sessionId, worldOwnerId);
        service.Write(CreateDiagnostic("warning-c", DiagnosticSeverity.Warning, 4), sessionId, worldOwnerId);

        var snapshot = service.GetSnapshot(new DiagnosticQuery(2, DiagnosticSeverity.Trace, null, null));
        Assert.That(snapshot.Records, Has.Length.EqualTo(2));
        Assert.That(snapshot.Records[0].Code, Is.EqualTo("warning-b"));
        Assert.That(snapshot.Records[1].Code, Is.EqualTo("warning-c"));
        Assert.That(snapshot.DroppedCount, Is.EqualTo(2));
        Assert.That(snapshot.CoalescedCount, Is.EqualTo(1));
    }

    [Test]
    public void EvictingCoalescedWarningSaturatesDroppedCount()
    {
        var service = new DiagnosticsService(2);
        var droppedCountField = typeof(DiagnosticsService).GetField(
            "_droppedCount",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The diagnostic drop counter field was not found.");
        droppedCountField.SetValue(service, long.MaxValue - 1);
        var sessionId = new Guid("00112233-4455-4677-8899-aabbccddeeff");
        const uint worldOwnerId = 17;

        service.Write(CreateDiagnostic("warning-a", DiagnosticSeverity.Warning, 1), sessionId, worldOwnerId);
        service.Write(CreateDiagnostic("warning-a", DiagnosticSeverity.Warning, 2), sessionId, worldOwnerId);
        service.Write(CreateDiagnostic("warning-b", DiagnosticSeverity.Warning, 3), sessionId, worldOwnerId);
        service.Write(CreateDiagnostic("warning-c", DiagnosticSeverity.Warning, 4), sessionId, worldOwnerId);

        var snapshot = service.GetSnapshot(new DiagnosticQuery(2, DiagnosticSeverity.Trace, null, null));
        Assert.That(snapshot.DroppedCount, Is.EqualTo(long.MaxValue));
        Assert.That(snapshot.CoalescedCount, Is.EqualTo(1));
    }

    [Test]
    public void MeasuresBoundedWarningRecordAllocationTraffic()
    {
        var service = new DiagnosticsService(4);
        var sessionId = new Guid("00112233-4455-4677-8899-aabbccddeeff");
        const uint worldOwnerId = 17;
        service.Write(CreateDiagnostic("warm-warning", DiagnosticSeverity.Warning, 0), sessionId, worldOwnerId);

        var snapshotId = new Guid("10112233-4455-4677-8899-aabbccddeeff");
        var drawId = new Guid("20112233-4455-4677-8899-aabbccddeeff");
        const long submissionSequence = long.MaxValue;
        const long completedFence = long.MaxValue;
        const int testedVertexCount = 262144;
        const int degenerateVertexCount = 262144;
        var producerAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        var snapshotIdText = snapshotId.ToString("N");
        var drawIdText = drawId.ToString("N");
        var submissionSequenceText = submissionSequence.ToString(CultureInfo.InvariantCulture);
        var completedFenceText = completedFence.ToString(CultureInfo.InvariantCulture);
        var testedVertexCountText = testedVertexCount.ToString(CultureInfo.InvariantCulture);
        var degenerateVertexCountText = degenerateVertexCount.ToString(CultureInfo.InvariantCulture);
        var commandIdentity = string.Concat(
            "submission/",
            submissionSequenceText,
            "/snapshot/",
            snapshotIdText,
            "/draw/",
            drawIdText);
        ImmutableArray<DiagnosticContextEntry> context = [
            new DiagnosticContextEntry("SnapshotId", snapshotIdText),
            new DiagnosticContextEntry("DrawId", drawIdText),
            new DiagnosticContextEntry("SubmissionSequence", submissionSequenceText),
            new DiagnosticContextEntry("CompletedFence", completedFenceText),
            new DiagnosticContextEntry("TestedVertexCount", testedVertexCountText),
            new DiagnosticContextEntry("DegenerateVertexCount", degenerateVertexCountText),
        ];
        var diagnostic = new Diagnostic(
            "DegenerateSkinnedNormal",
            DiagnosticSeverity.Warning,
            monotonicTimestamp: 1,
            hostId: 1,
            worldGuid: new Guid("30112233-4455-4677-8899-aabbccddeeff"),
            deviceId: 2,
            surfaceId: null,
            deviceEpoch: 1,
            surfaceEpoch: null,
            swapchainEpoch: null,
            tick: 0,
            systemId: null,
            passId: null,
            assetId: null,
            commandIdentity: commandIdentity,
            operation: "Renderer.SubmitAsync",
            nativeResult: null,
            message: "Skinned mesh vertices required the dominant-bone normal fallback.",
            context: context);
        var producerAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - producerAllocationStart;

        var serviceAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        service.Write(diagnostic, sessionId, worldOwnerId);
        var serviceAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - serviceAllocationStart;
        GC.KeepAlive(diagnostic);
        service.Close();

        Assert.That(commandIdentity, Has.Length.EqualTo(110));
        Assert.That(context, Has.Length.EqualTo(6));
        TestContext.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}/{RuntimeInformation.ProcessArchitecture}");
        TestContext.WriteLine($"Producer formatting and Diagnostic construction: {producerAllocatedBytes} bytes");
        TestContext.WriteLine($"DiagnosticsService warning admission: {serviceAllocatedBytes} bytes");
        TestContext.WriteLine($"Total measured allocation traffic: {checked(producerAllocatedBytes + serviceAllocatedBytes)} bytes");
    }

    [Test]
    public void RecordDroppedCountsTelemetryThatCouldNotBeMaterialized()
    {
        var service = new DiagnosticsService(1);

        service.RecordDropped();

        var snapshot = service.GetSnapshot(new DiagnosticQuery(1, DiagnosticSeverity.Trace, null, null));
        Assert.That(snapshot.Records, Is.Empty);
        Assert.That(snapshot.DroppedCount, Is.EqualTo(1));
        Assert.That(snapshot.CoalescedCount, Is.Zero);
    }

    [Test]
    public void WarningDiagnosticsCoalesceInPlaceAndKeepPriorSnapshotsImmutable()
    {
        var service = new DiagnosticsService();
        var sessionId = new Guid("00112233-4455-4677-8899-aabbccddeeff");
        var firstWorldGuid = new Guid("10112233-4455-4677-8899-aabbccddeeff");
        var secondWorldGuid = new Guid("20112233-4455-4677-8899-aabbccddeeff");
        ImmutableArray<DiagnosticContextEntry> firstContext = [
            new DiagnosticContextEntry("ActiveSubscriptions", "3"),
            new DiagnosticContextEntry("MaximumSubscriptions", "4"),
        ];
        var first = CreateDiagnostic("WorldSubscriptionCapacityExceeded", DiagnosticSeverity.Warning, 10, firstWorldGuid, firstContext);
        service.Write(first, sessionId, 17);
        var priorSnapshot = service.GetSnapshot(new DiagnosticQuery(10, DiagnosticSeverity.Trace, null, null));

        ImmutableArray<DiagnosticContextEntry> secondContext = [
            new DiagnosticContextEntry("ActiveSubscriptions", "4"),
            new DiagnosticContextEntry("MaximumSubscriptions", "4"),
        ];
        service.Write(CreateDiagnostic("WorldSubscriptionCapacityExceeded", DiagnosticSeverity.Warning, 11, secondWorldGuid, secondContext), sessionId, 17);

        var coalescedSnapshot = service.GetSnapshot(new DiagnosticQuery(10, DiagnosticSeverity.Trace, null, null));
        Assert.That(coalescedSnapshot.Records, Has.Length.EqualTo(1));
        Assert.That(coalescedSnapshot.Records[0].WorldGuid, Is.EqualTo(firstWorldGuid));
        Assert.That(coalescedSnapshot.Records[0].MonotonicTimestamp, Is.EqualTo(10));
        Assert.That(coalescedSnapshot.Records[0].Context, Has.Length.EqualTo(3));
        Assert.That(coalescedSnapshot.Records[0].Context[0], Is.EqualTo(firstContext[0]));
        Assert.That(coalescedSnapshot.Records[0].Context[2], Is.EqualTo(new DiagnosticContextEntry("OccurrenceCount", "2")));
        Assert.That(priorSnapshot.Records[0].Context, Has.Length.EqualTo(3));
        Assert.That(priorSnapshot.Records[0].Context[2], Is.EqualTo(new DiagnosticContextEntry("OccurrenceCount", "1")));
        Assert.That(coalescedSnapshot.CoalescedCount, Is.EqualTo(1));
        Assert.That(service.TryGetLatestFault(out var latestFault), Is.False);
        Assert.That(latestFault, Is.Null);

        _ = Assert.Throws<ArgumentException>(() => service.Write(
            CreateDiagnostic(
                "reserved-context",
                DiagnosticSeverity.Warning,
                12,
                firstWorldGuid,
                [new DiagnosticContextEntry("OccurrenceCount", "1")]),
            sessionId,
            17));
    }
}
