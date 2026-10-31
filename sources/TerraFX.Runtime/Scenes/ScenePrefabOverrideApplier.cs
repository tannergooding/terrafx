// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using TerraFX;

namespace TerraFX.Scenes;

internal static class ScenePrefabOverrideApplier
{
    internal static SceneAuthoredComponentRecord Apply(
        SceneAuthoredComponentRecord component,
        ScenePrefabOverride prefabOverride,
        CompiledContentSchemaRegistry schemas,
        EbinReadLimits limits)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(prefabOverride);
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(limits);

        if (component.Schema != prefabOverride.ComponentSchema)
        {
            throw new InvalidDataException("A Prefab override component tuple does not match the selected component record.");
        }

        if (!schemas.TryGetSchema(component.Schema, out var metadata))
        {
            throw new NotSupportedException($"The Prefab override component schema {component.Schema} is not registered.");
        }

        if (metadata.Role != ContentSchemaRole.Component)
        {
            throw new InvalidDataException("A Prefab override target schema does not identify a Component.");
        }

        var action = new ApplyAction(metadata, prefabOverride, schemas, limits);
        var encodedRecord = component.EncodedRecord;
        var status = EbinStructuralReader.Read(
            encodedRecord,
            limits,
            EbinReadPolicy.Canonical,
            ref action,
            out var bytesConsumed,
            out var diagnostic);

        if (status is EbinReadStatus.UnsupportedSchema)
        {
            throw new NotSupportedException($"The Prefab override component record uses an unsupported schema: {diagnostic?.Category ?? status.ToString()}.");
        }

        if ((status is not EbinReadStatus.Complete) || (bytesConsumed != encodedRecord.Length))
        {
            throw new InvalidDataException($"The Prefab override component record is invalid: {diagnostic?.Category ?? status.ToString()}.");
        }

