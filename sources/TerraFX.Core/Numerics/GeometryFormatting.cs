// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Globalization;
using System.Text;

namespace TerraFX.Numerics;

internal static class GeometryFormatting
{
    internal static string Format(ReadOnlySpan<float> components, int columns, string? format, IFormatProvider? provider)
    {
        var builder = new StringBuilder();

        if (components.Length > columns)
        {
            _ = builder.Append('[');
        }

        for (var index = 0; index < components.Length; index++)
        {
            _ = builder.Append(index % columns == 0 ? index == 0 ? "(" : "); (" : ", ");
            _ = builder.Append(components[index].ToString(format, provider));
        }

        _ = builder.Append(')');

        if (components.Length > columns)
        {
            _ = builder.Append(']');
        }

        return builder.ToString();
    }

    internal static bool TryFormat(ReadOnlySpan<float> components, int columns, bool vector, Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        var writer = new Writer(utf8Destination);
        Write(ref writer, components, columns, vector, format, provider);
        bytesWritten = writer.Written;
        return writer.Succeeded;
    }

    internal static bool TryFormat(ReadOnlySpan<float> components, int columns, bool vector, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        var writer = new Writer(destination);
        Write(ref writer, components, columns, vector, format, provider);
        charsWritten = writer.Written;
        return writer.Succeeded;
    }

    private static void Write(ref Writer writer, ReadOnlySpan<float> components, int columns, bool vector, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        var separator = vector ? NumberFormatInfo.GetInstance(provider).NumberGroupSeparator : ",";
        writer.Append(vector ? "<" : components.Length > columns ? "[(" : "(");

        for (var index = 0; index < components.Length; index++)
        {
            if (index != 0)
            {
                if (!vector && index % columns == 0)
                {
                    writer.Append("); (");
                }
                else
                {
                    writer.Append(separator);
                    writer.Append(" ");
                }
            }

            // Visit every scalar even after exhaustion: a later invalid format must still throw.
            writer.Append(components[index], format, provider);
        }

        writer.Append(vector ? ">" : components.Length > columns ? ")]" : ")");
    }

    private ref struct Writer
    {
        private readonly Span<char> _chars;
        private readonly Span<byte> _bytes;
        private readonly bool _utf8;
        private int _position;
        private bool _failed;

        internal Writer(Span<byte> destination)
        {
            _chars = default;
            _bytes = destination;
            _utf8 = true;
        }

        internal Writer(Span<char> destination)
        {
            _chars = destination;
            _bytes = default;
            _utf8 = false;
        }

        internal readonly bool Succeeded => !_failed;

        internal readonly int Written => _failed ? 0 : _position;

        internal void Append(ReadOnlySpan<char> text)
        {
            if (_failed)
            {
                return;
            }

            if (_utf8)
            {
                var count = Encoding.UTF8.GetByteCount(text);

                if (count > _bytes.Length - _position)
                {
                    _failed = true;
                    return;
                }

                _position += Encoding.UTF8.GetBytes(text, _bytes[_position..]);
            }
            else
            {
                if (!text.TryCopyTo(_chars[_position..]))
                {
                    _failed = true;
                    return;
                }

                _position += text.Length;
            }
        }

        internal void Append(float value, ReadOnlySpan<char> format, IFormatProvider? provider)
        {
            var success = _utf8
                ? value.TryFormat(_failed ? [] : _bytes[_position..], out var written, format, provider)
                : value.TryFormat(_failed ? [] : _chars[_position..], out written, format, provider);

            if (!success)
            {
                _ = value.ToString(format.ToString(), provider);
                _failed = true;
            }
            else if (!_failed)
            {
                _position += written;
            }
        }
    }
}
