// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Animation;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Hosting;
using TerraFX.Integration;
using TerraFX.Rendering;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceIdentityHistoryReaderTests
{
    [Test]
    public void RejectsDuplicateIdentityAcrossRetiredAndLiveAssignments()
    {
        DataSchema[] schemaRows = [
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
            .. AnimationSchemaCatalogue.Schemas,
            .. RenderingSchemaCatalogue.Schemas,
            .. StandaloneContinuationSchemaCatalogue.Schemas,
            WorldPersistenceWorldRecordWriterTests.CreateApplicationSchema(
                "application/world-root-test/tag/v1",
                ContentSchemaRole.EntityTag,
                0),
            WorldPersistenceWorldRecordWriterTests.CreateApplicationSchema(
                "application/world-root-test/system-state/v1",
                ContentSchemaRole.SystemState,
                7),
            WorldPersistenceWorldRecordWriterTests.CreateApplicationSchema(
                "application/world-root-test/resource-state/v1",
                ContentSchemaRole.ResourceState,
                23,
                4),
            WorldPersistenceWorldRecordWriterTests.CreateApplicationSchema(
                "application/world-root-test/attachment-state/v1",
                ContentSchemaRole.AttachmentState,
                8),
        ];
        var schemas = new CompiledContentSchemaRegistry([.. schemaRows]);
        var registration = new WorldRegistrationBuilder(schemas).Build();
        var firstEntityId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var secondEntityId = Guid.Parse("30000000-0000-0000-0000-000000000002");
        using var world = World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var firstEntity = world.CreateEntity(new EntityDescription(firstEntityId));
        _ = world.Step();
        world.DestroyEntity(firstEntity, HierarchyDestroyMode.DetachChildrenKeepWorld);
        _ = world.Step();
        _ = world.CreateEntity(new EntityDescription(secondEntityId));
        _ = world.Step();

        using var lease = world.AcquirePersistenceCaptureLease();
        var limits = WorldPersistenceEbinLimits.Create(world.Options);
        var worldRecord = WorldPersistenceWorldRecordWriterTests.ComposeWorldRecord(
            world,
            in lease,
            schemas,
            []);
        var assignmentCount = WorldPersistenceWorldRecordReader.MeasureIdentityAssignmentCount(worldRecord, limits);
        Assert.That(assignmentCount, Is.EqualTo(2));

        var identityScratch = new WorldPersistenceIdentityScratchEntry[assignmentCount];
        var contents = WorldPersistenceWorldRecordReader.Read(
            worldRecord,
            limits,
            targetRegistration: null,
            identityScratch: identityScratch);
        Assert.That(contents.Ecs.EntityCount, Is.EqualTo(1));

        var duplicateRecord = (byte[])worldRecord.Clone();
        Span<byte> firstEntityIdBytes = stackalloc byte[16];
        Span<byte> secondEntityIdBytes = stackalloc byte[16];
        _ = firstEntityId.TryWriteBytes(firstEntityIdBytes, bigEndian: true, out _);
        _ = secondEntityId.TryWriteBytes(secondEntityIdBytes, bigEndian: true, out _);
        var firstIdentityOffset = duplicateRecord.AsSpan().IndexOf(firstEntityIdBytes);
        Assert.That(firstIdentityOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(
            duplicateRecord.AsSpan(firstIdentityOffset + firstEntityIdBytes.Length).IndexOf(firstEntityIdBytes),
            Is.EqualTo(-1));
        secondEntityIdBytes.CopyTo(duplicateRecord.AsSpan(firstIdentityOffset, secondEntityIdBytes.Length));

        _ = Assert.Throws<ArgumentException>(() => WorldPersistenceWorldRecordReader.Read(
            duplicateRecord,
            limits,
            targetRegistration: null,
            identityScratch: identityScratch));
        _ = Assert.Throws<ArgumentException>(() => WorldPersistenceWorldRecordReader.Read(
            worldRecord,
            limits,
            targetRegistration: null,
            identityScratch: new WorldPersistenceIdentityScratchEntry[assignmentCount + 1]));
    }
}
