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
    private static readonly Guid s_entityReferenceBindingSchemaId = new ContentSchemaIdentityAssignment("native/entity-reference-binding").SchemaId;
    private static readonly Guid s_entitySelectorSchemaId = new ContentSchemaIdentityAssignment("native/entity-selector").SchemaId;
    private static readonly Guid s_externalSlotSchemaId = new ContentSchemaIdentityAssignment("native/external-slot").SchemaId;
    private static readonly Guid s_placementSlotBindingSchemaId = new ContentSchemaIdentityAssignment("native/placement-slot-binding").SchemaId;
    private static readonly Guid s_prefabOverrideSchemaId = new ContentSchemaIdentityAssignment("native/prefab-override").SchemaId;
    private static readonly Guid s_prefabPlacementSchemaId = new ContentSchemaIdentityAssignment("native/prefab-placement").SchemaId;
    private static readonly Guid s_prefabRootPoseSchemaId = new ContentSchemaIdentityAssignment("native/prefab-root-pose").SchemaId;
    private static readonly Guid s_prefabSchemaId = new ContentSchemaIdentityAssignment("native/prefab").SchemaId;
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
            var placement = ReadPrefabPlacement(record.GetRecordArrayElement(4, placementIndex), limits);

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
        var externalSlots = ReadAuthoredRecords(
            record,
            tag: 8,
            externalSlotField,
            s_externalSlotSchemaId,
            maximumCount: 65_536);

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
            var placement = ReadPrefabPlacement(record.GetRecordArrayElement(5, placementIndex), limits);

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
        var externalSlots = ReadAuthoredRecords(
            record,
            tag: 8,
            externalSlotField,
            s_externalSlotSchemaId,
            maximumCount: 65_536);

        return new ScenePrefabAsset(
            prefabId,
            entities.MoveToImmutable(),
            hierarchy,
            placements.MoveToImmutable(),
            references,
            externalSlots,
            [.. record.EncodedBytes]);
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

    private static ScenePrefabPlacement ReadPrefabPlacement(
        EbinRecordReadView record,
        EbinReadLimits limits)
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
        var overrides = ReadAuthoredRecords(
            record,
            tag: 5,
            overrideField,
            s_prefabOverrideSchemaId,
            maximumCount: 65_536);
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
