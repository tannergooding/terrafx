// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Assets;

internal static class AssetEnvelopeDecoder
{
    private static readonly DataSchema s_envelopeSchema = FindEnvelopeSchema();

    internal static BinaryRecordStatus Decode<T>(
        scoped ReadOnlySpan<byte> source,
        AssetId expectedAssetId,
        AssetStoreRegistration.IAssetCodec<T> codec,
        AssetCodecContext context,
        EbinReadLimits limits,
        [MaybeNull] out T value,
        out ImmutableArray<AssetReference> assetReferences,
        out BinarySchemaDiagnostic? schemaDiagnostic,
        out string? packageDiagnostic)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(limits);
        value = default;
        assetReferences = [];
        schemaDiagnostic = null;
        packageDiagnostic = null;

        var action = new DecodeAction<T>(expectedAssetId, codec, context);
        var readStatus = EbinStructuralReader.Read(
            source,
            limits,
            EbinReadPolicy.Canonical,
            ref action,
            out var bytesConsumed,
            out var readDiagnostic);

        if (readStatus is not EbinReadStatus.Complete)
        {
            packageDiagnostic = readDiagnostic?.Category;
            return readStatus switch {
                EbinReadStatus.NeedMoreData => BinaryRecordStatus.NeedMoreData,
                EbinReadStatus.UnsupportedSchema => BinaryRecordStatus.UnsupportedSchema,
                _ => BinaryRecordStatus.InvalidRecord,
            };
        }

        if (bytesConsumed != source.Length)
        {
            packageDiagnostic = "trailing-envelope-bytes";
            return BinaryRecordStatus.InvalidRecord;
        }

        if (action.Status is not BinaryRecordStatus.Complete)
        {
            schemaDiagnostic = action.SchemaDiagnostic;
            packageDiagnostic = action.PackageDiagnostic;
            return action.Status;
        }

