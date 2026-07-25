// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Text;
using NUnit.Framework;
using TerraFX.Utilities;

namespace TerraFX.UnitTests.Utilities;

/// <summary>Provides a set of tests covering the <see cref="MemoryUtilities" /> static class.</summary>
[TestFixture(TestOf = typeof(MemoryUtilities))]
internal static unsafe class MemoryUtilitiesTests
{
    /// <summary>Provides validation of the <see cref="MemoryUtilities.CopyUnsafe(void*, void*, nuint)" /> method.</summary>
    /// <remarks>
    ///     The lengths intentionally cover every copy path: the <c>SmallCopy</c> switch (0-32), the
    ///     trailing block copies (multiples of 32) plus a sub-32 remainder, and the 128-byte block loop
    ///     with a trailing remainder. The <c>32 * n + r</c> sizes (e.g. 40, 48, 80) are the regression
    ///     cases where the trailing remainder was previously copied from the wrong offset.
    /// </remarks>
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(8)]
    [TestCase(15)]
    [TestCase(16)]
    [TestCase(17)]
    [TestCase(31)]
    [TestCase(32)]
    [TestCase(33)]
    [TestCase(40)]
    [TestCase(47)]
    [TestCase(48)]
    [TestCase(63)]
    [TestCase(64)]
    [TestCase(65)]
    [TestCase(80)]
    [TestCase(95)]
    [TestCase(96)]
    [TestCase(97)]
    [TestCase(111)]
    [TestCase(112)]
    [TestCase(127)]
    [TestCase(128)]
    [TestCase(129)]
    [TestCase(160)]
    [TestCase(176)]
    [TestCase(192)]
    [TestCase(255)]
    public static void CopyUnsafeTest(int length)
    {
        const int MaxLength = 256;

        var source = stackalloc byte[MaxLength];
        var destination = stackalloc byte[MaxLength];

        for (var i = 0; i < MaxLength; i++)
        {
            source[i] = unchecked((byte)(i + 1));
            destination[i] = 0;
        }

        MemoryUtilities.CopyUnsafe(destination, source, (nuint)length);

        for (var i = 0; i < length; i++)
        {
            Assert.That(destination[i], Is.EqualTo(source[i]), $"byte {i} was not copied");
        }

        for (var i = length; i < MaxLength; i++)
        {
            Assert.That(destination[i], Is.EqualTo((byte)0), $"byte {i} was written past the requested length");
        }
    }

    /// <summary>Provides validation of the <see cref="MemoryUtilities.CopyUnsafe(void*, void*, nuint)" /> method when the source and destination overlap.</summary>
    /// <remarks>
    ///     Shifting left has to copy front to back and shifting right has to copy back to front, otherwise
    ///     a block is overwritten before it has been read. The shift range spans the 32-byte block size so
    ///     that both the block loop and the trailing remainder see an overlap.
    /// </remarks>
    [TestCase(false)]
    [TestCase(true)]
    public static void CopyUnsafeOverlappingTest(bool shiftRight)
    {
        const int MaxLength = 400;
        const int MaxShift = 48;

        var expected = stackalloc byte[MaxLength + MaxShift];
        var actual = stackalloc byte[MaxLength + MaxShift];

        var failures = new StringBuilder();

        for (var length = 1; length <= MaxLength; length++)
        {
            for (var shift = 1; shift <= MaxShift; shift++)
            {
                var total = length + shift;

                for (var i = 0; i < total; i++)
                {
                    expected[i] = unchecked((byte)(i + 1));
                    actual[i] = unchecked((byte)(i + 1));
                }

                if (shiftRight)
                {
                    Buffer.MemoryCopy(expected, expected + shift, total, length);
                    MemoryUtilities.CopyUnsafe(actual + shift, actual, (nuint)length);
                }
                else
                {
                    Buffer.MemoryCopy(expected + shift, expected, total, length);
                    MemoryUtilities.CopyUnsafe(actual, actual + shift, (nuint)length);
                }

                if (!new ReadOnlySpan<byte>(expected, total).SequenceEqual(new ReadOnlySpan<byte>(actual, total)))
                {
                    _ = failures.Append(length).Append(':').Append(shift).Append(' ');
                }
            }
        }

        Assert.That(failures.ToString(), Is.Empty, "the listed length:shift pairs did not match a memmove");
    }
}
