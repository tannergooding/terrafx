// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Scenes;

internal static class ScenePersistenceExternalBindingArrayWriter
{
    internal static EbinWriteStatus Measure(
        scoped ReadOnlySpan<SceneExternalBindingCaptureRow> rows,
        EbinReadLimits limits,
        out int recordStorageBytes,
        out int maximumCandidateBytes,
        out int arrayPayloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        recordStorageBytes = 0;
        maximumCandidateBytes = 0;
        arrayPayloadBytes = 0;
        diagnostic = null;
        long totalRecordStorageBytes = 0;
        var maximumChildCandidateBytes = 0;

        for (var index = 0; index < rows.Length; index++)
        {
            if ((index > 0) && (FoundationCatalogue.CompareRfc(rows[index - 1].BindingId, rows[index].BindingId) >= 0))
            {
                diagnostic = CreateDiagnostic("external-scene-binding-array-order");
                return EbinWriteStatus.InvalidRecord;
            }

            var status = ScenePersistenceExternalBindingWriter.Measure(
                rows[index],
                limits,
                out var childRecordBytes,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            totalRecordStorageBytes = checked(totalRecordStorageBytes + childRecordBytes);
            maximumChildCandidateBytes = Math.Max(maximumChildCandidateBytes, childRecordBytes);
        }

        if (totalRecordStorageBytes > int.MaxValue)
        {
            diagnostic = CreateDiagnostic("record-byte-budget");
            return EbinWriteStatus.InvalidRecord;
        }

        var arrayStatus = WorldPersistenceRecordArrayLayout.MeasurePayloadBytes(
            rows.Length,
            totalRecordStorageBytes,
            limits,
            out arrayPayloadBytes,
            out diagnostic);

        if (arrayStatus is not EbinWriteStatus.Complete)
        {
            return arrayStatus;
        }

        recordStorageBytes = checked((int)totalRecordStorageBytes);
        maximumCandidateBytes = maximumChildCandidateBytes;
        return EbinWriteStatus.Complete;
    }

    internal static EbinWriteStatus Write(
        scoped ReadOnlySpan<SceneExternalBindingCaptureRow> rows,
        EbinReadLimits limits,
        scoped Span<EbinPayloadSlice> recordSlices,
        scoped Span<byte> recordStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> arrayPayloadDestination,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = Measure(
            rows,
            limits,
            out var requiredRecordStorageBytes,
            out var requiredCandidateBytes,
            out var requiredPayloadBytes,
            out diagnostic);
        payloadBytes = 0;

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if ((recordSlices.Length != rows.Length)
            || (recordStorage.Length != requiredRecordStorageBytes)
            || (candidateStorage.Length != requiredCandidateBytes)
            || (arrayPayloadDestination.Length != requiredPayloadBytes))
        {
            throw new ArgumentException("The external-scene-binding array workspaces must match the measured lengths.");
        }

        ValidateNonoverlappingBuffers(recordSlices, recordStorage, candidateStorage, arrayPayloadDestination);
        var storageOffset = 0;

        for (var index = 0; index < rows.Length; index++)
        {
            var measureStatus = ScenePersistenceExternalBindingWriter.Measure(
                rows[index],
                limits,
                out var childRecordBytes,
                out diagnostic);

            if (measureStatus is not EbinWriteStatus.Complete)
            {
                return measureStatus;
            }

            var statusForRecord = ScenePersistenceExternalBindingWriter.Write(
                rows[index],
                limits,
                candidateStorage[..childRecordBytes],
                recordStorage.Slice(storageOffset, childRecordBytes),
                out var bytesWritten,
                out diagnostic);

            if (statusForRecord is not EbinWriteStatus.Complete)
            {
                return statusForRecord;
            }

            if (bytesWritten != childRecordBytes)
            {
                throw new InvalidOperationException("The external-scene-binding record extent changed after workspace measurement.");
            }

            recordSlices[index] = new EbinPayloadSlice(storageOffset, bytesWritten);
            storageOffset = checked(storageOffset + bytesWritten);
        }

        if (storageOffset != requiredRecordStorageBytes)
        {
            throw new InvalidOperationException("The external-scene-binding array extent changed after workspace measurement.");
        }

        status = EbinPayloadWriter.WriteRecordArray(
            recordSlices,
            recordStorage,
            limits,
            arrayPayloadDestination,
            out payloadBytes,
            out diagnostic);

        if ((status is EbinWriteStatus.Complete) && (payloadBytes != requiredPayloadBytes))
        {
            diagnostic = CreateDiagnostic("external-scene-binding-array-size-mismatch");
            payloadBytes = 0;
            return EbinWriteStatus.InvalidRecord;
        }

        return status;
    }

    private static EbinStructuralDiagnostic CreateDiagnostic(string category)
        => new EbinStructuralDiagnostic(
            new ContentSchemaIdentityAssignment("state/external-scene-binding").SchemaId,
            1,
            category,
            null,
            0,
            1,
            EbinStructuralDiagnosticSource.Construction);

    private static void ValidateNonoverlappingBuffers(
        scoped ReadOnlySpan<EbinPayloadSlice> recordSlices,
        scoped ReadOnlySpan<byte> recordStorage,
        scoped ReadOnlySpan<byte> candidateStorage,
        scoped ReadOnlySpan<byte> arrayPayloadDestination)
    {
        if (MemoryMarshal.AsBytes(recordSlices).Overlaps(recordStorage)
            || MemoryMarshal.AsBytes(recordSlices).Overlaps(candidateStorage)
            || MemoryMarshal.AsBytes(recordSlices).Overlaps(arrayPayloadDestination)
            || recordStorage.Overlaps(candidateStorage)
            || recordStorage.Overlaps(arrayPayloadDestination)
            || candidateStorage.Overlaps(arrayPayloadDestination))
        {
            throw new ArgumentException("The external-scene-binding array workspaces must not overlap.");
        }
    }
}
