// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceControlRecordArrayWriter
{
    internal static EbinWriteStatus Measure(
        WorldPersistenceCaptureLease lease,
        EbinReadLimits limits,
        out WorldPersistenceControlRecordArrayWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic)
        => MeasureCore(lease, limits, [], writeRecordSlices: false, out workspace, out diagnostic);

    internal static EbinWriteStatus Measure(
        WorldPersistenceCaptureLease lease,
        EbinReadLimits limits,
        scoped Span<EbinPayloadSlice> recordSlices,
        out WorldPersistenceControlRecordArrayWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic)
        => MeasureCore(lease, limits, recordSlices, writeRecordSlices: true, out workspace, out diagnostic);

    internal static EbinWriteStatus MeasureCore(
        WorldPersistenceCaptureLease lease,
        EbinReadLimits limits,
        scoped Span<EbinPayloadSlice> recordSlices,
        bool writeRecordSlices,
        out WorldPersistenceControlRecordArrayWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        workspace = default;
        diagnostic = null;
        lease.ValidateCurrent();

        var controlCount = lease.ControlRecordCount;

        if (writeRecordSlices && (recordSlices.Length != controlCount))
        {
            throw new ArgumentException("The control-record slice workspace must match the captured control count.", nameof(recordSlices));
        }

        if (lease.NextControlSequence != checked((long)controlCount + 1))
        {
            diagnostic = CreateDiagnostic(
                "next-control-sequence-mismatch",
                checked((ulong)lease.NextControlSequence),
                checked((ulong)controlCount + 1UL));
            return EbinWriteStatus.InvalidRecord;
        }

        var minimumArrayStatus = WorldPersistenceRecordArrayLayout.MeasurePayloadBytes(
            controlCount,
            0,
            limits,
            out _,
            out diagnostic);

        if (minimumArrayStatus is not EbinWriteStatus.Complete)
        {
            return minimumArrayStatus;
        }

        var recordStorageBytes = 0;
        var maximumCandidateRecordBytes = 0;
        var maximumRootPayloadBytes = 0;
        long maximumRecordWriterScratchBytes = 0;
        long previousAppliedAtNextTick = 0;

        for (var index = 0; index < controlCount; index++)
        {
            var control = lease.GetControlCapture(index);
            var expectedSequence = checked((long)index + 1);

            if (control.Sequence != expectedSequence)
            {
                diagnostic = CreateDiagnostic(
                    "control-sequence-order",
                    checked((ulong)control.Sequence),
                    checked((ulong)expectedSequence));
                return EbinWriteStatus.InvalidRecord;
            }

            if ((control.AppliedAtNextTick >= lease.NextTickIndex)
                || ((index > 0) && (control.AppliedAtNextTick < previousAppliedAtNextTick)))
            {
                diagnostic = CreateDiagnostic(
                    "control-tick-order",
                    checked((ulong)control.AppliedAtNextTick),
                    checked((ulong)lease.NextTickIndex));
                return EbinWriteStatus.InvalidRecord;
            }

            var status = WorldPersistenceControlRecordWriter.Measure(
                lease,
                control,
                limits,
                out var recordWorkspace,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            if (recordWorkspace.RecordBytes > (int.MaxValue - recordStorageBytes))
            {
                diagnostic = CreateDiagnostic(
                    "record-byte-budget",
                    checked((ulong)recordStorageBytes + (ulong)recordWorkspace.RecordBytes),
                    checked((ulong)limits.MaximumRecordBytes));
                return EbinWriteStatus.InvalidRecord;
            }

            if (writeRecordSlices)
            {
                recordSlices[index] = new EbinPayloadSlice(recordStorageBytes, recordWorkspace.RecordBytes);
            }

            recordStorageBytes = checked(recordStorageBytes + recordWorkspace.RecordBytes);
            maximumCandidateRecordBytes = Math.Max(maximumCandidateRecordBytes, recordWorkspace.RecordBytes);
            maximumRootPayloadBytes = Math.Max(maximumRootPayloadBytes, recordWorkspace.RootPayloadBytes);
            maximumRecordWriterScratchBytes = Math.Max(
                maximumRecordWriterScratchBytes,
                checked(recordWorkspace.WorkspaceBytes - (2L * recordWorkspace.RecordBytes)));
            previousAppliedAtNextTick = control.AppliedAtNextTick;
        }

        var arrayStatus = WorldPersistenceRecordArrayLayout.MeasurePayloadBytes(
            controlCount,
            recordStorageBytes,
            limits,
            out var arrayPayloadBytes,
            out diagnostic);

        if (arrayStatus is not EbinWriteStatus.Complete)
        {
            return arrayStatus;
        }

        var recordSlicesBytes = checked((long)controlCount * Unsafe.SizeOf<EbinPayloadSlice>());
        var workspaceBytes = checked(
            recordSlicesBytes
            + recordStorageBytes
            + maximumCandidateRecordBytes
            + maximumRootPayloadBytes
            + arrayPayloadBytes
            + maximumRecordWriterScratchBytes);
        workspace = new WorldPersistenceControlRecordArrayWorkspaceRequirements(
            controlCount,
            recordStorageBytes,
            maximumCandidateRecordBytes,
            maximumRootPayloadBytes,
            arrayPayloadBytes,
            maximumRecordWriterScratchBytes,
            workspaceBytes);
        return EbinWriteStatus.Complete;
    }

    internal static EbinWriteStatus Write(
        WorldPersistenceCaptureLease lease,
        EbinReadLimits limits,
        WorldPersistenceControlRecordArrayWorkspaceRequirements workspace,
        scoped Span<EbinPayloadSlice> recordSlices,
        scoped Span<byte> recordStorage,
        scoped Span<byte> candidateRecordStorage,
        scoped Span<byte> rootPayloadStorage,
        scoped Span<byte> arrayPayloadDestination,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        lease.ValidateCurrent();
        payloadBytes = 0;
        ValidateBuffers(
            workspace,
            recordSlices,
            recordStorage,
            candidateRecordStorage,
            rootPayloadStorage,
            arrayPayloadDestination);

        var measureStatus = Measure(
            lease,
            limits,
            recordSlices,
            out var measuredWorkspace,
            out diagnostic);

        if (measureStatus is not EbinWriteStatus.Complete)
        {
            return measureStatus;
        }

        if (!workspace.Matches(measuredWorkspace))
        {
            diagnostic = CreateDiagnostic("control-array-workspace-mismatch", 0, 1);
            return EbinWriteStatus.InvalidRecord;
        }

        for (var index = 0; index < workspace.ControlRecordCount; index++)
        {
            var control = lease.GetControlCapture(index);
            var slice = recordSlices[index];
            var measureStatusForRecord = WorldPersistenceControlRecordWriter.Measure(
                lease,
                control,
                limits,
                out var recordWorkspace,
                out diagnostic);

            if (measureStatusForRecord is not EbinWriteStatus.Complete)
            {
                return measureStatusForRecord;
            }

            if ((slice.Offset < 0)
                || (slice.ByteLength != recordWorkspace.RecordBytes)
                || (slice.Offset > recordStorage.Length)
                || (slice.ByteLength > (recordStorage.Length - slice.Offset)))
            {
                diagnostic = CreateDiagnostic("control-array-record-slice-mismatch", 0, 1);
                return EbinWriteStatus.InvalidRecord;
            }

            var status = WorldPersistenceControlRecordWriter.Write(
                lease,
                control,
                recordWorkspace,
                limits,
                rootPayloadStorage[..recordWorkspace.RootPayloadBytes],
                candidateRecordStorage[..recordWorkspace.RecordBytes],
                recordStorage.Slice(slice.Offset, slice.ByteLength),
                out var recordBytesWritten,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            if (recordBytesWritten != slice.ByteLength)
            {
                diagnostic = CreateDiagnostic(
                    "control-array-record-size-mismatch",
                    checked((ulong)recordBytesWritten),
                    checked((ulong)slice.ByteLength));
                return EbinWriteStatus.InvalidRecord;
            }
        }

        var arrayStatus = EbinPayloadWriter.WriteRecordArray(
            recordSlices,
            recordStorage,
            limits,
            arrayPayloadDestination,
            out payloadBytes,
            out diagnostic);

        if ((arrayStatus is EbinWriteStatus.Complete) && (payloadBytes != workspace.ArrayPayloadBytes))
        {
            diagnostic = CreateDiagnostic(
                "control-array-size-mismatch",
                checked((ulong)payloadBytes),
                checked((ulong)workspace.ArrayPayloadBytes));
            payloadBytes = 0;
            return EbinWriteStatus.InvalidRecord;
        }

        return arrayStatus;
    }

    private static EbinStructuralDiagnostic CreateDiagnostic(string category, ulong claim, ulong limit)
        => new EbinStructuralDiagnostic(
            new ContentSchemaIdentityAssignment("state/control").SchemaId,
            1,
            category,
            null,
            claim,
            limit,
            EbinStructuralDiagnosticSource.Construction);

    private static void ValidateBuffers(
        WorldPersistenceControlRecordArrayWorkspaceRequirements workspace,
        scoped ReadOnlySpan<EbinPayloadSlice> recordSlices,
        scoped ReadOnlySpan<byte> recordStorage,
        scoped ReadOnlySpan<byte> candidateRecordStorage,
        scoped ReadOnlySpan<byte> rootPayloadStorage,
        scoped ReadOnlySpan<byte> arrayPayloadDestination)
    {
        if ((recordSlices.Length != workspace.ControlRecordCount)
            || (recordStorage.Length != workspace.RecordStorageBytes)
            || (candidateRecordStorage.Length != workspace.MaximumCandidateRecordBytes)
            || (rootPayloadStorage.Length != workspace.MaximumRootPayloadBytes)
            || (arrayPayloadDestination.Length != workspace.ArrayPayloadBytes))
        {
            throw new ArgumentException("The control-record array buffers must match their measured extents.");
        }

        var recordSliceBytes = MemoryMarshal.AsBytes(recordSlices);

        if (recordSliceBytes.Overlaps(recordStorage)
            || recordSliceBytes.Overlaps(candidateRecordStorage)
            || recordSliceBytes.Overlaps(rootPayloadStorage)
            || recordSliceBytes.Overlaps(arrayPayloadDestination)
            || recordStorage.Overlaps(candidateRecordStorage)
            || recordStorage.Overlaps(rootPayloadStorage)
            || recordStorage.Overlaps(arrayPayloadDestination)
            || candidateRecordStorage.Overlaps(rootPayloadStorage)
            || candidateRecordStorage.Overlaps(arrayPayloadDestination)
            || rootPayloadStorage.Overlaps(arrayPayloadDestination))
        {
            throw new ArgumentException("The control-record array buffers must not overlap.");
        }
    }
}
