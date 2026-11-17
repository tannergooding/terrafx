// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using TerraFX;
using TerraFX.Assets;

namespace TerraFX.Animation;

internal static class AnimationControllerAssetReader
{
    private static readonly Guid s_actionLayerWeightSchemaId = new ContentSchemaIdentityAssignment("native/animation-action-layer-weight").SchemaId;
    private static readonly Guid s_actionNoneSchemaId = new ContentSchemaIdentityAssignment("native/animation-action-none").SchemaId;
    private static readonly Guid s_clipSchemaId = new ContentSchemaIdentityAssignment("native/clip").SchemaId;
    private static readonly Guid s_clipSlotSchemaId = new ContentSchemaIdentityAssignment("native/animation-clip-slot").SchemaId;
    private static readonly Guid s_clipWeightSchemaId = new ContentSchemaIdentityAssignment("native/animation-clip-weight").SchemaId;
    private static readonly Guid s_conditionAlwaysSchemaId = new ContentSchemaIdentityAssignment("native/animation-condition-always").SchemaId;
    private static readonly Guid s_conditionCompletedSchemaId = new ContentSchemaIdentityAssignment("native/animation-condition-completed").SchemaId;
    private static readonly Guid s_conditionManualSchemaId = new ContentSchemaIdentityAssignment("native/animation-condition-manual").SchemaId;
    private static readonly Guid s_controllerSchemaId = new ContentSchemaIdentityAssignment("native/animation-controller").SchemaId;
    private static readonly Guid s_layerSchemaId = new ContentSchemaIdentityAssignment("native/animation-layer").SchemaId;
    private static readonly Guid s_referencePoseSchemaId = new ContentSchemaIdentityAssignment("native/animation-reference-pose").SchemaId;
    private static readonly Guid s_sceneSchemaId = new ContentSchemaIdentityAssignment("native/scene").SchemaId;
    private static readonly Guid s_skeletonSchemaId = new ContentSchemaIdentityAssignment("native/skeleton").SchemaId;
    private static readonly Guid s_transitionSchemaId = new ContentSchemaIdentityAssignment("native/animation-transition").SchemaId;

    internal static AnimationControllerAsset Read(scoped EbinRecordReadView record, AssetCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ValidateHeader(record, s_controllerSchemaId, fieldCounts: [4, 5, 6, 7]);
        ValidateKnownFieldSet(record, maximumTag: 7, requiredTags: [3, 4, 5, 6]);
        var skeletonAssetId = ReadOptionalAssetReference(
            record,
            tag: 1,
            s_skeletonSchemaId,
            expectedMajor: 1,
            context);
        var sceneAssetId = ReadOptionalAssetReference(
            record,
            tag: 2,
            s_sceneSchemaId,
            expectedMajor: 2,
            context);
        var clipSlots = ReadClipSlots(record, context);
        var clipSlotIndices = new Dictionary<Guid, int>(clipSlots.Length);

        for (var index = 0; index < clipSlots.Length; index++)
        {
            clipSlotIndices.Add(clipSlots[index].ClipSlotId, index);
        }

        var layers = ReadLayers(record, clipSlotIndices);
        var transitions = ReadTransitions(record, clipSlotIndices, layers.Length);
        var rootMotionMode = ReadUInt8(record, tag: 6);
        var motionActorKey = ReadOptionalGuid(record, tag: 7);

        if ((rootMotionMode > 1)
            || ((rootMotionMode == 0) && motionActorKey.HasValue)
            || ((rootMotionMode == 1) && !motionActorKey.HasValue))
        {
            throw new InvalidDataException("Animation Controller root-motion mode and MotionActorKey must form a supported pair.");
        }

        return new AnimationControllerAsset(
            context.RootAssetId,
            skeletonAssetId,
            sceneAssetId,
            clipSlots,
            layers,
            transitions,
            rootMotionMode,
            motionActorKey,
            [.. record.EncodedBytes]);
    }

    private static EbinFieldReadView GetRequiredField(
        EbinRecordReadView record,
        uint tag,
        ushort kind,
        ulong count = 1)
    {
        if (!record.TryGetField(tag, out var field)
            || !field.IsRequired
            || (field.Kind != kind)
            || (field.Count != count))
        {
            throw new InvalidDataException($"Animation Controller field {tag} does not match its required wire shape.");
        }

        return field;
    }

