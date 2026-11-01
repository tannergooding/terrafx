// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using TerraFX;
using TerraFX.Assets;

namespace TerraFX.Rendering;

internal sealed class RenderTextureAssetCodec : IContextualBinarySchemaCodec<RenderTextureAsset>
{
    private static readonly Guid s_mipSchemaId = new ContentSchemaIdentityAssignment("native/texture-mip").SchemaId;
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("native/texture").SchemaId;

    public Guid SchemaId => s_schemaId;

    public int SchemaMajor => 1;

    public int SchemaMinor => 0;

    private static BinarySchemaDiagnostic? CreateDiagnostic(EbinReadDiagnostic? readDiagnostic)
    {
        if (readDiagnostic is not EbinReadDiagnostic diagnostic)
        {
            return null;
        }

        var schemaId = diagnostic.SchemaId == Guid.Empty ? s_schemaId : diagnostic.SchemaId;
        return new BinarySchemaDiagnostic(
            schemaId,
            diagnostic.Tag,
            [],
            diagnostic.Category,
            diagnostic.Claim,
            diagnostic.Limit);
    }

    private static BinarySchemaDiagnostic CreateDiagnostic(string category)
        => new(s_schemaId, 0, [], category, null, null);

    private static EbinRecord CreateRecord(RenderTextureAsset value, EbinReadLimits limits)
    {
        var mipRecords = ImmutableArray.CreateBuilder<EbinRecord>(value.Mips.Length);

        foreach (var mip in value.Mips)
        {
            mipRecords.Add(new EbinRecord(
                s_mipSchemaId,
                1,
                0,
                [
                    EbinPrimitives.UInt32(1, checked((uint)mip.Level)),
                    EbinPrimitives.UInt32(2, checked((uint)mip.Width)),
                    EbinPrimitives.UInt32(3, checked((uint)mip.Height)),
                    EbinPrimitives.Bytes(4, mip.Pixels.AsSpan()),
                ]));
        }

        if (!TryEncodeRecordArray(mipRecords.MoveToImmutable(), limits, out var mipPayload, out var diagnostic))
        {
            throw new InvalidDataException($"Texture mip records could not be encoded: {diagnostic?.Category ?? "unknown"}.");
        }

        var mipField = new EbinField(8, 24, 1, checked((ulong)value.Mips.Length), mipPayload);
        return new EbinRecord(
            s_schemaId,
            1,
            0,
            [
                EbinPrimitives.UInt32(1, checked((uint)value.Width)),
                EbinPrimitives.UInt32(2, checked((uint)value.Height)),
                EbinPrimitives.UInt8(3, value.Format),
                EbinPrimitives.UInt8(4, (byte)value.ColorSpace),
                EbinPrimitives.UInt8(5, (byte)value.AlphaMode),
                EbinPrimitives.UInt8(6, (byte)value.Semantic),
                EbinPrimitives.UInt8(7, (byte)value.MipPolicy),
                mipField,
            ]);
    }

    private static EbinFieldReadView GetRequiredField(EbinRecordReadView record, uint tag, ushort kind, ulong count)
    {
        if (!record.TryGetField(tag, out var field)
            || !field.IsRequired
            || (field.Kind != kind)
            || (field.Count != count))
        {
            throw new InvalidDataException($"The Texture record field {tag} does not match its required wire shape.");
        }

        return field;
    }

    private static EbinFieldReadView GetRequiredRecordArray(
        EbinRecordReadView record,
        uint tag,
        ulong minimumCount,
        ulong maximumCount)
    {
        if (!record.TryGetField(tag, out var field)
            || !field.IsRequired
            || (field.Kind != 24)
            || (field.Count < minimumCount)
            || (field.Count > maximumCount))
        {
            throw new InvalidDataException($"The Texture record field {tag} does not match its required record-array shape.");
        }

        return field;
    }

