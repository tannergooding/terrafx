// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;
using TerraFX.Assets;

namespace TerraFX.Scenes;

internal static class ScenePersistenceOccurrenceWriter
{
    private static readonly Guid s_assetReferenceSchemaId = new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId;
    private static readonly Guid s_prefabSchemaId = new ContentSchemaIdentityAssignment("native/prefab").SchemaId;
    private static readonly Guid s_sceneSchemaId = new ContentSchemaIdentityAssignment("native/scene").SchemaId;
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("state/scene-occurrence").SchemaId;

    internal static EbinWriteStatus Measure(
        SceneExpansionPlan plan,
        int occurrenceIndex,
        EbinReadLimits limits,
        out int recordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentOutOfRangeException.ThrowIfNegative(occurrenceIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(occurrenceIndex, plan.Occurrences.Length);
        var status = MeasureAssetReference(
            plan,
            occurrenceIndex,
            limits,
            out var assetReferenceRecordBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            recordBytes = 0;
            return status;
        }

        Span<Guid> placementPath = stackalloc Guid[32];
        var pathLength = plan.CopyOccurrencePlacementPath(occurrenceIndex, placementPath);
        var occurrence = plan.Occurrences[occurrenceIndex];
        var parented = occurrence.ParentOccurrenceIndex >= 0;
        var pathPayloadBytes = checked(32 + (pathLength * 16));
        var scalarPayloadBytes = checked(16 + assetReferenceRecordBytes + (parented ? 32 : 0));
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[5];
        Span<byte> payloadStorage = stackalloc byte[checked(pathPayloadBytes + scalarPayloadBytes)];
        status = BuildFields(
            plan,
            occurrenceIndex,
            limits,
            assetReferenceRecordBytes,
            placementPath,
            pathLength,
            fields,
            payloadStorage,
            out var fieldCount,
            out var payloadBytes,
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
            payloadStorage[..payloadBytes]);
        return EbinStructuralWriter.Measure(in source, limits, out recordBytes, out diagnostic);
    }

    internal static EbinWriteStatus Write(
        SceneExpansionPlan plan,
        int occurrenceIndex,
        EbinReadLimits limits,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = Measure(plan, occurrenceIndex, limits, out var recordBytes, out diagnostic);
        bytesWritten = 0;

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if ((candidateStorage.Length != recordBytes) || (destination.Length != recordBytes))
        {
            throw new ArgumentException("The scene-occurrence record workspaces must match the measured extent.");
        }

        status = MeasureAssetReference(
            plan,
            occurrenceIndex,
            limits,
            out var assetReferenceRecordBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        Span<Guid> placementPath = stackalloc Guid[32];
        var pathLength = plan.CopyOccurrencePlacementPath(occurrenceIndex, placementPath);
        var occurrence = plan.Occurrences[occurrenceIndex];
        var pathPayloadBytes = checked(32 + (pathLength * 16));
        var scalarPayloadBytes = checked(16 + assetReferenceRecordBytes + ((occurrence.ParentOccurrenceIndex >= 0) ? 32 : 0));
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[5];
        Span<byte> payloadStorage = stackalloc byte[checked(pathPayloadBytes + scalarPayloadBytes)];
        status = BuildFields(
            plan,
            occurrenceIndex,
            limits,
            assetReferenceRecordBytes,
            placementPath,
            pathLength,
            fields,
            payloadStorage,
            out var fieldCount,
            out var payloadBytes,
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
            payloadStorage[..payloadBytes]);
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
        ushort flags,
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

        fields[fieldCount++] = new EbinFieldSource(tag, 11, flags, 1, new EbinPayloadSlice(payloadOffset, fieldBytes));
        payloadOffset = checked(payloadOffset + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus BuildAssetReferenceFields(
        uint assetId,
        Guid schemaId,
        uint schemaMajor,
        Span<EbinFieldSource> fields,
        Span<byte> payloadStorage,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        fields.Clear();
        payloadStorage.Clear();
        payloadBytes = 0;
        var fieldCount = 0;
        var status = EbinPayloadWriter.Write(assetId, payloadStorage, out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(1, 3, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        status = AddGuidField(2, schemaId, 1, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = EbinPayloadWriter.Write(schemaMajor, payloadStorage[payloadBytes..], out fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount] = new EbinFieldSource(3, 3, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus BuildFields(
        SceneExpansionPlan plan,
        int occurrenceIndex,
        EbinReadLimits limits,
        int assetReferenceRecordBytes,
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
        var occurrence = plan.Occurrences[occurrenceIndex];
        var source = plan.GetSource(occurrenceIndex);
        var status = ValidateOccurrence(
            occurrence,
            source,
            occurrenceIndex,
            plan.Occurrences.Length,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var pathPayloadBytes = checked(32 + (pathLength * 16));
        status = EbinPayloadWriter.WriteFixedArray(
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
            throw new InvalidOperationException("The Scene occurrence-path extent changed after workspace measurement.");
        }

        fields[fieldCount++] = new EbinFieldSource(
            1,
            23,
            1,
            checked((ulong)pathLength),
            new EbinPayloadSlice(payloadBytes, fixedArrayBytes));
        payloadBytes = checked(payloadBytes + fixedArrayBytes);
        status = AddGuidField(
            2,
            occurrence.OccurrenceId,
            1,
            fields,
            ref fieldCount,
            payloadStorage,
            ref payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        Span<byte> assetCandidateStorage = stackalloc byte[assetReferenceRecordBytes];
        Span<byte> assetDestination = stackalloc byte[assetReferenceRecordBytes];
        status = WriteAssetReference(
            source,
            limits,
            assetCandidateStorage,
            assetDestination,
            out var assetReferenceBytesWritten,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (assetReferenceBytesWritten != assetReferenceRecordBytes)
        {
            throw new InvalidOperationException("The Scene occurrence asset-reference extent changed after workspace measurement.");
        }

        assetDestination.CopyTo(payloadStorage[payloadBytes..]);
        fields[fieldCount++] = new EbinFieldSource(
            3,
            22,
            1,
            1,
            new EbinPayloadSlice(payloadBytes, assetReferenceBytesWritten));
        payloadBytes = checked(payloadBytes + assetReferenceBytesWritten);

        if (occurrence.ParentOccurrenceIndex >= 0)
        {
            var parentId = plan.Occurrences[occurrence.ParentOccurrenceIndex].OccurrenceId;

            if (parentId == Guid.Empty)
            {
                diagnostic = CreateDiagnostic("scene-occurrence-parent-id", 0, 1);
                return EbinWriteStatus.InvalidRecord;
            }

            status = AddGuidField(
                4,
                parentId,
                0,
                fields,
                ref fieldCount,
                payloadStorage,
                ref payloadBytes,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            status = AddGuidField(
                5,
                occurrence.PlacementId,
                0,
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

        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    private static EbinStructuralDiagnostic CreateDiagnostic(string category, ulong claim = 0, ulong limit = 1)
        => new EbinStructuralDiagnostic(
            s_schemaId,
            0,
            category,
            null,
            claim,
            limit,
            EbinStructuralDiagnosticSource.Construction);

    private static EbinWriteStatus MeasureAssetReference(
        SceneExpansionPlan plan,
        int occurrenceIndex,
        EbinReadLimits limits,
        out int recordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var occurrence = plan.Occurrences[occurrenceIndex];
        var source = plan.GetSource(occurrenceIndex);
        var status = ValidateOccurrence(
            occurrence,
            source,
            occurrenceIndex,
            plan.Occurrences.Length,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            recordBytes = 0;
            return status;
        }

        var schemaId = source.IsPrefab ? s_prefabSchemaId : s_sceneSchemaId;
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[3];
        Span<byte> payloadStorage = stackalloc byte[24];
        status = BuildAssetReferenceFields(
            checked((uint)source.AssetId.Value),
            schemaId,
            schemaMajor: 2,
            fields,
            payloadStorage,
            out var payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            recordBytes = 0;
            return status;
        }

        var view = new EbinRecordWriteView(
            s_assetReferenceSchemaId,
            major: 1,
            minor: 0,
            fields,
            payloadStorage[..payloadBytes]);
        return EbinStructuralWriter.Measure(in view, limits, out recordBytes, out diagnostic);
    }

    private static EbinWriteStatus ValidateOccurrence(
        SceneExpandedOccurrence occurrence,
        SceneExpansionSource source,
        int occurrenceIndex,
        int occurrenceCount,
        out EbinStructuralDiagnostic? diagnostic)
    {
        if ((occurrence.OccurrenceId == Guid.Empty)
            || (occurrence.AssetId.Value <= 0)
            || (occurrence.AssetId != source.AssetId)
            || (occurrence.ParentOccurrenceIndex < -1)
            || (occurrence.ParentOccurrenceIndex >= occurrenceCount)
            || ((occurrence.ParentOccurrenceIndex >= 0) && (occurrence.ParentOccurrenceIndex >= occurrenceIndex))
            || ((occurrence.ParentOccurrenceIndex < 0) != (occurrence.PlacementId == Guid.Empty)))
        {
            diagnostic = CreateDiagnostic("scene-occurrence-metadata", 0, 1);
            return EbinWriteStatus.InvalidRecord;
        }

        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus WriteAssetReference(
        SceneExpansionSource source,
        EbinReadLimits limits,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[3];
        Span<byte> payloadStorage = stackalloc byte[24];
        var schemaId = source.IsPrefab ? s_prefabSchemaId : s_sceneSchemaId;
        var status = BuildAssetReferenceFields(
            checked((uint)source.AssetId.Value),
            schemaId,
            schemaMajor: 2,
            fields,
            payloadStorage,
            out var payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return status;
        }

        var view = new EbinRecordWriteView(
            s_assetReferenceSchemaId,
            major: 1,
            minor: 0,
            fields,
            payloadStorage[..payloadBytes]);
        return EbinStructuralWriter.Write(
            in view,
            limits,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }
}
