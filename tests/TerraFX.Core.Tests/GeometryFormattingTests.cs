// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using TerraFX.Numerics;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class GeometryFormattingTests
{
    private static void Check<T>(T value, string expected, string? format, IFormatProvider? provider)
        where T : IFormattable, ISpanFormattable, IUtf8SpanFormattable
    {
        Assert.That(value.ToString(format, provider), Is.EqualTo(expected));

        var chars = new char[expected.Length + 3];
        Array.Fill(chars, '#');
        Assert.That(value.TryFormat(chars, out var charsWritten, format, provider), Is.True);
        Assert.That(charsWritten, Is.EqualTo(expected.Length));
        Assert.That(new string(chars, 0, charsWritten), Is.EqualTo(expected));
        Assert.That(chars[charsWritten..], Is.All.EqualTo('#'));
        Assert.That(value.TryFormat(chars.AsSpan(0, expected.Length), out charsWritten, format, provider), Is.True);
        Assert.That(charsWritten, Is.EqualTo(expected.Length));
        Assert.That(value.TryFormat(chars.AsSpan(0, expected.Length - 1), out charsWritten, format, provider), Is.False);
        Assert.That(charsWritten, Is.Zero);
        Assert.That(value.TryFormat(Span<char>.Empty, out charsWritten, format, provider), Is.False);
        Assert.That(charsWritten, Is.Zero);

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var bytes = new byte[expectedBytes.Length + 3];
        Array.Fill(bytes, (byte)0xCC);
        Assert.That(value.TryFormat(bytes, out var bytesWritten, format, provider), Is.True);
        Assert.That(bytesWritten, Is.EqualTo(expectedBytes.Length));
        Assert.That(bytes[..bytesWritten], Is.EqualTo(expectedBytes));
        Assert.That(bytes[bytesWritten..], Is.All.EqualTo((byte)0xCC));
        Assert.That(value.TryFormat(bytes.AsSpan(0, expectedBytes.Length), out bytesWritten, format, provider), Is.True);
        Assert.That(bytesWritten, Is.EqualTo(expectedBytes.Length));
        Assert.That(value.TryFormat(bytes.AsSpan(0, expectedBytes.Length - 1), out bytesWritten, format, provider), Is.False);
        Assert.That(bytesWritten, Is.Zero);
        Assert.That(value.TryFormat(Span<byte>.Empty, out bytesWritten, format, provider), Is.False);
        Assert.That(bytesWritten, Is.Zero);
    }

    private static void CheckInvalid<T>(T value)
        where T : IFormattable, ISpanFormattable, IUtf8SpanFormattable
    {
        _ = Assert.Throws<FormatException>(() => value.ToString("Q", CultureInfo.InvariantCulture));
        _ = Assert.Throws<FormatException>(() => value.TryFormat(Span<char>.Empty, out _, "Q", CultureInfo.InvariantCulture));
        _ = Assert.Throws<FormatException>(() => value.TryFormat(Span<byte>.Empty, out _, "Q", CultureInfo.InvariantCulture));
        _ = Assert.Throws<FormatException>(() => value.TryFormat(new char[1], out _, "Q", CultureInfo.InvariantCulture));
        _ = Assert.Throws<FormatException>(() => value.TryFormat(new byte[1], out _, "Q", CultureInfo.InvariantCulture));
    }

    private static void CheckProvider<T>(T value, IFormatProvider provider)
        where T : IFormattable, ISpanFormattable, IUtf8SpanFormattable
    {
        _ = Assert.Throws<InvalidOperationException>(() => value.ToString("G", provider));
        _ = Assert.Throws<InvalidOperationException>(() => value.TryFormat(Span<char>.Empty, out _, "G", provider));
        _ = Assert.Throws<InvalidOperationException>(() => value.TryFormat(Span<byte>.Empty, out _, "G", provider));
    }

    private static long FormattingAllocation<T>(T value, IFormatProvider? provider)
        where T : ISpanFormattable, IUtf8SpanFormattable
    {
        var chars = RentedBuffer.Create<char>(1024, out var rentedChars);
        var bytes = RentedBuffer.Create<byte>(1024, out var rentedBytes);

        try
        {
            for (var index = 0; index < 100; index++)
            {
                _ = value.TryFormat(chars, out _, "F2", provider);
                _ = value.TryFormat(bytes, out _, "F2", provider);
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            var successes = 0;

            for (var index = 0; index < 1000; index++)
            {
                if (value.TryFormat(chars, out _, "F2", provider) && value.TryFormat(bytes, out _, "F2", provider))
                {
                    successes++;
                }
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(successes, Is.EqualTo(1000));
            return allocated;
        }
        finally
        {
            rentedBytes.Dispose();
            rentedChars.Dispose();
        }
    }

    [TestCase(null, "")]
    [TestCase("", "")]
    [TestCase("G", "")]
    [TestCase("F2", "")]
    [TestCase("F2", "fr-FR")]
    [TestCase("N2", "fr-FR")]
    [TestCase("F200", "")]
    public void AllSixTypesMatchTheirDefinedTextAndBothSpanInterfaces(string? format, string cultureName)
    {
        var provider = CultureInfo.GetCultureInfo(cultureName);
        var v2 = new Vector2(1.25f, -2.5f);
        var v3 = new Vector3(v2, 3);
        var v4 = new Vector4(v3, 4);
        var q = new Quaternion(v3, 4);
        var m32 = new Matrix3x2(new Vector2(1, 2), new Vector2(3, 4), new Vector2(5, 6));
        var m44 = new Matrix4x4(v4, v4, v4, v4);

        Check(v2, v2.AsSystemVector2().ToString(format, provider), format, provider);
        Check(v3, v3.AsSystemVector3().ToString(format, provider), format, provider);
        Check(v4, v4.AsSystemVector4().ToString(format, provider), format, provider);
        var row = $"({1.25f.ToString(format, provider)}, {(-2.5f).ToString(format, provider)}, {3.0f.ToString(format, provider)}, {4.0f.ToString(format, provider)})";
        Check(q, row, format, provider);
        var rows = $"[({1.0f.ToString(format, provider)}, {2.0f.ToString(format, provider)}); ({3.0f.ToString(format, provider)}, {4.0f.ToString(format, provider)}); ({5.0f.ToString(format, provider)}, {6.0f.ToString(format, provider)})]";
        Check(m32, rows, format, provider);
        Check(m44, $"[{row}; {row}; {row}; {row}]", format, provider);
    }

    [Test]
    public void CurrentCultureAndCustomNonAsciiNumberFormatAreHonored()
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var value = new Quaternion(1.25f, -2.5f, 3, 4);
            Assert.That(value.ToString("F2", null), Is.EqualTo("(1,25, -2,50, 3,00, 4,00)"));
            Check(value, "(1,25, -2,50, 3,00, 4,00)", "F2", null);
            Assert.That(value.ToString(), Is.EqualTo(value.ToString(null, null)));
            var vector = new Vector3(1.25f, -2.5f, 3);
            Check(vector, vector.AsSystemVector3().ToString(null, null), null, null);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        var custom = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        custom.NumberDecimalSeparator = "\u066B";
        custom.NumberGroupSeparator = "\u202F";
        custom.NegativeSign = "\u2212";
        custom.NaNSymbol = "\u2603";
        custom.PositiveInfinitySymbol = "\u221E";
        custom.NegativeInfinitySymbol = "\u2212\u221E";
        var v2 = new Vector2(1.25f, -2.5f);
        var v3 = new Vector3(v2, float.NaN);
        var v4 = new Vector4(v3, float.PositiveInfinity);
        var q = new Quaternion(v3, float.NegativeInfinity);

        Check(v2, v2.AsSystemVector2().ToString("F2", custom), "F2", custom);
        Check(v3, v3.AsSystemVector3().ToString("F2", custom), "F2", custom);
        Check(v4, v4.AsSystemVector4().ToString("F2", custom), "F2", custom);
        Check(q, q.ToString("F2", custom), "F2", custom);
        Check(new Matrix3x2(v2, v2, v2), new Matrix3x2(v2, v2, v2).ToString("F2", custom), "F2", custom);
        Check(new Matrix4x4(v4, v4, v4, v4), new Matrix4x4(v4, v4, v4, v4).ToString("F2", custom), "F2", custom);
    }

    [Test]
    public void InvalidFiniteFormatsAreNotHiddenByExhaustionOrEarlierNonfiniteComponents()
    {
        CheckInvalid(new Vector2(float.NaN, 1));
        CheckInvalid(new Vector3(float.NaN, float.NaN, 1));
        CheckInvalid(new Vector4(float.NaN, float.NaN, float.NaN, 1));
        CheckInvalid(new Quaternion(float.NaN, float.NaN, float.NaN, 1));
        CheckInvalid(new Matrix3x2(new Vector2(float.NaN), new Vector2(float.NaN), new Vector2(float.NaN, 1)));
        CheckInvalid(new Matrix4x4(new Vector4(float.NaN), new Vector4(float.NaN), new Vector4(float.NaN), new Vector4(float.NaN, float.NaN, float.NaN, 1)));
    }

    [Test]
    public void InvalidProviderExceptionsPropagateRatherThanReturningFalse()
    {
        var provider = new ThrowingProvider();
        CheckProvider(Vector2.One, provider);
        CheckProvider(Vector3.One, provider);
        CheckProvider(Vector4.One, provider);
        CheckProvider(Quaternion.Identity, provider);
        CheckProvider(Matrix3x2.Identity, provider);
        CheckProvider(Matrix4x4.Identity, provider);
    }

    [Test]
    public void NumericFormatParametersCarryTheAcceptedStringSyntaxMetadata()
    {
        Type[] types = [typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Quaternion), typeof(Matrix3x2), typeof(Matrix4x4)];

        foreach (var type in types)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            var parameters = methods.SelectMany(method => method.GetParameters()).Where(parameter => parameter.Name == "format").ToArray();
            Assert.That(parameters.Length, Is.EqualTo(3));

            foreach (var parameter in parameters)
            {
                Assert.That(parameter.GetCustomAttribute<StringSyntaxAttribute>()?.Syntax, Is.EqualTo("NumericFormat"));
            }
        }
    }

    [Test]
    public void SignedZeroAndNonfiniteScalarBehaviorAreRetained()
    {
        var v2 = new Vector2(-0.0f, float.NaN);
        var v3 = new Vector3(v2, float.PositiveInfinity);
        var v4 = new Vector4(v3, float.NegativeInfinity);
        var q = new Quaternion(v3, float.NegativeInfinity);

        Check(v2, v2.AsSystemVector2().ToString("G", CultureInfo.InvariantCulture), "G", CultureInfo.InvariantCulture);
        Check(v3, v3.AsSystemVector3().ToString("G", CultureInfo.InvariantCulture), "G", CultureInfo.InvariantCulture);
        Check(v4, v4.AsSystemVector4().ToString("G", CultureInfo.InvariantCulture), "G", CultureInfo.InvariantCulture);
        Check(q, "(-0, NaN, Infinity, -Infinity)", "G", CultureInfo.InvariantCulture);
        var allNan = new Quaternion(float.NaN, float.NaN, float.NaN, float.NaN);
        Check(allNan, allNan.ToString("Q", CultureInfo.InvariantCulture), "Q", CultureInfo.InvariantCulture);
    }

    [Test]
    public void WarmSuccessfulCharAndUtf8FormattingDoesNotAllocateEngineManagedStorage()
    {
        var provider = CultureInfo.InvariantCulture;
        Assert.That(FormattingAllocation(Vector2.One, provider), Is.Zero);
        Assert.That(FormattingAllocation(Vector3.One, provider), Is.Zero);
        Assert.That(FormattingAllocation(Vector4.One, provider), Is.Zero);
        Assert.That(FormattingAllocation(Quaternion.Identity, provider), Is.Zero);
        Assert.That(FormattingAllocation(Matrix3x2.Identity, provider), Is.Zero);
        Assert.That(FormattingAllocation(Matrix4x4.Identity, provider), Is.Zero);
        Assert.That(FormattingAllocation(Matrix4x4.Identity, null), Is.Zero);
    }

    private sealed class ThrowingProvider : IFormatProvider
    {
        public object GetFormat(Type? formatType) => throw new InvalidOperationException("Provider failure.");
    }
}
