// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using TerraFX.Numerics;
using BigInteger = System.Numerics.BigInteger;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class GeometryKernelTests
{
    private static void AssertProperFrame(Matrix4x4 view)
    {
        Assert.That(view.IsFinite().All(), Is.True);
        var right = new Vector3(view[0, 0], view[1, 0], view[2, 0]);
        var up = new Vector3(view[0, 1], view[1, 1], view[2, 1]);
        var forward = new Vector3(view[0, 2], view[1, 2], view[2, 2]);

        AssertUnit([right.X, right.Y, right.Z]);
        AssertUnit([up.X, up.Y, up.Z]);
        AssertUnit([forward.X, forward.Y, forward.Z]);
        Assert.That(Vector3.Dot(right, up), Is.EqualTo(0).Within(2.0e-6f));
        Assert.That(Vector3.Dot(right, forward), Is.EqualTo(0).Within(2.0e-6f));
        Assert.That(Vector3.Dot(up, forward), Is.EqualTo(0).Within(2.0e-6f));

        Assert.That(Vector3.Dot(right, Vector3.Cross(up, forward)), Is.EqualTo(1).Within(2.0e-6f));
        Assert.That(view[0, 3], Is.Zero);
        Assert.That(view[1, 3], Is.Zero);
        Assert.That(view[2, 3], Is.Zero);
        Assert.That(view[3, 3], Is.EqualTo(1));
    }

    private static void AssertQuaternionIdentity(Quaternion value)
    {
        Assert.That(value.X, Is.EqualTo(0).Within(2.0e-6f));
        Assert.That(value.Y, Is.EqualTo(0).Within(2.0e-6f));
        Assert.That(value.Z, Is.EqualTo(0).Within(2.0e-6f));
        Assert.That(value.W, Is.EqualTo(1).Within(2.0e-6f));
    }

    private static void AssertUnit(float[] components)
    {
        // Binary64 measures the unit-length invariant without changing the binary32 inputs.
        double square = 0;

        foreach (var component in components)
        {
            Assert.That(float.IsFinite(component), Is.True);
            square += (double)component * component;
        }

        Assert.That(Math.Sqrt(square), Is.EqualTo(1).Within(2.0e-6));
    }

    private static float Depth(Matrix4x4 projection, float z)
    {
        var clip = Vector4.Transform(new Vector4(0, 0, z, 1), projection);
        return clip.Z / clip.W;
    }

    private static BigInteger DyadicInteger(float component)
    {
        var bits = unchecked((uint)BitConverter.SingleToInt32Bits(component));
        var exponent = (int)((bits >> 23) & 255);
        var fraction = bits & 0x007F_FFFF;
        var integer = exponent == 0 ? new BigInteger(fraction) : new BigInteger(0x0080_0000 | fraction) << (exponent - 1);
        return (bits & 0x8000_0000) == 0 ? integer : -integer;
    }

    private static IEnumerable<Matrix3x2> Matrix3x2Cases()
    {
        yield return Matrix3x2.Identity;
        yield return default;
        yield return Matrix3x2.CreateScale(new Vector2(1, 0));
        yield return Matrix3x2.CreateScale(float.Epsilon);
        yield return Matrix3x2.CreateScale(float.MaxValue);
        yield return Matrix3x2.CreateScale(new Vector2(2, 3)) * Matrix3x2.CreateRotation(0.7f) * Matrix3x2.CreateTranslation(new Vector2(4096, -4096));
        yield return new Matrix3x2(new Vector2(1, 1), new Vector2(1, float.BitIncrement(1)), new Vector2(4096, -4096));
    }

    private static IEnumerable<Matrix4x4> Matrix4x4Cases()
    {
        yield return Matrix4x4.Identity;
        yield return default;
        yield return Matrix4x4.CreateScale(new Vector3(1, 0, 1));
        yield return Matrix4x4.CreateScale(new Vector3(float.Epsilon, 1, 1));
        yield return Matrix4x4.CreateScale(new Vector3(float.MaxValue));
        yield return Matrix4x4.CreateScale(new Vector3(2, 3, 4)) * Matrix4x4.CreateRotationY(0.7f) * Matrix4x4.CreateTranslation(new Vector3(4096, -4096, 4096));
        yield return Matrix4x4.CreatePerspectiveFieldOfView(1, 1.5f, 0.1f, 1000);
        yield return new Matrix4x4(new Vector4(1, 1, 0, 0), new Vector4(1, float.BitIncrement(1), 0, 0), Vector4.UnitZ, Vector4.UnitW);
        yield return new Matrix4x4(new Vector4(1, 2, 3, 4), new Vector4(5, 6, 7, 8), new Vector4(9, 10, 11, 12), new Vector4(13, 14, 15, 16));
    }

    private static IEnumerable<Matrix4x4> RejectedSrtCases()
    {
        yield return default;
        yield return Matrix4x4.CreateScale(new Vector3(0, 1, 1));
        yield return Matrix4x4.CreateScale(0.00001f);
        yield return Matrix4x4.CreateScale(1001);
        yield return new Matrix4x4(new Vector4(1, 0.5f, 0, 0), Vector4.UnitY, Vector4.UnitZ, Vector4.UnitW);
        yield return new Matrix4x4(new Vector4(1, 0, 0, 0.1f), Vector4.UnitY, Vector4.UnitZ, Vector4.UnitW);
    }

    private static bool Residual(System.Func<int, int, float> left, System.Func<int, int, float> right, int dimension)
    {
        var one = BigInteger.One << 298;

        for (var row = 0; row < dimension; row++)
        {
            for (var column = 0; column < dimension; column++)
            {
                BigInteger sum = 0;
                BigInteger magnitude = 0;

                for (var index = 0; index < dimension; index++)
                {
                    var term = DyadicInteger(left(row, index)) * DyadicInteger(right(index, column));
                    sum += term;
                    magnitude += BigInteger.Abs(term);
                }

                var error = BigInteger.Abs(sum - (row == column ? one : BigInteger.Zero));

                if (error * 100000 > one && error * 500000 > magnitude)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool Residual3(Matrix3x2 left, Matrix3x2 right) =>
        Residual((row, column) => column == 2 ? row == 2 ? 1 : 0 : left[row, column],
            (row, column) => column == 2 ? row == 2 ? 1 : 0 : right[row, column], 3);

    private static bool Residual4(Matrix4x4 left, Matrix4x4 right) => Residual((row, column) => left[row, column], (row, column) => right[row, column], 4);

    [TestCase(0.999999f)]
    [TestCase(1.0f)]
    [TestCase(1.000001f)]
    public void AxisAngleFactoriesAcceptAxesWithinBinary32UnitLengthTolerance(float x)
    {
        var axis = new Vector3(x, 0, 0);

        Assert.That(Quaternion.CreateFromAxisAngle(axis, 1.0f).IsFinite().All(), Is.True);
        Assert.That(Quaternion.CreateFromAxisAngle(axis, Angle.FromDegrees(90)).IsFinite().All(), Is.True);
        Assert.That(Matrix4x4.CreateFromAxisAngle(axis, 1.0f).IsFinite().All(), Is.True);
        Assert.That(Matrix4x4.CreateFromAxisAngle(axis, Angle.FromDegrees(90)).IsFinite().All(), Is.True);
    }

    [TestCase(0x3F7F_FFDEu, 0x0000_0000u, 0x3F7F_FFBCu, false)]
    [TestCase(0x3F7F_FFDEu, 0x3980_0000u, 0x3F7F_FFBDu, false)]
    [TestCase(0x3F7F_FFDEu, 0x39B5_04F3u, 0x3F7F_FFBEu, true)]
    [TestCase(0x3F7F_FFDFu, 0x0000_0000u, 0x3F7F_FFBEu, true)]
    [TestCase(0x3F80_0010u, 0x39B5_04F3u, 0x3F80_0021u, true)]
    [TestCase(0x3F80_0011u, 0x0000_0000u, 0x3F80_0022u, false)]
    public void AxisAngleFactoriesPreserveBothSquaredLengthAdmissionBoundaries(uint xBits, uint yBits, uint squaredBits, bool admitted)
    {
        var x = BitConverter.UInt32BitsToSingle(xBits);
        var y = BitConverter.UInt32BitsToSingle(yBits);
        Vector3[] axes = [new Vector3(x, y, -0.0f), new Vector3(-0.0f, -x, y), new Vector3(y, 0.0f, -x)];

        foreach (var axis in axes)
        {
            var squaredLength = Vector3.LengthSquared(axis);
            Assert.That(BitConverter.SingleToUInt32Bits(squaredLength), Is.EqualTo(squaredBits));
            Assert.That(float.Abs(float.Sqrt(squaredLength) - 1.0f) <= 2.0e-6f, Is.EqualTo(admitted));

            if (admitted)
            {
                Assert.That(Quaternion.CreateFromAxisAngle(axis, 1.0f).IsFinite().All(), Is.True);
                Assert.That(Quaternion.CreateFromAxisAngle(axis, Angle.FromDegrees(90)).IsFinite().All(), Is.True);
                Assert.That(Matrix4x4.CreateFromAxisAngle(axis, 1.0f).IsFinite().All(), Is.True);
                Assert.That(Matrix4x4.CreateFromAxisAngle(axis, Angle.FromDegrees(90)).IsFinite().All(), Is.True);
            }
            else
            {
                var quaternionRadians = Assert.Throws<ArgumentException>(() => Quaternion.CreateFromAxisAngle(axis, 1.0f));
                var quaternionAngle = Assert.Throws<ArgumentException>(() => Quaternion.CreateFromAxisAngle(axis, Angle.FromDegrees(90)));
                var matrixRadians = Assert.Throws<ArgumentException>(() => Matrix4x4.CreateFromAxisAngle(axis, 1.0f));
                var matrixAngle = Assert.Throws<ArgumentException>(() => Matrix4x4.CreateFromAxisAngle(axis, Angle.FromDegrees(90)));

                Assert.That(quaternionRadians?.ParamName, Is.EqualTo("unitAxis"));
                Assert.That(quaternionAngle?.ParamName, Is.EqualTo("unitAxis"));
                Assert.That(matrixRadians?.ParamName, Is.EqualTo("unitAxis"));
                Assert.That(matrixAngle?.ParamName, Is.EqualTo("unitAxis"));
            }
        }
    }

    [TestCase(0x7F80_0000u)]
    [TestCase(0xFF80_0000u)]
    [TestCase(0x7FC1_2345u)]
    [TestCase(0xFF81_2345u)]
    public void AxisAngleFactoriesRejectNonfiniteAxesWithTheOriginalArgumentContext(uint bits)
    {
        var component = BitConverter.UInt32BitsToSingle(bits);
        Vector3[] axes = [new Vector3(component, 0, 0), new Vector3(0, component, 0), new Vector3(0, 0, component)];

        foreach (var axis in axes)
        {
            var quaternionRadians = Assert.Throws<ArgumentException>(() => Quaternion.CreateFromAxisAngle(axis, 1.0f));
            var quaternionAngle = Assert.Throws<ArgumentException>(() => Quaternion.CreateFromAxisAngle(axis, Angle.FromDegrees(90)));
            var matrixRadians = Assert.Throws<ArgumentException>(() => Matrix4x4.CreateFromAxisAngle(axis, 1.0f));
            var matrixAngle = Assert.Throws<ArgumentException>(() => Matrix4x4.CreateFromAxisAngle(axis, Angle.FromDegrees(90)));

            Assert.That(quaternionRadians?.ParamName, Is.EqualTo("unitAxis"));
            Assert.That(quaternionAngle?.ParamName, Is.EqualTo("unitAxis"));
            Assert.That(matrixRadians?.ParamName, Is.EqualTo("unitAxis"));
            Assert.That(matrixAngle?.ParamName, Is.EqualTo("unitAxis"));
            Assert.That(quaternionRadians?.Message, Does.StartWith("Every component must be finite."));
            Assert.That(quaternionAngle?.Message, Does.StartWith("Every component must be finite."));
            Assert.That(matrixRadians?.Message, Does.StartWith("Every component must be finite."));
            Assert.That(matrixAngle?.Message, Does.StartWith("Every component must be finite."));
        }
    }

    [TestCase(0.0f)]
    [TestCase(2.0f)]
    [TestCase(1.000003f)]
    [TestCase(float.MaxValue)]
    public void AxisAngleFactoriesRejectRatherThanNormalizeNonunitAxes(float x)
    {
        var axis = new Vector3(x, 0, 0);

        _ = Assert.Throws<ArgumentException>(() => Quaternion.CreateFromAxisAngle(axis, 1.0f));
        _ = Assert.Throws<ArgumentException>(() => Quaternion.CreateFromAxisAngle(axis, Angle.FromDegrees(90)));
        _ = Assert.Throws<ArgumentException>(() => Matrix4x4.CreateFromAxisAngle(axis, 1.0f));
        _ = Assert.Throws<ArgumentException>(() => Matrix4x4.CreateFromAxisAngle(axis, Angle.FromDegrees(90)));
    }

    [TestCaseSource(nameof(RejectedSrtCases))]
    public void DecompositionRefusesShearSingularProjectiveAndInadmissibleScale(Matrix4x4 value)
    {
        Assert.That(Matrix4x4.TryDecompose(value, out var scale, out var rotation, out var translation), Is.False);
        Assert.That(scale, Is.EqualTo(default(Vector3)));
        Assert.That(rotation, Is.EqualTo(default(Quaternion)));
        Assert.That(translation, Is.EqualTo(default(Vector3)));
    }

    [TestCase(2.0f, 3.0f, 4.0f)]
    [TestCase(-2.0f, 3.0f, 4.0f)]
    [TestCase(-2.0f, -3.0f, -4.0f)]
    [TestCase(0.0001f, 1.0f, 1000.0f)]
    public void DecompositionRetainsAdmittedSrtAndChecksRecomposition(float x, float y, float z)
    {
        var value = Matrix4x4.CreateScale(new Vector3(x, y, z)) * Matrix4x4.CreateRotationY(0.7f) * Matrix4x4.CreateTranslation(new Vector3(10, 20, 30));

        Assert.That(Matrix4x4.TryDecompose(value, out var scale, out var rotation, out var translation), Is.True);
        var recomposed = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
        var maximum = BigInteger.One << 149;

        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                maximum = BigInteger.Max(maximum, BigInteger.Abs(DyadicInteger(value[row, column])));
            }
        }

        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                var residual = BigInteger.Abs(DyadicInteger(recomposed[row, column]) - DyadicInteger(value[row, column]));
                Assert.That(residual * 100000 <= maximum, Is.True);
            }
        }

        AssertUnit([rotation.X, rotation.Y, rotation.Z, rotation.W]);
        Assert.That(translation, Is.EqualTo(new Vector3(10, 20, 30)));
    }

    [Test]
    public void LargeRotatedTranslationIsNotBlanketRejected()
    {
        var matrix = Matrix4x4.CreateRotationY(0.7f) * Matrix4x4.CreateTranslation(new Vector3(4096, -4096, 4096));

        Assert.That(Matrix4x4.TryInvert(matrix, out var inverse), Is.True);
        Assert.That(Residual4(matrix, inverse), Is.True);
        Assert.That(Residual4(inverse, matrix), Is.True);

        var affine = Matrix3x2.CreateRotation(0.7f) * Matrix3x2.CreateTranslation(new Vector2(4096, -4096));
        Assert.That(Matrix3x2.TryInvert(affine, out var affineInverse), Is.True);
        Assert.That(Residual3(affine, affineInverse), Is.True);
        Assert.That(Residual3(affineInverse, affine), Is.True);
    }

    [Test]
    public void LookAtUsesExactLargeAndSubnormalDifferencesAndRefusesCoincidence()
    {
        Assert.That(Matrix4x4.TryCreateLookAt(new Vector3(-float.MaxValue, 0, 0), new Vector3(float.MaxValue, 0, 0), Vector3.UnitY, out var view), Is.True);
        AssertProperFrame(view);
        Assert.That(view.Row4.Z, Is.EqualTo(float.MaxValue));

        Assert.That(Matrix4x4.TryCreateLookAt(Vector3.Zero, new Vector3(float.Epsilon, 0, 0), new Vector3(0, float.Epsilon, 0), out view), Is.True);
        AssertProperFrame(view);

        Assert.That(Matrix4x4.TryCreateLookTo(Vector3.Zero, new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), Vector3.UnitY, out view), Is.True);
        AssertProperFrame(view);

        Assert.That(Matrix4x4.TryCreateLookAt(Vector3.One, Vector3.One, Vector3.UnitY, out view), Is.False);
        Assert.That(view, Is.EqualTo(default(Matrix4x4)));
        Assert.That(Matrix4x4.TryCreateLookTo(Vector3.Zero, Vector3.Zero, Vector3.UnitY, out view), Is.False);
        Assert.That(view, Is.EqualTo(default(Matrix4x4)));
        Assert.That(Matrix4x4.CreateLookTo(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY), Is.EqualTo(Matrix4x4.Identity));
    }

    [TestCase(0.0f)]
    [TestCase(1.0f)]
    [TestCase(-1.0f)]
    [TestCase(2.0f)]
    public void LookSourceParallelismIsEstablishedBeforeNormalizedRounding(float upScale)
    {
        var direction = new Vector3(1, 2, 3);
        var up = direction * upScale;

        Assert.That(Matrix4x4.TryCreateLookTo(Vector3.Zero, direction, up, out var view), Is.False);
        Assert.That(view, Is.EqualTo(default(Matrix4x4)));
        Assert.That(Matrix4x4.TryCreateLookAt(Vector3.Zero, direction, up, out view), Is.False);
        Assert.That(view, Is.EqualTo(default(Matrix4x4)));
        _ = Assert.Throws<ArgumentException>(() => Matrix4x4.CreateLookTo(Vector3.Zero, direction, up));
        _ = Assert.Throws<ArgumentException>(() => Matrix4x4.CreateLookAt(Vector3.Zero, direction, up));
    }

    [TestCaseSource(nameof(Matrix3x2Cases))]
    public void Matrix3x2InversionUsesTheFullHomogeneous3x3Residual(Matrix3x2 value)
    {
        var bclSuccess = System.Numerics.Matrix3x2.Invert(value.AsSystemMatrix3x2(), out var bclInverse);
        var candidate = bclInverse.AsTerraFXMatrix3x2();
        var expected = bclSuccess && candidate.IsFinite().All() && Residual3(value, candidate) && Residual3(candidate, value);

        Assert.That(Matrix3x2.TryInvert(value, out var inverse), Is.EqualTo(expected));

        if (expected)
        {
            Assert.That(inverse, Is.EqualTo(candidate));
            Assert.That(Residual3(value, inverse), Is.True);
            Assert.That(Residual3(inverse, value), Is.True);
        }
        else
        {
            Assert.That(inverse, Is.EqualTo(default(Matrix3x2)));
            _ = Assert.Throws<ArgumentException>(() => Matrix3x2.Invert(value));
        }
    }

    [TestCaseSource(nameof(Matrix4x4Cases))]
    public void Matrix4x4InversionMatchesBothExactProductResidualPredicates(Matrix4x4 value)
    {
        var bclSuccess = System.Numerics.Matrix4x4.Invert(value.AsSystemMatrix4x4(), out var bclInverse);
        var candidate = bclInverse.AsTerraFXMatrix4x4();
        var expected = bclSuccess && candidate.IsFinite().All() && Residual4(value, candidate) && Residual4(candidate, value);

        Assert.That(Matrix4x4.TryInvert(value, out var inverse), Is.EqualTo(expected));

        if (expected)
        {
            Assert.That(inverse, Is.EqualTo(candidate));
            Assert.That(Residual4(value, inverse), Is.True);
            Assert.That(Residual4(inverse, value), Is.True);
            Assert.That(Matrix4x4.Invert(value), Is.EqualTo(inverse));
        }
        else
        {
            Assert.That(inverse, Is.EqualTo(default(Matrix4x4)));
            _ = Assert.Throws<ArgumentException>(() => Matrix4x4.Invert(value));
        }
    }

    [TestCase(3, 0x1C84_61F9u, true)]
    [TestCase(3, 0x1C84_61FAu, false)]
    [TestCase(4, 0x1C84_61F9u, true)]
    [TestCase(4, 0x1C84_61FAu, false)]
    public void MatrixResidualAdmissionAtSubBinary64BoundariesUsesTheExactTolerance(int dimension, uint correctionBits, bool admitted)
    {
        var left = new float[dimension * dimension];
        var right = new float[dimension * dimension];

        for (var index = 0; index < dimension; index++)
        {
            left[(index * dimension) + index] = 1.0f;
            right[(index * dimension) + index] = 1.0f;
        }

        // The three contributions bracket exact 1e-5 within 1e-28; their binary64
        // sums coincide, so only the bounded fallback can decide this entry.
        left[1] = 1.0f;
        left[2] = BitConverter.UInt32BitsToSingle(0x2A8E_368Fu);
        right[1] = -1.0f;
        right[2] = BitConverter.UInt32BitsToSingle(correctionBits);
        right[dimension + 2] = BitConverter.UInt32BitsToSingle(0x3727_C5ACu);

        Assert.That(Residual((row, column) => left[(row * dimension) + column], (row, column) => right[(row * dimension) + column], dimension), Is.EqualTo(admitted));
        Assert.That(GeometryKernel.InverseResidual(left, right, dimension), Is.EqualTo(admitted));
    }

    [Test]
    public void MatrixResidualAdmissionCoversFiniteRandomCandidatesAcrossScaleRanges()
    {
        var random = new Random(4702);
        var successes = 0;
        var failures = 0;
        var residualFailures = 0;

        for (var sample = 0; sample < 200; sample++)
        {
            var scale = float.ScaleB(1.0f, (sample % 17 * 10) - 80);

            Vector4 Row()
            {
                return new Vector4((random.NextSingle() - 0.5f) * scale, (random.NextSingle() - 0.5f) * scale, (random.NextSingle() - 0.5f) * scale, (random.NextSingle() - 0.5f) * scale);
            }

            var matrix = new Matrix4x4(Row(), Row(), Row(), Row());
            var success = System.Numerics.Matrix4x4.Invert(matrix.AsSystemMatrix4x4(), out var bcl);
            var candidate = bcl.AsTerraFXMatrix4x4();
            var expected = success && candidate.IsFinite().All() && Residual4(matrix, candidate) && Residual4(candidate, matrix);

            Assert.That(Matrix4x4.TryInvert(matrix, out var inverse), Is.EqualTo(expected));

            if (expected)
            {
                successes++;
                Assert.That(inverse, Is.EqualTo(candidate));
            }
            else
            {
                failures++;

                if (success && candidate.IsFinite().All())
                {
                    residualFailures++;
                }

                Assert.That(inverse, Is.EqualTo(default(Matrix4x4)));
            }
        }

        Assert.That(successes, Is.GreaterThan(0));
        Assert.That(failures, Is.GreaterThan(0));
        Assert.That(residualFailures, Is.GreaterThan(0));
    }

    [Test]
    public void NearbyNonparallelSourcesProceedWithoutAngularEpsilon()
    {
        var direction = new Vector3(1, 2, 3);
        var up = new Vector3(1, 2, float.BitIncrement(3));

        Assert.That(Matrix4x4.TryCreateLookTo(Vector3.Zero, direction, up, out var view), Is.True);
        AssertProperFrame(view);
        Assert.That(Matrix4x4.TryCreateLookAt(Vector3.Zero, direction, up, out var at), Is.True);
        Assert.That(at, Is.EqualTo(view));

        var position = new Vector3(-16777216, -16777216, 0);
        var target = new Vector3(1, 0, 0);
        up = new Vector3(1, 1, 0);
        Assert.That(Matrix4x4.TryCreateLookAt(position, target, up, out view), Is.True);
        AssertProperFrame(view);
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void NonfiniteConstrainedInputsThrowInsteadOfReturningFalse(float component)
    {
        var v2 = new Vector2(1, component);
        var v3 = new Vector3(1, 0, component);
        var v4 = new Vector4(1, 0, 0, component);
        var q = new Quaternion(1, 0, 0, component);
        var m32 = new Matrix3x2(Vector2.UnitX, Vector2.UnitY, v2);
        var m44 = new Matrix4x4(Vector4.UnitX, Vector4.UnitY, Vector4.UnitZ, v4);

        _ = Assert.Throws<ArgumentException>(() => Vector2.TryNormalize(v2, out _));
        _ = Assert.Throws<ArgumentException>(() => Vector3.TryNormalize(v3, out _));
        _ = Assert.Throws<ArgumentException>(() => Vector4.TryNormalize(v4, out _));
        _ = Assert.Throws<ArgumentException>(() => Quaternion.TryNormalize(q, out _));
        _ = Assert.Throws<ArgumentException>(() => Quaternion.TryInvert(q, out _));
        _ = Assert.Throws<ArgumentException>(() => Matrix3x2.TryInvert(m32, out _));
        _ = Assert.Throws<ArgumentException>(() => Matrix4x4.TryInvert(m44, out _));
        _ = Assert.Throws<ArgumentException>(() => Matrix4x4.TryDecompose(m44, out _, out _, out _));
        _ = Assert.Throws<ArgumentException>(() => Matrix4x4.TryCreateLookTo(v3, Vector3.UnitZ, Vector3.UnitY, out _));
        _ = Assert.Throws<ArgumentException>(() => Matrix4x4.TryCreateLookTo(Vector3.Zero, v3, Vector3.UnitY, out _));
        _ = Assert.Throws<ArgumentException>(() => Matrix4x4.TryCreateLookAt(Vector3.Zero, Vector3.UnitZ, v3, out _));
        _ = Assert.Throws<ArgumentException>(() => Matrix3x2.CreateRotation(component));
        _ = Assert.Throws<ArgumentException>(() => Matrix4x4.CreateRotationX(component));
        _ = Assert.Throws<ArgumentException>(() => Matrix4x4.CreateRotationY(component));
        _ = Assert.Throws<ArgumentException>(() => Matrix4x4.CreateRotationZ(component));
        _ = Assert.Throws<ArgumentException>(() => Quaternion.CreateFromAxisAngle(Vector3.UnitX, component));
        _ = Assert.Throws<ArgumentException>(() => Matrix4x4.CreateFromAxisAngle(Vector3.UnitX, Angle.FromDegrees(component)));
        _ = Assert.Throws<ArgumentException>(() => Matrix4x4.CreatePerspectiveFieldOfView(1, 1, 0.1f, component));
        _ = Assert.Throws<ArgumentException>(() => Matrix4x4.CreateInfinitePerspectiveFieldOfView(1, component, 0.1f));
        _ = Assert.Throws<ArgumentException>(() => Matrix4x4.CreateOrthographic(2, 2, component, 100));
    }

    [Test]
    public void NormalizationHandlesMixedMagnitudesAcrossEveryFiniteExponent()
    {
        for (var exponent = -149; exponent <= 127; exponent++)
        {
            var value = new Vector4(float.ScaleB(1.0f, exponent), -float.Epsilon, float.ScaleB(0.75f, exponent), -0.0f);

            var normalized = Vector4.Normalize(value);
            AssertUnit([normalized.X, normalized.Y, normalized.Z, normalized.W]);
            Assert.That(BitConverter.SingleToInt32Bits(normalized.W), Is.EqualTo(int.MinValue));
        }
    }

    [Test]
    public void NormalizationMatchesLiteralVectorsAcrossDimensions()
    {
        var vector2 = Vector2.Normalize(new Vector2(3.0f, 4.0f));
        Assert.That(BitConverter.SingleToUInt32Bits(vector2.X), Is.EqualTo(0x3F19_999A));
        Assert.That(BitConverter.SingleToUInt32Bits(vector2.Y), Is.EqualTo(0x3F4C_CCCD));

        var vector3 = Vector3.Normalize(Vector3.One);
        Assert.That(BitConverter.SingleToUInt32Bits(vector3.X), Is.EqualTo(0x3F13_CD3A));
        Assert.That(BitConverter.SingleToUInt32Bits(vector3.Y), Is.EqualTo(0x3F13_CD3A));
        Assert.That(BitConverter.SingleToUInt32Bits(vector3.Z), Is.EqualTo(0x3F13_CD3A));

        var negativeZero = BitConverter.UInt32BitsToSingle(0x8000_0000);
        var vector4 = Vector4.Normalize(new Vector4(3.0f, 4.0f, 0.0f, negativeZero));
        Assert.That(BitConverter.SingleToUInt32Bits(vector4.X), Is.EqualTo(0x3F19_999A));
        Assert.That(BitConverter.SingleToUInt32Bits(vector4.Y), Is.EqualTo(0x3F4C_CCCD));
        Assert.That(BitConverter.SingleToUInt32Bits(vector4.Z), Is.Zero);
        Assert.That(BitConverter.SingleToUInt32Bits(vector4.W), Is.EqualTo(0x8000_0000));

        var maximum = float.MaxValue;
        var vector4Extreme = Vector4.Normalize(new Vector4(maximum, maximum, float.Epsilon, negativeZero));
        var quaternionExtreme = Quaternion.Normalize(new Quaternion(maximum, maximum, float.Epsilon, negativeZero));
        Assert.That(BitConverter.SingleToUInt32Bits(vector4Extreme.X), Is.EqualTo(0x3F35_04F3));
        Assert.That(BitConverter.SingleToUInt32Bits(vector4Extreme.Y), Is.EqualTo(0x3F35_04F3));
        Assert.That(BitConverter.SingleToUInt32Bits(vector4Extreme.Z), Is.Zero);
        Assert.That(BitConverter.SingleToUInt32Bits(vector4Extreme.W), Is.EqualTo(0x8000_0000));
        Assert.That(BitConverter.SingleToUInt32Bits(quaternionExtreme.X), Is.EqualTo(0x3F35_04F3));
        Assert.That(BitConverter.SingleToUInt32Bits(quaternionExtreme.Y), Is.EqualTo(0x3F35_04F3));
        Assert.That(BitConverter.SingleToUInt32Bits(quaternionExtreme.Z), Is.Zero);
        Assert.That(BitConverter.SingleToUInt32Bits(quaternionExtreme.W), Is.EqualTo(0x8000_0000));
    }

    [TestCase(float.Epsilon)]
    [TestCase(-float.Epsilon)]
    [TestCase(1.17549435e-38f)]
    [TestCase(1.0e-30f)]
    [TestCase(1.0f)]
    [TestCase(4096.0f)]
    [TestCase(1.0e30f)]
    [TestCase(float.MaxValue)]
    [TestCase(-float.MaxValue)]
    public void NormalizationSupportsFiniteExtremesAndPreservesZeroSigns(float magnitude)
    {
        var negativeZero = BitConverter.Int32BitsToSingle(int.MinValue);

        Assert.That(Vector2.TryNormalize(new Vector2(magnitude, negativeZero), out var v2), Is.True);
        Assert.That(Vector3.TryNormalize(new Vector3(magnitude, -magnitude, negativeZero), out var v3), Is.True);
        Assert.That(Vector4.TryNormalize(new Vector4(magnitude, -magnitude, magnitude, negativeZero), out var v4), Is.True);
        Assert.That(Quaternion.TryNormalize(new Quaternion(magnitude, -magnitude, magnitude, negativeZero), out var q), Is.True);

        AssertUnit([v2.X, v2.Y]);
        AssertUnit([v3.X, v3.Y, v3.Z]);
        AssertUnit([v4.X, v4.Y, v4.Z, v4.W]);
        AssertUnit([q.X, q.Y, q.Z, q.W]);

        Assert.That(BitConverter.SingleToInt32Bits(v2.Y), Is.EqualTo(int.MinValue));
        Assert.That(BitConverter.SingleToInt32Bits(v3.Z), Is.EqualTo(int.MinValue));
        Assert.That(BitConverter.SingleToInt32Bits(v4.W), Is.EqualTo(int.MinValue));
        Assert.That(BitConverter.SingleToInt32Bits(q.W), Is.EqualTo(int.MinValue));

        Assert.That(Vector2.Normalize(new Vector2(magnitude, negativeZero)), Is.EqualTo(v2));
        Assert.That(Vector3.Normalize(new Vector3(magnitude, -magnitude, negativeZero)), Is.EqualTo(v3));
        Assert.That(Vector4.Normalize(new Vector4(magnitude, -magnitude, magnitude, negativeZero)), Is.EqualTo(v4));
        Assert.That(Quaternion.Normalize(new Quaternion(magnitude, -magnitude, magnitude, negativeZero)), Is.EqualTo(q));
    }

    [Test]
    public void ProjectionDoesNotOverflowRepresentableDerivedProducts()
    {
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(float.Pi * 0.5f, 1, float.MaxValue * 0.25f, float.MaxValue * 0.5f);

        Assert.That(projection.IsFinite().All(), Is.True);
        Assert.That(projection[2, 2], Is.EqualTo(-1));
        Assert.That(projection[3, 2], Is.EqualTo(float.MaxValue * 0.5f));
    }

    [Test]
    public void ProjectionScalarRangesAndDerivedInverseFailuresAreObservable()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Matrix4x4.CreatePerspectiveFieldOfView(0, 1, 0.1f, 10));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Matrix4x4.CreatePerspectiveFieldOfView(float.Pi, 1, 0.1f, 10));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Matrix4x4.CreatePerspectiveFieldOfView(1, 0, 0.1f, 10));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Matrix4x4.CreatePerspectiveFieldOfView(1, 1, 0, 10));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Matrix4x4.CreatePerspectiveFieldOfView(1, 1, 10, 10));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Matrix4x4.CreateOrthographic(0, 2, -1, 1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Matrix4x4.CreateOrthographic(2, -1, -1, 1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Matrix4x4.CreateOrthographic(2, 2, 1, -1));
        _ = Assert.Throws<ArgumentException>(() => Matrix4x4.CreateInfinitePerspectiveFieldOfView(1, 1, float.Epsilon));
        _ = Assert.Throws<ArgumentException>(() => Matrix4x4.CreateOrthographic(2, 2, -float.MaxValue, float.MaxValue));
    }

    [TestCase(1.0f)]
    [TestCase(1.0e-38f)]
    [TestCase(float.MaxValue)]
    public void QuaternionInverseConjugatesVectorZeroSignsAndPreservesScalarSign(float magnitude)
    {
        var value = new Quaternion(0, -0.0f, 0, magnitude);

        Assert.That(Quaternion.TryInvert(value, out var inverse), Is.True);
        Assert.That(BitConverter.SingleToInt32Bits(inverse.X), Is.EqualTo(int.MinValue));
        Assert.That(BitConverter.SingleToInt32Bits(inverse.Y), Is.Zero);
        Assert.That(BitConverter.SingleToInt32Bits(inverse.Z), Is.EqualTo(int.MinValue));
        Assert.That(inverse.W, Is.GreaterThan(0));
        AssertQuaternionIdentity(value * inverse);
        AssertQuaternionIdentity(inverse * value);
    }

    [TestCase(1.0f)]
    [TestCase(1.0e-20f)]
    [TestCase(1.17549435e-38f)]
    [TestCase(1.0e-38f)]
    [TestCase(1.0e20f)]
    [TestCase(float.MaxValue)]
    public void QuaternionInverseHasFiniteRepresentabilityAndBothHamiltonProducts(float magnitude)
    {
        var value = new Quaternion(magnitude, -magnitude, 0, -0.0f);

        Assert.That(Quaternion.TryInvert(value, out var inverse), Is.True);
        Assert.That(inverse.IsFinite().All(), Is.True);

        AssertQuaternionIdentity(value * inverse);
        AssertQuaternionIdentity(inverse * value);
        Assert.That(Quaternion.Invert(value), Is.EqualTo(inverse));

        Assert.That(BitConverter.SingleToInt32Bits(inverse.Z), Is.EqualTo(int.MinValue));
        Assert.That(BitConverter.SingleToInt32Bits(inverse.W), Is.EqualTo(int.MinValue));
    }

    [Test]
    public void QuaternionOverflowBoundaryUsesTheExactBoundedFallback()
    {
        var value = new Quaternion(BitConverter.Int32BitsToSingle(0x0010_016B), BitConverter.Int32BitsToSingle(0x0010_0000), 0, 0);
        Assert.That(Quaternion.TryInvert(value, out var inverse), Is.True);
        Assert.That(BitConverter.SingleToInt32Bits(inverse.X), Is.EqualTo(unchecked((int)0xFF7F_FFFF)));
        Assert.That(BitConverter.SingleToInt32Bits(inverse.Y), Is.EqualTo(unchecked((int)0xFF7F_E951)));
        Assert.That(BitConverter.SingleToInt32Bits(inverse.Z), Is.EqualTo(int.MinValue));
        Assert.That(BitConverter.SingleToInt32Bits(inverse.W), Is.Zero);
        AssertQuaternionIdentity(value * inverse);
        AssertQuaternionIdentity(inverse * value);
    }

    [TestCase(0x0010_0000)]
    [TestCase(0x0010_0100)]
    [TestCase(0x0010_0101)]
    [TestCase(0x0010_016A)]
    [TestCase(0x0010_016B)]
    public void QuaternionSubnormalRepresentabilityUsesTheExactOverflowMidpoint(int xBits)
    {
        var value = new Quaternion(BitConverter.Int32BitsToSingle(xBits), BitConverter.Int32BitsToSingle(0x0010_0000), 0, 0);
        var nx = DyadicInteger(value.X);
        var ny = DyadicInteger(value.Y);
        var square = (nx * nx) + (ny * ny);
        var midpoint = (BigInteger.One << 128) - (BigInteger.One << 103);
        var representable = (nx << 149) < midpoint * square && (ny << 149) < midpoint * square;

        Assert.That(Quaternion.TryInvert(value, out var inverse), Is.EqualTo(representable));

        if (representable)
        {
            Assert.That(inverse.IsFinite().All(), Is.True);
            AssertQuaternionIdentity(value * inverse);
            AssertQuaternionIdentity(inverse * value);
        }
        else
        {
            Assert.That(inverse, Is.EqualTo(default(Quaternion)));
        }
    }

    [Test]
    public void ReversedPerspectiveInfiniteAndSignedOrthographicDepthMatchChapter03()
    {
        var finite = Matrix4x4.CreatePerspectiveFieldOfView(1.0f, 1.5f, 0.1f, 100.0f);

        Assert.That(Depth(finite, 0.1f), Is.EqualTo(1).Within(2.0e-6f));
        Assert.That(Depth(finite, 100), Is.EqualTo(0).Within(2.0e-6f));
        Assert.That(Vector4.Transform(new Vector4(0, 0, 3, 1), finite).W, Is.EqualTo(3));

        var infinite = Matrix4x4.CreateInfinitePerspectiveFieldOfView(1.0f, 1.5f, 0.1f);
        Assert.That(infinite[2, 2], Is.Zero);
        Assert.That(infinite[3, 2], Is.EqualTo(0.1f));
        Assert.That(Depth(infinite, 100), Is.EqualTo(0.001f).Within(2.0e-6f));

        var ortho = Matrix4x4.CreateOrthographic(20, 10, -10, 30);
        Assert.That(Depth(ortho, -10), Is.EqualTo(1).Within(2.0e-6f));
        Assert.That(Depth(ortho, 30), Is.EqualTo(0).Within(2.0e-6f));
        Assert.That(Vector4.Transform(new Vector4(10, 5, 0, 1), ortho).X, Is.EqualTo(1));

        Assert.That(Matrix4x4.TryInvert(finite, out _), Is.True);
        Assert.That(Matrix4x4.TryInvert(infinite, out _), Is.True);
        Assert.That(Matrix4x4.TryInvert(ortho, out _), Is.True);
    }

    [Test]
    public void ThrowingCameraFactoriesIdentifyTheResponsibleSourceParameter()
    {
        var direction = Assert.Throws<ArgumentException>(() => Matrix4x4.CreateLookTo(Vector3.Zero, Vector3.Zero, Vector3.UnitY));
        Assert.That(direction?.ParamName, Is.EqualTo("direction"));

        var target = Assert.Throws<ArgumentException>(() => Matrix4x4.CreateLookAt(Vector3.One, Vector3.One, Vector3.UnitY));
        Assert.That(target?.ParamName, Is.EqualTo("target"));

        var up = Assert.Throws<ArgumentException>(() => Matrix4x4.CreateLookTo(Vector3.Zero, new Vector3(1, 2, 3), new Vector3(2, 4, 6)));
        Assert.That(up?.ParamName, Is.EqualTo("up"));

        var position = Assert.Throws<ArgumentException>(() => Matrix4x4.CreateLookTo(new Vector3(float.MaxValue, float.MaxValue, 0), Vector3.UnitZ, new Vector3(1, -1, 0)));
        Assert.That(position?.ParamName, Is.EqualTo("position"));

        var height = Assert.Throws<ArgumentException>(() => Matrix4x4.CreateOrthographic(2, float.Epsilon, -1, 1));
        Assert.That(height?.ParamName, Is.EqualTo("height"));
    }

    [Test]
    public void UnitAxisAdmissionUsesTheExactLengthToleranceAndRetainsTheSuppliedAxis()
    {
        var axis = new Vector3(1.000001f, 0, 0);
        var rotation = Quaternion.CreateFromAxisAngle(axis, Angle.FromDegrees(180));

        Assert.That(rotation.X, Is.EqualTo(axis.X));
        Assert.That(rotation.W, Is.Zero);
        Assert.That(Matrix4x4.CreateFromAxisAngle(-Vector3.UnitX, Angle.FromDegrees(90)).IsFinite().All(), Is.True);
        var zeroError = Assert.Throws<ArgumentException>(() => Vector3.Normalize(Vector3.Zero));
        Assert.That(zeroError?.ParamName, Is.EqualTo("value"));
    }

    [Test]
    public void ViewTranslationRoundsExactDotsOnceAndRefusesNonrepresentableResults()
    {
        var coordinate = float.MaxValue * 0.8f;
        var position = new Vector3(coordinate);

        Assert.That(Matrix4x4.TryCreateLookTo(position, new Vector3(2, 2, -1), new Vector3(2, -1, 2), out var view), Is.True);
        AssertProperFrame(view);
        Assert.That(view.Row4.X / coordinate, Is.EqualTo(-1).Within(2.0e-6f));
        Assert.That(view.Row4.Y / coordinate, Is.EqualTo(-1).Within(2.0e-6f));
        Assert.That(view.Row4.Z / coordinate, Is.EqualTo(-1).Within(2.0e-6f));

        Assert.That(Matrix4x4.TryCreateLookTo(new Vector3(float.MaxValue, float.MaxValue, 0), Vector3.UnitZ, new Vector3(1, -1, 0), out view), Is.False);
        Assert.That(view, Is.EqualTo(default(Matrix4x4)));
    }

    [Test]
    public void ZeroConstrainedInputsRefuseWithoutIdentityOrZeroSuccess()
    {
        Assert.That(Vector2.TryNormalize(new Vector2(-0.0f), out var v2), Is.False);
        Assert.That(Vector3.TryNormalize(new Vector3(-0.0f), out var v3), Is.False);
        Assert.That(Vector4.TryNormalize(new Vector4(-0.0f), out var v4), Is.False);
        Assert.That(Quaternion.TryNormalize(default, out var q), Is.False);

        Assert.That(v2, Is.EqualTo(default(Vector2)));
        Assert.That(v3, Is.EqualTo(default(Vector3)));
        Assert.That(v4, Is.EqualTo(default(Vector4)));
        Assert.That(q, Is.EqualTo(default(Quaternion)));

        _ = Assert.Throws<ArgumentException>(() => Vector2.Normalize(default));
        _ = Assert.Throws<ArgumentException>(() => Vector3.Normalize(default));
        _ = Assert.Throws<ArgumentException>(() => Vector4.Normalize(default));
        _ = Assert.Throws<ArgumentException>(() => Quaternion.Normalize(default));
        Assert.That(Quaternion.TryInvert(default, out q), Is.False);
        Assert.That(q, Is.EqualTo(default(Quaternion)));

        _ = Assert.Throws<ArgumentException>(() => Quaternion.Invert(default));
        Assert.That(Quaternion.TryInvert(new Quaternion(float.Epsilon, 0, 0, 0), out q), Is.False);
        Assert.That(q, Is.EqualTo(default(Quaternion)));
    }
}
