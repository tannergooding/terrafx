// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Rendering;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

internal sealed partial class SceneAssetStoreTests
{
    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The owner boundary completes activation and unload requests before the prepared assets are released.")]
    public void PreparedAssetLeaseRetainsBundleAcrossActivationUnloadAndDispose()
    {
        var schemas = CreateRendererRegistry();
        var frameId = new Guid("30000000-0000-4000-8000-000000000001");
        var externalSlotId = new Guid("30000000-0000-4000-8000-000000000075");
        var textureRecord = CreateRenderAtlasPageTextureRecord(17);
        var atlasRecord = CreateRenderAtlasRecord(
            1,
            CreateRenderAtlasFrameRecord(frameId, page: 0, x: 4, y: 4, width: 4, height: 4));
        var sceneRecord = SceneAssetReaderTests.Fixtures.CreateScene(
            new AssetId(3),
            externalSlots: [CreateExternalSlot(externalSlotId, required: false, allowNull: true, requiredComponents: [])],
            additionalEntityComponents: [CreateRenderSpriteRecord(2, frameId, materialAssetId: null)]);
        Package.Entry[] packageEntries = [
            new(1, Package.CreateEnvelope(1, textureRecord)),
            new(2, Package.CreateEnvelope(2, atlasRecord)),
            new(3, Package.CreateEnvelope(3, sceneRecord)),
        ];
        var package = Package.CreatePackage(packageEntries);
        var packagePath = Package.WritePackage(package);
        var assetRegistrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = SceneSupport.RegisterAssetCodecs(assetRegistrationBuilder);
        _ = Renderer.RegisterAssetCodecs(assetRegistrationBuilder);

        AssetStore? assets = null;

        try
        {
            assets = AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), assetRegistrationBuilder.Build()))
                .GetAwaiter()
                .GetResult();
            var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
            _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
            _ = worldRegistrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration());
            using var world = World.Create(new WorldOptions(worldRegistrationBuilder.Build()));
            var loader = new SceneLoader(world, assets);
            var worldOwnedStateBytes = world.GetWorldOwnedStateBytes();
            var underfundedBindings = new SceneLoadOptions {
                ExternalBindings = [new SceneExternalBinding(
                    new Guid("30000000-0000-4000-8000-000000000073"),
                    target: null)],
                MaximumPreparationBytes = 1,
            };
            Assert.That(
                () => loader.PrepareAsync(new AssetId(3), underfundedBindings),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(world.GetWorldOwnedStateBytes(), Is.EqualTo(worldOwnedStateBytes));

            var reservationField = typeof(World).GetField(
                "_domainReservation",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("The World omitted its domain reservation.");
            var reservationForOptionFailure = (WorldDomainReservation?)reservationField.GetValue(world)
                ?? throw new InvalidOperationException("The World omitted its live domain reservation.");
            Assert.That(reservationForOptionFailure.IndependentUseBytes, Is.Zero);

            var invalidBindings = new SceneLoadOptions {
                ExternalBindings = [new SceneExternalBinding(
                    new Guid("30000000-0000-4000-8000-000000000074"),
                    target: null)],
            };
            _ = Assert.Throws<InvalidDataException>(
                () => loader.PrepareAsync(new AssetId(3), invalidBindings).GetAwaiter().GetResult());
            Assert.That(reservationForOptionFailure.IndependentUseBytes, Is.Zero);

            Task<PreparedScene>? fallbackPreparation = null;

            try
            {
                _ = Assert.Throws<InvalidOperationException>(
                    () => fallbackPreparation = loader.PrepareAsync(
                        new AssetId(3),
                        new SceneLoadOptions {
                            MaximumPreparationBytes = WorldSchedulingLimits.Default.MaximumOwnedStateBytes,
                        }));
            }
            finally
            {
                if (fallbackPreparation is not null)
                {
                    using var unexpectedResult = fallbackPreparation.GetAwaiter().GetResult();
                }
            }

            Assert.That(world.GetWorldOwnedStateBytes(), Is.EqualTo(worldOwnedStateBytes));

            var preparationOptions = new SceneLoadOptions {
                ExternalBindings = [new SceneExternalBinding(externalSlotId, target: null)],
            };
            using var prepared = loader.PrepareAsync(new AssetId(3), preparationOptions).GetAwaiter().GetResult();
            using var retainedAssets = prepared.AcquirePreparedAssets()
                ?? throw new InvalidOperationException("The prepared Sprite Scene omitted its asset bundle.");
            var bundle = retainedAssets.Value as RenderSceneAssetBundle
                ?? throw new InvalidOperationException("The prepared Sprite Scene retained an unexpected asset type.");

            var activation = loader.ActivateAsync(prepared);
            using var scheduler = new BlockingContinuationScheduler();
            var continuation = activation.ContinueWith(
                static _ => { },
                CancellationToken.None,
                TaskContinuationOptions.None,
                scheduler);
            var domainReservation = (WorldDomainReservation?)reservationField.GetValue(world)
                ?? throw new InvalidOperationException("The World omitted its live domain reservation.");
            var activationWorkspaceBytes = checked(
                WorldGuidUseControl.ControlByteCharge + preparationOptions.MaximumPreparationBytes);
            var publicationObservation = Task.Run(() => {
                try
                {
                    Assert.That(scheduler.WaitForQueuedContinuation(TimeSpan.FromSeconds(10)), Is.True);
                    Assert.That(domainReservation.IndependentUseBytes, Is.EqualTo(activationWorkspaceBytes));
                }
                finally
                {
                    scheduler.ReleaseContinuation();
                }
            });
            world.PumpControlBoundary();
            publicationObservation.GetAwaiter().GetResult();
            continuation.GetAwaiter().GetResult();
            var activationReceipt = activation.GetAwaiter().GetResult();
            Assert.That(activationReceipt.Status, Is.EqualTo(SceneActivationStatus.Activated));
            Assert.That(domainReservation.IndependentUseBytes, Is.Zero);
            var instance = activationReceipt.Instance
                ?? throw new InvalidOperationException("The successful Scene activation omitted its instance.");
            Assert.That(
                () => prepared.AcquirePreparedAssets(),
                Throws.TypeOf<InvalidOperationException>());

            var unload = instance.UnloadAsync(HierarchyDestroyMode.DestroySubtree);
            world.PumpControlBoundary();
            Assert.That(unload.GetAwaiter().GetResult().Status, Is.EqualTo(SceneUnloadStatus.Unloaded));
            Assert.That(
                bundle.GetAtlasPageSet(2).GetPage(0).Mips[0].Pixels[0],
                Is.EqualTo(17));
            retainedAssets.Dispose();
            Assert.That(() => bundle.GetAtlasPageSet(2), Throws.TypeOf<ObjectDisposedException>());

            using var disposedPrepared = loader.PrepareAsync(new AssetId(3), preparationOptions).GetAwaiter().GetResult();
            using var disposedAssets = disposedPrepared.AcquirePreparedAssets()
                ?? throw new InvalidOperationException("The prepared Sprite Scene omitted its asset bundle.");
            var disposedBundle = disposedAssets.Value as RenderSceneAssetBundle
                ?? throw new InvalidOperationException("The prepared Sprite Scene retained an unexpected asset type.");

            disposedPrepared.Dispose();
            Assert.That(
                disposedBundle.GetAtlasPageSet(2).GetPage(0).Mips[0].Pixels[0],
                Is.EqualTo(17));
            disposedAssets.Dispose();
            Assert.That(() => disposedBundle.GetAtlasPageSet(2), Throws.TypeOf<ObjectDisposedException>());
        }
        finally
        {
            try
            {
                assets?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            finally
            {
                File.Delete(packagePath);
            }
        }
    }

    private sealed class BlockingContinuationScheduler : TaskScheduler, IDisposable
    {
        private readonly ManualResetEventSlim _continuationAllowed = new();
        private readonly ManualResetEventSlim _continuationQueued = new();

        public void Dispose()
        {
            _continuationAllowed.Dispose();
            _continuationQueued.Dispose();
        }

        internal void ReleaseContinuation()
            => _continuationAllowed.Set();

        internal bool WaitForQueuedContinuation(TimeSpan timeout)
            => _continuationQueued.Wait(timeout);

        protected override IEnumerable<Task>? GetScheduledTasks()
            => [];

        protected override void QueueTask(Task task)
        {
            _continuationQueued.Set();

            if (!_continuationAllowed.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("The activation completion observer did not release its scheduled continuation.");
            }

            if (!TryExecuteTask(task))
            {
                throw new InvalidOperationException("The activation completion continuation could not be executed.");
            }
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
            => false;
    }
}
