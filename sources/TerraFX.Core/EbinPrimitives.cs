// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using TerraFX.Numerics;

namespace TerraFX;

internal static class EbinPrimitives
{
    internal static EbinField Binary32(uint tag, float value)
    {
        Unsafe.SkipInit(out InlineArray4<byte> storage); // BinaryPrimitives fills every payload byte before return.
        Span<byte> bytes = storage;

        BinaryPrimitives.WriteInt32LittleEndian(bytes, BitConverter.SingleToInt32Bits(value));
        return new EbinField(tag, 10, 1, 1, bytes);
    }

    internal static EbinField Boolean(uint tag, bool value) => new EbinField(tag, 9, 1, 1, [value ? (byte)1 : (byte)0]);

    internal static EbinField Bytes(uint tag, scoped ReadOnlySpan<byte> value) => new EbinField(tag, 13, 1, checked((ulong)value.Length), value);

    internal static byte[] Encode(EbinRecord record)
    {
        var status = EbinCodec.Prepare(record, out var bytes, out var diagnostic);

        if (status != EbinStatus.Complete)
        {
            ThrowHelper.ThrowArgumentException($"The record cannot be encoded: {diagnostic.Category}.", nameof(record));
        }

        return bytes;
    }

    internal static EbinField Geometry(uint tag, Matrix3x2 value) => Components(tag, 18, [value.Row1.X, value.Row1.Y, value.Row2.X, value.Row2.Y, value.Row3.X, value.Row3.Y]);
    internal static EbinField Geometry(uint tag, Matrix4x4 value) => Components(tag, 19, [
        value.Row1.X, value.Row1.Y, value.Row1.Z, value.Row1.W,
        value.Row2.X, value.Row2.Y, value.Row2.Z, value.Row2.W,
        value.Row3.X, value.Row3.Y, value.Row3.Z, value.Row3.W,
        value.Row4.X, value.Row4.Y, value.Row4.Z, value.Row4.W,
    ]);
    internal static EbinField Geometry(uint tag, Quaternion value) => Components(tag, 17, [value.X, value.Y, value.Z, value.W]);

    internal static EbinField Geometry(uint tag, Vector2 value) => Components(tag, 14, [value.X, value.Y]);
    internal static EbinField Geometry(uint tag, Vector3 value) => Components(tag, 15, [value.X, value.Y, value.Z]);
    internal static EbinField Geometry(uint tag, Vector4 value) => Components(tag, 16, [value.X, value.Y, value.Z, value.W]);

    internal static EbinField Guid(uint tag, Guid value)
    {
        Unsafe.SkipInit(out InlineArray16<byte> storage); // TryWriteBytes fills all 16 bytes before return.
        Span<byte> bytes = storage;

        _ = value.TryWriteBytes(bytes, bigEndian: true, out _);
        return new EbinField(tag, 11, 1, 1, bytes);
    }

    internal static EbinField Int16(uint tag, short value)
    {
        Unsafe.SkipInit(out InlineArray2<byte> storage); // BinaryPrimitives fills every payload byte before return.
        Span<byte> bytes = storage;

        BinaryPrimitives.WriteInt16LittleEndian(bytes, value);
        return new EbinField(tag, 6, 1, 1, bytes);
    }

    internal static EbinField Int32(uint tag, int value)
    {
        Unsafe.SkipInit(out InlineArray4<byte> storage); // BinaryPrimitives fills every payload byte before return.
        Span<byte> bytes = storage;

        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return new EbinField(tag, 7, 1, 1, bytes);
    }

    internal static EbinField Int64(uint tag, long value)
    {
        Unsafe.SkipInit(out InlineArray8<byte> storage); // BinaryPrimitives fills every payload byte before return.
        Span<byte> bytes = storage;

        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        return new EbinField(tag, 8, 1, 1, bytes);
    }

    internal static EbinField Int8(uint tag, sbyte value) => new EbinField(tag, 5, 1, 1, [unchecked((byte)value)]);

