// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistencePcgReaderTests
{
    private static byte[] CreatePcgRecord(Guid streamId, ulong state, ulong increment)
    {
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[3];
        Span<byte> payloadStorage = stackalloc byte[32];
        var status = EbinPayloadWriter.Write(streamId, payloadStorage, out var guidBytes, out var diagnostic);
        Assert.That(status, Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.ToString());
        fields[0] = new EbinFieldSource(1, 11, 1, 1, new EbinPayloadSlice(0, guidBytes));

        status = EbinPayloadWriter.Write(state, payloadStorage[guidBytes..], out var stateBytes, out diagnostic);
        Assert.That(status, Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.ToString());
        fields[1] = new EbinFieldSource(2, 4, 1, 1, new EbinPayloadSlice(guidBytes, stateBytes));

        var incrementOffset = checked(guidBytes + stateBytes);
        status = EbinPayloadWriter.Write(increment, payloadStorage[incrementOffset..], out var incrementBytes, out diagnostic);
        Assert.That(status, Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.ToString());
        fields[2] = new EbinFieldSource(3, 4, 1, 1, new EbinPayloadSlice(incrementOffset, incrementBytes));

        var schemaId = new ContentSchemaIdentityAssignment("state/pcg").SchemaId;
        var source = new EbinRecordWriteView(schemaId, 1, 0, fields, payloadStorage[..checked(incrementOffset + incrementBytes)]);
        Assert.That(EbinStructuralWriter.Measure(in source, EbinReadLimits.Foundation, out var recordBytes, out diagnostic), Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.ToString());

        var record = new byte[recordBytes];
        var candidate = new byte[recordBytes];
        Assert.That(EbinStructuralWriter.Write(in source, EbinReadLimits.Foundation, candidate, record, out var bytesWritten, out diagnostic), Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.ToString());
        Assert.That(bytesWritten, Is.EqualTo(record.Length));
        return record;
    }

    private static byte[] CreateWorldRecordArrayRoot(params byte[][] records)
    {
        var recordStorageBytes = 0;

        for (var index = 0; index < records.Length; index++)
        {
            recordStorageBytes = checked(recordStorageBytes + records[index].Length);
        }

        var recordStorage = new byte[recordStorageBytes];
        var slices = new EbinPayloadSlice[records.Length];
        var offset = 0;

        for (var index = 0; index < records.Length; index++)
        {
            records[index].CopyTo(recordStorage, offset);
            slices[index] = new EbinPayloadSlice(offset, records[index].Length);
            offset = checked(offset + records[index].Length);
        }

        Assert.That(
            EbinPayloadWriter.MeasureRecordArray(slices, recordStorage, EbinReadLimits.Foundation, out var payloadBytes, out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.ToString());
        var arrayPayload = new byte[payloadBytes];
        Assert.That(
            EbinPayloadWriter.WriteRecordArray(slices, recordStorage, EbinReadLimits.Foundation, arrayPayload, out var written, out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.ToString());
        Assert.That(written, Is.EqualTo(arrayPayload.Length));

        Span<EbinFieldSource> fields = [new EbinFieldSource(10, 24, 1, checked((ulong)records.Length), new EbinPayloadSlice(0, arrayPayload.Length))];
        var schemaId = new ContentSchemaIdentityAssignment("state/world").SchemaId;
        var source = new EbinRecordWriteView(schemaId, 1, 0, fields, arrayPayload);
        Assert.That(EbinStructuralWriter.Measure(in source, EbinReadLimits.Foundation, out var rootRecordBytes, out diagnostic), Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.ToString());

        var rootRecord = new byte[rootRecordBytes];
        var candidate = new byte[rootRecordBytes];
        Assert.That(EbinStructuralWriter.Write(in source, EbinReadLimits.Foundation, candidate, rootRecord, out var rootBytesWritten, out diagnostic), Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.ToString());
        Assert.That(rootBytesWritten, Is.EqualTo(rootRecord.Length));
        return rootRecord;
    }

    private static void ValidatePcgArrayRoot(byte[] rootRecord, int recordCount)
        => WorldPersistencePcgReader.Validate(rootRecord, 10, recordCount, "worldRecord");

    [Test]
    public void ValidatesRfcOrderedStreamIdsAndOddIncrements()
    {
        Guid firstStreamId = new("00000000-0000-4000-8000-000000000001");
        Guid secondStreamId = new("00000000-0000-4000-8000-000000000002");
        var firstRecord = CreatePcgRecord(firstStreamId, state: 42, increment: 85);
        var secondRecord = CreatePcgRecord(secondStreamId, state: 12, increment: 27);

        Assert.DoesNotThrow(() => ValidatePcgArrayRoot(CreateWorldRecordArrayRoot(firstRecord, secondRecord), 2));
        _ = Assert.Throws<ArgumentException>(() => ValidatePcgArrayRoot(CreateWorldRecordArrayRoot(secondRecord, firstRecord), 2));
        _ = Assert.Throws<ArgumentException>(() => ValidatePcgArrayRoot(CreateWorldRecordArrayRoot(CreatePcgRecord(firstStreamId, state: 1, increment: 2)), 1));
    }
}
