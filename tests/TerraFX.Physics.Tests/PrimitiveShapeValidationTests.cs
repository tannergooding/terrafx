// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using BigInteger = System.Numerics.BigInteger;
using TerraFX.Numerics;

namespace TerraFX.Physics.Tests;

[TestFixture]
internal sealed class PrimitiveShapeValidationTests
{
    private static readonly (int Exponent, uint MinimumBits, uint MaximumBits)[] s_primitiveExtentCases = [
        (-30, 0x6EA1_8F08, 0x7449_F2CA),
        (0, 0x3CCC_CCCD, 0x4280_0000),
        (3, 0x37D1_B717, 0x3D83_126F),
        (30, 0x0B01_CEB3, 0x10A2_4260),
    ];

    private static void AssertEnclosingRadius(Vector2 halfExtents, float enclosingRadius)
    {
        var x = EncodeUnits(halfExtents.X);
        var y = EncodeUnits(halfExtents.Y);
        var squaredNorm = (x * x) + (y * y);
        var radius = EncodeUnits(enclosingRadius);
        var previousRadius = EncodeUnits(float.BitDecrement(enclosingRadius));

        Assert.That(radius * radius, Is.GreaterThanOrEqualTo(squaredNorm));
        Assert.That(previousRadius * previousRadius, Is.LessThan(squaredNorm));
    }

    private static void AssertEnclosingRadius(Vector3 halfExtents, float enclosingRadius)
    {
        var x = EncodeUnits(halfExtents.X);
        var y = EncodeUnits(halfExtents.Y);
        var z = EncodeUnits(halfExtents.Z);
        var squaredNorm = (x * x) + (y * y) + (z * z);
        var radius = EncodeUnits(enclosingRadius);
        var previousRadius = EncodeUnits(float.BitDecrement(enclosingRadius));

        Assert.That(radius * radius, Is.GreaterThanOrEqualTo(squaredNorm));
        Assert.That(previousRadius * previousRadius, Is.LessThan(squaredNorm));
    }

    private static void AssertMaterializes(ShapeMeasure actual, uint expectedBits)
    {
        Assert.That(actual.TryGetSingle(out var value), Is.True);
        Assert.That(BitConverter.SingleToUInt32Bits(value), Is.EqualTo(expectedBits));
    }

    private static void AssertMeasure(ShapeMeasure actual, (uint SignificandBits, int BinaryExponent) expected)
    {
        Assert.That(unchecked((uint)BitConverter.SingleToInt32Bits(actual.Significand)), Is.EqualTo(expected.SignificandBits));
        Assert.That(actual.BinaryExponent, Is.EqualTo(expected.BinaryExponent));
        Assert.That(float.IsFinite(actual.Significand), Is.True);
        Assert.That(actual.Significand, Is.GreaterThanOrEqualTo(1.0f));
        Assert.That(actual.Significand, Is.LessThan(2.0f));
        Assert.That(actual.BinaryExponent, Is.InRange(-512, 512));
    }

    private static void AssertPrimitive2DBoundaries()
    {
        foreach (var (exponent, minimumBits, maximumBits) in s_primitiveExtentCases)
        {
            var settings = new SpatialSettings(exponent, 12);
            var geometryContext = new PrimitiveShapeGeometryContext2D(settings);
            var minimum = BitConverter.UInt32BitsToSingle(minimumBits);
            var maximum = BitConverter.UInt32BitsToSingle(maximumBits);

            Assert.That(BitConverter.SingleToInt32Bits(SpatialMath.ConvertQuantity(1, 40, 0, exponent, 1)), Is.EqualTo(minimumBits));
            Assert.That(BitConverter.SingleToInt32Bits(SpatialMath.ConvertQuantity(64, 1, 0, exponent, 1)), Is.EqualTo(maximumBits));
            Assert.That(BitConverter.SingleToInt32Bits(geometryContext.MinimumPrimitiveExtent), Is.EqualTo(minimumBits));
            Assert.That(BitConverter.SingleToInt32Bits(geometryContext.MaximumLocalExtent), Is.EqualTo(maximumBits));

            Assert.That(
                PrimitiveShapeValidator2D.Validate(new CircleShapeDefinition2D(minimum), geometryContext).Status,
                Is.EqualTo(ShapeValidationStatus.Valid),
                $"minimum circle exponent={exponent}");
            Assert.That(
                PrimitiveShapeValidator2D.Validate(new CircleShapeDefinition2D(float.BitDecrement(minimum)), geometryContext).Fault?.Code,
                Is.EqualTo(ShapeValidationFaultCode.PrimitiveExtentBelowMinimum),
                $"below-minimum circle exponent={exponent}");
            Assert.That(
                PrimitiveShapeValidator2D.Validate(new CircleShapeDefinition2D(maximum), geometryContext).Status,
                Is.EqualTo(ShapeValidationStatus.Valid),
                $"maximum circle exponent={exponent}");
            Assert.That(
                PrimitiveShapeValidator2D.Validate(new CircleShapeDefinition2D(float.BitIncrement(maximum)), geometryContext).Fault?.Code,
                Is.EqualTo(ShapeValidationFaultCode.LocalExtentExceeded),
                $"above-maximum circle exponent={exponent}");

            Assert.That(
                PrimitiveShapeValidator2D.Validate(new BoxShapeDefinition2D(new Vector2(minimum, minimum)), geometryContext).Status,
                Is.EqualTo(ShapeValidationStatus.Valid),
                $"minimum box exponent={exponent}");
            Assert.That(
                PrimitiveShapeValidator2D.Validate(new BoxShapeDefinition2D(new Vector2(float.BitDecrement(minimum), minimum)), geometryContext).Fault?.Field,
                Is.EqualTo(ShapeValidationField.HalfExtentX),
                $"below-minimum X exponent={exponent}");
            Assert.That(
                PrimitiveShapeValidator2D.Validate(new BoxShapeDefinition2D(new Vector2(minimum, float.BitDecrement(minimum))), geometryContext).Fault?.Field,
                Is.EqualTo(ShapeValidationField.HalfExtentY),
                $"below-minimum Y exponent={exponent}");
            Assert.That(
                PrimitiveShapeValidator2D.Validate(new BoxShapeDefinition2D(new Vector2(float.BitIncrement(maximum), minimum)), geometryContext).Fault?.Field,
                Is.EqualTo(ShapeValidationField.HalfExtentX),
                $"above-maximum X exponent={exponent}");
            Assert.That(
                PrimitiveShapeValidator2D.Validate(new BoxShapeDefinition2D(new Vector2(minimum, float.BitIncrement(maximum))), geometryContext).Fault?.Field,
                Is.EqualTo(ShapeValidationField.HalfExtentY),
                $"above-maximum Y exponent={exponent}");
            Assert.That(
                PrimitiveShapeValidator2D.Validate(new BoxShapeDefinition2D(new Vector2(maximum, minimum)), geometryContext).Fault?.Field,
                Is.EqualTo(ShapeValidationField.EnclosingRadius),
                $"corner beyond maximum exponent={exponent}");
        }
    }

