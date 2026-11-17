// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using NUnit.Framework;
using TerraFX.Animation;
using BclBigInteger = System.Numerics.BigInteger;

namespace TerraFX.Runtime.Tests.Animation;

internal sealed class AnimationTickKernelsTests
{
    private const int TicksPerSecondRate = 60;

    private static AnimationTickRemainder192 CreateRemainder(BclBigInteger value)
        => AnimationTickRemainder192.FromLittleEndian(EncodeRemainder(value));

    private static byte[] EncodeRemainder(BclBigInteger value)
    {
        var result = new byte[AnimationTickRemainder192.ByteLength];

        if (value.Sign < 0)
        {
            result.AsSpan().Fill(byte.MaxValue);
        }

        var encoded = value.ToByteArray(isUnsigned: false, isBigEndian: false);

        if (encoded.Length > result.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        encoded.AsSpan().CopyTo(result);
        return result;
    }

    private static byte[] SerializeRemainder(AnimationTickRemainder192 value)
    {
        var result = new byte[AnimationTickRemainder192.ByteLength];
        value.WriteLittleEndian(result);
        return result;
    }

    [Test]
    public void ExactBinary32AdvanceCarriesFractionAcrossSixtyFrames()
    {
        var remainder = default(AnimationTickRemainder192);
        var totalTicks = 0L;

        for (var index = 0; index < TicksPerSecondRate; index++)
        {
            var advance = AnimationTickKernels.Advance(
                remainder,
                speed: 1.0f,
                ticksPerSecond: TicksPerSecondRate);
            totalTicks = checked(totalTicks + advance.DeltaTicks);
            remainder = advance.Remainder;
        }

        Assert.That(totalTicks, Is.EqualTo(10_000_000));
        Assert.That(remainder, Is.EqualTo(default(AnimationTickRemainder192)));

        var singleStep = AnimationTickKernels.Advance(
            default,
            speed: 1.0f,
            ticksPerSecond: TicksPerSecondRate);
        Assert.That(singleStep.DeltaTicks, Is.EqualTo(166_666));
        Assert.That(
            SerializeRemainder(singleStep.Remainder),
            Is.EqualTo(EncodeRemainder(new BclBigInteger(40) << 149)));
    }

    [Test]
    public void ExtremeSpeedEndpointsProduceBoundedExactDeltas()
    {
        var maximumForward = AnimationTickKernels.Advance(default, speed: 4.0f, ticksPerSecond: 1);
        var maximumReverse = AnimationTickKernels.Advance(default, speed: -4.0f, ticksPerSecond: 1);

        Assert.Multiple(() => {
            Assert.That(maximumForward.DeltaTicks, Is.EqualTo(40_000_000));
            Assert.That(maximumForward.Remainder, Is.EqualTo(default(AnimationTickRemainder192)));
            Assert.That(maximumReverse.DeltaTicks, Is.EqualTo(-40_000_000));
            Assert.That(maximumReverse.Remainder, Is.EqualTo(default(AnimationTickRemainder192)));
        });
    }

    [Test]
    public void NonFiniteAndOutOfRangeSpeedRatesAndRemaindersAreRejected()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            AnimationTickKernels.Advance(default, float.NaN, TicksPerSecondRate));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            AnimationTickKernels.Advance(default, MathF.BitIncrement(4.0f), TicksPerSecondRate));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            AnimationTickKernels.Advance(default, MathF.BitDecrement(-4.0f), TicksPerSecondRate));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            AnimationTickKernels.Advance(default, 1.0f, ticksPerSecond: 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            AnimationTickKernels.Advance(default, 1.0f, ticksPerSecond: 1_001));

        var oneTickFraction = new BclBigInteger(TicksPerSecondRate) << 149;
        _ = Assert.Throws<InvalidDataException>(() =>
            AnimationTickKernels.Advance(CreateRemainder(oneTickFraction), 1.0f, TicksPerSecondRate));
    }

    [Test]
    public void SignedSpeedAndSubnormalSpeedPreserveExactRemainders()
    {
        var negative = AnimationTickKernels.Advance(
            default,
            speed: -1.0f,
            ticksPerSecond: TicksPerSecondRate);
        Assert.That(negative.DeltaTicks, Is.EqualTo(-166_666));
        Assert.That(
            SerializeRemainder(negative.Remainder),
            Is.EqualTo(EncodeRemainder(new BclBigInteger(-40) << 149)));

        var rateDenominator = new BclBigInteger(TicksPerSecondRate) << 149;
        var nearBoundaryRemainder = CreateRemainder(rateDenominator - 10_000_000);
        var subnormal = AnimationTickKernels.Advance(
            nearBoundaryRemainder,
            speed: float.Epsilon,
            ticksPerSecond: TicksPerSecondRate);
        Assert.That(subnormal.DeltaTicks, Is.EqualTo(1));
        Assert.That(subnormal.Remainder, Is.EqualTo(default(AnimationTickRemainder192)));

        var negativeZero = BitConverter.UInt32BitsToSingle(0x8000_0000);
        var retainedFraction = CreateRemainder(new BclBigInteger(123_456));
        var zeroAdvance = AnimationTickKernels.Advance(
            retainedFraction,
            negativeZero,
            TicksPerSecondRate);
        Assert.That(zeroAdvance.DeltaTicks, Is.Zero);
        Assert.That(zeroAdvance.Remainder, Is.EqualTo(retainedFraction));
    }
}
