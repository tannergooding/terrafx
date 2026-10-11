// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Numerics;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class GeometryComparisonTests
{
    private static float GetSignedUnit(int signCode, int component) =>
        (signCode & (1 << component)) != 0 ? 1.0f : -1.0f;

    [Test]
    public void VectorComparisonHandlesExceptionalAndNonNormalComponentsAtEveryDimension()
    {
        Vector3[] values3 =
        [
            new Vector3(float.NaN, 1.0f, 2.0f),
            new Vector3(float.NegativeInfinity, 0.0f, 0.0f),
            new Vector3(-float.MaxValue, 0.0f, float.Epsilon),
            new Vector3(-0.0f, 0.0f, 0.0f),
            new Vector3(float.Epsilon, 0.0f, 0.0f),
            new Vector3(float.MaxValue, float.Epsilon, 0.0f),
            new Vector3(float.PositiveInfinity, 0.0f, 0.0f),
        ];

        Vector4[] values4 =
        [
            new Vector4(1.0f, 2.0f, 3.0f, float.NaN),
            new Vector4(0.0f, 0.0f, float.NegativeInfinity, 0.0f),
            new Vector4(-float.MaxValue, 0.0f, 0.0f, float.Epsilon),
            new Vector4(-0.0f, 0.0f, 0.0f, 0.0f),
            new Vector4(float.Epsilon, 0.0f, 0.0f, 0.0f),
            new Vector4(float.MaxValue, float.MaxValue, float.Epsilon, 0.0f),
            new Vector4(0.0f, 0.0f, 0.0f, float.PositiveInfinity),
        ];

        ComparisonTestUtilities.AssertOrderingLaws(values3);
        ComparisonTestUtilities.AssertOrderingLaws(values4);
    }

    [Test]
    public void VectorComparisonObeysOrderingLawsForRawIeeeValues()
    {
        Vector2[] values =
        [
            new Vector2(float.NaN, 1.0f),
            new Vector2(float.NegativeInfinity, 0.0f),
            new Vector2(-float.MaxValue, 0.0f),
            new Vector2(-0.0f, 0.0f),
            new Vector2(float.Epsilon, 0.0f),
            new Vector2(float.MaxValue, float.Epsilon),
            new Vector2(float.PositiveInfinity, 0.0f),
        ];

        ComparisonTestUtilities.AssertOrderingLaws(values);
    }

    [Test]
    public void VectorComparisonUsesExactMagnitudeThenOrthantAndCounterclockwiseDirection()
    {
        var orthants = new[]
        {
            new Vector2(1.0f, 1.0f),
            new Vector2(-1.0f, 1.0f),
            new Vector2(1.0f, -1.0f),
            new Vector2(-1.0f, -1.0f),
        };

        ComparisonTestUtilities.AssertOrderingLaws(orthants);

        for (var index = 0; index < orthants.Length - 1; index++)
        {
            Assert.That(orthants[index].CompareTo(orthants[index + 1]), Is.GreaterThan(0));
        }

        Assert.That(new Vector2(-0.0f, 1.0f).CompareTo(new Vector2(0.0f, 1.0f)), Is.Zero);

        (Vector2 Before, Vector2 After)[] counterclockwisePairs =
        [
            (new Vector2(4.0f, 3.0f), new Vector2(3.0f, 4.0f)),
            (new Vector2(-3.0f, 4.0f), new Vector2(-4.0f, 3.0f)),
            (new Vector2(-4.0f, -3.0f), new Vector2(-3.0f, -4.0f)),
            (new Vector2(3.0f, -4.0f), new Vector2(4.0f, -3.0f)),
        ];

        foreach (var (before, after) in counterclockwisePairs)
        {
            Assert.That(before.CompareTo(after), Is.LessThan(0));
        }

        Assert.That(new Vector2(1.0f, 1.0f).CompareTo(new Vector2(2.0f, 0.0f)), Is.LessThan(0));
        Assert.That(GeometryPublicConsumer.Compare(new Vector3(4.0f, 3.0f, 0.0f), new Vector3(3.0f, 4.0f, 0.0f)).Comparison, Is.LessThan(0));
        Assert.That(new Vector3(0.0f, 4.0f, 3.0f).CompareTo(new Vector3(0.0f, 3.0f, 4.0f)), Is.LessThan(0));
        Assert.That(GeometryPublicConsumer.Compare(new Vector4(0.0f, 0.0f, 4.0f, 3.0f), new Vector4(0.0f, 0.0f, 3.0f, 4.0f)).Comparison, Is.LessThan(0));
    }

    [Test]
    public void VectorComparisonUsesReversedRowMajorSignCodesAtEveryDimension()
    {
        var vectors3 = new Vector3[8];

        for (var signCode = 7; signCode >= 0; signCode--)
        {
            vectors3[7 - signCode] = new Vector3(GetSignedUnit(signCode, 0), GetSignedUnit(signCode, 1), GetSignedUnit(signCode, 2));
        }

        ComparisonTestUtilities.AssertOrderingLaws(vectors3);

        var vectors4 = new Vector4[16];

        for (var signCode = 15; signCode >= 0; signCode--)
        {
            vectors4[15 - signCode] = new Vector4(
                GetSignedUnit(signCode, 0),
                GetSignedUnit(signCode, 1),
                GetSignedUnit(signCode, 2),
                GetSignedUnit(signCode, 3));
        }

        ComparisonTestUtilities.AssertOrderingLaws(vectors4);
    }

    [Test]
    public void VectorMagnitudeComparisonMatchesLiteralBoundaryOrder()
    {
        var subnormalDiagonal = new Vector2(float.Epsilon, float.Epsilon);
        var subnormalAxis = new Vector2(float.Epsilon, 0.0f);
        Assert.That(Vector2.LengthSquared(subnormalDiagonal), Is.Zero);
        Assert.That(Vector2.LengthSquared(subnormalAxis), Is.Zero);
        Assert.That(subnormalDiagonal.CompareTo(subnormalAxis), Is.GreaterThan(0));

        Assert.That(new Vector2(3.0f, 4.0f).CompareTo(new Vector2(4.0f, 4.0f)), Is.LessThan(0));

        var maximumAxis = new Vector3(float.MaxValue, 0.0f, 0.0f);
        var maximumPlusSubnormal = new Vector3(float.MaxValue, float.Epsilon, 0.0f);
        Assert.That(Vector3.LengthSquared(maximumAxis), Is.EqualTo(float.PositiveInfinity));
        Assert.That(Vector3.LengthSquared(maximumPlusSubnormal), Is.EqualTo(float.PositiveInfinity));
        Assert.That(maximumPlusSubnormal.CompareTo(maximumAxis), Is.GreaterThan(0));
    }

    [TestCase(-149)]
    [TestCase(-126)]
    [TestCase(-65)]
    [TestCase(0)]
    [TestCase(63)]
    [TestCase(127)]
    public void VectorMagnitudeComparisonRetainsSmallContributionsWhenRoundedLengthsTie(int exponent)
    {
        var x = float.ScaleB(1.0f, exponent);
        var left2 = new Vector2(x, float.Epsilon);
        var right2 = new Vector2(x, 0.0f);
        var left3 = new Vector3(x, 0.0f, float.Epsilon);
        var right3 = new Vector3(x, 0.0f, 0.0f);
        var left4 = new Vector4(x, 0.0f, 0.0f, float.Epsilon);
        var right4 = new Vector4(x, 0.0f, 0.0f, 0.0f);

        Assert.That(Vector2.LengthSquared(left2), Is.EqualTo(Vector2.LengthSquared(right2)));
        Assert.That(Vector3.LengthSquared(left3), Is.EqualTo(Vector3.LengthSquared(right3)));
        Assert.That(Vector4.LengthSquared(left4), Is.EqualTo(Vector4.LengthSquared(right4)));
        Assert.That(left2.CompareTo(right2), Is.GreaterThan(0));
        Assert.That(left3.CompareTo(right3), Is.GreaterThan(0));
        Assert.That(left4.CompareTo(right4), Is.GreaterThan(0));
    }

    [Test]
    public void VectorMagnitudeComparisonRetainsSubnormalAndExtremeContributions()
    {
        var largest = new Vector2(float.MaxValue, 0.0f);
        var largestPlusSubnormal = new Vector2(float.MaxValue, float.Epsilon);
        var smallest = new Vector2(float.Epsilon, 0.0f);
        var smallestDiagonal = new Vector2(float.Epsilon, float.Epsilon);

        Assert.That(largestPlusSubnormal.CompareTo(largest), Is.GreaterThan(0));
        Assert.That(smallestDiagonal.CompareTo(smallest), Is.GreaterThan(0));
        Assert.That(new Vector4(float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue).CompareTo(Vector4.Zero), Is.GreaterThan(0));
    }
}
