// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Entities;
using TerraFX.Numerics;

namespace TerraFX.Animation;

internal sealed class RootMotionSlotAuthor
{
    internal RootMotionSlotAuthor(
        bool present,
        Guid? playerId,
        Guid? playerCreationLineageId,
        Guid? playerCreationBranchId,
        Guid? actorId,
        Guid? actorCreationLineageId,
        Guid? actorCreationBranchId,
        long? tickIndex,
        Vector3? translation,
        Quaternion? rotation,
        ImmutableArray<RootMotionSourceAuthor>? sources,
        byte? extractionPolicy,
        WorldAssetReference? controller)
    {
        if (!present)
        {
            if (playerId.HasValue
                || playerCreationLineageId.HasValue
                || playerCreationBranchId.HasValue
                || actorId.HasValue
                || actorCreationLineageId.HasValue
                || actorCreationBranchId.HasValue
                || tickIndex.HasValue
                || translation.HasValue
                || rotation.HasValue
                || sources.HasValue
                || extractionPolicy.HasValue
                || controller.HasValue)
            {
                throw new ArgumentException("An absent root-motion slot must not contain body data.", nameof(present));
            }

            Present = false;
            PlayerId = null;
            PlayerCreationLineageId = null;
            PlayerCreationBranchId = null;
            ActorId = null;
            ActorCreationLineageId = null;
            ActorCreationBranchId = null;
            TickIndex = null;
            Translation = null;
            Rotation = null;
            Sources = null;
            ExtractionPolicy = null;
            Controller = null;
            return;
        }

        if (!playerId.HasValue
            || !playerCreationLineageId.HasValue
            || !playerCreationBranchId.HasValue
            || !actorId.HasValue
            || !actorCreationLineageId.HasValue
            || !actorCreationBranchId.HasValue
            || !tickIndex.HasValue
            || !translation.HasValue
            || !rotation.HasValue
            || !sources.HasValue
            || !extractionPolicy.HasValue
            || !controller.HasValue)
        {
            throw new ArgumentException("A present root-motion slot requires every body field.", nameof(present));
        }

        if ((playerId == Guid.Empty)
            || (playerCreationLineageId == Guid.Empty)
            || (playerCreationBranchId == Guid.Empty)
            || (actorId == Guid.Empty)
            || (actorCreationLineageId == Guid.Empty)
            || (actorCreationBranchId == Guid.Empty))
        {
            throw new ArgumentException("Root-motion player and actor identity facts cannot be empty.", nameof(playerId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(tickIndex.Value);

        if (!translation.Value.IsFinite().All())
        {
            throw new ArgumentOutOfRangeException(nameof(translation), "Root-motion translation must be finite.");
        }

        var rotationLengthSquared = Quaternion.Dot(rotation.Value, rotation.Value);

        if (!rotation.Value.IsFinite().All()
            || !float.IsFinite(rotationLengthSquared)
            || (MathF.Abs(rotationLengthSquared - 1.0f) > 2.0e-6f))
        {
            throw new ArgumentOutOfRangeException(nameof(rotation), "Root-motion rotation must be a finite unit quaternion.");
        }

        if (sources.Value.IsDefault || (sources.Value.Length > 4))
        {
            throw new ArgumentOutOfRangeException(nameof(sources), "Root-motion sources must be initialized and contain at most four entries.");
        }

        if (extractionPolicy.Value is not (byte)RootMotionExtractionPolicy.Extract)
        {
            throw new ArgumentOutOfRangeException(nameof(extractionPolicy));
        }

        if (!controller.Value.IsValid || (controller.Value.SchemaMajor != 1))
        {
            throw new ArgumentException("The root-motion Controller must select a valid native/animation-controller major-1 reference.", nameof(controller));
        }

        for (var index = 0; index < sources.Value.Length; index++)
        {
            var source = sources.Value[index];

            if (!source.IsValid)
            {
                throw new ArgumentException("Root-motion source rows must be valid with unique ClipSlotIds.", nameof(sources));
            }

            for (var previous = 0; previous < index; previous++)
            {
                if (sources.Value[previous].ClipSlotId == source.ClipSlotId)
                {
                    throw new ArgumentException("Root-motion source rows must be valid with unique ClipSlotIds.", nameof(sources));
                }
            }
        }

        if (sources.Value.IsEmpty
            && (!IsPositiveZero(translation.Value) || !IsIdentity(rotation.Value)))
        {
            throw new ArgumentException("An identity root-motion request must contain positive-zero translation and identity rotation.", nameof(sources));
        }

        Present = true;
        PlayerId = playerId;
        PlayerCreationLineageId = playerCreationLineageId;
        PlayerCreationBranchId = playerCreationBranchId;
        ActorId = actorId;
        ActorCreationLineageId = actorCreationLineageId;
        ActorCreationBranchId = actorCreationBranchId;
        TickIndex = tickIndex;
        Translation = translation;
        Rotation = rotation;
        Sources = ImmutableArrayCopy.Create(sources.Value);
        ExtractionPolicy = extractionPolicy;
        Controller = controller;
    }

    internal Guid? ActorCreationBranchId { get; }

    internal Guid? ActorCreationLineageId { get; }

    internal Guid? ActorId { get; }

    internal WorldAssetReference? Controller { get; }

    internal byte? ExtractionPolicy { get; }

    internal Guid? PlayerCreationBranchId { get; }

    internal Guid? PlayerCreationLineageId { get; }

    internal Guid? PlayerId { get; }

    internal bool Present { get; }

    internal Quaternion? Rotation { get; }

    internal ImmutableArray<RootMotionSourceAuthor>? Sources { get; }

    internal long? TickIndex { get; }

    internal Vector3? Translation { get; }

    private static bool IsIdentity(Quaternion value)
        => (BitConverter.SingleToInt32Bits(value.X) == 0)
        && (BitConverter.SingleToInt32Bits(value.Y) == 0)
        && (BitConverter.SingleToInt32Bits(value.Z) == 0)
        && (BitConverter.SingleToInt32Bits(value.W) == BitConverter.SingleToInt32Bits(1.0f));

    private static bool IsPositiveZero(Vector3 value)
        => (BitConverter.SingleToInt32Bits(value.X) == 0)
        && (BitConverter.SingleToInt32Bits(value.Y) == 0)
        && (BitConverter.SingleToInt32Bits(value.Z) == 0);
}