    private static void AssertPrimitive3DBoundaries()
    {
        foreach (var (exponent, minimumBits, maximumBits) in s_primitiveExtentCases)
        {
            var settings = new SpatialSettings(exponent, 12);
            var geometryContext = new PrimitiveShapeGeometryContext3D(settings);
            var minimum = BitConverter.UInt32BitsToSingle(minimumBits);
            var maximum = BitConverter.UInt32BitsToSingle(maximumBits);

            Assert.That(BitConverter.SingleToInt32Bits(SpatialMath.ConvertQuantity(1, 40, 0, exponent, 1)), Is.EqualTo(minimumBits));
            Assert.That(BitConverter.SingleToInt32Bits(SpatialMath.ConvertQuantity(64, 1, 0, exponent, 1)), Is.EqualTo(maximumBits));
            Assert.That(BitConverter.SingleToInt32Bits(geometryContext.MinimumPrimitiveExtent), Is.EqualTo(minimumBits));
            Assert.That(BitConverter.SingleToInt32Bits(geometryContext.MaximumLocalExtent), Is.EqualTo(maximumBits));

            Assert.That(
                PrimitiveShapeValidator3D.Validate(new SphereShapeDefinition3D(minimum), geometryContext).Status,
                Is.EqualTo(ShapeValidationStatus.Valid),
                $"minimum sphere exponent={exponent}");
            Assert.That(
                PrimitiveShapeValidator3D.Validate(new SphereShapeDefinition3D(float.BitDecrement(minimum)), geometryContext).Fault?.Code,
                Is.EqualTo(ShapeValidationFaultCode.PrimitiveExtentBelowMinimum),
                $"below-minimum sphere exponent={exponent}");
            Assert.That(
                PrimitiveShapeValidator3D.Validate(new SphereShapeDefinition3D(maximum), geometryContext).Status,
                Is.EqualTo(ShapeValidationStatus.Valid),
                $"maximum sphere exponent={exponent}");
            Assert.That(
                PrimitiveShapeValidator3D.Validate(new SphereShapeDefinition3D(float.BitIncrement(maximum)), geometryContext).Fault?.Code,
                Is.EqualTo(ShapeValidationFaultCode.LocalExtentExceeded),
                $"above-maximum sphere exponent={exponent}");

            Assert.That(
                PrimitiveShapeValidator3D.Validate(new BoxShapeDefinition3D(new Vector3(minimum, minimum, minimum)), geometryContext).Status,
                Is.EqualTo(ShapeValidationStatus.Valid),
                $"minimum box exponent={exponent}");
            Assert.That(
                PrimitiveShapeValidator3D.Validate(new BoxShapeDefinition3D(new Vector3(float.BitDecrement(minimum), minimum, minimum)), geometryContext).Fault?.Field,
                Is.EqualTo(ShapeValidationField.HalfExtentX),
                $"below-minimum X exponent={exponent}");
            Assert.That(
                PrimitiveShapeValidator3D.Validate(new BoxShapeDefinition3D(new Vector3(minimum, float.BitDecrement(minimum), minimum)), geometryContext).Fault?.Field,
                Is.EqualTo(ShapeValidationField.HalfExtentY),
                $"below-minimum Y exponent={exponent}");
            Assert.That(
                PrimitiveShapeValidator3D.Validate(new BoxShapeDefinition3D(new Vector3(minimum, minimum, float.BitDecrement(minimum))), geometryContext).Fault?.Field,
                Is.EqualTo(ShapeValidationField.HalfExtentZ),
                $"below-minimum Z exponent={exponent}");
            Assert.That(
                PrimitiveShapeValidator3D.Validate(new BoxShapeDefinition3D(new Vector3(float.BitIncrement(maximum), minimum, minimum)), geometryContext).Fault?.Field,
                Is.EqualTo(ShapeValidationField.HalfExtentX),
                $"above-maximum X exponent={exponent}");
            Assert.That(
                PrimitiveShapeValidator3D.Validate(new BoxShapeDefinition3D(new Vector3(minimum, float.BitIncrement(maximum), minimum)), geometryContext).Fault?.Field,
                Is.EqualTo(ShapeValidationField.HalfExtentY),
                $"above-maximum Y exponent={exponent}");
            Assert.That(
                PrimitiveShapeValidator3D.Validate(new BoxShapeDefinition3D(new Vector3(minimum, minimum, float.BitIncrement(maximum))), geometryContext).Fault?.Field,
                Is.EqualTo(ShapeValidationField.HalfExtentZ),
                $"above-maximum Z exponent={exponent}");
            Assert.That(
                PrimitiveShapeValidator3D.Validate(new BoxShapeDefinition3D(new Vector3(maximum, minimum, minimum)), geometryContext).Fault?.Field,
                Is.EqualTo(ShapeValidationField.EnclosingRadius),
                $"corner beyond maximum exponent={exponent}");
        }
    }