    private static RenderTextureAsset ReadTexture(EbinRecordReadView record)
    {
        var header = record.Header;

        if ((header.SchemaId != s_schemaId) || (header.Major != 1) || (header.Minor != 0))
        {
            throw new NotSupportedException("The selected native/texture schema version is unsupported.");
        }

        if (header.FieldCount != 8)
        {
            throw new InvalidDataException("A Texture record must contain exactly eight fields.");
        }

        var widthValue = ReadUInt32(record, tag: 1);
        var heightValue = ReadUInt32(record, tag: 2);

        if ((widthValue is 0 or > 4096) || (heightValue is 0 or > 4096))
        {
            throw new InvalidDataException("Texture dimensions must be in [1,4096].");
        }

        var format = ReadUInt8(record, tag: 3);
        var colorSpaceValue = ReadUInt8(record, tag: 4);
        var alphaModeValue = ReadUInt8(record, tag: 5);
        var semanticValue = ReadUInt8(record, tag: 6);
        var mipPolicyValue = ReadUInt8(record, tag: 7);

        if (format != 0)
        {
            throw new InvalidDataException("Only RGBA8 Texture assets are admitted.");
        }

        if (colorSpaceValue > (byte)RenderTextureColorSpace.Srgb
            || alphaModeValue > (byte)RenderTextureAlphaMode.Premultiplied
            || semanticValue > (byte)RenderTextureSemantic.Data
            || mipPolicyValue > (byte)RenderTextureMipPolicy.FullChain)
        {
            throw new InvalidDataException("The Texture color, alpha, semantic, or mip-policy value is unsupported.");
        }

        var colorSpace = (RenderTextureColorSpace)colorSpaceValue;
        var alphaMode = (RenderTextureAlphaMode)alphaModeValue;
        var semantic = (RenderTextureSemantic)semanticValue;
        var mipPolicy = (RenderTextureMipPolicy)mipPolicyValue;

        if ((semantic is RenderTextureSemantic.Normal or RenderTextureSemantic.Data)
            && ((colorSpace is not RenderTextureColorSpace.Linear)
                || (alphaMode is not RenderTextureAlphaMode.Straight)))
        {
            throw new InvalidDataException("Normal and Data Texture assets must be Linear and Straight alpha.");
        }

        var mipArray = GetRequiredRecordArray(record, tag: 8, minimumCount: 1, maximumCount: 13);
        var mipCount = checked((int)mipArray.Count);
        var width = checked((int)widthValue);
        var height = checked((int)heightValue);
        var fullMipCount = 1;

        for (var dimension = Math.Max(width, height); dimension > 1; dimension >>= 1)
        {
            fullMipCount++;
        }

        var expectedMipCount = (mipPolicy is RenderTextureMipPolicy.BaseOnly) ? 1 : fullMipCount;

        if (mipCount != expectedMipCount)
        {
            throw new InvalidDataException("The Texture mip count does not match its declared MipPolicy.");
        }

        var mips = ImmutableArray.CreateBuilder<RenderTextureMip>(mipCount);
        var mipWidth = width;
        var mipHeight = height;

        for (var index = 0; index < mipCount; index++)
        {
            var mipRecord = record.GetRecordArrayElement(8, index);
            var mipHeader = mipRecord.Header;

            if ((mipHeader.SchemaId != s_mipSchemaId) || (mipHeader.Major != 1) || (mipHeader.Minor != 0)
                || (mipHeader.FieldCount != 4))
            {
                throw new InvalidDataException("A Texture mip record has an invalid schema identity or field count.");
            }

            var level = ReadUInt32(mipRecord, tag: 1);
            var storedWidth = ReadUInt32(mipRecord, tag: 2);
            var storedHeight = ReadUInt32(mipRecord, tag: 3);
            var expectedPixelBytes = checked(mipWidth * mipHeight * 4);
            var pixels = GetRequiredField(mipRecord, tag: 4, kind: 13, count: checked((ulong)expectedPixelBytes));

            if ((level != (uint)index)
                || (storedWidth != (uint)mipWidth)
                || (storedHeight != (uint)mipHeight)
                || (pixels.Payload.Length != expectedPixelBytes))
            {
                throw new InvalidDataException("A Texture mip does not match its exact level, dimensions, or RGBA8 payload length.");
            }

            mips.Add(new RenderTextureMip(
                index,
                mipWidth,
                mipHeight,
                [.. pixels.Payload]));
            mipWidth = Math.Max(1, mipWidth / 2);
            mipHeight = Math.Max(1, mipHeight / 2);
        }

        return new RenderTextureAsset(
            width,
            height,
            format,
            colorSpace,
            alphaMode,
            semantic,
            mipPolicy,
            mips.MoveToImmutable());
    }

    private static uint ReadUInt32(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 3, count: 1);

        if (field.Payload.Length != sizeof(uint))
        {
            throw new InvalidDataException($"The Texture record field {tag} does not contain one UInt32.");
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(field.Payload);
    }

