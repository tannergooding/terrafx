// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Numerics;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class AngleComparisonTests
{
    [Test]
    public void AngleComparisonUsesDegreesByDefaultAndSupportsExplicitUnitComparers()
    {
        var acuteDegrees = Angle.FromDegrees(45.0f);
        var acuteTurns = Angle.FromTurns(0.125f);
        var obtuseDegrees = Angle.FromDegrees(100.0f);
        var quarterTurn = Angle.FromTurns(0.25f);

        Assert.That(AnglePublicConsumer.Compare(acuteDegrees, acuteTurns), Is.EqualTo((0, false, true, false, true)));
        Assert.That(AnglePublicConsumer.Compare(obtuseDegrees, quarterTurn).Comparison, Is.GreaterThan(0));

        foreach (var unit in Enum.GetValues<AngleUnit>())
        {
            var comparer = Angle.CreateComparer(unit);
            Assert.That(comparer.Compare(obtuseDegrees, quarterTurn), Is.GreaterThan(0));
            Assert.That(comparer.Compare(Angle.FromDegrees(3960.0f), Angle.FromTurns(11.0f)), Is.Zero);
            Assert.That(Angle.CreateComparer(unit), Is.SameAs(comparer));
        }

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Angle.CreateComparer((AngleUnit)3));
        Assert.That(Angle.FromTurns(float.MaxValue).CompareTo(Angle.FromDegrees(float.MaxValue)), Is.GreaterThan(0));
        Assert.That(Angle.FromRadians(float.PositiveInfinity).CompareTo(Angle.FromTurns(float.MaxValue)), Is.GreaterThan(0));
        Assert.That(Angle.FromRadians(float.NaN).CompareTo(Angle.FromDegrees(0.0f)), Is.LessThan(0));
        Assert.That(Angle.FromRadians(-0.0f).CompareTo(Angle.FromDegrees(0.0f)), Is.Zero);
    }

    [Test]
    public void MatchingUnitComparerPreservesStoredSingleOrdering()
    {
        var comparer = Angle.CreateComparer(AngleUnit.Radians);
        var lower = Angle.FromRadians(1.0f);
        var upper = Angle.FromRadians(float.BitIncrement(1.0f));

        Assert.That(comparer.Compare(lower, upper), Is.EqualTo(lower.Value.CompareTo(upper.Value)));
    }
}