    private static BigInteger EncodeUnits(float value)
    {
        var bits = unchecked((uint)BitConverter.SingleToInt32Bits(value));
        var exponent = (int)((bits >> 23) & 0xFF);
        var significand = bits & 0x007F_FFFF;

        if ((bits & 0x8000_0000U) != 0 || exponent == 0xFF)
        {
            throw new ArgumentException("The exact-radius assertion accepts only finite nonnegative values.", nameof(value));
        }

        var power = -149;

        if (exponent != 0)
        {
            significand |= 0x0080_0000U;
            power = exponent - 150;
        }

        return new BigInteger(significand) << (power + 149);
    }

    [Test]
    [Category("M03Primitive2D")]
    [Category("M03Primitive3D")]
    public void BoxEnclosingRadiusIsTheLeastBinary32ValueAboveTheExactNorm()
    {
        var definition2D = new BoxShapeDefinition2D(new Vector2(2.0f, 3.0f));
        var definition3D = new BoxShapeDefinition3D(new Vector3(2.0f, 3.0f, 4.0f));
        var response2D = PrimitiveShapeValidator2D.Validate(definition2D, PrimitiveShapeGeometryContext2D.Default);
        var response3D = PrimitiveShapeValidator3D.Validate(definition3D, PrimitiveShapeGeometryContext3D.Default);
        var statistics2D = response2D.Statistics ?? throw new InvalidOperationException("Valid geometry must provide statistics.");
        var statistics3D = response3D.Statistics ?? throw new InvalidOperationException("Valid geometry must provide statistics.");

        AssertEnclosingRadius(definition2D.HalfExtents, statistics2D.EnclosingRadius);
        AssertEnclosingRadius(definition3D.HalfExtents, statistics3D.EnclosingRadius);
    }

