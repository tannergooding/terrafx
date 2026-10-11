// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace TerraFX.Numerics;

/// <summary>A raw binary32 Hamilton quaternion with direct X/Y/Z/W lanes and scalar W.</summary>
public readonly struct Quaternion
    : IEquatable<Quaternion>,
      IFormattable,
      ISpanFormattable,
      IUtf8SpanFormattable,
      IAdditionOperators<Quaternion, Quaternion, Quaternion>,
      ISubtractionOperators<Quaternion, Quaternion, Quaternion>,
      IUnaryNegationOperators<Quaternion, Quaternion>,
      IMultiplyOperators<Quaternion, float, Quaternion>,
      IEqualityOperators<Quaternion, Quaternion, bool>,
      IMultiplyOperators<Quaternion, Quaternion, Quaternion>
{
    private readonly Vector128<float> _value;

    /// <summary>Initializes the vector and scalar parts without arithmetic.</summary>
    /// <param name="vector">The X, Y, and Z components.</param>
    /// <param name="scalar">The W component.</param>
    public Quaternion(Vector3 vector, float scalar)
    {
        _value = Vector128.Create(vector.X, vector.Y, vector.Z, scalar);
    }

    /// <summary>Initializes a quaternion without normalization, preserving supplied bits.</summary>
    /// <param name="x">The X component.</param>
    /// <param name="y">The Y component.</param>
    /// <param name="z">The Z component.</param>
    /// <param name="w">The scalar component.</param>
    public Quaternion(float x, float y, float z, float w)
    {
        _value = Vector128.Create(x, y, z, w);
    }

    private Quaternion(Vector128<float> value)
    {
        _value = value;
    }

    /// <summary>Gets the explicit identity rotation; the raw default is zero.</summary>
    public static Quaternion Identity => new Quaternion(0.0f, 0.0f, 0.0f, 1.0f);

    /// <summary>Gets the scalar W component.</summary>
    public float W => _value.GetElement(3);

    /// <summary>Gets the X component.</summary>
    public float X => _value.GetElement(0);

    /// <summary>Gets the Y component.</summary>
    public float Y => _value.GetElement(1);

    /// <summary>Gets the Z component.</summary>
    public float Z => _value.GetElement(2);

    /// <summary>Negates IEEE component equality.</summary>
    public static bool operator !=(Quaternion left, Quaternion right) => !(left == right);

    /// <summary>Computes the Hamilton product, applying right then left for unit rotations.</summary>
    public static Quaternion operator *(Quaternion left, Quaternion right) => Multiply(left, right);

    /// <summary>Multiplies every component by a scalar.</summary>
    public static Quaternion operator *(Quaternion value, float scalar) => new Quaternion(value._value * Vector128.Create(scalar));

    /// <summary>Multiplies every component by a scalar.</summary>
    public static Quaternion operator *(float scalar, Quaternion value) => value * scalar;

    /// <summary>Adds corresponding components with ordinary binary32 arithmetic.</summary>
    public static Quaternion operator +(Quaternion left, Quaternion right) => new Quaternion(left._value + right._value);

    /// <summary>Subtracts corresponding components with ordinary binary32 arithmetic.</summary>
    public static Quaternion operator -(Quaternion left, Quaternion right) => new Quaternion(left._value - right._value);

    /// <summary>Negates every component without canonicalizing the rotation sign.</summary>
    public static Quaternion operator -(Quaternion value) => new Quaternion(-value._value);

    /// <summary>Compares components using IEEE equality; NaN never compares equal.</summary>
    public static bool operator ==(Quaternion left, Quaternion right) => left.X == right.X && left.Y == right.Y && left.Z == right.Z && left.W == right.W;

    /// <summary>Composes unit rotations in application order: first, then second.</summary>
    public static Quaternion Compose(Quaternion first, Quaternion second) => Multiply(second, first);

    /// <summary>Negates XYZ and preserves W, including operation-specific zero signs.</summary>
    public static Quaternion Conjugate(Quaternion value) => new Quaternion(-value.X, -value.Y, -value.Z, value.W);

    /// <summary>Creates a rotation, halving the exact angle definition before argument reduction and rounding.</summary>
    /// <exception cref="ArgumentException">The axis is nonfinite/nonunit or the angle is nonfinite.</exception>
    public static Quaternion CreateFromAxisAngle(Vector3 unitAxis, Angle angle)
    {
        ThrowHelper.ThrowIfNotUnitAxis(unitAxis);
        ThrowHelper.ThrowIfNotFinite(angle);

        (var sin, var cos) = angle.HalfSinCos();
        return new Quaternion(unitAxis * sin, cos);
    }

    /// <summary>Creates a rotation from a finite unit axis and finite radians.</summary>
    /// <exception cref="ArgumentException">The axis is nonfinite/nonunit or radians are nonfinite.</exception>
    public static Quaternion CreateFromAxisAngle(Vector3 unitAxis, float radians)
    {
        ThrowHelper.ThrowIfNotUnitAxis(unitAxis);
        ThrowHelper.ThrowIfNotFinite(radians);
        return System.Numerics.Quaternion.CreateFromAxisAngle(unitAxis.AsSystemVector3(), radians).AsTerraFXQuaternion();
    }

    /// <summary>Computes the ordinary binary32 dot product.</summary>
    public static float Dot(Quaternion left, Quaternion right) => System.Numerics.Quaternion.Dot(left.AsSystemQuaternion(), right.AsSystemQuaternion());

    /// <summary>Obtains a finite Hamilton inverse with certified component representability.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is nonfinite or has no finite representable inverse.</exception>
    public static Quaternion Invert(Quaternion value)
    {
        if (!TryInvert(value, out var inverse))
        {
            ThrowHelper.ThrowArgumentException("The quaternion has no finite representable inverse.", nameof(value));
        }

        return inverse;
    }

    /// <summary>Interpolates unit quaternions on the shortest arc without clamping or positive-W canonicalization.</summary>
    public static Quaternion Lerp(Quaternion first, Quaternion second, float amount) => System.Numerics.Quaternion.Lerp(first.AsSystemQuaternion(), second.AsSystemQuaternion(), amount).AsTerraFXQuaternion();

    /// <summary>Computes the Hamilton product, applying right then left for unit rotations.</summary>
    public static Quaternion Multiply(Quaternion left, Quaternion right) => System.Numerics.Quaternion.Multiply(left.AsSystemQuaternion(), right.AsSystemQuaternion()).AsTerraFXQuaternion();

    /// <summary>Normalizes a finite nonzero quaternion without sign canonicalization.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is nonfinite or zero.</exception>
    public static Quaternion Normalize(Quaternion value)
    {
        if (!TryNormalize(value, out var normalized))
        {
            ThrowHelper.ThrowArgumentException("The quaternion must be nonzero.", nameof(value));
        }

        return normalized;
    }

    /// <summary>Spherically interpolates unit quaternions on the shortest arc without clamping.</summary>
    public static Quaternion Slerp(Quaternion first, Quaternion second, float amount) => System.Numerics.Quaternion.Slerp(first.AsSystemQuaternion(), second.AsSystemQuaternion(), amount).AsTerraFXQuaternion();

    /// <summary>Returns false and a default output only when no finite rounded inverse exists.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is nonfinite.</exception>
    public static bool TryInvert(Quaternion value, out Quaternion inverse)
    {
        ThrowHelper.ThrowIfNotFinite(value);
        inverse = default;

        var maximum = float.Max(float.Max(float.Abs(value.X), float.Abs(value.Y)), float.Max(float.Abs(value.Z), float.Abs(value.W)));

        if (maximum == 0.0f)
        {
            return false;
        }

        // Each exact inverse magnitude is <= 1/m. For normal m this is <= 2^126,
        // strictly below the binary32 overflow midpoint, certifying the scaled candidate.
        if (maximum >= float.ScaleB(1.0f, -126))
        {
            var x = value.X / maximum;
            var y = value.Y / maximum;
            var z = value.Z / maximum;
            var w = value.W / maximum;

            var sum = (x * x) + (y * y) + (z * z) + (w * w);
            inverse = new Quaternion(-x / sum / maximum, -y / sum / maximum, -z / sum / maximum, w / sum / maximum);
            return true;
        }

        // Subnormal m can straddle the overflow midpoint; round the bounded exact
        // inverse in fixed-width integer storage instead of trusting an overflowing candidate.
        return GeometryKernel.TryInvertQuaternion(value, out inverse);
    }

    /// <summary>Normalizes a finite quaternion; zero returns false and a default output.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is nonfinite.</exception>
    public static bool TryNormalize(Quaternion value, out Quaternion normalized)
    {
        ThrowHelper.ThrowIfNotFinite(value);
        var success = GeometryKernel.TryNormalize(value._value, out var result);
        normalized = new Quaternion(result);
        return success;
    }

    /// <summary>Copies this quaternion to its BCL counterpart without changing component bits.</summary>
    public System.Numerics.Quaternion AsSystemQuaternion()
    {
        // Direct X/Y/Z/W lanes match the reference-free BCL quaternion's sixteen-byte extent.
        unsafe
        {
            return Unsafe.BitCast<Quaternion, System.Numerics.Quaternion>(this);
        }
    }

    /// <inheritdoc />
    public bool Equals(Quaternion other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Quaternion other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);

    /// <summary>Classifies the X/Y/Z/W components as all-bits-set finite masks or zero masks.</summary>
    public Vector4 IsFinite() => new Vector4(Vector128.IsFinite(_value));

    /// <inheritdoc />
    public override string ToString() => ToString(null, null);

    /// <inheritdoc />
    public string ToString([StringSyntax("NumericFormat")] string? format, IFormatProvider? formatProvider) => GeometryFormatting.Format([X, Y, Z, W], 4, format, formatProvider);

    /// <inheritdoc />
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, [StringSyntax("NumericFormat")] ReadOnlySpan<char> format, IFormatProvider? provider) =>
        GeometryFormatting.TryFormat([X, Y, Z, W], 4, false, utf8Destination, out bytesWritten, format, provider);

    /// <inheritdoc />
    public bool TryFormat(Span<char> destination, out int charsWritten, [StringSyntax("NumericFormat")] ReadOnlySpan<char> format, IFormatProvider? provider) =>
        GeometryFormatting.TryFormat([X, Y, Z, W], 4, false, destination, out charsWritten, format, provider);
}
