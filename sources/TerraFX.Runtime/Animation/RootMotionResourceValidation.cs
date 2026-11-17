// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Entities;
using TerraFX.Numerics;

namespace TerraFX.Animation;

internal static class RootMotionResourceValidation
{
    internal static bool IsValid(in RootMotionRequest request)
    {
        if (!request.HasCanonicalStorage || !request.IsValid)
        {
            return request.HasCanonicalStorage;
        }

        Span<float> weights = stackalloc float[4];
        weights.Clear();

        for (var index = 0; index < request.SourceCount; index++)
        {
            var source = request.GetSource(index);
            weights[index] = source.Weight;

            for (var previous = 0; previous < index; previous++)
            {
                if (request.GetSource(previous).ClipSlotId == source.ClipSlotId)
                {
                    return false;
                }
            }
        }

        return IsValidWeights(weights[..request.SourceCount]);
    }

    internal static bool IsValidWeights(scoped ReadOnlySpan<float> weights)
    {
        if (weights.IsEmpty)
        {
            return true;
        }

        if (weights.Length == 1)
        {
            return weights[0] == 1.0f;
        }

        if (weights.Length > 4)
        {
            return false;
        }

        UInt128 low = 0;
        ulong high = 0;

        foreach (var weight in weights)
        {
            if (!float.IsFinite(weight) || (weight <= 0) || (weight > 1))
            {
                return false;
            }

            var bits = unchecked((uint)BitConverter.SingleToInt32Bits(weight));
            var exponent = (int)((bits >> 23) & 255);
            var significand = bits & 0x7FFFFF;

            if (exponent != 0)
            {
                significand |= 0x800000;
            }

            var shift = exponent == 0 ? 0 : exponent - 1;
            var part = (UInt128)significand << shift;
            var next = unchecked(low + part);
            high += (shift > 104 ? (ulong)significand >> (128 - shift) : 0) + (next < low ? 1UL : 0);
            low = next;
        }

        // Scaling by 2^149 makes the exact 2^-21 admissibility bound 2^128.
        const ulong OneHigh = 1UL << 21;
        return (high >= OneHigh - 1) && (high <= OneHigh + 1)
            && ((high != OneHigh + 1) || (low == 0));
    }

    internal readonly struct ResourceValidator : IWorldResourcePartitionRecordsValidator<RootMotionRequest, RootMotionResourceAuthor>
    {
        private static bool HasUniqueActors(Span<Guid> actors)
        {
            actors.Sort();

            for (var index = 1; index < actors.Length; index++)
            {
                if (actors[index - 1] == actors[index])
                {
                    return false;
                }
            }

            return true;
        }

        public bool IsValidAuthor(in RootMotionResourceAuthor value)
        {
            if (value is null || value.Values.IsDefault || (value.Values.Length != 4096))
            {
                return false;
            }

            var absentSeen = false;
            var previousPlayer = Guid.Empty;
            long? tick = null;
            Span<Guid> actors = stackalloc Guid[4096];
            actors.Clear();
            var actorCount = 0;
            var slotValidator = default(SlotValidator);

            foreach (var slot in value.Values)
            {
                if (!slotValidator.IsValidAuthor(in slot))
                {
                    return false;
                }

                if (!slot.Present)
                {
                    absentSeen = true;
                    continue;
                }

                if (slot.PlayerId is not { } player || slot.ActorId is not { } actor
                    || slot.TickIndex is not { } slotTick
                    || absentSeen || ((previousPlayer != Guid.Empty) && (FoundationCatalogue.CompareRfc(previousPlayer, player) >= 0))
                    || (tick.HasValue && (slotTick != tick)))
                {
                    return false;
                }

                previousPlayer = player;
                tick = slotTick;
                actors[actorCount++] = actor;
            }

            return HasUniqueActors(actors[..actorCount]);
        }

        public bool IsValidRuntime(scoped ReadOnlySpan<RootMotionRequest> partitions)
        {
            if (partitions.Length != 4096)
            {
                return false;
            }

            var absentSeen = false;
            var previousPlayer = Guid.Empty;
            Guid? world = null;
            long? tick = null;
            Span<Guid> actors = stackalloc Guid[4096];
            actors.Clear();
            var actorCount = 0;

            foreach (ref readonly var request in partitions)
            {
                if (!IsValid(in request))
                {
                    return false;
                }

                if (!request.IsValid)
                {
                    absentSeen = true;
                    continue;
                }

                var player = request.Player;

                if (absentSeen || ((previousPlayer != Guid.Empty) && (FoundationCatalogue.CompareRfc(previousPlayer, player.EntityId) >= 0))
                    || (tick.HasValue && (request.TickIndex != tick))
                    || (world.HasValue && (player.WorldGuid != world)))
                {
                    return false;
                }

                previousPlayer = player.EntityId;
                tick = request.TickIndex;
                world = player.WorldGuid;
                actors[actorCount++] = request.Actor.EntityId;
            }

            return HasUniqueActors(actors[..actorCount]);
        }
    }