    [Test]
    [Category("M03Primitive2D")]
    [Category("M03Primitive3D")]
    public void ComponentBoundsAreCheckedBeforeExactCornerRadius()
    {
        var context2D = PrimitiveShapeGeometryContext2D.Default;
        var accepted2D = PrimitiveShapeValidator2D.Validate(
            new BoxShapeDefinition2D(new Vector2(48.0f, 36.0f)),
            context2D);

        Assert.That(accepted2D.Status, Is.EqualTo(ShapeValidationStatus.Valid));
        Assert.That(accepted2D.Fault, Is.Null);
        Assert.That(accepted2D.Statistics?.EnclosingRadius, Is.EqualTo(60.0f));

        var corner2D = PrimitiveShapeValidator2D.Validate(
            new BoxShapeDefinition2D(new Vector2(64.0f, context2D.MinimumPrimitiveExtent)),
            context2D);

        Assert.That(corner2D.Status, Is.EqualTo(ShapeValidationStatus.Rejected));
        Assert.That(corner2D.Statistics, Is.Null);
        Assert.That(corner2D.Fault?.Code, Is.EqualTo(ShapeValidationFaultCode.LocalExtentExceeded));
        Assert.That(corner2D.Fault?.Field, Is.EqualTo(ShapeValidationField.EnclosingRadius));
        Assert.That(corner2D.Fault?.ObservedValue, Is.GreaterThan(64.0f));
        Assert.That(corner2D.Fault?.Limit, Is.EqualTo(64.0f));

        var aboveComponentLimit2D = PrimitiveShapeValidator2D.Validate(
            new BoxShapeDefinition2D(new Vector2(float.BitIncrement(context2D.MaximumLocalExtent), context2D.MinimumPrimitiveExtent)),
            context2D);

        Assert.That(aboveComponentLimit2D.Fault?.Code, Is.EqualTo(ShapeValidationFaultCode.LocalExtentExceeded));
        Assert.That(aboveComponentLimit2D.Fault?.Field, Is.EqualTo(ShapeValidationField.HalfExtentX));

        var minimumBeforeMaximum2D = PrimitiveShapeValidator2D.Validate(
            new BoxShapeDefinition2D(new Vector2(
                float.BitIncrement(context2D.MaximumLocalExtent),
                float.BitDecrement(context2D.MinimumPrimitiveExtent))),
            context2D);
        Assert.That(minimumBeforeMaximum2D.Fault?.Code, Is.EqualTo(ShapeValidationFaultCode.PrimitiveExtentBelowMinimum));
        Assert.That(minimumBeforeMaximum2D.Fault?.Field, Is.EqualTo(ShapeValidationField.HalfExtentY));

        var secondMaximum2D = PrimitiveShapeValidator2D.Validate(
            new BoxShapeDefinition2D(new Vector2(context2D.MaximumLocalExtent, float.BitIncrement(context2D.MaximumLocalExtent))),
            context2D);
        Assert.That(secondMaximum2D.Fault?.Code, Is.EqualTo(ShapeValidationFaultCode.LocalExtentExceeded));
        Assert.That(secondMaximum2D.Fault?.Field, Is.EqualTo(ShapeValidationField.HalfExtentY));

        var minimumBeforeMaximum = PrimitiveShapeValidator3D.Validate(
            new BoxShapeDefinition3D(new Vector3(65.0f, 0.0f, context2D.MinimumPrimitiveExtent)),
            PrimitiveShapeGeometryContext3D.Default);

        Assert.That(minimumBeforeMaximum.Fault?.Field, Is.EqualTo(ShapeValidationField.HalfExtentY));
        Assert.That(minimumBeforeMaximum.Fault?.Code, Is.EqualTo(ShapeValidationFaultCode.PrimitiveExtentBelowMinimum));

        var context3D = PrimitiveShapeGeometryContext3D.Default;
        var thirdMinimumBeforeMaximum3D = PrimitiveShapeValidator3D.Validate(
            new BoxShapeDefinition3D(new Vector3(
                float.BitIncrement(context3D.MaximumLocalExtent),
                float.BitIncrement(context3D.MaximumLocalExtent),
                float.BitDecrement(context3D.MinimumPrimitiveExtent))),
            context3D);
        Assert.That(thirdMinimumBeforeMaximum3D.Fault?.Code, Is.EqualTo(ShapeValidationFaultCode.PrimitiveExtentBelowMinimum));
        Assert.That(thirdMinimumBeforeMaximum3D.Fault?.Field, Is.EqualTo(ShapeValidationField.HalfExtentZ));

        var secondMaximum3D = PrimitiveShapeValidator3D.Validate(
            new BoxShapeDefinition3D(new Vector3(
                context3D.MaximumLocalExtent,
                float.BitIncrement(context3D.MaximumLocalExtent),
                float.BitIncrement(context3D.MaximumLocalExtent))),
            context3D);
        Assert.That(secondMaximum3D.Fault?.Code, Is.EqualTo(ShapeValidationFaultCode.LocalExtentExceeded));
        Assert.That(secondMaximum3D.Fault?.Field, Is.EqualTo(ShapeValidationField.HalfExtentY));

        var thirdMaximum3D = PrimitiveShapeValidator3D.Validate(
            new BoxShapeDefinition3D(new Vector3(
                context3D.MaximumLocalExtent,
                context3D.MaximumLocalExtent,
                float.BitIncrement(context3D.MaximumLocalExtent))),
            context3D);
        Assert.That(thirdMaximum3D.Fault?.Code, Is.EqualTo(ShapeValidationFaultCode.LocalExtentExceeded));
        Assert.That(thirdMaximum3D.Fault?.Field, Is.EqualTo(ShapeValidationField.HalfExtentZ));

        var corner3D = PrimitiveShapeValidator3D.Validate(
            new BoxShapeDefinition3D(new Vector3(64.0f, context2D.MinimumPrimitiveExtent, context2D.MinimumPrimitiveExtent)),
            PrimitiveShapeGeometryContext3D.Default);

        Assert.That(corner3D.Fault?.Field, Is.EqualTo(ShapeValidationField.EnclosingRadius));
        Assert.That(corner3D.Fault?.Code, Is.EqualTo(ShapeValidationFaultCode.LocalExtentExceeded));

        var aboveComponentLimit3D = PrimitiveShapeValidator3D.Validate(
            new BoxShapeDefinition3D(new Vector3(float.BitIncrement(context2D.MaximumLocalExtent), context2D.MinimumPrimitiveExtent, context2D.MinimumPrimitiveExtent)),
            PrimitiveShapeGeometryContext3D.Default);

        Assert.That(aboveComponentLimit3D.Fault?.Code, Is.EqualTo(ShapeValidationFaultCode.LocalExtentExceeded));
        Assert.That(aboveComponentLimit3D.Fault?.Field, Is.EqualTo(ShapeValidationField.HalfExtentX));

        var largeSettings = new SpatialSettings(-30, 12);
        var largeContext2D = new PrimitiveShapeGeometryContext2D(largeSettings);
        var largeContext3D = new PrimitiveShapeGeometryContext3D(largeSettings);
        Assert.That(float.IsPositiveInfinity(largeContext2D.MaximumLocalExtent * largeContext2D.MaximumLocalExtent), Is.True);
        Assert.That(float.IsPositiveInfinity(largeContext3D.MaximumLocalExtent * largeContext3D.MaximumLocalExtent), Is.True);

        var overflowingFloatSquare2D = PrimitiveShapeValidator2D.Validate(
            new BoxShapeDefinition2D(new Vector2(largeContext2D.MaximumLocalExtent, largeContext2D.MaximumLocalExtent)),
            largeContext2D);
        var overflowingFloatSquare3D = PrimitiveShapeValidator3D.Validate(
            new BoxShapeDefinition3D(new Vector3(largeContext3D.MaximumLocalExtent, largeContext3D.MaximumLocalExtent, largeContext3D.MaximumLocalExtent)),
            largeContext3D);
        var fault2D = overflowingFloatSquare2D.Fault ?? throw new InvalidOperationException("An oversized box must have a fault.");
        var fault3D = overflowingFloatSquare3D.Fault ?? throw new InvalidOperationException("An oversized box must have a fault.");

        Assert.That(fault2D.Field, Is.EqualTo(ShapeValidationField.EnclosingRadius));
        Assert.That(fault2D.ObservedValue, Is.GreaterThan(largeContext2D.MaximumLocalExtent));
        Assert.That(float.IsFinite(fault2D.ObservedValue), Is.True);
        AssertEnclosingRadius(
            new Vector2(largeContext2D.MaximumLocalExtent, largeContext2D.MaximumLocalExtent),
            fault2D.ObservedValue);
        Assert.That(fault3D.Field, Is.EqualTo(ShapeValidationField.EnclosingRadius));
        Assert.That(fault3D.ObservedValue, Is.GreaterThan(largeContext3D.MaximumLocalExtent));
        Assert.That(float.IsFinite(fault3D.ObservedValue), Is.True);
        AssertEnclosingRadius(
            new Vector3(largeContext3D.MaximumLocalExtent, largeContext3D.MaximumLocalExtent, largeContext3D.MaximumLocalExtent),
            fault3D.ObservedValue);

        var smallHalfExtent2D = largeContext2D.MaximumLocalExtent / 128.0f;
        var smallHalfExtent3D = largeContext3D.MaximumLocalExtent / 128.0f;
        var largeScaleSmall2D = PrimitiveShapeValidator2D.Validate(
            new BoxShapeDefinition2D(new Vector2(smallHalfExtent2D, smallHalfExtent2D)),
            largeContext2D);
        var largeScaleSmall3D = PrimitiveShapeValidator3D.Validate(
            new BoxShapeDefinition3D(new Vector3(smallHalfExtent3D, smallHalfExtent3D, smallHalfExtent3D)),
            largeContext3D);
        var smallStatistics2D = largeScaleSmall2D.Statistics ?? throw new InvalidOperationException("A bounded large-scale box must be valid.");
        var smallStatistics3D = largeScaleSmall3D.Statistics ?? throw new InvalidOperationException("A bounded large-scale box must be valid.");

        Assert.That(largeScaleSmall2D.Status, Is.EqualTo(ShapeValidationStatus.Valid));
        Assert.That(largeScaleSmall3D.Status, Is.EqualTo(ShapeValidationStatus.Valid));
        AssertEnclosingRadius(new Vector2(smallHalfExtent2D, smallHalfExtent2D), smallStatistics2D.EnclosingRadius);
        AssertEnclosingRadius(new Vector3(smallHalfExtent3D, smallHalfExtent3D, smallHalfExtent3D), smallStatistics3D.EnclosingRadius);
    }

