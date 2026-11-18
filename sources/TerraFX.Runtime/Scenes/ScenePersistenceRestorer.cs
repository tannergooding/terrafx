// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;

namespace TerraFX.Scenes;

internal static class ScenePersistenceRestorer
{
    private const ushort ArrayKind = 24;
    private const ushort FixedArrayKind = 23;
    private const ushort GuidKind = 11;
    private const ushort Int64Kind = 8;
    private static readonly Guid s_externalBindingSchemaId = new ContentSchemaIdentityAssignment("state/external-scene-binding").SchemaId;
    private static readonly Guid s_hierarchyNodeSchemaId = new ContentSchemaIdentityAssignment("scene/world-hierarchy-node").SchemaId;
    private static readonly Guid s_hierarchySchemaId = new ContentSchemaIdentityAssignment("scene/world-hierarchy").SchemaId;
    private static readonly Guid s_nativeAssetReferenceSchemaId = new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId;
    private static readonly Guid s_prefabSchemaId = new ContentSchemaIdentityAssignment("native/prefab").SchemaId;
    private static readonly Guid s_sceneSchemaId = new ContentSchemaIdentityAssignment("native/scene").SchemaId;
    private static readonly Guid s_sceneEntityBindingSchemaId = new ContentSchemaIdentityAssignment("state/scene-entity-binding").SchemaId;
    private static readonly Guid s_sceneInstanceSchemaId = new ContentSchemaIdentityAssignment("state/scene-instance").SchemaId;
    private static readonly Guid s_scenesSchemaId = new ContentSchemaIdentityAssignment("state/scenes").SchemaId;

