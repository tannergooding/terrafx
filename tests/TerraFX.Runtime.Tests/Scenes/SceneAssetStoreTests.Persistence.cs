// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
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
using TerraFX.Rendering;
using TerraFX.Runtime.Tests.Entities;
using TerraFX.Runtime.Tests.Hosting;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

internal sealed partial class SceneAssetStoreTests
{
    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2025",
        Justification = "Both returned save tasks are awaited before World disposal; SaveAsync captures synchronously and retains independent file-I/O state.")]
    [SuppressMessage(
        "Performance",
        "CA1849",
        Justification = "Blocking joins keep World disposal on its owner thread after asynchronous file I/O completes.")]
    public async Task SaveAsyncWritesSuccessiveAtomicGenerations()
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
        ]);
        var assetRegistration = new AssetStoreRegistrationBuilder(schemas).Build();
        var packageBytes = Package.CreatePackage();
        var packagePath = Package.WritePackage(packageBytes);
        var savePath = Path.Combine(Path.GetTempPath(), $"terrafx-{Guid.NewGuid():N}.state");
        var assets = await AssetStore.CreateAsync(
            new AssetStoreOptions(packagePath, SHA256.HashData(packageBytes), assetRegistration)).ConfigureAwait(false);

        try
        {
            var worldRegistration = new WorldRegistrationBuilder(schemas).Build();
            using var world = World.Create(new WorldOptions(worldRegistration) {
                CpuWorkerCount = 0,
                InitialReplayLineageId = Guid.NewGuid(),
                WorldGuid = Guid.NewGuid(),
            });
            _ = world.Step();
            var persistence = new WorldPersistence(assets, WorldPersistenceHeaderWriterTests.CreateMetadata());

            try
            {
                var options = new SaveOptions(savePath, SaveDurabilityPolicy.BestEffort);
                var firstSave = persistence.SaveAsync(world, options);
                var secondSave = persistence.SaveAsync(world, options);
                var firstReceipt = firstSave.GetAwaiter().GetResult();
                var secondReceipt = secondSave.GetAwaiter().GetResult();

                Assert.Multiple(() => {
                    Assert.That(
                        firstReceipt.Status is SaveStatus.DurableCommitted or SaveStatus.BestEffortCommitted,
                        Is.True);
                    Assert.That(secondReceipt.Status, Is.EqualTo(firstReceipt.Status));
                    Assert.That(firstReceipt.Diagnostic, Is.Null);
                    Assert.That(secondReceipt.Diagnostic, Is.Null);
                    Assert.That(firstReceipt.TransactionGeneration, Is.EqualTo(1));
                    Assert.That(secondReceipt.TransactionGeneration, Is.EqualTo(2));
                });

                var limits = WorldPersistenceEbinLimits.Create(world.Options);
                Assert.That(
                    WorldPersistenceCheckpointWriter.ReadTransactionGeneration(File.ReadAllBytesAsync(savePath).GetAwaiter().GetResult(), limits),
                    Is.EqualTo(2));
                Assert.That(
                    WorldPersistenceCheckpointWriter.ReadTransactionGeneration(
                        File.ReadAllBytesAsync(string.Concat(savePath, ".bak")).GetAwaiter().GetResult(),
                        limits),
                    Is.EqualTo(1));
            }
            finally
            {
                persistence.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        finally
        {
            await assets.DisposeAsync().ConfigureAwait(false);
            File.Delete(packagePath);
            File.Delete(savePath);
            File.Delete(string.Concat(savePath, ".bak"));
        }
    }
}