    [Test]
    [Category("M03Primitive2D")]
    [Category("M03Primitive3D")]
    public void ContextsBindPublishedPhysicalLimits()
    {
        var context2D = PrimitiveShapeGeometryContext2D.Default;
        var context3D = PrimitiveShapeGeometryContext3D.Default;

        Assert.That(context2D.MinimumPrimitiveExtent, Is.EqualTo(SpatialMath.ConvertQuantity(1, 40, 0, context2D.SpatialSettings.MetersPerUnitExponent, 1)));
        Assert.That(context3D.MinimumPrimitiveExtent, Is.EqualTo(SpatialMath.ConvertQuantity(1, 40, 0, context3D.SpatialSettings.MetersPerUnitExponent, 1)));

        _ = Assert.Throws<ArgumentException>(() => new PrimitiveShapeGeometryContext2D(default));
        _ = Assert.Throws<ArgumentException>(() => new PrimitiveShapeGeometryContext3D(default));
    }

    [Test]
    [Category("M03Primitive2D")]
    [Category("M03Primitive3D")]
    public void FiniteDefinitionsCopyInputBitsAndRejectNonfiniteValues()
    {
        var signedZero2D = new BoxShapeDefinition2D(new Vector2(-0.0f, 0.0f));
        var signedZero3D = new BoxShapeDefinition3D(new Vector3(-0.0f, 0.0f, -0.0f));
        var smallestSubnormal = BitConverter.UInt32BitsToSingle(1);
        var largestFinite = BitConverter.UInt32BitsToSingle(0x7F7F_FFFF);
        var copiedCircle = new CircleShapeDefinition2D(smallestSubnormal);
        var copiedBox2D = new BoxShapeDefinition2D(new Vector2(smallestSubnormal, largestFinite));
        var copiedSphere = new SphereShapeDefinition3D(largestFinite);
        var copiedBox3D = new BoxShapeDefinition3D(new Vector3(smallestSubnormal, largestFinite, smallestSubnormal));

        Assert.That(BitConverter.SingleToInt32Bits(signedZero2D.HalfExtents.X), Is.EqualTo(int.MinValue));
        Assert.That(BitConverter.SingleToInt32Bits(signedZero2D.HalfExtents.Y), Is.Zero);
        Assert.That(BitConverter.SingleToInt32Bits(signedZero3D.HalfExtents.X), Is.EqualTo(int.MinValue));
        Assert.That(BitConverter.SingleToInt32Bits(signedZero3D.HalfExtents.Z), Is.EqualTo(int.MinValue));
        Assert.That(BitConverter.SingleToInt32Bits(new CircleShapeDefinition2D(-0.0f).Radius), Is.EqualTo(int.MinValue));
        Assert.That(BitConverter.SingleToInt32Bits(new SphereShapeDefinition3D(-0.0f).Radius), Is.EqualTo(int.MinValue));
        Assert.That(BitConverter.SingleToInt32Bits(copiedCircle.Radius), Is.EqualTo(1));
        Assert.That(BitConverter.SingleToInt32Bits(copiedBox2D.HalfExtents.X), Is.EqualTo(1));
        Assert.That(unchecked((uint)BitConverter.SingleToInt32Bits(copiedBox2D.HalfExtents.Y)), Is.EqualTo(0x7F7F_FFFF));
        Assert.That(unchecked((uint)BitConverter.SingleToInt32Bits(copiedSphere.Radius)), Is.EqualTo(0x7F7F_FFFF));
        Assert.That(BitConverter.SingleToInt32Bits(copiedBox3D.HalfExtents.X), Is.EqualTo(1));
        Assert.That(unchecked((uint)BitConverter.SingleToInt32Bits(copiedBox3D.HalfExtents.Y)), Is.EqualTo(0x7F7F_FFFF));
        Assert.That(BitConverter.SingleToInt32Bits(copiedBox3D.HalfExtents.Z), Is.EqualTo(1));

        _ = Assert.Throws<ArgumentException>(() => new CircleShapeDefinition2D(float.NaN));
        _ = Assert.Throws<ArgumentException>(() => new SphereShapeDefinition3D(float.PositiveInfinity));
        _ = Assert.Throws<ArgumentException>(() => new BoxShapeDefinition2D(new Vector2(1.0f, float.NegativeInfinity)));
        _ = Assert.Throws<ArgumentException>(() => new BoxShapeDefinition3D(new Vector3(float.NaN, 1.0f, 1.0f)));

        _ = Assert.Throws<ArgumentNullException>(() => PrimitiveShapeValidator2D.Validate((BoxShapeDefinition2D)null!, PrimitiveShapeGeometryContext2D.Default));
        _ = Assert.Throws<ArgumentNullException>(() => PrimitiveShapeValidator2D.Validate((CircleShapeDefinition2D)null!, PrimitiveShapeGeometryContext2D.Default));
        _ = Assert.Throws<ArgumentNullException>(() => PrimitiveShapeValidator2D.Validate(new CircleShapeDefinition2D(1.0f), null!));
        _ = Assert.Throws<ArgumentNullException>(() => PrimitiveShapeValidator3D.Validate((BoxShapeDefinition3D)null!, PrimitiveShapeGeometryContext3D.Default));
        _ = Assert.Throws<ArgumentNullException>(() => PrimitiveShapeValidator3D.Validate((SphereShapeDefinition3D)null!, PrimitiveShapeGeometryContext3D.Default));
        _ = Assert.Throws<ArgumentNullException>(() => PrimitiveShapeValidator3D.Validate(new SphereShapeDefinition3D(1.0f), null!));

        Assert.That(
            PrimitiveShapeValidator2D.Validate(new CircleShapeDefinition2D(-1.0f), PrimitiveShapeGeometryContext2D.Default).Fault?.Code,
            Is.EqualTo(ShapeValidationFaultCode.PrimitiveExtentBelowMinimum));
        Assert.That(
            PrimitiveShapeValidator3D.Validate(new SphereShapeDefinition3D(-0.0f), PrimitiveShapeGeometryContext3D.Default).Fault?.Field,
            Is.EqualTo(ShapeValidationField.Radius));
        Assert.That(
            PrimitiveShapeValidator2D.Validate(new BoxShapeDefinition2D(new Vector2(-1.0f, 1.0f)), PrimitiveShapeGeometryContext2D.Default).Fault?.Field,
            Is.EqualTo(ShapeValidationField.HalfExtentX));
    }

