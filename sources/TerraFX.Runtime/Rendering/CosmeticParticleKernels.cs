// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal static class CosmeticParticleKernels
{
    internal static long GetLifeSteps(long lifetimeTicks, int tickRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lifetimeTicks);
        ArgumentOutOfRangeException.ThrowIfLessThan(tickRate, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tickRate, 1000);

        var product = (UInt128)(ulong)lifetimeTicks * (uint)tickRate;
        var steps = product / 10_000_000u;

        if ((product % 10_000_000u) != 0)
        {
            steps++;
        }

        if (steps == 0)
        {
            steps = 1;
        }

        if (steps > (UInt128)long.MaxValue)
        {
            throw new OverflowException("The particle lifetime does not fit its signed tick domain.");
        }

        return (long)steps;
    }

    internal static float GetNormalizedAge(long ageSteps, long lifeSteps)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ageSteps);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lifeSteps);

        ArgumentOutOfRangeException.ThrowIfGreaterThan(ageSteps, lifeSteps);

        Span<ulong> numerator = stackalloc ulong[1];
        Span<ulong> denominator = stackalloc ulong[1];
        Span<ulong> workspace = stackalloc ulong[BoundedRational.WorkspaceRegionCount];
        numerator[0] = checked((ulong)ageSteps);
        denominator[0] = checked((ulong)lifeSteps);
        var rational = new BoundedRational(workspace);
        rational.SetFraction(numerator, denominator, negative: false);
        return rational.RoundToSingle();
    }

    internal static Vector4 SampleColor(ReadOnlySpan<CosmeticParticleEmitterAsset.ColorKey> keys, float normalizedAge)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(keys.Length, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(keys.Length, 16);
        ValidateNormalizedAge(normalizedAge);

        for (var index = 0; index < keys.Length; index++)
        {
            var upper = keys[index];

            if (normalizedAge == upper.Age)
            {
                return upper.Color;
            }

            if (normalizedAge < upper.Age)
            {
                if (index == 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(normalizedAge));
                }

                var lower = keys[index - 1];
                var amount = (normalizedAge - lower.Age) / (upper.Age - lower.Age);
                return new Vector4(
                    Interpolate(lower.Color.X, upper.Color.X, amount),
                    Interpolate(lower.Color.Y, upper.Color.Y, amount),
                    Interpolate(lower.Color.Z, upper.Color.Z, amount),
                    Interpolate(lower.Color.W, upper.Color.W, amount));
            }
        }

        return keys[^1].Color;
    }

    internal static Vector2 SampleSize(ReadOnlySpan<CosmeticParticleEmitterAsset.SizeKey> keys, float normalizedAge)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(keys.Length, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(keys.Length, 16);
        ValidateNormalizedAge(normalizedAge);

        for (var index = 0; index < keys.Length; index++)
        {
            var upper = keys[index];

            if (normalizedAge == upper.Age)
            {
                return upper.Size;
            }

            if (normalizedAge < upper.Age)
            {
                if (index == 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(normalizedAge));
                }

                var lower = keys[index - 1];
                var amount = (normalizedAge - lower.Age) / (upper.Age - lower.Age);
                return new Vector2(
                    Interpolate(lower.Size.X, upper.Size.X, amount),
                    Interpolate(lower.Size.Y, upper.Size.Y, amount));
            }
        }

        return keys[^1].Size;
    }

    internal static Vector3 SampleVelocity(ref Pcg32 random, Vector3 minimum, Vector3 maximum)
    {
        ValidateVelocityRange(minimum, maximum);
        var unitX = GetUnitInterval(random.NextUInt());
        var unitY = GetUnitInterval(random.NextUInt());
        var unitZ = GetUnitInterval(random.NextUInt());

        return new Vector3(
            SampleLane(minimum.X, maximum.X, unitX),
            SampleLane(minimum.Y, maximum.Y, unitY),
            SampleLane(minimum.Z, maximum.Z, unitZ));
    }

    internal static bool TryAdvanceParticle(
        SpatialPosition3 position,
        Vector3 velocity,
        long ageSteps,
        long lifeSteps,
        int tickRate,
        SpatialSettings settings,
        out SpatialPosition3 nextPosition,
        out long nextAgeSteps)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ageSteps);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lifeSteps);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(ageSteps, lifeSteps);
        ArgumentOutOfRangeException.ThrowIfLessThan(tickRate, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tickRate, 1000);

        if (!velocity.IsFinite().All())
        {
            throw new ArgumentException("The particle velocity must be finite.", nameof(velocity));
        }

        var currentPosition = SpatialMath.Normalize(position, settings);
        nextAgeSteps = checked(ageSteps + 1);

        if (nextAgeSteps >= lifeSteps)
        {
            nextPosition = currentPosition;
            return false;
        }

        var deltaTime = 1.0f / tickRate;
        var displacement = new Vector3(
            velocity.X * deltaTime,
            velocity.Y * deltaTime,
            velocity.Z * deltaTime);
        var offset = currentPosition.Offset + displacement;

        if (!displacement.IsFinite().All() || !offset.IsFinite().All())
        {
            throw new ArithmeticException("The particle spatial integration is not finite.");
        }

        nextPosition = SpatialMath.Normalize(new SpatialPosition3(currentPosition.Chunk, offset), settings);
        return true;
    }

    private static float GetUnitInterval(uint word)
        => (word >> 8) * (1.0f / 16_777_216.0f);

    private static float Interpolate(float lower, float upper, float amount)
    {
        var range = upper - lower;
        var weighted = range * amount;
        var value = lower + weighted;

        if (!float.IsFinite(range) || !float.IsFinite(weighted) || !float.IsFinite(value))
        {
            throw new ArithmeticException("The particle curve interpolation is not finite.");
        }

        return value;
    }

    private static float SampleLane(float minimum, float maximum, float unit)
    {
        var range = maximum - minimum;
        var weighted = range * unit;
        var value = minimum + weighted;

        if (!float.IsFinite(range) || !float.IsFinite(weighted) || !float.IsFinite(value))
        {
            throw new ArithmeticException("The particle velocity sample is not finite.");
        }

        return value;
    }

    private static void ValidateNormalizedAge(float normalizedAge)
    {
        if (!float.IsFinite(normalizedAge) || (normalizedAge < 0.0f) || (normalizedAge > 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(normalizedAge));
        }
    }

    private static void ValidateVelocityRange(Vector3 minimum, Vector3 maximum)
    {
        if (!minimum.IsFinite().All()
            || !maximum.IsFinite().All()
            || (minimum.X > maximum.X)
            || (minimum.Y > maximum.Y)
            || (minimum.Z > maximum.Z))
        {
            throw new ArgumentException("Particle velocity limits must be finite and ordered.");
        }
    }

    internal struct Pcg32
    {
        private const ulong Multiplier = 6_364_136_223_846_793_005;

        private readonly ulong _increment;
        private ulong _state;

        internal Pcg32(ulong seed, ulong stream)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(stream, (ulong)long.MaxValue);
            _state = 0;
            _increment = (stream << 1) | 1;
            _ = NextUInt();
            _state = unchecked(_state + seed);
            _ = NextUInt();
        }

        internal readonly ulong Increment => _increment;

        internal readonly ulong State => _state;

        internal uint NextUInt()
        {
            if ((_increment & 1) == 0)
            {
                throw new InvalidOperationException("The PCG32 stream is uninitialized.");
            }

            var oldState = _state;
            _state = unchecked((oldState * Multiplier) + _increment);
            var xorshifted = unchecked((uint)(((oldState >> 18) ^ oldState) >> 27));
            var rotation = (int)(oldState >> 59);
            return (xorshifted >> rotation) | (xorshifted << ((-rotation) & 31));
        }
    }
}
