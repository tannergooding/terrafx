// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Numerics;
using System.Runtime.InteropServices;
using NUnit.Framework;
using TerraFX.Numerics;
using Matrix3x2 = TerraFX.Numerics.Matrix3x2;
using Matrix4x4 = TerraFX.Numerics.Matrix4x4;
using Quaternion = TerraFX.Numerics.Quaternion;
using Vector2 = TerraFX.Numerics.Vector2;
using Vector3 = TerraFX.Numerics.Vector3;
using Vector4 = TerraFX.Numerics.Vector4;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class GeometryArithmeticTests
{
    private static void AssertEquality<T>(T left, T right)
        where T : struct, IEquatable<T>, IEqualityOperators<T, T, bool>
    {
        Assert.That(left.Equals(right), Is.True);
        Assert.That(left.Equals((object)right), Is.True);
        Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
        Assert.That(GeometryPublicConsumer.Equal(left, right), Is.False);
        Assert.That(GeometryPublicConsumer.Equal(left, left), Is.False);
        Assert.That(left != right, Is.True);
    }

    private static void AssertMask<T>(T mask, int count, int expected)
        where T : unmanaged
    {
        var bits = MemoryMarshal.Cast<byte, int>(MemoryMarshal.AsBytes(new[] { mask }.AsSpan())).ToArray();
        Assert.That(bits.Length, Is.EqualTo(count));
        Assert.That(bits, Is.All.EqualTo(expected));
    }

    private static void AssertVectorNear(Vector3 actual, Vector3 expected)
    {
        Assert.That(actual.X, Is.EqualTo(expected.X).Within(2.0e-6f));
        Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(2.0e-6f));
        Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(2.0e-6f));
    }

    [TestCase(AngleUnit.Degrees, 90.0f)]
    [TestCase(AngleUnit.Turns, 0.25f)]
    [TestCase(AngleUnit.Degrees, 450.0f)]
    [TestCase(AngleUnit.Turns, 1.25f)]
    [TestCase(AngleUnit.Radians, float.Pi * 0.5f)]
    public void AllDefinitionAwareRotationFactoriesPreserveAxisAndApplicationSigns(AngleUnit unit, float value)
    {
        var angle = new Angle(value, unit);

        var rotated = Vector2.TransformDirection(Vector2.UnitX, Matrix3x2.CreateRotation(angle));
        Assert.That(rotated.X, Is.EqualTo(0).Within(2.0e-6f));
        Assert.That(rotated.Y, Is.EqualTo(1).Within(2.0e-6f));
        AssertVectorNear(Vector3.TransformDirection(Vector3.UnitY, Matrix4x4.CreateRotationX(angle)), Vector3.UnitZ);
        AssertVectorNear(Vector3.TransformDirection(Vector3.UnitZ, Matrix4x4.CreateRotationY(angle)), Vector3.UnitX);
        AssertVectorNear(Vector3.TransformDirection(Vector3.UnitX, Matrix4x4.CreateRotationZ(angle)), Vector3.UnitY);
        AssertVectorNear(Vector3.TransformDirection(Vector3.UnitX, Matrix4x4.CreateFromAxisAngle(Vector3.UnitZ, angle)), Vector3.UnitY);
        AssertVectorNear(Vector3.Rotate(Vector3.UnitX, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle)), Vector3.UnitY);
        Assert.That(angle.Value, Is.EqualTo(value));
        Assert.That(angle.Unit, Is.EqualTo(unit));
    }

    [Test]
    public void EqualityAndHashingUseFloatEqualsWhileOperatorsUseIeeeComparison()
    {
        var nan1 = BitConverter.Int32BitsToSingle(0x7FC1_2345);
        var nan2 = BitConverter.Int32BitsToSingle(unchecked((int)0xFF85_4321));
        AssertEquality(new Vector2(nan1, 0), new Vector2(nan2, -0.0f));
        AssertEquality(new Vector3(nan1, 0, 1), new Vector3(nan2, -0.0f, 1));
        AssertEquality(new Vector4(nan1, 0, 1, 2), new Vector4(nan2, -0.0f, 1, 2));
        AssertEquality(new Quaternion(nan1, 0, 1, 2), new Quaternion(nan2, -0.0f, 1, 2));
        AssertEquality(new Matrix3x2(new Vector2(nan1), Vector2.Zero, Vector2.One), new Matrix3x2(new Vector2(nan2), new Vector2(-0.0f), Vector2.One));
        AssertEquality(new Matrix4x4(new Vector4(nan1), Vector4.Zero, Vector4.One, Vector4.UnitW), new Matrix4x4(new Vector4(nan2), new Vector4(-0.0f), Vector4.One, Vector4.UnitW));
        Assert.That(Vector3.Zero == new Vector3(-0.0f), Is.True);
        Assert.That(Vector3.Zero.GetHashCode(), Is.EqualTo(new Vector3(-0.0f).GetHashCode()));
        Assert.That(Quaternion.Identity == -Quaternion.Identity, Is.False);
        Assert.That(Quaternion.Identity.Equals(-Quaternion.Identity), Is.False);
        Assert.That(Vector4.One.Equals(null), Is.False);
    }

    [TestCase(0x0000_0000, true)]
    [TestCase(unchecked((int)0x8000_0000), true)]
    [TestCase(0x0000_0001, true)]
    [TestCase(0x007F_FFFF, true)]
    [TestCase(0x7F7F_FFFF, true)]
    [TestCase(unchecked((int)0xFF7F_FFFF), true)]
    [TestCase(0x7F80_0000, false)]
    [TestCase(unchecked((int)0xFF80_0000), false)]
    [TestCase(0x7FC1_2345, false)]
    [TestCase(unchecked((int)0xFF81_2345), false)]
    public void FiniteClassificationPreservesShapeAndExactMaskBits(int bits, bool finite)
    {
        var component = BitConverter.Int32BitsToSingle(bits);
        var expected = finite ? -1 : 0;
        var v2 = new Vector2(component);
        var v3 = new Vector3(component);
        var v4 = new Vector4(component);
        var q = new Quaternion(component, component, component, component);
        var m32 = new Matrix3x2(v2, v2, v2);
        var m44 = new Matrix4x4(v4, v4, v4, v4);

        AssertMask(v2.IsFinite(), 2, expected);
        AssertMask(v3.IsFinite(), 3, expected);
        AssertMask(v4.IsFinite(), 4, expected);
        AssertMask(q.IsFinite(), 4, expected);
        AssertMask(m32.IsFinite(), 6, expected);
        AssertMask(m44.IsFinite(), 16, expected);
        Assert.That(BitConverter.SingleToInt32Bits(v2.X), Is.EqualTo(bits));
        Assert.That(BitConverter.SingleToInt32Bits(v3.Z), Is.EqualTo(bits));
        Assert.That(BitConverter.SingleToInt32Bits(v4.W), Is.EqualTo(bits));
        Assert.That(BitConverter.SingleToInt32Bits(q.W), Is.EqualTo(bits));
        Assert.That(BitConverter.SingleToInt32Bits(m32[2, 1]), Is.EqualTo(bits));
        Assert.That(BitConverter.SingleToInt32Bits(m44[3, 3]), Is.EqualTo(bits));
    }

    [Test]
    public void HamiltonComposeConjugationAndShortestArcRetainTheirContracts()
    {
        var first = Quaternion.CreateFromAxisAngle(Vector3.UnitX, Angle.FromDegrees(90));
        var second = Quaternion.CreateFromAxisAngle(Vector3.UnitY, Angle.FromDegrees(90));
        var composed = Quaternion.Compose(first, second);

        Assert.That(composed, Is.EqualTo(Quaternion.Multiply(second, first)));
        Assert.That(composed, Is.Not.EqualTo(first * second));
        AssertVectorNear(Vector3.Rotate(Vector3.UnitY, composed), Vector3.Rotate(Vector3.Rotate(Vector3.UnitY, first), second));
        AssertVectorNear(Vector3.TransformDirection(Vector3.UnitY, Matrix4x4.CreateFromQuaternion(first) * Matrix4x4.CreateFromQuaternion(second)), Vector3.Rotate(Vector3.UnitY, composed));

        var raw = new Quaternion(-0.0f, 2, -3, -0.0f);
        var conjugate = Quaternion.Conjugate(raw);
        Assert.That(BitConverter.SingleToInt32Bits(conjugate.X), Is.Zero);
        Assert.That(conjugate.Y, Is.EqualTo(-2));
        Assert.That(conjugate.Z, Is.EqualTo(3));
        Assert.That(BitConverter.SingleToInt32Bits(conjugate.W), Is.EqualTo(int.MinValue));
        Assert.That(float.Abs(Quaternion.Dot(Quaternion.Lerp(first, -first, 0.5f), first)), Is.EqualTo(1).Within(2.0e-6f));
        Assert.That(float.Abs(Quaternion.Dot(Quaternion.Slerp(first, -first, 0.5f), first)), Is.EqualTo(1).Within(2.0e-6f));
        Assert.That(Vector3.Rotate(Vector3.One, default), Is.EqualTo(Vector3.One));
        Assert.That(Matrix4x4.CreateFromQuaternion(default), Is.EqualTo(Matrix4x4.Identity));
        Assert.That(Vector3.Rotate(Vector3.UnitX, new Quaternion(0, 0, 2, 0)), Is.EqualTo(new Vector3(-7, 0, 0)));
    }

    [TestCase(-1, true)]
    [TestCase(0x7FFF_FFFF, false)]
    [TestCase(unchecked((int)0xFFFF_FFFE), false)]
    [TestCase(unchecked((int)0x8000_0000), false)]
    [TestCase(0x3F80_0000, false)]
    public void MaskReductionsRequireAllThirtyTwoBitsRatherThanNaNOrSignTruthiness(int bits, bool match)
    {
        var value = BitConverter.Int32BitsToSingle(bits);
        var v2 = new Vector2(value);
        var v3 = new Vector3(value);
        var v4 = new Vector4(value);
        var m32 = new Matrix3x2(v2, v2, v2);
        var m44 = new Matrix4x4(v4, v4, v4, v4);

        Assert.That(new[] { v2.All(), v3.All(), v4.All(), m32.All(), m44.All() }, Is.All.EqualTo(match));
        Assert.That(new[] { v2.Any(), v3.Any(), v4.Any(), m32.Any(), m44.Any() }, Is.All.EqualTo(match));
        Assert.That(new[] { v2.None(), v3.None(), v4.None(), m32.None(), m44.None() }, Is.All.EqualTo(!match));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void MaskReductionsUseOnlyLogicalLanes(int lane)
    {
        var mask = BitConverter.UInt32BitsToSingle(uint.MaxValue);
        var v4 = new Vector4(lane == 0 ? mask : 0.0f, lane == 1 ? mask : 0.0f, lane == 2 ? mask : 0.0f, lane == 3 ? mask : 0.0f);
        var v2 = new Vector2(v4.X, v4.Y);
        var v3 = new Vector3(v4.X, v4.Y, v4.Z);

        Assert.That(v2.All(), Is.False);
        Assert.That(v3.All(), Is.False);
        Assert.That(v4.All(), Is.False);
        Assert.That(v2.Any(), Is.EqualTo(lane < 2));
        Assert.That(v3.Any(), Is.EqualTo(lane < 3));
        Assert.That(v4.Any(), Is.True);
        Assert.That(v2.None(), Is.EqualTo(lane >= 2));
        Assert.That(v3.None(), Is.EqualTo(lane >= 3));
        Assert.That(v4.None(), Is.False);
    }

    [Test]
    public void MixedClassificationRetainsEachVectorQuaternionAndMatrixLane()
    {
        var vector = new Vector4(float.Epsilon, float.PositiveInfinity, -0.0f, BitConverter.Int32BitsToSingle(0x7F81_2345));
        var mask = vector.IsFinite();
        Assert.That(BitConverter.SingleToInt32Bits(mask.X), Is.EqualTo(-1));
        Assert.That(BitConverter.SingleToInt32Bits(mask.Y), Is.Zero);
        Assert.That(BitConverter.SingleToInt32Bits(mask.Z), Is.EqualTo(-1));
        Assert.That(BitConverter.SingleToInt32Bits(mask.W), Is.Zero);

        var quaternion = new Quaternion(vector.X, vector.Y, vector.Z, vector.W);
        Assert.That(quaternion.IsFinite(), Is.EqualTo(mask));

        var matrix = new Matrix4x4(Vector4.One, vector, Vector4.One, vector).IsFinite();

        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                var expected = row % 2 == 0 || column % 2 == 0 ? -1 : 0;
                Assert.That(BitConverter.SingleToInt32Bits(matrix[row, column]), Is.EqualTo(expected));
            }
        }

        var affine = new Matrix3x2(Vector2.One, new Vector2(float.NaN, 0), new Vector2(0, float.NegativeInfinity)).IsFinite();
        int[] expectedAffine = [-1, -1, 0, -1, -1, 0];
        Assert.That(MemoryMarshal.Cast<byte, int>(MemoryMarshal.AsBytes(new[] { affine }.AsSpan())).ToArray(), Is.EqualTo(expectedAffine));
    }

    [Test]
    public void QuaternionAngleHalvingOccursBeforeSubnormalRoundingAndReduction()
    {
        var tiny = Quaternion.CreateFromAxisAngle(Vector3.UnitX, Angle.FromTurns(float.Epsilon));
        Assert.That(BitConverter.SingleToInt32Bits(tiny.X), Is.EqualTo(3));
        Assert.That(tiny.W, Is.EqualTo(1));

        var fullTurn = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Angle.FromTurns(1));
        Assert.That(fullTurn.W, Is.EqualTo(-1));
        Assert.That(fullTurn.Z, Is.Zero);
        Assert.That(Quaternion.CreateFromAxisAngle(Vector3.UnitX, Angle.FromDegrees(float.MaxValue)).IsFinite().All(), Is.True);
        Assert.That(Matrix3x2.CreateRotation(Angle.FromDegrees(float.MaxValue)), Is.EqualTo(Matrix3x2.Identity));
        Assert.That(Matrix4x4.CreateRotationZ(Angle.FromDegrees(float.MaxValue)), Is.EqualTo(Matrix4x4.Identity));
    }

    [Test]
    public void ReductionsCoverEveryLogicalLaneAndScalarOverloadsUseIeeeEquality()
    {
        var mask = BitConverter.Int32BitsToSingle(-1);

        for (var lane = 0; lane < 16; lane++)
        {
            var components = new float[16];
            components[lane] = mask;
            var matrix = MemoryMarshal.Read<Matrix4x4>(MemoryMarshal.AsBytes(components.AsSpan()));
            Assert.That(matrix.Any(), Is.True);
            Assert.That(matrix.All(), Is.False);
            Assert.That(matrix.None(), Is.False);
            Assert.That(matrix.IsFinite().All(), Is.False);

            if (lane < 6)
            {
                var affine = MemoryMarshal.Read<Matrix3x2>(MemoryMarshal.AsBytes(components.AsSpan()));
                Assert.That(affine.Any(), Is.True);
                Assert.That(affine.All(), Is.False);
                Assert.That(affine.None(), Is.False);
            }

            if (lane < 3)
            {
                var vector = MemoryMarshal.Read<Vector3>(MemoryMarshal.AsBytes(components.AsSpan()));
                Assert.That(vector.Any(), Is.True);
                Assert.That(vector.All(), Is.False);
                Assert.That(vector.None(), Is.False);
            }
        }

        Assert.That(Vector3.One.All(), Is.False);
        Assert.That(Vector3.One.All(1.0f), Is.True);
        Assert.That(Vector2.One.None(), Is.True);
        Assert.That(new Vector2(0.0f, -0.0f).All(-0.0f), Is.True);
        Assert.That(new Vector3(float.NaN).Any(float.NaN), Is.False);
        Assert.That(new Vector4(float.PositiveInfinity).All(float.PositiveInfinity), Is.True);
        Assert.That(new Vector4(float.PositiveInfinity).None(float.NegativeInfinity), Is.True);
        Assert.That(new Matrix3x2(new Vector2(-0.0f), Vector2.Zero, Vector2.Zero).All(0.0f), Is.True);
        Assert.That(new Matrix4x4(new Vector4(float.NaN), new Vector4(float.NaN), new Vector4(float.NaN), new Vector4(float.NaN)).None(float.NaN), Is.True);
        Assert.That(Matrix4x4.Identity.Any(1.0f), Is.True);
        Assert.That(Matrix4x4.Identity.All(1.0f), Is.False);
        Assert.That(Matrix3x2.Identity.None(2.0f), Is.True);
    }

    [Test]
    public void RowVectorSrtAndPointDirectionHomogeneousContractsAreDistinct()
    {
        var angle = Angle.FromDegrees(90);
        var m32 = Matrix3x2.CreateScale(new Vector2(2, 3)) * Matrix3x2.CreateRotation(angle) * Matrix3x2.CreateTranslation(new Vector2(5, 7));

        Assert.That(Vector2.TransformPoint(new Vector2(1, 2), m32), Is.EqualTo(new Vector2(-1, 9)));
        Assert.That(Vector2.TransformDirection(new Vector2(1, 2), m32), Is.EqualTo(new Vector2(-6, 2)));
        Assert.That(m32.Row3, Is.EqualTo(new Vector2(5, 7)));

        var local = Matrix4x4.CreateScale(new Vector3(2, 3, 4)) * Matrix4x4.CreateRotationZ(angle) * Matrix4x4.CreateTranslation(new Vector3(5, 7, 11));
        var parent = Matrix4x4.CreateTranslation(new Vector3(13, 17, 19));
        var point = new Vector3(1, 2, 3);
        Assert.That(Vector3.TransformPoint(point, local * parent), Is.EqualTo(new Vector3(12, 26, 42)));
        Assert.That(Vector3.TransformDirection(point, local), Is.EqualTo(new Vector3(-6, 2, 12)));
        Assert.That(Vector4.Transform(new Vector4(point, 1), local), Is.EqualTo(new Vector4(-1, 9, 23, 1)));
        Assert.That(Matrix4x4.Transpose(Matrix4x4.Transpose(local)), Is.EqualTo(local));

        var perspective = Matrix4x4.CreateInfinitePerspectiveFieldOfView(1, 1, 0.1f);
        Assert.That(Vector4.Transform(new Vector4(1, 2, 0, 1), perspective).W, Is.Zero);
        Assert.That(Vector3.TransformPoint(new Vector3(1, 2, 0), perspective).Z, Is.EqualTo(0.1f));
    }

    [TestCase(0.0f)]
    [TestCase(-0.0f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void ScalarMaskReductionsRetainIeeeEquality(float value)
    {
        var vector = new Vector4(value);
        var equal = !float.IsNaN(value);

        Assert.That(vector.All(value), Is.EqualTo(equal));
        Assert.That(vector.Any(value), Is.EqualTo(equal));
        Assert.That(vector.None(value), Is.EqualTo(!equal));
    }

    [TestCase(1.25f, -2.5f, 3.0f, 4.0f)]
    [TestCase(-0.0f, float.MaxValue, float.Epsilon, -1.0f)]
    public void VectorArithmeticMatchesOrdinaryBclAlgebra(float x, float y, float z, float w)
    {
        var a2 = new Vector2(x, y);
        var a3 = new Vector3(x, y, z);
        var a4 = new Vector4(x, y, z, w);
        var b2 = new Vector2(2, -3);
        var b3 = new Vector3(2, -3, 4);
        var b4 = new Vector4(2, -3, 4, -5);

        Assert.That((a2 + b2).AsSystemVector2(), Is.EqualTo(a2.AsSystemVector2() + b2.AsSystemVector2()));
        Assert.That((a3 - b3).AsSystemVector3(), Is.EqualTo(a3.AsSystemVector3() - b3.AsSystemVector3()));
        Assert.That((a4 * b4).AsSystemVector4(), Is.EqualTo(a4.AsSystemVector4() * b4.AsSystemVector4()));
        Assert.That((a2 / b2).AsSystemVector2(), Is.EqualTo(a2.AsSystemVector2() / b2.AsSystemVector2()));
        Assert.That((a3 / b3).AsSystemVector3(), Is.EqualTo(a3.AsSystemVector3() / b3.AsSystemVector3()));
        Assert.That((a4 / b4).AsSystemVector4(), Is.EqualTo(a4.AsSystemVector4() / b4.AsSystemVector4()));
        Assert.That((a2 / 2.0f).AsSystemVector2(), Is.EqualTo(a2.AsSystemVector2() / 2.0f));
        Assert.That((a3 * 2.0f).AsSystemVector3(), Is.EqualTo(a3.AsSystemVector3() * 2.0f));
        Assert.That((-a4).AsSystemVector4(), Is.EqualTo(-a4.AsSystemVector4()));
        Assert.That(Vector2.Dot(a2, b2), Is.EqualTo(System.Numerics.Vector2.Dot(a2.AsSystemVector2(), b2.AsSystemVector2())));
        Assert.That(Vector3.Dot(a3, b3), Is.EqualTo(System.Numerics.Vector3.Dot(a3.AsSystemVector3(), b3.AsSystemVector3())));
        Assert.That(Vector4.Dot(a4, b4), Is.EqualTo(System.Numerics.Vector4.Dot(a4.AsSystemVector4(), b4.AsSystemVector4())));
        Assert.That(Vector2.LengthSquared(a2), Is.EqualTo(a2.AsSystemVector2().LengthSquared()));
        Assert.That(Vector3.Length(a3), Is.EqualTo(a3.AsSystemVector3().Length()));
        Assert.That(Vector4.DistanceSquared(a4, b4), Is.EqualTo(System.Numerics.Vector4.DistanceSquared(a4.AsSystemVector4(), b4.AsSystemVector4())));
        Assert.That(Vector3.Lerp(Vector3.Zero, Vector3.One, 2.0f), Is.EqualTo(new Vector3(2.0f)));
        Assert.That(Vector3.Cross(Vector3.UnitX, Vector3.UnitY), Is.EqualTo(Vector3.UnitZ));
    }
}
