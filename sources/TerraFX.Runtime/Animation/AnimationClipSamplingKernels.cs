// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using TerraFX.Numerics;

namespace TerraFX.Animation;

internal static class AnimationClipSamplingKernels
{
    private const byte InterpolationCubic = 2;
    private const byte InterpolationLinear = 1;
    private const byte InterpolationStep = 0;
    private const float MinimumSampleQuaternionLengthSquared = 1.0e-12f;

    internal static Quaternion SampleQuaternion(
        AnimationClipAsset clip,
        AnimationClipAsset.AnimationTrack track,
        long timeTicks)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ValidateClipTime(clip, timeTicks);
        var keys = track.QuaternionKeys;

        if (keys.IsDefaultOrEmpty)
        {
            throw new InvalidDataException("A sampled Rotation track has no initialized quaternion keys.");
        }

        var nextIndex = FindNextQuaternionKeyIndex(keys, timeTicks);

        if (track.Interpolation is InterpolationStep)
        {
            var selectedIndex = (nextIndex == 0) ? 0 : Math.Min(nextIndex - 1, keys.Length - 1);
            return NormalizeQuaternionSample(keys[selectedIndex].Value);
        }

        if (nextIndex == 0)
        {
            return NormalizeQuaternionSample(keys[0].Value);
        }

        if (nextIndex == keys.Length)
        {
            return NormalizeQuaternionSample(keys[^1].Value);
        }

        var first = keys[nextIndex - 1];
        var second = keys[nextIndex];

        if (second.TimeTicks == timeTicks)
        {
            return NormalizeQuaternionSample(second.Value);
        }

        var amount = CalculateAmount(first.TimeTicks, second.TimeTicks, timeTicks);

        if (track.Interpolation is InterpolationLinear)
        {
            return NormalizeQuaternionSample(Quaternion.Slerp(
                Quaternion.Normalize(first.Value),
                Quaternion.Normalize(second.Value),
                amount));
        }

        if (track.Interpolation is not InterpolationCubic
            || !first.OutTangent.HasValue
            || !second.InTangent.HasValue)
        {
            throw new InvalidDataException("A sampled Rotation track has invalid interpolation tangents.");
        }