    [Test]
    [Category("M03Primitive2D")]
    [Category("M03Primitive3D")]
    public void MeasureNormalizationRoundsTiesToEvenAndCarries()
    {
        var oddTieX = BitConverter.UInt32BitsToSingle(0x3F80_0001);
        var evenTieX = BitConverter.UInt32BitsToSingle(0x3F80_0003);
        var oddTie = PrimitiveShapeValidator2D.Validate(
            new BoxShapeDefinition2D(new Vector2(oddTieX, 1.5f)),
            PrimitiveShapeGeometryContext2D.Default);
        var evenTie = PrimitiveShapeValidator2D.Validate(
            new BoxShapeDefinition2D(new Vector2(evenTieX, 1.5f)),
            PrimitiveShapeGeometryContext2D.Default);
        var oddTieMeasure = (oddTie.Statistics ?? throw new InvalidOperationException("Valid geometry must provide statistics.")).SignedArea;
        var evenTieMeasure = (evenTie.Statistics ?? throw new InvalidOperationException("Valid geometry must provide statistics.")).SignedArea;

        Assert.That(BitConverter.SingleToInt32Bits(oddTieMeasure.Significand), Is.EqualTo(0x3FC0_0002));
        Assert.That(oddTieMeasure.BinaryExponent, Is.EqualTo(2));
        Assert.That(BitConverter.SingleToInt32Bits(evenTieMeasure.Significand), Is.EqualTo(0x3FC0_0004));
        Assert.That(evenTieMeasure.BinaryExponent, Is.EqualTo(2));

        var carryRadius = BitConverter.UInt32BitsToSingle(0x3F9E_CF4A);
        var carryContext = new PrimitiveShapeGeometryContext3D(SpatialSettings.Default);
        var carryResponse = PrimitiveShapeValidator3D.Validate(new SphereShapeDefinition3D(carryRadius), carryContext);
        var carryMeasure = (carryResponse.Statistics ?? throw new InvalidOperationException("Valid geometry must provide statistics.")).SignedVolume;

        Assert.That(BitConverter.SingleToInt32Bits(carryMeasure.Significand), Is.EqualTo(0x3F80_0000));
        Assert.That(carryMeasure.BinaryExponent, Is.EqualTo(3));
    }

    [Test]
    [Category("M03Primitive2D")]
    public void Primitive2DBoundaryObservationCoversSelectedUnitExponents()
    {
        AssertPrimitive2DBoundaries();
    }

    [Test]
    [Category("M03Primitive3D")]
    public void Primitive3DBoundaryObservationCoversSelectedUnitExponents()
    {
        AssertPrimitive3DBoundaries();
    }

