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
        var emptyArrayPayload = new byte[16];
        Assert.That(
            EbinPayloadWriter.WriteRecordArray([], [], limits, emptyArrayPayload, out var arrayBytesWritten, out var diagnostic),
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
        var assertions = new ScenePersistenceScenesRecordAssertions(hierarchyRecord);
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
    }
}