    internal static long MeasureHierarchyRestoreWorkspaceBytes(int nodeCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nodeCount);
        return (nodeCount == 0)
            ? 0
            : checked(128L + (2 * ArrayBytes(nodeCount, Unsafe.SizeOf<Entity>())));
    }

    internal static async Task<long?> MeasureRestoredScenePlanStorageBytesAsync(
        World world,
        AssetPackageLease packageLease,
        ReadOnlyMemory<byte> scenesRecord,
        WorldPersistenceScenesContents contents,
        ISceneExpansionResourcePreparer? resourcePreparer,
        long maximumExpandedBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(packageLease);
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumExpandedBytes);
        cancellationToken.ThrowIfCancellationRequested();

        var scenes = new EbinRecordReadView(scenesRecord.Span);

        if ((scenes.Header.SchemaId != s_scenesSchemaId)
            || (scenes.Header.Major != 1)
            || (scenes.Header.Minor != 0)
            || (scenes.FieldCount != 2)
            || (ReadArrayCount(scenes, 1) != contents.InstanceCount))
        {
            throw new ArgumentException("The candidate state/scenes record does not match its validated shape.", nameof(scenesRecord));
        }

        if ((resourcePreparer is not null) && (contents.InstanceCount != 0))
        {
            return null;
        }

        var defaultLimits = SceneExpansionLimits.Default;
        var requiredBytes = 0L;

        for (var instanceIndex = 0; instanceIndex < contents.InstanceCount; instanceIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var instanceRecord = GetRecordArrayElementMemory(scenesRecord, 1, instanceIndex);
            var instance = new EbinRecordReadView(instanceRecord.Span);

            if ((instance.Header.SchemaId != s_sceneInstanceSchemaId)
                || (instance.Header.Major != 1)
                || (instance.Header.Minor != 0)
                || (instance.FieldCount != 7))
            {
                throw new ArgumentException("A candidate SceneInstance record does not match its validated shape.", nameof(scenesRecord));
            }

            var sceneId = ReadSceneAssetId(instance);
            var limits = new SceneExpansionLimits(
                defaultLimits.MaximumExpandedEntities,
                defaultLimits.MaximumPrefabEntities,
                defaultLimits.MaximumExpandedOccurrences,
                defaultLimits.MaximumPrefabDepth,
                defaultLimits.MaximumHierarchyDepth,
                maximumExpandedBytes,
                rootExternalBindingCount: ReadArrayCount(instance, 5));

            if (resourcePreparer is null)
            {
                requiredBytes = checked(
                    requiredBytes
                    + await ScenePrefabExpansionPlanner.MeasureRequiredByteReservationAsync(
                        packageLease,
                        sceneId,
                        limits,
                        cancellationToken).ConfigureAwait(false));
            }
            else
            {
                var planRequiredBytes = await ScenePrefabExpansionPlanner.MeasureRequiredByteReservationAsync(
                    packageLease,
                    sceneId,
                    limits,
                    cancellationToken).ConfigureAwait(false);

                WorldPersistenceAdmissionReservation? planWorkspaceReservation = null;

                try
                {
                    if (!world.TryReservePersistenceAdmissionBytes(planRequiredBytes, out planWorkspaceReservation)
                        || (planWorkspaceReservation is null))
                    {
                        return null;
                    }

                    using var plan = await ScenePrefabExpansionPlanner.PrepareAsync(
                        packageLease,
                        sceneId,
                        limits,
                        cancellationToken).ConfigureAwait(false);

                    if (plan.RequiredByteReservation != planRequiredBytes)
                    {
                        throw new InvalidOperationException("Scene expansion measurement changed between preflight and preparation.");
                    }

                    var resourceStorageBytes = await resourcePreparer.MeasureStorageByteReservationAsync(
                        plan,
                        cancellationToken).ConfigureAwait(false);
                    requiredBytes = checked(requiredBytes + planRequiredBytes + resourceStorageBytes);
                }
                finally
                {
                    planWorkspaceReservation?.Dispose();
                }
            }
        }

        return requiredBytes;
    }

    internal static async Task<PreparedRestore> PrepareAsync(
        World world,
        AssetStore assets,
        AssetPackageLease packageLease,
        ReadOnlyMemory<byte> scenesRecord,
        WorldPersistenceScenesContents contents,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(packageLease);
        ArgumentNullException.ThrowIfNull(contents);
        cancellationToken.ThrowIfCancellationRequested();

        var scenes = new EbinRecordReadView(scenesRecord.Span);

        if ((scenes.Header.SchemaId != s_scenesSchemaId)
            || (scenes.Header.Major != 1)
            || (scenes.Header.Minor != 0)
            || (scenes.FieldCount != 2)
            || (ReadArrayCount(scenes, 1) != contents.InstanceCount))
        {
            throw new ArgumentException("The candidate state/scenes record does not match its validated shape.", nameof(scenesRecord));
        }

        var hierarchyRecord = GetRecordMemory(scenesRecord, 2);
        var hierarchyHeader = new EbinRecordReadView(hierarchyRecord.Span);

        if ((hierarchyHeader.Header.SchemaId != s_hierarchySchemaId)
            || (hierarchyHeader.Header.Major != 1)
            || (hierarchyHeader.Header.Minor != 0)
            || (hierarchyHeader.FieldCount != 2)
            || (ReadArrayCount(hierarchyHeader, 1) != contents.HierarchyNodeCount))
        {
            throw new ArgumentException("The candidate Scene hierarchy does not match its validated shape.", nameof(scenesRecord));
        }

        var sceneSources = new SceneRestoreInstance[contents.InstanceCount];
        var sceneSupport = world.Registration.SceneSupport;
        var registry = sceneSupport?.GetParticipant(world) as SceneWorldRegistry;

        if ((contents.InstanceCount != 0 || contents.HierarchyNodeCount != 0) && (registry is null))
        {
            throw new InvalidOperationException("The candidate World lacks the Scene support required by its saved Scene state.");
        }

        for (var instanceIndex = 0; instanceIndex < sceneSources.Length; instanceIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var instanceRecord = GetRecordArrayElementMemory(scenesRecord, 1, instanceIndex);
            var instance = new EbinRecordReadView(instanceRecord.Span);

            if ((instance.Header.SchemaId != s_sceneInstanceSchemaId)
                || (instance.Header.Major != 1)
                || (instance.Header.Minor != 0)
                || (instance.FieldCount != 7))
            {
                throw new ArgumentException("A candidate SceneInstance record does not match its validated shape.", nameof(scenesRecord));
            }

            var sceneInstanceId = ReadGuid(instance, 1);
            var assignmentOrdinalField = GetRequiredField(instance, 6);

            if ((assignmentOrdinalField.Kind != Int64Kind)
                || !assignmentOrdinalField.IsRequired
                || (assignmentOrdinalField.Count != 1)
                || (assignmentOrdinalField.Payload.Length != sizeof(long)))
            {
                throw new ArgumentException("A candidate SceneInstance assignment ordinal has an invalid wire shape.", nameof(scenesRecord));
            }

            var assignmentOrdinal = BinaryPrimitives.ReadInt64LittleEndian(assignmentOrdinalField.Payload);

            if ((assignmentOrdinal < 1)
                || !world.IdentityLedger.TryGetLiveLocator(sceneInstanceId, IdentityKind.SceneInstance, out var sceneLocator))
            {
                throw new ArgumentException("A candidate SceneInstance does not match a live identity assignment.", nameof(scenesRecord));
            }

            var liveSceneOrdinal = world.IdentityLedger.GetLiveOrdinal(IdentityKind.SceneInstance, sceneLocator);

            if (liveSceneOrdinal != (ulong)assignmentOrdinal)
            {
                throw new ArgumentException("A candidate SceneInstance ordinal disagrees with its identity ledger.", nameof(scenesRecord));
            }

            var externalBindings = ReadExternalBindings(world, instance);
            var options = new SceneLoadOptions {
                ExternalBindings = externalBindings,
            }.ValidateAndFreeze();
            sceneSources[instanceIndex] = new SceneRestoreInstance(
                sceneInstanceId,
                liveSceneOrdinal,
                instanceRecord,
                ReadSceneAssetId(instance),
                options);
        }

        var plans = new SceneExpansionPlan?[sceneSources.Length];

        if (registry is not null)
        {
            var loader = new SceneLoader(world, assets);

            try
            {
                for (var instanceIndex = 0; instanceIndex < sceneSources.Length; instanceIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var source = sceneSources[instanceIndex];
                    var plan = await loader.PrepareRestorePlanAsync(
                        packageLease,
                        source.SceneAssetId,
                        source.Options,
                        cancellationToken).ConfigureAwait(false);
                    var planTransferred = false;

                    try
                    {
                        ValidateSceneSourceExpansion(source.Record, plan, cancellationToken);
                        plans[instanceIndex] = plan;
                        planTransferred = true;
                    }
                    finally
                    {
                        if (!planTransferred)
                        {
                            plan.Dispose();
                        }
                    }
                }

                return new PreparedRestore(world, registry, sceneSources, plans, hierarchyRecord, contents.HierarchyNodeCount);
            }
            catch
            {
                DisposePlans(plans);
                throw;
            }
        }

        return new PreparedRestore(world, registry, sceneSources, plans, hierarchyRecord, contents.HierarchyNodeCount);
    }

    internal static void RestorePrepared(
        World world,
        PreparedRestore prepared,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(prepared);
        cancellationToken.ThrowIfCancellationRequested();
        prepared.EnsureBoundTo(world);

        for (var instanceIndex = 0; instanceIndex < prepared.InstanceCount; instanceIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = prepared.GetInstance(instanceIndex);
            var plan = prepared.TakePlan(instanceIndex);
            SceneInstance? instance = null;
            SceneInstanceReservation? reservation = null;
            var planTransferred = false;

            try
            {
                var firstEntityIdentityOrdinal = (plan.Entities.Length == 0)
                    ? 0
                    : checked(source.SceneIdentityOrdinal + 1);
                ValidateTargetEntityAssignments(
                    world,
                    source.Record,
                    plan,
                    firstEntityIdentityOrdinal,
                    cancellationToken);

                var registry = prepared.Registry
                    ?? throw new InvalidOperationException("The candidate Scene restoration omitted its Scene registry.");
                var bindingDiagnostics = SceneActivationExecutor.ValidateRestoredRootBindings(
                    world,
                    plan,
                    source.Options);

                if (!bindingDiagnostics.IsEmpty)
                {
                    throw new InvalidDataException(bindingDiagnostics[0].Message);
                }

                if (!registry.TryReserveInstance(plan.Occurrences.Length, out reservation)
                    || (reservation is null))
                {
                    throw new InvalidOperationException("The candidate Scene registry cannot admit a saved SceneInstance.");
                }

                var captureRows = SceneInstance.CaptureExternalBindings(world, source.Options.ExternalBindings);
                instance = new SceneInstance(world, registry, plan, captureRows);
                planTransferred = true;
                instance.Activate(source.SceneInstanceId, firstEntityIdentityOrdinal);
                reservation.Commit(instance);
                instance = null;
            }
            finally
            {
                reservation?.Dispose();
                instance?.RevokeFromWorld();

                if (!planTransferred)
                {
                    plan.Dispose();
                }
            }
        }

        var hierarchy = new EbinRecordReadView(prepared.HierarchyRecord.Span);
        RestoreHierarchy(world, prepared.Registry, hierarchy, prepared.HierarchyNodeCount, cancellationToken);
    }

    private static long ArrayBytes(int count, int elementSize)
    {
        if (count == 0)
        {
            return 0;
        }

        var payloadBytes = checked((long)count * elementSize);
        return checked(64L + ((payloadBytes + 15L) & ~15L));
    }

    private static void DisposePlans(Span<SceneExpansionPlan?> plans)
    {
        for (var index = 0; index < plans.Length; index++)
        {
            plans[index]?.Dispose();
            plans[index] = null;
        }
    }

    private static int FindExpandedEntityIndex(SceneExpansionPlan plan, Guid expandedEntityKey)
    {
        var keys = plan.EntityAssignmentKeys;
        var indices = plan.EntityIndicesByAssignmentOrdinal;
        var low = 0;
        var high = keys.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = FoundationCatalogue.CompareRfc(keys[middle], expandedEntityKey);

            if (comparison == 0)
            {
                return indices[middle];
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        throw new ArgumentException("A saved Scene Entity binding is absent from the expanded package source.");
    }

    private static ReadOnlyMemory<byte> GetRecordArrayElementMemory(
        ReadOnlyMemory<byte> recordMemory,
        uint tag,
        int index)
    {
        var record = new EbinRecordReadView(recordMemory.Span);
        var encodedRecord = record.GetRecordArrayElement(tag, index).EncodedBytes;

        if (!recordMemory.Span.Overlaps(encodedRecord, out var offset)
            || (offset < 0)
            || (offset > recordMemory.Length)
            || (encodedRecord.Length > (recordMemory.Length - offset)))
        {
            throw new ArgumentException("A candidate Scene child record is not backed by its enclosing state/scenes record.", nameof(recordMemory));
        }

        return recordMemory.Slice(offset, encodedRecord.Length);
    }

    private static ReadOnlyMemory<byte> GetRecordMemory(ReadOnlyMemory<byte> recordMemory, uint tag)
    {
        var record = new EbinRecordReadView(recordMemory.Span);
        var encodedRecord = record.GetRecord(tag).EncodedBytes;

        if (!recordMemory.Span.Overlaps(encodedRecord, out var offset)
            || (offset < 0)
            || (offset > recordMemory.Length)
            || (encodedRecord.Length > (recordMemory.Length - offset)))
        {
            throw new ArgumentException("A candidate Scene child record is not backed by its enclosing state/scenes record.", nameof(recordMemory));
        }

        return recordMemory.Slice(offset, encodedRecord.Length);
    }

    private static EbinFieldReadView GetRequiredField(EbinRecordReadView record, uint tag)
    {
        if (!record.TryGetField(tag, out var field))
        {
            throw new ArgumentException("A candidate Scene record omits a required field.");
        }

        return field;
    }

    private static int ReadArrayCount(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag);

        if ((field.Kind != ArrayKind)
            || !field.IsRequired
            || (field.Count > int.MaxValue))
        {
            throw new ArgumentException("A candidate Scene array has an invalid wire shape.");
        }

        return checked((int)field.Count);
    }

    private static ImmutableArray<SceneExternalBinding> ReadExternalBindings(
        World world,
        EbinRecordReadView instanceRecord)
    {
        var externalBindingCount = ReadArrayCount(instanceRecord, 5);
        var bindings = ImmutableArray.CreateBuilder<SceneExternalBinding>(externalBindingCount);

        for (var bindingIndex = 0; bindingIndex < externalBindingCount; bindingIndex++)
        {
            var binding = instanceRecord.GetRecordArrayElement(5, bindingIndex);

            if ((binding.Header.SchemaId != s_externalBindingSchemaId)
                || (binding.Header.Major != 1)
                || (binding.Header.Minor != 0)
                || (binding.FieldCount is not 1 and not 2))
            {
                throw new ArgumentException("A candidate Scene external binding does not match its validated shape.");
            }

            var slotId = ReadGuid(binding, 1);
            Entity? target = null;

            if (binding.TryGetField(2, out _))
            {
                target = world.ResolveRestoreEntity(ReadGuid(binding, 2, isRequired: false));
            }

            bindings.Add(new SceneExternalBinding(slotId, target));
        }

        return bindings.MoveToImmutable();
    }

    private static int ReadFixedArrayCount(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag);

        if ((field.Kind != FixedArrayKind)
            || !field.IsRequired
            || (field.Count > int.MaxValue)
            || (field.Payload.Length != checked(32 + (16 * (int)field.Count)))
            || (BinaryPrimitives.ReadUInt16LittleEndian(field.Payload[16..]) != GuidKind)
            || (BinaryPrimitives.ReadUInt32LittleEndian(field.Payload[20..]) != 16))
        {
            throw new ArgumentException("A candidate Scene fixed GUID array has an invalid wire shape.");
        }

        return checked((int)field.Count);
    }

    private static Guid ReadGuid(EbinRecordReadView record, uint tag, bool isRequired = true)
    {
        var field = GetRequiredField(record, tag);

        if ((field.Kind != GuidKind)
            || (field.IsRequired != isRequired)
            || (field.Count != 1)
            || (field.Payload.Length != 16))
        {
            throw new ArgumentException("A candidate Scene GUID field has an invalid wire shape.");
        }

        var value = new Guid(field.Payload, bigEndian: true);

        if (value == Guid.Empty)
        {
            throw new ArgumentException("A candidate Scene identity cannot be empty.");
        }

        return value;
    }

    private static Guid ReadGuidArrayElement(EbinFieldReadView field, int index)
    {
        var offset = checked(32 + (index * 16));
        return new Guid(field.Payload.Slice(offset, 16), bigEndian: true);
    }

    private static AssetId ReadSceneAssetId(EbinRecordReadView instance)
    {
        var reference = new EbinRecordReadView(instance.GetRecord(2).EncodedBytes);

        if ((reference.Header.SchemaId != s_nativeAssetReferenceSchemaId)
            || (reference.Header.Major != 1)
            || (reference.Header.Minor != 0)
            || (reference.FieldCount != 3))
        {
            throw new ArgumentException("A candidate Scene source has an invalid native asset reference.");
        }

        var assetId = ReadUInt32(reference, 1);
        var schemaId = ReadGuid(reference, 2);
        var schemaMajor = ReadUInt32(reference, 3);

        if ((assetId == 0) || (assetId > int.MaxValue) || (schemaId != s_sceneSchemaId) || (schemaMajor != 2))
        {
            throw new ArgumentException("A candidate Scene source does not identify a version-2 Scene asset.");
        }

        return new AssetId(checked((int)assetId));
    }

    private static uint ReadUInt32(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag);

        if ((field.Kind != 3)
            || !field.IsRequired
            || (field.Count != 1)
            || (field.Payload.Length != sizeof(uint)))
        {
            throw new ArgumentException("A candidate Scene asset reference has an invalid UInt32 field.");
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(field.Payload);
    }

    private static void RestoreHierarchy(
        World world,
        SceneWorldRegistry? registry,
        EbinRecordReadView hierarchy,
        int nodeCount,
        CancellationToken cancellationToken)
    {
        if (nodeCount == 0)
        {
            world.RestoreHierarchyLinks([], []);
            return;
        }

        var participant = registry
            ?? throw new InvalidOperationException("The candidate World lacks the Scene registry required by its hierarchy.");
        var workspaceBytes = MeasureHierarchyRestoreWorkspaceBytes(nodeCount);
        using var reservation = world.TryReserveRestoredSceneStorage(participant, workspaceBytes)
            ?? throw new InvalidOperationException("The candidate World cannot admit its hierarchy restoration workspace.");
        var entities = new Entity[nodeCount];
        var parents = new Entity[nodeCount];

        for (var nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = hierarchy.GetRecordArrayElement(1, nodeIndex);

            if ((node.Header.SchemaId != s_hierarchyNodeSchemaId)
                || (node.Header.Major != 1)
                || (node.Header.Minor != 0)
                || (node.FieldCount is not 2 and not 3))
            {
                throw new ArgumentException("A candidate hierarchy node does not match its validated shape.");
            }

            entities[nodeIndex] = world.ResolveRestoreEntity(ReadGuid(node, 1));

            if (node.TryGetField(2, out _))
            {
                parents[nodeIndex] = world.ResolveRestoreEntity(ReadGuid(node, 2, isRequired: false));
            }
        }

        world.RestoreHierarchyLinks(entities, parents);
    }

    private static void ValidateAssetReference(
        EbinRecordReadView record,
        uint assetId,
        Guid schemaId,
        uint schemaMajor)
    {
        if ((record.Header.SchemaId != s_nativeAssetReferenceSchemaId)
            || (record.Header.Major != 1)
            || (record.Header.Minor != 0)
            || (record.FieldCount != 3)
            || (ReadUInt32(record, 1) != assetId)
            || (ReadGuid(record, 2) != schemaId)
            || (ReadUInt32(record, 3) != schemaMajor))
        {
            throw new ArgumentException("A saved Scene source reference does not match the expanded package source.");
        }
    }

    private static void ValidateSceneSourceExpansion(
        ReadOnlyMemory<byte> instanceRecordBytes,
        SceneExpansionPlan plan,
        CancellationToken cancellationToken)
    {
        var instanceRecord = new EbinRecordReadView(instanceRecordBytes.Span);

        if (plan.SceneAssetId != ReadSceneAssetId(instanceRecord))
        {
            throw new ArgumentException("A saved SceneInstance root asset does not match its expanded package source.");
        }

        var occurrenceCount = ReadArrayCount(instanceRecord, 7);

        if (occurrenceCount != plan.Occurrences.Length)
        {
            throw new ArgumentException("The saved Scene occurrence count does not match its expanded package source.");
        }

        Span<Guid> placementPath = stackalloc Guid[32];

        for (var occurrenceIndex = 0; occurrenceIndex < occurrenceCount; occurrenceIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = instanceRecord.GetRecordArrayElement(7, occurrenceIndex);
            var occurrence = plan.Occurrences[occurrenceIndex];

            if ((record.Header.SchemaId != new ContentSchemaIdentityAssignment("state/scene-occurrence").SchemaId)
                || (record.Header.Major != 1)
                || (record.Header.Minor != 0)
                || (record.FieldCount is not 3 and not 5)
                || (ReadGuid(record, 2) != occurrence.OccurrenceId))
            {
                throw new ArgumentException("A saved Scene occurrence does not match its expanded package source.");
            }

            var expectedPathCount = plan.CopyOccurrencePlacementPath(occurrenceIndex, placementPath);
            var pathField = GetRequiredField(record, 1);

            if (ReadFixedArrayCount(record, 1) != expectedPathCount)
            {
                throw new ArgumentException("A saved Scene occurrence path does not match its expanded package source.");
            }

            for (var pathIndex = 0; pathIndex < expectedPathCount; pathIndex++)
            {
                if (ReadGuidArrayElement(pathField, pathIndex) != placementPath[pathIndex])
                {
                    throw new ArgumentException("A saved Scene occurrence path does not match its expanded package source.");
                }
            }

            var source = plan.GetSource(occurrenceIndex);
            ValidateAssetReference(
                new EbinRecordReadView(record.GetRecord(3).EncodedBytes),
                checked((uint)source.AssetId.Value),
                source.IsPrefab ? s_prefabSchemaId : s_sceneSchemaId,
                schemaMajor: 2);

            var hasParent = occurrence.ParentOccurrenceIndex >= 0;

            if ((hasParent != record.TryGetField(4, out _))
                || (hasParent != record.TryGetField(5, out _))
                || (record.FieldCount != (hasParent ? 5 : 3)))
            {
                throw new ArgumentException("A saved Scene occurrence parent does not match its expanded package source.");
            }

            if (hasParent
                && ((ReadGuid(record, 4, isRequired: false)
                        != plan.Occurrences[occurrence.ParentOccurrenceIndex].OccurrenceId)
                    || (ReadGuid(record, 5, isRequired: false) != occurrence.PlacementId)))
            {
                throw new ArgumentException("A saved Scene occurrence parent does not match its expanded package source.");
            }
        }

        var entityIdsField = GetRequiredField(instanceRecord, 3);
        var entityCount = ReadFixedArrayCount(instanceRecord, 3);
        var bindingCount = ReadArrayCount(instanceRecord, 4);

        if ((entityCount != bindingCount)
            || (entityCount > plan.Entities.Length))
        {
            throw new ArgumentException("The saved Scene Entity binding count does not match its expanded package source.");
        }

        var previousExpandedEntityKey = Guid.Empty;

        for (var bindingIndex = 0; bindingIndex < bindingCount; bindingIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var binding = instanceRecord.GetRecordArrayElement(4, bindingIndex);

            if ((binding.Header.SchemaId != s_sceneEntityBindingSchemaId)
                || (binding.Header.Major != 1)
                || (binding.Header.Minor != 0)
                || (binding.FieldCount != 6))
            {
                throw new ArgumentException("A saved Scene Entity binding does not match its validated shape.");
            }

            var expandedEntityKey = ReadGuid(binding, 6);

            if ((bindingIndex > 0)
                && (FoundationCatalogue.CompareRfc(previousExpandedEntityKey, expandedEntityKey) >= 0))
            {
                throw new ArgumentException("The saved Scene Entity bindings are not in canonical expanded-key order.");
            }

            var expandedEntityIndex = FindExpandedEntityIndex(plan, expandedEntityKey);
            var expandedEntity = plan.Entities[expandedEntityIndex];
            var occurrence = plan.Occurrences[expandedEntity.OccurrenceIndex];
            var entityId = ReadGuid(binding, 4);

            if ((ReadGuid(binding, 3) != expandedEntity.AuthoredEntityId)
                || (ReadGuid(binding, 5) != occurrence.OccurrenceId)
                || (ReadGuidArrayElement(entityIdsField, bindingIndex) != entityId))
            {
                throw new ArgumentException("A saved Scene Entity binding does not match its expanded package source.");
            }

            var expectedPathCount = plan.CopyOccurrencePlacementPath(expandedEntity.OccurrenceIndex, placementPath);
            var pathField = GetRequiredField(binding, 1);

            if (ReadFixedArrayCount(binding, 1) != expectedPathCount)
            {
                throw new ArgumentException("A saved Scene Entity binding path does not match its expanded package source.");
            }

            for (var pathIndex = 0; pathIndex < expectedPathCount; pathIndex++)
            {
                if (ReadGuidArrayElement(pathField, pathIndex) != placementPath[pathIndex])
                {
                    throw new ArgumentException("A saved Scene Entity binding path does not match its expanded package source.");
                }
            }

            var source = plan.GetSource(expandedEntity.OccurrenceIndex);
            ValidateAssetReference(
                new EbinRecordReadView(binding.GetRecord(2).EncodedBytes),
                checked((uint)source.AssetId.Value),
                source.IsPrefab ? s_prefabSchemaId : s_sceneSchemaId,
                schemaMajor: 2);

            previousExpandedEntityKey = expandedEntityKey;
        }
    }

    private static void ValidateTargetEntityAssignments(
        World world,
        ReadOnlyMemory<byte> instanceRecordBytes,
        SceneExpansionPlan plan,
        uint firstEntityIdentityOrdinal,
        CancellationToken cancellationToken)
    {
        var instanceRecord = new EbinRecordReadView(instanceRecordBytes.Span);
        var bindingCount = ReadArrayCount(instanceRecord, 4);

        for (var bindingIndex = 0; bindingIndex < bindingCount; bindingIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var binding = instanceRecord.GetRecordArrayElement(4, bindingIndex);
            var entityId = ReadGuid(binding, 4);
            var expandedEntityIndex = FindExpandedEntityIndex(plan, ReadGuid(binding, 6));

            if (!world.IdentityLedger.TryGetLiveLocator(entityId, IdentityKind.Entity, out var entityLocator))
            {
                throw new ArgumentException("A saved Scene Entity binding does not name a live candidate Entity.");
            }

            var expectedOrdinal = checked(
                (ulong)firstEntityIdentityOrdinal
                + (uint)plan.GetEntityAssignmentOrdinal(expandedEntityIndex));
            var actualOrdinal = world.IdentityLedger.GetLiveOrdinal(IdentityKind.Entity, entityLocator);

            if (actualOrdinal != expectedOrdinal)
            {
                throw new ArgumentException("A saved Scene Entity binding does not match its exact expanded assignment ordinal.");
            }
        }

        if (plan.Entities.Length != 0)
        {
            var lastEntityOrdinal = checked(
                (ulong)firstEntityIdentityOrdinal
                + (uint)plan.Entities.Length
                - 1);

            if (lastEntityOrdinal > checked((ulong)world.IdentityLedger.AssignmentCount))
            {
                throw new ArgumentException("The expanded Scene Entity assignment range exceeds the restored identity history.");
            }

            for (var assignmentIndex = 0; assignmentIndex < plan.Entities.Length; assignmentIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var assignmentOrdinal = checked(firstEntityIdentityOrdinal + (uint)assignmentIndex);

                if (world.IdentityLedger.GetKindByOrdinal(assignmentOrdinal) != IdentityKind.Entity)
                {
                    throw new ArgumentException("The expanded Scene Entity assignment range contains a non-Entity identity.");
                }
            }
        }
    }

    internal sealed class PreparedRestore : IDisposable
    {
        private readonly SceneRestoreInstance[] _instances;
        private bool _isDisposed;
        private readonly SceneExpansionPlan?[] _plans;
        private readonly SceneWorldRegistry? _registry;
        private readonly World _world;

        internal PreparedRestore(
            World world,
            SceneWorldRegistry? registry,
            SceneRestoreInstance[] instances,
            SceneExpansionPlan?[] plans,
            ReadOnlyMemory<byte> hierarchyRecord,
            int hierarchyNodeCount)
        {
            _world = world;
            _registry = registry;
            _instances = instances;
            _plans = plans;
            HierarchyRecord = hierarchyRecord;
            HierarchyNodeCount = hierarchyNodeCount;
        }

        internal int HierarchyNodeCount { get; }

        internal ReadOnlyMemory<byte> HierarchyRecord { get; }

        internal int InstanceCount => _instances.Length;

        internal SceneWorldRegistry? Registry => _registry;

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            DisposePlans(_plans);
        }

        internal void EnsureBoundTo(World world)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (!ReferenceEquals(world, _world))
            {
                throw new ArgumentException("The prepared Scene restore belongs to another exact candidate World.", nameof(world));
            }
        }

        internal SceneRestoreInstance GetInstance(int index)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _instances.Length);
            return _instances[index];
        }

        internal SceneExpansionPlan TakePlan(int index)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _plans.Length);
            var plan = _plans[index]
                ?? throw new InvalidOperationException("The prepared Scene restore already transferred this expansion plan.");
            _plans[index] = null;
            return plan;
        }
    }

    internal readonly record struct SceneRestoreInstance(
        Guid SceneInstanceId,
        uint SceneIdentityOrdinal,
        ReadOnlyMemory<byte> Record,
        AssetId SceneAssetId,
        SceneLoadOptionsSnapshot Options);
}
