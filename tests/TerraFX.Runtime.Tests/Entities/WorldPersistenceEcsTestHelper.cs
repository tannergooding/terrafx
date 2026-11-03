// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

internal static class WorldPersistenceEcsTestHelper
{
    internal static int CaptureEcsRecord<TAction>(
        World world,
        in WorldPersistenceCaptureLease lease,
        ref TAction assertions)
        where TAction : struct, IAction<EbinRecordReadView>
    {
        var buffers = CreateMeasurementBuffers(in lease);
        Assert.That(
            lease.MeasureEcsRecord(
                ref buffers,
                out var workspace,
                out var diagnostic,
                CancellationToken.None),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(lease.TryReserveAdmissionBytes(workspace.WorkspaceBytes, out var reservation), Is.True);
        Assert.That(reservation, Is.Not.Null);
        using var reservationScope = reservation!;
        AllocateWriteBuffers(ref buffers, workspace);
        Assert.That(buffers.GetWorkspaceBytes(), Is.EqualTo(workspace.WorkspaceBytes));

        Assert.That(
            lease.WriteEcsRecord(
                reservationScope,
                ref buffers,
                out var bytesWritten,
                out diagnostic,
                CancellationToken.None),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(bytesWritten, Is.EqualTo(workspace.RecordBytes));
        Assert.That(
            EbinStructuralReader.Read(
                buffers.Destination[..bytesWritten],
                WorldPersistenceEbinLimits.Create(world.Options),
                EbinReadPolicy.Canonical,
                ref assertions,
                out var bytesConsumed,
                out var readDiagnostic),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(readDiagnostic, Is.Null);
        Assert.That(bytesConsumed, Is.EqualTo(bytesWritten));
        return bytesWritten;
    }

    internal static byte[] CopyEcsRecord(World world, in WorldPersistenceCaptureLease lease)
    {
        var assertions = new EcsRecordCopyAction();
        var bytesWritten = CaptureEcsRecord(world, in lease, ref assertions);
        var record = assertions.Record
            ?? throw new InvalidOperationException("The capture helper did not return the actual ECS record.");

        if (record.Length != bytesWritten)
        {
            throw new InvalidOperationException("The captured ECS record length changed during copying.");
        }

        return record;
    }

    private static void AllocateWriteBuffers(
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
        buffers.Destination = new byte[workspace.RecordBytes];
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

    private struct EcsRecordCopyAction : IAction<EbinRecordReadView>
    {
        internal byte[]? Record { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
            => Record = record.EncodedBytes.ToArray();
    }
}
