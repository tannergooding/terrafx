// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using TerraFX.Assets;

namespace TerraFX.Rendering;

internal sealed class RenderMeshAsset2Codec : IContextualBinarySchemaCodec<RenderMeshAsset2>
{
    private const int MaximumRecordBytes = 268_435_456;
    private static readonly Guid s_materialSchemaId = new ContentSchemaIdentityAssignment("native/material").SchemaId;
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("native/mesh").SchemaId;
    private static readonly Guid s_skeletonSchemaId = new ContentSchemaIdentityAssignment("native/skeleton").SchemaId;

    public Guid SchemaId => s_schemaId;

    public int SchemaMajor => 2;

    public int SchemaMinor => 0;

    private static BinarySchemaDiagnostic CreateDiagnostic(string category)
        => new(s_schemaId, 0, [], category, null, null);

    private static void Preflight(EbinRecordReadView record, AssetCodecContext context)
    {
        RenderM05SchemaReader.Validate(
            record,
            context.Schemas,
            new ContentSchemaReference(s_schemaId, 2, 0, ContentSchemaScope.Content));
        _ = record.TryGetField(1, out var vertexField);
        var vertexCount = BinaryPrimitives.ReadUInt32LittleEndian(vertexField.Payload);

        if (vertexCount is 0 or > 262_144)
        {
            throw new InvalidDataException("Mesh VertexCount must be in [1,262144].");
        }

        _ = record.TryGetField(4, out var groups);
        _ = record.TryGetField(5, out var materials);
        _ = record.TryGetField(8, out var additionalLevels);
        _ = record.TryGetField(9, out var thresholds);

        if ((BinaryPrimitives.ReadUInt16LittleEndian(thresholds.Payload[16..]) != 10)
            || (BinaryPrimitives.ReadUInt32LittleEndian(thresholds.Payload[20..]) != sizeof(float))
            || (thresholds.Payload.Length != checked(32 + (checked((int)thresholds.Count) * sizeof(float)))))
        {
            throw new InvalidDataException("Mesh LOD thresholds require the exact binary32 fixed-array shape.");
        }

        if (thresholds.Count != additionalLevels.Count)
        {
            throw new InvalidDataException("Mesh LOD thresholds must have exactly one entry per additional level.");
        }

        var previousThreshold = float.PositiveInfinity;

        for (var index = 0; index < checked((int)thresholds.Count); index++)
        {
            var threshold = EbinPrimitives.Single(thresholds.Payload[(32 + (index * sizeof(float)))..]);

            if (!float.IsFinite(threshold) || (threshold <= 0.0f) || (threshold >= previousThreshold))
            {
                throw new InvalidDataException("Mesh LOD thresholds must be finite, positive, and strictly decreasing.");
            }

            previousThreshold = threshold;
        }

        var totalIndices = RenderMeshAssetCodec.PreflightSharedIndices(record, 3);
        var totalGroups = groups.Count;

        for (var index = 0; index < checked((int)additionalLevels.Count); index++)
        {
            var level = record.GetRecordArrayElement(8, index);
            _ = level.TryGetField(2, out var levelGroups);

            if (levelGroups.Count != groups.Count)
            {
                throw new InvalidDataException("Every Mesh LOD must preserve the logical group count.");
            }

            totalIndices = checked(totalIndices + RenderMeshAssetCodec.PreflightSharedIndices(level, 1));
            totalGroups = checked(totalGroups + levelGroups.Count);
        }

        if ((totalIndices > 1_048_576) || (totalGroups > 65_536))
        {
            throw new InvalidDataException("Mesh LOD aggregate indices or groups exceed their finite limits.");
        }

        var decodedBytes = checked((ulong)record.EncodedBytes.Length
            + (totalIndices * sizeof(uint))
            + (totalGroups * checked((ulong)Unsafe.SizeOf<RenderMeshGroup>()))
            + ((additionalLevels.Count + 1) * checked((ulong)Unsafe.SizeOf<RenderMeshLodLevel>()))
            + (materials.Count * checked((ulong)Unsafe.SizeOf<RenderMeshMaterialSlot>()))
            + (thresholds.Count * sizeof(float)));
        _ = record.TryGetField(2, out var streams);

        for (var index = 0; index < checked((int)streams.Count); index++)
        {
            var stream = record.GetRecordArrayElement(2, index);

            for (var fieldIndex = 0; fieldIndex < stream.FieldCount; fieldIndex++)
            {
                var field = stream.GetField(fieldIndex);

                if (field.Kind == 23)
                {
                    decodedBytes = checked(decodedBytes + checked((ulong)field.Payload.Length - 32));
                }
            }
        }

        if (decodedBytes > checked((ulong)Math.Min(MaximumRecordBytes, context.ReadLimits.MaximumRecordBytes)))
        {
            throw new InvalidDataException("The complete decoded Mesh LOD arrays and retained encoding exceed the asset byte limit.");
        }
    }

    private static RenderMeshAsset2 ReadMesh(EbinRecordReadView record, AssetCodecContext context)
    {
        Preflight(record, context);
        _ = record.TryGetField(1, out var vertexField);
        var vertexCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(vertexField.Payload));
        var vertices = RenderMeshAssetCodec.ReadSharedStreams(record, vertexCount);
        _ = record.TryGetField(5, out var materials);
        var materialSlots = ImmutableArray.CreateBuilder<RenderMeshMaterialSlot>(checked((int)materials.Count));

        for (var index = 0; index < checked((int)materials.Count); index++)
        {
            var (assetId, major) = ReadReference(record.GetRecordArrayElement(5, index), s_materialSchemaId, context);
            materialSlots.Add(new RenderMeshMaterialSlot(assetId, major));
        }

