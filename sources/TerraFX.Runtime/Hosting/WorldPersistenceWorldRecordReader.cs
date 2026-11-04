// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX;
using TerraFX.Entities;
using TerraFX.Scenes;

namespace TerraFX.Hosting;

internal static class WorldPersistenceWorldRecordReader
{
    private const ushort ArrayKind = 24;
    private const ushort FixedArrayKind = 23;
    private const ushort RecordKind = 22;
    private const int RequiredFieldCount = 10;

    private static readonly Guid s_clockSchemaId = new ContentSchemaIdentityAssignment("state/clock").SchemaId;
    private static readonly Guid s_configurationSchemaId = new ContentSchemaIdentityAssignment("state/configuration").SchemaId;
    private static readonly Guid s_controlSchemaId = new ContentSchemaIdentityAssignment("state/control").SchemaId;
    private static readonly Guid s_cursorsSchemaId = new ContentSchemaIdentityAssignment("state/cursors").SchemaId;
    private static readonly Guid s_ecsSchemaId = new ContentSchemaIdentityAssignment("state/ecs").SchemaId;
    private static readonly Guid s_frame2SchemaId = new ContentSchemaIdentityAssignment("foundation/frame2").SchemaId;
    private static readonly Guid s_frame3SchemaId = new ContentSchemaIdentityAssignment("foundation/frame3").SchemaId;
    private static readonly Guid s_pcgSchemaId = new ContentSchemaIdentityAssignment("state/pcg").SchemaId;
    private static readonly Guid s_registrationSchemaId = new ContentSchemaIdentityAssignment("state/registration").SchemaId;
    private static readonly Guid s_scenesSchemaId = new ContentSchemaIdentityAssignment("state/scenes").SchemaId;
    private static readonly Guid s_strictLayoutSchemaId = new ContentSchemaIdentityAssignment("state/strict-layout").SchemaId;
    private static readonly Guid s_worldSchemaId = new ContentSchemaIdentityAssignment("state/world").SchemaId;

