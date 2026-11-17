// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Animation;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Hosting;
using TerraFX.Integration;
using TerraFX.Numerics;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Animation;

[TestFixture]
internal sealed class RootMotionResourceTests
{
    private static EbinRecord Asset(uint id, string target)
        => new(new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId, 1, 0, [
            EbinPrimitives.UInt32(1, id),
            EbinPrimitives.Guid(2, new ContentSchemaIdentityAssignment(target).SchemaId),
            EbinPrimitives.UInt32(3, 1),
        ]);

    private static byte[] BuildLiteral(EbinReadLimits limits, params EbinRecord[] prefix)
    {
        var absent = EbinPrimitives.Encode(new EbinRecord(
            new ContentSchemaIdentityAssignment("animation/root-motion-slot").SchemaId, 1, 0,
            [EbinPrimitives.Boolean(1, false)]));
        var encoded = prefix.Select(EbinPrimitives.Encode).ToArray();
        var childBytes = checked(encoded.Sum(static value => value.Length) + ((4096 - prefix.Length) * absent.Length));
        var children = new byte[childBytes];
        var slices = new EbinPayloadSlice[4096];
        var cursor = 0;

        for (var index = 0; index < 4096; index++)
        {
            var child = index < encoded.Length ? encoded[index] : absent;
            child.CopyTo(children, cursor);
            slices[index] = new EbinPayloadSlice(cursor, child.Length);
            cursor += child.Length;
        }

        Assert.That(EbinPayloadWriter.MeasureRecordArray(slices, children, limits, out var arrayBytes, out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.Category);
        var payload = new byte[arrayBytes];
        Assert.That(EbinPayloadWriter.WriteRecordArray(slices, children, limits, payload, out var written, out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.Category);
        Assert.That(written, Is.EqualTo(arrayBytes));
        Span<EbinFieldSource> fields = [new(1, 24, 1, 4096, new EbinPayloadSlice(0, arrayBytes))];
        var source = new EbinRecordWriteView(
            new ContentSchemaIdentityAssignment("animation/root-motion-resource").SchemaId, 1, 0, fields, payload);
        Assert.That(EbinStructuralWriter.Measure(in source, limits, out var bytes, out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.Category);
        var candidate = new byte[bytes];
        var result = new byte[bytes];
        Assert.That(EbinStructuralWriter.Write(in source, limits, candidate, result, out written, out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.Category);
        Assert.That(written, Is.EqualTo(bytes));
        return result;
    }

    private static (World World, RootMotionResourceProvider Provider) CreateWorld(bool smallProfile = false, WorldSchedulingLimits? schedulingLimits = null)
    {
        var schemas = new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
            .. RenderingSchemaCatalogue.Schemas,
            .. AnimationSchemaCatalogue.Schemas,
            .. AnimationSchemaCatalogue.PlaybackSchemas,
            .. StandaloneContinuationSchemaCatalogue.Schemas,
            EnvelopeRole("application/root-motion-codec/entity-tag/v1", ContentSchemaRole.EntityTag),
            EnvelopeRole("application/root-motion-codec/system-state/v1", ContentSchemaRole.SystemState),
        ]);
        var provider = new RootMotionResourceProvider(schemas);
        var builder = new WorldRegistrationBuilder(schemas);
        _ = builder.RegisterResource(new SystemResourceDescription(
            AnimationResources.RootMotionRequestsId, 4096, allowParallelAccess: false), provider, provider);
        _ = builder.RegisterSceneSupport(SceneSupport.CreateRegistration());
        var world = World.Create(new WorldOptions(builder.Build()) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            MaximumSnapshotBytes = smallProfile ? 65_536 : 32 * 1024 * 1024,
            SchedulingLimits = schedulingLimits ?? WorldSchedulingLimits.Default,
            WorldGuid = Guid.NewGuid(),
        });
        return (world, provider);
    }

    private static int ElementOffset(byte[] record, int arrayOffset, int index)
        => checked(arrayOffset + (int)BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(arrayOffset + 8 + (8 * index))));