    private static EbinFieldReadView GetRequiredFixedArray(
        EbinRecordReadView record,
        uint tag,
        ulong minimumCount,
        ulong maximumCount)
    {
        if (!record.TryGetField(tag, out var field)
            || !field.IsRequired
            || (field.Kind != 23)
            || (field.Count < minimumCount)
            || (field.Count > maximumCount))
        {
            throw new InvalidDataException($"Animation Controller field {tag} does not match its required FixedArray wire shape.");
        }

        return field;
    }

    private static EbinFieldReadView GetRequiredRecordArray(
        EbinRecordReadView record,
        uint tag,
        ulong minimumCount,
        ulong maximumCount)
    {
        if (!record.TryGetField(tag, out var field)
            || !field.IsRequired
            || (field.Kind != 24)
            || (field.Count < minimumCount)
            || (field.Count > maximumCount))
        {
            throw new InvalidDataException($"Animation Controller field {tag} does not match its required record-array wire shape.");
        }

        return field;
    }

    private static bool IsWeightValid(float weight)
        => float.IsFinite(weight) && (weight >= 0.0f) && (weight <= 1.0f);

    private static AnimationControllerAsset.AnimationTransitionAction ReadAction(EbinRecordReadView record)
    {
        var header = record.Header;

        if ((header.Major != 1) || (header.Minor != 0))
        {
            throw new NotSupportedException("The selected Animation transition action schema version is unsupported.");
        }

        if (header.SchemaId == s_actionNoneSchemaId)
        {
            ValidateHeader(record, s_actionNoneSchemaId, fieldCounts: [0]);
            return new AnimationControllerAsset.AnimationTransitionAction(0, null, null);
        }

        if (header.SchemaId != s_actionLayerWeightSchemaId)
        {
            throw new InvalidDataException("An Animation transition selects an unsupported action schema.");
        }

        ValidateHeader(record, s_actionLayerWeightSchemaId, fieldCounts: [2]);
        var layerIndex = ReadUInt8(record, tag: 1);
        var weight = ReadSingle(record, tag: 2);

        if ((layerIndex > 7) || !IsWeightValid(weight))
        {
            throw new InvalidDataException("An Animation layer-weight action has an invalid layer or weight.");
        }

        return new AnimationControllerAsset.AnimationTransitionAction(1, layerIndex, weight);
    }

    private static AssetId ReadAssetReference(
        EbinRecordReadView record,
        uint tag,
        Guid expectedSchemaId,
        uint expectedMajor,
        AssetCodecContext context)
        => AnimationClipAssetReader.ReadAssetReferenceValue(
            GetRequiredField(record, tag, kind: 22).Payload,
            expectedSchemaId,
            expectedMajor,
            context);

    private static ImmutableArray<AnimationControllerAsset.AnimationClipSlot> ReadClipSlots(
        EbinRecordReadView record,
        AssetCodecContext context)
    {
        var slotsField = GetRequiredRecordArray(record, tag: 3, minimumCount: 1, maximumCount: 32);
        var slots = ImmutableArray.CreateBuilder<AnimationControllerAsset.AnimationClipSlot>(checked((int)slotsField.Count));
        var slotIds = new HashSet<Guid>();
        var clipAssetIds = new HashSet<int>();

        for (var index = 0; index < slots.Capacity; index++)
        {
            var slotRecord = record.GetRecordArrayElement(3, index);
            ValidateHeader(slotRecord, s_clipSlotSchemaId, fieldCounts: [2]);
            var clipSlotId = ReadGuid(slotRecord, tag: 1);
            var clipAssetId = ReadAssetReference(
                slotRecord,
                tag: 2,
                s_clipSchemaId,
                expectedMajor: 1,
                context);

            if (!slotIds.Add(clipSlotId) || !clipAssetIds.Add(clipAssetId.Value))
            {
                throw new InvalidDataException("Animation Controller ClipSlotIds and Clip AssetIds must be independently unique.");
            }

            slots.Add(new AnimationControllerAsset.AnimationClipSlot(clipSlotId, clipAssetId));
        }

        return slots.MoveToImmutable();
    }

