// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using TerraFX;

namespace TerraFX.Scenes;

internal static class ScenePersistenceScenesWriter
{
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("state/scenes").SchemaId;

    internal static EbinWriteStatus Measure(
        int instanceCount,
        scoped ReadOnlySpan<byte> instancesArrayPayload,
        scoped ReadOnlySpan<byte> hierarchyRecord,
        EbinReadLimits limits,
        scoped Span<byte> rootPayloadStorage,
        out ScenePersistenceScenesWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        workspace = default;
        diagnostic = null;

        if ((instanceCount < 0)
            || (instanceCount > 65_536)
            || (instanceCount > limits.MaximumArrayElements)
            || (instanceCount > limits.MaximumAggregateArrayElements)
            || (instancesArrayPayload.Length < 16)
            || (BinaryPrimitives.ReadUInt64LittleEndian(instancesArrayPayload) != checked((ulong)instanceCount)))
        {
            diagnostic = CreateDiagnostic("scenes-instance-array");
            return EbinWriteStatus.InvalidRecord;
        }

        var readStatus = EbinStructuralReader.Validate(
            hierarchyRecord,
            limits,
            EbinReadPolicy.Canonical,
            out var hierarchyBytesConsumed,
            out var hierarchyReadDiagnostic);

        if (readStatus is not EbinReadStatus.Complete)
        {
            return ConvertReadStatus(readStatus, hierarchyReadDiagnostic, "scenes-hierarchy-record", out diagnostic);
        }

        if (hierarchyBytesConsumed != hierarchyRecord.Length)
        {
            diagnostic = CreateDiagnostic("scenes-hierarchy-record-size");
            return EbinWriteStatus.InvalidRecord;
        }

        readStatus = EbinStructuralReader.ReadHeader(
            hierarchyRecord,
            limits,
            out var hierarchyHeader,
            out _,
            out hierarchyReadDiagnostic);

        if (readStatus is not EbinReadStatus.Complete)
        {
            return ConvertReadStatus(readStatus, hierarchyReadDiagnostic, "scenes-hierarchy-record-header", out diagnostic);
        }

        if ((hierarchyHeader.SchemaId != new ContentSchemaIdentityAssignment("scene/world-hierarchy").SchemaId)
            || (hierarchyHeader.Major != 1)
            || (hierarchyHeader.Minor != 0)
            || (hierarchyHeader.FieldCount != 2))
        {
            diagnostic = CreateDiagnostic("scenes-hierarchy-record-schema");
            return EbinWriteStatus.InvalidRecord;
        }

        var rootPayloadBytes = checked((long)instancesArrayPayload.Length + hierarchyRecord.Length);

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
            throw new ArgumentException("The state/scenes root payload workspace must match its measured extent.", nameof(rootPayloadStorage));
        }

        ValidateNonoverlappingPayloads(instancesArrayPayload, hierarchyRecord, rootPayloadStorage);
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[2];
        var buildStatus = BuildFields(
            instanceCount,
            instancesArrayPayload,
            hierarchyRecord,
            fields,
            rootPayloadStorage,
            out var payloadBytes,
            out diagnostic);

        if (buildStatus is not EbinWriteStatus.Complete)
        {
            return buildStatus;
        }

        if (payloadBytes != requiredPayloadBytes)
        {
            throw new InvalidOperationException("The state/scenes root payload extent changed during measurement.");
        }

        var source = new EbinRecordWriteView(
            s_schemaId,
            major: 1,
            minor: 0,
            fields,
            rootPayloadStorage[..payloadBytes]);
        var writeStatus = EbinStructuralWriter.Measure(in source, limits, out var recordBytes, out diagnostic);

        if (writeStatus is EbinWriteStatus.Complete)
        {
            workspace = new ScenePersistenceScenesWorkspaceRequirements(
                instanceCount,
                instancesArrayPayload.Length,
                hierarchyRecord.Length,
                requiredPayloadBytes,
                recordBytes);
        }

        return writeStatus;
    }

    internal static EbinWriteStatus Write(
        ScenePersistenceScenesWorkspaceRequirements workspace,
        int instanceCount,
        scoped ReadOnlySpan<byte> instancesArrayPayload,
        scoped ReadOnlySpan<byte> hierarchyRecord,
        EbinReadLimits limits,
        scoped Span<byte> rootPayloadStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = Measure(
            instanceCount,
            instancesArrayPayload,
            hierarchyRecord,
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
            diagnostic = CreateDiagnostic("scenes-workspace-mismatch");
            return EbinWriteStatus.InvalidRecord;
        }

        if ((candidateStorage.Length != workspace.RecordBytes)
            || (destination.Length != workspace.RecordBytes))
        {
            throw new ArgumentException("The state/scenes record workspaces must match their measured extent.");
        }

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[2];
        status = BuildFields(
            instanceCount,
            instancesArrayPayload,
            hierarchyRecord,
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
            throw new InvalidOperationException("The state/scenes root payload extent changed after workspace measurement.");
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

    private static EbinWriteStatus BuildFields(
        int instanceCount,
        scoped ReadOnlySpan<byte> instancesArrayPayload,
        scoped ReadOnlySpan<byte> hierarchyRecord,
        Span<EbinFieldSource> fields,
        Span<byte> rootPayloadStorage,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        fields.Clear();
        instancesArrayPayload.CopyTo(rootPayloadStorage);
        fields[0] = new EbinFieldSource(
            1,
            24,
            1,
            checked((ulong)instanceCount),
            new EbinPayloadSlice(0, instancesArrayPayload.Length));
        hierarchyRecord.CopyTo(rootPayloadStorage[instancesArrayPayload.Length..]);
        fields[1] = new EbinFieldSource(
            2,
            22,
            1,
            1,
            new EbinPayloadSlice(instancesArrayPayload.Length, hierarchyRecord.Length));
        payloadBytes = checked(instancesArrayPayload.Length + hierarchyRecord.Length);
        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus ConvertReadStatus(
        EbinReadStatus status,
        EbinReadDiagnostic? readDiagnostic,
        string fallbackCategory,
        out EbinStructuralDiagnostic? diagnostic)
    {
        diagnostic = readDiagnostic is EbinReadDiagnostic value
            ? new EbinStructuralDiagnostic(
                value.SchemaId,
                value.Tag,
                value.Category,
                (value.Offset == 0) ? null : value.Offset,
                value.Claim,
                value.Limit,
                EbinStructuralDiagnosticSource.EncodedInput)
            : CreateDiagnostic(fallbackCategory);
        return (status is EbinReadStatus.UnsupportedSchema)
            ? EbinWriteStatus.UnsupportedSchema
            : EbinWriteStatus.InvalidRecord;
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

    private static void ValidateNonoverlappingPayloads(
        scoped ReadOnlySpan<byte> instancesArrayPayload,
        scoped ReadOnlySpan<byte> hierarchyRecord,
        scoped ReadOnlySpan<byte> rootPayloadStorage)
    {
        if (instancesArrayPayload.Overlaps(rootPayloadStorage)
            || hierarchyRecord.Overlaps(rootPayloadStorage))
        {
            throw new ArgumentException("The state/scenes root payload must not overlap its source records.");
        }
    }
}
