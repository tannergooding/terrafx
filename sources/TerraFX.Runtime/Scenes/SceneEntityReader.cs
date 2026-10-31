// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Scenes;

internal static class SceneEntityReader
{
    private static readonly Guid s_entitySchemaId = new ContentSchemaIdentityAssignment("native/entity").SchemaId;
    private static readonly Guid s_localTransformSchemaId = new ContentSchemaIdentityAssignment("scene/local-transform").SchemaId;

    internal static SceneEntity Read(
        EbinRecordReadView record,
        CompiledContentSchemaRegistry schemas,
        EbinReadLimits limits)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(limits);
        ValidateHeader(record, s_entitySchemaId, major: 1, fieldCount: 2);
        var entityId = ReadGuid(record, tag: 1, isRequired: true);
        var componentField = GetRecordArrayField(record, tag: 2, isRequired: true, limits);

        if ((componentField.Count > 256)
            || (componentField.Count > checked((ulong)limits.MaximumArrayElements)))
        {
            throw new InvalidDataException("The authored Entity contains too many component records.");
        }

        var componentCount = checked((int)componentField.Count);
        var components = ImmutableArray.CreateBuilder<SceneAuthoredComponentRecord>(componentCount);
        var previousSchemaId = Guid.Empty;
        var hasLocalTransform = false;
        var localTransformPosition = Vector3.Zero;

        for (var componentIndex = 0; componentIndex < componentCount; componentIndex++)
        {
            var componentRecord = record.GetRecordArrayElement(2, componentIndex);
            var header = componentRecord.Header;

            if ((header.SchemaId == Guid.Empty) || (header.Major <= 0))
            {
                throw new InvalidDataException("An authored component has an invalid schema identity or major version.");
            }

            if (previousSchemaId != Guid.Empty
                && (FoundationCatalogue.CompareRfc(previousSchemaId, header.SchemaId) >= 0))
            {
                throw new InvalidDataException("Authored component records are not uniquely ordered by RFC schema identity.");
            }

            var schema = new ContentSchemaReference(
                header.SchemaId,
                header.Major,
                header.Minor,
                ContentSchemaScope.Content);

            if (!schemas.TryGetSchema(schema, out var metadata))
            {
                throw new NotSupportedException($"The authored component schema {schema} is not registered.");
            }

            if (metadata.Role is not ContentSchemaRole.Component and not ContentSchemaRole.EntityTag)
            {
                throw new InvalidDataException("An authored Entity record contains a non-component schema.");
            }

            if (header.SchemaId == s_localTransformSchemaId)
            {
                if ((header.Major != 1) || (header.Minor != 0))
                {
                    throw new NotSupportedException("The selected LocalTransform schema version is unsupported.");
                }

                hasLocalTransform = true;
                localTransformPosition = ReadLocalTransformPosition(componentRecord);
            }

            components.Add(new SceneAuthoredComponentRecord(schema, componentRecord.EncodedBytes));
            previousSchemaId = header.SchemaId;
        }

        if (!hasLocalTransform)
        {
            throw new InvalidDataException("Every native Scene Entity requires a LocalTransform component.");
        }

        return new SceneEntity(entityId, localTransformPosition, components.MoveToImmutable());
    }

    private static EbinFieldReadView GetField(
        EbinRecordReadView record,
        uint tag,
        ushort kind,
        int payloadLength)
    {
        if (!record.TryGetField(tag, out var field)
            || (field.Kind != kind)
            || !field.IsRequired
            || (field.Count != 1)
            || (field.Payload.Length != payloadLength))
        {
            throw new InvalidDataException($"Scene component field {tag} does not match its schema.");
        }

        return field;
    }

    private static EbinFieldReadView GetRecordArrayField(
        EbinRecordReadView record,
        uint tag,
        bool isRequired,
        EbinReadLimits limits)
    {
        if (!record.TryGetField(tag, out var field)
            || (field.Kind != 24)
            || (field.IsRequired != isRequired)
            || (field.Count > checked((ulong)limits.MaximumArrayElements))
            || (field.Count > int.MaxValue))
        {
            throw new InvalidDataException($"Authored Entity record-array field {tag} does not match its schema.");
        }

        return field;
    }

    private static Guid ReadGuid(EbinRecordReadView record, uint tag, bool isRequired)
    {
        if (!record.TryGetField(tag, out var field)
            || (field.Kind != 11)
            || (field.IsRequired != isRequired)
            || (field.Count != 1)
            || (field.Payload.Length != 16))
        {
            throw new InvalidDataException($"Authored Entity GUID field {tag} does not match its schema.");
        }

        var id = new Guid(field.Payload, bigEndian: true);

        if (id == Guid.Empty)
        {
            throw new InvalidDataException($"Authored Entity GUID field {tag} must be nonempty.");
        }

        return id;
    }

    private static Vector3 ReadLocalTransformPosition(EbinRecordReadView record)
    {
        ValidateHeader(record, s_localTransformSchemaId, major: 1, fieldCount: 3);
        var position = GetField(record, tag: 1, kind: 15, payloadLength: 12);
        _ = GetField(record, tag: 2, kind: 17, payloadLength: 16);
        _ = GetField(record, tag: 3, kind: 15, payloadLength: 12);
        return EbinPrimitives.Vector3(position.Payload);
    }

    private static void ValidateHeader(EbinRecordReadView record, Guid schemaId, uint major, int fieldCount)
    {
        var header = record.Header;

        if ((header.SchemaId != schemaId)
            || (header.Major != major)
            || (header.Minor != 0)
            || (record.FieldCount != fieldCount))
        {
            throw new InvalidDataException("The authored Entity record does not match the selected schema tuple.");
        }
    }
}
