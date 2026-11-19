// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

internal sealed class WorldDiagnosticsFundingTests
{
    private static Diagnostic CreateDiagnostic(
        string code,
        DiagnosticSeverity severity,
        long timestamp,
        Guid? worldGuid = null)
        => new(
            code,
            severity,
            timestamp,
            hostId: null,
            worldGuid,
            deviceId: null,
            surfaceId: null,
            deviceEpoch: null,
            surfaceEpoch: null,
            swapchainEpoch: null,
            tick: timestamp,
            systemId: null,
            passId: null,
            assetId: null,
            commandIdentity: null,
            operation: "TestOperation",
            nativeResult: null,
            message: "Test diagnostic.",
            context: []);

    private static Diagnostic CreateSkinnedNormalDiagnostic(Guid worldGuid, long submissionSequence)
    {
        var snapshotId = new Guid("11112222-3333-4444-8555-66778899aabb");
        var drawId = new Guid("00112233-4455-4677-8899-aabbccddeeff");
        var snapshotIdText = snapshotId.ToString("N");
        var drawIdText = drawId.ToString("N");
        var submissionSequenceText = submissionSequence.ToString(CultureInfo.InvariantCulture);
        var commandIdentity = string.Concat(
            "submission/",
            submissionSequenceText,
            "/snapshot/",
            snapshotIdText,
            "/draw/",
            drawIdText);

        return new Diagnostic(
            "DegenerateSkinnedNormal",
            DiagnosticSeverity.Warning,
            submissionSequence,
            hostId: 1,
            worldGuid,
            deviceId: 2,
            surfaceId: null,
            deviceEpoch: 1,
            surfaceEpoch: null,
            swapchainEpoch: null,
            tick: submissionSequence,
            systemId: null,
            passId: null,
            assetId: null,
            commandIdentity,
            operation: "Renderer.SubmitAsync",
            nativeResult: null,
            message: "Skinned mesh vertices required the dominant-bone normal fallback.",
            context: [
                new DiagnosticContextEntry("SnapshotId", snapshotIdText),
                new DiagnosticContextEntry("DrawId", drawIdText),
                new DiagnosticContextEntry("SubmissionSequence", submissionSequenceText),
                new DiagnosticContextEntry("CompletedFence", submissionSequenceText),
                new DiagnosticContextEntry("TestedVertexCount", "3"),
                new DiagnosticContextEntry("DegenerateVertexCount", "1"),
            ]);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Each measured WorldDiagnosticFundingControl owns and returns the reserved credit.")]
    private static (long First, long Steady) MeasureFundingControlAllocation()
    {
        const int InstanceCount = 64;
        const long EnvelopeBytes = 4096;
        var domain = new WorldAdmissionDomain(1_048_576);
        var account = new WorldDiagnosticAttributionAccount(Guid.NewGuid(), Guid.NewGuid(), EnvelopeBytes);
        account.BindOwner(1);
        var warmControls = new WorldDiagnosticFundingControl[InstanceCount];

        for (var index = 0; index < warmControls.Length; index++)
        {
            if (!domain.TryReserveRetainedDiagnosticSourceCredit(EnvelopeBytes, out var credit)
                || (credit is null))
            {
                throw new InvalidOperationException("The funding-control allocation warmup could not reserve its source envelope.");
            }

            warmControls[index] = new WorldDiagnosticFundingControl(account, credit);
            warmControls[index].ReleaseUse();
        }

        GC.KeepAlive(warmControls);
        var controls = new WorldDiagnosticFundingControl[InstanceCount * 2];
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < InstanceCount; index++)
        {
            if (!domain.TryReserveRetainedDiagnosticSourceCredit(EnvelopeBytes, out var credit)
                || (credit is null))
            {
                throw new InvalidOperationException("The funding-control allocation measurement could not reserve its source envelope.");
            }

            controls[index] = new WorldDiagnosticFundingControl(account, credit);
        }

        var firstAllocationBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        var steadyAllocationStart = GC.GetAllocatedBytesForCurrentThread();

        for (var index = InstanceCount; index < controls.Length; index++)
        {
            if (!domain.TryReserveRetainedDiagnosticSourceCredit(EnvelopeBytes, out var credit)
                || (credit is null))
            {
                throw new InvalidOperationException("The funding-control allocation measurement could not reserve its source envelope.");
            }

            controls[index] = new WorldDiagnosticFundingControl(account, credit);
        }

        var steadyAllocationBytes = GC.GetAllocatedBytesForCurrentThread() - steadyAllocationStart;

        foreach (var control in controls)
        {
            control.ReleaseUse();
        }

        GC.KeepAlive(controls);
        domain.Dispose();
        return (firstAllocationBytes / InstanceCount, steadyAllocationBytes / InstanceCount);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (long First, long Steady) MeasureServiceOwnerAllocation(
        WorldDiagnosticsServiceOwner.ServiceConfiguration configuration)
    {
        const int InstanceCount = 64;
        const int MaximumPendingReservations = 1;
        var service = new DiagnosticsService(
            configuration.MaximumRecords,
            configuration.DiagnosticsMaximumBytes);
        var warmOwners = new WorldDiagnosticsServiceOwner[InstanceCount];

        for (var index = 0; index < warmOwners.Length; index++)
        {
            warmOwners[index] = new WorldDiagnosticsServiceOwner(
                service,
                configuration.MaximumRecords,
                MaximumPendingReservations,
                ownsService: false);
        }

        GC.KeepAlive(warmOwners);
        var owners = new WorldDiagnosticsServiceOwner[InstanceCount * 2];
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < InstanceCount; index++)
        {
            owners[index] = new WorldDiagnosticsServiceOwner(
                service,
                configuration.MaximumRecords,
                MaximumPendingReservations,
                ownsService: false);
        }

        var firstAllocationBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        var steadyAllocationStart = GC.GetAllocatedBytesForCurrentThread();

        for (var index = InstanceCount; index < owners.Length; index++)
        {
            owners[index] = new WorldDiagnosticsServiceOwner(
                service,
                configuration.MaximumRecords,
                MaximumPendingReservations,
                ownsService: false);
        }

        var steadyAllocationBytes = GC.GetAllocatedBytesForCurrentThread() - steadyAllocationStart;
        GC.KeepAlive(owners);
        service.Close();
        return (firstAllocationBytes / InstanceCount, steadyAllocationBytes / InstanceCount);
    }

    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The WorldDiagnosticFundingControl owns and returns the reserved credit after its final source use.")]
    public void CrossServiceEvictionReleasesOnlyTheLocalSourceAttachment()
    {
        const long EnvelopeBytes = 32 * 1024;
        var domain = new WorldAdmissionDomain(1_048_576);
        var serviceA = new DiagnosticsService(1);
        var ownerA = new WorldDiagnosticsServiceOwner(serviceA, maximumRecords: 1, maximumPendingAttributionReservations: 0, ownsService: true);
        var serviceB = new DiagnosticsService(1);
        var ownerB = new WorldDiagnosticsServiceOwner(serviceB, maximumRecords: 1, maximumPendingAttributionReservations: 1, ownsService: false);
        var worldGuid = new Guid("30112233-4455-4677-8899-aabbccddeeff");
        var executionSessionId = new Guid("00112233-4455-4677-8899-aabbccddeeff");
        var capability = serviceA.CreateBorrowedWorldCapability(
            worldGuid,
            executionSessionId,
            EnvelopeBytes - WorldDiagnosticFundingControl.OwnedByteReservation);
        capability.BindOwner(17);

        if (!domain.TryReserveRetainedDiagnosticSourceCredit(EnvelopeBytes, out var credit)
            || (credit is null))
        {
            throw new InvalidOperationException("The test could not reserve the diagnostic source envelope.");
        }

        var fundingControl = new WorldDiagnosticFundingControl(capability.AttributionAccount, credit);
        var ownerAUseAcquired = false;
        var sourceUseReleased = false;
        var ownerBClosed = false;

        try
        {
            ownerA.RetainWorldUse();
            ownerAUseAcquired = true;
            ownerA.SetBaselineFundingControl(fundingControl);
            ownerA.WriteAttributed(
                capability,
                fundingControl,
                CreateDiagnostic("source-a", DiagnosticSeverity.Information, 1, worldGuid));
            Assert.That(capability.AttributionAccount.ReservedBytes, Is.GreaterThan(0));

            Span<int> contextStringLengths = [
                "SnapshotId".Length, 32,
                "DrawId".Length, 32,
                "SubmissionSequence".Length, 19,
                "CompletedFence".Length, 19,
                "TestedVertexCount".Length, 6,
                "DegenerateVertexCount".Length, 6,
            ];
            var admissionBytes = DiagnosticsService.GetNoncoalescingWarningAdmissionBytes(
                "DegenerateSkinnedNormal".Length,
                "Renderer.SubmitAsync".Length,
                "Skinned mesh vertices required the dominant-bone normal fallback.".Length,
                commandIdentityLength: 110,
                contextStringLengths);

            Assert.That(ownerB.TryReserveAttributionPayload(fundingControl, admissionBytes, out var reservation), Is.True);

            try
            {
                _ = ownerB.WriteNoncoalescingWarning(
                    fundingControl,
                    CreateSkinnedNormalDiagnostic(worldGuid, 1),
                    ref reservation);
            }
            finally
            {
                reservation.Dispose();
                ownerB.CompleteAttributionReservation(fundingControl);
            }

            ownerB.WriteUnattributed(
                CreateDiagnostic("evict-source-b", DiagnosticSeverity.Information, 2),
                executionSessionId: null,
                worldOwnerId: null);
            Assert.That(serviceB.RetainsAttributionAccount(capability.AttributionAccount), Is.False);
            Assert.That(capability.AttributionAccount.ReservedBytes, Is.GreaterThan(0));

            ownerA.CloseCapability(capability);
            ownerA.ReleaseWorldUse();
            ownerAUseAcquired = false;
            fundingControl.ReleaseUse();
            sourceUseReleased = true;

            _ = Assert.Throws<ObjectDisposedException>(() => _ = fundingControl.EnvelopeBytes);
            ownerB.CloseService();
            ownerBClosed = true;
        }
        finally
        {
            try
            {
                ownerA.CloseCapability(capability);
            }
            finally
            {
                try
                {
                    if (ownerAUseAcquired)
                    {
                        ownerA.ReleaseWorldUse();
                    }
                }
                finally
                {
                    try
                    {
                        if (!ownerBClosed)
                        {
                            ownerB.CloseService();
                        }
                    }
                    finally
                    {
                        try
                        {
                            if (!sourceUseReleased)
                            {
                                fundingControl.ReleaseUse();
                            }
                        }
                        finally
                        {
                            domain.Dispose();
                        }
                    }
                }
            }
        }
    }

    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The WorldDiagnosticFundingControl takes ownership of the operation credit.")]
    public void RetainedFundingUseOutlivesItsSourceUse()
    {
        const long EnvelopeBytes = 4096;
        using var domain = new WorldAdmissionDomain(1_048_576);
        var account = new WorldDiagnosticAttributionAccount(Guid.NewGuid(), Guid.NewGuid(), EnvelopeBytes);
        account.BindOwner(17);

        if (!domain.TryReserveRetainedDiagnosticSourceCredit(EnvelopeBytes, out var credit)
            || (credit is null))
        {
            throw new InvalidOperationException("The test could not reserve the diagnostic source envelope.");
        }

        var fundingControl = new WorldDiagnosticFundingControl(account, credit);
        var retainedUse = new WorldDiagnosticFundingUse(fundingControl);
        var sourceUseReleased = false;

        try
        {
            fundingControl.ReleaseUse();
            sourceUseReleased = true;
            Assert.That(fundingControl.EnvelopeBytes, Is.EqualTo(EnvelopeBytes));

            retainedUse.Dispose();
            Assert.That(() => fundingControl.EnvelopeBytes, Throws.TypeOf<ObjectDisposedException>());
        }
        finally
        {
            retainedUse.Dispose();

            if (!sourceUseReleased)
            {
                fundingControl.ReleaseUse();
            }
        }
    }

