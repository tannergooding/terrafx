// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace TerraFX.Numerics;

/// <summary>A row-vector matrix composed of four engine Vector4 rows.</summary>
public readonly partial struct Matrix4x4
    : IEquatable<Matrix4x4>,
      IFormattable,
      ISpanFormattable,
      IUtf8SpanFormattable,
      IMultiplyOperators<Matrix4x4, Matrix4x4, Matrix4x4>,
      IEqualityOperators<Matrix4x4, Matrix4x4, bool>
{
    private readonly Vector4 _row1;
    private readonly Vector4 _row2;
    private readonly Vector4 _row3;
    private readonly Vector4 _row4;

    /// <summary>Initializes raw rows without validation or arithmetic.</summary>
    /// <param name="row1">The first row.</param>
    /// <param name="row2">The second row.</param>
    /// <param name="row3">The third row.</param>
    /// <param name="row4">The fourth row, containing affine translation in XYZ.</param>
    public Matrix4x4(Vector4 row1, Vector4 row2, Vector4 row3, Vector4 row4)
    {
        _row1 = row1;
        _row2 = row2;
        _row3 = row3;
        _row4 = row4;
    }

    /// <summary>Gets the explicit identity transform; the raw default is zero.</summary>
    public static Matrix4x4 Identity => new Matrix4x4(Vector4.UnitX, Vector4.UnitY, Vector4.UnitZ, Vector4.UnitW);

    /// <summary>Gets a copy of the first row.</summary>
    public Vector4 Row1 => _row1;

    /// <summary>Gets a copy of the second row.</summary>
    public Vector4 Row2 => _row2;

    /// <summary>Gets a copy of the third row.</summary>
    public Vector4 Row3 => _row3;

    /// <summary>Gets a copy of the fourth row.</summary>
    public Vector4 Row4 => _row4;

    /// <summary>Gets an element using zero-based row and column indices.</summary>
    /// <param name="row">The row in [0, 3].</param>
    /// <param name="column">The column in [0, 3].</param>
    /// <exception cref="ArgumentOutOfRangeException">An index is outside the matrix extent.</exception>
    public float this[int row, int column]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(row);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(row, 3);
            ArgumentOutOfRangeException.ThrowIfNegative(column);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(column, 3);
            var selected = row switch {
                0 => _row1,
                1 => _row2,
                2 => _row3,
                _ => _row4
            };
            return column switch {
                0 => selected.X,
                1 => selected.Y,
                2 => selected.Z,
                _ => selected.W
            };
        }
    }

    /// <summary>Negates IEEE component equality.</summary>
    public static bool operator !=(Matrix4x4 left, Matrix4x4 right) => !(left == right);

    /// <summary>Multiplies in row-vector application order.</summary>
    public static Matrix4x4 operator *(Matrix4x4 left, Matrix4x4 right) => Multiply(left, right);

    /// <summary>Compares every component using IEEE equality.</summary>
    public static bool operator ==(Matrix4x4 left, Matrix4x4 right) => left._row1 == right._row1 && left._row2 == right._row2 && left._row3 == right._row3 && left._row4 == right._row4;

    /// <summary>Creates a row-vector rotation by evaluating a retained angle and finite unit axis.</summary>
    /// <exception cref="ArgumentException">The axis is nonfinite/nonunit or the angle is nonfinite.</exception>
    public static Matrix4x4 CreateFromAxisAngle(Vector3 unitAxis, Angle angle)
    {
        ThrowHelper.ThrowIfNotUnitAxis(unitAxis);
        ThrowHelper.ThrowIfNotFinite(angle);
        (var sin, var cos) = angle.SinCos();
        var x = unitAxis.X;
        var y = unitAxis.Y;
        var z = unitAxis.Z;
        var complement = 1.0f - cos;
        return new Matrix4x4(
            new Vector4((x * x * complement) + cos, (x * y * complement) + (z * sin), (x * z * complement) - (y * sin), 0.0f),
            new Vector4((y * x * complement) - (z * sin), (y * y * complement) + cos, (y * z * complement) + (x * sin), 0.0f),
            new Vector4((z * x * complement) + (y * sin), (z * y * complement) - (x * sin), (z * z * complement) + cos, 0.0f),
            Vector4.UnitW);
    }

    /// <summary>Creates a row-vector rotation from a finite unit axis and finite radians.</summary>
    /// <exception cref="ArgumentException">The axis is nonfinite/nonunit or radians are nonfinite.</exception>
    public static Matrix4x4 CreateFromAxisAngle(Vector3 unitAxis, float radians)
    {
        ThrowHelper.ThrowIfNotUnitAxis(unitAxis);
        ThrowHelper.ThrowIfNotFinite(radians);
        return System.Numerics.Matrix4x4.CreateFromAxisAngle(unitAxis.AsSystemVector3(), radians).AsTerraFXMatrix4x4();
    }

    /// <summary>Evaluates the BCL quaternion matrix polynomial; unit input is the caller's rotation contract.</summary>
    public static Matrix4x4 CreateFromQuaternion(Quaternion rotation) => System.Numerics.Matrix4x4.CreateFromQuaternion(rotation.AsSystemQuaternion()).AsTerraFXMatrix4x4();

    /// <summary>Creates an X-axis rotation by evaluating the retained angle definition.</summary>
    /// <exception cref="ArgumentException"><paramref name="angle"/> is nonfinite.</exception>
    public static Matrix4x4 CreateRotationX(Angle angle)
    {
        ThrowHelper.ThrowIfNotFinite(angle);
        (var sin, var cos) = angle.SinCos();
        return new Matrix4x4(Vector4.UnitX, new Vector4(0.0f, cos, sin, 0.0f), new Vector4(0.0f, -sin, cos, 0.0f), Vector4.UnitW);
    }

    /// <summary>Creates the BCL X-axis row-vector rotation for finite radians.</summary>
    /// <exception cref="ArgumentException"><paramref name="radians"/> is nonfinite.</exception>
    public static Matrix4x4 CreateRotationX(float radians)
    {
        ThrowHelper.ThrowIfNotFinite(radians);
        return System.Numerics.Matrix4x4.CreateRotationX(radians).AsTerraFXMatrix4x4();
    }

    /// <summary>Creates a Y-axis rotation by evaluating the retained angle definition.</summary>
    /// <exception cref="ArgumentException"><paramref name="angle"/> is nonfinite.</exception>
    public static Matrix4x4 CreateRotationY(Angle angle)
    {
        ThrowHelper.ThrowIfNotFinite(angle);
        (var sin, var cos) = angle.SinCos();
        return new Matrix4x4(new Vector4(cos, 0.0f, -sin, 0.0f), Vector4.UnitY, new Vector4(sin, 0.0f, cos, 0.0f), Vector4.UnitW);
    }

    /// <summary>Creates the BCL Y-axis row-vector rotation for finite radians.</summary>
    /// <exception cref="ArgumentException"><paramref name="radians"/> is nonfinite.</exception>
    public static Matrix4x4 CreateRotationY(float radians)
    {
        ThrowHelper.ThrowIfNotFinite(radians);
        return System.Numerics.Matrix4x4.CreateRotationY(radians).AsTerraFXMatrix4x4();
    }

    /// <summary>Creates a Z-axis rotation by evaluating the retained angle definition.</summary>
    /// <exception cref="ArgumentException"><paramref name="angle"/> is nonfinite.</exception>
    public static Matrix4x4 CreateRotationZ(Angle angle)
    {
        ThrowHelper.ThrowIfNotFinite(angle);
        (var sin, var cos) = angle.SinCos();
        return new Matrix4x4(new Vector4(cos, sin, 0.0f, 0.0f), new Vector4(-sin, cos, 0.0f, 0.0f), Vector4.UnitZ, Vector4.UnitW);
    }

    /// <summary>Creates the BCL Z-axis row-vector rotation for finite radians.</summary>
    /// <exception cref="ArgumentException"><paramref name="radians"/> is nonfinite.</exception>
    public static Matrix4x4 CreateRotationZ(float radians)
    {
        ThrowHelper.ThrowIfNotFinite(radians);
        return System.Numerics.Matrix4x4.CreateRotationZ(radians).AsTerraFXMatrix4x4();
    }

    /// <summary>Creates raw component-wise scale.</summary>
    public static Matrix4x4 CreateScale(Vector3 scale) => new Matrix4x4(new Vector4(scale.X, 0.0f, 0.0f, 0.0f), new Vector4(0.0f, scale.Y, 0.0f, 0.0f), new Vector4(0.0f, 0.0f, scale.Z, 0.0f), Vector4.UnitW);

    /// <summary>Creates raw uniform scale.</summary>
    public static Matrix4x4 CreateScale(float scale) => CreateScale(new Vector3(scale));

    /// <summary>Creates raw affine translation in the fourth row.</summary>
    public static Matrix4x4 CreateTranslation(Vector3 translation) => new Matrix4x4(Vector4.UnitX, Vector4.UnitY, Vector4.UnitZ, new Vector4(translation, 1.0f));

    /// <summary>Obtains a finite inverse passing both exact product residual checks.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is nonfinite or has no admitted inverse.</exception>
    public static Matrix4x4 Invert(Matrix4x4 value)
    {
        if (!TryInvert(value, out var inverse))
        {
            ThrowHelper.ThrowArgumentException("The matrix has no admitted inverse.", nameof(value));
        }

        return inverse;
    }

    /// <summary>Multiplies in row-vector order, applying left then right.</summary>
    public static Matrix4x4 Multiply(Matrix4x4 left, Matrix4x4 right) => System.Numerics.Matrix4x4.Multiply(left.AsSystemMatrix4x4(), right.AsSystemMatrix4x4()).AsTerraFXMatrix4x4();

    /// <summary>Transposes rows and columns without changing basis.</summary>
    public static Matrix4x4 Transpose(Matrix4x4 value) => System.Numerics.Matrix4x4.Transpose(value.AsSystemMatrix4x4()).AsTerraFXMatrix4x4();

    /// <summary>Decomposes admitted finite SRT, normalizing the candidate once and checking recomposition.</summary>
    /// <returns>False with default outputs when no representable admitted SRT exists.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is nonfinite.</exception>
    public static bool TryDecompose(Matrix4x4 value, out Vector3 scale, out Quaternion rotation, out Vector3 translation)
    {
        ThrowHelper.ThrowIfNotFinite(value);
        scale = default;
        rotation = default;
        translation = default;

        if (!System.Numerics.Matrix4x4.Decompose(value.AsSystemMatrix4x4(), out var candidateScale, out var candidateRotation, out var candidateTranslation))
        {
            return false;
        }

        var s = candidateScale.AsTerraFXVector3();
        var r = candidateRotation.AsTerraFXQuaternion();
        var t = candidateTranslation.AsTerraFXVector3();
        var lengthSquared = Quaternion.Dot(r, r);

        if (!s.IsFinite().All() || !r.IsFinite().All() || !t.IsFinite().All()
            || !float.IsFinite(lengthSquared) || lengthSquared <= 1.0e-12f
            || !IsAdmittedScale(s.X) || !IsAdmittedScale(s.Y) || !IsAdmittedScale(s.Z))
        {
            return false;
        }

        r = Quaternion.Normalize(r);
        var recomposed = CreateScale(s) * CreateFromQuaternion(r) * CreateTranslation(t);

        if (!recomposed.IsFinite().All() || !GeometryKernel.RecompositionResidual(value, recomposed))
        {
            return false;
        }

        scale = s;
        rotation = r;
        translation = t;
        return true;
    }

    /// <summary>Returns false and a default output if the BCL candidate fails finite/residual admission.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is nonfinite.</exception>
    public static bool TryInvert(Matrix4x4 value, out Matrix4x4 inverse)
    {
        ThrowHelper.ThrowIfNotFinite(value);
        inverse = default;

        if (!System.Numerics.Matrix4x4.Invert(value.AsSystemMatrix4x4(), out var candidate))
        {
            return false;
        }

        var result = candidate.AsTerraFXMatrix4x4();

        if (!result.IsFinite().All())
        {
            return false;
        }

        Unsafe.SkipInit(out InlineArray16<float> originalStorage); // CopyComponents fills every element before the residual check.
        Unsafe.SkipInit(out InlineArray16<float> invertedStorage); // CopyComponents fills every element before the residual check.
        Span<float> original = originalStorage;
        Span<float> inverted = invertedStorage;

        CopyComponents(value, original);
        CopyComponents(result, inverted);

        if (!GeometryKernel.InverseResidual(original, inverted, 4) || !GeometryKernel.InverseResidual(inverted, original, 4))
        {
            return false;
        }

        inverse = result;
        return true;
    }

    private static void CopyComponents(Matrix4x4 value, Span<float> components)
    {
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                components[(row * 4) + column] = value[row, column];
            }
        }
    }

    private static bool IsAdmittedScale(float value) => float.Abs(value) is >= 0.0001f and <= 1000.0f;

    /// <summary>Tests whether every component has exactly the all-bits-set representation.</summary>
    public bool All() => _row1.All() && _row2.All() && _row3.All() && _row4.All();

    /// <summary>Tests whether every component compares equal to a scalar under IEEE equality.</summary>
    /// <param name="value">The scalar to compare.</param>
    public bool All(float value) => _row1.All(value) && _row2.All(value) && _row3.All(value) && _row4.All(value);

    /// <summary>Tests whether any component has exactly the all-bits-set representation.</summary>
    public bool Any() => _row1.Any() || _row2.Any() || _row3.Any() || _row4.Any();

    /// <summary>Tests whether any component compares equal to a scalar under IEEE equality.</summary>
    /// <param name="value">The scalar to compare.</param>
    public bool Any(float value) => _row1.Any(value) || _row2.Any(value) || _row3.Any(value) || _row4.Any(value);

    /// <summary>Copies sixteen row-ordered component bits without transpose, basis, or depth conversion.</summary>
    public System.Numerics.Matrix4x4 AsSystemMatrix4x4()
    {
        // Four reference-free sixteen-byte engine rows match the BCL row-ordered extent.
        unsafe
        {
            return Unsafe.BitCast<Matrix4x4, System.Numerics.Matrix4x4>(this);
        }
    }

    /// <inheritdoc />
    public bool Equals(Matrix4x4 other) => _row1.Equals(other._row1) && _row2.Equals(other._row2) && _row3.Equals(other._row3) && _row4.Equals(other._row4);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Matrix4x4 other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_row1, _row2, _row3, _row4);

    /// <summary>Classifies all sixteen components independently as finite masks.</summary>
    public Matrix4x4 IsFinite() => new Matrix4x4(_row1.IsFinite(), _row2.IsFinite(), _row3.IsFinite(), _row4.IsFinite());

    /// <summary>Tests whether no component has exactly the all-bits-set representation.</summary>
    public bool None() => !Any();

    /// <summary>Tests whether no component compares equal to a scalar under IEEE equality.</summary>
    /// <param name="value">The scalar to compare.</param>
    public bool None(float value) => !Any(value);

    /// <inheritdoc />
    public override string ToString() => ToString(null, null);

    /// <inheritdoc />
    public string ToString([StringSyntax("NumericFormat")] string? format, IFormatProvider? formatProvider)
    {
        Unsafe.SkipInit(out InlineArray16<float> componentsStorage); // CopyComponents fills all 16 values before formatting.
        Span<float> components = componentsStorage;

        CopyComponents(this, components);
        return GeometryFormatting.Format(components, 4, format, formatProvider);
    }

    /// <inheritdoc />
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, [StringSyntax("NumericFormat")] ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        Unsafe.SkipInit(out InlineArray16<float> componentsStorage); // CopyComponents fills all 16 values before formatting.
        Span<float> components = componentsStorage;

        CopyComponents(this, components);
        return GeometryFormatting.TryFormat(components, 4, false, utf8Destination, out bytesWritten, format, provider);
    }

    /// <inheritdoc />
    public bool TryFormat(Span<char> destination, out int charsWritten, [StringSyntax("NumericFormat")] ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        Unsafe.SkipInit(out InlineArray16<float> componentsStorage); // CopyComponents fills all 16 values before formatting.
        Span<float> components = componentsStorage;

        CopyComponents(this, components);
        return GeometryFormatting.TryFormat(components, 4, false, destination, out charsWritten, format, provider);
    }
}
