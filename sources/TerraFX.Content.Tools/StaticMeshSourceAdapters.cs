// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using TerraFX;
using TerraFX.Numerics;
using BigInteger = System.Numerics.BigInteger;

namespace TerraFX.Content.Tools;

internal static class StaticMeshSourceAdapters
{
    internal const int MaximumGlbBytes = 67_108_864;
    internal const int MaximumObjBytes = 16_777_216;
    private const uint GlbBinaryChunk = 0x004E4942;
    private const int GlbChunkHeaderBytes = 8;
    private const int GlbHeaderBytes = 12;
    private const uint GlbJsonChunk = 0x4E4F534A;
    private const uint GlbMagic = 0x46546C67;
    private const int GlbNormalComponent = 5126;
    private const int GlbPositionComponent = 5126;
    private const int GlbTexCoordComponent = 5126;
    private const int GlbUnsignedIntComponent = 5125;
    private const int GlbUnsignedShortComponent = 5123;
    private const uint GlbVersion = 2;
    private const int IndexStride16 = sizeof(ushort);
    private const int IndexStride32 = sizeof(uint);
    private const int JsonMaximumDepth = 32;
    private const int MaximumIndexCount = 1_048_576;
    private const int MaximumVertexCount = 262_144;
    private const float UnitVectorTolerance = 2.0e-6f;
    private static readonly UTF8Encoding s_utf8 = new(false, true);
    private static readonly ContentSchemaIdentityAssignment s_assetReferenceSchema = new("native/asset-reference");
    private static readonly ContentSchemaIdentityAssignment s_boundsSchema = new("native/bounds3");
    private static readonly ContentSchemaIdentityAssignment s_meshGroupSchema = new("native/mesh-group");
    private static readonly ContentSchemaIdentityAssignment s_meshIndices16Schema = new("native/mesh-indices16");
    private static readonly ContentSchemaIdentityAssignment s_meshIndices32Schema = new("native/mesh-indices32");
    private static readonly ContentSchemaIdentityAssignment s_meshNormalSchema = new("native/mesh-normal");
    private static readonly ContentSchemaIdentityAssignment s_meshPositionSchema = new("native/mesh-position");
    private static readonly ContentSchemaIdentityAssignment s_meshUvSchema = new("native/mesh-uv");
    private static readonly ContentSchemaIdentityAssignment s_meshSchema = new("native/mesh");
    private static readonly ContentSchemaIdentityAssignment s_materialSchema = new("native/material");

    internal static byte[] CookGlbPackage(
        scoped ReadOnlySpan<byte> source,
        scoped ReadOnlySpan<byte> profileRecord,
        scoped ReadOnlySpan<byte> materialRecord,
        uint meshAssetId,
        uint materialAssetId)
    {
        var geometry = ReadGlb(source);
        return CreatePackage(geometry, profileRecord, materialRecord, meshAssetId, materialAssetId);
    }

    internal static byte[] CookWavefrontObjPackage(
        scoped ReadOnlySpan<byte> source,
        WavefrontObjSourceConvention convention,
        scoped ReadOnlySpan<byte> profileRecord,
        scoped ReadOnlySpan<byte> materialRecord,
        uint meshAssetId,
        uint materialAssetId)
    {
        if (convention is not WavefrontObjSourceConvention.RightHandedYUpForwardZMeters)
        {
            throw new ArgumentOutOfRangeException(nameof(convention));
        }

        var geometry = ReadWavefrontObj(source);
        return CreatePackage(geometry, profileRecord, materialRecord, meshAssetId, materialAssetId);
    }

    private static void AddIndexedCorner(
        ObjCorner corner,
        List<Vector3> sourcePositions,
        List<Vector3> sourceNormals,
        List<Vector2> sourceUvs,
        bool hasUv0,
        Dictionary<ObjVertexKey, uint> vertexIndices,
        List<Vector3> positions,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<uint> indices)
    {
        var key = new ObjVertexKey(corner.PositionIndex, corner.UvIndex, corner.NormalIndex);

        if (!vertexIndices.TryGetValue(key, out var outputIndex))
        {
            if (positions.Count >= MaximumVertexCount)
            {
                throw new InvalidDataException("The OBJ adapter exceeds the native Mesh vertex limit.");
            }

            var sourcePosition = sourcePositions[corner.PositionIndex];
            var sourceNormal = sourceNormals[corner.NormalIndex];
            positions.Add(new Vector3(sourcePosition.X, sourcePosition.Y, -sourcePosition.Z));
            normals.Add(new Vector3(sourceNormal.X, sourceNormal.Y, -sourceNormal.Z));

            if (hasUv0)
            {
                uvs.Add(sourceUvs[corner.UvIndex]);
            }

            outputIndex = checked((uint)(positions.Count - 1));
            vertexIndices.Add(key, outputIndex);
        }

        if (indices.Count >= MaximumIndexCount)
        {
            throw new InvalidDataException("The OBJ adapter exceeds the native Mesh index limit.");
        }

        indices.Add(outputIndex);
    }

