// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceEntityWriter
{
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("state/entity").SchemaId;

    internal static EbinWriteStatus Measure(
        WorldPersistenceCaptureLease lease,
        in World.EntityStateCaptureEnumerator entity,
        EbinReadLimits limits,
        scoped Span<EbinPayloadSlice> componentSlices,
        scoped Span<EbinPayloadSlice> tagSlices,
        scoped Span<EbinPayloadSlice> transientSchemaSlices,
        out WorldPersistenceEntityWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
        => MeasureWorkspace(
            lease,
            in entity,
            limits,
            componentSlices,
            tagSlices,
            transientSchemaSlices,
            out workspace,
            out diagnostic,
            cancellationToken);

    internal static EbinWriteStatus MeasureWorkspace(
        WorldPersistenceCaptureLease lease,
        in World.EntityStateCaptureEnumerator entity,
        EbinReadLimits limits,
        scoped Span<EbinPayloadSlice> componentSlices,
        scoped Span<EbinPayloadSlice> tagSlices,
        scoped Span<EbinPayloadSlice> transientSchemaSlices,
        out WorldPersistenceEntityWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        workspace = default;
        diagnostic = null;
        var entityId = entity.CurrentEntityId;
        var assignmentOrdinal = entity.CurrentAssignmentOrdinal;
        var sceneInstanceId = entity.CurrentSceneInstanceId;
        var componentCount = entity.CurrentPersistentComponentCount;
        var tagCount = entity.CurrentTagCount;
        var transientSchemaCount = entity.CurrentTransientSchemaCount;

        if ((entityId == Guid.Empty) || (assignmentOrdinal == 0))
        {
            diagnostic = CreateDiagnostic("entity-row-identity", 0, 1);
            return EbinWriteStatus.InvalidRecord;
        }

        if ((componentCount > 256) || (tagCount > 256) || (transientSchemaCount > 256))
        {
            diagnostic = CreateDiagnostic(
                "array-count-budget",
                checked((ulong)Math.Max(componentCount, Math.Max(tagCount, transientSchemaCount))),
                256);
            return EbinWriteStatus.InvalidRecord;
        }

        if (sceneInstanceId == Guid.Empty)
        {
            diagnostic = CreateDiagnostic("empty-scene-instance-id", 0, 1);
            return EbinWriteStatus.InvalidRecord;
        }

        if (componentSlices.Length != componentCount)
        {
            throw new ArgumentException("The component-slice table must match the exact persistent component count.", nameof(componentSlices));
        }

        if (tagSlices.Length != tagCount)
        {
            throw new ArgumentException("The tag-slice table must match the exact EntityTag count.", nameof(tagSlices));
        }

        if (transientSchemaSlices.Length != transientSchemaCount)
        {
            throw new ArgumentException("The transient-schema slice table must match the exact transient schema count.", nameof(transientSchemaSlices));
        }

        if (limits.MaximumFieldsPerRecord < (sceneInstanceId.HasValue ? 6 : 5))
        {
            diagnostic = CreateDiagnostic(
                "field-count-budget",
                checked((ulong)(sceneInstanceId.HasValue ? 6 : 5)),
                checked((ulong)limits.MaximumFieldsPerRecord));
            return EbinWriteStatus.InvalidRecord;
        }

        var currentEntity = entity.CurrentEntity;
        var previousSchema = default(ContentSchemaReference);
        var hasPreviousSchema = false;
        long componentStorageBytes = 0;
        var childCandidateBytes = 0;

        for (var index = 0; index < componentCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var schema = entity.GetPersistentComponentSchema(index);

            if (hasPreviousSchema && (CompareSchemaReferences(previousSchema, schema) >= 0))
            {
                throw new InvalidOperationException("Persistent component rows are not in canonical schema-header order.");
            }

            var byteCount = lease.CountComponentStateRecord(currentEntity, schema, cancellationToken);

            if (byteCount <= 0)
            {
                diagnostic = CreateDiagnostic("component-state-record-size", 0, 1);
                return EbinWriteStatus.InvalidRecord;
            }

            var componentRecordBytes = checked((long)byteCount);

            if (componentRecordBytes > limits.MaximumRecordBytes)
            {
                diagnostic = CreateDiagnostic(
                    "record-byte-budget",
                    checked((ulong)componentRecordBytes),
                    checked((ulong)limits.MaximumRecordBytes));
                return EbinWriteStatus.InvalidRecord;
            }

            componentSlices[index] = new EbinPayloadSlice(checked((int)componentStorageBytes), checked((int)componentRecordBytes));
            componentStorageBytes = checked(componentStorageBytes + componentRecordBytes);
            previousSchema = schema;
            hasPreviousSchema = true;
        }

        previousSchema = default;
        hasPreviousSchema = false;
        long tagStorageBytes = 0;

        for (var index = 0; index < tagCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var schema = entity.GetTagSchema(index);

            if (hasPreviousSchema && (CompareSchemaReferences(previousSchema, schema) >= 0))
            {
                throw new InvalidOperationException("EntityTag membership is not in canonical schema-header order.");
            }

            var tagStatus = MeasureTagRecord(schema, limits, out var tagRecordBytes, out diagnostic);

            if (tagStatus is not EbinWriteStatus.Complete)
            {
                return tagStatus;
            }

            tagSlices[index] = new EbinPayloadSlice(checked((int)tagStorageBytes), tagRecordBytes);
            tagStorageBytes = checked(tagStorageBytes + tagRecordBytes);
            childCandidateBytes = Math.Max(childCandidateBytes, tagRecordBytes);
            previousSchema = schema;
            hasPreviousSchema = true;
        }

        previousSchema = default;
        hasPreviousSchema = false;
        long transientSchemaStorageBytes = 0;

        for (var index = 0; index < transientSchemaCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var schema = entity.GetTransientComponentSchema(index);

            if (hasPreviousSchema && (CompareSchemaReferences(previousSchema, schema) >= 0))
            {
                throw new InvalidOperationException("Transient component schemas are not in canonical schema-tuple order.");
            }

            var schemaStatus = WorldPersistenceSchemaReferenceWriter.Measure(schema, limits, out var transientRecordBytes, out diagnostic);

            if (schemaStatus is not EbinWriteStatus.Complete)
            {
                return schemaStatus;
            }

            transientSchemaSlices[index] = new EbinPayloadSlice(checked((int)transientSchemaStorageBytes), transientRecordBytes);
            transientSchemaStorageBytes = checked(transientSchemaStorageBytes + transientRecordBytes);
            childCandidateBytes = Math.Max(childCandidateBytes, transientRecordBytes);
            previousSchema = schema;
            hasPreviousSchema = true;
        }

        var status = WorldPersistenceRecordArrayLayout.MeasurePayloadBytes(
            componentCount,
            componentStorageBytes,
            limits,
            out var componentArrayPayloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = WorldPersistenceRecordArrayLayout.MeasurePayloadBytes(
            tagCount,
            tagStorageBytes,
            limits,
            out var tagArrayPayloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = WorldPersistenceRecordArrayLayout.MeasurePayloadBytes(
            transientSchemaCount,
            transientSchemaStorageBytes,
            limits,
            out var transientSchemaArrayPayloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var hasSceneInstance = sceneInstanceId.HasValue;
        var rootPayloadStorageBytes = checked(
            16
            + componentArrayPayloadBytes
            + tagArrayPayloadBytes
            + transientSchemaArrayPayloadBytes
            + sizeof(long)
            + (hasSceneInstance ? 16 : 0));
        var recordBytes = GetRecordBytes(
            componentArrayPayloadBytes,
            tagArrayPayloadBytes,
            transientSchemaArrayPayloadBytes,
            hasSceneInstance);

        if (recordBytes > limits.MaximumRecordBytes)
        {
            diagnostic = CreateDiagnostic(
                "record-byte-budget",
                checked((ulong)recordBytes),
                checked((ulong)limits.MaximumRecordBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        workspace = new WorldPersistenceEntityWorkspaceRequirements(
            componentCount,
            tagCount,
            transientSchemaCount,
            hasSceneInstance,
            checked((int)componentStorageBytes),
            checked((int)tagStorageBytes),
            checked((int)transientSchemaStorageBytes),
            componentArrayPayloadBytes,
            tagArrayPayloadBytes,
            transientSchemaArrayPayloadBytes,
            childCandidateBytes,
            rootPayloadStorageBytes,
            recordBytes);
        return EbinWriteStatus.Complete;
    }

    internal static EbinWriteStatus Write(
        WorldPersistenceCaptureLease lease,
        in World.EntityStateCaptureEnumerator entity,
        EbinReadLimits limits,
        scoped Span<EbinPayloadSlice> componentSlices,
        scoped Span<byte> componentStorage,
        scoped Span<EbinPayloadSlice> tagSlices,
        scoped Span<byte> tagStorage,
        scoped Span<EbinPayloadSlice> transientSchemaSlices,
        scoped Span<byte> transientSchemaStorage,
        scoped Span<byte> childCandidateStorage,
        scoped Span<byte> payloadStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        var status = MeasureWorkspace(
            lease,
            in entity,
            limits,
            componentSlices,
            tagSlices,
            transientSchemaSlices,
            out var workspace,
            out diagnostic,
            cancellationToken);
        bytesWritten = 0;

        if (status is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return status;
        }

        return WriteMeasured(
            lease,
            in entity,
            limits,
            in workspace,
            componentSlices,
            componentStorage,
            tagSlices,
            tagStorage,
            transientSchemaSlices,
            transientSchemaStorage,
            childCandidateStorage,
            payloadStorage,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic,
            cancellationToken);
    }

    internal static EbinWriteStatus WriteMeasured(
        WorldPersistenceCaptureLease lease,
        in World.EntityStateCaptureEnumerator entity,
        EbinReadLimits limits,
        in WorldPersistenceEntityWorkspaceRequirements workspace,
        scoped ReadOnlySpan<EbinPayloadSlice> componentSlices,
        scoped Span<byte> componentStorage,
        scoped ReadOnlySpan<EbinPayloadSlice> tagSlices,
        scoped Span<byte> tagStorage,
        scoped ReadOnlySpan<EbinPayloadSlice> transientSchemaSlices,
        scoped Span<byte> transientSchemaStorage,
        scoped Span<byte> childCandidateStorage,
        scoped Span<byte> payloadStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        diagnostic = null;
        bytesWritten = 0;

        if ((componentStorage.Length != workspace.ComponentStorageBytes)
            || (tagStorage.Length != workspace.TagStorageBytes)
            || (transientSchemaStorage.Length != workspace.TransientSchemaStorageBytes)
            || (childCandidateStorage.Length != workspace.ChildCandidateBytes)
            || (payloadStorage.Length != workspace.PayloadStorageBytes)
            || (candidateStorage.Length != workspace.RecordBytes)
            || (destination.Length != workspace.RecordBytes))
        {
            throw new ArgumentException("The Entity record workspaces must match the exact measured lengths.");
        }

        if ((workspace.ComponentCount != entity.CurrentPersistentComponentCount)
            || (workspace.TagCount != entity.CurrentTagCount)
            || (workspace.TransientSchemaCount != entity.CurrentTransientSchemaCount)
            || (workspace.HasSceneInstance != entity.CurrentSceneInstanceId.HasValue))
        {
            throw new InvalidOperationException("The Entity capture changed after workspace measurement.");
        }

        var currentEntity = entity.CurrentEntity;
        var entityId = entity.CurrentEntityId;
        var assignmentOrdinal = entity.CurrentAssignmentOrdinal;
        var sceneInstanceId = entity.CurrentSceneInstanceId;
        var status = WriteComponentRecords(
            lease,
            currentEntity,
            in entity,
            componentSlices,
            componentStorage,
            out diagnostic,
            cancellationToken);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = WriteTagRecords(
            in entity,
            limits,
            tagSlices,
            tagStorage,
            childCandidateStorage,
            out diagnostic,
            cancellationToken);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = WriteSchemaReferenceRecords(
            in entity,
            limits,
            transientSchemaSlices,
            transientSchemaStorage,
            childCandidateStorage,
            out diagnostic,
            cancellationToken);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        payloadStorage.Clear();
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[6];
        var fieldCount = 0;
        var payloadBytes = 0;
        status = AddGuidField(1, entityId, isRequired: true, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var componentArrayOffset = payloadBytes;
        status = EbinPayloadWriter.WriteRecordArray(
            componentSlices,
            componentStorage,
            limits,
            payloadStorage.Slice(componentArrayOffset, workspace.ComponentArrayPayloadBytes),
            out var componentArrayBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (componentArrayBytes != workspace.ComponentArrayPayloadBytes)
        {
            throw new InvalidOperationException("The Entity component-array extent changed after workspace measurement.");
        }

        fields[fieldCount++] = new EbinFieldSource(
            2,
            24,
            1,
            checked((ulong)workspace.ComponentCount),
            new EbinPayloadSlice(componentArrayOffset, componentArrayBytes));
        payloadBytes = checked(payloadBytes + componentArrayBytes);
        var tagArrayOffset = payloadBytes;
        status = EbinPayloadWriter.WriteRecordArray(
            tagSlices,
            tagStorage,
            limits,
            payloadStorage.Slice(tagArrayOffset, workspace.TagArrayPayloadBytes),
            out var tagArrayBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (tagArrayBytes != workspace.TagArrayPayloadBytes)
        {
            throw new InvalidOperationException("The EntityTag array extent changed after workspace measurement.");
        }

        fields[fieldCount++] = new EbinFieldSource(
            3,
            24,
            1,
            checked((ulong)workspace.TagCount),
            new EbinPayloadSlice(tagArrayOffset, tagArrayBytes));
        payloadBytes = checked(payloadBytes + tagArrayBytes);
        var transientSchemaArrayOffset = payloadBytes;
        status = EbinPayloadWriter.WriteRecordArray(
            transientSchemaSlices,
            transientSchemaStorage,
            limits,
            payloadStorage.Slice(transientSchemaArrayOffset, workspace.TransientSchemaArrayPayloadBytes),
            out var transientSchemaArrayBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (transientSchemaArrayBytes != workspace.TransientSchemaArrayPayloadBytes)
        {
            throw new InvalidOperationException("The transient-schema array extent changed after workspace measurement.");
        }

        fields[fieldCount++] = new EbinFieldSource(
            4,
            24,
            1,
            checked((ulong)workspace.TransientSchemaCount),
            new EbinPayloadSlice(transientSchemaArrayOffset, transientSchemaArrayBytes));
        payloadBytes = checked(payloadBytes + transientSchemaArrayBytes);
        status = AddInt64Field(
            5,
            assignmentOrdinal,
            fields,
            ref fieldCount,
            payloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (sceneInstanceId is Guid sceneId)
        {
            status = AddGuidField(
                6,
                sceneId,
                isRequired: false,
                fields,
                ref fieldCount,
                payloadStorage,
                ref payloadBytes,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }
        }

        if (payloadBytes != workspace.PayloadStorageBytes)
        {
            throw new InvalidOperationException("The Entity record payload extent changed after workspace measurement.");
        }

        var source = new EbinRecordWriteView(
            s_schemaId,
            major: 1,
            minor: 0,
            fields[..fieldCount],
            payloadStorage[..payloadBytes]);
        status = EbinStructuralWriter.Measure(in source, limits, out var measuredRecordBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (measuredRecordBytes != workspace.RecordBytes)
        {
            diagnostic = CreateDiagnostic(
                "entity-record-size-mismatch",
                checked((ulong)measuredRecordBytes),
                checked((ulong)workspace.RecordBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        return EbinStructuralWriter.Write(
            in source,
            limits,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    private static EbinWriteStatus AddGuidField(
        uint tag,
        Guid value,
        bool isRequired,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadBytes..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(tag, 11, isRequired ? (ushort)1 : (ushort)0, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus AddInt64Field(
        uint tag,
        long value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadBytes..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(tag, 8, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static long Align16(long value)
        => checked((value + 15L) & ~15L);

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

    private static EbinStructuralDiagnostic CreateDiagnostic(string category, ulong claim, ulong limit)
        => new EbinStructuralDiagnostic(
            Guid.Empty,
            0,
            category,
            null,
            claim,
            limit,
            EbinStructuralDiagnosticSource.Construction);

    private static int GetRecordBytes(
        int componentArrayPayloadBytes,
        int tagArrayPayloadBytes,
        int transientSchemaArrayPayloadBytes,
        bool hasSceneInstance)
    {
        var fieldCount = hasSceneInstance ? 6 : 5;
        var recordOffset = Align16(64 + (fieldCount * 32));
        recordOffset = checked(Align16(recordOffset) + 16);
        recordOffset = checked(Align16(recordOffset) + componentArrayPayloadBytes);
        recordOffset = checked(Align16(recordOffset) + tagArrayPayloadBytes);
        recordOffset = checked(Align16(recordOffset) + transientSchemaArrayPayloadBytes);
        recordOffset = checked(Align16(recordOffset) + sizeof(long));

        if (hasSceneInstance)
        {
            recordOffset = checked(Align16(recordOffset) + 16);
        }

        return checked((int)Align16(recordOffset));
    }

    private static EbinWriteStatus MeasureTagRecord(
        ContentSchemaReference schema,
        EbinReadLimits limits,
        out int recordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var source = new EbinRecordWriteView(schema.SchemaId, schema.Major, schema.Minor, [], []);
        return EbinStructuralWriter.Measure(in source, limits, out recordBytes, out diagnostic);
    }

    private static EbinWriteStatus WriteComponentRecords(
        WorldPersistenceCaptureLease lease,
        Entity entityHandle,
        in World.EntityStateCaptureEnumerator entity,
        scoped ReadOnlySpan<EbinPayloadSlice> componentSlices,
        scoped Span<byte> componentStorage,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        for (var componentIndex = 0; componentIndex < componentSlices.Length; componentIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var schema = entity.GetPersistentComponentSchema(componentIndex);
            var slice = componentSlices[componentIndex];
            var bytesWritten = lease.WriteComponentStateRecord(
                entityHandle,
                schema,
                componentStorage.Slice(slice.Offset, slice.ByteLength),
                cancellationToken);

            if (bytesWritten != slice.ByteLength)
            {
                throw new InvalidOperationException("A component state writer changed its measured record extent.");
            }
        }

        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus WriteSchemaReferenceRecords(
        in World.EntityStateCaptureEnumerator entity,
        EbinReadLimits limits,
        scoped ReadOnlySpan<EbinPayloadSlice> transientSchemaSlices,
        scoped Span<byte> transientSchemaStorage,
        scoped Span<byte> candidateStorage,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        for (var schemaIndex = 0; schemaIndex < transientSchemaSlices.Length; schemaIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var schema = entity.GetTransientComponentSchema(schemaIndex);
            var slice = transientSchemaSlices[schemaIndex];
            var status = WorldPersistenceSchemaReferenceWriter.Write(
                schema,
                limits,
                candidateStorage,
                transientSchemaStorage.Slice(slice.Offset, slice.ByteLength),
                out var bytesWritten,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            if (bytesWritten != slice.ByteLength)
            {
                throw new InvalidOperationException("A transient schema-reference writer changed its measured record extent.");
            }
        }

        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus WriteTagRecords(
        in World.EntityStateCaptureEnumerator entity,
        EbinReadLimits limits,
        scoped ReadOnlySpan<EbinPayloadSlice> tagSlices,
        scoped Span<byte> tagStorage,
        scoped Span<byte> candidateStorage,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        for (var tagIndex = 0; tagIndex < tagSlices.Length; tagIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var schema = entity.GetTagSchema(tagIndex);
            var source = new EbinRecordWriteView(schema.SchemaId, schema.Major, schema.Minor, [], []);
            var slice = tagSlices[tagIndex];
            var status = EbinStructuralWriter.Write(
                in source,
                limits,
                candidateStorage,
                tagStorage.Slice(slice.Offset, slice.ByteLength),
                out var bytesWritten,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            if (bytesWritten != slice.ByteLength)
            {
                throw new InvalidOperationException("An EntityTag writer changed its measured record extent.");
            }
        }

        diagnostic = null;
        return EbinWriteStatus.Complete;
    }
}
