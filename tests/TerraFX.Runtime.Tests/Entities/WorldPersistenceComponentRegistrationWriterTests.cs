// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceComponentRegistrationWriterTests
{
    private static ComponentFactoryDescriptor CreateDescriptor(
        ComponentStorageFlags flags,
        ComponentTransientRestore restore)
    {
        var schema = new ContentSchemaReference(
            new ContentSchemaIdentityAssignment("application/component-registration-test/v1").SchemaId,
            1,
            2,
            ContentSchemaScope.Content);
        var layout = new ComponentLayout(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            sizeof(long));
        var storage = new ComponentStorageDeclaration(
            sizeof(long),
            [],
            [new ComponentPaddingInterval(0, sizeof(long))]);
        return new ComponentFactoryDescriptor(
            schema,
            layout,
            storage,
            restore,
            flags,
            Guid.Parse("20000000-0000-0000-0000-000000000002"),
            3,
            [],
            []);
    }

    [Test]
    public void RejectsInconsistentStorageAndRestoreMetadata()
    {
        var descriptor = CreateDescriptor(ComponentStorageFlags.Persistent, ComponentTransientRestore.Reset);
        Assert.That(
            WorldPersistenceComponentRegistrationWriter.Measure(
                descriptor,
                EbinReadLimits.Foundation,
                out var recordBytes,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.InvalidRecord));
        Assert.That(recordBytes, Is.Zero);
        Assert.That(diagnostic?.Category, Is.EqualTo("component-registration-metadata"));
    }

    [TestCase(ComponentStorageFlags.Persistent, ComponentTransientRestore.None)]
    [TestCase(ComponentStorageFlags.Transient, ComponentTransientRestore.Reset)]
    [TestCase(ComponentStorageFlags.Transient, ComponentTransientRestore.Derived)]
    public void WritesCanonicalComponentRegistration(
        ComponentStorageFlags flags,
        ComponentTransientRestore restore)
    {
        var descriptor = CreateDescriptor(flags, restore);
        Assert.That(
            WorldPersistenceComponentRegistrationWriter.Measure(
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
            WorldPersistenceComponentRegistrationWriter.Write(
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

        var assertions = new ComponentRegistrationAssertions(descriptor);
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

    private readonly struct ComponentRegistrationAssertions : IAction<EbinRecordReadView>
    {
        private readonly ComponentFactoryDescriptor _descriptor;

        internal ComponentRegistrationAssertions(ComponentFactoryDescriptor descriptor)
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
            Assert.That(
                record.Header.SchemaId,
                Is.EqualTo(new ContentSchemaIdentityAssignment("state/component-registration").SchemaId));
            Assert.That(record.Header.Major, Is.EqualTo(1));
            Assert.That(record.Header.Minor, Is.Zero);
            Assert.That(record.FieldCount, Is.EqualTo(5));

            for (var index = 0; index < record.FieldCount; index++)
            {
                var field = record.GetField(index);
                Assert.That(field.Tag, Is.EqualTo((uint)(index + 1)));
                Assert.That(field.IsRequired, Is.True);
                Assert.That(field.Count, Is.EqualTo(1));
                Assert.That(field.Kind, Is.EqualTo(index switch {
                    0 => (ushort)22,
                    1 or 2 => 1,
                    3 => 11,
                    _ => 3,
                }));
            }

            var schema = record.GetRecord(1);
            Assert.That(
                schema.Header.SchemaId,
                Is.EqualTo(new ContentSchemaIdentityAssignment("content/schema-ref").SchemaId));
            Assert.That(schema.FieldCount, Is.EqualTo(4));

            for (var index = 0; index < schema.FieldCount; index++)
            {
                var field = schema.GetField(index);
                Assert.That(field.Tag, Is.EqualTo((uint)(index + 1)));
                Assert.That(field.IsRequired, Is.True);
                Assert.That(field.Count, Is.EqualTo(1));
                Assert.That(field.Kind, Is.EqualTo(index switch {
                    0 => (ushort)11,
                    1 or 2 => 3,
                    _ => 1,
                }));
            }

            Assert.That(new Guid(schema.GetField(0).Payload, bigEndian: true), Is.EqualTo(_descriptor.Schema.SchemaId));
            Assert.That(ReadUInt32(schema.GetField(1)), Is.EqualTo((uint)_descriptor.Schema.Major));
            Assert.That(ReadUInt32(schema.GetField(2)), Is.EqualTo((uint)_descriptor.Schema.Minor));
            Assert.That(schema.GetField(3).Payload[0], Is.EqualTo((byte)_descriptor.Schema.Scope));

            Assert.That(record.GetField(1).Payload[0], Is.EqualTo((byte)_descriptor.Flags));
            Assert.That(record.GetField(2).Payload[0], Is.EqualTo((byte)_descriptor.Restore));
            Assert.That(new Guid(record.GetField(3).Payload, bigEndian: true), Is.EqualTo(_descriptor.FactoryId));
            Assert.That(ReadUInt32(record.GetField(4)), Is.EqualTo((uint)_descriptor.FactoryVersion));
        }
    }
}
