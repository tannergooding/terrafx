// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading.Tasks;
using TerraFX.Numerics;

namespace TerraFX.Core.SpatialConsumer;

internal static class Program
{
    private static void Check(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("The spatial public-consumer witness failed.");
        }
    }

    private static bool Equality<T>(T left, T right)
        where T : struct, IEquatable<T> => left.Equals(right) && left.Equals((object)right) && left.GetHashCode() == right.GetHashCode();

    private static void ExercisePublicSurface()
    {
        var settings = new SpatialSettings(metersPerUnitExponent: 0, chunkSizeExponent: 12);
        var profile = new SpatialProfileId(value: SpatialProfileId.Default.Value);
        Check(profile.IsValid && SpatialProfiles.TryGetSettings(id: profile, settings: out var found) && found == settings);
        SpatialProfiles.Register(id: profile, settings: settings);
        Check(settings.ChunkWidth == 4096 && settings.MetersPerUnitExponent == 0 && settings.ChunkSizeExponent == 12);
        Check(profile.ToString(format: "D", formatProvider: CultureInfo.InvariantCulture) == "c2ddc5d8-9ee6-8676-bdef-dffd36a56d89");
        Check(((IFormattable)profile).ToString("D", CultureInfo.InvariantCulture) == profile.ToString());
        Check(Equality(settings, SpatialSettings.Default) && Equality(profile, SpatialProfileId.Default));
        Check(!(settings != SpatialSettings.Default) && !(profile != SpatialProfileId.Default));

        var chunk2 = new ChunkIndex2(x: -2, y: 3);
        var chunk3 = new ChunkIndex3(x: -2, y: 3, z: 4);
        Check(chunk2.X == -2 && chunk2.Y == 3 && chunk3.X == -2 && chunk3.Y == 3 && chunk3.Z == 4);
        Check(Equality(chunk2, new ChunkIndex2(-2, 3)) && Equality(chunk3, new ChunkIndex3(-2, 3, 4)));
        Check(chunk2 == new ChunkIndex2(-2, 3) && !(chunk2 != new ChunkIndex2(-2, 3)));
        Check(chunk3 == new ChunkIndex3(-2, 3, 4) && !(chunk3 != new ChunkIndex3(-2, 3, 4)));

        var raw2 = new SpatialPosition2(chunk: chunk2, offset: new Vector2(4096, -1));
        var raw3 = new SpatialPosition3(chunk: chunk3, offset: new Vector3(4096, -1, -0.0f));
        var point2 = SpatialMath.Normalize(position: raw2, settings: settings);
        var point3 = SpatialMath.Normalize(position: raw3, settings: settings);
        Check(point2.Chunk == new ChunkIndex2(-1, 2) && point2.Offset == new Vector2(0, 4095));
        Check(point3.Chunk == new ChunkIndex3(-1, 2, 4) && point3.Offset == new Vector3(0, 4095, 0));
        Check(raw2 != point2 && raw3 != point3 && raw2 == new SpatialPosition2(chunk2, raw2.Offset) && raw3 == new SpatialPosition3(chunk3, raw3.Offset));
        Check(Equality(raw2, new SpatialPosition2(chunk2, raw2.Offset)) && Equality(raw3, new SpatialPosition3(chunk3, raw3.Offset)));
        Check(SpatialMath.ConvertPosition(position: raw2, sourceProfileId: profile, sourceSettings: settings, targetProfileId: profile, targetSettings: settings) == point2);
        Check(SpatialMath.ConvertPosition(position: raw3, sourceProfileId: profile, sourceSettings: settings, targetProfileId: profile, targetSettings: settings) == point3);

        var frame2 = new SpatialFrame2(ownerId: 1, profileId: profile, settings: settings, origin: point2, revision: 1, minimum: new Vector2(-8192), maximum: new Vector2(8192));
        var frame3 = new SpatialFrame3(ownerId: 1, profileId: profile, settings: settings, origin: point3, revision: 2, minimum: new Vector3(-8192), maximum: new Vector3(8192));
        Check(frame2.OwnerId == 1 && frame2.ProfileId == profile && frame2.Settings == settings && frame2.Origin == point2 && frame2.Revision == 1 && frame2.Minimum.X == -8192 && frame2.Maximum.Y == 8192);
        Check(frame3.OwnerId == 1 && frame3.ProfileId == profile && frame3.Settings == settings && frame3.Origin == point3 && frame3.Revision == 2 && frame3.Minimum.Z == -8192 && frame3.Maximum.Z == 8192);
        Check(SpatialMath.TryGetDisplacement(from: raw2, to: point2, settings: settings, maximumDistance: 0, displacement: out var d2) && d2 == Vector2.Zero);
        Check(SpatialMath.TryGetDisplacement(from: raw3, to: point3, settings: settings, maximumDistance: 0, displacement: out var d3) && d3 == Vector3.Zero);
        Check(SpatialMath.TryToFrameRelative(position: raw2, frame: frame2, relativePosition: out var r2) && r2 == Vector2.Zero);
        Check(SpatialMath.TryToFrameRelative(position: raw3, frame: frame3, relativePosition: out var r3) && r3 == Vector3.Zero);

        var bounds2 = new SpatialBounds2(minimum: raw2, maximum: point2, settings: settings);
        var bounds3 = new SpatialBounds3(minimum: raw3, maximum: point3, settings: settings);
        Check(bounds2.Minimum == raw2 && bounds2.Maximum == point2 && bounds2.Settings == settings);
        Check(bounds3.Minimum == raw3 && bounds3.Maximum == point3 && bounds3.Settings == settings);
        var pose2 = new RelativePose2(localPosition: new Vector2(1, 2), angle: 0.5f, frame: frame2);
        var pose3 = new RelativePose3(localPosition: new Vector3(1, 2, 3), rotation: new Quaternion(0, 0, 0, -2), frame: frame3);
        Check(pose2.LocalPosition == new Vector2(1, 2) && pose2.Angle == 0.5f && pose2.Frame.Revision == 1);
        Check(pose3.LocalPosition == new Vector3(1, 2, 3) && pose3.Rotation == -Quaternion.Identity && pose3.Frame.Revision == 2);

        var ray2 = new SpatialRay2(origin: raw2, unitDirection: Vector2.UnitX, maximumDistance: 9000);
        var ray3 = new SpatialRay3(origin: raw3, unitDirection: Vector3.UnitZ, maximumDistance: float.MaxValue);
        Check(ray2.Origin == raw2 && ray2.Direction == Vector2.UnitX && ray2.MaxDistance == 9000);
        Check(ray3.Origin == raw3 && ray3.Direction == Vector3.UnitZ && ray3.MaxDistance == float.MaxValue);
        Check(Equality(ray2, new SpatialRay2(raw2, Vector2.UnitX, 9000)) && Equality(ray3, new SpatialRay3(raw3, Vector3.UnitZ, float.MaxValue)));
        Check(ray2 == new SpatialRay2(raw2, Vector2.UnitX, 9000) && !(ray2 != new SpatialRay2(raw2, Vector2.UnitX, 9000)));
        Check(ray3 == new SpatialRay3(raw3, Vector3.UnitZ, float.MaxValue) && !(ray3 != new SpatialRay3(raw3, Vector3.UnitZ, float.MaxValue)));
    }

    private static void ExerciseRegistryCapacity()
    {
        Check(SpatialProfiles.TryGetSettings(SpatialProfileId.Default, out var defaults) && defaults == SpatialSettings.Default);
        Reject<ArgumentException>(() => SpatialProfiles.Register(default, defaults));
        Reject<ArgumentException>(() => SpatialProfiles.Register(Id(1), default));
        Check(!SpatialProfiles.TryGetSettings(Id(1), out var absent) && absent == default);
        _ = Parallel.For(1, 4096, index => SpatialProfiles.Register(Id(index), defaults));
        _ = Parallel.For(1, 4096, index => {
            Check(SpatialProfiles.TryGetSettings(Id(index), out var registered) && registered == defaults);
            SpatialProfiles.Register(Id(index), defaults);
        });
        SpatialProfiles.Register(SpatialProfileId.Default, defaults);
        Reject<ArgumentException>(() => SpatialProfiles.Register(Id(1), new SpatialSettings(0, 13)));
        Reject<ArgumentException>(() => SpatialProfiles.Register(Id(4096), default));
        Reject<ArgumentException>(() => SpatialProfiles.Register(default, defaults));
        Reject<InvalidOperationException>(() => SpatialProfiles.Register(Id(4096), defaults));
        Reject<InvalidOperationException>(() => SpatialProfiles.Register(Id(4097), defaults));
        Check(!SpatialProfiles.TryGetSettings(Id(4096), out absent) && absent == default);
        Check(!SpatialProfiles.TryGetSettings(Id(4097), out absent) && absent == default);
        Check(SpatialProfiles.TryGetSettings(Id(1), out var first) && first == defaults);
        Check(SpatialProfiles.TryGetSettings(Id(4095), out var last) && last == defaults);
    }

    private static SpatialProfileId Id(int index) => new SpatialProfileId(new Guid(index, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1));
    [SuppressMessage("Globalization", "CA1303", Justification = "These are machine-checked witness completion markers, not localized user messages.")]
    private static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            ExercisePublicSurface();
            Console.WriteLine("Spatial public consumer passed: 16 types and 8 dimensional operations; unsafe disabled.");
        }
        else if (args.Length == 1 && args[0] == "--capacity")
        {
            ExerciseRegistryCapacity();
            Console.WriteLine("Spatial registry capacity passed: 4096 identities; overflow and invalid registrations left no mutation.");
        }
        else
        {
            throw new ArgumentException("The only admitted argument is --capacity.", nameof(args));
        }
    }

    private static void Reject<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"The public consumer expected {typeof(TException).Name}.");
    }
}
