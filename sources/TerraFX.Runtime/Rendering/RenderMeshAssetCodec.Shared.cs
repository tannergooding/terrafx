// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;

namespace TerraFX.Rendering;

internal sealed partial class RenderMeshAssetCodec
{
    internal static ulong PreflightSharedIndices(EbinRecordReadView record, uint indexTag)
    {
        _ = GetRequiredField(record, indexTag, 22);
        var indices = record.GetRecord(indexTag);
        var schemaId = indices.Header.SchemaId;

        if ((schemaId != s_meshIndices16SchemaId) && (schemaId != s_meshIndices32SchemaId))
        {
            throw new NotSupportedException("The selected Mesh index schema is unsupported.");
        }

        ValidateHeader(indices, schemaId, [1]);
        var field = GetRequiredArrayField(indices, 1);
        var is16Bit = schemaId == s_meshIndices16SchemaId;
        ValidateFixedArray(
            field,
            is16Bit ? (ushort)2 : (ushort)3,
            is16Bit ? sizeof(ushort) : sizeof(uint),
            3,
            1_048_576);

        if ((field.Count % 3) != 0)
        {
            throw new InvalidDataException("Every Mesh LOD index count must be divisible by three.");
        }

        return field.Count;
    }

    internal static RenderMeshBounds ReadSharedBounds(EbinRecordReadView record, RenderMeshVertexStreams vertices)
    {
        _ = GetRequiredField(record, 6, 22);
        var bounds = ReadBounds(record.GetRecord(6));
        ValidateBounds(vertices.Positions, bounds);
        return bounds;
    }

    internal static RenderMeshLodLevel ReadSharedLevel(
        EbinRecordReadView record,
        uint indexTag,
        uint groupTag,
        RenderMeshVertexStreams vertices,
        int materialSlotCount)
    {
        var (format, indices) = ReadIndices(record, indexTag);

        if ((indices.Length % 3) != 0)
        {
            throw new InvalidDataException("Mesh triangle index count must be divisible by three.");
        }

        foreach (var index in indices)
        {
            if (index >= vertices.Positions.Length)
            {
                throw new InvalidDataException("A Mesh index is outside the actual vertex domain.");
            }
        }

        ValidateTriangles(vertices.Positions, indices);
        var groups = ReadGroups(record, checked((ulong)indices.Length), materialSlotCount, groupTag);
        return new RenderMeshLodLevel(format, indices, groups);
    }

    internal static RenderMeshVertexStreams ReadSharedStreams(EbinRecordReadView record, int vertexCount)
    {
        var streamArray = GetRequiredRecordArray(record, 2, 1, 8);
        var vertices = default(RenderMeshVertexStreams);
        var previousSemanticRank = -1;

        for (var index = 0; index < checked((int)streamArray.Count); index++)
        {
            var stream = record.GetRecordArrayElement(2, index);
            var schemaId = stream.Header.SchemaId;
            int semanticRank;

            if (schemaId == s_meshPositionSchemaId)
            {
                semanticRank = 0;
                vertices = vertices with { Positions = ReadPositionStream(stream, vertexCount) };
            }
            else if (schemaId == s_meshNormalSchemaId)
            {
                semanticRank = 1;
                vertices = vertices with { Normals = ReadNormalStream(stream, vertexCount) };
            }
            else if (schemaId == s_meshTangentSchemaId)
            {
                semanticRank = 2;
                vertices = vertices with { Tangents = ReadTangentStream(stream, vertexCount) };
            }
            else if (schemaId == s_meshUvSchemaId)
            {
                var (uvSet, values) = ReadUvStream(stream, vertexCount);
                semanticRank = 3 + uvSet;
                vertices = uvSet == 0 ? vertices with { Uv0 = values } : vertices with { Uv1 = values };
            }
            else if (schemaId == s_meshColorSchemaId)
            {
                semanticRank = 5;
                vertices = vertices with { Colors = ReadColorStream(stream, vertexCount) };
            }
            else if (schemaId == s_meshJointsSchemaId)
            {
                semanticRank = 6;
                vertices = vertices with { JointIndices = ReadJointStream(stream, vertexCount) };
            }
            else if (schemaId == s_meshWeightsSchemaId)
            {
                semanticRank = 7;
                vertices = vertices with { Weights = ReadWeightStream(stream, vertexCount) };
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

        if (vertices.Positions.IsDefault)
        {
            throw new InvalidDataException("Mesh Position is a required vertex stream.");
        }

        return vertices;
    }
}
