// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using TerraFX.Graphics;
using TerraFX.Rendering;

namespace TerraFX.Runtime.Tests.Rendering;

[TestFixture]
internal sealed class RenderMeshC0NormalDiagnosticBudgetTests
{
    [Test]
    public void CommandArrayFloorRefusesAtFirstOverBudgetDrawCountForEachGeometrySchedule()
    {
        const long budget = 32L * 1024 * 1024;

        static long Charge(long commandCount, long stride)
        {
            var payloadBytes = checked(commandCount * stride);
            var arrayBytes = checked(64 + ((payloadBytes + 15) & ~15L));
            return checked(2 * arrayBytes);
        }

        var commandStride = Unsafe.SizeOf<RecordedGraphicsCommand>();
        Assert.That(commandStride, Is.EqualTo(280));

        var repeatedGeometryFirstOverBudget = 1;

        while (Charge(checked((6L * repeatedGeometryFirstOverBudget) - 3), commandStride) <= budget)
        {
            repeatedGeometryFirstOverBudget++;
        }

        var distinctGeometryFirstOverBudget = 1;

        while (Charge(checked((5L * distinctGeometryFirstOverBudget) - 2), commandStride) <= budget)
        {
            distinctGeometryFirstOverBudget++;
        }

        Assert.Multiple(() => {
            Assert.That(repeatedGeometryFirstOverBudget, Is.EqualTo(9_987));
            Assert.That(distinctGeometryFirstOverBudget, Is.EqualTo(11_985));
            Assert.That(
                Charge(checked((6L * (repeatedGeometryFirstOverBudget - 1)) - 3), commandStride),
                Is.LessThanOrEqualTo(budget));
            Assert.That(
                Charge(checked((6L * repeatedGeometryFirstOverBudget) - 3), commandStride),
                Is.GreaterThan(budget));
            Assert.That(
                Charge(checked((5L * (distinctGeometryFirstOverBudget - 1)) - 2), commandStride),
                Is.LessThanOrEqualTo(budget));
            Assert.That(
                Charge(checked((5L * distinctGeometryFirstOverBudget) - 2), commandStride),
                Is.GreaterThan(budget));
        });
    }

    [Test]
    public void CommandRecorderDiagnosticScratchBackingAllocationIsMeasured()
    {
        const int BindingCount = 4;
        const int SampleCount = 64;

        _ = new Dictionary<(ShaderBindingGroup Group, ShaderResourceKind Kind, int Binding), ShaderResourceAccess>()
            .EnsureCapacity(BindingCount);
        _ = new HashSet<(ShaderBindingGroup Group, ShaderResourceKind Kind, int Binding)>()
            .EnsureCapacity(BindingCount);
        _ = new List<(bool IsTexture, ulong ResourceId)>().EnsureCapacity(BindingCount);

        var expectedBindings = new Dictionary<
            (ShaderBindingGroup Group, ShaderResourceKind Kind, int Binding),
            ShaderResourceAccess>[SampleCount];
        var actualBindings = new HashSet<(ShaderBindingGroup Group, ShaderResourceKind Kind, int Binding)>[SampleCount];
        var barrierResources = new List<(bool IsTexture, ulong ResourceId)>[SampleCount];
        var boundStorageUses = new StorageResourceUse[SampleCount][];

        for (var index = 0; index < SampleCount; index++)
        {
            expectedBindings[index] = [];
            actualBindings[index] = [];
            barrierResources[index] = [];
        }

        var allocationStart = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < SampleCount; index++)
        {
            _ = expectedBindings[index].EnsureCapacity(BindingCount);
            _ = actualBindings[index].EnsureCapacity(BindingCount);
            _ = barrierResources[index].EnsureCapacity(BindingCount);
            boundStorageUses[index] = new StorageResourceUse[BindingCount];
        }

        var allocatedBytesPerSample = (GC.GetAllocatedBytesForCurrentThread() - allocationStart) / SampleCount;
        var dictionaryCapacity = expectedBindings[0].EnsureCapacity(0);
        var hashSetCapacity = actualBindings[0].EnsureCapacity(0);
        var barrierListCapacity = barrierResources[0].Capacity;
        GC.KeepAlive(expectedBindings);
        GC.KeepAlive(actualBindings);
        GC.KeepAlive(barrierResources);
        GC.KeepAlive(boundStorageUses);

