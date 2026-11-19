// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

internal sealed class WorldLifetimeControlQuarantineTests
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CollectGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "A successful lifetime quarantine transfers the admission reservation to the pre-reserved process anchor; all failed paths release it here.")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<WorldLifetimeControl> CreateQuarantinedLifetimeControl(
        WorldGuidRegistry registry,
        WorldAdmissionDomain domain,
        Guid worldGuid)
    {
        var reservation = WorldSystemInstanceRegistry.ReserveQuarantineCapacity(0);
        WorldGuidGrant grant = default;
        WorldLineageGrant lineageGrant = default;
        WorldDomainReservation? domainReservation = null;
        WorldLifetimeControl? lifetimeControl = null;

        try
        {
            if (!registry.TryReserveFresh(worldGuid, out grant, out _))
            {
                throw new InvalidOperationException("The quarantine test could not reserve its WorldGuid.");
            }

            if (!domain.TryReserveWorld(4096, out domainReservation)
                || (domainReservation is null)
                || !domain.TryReserveFreshLineage(
                    domainReservation,
                    worldGuid,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    out lineageGrant,
                    out _,
                    issuanceNamespace: worldGuid))
            {
                throw new InvalidOperationException("The quarantine test could not reserve its World history.");
            }

            if (!registry.TryCommitFresh(grant)
                || !domain.TryCommitFreshLineage(lineageGrant))
            {
                throw new InvalidOperationException("The quarantine test could not publish its World identity.");
            }

            lifetimeControl = new WorldLifetimeControl(
                registry,
                domain,
                grant,
                defaultDomainOwner: null,
                domainReservation,
                lineageGrant,
                isReplacementCandidate: false);
            lifetimeControl.PublishFresh();

            if (!lifetimeControl.BeginRetirement() || !lifetimeControl.QuarantineAfterCleanupFailure())
            {
                throw new InvalidOperationException("The quarantine test could not revoke and quarantine its WorldGuid.");
            }

            WorldSystemInstanceRegistry.QuarantineLifetimeControl(lifetimeControl, reservation);
            return new WeakReference<WorldLifetimeControl>(lifetimeControl);
        }
        finally
        {
            if ((lifetimeControl is not null) && !lifetimeControl.IsQuarantined)
            {
                if (lineageGrant.IsValid)
                {
                    _ = domain.TryRollbackFreshLineage(lineageGrant)
                        || domain.TryReleaseFreshLineage(lineageGrant);
                }

                domainReservation?.Dispose();

                if (lifetimeControl.BeginRetirement())
                {
                    _ = lifetimeControl.CompleteRetirement();
                }
            }
            else if (lifetimeControl is null)
            {
                if (lineageGrant.IsValid)
                {
                    _ = domain.TryRollbackFreshLineage(lineageGrant)
                        || domain.TryReleaseFreshLineage(lineageGrant);
                }

                domainReservation?.Dispose();

                if (grant.IsValid)
                {
                    _ = registry.TryReleaseFresh(grant);
                }
            }

            reservation.Release();
            WorldSystemInstanceRegistry.ReleaseLifetimeControlCapacity(reservation);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (long First, long Steady) MeasureUninitializedClassAllocation(Type type)
    {
        const int InstanceCount = 128;
        var warmInstances = new object[InstanceCount];

        for (var index = 0; index < warmInstances.Length; index++)
        {
            warmInstances[index] = RuntimeHelpers.GetUninitializedObject(type);
        }

        GC.KeepAlive(warmInstances);
        CollectGarbage();
        var instances = new object[InstanceCount * 2];
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < InstanceCount; index++)
        {
            instances[index] = RuntimeHelpers.GetUninitializedObject(type);
        }

        var firstAllocationBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        var steadyAllocationStart = GC.GetAllocatedBytesForCurrentThread();

        for (var index = InstanceCount; index < instances.Length; index++)
        {
            instances[index] = RuntimeHelpers.GetUninitializedObject(type);
        }

        var steadyAllocationBytes = GC.GetAllocatedBytesForCurrentThread() - steadyAllocationStart;
        GC.KeepAlive(instances);
        return (firstAllocationBytes / InstanceCount, steadyAllocationBytes / InstanceCount);
    }

    [Test]
    public void CosmeticCreditCellBackingUsesCompiledStrideAndWorldAccounting()
    {
        const int Capacity = 128;
        const int ScalarBytes = (13 * sizeof(uint)) + sizeof(bool);
        var ownerType = typeof(WorldCosmeticCreditOwner);
        var backing = ownerType.GetField("_cells", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The compiled cosmetic credit backing is missing.");
        var cellType = ownerType.GetNestedType("Cell", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The compiled cosmetic credit cell is missing.");
        var stateType = ownerType.GetNestedType("CellState", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The compiled cosmetic credit cell state is missing.");
        var debtType = ownerType.GetNestedType("DebtKind", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The compiled cosmetic credit debt kind is missing.");
        var capacityField = ownerType.GetField(
            nameof(WorldCosmeticCreditOwner.MaximumCredits),
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The compiled cosmetic credit capacity is missing.");
        var observedCapacity = (int)(capacityField.GetRawConstantValue()
            ?? throw new InvalidOperationException("The compiled cosmetic credit capacity has no constant value."));
        (string Name, Type Type)[] expectedFields = [
            ("Generation", typeof(uint)),
            ("State", stateType),
            ("Debt", debtType),
            ("RawGeneration", typeof(uint)),
            ("Control", typeof(WorldGuidUseControl)),
            ("CarrierGeneration", typeof(uint)),
            ("HistoryRootIndex", typeof(int)),
            ("HistoryRootGeneration", typeof(uint)),
            ("HistoryInterestIndex", typeof(int)),
            ("HistoryInterestGeneration", typeof(uint)),
            ("OperationInterestIndex", typeof(int)),
            ("OperationInterestGeneration", typeof(uint)),
            ("ProcessUseIndex", typeof(int)),
            ("ProcessUseGeneration", typeof(uint)),
            ("Issued", typeof(bool)),
            ("AcquisitionFailure", typeof(Exception)),
            ("CleanupFailure", typeof(Exception)),
        ];

        Assert.Multiple(() => {
            Assert.That(cellType.IsValueType, Is.True);
            Assert.That(cellType.IsLayoutSequential, Is.True);
            Assert.That(backing.FieldType, Is.EqualTo(cellType.MakeArrayType()));
            Assert.That(Enum.GetUnderlyingType(stateType), Is.EqualTo(typeof(int)));
            Assert.That(Enum.GetUnderlyingType(debtType), Is.EqualTo(typeof(int)));
            Assert.That(
                cellType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Select(static field => (field.Name, field.FieldType)),
                Is.EquivalentTo(expectedFields));
            Assert.That(IntPtr.Size, Is.AnyOf(4, 8));
            Assert.That(observedCapacity, Is.EqualTo(Capacity));
        });

        var sizeOf = typeof(Unsafe).GetMethod(
            nameof(Unsafe.SizeOf),
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null)
            ?? throw new InvalidOperationException("The managed size observation is unavailable.");
        var observedStride = (int)(sizeOf.MakeGenericMethod(cellType).Invoke(null, null)
            ?? throw new InvalidOperationException("The managed size observation returned no result."));
        var arrayBytes = typeof(World).GetMethod(
            "ArrayBytes",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: [typeof(int), typeof(int)],
            modifiers: null)
            ?? throw new InvalidOperationException("The World array accounting rule is missing.");
        var accountingCharge = (long)(arrayBytes.Invoke(null, [observedCapacity, observedStride])
            ?? throw new InvalidOperationException("The World array accounting rule returned no result."));

        // The fixed scalar blocks introduce no padding beyond aligning the three
        // references and the final extent to the managed pointer alignment.
        var pointerSize = IntPtr.Size;
        var expectedStride = checked((ScalarBytes + (3 * pointerSize) + pointerSize - 1) & ~(pointerSize - 1));
        // Capacity 128 makes the payload an exact multiple of the rule's eight-byte alignment.
        var expectedCharge = checked(64L + ((long)Capacity * expectedStride));

        Assert.Multiple(() => {
            Assert.That(observedStride, Is.EqualTo(expectedStride));
            Assert.That(accountingCharge, Is.EqualTo(expectedCharge));
        });

        TestContext.Out.WriteLine(
            $"CellStride={observedStride}; Capacity={observedCapacity}; WorldArrayAccountingCharge={accountingCharge}; "
            + $"Framework={RuntimeInformation.FrameworkDescription}; Architecture={RuntimeInformation.ProcessArchitecture}; "
            + $"CoreLib={typeof(object).Assembly.GetName().Version}; RuntimeModule={ownerType.Module.ModuleVersionId}");
    }

    [Test]
    public void QuarantinedLifetimeControlRemainsAnchoredUntilProcessShutdown()
    {
        var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        var registry = WorldGuidRegistry.GetSelected();
        var worldGuid = Guid.NewGuid();

        try
        {
            var lifetimeControl = CreateQuarantinedLifetimeControl(registry, domain, worldGuid);
            domain.Dispose();

            CollectGarbage();
            Assert.That(lifetimeControl.TryGetTarget(out var retainedLifetime), Is.True);
            Assert.That(retainedLifetime!.IsQuarantined, Is.True);
            Assert.That(retainedLifetime.CompleteRetirement(), Is.False);
            Assert.That(registry.TryReserveFresh(worldGuid, out _, out _), Is.False);
            Assert.That(domain.Retirement.IsCompleted, Is.False);
        }
        finally
        {
            domain.Dispose();
        }
    }

    [Test]
    public void WorldDomainReservationFitsItsExisting64ByteEnvelope()
    {
        const int reservationCount = 128;
        var reservations = new object[reservationCount];
        var reservationType = typeof(WorldDomainReservation);
        _ = RuntimeHelpers.GetUninitializedObject(reservationType);
        var allocatedBytesBefore = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < reservations.Length; index++)
        {
            reservations[index] = RuntimeHelpers.GetUninitializedObject(reservationType);
        }

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBytesBefore;

        Assert.That(allocatedBytes, Is.EqualTo(reservationCount * 64));
    }

    [Test]
    public void WorldGuidUseControlFitsItsPrepaid256ByteEnvelope()
    {
        const int controlCount = 128;
        var controls = new object[controlCount];
        var controlType = typeof(WorldGuidUseControl);
        _ = RuntimeHelpers.GetUninitializedObject(controlType);
        var allocatedBytesBefore = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < controls.Length; index++)
        {
            controls[index] = RuntimeHelpers.GetUninitializedObject(controlType);
        }

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBytesBefore;

        Assert.That(
            allocatedBytes,
            Is.EqualTo(controlCount * WorldGuidUseControl.ControlByteCharge));
    }

    [Test]
    public void WorldLifetimeAndCosmeticOwnerHaveMeasuredConstructorFreeAllocations()
    {
        var (lifetimeFirstBytes, lifetimeSteadyBytes) = MeasureUninitializedClassAllocation(typeof(WorldLifetimeControl));
        var (ownerFirstBytes, ownerSteadyBytes) = MeasureUninitializedClassAllocation(typeof(WorldCosmeticCreditOwner));

        Assert.That(lifetimeSteadyBytes, Is.GreaterThan(0));
        Assert.That(ownerSteadyBytes, Is.GreaterThan(0));
        Assert.That(lifetimeSteadyBytes % IntPtr.Size, Is.Zero);
        Assert.That(ownerSteadyBytes % IntPtr.Size, Is.Zero);
        TestContext.Out.WriteLine(
            $"WorldLifetimeControlFirst={lifetimeFirstBytes}; WorldLifetimeControlSteady={lifetimeSteadyBytes}; "
            + $"CosmeticCreditOwnerFirst={ownerFirstBytes}; CosmeticCreditOwnerSteady={ownerSteadyBytes}; "
            + $"Framework={RuntimeInformation.FrameworkDescription}/{RuntimeInformation.ProcessArchitecture}; "
            + $"RuntimeModule={typeof(WorldLifetimeControl).Module.ModuleVersionId}");
    }
}
