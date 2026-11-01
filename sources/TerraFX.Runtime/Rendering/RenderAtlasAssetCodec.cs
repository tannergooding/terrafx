// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using TerraFX;
using TerraFX.Assets;

namespace TerraFX.Rendering;

internal sealed class RenderAtlasAssetCodec : IContextualBinarySchemaCodec<RenderAtlasAsset>
{
    private static readonly Guid s_assetReferenceSchemaId = new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId;
    private static readonly Guid s_frameSchemaId = new ContentSchemaIdentityAssignment("native/atlas-frame").SchemaId;
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("native/atlas").SchemaId;
    private static readonly Guid s_textureSchemaId = new ContentSchemaIdentityAssignment("native/texture").SchemaId;

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

    private static EbinRecord CreateRecord(RenderAtlasAsset value, EbinReadLimits limits)
    {
        var pageRecords = ImmutableArray.CreateBuilder<EbinRecord>(value.Pages.Length);

        foreach (var pageId in value.Pages)
        {
            pageRecords.Add(new EbinRecord(
                s_assetReferenceSchemaId,
                1,
                0,
                [
                    EbinPrimitives.UInt32(1, checked((uint)pageId.Value)),
                    EbinPrimitives.Guid(2, s_textureSchemaId),
                    EbinPrimitives.UInt32(3, 1),
                ]));
        }

        var frameRecords = ImmutableArray.CreateBuilder<EbinRecord>(value.Frames.Length);

        foreach (var frame in value.Frames)
        {
            frameRecords.Add(new EbinRecord(
                s_frameSchemaId,
                1,
                0,
                [
                    EbinPrimitives.Guid(1, frame.FrameId),
                    EbinPrimitives.UInt32(2, checked((uint)frame.Page)),
                    EbinPrimitives.UInt32(3, checked((uint)frame.X)),
                    EbinPrimitives.UInt32(4, checked((uint)frame.Y)),
                    EbinPrimitives.UInt32(5, checked((uint)frame.Width)),
                    EbinPrimitives.UInt32(6, checked((uint)frame.Height)),
                    EbinPrimitives.UInt8(7, checked((byte)frame.Padding)),
                ]));
        }

        var pageRecordArray = pageRecords.MoveToImmutable();

        if (!RenderAssetRecordArrayWriter.TryEncode(pageRecordArray.AsSpan(), limits, out var pagePayload, out var pageError))
        {
            throw new InvalidDataException($"Atlas page references could not be encoded: {pageError ?? "unknown"}.");
        }

        var frameRecordArray = frameRecords.MoveToImmutable();

        if (!RenderAssetRecordArrayWriter.TryEncode(frameRecordArray.AsSpan(), limits, out var framePayload, out var frameError))
        {
            throw new InvalidDataException($"Atlas frames could not be encoded: {frameError ?? "unknown"}.");
        }

        return new EbinRecord(
            s_schemaId,
            1,
            0,
            [
                new EbinField(1, 24, 1, checked((ulong)value.Pages.Length), pagePayload),
                new EbinField(2, 24, 1, checked((ulong)value.Frames.Length), framePayload),
            ]);
    }

    private static EbinFieldReadView GetRequiredField(EbinRecordReadView record, uint tag, ushort kind, ulong count)
    {
        if (!record.TryGetField(tag, out var field)
            || !field.IsRequired
            || (field.Kind != kind)
            || (field.Count != count))
        {
            throw new InvalidDataException($"The Atlas record field {tag} does not match its required wire shape.");
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
            throw new InvalidDataException($"The Atlas record field {tag} does not match its required record-array shape.");
        }

        return field;
    }