    private static AnimationControllerAsset.AnimationTransitionCondition ReadCondition(EbinRecordReadView record)
    {
        var header = record.Header;

        if ((header.Major != 1) || (header.Minor != 0))
        {
            throw new NotSupportedException("The selected Animation transition condition schema version is unsupported.");
        }

        if (header.SchemaId == s_conditionManualSchemaId)
        {
            ValidateHeader(record, s_conditionManualSchemaId, fieldCounts: [0]);
            return new AnimationControllerAsset.AnimationTransitionCondition(0);
        }

        if (header.SchemaId == s_conditionCompletedSchemaId)
        {
            ValidateHeader(record, s_conditionCompletedSchemaId, fieldCounts: [0]);
            return new AnimationControllerAsset.AnimationTransitionCondition(1);
        }

        if (header.SchemaId == s_conditionAlwaysSchemaId)
        {
            ValidateHeader(record, s_conditionAlwaysSchemaId, fieldCounts: [0]);
            return new AnimationControllerAsset.AnimationTransitionCondition(2);
        }

        throw new InvalidDataException("An Animation transition selects an unsupported condition schema.");
    }

    private static Guid ReadGuid(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 11);

        if (field.Payload.Length != 16)
        {
            throw new InvalidDataException($"Animation Controller field {tag} does not contain one GUID.");
        }

