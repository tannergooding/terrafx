// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Animation;
using TerraFX.Assets;
using TerraFX.Content.Tools;
using TerraFX.Content.Tools.Tests;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Hosting;
using TerraFX.Rendering;
using TerraFX.Runtime.Tests.Entities;
using TerraFX.Runtime.Tests.Hosting;
using TerraFX.Scenes;
using ParticleEmitterComponent = TerraFX.Rendering.RenderComponentDefinitions.ParticleEmitterComponent;

namespace TerraFX.Runtime.Tests.Rendering;

[TestFixture]
internal sealed class RenderParticleEmitterComponentTests
{
    private static readonly Guid s_componentSchemaId = new ContentSchemaIdentityAssignment("render/particle-emitter").SchemaId;
    private static readonly ContentSchemaReference s_componentSchema = new(s_componentSchemaId, 1, 0, ContentSchemaScope.Content);

    private static byte[] CaptureComponent(World world, Entity entity)
    {
        using var lease = world.AcquirePersistenceCaptureLease();
        var byteCount = lease.CountComponentStateRecord(entity, s_componentSchema, CancellationToken.None);
        var destination = new byte[checked((int)byteCount + 8)];
        Array.Fill(destination, (byte)0xCC);
        var written = lease.WriteComponentStateRecord(
            entity,
            s_componentSchema,
            destination,
            CancellationToken.None);
        Assert.That(written, Is.EqualTo(byteCount));
        Assert.That(destination.AsSpan(checked((int)written)).ToArray(), Is.All.EqualTo(0xCC));
        return destination.AsSpan(0, checked((int)written)).ToArray();
    }

    private static byte[] CreateComponentRecord(uint definitionAssetId, bool enabled, uint omittedTag = 0)
    {
        var definition = new EbinRecord(
            new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId,
            1,
            0,
            [
                EbinPrimitives.UInt32(1, definitionAssetId),
                EbinPrimitives.Guid(2, new ContentSchemaIdentityAssignment("native/cosmetic-particle-emitter").SchemaId),
                EbinPrimitives.UInt32(3, 1),
            ]);
        var fields = new List<EbinField>();

        if (omittedTag != 1)
        {
            fields.Add(EbinPrimitives.Record(1, definition));
        }

        if (omittedTag != 2)
        {
            fields.Add(EbinPrimitives.Boolean(2, enabled));
        }

        return EbinPrimitives.Encode(new EbinRecord(s_componentSchemaId, 1, 0, [.. fields]));
    }

    private static CompiledContentSchemaRegistry CreateSchemas()
        => new([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
            .. AnimationSchemaCatalogue.Schemas,
            .. RenderingSchemaCatalogue.Schemas,
            .. RenderingSchemaCatalogue.M05Schemas,
            .. StandaloneContinuationSchemaCatalogue.Schemas,
            WorldPersistenceWorldRecordWriterTests.CreateApplicationSchema(
                "application/world-root-test/tag/v1", ContentSchemaRole.EntityTag, 0),
            WorldPersistenceWorldRecordWriterTests.CreateApplicationSchema(
                "application/world-root-test/system-state/v1", ContentSchemaRole.SystemState, 7),
            WorldPersistenceWorldRecordWriterTests.CreateApplicationSchema(
                "application/world-root-test/resource-state/v1", ContentSchemaRole.ResourceState, 23, 4),
            WorldPersistenceWorldRecordWriterTests.CreateApplicationSchema(
                "application/world-root-test/attachment-state/v1", ContentSchemaRole.AttachmentState, 8),
        ]);

