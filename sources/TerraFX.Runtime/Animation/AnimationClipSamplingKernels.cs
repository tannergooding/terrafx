// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using TerraFX.Numerics;

namespace TerraFX.Animation;

internal static class AnimationClipSamplingKernels
{
    private const byte ChannelPosition = 0;
    private const byte ChannelScale = 2;
    private const byte InterpolationCubic = 2;
    private const byte InterpolationLinear = 1;
    private const byte InterpolationStep = 0;
    private const float MinimumSampleQuaternionLengthSquared = 1.0e-12f;

    internal static Vector3 ApplyAdditivePosition(
        Vector3 current,
        Vector3 sample,
        Vector3 reference,
        float weight)
    {
        current = ValidateVector3Sample(current, ChannelPosition);
        sample = ValidateVector3Sample(sample, ChannelPosition);
        reference = ValidateVector3Sample(reference, ChannelPosition);
        weight = ValidateBlendWeight(weight);
        return ValidateVector3Sample(current + ((sample - reference) * weight), ChannelPosition);
    }

    internal static Quaternion ApplyAdditiveQuaternion(
        Quaternion current,
        Quaternion sample,
        Quaternion reference,
        float weight)
    {
        current = NormalizeQuaternionSample(current);
        sample = NormalizeQuaternionSample(sample);
        reference = NormalizeQuaternionSample(reference);
        weight = ValidateBlendWeight(weight);
        var delta = NormalizeQuaternionSample(Quaternion.Compose(Quaternion.Invert(reference), sample));
        var weightedDelta = NormalizeQuaternionSample(Quaternion.Slerp(Quaternion.Identity, delta, weight));
        return NormalizeQuaternionSample(Quaternion.Compose(current, weightedDelta));
    }

    internal static Vector3 ApplyAdditiveScale(
        Vector3 current,
        Vector3 sample,
        Vector3 reference,
        float weight)
    {
        if (!AnimationScaleRules.IsScaleValid(current)
            || !AnimationScaleRules.IsScaleValid(sample)
            || !AnimationScaleRules.IsScaleValid(reference)
            || !HasPositiveScaleComponents(sample)
            || !HasPositiveScaleComponents(reference))
        {
            throw new InvalidDataException("Animation additive scale requires valid samples and positive sample/reference components.");
        }

        weight = ValidateBlendWeight(weight);
        var ratio = new Vector3(
            sample.X / reference.X,
            sample.Y / reference.Y,
            sample.Z / reference.Z);
        var weightedRatio = Vector3.Lerp(Vector3.One, ratio, weight);
        var result = current * weightedRatio;

        if (!AnimationScaleRules.IsScaleValid(result))
        {
            throw new InvalidDataException("An Animation additive scale result is outside its admitted component range.");
        }

        return result;
    }

    internal static Quaternion BlendOverrideQuaternion(
        ReadOnlySpan<Quaternion> samples,
        ReadOnlySpan<float> weights,
        Quaternion fallback)
    {
        ValidateBlendInputs(samples.Length, weights.Length);
        fallback = NormalizeQuaternionSample(fallback);
        var result = Quaternion.Identity;
        var totalWeight = 0.0f;

        for (var index = 0; index < samples.Length; index++)
        {
            var sample = NormalizeQuaternionSample(samples[index]);
            var weight = ValidateBlendWeight(weights[index]);

            if (weight == 0.0f)
            {
                continue;
            }

            var nextTotalWeight = totalWeight + weight;
            result = (totalWeight == 0.0f)
                ? sample
                : NormalizeQuaternionSample(Quaternion.Slerp(result, sample, weight / nextTotalWeight));
            totalWeight = nextTotalWeight;
        }

        return (totalWeight == 0.0f) ? fallback : result;
    }

