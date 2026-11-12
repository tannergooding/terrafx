// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Assets;
using TerraFX.Numerics;

namespace TerraFX.Animation;

/// <summary>Represents one immutable, package-backed native Animation Clip asset.</summary>
public sealed class AnimationClipAsset
{
    internal AnimationClipAsset(
        AssetId assetId,
        long durationTicks,
        byte loopMode,
        AssetId? skeletonAssetId,
        AssetId? sceneAssetId,
        RootMotionInfo? rootMotion,
        ClipBounds? bounds,
        byte boundsQualification,
        float boundsMargin,
        ImmutableArray<AnimationTrack> tracks,
        ImmutableArray<AnimationClipEvent> events,
        ImmutableArray<byte> encodedRecord)
    {
        if (!assetId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(assetId));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(durationTicks);

        if (loopMode > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(loopMode));
        }

        if ((boundsQualification != 0)
            || !float.IsFinite(boundsMargin)
            || (boundsMargin < 0.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(boundsQualification));
        }

        if (tracks.IsDefault || events.IsDefault || encodedRecord.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Animation Clip data must be initialized.");
        }

        AssetId = assetId;
        DurationTicks = durationTicks;
        LoopMode = loopMode;
        SkeletonAssetId = skeletonAssetId;
        SceneAssetId = sceneAssetId;
        RootMotion = rootMotion;
        Bounds = bounds;
        BoundsQualification = boundsQualification;
        BoundsMargin = boundsMargin;
        Tracks = tracks;
        Events = events;
        EncodedRecord = encodedRecord;
    }

    internal AssetId AssetId { get; }

    internal ClipBounds? Bounds { get; }

    internal float BoundsMargin { get; }

    internal byte BoundsQualification { get; }

    internal long DurationTicks { get; }

    internal ImmutableArray<byte> EncodedRecord { get; }

    internal ImmutableArray<AnimationClipEvent> Events { get; }

    internal byte LoopMode { get; }

    internal RootMotionInfo? RootMotion { get; }

    internal AssetId? SceneAssetId { get; }

    internal AssetId? SkeletonAssetId { get; }

    internal ImmutableArray<AnimationTrack> Tracks { get; }

    internal readonly record struct AnimationClipEvent(
        Guid EventId,
        long TimeTicks,
        ContentSchemaReference PayloadSchema,
        ImmutableArray<byte> PayloadRecord);

    internal readonly record struct AnimationTrack(
        Guid TargetId,
        byte TargetDomain,
        byte Channel,
        byte Interpolation,
        ImmutableArray<Vector3Key> Vector3Keys,
        ImmutableArray<QuaternionKey> QuaternionKeys,
        ImmutableArray<SpriteKey> SpriteKeys);

    internal readonly record struct ClipBounds(
        Vector3 Minimum,
        Vector3 Maximum,
        Vector3 SphereCenter,
        float SphereRadius);

    internal readonly record struct QuaternionKey(
        long TimeTicks,
        Quaternion Value,
        Vector4? InTangent,
        Vector4? OutTangent);

    internal readonly record struct RootMotionInfo(Guid RootJointId, byte Policy);

    internal readonly record struct SpriteKey(long TimeTicks, AssetId AtlasAssetId, Guid FrameId);

    internal readonly record struct Vector3Key(
        long TimeTicks,
        Vector3 Value,
        Vector3? InTangent,
        Vector3? OutTangent);
}