    private static RenderAtlasAsset ReadAtlas(EbinRecordReadView record)
    {
        var header = record.Header;

        if ((header.SchemaId != s_schemaId) || (header.Major != 1) || (header.Minor != 0))
        {
            throw new NotSupportedException("The selected native/atlas schema version is unsupported.");
        }

        if (header.FieldCount != 2)
        {
            throw new InvalidDataException("An Atlas record must contain exactly two fields.");
        }

        var pageArray = GetRequiredRecordArray(record, tag: 1, minimumCount: 1, maximumCount: 16);
        var frameArray = GetRequiredRecordArray(record, tag: 2, minimumCount: 1, maximumCount: 1_048_576);
        var pages = ImmutableArray.CreateBuilder<AssetId>(checked((int)pageArray.Count));

        for (var index = 0; index < checked((int)pageArray.Count); index++)
        {
            pages.Add(ReadTextureReference(record.GetRecordArrayElement(1, index)));
        }

        var frames = ImmutableArray.CreateBuilder<RenderAtlasFrame>(checked((int)frameArray.Count));
        var previousFrameId = Guid.Empty;

        for (var index = 0; index < checked((int)frameArray.Count); index++)
        {
            var frameRecord = record.GetRecordArrayElement(2, index);
            var frameHeader = frameRecord.Header;

            if ((frameHeader.SchemaId != s_frameSchemaId) || (frameHeader.Major != 1) || (frameHeader.Minor != 0)
                || (frameHeader.FieldCount != 7))
            {
                throw new InvalidDataException("An Atlas frame has an invalid schema identity or field count.");
            }

            var frameId = ReadGuid(frameRecord, tag: 1);
            var page = ReadUInt32(frameRecord, tag: 2);
            var x = ReadUInt32(frameRecord, tag: 3);
            var y = ReadUInt32(frameRecord, tag: 4);
            var width = ReadUInt32(frameRecord, tag: 5);
            var height = ReadUInt32(frameRecord, tag: 6);
            var padding = ReadUInt8(frameRecord, tag: 7);

            if ((frameId == Guid.Empty)
                || ((index > 0) && (FoundationCatalogue.CompareRfc(previousFrameId, frameId) >= 0)))
            {
                throw new InvalidDataException("Atlas frame IDs must be nonempty and uniquely RFC-sorted.");
            }

            if ((page >= pageArray.Count)
                || (x > 4095)
                || (y > 4095)
                || (width is 0 or > 4096)
                || (height is 0 or > 4096)
                || (padding != 2)
                || (x < padding)
                || (y < padding)
                || (checked((ulong)x + width + padding) > 4096)
                || (checked((ulong)y + height + padding) > 4096))
            {
                throw new InvalidDataException("An Atlas frame exceeds the admitted page, padding, or frame bounds.");
            }

            frames.Add(new RenderAtlasFrame(
                frameId,
                checked((int)page),
                checked((int)x),
                checked((int)y),
                checked((int)width),
                checked((int)height),
                padding));
            previousFrameId = frameId;
        }

        return new RenderAtlasAsset(pages.MoveToImmutable(), frames.MoveToImmutable());
    }

    private static Guid ReadGuid(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 11, count: 1);

        if (field.Payload.Length != 16)
        {
            throw new InvalidDataException($"The Atlas record field {tag} does not contain one GUID.");
        }

        return new Guid(field.Payload, bigEndian: true);
    }

    private static AssetId ReadTextureReference(EbinRecordReadView record)
    {
        var header = record.Header;

        if ((header.SchemaId != s_assetReferenceSchemaId) || (header.Major != 1) || (header.Minor != 0)
            || (header.FieldCount != 3))
        {
            throw new InvalidDataException("An Atlas page reference has an invalid asset-reference schema.");
        }

        var assetIdValue = ReadUInt32(record, tag: 1);
        var schemaId = ReadGuid(record, tag: 2);
        var schemaMajor = ReadUInt32(record, tag: 3);

        if ((assetIdValue is 0 or > int.MaxValue)
            || (schemaId != s_textureSchemaId)
            || (schemaMajor != 1))
        {
            throw new InvalidDataException("An Atlas page reference must identify a native/texture 1 asset.");
        }

        return new AssetId(checked((int)assetIdValue));
    }

    private static uint ReadUInt32(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 3, count: 1);

        if (field.Payload.Length != sizeof(uint))
        {
            throw new InvalidDataException($"The Atlas record field {tag} does not contain one UInt32.");
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(field.Payload);
    }

    private static byte ReadUInt8(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 1, count: 1);

        if (field.Payload.Length != sizeof(byte))
        {
            throw new InvalidDataException($"The Atlas record field {tag} does not contain one UInt8.");
        }

        return field.Payload[0];
    }

    public BinaryRecordStatus Read(
        scoped ReadOnlySpan<byte> source,
        AssetCodecContext context,
        [MaybeNull] out RenderAtlasAsset value,
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
            diagnostic = CreateDiagnostic("unsupported-atlas-schema");
            return BinaryRecordStatus.UnsupportedSchema;
        }
        catch (InvalidDataException)
        {
            diagnostic = CreateDiagnostic("invalid-atlas-record");
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
                bytesConsumed != source.Length ? "trailing-atlas-bytes" : "missing-atlas-value");
            bytesConsumed = 0;
            return BinaryRecordStatus.InvalidRecord;
        }

        value = action.Value;
        return BinaryRecordStatus.Complete;
    }

    public BinaryRecordStatus Write(
        in RenderAtlasAsset value,
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
            diagnostic = CreateDiagnostic("missing-atlas-value");
            return BinaryRecordStatus.InvalidRecord;
        }

        byte[] encodedRecord;

        try
        {
            var record = CreateRecord(value, context.ReadLimits);
            var status = EbinCodec.Prepare(record, out encodedRecord, out var ebinDiagnostic);

            if (status is not EbinStatus.Complete)
            {
                diagnostic = CreateDiagnostic($"atlas-encode-{ebinDiagnostic.Category}");
                return BinaryRecordStatus.InvalidRecord;
            }
        }
        catch (InvalidDataException)
        {
            diagnostic = CreateDiagnostic("invalid-atlas-value");
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
        internal RenderAtlasAsset? Value { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
            => Value = ReadAtlas(record);
    }
}