        Assert.Multiple(() => {
            Assert.That(GraphicsRecordingUseAdmission.IsSupportedProfile, Is.True);
            Assert.That(dictionaryCapacity, Is.EqualTo(7));
            Assert.That(hashSetCapacity, Is.EqualTo(7));
            Assert.That(barrierListCapacity, Is.EqualTo(BindingCount));
            Assert.That(
                Unsafe.SizeOf<(ShaderBindingGroup Group, ShaderResourceKind Kind, int Binding)>(),
                Is.EqualTo(16));
            Assert.That(Unsafe.SizeOf<StorageResourceUse>(), Is.EqualTo(72));
            Assert.That(allocatedBytesPerSample, Is.EqualTo(952));
            Assert.That(
                CommandRecorderRecordingCapacity.CalculateKnownFourBindingScratchArrayBytes(),
                Is.EqualTo(1184));
            Assert.That(
                CommandRecorderRecordingCapacity.CalculateKnownFourBindingScratchArrayBytes(),
                Is.GreaterThanOrEqualTo(allocatedBytesPerSample));
        });
    }

    [Test]
    public void DiagnosticRecipeAndStateChargesCoverTheirExactBackingArrays()
    {
        static long Charge(long count, long stride)
        {
            return checked(64 + ((checked(count * stride) + 15) & ~15L));
        }

        var recipeExpected = checked(
            Charge(2, IntPtr.Size)
            + Charge(1, sizeof(bool))
            + Charge(4, SkinnedNormalDiagnosticRecordingRecipe.DiagnosticResourceEntryStride)
            + Charge(8, sizeof(int)));
        var stateExpected = checked(
            Charge(4, sizeof(int))
            + Charge(3, Unsafe.SizeOf<StorageResourceUse>())
            + Charge(3, sizeof(bool))
            + Charge(2, Unsafe.SizeOf<StorageResourceUse>()));

        Assert.Multiple(() => {
            Assert.That(
                SkinnedNormalDiagnosticRecordingRecipe.CalculateKnownBackingArrayBytes(2, 1),
                Is.EqualTo(recipeExpected));
            Assert.That(
                SkinnedNormalDiagnosticRecordingRecipe.CalculateKnownBackingArrayBytes(0, 0),
                Is.Zero);
            Assert.That(
                SkinnedNormalDiagnosticRecordingState.CalculateKnownBackingArrayBytes(2, 1),
                Is.EqualTo(stateExpected));
            Assert.That(
                SkinnedNormalDiagnosticRecordingState.CalculateKnownBackingArrayBytes(0, 0),
                Is.Zero);
        });
    }

    [Test]
    public void DiagnosticRecipeObjectCarriersMatchSelectedRuntimeLayout()
    {
        const int SampleCount = 64;

        static long MeasureObjectBytes(Type type)
        {
            var warmInstances = new object[SampleCount];

            for (var index = 0; index < warmInstances.Length; index++)
            {
                warmInstances[index] = RuntimeHelpers.GetUninitializedObject(type);
            }

            GC.KeepAlive(warmInstances);
            var measuredInstances = new object[SampleCount];
            var allocationStart = GC.GetAllocatedBytesForCurrentThread();

            for (var index = 0; index < measuredInstances.Length; index++)
            {
                measuredInstances[index] = RuntimeHelpers.GetUninitializedObject(type);
            }

            var measuredBytes = (GC.GetAllocatedBytesForCurrentThread() - allocationStart) / SampleCount;
            GC.KeepAlive(measuredInstances);
            return measuredBytes;
        }

        var accessComparerType = typeof(SkinnedNormalDiagnosticRecordingRecipe).GetNestedType(
            "GraphResourceAccessIndexComparer",
            BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The diagnostic access comparer type is unavailable.");
        var entryComparerType = typeof(SkinnedNormalDiagnosticRecordingRecipe).GetNestedType(
            "DiagnosticResourceEntryComparer",
            BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The diagnostic resource comparer type is unavailable.");
        Assert.That(GraphicsRecordingUseAdmission.IsSupportedProfile, Is.True);

        var recipeBytes = MeasureObjectBytes(typeof(SkinnedNormalDiagnosticRecordingRecipe));
        var stateBytes = MeasureObjectBytes(typeof(SkinnedNormalDiagnosticRecordingState));
        var lockBytes = MeasureObjectBytes(typeof(System.Threading.Lock));
        var accessComparerBytes = MeasureObjectBytes(accessComparerType);
        var entryComparerBytes = MeasureObjectBytes(entryComparerType);

        Assert.Multiple(() => {
            Assert.That(recipeBytes, Is.EqualTo(SkinnedNormalDiagnosticRecordingRecipe.CarrierByteCharge));
            Assert.That(stateBytes, Is.EqualTo(SkinnedNormalDiagnosticRecordingState.CarrierByteCharge));
            Assert.That(lockBytes, Is.EqualTo(SkinnedNormalDiagnosticRecordingRecipe.LockByteCharge));
            Assert.That(
                accessComparerBytes,
                Is.EqualTo(SkinnedNormalDiagnosticRecordingRecipe.AccessComparerByteCharge));
            Assert.That(
                entryComparerBytes,
                Is.EqualTo(SkinnedNormalDiagnosticRecordingRecipe.EntryComparerByteCharge));
            Assert.That(
                SkinnedNormalDiagnosticRecordingRecipe.KnownObjectByteCharge,
                Is.EqualTo(
                    recipeBytes
                    + lockBytes
                    + (2 * accessComparerBytes)
                    + entryComparerBytes));
        });
    }

    [Test]
    public void EmptyDiagnosticHasNoPreparationOrRecordingMinimum()
    {
        Assert.That(
            RenderMeshC0NormalDiagnosticWork.CalculateKnownMinimumCpuBytesForWorstCaseGeometry(
                drawCount: 0,
                maximumIndexCount: 0,
                meshAssetCount: 0,
                maximumWorkBytes: 0,
                resultByteLength: 0,
                pendingGraphSubmissionBackingByteCharge: 64 + (4 * IntPtr.Size)),
            Is.Zero);
    }

    [Test]
    public void KnownMinimumRejectsCheckedArrayChargeOverflow()
    {
        _ = Assert.Throws<OutOfMemoryException>(() => RenderMeshC0NormalDiagnosticWork.CalculateKnownMinimumCpuBytesForWorstCaseGeometry(
            drawCount: 1,
            maximumIndexCount: 1,
            meshAssetCount: 1,
            maximumWorkBytes: long.MaxValue,
            resultByteLength: 1,
            pendingGraphSubmissionBackingByteCharge: 64 + (4 * IntPtr.Size)));
    }

    [Test]
    public void KnownMinimumRejectsNegativeOrOverflowingOwnerBackingCharge()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => RenderMeshC0NormalDiagnosticWork.CalculateKnownMinimumCpuBytesForWorstCaseGeometry(
            drawCount: 1,
            maximumIndexCount: 1,
            meshAssetCount: 1,
            maximumWorkBytes: 1,
            resultByteLength: 1,
            pendingGraphSubmissionBackingByteCharge: -1));
        _ = Assert.Throws<OutOfMemoryException>(() => RenderMeshC0NormalDiagnosticWork.CalculateKnownMinimumCpuBytesForWorstCaseGeometry(
            drawCount: 1,
            maximumIndexCount: 1,
            meshAssetCount: 1,
            maximumWorkBytes: 1,
            resultByteLength: 1,
            pendingGraphSubmissionBackingByteCharge: long.MaxValue));
    }

    [Test]
    public void KnownMinimumUsesConservativeArrayHeaderAndAlignmentCharges()
    {
        static long Charge(long count, long stride)
        {
            return checked(64 + ((checked(count * stride) + 15) & ~15L));
        }

        var expected = checked(
            Charge(1, Unsafe.SizeOf<RenderMeshC0NormalDiagnosticDraw>())
            + Charge(1, sizeof(int))
            + (3 * Charge(1, sizeof(byte)))
            + Charge(4, Unsafe.SizeOf<RecordedGraphicsCommand>())
            + Charge(3, Unsafe.SizeOf<RecordedGraphicsCommand>())
            + Charge(4, IntPtr.Size)
            + Charge(1, IntPtr.Size)
            + 80
            + 192
            + Charge(256, IntPtr.Size)
            + Charge(4, IntPtr.Size)
            + SkinnedNormalDiagnosticRecordingRecipe.CalculateKnownBackingArrayBytes(
                drawCount: 1,
                geometryCount: 1)
            + SkinnedNormalDiagnosticRecordingState.CalculateKnownBackingArrayBytes(
                drawCount: 1,
                geometryCount: 1)
            + CommandRecorderRecordingCapacity.CalculateKnownFourBindingScratchArrayBytes()
            + SkinnedNormalDiagnosticRecordingRecipe.KnownObjectByteCharge
            + SkinnedNormalDiagnosticRecordingState.CarrierByteCharge
            + RenderMeshC0NormalDiagnosticWork.CalculateKnownBindingPlanConstructionBytes(
                drawCount: 1,
                meshAssetCount: 1));

        Assert.That(
            RenderMeshC0NormalDiagnosticWork.CalculateKnownMinimumCpuBytesForWorstCaseGeometry(
                drawCount: 1,
                maximumIndexCount: 1,
                meshAssetCount: 1,
                maximumWorkBytes: 1,
                resultByteLength: 1,
                pendingGraphSubmissionBackingByteCharge: 64 + (4 * IntPtr.Size)),
            Is.EqualTo(expected));
    }

    [TestCase(3, 1, 4, 4)]
    [TestCase(9, 2, 9, 4)]
    [TestCase(8, 2, 8, 4)]
    [TestCase(15, 3, 15, 4)]
    [TestCase(13, 3, 13, 4)]
    [TestCase(14, 3, 14, 4)]
    public void RecordingArrayComponentIncludesSmallListCapacityAndBothFreezeCopies(
        int commandCount,
        int useCount,
        int commandCapacity,
        int useCapacity)
    {
        static long Charge(long count, long stride)
        {
            return checked(64 + ((checked(count * stride) + 15) & ~15L));
        }

        var capacity = new CommandRecorderRecordingCapacity(commandCount, useCount, 0, 4);
        var expected = checked(
            Charge(commandCapacity, Unsafe.SizeOf<RecordedGraphicsCommand>())
            + Charge(commandCount, Unsafe.SizeOf<RecordedGraphicsCommand>())
            + Charge(useCapacity, IntPtr.Size)
            + Charge(useCount, IntPtr.Size));
        Assert.That(capacity.CommandBackingCapacity, Is.EqualTo(commandCapacity));
        Assert.That(capacity.UseLeaseBackingCapacity, Is.EqualTo(useCapacity));
        Assert.That(capacity.KnownBackingArrayBytes, Is.EqualTo(expected));
    }

    [Test]
    public void RecordingArrayComponentRejectsCheckedCapacityOverflow()
    {
        _ = Assert.Throws<OverflowException>(
            () => CommandRecorderRecordingCapacity.CalculateKnownBackingArrayBytes(long.MaxValue, 1));
        Assert.That(CommandRecorderRecordingCapacity.CalculateKnownBackingArrayBytes(0, 0), Is.Zero);
    }

    [Test]
    public void RecordingUseCohortIncludesItsCarrierAndRejectsAnInsufficientGrant()
    {
        Assert.That(GraphicsRecordingUseAdmission.CalculateByteAllowance(0), Is.Zero);
        Assert.That(GraphicsRecordingUseAdmission.CalculateByteAllowance(2), Is.EqualTo(80 + (2 * 192)));
        _ = Assert.Throws<OutOfMemoryException>(() => GraphicsRecordingUseAdmission.Create(2, 80 + (2 * 192) - 1));
        _ = Assert.Throws<OverflowException>(() => GraphicsRecordingUseAdmission.CalculateByteAllowance(long.MaxValue));
    }

    [Test]
    public void RowCeilingMinimumCommandArraysExceedTheRecordingAllowance()
    {
        var drawCount = Renderer.MaximumDrawInstanceCount;
        var minimumCommandCount = checked((5L * drawCount) - 2);
        var minimumMutableAndFrozenCommands = checked(
            2L * minimumCommandCount * Unsafe.SizeOf<RecordedGraphicsCommand>());

        Assert.That(minimumMutableAndFrozenCommands, Is.GreaterThan(32L * 1024 * 1024));

        var knownMinimum = RenderMeshC0NormalDiagnosticWork.CalculateKnownMinimumCpuBytesForWorstCaseGeometry(
            drawCount,
            maximumIndexCount: 1,
            meshAssetCount: 1,
            maximumWorkBytes: checked(32L * drawCount),
            resultByteLength: checked(16L * drawCount),
            pendingGraphSubmissionBackingByteCharge: 64 + (4 * IntPtr.Size));
        Assert.That(knownMinimum, Is.GreaterThan(minimumMutableAndFrozenCommands));
        Assert.That(knownMinimum, Is.GreaterThan(32L * 1024 * 1024));
    }
}