        AssetId? skeletonAssetId = null;

        if (record.TryGetField(7, out _))
        {
            (skeletonAssetId, _) = ReadReference(record.GetRecord(7), s_skeletonSchemaId, context);
        }

        if ((vertices.JointIndices.IsDefault != vertices.Weights.IsDefault)
            || (vertices.JointIndices.IsDefault != !skeletonAssetId.HasValue))
        {
            throw new InvalidDataException("Mesh joints, weights, and Skeleton reference must be present together.");
        }

        var bounds = RenderMeshAssetCodec.ReadSharedBounds(record, vertices);
        _ = record.TryGetField(8, out var additionalLevels);
        var levels = ImmutableArray.CreateBuilder<RenderMeshLodLevel>(checked((int)additionalLevels.Count + 1));
        var finest = RenderMeshAssetCodec.ReadSharedLevel(record, 3, 4, vertices, materialSlots.Count);
        levels.Add(finest);

        for (var index = 0; index < checked((int)additionalLevels.Count); index++)
        {
            var level = RenderMeshAssetCodec.ReadSharedLevel(
                record.GetRecordArrayElement(8, index),
                1,
                2,
                vertices,
                materialSlots.Count);

            for (var groupIndex = 0; groupIndex < finest.Groups.Length; groupIndex++)
            {
                if (level.Groups[groupIndex].MaterialSlot != finest.Groups[groupIndex].MaterialSlot)
                {
                    throw new InvalidDataException("Every Mesh LOD must preserve the group-index to material-slot mapping.");
                }
            }

            levels.Add(level);
        }

        _ = record.TryGetField(9, out var thresholdsField);
        var thresholds = ImmutableArray.CreateBuilder<float>(checked((int)thresholdsField.Count));

        for (var index = 0; index < checked((int)thresholdsField.Count); index++)
        {
            thresholds.Add(EbinPrimitives.Single(thresholdsField.Payload[(32 + (index * sizeof(float)))..]));
        }

        return new RenderMeshAsset2(
            context.RootAssetId,
            vertexCount,
            vertices,
            levels.MoveToImmutable(),
            thresholds.MoveToImmutable(),
            materialSlots.MoveToImmutable(),
            skeletonAssetId,
            bounds,
            [.. record.EncodedBytes]);
    }

    private static (AssetId AssetId, int Major) ReadReference(
        EbinRecordReadView record,
        Guid expectedSchema,
        AssetCodecContext context)
    {
        _ = record.TryGetField(1, out var idField);
        _ = record.TryGetField(2, out var schemaField);
        _ = record.TryGetField(3, out var majorField);
        var id = BinaryPrimitives.ReadUInt32LittleEndian(idField.Payload);
        var schemaId = new Guid(schemaField.Payload, bigEndian: true);
        var major = BinaryPrimitives.ReadUInt32LittleEndian(majorField.Payload);

        if ((id is 0 or > int.MaxValue) || (schemaId != expectedSchema)
            || ((expectedSchema == s_materialSchemaId) ? major is not 1 and not 2 : major != 1))
        {
            throw new InvalidDataException("A Mesh 2.0 reference does not select an admitted native schema major.");
        }

        var reference = new ContentSchemaReference(schemaId, checked((int)major), 0, ContentSchemaScope.Content);

        if (!context.Schemas.TryGetSchema(reference, out var schema) || (schema.Role != ContentSchemaRole.NativeAsset))
        {
            throw new NotSupportedException("A Mesh 2.0 reference selects an absent exact native asset schema.");
        }

        return (new AssetId(checked((int)id)), checked((int)major));
    }

    public BinaryRecordStatus Read(
        scoped ReadOnlySpan<byte> source,
        AssetCodecContext context,
        [MaybeNull] out RenderMeshAsset2 value,
        out int bytesConsumed,
        out BinarySchemaDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(context);
        value = null;
        bytesConsumed = 0;
        diagnostic = null;

        if (source.Length > MaximumRecordBytes)
        {
            diagnostic = CreateDiagnostic("mesh-lod-record-byte-limit");
            return BinaryRecordStatus.InvalidRecord;
        }

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
                diagnostic = CreateDiagnostic(readDiagnostic?.Category ?? "invalid-mesh-lod-record");
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
            diagnostic = CreateDiagnostic("unsupported-mesh-lod-schema");
            bytesConsumed = 0;
            return BinaryRecordStatus.UnsupportedSchema;
        }
        catch (InvalidDataException)
        {
            diagnostic = CreateDiagnostic("invalid-mesh-lod-record");
            bytesConsumed = 0;
            return BinaryRecordStatus.InvalidRecord;
        }

        if ((bytesConsumed != source.Length) || (action.Value is null))
        {
            diagnostic = CreateDiagnostic("incomplete-mesh-lod-value");
            bytesConsumed = 0;
            return BinaryRecordStatus.InvalidRecord;
        }

        value = action.Value;
        return BinaryRecordStatus.Complete;
    }

    public BinaryRecordStatus Write(
        in RenderMeshAsset2 value,
        AssetCodecContext context,
        scoped Span<byte> destination,
        out int bytesWritten,
        out BinarySchemaDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(context);
        bytesWritten = 0;
        diagnostic = null;

        if ((value is null) || (value.AssetId != context.RootAssetId))
        {
            diagnostic = CreateDiagnostic("invalid-mesh-lod-value");
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

        internal RenderMeshAsset2? Value { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
            => Value = ReadMesh(record, _context);
    }
}
