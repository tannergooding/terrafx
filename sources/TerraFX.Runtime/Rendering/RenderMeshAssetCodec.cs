// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal sealed partial class RenderMeshAssetCodec : IContextualBinarySchemaCodec<RenderMeshAsset>
{
    // Three squared 278-bit binary32 deltas require at most 558 bits.
    private const int ExactDyadicLimbCount = 9;
    private const int FixedArrayHeaderBytes = 32;
    private const float UnitVectorTolerance = 2.0e-6f;

    private static readonly Guid s_assetReferenceSchemaId = new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId;
    private static readonly Guid s_boundsSchemaId = new ContentSchemaIdentityAssignment("native/bounds3").SchemaId;
    private static readonly Guid s_materialSchemaId = new ContentSchemaIdentityAssignment("native/material").SchemaId;
    private static readonly Guid s_meshColorSchemaId = new ContentSchemaIdentityAssignment("native/mesh-color").SchemaId;
    private static readonly Guid s_meshGroupSchemaId = new ContentSchemaIdentityAssignment("native/mesh-group").SchemaId;
    private static readonly Guid s_meshIndices16SchemaId = new ContentSchemaIdentityAssignment("native/mesh-indices16").SchemaId;
    private static readonly Guid s_meshIndices32SchemaId = new ContentSchemaIdentityAssignment("native/mesh-indices32").SchemaId;
    private static readonly Guid s_meshJointsSchemaId = new ContentSchemaIdentityAssignment("native/mesh-joints").SchemaId;
    private static readonly Guid s_meshNormalSchemaId = new ContentSchemaIdentityAssignment("native/mesh-normal").SchemaId;
    private static readonly Guid s_meshPositionSchemaId = new ContentSchemaIdentityAssignment("native/mesh-position").SchemaId;
    private static readonly Guid s_meshTangentSchemaId = new ContentSchemaIdentityAssignment("native/mesh-tangent").SchemaId;
    private static readonly Guid s_meshUvSchemaId = new ContentSchemaIdentityAssignment("native/mesh-uv").SchemaId;
    private static readonly Guid s_meshWeightsSchemaId = new ContentSchemaIdentityAssignment("native/mesh-weights").SchemaId;
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("native/mesh").SchemaId;
    private static readonly Guid s_skeletonSchemaId = new ContentSchemaIdentityAssignment("native/skeleton").SchemaId;

    public Guid SchemaId => s_schemaId;

    public int SchemaMajor => 1;

    public int SchemaMinor => 0;

    private static BinarySchemaDiagnostic CreateDiagnostic(EbinReadDiagnostic? readDiagnostic)
    {
        if (readDiagnostic is not EbinReadDiagnostic diagnostic)
        {
            return CreateDiagnostic("invalid-mesh-record");
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

    private static EbinFieldReadView GetRequiredArrayField(EbinRecordReadView record, uint tag)
    {
        if (!record.TryGetField(tag, out var field) || !field.IsRequired)
        {
            throw new InvalidDataException($"Mesh record field {tag} does not contain a required array.");
        }

        return field;
    }

    private static EbinFieldReadView GetRequiredField(
        EbinRecordReadView record,
        uint tag,
        ushort kind,
        ulong count = 1)
    {
        if (!record.TryGetField(tag, out var field)
            || !field.IsRequired
            || (field.Kind != kind)
            || (field.Count != count))
        {
            throw new InvalidDataException($"Mesh record field {tag} does not match its required wire shape.");
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
            throw new InvalidDataException($"Mesh record field {tag} does not match its required record-array shape.");
        }

        return field;
    }

    private static bool IsUnitVector(Vector3 value)
    {
        var length = Vector3.Length(value);
        return value.IsFinite().All()
            && float.IsFinite(length)
            && (float.Abs(length - 1.0f) <= UnitVectorTolerance);
    }

    private static AssetId ReadAssetReference(
        scoped ReadOnlySpan<byte> payload,
        Guid expectedSchemaId,
        AssetCodecContext context)
    {
        var record = new EbinRecordReadView(payload);
        ValidateHeader(record, s_assetReferenceSchemaId, [3]);
        var assetIdValue = ReadUInt32(record, tag: 1);
        var schemaId = ReadGuid(record, tag: 2);
        var schemaMajor = ReadUInt32(record, tag: 3);

        if ((assetIdValue is 0 or > int.MaxValue)
            || (schemaId != expectedSchemaId)
            || (schemaMajor != 1))
        {
            throw new InvalidDataException("A Mesh asset reference does not name its owner-selected native asset schema.");
        }

        var schemaReference = new ContentSchemaReference(
            schemaId,
            checked((int)schemaMajor),
            0,
            ContentSchemaScope.Content);

        if (!context.Schemas.TryGetSchema(schemaReference, out var schema)
            || (schema.Role != ContentSchemaRole.NativeAsset))
        {
            throw new InvalidDataException("A Mesh asset reference selects an absent or non-native compiled schema.");
        }

        return new AssetId(checked((int)assetIdValue));
    }

    private static RenderMeshBounds ReadBounds(EbinRecordReadView record)
    {
        ValidateHeader(record, s_boundsSchemaId, [4]);
        var minimum = ReadVector3Field(record, tag: 1);
        var maximum = ReadVector3Field(record, tag: 2);
        var sphereCenter = ReadVector3Field(record, tag: 3);
        var sphereRadius = ReadSingle(record, tag: 4);

        if ((minimum.X > maximum.X)
            || (minimum.Y > maximum.Y)
            || (minimum.Z > maximum.Z)
            || (sphereRadius < 0.0f))
        {
            throw new InvalidDataException("Mesh bounds are unordered or have a negative sphere radius.");
        }

        return new RenderMeshBounds(minimum, maximum, sphereCenter, sphereRadius);
    }

    private static ImmutableArray<Vector4> ReadColorStream(EbinRecordReadView record, int vertexCount)
    {
        ValidateHeader(record, s_meshColorSchemaId, [2]);
        ValidateStride(record, tag: 1, expectedStride: 16);
        var values = ReadVector4Array(record, tag: 2, checked((ulong)vertexCount));

        foreach (var value in values)
        {
            if ((value.X < 0.0f) || (value.X > 1.0f)
                || (value.Y < 0.0f) || (value.Y > 1.0f)
                || (value.Z < 0.0f) || (value.Z > 1.0f)
                || (value.W < 0.0f) || (value.W > 1.0f))
            {
                throw new InvalidDataException("Mesh linear color components must be in [0,1].");
            }
        }

        return values;
    }

    private static ImmutableArray<ushort> ReadFixedUInt16Array(
        EbinRecordReadView record,
        uint tag,
        ulong minimumCount,
        ulong maximumCount)
    {
        var field = GetRequiredArrayField(record, tag);
        ValidateFixedArray(field, elementKind: 2, stride: sizeof(ushort), minimumCount, maximumCount);
        var elementCount = checked((int)field.Count);
        var result = new ushort[elementCount];
        var elements = field.Payload[FixedArrayHeaderBytes..];

        for (var index = 0; index < elementCount; index++)
        {
            result[index] = BinaryPrimitives.ReadUInt16LittleEndian(elements[(index * sizeof(ushort))..]);
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(result);
    }

    private static ImmutableArray<uint> ReadFixedUInt32Array(
        EbinRecordReadView record,
        uint tag,
        ulong minimumCount,
        ulong maximumCount)
    {
        var field = GetRequiredArrayField(record, tag);
        ValidateFixedArray(field, elementKind: 3, stride: sizeof(uint), minimumCount, maximumCount);
        var elementCount = checked((int)field.Count);
        var result = new uint[elementCount];
        var elements = field.Payload[FixedArrayHeaderBytes..];

        for (var index = 0; index < elementCount; index++)
        {
            result[index] = BinaryPrimitives.ReadUInt32LittleEndian(elements[(index * sizeof(uint))..]);
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(result);
    }

    private static ImmutableArray<RenderMeshGroup> ReadGroups(
        EbinRecordReadView record,
        ulong maximumIndexCount,
        int materialSlotCount,
        uint tag = 4)
    {
        var groupArray = GetRequiredRecordArray(record, tag, minimumCount: 1, maximumCount: 65_536);
        var groupCount = checked((int)groupArray.Count);
        var groups = new RenderMeshGroup[groupCount];
        var expectedFirstIndex = 0UL;

        for (var index = 0; index < groupCount; index++)
        {
            var groupRecord = record.GetRecordArrayElement(tag, index);
            ValidateHeader(groupRecord, s_meshGroupSchemaId, [3]);
            var firstIndex = ReadUInt32(groupRecord, tag: 1);
            var indexCount = ReadUInt32(groupRecord, tag: 2);
            var materialSlot = ReadUInt32(groupRecord, tag: 3);

            if ((firstIndex != expectedFirstIndex)
                || (indexCount == 0)
                || ((indexCount % 3) != 0)
                || (materialSlot >= materialSlotCount))
            {
                throw new InvalidDataException("Mesh groups must be contiguous nonempty triangle ranges with valid material slots.");
            }

            expectedFirstIndex = checked(expectedFirstIndex + indexCount);

            if (expectedFirstIndex > maximumIndexCount)
            {
                throw new InvalidDataException("A Mesh group extends beyond the selected index array.");
            }

            groups[index] = new RenderMeshGroup(
                checked((int)firstIndex),
                checked((int)indexCount),
                checked((int)materialSlot));
        }

        if (expectedFirstIndex != maximumIndexCount)
        {
            throw new InvalidDataException("Mesh groups must cover the complete index array.");
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(groups);
    }

    private static Guid ReadGuid(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 11);

        if (field.Payload.Length != 16)
        {
            throw new InvalidDataException($"Mesh record field {tag} does not contain one GUID.");
        }

        return new Guid(field.Payload, bigEndian: true);
    }

    private static (RenderMeshIndexFormat Format, ImmutableArray<uint> Indices) ReadIndices(EbinRecordReadView record, uint tag = 3)
    {
        _ = GetRequiredField(record, tag, kind: 22);
        var indicesRecord = record.GetRecord(tag);
        var header = indicesRecord.Header;

        if ((header.Major != 1) || (header.Minor != 0))
        {
            throw new NotSupportedException("The selected Mesh index schema version is unsupported.");
        }

        if (header.SchemaId == s_meshIndices16SchemaId)
        {
            ValidateHeader(indicesRecord, s_meshIndices16SchemaId, [1]);
            var values = ReadFixedUInt16Array(indicesRecord, tag: 1, minimumCount: 3, maximumCount: 1_048_576);
            var indices = new uint[values.Length];

            for (var index = 0; index < values.Length; index++)
            {
                indices[index] = values[index];
            }

            return (RenderMeshIndexFormat.UInt16, ImmutableCollectionsMarshal.AsImmutableArray(indices));
        }

        if (header.SchemaId == s_meshIndices32SchemaId)
        {
            ValidateHeader(indicesRecord, s_meshIndices32SchemaId, [1]);
            return (
                RenderMeshIndexFormat.UInt32,
                ReadFixedUInt32Array(indicesRecord, tag: 1, minimumCount: 3, maximumCount: 1_048_576));
        }

        throw new NotSupportedException("The selected Mesh index schema is unsupported.");
    }

    private static ImmutableArray<ushort> ReadJointStream(EbinRecordReadView record, int vertexCount)
    {
        ValidateHeader(record, s_meshJointsSchemaId, [2]);
        ValidateStride(record, tag: 1, expectedStride: 8);
        var jointCount = checked((ulong)vertexCount * 4);
        return ReadFixedUInt16Array(record, tag: 2, minimumCount: jointCount, maximumCount: jointCount);
    }

    private static ImmutableArray<AssetId> ReadMaterialSlots(EbinRecordReadView record, AssetCodecContext context)
    {
        var materialArray = GetRequiredRecordArray(record, tag: 5, minimumCount: 1, maximumCount: 256);
        var materialCount = checked((int)materialArray.Count);
        var materialSlots = new AssetId[materialCount];

        for (var index = 0; index < materialCount; index++)
        {
            materialSlots[index] = ReadAssetReference(
                record.GetRecordArrayElement(5, index).EncodedBytes,
                s_materialSchemaId,
                context);
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(materialSlots);
    }

    private static RenderMeshAsset ReadMesh(scoped EbinRecordReadView record, AssetCodecContext context)
    {
        ValidateHeader(record, s_schemaId, [6, 7]);
        var vertexCountValue = ReadUInt32(record, tag: 1);

        if (vertexCountValue is 0 or > 262_144)
        {
            throw new InvalidDataException("Mesh VertexCount must be in [1,262144].");
        }

        var vertexCount = checked((int)vertexCountValue);
        var streamArray = GetRequiredRecordArray(record, tag: 2, minimumCount: 1, maximumCount: 8);
        ImmutableArray<Vector3> positions = default;
        ImmutableArray<Vector3> normals = default;
        ImmutableArray<Vector4> tangents = default;
        ImmutableArray<Vector2> uv0 = default;
        ImmutableArray<Vector2> uv1 = default;
        ImmutableArray<Vector4> colors = default;
        ImmutableArray<ushort> jointIndices = default;
        ImmutableArray<Vector4> weights = default;
        var previousSemanticRank = -1;

        for (var index = 0; index < checked((int)streamArray.Count); index++)
        {
            var streamRecord = record.GetRecordArrayElement(2, index);
            var header = streamRecord.Header;
            int semanticRank;

            if (header.SchemaId == s_meshPositionSchemaId)
            {
                semanticRank = 0;
                positions = ReadPositionStream(streamRecord, vertexCount);
            }
            else if (header.SchemaId == s_meshNormalSchemaId)
            {
                semanticRank = 1;
                normals = ReadNormalStream(streamRecord, vertexCount);
            }
            else if (header.SchemaId == s_meshTangentSchemaId)
            {
                semanticRank = 2;
                tangents = ReadTangentStream(streamRecord, vertexCount);
            }
            else if (header.SchemaId == s_meshUvSchemaId)
            {
                var (uvSet, values) = ReadUvStream(streamRecord, vertexCount);
                semanticRank = 3 + uvSet;

                if (uvSet == 0)
                {
                    uv0 = values;
                }
                else
                {
                    uv1 = values;
                }
            }
            else if (header.SchemaId == s_meshColorSchemaId)
            {
                semanticRank = 5;
                colors = ReadColorStream(streamRecord, vertexCount);
            }
            else if (header.SchemaId == s_meshJointsSchemaId)
            {
                semanticRank = 6;
                jointIndices = ReadJointStream(streamRecord, vertexCount);
            }
            else if (header.SchemaId == s_meshWeightsSchemaId)
            {
                semanticRank = 7;
                weights = ReadWeightStream(streamRecord, vertexCount);
            }
            else
            {
                throw new NotSupportedException("The selected Mesh vertex stream schema is unsupported.");
            }

            if (semanticRank <= previousSemanticRank)
            {
                throw new InvalidDataException("Mesh streams must be unique and sorted by their fixed semantic rank.");
            }

            previousSemanticRank = semanticRank;
        }

        if (positions.IsDefault)
        {
            throw new InvalidDataException("Mesh Position is a required vertex stream.");
        }

        var (indexFormat, indices) = ReadIndices(record);

        if ((indices.Length % 3) != 0)
        {
            throw new InvalidDataException("Mesh triangle index count must be divisible by three.");
        }

        foreach (var index in indices)
        {
            if (index >= vertexCountValue)
            {
                throw new InvalidDataException("A Mesh index is outside the actual vertex domain.");
            }
        }

        ValidateTriangles(positions, indices);
        var materialSlots = ReadMaterialSlots(record, context);
        var groups = ReadGroups(record, checked((ulong)indices.Length), materialSlots.Length);
        _ = GetRequiredField(record, tag: 6, kind: 22);
        var bounds = ReadBounds(record.GetRecord(6));
        ValidateBounds(positions, bounds);
        AssetId? skeletonAssetId = null;

        if (record.TryGetField(7, out var skeletonField))
        {
            if (skeletonField.IsRequired || (skeletonField.Kind != 22) || (skeletonField.Count != 1))
            {
                throw new InvalidDataException("Mesh Skeleton must have the optional asset-reference shape.");
            }

            skeletonAssetId = ReadAssetReference(skeletonField.Payload, s_skeletonSchemaId, context);
        }

        if (jointIndices.IsDefault != weights.IsDefault
            || (jointIndices.IsDefault != !skeletonAssetId.HasValue))
        {
            throw new InvalidDataException("Mesh joints, weights, and Skeleton reference must be present together.");
        }

        return new RenderMeshAsset(
            context.RootAssetId,
            vertexCount,
            positions,
            normals,
            tangents,
            uv0,
            uv1,
            colors,
            jointIndices,
            weights,
            indexFormat,
            indices,
            groups,
            materialSlots,
            skeletonAssetId,
            bounds,
            [.. record.EncodedBytes]);
    }

    private static ImmutableArray<Vector3> ReadNormalStream(EbinRecordReadView record, int vertexCount)
    {
        ValidateHeader(record, s_meshNormalSchemaId, [2]);
        ValidateStride(record, tag: 1, expectedStride: 12);
        var values = ReadVector3Array(record, tag: 2, checked((ulong)vertexCount));

        foreach (var value in values)
        {
            if (!IsUnitVector(value))
            {
                throw new InvalidDataException("Mesh normals must be finite unit vectors.");
            }
        }

        return values;
    }

    private static ImmutableArray<Vector3> ReadPositionStream(EbinRecordReadView record, int vertexCount)
    {
        ValidateHeader(record, s_meshPositionSchemaId, [2]);
        ValidateStride(record, tag: 1, expectedStride: 12);
        var values = ReadVector3Array(record, tag: 2, checked((ulong)vertexCount));

        foreach (var value in values)
        {
            if (!value.IsFinite().All())
            {
                throw new InvalidDataException("Mesh positions must be finite.");
            }
        }

        return values;
    }

    private static float ReadSingle(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 10);

        if (field.Payload.Length != sizeof(float))
        {
            throw new InvalidDataException($"Mesh record field {tag} does not contain one binary32.");
        }

        var value = EbinPrimitives.Single(field.Payload);
        return float.IsFinite(value)
            ? value
            : throw new InvalidDataException($"Mesh record field {tag} must be finite.");
    }

    private static ImmutableArray<Vector4> ReadTangentStream(EbinRecordReadView record, int vertexCount)
    {
        ValidateHeader(record, s_meshTangentSchemaId, [2]);
        ValidateStride(record, tag: 1, expectedStride: 16);
        var values = ReadVector4Array(record, tag: 2, checked((ulong)vertexCount));

        foreach (var value in values)
        {
            var direction = new Vector3(value.X, value.Y, value.Z);

            if (!IsUnitVector(direction)
                || ((BitConverter.SingleToInt32Bits(value.W) != 0x3F80_0000)
                    && (BitConverter.SingleToInt32Bits(value.W) != unchecked((int)0xBF80_0000))))
            {
                throw new InvalidDataException("Mesh tangent XYZ must be unit length and W must be exactly +1 or -1.");
            }
        }

        return values;
    }

    private static uint ReadUInt32(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 3);

        if ((field.Count != 1) || (field.Payload.Length != sizeof(uint)))
        {
            throw new InvalidDataException($"Mesh record field {tag} does not contain one UInt32.");
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(field.Payload);
    }

    private static byte ReadUInt8(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 1);

        if ((field.Count != 1) || (field.Payload.Length != sizeof(byte)))
        {
            throw new InvalidDataException($"Mesh record field {tag} does not contain one UInt8.");
        }

        return field.Payload[0];
    }

    private static (byte UvSet, ImmutableArray<Vector2> Values) ReadUvStream(EbinRecordReadView record, int vertexCount)
    {
        ValidateHeader(record, s_meshUvSchemaId, [3]);
        var uvSet = ReadUInt8(record, tag: 1);

        if (uvSet > 1)
        {
            throw new InvalidDataException("Mesh UVSet must be zero or one.");
        }

        ValidateStride(record, tag: 2, expectedStride: 8);
        return (uvSet, ReadVector2Array(record, tag: 3, checked((ulong)vertexCount)));
    }

    private static Vector2 ReadVector2(scoped ReadOnlySpan<byte> bytes)
        => new(EbinPrimitives.Single(bytes), EbinPrimitives.Single(bytes[4..]));

    private static ImmutableArray<Vector2> ReadVector2Array(
        EbinRecordReadView record,
        uint tag,
        ulong expectedCount)
    {
        var field = GetRequiredField(record, tag, kind: 23, count: expectedCount);
        ValidateFixedArray(field, elementKind: 14, stride: 8, minimumCount: expectedCount, maximumCount: expectedCount);
        var elementCount = checked((int)field.Count);
        var values = new Vector2[elementCount];
        var payload = field.Payload[FixedArrayHeaderBytes..];

        for (var index = 0; index < elementCount; index++)
        {
            var value = ReadVector2(payload[(index * 8)..]);

            if (!value.IsFinite().All())
            {
                throw new InvalidDataException("Mesh UV values must be finite.");
            }

            values[index] = value;
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(values);
    }

    private static ImmutableArray<Vector3> ReadVector3Array(
        EbinRecordReadView record,
        uint tag,
        ulong expectedCount)
    {
        var field = GetRequiredField(record, tag, kind: 23, count: expectedCount);
        ValidateFixedArray(field, elementKind: 15, stride: 12, minimumCount: expectedCount, maximumCount: expectedCount);
        var elementCount = checked((int)field.Count);
        var values = new Vector3[elementCount];
        var payload = field.Payload[FixedArrayHeaderBytes..];

        for (var index = 0; index < elementCount; index++)
        {
            values[index] = ReadVector3Payload(payload[(index * 12)..]);
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(values);
    }

    private static Vector3 ReadVector3Field(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 15);

        if ((field.Count != 1) || (field.Payload.Length != 12))
        {
            throw new InvalidDataException($"Mesh record field {tag} does not contain one Vector3.");
        }

        var value = ReadVector3Payload(field.Payload);
        return value.IsFinite().All()
            ? value
            : throw new InvalidDataException($"Mesh record field {tag} must be finite.");
    }

    private static Vector3 ReadVector3Payload(scoped ReadOnlySpan<byte> bytes)
        => EbinPrimitives.Vector3(bytes);

    private static Vector4 ReadVector4(scoped ReadOnlySpan<byte> bytes)
        => EbinPrimitives.Vector4(bytes);

    private static ImmutableArray<Vector4> ReadVector4Array(
        EbinRecordReadView record,
        uint tag,
        ulong expectedCount)
    {
        var field = GetRequiredField(record, tag, kind: 23, count: expectedCount);
        ValidateFixedArray(field, elementKind: 16, stride: 16, minimumCount: expectedCount, maximumCount: expectedCount);
        var elementCount = checked((int)field.Count);
        var values = new Vector4[elementCount];
        var payload = field.Payload[FixedArrayHeaderBytes..];

        for (var index = 0; index < elementCount; index++)
        {
            var value = ReadVector4(payload[(index * 16)..]);

            if (!value.IsFinite().All())
            {
                throw new InvalidDataException("Mesh Vector4 stream values must be finite.");
            }

            values[index] = value;
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(values);
    }

    private static ImmutableArray<Vector4> ReadWeightStream(EbinRecordReadView record, int vertexCount)
    {
        ValidateHeader(record, s_meshWeightsSchemaId, [2]);
        ValidateStride(record, tag: 1, expectedStride: 16);
        var values = ReadVector4Array(record, tag: 2, checked((ulong)vertexCount));

        foreach (var value in values)
        {
            if ((value.X < 0.0f) || (value.X > 1.0f)
                || (value.Y < 0.0f) || (value.Y > 1.0f)
                || (value.Z < 0.0f) || (value.Z > 1.0f)
                || (value.W < 0.0f) || (value.W > 1.0f))
            {
                throw new InvalidDataException("Mesh weights must be finite values in [0,1].");
            }

            ValidateWeightSum(value);
        }

        return values;
    }

    private static void ValidateBounds(ImmutableArray<Vector3> positions, RenderMeshBounds bounds)
    {
        for (var index = 0; index < positions.Length; index++)
        {
            var position = positions[index];

            if ((position.X < bounds.Minimum.X) || (position.X > bounds.Maximum.X)
                || (position.Y < bounds.Minimum.Y) || (position.Y > bounds.Maximum.Y)
                || (position.Z < bounds.Minimum.Z) || (position.Z > bounds.Maximum.Z))
            {
                throw new InvalidDataException("Mesh bounds must conservatively contain every supplied position.");
            }
        }

        Span<ulong> workspace = stackalloc ulong[ExactDyadicLimbCount * 9];
        var dx = new MeshExactDyadic(workspace[..ExactDyadicLimbCount]);
        var dy = new MeshExactDyadic(workspace.Slice(ExactDyadicLimbCount, ExactDyadicLimbCount));
        var dz = new MeshExactDyadic(workspace.Slice(ExactDyadicLimbCount * 2, ExactDyadicLimbCount));
        var left = new MeshExactDyadic(workspace.Slice(ExactDyadicLimbCount * 3, ExactDyadicLimbCount));
        var right = new MeshExactDyadic(workspace.Slice(ExactDyadicLimbCount * 4, ExactDyadicLimbCount));
        var product = new MeshExactDyadic(workspace.Slice(ExactDyadicLimbCount * 5, ExactDyadicLimbCount));
        var squaredDistance = new MeshExactDyadic(workspace.Slice(ExactDyadicLimbCount * 6, ExactDyadicLimbCount));
        var radius = new MeshExactDyadic(workspace.Slice(ExactDyadicLimbCount * 7, ExactDyadicLimbCount));
        var radiusSquared = new MeshExactDyadic(workspace.Slice(ExactDyadicLimbCount * 8, ExactDyadicLimbCount));
        radius.SetSingle(bounds.SphereRadius);
        radiusSquared.SetProduct(radius, radius);

        for (var corner = 0; corner < 8; corner++)
        {
            var x = ((corner & 1) == 0) ? bounds.Minimum.X : bounds.Maximum.X;
            var y = ((corner & 2) == 0) ? bounds.Minimum.Y : bounds.Maximum.Y;
            var z = ((corner & 4) == 0) ? bounds.Minimum.Z : bounds.Maximum.Z;
            squaredDistance.SetZero();
            left.SetSingle(x);
            right.SetSingle(bounds.SphereCenter.X);
            dx.SetDifference(left, right);
            product.SetProduct(dx, dx);
            squaredDistance.AddMagnitude(product);
            left.SetSingle(y);
            right.SetSingle(bounds.SphereCenter.Y);
            dy.SetDifference(left, right);
            product.SetProduct(dy, dy);
            squaredDistance.AddMagnitude(product);
            left.SetSingle(z);
            right.SetSingle(bounds.SphereCenter.Z);
            dz.SetDifference(left, right);
            product.SetProduct(dz, dz);
            squaredDistance.AddMagnitude(product);

            if (squaredDistance.CompareMagnitude(radiusSquared) > 0)
            {
                throw new InvalidDataException("The Mesh bounding sphere does not enclose its axis-aligned bounds.");
            }
        }
    }

    private static void ValidateFixedArray(
        EbinFieldReadView field,
        ushort elementKind,
        int stride,
        ulong minimumCount,
        ulong maximumCount)
    {
        if ((field.Kind != 23)
            || (field.Count < minimumCount)
            || (field.Count > maximumCount))
        {
            throw new InvalidDataException($"Mesh field {field.Tag} does not match its admitted fixed-array shape.");
        }

        var payload = field.Payload;
        var expectedLength = checked(FixedArrayHeaderBytes + (checked((int)field.Count) * stride));

        if ((payload.Length != expectedLength)
            || (BinaryPrimitives.ReadUInt16LittleEndian(payload[16..]) != elementKind)
            || (BinaryPrimitives.ReadUInt32LittleEndian(payload[20..]) != (uint)stride))
        {
            throw new InvalidDataException($"Mesh field {field.Tag} has an invalid fixed-array element type, stride, or extent.");
        }
    }

    private static void ValidateHeader(EbinRecordReadView record, Guid schemaId, ReadOnlySpan<int> fieldCounts)
    {
        var header = record.Header;

        if ((header.SchemaId != schemaId) || (header.Major != 1) || (header.Minor != 0))
        {
            throw new NotSupportedException("The selected Mesh asset schema version is unsupported.");
        }

        foreach (var fieldCount in fieldCounts)
        {
            if (header.FieldCount == fieldCount)
            {
                return;
            }
        }

        throw new InvalidDataException("A Mesh asset record has an invalid field count.");
    }

    private static void ValidateStride(EbinRecordReadView record, uint tag, uint expectedStride)
    {
        if (ReadUInt32(record, tag) != expectedStride)
        {
            throw new InvalidDataException($"Mesh stream field {tag} has an unsupported stride.");
        }
    }

    private static void ValidateTriangles(ImmutableArray<Vector3> positions, ImmutableArray<uint> indices)
    {
        Span<ulong> workspace = stackalloc ulong[ExactDyadicLimbCount * 11];
        var abX = new MeshExactDyadic(workspace[..ExactDyadicLimbCount]);
        var abY = new MeshExactDyadic(workspace.Slice(ExactDyadicLimbCount, ExactDyadicLimbCount));
        var abZ = new MeshExactDyadic(workspace.Slice(ExactDyadicLimbCount * 2, ExactDyadicLimbCount));
        var acX = new MeshExactDyadic(workspace.Slice(ExactDyadicLimbCount * 3, ExactDyadicLimbCount));
        var acY = new MeshExactDyadic(workspace.Slice(ExactDyadicLimbCount * 4, ExactDyadicLimbCount));
        var acZ = new MeshExactDyadic(workspace.Slice(ExactDyadicLimbCount * 5, ExactDyadicLimbCount));
        var left = new MeshExactDyadic(workspace.Slice(ExactDyadicLimbCount * 6, ExactDyadicLimbCount));
        var right = new MeshExactDyadic(workspace.Slice(ExactDyadicLimbCount * 7, ExactDyadicLimbCount));
        var firstProduct = new MeshExactDyadic(workspace.Slice(ExactDyadicLimbCount * 8, ExactDyadicLimbCount));
        var secondProduct = new MeshExactDyadic(workspace.Slice(ExactDyadicLimbCount * 9, ExactDyadicLimbCount));
        var cross = new MeshExactDyadic(workspace.Slice(ExactDyadicLimbCount * 10, ExactDyadicLimbCount));

        for (var index = 0; index < indices.Length; index += 3)
        {
            var a = positions[checked((int)indices[index])];
            var b = positions[checked((int)indices[index + 1])];
            var c = positions[checked((int)indices[index + 2])];

            left.SetSingle(b.X);
            right.SetSingle(a.X);
            abX.SetDifference(left, right);
            left.SetSingle(b.Y);
            right.SetSingle(a.Y);
            abY.SetDifference(left, right);
            left.SetSingle(b.Z);
            right.SetSingle(a.Z);
            abZ.SetDifference(left, right);
            left.SetSingle(c.X);
            right.SetSingle(a.X);
            acX.SetDifference(left, right);
            left.SetSingle(c.Y);
            right.SetSingle(a.Y);
            acY.SetDifference(left, right);
            left.SetSingle(c.Z);
            right.SetSingle(a.Z);
            acZ.SetDifference(left, right);

            firstProduct.SetProduct(abY, acZ);
            secondProduct.SetProduct(abZ, acY);
            cross.SetDifference(firstProduct, secondProduct);
            var zeroArea = cross.IsZero;
            firstProduct.SetProduct(abZ, acX);
            secondProduct.SetProduct(abX, acZ);
            cross.SetDifference(firstProduct, secondProduct);
            zeroArea &= cross.IsZero;
            firstProduct.SetProduct(abX, acY);
            secondProduct.SetProduct(abY, acX);
            cross.SetDifference(firstProduct, secondProduct);
            zeroArea &= cross.IsZero;

            if (zeroArea)
            {
                throw new InvalidDataException("Mesh triangles must have nonzero exact dyadic area.");
            }
        }
    }

    private static void ValidateWeightSum(Vector4 value)
    {
        Span<ulong> workspace = stackalloc ulong[ExactDyadicLimbCount * 4];
        var sum = new MeshExactDyadic(workspace[..ExactDyadicLimbCount]);
        var component = new MeshExactDyadic(workspace.Slice(ExactDyadicLimbCount, ExactDyadicLimbCount));
        var one = new MeshExactDyadic(workspace.Slice(ExactDyadicLimbCount * 2, ExactDyadicLimbCount));
        var difference = new MeshExactDyadic(workspace.Slice(ExactDyadicLimbCount * 3, ExactDyadicLimbCount));
        component.SetSingle(value.X);
        sum.AddMagnitude(component);
        component.SetSingle(value.Y);
        sum.AddMagnitude(component);
        component.SetSingle(value.Z);
        sum.AddMagnitude(component);
        component.SetSingle(value.W);
        sum.AddMagnitude(component);
        one.SetSingle(1.0f);
        difference.SetDifference(sum, one);
        difference.MultiplySmall(100_000);

        if (difference.CompareMagnitude(one) > 0)
        {
            throw new InvalidDataException("Each Mesh weight vector must sum to one within the exact dyadic tolerance 1/100000.");
        }
    }

    public BinaryRecordStatus Read(
        scoped ReadOnlySpan<byte> source,
        AssetCodecContext context,
        [MaybeNull] out RenderMeshAsset value,
        out int bytesConsumed,
        out BinarySchemaDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(context);
        value = null;
        bytesConsumed = 0;
        diagnostic = null;
        var action = new ReadAction(context);
        EbinReadStatus status;
        EbinReadDiagnostic? readDiagnostic;

        try
        {
            status = EbinStructuralReader.Read(
                source,
                context.ReadLimits,
                EbinReadPolicy.Canonical,
                ref action,
                out bytesConsumed,
                out readDiagnostic);
        }
        catch (NotSupportedException)
        {
            diagnostic = CreateDiagnostic("unsupported-mesh-schema");
            bytesConsumed = 0;
            return BinaryRecordStatus.UnsupportedSchema;
        }
        catch (InvalidDataException)
        {
            diagnostic = CreateDiagnostic("invalid-mesh-record");
            bytesConsumed = 0;
            return BinaryRecordStatus.InvalidRecord;
        }

        if (status is not EbinReadStatus.Complete)
        {
            diagnostic = CreateDiagnostic(readDiagnostic);
            bytesConsumed = 0;
            return status switch {
                EbinReadStatus.NeedMoreData => BinaryRecordStatus.NeedMoreData,
                EbinReadStatus.UnsupportedSchema => BinaryRecordStatus.UnsupportedSchema,
                _ => BinaryRecordStatus.InvalidRecord,
            };
        }

        if ((bytesConsumed != source.Length) || (action.Value is null))
        {
            diagnostic = CreateDiagnostic(bytesConsumed != source.Length ? "trailing-mesh-bytes" : "missing-mesh-value");
            bytesConsumed = 0;
            return BinaryRecordStatus.InvalidRecord;
        }

        value = action.Value;
        return BinaryRecordStatus.Complete;
    }

    public BinaryRecordStatus Write(
        in RenderMeshAsset value,
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
            diagnostic = CreateDiagnostic("missing-mesh-value");
            return BinaryRecordStatus.InvalidRecord;
        }

        if (value.AssetId != context.RootAssetId)
        {
            diagnostic = CreateDiagnostic("mesh-asset-id-mismatch");
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

    private ref struct MeshExactDyadic
    {
        private bool _isNegative;
        private readonly Span<ulong> _magnitude;

        internal MeshExactDyadic(Span<ulong> magnitude)
        {
            if (magnitude.Length != ExactDyadicLimbCount)
            {
                throw new ArgumentException("The exact Mesh workspace has an invalid limb count.", nameof(magnitude));
            }

            _magnitude = magnitude;
            _magnitude.Clear();
            _isNegative = false;
        }

        internal readonly bool IsZero => GetLimbLength(_magnitude) == 0;

        private static void AddMagnitude(
            scoped Span<ulong> destination,
            scoped ReadOnlySpan<ulong> left,
            scoped ReadOnlySpan<ulong> right)
        {
            UInt128 carry = 0;

            for (var index = 0; index < destination.Length; index++)
            {
                var sum = (UInt128)left[index] + right[index] + carry;
                destination[index] = unchecked((ulong)sum);
                carry = sum >> 64;
            }

            if (carry != 0)
            {
                ThrowOverflow();
            }
        }

        private static int CompareMagnitude(
            scoped ReadOnlySpan<ulong> left,
            scoped ReadOnlySpan<ulong> right)
        {
            for (var index = left.Length - 1; index >= 0; index--)
            {
                if (left[index] != right[index])
                {
                    return (left[index] < right[index]) ? -1 : 1;
                }
            }

            return 0;
        }

        private static int GetLimbLength(scoped ReadOnlySpan<ulong> value)
        {
            for (var index = value.Length - 1; index >= 0; index--)
            {
                if (value[index] != 0)
                {
                    return index + 1;
                }
            }

            return 0;
        }

        private static void MultiplyMagnitude(
            scoped Span<ulong> destination,
            scoped ReadOnlySpan<ulong> left,
            scoped ReadOnlySpan<ulong> right)
        {
            destination.Clear();
            var leftLength = GetLimbLength(left);
            var rightLength = GetLimbLength(right);

            for (var leftIndex = 0; leftIndex < leftLength; leftIndex++)
            {
                UInt128 carry = 0;

                for (var rightIndex = 0; rightIndex < rightLength; rightIndex++)
                {
                    var destinationIndex = leftIndex + rightIndex;

                    if (destinationIndex >= destination.Length)
                    {
                        ThrowOverflow();
                    }

                    var product = ((UInt128)left[leftIndex] * right[rightIndex])
                        + destination[destinationIndex]
                        + carry;
                    destination[destinationIndex] = unchecked((ulong)product);
                    carry = product >> 64;
                }

                var carryIndex = leftIndex + rightLength;

                while (carry != 0)
                {
                    if (carryIndex >= destination.Length)
                    {
                        ThrowOverflow();
                    }

                    var sum = (UInt128)destination[carryIndex] + carry;
                    destination[carryIndex] = unchecked((ulong)sum);
                    carry = sum >> 64;
                    carryIndex++;
                }
            }
        }

        private static void SubtractMagnitude(
            scoped Span<ulong> destination,
            scoped ReadOnlySpan<ulong> left,
            scoped ReadOnlySpan<ulong> right)
        {
            ulong borrow = 0;

            for (var index = 0; index < destination.Length; index++)
            {
                var subtrahend = (UInt128)right[index] + borrow;
                var minuend = (UInt128)left[index];
                destination[index] = unchecked((ulong)(minuend - subtrahend));
                borrow = (minuend < subtrahend) ? 1ul : 0ul;
            }

            if (borrow != 0)
            {
                throw new InvalidOperationException("Exact Mesh magnitude subtraction underflowed.");
            }
        }

        private static void ThrowOverflow()
            => throw new InvalidOperationException("The exact Mesh predicate exceeded its fixed dyadic workspace.");

        internal readonly void AddMagnitude(scoped in MeshExactDyadic value)
        {
            if (_isNegative || value._isNegative || _magnitude.Overlaps(value._magnitude))
            {
                throw new InvalidOperationException("Exact Mesh magnitude addition requires distinct nonnegative values.");
            }

            AddMagnitude(_magnitude, _magnitude, value._magnitude);
        }

        internal readonly int CompareMagnitude(scoped in MeshExactDyadic value)
            => CompareMagnitude(_magnitude, value._magnitude);

        internal void MultiplySmall(ulong multiplier)
        {
            if (multiplier == 0)
            {
                SetZero();
                return;
            }

            UInt128 carry = 0;

            for (var index = 0; index < _magnitude.Length; index++)
            {
                var product = ((UInt128)_magnitude[index] * multiplier) + carry;
                _magnitude[index] = unchecked((ulong)product);
                carry = product >> 64;
            }

            if (carry != 0)
            {
                ThrowOverflow();
            }
        }

        internal void SetDifference(scoped in MeshExactDyadic left, scoped in MeshExactDyadic right)
        {
            if (_magnitude.Overlaps(left._magnitude) || _magnitude.Overlaps(right._magnitude))
            {
                throw new InvalidOperationException("Exact Mesh difference requires a distinct destination.");
            }

            var leftIsZero = left.IsZero;
            var rightIsZero = right.IsZero;

            if (leftIsZero)
            {
                if (rightIsZero)
                {
                    SetZero();
                }
                else
                {
                    right._magnitude.CopyTo(_magnitude);
                    _isNegative = !right._isNegative;
                }

                return;
            }

            if (rightIsZero)
            {
                left._magnitude.CopyTo(_magnitude);
                _isNegative = left._isNegative;
                return;
            }

            if (left._isNegative != right._isNegative)
            {
                AddMagnitude(_magnitude, left._magnitude, right._magnitude);
                _isNegative = left._isNegative;
                return;
            }

            var comparison = CompareMagnitude(left._magnitude, right._magnitude);

            if (comparison == 0)
            {
                SetZero();
            }
            else if (comparison > 0)
            {
                SubtractMagnitude(_magnitude, left._magnitude, right._magnitude);
                _isNegative = left._isNegative;
            }
            else
            {
                SubtractMagnitude(_magnitude, right._magnitude, left._magnitude);
                _isNegative = !left._isNegative;
            }
        }

        internal void SetProduct(scoped in MeshExactDyadic left, scoped in MeshExactDyadic right)
        {
            if (_magnitude.Overlaps(left._magnitude) || _magnitude.Overlaps(right._magnitude))
            {
                throw new InvalidOperationException("Exact Mesh multiplication requires a distinct destination.");
            }

            MultiplyMagnitude(_magnitude, left._magnitude, right._magnitude);
            _isNegative = !(left.IsZero || right.IsZero)
                && (left._isNegative != right._isNegative);
        }

        internal void SetSingle(float value)
        {
            var bits = BitConverter.SingleToUInt32Bits(value);
            var exponent = checked((int)((bits >> 23) & 0xFF));

            if (exponent == 0xFF)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Exact Mesh predicates require finite binary32 values.");
            }

            var fraction = bits & 0x007F_FFFF;
            var significand = (exponent == 0) ? fraction : (fraction | 0x0080_0000u);
            _magnitude.Clear();

            if (significand == 0)
            {
                _isNegative = false;
                return;
            }

            // Scaling by 2^149 maps every finite binary32 value to an exact integer.
            var shift = (exponent == 0) ? 0 : exponent - 1;
            var limbIndex = shift / 64;
            var bitShift = shift % 64;

            if (limbIndex >= _magnitude.Length)
            {
                ThrowOverflow();
            }

            _magnitude[limbIndex] = (ulong)significand << bitShift;

            if (bitShift > 40)
            {
                if ((limbIndex + 1) >= _magnitude.Length)
                {
                    ThrowOverflow();
                }

                _magnitude[limbIndex + 1] = (ulong)significand >> (64 - bitShift);
            }

            _isNegative = (bits & 0x8000_0000u) != 0;
        }

        internal void SetZero()
        {
            _magnitude.Clear();
            _isNegative = false;
        }
    }

    private struct ReadAction : IAction<EbinRecordReadView>
    {
        private readonly AssetCodecContext _context;

        internal ReadAction(AssetCodecContext context)
        {
            _context = context;
            Value = null;
        }

        internal RenderMeshAsset? Value { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
            => Value = ReadMesh(record, _context);
    }
}
