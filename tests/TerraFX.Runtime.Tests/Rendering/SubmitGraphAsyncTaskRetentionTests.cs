// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Graphics;
using TerraFX.Graphics.Vulkan;
using TerraFX.Numerics;
using TerraFX.Rendering;

namespace TerraFX.Runtime.Tests.Rendering;

[TestFixture]
internal sealed class SubmitGraphAsyncTaskRetentionTests
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The actual SubmitGraphAsync task completes before the test disposes the target and GraphicsDevice.")]
    private static (
        Task<GraphicsGraphSubmissionResult> Task,
        WeakReference<GraphicsGraphSubmission> Submission) StartSubmitGraphAsync(
            CancellationToken cancellationToken = default)
    {
        var registration = VulkanGraphicsBackend.CreateRegistration();
        GraphicsDevice? device = null;

        try
        {
            try
            {
                device = GraphicsDevice.CreateAsync(
                    ownerId: 31,
                    registration,
                    GraphicsDeviceProfile.OffscreenNative,
                    frameSlotCount: 2,
                    GraphicsPlatformKind.None,
                    CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (DllNotFoundException exception)
            {
                Assert.Ignore($"The Vulkan loader is unavailable: {exception.Message}");
                throw;
            }
            catch (EntryPointNotFoundException exception)
            {
                Assert.Ignore($"A required Vulkan entry point is unavailable: {exception.Message}");
                throw;
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No Vulkan device satisfies OffscreenNative: {exception.Message}");
                throw;
            }

            var graphicsDevice = device
                ?? throw new InvalidOperationException("Vulkan GraphicsDevice startup returned no device.");
            using var target = graphicsDevice.CreateTextureAsync(
                new TextureDescription(
                    TextureDimension.Dimension2D,
                    width: 4,
                    height: 4,
                    depth: 1,
                    arrayLayers: 1,
                    mipLevels: 1,
                    TextureFormat.Rgba8Unorm,
                    TextureUsage.ColorAttachment | TextureUsage.CopySource,
                    debugName: "submit-graph-task-retention"),
                CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            var extent = new PixelSize(4, 4);
            var graph = new RenderGraph(new RenderGraphOptions(
                Guid.NewGuid(),
                Guid.NewGuid(),
                0,
                RenderFrameDimension.TwoDimensional,
                SpatialProfileId.Default,
                1,
                [Guid.NewGuid()],
                extent,
                target));
            var output = graph.OffscreenColorTargetVersion;
            var pass = graph.AddRasterPass(
                new RenderPassDescription("submit-graph-task-retention", 0, [], hasExternalSideEffects: false),
                RecordPass);
            var finalVersion = graph.WriteColorAttachment(
                pass,
                output,
                new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color),
                AttachmentLoadAction.Clear,
                AttachmentStoreAction.Store,
                Vector4.Zero);
            graph.SetPresentationSource(finalVersion);
            var compiled = graph.Compile();
            var plan = compiled.Plan
                ?? throw new InvalidOperationException("The graph did not produce a submission plan.");
            var recordedPasses = plan.RecordPassCallbacksAsync(builtInResources: null)
                .GetAwaiter()
                .GetResult();

            GraphicsGraphSubmission? submission = null;
            GraphicsDevice.GraphResourceUseLeaseSet? resourceUseLeases = null;
            GraphicsDevice.GraphFrameSlotReservation? frameSlotReservation = null;
            var submissionTransferred = false;

            try
            {
                submission = plan.CreateGraphicsSubmission(
                    target,
                    recordedPasses,
                    [],
                    [],
                    ImmutableDictionary<ulong, GraphResourceState>.Empty);
                var weakSubmission = new WeakReference<GraphicsGraphSubmission>(submission);
                resourceUseLeases = graphicsDevice.AcquireGraphResourceUseLeases([target.ResourceOwner]);
                frameSlotReservation = graphicsDevice.AcquireGraphFrameSlotAsync(CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                var submitTask = graphicsDevice.SubmitGraphAsync(
                    submission,
                    resourceUseLeases,
                    frameSlotReservation,
                    cancellationToken);
                submissionTransferred = true;
                submission = null;
                resourceUseLeases = null;
                frameSlotReservation = null;
                TestContext.WriteLine(
                    $"Runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}/"
                    + $"{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}; "
                    + $"SubmitGraphTask={submitTask.GetType().FullName}; "
                    + $"StatusBeforeCompletion={submitTask.Status}");

                if (cancellationToken.IsCancellationRequested)
                {
                    Assert.That(submitTask.Status, Is.EqualTo(TaskStatus.Canceled));
                }
                else
                {
                    var result = submitTask.GetAwaiter().GetResult();
                    Assert.That(result.DeviceEpoch, Is.EqualTo(graphicsDevice.DeviceEpoch));
                    Assert.That(result.SubmissionSequence, Is.GreaterThan(0));
                }

                return (submitTask, weakSubmission);
            }
            finally
            {
                resourceUseLeases?.Dispose();
                frameSlotReservation?.Dispose();

                if (submission is not null)
                {
                    submission.DisposeRecordedPassCommandLists();
                    submission.DisposeBackendStorage();
                }
                else if (!submissionTransferred)
                {
                    foreach (var recordedPass in recordedPasses)
                    {
                        recordedPass.Dispose();
                    }
                }
            }
        }
        finally
        {
            device?.ShutdownAsync().GetAwaiter().GetResult();
        }

        static void RecordPass(scoped ref CommandRecorder recorder)
        {
        }
    }

    [Test]
    public void CompletedSubmitGraphTaskDoesNotRetainSubmission()
    {
        var (submitTask, weakSubmission) = StartSubmitGraphAsync();
        Assert.That(submitTask.Status, Is.EqualTo(TaskStatus.RanToCompletion));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var retainedWhileTaskHeld = weakSubmission.TryGetTarget(out _);
        TestContext.WriteLine($"SubmitGraphTaskRetainsSubmissionAfterCompletion={retainedWhileTaskHeld}");
        Assert.That(retainedWhileTaskHeld, Is.False);

        submitTask = Task.FromResult(default(GraphicsGraphSubmissionResult));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var retainedAfterTaskDrop = weakSubmission.TryGetTarget(out _);
        TestContext.WriteLine($"SubmitGraphSubmissionRetainedAfterTaskDrop={retainedAfterTaskDrop}");

        Assert.That(retainedAfterTaskDrop, Is.False);
        GC.KeepAlive(submitTask);
    }

    [Test]
    public void PreCanceledSubmitGraphTaskPreservesCancellationToken()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var (submitTask, _) = StartSubmitGraphAsync(cancellationSource.Token);

        Assert.That(submitTask.Status, Is.EqualTo(TaskStatus.Canceled));
        var exception = Assert.Throws<OperationCanceledException>(() => submitTask.GetAwaiter().GetResult());
        Assert.That(exception?.CancellationToken, Is.EqualTo(cancellationSource.Token));
        GC.KeepAlive(submitTask);
    }
}