    private static byte[] CreateBoundsRecord(Vector3[] positions)
    {
        var minimum = positions[0];
        var maximum = positions[0];

        foreach (var position in positions)
        {
            minimum = new Vector3(
                MathF.Min(minimum.X, position.X),
                MathF.Min(minimum.Y, position.Y),
                MathF.Min(minimum.Z, position.Z));
            maximum = new Vector3(
                MathF.Max(maximum.X, position.X),
                MathF.Max(maximum.Y, position.Y),
                MathF.Max(maximum.Z, position.Z));
        }

        var maximumX = MathF.Max(float.Abs(minimum.X), float.Abs(maximum.X));
        var maximumY = MathF.Max(float.Abs(minimum.Y), float.Abs(maximum.Y));
        var maximumZ = MathF.Max(float.Abs(minimum.Z), float.Abs(maximum.Z));
        var scaledX = ToScaledInteger(maximumX);
        var scaledY = ToScaledInteger(maximumY);
        var scaledZ = ToScaledInteger(maximumZ);
        var maximumDistanceSquared = (scaledX * scaledX) + (scaledY * scaledY) + (scaledZ * scaledZ);
        var maximumDistance = Math.Sqrt(
            ((double)maximumX * maximumX)
            + ((double)maximumY * maximumY)
            + ((double)maximumZ * maximumZ));

        var sphereRadius = (float)maximumDistance;

        if (!float.IsFinite(sphereRadius))
        {
            throw new InvalidDataException("The imported Mesh bounds exceed the finite binary32 range.");
        }

        while (ToScaledInteger(sphereRadius) * ToScaledInteger(sphereRadius) < maximumDistanceSquared)
        {
            sphereRadius = MathF.BitIncrement(sphereRadius);

            if (!float.IsFinite(sphereRadius))
            {
                throw new InvalidDataException("The imported Mesh sphere radius exceeds the finite binary32 range.");
            }
        }

        while (sphereRadius > 0.0f)
        {
            var previous = MathF.BitDecrement(sphereRadius);

            if ((previous < 0.0f)
                || (ToScaledInteger(previous) * ToScaledInteger(previous) < maximumDistanceSquared))
            {
                break;
            }

            sphereRadius = previous;
        }

        return ContentPackageWriter.EncodeRecord(
            s_boundsSchema.SchemaId,
            1,
            0,
            [
                new ContentRecordField(1, 15, 1, ContentPackageWriter.EncodeVector3(minimum)),
                new ContentRecordField(2, 15, 1, ContentPackageWriter.EncodeVector3(maximum)),
                new ContentRecordField(3, 15, 1, ContentPackageWriter.EncodeVector3(Vector3.Zero)),
                new ContentRecordField(4, 10, 1, ContentPackageWriter.EncodeSingle(sphereRadius)),
            ]);
    }

    private static byte[] CreateIndicesRecord(uint[] indices)
    {
        var maximumIndex = 0u;

        foreach (var index in indices)
        {
            maximumIndex = Math.Max(maximumIndex, index);
        }

        if (maximumIndex <= ushort.MaxValue)
        {
            var shortIndices = new ushort[indices.Length];

            for (var index = 0; index < indices.Length; index++)
            {
                shortIndices[index] = checked((ushort)indices[index]);
            }

            return ContentPackageWriter.EncodeRecord(
                s_meshIndices16Schema.SchemaId,
                1,
                0,
                [new ContentRecordField(1, 23, checked((ulong)shortIndices.Length), ContentPackageWriter.EncodeFixedArray(shortIndices))]);
        }

        return ContentPackageWriter.EncodeRecord(
            s_meshIndices32Schema.SchemaId,
            1,
            0,
            [new ContentRecordField(1, 23, checked((ulong)indices.Length), ContentPackageWriter.EncodeFixedArray(indices))]);
    }

    private static byte[] CreateMeshRecord(MeshGeometry geometry, uint materialAssetId)
    {
        ValidateGeometry(geometry);
        var streams = new List<byte[]>(geometry.Uv0 is null ? 2 : 3) {
            CreateVector3Stream(s_meshPositionSchema.SchemaId, geometry.Positions),
            CreateVector3Stream(s_meshNormalSchema.SchemaId, geometry.Normals),
        };

        if (geometry.Uv0 is not null)
        {
            streams.Add(CreateUv0Stream(geometry.Uv0));
        }

        var indexRecord = CreateIndicesRecord(geometry.Indices);
        var groupRecord = ContentPackageWriter.EncodeRecord(
            s_meshGroupSchema.SchemaId,
            1,
            0,
            [
                new ContentRecordField(1, 3, 1, ContentPackageWriter.EncodeUInt32(0)),
                new ContentRecordField(2, 3, 1, ContentPackageWriter.EncodeUInt32(checked((uint)geometry.Indices.Length))),
                new ContentRecordField(3, 3, 1, ContentPackageWriter.EncodeUInt32(0)),
            ]);
        var materialReference = ContentPackageWriter.EncodeRecord(
            s_assetReferenceSchema.SchemaId,
            1,
            0,
            [
                new ContentRecordField(1, 3, 1, ContentPackageWriter.EncodeUInt32(materialAssetId)),
                new ContentRecordField(2, 11, 1, EncodeGuid(new ContentSchemaIdentityAssignment("native/material").SchemaId)),
                new ContentRecordField(3, 3, 1, ContentPackageWriter.EncodeUInt32(1)),
            ]);
        var bounds = CreateBoundsRecord(geometry.Positions);
        return ContentPackageWriter.EncodeRecord(
            s_meshSchema.SchemaId,
            1,
            0,
            [
                new ContentRecordField(1, 3, 1, ContentPackageWriter.EncodeUInt32(checked((uint)geometry.Positions.Length))),
                new ContentRecordField(2, 24, checked((ulong)streams.Count), ContentPackageWriter.EncodeRecordArray([.. streams])),
                new ContentRecordField(3, 22, 1, indexRecord),
                new ContentRecordField(4, 24, 1, ContentPackageWriter.EncodeRecordArray([groupRecord])),
                new ContentRecordField(5, 24, 1, ContentPackageWriter.EncodeRecordArray([materialReference])),
                new ContentRecordField(6, 22, 1, bounds),
            ]);
    }

