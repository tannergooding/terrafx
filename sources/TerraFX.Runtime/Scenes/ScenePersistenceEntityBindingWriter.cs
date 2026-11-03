// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Scenes;

internal static class ScenePersistenceEntityBindingWriter
{
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("state/scene-entity-binding").SchemaId;

    internal static EbinWriteStatus Measure(
        SceneExpansionPlan plan,
        int expandedEntityIndex,
        Guid entityId,
        EbinReadLimits limits,
        out int recordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentOutOfRangeException.ThrowIfEqual(entityId, Guid.Empty);
        var status = MeasureAssetReference(plan, expandedEntityIndex, limits, out var assetReferenceBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            recordBytes = 0;
            return status;
        }

        var expandedEntity = GetExpandedEntity(plan, expandedEntityIndex);
        Span<Guid> placementPath = stackalloc Guid[32];
        var pathLength = plan.CopyOccurrencePlacementPath(expandedEntity.OccurrenceIndex, placementPath);
        var payloadBytes = checked(32 + (pathLength * 16) + assetReferenceBytes + (4 * 16));
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[6];
        Span<byte> payloadStorage = stackalloc byte[payloadBytes];
        status = BuildFields(
            plan,
            expandedEntityIndex,
            entityId,
            limits,
            assetReferenceBytes,
            placementPath,
            pathLength,
            fields,
            payloadStorage,
            out var fieldCount,
            out var measuredPayloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            recordBytes = 0;
            return status;
        }

        var source = new EbinRecordWriteView(
            s_schemaId,
            major: 1,
            minor: 0,
            fields[..fieldCount],
            payloadStorage[..measuredPayloadBytes]);
        return EbinStructuralWriter.Measure(in source, limits, out recordBytes, out diagnostic);
    }

    internal static EbinWriteStatus Write(
        SceneExpansionPlan plan,
        int expandedEntityIndex,
        Guid entityId,
        EbinReadLimits limits,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = Measure(plan, expandedEntityIndex, entityId, limits, out var recordBytes, out diagnostic);
        bytesWritten = 0;

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if ((candidateStorage.Length != recordBytes) || (destination.Length != recordBytes))
        {
            throw new ArgumentException("The scene-entity-binding record workspaces must match the measured extent.");
        }

        status = MeasureAssetReference(plan, expandedEntityIndex, limits, out var assetReferenceBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var expandedEntity = GetExpandedEntity(plan, expandedEntityIndex);
        Span<Guid> placementPath = stackalloc Guid[32];
        var pathLength = plan.CopyOccurrencePlacementPath(expandedEntity.OccurrenceIndex, placementPath);
        var payloadBytes = checked(32 + (pathLength * 16) + assetReferenceBytes + (4 * 16));
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[6];
        Span<byte> payloadStorage = stackalloc byte[payloadBytes];
        status = BuildFields(
            plan,
            expandedEntityIndex,
            entityId,
            limits,
            assetReferenceBytes,
            placementPath,
            pathLength,
            fields,
            payloadStorage,
            out var fieldCount,
            out var measuredPayloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var source = new EbinRecordWriteView(
            s_schemaId,
            major: 1,
            minor: 0,
            fields[..fieldCount],
            payloadStorage[..measuredPayloadBytes]);
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
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadOffset,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadOffset..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(tag, 11, 1, 1, new EbinPayloadSlice(payloadOffset, fieldBytes));
        payloadOffset = checked(payloadOffset + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus BuildFields(
        SceneExpansionPlan plan,
        int expandedEntityIndex,
        Guid entityId,
        EbinReadLimits limits,
        int assetReferenceBytes,
        scoped ReadOnlySpan<Guid> placementPath,
        int pathLength,
        Span<EbinFieldSource> fields,
        Span<byte> payloadStorage,
        out int fieldCount,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        fields.Clear();
        payloadStorage.Clear();
        fieldCount = 0;
        payloadBytes = 0;
        var expandedEntity = GetExpandedEntity(plan, expandedEntityIndex);
        var occurrence = plan.Occurrences[expandedEntity.OccurrenceIndex];
        var source = plan.GetSource(expandedEntity.OccurrenceIndex);
        var sourceEntity = plan.GetSourceEntity(expandedEntityIndex);

        if ((entityId == Guid.Empty)
            || (expandedEntity.ExpandedEntityKey == Guid.Empty)
            || (expandedEntity.AuthoredEntityId == Guid.Empty)
            || (sourceEntity.AuthoredEntityId != expandedEntity.AuthoredEntityId)
            || (occurrence.OccurrenceId == Guid.Empty))
        {
            diagnostic = CreateDiagnostic("scene-entity-binding-metadata");
            return EbinWriteStatus.InvalidRecord;
        }

        var pathPayloadBytes = checked(32 + (pathLength * 16));
        var status = EbinPayloadWriter.WriteFixedArray(
            placementPath[..pathLength],
            limits,
            payloadStorage[..pathPayloadBytes],
            out var fixedArrayBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (fixedArrayBytes != pathPayloadBytes)
        {
            throw new InvalidOperationException("The scene-entity-binding path extent changed after workspace measurement.");
        }

        fields[fieldCount++] = new EbinFieldSource(
            1,
            23,
            1,
            checked((ulong)pathLength),
            new EbinPayloadSlice(payloadBytes, fixedArrayBytes));
        payloadBytes = checked(payloadBytes + fixedArrayBytes);
        Span<byte> assetCandidateStorage = stackalloc byte[assetReferenceBytes];
        Span<byte> assetDestination = stackalloc byte[assetReferenceBytes];
        status = ScenePersistenceOccurrenceWriter.WriteSourceAssetReference(
            source,
            limits,
            assetCandidateStorage,
            assetDestination,
            out var assetBytesWritten,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (assetBytesWritten != assetReferenceBytes)
        {
            throw new InvalidOperationException("The scene-entity-binding source extent changed after workspace measurement.");
        }

        assetDestination.CopyTo(payloadStorage[payloadBytes..]);
        fields[fieldCount++] = new EbinFieldSource(
            2,
            22,
            1,
            1,
            new EbinPayloadSlice(payloadBytes, assetBytesWritten));
        payloadBytes = checked(payloadBytes + assetBytesWritten);
        status = AddGuidField(3, expandedEntity.AuthoredEntityId, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddGuidField(4, entityId, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddGuidField(5, occurrence.OccurrenceId, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        return AddGuidField(
            6,
            expandedEntity.ExpandedEntityKey,
            fields,
            ref fieldCount,
            payloadStorage,
            ref payloadBytes,
            out diagnostic);
    }

    private static EbinStructuralDiagnostic CreateDiagnostic(string category)
        => new EbinStructuralDiagnostic(
            s_schemaId,
            0,
            category,
            null,
            0,
            1,
            EbinStructuralDiagnosticSource.Construction);

    private static SceneExpandedEntity GetExpandedEntity(SceneExpansionPlan plan, int expandedEntityIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expandedEntityIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(expandedEntityIndex, plan.Entities.Length);
        return plan.Entities[expandedEntityIndex];
    }

    private static EbinWriteStatus MeasureAssetReference(
        SceneExpansionPlan plan,
        int expandedEntityIndex,
        EbinReadLimits limits,
        out int recordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var expandedEntity = GetExpandedEntity(plan, expandedEntityIndex);
        return ScenePersistenceOccurrenceWriter.MeasureSourceAssetReference(
            plan,
            expandedEntity.OccurrenceIndex,
            limits,
            out recordBytes,
            out diagnostic);
    }
}