    [Test]
    public void RuntimeControlAllocationsFitTheirPrepaidCharges()
    {
        var (fundingControlFirst, fundingControlSteady) = MeasureFundingControlAllocation();
        var serviceOwnerConfiguration = WorldDiagnosticsServiceOwner.GetConfiguration(
            DiagnosticsService.MaximumServiceByteCapacity,
            maximumPendingAttributionReservations: 1);
        var (serviceOwnerFirst, serviceOwnerSteady) = MeasureServiceOwnerAllocation(serviceOwnerConfiguration);
        var serviceOwnerBytes = serviceOwnerConfiguration.OwnerByteReservation;

        TestContext.WriteLine($"WorldDiagnosticFundingControl first/steady: {fundingControlFirst}/{fundingControlSteady} bytes");
        TestContext.WriteLine($"WorldDiagnosticsServiceOwner first/steady: {serviceOwnerFirst}/{serviceOwnerSteady} bytes");
        TestContext.WriteLine($"WorldDiagnosticsServiceOwner charge: {serviceOwnerBytes} bytes");

        Assert.That(fundingControlFirst, Is.LessThanOrEqualTo(WorldDiagnosticFundingControl.OwnedByteReservation));
        Assert.That(fundingControlSteady, Is.LessThanOrEqualTo(WorldDiagnosticFundingControl.OwnedByteReservation));
        Assert.That(serviceOwnerFirst, Is.LessThanOrEqualTo(serviceOwnerBytes));
        Assert.That(serviceOwnerSteady, Is.LessThanOrEqualTo(serviceOwnerBytes));
    }