    [Test]
    [Category("M03Primitive2D")]
    [Category("M03Primitive3D")]
    public void ScaledPrimitiveStatisticsUseExactFormulaAndDimensionSpecificTopology()
    {
        var circle = PrimitiveShapeValidator2D.Validate(
            new CircleShapeDefinition2D(2.0f),
            PrimitiveShapeGeometryContext2D.Default);
        var box2D = PrimitiveShapeValidator2D.Validate(
            new BoxShapeDefinition2D(new Vector2(3.0f, 4.0f)),
            PrimitiveShapeGeometryContext2D.Default);
        var sphere = PrimitiveShapeValidator3D.Validate(
            new SphereShapeDefinition3D(2.0f),
            PrimitiveShapeGeometryContext3D.Default);
        var box3D = PrimitiveShapeValidator3D.Validate(
            new BoxShapeDefinition3D(new Vector3(3.0f, 4.0f, 12.0f)),
            PrimitiveShapeGeometryContext3D.Default);

        Assert.That(circle.Status, Is.EqualTo(ShapeValidationStatus.Valid));
        Assert.That(circle.Statistics?.VertexCount, Is.Null);
        Assert.That(circle.Statistics?.EdgeCount, Is.Null);
        Assert.That(circle.Statistics?.FaceCount, Is.Null);
        Assert.That(circle.Statistics?.ConnectedComponentCount, Is.EqualTo(1));
        Assert.That(circle.Statistics?.SignedArea.Significand, Is.EqualTo(BitConverter.UInt32BitsToSingle(0x3FC9_0FDB)));
        Assert.That(circle.Statistics?.SignedArea.BinaryExponent, Is.EqualTo(3));
        var circleStatistics = circle.Statistics ?? throw new InvalidOperationException("Valid geometry must provide statistics.");
        Assert.That(circleStatistics.SpatialSettings, Is.EqualTo(PrimitiveShapeGeometryContext2D.Default.SpatialSettings));
        Assert.That(circleStatistics.SpatialSettings, Is.EqualTo(SpatialSettings.Default));
        Assert.That(circleStatistics.LocalMinimum, Is.EqualTo(new Vector2(-2.0f, -2.0f)));
        Assert.That(circleStatistics.LocalMaximum, Is.EqualTo(new Vector2(2.0f, 2.0f)));
        Assert.That(circleStatistics.EnclosingRadius, Is.EqualTo(2.0f));
        Assert.That(circleStatistics.WeldedVertexCount, Is.Null);
        Assert.That(circleStatistics.PatchCount, Is.Null);
        Assert.That(circleStatistics.MaximumSnapDisplacement, Is.Null);

        Assert.That(box2D.Statistics?.VertexCount, Is.EqualTo(4));
        Assert.That(box2D.Statistics?.EdgeCount, Is.EqualTo(4));
        Assert.That(box2D.Statistics?.FaceCount, Is.Null);
        Assert.That(box2D.Statistics?.SignedArea.Significand, Is.EqualTo(1.5f));
        Assert.That(box2D.Statistics?.SignedArea.BinaryExponent, Is.EqualTo(5));
        Assert.That(box2D.Statistics?.EnclosingRadius, Is.EqualTo(5.0f));
        var boxStatistics2D = box2D.Statistics ?? throw new InvalidOperationException("Valid geometry must provide statistics.");
        Assert.That(boxStatistics2D.LocalMinimum, Is.EqualTo(new Vector2(-3.0f, -4.0f)));
        Assert.That(boxStatistics2D.LocalMaximum, Is.EqualTo(new Vector2(3.0f, 4.0f)));
        Assert.That(boxStatistics2D.ConnectedComponentCount, Is.EqualTo(1));
        Assert.That(boxStatistics2D.WeldedVertexCount, Is.Null);
        Assert.That(boxStatistics2D.PatchCount, Is.Null);
        Assert.That(boxStatistics2D.MaximumSnapDisplacement, Is.Null);

        Assert.That(sphere.Statistics?.VertexCount, Is.Null);
        Assert.That(sphere.Statistics?.EdgeCount, Is.Null);
        Assert.That(sphere.Statistics?.FaceCount, Is.Null);
        Assert.That(sphere.Statistics?.SignedVolume.Significand, Is.EqualTo(1.04719758f));
        Assert.That(sphere.Statistics?.SignedVolume.BinaryExponent, Is.EqualTo(5));
        var sphereStatistics = sphere.Statistics ?? throw new InvalidOperationException("Valid geometry must provide statistics.");
        Assert.That(sphereStatistics.LocalMinimum, Is.EqualTo(new Vector3(-2.0f, -2.0f, -2.0f)));
        Assert.That(sphereStatistics.LocalMaximum, Is.EqualTo(new Vector3(2.0f, 2.0f, 2.0f)));
        Assert.That(sphereStatistics.EnclosingRadius, Is.EqualTo(2.0f));
        Assert.That(sphereStatistics.ConnectedComponentCount, Is.EqualTo(1));
        Assert.That(sphereStatistics.WeldedVertexCount, Is.Null);
        Assert.That(sphereStatistics.PatchCount, Is.Null);
        Assert.That(sphereStatistics.MaximumSnapDisplacement, Is.Null);

        Assert.That(box3D.Statistics?.VertexCount, Is.EqualTo(8));
        Assert.That(box3D.Statistics?.EdgeCount, Is.EqualTo(12));
        Assert.That(box3D.Statistics?.FaceCount, Is.EqualTo(6));
        Assert.That(box3D.Statistics?.SignedVolume.Significand, Is.EqualTo(1.125f));
        Assert.That(box3D.Statistics?.SignedVolume.BinaryExponent, Is.EqualTo(10));
        Assert.That(box3D.Statistics?.EnclosingRadius, Is.EqualTo(13.0f));
        Assert.That(box3D.Statistics?.WeldedVertexCount, Is.Null);
        Assert.That(box3D.Statistics?.PatchCount, Is.Null);
        Assert.That(box3D.Statistics?.MaximumSnapDisplacement, Is.Null);
        Assert.That(box3D.Statistics?.SpatialSettings, Is.EqualTo(PrimitiveShapeGeometryContext3D.Default.SpatialSettings));
        Assert.That(box3D.Statistics?.SpatialSettings, Is.EqualTo(SpatialSettings.Default));
        var boxStatistics3D = box3D.Statistics ?? throw new InvalidOperationException("Valid geometry must provide statistics.");

        AssertMeasure(circleStatistics.SignedArea, (0x3FC9_0FDB, 3));
        AssertMeasure(boxStatistics2D.SignedArea, (0x3FC0_0000, 5));
        AssertMeasure(sphereStatistics.SignedVolume, (0x3F86_0A92, 5));
        AssertMeasure(boxStatistics3D.SignedVolume, (0x3F90_0000, 10));
        AssertMaterializes(circleStatistics.SignedArea, 0x4149_0FDB);
        AssertMaterializes(boxStatistics2D.SignedArea, 0x4240_0000);
        AssertMaterializes(sphereStatistics.SignedVolume, 0x4206_0A92);
        AssertMaterializes(boxStatistics3D.SignedVolume, 0x4490_0000);
    }

