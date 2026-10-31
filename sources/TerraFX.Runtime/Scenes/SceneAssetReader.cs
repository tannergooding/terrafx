// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using TerraFX;
using TerraFX.Assets;

namespace TerraFX.Scenes;

internal static class SceneAssetReader
{
    private static readonly Guid s_assetReferenceSchemaId = new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId;
    private static readonly Guid s_contentSchemaReferenceSchemaId = new ContentSchemaIdentityAssignment("content/schema-ref").SchemaId;
    private static readonly Guid s_entityReferenceBindingSchemaId = new ContentSchemaIdentityAssignment("native/entity-reference-binding").SchemaId;
    private static readonly Guid s_entitySelectorSchemaId = new ContentSchemaIdentityAssignment("native/entity-selector").SchemaId;
    private static readonly Guid s_externalSlotSchemaId = new ContentSchemaIdentityAssignment("native/external-slot").SchemaId;
    private static readonly Guid s_placementSlotBindingSchemaId = new ContentSchemaIdentityAssignment("native/placement-slot-binding").SchemaId;
    private static readonly Guid s_prefabOverrideSchemaId = new ContentSchemaIdentityAssignment("native/prefab-override").SchemaId;
    private static readonly Guid s_prefabPlacementSchemaId = new ContentSchemaIdentityAssignment("native/prefab-placement").SchemaId;
    private static readonly Guid s_prefabRootPoseSchemaId = new ContentSchemaIdentityAssignment("native/prefab-root-pose").SchemaId;
    private static readonly Guid s_prefabSchemaId = new ContentSchemaIdentityAssignment("native/prefab").SchemaId;
    private static readonly Guid s_propertySegmentSchemaId = new ContentSchemaIdentityAssignment("content/property-segment").SchemaId;
    private static readonly Guid s_referenceTargetSchemaId = new ContentSchemaIdentityAssignment("native/reference-target").SchemaId;
    private static readonly Guid s_rootPositionSchemaId = new ContentSchemaIdentityAssignment("scene/root-position").SchemaId;
    private static readonly Guid s_sceneSchemaId = new ContentSchemaIdentityAssignment("native/scene").SchemaId;

    internal static SceneHierarchyValidationWorkspace CreatePrefabValidationWorkspace(
        EbinRecordReadView record,
        AssetId expectedPrefabId,
        EbinReadLimits limits)
        => CreateValidationWorkspace(
            record,
            expectedPrefabId,
            s_prefabSchemaId,
            entityTag: 3,
            placementTag: 5,
            maximumEntityCount: 65_536,
            maximumPlacementCount: 1_048_576,
            fieldCount: 6,
            limits: limits);

    internal static SceneHierarchyValidationWorkspace CreateSceneValidationWorkspace(
        EbinRecordReadView record,
        AssetId expectedSceneId,
        EbinReadLimits limits)
        => CreateValidationWorkspace(
            record,
            expectedSceneId,
            s_sceneSchemaId,
            entityTag: 2,
            placementTag: 4,
            maximumEntityCount: 1_048_576,
            maximumPlacementCount: 1_048_576,
            fieldCount: 7,
            limits: limits);

