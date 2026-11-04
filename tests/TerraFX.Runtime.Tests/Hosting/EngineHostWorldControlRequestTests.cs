// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Hosting;
using TerraFX.Runtime.Tests.Scenes;

namespace TerraFX.Runtime.Tests.Hosting;

internal sealed partial class EngineHostOptionsTests
{
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
}
