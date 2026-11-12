// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Assets;
using TerraFX.Numerics;

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
            ValidateSkeletonBinding(clip, skeletonLease.Value);
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

    private static void ValidateSkeletonBinding(AnimationClipAsset clip, SkeletonAsset skeleton)
    {
        var jointIds = new HashSet<Guid>(skeleton.Joints.Length);

        foreach (var joint in skeleton.Joints)
        {
            _ = jointIds.Add(joint.JointId);
        }

        foreach (var track in clip.Tracks)
        {
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
