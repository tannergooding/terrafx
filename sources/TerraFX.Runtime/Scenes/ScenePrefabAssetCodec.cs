// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using TerraFX;
using TerraFX.Assets;

namespace TerraFX.Scenes;

internal sealed class ScenePrefabAssetCodec : IContextualBinarySchemaCodec<ScenePrefabAsset>
{
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("native/prefab").SchemaId;

    public Guid SchemaId => s_schemaId;

    public int SchemaMajor => 2;

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

    public BinaryRecordStatus Read(
        scoped ReadOnlySpan<byte> source,
        AssetCodecContext context,
        [MaybeNull] out ScenePrefabAsset value,
        out int bytesConsumed,
        out BinarySchemaDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(context);
        value = null;
        bytesConsumed = 0;
        diagnostic = null;
        var action = new ReadAction(context);
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
            diagnostic = CreateDiagnostic("unsupported-prefab-schema");
            bytesConsumed = 0;
            return BinaryRecordStatus.UnsupportedSchema;
        }
        catch (InvalidDataException)
        {
            diagnostic = CreateDiagnostic("invalid-prefab-record");
            bytesConsumed = 0;
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
                bytesConsumed != source.Length ? "trailing-prefab-bytes" : "missing-prefab-value");
            bytesConsumed = 0;
            return BinaryRecordStatus.InvalidRecord;
        }

        value = action.Value;
        return BinaryRecordStatus.Complete;
    }

    public BinaryRecordStatus Write(
        in ScenePrefabAsset value,
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
            diagnostic = CreateDiagnostic("missing-prefab-value");
            return BinaryRecordStatus.InvalidRecord;
        }

        if (value.AssetId != context.RootAssetId)
        {
            diagnostic = CreateDiagnostic("prefab-asset-id-mismatch");
            return BinaryRecordStatus.InvalidRecord;
        }

        var encodedRecord = value.EncodedRecord.AsSpan();

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
        private readonly AssetCodecContext _context;

        internal ReadAction(AssetCodecContext context)
        {
            _context = context;
            Value = null;
        }

        internal ScenePrefabAsset? Value { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
        {
            var workspace = SceneAssetReader.CreatePrefabValidationWorkspace(
                record,
                _context.RootAssetId,
                _context.ReadLimits);
            Value = SceneAssetReader.ReadPrefab(
                record,
                _context.RootAssetId,
                _context.Schemas,
                _context.ReadLimits,
                workspace,
                maximumHierarchyDepth: 256);
        }
    }
}
