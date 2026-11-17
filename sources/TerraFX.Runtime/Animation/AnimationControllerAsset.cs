// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Assets;
using TerraFX.Numerics;

namespace TerraFX.Animation;

/// <summary>Represents one immutable, package-backed native Animation Controller asset.</summary>
public sealed class AnimationControllerAsset
{
    internal AnimationControllerAsset(
        AssetId assetId,
        AssetId? skeletonAssetId,
        AssetId? sceneAssetId,
        ImmutableArray<AnimationClipSlot> clipSlots,
        ImmutableArray<AnimationLayer> layers,
        ImmutableArray<AnimationTransition> transitions,
        byte rootMotionMode,
        Guid? motionActorKey,
        ImmutableArray<byte> encodedRecord)
    {
        if (!assetId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(assetId));
        }

        if (clipSlots.IsDefaultOrEmpty || (clipSlots.Length > 32))
        {
            throw new ArgumentOutOfRangeException(nameof(clipSlots));
        }

        if (layers.IsDefaultOrEmpty || (layers.Length > 8))
        {
            throw new ArgumentOutOfRangeException(nameof(layers));
        }

        if (transitions.IsDefault || (transitions.Length > 64))
        {
            throw new ArgumentOutOfRangeException(nameof(transitions));
        }

        if ((rootMotionMode > 1)
            || ((rootMotionMode == 0) && motionActorKey.HasValue)
            || ((rootMotionMode == 1) && (!motionActorKey.HasValue || (motionActorKey == Guid.Empty))))
        {
            throw new ArgumentOutOfRangeException(nameof(rootMotionMode));
        }

        if (encodedRecord.IsDefaultOrEmpty)
        {
            throw new ArgumentException("The Animation Controller record must be initialized and nonempty.", nameof(encodedRecord));
        }

        AssetId = assetId;
        SkeletonAssetId = skeletonAssetId;
        SceneAssetId = sceneAssetId;
        ClipSlots = clipSlots;
        Layers = layers;
        Transitions = transitions;
        RootMotionMode = rootMotionMode;
        MotionActorKey = motionActorKey;
        EncodedRecord = encodedRecord;
    }

    internal AssetId AssetId { get; }

    internal ImmutableArray<AnimationClipSlot> ClipSlots { get; }

    internal ImmutableArray<byte> EncodedRecord { get; }

    internal ImmutableArray<AnimationLayer> Layers { get; }

    internal Guid? MotionActorKey { get; }

    internal byte RootMotionMode { get; }

    internal AssetId? SceneAssetId { get; }

    internal AssetId? SkeletonAssetId { get; }

    internal ImmutableArray<AnimationTransition> Transitions { get; }

    internal readonly record struct AnimationClipSlot(Guid ClipSlotId, AssetId ClipAssetId);

    internal readonly record struct AnimationClipWeight(Guid ClipSlotId, float Weight);

    internal readonly record struct AnimationLayer(
        byte Index,
        byte Mode,
        float Weight,
        ImmutableArray<float> Mask,
        ImmutableArray<AnimationReferencePose> ReferencePose,
        ImmutableArray<AnimationClipWeight> InitialClips,
        byte EventPolicy);

    internal readonly record struct AnimationReferencePose(
        byte TargetDomain,
        Guid TargetId,
        Vector3 Translation,
        Quaternion Rotation,
        Vector3 Scale);

    internal readonly record struct AnimationTransition(
        Guid TransitionId,
        byte LayerIndex,
        Guid? SourceClipSlotId,
        Guid TargetClipSlotId,
        long DurationTicks,
        int Priority,
        AnimationTransitionCondition Condition,
        byte InterruptPolicy,
        AnimationTransitionAction Action,
        byte EventPolicy);

    internal readonly record struct AnimationTransitionAction(byte Kind, byte? LayerIndex, float? Weight);

    internal readonly record struct AnimationTransitionCondition(byte Kind);
}
