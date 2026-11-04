// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using NUnit.Framework;
using TerraFX;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

[TestFixture]
internal sealed class ScenePersistenceScenesWriterTests
{
    [Test]
    public void WritesCanonicalEmptyScenesRootWithItsHierarchyRecord()
    {
        var limits = EbinReadLimits.Foundation;
        SceneInstance[] emptyInstances = [];
        EbinPayloadSlice[] noInstanceRecords = [];
        byte[] emptyInstanceRecordStorage = [];
        int[] emptyOrderScratch = [];
        EbinPayloadSlice[] emptyOrderedRecords = [];
        Assert.That(
            ScenePersistenceSceneInstanceArrayWriter.Measure(
                emptyInstances,
                noInstanceRecords,
                emptyInstanceRecordStorage,
                limits,
                emptyOrderScratch,
                emptyOrderedRecords,
                out var instanceArrayWorkspace,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        var emptyArrayPayload = new byte[instanceArrayWorkspace.ArrayPayloadBytes];
        Assert.That(
            ScenePersistenceSceneInstanceArrayWriter.Write(
                emptyInstances,
                noInstanceRecords,
                emptyInstanceRecordStorage,
                limits,
                instanceArrayWorkspace,
                emptyOrderScratch,
                emptyOrderedRecords,
                emptyArrayPayload,
                out var arrayBytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(arrayBytesWritten, Is.EqualTo(emptyArrayPayload.Length));
        Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(emptyArrayPayload), Is.Zero);

        var hierarchyRootPayload = new byte[48];
        Assert.That(
            ScenePersistenceWorldHierarchyWriter.Measure(
                nodeCount: 0,
                emptyArrayPayload,
                [],
                limits,
                hierarchyRootPayload,
                out var hierarchyWorkspace,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        var hierarchyCandidateStorage = new byte[hierarchyWorkspace.RecordBytes];
        var hierarchyRecord = new byte[hierarchyWorkspace.RecordBytes];
        Assert.That(
            ScenePersistenceWorldHierarchyWriter.Write(
                hierarchyWorkspace,
                nodeCount: 0,
                emptyArrayPayload,
                [],
                limits,
                hierarchyRootPayload,
                hierarchyCandidateStorage,
                hierarchyRecord,
                out var hierarchyBytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(hierarchyBytesWritten, Is.EqualTo(hierarchyRecord.Length));

        var scenesRootPayload = new byte[checked(emptyArrayPayload.Length + hierarchyRecord.Length)];
        Assert.That(
            ScenePersistenceScenesWriter.Measure(
                instanceCount: 0,
                emptyArrayPayload,
                hierarchyRecord,
                limits,
                scenesRootPayload,
                out var scenesWorkspace,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        var scenesCandidateStorage = new byte[scenesWorkspace.RecordBytes];
        var scenesRecord = new byte[scenesWorkspace.RecordBytes];
        Assert.That(
            ScenePersistenceScenesWriter.Write(
                scenesWorkspace,
                instanceCount: 0,
                emptyArrayPayload,
                hierarchyRecord,
                limits,
                scenesRootPayload,
                scenesCandidateStorage,
                scenesRecord,
                out var scenesBytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(scenesBytesWritten, Is.EqualTo(scenesRecord.Length));
        var assertions = new ScenePersistenceScenesRecordAssertions(emptyArrayPayload, [], hierarchyRecord);
        Assert.That(
            EbinStructuralReader.Read(
                scenesRecord,
                limits,
                EbinReadPolicy.Canonical,
                ref assertions,
                out var scenesBytesConsumed,
                out var readDiagnostic),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(readDiagnostic, Is.Null);
        Assert.That(assertions.IsMatch, Is.True);
        Assert.That(scenesBytesConsumed, Is.EqualTo(scenesRecord.Length));
        var decoded = WorldPersistenceScenesReader.Read(
            scenesRecord,
            limits,
            maximumActiveSceneInstances: 65_536,
            maximumLiveEntities: 1_048_576,
            maximumIdentityAssignments: 33_554_432,
            maximumSceneOccurrences: 1_048_576);
        Assert.That(decoded.InstanceCount, Is.Zero);
        Assert.That(decoded.HierarchyNodeCount, Is.Zero);
        Assert.That(decoded.RootCount, Is.Zero);
    }
}
