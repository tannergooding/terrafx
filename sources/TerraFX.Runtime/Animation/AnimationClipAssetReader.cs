// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Numerics;
using BigInteger = System.Numerics.BigInteger;

namespace TerraFX.Animation;

internal static class AnimationClipAssetReader
{
    private const byte ChannelRotation = 1;
    private const byte ChannelScale = 2;
    private const byte ChannelSpriteFrame = 3;
    private const byte InterpolationCubic = 2;
    private const byte InterpolationStep = 0;
    private const byte TargetDomainScene = 0;
    private const byte TargetDomainSkeleton = 1;

    private static readonly Guid s_assetReferenceSchemaId = new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId;
    private static readonly Guid s_atlasSchemaId = new ContentSchemaIdentityAssignment("native/atlas").SchemaId;
    private static readonly Guid s_boundsSchemaId = new ContentSchemaIdentityAssignment("native/bounds3").SchemaId;
    private static readonly Guid s_clipEventSchemaId = new ContentSchemaIdentityAssignment("native/clip-event").SchemaId;
    private static readonly Guid s_clipSchemaId = new ContentSchemaIdentityAssignment("native/clip").SchemaId;
    private static readonly Guid s_eventMarkerSchemaId = new ContentSchemaIdentityAssignment("native/event-marker").SchemaId;
    private static readonly Guid s_keyQuaternionSchemaId = new ContentSchemaIdentityAssignment("native/key-quaternion").SchemaId;
    private static readonly Guid s_keySpriteSchemaId = new ContentSchemaIdentityAssignment("native/key-sprite").SchemaId;
    private static readonly Guid s_keyVector3SchemaId = new ContentSchemaIdentityAssignment("native/key-vector3").SchemaId;
    private static readonly Guid s_rootMotionSchemaId = new ContentSchemaIdentityAssignment("native/root-motion").SchemaId;
    private static readonly Guid s_sceneSchemaId = new ContentSchemaIdentityAssignment("native/scene").SchemaId;
    private static readonly Guid s_skeletonSchemaId = new ContentSchemaIdentityAssignment("native/skeleton").SchemaId;
    private static readonly Guid s_spriteTrackSchemaId = new ContentSchemaIdentityAssignment("native/sprite-track").SchemaId;
    private static readonly Guid s_transformTrackSchemaId = new ContentSchemaIdentityAssignment("native/transform-track").SchemaId;

    internal static AnimationClipAsset Read(scoped EbinRecordReadView record, AssetCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ValidateHeader(record, s_clipSchemaId, fieldCounts: [6, 7, 8, 9, 10]);
        ValidateKnownFieldSet(record, maximumTag: 10, requiredTags: [1, 2, 3, 4, 8, 9]);
        var durationTicks = ReadInt64(record, tag: 1);
        var loopMode = ReadUInt8(record, tag: 4);
        var boundsQualification = ReadUInt8(record, tag: 8);
        var boundsMargin = ReadSingle(record, tag: 9);

        if ((durationTicks <= 0)
            || (loopMode > 1)
            || (boundsQualification != 0)
            || (boundsMargin < 0.0f))
        {
            throw new InvalidDataException("Animation Clip duration, loop mode, bounds qualification, or margin is invalid.");
        }

        var tracks = ReadTracks(record, durationTicks, context);
        var events = ReadEvents(record, durationTicks, context);
        var hasSceneTrack = false;
        var hasJointTrack = false;

        foreach (var track in tracks)
        {
            if (track.TargetDomain == 0)
            {
                hasSceneTrack = true;
            }
            else
            {
                hasJointTrack = true;
            }
        }

        AssetId? skeletonAssetId = null;
        AssetId? sceneAssetId = null;

        if (record.TryGetField(5, out var skeletonField))
        {
            if (skeletonField.IsRequired || (skeletonField.Kind != 22) || (skeletonField.Count != 1))
            {
                throw new InvalidDataException("Animation Clip Skeleton must have the optional asset-reference shape.");
            }

            skeletonAssetId = ReadAssetReferenceValue(skeletonField.Payload, s_skeletonSchemaId, expectedMajor: 1, context);
        }

        if (record.TryGetField(10, out var sceneField))
        {
            if (sceneField.IsRequired || (sceneField.Kind != 22) || (sceneField.Count != 1))
            {
                throw new InvalidDataException("Animation Clip Scene must have the optional asset-reference shape.");
            }

            sceneAssetId = ReadAssetReferenceValue(sceneField.Payload, s_sceneSchemaId, expectedMajor: 2, context);
        }

        AnimationClipAsset.RootMotionInfo? rootMotion = null;

        if (record.TryGetField(6, out var rootMotionField))
        {
            if (rootMotionField.IsRequired || (rootMotionField.Kind != 22) || (rootMotionField.Count != 1))
            {
                throw new InvalidDataException("Animation Clip RootMotion must have the optional record shape.");
            }

            rootMotion = ReadRootMotion(rootMotionField.Payload);
        }

        if ((skeletonAssetId.HasValue != (hasJointTrack || rootMotion.HasValue))
            || (sceneAssetId.HasValue != hasSceneTrack))
        {
            throw new InvalidDataException("Animation Clip Skeleton and Scene references must exactly match their symbolic track domains and root-motion declaration.");
        }

        AnimationClipAsset.ClipBounds? bounds = null;

        if (record.TryGetField(7, out var boundsField))
        {
            if (boundsField.IsRequired || (boundsField.Kind != 22) || (boundsField.Count != 1))
            {
                throw new InvalidDataException("Animation Clip Bounds must have the optional record shape.");
            }

            bounds = ReadBounds(boundsField.Payload);
        }

        return new AnimationClipAsset(
            context.RootAssetId,
            durationTicks,
            loopMode,
            skeletonAssetId,
            sceneAssetId,
            rootMotion,
            bounds,
            boundsQualification,
            boundsMargin,
            tracks,
            events,
            [.. record.EncodedBytes]);
    }