    internal static WorldPersistenceWorldRecordContents Read(
        ReadOnlyMemory<byte> worldRecord,
        EbinReadLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        var status = EbinStructuralReader.Validate(
            worldRecord.Span,
            limits,
            EbinReadPolicy.Canonical,
            out var bytesConsumed,
            out _);

        if ((status is not EbinReadStatus.Complete)
            || (bytesConsumed != worldRecord.Length))
        {
            throw new ArgumentException("The state/world record is not a complete canonical EBIN record.", nameof(worldRecord));
        }

        var record = new EbinRecordReadView(worldRecord.Span);

        if ((record.Header.SchemaId != s_worldSchemaId)
            || (record.Header.Major != 1)
            || (record.Header.Minor != 0)
            || (record.FieldCount is < RequiredFieldCount or > 12))
        {
            throw new ArgumentException("The state/world record has the wrong schema identity, version, or field count.", nameof(worldRecord));
        }

        ReadOnlyMemory<byte> configurationRecord = default;
        ReadOnlyMemory<byte> clockRecord = default;
        ReadOnlyMemory<byte> frame2Record = default;
        ReadOnlyMemory<byte> frame3Record = default;
        ReadOnlyMemory<byte> ecsRecord = default;
        ReadOnlyMemory<byte> scenesRecord = default;
        ReadOnlyMemory<byte> assetIdsPayload = default;
        var assetIdCount = 0;
        ReadOnlyMemory<byte> controlRecordsPayload = default;
        var controlRecordCount = 0;
        ReadOnlyMemory<byte> cursorsRecord = default;
        ReadOnlyMemory<byte> randomStreamsPayload = default;
        var randomStreamCount = 0;
        ReadOnlyMemory<byte> strictLayoutRecord = default;
        ReadOnlyMemory<byte> registrationRecord = default;
        var seenFields = 0U;

        for (var index = 0; index < record.FieldCount; index++)
        {
            var field = record.GetField(index);

            if ((field.Tag is < 1 or > 12) || ((seenFields & (1U << checked((int)field.Tag))) != 0))
            {
                throw new ArgumentException("The state/world record contains an unknown or duplicate field.", nameof(worldRecord));
            }

            seenFields |= 1U << checked((int)field.Tag);
            var payload = GetPayload(worldRecord, index);

            switch (field.Tag)
            {
                case 1:
                {
                    configurationRecord = ValidateRecordField(
                                        field,
                                        payload,
                                        isRequired: true,
                                        s_configurationSchemaId,
                                        limits,
                                        nameof(worldRecord));
                    break;
                }

                case 2:
                {
                    clockRecord = ValidateRecordField(field, payload, isRequired: true, s_clockSchemaId, limits, nameof(worldRecord));
                    break;
                }

                case 3:
                {
                    frame2Record = ValidateRecordField(field, payload, isRequired: false, s_frame2SchemaId, limits, nameof(worldRecord));
                    break;
                }

                case 4:
                {
                    frame3Record = ValidateRecordField(field, payload, isRequired: true, s_frame3SchemaId, limits, nameof(worldRecord));
                    break;
                }

                case 5:
                {
                    ecsRecord = ValidateRecordField(field, payload, isRequired: true, s_ecsSchemaId, limits, nameof(worldRecord));
                    break;
                }

                case 6:
                {
                    scenesRecord = ValidateRecordField(field, payload, isRequired: true, s_scenesSchemaId, limits, nameof(worldRecord));
                    break;
                }

                case 7:
                {
                    ValidateField(field, FixedArrayKind, isRequired: true, expectedCount: null, nameof(worldRecord));
                    ValidateAssetIdsPayload(payload.Span, field.Count, nameof(worldRecord));
                    assetIdsPayload = payload;
                    assetIdCount = checked((int)field.Count);
                    break;
                }

                case 8:
                {
                    ValidateField(field, ArrayKind, isRequired: true, expectedCount: null, nameof(worldRecord));
                    ValidateRecordArray(record, field.Tag, field.Count, s_controlSchemaId, limits, nameof(worldRecord));
                    controlRecordsPayload = payload;
                    controlRecordCount = checked((int)field.Count);
                    break;
                }

                case 9:
                {
                    cursorsRecord = ValidateRecordField(field, payload, isRequired: true, s_cursorsSchemaId, limits, nameof(worldRecord));
                    break;
                }

                case 10:
                {
                    ValidateField(field, ArrayKind, isRequired: true, expectedCount: null, nameof(worldRecord));
                    ValidateRecordArray(record, field.Tag, field.Count, s_pcgSchemaId, limits, nameof(worldRecord));
                    randomStreamsPayload = payload;
                    randomStreamCount = checked((int)field.Count);
                    break;
                }

                case 11:
                {
                    strictLayoutRecord = ValidateRecordField(
                                        field,
                                        payload,
                                        isRequired: false,
                                        s_strictLayoutSchemaId,
                                        limits,
                                        nameof(worldRecord));
                    break;
                }

                case 12:
                {
                    registrationRecord = ValidateRecordField(
                                        field,
                                        payload,
                                        isRequired: true,
                                        s_registrationSchemaId,
                                        limits,
                                        nameof(worldRecord));
                    break;
                }
            }
        }

        const uint RequiredFieldMask =
            (1U << 1)
            | (1U << 2)
            | (1U << 4)
            | (1U << 5)
            | (1U << 6)
            | (1U << 7)
            | (1U << 8)
            | (1U << 9)
            | (1U << 10)
            | (1U << 12);

        if ((seenFields & RequiredFieldMask) != RequiredFieldMask)
        {
            throw new ArgumentException("The state/world record omitted a required field.", nameof(worldRecord));
        }

        var parsedConfiguration = WorldPersistenceConfigurationReader.Read(configurationRecord, limits);
        var parsedClock = WorldPersistenceClockReader.Read(clockRecord, limits);

        if ((parsedClock.HasInitialFrame2 != !frame2Record.IsEmpty)
            || (controlRecordCount > parsedConfiguration.MaximumRecordedControls)
            || (parsedClock.SegmentCount > (parsedConfiguration.MaximumRecordedControls + 1)))
        {
            throw new ArgumentException("The state/world clock, controls, or initial-frame presence does not match the saved configuration.", nameof(worldRecord));
        }

        WorldPersistenceControlRecordReader.Validate(
            record,
            fieldTag: 8,
            controlRecordCount,
            record.GetRecord(2),
            parsedClock,
            nameof(worldRecord));
        var parsedCursors = WorldPersistenceCursorReader.Read(
            cursorsRecord,
            limits,
            controlRecordCount,
            parsedClock);
        var parsedRegistration = WorldPersistenceRegistrationReader.Read(
            registrationRecord,
            limits,
            parsedConfiguration.MaximumComponentTypes);
        var parsedEcs = WorldPersistenceEcsReader.Read(ecsRecord, limits, parsedConfiguration);
        var parsedScenes = WorldPersistenceScenesReader.Read(
            scenesRecord,
            limits,
            parsedConfiguration.MaximumActiveSceneInstances,
            parsedConfiguration.MaximumLiveEntities,
            parsedConfiguration.MaximumIdentityAssignments,
            parsedConfiguration.MaximumSceneOccurrences);

        if (parsedScenes.HierarchyNodeCount > parsedEcs.EntityCount)
        {
            throw new ArgumentException("The state/scenes hierarchy contains more nodes than live World entities.", nameof(worldRecord));
        }

        return new WorldPersistenceWorldRecordContents(
            parsedConfiguration,
            parsedClock,
            parsedCursors,
            parsedRegistration,
            parsedEcs,
            parsedScenes,
            configurationRecord,
            clockRecord,
            frame2Record,
            frame3Record,
            ecsRecord,
            scenesRecord,
            assetIdsPayload,
            assetIdCount,
            controlRecordsPayload,
            controlRecordCount,
            cursorsRecord,
            randomStreamsPayload,
            randomStreamCount,
            strictLayoutRecord,
            registrationRecord);
    }

