// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace TerraFX.Numerics;

/// <summary>A row-vector affine matrix composed of three engine Vector2 rows.</summary>
public readonly struct Matrix3x2
    : IEquatable<Matrix3x2>,
      IFormattable,
      ISpanFormattable,
      IUtf8SpanFormattable,
      IMultiplyOperators<Matrix3x2, Matrix3x2, Matrix3x2>,
      IEqualityOperators<Matrix3x2, Matrix3x2, bool>
{
    private readonly Vector2 _row1;
    private readonly Vector2 _row2;
    private readonly Vector2 _row3;

    /// <summary>Initializes raw rows without validation or arithmetic.</summary>
    /// <param name="row1">The first linear row.</param>
    /// <param name="row2">The second linear row.</param>
    /// <param name="row3">The translation row.</param>
    public Matrix3x2(Vector2 row1, Vector2 row2, Vector2 row3)
    {
        _row1 = row1;
        _row2 = row2;
        _row3 = row3;
    }

    /// <summary>Gets the explicit identity transform; the raw default is zero.</summary>
    public static Matrix3x2 Identity => new Matrix3x2(Vector2.UnitX, Vector2.UnitY, Vector2.Zero);

    /// <summary>Gets a copy of the first row.</summary>
    public Vector2 Row1 => _row1;

    /// <summary>Gets a copy of the second row.</summary>
    public Vector2 Row2 => _row2;

    /// <summary>Gets a copy of the translation row.</summary>
    public Vector2 Row3 => _row3;

    /// <summary>Gets an element using zero-based row and column indices.</summary>
    /// <param name="row">The row in [0, 2].</param>
    /// <param name="column">The column in [0, 1].</param>
    /// <exception cref="ArgumentOutOfRangeException">An index is outside the matrix extent.</exception>
    public float this[int row, int column]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(row);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(row, 2);
            ArgumentOutOfRangeException.ThrowIfNegative(column);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(column, 1);
            var selected = row switch {
                0 => _row1,
                1 => _row2,
                _ => _row3
            };
            return column == 0 ? selected.X : selected.Y;
        }
    }

    /// <summary>Negates IEEE component equality.</summary>
    public static bool operator !=(Matrix3x2 left, Matrix3x2 right) => !(left == right);

    /// <summary>Multiplies in row-vector application order.</summary>
    public static Matrix3x2 operator *(Matrix3x2 left, Matrix3x2 right) => Multiply(left, right);

    /// <summary>Compares every component using IEEE equality.</summary>
    public static bool operator ==(Matrix3x2 left, Matrix3x2 right) => left._row1 == right._row1 && left._row2 == right._row2 && left._row3 == right._row3;

    /// <summary>Creates a row-vector rotation by evaluating the retained angle definition.</summary>
    /// <exception cref="ArgumentException"><paramref name="angle"/> is nonfinite.</exception>
    public static Matrix3x2 CreateRotation(Angle angle)
    {
        ThrowHelper.ThrowIfNotFinite(angle);
        (var sin, var cos) = angle.SinCos();
        return new Matrix3x2(new Vector2(cos, sin), new Vector2(-sin, cos), Vector2.Zero);
    }

    /// <summary>Creates the BCL row-vector rotation for finite radians.</summary>
    /// <exception cref="ArgumentException"><paramref name="radians"/> is nonfinite.</exception>
    public static Matrix3x2 CreateRotation(float radians)
    {
        ThrowHelper.ThrowIfNotFinite(radians);
        return System.Numerics.Matrix3x2.CreateRotation(radians).AsTerraFXMatrix3x2();
    }

    /// <summary>Creates raw component-wise scale.</summary>
    public static Matrix3x2 CreateScale(Vector2 scale) => new Matrix3x2(new Vector2(scale.X, 0.0f), new Vector2(0.0f, scale.Y), Vector2.Zero);

    /// <summary>Creates raw uniform scale.</summary>
    public static Matrix3x2 CreateScale(float scale) => CreateScale(new Vector2(scale));

    /// <summary>Creates a raw affine translation in the third row.</summary>
    public static Matrix3x2 CreateTranslation(Vector2 translation) => new Matrix3x2(Vector2.UnitX, Vector2.UnitY, translation);

    /// <summary>Obtains a finite inverse passing both exact homogeneous 3x3 residual checks.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is nonfinite or has no admitted inverse.</exception>
    public static Matrix3x2 Invert(Matrix3x2 value)
    {
        if (!TryInvert(value, out var inverse))
        {
            ThrowHelper.ThrowArgumentException("The matrix has no admitted inverse.", nameof(value));
        }

        return inverse;
    }

    /// <summary>Multiplies in row-vector order, applying left then right.</summary>
    public static Matrix3x2 Multiply(Matrix3x2 left, Matrix3x2 right) => System.Numerics.Matrix3x2.Multiply(left.AsSystemMatrix3x2(), right.AsSystemMatrix3x2()).AsTerraFXMatrix3x2();

    /// <summary>Returns false and a default output if the BCL candidate fails finite/residual admission.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is nonfinite.</exception>
    public static bool TryInvert(Matrix3x2 value, out Matrix3x2 inverse)
    {
        ThrowHelper.ThrowIfNotFinite(value);
        inverse = default;

        if (!System.Numerics.Matrix3x2.Invert(value.AsSystemMatrix3x2(), out var candidate))
        {
            return false;
        }

        var result = candidate.AsTerraFXMatrix3x2();

        if (!result.IsFinite().All())
        {
            return false;
        }

        ReadOnlySpan<float> original = [value[0, 0], value[0, 1], 0.0f, value[1, 0], value[1, 1], 0.0f, value[2, 0], value[2, 1], 1.0f];
        ReadOnlySpan<float> inverted = [result[0, 0], result[0, 1], 0.0f, result[1, 0], result[1, 1], 0.0f, result[2, 0], result[2, 1], 1.0f];

        if (!GeometryKernel.InverseResidual(original, inverted, 3) || !GeometryKernel.InverseResidual(inverted, original, 3))
        {
            return false;
        }

        inverse = result;
        return true;
    }

    /// <summary>Tests whether all six components have exactly the all-bits-set representation.</summary>
    public bool All() => _row1.All() && _row2.All() && _row3.All();

    /// <summary>Tests whether all six components compare equal to a scalar under IEEE equality.</summary>
    /// <param name="value">The scalar to compare.</param>
    public bool All(float value) => _row1.All(value) && _row2.All(value) && _row3.All(value);

    /// <summary>Tests whether any component has exactly the all-bits-set representation.</summary>
    public bool Any() => _row1.Any() || _row2.Any() || _row3.Any();

    /// <summary>Tests whether any component compares equal to a scalar under IEEE equality.</summary>
    /// <param name="value">The scalar to compare.</param>
    public bool Any(float value) => _row1.Any(value) || _row2.Any(value) || _row3.Any(value);

    /// <summary>Copies six row-ordered component bits without transpose or basis conversion.</summary>
    public System.Numerics.Matrix3x2 AsSystemMatrix3x2()
    {
        // Three reference-free eight-byte engine rows match six BCL row-ordered floats.
        unsafe
        {
            return Unsafe.BitCast<Matrix3x2, System.Numerics.Matrix3x2>(this);
        }
    }

    /// <inheritdoc />
    public bool Equals(Matrix3x2 other) => _row1.Equals(other._row1) && _row2.Equals(other._row2) && _row3.Equals(other._row3);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Matrix3x2 other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_row1, _row2, _row3);

    /// <summary>Classifies all six components independently as finite masks.</summary>
    public Matrix3x2 IsFinite() => new Matrix3x2(_row1.IsFinite(), _row2.IsFinite(), _row3.IsFinite());

    /// <summary>Tests whether no component has exactly the all-bits-set representation.</summary>
    public bool None() => !Any();

    /// <summary>Tests whether no component compares equal to a scalar under IEEE equality.</summary>
    /// <param name="value">The scalar to compare.</param>
    public bool None(float value) => !Any(value);

    /// <inheritdoc />
    public override string ToString() => ToString(null, null);

    /// <inheritdoc />
    public string ToString([StringSyntax("NumericFormat")] string? format, IFormatProvider? formatProvider) =>
        GeometryFormatting.Format([_row1.X, _row1.Y, _row2.X, _row2.Y, _row3.X, _row3.Y], 2, format, formatProvider);

    /// <inheritdoc />
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, [StringSyntax("NumericFormat")] ReadOnlySpan<char> format, IFormatProvider? provider) =>
        GeometryFormatting.TryFormat([_row1.X, _row1.Y, _row2.X, _row2.Y, _row3.X, _row3.Y], 2, false, utf8Destination, out bytesWritten, format, provider);

    /// <inheritdoc />
    public bool TryFormat(Span<char> destination, out int charsWritten, [StringSyntax("NumericFormat")] ReadOnlySpan<char> format, IFormatProvider? provider) =>
        GeometryFormatting.TryFormat([_row1.X, _row1.Y, _row2.X, _row2.Y, _row3.X, _row3.Y], 2, false, destination, out charsWritten, format, provider);
}
