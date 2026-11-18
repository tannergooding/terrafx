// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Buffers;
using TerraFX.Content.Tools;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Graphics.D3D12;
using TerraFX.Graphics.Vulkan;
using TerraFX.Hosting;
using TerraFX.Interop.Vulkan;
using TerraFX.Numerics;
using TerraFX.Platform;
using TerraFX.Rendering;
using TerraFX.Runtime.Tests.Scenes;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Hosting;

internal sealed partial class EngineHostOptionsTests
{
    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task CopiesIntoColorAttachmentInLaterGraphPass(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useMultipleColorAttachments: true,
            useColorAttachmentCopyDestination: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task CopiesRenderedColorAttachmentInLaterGraphPass(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useMultipleColorAttachments: true,
            useColorAttachmentCopy: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task CreatesNativePipelineWithEightUniformBuffersAcrossGroups(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useEightUniformPipelineOnly: true);

    [Test]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The synchronous GraphicsDevice requests complete before their native owners are retired.")]
    public void D3D12GraphAllocatesTransientBufferAliasGroupsAcrossReservedSlots()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 transient allocation witness requires Windows 11.");
            return;
        }

        var registration = D3D12GraphicsBackend.CreateRegistration();
        var options = new EngineHostOptions(
            HostProfile.OffscreenNative,
            PlatformBackend.Headless,
            GraphicsBackend.D3D12,
            registration,
            CreateAssetStoreOptions()) {
            CpuWorkerCount = 0,
            GraphicsFrameSlotCount = 2,
            PersistenceMetadata = CreatePersistenceMetadata(),
        };
        var platformOwner = PlatformHostOwner.Create(options);
        GraphicsDevice? graphicsDevice = null;

        try
        {
            try
            {
                graphicsDevice = GraphicsDevice.CreateAsync(
                    platformOwner.OwnerId,
                    registration,
                    GraphicsDeviceProfile.OffscreenNative,
                    options.GraphicsFrameSlotCount,
                    GraphicsPlatformKind.None,
                    CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No D3D12 device satisfies OffscreenNative: {exception.Message}");
                return;
            }

            var targetExtent = new PixelSize(4, 4);
            using var target = graphicsDevice.CreateTextureAsync(
                new TextureDescription(
                    TextureDimension.Dimension2D,
                    targetExtent.Width,
                    targetExtent.Height,
                    depth: 1,
                    arrayLayers: 1,
                    mipLevels: 1,
                    format: TextureFormat.Rgba8Unorm,
                    usage: TextureUsage.ColorAttachment,
                    debugName: "d3d12-transient-buffer-slot-target"))
                .GetAwaiter()
                .GetResult();
            var graph = new RenderGraph(new RenderGraphOptions(
                Guid.NewGuid(),
                Guid.NewGuid(),
                0,
                RenderFrameDimension.TwoDimensional,
                SpatialProfileId.Default,
                1,
                [Guid.NewGuid()],
                targetExtent,
                target));
            var range = new GraphBufferRange(0, 16);
            RenderPassId? previousReadPass = null;

            for (var index = 0; index < 2; index++)
            {
                var transientBuffer = graph.CreateTransientBuffer(new BufferDescription(
                    16,
                    BufferUsage.Storage,
                    GpuMemoryKind.DeviceLocal,
                    structuredStride: sizeof(uint),
                    debugName: $"d3d12-transient-buffer-slot-{index}"));
                var writeOrder = checked(index * 2);
                var writePass = graph.AddComputePass(
                    new RenderPassDescription(
                        $"d3d12-transient-buffer-write-{index}",
                        writeOrder,
                        previousReadPass is RenderPassId predecessor ? [predecessor] : [],
                        hasExternalSideEffects: false));
                var writtenVersion = graph.WriteBuffer(
                    writePass,
                    transientBuffer,
                    GraphBufferUsage.StorageWrite,
                    range);
                var readPass = graph.AddComputePass(
                    new RenderPassDescription(
                        $"d3d12-transient-buffer-read-{index}",
                        writeOrder + 1,
                        [writePass],
                        hasExternalSideEffects: false));
                graph.ReadBuffer(readPass, writtenVersion, GraphBufferUsage.StorageRead, range);
                previousReadPass = readPass;
            }

            var outputPass = graph.AddRasterPass(new RenderPassDescription(
                "d3d12-transient-buffer-slot-output",
                4,
                [previousReadPass ?? throw new InvalidOperationException("The graph has no transient buffer reader.")],
                hasExternalSideEffects: false));
            var output = graph.WriteColorAttachment(
                outputPass,
                graph.OffscreenColorTargetVersion,
                new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color),
                AttachmentLoadAction.Clear,
                AttachmentStoreAction.Store,
                new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            graph.SetPresentationSource(output);

            var compileResponse = graph.Compile();
            Assert.That(compileResponse.Status, Is.EqualTo(RenderGraphCompileStatus.Compiled));
            var plan = compileResponse.Plan
                ?? throw new InvalidOperationException("The D3D12 transient-buffer graph has no plan.");
            Assert.That(plan.TransientBufferGroups, Has.Length.EqualTo(1));
            Assert.That(plan.TransientBufferGroups[0], Has.Length.EqualTo(2));
            Assert.That(plan.TransientBufferAliasingBarriers, Has.Length.EqualTo(1));

            using var firstSlot = graphicsDevice.AcquireGraphFrameSlotAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            using var secondSlot = graphicsDevice.AcquireGraphFrameSlotAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            using var firstGroup = graphicsDevice.CreateTransientBufferGroupAsync(
                plan.TransientBufferGroups[0],
                CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            using var secondGroup = graphicsDevice.CreateTransientBufferGroupAsync(
                plan.TransientBufferGroups[0],
                CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            var firstLease = firstGroup.Buffers[0].ResourceOwner.AllocationLease
                ?? throw new InvalidOperationException("The first D3D12 buffer group lost its allocation lease.");
            var secondLease = secondGroup.Buffers[0].ResourceOwner.AllocationLease
                ?? throw new InvalidOperationException("The second D3D12 buffer group lost its allocation lease.");

            Assert.That(firstLease, Is.Not.SameAs(secondLease));
            Assert.That(firstGroup.Buffers[1].ResourceOwner.AllocationLease, Is.SameAs(firstLease));
            Assert.That(secondGroup.Buffers[1].ResourceOwner.AllocationLease, Is.SameAs(secondLease));
        }
        finally
        {
            try
            {
                graphicsDevice?.ShutdownAsync().GetAwaiter().GetResult();
            }
            finally
            {
                platformOwner.DisposeFromOwner();
            }
        }
    }

    [Test]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The graph only performs synchronous metadata validation before native owners are retired.")]
    public void D3D12GraphBufferReadbackExportRejectsForeignVersionsAndRequiresUniqueContainedRange()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 readback-export witness requires Windows 11.");
            return;
        }

        var registration = D3D12GraphicsBackend.CreateRegistration();
        var options = new EngineHostOptions(
            HostProfile.OffscreenNative,
            PlatformBackend.Headless,
            GraphicsBackend.D3D12,
            registration,
            CreateAssetStoreOptions()) {
            CpuWorkerCount = 0,
            GraphicsFrameSlotCount = 2,
            PersistenceMetadata = CreatePersistenceMetadata(),
        };
        var platformOwner = PlatformHostOwner.Create(options);
        GraphicsDevice? graphicsDevice = null;

        try
        {
            try
            {
                graphicsDevice = GraphicsDevice.CreateAsync(
                    platformOwner.OwnerId,
                    registration,
                    GraphicsDeviceProfile.OffscreenNative,
                    options.GraphicsFrameSlotCount,
                    GraphicsPlatformKind.None,
                    CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No D3D12 device satisfies OffscreenNative: {exception.Message}");
                return;
            }

            var targetExtent = new PixelSize(4, 4);
            using var target = graphicsDevice.CreateTextureAsync(
                new TextureDescription(
                    TextureDimension.Dimension2D,
                    targetExtent.Width,
                    targetExtent.Height,
                    depth: 1,
                    arrayLayers: 1,
                    mipLevels: 1,
                    format: TextureFormat.Rgba8Unorm,
                    usage: TextureUsage.ColorAttachment,
                    debugName: "d3d12-readback-export-target"))
                .GetAwaiter()
                .GetResult();
            var graph = new RenderGraph(new RenderGraphOptions(
                Guid.NewGuid(),
                Guid.NewGuid(),
                0,
                RenderFrameDimension.TwoDimensional,
                SpatialProfileId.Default,
                1,
                [Guid.NewGuid()],
                targetExtent,
                target));
            var buffer = graph.CreateTransientBuffer(new BufferDescription(
                16,
                BufferUsage.Storage | BufferUsage.CopySource,
                GpuMemoryKind.DeviceLocal,
                structuredStride: sizeof(uint),
                debugName: "d3d12-readback-export-buffer"));
            var foreignGraph = new RenderGraph(new RenderGraphOptions(
                Guid.NewGuid(),
                Guid.NewGuid(),
                0,
                RenderFrameDimension.TwoDimensional,
                SpatialProfileId.Default,
                1,
                [Guid.NewGuid()],
                targetExtent,
                target));
            var foreignBuffer = foreignGraph.CreateTransientBuffer(new BufferDescription(
                16,
                BufferUsage.Storage | BufferUsage.CopySource,
                GpuMemoryKind.DeviceLocal,
                structuredStride: sizeof(uint),
                debugName: "d3d12-foreign-readback-export-buffer"));

            Assert.That(
                () => graph.ExportBufferForReadback(buffer, new GraphBufferRange(8, 16)),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => graph.ExportBufferForReadback(foreignBuffer, new GraphBufferRange(0, 16)),
                Throws.TypeOf<ArgumentException>());

            var range = new GraphBufferRange(0, 16);
            graph.ExportBufferForReadback(buffer, range);
            Assert.That(graph.NonAliasableTransientBufferIds, Does.Contain(buffer.ResourceId));
            Assert.That(
                () => graph.ExportBufferForReadback(buffer, range),
                Throws.TypeOf<InvalidOperationException>());
        }
        finally
        {
            try
            {
                graphicsDevice?.ShutdownAsync().GetAwaiter().GetResult();
            }
            finally
            {
                platformOwner.DisposeFromOwner();
            }
        }
    }

    [Test]
    [Category("ProcessIsolatedGraphQuarantine")]
    public Task D3D12GraphFenceTimeoutQuarantinesGraphResources()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 graph timeout witness targets Windows 11.");
            return Task.CompletedTask;
        }

        if (!StringComparer.Ordinal.Equals(
            Environment.GetEnvironmentVariable("TERRAFX_NATIVE_QUARANTINE_CHILD"),
            "1"))
        {
            NativeQuarantineTestProcess.Run("TestCategory=ProcessIsolatedGraphQuarantine");
            return Task.CompletedTask;
        }

        return RunOffscreenGraphAsync(
            GraphicsBackend.D3D12,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            simulateGraphFenceTimeout: true);
    }

    [Test]
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void D3D12GraphSubmissionRetainsTargetUntilFenceCompletion()
    {
        Task<RenderSubmissionReceipt>? returnedTask = null;
        WeakReference<RenderSnapshot>? snapshotReference = null;
        WeakReference<RenderGraphPlan>? planReference = null;
        WeakReference<Renderer>? rendererReference = null;
        WeakReference<GraphicsDevice>? deviceReference = null;

        void ObserveReturnedTask(
            Task<RenderSubmissionReceipt> task,
            RenderSnapshot snapshot,
            RenderGraphPlan plan,
            Renderer renderer,
            GraphicsDevice device)
        {
            returnedTask = task;
            snapshotReference = new WeakReference<RenderSnapshot>(snapshot);
            planReference = new WeakReference<RenderGraphPlan>(plan);
            rendererReference = new WeakReference<Renderer>(renderer);
            deviceReference = new WeakReference<GraphicsDevice>(device);
        }

        var helperTask = RunOffscreenGraphAsync(
            GraphicsBackend.D3D12,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: true,
            graphicsFrameSlotCount: 2,
            observeReturnedTask: ObserveReturnedTask);
        helperTask.GetAwaiter().GetResult();
        helperTask = Task.CompletedTask;
        GC.KeepAlive(helperTask);

        var completedTask = returnedTask;
        ArgumentNullException.ThrowIfNull(completedTask);
        var retainedSnapshot = snapshotReference
            ?? throw new InvalidOperationException("The graph task-retention probe captured no snapshot.");
        var retainedPlan = planReference
            ?? throw new InvalidOperationException("The graph task-retention probe captured no plan.");
        var retainedRenderer = rendererReference
            ?? throw new InvalidOperationException("The graph task-retention probe captured no Renderer.");
        var retainedDevice = deviceReference
            ?? throw new InvalidOperationException("The graph task-retention probe captured no GraphicsDevice.");
        Assert.That(completedTask.Status, Is.EqualTo(TaskStatus.RanToCompletion));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var snapshotRetainedWhileTaskHeld = retainedSnapshot.TryGetTarget(out _);
        var planRetainedWhileTaskHeld = retainedPlan.TryGetTarget(out _);
        var rendererRetainedWhileTaskHeld = retainedRenderer.TryGetTarget(out _);
        var deviceRetainedWhileTaskHeld = retainedDevice.TryGetTarget(out _);
        TestContext.WriteLine(
            $"CallerTaskRetainsSnapshot={snapshotRetainedWhileTaskHeld}; "
            + $"CallerTaskRetainsPlan={planRetainedWhileTaskHeld}; "
            + $"CallerTaskRetainsRenderer={rendererRetainedWhileTaskHeld}; "
            + $"CallerTaskRetainsDevice={deviceRetainedWhileTaskHeld}");
        Assert.That(snapshotRetainedWhileTaskHeld, Is.False);
        Assert.That(planRetainedWhileTaskHeld, Is.False);
        Assert.That(rendererRetainedWhileTaskHeld, Is.False);
        Assert.That(deviceRetainedWhileTaskHeld, Is.False);
        GC.KeepAlive(completedTask);
        returnedTask = null;
        completedTask = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.That(retainedSnapshot.TryGetTarget(out _), Is.False);
        Assert.That(retainedPlan.TryGetTarget(out _), Is.False);
        Assert.That(retainedRenderer.TryGetTarget(out _), Is.False);
        Assert.That(retainedDevice.TryGetTarget(out _), Is.False);
    }

    [Test]
    public Task D3D12GraphSubmissionRetainsTransientAliasGroupsPerSlot()
        => RunOffscreenGraphAsync(
            GraphicsBackend.D3D12,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: true,
            graphicsFrameSlotCount: 2,
            useTransientCopyAliasing: true);

    [Test]
    public Task D3D12SubmitsBufferCopyGraphFromUploadBuffer()
        => RunOffscreenGraphAsync(
            GraphicsBackend.D3D12,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: true,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useUploadHeapGraphSources: true);

    [Test]
    public Task D3D12SubmitsConstantWriteComputeStorageBufferGraph()
        => RunOffscreenGraphAsync(
            GraphicsBackend.D3D12,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useConstantWriteComputeBuffer: true);

    [Test]
    public Task D3D12SubmitsMultipleStorageBufferComputeGraph()
        => RunOffscreenGraphAsync(
            GraphicsBackend.D3D12,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: true,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false);

    [Test]
    public Task D3D12SubmitsR32FloatComputeStorageImage()
        => RunOffscreenGraphAsync(
            GraphicsBackend.D3D12,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: true,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useFloatStorageImage: true);

    [Test]
    public Task D3D12SubmitsR32UIntComputeStorageImage()
        => RunOffscreenGraphAsync(
            GraphicsBackend.D3D12,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: true,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false);

    [Test]
    public Task D3D12SubmitsRgba16FloatComputeStorageImage()
        => RunOffscreenGraphAsync(
            GraphicsBackend.D3D12,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: true,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useRgba16FloatStorageImage: true);

    [Test]
    public Task D3D12SubmitsTransientBufferAliasGroupsAcrossLiveSlots()
        => RunOffscreenGraphAsync(
            GraphicsBackend.D3D12,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: true,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: true,
            graphicsFrameSlotCount: 2,
            useUploadHeapGraphSources: true);

    [Test]
    public Task D3D12SubmitsTransientStorageBufferComputeGraph()
        => RunOffscreenGraphAsync(
            GraphicsBackend.D3D12,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: true,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false);

