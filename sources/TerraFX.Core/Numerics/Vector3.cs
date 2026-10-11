// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace TerraFX.Numerics;

/// <summary>A three-component binary32 vector with exactly twelve bytes of raw storage.</summary>
public readonly struct Vector3
    : IComparable<Vector3>,
      IEquatable<Vector3>,
      IFormattable,
      ISpanFormattable,
      IUtf8SpanFormattable,
      IAdditionOperators<Vector3, Vector3, Vector3>,
      ISubtractionOperators<Vector3, Vector3, Vector3>,
      IUnaryNegationOperators<Vector3, Vector3>,
      IMultiplyOperators<Vector3, float, Vector3>,
      IDivisionOperators<Vector3, float, Vector3>,
      IEqualityOperators<Vector3, Vector3, bool>,
      IComparisonOperators<Vector3, Vector3, bool>,
      IMultiplyOperators<Vector3, Vector3, Vector3>,
      IDivisionOperators<Vector3, Vector3, Vector3>
{
    private readonly System.Numerics.Vector3 _value;

    /// <summary>Initializes a vector from an XY vector and a Z component without arithmetic.</summary>
    /// <param name="xy">The X and Y components.</param>
    /// <param name="z">The Z component.</param>
    public Vector3(Vector2 xy, float z)
    {
        _value = new System.Numerics.Vector3(xy.X, xy.Y, z);
    }

    /// <summary>Initializes every component with the supplied value.</summary>
    /// <param name="value">The raw component value.</param>
    public Vector3(float value)
    {
        _value = new System.Numerics.Vector3(value);
    }

    /// <summary>Initializes a vector, preserving every supplied component bit.</summary>
    /// <param name="x">The X component.</param>
    /// <param name="y">The Y component.</param>
    /// <param name="z">The Z component.</param>
    public Vector3(float x, float y, float z)
    {
        _value = new System.Numerics.Vector3(x, y, z);
    }

    internal Vector3(System.Numerics.Vector3 value)
    {
        _value = value;
    }

    /// <summary>Gets the vector with every component equal to one.</summary>
    public static Vector3 One => new Vector3(1.0f);

    /// <summary>Gets the positive X unit vector.</summary>
    public static Vector3 UnitX => new Vector3(1.0f, 0.0f, 0.0f);

    /// <summary>Gets the positive Y unit vector.</summary>
    public static Vector3 UnitY => new Vector3(0.0f, 1.0f, 0.0f);

    /// <summary>Gets the positive Z unit vector.</summary>
    public static Vector3 UnitZ => new Vector3(0.0f, 0.0f, 1.0f);

    /// <summary>Gets the zero vector.</summary>
    public static Vector3 Zero => default;

    /// <summary>Gets the X component.</summary>
    public float X => _value.X;

    /// <summary>Gets the Y component.</summary>
    public float Y => _value.Y;

    /// <summary>Gets the Z component.</summary>
    public float Z => _value.Z;

    /// <summary>Negates IEEE component equality.</summary>
    public static bool operator !=(Vector3 left, Vector3 right) => !(left == right);

    /// <summary>Multiplies corresponding components.</summary>
    public static Vector3 operator *(Vector3 left, Vector3 right) => new Vector3(left._value * right._value);

    /// <summary>Multiplies every component by a scalar.</summary>
    public static Vector3 operator *(Vector3 value, float scalar) => new Vector3(value._value * scalar);

    /// <summary>Multiplies every component by a scalar.</summary>
    public static Vector3 operator *(float scalar, Vector3 value) => value * scalar;

    /// <summary>Adds corresponding components with ordinary binary32 arithmetic.</summary>
    public static Vector3 operator +(Vector3 left, Vector3 right) => new Vector3(left._value + right._value);

    /// <summary>Subtracts corresponding components with ordinary binary32 arithmetic.</summary>
    public static Vector3 operator -(Vector3 left, Vector3 right) => new Vector3(left._value - right._value);

    /// <summary>Negates every component.</summary>
    public static Vector3 operator -(Vector3 value) => new Vector3(-value._value);

    /// <summary>Divides corresponding components.</summary>
    public static Vector3 operator /(Vector3 left, Vector3 right) => new Vector3(left._value / right._value);

    /// <summary>Divides every component by a scalar.</summary>
    public static Vector3 operator /(Vector3 value, float scalar) => new Vector3(value._value / scalar);

    /// <summary>Compares vectors by magnitude, sign orthant, then direction.</summary>
    public static bool operator <(Vector3 left, Vector3 right) => left.CompareTo(right) < 0;

    /// <summary>Compares vectors by magnitude, sign orthant, then direction.</summary>
    public static bool operator <=(Vector3 left, Vector3 right) => left.CompareTo(right) <= 0;

    /// <summary>Compares components using IEEE equality; NaN never compares equal.</summary>
    public static bool operator ==(Vector3 left, Vector3 right) => left.X == right.X && left.Y == right.Y && left.Z == right.Z;

    /// <summary>Compares vectors by magnitude, sign orthant, then direction.</summary>
    public static bool operator >(Vector3 left, Vector3 right) => left.CompareTo(right) > 0;

    /// <summary>Compares vectors by magnitude, sign orthant, then direction.</summary>
    public static bool operator >=(Vector3 left, Vector3 right) => left.CompareTo(right) >= 0;

    /// <summary>Computes the BCL cross-product polynomial; Cross(UnitX, UnitY) is UnitZ.</summary>
    public static Vector3 Cross(Vector3 left, Vector3 right) => new Vector3(System.Numerics.Vector3.Cross(left._value, right._value));

    /// <summary>Computes the ordinary binary32 distance.</summary>
    public static float Distance(Vector3 left, Vector3 right) => System.Numerics.Vector3.Distance(left._value, right._value);

    /// <summary>Computes the ordinary binary32 squared distance.</summary>
    public static float DistanceSquared(Vector3 left, Vector3 right) => System.Numerics.Vector3.DistanceSquared(left._value, right._value);

    /// <summary>Computes the ordinary binary32 dot product.</summary>
    public static float Dot(Vector3 left, Vector3 right) => System.Numerics.Vector3.Dot(left._value, right._value);

    /// <summary>Computes the ordinary binary32 length.</summary>
    public static float Length(Vector3 value) => value._value.Length();

    /// <summary>Computes the ordinary binary32 squared length.</summary>
    public static float LengthSquared(Vector3 value) => value._value.LengthSquared();

    /// <summary>Linearly interpolates without clamping the amount.</summary>
    public static Vector3 Lerp(Vector3 first, Vector3 second, float amount) => new Vector3(System.Numerics.Vector3.Lerp(first._value, second._value, amount));

    /// <summary>Normalizes a finite nonzero vector using max-component scaling.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is nonfinite or zero.</exception>
    public static Vector3 Normalize(Vector3 value)
    {
        if (!TryNormalize(value, out var normalized))
        {
            ThrowHelper.ThrowArgumentException("The vector must be nonzero.", nameof(value));
        }

        return normalized;
    }

    /// <summary>Applies the raw quaternion matrix polynomial; rigid rotation requires caller-admitted unit input.</summary>
    public static Vector3 Rotate(Vector3 value, Quaternion rotation)
    {
        // The accepted 1 - 2*(yy + zz) polynomial is identity for a zero quaternion.
        // Unit-only optimized vector kernels need not preserve this raw-input behavior.
        return TransformDirection(value, Matrix4x4.CreateFromQuaternion(rotation));
    }

    /// <summary>Evaluates the linear row-vector polynomial, excluding translation.</summary>
    public static Vector3 TransformDirection(Vector3 direction, Matrix4x4 matrix) => new Vector3(System.Numerics.Vector3.TransformNormal(direction._value, matrix.AsSystemMatrix4x4()));

    /// <summary>Evaluates affine XYZ with input W equal to one, without projective division.</summary>
    public static Vector3 TransformPoint(Vector3 point, Matrix4x4 matrix) => new Vector3(System.Numerics.Vector3.Transform(point._value, matrix.AsSystemMatrix4x4()));

    /// <summary>Normalizes a finite vector; zero returns false and a default output.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is nonfinite.</exception>
    public static bool TryNormalize(Vector3 value, out Vector3 normalized)
    {
        ThrowHelper.ThrowIfNotFinite(value);
        var magnitude = System.Numerics.Vector3.Abs(value._value);
        var maximum = float.Max(magnitude.X, float.Max(magnitude.Y, magnitude.Z));

        if (maximum == 0.0f)
        {
            normalized = default;
            return false;
        }

        var scaled = value._value / maximum;
        var squares = scaled * scaled;
        normalized = new Vector3(scaled / float.Sqrt(squares.X + squares.Y + squares.Z));

        return true;
    }

    /// <summary>Tests whether every logical component has exactly the all-bits-set representation.</summary>
    public bool All() => (Vector128.ExtractMostSignificantBits(Vector128.Equals(_value.AsVector128().AsInt32(), Vector128.Create(-1))) & 0b111) == 0b111;

    /// <summary>Tests whether every component compares equal to a scalar under IEEE equality.</summary>
    /// <param name="value">The scalar to compare.</param>
    public bool All(float value) => X == value && Y == value && Z == value;

    /// <summary>Tests whether any logical component has exactly the all-bits-set representation.</summary>
    public bool Any() => Vector128.EqualsAny(_value.AsVector128().AsInt32(), Vector128.Create(-1));

    /// <summary>Tests whether any component compares equal to a scalar under IEEE equality.</summary>
    /// <param name="value">The scalar to compare.</param>
    public bool Any(float value) => X == value || Y == value || Z == value;

    /// <summary>Copies this vector to its BCL counterpart without changing component bits.</summary>
    public System.Numerics.Vector3 AsSystemVector3()
    {
        // The complete extent is twelve bytes; this does not borrow or load a fourth lane.
        unsafe
        {
            return Unsafe.BitCast<Vector3, System.Numerics.Vector3>(this);
        }
    }

    /// <inheritdoc />
    public int CompareTo(Vector3 other) => MagnitudeOrdering.Compare(_value, other._value);

    /// <inheritdoc />
    public bool Equals(Vector3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Vector3 other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    /// <summary>Classifies each component as an all-bits-set finite mask or a zero mask.</summary>
    public Vector3 IsFinite() => new Vector3(System.Numerics.Vector3.IsFinite(_value));

    /// <summary>Tests whether no logical component has exactly the all-bits-set representation.</summary>
    public bool None() => !Any();

    /// <summary>Tests whether no component compares equal to a scalar under IEEE equality.</summary>
    /// <param name="value">The scalar to compare.</param>
    public bool None(float value) => !Any(value);

    /// <inheritdoc />
    public override string ToString() => ToString(null, null);

    /// <inheritdoc />
    public string ToString([StringSyntax("NumericFormat")] string? format, IFormatProvider? formatProvider) => _value.ToString(format, formatProvider);

    /// <inheritdoc />
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, [StringSyntax("NumericFormat")] ReadOnlySpan<char> format, IFormatProvider? provider) =>
        GeometryFormatting.TryFormat([X, Y, Z], 3, true, utf8Destination, out bytesWritten, format, provider);

    /// <inheritdoc />
    public bool TryFormat(Span<char> destination, out int charsWritten, [StringSyntax("NumericFormat")] ReadOnlySpan<char> format, IFormatProvider? provider) =>
        GeometryFormatting.TryFormat([X, Y, Z], 3, true, destination, out charsWritten, format, provider);
}
