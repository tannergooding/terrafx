// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Animation;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Hosting;
using TerraFX.Runtime.Tests.Entities;
using TerraFX.Runtime.Tests.Scenes;
using TerraFX.Rendering;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Hosting;

internal sealed partial class EngineHostOptionsTests
{
    [Test]
    public async Task CommitsPreparedBranchAndAdoptsReplacementAtHostBoundaryAsync()
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
        var savePath = Path.Combine(Path.GetTempPath(), $"terrafx-{Guid.NewGuid():N}.commit.state");
        var assetRegistration = new AssetStoreRegistrationBuilder(schemas).Build();
        var options = CreateHeadlessOptions(
            new AssetStoreOptions(packagePath, SHA256.HashData(packageBytes), assetRegistration),
            cpuWorkerCount: 0);
        EngineHost? host = null;
        World? sourceWorld = null;

        try
        {
            host = await EngineHost.StartAsync(options).ConfigureAwait(false);
            var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
            _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
            var registration = worldRegistrationBuilder.Build();
            var sourceEntityId = Guid.Empty;

            await host.InvokeAsync(context => {
                sourceWorld = context.CreateWorld(new WorldOptions(registration) {
                    CpuWorkerCount = 0,
                    InitialReplayLineageId = Guid.NewGuid(),
                    WorldGuid = Guid.NewGuid(),
                });
                var entity = sourceWorld.CreateEntity(new EntityDescription());
                sourceEntityId = sourceWorld.GetEntityId(entity);
            }).ConfigureAwait(false);

            await Task.Delay(50).ConfigureAwait(false);
            Task<SaveReceipt>? saveTask = null;
            await host.InvokeAsync(_ => saveTask = host.Persistence.SaveAsync(
                    sourceWorld!,
                    new SaveOptions(savePath, SaveDurabilityPolicy.BestEffort)))
                .ConfigureAwait(false);
            var saveReceipt = await (saveTask
                ?? throw new InvalidOperationException("The source World Save was not admitted."))
                .ConfigureAwait(false);
            Assert.That(saveReceipt.Status, Is.AnyOf(SaveStatus.DurableCommitted, SaveStatus.BestEffortCommitted));

            Task<WorldLoadPreparation>? preparationTask = null;
            await host.InvokeAsync(_ => preparationTask = host.Persistence.PrepareLoadAsync(
                    sourceWorld!,
                    savePath,
                    new LoadOptions(WorldRestoreIntent.Branch) {
                        IdentityBranchId = Guid.NewGuid(),
                    }))
                .ConfigureAwait(false);
            using var preparation = await (preparationTask
                ?? throw new InvalidOperationException("The target-bound Load preparation was not admitted."))
                .ConfigureAwait(false);
            Assert.That(preparation.Status, Is.EqualTo(WorldLoadPreparationStatus.Prepared));
            var plan = preparation.Plan
                ?? throw new InvalidOperationException("The validated checkpoint did not produce its Load plan.");
            var sourceWorldGuid = sourceWorld!.WorldGuid;
            var targetNextTick = plan.SnapshotTick;

            var standalonePersistence = new WorldPersistence(host.Assets, CreatePersistenceMetadata());

            try
            {
                Task<WorldLoadReceipt>? standaloneLoadTask = null;
                await host.InvokeAsync(_ => standaloneLoadTask = standalonePersistence.CommitLoadAsync(plan))
                    .ConfigureAwait(false);
                var standaloneReceipt = await (standaloneLoadTask
                    ?? throw new InvalidOperationException("The standalone persistence service omitted its Load result."))
                    .ConfigureAwait(false);
                Assert.That(standaloneReceipt.Status, Is.EqualTo(WorldLoadStatus.Rejected));
                Assert.That(standaloneReceipt.Rejection?.Code, Is.EqualTo("load-target-binding-stale"));
            }
            finally
            {
                await standalonePersistence.DisposeAsync().ConfigureAwait(false);
            }

            Task<WorldLoadReceipt>? loadReceiptTask = null;
            await host.InvokeAsync(_ => {
                loadReceiptTask = host.Persistence.CommitLoadAsync(plan);
                preparation.Dispose();
            }).ConfigureAwait(false);

            var loadReceipt = await (loadReceiptTask
                ?? throw new InvalidOperationException("The prepared World replacement was not captured."))
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(loadReceipt.Status, Is.EqualTo(WorldLoadStatus.Loaded), loadReceipt.Rejection?.Message);
                Assert.That(loadReceipt.ReplacementWorld, Is.Not.Null);
                Assert.That(loadReceipt.ReplacementWorld, Is.Not.SameAs(sourceWorld));
                Assert.That(loadReceipt.ReplacementWorld!.WorldGuid, Is.EqualTo(sourceWorldGuid));
                Assert.That(loadReceipt.LoadedTick, Is.EqualTo(targetNextTick));
                Assert.That(loadReceipt.IdentityRemap, Is.Not.Null);
            });

            var replacementWorld = loadReceipt.ReplacementWorld
                ?? throw new InvalidOperationException("The successful load receipt omitted its replacement World.");
            Assert.That(loadReceipt.IdentityRemap!.TryMapEntity(sourceEntityId, out var mappedEntityId), Is.True);
            Assert.That(mappedEntityId, Is.EqualTo(sourceEntityId));

            await (loadReceipt.OldWorldRetirement
                ?? throw new InvalidOperationException("The successful load receipt omitted old-World retirement."))
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);

            var nextTickBeforePump = 0L;
            await host.InvokeAsync(_ => nextTickBeforePump = replacementWorld.NextTickIndex).ConfigureAwait(false);

            var nextTickAfterPump = 0L;

            for (var attempt = 0; (attempt < 100) && (nextTickAfterPump <= nextTickBeforePump); attempt++)
            {
                await Task.Delay(50).ConfigureAwait(false);
                await host.InvokeAsync(_ => nextTickAfterPump = replacementWorld.NextTickIndex).ConfigureAwait(false);
            }

            Assert.That(nextTickAfterPump, Is.GreaterThan(nextTickBeforePump));
        }
        finally
        {
            if (host is not null)
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }

            File.Delete(packagePath);
            File.Delete(savePath);
            File.Delete(string.Concat(savePath, ".bak"));
        }
    }
}
