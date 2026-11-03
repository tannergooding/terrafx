// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceAccessWriterTests
{
    [Test]
    public void OmitsAbsentPartitionRange()
    {
        var access = new SystemResourceAccess(
            SystemResourceKind.EntityMembership,
            WorldSystemResources.EntityMembershipId,
            SystemAccessMode.Read);
        Assert.That(
            WorldPersistenceAccessWriter.Measure(
                access,
                EbinReadLimits.Foundation,
                out var recordBytes,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        var candidateStorage = new byte[recordBytes];
        var destination = new byte[recordBytes];
        Assert.That(
            WorldPersistenceAccessWriter.Write(
                access,
                EbinReadLimits.Foundation,
                candidateStorage,
                destination,
                out var bytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);

        var assertions = new AccessAssertions(access, null);
        Assert.That(
            EbinStructuralReader.Read(
                destination.AsSpan(0, bytesWritten),
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref assertions,
                out var bytesConsumed,
                out var readDiagnostic),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(readDiagnostic, Is.Null);
        Assert.That(bytesConsumed, Is.EqualTo(bytesWritten));
    }

    [Test]
    public void RejectsDefaultAccess()
    {
        Assert.That(
            WorldPersistenceAccessWriter.Measure(
                default,
                EbinReadLimits.Foundation,
                out var recordBytes,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.InvalidRecord));
        Assert.That(recordBytes, Is.Zero);
        Assert.That(diagnostic?.Category, Is.EqualTo("system-access-invalid"));
    }

    [Test]
    public void RejectsDefaultPartitionRange()
    {
        Assert.That(
            WorldPersistencePartitionRangeWriter.Measure(
                default,
                EbinReadLimits.Foundation,
                out var recordBytes,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.InvalidRecord));
        Assert.That(recordBytes, Is.Zero);
        Assert.That(diagnostic?.Category, Is.EqualTo("partition-range-invalid"));
    }

    [Test]
    public void WritesAccessWithPartitionRange()
    {
        var access = new SystemResourceAccess(
            SystemResourceKind.Component,
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            SystemAccessMode.Write,
            new SystemPartitionRange(7, 11));
        var range = new SystemPartitionRange(7, 11);
        Assert.That(
            WorldPersistencePartitionRangeWriter.Measure(
                range,
                EbinReadLimits.Foundation,
                out var rangeRecordBytes,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        var rangeCandidateStorage = new byte[rangeRecordBytes];
        var rangeDestination = new byte[rangeRecordBytes];
        Assert.That(
            WorldPersistencePartitionRangeWriter.Write(
                range,
                EbinReadLimits.Foundation,
                rangeCandidateStorage,
                rangeDestination,
                out var rangeBytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(rangeBytesWritten, Is.EqualTo(rangeRecordBytes));

        var rangeAssertions = new PartitionRangeAssertions(range);
        Assert.That(
            EbinStructuralReader.Read(
                rangeDestination.AsSpan(0, rangeBytesWritten),
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref rangeAssertions,
                out var rangeBytesConsumed,
                out var readDiagnostic),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(readDiagnostic, Is.Null);
        Assert.That(rangeBytesConsumed, Is.EqualTo(rangeBytesWritten));

        Assert.That(
            WorldPersistenceAccessWriter.Measure(
                access,
                EbinReadLimits.Foundation,
                out var accessRecordBytes,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        var accessCandidateStorage = new byte[accessRecordBytes];
        var accessDestination = new byte[accessRecordBytes];
        Assert.That(
            WorldPersistenceAccessWriter.Write(
                access,
                EbinReadLimits.Foundation,
                accessCandidateStorage,
                accessDestination,
                out var accessBytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(accessBytesWritten, Is.EqualTo(accessRecordBytes));

        var accessAssertions = new AccessAssertions(access, range);
        Assert.That(
            EbinStructuralReader.Read(
                accessDestination.AsSpan(0, accessBytesWritten),
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref accessAssertions,
                out var accessBytesConsumed,
                out readDiagnostic),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(readDiagnostic, Is.Null);
        Assert.That(accessBytesConsumed, Is.EqualTo(accessBytesWritten));
    }

    private readonly struct AccessAssertions : IAction<EbinRecordReadView>
    {
        private readonly SystemResourceAccess _access;
        private readonly SystemPartitionRange? _range;

        internal AccessAssertions(SystemResourceAccess access, SystemPartitionRange? range)
        {
            _access = access;
            _range = range;
        }

        private static void AssertRequiredField(EbinFieldReadView field, uint tag, ushort kind)
        {
            Assert.That(field.Tag, Is.EqualTo(tag));
            Assert.That(field.Kind, Is.EqualTo(kind));
            Assert.That(field.IsRequired, Is.True);
            Assert.That(field.Count, Is.EqualTo(1));
        }

        private static uint ReadUInt32(EbinFieldReadView field)
        {
            Assert.That(field.Kind, Is.EqualTo(3));
            Assert.That(field.Payload.Length, Is.EqualTo(sizeof(uint)));
            return BinaryPrimitives.ReadUInt32LittleEndian(field.Payload);
        }

        public readonly void Invoke(scoped EbinRecordReadView record)
        {
            Assert.That(record.Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/access").SchemaId));
            Assert.That(record.Header.Major, Is.EqualTo(1));
            Assert.That(record.Header.Minor, Is.Zero);
            Assert.That(record.FieldCount, Is.EqualTo(_range.HasValue ? 4 : 3));
            AssertRequiredField(record.GetField(0), 1, 1);
            AssertRequiredField(record.GetField(1), 2, 11);
            AssertRequiredField(record.GetField(2), 3, 1);
            Assert.That(record.GetField(0).Payload[0], Is.EqualTo((byte)_access.Kind));
            Assert.That(new Guid(record.GetField(1).Payload, bigEndian: true), Is.EqualTo(_access.ResourceId));
            Assert.That(record.GetField(2).Payload[0], Is.EqualTo((byte)_access.Mode));

            if (_range is SystemPartitionRange expectedRange)
            {
                var range = record.GetRecord(4);
                Assert.That(range.Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/partition-range").SchemaId));
                Assert.That(range.FieldCount, Is.EqualTo(2));
                Assert.That(ReadUInt32(range.GetField(0)), Is.EqualTo((uint)expectedRange.Start));
                Assert.That(ReadUInt32(range.GetField(1)), Is.EqualTo((uint)expectedRange.Length));
                var rangeField = record.GetField(3);
                Assert.That(rangeField.Tag, Is.EqualTo(4));
                Assert.That(rangeField.Kind, Is.EqualTo(22));
                Assert.That(rangeField.IsRequired, Is.False);
                Assert.That(rangeField.Count, Is.EqualTo(1));
            }
        }
    }

    private readonly struct PartitionRangeAssertions : IAction<EbinRecordReadView>
    {
        private readonly SystemPartitionRange _range;

        internal PartitionRangeAssertions(SystemPartitionRange range)
        {
            _range = range;
        }

        private static void AssertRequiredField(EbinFieldReadView field, uint tag)
        {
            Assert.That(field.Tag, Is.EqualTo(tag));
            Assert.That(field.Kind, Is.EqualTo(3));
            Assert.That(field.IsRequired, Is.True);
            Assert.That(field.Count, Is.EqualTo(1));
        }

        private static uint ReadUInt32(EbinFieldReadView field)
        {
            Assert.That(field.Kind, Is.EqualTo(3));
            Assert.That(field.Payload.Length, Is.EqualTo(sizeof(uint)));
            return BinaryPrimitives.ReadUInt32LittleEndian(field.Payload);
        }

        public readonly void Invoke(scoped EbinRecordReadView record)
        {
            Assert.That(record.Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/partition-range").SchemaId));
            Assert.That(record.Header.Major, Is.EqualTo(1));
            Assert.That(record.Header.Minor, Is.Zero);
            Assert.That(record.FieldCount, Is.EqualTo(2));
            AssertRequiredField(record.GetField(0), 1);
            AssertRequiredField(record.GetField(1), 2);
            Assert.That(ReadUInt32(record.GetField(0)), Is.EqualTo((uint)_range.Start));
            Assert.That(ReadUInt32(record.GetField(1)), Is.EqualTo((uint)_range.Length));
        }
    }
}
