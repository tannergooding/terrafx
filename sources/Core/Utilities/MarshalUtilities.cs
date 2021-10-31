// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using static TerraFX.Utilities.UnsafeUtilities;

namespace TerraFX.Utilities
{
    /// <summary>Provides a set of methods to supplement or replace <see cref="Marshal" />.</summary>
    public static unsafe class MarshalUtilities
    {
        /// <summary>Gets a string for a given span.</summary>
        /// <param name="span">The span for which to create the string.</param>
        /// <returns>A string created from <paramref name="span" />.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string? GetString(this ReadOnlySpan<sbyte> span)
        {
            string? result;

            if (span.GetPointer() != null)
            {
                result = Encoding.UTF8.GetString(span.As<sbyte, byte>());
            }
            else
            {
                result = null;
            }

            return result;
        }

        /// <summary>Gets a string for a given span.</summary>
        /// <param name="span">The span for which to create the string.</param>
        /// <returns>A string created from <paramref name="span" />.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string? GetString(this ReadOnlySpan<ushort> span)
        {
            string? result;

            if (span.GetPointer() != null)
            {
                result = new string(span.As<ushort, char>());
            }
            else
            {
                result = null;
            }

            return result;
        }

        /// <inheritdoc cref="Marshal.GetLastWin32Error" />
        public static int GetLastError() => Marshal.GetLastWin32Error();

        /// <summary>Marshals a string to a null-terminated ASCII string.</summary>
        /// <param name="source">The string for which to marshal.</param>
        /// <returns>A null-terminated ASCII string that is equivalent to <paramref name="source" />.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySpan<sbyte> GetAsciiSpan(this string? source)
        {
            ReadOnlySpan<byte> result;

            if (source is not null)
            {
                var maxLength = Encoding.ASCII.GetMaxByteCount(source.Length);
                var bytes = new byte[maxLength + 1];

                var length = Encoding.ASCII.GetBytes(source, bytes);
                result = bytes.AsSpan(0, length);

            }
            else
            {
                result = null;
            }

            return result.As<byte, sbyte>();
        }

        /// <summary>Gets a span for a null-terminated ASCII character sequence.</summary>
        /// <param name="source">The pointer to a null-terminated ASCII character sequence.</param>
        /// <param name="maxLength">The maxmimum length of <paramref name="source" /> or <c>-1</c> if the maximum length is unknown.</param>
        /// <returns>A span that starts at <paramref name="source" /> and extends to <paramref name="maxLength" /> or the first null character, whichever comes first.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySpan<sbyte> GetAsciiSpan(sbyte* source, int maxLength = -1)
            => GetUtf8Span(source, maxLength);

        /// <summary>Gets a span for a null-terminated ASCII character sequence.</summary>
        /// <param name="source">The reference to a null-terminated ASCII character sequence.</param>
        /// <param name="maxLength">The maxmimum length of <paramref name="source" /> or <c>-1</c> if the maximum length is unknown.</param>
        /// <returns>A span that starts at <paramref name="source" /> and extends to <paramref name="maxLength" /> or the first null character, whichever comes first.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySpan<sbyte> GetAsciiSpan(in sbyte source, int maxLength = -1)
            => GetUtf8Span(in source, maxLength);

        /// <summary>Gets a null-terminated sequence of UTF8 characters for a string.</summary>
        /// <param name="source">The string for which to get the null-terminated UTF8 character sequence.</param>
        /// <returns>A null-terminated UTF8 character sequence created from <paramref name="source" />.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySpan<sbyte> GetUtf8Span(this string? source)
        {
            ReadOnlySpan<byte> result;

            if (source is not null)
            {
                var maxLength = Encoding.UTF8.GetMaxByteCount(source.Length);
                var bytes = new byte[maxLength + 1];

                var length = Encoding.UTF8.GetBytes(source, bytes);
                result = bytes.AsSpan(0, length);
            }
            else
            {
                result = null;
            }

            return result.As<byte, sbyte>();
        }

        /// <summary>Gets a span for a null-terminated UTF8 character sequence.</summary>
        /// <param name="source">The pointer to a null-terminated UTF8 character sequence.</param>
        /// <param name="maxLength">The maxmimum length of <paramref name="source" /> or <c>-1</c> if the maximum length is unknown.</param>
        /// <returns>A span that starts at <paramref name="source" /> and extends to <paramref name="maxLength" /> or the first null character, whichever comes first.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySpan<sbyte> GetUtf8Span(sbyte* source, int maxLength = -1)
            => (source != null) ? GetUtf8Span(in source[0], maxLength) : null;

        /// <summary>Gets a span for a null-terminated UTF8 character sequence.</summary>
        /// <param name="source">The reference to a null-terminated UTF8 character sequence.</param>
        /// <param name="maxLength">The maxmimum length of <paramref name="source" /> or <c>-1</c> if the maximum length is unknown.</param>
        /// <returns>A span that starts at <paramref name="source" /> and extends to <paramref name="maxLength" /> or the first null character, whichever comes first.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySpan<sbyte> GetUtf8Span(in sbyte source, int maxLength = -1)
        {
            ReadOnlySpan<sbyte> result;

            if (!IsNullRef(in source))
            {
                if (maxLength < 0)
                {
                    maxLength = int.MaxValue;
                }

                result = CreateReadOnlySpan(in source, maxLength);
                var length = result.IndexOf((sbyte)'\0');

                if (length != -1)
                {
                    result = result.Slice(0, length);
                }
            }
            else
            {
                result = null;
            }

            return result;
        }

        /// <summary>Gets a null-terminated sequence of UTF16 characters for a string.</summary>
        /// <param name="source">The string for which to get the null-terminated UTF16 character sequence.</param>
        /// <returns>A null-terminated UTF16 character sequence created from <paramref name="source" />.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySpan<ushort> GetUtf16Span(this string? source) => source.AsSpan().As<char, ushort>();

        /// <summary>Marshals a null-terminated UTF16 string to a <see cref="ReadOnlySpan{UInt16}" />.</summary>
        /// <param name="source">The pointer to a null-terminated UTF16 string.</param>
        /// <param name="maxLength">The maxmimum length of <paramref name="source" /> or <c>-1</c> if the maximum length is unknown.</param>
        /// <returns>A <see cref="ReadOnlySpan{UInt16}" /> that starts at <paramref name="source" /> and extends to <paramref name="maxLength" /> or the first null character, whichever comes first.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySpan<ushort> GetUtf16Span(ushort* source, int maxLength = -1)
            => (source != null) ? GetUtf16Span(in source[0], maxLength) : null;

        /// <summary>Marshals a null-terminated UTF16 string to a <see cref="ReadOnlySpan{UInt16}" />.</summary>
        /// <param name="source">The reference to a null-terminated UTF16 string.</param>
        /// <param name="maxLength">The maxmimum length of <paramref name="source" /> or <c>-1</c> if the maximum length is unknown.</param>
        /// <returns>A <see cref="ReadOnlySpan{UInt16}" /> that starts at <paramref name="source" /> and extends to <paramref name="maxLength" /> or the first null character, whichever comes first.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySpan<ushort> GetUtf16Span(in ushort source, int maxLength = -1)
        {
            ReadOnlySpan<ushort> result;

            if (!IsNullRef(in source))
            {
                if (maxLength < 0)
                {
                    maxLength = int.MaxValue;
                }

                result = CreateReadOnlySpan(in source, maxLength);
                var length = result.IndexOf('\0');

                if (length != -1)
                {
                    result = result.Slice(0, length);
                }
            }
            else
            {
                result = null;
            }

            return result;
        }
    }
}