// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests;

[TestFixture]
internal sealed class WorldSchedulingLimitsTests
{
    private const long MinimumBytes = 4096;

    [TestCase(4095L, 0, "maximumRegistrationBytes")]
    [TestCase((16L * 1024 * 1024) + 1, 0, "maximumRegistrationBytes")]
    [TestCase(4095L, 1, "maximumGraphBytes")]
    [TestCase((8L * 1024 * 1024) + 1, 1, "maximumGraphBytes")]
    [TestCase(4095L, 2, "maximumWorkPlanBytes")]
    [TestCase((64L * 1024 * 1024) + 1, 2, "maximumWorkPlanBytes")]
    [TestCase(4095L, 3, "maximumCommandStagingBytes")]
    [TestCase((32L * 1024 * 1024) + 1, 3, "maximumCommandStagingBytes")]
    [TestCase(4095L, 4, "maximumOwnedStateBytes")]
    [TestCase((2L * 1024 * 1024 * 1024) + 1, 4, "maximumOwnedStateBytes")]
    [TestCase(4095L, 5, "maximumDiagnosticBytes")]
    [TestCase((8L * 1024 * 1024) + 1, 5, "maximumDiagnosticBytes")]
    public void ConstructorRejectsByteBudgetOutsideItsAllowedInterval(long value, int index, string parameterName)
    {
        var values = new long[6];
        Array.Fill(values, MinimumBytes);
        values[index] = value;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorldSchedulingLimits(values[0], values[1], values[2], values[3], values[4], values[5], 0));

        Assert.That(exception!.ParamName, Is.EqualTo(parameterName));
    }

    [TestCase(-1)]
    [TestCase(16_777_217)]
    public void ConstructorRejectsConflictComparisonBudgetOutsideItsAllowedInterval(int value)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorldSchedulingLimits(MinimumBytes, MinimumBytes, MinimumBytes, MinimumBytes, MinimumBytes, MinimumBytes, value));

        Assert.That(exception!.ParamName, Is.EqualTo("maximumConflictComparisons"));
    }

    [Test]
    public void DefaultLimitsUseTheDocumentedBudgets()
    {
        var limits = WorldSchedulingLimits.Default;

        Assert.That(limits.MaximumRegistrationBytes, Is.EqualTo(16L * 1024 * 1024));
        Assert.That(limits.MaximumGraphBytes, Is.EqualTo(8L * 1024 * 1024));
        Assert.That(limits.MaximumWorkPlanBytes, Is.EqualTo(64L * 1024 * 1024));
        Assert.That(limits.MaximumCommandStagingBytes, Is.EqualTo(32L * 1024 * 1024));
        Assert.That(limits.MaximumOwnedStateBytes, Is.EqualTo(2L * 1024 * 1024 * 1024));
        Assert.That(limits.MaximumResourceAndSystemStateBytes, Is.EqualTo(128L * 1024 * 1024));
        Assert.That(limits.MaximumDiagnosticBytes, Is.EqualTo(8L * 1024 * 1024));
        Assert.That(limits.MaximumConflictComparisons, Is.EqualTo(16_777_216));
    }

    [Test]
    public void LimitsAdmitTheirExactMinimumAndCeiling()
    {
        var minimum = new WorldSchedulingLimits(MinimumBytes, MinimumBytes, MinimumBytes, MinimumBytes, MinimumBytes, MinimumBytes, 0);
        var maximum = WorldSchedulingLimits.Default;

        Assert.That(minimum.MaximumRegistrationBytes, Is.EqualTo(MinimumBytes));
        Assert.That(minimum.MaximumGraphBytes, Is.EqualTo(MinimumBytes));
        Assert.That(minimum.MaximumWorkPlanBytes, Is.EqualTo(MinimumBytes));
        Assert.That(minimum.MaximumCommandStagingBytes, Is.EqualTo(MinimumBytes));
        Assert.That(minimum.MaximumOwnedStateBytes, Is.EqualTo(MinimumBytes));
        Assert.That(minimum.MaximumResourceAndSystemStateBytes, Is.EqualTo(MinimumBytes));
        Assert.That(minimum.MaximumDiagnosticBytes, Is.EqualTo(MinimumBytes));
        Assert.That(minimum.MaximumConflictComparisons, Is.Zero);
        Assert.That(maximum.MaximumConflictComparisons, Is.EqualTo(16_777_216));
    }

    [Test]
    public void ResourceAndSystemStateSubceilingIsBoundedBySharedWorldState()
    {
        var limits = new WorldSchedulingLimits(
            16L * 1024 * 1024,
            8L * 1024 * 1024,
            64L * 1024 * 1024,
            32L * 1024 * 1024,
            32L * 1024 * 1024,
            8L * 1024 * 1024,
            16_777_216);

        Assert.That(limits.MaximumResourceAndSystemStateBytes, Is.EqualTo(32L * 1024 * 1024));
    }
}
