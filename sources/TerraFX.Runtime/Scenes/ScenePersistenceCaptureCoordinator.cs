// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Scenes;

internal static class ScenePersistenceCaptureCoordinator
{
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("state/scenes").SchemaId;

    internal static EbinWriteStatus Capture(
        World world,
        WorldPersistenceCaptureLease lease,
        out byte[]? record,
        out WorldPersistenceAdmissionReservation? reservation,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(world);
        lease.ValidateCurrent();

        if (!lease.IsBoundTo(world))
        {
            throw new ArgumentException("The capture lease must belong to this exact World.", nameof(lease));
        }

        record = null;
        reservation = null;
        var limits = WorldPersistenceEbinLimits.Create(world.Options);
        SceneWorldRegistry? registry = null;

        if (world.Registration.SceneSupport is { } sceneSupport)
        {
            registry = world.GetSceneParticipant(sceneSupport) as SceneWorldRegistry
                ?? throw new InvalidOperationException("The World Scene registration did not create its matching Scene registry.");
        }

        var instances = new SceneInstance[registry?.GetPersistenceInstanceCount(lease) ?? 0];
        registry?.CopyPersistenceInstances(lease, instances);
        var instanceBuffers = new InstanceCaptureBuffers[instances.Length];
        long instanceRecordStorageBytes = 0;
        var instanceMaximumCandidateBytes = 0;

        for (var index = 0; index < instances.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var instanceStatus = BuildInstanceBuffers(
                instances[index],
                lease,
                limits,
                out var buffers,
                out diagnostic,
                cancellationToken);

            if (instanceStatus is not EbinWriteStatus.Complete)
            {
                return instanceStatus;
            }

            instanceBuffers[index] = buffers;
            instanceRecordStorageBytes = checked(instanceRecordStorageBytes + buffers.Workspace.RecordBytes);
            instanceMaximumCandidateBytes = Math.Max(instanceMaximumCandidateBytes, buffers.Workspace.RecordBytes);
        }

        if (instanceRecordStorageBytes > int.MaxValue)
        {
            diagnostic = CreateDiagnostic(
                "record-byte-budget",
                checked((ulong)instanceRecordStorageBytes),
                int.MaxValue);
            return EbinWriteStatus.InvalidRecord;
        }

        var instanceRecordSlices = new EbinPayloadSlice[instances.Length];
        var instanceRecordStorage = new byte[checked((int)instanceRecordStorageBytes)];
        var instanceCandidateStorage = new byte[instanceMaximumCandidateBytes];
        var statusForRecords = WriteSceneInstanceRecords(
            instances,
            instanceBuffers,
            lease,
            limits,
            instanceRecordSlices,
            instanceRecordStorage,
            instanceCandidateStorage,
            out diagnostic,
            cancellationToken);

        if (statusForRecords is not EbinWriteStatus.Complete)
        {
            return statusForRecords;
        }

        var instanceOrderScratch = new int[instances.Length];
        var orderedInstanceRecords = new EbinPayloadSlice[instances.Length];
        var status = ScenePersistenceSceneInstanceArrayWriter.Measure(
            instances,
            instanceRecordSlices,
            instanceRecordStorage,
            limits,
            instanceOrderScratch,
            orderedInstanceRecords,
            out var instanceArrayWorkspace,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var instancesArrayPayload = new byte[instanceArrayWorkspace.ArrayPayloadBytes];
        status = ScenePersistenceSceneInstanceArrayWriter.Write(
            instances,
            instanceRecordSlices,
            instanceRecordStorage,
            limits,
            instanceArrayWorkspace,
            instanceOrderScratch,
            orderedInstanceRecords,
            instancesArrayPayload,
            out var instanceArrayBytesWritten,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (instanceArrayBytesWritten != instancesArrayPayload.Length)
        {
            throw new InvalidOperationException("The Scene instance array extent changed after measurement.");
        }

        status = BuildWorldHierarchyBuffers(
            world,
            lease,
            limits,
            out var hierarchyBuffers,
            out diagnostic,
            cancellationToken);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var scenesRootPayloadStorage = new byte[checked(
            instancesArrayPayload.Length
            + hierarchyBuffers.Record.Length)];
        status = ScenePersistenceScenesWriter.Measure(
            instances.Length,
            instancesArrayPayload,
            hierarchyBuffers.Record,
            limits,
            scenesRootPayloadStorage,
            out var scenesWorkspace,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var scenesCandidateStorage = new byte[scenesWorkspace.RecordBytes];
        var scenesDestination = new byte[scenesWorkspace.RecordBytes];
        status = ScenePersistenceScenesWriter.Write(
            scenesWorkspace,
            instances.Length,
            instancesArrayPayload,
            hierarchyBuffers.Record,
            limits,
            scenesRootPayloadStorage,
            scenesCandidateStorage,
            scenesDestination,
            out var scenesBytesWritten,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (scenesBytesWritten != scenesDestination.Length)
        {
            throw new InvalidOperationException("The Scene record extent changed after measurement.");
        }

        var workspaceBytes = GetWorkspaceBytes(
            instanceBuffers,
            instanceArrayWorkspace,
            instanceCandidateStorage,
            hierarchyBuffers,
            scenesWorkspace,
            instancesArrayPayload.Length);
        var outputWorkspaceBytes = (long)scenesWorkspace.RecordBytes;
        var scratchWorkspaceBytes = checked(workspaceBytes - outputWorkspaceBytes);

        if (!lease.TryReserveAdmissionBytes(scratchWorkspaceBytes, out var scratchReservation))
        {
            throw new InvalidOperationException("The Scene capture workspace was refused by admission.");
        }

        var scratchReservationScope = scratchReservation
            ?? throw new InvalidOperationException("The Scene scratch reservation was not created.");

        if (!lease.TryReserveAdmissionBytes(outputWorkspaceBytes, out var outputReservation))
        {
            scratchReservationScope.Dispose();
            throw new InvalidOperationException("The Scene record reservation was refused by admission.");
        }

        var outputReservationScope = outputReservation
            ?? throw new InvalidOperationException("The Scene record reservation was not created.");

        try
        {
            using (scratchReservationScope.AcquireUse(world, scratchWorkspaceBytes))
            using (outputReservationScope.AcquireUse(world, outputWorkspaceBytes))
            {
                registry?.CopyPersistenceInstances(lease, instances);
                status = WriteSceneInstanceRecords(
                    instances,
                    instanceBuffers,
                    lease,
                    limits,
                    instanceRecordSlices,
                    instanceRecordStorage,
                    instanceCandidateStorage,
                    out diagnostic,
                    cancellationToken);

                if (status is not EbinWriteStatus.Complete)
                {
                    return status;
                }

                status = ScenePersistenceSceneInstanceArrayWriter.Write(
                    instances,
                    instanceRecordSlices,
                    instanceRecordStorage,
                    limits,
                    instanceArrayWorkspace,
                    instanceOrderScratch,
                    orderedInstanceRecords,
                    instancesArrayPayload,
                    out instanceArrayBytesWritten,
                    out diagnostic);

                if (status is not EbinWriteStatus.Complete)
                {
                    return status;
                }

                if (instanceArrayBytesWritten != instancesArrayPayload.Length)
                {
                    throw new InvalidOperationException("The Scene instance array extent changed after capture.");
                }

                status = WriteWorldHierarchy(
                    world,
                    lease,
                    limits,
                    ref hierarchyBuffers,
                    out diagnostic,
                    cancellationToken);

                if (status is not EbinWriteStatus.Complete)
                {
                    return status;
                }

                status = ScenePersistenceScenesWriter.Write(
                    scenesWorkspace,
                    instances.Length,
                    instancesArrayPayload,
                    hierarchyBuffers.Record,
                    limits,
                    scenesRootPayloadStorage,
                    scenesCandidateStorage,
                    scenesDestination,
                    out scenesBytesWritten,
                    out diagnostic);

                if (status is not EbinWriteStatus.Complete)
                {
                    return status;
                }

                if (scenesBytesWritten != scenesDestination.Length)
                {
                    throw new InvalidOperationException("The Scene record extent changed after capture.");
                }

                record = scenesDestination;
            }

            reservation = outputReservationScope;
            return EbinWriteStatus.Complete;
        }
        finally
        {
            scratchReservationScope.Dispose();

            if (reservation is null)
            {
                outputReservationScope.Dispose();
            }
        }
    }

    private static EbinWriteStatus BuildInstanceBuffers(
        SceneInstance instance,
        WorldPersistenceCaptureLease lease,
        EbinReadLimits limits,
        out InstanceCaptureBuffers buffers,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        buffers = default;
        diagnostic = null;
        var plan = instance.GetPersistencePlan(lease);
        var entityCount = instance.GetPersistenceEntityCount(lease);
        var entityIds = new Guid[entityCount];
        var entityIdsPayload = new byte[checked(32 + (entityCount * 16))];
        var status = EbinPayloadWriter.WriteFixedArray(
            entityIds,
            limits,
            entityIdsPayload,
            out var entityIdsPayloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (entityIdsPayloadBytes != entityIdsPayload.Length)
        {
            throw new InvalidOperationException("The Scene EntityId fixed-array extent changed after measurement.");
        }

        var bindingSlices = new EbinPayloadSlice[entityCount];
        long bindingStorageBytes = 0;
        var bindingCandidateBytes = 0;
        var bindingIndex = 0;

        foreach (var expandedEntityIndex in plan.EntityIndicesByAssignmentOrdinal)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!instance.TryGetPersistenceEntityId(lease, expandedEntityIndex, out _))
            {
                continue;
            }

            status = instance.MeasurePersistenceEntityBindingRecord(
                lease,
                expandedEntityIndex,
                out var bindingRecordBytes,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            if (bindingStorageBytes > int.MaxValue)
            {
                diagnostic = CreateDiagnostic("record-byte-budget", checked((ulong)bindingStorageBytes), int.MaxValue);
                return EbinWriteStatus.InvalidRecord;
            }

            bindingSlices[bindingIndex++] = new EbinPayloadSlice(
                checked((int)bindingStorageBytes),
                bindingRecordBytes);
            bindingStorageBytes = checked(bindingStorageBytes + bindingRecordBytes);
            bindingCandidateBytes = Math.Max(bindingCandidateBytes, bindingRecordBytes);

            if (bindingStorageBytes > int.MaxValue)
            {
                diagnostic = CreateDiagnostic("record-byte-budget", checked((ulong)bindingStorageBytes), int.MaxValue);
                return EbinWriteStatus.InvalidRecord;
            }
        }

        if (bindingIndex != entityCount)
        {
            throw new InvalidOperationException("The Scene Entity binding count changed during capture measurement.");
        }

        status = WorldPersistenceRecordArrayLayout.MeasurePayloadBytes(
            entityCount,
            bindingStorageBytes,
            limits,
            out var bindingArrayPayloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var bindingRecordStorage = new byte[checked((int)bindingStorageBytes)];
        var bindingCandidateStorage = new byte[bindingCandidateBytes];
        var bindingArrayPayload = new byte[bindingArrayPayloadBytes];
        var externalBindings = instance.GetExternalBindingCaptureRows(lease);
        status = ScenePersistenceExternalBindingArrayWriter.Measure(
            externalBindings,
            limits,
            out var externalBindingRecordStorageBytes,
            out var externalBindingCandidateBytes,
            out var externalBindingArrayPayloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var externalBindingSlices = new EbinPayloadSlice[externalBindings.Length];
        var externalBindingRecordStorage = new byte[externalBindingRecordStorageBytes];
        var externalBindingCandidateStorage = new byte[externalBindingCandidateBytes];
        var externalBindingArrayPayload = new byte[externalBindingArrayPayloadBytes];
        var occurrenceCount = plan.Occurrences.Length;
        var occurrenceSlices = new EbinPayloadSlice[occurrenceCount];
        long occurrenceRecordStorageBytes = 0;
        var occurrenceCandidateBytes = 0;

        for (var occurrenceIndex = 0; occurrenceIndex < occurrenceCount; occurrenceIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            status = instance.MeasurePersistenceOccurrenceRecord(
                lease,
                occurrenceIndex,
                out var occurrenceRecordBytes,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            if (occurrenceRecordStorageBytes > int.MaxValue)
            {
                diagnostic = CreateDiagnostic("record-byte-budget", checked((ulong)occurrenceRecordStorageBytes), int.MaxValue);
                return EbinWriteStatus.InvalidRecord;
            }

            occurrenceSlices[occurrenceIndex] = new EbinPayloadSlice(
                checked((int)occurrenceRecordStorageBytes),
                occurrenceRecordBytes);
            occurrenceRecordStorageBytes = checked(occurrenceRecordStorageBytes + occurrenceRecordBytes);
            occurrenceCandidateBytes = Math.Max(occurrenceCandidateBytes, occurrenceRecordBytes);

            if (occurrenceRecordStorageBytes > int.MaxValue)
            {
                diagnostic = CreateDiagnostic("record-byte-budget", checked((ulong)occurrenceRecordStorageBytes), int.MaxValue);
                return EbinWriteStatus.InvalidRecord;
            }
        }

        status = WorldPersistenceRecordArrayLayout.MeasurePayloadBytes(
            occurrenceCount,
            occurrenceRecordStorageBytes,
            limits,
            out var occurrenceArrayPayloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var occurrenceRecordStorage = new byte[checked((int)occurrenceRecordStorageBytes)];
        var occurrenceCandidateStorage = new byte[occurrenceCandidateBytes];
        var occurrenceArrayPayload = new byte[occurrenceArrayPayloadBytes];
        status = WriteInstanceArrays(
            instance,
            lease,
            limits,
            entityIds,
            entityIdsPayload,
            bindingSlices,
            bindingRecordStorage,
            bindingCandidateStorage,
            bindingArrayPayload,
            externalBindingSlices,
            externalBindingRecordStorage,
            externalBindingCandidateStorage,
            externalBindingArrayPayload,
            occurrenceSlices,
            occurrenceRecordStorage,
            occurrenceCandidateStorage,
            occurrenceArrayPayload,
            out diagnostic,
            cancellationToken);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = ScenePersistenceOccurrenceWriter.MeasureSourceAssetReference(
            plan,
            0,
            limits,
            out var sourceRecordBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var rootPayloadStorageBytes = checked(
            16L
            + sourceRecordBytes
            + entityIdsPayload.Length
            + bindingArrayPayload.Length
            + externalBindingArrayPayload.Length
            + sizeof(long)
            + occurrenceArrayPayload.Length);

        if ((rootPayloadStorageBytes > int.MaxValue) || (rootPayloadStorageBytes > limits.MaximumRecordBytes))
        {
            diagnostic = CreateDiagnostic(
                "record-byte-budget",
                checked((ulong)rootPayloadStorageBytes),
                checked((ulong)Math.Min(int.MaxValue, limits.MaximumRecordBytes)));
            return EbinWriteStatus.InvalidRecord;
        }

        var rootPayloadStorage = new byte[checked((int)rootPayloadStorageBytes)];
        status = ScenePersistenceSceneInstanceWriter.Measure(
            instance,
            lease,
            entityIdsPayload,
            bindingArrayPayload,
            externalBindingArrayPayload,
            occurrenceArrayPayload,
            limits,
            rootPayloadStorage,
            out var workspace,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        buffers = new InstanceCaptureBuffers {
            BindingArrayPayload = bindingArrayPayload,
            BindingCandidateStorage = bindingCandidateStorage,
            BindingRecordStorage = bindingRecordStorage,
            BindingSlices = bindingSlices,
            EntityIds = entityIds,
            EntityIdsPayload = entityIdsPayload,
            ExternalBindingArrayPayload = externalBindingArrayPayload,
            ExternalBindingCandidateStorage = externalBindingCandidateStorage,
            ExternalBindingRecordStorage = externalBindingRecordStorage,
            ExternalBindingSlices = externalBindingSlices,
            OccurrenceArrayPayload = occurrenceArrayPayload,
            OccurrenceCandidateStorage = occurrenceCandidateStorage,
            OccurrenceRecordStorage = occurrenceRecordStorage,
            OccurrenceSlices = occurrenceSlices,
            RootPayloadStorage = rootPayloadStorage,
            Workspace = workspace,
        };
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus BuildWorldHierarchyBuffers(
        World world,
        WorldPersistenceCaptureLease lease,
        EbinReadLimits limits,
        out WorldHierarchyBuffers buffers,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        buffers = default;
        diagnostic = null;
        cancellationToken.ThrowIfCancellationRequested();
        var entityRows = new World.EntityStateCaptureRow[lease.GetEntityStateCount()];
        var hierarchyRows = new SceneWorldHierarchyCaptureRow[entityRows.Length];
        ScenePersistenceWorldHierarchyCapture.Capture(
            world,
            lease,
            entityRows,
            hierarchyRows,
            out var rootCount,
            out var childCount);
        var rootEntityIds = new Guid[rootCount];
        var childEntityIds = new Guid[childCount];
        ScenePersistenceWorldHierarchyCapture.CopyCanonicalLists(
            world,
            lease,
            entityRows,
            hierarchyRows,
            rootEntityIds,
            childEntityIds);
        var nodePayloadBytes = 0;

        foreach (var row in hierarchyRows)
        {
            nodePayloadBytes = Math.Max(
                nodePayloadBytes,
                ScenePersistenceWorldHierarchyNodeWriter.GetPayloadBytes(row.ParentEntityId.HasValue, row.ChildCount));
        }

        var nodePayloadStorage = new byte[nodePayloadBytes];
        var status = ScenePersistenceWorldHierarchyNodeArrayWriter.Measure(
            hierarchyRows,
            childEntityIds,
            limits,
            nodePayloadStorage,
            out var nodeWorkspace,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var nodeRecordSlices = new EbinPayloadSlice[nodeWorkspace.NodeCount];
        var nodeRecordStorage = new byte[nodeWorkspace.RecordStorageBytes];
        var nodeCandidateStorage = new byte[nodeWorkspace.MaximumCandidateBytes];
        var nodeArrayPayload = new byte[nodeWorkspace.ArrayPayloadBytes];
        status = ScenePersistenceWorldHierarchyNodeArrayWriter.Write(
            hierarchyRows,
            childEntityIds,
            limits,
            nodeWorkspace,
            nodeRecordSlices,
            nodeRecordStorage,
            nodeCandidateStorage,
            nodePayloadStorage,
            nodeArrayPayload,
            out var nodeArrayBytesWritten,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (nodeArrayBytesWritten != nodeArrayPayload.Length)
        {
            throw new InvalidOperationException("The World hierarchy node array extent changed after measurement.");
        }

        var rootPayloadStorage = new byte[checked(
            nodeArrayPayload.Length
            + 32
            + (rootEntityIds.Length * 16))];
        status = ScenePersistenceWorldHierarchyWriter.Measure(
            hierarchyRows.Length,
            nodeArrayPayload,
            rootEntityIds,
            limits,
            rootPayloadStorage,
            out var recordWorkspace,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var recordCandidateStorage = new byte[recordWorkspace.RecordBytes];
        var record = new byte[recordWorkspace.RecordBytes];
        status = ScenePersistenceWorldHierarchyWriter.Write(
            recordWorkspace,
            hierarchyRows.Length,
            nodeArrayPayload,
            rootEntityIds,
            limits,
            rootPayloadStorage,
            recordCandidateStorage,
            record,
            out var bytesWritten,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (bytesWritten != record.Length)
        {
            throw new InvalidOperationException("The World hierarchy record extent changed after measurement.");
        }

        buffers = new WorldHierarchyBuffers {
            ChildCount = childCount,
            ChildEntityIds = childEntityIds,
            EntityRows = entityRows,
            HierarchyRows = hierarchyRows,
            NodeArrayPayload = nodeArrayPayload,
            NodeCandidateStorage = nodeCandidateStorage,
            NodePayloadStorage = nodePayloadStorage,
            NodeRecordSlices = nodeRecordSlices,
            NodeRecordStorage = nodeRecordStorage,
            NodeWorkspace = nodeWorkspace,
            Record = record,
            RecordCandidateStorage = recordCandidateStorage,
            RecordWorkspace = recordWorkspace,
            RootCount = rootCount,
            RootEntityIds = rootEntityIds,
            RootPayloadStorage = rootPayloadStorage,
        };
        return EbinWriteStatus.Complete;
    }

    private static EbinStructuralDiagnostic CreateDiagnostic(string category, ulong actual, ulong expected)
        => new(
            s_schemaId,
            1,
            category,
            null,
            actual,
            expected,
            EbinStructuralDiagnosticSource.Construction);

    private static long GetWorkspaceBytes(
        scoped ReadOnlySpan<InstanceCaptureBuffers> instanceBuffers,
        ScenePersistenceSceneInstanceArrayWorkspaceRequirements instanceArrayWorkspace,
        scoped ReadOnlySpan<byte> instanceCandidateStorage,
        WorldHierarchyBuffers hierarchyBuffers,
        ScenePersistenceScenesWorkspaceRequirements scenesWorkspace,
        int instancesArrayPayloadBytes)
    {
        long workspaceBytes = 0;
        workspaceBytes = checked(
            workspaceBytes
            + ((long)instanceBuffers.Length * Unsafe.SizeOf<InstanceCaptureBuffers>())
            + instanceArrayWorkspace.WorkspaceBytes
            + instanceCandidateStorage.Length
            + hierarchyBuffers.GetWorkspaceBytes()
            + scenesWorkspace.WorkspaceBytes
            - instancesArrayPayloadBytes
            - hierarchyBuffers.Record.Length);

        for (var index = 0; index < instanceBuffers.Length; index++)
        {
            workspaceBytes = checked(workspaceBytes + instanceBuffers[index].GetWorkspaceBytes());
        }

        return workspaceBytes;
    }

    private static EbinWriteStatus WriteInstanceArrays(
        SceneInstance instance,
        WorldPersistenceCaptureLease lease,
        EbinReadLimits limits,
        scoped Span<Guid> entityIds,
        scoped Span<byte> entityIdsPayload,
        scoped ReadOnlySpan<EbinPayloadSlice> bindingSlices,
        scoped Span<byte> bindingRecordStorage,
        scoped Span<byte> bindingCandidateStorage,
        scoped Span<byte> bindingArrayPayload,
        scoped Span<EbinPayloadSlice> externalBindingSlices,
        scoped Span<byte> externalBindingRecordStorage,
        scoped Span<byte> externalBindingCandidateStorage,
        scoped Span<byte> externalBindingArrayPayload,
        scoped ReadOnlySpan<EbinPayloadSlice> occurrenceSlices,
        scoped Span<byte> occurrenceRecordStorage,
        scoped Span<byte> occurrenceCandidateStorage,
        scoped Span<byte> occurrenceArrayPayload,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        instance.CopyPersistenceEntityIds(lease, entityIds);
        var status = EbinPayloadWriter.WriteFixedArray(
            entityIds,
            limits,
            entityIdsPayload,
            out var entityIdsPayloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (entityIdsPayloadBytes != entityIdsPayload.Length)
        {
            throw new InvalidOperationException("The Scene EntityId fixed-array extent changed after capture measurement.");
        }

        var plan = instance.GetPersistencePlan(lease);
        var bindingIndex = 0;

        foreach (var expandedEntityIndex in plan.EntityIndicesByAssignmentOrdinal)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!instance.TryGetPersistenceEntityId(lease, expandedEntityIndex, out _))
            {
                continue;
            }

            if (bindingIndex >= bindingSlices.Length)
            {
                throw new InvalidOperationException("The live Scene Entity binding set grew after measurement.");
            }

            var slice = bindingSlices[bindingIndex++];
            status = instance.WritePersistenceEntityBindingRecord(
                lease,
                expandedEntityIndex,
                bindingCandidateStorage[..slice.ByteLength],
                bindingRecordStorage.Slice(slice.Offset, slice.ByteLength),
                out var bytesWritten,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            if (bytesWritten != slice.ByteLength)
            {
                throw new InvalidOperationException("The Scene Entity binding record extent changed after measurement.");
            }
        }

        if (bindingIndex != bindingSlices.Length)
        {
            throw new InvalidOperationException("The live Scene Entity binding set shrank after measurement.");
        }

        status = EbinPayloadWriter.WriteRecordArray(
            bindingSlices,
            bindingRecordStorage,
            limits,
            bindingArrayPayload,
            out var bindingArrayBytesWritten,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (bindingArrayBytesWritten != bindingArrayPayload.Length)
        {
            throw new InvalidOperationException("The Scene Entity binding array extent changed after measurement.");
        }

        var externalBindings = instance.GetExternalBindingCaptureRows(lease);
        status = ScenePersistenceExternalBindingArrayWriter.Write(
            externalBindings,
            limits,
            externalBindingSlices,
            externalBindingRecordStorage,
            externalBindingCandidateStorage,
            externalBindingArrayPayload,
            out var externalBindingArrayBytesWritten,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (externalBindingArrayBytesWritten != externalBindingArrayPayload.Length)
        {
            throw new InvalidOperationException("The external Scene binding array extent changed after measurement.");
        }

        for (var occurrenceIndex = 0; occurrenceIndex < occurrenceSlices.Length; occurrenceIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var slice = occurrenceSlices[occurrenceIndex];
            status = instance.WritePersistenceOccurrenceRecord(
                lease,
                occurrenceIndex,
                occurrenceCandidateStorage[..slice.ByteLength],
                occurrenceRecordStorage.Slice(slice.Offset, slice.ByteLength),
                out var bytesWritten,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            if (bytesWritten != slice.ByteLength)
            {
                throw new InvalidOperationException("The Scene occurrence record extent changed after measurement.");
            }
        }

        status = EbinPayloadWriter.WriteRecordArray(
            occurrenceSlices,
            occurrenceRecordStorage,
            limits,
            occurrenceArrayPayload,
            out var occurrenceArrayBytesWritten,
            out diagnostic);

        if (status is EbinWriteStatus.Complete)
        {
            if (occurrenceArrayBytesWritten != occurrenceArrayPayload.Length)
            {
                throw new InvalidOperationException("The Scene occurrence array extent changed after measurement.");
            }
        }

        return status;
    }

    private static EbinWriteStatus WriteSceneInstanceRecords(
        scoped ReadOnlySpan<SceneInstance> instances,
        scoped Span<InstanceCaptureBuffers> instanceBuffers,
        WorldPersistenceCaptureLease lease,
        EbinReadLimits limits,
        scoped Span<EbinPayloadSlice> recordSlices,
        scoped Span<byte> recordStorage,
        scoped Span<byte> candidateStorage,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        diagnostic = null;
        var recordOffset = 0;

        for (var index = 0; index < instances.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ref var buffers = ref instanceBuffers[index];
            var status = WriteInstanceArrays(
                instances[index],
                lease,
                limits,
                buffers.EntityIds,
                buffers.EntityIdsPayload,
                buffers.BindingSlices,
                buffers.BindingRecordStorage,
                buffers.BindingCandidateStorage,
                buffers.BindingArrayPayload,
                buffers.ExternalBindingSlices,
                buffers.ExternalBindingRecordStorage,
                buffers.ExternalBindingCandidateStorage,
                buffers.ExternalBindingArrayPayload,
                buffers.OccurrenceSlices,
                buffers.OccurrenceRecordStorage,
                buffers.OccurrenceCandidateStorage,
                buffers.OccurrenceArrayPayload,
                out diagnostic,
                cancellationToken);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            var recordBytes = buffers.Workspace.RecordBytes;
            status = ScenePersistenceSceneInstanceWriter.Write(
                instances[index],
                lease,
                buffers.Workspace,
                buffers.EntityIdsPayload,
                buffers.BindingArrayPayload,
                buffers.ExternalBindingArrayPayload,
                buffers.OccurrenceArrayPayload,
                limits,
                buffers.RootPayloadStorage,
                candidateStorage[..recordBytes],
                recordStorage.Slice(recordOffset, recordBytes),
                out var bytesWritten,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            if (bytesWritten != recordBytes)
            {
                throw new InvalidOperationException("The Scene instance record extent changed after measurement.");
            }

            recordSlices[index] = new EbinPayloadSlice(recordOffset, bytesWritten);
            recordOffset = checked(recordOffset + bytesWritten);
        }

        if (recordOffset != recordStorage.Length)
        {
            throw new InvalidOperationException("The Scene instance record storage extent changed after measurement.");
        }

        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus WriteWorldHierarchy(
        World world,
        WorldPersistenceCaptureLease lease,
        EbinReadLimits limits,
        ref WorldHierarchyBuffers buffers,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ScenePersistenceWorldHierarchyCapture.Capture(
            world,
            lease,
            buffers.EntityRows,
            buffers.HierarchyRows,
            out var rootCount,
            out var childCount);

        if ((rootCount != buffers.RootCount) || (childCount != buffers.ChildCount))
        {
            throw new InvalidOperationException("The World hierarchy counts changed after capture measurement.");
        }

        ScenePersistenceWorldHierarchyCapture.CopyCanonicalLists(
            world,
            lease,
            buffers.EntityRows,
            buffers.HierarchyRows,
            buffers.RootEntityIds,
            buffers.ChildEntityIds);
        var status = ScenePersistenceWorldHierarchyNodeArrayWriter.Write(
            buffers.HierarchyRows,
            buffers.ChildEntityIds,
            limits,
            buffers.NodeWorkspace,
            buffers.NodeRecordSlices,
            buffers.NodeRecordStorage,
            buffers.NodeCandidateStorage,
            buffers.NodePayloadStorage,
            buffers.NodeArrayPayload,
            out var nodeArrayBytesWritten,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (nodeArrayBytesWritten != buffers.NodeArrayPayload.Length)
        {
            throw new InvalidOperationException("The World hierarchy node array extent changed after capture.");
        }

        status = ScenePersistenceWorldHierarchyWriter.Write(
            buffers.RecordWorkspace,
            buffers.HierarchyRows.Length,
            buffers.NodeArrayPayload,
            buffers.RootEntityIds,
            limits,
            buffers.RootPayloadStorage,
            buffers.RecordCandidateStorage,
            buffers.Record,
            out var recordBytesWritten,
            out diagnostic);

        if ((status is EbinWriteStatus.Complete) && (recordBytesWritten != buffers.Record.Length))
        {
            throw new InvalidOperationException("The World hierarchy record extent changed after capture.");
        }

        return status;
    }

    private struct InstanceCaptureBuffers
    {
        internal byte[] BindingArrayPayload;

        internal byte[] BindingCandidateStorage;

        internal byte[] BindingRecordStorage;

        internal EbinPayloadSlice[] BindingSlices;

        internal Guid[] EntityIds;

        internal byte[] EntityIdsPayload;

        internal byte[] ExternalBindingArrayPayload;

        internal byte[] ExternalBindingCandidateStorage;

        internal byte[] ExternalBindingRecordStorage;

        internal EbinPayloadSlice[] ExternalBindingSlices;

        internal byte[] OccurrenceArrayPayload;

        internal byte[] OccurrenceCandidateStorage;

        internal byte[] OccurrenceRecordStorage;

        internal EbinPayloadSlice[] OccurrenceSlices;

        internal byte[] RootPayloadStorage;

        internal ScenePersistenceInstanceWorkspaceRequirements Workspace;

        internal readonly long GetWorkspaceBytes()
        {
            var bytes = checked(Workspace.WorkspaceBytes - (2L * Workspace.RecordBytes));
            bytes = checked(bytes + ((long)EntityIds.Length * Unsafe.SizeOf<Guid>()));
            bytes = checked(bytes + ((long)BindingSlices.Length * Unsafe.SizeOf<EbinPayloadSlice>()));
            bytes = checked(bytes + BindingRecordStorage.Length);
            bytes = checked(bytes + BindingCandidateStorage.Length);
            bytes = checked(bytes + ((long)ExternalBindingSlices.Length * Unsafe.SizeOf<EbinPayloadSlice>()));
            bytes = checked(bytes + ExternalBindingRecordStorage.Length);
            bytes = checked(bytes + ExternalBindingCandidateStorage.Length);
            bytes = checked(bytes + ((long)OccurrenceSlices.Length * Unsafe.SizeOf<EbinPayloadSlice>()));
            bytes = checked(bytes + OccurrenceRecordStorage.Length);
            bytes = checked(bytes + OccurrenceCandidateStorage.Length);
            return bytes;
        }
    }

    private struct WorldHierarchyBuffers
    {
        internal int ChildCount;

        internal Guid[] ChildEntityIds;

        internal World.EntityStateCaptureRow[] EntityRows;

        internal SceneWorldHierarchyCaptureRow[] HierarchyRows;

        internal byte[] NodeArrayPayload;

        internal byte[] NodeCandidateStorage;

        internal byte[] NodePayloadStorage;

        internal EbinPayloadSlice[] NodeRecordSlices;

        internal byte[] NodeRecordStorage;

        internal ScenePersistenceWorldHierarchyNodeArrayWorkspaceRequirements NodeWorkspace;

        internal byte[] Record;

        internal byte[] RecordCandidateStorage;

        internal ScenePersistenceWorldHierarchyWorkspaceRequirements RecordWorkspace;

        internal int RootCount;

        internal Guid[] RootEntityIds;

        internal byte[] RootPayloadStorage;

        internal readonly long GetWorkspaceBytes()
        {
            var bytes = checked(
                ((long)EntityRows.Length * Unsafe.SizeOf<World.EntityStateCaptureRow>())
                + NodeWorkspace.WorkspaceBytes
                + RecordWorkspace.WorkspaceBytes
                - NodeArrayPayload.Length);
            return bytes;
        }
    }
}
