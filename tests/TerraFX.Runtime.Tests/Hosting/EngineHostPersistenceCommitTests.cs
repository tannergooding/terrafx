// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Animation;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Hosting;
using TerraFX.Input;
using TerraFX.Platform;
using TerraFX.Runtime.Tests.Entities;
using TerraFX.Runtime.Tests.Scenes;
using TerraFX.Rendering;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Hosting;

[TestFixture]
internal sealed class WorldPersistenceCommitTests
{
    private static EngineHostOptions CreateCommitHeadlessOptions(AssetStoreOptions assetStoreOptions)
        => new(
            HostProfile.Headless,
            PlatformBackend.Headless,
            graphicsBackend: null,
            graphicsBackendRegistration: null,
            assetStoreOptions: assetStoreOptions) {
            CpuWorkerCount = 0,
            GraphicsFrameSlotCount = 3,
            OverrunPolicy = SimulationOverrunPolicy.Pause,
            GraphicsFailurePolicy = GraphicsFailurePolicy.StopHost,
            ClipboardServicePolicy = ClipboardServicePolicy.NotRequested,
            PersistenceMetadata = CreateCommitPersistenceMetadata(),
            ShutdownTimeout = TimeSpan.FromSeconds(5),
        };

    private static WorldPersistenceMetadata CreateCommitPersistenceMetadata()
    {
        var digest = new byte[32];
        var application = new WorldPersistenceApplication(
            new Guid("10000000-0000-0000-0000-000000000000"),
            majorVersion: 1,
            minorVersion: 0,
            modules: []);
        var runtimeProfile = new WorldPersistenceRuntimeProfile(
            digest,
            digest,
            digest,
            WorldPersistenceOperatingSystem.Windows,
            WorldPersistenceCodeGeneration.Jit,
            WorldPersistenceInstructionSet.None,
            digest,
            WorldPersistenceFmaPolicy.Disallowed,
            maximumSimdBits: 0,
            modules: []);

        return new WorldPersistenceMetadata(application, runtimeProfile);
    }

