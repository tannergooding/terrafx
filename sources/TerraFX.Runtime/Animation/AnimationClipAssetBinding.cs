// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Assets;
using TerraFX.Numerics;
using TerraFX.Rendering;
using TerraFX.Scenes;

namespace TerraFX.Animation;

internal static class AnimationClipAssetBinding
{
    private const byte ChannelScale = 2;
    private const byte InterpolationCubic = 2;
    private const byte TargetDomainSkeleton = 1;

    internal static async Task<AssetLease<SkeletonAsset>?> LoadSkeletonAsync(
        AssetLease<AnimationClipAsset> clipLease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clipLease);
        cancellationToken.ThrowIfCancellationRequested();
        using var retainedClipLease = clipLease.Retain();
        var clip = retainedClipLease.Value;

        if (clip.SkeletonAssetId is not { } skeletonAssetId)
        {
            return null;
        }

        var skeletonLease = await retainedClipLease.LoadFromPackageAsync<SkeletonAsset>(
            skeletonAssetId,
            cancellationToken).ConfigureAwait(false);
        var isValid = false;

        try
        {
            ValidateSkeletonBinding(clip, skeletonLease.Value, cancellationToken);
            isValid = true;
            return skeletonLease;
        }
        finally
        {
            if (!isValid)
            {
                skeletonLease.Dispose();
            }
        }
    }

    internal static async Task ValidatePackageBindingsAsync(
        AssetLease<AnimationClipAsset> clipLease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clipLease);
        cancellationToken.ThrowIfCancellationRequested();
        using var retainedClipLease = clipLease.Retain();
        var clip = retainedClipLease.Value;
        var skeletonLease = await LoadSkeletonAsync(retainedClipLease, cancellationToken).ConfigureAwait(false);

        try
        {
            if (clip.SceneAssetId is { } sceneAssetId)
            {
                SceneExpansionPlan? scenePlan = null;

                try
                {
                    scenePlan = await ScenePrefabExpansionPlanner.PrepareTakingLeaseAsync(
                        await retainedClipLease.LoadFromPackageAsync<SceneAsset>(
                            sceneAssetId,
                            cancellationToken).ConfigureAwait(false),
                        SceneExpansionLimits.Default,
                        useControl: null,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    ValidateSceneTargets(clip, scenePlan, cancellationToken);
                }
                finally
                {
                    scenePlan?.Dispose();
                }
            }

            await ValidateAtlasFramesAsync(clip, retainedClipLease, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            skeletonLease?.Dispose();
        }
    }

    internal static async Task ValidatePackageBindingsAsync(
        AssetLease<AnimationClipAsset> clipLease,
        SceneExpansionPlan scenePlan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clipLease);
        ArgumentNullException.ThrowIfNull(scenePlan);
        cancellationToken.ThrowIfCancellationRequested();
        using var retainedClipLease = clipLease.Retain();
        var clip = retainedClipLease.Value;

        if (!ReferenceEquals(retainedClipLease.BackingToken, scenePlan.BackingToken))
        {
            throw new InvalidDataException("An Animation Clip and Scene expansion must retain the exact package backing.");
        }

        var skeletonLease = await LoadSkeletonAsync(retainedClipLease, cancellationToken).ConfigureAwait(false);

        try
        {
            if (clip.SceneAssetId is { } sceneAssetId)
            {
                if (scenePlan.SceneAssetId != sceneAssetId)
                {
                    throw new InvalidDataException("An Animation Clip must validate against its exact selected Scene expansion.");
                }

                ValidateSceneTargets(clip, scenePlan, cancellationToken);
            }

            await ValidateAtlasFramesAsync(clip, retainedClipLease, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            skeletonLease?.Dispose();
        }
    }

    private static async Task ValidateAtlasFramesAsync(
        AnimationClipAsset clip,
        AssetLease<AnimationClipAsset> clipLease,
        CancellationToken cancellationToken)
    {
        var requiredFrameIdsByAtlas = new Dictionary<AssetId, HashSet<Guid>>();

        foreach (var track in clip.Tracks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var spriteKey in track.SpriteKeys)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!requiredFrameIdsByAtlas.TryGetValue(spriteKey.AtlasAssetId, out var frameIds))
                {
                    frameIds = [];
                    requiredFrameIdsByAtlas.Add(spriteKey.AtlasAssetId, frameIds);
                }

                _ = frameIds.Add(spriteKey.FrameId);
            }
        }

        foreach (var atlasRequirement in requiredFrameIdsByAtlas)
        {
            using var atlasLease = await clipLease.LoadFromPackageAsync<RenderAtlasAsset>(
                atlasRequirement.Key,
                cancellationToken).ConfigureAwait(false);
            var availableFrames = new HashSet<Guid>();

            foreach (var frame in atlasLease.Value.Frames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!availableFrames.Add(frame.FrameId))
                {
                    throw new InvalidDataException("A selected Atlas contains duplicate FrameIds.");
                }
            }

            foreach (var frameId in atlasRequirement.Value)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!availableFrames.Contains(frameId))
                {
                    throw new InvalidDataException("An Animation Clip Sprite key names a FrameId absent from its selected Atlas.");
                }
            }
        }
    }

    private static void ValidateSceneTargets(
        AnimationClipAsset clip,
        SceneExpansionPlan scenePlan,
        CancellationToken cancellationToken)
    {
        var expandedEntityKeys = new HashSet<Guid>();

        foreach (var entity in scenePlan.Entities)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!expandedEntityKeys.Add(entity.ExpandedEntityKey))
            {
                throw new InvalidDataException("A selected Scene expansion contains duplicate ExpandedEntityKeys.");
            }
        }

        foreach (var track in clip.Tracks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if ((track.TargetDomain == 0) && !expandedEntityKeys.Contains(track.TargetId))
            {
                throw new InvalidDataException("An Animation Clip Scene track names an ExpandedEntityKey absent from its selected Scene expansion.");
            }
        }
    }

    private static void ValidateSkeletonBinding(
        AnimationClipAsset clip,
        SkeletonAsset skeleton,
        CancellationToken cancellationToken)
    {
        var jointIds = new HashSet<Guid>(skeleton.Joints.Length);

        foreach (var joint in skeleton.Joints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = jointIds.Add(joint.JointId);
        }

        foreach (var track in clip.Tracks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if ((track.TargetDomain == TargetDomainSkeleton) && !jointIds.Contains(track.TargetId))
            {
                throw new InvalidDataException("An Animation Clip joint track names a JointId absent from its selected Skeleton.");
            }
        }

        if (clip.RootMotion is not { } rootMotion)
        {
            return;
        }

        if (skeleton.RootMotionJointId != rootMotion.RootJointId)
        {
            throw new InvalidDataException("Animation Clip RootMotion must match the selected Skeleton RootMotionJoint.");
        }

        foreach (var track in clip.Tracks)
        {
            if ((track.TargetDomain != TargetDomainSkeleton)
                || (track.TargetId != rootMotion.RootJointId)
                || (track.Channel != ChannelScale))
            {
                continue;
            }

            foreach (var key in track.Vector3Keys)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if ((key.Value != Vector3.One)
                    || ((track.Interpolation == InterpolationCubic)
                        && ((key.InTangent is { } inTangent && (inTangent != Vector3.Zero))
                            || (key.OutTangent is { } outTangent && (outTangent != Vector3.Zero)))))
                {
                    throw new InvalidDataException("An extracted root-motion joint must keep unit scale with zero cubic scale tangents.");
                }
            }
        }
    }
}