    private static byte ReadUInt8(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 1, count: 1);

        if (field.Payload.Length != sizeof(byte))
        {
            throw new InvalidDataException($"The Texture record field {tag} does not contain one UInt8.");
        }

        return field.Payload[0];
    }

    private static bool TryEncodeRecordArray(
        ImmutableArray<EbinRecord> records,
        EbinReadLimits limits,
        out byte[] payload,
        out BinarySchemaDiagnostic? diagnostic)
    {
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
                diagnostic = CreateDiagnostic($"texture-mip-encode-{ebinDiagnostic.Category}");
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
            diagnostic = CreateDiagnostic($"texture-mip-array-{structuralDiagnostic?.Category ?? measureStatus.ToString()}");
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
            diagnostic = CreateDiagnostic($"texture-mip-array-{structuralDiagnostic?.Category ?? writeStatus.ToString()}");
            return false;
        }

        if (bytesWritten != payload.Length)
        {
            payload = [];
            diagnostic = CreateDiagnostic("texture-mip-array-length-mismatch");
            return false;
        }

        return true;
    }

    public BinaryRecordStatus Read(
        scoped ReadOnlySpan<byte> source,
        AssetCodecContext context,
        [MaybeNull] out RenderTextureAsset value,
        out int bytesConsumed,
        out BinarySchemaDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(context);
        value = null;
        bytesConsumed = 0;
        diagnostic = null;
        var action = new ReadAction();
        EbinReadStatus readStatus;
        EbinReadDiagnostic? readDiagnostic;

        try
        {
            readStatus = EbinStructuralReader.Read(
                source,
                context.ReadLimits,
                EbinReadPolicy.Canonical,
                ref action,
                out bytesConsumed,
                out readDiagnostic);
        }
        catch (NotSupportedException)
        {
            diagnostic = CreateDiagnostic("unsupported-texture-schema");
            return BinaryRecordStatus.UnsupportedSchema;
        }
        catch (InvalidDataException)
        {
            diagnostic = CreateDiagnostic("invalid-texture-record");
            return BinaryRecordStatus.InvalidRecord;
        }

        if (readStatus is not EbinReadStatus.Complete)
        {
            diagnostic = CreateDiagnostic(readDiagnostic);
            bytesConsumed = 0;
            return readStatus switch {
                EbinReadStatus.NeedMoreData => BinaryRecordStatus.NeedMoreData,
                EbinReadStatus.UnsupportedSchema => BinaryRecordStatus.UnsupportedSchema,
                _ => BinaryRecordStatus.InvalidRecord,
            };
        }

        if ((bytesConsumed != source.Length) || (action.Value is null))
        {
            diagnostic = CreateDiagnostic(
                bytesConsumed != source.Length ? "trailing-texture-bytes" : "missing-texture-value");
            bytesConsumed = 0;
            return BinaryRecordStatus.InvalidRecord;
        }

        value = action.Value;
        return BinaryRecordStatus.Complete;
    }

    public BinaryRecordStatus Write(
        in RenderTextureAsset value,
        AssetCodecContext context,
        scoped Span<byte> destination,
        out int bytesWritten,
        out BinarySchemaDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(context);
        bytesWritten = 0;
        diagnostic = null;

        if (value is null)
        {
            diagnostic = CreateDiagnostic("missing-texture-value");
            return BinaryRecordStatus.InvalidRecord;
        }

        byte[] encodedRecord;

        try
        {
            var record = CreateRecord(value, context.ReadLimits);
            var status = EbinCodec.Prepare(record, out encodedRecord, out var ebinDiagnostic);

            if (status is not EbinStatus.Complete)
            {
                diagnostic = CreateDiagnostic($"texture-encode-{ebinDiagnostic.Category}");
                return BinaryRecordStatus.InvalidRecord;
            }
        }
        catch (InvalidDataException)
        {
            diagnostic = CreateDiagnostic("invalid-texture-value");
            return BinaryRecordStatus.InvalidRecord;
        }

        if (destination.Length < encodedRecord.Length)
        {
            return BinaryRecordStatus.DestinationTooSmall;
        }

        encodedRecord.CopyTo(destination);
        bytesWritten = encodedRecord.Length;
        return BinaryRecordStatus.Complete;
    }

    private struct ReadAction : IAction<EbinRecordReadView>
    {
        internal RenderTextureAsset? Value { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
            => Value = ReadTexture(record);
    }
}
