// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace TerraFX.Numerics;

/// <summary>A four-component binary32 vector storing X, Y, Z, W directly in four intrinsic lanes.</summary>
public readonly struct Vector4
    : IComparable<Vector4>,
      IEquatable<Vector4>,
      IFormattable,
      ISpanFormattable,
      IUtf8SpanFormattable,
      IAdditionOperators<Vector4, Vector4, Vector4>,
      ISubtractionOperators<Vector4, Vector4, Vector4>,
      IUnaryNegationOperators<Vector4, Vector4>,
      IMultiplyOperators<Vector4, float, Vector4>,
      IDivisionOperators<Vector4, float, Vector4>,
      IEqualityOperators<Vector4, Vector4, bool>,
      IComparisonOperators<Vector4, Vector4, bool>,
      IMultiplyOperators<Vector4, Vector4, Vector4>,
      IDivisionOperators<Vector4, Vector4, Vector4>
{
    private readonly Vector128<float> _value;

    /// <summary>Initializes XYZ and W without arithmetic.</summary>
    /// <param name="xyz">The X, Y, and Z components.</param>
    /// <param name="w">The W component.</param>
    public Vector4(Vector3 xyz, float w)
    {
        _value = Vector128.Create(xyz.X, xyz.Y, xyz.Z, w);
    }

    /// <summary>Initializes every component with the supplied value.</summary>
    /// <param name="value">The raw component value.</param>
    public Vector4(float value)
    {
        _value = Vector128.Create(value);
    }

    /// <summary>Initializes a vector, preserving every supplied component bit.</summary>
    /// <param name="x">The X component.</param>
    /// <param name="y">The Y component.</param>
    /// <param name="z">The Z component.</param>
    /// <param name="w">The W component.</param>
    public Vector4(float x, float y, float z, float w)
    {
        _value = Vector128.Create(x, y, z, w);
    }

    internal Vector4(Vector128<float> value)
    {
        _value = value;
    }

    /// <summary>Gets the vector with every component equal to one.</summary>
    public static Vector4 One => new Vector4(1.0f);

    /// <summary>Gets the positive W unit vector.</summary>
    public static Vector4 UnitW => new Vector4(0.0f, 0.0f, 0.0f, 1.0f);

    /// <summary>Gets the positive X unit vector.</summary>
    public static Vector4 UnitX => new Vector4(1.0f, 0.0f, 0.0f, 0.0f);

    /// <summary>Gets the positive Y unit vector.</summary>
    public static Vector4 UnitY => new Vector4(0.0f, 1.0f, 0.0f, 0.0f);

    /// <summary>Gets the positive Z unit vector.</summary>
    public static Vector4 UnitZ => new Vector4(0.0f, 0.0f, 1.0f, 0.0f);

    /// <summary>Gets the zero vector.</summary>
    public static Vector4 Zero => default;

    /// <summary>Gets the W component.</summary>
    public float W => _value.GetElement(3);

    /// <summary>Gets the X component.</summary>
    public float X => _value.GetElement(0);

    /// <summary>Gets the Y component.</summary>
    public float Y => _value.GetElement(1);

    /// <summary>Gets the Z component.</summary>
    public float Z => _value.GetElement(2);

    /// <summary>Negates IEEE component equality.</summary>
    public static bool operator !=(Vector4 left, Vector4 right) => !(left == right);

    /// <summary>Multiplies corresponding components.</summary>
    public static Vector4 operator *(Vector4 left, Vector4 right) => new Vector4(left._value * right._value);

    /// <summary>Multiplies every component by a scalar.</summary>
    public static Vector4 operator *(Vector4 value, float scalar) => new Vector4(value._value * Vector128.Create(scalar));

    /// <summary>Multiplies every component by a scalar.</summary>
    public static Vector4 operator *(float scalar, Vector4 value) => value * scalar;

    /// <summary>Adds corresponding components with ordinary binary32 arithmetic.</summary>
    public static Vector4 operator +(Vector4 left, Vector4 right) => new Vector4(left._value + right._value);

    /// <summary>Subtracts corresponding components with ordinary binary32 arithmetic.</summary>
    public static Vector4 operator -(Vector4 left, Vector4 right) => new Vector4(left._value - right._value);

    /// <summary>Negates every component.</summary>
    public static Vector4 operator -(Vector4 value) => new Vector4(-value._value);

    /// <summary>Divides corresponding components.</summary>
    public static Vector4 operator /(Vector4 left, Vector4 right) => new Vector4(left._value / right._value);

    /// <summary>Divides every component by a scalar.</summary>
    public static Vector4 operator /(Vector4 value, float scalar) => new Vector4(value._value / Vector128.Create(scalar));

    /// <summary>Compares vectors by magnitude, sign orthant, then direction.</summary>
    public static bool operator <(Vector4 left, Vector4 right) => left.CompareTo(right) < 0;

    /// <summary>Compares vectors by magnitude, sign orthant, then direction.</summary>
    public static bool operator <=(Vector4 left, Vector4 right) => left.CompareTo(right) <= 0;

    /// <summary>Compares components using IEEE equality; NaN never compares equal.</summary>
    public static bool operator ==(Vector4 left, Vector4 right) => left.X == right.X && left.Y == right.Y && left.Z == right.Z && left.W == right.W;

    /// <summary>Compares vectors by magnitude, sign orthant, then direction.</summary>
    public static bool operator >(Vector4 left, Vector4 right) => left.CompareTo(right) > 0;

    /// <summary>Compares vectors by magnitude, sign orthant, then direction.</summary>
    public static bool operator >=(Vector4 left, Vector4 right) => left.CompareTo(right) >= 0;

    /// <summary>Computes the ordinary binary32 distance.</summary>
    public static float Distance(Vector4 left, Vector4 right) => System.Numerics.Vector4.Distance(left.AsSystemVector4(), right.AsSystemVector4());

    /// <summary>Computes the ordinary binary32 squared distance.</summary>
    public static float DistanceSquared(Vector4 left, Vector4 right) => System.Numerics.Vector4.DistanceSquared(left.AsSystemVector4(), right.AsSystemVector4());

    /// <summary>Computes the ordinary binary32 dot product.</summary>
    public static float Dot(Vector4 left, Vector4 right) => System.Numerics.Vector4.Dot(left.AsSystemVector4(), right.AsSystemVector4());

    /// <summary>Computes the ordinary binary32 length.</summary>
    public static float Length(Vector4 value) => value.AsSystemVector4().Length();

    /// <summary>Computes the ordinary binary32 squared length.</summary>
    public static float LengthSquared(Vector4 value) => value.AsSystemVector4().LengthSquared();

    /// <summary>Linearly interpolates without clamping the amount.</summary>
    public static Vector4 Lerp(Vector4 first, Vector4 second, float amount) => System.Numerics.Vector4.Lerp(first.AsSystemVector4(), second.AsSystemVector4(), amount).AsTerraFXVector4();

    /// <summary>Normalizes a finite nonzero vector using max-component scaling.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is nonfinite or zero.</exception>
    public static Vector4 Normalize(Vector4 value)
    {
        if (!TryNormalize(value, out var normalized))
        {
            ThrowHelper.ThrowArgumentException("The vector must be nonzero.", nameof(value));
        }

        return normalized;
    }

    /// <summary>Evaluates the homogeneous row-vector transform, retaining output W.</summary>
    public static Vector4 Transform(Vector4 value, Matrix4x4 matrix) => System.Numerics.Vector4.Transform(value.AsSystemVector4(), matrix.AsSystemMatrix4x4()).AsTerraFXVector4();

    /// <summary>Normalizes a finite vector; zero returns false and a default output.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is nonfinite.</exception>
    public static bool TryNormalize(Vector4 value, out Vector4 normalized)
    {
        ThrowHelper.ThrowIfNotFinite(value);
        var success = GeometryKernel.TryNormalize(value._value, out var result);
        normalized = new Vector4(result);
        return success;
    }

    /// <summary>Tests whether every component has exactly the all-bits-set representation.</summary>
    public bool All() => Vector128.EqualsAll(_value.AsInt32(), Vector128.Create(-1));

    /// <summary>Tests whether every component compares equal to a scalar under IEEE equality.</summary>
    /// <param name="value">The scalar to compare.</param>
    public bool All(float value) => Vector128.EqualsAll(_value, Vector128.Create(value));

    /// <summary>Tests whether any component has exactly the all-bits-set representation.</summary>
    public bool Any() => Vector128.EqualsAny(_value.AsInt32(), Vector128.Create(-1));

    /// <summary>Tests whether any component compares equal to a scalar under IEEE equality.</summary>
    /// <param name="value">The scalar to compare.</param>
    public bool Any(float value) => Vector128.EqualsAny(_value, Vector128.Create(value));

    /// <summary>Copies this vector to its BCL counterpart without changing component bits.</summary>
    public System.Numerics.Vector4 AsSystemVector4()
    {
        // Both reference-free values contain exactly X/Y/Z/W at offsets 0/4/8/12.
        unsafe
        {
            return Unsafe.BitCast<Vector4, System.Numerics.Vector4>(this);
        }
    }

    /// <inheritdoc />
    public int CompareTo(Vector4 other) => MagnitudeOrdering.Compare(_value, other._value);

    /// <inheritdoc />
    public bool Equals(Vector4 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Vector4 other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);

    /// <summary>Classifies each component as an all-bits-set finite mask or a zero mask.</summary>
    public Vector4 IsFinite() => new Vector4(Vector128.IsFinite(_value));

    /// <summary>Tests whether no component has exactly the all-bits-set representation.</summary>
    public bool None() => !Any();

    /// <summary>Tests whether no component compares equal to a scalar under IEEE equality.</summary>
    /// <param name="value">The scalar to compare.</param>
    public bool None(float value) => !Any(value);

    /// <inheritdoc />
    public override string ToString() => ToString(null, null);

    /// <inheritdoc />
    public string ToString([StringSyntax("NumericFormat")] string? format, IFormatProvider? formatProvider) => AsSystemVector4().ToString(format, formatProvider);

    /// <inheritdoc />
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, [StringSyntax("NumericFormat")] ReadOnlySpan<char> format, IFormatProvider? provider) =>
        GeometryFormatting.TryFormat([X, Y, Z, W], 4, true, utf8Destination, out bytesWritten, format, provider);

    /// <inheritdoc />
    public bool TryFormat(Span<char> destination, out int charsWritten, [StringSyntax("NumericFormat")] ReadOnlySpan<char> format, IFormatProvider? provider) =>
        GeometryFormatting.TryFormat([X, Y, Z, W], 4, true, destination, out charsWritten, format, provider);
}
