// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
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
    public void EmptyDiagnosticHasNoPreparationOrRecordingMinimum()
    {
        Assert.That(
            RenderMeshC0NormalDiagnosticWork.CalculateKnownMinimumCpuBytesForWorstCaseGeometry(
                drawCount: 0,
                maximumIndexCount: 0,
                meshAssetCount: 0,
                maximumWorkBytes: 0,
                resultByteLength: 0),
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
            resultByteLength: 1));
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
            + 192);

        Assert.That(
            RenderMeshC0NormalDiagnosticWork.CalculateKnownMinimumCpuBytesForWorstCaseGeometry(
                drawCount: 1,
                maximumIndexCount: 1,
                meshAssetCount: 1,
                maximumWorkBytes: 1,
                resultByteLength: 1),
            Is.EqualTo(expected));
    }

    [TestCase(3, 1, 4, 4)]
    [TestCase(9, 2, 9, 4)]
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
            resultByteLength: checked(16L * drawCount));
        Assert.That(knownMinimum, Is.GreaterThan(minimumMutableAndFrozenCommands));
        Assert.That(knownMinimum, Is.GreaterThan(32L * 1024 * 1024));
    }
}
