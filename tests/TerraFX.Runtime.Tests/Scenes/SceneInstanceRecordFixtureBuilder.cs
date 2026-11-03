// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

internal static class SceneInstanceRecordFixtureBuilder
{
    internal static byte[] Create(SceneInstance instance, WorldPersistenceCaptureLease lease)
    {
        var limits = EbinReadLimits.Foundation;
        var liveEntityCount = instance.GetPersistenceEntityCount(lease);
        var entityIds = new Guid[liveEntityCount];
        instance.CopyPersistenceEntityIds(lease, entityIds);
        var entityIdsPayload = new byte[checked(32 + (entityIds.Length * 16))];
        Assert.That(
            EbinPayloadWriter.WriteFixedArray(entityIds.AsSpan(), limits, entityIdsPayload, out var entityIdsPayloadBytes, out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(entityIdsPayloadBytes, Is.EqualTo(entityIdsPayload.Length));

        var bindingRecords = new byte[liveEntityCount][];
        var bindingIndex = 0;

        for (var index = 0; index < instance.EntityCount; index++)
        {
            if (!instance.TryGetPersistenceEntityId(lease, index, out _))
            {
                continue;
            }

            Assert.That(
                instance.MeasurePersistenceEntityBindingRecord(lease, index, out var bindingRecordBytes, out diagnostic),
                Is.EqualTo(EbinWriteStatus.Complete),
                diagnostic?.Category);
            Assert.That(diagnostic, Is.Null);
            var bindingCandidateStorage = new byte[bindingRecordBytes];
            var bindingRecord = new byte[bindingRecordBytes];
            Assert.That(
                instance.WritePersistenceEntityBindingRecord(
                    lease,
                    index,
                    bindingCandidateStorage,
                    bindingRecord,
                    out var bindingBytesWritten,
                    out diagnostic),
                Is.EqualTo(EbinWriteStatus.Complete),
                diagnostic?.Category);
            Assert.That(diagnostic, Is.Null);
            Assert.That(bindingBytesWritten, Is.EqualTo(bindingRecord.Length));
            bindingRecords[bindingIndex++] = bindingRecord;
        }

        if (bindingIndex != bindingRecords.Length)
        {
            throw new InvalidOperationException("The captured Scene binding count changed during fixture construction.");
        }

        var bindingsArrayPayload = SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayPayload(bindingRecords);

        var externalBindings = instance.GetExternalBindingCaptureRows(lease);
        Assert.That(externalBindings.Length, Is.Zero);
        Assert.That(
            ScenePersistenceExternalBindingArrayWriter.Measure(
                externalBindings,
                limits,
                out var externalRecordStorageBytes,
                out var externalCandidateBytes,
                out var externalArrayPayloadBytes,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        var externalSlices = new EbinPayloadSlice[externalBindings.Length];
        var externalRecordStorage = new byte[externalRecordStorageBytes];
        var externalCandidateStorage = new byte[externalCandidateBytes];
        var externalBindingsPayload = new byte[externalArrayPayloadBytes];
        Assert.That(
            ScenePersistenceExternalBindingArrayWriter.Write(
                externalBindings,
                limits,
                externalSlices,
                externalRecordStorage,
                externalCandidateStorage,
                externalBindingsPayload,
                out var externalPayloadBytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(externalPayloadBytesWritten, Is.EqualTo(externalBindingsPayload.Length));

        var occurrenceRecords = new byte[instance.OccurrenceCount][];

        for (var index = 0; index < instance.OccurrenceCount; index++)
        {
            Assert.That(
                instance.MeasurePersistenceOccurrenceRecord(lease, index, out var occurrenceRecordBytes, out diagnostic),
                Is.EqualTo(EbinWriteStatus.Complete),
                diagnostic?.Category);
            Assert.That(diagnostic, Is.Null);
            var occurrenceCandidateStorage = new byte[occurrenceRecordBytes];
            var occurrenceRecord = new byte[occurrenceRecordBytes];
            Assert.That(
                instance.WritePersistenceOccurrenceRecord(
                    lease,
                    index,
                    occurrenceCandidateStorage,
                    occurrenceRecord,
                    out var occurrenceBytesWritten,
                    out diagnostic),
                Is.EqualTo(EbinWriteStatus.Complete),
                diagnostic?.Category);
            Assert.That(diagnostic, Is.Null);
            Assert.That(occurrenceBytesWritten, Is.EqualTo(occurrenceRecord.Length));
            occurrenceRecords[index] = occurrenceRecord;
        }

        var occurrenceArrayPayload = SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayPayload(occurrenceRecords);
        var plan = instance.GetPersistencePlan(lease);
        Assert.That(
            ScenePersistenceOccurrenceWriter.MeasureSourceAssetReference(
                plan,
                0,
                limits,
                out var sourceRecordBytes,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        var rootPayloadStorage = new byte[checked(
            16
            + sourceRecordBytes
            + entityIdsPayload.Length
            + bindingsArrayPayload.Length
            + externalBindingsPayload.Length
            + sizeof(long)
            + occurrenceArrayPayload.Length)];
        Assert.That(
            ScenePersistenceSceneInstanceWriter.Measure(
                instance,
                lease,
                entityIdsPayload,
                bindingsArrayPayload,
                externalBindingsPayload,
                occurrenceArrayPayload,
                limits,
                rootPayloadStorage,
                out var workspace,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(lease.TryReserveAdmissionBytes(workspace.WorkspaceBytes, out var reservation), Is.True);
        Assert.That(reservation, Is.Not.Null);

        using (reservation)
        {
            var candidateStorage = new byte[workspace.RecordBytes];
            var destination = new byte[workspace.RecordBytes];
            Assert.That(
                ScenePersistenceSceneInstanceWriter.Write(
                    instance,
                    lease,
                    workspace,
                    entityIdsPayload,
                    bindingsArrayPayload,
                    externalBindingsPayload,
                    occurrenceArrayPayload,
                    limits,
                    rootPayloadStorage,
                    candidateStorage,
                    destination,
                    out var bytesWritten,
                    out diagnostic),
                Is.EqualTo(EbinWriteStatus.Complete),
                diagnostic?.Category);
            Assert.That(diagnostic, Is.Null);
            Assert.That(bytesWritten, Is.EqualTo(destination.Length));
            return destination;
        }
    }

    internal static byte[] CreateWorldHierarchyRecord(World world, WorldPersistenceCaptureLease lease)
    {
        var limits = EbinReadLimits.Foundation;
        var entityRows = new World.EntityStateCaptureRow[lease.GetEntityStateCount()];
        var hierarchyRows = new SceneWorldHierarchyCaptureRow[entityRows.Length];
        ScenePersistenceWorldHierarchyCapture.Capture(
            world,
            lease,
            entityRows,
            hierarchyRows,
            out var rootCount,
            out var childCount);
        var rootEntityIds = new Guid[rootCount];
        var childEntityIds = new Guid[childCount];
        ScenePersistenceWorldHierarchyCapture.CopyCanonicalLists(
            world,
            lease,
            entityRows,
            hierarchyRows,
            rootEntityIds,
            childEntityIds);
        var nodePayloadBytes = 0;

        foreach (var row in hierarchyRows)
        {
            nodePayloadBytes = Math.Max(
                nodePayloadBytes,
                ScenePersistenceWorldHierarchyNodeWriter.GetPayloadBytes(row.ParentEntityId.HasValue, row.ChildCount));
        }

        var nodePayloadStorage = new byte[nodePayloadBytes];
        Assert.That(
            ScenePersistenceWorldHierarchyNodeArrayWriter.Measure(
                hierarchyRows,
                childEntityIds,
                limits,
                nodePayloadStorage,
                out var nodeArrayWorkspace,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        var nodeArrayPayload = new byte[nodeArrayWorkspace.ArrayPayloadBytes];
        var captureWorkspaceBytes = checked(
            nodeArrayWorkspace.WorkspaceBytes
            + ((long)entityRows.Length * Unsafe.SizeOf<World.EntityStateCaptureRow>()));
        Assert.That(lease.TryReserveAdmissionBytes(captureWorkspaceBytes, out var captureReservation), Is.True);
        Assert.That(captureReservation, Is.Not.Null);

        using (captureReservation)
        {
            var recordSlices = new EbinPayloadSlice[nodeArrayWorkspace.NodeCount];
            var nodeRecordStorage = new byte[nodeArrayWorkspace.RecordStorageBytes];
            var nodeCandidateStorage = new byte[nodeArrayWorkspace.MaximumCandidateBytes];
            Assert.That(
                ScenePersistenceWorldHierarchyNodeArrayWriter.Write(
                    hierarchyRows,
                    childEntityIds,
                    limits,
                    nodeArrayWorkspace,
                    recordSlices,
                    nodeRecordStorage,
                    nodeCandidateStorage,
                    nodePayloadStorage,
                    nodeArrayPayload,
                    out var nodeArrayBytesWritten,
                    out diagnostic),
                Is.EqualTo(EbinWriteStatus.Complete),
                diagnostic?.Category);
            Assert.That(diagnostic, Is.Null);
            Assert.That(nodeArrayBytesWritten, Is.EqualTo(nodeArrayPayload.Length));
            var rootPayloadStorage = new byte[checked(
                nodeArrayPayload.Length
                + 32
                + (rootEntityIds.Length * 16))];
            Assert.That(
                ScenePersistenceWorldHierarchyWriter.Measure(
                    hierarchyRows.Length,
                    nodeArrayPayload,
                    rootEntityIds,
                    limits,
                    rootPayloadStorage,
                    out var hierarchyWorkspace,
                    out diagnostic),
                Is.EqualTo(EbinWriteStatus.Complete),
                diagnostic?.Category);
            Assert.That(diagnostic, Is.Null);
            var rootWorkspaceBytes = checked(hierarchyWorkspace.WorkspaceBytes - nodeArrayPayload.Length);
            Assert.That(lease.TryReserveAdmissionBytes(rootWorkspaceBytes, out var rootReservation), Is.True);
            Assert.That(rootReservation, Is.Not.Null);

            using (rootReservation)
            {
                var candidateStorage = new byte[hierarchyWorkspace.RecordBytes];
                var destination = new byte[hierarchyWorkspace.RecordBytes];
                Assert.That(
                    ScenePersistenceWorldHierarchyWriter.Write(
                        hierarchyWorkspace,
                        hierarchyRows.Length,
                        nodeArrayPayload,
                        rootEntityIds,
                        limits,
                        rootPayloadStorage,
                        candidateStorage,
                        destination,
                        out var bytesWritten,
                        out diagnostic),
                    Is.EqualTo(EbinWriteStatus.Complete),
                    diagnostic?.Category);
                Assert.That(diagnostic, Is.Null);
                Assert.That(bytesWritten, Is.EqualTo(destination.Length));
                return destination;
            }
        }
    }
}
