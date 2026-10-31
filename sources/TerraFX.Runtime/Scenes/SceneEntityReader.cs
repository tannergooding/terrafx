// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Scenes;

internal static class SceneEntityReader
{
    // The EBIN record, field header, and 24-byte Position payload are padded to a 16-byte boundary.
    internal const int RootPositionRecordByteLength = 128;

    private static readonly Guid s_entitySchemaId = new ContentSchemaIdentityAssignment("native/entity").SchemaId;
    private static readonly Guid s_localTransformSchemaId = new ContentSchemaIdentityAssignment("scene/local-transform").SchemaId;
    private static readonly Guid s_rootPositionSchemaId = new ContentSchemaIdentityAssignment("scene/root-position").SchemaId;

    internal static Guid LocalTransformSchemaId => s_localTransformSchemaId;

    internal static Guid RootPositionSchemaId => s_rootPositionSchemaId;

    internal static SceneAuthoredComponentRecord CreateRootPositionRecord(scoped ReadOnlySpan<byte> payload)
    {
        ValidateRootPositionPayload(payload);
        var position = new SpatialPosition3(
            new ChunkIndex3(
                BinaryPrimitives.ReadInt32LittleEndian(payload),
                BinaryPrimitives.ReadInt32LittleEndian(payload[4..]),
                BinaryPrimitives.ReadInt32LittleEndian(payload[8..])),
            EbinPrimitives.Vector3(payload[12..]));
        var record = new EbinRecord(s_rootPositionSchemaId, 1, 0, [EbinPrimitives.Position(1, position)]);
        var status = EbinCodec.Prepare(record, out var encodedRecord, out var diagnostic);

        if (status != EbinStatus.Complete)
        {
            throw new InvalidDataException($"The Prefab RootPose RootPosition could not be encoded: {diagnostic.Category}.");
        }

        return new SceneAuthoredComponentRecord(
            new ContentSchemaReference(s_rootPositionSchemaId, 1, 0, ContentSchemaScope.Content),
            encodedRecord);
    }

    internal static SceneEntity Read(
        EbinRecordReadView record,
        CompiledContentSchemaRegistry schemas,
        EbinReadLimits limits,
        ImmutableArray<SceneEntityReferenceBinding> referenceBindings,
        bool allowInstanceAbsoluteReferenceSources,
        out int entityReferenceLeafCount)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(limits);
        entityReferenceLeafCount = 0;
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

            if (header.SchemaId == s_rootPositionSchemaId)
            {
                ValidateRootPositionRecord(componentRecord);
            }

            if (metadata.Role == ContentSchemaRole.Component)
            {
                entityReferenceLeafCount = checked(
                    entityReferenceLeafCount
                    + SceneEntityReferenceBindingValidator.ValidateComponentRecord(
                        componentRecord,
                        entityId,
                        schema,
                        metadata,
                        referenceBindings,
                        schemas,
                        allowInstanceAbsoluteReferenceSources));
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

    internal static Vector3 ReadLocalTransformPosition(
        SceneAuthoredComponentRecord component,
        EbinReadLimits limits)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(limits);
        var action = new LocalTransformPositionReadAction();
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
            throw new NotSupportedException($"The LocalTransform record uses an unsupported schema: {diagnostic?.Category ?? status.ToString()}.");
        }

        if ((status is not EbinReadStatus.Complete) || (bytesConsumed != encodedRecord.Length))
        {
            throw new InvalidDataException($"The LocalTransform record is invalid: {diagnostic?.Category ?? status.ToString()}.");
        }

        return action.Position;
    }

    internal static SceneAuthoredComponentRecord ReadLocalTransformRecord(
        EbinRecordReadView record,
        out Vector3 position)
    {
        position = ReadLocalTransformPosition(record);
        var header = record.Header;
        var schema = new ContentSchemaReference(
            header.SchemaId,
            header.Major,
            header.Minor,
            ContentSchemaScope.Content);
        return new SceneAuthoredComponentRecord(schema, record.EncodedBytes);
    }

    internal static void ValidateRootPositionComponentRecord(
        SceneAuthoredComponentRecord component,
        EbinReadLimits limits)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(limits);
        var action = new RootPositionValidationAction();
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
            throw new NotSupportedException($"The RootPosition record uses an unsupported schema: {diagnostic?.Category ?? status.ToString()}.");
        }

        if ((status is not EbinReadStatus.Complete) || (bytesConsumed != encodedRecord.Length))
        {
            throw new InvalidDataException($"The RootPosition record is invalid: {diagnostic?.Category ?? status.ToString()}.");
        }
    }

    internal static void ValidateRootPositionPayload(scoped ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 24)
        {
            throw new InvalidDataException("The RootPosition payload must contain exactly 24 bytes.");
        }

        if (!EbinPrimitives.Vector3(payload[12..]).IsFinite().All())
        {
            throw new InvalidDataException("The RootPosition offset must be finite.");
        }
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
        var rotation = GetField(record, tag: 2, kind: 17, payloadLength: 16);
        var scale = GetField(record, tag: 3, kind: 15, payloadLength: 12);
        var positionValue = EbinPrimitives.Vector3(position.Payload);
        var rotationValue = new Quaternion(
            EbinPrimitives.Single(rotation.Payload),
            EbinPrimitives.Single(rotation.Payload[4..]),
            EbinPrimitives.Single(rotation.Payload[8..]),
            EbinPrimitives.Single(rotation.Payload[12..]));
        var scaleValue = EbinPrimitives.Vector3(scale.Payload);

        try
        {
            _ = SceneComponentDefinitions.CreateAssignedLocalTransform(positionValue, rotationValue, scaleValue);
        }
        catch (ArgumentException error)
        {
            throw new InvalidDataException("The authored LocalTransform is invalid.", error);
        }

        return positionValue;
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

    private static void ValidateRootPositionRecord(EbinRecordReadView record)
    {
        ValidateHeader(record, s_rootPositionSchemaId, major: 1, fieldCount: 1);
        var position = GetField(record, tag: 1, kind: 21, payloadLength: 24);
        ValidateRootPositionPayload(position.Payload);
    }

    private struct LocalTransformPositionReadAction : IAction<EbinRecordReadView>
    {
        internal Vector3 Position { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
            => Position = ReadLocalTransformPosition(record);
    }

    private struct RootPositionValidationAction : IAction<EbinRecordReadView>
    {
        public readonly void Invoke(scoped EbinRecordReadView record)
            => ValidateRootPositionRecord(record);
    }
}