    internal static AssetId ReadAssetReferenceValue(
        scoped ReadOnlySpan<byte> payload,
        Guid expectedSchemaId,
        uint expectedMajor,
        AssetCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var reference = new EbinRecordReadView(payload);
        ValidateHeader(reference, s_assetReferenceSchemaId, fieldCounts: [3]);
        var assetIdValue = ReadUInt32(reference, tag: 1);
        var schemaId = ReadGuid(reference, tag: 2);
        var schemaMajor = ReadUInt32(reference, tag: 3);

        if ((assetIdValue is 0 or > int.MaxValue)
            || (schemaId != expectedSchemaId)
            || (schemaMajor != expectedMajor))
        {
            throw new InvalidDataException("An Animation asset reference does not name its owner-selected native asset schema.");
        }

        var schemaReference = new ContentSchemaReference(schemaId, checked((int)schemaMajor), 0, ContentSchemaScope.Content);

        if (!context.Schemas.TryGetSchema(schemaReference, out var schema)
            || (schema.Role != ContentSchemaRole.NativeAsset))
        {
            throw new InvalidDataException("An Animation asset reference selects an absent or non-native compiled schema.");
        }

        return new AssetId(checked((int)assetIdValue));
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
            throw new InvalidDataException($"Animation Clip field {tag} does not match its required wire shape.");
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
            || (field.Kind != 24))
        {
            throw new InvalidDataException($"Animation Clip field {tag} does not match its required record-array wire shape.");
        }

        if ((field.Count < minimumCount) || (field.Count > maximumCount))
        {
            throw new InvalidDataException($"Animation Clip field {tag} has a record-array count outside its admitted range.");
        }

