// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using TerraFX.Assets;

namespace TerraFX.Rendering;

internal sealed class RenderMaterialAsset2Codec : IContextualBinarySchemaCodec<RenderMaterialAsset2>
{
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("native/material").SchemaId;

    public Guid SchemaId => s_schemaId;

    public int SchemaMajor => 2;

    public int SchemaMinor => 0;

    private static BinarySchemaDiagnostic CreateDiagnostic(string category)
        => new(s_schemaId, 0, [], category, null, null);

    public BinaryRecordStatus Read(
        scoped ReadOnlySpan<byte> source,
        AssetCodecContext context,
        [MaybeNull] out RenderMaterialAsset2 value,
        out int bytesConsumed,
        out BinarySchemaDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(context);
        value = null;
        bytesConsumed = 0;
        diagnostic = null;
        var action = new ReadAction(context);

        try
        {
            var status = EbinStructuralReader.Read(
                source,
                context.ReadLimits,
                EbinReadPolicy.Canonical,
                ref action,
                out bytesConsumed,
                out var readDiagnostic);

            if (status is not EbinReadStatus.Complete)
            {
                diagnostic = CreateDiagnostic(readDiagnostic?.Category ?? "invalid-material-2-record");
                bytesConsumed = 0;
                return status switch {
                    EbinReadStatus.NeedMoreData => BinaryRecordStatus.NeedMoreData,
                    EbinReadStatus.UnsupportedSchema => BinaryRecordStatus.UnsupportedSchema,
                    _ => BinaryRecordStatus.InvalidRecord,
                };
            }
        }
        catch (NotSupportedException)
        {
            diagnostic = CreateDiagnostic("unsupported-material-2-schema");
            bytesConsumed = 0;
            return BinaryRecordStatus.UnsupportedSchema;
        }
        catch (InvalidDataException)
        {
            diagnostic = CreateDiagnostic("invalid-material-2-record");
            bytesConsumed = 0;
            return BinaryRecordStatus.InvalidRecord;
        }

        if ((bytesConsumed != source.Length) || (action.Value is null))
        {
            diagnostic = CreateDiagnostic("incomplete-material-2-value");
            bytesConsumed = 0;
            return BinaryRecordStatus.InvalidRecord;
        }

        value = action.Value;
        return BinaryRecordStatus.Complete;
    }

    public BinaryRecordStatus Write(
        in RenderMaterialAsset2 value,
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
            diagnostic = CreateDiagnostic("missing-material-2-value");
            return BinaryRecordStatus.InvalidRecord;
        }

        if (destination.Length < value.EncodedRecord.Length)
        {
            return BinaryRecordStatus.DestinationTooSmall;
        }

        value.EncodedRecord.AsSpan().CopyTo(destination);
        bytesWritten = value.EncodedRecord.Length;
        return BinaryRecordStatus.Complete;
    }

    private struct ReadAction : IAction<EbinRecordReadView>
    {
        private readonly AssetCodecContext _context;

        internal ReadAction(AssetCodecContext context)
        {
            _context = context;
            Value = null;
        }

        internal RenderMaterialAsset2? Value { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
        {
            RenderM05SchemaReader.Validate(
                record,
                _context.Schemas,
                new ContentSchemaReference(s_schemaId, 2, 0, ContentSchemaScope.Content));
            _ = record.TryGetField(14, out var cutoffField);
            var cutoff = EbinPrimitives.Single(cutoffField.Payload);

            if (!float.IsFinite(cutoff) || (cutoff < 0.0f) || (cutoff > 1.0f))
            {
                throw new InvalidDataException("Material 2.0 PickAlphaCutoff must be finite and in [0,1].");
            }

            Value = new RenderMaterialAsset2(
                RenderMaterialAssetCodec.ReadSharedData(record),
                cutoff,
                [.. record.EncodedBytes]);
        }
    }
}
