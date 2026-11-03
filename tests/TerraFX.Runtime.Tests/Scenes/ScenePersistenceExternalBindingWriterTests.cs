// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using NUnit.Framework;
using TerraFX;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

[TestFixture]
internal sealed class ScenePersistenceExternalBindingWriterTests
{
    [Test]
    public void RejectsEmptyBindingAndTargetIdentities()
    {
        Assert.That(
            ScenePersistenceExternalBindingWriter.Measure(
                new SceneExternalBindingCaptureRow(Guid.Empty, null),
                EbinReadLimits.Foundation,
                out var recordBytes,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.InvalidRecord));
        Assert.That(recordBytes, Is.Zero);
        Assert.That(diagnostic?.Category, Is.EqualTo("external-scene-binding-metadata"));
        Assert.That(
            ScenePersistenceExternalBindingWriter.Measure(
                new SceneExternalBindingCaptureRow(
                    Guid.Parse("10000000-0000-0000-0000-000000000001"),
                    Guid.Empty),
                EbinReadLimits.Foundation,
                out recordBytes,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.InvalidRecord));
        Assert.That(recordBytes, Is.Zero);
        Assert.That(diagnostic?.Category, Is.EqualTo("external-scene-binding-metadata"));
    }

    [Test]
    public void RejectsUnsortedExternalBindingRows()
    {
        SceneExternalBindingCaptureRow[] rows = [
            new SceneExternalBindingCaptureRow(Guid.Parse("20000000-0000-0000-0000-000000000002"), null),
            new SceneExternalBindingCaptureRow(Guid.Parse("10000000-0000-0000-0000-000000000001"), null),
        ];
        Assert.That(
            ScenePersistenceExternalBindingArrayWriter.Measure(
                rows,
                EbinReadLimits.Foundation,
                out var recordStorageBytes,
                out var candidateBytes,
                out var arrayPayloadBytes,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.InvalidRecord));
        Assert.That(recordStorageBytes, Is.Zero);
        Assert.That(candidateBytes, Is.Zero);
        Assert.That(arrayPayloadBytes, Is.Zero);
        Assert.That(diagnostic?.Category, Is.EqualTo("external-scene-binding-array-order"));
    }