    private static World CreateWorld(CompiledContentSchemaRegistry? schemas = null, bool sceneSupport = false)
    {
        schemas ??= CreateSchemas();
        var builder = RenderComponentDefinitions.RegisterParticleEmitter(new WorldRegistrationBuilder(schemas), schemas);

        if (sceneSupport)
        {
            _ = builder.RegisterSceneSupport(SceneSupport.CreateRegistration());
        }

        return World.Create(new WorldOptions(builder.Build()) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DefaultInitializationAndDefaultValueDoNotPublishAnEntity(bool useDefaults)
    {
        using var world = CreateWorld();
        var entityId = Guid.NewGuid();
        var invalid = default(ParticleEmitterComponent);
        var initialization = useDefaults
            ? ComponentInitialization.Defaults<ParticleEmitterComponent>()
            : ComponentInitialization.Value(in invalid);
        var revision = world.CanonicalMutationRevision;

        _ = Assert.Throws<InvalidOperationException>(() => world.CreateEntity(new EntityDescription(
            persistentId: entityId,
            components: [initialization])));
        Assert.Multiple(() => {
            Assert.That(world.TryGetEntity(entityId, out _), Is.False);
            Assert.That(world.CanonicalMutationRevision, Is.EqualTo(revision));
            Assert.That(world.LastFault, Is.Null);
        });

        var valid = new ParticleEmitterComponent(1, enabled: true);
        var entity = world.CreateEntity(new EntityDescription(
            persistentId: entityId,
            components: [ComponentInitialization.Value(in valid)]));
        Assert.That(world.GetEntityId(entity), Is.EqualTo(entityId));
    }

    [Test]
    public void FailedSetPreservesTheValueAndCommittedMarker()
    {
        using var world = CreateWorld();
        var value = new ParticleEmitterComponent(37, enabled: true);
        var entity = world.CreateEntity(new EntityDescription(components: [ComponentInitialization.Value(in value)]));
        _ = world.Step();
        var expected = CaptureComponent(world, entity);
        var revision = world.CanonicalMutationRevision;
        var invalid = default(ParticleEmitterComponent);

        _ = Assert.Throws<InvalidOperationException>(() => world.Set(entity, in invalid));

        Assert.Multiple(() => {
            Assert.That(world.CanonicalMutationRevision, Is.EqualTo(revision));
            Assert.That(world.LastFault, Is.Null);
            Assert.That(world.TryGet<ParticleEmitterComponent>(entity, out var actual), Is.True);
            Assert.That(actual.DefinitionAssetId, Is.EqualTo(37));
            Assert.That(actual.Enabled, Is.True);
        });
        Assert.That(CaptureComponent(world, entity), Is.EqualTo(expected));
    }

    [TestCase(1, false)]
    [TestCase(1, true)]
    [TestCase(int.MaxValue, false)]
    [TestCase(int.MaxValue, true)]
    public void RegisteredValuesUseWorldStorageAndCanonicalState(int definitionAssetId, bool enabled)
    {
        using var world = CreateWorld();
        var value = new ParticleEmitterComponent(definitionAssetId, enabled);
        var entity = world.CreateEntity(new EntityDescription(components: [ComponentInitialization.Value(in value)]));
        _ = world.Step();
        Assert.That(CaptureComponent(world, entity), Is.EqualTo(CreateComponentRecord(checked((uint)definitionAssetId), enabled)));

        var replacementId = (definitionAssetId == 1) ? int.MaxValue : 1;
        var replacement = new ParticleEmitterComponent(replacementId, !enabled);
        world.Set(entity, in replacement);
        Assert.Multiple(() => {
            Assert.That(world.TryGet<ParticleEmitterComponent>(entity, out var actual), Is.True);
            Assert.That(actual.DefinitionAssetId, Is.EqualTo(replacementId));
            Assert.That(actual.Enabled, Is.EqualTo(!enabled));
        });
        _ = world.Step();
        Assert.That(CaptureComponent(world, entity), Is.EqualTo(CreateComponentRecord(checked((uint)replacementId), !enabled)));
    }

    [TestCase(0u)]
    [TestCase(0x80000000u)]
    [TestCase(uint.MaxValue)]
    public void SerializedOutOfRangeDefinitionUsesTheReadRejectionChannel(uint definitionAssetId)
    {
        using var fixture = new PersistenceFixture(enabled: false);
        fixture.ReplaceSavedComponent(CreateComponentRecord(definitionAssetId, enabled: false));
        using var preparation = fixture.Prepare();
        Assert.That(preparation.Status, Is.EqualTo(WorldLoadPreparationStatus.Prepared));
        var exception = Assert.Throws<InvalidDataException>(() => fixture.Commit(preparation));
        Assert.That(exception!.Message, Does.Contain("ComponentRead.InvalidValue"));
        Assert.That(fixture.World.TryGetEntity(fixture.EntityId, out var entity), Is.True);
        Assert.That(fixture.World.TryGet<ParticleEmitterComponent>(entity, out var value), Is.True);
        Assert.That(value.DefinitionAssetId, Is.EqualTo(NativeMeshTestData.ParticleEmitterId));
        Assert.That(fixture.World.LastFault, Is.Null);
    }

    [TestCase(1u)]
    [TestCase(2u)]
    public void SerializedRequiredFieldsAreNotRepairedFromDefaults(uint omittedTag)
    {
        using var fixture = new PersistenceFixture(enabled: true);
        fixture.ReplaceSavedComponent(CreateComponentRecord(NativeMeshTestData.ParticleEmitterId, enabled: true, omittedTag));
        using var preparation = fixture.Prepare();
        Assert.Multiple(() => {
            Assert.That(preparation.Status, Is.EqualTo(WorldLoadPreparationStatus.Rejected));
            Assert.That(preparation.Plan, Is.Null);
            Assert.That(preparation.Diagnostics, Is.Not.Empty);
            Assert.That(fixture.World.LastFault, Is.Null);
        });
        Assert.That(fixture.World.TryGetEntity(fixture.EntityId, out var entity), Is.True);
        Assert.That(fixture.World.TryGet<ParticleEmitterComponent>(entity, out var actual), Is.True);
        Assert.That(actual.DefinitionAssetId, Is.EqualTo(NativeMeshTestData.ParticleEmitterId));
        Assert.That(actual.Enabled, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void StandardBranchRestoresTheRegisteredComponentWithoutScenePreparation(bool enabled)
    {
        using var fixture = new PersistenceFixture(enabled);
        using var preparation = fixture.Prepare();
        Assert.That(preparation.Status, Is.EqualTo(WorldLoadPreparationStatus.Prepared));
        fixture.Commit(preparation);
        Assert.That(fixture.World.TryGetEntity(fixture.EntityId, out var entity), Is.True);
        Assert.Multiple(() => {
            Assert.That(fixture.World.TryGet<ParticleEmitterComponent>(entity, out var actual), Is.True);
            Assert.That(actual.DefinitionAssetId, Is.EqualTo(NativeMeshTestData.ParticleEmitterId));
            Assert.That(actual.Enabled, Is.EqualTo(enabled));
        });
        Assert.That(
            CaptureComponent(fixture.World, entity),
            Is.EqualTo(CreateComponentRecord(NativeMeshTestData.ParticleEmitterId, enabled)));
    }

    [Test]
    public void WritableScopeValidatesTheStoredValueAndFaultsTheWorld()
    {
        using var world = CreateWorld();
        var value = new ParticleEmitterComponent(1, enabled: false);
        _ = world.CreateEntity(new EntityDescription(components: [ComponentInitialization.Value(in value)]));
        var query = new EntityQueryDescription(
            [s_componentSchemaId],
            [],
            [new SystemResourceAccess(SystemResourceKind.Component, s_componentSchemaId, SystemAccessMode.Write)]);
        var action = new ClearEmitterAction();
        var exception = Assert.Throws<InvalidOperationException>(() => world.ForEachChunk(query, ref action));

        Assert.Multiple(() => {
            Assert.That(action.Completed, Is.True);
            Assert.That(action.Visited, Is.EqualTo(1));
            Assert.That(exception!.Message, Does.Contain("Definition"));
            Assert.That(world.LastFault?.Code, Is.EqualTo("WORLD_COMPONENT_VALIDATION_FAILED"));
        });
        _ = Assert.Throws<InvalidOperationException>(() => world.Step());
    }

    private struct ClearEmitterAction : IAction<EntityChunkView>
    {
        internal bool Completed { get; private set; }

        internal int Visited { get; private set; }

        public void Invoke(scoped EntityChunkView value)
        {
            var emitters = value.Write<ParticleEmitterComponent>();
            Visited += emitters.Length;
            emitters.Clear();
            Completed = true;
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The standalone owner thread synchronously joins every asset, persistence, and retirement task before disposing its owners.")]
    private sealed class PersistenceFixture : IDisposable
    {
        private readonly AssetStore? _assets;
        private readonly string _directory;
        private readonly WorldPersistence? _persistence;
        private World? _world;

        internal PersistenceFixture(bool enabled)
        {
            _directory = Path.Combine(
                NativeMeshTestData.FindRepositoryRoot(),
                "artifacts",
                "tst",
                "particle-component",
                Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(_directory);
            SavePath = Path.Combine(_directory, "world.state");
            EntityId = Guid.NewGuid();

            try
            {
                var schemas = CreateSchemas();
                var assetBuilder = Renderer.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas));
                _ = Renderer.RegisterM05AssetCodecs(assetBuilder, RendererCpuFeatures.CosmeticParticles);
                var packagePath = NativeMeshTestData.WriteParticlePackageAsync(
                    _directory,
                    NativeMeshTestData.CreateParticleEmitterRecord()).GetAwaiter().GetResult();
                _assets = AssetStore.CreateAsync(new AssetStoreOptions(
                    packagePath,
                    SHA256.HashData(File.ReadAllBytes(packagePath)),
                    assetBuilder.Build())).GetAwaiter().GetResult();
                // Persistence restores Entity hierarchy through the real Scene owner, without preparing Scene content.
                _world = CreateWorld(schemas, sceneSupport: true);
                var value = new ParticleEmitterComponent(checked((int)NativeMeshTestData.ParticleEmitterId), enabled);
                _ = World.CreateEntity(new EntityDescription(
                    persistentId: EntityId,
                    components: [ComponentInitialization.Value(in value)]));
                _ = World.Step();
                _persistence = new WorldPersistence(_assets, WorldPersistenceHeaderWriterTests.CreateMetadata());
                var save = _persistence.SaveAsync(
                    World,
                    new SaveOptions(SavePath, SaveDurabilityPolicy.BestEffort)).GetAwaiter().GetResult();
                Assert.That(save.Status, Is.AnyOf(SaveStatus.BestEffortCommitted, SaveStatus.DurableCommitted), save.Diagnostic?.Message);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal Guid EntityId { get; }

        internal string SavePath { get; }

        internal World World => _world ?? throw new InvalidOperationException("The fixture World is unavailable.");

        private WorldPersistence Persistence
            => _persistence ?? throw new InvalidOperationException("The fixture persistence service is unavailable.");

        private static byte[] ReplaceComponent(
            EbinRecordReadView record,
            byte[] replacement,
            ref int replacements)
        {
            if (record.Header.SchemaId == s_componentSchemaId)
            {
                replacements++;
                return replacement;
            }

            var fields = new EbinField[record.FieldCount];

            for (var index = 0; index < fields.Length; index++)
            {
                var field = record.GetField(index);
                byte[] payload;

                if (field.Kind == 22)
                {
                    payload = ReplaceComponent(record.GetRecord(field.Tag), replacement, ref replacements);
                }
                else if (field.Kind == 24)
                {
                    var records = new byte[checked((int)field.Count)][];

                    for (var element = 0; element < records.Length; element++)
                    {
                        records[element] = ReplaceComponent(record.GetRecordArrayElement(field.Tag, element), replacement, ref replacements);
                    }

                    payload = ContentPackageWriter.EncodeRecordArray(records);
                }
                else
                {
                    payload = field.Payload.ToArray();
                }

                fields[index] = new EbinField(field.Tag, field.Kind, field.IsRequired ? (ushort)1 : (ushort)0, field.Count, payload);
            }

            return EbinPrimitives.Encode(new EbinRecord(
                record.Header.SchemaId,
                checked((uint)record.Header.Major),
                checked((uint)record.Header.Minor),
                [.. fields]));
        }

        public void Dispose()
        {
            _world?.Dispose();
            _persistence?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _assets?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Directory.Delete(_directory, recursive: true);
        }

        internal void Commit(WorldLoadPreparation preparation)
        {
            var plan = preparation.Plan ?? throw new InvalidOperationException("The real preparation omitted its load plan.");
            var receipt = Persistence.CommitLoadAsync(plan).GetAwaiter().GetResult();
            Assert.That(receipt.Status, Is.EqualTo(WorldLoadStatus.Loaded), receipt.Rejection?.Message);
            _world = receipt.ReplacementWorld ?? throw new InvalidOperationException("The real load omitted its replacement World.");
            using var remap = receipt.IdentityRemap;
            (receipt.OldWorldRetirement ?? throw new InvalidOperationException("The load omitted its retirement."))
                .GetAwaiter().GetResult();
        }

        internal WorldLoadPreparation Prepare()
            => Persistence.PrepareLoadAsync(World, SavePath, new LoadOptions(WorldRestoreIntent.Branch) {
                IdentityBranchId = Guid.NewGuid(),
            }).GetAwaiter().GetResult();

        internal void ReplaceSavedComponent(byte[] replacement)
        {
            var limits = WorldPersistenceEbinLimits.Create(World.Options);
            var checkpoint = new EbinRecordReadView(
                WorldPersistenceCheckpointWriter.ReadCheckpointRecord(File.ReadAllBytes(SavePath), limits));
            var header = checkpoint.GetRecord(1).EncodedBytes.ToArray();
            var replacements = 0;
            var worldRecord = ReplaceComponent(
                checkpoint.GetRecord(2),
                replacement,
                ref replacements);
            Assert.That(replacements, Is.EqualTo(1));
            File.WriteAllBytes(
                SavePath,
                WorldPersistenceCheckpointWriter.Create(header, worldRecord, 1, limits).FileRecord);
        }
    }
}