    private static byte[] CreatePackage(
        MeshGeometry geometry,
        scoped ReadOnlySpan<byte> profileRecord,
        scoped ReadOnlySpan<byte> materialRecord,
        uint meshAssetId,
        uint materialAssetId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(meshAssetId);
        ArgumentOutOfRangeException.ThrowIfZero(materialAssetId);

        if ((meshAssetId > int.MaxValue) || (materialAssetId > int.MaxValue))
        {
            throw new ArgumentOutOfRangeException(nameof(meshAssetId), "AssetIds must be in [1,2147483647].");
        }

        if (meshAssetId == materialAssetId)
        {
            throw new ArgumentException("The Mesh and Material AssetIds must be distinct.", nameof(materialAssetId));
        }

        _ = ContentPackageWriter.ValidateRecord(
            materialRecord,
            s_materialSchema.SchemaId,
            major: 1,
            minor: 0,
            "material",
            ContentPackageWriter.AssetRecordLimits);
        var meshRecord = CreateMeshRecord(geometry, materialAssetId);
        return ContentPackageWriter.CreatePackage(
            profileRecord,
            [
                new ContentPackageAsset(meshAssetId, meshRecord),
                new ContentPackageAsset(materialAssetId, materialRecord.ToArray()),
            ]);
    }

    private static byte[] CreateUv0Stream(Vector2[] values)
        => ContentPackageWriter.EncodeRecord(
            s_meshUvSchema.SchemaId,
            1,
            0,
            [
                new ContentRecordField(1, 1, 1, ContentPackageWriter.EncodeUInt8(0)),
                new ContentRecordField(2, 3, 1, ContentPackageWriter.EncodeUInt32(8)),
                new ContentRecordField(3, 23, checked((ulong)values.Length), ContentPackageWriter.EncodeFixedArray(values)),
            ]);

    private static byte[] CreateVector3Stream(Guid schemaId, Vector3[] values)
        => ContentPackageWriter.EncodeRecord(
            schemaId,
            1,
            0,
            [
                new ContentRecordField(1, 3, 1, ContentPackageWriter.EncodeUInt32(12)),
                new ContentRecordField(2, 23, checked((ulong)values.Length), ContentPackageWriter.EncodeFixedArray(values)),
            ]);

    private static byte[] EncodeGuid(Guid value)
    {
        var bytes = new byte[16];
        _ = value.TryWriteBytes(bytes, bigEndian: true, out _);
        return bytes;
    }

