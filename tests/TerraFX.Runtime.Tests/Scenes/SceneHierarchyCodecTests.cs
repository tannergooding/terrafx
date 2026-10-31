// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using NUnit.Framework;
using TerraFX;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

[TestFixture]
internal sealed class SceneHierarchyCodecTests
{
    [Test]
    public void ReadsCanonicalHierarchyRecords()
    {
        var rootEntityId = new Guid("10000000-0000-4000-8000-000000000001");
        var childEntityId = new Guid("20000000-0000-4000-8000-000000000002");
        var placementId = new Guid("30000000-0000-4000-8000-000000000003");
        var expected = new SceneHierarchy(
            [
                new SceneHierarchyNode(
                    rootEntityId,
                    null,
                    [
                        new SceneHierarchyEntry(SceneHierarchyEntryKind.Entity, childEntityId),
                        new SceneHierarchyEntry(SceneHierarchyEntryKind.PrefabPlacement, placementId),
                    ]),
                new SceneHierarchyNode(
                    childEntityId,
                    rootEntityId,
                    []),
            ],
            [
                new SceneHierarchyEntry(SceneHierarchyEntryKind.Entity, rootEntityId),
                new SceneHierarchyEntry(SceneHierarchyEntryKind.PrefabPlacement, placementId),
            ]);
        var rootNode = FixtureBuilder.EncodeNode(
            rootEntityId,
            parentEntityId: null,
            FixtureBuilder.EncodeEntry((byte)SceneHierarchyEntryKind.Entity, childEntityId),
            FixtureBuilder.EncodeEntry((byte)SceneHierarchyEntryKind.PrefabPlacement, placementId));
        var childNode = FixtureBuilder.EncodeNode(childEntityId, rootEntityId);
        var rootEntity = FixtureBuilder.EncodeEntry((byte)SceneHierarchyEntryKind.Entity, rootEntityId);
        var rootPlacement = FixtureBuilder.EncodeEntry((byte)SceneHierarchyEntryKind.PrefabPlacement, placementId);
        var encoded = FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/hierarchy").SchemaId,
            2,
            0,
            FixtureBuilder.CreateRecordArrayField(1, rootNode, childNode),
            FixtureBuilder.CreateRecordArrayField(2, rootEntity, rootPlacement));

        var actual = SceneHierarchyReader.Read(encoded, EbinReadLimits.Foundation);