        var value = new Guid(field.Payload, bigEndian: true);
        return value != Guid.Empty
            ? value
            : throw new InvalidDataException($"Animation Controller field {tag} contains an empty GUID.");
    }

    private static ImmutableArray<AnimationControllerAsset.AnimationClipWeight> ReadInitialClips(
        EbinRecordReadView layerRecord,
        Dictionary<Guid, int> clipSlotIndices)
    {
        var clipsField = GetRequiredRecordArray(layerRecord, tag: 6, minimumCount: 0, maximumCount: 4);
        var clips = ImmutableArray.CreateBuilder<AnimationControllerAsset.AnimationClipWeight>(checked((int)clipsField.Count));
        var previousSlotIndex = -1;

        for (var index = 0; index < clips.Capacity; index++)
        {
            var clipRecord = layerRecord.GetRecordArrayElement(6, index);
            ValidateHeader(clipRecord, s_clipWeightSchemaId, fieldCounts: [2]);
            var clipSlotId = ReadGuid(clipRecord, tag: 1);
            var weight = ReadSingle(clipRecord, tag: 2);

            if (!clipSlotIndices.TryGetValue(clipSlotId, out var slotIndex)
                || (slotIndex <= previousSlotIndex)
                || !IsWeightValid(weight))
            {
                throw new InvalidDataException("Animation Controller initial Clips must be unique, controller-ordered slots with finite weights in [0,1].");
            }

            previousSlotIndex = slotIndex;
            clips.Add(new AnimationControllerAsset.AnimationClipWeight(clipSlotId, weight));
        }

        return clips.MoveToImmutable();
    }

    private static int ReadInt32(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 7);
        return BinaryPrimitives.ReadInt32LittleEndian(field.Payload);
    }

    private static long ReadInt64(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 8);
        return BinaryPrimitives.ReadInt64LittleEndian(field.Payload);
    }

    private static ImmutableArray<AnimationControllerAsset.AnimationLayer> ReadLayers(
        EbinRecordReadView record,
        Dictionary<Guid, int> clipSlotIndices)
    {
        var layerArray = GetRequiredRecordArray(record, tag: 4, minimumCount: 1, maximumCount: 8);
        var layers = ImmutableArray.CreateBuilder<AnimationControllerAsset.AnimationLayer>(checked((int)layerArray.Count));

        for (var index = 0; index < layers.Capacity; index++)
        {
            var layerRecord = record.GetRecordArrayElement(4, index);
            ValidateHeader(layerRecord, s_layerSchemaId, fieldCounts: [7]);
            var layerIndex = ReadUInt8(layerRecord, tag: 1);
            var mode = ReadUInt8(layerRecord, tag: 2);
            var weight = ReadSingle(layerRecord, tag: 3);
            var mask = ReadMask(layerRecord);
            var referencePose = ReadReferencePose(layerRecord);
            var initialClips = ReadInitialClips(layerRecord, clipSlotIndices);
            var eventPolicy = ReadUInt8(layerRecord, tag: 7);

            if ((layerIndex != index)
                || (mode > 1)
                || !IsWeightValid(weight)
                || (eventPolicy > 2))
            {
                throw new InvalidDataException("Animation Controller layer indices, modes, weights, or event policies are invalid.");
            }

            layers.Add(new AnimationControllerAsset.AnimationLayer(
                layerIndex,
                mode,
                weight,
                mask,
                referencePose,
                initialClips,
                eventPolicy));
        }

        return layers.MoveToImmutable();
    }

    private static ImmutableArray<float> ReadMask(EbinRecordReadView layerRecord)
    {
        var field = GetRequiredFixedArray(layerRecord, tag: 4, minimumCount: 0, maximumCount: 4352);

        if (field.Payload.Length < 32)
        {
            throw new InvalidDataException("Animation Controller layer Mask is shorter than its FixedArray header.");
        }

        var elementKind = BinaryPrimitives.ReadUInt16LittleEndian(field.Payload[16..]);
        var stride = BinaryPrimitives.ReadUInt32LittleEndian(field.Payload[20..]);
        var dataLength = checked((long)field.Count * sizeof(float));

        if ((field.Count > 4352)
            || (elementKind != 10)
            || (stride != sizeof(float))
            || (field.Payload.Length != checked(32 + (int)dataLength)))
        {
            throw new InvalidDataException("Animation Controller layer Mask does not match its bounded binary32 FixedArray shape.");
        }

        var values = ImmutableArray.CreateBuilder<float>(checked((int)field.Count));

        for (var index = 0; index < values.Capacity; index++)
        {
            var value = EbinPrimitives.Single(field.Payload[(32 + (index * sizeof(float)))..]);

            if (!IsWeightValid(value))
            {
                throw new InvalidDataException("Animation Controller layer Mask weights must be finite and in [0,1].");
            }

            values.Add(value);
        }

        return values.MoveToImmutable();
    }

    private static AssetId? ReadOptionalAssetReference(
        EbinRecordReadView record,
        uint tag,
        Guid expectedSchemaId,
        uint expectedMajor,
        AssetCodecContext context)
    {
        if (!record.TryGetField(tag, out var field))
        {
            return null;
        }

        if (field.IsRequired || (field.Kind != 22) || (field.Count != 1))
        {
            throw new InvalidDataException($"Animation Controller optional AssetReference field {tag} has an invalid wire shape.");
        }

        return AnimationClipAssetReader.ReadAssetReferenceValue(
            field.Payload,
            expectedSchemaId,
            expectedMajor,
            context);
    }

    private static Guid? ReadOptionalGuid(EbinRecordReadView record, uint tag)
    {
        if (!record.TryGetField(tag, out var field))
        {
            return null;
        }

        if (field.IsRequired
            || (field.Kind != 11)
            || (field.Count != 1)
            || (field.Payload.Length != 16))
        {
            throw new InvalidDataException($"Animation Controller optional GUID field {tag} has an invalid wire shape or reference annotation.");
        }

        var value = new Guid(field.Payload, bigEndian: true);
        return value != Guid.Empty
            ? value
            : throw new InvalidDataException($"Animation Controller optional GUID field {tag} is empty.");
    }

    private static ImmutableArray<AnimationControllerAsset.AnimationReferencePose> ReadReferencePose(
        EbinRecordReadView layerRecord)
    {
        var poseArray = GetRequiredRecordArray(layerRecord, tag: 5, minimumCount: 0, maximumCount: 4352);
        var poses = ImmutableArray.CreateBuilder<AnimationControllerAsset.AnimationReferencePose>(checked((int)poseArray.Count));
        byte previousDomain = 0;
        var previousTargetId = Guid.Empty;

        for (var index = 0; index < poses.Capacity; index++)
        {
            var poseRecord = layerRecord.GetRecordArrayElement(5, index);
            ValidateHeader(poseRecord, s_referencePoseSchemaId, fieldCounts: [3]);
            var targetDomain = ReadUInt8(poseRecord, tag: 1);
            var targetId = ReadGuid(poseRecord, tag: 2);
            var (translation, rotation, scale) = AnimationAssetReader.ReadLocalTransform(poseRecord.GetRecord(3));

            if ((targetDomain > 1)
                || ((index > 0) && ((targetDomain < previousDomain)
                    || ((targetDomain == previousDomain)
                        && (FoundationCatalogue.CompareRfc(previousTargetId, targetId) >= 0)))))
            {
                throw new InvalidDataException("Animation Controller ReferencePose entries must be unique and ordered by target domain and RFC identity.");
            }

            previousDomain = targetDomain;
            previousTargetId = targetId;
            poses.Add(new AnimationControllerAsset.AnimationReferencePose(
                targetDomain,
                targetId,
                translation,
                rotation,
                scale));
        }

        return poses.MoveToImmutable();
    }

    private static float ReadSingle(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 10);
        return EbinPrimitives.Single(field.Payload);
    }

    private static AnimationControllerAsset.AnimationTransition ReadTransition(
        EbinRecordReadView transitionRecord,
        Dictionary<Guid, int> clipSlotIndices,
        int layerCount)
    {
        ValidateHeader(transitionRecord, s_transitionSchemaId, fieldCounts: [9, 10]);
        ValidateKnownFieldSet(
            transitionRecord,
            maximumTag: 10,
            requiredTags: [1, 2, 4, 5, 6, 7, 8, 9, 10]);
        var transitionId = ReadGuid(transitionRecord, tag: 1);
        var layerIndex = ReadUInt8(transitionRecord, tag: 2);
        var sourceClipSlotId = ReadOptionalGuid(transitionRecord, tag: 3);
        var targetClipSlotId = ReadGuid(transitionRecord, tag: 4);
        var durationTicks = ReadInt64(transitionRecord, tag: 5);
        var priority = ReadInt32(transitionRecord, tag: 6);
        var condition = ReadCondition(transitionRecord.GetRecord(7));
        var interruptPolicy = ReadUInt8(transitionRecord, tag: 8);
        var action = ReadAction(transitionRecord.GetRecord(9));
        var eventPolicy = ReadUInt8(transitionRecord, tag: 10);

        if ((layerIndex >= layerCount)
            || (durationTicks < 0)
            || (interruptPolicy > 1)
            || (eventPolicy > 2)
            || !clipSlotIndices.ContainsKey(targetClipSlotId)
            || (sourceClipSlotId is Guid source && !clipSlotIndices.ContainsKey(source))
            || (sourceClipSlotId == targetClipSlotId))
        {
            throw new InvalidDataException("An Animation transition has an invalid layer, duration, policy, or Clip-slot operand.");
        }

        if ((action.LayerIndex is byte actionLayerIndex) && (actionLayerIndex >= layerCount))
        {
            throw new InvalidDataException("An Animation transition action selects an absent layer.");
        }

        return new AnimationControllerAsset.AnimationTransition(
            transitionId,
            layerIndex,
            sourceClipSlotId,
            targetClipSlotId,
            durationTicks,
            priority,
            condition,
            interruptPolicy,
            action,
            eventPolicy);
    }

    private static ImmutableArray<AnimationControllerAsset.AnimationTransition> ReadTransitions(
        EbinRecordReadView record,
        Dictionary<Guid, int> clipSlotIndices,
        int layerCount)
    {
        var transitionArray = GetRequiredRecordArray(record, tag: 5, minimumCount: 0, maximumCount: 64);
        var transitions = ImmutableArray.CreateBuilder<AnimationControllerAsset.AnimationTransition>(checked((int)transitionArray.Count));
        var previousId = Guid.Empty;

        for (var index = 0; index < transitions.Capacity; index++)
        {
            var transition = ReadTransition(record.GetRecordArrayElement(5, index), clipSlotIndices, layerCount);

            if ((index > 0) && (FoundationCatalogue.CompareRfc(previousId, transition.TransitionId) >= 0))
            {
                throw new InvalidDataException("Animation Controller TransitionIds must be uniquely RFC-ordered.");
            }

            previousId = transition.TransitionId;
            transitions.Add(transition);
        }

        return transitions.MoveToImmutable();
    }

    private static byte ReadUInt8(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 1);
        return field.Payload[0];
    }

    private static void ValidateHeader(
        EbinRecordReadView record,
        Guid schemaId,
        ReadOnlySpan<int> fieldCounts)
    {
        var header = record.Header;

        if ((header.SchemaId != schemaId) || (header.Major != 1) || (header.Minor != 0))
        {
            throw new NotSupportedException("The selected Animation Controller schema version is unsupported.");
        }

        foreach (var fieldCount in fieldCounts)
        {
            if (header.FieldCount == fieldCount)
            {
                return;
            }
        }

        throw new InvalidDataException("An Animation Controller record has an invalid field count.");
    }

    private static void ValidateKnownFieldSet(
        EbinRecordReadView record,
        uint maximumTag,
        ReadOnlySpan<uint> requiredTags)
    {
        Span<bool> present = stackalloc bool[checked((int)maximumTag + 1)];
        present.Clear();

        for (var index = 0; index < record.FieldCount; index++)
        {
            var tag = record.GetField(index).Tag;

            if ((tag > maximumTag) || present[checked((int)tag)])
            {
                throw new InvalidDataException("An Animation Controller record contains an unsupported or duplicate field tag.");
            }

            present[checked((int)tag)] = true;
        }

        foreach (var tag in requiredTags)
        {
            if (!present[checked((int)tag)])
            {
                throw new InvalidDataException($"Animation Controller record is missing required field {tag}.");
            }
        }
    }
}
