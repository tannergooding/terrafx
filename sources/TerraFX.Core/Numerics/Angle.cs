// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace TerraFX.Numerics;

/// <summary>A binary32 angle definition that retains its authored unit until explicitly evaluated.</summary>
public readonly struct Angle
    : IComparable<Angle>,
      IEquatable<Angle>,
      IFormattable,
      ISpanFormattable,
      IUtf8SpanFormattable,
      IAdditionOperators<Angle, Angle, Angle>,
      ISubtractionOperators<Angle, Angle, Angle>,
      IUnaryNegationOperators<Angle, Angle>,
      IMultiplyOperators<Angle, float, Angle>,
      IDivisionOperators<Angle, float, Angle>,
      IEqualityOperators<Angle, Angle, bool>,
      IComparisonOperators<Angle, Angle, bool>
{
    // The degree-to-turn fallback's 182-bit rounding workspace fits in three limbs.
    private const int DegreeToTurnsRationalLimbCount = 3;

    // 128 keeps the largest pi conversion multiplier, 180/pi, below half of Single.MaxValue.
    private const float PiConversionFastPathMagnitudeLimit = float.MaxValue / 128.0f;

    // This is 2P + 1 for the accepted midpoint of P/2^256 < pi < (P + 1)/2^256.
    private const string PiMidpointNumeratorHex = "6487ED5110B4611A626330545C06E0E69481270444533E3A01105DF531D9CD913";

    // The 259-bit pi midpoint numerator fits in five limbs.
    private const int PiMidpointNumeratorLimbCount = 5;

    // The largest bounded Angle intermediate is 439 bits; seven limbs provide 448 bits.
    private const int RationalLimbCount = 7;

    private static readonly IComparer<Angle> s_degreesComparer = new AngleComparer(AngleUnit.Degrees);
    private static readonly IComparer<Angle> s_radiansComparer = new AngleComparer(AngleUnit.Radians);
    private static readonly IComparer<Angle> s_turnsComparer = new AngleComparer(AngleUnit.Turns);

    private readonly float _value;
    private readonly AngleUnit _unit;

    /// <summary>Initializes an angle definition without converting or normalizing its value.</summary>
    /// <param name="value">The binary32 value to retain.</param>
    /// <param name="unit">The unit associated with <paramref name="value"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="unit"/> is not a defined angle unit.</exception>
    public Angle(float value, AngleUnit unit)
    {
        if (!Enum.IsDefined(unit))
        {
            ThrowHelper.ThrowArgumentOutOfRangeException(nameof(unit));
        }

        _value = value;
        _unit = unit;
    }

    /// <summary>Gets the retained unit.</summary>
    public AngleUnit Unit => _unit;

    /// <summary>Gets the retained binary32 value.</summary>
    public float Value => _value;

    /// <summary>Determines whether two angle definitions are unequal under IEEE binary32 comparison.</summary>
    /// <param name="left">The first angle.</param>
    /// <param name="right">The second angle.</param>
    /// <returns><see langword="true"/> when units differ or values compare unequal.</returns>
    public static bool operator !=(Angle left, Angle right) => !(left == right);

    /// <summary>Multiplies an angle definition by a binary32 scalar.</summary>
    /// <param name="value">The angle to scale.</param>
    /// <param name="scalar">The scalar multiplier.</param>
    /// <returns>The scaled binary32 value retaining its unit.</returns>
    public static Angle operator *(Angle value, float scalar) => new Angle(value._value * scalar, value._unit);

    /// <summary>Multiplies a binary32 scalar by an angle definition.</summary>
    /// <param name="scalar">The scalar multiplier.</param>
    /// <param name="value">The angle to scale.</param>
    /// <returns>The scaled binary32 value retaining its unit.</returns>
    public static Angle operator *(float scalar, Angle value) => value * scalar;

    /// <summary>Adds two angle definitions with identical units.</summary>
    /// <param name="left">The first angle.</param>
    /// <param name="right">The second angle.</param>
    /// <returns>The binary32 sum retaining the shared unit.</returns>
    /// <exception cref="ArgumentException">The angles have different units.</exception>
    public static Angle operator +(Angle left, Angle right)
    {
        if (left._unit != right._unit)
        {
            ThrowHelper.ThrowArgumentException("Angles must have identical units.", nameof(right));
        }

        return new Angle(left._value + right._value, left._unit);
    }

    /// <summary>Subtracts two angle definitions with identical units.</summary>
    /// <param name="left">The angle from which to subtract.</param>
    /// <param name="right">The angle to subtract.</param>
    /// <returns>The binary32 difference retaining the shared unit.</returns>
    /// <exception cref="ArgumentException">The angles have different units.</exception>
    public static Angle operator -(Angle left, Angle right)
    {
        if (left._unit != right._unit)
        {
            ThrowHelper.ThrowArgumentException("Angles must have identical units.", nameof(right));
        }

        return new Angle(left._value - right._value, left._unit);
    }

    /// <summary>Negates an angle definition.</summary>
    /// <param name="value">The angle to negate.</param>
    /// <returns>The negated binary32 value retaining its unit.</returns>
    public static Angle operator -(Angle value) => new Angle(-value._value, value._unit);

    /// <summary>Divides an angle definition by a binary32 scalar.</summary>
    /// <param name="value">The angle to divide.</param>
    /// <param name="scalar">The scalar divisor.</param>
    /// <returns>The divided binary32 value retaining its unit.</returns>
    public static Angle operator /(Angle value, float scalar) => new Angle(value._value / scalar, value._unit);

    /// <summary>Compares angles by their degree value.</summary>
    public static bool operator <(Angle left, Angle right) => left.CompareTo(right) < 0;

    /// <summary>Compares angles by their degree value.</summary>
    public static bool operator <=(Angle left, Angle right) => left.CompareTo(right) <= 0;

    /// <summary>Determines whether two angle definitions are equal under IEEE binary32 comparison.</summary>
    /// <param name="left">The first angle.</param>
    /// <param name="right">The second angle.</param>
    /// <returns><see langword="true"/> when units match and values compare equal.</returns>
    public static bool operator ==(Angle left, Angle right) => left._unit == right._unit && left._value == right._value;

    /// <summary>Compares angles by their degree value.</summary>
    public static bool operator >(Angle left, Angle right) => left.CompareTo(right) > 0;

    /// <summary>Compares angles by their degree value.</summary>
    public static bool operator >=(Angle left, Angle right) => left.CompareTo(right) >= 0;

    /// <summary>Creates a comparer that evaluates angles in the selected unit.</summary>
    /// <param name="unit">The unit used as the comparison key.</param>
    /// <returns>A comparer singleton for <paramref name="unit"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="unit"/> is not a defined angle unit.</exception>
    public static IComparer<Angle> CreateComparer(AngleUnit unit) =>
        unit switch {
            AngleUnit.Degrees => s_degreesComparer,
            AngleUnit.Radians => s_radiansComparer,
            AngleUnit.Turns => s_turnsComparer,
            _ => ThrowHelper.ThrowArgumentOutOfRangeException<IComparer<Angle>>(nameof(unit)),
        };

    /// <summary>Creates an angle definition in degrees.</summary>
    /// <param name="value">The binary32 degree value to retain.</param>
    /// <returns>An angle retaining <paramref name="value"/> in degrees.</returns>
    public static Angle FromDegrees(float value) => new Angle(value, AngleUnit.Degrees);

    /// <summary>Creates an angle definition in radians.</summary>
    /// <param name="value">The binary32 radian value to retain.</param>
    /// <returns>An angle retaining <paramref name="value"/> in radians.</returns>
    public static Angle FromRadians(float value) => new Angle(value, AngleUnit.Radians);

    /// <summary>Creates an angle definition in turns.</summary>
    /// <param name="value">The binary32 turn value to retain.</param>
    /// <returns>An angle retaining <paramref name="value"/> in turns.</returns>
    public static Angle FromTurns(float value) => new Angle(value, AngleUnit.Turns);

    private static int CompareInUnit(Angle left, Angle right, AngleUnit unit)
    {
        if ((left._unit == unit) && (right._unit == unit))
        {
            return left._value.CompareTo(right._value);
        }

        return GetValueInUnit(left, unit).CompareTo(GetValueInUnit(right, unit));
    }

    private static double GetValueInUnit(Angle angle, AngleUnit targetUnit)
    {
        if (angle._unit == targetUnit)
        {
            return angle._value;
        }

        var value = (double)angle._value;
        // Normalize mixed-unit values through degrees so equivalent degree/turn inputs retain the same key in every comparer.
        var degrees = angle._unit switch {
            AngleUnit.Degrees => value,
            AngleUnit.Turns => value * 360.0,
            AngleUnit.Radians => double.RadiansToDegrees(value),
            _ => ThrowHelper.ThrowInvalidOperationException<double>("The stored angle unit is invalid."),
        };

        return targetUnit switch {
            AngleUnit.Degrees => degrees,
            AngleUnit.Turns => degrees / 360.0,
            AngleUnit.Radians => double.DegreesToRadians(degrees),
            _ => ThrowHelper.ThrowInvalidOperationException<double>("The angle comparison unit is not defined."),
        };
    }

    /// <inheritdoc />
    public int CompareTo(Angle other) => CompareInUnit(this, other, AngleUnit.Degrees);

    /// <inheritdoc />
    public bool Equals(Angle other) => _unit == other._unit && _value.Equals(other._value);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Angle other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_value, _unit);

    /// <summary>Evaluates the sine and cosine of this definition.</summary>
    /// <returns>The sine and cosine as a named pair.</returns>
    /// <exception cref="ArgumentException"><see cref="Value"/> is not finite.</exception>
    public (float Sin, float Cos) SinCos()
    {
        if (!float.IsFinite(_value))
        {
            ThrowHelper.ThrowArgumentException("The angle value must be finite.");
        }

        if (_unit == AngleUnit.Radians)
        {
            return float.SinCos(_value);
        }

        if (_unit is not (AngleUnit.Degrees or AngleUnit.Turns))
        {
            ThrowHelper.ThrowInvalidOperationException("The stored angle unit is invalid.");
        }

        if ((_unit == AngleUnit.Degrees) && (float.Abs(_value) <= 180.0f))
        {
            if (_value == 0.0f)
            {
                return (float.CopySign(0.0f, _value), 1.0f);
            }

            if (_value == 90.0f)
            {
                return (1.0f, 0.0f);
            }

            if (_value == -90.0f)
            {
                return (-1.0f, 0.0f);
            }

            if (float.Abs(_value) == 180.0f)
            {
                return (float.CopySign(0.0f, _value), -1.0f);
            }

            return float.SinCos(float.DegreesToRadians(_value));
        }

        if ((_unit == AngleUnit.Turns) && (float.Abs(_value) <= 0.5f))
        {
            if (_value == 0.0f)
            {
                return (float.CopySign(0.0f, _value), 1.0f);
            }

            if (_value == 0.25f)
            {
                return (1.0f, 0.0f);
            }

            if (_value == -0.25f)
            {
                return (-1.0f, 0.0f);
            }

            if (float.Abs(_value) == 0.5f)
            {
                return (float.CopySign(0.0f, _value), -1.0f);
            }

            return float.SinCos((float)(_value * double.Tau));
        }

        if (_unit == AngleUnit.Turns)
        {
            // A one-turn period is an exact binary32 remainder, so large turns need no rational workspace.
            var turns = _value % 1.0f;

            if (turns > 0.5f)
            {
                turns -= 1.0f;
            }
            else if (turns < -0.5f)
            {
                turns += 1.0f;
            }

            if (turns == 0.0f)
            {
                return (float.CopySign(0.0f, _value), 1.0f);
            }

            if (turns == 0.25f)
            {
                return (1.0f, 0.0f);
            }

            if (turns == -0.25f)
            {
                return (-1.0f, 0.0f);
            }

            if (float.Abs(turns) == 0.5f)
            {
                return (float.CopySign(0.0f, _value), -1.0f);
            }

            return float.SinCos((float)(turns * double.Tau));
        }

        Unsafe.SkipInit(out InlineBuffer32<ulong> workspaceStorage); // BoundedRational writes or clears ranges before reading them.
        Span<ulong> workspace = workspaceStorage;
        workspace = workspace[..(RationalLimbCount * BoundedRational.WorkspaceRegionCount)];

        var rational = new BoundedRational(workspace);
        rational.SetSingle(_value);
        rational.ReduceModuloDenominator(360u);

        if (rational.NumeratorIsZero)
        {
            return (float.CopySign(0.0f, _value), 1.0f);
        }

        if (rational.NumeratorTimesPowerOfTwoEqualsDenominator(2))
        {
            return (rational.IsNegative ? -1.0f : 1.0f, 0.0f);
        }

        if (rational.NumeratorTimesPowerOfTwoEqualsDenominator(1))
        {
            return (float.CopySign(0.0f, rational.IsNegative ? -1.0f : 1.0f), -1.0f);
        }

        Unsafe.SkipInit(out InlineArray16<ulong> piMidpointStorage); // ParseHex clears it before accumulating digits.
        Span<ulong> piMidpointNumerator = piMidpointStorage;
        piMidpointNumerator = piMidpointNumerator[..PiMidpointNumeratorLimbCount];

        BoundedRational.ParseHex(PiMidpointNumeratorHex, piMidpointNumerator);
        rational.MultiplyNumerator(piMidpointNumerator);
        rational.ShiftDenominatorLeft(256);

        var radians = rational.RoundToSingle();
        return float.SinCos(radians);
    }

    /// <summary>Evaluates this definition in degrees.</summary>
    /// <returns>The converted binary32 degree value.</returns>
    /// <exception cref="ArgumentException"><see cref="Value"/> is not finite.</exception>
    /// <exception cref="OverflowException">The converted value rounds to infinity.</exception>
    public float ToDegrees()
    {
        if (!float.IsFinite(_value))
        {
            ThrowHelper.ThrowArgumentException("The angle value must be finite.");
        }

        if (_unit == AngleUnit.Degrees)
        {
            return _value;
        }

        if (_unit == AngleUnit.Turns)
        {
            // A binary32 significand times 360 has at most 33 bits, so binary64 holds the exact product.
            var result = (float)(_value * 360.0);

            if (!float.IsFinite(result))
            {
                ThrowHelper.ThrowOverflowException("The converted value rounds to infinity.");
            }

            return result;
        }

        if (_unit == AngleUnit.Radians)
        {
            var result = float.RadiansToDegrees(_value);

            if (float.IsFinite(result))
            {
                return result;
            }
        }

        if (_unit is not AngleUnit.Radians)
        {
            ThrowHelper.ThrowInvalidOperationException("The angle conversion is not defined.");
        }

        Unsafe.SkipInit(out InlineBuffer32<ulong> workspaceStorage); // BoundedRational writes or clears ranges before reading them.
        Span<ulong> workspace = workspaceStorage;
        workspace = workspace[..(RationalLimbCount * BoundedRational.WorkspaceRegionCount)];

        var rational = new BoundedRational(workspace);
        rational.SetSingle(_value);

        Unsafe.SkipInit(out InlineArray16<ulong> piMidpointStorage); // ParseHex clears it before accumulating digits.
        Span<ulong> piMidpointNumerator = piMidpointStorage;
        piMidpointNumerator = piMidpointNumerator[..PiMidpointNumeratorLimbCount];

        BoundedRational.ParseHex(PiMidpointNumeratorHex, piMidpointNumerator);
        rational.MultiplyNumerator(180);
        rational.ShiftNumeratorLeft(257);
        rational.MultiplyDenominator(piMidpointNumerator);

        return rational.RoundToSingle(float.IsNegative(_value));
    }

    /// <summary>Evaluates this definition in radians.</summary>
    /// <returns>The converted binary32 radian value.</returns>
    /// <exception cref="ArgumentException"><see cref="Value"/> is not finite.</exception>
    /// <exception cref="OverflowException">The converted value rounds to infinity.</exception>
    public float ToRadians()
    {
        if (!float.IsFinite(_value))
        {
            ThrowHelper.ThrowArgumentException("The angle value must be finite.");
        }

        if (_unit == AngleUnit.Radians)
        {
            return _value;
        }

        if (_unit == AngleUnit.Degrees)
        {
            var result = float.DegreesToRadians(_value);

            if (float.IsFinite(result))
            {
                return result;
            }

            Unsafe.SkipInit(out InlineBuffer32<ulong> largeWorkspaceStorage); // BoundedRational writes or clears ranges before reading them.
            Span<ulong> largeWorkspace = largeWorkspaceStorage;
            largeWorkspace = largeWorkspace[..(RationalLimbCount * BoundedRational.WorkspaceRegionCount)];

            var largeRational = new BoundedRational(largeWorkspace);
            largeRational.SetSingle(_value);

            Unsafe.SkipInit(out InlineArray16<ulong> largePiMidpointStorage); // ParseHex clears it before accumulating digits.
            Span<ulong> largePiMidpointNumerator = largePiMidpointStorage;
            largePiMidpointNumerator = largePiMidpointNumerator[..PiMidpointNumeratorLimbCount];

            BoundedRational.ParseHex(PiMidpointNumeratorHex, largePiMidpointNumerator);
            largeRational.MultiplyNumerator(largePiMidpointNumerator);
            largeRational.ShiftDenominatorLeft(257);
            largeRational.MultiplyDenominator(180);

            return largeRational.RoundToSingle(float.IsNegative(_value));
        }

        if ((_unit == AngleUnit.Turns) && (float.Abs(_value) <= PiConversionFastPathMagnitudeLimit))
        {
            return (float)(_value * double.Tau);
        }

        if (_unit is not AngleUnit.Turns)
        {
            ThrowHelper.ThrowInvalidOperationException("The angle conversion is not defined.");
        }

        Unsafe.SkipInit(out InlineBuffer32<ulong> workspaceStorage); // BoundedRational writes or clears ranges before reading them.
        Span<ulong> workspace = workspaceStorage;
        workspace = workspace[..(RationalLimbCount * BoundedRational.WorkspaceRegionCount)];

        var rational = new BoundedRational(workspace);
        rational.SetSingle(_value);

        Unsafe.SkipInit(out InlineArray16<ulong> piMidpointStorage); // ParseHex clears it before accumulating digits.
        Span<ulong> piMidpointNumerator = piMidpointStorage;
        piMidpointNumerator = piMidpointNumerator[..PiMidpointNumeratorLimbCount];

        BoundedRational.ParseHex(PiMidpointNumeratorHex, piMidpointNumerator);
        rational.MultiplyNumerator(piMidpointNumerator);
        rational.ShiftDenominatorLeft(256);

        return rational.RoundToSingle(float.IsNegative(_value));
    }

    /// <inheritdoc />
    public override string ToString() => ToString(null, null);

    /// <inheritdoc />
    public string ToString([StringSyntax("NumericFormat")] string? format, IFormatProvider? formatProvider) => string.Concat(_value.ToString(format, formatProvider), " ", GetUnitName());

    /// <summary>Evaluates this definition in turns.</summary>
    /// <returns>The converted binary32 turn value.</returns>
    /// <exception cref="ArgumentException"><see cref="Value"/> is not finite.</exception>
    /// <exception cref="OverflowException">The converted value rounds to infinity.</exception>
    public float ToTurns()
    {
        if (!float.IsFinite(_value))
        {
            ThrowHelper.ThrowArgumentException("The angle value must be finite.");
        }

        if (_unit == AngleUnit.Turns)
        {
            return _value;
        }

        if (_unit == AngleUnit.Degrees)
        {
            var quotient = _value / 360.0;
            var rounded = (float)quotient;
            var lowerMidpoint = ((double)float.BitDecrement(rounded) + rounded) * 0.5;
            var upperMidpoint = ((double)rounded + float.BitIncrement(rounded)) * 0.5;
            var divisionErrorBound = double.ScaleB(double.Abs(quotient), -51);

            // IEEE binary64 division error is below this bound; accept only when it cannot cross a binary32 midpoint.
            if ((quotient - lowerMidpoint > divisionErrorBound) && (upperMidpoint - quotient > divisionErrorBound))
            {
                return rounded;
            }
        }
        else if (_unit == AngleUnit.Radians)
        {
            return (float)(_value / double.Tau);
        }
        else
        {
            ThrowHelper.ThrowInvalidOperationException("The angle conversion is not defined.");
        }

        Unsafe.SkipInit(out InlineBuffer32<ulong> workspaceStorage); // BoundedRational writes or clears ranges before reading them.
        Span<ulong> workspace = workspaceStorage;
        workspace = workspace[..(DegreeToTurnsRationalLimbCount * BoundedRational.WorkspaceRegionCount)];

        var rational = new BoundedRational(workspace);
        rational.SetSingle(_value);

        rational.MultiplyDenominator(360);

        return rational.RoundToSingle(float.IsNegative(_value));
    }

    /// <inheritdoc />
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, [StringSyntax("NumericFormat")] ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        bytesWritten = 0;

        if (!_value.TryFormat(utf8Destination, out var valueBytesWritten, format, provider))
        {
            _ = _value.ToString(format.ToString(), provider);
            return false;
        }

        var unitName = GetUnitName();
        var requiredLength = valueBytesWritten + 1 + unitName.Length;

        if (requiredLength > utf8Destination.Length)
        {
            return false;
        }

        utf8Destination[valueBytesWritten] = (byte)' ';

        for (var index = 0; index < unitName.Length; index++)
        {
            utf8Destination[valueBytesWritten + 1 + index] = (byte)unitName[index];
        }

        bytesWritten = requiredLength;
        return true;
    }

    /// <inheritdoc />
    public bool TryFormat(Span<char> destination, out int charsWritten, [StringSyntax("NumericFormat")] ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        charsWritten = 0;

        if (!_value.TryFormat(destination, out var valueCharsWritten, format, provider))
        {
            _ = _value.ToString(format.ToString(), provider);
            return false;
        }

        var unitName = GetUnitName();
        var requiredLength = valueCharsWritten + 1 + unitName.Length;

        if (requiredLength > destination.Length)
        {
            return false;
        }

        destination[valueCharsWritten] = ' ';
        unitName.AsSpan().CopyTo(destination.Slice(valueCharsWritten + 1));

        charsWritten = requiredLength;
        return true;
    }

    /// <summary>Computes the sine and cosine of half this angle.</summary>
    internal (float Sin, float Cos) HalfSinCos()
    {
        if (!float.IsFinite(_value))
        {
            ThrowHelper.ThrowArgumentException("The angle value must be finite.");
        }

        if (_unit == AngleUnit.Radians)
        {
            return float.SinCos((float)(_value * 0.5));
        }

        if (_unit is not (AngleUnit.Degrees or AngleUnit.Turns))
        {
            ThrowHelper.ThrowInvalidOperationException("The stored angle unit is invalid.");
        }

        if ((_unit == AngleUnit.Degrees) && (float.Abs(_value) <= 360.0f))
        {
            if (_value == 0.0f)
            {
                return (float.CopySign(0.0f, _value), 1.0f);
            }

            if (_value == 180.0f)
            {
                return (1.0f, 0.0f);
            }

            if (_value == -180.0f)
            {
                return (-1.0f, 0.0f);
            }

            if (float.Abs(_value) == 360.0f)
            {
                return (float.CopySign(0.0f, _value), -1.0f);
            }

            return float.SinCos(float.DegreesToRadians(_value * 0.5f));
        }

        if ((_unit == AngleUnit.Turns) && (float.Abs(_value) <= 1.0f))
        {
            if (_value == 0.0f)
            {
                return (float.CopySign(0.0f, _value), 1.0f);
            }

            if (_value == 0.5f)
            {
                return (1.0f, 0.0f);
            }

            if (_value == -0.5f)
            {
                return (-1.0f, 0.0f);
            }

            if (float.Abs(_value) == 1.0f)
            {
                return (float.CopySign(0.0f, _value), -1.0f);
            }

            return float.SinCos((float)(_value * double.Pi));
        }

        if (_unit == AngleUnit.Turns)
        {
            // A two-turn half-angle period is an exact binary32 remainder.
            var turns = _value % 2.0f;

            if (turns > 1.0f)
            {
                turns -= 2.0f;
            }
            else if (turns < -1.0f)
            {
                turns += 2.0f;
            }

            if (turns == 0.0f)
            {
                return (float.CopySign(0.0f, _value), 1.0f);
            }

            if (turns == 0.5f)
            {
                return (1.0f, 0.0f);
            }

            if (turns == -0.5f)
            {
                return (-1.0f, 0.0f);
            }

            if (float.Abs(turns) == 1.0f)
            {
                return (float.CopySign(0.0f, _value), -1.0f);
            }

            return float.SinCos((float)(turns * double.Pi));
        }

        Unsafe.SkipInit(out InlineBuffer32<ulong> workspaceStorage); // BoundedRational writes or clears ranges before reading them.
        Span<ulong> workspace = workspaceStorage;
        workspace = workspace[..(RationalLimbCount * BoundedRational.WorkspaceRegionCount)];

        var rational = new BoundedRational(workspace);
        rational.SetSingle(_value);
        rational.ShiftDenominatorLeft(1);
        rational.ReduceModuloDenominator(360u);

        if (rational.NumeratorIsZero)
        {
            return (float.CopySign(0.0f, _value), 1.0f);
        }

        if (rational.NumeratorTimesPowerOfTwoEqualsDenominator(2))
        {
            return (rational.IsNegative ? -1.0f : 1.0f, 0.0f);
        }

        if (rational.NumeratorTimesPowerOfTwoEqualsDenominator(1))
        {
            return (float.CopySign(0.0f, rational.IsNegative ? -1.0f : 1.0f), -1.0f);
        }

        Unsafe.SkipInit(out InlineArray16<ulong> piMidpointStorage); // ParseHex clears it before accumulating digits.
        Span<ulong> piMidpointNumerator = piMidpointStorage;
        piMidpointNumerator = piMidpointNumerator[..PiMidpointNumeratorLimbCount];

        BoundedRational.ParseHex(PiMidpointNumeratorHex, piMidpointNumerator);
        rational.MultiplyNumerator(piMidpointNumerator);
        rational.ShiftDenominatorLeft(256);

        var radians = rational.RoundToSingle();
        return float.SinCos(radians);
    }

    private string GetUnitName()
    {
        switch (_unit)
        {
            case AngleUnit.Degrees:
            {
                return nameof(AngleUnit.Degrees);
            }

            case AngleUnit.Turns:
            {
                return nameof(AngleUnit.Turns);
            }

            case AngleUnit.Radians:
            {
                return nameof(AngleUnit.Radians);
            }

            default:
            {
                return ThrowHelper.ThrowInvalidOperationException<string>("The stored angle unit is invalid.");
            }
        }
    }

    private sealed class AngleComparer : IComparer<Angle>
    {
        private readonly AngleUnit _unit;

        internal AngleComparer(AngleUnit unit)
        {
            _unit = unit;
        }

        public int Compare(Angle left, Angle right) => CompareInUnit(left, right, _unit);
    }
}
