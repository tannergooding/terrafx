// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;
using TerraFX.Numerics;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class AngleTests
{
    private static void AssertWithinTwoUlps(float actual, float expected)
    {
        var actualOrdered = OrderedBits(actual);
        var expectedOrdered = OrderedBits(expected);
        var difference = actualOrdered >= expectedOrdered ? actualOrdered - expectedOrdered : expectedOrdered - actualOrdered;
        Assert.That(difference, Is.LessThanOrEqualTo(2u), $"Expected {expected:R}, got {actual:R}.");
    }

    private static void FormatWithInvalidCharFormat(Angle angle)
    {
        Span<char> destination = [];
        _ = AnglePublicConsumer.TryFormat(angle, destination, out _, "Q", CultureInfo.InvariantCulture);
    }

    private static void FormatWithInvalidUtf8Format(Angle angle)
    {
        Span<byte> destination = [];
        _ = AnglePublicConsumer.TryFormatUtf8(angle, destination, out _, "Q", CultureInfo.InvariantCulture);
    }

    private static uint OrderedBits(float value)
    {
        var bits = BitConverter.SingleToUInt32Bits(value);
        return (bits & 0x8000_0000) != 0 ? ~bits : bits | 0x8000_0000;
    }

    [Test]
    public void ArithmeticRetainsUnitsAndUsesOrdinaryFloatSemantics()
    {
        var left = Angle.FromDegrees(30.0f);
        var right = Angle.FromDegrees(60.0f);

        var sum = AnglePublicConsumer.Add(left, right);
        Assert.That(sum.Value, Is.EqualTo(90.0f));
        Assert.That(sum.Unit, Is.EqualTo(AngleUnit.Degrees));
        Assert.That((right - left).Value, Is.EqualTo(30.0f));
        Assert.That((-left).Value, Is.EqualTo(-30.0f));
        Assert.That((left * 2.0f).Value, Is.EqualTo(60.0f));
        Assert.That((2.0f * left).Value, Is.EqualTo(60.0f));
        Assert.That(float.IsPositiveInfinity((left / 0.0f).Value), Is.True);
        Assert.That((Angle.FromTurns(0.0f) * float.PositiveInfinity).Value, Is.NaN);

        _ = Assert.Throws<ArgumentException>(() => _ = left + Angle.FromRadians(1.0f));
        _ = Assert.Throws<ArgumentException>(() => _ = left - Angle.FromTurns(1.0f));
    }

    [Test]
    public void BoundedEvaluationDoesNotAllocateAfterWarmup()
    {
        var degrees = Angle.FromDegrees(12.5f);
        var turns = Angle.FromTurns(-0.125f);
        var radians = Angle.FromRadians(1.25f);
        var checksum = 0.0f;

        for (var index = 0; index < 64; index++)
        {
            checksum += degrees.ToRadians() + radians.ToTurns();
            var (sin, cos) = turns.SinCos();
            checksum += sin + cos;
        }

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 256; index++)
        {
            checksum += degrees.ToRadians() + radians.ToTurns();
            var (sin, cos) = turns.SinCos();
            checksum += sin + cos;
        }

        var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
        Assert.That(float.IsFinite(checksum), Is.True);
        Assert.That(allocatedAfter - allocatedBefore, Is.Zero);
    }

    [Test]
    public void BoundedFallbackEvaluationDoesNotAllocateAfterWarmup()
    {
        var largeTurns = Angle.FromTurns(float.ScaleB(1.0f, 122));
        var largeRadians = Angle.FromRadians(float.ScaleB(1.0f, 122));
        var largeDegrees = Angle.FromDegrees(float.MaxValue);
        var checksum = 0.0;

        for (var index = 0; index < 64; index++)
        {
            checksum += largeTurns.ToRadians();
            checksum += largeRadians.ToDegrees();
            var (sin, cos) = largeDegrees.SinCos();
            checksum += sin + cos;
        }

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 256; index++)
        {
            checksum += largeTurns.ToRadians();
            checksum += largeRadians.ToDegrees();
            var (sin, cos) = largeDegrees.SinCos();
            checksum += sin + cos;
        }

        var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
        Assert.That(double.IsFinite(checksum), Is.True);
        Assert.That(allocatedAfter - allocatedBefore, Is.Zero);
    }

    [Test]
    public void CharAndUtf8TryFormatHonorCapacitySuffixAndInvalidFormats()
    {
        var angle = Angle.FromDegrees(12.5f);
        Unsafe.SkipInit(out InlineBuffer32<char> charDestinationStorage); // The following Fill initializes the entire destination.
        Span<char> charDestination = charDestinationStorage;
        charDestination.Fill('x');

        var charSuccess = AnglePublicConsumer.TryFormat(angle, charDestination, out var charsWritten, "F1", CultureInfo.InvariantCulture);
        Assert.That(charSuccess, Is.True);
        Assert.That(charDestination[..charsWritten].ToString(), Is.EqualTo("12.5 Degrees"));
        Assert.That(charDestination[charsWritten..].IndexOfAnyExcept('x'), Is.EqualTo(-1));

        Unsafe.SkipInit(out InlineBuffer32<byte> byteDestinationStorage); // The following Fill initializes the entire destination.
        Span<byte> byteDestination = byteDestinationStorage;
        byteDestination.Fill((byte)'x');

        var byteSuccess = AnglePublicConsumer.TryFormatUtf8(angle, byteDestination, out var bytesWritten, "F1", CultureInfo.InvariantCulture);
        Assert.That(byteSuccess, Is.True);
        Assert.That(Encoding.UTF8.GetString(byteDestination[..bytesWritten]), Is.EqualTo("12.5 Degrees"));
        Assert.That(byteDestination[bytesWritten..].IndexOfAnyExcept((byte)'x'), Is.EqualTo(-1));

        Unsafe.SkipInit(out InlineArray2<char> shortCharsStorage); // Failed output is never inspected by this test.
        Span<char> shortChars = shortCharsStorage;
        Assert.That(AnglePublicConsumer.TryFormat(angle, shortChars, out charsWritten, "F1", CultureInfo.InvariantCulture), Is.False);
        Assert.That(charsWritten, Is.Zero);
        Assert.That(AnglePublicConsumer.TryFormat(angle, [], out charsWritten, "F1", CultureInfo.InvariantCulture), Is.False);
        Assert.That(charsWritten, Is.Zero);

        Unsafe.SkipInit(out InlineArray2<byte> shortBytesStorage); // Failed output is never inspected by this test.
        Span<byte> shortBytes = shortBytesStorage;
        Assert.That(AnglePublicConsumer.TryFormatUtf8(angle, shortBytes, out bytesWritten, "F1", CultureInfo.InvariantCulture), Is.False);
        Assert.That(bytesWritten, Is.Zero);
        Assert.That(AnglePublicConsumer.TryFormatUtf8(angle, [], out bytesWritten, "F1", CultureInfo.InvariantCulture), Is.False);
        Assert.That(bytesWritten, Is.Zero);

        _ = Assert.Throws<FormatException>(() => FormatWithInvalidCharFormat(angle));
        _ = Assert.Throws<FormatException>(() => FormatWithInvalidUtf8Format(angle));
    }

    [Test]
    public void ConstructionPreservesRawValueBitsAndRejectsUnknownUnits()
    {
        uint[] valueBits =
        [
            0x0000_0000u,
            0x8000_0000u,
            0x0000_0001u,
            0x7F80_0000u,
            0xFFC1_2345u,
        ];

        foreach (var bits in valueBits)
        {
            var value = BitConverter.UInt32BitsToSingle(bits);

            foreach (var unit in Enum.GetValues<AngleUnit>())
            {
                var angle = new Angle(value, unit);
                Assert.That(BitConverter.SingleToUInt32Bits(angle.Value), Is.EqualTo(bits));
                Assert.That(angle.Unit, Is.EqualTo(unit));
            }
        }

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new Angle(1.0f, (AngleUnit)3));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new Angle(1.0f, (AngleUnit)(-1)));
    }

    [Test]
    public void ConversionRejectsNonfiniteDefinitionsAndReturnsSameUnitBits()
    {
        foreach (var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            _ = Assert.Throws<ArgumentException>(() => Angle.FromDegrees(value).ToDegrees());
            _ = Assert.Throws<ArgumentException>(() => Angle.FromTurns(value).ToDegrees());
            _ = Assert.Throws<ArgumentException>(() => Angle.FromRadians(value).ToDegrees());
        }

        uint[] finiteBits =
        [
            0x0000_0000u,
            0x8000_0000u,
            0x0000_0001u,
            0x3F80_0000u,
            0xFF7F_FFFFu,
        ];

        foreach (var bits in finiteBits)
        {
            var value = BitConverter.UInt32BitsToSingle(bits);
            Assert.That(BitConverter.SingleToUInt32Bits(Angle.FromDegrees(value).ToDegrees()), Is.EqualTo(bits));
            Assert.That(BitConverter.SingleToUInt32Bits(Angle.FromTurns(value).ToTurns()), Is.EqualTo(bits));
            Assert.That(BitConverter.SingleToUInt32Bits(Angle.FromRadians(value).ToRadians()), Is.EqualTo(bits));
        }
    }

    [Test]
    public void DegreeAndTurnConversionsRoundTheExactRationalOnce()
    {
        (uint InputBits, uint ExpectedBits)[] degreeCases = [
            (0x3F80_0000, 0x3B36_0B61),
            (0x42B4_0000, 0x3E80_0000),
            (0x43B4_0000, 0x3F80_0000),
            (0x0000_0001, 0x0000_0000),
            (0x8000_0001, 0x8000_0000),
            (0x7F7F_FFFF, 0x7B36_0B60),
        ];

        foreach (var (inputBits, expectedBits) in degreeCases)
        {
            var input = BitConverter.UInt32BitsToSingle(inputBits);
            Assert.That(BitConverter.SingleToUInt32Bits(Angle.FromDegrees(input).ToTurns()), Is.EqualTo(expectedBits));
        }

        (uint InputBits, uint ExpectedBits)[] turnCases = [
            (0x3E80_0000, 0x42B4_0000),
            (0xBE00_0000, 0xC234_0000),
            (0x0000_0001, 0x0000_0168),
            (0x8000_0001, 0x8000_0168),
            (0x7A00_0000, 0x7E34_0000),
        ];

        foreach (var (inputBits, expectedBits) in turnCases)
        {
            var input = BitConverter.UInt32BitsToSingle(inputBits);
            Assert.That(BitConverter.SingleToUInt32Bits(Angle.FromTurns(input).ToDegrees()), Is.EqualTo(expectedBits));
        }

        _ = Assert.Throws<OverflowException>(() => Angle.FromTurns(float.ScaleB(1.0f, 120)).ToDegrees());
        _ = Assert.Throws<OverflowException>(() => Angle.FromTurns(float.MaxValue).ToDegrees());
    }

    [Test]
    public void DegreeAndTurnTrigReducesExactDefinitionsAndReturnsExactCardinals()
    {
        (Angle Angle, uint SinBits, uint CosBits)[] cases =
        [
            (Angle.FromDegrees(720.0f), 0x0000_0000u, 0x3F80_0000u),
            (Angle.FromDegrees(-720.0f), 0x8000_0000u, 0x3F80_0000u),
            (Angle.FromDegrees(90.0f), 0x3F80_0000u, 0x0000_0000u),
            (Angle.FromDegrees(-90.0f), 0xBF80_0000u, 0x0000_0000u),
            (Angle.FromDegrees(180.0f), 0x0000_0000u, 0xBF80_0000u),
            (Angle.FromDegrees(-180.0f), 0x8000_0000u, 0xBF80_0000u),
            (Angle.FromTurns(2.0f), 0x0000_0000u, 0x3F80_0000u),
            (Angle.FromTurns(-2.0f), 0x8000_0000u, 0x3F80_0000u),
            (Angle.FromTurns(2.25f), 0x3F80_0000u, 0x0000_0000u),
            (Angle.FromTurns(-2.25f), 0xBF80_0000u, 0x0000_0000u),
            (Angle.FromTurns(2.5f), 0x0000_0000u, 0xBF80_0000u),
            (Angle.FromTurns(-2.5f), 0x8000_0000u, 0xBF80_0000u),
            (Angle.FromTurns(0.25f), 0x3F80_0000u, 0x0000_0000u),
            (Angle.FromTurns(-0.25f), 0xBF80_0000u, 0x0000_0000u),
            (Angle.FromTurns(0.5f), 0x0000_0000u, 0xBF80_0000u),
            (Angle.FromTurns(-0.5f), 0x8000_0000u, 0xBF80_0000u),
        ];

        foreach ((var angle, var sinBits, var cosBits) in cases)
        {
            var originalValueBits = BitConverter.SingleToUInt32Bits(angle.Value);
            (var sin, var cos) = angle.SinCos();
            Assert.That(BitConverter.SingleToUInt32Bits(sin), Is.EqualTo(sinBits));
            Assert.That(BitConverter.SingleToUInt32Bits(cos), Is.EqualTo(cosBits));
            Assert.That(BitConverter.SingleToUInt32Bits(angle.Value), Is.EqualTo(originalValueBits));
        }

        (var maxValueSin, var maxValueCos) = Angle.FromDegrees(float.MaxValue).SinCos();
        Assert.That(BitConverter.SingleToUInt32Bits(maxValueSin), Is.EqualTo(0u));
        Assert.That(maxValueCos, Is.EqualTo(1.0f));
        (maxValueSin, maxValueCos) = Angle.FromDegrees(float.MinValue).SinCos();
        Assert.That(BitConverter.SingleToUInt32Bits(maxValueSin), Is.EqualTo(0x8000_0000u));
        Assert.That(maxValueCos, Is.EqualTo(1.0f));
    }

    [Test]
    public void EqualitySeparatesDefinitionEqualityFromIeeeOperatorEquality()
    {
        var positiveZero = Angle.FromDegrees(0.0f);
        var negativeZero = Angle.FromDegrees(BitConverter.UInt32BitsToSingle(0x8000_0000u));
        Assert.That(positiveZero.Equals(negativeZero), Is.True);
        Assert.That(positiveZero == negativeZero, Is.True);
        Assert.That(positiveZero.GetHashCode(), Is.EqualTo(negativeZero.GetHashCode()));
        Assert.That(positiveZero.Equals(Angle.FromTurns(0.0f)), Is.False);

        var nan = BitConverter.UInt32BitsToSingle(0x7FC1_2345u);
        var firstNaN = Angle.FromRadians(nan);
        var secondNaN = Angle.FromRadians(BitConverter.UInt32BitsToSingle(0x7FC5_4321u));
        var sameNaN = firstNaN;
        Assert.That(firstNaN.Equals(secondNaN), Is.True);
        Assert.That(firstNaN == sameNaN, Is.False);
        Assert.That(firstNaN != sameNaN, Is.True);
        Assert.That(firstNaN.GetHashCode(), Is.EqualTo(secondNaN.GetHashCode()));
    }

    [Test]
    public void FormatParametersExposeNumericFormatSyntax()
    {
        var toString = typeof(Angle).GetMethod(nameof(Angle.ToString), [typeof(string), typeof(IFormatProvider)])!;
        var stringSyntax = toString.GetParameters()[0].GetCustomAttribute<StringSyntaxAttribute>();
        Assert.That(stringSyntax?.Syntax, Is.EqualTo("NumericFormat"));
    }

    [Test]
    public void FormattingPreservesTheStoredDefinitionAndCulture()
    {
        var angle = Angle.FromDegrees(90.0f);
        Assert.That(angle.ToString(format: "F2", formatProvider: CultureInfo.InvariantCulture), Is.EqualTo("90.00 Degrees"));
        Assert.That(AnglePublicConsumer.Format(angle, null, CultureInfo.InvariantCulture), Is.EqualTo("90 Degrees"));
        Assert.That(AnglePublicConsumer.Format(angle, "F2", CultureInfo.InvariantCulture), Is.EqualTo("90.00 Degrees"));

        var frenchCulture = CultureInfo.GetCultureInfo("fr-FR");
        Assert.That(AnglePublicConsumer.Format(angle, "F2", frenchCulture), Is.EqualTo("90,00 Degrees"));
        Assert.That(AnglePublicConsumer.Format(Angle.FromDegrees(-1.25f), "F2", frenchCulture), Is.EqualTo("-1,25 Degrees"));

        var customNumberFormat = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        customNumberFormat.NumberDecimalSeparator = "界";
        Assert.That(AnglePublicConsumer.Format(Angle.FromRadians(1.25f), "F2", customNumberFormat), Is.EqualTo("1界25 Radians"));
        Assert.That(AnglePublicConsumer.Format(Angle.FromTurns(float.NaN), null, CultureInfo.InvariantCulture), Is.EqualTo("NaN Turns"));
    }

    [Test]
    public void HalfTurnTrigReturnsExactLargeCardinals()
    {
        (Angle Angle, uint SinBits, uint CosBits)[] cases =
        [
            (Angle.FromTurns(2.0f), 0x0000_0000u, 0x3F80_0000u),
            (Angle.FromTurns(-2.0f), 0x8000_0000u, 0x3F80_0000u),
            (Angle.FromTurns(2.5f), 0x3F80_0000u, 0x0000_0000u),
            (Angle.FromTurns(-2.5f), 0xBF80_0000u, 0x0000_0000u),
            (Angle.FromTurns(3.0f), 0x0000_0000u, 0xBF80_0000u),
            (Angle.FromTurns(-3.0f), 0x8000_0000u, 0xBF80_0000u),
            (Angle.FromTurns(3.5f), 0xBF80_0000u, 0x0000_0000u),
            (Angle.FromTurns(-3.5f), 0x3F80_0000u, 0x0000_0000u),
            (Angle.FromTurns(float.MaxValue), 0x0000_0000u, 0x3F80_0000u),
            (Angle.FromTurns(float.MinValue), 0x8000_0000u, 0x3F80_0000u),
        ];

        foreach ((var angle, var sinBits, var cosBits) in cases)
        {
            (var sin, var cos) = angle.HalfSinCos();
            Assert.That(BitConverter.SingleToUInt32Bits(sin), Is.EqualTo(sinBits));
            Assert.That(BitConverter.SingleToUInt32Bits(cos), Is.EqualTo(cosBits));
        }
    }

    [Test]
    public void LayoutAndDefaultRetainTheDefinitionContract()
    {
        Assert.That(Enum.GetUnderlyingType(typeof(AngleUnit)), Is.EqualTo(typeof(int)));
        var units = Enum.GetValues<AngleUnit>();
        Assert.That(Convert.ToInt32(units[0], CultureInfo.InvariantCulture), Is.EqualTo(0));
        Assert.That(Convert.ToInt32(units[1], CultureInfo.InvariantCulture), Is.EqualTo(1));
        Assert.That(Convert.ToInt32(units[2], CultureInfo.InvariantCulture), Is.EqualTo(2));
        Assert.That(Marshal.SizeOf<Angle>(), Is.EqualTo(8));
        Assert.That(Marshal.OffsetOf<Angle>("_value").ToInt32(), Is.EqualTo(0));
        Assert.That(Marshal.OffsetOf<Angle>("_unit").ToInt32(), Is.EqualTo(4));
        Assert.That(default(Angle).Value, Is.EqualTo(0.0f));
        Assert.That(default(Angle).Unit, Is.EqualTo(AngleUnit.Degrees));
    }

    [Test]
    public void PiConversionsStayWithinTheSpecifiedUlps()
    {
        (uint InputBits, uint ExpectedBits)[] degreeCases = [
            (0x3F80_0000, 0x3C8E_FA35),
            (0x4234_0000, 0x3F49_0FDB),
            (0x0000_0001, 0x0000_0000),
            (0x8000_0001, 0x8000_0000),
            (0x7F7F_FFFF, 0x7C8E_FA35),
        ];

        foreach (var (inputBits, expectedBits) in degreeCases)
        {
            var input = BitConverter.UInt32BitsToSingle(inputBits);
            var expected = BitConverter.UInt32BitsToSingle(expectedBits);
            AssertWithinTwoUlps(Angle.FromDegrees(input).ToRadians(), expected);
        }

        (uint InputBits, uint ExpectedBits)[] turnCases = [
            (0x3E80_0000, 0x3FC9_0FDB),
            (0xBE00_0000, 0xBF49_0FDB),
            (0x0000_0001, 0x0000_0006),
            (0x8000_0001, 0x8000_0006),
            (0x7180_0000, 0x72C9_0FDB),
        ];

        foreach (var (inputBits, expectedBits) in turnCases)
        {
            var input = BitConverter.UInt32BitsToSingle(inputBits);
            var expected = BitConverter.UInt32BitsToSingle(expectedBits);
            AssertWithinTwoUlps(Angle.FromTurns(input).ToRadians(), expected);
        }

        (uint InputBits, uint DegreesBits, uint TurnsBits)[] radianCases = [
            (0x3F80_0000, 0x4265_2EE1, 0x3E22_F983),
            (0xBF80_0000, 0xC265_2EE1, 0xBE22_F983),
            (0x0000_0001, 0x0000_0039, 0x0000_0000),
            (0x8000_0001, 0x8000_0039, 0x8000_0000),
            (0x7180_0000, 0x7465_2EE1, 0x7022_F983),
        ];

        foreach (var (inputBits, degreesBits, turnsBits) in radianCases)
        {
            var input = BitConverter.UInt32BitsToSingle(inputBits);
            AssertWithinTwoUlps(Angle.FromRadians(input).ToDegrees(), BitConverter.UInt32BitsToSingle(degreesBits));
            AssertWithinTwoUlps(Angle.FromRadians(input).ToTurns(), BitConverter.UInt32BitsToSingle(turnsBits));
        }

        Assert.That(float.IsFinite(Angle.FromDegrees(float.MaxValue).ToRadians()), Is.True);
        _ = Assert.Throws<OverflowException>(() => Angle.FromTurns(float.MaxValue).ToRadians());
        _ = Assert.Throws<OverflowException>(() => Angle.FromRadians(float.MaxValue).ToDegrees());
    }

    [Test]
    public void StandardSuccessfulFormattingDoesNotAllocateAfterWarmup()
    {
        var angle = Angle.FromDegrees(12.5f);
        Unsafe.SkipInit(out InlineBuffer32<char> charsStorage); // The allocation test never inspects buffer contents.
        Unsafe.SkipInit(out InlineBuffer32<byte> bytesStorage); // The allocation test never inspects buffer contents.
        Span<char> chars = charsStorage;
        Span<byte> bytes = bytesStorage;

        for (var index = 0; index < 64; index++)
        {
            _ = AnglePublicConsumer.TryFormat(angle, chars, out _, "G9", CultureInfo.InvariantCulture);
            _ = AnglePublicConsumer.TryFormatUtf8(angle, bytes, out _, "G9", CultureInfo.InvariantCulture);
        }

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 256; index++)
        {
            _ = AnglePublicConsumer.TryFormat(angle, chars, out _, "G9", CultureInfo.InvariantCulture);
            _ = AnglePublicConsumer.TryFormatUtf8(angle, bytes, out _, "G9", CultureInfo.InvariantCulture);
        }

        var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
        Assert.That(allocatedAfter - allocatedBefore, Is.Zero);
    }

    [Test]
    public void TrigHandlesNoncardinalLargeDefinitionsAndRadianSignedZero()
    {
        (var sin, var cos) = Angle.FromDegrees(30.0f).SinCos();
        Assert.That(sin, Is.EqualTo(0.5f).Within(2.0e-6f));
        Assert.That(cos, Is.EqualTo((float)(double.Sqrt(3.0) / 2.0)).Within(2.0e-6f));

        var largeDefinition = 16_777_216.0f;
        (sin, cos) = Angle.FromDegrees(largeDefinition).SinCos();
        AssertWithinTwoUlps(sin, (float)double.Sin(double.DegreesToRadians(136.0)));
        AssertWithinTwoUlps(cos, (float)double.Cos(double.DegreesToRadians(136.0)));

        var negativeZero = BitConverter.UInt32BitsToSingle(0x8000_0000u);
        (sin, cos) = Angle.FromRadians(negativeZero).SinCos();
        Assert.That(BitConverter.SingleToUInt32Bits(sin), Is.EqualTo(0x8000_0000u));
        Assert.That(cos, Is.EqualTo(1.0f));

        _ = Assert.Throws<ArgumentException>(() => Angle.FromDegrees(float.NaN).SinCos());
        _ = Assert.Throws<ArgumentException>(() => Angle.FromTurns(float.PositiveInfinity).SinCos());
        _ = Assert.Throws<ArgumentException>(() => Angle.FromRadians(float.NegativeInfinity).SinCos());
    }

    [Test]
    public void TurnTrigReducesLargeValuesByExactPowerOfTwoPeriods()
    {
        float[] values = [3.125f, -3.125f, 1000.375f, -1000.375f, float.MaxValue, float.MinValue];

        foreach (var value in values)
        {
            var turns = value % 1.0f;

            if (turns > 0.5f)
            {
                turns -= 1.0f;
            }
            else if (turns < -0.5f)
            {
                turns += 1.0f;
            }

            var radians = turns * double.Tau;
            (var sin, var cos) = Angle.FromTurns(value).SinCos();
            Assert.That(sin, Is.EqualTo((float)double.Sin(radians)).Within(2.0e-6f));
            Assert.That(cos, Is.EqualTo((float)double.Cos(radians)).Within(2.0e-6f));

            var halfTurns = value % 2.0f;

            if (halfTurns > 1.0f)
            {
                halfTurns -= 2.0f;
            }
            else if (halfTurns < -1.0f)
            {
                halfTurns += 2.0f;
            }

            radians = halfTurns * double.Pi;
            (sin, cos) = Angle.FromTurns(value).HalfSinCos();
            Assert.That(sin, Is.EqualTo((float)double.Sin(radians)).Within(2.0e-6f));
            Assert.That(cos, Is.EqualTo((float)double.Cos(radians)).Within(2.0e-6f));
        }
    }

    [Test]
    public void UnitConversionsUseExactRatiosAndRetainSignedZero()
    {
        Assert.That(Angle.FromDegrees(360.0f).ToTurns(), Is.EqualTo(1.0f));
        Assert.That(Angle.FromDegrees(90.0f).ToTurns(), Is.EqualTo(0.25f));
        Assert.That(BitConverter.SingleToUInt32Bits(Angle.FromDegrees(1.0f).ToTurns()), Is.EqualTo(0x3B36_0B61u));
        Assert.That(Angle.FromTurns(0.25f).ToDegrees(), Is.EqualTo(90.0f));
        Assert.That(Angle.FromTurns(1.0f).ToDegrees(), Is.EqualTo(360.0f));

        var negativeZero = BitConverter.UInt32BitsToSingle(0x8000_0000u);
        Assert.That(BitConverter.SingleToUInt32Bits(Angle.FromDegrees(negativeZero).ToTurns()), Is.EqualTo(0x8000_0000u));
        Assert.That(BitConverter.SingleToUInt32Bits(Angle.FromDegrees(negativeZero).ToRadians()), Is.EqualTo(0x8000_0000u));
        Assert.That(BitConverter.SingleToUInt32Bits(Angle.FromTurns(negativeZero).ToRadians()), Is.EqualTo(0x8000_0000u));
        Assert.That(BitConverter.SingleToUInt32Bits(Angle.FromRadians(negativeZero).ToDegrees()), Is.EqualTo(0x8000_0000u));
        Assert.That(BitConverter.SingleToUInt32Bits(Angle.FromDegrees(-float.Epsilon).ToTurns()), Is.EqualTo(0x8000_0000u));
        Assert.That(BitConverter.SingleToUInt32Bits(Angle.FromRadians(negativeZero).ToRadians()), Is.EqualTo(0x8000_0000u));
    }

    [Test]
    public void Utf8FormattingSupportsNonasciiNumericProviderSymbols()
    {
        var customNumberFormat = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        customNumberFormat.NumberDecimalSeparator = "界";

        Unsafe.SkipInit(out InlineBuffer32<byte> destinationStorage); // Only the reported written prefix is inspected.
        Span<byte> destination = destinationStorage;

        var success = AnglePublicConsumer.TryFormatUtf8(Angle.FromDegrees(1.25f), destination, out var bytesWritten, "F2", customNumberFormat);
        Assert.That(success, Is.True);
        Assert.That(Encoding.UTF8.GetString(destination[..bytesWritten]), Is.EqualTo("1界25 Degrees"));
    }
}
