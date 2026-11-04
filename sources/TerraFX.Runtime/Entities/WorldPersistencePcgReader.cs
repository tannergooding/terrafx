// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistencePcgReader
{
    private const ushort GuidKind = 11;
    private const int MaximumStreamCount = 65_536;
    private const int RecordFieldCount = 3;
    private const ushort UInt64Kind = 4;

    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("state/pcg").SchemaId;

    internal static void Validate(
        ReadOnlyMemory<byte> worldRecord,
        uint fieldTag,
        int randomStreamCount,
        string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(randomStreamCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(randomStreamCount, MaximumStreamCount);

        var record = new EbinRecordReadView(worldRecord.Span);
        var previousStreamId = Guid.Empty;

        for (var index = 0; index < randomStreamCount; index++)
        {
            var pcgRecord = record.GetRecordArrayElement(fieldTag, index);

            if ((pcgRecord.Header.SchemaId != s_schemaId)
                || (pcgRecord.Header.Major != 1)
                || (pcgRecord.Header.Minor != 0)
                || (pcgRecord.FieldCount != RecordFieldCount))
            {
                throw new ArgumentException("A state/pcg stream has the wrong schema identity, version, or field count.", parameterName);
            }

            var streamId = ReadGuid(pcgRecord, 1, parameterName);
            var increment = ReadUInt64(pcgRecord, 3, parameterName);

            if (((index > 0) && (FoundationCatalogue.CompareRfc(previousStreamId, streamId) >= 0))
                || ((increment & 1) == 0))
            {
                throw new ArgumentException("The state/pcg stream IDs are not unique RFC order or its increment is not odd.", parameterName);
            }

            _ = ReadUInt64(pcgRecord, 2, parameterName);
            previousStreamId = streamId;
        }
    }

    private static EbinFieldReadView GetRequiredField(
        EbinRecordReadView record,
        uint tag,
        ushort kind,
        string parameterName)
    {
        if (!record.TryGetField(tag, out var field)
            || (field.Kind != kind)
            || !field.IsRequired
            || (field.Count != 1))
        {
            throw new ArgumentException("A state/pcg field has an invalid wire shape.", parameterName);
        }

        return field;
    }

    private static Guid ReadGuid(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, GuidKind, parameterName);

        if (field.Payload.Length != 16)
        {
            throw new ArgumentException("A state/pcg StreamId has an invalid payload length.", parameterName);
        }

        var streamId = new Guid(field.Payload, bigEndian: true);

        if (streamId == Guid.Empty)
        {
            throw new ArgumentException("A state/pcg StreamId cannot be empty.", parameterName);
        }

        return streamId;
    }

    private static ulong ReadUInt64(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, UInt64Kind, parameterName);

        if (field.Payload.Length != sizeof(ulong))
        {
            throw new ArgumentException("A state/pcg UInt64 field has an invalid payload length.", parameterName);
        }

        return BinaryPrimitives.ReadUInt64LittleEndian(field.Payload);
    }
}