    internal static SceneAsset Read(
        EbinRecordReadView record,
        AssetId expectedSceneId,
        CompiledContentSchemaRegistry schemas,
        EbinReadLimits limits,
        SceneHierarchyValidationWorkspace hierarchyWorkspace,
        int maximumHierarchyDepth)
    {
        if (!expectedSceneId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedSceneId));
        }

        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(hierarchyWorkspace);
        ValidateHeader(record, s_sceneSchemaId, major: 2, fieldCount: 7);

        var sceneId = ReadAssetId(record, tag: 1);

        if (sceneId != expectedSceneId)
        {
            throw new InvalidDataException("The SceneId does not match the enclosing AssetId.");
        }

        var entityField = GetRecordArrayField(record, tag: 2, maximumCount: 1_048_576, limits);
        var placementField = GetRecordArrayField(record, tag: 4, maximumCount: 1_048_576, limits);
        var referenceField = GetRecordArrayField(record, tag: 6, maximumCount: 1_048_576, limits);
        var overrideField = GetRecordArrayField(record, tag: 7, maximumCount: 65_536, limits);
        var externalSlotField = GetRecordArrayField(record, tag: 8, maximumCount: 65_536, limits);
        var entityCount = checked((int)entityField.Count);
        var placementCount = checked((int)placementField.Count);

        if ((hierarchyWorkspace.EntityCount != entityCount)
            || (hierarchyWorkspace.PlacementCount != placementCount))
        {
            throw new InvalidOperationException("The hierarchy validation workspace does not match the Scene entity and placement counts.");
        }

        var entities = ImmutableArray.CreateBuilder<SceneEntity>(entityCount);
        var entityIds = hierarchyWorkspace.EntityIds;
        var previousEntityId = Guid.Empty;

        for (var entityIndex = 0; entityIndex < entityCount; entityIndex++)
        {
            var entity = SceneEntityReader.Read(record.GetRecordArrayElement(2, entityIndex), schemas, limits);

            if (previousEntityId != Guid.Empty
                && (FoundationCatalogue.CompareRfc(previousEntityId, entity.AuthoredEntityId) >= 0))
            {
                throw new InvalidDataException("Scene Entity records are not in strictly increasing RFC EntityId order.");
            }

            previousEntityId = entity.AuthoredEntityId;
            entityIds[entityIndex] = entity.AuthoredEntityId;
            entities.Add(entity);
        }

        var hierarchy = SceneHierarchyReader.Read(record.GetRecord(3).EncodedBytes, limits);
        var placements = ImmutableArray.CreateBuilder<ScenePrefabPlacement>(placementCount);
        var placementIds = hierarchyWorkspace.Placements;
        var previousPlacementId = Guid.Empty;

        for (var placementIndex = 0; placementIndex < placementCount; placementIndex++)
        {
            var placement = ReadPrefabPlacement(
                record.GetRecordArrayElement(4, placementIndex),
                schemas,
                limits,
                allowInternalOverrideReferences: true);

            if (previousPlacementId != Guid.Empty
                && (FoundationCatalogue.CompareRfc(previousPlacementId, placement.PlacementId) >= 0))
            {
                throw new InvalidDataException("Scene PrefabPlacement records are not in strictly increasing RFC PlacementId order.");
            }

            previousPlacementId = placement.PlacementId;
            placementIds[placementIndex] = new SceneHierarchyPlacement(placement.PlacementId, placement.ParentEntityId);
            placements.Add(placement);
        }

        var validation = SceneHierarchyValidator.Validate(
            hierarchy,
            entityIds,
            placementIds,
            hierarchyWorkspace,
            maximumHierarchyDepth);

        if (!validation.IsValid)
        {
            throw new InvalidDataException(
                $"The Scene hierarchy is invalid: {validation.Error} ({validation.SubjectId:N}).");
        }

        ValidateRootPositionComponents(entities, hierarchy);

        var references = ReadAuthoredRecords(
            record,
            tag: 6,
            referenceField,
            s_entityReferenceBindingSchemaId,
            maximumCount: 1_048_576);
        var overrides = ReadAuthoredRecords(
            record,
            tag: 7,
            overrideField,
            s_prefabOverrideSchemaId,
            maximumCount: 65_536);
        var externalSlots = ReadExternalSlots(record, tag: 8, externalSlotField, schemas, limits);

        return new SceneAsset(
            sceneId,
            entities.MoveToImmutable(),
            hierarchy,
            placements.MoveToImmutable(),
            references,
            overrides,
            externalSlots,
            [.. record.EncodedBytes]);
    }

    internal static ScenePrefabAsset ReadPrefab(
        EbinRecordReadView record,
        AssetId expectedPrefabId,
        CompiledContentSchemaRegistry schemas,
        EbinReadLimits limits,
        SceneHierarchyValidationWorkspace hierarchyWorkspace,
        int maximumHierarchyDepth)
    {
        if (!expectedPrefabId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedPrefabId));
        }

        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(hierarchyWorkspace);
        ValidateHeader(record, s_prefabSchemaId, major: 2, fieldCount: 6);

        var prefabId = ReadAssetId(record, tag: 1);

        if (prefabId != expectedPrefabId)
        {
            throw new InvalidDataException("The PrefabId does not match the enclosing AssetId.");
        }

        var entityField = GetRecordArrayField(record, tag: 3, maximumCount: 65_536, limits);
        var placementField = GetRecordArrayField(record, tag: 5, maximumCount: 1_048_576, limits);
        var referenceField = GetRecordArrayField(record, tag: 6, maximumCount: 1_048_576, limits);
        var externalSlotField = GetRecordArrayField(record, tag: 8, maximumCount: 65_536, limits);
        var entityCount = checked((int)entityField.Count);
        var placementCount = checked((int)placementField.Count);

        if ((hierarchyWorkspace.EntityCount != entityCount)
            || (hierarchyWorkspace.PlacementCount != placementCount))
        {
            throw new InvalidOperationException("The hierarchy validation workspace does not match the Prefab entity and placement counts.");
        }

        var entities = ImmutableArray.CreateBuilder<SceneEntity>(entityCount);
        var entityIds = hierarchyWorkspace.EntityIds;
        var previousEntityId = Guid.Empty;

        for (var entityIndex = 0; entityIndex < entityCount; entityIndex++)
        {
            var entity = SceneEntityReader.Read(record.GetRecordArrayElement(3, entityIndex), schemas, limits);

            if (previousEntityId != Guid.Empty
                && (FoundationCatalogue.CompareRfc(previousEntityId, entity.AuthoredEntityId) >= 0))
            {
                throw new InvalidDataException("Prefab Entity records are not in strictly increasing RFC EntityId order.");
            }

            previousEntityId = entity.AuthoredEntityId;
            entityIds[entityIndex] = entity.AuthoredEntityId;
            entities.Add(entity);
        }

        var hierarchy = SceneHierarchyReader.Read(record.GetRecord(4).EncodedBytes, limits);
        var placements = ImmutableArray.CreateBuilder<ScenePrefabPlacement>(placementCount);
        var placementIds = hierarchyWorkspace.Placements;
        var previousPlacementId = Guid.Empty;

        for (var placementIndex = 0; placementIndex < placementCount; placementIndex++)
        {
            var placement = ReadPrefabPlacement(
                record.GetRecordArrayElement(5, placementIndex),
                schemas,
                limits,
                allowInternalOverrideReferences: false);

            if (previousPlacementId != Guid.Empty
                && (FoundationCatalogue.CompareRfc(previousPlacementId, placement.PlacementId) >= 0))
            {
                throw new InvalidDataException("Prefab Placement records are not in strictly increasing RFC PlacementId order.");
            }

            previousPlacementId = placement.PlacementId;
            placementIds[placementIndex] = new SceneHierarchyPlacement(placement.PlacementId, placement.ParentEntityId);
            placements.Add(placement);
        }

        var validation = SceneHierarchyValidator.Validate(
            hierarchy,
            entityIds,
            placementIds,
            hierarchyWorkspace,
            maximumHierarchyDepth);

        if (!validation.IsValid)
        {
            throw new InvalidDataException(
                $"The Prefab hierarchy is invalid: {validation.Error} ({validation.SubjectId:N}).");
        }

        var references = ReadAuthoredRecords(
            record,
            tag: 6,
            referenceField,
            s_entityReferenceBindingSchemaId,
            maximumCount: 1_048_576);
        var externalSlots = ReadExternalSlots(record, tag: 8, externalSlotField, schemas, limits);

        return new ScenePrefabAsset(
            prefabId,
            entities.MoveToImmutable(),
            hierarchy,
            placements.MoveToImmutable(),
            references,
            externalSlots,
            [.. record.EncodedBytes]);
    }

    private static int CompareSchemaReferences(ContentSchemaReference left, ContentSchemaReference right)
    {
        var comparison = FoundationCatalogue.CompareRfc(left.SchemaId, right.SchemaId);

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.Major.CompareTo(right.Major);

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.Minor.CompareTo(right.Minor);
        return (comparison != 0) ? comparison : left.Scope.CompareTo(right.Scope);
    }

    private static SceneHierarchyValidationWorkspace CreateValidationWorkspace(
        EbinRecordReadView record,
        AssetId expectedAssetId,
        Guid schemaId,
        uint entityTag,
        uint placementTag,
        int maximumEntityCount,
        int maximumPlacementCount,
        int fieldCount,
        EbinReadLimits limits)
    {
        if (!expectedAssetId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedAssetId));
        }

        ArgumentNullException.ThrowIfNull(limits);
        ValidateHeader(record, schemaId, major: 2, fieldCount: fieldCount);

        if (ReadAssetId(record, tag: 1) != expectedAssetId)
        {
            throw new InvalidDataException("The asset identity does not match the enclosing AssetId.");
        }

        var entityField = GetRecordArrayField(record, entityTag, maximumEntityCount, limits);
        var placementField = GetRecordArrayField(record, placementTag, maximumPlacementCount, limits);
        return new SceneHierarchyValidationWorkspace(
            checked((int)entityField.Count),
            checked((int)placementField.Count));
    }

    private static EbinFieldReadView GetFixedGuidArrayField(
        EbinRecordReadView record,
        uint tag,
        int maximumCount,
        EbinReadLimits limits)
    {
        if (!record.TryGetField(tag, out var field)
            || (field.Kind != 23)
            || !field.IsRequired
            || (field.Count > checked((ulong)maximumCount))
            || (field.Count > checked((ulong)limits.MaximumArrayElements))
            || (field.Count > int.MaxValue)
            || (field.Payload.Length != checked(32 + (checked((int)field.Count) * 16)))
            || (BinaryPrimitives.ReadUInt16LittleEndian(field.Payload[16..]) != 11)
            || (BinaryPrimitives.ReadUInt32LittleEndian(field.Payload[20..]) != 16))
        {
            throw new InvalidDataException($"Scene fixed GUID array field {tag} does not match its schema.");
        }

        return field;
    }

    private static ImmutableArray<ContentSchemaReference> GetPrefabOverrideLiteralSchemas(
        CompiledContentSchemaRegistry schemas)
    {
        var prefabOverrideSchema = new ContentSchemaReference(
            s_prefabOverrideSchemaId,
            1,
            0,
            ContentSchemaScope.Content);

        if (!schemas.TryGetSchema(prefabOverrideSchema, out var metadata)
            || (metadata.Role != ContentSchemaRole.Auxiliary))
        {
            throw new NotSupportedException("The selected schema registry does not contain the native/prefab-override descriptor.");
        }

        foreach (var field in metadata.Fields)
        {
            if (field.Tag == 4)
            {
                if ((field.WireKind != 22)
                    || field.IsRequired
                    || field.NestedSchemas.IsEmpty
                    || (field.NestedRoleMask != 0))
                {
                    throw new InvalidOperationException("The native/prefab-override Value field does not expose its fixed literal schema set.");
                }

                return field.NestedSchemas;
            }
        }

        throw new InvalidOperationException("The native/prefab-override descriptor has no Value field.");
    }

    private static EbinFieldReadView GetRecordArrayField(
        EbinRecordReadView record,
        uint tag,
        int maximumCount,
        EbinReadLimits limits)
    {
        if (!record.TryGetField(tag, out var field)
            || (field.Kind != 24)
            || !field.IsRequired
            || (field.Count > checked((ulong)maximumCount))
            || (field.Count > checked((ulong)limits.MaximumArrayElements))
            || (field.Count > int.MaxValue))
        {
            throw new InvalidDataException($"Scene record-array field {tag} does not match its schema.");
        }

        return field;
    }

    private static AssetId ReadAssetId(EbinRecordReadView record, uint tag)
    {
        if (!record.TryGetField(tag, out var field)
            || (field.Kind != 3)
            || !field.IsRequired
            || (field.Count != 1)
            || (field.Payload.Length != sizeof(uint)))
        {
            throw new InvalidDataException($"Scene AssetId field {tag} does not match its schema.");
        }

        var value = BinaryPrimitives.ReadUInt32LittleEndian(field.Payload);

        if (value is 0 or > int.MaxValue)
        {
            throw new InvalidDataException("The Scene AssetId is outside the admitted signed identity range.");
        }

        return new AssetId(checked((int)value));
    }

    private static ImmutableArray<SceneAuthoredRecord> ReadAuthoredRecords(
        EbinRecordReadView record,
        uint tag,
        EbinFieldReadView field,
        Guid schemaId,
        int maximumCount)
    {
        if (field.Count > checked((ulong)maximumCount))
        {
            throw new InvalidDataException($"Scene record-array field {tag} exceeds its selected schema limit.");
        }

        var count = checked((int)field.Count);
        var records = ImmutableArray.CreateBuilder<SceneAuthoredRecord>(count);

        for (var index = 0; index < count; index++)
        {
            var nestedRecord = record.GetRecordArrayElement(tag, index);
            ValidateSchema(nestedRecord, schemaId, major: 1);
            var header = nestedRecord.Header;
            var schema = new ContentSchemaReference(
                header.SchemaId,
                header.Major,
                header.Minor,
                ContentSchemaScope.Content);
            records.Add(new SceneAuthoredRecord(schema, nestedRecord.EncodedBytes));
        }

        return records.MoveToImmutable();
    }

    private static bool ReadBoolean(EbinRecordReadView record, uint tag)
    {
        if (!record.TryGetField(tag, out var field)
            || (field.Kind != 9)
            || !field.IsRequired
            || (field.Count != 1)
            || (field.Payload.Length != sizeof(byte))
            || (field.Payload[0] > 1))
        {
            throw new InvalidDataException($"Scene Boolean field {tag} does not match its schema.");
        }

        return field.Payload[0] != 0;
    }

    private static SceneEntitySelector ReadEntitySelector(EbinRecordReadView record, EbinReadLimits limits)
    {
        ValidateHeader(record, s_entitySelectorSchemaId, major: 1, fieldCount: 3);
        var scope = ReadUInt8(record, tag: 1);
        var placementPathField = GetFixedGuidArrayField(record, tag: 2, maximumCount: 32, limits);
        var pathCount = checked((int)placementPathField.Count);
        var placementPath = ImmutableArray.CreateBuilder<Guid>(pathCount);
        var pathPayload = placementPathField.Payload[32..];

        for (var index = 0; index < pathCount; index++)
        {
            placementPath.Add(ReadGuidPayload(pathPayload.Slice(index * 16, 16)));
        }

        if (scope is not (byte)SceneEntitySelectorScope.OccurrenceRelative
            and not (byte)SceneEntitySelectorScope.InstanceAbsolute)
        {
            throw new InvalidDataException("An EntitySelector scope is not defined by the schema.");
        }

        return new SceneEntitySelector(
            (SceneEntitySelectorScope)scope,
            placementPath.MoveToImmutable(),
            ReadGuid(record, tag: 3, isRequired: true));
    }

    private static SceneExternalSlot ReadExternalSlot(
        EbinRecordReadView record,
        CompiledContentSchemaRegistry schemas,
        EbinReadLimits limits)
    {
        ValidateHeader(record, s_externalSlotSchemaId, major: 1, fieldCount: 4);
        var slotId = ReadGuid(record, tag: 1, isRequired: true);
        var required = ReadBoolean(record, tag: 2);
        var allowNull = ReadBoolean(record, tag: 3);

        if (required && allowNull)
        {
            throw new InvalidDataException("A required external slot cannot allow a null binding.");
        }

        var requiredComponentsField = GetRecordArrayField(record, tag: 4, maximumCount: 256, limits);
        var count = checked((int)requiredComponentsField.Count);
        var requiredComponents = ImmutableArray.CreateBuilder<ContentSchemaReference>(count);
        ContentSchemaReference? previousSchema = null;

        for (var index = 0; index < count; index++)
        {
            var schema = ReadSchemaReference(record.GetRecordArrayElement(4, index));

            if (schema.Scope != ContentSchemaScope.Content)
            {
                throw new InvalidDataException("An external-slot RequiredComponents entry must use Content scope.");
            }

            if (!schemas.TryGetSchema(schema, out var metadata))
            {
                throw new NotSupportedException($"The external-slot RequiredComponents schema {schema} is not registered.");
            }

            if (metadata.Role is not ContentSchemaRole.Component and not ContentSchemaRole.EntityTag)
            {
                throw new InvalidDataException("An external-slot RequiredComponents entry must identify a Component or EntityTag.");
            }

            if (previousSchema is { } previous && (CompareSchemaReferences(previous, schema) >= 0))
            {
                throw new InvalidDataException("External-slot RequiredComponents are not uniquely sorted by schema tuple.");
            }

            requiredComponents.Add(schema);
            previousSchema = schema;
        }

        return new SceneExternalSlot(slotId, required, allowNull, requiredComponents.MoveToImmutable());
    }

    private static ImmutableArray<SceneExternalSlot> ReadExternalSlots(
        EbinRecordReadView record,
        uint tag,
        EbinFieldReadView field,
        CompiledContentSchemaRegistry schemas,
        EbinReadLimits limits)
    {
        var count = checked((int)field.Count);
        var externalSlots = ImmutableArray.CreateBuilder<SceneExternalSlot>(count);
        Guid? previousSlotId = null;

        for (var index = 0; index < count; index++)
        {
            var externalSlot = ReadExternalSlot(record.GetRecordArrayElement(tag, index), schemas, limits);

            if (previousSlotId is { } previous
                && (FoundationCatalogue.CompareRfc(previous, externalSlot.SlotId) >= 0))
            {
                throw new InvalidDataException("External slots are not uniquely sorted by RFC SlotId.");
            }

            externalSlots.Add(externalSlot);
            previousSlotId = externalSlot.SlotId;
        }

        return externalSlots.MoveToImmutable();
    }

    private static Guid ReadGuid(EbinRecordReadView record, uint tag, bool isRequired)
    {
        if (!record.TryGetField(tag, out var field)
            || (field.Kind != 11)
            || (field.IsRequired != isRequired)
            || (field.Count != 1)
            || (field.Payload.Length != 16))
        {
            throw new InvalidDataException($"Scene GUID field {tag} does not match its schema.");
        }

        return ReadGuidPayload(field.Payload);
    }

    private static Guid ReadGuidPayload(scoped ReadOnlySpan<byte> payload)
    {
        var id = new Guid(payload, bigEndian: true);

        if (id == Guid.Empty)
        {
            throw new InvalidDataException("Scene GUID values must be nonempty.");
        }

        return id;
    }

    private static ScenePlacementSlotBinding ReadPlacementSlotBinding(
        EbinRecordReadView record,
        EbinReadLimits limits)
    {
        var hasTarget = record.TryGetField(2, out var targetField);
        ValidateHeader(record, s_placementSlotBindingSchemaId, major: 1, fieldCount: hasTarget ? 2 : 1);
        var slotId = ReadGuid(record, tag: 1, isRequired: true);
        SceneReferenceTarget? target = null;

        if (hasTarget)
        {
            if ((targetField.Kind != 22)
                || targetField.IsRequired
                || (targetField.Count != 1))
            {
                throw new InvalidDataException("The PrefabPlacement slot target is not an optional reference record.");
            }

            target = ReadReferenceTarget(record.GetRecord(2), limits);
        }

        return new ScenePlacementSlotBinding(slotId, target);
    }

    private static ImmutableArray<ScenePlacementSlotBinding> ReadPlacementSlotBindings(
        EbinRecordReadView record,
        EbinFieldReadView field,
        EbinReadLimits limits)
    {
        var count = checked((int)field.Count);
        var bindings = ImmutableArray.CreateBuilder<ScenePlacementSlotBinding>(count);

        for (var index = 0; index < count; index++)
        {
            bindings.Add(ReadPlacementSlotBinding(record.GetRecordArrayElement(6, index), limits));
        }

        return bindings.MoveToImmutable();
    }

    private static ScenePrefabOverride ReadPrefabOverride(
        EbinRecordReadView record,
        CompiledContentSchemaRegistry schemas,
        EbinReadLimits limits,
        ImmutableArray<ContentSchemaReference> fixedLiteralSchemas,
        bool allowInternalOverrideReferences)
    {
        var hasValue = record.TryGetField(4, out var valueField);
        var hasOperation = record.TryGetField(5, out var operationField);
        var hasReferenceValue = record.TryGetField(6, out var referenceValueField);
        var fieldCount = 3 + (hasValue ? 1 : 0) + (hasOperation ? 1 : 0) + (hasReferenceValue ? 1 : 0);
        ValidateHeader(record, s_prefabOverrideSchemaId, major: 1, fieldCount);

        var target = ReadEntitySelector(record.GetRecord(1), limits);

        if (target.Scope is not SceneEntitySelectorScope.OccurrenceRelative)
        {
            throw new InvalidDataException("A PrefabPlacement override target must be occurrence-relative.");
        }

        var componentSchema = ReadSchemaReference(record.GetRecord(2));

        if (componentSchema.Scope != ContentSchemaScope.Content)
        {
            throw new InvalidDataException("A PrefabPlacement override component schema must use Content scope.");
        }

        if (!schemas.TryGetSchema(componentSchema, out var componentMetadata))
        {
            throw new NotSupportedException($"The PrefabPlacement override component schema {componentSchema} is not registered.");
        }

        if (componentMetadata.Role != ContentSchemaRole.Component)
        {
            throw new InvalidDataException("A PrefabPlacement override schema does not identify a Component.");
        }

        var propertyPathField = GetRecordArrayField(record, tag: 3, maximumCount: 64, limits);

        if (propertyPathField.Count == 0)
        {
            throw new InvalidDataException("A PrefabPlacement override property path cannot be empty.");
        }

        var propertyPath = ReadPropertyPath(record, tag: 3, propertyPathField);
        var operation = ScenePrefabOverrideOperation.Set;

        if (hasOperation)
        {
            if ((operationField.Kind != 1)
                || operationField.IsRequired
                || (operationField.Count != 1)
                || (operationField.Payload.Length != sizeof(byte))
                || (operationField.Payload[0] > (byte)ScenePrefabOverrideOperation.Omit))
            {
                throw new InvalidDataException("A PrefabPlacement override operation is not defined by the schema.");
            }

            operation = (ScenePrefabOverrideOperation)operationField.Payload[0];
        }

        SceneAuthoredRecord? value = null;
        EbinRecordReadView valueRecord = default;

        if (hasValue)
        {
            if ((valueField.Kind != 22)
                || valueField.IsRequired
                || (valueField.Count != 1))
            {
                throw new InvalidDataException("A PrefabPlacement override value is not an optional literal record.");
            }

            valueRecord = record.GetRecord(4);
            var valueHeader = valueRecord.Header;
            var valueSchema = new ContentSchemaReference(
                valueHeader.SchemaId,
                valueHeader.Major,
                valueHeader.Minor,
                ContentSchemaScope.Content);

            var isFixedLiteralSchema = false;

            for (var index = 0; index < fixedLiteralSchemas.Length; index++)
            {
                if (fixedLiteralSchemas[index] == valueSchema)
                {
                    isFixedLiteralSchema = true;
                    break;
                }
            }

            if (!schemas.TryGetSchema(valueSchema, out var valueMetadata))
            {
                throw new NotSupportedException($"The PrefabPlacement override value schema {valueSchema} is not registered.");
            }

            if (!isFixedLiteralSchema
                || (valueMetadata.Owner != ContentSchemaOwner.Core)
                || (valueMetadata.Role != ContentSchemaRole.Descriptor)
                || !valueMetadata.IdentityAssignment.Name.StartsWith("content/literal/", StringComparison.Ordinal)
                || (valueMetadata.Fields.Length != 1)
                || (valueMetadata.Fields[0].Tag != 1)
                || !valueMetadata.Fields[0].IsRequired
                || (valueMetadata.Fields[0].WireKind is < 1 or > 21))
            {
                throw new InvalidDataException("A PrefabPlacement override value is not a fixed Core literal schema.");
            }

            ValidateHeader(valueRecord, valueSchema.SchemaId, major: 1, fieldCount: 1);
            var literalField = valueMetadata.Fields[0];

            if (!valueRecord.TryGetField(1, out var literalValueField)
                || (literalValueField.Kind != literalField.WireKind)
                || !literalValueField.IsRequired
                || (literalValueField.Count < literalField.MinimumCount)
                || (literalValueField.Count > literalField.MaximumCount))
            {
                throw new InvalidDataException("A PrefabPlacement override literal does not match its registered fixed field.");
            }

            value = new SceneAuthoredRecord(valueSchema, valueRecord.EncodedBytes);
        }

        SceneReferenceTarget? referenceValue = null;

        if (hasReferenceValue)
        {
            if ((referenceValueField.Kind != 22)
                || referenceValueField.IsRequired
                || (referenceValueField.Count != 1))
            {
                throw new InvalidDataException("A PrefabPlacement override reference value is not an optional reference record.");
            }

            referenceValue = ReadReferenceTarget(record.GetRecord(6), limits);

            if (value is null)
            {
                throw new InvalidDataException("A Prefab override ReferenceValue requires a literal Value.");
            }

            if ((referenceValue.Kind == SceneReferenceTargetKind.Internal)
                && !allowInternalOverrideReferences)
            {
                throw new InvalidDataException("A reusable Prefab cannot contain an Internal Prefab override reference.");
            }

            if (!schemas.TryGetSchema(value.Schema, out var literalMetadata))
            {
                throw new InvalidDataException("The Prefab override literal schema is not registered.");
            }

            var literalField = literalMetadata.Fields[0];

            if (literalField.WireKind != 11
                || !valueRecord.TryGetField(1, out var referenceLiteralField)
                || (referenceLiteralField.Kind != 11)
                || !referenceLiteralField.IsRequired
                || (referenceLiteralField.Count != 1)
                || (referenceLiteralField.Payload.Length != 16))
            {
                throw new InvalidDataException("A Prefab override ReferenceValue requires a GUID literal Value.");
            }

            var symbolicValue = ReadGuidPayload(referenceLiteralField.Payload);
            var expectedSymbol = referenceValue.Kind switch {
                SceneReferenceTargetKind.Internal => referenceValue.Entity!.Value.AuthoredEntityId,
                SceneReferenceTargetKind.ExternalSlot => referenceValue.ExternalSlotId!.Value,
                _ => throw new InvalidDataException("A Prefab override reference kind is not defined."),
            };

            if (symbolicValue != expectedSymbol)
            {
                throw new InvalidDataException("A Prefab override GUID literal does not match its typed reference target.");
            }
        }

        if ((operation == ScenePrefabOverrideOperation.Set) && (value is null))
        {
            throw new InvalidDataException("A Set PrefabPlacement override requires a value record.");
        }

        if ((operation == ScenePrefabOverrideOperation.Omit)
            && ((value is not null) || (referenceValue is not null)))
        {
            throw new InvalidDataException("An Omit PrefabPlacement override cannot carry a value or reference target.");
        }

        return new ScenePrefabOverride(
            target,
            componentSchema,
            propertyPath,
            operation,
            value,
            referenceValue);
    }

    private static ImmutableArray<ScenePrefabOverride> ReadPrefabOverrides(
        EbinRecordReadView record,
        EbinFieldReadView field,
        CompiledContentSchemaRegistry schemas,
        EbinReadLimits limits,
        bool allowInternalOverrideReferences)
    {
        var count = checked((int)field.Count);
        var overrides = ImmutableArray.CreateBuilder<ScenePrefabOverride>(count);

        if (count == 0)
        {
            return overrides.MoveToImmutable();
        }

        var fixedLiteralSchemas = GetPrefabOverrideLiteralSchemas(schemas);

        for (var index = 0; index < count; index++)
        {
            overrides.Add(ReadPrefabOverride(
                record.GetRecordArrayElement(5, index),
                schemas,
                limits,
                fixedLiteralSchemas,
                allowInternalOverrideReferences));
        }

        return overrides.MoveToImmutable();
    }

    private static ScenePrefabPlacement ReadPrefabPlacement(
        EbinRecordReadView record,
        CompiledContentSchemaRegistry schemas,
        EbinReadLimits limits,
        bool allowInternalOverrideReferences)
    {
        var hasParent = record.TryGetField(3, out var parentField);
        ValidateHeader(record, s_prefabPlacementSchemaId, major: 1, fieldCount: hasParent ? 6 : 5);
        var placementId = ReadGuid(record, tag: 1, isRequired: true);
        var prefabAssetId = ReadPrefabReference(record.GetRecord(2));
        Guid? parentEntityId = null;

        if (hasParent)
        {
            if ((parentField.Kind != 11)
                || parentField.IsRequired
                || (parentField.Count != 1)
                || (parentField.Payload.Length != 16))
            {
                throw new InvalidDataException("The PrefabPlacement parent field is not an optional single Entity GUID.");
            }

            parentEntityId = ReadGuidPayload(parentField.Payload);
        }

        var rootPoseField = GetRecordArrayField(record, tag: 4, maximumCount: 65_536, limits);
        var overrideField = GetRecordArrayField(record, tag: 5, maximumCount: 65_536, limits);
        var slotBindingField = GetRecordArrayField(record, tag: 6, maximumCount: 65_536, limits);
        var rootPoses = ReadPrefabRootPoses(record, rootPoseField, limits);
        var overrides = ReadPrefabOverrides(
            record,
            overrideField,
            schemas,
            limits,
            allowInternalOverrideReferences);
        var slotBindings = ReadPlacementSlotBindings(record, slotBindingField, limits);
        var encodedRecord = ImmutableArray.CreateRange(record.EncodedBytes.ToArray());
        return new ScenePrefabPlacement(
            placementId,
            prefabAssetId,
            parentEntityId,
            rootPoses,
            overrides,
            slotBindings,
            encodedRecord);
    }

    private static AssetId ReadPrefabReference(EbinRecordReadView record)
    {
        ValidateHeader(record, s_assetReferenceSchemaId, major: 1, fieldCount: 3);
        var assetId = ReadUInt32(record, tag: 1);
        var schemaId = ReadGuid(record, tag: 2, isRequired: true);
        var schemaMajor = ReadUInt32(record, tag: 3);

        if (assetId is 0 or > int.MaxValue)
        {
            throw new InvalidDataException("The Prefab AssetId is outside the admitted signed identity range.");
        }

        if (schemaId != s_prefabSchemaId)
        {
            throw new InvalidDataException("A PrefabPlacement must reference a native/prefab asset.");
        }

        if (schemaMajor != 2)
        {
            throw new NotSupportedException("The referenced Prefab schema major is unsupported.");
        }

        return new AssetId(checked((int)assetId));
    }

    private static ScenePrefabRootPose ReadPrefabRootPose(EbinRecordReadView record, EbinReadLimits limits)
    {
        var hasRootPosition = record.TryGetField(3, out var rootPositionField);
        ValidateHeader(record, s_prefabRootPoseSchemaId, major: 1, fieldCount: hasRootPosition ? 3 : 2);
        var target = ReadEntitySelector(record.GetRecord(1), limits);

        if (target.Scope is not SceneEntitySelectorScope.OccurrenceRelative)
        {
            throw new InvalidDataException("A Prefab RootPose target must be occurrence-relative.");
        }

        var localTransform = SceneEntityReader.ReadLocalTransformRecord(record.GetRecord(2));
        ImmutableArray<byte>? rootPositionPayload = null;

        if (hasRootPosition)
        {
            if ((rootPositionField.Kind != 21)
                || rootPositionField.IsRequired
                || (rootPositionField.Count != 1)
                || (rootPositionField.Payload.Length != 24))
            {
                throw new InvalidDataException("The Prefab RootPose RootPosition does not match its optional schema field.");
            }

            rootPositionPayload = [.. rootPositionField.Payload];
        }

        return new ScenePrefabRootPose(target, localTransform, rootPositionPayload);
    }

    private static ImmutableArray<ScenePrefabRootPose> ReadPrefabRootPoses(
        EbinRecordReadView record,
        EbinFieldReadView field,
        EbinReadLimits limits)
    {
        var count = checked((int)field.Count);
        var rootPoses = ImmutableArray.CreateBuilder<ScenePrefabRootPose>(count);

        for (var index = 0; index < count; index++)
        {
            rootPoses.Add(ReadPrefabRootPose(record.GetRecordArrayElement(4, index), limits));
        }

        return rootPoses.MoveToImmutable();
    }

    private static ImmutableArray<ScenePropertyPathSegment> ReadPropertyPath(
        EbinRecordReadView record,
        uint tag,
        EbinFieldReadView field)
    {
        var count = checked((int)field.Count);
        var segments = ImmutableArray.CreateBuilder<ScenePropertyPathSegment>(count);

        for (var index = 0; index < count; index++)
        {
            var segmentRecord = record.GetRecordArrayElement(tag, index);
            ValidateHeader(segmentRecord, s_propertySegmentSchemaId, major: 1, fieldCount: 2);
            var kind = ReadUInt8(segmentRecord, tag: 1);
            var value = ReadUInt32(segmentRecord, tag: 2);

            if (kind == (byte)ScenePropertyPathSegmentKind.FieldTag)
            {
                if (value == 0)
                {
                    throw new InvalidDataException("A Prefab property FieldTag segment must be nonzero.");
                }

                segments.Add(new ScenePropertyPathSegment(ScenePropertyPathSegmentKind.FieldTag, value));
            }
            else if (kind == (byte)ScenePropertyPathSegmentKind.FixedArrayIndex)
            {
                segments.Add(new ScenePropertyPathSegment(ScenePropertyPathSegmentKind.FixedArrayIndex, value));
            }
            else
            {
                throw new InvalidDataException("A Prefab property path segment kind is not defined.");
            }
        }

        return segments.MoveToImmutable();
    }

    private static SceneReferenceTarget ReadReferenceTarget(EbinRecordReadView record, EbinReadLimits limits)
    {
        var hasEntity = record.TryGetField(2, out var entityField);
        var hasExternalSlot = record.TryGetField(3, out var externalSlotField);
        ValidateHeader(record, s_referenceTargetSchemaId, major: 1, fieldCount: 2);
        var kind = ReadUInt8(record, tag: 1);

        if (kind is (byte)SceneReferenceTargetKind.Internal)
        {
            if (!hasEntity
                || hasExternalSlot
                || (entityField.Kind != 22)
                || entityField.IsRequired
                || (entityField.Count != 1))
            {
                throw new InvalidDataException("An Internal reference target requires only an EntitySelector.");
            }

            return new SceneReferenceTarget(
                SceneReferenceTargetKind.Internal,
                ReadEntitySelector(record.GetRecord(2), limits),
                externalSlotId: null);
        }

        if (kind is (byte)SceneReferenceTargetKind.ExternalSlot)
        {
            if (hasEntity
                || !hasExternalSlot
                || (externalSlotField.Kind != 11)
                || externalSlotField.IsRequired
                || (externalSlotField.Count != 1)
                || (externalSlotField.Payload.Length != 16))
            {
                throw new InvalidDataException("An ExternalSlot reference target requires only an ExternalSlotId.");
            }

            return new SceneReferenceTarget(
                SceneReferenceTargetKind.ExternalSlot,
                entity: null,
                externalSlotId: ReadGuidPayload(externalSlotField.Payload));
        }

        throw new InvalidDataException("A reference-target kind is not defined by the schema.");
    }

    private static ContentSchemaReference ReadSchemaReference(EbinRecordReadView record)
    {
        ValidateHeader(record, s_contentSchemaReferenceSchemaId, major: 1, fieldCount: 4);
        var schemaId = ReadGuid(record, tag: 1, isRequired: true);
        var major = ReadUInt32(record, tag: 2);
        var minor = ReadUInt32(record, tag: 3);
        var scope = ReadUInt8(record, tag: 4);

        if ((major is 0 or > int.MaxValue)
            || (minor > int.MaxValue)
            || (scope > (byte)ContentSchemaScope.Foundation))
        {
            throw new InvalidDataException("A content schema reference contains an invalid version or scope.");
        }

        return new ContentSchemaReference(
            schemaId,
            checked((int)major),
            checked((int)minor),
            (ContentSchemaScope)scope);
    }

    private static uint ReadUInt32(EbinRecordReadView record, uint tag)
    {
        if (!record.TryGetField(tag, out var field)
            || (field.Kind != 3)
            || !field.IsRequired
            || (field.Count != 1)
            || (field.Payload.Length != sizeof(uint)))
        {
            throw new InvalidDataException($"Scene UInt32 field {tag} does not match its schema.");
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(field.Payload);
    }

    private static byte ReadUInt8(EbinRecordReadView record, uint tag)
    {
        if (!record.TryGetField(tag, out var field)
            || (field.Kind != 1)
            || !field.IsRequired
            || (field.Count != 1)
            || (field.Payload.Length != sizeof(byte)))
        {
            throw new InvalidDataException($"Scene UInt8 field {tag} does not match its schema.");
        }

        return field.Payload[0];
    }

    private static void ValidateHeader(EbinRecordReadView record, Guid schemaId, uint major, int fieldCount)
    {
        ValidateSchema(record, schemaId, major);

        if (record.FieldCount != fieldCount)
        {
            throw new InvalidDataException("The Scene record has an unexpected field count.");
        }
    }

    private static void ValidateRootPositionComponents(
        ImmutableArray<SceneEntity>.Builder entities,
        SceneHierarchy hierarchy)
    {
        for (var entityIndex = 0; entityIndex < entities.Count; entityIndex++)
        {
            var entity = entities[entityIndex];
            var isRoot = hierarchy.Nodes[entityIndex].ParentEntityId is null;
            var hasRootPosition = false;

            foreach (var component in entity.Components)
            {
                if (component.Schema.SchemaId == s_rootPositionSchemaId)
                {
                    if ((component.Schema.Major != 1) || (component.Schema.Minor != 0))
                    {
                        throw new NotSupportedException("The selected RootPosition schema version is unsupported.");
                    }

                    hasRootPosition = true;
                }
            }

            if (isRoot != hasRootPosition)
            {
                throw new InvalidDataException(
                    isRoot
                        ? "A root Scene Entity requires RootPosition."
                        : "A child Scene Entity cannot contain RootPosition.");
            }

            if (isRoot)
            {
                var position = entity.LocalTransformPosition;

                if ((position.X != 0.0f) || (position.Y != 0.0f) || (position.Z != 0.0f))
                {
                    throw new InvalidDataException("A root Scene Entity must have zero LocalTransform.Position.");
                }
            }
        }
    }

    private static void ValidateSchema(EbinRecordReadView record, Guid schemaId, uint major)
    {
        var header = record.Header;

        if ((header.SchemaId != schemaId)
            || (header.Major != major)
            || (header.Minor != 0))
        {
            throw new InvalidDataException("A Scene record does not match the selected schema tuple.");
        }
    }
}
