// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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
        ImmutableArray<DiagnosticContextEntry> context = default,
        string operation = "TestOperation",
        string message = "Test diagnostic.")
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
            operation,
            null,
            message,
            context.IsDefault ? [] : context);

    private static Diagnostic CreateSkinnedNormalDiagnostic(
        Guid worldGuid,
        long submissionSequence,
        Guid drawId,
        int degenerateVertexCount = 1,
        int testedVertexCount = 3,
        Guid? commandDrawId = null)
    {
        var snapshotId = new Guid("11112222-3333-4444-8555-66778899aabb");
        var snapshotIdText = snapshotId.ToString("N");
        var drawIdText = drawId.ToString("N");
        var commandDrawIdText = (commandDrawId ?? drawId).ToString("N");
        var submissionSequenceText = submissionSequence.ToString(CultureInfo.InvariantCulture);
        var commandIdentity = string.Concat(
            "submission/",
            submissionSequenceText,
            "/snapshot/",
            snapshotIdText,
            "/draw/",
            commandDrawIdText);

        return new Diagnostic(
            "DegenerateSkinnedNormal",
            DiagnosticSeverity.Warning,
            submissionSequence,
            1,
            worldGuid,
            2,
            null,
            1,
            null,
            null,
            submissionSequence,
            null,
            null,
            null,
            commandIdentity,
            "Renderer.SubmitAsync",
            null,
            "Skinned mesh vertices required the dominant-bone normal fallback.",
            [
                new DiagnosticContextEntry("SnapshotId", snapshotIdText),
                new DiagnosticContextEntry("DrawId", drawIdText),
                new DiagnosticContextEntry("SubmissionSequence", submissionSequenceText),
                new DiagnosticContextEntry("CompletedFence", submissionSequenceText),
                new DiagnosticContextEntry("TestedVertexCount", testedVertexCount.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticContextEntry("DegenerateVertexCount", degenerateVertexCount.ToString(CultureInfo.InvariantCulture)),
            ]);
    }

    [Test]
    public void AbandonedWarningPreclaimRefundsServiceAndWorldReservations()
    {
        var minimumServiceBytes = DiagnosticsService.GetMinimumServiceByteReservation();
        var service = new DiagnosticsService(1, minimumServiceBytes);
        var worldGuid = new Guid("30112233-4455-4677-8899-aabbccddeeff");
        var executionSessionId = new Guid("00112233-4455-4677-8899-aabbccddeeff");
        var capability = service.CreateBorrowedWorldCapability(worldGuid, executionSessionId, maximumDiagnosticBytes: 4096);
        capability.BindOwner(17);
        var account = capability.AttributionAccount;
        var preclaimBytes = DiagnosticsService.GetNoncoalescingWarningAdmissionBytes(
            codeLength: 1,
            operationLength: 1,
            messageLength: 1,
            commandIdentityLength: 0,
            contextStringLengths: []);

        Assert.That(service.TryReserveAttributionPayload(account, preclaimBytes, out var reservation), Is.True);
        Assert.That(account.ReservedBytes, Is.EqualTo(preclaimBytes));
        reservation.Dispose();
        Assert.That(account.ReservedBytes, Is.Zero);

        service.Write(
            CreateDiagnostic("x", DiagnosticSeverity.Warning, 1, worldGuid, operation: "x", message: "x"),
            executionSessionId,
            17,
            account);
        Assert.That(
            service.GetSnapshot(new DiagnosticQuery(1, DiagnosticSeverity.Warning, null, null)).Records,
            Has.Length.EqualTo(1));

        capability.Close();
        service.Close();
    }

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
    public void DiagnosticByteBudgetAdmitsExactlyTheMinimumRingAndWarningWorkspace()
    {
        var minimumBytes = DiagnosticsService.GetMinimumServiceByteReservation();

        Assert.That(DiagnosticsService.GetMaximumRecordCount(minimumBytes), Is.EqualTo(1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => DiagnosticsService.GetMaximumRecordCount(minimumBytes - 1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new DiagnosticsService(1, minimumBytes - 1));

        var service = new DiagnosticsService(1, minimumBytes);
        service.Write(
            CreateDiagnostic("x", DiagnosticSeverity.Warning, 1, operation: "x", message: "x"),
            null,
            null);
        Assert.That(
            service.GetSnapshot(new DiagnosticQuery(1, DiagnosticSeverity.Warning, null, null)).Records,
            Has.Length.EqualTo(1));
        service.Close();
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
        const int diagnosticCapacity = 8192;
        var service = new DiagnosticsService(4);
        var ownedByteReservation = DiagnosticsService.GetOwnedByteReservation(diagnosticCapacity);
        var diagnosticEntryStride = checked((ownedByteReservation - 256 - 64) / diagnosticCapacity);
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
        TestContext.WriteLine(
            $"DiagnosticsService full-ring reservation: capacity={diagnosticCapacity}, bytes={ownedByteReservation}, entry-stride={diagnosticEntryStride}");
        TestContext.WriteLine($"Producer formatting and Diagnostic construction: {producerAllocatedBytes} bytes");
        TestContext.WriteLine($"DiagnosticsService warning admission: {serviceAllocatedBytes} bytes");
        TestContext.WriteLine($"Total measured allocation traffic: {checked(producerAllocatedBytes + serviceAllocatedBytes)} bytes");
    }

    [Test]
    public void MeasuresDiagnosticRootAndContextCopyAllocationTraffic()
    {
        const int iterationCount = 1024;
        const string code = "MeasuredDiagnostic";
        const string operation = "DiagnosticsService.Measure";
        const string message = "A preconstructed diagnostic message.";
        ImmutableArray<DiagnosticContextEntry> emptyContext = [];
        ImmutableArray<DiagnosticContextEntry> context = [
            new DiagnosticContextEntry("Context0", "Value0"),
            new DiagnosticContextEntry("Context1", "Value1"),
            new DiagnosticContextEntry("Context2", "Value2"),
            new DiagnosticContextEntry("Context3", "Value3"),
            new DiagnosticContextEntry("Context4", "Value4"),
            new DiagnosticContextEntry("Context5", "Value5"),
            new DiagnosticContextEntry("Context6", "Value6"),
        ];

        static Diagnostic CreateDiagnostic(
            string code,
            string operation,
            string message,
            ImmutableArray<DiagnosticContextEntry> context,
            DiagnosticSeverity severity = DiagnosticSeverity.Warning)
        {
            return new Diagnostic(
                code,
                severity,
                monotonicTimestamp: 1,
                hostId: null,
                worldGuid: null,
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
                operation,
                nativeResult: null,
                message,
                context);
        }

        static long MeasureStringAllocation(int length)
        {
            var measuredString = new string('x', length);
            GC.KeepAlive(measuredString);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var start = GC.GetAllocatedBytesForCurrentThread();
            const int count = 1024;

            for (var index = 0; index < count; index++)
            {
                measuredString = new string('x', length);
            }

            var bytes = checked(GC.GetAllocatedBytesForCurrentThread() - start);
            GC.KeepAlive(measuredString);
            return bytes / count;
        }

        static long MeasureServiceAllocation(int maximumRecords)
        {
            var warmService = new DiagnosticsService(maximumRecords);
            GC.KeepAlive(warmService);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var start = GC.GetAllocatedBytesForCurrentThread();
            var measuredService = new DiagnosticsService(maximumRecords);
            var bytes = checked(GC.GetAllocatedBytesForCurrentThread() - start);
            GC.KeepAlive(measuredService);
            return bytes;
        }

        static long MeasureAttributionAccountAllocation()
        {
            var worldGuid = new Guid("30112233-4455-4677-8899-aabbccddeeff");
            var executionSessionId = new Guid("00112233-4455-4677-8899-aabbccddeeff");
            var warmAccount = new WorldDiagnosticAttributionAccount(worldGuid, executionSessionId, 4096);
            GC.KeepAlive(warmAccount);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var start = GC.GetAllocatedBytesForCurrentThread();
            var measuredAccount = new WorldDiagnosticAttributionAccount(worldGuid, executionSessionId, 4096);
            var bytes = checked(GC.GetAllocatedBytesForCurrentThread() - start);
            GC.KeepAlive(measuredAccount);
            return bytes;
        }

        static long MeasureRetentionAllocation(
            string operation,
            string message,
            ImmutableArray<DiagnosticContextEntry> context)
        {
            var service = new DiagnosticsService(4);
            var first = CreateDiagnostic("retention-warm", operation, message, context);
            const int iterationCount = 8;
            var diagnostics = new Diagnostic[iterationCount];

            for (var index = 0; index < diagnostics.Length; index++)
            {
                diagnostics[index] = CreateDiagnostic(
                    $"retention-{index}",
                    operation,
                    message,
                    context,
                    DiagnosticSeverity.Information);
            }

            service.Write(first, null, null);
            GC.KeepAlive(first);
            GC.KeepAlive(diagnostics);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var start = GC.GetAllocatedBytesForCurrentThread();

            for (var index = 0; index < diagnostics.Length; index++)
            {
                service.Write(diagnostics[index], null, null);
            }

            var bytes = checked((GC.GetAllocatedBytesForCurrentThread() - start) / iterationCount);
            GC.KeepAlive(service);
            GC.KeepAlive(diagnostics);
            service.Close();
            return bytes;
        }

        _ = CreateDiagnostic(code, operation, message, emptyContext);
        _ = CreateDiagnostic(code, operation, message, context);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var noContextStart = GC.GetAllocatedBytesForCurrentThread();
        Diagnostic? lastDiagnostic = null;

        for (var index = 0; index < iterationCount; index++)
        {
            lastDiagnostic = CreateDiagnostic(code, operation, message, emptyContext);
        }

        var noContextBytes = checked(GC.GetAllocatedBytesForCurrentThread() - noContextStart);
        GC.KeepAlive(lastDiagnostic);

        var contextStart = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < iterationCount; index++)
        {
            lastDiagnostic = CreateDiagnostic(code, operation, message, context);
        }

        var contextBytes = checked(GC.GetAllocatedBytesForCurrentThread() - contextStart);
        GC.KeepAlive(lastDiagnostic);

        const int diagnosticCapacity = 8192;
        var singleRecordServiceBytes = MeasureServiceAllocation(1);
        var fullRingServiceBytes = MeasureServiceAllocation(diagnosticCapacity);
        var attributionAccountBytes = MeasureAttributionAccountAllocation();
        var retentionBytes = MeasureRetentionAllocation(operation, message, emptyContext);
        var ringAllocationDelta = checked(fullRingServiceBytes - singleRecordServiceBytes);
        var measuredEntryStride = ringAllocationDelta / (diagnosticCapacity - 1);
        var ringStrideRemainder = ringAllocationDelta % (diagnosticCapacity - 1);
        int[] stringLengths = [6, 10, 14, 15, 17, 18, 19, 20, 21, 23, 32, 65, 110];

        TestContext.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}/{RuntimeInformation.ProcessArchitecture}");
        TestContext.WriteLine($"Diagnostic with empty context: {noContextBytes / iterationCount} bytes per record");
        TestContext.WriteLine($"Diagnostic with seven-entry context: {contextBytes / iterationCount} bytes per record");
        TestContext.WriteLine($"Seven-entry context copy delta: {(contextBytes - noContextBytes) / iterationCount} bytes per record");
        TestContext.WriteLine($"DiagnosticContextEntry stride: {Unsafe.SizeOf<DiagnosticContextEntry>()} bytes");
        TestContext.WriteLine($"WorldDiagnosticAttributionAccount allocation: {attributionAccountBytes} bytes");
        TestContext.WriteLine($"DiagnosticsService preconstructed Information admission: {retentionBytes} bytes");
        TestContext.WriteLine(
            $"DiagnosticsService construction: capacity=1, {singleRecordServiceBytes} bytes; capacity={diagnosticCapacity}, {fullRingServiceBytes} bytes; ring allocation delta={ringAllocationDelta} bytes, inferred entry-stride={measuredEntryStride}, remainder={ringStrideRemainder}");

        foreach (var stringLength in stringLengths)
        {
            TestContext.WriteLine($"String object length {stringLength}: {MeasureStringAllocation(stringLength)} bytes");
        }
    }

    [Test]
    public void NoncoalescingWarningRejectsDiagnosticsOutsideSkinnedNormalFormat()
    {
        var service = new DiagnosticsService(4);
        var worldGuid = new Guid("30112233-4455-4677-8899-aabbccddeeff");
        var capability = service.CreateBorrowedWorldCapability(worldGuid, Guid.NewGuid(), maximumDiagnosticBytes: 4096);
        capability.BindOwner(17);
        var account = capability.AttributionAccount;
        Diagnostic[] invalidWarnings = [
            CreateDiagnostic("other-warning", DiagnosticSeverity.Warning, 1, worldGuid),
            CreateSkinnedNormalDiagnostic(
                worldGuid,
                1,
                new Guid("01000000-0000-0000-0000-000000000001"),
                degenerateVertexCount: 0),
            CreateSkinnedNormalDiagnostic(
                worldGuid,
                1,
                new Guid("02000000-0000-0000-0000-000000000002"),
                testedVertexCount: 262145),
            CreateSkinnedNormalDiagnostic(
                worldGuid,
                1,
                new Guid("03000000-0000-0000-0000-000000000003"),
                commandDrawId: new Guid("04000000-0000-0000-0000-000000000004")),
        ];

        foreach (var diagnostic in invalidWarnings)
        {
            Assert.That(service.TryReserveAttributionPayload(account, 1, out var reservation), Is.True);

            var rejected = false;

            try
            {
                _ = service.WriteNoncoalescingWarning(diagnostic, ref reservation);
            }
            catch (ArgumentException)
            {
                rejected = true;
            }
            finally
            {
                reservation.Dispose();
            }

            Assert.That(rejected, Is.True);
            Assert.That(account.ReservedBytes, Is.Zero);
        }

        var snapshot = service.GetSnapshot(new DiagnosticQuery(4, DiagnosticSeverity.Warning, null, null));
        Assert.That(snapshot.Records, Is.Empty);
        Assert.That(snapshot.DroppedCount, Is.Zero);

        capability.Close();
        service.Close();
    }

    [Test]
    public void NoncoalescingWarningsRetainDistinctRowsAndRefundUnusedPreclaims()
    {
        var service = new DiagnosticsService(4);
        var worldGuid = new Guid("30112233-4455-4677-8899-aabbccddeeff");
        var executionSessionId = new Guid("00112233-4455-4677-8899-aabbccddeeff");
        var capability = service.CreateBorrowedWorldCapability(worldGuid, executionSessionId, maximumDiagnosticBytes: 16384);
        capability.BindOwner(17);
        var account = capability.AttributionAccount;
        Span<int> contextStringLengths = [
            "SnapshotId".Length, 32,
            "DrawId".Length, 32,
            "SubmissionSequence".Length, 19,
            "CompletedFence".Length, 19,
            "TestedVertexCount".Length, 6,
            "DegenerateVertexCount".Length, 6,
        ];
        var admissionBytes = DiagnosticsService.GetNoncoalescingWarningAdmissionBytes(
            codeLength: "DegenerateSkinnedNormal".Length,
            operationLength: "Renderer.SubmitAsync".Length,
            messageLength: "Skinned mesh vertices required the dominant-bone normal fallback.".Length,
            commandIdentityLength: 110,
            contextStringLengths);

        for (var index = 0; index < 2; index++)
        {
            var preclaimBytes = admissionBytes + 1024;
            var reservedBytesBefore = account.ReservedBytes;
            Assert.That(service.TryReserveAttributionPayload(account, preclaimBytes, out var reservation), Is.True);
            var drawId = new Guid((index == 0) ? "01000000-0000-0000-0000-000000000001" : "02000000-0000-0000-0000-000000000002");
            Assert.That(
                service.WriteNoncoalescingWarning(
                    CreateSkinnedNormalDiagnostic(worldGuid, index + 1, drawId),
                    ref reservation),
                Is.True);
            Assert.That(account.ReservedBytes, Is.EqualTo(reservedBytesBefore + preclaimBytes));
            reservation.Dispose();
            Assert.That(account.ReservedBytes, Is.GreaterThan(reservedBytesBefore));
            Assert.That(account.ReservedBytes, Is.LessThan(reservedBytesBefore + preclaimBytes));
        }

        var snapshot = service.GetSnapshot(new DiagnosticQuery(4, DiagnosticSeverity.Warning, null, null));

        Assert.That(snapshot.Records, Has.Length.EqualTo(2));
        Assert.That(snapshot.Records[0].Context[^1], Is.EqualTo(new DiagnosticContextEntry("OccurrenceCount", "1")));
        Assert.That(snapshot.Records[1].Context[^1], Is.EqualTo(new DiagnosticContextEntry("OccurrenceCount", "1")));
        Assert.That(snapshot.CoalescedCount, Is.Zero);
        Assert.That(account.ReservedBytes, Is.LessThan(account.MaximumRetainedBytes));

        capability.Close();
        service.Close();
        Assert.That(account.ReservedBytes, Is.Zero);
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

    [Test]
    public void WarningReplacementRequiresTheOldAndNewPayloadPeak()
    {
        var service = new DiagnosticsService(4);
        var worldGuid = new Guid("30112233-4455-4677-8899-aabbccddeeff");
        var executionSessionId = new Guid("00112233-4455-4677-8899-aabbccddeeff");
        var capability = service.CreateBorrowedWorldCapability(worldGuid, executionSessionId, maximumDiagnosticBytes: 4096);
        capability.BindOwner(17);
        var account = capability.AttributionAccount;
        service.Write(
            CreateDiagnostic("same-warning", DiagnosticSeverity.Warning, 1, worldGuid),
            executionSessionId,
            17,
            account);

        var unallocatedBytes = account.MaximumRetainedBytes - account.ReservedBytes;
        Assert.That(unallocatedBytes, Is.GreaterThan(0));
        Assert.That(service.TryReserveAttributionPayload(account, unallocatedBytes, out var blocker), Is.True);

        service.Write(
            CreateDiagnostic("same-warning", DiagnosticSeverity.Warning, 2, worldGuid),
            executionSessionId,
            17,
            account);
        var rejectedSnapshot = service.GetSnapshot(new DiagnosticQuery(4, DiagnosticSeverity.Warning, null, null));
        Assert.That(rejectedSnapshot.Records, Has.Length.EqualTo(1));
        Assert.That(rejectedSnapshot.Records[0].MonotonicTimestamp, Is.EqualTo(1));
        Assert.That(rejectedSnapshot.Records[0].Context[^1], Is.EqualTo(new DiagnosticContextEntry("OccurrenceCount", "1")));
        Assert.That(rejectedSnapshot.DroppedCount, Is.EqualTo(1));
        Assert.That(rejectedSnapshot.CoalescedCount, Is.Zero);

        blocker.Dispose();
        service.Write(
            CreateDiagnostic("same-warning", DiagnosticSeverity.Warning, 3, worldGuid),
            executionSessionId,
            17,
            account);
        var admittedSnapshot = service.GetSnapshot(new DiagnosticQuery(4, DiagnosticSeverity.Warning, null, null));

        Assert.That(admittedSnapshot.Records, Has.Length.EqualTo(1));
        Assert.That(admittedSnapshot.Records[0].MonotonicTimestamp, Is.EqualTo(1));
        Assert.That(admittedSnapshot.Records[0].Context[^1], Is.EqualTo(new DiagnosticContextEntry("OccurrenceCount", "2")));
        Assert.That(admittedSnapshot.DroppedCount, Is.EqualTo(1));
        Assert.That(admittedSnapshot.CoalescedCount, Is.EqualTo(1));

        capability.Close();
        service.Close();
    }

    [Test]
    public void WorldAttributionIsSharedAcrossServicesAndReleasedAfterServiceClose()
    {
        var service = new DiagnosticsService(4);
        var otherService = new DiagnosticsService(4);
        var worldGuid = new Guid("30112233-4455-4677-8899-aabbccddeeff");
        var executionSessionId = new Guid("00112233-4455-4677-8899-aabbccddeeff");
        var capability = service.CreateBorrowedWorldCapability(worldGuid, executionSessionId, maximumDiagnosticBytes: 4096);
        capability.BindOwner(17);
        var account = capability.AttributionAccount;
        var diagnostic = CreateDiagnostic("attributed", DiagnosticSeverity.Information, 1, worldGuid);

        service.Write(diagnostic, executionSessionId, 17, account);

        var retainedBytes = account.ReservedBytes;
        Assert.That(retainedBytes, Is.GreaterThan(0));
        Assert.That(retainedBytes, Is.LessThanOrEqualTo(account.MaximumRetainedBytes));
        Assert.That(
            otherService.TryReserveAttributionPayload(account, account.MaximumRetainedBytes, out _),
            Is.False);
        Assert.That(account.ReservedBytes, Is.EqualTo(retainedBytes));
        Assert.That(
            otherService.GetSnapshot(new DiagnosticQuery(1, DiagnosticSeverity.Trace, null, null)).DroppedCount,
            Is.EqualTo(1));

        var snapshot = service.GetSnapshot(new DiagnosticQuery(1, DiagnosticSeverity.Trace, null, null));
        service.Close();

        Assert.That(account.ReservedBytes, Is.Zero);
        Assert.That(snapshot.Records[0], Is.SameAs(diagnostic));
        Assert.That(snapshot.Records[0].Code, Is.EqualTo("attributed"));

        Assert.That(
            otherService.TryReserveAttributionPayload(account, account.MaximumRetainedBytes, out var reservation),
            Is.True);
        reservation.Dispose();
        Assert.That(account.ReservedBytes, Is.Zero);

        capability.Close();
        otherService.Close();
    }

    [Test]
    public void WorldQuotaPressureEvictsOnlyThatWorldsServiceRows()
    {
        var service = new DiagnosticsService(16);
        var executionSessionId = new Guid("00112233-4455-4677-8899-aabbccddeeff");
        var firstWorldGuid = new Guid("30112233-4455-4677-8899-aabbccddeeff");
        var secondWorldGuid = new Guid("40112233-4455-4677-8899-aabbccddeeff");
        var firstWorld = service.CreateBorrowedWorldCapability(firstWorldGuid, executionSessionId, maximumDiagnosticBytes: 4096);
        var secondWorld = service.CreateBorrowedWorldCapability(secondWorldGuid, executionSessionId, maximumDiagnosticBytes: 4096);
        firstWorld.BindOwner(17);
        secondWorld.BindOwner(18);
        var firstAccount = firstWorld.AttributionAccount;
        var secondAccount = secondWorld.AttributionAccount;

        for (var index = 0; index < 12; index++)
        {
            service.Write(
                CreateDiagnostic($"world-a-{index}", DiagnosticSeverity.Information, index, firstWorldGuid),
                executionSessionId,
                17,
                firstAccount);
        }

        service.Write(
            CreateDiagnostic("world-b-retained", DiagnosticSeverity.Information, 20, secondWorldGuid),
            executionSessionId,
            18,
            secondAccount);
        var snapshot = service.GetSnapshot(new DiagnosticQuery(16, DiagnosticSeverity.Trace, null, null));

        Assert.That(snapshot.Records.Any(static record => record.Code == "world-a-0"), Is.False);
        Assert.That(snapshot.Records.Any(static record => record.Code == "world-a-11"), Is.True);
        Assert.That(snapshot.Records.Any(static record => record.Code == "world-b-retained"), Is.True);
        Assert.That(snapshot.DroppedCount, Is.GreaterThan(0));
        Assert.That(firstAccount.ReservedBytes, Is.LessThanOrEqualTo(firstAccount.MaximumRetainedBytes));
        Assert.That(secondAccount.ReservedBytes, Is.GreaterThan(0));

        firstWorld.Close();
        secondWorld.Close();
        service.Close();
        Assert.That(firstAccount.ReservedBytes, Is.Zero);
        Assert.That(secondAccount.ReservedBytes, Is.Zero);
    }
}
