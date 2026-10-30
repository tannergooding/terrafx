// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace TerraFX;

/// <summary>Provides borrowed access to a structurally validated EBIN record.</summary>
public readonly ref struct EbinRecordReadView
{
    private readonly ReadOnlySpan<byte> _encodedBytes;

    internal EbinRecordReadView(ReadOnlySpan<byte> encodedBytes)
    {
        _encodedBytes = encodedBytes;
    }

    /// <summary>Gets the complete encoded record prefix.</summary>
    public ReadOnlySpan<byte> EncodedBytes => _encodedBytes;

    /// <summary>Gets the number of fields in the record.</summary>
    public int FieldCount => new EbinRecordView(_encodedBytes).FieldCount;

    /// <summary>Gets the validated record header.</summary>
    public EbinRecordHeader Header
    {
        get
        {
            var schemaId = new Guid(_encodedBytes.Slice(16, 16), bigEndian: true);
            var major = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(_encodedBytes[48..]));
            var minor = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(_encodedBytes[52..]));
            var recordByteLength = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(_encodedBytes[32..]));

            return new EbinRecordHeader(schemaId, major, minor, FieldCount, recordByteLength);
        }
    }

    /// <summary>Gets the field at the specified index.</summary>
    /// <param name="index">The zero-based field index.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the field range.</exception>
    public EbinFieldReadView GetField(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, FieldCount);

        var view = new EbinRecordView(_encodedBytes);
        var entry = view.Entry(index);
        return new EbinFieldReadView(
            BinaryPrimitives.ReadUInt32LittleEndian(entry),
            BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]),
            BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]),
            BinaryPrimitives.ReadUInt64LittleEndian(entry[24..]),
            view.Payload(index));
    }

    /// <summary>Gets a nested record field by tag.</summary>
    /// <param name="fieldTag">The nested record field tag.</param>
    /// <exception cref="KeyNotFoundException"><paramref name="fieldTag"/> is not present.</exception>
    /// <exception cref="InvalidOperationException">The field is not a nested record.</exception>
    public EbinRecordReadView GetRecord(uint fieldTag)
    {
        var field = GetRequiredField(fieldTag);

        if (field.Kind != 22)
        {
            throw new InvalidOperationException("The requested field is not a nested record.");
        }

        return new EbinRecordReadView(field.Payload);
    }

    /// <summary>Gets a nested record-array element by field tag and index.</summary>
    /// <param name="fieldTag">The record-array field tag.</param>
    /// <param name="elementIndex">The zero-based element index.</param>
    /// <exception cref="KeyNotFoundException"><paramref name="fieldTag"/> is not present.</exception>
    /// <exception cref="InvalidOperationException">The field is not a record array.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="elementIndex"/> is outside the array range.</exception>
    public EbinRecordReadView GetRecordArrayElement(uint fieldTag, int elementIndex)
    {
        var field = GetRequiredField(fieldTag);

        if (field.Kind != 24)
        {
            throw new InvalidOperationException("The requested field is not a record array.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(elementIndex);

        if ((ulong)elementIndex >= field.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(elementIndex));
        }

        return new EbinRecordReadView(EbinCodec.ArrayElement(field.Payload, elementIndex));
    }

    /// <summary>Attempts to get a field by tag.</summary>
    /// <param name="tag">The field tag.</param>
    /// <param name="field">The borrowed field view, or its default value when absent.</param>
    /// <returns><see langword="true"/> if the field is present.</returns>
    public bool TryGetField(uint tag, out EbinFieldReadView field)
    {
        var index = new EbinRecordView(_encodedBytes).Find(tag);

        if (index < 0)
        {
            field = default;
            return false;
        }

        field = GetField(index);
        return true;
    }

    private EbinFieldReadView GetRequiredField(uint tag)
    {
        if (!TryGetField(tag, out var field))
        {
            throw new KeyNotFoundException($"The record does not contain field tag {tag}.");
        }

        return field;
    }
}