    [Test]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The synchronous GraphicsDevice requests complete before their native owners are retired.")]
    public void D3D12TransientTextureAllocationPoolReusesAfterRetirement()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 transient allocation witness requires Windows 11.");
            return;
        }

        var registration = D3D12GraphicsBackend.CreateRegistration();
        var options = new EngineHostOptions(
            HostProfile.OffscreenNative,
            PlatformBackend.Headless,
            GraphicsBackend.D3D12,
            registration,
            CreateAssetStoreOptions()) {
            CpuWorkerCount = 0,
            PersistenceMetadata = CreatePersistenceMetadata(),
        };
        var platformOwner = PlatformHostOwner.Create(options);
        GraphicsDevice? graphicsDevice = null;

        try
        {
            try
            {
                graphicsDevice = GraphicsDevice.CreateAsync(
                    platformOwner.OwnerId,
                    registration,
                    GraphicsDeviceProfile.OffscreenNative,
                    options.GraphicsFrameSlotCount,
                    GraphicsPlatformKind.None,
                    CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No D3D12 device satisfies OffscreenNative: {exception.Message}");
                return;
            }

            var registry = new GraphResourceRegistry(graphicsDevice, graphOwnerId: 7);
            var description = new TextureDescription(
                TextureDimension.Dimension2D,
                width: 4,
                height: 4,
                depth: 1,
                arrayLayers: 1,
                mipLevels: 1,
                TextureFormat.Rgba8Unorm,
                TextureUsage.Sampled | TextureUsage.ColorAttachment,
                debugName: "D3D12 completed transient texture reuse");
            var versions = ImmutableArray.Create(
                registry.CreateTransientTexture(description),
                registry.CreateTransientTexture(description));
            GraphicsResourceAllocationLease? firstAllocationLease = null;

            for (var iteration = 0; iteration < 16; iteration++)
            {
                GraphicsResourceAllocationLease allocationLease;

                using (var group = graphicsDevice.CreateTransientTextureGroupAsync(
                           versions,
                           CancellationToken.None)
                       .GetAwaiter()
                       .GetResult())
                {
                    Assert.That(group.Textures.Length, Is.EqualTo(2));
                    allocationLease = group.Textures[0].ResourceOwner.AllocationLease
                        ?? throw new InvalidOperationException("The D3D12 transient texture group lost its shared allocation lease.");
                    Assert.That(group.Textures[1].ResourceOwner.AllocationLease, Is.SameAs(allocationLease));
                    firstAllocationLease ??= allocationLease;
                    Assert.That(allocationLease, Is.SameAs(firstAllocationLease));
                }

                Assert.That(
                    SpinWait.SpinUntil(() => allocationLease.ReferenceCount == 1, TimeSpan.FromSeconds(5)),
                    Is.True);
            }
        }
        finally
        {
            try
            {
                graphicsDevice?.ShutdownAsync().GetAwaiter().GetResult();
            }
            finally
            {
                platformOwner.DisposeFromOwner();
            }
        }
    }

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task LoadsStoredColorAndDepthAttachmentsAsync(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useDepthAttachment: true,
            useColorAndDepthAttachmentLoad: true);

    [Test]
    public Task PlansOverlappingTransientTextureLifetimesSeparatelyOnVulkan()
        => RunOffscreenGraphAsync(
            GraphicsBackend.Vulkan,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useTextureCopy: true,
            useTransientTextureCopy: true,
            useTransientTextureAliasing: true,
            overlapTransientTextureLifetimes: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task PlansSequentialTransientTextureAliasGroupsAndSubmitsAsync(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useTextureCopy: true,
            useTransientTextureCopy: true,
            useTransientTextureAliasing: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task RasterRenderAreaMayExceedGraphOutputWhenAttachmentIsLarger(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useAttachmentScopedRenderArea: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task RasterRenderAreaScopesAttachmentClear(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useRenderAreaClear: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task Rejects3DStorageImageShaderWhenBoundTo2DTexture(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: true,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            use3DStorageImage: true,
            expectStorageImageDimensionRejection: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsCombinedBufferAndTextureCopyGraphAndReadsBackAsync(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: true,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useTextureCopy: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsConstantWriteComputeStorageBufferSubrangeGraph(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useConstantWriteComputeBuffer: true,
            useStorageBufferSubrange: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsD32FloatSampledImage(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: true,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            sampledTextureFormat: TextureFormat.D32Float);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsDepthAttachmentGraph(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useDepthAttachment: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsDontCareDiscardColorAndDepthAttachmentsAsync(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useDepthAttachment: true,
            useAttachmentDontCareDiscardRoundTrip: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsDontCareDiscardWithRasterStorageBarrierAsync(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: true,
            useSampledTexture: true,
            useStorageResources: true,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useAttachmentDontCareDiscardRoundTrip: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsMatrixAndArrayFrameUniformBuffer(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: true,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useFrameLayoutUniformBuffer: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsMultipleColorAttachmentGraphAndReadsBackAsync(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useMultipleColorAttachments: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsMultipleComputeStorageBufferSubranges(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: true,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useStorageBufferSubrange: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsMultipleComputeStorageImageSubranges(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: true,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useMultipleComputeStorageImages: true,
            useStorageImageSubrange: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsMultipleComputeStorageImages(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: true,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useMultipleComputeStorageImages: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsMultipleFrameUniformBufferSubranges(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: true,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useMultipleFrameUniformBuffers: true,
            useUniformBufferSubrange: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsMultipleFrameUniformBuffers(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: true,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useMultipleFrameUniformBuffers: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsMultipleSampledImageMipSubranges(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: true,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useMultipleSampledImages: true,
            useSampledTextureSubrange: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsMultipleSampledImages(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: true,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useMultipleSampledImages: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsMultipleTextureCopyGraphAndReadsBackAsync(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useTextureCopy: true,
            useMultipleTextureCopies: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsNineBufferImportCopyGraphAndReadsBackAsync(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: true,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useManyBufferCopyDestinations: true);

    [TestCase(GraphicsBackend.D3D12, false, false, false, false, false, false, false, false)]
    [TestCase(GraphicsBackend.Vulkan, false, false, false, false, false, false, false, false)]
    [TestCase(GraphicsBackend.D3D12, true, false, false, false, false, false, false, false)]
    [TestCase(GraphicsBackend.Vulkan, true, false, false, false, false, false, false, false)]
    [TestCase(GraphicsBackend.D3D12, false, true, false, false, false, false, false, false)]
    [TestCase(GraphicsBackend.Vulkan, false, true, false, false, false, false, false, false)]
    [TestCase(GraphicsBackend.D3D12, true, true, false, false, false, false, false, false)]
    [TestCase(GraphicsBackend.Vulkan, true, true, false, false, false, false, false, false)]
    [TestCase(GraphicsBackend.D3D12, true, true, true, false, false, false, false, false)]
    [TestCase(GraphicsBackend.Vulkan, true, true, true, false, false, false, false, false)]
    [TestCase(GraphicsBackend.D3D12, false, false, false, true, false, false, false, false)]
    [TestCase(GraphicsBackend.Vulkan, false, false, false, true, false, false, false, false)]
    [TestCase(GraphicsBackend.D3D12, false, false, false, false, true, false, false, false)]
    [TestCase(GraphicsBackend.Vulkan, false, false, false, false, true, false, false, false)]
    [TestCase(GraphicsBackend.D3D12, false, false, false, false, false, true, false, false)]
    [TestCase(GraphicsBackend.Vulkan, false, false, false, false, false, true, false, false)]
    [TestCase(GraphicsBackend.D3D12, false, false, false, false, false, false, true, false)]
    [TestCase(GraphicsBackend.Vulkan, false, false, false, false, false, false, true, false)]
    [TestCase(GraphicsBackend.D3D12, false, false, false, false, false, false, false, true)]
    [TestCase(GraphicsBackend.Vulkan, false, false, false, false, false, false, false, true)]
    [TestCase(GraphicsBackend.D3D12, false, false, false, false, false, false, false, true, true)]
    [TestCase(GraphicsBackend.Vulkan, false, false, false, false, false, false, false, true, true)]
    public Task SubmitsOffscreenClearGraphAndReadsBackAsync(
        GraphicsBackend backend,
        bool useUniformBuffer,
        bool useSampledTexture,
        bool useStorageResources,
        bool useBufferCopy,
        bool useCompute,
        bool useTransientStorage,
        bool useMultipleComputeStorageBuffers,
        bool useTransientBufferAliasing,
        bool overlapTransientBufferLifetimes = false)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer,
            useSampledTexture,
            useStorageResources,
            useBufferCopy,
            useCompute,
            useTransientStorage,
            useMultipleComputeStorageBuffers,
            useTransientBufferAliasing,
            overlapTransientBufferLifetimes,
            holdGraphCompletion: false);

    [Test]
    public Task SubmitsPackageBackedTextureGraphOnVulkan()
        => RunOffscreenGraphAsync(
            GraphicsBackend.Vulkan,
            useUniformBuffer: false,
            useSampledTexture: true,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            usePackageResidencyTexture: true,
            graphicsFrameSlotCount: 4);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsPartialCombinedBufferAndTextureCopyGraphAndReadsBackAsync(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: true,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useTextureCopy: true,
            usePartialTextureCopy: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsPartialMultipleTextureCopyGraphAndReadsBackAsync(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useTextureCopy: true,
            useMultipleTextureCopies: true,
            usePartialTextureCopy: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsPartialTextureCopyGraphAndReadsBackAsync(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useTextureCopy: true,
            usePartialTextureCopy: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsR32FloatSampledImage(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: true,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            sampledTextureFormat: TextureFormat.R32Float);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsR32UIntColorAttachmentGraph(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            targetColorFormat: TextureFormat.R32UInt);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsR32UIntComputeStorageImageFor3DTexture(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: true,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            use3DStorageImage: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsR8UnormSampledArrayLayer(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: true,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            sampledTextureFormat: TextureFormat.R8Unorm,
            useArrayedSampledTexture: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsR8UnormSampledImage(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: true,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            sampledTextureFormat: TextureFormat.R8Unorm);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsRgba16FloatColorAttachmentGraph(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: true,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            sampledTextureFormat: TextureFormat.Rgba16Float,
            targetColorFormat: TextureFormat.Rgba16Float);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsRgba16FloatSampledImage(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: true,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            sampledTextureFormat: TextureFormat.Rgba16Float);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsRgba8SrgbColorAttachmentGraph(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: true,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            sampledTextureFormat: TextureFormat.Rgba8Srgb,
            targetColorFormat: TextureFormat.Rgba8Srgb);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsRgba8SrgbSampledImage(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: true,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            sampledTextureFormat: TextureFormat.Rgba8Srgb);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsSampledTextureMipSubrangeGraph(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: true,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useSampledTextureSubrange: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsSixteenSampledImagesWithEightSamplersAsync(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: true,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useSixteenSampledImages: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsStorageImageMipSubrangeGraph(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: true,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useRgba16FloatStorageImage: true,
            useStorageImageSubrange: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsTextureCopyGraphAndReadsBackAsync(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useTextureCopy: true);

    [Test]
    public Task SubmitsTransientBufferAliasGraphFromUploadBufferOnVulkan()
        => RunOffscreenGraphAsync(
            GraphicsBackend.Vulkan,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: true,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useUploadHeapGraphSources: true);

    [Test]
    public Task SubmitsTransientBufferAliasGroupsAcrossLiveSlotsOnVulkan()
        => RunOffscreenGraphAsync(
            GraphicsBackend.Vulkan,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: true,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: true,
            graphicsFrameSlotCount: 2,
            useUploadHeapGraphSources: true);

    [Test]
    public Task SubmitsTransientCopyAliasGroupsOnVulkan()
        => RunOffscreenGraphAsync(
            GraphicsBackend.Vulkan,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            graphicsFrameSlotCount: 2,
            useTransientCopyAliasing: true);

    [Test]
    public Task SubmitsTransientTextureAliasGroupsAcrossLiveSlotsOnVulkan()
        => RunOffscreenGraphAsync(
            GraphicsBackend.Vulkan,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: true,
            graphicsFrameSlotCount: 2,
            useTransientCopyAliasing: true,
            useDelayedTransientTextureAliasing: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsTransientTextureCopyGraphAndReadsBackAsync(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useTextureCopy: true,
            useTransientTextureCopy: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SubmitsUniformBufferSubrangeGraph(GraphicsBackend backend)
        => RunOffscreenGraphAsync(
            backend,
            useUniformBuffer: true,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useUniformBufferSubrange: true);

    [Test]
    [Category("ProcessIsolatedVulkanGraphQuarantine")]
    public Task VulkanGraphFenceTimeoutQuarantinesGraphResources()
    {
        if (!StringComparer.Ordinal.Equals(
            Environment.GetEnvironmentVariable("TERRAFX_NATIVE_QUARANTINE_CHILD"),
            "1"))
        {
            NativeQuarantineTestProcess.Run("TestCategory=ProcessIsolatedVulkanGraphQuarantine");
            return Task.CompletedTask;
        }

        return RunOffscreenGraphAsync(
            GraphicsBackend.Vulkan,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            simulateVulkanGraphFenceTimeout: true);
    }

    [Test]
    public Task VulkanRejectsR32FloatComputeStorageImageFormatMismatch()
        => RunOffscreenGraphAsync(
            GraphicsBackend.Vulkan,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: true,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useFloatStorageImage: true,
            expectStorageImageFormatRejection: true);

    [Test]
    public Task VulkanRejectsR32SIntComputeStorageImageWithoutTextureFormat()
        => RunOffscreenGraphAsync(
            GraphicsBackend.Vulkan,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: true,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useSignedStorageImage: true,
            expectStorageImageFormatRejection: true);

    [Test]
    public Task VulkanSubmitsConstantWriteComputeStorageBufferGraph()
        => RunOffscreenGraphAsync(
            GraphicsBackend.Vulkan,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useConstantWriteComputeBuffer: true);

    [Test]
    public Task VulkanSubmitsMultipleStorageBufferComputeGraph()
        => RunOffscreenGraphAsync(
            GraphicsBackend.Vulkan,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: true,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false);

    [Test]
    public Task VulkanSubmitsR32FloatComputeStorageImage()
        => RunOffscreenGraphAsync(
            GraphicsBackend.Vulkan,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: true,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useFloatStorageImage: true);

    [Test]
    public Task VulkanSubmitsRgba16FloatComputeStorageImage()
        => RunOffscreenGraphAsync(
            GraphicsBackend.Vulkan,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: true,
            useTransientStorage: false,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false,
            useRgba16FloatStorageImage: true);

    [Test]
    public Task VulkanSubmitsTransientStorageBufferComputeGraph()
        => RunOffscreenGraphAsync(
            GraphicsBackend.Vulkan,
            useUniformBuffer: false,
            useSampledTexture: false,
            useStorageResources: false,
            useBufferCopy: false,
            useCompute: false,
            useTransientStorage: true,
            useMultipleComputeStorageBuffers: false,
            useTransientBufferAliasing: false,
            overlapTransientBufferLifetimes: false,
            holdGraphCompletion: false);

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The gated graph test requests target retirement while GPU completion is withheld.")]
    private async Task RunOffscreenGraphAsync(
        GraphicsBackend backend,
        bool useUniformBuffer,
        bool useSampledTexture,
        bool useStorageResources,
        bool useBufferCopy,
        bool useCompute,
        bool useTransientStorage,
        bool useMultipleComputeStorageBuffers,
        bool useTransientBufferAliasing,
        bool overlapTransientBufferLifetimes,
        bool holdGraphCompletion,
        bool usePackageResidencyTexture = false,
        bool useTextureCopy = false,
        bool useTransientTextureCopy = false,
        bool useTransientTextureAliasing = false,
        bool useMultipleTextureCopies = false,
        bool usePartialTextureCopy = false,
        bool overlapTransientTextureLifetimes = false,
        int graphicsFrameSlotCount = 3,
        bool useTransientCopyAliasing = false,
        bool useDelayedTransientTextureAliasing = false,
        bool useUploadHeapGraphSources = false,
        bool useFloatStorageImage = false,
        bool useSignedStorageImage = false,
        bool useRgba16FloatStorageImage = false,
        bool expectStorageImageFormatRejection = false,
        bool useConstantWriteComputeBuffer = false,
        bool useStorageBufferSubrange = false,
        bool useUniformBufferSubrange = false,
        bool useSampledTextureSubrange = false,
        bool simulateGraphFenceTimeout = false,
        bool useStorageImageSubrange = false,
        bool simulateVulkanGraphFenceTimeout = false,
        bool useMultipleComputeStorageImages = false,
        bool useMultipleSampledImages = false,
        bool useMultipleFrameUniformBuffers = false,
        bool use3DStorageImage = false,
        bool expectStorageImageDimensionRejection = false,
        bool useFrameLayoutUniformBuffer = false,
        TextureFormat sampledTextureFormat = TextureFormat.Rgba8Unorm,
        bool useArrayedSampledTexture = false,
        bool useDepthAttachment = false,
        TextureFormat targetColorFormat = TextureFormat.Rgba8Unorm,
        bool useColorAndDepthAttachmentLoad = false,
        bool useAttachmentDontCareDiscardRoundTrip = false,
        bool useMultipleColorAttachments = false,
        bool useColorAttachmentCopy = false,
        bool useColorAttachmentCopyDestination = false,
        bool useEightUniformPipelineOnly = false,
        bool useSixteenSampledImages = false,
        bool useManyBufferCopyDestinations = false,
        bool useRenderAreaClear = false,
        bool useAttachmentScopedRenderArea = false,
        Action<Task<RenderSubmissionReceipt>, RenderSnapshot, RenderGraphPlan, Renderer, GraphicsDevice>? observeReturnedTask = null)
    {
        var frameUniformBindingIndex = useFrameLayoutUniformBuffer ? 2 : 0;
        var sampledImageBindingIndex = useArrayedSampledTexture ? 2 : 0;

        if ((backend == GraphicsBackend.D3D12) && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 offscreen graph path targets Windows 11.");
            return;
        }

        var shaderFixtureDirectory = Environment.GetEnvironmentVariable("TERRAFX_M04_SHADER_FIXTURE_DIRECTORY");

        if ((useUniformBuffer
                || useSampledTexture
                || useStorageResources
                || useSixteenSampledImages
                || useEightUniformPipelineOnly
                || useCompute
                || useTransientStorage
                || useMultipleComputeStorageBuffers
                || useConstantWriteComputeBuffer
                || useStorageBufferSubrange
                || useMultipleComputeStorageImages
                || useMultipleSampledImages
                || useMultipleFrameUniformBuffers
                || useFrameLayoutUniformBuffer
                || use3DStorageImage
                || useFloatStorageImage
                || useSignedStorageImage
                || useRgba16FloatStorageImage
                || useDepthAttachment
                || useMultipleColorAttachments
                || (targetColorFormat == TextureFormat.R32UInt))
            && string.IsNullOrWhiteSpace(shaderFixtureDirectory))
        {
            Assert.Ignore("The M04 shader fixture is required for resource-bound graph cases.");
            return;
        }

        var schemas = new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
            .. RenderingSchemaCatalogue.Schemas,
        ]);
        const int PackageTextureAssetId = 71;
        var sampleBytes = sampledTextureFormat switch {
            TextureFormat.R8Unorm => [128],
            TextureFormat.R32Float => BitConverter.GetBytes(0.5f),
            TextureFormat.D32Float => BitConverter.GetBytes(0.5f),
            TextureFormat.Rgba16Float => [0x00, 0x3C, 0x00, 0x38, 0x00, 0x34, 0x00, 0x3C],
            TextureFormat.Rgba8Srgb => [128, 64, 32, 255],
            _ => [32, 128, 224, 255],
        };

        if (useSixteenSampledImages)
        {
            sampleBytes = [0, 255, 0, 255];
        }

        var sixteenSampledTextures = new GpuTexture?[15];

        if (useArrayedSampledTexture)
        {
            if (sampledTextureFormat != TextureFormat.R8Unorm)
            {
                throw new ArgumentOutOfRangeException(nameof(sampledTextureFormat));
            }

            sampleBytes = [0, 128];
        }

        var sampledTextureAspect = sampledTextureFormat is TextureFormat.D32Float
            ? TextureAspect.Depth
            : TextureAspect.Color;
        var targetBytesPerPixel = targetColorFormat is TextureFormat.Rgba16Float ? 8 : 4;
        var packageBytes = SceneAssetStoreTests.Package.CreatePackage();

        if (usePackageResidencyTexture)
        {
            var mipRecord = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                new ContentSchemaIdentityAssignment("native/texture-mip").SchemaId,
                1,
                0,
                SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(1, 0),
                SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(2, 1),
                SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(3, 1),
                new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(
                    4,
                    13,
                    1,
                    checked((ulong)sampleBytes.Length),
                    sampleBytes));
            var textureRecord = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                new ContentSchemaIdentityAssignment("native/texture").SchemaId,
                1,
                0,
                SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(1, 1),
                SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(2, 1),
                SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(3, 0),
                SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(4, (byte)RenderTextureColorSpace.Linear),
                SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(5, (byte)RenderTextureAlphaMode.Straight),
                SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(6, (byte)RenderTextureSemantic.Color),
                SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(7, (byte)RenderTextureMipPolicy.BaseOnly),
                SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(8, [mipRecord]));
            packageBytes = SceneAssetStoreTests.Package.CreatePackage(
                new SceneAssetStoreTests.Package.Entry(
                    PackageTextureAssetId,
                    SceneAssetStoreTests.Package.CreateEnvelope(PackageTextureAssetId, textureRecord)));
        }

        var packagePath = SceneAssetStoreTests.Package.WritePackage(packageBytes);
        var assetStoreRegistrationBuilder = new AssetStoreRegistrationBuilder(schemas);

        if (usePackageResidencyTexture)
        {
            _ = Renderer.RegisterAssetCodecs(assetStoreRegistrationBuilder);
        }

        var assetStoreRegistration = assetStoreRegistrationBuilder.Build();
        using var twoFenceWaitsEntered = new ManualResetEventSlim(initialState: false);
        using var continueFenceWait = new ManualResetEventSlim(initialState: false);
        var holdFenceWaitCount = 0;
        var holdCompletedGraphFenceValue = 0;
        var enteredFenceWaitCount = 0;
        var completedGraphFenceWaitCount = 0;
        var nativeGraphFenceWaitResult = -1;
        var transientGraphAllocationLeases = new ConcurrentQueue<ImmutableArray<GraphicsResourceAllocationLease>>();
        var transientBufferGraphAllocationLeases = new ConcurrentQueue<ImmutableArray<GraphicsResourceAllocationLease>>();

        uint WaitForGraphFence(nint fenceEvent, uint timeout)
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                throw new PlatformNotSupportedException("The graph fence-wait witness requires Windows 11.");
            }

            if (Interlocked.Decrement(ref holdFenceWaitCount) >= 0)
            {
                if (Interlocked.Increment(ref enteredFenceWaitCount) == 2)
                {
                    twoFenceWaitsEntered.Set();
                }

                if (!continueFenceWait.Wait(TimeSpan.FromSeconds(10), CancellationToken.None))
                {
                    return 0xFFFFFFFF;
                }
            }

            var result = WindowsTestNativeMethods.WaitForSingleObject(fenceEvent, timeout);

            if (result == 0)
            {
                _ = Interlocked.Increment(ref completedGraphFenceWaitCount);
            }

            if (simulateGraphFenceTimeout && (result == 0))
            {
                Volatile.Write(ref nativeGraphFenceWaitResult, unchecked((int)result));
                return 0x00000102;
            }

            return result;
        }

        long OverrideCompletedFence(long completedFence)
        {
            return (Volatile.Read(ref holdCompletedGraphFenceValue) == 0) || continueFenceWait.IsSet
                ? completedFence
                : 0;
        }

        void WaitForVulkanGraphFence(long fenceValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fenceValue);

            if (Interlocked.Decrement(ref holdFenceWaitCount) >= 0)
            {
                if (Interlocked.Increment(ref enteredFenceWaitCount) == 2)
                {
                    twoFenceWaitsEntered.Set();
                }

                if (!continueFenceWait.Wait(TimeSpan.FromSeconds(10), CancellationToken.None))
                {
                    throw new TimeoutException("Timed out while holding Vulkan graph-fence observation.");
                }
            }
        }

        VkResult OverrideVulkanGraphFenceWait(long fenceValue, VkResult nativeResult)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fenceValue);

            if (simulateVulkanGraphFenceTimeout && (nativeResult == VkResult.VK_SUCCESS))
            {
                Volatile.Write(ref nativeGraphFenceWaitResult, (int)nativeResult);
                return VkResult.VK_TIMEOUT;
            }

            return nativeResult;
        }

        void ObserveGraphSubmission(GraphicsGraphSubmission submission)
        {
            var allocationLeases = ImmutableArray.CreateBuilder<GraphicsResourceAllocationLease>();

            foreach (var textureImport in submission.TextureImports)
            {
                if (!textureImport.IsTransient)
                {
                    continue;
                }

                var allocationLease = textureImport.Texture.ResourceOwner.AllocationLease
                    ?? throw new InvalidOperationException("A transient graph texture lost its allocation lease before native submission.");
                allocationLeases.Add(allocationLease);
            }

            if (allocationLeases.Count > 0)
            {
                transientGraphAllocationLeases.Enqueue(allocationLeases.ToImmutable());
            }

            var bufferAllocationLeases = ImmutableArray.CreateBuilder<GraphicsResourceAllocationLease>();

            foreach (var bufferImport in submission.BufferImports)
            {
                if (!bufferImport.IsTransient)
                {
                    continue;
                }

                var allocationLease = bufferImport.Buffer.ResourceOwner.AllocationLease
                    ?? throw new InvalidOperationException("A transient graph buffer lost its allocation lease before native submission.");
                bufferAllocationLeases.Add(allocationLease);
            }

            if (bufferAllocationLeases.Count > 0)
            {
                transientBufferGraphAllocationLeases.Enqueue(bufferAllocationLeases.ToImmutable());
            }
        }

        var graphicsRegistration = backend switch {
            GraphicsBackend.D3D12 when holdGraphCompletion || simulateGraphFenceTimeout => new GraphicsBackendRegistration(
                new D3D12GraphicsBackend.D3D12BackendFactory(
                    graphWaitForSingleObject: WaitForGraphFence,
                    completedFenceValueOverride: OverrideCompletedFence,
                    graphSubmissionObserver: (useTransientCopyAliasing || useTransientBufferAliasing) ? ObserveGraphSubmission : null)),
            GraphicsBackend.Vulkan when holdGraphCompletion || simulateVulkanGraphFenceTimeout
                => VulkanGraphicsBackend.CreateRegistrationForTesting(
                    graphFenceWaitObserver: WaitForVulkanGraphFence,
                    completedGraphFenceValueOverride: OverrideCompletedFence,
                    graphSubmissionObserver: (useTransientBufferAliasing || useTransientCopyAliasing || useDelayedTransientTextureAliasing)
                        ? ObserveGraphSubmission
                        : null,
                    graphFenceWaitResultOverride: simulateVulkanGraphFenceTimeout
                        ? OverrideVulkanGraphFenceWait
                        : null),
            GraphicsBackend.D3D12 => D3D12GraphicsBackend.CreateRegistration(),
            GraphicsBackend.Vulkan => VulkanGraphicsBackend.CreateRegistration(),
            _ => throw new ArgumentOutOfRangeException(nameof(backend)),
        };
        var options = new EngineHostOptions(
            HostProfile.OffscreenNative,
            PlatformBackend.Headless,
            backend,
            graphicsRegistration,
            new AssetStoreOptions(packagePath, SHA256.HashData(packageBytes), assetStoreRegistration)) {
            CpuWorkerCount = 0,
            PersistenceMetadata = CreatePersistenceMetadata(),
            GraphicsFrameSlotCount = graphicsFrameSlotCount,
        };
        EngineHost? host = null;
        GpuTexture? target = null;
        GpuTexture? oversizedAttachment = null;
        GpuTexture? oversizedDepthAttachment = null;
        var secondaryColorAttachmentTextures = new GpuTexture?[3];
        GpuTexture? depthAttachmentTexture = null;
        GpuTexture? delayedGraphTarget = null;
        GpuTexture? delayedGraphDestination = null;
        GpuTexture? copySourceTexture = null;
        GpuTexture? copyDestinationTexture = null;
        GpuTexture? secondCopySourceTexture = null;
        GpuTexture? secondCopyDestinationTexture = null;
        TextureDescription? textureCopyDescription = null;
        byte[]? sourceTextureCopyBytes = null;
        byte[]? expectedTextureCopyBytes = null;
        byte[]? expectedSecondTextureCopyBytes = null;
        GpuTexture? sampledTexture = null;
        GpuTexture? additionalSampledTexture = null;
        GpuResidencyLease? sampledTextureResidencyLease = null;
        GpuTexture? storageImage = null;
        GpuTexture? additionalStorageImage = null;
        GpuBuffer? frameUniformBuffer = null;
        GpuBuffer? additionalFrameUniformBuffer = null;
        long uniformBufferBindingOffset = 0;
        long additionalFrameUniformBindingOffset = 0;
        GpuBuffer? storageBuffer = null;
        long storageBufferImportOffset = 0;
        long storageBufferBindingOffset = 0;
        long secondStorageBufferImportOffset = 0;
        long secondStorageBufferBindingOffset = 0;
        long copyDestinationRangeOffset = 0;
        GpuBuffer? secondStorageBuffer = null;
        GpuBuffer? copySourceBuffer = null;
        GpuBuffer? secondCopySourceBuffer = null;
        GpuBuffer? copyDestinationBuffer = null;
        var additionalCopyDestinationBuffers = new GpuBuffer?[7];
        GpuBuffer? secondCopyDestinationBuffer = null;
        byte[]? expectedCopiedBytes = null;
        byte[]? expectedSecondCopiedBytes = null;
        GpuPipeline? pipeline = null;
        GpuPipeline? computePipeline = null;
        GpuSampler? frameSampler = null;
        RenderSnapshot? snapshot = null;
        RenderSnapshot? secondSnapshot = null;

        try
        {
            try
            {
                host = await EngineHost.StartAsync(options).ConfigureAwait(false);
            }
            catch (DllNotFoundException exception)
            {
                Assert.Ignore($"{backend} system library is unavailable: {exception.Message}");
            }
            catch (EntryPointNotFoundException exception)
            {
                Assert.Ignore($"{backend} system entry point is unavailable: {exception.Message}");
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No available {backend} device satisfies OffscreenNative: {exception.Message}");
            }

            var engineHost = host
                ?? throw new InvalidOperationException($"{backend} EngineHost startup returned no host.");
            var graphicsDevice = engineHost.GraphicsDevice
                ?? throw new InvalidOperationException($"{backend} OffscreenNative host returned no GraphicsDevice.");
            var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
            _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
            _ = worldRegistrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration());
            var worldRegistration = worldRegistrationBuilder.Build();
            World? world = null;
            Entity cameraEntity = default;
            var cameraId = Guid.Empty;
            var targetExtent = new PixelSize(32, 32);
            var initialCameraClearColor = (targetColorFormat == TextureFormat.R32UInt)
                ? Vector4.Zero
                : new Vector4(0.5f, 0.5f, 0.5f, 1.0f);

            static SceneComponentDefinitions.CameraComponent CreateCameraComponent(Vector4 clearColor)
            {
                return new(
                    (byte)CameraProjectionKind.Perspective,
                    float.Pi / 3.0f,
                    10.0f,
                    0.1f,
                    (byte)CameraFarMode.Finite,
                    new SceneComponentDefinitions.OptionalSingle(1000.0f),
                    default,
                    new Vector4(0.0f, 0.0f, 1.0f, 1.0f),
                    (byte)(CameraClearFlags.Color | CameraClearFlags.Depth),
                    clearColor,
                    ulong.MaxValue,
                    order: 0);
            }

            await engineHost.InvokeAsync(_ => {
                world = _.CreateWorld(new WorldOptions(worldRegistration) {
                    CpuWorkerCount = 0,
                    InitialReplayLineageId = Guid.NewGuid(),
                    WorldGuid = Guid.NewGuid(),
                });
                var camera = CreateCameraComponent(initialCameraClearColor);
                var transform = new SceneComponentDefinitions.LocalTransformComponent(
                    Vector3.Zero,
                    Quaternion.Identity,
                    Vector3.One);
                var rootPosition = new SceneComponentDefinitions.RootPositionComponent(default);
                cameraEntity = world.CreateEntity(new EntityDescription(components: [
                    ComponentInitialization.Value(in camera),
                    ComponentInitialization.Value(in transform),
                    ComponentInitialization.Value(in rootPosition),
                ]));
                cameraId = world.GetEntityId(cameraEntity);
                var publication = world.Step().Publication;
                var extraction = engineHost.Renderer.Extract(
                    world,
                    new RenderExtractionOptions3D(publication.Frame3, targetExtent, [cameraId]));

                if (extraction.Status != RenderExtractionStatus.Extracted)
                {
                    throw new InvalidOperationException(
                        $"{backend} graph submission snapshot extraction failed: {string.Join("; ", extraction.Diagnostics)}");
                }

                snapshot = extraction.Snapshot
                    ?? throw new InvalidOperationException("The successful extraction omitted its render snapshot.");
            }).ConfigureAwait(false);

            var targetUsage = TextureUsage.ColorAttachment | TextureUsage.CopySource;

            if (backend == GraphicsBackend.Vulkan)
            {
                targetUsage |= TextureUsage.CopyDestination;
            }

            target = await graphicsDevice.CreateTextureAsync(
                new TextureDescription(
                    TextureDimension.Dimension2D,
                    targetExtent.Width,
                    targetExtent.Height,
                    depth: 1,
                    arrayLayers: 1,
                    mipLevels: 1,
                    targetColorFormat,
                    targetUsage,
                    $"m04-{backend}-offscreen-clear"))
                .ConfigureAwait(false);

            if (useAttachmentScopedRenderArea)
            {
                oversizedAttachment = await graphicsDevice.CreateTextureAsync(
                        new TextureDescription(
                            TextureDimension.Dimension2D,
                            64,
                            64,
                            depth: 1,
                            arrayLayers: 1,
                            mipLevels: 1,
                            TextureFormat.Rgba8Unorm,
                            TextureUsage.ColorAttachment | TextureUsage.CopySource,
                            $"m05-{backend}-oversized-render-area-attachment"))
                    .ConfigureAwait(false);
                oversizedDepthAttachment = await graphicsDevice.CreateTextureAsync(
                    new TextureDescription(
                        TextureDimension.Dimension2D,
                        128,
                        64,
                        depth: 1,
                        arrayLayers: 1,
                        mipLevels: 1,
                        TextureFormat.D32Float,
                        TextureUsage.DepthAttachment | TextureUsage.Sampled | TextureUsage.CopySource,
                        $"m05-{backend}-oversized-depth-render-area-attachment"))
                    .ConfigureAwait(false);
            }

            if (useMultipleColorAttachments)
            {
                for (var index = 0; index < secondaryColorAttachmentTextures.Length; index++)
                {
                    secondaryColorAttachmentTextures[index] = await graphicsDevice.CreateTextureAsync(
                        new TextureDescription(
                            TextureDimension.Dimension2D,
                            targetExtent.Width,
                            targetExtent.Height,
                            depth: 1,
                            arrayLayers: 1,
                            mipLevels: 1,
                            TextureFormat.Rgba8Unorm,
                            TextureUsage.ColorAttachment | TextureUsage.CopySource,
                            $"m04-{backend}-color-attachment-{index + 1}"))
                        .ConfigureAwait(false);
                }
            }

            if (useColorAttachmentCopy)
            {
                var copyDestinationUsage = TextureUsage.CopySource | TextureUsage.CopyDestination;

                if (useColorAttachmentCopyDestination)
                {
                    copyDestinationUsage |= TextureUsage.ColorAttachment;
                }

                copyDestinationTexture = await graphicsDevice.CreateTextureAsync(
                    new TextureDescription(
                        TextureDimension.Dimension2D,
                        targetExtent.Width,
                        targetExtent.Height,
                        depth: 1,
                        arrayLayers: 1,
                        mipLevels: 1,
                        TextureFormat.Rgba8Unorm,
                        copyDestinationUsage,
                        $"m04-{backend}-color-attachment-copy-destination"))
                    .ConfigureAwait(false);
            }
            else if (useColorAttachmentCopyDestination)
            {
                copyDestinationTexture = await graphicsDevice.CreateTextureAsync(
                    new TextureDescription(
                        TextureDimension.Dimension2D,
                        targetExtent.Width,
                        targetExtent.Height,
                        depth: 1,
                        arrayLayers: 1,
                        mipLevels: 1,
                        TextureFormat.Rgba8Unorm,
                        TextureUsage.CopySource | TextureUsage.CopyDestination | TextureUsage.ColorAttachment,
                        $"m04-{backend}-color-attachment-copy-destination"))
                    .ConfigureAwait(false);
            }

            if (useSixteenSampledImages)
            {
                if (sampledTextureFormat != TextureFormat.Rgba8Unorm)
                {
                    throw new InvalidOperationException("The sixteen-image graph witness requires RGBA8Unorm sampled textures.");
                }

                for (var index = 0; index < sixteenSampledTextures.Length; index++)
                {
                    var imageIndex = index + 1;
                    var texture = await graphicsDevice.CreateTextureAsync(
                        new TextureDescription(
                            TextureDimension.Dimension2D,
                            width: 1,
                            height: 1,
                            depth: 1,
                            arrayLayers: 1,
                            mipLevels: 1,
                            TextureFormat.Rgba8Unorm,
                            TextureUsage.Sampled | TextureUsage.CopyDestination,
                            $"m04-{backend}-sampled-texture-{imageIndex}"))
                        .ConfigureAwait(false);
                    sixteenSampledTextures[index] = texture;
                    byte[] textureBytes = [
                        checked((byte)(imageIndex * 16)),
                        checked((byte)(255 - (imageIndex * 16))),
                        checked((byte)(imageIndex * 8)),
                        byte.MaxValue,
                    ];
                    var sampleStorage = UnmanagedBuffer<byte>.Allocate(
                        textureBytes.Length,
                        new BufferAllocationOptions(textureBytes.Length, alignment: 1));
                    sampleStorage.CopyFrom(textureBytes.AsSpan(), 0);

                    using var ownedSampleBytes = sampleStorage.TransferToOwnedBuffer();
                    using var sampleLease = ownedSampleBytes.AcquireReadLease(0, textureBytes.Length);
                    var uploadReceipt = await graphicsDevice.UploadTextureAsync(
                        texture,
                        new TextureSubresource(0, 0, TextureAspect.Color),
                        sampleLease,
                        0,
                        new TextureDataLayout(rowPitch: textureBytes.Length, slicePitch: textureBytes.Length))
                        .ConfigureAwait(false);
                    Assert.That(uploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));
                }
            }

            if (useDepthAttachment)
            {
                depthAttachmentTexture = await graphicsDevice.CreateTextureAsync(
                    new TextureDescription(
                        TextureDimension.Dimension2D,
                        targetExtent.Width,
                        targetExtent.Height,
                        depth: 1,
                        arrayLayers: 1,
                        mipLevels: 1,
                        TextureFormat.D32Float,
                        TextureUsage.DepthAttachment | TextureUsage.Sampled,
                        $"m04-{backend}-depth-attachment"))
                    .ConfigureAwait(false);
            }

            if (backend == GraphicsBackend.Vulkan)
            {
                var initialTargetBytes = new byte[checked(targetExtent.Width * targetExtent.Height * targetBytesPerPixel)];
                Array.Fill(initialTargetBytes, (byte)0x3D);
                var initialTargetStorage = UnmanagedBuffer<byte>.Allocate(
                    initialTargetBytes.Length,
                    new BufferAllocationOptions(initialTargetBytes.Length, alignment: 1));
                initialTargetStorage.CopyFrom(initialTargetBytes.AsSpan(), 0);

                using var ownedInitialTarget = initialTargetStorage.TransferToOwnedBuffer();
                using var initialTargetLease = ownedInitialTarget.AcquireReadLease(0, initialTargetBytes.Length);
                var uploadReceipt = await graphicsDevice.UploadTextureAsync(
                    target,
                    new TextureSubresource(0, 0, TextureAspect.Color),
                    initialTargetLease,
                    0,
                    new TextureDataLayout(targetExtent.Width * targetBytesPerPixel, initialTargetBytes.Length))
                    .ConfigureAwait(false);
                Assert.That(uploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));
            }

            if (useTransientCopyAliasing)
            {
                copyDestinationTexture = await graphicsDevice.CreateTextureAsync(
                    new TextureDescription(
                        TextureDimension.Dimension2D,
                        targetExtent.Width,
                        targetExtent.Height,
                        depth: 1,
                        arrayLayers: 1,
                        mipLevels: 1,
                        TextureFormat.Rgba8Unorm,
                        TextureUsage.CopySource | TextureUsage.CopyDestination,
                        $"m04-{backend}-slot-transient-copy-destination"))
                    .ConfigureAwait(false);
            }

            if (useMultipleSampledImages)
            {
                additionalSampledTexture = await graphicsDevice.CreateTextureAsync(
                    new TextureDescription(
                        TextureDimension.Dimension2D,
                        useSampledTextureSubrange ? 2 : 1,
                        useSampledTextureSubrange ? 2 : 1,
                        depth: 1,
                        arrayLayers: 1,
                        mipLevels: useSampledTextureSubrange ? 2 : 1,
                        TextureFormat.Rgba8Unorm,
                        TextureUsage.Sampled | TextureUsage.CopyDestination,
                        $"m04-{backend}-frame-second-sampled-texture"))
                    .ConfigureAwait(false);

                byte[] additionalSampleBytes = [64, 32, 32, 255];
                var additionalSampleStorage = UnmanagedBuffer<byte>.Allocate(
                    additionalSampleBytes.Length,
                    new BufferAllocationOptions(additionalSampleBytes.Length, alignment: 1));
                additionalSampleStorage.CopyFrom(additionalSampleBytes.AsSpan(), 0);

                using var ownedAdditionalSampleBytes = additionalSampleStorage.TransferToOwnedBuffer();
                using var additionalSampleLease = ownedAdditionalSampleBytes.AcquireReadLease(
                    0,
                    additionalSampleBytes.Length);
                UploadReceipt additionalSampleUploadReceipt;

                try
                {
                    if (useSampledTextureSubrange)
                    {
                        var additionalBaseMipBytes = new byte[16];

                        for (var pixelIndex = 0; pixelIndex < 4; pixelIndex++)
                        {
                            var pixelOffset = checked(pixelIndex * 4);
                            additionalBaseMipBytes[pixelOffset + 1] = 255;
                            additionalBaseMipBytes[pixelOffset + 3] = 255;
                        }

                        var additionalBaseMipStorage = UnmanagedBuffer<byte>.Allocate(
                            additionalBaseMipBytes.Length,
                            new BufferAllocationOptions(additionalBaseMipBytes.Length, alignment: 1));
                        additionalBaseMipStorage.CopyFrom(additionalBaseMipBytes.AsSpan(), 0);

                        using var ownedAdditionalBaseMipBytes = additionalBaseMipStorage.TransferToOwnedBuffer();
                        using var additionalBaseMipLease = ownedAdditionalBaseMipBytes.AcquireReadLease(
                            0,
                            additionalBaseMipBytes.Length);
                        var additionalBaseMipUploadReceipt = await graphicsDevice.UploadTextureAsync(
                            additionalSampledTexture,
                            new TextureSubresource(0, 0, TextureAspect.Color),
                            additionalBaseMipLease,
                            0,
                            new TextureDataLayout(rowPitch: 8, slicePitch: 16))
                            .ConfigureAwait(false);
                        Assert.That(additionalBaseMipUploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));
                    }

                    additionalSampleUploadReceipt = await graphicsDevice.UploadTextureAsync(
                        additionalSampledTexture,
                        new TextureSubresource(useSampledTextureSubrange ? 1 : 0, 0, TextureAspect.Color),
                        additionalSampleLease,
                        0,
                        new TextureDataLayout(rowPitch: 4, slicePitch: 4))
                        .ConfigureAwait(false);
                }
                catch (OutOfMemoryException exception) when (
                    (backend == GraphicsBackend.D3D12)
                    && exception.Message.Contains("staging budget", StringComparison.Ordinal))
                {
                    Assert.Ignore($"D3D12 sampled-image validation requires an available staging budget: {exception.Message}");
                    throw;
                }

                Assert.That(additionalSampleUploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));
            }

            if (useDelayedTransientTextureAliasing)
            {
                delayedGraphTarget = await graphicsDevice.CreateTextureAsync(
                    new TextureDescription(
                        TextureDimension.Dimension2D,
                        targetExtent.Width,
                        targetExtent.Height,
                        depth: 1,
                        arrayLayers: 1,
                        mipLevels: 1,
                        TextureFormat.Rgba8Unorm,
                        TextureUsage.ColorAttachment | TextureUsage.CopySource,
                        $"m04-{backend}-delayed-slot-target"))
                    .ConfigureAwait(false);
                delayedGraphDestination = await graphicsDevice.CreateTextureAsync(
                    new TextureDescription(
                        TextureDimension.Dimension2D,
                        targetExtent.Width,
                        targetExtent.Height,
                        depth: 1,
                        arrayLayers: 1,
                        mipLevels: 1,
                        TextureFormat.Rgba8Unorm,
                        TextureUsage.CopySource | TextureUsage.CopyDestination,
                        $"m04-{backend}-delayed-slot-copy-destination"))
                    .ConfigureAwait(false);
            }

            if (useTextureCopy)
            {
                if (usePartialTextureCopy)
                {
                    sourceTextureCopyBytes = [
                        0x00, 0x01, 0x02, 0xFF, 0x10, 0x11, 0x12, 0xFF, 0x20, 0x21, 0x22, 0xFF, 0x30, 0x31, 0x32, 0xFF,
                        0x40, 0x41, 0x42, 0xFF, 0x50, 0x51, 0x52, 0xFF, 0x60, 0x61, 0x62, 0xFF, 0x70, 0x71, 0x72, 0xFF,
                        0x80, 0x81, 0x82, 0xFF, 0x90, 0x91, 0x92, 0xFF, 0xA0, 0xA1, 0xA2, 0xFF, 0xB0, 0xB1, 0xB2, 0xFF,
                        0xC0, 0xC1, 0xC2, 0xFF, 0xD0, 0xD1, 0xD2, 0xFF, 0xE0, 0xE1, 0xE2, 0xFF, 0xF0, 0xF1, 0xF2, 0xFF,
                    ];
                    expectedTextureCopyBytes = [
                        0x50, 0x51, 0x52, 0xFF,
                        0x60, 0x61, 0x62, 0xFF,
                        0x90, 0x91, 0x92, 0xFF,
                        0xA0, 0xA1, 0xA2, 0xFF,
                    ];
                }
                else
                {
                    expectedTextureCopyBytes = [
                        0x13, 0x29, 0x4F, 0x97,
                        0xE3, 0x35, 0x6B, 0xB9,
                        0x11, 0x23, 0x47, 0x8B,
                        0xD1, 0x2D, 0x5B, 0xA7,
                    ];
                    sourceTextureCopyBytes = expectedTextureCopyBytes;
                }

                var copyTextureUsage = TextureUsage.CopySource | TextureUsage.CopyDestination;
                var copyTextureDescription = new TextureDescription(
                    TextureDimension.Dimension2D,
                    width: (useTransientTextureCopy || useTransientTextureAliasing) ? 2 : 4,
                    height: (useTransientTextureCopy || useTransientTextureAliasing) ? 2 : 4,
                    depth: 1,
                    arrayLayers: (useTransientTextureCopy || useTransientTextureAliasing || usePartialTextureCopy) ? 1 : 2,
                    mipLevels: (useTransientTextureCopy || useTransientTextureAliasing || usePartialTextureCopy) ? 1 : 2,
                    TextureFormat.Rgba8Unorm,
                    copyTextureUsage,
                    $"m04-{backend}-graph-copy-texture");
                textureCopyDescription = copyTextureDescription;
                copySourceTexture = await graphicsDevice.CreateTextureAsync(copyTextureDescription).ConfigureAwait(false);
                var copyDestinationDescription = usePartialTextureCopy
                    ? new TextureDescription(
                        TextureDimension.Dimension2D,
                        width: 2,
                        height: 2,
                        depth: 1,
                        arrayLayers: 1,
                        mipLevels: 1,
                        TextureFormat.Rgba8Unorm,
                        copyTextureUsage,
                        $"m04-{backend}-graph-copy-destination-texture")
                    : copyTextureDescription;
                copyDestinationTexture = await graphicsDevice.CreateTextureAsync(copyDestinationDescription).ConfigureAwait(false);
                var sourceStorage = UnmanagedBuffer<byte>.Allocate(
                    sourceTextureCopyBytes.Length,
                    new BufferAllocationOptions(sourceTextureCopyBytes.Length, alignment: 1));
                sourceStorage.CopyFrom(sourceTextureCopyBytes.AsSpan(), 0);

                using var ownedSourceBytes = sourceStorage.TransferToOwnedBuffer();
                using var sourceLease = ownedSourceBytes.AcquireReadLease(0, sourceTextureCopyBytes.Length);
                UploadReceipt sourceUploadReceipt;

                try
                {
                    var sourceSubresource = (useTransientTextureCopy || useTransientTextureAliasing || usePartialTextureCopy)
                        ? new TextureSubresource(0, 0, TextureAspect.Color)
                        : new TextureSubresource(1, 1, TextureAspect.Color);
                    sourceUploadReceipt = await graphicsDevice.UploadTextureAsync(
                        copySourceTexture,
                        sourceSubresource,
                        sourceLease,
                        0,
                        new TextureDataLayout(
                            rowPitch: (usePartialTextureCopy ? 4 : 2) * 4,
                            slicePitch: sourceTextureCopyBytes.Length))
                        .ConfigureAwait(false);
                }
                catch (OutOfMemoryException exception) when (
                    (backend == GraphicsBackend.D3D12)
                    && exception.Message.Contains("staging budget", StringComparison.Ordinal))
                {
                    Assert.Ignore($"{backend} graph texture-copy validation requires an available staging budget: {exception.Message}");
                    throw;
                }

                Assert.That(sourceUploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));

                if (useMultipleTextureCopies)
                {
                    expectedSecondTextureCopyBytes = [
                        0xA1, 0x37, 0xE2, 0x4B,
                        0x18, 0xC5, 0x6A, 0xD3,
                        0x2F, 0x84, 0x59, 0xB6,
                        0x0D, 0xFA, 0x73, 0x21,
                    ];
                    var secondCopyTextureDescription = usePartialTextureCopy
                        ? new TextureDescription(
                            TextureDimension.Dimension2D,
                            width: 2,
                            height: 2,
                            depth: 1,
                            arrayLayers: 1,
                            mipLevels: 1,
                            TextureFormat.Rgba8Unorm,
                            copyTextureUsage,
                            $"m04-{backend}-graph-second-copy-texture")
                        : copyTextureDescription;
                    secondCopySourceTexture = await graphicsDevice.CreateTextureAsync(secondCopyTextureDescription)
                        .ConfigureAwait(false);
                    secondCopyDestinationTexture = await graphicsDevice.CreateTextureAsync(secondCopyTextureDescription)
                        .ConfigureAwait(false);
                    var secondSourceStorage = UnmanagedBuffer<byte>.Allocate(
                        expectedSecondTextureCopyBytes.Length,
                        new BufferAllocationOptions(expectedSecondTextureCopyBytes.Length, alignment: 1));
                    secondSourceStorage.CopyFrom(expectedSecondTextureCopyBytes.AsSpan(), 0);

                    using var ownedSecondSourceBytes = secondSourceStorage.TransferToOwnedBuffer();
                    using var secondSourceLease = ownedSecondSourceBytes.AcquireReadLease(
                        0,
                        expectedSecondTextureCopyBytes.Length);
                    UploadReceipt secondSourceUploadReceipt;

                    try
                    {
                        secondSourceUploadReceipt = await graphicsDevice.UploadTextureAsync(
                            secondCopySourceTexture,
                            new TextureSubresource(
                                usePartialTextureCopy ? 0 : 1,
                                usePartialTextureCopy ? 0 : 1,
                                TextureAspect.Color),
                            secondSourceLease,
                            0,
                            new TextureDataLayout(rowPitch: 2 * 4, slicePitch: expectedSecondTextureCopyBytes.Length))
                            .ConfigureAwait(false);
                    }
                    catch (OutOfMemoryException exception) when (
                        (backend == GraphicsBackend.D3D12)
                        && exception.Message.Contains("staging budget", StringComparison.Ordinal))
                    {
                        Assert.Ignore($"{backend} graph texture-copy validation requires an available staging budget: {exception.Message}");
                        throw;
                    }

                    Assert.That(secondSourceUploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));
                }
            }

            if (useSampledTexture)
            {
                if (usePackageResidencyTexture)
                {
                    using var sampleAssetLease = await engineHost.Assets.LoadAsync<RenderTextureAsset>(
                        new AssetId(PackageTextureAssetId)).ConfigureAwait(false);
                    sampledTextureResidencyLease = await (engineHost.GpuResidency
                        ?? throw new InvalidOperationException("OffscreenNative startup returned no GPU residency service."))
                        .EnsureGpuResidencyAsync(sampleAssetLease, new ResidencyOptions())
                        .ConfigureAwait(false);
                    sampledTexture = sampledTextureResidencyLease.Texture;
                }
                else
                {
                    var textureWidth = useSampledTextureSubrange ? 2 : 1;
                    var textureMipLevels = useSampledTextureSubrange ? 2 : 1;
                    var textureArrayLayers = useArrayedSampledTexture ? 2 : 1;
                    sampledTexture = await graphicsDevice.CreateTextureAsync(
                        new TextureDescription(
                            TextureDimension.Dimension2D,
                            textureWidth,
                            textureWidth,
                            depth: 1,
                            arrayLayers: textureArrayLayers,
                            mipLevels: textureMipLevels,
                            sampledTextureFormat,
                            TextureUsage.Sampled | TextureUsage.CopyDestination,
                            $"m04-{backend}-frame-sampled-texture"))
                        .ConfigureAwait(false);

                    try
                    {
                        if (useSampledTextureSubrange)
                        {
                            var baseMipBytes = new byte[16];

                            for (var pixelIndex = 0; pixelIndex < 4; pixelIndex++)
                            {
                                var pixelOffset = checked(pixelIndex * 4);
                                baseMipBytes[pixelOffset] = 255;
                                baseMipBytes[pixelOffset + 3] = 255;
                            }

                            var baseMipStorage = UnmanagedBuffer<byte>.Allocate(
                                baseMipBytes.Length,
                                new BufferAllocationOptions(baseMipBytes.Length, alignment: 1));
                            baseMipStorage.CopyFrom(baseMipBytes.AsSpan(), 0);

                            using var ownedBaseMipBytes = baseMipStorage.TransferToOwnedBuffer();
                            using var baseMipLease = ownedBaseMipBytes.AcquireReadLease(0, baseMipBytes.Length);
                            var baseMipUploadReceipt = await graphicsDevice.UploadTextureAsync(
                                sampledTexture,
                                new TextureSubresource(0, 0, sampledTextureAspect),
                                baseMipLease,
                                0,
                                new TextureDataLayout(rowPitch: 8, slicePitch: 16))
                                .ConfigureAwait(false);
                            Assert.That(baseMipUploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));
                        }

                        var sampleStorage = UnmanagedBuffer<byte>.Allocate(
                            sampleBytes.Length,
                            new BufferAllocationOptions(sampleBytes.Length, alignment: 1));
                        sampleStorage.CopyFrom(sampleBytes.AsSpan(), 0);

                        using var ownedSampleBytes = sampleStorage.TransferToOwnedBuffer();
                        using var sampleLease = ownedSampleBytes.AcquireReadLease(0, sampleBytes.Length);
                        var sampleMipLevel = useSampledTextureSubrange ? 1 : 0;
                        var sampleByteCount = useArrayedSampledTexture ? 1 : sampleBytes.Length;
                        var sampleUploadReceipt = await graphicsDevice.UploadTextureAsync(
                            sampledTexture,
                            new TextureSubresource(sampleMipLevel, 0, sampledTextureAspect),
                            sampleLease,
                            0,
                            new TextureDataLayout(rowPitch: sampleByteCount, slicePitch: sampleByteCount))
                            .ConfigureAwait(false);
                        Assert.That(sampleUploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));

                        if (useArrayedSampledTexture)
                        {
                            var secondSampleUploadReceipt = await graphicsDevice.UploadTextureAsync(
                                sampledTexture,
                                new TextureSubresource(0, 1, sampledTextureAspect),
                                sampleLease,
                                1,
                                new TextureDataLayout(rowPitch: 1, slicePitch: 1))
                                .ConfigureAwait(false);
                            Assert.That(secondSampleUploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));
                        }
                    }
                    catch (OutOfMemoryException exception) when (
                        (backend == GraphicsBackend.D3D12)
                        && exception.Message.Contains("staging budget", StringComparison.Ordinal))
                    {
                        Assert.Ignore($"D3D12 sampled-image validation requires an available staging budget: {exception.Message}");
                        throw;
                    }
                }

                frameSampler = await graphicsDevice.CreateSamplerAsync(
                    new SamplerDescription(
                        SamplerFilter.Linear,
                        SamplerFilter.Linear,
                        SamplerFilter.None,
                        SamplerAddressMode.Clamp,
                        SamplerAddressMode.Clamp,
                        SamplerAddressMode.Clamp,
                        maximumAnisotropy: 1.0f))
                    .ConfigureAwait(false);
            }

            var renderer = engineHost.Renderer;
            var renderSnapshot = snapshot
                ?? throw new InvalidOperationException("The host renderer omitted its render snapshot.");

            if (useRenderAreaClear)
            {
                var invalidGraph = renderer.BuildGraph(renderSnapshot, target);
                var renderArea = new RenderArea(targetExtent.Width / 2, targetExtent.Height / 2, 1, 1);
                var invalidAreaPass = invalidGraph.AddRasterPass(new RenderPassDescription(
                    "offscreen-invalid-partial-area",
                    0,
                    [],
                    hasExternalSideEffects: false,
                    renderArea: renderArea));
                var invalidAreaTexture = invalidGraph.CreateTransientTexture(new TextureDescription(
                    TextureDimension.Dimension2D,
                    targetExtent.Width,
                    targetExtent.Height,
                    depth: 1,
                    arrayLayers: 1,
                    mipLevels: 1,
                    TextureFormat.Rgba8Unorm,
                    TextureUsage.ColorAttachment,
                    "offscreen-invalid-partial-area"));

                _ = Assert.Throws<ArgumentException>(() => invalidGraph.WriteColorAttachment(
                    invalidAreaPass,
                    invalidAreaTexture,
                    new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color),
                    AttachmentLoadAction.Clear,
                    AttachmentStoreAction.Store,
                    Vector4.Zero));
                _ = Assert.Throws<ArgumentException>(() => invalidGraph.AddComputePass(new RenderPassDescription(
                    "offscreen-invalid-compute-area",
                    1,
                    [],
                    hasExternalSideEffects: false,
                    renderArea: renderArea)));
            }

            if (!string.IsNullOrWhiteSpace(shaderFixtureDirectory))
            {
                var shaderExtension = backend == GraphicsBackend.D3D12 ? "dxil" : "spv";
                var vertexStage = useDepthAttachment ? "vertex-depth" : "vertex";
                var pixelStage = useEightUniformPipelineOnly
                    ? "pixel-eight-uniforms"
                    : useSixteenSampledImages
                    ? "pixel-sampled-sixteen"
                    : useMultipleColorAttachments
                    ? "pixel-multiple-color-targets"
                    : (targetColorFormat == TextureFormat.R32UInt)
                        ? "pixel-r32uint"
                        : useDepthAttachment
                            ? "pixel-depth"
                            : useFrameLayoutUniformBuffer
                            ? "pixel-frame-layout"
                            : useStorageResources
                                ? "pixel-sampled-frame-storage"
                                : useMultipleFrameUniformBuffers
                                ? "pixel-multiple-frame-uniforms"
                                : useMultipleSampledImages
                                ? "pixel-sampled-multiple"
                                : useSampledTexture
                                ? useArrayedSampledTexture
                                    ? "pixel-sampled-array"
                                    : (useUniformBuffer ? "pixel-sampled-frame" : "pixel-sampled")
                                : (useUniformBuffer ? "pixel" : "pixel-graph");
                using var vertexBytecodeStorage = CreateBytecodeStorage(Path.Combine(
                    shaderFixtureDirectory,
                    $"M04OffscreenTriangle.{vertexStage}.{shaderExtension}"));
                using var pixelBytecodeStorage = CreateBytecodeStorage(Path.Combine(
                    shaderFixtureDirectory,
                    $"M04OffscreenTriangle.{pixelStage}.{shaderExtension}"));
                using var vertexBytecode = vertexBytecodeStorage.AcquireReadLease(0, vertexBytecodeStorage.Length);
                using var pixelBytecode = pixelBytecodeStorage.AcquireReadLease(0, pixelBytecodeStorage.Length);
                var vertexReflection = M04ShaderFixtureCompiler.ReadTypedReflection(shaderFixtureDirectory, vertexStage);
                var pixelReflection = M04ShaderFixtureCompiler.ReadTypedReflection(shaderFixtureDirectory, pixelStage);
                var binaryFormat = backend == GraphicsBackend.D3D12
                    ? ShaderBinaryFormat.Dxil
                    : ShaderBinaryFormat.SpirV;
                ImmutableArray<TextureFormat> colorTargetFormats = useMultipleColorAttachments
                    ? [
                        targetColorFormat,
                        TextureFormat.Rgba8Unorm,
                        TextureFormat.Rgba8Unorm,
                        TextureFormat.Rgba8Unorm,
                    ]
                    : [targetColorFormat];
                var pipelineDescription = new GraphicsPipelineDescription(
                    [
                        new ShaderBytecodeInput(ShaderStage.Vertex, binaryFormat, vertexBytecode, vertexReflection),
                        new ShaderBytecodeInput(ShaderStage.Pixel, binaryFormat, pixelBytecode, pixelReflection),
                    ],
                    [],
                    vertexStride: 0,
                    colorTargetFormats,
                    depthTargetFormat: useDepthAttachment ? TextureFormat.D32Float : null,
                    PrimitiveTopology.TriangleList,
                    PipelineBlendMode.Opaque,
                    useDepthAttachment
                        ? PipelineDepthMode.ReversedGreaterEqualWrite
                        : PipelineDepthMode.Disabled,
                    PipelineCullMode.None,
                    frontFaceClockwise: false,
                    bindingLayoutVersion: 1);
                pipeline = await graphicsDevice.CreatePipelineAsync(pipelineDescription, CancellationToken.None)
                    .ConfigureAwait(false);

                if (useEightUniformPipelineOnly)
                {
                    Assert.That(pipeline.Handle.IsValid, Is.True);
                    return;
                }

                if (useCompute
                    || useTransientStorage
                    || useMultipleComputeStorageBuffers
                    || useConstantWriteComputeBuffer)
                {
                    var computeStage = use3DStorageImage
                        ? "compute-storage-3d-image"
                        : useConstantWriteComputeBuffer
                        ? "compute-storage-write"
                        : useMultipleComputeStorageImages
                            ? "compute-storage-images"
                            : useFloatStorageImage
                            ? "compute-storage-float-image"
                            : useRgba16FloatStorageImage
                                ? "compute-storage-rgba16-image"
                                : useSignedStorageImage
                                    ? "compute-storage-signed-image"
                                    : useMultipleComputeStorageBuffers
                                        ? "compute-storage-buffers"
                                        : "compute-storage";
                    using var computeBytecodeStorage = CreateBytecodeStorage(Path.Combine(
                        shaderFixtureDirectory,
                        $"M04OffscreenTriangle.{computeStage}.{shaderExtension}"));
                    using var computeBytecode = computeBytecodeStorage.AcquireReadLease(0, computeBytecodeStorage.Length);
                    var computeReflection = M04ShaderFixtureCompiler.ReadTypedReflection(shaderFixtureDirectory, computeStage);
                    var computePipelineDescription = new ComputePipelineDescription(
                        new ShaderBytecodeInput(ShaderStage.Compute, binaryFormat, computeBytecode, computeReflection),
                        bindingLayoutVersion: 1);
                    computePipeline = await graphicsDevice.CreatePipelineAsync(
                        computePipelineDescription,
                        CancellationToken.None).ConfigureAwait(false);
                }
            }

            if (useUniformBuffer)
            {
                uniformBufferBindingOffset = useUniformBufferSubrange
                    ? checked(graphicsDevice.Capabilities.Limits.UniformBufferOffsetAlignment * sizeof(float))
                    : 0;
                var frameUniformBytes = new byte[checked((int)(uniformBufferBindingOffset + 256))];
                var uniformDataOffset = checked((int)uniformBufferBindingOffset);

                if (useFrameLayoutUniformBuffer)
                {
                    _ = BitConverter.TryWriteBytes(
                        frameUniformBytes.AsSpan(uniformDataOffset + (sizeof(float) * 1), sizeof(float)),
                        1.0f);
                    _ = BitConverter.TryWriteBytes(
                        frameUniformBytes.AsSpan(uniformDataOffset + (sizeof(float) * 4), sizeof(float)),
                        1.0f);
                    _ = BitConverter.TryWriteBytes(
                        frameUniformBytes.AsSpan(uniformDataOffset + (sizeof(float) * 9), sizeof(float)),
                        1.0f);
                    _ = BitConverter.TryWriteBytes(
                        frameUniformBytes.AsSpan(uniformDataOffset + (sizeof(float) * 15), sizeof(float)),
                        1.0f);
                    _ = BitConverter.TryWriteBytes(
                        frameUniformBytes.AsSpan(uniformDataOffset + 64, sizeof(float)),
                        1.0f);
                    _ = BitConverter.TryWriteBytes(
                        frameUniformBytes.AsSpan(uniformDataOffset + 84, sizeof(float)),
                        1.0f);
                }
                else
                {
                    _ = BitConverter.TryWriteBytes(
                        frameUniformBytes.AsSpan(uniformDataOffset, sizeof(float)),
                        useSampledTexture ? 1.0f : 0.0f);
                    _ = BitConverter.TryWriteBytes(
                        frameUniformBytes.AsSpan(uniformDataOffset + sizeof(float), sizeof(float)),
                        1.0f);
                    _ = BitConverter.TryWriteBytes(
                        frameUniformBytes.AsSpan(uniformDataOffset + (sizeof(float) * 2), sizeof(float)),
                        useSampledTexture ? 1.0f : 0.0f);
                    _ = BitConverter.TryWriteBytes(
                        frameUniformBytes.AsSpan(uniformDataOffset + (sizeof(float) * 3), sizeof(float)),
                        1.0f);
                }

                frameUniformBuffer = await graphicsDevice.CreateBufferAsync(
                    new BufferDescription(
                        frameUniformBytes.Length,
                        BufferUsage.Uniform | BufferUsage.CopyDestination,
                        GpuMemoryKind.DeviceLocal,
                        structuredStride: null,
                        debugName: $"m04-{backend}-frame-uniform"))
                    .ConfigureAwait(false);
                var uploadReceipt = await graphicsDevice.UploadBufferAsync(
                    frameUniformBuffer,
                    0,
                    frameUniformBytes.AsSpan())
                    .ConfigureAwait(false);
                Assert.That(uploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));

                if (useMultipleFrameUniformBuffers)
                {
                    additionalFrameUniformBindingOffset = useUniformBufferSubrange
                        ? checked(uniformBufferBindingOffset * 2)
                        : 0;
                    var secondUniformBytes = new byte[checked(
                        (int)(additionalFrameUniformBindingOffset + 256))];

                    if (useUniformBufferSubrange)
                    {
                        Array.Fill(secondUniformBytes, (byte)0xA5);
                    }

                    var secondUniformDataOffset = checked((int)additionalFrameUniformBindingOffset);
                    _ = BitConverter.TryWriteBytes(
                        secondUniformBytes.AsSpan(secondUniformDataOffset, sizeof(float)),
                        1.0f);
                    _ = BitConverter.TryWriteBytes(
                        secondUniformBytes.AsSpan(secondUniformDataOffset + sizeof(float), sizeof(float)),
                        0.0f);
                    _ = BitConverter.TryWriteBytes(
                        secondUniformBytes.AsSpan(secondUniformDataOffset + (sizeof(float) * 2), sizeof(float)),
                        1.0f);
                    _ = BitConverter.TryWriteBytes(
                        secondUniformBytes.AsSpan(secondUniformDataOffset + (sizeof(float) * 3), sizeof(float)),
                        1.0f);
                    additionalFrameUniformBuffer = await graphicsDevice.CreateBufferAsync(
                        new BufferDescription(
                            secondUniformBytes.Length,
                            BufferUsage.Uniform | BufferUsage.CopyDestination,
                            GpuMemoryKind.DeviceLocal,
                            structuredStride: null,
                            debugName: $"m04-{backend}-frame-second-uniform"))
                        .ConfigureAwait(false);
                    var secondUniformUploadReceipt = await graphicsDevice.UploadBufferAsync(
                        additionalFrameUniformBuffer,
                        0,
                        secondUniformBytes.AsSpan())
                        .ConfigureAwait(false);
                    Assert.That(secondUniformUploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));
                }
            }

            if (useStorageResources
                || useCompute
                || useMultipleComputeStorageBuffers
                || useConstantWriteComputeBuffer
                || useStorageBufferSubrange)
            {
                if (useStorageBufferSubrange)
                {
                    storageBufferImportOffset = checked(
                        graphicsDevice.Capabilities.Limits.StorageBufferOffsetAlignment * sizeof(uint));
                    storageBufferBindingOffset = checked(storageBufferImportOffset * 2);

                    if (useMultipleComputeStorageBuffers)
                    {
                        secondStorageBufferImportOffset = checked(
                            storageBufferBindingOffset + graphicsDevice.Capabilities.Limits.StorageBufferOffsetAlignment);
                        secondStorageBufferBindingOffset = checked(
                            secondStorageBufferImportOffset + graphicsDevice.Capabilities.Limits.StorageBufferOffsetAlignment);
                    }
                }

                var storageBufferByteLength = checked(storageBufferBindingOffset + sizeof(uint));
                storageBuffer = await graphicsDevice.CreateBufferAsync(
                    new BufferDescription(
                        storageBufferByteLength,
                        BufferUsage.Storage | BufferUsage.CopySource | BufferUsage.CopyDestination,
                        GpuMemoryKind.DeviceLocal,
                        structuredStride: sizeof(uint),
                        debugName: $"m04-{backend}-frame-storage-buffer"))
                    .ConfigureAwait(false);
                var storageBufferInitialBytes = new byte[checked((int)storageBufferByteLength)];

                if (useStorageBufferSubrange)
                {
                    _ = BitConverter.TryWriteBytes(storageBufferInitialBytes.AsSpan(0, sizeof(uint)), 0x56473829u);
                    var storageBufferPrefixOffset = checked(storageBufferBindingOffset - sizeof(uint));
                    _ = BitConverter.TryWriteBytes(
                        storageBufferInitialBytes.AsSpan(checked((int)storageBufferPrefixOffset), sizeof(uint)),
                        0xA6B7C8D9u);
                }

                var storageBufferUploadReceipt = await graphicsDevice.UploadBufferAsync(
                    storageBuffer,
                    0,
                    storageBufferInitialBytes.AsSpan())
                    .ConfigureAwait(false);
                Assert.That(storageBufferUploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));
            }

            if (useMultipleComputeStorageBuffers)
            {
                var secondStorageBufferByteLength = useStorageBufferSubrange
                    ? checked(secondStorageBufferBindingOffset + sizeof(uint))
                    : sizeof(uint);
                secondStorageBuffer = await graphicsDevice.CreateBufferAsync(
                    new BufferDescription(
                        secondStorageBufferByteLength,
                        BufferUsage.Storage | BufferUsage.CopySource | BufferUsage.CopyDestination,
                        GpuMemoryKind.DeviceLocal,
                        structuredStride: sizeof(uint),
                        debugName: $"m04-{backend}-frame-second-storage-buffer"))
                    .ConfigureAwait(false);

                var secondStorageBufferInitialBytes = new byte[checked((int)secondStorageBufferByteLength)];

                if (useStorageBufferSubrange)
                {
                    _ = BitConverter.TryWriteBytes(secondStorageBufferInitialBytes.AsSpan(0, sizeof(uint)), 0x6B7C8D9Eu);
                    var secondStorageBufferPrefixOffset = checked(secondStorageBufferBindingOffset - sizeof(uint));
                    _ = BitConverter.TryWriteBytes(
                        secondStorageBufferInitialBytes.AsSpan(checked((int)secondStorageBufferPrefixOffset), sizeof(uint)),
                        0x8394A5B6u);
                }

                var secondStorageBufferUploadReceipt = await graphicsDevice.UploadBufferAsync(
                    secondStorageBuffer,
                    0,
                    secondStorageBufferInitialBytes.AsSpan())
                    .ConfigureAwait(false);
                Assert.That(secondStorageBufferUploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));
            }

            byte[]? storageImageBaseMipBytes = null;
            byte[]? additionalStorageImageBaseMipBytes = null;
            var storageImageBytesPerTexel = useRgba16FloatStorageImage ? sizeof(ushort) * 4 : sizeof(uint);
            var storageImageMipLevel = useStorageImageSubrange ? 1 : 0;

            if (useStorageResources || useCompute || useTransientStorage)
            {
                var storageImageWidth = useStorageImageSubrange ? 2 : 1;
                var storageImageDimension = (use3DStorageImage && !expectStorageImageDimensionRejection)
                    ? TextureDimension.Dimension3D
                    : TextureDimension.Dimension2D;
                var storageImageDepth = storageImageDimension == TextureDimension.Dimension3D ? 2 : 1;
                var storageImageMipLevels = useStorageImageSubrange ? 2 : 1;
                storageImage = await graphicsDevice.CreateTextureAsync(
                    new TextureDescription(
                        storageImageDimension,
                        storageImageWidth,
                        storageImageWidth,
                        depth: storageImageDepth,
                        arrayLayers: 1,
                        mipLevels: storageImageMipLevels,
                        useFloatStorageImage && !expectStorageImageFormatRejection
                            ? TextureFormat.R32Float
                            : useRgba16FloatStorageImage
                                ? TextureFormat.Rgba16Float
                                : TextureFormat.R32UInt,
                        TextureUsage.Storage | TextureUsage.CopySource | TextureUsage.CopyDestination,
                        $"m04-{backend}-frame-storage-image"))
                        .ConfigureAwait(false);

                if (useStorageImageSubrange)
                {
                    storageImageBaseMipBytes = new byte[checked(storageImageBytesPerTexel * 4)];
                    Array.Fill(storageImageBaseMipBytes, (byte)0xA5);
                    var storageImageBaseMipStorage = UnmanagedBuffer<byte>.Allocate(
                        storageImageBaseMipBytes.Length,
                        new BufferAllocationOptions(storageImageBaseMipBytes.Length, alignment: 1));
                    storageImageBaseMipStorage.CopyFrom(storageImageBaseMipBytes.AsSpan(), 0);

                    using var ownedStorageImageBaseMipBytes = storageImageBaseMipStorage.TransferToOwnedBuffer();
                    using var storageImageBaseMipLease = ownedStorageImageBaseMipBytes.AcquireReadLease(
                        0,
                        storageImageBaseMipBytes.Length);
                    var storageImageBaseMipUploadReceipt = await graphicsDevice.UploadTextureAsync(
                        storageImage,
                        new TextureSubresource(0, 0, TextureAspect.Color),
                        storageImageBaseMipLease,
                        0,
                        new TextureDataLayout(
                            rowPitch: checked(storageImageBytesPerTexel * 2),
                            slicePitch: checked(storageImageBytesPerTexel * 4)))
                        .ConfigureAwait(false);
                    Assert.That(storageImageBaseMipUploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));
                }

                var storageImageInitialBytes = new byte[checked(storageImageBytesPerTexel * storageImageDepth)];
                var storageImageInitialStorage = UnmanagedBuffer<byte>.Allocate(
                    storageImageInitialBytes.Length,
                    new BufferAllocationOptions(storageImageInitialBytes.Length, alignment: 1));
                storageImageInitialStorage.CopyFrom(storageImageInitialBytes.AsSpan(), 0);

                using var ownedStorageImageInitialBytes = storageImageInitialStorage.TransferToOwnedBuffer();
                using var storageImageInitialLease = ownedStorageImageInitialBytes.AcquireReadLease(0, storageImageInitialBytes.Length);
                var storageImageUploadReceipt = await graphicsDevice.UploadTextureAsync(
                    storageImage,
                    new TextureSubresource(storageImageMipLevel, 0, TextureAspect.Color),
                    storageImageInitialLease,
                    0,
                    new TextureDataLayout(
                        rowPitch: storageImageBytesPerTexel,
                        slicePitch: storageImageBytesPerTexel))
                    .ConfigureAwait(false);
                Assert.That(storageImageUploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));
            }

            if (useMultipleComputeStorageImages)
            {
                var additionalStorageImageWidth = useStorageImageSubrange ? 2 : 1;
                additionalStorageImage = await graphicsDevice.CreateTextureAsync(
                    new TextureDescription(
                        TextureDimension.Dimension2D,
                        additionalStorageImageWidth,
                        additionalStorageImageWidth,
                        depth: 1,
                        arrayLayers: 1,
                        mipLevels: useStorageImageSubrange ? 2 : 1,
                        TextureFormat.R32UInt,
                        TextureUsage.Storage | TextureUsage.CopySource | TextureUsage.CopyDestination,
                        $"m04-{backend}-frame-second-storage-image"))
                    .ConfigureAwait(false);

                if (useStorageImageSubrange)
                {
                    additionalStorageImageBaseMipBytes = new byte[checked(sizeof(uint) * 4)];
                    Array.Fill(additionalStorageImageBaseMipBytes, (byte)0x5A);
                    var additionalStorageImageBaseMipStorage = UnmanagedBuffer<byte>.Allocate(
                        additionalStorageImageBaseMipBytes.Length,
                        new BufferAllocationOptions(additionalStorageImageBaseMipBytes.Length, alignment: 1));
                    additionalStorageImageBaseMipStorage.CopyFrom(additionalStorageImageBaseMipBytes.AsSpan(), 0);

                    using var ownedAdditionalStorageImageBaseMipBytes =
                        additionalStorageImageBaseMipStorage.TransferToOwnedBuffer();
                    using var additionalStorageImageBaseMipLease = ownedAdditionalStorageImageBaseMipBytes.AcquireReadLease(
                        0,
                        additionalStorageImageBaseMipBytes.Length);
                    var additionalStorageImageBaseMipUploadReceipt = await graphicsDevice.UploadTextureAsync(
                        additionalStorageImage,
                        new TextureSubresource(0, 0, TextureAspect.Color),
                        additionalStorageImageBaseMipLease,
                        0,
                        new TextureDataLayout(rowPitch: sizeof(uint) * 2, slicePitch: sizeof(uint) * 4))
                        .ConfigureAwait(false);
                    Assert.That(additionalStorageImageBaseMipUploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));
                }

                var additionalStorageImageInitialBytes = new byte[sizeof(uint)];
                var additionalStorageImageInitialStorage = UnmanagedBuffer<byte>.Allocate(
                    additionalStorageImageInitialBytes.Length,
                    new BufferAllocationOptions(additionalStorageImageInitialBytes.Length, alignment: 1));
                additionalStorageImageInitialStorage.CopyFrom(additionalStorageImageInitialBytes.AsSpan(), 0);

                using var ownedAdditionalStorageImageInitialBytes = additionalStorageImageInitialStorage.TransferToOwnedBuffer();
                using var additionalStorageImageInitialLease = ownedAdditionalStorageImageInitialBytes.AcquireReadLease(
                    0,
                    additionalStorageImageInitialBytes.Length);
                var additionalStorageImageUploadReceipt = await graphicsDevice.UploadTextureAsync(
                    additionalStorageImage,
                    new TextureSubresource(useStorageImageSubrange ? 1 : 0, 0, TextureAspect.Color),
                    additionalStorageImageInitialLease,
                    0,
                    new TextureDataLayout(rowPitch: sizeof(uint), slicePitch: sizeof(uint)))
                    .ConfigureAwait(false);
                Assert.That(additionalStorageImageUploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));
            }

            if (useBufferCopy
                || useCompute
                || useTransientStorage
                || useMultipleComputeStorageBuffers
                || useTransientBufferAliasing
                || useConstantWriteComputeBuffer)
            {
                expectedCopiedBytes = useMultipleComputeStorageImages
                    ? [0x40, 0x30, 0x20, 0x10]
                    : useConstantWriteComputeBuffer
                    ? [0x40, 0x30, 0x20, 0x10]
                    : useFloatStorageImage || useSignedStorageImage || useRgba16FloatStorageImage
                        ? [0x00, 0x00, 0x00, 0x00]
                        : useCompute || useTransientStorage || useMultipleComputeStorageBuffers
                            ? [0x02, 0x00, 0x00, 0x00]
                            : [
                        0x11,
                        0x23,
                        0x47,
                        0x8B,
                        0xD1,
                        0x2D,
                        0x5B,
                        0xA7,
                        0x13,
                        0x29,
                        0x4F,
                        0x97,
                        0xE3,
                        0x35,
                        0x6B,
                        0xB9,
                    ];
                expectedSecondCopiedBytes = useMultipleComputeStorageBuffers
                    ? [0x04, 0x00, 0x00, 0x00]
                    : useTransientBufferAliasing
                        ? [
                            0x91,
                            0x37,
                            0xE2,
                            0x4B,
                            0x18,
                            0xC5,
                            0x6A,
                            0xD3,
                            0x2F,
                            0x84,
                            0x59,
                            0xB6,
                            0x0D,
                            0xFA,
                            0x73,
                            0x21,
                        ]
                        : null;

                if (useBufferCopy || useTransientStorage || useTransientBufferAliasing)
                {
                    try
                    {
                        copySourceBuffer = await graphicsDevice.CreateBufferAsync(
                            new BufferDescription(
                                expectedCopiedBytes.Length,
                                useUploadHeapGraphSources
                                    ? BufferUsage.CopySource
                                    : BufferUsage.CopySource | BufferUsage.CopyDestination,
                                useUploadHeapGraphSources ? GpuMemoryKind.Upload : GpuMemoryKind.DeviceLocal,
                                structuredStride: null,
                                debugName: $"m04-{backend}-copy-source"))
                            .ConfigureAwait(false);
                    }
                    catch (OutOfMemoryException exception) when (
                        (backend == GraphicsBackend.D3D12)
                        && useUploadHeapGraphSources
                        && exception.Message.Contains("budget", StringComparison.OrdinalIgnoreCase))
                    {
                        Assert.Ignore($"D3D12 host-written graph sources require available upload-heap budget: {exception.Message}");
                        throw;
                    }
                }

                var copyDestinationByteLength = expectedCopiedBytes.Length;

                if (useStorageBufferSubrange)
                {
                    copyDestinationRangeOffset = storageBufferBindingOffset;
                    copyDestinationByteLength = checked(
                        (int)(copyDestinationRangeOffset + expectedCopiedBytes.Length));
                }

                copyDestinationBuffer = await graphicsDevice.CreateBufferAsync(
                    new BufferDescription(
                        copyDestinationByteLength,
                        BufferUsage.CopySource | BufferUsage.CopyDestination,
                        GpuMemoryKind.DeviceLocal,
                        structuredStride: null,
                        debugName: $"m04-{backend}-copy-destination"))
                    .ConfigureAwait(false);

                if (useManyBufferCopyDestinations)
                {
                    for (var index = 0; index < additionalCopyDestinationBuffers.Length; index++)
                    {
                        additionalCopyDestinationBuffers[index] = await graphicsDevice.CreateBufferAsync(
                            new BufferDescription(
                                copyDestinationByteLength,
                                BufferUsage.CopySource | BufferUsage.CopyDestination,
                                GpuMemoryKind.DeviceLocal,
                                structuredStride: null,
                                debugName: $"m04-{backend}-copy-destination-{index + 1}"))
                            .ConfigureAwait(false);
                    }
                }

                if (useStorageBufferSubrange)
                {
                    var initialBytes = new byte[copyDestinationByteLength];
                    _ = BitConverter.TryWriteBytes(initialBytes.AsSpan(0, sizeof(uint)), 0x29384756u);
                    var copyDestinationPrefixOffset = checked(copyDestinationRangeOffset - sizeof(uint));
                    _ = BitConverter.TryWriteBytes(
                        initialBytes.AsSpan(checked((int)copyDestinationPrefixOffset), sizeof(uint)),
                        0xD9C8B7A6u);
                    var uploadReceipt = await graphicsDevice.UploadBufferAsync(
                        copyDestinationBuffer,
                        0,
                        initialBytes.AsSpan())
                        .ConfigureAwait(false);
                    Assert.That(uploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));
                }

                if (useMultipleComputeStorageBuffers || useTransientBufferAliasing)
                {
                    secondCopyDestinationBuffer = await graphicsDevice.CreateBufferAsync(
                        new BufferDescription(
                            expectedSecondCopiedBytes?.Length
                                ?? throw new InvalidOperationException("The graph multi-buffer compute test has no second expected output."),
                            BufferUsage.CopySource | BufferUsage.CopyDestination,
                            GpuMemoryKind.DeviceLocal,
                            structuredStride: null,
                            debugName: $"m04-{backend}-second-copy-destination"))
                        .ConfigureAwait(false);
                }

                if (useTransientBufferAliasing)
                {
                    var secondSourceBytes = expectedSecondCopiedBytes
                        ?? throw new InvalidOperationException("The graph transient alias test has no second source data.");

                    try
                    {
                        secondCopySourceBuffer = await graphicsDevice.CreateBufferAsync(
                            new BufferDescription(
                                secondSourceBytes.Length,
                                useUploadHeapGraphSources
                                    ? BufferUsage.CopySource
                                    : BufferUsage.CopySource | BufferUsage.CopyDestination,
                                useUploadHeapGraphSources ? GpuMemoryKind.Upload : GpuMemoryKind.DeviceLocal,
                                structuredStride: null,
                                debugName: $"m04-{backend}-second-copy-source"))
                            .ConfigureAwait(false);
                    }
                    catch (OutOfMemoryException exception) when (
                        (backend == GraphicsBackend.D3D12)
                        && useUploadHeapGraphSources
                        && exception.Message.Contains("budget", StringComparison.OrdinalIgnoreCase))
                    {
                        Assert.Ignore($"D3D12 host-written graph sources require available upload-heap budget: {exception.Message}");
                        throw;
                    }

                    if (useUploadHeapGraphSources)
                    {
                        await graphicsDevice.WriteUploadBufferAsync(
                            secondCopySourceBuffer,
                            0,
                            secondSourceBytes.AsSpan()).ConfigureAwait(false);
                    }
                    else
                    {
                        var secondSourceUploadReceipt = await graphicsDevice.UploadBufferAsync(
                            secondCopySourceBuffer,
                            0,
                            secondSourceBytes.AsSpan())
                            .ConfigureAwait(false);
                        Assert.That(secondSourceUploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));
                    }
                }

                if (copySourceBuffer is GpuBuffer copyUploadSourceBuffer)
                {
                    var sourceBytes = useTransientStorage
                        ? new byte[expectedCopiedBytes.Length]
                        : expectedCopiedBytes;

                    if (useUploadHeapGraphSources)
                    {
                        await graphicsDevice.WriteUploadBufferAsync(
                            copyUploadSourceBuffer,
                            0,
                            sourceBytes.AsSpan()).ConfigureAwait(false);
                    }
                    else
                    {
                        var copyUploadReceipt = await graphicsDevice.UploadBufferAsync(
                            copyUploadSourceBuffer,
                            0,
                            sourceBytes.AsSpan())
                            .ConfigureAwait(false);
                        Assert.That(copyUploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));
                    }
                }
            }

            var graph = renderer.BuildGraph(
                renderSnapshot,
                target);

            if (useAttachmentScopedRenderArea)
            {
                const int attachmentExtent = 64;
                var renderArea = new RenderArea(0, 0, attachmentExtent, attachmentExtent);
                var invalidGraph = renderer.BuildGraph(renderSnapshot, target);
                var invalidOutputPass = invalidGraph.AddRasterPass(new RenderPassDescription(
                    "m05-render-area-too-large-for-attached-output",
                    int.MinValue,
                    [],
                    hasExternalSideEffects: false,
                    renderArea: renderArea));
                _ = Assert.Throws<ArgumentException>(() => invalidGraph.WriteColorAttachment(
                    invalidOutputPass,
                    invalidGraph.OffscreenColorTargetVersion,
                    new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color),
                    AttachmentLoadAction.Clear,
                    AttachmentStoreAction.Store,
                    Vector4.Zero));

                var selectedAttachment = oversizedAttachment
                    ?? throw new InvalidOperationException("The attachment-scoped render-area test has no larger attachment.");
                var attachmentRange = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);
                var attachmentInitialVersion = graph.ImportTexture(
                    selectedAttachment,
                    attachmentRange,
                    new GraphImportOptions(
                        GraphResourceState.Undefined,
                        contentsValid: false,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                var selectedDepthAttachment = oversizedDepthAttachment
                    ?? throw new InvalidOperationException("The attachment-scoped render-area test has no larger depth attachment.");
                var depthRange = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Depth);
                var depthInitialVersion = graph.ImportTexture(
                    selectedDepthAttachment,
                    depthRange,
                    new GraphImportOptions(
                        GraphResourceState.Undefined,
                        contentsValid: false,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                var depthInitializationArea = new RenderArea(0, 0, 128, attachmentExtent);
                var depthInitializationPass = graph.AddRasterPass(new RenderPassDescription(
                    "m05-depth-attachment-initialization",
                    int.MinValue,
                    [],
                    hasExternalSideEffects: true,
                    renderArea: depthInitializationArea));
                var depthInitializedVersion = graph.WriteDepthAttachment(
                    depthInitializationPass,
                    depthInitialVersion,
                    depthRange,
                    AttachmentLoadAction.Clear,
                    AttachmentStoreAction.Store,
                    clearDepth: 0.0f);
                var depthPass = graph.AddRasterPass(new RenderPassDescription(
                    "m05-attachment-scoped-color-and-depth-render-area",
                    int.MinValue + 1,
                    [depthInitializationPass],
                    hasExternalSideEffects: true,
                    renderArea: renderArea));
                _ = graph.WriteColorAttachment(
                    depthPass,
                    attachmentInitialVersion,
                    attachmentRange,
                    AttachmentLoadAction.Clear,
                    AttachmentStoreAction.Store,
                    new Vector4(0.0f, 1.0f, 1.0f, 1.0f));
                _ = graph.WriteDepthAttachment(
                    depthPass,
                    depthInitializedVersion,
                    depthRange,
                    AttachmentLoadAction.Load,
                    AttachmentStoreAction.Store,
                    clearDepth: 0.0f);
            }

            var sampleRange = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);
            var sampledTextureFirstLayer = useArrayedSampledTexture ? 1 : 0;
            var sampledTextureRange = new GraphTextureRange(0, 1, sampledTextureFirstLayer, 1, sampledTextureAspect);
            var sampledTextureImportRange = useSampledTextureSubrange
                ? new GraphTextureRange(0, 2, sampledTextureFirstLayer, 1, sampledTextureAspect)
                : sampledTextureRange;
            var sampledTextureBindingRange = useSampledTextureSubrange
                ? new GraphTextureRange(1, 1, sampledTextureFirstLayer, 1, sampledTextureAspect)
                : sampledTextureRange;
            var storageImageImportRange = useStorageImageSubrange
                ? new GraphTextureRange(0, 2, 0, 1, TextureAspect.Color)
                : sampleRange;
            var storageImageBindingRange = useStorageImageSubrange
                ? new GraphTextureRange(1, 1, 0, 1, TextureAspect.Color)
                : sampleRange;
            var additionalStorageImageImportRange = useStorageImageSubrange
                ? new GraphTextureRange(0, 2, 0, 1, TextureAspect.Color)
                : sampleRange;
            var additionalStorageImageBindingRange = useStorageImageSubrange
                ? new GraphTextureRange(1, 1, 0, 1, TextureAspect.Color)
                : sampleRange;
            var copyTextureRange = (
                useTransientTextureCopy
                || useTransientTextureAliasing
                || usePartialTextureCopy
                || useTransientCopyAliasing
                || useColorAttachmentCopy
                || useColorAttachmentCopyDestination)
                ? new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color)
                : new GraphTextureRange(1, 1, 1, 1, TextureAspect.Color);
            GraphBufferVersion? frameUniformVersion = null;
            GraphBufferVersion? additionalFrameUniformVersion = null;
            GraphBufferRange? frameUniformBindingRange = null;
            GraphBufferRange? additionalFrameUniformBindingRange = null;
            GraphBufferVersion? storageBufferInitialVersion = null;
            GraphBufferVersion? secondStorageBufferInitialVersion = null;
            GraphBufferVersion? storageBufferWriteVersion = null;
            GraphBufferVersion? computeStorageWriteVersion = null;
            GraphBufferVersion? secondComputeStorageWriteVersion = null;
            GraphBufferVersion? copySourceVersion = null;
            GraphBufferRange? copySourceBufferRange = null;
            GraphBufferVersion? secondCopySourceVersion = null;
            GraphBufferRange? secondCopySourceBufferRange = null;
            GraphBufferVersion? copyDestinationInitialVersion = null;
            GraphBufferVersion? copyDestinationWriteVersion = null;
            var additionalCopyDestinationInitialVersions =
                new GraphBufferVersion?[additionalCopyDestinationBuffers.Length];
            var additionalCopyDestinationWriteVersions =
                new GraphBufferVersion?[additionalCopyDestinationBuffers.Length];
            GraphBufferVersion? secondCopyDestinationInitialVersion = null;
            GraphBufferVersion? secondCopyDestinationWriteVersion = null;
            GraphBufferVersion? transientBufferCopyWriteVersion = null;
            GraphBufferVersion? transientSecondBufferCopyWriteVersion = null;
            GraphBufferVersion? transientInitializationSourceVersion = null;
            GraphBufferVersion? transientSecondInitializationSourceVersion = null;
            GraphTextureVersion? sampledTextureVersion = null;
            var sixteenSampledTextureVersions = new GraphTextureVersion?[sixteenSampledTextures.Length];
            var secondaryColorAttachmentInitialVersions = new GraphTextureVersion?[secondaryColorAttachmentTextures.Length];
            var secondaryColorAttachmentWriteVersions = new GraphTextureVersion?[secondaryColorAttachmentTextures.Length];
            GraphTextureVersion? depthAttachmentInitialVersion = null;
            GraphTextureVersion? copySourceTextureVersion = null;
            GraphTextureVersion? copyDestinationTextureInitialVersion = null;
            GraphTextureVersion? copyDestinationTextureWriteVersion = null;
            GraphTextureVersion? colorAttachmentCopyDestinationVersion = null;
            GraphTextureVersion? secondCopySourceTextureVersion = null;
            GraphTextureVersion? secondCopyDestinationTextureInitialVersion = null;
            GraphTextureVersion? secondCopyDestinationTextureWriteVersion = null;
            GraphTextureVersion? transientTextureInitialVersion = null;
            GraphTextureVersion? transientTextureWriteVersion = null;
            GraphTextureVersion? secondTransientTextureInitialVersion = null;
            GraphTextureVersion? secondTransientTextureWriteVersion = null;
            GraphTextureVersion? transientSlotSourceVersion = null;
            GraphTextureVersion? transientSlotA = null;
            GraphTextureVersion? transientSlotB = null;
            GraphTextureVersion? transientSlotDestinationA = null;
            GraphTextureVersion? transientSlotDestinationB = null;
            GraphTextureVersion? firstTransientTextureDestinationVersion = null;
            GraphTextureVersion? storageImageInitialVersion = null;
            GraphTextureVersion? storageImageWriteVersion = null;
            GraphTextureVersion? computeStorageImageWriteVersion = null;
            GraphTextureVersion? additionalStorageImageInitialVersion = null;
            GraphTextureVersion? additionalComputeStorageImageWriteVersion = null;
            BindingSet? frameBindingSet = null;
            BindingSet? computeBindingSet = null;
            var bufferBindings = ImmutableArray.CreateBuilder<BufferBinding>(
                (useUniformBuffer ? 1 : 0) + (useMultipleFrameUniformBuffers ? 1 : 0));
            var storageBufferBindings = ImmutableArray.CreateBuilder<BufferBinding>(useStorageResources ? 1 : 0);
            var textureBindings = ImmutableArray.CreateBuilder<TextureBinding>(
                (useSampledTexture ? 1 : 0)
                    + (useMultipleSampledImages ? 1 : 0)
                    + (useStorageResources ? 1 : 0)
                    + (useSixteenSampledImages ? sixteenSampledTextures.Length : 0));
            var samplerBindings = ImmutableArray.CreateBuilder<SamplerBinding>(
                (useSampledTexture ? 1 : 0)
                    + (useMultipleSampledImages ? 1 : 0)
                    + (useSixteenSampledImages ? 7 : 0));

            if (frameUniformBuffer is GpuBuffer selectedBuffer)
            {
                var importRange = new GraphBufferRange(0, selectedBuffer.ByteLength);
                frameUniformVersion = graph.ImportBuffer(
                    selectedBuffer,
                    importRange,
                    new GraphImportOptions(
                        GraphResourceState.General,
                        contentsValid: true,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                var bindingRange = useUniformBufferSubrange
                    ? new GraphBufferRange(
                        uniformBufferBindingOffset,
                        selectedBuffer.ByteLength - uniformBufferBindingOffset)
                    : importRange;
                frameUniformBindingRange = bindingRange;
                bufferBindings.Add(new BufferBinding(
                    ShaderBindingGroup.Frame,
                    frameUniformBindingIndex,
                    ShaderResourceKind.UniformBuffer,
                    frameUniformVersion,
                    bindingRange));
            }

            if (additionalFrameUniformBuffer is GpuBuffer selectedAdditionalUniformBuffer)
            {
                var importRange = new GraphBufferRange(0, selectedAdditionalUniformBuffer.ByteLength);
                additionalFrameUniformVersion = graph.ImportBuffer(
                    selectedAdditionalUniformBuffer,
                    importRange,
                    new GraphImportOptions(
                        GraphResourceState.General,
                        contentsValid: true,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                var bindingRange = useUniformBufferSubrange
                    ? new GraphBufferRange(additionalFrameUniformBindingOffset, 256)
                    : importRange;
                additionalFrameUniformBindingRange = bindingRange;
                bufferBindings.Add(new BufferBinding(
                    ShaderBindingGroup.Frame,
                    1,
                    ShaderResourceKind.UniformBuffer,
                    additionalFrameUniformVersion,
                    bindingRange));
            }

            if (storageBuffer is GpuBuffer selectedStorageBuffer)
            {
                var range = useStorageBufferSubrange
                    ? new GraphBufferRange(
                        storageBufferImportOffset,
                        checked(storageBufferBindingOffset - storageBufferImportOffset + sizeof(uint)))
                    : new GraphBufferRange(0, selectedStorageBuffer.ByteLength);
                storageBufferInitialVersion = graph.ImportBuffer(
                    selectedStorageBuffer,
                    range,
                    new GraphImportOptions(
                        GraphResourceState.General,
                        contentsValid: true,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
            }

            for (var index = 0; index < secondaryColorAttachmentTextures.Length; index++)
            {
                if (secondaryColorAttachmentTextures[index] is GpuTexture selectedSecondaryColorAttachmentTexture)
                {
                    secondaryColorAttachmentInitialVersions[index] = graph.ImportTexture(
                        selectedSecondaryColorAttachmentTexture,
                        new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color),
                        new GraphImportOptions(
                            GraphResourceState.Undefined,
                            contentsValid: false,
                            completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                }
            }

            if (additionalStorageImage is GpuTexture selectedAdditionalStorageImage)
            {
                additionalStorageImageInitialVersion = graph.ImportTexture(
                    selectedAdditionalStorageImage,
                    additionalStorageImageImportRange,
                    new GraphImportOptions(
                        GraphResourceState.General,
                        contentsValid: true,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
            }

            if (secondStorageBuffer is GpuBuffer selectedSecondStorageBuffer)
            {
                var range = useStorageBufferSubrange
                    ? new GraphBufferRange(
                        secondStorageBufferImportOffset,
                        checked(secondStorageBufferBindingOffset - secondStorageBufferImportOffset + sizeof(uint)))
                    : new GraphBufferRange(0, selectedSecondStorageBuffer.ByteLength);
                secondStorageBufferInitialVersion = graph.ImportBuffer(
                    selectedSecondStorageBuffer,
                    range,
                    new GraphImportOptions(
                        GraphResourceState.General,
                        contentsValid: true,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
            }

            if (copySourceBuffer is GpuBuffer selectedCopySourceBuffer)
            {
                var range = new GraphBufferRange(0, selectedCopySourceBuffer.ByteLength);
                copySourceVersion = graph.ImportBuffer(
                    selectedCopySourceBuffer,
                    range,
                    new GraphImportOptions(
                        GraphResourceState.General,
                        contentsValid: true,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                transientInitializationSourceVersion = copySourceVersion;
            }

            if (secondCopySourceBuffer is GpuBuffer selectedSecondCopySourceBuffer)
            {
                var range = new GraphBufferRange(0, selectedSecondCopySourceBuffer.ByteLength);
                secondCopySourceVersion = graph.ImportBuffer(
                    selectedSecondCopySourceBuffer,
                    range,
                    new GraphImportOptions(
                        GraphResourceState.General,
                        contentsValid: true,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                transientSecondInitializationSourceVersion = secondCopySourceVersion;
            }

            if (copyDestinationBuffer is GpuBuffer selectedCopyDestinationBuffer)
            {
                var copiedByteLength = expectedCopiedBytes?.Length
                    ?? throw new InvalidOperationException("The graph copy destination has no expected bytes.");
                var range = useStorageBufferSubrange
                    ? new GraphBufferRange(copyDestinationRangeOffset, copiedByteLength)
                    : new GraphBufferRange(0, selectedCopyDestinationBuffer.ByteLength);
                copyDestinationInitialVersion = graph.ImportBuffer(
                    selectedCopyDestinationBuffer,
                    range,
                    new GraphImportOptions(
                        useStorageBufferSubrange ? GraphResourceState.General : GraphResourceState.Undefined,
                        contentsValid: useStorageBufferSubrange,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
            }

            for (var index = 0; index < additionalCopyDestinationBuffers.Length; index++)
            {
                if (additionalCopyDestinationBuffers[index] is GpuBuffer additionalCopyDestinationBuffer)
                {
                    var range = new GraphBufferRange(0, additionalCopyDestinationBuffer.ByteLength);
                    additionalCopyDestinationInitialVersions[index] = graph.ImportBuffer(
                        additionalCopyDestinationBuffer,
                        range,
                        new GraphImportOptions(
                            GraphResourceState.Undefined,
                            contentsValid: false,
                            completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                }
            }

            if (secondCopyDestinationBuffer is GpuBuffer selectedSecondCopyDestinationBuffer)
            {
                var range = new GraphBufferRange(0, selectedSecondCopyDestinationBuffer.ByteLength);
                secondCopyDestinationInitialVersion = graph.ImportBuffer(
                    selectedSecondCopyDestinationBuffer,
                    range,
                    new GraphImportOptions(
                        GraphResourceState.Undefined,
                        contentsValid: false,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
            }

            if (depthAttachmentTexture is GpuTexture selectedDepthAttachmentTexture)
            {
                depthAttachmentInitialVersion = graph.ImportTexture(
                    selectedDepthAttachmentTexture,
                    new GraphTextureRange(0, 1, 0, 1, TextureAspect.Depth),
                    new GraphImportOptions(
                        GraphResourceState.Undefined,
                        contentsValid: false,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
            }

            if (sampledTexture is GpuTexture selectedSampledTexture)
            {
                sampledTextureVersion = graph.ImportTexture(
                    selectedSampledTexture,
                    sampledTextureImportRange,
                    new GraphImportOptions(
                        GraphResourceState.General,
                        contentsValid: true,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                textureBindings.Add(new TextureBinding(
                    ShaderBindingGroup.Frame,
                    sampledImageBindingIndex,
                    ShaderResourceKind.SampledImage,
                    sampledTextureVersion,
                    sampledTextureBindingRange));
                samplerBindings.Add(new SamplerBinding(
                    ShaderBindingGroup.Frame,
                    sampledImageBindingIndex,
                    frameSampler
                        ?? throw new InvalidOperationException("The sampled Frame pipeline has no sampler.")));
            }

            GraphTextureVersion? additionalSampledTextureVersion = null;

            if (additionalSampledTexture is GpuTexture selectedAdditionalSampledTexture)
            {
                additionalSampledTextureVersion = graph.ImportTexture(
                    selectedAdditionalSampledTexture,
                    sampledTextureImportRange,
                    new GraphImportOptions(
                        GraphResourceState.General,
                        contentsValid: true,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                textureBindings.Add(new TextureBinding(
                    ShaderBindingGroup.Frame,
                    1,
                    ShaderResourceKind.SampledImage,
                    additionalSampledTextureVersion,
                    sampledTextureBindingRange));
                samplerBindings.Add(new SamplerBinding(
                    ShaderBindingGroup.Frame,
                    1,
                    frameSampler
                        ?? throw new InvalidOperationException("The sampled Frame pipeline has no sampler.")));
            }

            for (var index = 0; index < sixteenSampledTextures.Length; index++)
            {
                if (sixteenSampledTextures[index] is not GpuTexture selectedTexture)
                {
                    continue;
                }

                var imageIndex = index + 1;
                var manySampledTextureVersion = graph.ImportTexture(
                    selectedTexture,
                    sampledTextureRange,
                    new GraphImportOptions(
                        GraphResourceState.General,
                        contentsValid: true,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                sixteenSampledTextureVersions[index] = manySampledTextureVersion;
                textureBindings.Add(new TextureBinding(
                    ShaderBindingGroup.Frame,
                    imageIndex,
                    ShaderResourceKind.SampledImage,
                    manySampledTextureVersion,
                    sampledTextureRange));

                if ((imageIndex & 1) == 0)
                {
                    samplerBindings.Add(new SamplerBinding(
                        ShaderBindingGroup.Frame,
                        imageIndex / 2,
                        frameSampler
                            ?? throw new InvalidOperationException("The sixteen-image Frame pipeline has no sampler.")));
                }
            }

            if (copySourceTexture is GpuTexture selectedCopySourceTexture)
            {
                copySourceTextureVersion = graph.ImportTexture(
                    selectedCopySourceTexture,
                    copyTextureRange,
                    new GraphImportOptions(
                        GraphResourceState.General,
                        contentsValid: true,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
            }

            if (copyDestinationTexture is GpuTexture selectedCopyDestinationTexture)
            {
                copyDestinationTextureInitialVersion = graph.ImportTexture(
                    selectedCopyDestinationTexture,
                    copyTextureRange,
                    new GraphImportOptions(
                        GraphResourceState.Undefined,
                        contentsValid: false,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
            }

            if (secondCopySourceTexture is GpuTexture selectedSecondCopySourceTexture)
            {
                secondCopySourceTextureVersion = graph.ImportTexture(
                    selectedSecondCopySourceTexture,
                    copyTextureRange,
                    new GraphImportOptions(
                        GraphResourceState.General,
                        contentsValid: true,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
            }

            if (secondCopyDestinationTexture is GpuTexture selectedSecondCopyDestinationTexture)
            {
                secondCopyDestinationTextureInitialVersion = graph.ImportTexture(
                    selectedSecondCopyDestinationTexture,
                    copyTextureRange,
                    new GraphImportOptions(
                        GraphResourceState.Undefined,
                        contentsValid: false,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
            }

            if (storageImage is GpuTexture selectedStorageImage)
            {
                storageImageInitialVersion = graph.ImportTexture(
                    selectedStorageImage,
                    storageImageImportRange,
                    new GraphImportOptions(
                        GraphResourceState.General,
                        contentsValid: true,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
            }

            GraphBufferVersion? secondFrameUniformVersion = null;
            GraphBufferVersion? secondAdditionalFrameUniformVersion = null;
            GraphTextureVersion? secondSampledTextureVersion = null;
            GraphTextureVersion? secondAdditionalSampledTextureVersion = null;
            BindingSet? secondFrameBindingSet = null;

            if (useTransientTextureCopy || useTransientTextureAliasing)
            {
                transientTextureInitialVersion = graph.CreateTransientTexture(
                    textureCopyDescription
                        ?? throw new InvalidOperationException("The graph transient texture-copy test has no texture description."));

                if (useTransientTextureAliasing)
                {
                    secondTransientTextureInitialVersion = graph.CreateTransientTexture(
                        textureCopyDescription
                            ?? throw new InvalidOperationException("The graph transient texture-alias test has no texture description."));
                }
            }

            void RecordClear(scoped ref CommandRecorder recorder)
            {
                if (pipeline is GpuPipeline selectedPipeline)
                {
                    recorder.SetPipeline(selectedPipeline.Handle);

                    if (frameBindingSet is BindingSet selectedBindings)
                    {
                        recorder.SetBindings(selectedBindings);
                    }

                    recorder.Draw(new DrawArguments(3, useDepthAttachment ? 2 : 1, 0, 0));

                    if (useStorageResources)
                    {
                        recorder.Draw(new DrawArguments(3, useDepthAttachment ? 2 : 1, 0, 0));
                    }
                }
            }

            void RecordSecondClear(scoped ref CommandRecorder recorder)
            {
                if (pipeline is GpuPipeline selectedPipeline)
                {
                    recorder.SetPipeline(selectedPipeline.Handle);

                    if (secondFrameBindingSet is BindingSet selectedBindings)
                    {
                        recorder.SetBindings(selectedBindings);
                    }

                    recorder.Draw(new DrawArguments(3, 1, 0, 0));

                    if (useStorageResources)
                    {
                        recorder.Draw(new DrawArguments(3, 1, 0, 0));
                    }
                }
            }

            void RecordEmptyPass(scoped ref CommandRecorder recorder)
            {
                _ = recorder;
            }

            void RecordCopy(scoped ref CommandRecorder recorder)
            {
                if ((copySourceVersion is GraphBufferVersion selectedSource)
                    && (copyDestinationWriteVersion is GraphBufferVersion selectedDestination))
                {
                    var sourceRange = copySourceBufferRange ?? selectedSource.Range;
                    recorder.CopyBuffer(
                        selectedSource,
                        sourceRange.ByteOffset,
                        selectedDestination,
                        selectedDestination.Range.ByteOffset,
                        sourceRange.ByteLength);
                }

                if (useManyBufferCopyDestinations
                    && (copySourceVersion is GraphBufferVersion manyDestinationSource))
                {
                    var sourceRange = copySourceBufferRange ?? manyDestinationSource.Range;

                    foreach (var destinationVersion in additionalCopyDestinationWriteVersions)
                    {
                        if (destinationVersion is GraphBufferVersion additionalDestinationVersion)
                        {
                            recorder.CopyBuffer(
                                manyDestinationSource,
                                sourceRange.ByteOffset,
                                additionalDestinationVersion,
                                additionalDestinationVersion.Range.ByteOffset,
                                sourceRange.ByteLength);
                        }
                    }
                }

                if (useMultipleComputeStorageBuffers
                    && (secondCopySourceVersion is GraphBufferVersion selectedSecondSource)
                    && (secondCopyDestinationWriteVersion is GraphBufferVersion selectedSecondDestination))
                {
                    var sourceRange = secondCopySourceBufferRange ?? selectedSecondSource.Range;
                    recorder.CopyBuffer(
                        selectedSecondSource,
                        sourceRange.ByteOffset,
                        selectedSecondDestination,
                        selectedSecondDestination.Range.ByteOffset,
                        sourceRange.ByteLength);
                }

                if ((useTransientTextureCopy || useTransientTextureAliasing)
                    && (copySourceTextureVersion is GraphTextureVersion selectedTextureSource)
                    && (transientTextureWriteVersion is GraphTextureVersion selectedTransientTexture))
                {
                    recorder.CopyTexture(selectedTextureSource, selectedTransientTexture);
                }
                else if ((copySourceTextureVersion is GraphTextureVersion directTextureSource)
                    && (copyDestinationTextureWriteVersion is GraphTextureVersion directTextureDestination))
                {
                    if (usePartialTextureCopy)
                    {
                        recorder.CopyTexture(
                            directTextureSource,
                            directTextureDestination,
                            new TextureCopyRegion(
                                sourceX: 1,
                                sourceY: 1,
                                width: 2,
                                height: 2));
                    }
                    else
                    {
                        recorder.CopyTexture(directTextureSource, directTextureDestination);
                    }
                }

                if (useMultipleTextureCopies
                    && (secondCopySourceTextureVersion is GraphTextureVersion selectedSecondTextureSource)
                    && (secondCopyDestinationTextureWriteVersion is GraphTextureVersion selectedSecondTextureDestination))
                {
                    recorder.CopyTexture(selectedSecondTextureSource, selectedSecondTextureDestination);
                }
            }

            void RecordColorAttachmentCopy(scoped ref CommandRecorder recorder)
            {
                var source = secondaryColorAttachmentWriteVersions[0]
                    ?? throw new InvalidOperationException("The graph color-attachment copy has no source version.");
                var destination = colorAttachmentCopyDestinationVersion
                    ?? throw new InvalidOperationException("The graph color-attachment copy has no destination version.");
                recorder.CopyTexture(source, destination);
            }

            void RecordTransientTextureCopy(scoped ref CommandRecorder recorder)
            {
                if ((transientTextureWriteVersion is GraphTextureVersion selectedTransientTexture)
                    && (firstTransientTextureDestinationVersion is GraphTextureVersion selectedTextureDestination))
                {
                    recorder.CopyTexture(selectedTransientTexture, selectedTextureDestination);
                }
            }

            void RecordSecondTransientTextureInitialize(scoped ref CommandRecorder recorder)
            {
                if ((copySourceTextureVersion is GraphTextureVersion selectedSource)
                    && (secondTransientTextureWriteVersion is GraphTextureVersion selectedDestination))
                {
                    recorder.CopyTexture(selectedSource, selectedDestination);
                }
            }

            void RecordSecondTransientTextureCopy(scoped ref CommandRecorder recorder)
            {
                if ((secondTransientTextureWriteVersion is GraphTextureVersion selectedTransientTexture)
                    && (copyDestinationTextureWriteVersion is GraphTextureVersion selectedTextureDestination))
                {
                    recorder.CopyTexture(selectedTransientTexture, selectedTextureDestination);
                }
            }

            void RecordTransientSlotInitializeA(scoped ref CommandRecorder recorder)
            {
                if ((transientSlotSourceVersion is GraphTextureVersion selectedSource)
                    && (transientSlotA is GraphTextureVersion selectedDestination))
                {
                    recorder.CopyTexture(selectedSource, selectedDestination);
                }
            }

            void RecordTransientSlotCopyA(scoped ref CommandRecorder recorder)
            {
                if ((transientSlotA is GraphTextureVersion selectedSource)
                    && (transientSlotDestinationA is GraphTextureVersion selectedDestination))
                {
                    recorder.CopyTexture(selectedSource, selectedDestination);
                }
            }

            void RecordTransientSlotInitializeB(scoped ref CommandRecorder recorder)
            {
                if ((transientSlotSourceVersion is GraphTextureVersion selectedSource)
                    && (transientSlotB is GraphTextureVersion selectedDestination))
                {
                    recorder.CopyTexture(selectedSource, selectedDestination);
                }
            }

            void RecordTransientSlotCopyB(scoped ref CommandRecorder recorder)
            {
                if ((transientSlotB is GraphTextureVersion selectedSource)
                    && (transientSlotDestinationB is GraphTextureVersion selectedDestination))
                {
                    recorder.CopyTexture(selectedSource, selectedDestination);
                }
            }

            void RecordTransientBufferInitialize(scoped ref CommandRecorder recorder)
            {
                if ((transientInitializationSourceVersion is GraphBufferVersion selectedSource)
                    && (transientBufferCopyWriteVersion is GraphBufferVersion selectedDestination))
                {
                    recorder.CopyBuffer(
                        selectedSource,
                        0,
                        selectedDestination,
                        0,
                        selectedSource.Range.ByteLength);
                }
            }

            void RecordTransientBufferSecondInitialize(scoped ref CommandRecorder recorder)
            {
                if ((transientSecondInitializationSourceVersion is GraphBufferVersion selectedSource)
                    && (transientSecondBufferCopyWriteVersion is GraphBufferVersion selectedDestination))
                {
                    recorder.CopyBuffer(
                        selectedSource,
                        0,
                        selectedDestination,
                        0,
                        selectedSource.Range.ByteLength);
                }
            }

            void RecordSecondCopy(scoped ref CommandRecorder recorder)
            {
                if (useTransientBufferAliasing
                    && (secondCopySourceVersion is GraphBufferVersion selectedSource)
                    && (secondCopyDestinationWriteVersion is GraphBufferVersion selectedDestination))
                {
                    recorder.CopyBuffer(
                        selectedSource,
                        0,
                        selectedDestination,
                        0,
                        selectedSource.Range.ByteLength);
                }
            }

            void RecordCompute(scoped ref CommandRecorder recorder)
            {
                var selectedPipeline = computePipeline
                    ?? throw new InvalidOperationException("The graph compute test has no compute pipeline.");
                var selectedBindings = computeBindingSet
                    ?? throw new InvalidOperationException("The graph compute test has no storage binding set.");
                recorder.SetPipeline(selectedPipeline.Handle);
                recorder.SetBindings(selectedBindings);
                recorder.Dispatch(new DispatchSize(1, 1, 1));
                recorder.Dispatch(new DispatchSize(1, 1, 1));
            }

            RenderPassId? computePassId = null;

            if (useCompute
                || useTransientStorage
                || useMultipleComputeStorageBuffers
                || useConstantWriteComputeBuffer)
            {
                ImmutableArray<RenderPassId> computePredecessors = [];

                if (useTransientStorage)
                {
                    var transientBufferInitial = graph.CreateTransientBuffer(new BufferDescription(
                        sizeof(uint),
                        BufferUsage.Storage | BufferUsage.CopySource | BufferUsage.CopyDestination,
                        GpuMemoryKind.DeviceLocal,
                        structuredStride: sizeof(uint),
                        debugName: $"m04-{backend}-transient-storage-buffer"));
                    var initializationPass = graph.AddCopyPass(
                        new RenderPassDescription("offscreen-transient-buffer-initialize", 0, [], hasExternalSideEffects: false),
                        RecordTransientBufferInitialize);
                    var initializationSource = transientInitializationSourceVersion
                        ?? throw new InvalidOperationException("The graph transient test has no imported initialization source.");
                    graph.ReadBuffer(
                        initializationPass,
                        initializationSource,
                        GraphBufferUsage.CopySource,
                        initializationSource.Range);
                    transientBufferCopyWriteVersion = graph.WriteBuffer(
                        initializationPass,
                        transientBufferInitial,
                        GraphBufferUsage.CopyDestination,
                        transientBufferInitial.Range);
                    computePredecessors = [initializationPass];
                }

                var computePass = graph.AddComputePass(
                    new RenderPassDescription(
                        "offscreen-compute-storage",
                        computePredecessors.Length,
                        computePredecessors,
                        hasExternalSideEffects: false),
                    RecordCompute);
                computePassId = computePass;

                if (useConstantWriteComputeBuffer)
                {
                    var initialStorageBuffer = storageBufferInitialVersion
                        ?? throw new InvalidOperationException("The graph constant-write compute test has no imported storage buffer.");
                    var storageBufferRange = useStorageBufferSubrange
                        ? new GraphBufferRange(storageBufferBindingOffset, sizeof(uint))
                        : initialStorageBuffer.Range;
                    computeStorageWriteVersion = graph.ReadWriteBuffer(
                        computePass,
                        initialStorageBuffer,
                        storageBufferRange);
                    copySourceVersion = computeStorageWriteVersion;
                    copySourceBufferRange = storageBufferRange;
                    computeBindingSet = new BindingSet(
                        [
                            new BufferBinding(
                                ShaderBindingGroup.Frame,
                                0,
                                ShaderResourceKind.StorageBuffer,
                                computeStorageWriteVersion,
                                storageBufferRange),
                        ],
                        [],
                        []);
                }
                else if (useMultipleComputeStorageImages)
                {
                    var initialStorageBuffer = storageBufferInitialVersion
                        ?? throw new InvalidOperationException("The graph multi-image compute test has no imported storage buffer.");
                    computeStorageWriteVersion = graph.ReadWriteBuffer(
                        computePass,
                        initialStorageBuffer,
                        initialStorageBuffer.Range);
                    copySourceVersion = computeStorageWriteVersion;
                    var initialStorageImage = storageImageInitialVersion
                        ?? throw new InvalidOperationException("The graph multi-image compute test has no first storage image.");
                    computeStorageImageWriteVersion = graph.ReadWriteTexture(
                        computePass,
                        initialStorageImage,
                        storageImageBindingRange);
                    var initialAdditionalStorageImage = additionalStorageImageInitialVersion
                        ?? throw new InvalidOperationException("The graph multi-image compute test has no second storage image.");
                    additionalComputeStorageImageWriteVersion = graph.ReadWriteTexture(
                        computePass,
                        initialAdditionalStorageImage,
                        additionalStorageImageBindingRange);
                    computeBindingSet = new BindingSet(
                        [
                            new BufferBinding(
                                ShaderBindingGroup.Frame,
                                0,
                                ShaderResourceKind.StorageBuffer,
                                computeStorageWriteVersion,
                                initialStorageBuffer.Range),
                        ],
                        [
                            new TextureBinding(
                                ShaderBindingGroup.Frame,
                                1,
                                ShaderResourceKind.StorageImage,
                                computeStorageImageWriteVersion,
                                storageImageBindingRange),
                            new TextureBinding(
                                ShaderBindingGroup.Frame,
                                6,
                                ShaderResourceKind.StorageImage,
                                additionalComputeStorageImageWriteVersion,
                                additionalStorageImageBindingRange),
                        ],
                        []);
                }
                else if (useMultipleComputeStorageBuffers)
                {
                    var initialStorageBuffer = storageBufferInitialVersion
                        ?? throw new InvalidOperationException("The graph multi-buffer compute test has no first imported storage buffer.");
                    var initialSecondStorageBuffer = secondStorageBufferInitialVersion
                        ?? throw new InvalidOperationException("The graph multi-buffer compute test has no second imported storage buffer.");
                    var firstBindingRange = useStorageBufferSubrange
                        ? new GraphBufferRange(storageBufferBindingOffset, sizeof(uint))
                        : initialStorageBuffer.Range;
                    var secondBindingRange = useStorageBufferSubrange
                        ? new GraphBufferRange(secondStorageBufferBindingOffset, sizeof(uint))
                        : initialSecondStorageBuffer.Range;
                    computeStorageWriteVersion = graph.ReadWriteBuffer(
                        computePass,
                        initialStorageBuffer,
                        firstBindingRange);
                    secondComputeStorageWriteVersion = graph.ReadWriteBuffer(
                        computePass,
                        initialSecondStorageBuffer,
                        secondBindingRange);
                    copySourceVersion = computeStorageWriteVersion;
                    copySourceBufferRange = firstBindingRange;
                    secondCopySourceVersion = secondComputeStorageWriteVersion;
                    secondCopySourceBufferRange = secondBindingRange;
                    computeBindingSet = new BindingSet(
                        [
                            new BufferBinding(
                                ShaderBindingGroup.Frame,
                                0,
                                ShaderResourceKind.StorageBuffer,
                                computeStorageWriteVersion,
                                firstBindingRange),
                            new BufferBinding(
                                ShaderBindingGroup.Frame,
                                2,
                                ShaderResourceKind.StorageBuffer,
                                secondComputeStorageWriteVersion,
                                secondBindingRange),
                        ],
                        [],
                        []);
                }
                else if (useTransientStorage)
                {
                    var transientBufferCopyDestination = transientBufferCopyWriteVersion
                        ?? throw new InvalidOperationException("The graph transient test has no initialized transient storage buffer.");
                    computeStorageWriteVersion = graph.ReadWriteBuffer(
                        computePass,
                        transientBufferCopyDestination,
                        transientBufferCopyDestination.Range);
                    copySourceVersion = computeStorageWriteVersion;
                    var initialStorageImage = storageImageInitialVersion
                        ?? throw new InvalidOperationException("The transient graph compute test has no imported storage image.");
                    computeStorageImageWriteVersion = graph.ReadWriteTexture(
                        computePass,
                        initialStorageImage,
                        storageImageBindingRange);
                    computeBindingSet = new BindingSet(
                        [
                            new BufferBinding(
                                ShaderBindingGroup.Frame,
                                0,
                                ShaderResourceKind.StorageBuffer,
                                computeStorageWriteVersion,
                                transientBufferCopyDestination.Range),
                        ],
                        [
                            new TextureBinding(
                                ShaderBindingGroup.Frame,
                                1,
                                ShaderResourceKind.StorageImage,
                                computeStorageImageWriteVersion,
                                storageImageBindingRange),
                        ],
                        []);
                }
                else
                {
                    var initialStorageBuffer = storageBufferInitialVersion
                        ?? throw new InvalidOperationException("The graph compute test has no imported storage buffer.");
                    computeStorageWriteVersion = graph.ReadWriteBuffer(
                        computePass,
                        initialStorageBuffer,
                        initialStorageBuffer.Range);
                    copySourceVersion = computeStorageWriteVersion;
                    var initialStorageImage = storageImageInitialVersion
                        ?? throw new InvalidOperationException("The graph compute test has no imported storage image.");
                    computeStorageImageWriteVersion = graph.ReadWriteTexture(
                        computePass,
                        initialStorageImage,
                        storageImageBindingRange);
                    computeBindingSet = new BindingSet(
                        [
                            new BufferBinding(
                                ShaderBindingGroup.Frame,
                                0,
                                ShaderResourceKind.StorageBuffer,
                                computeStorageWriteVersion,
                                initialStorageBuffer.Range),
                        ],
                        [
                            new TextureBinding(
                                ShaderBindingGroup.Frame,
                                use3DStorageImage
                                    ? 7
                                    : useFloatStorageImage
                                        ? 3
                                        : useRgba16FloatStorageImage
                                            ? 5
                                            : useSignedStorageImage
                                                ? 4
                                                : 1,
                                ShaderResourceKind.StorageImage,
                                computeStorageImageWriteVersion,
                                storageImageBindingRange),
                        ],
                        []);
                }
            }

            RenderPassId? copyPassId = null;

            if (useTransientBufferAliasing)
            {
                var transientDescription = new BufferDescription(
                    (expectedCopiedBytes
                        ?? throw new InvalidOperationException("The graph transient alias test has no first source data.")).Length,
                    BufferUsage.CopySource | BufferUsage.CopyDestination,
                    GpuMemoryKind.DeviceLocal,
                    structuredStride: null,
                    debugName: $"m04-{backend}-transient-alias-a");
                var transientFirstInitial = graph.CreateTransientBuffer(transientDescription);
                var initializationSource = transientInitializationSourceVersion
                    ?? throw new InvalidOperationException("The graph transient alias test has no first initialization source.");
                var initializationFirstPass = graph.AddCopyPass(
                    new RenderPassDescription("offscreen-transient-alias-a-in", 0, [], hasExternalSideEffects: false),
                    RecordTransientBufferInitialize);
                graph.ReadBuffer(
                    initializationFirstPass,
                    initializationSource,
                    GraphBufferUsage.CopySource,
                    initializationSource.Range);
                transientBufferCopyWriteVersion = graph.WriteBuffer(
                    initializationFirstPass,
                    transientFirstInitial,
                    GraphBufferUsage.CopyDestination,
                    transientFirstInitial.Range);
                copySourceVersion = transientBufferCopyWriteVersion;

                var copyFirstPass = graph.AddCopyPass(
                    new RenderPassDescription(
                        "offscreen-transient-alias-a-out",
                        overlapTransientBufferLifetimes ? 2 : 1,
                        [initializationFirstPass],
                        hasExternalSideEffects: false),
                    RecordCopy);
                var transientFirstSource = transientBufferCopyWriteVersion
                    ?? throw new InvalidOperationException("The graph transient alias test has no first copy-source version.");
                graph.ReadBuffer(
                    copyFirstPass,
                    transientFirstSource,
                    GraphBufferUsage.CopySource,
                    transientFirstSource.Range);
                var firstCopyDestination = copyDestinationInitialVersion
                    ?? throw new InvalidOperationException("The graph transient alias test has no first copy destination.");
                copyDestinationWriteVersion = graph.WriteBuffer(
                    copyFirstPass,
                    firstCopyDestination,
                    GraphBufferUsage.CopyDestination,
                    firstCopyDestination.Range);

                var transientSecondInitial = graph.CreateTransientBuffer(new BufferDescription(
                    transientDescription.ByteLength,
                    transientDescription.Usage,
                    transientDescription.MemoryKind,
                    transientDescription.StructuredStride,
                    $"m04-{backend}-transient-alias-b"));
                var secondInitializationSource = transientSecondInitializationSourceVersion
                    ?? throw new InvalidOperationException("The graph transient alias test has no second initialization source.");
                var secondInitializationPredecessor = overlapTransientBufferLifetimes
                    ? initializationFirstPass
                    : copyFirstPass;
                var initializationSecondPass = graph.AddCopyPass(
                    new RenderPassDescription(
                        "offscreen-transient-alias-b-in",
                        overlapTransientBufferLifetimes ? 1 : 2,
                        [secondInitializationPredecessor],
                        hasExternalSideEffects: false),
                    RecordTransientBufferSecondInitialize);
                graph.ReadBuffer(
                    initializationSecondPass,
                    secondInitializationSource,
                    GraphBufferUsage.CopySource,
                    secondInitializationSource.Range);
                transientSecondBufferCopyWriteVersion = graph.WriteBuffer(
                    initializationSecondPass,
                    transientSecondInitial,
                    GraphBufferUsage.CopyDestination,
                    transientSecondInitial.Range);
                secondCopySourceVersion = transientSecondBufferCopyWriteVersion;

                var copySecondPass = graph.AddCopyPass(
                    new RenderPassDescription(
                        "offscreen-transient-alias-b-out",
                        3,
                        [initializationSecondPass],
                        hasExternalSideEffects: false),
                    RecordSecondCopy);
                var transientSecondSource = transientSecondBufferCopyWriteVersion
                    ?? throw new InvalidOperationException("The graph transient alias test has no second copy-source version.");
                graph.ReadBuffer(
                    copySecondPass,
                    transientSecondSource,
                    GraphBufferUsage.CopySource,
                    transientSecondSource.Range);
                var secondCopyDestination = secondCopyDestinationInitialVersion
                    ?? throw new InvalidOperationException("The graph transient alias test has no second copy destination.");
                secondCopyDestinationWriteVersion = graph.WriteBuffer(
                    copySecondPass,
                    secondCopyDestination,
                    GraphBufferUsage.CopyDestination,
                    secondCopyDestination.Range);
                copyPassId = copySecondPass;
            }
            else if (useTransientTextureAliasing)
            {
                var selectedCopySource = copySourceTextureVersion
                    ?? throw new InvalidOperationException("The graph transient texture-alias test has no imported source.");
                var firstTransientTexture = transientTextureInitialVersion
                    ?? throw new InvalidOperationException("The graph transient texture-alias test has no first transient texture.");
                var secondTransientTexture = secondTransientTextureInitialVersion
                    ?? throw new InvalidOperationException("The graph transient texture-alias test has no second transient texture.");

                var copyFirstTextureInPass = graph.AddCopyPass(
                    new RenderPassDescription("offscreen-transient-texture-alias-a-in", 0, [], hasExternalSideEffects: false),
                    RecordCopy);
                graph.ReadTexture(
                    copyFirstTextureInPass,
                    selectedCopySource,
                    GraphTextureUsage.CopySource,
                    copyTextureRange);
                transientTextureWriteVersion = graph.WriteTexture(
                    copyFirstTextureInPass,
                    firstTransientTexture,
                    GraphTextureUsage.CopyDestination,
                    copyTextureRange);

                RenderPassId AddSecondTextureInputPass(RenderPassId predecessor, int order)
                {
                    var pass = graph.AddCopyPass(
                        new RenderPassDescription(
                            "offscreen-transient-texture-alias-b-in",
                            order,
                            [predecessor],
                            hasExternalSideEffects: false),
                        RecordSecondTransientTextureInitialize);
                    graph.ReadTexture(
                        pass,
                        selectedCopySource,
                        GraphTextureUsage.CopySource,
                        copyTextureRange);
                    secondTransientTextureWriteVersion = graph.WriteTexture(
                        pass,
                        secondTransientTexture,
                        GraphTextureUsage.CopyDestination,
                        copyTextureRange);
                    return pass;
                }

                RenderPassId AddFirstTextureOutputPass(RenderPassId predecessor, int order)
                {
                    var pass = graph.AddCopyPass(
                        new RenderPassDescription(
                            "offscreen-transient-texture-alias-a-out",
                            order,
                            [predecessor],
                            hasExternalSideEffects: false),
                        RecordTransientTextureCopy);
                    var firstTransientTextureSource = transientTextureWriteVersion
                        ?? throw new InvalidOperationException("The graph transient texture-alias test has no first write.");
                    graph.ReadTexture(
                        pass,
                        firstTransientTextureSource,
                        GraphTextureUsage.CopySource,
                        copyTextureRange);
                    var firstCopyDestination = copyDestinationTextureInitialVersion
                        ?? throw new InvalidOperationException("The graph transient texture-alias test has no destination.");
                    firstTransientTextureDestinationVersion = graph.WriteTexture(
                        pass,
                        firstCopyDestination,
                        GraphTextureUsage.CopyDestination,
                        copyTextureRange);
                    copyDestinationTextureWriteVersion = firstTransientTextureDestinationVersion;
                    return pass;
                }

                RenderPassId copyFirstTextureOutPass;
                RenderPassId copySecondTextureInPass;

                if (overlapTransientTextureLifetimes)
                {
                    copySecondTextureInPass = AddSecondTextureInputPass(copyFirstTextureInPass, 1);
                    copyFirstTextureOutPass = AddFirstTextureOutputPass(copySecondTextureInPass, 2);
                }
                else
                {
                    copyFirstTextureOutPass = AddFirstTextureOutputPass(copyFirstTextureInPass, 1);
                    copySecondTextureInPass = AddSecondTextureInputPass(copyFirstTextureOutPass, 2);
                }

                var copySecondTextureOutPass = graph.AddCopyPass(
                    new RenderPassDescription(
                        "offscreen-transient-texture-alias-b-out",
                        3,
                        [copyFirstTextureOutPass, copySecondTextureInPass],
                        hasExternalSideEffects: false),
                    RecordSecondTransientTextureCopy);
                var secondTransientTextureSource = secondTransientTextureWriteVersion
                    ?? throw new InvalidOperationException("The graph transient texture-alias test has no second write.");
                graph.ReadTexture(
                    copySecondTextureOutPass,
                    secondTransientTextureSource,
                    GraphTextureUsage.CopySource,
                    copyTextureRange);
                var secondCopyDestination = copyDestinationTextureWriteVersion
                    ?? throw new InvalidOperationException("The graph transient texture-alias test has no first destination write.");
                copyDestinationTextureWriteVersion = graph.WriteTexture(
                    copySecondTextureOutPass,
                    secondCopyDestination,
                    GraphTextureUsage.CopyDestination,
                    copyTextureRange);
                copyPassId = copySecondTextureOutPass;
            }
            else if (useTransientTextureCopy)
            {
                var selectedCopySource = copySourceTextureVersion
                    ?? throw new InvalidOperationException("The graph transient texture-copy test has no imported source.");
                var transientTexture = transientTextureInitialVersion
                    ?? throw new InvalidOperationException("The graph transient texture-copy test has no transient texture.");
                var copyTextureInPass = graph.AddCopyPass(
                    new RenderPassDescription("offscreen-transient-texture-copy-in", 0, [], hasExternalSideEffects: false),
                    RecordCopy);
                graph.ReadTexture(
                    copyTextureInPass,
                    selectedCopySource,
                    GraphTextureUsage.CopySource,
                    copyTextureRange);
                transientTextureWriteVersion = graph.WriteTexture(
                    copyTextureInPass,
                    transientTexture,
                    GraphTextureUsage.CopyDestination,
                    copyTextureRange);

                var copyTextureOutPass = graph.AddCopyPass(
                    new RenderPassDescription(
                        "offscreen-transient-texture-copy-out",
                        1,
                        [copyTextureInPass],
                        hasExternalSideEffects: false),
                    RecordTransientTextureCopy);
                var selectedTransientTexture = transientTextureWriteVersion
                    ?? throw new InvalidOperationException("The graph transient texture-copy test has no written transient version.");
                graph.ReadTexture(
                    copyTextureOutPass,
                    selectedTransientTexture,
                    GraphTextureUsage.CopySource,
                    copyTextureRange);
                var selectedCopyDestination = copyDestinationTextureInitialVersion
                    ?? throw new InvalidOperationException("The graph transient texture-copy test has no imported destination.");
                copyDestinationTextureWriteVersion = graph.WriteTexture(
                    copyTextureOutPass,
                    selectedCopyDestination,
                    GraphTextureUsage.CopyDestination,
                    copyTextureRange);
                firstTransientTextureDestinationVersion = copyDestinationTextureWriteVersion;
                copyPassId = copyTextureOutPass;
            }
            else if (useBufferCopy
                || useCompute
                || useTransientStorage
                || useMultipleComputeStorageBuffers
                || useConstantWriteComputeBuffer
                || useTextureCopy)
            {
                ImmutableArray<RenderPassId> copyPredecessors = computePassId is RenderPassId copyComputePredecessor
                    ? [copyComputePredecessor]
                    : [];
                var copyPass = graph.AddCopyPass(
                    new RenderPassDescription(
                        "offscreen-buffer-copy",
                        computePassId.HasValue ? 1 : 0,
                        copyPredecessors,
                        hasExternalSideEffects: false),
                    RecordCopy);
                copyPassId = copyPass;

                if ((copySourceVersion is GraphBufferVersion selectedCopySource)
                    && (copyDestinationInitialVersion is GraphBufferVersion selectedCopyDestination))
                {
                    var sourceRange = copySourceBufferRange ?? selectedCopySource.Range;
                    graph.ReadBuffer(
                        copyPass,
                        selectedCopySource,
                        GraphBufferUsage.CopySource,
                        sourceRange);
                    copyDestinationWriteVersion = graph.WriteBuffer(
                        copyPass,
                        selectedCopyDestination,
                        GraphBufferUsage.CopyDestination,
                        selectedCopyDestination.Range);
                }

                if (useManyBufferCopyDestinations)
                {
                    for (var index = 0; index < additionalCopyDestinationInitialVersions.Length; index++)
                    {
                        if (additionalCopyDestinationInitialVersions[index] is GraphBufferVersion additionalInitialVersion)
                        {
                            additionalCopyDestinationWriteVersions[index] = graph.WriteBuffer(
                                copyPass,
                                additionalInitialVersion,
                                GraphBufferUsage.CopyDestination,
                                additionalInitialVersion.Range);
                        }
                    }
                }

                if ((copySourceTextureVersion is GraphTextureVersion selectedTextureSource)
                    && (copyDestinationTextureInitialVersion is GraphTextureVersion selectedTextureDestination))
                {
                    graph.ReadTexture(
                        copyPass,
                        selectedTextureSource,
                        GraphTextureUsage.CopySource,
                        selectedTextureSource.Range);
                    copyDestinationTextureWriteVersion = graph.WriteTexture(
                        copyPass,
                        selectedTextureDestination,
                        GraphTextureUsage.CopyDestination,
                        selectedTextureDestination.Range);
                }

                if (useMultipleTextureCopies
                    && (secondCopySourceTextureVersion is GraphTextureVersion selectedSecondTextureSource)
                    && (secondCopyDestinationTextureInitialVersion is GraphTextureVersion selectedSecondTextureDestination))
                {
                    graph.ReadTexture(
                        copyPass,
                        selectedSecondTextureSource,
                        GraphTextureUsage.CopySource,
                        selectedSecondTextureSource.Range);
                    secondCopyDestinationTextureWriteVersion = graph.WriteTexture(
                        copyPass,
                        selectedSecondTextureDestination,
                        GraphTextureUsage.CopyDestination,
                        selectedSecondTextureDestination.Range);
                }

                if (useMultipleComputeStorageBuffers)
                {
                    var selectedSecondCopySource = secondCopySourceVersion
                        ?? throw new InvalidOperationException("The graph multi-buffer compute test has no second copy-source version.");
                    var selectedSecondCopyDestination = secondCopyDestinationInitialVersion
                        ?? throw new InvalidOperationException("The graph multi-buffer compute test has no second copy-destination version.");
                    var sourceRange = secondCopySourceBufferRange ?? selectedSecondCopySource.Range;
                    graph.ReadBuffer(
                        copyPass,
                        selectedSecondCopySource,
                        GraphBufferUsage.CopySource,
                        sourceRange);
                    secondCopyDestinationWriteVersion = graph.WriteBuffer(
                        copyPass,
                        selectedSecondCopyDestination,
                        GraphBufferUsage.CopyDestination,
                        selectedSecondCopyDestination.Range);
                }
            }

            var rasterPredecessorBuilder = ImmutableArray.CreateBuilder<RenderPassId>(2);

            if (copyPassId is RenderPassId copyPredecessor)
            {
                rasterPredecessorBuilder.Add(copyPredecessor);
            }

            if (computePassId is RenderPassId computePredecessor)
            {
                rasterPredecessorBuilder.Add(computePredecessor);
            }

            var rasterPredecessors = rasterPredecessorBuilder.ToImmutable();
            var pass = graph.AddRasterPass(
                new RenderPassDescription(
                    "offscreen-clear",
                    rasterPredecessors.Length,
                    rasterPredecessors,
                    hasExternalSideEffects: false),
                RecordClear);

            if (frameUniformVersion is not null)
            {
                var range = frameUniformBindingRange ?? frameUniformVersion.Range;
                graph.ReadBuffer(
                    pass,
                    frameUniformVersion,
                    GraphBufferUsage.UniformRead,
                    range);
            }

            if (additionalFrameUniformVersion is not null)
            {
                graph.ReadBuffer(
                    pass,
                    additionalFrameUniformVersion,
                    GraphBufferUsage.UniformRead,
                    additionalFrameUniformVersion.Range);
            }

            if (sampledTextureVersion is not null)
            {
                graph.ReadTexture(
                    pass,
                    sampledTextureVersion,
                    GraphTextureUsage.SampledRead,
                    sampledTextureBindingRange);
            }

            if (additionalSampledTextureVersion is GraphTextureVersion selectedAdditionalSampledTextureVersion)
            {
                graph.ReadTexture(
                    pass,
                    selectedAdditionalSampledTextureVersion,
                    GraphTextureUsage.SampledRead,
                    sampledTextureBindingRange);
            }

            for (var index = 0; index < sixteenSampledTextureVersions.Length; index++)
            {
                if (sixteenSampledTextureVersions[index] is GraphTextureVersion selectedSixteenSampledTextureVersion)
                {
                    graph.ReadTexture(
                        pass,
                        selectedSixteenSampledTextureVersion,
                        GraphTextureUsage.SampledRead,
                        sampledTextureRange);
                }
            }

            if (useStorageResources && (storageBufferInitialVersion is not null))
            {
                var range = storageBufferInitialVersion.Range;
                storageBufferWriteVersion = graph.ReadWriteBuffer(
                    pass,
                    storageBufferInitialVersion,
                    range);
                storageBufferBindings.Add(new BufferBinding(
                    ShaderBindingGroup.Frame,
                    0,
                    ShaderResourceKind.StorageBuffer,
                    storageBufferWriteVersion,
                    range));
            }

            if (useStorageResources && (storageImageInitialVersion is not null))
            {
                storageImageWriteVersion = graph.ReadWriteTexture(
                    pass,
                    storageImageInitialVersion,
                    storageImageBindingRange);
                textureBindings.Add(new TextureBinding(
                    ShaderBindingGroup.Frame,
                    1,
                    ShaderResourceKind.StorageImage,
                    storageImageWriteVersion,
                    storageImageBindingRange));
            }

            if (useUniformBuffer
                || useMultipleFrameUniformBuffers
                || useSampledTexture
                || useSixteenSampledImages
                || useStorageResources)
            {
                frameBindingSet = new BindingSet(
                    [
                        .. bufferBindings.ToImmutable(),
                        .. storageBufferBindings.ToImmutable(),
                    ],
                    textureBindings.ToImmutable(),
                    samplerBindings.ToImmutable());
            }

            var outputVersion = graph.WriteColorAttachment(
                pass,
                graph.OffscreenColorTargetVersion,
                new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color),
                useAttachmentDontCareDiscardRoundTrip
                    ? AttachmentLoadAction.DontCare
                    : AttachmentLoadAction.Clear,
                useAttachmentDontCareDiscardRoundTrip
                    ? AttachmentStoreAction.Discard
                    : AttachmentStoreAction.Store,
                renderSnapshot.Cameras[0].ClearColor);

            for (var index = 0; index < secondaryColorAttachmentInitialVersions.Length; index++)
            {
                if (secondaryColorAttachmentInitialVersions[index] is GraphTextureVersion selectedSecondaryColorAttachmentVersion)
                {
                    secondaryColorAttachmentWriteVersions[index] = graph.WriteColorAttachment(
                        pass,
                        selectedSecondaryColorAttachmentVersion,
                        selectedSecondaryColorAttachmentVersion.Range,
                        AttachmentLoadAction.Clear,
                        AttachmentStoreAction.Store,
                        new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
                }
            }

            GraphTextureVersion? depthAttachmentVersion = null;

            if (depthAttachmentInitialVersion is GraphTextureVersion selectedDepthAttachmentVersion)
            {
                depthAttachmentVersion = graph.WriteDepthAttachment(
                    pass,
                    selectedDepthAttachmentVersion,
                    selectedDepthAttachmentVersion.Range,
                    useAttachmentDontCareDiscardRoundTrip
                        ? AttachmentLoadAction.DontCare
                        : AttachmentLoadAction.Clear,
                    useAttachmentDontCareDiscardRoundTrip
                        ? AttachmentStoreAction.Discard
                        : AttachmentStoreAction.Store,
                    clearDepth: 0.0f);
            }

            if (useTransientCopyAliasing)
            {
                var transientDescription = new TextureDescription(
                    TextureDimension.Dimension2D,
                    targetExtent.Width,
                    targetExtent.Height,
                    depth: 1,
                    arrayLayers: 1,
                    mipLevels: 1,
                    TextureFormat.Rgba8Unorm,
                    TextureUsage.CopySource | TextureUsage.CopyDestination,
                    debugName: $"m04-{backend}-slot-transient-copy");
                var transientRange = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);
                transientSlotSourceVersion = outputVersion;
                var firstTransientTexture = graph.CreateTransientTexture(transientDescription);
                var firstCopyInputPass = graph.AddCopyPass(
                    new RenderPassDescription(
                        "offscreen-slot-transient-copy-a-in",
                        checked(rasterPredecessors.Length + 1),
                        [pass],
                        hasExternalSideEffects: true),
                    RecordTransientSlotInitializeA);
                graph.ReadTexture(
                    firstCopyInputPass,
                    outputVersion,
                    GraphTextureUsage.CopySource,
                    transientRange);
                transientSlotA = graph.WriteTexture(
                    firstCopyInputPass,
                    firstTransientTexture,
                    GraphTextureUsage.CopyDestination,
                    transientRange);

                var firstCopyOutputPass = graph.AddCopyPass(
                    new RenderPassDescription(
                        "offscreen-slot-transient-copy-a-out",
                        checked(rasterPredecessors.Length + 2),
                        [firstCopyInputPass],
                        hasExternalSideEffects: true),
                    RecordTransientSlotCopyA);
                graph.ReadTexture(
                    firstCopyOutputPass,
                    transientSlotA
                        ?? throw new InvalidOperationException("The first transient copy pass omitted its destination."),
                    GraphTextureUsage.CopySource,
                    transientRange);
                transientSlotDestinationA = graph.WriteTexture(
                    firstCopyOutputPass,
                    copyDestinationTextureInitialVersion
                        ?? throw new InvalidOperationException("The slot-transient copy test has no imported copy destination."),
                    GraphTextureUsage.CopyDestination,
                    transientRange);

                var secondTransientTexture = graph.CreateTransientTexture(transientDescription);
                var secondCopyInputPass = graph.AddCopyPass(
                    new RenderPassDescription(
                        "offscreen-slot-transient-copy-b-in",
                        checked(rasterPredecessors.Length + 3),
                        [firstCopyOutputPass],
                        hasExternalSideEffects: true),
                    RecordTransientSlotInitializeB);
                graph.ReadTexture(
                    secondCopyInputPass,
                    outputVersion,
                    GraphTextureUsage.CopySource,
                    transientRange);
                transientSlotB = graph.WriteTexture(
                    secondCopyInputPass,
                    secondTransientTexture,
                    GraphTextureUsage.CopyDestination,
                    transientRange);

                var secondCopyOutputPass = graph.AddCopyPass(
                    new RenderPassDescription(
                        "offscreen-slot-transient-copy-b-out",
                        checked(rasterPredecessors.Length + 4),
                        [secondCopyInputPass],
                        hasExternalSideEffects: true),
                    RecordTransientSlotCopyB);
                graph.ReadTexture(
                    secondCopyOutputPass,
                    transientSlotB
                        ?? throw new InvalidOperationException("The second transient copy pass omitted its destination."),
                    GraphTextureUsage.CopySource,
                    transientRange);
                transientSlotDestinationB = graph.WriteTexture(
                    secondCopyOutputPass,
                    transientSlotDestinationA
                        ?? throw new InvalidOperationException("The first transient copy output omitted its destination."),
                    GraphTextureUsage.CopyDestination,
                    transientRange);
            }

            if (useStorageResources)
            {
                var secondPass = graph.AddRasterPass(
                    new RenderPassDescription("offscreen-storage-hazard", 1, [], hasExternalSideEffects: false),
                    RecordSecondClear);
                var secondBufferBindings = ImmutableArray.CreateBuilder<BufferBinding>(
                    (frameUniformVersion is null ? 0 : 1) + (storageBufferWriteVersion is null ? 0 : 1));
                var secondTextureBindings = ImmutableArray.CreateBuilder<TextureBinding>(
                    (sampledTextureVersion is null ? 0 : 1) + (storageImageWriteVersion is null ? 0 : 1));
                var secondSamplerBindings = ImmutableArray.CreateBuilder<SamplerBinding>(
                    sampledTextureVersion is null ? 0 : 1);

                if (frameUniformVersion is GraphBufferVersion secondUniformVersion)
                {
                    secondFrameUniformVersion = secondUniformVersion;
                    graph.ReadBuffer(
                        secondPass,
                        secondUniformVersion,
                        GraphBufferUsage.UniformRead,
                        secondUniformVersion.Range);
                    secondBufferBindings.Add(new BufferBinding(
                        ShaderBindingGroup.Frame,
                        frameUniformBindingIndex,
                        ShaderResourceKind.UniformBuffer,
                        secondUniformVersion,
                        secondUniformVersion.Range));
                }

                if (sampledTextureVersion is GraphTextureVersion secondSampledVersion)
                {
                    secondSampledTextureVersion = secondSampledVersion;
                    graph.ReadTexture(
                        secondPass,
                        secondSampledVersion,
                        GraphTextureUsage.SampledRead,
                        sampledTextureBindingRange);
                    secondTextureBindings.Add(new TextureBinding(
                        ShaderBindingGroup.Frame,
                        sampledImageBindingIndex,
                        ShaderResourceKind.SampledImage,
                        secondSampledVersion,
                        sampledTextureBindingRange));
                    secondSamplerBindings.Add(new SamplerBinding(
                        ShaderBindingGroup.Frame,
                        sampledImageBindingIndex,
                        frameSampler
                            ?? throw new InvalidOperationException("The sampled Frame pipeline has no sampler.")));
                }

                var previousStorageBufferVersion = storageBufferWriteVersion
                    ?? throw new InvalidOperationException("The graph storage test has no first buffer write version.");
                var secondStorageBufferVersion = graph.ReadWriteBuffer(
                    secondPass,
                    previousStorageBufferVersion,
                    previousStorageBufferVersion.Range);
                secondBufferBindings.Add(new BufferBinding(
                    ShaderBindingGroup.Frame,
                    0,
                    ShaderResourceKind.StorageBuffer,
                    secondStorageBufferVersion,
                    previousStorageBufferVersion.Range));

                var previousStorageImageVersion = storageImageWriteVersion
                    ?? throw new InvalidOperationException("The graph storage test has no first image write version.");
                var secondStorageImageVersion = graph.ReadWriteTexture(
                    secondPass,
                    previousStorageImageVersion,
                    storageImageBindingRange);
                secondTextureBindings.Add(new TextureBinding(
                    ShaderBindingGroup.Frame,
                    1,
                    ShaderResourceKind.StorageImage,
                    secondStorageImageVersion,
                    storageImageBindingRange));
                secondFrameBindingSet = new BindingSet(
                    secondBufferBindings.MoveToImmutable(),
                    secondTextureBindings.MoveToImmutable(),
                    secondSamplerBindings.MoveToImmutable());
                var secondOutputVersion = graph.WriteColorAttachment(
                    secondPass,
                    outputVersion,
                    new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color),
                    AttachmentLoadAction.Clear,
                    AttachmentStoreAction.Store,
                    renderSnapshot.Cameras[0].ClearColor);
                outputVersion = secondOutputVersion;
            }

            if (useColorAndDepthAttachmentLoad)
            {
                var loadPass = graph.AddRasterPass(
                    new RenderPassDescription(
                        "offscreen-attachment-load",
                        rasterPredecessors.Length + 1,
                        [pass],
                        hasExternalSideEffects: false),
                    RecordEmptyPass);
                outputVersion = graph.WriteColorAttachment(
                    loadPass,
                    outputVersion,
                    new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color),
                    AttachmentLoadAction.Load,
                    AttachmentStoreAction.Store,
                    Vector4.Zero);
                var loadDepthVersion = depthAttachmentVersion
                    ?? throw new InvalidOperationException("The graph attachment-load test has no depth attachment.");
                _ = graph.WriteDepthAttachment(
                    loadPass,
                    loadDepthVersion,
                    loadDepthVersion.Range,
                    AttachmentLoadAction.Load,
                    AttachmentStoreAction.Store,
                    clearDepth: 0.0f);
            }
            else if (useAttachmentDontCareDiscardRoundTrip)
            {
                var restorePass = graph.AddRasterPass(
                    new RenderPassDescription(
                        "offscreen-attachment-restore",
                        rasterPredecessors.Length + 1,
                        [pass],
                        hasExternalSideEffects: false),
                    RecordEmptyPass);
                outputVersion = graph.WriteColorAttachment(
                    restorePass,
                    outputVersion,
                    new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color),
                    AttachmentLoadAction.Clear,
                    AttachmentStoreAction.Store,
                    renderSnapshot.Cameras[0].ClearColor);

                if (depthAttachmentVersion is GraphTextureVersion discardDepthVersion)
                {
                    depthAttachmentVersion = graph.WriteDepthAttachment(
                        restorePass,
                        discardDepthVersion,
                        discardDepthVersion.Range,
                        AttachmentLoadAction.Clear,
                        AttachmentStoreAction.Store,
                        clearDepth: 0.0f);
                }
            }

            if (useColorAttachmentCopy || useColorAttachmentCopyDestination)
            {
                var source = secondaryColorAttachmentWriteVersions[0]
                    ?? throw new InvalidOperationException("The graph color-attachment copy has no rendered source.");
                var destination = copyDestinationTextureInitialVersion
                    ?? throw new InvalidOperationException("The graph color-attachment copy has no imported destination.");
                var copyPass = graph.AddCopyPass(
                    new RenderPassDescription(
                        "offscreen-color-attachment-copy",
                        1,
                        [pass],
                        hasExternalSideEffects: false),
                    RecordColorAttachmentCopy);
                graph.ReadTexture(
                    copyPass,
                    source,
                    GraphTextureUsage.CopySource,
                    source.Range);
                colorAttachmentCopyDestinationVersion = graph.WriteTexture(
                    copyPass,
                    destination,
                    GraphTextureUsage.CopyDestination,
                    destination.Range);
                copyDestinationTextureWriteVersion = colorAttachmentCopyDestinationVersion;

                if (useColorAttachmentCopyDestination)
                {
                    var destinationVersion = colorAttachmentCopyDestinationVersion
                        ?? throw new InvalidOperationException("The graph color-attachment copy has no destination version.");
                    var loadPass = graph.AddRasterPass(
                        new RenderPassDescription(
                            "offscreen-color-attachment-copy-destination-load",
                            2,
                            [copyPass],
                            hasExternalSideEffects: false),
                        RecordEmptyPass);
                    outputVersion = graph.WriteColorAttachment(
                        loadPass,
                        outputVersion,
                        new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color),
                        AttachmentLoadAction.Load,
                        AttachmentStoreAction.Store,
                        Vector4.Zero);
                    copyDestinationTextureWriteVersion = graph.WriteColorAttachment(
                        loadPass,
                        destinationVersion,
                        destinationVersion.Range,
                        AttachmentLoadAction.Load,
                        AttachmentStoreAction.Store,
                        Vector4.Zero);
                }
            }

            if (useRenderAreaClear)
            {
                var renderArea = new RenderArea(1, 1, 4, 4);
                var partialClearPass = graph.AddRasterPass(new RenderPassDescription(
                    "offscreen-partial-area-clear",
                    rasterPredecessors.Length + 1,
                    [pass],
                    hasExternalSideEffects: false,
                    renderArea: renderArea));
                outputVersion = graph.WriteColorAttachment(
                    partialClearPass,
                    outputVersion,
                    new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color),
                    AttachmentLoadAction.Clear,
                    AttachmentStoreAction.Store,
                    new Vector4(0.0f, 1.0f, 0.0f, 1.0f));
            }

            graph.SetPresentationSource(outputVersion);

            var compileResponse = graph.Compile();
            Assert.That(compileResponse.Status, Is.EqualTo(RenderGraphCompileStatus.Compiled));
            Assert.That(compileResponse.Diagnostics, Is.Empty);
            var plan = compileResponse.Plan
                ?? throw new InvalidOperationException("The compiled graph response omitted its plan.");

            if (expectStorageImageFormatRejection || expectStorageImageDimensionRejection)
            {
                var exception = Assert.ThrowsAsync<NotSupportedException>(async () =>
                    await renderer.SubmitAsync(renderSnapshot, plan).ConfigureAwait(false));
                var expectedRejection = expectStorageImageDimensionRejection
                    ? "must match its reflected format/dimension"
                    : useSignedStorageImage
                        ? "no texture format for an R32SInt storage image"
                        : "must match its reflected format";
                Assert.That(
                    exception!.Message,
                    Does.Contain(expectedRejection));
                return;
            }

            RenderGraphPlan? delayedTransientTexturePlan = null;

            if (useDelayedTransientTextureAliasing)
            {
                delayedTransientTexturePlan = CreateDelayedTransientTextureAliasPlan();
                Assert.That(delayedTransientTexturePlan.TransientTextureGroups, Has.Length.EqualTo(1));
                Assert.That(delayedTransientTexturePlan.TransientTextureGroups[0], Has.Length.EqualTo(2));
                Assert.That(delayedTransientTexturePlan.TransientTextureAliasingBarriers, Has.Length.EqualTo(1));
            }

            RenderGraphPlan CreateDelayedTransientTextureAliasPlan()
            {
                var selectedTarget = delayedGraphTarget
                    ?? throw new InvalidOperationException("The delayed Vulkan texture graph has no output target.");
                var selectedDestination = secondCopyDestinationTexture
                    ?? delayedGraphDestination
                    ?? throw new InvalidOperationException("The delayed Vulkan texture graph has no copy destination.");
                var delayedGraph = renderer.BuildGraph(renderSnapshot, selectedTarget);
                var targetRange = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);
                var targetVersion = delayedGraph.OffscreenColorTargetVersion;

                void RecordTargetClear(scoped ref CommandRecorder recorder)
                {
                    _ = recorder;
                }

                var clearPass = delayedGraph.AddRasterPass(
                    new RenderPassDescription("delayed-slot-target-clear", 0, [], hasExternalSideEffects: false),
                    RecordTargetClear);
                var clearedTarget = delayedGraph.WriteColorAttachment(
                    clearPass,
                    targetVersion,
                    targetRange,
                    AttachmentLoadAction.Clear,
                    AttachmentStoreAction.Store,
                    renderSnapshot.Cameras[0].ClearColor);
                var transientDescription = new TextureDescription(
                    TextureDimension.Dimension2D,
                    targetExtent.Width,
                    targetExtent.Height,
                    depth: 1,
                    arrayLayers: 1,
                    mipLevels: 1,
                    TextureFormat.Rgba8Unorm,
                    TextureUsage.CopySource | TextureUsage.CopyDestination,
                    "delayed-slot-alias-transient");
                var transientA = delayedGraph.CreateTransientTexture(transientDescription);
                var transientB = delayedGraph.CreateTransientTexture(transientDescription);
                var destinationInitial = delayedGraph.ImportTexture(
                    selectedDestination,
                    targetRange,
                    new GraphImportOptions(
                        GraphResourceState.Undefined,
                        contentsValid: false,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                GraphTextureVersion? transientASource = null;
                GraphTextureVersion? transientAWrite = null;
                GraphTextureVersion? transientBSource = null;
                GraphTextureVersion? transientBWrite = null;
                GraphTextureVersion? destinationWriteA = null;
                GraphTextureVersion? destinationWriteB = null;

                void RecordTransientAInitialize(scoped ref CommandRecorder recorder)
                {
                    if ((clearedTarget is GraphTextureVersion source)
                        && (transientAWrite is GraphTextureVersion destination))
                    {
                        recorder.CopyTexture(source, destination);
                    }
                }

                void RecordTransientACopy(scoped ref CommandRecorder recorder)
                {
                    if ((transientASource is GraphTextureVersion source)
                        && (destinationWriteA is GraphTextureVersion destination))
                    {
                        recorder.CopyTexture(source, destination);
                    }
                }

                void RecordTransientBInitialize(scoped ref CommandRecorder recorder)
                {
                    if ((clearedTarget is GraphTextureVersion source)
                        && (transientBWrite is GraphTextureVersion destination))
                    {
                        recorder.CopyTexture(source, destination);
                    }
                }

                void RecordTransientBCopy(scoped ref CommandRecorder recorder)
                {
                    if ((transientBSource is GraphTextureVersion source)
                        && (destinationWriteB is GraphTextureVersion destination))
                    {
                        recorder.CopyTexture(source, destination);
                    }
                }

                var initializeAPass = delayedGraph.AddCopyPass(
                    new RenderPassDescription("delayed-slot-alias-a-in", 1, [clearPass], hasExternalSideEffects: true),
                    RecordTransientAInitialize);
                delayedGraph.ReadTexture(initializeAPass, clearedTarget, GraphTextureUsage.CopySource, targetRange);
                transientAWrite = delayedGraph.WriteTexture(
                    initializeAPass,
                    transientA,
                    GraphTextureUsage.CopyDestination,
                    targetRange);
                transientASource = transientAWrite;

                var copyAPass = delayedGraph.AddCopyPass(
                    new RenderPassDescription("delayed-slot-alias-a-out", 2, [initializeAPass], hasExternalSideEffects: true),
                    RecordTransientACopy);
                var selectedTransientASource = transientASource
                    ?? throw new InvalidOperationException("The delayed Vulkan texture graph has no first transient source.");
                delayedGraph.ReadTexture(copyAPass, selectedTransientASource, GraphTextureUsage.CopySource, targetRange);
                destinationWriteA = delayedGraph.WriteTexture(
                    copyAPass,
                    destinationInitial,
                    GraphTextureUsage.CopyDestination,
                    targetRange);

                var initializeBPass = delayedGraph.AddCopyPass(
                    new RenderPassDescription("delayed-slot-alias-b-in", 3, [copyAPass], hasExternalSideEffects: true),
                    RecordTransientBInitialize);
                delayedGraph.ReadTexture(initializeBPass, clearedTarget, GraphTextureUsage.CopySource, targetRange);
                transientBWrite = delayedGraph.WriteTexture(
                    initializeBPass,
                    transientB,
                    GraphTextureUsage.CopyDestination,
                    targetRange);
                transientBSource = transientBWrite;

                var copyBPass = delayedGraph.AddCopyPass(
                    new RenderPassDescription("delayed-slot-alias-b-out", 4, [initializeBPass], hasExternalSideEffects: true),
                    RecordTransientBCopy);
                var selectedTransientBSource = transientBSource
                    ?? throw new InvalidOperationException("The delayed Vulkan texture graph has no second transient source.");
                var selectedDestinationWriteA = destinationWriteA
                    ?? throw new InvalidOperationException("The delayed Vulkan texture graph has no first destination version.");
                delayedGraph.ReadTexture(copyBPass, selectedTransientBSource, GraphTextureUsage.CopySource, targetRange);
                destinationWriteB = delayedGraph.WriteTexture(
                    copyBPass,
                    selectedDestinationWriteA,
                    GraphTextureUsage.CopyDestination,
                    targetRange);
                delayedGraph.SetPresentationSource(clearedTarget);

                var delayedCompileResponse = delayedGraph.Compile();
                Assert.That(delayedCompileResponse.Status, Is.EqualTo(RenderGraphCompileStatus.Compiled));
                Assert.That(delayedCompileResponse.Diagnostics, Is.Empty);
                return delayedCompileResponse.Plan
                    ?? throw new InvalidOperationException("The delayed Vulkan texture graph did not compile.");
            }

            if (useTransientCopyAliasing)
            {
                Assert.That(plan.TransientTextureGroups, Has.Length.EqualTo(1));
                Assert.That(plan.TransientTextureGroups[0], Has.Length.EqualTo(2));
                Assert.That(plan.TransientTextureAliasingBarriers, Has.Length.EqualTo(1));
            }

            if (useTransientBufferAliasing)
            {
                if (overlapTransientBufferLifetimes)
                {
                    Assert.That(plan.TransientBufferGroups.Length, Is.EqualTo(2));
                    Assert.That(plan.TransientBufferGroups[0].Length, Is.EqualTo(1));
                    Assert.That(plan.TransientBufferGroups[1].Length, Is.EqualTo(1));
                    Assert.That(plan.TransientBufferAliasingBarriers, Is.Empty);
                }
                else
                {
                    Assert.That(plan.TransientBufferGroups.Length, Is.EqualTo(1));
                    Assert.That(plan.TransientBufferGroups[0].Length, Is.EqualTo(2));
                    Assert.That(plan.TransientBufferAliasingBarriers.Length, Is.EqualTo(1));
                }
            }

            if (useTransientTextureAliasing)
            {
                if (overlapTransientTextureLifetimes)
                {
                    Assert.That(plan.TransientTextureGroups.Length, Is.EqualTo(2));
                    Assert.That(plan.TransientTextureGroups[0], Has.Length.EqualTo(1));
                    Assert.That(plan.TransientTextureGroups[1], Has.Length.EqualTo(1));
                    Assert.That(plan.TransientTextureAliasingBarriers, Is.Empty);
                }
                else
                {
                    Assert.That(plan.TransientTextureGroups.Length, Is.EqualTo(1));
                    Assert.That(plan.TransientTextureGroups[0].Length, Is.EqualTo(2));
                    Assert.That(plan.TransientTextureAliasingBarriers.Length, Is.EqualTo(1));
                    Assert.That(
                        plan.TransientTextureAliasingBarriers[0].PassId,
                        Is.EqualTo(plan.OrderedPasses[2]));

                    using var materializedGroup = await graphicsDevice.CreateTransientTextureGroupAsync(
                            plan.TransientTextureGroups[0],
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    Assert.That(materializedGroup.Textures.Length, Is.EqualTo(2));
                }
            }

            RenderSubmissionReceipt receipt;

            if (holdGraphCompletion)
            {
                _ = Interlocked.Exchange(ref holdFenceWaitCount, 2);
                _ = Interlocked.Exchange(ref holdCompletedGraphFenceValue, 1);
                var submissionTask = renderer.SubmitAsync(renderSnapshot, plan);
                observeReturnedTask?.Invoke(submissionTask, renderSnapshot, plan, renderer, graphicsDevice);
                var secondSubmissionTask = renderer.SubmitAsync(
                    renderSnapshot,
                    delayedTransientTexturePlan ?? plan);
                TestContext.WriteLine(
                    $"Runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; "
                    + $"Architecture={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}; "
                    + $"RendererTask={submissionTask.GetType().FullName}; "
                    + $"StatusBeforeCompletion={submissionTask.Status}");

                var twoGraphWaitsStarted = twoFenceWaitsEntered.Wait(TimeSpan.FromSeconds(5));
                Assert.That(
                    twoGraphWaitsStarted,
                    Is.True,
                    $"Graph waiters={Volatile.Read(ref enteredFenceWaitCount)}; first={submissionTask.Status}; "
                    + $"first exception={submissionTask.Exception}; "
                    + $"second={secondSubmissionTask.Status}; second exception={secondSubmissionTask.Exception}");
                var observedTransientAllocationGroups = transientGraphAllocationLeases.ToArray();

                if (useTransientCopyAliasing)
                {
                    Assert.That(observedTransientAllocationGroups, Has.Length.EqualTo(2));

                    foreach (var allocationGroup in observedTransientAllocationGroups)
                    {
                        Assert.That(allocationGroup, Has.Length.EqualTo(2));
                        Assert.That(allocationGroup[1], Is.SameAs(allocationGroup[0]));
                        Assert.That(allocationGroup[0].IsReleased, Is.False);
                    }

                    Assert.That(
                        observedTransientAllocationGroups[0][0],
                        Is.Not.SameAs(observedTransientAllocationGroups[1][0]));
                }

                var observedTransientBufferAllocationGroups = transientBufferGraphAllocationLeases.ToArray();

                if (useTransientBufferAliasing)
                {
                    Assert.That(observedTransientBufferAllocationGroups, Has.Length.EqualTo(2));

                    foreach (var allocationGroup in observedTransientBufferAllocationGroups)
                    {
                        Assert.That(allocationGroup, Has.Length.EqualTo(2));
                        Assert.That(allocationGroup[1], Is.SameAs(allocationGroup[0]));
                        Assert.That(allocationGroup[0].IsReleased, Is.False);
                    }

                    Assert.That(
                        observedTransientBufferAllocationGroups[0][0],
                        Is.Not.SameAs(observedTransientBufferAllocationGroups[1][0]));
                }

                Assert.That(submissionTask.IsCompleted, Is.False);
                Assert.That(secondSubmissionTask.IsCompleted, Is.False);
                using var slotWaitCancellation = new CancellationTokenSource();
                var cancelledSubmissionTask = renderer.SubmitAsync(
                    renderSnapshot,
                    plan,
                    slotWaitCancellation.Token);
                await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
                Assert.That(cancelledSubmissionTask.IsCompleted, Is.False);
                await slotWaitCancellation.CancelAsync().ConfigureAwait(false);
                var cancellationException = Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await cancelledSubmissionTask.ConfigureAwait(false));
                Assert.That(cancelledSubmissionTask.Status, Is.EqualTo(TaskStatus.Canceled));
                Assert.That(cancellationException?.CancellationToken, Is.EqualTo(slotWaitCancellation.Token));

                var submittedTarget = target
                    ?? throw new InvalidOperationException("The delayed graph test has no imported color target.");
                var submittedTargetOwner = submittedTarget.ResourceOwner;
                submittedTarget.Dispose();
                target = null;
                Assert.That(submittedTargetOwner.HasActiveUses, Is.True);
                Assert.That(submittedTargetOwner.IsRetirementRequested, Is.True);
                Assert.That(submittedTargetOwner.LastSubmittedQueueFence, Is.GreaterThan(0));

                continueFenceWait.Set();
                receipt = await submissionTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                var queuedReceipt = await secondSubmissionTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(submissionTask.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                Assert.That(secondSubmissionTask.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                Assert.That(receipt.CompletedFence, Is.GreaterThan(0));
                Assert.That(queuedReceipt.CompletedFence, Is.GreaterThan(0));
                Assert.That(queuedReceipt.CompletedFence, Is.Not.EqualTo(receipt.CompletedFence));

                if (backend == GraphicsBackend.D3D12)
                {
                    Assert.That(Volatile.Read(ref completedGraphFenceWaitCount), Is.EqualTo(2));
                }

                Assert.That(submittedTargetOwner.HasActiveUses, Is.False);
                var expectedTargetFence = delayedTransientTexturePlan is null
                    ? Math.Max(receipt.CompletedFence, queuedReceipt.CompletedFence)
                    : receipt.CompletedFence;
                Assert.That(
                    submittedTargetOwner.LastSubmittedQueueFence,
                    Is.EqualTo(expectedTargetFence));

                if (useTransientCopyAliasing)
                {
                    Assert.That(
                        SpinWait.SpinUntil(() => {
                            foreach (var allocationGroup in observedTransientAllocationGroups)
                            {
                                foreach (var allocationLease in allocationGroup)
                                {
                                    if (allocationLease.IsReleased || (allocationLease.ReferenceCount != 1))
                                    {
                                        return false;
                                    }
                                }
                            }

                            return true;
                        }, TimeSpan.FromSeconds(5)),
                        Is.True);
                }

                if (useTransientBufferAliasing)
                {
                    Assert.That(
                        SpinWait.SpinUntil(() => {
                            foreach (var allocationGroup in observedTransientBufferAllocationGroups)
                            {
                                foreach (var allocationLease in allocationGroup)
                                {
                                    if (!allocationLease.IsReleased)
                                    {
                                        return false;
                                    }
                                }
                            }

                            return true;
                        }, TimeSpan.FromSeconds(5)),
                        Is.True);
                }
            }
            else if (simulateGraphFenceTimeout || simulateVulkanGraphFenceTimeout)
            {
                var submittedTarget = target
                    ?? throw new InvalidOperationException("The graph-timeout test has no imported color target.");
                var submittedTargetOwner = submittedTarget.ResourceOwner;
                var submissionTask = renderer.SubmitAsync(renderSnapshot, plan);

                if (backend == GraphicsBackend.D3D12)
                {
                    _ = Assert.ThrowsAsync<GraphicsDeviceLostException>(async () =>
                        await submissionTask.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false));
                }
                else
                {
                    _ = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                        await submissionTask.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false));
                }

                Assert.That(Volatile.Read(ref nativeGraphFenceWaitResult), Is.EqualTo(0));
                Assert.That(
                    graphicsDevice.Status is GraphicsDeviceStatus.Lost or GraphicsDeviceStatus.Stopping,
                    Is.True);
                Assert.That(submittedTargetOwner.HasActiveUses, Is.True);
                target = null;
                submittedTarget.Dispose();
                Assert.That(submittedTargetOwner.HasActiveUses, Is.True);

                var selectedHost = host
                    ?? throw new InvalidOperationException("The graph-timeout test lost its EngineHost.");
                host = null;
                var shutdownException = Assert.ThrowsAsync<AggregateException>(async () =>
                    await selectedHost.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false));
                Assert.That(
                    shutdownException?.ToString(),
                    Does.Contain(backend == GraphicsBackend.D3D12 ? "0x800705B4" : "VK_TIMEOUT"));
                Assert.That(graphicsDevice.Status, Is.EqualTo(GraphicsDeviceStatus.Stopping));
                return;
            }
            else
            {
                receipt = await renderer.SubmitAsync(renderSnapshot, plan).ConfigureAwait(false);
            }

            if (holdGraphCompletion)
            {
                Assert.That(receipt.CompletedFence, Is.GreaterThan(0));
                return;
            }

            var activeTarget = target
                ?? throw new InvalidOperationException("The graph witness lost its color target before the second submission.");
            RenderSubmissionReceipt? secondReceipt = null;

            using (var firstReadback = await graphicsDevice.ReadbackTextureAsync(
                activeTarget,
                new TextureSubresource(0, 0, TextureAspect.Color))
                .ConfigureAwait(false))
            {
                var firstPixel = new byte[targetBytesPerPixel];

                using (var readLease = firstReadback.AcquireReadLease())
                {
                    if (useRenderAreaClear)
                    {
                        var readbackBytes = new byte[checked((int)firstReadback.ByteLength)];
                        var copyAction = new ReadbackCopyAction(readbackBytes);
                        readLease.UseWindow(0, readbackBytes.Length, ref copyAction);
                        readbackBytes.AsSpan(0, firstPixel.Length).CopyTo(firstPixel);

                        for (var y = 0; y < firstReadback.Height; y++)
                        {
                            for (var x = 0; x < firstReadback.Width; x++)
                            {
                                var pixelOffset = checked((int)((firstReadback.RowPitch * y) + (x * targetBytesPerPixel)));
                                var isGreenPixel = (readbackBytes[pixelOffset] == 0)
                                    && (readbackBytes[pixelOffset + 1] == 255)
                                    && (readbackBytes[pixelOffset + 2] == 0)
                                    && (readbackBytes[pixelOffset + 3] == 255);
                                Assert.That(isGreenPixel, Is.EqualTo((x >= 1) && (x < 5) && (y >= 1) && (y < 5)));
                            }
                        }
                    }
                    else
                    {
                        var copyAction = new ReadbackCopyAction(firstPixel);
                        readLease.UseWindow(0, firstPixel.Length, ref copyAction);
                    }
                }

                Assert.Multiple(() => {
                    if (targetColorFormat == TextureFormat.R32UInt)
                    {
                        Assert.That(BitConverter.ToUInt32(firstPixel, 0), Is.Zero);
                    }
                    else if (targetColorFormat == TextureFormat.Rgba16Float)
                    {
                        Assert.That(firstPixel[0], Is.EqualTo(0x00));
                        Assert.That(firstPixel[1], Is.EqualTo(0x38));
                        Assert.That(firstPixel[2], Is.EqualTo(0x00));
                        Assert.That(firstPixel[3], Is.EqualTo(0x38));
                        Assert.That(firstPixel[4], Is.EqualTo(0x00));
                        Assert.That(firstPixel[5], Is.EqualTo(0x38));
                        Assert.That(firstPixel[6], Is.EqualTo(0x00));
                        Assert.That(firstPixel[7], Is.EqualTo(0x3C));
                    }
                    else if (targetColorFormat == TextureFormat.Rgba8Srgb)
                    {
                        Assert.That(firstPixel[0], Is.EqualTo(188));
                        Assert.That(firstPixel[1], Is.EqualTo(188));
                        Assert.That(firstPixel[2], Is.EqualTo(188));
                        Assert.That(firstPixel[3], Is.EqualTo(255));
                    }
                    else
                    {
                        Assert.That(firstPixel[0], Is.InRange(127, 129));
                        Assert.That(firstPixel[1], Is.InRange(127, 129));
                        Assert.That(firstPixel[2], Is.InRange(127, 129));
                        Assert.That(firstPixel[3], Is.EqualTo(255));
                    }

                    Assert.That(renderSnapshot.Cameras[0].ClearColor, Is.EqualTo(initialCameraClearColor));
                });
            }

            if (useTextureCopy)
            {
                var copiedTexture = copyDestinationTexture
                    ?? throw new InvalidOperationException("The graph texture-copy test has no destination texture.");
                var expectedBytes = expectedTextureCopyBytes
                    ?? throw new InvalidOperationException("The graph texture-copy test has no expected bytes.");

                using var copyTextureReadback = await graphicsDevice.ReadbackTextureAsync(
                    copiedTexture,
                    (useTransientTextureCopy || useTransientTextureAliasing || usePartialTextureCopy)
                        ? new TextureSubresource(0, 0, TextureAspect.Color)
                        : new TextureSubresource(1, 1, TextureAspect.Color))
                    .ConfigureAwait(false);
                var paddedBytes = new byte[checked((int)copyTextureReadback.ByteLength)];

                using (var copyTextureReadLease = copyTextureReadback.AcquireReadLease())
                {
                    var copyAction = new ReadbackCopyAction(paddedBytes);
                    copyTextureReadLease.UseWindow(0, paddedBytes.Length, ref copyAction);
                }

                for (var y = 0; y < copyTextureReadback.Height; y++)
                {
                    var actualRow = paddedBytes.AsSpan(
                        checked((int)(copyTextureReadback.RowPitch * y)),
                        checked((int)(copyTextureReadback.Width * 4)));
                    var expectedRow = expectedBytes.AsSpan(
                        checked((int)(copyTextureReadback.Width * 4 * y)),
                        checked((int)(copyTextureReadback.Width * 4)));
                    Assert.That(actualRow.ToArray(), Is.EqualTo(expectedRow.ToArray()));
                }

                if (useTransientCopyAliasing)
                {
                    var slotCopyDestinationTexture = copyDestinationTexture
                        ?? throw new InvalidOperationException("The slot-transient copy test has no imported destination texture.");
                    using var slotCopyReadback = await graphicsDevice.ReadbackTextureAsync(
                        slotCopyDestinationTexture,
                        new TextureSubresource(0, 0, TextureAspect.Color)).ConfigureAwait(false);
                    var slotPaddedBytes = new byte[checked((int)slotCopyReadback.ByteLength)];

                    using (var slotReadLease = slotCopyReadback.AcquireReadLease())
                    {
                        var slotCopyAction = new ReadbackCopyAction(slotPaddedBytes);
                        slotReadLease.UseWindow(0, slotPaddedBytes.Length, ref slotCopyAction);
                    }

                    var slotCenterRowOffset = checked((int)(slotCopyReadback.RowPitch * (slotCopyReadback.Height / 2)));
                    var slotCenterColumnOffset = checked(slotCopyReadback.Width / 2 * sizeof(uint));
                    var slotCenterPixelOffset = checked(slotCenterRowOffset + slotCenterColumnOffset);
                    var slotLastPixelOffset = checked((int)(slotCopyReadback.RowPitch * (slotCopyReadback.Height - 1))
                        + ((slotCopyReadback.Width - 1) * sizeof(uint)));

                    foreach (var pixelOffset in new[] { 0, slotCenterPixelOffset, slotLastPixelOffset })
                    {
                        for (var channel = 0; channel < 3; channel++)
                        {
                            Assert.That(slotPaddedBytes[pixelOffset + channel], Is.InRange(127, 129));
                        }

                        Assert.That(slotPaddedBytes[pixelOffset + 3], Is.EqualTo(255));
                    }
                }

                if (useMultipleTextureCopies)
                {
                    var secondCopiedTexture = secondCopyDestinationTexture
                        ?? throw new InvalidOperationException("The graph multi-texture-copy test has no second destination.");
                    var secondExpectedBytes = expectedSecondTextureCopyBytes
                        ?? throw new InvalidOperationException("The graph multi-texture-copy test has no second expected output.");
                    using var secondTextureReadback = await graphicsDevice.ReadbackTextureAsync(
                        secondCopiedTexture,
                        new TextureSubresource(
                            usePartialTextureCopy ? 0 : 1,
                            usePartialTextureCopy ? 0 : 1,
                            TextureAspect.Color))
                        .ConfigureAwait(false);
                    var secondPaddedBytes = new byte[checked((int)secondTextureReadback.ByteLength)];

                    using (var secondTextureReadLease = secondTextureReadback.AcquireReadLease())
                    {
                        var copyAction = new ReadbackCopyAction(secondPaddedBytes);
                        secondTextureReadLease.UseWindow(0, secondPaddedBytes.Length, ref copyAction);
                    }

                    for (var y = 0; y < secondTextureReadback.Height; y++)
                    {
                        var actualRow = secondPaddedBytes.AsSpan(
                            checked((int)(secondTextureReadback.RowPitch * y)),
                            checked((int)(secondTextureReadback.Width * 4)));
                        var expectedRow = secondExpectedBytes.AsSpan(
                            checked((int)(secondTextureReadback.Width * 4 * y)),
                            checked((int)(secondTextureReadback.Width * 4)));
                        Assert.That(actualRow.ToArray(), Is.EqualTo(expectedRow.ToArray()));
                    }
                }
            }

            var selectedWorld = world
                ?? throw new InvalidOperationException("The graph camera mutation has no live World.");
            await engineHost.InvokeAsync(_ => {
                var updatedCamera = CreateCameraComponent(new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
                selectedWorld.Set(cameraEntity, in updatedCamera);
                var publication = selectedWorld.Step().Publication;
                var extraction = renderer.Extract(
                    selectedWorld,
                    new RenderExtractionOptions3D(publication.Frame3, targetExtent, [cameraId]));

                if (extraction.Status != RenderExtractionStatus.Extracted)
                {
                    throw new InvalidOperationException(
                        $"{backend} updated graph camera extraction failed: {string.Join("; ", extraction.Diagnostics)}");
                }

                secondSnapshot = extraction.Snapshot
                    ?? throw new InvalidOperationException("The successful updated extraction omitted its snapshot.");
            }).ConfigureAwait(false);
            var updatedSnapshot = secondSnapshot
                ?? throw new InvalidOperationException("The updated camera extraction omitted its render snapshot.");
            Assert.That(renderSnapshot.Cameras[0].ClearColor, Is.EqualTo(initialCameraClearColor));
            Assert.That(updatedSnapshot.Cameras[0].ClearColor, Is.EqualTo(new Vector4(0.0f, 0.0f, 0.0f, 1.0f)));

            {
                var secondGraph = renderer.BuildGraph(updatedSnapshot, activeTarget);
                var secondBufferBindings = ImmutableArray.CreateBuilder<BufferBinding>(
                    (useUniformBuffer ? 1 : 0)
                    + (useMultipleFrameUniformBuffers ? 1 : 0)
                    + (useStorageResources ? 1 : 0));
                var secondTextureBindings = ImmutableArray.CreateBuilder<TextureBinding>(
                    (useSampledTexture ? 1 : 0)
                        + (useMultipleSampledImages ? 1 : 0)
                        + (useStorageResources ? 1 : 0)
                        + (useSixteenSampledImages ? sixteenSampledTextures.Length : 0));
                var secondSamplerBindings = ImmutableArray.CreateBuilder<SamplerBinding>(
                    (useSampledTexture ? 1 : 0)
                        + (useMultipleSampledImages ? 1 : 0)
                        + (useSixteenSampledImages ? 7 : 0));
                GraphBufferVersion? secondGraphStorageBufferInitialVersion = null;
                GraphTextureVersion? secondStorageImageInitialVersion = null;
                GraphTextureVersion? secondDepthAttachmentInitialVersion = null;
                var secondColorAttachmentInitialVersions =
                    new GraphTextureVersion?[secondaryColorAttachmentTextures.Length];
                var secondSixteenSampledTextureVersions = new GraphTextureVersion?[sixteenSampledTextures.Length];

                if (frameUniformBuffer is GpuBuffer secondSelectedBuffer)
                {
                    var importRange = new GraphBufferRange(0, secondSelectedBuffer.ByteLength);
                    secondFrameUniformVersion = secondGraph.ImportBuffer(
                        secondSelectedBuffer,
                        importRange,
                        new GraphImportOptions(
                            GraphResourceState.UniformRead,
                            contentsValid: true,
                            completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                    var bindingRange = frameUniformBindingRange ?? importRange;
                    secondBufferBindings.Add(new BufferBinding(
                        ShaderBindingGroup.Frame,
                        frameUniformBindingIndex,
                        ShaderResourceKind.UniformBuffer,
                        secondFrameUniformVersion,
                        bindingRange));
                }

                if (additionalFrameUniformBuffer is GpuBuffer secondSelectedAdditionalBuffer)
                {
                    var importRange = new GraphBufferRange(0, secondSelectedAdditionalBuffer.ByteLength);
                    secondAdditionalFrameUniformVersion = secondGraph.ImportBuffer(
                        secondSelectedAdditionalBuffer,
                        importRange,
                        new GraphImportOptions(
                            GraphResourceState.UniformRead,
                            contentsValid: true,
                            completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                    var bindingRange = additionalFrameUniformBindingRange ?? importRange;
                    secondBufferBindings.Add(new BufferBinding(
                        ShaderBindingGroup.Frame,
                        1,
                        ShaderResourceKind.UniformBuffer,
                        secondAdditionalFrameUniformVersion,
                        bindingRange));
                }

                if (sampledTexture is GpuTexture secondSampledTexture)
                {
                    secondSampledTextureVersion = secondGraph.ImportTexture(
                        secondSampledTexture,
                        sampledTextureBindingRange,
                        new GraphImportOptions(
                            GraphResourceState.ShaderSampledRead,
                            contentsValid: true,
                            completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                    secondTextureBindings.Add(new TextureBinding(
                        ShaderBindingGroup.Frame,
                        sampledImageBindingIndex,
                        ShaderResourceKind.SampledImage,
                        secondSampledTextureVersion,
                        sampledTextureBindingRange));
                    secondSamplerBindings.Add(new SamplerBinding(
                        ShaderBindingGroup.Frame,
                        sampledImageBindingIndex,
                        frameSampler
                            ?? throw new InvalidOperationException("The sampled Frame pipeline has no sampler.")));
                }

                if (additionalSampledTexture is GpuTexture secondAdditionalSampledTexture)
                {
                    secondAdditionalSampledTextureVersion = secondGraph.ImportTexture(
                            secondAdditionalSampledTexture,
                            sampledTextureBindingRange,
                            new GraphImportOptions(
                                GraphResourceState.ShaderSampledRead,
                                contentsValid: true,
                                completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                    secondTextureBindings.Add(new TextureBinding(
                            ShaderBindingGroup.Frame,
                            1,
                            ShaderResourceKind.SampledImage,
                            secondAdditionalSampledTextureVersion,
                            sampledTextureBindingRange));
                    secondSamplerBindings.Add(new SamplerBinding(
                            ShaderBindingGroup.Frame,
                            1,
                            frameSampler
                                ?? throw new InvalidOperationException("The sampled Frame pipeline has no sampler.")));
                }

                for (var index = 0; index < sixteenSampledTextures.Length; index++)
                {
                    if (sixteenSampledTextures[index] is not GpuTexture selectedTexture)
                    {
                        continue;
                    }

                    var imageIndex = index + 1;
                    var manySampledTextureVersion = secondGraph.ImportTexture(
                        selectedTexture,
                        sampledTextureRange,
                        new GraphImportOptions(
                                GraphResourceState.ShaderSampledRead,
                                contentsValid: true,
                                completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                    secondSixteenSampledTextureVersions[index] = manySampledTextureVersion;
                    secondTextureBindings.Add(new TextureBinding(
                        ShaderBindingGroup.Frame,
                        imageIndex,
                        ShaderResourceKind.SampledImage,
                        manySampledTextureVersion,
                        sampledTextureRange));

                    if ((imageIndex & 1) == 0)
                    {
                        secondSamplerBindings.Add(new SamplerBinding(
                                ShaderBindingGroup.Frame,
                                imageIndex / 2,
                                frameSampler
                                    ?? throw new InvalidOperationException("The sixteen-image Frame pipeline has no sampler.")));
                    }
                }

                if (useStorageResources && (storageBuffer is GpuBuffer selectedSecondGraphStorageBuffer))
                {
                    var range = new GraphBufferRange(0, selectedSecondGraphStorageBuffer.ByteLength);
                    secondGraphStorageBufferInitialVersion = secondGraph.ImportBuffer(
                        selectedSecondGraphStorageBuffer,
                        range,
                        new GraphImportOptions(
                            GraphResourceState.StorageReadWrite,
                            contentsValid: true,
                            completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                }

                if (useStorageResources && (storageImage is GpuTexture secondStorageImage))
                {
                    secondStorageImageInitialVersion = secondGraph.ImportTexture(
                        secondStorageImage,
                        storageImageBindingRange,
                        new GraphImportOptions(
                            GraphResourceState.StorageReadWrite,
                            contentsValid: true,
                            completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                }

                if (useDepthAttachment && (depthAttachmentTexture is GpuTexture secondDepthAttachmentTexture))
                {
                    secondDepthAttachmentInitialVersion = secondGraph.ImportTexture(
                        secondDepthAttachmentTexture,
                        new GraphTextureRange(0, 1, 0, 1, TextureAspect.Depth),
                        new GraphImportOptions(
                            GraphResourceState.DepthWrite,
                            contentsValid: true,
                            completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                }

                for (var index = 0; index < secondaryColorAttachmentTextures.Length; index++)
                {
                    if (useMultipleColorAttachments
                        && (secondaryColorAttachmentTextures[index] is GpuTexture secondColorAttachmentTexture))
                    {
                        secondColorAttachmentInitialVersions[index] = secondGraph.ImportTexture(
                            secondColorAttachmentTexture,
                            new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color),
                            new GraphImportOptions(
                                (useColorAttachmentCopy || useColorAttachmentCopyDestination)
                                && (index == 0)
                                    ? GraphResourceState.CopySource
                                    : GraphResourceState.ColorAttachment,
                                contentsValid: true,
                                completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                    }
                }

                var secondPass = secondGraph.AddRasterPass(
                    new RenderPassDescription("offscreen-clear-again", 0, [], hasExternalSideEffects: false),
                    RecordSecondClear);

                if (secondFrameUniformVersion is not null)
                {
                    var range = frameUniformBindingRange ?? secondFrameUniformVersion.Range;
                    secondGraph.ReadBuffer(
                        secondPass,
                        secondFrameUniformVersion,
                        GraphBufferUsage.UniformRead,
                        range);
                }

                if (secondAdditionalFrameUniformVersion is not null)
                {
                    secondGraph.ReadBuffer(
                        secondPass,
                        secondAdditionalFrameUniformVersion,
                        GraphBufferUsage.UniformRead,
                        secondAdditionalFrameUniformVersion.Range);
                }

                if (secondSampledTextureVersion is not null)
                {
                    secondGraph.ReadTexture(
                        secondPass,
                        secondSampledTextureVersion,
                        GraphTextureUsage.SampledRead,
                        secondSampledTextureVersion.Range);
                }

                if (secondAdditionalSampledTextureVersion is not null)
                {
                    secondGraph.ReadTexture(
                        secondPass,
                        secondAdditionalSampledTextureVersion,
                        GraphTextureUsage.SampledRead,
                        sampledTextureBindingRange);
                }

                for (var index = 0; index < secondSixteenSampledTextureVersions.Length; index++)
                {
                    if (secondSixteenSampledTextureVersions[index] is GraphTextureVersion selectedVersion)
                    {
                        secondGraph.ReadTexture(
                            secondPass,
                            selectedVersion,
                            GraphTextureUsage.SampledRead,
                            sampledTextureRange);
                    }
                }

                if (secondGraphStorageBufferInitialVersion is GraphBufferVersion storageBufferVersion)
                {
                    var secondStorageBufferWriteVersion = secondGraph.ReadWriteBuffer(
                        secondPass,
                        storageBufferVersion,
                        storageBufferVersion.Range);
                    secondBufferBindings.Add(new BufferBinding(
                        ShaderBindingGroup.Frame,
                        0,
                        ShaderResourceKind.StorageBuffer,
                        secondStorageBufferWriteVersion,
                        storageBufferVersion.Range));
                }

                if (secondStorageImageInitialVersion is GraphTextureVersion storageImageVersion)
                {
                    var secondStorageImageWriteVersion = secondGraph.ReadWriteTexture(
                        secondPass,
                        storageImageVersion,
                        storageImageBindingRange);
                    secondTextureBindings.Add(new TextureBinding(
                        ShaderBindingGroup.Frame,
                        1,
                        ShaderResourceKind.StorageImage,
                        secondStorageImageWriteVersion,
                        storageImageBindingRange));
                }

                if (useUniformBuffer
                    || useMultipleFrameUniformBuffers
                    || useSampledTexture
                    || useMultipleSampledImages
                    || useSixteenSampledImages
                    || useStorageResources)
                {
                    secondFrameBindingSet = new BindingSet(
                        secondBufferBindings.MoveToImmutable(),
                        secondTextureBindings.MoveToImmutable(),
                        secondSamplerBindings.MoveToImmutable());
                }

                var secondOutputVersion = secondGraph.WriteColorAttachment(
                    secondPass,
                    secondGraph.OffscreenColorTargetVersion,
                    new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color),
                    AttachmentLoadAction.Clear,
                    AttachmentStoreAction.Store,
                    updatedSnapshot.Cameras[0].ClearColor);

                for (var index = 0; index < secondColorAttachmentInitialVersions.Length; index++)
                {
                    if (secondColorAttachmentInitialVersions[index] is GraphTextureVersion selectedSecondColorAttachmentVersion)
                    {
                        _ = secondGraph.WriteColorAttachment(
                            secondPass,
                            selectedSecondColorAttachmentVersion,
                            selectedSecondColorAttachmentVersion.Range,
                            AttachmentLoadAction.Clear,
                            AttachmentStoreAction.Store,
                            new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
                    }
                }

                if (secondDepthAttachmentInitialVersion is GraphTextureVersion selectedSecondDepthVersion)
                {
                    _ = secondGraph.WriteDepthAttachment(
                        secondPass,
                        selectedSecondDepthVersion,
                        selectedSecondDepthVersion.Range,
                        useColorAndDepthAttachmentLoad
                            ? AttachmentLoadAction.Load
                            : AttachmentLoadAction.Clear,
                        AttachmentStoreAction.Store,
                        clearDepth: useColorAndDepthAttachmentLoad ? 1.0f : 0.0f);
                }

                secondGraph.SetPresentationSource(secondOutputVersion);
                var secondCompileResponse = secondGraph.Compile();
                var secondCompileDiagnosticMessage = secondCompileResponse.Diagnostics.IsEmpty
                    ? string.Empty
                    : secondCompileResponse.Diagnostics[0].Message;
                Assert.That(
                    secondCompileResponse.Status,
                    Is.EqualTo(RenderGraphCompileStatus.Compiled),
                    secondCompileDiagnosticMessage);
                var secondPlan = secondCompileResponse.Plan
                    ?? throw new InvalidOperationException("The second compiled graph response omitted its plan.");
                secondReceipt = await renderer.SubmitAsync(updatedSnapshot, secondPlan).ConfigureAwait(false);
            }

            if (useMultipleColorAttachments)
            {
                byte[][] expectedPixels = [
                    [0, 255, 255, 255],
                    [255, 255, 0, 255],
                    [255, 255, 255, 255],
                ];

                for (var index = 0; index < secondaryColorAttachmentTextures.Length; index++)
                {
                    var secondaryColorTexture = secondaryColorAttachmentTextures[index]
                        ?? throw new InvalidOperationException("The multiple-color-attachment test has an incomplete texture set.");
                    using var secondaryReadback = await graphicsDevice.ReadbackTextureAsync(
                        secondaryColorTexture,
                        new TextureSubresource(0, 0, TextureAspect.Color))
                        .ConfigureAwait(false);
                    var centerPixel = new byte[4];
                    var attachmentPixelOffset = checked(
                        (int)((secondaryReadback.RowPitch * (secondaryReadback.Height / 2))
                            + (secondaryReadback.Width / 2 * centerPixel.Length)));

                    using (var readLease = secondaryReadback.AcquireReadLease())
                    {
                        var copyAction = new ReadbackCopyAction(centerPixel);
                        readLease.UseWindow(attachmentPixelOffset, centerPixel.Length, ref copyAction);
                    }

                    Assert.That(centerPixel, Is.EqualTo(expectedPixels[index]));
                }
            }

            if (useAttachmentScopedRenderArea)
            {
                var selectedAttachment = oversizedAttachment
                    ?? throw new InvalidOperationException("The attachment-scoped render-area test lost its larger attachment.");
                using var attachmentReadback = await graphicsDevice.ReadbackTextureAsync(
                    selectedAttachment,
                    new TextureSubresource(0, 0, TextureAspect.Color)).ConfigureAwait(false);
                var attachmentPixel = new byte[4];
                var attachmentPixelOffset = checked(
                    (int)((attachmentReadback.RowPitch * (attachmentReadback.Height - 1))
                        + ((attachmentReadback.Width - 1) * attachmentPixel.Length)));

                using (var readLease = attachmentReadback.AcquireReadLease())
                {
                    var copyAction = new ReadbackCopyAction(attachmentPixel);
                    readLease.UseWindow(attachmentPixelOffset, attachmentPixel.Length, ref copyAction);
                }

                Assert.That(attachmentPixel, Is.EqualTo(new byte[] { 0, 255, 255, byte.MaxValue }));

                var selectedDepthAttachment = oversizedDepthAttachment
                    ?? throw new InvalidOperationException("The attachment-scoped render-area test lost its larger depth attachment.");
                var (depthState, depthContentsValid) =
                    selectedDepthAttachment.ResourceOwner.GetGraphImportState(
                        new GraphTextureRange(0, 1, 0, 1, TextureAspect.Depth));
                Assert.Multiple(() => {
                    Assert.That(depthState, Is.EqualTo(GraphResourceState.DepthWrite));
                    Assert.That(depthContentsValid, Is.True);
                });
            }

            if (useColorAttachmentCopy || useColorAttachmentCopyDestination)
            {
                var copiedColorAttachment = copyDestinationTexture
                    ?? throw new InvalidOperationException("The graph color-attachment copy has no destination texture.");
                using var colorAttachmentCopyReadback = await graphicsDevice.ReadbackTextureAsync(
                    copiedColorAttachment,
                    new TextureSubresource(0, 0, TextureAspect.Color))
                    .ConfigureAwait(false);
                var centerPixel = new byte[4];
                var colorCopyCenterPixelOffset = checked(
                    (int)((colorAttachmentCopyReadback.RowPitch * (colorAttachmentCopyReadback.Height / 2))
                        + (colorAttachmentCopyReadback.Width / 2 * centerPixel.Length)));

                using (var readLease = colorAttachmentCopyReadback.AcquireReadLease())
                {
                    var copyAction = new ReadbackCopyAction(centerPixel);
                    readLease.UseWindow(colorCopyCenterPixelOffset, centerPixel.Length, ref copyAction);
                }

                Assert.That(centerPixel, Is.EqualTo(new byte[] { 0, 255, 255, 255 }));
            }

            Assert.Multiple(() => {
                Assert.That(receipt.SubmissionSequence, Is.GreaterThan(0));
                Assert.That(receipt.CompletedFence, Is.GreaterThan(0));
                Assert.That(receipt.PresentationSequence, Is.Null);
                Assert.That(receipt.DeviceEpoch, Is.EqualTo(graphicsDevice.DeviceEpoch));
                Assert.That(receipt.SurfaceEpoch, Is.Zero);
                Assert.That(receipt.SwapchainEpoch, Is.Zero);

                if (secondReceipt is RenderSubmissionReceipt completedSecondReceipt)
                {
                    Assert.That(completedSecondReceipt.SubmissionSequence, Is.EqualTo(receipt.SubmissionSequence + 1));
                    Assert.That(completedSecondReceipt.CompletedFence, Is.GreaterThan(receipt.CompletedFence));
                }
                else
                {
                    Assert.Fail("The graph witness omitted its second submission.");
                }
            });

            using var readback = await graphicsDevice.ReadbackTextureAsync(
                activeTarget,
                new TextureSubresource(0, 0, TextureAspect.Color))
                .ConfigureAwait(false);
            var padded = new byte[checked((int)readback.ByteLength)];

            using (var readLease = readback.AcquireReadLease())
            {
                var copyAction = new ReadbackCopyAction(padded);
                readLease.UseWindow(0, padded.Length, ref copyAction);
            }

            var centerPixelOffset = checked(
                (int)((readback.RowPitch * (targetExtent.Height / 2)) + (targetExtent.Width / 2 * targetBytesPerPixel)));
            var drawPixelCount = 0;
            var (expectedSampledRed, expectedSampledGreen, expectedSampledBlue) = sampledTextureFormat switch {
                TextureFormat.R8Unorm or TextureFormat.R32Float or TextureFormat.D32Float
                    => (Red: (byte)128, Green: (byte)0, Blue: (byte)0),
                TextureFormat.Rgba16Float
                    => (Red: (byte)255, Green: (byte)128, Blue: (byte)64),
                TextureFormat.Rgba8Srgb
                    => (Red: (byte)55, Green: (byte)13, Blue: (byte)4),
                _ => (Red: (byte)32, Green: (byte)128, Blue: (byte)224),
            };

            if ((targetColorFormat == TextureFormat.Rgba8Srgb)
                && (sampledTextureFormat == TextureFormat.Rgba8Srgb))
            {
                expectedSampledRed = 128;
                expectedSampledGreen = 64;
                expectedSampledBlue = 32;
            }

            var expectedRed = useStorageResources
                ? (byte)255
                : useMultipleSampledImages
                    ? (byte)48
                    : useSixteenSampledImages
                        ? (byte)120
                    : useFrameLayoutUniformBuffer
                        ? (byte)255
                    : useMultipleFrameUniformBuffers
                        ? (byte)128
                    : (useSampledTexture ? expectedSampledRed : (useUniformBuffer ? (byte)0 : (byte)255));
            var expectedGreen = useStorageResources
                ? (byte)255
                : useMultipleSampledImages
                    ? (byte)80
                    : useSixteenSampledImages
                        ? (byte)135
                    : useFrameLayoutUniformBuffer
                        ? (byte)255
                    : useMultipleFrameUniformBuffers
                        ? (byte)128
                    : (useSampledTexture ? expectedSampledGreen : (useUniformBuffer ? (byte)255 : (byte)0));
            var expectedBlue = useStorageResources
                ? (byte)255
                : useMultipleSampledImages
                    ? (byte)128
                    : useSixteenSampledImages
                        ? (byte)60
                    : useFrameLayoutUniformBuffer
                        ? (byte)255
                    : useMultipleFrameUniformBuffers
                        ? (byte)128
                    : (useSampledTexture ? expectedSampledBlue : (useUniformBuffer ? (byte)0 : (byte)255));
            var expectedBackgroundRed = 0;
            var expectedBackgroundGreen = 0;
            var expectedBackgroundBlue = 0;

            for (var y = 0; y < readback.Height; y++)
            {
                for (var x = 0; x < readback.Width; x++)
                {
                    var pixelOffset = checked((int)((readback.RowPitch * y) + (x * targetBytesPerPixel)));

                    var isDrawPixel = (targetColorFormat == TextureFormat.R32UInt)
                        ? (BitConverter.ToUInt32(padded, pixelOffset) == 0x12345678u)
                        : (targetColorFormat == TextureFormat.Rgba16Float)
                        ? (padded[pixelOffset] == 0x00)
                            && (padded[pixelOffset + 1] == 0x3C)
                            && (padded[pixelOffset + 2] == 0x00)
                            && (padded[pixelOffset + 3] == 0x38)
                            && (padded[pixelOffset + 4] == 0x00)
                            && (padded[pixelOffset + 5] == 0x34)
                            && (padded[pixelOffset + 6] == 0x00)
                            && (padded[pixelOffset + 7] == 0x3C)
                        : (padded[pixelOffset] == expectedRed)
                            && (padded[pixelOffset + 1] == expectedGreen)
                            && (padded[pixelOffset + 2] == expectedBlue)
                            && (padded[pixelOffset + 3] == 255);

                    if (isDrawPixel)
                    {
                        drawPixelCount++;
                    }
                }
            }

            Assert.Multiple(() => {
                Assert.That(padded[0], Is.InRange(expectedBackgroundRed - 1, expectedBackgroundRed + 1));
                Assert.That(padded[1], Is.InRange(expectedBackgroundGreen - 1, expectedBackgroundGreen + 1));
                Assert.That(padded[2], Is.InRange(expectedBackgroundBlue - 1, expectedBackgroundBlue + 1));
                Assert.That(
                    padded[targetColorFormat == TextureFormat.Rgba16Float ? 7 : 3],
                    Is.EqualTo(
                        targetColorFormat == TextureFormat.Rgba16Float
                            ? 0x3C
                            : (targetColorFormat == TextureFormat.R32UInt ? 0 : 255)));

                if (pipeline is not null)
                {
                    Assert.That(drawPixelCount, Is.GreaterThan(0));

                    if (targetColorFormat == TextureFormat.R32UInt)
                    {
                        Assert.That(BitConverter.ToUInt32(padded, centerPixelOffset), Is.EqualTo(0x12345678u));
                    }
                }
                else
                {
                    Assert.That(padded[centerPixelOffset], Is.EqualTo(expectedBackgroundRed));
                    Assert.That(padded[centerPixelOffset + 1], Is.EqualTo(expectedBackgroundGreen));
                    Assert.That(padded[centerPixelOffset + 2], Is.EqualTo(expectedBackgroundBlue));
                }
            });

            if (useStorageResources)
            {
                var storageBufferForReadback = storageBuffer
                    ?? throw new InvalidOperationException("The graph storage test has no storage buffer.");
                using var storageBufferReadback = await graphicsDevice.ReadbackBufferAsync(
                    storageBufferForReadback,
                    0,
                    sizeof(uint))
                    .ConfigureAwait(false);
                var storageBufferBytes = new byte[sizeof(uint)];

                using (var bufferReadLease = storageBufferReadback.AcquireReadLease(0, storageBufferBytes.Length))
                {
                    var copyAction = new ReadbackCopyAction(storageBufferBytes);
                    bufferReadLease.UseWindow(0, storageBufferBytes.Length, ref copyAction);
                }

                Assert.That(
                    BitConverter.ToUInt32(storageBufferBytes, 0),
                    Is.EqualTo(6u));

                var storageImageForReadback = storageImage
                    ?? throw new InvalidOperationException("The graph storage test has no storage image.");
                using var storageImageReadback = await graphicsDevice.ReadbackTextureAsync(
                    storageImageForReadback,
                    new TextureSubresource(storageImageMipLevel, 0, TextureAspect.Color))
                    .ConfigureAwait(false);
                var storageImageBytes = new byte[checked((int)storageImageReadback.ByteLength)];

                using (var imageReadLease = storageImageReadback.AcquireReadLease())
                {
                    var copyAction = new ReadbackCopyAction(storageImageBytes);
                    imageReadLease.UseWindow(0, storageImageBytes.Length, ref copyAction);
                }

                Assert.That(
                    BitConverter.ToUInt32(storageImageBytes, 0),
                    Is.EqualTo(6u));
            }

            if (useCompute)
            {
                var computeStorageBuffer = storageBuffer
                    ?? throw new InvalidOperationException("The graph compute test has no storage buffer.");
                using var computeReadback = await graphicsDevice.ReadbackBufferAsync(
                    computeStorageBuffer,
                    0,
                    sizeof(uint))
                    .ConfigureAwait(false);
                var computeStorageBytes = new byte[sizeof(uint)];

                using (var computeReadLease = computeReadback.AcquireReadLease(0, computeStorageBytes.Length))
                {
                    var copyAction = new ReadbackCopyAction(computeStorageBytes);
                    computeReadLease.UseWindow(0, computeStorageBytes.Length, ref copyAction);
                }

                Assert.That(
                    BitConverter.ToUInt32(computeStorageBytes, 0),
                    Is.EqualTo(useMultipleComputeStorageImages
                        ? 0x10203040u
                        : useFloatStorageImage || useRgba16FloatStorageImage
                            ? 0u
                            : 2u));

                var computeStorageImage = storageImage
                    ?? throw new InvalidOperationException("The graph compute test has no storage image.");
                using var computeImageReadback = await graphicsDevice.ReadbackTextureAsync(
                    computeStorageImage,
                    new TextureSubresource(storageImageMipLevel, 0, TextureAspect.Color))
                    .ConfigureAwait(false);
                var computeStorageImageBytes = new byte[checked((int)computeImageReadback.ByteLength)];

                using (var computeImageReadLease = computeImageReadback.AcquireReadLease())
                {
                    var copyAction = new ReadbackCopyAction(computeStorageImageBytes);
                    computeImageReadLease.UseWindow(0, computeStorageImageBytes.Length, ref copyAction);
                }

                if (use3DStorageImage)
                {
                    Assert.That(computeImageReadback.Depth, Is.EqualTo(2));
                    Assert.That(BitConverter.ToUInt32(computeStorageImageBytes, 0), Is.Zero);
                    Assert.That(
                        BitConverter.ToUInt32(
                            computeStorageImageBytes,
                            checked((int)computeImageReadback.SlicePitch)),
                        Is.EqualTo(2u));
                }
                else if (useFloatStorageImage)
                {
                    Assert.That(BitConverter.ToSingle(computeStorageImageBytes, 0), Is.EqualTo(1.0f));
                }
                else if (useRgba16FloatStorageImage)
                {
                    Assert.That((float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(computeStorageImageBytes, 0)), Is.EqualTo(1.0f));
                    Assert.That((float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(computeStorageImageBytes, sizeof(ushort))), Is.EqualTo(2.0f));
                    Assert.That((float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(computeStorageImageBytes, sizeof(ushort) * 2)), Is.EqualTo(3.0f));
                    Assert.That((float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(computeStorageImageBytes, sizeof(ushort) * 3)), Is.EqualTo(4.0f));
                }
                else
                {
                    Assert.That(
                        BitConverter.ToUInt32(computeStorageImageBytes, 0),
                        Is.EqualTo(useMultipleComputeStorageImages ? 0x11223344u : 2u));
                }

                if (useMultipleComputeStorageImages)
                {
                    var secondComputeStorageImage = additionalStorageImage
                        ?? throw new InvalidOperationException("The graph multi-image compute test has no second storage image.");
                    using var secondComputeImageReadback = await graphicsDevice.ReadbackTextureAsync(
                        secondComputeStorageImage,
                        new TextureSubresource(useStorageImageSubrange ? 1 : 0, 0, TextureAspect.Color))
                        .ConfigureAwait(false);
                    var secondComputeStorageImageBytes = new byte[checked((int)secondComputeImageReadback.ByteLength)];

                    using (var secondComputeImageReadLease = secondComputeImageReadback.AcquireReadLease())
                    {
                        var copyAction = new ReadbackCopyAction(secondComputeStorageImageBytes);
                        secondComputeImageReadLease.UseWindow(0, secondComputeStorageImageBytes.Length, ref copyAction);
                    }

                    Assert.That(BitConverter.ToUInt32(secondComputeStorageImageBytes, 0), Is.EqualTo(0x55667788u));
                }

                if (useStorageImageSubrange)
                {
                    var expectedBaseMipBytes = storageImageBaseMipBytes
                        ?? throw new InvalidOperationException("The storage-image subrange test has no mip-0 sentinel.");
                    using var baseMipReadback = await graphicsDevice.ReadbackTextureAsync(
                        computeStorageImage,
                        new TextureSubresource(0, 0, TextureAspect.Color))
                        .ConfigureAwait(false);
                    var baseMipReadbackBytes = new byte[checked((int)baseMipReadback.ByteLength)];
                    var expectedBaseMipReadbackBytes = new byte[baseMipReadbackBytes.Length];
                    var baseMipRowByteLength = checked((int)(baseMipReadback.Width * storageImageBytesPerTexel));

                    for (var row = 0; row < baseMipReadback.Height; row++)
                    {
                        expectedBaseMipBytes.AsSpan(
                            checked(row * baseMipRowByteLength),
                            baseMipRowByteLength)
                            .CopyTo(expectedBaseMipReadbackBytes.AsSpan(
                                checked((int)(baseMipReadback.RowPitch * row)),
                                baseMipRowByteLength));
                    }

                    using (var baseMipReadLease = baseMipReadback.AcquireReadLease())
                    {
                        var copyAction = new ReadbackCopyAction(baseMipReadbackBytes);
                        baseMipReadLease.UseWindow(0, baseMipReadbackBytes.Length, ref copyAction);
                    }

                    Assert.That(baseMipReadbackBytes, Is.EqualTo(expectedBaseMipReadbackBytes));

                    if (useMultipleComputeStorageImages)
                    {
                        var expectedAdditionalBaseMipBytes = additionalStorageImageBaseMipBytes
                            ?? throw new InvalidOperationException("The multiple storage-image subrange test has no second mip-0 sentinel.");
                        var additionalStorageImageForReadback = additionalStorageImage
                            ?? throw new InvalidOperationException("The multiple storage-image subrange test has no second storage image.");
                        using var additionalBaseMipReadback = await graphicsDevice.ReadbackTextureAsync(
                            additionalStorageImageForReadback,
                            new TextureSubresource(0, 0, TextureAspect.Color))
                            .ConfigureAwait(false);
                        var additionalBaseMipReadbackBytes = new byte[checked((int)additionalBaseMipReadback.ByteLength)];
                        var expectedAdditionalBaseMipReadbackBytes = new byte[additionalBaseMipReadbackBytes.Length];
                        var additionalBaseMipRowByteLength = checked(
                            (int)(additionalBaseMipReadback.Width * sizeof(uint)));

                        for (var row = 0; row < additionalBaseMipReadback.Height; row++)
                        {
                            expectedAdditionalBaseMipBytes.AsSpan(
                                checked(row * additionalBaseMipRowByteLength),
                                additionalBaseMipRowByteLength)
                                .CopyTo(expectedAdditionalBaseMipReadbackBytes.AsSpan(
                                    checked((int)(additionalBaseMipReadback.RowPitch * row)),
                                    additionalBaseMipRowByteLength));
                        }

                        using (var additionalBaseMipReadLease = additionalBaseMipReadback.AcquireReadLease())
                        {
                            var copyAction = new ReadbackCopyAction(additionalBaseMipReadbackBytes);
                            additionalBaseMipReadLease.UseWindow(
                                0,
                                additionalBaseMipReadbackBytes.Length,
                                ref copyAction);
                        }

                        Assert.That(
                            additionalBaseMipReadbackBytes,
                            Is.EqualTo(expectedAdditionalBaseMipReadbackBytes));
                    }
                }
            }

            if (useTransientStorage)
            {
                var transientStorageImage = storageImage
                    ?? throw new InvalidOperationException("The transient graph compute test has no storage image.");
                using var transientImageReadback = await graphicsDevice.ReadbackTextureAsync(
                    transientStorageImage,
                    new TextureSubresource(0, 0, TextureAspect.Color))
                    .ConfigureAwait(false);
                var transientImageBytes = new byte[checked((int)transientImageReadback.ByteLength)];

                using (var transientImageReadLease = transientImageReadback.AcquireReadLease())
                {
                    var copyAction = new ReadbackCopyAction(transientImageBytes);
                    transientImageReadLease.UseWindow(0, transientImageBytes.Length, ref copyAction);
                }

                Assert.That(BitConverter.ToUInt32(transientImageBytes, 0), Is.EqualTo(2u));
            }

            if (useMultipleComputeStorageBuffers)
            {
                var computeStorageBuffer = storageBuffer
                    ?? throw new InvalidOperationException("The graph multi-buffer compute test has no first storage buffer.");
                using var firstComputeReadback = await graphicsDevice.ReadbackBufferAsync(
                    computeStorageBuffer,
                    useStorageBufferSubrange ? storageBufferBindingOffset : 0,
                    sizeof(uint))
                    .ConfigureAwait(false);
                var firstComputeBytes = new byte[sizeof(uint)];

                using (var firstComputeReadLease = firstComputeReadback.AcquireReadLease(0, firstComputeBytes.Length))
                {
                    var copyAction = new ReadbackCopyAction(firstComputeBytes);
                    firstComputeReadLease.UseWindow(0, firstComputeBytes.Length, ref copyAction);
                }

                Assert.That(BitConverter.ToUInt32(firstComputeBytes, 0), Is.EqualTo(2u));

                var secondComputeStorageBuffer = secondStorageBuffer
                    ?? throw new InvalidOperationException("The graph multi-buffer compute test has no second storage buffer.");
                using var secondComputeReadback = await graphicsDevice.ReadbackBufferAsync(
                    secondComputeStorageBuffer,
                    useStorageBufferSubrange ? secondStorageBufferBindingOffset : 0,
                    sizeof(uint))
                    .ConfigureAwait(false);
                var secondComputeBytes = new byte[sizeof(uint)];

                using (var secondComputeReadLease = secondComputeReadback.AcquireReadLease(0, secondComputeBytes.Length))
                {
                    var copyAction = new ReadbackCopyAction(secondComputeBytes);
                    secondComputeReadLease.UseWindow(0, secondComputeBytes.Length, ref copyAction);
                }

                Assert.That(BitConverter.ToUInt32(secondComputeBytes, 0), Is.EqualTo(4u));
            }

            if (useBufferCopy
                || useCompute
                || useTransientStorage
                || useMultipleComputeStorageBuffers
                || useTransientBufferAliasing
                || useConstantWriteComputeBuffer)
            {
                var destinationBufferForReadback = copyDestinationBuffer
                    ?? throw new InvalidOperationException("The graph copy test has no destination buffer.");
                var expectedBytes = expectedCopiedBytes
                    ?? throw new InvalidOperationException("The graph copy test has no expected bytes.");
                using var copyReadback = await graphicsDevice.ReadbackBufferAsync(
                    destinationBufferForReadback,
                    useStorageBufferSubrange ? copyDestinationRangeOffset : 0,
                    expectedBytes.Length)
                    .ConfigureAwait(false);
                var copiedBytes = new byte[expectedBytes.Length];

                using (var copyReadLease = copyReadback.AcquireReadLease(0, copiedBytes.Length))
                {
                    var copyAction = new ReadbackCopyAction(copiedBytes);
                    copyReadLease.UseWindow(0, copiedBytes.Length, ref copyAction);
                }

                Assert.That(copiedBytes, Is.EqualTo(expectedBytes));

                if (useManyBufferCopyDestinations)
                {
                    for (var index = 0; index < additionalCopyDestinationBuffers.Length; index++)
                    {
                        var additionalDestination = additionalCopyDestinationBuffers[index]
                            ?? throw new InvalidOperationException("The graph buffer-import test has an incomplete destination set.");
                        using var additionalReadback = await graphicsDevice.ReadbackBufferAsync(
                            additionalDestination,
                            0,
                            expectedBytes.Length)
                            .ConfigureAwait(false);
                        var additionalCopiedBytes = new byte[expectedBytes.Length];

                        using (var additionalReadLease = additionalReadback.AcquireReadLease(0, additionalCopiedBytes.Length))
                        {
                            var copyAction = new ReadbackCopyAction(additionalCopiedBytes);
                            additionalReadLease.UseWindow(0, additionalCopiedBytes.Length, ref copyAction);
                        }

                        Assert.That(additionalCopiedBytes, Is.EqualTo(expectedBytes));
                    }
                }

                if (useStorageBufferSubrange)
                {
                    var storageBufferForReadback = storageBuffer
                        ?? throw new InvalidOperationException("The subrange graph has no storage buffer.");
                    using var prefixReadback = await graphicsDevice.ReadbackBufferAsync(
                        storageBufferForReadback,
                        0,
                        sizeof(uint))
                        .ConfigureAwait(false);
                    var prefixBytes = new byte[sizeof(uint)];

                    using (var prefixReadLease = prefixReadback.AcquireReadLease(0, prefixBytes.Length))
                    {
                        var copyAction = new ReadbackCopyAction(prefixBytes);
                        prefixReadLease.UseWindow(0, prefixBytes.Length, ref copyAction);
                    }

                    Assert.That(BitConverter.ToUInt32(prefixBytes, 0), Is.EqualTo(0x56473829u));

                    var storageBufferPrefixOffset = checked(storageBufferBindingOffset - sizeof(uint));
                    using var storageBindingPrefixReadback = await graphicsDevice.ReadbackBufferAsync(
                        storageBufferForReadback,
                        storageBufferPrefixOffset,
                        sizeof(uint))
                        .ConfigureAwait(false);
                    var storageBindingPrefixBytes = new byte[sizeof(uint)];

                    using (var storageBindingPrefixReadLease = storageBindingPrefixReadback.AcquireReadLease(0, storageBindingPrefixBytes.Length))
                    {
                        var copyAction = new ReadbackCopyAction(storageBindingPrefixBytes);
                        storageBindingPrefixReadLease.UseWindow(0, storageBindingPrefixBytes.Length, ref copyAction);
                    }

                    Assert.That(BitConverter.ToUInt32(storageBindingPrefixBytes, 0), Is.EqualTo(0xA6B7C8D9u));

                    if (useMultipleComputeStorageBuffers)
                    {
                        var secondStorageBufferForReadback = secondStorageBuffer
                            ?? throw new InvalidOperationException("The multi-buffer subrange graph has no second storage buffer.");
                        using var secondStoragePrefixReadback = await graphicsDevice.ReadbackBufferAsync(
                            secondStorageBufferForReadback,
                            0,
                            sizeof(uint))
                            .ConfigureAwait(false);
                        var secondStoragePrefixBytes = new byte[sizeof(uint)];

                        using (var secondStoragePrefixReadLease = secondStoragePrefixReadback.AcquireReadLease(
                            0,
                            secondStoragePrefixBytes.Length))
                        {
                            var copyAction = new ReadbackCopyAction(secondStoragePrefixBytes);
                            secondStoragePrefixReadLease.UseWindow(
                                0,
                                secondStoragePrefixBytes.Length,
                                ref copyAction);
                        }

                        Assert.That(BitConverter.ToUInt32(secondStoragePrefixBytes, 0), Is.EqualTo(0x6B7C8D9Eu));

                        var secondStorageBufferPrefixOffset = checked(secondStorageBufferBindingOffset - sizeof(uint));
                        using var secondStorageBindingPrefixReadback = await graphicsDevice.ReadbackBufferAsync(
                            secondStorageBufferForReadback,
                            secondStorageBufferPrefixOffset,
                            sizeof(uint))
                            .ConfigureAwait(false);
                        var secondStorageBindingPrefixBytes = new byte[sizeof(uint)];

                        using (var secondStorageBindingPrefixReadLease = secondStorageBindingPrefixReadback.AcquireReadLease(
                            0,
                            secondStorageBindingPrefixBytes.Length))
                        {
                            var copyAction = new ReadbackCopyAction(secondStorageBindingPrefixBytes);
                            secondStorageBindingPrefixReadLease.UseWindow(
                                0,
                                secondStorageBindingPrefixBytes.Length,
                                ref copyAction);
                        }

                        Assert.That(BitConverter.ToUInt32(secondStorageBindingPrefixBytes, 0), Is.EqualTo(0x8394A5B6u));
                    }

                    using var destinationPrefixReadback = await graphicsDevice.ReadbackBufferAsync(
                        destinationBufferForReadback,
                        0,
                        sizeof(uint))
                        .ConfigureAwait(false);
                    var destinationPrefixBytes = new byte[sizeof(uint)];

                    using (var destinationPrefixReadLease = destinationPrefixReadback.AcquireReadLease(0, destinationPrefixBytes.Length))
                    {
                        var copyAction = new ReadbackCopyAction(destinationPrefixBytes);
                        destinationPrefixReadLease.UseWindow(0, destinationPrefixBytes.Length, ref copyAction);
                    }

                    Assert.That(BitConverter.ToUInt32(destinationPrefixBytes, 0), Is.EqualTo(0x29384756u));

                    var copyDestinationPrefixOffset = checked(copyDestinationRangeOffset - sizeof(uint));
                    using var copyDestinationRangePrefixReadback = await graphicsDevice.ReadbackBufferAsync(
                        destinationBufferForReadback,
                        copyDestinationPrefixOffset,
                        sizeof(uint))
                        .ConfigureAwait(false);
                    var copyDestinationRangePrefixBytes = new byte[sizeof(uint)];

                    using (var copyDestinationRangePrefixReadLease = copyDestinationRangePrefixReadback.AcquireReadLease(
                        0,
                        copyDestinationRangePrefixBytes.Length))
                    {
                        var copyAction = new ReadbackCopyAction(copyDestinationRangePrefixBytes);
                        copyDestinationRangePrefixReadLease.UseWindow(
                            0,
                            copyDestinationRangePrefixBytes.Length,
                            ref copyAction);
                    }

                    Assert.That(BitConverter.ToUInt32(copyDestinationRangePrefixBytes, 0), Is.EqualTo(0xD9C8B7A6u));
                }

                if (useMultipleComputeStorageBuffers || useTransientBufferAliasing)
                {
                    var secondDestinationBufferForReadback = secondCopyDestinationBuffer
                        ?? throw new InvalidOperationException("The graph multi-buffer compute test has no second destination buffer.");
                    var secondExpectedBytes = expectedSecondCopiedBytes
                        ?? throw new InvalidOperationException("The graph multi-buffer compute test has no second expected output.");
                    using var secondCopyReadback = await graphicsDevice.ReadbackBufferAsync(
                        secondDestinationBufferForReadback,
                        0,
                        secondExpectedBytes.Length)
                        .ConfigureAwait(false);
                    var secondCopiedBytes = new byte[secondExpectedBytes.Length];

                    using (var secondCopyReadLease = secondCopyReadback.AcquireReadLease(0, secondCopiedBytes.Length))
                    {
                        var copyAction = new ReadbackCopyAction(secondCopiedBytes);
                        secondCopyReadLease.UseWindow(0, secondCopiedBytes.Length, ref copyAction);
                    }

                    Assert.That(secondCopiedBytes, Is.EqualTo(secondExpectedBytes));
                }
            }

            var finalReceipt = secondReceipt ?? receipt;
            TestContext.WriteLine(
                $"Backend={backend}; Adapter={graphicsDevice.Capabilities.AdapterName}; "
                + $"Submissions={receipt.SubmissionSequence}/{finalReceipt.SubmissionSequence}; "
                + $"Fence={finalReceipt.CompletedFence}; "
                + $"UniformBuffer={useUniformBuffer}; "
                + $"SampledTexture={useSampledTexture}; "
                + $"SixteenSampledImages={useSixteenSampledImages}; "
                + $"DepthAttachment={useDepthAttachment}; "
                + $"MultipleColorAttachments={useMultipleColorAttachments}; "
                + $"StorageResources={useStorageResources}; "
                + $"BufferCopy={useBufferCopy || useCompute || useManyBufferCopyDestinations || useMultipleComputeStorageBuffers || useTransientBufferAliasing || useConstantWriteComputeBuffer}; "
                + $"TextureCopy={useTextureCopy}; "
                + $"TransientTextureCopy={useTransientTextureCopy}; "
                + $"TransientTextureAliasing={useTransientTextureAliasing}; "
                + $"MultipleTextureCopies={useMultipleTextureCopies}; "
                + $"Compute={useCompute || useConstantWriteComputeBuffer}; "
                + $"MultipleComputeStorageBuffers={useMultipleComputeStorageBuffers}; "
                + $"TransientBufferAliasing={useTransientBufferAliasing}; "
                + $"OverlappingTransientLifetimes={overlapTransientBufferLifetimes}; "
                + $"CameraClear={renderSnapshot.Cameras[0].ClearColor}->{updatedSnapshot.Cameras[0].ClearColor}; "
                + $"TargetFormat={targetColorFormat}; "
                + $"Pixel={Convert.ToHexString(padded.AsSpan(centerPixelOffset, targetBytesPerPixel))}; "
                + $"DrawPixels={drawPixelCount}");
        }
        finally
        {
            continueFenceWait.Set();
            snapshot?.Dispose();
            secondSnapshot?.Dispose();
            pipeline?.Dispose();
            computePipeline?.Dispose();
            frameSampler?.Dispose();
            frameUniformBuffer?.Dispose();
            additionalFrameUniformBuffer?.Dispose();
            storageBuffer?.Dispose();
            secondStorageBuffer?.Dispose();
            copySourceBuffer?.Dispose();
            secondCopySourceBuffer?.Dispose();
            copyDestinationBuffer?.Dispose();

            foreach (var additionalCopyDestinationBuffer in additionalCopyDestinationBuffers)
            {
                additionalCopyDestinationBuffer?.Dispose();
            }

            secondCopyDestinationBuffer?.Dispose();
            copySourceTexture?.Dispose();
            copyDestinationTexture?.Dispose();
            secondCopySourceTexture?.Dispose();
            secondCopyDestinationTexture?.Dispose();

            if (sampledTextureResidencyLease is not null)
            {
                sampledTextureResidencyLease.Dispose();
            }
            else
            {
                sampledTexture?.Dispose();
            }

            additionalSampledTexture?.Dispose();

            foreach (var sixteenSampledTexture in sixteenSampledTextures)
            {
                sixteenSampledTexture?.Dispose();
            }

            storageImage?.Dispose();
            additionalStorageImage?.Dispose();

            foreach (var secondaryColorAttachmentTexture in secondaryColorAttachmentTextures)
            {
                secondaryColorAttachmentTexture?.Dispose();
            }

            oversizedAttachment?.Dispose();
            oversizedDepthAttachment?.Dispose();
            depthAttachmentTexture?.Dispose();
            target?.Dispose();
            delayedGraphTarget?.Dispose();
            delayedGraphDestination?.Dispose();

            if (host is not null)
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }

            File.Delete(packagePath);
        }
    }
}
