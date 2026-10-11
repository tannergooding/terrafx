// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX.Numerics;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class SpatialValueTests
{
    internal static SpatialFrame2 Frame2() => new SpatialFrame2(1, SpatialProfileId.Default, SpatialSettings.Default, default, 1, new Vector2(-4096), new Vector2(4096));

    internal static SpatialFrame3 Frame3() => new SpatialFrame3(1, SpatialProfileId.Default, SpatialSettings.Default, default, 1, new Vector3(-4096), new Vector3(4096));

    private static void Layout<T>(int size, Type[] backing)
        where T : unmanaged
    {
        Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<T>(), Is.False);
        Assert.That(Marshal.SizeOf<T>(), Is.EqualTo(size));
        Assert.That(typeof(T).IsLayoutSequential, Is.True);
        var fields = typeof(T).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.That(fields.Select(field => field.FieldType), Is.EqualTo(backing));
        var offset = 0;

        foreach (var field in fields)
        {
            Assert.That(Marshal.OffsetOf<T>(field.Name).ToInt32(), Is.EqualTo(offset));
            offset += Marshal.SizeOf(field.FieldType);
        }
    }

    [Test]
    public void BoundsRetainClosedEndpointsAndCompareExactSignedCellCoordinates()
    {
        var settings = SpatialSettings.Default;
        var closed2 = new SpatialPosition2(new ChunkIndex2(int.MaxValue, -3), new Vector2(4096, 4096));
        var alias2 = new SpatialPosition2(new ChunkIndex2(int.MaxValue, -2), new Vector2(4096, 0));
        var closed3 = new SpatialPosition3(new ChunkIndex3(int.MaxValue, -3, 7), new Vector3(4096, 4096, 4096));
        var alias3 = new SpatialPosition3(new ChunkIndex3(int.MaxValue, -2, 8), new Vector3(4096, 0, 0));
        var b2 = new SpatialBounds2(closed2, alias2, settings);
        var b3 = new SpatialBounds3(closed3, alias3, settings);

        Assert.That(b2.Minimum, Is.EqualTo(closed2));
        Assert.That(b2.Maximum, Is.EqualTo(alias2));
        Assert.That(b3.Minimum, Is.EqualTo(closed3));
        Assert.That(b3.Maximum, Is.EqualTo(alias3));
        var near2 = new SpatialPosition2(new ChunkIndex2(int.MaxValue, 0), Vector2.One);
        var near3 = new SpatialPosition3(new ChunkIndex3(int.MaxValue, 0, 0), Vector3.One);
        _ = Assert.Throws<ArgumentException>(() => _ = new SpatialBounds2(near2, new SpatialPosition2(near2.Chunk, Vector2.Zero), settings));
        _ = Assert.Throws<ArgumentException>(() => _ = new SpatialBounds3(near3, new SpatialPosition3(near3.Chunk, Vector3.Zero), settings));
        _ = Assert.Throws<ArgumentException>(() => _ = new SpatialBounds2(default, default, default));
        _ = Assert.Throws<ArgumentException>(() => _ = new SpatialBounds3(default, default, default));
        var reversedCells = new SpatialBounds2(new SpatialPosition2(new ChunkIndex2(1, 1), new Vector2(-4096)), default, settings);
        Assert.That(reversedCells.Minimum.Chunk, Is.EqualTo(new ChunkIndex2(1, 1)));
        _ = new SpatialBounds3(new SpatialPosition3(new ChunkIndex3(int.MinValue, int.MinValue, int.MinValue), Vector3.Zero), new SpatialPosition3(new ChunkIndex3(int.MaxValue, int.MaxValue, int.MaxValue), Vector3.Zero), settings);
    }

    [Test]
    public void ConcurrentRegistrationAndLookupAreAppendOnlyAndIdempotent()
    {
        var id = new SpatialProfileId(Guid.NewGuid());
        var settings = new SpatialSettings(-3, 13);
        _ = Parallel.For(0, 100, _ => {
            SpatialProfiles.Register(id, settings);
            Assert.That(SpatialProfiles.TryGetSettings(id, out var found), Is.True);
            Assert.That(found, Is.EqualTo(settings));
        });
        _ = Assert.Throws<ArgumentException>(() => SpatialProfiles.Register(id, new SpatialSettings(-3, 12)));
        Assert.That(SpatialProfiles.TryGetSettings(id, out var retained), Is.True);
        Assert.That(retained, Is.EqualTo(settings));
    }

    [Test]
    public void DefaultProfileIsUsableWithoutAHostAndInvalidOrConflictingRegistrationDoesNotMutate()
    {
        var id = SpatialProfileId.Default;
        Assert.That(id.Value, Is.EqualTo(new Guid("c2ddc5d8-9ee6-8676-bdef-dffd36a56d89")));
        Assert.That(id.IsValid, Is.True);
        Assert.That(default(SpatialProfileId).IsValid, Is.False);
        _ = Assert.Throws<ArgumentException>(() => _ = new SpatialProfileId(Guid.Empty));
        Assert.That(SpatialProfiles.TryGetSettings(id, out var settings), Is.True);
        Assert.That(settings, Is.EqualTo(SpatialSettings.Default));
        SpatialProfiles.Register(id, settings);
        _ = Assert.Throws<ArgumentException>(() => SpatialProfiles.Register(id, new SpatialSettings(1, 12)));
        _ = Assert.Throws<ArgumentException>(() => SpatialProfiles.Register(default, settings));
        _ = Assert.Throws<ArgumentException>(() => SpatialProfiles.TryGetSettings(default, out _));
        var absentId = new SpatialProfileId(Guid.NewGuid());
        _ = Assert.Throws<ArgumentException>(() => SpatialProfiles.Register(absentId, default));
        Assert.That(SpatialProfiles.TryGetSettings(absentId, out var absent), Is.False);
        Assert.That(absent, Is.EqualTo(default(SpatialSettings)));
        Assert.That(SpatialProfiles.TryGetSettings(id, out settings), Is.True);
        Assert.That(settings, Is.EqualTo(SpatialSettings.Default));
        Assert.That(id == new SpatialProfileId(id.Value), Is.True);
        Assert.That(id != new SpatialProfileId(id.Value), Is.False);
        Assert.That(id.Equals((object)new SpatialProfileId(id.Value)), Is.True);
        Assert.That(id.GetHashCode(), Is.EqualTo(new SpatialProfileId(id.Value).GetHashCode()));
        Assert.That(id.ToString(), Is.EqualTo(id.Value.ToString()));
        Assert.That(id.ToString("N", CultureInfo.InvariantCulture), Is.EqualTo(id.Value.ToString("N", CultureInfo.InvariantCulture)));
        Assert.That(((IFormattable)id).ToString("B", CultureInfo.InvariantCulture), Is.EqualTo(id.Value.ToString("B", CultureInfo.InvariantCulture)));
        var previous = new SpatialProfileId(new Guid("2ed8b750-e1a0-4b46-9000-000000000002"));
        _ = Assert.Throws<ArgumentException>(() => SpatialProfiles.Register(previous, SpatialSettings.Default));
        Assert.That(SpatialProfiles.TryGetSettings(previous, out absent), Is.False);
        Assert.That(absent, Is.EqualTo(default(SpatialSettings)));
    }

    [Test]
    public void EqualityIsStoredFieldsNotNormalizedCoordinateIdentity()
    {
        var alias2 = new SpatialPosition2(default, new Vector2(4096, 0));
        var alias3 = new SpatialPosition3(default, new Vector3(4096, 0, 0));
        var normalized2 = SpatialMath.Normalize(alias2, SpatialSettings.Default);
        var normalized3 = SpatialMath.Normalize(alias3, SpatialSettings.Default);

        Assert.That(alias2 == normalized2, Is.False);
        Assert.That(alias3 == normalized3, Is.False);
        Assert.That(alias2 != normalized2, Is.True);
        Assert.That(alias3 != normalized3, Is.True);
        Assert.That(alias2.Equals((object)normalized2), Is.False);
        Assert.That(alias3.Equals((object)normalized3), Is.False);

        var zero2 = new SpatialPosition2(default, new Vector2(-0.0f));
        var zero3 = new SpatialPosition3(default, new Vector3(-0.0f));
        Assert.That(zero2 == default, Is.True);
        Assert.That(zero3 == default, Is.True);
        Assert.That(zero2.GetHashCode(), Is.EqualTo(default(SpatialPosition2).GetHashCode()));
        Assert.That(zero3.GetHashCode(), Is.EqualTo(default(SpatialPosition3).GetHashCode()));
        Assert.That(new ChunkIndex2(-1, 1) != new ChunkIndex2(1, -1), Is.True);
        Assert.That(new ChunkIndex3(0, 0, int.MinValue) != new ChunkIndex3(0, 0, int.MaxValue), Is.True);
    }

    [Test]
    public void FramesRejectInvalidIdentityProfileOriginAndExtentsWithoutAnInventedCap()
    {
        var settings = SpatialSettings.Default;
        var profile = SpatialProfileId.Default;
        var unknown = new SpatialProfileId(Guid.NewGuid());

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SpatialFrame2(0, profile, settings, default, 1, default, default));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SpatialFrame3(0, profile, settings, default, 1, default, default));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SpatialFrame2(1, profile, settings, default, 0, default, default));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SpatialFrame3(1, profile, settings, default, -1, default, default));
        _ = Assert.Throws<ArgumentException>(() => _ = new SpatialFrame2(1, default, settings, default, 1, default, default));
        _ = Assert.Throws<ArgumentException>(() => _ = new SpatialFrame3(1, unknown, settings, default, 1, default, default));
        _ = Assert.Throws<ArgumentException>(() => _ = new SpatialFrame2(1, profile, new SpatialSettings(0, 13), default, 1, default, default));
        _ = Assert.Throws<ArgumentException>(() => _ = new SpatialFrame3(1, profile, default, default, 1, default, default));

        foreach (var invalid in new[] { -0.0f, -1.0f, 4096.0f })
        {
            _ = Assert.Throws<ArgumentException>(() => _ = new SpatialFrame2(1, profile, settings, new SpatialPosition2(default, new Vector2(invalid)), 1, default, default));
            _ = Assert.Throws<ArgumentException>(() => _ = new SpatialFrame3(1, profile, settings, new SpatialPosition3(default, new Vector3(invalid)), 1, default, default));
        }

        _ = Assert.Throws<ArgumentException>(() => _ = new SpatialFrame2(1, profile, settings, default, 1, Vector2.One, Vector2.Zero));
        _ = Assert.Throws<ArgumentException>(() => _ = new SpatialFrame3(1, profile, settings, default, 1, Vector3.Zero, new Vector3(0, 0, -1)));
        _ = Assert.Throws<ArgumentException>(() => _ = new SpatialFrame2(1, profile, settings, default, 1, new Vector2(float.NaN), Vector2.One));
        _ = Assert.Throws<ArgumentException>(() => _ = new SpatialFrame3(1, profile, settings, default, 1, Vector3.Zero, new Vector3(float.PositiveInfinity)));
        var frame2 = new SpatialFrame2(uint.MaxValue, profile, settings, default, long.MaxValue, new Vector2(-float.MaxValue), new Vector2(float.MaxValue));
        var frame3 = new SpatialFrame3(uint.MaxValue, profile, settings, default, long.MaxValue, new Vector3(-float.MaxValue), new Vector3(float.MaxValue));
        Assert.That(frame2.Minimum.X, Is.EqualTo(-float.MaxValue));
        Assert.That(frame3.Maximum.Z, Is.EqualTo(float.MaxValue));
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void OperationsRejectNonfiniteOffsetsEvenWhenSuppliedThroughSafeBitCopies(float invalid)
    {
        Unsafe.SkipInit(out InlineArray16<byte> bytes2Storage); // The next Clear initializes every exposed byte.
        Unsafe.SkipInit(out InlineBuffer32<byte> bytes3Storage); // The next Clear initializes every exposed byte.
        Span<byte> bytes2 = bytes2Storage;
        var bytes3 = bytes3Storage[..24];
        bytes2.Clear();
        bytes3.Clear();
        Assert.That(BitConverter.TryWriteBytes(bytes2[8..12], invalid), Is.True);
        Assert.That(BitConverter.TryWriteBytes(bytes3[20..24], invalid), Is.True);
        var p2 = MemoryMarshal.Read<SpatialPosition2>(bytes2);
        var p3 = MemoryMarshal.Read<SpatialPosition3>(bytes3);
        var settings = SpatialSettings.Default;
        var profile = SpatialProfileId.Default;
        _ = Assert.Throws<ArgumentException>(() => SpatialMath.Normalize(p2, settings));
        _ = Assert.Throws<ArgumentException>(() => SpatialMath.Normalize(p3, settings));
        _ = Assert.Throws<ArgumentException>(() => SpatialMath.ConvertPosition(p2, profile, settings, profile, settings));
        _ = Assert.Throws<ArgumentException>(() => SpatialMath.ConvertPosition(p3, profile, settings, profile, settings));
        _ = Assert.Throws<ArgumentException>(() => SpatialMath.TryGetDisplacement(p2, default, settings, 0, out _));
        _ = Assert.Throws<ArgumentException>(() => SpatialMath.TryGetDisplacement(default, p3, settings, 0, out _));
        _ = Assert.Throws<ArgumentException>(() => SpatialMath.TryToFrameRelative(p2, Frame2(), out _));
        _ = Assert.Throws<ArgumentException>(() => SpatialMath.TryToFrameRelative(p3, Frame3(), out _));
        _ = Assert.Throws<ArgumentException>(() => _ = new SpatialBounds2(p2, default, settings));
        _ = Assert.Throws<ArgumentException>(() => _ = new SpatialBounds3(default, p3, settings));
        _ = Assert.Throws<ArgumentException>(() => _ = new SpatialRay2(p2, Vector2.UnitX, 0));
        _ = Assert.Throws<ArgumentException>(() => _ = new SpatialRay3(p3, Vector3.UnitX, 0));
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void PositionConstructorsRejectEveryNonfiniteLane(float invalid)
    {
        _ = Assert.Throws<ArgumentException>(() => _ = new SpatialPosition2(default, new Vector2(invalid, 0)));
        _ = Assert.Throws<ArgumentException>(() => _ = new SpatialPosition2(default, new Vector2(0, invalid)));
        _ = Assert.Throws<ArgumentException>(() => _ = new SpatialPosition3(default, new Vector3(invalid, 0, 0)));
        _ = Assert.Throws<ArgumentException>(() => _ = new SpatialPosition3(default, new Vector3(0, invalid, 0)));
        _ = Assert.Throws<ArgumentException>(() => _ = new SpatialPosition3(default, new Vector3(0, 0, invalid)));
    }

    [Test]
    public void PositionLayoutsContainOnlyAcceptedFieldsAndRawFiniteBits()
    {
        Layout<ChunkIndex2>(8, [typeof(int), typeof(int)]);
        Layout<ChunkIndex3>(12, [typeof(int), typeof(int), typeof(int)]);
        Layout<SpatialPosition2>(16, [typeof(ChunkIndex2), typeof(Vector2)]);
        Layout<SpatialPosition3>(24, [typeof(ChunkIndex3), typeof(Vector3)]);

        Assert.That(MemoryMarshal.AsBytes(new SpatialPosition2[2].AsSpan()).Length, Is.EqualTo(32));
        Assert.That(MemoryMarshal.AsBytes(new SpatialPosition3[2].AsSpan()).Length, Is.EqualTo(48));

        var c2 = new ChunkIndex2(int.MinValue, int.MaxValue);
        var c3 = new ChunkIndex3(int.MinValue, int.MaxValue, -1);
        var p2 = new SpatialPosition2(c2, new Vector2(-0.0f, float.MaxValue));
        var p3 = new SpatialPosition3(c3, new Vector3(-0.0f, -float.MaxValue, float.Epsilon));
        Assert.That(p2.Chunk, Is.EqualTo(c2));
        Assert.That(p3.Chunk, Is.EqualTo(c3));
        Assert.That(BitConverter.SingleToInt32Bits(p2.Offset.X), Is.EqualTo(int.MinValue));
        Assert.That(BitConverter.SingleToInt32Bits(p3.Offset.X), Is.EqualTo(int.MinValue));
        Assert.That(p2.Offset.Y, Is.EqualTo(float.MaxValue));
        Assert.That(p3.Offset.Y, Is.EqualTo(-float.MaxValue));
        Assert.That(p3.Offset.Z, Is.EqualTo(float.Epsilon));
        var bytes2 = MemoryMarshal.AsBytes(new[] { p2 }.AsSpan());
        var bytes3 = MemoryMarshal.AsBytes(new[] { p3 }.AsSpan());
        Assert.That(BitConverter.ToInt32(bytes2[..4]), Is.EqualTo(int.MinValue));
        Assert.That(BitConverter.ToInt32(bytes2[4..8]), Is.EqualTo(int.MaxValue));
        Assert.That(BitConverter.ToInt32(bytes2[8..12]), Is.EqualTo(int.MinValue));
        Assert.That(BitConverter.ToInt32(bytes3[8..12]), Is.EqualTo(-1));
        Assert.That(BitConverter.ToInt32(bytes3[12..16]), Is.EqualTo(int.MinValue));
    }

    [Test]
    public void PublicSurfaceHasOnlyNormativeMembersAndDimensionalOverloads()
    {
        Type[] values = [typeof(ChunkIndex2), typeof(ChunkIndex3), typeof(SpatialSettings), typeof(SpatialProfileId), typeof(SpatialPosition2), typeof(SpatialPosition3), typeof(SpatialFrame2), typeof(SpatialFrame3), typeof(SpatialBounds2), typeof(SpatialBounds3), typeof(RelativePose2), typeof(RelativePose3), typeof(SpatialRay2), typeof(SpatialRay3)];

        foreach (var value in values)
        {
            Assert.That(value.Namespace, Is.EqualTo("TerraFX"));
            Assert.That(value.Assembly, Is.EqualTo(typeof(SpatialMath).Assembly));
            Assert.That(value.GetConstructors().Length, Is.EqualTo(1));
            Assert.That(value.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static), Is.Empty);
        }

        Type[] descriptors = [typeof(SpatialFrame2), typeof(SpatialFrame3), typeof(SpatialBounds2), typeof(SpatialBounds3), typeof(RelativePose2), typeof(RelativePose3)];

        foreach (var descriptor in descriptors)
        {
            var methods = descriptor.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
            Assert.That(methods.All(method => method.IsSpecialName && method.Name.StartsWith("get_", StringComparison.Ordinal)), Is.True);
        }

        var math = typeof(SpatialMath).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.That(math.Length, Is.EqualTo(8));
        Assert.That(math.GroupBy(method => method.Name).ToDictionary(group => group.Key, group => group.Count()),
            Is.EquivalentTo(new Dictionary<string, int> { ["Normalize"] = 2, ["ConvertPosition"] = 2, ["TryGetDisplacement"] = 2, ["TryToFrameRelative"] = 2 }));
        var profiles = typeof(SpatialProfiles).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.That(profiles.Select(method => method.Name), Is.EquivalentTo(new[] { "Register", "TryGetSettings" }));
    }

    [TestCase(0.99999f, false)]
    [TestCase(0.999991f, true)]
    [TestCase(1.0f, true)]
    [TestCase(1.000009f, true)]
    [TestCase(1.00001f, false)]
    [TestCase(0.0f, false)]
    [TestCase(float.Epsilon, false)]
    [TestCase(float.MaxValue, false)]
    [TestCase(float.NaN, false)]
    [TestCase(float.PositiveInfinity, false)]
    public void RaysAdmitExactNearUnitToleranceAndNormalizeOnlyAcceptedDirection(float magnitude, bool admitted)
    {
        var raw2 = new SpatialPosition2(default, new Vector2(-4096, -0.0f));
        var raw3 = new SpatialPosition3(default, new Vector3(4096, -4096, -0.0f));
        var direction2 = new Vector2(magnitude, -0.0f);
        var direction3 = new Vector3(0, magnitude, -0.0f);

        if (!admitted)
        {
            _ = Assert.Throws<ArgumentException>(() => _ = new SpatialRay2(raw2, direction2, 0));
            _ = Assert.Throws<ArgumentException>(() => _ = new SpatialRay3(raw3, direction3, 0));
            return;
        }

        var ray2 = new SpatialRay2(raw2, direction2, 9000);
        var ray3 = new SpatialRay3(raw3, direction3, float.MaxValue);
        Assert.That(ray2.Origin, Is.EqualTo(raw2));
        Assert.That(ray3.Origin, Is.EqualTo(raw3));
        Assert.That(ray2.Direction, Is.EqualTo(Vector2.Normalize(direction2)));
        Assert.That(ray3.Direction, Is.EqualTo(Vector3.Normalize(direction3)));
        Assert.That(BitConverter.SingleToInt32Bits(ray2.Direction.Y), Is.EqualTo(int.MinValue));
        Assert.That(BitConverter.SingleToInt32Bits(ray3.Direction.Z), Is.EqualTo(int.MinValue));
        Assert.That(ray2.MaxDistance, Is.EqualTo(9000));
        Assert.That(ray3.MaxDistance, Is.EqualTo(float.MaxValue));
    }

    [Test]
    public void RaysRejectInvalidDistancesAndCompareOnlyStoredFields()
    {
        foreach (var distance in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            _ = Assert.Throws<ArgumentException>(() => _ = new SpatialRay2(default, Vector2.UnitX, distance));
            _ = Assert.Throws<ArgumentException>(() => _ = new SpatialRay3(default, Vector3.UnitX, distance));
        }

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SpatialRay2(default, Vector2.UnitX, -1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SpatialRay3(default, Vector3.UnitX, -float.Epsilon));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SpatialRay2(default, Vector2.UnitX, -0.0f));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SpatialRay3(default, Vector3.UnitX, -0.0f));
        var r2 = new SpatialRay2(default, Vector2.Normalize(new Vector2(1, 2)), 0.0f);
        var r3 = new SpatialRay3(default, Vector3.Normalize(new Vector3(1, 2, 3)), 0);
        Assert.That(BitConverter.SingleToInt32Bits(r2.MaxDistance), Is.EqualTo(0));
        Assert.That(r2 == new SpatialRay2(default, r2.Direction, 0), Is.True);
        Assert.That(r3 == new SpatialRay3(default, r3.Direction, 0), Is.True);
        Assert.That(r2.Equals((object)new SpatialRay2(default, r2.Direction, 0)), Is.True);
        Assert.That(r3.Equals((object)new SpatialRay3(default, r3.Direction, 0)), Is.True);
        Assert.That(r2.GetHashCode(), Is.EqualTo(new SpatialRay2(default, r2.Direction, 0).GetHashCode()));
        Assert.That(r3.GetHashCode(), Is.EqualTo(new SpatialRay3(default, r3.Direction, 0).GetHashCode()));
        Assert.That(r2 != new SpatialRay2(new SpatialPosition2(default, new Vector2(4096)), r2.Direction, 0), Is.True);
        Assert.That(r3 != new SpatialRay3(new SpatialPosition3(default, new Vector3(4096)), r3.Direction, 0), Is.True);
    }

    [Test]
    public void RelativePosesUseSceneNormAdmissionNormalizeOnceAndKeepQuaternionSign()
    {
        var f2 = Frame2();
        var f3 = Frame3();

        var p2 = new RelativePose2(new Vector2(-0.0f, float.MaxValue), -0.0f, f2);
        Assert.That(BitConverter.SingleToInt32Bits(p2.Angle), Is.EqualTo(int.MinValue));
        Assert.That(BitConverter.SingleToInt32Bits(p2.LocalPosition.X), Is.EqualTo(int.MinValue));
        Assert.That(p2.Frame.Revision, Is.EqualTo(f2.Revision));
        var p3 = new RelativePose3(Vector3.One, new Quaternion(1, 2, 3, -4), f3);
        Assert.That(p3.Rotation, Is.EqualTo(Quaternion.Normalize(new Quaternion(1, 2, 3, -4))));
        Assert.That(p3.Rotation.W, Is.LessThan(0));
        Assert.That(p3.Frame.Origin, Is.EqualTo(f3.Origin));

        _ = new RelativePose3(Vector3.Zero, new Quaternion(0, 0, 0, 0.000002f), f3);

        foreach (var rotation in new[] { default, new Quaternion(0, 0, 0, float.Epsilon), new Quaternion(0, 0, 0, 0.000001f), new Quaternion(float.MaxValue, 0, 0, 0), new Quaternion(float.NaN, 0, 0, 1) })
        {
            _ = Assert.Throws<ArgumentException>(() => _ = new RelativePose3(Vector3.Zero, rotation, f3));
        }

        _ = Assert.Throws<ArgumentException>(() => _ = new RelativePose2(new Vector2(float.NaN), 0, f2));
        _ = Assert.Throws<ArgumentException>(() => _ = new RelativePose2(Vector2.Zero, float.PositiveInfinity, f2));
        _ = Assert.Throws<ArgumentException>(() => _ = new RelativePose3(new Vector3(float.PositiveInfinity), Quaternion.Identity, f3));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new RelativePose2(Vector2.Zero, 0, default));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new RelativePose3(Vector3.Zero, Quaternion.Identity, default));
    }

    [TestCase("")]
    [TestCase("--capacity")]
    public void SeparateUnsafeDisabledConsumerExecutesAndCapacityIsProcessIsolated(string argument)
    {
        var testDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = testDirectory.Parent ?? throw new InvalidOperationException("Missing configuration directory.");
        var projectDirectory = configuration.Parent ?? throw new InvalidOperationException("Missing project directory.");
        var testsDirectory = projectDirectory.Parent ?? throw new InvalidOperationException("Missing tests output directory.");
        var consumer = Path.Combine(testsDirectory.FullName, "TerraFX.Core.SpatialConsumer", configuration.Name, testDirectory.Name, "TerraFX.Core.SpatialConsumer.dll");
        Assert.That(File.Exists(consumer), Is.True, "The consumer project reference must build the separate witness.");
        var start = new ProcessStartInfo("dotnet") {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(consumer);

        if (argument.Length != 0)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("The spatial witness process did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(60000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("The spatial public witness exceeded its 60-second execution bound.");
        }

        Task.WaitAll(output, errors);
        Assert.That(process.ExitCode, Is.Zero, output.Result + errors.Result);
        Assert.That(output.Result, Does.Contain(argument.Length == 0 ? "Spatial public consumer passed:" : "Spatial registry capacity passed: 4096"));
    }

    [Test]
    public void SettingsAdmitEveryExponentAndRejectInvalidDefaultsAndNearestOutsideValues()
    {
        Assert.That(SpatialSettings.Default, Is.EqualTo(new SpatialSettings(0, 12)));
        Assert.That(SpatialSettings.Default.ChunkWidth, Is.EqualTo(4096));
        Assert.That(default(SpatialSettings), Is.Not.EqualTo(SpatialSettings.Default));
        _ = Assert.Throws<ArgumentException>(() => _ = default(SpatialSettings).ChunkWidth);

        for (var unit = -30; unit <= 30; unit++)
        {
            for (var chunk = 8; chunk <= 16; chunk++)
            {
                var settings = new SpatialSettings(unit, chunk);
                Assert.That(settings.MetersPerUnitExponent, Is.EqualTo(unit));
                Assert.That(settings.ChunkSizeExponent, Is.EqualTo(chunk));
                Assert.That(settings.ChunkWidth, Is.EqualTo(1 << chunk));
                Assert.That(settings == new SpatialSettings(unit, chunk), Is.True);
                Assert.That(settings != new SpatialSettings(unit, chunk), Is.False);
                Assert.That(settings.Equals((object)new SpatialSettings(unit, chunk)), Is.True);
                Assert.That(settings.GetHashCode(), Is.EqualTo(new SpatialSettings(unit, chunk).GetHashCode()));
            }
        }

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SpatialSettings(-31, 12));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SpatialSettings(31, 12));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SpatialSettings(0, 7));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SpatialSettings(0, 17));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SpatialSettings(int.MinValue, 12));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SpatialSettings(0, int.MaxValue));
    }
}
