// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;

namespace TerraFX;

// Used only after structural scanning. Prefix validation accesses admitted ranges only.
internal readonly ref struct EbinRecordView
{
    private readonly ReadOnlySpan<byte> _bytes;

    internal EbinRecordView(ReadOnlySpan<byte> bytes)
    {
        _bytes = bytes;
    }

    internal ReadOnlySpan<byte> Bytes => _bytes;
    internal int FieldCount => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(_bytes[12..]));
    internal uint Major => BinaryPrimitives.ReadUInt32LittleEndian(_bytes[48..]);
    internal uint Minor => BinaryPrimitives.ReadUInt32LittleEndian(_bytes[52..]);
    internal Guid SchemaId => new Guid(_bytes.Slice(16, 16), bigEndian: true);

    internal ReadOnlySpan<byte> Entry(int index) => _bytes.Slice(64 + (index * 32), 32);

    internal ReadOnlySpan<byte> Field(uint tag) => Payload(Find(tag));

    internal int Find(uint tag)
    {
        for (var index = 0; index < FieldCount; index++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(Entry(index)) == tag)
            {
                return index;
            }
        }

        return -1;
    }

    internal bool IsCanonical()
    {
        var cursor = checked((ulong)(64 + (FieldCount * 32)));

        for (var index = 0; index < FieldCount; index++)
        {
            var entry = Entry(index);
            _ = EbinCodec.TryAlign(cursor, out cursor);

            if (BinaryPrimitives.ReadUInt64LittleEndian(entry[8..]) != cursor)
            {
                return false;
            }

            cursor += BinaryPrimitives.ReadUInt64LittleEndian(entry[16..]);
        }

        _ = EbinCodec.TryAlign(cursor, out cursor);
        return cursor == BinaryPrimitives.ReadUInt64LittleEndian(_bytes[32..]);
    }

    internal ReadOnlySpan<byte> Payload(int index)
    {
        var entry = Entry(index);
        return _bytes.Slice(checked((int)BinaryPrimitives.ReadUInt64LittleEndian(entry[8..])), checked((int)BinaryPrimitives.ReadUInt64LittleEndian(entry[16..])));
    }
}