        var amountSquared = amount * amount;
        var amountCubed = amountSquared * amount;
        var firstWeight = (2.0f * amountCubed) - (3.0f * amountSquared) + 1.0f;
        var firstTangentWeight = amountCubed - (2.0f * amountSquared) + amount;
        var secondWeight = (-2.0f * amountCubed) + (3.0f * amountSquared);
        var secondTangentWeight = amountCubed - amountSquared;
        var tangentSeconds = CalculateDurationSeconds(first.TimeTicks, second.TimeTicks);
        var firstOutTangent = first.OutTangent.Value;
        var secondInTangent = second.InTangent.Value;
        var interpolated = (first.Value * firstWeight)
            + (new Quaternion(
                new Vector3(firstOutTangent.X, firstOutTangent.Y, firstOutTangent.Z),
                firstOutTangent.W) * (firstTangentWeight * tangentSeconds))
            + (second.Value * secondWeight)
            + (new Quaternion(
                new Vector3(secondInTangent.X, secondInTangent.Y, secondInTangent.Z),
                secondInTangent.W) * (secondTangentWeight * tangentSeconds));
        return NormalizeQuaternionSample(interpolated);
    }

    internal static AnimationClipAsset.SpriteKey SampleSprite(
        AnimationClipAsset clip,
        AnimationClipAsset.AnimationTrack track,
        long timeTicks)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ValidateClipTime(clip, timeTicks);
        var keys = track.SpriteKeys;

        if (keys.IsDefaultOrEmpty || (track.Interpolation is not InterpolationStep))
        {
            throw new InvalidDataException("A sampled Sprite track must have initialized Step keys.");
        }

        var nextIndex = FindNextSpriteKeyIndex(keys, timeTicks);
        var selectedIndex = (nextIndex == 0) ? 0 : Math.Min(nextIndex - 1, keys.Length - 1);
        return keys[selectedIndex];
    }

    internal static Vector3 SampleVector3(
        AnimationClipAsset clip,
        AnimationClipAsset.AnimationTrack track,
        long timeTicks)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ValidateClipTime(clip, timeTicks);
        var keys = track.Vector3Keys;

        if (keys.IsDefaultOrEmpty)
        {
            throw new InvalidDataException("A sampled Position/Scale track has no initialized vector keys.");
        }

        var nextIndex = FindNextVector3KeyIndex(keys, timeTicks);

        if (track.Interpolation is InterpolationStep)
        {
            var selectedIndex = (nextIndex == 0) ? 0 : Math.Min(nextIndex - 1, keys.Length - 1);
            return ValidateVector3Sample(keys[selectedIndex].Value);
        }

        if (nextIndex == 0)
        {
            return ValidateVector3Sample(keys[0].Value);
        }

        if (nextIndex == keys.Length)
        {
            return ValidateVector3Sample(keys[^1].Value);
        }

        var first = keys[nextIndex - 1];
        var second = keys[nextIndex];

        if (second.TimeTicks == timeTicks)
        {
            return ValidateVector3Sample(second.Value);
        }

        var amount = CalculateAmount(first.TimeTicks, second.TimeTicks, timeTicks);

        if (track.Interpolation is InterpolationLinear)
        {
            return ValidateVector3Sample(Vector3.Lerp(first.Value, second.Value, amount));
        }

        if (track.Interpolation is not InterpolationCubic
            || !first.OutTangent.HasValue
            || !second.InTangent.HasValue)
        {
            throw new InvalidDataException("A sampled Position/Scale track has invalid interpolation tangents.");
        }

        var amountSquared = amount * amount;
        var amountCubed = amountSquared * amount;
        var firstWeight = (2.0f * amountCubed) - (3.0f * amountSquared) + 1.0f;
        var firstTangentWeight = amountCubed - (2.0f * amountSquared) + amount;
        var secondWeight = (-2.0f * amountCubed) + (3.0f * amountSquared);
        var secondTangentWeight = amountCubed - amountSquared;
        var tangentSeconds = CalculateDurationSeconds(first.TimeTicks, second.TimeTicks);
        return ValidateVector3Sample(
            (first.Value * firstWeight)
            + (first.OutTangent.Value * (firstTangentWeight * tangentSeconds))
            + (second.Value * secondWeight)
            + (second.InTangent.Value * (secondTangentWeight * tangentSeconds)));
    }

    private static float CalculateAmount(long firstTimeTicks, long secondTimeTicks, long timeTicks)
    {
        var durationTicks = secondTimeTicks - firstTimeTicks;

        if ((durationTicks <= 0) || (timeTicks <= firstTimeTicks) || (timeTicks >= secondTimeTicks))
        {
            throw new InvalidDataException("Animation key sampling selected an invalid interpolation interval.");
        }

        return checked((float)((timeTicks - firstTimeTicks) / (double)durationTicks));
    }

    private static float CalculateDurationSeconds(long firstTimeTicks, long secondTimeTicks)
    {
        var durationTicks = secondTimeTicks - firstTimeTicks;
        var durationSeconds = (float)(durationTicks / (double)TimeSpan.TicksPerSecond);

        if ((durationTicks <= 0) || !float.IsFinite(durationSeconds) || (durationSeconds <= 0.0f))
        {
            throw new InvalidDataException("Animation key tangent time scaling is not representable.");
        }

        return durationSeconds;
    }

    private static int FindNextQuaternionKeyIndex(
        System.Collections.Immutable.ImmutableArray<AnimationClipAsset.QuaternionKey> keys,
        long timeTicks)
    {
        var low = 0;
        var high = keys.Length;

        while (low < high)
        {
            var middle = low + ((high - low) / 2);

            if (keys[middle].TimeTicks < timeTicks)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static int FindNextSpriteKeyIndex(
        System.Collections.Immutable.ImmutableArray<AnimationClipAsset.SpriteKey> keys,
        long timeTicks)
    {
        var low = 0;
        var high = keys.Length;

        while (low < high)
        {
            var middle = low + ((high - low) / 2);

            if (keys[middle].TimeTicks < timeTicks)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static int FindNextVector3KeyIndex(
        System.Collections.Immutable.ImmutableArray<AnimationClipAsset.Vector3Key> keys,
        long timeTicks)
    {
        var low = 0;
        var high = keys.Length;

        while (low < high)
        {
            var middle = low + ((high - low) / 2);

            if (keys[middle].TimeTicks < timeTicks)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static Quaternion NormalizeQuaternionSample(Quaternion value)
    {
        var lengthSquared = Quaternion.Dot(value, value);

        if (!value.IsFinite().All()
            || !float.IsFinite(lengthSquared)
            || (lengthSquared <= MinimumSampleQuaternionLengthSquared)
            || !Quaternion.TryNormalize(value, out var normalized))
        {
            throw new InvalidDataException("An Animation quaternion sample has a nonfinite or near-zero norm.");
        }

        return normalized;
    }

    private static void ValidateClipTime(AnimationClipAsset clip, long timeTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(timeTicks);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(timeTicks, clip.DurationTicks);
    }

    private static Vector3 ValidateVector3Sample(Vector3 value)
    {
        if (!value.IsFinite().All())
        {
            throw new InvalidDataException("An Animation vector sample is not finite.");
        }

        return value;
    }
}
