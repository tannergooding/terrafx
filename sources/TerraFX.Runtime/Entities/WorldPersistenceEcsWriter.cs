// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceEcsWriter
{
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("state/ecs").SchemaId;

    internal static EbinWriteStatus MeasureWorkspace(
        WorldPersistenceCaptureLease lease,
        ref WorldPersistenceEcsBuffers buffers,
        EbinReadLimits limits,
        out WorldPersistenceEcsWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        workspace = default;
        diagnostic = null;

        if (limits.MaximumFieldsPerRecord < 5)
        {
            diagnostic = CreateDiagnostic("field-count-budget", 5, checked((ulong)limits.MaximumFieldsPerRecord));
            return EbinWriteStatus.InvalidRecord;
        }

        var entityCount = lease.GetEntityStateCount();
        var systemCount = lease.GetSystemStateCount();
        var resourceCount = lease.GetResourceStateCount();
        var attachmentCount = lease.GetAttachmentStateCount();
        var history = lease.GetIdentityHistoryCapture();
        ValidateMeasurementBuffers(
            buffers,
            entityCount,
            systemCount,
            resourceCount,
            attachmentCount,
            history);
        buffers.EntitySlices.Clear();
        buffers.SystemSlices.Clear();
        buffers.ResourceSlices.Clear();
        buffers.AttachmentSlices.Clear();

        var entityStatus = MeasureEntityRecords(
            lease,
            ref buffers,
            limits,
            cancellationToken,
            out var entityStorageBytes,
            out var entityComponentStorageBytes,
            out var entityTagStorageBytes,
            out var entityTransientSchemaStorageBytes,
            out var entityChildCandidateBytes,
            out var entityPayloadStorageBytes,
            out var entityCandidateStorageBytes,
            out diagnostic);

        if (entityStatus is not EbinWriteStatus.Complete)
        {
            return entityStatus;
        }

        var systemStatus = MeasureSystemRecords(
            lease,
            buffers.SystemSlices,
            limits,
            cancellationToken,
            out var systemStorageBytes,
            out var statePayloadRecordStorageBytes,
            out var stateRowPayloadStorageBytes,
            out var stateRowCandidateStorageBytes,
            out diagnostic);

        if (systemStatus is not EbinWriteStatus.Complete)
        {
            return systemStatus;
        }

        var resourceStatus = MeasureResourceRecords(
            lease,
            buffers.ResourceSlices,
            limits,
            cancellationToken,
            ref statePayloadRecordStorageBytes,
            ref stateRowPayloadStorageBytes,
            ref stateRowCandidateStorageBytes,
            out var resourceStorageBytes,
            out diagnostic);

        if (resourceStatus is not EbinWriteStatus.Complete)
        {
            return resourceStatus;
        }

        var attachmentStatus = MeasureAttachmentRecords(
            lease,
            ref buffers,
            limits,
            cancellationToken,
            ref statePayloadRecordStorageBytes,
            ref stateRowPayloadStorageBytes,
            ref stateRowCandidateStorageBytes,
            out var attachmentStorageBytes,
            out diagnostic);

        if (attachmentStatus is not EbinWriteStatus.Complete)
        {
            return attachmentStatus;
        }

        var identityStatus = lease.MeasureIdentityHistoryRecord(
            in history,
            buffers.IdentityBranchSlices,
            buffers.IdentityPageSlices,
            buffers.IdentityPageStableIds,
            buffers.IdentityPageMetadata,
            buffers.IdentityPagePayloadStorage,
            out var identityWorkspace,
            out diagnostic);

        if (identityStatus is not EbinWriteStatus.Complete)
        {
            return identityStatus;
        }

        var arrayStatus = WorldPersistenceRecordArrayLayout.MeasurePayloadBytes(
            entityCount,
            entityStorageBytes,
            limits,
            out var entityArrayPayloadBytes,
            out diagnostic);

        if (arrayStatus is not EbinWriteStatus.Complete)
        {
            return arrayStatus;
        }

        arrayStatus = WorldPersistenceRecordArrayLayout.MeasurePayloadBytes(
            systemCount,
            systemStorageBytes,
            limits,
            out var systemArrayPayloadBytes,
            out diagnostic);

        if (arrayStatus is not EbinWriteStatus.Complete)
        {
            return arrayStatus;
        }

        arrayStatus = WorldPersistenceRecordArrayLayout.MeasurePayloadBytes(
            resourceCount,
            resourceStorageBytes,
            limits,
            out var resourceArrayPayloadBytes,
            out diagnostic);

        if (arrayStatus is not EbinWriteStatus.Complete)
        {
            return arrayStatus;
        }

        arrayStatus = WorldPersistenceRecordArrayLayout.MeasurePayloadBytes(
            attachmentCount,
            attachmentStorageBytes,
            limits,
            out var attachmentArrayPayloadBytes,
            out diagnostic);

        if (arrayStatus is not EbinWriteStatus.Complete)
        {
            return arrayStatus;
        }

        var rootPayloadStorageBytes = checked(
            (long)entityArrayPayloadBytes
            + systemArrayPayloadBytes
            + resourceArrayPayloadBytes
            + attachmentArrayPayloadBytes
            + identityWorkspace.RecordBytes);

        if (rootPayloadStorageBytes > int.MaxValue)
        {
            diagnostic = CreateDiagnostic(
                "record-byte-budget",
                checked((ulong)rootPayloadStorageBytes),
                checked((ulong)limits.MaximumRecordBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        var recordBytes = GetRecordBytes(
            entityArrayPayloadBytes,
            systemArrayPayloadBytes,
            resourceArrayPayloadBytes,
            attachmentArrayPayloadBytes,
            identityWorkspace.RecordBytes);

        if (recordBytes > limits.MaximumRecordBytes)
        {
            diagnostic = CreateDiagnostic(
                "record-byte-budget",
                checked((ulong)recordBytes),
                checked((ulong)limits.MaximumRecordBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        workspace = new WorldPersistenceEcsWorkspaceRequirements(
            entityCount,
            systemCount,
            resourceCount,
            attachmentCount,
            entityStorageBytes,
            systemStorageBytes,
            resourceStorageBytes,
            attachmentStorageBytes,
            entityArrayPayloadBytes,
            systemArrayPayloadBytes,
            resourceArrayPayloadBytes,
            attachmentArrayPayloadBytes,
            entityComponentStorageBytes,
            entityTagStorageBytes,
            entityTransientSchemaStorageBytes,
            entityChildCandidateBytes,
            entityPayloadStorageBytes,
            entityCandidateStorageBytes,
            statePayloadRecordStorageBytes,
            stateRowPayloadStorageBytes,
            stateRowCandidateStorageBytes,
            checked((int)rootPayloadStorageBytes),
            checked((int)recordBytes),
            identityWorkspace);
        return EbinWriteStatus.Complete;
    }

    internal static EbinWriteStatus Write(
        WorldPersistenceCaptureLease lease,
        ref WorldPersistenceEcsBuffers buffers,
        EbinReadLimits limits,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        var status = MeasureWorkspace(
            lease,
            ref buffers,
            limits,
            out var workspace,
            out diagnostic,
            cancellationToken);
        bytesWritten = 0;

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        ValidateWriteBuffers(buffers, workspace);

        status = WriteEntityRecords(
            lease,
            ref buffers,
            limits,
            workspace,
            out diagnostic,
            cancellationToken);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = WriteSystemRecords(
            lease,
            buffers.SystemSlices,
            buffers.SystemStorage,
            buffers.StatePayloadRecordStorage,
            buffers.StateRowPayloadStorage,
            buffers.StateRowCandidateStorage,
            limits,
            out diagnostic,
            cancellationToken);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = WriteResourceRecords(
            lease,
            buffers.ResourceSlices,
            buffers.ResourceStorage,
            buffers.StatePayloadRecordStorage,
            buffers.StateRowPayloadStorage,
            buffers.StateRowCandidateStorage,
            limits,
            out diagnostic,
            cancellationToken);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = WriteAttachmentRecords(
            lease,
            ref buffers,
            limits,
            out diagnostic,
            cancellationToken);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        buffers.RootPayloadStorage.Clear();
        var fieldCount = 0;
        var payloadBytes = 0;
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[5];
        status = EbinPayloadWriter.WriteRecordArray(
            buffers.EntitySlices,
            buffers.EntityStorage,
            limits,
            buffers.RootPayloadStorage.Slice(payloadBytes, workspace.EntityArrayPayloadBytes),
            out var arrayBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (arrayBytes != workspace.EntityArrayPayloadBytes)
        {
            throw new InvalidOperationException("The state/ecs Entity array extent changed after workspace measurement.");
        }

        fields[fieldCount++] = new EbinFieldSource(
            1,
            24,
            1,
            checked((ulong)workspace.EntityCount),
            new EbinPayloadSlice(payloadBytes, arrayBytes));
        payloadBytes = checked(payloadBytes + arrayBytes);
        status = EbinPayloadWriter.WriteRecordArray(
            buffers.SystemSlices,
            buffers.SystemStorage,
            limits,
            buffers.RootPayloadStorage.Slice(payloadBytes, workspace.SystemArrayPayloadBytes),
            out arrayBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (arrayBytes != workspace.SystemArrayPayloadBytes)
        {
            throw new InvalidOperationException("The state/ecs System array extent changed after workspace measurement.");
        }

        fields[fieldCount++] = new EbinFieldSource(
            2,
            24,
            1,
            checked((ulong)workspace.SystemCount),
            new EbinPayloadSlice(payloadBytes, arrayBytes));
        payloadBytes = checked(payloadBytes + arrayBytes);
        status = EbinPayloadWriter.WriteRecordArray(
            buffers.ResourceSlices,
            buffers.ResourceStorage,
            limits,
            buffers.RootPayloadStorage.Slice(payloadBytes, workspace.ResourceArrayPayloadBytes),
            out arrayBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (arrayBytes != workspace.ResourceArrayPayloadBytes)
        {
            throw new InvalidOperationException("The state/ecs Resource array extent changed after workspace measurement.");
        }

        fields[fieldCount++] = new EbinFieldSource(
            3,
            24,
            1,
            checked((ulong)workspace.ResourceCount),
            new EbinPayloadSlice(payloadBytes, arrayBytes));
        payloadBytes = checked(payloadBytes + arrayBytes);
        status = EbinPayloadWriter.WriteRecordArray(
            buffers.AttachmentSlices,
            buffers.AttachmentStorage,
            limits,
            buffers.RootPayloadStorage.Slice(payloadBytes, workspace.AttachmentArrayPayloadBytes),
            out arrayBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (arrayBytes != workspace.AttachmentArrayPayloadBytes)
        {
            throw new InvalidOperationException("The state/ecs Attachment array extent changed after workspace measurement.");
        }

        fields[fieldCount++] = new EbinFieldSource(
            4,
            24,
            1,
            checked((ulong)workspace.AttachmentCount),
            new EbinPayloadSlice(payloadBytes, arrayBytes));
        payloadBytes = checked(payloadBytes + arrayBytes);
        var identityHistoryOffset = payloadBytes;
        var history = lease.GetIdentityHistoryCapture();
        cancellationToken.ThrowIfCancellationRequested();
        status = WorldPersistenceIdentityHistoryWriter.Write(
            in history,
            limits,
            buffers.IdentityBranchSlices,
            buffers.IdentityBranchStorage,
            buffers.IdentityPageSlices,
            buffers.IdentityPageStorage,
            buffers.IdentityPageStableIds,
            buffers.IdentityPageMetadata,
            buffers.IdentityPagePayloadStorage,
            buffers.IdentityChildCandidateStorage,
            buffers.IdentityRootPayloadStorage,
            buffers.IdentityCandidateStorage,
            buffers.RootPayloadStorage.Slice(identityHistoryOffset, workspace.IdentityHistory.RecordBytes),
            out var identityHistoryBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (identityHistoryBytes != workspace.IdentityHistory.RecordBytes)
        {
            throw new InvalidOperationException("The state/ecs identity-history extent changed after workspace measurement.");
        }

        fields[fieldCount++] = new EbinFieldSource(
            5,
            22,
            1,
            1,
            new EbinPayloadSlice(identityHistoryOffset, identityHistoryBytes));
        payloadBytes = checked(payloadBytes + identityHistoryBytes);

        if (payloadBytes != workspace.RootPayloadStorageBytes)
        {
            throw new InvalidOperationException("The state/ecs payload extent changed after workspace measurement.");
        }

        var source = new EbinRecordWriteView(
            s_schemaId,
            major: 1,
            minor: 0,
            fields[..fieldCount],
            buffers.RootPayloadStorage[..payloadBytes]);
        status = EbinStructuralWriter.Measure(in source, limits, out var measuredRecordBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (measuredRecordBytes != workspace.RecordBytes)
        {
            diagnostic = CreateDiagnostic(
                "ecs-record-size-mismatch",
                checked((ulong)measuredRecordBytes),
                checked((ulong)workspace.RecordBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        return EbinStructuralWriter.Write(
            in source,
            limits,
            buffers.EcsCandidateStorage,
            buffers.Destination,
            out bytesWritten,
            out diagnostic);
    }

    private static long Align16(long value)
        => checked((value + 15L) & ~15L);

    private static EbinWriteStatus AppendRecordSlice(
        scoped Span<EbinPayloadSlice> slices,
        int index,
        int recordBytes,
        EbinReadLimits limits,
        ref long storageBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        diagnostic = null;

        if (recordBytes <= 0)
        {
            diagnostic = CreateDiagnostic("record-byte-budget", 0, 1);
            return EbinWriteStatus.InvalidRecord;
        }

        if (storageBytes > int.MaxValue)
        {
            diagnostic = CreateDiagnostic(
                "record-byte-budget",
                checked((ulong)storageBytes),
                checked((ulong)Math.Min(int.MaxValue, limits.MaximumRecordBytes)));
            return EbinWriteStatus.InvalidRecord;
        }

        if (index >= slices.Length)
        {
            throw new InvalidOperationException("The State2 row cursor exceeds its exact slice table.");
        }

        var nextStorageBytes = checked(storageBytes + recordBytes);

        if (nextStorageBytes > int.MaxValue)
        {
            diagnostic = CreateDiagnostic(
                "record-byte-budget",
                checked((ulong)nextStorageBytes),
                checked((ulong)Math.Min(int.MaxValue, limits.MaximumRecordBytes)));
            return EbinWriteStatus.InvalidRecord;
        }

        slices[index] = new EbinPayloadSlice(checked((int)storageBytes), recordBytes);
        storageBytes = nextStorageBytes;
        return EbinWriteStatus.Complete;
    }

    private static EbinStructuralDiagnostic CreateDiagnostic(string category, ulong claim, ulong limit)
        => new EbinStructuralDiagnostic(
            Guid.Empty,
            0,
            category,
            null,
            claim,
            limit,
            EbinStructuralDiagnosticSource.Construction);

    private static EbinWriteStatus GetPayloadRecordBytes(
        nint byteCount,
        string diagnosticCategory,
        EbinReadLimits limits,
        out int payloadRecordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        payloadRecordBytes = 0;
        diagnostic = null;
        var recordBytes = checked((long)byteCount);

        if (recordBytes <= 0)
        {
            diagnostic = CreateDiagnostic(diagnosticCategory, 0, 1);
            return EbinWriteStatus.InvalidRecord;
        }

        if (recordBytes > limits.MaximumRecordBytes)
        {
            diagnostic = CreateDiagnostic(
                "record-byte-budget",
                checked((ulong)recordBytes),
                checked((ulong)limits.MaximumRecordBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        payloadRecordBytes = checked((int)recordBytes);
        return EbinWriteStatus.Complete;
    }

    private static long GetRecordBytes(
        int entityArrayPayloadBytes,
        int systemArrayPayloadBytes,
        int resourceArrayPayloadBytes,
        int attachmentArrayPayloadBytes,
        int identityHistoryRecordBytes)
    {
        var recordBytes = Align16(64L + (5L * 32L));
        recordBytes = checked(Align16(recordBytes) + entityArrayPayloadBytes);
        recordBytes = checked(Align16(recordBytes) + systemArrayPayloadBytes);
        recordBytes = checked(Align16(recordBytes) + resourceArrayPayloadBytes);
        recordBytes = checked(Align16(recordBytes) + attachmentArrayPayloadBytes);
        recordBytes = checked(Align16(recordBytes) + identityHistoryRecordBytes);
        return Align16(recordBytes);
    }

    private static EbinWriteStatus MeasureAttachmentRecords(
        WorldPersistenceCaptureLease lease,
        ref WorldPersistenceEcsBuffers buffers,
        EbinReadLimits limits,
        CancellationToken cancellationToken,
        ref int statePayloadRecordStorageBytes,
        ref int stateRowPayloadStorageBytes,
        ref int stateRowCandidateStorageBytes,
        out int storageBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        storageBytes = 0;
        diagnostic = null;
        long totalStorageBytes = 0;
        var rows = lease.GetCanonicalAttachmentStateEnumerator(buffers.AttachmentRows);
        var index = 0;

        while (rows.MoveNext())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payloadStatus = GetPayloadRecordBytes(
                lease.CountAttachmentStateRecord(rows.CurrentIndex, cancellationToken),
                "attachment-state-record-size",
                limits,
                out var payloadRecordBytes,
                out diagnostic);

            if (payloadStatus is not EbinWriteStatus.Complete)
            {
                return payloadStatus;
            }

            var status = WorldPersistenceStateRowWriter.MeasureAttachment(
                rows.CurrentSchema,
                rows.CurrentOwnerEntityId.HasValue,
                payloadRecordBytes,
                limits,
                out _,
                out var rowRecordBytes,
                out var rowPayloadBytes,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            status = AppendRecordSlice(
                buffers.AttachmentSlices,
                index,
                rowRecordBytes,
                limits,
                ref totalStorageBytes,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            statePayloadRecordStorageBytes = Math.Max(statePayloadRecordStorageBytes, payloadRecordBytes);
            stateRowPayloadStorageBytes = Math.Max(stateRowPayloadStorageBytes, rowPayloadBytes);
            stateRowCandidateStorageBytes = Math.Max(stateRowCandidateStorageBytes, rowRecordBytes);
            index++;
        }

        if (index != buffers.AttachmentRows.Length)
        {
            throw new InvalidOperationException("The canonical attachment cursor count changed during workspace measurement.");
        }

        storageBytes = checked((int)totalStorageBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus MeasureEntityRecords(
        WorldPersistenceCaptureLease lease,
        ref WorldPersistenceEcsBuffers buffers,
        EbinReadLimits limits,
        CancellationToken cancellationToken,
        out int storageBytes,
        out int componentStorageBytes,
        out int tagStorageBytes,
        out int transientSchemaStorageBytes,
        out int childCandidateBytes,
        out int payloadStorageBytes,
        out int candidateStorageBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        storageBytes = 0;
        componentStorageBytes = 0;
        tagStorageBytes = 0;
        transientSchemaStorageBytes = 0;
        childCandidateBytes = 0;
        payloadStorageBytes = 0;
        candidateStorageBytes = 0;
        diagnostic = null;
        long totalStorageBytes = 0;
        var entities = lease.GetCanonicalEntityStateEnumerator(buffers.EntityRows);
        var index = 0;

        while (entities.MoveNext())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var componentCount = entities.CurrentPersistentComponentCount;
            var tagCount = entities.CurrentTagCount;
            var transientSchemaCount = entities.CurrentTransientSchemaCount;

            if ((componentCount > WorldPersistenceEcsWorkspaceRequirements.MaximumEntitySchemaRecordCount)
                || (tagCount > WorldPersistenceEcsWorkspaceRequirements.MaximumEntitySchemaRecordCount)
                || (transientSchemaCount > WorldPersistenceEcsWorkspaceRequirements.MaximumEntitySchemaRecordCount))
            {
                diagnostic = CreateDiagnostic(
                    "array-count-budget",
                    checked((ulong)Math.Max(componentCount, Math.Max(tagCount, transientSchemaCount))),
                    WorldPersistenceEcsWorkspaceRequirements.MaximumEntitySchemaRecordCount);
                return EbinWriteStatus.InvalidRecord;
            }

            var status = lease.MeasureEntityRecord(
                in entities,
                buffers.EntityComponentSlices[..componentCount],
                buffers.EntityTagSlices[..tagCount],
                buffers.EntityTransientSchemaSlices[..transientSchemaCount],
                out var entityWorkspace,
                out diagnostic,
                cancellationToken);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            status = AppendRecordSlice(
                buffers.EntitySlices,
                index,
                entityWorkspace.RecordBytes,
                limits,
                ref totalStorageBytes,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            componentStorageBytes = Math.Max(componentStorageBytes, entityWorkspace.ComponentStorageBytes);
            tagStorageBytes = Math.Max(tagStorageBytes, entityWorkspace.TagStorageBytes);
            transientSchemaStorageBytes = Math.Max(transientSchemaStorageBytes, entityWorkspace.TransientSchemaStorageBytes);
            childCandidateBytes = Math.Max(childCandidateBytes, entityWorkspace.ChildCandidateBytes);
            payloadStorageBytes = Math.Max(payloadStorageBytes, entityWorkspace.PayloadStorageBytes);
            candidateStorageBytes = Math.Max(candidateStorageBytes, entityWorkspace.RecordBytes);
            index++;
        }

        if (index != buffers.EntityRows.Length)
        {
            throw new InvalidOperationException("The canonical Entity cursor count changed during workspace measurement.");
        }

        storageBytes = checked((int)totalStorageBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus MeasureResourceRecords(
        WorldPersistenceCaptureLease lease,
        scoped Span<EbinPayloadSlice> slices,
        EbinReadLimits limits,
        CancellationToken cancellationToken,
        ref int statePayloadRecordStorageBytes,
        ref int stateRowPayloadStorageBytes,
        ref int stateRowCandidateStorageBytes,
        out int storageBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        storageBytes = 0;
        diagnostic = null;
        long totalStorageBytes = 0;
        var rows = lease.GetResourceStateEnumerator();
        var index = 0;

        while (rows.MoveNext())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payloadStatus = GetPayloadRecordBytes(
                lease.CountResourceStateRecord(rows.CurrentIndex, cancellationToken),
                "resource-state-record-size",
                limits,
                out var payloadRecordBytes,
                out diagnostic);

            if (payloadStatus is not EbinWriteStatus.Complete)
            {
                return payloadStatus;
            }

            var status = WorldPersistenceStateRowWriter.MeasureResource(
                payloadRecordBytes,
                limits,
                out var rowRecordBytes,
                out var rowPayloadBytes,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            status = AppendRecordSlice(slices, index, rowRecordBytes, limits, ref totalStorageBytes, out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            statePayloadRecordStorageBytes = Math.Max(statePayloadRecordStorageBytes, payloadRecordBytes);
            stateRowPayloadStorageBytes = Math.Max(stateRowPayloadStorageBytes, rowPayloadBytes);
            stateRowCandidateStorageBytes = Math.Max(stateRowCandidateStorageBytes, rowRecordBytes);
            index++;
        }

        if (index != slices.Length)
        {
            throw new InvalidOperationException("The resource-state cursor count changed during workspace measurement.");
        }

        storageBytes = checked((int)totalStorageBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus MeasureSystemRecords(
        WorldPersistenceCaptureLease lease,
        scoped Span<EbinPayloadSlice> slices,
        EbinReadLimits limits,
        CancellationToken cancellationToken,
        out int storageBytes,
        out int statePayloadRecordStorageBytes,
        out int stateRowPayloadStorageBytes,
        out int stateRowCandidateStorageBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        storageBytes = 0;
        statePayloadRecordStorageBytes = 0;
        stateRowPayloadStorageBytes = 0;
        stateRowCandidateStorageBytes = 0;
        diagnostic = null;
        long totalStorageBytes = 0;
        var rows = lease.GetSystemStateEnumerator();
        var index = 0;

        while (rows.MoveNext())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (rows.CurrentSystemId == Guid.Empty)
            {
                throw new InvalidOperationException("A stateful system registration has an empty SystemId.");
            }

            var payloadStatus = GetPayloadRecordBytes(
                lease.CountSystemStateRecord(rows.CurrentIndex, cancellationToken),
                "system-state-record-size",
                limits,
                out var payloadRecordBytes,
                out diagnostic);

            if (payloadStatus is not EbinWriteStatus.Complete)
            {
                return payloadStatus;
            }

            var status = WorldPersistenceStateRowWriter.MeasureSystem(
                payloadRecordBytes,
                limits,
                out var rowRecordBytes,
                out var rowPayloadBytes,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            status = AppendRecordSlice(slices, index, rowRecordBytes, limits, ref totalStorageBytes, out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            statePayloadRecordStorageBytes = Math.Max(statePayloadRecordStorageBytes, payloadRecordBytes);
            stateRowPayloadStorageBytes = Math.Max(stateRowPayloadStorageBytes, rowPayloadBytes);
            stateRowCandidateStorageBytes = Math.Max(stateRowCandidateStorageBytes, rowRecordBytes);
            index++;
        }

        if (index != slices.Length)
        {
            throw new InvalidOperationException("The system-state cursor count changed during workspace measurement.");
        }

        storageBytes = checked((int)totalStorageBytes);
        return EbinWriteStatus.Complete;
    }

    private static void ValidateMeasurementBuffers(
        WorldPersistenceEcsBuffers buffers,
        int entityCount,
        int systemCount,
        int resourceCount,
        int attachmentCount,
        in World.IdentityHistoryCapture history)
    {
        if ((buffers.EntityRows.Length != entityCount)
            || (buffers.EntitySlices.Length != entityCount)
            || (buffers.SystemSlices.Length != systemCount)
            || (buffers.ResourceSlices.Length != resourceCount)
            || (buffers.AttachmentRows.Length != attachmentCount)
            || (buffers.AttachmentSlices.Length != attachmentCount)
            || (buffers.EntityComponentSlices.Length != WorldPersistenceEcsWorkspaceRequirements.MaximumEntitySchemaRecordCount)
            || (buffers.EntityTagSlices.Length != WorldPersistenceEcsWorkspaceRequirements.MaximumEntitySchemaRecordCount)
            || (buffers.EntityTransientSchemaSlices.Length != WorldPersistenceEcsWorkspaceRequirements.MaximumEntitySchemaRecordCount))
        {
            throw new ArgumentException("The State2 ECS row and slice workspaces must match the exact capture counts.");
        }

        if ((buffers.IdentityBranchSlices.Length != history.BranchCount)
            || (buffers.IdentityPageSlices.Length != history.PageCount))
        {
            throw new ArgumentException("The identity-history slice tables must match the exact capture counts.");
        }
    }

    private static void ValidateWriteBuffers(
        WorldPersistenceEcsBuffers buffers,
        WorldPersistenceEcsWorkspaceRequirements workspace)
    {
        var identityWorkspace = workspace.IdentityHistory;
        var expectedPageWorkspace = (identityWorkspace.PageCount == 0)
            ? 0
            : WorldPersistenceIdentityPageWriter.GetMinimumPayloadWorkspaceBytes(StableIdentityLedger.RecordsPerPage);
        var expectedPageRecordWorkspace = (identityWorkspace.PageCount == 0)
            ? 0
            : StableIdentityLedger.RecordsPerPage;

        if ((buffers.EntityStorage.Length != workspace.EntityStorageBytes)
            || (buffers.SystemStorage.Length != workspace.SystemStorageBytes)
            || (buffers.ResourceStorage.Length != workspace.ResourceStorageBytes)
            || (buffers.AttachmentStorage.Length != workspace.AttachmentStorageBytes)
            || (buffers.EntityComponentStorage.Length != workspace.EntityComponentStorageBytes)
            || (buffers.EntityTagStorage.Length != workspace.EntityTagStorageBytes)
            || (buffers.EntityTransientSchemaStorage.Length != workspace.EntityTransientSchemaStorageBytes)
            || (buffers.EntityChildCandidateStorage.Length != workspace.EntityChildCandidateBytes)
            || (buffers.EntityPayloadStorage.Length != workspace.EntityPayloadStorageBytes)
            || (buffers.EntityCandidateStorage.Length != workspace.EntityCandidateStorageBytes)
            || (buffers.StatePayloadRecordStorage.Length != workspace.StatePayloadRecordStorageBytes)
            || (buffers.StateRowPayloadStorage.Length != workspace.StateRowPayloadStorageBytes)
            || (buffers.StateRowCandidateStorage.Length != workspace.StateRowCandidateStorageBytes)
            || (buffers.IdentityBranchStorage.Length != identityWorkspace.BranchStorageBytes)
            || (buffers.IdentityPageStorage.Length != identityWorkspace.PageStorageBytes)
            || (buffers.IdentityPageStableIds.Length != expectedPageRecordWorkspace)
            || (buffers.IdentityPageMetadata.Length != expectedPageRecordWorkspace)
            || (buffers.IdentityPagePayloadStorage.Length != expectedPageWorkspace)
            || (buffers.IdentityChildCandidateStorage.Length != identityWorkspace.ChildCandidateBytes)
            || (buffers.IdentityRootPayloadStorage.Length != identityWorkspace.RootPayloadStorageBytes)
            || (buffers.IdentityCandidateStorage.Length != identityWorkspace.RecordBytes)
            || (buffers.RootPayloadStorage.Length != workspace.RootPayloadStorageBytes)
            || (buffers.EcsCandidateStorage.Length != workspace.RecordBytes)
            || (buffers.Destination.Length != workspace.RecordBytes))
        {
            throw new ArgumentException("The State2 ECS byte workspaces must match the exact measured lengths.");
        }

        if (buffers.GetWorkspaceBytes() != workspace.WorkspaceBytes)
        {
            throw new InvalidOperationException("The State2 ECS workspace accounting changed after measurement.");
        }
    }

    private static EbinWriteStatus WriteAttachmentRecords(
        WorldPersistenceCaptureLease lease,
        ref WorldPersistenceEcsBuffers buffers,
        EbinReadLimits limits,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        diagnostic = null;
        var rows = lease.GetCanonicalAttachmentStateEnumerator(buffers.AttachmentRows);
        var index = 0;

        while (rows.MoveNext())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payloadStatus = GetPayloadRecordBytes(
                lease.CountAttachmentStateRecord(rows.CurrentIndex, cancellationToken),
                "attachment-state-record-size",
                limits,
                out var measuredPayloadRecordBytes,
                out diagnostic);

            if (payloadStatus is not EbinWriteStatus.Complete)
            {
                return payloadStatus;
            }

            var status = WorldPersistenceStateRowWriter.MeasureAttachment(
                rows.CurrentSchema,
                rows.CurrentOwnerEntityId.HasValue,
                measuredPayloadRecordBytes,
                limits,
                out _,
                out var rowRecordBytes,
                out var rowPayloadBytes,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            var rowSlice = buffers.AttachmentSlices[index];

            if (rowSlice.ByteLength != rowRecordBytes)
            {
                throw new InvalidOperationException("The attachment-state record extent changed after workspace measurement.");
            }

            status = WriteMeasuredAttachmentRecord(
                lease,
                rows.CurrentIndex,
                rows.CurrentSchema,
                rows.CurrentOwnerEntityId,
                measuredPayloadRecordBytes,
                rowPayloadBytes,
                rowRecordBytes,
                limits,
                buffers.StatePayloadRecordStorage,
                buffers.StateRowPayloadStorage,
                buffers.StateRowCandidateStorage,
                buffers.AttachmentStorage.Slice(rowSlice.Offset, rowSlice.ByteLength),
                out diagnostic,
                cancellationToken);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            index++;
        }

        if (index != buffers.AttachmentSlices.Length)
        {
            throw new InvalidOperationException("The canonical attachment cursor count changed during serialization.");
        }

        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus WriteEntityRecords(
        WorldPersistenceCaptureLease lease,
        ref WorldPersistenceEcsBuffers buffers,
        EbinReadLimits limits,
        WorldPersistenceEcsWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        diagnostic = null;
        var entities = lease.GetCanonicalEntityStateEnumerator(buffers.EntityRows);
        var index = 0;

        while (entities.MoveNext())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var componentCount = entities.CurrentPersistentComponentCount;
            var tagCount = entities.CurrentTagCount;
            var transientSchemaCount = entities.CurrentTransientSchemaCount;
            var status = lease.MeasureEntityRecord(
                in entities,
                buffers.EntityComponentSlices[..componentCount],
                buffers.EntityTagSlices[..tagCount],
                buffers.EntityTransientSchemaSlices[..transientSchemaCount],
                out var entityWorkspace,
                out diagnostic,
                cancellationToken);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            var entitySlice = buffers.EntitySlices[index];

            if (entitySlice.ByteLength != entityWorkspace.RecordBytes)
            {
                throw new InvalidOperationException("An Entity record extent changed after ECS workspace measurement.");
            }

            status = WorldPersistenceEntityWriter.WriteMeasured(
                lease,
                in entities,
                limits,
                in entityWorkspace,
                buffers.EntityComponentSlices[..componentCount],
                buffers.EntityComponentStorage[..entityWorkspace.ComponentStorageBytes],
                buffers.EntityTagSlices[..tagCount],
                buffers.EntityTagStorage[..entityWorkspace.TagStorageBytes],
                buffers.EntityTransientSchemaSlices[..transientSchemaCount],
                buffers.EntityTransientSchemaStorage[..entityWorkspace.TransientSchemaStorageBytes],
                buffers.EntityChildCandidateStorage[..entityWorkspace.ChildCandidateBytes],
                buffers.EntityPayloadStorage[..entityWorkspace.PayloadStorageBytes],
                buffers.EntityCandidateStorage[..entityWorkspace.RecordBytes],
                buffers.EntityStorage.Slice(entitySlice.Offset, entitySlice.ByteLength),
                out var bytesWritten,
                out diagnostic,
                cancellationToken);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            if (bytesWritten != entitySlice.ByteLength)
            {
                throw new InvalidOperationException("An Entity record writer changed its measured extent.");
            }

            index++;
        }

        if (index != workspace.EntityCount)
        {
            throw new InvalidOperationException("The canonical Entity cursor count changed during serialization.");
        }

        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus WriteMeasuredAttachmentRecord(
        WorldPersistenceCaptureLease lease,
        int attachmentIndex,
        ContentSchemaReference schema,
        Guid? ownerEntityId,
        int payloadRecordBytes,
        int rowPayloadBytes,
        int rowRecordBytes,
        EbinReadLimits limits,
        scoped Span<byte> statePayloadRecordStorage,
        scoped Span<byte> stateRowPayloadStorage,
        scoped Span<byte> stateRowCandidateStorage,
        scoped Span<byte> destination,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        var bytesWritten = lease.WriteAttachmentStateRecord(
            attachmentIndex,
            statePayloadRecordStorage[..payloadRecordBytes],
            cancellationToken);

        if (bytesWritten != payloadRecordBytes)
        {
            throw new InvalidOperationException("An attachment state writer changed its measured record extent.");
        }

        var status = WorldPersistenceStateRowWriter.WriteAttachment(
            schema,
            ownerEntityId,
            statePayloadRecordStorage[..payloadRecordBytes],
            limits,
            stateRowPayloadStorage[..rowPayloadBytes],
            stateRowCandidateStorage[..rowRecordBytes],
            destination,
            out var rowBytesWritten,
            out diagnostic);

        if (status is EbinWriteStatus.Complete && (rowBytesWritten != rowRecordBytes))
        {
            throw new InvalidOperationException("An attachment-state row writer changed its measured extent.");
        }

        return status;
    }

    private static EbinWriteStatus WriteMeasuredResourceRecord(
        WorldPersistenceCaptureLease lease,
        int resourceIndex,
        Guid resourceId,
        ContentSchemaReference payloadSchema,
        int payloadRecordBytes,
        int rowPayloadBytes,
        int rowRecordBytes,
        EbinReadLimits limits,
        scoped Span<byte> statePayloadRecordStorage,
        scoped Span<byte> stateRowPayloadStorage,
        scoped Span<byte> stateRowCandidateStorage,
        scoped Span<byte> destination,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        var bytesWritten = lease.WriteResourceStateRecord(
            resourceIndex,
            statePayloadRecordStorage[..payloadRecordBytes],
            cancellationToken);

        if (bytesWritten != payloadRecordBytes)
        {
            throw new InvalidOperationException("A resource state writer changed its measured record extent.");
        }

        var status = WorldPersistenceStateRowWriter.WriteResource(
            resourceId,
            payloadSchema,
            statePayloadRecordStorage[..payloadRecordBytes],
            limits,
            stateRowPayloadStorage[..rowPayloadBytes],
            stateRowCandidateStorage[..rowRecordBytes],
            destination,
            out var rowBytesWritten,
            out diagnostic);

        if (status is EbinWriteStatus.Complete && (rowBytesWritten != rowRecordBytes))
        {
            throw new InvalidOperationException("A resource-state row writer changed its measured extent.");
        }

        return status;
    }

    private static EbinWriteStatus WriteMeasuredSystemRecord(
        WorldPersistenceCaptureLease lease,
        int systemIndex,
        Guid systemId,
        ContentSchemaReference payloadSchema,
        int payloadRecordBytes,
        int rowPayloadBytes,
        int rowRecordBytes,
        EbinReadLimits limits,
        scoped Span<byte> statePayloadRecordStorage,
        scoped Span<byte> stateRowPayloadStorage,
        scoped Span<byte> stateRowCandidateStorage,
        scoped Span<byte> destination,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        var bytesWritten = lease.WriteSystemStateRecord(
            systemIndex,
            statePayloadRecordStorage[..payloadRecordBytes],
            cancellationToken);

        if (bytesWritten != payloadRecordBytes)
        {
            throw new InvalidOperationException("A system state writer changed its measured record extent.");
        }

        var status = WorldPersistenceStateRowWriter.WriteSystem(
            systemId,
            payloadSchema,
            statePayloadRecordStorage[..payloadRecordBytes],
            limits,
            stateRowPayloadStorage[..rowPayloadBytes],
            stateRowCandidateStorage[..rowRecordBytes],
            destination,
            out var rowBytesWritten,
            out diagnostic);

        if (status is EbinWriteStatus.Complete && (rowBytesWritten != rowRecordBytes))
        {
            throw new InvalidOperationException("A system-state row writer changed its measured extent.");
        }

        return status;
    }

    private static EbinWriteStatus WriteResourceRecords(
        WorldPersistenceCaptureLease lease,
        scoped ReadOnlySpan<EbinPayloadSlice> slices,
        scoped Span<byte> storage,
        scoped Span<byte> statePayloadRecordStorage,
        scoped Span<byte> stateRowPayloadStorage,
        scoped Span<byte> stateRowCandidateStorage,
        EbinReadLimits limits,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        diagnostic = null;
        var rows = lease.GetResourceStateEnumerator();
        var index = 0;

        while (rows.MoveNext())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payloadStatus = GetPayloadRecordBytes(
                lease.CountResourceStateRecord(rows.CurrentIndex, cancellationToken),
                "resource-state-record-size",
                limits,
                out var payloadRecordBytes,
                out diagnostic);

            if (payloadStatus is not EbinWriteStatus.Complete)
            {
                return payloadStatus;
            }

            var status = WorldPersistenceStateRowWriter.MeasureResource(
                payloadRecordBytes,
                limits,
                out var rowRecordBytes,
                out var rowPayloadBytes,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            var slice = slices[index];

            if (slice.ByteLength != rowRecordBytes)
            {
                throw new InvalidOperationException("A resource-state record extent changed after ECS workspace measurement.");
            }

            status = WriteMeasuredResourceRecord(
                lease,
                rows.CurrentIndex,
                rows.CurrentResourceId,
                rows.CurrentSchema,
                payloadRecordBytes,
                rowPayloadBytes,
                rowRecordBytes,
                limits,
                statePayloadRecordStorage,
                stateRowPayloadStorage,
                stateRowCandidateStorage,
                storage.Slice(slice.Offset, slice.ByteLength),
                out diagnostic,
                cancellationToken);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            index++;
        }

        if (index != slices.Length)
        {
            throw new InvalidOperationException("The resource-state cursor count changed during serialization.");
        }

        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus WriteSystemRecords(
        WorldPersistenceCaptureLease lease,
        scoped ReadOnlySpan<EbinPayloadSlice> slices,
        scoped Span<byte> storage,
        scoped Span<byte> statePayloadRecordStorage,
        scoped Span<byte> stateRowPayloadStorage,
        scoped Span<byte> stateRowCandidateStorage,
        EbinReadLimits limits,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        diagnostic = null;
        var rows = lease.GetSystemStateEnumerator();
        var index = 0;

        while (rows.MoveNext())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payloadStatus = GetPayloadRecordBytes(
                lease.CountSystemStateRecord(rows.CurrentIndex, cancellationToken),
                "system-state-record-size",
                limits,
                out var payloadRecordBytes,
                out diagnostic);

            if (payloadStatus is not EbinWriteStatus.Complete)
            {
                return payloadStatus;
            }

            var status = WorldPersistenceStateRowWriter.MeasureSystem(
                payloadRecordBytes,
                limits,
                out var rowRecordBytes,
                out var rowPayloadBytes,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            var slice = slices[index];

            if (slice.ByteLength != rowRecordBytes)
            {
                throw new InvalidOperationException("A system-state record extent changed after ECS workspace measurement.");
            }

            status = WriteMeasuredSystemRecord(
                lease,
                rows.CurrentIndex,
                rows.CurrentSystemId,
                rows.CurrentSchema,
                payloadRecordBytes,
                rowPayloadBytes,
                rowRecordBytes,
                limits,
                statePayloadRecordStorage,
                stateRowPayloadStorage,
                stateRowCandidateStorage,
                storage.Slice(slice.Offset, slice.ByteLength),
                out diagnostic,
                cancellationToken);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            index++;
        }

        if (index != slices.Length)
        {
            throw new InvalidOperationException("The system-state cursor count changed during serialization.");
        }

        diagnostic = null;
        return EbinWriteStatus.Complete;
    }
}
