// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceResourceRegistrationWriterTests
{
    private static WorldResourceRegistrationDescriptor CreateDescriptor(bool allowParallelAccess, int factoryVersion = 2)
    {
        var schema = new ContentSchemaReference(
            new ContentSchemaIdentityAssignment("application/resource-registration-test/v1").SchemaId,
            2,
            1,
            ContentSchemaScope.Content);
        var storage = new ComponentStorageDeclaration(
            sizeof(long),
            [],
            [new ComponentPaddingInterval(0, sizeof(long))]);
        var factory = new WorldResourceFactoryDescriptor(
            schema,
            Guid.Parse("20000000-0000-0000-0000-000000000002"),
            factoryVersion,
            storage,
            []);
        var description = new SystemResourceDescription(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            13,
            allowParallelAccess);
        return new WorldResourceRegistrationDescriptor(description, factory);
    }

    [Test]
    public void RejectsInvalidFactoryVersion()
    {
        var descriptor = CreateDescriptor(allowParallelAccess: true, factoryVersion: 0);
        Assert.That(
            WorldPersistenceResourceRegistrationWriter.Measure(
                descriptor,
                EbinReadLimits.Foundation,
                out var recordBytes,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.InvalidRecord));
        Assert.That(recordBytes, Is.Zero);
        Assert.That(diagnostic?.Category, Is.EqualTo("resource-registration-metadata"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void WritesCanonicalResourceRegistration(bool allowParallelAccess)
    {
        var descriptor = CreateDescriptor(allowParallelAccess);
        Assert.That(
            WorldPersistenceResourceRegistrationWriter.Measure(
                descriptor,
                EbinReadLimits.Foundation,
                out var recordBytes,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        var candidateStorage = new byte[recordBytes];
        var destination = new byte[recordBytes];
        Assert.That(
            WorldPersistenceResourceRegistrationWriter.Write(
                descriptor,
                EbinReadLimits.Foundation,
                candidateStorage,
                destination,
                out var bytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(bytesWritten, Is.EqualTo(recordBytes));

        var assertions = new ResourceRegistrationAssertions(descriptor);
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

    private readonly struct ResourceRegistrationAssertions : IAction<EbinRecordReadView>
    {
        private readonly WorldResourceRegistrationDescriptor _descriptor;

        internal ResourceRegistrationAssertions(WorldResourceRegistrationDescriptor descriptor)
        {
            _descriptor = descriptor;
        }

        private static uint ReadUInt32(EbinFieldReadView field)
        {
            Assert.That(field.Kind, Is.EqualTo(3));
            Assert.That(field.Payload.Length, Is.EqualTo(sizeof(uint)));
            return BinaryPrimitives.ReadUInt32LittleEndian(field.Payload);
        }

        public readonly void Invoke(scoped EbinRecordReadView record)
        {
            Assert.That(record.Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/resource-registration").SchemaId));
            Assert.That(record.Header.Major, Is.EqualTo(1));
            Assert.That(record.Header.Minor, Is.Zero);
            Assert.That(record.FieldCount, Is.EqualTo(6));

            var expectedKinds = new ushort[] { 11, 3, 9, 22, 11, 3 };

            for (var index = 0; index < record.FieldCount; index++)
            {
                var field = record.GetField(index);
                Assert.That(field.Tag, Is.EqualTo((uint)(index + 1)));
                Assert.That(field.Kind, Is.EqualTo(expectedKinds[index]));
                Assert.That(field.IsRequired, Is.True);
                Assert.That(field.Count, Is.EqualTo(1));
            }

            Assert.That(new Guid(record.GetField(0).Payload, bigEndian: true), Is.EqualTo(_descriptor.ResourceId));
            Assert.That(ReadUInt32(record.GetField(1)), Is.EqualTo((uint)_descriptor.PartitionCount));
            Assert.That(record.GetField(2).Payload[0], Is.EqualTo(_descriptor.AllowParallelAccess ? (byte)1 : (byte)0));

            var schema = record.GetRecord(4);
            Assert.That(schema.Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("content/schema-ref").SchemaId));
            Assert.That(new Guid(schema.GetField(0).Payload, bigEndian: true), Is.EqualTo(_descriptor.Factory.Schema.SchemaId));
            Assert.That(ReadUInt32(schema.GetField(1)), Is.EqualTo((uint)_descriptor.Factory.Schema.Major));
            Assert.That(ReadUInt32(schema.GetField(2)), Is.EqualTo((uint)_descriptor.Factory.Schema.Minor));
            Assert.That(schema.GetField(3).Payload[0], Is.EqualTo((byte)_descriptor.Factory.Schema.Scope));
            Assert.That(new Guid(record.GetField(4).Payload, bigEndian: true), Is.EqualTo(_descriptor.Factory.FactoryId));
            Assert.That(ReadUInt32(record.GetField(5)), Is.EqualTo((uint)_descriptor.Factory.FactoryVersion));
        }
    }
}
