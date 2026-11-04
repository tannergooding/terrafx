// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Hosting;
using TerraFX.Numerics;
using TerraFX.Platform;
using TerraFX.Scenes;

namespace TerraFX.Runtime.ComponentConsumer;

internal static partial class Program
{
    private static WorldPersistenceMetadata CreateHostPersistenceMetadata()
    {
        var digest = new byte[32];
        var application = new WorldPersistenceApplication(
            new Guid("10000000-0000-0000-0000-000000000001"),
            majorVersion: 1,
            minorVersion: 0,
            modules: []);
        var operatingSystem = OperatingSystem.IsWindows()
            ? WorldPersistenceOperatingSystem.Windows
            : OperatingSystem.IsLinux()
                ? WorldPersistenceOperatingSystem.Linux
                : throw new PlatformNotSupportedException("The State2 runtime profile supports Windows and Linux only.");
        var codeGeneration = RuntimeFeature.IsDynamicCodeSupported
            ? WorldPersistenceCodeGeneration.Jit
            : WorldPersistenceCodeGeneration.NativeAot;
        var runtimeProfile = new WorldPersistenceRuntimeProfile(
            digest,
            digest,
            digest,
            operatingSystem,
            codeGeneration,
            WorldPersistenceInstructionSet.None,
            digest,
            WorldPersistenceFmaPolicy.Disallowed,
            maximumSimdBits: 0,
            modules: []);
        return new WorldPersistenceMetadata(application, runtimeProfile);
    }

    private static int RunHostedSceneActivationConsumer(
        AssetStoreOptions assetStoreOptions,
        WorldRegistration registration,
        bool optional,
        Guid externalSlotId,
        Guid rootEntityId)
        => RunHostedSceneActivationConsumerAsync(
            assetStoreOptions,
            registration,
            optional,
            externalSlotId,
            rootEntityId)
            .GetAwaiter()
            .GetResult();

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Globalization",
        "CA1303",
        Justification = "The public-consumer witness uses a machine-checked completion marker.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "Both host control tasks are awaited before the EngineHost is disposed in finally.")]
    private static async Task<int> RunHostedSceneActivationConsumerAsync(
        AssetStoreOptions assetStoreOptions,
        WorldRegistration registration,
        bool optional,
        Guid externalSlotId,
        Guid rootEntityId)
    {
        var options = new EngineHostOptions(
            HostProfile.Headless,
            PlatformBackend.Headless,
            graphicsBackend: null,
            graphicsBackendRegistration: null,
            assetStoreOptions) {
            CpuWorkerCount = 0,
            PersistenceMetadata = CreateHostPersistenceMetadata(),
        };
        var host = await EngineHost.StartAsync(options).ConfigureAwait(false);

        try
        {
            World? world = null;
            SceneLoader? loader = null;
            SpatialFrame2 nextFrame2 = default;
            SpatialFrame3 nextFrame = default;

            await host.InvokeAsync(context => {
                world = context.CreateWorld(new WorldOptions(registration) {
                    CpuWorkerCount = 0,
                });
                var currentFrame = world.CurrentFrame3;
                nextFrame = world.CreateNextFrame3(
                    currentFrame.Origin,
                    currentFrame.Minimum,
                    currentFrame.Maximum);
                var currentFrame2 = world.CurrentFrame2
                    ?? throw new InvalidOperationException("The hosted World did not enable 2D frame admission.");
                nextFrame2 = world.CreateNextFrame2(
                    currentFrame2.Origin,
                    currentFrame2.Minimum,
                    currentFrame2.Maximum);
                loader = new SceneLoader(world, host.Assets);
            }).ConfigureAwait(false);

            var hostedWorld = world
                ?? throw new InvalidOperationException("The EngineContext did not create its hosted World.");
            var frameTask = host.ChangeFrameAsync(hostedWorld, nextFrame);
            var frame2Task = host.ChangeFrameAsync(hostedWorld, nextFrame2);
            var frameReceipt = await frameTask
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            Check((frameReceipt.Status == FrameChangeStatus.Applied)
                && (frameReceipt.CommittedFrame3 is SpatialFrame3 committedFrame)
                && committedFrame.Equals(nextFrame));
            var frame2Receipt = await frame2Task
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            Check((frame2Receipt.Status == FrameChangeStatus.Applied)
                && (frame2Receipt.CommittedFrame2 is SpatialFrame2 committedFrame2)
                && committedFrame2.Equals(nextFrame2));

            var timeScaleReceipt = await host.ChangeTimeScaleAsync(hostedWorld, 0.5f)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            Check((timeScaleReceipt.Status == TimeScaleChangeStatus.Applied)
                && (timeScaleReceipt.AppliedTimeScale == 0.5f));

            using var prepared = await loader!.PrepareAsync(
                new AssetId(3),
                new SceneLoadOptions {
                    ExternalBindings = [new SceneExternalBinding(externalSlotId, target: null)],
                })
                .ConfigureAwait(false);

            SceneActivationReceipt receipt;

            try
            {
                receipt = await loader.ActivateAsync(prepared)
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
            }
            catch (InvalidDataException) when (!optional)
            {
                Console.WriteLine("Scene host consumer required-null rejection passed.");
                return 0;
            }

            if (!optional)
            {
                throw new InvalidOperationException("The Scene host consumer accepted null for a required Entity reference.");
            }

            Check(receipt.Status == SceneActivationStatus.Activated);
            var instance = receipt.Instance
                ?? throw new InvalidOperationException("The Scene host consumer activation omitted its instance.");
            Task<SceneUnloadReceipt>? unloadTask = null;

            await host.InvokeAsync(_ => {
                Check(instance.TryGetEntity(rootEntityId, out var entity));
                Check(world!.TryGet<ReferenceComponent>(entity, out var component));
                Check(!component.Target.IsValid);
                unloadTask = instance.UnloadAsync(HierarchyDestroyMode.DestroySubtree);
            }).ConfigureAwait(false);

            Check((await (unloadTask
                ?? throw new InvalidOperationException("The Scene host consumer unload was not admitted."))
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false)).Status == SceneUnloadStatus.Unloaded);
            Console.WriteLine("Scene host consumer optional-null activation passed.");
            return 0;
        }
        finally
        {
            await host.DisposeAsync().ConfigureAwait(false);
        }
    }
}
