// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX.Assets;
using TerraFX.Graphics;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

internal sealed partial class SceneAssetStoreTests
{
    [Test]
    public async Task ScenePackageLeaseDescribesOnlyFactoryRegisteredSceneAndPrefabReads()
    {
        const int prefabAssetId = 1;
        const int sceneAssetId = 2;
        var schemas = new CompiledContentSchemaRegistry([
            .. SceneAssetReaderTests.Fixtures.CreateRegistry().Schemas,
        ]);
        var registration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
        var scene = SceneAssetReaderTests.Fixtures.CreateScene(new AssetId(sceneAssetId));
        var prefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(prefabAssetId),
            entityId: new Guid("10000000-0000-4000-8000-000000000001"));
        var package = Package.CreatePackage(
            new Package.Entry(prefabAssetId, Package.CreateEnvelope(prefabAssetId, prefab)),
            new Package.Entry(sceneAssetId, Package.CreateEnvelope(sceneAssetId, scene)));
        var path = Package.WritePackage(package);
        var store = await AssetStore.CreateAsync(
            new AssetStoreOptions(path, SHA256.HashData(package), registration)).ConfigureAwait(false);

        try
        {
            using var packageLease = store.AcquirePackageLease();

            Assert.Multiple(() => {
                Assert.That(
                    SceneSupport.TryDescribeSceneAssetRead(store, new AssetId(sceneAssetId), out _),
                    Is.True);
                Assert.That(
                    SceneSupport.TryDescribePrefabAssetRead(packageLease, new AssetId(prefabAssetId), out _),
                    Is.True);
                Assert.That(
                    SceneSupport.TryDescribeSceneAssetRead(packageLease, new AssetId(sceneAssetId), out _),
                    Is.True);
                Assert.That(
                    SceneSupport.TryDescribeSceneAssetRead(packageLease, new AssetId(99), out _),
                    Is.False);
            });

            using var sceneLease = await store.LoadAsync<SceneAsset>(new AssetId(sceneAssetId)).ConfigureAwait(false);
            Assert.That(
                SceneSupport.TryDescribePrefabAssetRead(sceneLease, new AssetId(prefabAssetId), out _),
                Is.True);
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(path);
        }
    }

    [Test]
    public async Task ScenePrefabExpansionUsesRootSceneLeaseBackingAfterStoreReplacement()
    {
        var rootEntityId = new Guid("10000000-0000-4000-8000-000000000001");
        var authoredPrefabEntityId = new Guid("10000000-0000-4000-8000-000000000002");
        var replacementPrefabEntityId = new Guid("10000000-0000-4000-8000-000000000003");
        var placementId = new Guid("20000000-0000-4000-8000-000000000001");
        const int prefabAssetId = 1;
        const int sceneAssetId = 2;
        var schemas = new CompiledContentSchemaRegistry([
            .. SceneAssetReaderTests.Fixtures.CreateRegistry().Schemas,
        ]);
        var registrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = SceneSupport.RegisterAssetCodecs(registrationBuilder);
        var registration = registrationBuilder.Build();
        var authoredPrefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(prefabAssetId),
            entityId: authoredPrefabEntityId);
        var scene = SceneAssetReaderTests.Fixtures.CreateSceneWithChildPlacement(
            new AssetId(sceneAssetId),
            rootEntityId,
            placementId,
            SceneAssetReaderTests.Fixtures.CreateRootPose(
                authoredPrefabEntityId,
                includeRootPosition: false));
        var replacementPrefab = SceneAssetReaderTests.Fixtures.CreatePrefab(
            new AssetId(prefabAssetId),
            entityId: replacementPrefabEntityId);
        var originalPackage = Package.CreatePackage(
            new Package.Entry(prefabAssetId, Package.CreateEnvelope(prefabAssetId, authoredPrefab)),
            new Package.Entry(sceneAssetId, Package.CreateEnvelope(sceneAssetId, scene)));
        var replacementPackage = Package.CreatePackage(
            new Package.Entry(prefabAssetId, Package.CreateEnvelope(prefabAssetId, replacementPrefab)));
        var originalPath = Package.WritePackage(originalPackage);
        var replacementPath = Package.WritePackage(replacementPackage);
        var store = await AssetStore.CreateAsync(
            new AssetStoreOptions(originalPath, SHA256.HashData(originalPackage), registration)).ConfigureAwait(false);
        AssetLease<SceneAsset>? sceneLease = null;
        SceneExpansionPlan? plan = null;

        try
        {
            sceneLease = await store.LoadAsync<SceneAsset>(new AssetId(sceneAssetId)).ConfigureAwait(false);
            await store.ReplacePackageAsync(
                new AssetStoreOptions(replacementPath, SHA256.HashData(replacementPackage), registration),
                CancellationToken.None).ConfigureAwait(false);

            var transferredSceneLease = sceneLease;
            sceneLease = null;
            plan = await ScenePrefabExpansionPlanner.PrepareTakingLeaseAsync(
                transferredSceneLease,
                SceneExpansionLimits.Default).ConfigureAwait(false);
            Assert.That(plan.Occurrences, Has.Length.EqualTo(2));
            Assert.That(plan.Entities, Has.Length.EqualTo(2));
            Assert.That(plan.Entities[1].AuthoredEntityId, Is.EqualTo(authoredPrefabEntityId));
        }
        finally
        {
            plan?.Dispose();
            sceneLease?.Dispose();
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(originalPath);
            File.Delete(replacementPath);
        }
    }
}
