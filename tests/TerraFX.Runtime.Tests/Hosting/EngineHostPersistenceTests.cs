// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Animation;
using TerraFX.Assets;
using TerraFX.Content.Tools;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Hosting;
using TerraFX.Rendering;
using TerraFX.Runtime.Tests.Entities;
using TerraFX.Runtime.Tests.Scenes;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Hosting;

internal sealed partial class EngineHostOptionsTests
{
    [Test]
    public async Task HeadlessEngineHostPersistsAHostedWorldAsync()
    {
        var schemas = new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
            .. AnimationSchemaCatalogue.Schemas,
            .. RenderingSchemaCatalogue.Schemas,
            .. StandaloneContinuationSchemaCatalogue.Schemas,
            WorldPersistenceWorldRecordWriterTests.CreateApplicationSchema(
                "application/host-world-test/tag/v1",
                ContentSchemaRole.EntityTag,
                0),
            WorldPersistenceWorldRecordWriterTests.CreateApplicationSchema(
                "application/host-world-test/system-state/v1",
                ContentSchemaRole.SystemState,
                7),
            WorldPersistenceWorldRecordWriterTests.CreateApplicationSchema(
                "application/host-world-test/resource-state/v1",
                ContentSchemaRole.ResourceState,
                23,
                4),
            WorldPersistenceWorldRecordWriterTests.CreateApplicationSchema(
                "application/host-world-test/attachment-state/v1",
                ContentSchemaRole.AttachmentState,
                8),
        ]);
        var packageBytes = SceneAssetStoreTests.Package.CreatePackage();
        var packagePath = SceneAssetStoreTests.Package.WritePackage(packageBytes);
        var savePath = Path.Combine(Path.GetTempPath(), $"terrafx-{Guid.NewGuid():N}.state");
        var strictLocalSavePath = Path.Combine(Path.GetTempPath(), $"terrafx-{Guid.NewGuid():N}.strict.state");
        var assetRegistration = new AssetStoreRegistrationBuilder(schemas).Build();
        var assetStoreOptions = new AssetStoreOptions(packagePath, SHA256.HashData(packageBytes), assetRegistration);
        var options = CreateHeadlessOptions(assetStoreOptions, cpuWorkerCount: 0);
        EngineHost? host = null;
        World? world = null;
        World? strictLocalWorld = null;

        try
        {
            host = await EngineHost.StartAsync(options).ConfigureAwait(false);
            var worldRegistration = new WorldRegistrationBuilder(schemas).Build();
            await host.InvokeAsync(engineContext => world = engineContext.CreateWorld(new WorldOptions(worldRegistration) {
                CpuWorkerCount = 0,
                InitialReplayLineageId = Guid.NewGuid(),
                WorldGuid = Guid.NewGuid(),
            })).ConfigureAwait(false);
            await Task.Delay(50).ConfigureAwait(false);
            Task<SaveReceipt>? saveTask = null;

            await host.InvokeAsync(_ => saveTask = host.Persistence.SaveAsync(
                    world!,
                    new SaveOptions(savePath, SaveDurabilityPolicy.BestEffort))).ConfigureAwait(false);

            var receipt = await (saveTask
                ?? throw new InvalidOperationException("The hosted Save operation was not admitted."))
                .ConfigureAwait(false);

            Assert.That(receipt.Status, Is.AnyOf(SaveStatus.DurableCommitted, SaveStatus.BestEffortCommitted));
            Assert.That(receipt.TransactionGeneration, Is.EqualTo(1));
            Assert.That(receipt.Diagnostic, Is.Null);
            Assert.That(File.Exists(savePath), Is.True);
            var fileRecord = await File.ReadAllBytesAsync(savePath).ConfigureAwait(false);
            var limits = WorldPersistenceEbinLimits.Create(world!.Options);
            var identityAssignmentCount = WorldPersistenceCheckpointWriter.MeasureIdentityAssignmentCount(fileRecord, limits);
            Assert.That(identityAssignmentCount, Is.Zero);
            var checkpoint = WorldPersistenceCheckpointWriter.ReadContents(
                fileRecord,
                limits,
                world.Options.Registration,
                identityScratch: new WorldPersistenceIdentityScratchEntry[identityAssignmentCount]);
            var worldContents = checkpoint.WorldContents;

            Assert.Multiple(() => {
                Assert.That(checkpoint.TransactionGeneration, Is.EqualTo(1));
                Assert.That(checkpoint.Mode, Is.EqualTo((byte)WorldPersistenceMode.Standard));
                Assert.That(checkpoint.WorldGuid, Is.EqualTo(world.WorldGuid));
                Assert.That(checkpoint.CanonicalHash.Length, Is.EqualTo(32));
                Assert.That(checkpoint.CheckpointDigest.Length, Is.EqualTo(32));
                Assert.That(checkpoint.HeaderRecord.IsEmpty, Is.False);
                Assert.That(checkpoint.WorldRecord.IsEmpty, Is.False);
                Assert.That(worldContents.Configuration.MaximumLiveEntities, Is.EqualTo(world.Options.MaximumLiveEntities));
                Assert.That(worldContents.Configuration.MaximumSnapshotBytes, Is.EqualTo(world.Options.MaximumSnapshotBytes));
                Assert.That(worldContents.Clock.NextTick, Is.EqualTo(checkpoint.NextTick));
                Assert.That(worldContents.Clock.TickRate, Is.EqualTo(world.Options.TickRate));
                Assert.That(worldContents.Clock.SegmentCount, Is.GreaterThanOrEqualTo(1));
                Assert.That(worldContents.Ecs.WorldGuid, Is.EqualTo(checkpoint.WorldGuid));
                Assert.That(worldContents.Ecs.ReplayLineageId, Is.EqualTo(checkpoint.LineageGuid));
                Assert.That(worldContents.Scenes.InstanceCount, Is.Zero);
                Assert.That(worldContents.Cursors.NextControlSequence, Is.EqualTo(1));
                Assert.That(worldContents.ConfigurationRecord.IsEmpty, Is.False);
                Assert.That(worldContents.ClockRecord.IsEmpty, Is.False);
                Assert.That(worldContents.Frame2Record.IsEmpty, Is.False);
                Assert.That(worldContents.Frame3Record.IsEmpty, Is.False);
                Assert.That(worldContents.EcsRecord.IsEmpty, Is.False);
                Assert.That(worldContents.ScenesRecord.IsEmpty, Is.False);
                Assert.That(worldContents.AssetIdCount, Is.Zero);
                Assert.That(worldContents.ControlRecordCount, Is.Zero);
                Assert.That(worldContents.CursorsRecord.IsEmpty, Is.False);
                Assert.That(worldContents.RandomStreamCount, Is.Zero);
                Assert.That(worldContents.StrictLayoutRecord.IsEmpty, Is.True);
                Assert.That(worldContents.RegistrationRecord.IsEmpty, Is.False);
            });

            var strictLocalHeaderRecord = WorldPersistenceHeaderWriter.Create(
                options.PersistenceMetadata,
                WorldPersistenceMode.StrictLocal,
                SHA256.HashData(packageBytes),
                checkpoint.WorldGuid,
                checkpoint.LineageGuid,
                checkpoint.NextTick,
                checked((byte)host.CpuWorkerCount),
                WorldPersistenceHeaderWriterTests.CreateFoundationProfileRecord());
            var strictLocalCheckpointRecord = WorldPersistenceCheckpointWriter.CreateCheckpoint(
                strictLocalHeaderRecord,
                checkpoint.WorldRecord,
                limits).CheckpointRecord;
            var strictLocalFileRecord = WorldPersistenceCheckpointWriter.CreateFileRecord(
                transactionGeneration: 2,
                checkpointRecord: strictLocalCheckpointRecord,
                limits: limits).FileRecord;
            _ = Assert.Throws<ArgumentException>(() => WorldPersistenceCheckpointWriter.ReadContents(
                strictLocalFileRecord,
                limits,
                world.Options.Registration,
                identityScratch: new WorldPersistenceIdentityScratchEntry[identityAssignmentCount]));

            Task<SaveReceipt>? mismatchedStrictLocalSaveTask = null;
            await host.InvokeAsync(_ => mismatchedStrictLocalSaveTask = host.Persistence.SaveAsync(
                    world!,
                    new SaveOptions(strictLocalSavePath, SaveDurabilityPolicy.BestEffort) {
                        Mode = WorldPersistenceMode.StrictLocal,
                    }))
                .ConfigureAwait(false);
            var mismatchedStrictLocalReceipt = await (mismatchedStrictLocalSaveTask
                ?? throw new InvalidOperationException("The mismatched StrictLocal Save operation was not admitted."))
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(mismatchedStrictLocalReceipt.Status, Is.EqualTo(SaveStatus.Rejected));
                Assert.That(mismatchedStrictLocalReceipt.Diagnostic?.Code, Is.EqualTo("save-mode-profile-mismatch"));
                Assert.That(File.Exists(strictLocalSavePath), Is.False);
            });

            await host.InvokeAsync(engineContext => strictLocalWorld = engineContext.CreateWorld(new WorldOptions(worldRegistration) {
                CpuWorkerCount = 0,
                InitialReplayLineageId = Guid.NewGuid(),
                Profile = SimulationProfile.StrictLocal,
                WorldGuid = Guid.NewGuid(),
            })).ConfigureAwait(false);
            await Task.Delay(50).ConfigureAwait(false);
            Task<SaveReceipt>? strictLocalSaveTask = null;
            await host.InvokeAsync(_ => strictLocalSaveTask = host.Persistence.SaveAsync(
                    strictLocalWorld!,
                    new SaveOptions(strictLocalSavePath, SaveDurabilityPolicy.BestEffort) {
                        Mode = WorldPersistenceMode.StrictLocal,
                    }))
                .ConfigureAwait(false);
            var strictLocalReceipt = await (strictLocalSaveTask
                ?? throw new InvalidOperationException("The hosted StrictLocal Save operation was not admitted."))
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(strictLocalReceipt.Status, Is.AnyOf(SaveStatus.DurableCommitted, SaveStatus.BestEffortCommitted));
                Assert.That(strictLocalReceipt.TransactionGeneration, Is.EqualTo(1));
                Assert.That(strictLocalReceipt.Diagnostic, Is.Null);
                Assert.That(File.Exists(strictLocalSavePath), Is.True);
            });

            var savedStrictLocalWorld = strictLocalWorld
                ?? throw new InvalidOperationException("The StrictLocal World was not created.");
            var strictLocalFileRecordFromSave = await File.ReadAllBytesAsync(strictLocalSavePath).ConfigureAwait(false);
            var strictLocalLimits = WorldPersistenceEbinLimits.Create(savedStrictLocalWorld.Options);
            var strictLocalCheckpointRecordFromSave = WorldPersistenceCheckpointWriter.ReadCheckpointRecord(
                strictLocalFileRecordFromSave,
                strictLocalLimits);
            var strictLocalWorldRecordAction = new StrictLocalWorldRecordAction();
            var strictLocalWorldRecordStatus = EbinStructuralReader.Read(
                strictLocalCheckpointRecordFromSave,
                strictLocalLimits,
                EbinReadPolicy.Canonical,
                ref strictLocalWorldRecordAction,
                out var strictLocalCheckpointBytesConsumed,
                out var strictLocalCheckpointDiagnostic);

            Assert.That(strictLocalWorldRecordStatus, Is.EqualTo(EbinReadStatus.Complete), strictLocalCheckpointDiagnostic?.Category);
            Assert.That(strictLocalCheckpointBytesConsumed, Is.EqualTo(strictLocalCheckpointRecordFromSave.Length));
            var strictLocalWorldRecord = strictLocalWorldRecordAction.WorldRecord
                ?? throw new InvalidOperationException("The StrictLocal checkpoint did not contain its World record.");
            var strictLocalIdentityAssignmentCount = WorldPersistenceCheckpointWriter.MeasureIdentityAssignmentCount(
                strictLocalFileRecordFromSave,
                strictLocalLimits);
            var strictLayoutWorkspace = WorldPersistenceWorldRecordReader.MeasureStrictLayoutReadWorkspace(
                strictLocalWorldRecord,
                strictLocalLimits);
            var strictLayoutBuffers = WorldPersistenceStrictLayoutReadBuffers.Create(strictLayoutWorkspace);
            var strictLocalContents = WorldPersistenceCheckpointWriter.ReadContents(
                strictLocalFileRecordFromSave,
                strictLocalLimits,
                savedStrictLocalWorld.Options.Registration,
                schemas,
                assetIdScratch: [],
                identityScratch: new WorldPersistenceIdentityScratchEntry[strictLocalIdentityAssignmentCount],
                ref strictLayoutBuffers);

            Assert.Multiple(() => {
                Assert.That(strictLocalContents.Mode, Is.EqualTo((byte)WorldPersistenceMode.StrictLocal));
                Assert.That(strictLocalContents.WorldContents.StrictLayoutRecord.IsEmpty, Is.False);
                Assert.That(strictLocalContents.WorldContents.Cursors.AcceptedOwnerRequests, Is.Not.Null);
                Assert.That(strictLocalContents.WorldContents.Ecs.WorldGuid, Is.EqualTo(savedStrictLocalWorld.WorldGuid));
            });

            await host.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (host is not null)
                {
                    await host.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                File.Delete(packagePath);
                File.Delete(savePath);
                File.Delete(string.Concat(savePath, ".bak"));
                File.Delete(strictLocalSavePath);
                File.Delete(string.Concat(strictLocalSavePath, ".bak"));
            }
        }
    }

    private struct StrictLocalWorldRecordAction : IAction<EbinRecordReadView>
    {
        private static readonly Guid s_checkpointSchemaId = new ContentSchemaIdentityAssignment("state/checkpoint").SchemaId;

        internal byte[]? WorldRecord { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
        {
            if (record.Header.SchemaId == s_checkpointSchemaId)
            {
                WorldRecord = record.GetRecord(2).EncodedBytes.ToArray();
            }
        }
    }
}