    private static CompiledContentSchemaRegistry CreateCommitSchemas()
        => new([
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

    private static WorldRegistration CreateDomainClosureRegistration(
        CompiledContentSchemaRegistry schemas,
        DomainClosureState state)
    {
        var binding = WorldSystemFactoryBinding.Stateless<
            DomainClosureSystem,
            DomainClosureSystemCreate,
            DomainClosureSystemRelease>(
            Guid.NewGuid(),
            1,
            new WorldSystemSourceStateDeclaration(typeof(DomainClosureSystem), []),
            new DomainClosureSystemCreate(state),
            new DomainClosureSystemRelease(state));
        var builder = new WorldRegistrationBuilder(schemas);
        _ = builder.RegisterSceneSupport(SceneSupport.CreateRegistration());
        _ = builder.RegisterSystem(
            new SystemDescription(
                Guid.NewGuid(),
                0,
                SimulationStage.Input,
                [],
                [],
                [],
                SystemThreadMode.OwnerThread),
            new DomainClosureSystemFactory(binding));
        return builder.Build();
    }

    [Test]
    public async Task CommitsPreparedBranchAndAdoptsReplacementAtHostBoundaryAsync()
    {
        var schemas = CreateCommitSchemas();
        var packageBytes = SceneAssetStoreTests.Package.CreatePackage();
        var packagePath = SceneAssetStoreTests.Package.WritePackage(packageBytes);
        var savePath = Path.Combine(Path.GetTempPath(), $"terrafx-{Guid.NewGuid():N}.commit.state");
        var assetRegistration = new AssetStoreRegistrationBuilder(schemas).Build();
        var options = CreateCommitHeadlessOptions(
            new AssetStoreOptions(packagePath, SHA256.HashData(packageBytes), assetRegistration));
        EngineHost? host = null;
        World? sourceWorld = null;
        InputRouter? inputRouter = null;
        InputRecorder? inputRecorder = null;

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
                inputRouter = context.CreateInputRouter(InputRecipientKind.SimulationTimeline);
                Assert.That(
                    context.BindHeadlessGameplayInput(inputRouter, sourceWorld).Status,
                    Is.EqualTo(InputBindingStatus.Bound));
                inputRecorder = inputRouter.StartRecording();
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

            var standalonePersistence = new WorldPersistence(host.Assets, CreateCommitPersistenceMetadata());

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

            InputRoutingReceipt? inputReceipt = null;
            var expectedInputTick = 0L;

            await host.InvokeAsync(context => {
                expectedInputTick = replacementWorld.NextTickIndex;
                inputReceipt = context.InjectHeadlessInput(
                    [inputRouter!],
                    new InputInjection(new KeyboardControl(7, 4), InputKeyEventKind.Down))[0];
                Assert.That(inputRecorder!.IsComplete, Is.False);
            }).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(inputReceipt!.Status, Is.EqualTo(InputRoutingStatus.Applied));
                Assert.That(inputReceipt!.AssignedTick, Is.EqualTo(expectedInputTick));
            });

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

    [Test]
    public async Task CommitsPreparedBranchCapacityRefusalAsync()
    {
        var schemas = CreateCommitSchemas();
        var packageBytes = SceneAssetStoreTests.Package.CreatePackage();
        var packagePath = SceneAssetStoreTests.Package.WritePackage(packageBytes);
        var savePath = Path.Combine(Path.GetTempPath(), $"terrafx-{Guid.NewGuid():N}.queue-full.state");
        var assetRegistration = new AssetStoreRegistrationBuilder(schemas).Build();
        var options = CreateCommitHeadlessOptions(
            new AssetStoreOptions(packagePath, SHA256.HashData(packageBytes), assetRegistration));
        EngineHost? host = null;
        World? world = null;
        using var releaseOwner = new ManualResetEventSlim();

        try
        {
            host = await EngineHost.StartAsync(options).ConfigureAwait(false);
            var activeHost = host;
            Task<SaveReceipt>? saveTask = null;
            await activeHost.InvokeAsync(context => {
                var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
                _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
                world = context.CreateWorld(new WorldOptions(worldRegistrationBuilder.Build()) {
                    CpuWorkerCount = 0,
                    InitialReplayLineageId = Guid.NewGuid(),
                    WorldGuid = Guid.NewGuid(),
                });
                _ = world!.CreateEntity(new EntityDescription());
                _ = world!.Step();
                saveTask = activeHost.Persistence.SaveAsync(
                    world!,
                    new SaveOptions(savePath, SaveDurabilityPolicy.BestEffort));
            }).ConfigureAwait(false);
            var save = await (saveTask
                ?? throw new InvalidOperationException("The queue-full Load source Save was not admitted."))
                .ConfigureAwait(false);
            Assert.That(save.Status, Is.AnyOf(SaveStatus.DurableCommitted, SaveStatus.BestEffortCommitted));

            Task<WorldLoadPreparation>? preparationTask = null;
            await activeHost.InvokeAsync(_ => preparationTask = activeHost.Persistence.PrepareLoadAsync(
                    world!,
                    savePath,
                    new LoadOptions(WorldRestoreIntent.Branch) {
                        IdentityBranchId = Guid.NewGuid(),
                    }))
                .ConfigureAwait(false);
            using var preparation = await (preparationTask
                ?? throw new InvalidOperationException("The queue-full Load preparation was not admitted."))
                .ConfigureAwait(false);
            Assert.That(preparation.Status, Is.EqualTo(WorldLoadPreparationStatus.Prepared));
            var plan = preparation.Plan
                ?? throw new InvalidOperationException("The queue-full checkpoint did not produce its Load plan.");

            using var ownerBlocked = new ManualResetEventSlim();
            var blockerTask = activeHost.InvokeAsync(_ => {
                ownerBlocked.Set();
                releaseOwner.Wait();
            });
            Assert.That(ownerBlocked.Wait(TimeSpan.FromSeconds(5)), Is.True);

            var queuedRequests = new List<Task>();
            var queueFull = false;

            for (var index = 0; index < 1024; index++)
            {
                var request = activeHost.InvokeAsync(_ => { });

                if (request.IsFaulted)
                {
                    queueFull = true;
                    break;
                }

                queuedRequests.Add(request);
            }

            Assert.That(queueFull, Is.True);
            var receipt = await activeHost.Persistence.CommitLoadAsync(plan).ConfigureAwait(false);
            Assert.That(receipt.Status, Is.EqualTo(WorldLoadStatus.Rejected));
            Assert.That(receipt.Rejection?.Code, Is.EqualTo("load-owner-queue-full"));
            Assert.That(receipt.ReplacementWorld, Is.Null);
            Assert.That(receipt.IdentityRemap, Is.Null);

            releaseOwner.Set();
            await blockerTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await Task.WhenAll(queuedRequests).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        finally
        {
            releaseOwner.Set();

            if (host is not null)
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }

            File.Delete(packagePath);
            File.Delete(savePath);
            File.Delete(string.Concat(savePath, ".bak"));
        }
    }

    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The standalone owner-thread test synchronously joins every persistence and retirement task before disposing its World.")]
    public void CommitsPreparedBranchForStandaloneWorld()
    {
        var schemas = CreateCommitSchemas();
        var packageBytes = SceneAssetStoreTests.Package.CreatePackage();
        var packagePath = SceneAssetStoreTests.Package.WritePackage(packageBytes);
        var savePath = Path.Combine(Path.GetTempPath(), $"terrafx-{Guid.NewGuid():N}.standalone-commit.state");
        AssetStore? assets = null;
        WorldPersistence? persistence = null;
        World? sourceWorld = null;
        World? replacementWorld = null;

        try
        {
            var assetRegistration = new AssetStoreRegistrationBuilder(schemas).Build();
            assets = AssetStore.CreateAsync(new AssetStoreOptions(
                packagePath,
                SHA256.HashData(packageBytes),
                assetRegistration)).GetAwaiter().GetResult();
            var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
            _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
            sourceWorld = World.Create(new WorldOptions(worldRegistrationBuilder.Build()) {
                CpuWorkerCount = 0,
                InitialReplayLineageId = Guid.NewGuid(),
                WorldGuid = Guid.NewGuid(),
            });
            persistence = new WorldPersistence(assets, CreateCommitPersistenceMetadata());

            var entity = sourceWorld.CreateEntity(new EntityDescription());
            var entityId = sourceWorld.GetEntityId(entity);
            _ = sourceWorld.Step();
            var save = persistence.SaveAsync(
                sourceWorld,
                new SaveOptions(savePath, SaveDurabilityPolicy.BestEffort)).GetAwaiter().GetResult();
            Assert.That(save.Status, Is.AnyOf(SaveStatus.DurableCommitted, SaveStatus.BestEffortCommitted));
            using var preparation = persistence.PrepareLoadAsync(
                sourceWorld,
                savePath,
                new LoadOptions(WorldRestoreIntent.Branch) {
                    IdentityBranchId = Guid.NewGuid(),
                }).GetAwaiter().GetResult();
            Assert.That(preparation.Status, Is.EqualTo(WorldLoadPreparationStatus.Prepared));
            var plan = preparation.Plan
                ?? throw new InvalidOperationException("The standalone checkpoint did not produce its Load plan.");
            var receipt = persistence.CommitLoadAsync(plan).GetAwaiter().GetResult();
            Assert.That(receipt.Status, Is.EqualTo(WorldLoadStatus.Loaded), receipt.Rejection?.Message);
            replacementWorld = receipt.ReplacementWorld
                ?? throw new InvalidOperationException("The standalone Load omitted its replacement World.");
            Assert.That(replacementWorld, Is.Not.SameAs(sourceWorld));
            Assert.That(receipt.IdentityRemap!.TryMapEntity(entityId, out var mappedId), Is.True);
            Assert.That(mappedId, Is.EqualTo(entityId));
            (receipt.OldWorldRetirement
                ?? throw new InvalidOperationException("The standalone Load omitted old-World retirement."))
                .WaitAsync(TimeSpan.FromSeconds(5))
                .GetAwaiter()
                .GetResult();
        }
        finally
        {
            replacementWorld?.Dispose();
            sourceWorld?.Dispose();
            persistence?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            assets?.DisposeAsync().AsTask().GetAwaiter().GetResult();

            File.Delete(packagePath);
            File.Delete(savePath);
            File.Delete(string.Concat(savePath, ".bak"));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The standalone owner-thread test synchronously joins every persistence and retirement task before disposing its Worlds.")]
    public void CompletesCapturedLoadAfterExternalDomainClosure(bool includeActiveScene)
    {
        var schemas = CreateCommitSchemas();
        var sceneAssetId = new AssetId(1);
        var packageBytes = includeActiveScene
            ? SceneAssetStoreTests.Package.CreatePackage(
                new SceneAssetStoreTests.Package.Entry(
                    checked((uint)sceneAssetId.Value),
                    SceneAssetStoreTests.Package.CreateEnvelope(
                        checked((uint)sceneAssetId.Value),
                        SceneAssetReaderTests.Fixtures.CreateScene(sceneAssetId))))
            : SceneAssetStoreTests.Package.CreatePackage();
        var packagePath = SceneAssetStoreTests.Package.WritePackage(packageBytes);
        var savePath = Path.Combine(Path.GetTempPath(), $"terrafx-{Guid.NewGuid():N}.captured-closed-domain.state");
        using var domain = new WorldAdmissionDomain(8_589_934_592L);
        var closureState = new DomainClosureState(domain);
        AssetStore? assets = null;
        WorldPersistence? persistence = null;
        World? world = null;
        World? replacementWorld = null;
        Guid? sourceInstanceId = null;

        try
        {
            var assetRegistrationBuilder = new AssetStoreRegistrationBuilder(schemas);

            if (includeActiveScene)
            {
                _ = SceneSupport.RegisterAssetCodecs(assetRegistrationBuilder);
            }

            var assetRegistration = assetRegistrationBuilder.Build();
            assets = AssetStore.CreateAsync(new AssetStoreOptions(
                packagePath,
                SHA256.HashData(packageBytes),
                assetRegistration)).GetAwaiter().GetResult();
            world = World.Create(new WorldOptions(CreateDomainClosureRegistration(schemas, closureState)) {
                AdmissionDomain = domain,
                CpuWorkerCount = 0,
                InitialReplayLineageId = Guid.NewGuid(),
                MaximumPersistenceAdmissionBytes = 8_589_934_592L,
                MaximumSnapshotBytes = 16L * 1024 * 1024,
                WorldGuid = Guid.NewGuid(),
            });
            persistence = new WorldPersistence(assets, CreateCommitPersistenceMetadata());

            if (includeActiveScene)
            {
                var sceneLoader = new SceneLoader(world, assets);
                using var preparedScene = sceneLoader
                    .PrepareAsync(sceneAssetId, new SceneLoadOptions())
                    .GetAwaiter()
                    .GetResult();
                var activationTask = sceneLoader.ActivateAsync(preparedScene);
                world.PumpControlBoundary();
                var activation = activationTask.GetAwaiter().GetResult();
                Assert.That(activation.Status, Is.EqualTo(SceneActivationStatus.Activated));
                sourceInstanceId = activation.Instance
                    ?.SceneInstanceId
                    ?? throw new InvalidOperationException("The activated Scene omitted its instance.");
            }
            else
            {
                _ = world.CreateEntity(new EntityDescription());
            }

            _ = world.Step();
            var save = persistence.SaveAsync(
                world,
                new SaveOptions(savePath, SaveDurabilityPolicy.BestEffort)).GetAwaiter().GetResult();
            Assert.That(save.Status, Is.AnyOf(SaveStatus.DurableCommitted, SaveStatus.BestEffortCommitted), save.Diagnostic?.Message);
            using var preparation = persistence.PrepareLoadAsync(
                world,
                savePath,
                new LoadOptions(WorldRestoreIntent.Branch) {
                    IdentityBranchId = Guid.NewGuid(),
                    MaximumSnapshotBytes = 16L * 1024 * 1024,
                }).GetAwaiter().GetResult();
            Assert.That(preparation.Status, Is.EqualTo(WorldLoadPreparationStatus.Prepared));
            var plan = preparation.Plan
                ?? throw new InvalidOperationException("The external-domain checkpoint did not produce its Load plan.");

            closureState.CloseDomainOnCreate = true;
            var receipt = persistence.CommitLoadAsync(plan).GetAwaiter().GetResult();
            Assert.That(closureState.DomainWasClosed, Is.True);
            Assert.That(domain.Retirement.IsCompleted, Is.False);
            Assert.That(receipt.Status, Is.EqualTo(WorldLoadStatus.Loaded), receipt.Rejection?.Message);
            replacementWorld = receipt.ReplacementWorld
                ?? throw new InvalidOperationException("The captured Load omitted its replacement World.");

            if (sourceInstanceId is Guid expectedInstanceId)
            {
                Assert.That(
                    receipt.IdentityRemap!.TryMapSceneInstance(expectedInstanceId, out var mappedInstanceId),
                    Is.True);
                Assert.That(mappedInstanceId, Is.EqualTo(expectedInstanceId));
            }

            (receipt.OldWorldRetirement
                ?? throw new InvalidOperationException("The captured Load omitted old-World retirement."))
                .WaitAsync(TimeSpan.FromSeconds(5))
                .GetAwaiter()
                .GetResult();
        }
        finally
        {
            replacementWorld?.Dispose();
            world?.Dispose();
            persistence?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            assets?.DisposeAsync().AsTask().GetAwaiter().GetResult();

            File.Delete(packagePath);
            File.Delete(savePath);
            File.Delete(string.Concat(savePath, ".bak"));
        }
    }

    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The standalone owner-thread test synchronously joins every persistence task before disposing its World.")]
    public void RejectsLoadAfterTargetBecomesStaleDuringCandidateConstruction()
    {
        var schemas = CreateCommitSchemas();
        var packageBytes = SceneAssetStoreTests.Package.CreatePackage();
        var packagePath = SceneAssetStoreTests.Package.WritePackage(packageBytes);
        var savePath = Path.Combine(Path.GetTempPath(), $"terrafx-{Guid.NewGuid():N}.candidate-stale-target.state");
        using var domain = new WorldAdmissionDomain(8_589_934_592L);
        var callbackState = new DomainClosureState(domain);
        AssetStore? assets = null;
        WorldPersistence? persistence = null;
        World? world = null;

        try
        {
            var assetRegistration = new AssetStoreRegistrationBuilder(schemas).Build();
            assets = AssetStore.CreateAsync(new AssetStoreOptions(
                packagePath,
                SHA256.HashData(packageBytes),
                assetRegistration)).GetAwaiter().GetResult();
            world = World.Create(new WorldOptions(CreateDomainClosureRegistration(schemas, callbackState)) {
                AdmissionDomain = domain,
                CpuWorkerCount = 0,
                InitialReplayLineageId = Guid.NewGuid(),
                MaximumPersistenceAdmissionBytes = 8_589_934_592L,
                MaximumSnapshotBytes = 16L * 1024 * 1024,
                WorldGuid = Guid.NewGuid(),
            });
            persistence = new WorldPersistence(assets, CreateCommitPersistenceMetadata());
            _ = world.CreateEntity(new EntityDescription());
            _ = world.Step();
            var save = persistence.SaveAsync(
                world,
                new SaveOptions(savePath, SaveDurabilityPolicy.BestEffort)).GetAwaiter().GetResult();
            Assert.That(save.Status, Is.AnyOf(SaveStatus.DurableCommitted, SaveStatus.BestEffortCommitted), save.Diagnostic?.Message);
            using var preparation = persistence.PrepareLoadAsync(
                world,
                savePath,
                new LoadOptions(WorldRestoreIntent.Branch) {
                    IdentityBranchId = Guid.NewGuid(),
                    MaximumSnapshotBytes = 16L * 1024 * 1024,
                }).GetAwaiter().GetResult();
            Assert.That(preparation.Status, Is.EqualTo(WorldLoadPreparationStatus.Prepared));
            var plan = preparation.Plan
                ?? throw new InvalidOperationException("The candidate-stale checkpoint did not produce its Load plan.");

            callbackState.TargetWorldToDisposeOnCreate = world;
            var receipt = persistence.CommitLoadAsync(plan).GetAwaiter().GetResult();
            Assert.That(callbackState.TargetWorldWasDisposed, Is.True);
            Assert.That(receipt.Status, Is.EqualTo(WorldLoadStatus.Rejected));
            Assert.That(receipt.Rejection?.Code, Is.EqualTo("load-target-binding-stale"));
            Assert.That(receipt.ReplacementWorld, Is.Null);
            Assert.That(receipt.IdentityRemap, Is.Null);
        }
        finally
        {
            world?.Dispose();
            persistence?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            assets?.DisposeAsync().AsTask().GetAwaiter().GetResult();

            File.Delete(packagePath);
            File.Delete(savePath);
            File.Delete(string.Concat(savePath, ".bak"));
        }
    }

    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The standalone owner-thread test synchronously joins every persistence task before disposing its World.")]
    public void RejectsLoadCaptureAfterExternalDomainClosure()
    {
        var schemas = CreateCommitSchemas();
        var packageBytes = SceneAssetStoreTests.Package.CreatePackage();
        var packagePath = SceneAssetStoreTests.Package.WritePackage(packageBytes);
        var savePath = Path.Combine(Path.GetTempPath(), $"terrafx-{Guid.NewGuid():N}.closed-domain.state");
        using var domain = new WorldAdmissionDomain(8_589_934_592L);
        AssetStore? assets = null;
        WorldPersistence? persistence = null;
        World? world = null;

        try
        {
            var assetRegistration = new AssetStoreRegistrationBuilder(schemas).Build();
            assets = AssetStore.CreateAsync(new AssetStoreOptions(
                packagePath,
                SHA256.HashData(packageBytes),
                assetRegistration)).GetAwaiter().GetResult();
            var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
            _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
            world = World.Create(new WorldOptions(worldRegistrationBuilder.Build()) {
                AdmissionDomain = domain,
                CpuWorkerCount = 0,
                InitialReplayLineageId = Guid.NewGuid(),
                MaximumPersistenceAdmissionBytes = 8_589_934_592L,
                MaximumSnapshotBytes = 16L * 1024 * 1024,
                WorldGuid = Guid.NewGuid(),
            });
            persistence = new WorldPersistence(assets, CreateCommitPersistenceMetadata());
            var entity = world.CreateEntity(new EntityDescription());
            var entityId = world.GetEntityId(entity);
            _ = world.Step();
            var save = persistence.SaveAsync(
                world,
                new SaveOptions(savePath, SaveDurabilityPolicy.BestEffort)).GetAwaiter().GetResult();
            Assert.That(
                save.Status,
                Is.AnyOf(SaveStatus.DurableCommitted, SaveStatus.BestEffortCommitted),
                save.Diagnostic?.Message);
            using var preparation = persistence.PrepareLoadAsync(
                world,
                savePath,
                new LoadOptions(WorldRestoreIntent.Branch) {
                    IdentityBranchId = Guid.NewGuid(),
                    MaximumSnapshotBytes = 16L * 1024 * 1024,
                }).GetAwaiter().GetResult();
            Assert.That(preparation.Status, Is.EqualTo(WorldLoadPreparationStatus.Prepared));
            var plan = preparation.Plan
                ?? throw new InvalidOperationException("The external-domain checkpoint did not produce its Load plan.");

            domain.Dispose();
            var receipt = persistence.CommitLoadAsync(plan).GetAwaiter().GetResult();
            Assert.That(receipt.Status, Is.EqualTo(WorldLoadStatus.Rejected));
            Assert.That(receipt.Rejection?.Code, Is.EqualTo("load-domain-closed"));
            Assert.That(receipt.ReplacementWorld, Is.Null);
            Assert.That(receipt.IdentityRemap, Is.Null);
            Assert.That(world.GetEntityId(entity), Is.EqualTo(entityId));
        }
        finally
        {
            world?.Dispose();
            persistence?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            assets?.DisposeAsync().AsTask().GetAwaiter().GetResult();

            File.Delete(packagePath);
            File.Delete(savePath);
            File.Delete(string.Concat(savePath, ".bak"));
        }
    }

    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The standalone owner-thread test synchronously joins every persistence task before disposing its World.")]
    public void RejectsLoadCaptureAfterTargetDisposal()
    {
        var schemas = CreateCommitSchemas();
        var packageBytes = SceneAssetStoreTests.Package.CreatePackage();
        var packagePath = SceneAssetStoreTests.Package.WritePackage(packageBytes);
        var savePath = Path.Combine(Path.GetTempPath(), $"terrafx-{Guid.NewGuid():N}.stale-target.state");
        using var domain = new WorldAdmissionDomain(8_589_934_592L);
        AssetStore? assets = null;
        WorldPersistence? persistence = null;
        World? world = null;

        try
        {
            var assetRegistration = new AssetStoreRegistrationBuilder(schemas).Build();
            assets = AssetStore.CreateAsync(new AssetStoreOptions(
                packagePath,
                SHA256.HashData(packageBytes),
                assetRegistration)).GetAwaiter().GetResult();
            var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
            _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
            world = World.Create(new WorldOptions(worldRegistrationBuilder.Build()) {
                AdmissionDomain = domain,
                CpuWorkerCount = 0,
                InitialReplayLineageId = Guid.NewGuid(),
                MaximumPersistenceAdmissionBytes = 8_589_934_592L,
                MaximumSnapshotBytes = 16L * 1024 * 1024,
                WorldGuid = Guid.NewGuid(),
            });
            persistence = new WorldPersistence(assets, CreateCommitPersistenceMetadata());
            _ = world.CreateEntity(new EntityDescription());
            _ = world.Step();
            var save = persistence.SaveAsync(
                world,
                new SaveOptions(savePath, SaveDurabilityPolicy.BestEffort)).GetAwaiter().GetResult();
            Assert.That(save.Status, Is.AnyOf(SaveStatus.DurableCommitted, SaveStatus.BestEffortCommitted), save.Diagnostic?.Message);
            using var preparation = persistence.PrepareLoadAsync(
                world,
                savePath,
                new LoadOptions(WorldRestoreIntent.Branch) {
                    IdentityBranchId = Guid.NewGuid(),
                    MaximumSnapshotBytes = 16L * 1024 * 1024,
                }).GetAwaiter().GetResult();
            Assert.That(preparation.Status, Is.EqualTo(WorldLoadPreparationStatus.Prepared));
            var plan = preparation.Plan
                ?? throw new InvalidOperationException("The stale-target checkpoint did not produce its Load plan.");

            world.Dispose();
            var receipt = persistence.CommitLoadAsync(plan).GetAwaiter().GetResult();
            Assert.That(receipt.Status, Is.EqualTo(WorldLoadStatus.Rejected));
            Assert.That(receipt.Rejection?.Code, Is.EqualTo("load-target-binding-stale"));
            Assert.That(receipt.ReplacementWorld, Is.Null);
            Assert.That(receipt.IdentityRemap, Is.Null);
        }
        finally
        {
            world?.Dispose();
            persistence?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            assets?.DisposeAsync().AsTask().GetAwaiter().GetResult();

            File.Delete(packagePath);
            File.Delete(savePath);
            File.Delete(string.Concat(savePath, ".bak"));
        }
    }

    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The standalone owner-thread test synchronously joins every persistence and retirement task before disposing its Worlds.")]
    public void ReportsOldWorldRetirementFailureWithoutRejectingLoad()
    {
        var schemas = CreateCommitSchemas();
        var packageBytes = SceneAssetStoreTests.Package.CreatePackage();
        var packagePath = SceneAssetStoreTests.Package.WritePackage(packageBytes);
        var savePath = Path.Combine(Path.GetTempPath(), $"terrafx-{Guid.NewGuid():N}.retirement-failure.state");
        using var domain = new WorldAdmissionDomain(8_589_934_592L);
        var callbackState = new DomainClosureState(domain);
        AssetStore? assets = null;
        WorldPersistence? persistence = null;
        World? world = null;
        World? replacementWorld = null;

        try
        {
            var assetRegistration = new AssetStoreRegistrationBuilder(schemas).Build();
            assets = AssetStore.CreateAsync(new AssetStoreOptions(
                packagePath,
                SHA256.HashData(packageBytes),
                assetRegistration)).GetAwaiter().GetResult();
            world = World.Create(new WorldOptions(CreateDomainClosureRegistration(schemas, callbackState)) {
                AdmissionDomain = domain,
                CpuWorkerCount = 0,
                InitialReplayLineageId = Guid.NewGuid(),
                MaximumPersistenceAdmissionBytes = 8_589_934_592L,
                MaximumSnapshotBytes = 16L * 1024 * 1024,
                WorldGuid = Guid.NewGuid(),
            });
            persistence = new WorldPersistence(assets, CreateCommitPersistenceMetadata());
            _ = world.CreateEntity(new EntityDescription());
            _ = world.Step();
            var save = persistence.SaveAsync(
                world,
                new SaveOptions(savePath, SaveDurabilityPolicy.BestEffort)).GetAwaiter().GetResult();
            Assert.That(save.Status, Is.AnyOf(SaveStatus.DurableCommitted, SaveStatus.BestEffortCommitted), save.Diagnostic?.Message);
            using var preparation = persistence.PrepareLoadAsync(
                world,
                savePath,
                new LoadOptions(WorldRestoreIntent.Branch) {
                    IdentityBranchId = Guid.NewGuid(),
                    MaximumSnapshotBytes = 16L * 1024 * 1024,
                }).GetAwaiter().GetResult();
            Assert.That(preparation.Status, Is.EqualTo(WorldLoadPreparationStatus.Prepared));
            var plan = preparation.Plan
                ?? throw new InvalidOperationException("The retirement-failure checkpoint did not produce its Load plan.");

            callbackState.ThrowOnNextRelease = true;
            var receipt = persistence.CommitLoadAsync(plan).GetAwaiter().GetResult();
            Assert.That(receipt.Status, Is.EqualTo(WorldLoadStatus.Loaded), receipt.Rejection?.Message);
            replacementWorld = receipt.ReplacementWorld
                ?? throw new InvalidOperationException("The Load omitted its replacement World after old-World retirement failed.");
            var retirement = receipt.OldWorldRetirement
                ?? throw new InvalidOperationException("The Load omitted old-World retirement.");
            _ = Assert.Throws<WorldSystemCleanupException>(() => retirement.GetAwaiter().GetResult());
            Assert.That(callbackState.ThrowOnNextRelease, Is.False);
        }
        finally
        {
            replacementWorld?.Dispose();
            world?.Dispose();
            persistence?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            assets?.DisposeAsync().AsTask().GetAwaiter().GetResult();

            File.Delete(packagePath);
            File.Delete(savePath);
            File.Delete(string.Concat(savePath, ".bak"));
        }
    }

    private sealed class DomainClosureState
    {
        internal DomainClosureState(WorldAdmissionDomain domain)
        {
            Domain = domain;
        }

        internal bool CloseDomainOnCreate { get; set; }

        internal WorldAdmissionDomain Domain { get; }

        internal bool DomainWasClosed { get; set; }

        internal InvalidOperationException ReleaseFailure { get; } = new("The old World Release callback failed.");

        internal World? TargetWorldToDisposeOnCreate { get; set; }

        internal bool TargetWorldWasDisposed { get; set; }

        internal bool ThrowOnNextRelease { get; set; }
    }

    private sealed class DomainClosureSystem : IWorldSystem
    {
        public void Execute(WorldSystemContext context)
        {
        }
    }

    private readonly struct DomainClosureSystemCreate : IWorldSystemCreate<DomainClosureSystem>
    {
        private readonly DomainClosureState _state;

        internal DomainClosureSystemCreate(DomainClosureState state)
        {
            _state = state;
        }

        public DomainClosureSystem Invoke()
        {
            if (_state.CloseDomainOnCreate)
            {
                _state.Domain.Dispose();
                _state.DomainWasClosed = true;
            }

            if (_state.TargetWorldToDisposeOnCreate is { } targetWorld)
            {
                _state.TargetWorldToDisposeOnCreate = null;
                targetWorld.Dispose();
                _state.TargetWorldWasDisposed = true;
            }

            return new DomainClosureSystem();
        }
    }

    private sealed class DomainClosureSystemFactory : IWorldSystemFactory
    {
        internal DomainClosureSystemFactory(WorldSystemFactoryBinding binding)
        {
            Binding = binding;
        }

        public WorldSystemFactoryBinding Binding { get; }
    }

    private readonly struct DomainClosureSystemRelease : IWorldSystemRelease<DomainClosureSystem>
    {
        private readonly DomainClosureState _state;

        internal DomainClosureSystemRelease(DomainClosureState state)
        {
            _state = state;
        }

        public void Invoke(DomainClosureSystem system)
        {
            ArgumentNullException.ThrowIfNull(system);

            if (_state.ThrowOnNextRelease)
            {
                _state.ThrowOnNextRelease = false;
                throw _state.ReleaseFailure;
            }
        }
    }
}