    [Test]
    public void WritesCanonicalEmptyExternalBindingArray()
    {
        Assert.That(
            ScenePersistenceExternalBindingArrayWriter.Measure(
                [],
                EbinReadLimits.Foundation,
                out var recordStorageBytes,
                out var candidateBytes,
                out var arrayPayloadBytes,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(recordStorageBytes, Is.Zero);
        Assert.That(candidateBytes, Is.Zero);
        Assert.That(arrayPayloadBytes, Is.EqualTo(16));
        var payload = new byte[arrayPayloadBytes];
        Assert.That(
            ScenePersistenceExternalBindingArrayWriter.Write(
                [],
                EbinReadLimits.Foundation,
                [],
                [],
                [],
                payload,
                out var payloadBytes,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(payloadBytes, Is.EqualTo(16));
        Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(payload), Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void WritesCanonicalExternalBinding(bool hasTarget)
    {
        var bindingId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        Guid? targetEntityId = hasTarget
            ? Guid.Parse("20000000-0000-0000-0000-000000000002")
            : null;
        var row = new SceneExternalBindingCaptureRow(bindingId, targetEntityId);
        Assert.That(
            ScenePersistenceExternalBindingWriter.Measure(
                row,
                EbinReadLimits.Foundation,
                out var recordBytes,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        var candidateStorage = new byte[recordBytes];
        var destination = new byte[recordBytes];
        Assert.That(
            ScenePersistenceExternalBindingWriter.Write(
                row,
                EbinReadLimits.Foundation,
                candidateStorage,
                destination,
                out var bytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(bytesWritten, Is.EqualTo(recordBytes));
        var assertions = new ExternalBindingAssertions(bindingId, targetEntityId);
        Assert.That(
            EbinStructuralReader.Read(
                destination,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref assertions,
                out var bytesConsumed,
                out var readDiagnostic),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(readDiagnostic, Is.Null);
        Assert.That(assertions.IsMatch, Is.True);
        Assert.That(bytesConsumed, Is.EqualTo(destination.Length));
    }

    [Test]
    public void WritesCanonicalExternalBindingArray()
    {
        SceneExternalBindingCaptureRow[] rows = [
            new SceneExternalBindingCaptureRow(
                Guid.Parse("10000000-0000-0000-0000-000000000001"),
                null),
            new SceneExternalBindingCaptureRow(
                Guid.Parse("20000000-0000-0000-0000-000000000002"),
                Guid.Parse("30000000-0000-0000-0000-000000000003")),
        ];
        Assert.That(
            ScenePersistenceExternalBindingArrayWriter.Measure(
                rows,
                EbinReadLimits.Foundation,
                out var recordStorageBytes,
                out var candidateBytes,
                out var arrayPayloadBytes,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        var recordSlices = new EbinPayloadSlice[rows.Length];
        var recordStorage = new byte[recordStorageBytes];
        var candidateStorage = new byte[candidateBytes];
        var payload = new byte[arrayPayloadBytes];
        Assert.That(
            ScenePersistenceExternalBindingArrayWriter.Write(
                rows,
                EbinReadLimits.Foundation,
                recordSlices,
                recordStorage,
                candidateStorage,
                payload,
                out var payloadBytes,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(payloadBytes, Is.EqualTo(arrayPayloadBytes));
        Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(payload), Is.EqualTo(2));
        var previousOffset = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(8));

        for (var index = 0; index < rows.Length; index++)
        {
            var endOffset = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(16 + (index * 8)));
            var assertions = new ExternalBindingAssertions(rows[index].BindingId, rows[index].TargetEntityId);
            Assert.That(
                EbinStructuralReader.Read(
                    payload.AsSpan(checked((int)previousOffset), checked((int)(endOffset - previousOffset))),
                    EbinReadLimits.Foundation,
                    EbinReadPolicy.Canonical,
                    ref assertions,
                    out var rowBytes,
                    out var readDiagnostic),
                Is.EqualTo(EbinReadStatus.Complete));
            Assert.That(readDiagnostic, Is.Null);
            Assert.That(assertions.IsMatch, Is.True);
            Assert.That(rowBytes, Is.EqualTo(checked((int)(endOffset - previousOffset))));
            previousOffset = endOffset;
        }

        Assert.That(previousOffset, Is.EqualTo((ulong)payload.Length));
    }

    private struct ExternalBindingAssertions : IAction<EbinRecordReadView>
    {
        private readonly Guid _bindingId;
        private readonly Guid? _targetEntityId;

        internal ExternalBindingAssertions(Guid bindingId, Guid? targetEntityId)
        {
            _bindingId = bindingId;
            _targetEntityId = targetEntityId;
            IsMatch = false;
        }

        internal bool IsMatch { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
        {
            IsMatch = (record.Header.SchemaId == new ContentSchemaIdentityAssignment("state/external-scene-binding").SchemaId)
                && (record.Header.Major == 1)
                && (record.Header.Minor == 0)
                && (record.FieldCount == (_targetEntityId.HasValue ? 2 : 1));

            if (!IsMatch)
            {
                return;
            }

            var binding = record.GetField(0);
            IsMatch = (binding.Tag == 1)
                && (binding.Kind == 11)
                && binding.IsRequired
                && (new Guid(binding.Payload, bigEndian: true) == _bindingId);

            if (_targetEntityId is Guid expectedTarget)
            {
                var target = record.GetField(1);
                IsMatch &= (target.Tag == 2)
                    && (target.Kind == 11)
                    && !target.IsRequired
                    && (new Guid(target.Payload, bigEndian: true) == expectedTarget);
            }
        }
    }
}
