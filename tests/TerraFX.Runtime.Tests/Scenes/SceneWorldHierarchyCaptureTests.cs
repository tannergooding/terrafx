// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

[TestFixture]
internal sealed class SceneWorldHierarchyCaptureTests
{
    private static CompiledContentSchemaRegistry CreateSchemaRegistry()
        => new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
        ]);

    [Test]
    public void CapturesCanonicalWorldHierarchyFromThePersistenceLease()
    {
        var builder = new WorldRegistrationBuilder(CreateSchemaRegistry());
        var sceneSupport = SceneSupport.CreateRegistration();
        _ = builder.RegisterSceneSupport(sceneSupport);
        using var world = World.Create(new WorldOptions(builder.Build()));
        var participant = sceneSupport.GetParticipant(world);
        var rootEntityId = new Guid("90000000-0000-4000-8000-000000000001");
        var firstChildEntityId = new Guid("20000000-0000-4000-8000-000000000002");
        var secondChildEntityId = new Guid("70000000-0000-4000-8000-000000000003");
        var otherRootEntityId = new Guid("10000000-0000-4000-8000-000000000004");
        var root = world.CreateEntity(new EntityDescription(persistentId: rootEntityId));
        var secondChild = world.CreateEntity(new EntityDescription(persistentId: secondChildEntityId));
        var firstChild = world.CreateEntity(new EntityDescription(persistentId: firstChildEntityId));
        _ = world.CreateEntity(new EntityDescription(persistentId: otherRootEntityId));
        world.ApplyHierarchyParentChange(participant, secondChild, root);
        world.ApplyHierarchyParentChange(participant, firstChild, root);
        _ = world.Step();

        using var lease = world.AcquirePersistenceCaptureLease();
        var entityRows = new World.EntityStateCaptureRow[lease.GetEntityStateCount()];
        var hierarchyRows = new SceneWorldHierarchyCaptureRow[entityRows.Length];
        ScenePersistenceWorldHierarchyCapture.Capture(
            world,
            lease,
            entityRows,
            hierarchyRows,
            out var rootCount,
            out var childCount);
        Assert.That(rootCount, Is.EqualTo(2));
        Assert.That(childCount, Is.EqualTo(2));
        Assert.That(hierarchyRows, Has.Length.EqualTo(4));
        var capturedEntityIds = new Guid[hierarchyRows.Length];

        for (var index = 0; index < hierarchyRows.Length; index++)
        {
            capturedEntityIds[index] = hierarchyRows[index].EntityId;
        }

        Guid[] expectedEntityIds = [otherRootEntityId, firstChildEntityId, secondChildEntityId, rootEntityId];
        Assert.That(capturedEntityIds, Is.EqualTo(expectedEntityIds));

        var roots = new Guid[rootCount];
        var children = new Guid[childCount];
        ScenePersistenceWorldHierarchyCapture.CopyCanonicalLists(hierarchyRows, roots, children);

        Guid[] expectedRootIds = [otherRootEntityId, rootEntityId];
        Guid[] expectedChildIds = [firstChildEntityId, secondChildEntityId];
        Assert.That(roots, Is.EqualTo(expectedRootIds));
        Assert.That(children, Is.EqualTo(expectedChildIds));
        Assert.That(hierarchyRows[1].ParentEntityId, Is.EqualTo(rootEntityId));
        Assert.That(hierarchyRows[2].ParentEntityId, Is.EqualTo(rootEntityId));
        Assert.That(hierarchyRows[3].ChildCount, Is.EqualTo(2));

        var limits = EbinReadLimits.Foundation;
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
                children,
                limits,
                nodePayloadStorage,
                out var nodeArrayWorkspace,
                out var nodeArrayDiagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            nodeArrayDiagnostic?.Category);
        Assert.That(nodeArrayDiagnostic, Is.Null);
        var nodeArrayPayload = new byte[nodeArrayWorkspace.ArrayPayloadBytes];
        var nodeReservationBytes = checked(
            nodeArrayWorkspace.WorkspaceBytes
            + ((long)entityRows.Length * Unsafe.SizeOf<World.EntityStateCaptureRow>()));
        Assert.That(lease.TryReserveAdmissionBytes(nodeReservationBytes, out var nodeReservation), Is.True);
        Assert.That(nodeReservation, Is.Not.Null);

        using (nodeReservation)
        {
            var recordSlices = new EbinPayloadSlice[nodeArrayWorkspace.NodeCount];
            var nodeRecordStorage = new byte[nodeArrayWorkspace.RecordStorageBytes];
            var nodeCandidateStorage = new byte[nodeArrayWorkspace.MaximumCandidateBytes];
            Assert.That(
                ScenePersistenceWorldHierarchyNodeArrayWriter.Write(
                    hierarchyRows,
                    children,
                    limits,
                    nodeArrayWorkspace,
                    recordSlices,
                    nodeRecordStorage,
                    nodeCandidateStorage,
                    nodePayloadStorage,
                    nodeArrayPayload,
                    out var nodeArrayBytesWritten,
                    out nodeArrayDiagnostic),
                Is.EqualTo(EbinWriteStatus.Complete),
                nodeArrayDiagnostic?.Category);
            Assert.That(nodeArrayDiagnostic, Is.Null);
            Assert.That(nodeArrayBytesWritten, Is.EqualTo(nodeArrayPayload.Length));

            var rootPayloadStorage = new byte[checked(nodeArrayPayload.Length + 32 + (roots.Length * 16))];
            Assert.That(
                ScenePersistenceWorldHierarchyWriter.Measure(
                    hierarchyRows.Length,
                    nodeArrayPayload,
                    roots,
                    limits,
                    rootPayloadStorage,
                    out var hierarchyWorkspace,
                    out var hierarchyDiagnostic),
                Is.EqualTo(EbinWriteStatus.Complete),
                hierarchyDiagnostic?.Category);
            Assert.That(hierarchyDiagnostic, Is.Null);
            var rootReservationBytes = checked(hierarchyWorkspace.WorkspaceBytes - nodeArrayPayload.Length);
            Assert.That(lease.TryReserveAdmissionBytes(rootReservationBytes, out var rootReservation), Is.True);
            Assert.That(rootReservation, Is.Not.Null);

            using (rootReservation)
            {
                var rootCandidateStorage = new byte[hierarchyWorkspace.RecordBytes];
                var hierarchyRecord = new byte[hierarchyWorkspace.RecordBytes];
                Assert.That(
                    ScenePersistenceWorldHierarchyWriter.Write(
                        hierarchyWorkspace,
                        hierarchyRows.Length,
                        nodeArrayPayload,
                        roots,
                        limits,
                        rootPayloadStorage,
                        rootCandidateStorage,
                        hierarchyRecord,
                        out var hierarchyBytesWritten,
                        out hierarchyDiagnostic),
                    Is.EqualTo(EbinWriteStatus.Complete),
                    hierarchyDiagnostic?.Category);
                Assert.That(hierarchyDiagnostic, Is.Null);
                Assert.That(hierarchyBytesWritten, Is.EqualTo(hierarchyRecord.Length));
                var assertions = new SceneWorldHierarchyRecordAssertions(hierarchyRows, roots, children);
                Assert.That(
                    EbinStructuralReader.Read(
                        hierarchyRecord,
                        limits,
                        EbinReadPolicy.Canonical,
                        ref assertions,
                        out var bytesConsumed,
                        out var readDiagnostic),
                    Is.EqualTo(EbinReadStatus.Complete));
                Assert.That(readDiagnostic, Is.Null);
                Assert.That(assertions.IsMatch, Is.True);
                Assert.That(bytesConsumed, Is.EqualTo(hierarchyRecord.Length));
            }
        }
    }

    [Test]
    public void CapturesEveryEntityAsARootWithoutSceneSupport()
    {
        var builder = new WorldRegistrationBuilder(CreateSchemaRegistry());
        using var world = World.Create(new WorldOptions(builder.Build()));
        var entityId = new Guid("50000000-0000-4000-8000-000000000005");
        _ = world.CreateEntity(new EntityDescription(persistentId: entityId));
        _ = world.Step();

        using var lease = world.AcquirePersistenceCaptureLease();
        var entityRows = new World.EntityStateCaptureRow[lease.GetEntityStateCount()];
        var hierarchyRows = new SceneWorldHierarchyCaptureRow[entityRows.Length];
        ScenePersistenceWorldHierarchyCapture.Capture(
            world,
            lease,
            entityRows,
            hierarchyRows,
            out var rootCount,
            out var childCount);
        var roots = new Guid[rootCount];
        var children = new Guid[childCount];
        ScenePersistenceWorldHierarchyCapture.CopyCanonicalLists(hierarchyRows, roots, children);

        Guid[] expectedRootIds = [entityId];
        Assert.That(rootCount, Is.EqualTo(1));
        Assert.That(childCount, Is.Zero);
        Assert.That(roots, Is.EqualTo(expectedRootIds));
        Assert.That(children, Is.Empty);
    }

    [Test]
    public void WritesCanonicalEmptyWorldHierarchy()
    {
        var limits = EbinReadLimits.Foundation;
        var hierarchyRows = Array.Empty<SceneWorldHierarchyCaptureRow>();
        var childEntityIds = Array.Empty<Guid>();
        var nodePayloadStorage = Array.Empty<byte>();
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
        var recordSlices = Array.Empty<EbinPayloadSlice>();
        var nodeRecordStorage = Array.Empty<byte>();
        var nodeCandidateStorage = Array.Empty<byte>();
        var nodeArrayPayload = new byte[nodeArrayWorkspace.ArrayPayloadBytes];
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
        var rootEntityIds = Array.Empty<Guid>();
        var rootPayloadStorage = new byte[48];
        Assert.That(
            ScenePersistenceWorldHierarchyWriter.Measure(
                0,
                nodeArrayPayload,
                rootEntityIds,
                limits,
                rootPayloadStorage,
                out var workspace,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        var candidateStorage = new byte[workspace.RecordBytes];
        var destination = new byte[workspace.RecordBytes];
        Assert.That(
            ScenePersistenceWorldHierarchyWriter.Write(
                workspace,
                0,
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
        var assertions = new SceneWorldHierarchyRecordAssertions([], [], []);
        Assert.That(
            EbinStructuralReader.Read(
                destination,
                limits,
                EbinReadPolicy.Canonical,
                ref assertions,
                out var bytesConsumed,
                out var readDiagnostic),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(readDiagnostic, Is.Null);
        Assert.That(assertions.IsMatch, Is.True);
        Assert.That(bytesConsumed, Is.EqualTo(destination.Length));
    }
}
