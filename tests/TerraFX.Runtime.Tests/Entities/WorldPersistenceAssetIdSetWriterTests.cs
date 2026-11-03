// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceAssetIdSetWriterTests
{
    private static readonly ContentSchemaIdentityAssignment s_assetReferenceIdentity =
        new("native/asset-reference");
    private static readonly ContentSchemaIdentityAssignment s_containerIdentity =
        new("test/asset-reference-container");

    private static byte[] CreateAssetReferenceRecord(uint assetId)
    {
        var assetReferenceSchema = GetAssetReferenceSchema().Schema;
        var payload = new byte[48];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, assetId);
        _ = new ContentSchemaIdentityAssignment("test/asset").SchemaId.TryWriteBytes(payload.AsSpan(16, 16), bigEndian: true, out _);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(32), 1);
        EbinFieldSource[] fields = [
            new(1, 3, 1, 1, new EbinPayloadSlice(0, 4)),
            new(2, 11, 1, 1, new EbinPayloadSlice(16, 16)),
            new(3, 3, 1, 1, new EbinPayloadSlice(32, 4)),
        ];
        return WriteRecord(assetReferenceSchema, fields, payload);
    }

    private static byte[] CreateContainerRecord(ContentSchemaReference schema, uint[] assetIds)
    {
        var references = new byte[assetIds.Length][];
        var payloadBytes = 0;

        for (var index = 0; index < references.Length; index++)
        {
            references[index] = CreateAssetReferenceRecord(assetIds[index]);
            payloadBytes = checked(payloadBytes + references[index].Length);
        }

        var payload = new byte[payloadBytes];
        var fields = new EbinFieldSource[references.Length];
        var offset = 0;

        for (var index = 0; index < references.Length; index++)
        {
            var record = references[index];
            record.CopyTo(payload, offset);
            fields[index] = new EbinFieldSource(
                checked((uint)index + 1),
                22,
                1,
                1,
                new EbinPayloadSlice(offset, record.Length));
            offset = checked(offset + record.Length);
        }

        return WriteRecord(schema, fields, payload);
    }

    private static DataSchema CreateContainerSchema(ContentSchemaReference assetReferenceSchema)
    {
        var fields = ImmutableArray.CreateBuilder<ContentFieldDescriptor>(3);

        for (uint tag = 1; tag <= 3; tag++)
        {
            fields.Add(new ContentFieldDescriptor(
                tag,
                22,
                0,
                isRequired: true,
                1,
                1,
                [assetReferenceSchema],
                0,
                ContentUnit.None,
                ContentReferenceKind.Asset,
                ContentAuthoring.None,
                null));
        }

        return new DataSchema(
            new ContentSchemaReference(s_containerIdentity.SchemaId, 1, 0, ContentSchemaScope.Content),
            s_containerIdentity,
            ContentSchemaRole.NativeAsset,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            fields.MoveToImmutable());
    }

    private static DataSchema GetAssetReferenceSchema()
    {
        foreach (var schema in NativeAssetSchemaCatalogue.Schemas)
        {
            if (schema.IdentityAssignment == s_assetReferenceIdentity)
            {
                return schema;
            }
        }

        throw new InvalidOperationException("The native asset-reference schema is missing.");
    }

    private static byte[] WriteRecord(
        ContentSchemaReference schema,
        EbinFieldSource[] fields,
        byte[] payload)
    {
        var source = new EbinRecordWriteView(schema.SchemaId, schema.Major, schema.Minor, fields, payload);
        Assert.That(
            EbinStructuralWriter.Measure(in source, EbinReadLimits.Foundation, out var recordBytes, out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        var candidate = new byte[recordBytes];
        var destination = new byte[recordBytes];
        Assert.That(
            EbinStructuralWriter.Write(
                in source,
                EbinReadLimits.Foundation,
                candidate,
                destination,
                out var bytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(bytesWritten, Is.EqualTo(destination.Length));
        return destination;
    }

    [Test]
    public void SortsAndDeduplicatesReferencesAcrossEcsAndSceneRoots()
    {
        var assetReferenceSchema = GetAssetReferenceSchema();
        var containerSchema = CreateContainerSchema(assetReferenceSchema.Schema);
        var schemas = new CompiledContentSchemaRegistry([.. NativeAssetSchemaCatalogue.Schemas, containerSchema]);
        var ecsRecord = CreateContainerRecord(containerSchema.Schema, [7, 3, 7]);
        var scenesRecord = CreateContainerRecord(containerSchema.Schema, [5, 3, 5]);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(ecsRecord.AsSpan(12)), Is.EqualTo(3));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(scenesRecord.AsSpan(12)), Is.EqualTo(3));

        for (var index = 0; index < 3; index++)
        {
            var nestedOffset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(ecsRecord.AsSpan(72 + (index * 32))));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(ecsRecord.AsSpan(nestedOffset + 12)), Is.EqualTo(3));
        }

        Assert.That(
            WorldPersistenceAssetIdSetWriter.MeasureReferenceCount(
                schemas,
                ecsRecord,
                scenesRecord,
                EbinReadLimits.Foundation),
            Is.EqualTo(6));

        var assetIds = new uint[6];
        var uniqueCount = WorldPersistenceAssetIdSetWriter.CopySortedUniqueAssetIds(
            schemas,
            ecsRecord,
            scenesRecord,
            EbinReadLimits.Foundation,
            assetIds);

        Assert.That(uniqueCount, Is.EqualTo(3));
        uint[] expectedAssetIds = [3, 5, 7];
        Assert.That(assetIds.AsSpan(0, uniqueCount).ToArray(), Is.EqualTo(expectedAssetIds));
    }
}
