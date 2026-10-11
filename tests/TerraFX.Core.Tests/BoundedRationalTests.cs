// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Numerics;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class BoundedRationalTests
{
    private static uint RoundToSingleBits(ulong[] numerator, ulong[] denominator, bool negative = false, bool negativeZero = false)
    {
        Span<ulong> workspace = stackalloc ulong[BoundedRational.MaximumLimbCount * BoundedRational.WorkspaceRegionCount];
        Span<ulong> numeratorLimbs = stackalloc ulong[BoundedRational.MaximumLimbCount];
        Span<ulong> denominatorLimbs = stackalloc ulong[BoundedRational.MaximumLimbCount];
        workspace.Clear();
        numeratorLimbs.Clear();
        denominatorLimbs.Clear();
        numerator.AsSpan().CopyTo(numeratorLimbs);
        denominator.AsSpan().CopyTo(denominatorLimbs);

        var rational = new BoundedRational(workspace);
        rational.SetFraction(numeratorLimbs, denominatorLimbs, negative);
        return BitConverter.SingleToUInt32Bits(rational.RoundToSingle(negativeZero));
    }

    private static (bool Success, uint Bits) TryRoundToSingleBits(
        ulong[] numerator,
        ulong[] denominator,
        bool negative = false,
        bool negativeZero = false)
    {
        Span<ulong> workspace = stackalloc ulong[BoundedRational.MaximumLimbCount * BoundedRational.WorkspaceRegionCount];
        Span<ulong> numeratorLimbs = stackalloc ulong[BoundedRational.MaximumLimbCount];
        Span<ulong> denominatorLimbs = stackalloc ulong[BoundedRational.MaximumLimbCount];
        workspace.Clear();
        numeratorLimbs.Clear();
        denominatorLimbs.Clear();
        numerator.AsSpan().CopyTo(numeratorLimbs);
        denominator.AsSpan().CopyTo(denominatorLimbs);

        var rational = new BoundedRational(workspace);
        rational.SetFraction(numeratorLimbs, denominatorLimbs, negative);
        var success = rational.TryRoundToSingle(out var result, negativeZero);
        return (success, BitConverter.SingleToUInt32Bits(result));
    }

    [Test]
    public void RoundsAtTheFiniteOverflowBoundaryWithoutReturningInfinity()
    {
        const ulong MaximumFinite = 0xFFFF_FF00_0000_0000;
        const ulong OverflowMidpointHighLimb = ((1UL << 25) - 1) << 39;

        Assert.That(RoundToSingleBits([0, MaximumFinite], [1]), Is.EqualTo(0x7F7F_FFFF));
        Assert.That(
            RoundToSingleBits([ulong.MaxValue, OverflowMidpointHighLimb - 1], [1]),
            Is.EqualTo(0x7F7F_FFFF));

        var (positiveSuccess, positiveBits) = TryRoundToSingleBits([0, OverflowMidpointHighLimb], [1]);
        var (negativeSuccess, negativeBits) = TryRoundToSingleBits([0, OverflowMidpointHighLimb], [1], negative: true);
        Assert.That(positiveSuccess, Is.False);
        Assert.That(positiveBits, Is.Zero);
        Assert.That(negativeSuccess, Is.False);
        Assert.That(negativeBits, Is.Zero);
        _ = Assert.Throws<OverflowException>(() => RoundToSingleBits([0, OverflowMidpointHighLimb], [1]));
        _ = Assert.Throws<OverflowException>(() => RoundToSingleBits([0, OverflowMidpointHighLimb], [1], negative: true));
    }

    [Test]
    public void RoundsExactRatiosAtNormalAndSubnormalBoundaries()
    {
        Assert.That(RoundToSingleBits([1], [3]), Is.EqualTo(0x3EAA_AAAB));
        Assert.That(RoundToSingleBits([1], [3], negative: true), Is.EqualTo(0xBEAA_AAAB));

        Assert.That(RoundToSingleBits([16_777_217], [16_777_216]), Is.EqualTo(0x3F80_0000));
        Assert.That(RoundToSingleBits([16_777_219], [16_777_216]), Is.EqualTo(0x3F80_0002));

        Assert.That(RoundToSingleBits([1], [0, 0, 1UL << 21]), Is.EqualTo(0x0000_0001));
        Assert.That(RoundToSingleBits([1], [0, 0, 1UL << 22]), Is.Zero);
        Assert.That(RoundToSingleBits([1], [0, 0, 1UL << 22], negative: true), Is.EqualTo(0x8000_0000));
        Assert.That(RoundToSingleBits([3], [0, 0, 1UL << 22]), Is.EqualTo(0x0000_0002));
        Assert.That(RoundToSingleBits([8_388_607], [0, 0, 1UL << 21]), Is.EqualTo(0x007F_FFFF));
        Assert.That(RoundToSingleBits([16_777_215], [0, 0, 1UL << 22]), Is.EqualTo(0x0080_0000));

        Assert.That(RoundToSingleBits([0], [1]), Is.Zero);
        Assert.That(RoundToSingleBits([0], [1], negativeZero: true), Is.EqualTo(0x8000_0000));
    }
}
