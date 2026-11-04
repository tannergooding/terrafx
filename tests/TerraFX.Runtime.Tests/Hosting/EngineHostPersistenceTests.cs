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
        var assetRegistration = new AssetStoreRegistrationBuilder(schemas).Build();
        var assetStoreOptions = new AssetStoreOptions(packagePath, SHA256.HashData(packageBytes), assetRegistration);
        var options = CreateHeadlessOptions(assetStoreOptions, cpuWorkerCount: 0);
        EngineHost? host = null;
        World? world = null;

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
            Assert.That(
                WorldPersistenceCheckpointWriter.ReadTransactionGeneration(
                    await File.ReadAllBytesAsync(savePath).ConfigureAwait(false),
                    WorldPersistenceEbinLimits.Create(world!.Options)),
                Is.EqualTo(1));

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
            }
        }
    }
}