    internal static Vector3 BlendOverrideScale(
        ReadOnlySpan<Vector3> samples,
        ReadOnlySpan<float> weights,
        Vector3 fallback)
    {
        ValidateBlendInputs(samples.Length, weights.Length);

        if (!AnimationScaleRules.IsScaleValid(fallback))
        {
            throw new InvalidDataException("An Animation fallback scale is outside its admitted component range.");
        }

        var weightedSample = Vector3.Zero;
        var totalWeight = 0.0f;

        for (var index = 0; index < samples.Length; index++)
        {
            var sample = samples[index];

            if (!AnimationScaleRules.IsScaleValid(sample))
            {
                throw new InvalidDataException("An Animation scale sample is outside its admitted component range.");
            }

            var weight = ValidateBlendWeight(weights[index]);
            weightedSample += sample * weight;
            totalWeight += weight;
        }

        if (totalWeight == 0.0f)
        {
            return fallback;
        }

        var result = weightedSample / totalWeight;
        return AnimationScaleRules.IsScaleValid(result)
            ? result
            : throw new InvalidDataException("An Animation override scale result is outside its admitted component range.");
    }

    internal static Vector3 BlendOverrideVector3(
        ReadOnlySpan<Vector3> samples,
        ReadOnlySpan<float> weights,
        Vector3 fallback)
    {
        ValidateBlendInputs(samples.Length, weights.Length);
        fallback = ValidateVector3Sample(fallback, ChannelPosition);
        var weightedSample = Vector3.Zero;
        var totalWeight = 0.0f;

        for (var index = 0; index < samples.Length; index++)
        {
            var sample = ValidateVector3Sample(samples[index], ChannelPosition);
            var weight = ValidateBlendWeight(weights[index]);
            weightedSample += sample * weight;
            totalWeight += weight;
        }

        return (totalWeight == 0.0f)
            ? fallback
            : ValidateVector3Sample(weightedSample / totalWeight, ChannelPosition);
    }

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

        if (track.Channel is not ChannelPosition and not ChannelScale)
        {
            throw new InvalidDataException("A sampled Vector3 track must be a Position or Scale track.");
        }

        var keys = track.Vector3Keys;

        if (keys.IsDefaultOrEmpty)
        {
            throw new InvalidDataException("A sampled Position/Scale track has no initialized vector keys.");
        }

        var nextIndex = FindNextVector3KeyIndex(keys, timeTicks);

        if (track.Interpolation is InterpolationStep)
        {
            var selectedIndex = (nextIndex == 0) ? 0 : Math.Min(nextIndex - 1, keys.Length - 1);
            return ValidateVector3Sample(keys[selectedIndex].Value, track.Channel);
        }

        if (nextIndex == 0)
        {
            return ValidateVector3Sample(keys[0].Value, track.Channel);
        }

        if (nextIndex == keys.Length)
        {
            return ValidateVector3Sample(keys[^1].Value, track.Channel);
        }

        var first = keys[nextIndex - 1];
        var second = keys[nextIndex];

        if (second.TimeTicks == timeTicks)
        {
            return ValidateVector3Sample(second.Value, track.Channel);
        }

        var amount = CalculateAmount(first.TimeTicks, second.TimeTicks, timeTicks);

        if (track.Interpolation is InterpolationLinear)
        {
            return ValidateVector3Sample(Vector3.Lerp(first.Value, second.Value, amount), track.Channel);
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
            + (second.InTangent.Value * (secondTangentWeight * tangentSeconds)),
            track.Channel);
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

    private static bool HasPositiveScaleComponents(Vector3 value)
        => (value.X > 0.0f) && (value.Y > 0.0f) && (value.Z > 0.0f);

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

    private static void ValidateBlendInputs(int sampleCount, int weightCount)
    {
        if (sampleCount != weightCount)
        {
            throw new ArgumentException("Animation blend samples and weights must have matching lengths.");
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(sampleCount, 4);
    }

    private static float ValidateBlendWeight(float weight)
    {
        if (!float.IsFinite(weight) || (weight < 0.0f) || (weight > 1.0f))
        {
            throw new InvalidDataException("An Animation blend weight must be finite and in [0,1].");
        }

        return weight;
    }

    private static void ValidateClipTime(AnimationClipAsset clip, long timeTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(timeTicks);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(timeTicks, clip.DurationTicks);
    }

    private static Vector3 ValidateVector3Sample(Vector3 value, byte channel)
    {
        if (!value.IsFinite().All())
        {
            throw new InvalidDataException("An Animation vector sample is not finite.");
        }

        if ((channel == ChannelScale) && !AnimationScaleRules.IsScaleValid(value))
        {
            throw new InvalidDataException("An Animation Scale sample has component magnitudes outside [0.0001,1000].");
        }

        return value;
    }
}