    private static void EnsureProperties(JsonElement element, ReadOnlySpan<string> allowed)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("A GLB value with object fields is not a JSON object.");
        }

        foreach (var property in element.EnumerateObject())
        {
            var found = false;

            for (var index = 0; index < allowed.Length; index++)
            {
                if (StringComparer.Ordinal.Equals(property.Name, allowed[index]))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                throw new NotSupportedException($"The GLB property '{property.Name}' is outside the static Mesh adapter subset.");
            }
        }
    }

    private static int GetOptionalInt32(JsonElement element, string propertyName, int defaultValue, int minimum, int maximum)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return defaultValue;
        }

        if ((value.ValueKind != JsonValueKind.Number)
            || !value.TryGetInt32(out var result)
            || (result < minimum)
            || (result > maximum))
        {
            throw new InvalidDataException($"The GLB property '{propertyName}' is outside its admitted integer range.");
        }

        return result;
    }

    private static int GetRequiredInt32(JsonElement element, string propertyName, int minimum, int maximum)
    {
        var value = GetRequiredProperty(element, propertyName);

        if ((value.ValueKind != JsonValueKind.Number)
            || !value.TryGetInt32(out var result)
            || (result < minimum)
            || (result > maximum))
        {
            throw new InvalidDataException($"The GLB property '{propertyName}' is outside its admitted integer range.");
        }

        return result;
    }

    private static JsonElement GetRequiredProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var value))
        {
            throw new InvalidDataException($"The GLB JSON object is missing required property '{propertyName}'.");
        }

        return value;
    }

    private static bool IsUnitVector(Vector3 value)
    {
        var length = Vector3.Length(value);
        return value.IsFinite().All()
            && float.IsFinite(length)
            && (float.Abs(length - 1.0f) <= UnitVectorTolerance);
    }

    private static float ParseFiniteSingle(string token, string semantic)
    {
        if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || !float.IsFinite(value))
        {
            throw new InvalidDataException($"An {semantic} value is not a finite binary32 number.");
        }

        return value;
    }

    private static ObjCorner ParseObjCorner(
        string token,
        int positionCount,
        int uvCount,
        int normalCount)
    {
        var components = token.Split('/');

        if ((components.Length != 3)
            || (components[0].Length == 0)
            || (components[2].Length == 0))
        {
            throw new NotSupportedException("OBJ face corners must specify position and normal indices with an optional UV0 index.");
        }

        var positionIndex = ParseObjIndex(components[0], positionCount, "position");
        var uvIndex = (components[1].Length == 0)
            ? -1
            : ParseObjIndex(components[1], uvCount, "UV0");
        var normalIndex = ParseObjIndex(components[2], normalCount, "normal");
        return new ObjCorner(positionIndex, uvIndex, normalIndex);
    }

    private static int ParseObjIndex(string token, int currentCount, string semantic)
    {
        if (!int.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            || (value == 0))
        {
            throw new InvalidDataException($"An OBJ {semantic} index is not a nonzero signed integer.");
        }

        var index = (value > 0) ? value - 1 : checked(currentCount + value);

        if ((uint)index >= (uint)currentCount)
        {
            throw new InvalidDataException($"An OBJ {semantic} index refers outside the values already declared.");
        }

        return index;
    }

    private static ReadOnlySpan<byte> ReadAccessorBytes(
        JsonElement accessor,
        JsonElement bufferViews,
        ReadOnlySpan<byte> binary,
        int elementStride,
        int count)
    {
        var bufferViewIndex = GetRequiredInt32(accessor, "bufferView", 0, bufferViews.GetArrayLength() - 1);
        var bufferView = bufferViews[bufferViewIndex];
        var viewByteOffset = GetOptionalInt32(bufferView, "byteOffset", 0, 0, binary.Length);
        var viewByteLength = GetRequiredInt32(bufferView, "byteLength", 1, binary.Length);
        var accessorByteOffset = GetOptionalInt32(accessor, "byteOffset", 0, 0, viewByteLength);
        var byteLength = checked(elementStride * count);
        var endOffset = checked((long)accessorByteOffset + byteLength);

        if ((endOffset > viewByteLength)
            || (checked((long)viewByteOffset + endOffset) > binary.Length))
        {
            throw new InvalidDataException("A GLB accessor exceeds its selected buffer view.");
        }

        if (((viewByteOffset + accessorByteOffset) % Math.Min(elementStride, sizeof(uint))) != 0)
        {
            throw new InvalidDataException("A GLB accessor is not aligned for its component type.");
        }

        return binary.Slice(checked(viewByteOffset + accessorByteOffset), byteLength);
    }

    private static MeshGeometry ReadGlb(ReadOnlySpan<byte> source)
    {
        ArgumentOutOfRangeException.ThrowIfZero(source.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(source.Length, MaximumGlbBytes);

        if ((source.Length < GlbHeaderBytes)
            || (BinaryPrimitives.ReadUInt32LittleEndian(source) != GlbMagic)
            || (BinaryPrimitives.ReadUInt32LittleEndian(source[4..]) != GlbVersion)
            || (BinaryPrimitives.ReadUInt32LittleEndian(source[8..]) != source.Length))
        {
            throw new InvalidDataException("The source is not a complete glTF 2.0 binary container.");
        }

        var cursor = GlbHeaderBytes;
        ReadOnlySpan<byte> jsonChunk = default;
        ReadOnlySpan<byte> binaryChunk = default;
        var chunkIndex = 0;

        while (cursor < source.Length)
        {
            if ((source.Length - cursor) < GlbChunkHeaderBytes)
            {
                throw new InvalidDataException("The GLB container ends in a partial chunk header.");
            }

            var chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(source[cursor..]);
            var chunkType = BinaryPrimitives.ReadUInt32LittleEndian(source[(cursor + sizeof(uint))..]);
            cursor += GlbChunkHeaderBytes;

            if ((chunkLength > int.MaxValue)
                || ((chunkLength % 4) != 0)
                || (chunkLength > (uint)(source.Length - cursor)))
            {
                throw new InvalidDataException("A GLB chunk exceeds the source container.");
            }

            var chunk = source.Slice(cursor, checked((int)chunkLength));

            if ((chunkIndex == 0) && (chunkType == GlbJsonChunk))
            {
                jsonChunk = chunk;
            }
            else if ((chunkIndex == 1) && (chunkType == GlbBinaryChunk))
            {
                binaryChunk = chunk;
            }
            else
            {
                throw new InvalidDataException("The GLB adapter accepts one JSON chunk followed by one BIN chunk.");
            }

            cursor = checked(cursor + (int)chunkLength);
            chunkIndex++;
        }

        if ((chunkIndex != 2) || jsonChunk.IsEmpty || binaryChunk.IsEmpty)
        {
            throw new InvalidDataException("The GLB container must contain exactly one JSON chunk and one BIN chunk.");
        }

        using var document = JsonDocument.Parse(
            ReadGlbJsonChunk(jsonChunk),
            new JsonDocumentOptions {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = JsonMaximumDepth,
            });
        var root = document.RootElement;
        RejectDuplicateProperties(root);
        EnsureProperties(root, ["asset", "buffers", "bufferViews", "accessors", "meshes"]);
        var asset = GetRequiredProperty(root, "asset");
        EnsureProperties(asset, ["version", "minVersion", "generator", "copyright", "extras"]);
        var version = GetRequiredProperty(asset, "version").GetString();

        if (!StringComparer.Ordinal.Equals(version, "2.0"))
        {
            throw new NotSupportedException("The GLB adapter requires glTF version 2.0.");
        }

        if (asset.TryGetProperty("minVersion", out var minimumVersion)
            && !StringComparer.Ordinal.Equals(minimumVersion.GetString(), "2.0"))
        {
            throw new NotSupportedException("The GLB declares an unsupported minimum glTF version.");
        }

        var bufferArray = GetRequiredProperty(root, "buffers");

        if ((bufferArray.ValueKind != JsonValueKind.Array) || (bufferArray.GetArrayLength() != 1))
        {
            throw new InvalidDataException("The GLB adapter requires exactly one embedded buffer.");
        }

        var buffer = bufferArray[0];
        EnsureProperties(buffer, ["byteLength", "extras"]);

        if (buffer.TryGetProperty("uri", out _))
        {
            throw new NotSupportedException("The GLB adapter does not load external or data-URI buffers.");
        }

        var declaredBufferLength = GetRequiredInt32(buffer, "byteLength", minimum: 1, MaximumGlbBytes);

        if ((binaryChunk.Length < declaredBufferLength)
            || (binaryChunk.Length - declaredBufferLength > 3))
        {
            throw new InvalidDataException("The GLB BIN chunk length does not match the declared embedded buffer.");
        }

        for (var index = declaredBufferLength; index < binaryChunk.Length; index++)
        {
            if (binaryChunk[index] != 0)
            {
                throw new InvalidDataException("GLB BIN chunk alignment padding must be zero.");
            }
        }

        var bufferViewArray = GetRequiredProperty(root, "bufferViews");

        if ((bufferViewArray.ValueKind != JsonValueKind.Array) || (bufferViewArray.GetArrayLength() is < 1 or > 4))
        {
            throw new InvalidDataException("The GLB adapter requires one through four buffer views.");
        }

        foreach (var bufferView in bufferViewArray.EnumerateArray())
        {
            EnsureProperties(bufferView, ["buffer", "byteOffset", "byteLength", "extras"]);

            if (GetRequiredInt32(bufferView, "buffer", 0, 0) != 0)
            {
                throw new NotSupportedException("The GLB adapter accepts only the single embedded buffer.");
            }

            var byteOffset = GetOptionalInt32(bufferView, "byteOffset", 0, 0, declaredBufferLength);
            var byteLength = GetRequiredInt32(bufferView, "byteLength", minimum: 1, declaredBufferLength);

            if ((long)byteOffset + byteLength > declaredBufferLength)
            {
                throw new InvalidDataException("A GLB buffer view exceeds the declared embedded buffer.");
            }

            if (bufferView.TryGetProperty("byteStride", out _))
            {
                throw new NotSupportedException("The GLB adapter accepts only tightly packed accessors.");
            }
        }

        var accessorArray = GetRequiredProperty(root, "accessors");

        if ((accessorArray.ValueKind != JsonValueKind.Array) || (accessorArray.GetArrayLength() is < 2 or > 4))
        {
            throw new InvalidDataException("The GLB adapter requires two through four accessors.");
        }

        var meshArray = GetRequiredProperty(root, "meshes");

        if ((meshArray.ValueKind != JsonValueKind.Array) || (meshArray.GetArrayLength() != 1))
        {
            throw new InvalidDataException("The GLB adapter requires exactly one Mesh.");
        }

        var mesh = meshArray[0];
        EnsureProperties(mesh, ["primitives", "extras"]);
        var primitiveArray = GetRequiredProperty(mesh, "primitives");

        if ((primitiveArray.ValueKind != JsonValueKind.Array) || (primitiveArray.GetArrayLength() != 1))
        {
            throw new NotSupportedException("The GLB adapter accepts exactly one primitive per Mesh.");
        }

        var primitive = primitiveArray[0];
        EnsureProperties(primitive, ["attributes", "indices", "mode", "extras"]);
        var mode = GetOptionalInt32(primitive, "mode", 4, minimum: 4, maximum: 4);
        _ = mode;
        var attributes = GetRequiredProperty(primitive, "attributes");
        EnsureProperties(attributes, ["POSITION", "NORMAL", "TEXCOORD_0"]);
        var positionsAccessor = GetRequiredInt32(attributes, "POSITION", minimum: 0, accessorArray.GetArrayLength() - 1);
        var normalsAccessor = GetRequiredInt32(attributes, "NORMAL", minimum: 0, accessorArray.GetArrayLength() - 1);
        var positionCount = ReadVector3Accessor(
            accessorArray,
            positionsAccessor,
            bufferViewArray,
            binaryChunk,
            expectedComponentType: GlbPositionComponent,
            "POSITION",
            out var positions);
        var normalCount = ReadVector3Accessor(
            accessorArray,
            normalsAccessor,
            bufferViewArray,
            binaryChunk,
            expectedComponentType: GlbNormalComponent,
            "NORMAL",
            out var normals);

        if (normalCount != positionCount)
        {
            throw new InvalidDataException("GLB POSITION and NORMAL accessors must have equal counts.");
        }

        Vector2[]? uvs = null;

        if (attributes.TryGetProperty("TEXCOORD_0", out var uvAccessor))
        {
            if ((uvAccessor.ValueKind != JsonValueKind.Number)
                || !uvAccessor.TryGetInt32(out var uvAccessorIndex)
                || (uvAccessorIndex < 0)
                || (uvAccessorIndex >= accessorArray.GetArrayLength()))
            {
                throw new InvalidDataException("The GLB TEXCOORD_0 accessor index is invalid.");
            }

            var uvCount = ReadVector2Accessor(
                accessorArray,
                uvAccessorIndex,
                bufferViewArray,
                binaryChunk,
                GlbTexCoordComponent,
                out uvs);

            if (uvCount != positionCount)
            {
                throw new InvalidDataException("GLB POSITION and TEXCOORD_0 accessors must have equal counts.");
            }
        }

        uint[] indices;

        if (primitive.TryGetProperty("indices", out var indexAccessor))
        {
            if ((indexAccessor.ValueKind != JsonValueKind.Number)
                || !indexAccessor.TryGetInt32(out var indexAccessorIndex)
                || (indexAccessorIndex < 0)
                || (indexAccessorIndex >= accessorArray.GetArrayLength()))
            {
                throw new InvalidDataException("The GLB index accessor index is invalid.");
            }

            _ = ReadIndexAccessor(accessorArray, indexAccessorIndex, bufferViewArray, binaryChunk, out indices);
        }
        else
        {
            if ((positionCount % 3) != 0)
            {
                throw new InvalidDataException("An unindexed GLB triangle primitive must have a vertex count divisible by three.");
            }

            indices = new uint[positionCount];

            for (var index = 0; index < indices.Length; index++)
            {
                indices[index] = checked((uint)index);
            }
        }

        if ((indices.Length % 3) != 0)
        {
            throw new InvalidDataException("The GLB triangle index count must be divisible by three.");
        }

        ReflectAndReverse(positions, normals, indices);
        var geometry = new MeshGeometry(positions, normals, uvs, indices);
        ValidateGeometry(geometry);
        return geometry;
    }

    private static byte[] ReadGlbJsonChunk(scoped ReadOnlySpan<byte> jsonChunk)
    {
        var jsonLength = jsonChunk.Length;

        while ((jsonLength > 0) && (jsonChunk[jsonLength - 1] == (byte)' '))
        {
            jsonLength--;
        }

        if (jsonLength == 0)
        {
            throw new InvalidDataException("The GLB JSON chunk is empty.");
        }

        return jsonChunk[..jsonLength].ToArray();
    }

    private static int ReadIndexAccessor(
        JsonElement accessors,
        int accessorIndex,
        JsonElement bufferViews,
        ReadOnlySpan<byte> binary,
        out uint[] indices)
    {
        var accessor = accessors[accessorIndex];
        ValidateAccessor(accessor, expectedType: "SCALAR");
        var componentType = GetRequiredInt32(accessor, "componentType", GlbUnsignedShortComponent, GlbUnsignedIntComponent);

        if (componentType is not GlbUnsignedShortComponent and not GlbUnsignedIntComponent)
        {
            throw new NotSupportedException("GLB indices must use unsigned 16- or 32-bit components.");
        }

        var count = GetRequiredInt32(accessor, "count", 3, MaximumIndexCount);
        var bytesPerIndex = (componentType == GlbUnsignedShortComponent) ? IndexStride16 : IndexStride32;
        var data = ReadAccessorBytes(accessor, bufferViews, binary, bytesPerIndex, count);
        indices = new uint[count];

        for (var index = 0; index < count; index++)
        {
            var offset = checked(index * bytesPerIndex);
            indices[index] = (componentType == GlbUnsignedShortComponent)
                ? BinaryPrimitives.ReadUInt16LittleEndian(data[offset..])
                : BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        }

        return count;
    }

    private static float ReadSingle(ReadOnlySpan<byte> source)
        => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(source));

    private static int ReadVector2Accessor(
        JsonElement accessors,
        int accessorIndex,
        JsonElement bufferViews,
        ReadOnlySpan<byte> binary,
        int expectedComponentType,
        out Vector2[] values)
    {
        var accessor = accessors[accessorIndex];
        ValidateAccessor(accessor, expectedType: "VEC2");
        ValidateComponentType(accessor, expectedComponentType);
        var count = GetRequiredInt32(accessor, "count", 1, MaximumVertexCount);
        var data = ReadAccessorBytes(accessor, bufferViews, binary, 2 * sizeof(float), count);
        values = new Vector2[count];

        for (var index = 0; index < count; index++)
        {
            var offset = checked(index * 2 * sizeof(float));
            values[index] = new Vector2(ReadSingle(data[offset..]), ReadSingle(data[(offset + sizeof(float))..]));
        }

        return count;
    }

    private static Vector3 ReadVector3(string[] tokens, int startIndex, string semantic)
        => new(
            ParseFiniteSingle(tokens[startIndex], semantic),
            ParseFiniteSingle(tokens[startIndex + 1], semantic),
            ParseFiniteSingle(tokens[startIndex + 2], semantic));

    private static int ReadVector3Accessor(
        JsonElement accessors,
        int accessorIndex,
        JsonElement bufferViews,
        ReadOnlySpan<byte> binary,
        int expectedComponentType,
        string semantic,
        out Vector3[] values)
    {
        var accessor = accessors[accessorIndex];
        ValidateAccessor(accessor, expectedType: "VEC3");
        ValidateComponentType(accessor, expectedComponentType);
        var count = GetRequiredInt32(accessor, "count", 1, MaximumVertexCount);
        var data = ReadAccessorBytes(accessor, bufferViews, binary, 3 * sizeof(float), count);
        values = new Vector3[count];

        for (var index = 0; index < count; index++)
        {
            var offset = checked(index * 3 * sizeof(float));
            values[index] = new Vector3(
                ReadSingle(data[offset..]),
                ReadSingle(data[(offset + sizeof(float))..]),
                ReadSingle(data[(offset + (2 * sizeof(float)))..]));
        }

        if (semantic is "NORMAL")
        {
            for (var index = 0; index < values.Length; index++)
            {
                if (!IsUnitVector(values[index]))
                {
                    throw new InvalidDataException("GLB normals must already satisfy the native unit-vector tolerance.");
                }
            }
        }

        return count;
    }

    private static MeshGeometry ReadWavefrontObj(ReadOnlySpan<byte> source)
    {
        ArgumentOutOfRangeException.ThrowIfZero(source.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(source.Length, MaximumObjBytes);
        string text;

        try
        {
            text = s_utf8.GetString(source);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("The OBJ source is not valid UTF-8.", exception);
        }

        if (text.StartsWith('\uFEFF', StringComparison.Ordinal))
        {
            text = text[1..];
        }

        var sourcePositions = new List<Vector3>();
        var sourceNormals = new List<Vector3>();
        var sourceUvs = new List<Vector2>();
        var vertexIndices = new Dictionary<ObjVertexKey, uint>();
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<uint>();
        bool? hasUv0 = null;
        using var reader = new StringReader(text);
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            var commentIndex = line.IndexOf('#', StringComparison.Ordinal);

            if (commentIndex >= 0)
            {
                line = line[..commentIndex];
            }

            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length == 0)
            {
                continue;
            }

            switch (tokens[0])
            {
                case "v":
                {
                    if (tokens.Length != 4)
                    {
                        throw new NotSupportedException("OBJ vertex records must contain exactly three coordinates and no colors.");
                    }

                    sourcePositions.Add(ReadVector3(tokens, 1, "OBJ position"));
                    ArgumentOutOfRangeException.ThrowIfGreaterThan(sourcePositions.Count, MaximumVertexCount);
                    break;
                }

                case "vn":
                {
                    if (tokens.Length != 4)
                    {
                        throw new InvalidDataException("OBJ normal records must contain exactly three coordinates.");
                    }

                    sourceNormals.Add(ReadVector3(tokens, 1, "OBJ normal"));
                    ArgumentOutOfRangeException.ThrowIfGreaterThan(sourceNormals.Count, MaximumVertexCount);
                    break;
                }

                case "vt":
                {
                    if (tokens.Length != 3)
                    {
                        throw new NotSupportedException("OBJ texture coordinates must contain exactly two values.");
                    }

                    sourceUvs.Add(new Vector2(
                        ParseFiniteSingle(tokens[1], "OBJ UV0"),
                        ParseFiniteSingle(tokens[2], "OBJ UV0")));
                    ArgumentOutOfRangeException.ThrowIfGreaterThan(sourceUvs.Count, MaximumVertexCount);
                    break;
                }

                case "f":
                {
                    if (tokens.Length != 4)
                    {
                        throw new NotSupportedException("The OBJ adapter accepts triangle faces only; it does not triangulate polygons.");
                    }

                    var corners = new ObjCorner[3];

                    for (var index = 0; index < corners.Length; index++)
                    {
                        corners[index] = ParseObjCorner(
                            tokens[index + 1],
                            sourcePositions.Count,
                            sourceUvs.Count,
                            sourceNormals.Count);
                        var faceHasUv = corners[index].UvIndex >= 0;

                        if ((hasUv0 is bool previousHasUv) && (previousHasUv != faceHasUv))
                        {
                            throw new InvalidDataException("OBJ UV0 must be present for every face corner or absent throughout.");
                        }

                        hasUv0 = faceHasUv;
                    }

                    AddIndexedCorner(
                        corners[0],
                        sourcePositions,
                        sourceNormals,
                        sourceUvs,
                        hasUv0 ?? false,
                        vertexIndices,
                        positions,
                        normals,
                        uvs,
                        indices);
                    AddIndexedCorner(
                        corners[2],
                        sourcePositions,
                        sourceNormals,
                        sourceUvs,
                        hasUv0 ?? false,
                        vertexIndices,
                        positions,
                        normals,
                        uvs,
                        indices);
                    AddIndexedCorner(
                        corners[1],
                        sourcePositions,
                        sourceNormals,
                        sourceUvs,
                        hasUv0 ?? false,
                        vertexIndices,
                        positions,
                        normals,
                        uvs,
                        indices);
                    break;
                }

                default:
                {
                    throw new NotSupportedException($"The OBJ statement '{tokens[0]}' is outside the static Mesh adapter subset.");
                }
            }
        }

        if (positions.Count == 0)
        {
            throw new InvalidDataException("The OBJ source contains no supported triangle faces.");
        }

        var geometry = new MeshGeometry(
            [.. positions],
            [.. normals],
            (hasUv0 is true) ? [.. uvs] : null,
            [.. indices]);
        ValidateGeometry(geometry);
        return geometry;
    }

    private static void ReflectAndReverse(Vector3[] positions, Vector3[] normals, uint[] indices)
    {
        for (var index = 0; index < positions.Length; index++)
        {
            positions[index] = new Vector3(positions[index].X, positions[index].Y, -positions[index].Z);
            normals[index] = new Vector3(normals[index].X, normals[index].Y, -normals[index].Z);
        }

        for (var index = 0; index < indices.Length; index += 3)
        {
            (indices[index + 1], indices[index + 2]) = (indices[index + 2], indices[index + 1]);
        }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
            {
                foreach (var child in element.EnumerateArray())
                {
                    RejectDuplicateProperties(child);
                }

                break;
            }

            case JsonValueKind.Object:
            {
                var names = new HashSet<string>(StringComparer.Ordinal);

                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                    {
                        throw new InvalidDataException($"The GLB JSON object repeats property '{property.Name}'.");
                    }

                    RejectDuplicateProperties(property.Value);
                }

                break;
            }
        }
    }

    private static BigInteger ToScaledInteger(float value)
    {
        var bits = unchecked((uint)BitConverter.SingleToInt32Bits(value));
        var negative = (bits & 0x80000000u) != 0;
        var exponentBits = (int)((bits >> 23) & 0xFF);
        var fraction = bits & 0x7FFFFFu;

        if (exponentBits == 0xFF)
        {
            throw new InvalidDataException("Mesh values must be finite.");
        }

        var significand = (exponentBits == 0) ? fraction : (fraction | 0x800000u);

        if (significand == 0)
        {
            return BigInteger.Zero;
        }

        var exponent = (exponentBits == 0) ? 0 : exponentBits - 1;
        var scaled = new BigInteger(significand) << exponent;
        return negative ? -scaled : scaled;
    }

    private static void ValidateAccessor(JsonElement accessor, string expectedType)
    {
        EnsureProperties(accessor, ["bufferView", "byteOffset", "componentType", "normalized", "count", "type", "min", "max", "extras"]);

        if (accessor.TryGetProperty("sparse", out _))
        {
            throw new NotSupportedException("The GLB adapter does not accept sparse accessors.");
        }

        var type = GetRequiredProperty(accessor, "type").GetString();

        if (!StringComparer.Ordinal.Equals(type, expectedType))
        {
            throw new NotSupportedException($"The GLB accessor type '{type}' is outside the selected static Mesh stream.");
        }

        if (accessor.TryGetProperty("normalized", out var normalized)
            && ((normalized.ValueKind != JsonValueKind.False) || normalized.GetBoolean()))
        {
            throw new NotSupportedException("The GLB adapter accepts only non-normalized FLOAT vertex attributes.");
        }
    }

    private static void ValidateComponentType(JsonElement accessor, int expectedComponentType)
    {
        if (GetRequiredInt32(accessor, "componentType", 0, int.MaxValue) != expectedComponentType)
        {
            throw new NotSupportedException("The GLB vertex attribute uses an unsupported component type.");
        }
    }

    private static void ValidateGeometry(MeshGeometry geometry)
    {
        if ((geometry.Positions.Length is < 1 or > MaximumVertexCount)
            || (geometry.Normals.Length != geometry.Positions.Length)
            || ((geometry.Uv0 is not null) && (geometry.Uv0.Length != geometry.Positions.Length))
            || (geometry.Indices.Length is < 3 or > MaximumIndexCount)
            || ((geometry.Indices.Length % 3) != 0))
        {
            throw new InvalidDataException("The imported geometry does not fit the native Mesh vertex and triangle limits.");
        }

        for (var index = 0; index < geometry.Positions.Length; index++)
        {
            if (!geometry.Positions[index].IsFinite().All()
                || !IsUnitVector(geometry.Normals[index])
                || ((geometry.Uv0 is not null) && !geometry.Uv0[index].IsFinite().All()))
            {
                throw new InvalidDataException("The imported Mesh contains nonfinite geometry or a nonunit normal.");
            }
        }

        for (var index = 0; index < geometry.Indices.Length; index += 3)
        {
            if ((geometry.Indices[index] >= geometry.Positions.Length)
                || (geometry.Indices[index + 1] >= geometry.Positions.Length)
                || (geometry.Indices[index + 2] >= geometry.Positions.Length))
            {
                throw new InvalidDataException("The imported Mesh index is outside the vertex domain.");
            }

            var a = geometry.Positions[checked((int)geometry.Indices[index])];
            var b = geometry.Positions[checked((int)geometry.Indices[index + 1])];
            var c = geometry.Positions[checked((int)geometry.Indices[index + 2])];
            var abX = ToScaledInteger(b.X) - ToScaledInteger(a.X);
            var abY = ToScaledInteger(b.Y) - ToScaledInteger(a.Y);
            var abZ = ToScaledInteger(b.Z) - ToScaledInteger(a.Z);
            var acX = ToScaledInteger(c.X) - ToScaledInteger(a.X);
            var acY = ToScaledInteger(c.Y) - ToScaledInteger(a.Y);
            var acZ = ToScaledInteger(c.Z) - ToScaledInteger(a.Z);
            var crossX = (abY * acZ) - (abZ * acY);
            var crossY = (abZ * acX) - (abX * acZ);
            var crossZ = (abX * acY) - (abY * acX);

            if (crossX.IsZero && crossY.IsZero && crossZ.IsZero)
            {
                throw new InvalidDataException("Imported Mesh triangles must have nonzero exact dyadic area.");
            }
        }
    }

    private readonly record struct MeshGeometry(
        Vector3[] Positions,
        Vector3[] Normals,
        Vector2[]? Uv0,
        uint[] Indices);

    private readonly record struct ObjCorner(int PositionIndex, int UvIndex, int NormalIndex);

    private readonly record struct ObjVertexKey(int PositionIndex, int UvIndex, int NormalIndex);
}
