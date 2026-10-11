// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Numerics;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class SpatialMathTests
{
    private static void AssertAxis(int chunk, float offset, (int Chunk, float Offset) expected)
    {
        Assert.That(chunk, Is.EqualTo(expected.Chunk));
        Assert.That(BitConverter.SingleToInt32Bits(offset), Is.EqualTo(BitConverter.SingleToInt32Bits(expected.Offset)));
    }

    private static SpatialProfileId Register(SpatialSettings settings)
    {
        // Deterministic fixture-only identities keep every repeated declaration
        // idempotent instead of consuming process-global capacity on each test.
        var id = new SpatialProfileId(new Guid(settings.MetersPerUnitExponent + 31, (short)settings.ChunkSizeExponent, 0, 0x53, 0x50, 0x41, 0x54, 0x49, 0x41, 0x4C, 0x01));
        SpatialProfiles.Register(id, settings);
        return id;
    }

    [Test]
    public void AlreadyCanonicalAndSignedZeroNormalizationHaveNoManagedAllocation()
    {
        var settings = SpatialSettings.Default;
        var p2 = new SpatialPosition2(new ChunkIndex2(int.MaxValue, int.MinValue), new Vector2(1, -0.0f));
        var p3 = new SpatialPosition3(new ChunkIndex3(int.MaxValue, int.MinValue, 17), new Vector3(1, 2, -0.0f));

        for (var index = 0; index < 100; index++)
        {
            _ = SpatialMath.Normalize(p2, settings);
            _ = SpatialMath.Normalize(p3, settings);
            _ = SpatialMath.ConvertPosition(p2, SpatialProfileId.Default, settings, SpatialProfileId.Default, settings);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 1000; index++)
        {
            p2 = SpatialMath.Normalize(p2, settings);
            p3 = SpatialMath.Normalize(p3, settings);
            p2 = SpatialMath.ConvertPosition(p2, SpatialProfileId.Default, settings, SpatialProfileId.Default, settings);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero);
        Assert.That(BitConverter.SingleToInt32Bits(p2.Offset.Y), Is.Zero);
        Assert.That(BitConverter.SingleToInt32Bits(p3.Offset.Z), Is.Zero);
    }

    [Test]
    public void ConversionChecksRoundedEndpointOverflowAndDoesNotPrematurelyNormalizeTheSource()
    {
        var source = new SpatialSettings(-1, 16);
        var target = new SpatialSettings(0, 8);
        var sourceId = Register(source);
        var targetId = Register(target);
        var almost = new SpatialPosition2(new ChunkIndex2(int.MaxValue, 0), new Vector2(-float.Epsilon, 0));
        var sameUnitTarget = new SpatialSettings(-1, 16);
        var sameUnitId = Register(sameUnitTarget);

        var canonical = SpatialMath.ConvertPosition(almost, sourceId, source, sameUnitId, sameUnitTarget);
        Assert.That(canonical.Chunk.X, Is.EqualTo(int.MaxValue));
        Assert.That(canonical.Offset.X, Is.Zero);
        // 65536 * 0.1 / 256 = 25.6 target cells per source cell. This source
        // rational is below the outer target face but its remainder rounds to C.
        var cell = 83886080;
        var offset = -float.Epsilon;
        var outer2 = new SpatialPosition2(new ChunkIndex2(cell, 0), new Vector2(offset, 0));
        var outer3 = new SpatialPosition3(new ChunkIndex3(0, 0, cell), new Vector3(0, 0, offset));
        _ = Assert.Throws<OverflowException>(() => SpatialMath.ConvertPosition(outer2, sourceId, source, targetId, target));
        _ = Assert.Throws<OverflowException>(() => SpatialMath.ConvertPosition(outer3, sourceId, source, targetId, target));
        Assert.That(outer2.Chunk.X, Is.EqualTo(cell));
        Assert.That(outer3.Chunk.Z, Is.EqualTo(cell));
        var shrinking = new SpatialSettings(30, 12);
        var shrinkingId = Register(shrinking);
        var enormousRaw = new SpatialPosition3(default, new Vector3(float.MaxValue));
        var shrunk = SpatialMath.ConvertPosition(enormousRaw, SpatialProfileId.Default, SpatialSettings.Default, shrinkingId, shrinking);
        var expectedShrunk = (83076, BitConverter.UInt32BitsToSingle(0x453E_AA37));
        AssertAxis(shrunk.Chunk.X, shrunk.Offset.X, expectedShrunk);
        AssertAxis(shrunk.Chunk.Y, shrunk.Offset.Y, expectedShrunk);
        AssertAxis(shrunk.Chunk.Z, shrunk.Offset.Z, expectedShrunk);
    }

    [Test]
    public void ConversionHandlesExactLengthSeamsNegativeCarriesNormalAndSubnormalNearestEven()
    {
        var meters = SpatialSettings.Default;
        var millimeters = new SpatialSettings(-3, 12);
        var kilometers = new SpatialSettings(3, 13);
        var mmId = Register(millimeters);
        var kmId = Register(kilometers);

        var p2 = new SpatialPosition2(new ChunkIndex2(1, -1), new Vector2(0, 4095));
        var converted = SpatialMath.ConvertPosition(p2, SpatialProfileId.Default, meters, mmId, millimeters);
        Assert.That(converted.Chunk, Is.EqualTo(new ChunkIndex2(1000, -1)));
        Assert.That(converted.Offset, Is.EqualTo(new Vector2(0, 3096)));
        var raw = new SpatialPosition3(default, new Vector3(1.0f, -0.5f, 5.0f));
        var converted3 = SpatialMath.ConvertPosition(raw, kmId, kilometers, SpatialProfileId.Default, meters);
        Assert.That(converted3.Chunk, Is.EqualTo(new ChunkIndex3(0, -1, 1)));
        Assert.That(converted3.Offset, Is.EqualTo(new Vector3(1000.0f, 3596.0f, 904.0f)));

        foreach (var (bits, expectedBits) in new[] { (500, 0), (1500, 2) })
        {
            var tiny = BitConverter.Int32BitsToSingle(bits);
            var result2 = SpatialMath.ConvertPosition(new SpatialPosition2(default, new Vector2(tiny)), mmId, millimeters, SpatialProfileId.Default, meters);
            var result3 = SpatialMath.ConvertPosition(new SpatialPosition3(default, new Vector3(tiny)), mmId, millimeters, SpatialProfileId.Default, meters);
            Assert.That(BitConverter.SingleToInt32Bits(result2.Offset.X), Is.EqualTo(expectedBits));
            Assert.That(BitConverter.SingleToInt32Bits(result3.Offset.Z), Is.EqualTo(expectedBits));
        }
    }

    [Test]
    public void ConversionKeepsIndependentAxesAcrossUnitAndChunkWidthChanges()
    {
        var source = new SpatialSettings(-3, 8);
        var target = new SpatialSettings(0, 12);
        var sourceId = Register(source);
        var targetId = Register(target);
        var p2 = new SpatialPosition2(new ChunkIndex2(1000, -1000), Vector2.Zero);
        var p3 = new SpatialPosition3(new ChunkIndex3(1000, -1000, 0), new Vector3(0, 0, 4096.0f));
        var result2 = SpatialMath.ConvertPosition(p2, sourceId, source, targetId, target);
        var result3 = SpatialMath.ConvertPosition(p3, sourceId, source, targetId, target);
        AssertAxis(result2.Chunk.X, result2.Offset.X, (0, 256.0f));
        AssertAxis(result2.Chunk.Y, result2.Offset.Y, (-1, 3840.0f));
        AssertAxis(result3.Chunk.X, result3.Offset.X, (0, 256.0f));
        AssertAxis(result3.Chunk.Y, result3.Offset.Y, (-1, 3840.0f));
        AssertAxis(result3.Chunk.Z, result3.Offset.Z, (0, BitConverter.UInt32BitsToSingle(0x4083_126F)));
    }

    [Test]
    public void ConversionValidatesBothExplicitRegisteredDeclarationsBeforeEvaluatingCoordinates()
    {
        var settings = SpatialSettings.Default;
        var id = SpatialProfileId.Default;
        var unknown = new SpatialProfileId(Guid.NewGuid());

        _ = Assert.Throws<ArgumentException>(() => SpatialMath.ConvertPosition(default(SpatialPosition2), default, settings, id, settings));
        _ = Assert.Throws<ArgumentException>(() => SpatialMath.ConvertPosition(default(SpatialPosition3), id, settings, default, settings));
        _ = Assert.Throws<ArgumentException>(() => SpatialMath.ConvertPosition(default(SpatialPosition2), unknown, settings, id, settings));
        _ = Assert.Throws<ArgumentException>(() => SpatialMath.ConvertPosition(default(SpatialPosition3), id, settings, unknown, settings));
        _ = Assert.Throws<ArgumentException>(() => SpatialMath.ConvertPosition(default(SpatialPosition2), id, new SpatialSettings(1, 12), id, settings));
        _ = Assert.Throws<ArgumentException>(() => SpatialMath.ConvertPosition(default(SpatialPosition3), id, settings, id, new SpatialSettings(0, 13)));
        _ = Assert.Throws<ArgumentException>(() => SpatialMath.ConvertPosition(default(SpatialPosition2), id, default, id, settings));
        _ = Assert.Throws<ArgumentException>(() => SpatialMath.ConvertPosition(default(SpatialPosition3), id, settings, id, default));
    }

    [Test]
    public void DimensionalConversionRejectsUnsupportedDomainsAndNonfiniteValues()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => SpatialKernel.ConvertQuantity(1, -31, 0, 1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => SpatialKernel.ConvertQuantity(1, 0, 31, 1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => SpatialKernel.ConvertQuantity(1, 0, 0, -4));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => SpatialKernel.ConvertFeetQuantity(1, 31, 1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => SpatialKernel.ConvertFeetQuantity(1, 0, 4));
        _ = Assert.Throws<ArgumentException>(() => SpatialKernel.ConvertQuantity(float.NaN, 0, 0, 1));
        _ = Assert.Throws<ArgumentException>(() => SpatialKernel.ConvertFeetQuantity(float.PositiveInfinity, 0, 1));
        Assert.That(
            BitConverter.SingleToInt32Bits(SpatialKernel.ConvertQuantity(-0.0f, -30, 30, -3)),
            Is.EqualTo(int.MinValue));
        Assert.That(
            BitConverter.SingleToInt32Bits(SpatialKernel.ConvertQuantity(0.0f, 30, -30, 0)),
            Is.Zero);
    }

    [Test]
    public void DisplacementHandlesRawAliasesAndRejectsOverflowEvenWhenFarOrEqual()
    {
        var settings = SpatialSettings.Default;
        var p2 = new SpatialPosition2(new ChunkIndex2(2, -3), new Vector2(-8192, 12288));
        var p3 = new SpatialPosition3(new ChunkIndex3(2, -3, 1), new Vector3(-8192, 12288, -4096));

        Assert.That(SpatialMath.TryGetDisplacement(default, p2, settings, 0, out var d2), Is.True);
        Assert.That(SpatialMath.TryGetDisplacement(default, p3, settings, 0, out var d3), Is.True);
        Assert.That(d2, Is.EqualTo(Vector2.Zero));
        Assert.That(d3, Is.EqualTo(Vector3.Zero));
        var overflow2 = new SpatialPosition2(new ChunkIndex2(int.MaxValue, 0), new Vector2(4096, 0));
        var overflow3 = new SpatialPosition3(new ChunkIndex3(0, 0, int.MinValue), new Vector3(0, 0, -1));
        _ = Assert.Throws<OverflowException>(() => SpatialMath.TryGetDisplacement(default, overflow2, settings, 0, out _));
        _ = Assert.Throws<OverflowException>(() => SpatialMath.TryGetDisplacement(overflow3, overflow3, settings, 0, out _));
    }

    [Test]
    public void ExactDistanceAdmissionDoesNotMistakeRoundedInsideValuesOrPerAxisBoundsForEuclideanDistance()
    {
        var settings = SpatialSettings.Default;
        var raw2 = new SpatialPosition2(default, new Vector2(1, float.Epsilon));
        var raw3 = new SpatialPosition3(default, new Vector3(1, float.Epsilon, 0));
        Assert.That(SpatialMath.TryGetDisplacement(default, raw2, settings, 1, out var d2), Is.False);
        Assert.That(SpatialMath.TryGetDisplacement(default, raw3, settings, 1, out var d3), Is.False);
        Assert.That(d2, Is.EqualTo(default(Vector2)));
        Assert.That(d3, Is.EqualTo(default(Vector3)));
        Assert.That(SpatialMath.TryGetDisplacement(default, new SpatialPosition2(default, new Vector2(3, 4)), settings, 5, out d2), Is.True);
        Assert.That(d2, Is.EqualTo(new Vector2(3, 4)));
        Assert.That(SpatialMath.TryGetDisplacement(default, new SpatialPosition3(default, new Vector3(3, 4, 12)), settings, 13, out d3), Is.True);
        Assert.That(d3, Is.EqualTo(new Vector3(3, 4, 12)));
        Assert.That(SpatialMath.TryGetDisplacement(default, new SpatialPosition2(default, Vector2.One), settings, 1, out _), Is.False);
        Assert.That(SpatialMath.TryGetDisplacement(default, new SpatialPosition3(default, Vector3.One), settings, 1, out _), Is.False);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => SpatialMath.TryGetDisplacement(default(SpatialPosition2), default, settings, -0.0f, out _));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => SpatialMath.TryGetDisplacement(default(SpatialPosition3), default, settings, -0.0f, out _));
        Assert.That(SpatialMath.TryGetDisplacement(default, new SpatialPosition2(default, new Vector2(-float.Epsilon, 0)), settings, float.Epsilon, out d2), Is.True);
        Assert.That(d2.X, Is.EqualTo(-float.Epsilon));
        Assert.That(SpatialMath.TryGetDisplacement(default, new SpatialPosition3(default, new Vector3(0, 0, -float.Epsilon)), settings, 0, out d3), Is.False);
        Assert.That(d3, Is.EqualTo(default(Vector3)));
    }

    [Test]
    public void ExactFootQuantitiesUseNamedDimensionalCases()
    {
        (uint InputBits, int TargetExponent, int LengthDimension, uint ExpectedBits)[] cases = [
            (0x3F80_0000, 0, 1, 0x3E9C_0EBF),
            (0x3F80_0000, 3, 1, 0x399F_CD90),
            (0x3F80_0000, 0, 2, 0x3DBE_43F3),
            (0x3F80_0000, 0, -2, 0x412C_38FA),
        ];

        foreach (var (inputBits, targetExponent, lengthDimension, expectedBits) in cases)
        {
            var input = BitConverter.UInt32BitsToSingle(inputBits);
            var actual = SpatialKernel.ConvertFeetQuantity(input, targetExponent, lengthDimension);
            Assert.That(BitConverter.SingleToUInt32Bits(actual), Is.EqualTo(expectedBits));
        }

        Assert.That(BitConverter.SingleToUInt32Bits(SpatialKernel.ConvertFeetQuantity(float.Epsilon, 30, 3)), Is.Zero);
        _ = Assert.Throws<OverflowException>(() => SpatialKernel.ConvertFeetQuantity(float.MaxValue, -30, 3));
        Assert.That(
            BitConverter.SingleToInt32Bits(SpatialKernel.ConvertFeetQuantity(-0.0f, 0, -2)),
            Is.EqualTo(int.MinValue));
    }

    [Test]
    public void ExtremeCellDifferencesAreWidenedAndFarRequestsRefuseWithDefaultOutputs()
    {
        var settings = SpatialSettings.Default;
        var from2 = new SpatialPosition2(new ChunkIndex2(int.MinValue, 0), Vector2.Zero);
        var to2 = new SpatialPosition2(new ChunkIndex2(int.MaxValue, 0), Vector2.One);
        var from3 = new SpatialPosition3(new ChunkIndex3(0, 0, int.MinValue), Vector3.Zero);
        var to3 = new SpatialPosition3(new ChunkIndex3(0, 0, int.MaxValue), Vector3.One);

        Assert.That(SpatialMath.TryGetDisplacement(from2, to2, settings, 8192, out var d2), Is.False);
        Assert.That(SpatialMath.TryGetDisplacement(from3, to3, settings, 8192, out var d3), Is.False);
        Assert.That(d2, Is.EqualTo(default(Vector2)));
        Assert.That(d3, Is.EqualTo(default(Vector3)));
        Assert.That(SpatialMath.TryGetDisplacement(from2, to2, settings, float.MaxValue, out d2), Is.True);
        Assert.That(SpatialMath.TryGetDisplacement(from3, to3, settings, float.MaxValue, out d3), Is.True);
        var expected = BitConverter.UInt32BitsToSingle(0x5580_0000);
        Assert.That(d2.X, Is.EqualTo(expected));
        Assert.That(d3.Z, Is.EqualTo(expected));
        Assert.That(SpatialMath.TryGetDisplacement(to2, from2, settings, float.MaxValue, out d2), Is.True);
        Assert.That(d2.X, Is.EqualTo(-expected));
    }

    [Test]
    public void FrameRelativeUsesExplicitSettingsClosedExactExtentsAndRawAliases()
    {
        var settings = new SpatialSettings(-3, 8);
        var profile = Register(settings);
        var f2 = new SpatialFrame2(7, profile, settings, new SpatialPosition2(new ChunkIndex2(-1, 17), new Vector2(255, 0)), 9, new Vector2(-1), new Vector2(1));
        var f3 = new SpatialFrame3(7, profile, settings, new SpatialPosition3(new ChunkIndex3(-1, 17, 42), new Vector3(255, 0, 0)), 9, new Vector3(-1), new Vector3(1));
        var p2 = new SpatialPosition2(new ChunkIndex2(0, 17), Vector2.Zero);
        var p3 = new SpatialPosition3(new ChunkIndex3(0, 17, 42), Vector3.Zero);
        Assert.That(SpatialMath.TryToFrameRelative(p2, f2, out var r2), Is.True);
        Assert.That(SpatialMath.TryToFrameRelative(p3, f3, out var r3), Is.True);
        Assert.That(r2, Is.EqualTo(Vector2.UnitX));
        Assert.That(r3, Is.EqualTo(Vector3.UnitX));
        Assert.That(SpatialMath.TryToFrameRelative(new SpatialPosition2(f2.Origin.Chunk, new Vector2(254, 0)), f2, out r2), Is.True);
        Assert.That(r2.X, Is.EqualTo(-1));
        Assert.That(SpatialMath.TryToFrameRelative(new SpatialPosition3(f3.Origin.Chunk, new Vector3(254, 0, 0)), f3, out r3), Is.True);
        Assert.That(r3.X, Is.EqualTo(-1));
        Assert.That(SpatialMath.TryToFrameRelative(new SpatialPosition2(p2.Chunk, new Vector2(float.Epsilon, 0)), f2, out r2), Is.False);
        Assert.That(SpatialMath.TryToFrameRelative(new SpatialPosition3(p3.Chunk, new Vector3(float.Epsilon, 0, 0)), f3, out r3), Is.False);
        Assert.That(r2, Is.EqualTo(default(Vector2)));
        Assert.That(r3, Is.EqualTo(default(Vector3)));
        Assert.That(SpatialMath.TryToFrameRelative(new SpatialPosition2(f2.Origin.Chunk, new Vector2(256, 0)), f2, out r2), Is.True);
        Assert.That(r2.X, Is.EqualTo(1));
        var zeroExtent2 = new SpatialFrame2(1, SpatialProfileId.Default, SpatialSettings.Default, default, 1, Vector2.Zero, Vector2.Zero);
        var zeroExtent3 = new SpatialFrame3(1, SpatialProfileId.Default, SpatialSettings.Default, default, 1, Vector3.Zero, Vector3.Zero);
        Assert.That(SpatialMath.TryToFrameRelative(new SpatialPosition2(default, new Vector2(-float.Epsilon, 0)), zeroExtent2, out _), Is.False);
        Assert.That(SpatialMath.TryToFrameRelative(new SpatialPosition3(default, new Vector3(0, 0, -float.Epsilon)), zeroExtent3, out _), Is.False);
    }

    [Test]
    public void InvalidTryArgumentsAndCanonicalizationOverflowThrowRatherThanFalse()
    {
        _ = Assert.Throws<ArgumentException>(() => SpatialMath.Normalize(default(SpatialPosition2), default));
        _ = Assert.Throws<ArgumentException>(() => SpatialMath.Normalize(default(SpatialPosition3), default));
        _ = Assert.Throws<ArgumentException>(() => SpatialMath.TryGetDisplacement(default(SpatialPosition2), default, default, 0, out _));
        _ = Assert.Throws<ArgumentException>(() => SpatialMath.TryGetDisplacement(default(SpatialPosition3), default, default, 0, out _));

        foreach (var distance in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            _ = Assert.Throws<ArgumentException>(() => SpatialMath.TryGetDisplacement(default(SpatialPosition2), default, SpatialSettings.Default, distance, out _));
            _ = Assert.Throws<ArgumentException>(() => SpatialMath.TryGetDisplacement(default(SpatialPosition3), default, SpatialSettings.Default, distance, out _));
        }

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => SpatialMath.TryGetDisplacement(default(SpatialPosition2), default, SpatialSettings.Default, -1, out _));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => SpatialMath.TryGetDisplacement(default(SpatialPosition3), default, SpatialSettings.Default, -1, out _));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => SpatialMath.TryToFrameRelative(default, default(SpatialFrame2), out _));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => SpatialMath.TryToFrameRelative(default, default(SpatialFrame3), out _));
        _ = Assert.Throws<OverflowException>(() => SpatialMath.TryToFrameRelative(new SpatialPosition2(new ChunkIndex2(int.MaxValue, 0), new Vector2(4096, 0)), SpatialValueTests.Frame2(), out _));
        _ = Assert.Throws<OverflowException>(() => SpatialMath.TryToFrameRelative(new SpatialPosition3(new ChunkIndex3(0, 0, int.MinValue), new Vector3(0, 0, -float.Epsilon)), SpatialValueTests.Frame3(), out _));
    }

    [Test]
    public void NormalizationChecksExactAndRoundedCarriesAtSignedIntBoundariesWithoutPartialState()
    {
        var settings = SpatialSettings.Default;
        var maximum2 = new SpatialPosition2(new ChunkIndex2(int.MaxValue, 4), new Vector2(4096, 4096));
        var maximum3 = new SpatialPosition3(new ChunkIndex3(4, 5, int.MaxValue), new Vector3(4096, 0, 4096));

        _ = Assert.Throws<OverflowException>(() => SpatialMath.Normalize(maximum2, settings));
        _ = Assert.Throws<OverflowException>(() => SpatialMath.Normalize(maximum3, settings));
        Assert.That(maximum2.Chunk.X, Is.EqualTo(int.MaxValue));
        Assert.That(maximum3.Chunk.X, Is.EqualTo(4));

        foreach (var offset in new[] { -1, -float.Epsilon, -float.MaxValue })
        {
            _ = Assert.Throws<OverflowException>(() => SpatialMath.Normalize(new SpatialPosition2(new ChunkIndex2(int.MinValue, 0), new Vector2(offset, 0)), settings));
            _ = Assert.Throws<OverflowException>(() => SpatialMath.Normalize(new SpatialPosition3(new ChunkIndex3(0, 0, int.MinValue), new Vector3(0, 0, offset)), settings));
        }

        _ = Assert.Throws<OverflowException>(() => SpatialMath.Normalize(new SpatialPosition2(default, new Vector2(float.MaxValue)), settings));
        _ = Assert.Throws<OverflowException>(() => SpatialMath.Normalize(new SpatialPosition3(default, new Vector3(-float.MaxValue)), settings));
        var fromMinimum = SpatialMath.Normalize(new SpatialPosition2(new ChunkIndex2(int.MinValue, int.MinValue), new Vector2(4096)), settings);
        Assert.That(fromMinimum.Chunk, Is.EqualTo(new ChunkIndex2(int.MinValue + 1, int.MinValue + 1)));
        var fromMaximum = SpatialMath.Normalize(new SpatialPosition3(new ChunkIndex3(int.MaxValue, int.MaxValue, int.MaxValue), new Vector3(-4096)), settings);
        Assert.That(fromMaximum.Chunk, Is.EqualTo(new ChunkIndex3(int.MaxValue - 1, int.MaxValue - 1, int.MaxValue - 1)));
        var cancellation = new SpatialPosition2(new ChunkIndex2(int.MinValue, 0), new Vector2(float.ScaleB(1.0f, 43), 0));
        Assert.That(SpatialMath.Normalize(cancellation, settings), Is.EqualTo(default(SpatialPosition2)));
    }

    [Test]
    public void NormalizationUsesFloorRemainderAtSelectedChunkBoundaries()
    {
        (int Exponent, uint NextOffsetBits)[] widths = [
            (8, 0x3800_0000),
            (12, 0x3A00_0000),
            (16, 0x3C00_0000),
        ];
        var negativeZero = BitConverter.UInt32BitsToSingle(0x8000_0000);

        foreach (var (exponent, nextOffsetBits) in widths)
        {
            var settings = new SpatialSettings(0, exponent);
            var width = settings.ChunkWidth;
            var nextOffset = BitConverter.UInt32BitsToSingle(nextOffsetBits);
            var cases = new (float Offset, (int Chunk, float Offset) Expected)[] {
                (negativeZero, (-7, 0.0f)),
                (-float.Epsilon, (-7, 0.0f)),
                (-1.0f, (-8, width - 1.0f)),
                (width, (-6, 0.0f)),
                (-width, (-8, 0.0f)),
                (float.BitIncrement(width), (-6, nextOffset)),
            };

            foreach (var (offset, expected) in cases)
            {
                var p2 = new SpatialPosition2(new ChunkIndex2(-7, 5), new Vector2(offset, negativeZero));
                var p3 = new SpatialPosition3(new ChunkIndex3(-7, 5, -7), new Vector3(offset, negativeZero, offset));
                var normalized2 = SpatialMath.Normalize(p2, settings);
                var normalized3 = SpatialMath.Normalize(p3, settings);
                AssertAxis(normalized2.Chunk.X, normalized2.Offset.X, expected);
                AssertAxis(normalized3.Chunk.X, normalized3.Offset.X, expected);
                AssertAxis(normalized3.Chunk.Z, normalized3.Offset.Z, expected);
                Assert.That(normalized2.Chunk.Y, Is.EqualTo(5));
                Assert.That(normalized3.Chunk.Y, Is.EqualTo(5));
                Assert.That(BitConverter.SingleToInt32Bits(normalized2.Offset.Y), Is.Zero);
                Assert.That(BitConverter.SingleToInt32Bits(normalized3.Offset.Y), Is.Zero);
                Assert.That(normalized2.Offset.X, Is.InRange(0, float.BitDecrement(width)));
                Assert.That(SpatialMath.Normalize(normalized2, settings), Is.EqualTo(normalized2));
                Assert.That(SpatialMath.Normalize(normalized3, settings), Is.EqualTo(normalized3));
                Assert.That(p2.Offset.X, Is.EqualTo(offset));
            }
        }
    }



    [TestCase(12)]
    [TestCase(13)]
    public void SignedAdjacentSeamsRetainTheExactSmallGapAndUseClosedDistanceBounds(int exponent)
    {
        var settings = new SpatialSettings(0, exponent);
        var width = settings.ChunkWidth;
        var inside = float.BitDecrement(width);
        var gap = width - inside;

        Assert.That(gap, Is.EqualTo(float.ScaleB(width, -24)));
        Assert.That(float.BitIncrement(width) - width, Is.EqualTo(gap * 2));
        Assert.That(float.BitIncrement(-width) + width, Is.EqualTo(gap));
        Assert.That(-width - float.BitDecrement(-width), Is.EqualTo(gap * 2));

        foreach (var cell in new[] { int.MinValue, -17, -1, 0, 17, int.MaxValue - 1 })
        {
            var from2 = new SpatialPosition2(new ChunkIndex2(cell, 0), new Vector2(inside, 0));
            var to2 = new SpatialPosition2(new ChunkIndex2(cell + 1, 0), new Vector2(gap / 2, 0));
            var from3 = new SpatialPosition3(new ChunkIndex3(0, 0, cell), new Vector3(0, 0, inside));
            var to3 = new SpatialPosition3(new ChunkIndex3(0, 0, cell + 1), new Vector3(0, 0, gap / 2));
            var expectedBits = exponent switch {
                12 => 0x39C0_0000u,
                13 => 0x3A40_0000u,
                _ => throw new AssertionException("Unexpected seam exponent."),
            };
            var expected = BitConverter.UInt32BitsToSingle(expectedBits);
            Assert.That(SpatialMath.TryGetDisplacement(from2, to2, settings, expected, out var d2), Is.True);
            Assert.That(SpatialMath.TryGetDisplacement(from3, to3, settings, expected, out var d3), Is.True);
            Assert.That(d2, Is.EqualTo(new Vector2(expected, 0)));
            Assert.That(d3, Is.EqualTo(new Vector3(0, 0, expected)));
            Assert.That(SpatialMath.TryGetDisplacement(to2, from2, settings, expected, out d2), Is.True);
            Assert.That(SpatialMath.TryGetDisplacement(to3, from3, settings, expected, out d3), Is.True);
            Assert.That(d2.X, Is.EqualTo(-expected));
            Assert.That(d3.Z, Is.EqualTo(-expected));
            Assert.That(SpatialMath.TryGetDisplacement(from2, to2, settings, float.BitDecrement(expected), out d2), Is.False);
            Assert.That(SpatialMath.TryGetDisplacement(from3, to3, settings, float.BitDecrement(expected), out d3), Is.False);
            Assert.That(d2, Is.EqualTo(default(Vector2)));
            Assert.That(d3, Is.EqualTo(default(Vector3)));
        }
    }
}