        return new SceneAuthoredComponentRecord(
            component.Schema,
            action.Result ?? throw new InvalidDataException("The Prefab override did not produce a component record."));
    }

    internal static DataSchema ResolveNestedSchema(
        ContentFieldDescriptor field,
        EbinRecordReadView nestedRecord,
        CompiledContentSchemaRegistry schemas)
    {
        var header = nestedRecord.Header;

        if ((header.SchemaId == Guid.Empty) || (header.Major <= 0))
        {
            throw new InvalidDataException("A nested component record has an invalid schema identity or major version.");
        }

        if (!field.NestedSchemas.IsEmpty)
        {
            for (var index = 0; index < field.NestedSchemas.Length; index++)
            {
                var nestedSchema = field.NestedSchemas[index];

                if ((nestedSchema.SchemaId == header.SchemaId)
                    && (nestedSchema.Major == header.Major)
                    && (nestedSchema.Minor == header.Minor))
                {
                    if (!schemas.TryGetSchema(nestedSchema, out var exactMetadata))
                    {
                        throw new NotSupportedException($"The nested component schema {nestedSchema} is not registered.");
                    }

                    return exactMetadata;
                }
            }

            throw new InvalidDataException("A nested component record is outside the field's exact schema set.");
        }

        var contentSchema = new ContentSchemaReference(
            header.SchemaId,
            header.Major,
            header.Minor,
            ContentSchemaScope.Content);

        if (schemas.TryGetSchema(contentSchema, out var metadata)
            && IsAllowedNestedRole(field, metadata.Role))
        {
            return metadata;
        }

        var foundationSchema = new ContentSchemaReference(
            header.SchemaId,
            header.Major,
            header.Minor,
            ContentSchemaScope.Foundation);

        if (schemas.TryGetSchema(foundationSchema, out metadata)
            && IsAllowedNestedRole(field, metadata.Role))
        {
            return metadata;
        }

        throw new NotSupportedException("A nested component record does not resolve to an allowed registered schema role.");
    }

    private static byte[] ApplyFixedArrayElement(
        EbinRecordReadView record,
        EbinFieldReadView field,
        ContentFieldDescriptor fieldDescriptor,
        ScenePrefabOverride prefabOverride,
        EbinReadLimits limits,
        LiteralValue? value,
        ScenePropertyPathSegment indexSegment)
    {
        if (prefabOverride.Operation != ScenePrefabOverrideOperation.Set)
        {
            throw new InvalidDataException("An Omit Prefab override cannot target a FixedArray element.");
        }

        if (fieldDescriptor.WireKind != 23)
        {
            throw new InvalidDataException("A FixedArrayIndex property segment must follow a FixedArray field.");
        }

        if (indexSegment.Value >= field.Count)
        {
            throw new InvalidDataException("A Prefab override FixedArrayIndex is outside the field extent.");
        }

        var literal = value
            ?? throw new InvalidDataException("A Set Prefab override requires a fixed literal Value.");
        var payload = field.Payload;

        if (payload.Length < 32)
        {
            throw new InvalidDataException("A Prefab override FixedArray payload has no complete array header.");
        }

        var elementKind = BinaryPrimitives.ReadUInt16LittleEndian(payload[16..]);
        var elementStride = BinaryPrimitives.ReadUInt32LittleEndian(payload[20..]);

        if ((elementKind != fieldDescriptor.ElementKind)
            || (elementStride == 0)
            || (elementStride > int.MaxValue)
            || (literal.WireKind != elementKind)
            || (literal.Count != 1)
            || (literal.Payload.Length != checked((int)elementStride)))
        {
            throw new InvalidDataException("A Prefab override Value does not match the FixedArray element shape.");
        }

        ValidateReferenceValue(fieldDescriptor, prefabOverride, literal);
        var elementIndex = checked((int)indexSegment.Value);
        var elementOffset = checked(32 + (elementIndex * checked((int)elementStride)));

        if (checked(elementOffset + checked((int)elementStride)) > payload.Length)
        {
            throw new InvalidDataException("A Prefab override FixedArrayIndex exceeds the encoded payload extent.");
        }

        var updatedPayload = payload.ToArray();
        literal.Payload.AsSpan().CopyTo(updatedPayload.AsSpan(elementOffset, checked((int)elementStride)));
        return RewriteRecord(
            record,
            field.Tag,
            removeField: false,
            field.Kind,
            field.Count,
            updatedPayload,
            limits);
    }

    private static byte[] ApplyLeaf(
        EbinRecordReadView record,
        EbinFieldReadView field,
        ContentFieldDescriptor fieldDescriptor,
        ScenePrefabOverride prefabOverride,
        EbinReadLimits limits,
        LiteralValue? value)
    {
        if (prefabOverride.Operation is ScenePrefabOverrideOperation.Omit)
        {
            if (fieldDescriptor.IsRequired || field.IsRequired)
            {
                throw new InvalidDataException("An Omit Prefab override can target only an optional field.");
            }

            if ((prefabOverride.Value is not null) || (prefabOverride.ReferenceValue is not null))
            {
                throw new InvalidDataException("An Omit Prefab override cannot carry a value or reference target.");
            }

            return RewriteRecord(
                record,
                field.Tag,
                removeField: true,
                field.Kind,
                field.Count,
                replacementPayload: null,
                limits);
        }

        if (prefabOverride.Operation is not ScenePrefabOverrideOperation.Set)
        {
            throw new InvalidDataException("A Prefab override operation is not defined.");
        }

        if (fieldDescriptor.WireKind is 22 or 23 or 24)
        {
            throw new InvalidDataException("Set replaces only a fixed scalar or geometry leaf, not a record or array.");
        }

        var literal = value
            ?? throw new InvalidDataException("A Set Prefab override requires a fixed literal Value.");

        if ((literal.WireKind != fieldDescriptor.WireKind)
            || (literal.Count != field.Count)
            || (literal.Payload.Length != field.Payload.Length)
            || (literal.Count < fieldDescriptor.MinimumCount)
            || (literal.Count > fieldDescriptor.MaximumCount))
        {
            throw new InvalidDataException("A Prefab override literal does not match the selected component field shape.");
        }

        ValidateReferenceValue(fieldDescriptor, prefabOverride, literal);
        return RewriteRecord(
            record,
            field.Tag,
            removeField: false,
            field.Kind,
            field.Count,
            literal.Payload,
            limits);
    }

    private static byte[] ApplyPath(
        EbinRecordReadView record,
        DataSchema schema,
        ScenePrefabOverride prefabOverride,
        CompiledContentSchemaRegistry schemas,
        EbinReadLimits limits,
        LiteralValue? value,
        int pathIndex)
    {
        var path = prefabOverride.PropertyPath;

        if ((pathIndex >= path.Length)
            || (path[pathIndex].Kind != ScenePropertyPathSegmentKind.FieldTag)
            || (path[pathIndex].Value == 0))
        {
            throw new InvalidDataException("A Prefab override property path is not a valid FieldTag sequence.");
        }

        var tag = path[pathIndex].Value;
        var fieldDescriptor = FindField(schema, tag)
            ?? throw new InvalidDataException($"Prefab override field tag {tag} is absent from the selected schema.");

        if ((fieldDescriptor.AuthoringFlags & ContentAuthoring.PrefabEditable) == 0)
        {
            throw new InvalidDataException($"Prefab override field tag {tag} is not PrefabEditable.");
        }

        if (!record.TryGetField(tag, out var field))
        {
            throw new InvalidDataException($"Prefab override field tag {tag} is absent from the authored component record.");
        }

        ValidateFieldShape(field, fieldDescriptor);

        if (pathIndex == (path.Length - 1))
        {
            return ApplyLeaf(record, field, fieldDescriptor, prefabOverride, limits, value);
        }

        var nextSegment = path[pathIndex + 1];

        if ((fieldDescriptor.WireKind == 22)
            && (nextSegment.Kind == ScenePropertyPathSegmentKind.FieldTag))
        {
            var nestedRecord = record.GetRecord(tag);
            var nestedSchema = ResolveNestedSchema(fieldDescriptor, nestedRecord, schemas);
            var updatedNestedRecord = ApplyPath(
                nestedRecord,
                nestedSchema,
                prefabOverride,
                schemas,
                limits,
                value,
                pathIndex + 1);
            return RewriteRecord(
                record,
                tag,
                removeField: false,
                field.Kind,
                field.Count,
                updatedNestedRecord,
                limits);
        }

        if ((fieldDescriptor.WireKind == 23)
            && (nextSegment.Kind == ScenePropertyPathSegmentKind.FixedArrayIndex)
            && (pathIndex + 2 == path.Length))
        {
            return ApplyFixedArrayElement(
                record,
                field,
                fieldDescriptor,
                prefabOverride,
                limits,
                value,
                nextSegment);
        }

        throw new InvalidDataException("A Prefab override path cannot descend through this field kind.");
    }

    private static ContentFieldDescriptor? FindField(DataSchema schema, uint tag)
    {
        var low = 0;
        var high = schema.Fields.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var field = schema.Fields[middle];

            if (field.Tag == tag)
            {
                return field;
            }

            if (field.Tag < tag)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return null;
    }

    private static bool IsAllowedNestedRole(ContentFieldDescriptor field, ContentSchemaRole role)
    {
        if (field.NestedRoleMask == 0)
        {
            return false;
        }

        var roleBit = 1UL << ((int)role - 1);
        return (field.NestedRoleMask & roleBit) != 0;
    }

    private static LiteralValue ReadLiteralValue(
        SceneAuthoredRecord value,
        CompiledContentSchemaRegistry schemas,
        EbinReadLimits limits)
    {
        if (!schemas.TryGetSchema(value.Schema, out var metadata))
        {
            throw new NotSupportedException($"The Prefab override value schema {value.Schema} is not registered.");
        }

        var allowedSchemas = SceneAssetReader.GetPrefabOverrideLiteralSchemas(schemas);
        var isAllowed = false;

        for (var index = 0; index < allowedSchemas.Length; index++)
        {
            if (allowedSchemas[index] == value.Schema)
            {
                isAllowed = true;
                break;
            }
        }

        if (!isAllowed
            || (metadata.Owner != ContentSchemaOwner.Core)
            || (metadata.Role != ContentSchemaRole.Descriptor)
            || (metadata.Fields.Length != 1)
            || (metadata.Fields[0].Tag != 1)
            || !metadata.Fields[0].IsRequired
            || (metadata.Fields[0].WireKind is < 1 or > 21))
        {
            throw new InvalidDataException("A Prefab override Value is not a member of the registered fixed literal set.");
        }

        var action = new LiteralReadAction(value.Schema, metadata);
        var encodedRecord = value.EncodedRecord;
        var status = EbinStructuralReader.Read(
            encodedRecord.AsSpan(),
            limits,
            EbinReadPolicy.Canonical,
            ref action,
            out var bytesConsumed,
            out var diagnostic);

        if (status is EbinReadStatus.UnsupportedSchema)
        {
            throw new NotSupportedException($"The Prefab override Value uses an unsupported schema: {diagnostic?.Category ?? status.ToString()}.");
        }

        if ((status is not EbinReadStatus.Complete) || (bytesConsumed != encodedRecord.Length))
        {
            throw new InvalidDataException($"The Prefab override Value record is invalid: {diagnostic?.Category ?? status.ToString()}.");
        }

        return action.Result
            ?? throw new InvalidDataException("The Prefab override Value did not produce a literal.");
    }

    private static byte[] RewriteRecord(
        EbinRecordReadView record,
        uint tag,
        bool removeField,
        ushort replacementKind,
        ulong replacementCount,
        byte[]? replacementPayload,
        EbinReadLimits limits)
    {
        if (removeField == (replacementPayload is not null))
        {
            throw new ArgumentException("Record replacement payload presence does not match field removal.", nameof(replacementPayload));
        }

        var header = record.Header;
        var outputFieldCount = record.FieldCount - (removeField ? 1 : 0);
        var fieldSources = new EbinFieldSource[outputFieldCount];
        var payloadBytes = 0;

        for (var index = 0; index < record.FieldCount; index++)
        {
            var field = record.GetField(index);

            if ((field.Tag == tag) && removeField)
            {
                continue;
            }

            var fieldPayloadLength = (field.Tag == tag)
                ? replacementPayload!.Length
                : field.Payload.Length;
            payloadBytes = checked(payloadBytes + fieldPayloadLength);
        }

        var payloadStorage = new byte[payloadBytes];
        var payloadOffset = 0;
        var sourceIndex = 0;
        var foundField = false;

        for (var index = 0; index < record.FieldCount; index++)
        {
            var field = record.GetField(index);

            if (field.Tag == tag)
            {
                foundField = true;

                if (removeField)
                {
                    continue;
                }

                var payload = replacementPayload
                    ?? throw new ArgumentException("A retained field requires a replacement payload.", nameof(replacementPayload));
                payload.CopyTo(payloadStorage.AsSpan(payloadOffset));
                fieldSources[sourceIndex++] = new EbinFieldSource(
                    field.Tag,
                    replacementKind,
                    field.IsRequired ? (ushort)1 : (ushort)0,
                    replacementCount,
                    new EbinPayloadSlice(payloadOffset, payload.Length));
                payloadOffset += payload.Length;
            }
            else
            {
                field.Payload.CopyTo(payloadStorage.AsSpan(payloadOffset));
                fieldSources[sourceIndex++] = new EbinFieldSource(
                    field.Tag,
                    field.Kind,
                    field.IsRequired ? (ushort)1 : (ushort)0,
                    field.Count,
                    new EbinPayloadSlice(payloadOffset, field.Payload.Length));
                payloadOffset += field.Payload.Length;
            }
        }

        if (!foundField)
        {
            throw new InvalidDataException($"Prefab override field tag {tag} is missing from the record being rewritten.");
        }

        var writeView = new EbinRecordWriteView(
            header.SchemaId,
            header.Major,
            header.Minor,
            fieldSources,
            payloadStorage);
        var measureStatus = EbinStructuralWriter.Measure(in writeView, limits, out var requiredBytes, out var diagnostic);

        if (measureStatus is not EbinWriteStatus.Complete)
        {
            throw new InvalidDataException($"The rewritten Prefab component record cannot be measured: {diagnostic?.Category ?? measureStatus.ToString()}.");
        }

        var candidate = new byte[requiredBytes];
        var prepareStatus = EbinStructuralWriter.Prepare(
            in writeView,
            limits,
            candidate,
            out var candidateBytes,
            out diagnostic);

        if ((prepareStatus is not EbinWriteStatus.Complete) || (candidateBytes != requiredBytes))
        {
            throw new InvalidDataException($"The rewritten Prefab component record is invalid: {diagnostic?.Category ?? prepareStatus.ToString()}.");
        }

        return candidate;
    }

    private static void ValidateFieldShape(EbinFieldReadView field, ContentFieldDescriptor descriptor)
    {
        if ((field.Kind != descriptor.WireKind)
            || (field.IsRequired != descriptor.IsRequired)
            || (field.Count < descriptor.MinimumCount)
            || (field.Count > descriptor.MaximumCount))
        {
            throw new InvalidDataException($"Component field {field.Tag} does not match its registered schema descriptor.");
        }
    }

    private static void ValidateReferenceValue(
        ContentFieldDescriptor fieldDescriptor,
        ScenePrefabOverride prefabOverride,
        LiteralValue literal)
    {
        if (fieldDescriptor.Reference == ContentReferenceKind.Entity)
        {
            if ((literal.WireKind != 11)
                || (literal.Payload.Length != 16)
                || (prefabOverride.ReferenceValue is null))
            {
                throw new InvalidDataException("A Prefab Set for an Entity field requires a GUID literal and ReferenceValue.");
            }

            var symbolicValue = new Guid(literal.Payload, bigEndian: true);
            var referenceValue = prefabOverride.ReferenceValue;
            var expectedSymbol = referenceValue.Kind switch {
                SceneReferenceTargetKind.Internal => referenceValue.Entity!.Value.AuthoredEntityId,
                SceneReferenceTargetKind.ExternalSlot => referenceValue.ExternalSlotId!.Value,
                _ => throw new InvalidDataException("A Prefab override reference target kind is not defined."),
            };

            if (symbolicValue == Guid.Empty || (symbolicValue != expectedSymbol))
            {
                throw new InvalidDataException("A Prefab Entity override Value does not match its typed ReferenceValue.");
            }
        }
        else if (prefabOverride.ReferenceValue is not null)
        {
            throw new InvalidDataException("A Prefab ReferenceValue can target only a declared Entity leaf.");
        }
    }

    private struct ApplyAction : IAction<EbinRecordReadView>
    {
        private readonly DataSchema _componentSchema;
        private readonly ScenePrefabOverride _prefabOverride;
        private readonly CompiledContentSchemaRegistry _schemas;
        private readonly EbinReadLimits _limits;

        internal ApplyAction(
            DataSchema componentSchema,
            ScenePrefabOverride prefabOverride,
            CompiledContentSchemaRegistry schemas,
            EbinReadLimits limits)
        {
            _componentSchema = componentSchema;
            _prefabOverride = prefabOverride;
            _schemas = schemas;
            _limits = limits;
            Result = null;
        }

        internal byte[]? Result { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
        {
            var header = record.Header;

            if ((header.SchemaId != _componentSchema.Schema.SchemaId)
                || (header.Major != _componentSchema.Schema.Major)
                || (header.Minor != _componentSchema.Schema.Minor))
            {
                throw new InvalidDataException("The authored component record does not match its registered schema tuple.");
            }

            LiteralValue? literal = null;

            if (_prefabOverride.Operation == ScenePrefabOverrideOperation.Set)
            {
                var value = _prefabOverride.Value
                    ?? throw new InvalidDataException("A Set Prefab override requires a literal Value.");
                literal = ReadLiteralValue(value, _schemas, _limits);
            }

            Result = ApplyPath(
                record,
                _componentSchema,
                _prefabOverride,
                _schemas,
                _limits,
                literal,
                pathIndex: 0);
        }
    }

    private struct LiteralReadAction : IAction<EbinRecordReadView>
    {
        private readonly DataSchema _metadata;
        private readonly ContentSchemaReference _schema;

        internal LiteralReadAction(ContentSchemaReference schema, DataSchema metadata)
        {
            _schema = schema;
            _metadata = metadata;
            Result = null;
        }

        internal LiteralValue? Result { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
        {
            var header = record.Header;

            if ((header.SchemaId != _schema.SchemaId)
                || (header.Major != _schema.Major)
                || (header.Minor != _schema.Minor)
                || (record.FieldCount != 1))
            {
                throw new InvalidDataException("A Prefab override literal record does not match its exact schema tuple.");
            }

            var fieldDescriptor = _metadata.Fields[0];

            if (!record.TryGetField(1, out var field)
                || (field.Kind != fieldDescriptor.WireKind)
                || !field.IsRequired
                || (field.Count < fieldDescriptor.MinimumCount)
                || (field.Count > fieldDescriptor.MaximumCount))
            {
                throw new InvalidDataException("A Prefab override literal does not match its registered Value field.");
            }

            Result = new LiteralValue(field.Kind, field.Count, field.Payload.ToArray());
        }
    }

    private readonly record struct LiteralValue(ushort WireKind, ulong Count, byte[] Payload);
}