    internal static EbinField Position(uint tag, SpatialPosition2 value)
    {
        Unsafe.SkipInit(out InlineArray16<byte> storage); // These writes fill every byte before return.
        Span<byte> bytes = storage;

        BinaryPrimitives.WriteInt32LittleEndian(bytes, value.Chunk.X);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], value.Chunk.Y);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], BitConverter.SingleToInt32Bits(value.Offset.X));
        BinaryPrimitives.WriteInt32LittleEndian(bytes[12..], BitConverter.SingleToInt32Bits(value.Offset.Y));
        return new EbinField(tag, 20, 1, 1, bytes);
    }

    internal static EbinField Position(uint tag, SpatialPosition3 value)
    {
        Unsafe.SkipInit(out InlineBuffer32<byte> storage); // These writes fill all 24 exposed bytes before return.
        var bytes = storage[..24];

        BinaryPrimitives.WriteInt32LittleEndian(bytes, value.Chunk.X);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], value.Chunk.Y);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], value.Chunk.Z);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[12..], BitConverter.SingleToInt32Bits(value.Offset.X));
        BinaryPrimitives.WriteInt32LittleEndian(bytes[16..], BitConverter.SingleToInt32Bits(value.Offset.Y));
        BinaryPrimitives.WriteInt32LittleEndian(bytes[20..], BitConverter.SingleToInt32Bits(value.Offset.Z));
        return new EbinField(tag, 21, 1, 1, bytes);
    }

    internal static EbinField Record(uint tag, EbinRecord value) => new EbinField(tag, 22, 1, 1, Encode(value));

    internal static EbinField RecordArray(uint tag, scoped ReadOnlySpan<EbinRecord> records)
    {
        var destination = new byte[EbinCodec.MaximumRecordBytes];
        var status = WriteRecordArray(records, destination, out var written, out var diagnostic);

        if (status != EbinStatus.Complete)
        {
            ThrowHelper.ThrowArgumentException($"The record array cannot be encoded: {diagnostic.Category}.", nameof(records));
        }

        return new EbinField(tag, 24, 1, checked((ulong)records.Length), destination.AsSpan(0, written));
    }

    internal static float Single(scoped ReadOnlySpan<byte> bytes) => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes));

    internal static EbinField UInt16(uint tag, ushort value)
    {
        Unsafe.SkipInit(out InlineArray2<byte> storage); // BinaryPrimitives fills every payload byte before return.
        Span<byte> bytes = storage;

        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        return new EbinField(tag, 2, 1, 1, bytes);
    }

    internal static EbinField UInt32(uint tag, uint value)
    {
        Unsafe.SkipInit(out InlineArray4<byte> storage); // BinaryPrimitives fills every payload byte before return.
        Span<byte> bytes = storage;

        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return new EbinField(tag, 3, 1, 1, bytes);
    }

    internal static EbinField UInt64(uint tag, ulong value)
    {
        Unsafe.SkipInit(out InlineArray8<byte> storage); // BinaryPrimitives fills every payload byte before return.
        Span<byte> bytes = storage;

        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        return new EbinField(tag, 4, 1, 1, bytes);
    }

    internal static EbinField UInt8(uint tag, byte value) => new EbinField(tag, 1, 1, 1, [value]);
    internal static Vector2 Vector2(scoped ReadOnlySpan<byte> bytes) => new Vector2(Single(bytes), Single(bytes[4..]));
    internal static Vector3 Vector3(scoped ReadOnlySpan<byte> bytes) => new Vector3(Single(bytes), Single(bytes[4..]), Single(bytes[8..]));
    internal static Vector4 Vector4(scoped ReadOnlySpan<byte> bytes) => new Vector4(Single(bytes), Single(bytes[4..]), Single(bytes[8..]), Single(bytes[12..]));

    internal static EbinStatus WriteFixedArray(ushort elementKind, ulong count, scoped ReadOnlySpan<byte> elements, scoped Span<byte> destination, out int written, out EbinDiagnostic diagnostic)
    {
        written = 0;
        var stride = EbinCodec.Stride(elementKind);

        if (stride == 0)
        {
            return EbinCodec.Fail(default, 0, "fixedarray-stride", out diagnostic);
        }

        if (!EbinCodec.TryMultiply(count, checked((ulong)stride), out var dataLength) || !EbinCodec.TryAdd(32, dataLength, out var length))
        {
            return EbinCodec.Fail(default, 0, "array-product-overflow", out diagnostic);
        }

        if (count > EbinCodec.MaximumArrayCount || length > EbinCodec.MaximumRecordBytes)
        {
            return EbinCodec.Fail(default, 0, "array-count-budget", out diagnostic, limit: EbinCodec.MaximumArrayCount);
        }

        if (dataLength != (ulong)elements.Length)
        {
            return EbinCodec.Fail(default, 0, "fixedarray-extent", out diagnostic);
        }

        var bytes = new byte[checked((int)length)];

        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(16), elementKind);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), checked((uint)stride));

        elements.CopyTo(bytes.AsSpan(32));
        return EbinCodec.WritePayload(23, count, bytes, destination, out written, out diagnostic);
    }

    internal static EbinStatus WriteRecordArray(scoped ReadOnlySpan<EbinRecord> records, scoped Span<byte> destination, out int written, out EbinDiagnostic diagnostic)
    {
        written = 0;
        diagnostic = default;

        if (records.Length > (int)EbinCodec.MaximumArrayCount)
        {
            return EbinCodec.Fail(default, 0, "array-count-budget", out diagnostic, limit: EbinCodec.MaximumArrayCount);
        }

        _ = EbinCodec.TryAlign(8UL + (8UL * checked((ulong)records.Length + 1)), out var length);

        if (length + (64UL * (ulong)records.Length) > EbinCodec.MaximumRecordBytes)
        {
            return EbinCodec.Fail(default, 0, "record-byte-budget", out diagnostic, limit: EbinCodec.MaximumRecordBytes);
        }

        var children = new byte[records.Length][];
        var first = length;

        for (var index = 0; index < records.Length; index++)
        {
            ArgumentNullException.ThrowIfNull(records[index]);
            var status = EbinCodec.Prepare(records[index], out children[index], out diagnostic);

            if (status != EbinStatus.Complete)
            {
                return status;
            }

            status = EbinCodec.ValidateRequiredKinds(new EbinRecordView(children[index]), out diagnostic);

            if (status != EbinStatus.Complete)
            {
                return status;
            }

            length += checked((ulong)children[index].Length);

            if (length > EbinCodec.MaximumRecordBytes)
            {
                return EbinCodec.Fail(default, 0, "record-byte-budget", out diagnostic, limit: EbinCodec.MaximumRecordBytes);
            }
        }

        if (length > (ulong)destination.Length)
        {
            diagnostic = new EbinDiagnostic(default, 0, "destination-too-small", Limit: length);
            return EbinStatus.DestinationTooSmall;
        }

        var bytes = new byte[checked((int)length)];

        BinaryPrimitives.WriteUInt64LittleEndian(bytes, checked((ulong)records.Length));
        var offset = checked((int)first);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), first);

        for (var index = 0; index < records.Length; index++)
        {
            children[index].CopyTo(bytes, offset);
            offset += children[index].Length;
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16 + (index * 8)), checked((ulong)offset));
        }

        return EbinCodec.WritePayload(24, checked((ulong)records.Length), bytes, destination, out written, out diagnostic);
    }

    private static EbinField Components(uint tag, ushort kind, scoped ReadOnlySpan<float> values)
    {
        Unsafe.SkipInit(out InlineBuffer64<byte> storage); // The loop fills the entire exposed prefix before return.
        Span<byte> bytes = storage;

        for (var index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes[(index * 4)..], BitConverter.SingleToInt32Bits(values[index]));
        }

        return new EbinField(tag, kind, 1, 1, bytes[..(values.Length * 4)]);
    }
}
