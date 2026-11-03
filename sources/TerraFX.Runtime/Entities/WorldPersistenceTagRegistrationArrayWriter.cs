// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceTagRegistrationArrayWriter
{
    internal static EbinWriteStatus Measure(
        WorldRegistration registration,
        EbinReadLimits limits,
        out WorldPersistenceRegistrationArrayWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(limits);
        workspace = default;
        diagnostic = null;
        long recordStorageBytes = 0;
        var maximumCandidateBytes = 0;

        foreach (var tag in registration.Tags)
        {
            var status = WorldPersistenceSchemaReferenceWriter.Measure(
                tag.Schema,
                limits,
                out var recordBytes,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            recordStorageBytes = checked(recordStorageBytes + recordBytes);
            maximumCandidateBytes = Math.Max(maximumCandidateBytes, recordBytes);
        }

        if (recordStorageBytes > int.MaxValue)
        {
            diagnostic = CreateDiagnostic(
                "record-byte-budget",
                checked((ulong)recordStorageBytes),
                checked((ulong)limits.MaximumRecordBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        var arrayStatus = WorldPersistenceRecordArrayLayout.MeasurePayloadBytes(
            registration.Tags.Length,
            recordStorageBytes,
            limits,
            out var arrayPayloadBytes,
            out diagnostic);

        if (arrayStatus is not EbinWriteStatus.Complete)
        {
            return arrayStatus;
        }

        workspace = new WorldPersistenceRegistrationArrayWorkspaceRequirements(
            registration.Tags.Length,
            checked((int)recordStorageBytes),
            maximumCandidateBytes,
            arrayPayloadBytes);
        return EbinWriteStatus.Complete;
    }

    internal static EbinWriteStatus Write(
        WorldRegistration registration,
        EbinReadLimits limits,
        WorldPersistenceRegistrationArrayWorkspaceRequirements workspace,
        scoped Span<EbinPayloadSlice> recordSlices,
        scoped Span<byte> recordStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> arrayPayloadDestination,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = Measure(registration, limits, out var measuredWorkspace, out diagnostic);
        payloadBytes = 0;

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (!workspace.Matches(measuredWorkspace))
        {
            diagnostic = CreateDiagnostic("tag-registration-array-workspace-mismatch", 0, 1);
            return EbinWriteStatus.InvalidRecord;
        }

        ValidateBuffers(
            workspace,
            recordSlices,
            recordStorage,
            candidateStorage,
            arrayPayloadDestination);
        var storageOffset = 0;

        for (var index = 0; index < registration.Tags.Length; index++)
        {
            var tag = registration.Tags[index];
            var statusForRecord = WorldPersistenceSchemaReferenceWriter.Write(
                tag.Schema,
                limits,
                candidateStorage,
                recordStorage[storageOffset..],
                out var recordBytes,
                out diagnostic);

            if (statusForRecord is not EbinWriteStatus.Complete)
            {
                return statusForRecord;
            }

            recordSlices[index] = new EbinPayloadSlice(storageOffset, recordBytes);
            storageOffset = checked(storageOffset + recordBytes);
        }

        if (storageOffset != workspace.RecordStorageBytes)
        {
            throw new InvalidOperationException("The tag schema-reference array extent changed after workspace measurement.");
        }

        status = EbinPayloadWriter.WriteRecordArray(
            recordSlices,
            recordStorage,
            limits,
            arrayPayloadDestination,
            out payloadBytes,
            out diagnostic);

        if ((status is EbinWriteStatus.Complete) && (payloadBytes != workspace.ArrayPayloadBytes))
        {
            diagnostic = CreateDiagnostic(
                "tag-registration-array-size-mismatch",
                checked((ulong)payloadBytes),
                checked((ulong)workspace.ArrayPayloadBytes));
            payloadBytes = 0;
            return EbinWriteStatus.InvalidRecord;
        }

        return status;
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

    private static void ValidateBuffers(
        WorldPersistenceRegistrationArrayWorkspaceRequirements workspace,
        scoped ReadOnlySpan<EbinPayloadSlice> recordSlices,
        scoped ReadOnlySpan<byte> recordStorage,
        scoped ReadOnlySpan<byte> candidateStorage,
        scoped ReadOnlySpan<byte> arrayPayloadDestination)
    {
        if ((recordSlices.Length != workspace.RecordCount)
            || (recordStorage.Length != workspace.RecordStorageBytes)
            || (candidateStorage.Length != workspace.MaximumCandidateBytes)
            || (arrayPayloadDestination.Length != workspace.ArrayPayloadBytes))
        {
            throw new ArgumentException("The tag-registration array workspaces must match the measured lengths.");
        }

        if (MemoryMarshal.AsBytes(recordSlices).Overlaps(recordStorage)
            || MemoryMarshal.AsBytes(recordSlices).Overlaps(candidateStorage)
            || MemoryMarshal.AsBytes(recordSlices).Overlaps(arrayPayloadDestination)
            || recordStorage.Overlaps(candidateStorage)
            || recordStorage.Overlaps(arrayPayloadDestination)
            || candidateStorage.Overlaps(arrayPayloadDestination))
        {
            throw new ArgumentException("The tag-registration array workspaces must not overlap.");
        }
    }
}
