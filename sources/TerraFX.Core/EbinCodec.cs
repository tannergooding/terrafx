// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace TerraFX;

internal static class EbinCodec
{
    internal const uint ExtensionFieldTag = uint.MaxValue;
    internal static Guid ExtensionRecordSchemaId { get; } = new Guid("f57cb3ae-6f40-81d5-b6b1-80dc840cd30c");
    internal const ulong MaximumArrayCount = 1_048_576;
    internal const int MaximumDepth = 64;
    internal const int MaximumFields = 64;
    internal const int MaximumRecordBytes = 65_536;

    internal static ReadOnlySpan<byte> ArrayElement(ReadOnlySpan<byte> bytes, int index)
    {
        var start = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes[(8 + (index * 8))..]));
        var end = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes[(16 + (index * 8))..]));
        return bytes[start..end];
    }

    internal static EbinStatus Fail(Guid schema, uint tag, string category, out EbinDiagnostic diagnostic, ulong offset = 0, ulong limit = 0)
    {
        diagnostic = new EbinDiagnostic(schema, tag, category, offset, limit);
        return EbinStatus.InvalidRecord;
    }

    internal static bool MatchesPrefix(scoped ReadOnlySpan<byte> source, int offset, scoped ReadOnlySpan<byte> expected)
    {
        var length = Math.Clamp(source.Length - offset, 0, expected.Length);
        return source.Slice(Math.Min(source.Length, offset), length).SequenceEqual(expected[..length]);
    }

    internal static EbinStatus Prepare(EbinRecord value, out byte[] bytes, out EbinDiagnostic diagnostic)
    {
        bytes = [];
        diagnostic = default;

        if (value.Fields.Length > MaximumFields)
        {
            return Fail(value.SchemaId, 0, "field-count-budget", out diagnostic, limit: MaximumFields);
        }

        var cursor = checked((ulong)(64 + (value.Fields.Length * 32)));

        foreach (var field in value.Fields)
        {
            if (!TryAlign(cursor, out cursor) || !TryAdd(cursor, checked((ulong)field.Payload.Length), out cursor))
            {
                return Fail(value.SchemaId, field.Tag, "range-arithmetic-overflow", out diagnostic);
            }
        }

        if (!TryAlign(cursor, out cursor) || cursor > MaximumRecordBytes)
        {
            return Fail(value.SchemaId, 0, "record-byte-budget", out diagnostic, limit: MaximumRecordBytes);
        }

        var candidate = new byte[checked((int)cursor)];

        "EBIN"u8.CopyTo(candidate);
        BinaryPrimitives.WriteUInt16LittleEndian(candidate.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(candidate.AsSpan(8), 64);
        BinaryPrimitives.WriteUInt32LittleEndian(candidate.AsSpan(12), checked((uint)value.Fields.Length));
        _ = value.SchemaId.TryWriteBytes(candidate.AsSpan(16, 16), bigEndian: true, out _);
        BinaryPrimitives.WriteUInt64LittleEndian(candidate.AsSpan(32), cursor);
        BinaryPrimitives.WriteUInt64LittleEndian(candidate.AsSpan(40), checked((ulong)value.Fields.Length * 32));
        BinaryPrimitives.WriteUInt32LittleEndian(candidate.AsSpan(48), value.Major);
        BinaryPrimitives.WriteUInt32LittleEndian(candidate.AsSpan(52), value.Minor);

        var offset = 64 + (value.Fields.Length * 32);

        for (var index = 0; index < value.Fields.Length; index++)
        {
            var field = value.Fields[index];
            var entry = candidate.AsSpan(64 + (index * 32), 32);
            BinaryPrimitives.WriteUInt32LittleEndian(entry, field.Tag);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], field.Kind);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], field.Flags);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[8..], checked((ulong)offset));
            BinaryPrimitives.WriteUInt64LittleEndian(entry[16..], checked((ulong)field.Payload.Length));
            BinaryPrimitives.WriteUInt64LittleEndian(entry[24..], field.Count);
            field.Payload.CopyTo(candidate.AsSpan(offset));
            offset = checked((offset + field.Payload.Length + 15) & ~15);
        }

        var status = ScanRecord(candidate, 1, out _, out diagnostic);

        if (status == EbinStatus.Complete)
        {
            bytes = candidate;
        }

        return status;
    }

    internal static EbinStatus Read(scoped ReadOnlySpan<byte> source, out EbinRecord? value, out int consumed, out EbinDiagnostic diagnostic, int storageBudget = MaximumRecordBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(storageBudget);
        value = null;
        consumed = 0;
        var status = ScanRecord(source, 1, out var total, out diagnostic);

        if (status != EbinStatus.Complete)
        {
            if (status == EbinStatus.NeedMoreData)
            {
                if (ReadUnsignedPrefix(source, 32, 8) > (ulong)storageBudget)
                {
                    return Fail(source.Length >= 32 ? new Guid(source.Slice(16, 16), bigEndian: true) : default, 0, "retained-storage-budget", out diagnostic, limit: checked((ulong)storageBudget));
                }

                return ValidateRequiredPrefix(source, out diagnostic);
            }

            return status;
        }

        status = ValidateRequiredKinds(new EbinRecordView(source[..total]), out diagnostic);

        if (status != EbinStatus.Complete)
        {
            return status;
        }

        if (total > storageBudget)
        {
            return Fail(new Guid(source.Slice(16, 16), bigEndian: true), 0, "retained-storage-budget", out diagnostic, limit: checked((ulong)storageBudget));
        }

        value = Stage(source[..total]);
        consumed = total;
        return EbinStatus.Complete;
    }

    internal static EbinStatus ReadPayload(ushort kind, ulong count, int byteLength, scoped ReadOnlySpan<byte> source, out byte[]? value, out int consumed, out EbinDiagnostic diagnostic)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteLength);
        value = null;
        consumed = 0;

        if (byteLength > MaximumRecordBytes)
        {
            return Fail(default, 0, "record-byte-budget", out diagnostic, limit: MaximumRecordBytes);
        }

        if (kind > 24)
        {
            diagnostic = new EbinDiagnostic(default, 0, "unsupported-required-kind");
            return EbinStatus.UnsupportedSchema;
        }

        var bytes = source[..Math.Min(byteLength, source.Length)];
        var status = ScanPayload(kind, count, checked((ulong)byteLength), bytes, 0, out diagnostic);

        if (status == EbinStatus.NeedMoreData && kind == 22)
        {
            return ValidateRequiredPrefix(bytes, out diagnostic);
        }

        if (status == EbinStatus.NeedMoreData && kind == 24 && bytes.Length >= 8)
        {
            var children = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes));

            if (bytes.Length >= 16 + (children * 8))
            {
                for (var child = 0; child < children; child++)
                {
                    var start = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes[(8 + (child * 8))..]));
                    var end = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes[(16 + (child * 8))..]));
                    var childBytes = start < bytes.Length ? bytes.Slice(start, Math.Min(bytes.Length, end) - start) : [];

                    if (ValidateRequiredPrefix(childBytes, out diagnostic) == EbinStatus.UnsupportedSchema)
                    {
                        return EbinStatus.UnsupportedSchema;
                    }
                }
            }
        }

        if (status == EbinStatus.Complete)
        {
            status = ValidatePayloadKinds(kind, bytes, out diagnostic);
        }

        if (status == EbinStatus.Complete)
        {
            value = bytes.ToArray();
            consumed = byteLength;
        }

        return status;
    }

    internal static ulong ReadUnsignedPrefix(scoped ReadOnlySpan<byte> source, int offset, int width)
    {
        // Missing high bytes can only increase an unsigned little-endian claim.
        var length = Math.Clamp(source.Length - offset, 0, width);
        ulong value = 0;

        for (var index = 0; index < length; index++)
        {
            value |= (ulong)source[offset + index] << (index * 8);
        }

        return value;
    }

    internal static EbinStatus ScanPayload(ushort kind, ulong count, ulong length, scoped ReadOnlySpan<byte> bytes, int depth, out EbinDiagnostic diagnostic)
    {
        diagnostic = default;
        var stride = Stride(kind);

        if (stride != 0)
        {
            if (length != (ulong)stride)
            {
                return Fail(default, 0, length == 0 ? "scalar-empty" : "scalar-width", out diagnostic);
            }

            if (count != 1)
            {
                return Fail(default, 0, "scalar-count", out diagnostic);
            }

            if (kind == 9 && bytes.Length != 0 && bytes[0] > 1)
            {
                return Fail(default, 0, "boolean-domain", out diagnostic);
            }
        }
        else if (kind is 12 or 13)
        {
            if (count != length)
            {
                return Fail(default, 0, "byte-count", out diagnostic);
            }

            if (kind == 12)
            {
                if (length > 1_048_576)
                {
                    return Fail(default, 0, "utf8-byte-budget", out diagnostic, limit: 1_048_576);
                }

                var text = bytes;

                while (!text.IsEmpty)
                {
                    var status = Rune.DecodeFromUtf8(text, out _, out var consumed);

                    if (status == OperationStatus.NeedMoreData && (ulong)bytes.Length < length)
                    {
                        var sequenceLength = text[0] >= 0xF0 ? 4UL : text[0] >= 0xE0 ? 3UL : 2UL;

                        if (length - checked((ulong)(bytes.Length - text.Length)) < sequenceLength)
                        {
                            return Fail(default, 0, "utf8-invalid", out diagnostic);
                        }

                        break;
                    }

                    if (status != OperationStatus.Done)
                    {
                        return Fail(default, 0, "utf8-invalid", out diagnostic);
                    }

                    text = text[consumed..];
                }
            }
        }
        else if (kind == 22)
        {
            if (count != 1 || length < 64 || (length & 15) != 0)
            {
                return Fail(default, 0, "nested-record-extent", out diagnostic);
            }

            if (bytes.Length >= 40 && BinaryPrimitives.ReadUInt64LittleEndian(bytes[32..]) != length)
            {
                return Fail(default, 0, "nested-record-total", out diagnostic);
            }

            var status = ScanRecord(bytes, depth + 1, out var total, out diagnostic);

            if (status != EbinStatus.Complete)
            {
                return status;
            }

            if ((ulong)total != length)
            {
                return Fail(default, 0, "nested-record-total", out diagnostic);
            }
        }
        else if (kind == 23)
        {
            if (length < 32)
            {
                return Fail(default, 0, "fixedarray-descriptor", out diagnostic);
            }

            if (bytes[..Math.Min(bytes.Length, 16)].ContainsAnyExcept((byte)0)
                || !MatchesPrefix(bytes, 18, [0, 0]) || !MatchesPrefix(bytes, 24, [0, 0, 0, 0, 0, 0, 0, 0]))
            {
                return Fail(default, 0, "fixedarray-reserved", out diagnostic);
            }

            if (bytes.Length >= 18)
            {
                var elementKind = BinaryPrimitives.ReadUInt16LittleEndian(bytes[16..]);
                stride = Stride(elementKind);

                if (stride == 0 || !MatchesPrefix(bytes, 20, [checked((byte)stride), 0, 0, 0]))
                {
                    return Fail(default, 0, "fixedarray-stride", out diagnostic);
                }

                if (!TryMultiply(count, checked((ulong)stride), out var dataBytes) || !TryAdd(32, dataBytes, out var required))
                {
                    return Fail(default, 0, "array-product-overflow", out diagnostic);
                }

                if (count > MaximumArrayCount)
                {
                    return Fail(default, 0, "array-count-budget", out diagnostic, limit: MaximumArrayCount);
                }

                if (required != length)
                {
                    return Fail(default, 0, "fixedarray-extent", out diagnostic);
                }

                if (elementKind == 9 && bytes.Length > 32)
                {
                    foreach (var value in bytes[32..])
                    {
                        if (value > 1)
                        {
                            return Fail(default, 0, "boolean-domain", out diagnostic);
                        }
                    }
                }
            }
            else if (count > MaximumArrayCount)
            {
                return Fail(default, 0, "array-count-budget", out diagnostic, limit: MaximumArrayCount);
            }
        }
        else if (kind == 24)
        {
            return ScanRecordArray(count, length, bytes, depth, out diagnostic);
        }
        else if (kind == 0)
        {
            return Fail(default, 0, "field-kind-zero", out diagnostic);
        }

        return (ulong)bytes.Length < length ? EbinStatus.NeedMoreData : EbinStatus.Complete;
    }

    internal static EbinStatus ScanRecord(scoped ReadOnlySpan<byte> source, int depth, out int total, out EbinDiagnostic diagnostic)
    {
        total = 0;
        diagnostic = default;
        var schema = source.Length >= 32 ? new Guid(source.Slice(16, 16), bigEndian: true) : default;

        if (depth > MaximumDepth)
        {
            return Fail(schema, 0, "nesting-depth-budget", out diagnostic, limit: MaximumDepth);
        }

        if (!source[..Math.Min(source.Length, 4)].SequenceEqual("EBIN"u8[..Math.Min(source.Length, 4)]))
        {
            return Fail(schema, 0, "header-magic", out diagnostic);
        }

        if (!MatchesPrefix(source, 4, [1, 0]) || !MatchesPrefix(source, 6, [0, 0]))
        {
            diagnostic = new EbinDiagnostic(schema, 0, "unsupported-container-version");
            return EbinStatus.UnsupportedSchema;
        }

        if (!MatchesPrefix(source, 8, [64, 0, 0, 0]))
        {
            return Fail(schema, 0, "header-extent", out diagnostic);
        }

        var count = checked((uint)ReadUnsignedPrefix(source, 12, 4));

        if (count > MaximumFields)
        {
            return Fail(schema, 0, "field-count-budget", out diagnostic, limit: MaximumFields);
        }

        var payloadStart = 64UL + (count * 32UL);
        var extent = source.Length >= 40 ? BinaryPrimitives.ReadUInt64LittleEndian(source[32..]) : 0;

        if (source.Length < 40 && ReadUnsignedPrefix(source, 32, 8) > MaximumRecordBytes)
        {
            return Fail(schema, 0, "record-byte-budget", out diagnostic, limit: MaximumRecordBytes);
        }

        if (source.Length > 32 && (source[32] & 15) != 0)
        {
            return Fail(schema, 0, "header-extent", out diagnostic);
        }

        if (source.Length >= 40)
        {
            if (!TryAlign(extent, out _))
            {
                return Fail(schema, 0, "range-arithmetic-overflow", out diagnostic);
            }

            if (extent > MaximumRecordBytes)
            {
                return Fail(schema, 0, "record-byte-budget", out diagnostic, limit: MaximumRecordBytes);
            }

            if (extent < payloadStart || (extent & 15) != 0)
            {
                return Fail(schema, 0, "header-extent", out diagnostic);
            }
        }

        Unsafe.SkipInit(out InlineArray8<byte> expectedTableStorage); // WriteUInt64 fills all 8 bytes before comparison.
        Span<byte> expectedTableBytes = expectedTableStorage;

        BinaryPrimitives.WriteUInt64LittleEndian(expectedTableBytes, count * 32UL);

        if (!MatchesPrefix(source, 40, expectedTableBytes))
        {
            return Fail(schema, 0, "field-table-extent", out diagnostic);
        }

        if ((source.Length >= 52 && BinaryPrimitives.ReadUInt32LittleEndian(source[48..]) == 0)
            || ReadUnsignedPrefix(source, 48, 4) > int.MaxValue || ReadUnsignedPrefix(source, 52, 4) > int.MaxValue)
        {
            return Fail(schema, 0, "signed-projection-range", out diagnostic);
        }

        if (!MatchesPrefix(source, 56, [0, 0, 0, 0]))
        {
            return Fail(schema, 0, "header-flags", out diagnostic);
        }

        if (!MatchesPrefix(source, 60, [0, 0, 0, 0]))
        {
            return Fail(schema, 0, "header-reserved", out diagnostic);
        }

        if (source.Length < 64)
        {
            return EbinStatus.NeedMoreData;
        }

        source = source[..Math.Min(source.Length, checked((int)extent))];
        uint previousTag = 0;
        Unsafe.SkipInit(out InlineArray8<byte> scalarExtentStorage); // Each comparison follows a full 8-byte write.
        Span<byte> scalarExtent = scalarExtentStorage;

        for (var index = 0; index < count; index++)
        {
            var entryStart = 64 + (index * 32);
            var available = Math.Clamp(source.Length - entryStart, 0, 32);
            var entry = source.Slice(Math.Min(entryStart, source.Length), available);
            var tag = available >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(entry) : 0;

            if (available >= 4 && (tag == 0 || tag <= previousTag))
            {
                var duplicate = false;

                for (var earlier = 0; earlier < index; earlier++)
                {
                    duplicate |= BinaryPrimitives.ReadUInt32LittleEndian(source[(64 + (earlier * 32))..]) == tag;
                }

                return Fail(schema, tag, tag == 0 ? "field-tag-zero" : duplicate ? "field-tag-duplicate" : "field-tag-order", out diagnostic);
            }

            var kind = available >= 6 ? BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]) : (ushort)0;

            if (available >= 6 && kind == 0)
            {
                return Fail(schema, tag, "field-kind-zero", out diagnostic);
            }

            var fixedStride = Stride(kind);
            BinaryPrimitives.WriteUInt64LittleEndian(scalarExtent, checked((ulong)fixedStride));

            if (fixedStride != 0 && available < 24 && !MatchesPrefix(entry, 16, scalarExtent))
            {
                return Fail(schema, tag, "scalar-width", out diagnostic);
            }

            BinaryPrimitives.WriteUInt64LittleEndian(scalarExtent, 1);

            if ((fixedStride != 0 || kind == 22) && available < 32 && !MatchesPrefix(entry, 24, scalarExtent))
            {
                return Fail(schema, tag, "scalar-count", out diagnostic);
            }

            if ((available >= 7 && entry[6] > 1) || (available >= 8 && entry[7] != 0))
            {
                return Fail(schema, tag, "field-flags", out diagnostic);
            }

            var offset = available >= 16 ? BinaryPrimitives.ReadUInt64LittleEndian(entry[8..]) : 0;
            var length = available >= 24 ? BinaryPrimitives.ReadUInt64LittleEndian(entry[16..]) : 0;

            if (available > 8 && (entry[8] & 15) != 0)
            {
                return Fail(schema, tag, "field-range-alignment", out diagnostic);
            }

            if (available >= 24)
            {
                if (!TryAdd(offset, length, out var end) || !TryAlign(end, out _))
                {
                    return Fail(schema, tag, "range-arithmetic-overflow", out diagnostic);
                }

                if (offset < payloadStart)
                {
                    return Fail(schema, tag, offset < 64 ? "field-range-in-header" : "field-range-in-header-table", out diagnostic);
                }

                if (end > extent)
                {
                    return Fail(schema, tag, "field-range-past-end", out diagnostic);
                }

                var stride = Stride(kind);

                if (stride != 0 && length != (ulong)stride)
                {
                    return Fail(schema, tag, length == 0 ? "scalar-empty" : "scalar-width", out diagnostic);
                }

                for (var earlier = 0; earlier < index; earlier++)
                {
                    var other = source.Slice(64 + (earlier * 32), 32);
                    var otherOffset = BinaryPrimitives.ReadUInt64LittleEndian(other[8..]);
                    var otherLength = BinaryPrimitives.ReadUInt64LittleEndian(other[16..]);

                    if (length != 0 && otherLength != 0 && offset < otherOffset + otherLength && otherOffset < end)
                    {
                        return Fail(schema, tag, "field-range-overlap", out diagnostic);
                    }
                }
            }

            if (available < 32)
            {
                return EbinStatus.NeedMoreData;
            }

            previousTag = tag;
            var elementCount = BinaryPrimitives.ReadUInt64LittleEndian(entry[24..]);
            var fieldBytes = offset <= (ulong)source.Length
                ? source.Slice(checked((int)offset), checked((int)Math.Min(length, (ulong)source.Length - offset)))
                : [];
            var status = ScanPayload(kind, elementCount, length, fieldBytes, depth, out diagnostic);

            if (status is EbinStatus.InvalidRecord or EbinStatus.UnsupportedSchema)
            {
                diagnostic = diagnostic with {
                    SchemaId = diagnostic.SchemaId == default ? schema : diagnostic.SchemaId,
                    Tag = diagnostic.Tag == 0 ? tag : diagnostic.Tag
                };
                return status;
            }
        }

        if (source.Length >= (int)payloadStart)
        {
            // Visit positive ranges in offset order, independently of table/tag order.
            var cursor = payloadStart;

            while (cursor < (ulong)source.Length)
            {
                var nextStart = extent;
                var nextEnd = extent;

                for (var index = 0; index < count; index++)
                {
                    var entry = source.Slice(64 + (index * 32), 32);
                    var start = BinaryPrimitives.ReadUInt64LittleEndian(entry[8..]);
                    var length = BinaryPrimitives.ReadUInt64LittleEndian(entry[16..]);

                    if (length != 0 && start >= cursor && start < nextStart)
                    {
                        nextStart = start;
                        nextEnd = start + length;
                    }
                }

                var paddingEnd = Math.Min(nextStart, checked((ulong)source.Length));

                if (source.Slice(checked((int)cursor), checked((int)(paddingEnd - cursor))).ContainsAnyExcept((byte)0))
                {
                    return Fail(schema, 0, "padding-nonzero", out diagnostic, offset: cursor);
                }

                cursor = nextEnd;
            }
        }

        if ((ulong)source.Length < extent)
        {
            return EbinStatus.NeedMoreData;
        }

        total = checked((int)extent);
        return EbinStatus.Complete;
    }

    internal static EbinRecord Stage(scoped ReadOnlySpan<byte> bytes)
    {
        var view = new EbinRecordView(bytes);
        var fields = new EbinField[view.FieldCount];

        for (var index = 0; index < fields.Length; index++)
        {
            var entry = view.Entry(index);
            fields[index] = new EbinField(
                BinaryPrimitives.ReadUInt32LittleEndian(entry),
                BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]),
                BinaryPrimitives.ReadUInt64LittleEndian(entry[24..]),
                view.Payload(index));
        }

        return new EbinRecord(view.SchemaId, view.Major, view.Minor, fields);
    }

    internal static int Stride(ushort kind) => kind switch {
        1 or 5 or 9 => 1,
        2 or 6 => 2,
        3 or 7 or 10 => 4,
        4 or 8 or 14 => 8,
        11 or 16 or 17 or 20 => 16,
        15 => 12,
        18 or 21 => 24,
        19 => 64,
        _ => 0,
    };

    internal static bool TryAdd(ulong left, ulong right, out ulong result)
    {
        result = unchecked(left + right);
        return result >= left;
    }

    internal static bool TryAlign(ulong value, out ulong result)
    {
        if (!TryAdd(value, 15, out result))
        {
            return false;
        }

        result &= ~15UL;
        return true;
    }

    internal static bool TryMultiply(ulong left, ulong right, out ulong result)
    {
        result = 0;

        if (right != 0 && left > ulong.MaxValue / right)
        {
            return false;
        }

        result = left * right;
        return true;
    }

    internal static EbinStatus ValidateRequiredKinds(EbinRecordView view, out EbinDiagnostic diagnostic)
    {
        diagnostic = default;

        for (var index = 0; index < view.FieldCount; index++)
        {
            var entry = view.Entry(index);
            var kind = BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]);

            if (kind > 24 && BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]) == 1)
            {
                diagnostic = new EbinDiagnostic(view.SchemaId, BinaryPrimitives.ReadUInt32LittleEndian(entry), "unsupported-required-kind");
                return EbinStatus.UnsupportedSchema;
            }

            if (kind == 22)
            {
                var status = ValidateRequiredKinds(new EbinRecordView(view.Payload(index)), out diagnostic);

                if (status != EbinStatus.Complete)
                {
                    return status;
                }
            }
            else if (kind == 24)
            {
                var payload = view.Payload(index);
                var count = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(payload));

                for (var child = 0; child < count; child++)
                {
                    var status = ValidateRequiredKinds(new EbinRecordView(ArrayElement(payload, child)), out diagnostic);

                    if (status != EbinStatus.Complete)
                    {
                        return status;
                    }
                }
            }
        }

        return EbinStatus.Complete;
    }

    internal static EbinStatus ValidateRequiredPrefix(scoped ReadOnlySpan<byte> source, out EbinDiagnostic diagnostic)
    {
        diagnostic = default;

        if (source.Length < 64)
        {
            return EbinStatus.NeedMoreData;
        }

        var schema = new Guid(source.Slice(16, 16), bigEndian: true);
        var count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(source[12..]));

        for (var index = 0; index < count; index++)
        {
            var start = 64 + (index * 32);
            var available = Math.Clamp(source.Length - start, 0, 32);

            if (available < 8)
            {
                break;
            }

            var entry = source.Slice(start, available);
            var kind = BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]);

            if (kind > 24 && BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]) == 1)
            {
                diagnostic = new EbinDiagnostic(schema, BinaryPrimitives.ReadUInt32LittleEndian(entry), "unsupported-required-kind");
                return EbinStatus.UnsupportedSchema;
            }

            if (available != 32)
            {
                break;
            }

            var offset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(entry[8..]));
            var length = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(entry[16..]));
            var payload = offset < source.Length ? source.Slice(offset, Math.Min(length, source.Length - offset)) : [];

            if (kind == 22)
            {
                var status = ValidateRequiredPrefix(payload, out diagnostic);

                if (status == EbinStatus.UnsupportedSchema)
                {
                    return status;
                }
            }
            else if (kind == 24 && payload.Length >= 8)
            {
                var children = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(payload));

                if (payload.Length >= 16 + (children * 8))
                {
                    for (var child = 0; child < children; child++)
                    {
                        var childStart = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(payload[(8 + (child * 8))..]));
                        var childEnd = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(payload[(16 + (child * 8))..]));
                        var childBytes = childStart < payload.Length ? payload.Slice(childStart, Math.Min(payload.Length, childEnd) - childStart) : [];
                        var status = ValidateRequiredPrefix(childBytes, out diagnostic);

                        if (status == EbinStatus.UnsupportedSchema)
                        {
                            return status;
                        }
                    }
                }
            }
        }

        return EbinStatus.NeedMoreData;
    }

    internal static EbinStatus Write(EbinRecord value, scoped Span<byte> destination, out int written, out EbinDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(value);
        written = 0;

        var status = Prepare(value, out var bytes, out diagnostic);

        if (status != EbinStatus.Complete)
        {
            return status;
        }

        status = ValidateRequiredKinds(new EbinRecordView(bytes), out diagnostic);

        if (status != EbinStatus.Complete)
        {
            return status;
        }

        if (destination.Length < bytes.Length)
        {
            diagnostic = new EbinDiagnostic(value.SchemaId, 0, "destination-too-small", Limit: checked((ulong)bytes.Length));
            return EbinStatus.DestinationTooSmall;
        }

        bytes.CopyTo(destination);

        written = bytes.Length;
        return EbinStatus.Complete;
    }

    internal static EbinStatus WritePayload(ushort kind, ulong count, scoped ReadOnlySpan<byte> value, scoped Span<byte> destination, out int written, out EbinDiagnostic diagnostic)
    {
        written = 0;

        if (kind > 24)
        {
            diagnostic = new EbinDiagnostic(default, 0, "unsupported-required-kind");
            return EbinStatus.UnsupportedSchema;
        }

        if (value.Length > MaximumRecordBytes)
        {
            return Fail(default, 0, "record-byte-budget", out diagnostic, limit: MaximumRecordBytes);
        }

        var status = ScanPayload(kind, count, checked((ulong)value.Length), value, 0, out diagnostic);

        if (status != EbinStatus.Complete)
        {
            return status;
        }

        status = ValidatePayloadKinds(kind, value, out diagnostic);

        if (status != EbinStatus.Complete)
        {
            return status;
        }

        if (destination.Length < value.Length)
        {
            diagnostic = new EbinDiagnostic(default, 0, "destination-too-small", Limit: checked((ulong)value.Length));
            return EbinStatus.DestinationTooSmall;
        }

        value.CopyTo(destination);

        written = value.Length;
        return EbinStatus.Complete;
    }

    private static EbinStatus ScanRecordArray(ulong count, ulong length, scoped ReadOnlySpan<byte> bytes, int depth, out EbinDiagnostic diagnostic)
    {
        diagnostic = default;

        if (!TryAdd(count, 1, out var offsetsCount) || !TryMultiply(offsetsCount, 8, out var tableBytes)
            || !TryAdd(8, tableBytes, out tableBytes) || !TryAlign(tableBytes, out var first))
        {
            return Fail(default, 0, "array-product-overflow", out diagnostic);
        }

        if (count > MaximumArrayCount)
        {
            return Fail(default, 0, "array-count-budget", out diagnostic, limit: MaximumArrayCount);
        }

        if (length < first || (length & 15) != 0 || (count == 0 && length != 16))
        {
            return Fail(default, 0, "recordarray-extent", out diagnostic);
        }

        if (bytes.Length < 16 + ((int)count * 8) && first + (count * 64) > length)
        {
            return Fail(default, 0, "recordarray-nested-total", out diagnostic);
        }

        if (bytes.Length >= 8 && BinaryPrimitives.ReadUInt64LittleEndian(bytes) != count)
        {
            return Fail(default, 0, "recordarray-count", out diagnostic);
        }

        if ((ulong)bytes.Length > tableBytes && bytes.Slice(checked((int)tableBytes), checked((int)(Math.Min(first, (ulong)bytes.Length) - tableBytes))).ContainsAnyExcept((byte)0))
        {
            return Fail(default, 0, "padding-nonzero", out diagnostic);
        }

        if (bytes.Length >= 16 && BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]) != first)
        {
            return Fail(default, 0, "recordarray-first-offset", out diagnostic);
        }

        for (var index = 0; index < (int)count; index++)
        {
            if (bytes.Length < 24 + (index * 8))
            {
                return EbinStatus.NeedMoreData;
            }

            var start = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(8 + (index * 8))..]);
            var end = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(16 + (index * 8))..]);

            if (end <= start || (start & 15) != 0 || (end & 15) != 0 || end > length)
            {
                return Fail(default, 0, "recordarray-offset-order", out diagnostic);
            }

            if (end - start < 64)
            {
                return Fail(default, 0, "recordarray-nested-total", out diagnostic);
            }

            var child = (ulong)bytes.Length >= start ? bytes.Slice(checked((int)start), checked((int)Math.Min(end - start, (ulong)bytes.Length - start))) : [];

            if (child.Length >= 40 && BinaryPrimitives.ReadUInt64LittleEndian(child[32..]) != end - start)
            {
                return Fail(default, 0, "recordarray-nested-total", out diagnostic);
            }

            var status = ScanRecord(child, depth + 1, out var total, out diagnostic);

            if (status is EbinStatus.InvalidRecord or EbinStatus.UnsupportedSchema)
            {
                return status;
            }

            if (status == EbinStatus.Complete && (ulong)total != end - start)
            {
                return Fail(default, 0, "recordarray-nested-total", out diagnostic);
            }
        }

        if (bytes.Length < 16 + ((int)count * 8))
        {
            return EbinStatus.NeedMoreData;
        }

        if (BinaryPrimitives.ReadUInt64LittleEndian(bytes[(8 + ((int)count * 8))..]) != length)
        {
            return Fail(default, 0, "recordarray-final-offset", out diagnostic);
        }

        return (ulong)bytes.Length < length ? EbinStatus.NeedMoreData : EbinStatus.Complete;
    }

    private static EbinStatus ValidatePayloadKinds(ushort kind, scoped ReadOnlySpan<byte> bytes, out EbinDiagnostic diagnostic)
    {
        diagnostic = default;

        if (kind == 22)
        {
            return ValidateRequiredKinds(new EbinRecordView(bytes), out diagnostic);
        }

        if (kind == 24)
        {
            var count = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes));

            for (var index = 0; index < count; index++)
            {
                var status = ValidateRequiredKinds(new EbinRecordView(ArrayElement(bytes, index)), out diagnostic);

                if (status != EbinStatus.Complete)
                {
                    return status;
                }
            }
        }

        return EbinStatus.Complete;
    }
}