    private static DataSchema EnvelopeRole(string key, ContentSchemaRole role)
    {
        var assignment = new ContentSchemaIdentityAssignment(key);
        return new DataSchema(
            new ContentSchemaReference(assignment.SchemaId, 1, 0, ContentSchemaScope.Content),
            assignment, role, ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent, ContentTransientRestore.NotApplicable, []);
    }

    private static int FieldOffset(byte[] record, int recordOffset, uint tag)
    {
        var count = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(recordOffset + 12));

        for (var index = 0; index < count; index++)
        {
            var entry = recordOffset + 64 + (index * 32);

            if (BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(entry)) == tag)
            {
                return checked(recordOffset + (int)BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(entry + 8)));
            }
        }

        throw new InvalidOperationException("The literal fixture omitted its requested field.");
    }

    private static EbinField Optional(EbinField field) => new(field.Tag, field.Kind, 0, field.Count, field.Payload);

    private static EbinRecord Present(WorldEntityIdentity player, WorldEntityIdentity actor, long tick, int sourceCount)
    {
        var sources = new EbinRecord[sourceCount];

        for (var index = 0; index < sourceCount; index++)
        {
            sources[index] = new EbinRecord(
                new ContentSchemaIdentityAssignment("animation/root-motion-source").SchemaId, 1, 0, [
                    EbinPrimitives.Guid(1, new Guid(0x60000000 + index, 0, 0, new byte[8])),
                    EbinPrimitives.Record(2, Asset(checked((uint)(101 + index)), "native/clip")),
                    EbinPrimitives.Int64(3, index + 1),
                    EbinPrimitives.Int64(4, -2 - index),
                    EbinPrimitives.Binary32(5, 0.25f),
                ]);
        }

        var translation = sourceCount == 0 ? Vector3.Zero : new Vector3(-0.0f, 2.25f, 0.0f);
        return new EbinRecord(
            new ContentSchemaIdentityAssignment("animation/root-motion-slot").SchemaId, 1, 0, [
                EbinPrimitives.Boolean(1, true),
                Optional(EbinPrimitives.Guid(2, player.EntityId)),
                Optional(EbinPrimitives.Guid(3, player.CreationLineageId)),
                Optional(EbinPrimitives.Guid(4, player.CreationBranchId)),
                Optional(EbinPrimitives.Guid(5, actor.EntityId)),
                Optional(EbinPrimitives.Guid(6, actor.CreationLineageId)),
                Optional(EbinPrimitives.Guid(7, actor.CreationBranchId)),
                Optional(EbinPrimitives.Int64(8, tick)),
                Optional(EbinPrimitives.Geometry(9, translation)),
                Optional(EbinPrimitives.Geometry(10, Quaternion.Identity)),
                Optional(EbinPrimitives.RecordArray(11, sources)),
                Optional(EbinPrimitives.UInt8(12, 1)),
                Optional(EbinPrimitives.Record(13, Asset(7, "native/animation-controller"))),
            ]);
    }

    private static WorldPersistenceWorldRecordContents ReadWorld(World world, byte[] bytes)
    {
        var limits = WorldPersistenceEbinLimits.Create(world.Options);
        var identities = new WorldPersistenceIdentityScratchEntry[
            WorldPersistenceWorldRecordReader.MeasureIdentityAssignmentCount(bytes, limits)];
        var lineages = new Guid[WorldPersistenceWorldRecordReader.MeasureLineageMembershipScratchCapacity(bytes, limits)];
        var strict = default(WorldPersistenceStrictLayoutReadBuffers);
        return WorldPersistenceWorldRecordReader.Read(
            bytes, limits, world.Options.Registration, identities, lineages, ref strict);
    }

    [Test]
    public void ActualStateEnvelopeCarriesWideResourceAndRegisteredReadRejectsBadPresence()
    {
        var fixture = CreateWorld();
        using var world = fixture.World;
        _ = world.Step();
        using var lease = world.AcquirePersistenceCaptureLease();
        var status = WorldPersistenceCaptureCoordinator.Capture(
            world, lease, world.Options.Registration.Schemas, out var reservation, out var bytes, out _, out var diagnostic, CancellationToken.None);
        using var retained = reservation;
        Assert.That(status, Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.Category);
        var encoded = bytes ?? throw new InvalidOperationException("The actual capture omitted its World record.");
        var contents = ReadWorld(world, encoded);
        Assert.That(contents.Ecs.ResourceCount, Is.EqualTo(1));
        var ecsOffset = FieldOffset(encoded, 0, 5);
        var rowsOffset = FieldOffset(encoded, ecsOffset, 3);
        var rowOffset = ElementOffset(encoded, rowsOffset, 0);
        var payloadOffset = FieldOffset(encoded, rowOffset, 2);
        var valuesOffset = FieldOffset(encoded, payloadOffset, 1);
        var slotOffset = ElementOffset(encoded, valuesOffset, 0);
        encoded[FieldOffset(encoded, slotOffset, 1)] = 1;
        _ = ReadWorld(world, encoded);
        var resource = world.Options.Registration.Resources[0];
        var storage = resource.CreateStorage();
        resource.InitializeAndValidate(storage, world);
        var payload = new EbinRecordReadView(encoded).GetRecord(5).GetRecordArrayElement(3, 0).GetRecord(2);
        var source = payload.EncodedBytes.ToArray();
        var completedTick = lease.CompletedTick;
        Assert.That(() => resource.RestoreStateRecord(source, storage, CancellationToken.None, world, completedTick),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(((RootMotionRequest[])storage).All(static value => !value.IsValid && value.HasCanonicalStorage), Is.True);
    }

    [Test]
    public void CapturesExactly4096AbsentSlotsWithoutChangingLegacyWriterBound()
    {
        var fixture = CreateWorld();
        using var world = fixture.World;
        _ = world.Step();
        using var lease = world.AcquirePersistenceCaptureLease();
        var limits = WorldPersistenceEbinLimits.Create(world.Options);
        var expected = BuildLiteral(limits);
        Assert.That(expected, Has.Length.EqualTo(491_632));
        var count = lease.CountResourceStateRecord(0, CancellationToken.None);
        Assert.That(count, Is.EqualTo((nint)491_632));
        var encoded = new byte[checked((int)count + 16)];
        encoded.AsSpan().Fill(0xCD);
        Assert.That(lease.WriteResourceStateRecord(0, encoded, CancellationToken.None), Is.EqualTo(count));
        Assert.That(encoded.AsSpan(0, expected.Length).ToArray(), Is.EqualTo(expected));
        Assert.That(encoded.AsSpan(expected.Length).ToArray(), Is.All.EqualTo(0xCD));
        var rootArray = FieldOffset(expected, 0, 1);
        var legacyRecord = new EbinRecord(
            new ContentSchemaIdentityAssignment("animation/root-motion-resource").SchemaId, 1, 0,
            [new EbinField(1, 24, 1, 4096, expected.AsSpan(rootArray))]);
        Assert.That(EbinCodec.Prepare(legacyRecord, out _, out var legacyDiagnostic), Is.EqualTo(EbinStatus.InvalidRecord));
        Assert.That(legacyDiagnostic.Category, Is.EqualTo("record-byte-budget"));
        Assert.That(
            () => WorldResourceFactoryBinding<RootMotionRequest, RootMotionResourceAuthor>.Create(
                Guid.NewGuid(), 1, WorldResourceSchema<RootMotionRequest>.FromRegistry(world.Options.Registration.Schemas, fixture.Provider.Factory.Descriptor.Schema),
                fixture.Provider.Factory.Descriptor.PartitionStorage, fixture.Provider.Factory.Family.Fields, fixture.Provider),
            Throws.ArgumentException);
    }

    [Test]
    public void IssuedSmallSnapshotProfileRejectsWideReadAndWriteWithoutFallingBack()
    {
        var fixture = CreateWorld(smallProfile: true);
        using var world = fixture.World;
        _ = world.Step();
        var resource = world.Options.Registration.Resources[0];
        var storage = resource.CreateStorage();
        resource.InitializeAndValidate(storage, world);
        using var lease = world.AcquirePersistenceCaptureLease();
        var rejected = false;

        try
        {
            _ = lease.CountResourceStateRecord(0, CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        Assert.That(rejected, Is.True);
        var wideFixture = CreateWorld();
        using var wideWorld = wideFixture.World;
        var source = BuildLiteral(WorldPersistenceEbinLimits.Create(wideWorld.Options));
        var tick = lease.CompletedTick;
        Assert.That(() => resource.RestoreStateRecord(source, storage, CancellationToken.None, world, tick),
            Throws.TypeOf<InvalidDataException>());
        var destination = new byte[source.Length];
        destination.AsSpan().Fill(0xCD);
        Assert.That(() => resource.WriteStateRecord(storage, destination, CancellationToken.None, world, tick),
            Throws.InvalidOperationException);
        Assert.That(destination, Is.All.EqualTo(0xCD));
        Assert.That(((RootMotionRequest[])storage).All(static value => !value.IsValid), Is.True);
    }

    [TestCase("presence")]
    [TestCase("count")]
    [TestCase("tick")]
    [TestCase("branch")]
    [TestCase("controller")]
    [TestCase("clip-major")]
    [TestCase("duplicate-source")]
    [TestCase("weights")]
    public void MalformedLiteralRejectsWithoutWritingPartitions(string mutation)
    {
        var fixture = CreateWorld();
        using var world = fixture.World;
        var playerEntity = world.CreateEntity(new EntityDescription());
        var actorEntity = world.CreateEntity(new EntityDescription());
        var player = world.GetEntityIdentity(playerEntity);
        var actor = world.GetEntityIdentity(actorEntity);
        _ = world.Step();
        long tick;

        using (var lease = world.AcquirePersistenceCaptureLease())
        {
            tick = lease.CompletedTick;
        }

        var bytes = BuildLiteral(WorldPersistenceEbinLimits.Create(world.Options), Present(player, actor, tick, 4));
        var values = FieldOffset(bytes, 0, 1);
        var slot = ElementOffset(bytes, values, 0);
        var sourceArray = FieldOffset(bytes, slot, 11);
        var source0 = ElementOffset(bytes, sourceArray, 0);
        var source1 = ElementOffset(bytes, sourceArray, 1);

        switch (mutation)
        {
            case "presence":
            {
                bytes[FieldOffset(bytes, slot, 1)] = 0;
                break;
            }

            case "count":
            {
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(64 + 24), 4095);
                break;
            }

            case "tick":
            {
                BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(FieldOffset(bytes, slot, 8)), tick + 1);
                break;
            }

            case "branch":
            {
                _ = Guid.NewGuid().TryWriteBytes(bytes.AsSpan(FieldOffset(bytes, slot, 4), 16), bigEndian: true, out _);
                break;
            }

            case "controller":
            {
                var asset = FieldOffset(bytes, slot, 13);
                _ = new ContentSchemaIdentityAssignment("native/clip").SchemaId.TryWriteBytes(
                    bytes.AsSpan(FieldOffset(bytes, asset, 2), 16), bigEndian: true, out _);
                break;
            }

            case "clip-major":
            {
                var asset = FieldOffset(bytes, source0, 2);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(FieldOffset(bytes, asset, 3)), 2);
                break;
            }

            case "duplicate-source":
            {
                bytes.AsSpan(FieldOffset(bytes, source0, 1), 16).CopyTo(bytes.AsSpan(FieldOffset(bytes, source1, 1), 16));
                break;
            }

            case "weights":
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(FieldOffset(bytes, source0, 5)), BitConverter.SingleToInt32Bits(0.5f));
                break;
            }

            default:
            {
                throw new InvalidOperationException("Unknown literal mutation.");
            }
        }

        var resource = world.Options.Registration.Resources[0];
        var storage = resource.CreateStorage();
        resource.InitializeAndValidate(storage, world);
        Assert.That(() => resource.RestoreStateRecord(bytes, storage, CancellationToken.None, world, tick), Throws.TypeOf<InvalidDataException>());
        Assert.That(((RootMotionRequest[])storage).All(static value => !value.IsValid && value.HasCanonicalStorage), Is.True);
    }

    [Test]
    public void NormalizedWeightPredicatePreservesRoundedVectorsAndItsExactBound()
    {
        Assert.That(RootMotionResourceValidation.IsValidWeights([1.0f]), Is.True);
        Assert.That(RootMotionResourceValidation.IsValidWeights([0.5f]), Is.False);
        Assert.That(RootMotionResourceValidation.IsValidWeights([1.0f / 3.0f, 1.0f / 3.0f, 1.0f / 3.0f]), Is.True);
        const float Upper = 0.5000004768371582f;
        const float Lower = 0.4999995231628418f;
        Assert.That(RootMotionResourceValidation.IsValidWeights([0.5f, Upper]), Is.True);
        Assert.That(RootMotionResourceValidation.IsValidWeights([0.5f, MathF.BitIncrement(Upper)]), Is.False);
        Assert.That(RootMotionResourceValidation.IsValidWeights([0.5f, Lower]), Is.True);
        Assert.That(RootMotionResourceValidation.IsValidWeights([0.5f, MathF.BitDecrement(Lower)]), Is.False);
        Assert.That(RootMotionResourceValidation.IsValidWeights([0.75f, 0.75f]), Is.False);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void RejectsPresentSuffixOrDescendingPlayerOrder(bool suffix)
    {
        var fixture = CreateWorld();
        using var world = fixture.World;
        var player1 = world.CreateEntity(new EntityDescription(persistentId: new Guid("10000000-0000-4000-8000-000000000001")));
        var actor1 = world.CreateEntity(new EntityDescription(persistentId: new Guid("20000000-0000-4000-8000-000000000001")));
        var player2 = world.CreateEntity(new EntityDescription(persistentId: new Guid("30000000-0000-4000-8000-000000000001")));
        var actor2 = world.CreateEntity(new EntityDescription(persistentId: new Guid("40000000-0000-4000-8000-000000000001")));
        _ = world.Step();
        long tick;

        using (var lease = world.AcquirePersistenceCaptureLease())
        {
            tick = lease.CompletedTick;
        }

        var first = Present(world.GetEntityIdentity(player1), world.GetEntityIdentity(actor1), tick, 0);
        var second = Present(world.GetEntityIdentity(player2), world.GetEntityIdentity(actor2), tick, 0);
        var absent = new EbinRecord(new ContentSchemaIdentityAssignment("animation/root-motion-slot").SchemaId, 1, 0,
            [EbinPrimitives.Boolean(1, false)]);
        var bytes = BuildLiteral(WorldPersistenceEbinLimits.Create(world.Options), suffix ? [absent, first] : [second, first]);
        var resource = world.Options.Registration.Resources[0];
        var storage = resource.CreateStorage();
        resource.InitializeAndValidate(storage, world);
        Assert.That(() => resource.RestoreStateRecord(bytes, storage, CancellationToken.None, world, tick),
            Throws.TypeOf<InvalidDataException>());
    }

    [TestCase(0)]
    [TestCase(4)]
    public void RestoresExactPresentSourcesAndRetiredIdentityFacts(int sourceCount)
    {
        var fixture = CreateWorld();
        using var world = fixture.World;
        var playerEntity = world.CreateEntity(new EntityDescription());
        var actorEntity = world.CreateEntity(new EntityDescription());
        var player = world.GetEntityIdentity(playerEntity);
        var actor = world.GetEntityIdentity(actorEntity);
        world.DestroyEntity(playerEntity, HierarchyDestroyMode.DetachChildrenKeepWorld);
        world.DestroyEntity(actorEntity, HierarchyDestroyMode.DetachChildrenKeepWorld);
        _ = world.Step();
        long tick;

        using (var lease = world.AcquirePersistenceCaptureLease())
        {
            tick = lease.CompletedTick;
        }

        var expected = BuildLiteral(WorldPersistenceEbinLimits.Create(world.Options), Present(player, actor, tick, sourceCount));
        Assert.That(expected.Length, Is.EqualTo(sourceCount == 0 ? 492_400 : 494_416));
        var resource = world.Options.Registration.Resources[0];
        var storage = resource.CreateStorage();
        resource.InitializeAndValidate(storage, world);
        resource.RestoreStateRecord(expected, storage, CancellationToken.None, world, tick);
        var values = (RootMotionRequest[])storage;
        Assert.That(values[0].Player.EntityId, Is.EqualTo(player.EntityId));
        Assert.That(values[0].Actor.CreationBranchId, Is.EqualTo(actor.CreationBranchId));
        Assert.That(values[0].SourceCount, Is.EqualTo(sourceCount));
        Assert.That(values.Skip(1).All(static value => !value.IsValid && value.HasCanonicalStorage), Is.True);
        var encoded = new byte[expected.Length + 16];
        encoded.AsSpan().Fill(0xCD);
        Assert.That(resource.CountStateRecord(storage, CancellationToken.None, world, tick), Is.EqualTo((nint)expected.Length));
        Assert.That(resource.WriteStateRecord(storage, encoded, CancellationToken.None, world, tick), Is.EqualTo((nint)expected.Length));
        Assert.That(encoded.AsSpan(0, expected.Length).ToArray(), Is.EqualTo(expected));
        Assert.That(encoded.AsSpan(expected.Length).ToArray(), Is.All.EqualTo(0xCD));

        if (sourceCount != 0)
        {
            Assert.That(BitConverter.SingleToInt32Bits(values[0].Translation.X), Is.EqualTo(unchecked((int)0x80000000)));
            Assert.That(values[0].GetSource(3).Clip.Value, Is.EqualTo(104));
            Assert.That(values[0].GetSource(3).LoopOrdinal, Is.EqualTo(-5));
        }
    }

    [Test]
    public void WholeWriterEnvelopeRefusesBeforeOutputAndReleasesItsCredits()
    {
        var baselineFixture = CreateWorld();
        using var baseline = baselineFixture.World;
        _ = baseline.Step();
        var maximumOwned = checked(baseline.GetWorldOwnedStateBytes()
            + baselineFixture.Provider.GetOperationByteReservation(WorldResourceOperationKind.Count)
            + baselineFixture.Provider.GetOperationByteReservation(WorldResourceOperationKind.Validation));
        var defaults = WorldSchedulingLimits.Default;
        var limits = new WorldSchedulingLimits(
            defaults.MaximumRegistrationBytes, defaults.MaximumGraphBytes, defaults.MaximumWorkPlanBytes,
            defaults.MaximumCommandStagingBytes, maximumOwned,
            defaults.MaximumDiagnosticBytes, defaults.MaximumConflictComparisons);
        var fixture = CreateWorld(schedulingLimits: limits);
        using var world = fixture.World;
        _ = world.Step();
        using var lease = world.AcquirePersistenceCaptureLease();
        var bytes = lease.CountResourceStateRecord(0, CancellationToken.None);
        var before = world.GetWorldOwnedStateBytes();
        var destination = new byte[checked((int)bytes)];
        destination.AsSpan().Fill(0xCD);
        var rejected = false;

        try
        {
            _ = lease.WriteResourceStateRecord(0, destination, CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        Assert.That(rejected, Is.True);
        Assert.That(destination, Is.All.EqualTo(0xCD));
        Assert.That(world.GetWorldOwnedStateBytes(), Is.EqualTo(before));
        Assert.That(lease.CountResourceStateRecord(0, CancellationToken.None), Is.EqualTo(bytes));
    }
}