        return field;
    }

    private static AssetId ReadAssetReference(
        EbinRecordReadView record,
        uint tag,
        Guid expectedSchemaId,
        uint expectedMajor,
        AssetCodecContext context)
        => ReadAssetReferenceValue(
            GetRequiredField(record, tag, kind: 22).Payload,
            expectedSchemaId,
            expectedMajor,
            context);

    private static AnimationClipAsset.ClipBounds ReadBounds(scoped ReadOnlySpan<byte> encodedBounds)
    {
        var record = new EbinRecordReadView(encodedBounds);
        ValidateHeader(record, s_boundsSchemaId, fieldCounts: [4]);
        var minimum = ReadVector3(record, tag: 1);
        var maximum = ReadVector3(record, tag: 2);
        var sphereCenter = ReadVector3(record, tag: 3);
        var sphereRadius = ReadSingle(record, tag: 4);

        if ((minimum.X > maximum.X)
            || (minimum.Y > maximum.Y)
            || (minimum.Z > maximum.Z)
            || (sphereRadius < 0.0f))
        {
            throw new InvalidDataException("Animation Clip bounds are unordered or have a negative sphere radius.");
        }

        ValidateSphereContainsBounds(minimum, maximum, sphereCenter, sphereRadius);
        return new AnimationClipAsset.ClipBounds(minimum, maximum, sphereCenter, sphereRadius);
    }

    private static ImmutableArray<AnimationClipAsset.AnimationClipEvent> ReadEvents(
        EbinRecordReadView record,
        long durationTicks,
        AssetCodecContext context)
    {
        var eventArray = GetRequiredRecordArray(
            record,
            tag: 3,
            minimumCount: 0,
            maximumCount: checked((ulong)context.ReadLimits.MaximumArrayElements));
        var events = ImmutableArray.CreateBuilder<AnimationClipAsset.AnimationClipEvent>(checked((int)eventArray.Count));
        var eventIds = new HashSet<Guid>();
        var previousTime = -1L;
        var previousEventId = Guid.Empty;

        for (var index = 0; index < checked((int)eventArray.Count); index++)
        {
            var eventRecord = record.GetRecordArrayElement(3, index);
            ValidateHeader(eventRecord, s_clipEventSchemaId, fieldCounts: [3]);
            var eventId = ReadGuid(eventRecord, tag: 1);
            var timeTicks = ReadInt64(eventRecord, tag: 2);
            var payloadRecord = eventRecord.GetRecord(3);

            if ((eventId == Guid.Empty)
                || !eventIds.Add(eventId)
                || (timeTicks < 0)
                || (timeTicks > durationTicks)
                || ((index > 0) && ((timeTicks < previousTime)
                    || ((timeTicks == previousTime)
                        && (FoundationCatalogue.CompareRfc(previousEventId, eventId) >= 0)))))
            {
                throw new InvalidDataException("Animation Clip event identities and times must be uniquely ordered within duration.");
            }

            var schema = ValidateEventPayload(payloadRecord, context);
            events.Add(new AnimationClipAsset.AnimationClipEvent(
                eventId,
                timeTicks,
                schema,
                [.. payloadRecord.EncodedBytes]));
            previousEventId = eventId;
            previousTime = timeTicks;
        }

        return events.MoveToImmutable();
    }

    private static Guid ReadGuid(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 11);

        if (field.Payload.Length != 16)
        {
            throw new InvalidDataException($"Animation Clip field {tag} does not contain one GUID.");
        }

        return new Guid(field.Payload, bigEndian: true);
    }

    private static long ReadInt64(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 8);

        if (field.Payload.Length != sizeof(long))
        {
            throw new InvalidDataException($"Animation Clip field {tag} does not contain one Int64.");
        }

        return BinaryPrimitives.ReadInt64LittleEndian(field.Payload);
    }

    private static Vector3? ReadOptionalVector3(EbinRecordReadView record, uint tag)
    {
        if (!record.TryGetField(tag, out var field))
        {
            return null;
        }

        if (field.IsRequired || (field.Kind != 15) || (field.Count != 1) || (field.Payload.Length != 12))
        {
            throw new InvalidDataException($"Animation Clip optional Vector3 field {tag} has an invalid wire shape.");
        }

        var value = EbinPrimitives.Vector3(field.Payload);
        return value.IsFinite().All()
            ? value
            : throw new InvalidDataException($"Animation Clip optional Vector3 field {tag} must be finite.");
    }

    private static Vector4? ReadOptionalVector4(EbinRecordReadView record, uint tag)
    {
        if (!record.TryGetField(tag, out var field))
        {
            return null;
        }

        if (field.IsRequired || (field.Kind != 16) || (field.Count != 1) || (field.Payload.Length != 16))
        {
            throw new InvalidDataException($"Animation Clip optional Vector4 field {tag} has an invalid wire shape.");
        }

        var value = EbinPrimitives.Vector4(field.Payload);
        return value.IsFinite().All()
            ? value
            : throw new InvalidDataException($"Animation Clip optional Vector4 field {tag} must be finite.");
    }

    private static Quaternion ReadQuaternion(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 17);

        if (field.Payload.Length != 16)
        {
            throw new InvalidDataException($"Animation Clip field {tag} does not contain one Quaternion.");
        }

        return new Quaternion(
            EbinPrimitives.Single(field.Payload),
            EbinPrimitives.Single(field.Payload[4..]),
            EbinPrimitives.Single(field.Payload[8..]),
            EbinPrimitives.Single(field.Payload[12..]));
    }

    private static ImmutableArray<AnimationClipAsset.QuaternionKey> ReadQuaternionKeys(
        EbinRecordReadView record,
        EbinFieldReadView keyArray,
        byte interpolation,
        long durationTicks)
    {
        var keys = ImmutableArray.CreateBuilder<AnimationClipAsset.QuaternionKey>(checked((int)keyArray.Count));
        var previousTime = -1L;

        for (var index = 0; index < checked((int)keyArray.Count); index++)
        {
            var keyRecord = record.GetRecordArrayElement(5, index);
            ValidateHeader(keyRecord, s_keyQuaternionSchemaId, fieldCounts: [2, 3, 4]);
            ValidateKnownFieldSet(keyRecord, maximumTag: 4, requiredTags: [1, 2]);
            var timeTicks = ReadInt64(keyRecord, tag: 1);
            var value = ReadQuaternion(keyRecord, tag: 2);
            var inTangent = ReadOptionalVector4(keyRecord, tag: 3);
            var outTangent = ReadOptionalVector4(keyRecord, tag: 4);
            ValidateKeyTime(timeTicks, previousTime, durationTicks);

            var lengthSquared = Quaternion.Dot(value, value);

            if (!value.IsFinite().All() || !float.IsFinite(lengthSquared) || (lengthSquared <= 1.0e-12f))
            {
                throw new InvalidDataException("Animation Quaternion key values must have a finite nonzero norm.");
            }

            ValidateTangents(interpolation, inTangent.HasValue, outTangent.HasValue);

            if ((inTangent is { } inValue && !inValue.IsFinite().All())
                || (outTangent is { } outValue && !outValue.IsFinite().All()))
            {
                throw new InvalidDataException("Animation Quaternion tangents must be finite.");
            }

            keys.Add(new AnimationClipAsset.QuaternionKey(
                timeTicks,
                Quaternion.Normalize(value),
                inTangent,
                outTangent));
            previousTime = timeTicks;
        }

        return keys.MoveToImmutable();
    }

    private static AnimationClipAsset.RootMotionInfo ReadRootMotion(scoped ReadOnlySpan<byte> payload)
    {
        var record = new EbinRecordReadView(payload);
        ValidateHeader(record, s_rootMotionSchemaId, fieldCounts: [2]);
        var rootJointId = ReadGuid(record, tag: 1);
        var policy = ReadUInt8(record, tag: 2);

        if ((rootJointId == Guid.Empty) || (policy != 0))
        {
            throw new InvalidDataException("Animation RootMotion requires a nonempty JointId and the admitted policy value zero.");
        }

        return new AnimationClipAsset.RootMotionInfo(rootJointId, policy);
    }

    private static float ReadSingle(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 10);

        if (field.Payload.Length != sizeof(float))
        {
            throw new InvalidDataException($"Animation Clip field {tag} does not contain one binary32.");
        }

        var value = EbinPrimitives.Single(field.Payload);
        return float.IsFinite(value)
            ? value
            : throw new InvalidDataException($"Animation Clip field {tag} must be finite.");
    }

    private static AnimationClipAsset.AnimationTrack ReadSpriteTrack(
        EbinRecordReadView record,
        long durationTicks,
        AssetCodecContext context)
    {
        ValidateHeader(record, s_spriteTrackSchemaId, fieldCounts: [2]);
        var targetEntityId = ReadGuid(record, tag: 1);
        var keyArray = GetRequiredRecordArray(record, tag: 2, minimumCount: 1, maximumCount: 1_048_576);
        var keys = ImmutableArray.CreateBuilder<AnimationClipAsset.SpriteKey>(checked((int)keyArray.Count));
        var previousTime = -1L;

        if (targetEntityId == Guid.Empty)
        {
            throw new InvalidDataException("An Animation Sprite track target cannot be empty.");
        }

        for (var index = 0; index < checked((int)keyArray.Count); index++)
        {
            var keyRecord = record.GetRecordArrayElement(2, index);
            ValidateHeader(keyRecord, s_keySpriteSchemaId, fieldCounts: [3]);
            var timeTicks = ReadInt64(keyRecord, tag: 1);
            var atlasAssetId = ReadAssetReference(keyRecord, tag: 2, s_atlasSchemaId, expectedMajor: 1, context);
            var frameId = ReadGuid(keyRecord, tag: 3);

            if ((timeTicks < 0) || (timeTicks > durationTicks) || (timeTicks <= previousTime) || (frameId == Guid.Empty))
            {
                throw new InvalidDataException("Animation Sprite keys must have strictly ordered in-duration times and nonempty frame identities.");
            }

            keys.Add(new AnimationClipAsset.SpriteKey(timeTicks, atlasAssetId, frameId));
            previousTime = timeTicks;
        }

        return new AnimationClipAsset.AnimationTrack(
            targetEntityId,
            TargetDomainScene,
            ChannelSpriteFrame,
            InterpolationStep,
            [],
            [],
            keys.MoveToImmutable());
    }

    private static ImmutableArray<AnimationClipAsset.AnimationTrack> ReadTracks(
        EbinRecordReadView record,
        long durationTicks,
        AssetCodecContext context)
    {
        var trackArray = GetRequiredRecordArray(record, tag: 2, minimumCount: 0, maximumCount: 4096);
        var tracks = ImmutableArray.CreateBuilder<AnimationClipAsset.AnimationTrack>(checked((int)trackArray.Count));
        var previousDomain = -1;
        var previousTarget = Guid.Empty;
        var previousChannel = -1;

        for (var index = 0; index < checked((int)trackArray.Count); index++)
        {
            var trackRecord = record.GetRecordArrayElement(2, index);
            var header = trackRecord.Header;
            AnimationClipAsset.AnimationTrack track;

            if (header.SchemaId == s_transformTrackSchemaId)
            {
                track = ReadTransformTrack(trackRecord, durationTicks);
            }
            else if (header.SchemaId == s_spriteTrackSchemaId)
            {
                track = ReadSpriteTrack(trackRecord, durationTicks, context);
            }
            else
            {
                throw new NotSupportedException("Animation Clip contains an unsupported track schema.");
            }

            var targetComparison = ((int)track.TargetDomain).CompareTo(previousDomain);

            if ((targetComparison < 0)
                || ((targetComparison == 0)
                    && (index > 0)
                    && ((FoundationCatalogue.CompareRfc(previousTarget, track.TargetId) > 0)
                        || ((previousTarget == track.TargetId) && (track.Channel <= previousChannel)))))
            {
                throw new InvalidDataException("Animation Clip tracks are not uniquely sorted by domain, target RFC identity, and channel rank.");
            }

            tracks.Add(track);
            previousDomain = track.TargetDomain;
            previousTarget = track.TargetId;
            previousChannel = track.Channel;
        }

        return tracks.MoveToImmutable();
    }

    private static AnimationClipAsset.AnimationTrack ReadTransformTrack(
        EbinRecordReadView record,
        long durationTicks)
    {
        ValidateHeader(record, s_transformTrackSchemaId, fieldCounts: [5]);
        var targetId = ReadGuid(record, tag: 1);
        var targetDomain = ReadUInt8(record, tag: 2);
        var channel = ReadUInt8(record, tag: 3);
        var interpolation = ReadUInt8(record, tag: 4);
        var keyArray = GetRequiredRecordArray(record, tag: 5, minimumCount: 1, maximumCount: 1_048_576);

        if ((targetId == Guid.Empty)
            || (targetDomain > TargetDomainSkeleton)
            || (channel > ChannelScale)
            || (interpolation > InterpolationCubic))
        {
            throw new InvalidDataException("An Animation Transform track target or enum value is invalid.");
        }

        if (channel == ChannelRotation)
        {
            var keys = ReadQuaternionKeys(record, keyArray, interpolation, durationTicks);
            return new AnimationClipAsset.AnimationTrack(targetId, targetDomain, channel, interpolation, [], keys, []);
        }

        var vectorKeys = ReadVector3Keys(record, keyArray, channel, interpolation, durationTicks);
        return new AnimationClipAsset.AnimationTrack(targetId, targetDomain, channel, interpolation, vectorKeys, [], []);
    }

    private static uint ReadUInt32(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 3);

        if (field.Payload.Length != sizeof(uint))
        {
            throw new InvalidDataException($"Animation Clip field {tag} does not contain one UInt32.");
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(field.Payload);
    }

    private static byte ReadUInt8(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 1);

        if (field.Payload.Length != sizeof(byte))
        {
            throw new InvalidDataException($"Animation Clip field {tag} does not contain one UInt8.");
        }

        return field.Payload[0];
    }

    private static Vector3 ReadVector3(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 15);

        if (field.Payload.Length != 12)
        {
            throw new InvalidDataException($"Animation Clip field {tag} does not contain one Vector3.");
        }

        var value = EbinPrimitives.Vector3(field.Payload);
        return value.IsFinite().All()
            ? value
            : throw new InvalidDataException($"Animation Clip field {tag} must be finite.");
    }

    private static ImmutableArray<AnimationClipAsset.Vector3Key> ReadVector3Keys(
        EbinRecordReadView record,
        EbinFieldReadView keyArray,
        byte channel,
        byte interpolation,
        long durationTicks)
    {
        var keys = ImmutableArray.CreateBuilder<AnimationClipAsset.Vector3Key>(checked((int)keyArray.Count));
        var previousTime = -1L;

        for (var index = 0; index < checked((int)keyArray.Count); index++)
        {
            var keyRecord = record.GetRecordArrayElement(5, index);
            ValidateHeader(keyRecord, s_keyVector3SchemaId, fieldCounts: [2, 3, 4]);
            ValidateKnownFieldSet(keyRecord, maximumTag: 4, requiredTags: [1, 2]);
            var timeTicks = ReadInt64(keyRecord, tag: 1);
            var value = ReadVector3(keyRecord, tag: 2);
            var inTangent = ReadOptionalVector3(keyRecord, tag: 3);
            var outTangent = ReadOptionalVector3(keyRecord, tag: 4);
            ValidateKeyTime(timeTicks, previousTime, durationTicks);

            if (!value.IsFinite().All()
                || ((channel == ChannelScale) && !AnimationScaleRules.IsScaleValid(value)))
            {
                throw new InvalidDataException(channel == ChannelScale
                    ? "Animation Scale keys must be finite with component magnitudes in [0.0001,1000]."
                    : "Animation Position keys must be finite.");
            }

            ValidateTangents(interpolation, inTangent.HasValue, outTangent.HasValue);

            if ((inTangent is { } inValue && !inValue.IsFinite().All())
                || (outTangent is { } outValue && !outValue.IsFinite().All()))
            {
                throw new InvalidDataException("Animation Vector3 tangents must be finite.");
            }

            keys.Add(new AnimationClipAsset.Vector3Key(timeTicks, value, inTangent, outTangent));
            previousTime = timeTicks;
        }

        return keys.MoveToImmutable();
    }

    private static Vector4 ReadVector4(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 16);

        if (field.Payload.Length != 16)
        {
            throw new InvalidDataException($"Animation Clip field {tag} does not contain one Vector4.");
        }

        var value = EbinPrimitives.Vector4(field.Payload);
        return value.IsFinite().All()
            ? value
            : throw new InvalidDataException($"Animation Clip field {tag} must be finite.");
    }

    private static BigInteger ToScaledInteger(float value)
    {
        var bits = unchecked((uint)BitConverter.SingleToInt32Bits(value));
        var exponent = checked((int)((bits >> 23) & 0xFF));
        var fraction = bits & 0x007F_FFFF;

        if (exponent == 0xFF)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Exact bounds inclusion requires finite binary32 values.");
        }

        var significand = (exponent == 0) ? fraction : (fraction | 0x0080_0000);
        var integer = new BigInteger(significand);

        if (exponent > 1)
        {
            integer <<= exponent - 1;
        }

        return ((bits & 0x8000_0000) == 0) ? integer : -integer;
    }

    private static ContentSchemaReference ValidateEventPayload(
        EbinRecordReadView payload,
        AssetCodecContext context)
    {
        var header = payload.Header;
        var schemaReference = new ContentSchemaReference(
            header.SchemaId,
            header.Major,
            header.Minor,
            ContentSchemaScope.Content);

        if (!context.Schemas.TryGetSchema(schemaReference, out var schema)
            || (schema.Role != ContentSchemaRole.EventPayload))
        {
            throw new InvalidDataException("An Animation Clip event payload must select an exact compiled EventPayload schema.");
        }

        if (header.SchemaId == s_eventMarkerSchemaId)
        {
            ValidateHeader(payload, s_eventMarkerSchemaId, fieldCounts: [1]);
            var nameField = GetRequiredField(payload, tag: 1, kind: 12);

            if ((nameField.Payload.Length is < 1 or > 128) || (nameField.Count != (ulong)nameField.Payload.Length))
            {
                throw new InvalidDataException("An Animation event marker name must contain 1 through 128 UTF-8 bytes.");
            }
        }

        return schemaReference;
    }

    private static void ValidateHeader(
        EbinRecordReadView record,
        Guid schemaId,
        ReadOnlySpan<int> fieldCounts)
    {
        var header = record.Header;

        if ((header.SchemaId != schemaId) || (header.Major != 1) || (header.Minor != 0))
        {
            throw new NotSupportedException("The selected Animation Clip nested schema version is unsupported.");
        }

        foreach (var fieldCount in fieldCounts)
        {
            if (header.FieldCount == fieldCount)
            {
                return;
            }
        }

        throw new InvalidDataException("An Animation Clip nested record has an invalid field count.");
    }

    private static void ValidateKeyTime(long timeTicks, long previousTime, long durationTicks)
    {
        if ((timeTicks < 0) || (timeTicks > durationTicks) || (timeTicks <= previousTime))
        {
            throw new InvalidDataException("Animation key times must be strictly increasing within the closed clip duration.");
        }
    }

    private static void ValidateKnownFieldSet(
        EbinRecordReadView record,
        uint maximumTag,
        ReadOnlySpan<uint> requiredTags)
    {
        var seen = 0UL;

        for (var index = 0; index < record.FieldCount; index++)
        {
            var tag = record.GetField(index).Tag;

            if ((tag == 0) || (tag > maximumTag))
            {
                throw new InvalidDataException("An Animation Clip record contains an unknown field tag.");
            }

            seen |= 1UL << checked((int)tag - 1);
        }

        foreach (var tag in requiredTags)
        {
            if ((seen & (1UL << checked((int)tag - 1))) == 0)
            {
                throw new InvalidDataException($"Animation Clip required field {tag} is absent.");
            }
        }
    }

    private static void ValidateSphereContainsBounds(
        Vector3 minimum,
        Vector3 maximum,
        Vector3 center,
        float radius)
    {
        var radiusScaled = ToScaledInteger(radius);
        var radiusSquared = radiusScaled * radiusScaled;

        for (var corner = 0; corner < 8; corner++)
        {
            var x = ((corner & 1) == 0) ? minimum.X : maximum.X;
            var y = ((corner & 2) == 0) ? minimum.Y : maximum.Y;
            var z = ((corner & 4) == 0) ? minimum.Z : maximum.Z;
            var dx = ToScaledInteger(x) - ToScaledInteger(center.X);
            var dy = ToScaledInteger(y) - ToScaledInteger(center.Y);
            var dz = ToScaledInteger(z) - ToScaledInteger(center.Z);

            if (((dx * dx) + (dy * dy) + (dz * dz)) > radiusSquared)
            {
                throw new InvalidDataException("The Animation Clip bounding sphere does not enclose its axis-aligned bounds.");
            }
        }
    }

    private static void ValidateTangents(byte interpolation, bool hasInTangent, bool hasOutTangent)
    {
        var cubic = interpolation == InterpolationCubic;

        if ((cubic != (hasInTangent && hasOutTangent)) || (hasInTangent != hasOutTangent))
        {
            throw new InvalidDataException("Cubic Animation keys require both tangents; Step and Linear keys forbid tangents.");
        }
    }
}