    internal readonly struct SlotValidator : IWorldResourceRecordValueValidator<RootMotionRequest, RootMotionSlotAuthor>
    {
        public bool IsValidAuthor(in RootMotionSlotAuthor value)
        {
            if (value is null)
            {
                return false;
            }

            if (!value.Present)
            {
                return !value.PlayerId.HasValue && !value.PlayerCreationLineageId.HasValue
                    && !value.PlayerCreationBranchId.HasValue && !value.ActorId.HasValue
                    && !value.ActorCreationLineageId.HasValue && !value.ActorCreationBranchId.HasValue
                    && !value.TickIndex.HasValue && !value.Translation.HasValue && !value.Rotation.HasValue
                    && !value.Sources.HasValue && !value.ExtractionPolicy.HasValue && !value.Controller.HasValue;
            }

            if (!value.PlayerId.HasValue || !value.PlayerCreationLineageId.HasValue || !value.PlayerCreationBranchId.HasValue
                || !value.ActorId.HasValue || !value.ActorCreationLineageId.HasValue || !value.ActorCreationBranchId.HasValue
                || !value.TickIndex.HasValue || !value.Translation.HasValue || !value.Rotation.HasValue
                || !value.Sources.HasValue || value.Sources.Value.IsDefault || (value.Sources.Value.Length > 4)
                || !value.ExtractionPolicy.HasValue || !value.Controller.HasValue)
            {
                return false;
            }

            Span<float> weights = stackalloc float[4];
            weights.Clear();

            for (var index = 0; index < value.Sources.Value.Length; index++)
            {
                var source = value.Sources.Value[index];

                if (!source.IsValid)
                {
                    return false;
                }

                weights[index] = source.Weight;

                for (var previous = 0; previous < index; previous++)
                {
                    if (value.Sources.Value[previous].ClipSlotId == source.ClipSlotId)
                    {
                        return false;
                    }
                }
            }

            if ((value.PlayerId == Guid.Empty) || (value.PlayerCreationLineageId == Guid.Empty)
                || (value.PlayerCreationBranchId == Guid.Empty) || (value.ActorId == Guid.Empty)
                || (value.ActorCreationLineageId == Guid.Empty) || (value.ActorCreationBranchId == Guid.Empty)
                || (value.TickIndex < 0) || !value.Controller.Value.IsValid || (value.Controller.Value.SchemaMajor != 1)
                || (value.ExtractionPolicy != (byte)RootMotionExtractionPolicy.Extract))
            {
                return false;
            }

            var translation = value.Translation.Value;
            var rotation = value.Rotation.Value;
            var lengthSquared = Quaternion.Dot(rotation, rotation);
            var identityRequired = value.Sources.Value.IsEmpty;
            return translation.IsFinite().All() && rotation.IsFinite().All() && float.IsFinite(lengthSquared)
                && (MathF.Abs(lengthSquared - 1.0f) <= 2.0e-6f)
                && IsValidWeights(weights[..value.Sources.Value.Length])
                && (!identityRequired
                    || ((BitConverter.SingleToInt32Bits(translation.X) == 0)
                        && (BitConverter.SingleToInt32Bits(translation.Y) == 0)
                        && (BitConverter.SingleToInt32Bits(translation.Z) == 0)
                        && (BitConverter.SingleToInt32Bits(rotation.X) == 0)
                        && (BitConverter.SingleToInt32Bits(rotation.Y) == 0)
                        && (BitConverter.SingleToInt32Bits(rotation.Z) == 0)
                        && (BitConverter.SingleToInt32Bits(rotation.W) == BitConverter.SingleToInt32Bits(1.0f))));
        }

        public bool IsValidRuntime(in RootMotionRequest value) => IsValid(in value);
    }

    internal readonly struct SourceValidator : IWorldResourceRecordValueValidator<RootMotionSource, RootMotionSourceAuthor>
    {
        public bool IsValidAuthor(in RootMotionSourceAuthor value) => value.IsValid;

        public bool IsValidRuntime(in RootMotionSource value) => value.IsValid;
    }
}
