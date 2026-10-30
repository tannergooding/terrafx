// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

internal struct EbinScanState
{
    private long _arrayElementCount;
    private long _fieldCount;
    private readonly EbinReadLimits _limits;
    private long _recordCount;

    internal EbinScanState(EbinReadLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        _arrayElementCount = 0;
        _fieldCount = 0;
        _limits = limits;
        _recordCount = 0;
    }

    internal bool TryAddArrayElements(Guid schemaId, uint tag, ulong count, out EbinDiagnostic diagnostic)
    {
        if (count > (ulong)_limits.MaximumArrayElements)
        {
            _ = EbinCodec.Fail(schemaId, tag, "array-count-budget", out diagnostic, limit: checked((ulong)_limits.MaximumArrayElements), claim: count);
            return false;
        }

        var total = checked(_arrayElementCount + checked((long)count));

        if (total > _limits.MaximumAggregateArrayElements)
        {
            _ = EbinCodec.Fail(schemaId, tag, "aggregate-array-budget", out diagnostic, limit: checked((ulong)_limits.MaximumAggregateArrayElements), claim: checked((ulong)total));
            return false;
        }

        _arrayElementCount = total;
        diagnostic = default;
        return true;
    }

    internal bool TryCountFields(Guid schemaId, uint count, out EbinDiagnostic diagnostic)
    {
        if ((ulong)count > (ulong)_limits.MaximumFieldsPerRecord)
        {
            _ = EbinCodec.Fail(schemaId, 0, "field-count-budget", out diagnostic, limit: checked((ulong)_limits.MaximumFieldsPerRecord), claim: count);
            return false;
        }

        var total = checked(_fieldCount + count);

        if (total > _limits.MaximumAggregateFields)
        {
            _ = EbinCodec.Fail(schemaId, 0, "aggregate-field-budget", out diagnostic, limit: checked((ulong)_limits.MaximumAggregateFields), claim: checked((ulong)total));
            return false;
        }

        _fieldCount = total;
        diagnostic = default;
        return true;
    }

    internal bool TryCountRecord(Guid schemaId, int depth, out EbinDiagnostic diagnostic)
    {
        if (depth > _limits.MaximumDepth)
        {
            _ = EbinCodec.Fail(schemaId, 0, "nesting-depth-budget", out diagnostic, limit: checked((ulong)_limits.MaximumDepth), claim: checked((ulong)depth));
            return false;
        }

        var total = checked(_recordCount + 1);

        if (total > _limits.MaximumAggregateRecords)
        {
            _ = EbinCodec.Fail(schemaId, 0, "aggregate-record-budget", out diagnostic, limit: checked((ulong)_limits.MaximumAggregateRecords), claim: checked((ulong)total));
            return false;
        }

        _recordCount = total;
        diagnostic = default;
        return true;
    }

    internal readonly bool TryValidateRecordBytes(Guid schemaId, ulong recordBytes, out EbinDiagnostic diagnostic)
    {
        if (recordBytes > (ulong)_limits.MaximumRecordBytes)
        {
            _ = EbinCodec.Fail(schemaId, 0, "record-byte-budget", out diagnostic, limit: checked((ulong)_limits.MaximumRecordBytes), claim: recordBytes);
            return false;
        }

        diagnostic = default;
        return true;
    }

    internal readonly bool TryValidateUtf8Bytes(Guid schemaId, uint tag, ulong byteCount, out EbinDiagnostic diagnostic)
    {
        if (byteCount > (ulong)_limits.MaximumUtf8Bytes)
        {
            _ = EbinCodec.Fail(schemaId, tag, "utf8-byte-budget", out diagnostic, limit: checked((ulong)_limits.MaximumUtf8Bytes), claim: byteCount);
            return false;
        }

        diagnostic = default;
        return true;
    }
}
