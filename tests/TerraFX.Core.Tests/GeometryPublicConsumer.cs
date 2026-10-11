// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Numerics;
using TerraFX.Numerics;
using Matrix3x2 = TerraFX.Numerics.Matrix3x2;
using Matrix4x4 = TerraFX.Numerics.Matrix4x4;
using Quaternion = TerraFX.Numerics.Quaternion;
using Vector2 = TerraFX.Numerics.Vector2;
using Vector3 = TerraFX.Numerics.Vector3;
using Vector4 = TerraFX.Numerics.Vector4;

namespace TerraFX.Core.Tests;

internal static class GeometryPublicConsumer
{
    public static (int Comparison, bool LessThan, bool LessThanOrEqual, bool GreaterThan, bool GreaterThanOrEqual) Compare<T>(T left, T right)
        where T : IComparable<T>, IComparisonOperators<T, T, bool>
    {
        return (left.CompareTo(right), left < right, left <= right, left > right, left >= right);
    }

    public static bool Equal<T>(T left, T right)
        where T : IEqualityOperators<T, T, bool> => left == right;
    public static bool Exercise()
    {
        var xy = new Vector2(x: 1.0f, y: 2.0f);
        var xyz = new Vector3(xy: xy, z: 3.0f);
        var xyzw = new Vector4(xyz: xyz, w: 4.0f);
        var quaternion = new Quaternion(vector: xyz, scalar: 4.0f);
        var affine = new Matrix3x2(row1: Vector2.UnitX, row2: Vector2.UnitY, row3: xy);
        var matrix = new Matrix4x4(row1: Vector4.UnitX, row2: Vector4.UnitY, row3: Vector4.UnitZ, row4: new Vector4(xyz, 1.0f));

        _ = new Vector2(value: 1.0f);
        _ = new Vector3(value: 1.0f);
        _ = new Vector3(x: 1.0f, y: 2.0f, z: 3.0f);
        _ = new Vector4(value: 1.0f);
        _ = new Vector4(x: 1.0f, y: 2.0f, z: 3.0f, w: 4.0f);
        _ = new Quaternion(x: 1.0f, y: 2.0f, z: 3.0f, w: 4.0f);
        _ = (xy.X, xy.Y, xyz.X, xyz.Y, xyz.Z, xyzw.X, xyzw.Y, xyzw.Z, xyzw.W, quaternion.X, quaternion.Y, quaternion.Z, quaternion.W);
        _ = (Vector2.Zero, Vector2.One, Vector3.Zero, Vector3.One, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, Vector4.Zero, Vector4.One);
        _ = (affine.Row1, affine.Row2, affine.Row3, affine[row: 2, column: 1], matrix.Row1, matrix.Row2, matrix.Row3, matrix.Row4, matrix[row: 3, column: 2]);
        _ = (xy.All(), xy.Any(), xy.None(), xy.All(value: 1), xy.Any(value: 1), xy.None(value: 1));
        _ = (xyz.All(), xyz.Any(), xyz.None(), xyz.All(value: 1), xyz.Any(value: 1), xyz.None(value: 1));
        _ = (xyzw.All(), xyzw.Any(), xyzw.None(), xyzw.All(value: 1), xyzw.Any(value: 1), xyzw.None(value: 1));
        _ = (affine.All(), affine.Any(), affine.None(), affine.All(value: 1), affine.Any(value: 1), affine.None(value: 1));
        _ = (matrix.All(), matrix.Any(), matrix.None(), matrix.All(value: 1), matrix.Any(value: 1), matrix.None(value: 1));

        _ = xy.AsSystemVector2().AsTerraFXVector2();
        _ = xyz.AsSystemVector3().AsTerraFXVector3();
        _ = xyzw.AsSystemVector4().AsTerraFXVector4();
        _ = quaternion.AsSystemQuaternion().AsTerraFXQuaternion();
        _ = affine.AsSystemMatrix3x2().AsTerraFXMatrix3x2();
        _ = matrix.AsSystemMatrix4x4().AsTerraFXMatrix4x4();
        _ = SystemNumericsExtensions.AsTerraFXVector2(value: xy.AsSystemVector2());
        _ = SystemNumericsExtensions.AsTerraFXVector3(value: xyz.AsSystemVector3());
        _ = SystemNumericsExtensions.AsTerraFXVector4(value: xyzw.AsSystemVector4());
        _ = SystemNumericsExtensions.AsTerraFXQuaternion(value: quaternion.AsSystemQuaternion());
        _ = SystemNumericsExtensions.AsTerraFXMatrix3x2(value: affine.AsSystemMatrix3x2());
        _ = SystemNumericsExtensions.AsTerraFXMatrix4x4(value: matrix.AsSystemMatrix4x4());

        _ = VectorOperators(xy);
        _ = VectorOperators(xyz);
        _ = VectorOperators(xyzw);
        _ = QuaternionOperators(quaternion);
        _ = Multiply(affine, Matrix3x2.Identity);
        _ = Multiply(matrix, Matrix4x4.Identity);
        _ = (2.0f * xy, 2.0f * xyz, 2.0f * xyzw, 2.0f * quaternion);
        _ = (Vector2.Dot(xy, xy), Vector2.Length(xy), Vector2.LengthSquared(xy), Vector2.Distance(xy, xy), Vector2.DistanceSquared(xy, xy));
        _ = (Vector3.Dot(xyz, xyz), Vector3.Length(xyz), Vector3.LengthSquared(xyz), Vector3.Distance(xyz, xyz), Vector3.DistanceSquared(xyz, xyz));
        _ = (Vector4.Dot(xyzw, xyzw), Vector4.Length(xyzw), Vector4.LengthSquared(xyzw), Vector4.Distance(xyzw, xyzw), Vector4.DistanceSquared(xyzw, xyzw));
        _ = (Vector2.Lerp(xy, xy, 2), Vector3.Lerp(xyz, xyz, 2), Vector4.Lerp(xyzw, xyzw, 2), Vector3.Cross(xyz, xyz));
        _ = (Vector2.Normalize(xy), Vector3.Normalize(xyz), Vector4.Normalize(xyzw), Quaternion.Normalize(quaternion));
        _ = Vector2.TryNormalize(value: xy, normalized: out _);
        _ = Vector3.TryNormalize(value: xyz, normalized: out _);
        _ = Vector4.TryNormalize(value: xyzw, normalized: out _);
        _ = Quaternion.TryNormalize(value: quaternion, normalized: out _);
        _ = (Vector2.TransformPoint(xy, affine), Vector2.TransformDirection(xy, affine), Vector3.TransformPoint(xyz, matrix), Vector3.TransformDirection(xyz, matrix), Vector3.Rotate(xyz, Quaternion.Identity), Vector4.Transform(xyzw, matrix));

        _ = (Quaternion.Dot(quaternion, quaternion), Quaternion.Multiply(quaternion, quaternion), Quaternion.Compose(quaternion, quaternion), Quaternion.Conjugate(quaternion), Quaternion.Invert(quaternion));
        _ = Quaternion.TryInvert(value: quaternion, inverse: out _);
        _ = (Quaternion.Lerp(Quaternion.Identity, Quaternion.Identity, 0.5f), Quaternion.Slerp(Quaternion.Identity, Quaternion.Identity, 0.5f));
        _ = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, default(float));
        _ = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, default(Angle));
        _ = Matrix3x2.CreateRotation(default(float));
        _ = Matrix3x2.CreateRotation(default(Angle));
        _ = (Matrix3x2.CreateTranslation(xy), Matrix3x2.CreateScale(xy), Matrix3x2.CreateScale(2.0f), Matrix3x2.Multiply(affine, affine), Matrix3x2.Invert(affine));
        _ = Matrix3x2.TryInvert(value: affine, inverse: out _);

        _ = (Matrix4x4.CreateTranslation(xyz), Matrix4x4.CreateScale(xyz), Matrix4x4.CreateScale(2.0f), Matrix4x4.Multiply(matrix, matrix), Matrix4x4.Transpose(matrix), Matrix4x4.Invert(matrix));
        _ = Matrix4x4.TryInvert(value: matrix, inverse: out _);
        _ = Matrix4x4.TryDecompose(value: matrix, scale: out _, rotation: out _, translation: out _);
        _ = (Matrix4x4.CreateRotationX(radians: 0), Matrix4x4.CreateRotationY(radians: 0), Matrix4x4.CreateRotationZ(radians: 0));
        _ = (Matrix4x4.CreateRotationX(default(Angle)), Matrix4x4.CreateRotationY(default(Angle)), Matrix4x4.CreateRotationZ(default(Angle)));
        _ = Matrix4x4.CreateFromQuaternion(rotation: Quaternion.Identity);
        _ = Matrix4x4.CreateFromAxisAngle(unitAxis: Vector3.UnitX, radians: 0);
        _ = Matrix4x4.CreateFromAxisAngle(Vector3.UnitX, default(Angle));
        _ = Matrix4x4.CreatePerspectiveFieldOfView(fieldOfView: 1, aspectRatio: 1, nearPlane: 0.1f, farPlane: 100);
        _ = Matrix4x4.CreateInfinitePerspectiveFieldOfView(fieldOfView: 1, aspectRatio: 1, nearPlane: 0.1f);
        _ = Matrix4x4.CreateOrthographic(width: 2, height: 2, nearPlane: -1, farPlane: 1);
        _ = Matrix4x4.CreateLookTo(position: Vector3.Zero, direction: Vector3.UnitZ, up: Vector3.UnitY);
        _ = Matrix4x4.CreateLookAt(position: Vector3.Zero, target: Vector3.UnitZ, up: Vector3.UnitY);
        _ = Matrix4x4.TryCreateLookTo(position: Vector3.Zero, direction: Vector3.UnitZ, up: Vector3.UnitY, view: out _);
        _ = Matrix4x4.TryCreateLookAt(position: Vector3.Zero, target: Vector3.UnitZ, up: Vector3.UnitY, view: out _);

        Format(xy);
        Format(xyz);
        Format(xyzw);
        Format(quaternion);
        Format(affine);
        Format(matrix);
        return xy.IsFinite().All() && xyz.IsFinite().All() && xyzw.IsFinite().All() && quaternion.IsFinite().All() && affine.IsFinite().All() && matrix.IsFinite().All();
    }

    public static T Multiply<T>(T left, T right)
        where T : IMultiplyOperators<T, T, T>, IEqualityOperators<T, T, bool> => left * right;

    public static T QuaternionOperators<T>(T value)
        where T : IAdditionOperators<T, T, T>, ISubtractionOperators<T, T, T>, IUnaryNegationOperators<T, T>, IMultiplyOperators<T, T, T>, IMultiplyOperators<T, float, T>, IEqualityOperators<T, T, bool>
    {
        _ = value == value + value;
        _ = value != value - value;
        return -value * value * 2.0f;
    }

    public static T VectorOperators<T>(T value)
        where T : IAdditionOperators<T, T, T>, ISubtractionOperators<T, T, T>, IUnaryNegationOperators<T, T>, IMultiplyOperators<T, T, T>, IDivisionOperators<T, T, T>, IMultiplyOperators<T, float, T>, IDivisionOperators<T, float, T>, IEqualityOperators<T, T, bool>
    {
        _ = value == value + value;
        _ = value != value - value;
        return -value * value / value * 2.0f / 2.0f;
    }

    private static void Format<T>(T value)
        where T : IEquatable<T>, IFormattable, ISpanFormattable, IUtf8SpanFormattable
    {
        _ = value.Equals(value);
        _ = value.ToString(format: null, formatProvider: null);
        var chars = RentedBuffer.Create<char>(1024, out var rentedChars);
        var bytes = RentedBuffer.Create<byte>(1024, out var rentedBytes);

        try
        {
            _ = value.TryFormat(destination: chars, charsWritten: out _, format: "F2", provider: null);
            _ = value.TryFormat(utf8Destination: bytes, bytesWritten: out _, format: "F2", provider: null);
        }
        finally
        {
            rentedBytes.Dispose();
            rentedChars.Dispose();
        }
    }
}
