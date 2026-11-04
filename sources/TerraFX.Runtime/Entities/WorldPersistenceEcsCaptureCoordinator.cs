// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceEcsCaptureCoordinator
{
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
        var buffers = CreateMeasurementBuffers(in lease);
        var status = lease.MeasureEcsRecord(
            ref buffers,
            out var workspace,
            out diagnostic,
            cancellationToken);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var destination = AllocateWriteBuffers(ref buffers, workspace);
        var workspaceBytes = buffers.GetWorkspaceBytes();

        if (workspaceBytes != workspace.WorkspaceBytes)
        {
            throw new InvalidOperationException("The ECS capture buffers do not match their measured workspace.");
        }

        var outputWorkspaceBytes = (long)workspace.RecordBytes;
        var scratchWorkspaceBytes = checked(workspaceBytes - outputWorkspaceBytes);

        if (!lease.TryReserveAdmissionBytes(scratchWorkspaceBytes, out var scratchReservation))
        {
            throw new InvalidOperationException("The ECS capture workspace was refused by admission.");
        }

        var scratchReservationScope = scratchReservation
            ?? throw new InvalidOperationException("The ECS scratch reservation was not created.");

        if (!lease.TryReserveAdmissionBytes(outputWorkspaceBytes, out var outputReservation))
        {
            scratchReservationScope.Dispose();
            throw new InvalidOperationException("The ECS record reservation was refused by admission.");
        }

        var outputReservationScope = outputReservation
            ?? throw new InvalidOperationException("The ECS record reservation was not created.");

        try
        {
            using (scratchReservationScope.AcquireUse(world, scratchWorkspaceBytes))
            using (outputReservationScope.AcquireUse(world, outputWorkspaceBytes))
            {
                status = WorldPersistenceEcsWriter.Write(
                    lease,
                    ref buffers,
                    WorldPersistenceEbinLimits.Create(world.Options),
                    out var bytesWritten,
                    out diagnostic,
                    cancellationToken);

                if (status is not EbinWriteStatus.Complete)
                {
                    return status;
                }

                if (bytesWritten != workspace.RecordBytes)
                {
                    throw new InvalidOperationException("The ECS record extent changed after capture measurement.");
                }

                record = destination;
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

    private static byte[] AllocateWriteBuffers(
        ref WorldPersistenceEcsBuffers buffers,
        WorldPersistenceEcsWorkspaceRequirements workspace)
    {
        buffers.AttachmentStorage = new byte[workspace.AttachmentStorageBytes];
        buffers.EcsCandidateStorage = new byte[workspace.RecordBytes];
        buffers.EntityCandidateStorage = new byte[workspace.EntityCandidateStorageBytes];
        buffers.EntityChildCandidateStorage = new byte[workspace.EntityChildCandidateBytes];
        buffers.EntityComponentStorage = new byte[workspace.EntityComponentStorageBytes];
        buffers.EntityPayloadStorage = new byte[workspace.EntityPayloadStorageBytes];
        buffers.EntityStorage = new byte[workspace.EntityStorageBytes];
        buffers.EntityTagStorage = new byte[workspace.EntityTagStorageBytes];
        buffers.EntityTransientSchemaStorage = new byte[workspace.EntityTransientSchemaStorageBytes];
        buffers.IdentityBranchStorage = new byte[workspace.IdentityHistory.BranchStorageBytes];
        buffers.IdentityCandidateStorage = new byte[workspace.IdentityHistory.RecordBytes];
        buffers.IdentityChildCandidateStorage = new byte[workspace.IdentityHistory.ChildCandidateBytes];
        buffers.IdentityPageStorage = new byte[workspace.IdentityHistory.PageStorageBytes];
        buffers.IdentityRootPayloadStorage = new byte[workspace.IdentityHistory.RootPayloadStorageBytes];
        buffers.ResourceStorage = new byte[workspace.ResourceStorageBytes];
        buffers.RootPayloadStorage = new byte[workspace.RootPayloadStorageBytes];
        buffers.StatePayloadRecordStorage = new byte[workspace.StatePayloadRecordStorageBytes];
        buffers.StateRowCandidateStorage = new byte[workspace.StateRowCandidateStorageBytes];
        buffers.StateRowPayloadStorage = new byte[workspace.StateRowPayloadStorageBytes];
        buffers.SystemStorage = new byte[workspace.SystemStorageBytes];
        var destination = new byte[workspace.RecordBytes];
        buffers.Destination = destination;
        return destination;
    }

    private static WorldPersistenceEcsBuffers CreateMeasurementBuffers(in WorldPersistenceCaptureLease lease)
    {
        var history = lease.GetIdentityHistoryCapture();
        var pageWorkspaceBytes = (history.PageCount == 0)
            ? 0
            : WorldPersistenceIdentityPageWriter.GetMinimumPayloadWorkspaceBytes(StableIdentityLedger.RecordsPerPage);
        var pageRecords = (history.PageCount == 0) ? 0 : StableIdentityLedger.RecordsPerPage;
        var buffers = default(WorldPersistenceEcsBuffers);
        buffers.EntityRows = new World.EntityStateCaptureRow[lease.GetEntityStateCount()];
        buffers.EntitySlices = new EbinPayloadSlice[buffers.EntityRows.Length];
        buffers.SystemSlices = new EbinPayloadSlice[lease.GetSystemStateCount()];
        buffers.ResourceSlices = new EbinPayloadSlice[lease.GetResourceStateCount()];
        buffers.AttachmentRows = new World.AttachmentStateCaptureRow[lease.GetAttachmentStateCount()];
        buffers.AttachmentSlices = new EbinPayloadSlice[buffers.AttachmentRows.Length];
        buffers.EntityComponentSlices = new EbinPayloadSlice[WorldPersistenceEcsWorkspaceRequirements.MaximumEntitySchemaRecordCount];
        buffers.EntityTagSlices = new EbinPayloadSlice[WorldPersistenceEcsWorkspaceRequirements.MaximumEntitySchemaRecordCount];
        buffers.EntityTransientSchemaSlices = new EbinPayloadSlice[WorldPersistenceEcsWorkspaceRequirements.MaximumEntitySchemaRecordCount];
        buffers.IdentityBranchSlices = new EbinPayloadSlice[history.BranchCount];
        buffers.IdentityPageSlices = new EbinPayloadSlice[history.PageCount];
        buffers.IdentityPageStableIds = new Guid[pageRecords];
        buffers.IdentityPageMetadata = new uint[pageRecords];
        buffers.IdentityPagePayloadStorage = new byte[pageWorkspaceBytes];
        return buffers;
    }
}