    [Test]
    public void ServiceConfigurationFundsBoundedAttachmentArrayWithinEightMiB()
    {
        var configuration = WorldDiagnosticsServiceOwner.GetConfiguration(
            DiagnosticsService.MaximumServiceByteCapacity,
            maximumPendingAttributionReservations: 1);
        var service = new DiagnosticsService(
            configuration.MaximumRecords,
            configuration.DiagnosticsMaximumBytes);
        var serviceOwner = new WorldDiagnosticsServiceOwner(
            service,
            configuration.MaximumRecords,
            maximumPendingAttributionReservations: 1,
            ownsService: false);

        Assert.That(serviceOwner.MaximumRecords, Is.EqualTo(configuration.MaximumRecords));
        Assert.That(serviceOwner.OwnedByteReservation, Is.EqualTo(configuration.OwnerByteReservation));
        Assert.That(service.MaximumByteReservation, Is.EqualTo(configuration.DiagnosticsMaximumBytes));
        Assert.That(
            service.MaximumByteReservation + serviceOwner.OwnedByteReservation,
            Is.EqualTo(DiagnosticsService.MaximumServiceByteCapacity));
        Assert.That(
            service.OwnedByteReservation + serviceOwner.OwnedByteReservation,
            Is.LessThanOrEqualTo(DiagnosticsService.MaximumServiceByteCapacity));

        TestContext.WriteLine(
            $"Record capacity: {configuration.MaximumRecords}; attachment cells: {configuration.MaximumRecords + 3}; owner charge: {serviceOwner.OwnedByteReservation} bytes");
        serviceOwner.CloseService();
    }
}
