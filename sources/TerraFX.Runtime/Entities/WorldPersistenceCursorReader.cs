// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceCursorReader
{
    private const ushort ArrayKind = 24;
    private const int CursorFieldCountWithoutStrictLocal = 4;
    private const ushort Int64Kind = 8;
    private const int MaximumFieldCount = 7;

    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("state/cursors").SchemaId;

    internal static WorldPersistenceCursorContents Read(
        ReadOnlyMemory<byte> cursorsRecord,
        EbinReadLimits limits,
        int controlRecordCount,
        WorldPersistenceClockContents clock)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfNegative(controlRecordCount);

        var status = EbinStructuralReader.Validate(
            cursorsRecord.Span,
            limits,
            EbinReadPolicy.Canonical,
            out var bytesConsumed,
            out _);

        if ((status is not EbinReadStatus.Complete)
            || (bytesConsumed != cursorsRecord.Length))
        {
            throw new ArgumentException("The state/cursors record is not a complete canonical EBIN record.", nameof(cursorsRecord));
        }

        var record = new EbinRecordReadView(cursorsRecord.Span);
        var hasNextOwnerRequestSequence = record.TryGetField(2, out var nextOwnerRequestSequenceField);
        var hasNextBarrierEpoch = record.TryGetField(3, out var nextBarrierEpochField);
        var hasAcceptedOwnerRequests = record.TryGetField(7, out var acceptedOwnerRequestsField);
        var hasStrictLocalCursors = hasNextOwnerRequestSequence && hasNextBarrierEpoch && hasAcceptedOwnerRequests;

        if ((record.Header.SchemaId != s_schemaId)
            || (record.Header.Major != 1)
            || (record.Header.Minor != 0)
            || (record.FieldCount is not CursorFieldCountWithoutStrictLocal and not MaximumFieldCount)
            || (hasNextOwnerRequestSequence != hasNextBarrierEpoch)
            || (hasNextOwnerRequestSequence != hasAcceptedOwnerRequests))
        {
            throw new ArgumentException("The state/cursors record has the wrong schema identity, version, or optional cursor group.", nameof(cursorsRecord));
        }

        var nextControlSequence = ReadRequiredInt64(record, 1, nameof(cursorsRecord));
        long? nextOwnerRequestSequence = hasStrictLocalCursors
            ? ReadOptionalInt64(nextOwnerRequestSequenceField, nameof(cursorsRecord))
            : null;
        long? nextBarrierEpoch = hasStrictLocalCursors
            ? ReadOptionalInt64(nextBarrierEpochField, nameof(cursorsRecord))
            : null;
        var pendingCommandCount = ReadEmptyArrayCount(record, 4, nameof(cursorsRecord));
        var pendingOwnerRequestCount = ReadEmptyArrayCount(record, 5, nameof(cursorsRecord));
        var pendingWorldEventCount = ReadEmptyArrayCount(record, 6, nameof(cursorsRecord));
        long? acceptedOwnerRequests = hasStrictLocalCursors
            ? ReadOptionalInt64(acceptedOwnerRequestsField, nameof(cursorsRecord))
            : null;

        if (nextControlSequence != checked((long)controlRecordCount + 1))
        {
            throw new ArgumentException("The state/cursors next-control sequence does not follow the saved controls.", nameof(cursorsRecord));
        }

        if (hasStrictLocalCursors)
        {
            if ((nextOwnerRequestSequence is not { } nextOwnerRequest)
                || (nextBarrierEpoch is not { } nextBarrier)
                || (acceptedOwnerRequests is not { } acceptedRequests)
                || (acceptedRequests < 0)
                || (acceptedRequests == long.MaxValue)
                || (nextOwnerRequest != checked(acceptedRequests + 1))
                || (nextBarrier != checked(clock.NextTick + 1)))
            {
                throw new ArgumentException("The state/cursors StrictLocal values are inconsistent with owner admissions or the saved clock.", nameof(cursorsRecord));
            }
        }

        return new WorldPersistenceCursorContents(
            nextControlSequence,
            nextOwnerRequestSequence,
            nextBarrierEpoch,
            acceptedOwnerRequests,
            pendingCommandCount,
            pendingOwnerRequestCount,
            pendingWorldEventCount);
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
            throw new ArgumentException("A state/cursors field has an invalid wire shape.", parameterName);
        }

        return field;
    }

    private static int ReadEmptyArrayCount(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, ArrayKind, expectedCount: 0, parameterName);

        if ((field.Payload.Length != 16)
            || (BinaryPrimitives.ReadUInt64LittleEndian(field.Payload) != 0)
            || (BinaryPrimitives.ReadUInt64LittleEndian(field.Payload[8..]) != 16))
        {
            throw new ArgumentException("A state/cursors pending-work array is not the canonical empty form.", parameterName);
        }

        return checked((int)field.Count);
    }

    private static long ReadOptionalInt64(EbinFieldReadView field, string parameterName)
    {
        if ((field.Kind != Int64Kind)
            || field.IsRequired
            || (field.Count != 1)
            || (field.Payload.Length != sizeof(long)))
        {
            throw new ArgumentException("An optional state/cursors Int64 field has an invalid wire shape.", parameterName);
        }

        return BinaryPrimitives.ReadInt64LittleEndian(field.Payload);
    }

    private static long ReadRequiredInt64(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, Int64Kind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(long))
        {
            throw new ArgumentException("A state/cursors Int64 field has an invalid payload length.", parameterName);
        }

        var value = BinaryPrimitives.ReadInt64LittleEndian(field.Payload);

        if (value < 1)
        {
            throw new ArgumentException("The state/cursors next-control sequence is outside its supported range.", parameterName);
        }

        return value;
    }
}
