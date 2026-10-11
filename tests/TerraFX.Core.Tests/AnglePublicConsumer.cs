// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Numerics;

namespace TerraFX.Core.Tests;

internal static class AnglePublicConsumer
{
    public static T Add<T>(T left, T right)
        where T : IAdditionOperators<T, T, T> => left + right;

    public static (int Comparison, bool LessThan, bool LessThanOrEqual, bool GreaterThan, bool GreaterThanOrEqual) Compare<T>(T left, T right)
        where T : IComparable<T>, IComparisonOperators<T, T, bool>
    {
        return (left.CompareTo(right), left < right, left <= right, left > right, left >= right);
    }

    public static string Format<T>(T value, string? format, IFormatProvider? formatProvider)
        where T : IFormattable => value.ToString(format, formatProvider);

    public static bool TryFormat<T>(T value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? formatProvider)
        where T : ISpanFormattable => value.TryFormat(destination, out charsWritten, format, formatProvider);

    public static bool TryFormatUtf8<T>(T value, Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? formatProvider)
        where T : IUtf8SpanFormattable => value.TryFormat(utf8Destination, out bytesWritten, format, formatProvider);
}
