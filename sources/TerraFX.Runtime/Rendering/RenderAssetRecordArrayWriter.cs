// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Rendering;

internal static class RenderAssetRecordArrayWriter
{
    internal static bool TryEncode(
        scoped ReadOnlySpan<EbinRecord> records,
        EbinReadLimits limits,
        out byte[] payload,
        out string? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        payload = [];
        diagnostic = null;
        var encodedRecords = new byte[records.Length][];
        var recordSlices = new EbinPayloadSlice[records.Length];
        var storageLength = 0;

        for (var index = 0; index < records.Length; index++)
        {
            var status = EbinCodec.Prepare(records[index], out var encodedRecord, out var ebinDiagnostic);

            if (status is not EbinStatus.Complete)
            {
                diagnostic = ebinDiagnostic.Category;
                return false;
            }

            encodedRecords[index] = encodedRecord;
            recordSlices[index] = new EbinPayloadSlice(storageLength, encodedRecord.Length);
            storageLength = checked(storageLength + encodedRecord.Length);
        }

        var storage = new byte[storageLength];

        for (var index = 0; index < encodedRecords.Length; index++)
        {
            encodedRecords[index].CopyTo(storage, recordSlices[index].Offset);
        }

        var measureStatus = EbinPayloadWriter.MeasureRecordArray(
            recordSlices,
            storage,
            limits,
            out var payloadLength,
            out var structuralDiagnostic);

        if (measureStatus is not EbinWriteStatus.Complete)
        {
            diagnostic = structuralDiagnostic?.Category ?? measureStatus.ToString();
            return false;
        }

        payload = new byte[payloadLength];
        var writeStatus = EbinPayloadWriter.WriteRecordArray(
            recordSlices,
            storage,
            limits,
            payload,
            out var bytesWritten,
            out structuralDiagnostic);

        if (writeStatus is not EbinWriteStatus.Complete)
        {
            payload = [];
            diagnostic = structuralDiagnostic?.Category ?? writeStatus.ToString();
            return false;
        }

        if (bytesWritten != payload.Length)
        {
            payload = [];
            diagnostic = "record-array-length-mismatch";
            return false;
        }

        return true;
    }
}