    [Test]
    [Category("M03Primitive2D")]
    [Category("M03Primitive3D")]
    public void ScaledSphereMeasureAndStoredPairConversionPreserveBothRoundingBoundaries()
    {
        const uint RadiusBits = 0x28E1_2FB1;
        var radius = BitConverter.UInt32BitsToSingle(RadiusBits);
        var context = new PrimitiveShapeGeometryContext3D(new SpatialSettings(12, 12));
        var response = PrimitiveShapeValidator3D.Validate(new SphereShapeDefinition3D(radius), context);
        var statistics = response.Statistics ?? throw new InvalidOperationException("Valid geometry must provide statistics.");
        var measure = statistics.SignedVolume;
        var zeroMeasure = default(ShapeMeasure);

        Assert.That(response.Status, Is.EqualTo(ShapeValidationStatus.Valid));
        Assert.That(radius, Is.GreaterThanOrEqualTo(context.MinimumPrimitiveExtent));
        Assert.That(zeroMeasure.TryGetSingle(out var zero), Is.True);
        Assert.That(BitConverter.SingleToInt32Bits(zero), Is.Zero);
        Assert.That(BitConverter.SingleToInt32Bits(measure.Significand), Is.EqualTo(0x3FB6_7680));
        Assert.That(measure.BinaryExponent, Is.EqualTo(-134));
        Assert.That(measure.TryGetSingle(out var storedValue), Is.True);
        Assert.That(BitConverter.SingleToInt32Bits(storedValue), Is.EqualTo(0x0000_B676));

        const uint DirectFormulaBits = 0x0000_B677;
        const uint OneMeterRadiusBits = 0x56B5_E621;
        Assert.That(DirectFormulaBits, Is.Not.EqualTo(BitConverter.SingleToUInt32Bits(storedValue)));
        var oneMeterRadius = BitConverter.UInt32BitsToSingle(OneMeterRadiusBits);
        var largeMeasureContext = new PrimitiveShapeGeometryContext3D(new SpatialSettings(-14, 12));
        var largeMeasure = PrimitiveShapeValidator3D.Validate(new SphereShapeDefinition3D(oneMeterRadius), largeMeasureContext);
        var largeStatistics = largeMeasure.Statistics ?? throw new InvalidOperationException("A one-meter sphere in fine units must remain valid.");

        Assert.That(BitConverter.SingleToUInt32Bits(SpatialMath.ConvertQuantity(1, 0, -14, 1)), Is.EqualTo(OneMeterRadiusBits));
        Assert.That(largeMeasure.Status, Is.EqualTo(ShapeValidationStatus.Valid));
        Assert.That(BitConverter.SingleToInt32Bits(largeStatistics.SignedVolume.Significand), Is.EqualTo(0x3FC0_56FF));
        Assert.That(largeStatistics.SignedVolume.BinaryExponent, Is.EqualTo(141));
        Assert.That(largeStatistics.SignedVolume.TryGetSingle(out var largeSingle), Is.False);
        Assert.That(BitConverter.SingleToInt32Bits(largeSingle), Is.Zero);

        foreach (var exponent in new[] { -30, 30 })
        {
            var extremeContext = new PrimitiveShapeGeometryContext3D(new SpatialSettings(exponent, 12));
            var extreme = PrimitiveShapeValidator3D.Validate(
                new SphereShapeDefinition3D(extremeContext.MinimumPrimitiveExtent),
                extremeContext);
            var extremeBox = PrimitiveShapeValidator3D.Validate(
                new BoxShapeDefinition3D(new Vector3(
                    extremeContext.MinimumPrimitiveExtent,
                    extremeContext.MinimumPrimitiveExtent,
                    extremeContext.MinimumPrimitiveExtent)),
                extremeContext);
            var extremeStatistics = extreme.Statistics ?? throw new InvalidOperationException("The published minimum sphere must be valid.");
            var extremeBoxStatistics = extremeBox.Statistics ?? throw new InvalidOperationException("The published minimum 3D box must be valid.");
            var measureExponent = extremeStatistics.SignedVolume.BinaryExponent;

            Assert.That(extreme.Status, Is.EqualTo(ShapeValidationStatus.Valid));
            Assert.That(extremeStatistics.SignedVolume.Significand, Is.GreaterThanOrEqualTo(1.0f));
            Assert.That(extremeStatistics.SignedVolume.Significand, Is.LessThan(2.0f));
            Assert.That(measureExponent, exponent == -30 ? Is.GreaterThan(127) : Is.LessThan(-149));
            Assert.That(extremeStatistics.SignedVolume.TryGetSingle(out var extremeSingle), Is.False);
            Assert.That(BitConverter.SingleToInt32Bits(extremeSingle), Is.Zero);
            Assert.That(extremeBox.Status, Is.EqualTo(ShapeValidationStatus.Valid));
            Assert.That(extremeBoxStatistics.SignedVolume.Significand, Is.GreaterThanOrEqualTo(1.0f));
            Assert.That(extremeBoxStatistics.SignedVolume.Significand, Is.LessThan(2.0f));
            Assert.That(extremeBoxStatistics.SignedVolume.BinaryExponent, Is.InRange(-512, 512));
            Assert.That(extremeBoxStatistics.SignedVolume.TryGetSingle(out var extremeBoxSingle), Is.False);
            Assert.That(BitConverter.SingleToInt32Bits(extremeBoxSingle), Is.Zero);
        }

        foreach (var exponent in new[] { -30, 30 })
        {
            var extremeContext = new PrimitiveShapeGeometryContext2D(new SpatialSettings(exponent, 12));
            var extreme = PrimitiveShapeValidator2D.Validate(
                new CircleShapeDefinition2D(extremeContext.MinimumPrimitiveExtent),
                extremeContext);
            var extremeBox = PrimitiveShapeValidator2D.Validate(
                new BoxShapeDefinition2D(new Vector2(
                    extremeContext.MinimumPrimitiveExtent,
                    extremeContext.MinimumPrimitiveExtent)),
                extremeContext);
            var extremeStatistics = extreme.Statistics ?? throw new InvalidOperationException("Valid geometry must provide statistics.");
            var extremeBoxStatistics = extremeBox.Statistics ?? throw new InvalidOperationException("The published minimum 2D box must be valid.");

            Assert.That(extreme.Status, Is.EqualTo(ShapeValidationStatus.Valid));
            Assert.That(extremeStatistics.SignedArea.Significand, Is.GreaterThanOrEqualTo(1.0f));
            Assert.That(extremeStatistics.SignedArea.Significand, Is.LessThan(2.0f));
            Assert.That(extremeStatistics.SignedArea.BinaryExponent, Is.InRange(-512, 512));
            Assert.That(extremeStatistics.SignedArea.TryGetSingle(out var single), Is.False);
            Assert.That(BitConverter.SingleToInt32Bits(single), Is.Zero);
            Assert.That(extremeBox.Status, Is.EqualTo(ShapeValidationStatus.Valid));
            Assert.That(extremeBoxStatistics.SignedArea.Significand, Is.GreaterThanOrEqualTo(1.0f));
            Assert.That(extremeBoxStatistics.SignedArea.Significand, Is.LessThan(2.0f));
            Assert.That(extremeBoxStatistics.SignedArea.BinaryExponent, Is.InRange(-512, 512));
            Assert.That(extremeBoxStatistics.SignedArea.TryGetSingle(out var extremeBoxSingle), Is.False);
            Assert.That(BitConverter.SingleToInt32Bits(extremeBoxSingle), Is.Zero);
        }
    }
}
