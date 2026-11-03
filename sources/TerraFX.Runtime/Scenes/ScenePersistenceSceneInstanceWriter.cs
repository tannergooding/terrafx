// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Scenes;

internal static class ScenePersistenceSceneInstanceWriter
{
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("state/scene-instance").SchemaId;

    internal static EbinWriteStatus Measure(
        SceneInstance instance,
        WorldPersistenceCaptureLease lease,
        scoped ReadOnlySpan<byte> entityIdsPayload,
        scoped ReadOnlySpan<byte> bindingsArrayPayload,
        scoped ReadOnlySpan<byte> externalBindingsArrayPayload,
        scoped ReadOnlySpan<byte> occurrencesArrayPayload,
        EbinReadLimits limits,
        scoped Span<byte> rootPayloadStorage,
        out ScenePersistenceInstanceWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(limits);
        workspace = default;
        diagnostic = null;
        var plan = instance.GetPersistencePlan(lease);
        var entityCount = plan.Entities.Length;
        var bindingCount = entityCount;
        var externalBindingCount = instance.GetExternalBindingCaptureRows(lease).Length;
        var occurrenceCount = plan.Occurrences.Length;
        var assignmentOrdinal = instance.GetPersistenceAssignmentOrdinal(lease);

        if ((instance.SceneInstanceId == Guid.Empty)
            || (assignmentOrdinal == 0)
            || (occurrenceCount == 0)
            || plan.GetSource(0).IsPrefab
            || (plan.SceneAssetId != instance.AssetId))
        {
            diagnostic = CreateDiagnostic("scene-instance-metadata");
            return EbinWriteStatus.InvalidRecord;
        }

        var status = ValidateArrayPayload(entityIdsPayload, entityCount, fixedArray: true, "scene-entity-id-array", limits, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = ValidateArrayPayload(bindingsArrayPayload, bindingCount, fixedArray: false, "scene-entity-binding-array", limits, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = ValidateArrayPayload(
            externalBindingsArrayPayload,
            externalBindingCount,
            fixedArray: false,
            "external-scene-binding-array",
            limits,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = ValidateArrayPayload(occurrencesArrayPayload, occurrenceCount, fixedArray: false, "scene-occurrence-array", limits, out diagnostic);

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

        var rootPayloadBytes = checked(
            16L
            + sourceRecordBytes
            + entityIdsPayload.Length
            + bindingsArrayPayload.Length
            + externalBindingsArrayPayload.Length
            + sizeof(long)
            + occurrencesArrayPayload.Length);

        if ((rootPayloadBytes > int.MaxValue) || (rootPayloadBytes > limits.MaximumRecordBytes))
        {
            diagnostic = CreateDiagnostic(
                "record-byte-budget",
                checked((ulong)rootPayloadBytes),
                checked((ulong)limits.MaximumRecordBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        var requiredPayloadBytes = checked((int)rootPayloadBytes);

        if (rootPayloadStorage.Length != requiredPayloadBytes)
        {
            throw new ArgumentException("The state/scene-instance root-payload workspace must match its measured extent.", nameof(rootPayloadStorage));
        }

        ValidateNonoverlappingPayloads(
            entityIdsPayload,
            bindingsArrayPayload,
            externalBindingsArrayPayload,
            occurrencesArrayPayload,
            rootPayloadStorage);
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[7];
        status = BuildFields(
            instance,
            plan,
            assignmentOrdinal,
            limits,
            sourceRecordBytes,
            entityIdsPayload,
            bindingsArrayPayload,
            bindingCount,
            externalBindingsArrayPayload,
            externalBindingCount,
            occurrencesArrayPayload,
            occurrenceCount,
            fields,
            rootPayloadStorage,
            out var payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (payloadBytes != requiredPayloadBytes)
        {
            throw new InvalidOperationException("The state/scene-instance root-payload extent changed after workspace measurement.");
        }

        var source = new EbinRecordWriteView(
            s_schemaId,
            major: 1,
            minor: 0,
            fields,
            rootPayloadStorage[..payloadBytes]);
        status = EbinStructuralWriter.Measure(in source, limits, out var recordBytes, out diagnostic);

        if (status is EbinWriteStatus.Complete)
        {
            workspace = new ScenePersistenceInstanceWorkspaceRequirements(
                entityCount,
                bindingCount,
                externalBindingCount,
                occurrenceCount,
                entityIdsPayload.Length,
                bindingsArrayPayload.Length,
                externalBindingsArrayPayload.Length,
                occurrencesArrayPayload.Length,
                sourceRecordBytes,
                requiredPayloadBytes,
                recordBytes);
        }

        return status;
    }

    internal static EbinWriteStatus Write(
        SceneInstance instance,
        WorldPersistenceCaptureLease lease,
        ScenePersistenceInstanceWorkspaceRequirements workspace,
        scoped ReadOnlySpan<byte> entityIdsPayload,
        scoped ReadOnlySpan<byte> bindingsArrayPayload,
        scoped ReadOnlySpan<byte> externalBindingsArrayPayload,
        scoped ReadOnlySpan<byte> occurrencesArrayPayload,
        EbinReadLimits limits,
        scoped Span<byte> rootPayloadStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = Measure(
            instance,
            lease,
            entityIdsPayload,
            bindingsArrayPayload,
            externalBindingsArrayPayload,
            occurrencesArrayPayload,
            limits,
            rootPayloadStorage,
            out var measuredWorkspace,
            out diagnostic);
        bytesWritten = 0;

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (!workspace.Matches(measuredWorkspace))
        {
            diagnostic = CreateDiagnostic("scene-instance-workspace-mismatch");
            return EbinWriteStatus.InvalidRecord;
        }

        if ((candidateStorage.Length != workspace.RecordBytes)
            || (destination.Length != workspace.RecordBytes))
        {
            throw new ArgumentException("The state/scene-instance record workspaces must match its measured extent.");
        }

        var plan = instance.GetPersistencePlan(lease);
        var assignmentOrdinal = instance.GetPersistenceAssignmentOrdinal(lease);
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

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[7];
        status = BuildFields(
            instance,
            plan,
            assignmentOrdinal,
            limits,
            sourceRecordBytes,
            entityIdsPayload,
            bindingsArrayPayload,
            workspace.BindingCount,
            externalBindingsArrayPayload,
            workspace.ExternalBindingCount,
            occurrencesArrayPayload,
            workspace.OccurrenceCount,
            fields,
            rootPayloadStorage,
            out var payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (payloadBytes != workspace.RootPayloadBytes)
        {
            throw new InvalidOperationException("The state/scene-instance root-payload extent changed after workspace measurement.");
        }

        var source = new EbinRecordWriteView(
            s_schemaId,
            major: 1,
            minor: 0,
            fields,
            rootPayloadStorage[..payloadBytes]);
        return EbinStructuralWriter.Write(
            in source,
            limits,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    private static void AddArrayField(
        uint tag,
        int count,
        scoped ReadOnlySpan<byte> payload,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> rootPayloadStorage,
        ref int payloadOffset)
    {
        payload.CopyTo(rootPayloadStorage[payloadOffset..]);
        fields[fieldCount++] = new EbinFieldSource(
            tag,
            24,
            1,
            checked((ulong)count),
            new EbinPayloadSlice(payloadOffset, payload.Length));
        payloadOffset = checked(payloadOffset + payload.Length);
    }

    private static void AddFixedArrayField(
        uint tag,
        int count,
        scoped ReadOnlySpan<byte> payload,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> rootPayloadStorage,
        ref int payloadOffset)
    {
        payload.CopyTo(rootPayloadStorage[payloadOffset..]);
        fields[fieldCount++] = new EbinFieldSource(
            tag,
            23,
            1,
            checked((ulong)count),
            new EbinPayloadSlice(payloadOffset, payload.Length));
        payloadOffset = checked(payloadOffset + payload.Length);
    }

    private static EbinWriteStatus AddGuidField(
        uint tag,
        Guid value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> rootPayloadStorage,
        ref int payloadOffset,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, rootPayloadStorage[payloadOffset..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(tag, 11, 1, 1, new EbinPayloadSlice(payloadOffset, fieldBytes));
        payloadOffset = checked(payloadOffset + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus AddInt64Field(
        uint tag,
        long value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> rootPayloadStorage,
        ref int payloadOffset,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, rootPayloadStorage[payloadOffset..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(tag, 8, 1, 1, new EbinPayloadSlice(payloadOffset, fieldBytes));
        payloadOffset = checked(payloadOffset + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus AddRecordField(
        uint tag,
        scoped ReadOnlySpan<byte> record,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> rootPayloadStorage,
        ref int payloadOffset,
        out EbinStructuralDiagnostic? diagnostic)
    {
        record.CopyTo(rootPayloadStorage[payloadOffset..]);
        fields[fieldCount++] = new EbinFieldSource(tag, 22, 1, 1, new EbinPayloadSlice(payloadOffset, record.Length));
        payloadOffset = checked(payloadOffset + record.Length);
        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus BuildFields(
        SceneInstance instance,
        SceneExpansionPlan plan,
        uint assignmentOrdinal,
        EbinReadLimits limits,
        int sourceRecordBytes,
        scoped ReadOnlySpan<byte> entityIdsPayload,
        scoped ReadOnlySpan<byte> bindingsArrayPayload,
        int bindingCount,
        scoped ReadOnlySpan<byte> externalBindingsArrayPayload,
        int externalBindingCount,
        scoped ReadOnlySpan<byte> occurrencesArrayPayload,
        int occurrenceCount,
        Span<EbinFieldSource> fields,
        Span<byte> rootPayloadStorage,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        fields.Clear();
        rootPayloadStorage.Clear();
        payloadBytes = 0;
        diagnostic = null;
        var fieldCount = 0;
        var offset = 0;
        var status = AddGuidField(1, instance.SceneInstanceId, fields, ref fieldCount, rootPayloadStorage, ref offset, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        Span<byte> sourceCandidateStorage = stackalloc byte[sourceRecordBytes];
        Span<byte> sourceRecordStorage = stackalloc byte[sourceRecordBytes];
        status = ScenePersistenceOccurrenceWriter.WriteSourceAssetReference(
            plan.GetSource(0),
            limits,
            sourceCandidateStorage,
            sourceRecordStorage,
            out var sourceBytesWritten,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (sourceBytesWritten != sourceRecordBytes)
        {
            throw new InvalidOperationException("The Scene source reference extent changed after workspace measurement.");
        }

        status = AddRecordField(2, sourceRecordStorage, fields, ref fieldCount, rootPayloadStorage, ref offset, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        AddFixedArrayField(3, plan.Entities.Length, entityIdsPayload, fields, ref fieldCount, rootPayloadStorage, ref offset);
        AddArrayField(4, bindingCount, bindingsArrayPayload, fields, ref fieldCount, rootPayloadStorage, ref offset);
        AddArrayField(5, externalBindingCount, externalBindingsArrayPayload, fields, ref fieldCount, rootPayloadStorage, ref offset);
        status = AddInt64Field(
            6,
            assignmentOrdinal,
            fields,
            ref fieldCount,
            rootPayloadStorage,
            ref offset,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        AddArrayField(7, occurrenceCount, occurrencesArrayPayload, fields, ref fieldCount, rootPayloadStorage, ref offset);
        payloadBytes = offset;
        return EbinWriteStatus.Complete;
    }

    private static EbinStructuralDiagnostic CreateDiagnostic(string category, ulong claim = 0, ulong limit = 1)
        => new EbinStructuralDiagnostic(
            new ContentSchemaIdentityAssignment("state/scene-instance").SchemaId,
            0,
            category,
            null,
            claim,
            limit,
            EbinStructuralDiagnosticSource.Construction);

    private static EbinWriteStatus ValidateArrayPayload(
        scoped ReadOnlySpan<byte> payload,
        int expectedCount,
        bool fixedArray,
        string category,
        EbinReadLimits limits,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var minimumBytes = fixedArray ? 32 : 16;
        var invalidPayload = payload.Length < minimumBytes;

        if (!invalidPayload && fixedArray)
        {
            var expectedBytes = checked(32L + (16L * expectedCount));
            invalidPayload = (expectedBytes > int.MaxValue)
                || (payload.Length != expectedBytes)
                || (BinaryPrimitives.ReadUInt16LittleEndian(payload[16..]) != 11)
                || (BinaryPrimitives.ReadUInt32LittleEndian(payload[20..]) != 16);
        }
        else if (!invalidPayload)
        {
            invalidPayload = BinaryPrimitives.ReadUInt64LittleEndian(payload) != checked((ulong)expectedCount);
        }

        if (invalidPayload)
        {
            var claim = fixedArray
                ? checked((ulong)payload.Length)
                : (payload.Length >= sizeof(ulong) ? BinaryPrimitives.ReadUInt64LittleEndian(payload) : 0);
            var limit = fixedArray
                ? checked((ulong)(32L + (16L * expectedCount)))
                : checked((ulong)expectedCount);
            diagnostic = CreateDiagnostic(category, claim, limit);
            return EbinWriteStatus.InvalidRecord;
        }

        if ((expectedCount > limits.MaximumArrayElements)
            || (expectedCount > limits.MaximumAggregateArrayElements))
        {
            diagnostic = CreateDiagnostic(
                "array-count-budget",
                checked((ulong)expectedCount),
                checked((ulong)Math.Min(limits.MaximumArrayElements, limits.MaximumAggregateArrayElements)));
            return EbinWriteStatus.InvalidRecord;
        }

        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    private static void ValidateNonoverlappingPayloads(
        scoped ReadOnlySpan<byte> entityIdsPayload,
        scoped ReadOnlySpan<byte> bindingsArrayPayload,
        scoped ReadOnlySpan<byte> externalBindingsArrayPayload,
        scoped ReadOnlySpan<byte> occurrencesArrayPayload,
        scoped Span<byte> rootPayloadStorage)
    {
        if (rootPayloadStorage.Overlaps(entityIdsPayload)
            || rootPayloadStorage.Overlaps(bindingsArrayPayload)
            || rootPayloadStorage.Overlaps(externalBindingsArrayPayload)
            || rootPayloadStorage.Overlaps(occurrencesArrayPayload))
        {
            throw new ArgumentException("The state/scene-instance root payload cannot overlap its source arrays.", nameof(rootPayloadStorage));
        }
    }
}