        value = action.Value;
        assetReferences = action.AssetReferences;
        return BinaryRecordStatus.Complete;
    }

    internal static BinaryRecordStatus Inspect(
        scoped ReadOnlySpan<byte> source,
        AssetId expectedAssetId,
        CompiledContentSchemaRegistry schemas,
        EbinReadLimits limits,
        out ContentSchemaReference payloadSchema,
        out string? packageDiagnostic)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(limits);
        payloadSchema = default;
        packageDiagnostic = null;

        var action = new InspectAction(expectedAssetId, schemas);
        var readStatus = EbinStructuralReader.Read(
            source,
            limits,
            EbinReadPolicy.Canonical,
            ref action,
            out var bytesConsumed,
            out var readDiagnostic);

        if (readStatus is not EbinReadStatus.Complete)
        {
            packageDiagnostic = readDiagnostic?.Category;
            return readStatus switch {
                EbinReadStatus.NeedMoreData => BinaryRecordStatus.NeedMoreData,
                EbinReadStatus.UnsupportedSchema => BinaryRecordStatus.UnsupportedSchema,
                _ => BinaryRecordStatus.InvalidRecord,
            };
        }

        if (bytesConsumed != source.Length)
        {
            packageDiagnostic = "trailing-envelope-bytes";
            return BinaryRecordStatus.InvalidRecord;
        }

        if (action.Status is not BinaryRecordStatus.Complete)
        {
            packageDiagnostic = action.PackageDiagnostic;
            return action.Status;
        }

        payloadSchema = action.PayloadSchema;
        return BinaryRecordStatus.Complete;
    }

    private static DataSchema FindEnvelopeSchema()
    {
        foreach (var schema in NativeAssetSchemaCatalogue.Schemas)
        {
            if (StringComparer.Ordinal.Equals(schema.IdentityAssignment.Name, "native/envelope"))
            {
                return schema;
            }
        }

        throw new InvalidOperationException("The compiled native-envelope schema is missing.");
    }

    private static BinaryRecordStatus InspectEnvelope(
        scoped EbinRecordReadView envelope,
        AssetId expectedAssetId,
        CompiledContentSchemaRegistry schemas,
        out ContentSchemaReference payloadSchema,
        out DataSchema? dataSchema,
        out string? packageDiagnostic)
    {
        payloadSchema = default;
        dataSchema = null;
        packageDiagnostic = null;
        var envelopeHeader = envelope.Header;

        if ((envelopeHeader.SchemaId != s_envelopeSchema.Schema.SchemaId)
            || (envelopeHeader.Major != s_envelopeSchema.Schema.Major)
            || (envelopeHeader.Minor != s_envelopeSchema.Schema.Minor)
            || (envelope.FieldCount != s_envelopeSchema.Fields.Length))
        {
            packageDiagnostic = "native-envelope-schema";
            return BinaryRecordStatus.UnsupportedSchema;
        }

        var assetIdField = envelope.GetField(0);
        var payloadField = envelope.GetField(1);

        if (!MatchesField(assetIdField, s_envelopeSchema.Fields[0])
            || !MatchesField(payloadField, s_envelopeSchema.Fields[1])
            || (assetIdField.Payload.Length != sizeof(uint))
            || (BinaryPrimitives.ReadUInt32LittleEndian(assetIdField.Payload) != checked((uint)expectedAssetId.Value)))
        {
            packageDiagnostic = "native-envelope-fields";
            return BinaryRecordStatus.InvalidRecord;
        }

        var payloadHeader = envelope.GetRecord(2).Header;

        if ((payloadHeader.SchemaId == Guid.Empty)
            || (payloadHeader.Major <= 0)
            || (payloadHeader.Minor < 0))
        {
            packageDiagnostic = "invalid-native-asset-schema";
            return BinaryRecordStatus.InvalidRecord;
        }

        payloadSchema = new ContentSchemaReference(
            payloadHeader.SchemaId,
            payloadHeader.Major,
            payloadHeader.Minor,
            ContentSchemaScope.Content);

        if (!schemas.TryGetSchema(payloadSchema, out dataSchema)
            || (dataSchema.Role != ContentSchemaRole.NativeAsset))
        {
            packageDiagnostic = "unregistered-native-asset-schema";
            return BinaryRecordStatus.UnsupportedSchema;
        }

        return BinaryRecordStatus.Complete;
    }

    private static bool MatchesField(EbinFieldReadView field, ContentFieldDescriptor descriptor)
        => (field.Tag == descriptor.Tag)
        && (field.Kind == descriptor.WireKind)
        && (field.IsRequired == descriptor.IsRequired)
        && (field.Count >= descriptor.MinimumCount)
        && (field.Count <= descriptor.MaximumCount);

    private struct DecodeAction<T> : IAction<EbinRecordReadView>
    {
        private readonly AssetCodecContext _context;
        private readonly AssetStoreRegistration.IAssetCodec<T> _codec;
        private readonly AssetId _expectedAssetId;

        internal DecodeAction(
            AssetId expectedAssetId,
            AssetStoreRegistration.IAssetCodec<T> codec,
            AssetCodecContext context)
        {
            _expectedAssetId = expectedAssetId;
            _codec = codec;
            _context = context;
            Status = BinaryRecordStatus.InvalidRecord;
            SchemaDiagnostic = null;
            PackageDiagnostic = null;
            AssetReferences = [];
            Value = default!;
        }

        internal ImmutableArray<AssetReference> AssetReferences { get; private set; }

        internal string? PackageDiagnostic { get; private set; }

        internal BinarySchemaDiagnostic? SchemaDiagnostic { get; private set; }

        internal BinaryRecordStatus Status { get; private set; }

        [MaybeNull] internal T Value { get; private set; }

        private static bool MatchesField(EbinFieldReadView field, ContentFieldDescriptor descriptor)
            => AssetEnvelopeDecoder.MatchesField(field, descriptor);

        public void Invoke(scoped EbinRecordReadView envelope)
        {
            Status = InspectEnvelope(
                envelope,
                _expectedAssetId,
                _context.Schemas,
                out var payloadSchema,
                out var dataSchema,
                out var packageDiagnostic);
            PackageDiagnostic = packageDiagnostic;

            if (Status is not BinaryRecordStatus.Complete)
            {
                return;
            }

            if (_codec.Schema != payloadSchema)
            {
                PackageDiagnostic = "unregistered-native-asset-schema";
                Status = BinaryRecordStatus.UnsupportedSchema;
                return;
            }

            var payload = envelope.GetRecord(2);

            if (dataSchema is null)
            {
                PackageDiagnostic = "unregistered-native-asset-schema";
                Status = BinaryRecordStatus.UnsupportedSchema;
                return;
            }

            AssetReferences = AssetReferenceScanner.Scan(payload, dataSchema, _context.Schemas);

            Status = _codec.Read(payload.EncodedBytes, _context, out var value, out var consumed, out var diagnostic);

            if (Status is BinaryRecordStatus.Complete)
            {
                if (consumed != payload.EncodedBytes.Length)
                {
                    PackageDiagnostic = "native-asset-codec-consumption";
                    Status = BinaryRecordStatus.InvalidRecord;
                }
                else if (value is null)
                {
                    PackageDiagnostic = "native-asset-codec-null-value";
                    Status = BinaryRecordStatus.InvalidRecord;
                }
                else if (diagnostic is not null)
                {
                    PackageDiagnostic = "native-asset-codec-diagnostic-on-success";
                    Status = BinaryRecordStatus.InvalidRecord;
                }
                else
                {
                    Value = value!;
                }
            }
            else
            {
                SchemaDiagnostic = diagnostic;
            }
        }
    }

    private struct InspectAction : IAction<EbinRecordReadView>
    {
        private readonly CompiledContentSchemaRegistry _schemas;
        private readonly AssetId _expectedAssetId;

        internal InspectAction(AssetId expectedAssetId, CompiledContentSchemaRegistry schemas)
        {
            _expectedAssetId = expectedAssetId;
            _schemas = schemas;
            PayloadSchema = default;
            Status = BinaryRecordStatus.InvalidRecord;
        }

        internal string? PackageDiagnostic { get; private set; }

        internal ContentSchemaReference PayloadSchema { get; private set; }

        internal BinaryRecordStatus Status { get; private set; }

        public void Invoke(scoped EbinRecordReadView envelope)
        {
            Status = InspectEnvelope(
                envelope,
                _expectedAssetId,
                _schemas,
                out var payloadSchema,
                out _,
                out var diagnostic);
            PayloadSchema = payloadSchema;
            PackageDiagnostic = diagnostic;
        }
    }
}