        Assert.That(actual.Nodes.Length, Is.EqualTo(expected.Nodes.Length));
        Assert.That(actual.Nodes[0].EntityId, Is.EqualTo(expected.Nodes[0].EntityId));
        Assert.That(actual.Nodes[0].ParentEntityId, Is.EqualTo(expected.Nodes[0].ParentEntityId));
        Assert.That(
            actual.Nodes[0].Children.AsSpan().SequenceEqual(expected.Nodes[0].Children.AsSpan()),
            Is.True);
        Assert.That(actual.Nodes[1].EntityId, Is.EqualTo(expected.Nodes[1].EntityId));
        Assert.That(actual.Nodes[1].ParentEntityId, Is.EqualTo(expected.Nodes[1].ParentEntityId));
        Assert.That(
            actual.Nodes[1].Children.AsSpan().SequenceEqual(expected.Nodes[1].Children.AsSpan()),
            Is.True);
        Assert.That(actual.Roots.AsSpan().SequenceEqual(expected.Roots.AsSpan()), Is.True);
    }

    [Test]
    public void RejectsTruncatedAndTrailingHierarchyRecords()
    {
        var entityId = new Guid("10000000-0000-4000-8000-000000000001");
        var valid = FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/hierarchy").SchemaId,
            2,
            0,
            FixtureBuilder.CreateRecordArrayField(1, FixtureBuilder.EncodeNode(entityId, parentEntityId: null)),
            FixtureBuilder.CreateRecordArrayField(2, FixtureBuilder.EncodeEntry((byte)SceneHierarchyEntryKind.Entity, entityId)));
        var truncated = valid[..^1];
        var trailing = new byte[checked(valid.Length + 1)];
        valid.CopyTo(trailing, 0);

        Assert.That(
            () => SceneHierarchyReader.Read(truncated, EbinReadLimits.Foundation),
            Throws.TypeOf<InvalidDataException>());

        Assert.That(
            () => SceneHierarchyReader.Read(trailing, EbinReadLimits.Foundation),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsUnorderedNodesAndUnknownEntryKinds()
    {
        var firstEntityId = new Guid("10000000-0000-4000-8000-000000000001");
        var secondEntityId = new Guid("20000000-0000-4000-8000-000000000002");
        var unordered = FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/hierarchy").SchemaId,
            2,
            0,
            FixtureBuilder.CreateRecordArrayField(
                1,
                FixtureBuilder.EncodeNode(secondEntityId, parentEntityId: null),
                FixtureBuilder.EncodeNode(firstEntityId, parentEntityId: null)),
            FixtureBuilder.CreateRecordArrayField(2));
        var unknownKind = FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/hierarchy").SchemaId,
            2,
            0,
            FixtureBuilder.CreateRecordArrayField(1),
            FixtureBuilder.CreateRecordArrayField(2, FixtureBuilder.EncodeEntry(0, firstEntityId)));

        Assert.That(
            () => SceneHierarchyReader.Read(unordered, EbinReadLimits.Foundation),
            Throws.TypeOf<InvalidDataException>());

        Assert.That(
            () => SceneHierarchyReader.Read(unknownKind, EbinReadLimits.Foundation),
            Throws.TypeOf<InvalidDataException>());
    }

    internal static class FixtureBuilder
    {
        public static FixtureField CreateFloatGeometryField(uint tag, ushort kind, params float[] values)
        {
            var payload = new byte[checked(values.Length * sizeof(float))];

            for (var index = 0; index < values.Length; index++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    payload.AsSpan(index * sizeof(float)),
                    BitConverter.SingleToInt32Bits(values[index]));
            }

            return new FixtureField(tag, kind, 1, 1, payload);
        }

        public static FixtureField CreateGuidField(uint tag, Guid value, bool isRequired = true)
            => new(tag, 11, isRequired ? (ushort)1 : (ushort)0, 1, GetGuidBytes(value));

        public static FixtureField CreateRecordArrayField(uint tag, params byte[][] records)
            => new(tag, 24, 1, checked((ulong)records.Length), CreateRecordArrayPayload(records));

        public static byte[] CreateRecordArrayPayload(byte[][] records)
        {
            var storageBytes = 0;

            foreach (var record in records)
            {
                storageBytes = checked(storageBytes + record.Length);
            }

            var storage = new byte[storageBytes];
            var slices = new EbinPayloadSlice[records.Length];
            var offset = 0;

            for (var index = 0; index < records.Length; index++)
            {
                var record = records[index];
                record.CopyTo(storage, offset);
                slices[index] = new EbinPayloadSlice(offset, record.Length);
                offset += record.Length;
            }

            var limits = EbinReadLimits.Foundation;
            var measureStatus = EbinPayloadWriter.MeasureRecordArray(slices, storage, limits, out var payloadBytes, out var diagnostic);

            if (measureStatus is not EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException($"The hierarchy test record-array fixture failed: {diagnostic?.Category ?? measureStatus.ToString()}.");
            }

            var payload = new byte[payloadBytes];
            var writeStatus = EbinPayloadWriter.WriteRecordArray(
                slices,
                storage,
                limits,
                payload,
                out var bytesWritten,
                out diagnostic);

            if (writeStatus is not EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException($"The hierarchy test record-array fixture failed: {diagnostic?.Category ?? writeStatus.ToString()}.");
            }

            return payload[..bytesWritten];
        }

        public static FixtureField CreateUInt8Field(uint tag, byte value)
            => new(tag, 1, 1, 1, [value]);

        public static byte[] EncodeEntry(byte kind, Guid id)
            => EncodeRecord(
                new ContentSchemaIdentityAssignment("native/hierarchy-entry").SchemaId,
                1,
                0,
                CreateUInt8Field(1, kind),
                CreateGuidField(2, id));

        public static byte[] EncodeNode(
            Guid entityId,
            Guid? parentEntityId,
            params byte[][] children)
        {
            var fields = new FixtureField[parentEntityId.HasValue ? 3 : 2];
            var fieldIndex = 0;
            fields[fieldIndex++] = CreateGuidField(1, entityId);

            if (parentEntityId is { } parentId)
            {
                fields[fieldIndex++] = CreateGuidField(2, parentId, isRequired: false);
            }

            fields[fieldIndex] = CreateRecordArrayField(3, children);
            return EncodeRecord(
                new ContentSchemaIdentityAssignment("native/hierarchy-node").SchemaId,
                2,
                0,
                fields);
        }

        public static byte[] EncodeRecord(
            Guid schemaId,
            int major,
            int minor,
            params FixtureField[] fields)
        {
            var payloadBytes = 0;

            foreach (var field in fields)
            {
                payloadBytes = checked(payloadBytes + field.Payload.Length);
            }

            var payloadStorage = new byte[payloadBytes];
            var fieldSources = new EbinFieldSource[fields.Length];
            var payloadOffset = 0;

            for (var fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
            {
                var field = fields[fieldIndex];
                field.Payload.CopyTo(payloadStorage, payloadOffset);
                fieldSources[fieldIndex] = new EbinFieldSource(
                    field.Tag,
                    field.Kind,
                    field.Flags,
                    field.Count,
                    new EbinPayloadSlice(payloadOffset, field.Payload.Length));
                payloadOffset += field.Payload.Length;
            }

            var view = new EbinRecordWriteView(schemaId, major, minor, fieldSources, payloadStorage);
            var limits = EbinReadLimits.Foundation;
            var measureStatus = EbinStructuralWriter.Measure(in view, limits, out var candidateBytes, out var diagnostic);

            if (measureStatus is not EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException($"The hierarchy test record failed: {diagnostic?.Category ?? measureStatus.ToString()}.");
            }

            var candidate = new byte[candidateBytes];
            var writeStatus = EbinStructuralWriter.Prepare(in view, limits, candidate, out var bytesWritten, out diagnostic);

            if (writeStatus is not EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException($"The hierarchy test record failed: {diagnostic?.Category ?? writeStatus.ToString()}.");
            }

            return candidate[..bytesWritten];
        }

        public static byte[] GetGuidBytes(Guid value)
        {
            var bytes = new byte[16];
            _ = value.TryWriteBytes(bytes, bigEndian: true, out _);
            return bytes;
        }

        internal readonly record struct FixtureField(
            uint Tag,
            ushort Kind,
            ushort Flags,
            ulong Count,
            byte[] Payload);
    }
}