    private static ReadOnlyMemory<byte> GetPayload(ReadOnlyMemory<byte> record, int fieldIndex)
    {
        var entryOffset = checked(64 + (fieldIndex * 32));
        var entry = record.Span.Slice(entryOffset, 32);
        var payloadOffset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(entry[8..]));
        var payloadLength = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(entry[16..]));
        return record.Slice(payloadOffset, payloadLength);
    }

    private static void ValidateAssetIdsPayload(
        scoped ReadOnlySpan<byte> payload,
        ulong count,
        string parameterName)
    {
        var expectedLength = checked(32UL + (count * sizeof(uint)));

        if (((ulong)payload.Length != expectedLength)
            || (BinaryPrimitives.ReadUInt16LittleEndian(payload[16..]) != 3)
            || (BinaryPrimitives.ReadUInt32LittleEndian(payload[20..]) != sizeof(uint)))
        {
            throw new ArgumentException("The state/world AssetIds field has an invalid fixed-array payload.", parameterName);
        }

        var encodedIds = payload[32..];
        var previousId = 0U;

        for (var index = 0UL; index < count; index++)
        {
            var offset = checked((int)(index * sizeof(uint)));
            var assetId = BinaryPrimitives.ReadUInt32LittleEndian(encodedIds[offset..]);

            if ((assetId == 0) || (assetId <= previousId))
            {
                throw new ArgumentException("The state/world AssetIds field is not a sorted unique positive set.", parameterName);
            }

            previousId = assetId;
        }
    }

    private static void ValidateField(
        EbinFieldReadView field,
        ushort kind,
        bool isRequired,
        ulong? expectedCount,
        string parameterName)
    {
        if ((field.Kind != kind)
            || (field.IsRequired != isRequired)
            || (expectedCount.HasValue && (field.Count != expectedCount.Value)))
        {
            throw new ArgumentException("A state/world field has an invalid wire shape.", parameterName);
        }
    }

    private static void ValidateRecord(
        scoped ReadOnlySpan<byte> record,
        Guid schemaId,
        EbinReadLimits limits,
        string parameterName)
    {
        var status = EbinStructuralReader.ReadHeader(record, limits, out var header, out _, out _);

        if ((status is not EbinReadStatus.Complete)
            || (header.SchemaId != schemaId)
            || (header.Major != 1)
            || (header.Minor != 0))
        {
            throw new ArgumentException("A state/world child record has the wrong schema identity or version.", parameterName);
        }
    }

    private static void ValidateRecordArray(
        EbinRecordReadView worldRecord,
        uint fieldTag,
        ulong count,
        Guid schemaId,
        EbinReadLimits limits,
        string parameterName)
    {
        if (count > int.MaxValue)
        {
            throw new ArgumentException("A state/world record array exceeds the supported count.", parameterName);
        }

        for (var index = 0; index < (int)count; index++)
        {
            var element = worldRecord.GetRecordArrayElement(fieldTag, index);
            ValidateRecord(element.EncodedBytes, schemaId, limits, parameterName);
        }
    }

    private static ReadOnlyMemory<byte> ValidateRecordField(
        EbinFieldReadView field,
        ReadOnlyMemory<byte> payload,
        bool isRequired,
        Guid schemaId,
        EbinReadLimits limits,
        string parameterName)
    {
        ValidateField(field, RecordKind, isRequired, expectedCount: 1, parameterName);
        ValidateRecord(payload.Span, schemaId, limits, parameterName);
        return payload;
    }
}
