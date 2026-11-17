// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using TerraFX.Numerics;

namespace TerraFX.Animation;

internal static class AnimationRootMotionKernels
{
    private const byte ChannelPosition = 0;
    private const byte ChannelRotation = 1;
    private const byte ChannelScale = 2;
    private const byte LoopModeClamp = 0;
    private const byte LoopModeLoop = 1;
    private const byte TargetDomainSkeleton = 1;

    internal static Matrix4x4 CalculateDelta(
        AnimationClipAsset clip,
        SkeletonAsset skeleton,
        long firstLoopOrdinal,
        long firstTimeTicks,
        long secondLoopOrdinal,
        long secondTimeTicks)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ArgumentNullException.ThrowIfNull(skeleton);
        var rootJointId = ValidateBinding(clip, skeleton);
        var first = CreateEndpoint(clip, firstLoopOrdinal, firstTimeTicks);
        var second = CreateEndpoint(clip, secondLoopOrdinal, secondTimeTicks);
        var order = Compare(first, second);

        if (order == 0)
        {
            return Matrix4x4.Identity;
        }

        var delta = (order < 0)
            ? CalculateForwardDelta(clip, skeleton, rootJointId, first, second)
            : Invert(CalculateForwardDelta(clip, skeleton, rootJointId, second, first));
        return ToMatrix(delta);
    }

    private static RigidTransform CalculateForwardDelta(
        AnimationClipAsset clip,
        SkeletonAsset skeleton,
        Guid rootJointId,
        RootMotionEndpoint first,
        RootMotionEndpoint second)
    {
        var firstTransform = SampleRootTransform(clip, skeleton, rootJointId, first.TimeTicks);

        if (first.LoopOrdinal == second.LoopOrdinal)
        {
            var secondTransform = SampleRootTransform(clip, skeleton, rootJointId, second.TimeTicks);
            return Compose(secondTransform, Invert(firstTransform));
        }

        var startTransform = SampleRootTransform(clip, skeleton, rootJointId, 0);
        var endTransform = SampleRootTransform(clip, skeleton, rootJointId, clip.DurationTicks);
        var cycleDelta = Compose(endTransform, Invert(startTransform));
        var firstSegment = Compose(endTransform, Invert(firstTransform));
        var secondSegment = SampleRootTransform(clip, skeleton, rootJointId, second.TimeTicks);
        secondSegment = Compose(secondSegment, Invert(startTransform));
        var fullLoopCount = checked((ulong)((Int128)second.LoopOrdinal - first.LoopOrdinal - 1));
        var repeatedLoops = Pow(cycleDelta, fullLoopCount);

        return Compose(Compose(secondSegment, repeatedLoops), firstSegment);
    }

    private static int Compare(RootMotionEndpoint first, RootMotionEndpoint second)
    {
        var loopOrder = first.LoopOrdinal.CompareTo(second.LoopOrdinal);
        return (loopOrder != 0) ? loopOrder : first.TimeTicks.CompareTo(second.TimeTicks);
    }

    private static RigidTransform Compose(RigidTransform left, RigidTransform right)
    {
        var rotation = Quaternion.Compose(left.Rotation, right.Rotation);

        if (!rotation.IsFinite().All() || !Quaternion.TryNormalize(rotation, out var normalizedRotation))
        {
            throw new InvalidDataException("An Animation root-motion rotation composition is not finite and normalizable.");
        }

        var translation = Vector3.TransformDirection(
            left.Translation,
            Matrix4x4.CreateFromQuaternion(right.Rotation)) + right.Translation;

        if (!translation.IsFinite().All())
        {
            throw new InvalidDataException("An Animation root-motion translation composition is not finite.");
        }

        return new RigidTransform(translation, normalizedRotation);
    }

    private static RootMotionEndpoint CreateEndpoint(
        AnimationClipAsset clip,
        long loopOrdinal,
        long timeTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(timeTicks);

        ArgumentOutOfRangeException.ThrowIfGreaterThan(timeTicks, clip.DurationTicks);

        if (clip.LoopMode is LoopModeClamp)
        {
            ArgumentOutOfRangeException.ThrowIfNotEqual(loopOrdinal, 0);

            return new RootMotionEndpoint(loopOrdinal, timeTicks);
        }

        if (clip.LoopMode is not LoopModeLoop)
        {
            throw new InvalidDataException("The selected Animation Clip has an unsupported loop mode.");
        }

        if (timeTicks == clip.DurationTicks)
        {
            return new RootMotionEndpoint(checked(loopOrdinal + 1), 0);
        }

        return new RootMotionEndpoint(loopOrdinal, timeTicks);
    }

    private static RigidTransform Invert(RigidTransform value)
    {
        var inverseRotation = Quaternion.Invert(value.Rotation);

        if (!inverseRotation.IsFinite().All() || !Quaternion.TryNormalize(inverseRotation, out var normalizedRotation))
        {
            throw new InvalidDataException("An Animation root-motion rotation cannot be inverted.");
        }

        var inverseTranslation = Vector3.TransformDirection(
            -value.Translation,
            Matrix4x4.CreateFromQuaternion(normalizedRotation));

        if (!inverseTranslation.IsFinite().All())
        {
            throw new InvalidDataException("An Animation root-motion translation cannot be inverted.");
        }

        return new RigidTransform(inverseTranslation, normalizedRotation);
    }

    private static RigidTransform Pow(RigidTransform value, ulong exponent)
    {
        var result = new RigidTransform(Vector3.Zero, Quaternion.Identity);

        while (exponent != 0)
        {
            if ((exponent & 1) != 0)
            {
                result = Compose(value, result);
            }

            exponent >>= 1;

            if (exponent != 0)
            {
                value = Compose(value, value);
            }
        }

        return result;
    }

    private static RigidTransform SampleRootTransform(
        AnimationClipAsset clip,
        SkeletonAsset skeleton,
        Guid rootJointId,
        long timeTicks)
    {
        var joint = default(SkeletonJoint);
        var jointFound = false;

        foreach (var candidate in skeleton.Joints)
        {
            if (candidate.JointId != rootJointId)
            {
                continue;
            }

            if (jointFound)
            {
                throw new InvalidDataException("The selected Skeleton contains duplicate root-motion joints.");
            }

            joint = candidate;
            jointFound = true;
        }

        if (!jointFound || (joint.ParentIndex != -1) || (joint.BindScale != Vector3.One))
        {
            throw new InvalidDataException("Animation root motion requires the selected Skeleton's parentless unit-scale root.");
        }

        AnimationClipAsset.AnimationTrack? positionTrack = null;
        AnimationClipAsset.AnimationTrack? rotationTrack = null;
        AnimationClipAsset.AnimationTrack? scaleTrack = null;

        foreach (var track in clip.Tracks)
        {
            if ((track.TargetDomain != TargetDomainSkeleton) || (track.TargetId != rootJointId))
            {
                continue;
            }

            switch (track.Channel)
            {
                case ChannelPosition:
                {
                    SetTrack(ref positionTrack, track);
                    break;
                }

                case ChannelRotation:
                {
                    SetTrack(ref rotationTrack, track);
                    break;
                }

                case ChannelScale:
                {
                    SetTrack(ref scaleTrack, track);
                    break;
                }

                default:
                {
                    throw new InvalidDataException("A Skeleton root track has an unsupported channel.");
                }
            }
        }

        var translation = positionTrack is { } selectedPositionTrack
            ? AnimationClipSamplingKernels.SampleVector3(clip, selectedPositionTrack, timeTicks)
            : joint.BindTranslation;
        var rotation = rotationTrack is { } selectedRotationTrack
            ? AnimationClipSamplingKernels.SampleQuaternion(clip, selectedRotationTrack, timeTicks)
            : joint.BindRotation;

        if ((scaleTrack is { } selectedScaleTrack)
            && (AnimationClipSamplingKernels.SampleVector3(clip, selectedScaleTrack, timeTicks) != Vector3.One))
        {
            throw new InvalidDataException("An extracted Animation root must retain unit scale at every sampled time.");
        }

        if (!rotation.IsFinite().All() || !Quaternion.TryNormalize(rotation, out var normalizedRotation))
        {
            throw new InvalidDataException("An Animation root rotation is not finite and normalizable.");
        }

        if (!translation.IsFinite().All())
        {
            throw new InvalidDataException("An Animation root translation is not finite.");
        }

        return new RigidTransform(translation, normalizedRotation);
    }

    private static void SetTrack(
        ref AnimationClipAsset.AnimationTrack? destination,
        AnimationClipAsset.AnimationTrack track)
    {
        if (destination.HasValue)
        {
            throw new InvalidDataException("An Animation root-motion track contains duplicate transform channels.");
        }

        destination = track;
    }

    private static Matrix4x4 ToMatrix(RigidTransform value)
    {
        var result = Matrix4x4.CreateFromQuaternion(value.Rotation)
            * Matrix4x4.CreateTranslation(value.Translation);

        return result.IsFinite().All()
            ? result
            : throw new InvalidDataException("An Animation root-motion transform is not finite.");
    }

    private static Guid ValidateBinding(AnimationClipAsset clip, SkeletonAsset skeleton)
    {
        var rootMotion = clip.RootMotion
            ?? throw new InvalidDataException("The selected Animation Clip has no root-motion track.");

        if ((rootMotion.Policy != 0)
            || (clip.SkeletonAssetId != skeleton.AssetId)
            || (skeleton.RootMotionJointId != rootMotion.RootJointId))
        {
            throw new InvalidDataException("The Animation Clip root-motion binding does not match the selected Skeleton.");
        }

        return rootMotion.RootJointId;
    }

    private readonly record struct RigidTransform(Vector3 Translation, Quaternion Rotation);

    private readonly record struct RootMotionEndpoint(long LoopOrdinal, long TimeTicks);
}
