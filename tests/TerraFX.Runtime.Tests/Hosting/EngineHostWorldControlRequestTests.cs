// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Hosting;
using TerraFX.Numerics;
using TerraFX.Runtime.Tests.Scenes;

namespace TerraFX.Runtime.Tests.Hosting;

internal sealed partial class EngineHostOptionsTests
{
    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using 'IDisposable' instances complete before the instances are disposed",
        Justification = "Host disposal joins the owner system before the probe releases its synchronization event.")]
    public async Task HostControlsCanBeAdmittedDuringAnActiveWorldTickAsync()
    {
        var packageBytes = SceneAssetStoreTests.Package.CreatePackage();
        var packagePath = SceneAssetStoreTests.Package.WritePackage(packageBytes);
        var assetStoreOptions = new AssetStoreOptions(
            packagePath,
            SHA256.HashData(packageBytes),
            CreateAssetStoreOptions().Registration);
        var options = CreateHeadlessOptions(assetStoreOptions, cpuWorkerCount: 0);
        using var probe = new ActiveTickControlProbe();
        EngineHost? host = null;
        World? world = null;
        SpatialFrame2 nextFrame2 = default;
        SpatialFrame3 nextFrame = default;

        try
        {
            host = await EngineHost.StartAsync(options).ConfigureAwait(false);
            var builder = new WorldRegistrationBuilder(assetStoreOptions.Registration.Schemas);
            var sourceState = new WorldSystemSourceStateDeclaration(typeof(ActiveTickSystem), []);
            var binding = WorldSystemFactoryBinding.Stateless<
                ActiveTickSystem,
                ActiveTickSystemCreate,
                ActiveTickSystemRelease>(
                    Guid.NewGuid(),
                    1,
                    sourceState,
                    new ActiveTickSystemCreate(probe),
                    default);
            _ = builder.RegisterSystem(
                new SystemDescription(
                    Guid.NewGuid(),
                    0,
                    SimulationStage.Input,
                    [],
                    [],
                    [],
                    SystemThreadMode.OwnerThread),
                new ActiveTickSystemFactory(binding));
            var registration = builder.Build();

            await host.InvokeAsync(context => {
                world = context.CreateWorld(new WorldOptions(registration) {
                    CpuWorkerCount = 0,
                    InitialReplayLineageId = Guid.NewGuid(),
                    WorldGuid = Guid.NewGuid(),
                });
                var currentFrame = world.CurrentFrame3;
                nextFrame = world.CreateNextFrame3(
                    currentFrame.Origin,
                    currentFrame.Minimum,
                    currentFrame.Maximum);
                var currentFrame2 = world.CurrentFrame2
                    ?? throw new InvalidOperationException("The test World did not enable 2D frame admission.");
                nextFrame2 = world.CreateNextFrame2(
                    currentFrame2.Origin,
                    currentFrame2.Minimum,
                    currentFrame2.Maximum);
            }).ConfigureAwait(false);
            await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            var targetWorld = world
                ?? throw new InvalidOperationException("The EngineContext did not create its hosted World.");
            var frame2Task = host.ChangeFrameAsync(targetWorld, nextFrame2);
            var frameTask = host.ChangeFrameAsync(targetWorld, nextFrame);
            var timeScaleTask = host.ChangeTimeScaleAsync(targetWorld, 0.5f);
            Assert.That(frame2Task.IsCompleted, Is.False);
            Assert.That(frameTask.IsCompleted, Is.False);
            Assert.That(timeScaleTask.IsCompleted, Is.False);

            probe.Release.Set();
            var frame2Receipt = await frame2Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(frame2Receipt.Status, Is.EqualTo(FrameChangeStatus.Applied));
            var committedFrame2 = frame2Receipt.CommittedFrame2
                ?? throw new InvalidOperationException("The applied 2D frame receipt omitted its committed frame.");
            Assert.That(committedFrame2.Equals(nextFrame2), Is.True);
            var frameReceipt = await frameTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(frameReceipt.Status, Is.EqualTo(FrameChangeStatus.Applied));
            var committedFrame = frameReceipt.CommittedFrame3
                ?? throw new InvalidOperationException("The applied 3D frame receipt omitted its committed frame.");
            Assert.That(committedFrame.Equals(nextFrame), Is.True);
            var receipt = await timeScaleTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(receipt.Status, Is.EqualTo(TimeScaleChangeStatus.Applied));
            Assert.That(receipt.AppliedTimeScale, Is.EqualTo(0.5f));
            await host.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            probe.Release.Set();

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
            }
        }
    }

    [Test]
    public async Task HostedWorldControlsApplyAtOwnerBoundaryWithSeparateQueueAdmissionAsync()
    {
        var packageBytes = SceneAssetStoreTests.Package.CreatePackage();
        var packagePath = SceneAssetStoreTests.Package.WritePackage(packageBytes);
        var savePath = Path.Combine(Path.GetTempPath(), $"terrafx-{Guid.NewGuid():N}.state");
        var assetStoreOptions = new AssetStoreOptions(
            packagePath,
            SHA256.HashData(packageBytes),
            CreateAssetStoreOptions().Registration);
        var options = CreateHeadlessOptions(assetStoreOptions, cpuWorkerCount: 0);
        EngineHost? host = null;
        EngineHost? otherHost = null;
        World? world = null;
        var blocker = Task.CompletedTask;
        Task<SaveReceipt>? pendingSaveTask = null;
        var releaseOwner = new ManualResetEventSlim(false);
        var ownerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            host = await EngineHost.StartAsync(options).ConfigureAwait(false);
            var registration = new WorldRegistrationBuilder(assetStoreOptions.Registration.Schemas).Build();
            SpatialFrame3 nextFrame = default;

            await host.InvokeAsync(context => {
                world = context.CreateWorld(new WorldOptions(registration) {
                    CpuWorkerCount = 0,
                    InitialReplayLineageId = Guid.NewGuid(),
                    MaximumPendingOwnerRequests = 1,
                    InitialTimeScale = 0,
                    WorldGuid = Guid.NewGuid(),
                });
                var currentFrame = world.CurrentFrame3;
                nextFrame = world.CreateNextFrame3(currentFrame.Origin, currentFrame.Minimum, currentFrame.Maximum);
            }).ConfigureAwait(false);

            var targetWorld = world
                ?? throw new InvalidOperationException("The owner callback did not create its World.");
            otherHost = await EngineHost.StartAsync(options).ConfigureAwait(false);
            var wrongHost = otherHost
                ?? throw new InvalidOperationException("The second EngineHost did not start.");

            try
            {
                _ = await wrongHost.ChangeFrameAsync(targetWorld, nextFrame).ConfigureAwait(false);
                Assert.Fail("A different EngineHost accepted a World it does not own.");
            }
            catch (ArgumentException)
            {
            }

            using var preCanceled = new CancellationTokenSource();
            await preCanceled.CancelAsync().ConfigureAwait(false);
            var canceledFrameChange = host.ChangeFrameAsync(targetWorld, nextFrame, preCanceled.Token);
            Assert.That(canceledFrameChange.IsCanceled, Is.True);

            try
            {
                _ = await canceledFrameChange.ConfigureAwait(false);
                Assert.Fail("A pre-canceled frame request did not return a canceled task.");
            }
            catch (OperationCanceledException)
            {
            }

            blocker = host.InvokeAsync(context => {
                _ = ownerEntered.TrySetResult();
                releaseOwner.Wait();
            });
            await ownerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            var timeScaleTask = host.ChangeTimeScaleAsync(targetWorld, 0.5f);
            Assert.That(timeScaleTask.IsCompleted, Is.False);
            var refusedTimeScale = await host.ChangeTimeScaleAsync(targetWorld, 1.5f).ConfigureAwait(false);
            Assert.That(refusedTimeScale.Status, Is.EqualTo(TimeScaleChangeStatus.Rejected));
            Assert.That(refusedTimeScale.Diagnostics, Has.Length.EqualTo(1));
            Assert.That(refusedTimeScale.Diagnostics[0].Code, Is.EqualTo("WorldOwnerQueueFull"));
            var refusedFrameChange = host.ChangeFrameAsync(targetWorld, nextFrame);
            Assert.That(refusedFrameChange.IsFaulted, Is.True);
            WorldOwnerQueueFullException queueFull;

            try
            {
                _ = await refusedFrameChange.ConfigureAwait(false);
                throw new AssertionException("A full World owner-control queue did not fault the frame request task.");
            }
            catch (WorldOwnerQueueFullException exception)
            {
                queueFull = exception;
            }

            Assert.That(queueFull.Dimension, Is.EqualTo(3));
            Assert.That(queueFull.PendingCount, Is.EqualTo(1));
            Assert.That(queueFull.PendingLimit, Is.EqualTo(1));
            Assert.That(queueFull.NextOwnerRequestSequence, Is.EqualTo(2));
            Assert.That(queueFull.AcceptedOwnerRequests, Is.EqualTo(1));
            Assert.That(queueFull.NextControlSequence, Is.EqualTo(1));
            Assert.That(queueFull.Diagnostic.Code, Is.EqualTo("WorldOwnerQueueFull"));

            var saveAdmissionTask = host.InvokeAsync(_ => pendingSaveTask = host.Persistence.SaveAsync(
                    targetWorld,
                    new SaveOptions(savePath, SaveDurabilityPolicy.BestEffort)));
            releaseOwner.Set();
            await blocker.ConfigureAwait(false);
            await saveAdmissionTask.ConfigureAwait(false);
            var pendingSaveReceipt = await (pendingSaveTask
                ?? throw new InvalidOperationException("The pending World-control Save operation was not admitted."))
                .ConfigureAwait(false);
            Assert.That(pendingSaveReceipt.Status, Is.EqualTo(SaveStatus.Rejected));
            Assert.That(pendingSaveReceipt.Diagnostic?.Code, Is.EqualTo("save-capture-rejected"));
            Assert.That(File.Exists(savePath), Is.False);

            var timeScaleReceipt = await timeScaleTask.ConfigureAwait(false);
            Assert.That(timeScaleReceipt.Status, Is.EqualTo(TimeScaleChangeStatus.Applied));
            Assert.That(timeScaleReceipt.AppliedTimeScale, Is.EqualTo(0.5f));
            Assert.That(timeScaleReceipt.Diagnostics, Is.Empty);

            var frameReceipt = await host.ChangeFrameAsync(targetWorld, nextFrame).ConfigureAwait(false);
            Assert.That(frameReceipt.Status, Is.EqualTo(FrameChangeStatus.Applied));
            Assert.That(frameReceipt.CommittedFrame3, Is.EqualTo(nextFrame));

            await host.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            releaseOwner.Set();

            try
            {
                await blocker.ConfigureAwait(false);
            }
            finally
            {
                releaseOwner.Dispose();

                try
                {
                    if (otherHost is not null)
                    {
                        await otherHost.DisposeAsync().ConfigureAwait(false);
                    }
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
                        File.Delete(savePath);
                        File.Delete(packagePath);
                    }
                }
            }
        }
    }

    private sealed class ActiveTickControlProbe : IDisposable
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly ManualResetEventSlim Release = new(false);

        public void Dispose() => Release.Dispose();
    }

    private sealed class ActiveTickSystem : IWorldSystem
    {
        private readonly ActiveTickControlProbe _probe;

        internal ActiveTickSystem(ActiveTickControlProbe probe)
        {
            _probe = probe;
        }

        public void Execute(WorldSystemContext context)
        {
            _ = _probe.Entered.TrySetResult();
            _probe.Release.Wait();
        }
    }

    private readonly struct ActiveTickSystemCreate : IWorldSystemCreate<ActiveTickSystem>
    {
        private readonly ActiveTickControlProbe _probe;

        internal ActiveTickSystemCreate(ActiveTickControlProbe probe)
        {
            _probe = probe;
        }

        public ActiveTickSystem Invoke() => new(_probe);
    }

    private sealed class ActiveTickSystemFactory : IWorldSystemFactory
    {
        private readonly WorldSystemFactoryBinding _binding;

        internal ActiveTickSystemFactory(WorldSystemFactoryBinding binding)
        {
            _binding = binding;
        }

        public WorldSystemFactoryBinding Binding => _binding;
    }

    private readonly struct ActiveTickSystemRelease : IWorldSystemRelease<ActiveTickSystem>
    {
        public void Invoke(ActiveTickSystem system)
            => ArgumentNullException.ThrowIfNull(system);
    }
}
