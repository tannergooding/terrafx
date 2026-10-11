// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace TerraFX.Numerics;

/// <summary>A two-component binary32 vector with raw IEEE component storage.</summary>
public readonly struct Vector2
    : IComparable<Vector2>,
      IEquatable<Vector2>,
      IFormattable,
      ISpanFormattable,
      IUtf8SpanFormattable,
      IAdditionOperators<Vector2, Vector2, Vector2>,
      ISubtractionOperators<Vector2, Vector2, Vector2>,
      IUnaryNegationOperators<Vector2, Vector2>,
      IMultiplyOperators<Vector2, float, Vector2>,
      IDivisionOperators<Vector2, float, Vector2>,
      IEqualityOperators<Vector2, Vector2, bool>,
      IComparisonOperators<Vector2, Vector2, bool>,
      IMultiplyOperators<Vector2, Vector2, Vector2>,
      IDivisionOperators<Vector2, Vector2, Vector2>
{
    private readonly System.Numerics.Vector2 _value;

    /// <summary>Initializes every component with the supplied value.</summary>
    /// <param name="value">The raw component value.</param>
    public Vector2(float value)
    {
        _value = new System.Numerics.Vector2(value);
    }

    /// <summary>Initializes a vector, preserving every supplied component bit.</summary>
    /// <param name="x">The X component.</param>
    /// <param name="y">The Y component.</param>
    public Vector2(float x, float y)
    {
        _value = new System.Numerics.Vector2(x, y);
    }

    internal Vector2(System.Numerics.Vector2 value)
    {
        _value = value;
    }

    /// <summary>Gets the vector with every component equal to one.</summary>
    public static Vector2 One => new Vector2(1.0f);

    /// <summary>Gets the positive X unit vector.</summary>
    public static Vector2 UnitX => new Vector2(1.0f, 0.0f);

    /// <summary>Gets the positive Y unit vector.</summary>
    public static Vector2 UnitY => new Vector2(0.0f, 1.0f);

    /// <summary>Gets the zero vector.</summary>
    public static Vector2 Zero => default;

    /// <summary>Gets the X component.</summary>
    public float X => _value.X;

    /// <summary>Gets the Y component.</summary>
    public float Y => _value.Y;

    /// <summary>Negates IEEE component equality.</summary>
    public static bool operator !=(Vector2 left, Vector2 right) => !(left == right);

    /// <summary>Multiplies corresponding components.</summary>
    public static Vector2 operator *(Vector2 left, Vector2 right) => new Vector2(left._value * right._value);

    /// <summary>Multiplies every component by a scalar.</summary>
    public static Vector2 operator *(Vector2 value, float scalar) => new Vector2(value._value * scalar);

    /// <summary>Multiplies every component by a scalar.</summary>
    public static Vector2 operator *(float scalar, Vector2 value) => value * scalar;

    /// <summary>Adds corresponding components with ordinary binary32 arithmetic.</summary>
    public static Vector2 operator +(Vector2 left, Vector2 right) => new Vector2(left._value + right._value);

    /// <summary>Subtracts corresponding components with ordinary binary32 arithmetic.</summary>
    public static Vector2 operator -(Vector2 left, Vector2 right) => new Vector2(left._value - right._value);

    /// <summary>Negates every component.</summary>
    public static Vector2 operator -(Vector2 value) => new Vector2(-value._value);

    /// <summary>Divides corresponding components.</summary>
    public static Vector2 operator /(Vector2 left, Vector2 right) => new Vector2(left._value / right._value);

    /// <summary>Divides every component by a scalar.</summary>
    public static Vector2 operator /(Vector2 value, float scalar) => new Vector2(value._value / scalar);

    /// <summary>Compares vectors by magnitude, sign orthant, then direction.</summary>
    public static bool operator <(Vector2 left, Vector2 right) => left.CompareTo(right) < 0;

    /// <summary>Compares vectors by magnitude, sign orthant, then direction.</summary>
    public static bool operator <=(Vector2 left, Vector2 right) => left.CompareTo(right) <= 0;

    /// <summary>Compares components using IEEE equality; NaN never compares equal.</summary>
    public static bool operator ==(Vector2 left, Vector2 right) => left.X == right.X && left.Y == right.Y;

    /// <summary>Compares vectors by magnitude, sign orthant, then direction.</summary>
    public static bool operator >(Vector2 left, Vector2 right) => left.CompareTo(right) > 0;

    /// <summary>Compares vectors by magnitude, sign orthant, then direction.</summary>
    public static bool operator >=(Vector2 left, Vector2 right) => left.CompareTo(right) >= 0;

    /// <summary>Computes the ordinary binary32 distance.</summary>
    public static float Distance(Vector2 left, Vector2 right) => System.Numerics.Vector2.Distance(left._value, right._value);

    /// <summary>Computes the ordinary binary32 squared distance.</summary>
    public static float DistanceSquared(Vector2 left, Vector2 right) => System.Numerics.Vector2.DistanceSquared(left._value, right._value);

    /// <summary>Computes the ordinary binary32 dot product.</summary>
    public static float Dot(Vector2 left, Vector2 right) => System.Numerics.Vector2.Dot(left._value, right._value);

    /// <summary>Computes the ordinary binary32 length.</summary>
    public static float Length(Vector2 value) => value._value.Length();

    /// <summary>Computes the ordinary binary32 squared length.</summary>
    public static float LengthSquared(Vector2 value) => value._value.LengthSquared();

    /// <summary>Linearly interpolates without clamping the amount.</summary>
    public static Vector2 Lerp(Vector2 first, Vector2 second, float amount) => new Vector2(System.Numerics.Vector2.Lerp(first._value, second._value, amount));

    /// <summary>Normalizes a finite nonzero vector using max-component scaling.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is nonfinite or zero.</exception>
    public static Vector2 Normalize(Vector2 value)
    {
        if (!TryNormalize(value, out var normalized))
        {
            ThrowHelper.ThrowArgumentException("The vector must be nonzero.", nameof(value));
        }

        return normalized;
    }

    /// <summary>Evaluates the linear row-vector polynomial, excluding translation.</summary>
    public static Vector2 TransformDirection(Vector2 direction, Matrix3x2 matrix) => new Vector2(System.Numerics.Vector2.TransformNormal(direction._value, matrix.AsSystemMatrix3x2()));

    /// <summary>Evaluates an affine row-vector point transform, including translation.</summary>
    public static Vector2 TransformPoint(Vector2 point, Matrix3x2 matrix) => new Vector2(System.Numerics.Vector2.Transform(point._value, matrix.AsSystemMatrix3x2()));

    /// <summary>Normalizes a finite vector; zero returns false and a default output.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is nonfinite.</exception>
    public static bool TryNormalize(Vector2 value, out Vector2 normalized)
    {
        ThrowHelper.ThrowIfNotFinite(value);
        var magnitude = System.Numerics.Vector2.Abs(value._value);
        var maximum = float.Max(magnitude.X, magnitude.Y);

        if (maximum == 0.0f)
        {
            normalized = default;
            return false;
        }

        var scaled = value._value / maximum;
        normalized = new Vector2(scaled / float.Sqrt(scaled.LengthSquared()));

        return true;
    }

    /// <summary>Tests whether every component has exactly the all-bits-set representation.</summary>
    public bool All() => (Vector128.ExtractMostSignificantBits(Vector128.Equals(_value.AsVector128().AsInt32(), Vector128.Create(-1))) & 0b11) == 0b11;

    /// <summary>Tests whether every component compares equal to a scalar under IEEE equality.</summary>
    /// <param name="value">The scalar to compare.</param>
    public bool All(float value) => X == value && Y == value;

    /// <summary>Tests whether any component has exactly the all-bits-set representation.</summary>
    public bool Any() => Vector128.EqualsAny(_value.AsVector128().AsInt32(), Vector128.Create(-1));

    /// <summary>Tests whether any component compares equal to a scalar under IEEE equality.</summary>
    /// <param name="value">The scalar to compare.</param>
    public bool Any(float value) => X == value || Y == value;

    /// <summary>Copies this vector to its BCL counterpart without changing component bits.</summary>
    /// <returns>An independent vector with identical component bits.</returns>
    public System.Numerics.Vector2 AsSystemVector2()
    {
        // Both sequential, reference-free values have exactly two binary32 components.
        unsafe
        {
            return Unsafe.BitCast<Vector2, System.Numerics.Vector2>(this);
        }
    }

    /// <inheritdoc />
    public int CompareTo(Vector2 other) => MagnitudeOrdering.Compare(_value, other._value);

    /// <inheritdoc />
    public bool Equals(Vector2 other) => X.Equals(other.X) && Y.Equals(other.Y);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Vector2 other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y);

    /// <summary>Classifies each component as an all-bits-set finite mask or a zero mask.</summary>
    /// <returns>The component-wise finite mask.</returns>
    public Vector2 IsFinite() => new Vector2(System.Numerics.Vector2.IsFinite(_value));

    /// <summary>Tests whether no component has exactly the all-bits-set representation.</summary>
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
        GeometryFormatting.TryFormat([X, Y], 2, true, utf8Destination, out bytesWritten, format, provider);

    /// <inheritdoc />
    public bool TryFormat(Span<char> destination, out int charsWritten, [StringSyntax("NumericFormat")] ReadOnlySpan<char> format, IFormatProvider? provider) =>
        GeometryFormatting.TryFormat([X, Y], 2, true, destination, out charsWritten, format, provider);
}
