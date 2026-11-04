// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceClockReader
{
    private const ushort ArrayKind = 24;
    private const ushort BooleanKind = 9;
    private const int ClockFieldCountWithoutFrame2 = 9;
    private const ushort FloatKind = 10;
    private const ushort Int64Kind = 8;
    private const int MaximumTimeSegmentCount = 65_537;
    private const ushort RecordKind = 22;
    private const int TimeSegmentFieldCount = 3;
    private const ushort UInt32Kind = 3;

    private static readonly Guid s_clockSchemaId = new ContentSchemaIdentityAssignment("state/clock").SchemaId;
    private static readonly Guid s_frame2SchemaId = new ContentSchemaIdentityAssignment("foundation/frame2").SchemaId;
    private static readonly Guid s_frame3SchemaId = new ContentSchemaIdentityAssignment("foundation/frame3").SchemaId;
    private static readonly Guid s_timeSegmentSchemaId = new ContentSchemaIdentityAssignment("state/time-segment").SchemaId;

    internal static WorldPersistenceClockContents Read(
        ReadOnlyMemory<byte> clockRecord,
        EbinReadLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        var status = EbinStructuralReader.Validate(
            clockRecord.Span,
            limits,
            EbinReadPolicy.Canonical,
            out var bytesConsumed,
            out _);

        if ((status is not EbinReadStatus.Complete)
            || (bytesConsumed != clockRecord.Length))
        {
            throw new ArgumentException("The state/clock record is not a complete canonical EBIN record.", nameof(clockRecord));
        }

        var record = new EbinRecordReadView(clockRecord.Span);
        var hasInitialFrame2 = record.TryGetField(8, out var initialFrame2Field);

        if ((record.Header.SchemaId != s_clockSchemaId)
            || (record.Header.Major != 1)
            || (record.Header.Minor != 0)
            || (record.FieldCount != (hasInitialFrame2 ? ClockFieldCountWithoutFrame2 + 1 : ClockFieldCountWithoutFrame2)))
        {
            throw new ArgumentException("The state/clock record has the wrong schema identity, version, or field count.", nameof(clockRecord));
        }

        var nextTick = ReadInt64(record, 1, nameof(clockRecord));
        var tickRate = ReadBoundedUInt32(record, 2, 1000, nameof(clockRecord));
        var timeScale = ReadFloat(record, 3, nameof(clockRecord));
        var nextPublicationSequence = ReadInt64(record, 4, nameof(clockRecord));
        var segmentsField = GetRequiredField(record, 5, ArrayKind, expectedCount: null, nameof(clockRecord));

        if (segmentsField.Count is < 1 or > MaximumTimeSegmentCount)
        {
            throw new ArgumentException("The state/clock segment array is outside its supported count.", nameof(clockRecord));
        }

        var segmentCount = checked((int)segmentsField.Count);
        var replayComplete = ReadBoolean(record, 6, nameof(clockRecord));
        var currentSegmentSequence = ReadInt64(record, 7, nameof(clockRecord));

        if (hasInitialFrame2)
        {
            ValidateOptionalFrameField(initialFrame2Field, s_frame2SchemaId, nameof(clockRecord));
        }

        var initialFrame2Record = hasInitialFrame2 ? GetPayload(clockRecord, fieldIndex: 7) : ReadOnlyMemory<byte>.Empty;
        var initialFrame3FieldIndex = hasInitialFrame2 ? 8 : 7;
        var initialFrame3Field = GetRequiredField(record, 9, RecordKind, expectedCount: 1, nameof(clockRecord));
        ValidateFrameRecord(initialFrame3Field, s_frame3SchemaId, nameof(clockRecord));
        var initialFrame3Record = GetPayload(clockRecord, initialFrame3FieldIndex);
        var historyBaseNextTick = ReadInt64(record, 10, nameof(clockRecord));

        if ((nextTick < 1)
            || (nextTick == long.MaxValue)
            || (nextPublicationSequence != checked(nextTick + 1))
            || !IsValidTimeScale(timeScale)
            || replayComplete
            || (currentSegmentSequence != segmentCount)
            || (historyBaseNextTick != 0))
        {
            throw new ArgumentException("The state/clock record has inconsistent tick, segment, or replay metadata.", nameof(clockRecord));
        }

        var previousStartNextTick = 0L;
        var finalTimeScale = 0.0f;

        for (var index = 0; index < segmentCount; index++)
        {
            var segment = record.GetRecordArrayElement(5, index);
            var expectedSequence = checked((long)index + 1);
            ReadTimeSegment(
                segment,
                expectedSequence,
                nextTick,
                previousStartNextTick,
                index == 0,
                out var segmentStartNextTick,
                out var segmentTimeScale,
                nameof(clockRecord));
            previousStartNextTick = segmentStartNextTick;
            finalTimeScale = segmentTimeScale;
        }

        if (BitConverter.SingleToUInt32Bits(finalTimeScale) != BitConverter.SingleToUInt32Bits(timeScale))
        {
            throw new ArgumentException("The state/clock time scale does not match the final time segment.", nameof(clockRecord));
        }

        return new WorldPersistenceClockContents(
            nextTick,
            tickRate,
            timeScale,
            nextPublicationSequence,
            segmentCount,
            currentSegmentSequence,
            hasInitialFrame2,
            initialFrame2Record,
            initialFrame3Record,
            historyBaseNextTick);
    }

    private static ReadOnlyMemory<byte> GetPayload(ReadOnlyMemory<byte> record, int fieldIndex)
    {
        var entryOffset = checked(64 + (fieldIndex * 32));
        var entry = record.Span.Slice(entryOffset, 32);
        var payloadOffset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(entry[8..]));
        var payloadLength = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(entry[16..]));
        return record.Slice(payloadOffset, payloadLength);
    }

    private static EbinFieldReadView GetRequiredField(
        EbinRecordReadView record,
        uint tag,
        ushort kind,
        ulong? expectedCount,
        string parameterName)
    {
        if (!record.TryGetField(tag, out var field)
            || (field.Kind != kind)
            || !field.IsRequired
            || (expectedCount.HasValue && (field.Count != expectedCount.Value)))
        {
            throw new ArgumentException("A state/clock field has an invalid wire shape.", parameterName);
        }

        return field;
    }

    private static bool IsValidTimeScale(float value)
        => float.IsFinite(value) && (value >= 0.0f) && (value <= 4.0f);

    private static bool ReadBoolean(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, BooleanKind, expectedCount: 1, parameterName);

        if ((field.Payload.Length != sizeof(byte)) || (field.Payload[0] > 1))
        {
            throw new ArgumentException("A state/clock Boolean field has an invalid payload.", parameterName);
        }

        return field.Payload[0] != 0;
    }

    private static int ReadBoundedUInt32(
        EbinRecordReadView record,
        uint tag,
        uint maximum,
        string parameterName)
    {
        var value = ReadUInt32(record, tag, parameterName);

        if ((value == 0) || (value > maximum))
        {
            throw new ArgumentException("The state/clock tick rate is outside its supported range.", parameterName);
        }

        return checked((int)value);
    }

    private static float ReadFloat(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, FloatKind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(float))
        {
            throw new ArgumentException("A state/clock binary32 field has an invalid payload length.", parameterName);
        }

        var value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(field.Payload));

        if (!IsValidTimeScale(value))
        {
            throw new ArgumentException("A state/clock time scale is outside its supported range.", parameterName);
        }

        return value;
    }

    private static long ReadInt64(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, Int64Kind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(long))
        {
            throw new ArgumentException("A state/clock Int64 field has an invalid payload length.", parameterName);
        }

        return BinaryPrimitives.ReadInt64LittleEndian(field.Payload);
    }

    private static void ReadTimeSegment(
        EbinRecordReadView record,
        long expectedSequence,
        long nextTick,
        long previousStartNextTick,
        bool isInitialSegment,
        out long startNextTick,
        out float timeScale,
        string parameterName)
    {
        if ((record.Header.SchemaId != s_timeSegmentSchemaId)
            || (record.Header.Major != 1)
            || (record.Header.Minor != 0)
            || (record.FieldCount != TimeSegmentFieldCount))
        {
            throw new ArgumentException("A state/time-segment record has the wrong schema identity, version, or field count.", parameterName);
        }

        var sequence = ReadInt64(record, 1, parameterName);
        startNextTick = ReadInt64(record, 2, parameterName);
        timeScale = ReadFloat(record, 3, parameterName);

        if ((sequence != expectedSequence)
            || (startNextTick < 0)
            || (startNextTick >= nextTick)
            || (isInitialSegment && (startNextTick != 0))
            || (!isInitialSegment && (startNextTick < previousStartNextTick)))
        {
            throw new ArgumentException("A state/time-segment record has inconsistent sequence or tick ordering.", parameterName);
        }
    }

    private static uint ReadUInt32(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, UInt32Kind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(uint))
        {
            throw new ArgumentException("A state/clock UInt32 field has an invalid payload length.", parameterName);
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(field.Payload);
    }

    private static void ValidateFrameRecord(
        EbinFieldReadView field,
        Guid schemaId,
        string parameterName)
    {
        var record = new EbinRecordReadView(field.Payload);

        if ((record.Header.SchemaId != schemaId)
            || (record.Header.Major != 1)
            || (record.Header.Minor != 0))
        {
            throw new ArgumentException("A state/clock initial frame has the wrong schema identity or version.", parameterName);
        }
    }

    private static void ValidateOptionalFrameField(
        EbinFieldReadView field,
        Guid schemaId,
        string parameterName)
    {
        if ((field.Kind != RecordKind)
            || field.IsRequired
            || (field.Count != 1))
        {
            throw new ArgumentException("The optional state/clock Frame2 field has an invalid wire shape.", parameterName);
        }

        ValidateFrameRecord(field, schemaId, parameterName);
    }
}
