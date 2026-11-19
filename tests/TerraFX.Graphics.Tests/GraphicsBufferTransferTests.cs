// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Buffers;
using TerraFX.Graphics;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class GraphicsBufferTransferTests
{
    private static BufferDescription CreateBufferDescription()
        => new(
            byteLength: 8,
            BufferUsage.CopySource | BufferUsage.CopyDestination,
            GpuMemoryKind.DeviceLocal,
            structuredStride: null,
            debugName: "Buffer transfer test");

    private static GraphicsGraphSubmission CreateBufferImportIndexSubmission(
        GraphicsDevice device,
        GpuTexture target,
        ImmutableArray<GraphBufferImportRecord> imports,
        long maximumPreflightWorkspaceBytes,
        ImmutableArray<RenderPassId> orderedPasses = default,
        ImmutableArray<GraphTextureImportRecord> additionalTextureImports = default,
        ImmutableArray<GraphResourceAliasingBarrier> aliasingBarriers = default,
        ImmutableArray<GraphTextureAliasingBarrier> textureAliasingBarriers = default,
        ImmutableArray<GraphResourceAccess> accesses = default,
        ImmutableArray<GraphicsGraphSubmissionPass> submissionPasses = default)
    {
        var version = new GraphTextureVersion(
            device.DeviceEpoch, 1, 0, target.Description, new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color));
        var textureImports = ImmutableArray.CreateBuilder<GraphTextureImportRecord>(
            1 + (additionalTextureImports.IsDefault ? 0 : additionalTextureImports.Length));
        textureImports.Add(new GraphTextureImportRecord(
            target,
            version,
            new GraphImportOptions(GraphResourceState.Undefined, false, GraphImportCompletionPolicy.RequireIdle)));

        if (!additionalTextureImports.IsDefault)
        {
            textureImports.AddRange(additionalTextureImports);
        }

        return new GraphicsGraphSubmission(
            device.DeviceEpoch,
            target,
            surfaceTarget: null,
            version,
            surfaceEpoch: 0,
            swapchainEpoch: 0,
            orderedPasses.IsDefault ? [] : orderedPasses,
            submissionPasses.IsDefault ? [] : submissionPasses,
            [],
            aliasingBarriers.IsDefault ? [] : aliasingBarriers,
            textureAliasingBarriers.IsDefault ? [] : textureAliasingBarriers,
            accesses.IsDefault ? [] : accesses,
            imports,
            textureImports.MoveToImmutable(),
            [],
            maximumPreflightWorkspaceBytes: maximumPreflightWorkspaceBytes);
    }

    private static GraphicsCapabilities CreateCapabilities(bool includeShaderModel6 = true, bool includeUploadHeap = false)
    {
        ImmutableArray<GraphicsFeature> features = includeShaderModel6
            ? [GraphicsFeature.Direct3DFeatureLevel12, GraphicsFeature.ShaderModel6]
            : [GraphicsFeature.Direct3DFeatureLevel12];
        const TextureUsage CopyUsages = TextureUsage.CopySource | TextureUsage.CopyDestination;
        var formats = ImmutableArray.Create(
            new GraphicsFormatCapabilities(TextureFormat.Rgba8Unorm, TextureUsage.Sampled | TextureUsage.ColorAttachment | CopyUsages, [1]),
            new GraphicsFormatCapabilities(TextureFormat.Rgba8Srgb, TextureUsage.Sampled | TextureUsage.ColorAttachment | CopyUsages, [1]),
            new GraphicsFormatCapabilities(TextureFormat.Rgba16Float, TextureUsage.Sampled | TextureUsage.Storage | TextureUsage.ColorAttachment | CopyUsages, [1]),
            new GraphicsFormatCapabilities(TextureFormat.R8Unorm, TextureUsage.Sampled | CopyUsages, [1]),
            new GraphicsFormatCapabilities(TextureFormat.R32Float, TextureUsage.Sampled | TextureUsage.Storage | CopyUsages, [1]),
            new GraphicsFormatCapabilities(TextureFormat.R32UInt, TextureUsage.Storage | TextureUsage.ColorAttachment | CopyUsages, [1]),
            new GraphicsFormatCapabilities(TextureFormat.D32Float, TextureUsage.DepthAttachment | TextureUsage.Sampled | CopyUsages, [1]));
        var limits = new GraphicsLimits(
            maximumTextureDimension2D: 16384,
            maximumTextureDimension3D: 2048,
            maximumArrayLayers: 2048,
            maximumColorAttachments: 8,
            maximumSampledImagesPerStage: 128,
            maximumSampledImagesPerPipeline: 64,
            maximumSamplersPerStage: 32,
            maximumSamplersPerPipeline: 32,
            maximumSamplerAnisotropy: 1.0f,
            maximumUniformBuffersPerStage: 16,
            maximumUniformBuffersPerPipeline: 16,
            maximumStorageBuffersPerStage: 32,
            maximumStorageImagesPerStage: 32,
            maximumStorageBuffersPerPipeline: 32,
            maximumStorageImagesPerPipeline: 32,
            maximumStorageResourcesPerPipeline: 32,
            maximumResourcesPerStage: 144,
            maximumUniformBufferRangeBytes: 65536,
            uniformBufferOffsetAlignment: 256,
            storageBufferOffsetAlignment: 16,
            bufferCopyOffsetAlignment: 16,
            textureRowPitchAlignment: 256,
            texturePlacementAlignment: 512,
            maximumComputeWorkGroupSizeX: 1024,
            maximumComputeWorkGroupSizeY: 1024,
            maximumComputeWorkGroupSizeZ: 64,
            maximumComputeWorkGroupCountX: 65535,
            maximumComputeWorkGroupCountY: 65535,
            maximumComputeWorkGroupCountZ: 65535,
            maximumComputeWorkGroupInvocations: 1024,
            maximumVertexInputAttributes: 32,
            maximumVertexInputBindings: 32,
            maximumBufferByteLength: 1L << 40);

        var heaps = ImmutableArray.CreateBuilder<GraphicsMemoryHeapCapabilities>(includeUploadHeap ? 2 : 1);
        heaps.Add(new GraphicsMemoryHeapCapabilities(
            0,
            GraphicsMemoryHeapProperties.DeviceLocal,
            4L * 1024 * 1024 * 1024,
            3L * 1024 * 1024 * 1024));

        if (includeUploadHeap)
        {
            heaps.Add(new GraphicsMemoryHeapCapabilities(
                1,
                GraphicsMemoryHeapProperties.HostVisible | GraphicsMemoryHeapProperties.HostCoherent,
                4L * 1024 * 1024 * 1024,
                3L * 1024 * 1024 * 1024));
        }

        return new GraphicsCapabilities(
            GraphicsBackend.D3D12,
            GraphicsDeviceProfile.OffscreenNative,
            new GraphicsAdapterId(GraphicsBackend.D3D12, [0, 1, 2, 3, 4, 5, 6, 7]),
            vendorId: 1,
            deviceId: 1,
            adapterName: "Buffer transfer test adapter",
            driverName: "Buffer transfer test driver",
            driverVersion: "1",
            new GraphicsApiVersion(12, 0, 0, 0),
            features,
            features,
            limits,
            formats,
            heaps.MoveToImmutable());
    }

    private static async Task<GraphicsDevice> CreateGraphicsDeviceAsync(
        FakeBackendDevice backendDevice,
        int frameSlotCount = 3)
        => await GraphicsDevice.CreateAsync(
            ownerId: 31,
            new GraphicsBackendRegistration(new FakeBackendFactory(backendDevice)),
            GraphicsDeviceProfile.OffscreenNative,
            frameSlotCount,
            GraphicsPlatformKind.None,
            CancellationToken.None).ConfigureAwait(false);

    private static SamplerDescription CreateSamplerDescription()
        => new(
            SamplerFilter.Linear,
            SamplerFilter.Linear,
            SamplerFilter.Linear,
            SamplerAddressMode.Repeat,
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            maximumAnisotropy: 8.0f);

    private static TextureDescription CreateTextureDescription()
        => new(
            TextureDimension.Dimension2D,
            width: 3,
            height: 2,
            depth: 1,
            arrayLayers: 1,
            mipLevels: 1,
            TextureFormat.Rgba8Unorm,
            TextureUsage.Sampled | TextureUsage.CopySource | TextureUsage.CopyDestination,
            debugName: "Texture transfer test");

    private static TextureDescription CreateVolumeTextureDescription()
        => new(
            TextureDimension.Dimension3D,
            width: 2,
            height: 2,
            depth: 2,
            arrayLayers: 1,
            mipLevels: 1,
            TextureFormat.R8Unorm,
            TextureUsage.CopySource | TextureUsage.CopyDestination,
            debugName: "Volume texture transfer test");

    [Test]
    [SuppressMessage(
        "Usage",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "This test disposes the source buffer during an admitted readback to verify its independent use lease.")]
    public async Task AdmittedBufferReadbackRetainsSourceUntilBackendCompletion()
    {
        using var readbackWaitEntered = new ManualResetEventSlim(initialState: false);
        using var continueReadback = new ManualResetEventSlim(initialState: false);
        using var cancellationSource = new CancellationTokenSource();
        var backendDevice = new FakeBackendDevice(readbackWaitEntered, continueReadback, isReadbackWait: true);
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var source = await graphicsDevice.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);
        var readbackTask = graphicsDevice.ReadbackBufferAsync(
            source,
            sourceOffset: 0,
            byteLength: 1,
            cancellationSource.Token);

        try
        {
            Assert.That(readbackWaitEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            await cancellationSource.CancelAsync().ConfigureAwait(false);
            source.Dispose();

            Assert.That(readbackTask.IsCompleted, Is.False);
            Assert.That(backendDevice.BufferDisposalCount, Is.Zero);

            continueReadback.Set();
            _ = Assert.ThrowsAsync<TaskCanceledException>(
                async () => await readbackTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(backendDevice.BufferDisposalCount, Is.EqualTo(1));
        }
        finally
        {
            continueReadback.Set();
            source.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    [SuppressMessage(
        "Usage",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "This test disposes caller owners while an admitted texture transfer is blocked to verify its independent credits.")]
    public async Task AdmittedTextureUploadRetainsSourceAndDestinationUntilBackendCompletion()
    {
        using var continueUpload = new ManualResetEventSlim(initialState: false);
        using var uploadEntered = new ManualResetEventSlim(initialState: false);
        using var cancellationSource = new CancellationTokenSource();
        var backendDevice = new FakeBackendDevice(uploadEntered, continueUpload);
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var texture = await graphicsDevice.CreateTextureAsync(CreateTextureDescription()).ConfigureAwait(false);
        var sourceOwner = UnmanagedBuffer<byte>.Allocate(32, new BufferAllocationOptions(32, 1));
        sourceOwner.CopyFrom(new byte[32], 0);
        var sourceLease = sourceOwner.AcquireRead(0, 32);
        var sourceLeaseDisposed = false;
        var sourceOwnerDisposed = false;
        var uploadTask = graphicsDevice.UploadTextureAsync(
            texture,
            new TextureSubresource(0, 0, TextureAspect.Color),
            sourceLease,
            sourceOffset: 0,
            new TextureDataLayout(rowPitch: 12, slicePitch: 24),
            cancellationSource.Token);

        try
        {
            Assert.That(uploadEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            sourceLease.Dispose();
            sourceLeaseDisposed = true;
            sourceOwner.Dispose();
            sourceOwnerDisposed = true;
            texture.Dispose();
            await cancellationSource.CancelAsync().ConfigureAwait(false);

            Assert.That(uploadTask.IsCompleted, Is.False);
            Assert.That(backendDevice.TextureDisposalCount, Is.Zero);

            continueUpload.Set();
            _ = Assert.ThrowsAsync<TaskCanceledException>(
                async () => await uploadTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(backendDevice.TextureDisposalCount, Is.EqualTo(1));
        }
        finally
        {
            continueUpload.Set();

            if (!sourceLeaseDisposed)
            {
                sourceLease.Dispose();
            }

            if (!sourceOwnerDisposed)
            {
                sourceOwner.Dispose();
            }

            texture.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    [SuppressMessage(
        "Usage",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "This test disposes caller owners while an admitted transfer is blocked to verify its independent credits.")]
    public async Task AdmittedUploadRetainsSourceAndDestinationUntilBackendCompletion()
    {
        using var continueUpload = new ManualResetEventSlim(initialState: false);
        using var uploadEntered = new ManualResetEventSlim(initialState: false);
        using var cancellationSource = new CancellationTokenSource();
        var backendDevice = new FakeBackendDevice(uploadEntered, continueUpload);
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var destination = await graphicsDevice.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);
        var sourceOwner = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(4, 1));
        sourceOwner.CopyFrom([9, 8, 7, 6], 0);
        var sourceLease = sourceOwner.AcquireRead(0, 4);
        var sourceLeaseDisposed = false;
        var sourceOwnerDisposed = false;
        var uploadTask = graphicsDevice.UploadBufferAsync(
            destination,
            destinationOffset: 0,
            sourceLease,
            sourceOffset: 0,
            byteLength: 4,
            cancellationSource.Token);

        try
        {
            Assert.That(uploadEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            sourceLease.Dispose();
            sourceLeaseDisposed = true;
            sourceOwner.Dispose();
            sourceOwnerDisposed = true;
            destination.Dispose();
            await cancellationSource.CancelAsync().ConfigureAwait(false);

            Assert.That(uploadTask.IsCompleted, Is.False);
            Assert.That(backendDevice.BufferDisposalCount, Is.Zero);

            continueUpload.Set();
            _ = Assert.ThrowsAsync<TaskCanceledException>(
                async () => await uploadTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(backendDevice.BufferDisposalCount, Is.EqualTo(1));
            Assert.That(backendDevice.LastBufferBytes, Is.EqualTo(new byte[] { 9, 8, 7, 6, 0, 0, 0, 0 }));
        }
        finally
        {
            continueUpload.Set();

            if (!sourceLeaseDisposed)
            {
                sourceLease.Dispose();
            }

            if (!sourceOwnerDisposed)
            {
                sourceOwner.Dispose();
            }

            destination.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task BufferAllocationBudgetAcceptsExactPerDeviceLimit()
    {
        var backendDevice = new FakeBackendDevice(bufferAllocationSizeBytes: 512L * 1024 * 1024);
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        GpuBuffer? buffer = null;

        try
        {
            buffer = await graphicsDevice.CreateBufferAsync(CreateBufferDescription())
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            Assert.That(backendDevice.BufferCreationCount, Is.EqualTo(1));
            _ = Assert.ThrowsAsync<OutOfMemoryException>(
                async () => await graphicsDevice.CreateBufferAsync(CreateBufferDescription())
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false));
            Assert.That(backendDevice.BufferCreationCount, Is.EqualTo(1));

            buffer.Dispose();
            buffer = null;
            Assert.That(
                SpinWait.SpinUntil(() => backendDevice.BufferDisposalCount == 1, TimeSpan.FromSeconds(5)),
                Is.True);

            buffer = await graphicsDevice.CreateBufferAsync(CreateBufferDescription())
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            Assert.That(backendDevice.BufferCreationCount, Is.EqualTo(2));
        }
        finally
        {
            buffer?.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }

        Assert.That(backendDevice.BufferDisposalCount, Is.EqualTo(2));
    }

    [Test]
    public async Task BufferAllocationBudgetRejectsBeforeBackendCreation()
    {
        var backendDevice = new FakeBackendDevice(bufferAllocationSizeBytes: (512L * 1024 * 1024) + 1);
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);

        try
        {
            _ = Assert.ThrowsAsync<OutOfMemoryException>(
                async () => await graphicsDevice.CreateBufferAsync(CreateBufferDescription())
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false));

            Assert.That(backendDevice.BufferAllocationCreationCount, Is.Zero);
            Assert.That(backendDevice.BufferCreationCount, Is.Zero);
            Assert.That(graphicsDevice.Status, Is.EqualTo(GraphicsDeviceStatus.Ready));
        }
        finally
        {
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task BufferAllocationReservationRecoversAfterBackendCreationFailure()
    {
        const int failureCount = 257;
        const long exactBudgetBytes = 512L * 1024 * 1024;
        var backendDevice = new FakeBackendDevice(
            bufferAllocationSizeBytes: exactBudgetBytes,
            bufferCreationFailureCount: failureCount);
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        GpuBuffer? buffer = null;

        try
        {
            for (var index = 0; index < failureCount; index++)
            {
                _ = Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await graphicsDevice.CreateBufferAsync(CreateBufferDescription()).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
            }

            buffer = await graphicsDevice.CreateBufferAsync(CreateBufferDescription()).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(backendDevice.BufferCreationCount, Is.EqualTo(failureCount + 1));
        }
        finally
        {
            buffer?.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }

        Assert.That(backendDevice.BufferDisposalCount, Is.EqualTo(1));
    }

    [Test]
    public async Task BufferNativeHeapBudgetAcceptsExactAllocationSize()
    {
        var bufferDescription = CreateBufferDescription();
        var backendDevice = new FakeBackendDevice(bufferBudgetBytes: bufferDescription.ByteLength);
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        GpuBuffer? buffer = null;

        try
        {
            buffer = await graphicsDevice.CreateBufferAsync(bufferDescription)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            Assert.That(backendDevice.BufferCreationCount, Is.EqualTo(1));
            _ = Assert.ThrowsAsync<OutOfMemoryException>(
                async () => await graphicsDevice.CreateBufferAsync(bufferDescription)
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false));
            Assert.That(backendDevice.BufferCreationCount, Is.EqualTo(1));

            buffer.Dispose();
            buffer = null;
            Assert.That(
                SpinWait.SpinUntil(() => backendDevice.BufferDisposalCount == 1, TimeSpan.FromSeconds(5)),
                Is.True);

            buffer = await graphicsDevice.CreateBufferAsync(bufferDescription)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            Assert.That(backendDevice.BufferCreationCount, Is.EqualTo(2));
        }
        finally
        {
            buffer?.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }

        Assert.That(backendDevice.BufferDisposalCount, Is.EqualTo(2));
    }

    [Test]
    public async Task BufferNativeHeapBudgetRejectsBeforeBackendCreation()
    {
        var backendDevice = new FakeBackendDevice(bufferBudgetBytes: CreateBufferDescription().ByteLength - 1);
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);

        try
        {
            _ = Assert.ThrowsAsync<OutOfMemoryException>(
                async () => await graphicsDevice.CreateBufferAsync(CreateBufferDescription())
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false));

            Assert.That(backendDevice.BufferAllocationCreationCount, Is.Zero);
            Assert.That(backendDevice.BufferCreationCount, Is.Zero);
        }
        finally
        {
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    [SuppressMessage(
        "Usage",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The queued readbacks retain their own source-use leases; shutdown joins pending reads on failure paths.")]
    public async Task BufferReadbackCompletionAdmissionIsBounded()
    {
        const int maximumReadbackCount = 256;
        using var uploadEntered = new ManualResetEventSlim(initialState: false);
        using var continueUpload = new ManualResetEventSlim(initialState: false);
        using var readbackWaitEntered = new ManualResetEventSlim(initialState: false);
        using var continueReadback = new ManualResetEventSlim(initialState: false);
        var backendDevice = new FakeBackendDevice(
            uploadEntered,
            continueUpload,
            readbackWaitEntered,
            continueReadback);
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var uploadBlocker = await graphicsDevice.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);
        var readbackSource = await graphicsDevice.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);
        var readbackTasks = new Task<OwnedBuffer<byte>>[maximumReadbackCount];

        try
        {
            var blockingUpload = graphicsDevice.UploadBufferAsync(uploadBlocker, destinationOffset: 0, [0x42]);
            Assert.That(uploadEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);

            for (var index = 0; index < readbackTasks.Length; index++)
            {
                readbackTasks[index] = graphicsDevice.ReadbackBufferAsync(
                    readbackSource,
                    sourceOffset: 0,
                    byteLength: 1);
            }

            var overflowReadback = graphicsDevice.ReadbackBufferAsync(
                readbackSource,
                sourceOffset: 0,
                byteLength: 1);
            Assert.That(overflowReadback.IsCompleted, Is.False);
            continueUpload.Set();
            _ = await blockingUpload.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(
                SpinWait.SpinUntil(
                    () => (backendDevice.ReadbackBeginCount == maximumReadbackCount)
                        && readbackWaitEntered.IsSet,
                    TimeSpan.FromSeconds(5)),
                Is.True);
            Assert.That(overflowReadback.IsCompleted, Is.False);

            continueReadback.Set();

            for (var index = 0; index < readbackTasks.Length; index++)
            {
                using var readback = await readbackTasks[index]
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                Assert.That(readback.ByteLength, Is.EqualTo((nint)1));
            }

            using var overflowResult = await overflowReadback.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(overflowResult.ByteLength, Is.EqualTo((nint)1));
            Assert.That(backendDevice.ReadbackBeginCount, Is.EqualTo(maximumReadbackCount + 1));
        }
        finally
        {
            continueUpload.Set();
            continueReadback.Set();
            uploadBlocker.Dispose();
            readbackSource.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task BufferReadbackWaitRunsOffOwnerWhileOwnerProcessesRequests()
    {
        using var readbackWaitEntered = new ManualResetEventSlim(initialState: false);
        using var continueReadback = new ManualResetEventSlim(initialState: false);
        var backendDevice = new FakeBackendDevice(readbackWaitEntered, continueReadback, isReadbackWait: true);
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var source = await graphicsDevice.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);
        GpuBuffer? ownerResponsiveBuffer = null;

        try
        {
            var readbackTask = graphicsDevice.ReadbackBufferAsync(source, sourceOffset: 0, byteLength: 1);
            Assert.That(readbackWaitEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(readbackTask.IsCompleted, Is.False);

            ownerResponsiveBuffer = await graphicsDevice.CreateBufferAsync(CreateBufferDescription())
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);

            Assert.That(readbackTask.IsCompleted, Is.False);
            continueReadback.Set();
            using var readback = await readbackTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(readback.ByteLength, Is.EqualTo((nint)1));
            Assert.That(backendDevice.ReadbackWaitThreadId, Is.Not.EqualTo(backendDevice.ReadbackBeginThreadId));
            Assert.That(backendDevice.ReadbackCopyThreadId, Is.EqualTo(backendDevice.ReadbackBeginThreadId));
        }
        finally
        {
            continueReadback.Set();
            source.Dispose();
            ownerResponsiveBuffer?.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task ConcurrentTransientBufferGroupsRespectBudgetAndRecoverAfterUseRelease()
    {
        const int liveGroupCount = 4;
        const long allocationSizeBytes = 16;
        const long exactBudgetBytes = liveGroupCount * allocationSizeBytes;
        var backendDevice = new FakeBackendDevice(
            bufferAllocationSizeBytes: allocationSizeBytes,
            bufferBudgetBytes: exactBudgetBytes);
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var registry = new GraphResourceRegistry(graphicsDevice, graphOwnerId: 7);
        var description = new BufferDescription(
            byteLength: allocationSizeBytes,
            BufferUsage.Storage,
            GpuMemoryKind.DeviceLocal,
            structuredStride: null,
            debugName: "Concurrent transient buffer group test");
        var versions = ImmutableArray.Create(
            registry.CreateTransientBuffer(description),
            registry.CreateTransientBuffer(description));
        var groups = new List<GraphicsTransientBufferGroup>(liveGroupCount + 1);
        var uses = new GraphicsResourceOwner.UseLease?[liveGroupCount];

        try
        {
            for (var index = 0; index < liveGroupCount; index++)
            {
                var group = await graphicsDevice.CreateTransientBufferGroupAsync(
                        versions,
                        CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                groups.Add(group);
                uses[index] = group.Buffers[0].ResourceOwner.AcquireUse(nameof(groups));
            }

            var allocationLeases = new HashSet<GraphicsResourceAllocationLease>();

            foreach (var group in groups)
            {
                var allocationLease = group.Buffers[0].ResourceOwner.AllocationLease
                    ?? throw new InvalidOperationException("A live transient buffer group lost its shared allocation lease.");
                Assert.That(allocationLeases.Add(allocationLease), Is.True);
                Assert.That(group.Buffers[1].ResourceOwner.AllocationLease, Is.SameAs(allocationLease));
            }

            foreach (var group in groups)
            {
                group.Dispose();
            }

            Assert.That(
                SpinWait.SpinUntil(() => backendDevice.BufferDisposalCount == liveGroupCount, TimeSpan.FromSeconds(5)),
                Is.True);
            Assert.That(backendDevice.BufferAllocationDisposalCount, Is.Zero);
            _ = Assert.ThrowsAsync<OutOfMemoryException>(
                async () => await graphicsDevice.CreateTransientBufferGroupAsync(
                        versions,
                        CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false));

            uses[1]?.Dispose();
            uses[1] = null;
            Assert.That(
                SpinWait.SpinUntil(() => backendDevice.BufferAllocationDisposalCount == 1, TimeSpan.FromSeconds(5)),
                Is.True);

            var replacementGroup = await graphicsDevice.CreateTransientBufferGroupAsync(
                    versions,
                    CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            groups.Add(replacementGroup);
            var replacementAllocationLease = replacementGroup.Buffers[0].ResourceOwner.AllocationLease
                ?? throw new InvalidOperationException("The replacement transient buffer group lost its shared allocation lease.");
            Assert.That(allocationLeases.Contains(replacementAllocationLease), Is.False);
            replacementGroup.Dispose();
            Assert.That(
                SpinWait.SpinUntil(() => backendDevice.BufferAllocationDisposalCount == 2, TimeSpan.FromSeconds(5)),
                Is.True);

            for (var index = 0; index < uses.Length; index++)
            {
                uses[index]?.Dispose();
                uses[index] = null;
            }

            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(backendDevice.BufferAllocationCreationCount, Is.EqualTo(liveGroupCount + 1));
            Assert.That(backendDevice.BufferAllocationDisposalCount, Is.EqualTo(liveGroupCount + 1));
            Assert.That(backendDevice.BufferDisposalCount, Is.EqualTo((liveGroupCount + 1) * 2));
        }
        finally
        {
            foreach (var use in uses)
            {
                use?.Dispose();
            }

            foreach (var group in groups)
            {
                group.Dispose();
            }

            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task ConcurrentTransientTextureGroupsRespectBudgetAndRecoverAfterUseRelease()
    {
        const int liveGroupCount = 4;
        const long allocationSizeBytes = 16;
        const long exactBudgetBytes = liveGroupCount * allocationSizeBytes;
        var backendDevice = new FakeBackendDevice(
            textureBudgetBytes: exactBudgetBytes,
            textureAllocationSizeBytes: allocationSizeBytes);
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var registry = new GraphResourceRegistry(graphicsDevice, graphOwnerId: 7);
        var description = new TextureDescription(
            TextureDimension.Dimension2D,
            width: 2,
            height: 2,
            depth: 1,
            arrayLayers: 1,
            mipLevels: 1,
            TextureFormat.Rgba8Unorm,
            TextureUsage.Sampled | TextureUsage.CopySource | TextureUsage.CopyDestination,
            debugName: "Concurrent transient texture group test");
        var versions = ImmutableArray.Create(
            registry.CreateTransientTexture(description),
            registry.CreateTransientTexture(description));
        var groups = new List<GraphicsTransientTextureGroup>(liveGroupCount + 1);
        var uses = new GraphicsResourceOwner.UseLease?[liveGroupCount];
        GpuTexture? budgetProbe = null;

        try
        {
            for (var index = 0; index < liveGroupCount; index++)
            {
                var group = await graphicsDevice.CreateTransientTextureGroupAsync(
                        versions,
                        CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                groups.Add(group);
                uses[index] = group.Textures[0].ResourceOwner.AcquireUse(nameof(groups));
            }

            var allocationLeases = new HashSet<GraphicsResourceAllocationLease>();

            foreach (var group in groups)
            {
                var allocationLease = group.Textures[0].ResourceOwner.AllocationLease
                    ?? throw new InvalidOperationException("A live transient texture group lost its shared allocation lease.");
                Assert.That(allocationLeases.Add(allocationLease), Is.True);
                Assert.That(group.Textures[1].ResourceOwner.AllocationLease, Is.SameAs(allocationLease));
            }

            foreach (var group in groups)
            {
                group.Dispose();
            }

            Assert.That(
                SpinWait.SpinUntil(() => backendDevice.TextureDisposalCount == liveGroupCount, TimeSpan.FromSeconds(5)),
                Is.True);
            Assert.That(backendDevice.TextureAllocationDisposalCount, Is.Zero);
            _ = Assert.ThrowsAsync<OutOfMemoryException>(
                async () => await graphicsDevice.CreateTransientTextureGroupAsync(
                        versions,
                        CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false));

            uses[1]?.Dispose();
            uses[1] = null;
            Assert.That(
                SpinWait.SpinUntil(() => backendDevice.TextureDisposalCount == (liveGroupCount + 1), TimeSpan.FromSeconds(5)),
                Is.True);

            var replacementGroup = await graphicsDevice.CreateTransientTextureGroupAsync(
                    versions,
                    CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            groups.Add(replacementGroup);
            var replacementAllocationLease = replacementGroup.Textures[0].ResourceOwner.AllocationLease
                ?? throw new InvalidOperationException("The replacement transient texture group lost its shared allocation lease.");
            Assert.That(allocationLeases.Contains(replacementAllocationLease), Is.True);
            Assert.That(backendDevice.TextureAllocationCreationCount, Is.EqualTo(liveGroupCount));
            Assert.That(backendDevice.TextureAllocationDisposalCount, Is.Zero);
            replacementGroup.Dispose();
            Assert.That(
                SpinWait.SpinUntil(
                    () => backendDevice.TextureDisposalCount == (liveGroupCount + 3),
                    TimeSpan.FromSeconds(5)),
                Is.True);

            budgetProbe = await graphicsDevice.CreateTextureAsync(description)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            Assert.That(backendDevice.TextureAllocationDisposalCount, Is.EqualTo(1));
            budgetProbe.Dispose();
            budgetProbe = null;
            Assert.That(
                SpinWait.SpinUntil(
                    () => backendDevice.TextureDisposalCount == (liveGroupCount + 4),
                    TimeSpan.FromSeconds(5)),
                Is.True);

            for (var index = 0; index < uses.Length; index++)
            {
                uses[index]?.Dispose();
                uses[index] = null;
            }

            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(backendDevice.TextureAllocationCreationCount, Is.EqualTo(liveGroupCount));
            Assert.That(backendDevice.TextureAllocationDisposalCount, Is.EqualTo(liveGroupCount));
            Assert.That(backendDevice.TextureDisposalCount, Is.EqualTo(((liveGroupCount + 1) * 2) + 1));
        }
        finally
        {
            budgetProbe?.Dispose();

            foreach (var use in uses)
            {
                use?.Dispose();
            }

            foreach (var group in groups)
            {
                group.Dispose();
            }

            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task GraphAliasingAfterIndexesUseExactOwnedCapacity()
    {
        var device = await CreateGraphicsDeviceAsync(new FakeBackendDevice()).ConfigureAwait(false);

        try
        {
            using var target = await device.CreateTextureAsync(CreateTextureDescription()).ConfigureAwait(false);
            var pass = new RenderPassId(9, 3);
            var bufferDescription = CreateBufferDescription();
            var beforeBufferVersion = new GraphBufferVersion(
                device.DeviceEpoch,
                20,
                0,
                bufferDescription,
                new GraphBufferRange(0, bufferDescription.ByteLength));
            var duplicateBeforeBufferVersion = new GraphBufferVersion(
                device.DeviceEpoch,
                21,
                0,
                bufferDescription,
                new GraphBufferRange(0, bufferDescription.ByteLength));
            var afterBufferVersion = new GraphBufferVersion(
                device.DeviceEpoch,
                30,
                0,
                bufferDescription,
                new GraphBufferRange(0, bufferDescription.ByteLength));
            var bufferAliasingBarriers = ImmutableArray.Create(
                new GraphResourceAliasingBarrier(pass, beforeBufferVersion, afterBufferVersion),
                new GraphResourceAliasingBarrier(pass, duplicateBeforeBufferVersion, afterBufferVersion));
            var textureDescription = target.Description;
            var textureRange = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);
            var beforeTextureVersion = new GraphTextureVersion(
                device.DeviceEpoch, 40, 0, textureDescription, textureRange);
            var duplicateBeforeTextureVersion = new GraphTextureVersion(
                device.DeviceEpoch, 41, 0, textureDescription, textureRange);
            var afterTextureVersion = new GraphTextureVersion(
                device.DeviceEpoch, 50, 0, textureDescription, textureRange);
            var textureAliasingBarriers = ImmutableArray.Create(
                new GraphTextureAliasingBarrier(pass, beforeTextureVersion, afterTextureVersion),
                new GraphTextureAliasingBarrier(pass, duplicateBeforeTextureVersion, afterTextureVersion));
            var preflightIndexCharge = GraphicsGraphSubmission.CalculatePreflightWorkspaceBackingByteCharge(
                bufferImportCapacity: 0,
                textureImportCapacity: 1,
                orderedPassCapacity: 1,
                bufferAliasingCapacity: 2,
                textureAliasingCapacity: 2,
                bufferImportFlagCapacity: 0,
                textureImportFlagCapacity: 1,
                passValidationCapacity: 0);
            var submission = CreateBufferImportIndexSubmission(
                device,
                target,
                [],
                preflightIndexCharge,
                [pass],
                aliasingBarriers: bufferAliasingBarriers,
                textureAliasingBarriers: textureAliasingBarriers);

            Assert.That(submission.BufferAliasingAfterIndexStorageCapacity, Is.EqualTo(2));
            Assert.That(submission.BufferAliasingAfterIndexBackingByteCharge, Is.EqualTo(80));
            Assert.That(submission.TextureAliasingAfterIndexStorageCapacity, Is.EqualTo(2));
            Assert.That(submission.TextureAliasingAfterIndexBackingByteCharge, Is.EqualTo(80));
            Assert.That(submission.TryGetBufferAliasingAfterIndex(30, out var firstBufferBarrier), Is.True);
            Assert.That(firstBufferBarrier, Is.Zero);
            Assert.That(submission.TryGetTextureAliasingAfterIndex(50, out var firstTextureBarrier), Is.True);
            Assert.That(firstTextureBarrier, Is.Zero);
            Assert.That(submission.TryGetBufferAliasingAfterIndex(99, out _), Is.False);
            Assert.That(submission.TryGetTextureAliasingAfterIndex(99, out _), Is.False);

            var before = GC.GetAllocatedBytesForCurrentThread();
            var matches = true;

            for (var index = 0; index < 32; index++)
            {
                matches &= submission.TryGetBufferAliasingAfterIndex(30, out firstBufferBarrier)
                    && (firstBufferBarrier == 0);
                matches &= submission.TryGetTextureAliasingAfterIndex(50, out firstTextureBarrier)
                    && (firstTextureBarrier == 0);
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(matches, Is.True);
            Assert.That(allocated, Is.Zero);

            _ = Assert.Throws<OutOfMemoryException>(
                () => CreateBufferImportIndexSubmission(
                    device,
                    target,
                    [],
                    preflightIndexCharge - 1,
                    [pass],
                    aliasingBarriers: bufferAliasingBarriers,
                    textureAliasingBarriers: textureAliasingBarriers));
        }
        finally
        {
            await device.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public void GraphBufferImportIndexChargeRejectsInvalidOrOverflowingCapacity()
    {
        Assert.That(GraphicsGraphSubmission.CalculateBufferImportIndexBackingByteCharge(0), Is.Zero);
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => GraphicsGraphSubmission.CalculateBufferImportIndexBackingByteCharge(-1));
        _ = Assert.Throws<OverflowException>(
            () => GraphicsGraphSubmission.CalculateBufferImportIndexBackingByteCharge(long.MaxValue));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    public async Task GraphBufferImportIndexLookupUsesExactOwnedCapacity(int scenario)
    {
        ulong[] identities = scenario switch {
            0 => [],
            1 => [17],
            2 => [9, 1, 7, 3],
            3 => [ulong.MaxValue, 2, 1UL << 63, 7],
            4 => [2, 3, 4, 5, 6, 7],
            5 => [7, 6, 5, 4, 3, 2],
            6 => [42, 7, 42, 3],
            _ => [9, 17, 1, 13, 5, 16, 2, 12, 4, 14, 6, 11, 3, 15, 7, 10, 8],
        };
        var device = await CreateGraphicsDeviceAsync(new FakeBackendDevice()).ConfigureAwait(false);
        using var target = await device.CreateTextureAsync(CreateTextureDescription()).ConfigureAwait(false);
        var buffers = new List<GpuBuffer>();

        try
        {
            var imports = ImmutableArray.CreateBuilder<GraphBufferImportRecord>(identities.Length);

            foreach (var identity in identities)
            {
                var buffer = await device.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);
                buffers.Add(buffer);
                var version = new GraphBufferVersion(
                    device.DeviceEpoch, identity, 0, buffer.Description, new GraphBufferRange(0, buffer.ByteLength));
                imports.Add(new GraphBufferImportRecord(
                    buffer,
                    version,
                    new GraphImportOptions(GraphResourceState.Undefined, false, GraphImportCompletionPolicy.RequireIdle)));
            }

            var frozenImports = imports.MoveToImmutable();
            var expectedBufferCharge = GraphicsGraphSubmission.CalculateBufferImportIndexBackingByteCharge(identities.Length);
            var expectedCharge = GraphicsGraphSubmission.CalculatePreflightWorkspaceBackingByteCharge(
                identities.Length,
                textureImportCapacity: 1,
                orderedPassCapacity: 0,
                bufferAliasingCapacity: 0,
                textureAliasingCapacity: 0,
                bufferImportFlagCapacity: identities.Length,
                textureImportFlagCapacity: 1,
                passValidationCapacity: 0);
            var submission = CreateBufferImportIndexSubmission(device, target, frozenImports, expectedCharge);
            Assert.That(submission.BufferImportIndexStorageCapacity, Is.EqualTo(identities.Length));
            Assert.That(submission.BufferImportIndexBackingByteCharge, Is.EqualTo(expectedBufferCharge));
            Assert.That(submission.TextureImportIndexStorageCapacity, Is.EqualTo(1));
            Assert.That(submission.TextureImportIndexBackingByteCharge, Is.EqualTo(80));
            var expectedIndices = new int[identities.Length];

            for (var index = 0; index < identities.Length; index++)
            {
                expectedIndices[index] = index;

                for (var earlier = 0; earlier < index; earlier++)
                {
                    if (identities[earlier] == identities[index])
                    {
                        expectedIndices[index] = earlier;
                        break;
                    }
                }

                Assert.That(submission.TryGetIndexedBufferImport(identities[index], out var observed), Is.True);
                Assert.That(observed, Is.SameAs(frozenImports[expectedIndices[index]]));
                Assert.That(submission.GetBufferImportIndex(identities[index]), Is.EqualTo(expectedIndices[index]));
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            var matches = true;

            for (var index = 0; index < identities.Length; index++)
            {
                matches &= submission.TryGetIndexedBufferImport(identities[index], out var observed)
                    && ReferenceEquals(observed, frozenImports[expectedIndices[index]]);
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(matches, Is.True);
            Assert.That(allocated, Is.Zero);
            Assert.That(submission.TryGetIndexedBufferImport(0, out _), Is.False);
            Assert.That(submission.TryGetIndexedBufferImport(100, out _), Is.False);

            if (identities.Length != 0)
            {
                _ = Assert.Throws<OutOfMemoryException>(
                    () => CreateBufferImportIndexSubmission(device, target, frozenImports, expectedCharge - 1));
            }
        }
        finally
        {
            foreach (var buffer in buffers)
            {
                buffer.Dispose();
            }

            target.Dispose();
            await device.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task GraphBufferImportIndexPreservesDuplicateAndUsageValidationOrder(bool unsupportedFirst)
    {
        var device = await CreateGraphicsDeviceAsync(new FakeBackendDevice()).ConfigureAwait(false);
        using var target = await device.CreateTextureAsync(CreateTextureDescription()).ConfigureAwait(false);
        using var unsupported = await device.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);
        using var supported = await device.CreateBufferAsync(
            new BufferDescription(64, BufferUsage.Storage, GpuMemoryKind.DeviceLocal, 16, "indexed-storage")).ConfigureAwait(false);

        try
        {
            var options = new GraphImportOptions(GraphResourceState.Undefined, false, GraphImportCompletionPolicy.RequireIdle);
            var unsupportedImport = new GraphBufferImportRecord(
                unsupported,
                new GraphBufferVersion(device.DeviceEpoch, 31, 0, unsupported.Description, new GraphBufferRange(0, unsupported.ByteLength)),
                options);
            var supportedImport = new GraphBufferImportRecord(
                supported,
                new GraphBufferVersion(device.DeviceEpoch, 31, 0, supported.Description, new GraphBufferRange(0, supported.ByteLength)),
                options);
            var submission = CreateBufferImportIndexSubmission(
                device,
                target,
                unsupportedFirst ? [unsupportedImport, supportedImport] : [supportedImport, unsupportedImport],
                GraphicsGraphSubmission.CalculatePreflightWorkspaceBackingByteCharge(
                    bufferImportCapacity: 2,
                    textureImportCapacity: 1,
                    orderedPassCapacity: 0,
                    bufferAliasingCapacity: 0,
                    textureAliasingCapacity: 0,
                    bufferImportFlagCapacity: 2,
                    textureImportFlagCapacity: 1,
                    passValidationCapacity: 0));
            var field = typeof(GraphicsGraphSubmission).GetField("_bufferImportIndex", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("The private index backing was not found.");
            var backing = field.GetValue(submission);
            var expectedMessage = unsupportedFirst
                ? "Native graph submission requires each imported buffer to have a supported declared usage."
                : "Native graph submission requires one import per graph buffer resource.";

            for (var attempt = 0; attempt < 2; attempt++)
            {
                var exception = Assert.Throws<NotSupportedException>(submission.ValidateNativeRasterSubmission);
                Assert.That(exception?.Message, Is.EqualTo(expectedMessage));
                Assert.That(field.GetValue(submission), Is.SameAs(backing));
                Assert.That(submission.BufferImportIndexBackingByteCharge, Is.EqualTo(80));
            }
        }
        finally
        {
            unsupported.Dispose();
            supported.Dispose();
            target.Dispose();
            await device.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task GraphBufferImportIndexPreservesMalformedEntryFailureOrder()
    {
        var device = await CreateGraphicsDeviceAsync(new FakeBackendDevice()).ConfigureAwait(false);
        using var target = await device.CreateTextureAsync(CreateTextureDescription()).ConfigureAwait(false);
        using var buffer = await device.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);

        try
        {
            var imports = new GraphBufferImportRecord[2];
            imports[0] = new GraphBufferImportRecord(
                buffer,
                new GraphBufferVersion(device.DeviceEpoch, 7, 0, buffer.Description, new GraphBufferRange(0, buffer.ByteLength)),
                new GraphImportOptions(GraphResourceState.Undefined, false, GraphImportCompletionPolicy.RequireIdle));
            var submission = CreateBufferImportIndexSubmission(
                device,
                target,
                ImmutableCollectionsMarshal.AsImmutableArray(imports),
                GraphicsGraphSubmission.CalculatePreflightWorkspaceBackingByteCharge(
                    bufferImportCapacity: imports.Length,
                    textureImportCapacity: 1,
                    orderedPassCapacity: 0,
                    bufferAliasingCapacity: 0,
                    textureAliasingCapacity: 0,
                    bufferImportFlagCapacity: imports.Length,
                    textureImportFlagCapacity: 1,
                    passValidationCapacity: 0));
            var exception = Assert.Throws<NotSupportedException>(submission.ValidateNativeRasterSubmission);
            Assert.That(exception?.Message, Is.EqualTo("Native graph submission requires each imported buffer to have a supported declared usage."));
        }
        finally
        {
            buffer.Dispose();
            target.Dispose();
            await device.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task GraphBufferImportIndexRemainsOwnedAcrossRecordingRelease(bool quarantine)
    {
        var device = await CreateGraphicsDeviceAsync(new FakeBackendDevice()).ConfigureAwait(false);
        using var target = await device.CreateTextureAsync(CreateTextureDescription()).ConfigureAwait(false);
        using var transientTexture = await device.CreateTextureAsync(CreateTextureDescription()).ConfigureAwait(false);
        using var buffer = await device.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);

        try
        {
            var import = new GraphBufferImportRecord(
                buffer,
                new GraphBufferVersion(device.DeviceEpoch, 7, 0, buffer.Description, new GraphBufferRange(0, buffer.ByteLength)),
                new GraphImportOptions(GraphResourceState.Undefined, false, GraphImportCompletionPolicy.RequireIdle));
            var textureImport = new GraphTextureImportRecord(
                transientTexture,
                new GraphTextureVersion(
                    device.DeviceEpoch,
                    9,
                    0,
                    transientTexture.Description,
                    new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color)),
                new GraphImportOptions(
                    GraphResourceState.Undefined,
                    contentsValid: false,
                    completionPolicy: GraphImportCompletionPolicy.RequireIdle),
                isTransient: true);
            var pass = new RenderPassId(1, 0);
            var beforeBufferVersion = new GraphBufferVersion(
                    device.DeviceEpoch,
                    20,
                    0,
                    buffer.Description,
                    new GraphBufferRange(0, buffer.ByteLength));
            var afterBufferVersion = new GraphBufferVersion(
                    device.DeviceEpoch,
                    30,
                    0,
                    buffer.Description,
                    new GraphBufferRange(0, buffer.ByteLength));
            var bufferAliasingBarriers = ImmutableArray.Create(
                    new GraphResourceAliasingBarrier(pass, beforeBufferVersion, afterBufferVersion));
            var textureRange = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);
            var beforeTextureVersion = new GraphTextureVersion(device.DeviceEpoch, 40, 0, target.Description, textureRange);
            var afterTextureVersion = new GraphTextureVersion(device.DeviceEpoch, 50, 0, target.Description, textureRange);
            var textureAliasingBarriers = ImmutableArray.Create(
                    new GraphTextureAliasingBarrier(pass, beforeTextureVersion, afterTextureVersion));
            var submission = CreateBufferImportIndexSubmission(
                device,
                target,
                [import],
                    GraphicsGraphSubmission.CalculatePreflightWorkspaceBackingByteCharge(
                        bufferImportCapacity: 1,
                        textureImportCapacity: 2,
                        orderedPassCapacity: 1,
                        bufferAliasingCapacity: 1,
                        textureAliasingCapacity: 1,
                        bufferImportFlagCapacity: 1,
                        textureImportFlagCapacity: 2,
                        passValidationCapacity: 0),
                [pass],
                    [textureImport],
                    bufferAliasingBarriers,
                    textureAliasingBarriers);
            var type = typeof(GraphicsGraphSubmission);
            var bufferAliasingIndexField = type.GetField("_bufferAliasingAfterIndex", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("The buffer aliasing index backing was not found.");
            var bufferIndexField = type.GetField("_bufferImportIndex", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("The buffer import index backing was not found.");
            var textureAliasingIndexField = type.GetField("_textureAliasingAfterIndex", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("The texture aliasing index backing was not found.");
            var textureIndexField = type.GetField("_textureImportIndex", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("The texture import index backing was not found.");
            var passIndexField = type.GetField("_orderedPassIndex", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("The ordered pass index backing was not found.");
            var bufferAliasingIndex = bufferAliasingIndexField.GetValue(submission);
            var bufferIndex = bufferIndexField.GetValue(submission);
            var textureAliasingIndex = textureAliasingIndexField.GetValue(submission);
            var textureIndex = textureIndexField.GetValue(submission);
            var passIndex = passIndexField.GetValue(submission);

            if (quarantine)
            {
                submission.RetainRecordingForUncertainSubmission();
            }

            submission.DisposeRecordedPassCommandLists();
            Assert.That(bufferAliasingIndexField.GetValue(submission), Is.SameAs(bufferAliasingIndex));
            Assert.That(bufferIndexField.GetValue(submission), Is.SameAs(bufferIndex));
            Assert.That(textureAliasingIndexField.GetValue(submission), Is.SameAs(textureAliasingIndex));
            Assert.That(textureIndexField.GetValue(submission), Is.SameAs(textureIndex));
            Assert.That(passIndexField.GetValue(submission), Is.SameAs(passIndex));
            Assert.That(submission.BufferAliasingAfterIndexStorageCapacity, Is.EqualTo(1));
            Assert.That(submission.BufferAliasingAfterIndexBackingByteCharge, Is.EqualTo(80));
            Assert.That(submission.BufferImportIndexStorageCapacity, Is.EqualTo(1));
            Assert.That(submission.BufferImportIndexBackingByteCharge, Is.EqualTo(80));
            Assert.That(submission.TextureAliasingAfterIndexStorageCapacity, Is.EqualTo(1));
            Assert.That(submission.TextureAliasingAfterIndexBackingByteCharge, Is.EqualTo(80));
            Assert.That(submission.TextureImportIndexStorageCapacity, Is.EqualTo(2));
            Assert.That(submission.TextureImportIndexBackingByteCharge, Is.EqualTo(80));
            Assert.That(submission.OrderedPassIndexStorageCapacity, Is.EqualTo(1));
            Assert.That(submission.OrderedPassIndexBackingByteCharge, Is.EqualTo(80));
            Assert.That(submission.TryGetBufferAliasingAfterIndex(30, out var observedBufferAlias), Is.True);
            Assert.That(observedBufferAlias, Is.Zero);
            Assert.That(submission.TryGetIndexedBufferImport(7, out var observed), Is.True);
            Assert.That(observed, Is.SameAs(import));
            Assert.That(submission.TryGetTextureAliasingAfterIndex(50, out var observedTextureAlias), Is.True);
            Assert.That(observedTextureAlias, Is.Zero);
            Assert.That(submission.TryGetIndexedTextureImport(9, out var observedTexture), Is.True);
            Assert.That(observedTexture, Is.SameAs(textureImport));
            Assert.That(submission.TryGetOrderedPassIndex(pass, out var observedPassIndex), Is.True);
            Assert.That(observedPassIndex, Is.Zero);
        }
        finally
        {
            buffer.Dispose();
            transientTexture.Dispose();
            target.Dispose();
            await device.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task GraphCopyPassResourceUsageCountsDistinctImports()
    {
        var device = await CreateGraphicsDeviceAsync(new FakeBackendDevice()).ConfigureAwait(false);

        try
        {
            using var target = await device.CreateTextureAsync(CreateTextureDescription()).ConfigureAwait(false);
            using var source = await device.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);
            using var destination = await device.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);
            var range = new GraphBufferRange(0, source.ByteLength);
            _ = await device.UploadBufferAsync(source, 0, [0, 1, 2, 3, 4, 5, 6, 7])
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            var (sourceState, sourceContentsValid, _, _) = source.ResourceOwner.CaptureGraphImportState(range);
            Assert.That(sourceContentsValid, Is.True);
            var passId = new RenderPassId(19, 0);
            var sourceVersion = new GraphBufferVersion(device.DeviceEpoch, 50, 0, source.Description, range);
            var destinationVersion = new GraphBufferVersion(device.DeviceEpoch, 60, 0, destination.Description, range);
            var sourceAccess = new GraphResourceAccess(
                passId,
                sourceVersion,
                range,
                GraphBufferUsage.CopySource,
                isWrite: false);
            var destinationAccess = new GraphResourceAccess(
                passId,
                destinationVersion,
                range,
                GraphBufferUsage.CopyDestination,
                isWrite: true,
                contentsValid: false);
            var accesses = ImmutableArray.Create(sourceAccess, sourceAccess, destinationAccess, destinationAccess);

            void RecordCopy(scoped ref CommandRecorder recorder)
            {
                recorder.CopyBuffer(sourceVersion, 0, destinationVersion, 0, source.ByteLength);
            }

            var recorded = await device.RecordPassAsync(
                    passId,
                    CommandRecorderPassKind.Copy,
                    accesses,
                    [],
                    depthTargetFormat: null,
                    RecordCopy)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            GraphicsGraphSubmission? submission = null;

            try
            {
                var targetRange = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);
                var targetVersion = new GraphTextureVersion(
                    device.DeviceEpoch,
                    1,
                    0,
                    target.Description,
                    targetRange);
                var (targetState, targetContentsValid, _, _) = target.ResourceOwner.CaptureGraphImportState(targetRange);
                var workspaceCharge = GraphicsGraphSubmission.CalculatePreflightWorkspaceBackingByteCharge(
                    bufferImportCapacity: 2,
                    textureImportCapacity: 1,
                    orderedPassCapacity: 1,
                    bufferAliasingCapacity: 0,
                    textureAliasingCapacity: 0,
                    bufferImportFlagCapacity: 2,
                    textureImportFlagCapacity: 1,
                    passValidationCapacity: 1,
                    accessPairCapacity: accesses.Length);
                submission = new GraphicsGraphSubmission(
                    device.DeviceEpoch,
                    target,
                    surfaceTarget: null,
                    targetVersion,
                    surfaceEpoch: 0,
                    swapchainEpoch: 0,
                    [passId],
                    [new GraphicsGraphSubmissionPass(passId, CommandRecorderPassKind.Copy, null, recorded, [])],
                    [],
                    [],
                    [],
                    accesses,
                    [
                        new GraphBufferImportRecord(
                            source,
                            sourceVersion,
                            new GraphImportOptions(sourceState, sourceContentsValid, GraphImportCompletionPolicy.RequireIdle)),
                        new GraphBufferImportRecord(
                            destination,
                            destinationVersion,
                            new GraphImportOptions(GraphResourceState.Undefined, false, GraphImportCompletionPolicy.RequireIdle)),
                    ],
                    [
                        new GraphTextureImportRecord(
                            target,
                            targetVersion,
                            new GraphImportOptions(targetState, targetContentsValid, GraphImportCompletionPolicy.RequireIdle)),
                    ],
                    [recorded],
                    maximumPreflightWorkspaceBytes: workspaceCharge);

                var exception = Assert.Throws<NotSupportedException>(submission.ValidateNativeRasterSubmission);
                Assert.That(exception?.Message, Is.EqualTo("Native graph submission requires a graph-output color attachment."));
            }
            finally
            {
                if (submission is not null)
                {
                    submission.DisposeRecordedPassCommandLists();
                }
                else
                {
                    recorded.Dispose();
                }
            }
        }
        finally
        {
            await device.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public async Task GraphFrameSlotAdmissionWaitsAtCapacityAndRecoversAfterRelease(int frameSlotCount)
    {
        var graphicsDevice = await CreateGraphicsDeviceAsync(
            new FakeBackendDevice(),
            frameSlotCount).ConfigureAwait(false);
        var reservations = new GraphicsDevice.GraphFrameSlotReservation?[frameSlotCount];
        var retainedBackingCharge = 64 + (4 * IntPtr.Size);

        try
        {
            Assert.That(graphicsDevice.PendingGraphSubmissionBackingByteCharge, Is.EqualTo(retainedBackingCharge));

            for (var index = 0; index < reservations.Length; index++)
            {
                reservations[index] = await graphicsDevice.AcquireGraphFrameSlotAsync(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
            }

            var transferredReservation = reservations[0]
                ?? throw new InvalidOperationException("The initial graph-frame reservation was not acquired.");
            Assert.That(transferredReservation.TryTransferToRequest(), Is.True);
            Assert.That(transferredReservation.TryTransferToRequest(), Is.False);
            transferredReservation.Dispose();

            var waitingReservation = graphicsDevice.AcquireGraphFrameSlotAsync(CancellationToken.None);
            Assert.That(waitingReservation.IsCompleted, Is.False);
            Assert.That(graphicsDevice.PendingGraphSubmissionBackingByteCharge, Is.EqualTo(retainedBackingCharge));

            transferredReservation.ReleaseFromRequest();
            reservations[0] = null;
            using var recoveredReservation = await waitingReservation
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);

            var nextWaitingReservation = graphicsDevice.AcquireGraphFrameSlotAsync(CancellationToken.None);
            Assert.That(nextWaitingReservation.IsCompleted, Is.False);

            recoveredReservation.Dispose();
            using var replacementReservation = await nextWaitingReservation
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            Assert.That(graphicsDevice.PendingGraphSubmissionBackingByteCharge, Is.EqualTo(retainedBackingCharge));
        }
        finally
        {
            foreach (var reservation in reservations)
            {
                reservation?.Dispose();
            }

            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public void GraphImportIndexChargeCoversEachExplicitBacking()
    {
        Assert.That(GraphicsGraphSubmission.CalculateImportIndexBackingByteCharge(0, 0, 0), Is.Zero);
        Assert.That(GraphicsGraphSubmission.CalculateImportIndexBackingByteCharge(0, 4, 3), Is.EqualTo(160));
        Assert.That(GraphicsGraphSubmission.CalculatePreflightIndexBackingByteCharge(0, 4, 3, 2, 2), Is.EqualTo(320));
        Assert.That(GraphicsGraphSubmission.CalculateBufferImportFlagsBackingByteCharge(0), Is.Zero);
        Assert.That(GraphicsGraphSubmission.CalculateBufferImportFlagsBackingByteCharge(17), Is.EqualTo(112));
        Assert.That(GraphicsGraphSubmission.CalculateTextureImportFlagsBackingByteCharge(0), Is.Zero);
        Assert.That(GraphicsGraphSubmission.CalculateTextureImportFlagsBackingByteCharge(17), Is.EqualTo(112));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => GraphicsGraphSubmission.CalculateImportIndexBackingByteCharge(-1, 0, 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => GraphicsGraphSubmission.CalculateImportIndexBackingByteCharge(0, -1, 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => GraphicsGraphSubmission.CalculateImportIndexBackingByteCharge(0, 0, -1));
        _ = Assert.Throws<OverflowException>(
            () => GraphicsGraphSubmission.CalculateImportIndexBackingByteCharge(long.MaxValue, 0, 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => GraphicsGraphSubmission.CalculateBufferImportFlagsBackingByteCharge(-1));
        _ = Assert.Throws<OverflowException>(
            () => GraphicsGraphSubmission.CalculateBufferImportFlagsBackingByteCharge(long.MaxValue));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => GraphicsGraphSubmission.CalculateTextureImportFlagsBackingByteCharge(-1));
        _ = Assert.Throws<OverflowException>(
            () => GraphicsGraphSubmission.CalculateTextureImportFlagsBackingByteCharge(long.MaxValue));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => GraphicsGraphSubmission.CalculatePreflightIndexBackingByteCharge(0, 0, 0, -1, 0));
        _ = Assert.Throws<OverflowException>(
            () => GraphicsGraphSubmission.CalculatePreflightIndexBackingByteCharge(0, 0, 0, long.MaxValue, 0));
    }

    [Test]
    public async Task GraphImportIndicesPreserveDuplicateRefusal()
    {
        var device = await CreateGraphicsDeviceAsync(new FakeBackendDevice()).ConfigureAwait(false);

        try
        {
            using var target = await device.CreateTextureAsync(CreateTextureDescription()).ConfigureAwait(false);
            using var transientTexture = await device.CreateTextureAsync(CreateTextureDescription()).ConfigureAwait(false);
            var range = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);
            var options = new GraphImportOptions(
                GraphResourceState.Undefined,
                contentsValid: false,
                completionPolicy: GraphImportCompletionPolicy.RequireIdle);
            var duplicateTransientImports = ImmutableArray.Create(
                new GraphTextureImportRecord(
                    transientTexture,
                    new GraphTextureVersion(device.DeviceEpoch, 90, 0, transientTexture.Description, range),
                    options,
                    isTransient: true),
                new GraphTextureImportRecord(
                    transientTexture,
                    new GraphTextureVersion(device.DeviceEpoch, 90, 0, transientTexture.Description, range),
                    options,
                    isTransient: true));
            var duplicatePass = new RenderPassId(7, 3);

            var duplicateTextureSubmission = CreateBufferImportIndexSubmission(
                device,
                target,
                [],
                GraphicsGraphSubmission.CalculatePreflightWorkspaceBackingByteCharge(
                    bufferImportCapacity: 0,
                    textureImportCapacity: 3,
                    orderedPassCapacity: 0,
                    bufferAliasingCapacity: 0,
                    textureAliasingCapacity: 0,
                    bufferImportFlagCapacity: 0,
                    textureImportFlagCapacity: 3,
                    passValidationCapacity: 0),
                additionalTextureImports: duplicateTransientImports);
            var textureException = Assert.Throws<NotSupportedException>(duplicateTextureSubmission.ValidateNativeRasterSubmission);
            Assert.That(textureException?.Message, Is.EqualTo("Native graph submission requires one transient import per graph texture resource."));

            var duplicateColorVersion = new GraphTextureVersion(
                device.DeviceEpoch,
                91,
                0,
                transientTexture.Description,
                range);
            var duplicateColorImports = ImmutableArray.Create(
                new GraphTextureImportRecord(transientTexture, duplicateColorVersion, options),
                new GraphTextureImportRecord(transientTexture, duplicateColorVersion, options));
            var duplicateColorAccess = new GraphResourceAccess(
                duplicatePass,
                duplicateColorVersion,
                range,
                TextureUsage.ColorAttachment,
                AttachmentLoadAction.Clear,
                AttachmentStoreAction.Store,
                isWrite: true,
                contentsValid: false);
            var duplicateColorWorkspaceCharge = GraphicsGraphSubmission.CalculatePreflightWorkspaceBackingByteCharge(
                bufferImportCapacity: 0,
                textureImportCapacity: 3,
                orderedPassCapacity: 0,
                bufferAliasingCapacity: 0,
                textureAliasingCapacity: 0,
                bufferImportFlagCapacity: 0,
                textureImportFlagCapacity: 3,
                passValidationCapacity: 0,
                accessPairCapacity: 1);
            var duplicateColorSubmission = CreateBufferImportIndexSubmission(
                device,
                target,
                [],
                duplicateColorWorkspaceCharge,
                additionalTextureImports: duplicateColorImports,
                accesses: [duplicateColorAccess]);
            var duplicateColorException = Assert.Throws<NotSupportedException>(
                duplicateColorSubmission.ValidateNativeRasterSubmission);
            Assert.That(
                duplicateColorException?.Message,
                Is.EqualTo("Native graph color attachments require valid 2D color imports in a supported state."));

            var duplicatePassSubmission = CreateBufferImportIndexSubmission(
                device,
                target,
                [],
                256,
                [duplicatePass, duplicatePass]);
            var passException = Assert.Throws<ArgumentException>(duplicatePassSubmission.ValidateNativeRasterSubmission);
            Assert.That(passException?.ParamName, Is.Null);
        }
        finally
        {
            await device.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task GraphImportOptionsCompareWithOwnerStateAndReportPendingUses()
    {
        var backendDevice = new FakeBackendDevice();
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var buffer = await graphicsDevice.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);

        try
        {
            var range = new GraphBufferRange(0, buffer.ByteLength);
            (var idleStateMatches, var idleContentsMatch, var idleLastUseSequence, var idleIsIdle) =
                buffer.ResourceOwner.EvaluateGraphImportOptions(
                range,
                new GraphImportOptions(
                    GraphResourceState.Undefined,
                    contentsValid: false,
                    GraphImportCompletionPolicy.RequireIdle));
            Assert.That(idleStateMatches, Is.True);
            Assert.That(idleContentsMatch, Is.True);
            Assert.That(idleLastUseSequence, Is.Zero);
            Assert.That(idleIsIdle, Is.True);

            using var use = buffer.ResourceOwner.AcquireUse(nameof(buffer));
            (var pendingStateMatches, var pendingContentsMatch, var pendingLastUseSequence, var pendingIsIdle) =
                buffer.ResourceOwner.EvaluateGraphImportOptions(
                range,
                new GraphImportOptions(
                    GraphResourceState.Undefined,
                    contentsValid: false,
                    GraphImportCompletionPolicy.WaitForLastUse));
            Assert.That(pendingStateMatches, Is.True);
            Assert.That(pendingContentsMatch, Is.True);
            Assert.That(pendingLastUseSequence, Is.EqualTo(use.Sequence));
            Assert.That(pendingIsIdle, Is.False);

            (var mismatchStateMatches, var mismatchContentsMatch, _, _) =
                buffer.ResourceOwner.EvaluateGraphImportOptions(
                range,
                new GraphImportOptions(
                    GraphResourceState.General,
                    contentsValid: false,
                    GraphImportCompletionPolicy.RequireIdle));
            Assert.That(mismatchStateMatches, Is.False);
            Assert.That(mismatchContentsMatch, Is.True);
        }
        finally
        {
            buffer.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task GraphPassValidationWorkspaceUsesExactOwnedCapacity()
    {
        var device = await CreateGraphicsDeviceAsync(new FakeBackendDevice()).ConfigureAwait(false);

        try
        {
            using var target = await device.CreateTextureAsync(CreateTextureDescription()).ConfigureAwait(false);
            var passId = new RenderPassId(1, 0);
            var bufferRange = new GraphBufferRange(0, 8);
            var targetRange = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);
            var targetVersion = new GraphTextureVersion(
                device.DeviceEpoch,
                1,
                0,
                target.Description,
                targetRange);
            var colorAttachment = new GraphicsGraphSubmissionAttachment(
                targetVersion,
                targetRange,
                AttachmentLoadAction.Clear,
                AttachmentStoreAction.Store,
                default,
                1.0f,
                false);
            var otherColorVersion = new GraphTextureVersion(
                device.DeviceEpoch,
                3,
                0,
                target.Description,
                targetRange);
            var otherColorAttachment = colorAttachment with { Version = otherColorVersion };
            var pass = new GraphicsGraphSubmissionPass(
                passId,
                CommandRecorderPassKind.Copy,
                null,
                null,
                [otherColorAttachment, colorAttachment, otherColorAttachment]);
            var bufferVersion = new GraphBufferVersion(
                device.DeviceEpoch,
                2,
                0,
                CreateBufferDescription(),
                bufferRange);
            var access = new GraphResourceAccess(
                passId,
                bufferVersion,
                bufferRange,
                GraphBufferUsage.CopySource,
                isWrite: false);
            var expectedCharge = GraphicsGraphSubmission.CalculatePreflightWorkspaceBackingByteCharge(
                bufferImportCapacity: 0,
                textureImportCapacity: 1,
                orderedPassCapacity: 0,
                bufferAliasingCapacity: 0,
                textureAliasingCapacity: 0,
                bufferImportFlagCapacity: 0,
                textureImportFlagCapacity: 1,
                passValidationCapacity: 1,
                accessPairCapacity: 1,
                colorAttachmentIndexCapacity: 3);

            Assert.That(GraphicsGraphSubmission.CalculateAccessPairIndexBackingByteCharge(1), Is.EqualTo(80));
            Assert.That(GraphicsGraphSubmission.CalculateAccessValidationFlagsBackingByteCharge(1), Is.EqualTo(80));
            Assert.That(GraphicsGraphSubmission.CalculatePassValidationIndexBackingByteCharge(1), Is.EqualTo(80));
            Assert.That(GraphicsGraphSubmission.CalculatePassValidationFlagsBackingByteCharge(1), Is.EqualTo(80));
            Assert.That(GraphicsGraphSubmission.CalculateTextureImportFlagsBackingByteCharge(1), Is.EqualTo(80));
            Assert.That(GraphicsGraphSubmission.CalculateColorAttachmentResourceIndexBackingByteCharge(3), Is.EqualTo(192));
            Assert.That(expectedCharge, Is.EqualTo(672));
            _ = Assert.Throws<ArgumentOutOfRangeException>(
                () => GraphicsGraphSubmission.CalculateAccessPairIndexBackingByteCharge(-1));
            _ = Assert.Throws<OverflowException>(
                () => GraphicsGraphSubmission.CalculateAccessPairIndexBackingByteCharge(long.MaxValue));
            _ = Assert.Throws<ArgumentOutOfRangeException>(
                () => GraphicsGraphSubmission.CalculatePassValidationFlagsBackingByteCharge(-1));
            _ = Assert.Throws<OverflowException>(
                () => GraphicsGraphSubmission.CalculatePassValidationFlagsBackingByteCharge(long.MaxValue));
            _ = Assert.Throws<ArgumentOutOfRangeException>(
                () => GraphicsGraphSubmission.CalculateColorAttachmentResourceIndexBackingByteCharge(-1));
            _ = Assert.Throws<OverflowException>(
                () => GraphicsGraphSubmission.CalculateColorAttachmentResourceIndexBackingByteCharge(long.MaxValue));

            _ = Assert.Throws<OutOfMemoryException>(
                () => CreateBufferImportIndexSubmission(
                    device,
                    target,
                    [],
                    expectedCharge - 1,
                    accesses: [access],
                    submissionPasses: [pass]));

            var submission = CreateBufferImportIndexSubmission(
                device,
                target,
                [],
                expectedCharge,
                accesses: [access],
                submissionPasses: [pass]);
            Assert.That(submission.AccessPairIndexStorageCapacity, Is.EqualTo(1));
            Assert.That(submission.AccessPairIndexBackingByteCharge, Is.EqualTo(80));
            Assert.That(submission.PassValidationIndexStorageCapacity, Is.EqualTo(1));
            Assert.That(submission.PassValidationIndexBackingByteCharge, Is.EqualTo(80));
            Assert.That(submission.ColorAttachmentResourceIndexStorageCapacity, Is.EqualTo(3));
            Assert.That(submission.ColorAttachmentResourceOrderStorageCapacity, Is.EqualTo(3));
            Assert.That(submission.ColorAttachmentResourceIndexBackingByteCharge, Is.EqualTo(192));
            Assert.That(submission.ColorAttachmentResourceCount, Is.EqualTo(2));
            Assert.That(submission.GetSubmissionPassIndex(passId), Is.Zero);
            Assert.That(submission.GetColorAttachmentResourceId(0), Is.EqualTo(otherColorVersion.ResourceId));
            Assert.That(submission.GetColorAttachmentResourceId(1), Is.EqualTo(targetVersion.ResourceId));
            Assert.That(submission.GetColorAttachmentResourceIndex(otherColorVersion.ResourceId), Is.Zero);
            Assert.That(submission.GetColorAttachmentResourceIndex(targetVersion.ResourceId), Is.EqualTo(1));
            Assert.That(submission.GetTextureImportIndex(targetVersion.ResourceId), Is.Zero);
        }
        finally
        {
            await device.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [TestCase(0, true, 0)]
    [TestCase(1, false, 0)]
    [TestCase(4, true, 0)]
    [TestCase(8, true, 0)]
    [TestCase(2, true, 1)]
    [TestCase(2, true, 2)]
    [TestCase(2, true, 3)]
    public async Task GraphPreflightColorFormatsBorrowAttachmentOrderWithoutScratch(
        int colorCount,
        bool includeDepth,
        int mismatch)
    {
        var device = await CreateGraphicsDeviceAsync(new FakeBackendDevice()).ConfigureAwait(false);

        try
        {
            var resources = new GraphResourceRegistry(device, graphOwnerId: 301);
            var attachments = ImmutableArray.CreateBuilder<GraphicsGraphSubmissionAttachment>();
            var formats = ImmutableArray.CreateBuilder<TextureFormat>();

            for (var index = 0; index < colorCount; index++)
            {
                var format = (index % 2 == 0) ? TextureFormat.Rgba8Unorm : TextureFormat.Rgba16Float;
                var version = resources.CreateTransientTexture(new TextureDescription(
                    TextureDimension.Dimension2D, 4, 4, 1, 1, 1, format, TextureUsage.ColorAttachment, "preflight-color"));
                attachments.Add(new GraphicsGraphSubmissionAttachment(
                    version, version.Range, AttachmentLoadAction.Clear, AttachmentStoreAction.Store, default, 1.0f, false));
                formats.Add(format);
            }

            if (includeDepth)
            {
                var version = resources.CreateTransientTexture(new TextureDescription(
                    TextureDimension.Dimension2D, 4, 4, 1, 1, 1, TextureFormat.D32Float, TextureUsage.DepthAttachment, "preflight-depth"));
                attachments.Insert(colorCount / 2, new GraphicsGraphSubmissionAttachment(
                    version, version.Range, AttachmentLoadAction.Clear, AttachmentStoreAction.Store, default, 1.0f, true));
            }

            if (mismatch == 1)
            {
                (formats[0], formats[1]) = (formats[1], formats[0]);
            }
            else if (mismatch == 2)
            {
                formats.RemoveAt(formats.Count - 1);
            }
            else if (mismatch == 3)
            {
                formats.Add(TextureFormat.Rgba8Unorm);
            }

            var frozenFormats = formats.ToImmutable();
            var frozenAttachments = attachments.ToImmutable();
            var expected = mismatch == 0;
            Assert.That(GraphicsGraphSubmission.MatchesColorTargetFormats(frozenFormats, frozenAttachments), Is.EqualTo(expected));
            var before = GC.GetAllocatedBytesForCurrentThread();
            var matches = GraphicsGraphSubmission.MatchesColorTargetFormats(frozenFormats, frozenAttachments);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(matches, Is.EqualTo(expected));
            Assert.That(allocated, Is.Zero);
        }
        finally
        {
            await device.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [TestCase(false, 0, true)]
    [TestCase(false, 1, false)]
    [TestCase(false, 2, false)]
    [TestCase(false, 3, false)]
    [TestCase(false, 4, false)]
    [TestCase(false, 5, false)]
    [TestCase(true, 0, true)]
    [TestCase(true, 1, false)]
    [TestCase(true, 2, false)]
    public async Task GraphPriorStorageHazardBorrowsFrozenAccessesWithoutScratch(
        bool texture,
        int scenario,
        bool expected)
    {
        var device = await CreateGraphicsDeviceAsync(new FakeBackendDevice()).ConfigureAwait(false);
        using var target = await device.CreateTextureAsync(CreateTextureDescription()).ConfigureAwait(false);

        try
        {
            var earlierPass = new RenderPassId(302, 2);
            var currentPass = new RenderPassId(302, 0);
            var laterPass = new RenderPassId(302, 1);
            var bufferDescription = new BufferDescription(64, BufferUsage.Storage, GpuMemoryKind.DeviceLocal, 16, "hazard-buffer");
            var bufferVersion = new GraphBufferVersion(device.DeviceEpoch, 2, 0, bufferDescription, new GraphBufferRange(0, 64));
            var otherBufferVersion = new GraphBufferVersion(device.DeviceEpoch, 3, 0, bufferDescription, new GraphBufferRange(0, 64));
            var textureDescription = new TextureDescription(
                TextureDimension.Dimension2D, 4, 4, 1, 1, 2, TextureFormat.Rgba16Float, TextureUsage.Storage, "hazard-texture");
            var textureVersion = new GraphTextureVersion(
                device.DeviceEpoch, 2, 0, textureDescription, new GraphTextureRange(0, 2, 0, 1, TextureAspect.Color));
            var otherTextureVersion = new GraphTextureVersion(
                device.DeviceEpoch, 3, 0, textureDescription, new GraphTextureRange(0, 2, 0, 1, TextureAspect.Color));

            GraphResourceAccess CreateAccess(RenderPassId pass, bool write, ulong pairId, bool currentRange, bool otherResource = false)
            {
                if (texture)
                {
                    return new GraphResourceAccess(
                        pass,
                        otherResource ? otherTextureVersion : textureVersion,
                        new GraphTextureRange(currentRange && (scenario == 1) ? 1 : 0, 1, 0, 1, TextureAspect.Color),
                        write ? GraphTextureUsage.StorageWrite : GraphTextureUsage.StorageRead,
                        write,
                        storageReadWritePairId: pairId);
                }

                return new GraphResourceAccess(
                    pass,
                    otherResource ? otherBufferVersion : bufferVersion,
                    new GraphBufferRange(currentRange ? (scenario == 1 ? 16 : 8) : 0, 16),
                    write ? GraphBufferUsage.StorageWrite : GraphBufferUsage.StorageRead,
                    write,
                    storageReadWritePairId: pairId);
            }

            var outputVersion = new GraphTextureVersion(
                device.DeviceEpoch, 1, 0, target.Description, new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color));
            var submission = new GraphicsGraphSubmission(
                device.DeviceEpoch,
                target,
                surfaceTarget: null,
                outputVersion,
                surfaceEpoch: 0,
                swapchainEpoch: 0,
                [earlierPass, currentPass, laterPass],
                [],
                [],
                [],
                [],
                [
                    CreateAccess(currentPass, false, scenario == 5 ? 0UL : 2UL, true),
                    CreateAccess(earlierPass, true, 3, true, otherResource: true),
                    CreateAccess(laterPass, true, 4, true),
                    CreateAccess(scenario == 2 ? laterPass : earlierPass, scenario != 3, scenario == 4 ? 0UL : 1UL, false),
                ],
                [],
                [new GraphTextureImportRecord(
                    target,
                    outputVersion,
                    new GraphImportOptions(GraphResourceState.Undefined, false, GraphImportCompletionPolicy.RequireIdle))],
                []);
            Assert.That(submission.HasPriorStorageWriteHazard(currentPass, 2), Is.EqualTo(expected));
            var before = GC.GetAllocatedBytesForCurrentThread();
            var hazard = submission.HasPriorStorageWriteHazard(currentPass, 2);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(hazard, Is.EqualTo(expected));
            Assert.That(allocated, Is.Zero);
            Assert.That(submission.HasPriorStorageWriteHazard(earlierPass, 2), Is.False);
            Assert.That(submission.HasPriorStorageWriteHazard(currentPass, 4), Is.False);

            if (!texture && (scenario == 0))
            {
                var exception = Assert.Throws<ArgumentException>(
                    () => submission.HasPriorStorageWriteHazard(new RenderPassId(302, 3), 2));
                Assert.That(exception?.ParamName, Is.EqualTo("passId"));
            }
        }
        finally
        {
            target.Dispose();
            await device.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task GraphResourceRegistryRejectsConflictingExplicitAndInferredDependencies()
    {
        var backendDevice = new FakeBackendDevice();
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var registry = new GraphResourceRegistry(graphicsDevice, graphOwnerId: 7);
        var description = new BufferDescription(
            byteLength: 8,
            BufferUsage.Storage,
            GpuMemoryKind.DeviceLocal,
            structuredStride: null,
            debugName: "Transient graph buffer");

        try
        {
            var reader = registry.AddPass(new RenderPassDescription("reader", 0, [], false));
            var writer = registry.AddPass(new RenderPassDescription("writer", 1, [reader], false));
            var initialVersion = registry.CreateTransientBuffer(description);
            var writtenVersion = registry.WriteBuffer(
                writer,
                initialVersion,
                new GraphBufferRange(0, 8),
                GraphBufferUsage.StorageWrite);
            registry.ReadBuffer(
                reader,
                writtenVersion,
                new GraphBufferRange(0, 8),
                GraphBufferUsage.StorageRead);

            var result = registry.ValidateCompilation();
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.AccessesValid, Is.True);
            Assert.That(result.ImportsValid, Is.True);
            Assert.That(result.ContentsValid, Is.True);
            Assert.That(result.DependenciesValid, Is.False);
            Assert.That(result.OrderedPasses, Is.Empty);
        }
        finally
        {
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task GraphResourceRegistryRejectsOverlappingSamePassTextureFeedback()
    {
        var backendDevice = new FakeBackendDevice();
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var registry = new GraphResourceRegistry(graphicsDevice, graphOwnerId: 7);
        var description = new TextureDescription(
            TextureDimension.Dimension2D,
            4,
            4,
            1,
            1,
            1,
            TextureFormat.Rgba16Float,
            TextureUsage.Sampled | TextureUsage.Storage,
            "Transient feedback texture");
        var range = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);

        try
        {
            var seedPass = registry.AddPass(new RenderPassDescription("seed", 0, [], false));
            var feedbackPass = registry.AddPass(new RenderPassDescription("feedback", 1, [], false));
            var initialVersion = registry.CreateTransientTexture(description);
            var initializedVersion = registry.WriteTexture(
                seedPass,
                initialVersion,
                range,
                GraphTextureUsage.StorageWrite);
            registry.ReadTexture(feedbackPass, initializedVersion, range, GraphTextureUsage.SampledRead);
            registry.ReadTexture(feedbackPass, initializedVersion, range, GraphTextureUsage.StorageRead);
            _ = registry.WriteTexture(
                feedbackPass,
                initializedVersion,
                range,
                GraphTextureUsage.StorageWrite);

            var result = registry.ValidateCompilation();
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.AccessesValid, Is.False);
            Assert.That(result.ImportsValid, Is.True);
            Assert.That(result.ContentsValid, Is.True);
            Assert.That(result.DependenciesValid, Is.True);
            Assert.That(result.OrderedPasses, Is.Empty);
        }
        finally
        {
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task GraphResourceRegistryRejectsUninitializedStorageReadWriteAndTracksValidPairs()
    {
        var backendDevice = new FakeBackendDevice();
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var registry = new GraphResourceRegistry(graphicsDevice, graphOwnerId: 7);
        var bufferDescription = new BufferDescription(
            16,
            BufferUsage.Storage,
            GpuMemoryKind.DeviceLocal,
            structuredStride: null,
            debugName: "Storage read-write buffer");
        var textureDescription = new TextureDescription(
            TextureDimension.Dimension2D,
            4,
            4,
            1,
            1,
            1,
            TextureFormat.Rgba16Float,
            TextureUsage.Storage,
            "Storage read-write texture");
        var bufferRange = new GraphBufferRange(0, 16);
        var textureRange = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);

        try
        {
            var bufferSeed = registry.AddPass(new RenderPassDescription("buffer-seed", 0, [], false));
            var bufferReadWrite = registry.AddPass(new RenderPassDescription("buffer-read-write", 1, [], false));
            var bufferRead = registry.AddPass(new RenderPassDescription("buffer-read", 2, [], false));
            var textureSeed = registry.AddPass(new RenderPassDescription("texture-seed", 3, [], false));
            var textureReadWrite = registry.AddPass(new RenderPassDescription("texture-read-write", 4, [], false));
            var textureRead = registry.AddPass(new RenderPassDescription("texture-read", 5, [], false));
            var bufferVersion0 = registry.CreateTransientBuffer(bufferDescription);
            var bufferVersion1 = registry.WriteBuffer(
                bufferSeed,
                bufferVersion0,
                bufferRange,
                GraphBufferUsage.StorageWrite);
            var bufferVersion2 = registry.ReadWriteBuffer(bufferReadWrite, bufferVersion1, bufferRange);
            registry.ReadBuffer(bufferRead, bufferVersion2, bufferRange, GraphBufferUsage.StorageRead);
            var textureVersion0 = registry.CreateTransientTexture(textureDescription);
            var textureVersion1 = registry.WriteTexture(
                textureSeed,
                textureVersion0,
                textureRange,
                GraphTextureUsage.StorageWrite);
            var textureVersion2 = registry.ReadWriteTexture(textureReadWrite, textureVersion1, textureRange);
            registry.ReadTexture(textureRead, textureVersion2, textureRange, GraphTextureUsage.StorageRead);

            var result = registry.ValidateCompilation();
            var readWriteTransitions = 0;

            foreach (var transition in result.StateTransitions)
            {
                if (transition.AfterState == GraphResourceState.StorageReadWrite)
                {
                    readWriteTransitions++;
                }
            }

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.AccessesValid, Is.True);
            Assert.That(result.ContentsValid, Is.True);
            Assert.That(result.DependenciesValid, Is.True);
            Assert.That(result.OrderedPasses.IndexOf(bufferSeed), Is.LessThan(result.OrderedPasses.IndexOf(bufferReadWrite)));
            Assert.That(result.OrderedPasses.IndexOf(bufferReadWrite), Is.LessThan(result.OrderedPasses.IndexOf(bufferRead)));
            Assert.That(result.OrderedPasses.IndexOf(textureSeed), Is.LessThan(result.OrderedPasses.IndexOf(textureReadWrite)));
            Assert.That(result.OrderedPasses.IndexOf(textureReadWrite), Is.LessThan(result.OrderedPasses.IndexOf(textureRead)));
            Assert.That(readWriteTransitions, Is.EqualTo(2));

            var uninitializedVersion = registry.CreateTransientBuffer(bufferDescription);
            var invalidReadWritePass = registry.AddPass(new RenderPassDescription("invalid-read-write", 6, [], false));
            _ = registry.ReadWriteBuffer(invalidReadWritePass, uninitializedVersion, bufferRange);
            var invalidResult = registry.ValidateCompilation();
            Assert.That(invalidResult.IsValid, Is.False);
            Assert.That(invalidResult.AccessesValid, Is.True);
            Assert.That(invalidResult.ContentsValid, Is.False);
        }
        finally
        {
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task GraphResourceRegistryTracksAttachmentLoadAndDiscardValidity()
    {
        var backendDevice = new FakeBackendDevice();
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var registry = new GraphResourceRegistry(graphicsDevice, graphOwnerId: 7);
        var description = new TextureDescription(
            TextureDimension.Dimension2D,
            4,
            4,
            1,
            1,
            1,
            TextureFormat.Rgba8Unorm,
            TextureUsage.Sampled | TextureUsage.ColorAttachment,
            "Transient graph attachment");
        var range = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);

        try
        {
            var loadPass = registry.AddPass(new RenderPassDescription("load", 0, [], false));
            var discardPass = registry.AddPass(new RenderPassDescription("discard", 1, [], false));
            var readPass = registry.AddPass(new RenderPassDescription("read", 2, [], false));
            var initialVersion = registry.CreateTransientTexture(description);
            var loadedVersion = registry.WriteAttachment(
                loadPass,
                initialVersion,
                range,
                AttachmentLoadAction.Load,
                AttachmentStoreAction.Store);
            var discardedVersion = registry.WriteAttachment(
                discardPass,
                loadedVersion,
                range,
                AttachmentLoadAction.Clear,
                AttachmentStoreAction.Discard);
            registry.ReadTexture(readPass, discardedVersion, range, GraphTextureUsage.SampledRead);

            Assert.That(registry.HasUninitializedReads, Is.True);
            Assert.That(registry.Accesses, Has.Length.EqualTo(4));
            Assert.That(registry.Accesses[0].IsWrite, Is.False);
            Assert.That(registry.Accesses[0].ContentsValid, Is.False);
            Assert.That(registry.Accesses[1].IsWrite, Is.True);
            Assert.That(registry.Accesses[1].ContentsValid, Is.False);
            Assert.That(registry.Accesses[2].IsWrite, Is.True);
            Assert.That(registry.Accesses[2].ContentsValid, Is.False);
            Assert.That(registry.Accesses[3].ContentsValid, Is.False);
            Assert.That(registry.TryOrder(out var orderedPasses), Is.True);
            ImmutableArray<RenderPassId> expectedOrder = [loadPass, discardPass, readPass];
            Assert.That(orderedPasses, Is.EqualTo(expectedOrder));
            var result = registry.ValidateCompilation();
            Assert.That(result.ContentsValid, Is.False);
            Assert.That(result.AccessesValid, Is.True);
        }
        finally
        {
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task GraphResourceRegistryTracksBufferRangeMembershipAndOverlaps()
    {
        var backendDevice = new FakeBackendDevice();
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var buffer = await graphicsDevice.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);
        var registry = new GraphResourceRegistry(graphicsDevice, graphOwnerId: 7);
        var options = new GraphImportOptions(
            GraphResourceState.Undefined,
            contentsValid: false,
            GraphImportCompletionPolicy.RequireIdle);

        try
        {
            var firstVersion = registry.ImportBuffer(buffer, new GraphBufferRange(0, 4), options);
            var secondVersion = registry.ImportBuffer(buffer, new GraphBufferRange(4, 4), options);
            var firstImport = registry.GetBufferImport(firstVersion);
            var secondImport = registry.GetBufferImport(secondVersion);

            Assert.That(firstVersion.Version, Is.Zero);
            Assert.That(secondVersion.Version, Is.Zero);
            Assert.That(firstVersion.ResourceId, Is.Not.EqualTo(secondVersion.ResourceId));
            Assert.That(firstImport.CaptureValidation().IsAdmissible, Is.True);
            Assert.That(firstImport.HasOverlappingImport, Is.False);
            Assert.That(secondImport.HasOverlappingImport, Is.False);

            var overlappingVersion = registry.ImportBuffer(buffer, new GraphBufferRange(2, 2), options);
            var overlappingImport = registry.GetBufferImport(overlappingVersion);
            Assert.That(firstImport.HasOverlappingImport, Is.True);
            Assert.That(secondImport.HasOverlappingImport, Is.False);
            Assert.That(overlappingImport.CaptureValidation().IsAdmissible, Is.False);
            Assert.That(registry.ValidateCompilation().ImportsValid, Is.False);

            var staleRegistry = new GraphResourceRegistry(graphicsDevice, graphOwnerId: 8);
            var staleVersion = staleRegistry.ImportBuffer(buffer, new GraphBufferRange(0, 1), options);
            _ = await graphicsDevice.UploadBufferAsync(buffer, destinationOffset: 0, [1]).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            var staleValidation = staleRegistry.GetBufferImport(staleVersion).CaptureValidation();
            Assert.That(staleValidation.StateMatches, Is.False);
            Assert.That(staleValidation.ContentsMatch, Is.False);
        }
        finally
        {
            buffer.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task GraphResourceRegistryTracksDisjointBufferStateRanges()
    {
        var backendDevice = new FakeBackendDevice();
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var registry = new GraphResourceRegistry(graphicsDevice, graphOwnerId: 7);
        var description = new BufferDescription(
            byteLength: 64,
            BufferUsage.Storage | BufferUsage.CopyDestination,
            GpuMemoryKind.DeviceLocal,
            structuredStride: null,
            debugName: "Transient graph buffer");

        try
        {
            var storageWriter = registry.AddPass(new RenderPassDescription("storage", 0, [], false));
            var copyWriter = registry.AddPass(new RenderPassDescription("copy", 1, [], false));
            var reader = registry.AddPass(new RenderPassDescription("reader", 2, [], false));
            var initialVersion = registry.CreateTransientBuffer(description);
            var storageVersion = registry.WriteBuffer(
                storageWriter,
                initialVersion,
                new GraphBufferRange(0, 16),
                GraphBufferUsage.StorageWrite);
            var copyVersion = registry.WriteBuffer(
                copyWriter,
                storageVersion,
                new GraphBufferRange(16, 16),
                GraphBufferUsage.CopyDestination);
            registry.ReadBuffer(
                reader,
                copyVersion,
                new GraphBufferRange(0, 16),
                GraphBufferUsage.StorageRead);

            var result = registry.ValidateCompilation();
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.StateTransitions, Has.Length.EqualTo(3));
            Assert.That(result.StateTransitions[0].PassId, Is.EqualTo(storageWriter));
            Assert.That(result.StateTransitions[0].BeforeState, Is.EqualTo(GraphResourceState.Undefined));
            Assert.That(result.StateTransitions[0].AfterState, Is.EqualTo(GraphResourceState.StorageWrite));
            Assert.That(result.StateTransitions[0].BufferRange, Is.EqualTo(new GraphBufferRange(0, 16)));
            Assert.That(result.StateTransitions[1].PassId, Is.EqualTo(copyWriter));
            Assert.That(result.StateTransitions[1].BeforeState, Is.EqualTo(GraphResourceState.Undefined));
            Assert.That(result.StateTransitions[1].AfterState, Is.EqualTo(GraphResourceState.CopyDestination));
            Assert.That(result.StateTransitions[1].BufferRange, Is.EqualTo(new GraphBufferRange(16, 16)));
            Assert.That(result.StateTransitions[2].PassId, Is.EqualTo(reader));
            Assert.That(result.StateTransitions[2].BeforeState, Is.EqualTo(GraphResourceState.StorageWrite));
            Assert.That(result.StateTransitions[2].AfterState, Is.EqualTo(GraphResourceState.StorageRead));
            Assert.That(result.StateTransitions[2].BufferRange, Is.EqualTo(new GraphBufferRange(0, 16)));
        }
        finally
        {
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task GraphResourceRegistryTracksDisjointTextureStateRanges()
    {
        var backendDevice = new FakeBackendDevice();
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var registry = new GraphResourceRegistry(graphicsDevice, graphOwnerId: 7);
        var description = new TextureDescription(
            TextureDimension.Dimension2D,
            8,
            8,
            1,
            1,
            2,
            TextureFormat.Rgba16Float,
            TextureUsage.Sampled | TextureUsage.Storage | TextureUsage.CopyDestination,
            "Transient graph texture");
        var mip0 = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);
        var mip1 = new GraphTextureRange(1, 1, 0, 1, TextureAspect.Color);
        var bothMips = new GraphTextureRange(0, 2, 0, 1, TextureAspect.Color);

        try
        {
            var storageWriter = registry.AddPass(new RenderPassDescription("storage", 0, [], false));
            var copyWriter = registry.AddPass(new RenderPassDescription("copy", 1, [], false));
            var reader = registry.AddPass(new RenderPassDescription("reader", 2, [], false));
            var initialVersion = registry.CreateTransientTexture(description);
            var storageVersion = registry.WriteTexture(
                storageWriter,
                initialVersion,
                mip0,
                GraphTextureUsage.StorageWrite);
            var copyVersion = registry.WriteTexture(
                copyWriter,
                storageVersion,
                mip1,
                GraphTextureUsage.CopyDestination);
            registry.ReadTexture(reader, copyVersion, bothMips, GraphTextureUsage.SampledRead);

            var result = registry.ValidateCompilation();
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.StateTransitions, Has.Length.EqualTo(4));
            Assert.That(result.StateTransitions[0].PassId, Is.EqualTo(storageWriter));
            Assert.That(result.StateTransitions[0].BeforeState, Is.EqualTo(GraphResourceState.Undefined));
            Assert.That(result.StateTransitions[0].AfterState, Is.EqualTo(GraphResourceState.StorageWrite));
            Assert.That(result.StateTransitions[0].TextureRange, Is.EqualTo(mip0));
            Assert.That(result.StateTransitions[1].PassId, Is.EqualTo(copyWriter));
            Assert.That(result.StateTransitions[1].BeforeState, Is.EqualTo(GraphResourceState.Undefined));
            Assert.That(result.StateTransitions[1].AfterState, Is.EqualTo(GraphResourceState.CopyDestination));
            Assert.That(result.StateTransitions[1].TextureRange, Is.EqualTo(mip1));
            Assert.That(result.StateTransitions[2].PassId, Is.EqualTo(reader));
            Assert.That(result.StateTransitions[2].BeforeState, Is.EqualTo(GraphResourceState.StorageWrite));
            Assert.That(result.StateTransitions[2].AfterState, Is.EqualTo(GraphResourceState.ShaderSampledRead));
            Assert.That(result.StateTransitions[2].TextureRange, Is.EqualTo(mip0));
            Assert.That(result.StateTransitions[3].PassId, Is.EqualTo(reader));
            Assert.That(result.StateTransitions[3].BeforeState, Is.EqualTo(GraphResourceState.CopyDestination));
            Assert.That(result.StateTransitions[3].AfterState, Is.EqualTo(GraphResourceState.ShaderSampledRead));
            Assert.That(result.StateTransitions[3].TextureRange, Is.EqualTo(mip1));
        }
        finally
        {
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task GraphResourceRegistryTracksImportedInitialStateTransitions()
    {
        var backendDevice = new FakeBackendDevice();
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var buffer = await graphicsDevice.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);
        var range = new GraphBufferRange(0, buffer.ByteLength);

        try
        {
            _ = await graphicsDevice.UploadBufferAsync(buffer, destinationOffset: 0, [1, 2, 3, 4, 5, 6, 7, 8])
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            var registry = new GraphResourceRegistry(graphicsDevice, graphOwnerId: 7);
            var version = registry.ImportBuffer(
                buffer,
                range,
                new GraphImportOptions(
                    GraphResourceState.General,
                    contentsValid: true,
                    GraphImportCompletionPolicy.RequireIdle));
            var importValidation = registry.GetBufferImport(version).CaptureValidation();
            Assert.That(importValidation.StateMatches, Is.True);
            Assert.That(importValidation.ContentsMatch, Is.True);
            Assert.That(importValidation.IsIdle, Is.True);
            var pass = registry.AddPass(new RenderPassDescription("copy", 0, [], false));
            registry.ReadBuffer(pass, version, range, GraphBufferUsage.CopySource);

            var result = registry.ValidateCompilation();
            Assert.That(result.AccessesValid, Is.True);
            Assert.That(result.ImportsValid, Is.True);
            Assert.That(result.ContentsValid, Is.True);
            Assert.That(result.DependenciesValid, Is.True);
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.StateTransitions, Has.Length.EqualTo(1));
            Assert.That(result.StateTransitions[0].BeforeState, Is.EqualTo(GraphResourceState.General));
            Assert.That(result.StateTransitions[0].AfterState, Is.EqualTo(GraphResourceState.CopySource));
        }
        finally
        {
            buffer.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task GraphResourceRegistryTracksTextureSubresourceMembership()
    {
        var backendDevice = new FakeBackendDevice();
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var textureDescription = new TextureDescription(
            TextureDimension.Dimension2D,
            4,
            4,
            1,
            2,
            3,
            TextureFormat.Rgba8Unorm,
            TextureUsage.Sampled,
            string.Empty);
        var texture = await graphicsDevice.CreateTextureAsync(textureDescription).ConfigureAwait(false);
        var registry = new GraphResourceRegistry(graphicsDevice, graphOwnerId: 7);
        var options = new GraphImportOptions(
            GraphResourceState.Undefined,
            contentsValid: false,
            GraphImportCompletionPolicy.RequireIdle);

        try
        {
            var firstVersion = registry.ImportTexture(
                texture,
                new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color),
                options);
            var secondVersion = registry.ImportTexture(
                texture,
                new GraphTextureRange(0, 1, 1, 1, TextureAspect.Color),
                options);
            var firstImport = registry.GetTextureImport(firstVersion);
            var secondImport = registry.GetTextureImport(secondVersion);
            Assert.That(firstImport.CaptureValidation().IsAdmissible, Is.True);
            Assert.That(firstImport.HasOverlappingImport, Is.False);
            Assert.That(secondImport.HasOverlappingImport, Is.False);

            var overlapVersion = registry.ImportTexture(
                texture,
                new GraphTextureRange(0, 1, 0, 2, TextureAspect.Color),
                options);
            var overlapImport = registry.GetTextureImport(overlapVersion);
            Assert.That(firstImport.HasOverlappingImport, Is.True);
            Assert.That(secondImport.HasOverlappingImport, Is.True);
            Assert.That(overlapImport.CaptureValidation().IsAdmissible, Is.False);
        }
        finally
        {
            texture.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task GraphResourceRegistryTracksTransientBufferAccessesAndContentValidity()
    {
        var backendDevice = new FakeBackendDevice();
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var registry = new GraphResourceRegistry(graphicsDevice, graphOwnerId: 7);
        var description = new BufferDescription(
            byteLength: 64,
            BufferUsage.Storage,
            GpuMemoryKind.DeviceLocal,
            structuredStride: null,
            debugName: "Transient graph buffer");

        try
        {
            var writer = registry.AddPass(new RenderPassDescription("write", 0, [], false));
            var reader = registry.AddPass(new RenderPassDescription("read", -1, [], false));
            var initialVersion = registry.CreateTransientBuffer(description);
            var writtenVersion = registry.WriteBuffer(
                writer,
                initialVersion,
                new GraphBufferRange(0, 16),
                GraphBufferUsage.StorageWrite);
            registry.ReadBuffer(
                reader,
                writtenVersion,
                new GraphBufferRange(0, 16),
                GraphBufferUsage.StorageRead);
            registry.ReadBuffer(
                reader,
                writtenVersion,
                new GraphBufferRange(16, 16),
                GraphBufferUsage.StorageRead);

            Assert.That(registry.HasUninitializedReads, Is.True);
            Assert.That(registry.Accesses, Has.Length.EqualTo(3));
            Assert.That(registry.Accesses[0].IsWrite, Is.True);
            Assert.That(registry.Accesses[1].ContentsValid, Is.True);
            Assert.That(registry.Accesses[2].ContentsValid, Is.False);
            Assert.That(registry.TryOrder(out var orderedPasses), Is.True);
            ImmutableArray<RenderPassId> expectedOrder = [writer, reader];
            Assert.That(orderedPasses, Is.EqualTo(expectedOrder));
            var invalidContentResult = registry.ValidateCompilation();
            Assert.That(invalidContentResult.IsValid, Is.False);
            Assert.That(invalidContentResult.AccessesValid, Is.True);
            Assert.That(invalidContentResult.ImportsValid, Is.True);
            Assert.That(invalidContentResult.ContentsValid, Is.False);
            Assert.That(invalidContentResult.DependenciesValid, Is.True);
            Assert.That(invalidContentResult.OrderedPasses, Is.Empty);

            var foreignRegistry = new GraphResourceRegistry(graphicsDevice, graphOwnerId: 8);
            var foreignVersion = foreignRegistry.CreateTransientBuffer(description);
            _ = Assert.Throws<ArgumentException>(
                () => registry.ReadBuffer(
                    reader,
                    foreignVersion,
                    new GraphBufferRange(0, 16),
                    GraphBufferUsage.StorageRead));
            _ = Assert.Throws<ArgumentException>(
                () => registry.ReadBuffer(
                    reader,
                    initialVersion,
                    new GraphBufferRange(0, 16),
                    GraphBufferUsage.VertexRead));
        }
        finally
        {
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task GraphResourceRegistryValidatesCompleteAccessPlan()
    {
        var backendDevice = new FakeBackendDevice();
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var registry = new GraphResourceRegistry(graphicsDevice, graphOwnerId: 7);
        var description = new BufferDescription(
            byteLength: 8,
            BufferUsage.Storage,
            GpuMemoryKind.DeviceLocal,
            structuredStride: null,
            debugName: "Transient graph buffer");

        try
        {
            var writer = registry.AddPass(new RenderPassDescription("write", 0, [], false));
            var reader = registry.AddPass(new RenderPassDescription("read", -1, [], false));
            var initialVersion = registry.CreateTransientBuffer(description);
            var writtenVersion = registry.WriteBuffer(
                writer,
                initialVersion,
                new GraphBufferRange(0, 8),
                GraphBufferUsage.StorageWrite);
            registry.ReadBuffer(
                reader,
                writtenVersion,
                new GraphBufferRange(0, 8),
                GraphBufferUsage.StorageRead);

            var result = registry.ValidateCompilation();
            ImmutableArray<RenderPassId> expectedOrder = [writer, reader];
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.AccessesValid, Is.True);
            Assert.That(result.ImportsValid, Is.True);
            Assert.That(result.ContentsValid, Is.True);
            Assert.That(result.DependenciesValid, Is.True);
            Assert.That(result.OrderedPasses, Is.EqualTo(expectedOrder));
            Assert.That(result.StateTransitions, Has.Length.EqualTo(2));
            Assert.That(result.StateTransitions[0].BeforeState, Is.EqualTo(GraphResourceState.Undefined));
            Assert.That(result.StateTransitions[0].AfterState, Is.EqualTo(GraphResourceState.StorageWrite));
            Assert.That(result.StateTransitions[1].BeforeState, Is.EqualTo(GraphResourceState.StorageWrite));
            Assert.That(result.StateTransitions[1].AfterState, Is.EqualTo(GraphResourceState.StorageRead));
        }
        finally
        {
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task GraphTextureAndPassIndicesUseExactOwnedCapacity()
    {
        var device = await CreateGraphicsDeviceAsync(new FakeBackendDevice()).ConfigureAwait(false);

        try
        {
            using var target = await device.CreateTextureAsync(CreateTextureDescription()).ConfigureAwait(false);
            using var transientTexture = await device.CreateTextureAsync(CreateTextureDescription()).ConfigureAwait(false);
            using var nonTransientTexture = await device.CreateTextureAsync(CreateTextureDescription()).ConfigureAwait(false);
            using var laterTransientTexture = await device.CreateTextureAsync(CreateTextureDescription()).ConfigureAwait(false);
            var range = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);
            var options = new GraphImportOptions(
                GraphResourceState.Undefined,
                contentsValid: false,
                completionPolicy: GraphImportCompletionPolicy.RequireIdle);
            var additionalTextureImports = ImmutableArray.Create(
                new GraphTextureImportRecord(
                    transientTexture,
                    new GraphTextureVersion(device.DeviceEpoch, 90, 0, transientTexture.Description, range),
                    options,
                    isTransient: true),
                new GraphTextureImportRecord(
                    nonTransientTexture,
                    new GraphTextureVersion(device.DeviceEpoch, 90, 0, nonTransientTexture.Description, range),
                    options),
                new GraphTextureImportRecord(
                    laterTransientTexture,
                    new GraphTextureVersion(device.DeviceEpoch, 12, 0, laterTransientTexture.Description, range),
                    options,
                    isTransient: true));
            var orderedPasses = ImmutableArray.Create(
                new RenderPassId(9, 4),
                new RenderPassId(2, 7),
                new RenderPassId(9, 1));
            var expectedCharge = GraphicsGraphSubmission.CalculatePreflightWorkspaceBackingByteCharge(
                bufferImportCapacity: 0,
                textureImportCapacity: 4,
                orderedPassCapacity: orderedPasses.Length,
                bufferAliasingCapacity: 0,
                textureAliasingCapacity: 0,
                bufferImportFlagCapacity: 0,
                textureImportFlagCapacity: 4,
                passValidationCapacity: 0);
            var submission = CreateBufferImportIndexSubmission(
                device,
                target,
                [],
                expectedCharge,
                orderedPasses,
                additionalTextureImports);

            Assert.That(submission.TextureImportIndexStorageCapacity, Is.EqualTo(4));
            Assert.That(submission.TextureImportIndexBackingByteCharge, Is.EqualTo(80));
            Assert.That(submission.OrderedPassIndexStorageCapacity, Is.EqualTo(3));
            Assert.That(submission.OrderedPassIndexBackingByteCharge, Is.EqualTo(80));
            Assert.That(submission.TryGetIndexedTextureImport(90, out var observedTransient), Is.True);
            Assert.That(observedTransient, Is.SameAs(additionalTextureImports[0]));
            Assert.That(submission.TryGetIndexedTextureImport(12, out var observedLaterTransient), Is.True);
            Assert.That(observedLaterTransient, Is.SameAs(additionalTextureImports[2]));
            Assert.That(submission.TryGetIndexedTextureImport(1, out _), Is.False);
            Assert.That(submission.TryGetOrderedPassIndex(orderedPasses[0], out var firstPassIndex), Is.True);
            Assert.That(firstPassIndex, Is.EqualTo(0));
            Assert.That(submission.TryGetOrderedPassIndex(orderedPasses[1], out var secondPassIndex), Is.True);
            Assert.That(secondPassIndex, Is.EqualTo(1));
            Assert.That(submission.TryGetOrderedPassIndex(orderedPasses[2], out var thirdPassIndex), Is.True);
            Assert.That(thirdPassIndex, Is.EqualTo(2));
            Assert.That(submission.TryGetOrderedPassIndex(new RenderPassId(3, 5), out _), Is.False);

            var before = GC.GetAllocatedBytesForCurrentThread();
            var matches = true;

            for (var index = 0; index < 32; index++)
            {
                matches &= submission.TryGetIndexedTextureImport(90, out var observedTexture)
                    && ReferenceEquals(observedTexture, additionalTextureImports[0]);
                matches &= submission.TryGetOrderedPassIndex(orderedPasses[2], out var observedPassIndex)
                    && (observedPassIndex == 2);
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(matches, Is.True);
            Assert.That(allocated, Is.Zero);

            _ = Assert.Throws<OutOfMemoryException>(
                () => CreateBufferImportIndexSubmission(
                    device,
                    target,
                    [],
                    expectedCharge - 1,
                    orderedPasses,
                    additionalTextureImports));
        }
        finally
        {
            await device.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task GraphicsDeviceShutdownQuarantinesBackendWhenDisposalFails()
    {
        var backendDevice = new FakeBackendDevice(throwOnDispose: true);
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var shutdown = graphicsDevice.ShutdownAsync();
        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await shutdown.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            ?? throw new InvalidOperationException("Backend disposal failure returned no shutdown exception.");

        Assert.That(exception.Message, Is.EqualTo("The fake backend disposal failed."));
        Assert.That(graphicsDevice.Status, Is.EqualTo(GraphicsDeviceStatus.Stopping));
        Assert.That(backendDevice.DeviceDisposalCount, Is.EqualTo(1));
    }

    [Test]
    public async Task GraphicsDeviceStartupCancellationDisposesCreatedBackend()
    {
        using var createEntered = new ManualResetEventSlim(initialState: false);
        using var continueCreate = new ManualResetEventSlim(initialState: false);
        using var cancellationSource = new CancellationTokenSource();
        var backendDevice = new FakeBackendDevice();
        var registration = new GraphicsBackendRegistration(
            new FakeBackendFactory(backendDevice, createEntered, continueCreate));
        var startup = GraphicsDevice.CreateAsync(
            ownerId: 31,
            registration,
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None,
            cancellationSource.Token);

        try
        {
            Assert.That(createEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
        }
        finally
        {
            await cancellationSource.CancelAsync().ConfigureAwait(false);
            continueCreate.Set();
        }

        _ = Assert.ThrowsAsync<TaskCanceledException>(
            async () => await startup.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
        Assert.That(backendDevice.DeviceDisposalCount, Is.EqualTo(1));
    }

    [Test]
    public async Task GraphicsDeviceStartupDisposesBackendWhenCapabilityAdmissionFails()
    {
        var backendDevice = new FakeBackendDevice(capabilities: CreateCapabilities(includeShaderModel6: false));
        var registration = new GraphicsBackendRegistration(new FakeBackendFactory(backendDevice));
        var startup = GraphicsDevice.CreateAsync(
            ownerId: 31,
            registration,
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None,
            CancellationToken.None);

        _ = Assert.ThrowsAsync<NotSupportedException>(
            async () => await startup.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
        Assert.That(backendDevice.DeviceDisposalCount, Is.EqualTo(1));
    }

    [Test]
    public async Task GraphicsDeviceStartupQuarantinesBackendWhenAdmissionCleanupFails()
    {
        var backendDevice = new FakeBackendDevice(
            capabilities: CreateCapabilities(includeShaderModel6: false),
            throwOnDispose: true);
        var registration = new GraphicsBackendRegistration(new FakeBackendFactory(backendDevice));
        var startup = GraphicsDevice.CreateAsync(
            ownerId: 31,
            registration,
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None,
            CancellationToken.None);

        var exception = Assert.ThrowsAsync<AggregateException>(
            async () => await startup.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            ?? throw new InvalidOperationException("Startup cleanup failure returned no aggregate exception.");

        Assert.That(exception.InnerExceptions, Has.Count.EqualTo(2));
        Assert.That(exception.InnerExceptions[0], Is.TypeOf<NotSupportedException>());
        Assert.That(exception.InnerExceptions[1].Message, Is.EqualTo("The fake backend disposal failed."));
        Assert.That(backendDevice.DeviceDisposalCount, Is.EqualTo(1));
    }

    [Test]
    public async Task PreclaimedReadbackAccountsForActualCpuBackingThroughLeaseDisposal()
    {
        var backend = new FakeBackendDevice();
        var device = await CreateGraphicsDeviceAsync(backend).ConfigureAwait(false);
        using var source = await device.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);
        var footprint = new BufferCompletionFootprint(1, alignment: 16);
        using var destination = GraphicsBufferReadbackStorage.Create(device, 1, footprint.TotalByteAllowance, alignment: 16);
        var admission = destination.Admission ?? throw new InvalidOperationException("The readback has no Core admission.");

        try
        {
            _ = await device.UploadBufferAsync(source, 0, [0x42]).ConfigureAwait(false);
            Assert.That(admission.NativeByteCharge, Is.EqualTo(16));
            using var result = await device.ReadbackBufferAsync(source, 0, 1, destination, CancellationToken.None).ConfigureAwait(false);
            using var lease = result.AcquireReadLease(0, 1);
            var action = new AdmittedByteReadAction();
            lease.UseWindow(0, 1, ref action);
            Assert.That(action.Value, Is.EqualTo(0x42));
            Assert.That(result.ByteLength, Is.EqualTo((nint)1));
            Assert.That(backend.ReadbackBeginCount, Is.EqualTo(1));
            result.Dispose();
            destination.Dispose();
            Assert.That(admission.NativeByteCharge, Is.EqualTo(16));
            lease.Dispose();
            Assert.That(admission.NativeByteCharge, Is.Zero);
            Assert.That(admission.ManagedByteCharge, Is.EqualTo(BufferCompletionFootprint.ManagedByteAllowance));
        }
        finally
        {
            source.Dispose();
            await device.ShutdownAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task PreclaimedReadbackRefusesAnInsufficientCoreAllowanceBeforeBackendAdmission()
    {
        var backend = new FakeBackendDevice();
        var device = await CreateGraphicsDeviceAsync(backend).ConfigureAwait(false);

        try
        {
            var footprint = new BufferCompletionFootprint(1, alignment: 16);
            _ = Assert.Throws<OutOfMemoryException>(
                () => GraphicsBufferReadbackStorage.Create(device, 1, footprint.TotalByteAllowance - 1, alignment: 16));
            Assert.That(backend.ReadbackBeginCount, Is.Zero);
        }
        finally
        {
            await device.ShutdownAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task PreclaimedReadbackRejectsAnUnqualifiedDestination()
    {
        var backend = new FakeBackendDevice();
        var device = await CreateGraphicsDeviceAsync(backend).ConfigureAwait(false);
        using var source = await device.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);
        using var destination = GraphicsBufferReadbackStorage.Create(device, 1);

        try
        {
            _ = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await device.ReadbackBufferAsync(source, 0, 1, destination, CancellationToken.None).ConfigureAwait(false));
            Assert.That(backend.ReadbackBeginCount, Is.Zero);
        }
        finally
        {
            source.Dispose();
            await device.ShutdownAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task PreclaimedReadbackRemainsCallerOwnedOnPreCancellation()
    {
        var backend = new FakeBackendDevice();
        var device = await CreateGraphicsDeviceAsync(backend).ConfigureAwait(false);
        using var source = await device.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);
        using var destination = GraphicsBufferReadbackStorage.Create(device, 1, 16 * 1024 * 1024);
        var admission = destination.Admission ?? throw new InvalidOperationException("The readback has no Core admission.");
        using var cancellation = new CancellationTokenSource();

        try
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
            _ = Assert.ThrowsAsync<OperationCanceledException>(
                async () => await device.ReadbackBufferAsync(source, 0, 1, destination, cancellation.Token).ConfigureAwait(false));
            Assert.That(backend.ReadbackBeginCount, Is.Zero);
            Assert.That(admission.NativeByteCharge, Is.EqualTo(1));
            destination.Dispose();
            Assert.That(admission.NativeByteCharge, Is.Zero);
        }
        finally
        {
            source.Dispose();
            await device.ShutdownAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task PreclaimedReadbackRetainsItsCoreChargeInUncertainWorkQuarantine()
    {
        var backend = new FakeBackendDevice {
            FailReadbackFenceWait = true,
        };
        var device = await CreateGraphicsDeviceAsync(backend).ConfigureAwait(false);
        using var source = await device.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);
        using var destination = GraphicsBufferReadbackStorage.Create(device, 1, 16 * 1024 * 1024);
        var admission = destination.Admission ?? throw new InvalidOperationException("The readback has no Core admission.");

        try
        {
            _ = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await device.ReadbackBufferAsync(source, 0, 1, destination, CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
            destination.Dispose();
            Assert.That(device.Status, Is.AnyOf(GraphicsDeviceStatus.Lost, GraphicsDeviceStatus.Stopping, GraphicsDeviceStatus.Disposed));
            Assert.That(admission.NativeByteCharge, Is.EqualTo(1));
            Assert.That(admission.ManagedByteCharge, Is.GreaterThan(0));
        }
        finally
        {
            backend.FailReadbackFenceWait = false;
            source.Dispose();
            var failure = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await device.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
            Assert.That(failure?.Message, Is.EqualTo("The controlled readback fence wait failed."));
            Assert.That(admission.NativeByteCharge, Is.EqualTo(1));
        }
    }

    [Test]
    [SuppressMessage(
        "Usage",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "This test disposes buffers while their already-admitted transfers are queued behind a blocked owner operation.")]
    public async Task QueuedTransfersRetainBuffersWhenRetirementRunsFirst()
    {
        using var continueUpload = new ManualResetEventSlim(initialState: false);
        using var uploadEntered = new ManualResetEventSlim(initialState: false);
        var backendDevice = new FakeBackendDevice(uploadEntered, continueUpload);
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var blocker = await graphicsDevice.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);
        var uploadDestination = await graphicsDevice.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);
        var readbackSource = await graphicsDevice.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);

        try
        {
            var blockerTask = graphicsDevice.UploadBufferAsync(blocker, destinationOffset: 0, [1]);
            Assert.That(uploadEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);

            var queuedUploadTask = graphicsDevice.UploadBufferAsync(uploadDestination, destinationOffset: 0, [2]);
            var queuedReadbackTask = graphicsDevice.ReadbackBufferAsync(readbackSource, sourceOffset: 0, byteLength: 1);
            uploadDestination.Dispose();
            readbackSource.Dispose();

            Assert.That(queuedUploadTask.IsCompleted, Is.False);
            Assert.That(queuedReadbackTask.IsCompleted, Is.False);

            continueUpload.Set();
            _ = await blockerTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(
                (await queuedUploadTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false)).Status,
                Is.EqualTo(UploadStatus.Ready));
            using var readback = await queuedReadbackTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(readback.ByteLength, Is.EqualTo((nint)1));

            blocker.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(backendDevice.BufferDisposalCount, Is.EqualTo(3));
        }
        finally
        {
            continueUpload.Set();
            blocker.Dispose();
            uploadDestination.Dispose();
            readbackSource.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public void RecordedCommandReadCopiesShareOneReleaseBoundary()
    {
        var owner = new RecordedPassCommandList(
            new RenderPassId(300, 0),
            CommandRecorderPassKind.Compute,
            [new RecordedGraphicsCommand(GraphicsCommandKind.Dispatch)],
            []);
        var firstReadState = 0;
        var firstRead = owner.ReadCommands(ref firstReadState);
        var copiedRead = firstRead;
        firstRead.Dispose();

        var secondReadState = 0;
        var secondRead = owner.ReadCommands(ref secondReadState);
        owner.Dispose();
        copiedRead.Dispose();
        Assert.That(secondRead.Commands.Length, Is.EqualTo(1));
        secondRead.Dispose();

        var refusedReadState = 0;
        var refusedRead = false;

        try
        {
            using var read = owner.ReadCommands(ref refusedReadState);
        }
        catch (ObjectDisposedException)
        {
            refusedRead = true;
        }

        Assert.That(refusedRead, Is.True);
    }

    [Test]
    public async Task ResourceUseWaitersObserveOnlyTheirCapturedSequence()
    {
        var backendDevice = new FakeBackendDevice();
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var buffer = await graphicsDevice.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);

        try
        {
            using var firstUse = buffer.ResourceOwner.AcquireUse(nameof(buffer));
            using var secondUse = buffer.ResourceOwner.AcquireUse(nameof(buffer));
            (var state, var contentsValid, var lastUseSequence, var isIdle) =
                buffer.ResourceOwner.CaptureGraphImportState(new GraphBufferRange(0, buffer.ByteLength));
            var firstUseCompletion = buffer.ResourceOwner.WaitForUsesAsync(firstUse.Sequence);
            var allUseCompletion = buffer.ResourceOwner.WaitForUsesAsync(lastUseSequence);

            Assert.That(state, Is.EqualTo(GraphResourceState.Undefined));
            Assert.That(contentsValid, Is.False);
            Assert.That(lastUseSequence, Is.EqualTo(secondUse.Sequence));
            Assert.That(isIdle, Is.False);
            Assert.That(firstUseCompletion.IsCompleted, Is.False);
            Assert.That(allUseCompletion.IsCompleted, Is.False);

            secondUse.Dispose();
            Assert.That(firstUseCompletion.IsCompleted, Is.False);
            Assert.That(allUseCompletion.IsCompleted, Is.False);

            firstUse.Dispose();
            await firstUseCompletion.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await allUseCompletion.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(buffer.ResourceOwner.CaptureGraphImportState(new GraphBufferRange(0, buffer.ByteLength)).IsIdle, Is.True);

            using var laterUse = buffer.ResourceOwner.AcquireUse(nameof(buffer));
            await buffer.ResourceOwner.WaitForUsesAsync(lastUseSequence).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(buffer.ResourceOwner.CaptureGraphImportState(new GraphBufferRange(0, buffer.ByteLength)).IsIdle, Is.False);
        }
        finally
        {
            buffer.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task SamplerCreationCancellationRetiresAfterBackendCreation()
    {
        using var cancellationSource = new CancellationTokenSource();
        using var samplerCreationEntered = new ManualResetEventSlim(initialState: false);
        using var continueSamplerCreation = new ManualResetEventSlim(initialState: false);
        using var samplerDisposed = new ManualResetEventSlim(initialState: false);
        var backendDevice = new FakeBackendDevice();
        backendDevice.ConfigureSamplerCreationGate(samplerCreationEntered, continueSamplerCreation, samplerDisposed);
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);

        try
        {
            var samplerTask = graphicsDevice.CreateSamplerAsync(CreateSamplerDescription(), cancellationSource.Token);
            Assert.That(samplerCreationEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);

            await cancellationSource.CancelAsync().ConfigureAwait(false);
            Assert.That(backendDevice.SamplerDisposalCount, Is.Zero);

            continueSamplerCreation.Set();
            _ = Assert.ThrowsAsync<TaskCanceledException>(
                async () => _ = await samplerTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
            Assert.That(samplerDisposed.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(backendDevice.SamplerDisposalCount, Is.EqualTo(1));
        }
        finally
        {
            continueSamplerCreation.Set();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task SamplerCreationClampsAnisotropyAndRetiresOnOwner()
    {
        var backendDevice = new FakeBackendDevice();
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var sampler = await graphicsDevice.CreateSamplerAsync(CreateSamplerDescription())
            .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        try
        {
            Assert.That(sampler.Handle.IsValid, Is.True);
            Assert.That(sampler.DeviceEpoch, Is.EqualTo(graphicsDevice.DeviceEpoch));
            Assert.That(backendDevice.EffectiveSamplerAnisotropy, Is.EqualTo(1.0f));

            sampler.Dispose();
            sampler = null;
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(backendDevice.SamplerDisposalCount, Is.EqualTo(1));
        }
        finally
        {
            sampler?.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task SamplerCreationPreCancellationDoesNotReachBackend()
    {
        using var cancellationSource = new CancellationTokenSource();
        var backendDevice = new FakeBackendDevice();
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);

        try
        {
            await cancellationSource.CancelAsync().ConfigureAwait(false);
            var samplerTask = graphicsDevice.CreateSamplerAsync(CreateSamplerDescription(), cancellationSource.Token);
            _ = Assert.ThrowsAsync<OperationCanceledException>(
                async () => _ = await samplerTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
            Assert.That(backendDevice.SamplerCreationCount, Is.Zero);
        }
        finally
        {
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task SharedNativeHeapBudgetAccountsForBuffersAndTexturesTogether()
    {
        var backendDevice = new FakeBackendDevice(
            textureBudgetBytes: 32,
            textureAllocationSizeBytes: 24,
            bufferBudgetBytes: 32,
            bufferAllocationSizeBytes: 8);
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        GpuTexture? texture = null;
        GpuBuffer? buffer = null;

        try
        {
            texture = await graphicsDevice.CreateTextureAsync(CreateTextureDescription())
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            buffer = await graphicsDevice.CreateBufferAsync(CreateBufferDescription())
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            Assert.That(backendDevice.TextureCreationCount, Is.EqualTo(1));
            Assert.That(backendDevice.BufferCreationCount, Is.EqualTo(1));
            _ = Assert.ThrowsAsync<OutOfMemoryException>(
                async () => await graphicsDevice.CreateBufferAsync(CreateBufferDescription())
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false));
            Assert.That(backendDevice.BufferCreationCount, Is.EqualTo(1));

            texture.Dispose();
            texture = null;
            Assert.That(
                SpinWait.SpinUntil(() => backendDevice.TextureDisposalCount == 1, TimeSpan.FromSeconds(5)),
                Is.True);

            using var replacementBuffer = await graphicsDevice.CreateBufferAsync(CreateBufferDescription())
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            Assert.That(backendDevice.BufferCreationCount, Is.EqualTo(2));
        }
        finally
        {
            texture?.Dispose();
            buffer?.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }

        Assert.That(backendDevice.TextureDisposalCount, Is.EqualTo(1));
        Assert.That(backendDevice.BufferDisposalCount, Is.EqualTo(2));
    }

    [Test]
    public async Task SpanUploadCopiesCallerBytesAndReadbackReturnsTheRequestedRange()
    {
        var backendDevice = new FakeBackendDevice();
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var buffer = await graphicsDevice.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);

        try
        {
            (var initialState, var initialContentsValid, var initialLastUseSequence, var initialIsIdle) =
                buffer.ResourceOwner.CaptureGraphImportState(new GraphBufferRange(0, buffer.ByteLength));
            Assert.That(initialState, Is.EqualTo(GraphResourceState.Undefined));
            Assert.That(initialContentsValid, Is.False);
            Assert.That(initialLastUseSequence, Is.Zero);
            Assert.That(initialIsIdle, Is.True);

            var source = new byte[] { 1, 2, 3, 4 };
            var uploadTask = graphicsDevice.UploadBufferAsync(buffer, destinationOffset: 2, source);
            source[0] = byte.MaxValue;

            var receipt = await uploadTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(receipt.Status, Is.EqualTo(UploadStatus.Ready));
            Assert.That(receipt.DeviceEpoch, Is.EqualTo(graphicsDevice.DeviceEpoch));
            Assert.That(receipt.CompletedFence, Is.EqualTo(1));
            Assert.That(buffer.ResourceOwner.LastSubmittedQueueFence, Is.EqualTo(receipt.CompletedFence));

            (var uploadedState, var uploadedContentsValid, var lastUseSequence, var isIdle) =
                buffer.ResourceOwner.CaptureGraphImportState(new GraphBufferRange(2, 4));
            (var untouchedState, var untouchedContentsValid, var untouchedSequence, var untouchedIsIdle) =
                buffer.ResourceOwner.CaptureGraphImportState(new GraphBufferRange(0, 2));
            Assert.That(uploadedState, Is.EqualTo(GraphResourceState.General));
            Assert.That(uploadedContentsValid, Is.True);
            Assert.That(untouchedState, Is.EqualTo(GraphResourceState.General));
            Assert.That(untouchedContentsValid, Is.False);
            Assert.That(lastUseSequence, Is.EqualTo(1));
            Assert.That(isIdle, Is.True);
            Assert.That(untouchedSequence, Is.EqualTo(1));
            Assert.That(untouchedIsIdle, Is.True);

            using var readback = await graphicsDevice.ReadbackBufferAsync(buffer, sourceOffset: 1, byteLength: 6)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            using var readbackLease = readback.AcquireReadLease(0, 6);
            var bytes = new byte[6];
            var copyAction = new ReadbackCopyAction(bytes);
            readbackLease.UseWindow(0, bytes.Length, ref copyAction);

            Assert.That(bytes, Is.EqualTo(new byte[] { 0, 1, 2, 3, 4, 0 }));
            ArgumentOutOfRangeException? readbackException = null;

            try
            {
                _ = await graphicsDevice.ReadbackBufferAsync(buffer, sourceOffset: 8, byteLength: 1).ConfigureAwait(false);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                readbackException = exception;
            }

            Assert.That(readbackException, Is.Not.Null);
        }
        finally
        {
            buffer.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task SubmissionRecordingRootsDetachOnlyAfterSafeRelease(bool quarantine, bool readThrows)
    {
        var backend = new FakeBackendDevice();
        var device = await CreateGraphicsDeviceAsync(backend).ConfigureAwait(false);
        using var target = await device.CreateTextureAsync(CreateTextureDescription()).ConfigureAwait(false);
        using var buffer = await device.CreateBufferAsync(new BufferDescription(
            64, BufferUsage.Storage, GpuMemoryKind.DeviceLocal, 16, "recorded-binding")).ConfigureAwait(false);
        using var readEntered = new ManualResetEventSlim(initialState: false);
        using var continueRead = new ManualResetEventSlim(initialState: false);
        using var lastReadEntered = new ManualResetEventSlim(initialState: false);
        using var continueLastRead = new ManualResetEventSlim(initialState: false);
        GraphicsResourceOwner.UseLease? fixtureUse = null;
        RecordedPassCommandList? recorded = null;
        Task? readerTask = null;
        Task? lastReaderTask = null;

        try
        {
            var resources = new GraphResourceRegistry(device, graphOwnerId: 300);
            var passId = resources.AddPass(new RenderPassDescription("recorded-owner", 0, [], false));
            var version = resources.ImportTexture(
                target,
                new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color),
                new GraphImportOptions(GraphResourceState.Undefined, false, GraphImportCompletionPolicy.RequireIdle));
            var bufferVersion = resources.ImportBuffer(
                buffer,
                new GraphBufferRange(0, buffer.ByteLength),
                new GraphImportOptions(GraphResourceState.Undefined, false, GraphImportCompletionPolicy.RequireIdle));
            var binding = new BufferBinding(
                ShaderBindingGroup.Frame, 0, ShaderResourceKind.StorageBuffer, bufferVersion, bufferVersion.Range);
            var bindings = new BindingSet([binding], [], []);
            ImmutableArray<RecordedGraphicsCommand> backing = [
                new RecordedGraphicsCommand(GraphicsCommandKind.SetBindings, bindings: bindings),
            ];
            fixtureUse = buffer.ResourceOwner.AcquireUse("recorded command owner");
            recorded = new RecordedPassCommandList(
                passId,
                CommandRecorderPassKind.Compute,
                backing,
                [fixtureUse]);
            var submission = new GraphicsGraphSubmission(
                device.DeviceEpoch,
                target,
                surfaceTarget: null,
                version,
                surfaceEpoch: 0,
                swapchainEpoch: 0,
                [passId],
                [new GraphicsGraphSubmissionPass(passId, CommandRecorderPassKind.Compute, null, recorded, [])],
                [],
                [],
                [],
                [],
                resources.BufferImports,
                resources.TextureImports,
                [recorded]);
            var retainedPassArray = submission.Passes;
            var retainedPass = retainedPassArray[0];
            var retainedOwners = submission.RecordedPassCommandLists;
            var retainedOwner = retainedOwners[0];
            Assert.That(retainedPass.CommandList, Is.SameAs(recorded));
            Assert.That(retainedOwner, Is.SameAs(recorded));
            RecordedGraphicsCommand retainedCommand;

            var initialReadState = 0;
            using (var commandRead = retainedPass.ReadCommands(ref initialReadState))
            {
                Assert.That(commandRead.Matches(retainedOwner, backing), Is.True);
                retainedCommand = commandRead.Commands[0];
            }

            var retainedBindings = retainedCommand.Bindings
                ?? throw new InvalidOperationException("The recorded binding command lost its binding set.");
            var retainedBuffers = retainedBindings.Buffers;
            var retainedBinding = retainedBuffers[0];
            var wrongPass = retainedPass with { PassId = new RenderPassId(301, 0) };
            var wrongKind = retainedPass with { Kind = CommandRecorderPassKind.Copy };
            var terminalPass = retainedPass with { Kind = CommandRecorderPassKind.Presentation };
            _ = Assert.Throws<InvalidOperationException>(() => {
                var readState = 0;
                using var commandRead = wrongPass.ReadCommands(ref readState);
            });
            _ = Assert.Throws<InvalidOperationException>(() => {
                var readState = 0;
                using var commandRead = wrongKind.ReadCommands(ref readState);
            });
            _ = Assert.Throws<InvalidOperationException>(() => {
                var readState = 0;
                using var commandRead = terminalPass.ReadCommands(ref readState);
            });

            readerTask = Task.Run(() => {
                var readState = 0;
                using var commandRead = retainedPass.ReadCommands(ref readState);
                readEntered.Set();
                Assert.That(continueRead.Wait(TimeSpan.FromSeconds(30)), Is.True);
                Assert.That(commandRead.Matches(retainedOwner, backing), Is.True);
                Assert.That(commandRead.Commands[0].Bindings, Is.SameAs(bindings));
                Assert.That(fixtureUse.IsOwnedBy(buffer.ResourceOwner), Is.True);

                if (readThrows)
                {
                    throw new InvalidOperationException("Synchronous command reader failed.");
                }
            });
            Assert.That(readEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            lastReaderTask = Task.Run(() => {
                var readState = 0;
                using var commandRead = retainedOwner.ReadCommands(ref readState);
                lastReadEntered.Set();
                Assert.That(continueLastRead.Wait(TimeSpan.FromSeconds(30)), Is.True);
                Assert.That(commandRead.Matches(retainedOwner, backing), Is.True);
                Assert.That(commandRead.Commands[0].Bindings, Is.SameAs(bindings));
                Assert.That(fixtureUse.IsOwnedBy(buffer.ResourceOwner), Is.True);
            });
            Assert.That(lastReadEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);

            void CloseOnOwner(scoped ref CommandRecorder recorder)
            {
                if (quarantine)
                {
                    submission.RetainRecordingForUncertainSubmission();
                    submission.RetainRecordingForUncertainSubmission();
                }

                submission.DisposeRecordedPassCommandLists();
                recorded.Dispose();
                submission.DisposeRecordedPassCommandLists();
                retainedOwner.Dispose();
            }

            using var closeRecording = await device.RecordPassAsync(
                new RenderPassId(301, 1), CommandRecorderPassKind.Copy, [], [], null, CloseOnOwner)
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(readerTask.IsCompleted, Is.False);
            Assert.That(buffer.ResourceOwner.HasActiveUses, Is.True);
            Assert.That(submission.Passes.IsEmpty, Is.EqualTo(!quarantine));
            Assert.That(submission.RecordedPassCommandLists.IsEmpty, Is.EqualTo(!quarantine));
            _ = Assert.Throws<ObjectDisposedException>(() => {
                var readState = 0;
                using var commandRead = retainedOwner.ReadCommands(ref readState);
            });
            _ = Assert.Throws<ObjectDisposedException>(() => {
                var readState = 0;
                using var commandRead = retainedPass.ReadCommands(ref readState);
            });
            _ = Assert.Throws<ObjectDisposedException>(() => {
                var readState = 0;
                using var commandRead = retainedPassArray[0].ReadCommands(ref readState);
            });

            if (!quarantine)
            {
                _ = Assert.Throws<ObjectDisposedException>(submission.RetainRecordingForUncertainSubmission);
                _ = Assert.Throws<ObjectDisposedException>(retainedOwner.RetainForUncertainSubmission);
            }

            continueRead.Set();

            if (readThrows)
            {
                var exception = Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await readerTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
                Assert.That(exception?.Message, Is.EqualTo("Synchronous command reader failed."));
            }
            else
            {
                await readerTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }

            Assert.That(lastReaderTask.IsCompleted, Is.False);
            Assert.That(buffer.ResourceOwner.HasActiveUses, Is.True);
            continueLastRead.Set();
            await lastReaderTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(buffer.ResourceOwner.HasActiveUses, Is.EqualTo(quarantine));
            Assert.That(fixtureUse.IsOwnedBy(buffer.ResourceOwner), Is.EqualTo(quarantine));
            var backingField = typeof(RecordedPassCommandList).GetField("_commands", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("The private command backing was not found.");
            var remainingBacking = (ImmutableArray<RecordedGraphicsCommand>)(backingField.GetValue(retainedOwner)
                ?? throw new InvalidOperationException("The command backing was uninitialized."));
            Assert.That(remainingBacking.IsEmpty, Is.EqualTo(!quarantine));
            Assert.That(remainingBacking == backing, Is.EqualTo(quarantine));
            Assert.That(retainedPass.CommandList, Is.SameAs(retainedOwner));
            Assert.That(retainedPassArray[0].CommandList, Is.SameAs(retainedOwner));
            Assert.That(retainedOwners[0], Is.SameAs(retainedOwner));
            Assert.That(retainedCommand.Bindings, Is.SameAs(bindings));
            Assert.That(retainedBindings, Is.SameAs(bindings));
            Assert.That(retainedBuffers == bindings.Buffers, Is.True);
            Assert.That(retainedBuffers.Length, Is.EqualTo(1));
            Assert.That(retainedBinding, Is.SameAs(binding));
            Assert.That(retainedBinding.Resource, Is.SameAs(bufferVersion));
            retainedOwner.Dispose();
            submission.DisposeRecordedPassCommandLists();
            _ = Assert.Throws<ObjectDisposedException>(() => {
                var readState = 0;
                using var commandRead = retainedOwner.ReadCommands(ref readState);
            });
            _ = Assert.Throws<ObjectDisposedException>(submission.ValidateNativeRasterSubmission);
        }
        finally
        {
            continueRead.Set();
            continueLastRead.Set();

            if (readerTask is not null)
            {
                await readerTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }

            if (lastReaderTask is not null)
            {
                await lastReaderTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }

            recorded?.Dispose();
            fixtureUse?.Dispose();
            buffer.Dispose();
            target.Dispose();
            await device.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task TextureAllocationAndRetirementReservationsRecoverAfterBackendCreationFailures()
    {
        const int failureCount = 257;
        var backendDevice = new FakeBackendDevice(
            textureAllocationSizeBytes: 300L * 1024 * 1024,
            textureCreationFailureCount: failureCount);
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        GpuTexture? texture = null;

        try
        {
            for (var index = 0; index < failureCount; index++)
            {
                _ = Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await graphicsDevice.CreateTextureAsync(CreateTextureDescription()).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
            }

            texture = await graphicsDevice.CreateTextureAsync(CreateTextureDescription()).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(backendDevice.TextureCreationCount, Is.EqualTo(failureCount + 1));
        }
        finally
        {
            texture?.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }

        Assert.That(backendDevice.TextureDisposalCount, Is.EqualTo(1));
    }

    [Test]
    public async Task TextureAllocationBudgetAcceptsExactPerDeviceLimit()
    {
        var backendDevice = new FakeBackendDevice(textureAllocationSizeBytes: 512L * 1024 * 1024);
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        GpuTexture? texture = null;

        try
        {
            texture = await graphicsDevice.CreateTextureAsync(CreateTextureDescription()).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(backendDevice.TextureCreationCount, Is.EqualTo(1));
        }
        finally
        {
            texture?.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }

        Assert.That(backendDevice.TextureDisposalCount, Is.EqualTo(1));
    }

    [Test]
    public async Task TextureAllocationBudgetRejectsBeforeBackendCreationAndLeavesDeviceUsable()
    {
        var backendDevice = new FakeBackendDevice();
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        GpuTexture? texture = null;

        try
        {
            var oversizedTexture = new TextureDescription(
                TextureDimension.Dimension2D,
                width: 16384,
                height: 16384,
                depth: 1,
                arrayLayers: 1,
                mipLevels: 1,
                TextureFormat.Rgba8Unorm,
                TextureUsage.Sampled | TextureUsage.CopySource | TextureUsage.CopyDestination,
                debugName: "Oversized texture allocation test");
            _ = Assert.ThrowsAsync<OutOfMemoryException>(
                async () => await graphicsDevice.CreateTextureAsync(oversizedTexture).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));

            Assert.That(backendDevice.TextureCreationCount, Is.Zero);

            texture = await graphicsDevice.CreateTextureAsync(CreateTextureDescription()).ConfigureAwait(false);
            Assert.That(backendDevice.TextureCreationCount, Is.EqualTo(1));
        }
        finally
        {
            texture?.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }

        Assert.That(backendDevice.TextureDisposalCount, Is.EqualTo(1));
    }

    [Test]
    public async Task TextureNativeHeapBudgetAcceptsExactAllocationSize()
    {
        var backendDevice = new FakeBackendDevice(textureBudgetBytes: 24);
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        GpuTexture? texture = null;

        try
        {
            texture = await graphicsDevice.CreateTextureAsync(CreateTextureDescription()).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(backendDevice.TextureCreationCount, Is.EqualTo(1));
        }
        finally
        {
            texture?.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }

        Assert.That(backendDevice.TextureDisposalCount, Is.EqualTo(1));
    }

    [Test]
    public async Task TextureNativeHeapBudgetRejectsBeforeBackendCreation()
    {
        var backendDevice = new FakeBackendDevice(textureBudgetBytes: 23);
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);

        try
        {
            _ = Assert.ThrowsAsync<OutOfMemoryException>(
                async () => await graphicsDevice.CreateTextureAsync(CreateTextureDescription()).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));

            Assert.That(backendDevice.TextureCreationCount, Is.Zero);
        }
        finally
        {
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task TextureSubresourceUploadTracksStateAndContentsPerMipAndLayer()
    {
        var backendDevice = new FakeBackendDevice();
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var description = new TextureDescription(
            TextureDimension.Dimension2D,
            width: 4,
            height: 4,
            depth: 1,
            arrayLayers: 2,
            mipLevels: 3,
            TextureFormat.Rgba8Unorm,
            TextureUsage.CopySource | TextureUsage.CopyDestination,
            debugName: "Graph subresource state test");
        var texture = await graphicsDevice.CreateTextureAsync(description).ConfigureAwait(false);
        using var sourceOwner = UnmanagedBuffer<byte>.Allocate(64, new BufferAllocationOptions(64, 1));
        sourceOwner.CopyFrom(new byte[64], 0);
        using var sourceLease = sourceOwner.AcquireRead(0, 64);

        try
        {
            var uploadedSubresource = new TextureSubresource(0, 1, TextureAspect.Color);
            var receipt = await graphicsDevice.UploadTextureAsync(
                texture,
                uploadedSubresource,
                sourceLease,
                sourceOffset: 0,
                new TextureDataLayout(rowPitch: 16, slicePitch: 64)).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(receipt.Status, Is.EqualTo(UploadStatus.Ready));

            (var uploadedState, var uploadedContents) = texture.ResourceOwner.GetGraphImportState(
                new GraphTextureRange(0, 1, 1, 1, TextureAspect.Color));
            (var untouchedState, var untouchedContents) = texture.ResourceOwner.GetGraphImportState(
                new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color));
            Assert.That(uploadedState, Is.EqualTo(GraphResourceState.General));
            Assert.That(uploadedContents, Is.True);
            Assert.That(untouchedState, Is.EqualTo(GraphResourceState.Undefined));
            Assert.That(untouchedContents, Is.False);
            _ = Assert.Throws<InvalidOperationException>(
                () => texture.ResourceOwner.GetGraphImportState(new GraphTextureRange(0, 1, 0, 2, TextureAspect.Color)));
        }
        finally
        {
            texture.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task TextureUploadRepackagesRowsAndReadbackZeroesExposedPadding()
    {
        var backendDevice = new FakeBackendDevice();
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var texture = await graphicsDevice.CreateTextureAsync(CreateTextureDescription()).ConfigureAwait(false);
        var subresource = new TextureSubresource(0, 0, TextureAspect.Color);
        var graphRange = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);
        var sourceBytes = new byte[40];
        Array.Fill(sourceBytes, (byte)0xEE);

        for (var row = 0; row < 2; row++)
        {
            for (var index = 0; index < 12; index++)
            {
                sourceBytes[2 + (row * 16) + index] = checked((byte)((row * 12) + index + 1));
            }
        }

        var sourceOwner = UnmanagedBuffer<byte>.Allocate(40, new BufferAllocationOptions(40, 1));
        sourceOwner.CopyFrom(sourceBytes, 0);
        var sourceLease = sourceOwner.AcquireRead(0, 40);
        var sourceLeaseDisposed = false;
        var sourceOwnerDisposed = false;

        try
        {
            (var initialState, var initialContentsValid, var initialLastUseSequence, var initialIsIdle) =
                texture.ResourceOwner.CaptureGraphImportState(graphRange);
            Assert.That(initialState, Is.EqualTo(GraphResourceState.Undefined));
            Assert.That(initialContentsValid, Is.False);
            Assert.That(initialLastUseSequence, Is.Zero);
            Assert.That(initialIsIdle, Is.True);

            var receipt = await graphicsDevice.UploadTextureAsync(
                texture,
                subresource,
                sourceLease,
                sourceOffset: 2,
                new TextureDataLayout(rowPitch: 16, slicePitch: 32)).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(receipt.Status, Is.EqualTo(UploadStatus.Ready));
            Assert.That(receipt.CompletedFence, Is.EqualTo(1));
            Assert.That(texture.ResourceOwner.LastSubmittedQueueFence, Is.EqualTo(receipt.CompletedFence));
            (var uploadedState, var uploadedContentsValid, var lastUseSequence, var isIdle) =
                texture.ResourceOwner.CaptureGraphImportState(graphRange);
            Assert.That(uploadedState, Is.EqualTo(GraphResourceState.General));
            Assert.That(uploadedContentsValid, Is.True);
            Assert.That(lastUseSequence, Is.EqualTo(1));
            Assert.That(isIdle, Is.True);

            sourceLease.Dispose();
            sourceLeaseDisposed = true;
            sourceOwner.Dispose();
            sourceOwnerDisposed = true;

            using var readback = await graphicsDevice.ReadbackTextureAsync(
                texture,
                subresource).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            using var readbackLease = readback.AcquireReadLease();
            var actual = new byte[32];
            var copyAction = new ReadbackCopyAction(actual);
            readbackLease.UseWindow(0, actual.Length, ref copyAction);
            var expected = new byte[32];

            for (var row = 0; row < 2; row++)
            {
                for (var index = 0; index < 12; index++)
                {
                    expected[(row * 16) + index] = checked((byte)((row * 12) + index + 1));
                }
            }

            Assert.That(readback.Format, Is.EqualTo(TextureFormat.Rgba8Unorm));
            Assert.That(readback.Width, Is.EqualTo(3));
            Assert.That(readback.Height, Is.EqualTo(2));
            Assert.That(readback.Depth, Is.EqualTo(1));
            Assert.That(readback.RowPitch, Is.EqualTo((nint)16));
            Assert.That(readback.SlicePitch, Is.EqualTo((nint)32));
            Assert.That(readback.ByteLength, Is.EqualTo((nint)32));
            Assert.That(actual, Is.EqualTo(expected));

            texture.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(backendDevice.TextureDisposalCount, Is.EqualTo(1));
        }
        finally
        {
            if (!sourceLeaseDisposed)
            {
                sourceLease.Dispose();
            }

            if (!sourceOwnerDisposed)
            {
                sourceOwner.Dispose();
            }

            texture.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task TransientBufferGroupDefersAllocationReleaseUntilActiveUseCompletes()
    {
        const long exactBudgetBytes = 16;
        var backendDevice = new FakeBackendDevice(
            bufferAllocationSizeBytes: exactBudgetBytes,
            bufferBudgetBytes: exactBudgetBytes);
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var registry = new GraphResourceRegistry(graphicsDevice, graphOwnerId: 7);
        var description = new BufferDescription(
            byteLength: exactBudgetBytes,
            BufferUsage.Storage,
            GpuMemoryKind.DeviceLocal,
            structuredStride: null,
            debugName: "Transient group active-use test");
        var versions = ImmutableArray.Create(
            registry.CreateTransientBuffer(description),
            registry.CreateTransientBuffer(description));
        GraphicsTransientBufferGroup? group = null;
        GraphicsResourceOwner.UseLease? use = null;

        try
        {
            group = await graphicsDevice.CreateTransientBufferGroupAsync(
                    versions,
                    CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            var resourceOwner = group.Buffers[0].ResourceOwner;
            use = resourceOwner.AcquireUse(nameof(group));
            group.Dispose();
            group = null;

            var shutdownTask = graphicsDevice.ShutdownAsync();

            Assert.That(
                SpinWait.SpinUntil(() => backendDevice.BufferDisposalCount == 1, TimeSpan.FromSeconds(5)),
                Is.True);
            Assert.That(resourceOwner.IsRetirementRequested, Is.True);
            Assert.That(resourceOwner.HasActiveUses, Is.True);
            Assert.That(backendDevice.BufferAllocationDisposalCount, Is.Zero);
            Assert.That(shutdownTask.IsCompleted, Is.False);

            use.Dispose();
            use = null;
            await shutdownTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(backendDevice.BufferAllocationDisposalCount, Is.EqualTo(1));
            Assert.That(backendDevice.BufferDisposalCount, Is.EqualTo(2));
        }
        finally
        {
            use?.Dispose();
            group?.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [TestCase(1, 0)]
    [TestCase(0, 1)]
    [TestCase(0, 2)]
    public async Task TransientBufferGroupRollbackReleasesSharedAllocationAndBudget(
        int bufferAllocationCreationFailureCount,
        int bufferCreationFailureAt)
    {
        const long bufferLengthBytes = 16;
        const long exactBudgetBytes = 256L * 1024 * 1024;
        var backendDevice = new FakeBackendDevice(
            bufferAllocationSizeBytes: exactBudgetBytes,
            bufferBudgetBytes: exactBudgetBytes,
            bufferAllocationCreationFailureCount: bufferAllocationCreationFailureCount,
            bufferCreationFailureAt: bufferCreationFailureAt);
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var registry = new GraphResourceRegistry(graphicsDevice, graphOwnerId: 7);
        var description = new BufferDescription(
            byteLength: bufferLengthBytes,
            BufferUsage.Storage,
            GpuMemoryKind.DeviceLocal,
            structuredStride: null,
            debugName: "Transient group rollback test");
        var versions = ImmutableArray.Create(
            registry.CreateTransientBuffer(description),
            registry.CreateTransientBuffer(description));
        GraphicsTransientBufferGroup? group = null;

        try
        {
            _ = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await graphicsDevice.CreateTransientBufferGroupAsync(
                        versions,
                        CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false));

            Assert.That(backendDevice.BufferAllocationCreationCount, Is.EqualTo(1));
            Assert.That(
                backendDevice.BufferAllocationDisposalCount,
                Is.EqualTo(bufferAllocationCreationFailureCount == 0 ? 1 : 0));
            Assert.That(backendDevice.BufferDisposalCount, Is.EqualTo(Math.Max(bufferCreationFailureAt - 1, 0)));

            group = await graphicsDevice.CreateTransientBufferGroupAsync(
                    versions,
                    CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);

            Assert.That(group.Buffers, Has.Length.EqualTo(2));
            group.Dispose();
            group = null;

            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(backendDevice.BufferAllocationCreationCount, Is.EqualTo(2));
            Assert.That(
                backendDevice.BufferAllocationDisposalCount,
                Is.EqualTo(2 - bufferAllocationCreationFailureCount));
            Assert.That(backendDevice.BufferCreationCount, Is.EqualTo(bufferCreationFailureAt + 2));
            Assert.That(
                backendDevice.BufferDisposalCount,
                Is.EqualTo(Math.Max(bufferCreationFailureAt - 1, 0) + 2));
        }
        finally
        {
            group?.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task TransientTextureAllocationPoolReusesAllocationsAcrossConcurrentWaves()
    {
        const int waveCount = 32;
        const int liveGroupCount = 4;
        const long allocationSizeBytes = 16;
        const long exactBudgetBytes = liveGroupCount * allocationSizeBytes;
        var backendDevice = new FakeBackendDevice(
            textureBudgetBytes: exactBudgetBytes,
            textureAllocationSizeBytes: allocationSizeBytes);
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var registry = new GraphResourceRegistry(graphicsDevice, graphOwnerId: 7);
        var description = new TextureDescription(
            TextureDimension.Dimension2D,
            width: 2,
            height: 2,
            depth: 1,
            arrayLayers: 1,
            mipLevels: 1,
            TextureFormat.Rgba8Unorm,
            TextureUsage.Sampled | TextureUsage.CopySource | TextureUsage.CopyDestination,
            debugName: "Transient texture allocation pool stress");
        var versions = ImmutableArray.Create(
            registry.CreateTransientTexture(description),
            registry.CreateTransientTexture(description));
        var groups = new List<GraphicsTransientTextureGroup>(liveGroupCount);

        try
        {
            for (var wave = 0; wave < waveCount; wave++)
            {
                for (var index = 0; index < liveGroupCount; index++)
                {
                    groups.Add(await graphicsDevice.CreateTransientTextureGroupAsync(
                            versions,
                            CancellationToken.None)
                        .WaitAsync(TimeSpan.FromSeconds(5))
                        .ConfigureAwait(false));
                }

                var allocationLeases = new HashSet<GraphicsResourceAllocationLease>();

                foreach (var group in groups)
                {
                    var allocationLease = group.Textures[0].ResourceOwner.AllocationLease
                        ?? throw new InvalidOperationException("A live transient texture group lost its shared allocation lease.");
                    Assert.That(allocationLeases.Add(allocationLease), Is.True);
                    Assert.That(group.Textures[1].ResourceOwner.AllocationLease, Is.SameAs(allocationLease));
                }

                while (groups.Count > 0)
                {
                    var lastGroupIndex = groups.Count - 1;
                    var group = groups[lastGroupIndex];
                    groups.RemoveAt(lastGroupIndex);
                    group.Dispose();
                }

                var expectedTextureDisposals = (wave + 1) * liveGroupCount * versions.Length;
                Assert.That(
                    SpinWait.SpinUntil(
                        () => backendDevice.TextureDisposalCount == expectedTextureDisposals,
                        TimeSpan.FromSeconds(5)),
                    Is.True);
                Assert.That(backendDevice.TextureAllocationCreationCount, Is.EqualTo(liveGroupCount));
                Assert.That(backendDevice.TextureAllocationDisposalCount, Is.Zero);

                foreach (var allocationLease in allocationLeases)
                {
                    Assert.That(
                        SpinWait.SpinUntil(() => allocationLease.ReferenceCount == 1, TimeSpan.FromSeconds(5)),
                        Is.True);
                }
            }

            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(backendDevice.TextureAllocationCreationCount, Is.EqualTo(liveGroupCount));
            Assert.That(backendDevice.TextureAllocationDisposalCount, Is.EqualTo(liveGroupCount));
            Assert.That(
                backendDevice.TextureDisposalCount,
                Is.EqualTo(waveCount * liveGroupCount * versions.Length));
        }
        finally
        {
            foreach (var group in groups)
            {
                group.Dispose();
            }

            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task TransientTextureGroupRetainsSharedAllocationUntilActiveUseCompletes()
    {
        const long exactBudgetBytes = 16;
        var backendDevice = new FakeBackendDevice(textureBudgetBytes: exactBudgetBytes);
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var registry = new GraphResourceRegistry(graphicsDevice, graphOwnerId: 7);
        var description = new TextureDescription(
            TextureDimension.Dimension2D,
            width: 2,
            height: 2,
            depth: 1,
            arrayLayers: 1,
            mipLevels: 1,
            TextureFormat.Rgba8Unorm,
            TextureUsage.Sampled | TextureUsage.CopySource | TextureUsage.CopyDestination,
            debugName: "Transient texture group active-use test");
        var versions = ImmutableArray.Create(
            registry.CreateTransientTexture(description),
            registry.CreateTransientTexture(description));
        GraphicsTransientTextureGroup? group = null;
        GraphicsResourceOwner.UseLease? use = null;

        try
        {
            group = await graphicsDevice.CreateTransientTextureGroupAsync(
                    versions,
                    CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            var resourceOwner = group.Textures[0].ResourceOwner;
            use = resourceOwner.AcquireUse(nameof(group));
            group.Dispose();
            group = null;

            var shutdownTask = graphicsDevice.ShutdownAsync();

            Assert.That(
                SpinWait.SpinUntil(() => backendDevice.TextureDisposalCount == 1, TimeSpan.FromSeconds(5)),
                Is.True);
            Assert.That(resourceOwner.IsRetirementRequested, Is.True);
            Assert.That(resourceOwner.HasActiveUses, Is.True);
            Assert.That(backendDevice.TextureAllocationDisposalCount, Is.Zero);
            Assert.That(shutdownTask.IsCompleted, Is.False);

            use.Dispose();
            use = null;
            await shutdownTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(backendDevice.TextureAllocationCreationCount, Is.EqualTo(1));
            Assert.That(backendDevice.TextureAllocationDisposalCount, Is.EqualTo(1));
            Assert.That(backendDevice.TextureDisposalCount, Is.EqualTo(2));
        }
        finally
        {
            use?.Dispose();
            group?.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [TestCase(1, 0)]
    [TestCase(0, 1)]
    [TestCase(0, 2)]
    public async Task TransientTextureGroupRollbackReleasesSharedAllocationAndBudget(
        int textureAllocationCreationFailureCount,
        int textureCreationFailureAt)
    {
        const long exactBudgetBytes = 16;
        var backendDevice = new FakeBackendDevice(
            textureBudgetBytes: exactBudgetBytes,
            textureAllocationSizeBytes: exactBudgetBytes,
            textureAllocationCreationFailureCount: textureAllocationCreationFailureCount,
            textureCreationFailureAt: textureCreationFailureAt);
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var registry = new GraphResourceRegistry(graphicsDevice, graphOwnerId: 7);
        var description = new TextureDescription(
            TextureDimension.Dimension2D,
            width: 2,
            height: 2,
            depth: 1,
            arrayLayers: 1,
            mipLevels: 1,
            TextureFormat.Rgba8Unorm,
            TextureUsage.Sampled | TextureUsage.CopySource | TextureUsage.CopyDestination,
            debugName: "Transient texture group rollback test");
        var versions = ImmutableArray.Create(
            registry.CreateTransientTexture(description),
            registry.CreateTransientTexture(description));
        GraphicsTransientTextureGroup? group = null;

        try
        {
            _ = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await graphicsDevice.CreateTransientTextureGroupAsync(
                        versions,
                        CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false));

            Assert.That(backendDevice.TextureAllocationCreationCount, Is.EqualTo(1));
            Assert.That(
                backendDevice.TextureAllocationDisposalCount,
                Is.EqualTo(textureAllocationCreationFailureCount == 0 ? 1 : 0));
            Assert.That(backendDevice.TextureDisposalCount, Is.EqualTo(Math.Max(textureCreationFailureAt - 1, 0)));

            group = await graphicsDevice.CreateTransientTextureGroupAsync(
                    versions,
                    CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            Assert.That(group.Textures, Has.Length.EqualTo(2));
            group.Dispose();
            group = null;
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(backendDevice.TextureAllocationCreationCount, Is.EqualTo(2));
            Assert.That(
                backendDevice.TextureAllocationDisposalCount,
                Is.EqualTo(2 - textureAllocationCreationFailureCount));
            Assert.That(backendDevice.TextureCreationCount, Is.EqualTo(textureCreationFailureAt + 2));
            Assert.That(
                backendDevice.TextureDisposalCount,
                Is.EqualTo(Math.Max(textureCreationFailureAt - 1, 0) + 2));
        }
        finally
        {
            group?.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The upload write is awaited before disposal; its exclusive resource lease also protects a timed-out request.")]
    public async Task UploadBufferHostWriteMarksContentsAndRejectsAnActiveGpuUse()
    {
        var backendDevice = new FakeBackendDevice(capabilities: CreateCapabilities(includeUploadHeap: true));
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        GpuBuffer? buffer = null;

        try
        {
            buffer = await graphicsDevice.CreateBufferAsync(
                new BufferDescription(
                    16,
                    BufferUsage.CopySource,
                    GpuMemoryKind.Upload,
                    structuredStride: null,
                    debugName: "Host-written upload buffer"))
                .ConfigureAwait(false);
            var payload = new byte[] { 0x13, 0x29, 0x4F, 0x97 };
            var expected = payload.AsSpan().ToArray();
            var writeTask = graphicsDevice.WriteUploadBufferAsync(buffer, 4, payload);
            payload.AsSpan().Clear();
            await writeTask
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);

            var (state, contentsValid, _, isIdle) = buffer.ResourceOwner.CaptureGraphImportState(
                new GraphBufferRange(4, payload.Length));
            Assert.That(state, Is.EqualTo(GraphResourceState.General));
            Assert.That(contentsValid, Is.True);
            Assert.That(isIdle, Is.True);
            Assert.That(buffer.ResourceOwner.LastSubmittedQueueFence, Is.Zero);

            using var readback = await graphicsDevice.ReadbackBufferAsync(buffer, 4, payload.Length)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            using var readLease = readback.AcquireReadLease(0, payload.Length);
            var actual = new byte[payload.Length];
            var copyAction = new ReadbackCopyAction(actual);
            readLease.UseWindow(0, actual.Length, ref copyAction);
            Assert.That(actual, Is.EqualTo(expected));

            using var activeUse = buffer.ResourceOwner.AcquireUse(nameof(buffer));
            _ = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await graphicsDevice.WriteUploadBufferAsync(buffer, 0, payload).ConfigureAwait(false));
        }
        finally
        {
            buffer?.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task VolumeTextureTransferHonorsDepthSlicePitchAndZeroesPadding()
    {
        var backendDevice = new FakeBackendDevice();
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var texture = await graphicsDevice.CreateTextureAsync(CreateVolumeTextureDescription()).ConfigureAwait(false);
        var sourceBytes = new byte[16];
        Array.Fill(sourceBytes, (byte)0xEE);
        sourceBytes[1] = 1;
        sourceBytes[2] = 2;
        sourceBytes[5] = 3;
        sourceBytes[6] = 4;
        sourceBytes[9] = 5;
        sourceBytes[10] = 6;
        sourceBytes[13] = 7;
        sourceBytes[14] = 8;
        var sourceOwner = UnmanagedBuffer<byte>.Allocate(16, new BufferAllocationOptions(16, 1));
        sourceOwner.CopyFrom(sourceBytes, 0);
        var sourceLease = sourceOwner.AcquireRead(0, 16);
        var sourceLeaseDisposed = false;
        var sourceOwnerDisposed = false;

        try
        {
            _ = await graphicsDevice.UploadTextureAsync(
                texture,
                new TextureSubresource(0, 0, TextureAspect.Color),
                sourceLease,
                sourceOffset: 1,
                new TextureDataLayout(rowPitch: 4, slicePitch: 8)).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            sourceLease.Dispose();
            sourceLeaseDisposed = true;
            sourceOwner.Dispose();
            sourceOwnerDisposed = true;

            using var readback = await graphicsDevice.ReadbackTextureAsync(
                texture,
                new TextureSubresource(0, 0, TextureAspect.Color)).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            using var readbackLease = readback.AcquireReadLease();
            var actual = new byte[24];
            var copyAction = new ReadbackCopyAction(actual);
            readbackLease.UseWindow(0, actual.Length, ref copyAction);
            var expected = new byte[24];
            expected[0] = 1;
            expected[1] = 2;
            expected[6] = 3;
            expected[7] = 4;
            expected[12] = 5;
            expected[13] = 6;
            expected[18] = 7;
            expected[19] = 8;

            Assert.That(readback.Width, Is.EqualTo(2));
            Assert.That(readback.Height, Is.EqualTo(2));
            Assert.That(readback.Depth, Is.EqualTo(2));
            Assert.That(readback.RowPitch, Is.EqualTo((nint)6));
            Assert.That(readback.SlicePitch, Is.EqualTo((nint)12));
            Assert.That(readback.ByteLength, Is.EqualTo((nint)24));
            Assert.That(actual, Is.EqualTo(expected));
        }
        finally
        {
            if (!sourceLeaseDisposed)
            {
                sourceLease.Dispose();
            }

            if (!sourceOwnerDisposed)
            {
                sourceOwner.Dispose();
            }

            texture.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    private struct AdmittedByteReadAction : IAction<ReadOnlySpan<byte>>
    {
        public byte Value { get; private set; }

        public void Invoke(scoped ReadOnlySpan<byte> argument)
            => Value = argument[0];
    }

    private sealed class FakeBackendBuffer : IGraphicsBackendBuffer
    {
        private readonly FakeBackendDevice _device;
        private int _isDisposed;

        internal FakeBackendBuffer(FakeBackendDevice device, BufferDescription description)
        {
            _device = device;
            Bytes = new byte[checked((int)description.ByteLength)];
        }

        internal byte[] Bytes { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                _device.BufferDisposed(this);
            }
        }
    }

    private sealed class FakeBackendBufferAllocation : IGraphicsBackendBufferAllocation
    {
        private readonly FakeBackendDevice _device;
        private int _isDisposed;

        internal FakeBackendBufferAllocation(FakeBackendDevice device)
        {
            _device = device;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                _device.BufferAllocationDisposed();
            }
        }
    }

    private sealed class FakeBackendBufferReadback : IGraphicsBackendBufferReadback
    {
        private byte[]? _bytes;
        private readonly Action? _copyObserver;
        private int _state;

        internal FakeBackendBufferReadback(byte[] bytes, Action? copyObserver)
        {
            _copyObserver = copyObserver;
            _bytes = bytes;
        }

        public long CompletionFence => 1;

        public void CopyCompleted(BufferWriteLease<byte> destination)
        {
            ArgumentNullException.ThrowIfNull(destination);

            if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            {
                throw new InvalidOperationException("The fake buffer readback has already been copied or disposed.");
            }

            _copyObserver?.Invoke();
            var bytes = Interlocked.Exchange(ref _bytes, null)
                ?? throw new InvalidOperationException("The fake buffer readback has no owned bytes.");
            var action = new ReadbackCopyAction(bytes);
            destination.UseWindow(0, bytes.Length, ref action);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _state, 2) != 2)
            {
                _ = Interlocked.Exchange(ref _bytes, null);
            }
        }

        private readonly struct ReadbackCopyAction(byte[] bytes) : IAction<Span<byte>>
        {
            private readonly byte[] _bytes = bytes;

            public void Invoke(scoped Span<byte> argument)
                => _bytes.AsSpan().CopyTo(argument);
        }
    }

    private sealed class FakeBackendDevice : IGraphicsBackendDevice
    {
        private int _bufferAllocationCreationCount;
        private int _bufferAllocationDisposalCount;
        private readonly long? _bufferAllocationSizeBytes;
        private readonly long _bufferBudgetBytes;
        private int _bufferCreationCount;
        private readonly int _bufferCreationFailureAt;
        private int _bufferDisposalCount;
        private readonly List<byte[]> _buffers = [];
        private readonly GraphicsCapabilities _capabilities;
        private long _completedFence;
        private readonly ManualResetEventSlim? _continueReadback;
        private ManualResetEventSlim? _continueSamplerCreation;
        private readonly ManualResetEventSlim? _continueUpload;
        private int _deviceDisposalCount;
        private int _effectiveSamplerAnisotropyBits;
        private byte[]? _lastBufferBytes;
        private int _readbackBeginCount;
        private int _readbackBeginThreadId;
        private int _readbackCopyThreadId;
        private readonly ManualResetEventSlim? _readbackWaitEntered;
        private int _readbackWaitThreadId;
        private int _remainingBufferAllocationCreationFailures;
        private int _remainingBufferCreationFailures;
        private int _remainingTextureAllocationCreationFailures;
        private int _remainingTextureCreationFailures;
        private int _samplerCreationCount;
        private ManualResetEventSlim? _samplerCreationEntered;
        private int _samplerDisposalCount;
        private ManualResetEventSlim? _samplerDisposed;
        private int _textureAllocationCreationCount;
        private int _textureAllocationDisposalCount;
        private readonly long? _textureAllocationSizeBytes;
        private readonly long _textureBudgetBytes;
        private int _textureCreationCount;
        private readonly int _textureCreationFailureAt;
        private int _textureDisposalCount;
        private readonly bool _throwOnDispose;
        private readonly ManualResetEventSlim? _uploadEntered;

        internal FakeBackendDevice(
            ManualResetEventSlim entered,
            ManualResetEventSlim continueWork,
            bool isReadbackWait = false)
        {
            if (isReadbackWait)
            {
                _readbackWaitEntered = entered;
                _continueReadback = continueWork;
            }
            else
            {
                _uploadEntered = entered;
                _continueUpload = continueWork;
            }

            _bufferBudgetBytes = 3L * 1024 * 1024 * 1024;
            _textureBudgetBytes = 3L * 1024 * 1024 * 1024;
            _capabilities = CreateCapabilities();
        }

        internal FakeBackendDevice(
            ManualResetEventSlim uploadEntered,
            ManualResetEventSlim continueUpload,
            ManualResetEventSlim readbackWaitEntered,
            ManualResetEventSlim continueReadback)
        {
            _uploadEntered = uploadEntered;
            _continueUpload = continueUpload;
            _readbackWaitEntered = readbackWaitEntered;
            _continueReadback = continueReadback;
            _bufferBudgetBytes = 3L * 1024 * 1024 * 1024;
            _textureBudgetBytes = 3L * 1024 * 1024 * 1024;
            _capabilities = CreateCapabilities();
        }

        internal FakeBackendDevice(
            long textureBudgetBytes = 3L * 1024 * 1024 * 1024,
            long? textureAllocationSizeBytes = null,
            int textureCreationFailureCount = 0,
            int textureAllocationCreationFailureCount = 0,
            int textureCreationFailureAt = 0,
            long? bufferAllocationSizeBytes = null,
            long bufferBudgetBytes = 3L * 1024 * 1024 * 1024,
            int bufferAllocationCreationFailureCount = 0,
            int bufferCreationFailureCount = 0,
            int bufferCreationFailureAt = 0,
            GraphicsCapabilities? capabilities = null,
            bool throwOnDispose = false)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(textureBudgetBytes);
            ArgumentOutOfRangeException.ThrowIfNegative(textureCreationFailureCount);
            ArgumentOutOfRangeException.ThrowIfNegative(textureAllocationCreationFailureCount);
            ArgumentOutOfRangeException.ThrowIfNegative(textureCreationFailureAt);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferBudgetBytes);
            ArgumentOutOfRangeException.ThrowIfNegative(bufferAllocationCreationFailureCount);
            ArgumentOutOfRangeException.ThrowIfNegative(bufferCreationFailureCount);
            ArgumentOutOfRangeException.ThrowIfNegative(bufferCreationFailureAt);

            if (textureAllocationSizeBytes is <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(textureAllocationSizeBytes));
            }

            if (bufferAllocationSizeBytes is <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bufferAllocationSizeBytes));
            }

            _textureBudgetBytes = textureBudgetBytes;
            _textureAllocationSizeBytes = textureAllocationSizeBytes;
            _remainingTextureAllocationCreationFailures = textureAllocationCreationFailureCount;
            _remainingTextureCreationFailures = textureCreationFailureCount;
            _textureCreationFailureAt = textureCreationFailureAt;
            _bufferAllocationSizeBytes = bufferAllocationSizeBytes;
            _bufferBudgetBytes = bufferBudgetBytes;
            _remainingBufferAllocationCreationFailures = bufferAllocationCreationFailureCount;
            _remainingBufferCreationFailures = bufferCreationFailureCount;
            _bufferCreationFailureAt = bufferCreationFailureAt;
            _capabilities = capabilities ?? CreateCapabilities();
            _throwOnDispose = throwOnDispose;
        }

        public GraphicsCapabilities Capabilities => _capabilities;

        internal int BufferAllocationCreationCount => Volatile.Read(ref _bufferAllocationCreationCount);

        internal int BufferAllocationDisposalCount => Volatile.Read(ref _bufferAllocationDisposalCount);

        internal int BufferCreationCount => Volatile.Read(ref _bufferCreationCount);

        internal int BufferDisposalCount => Volatile.Read(ref _bufferDisposalCount);

        internal int DeviceDisposalCount => Volatile.Read(ref _deviceDisposalCount);

        internal float EffectiveSamplerAnisotropy
            => BitConverter.Int32BitsToSingle(Volatile.Read(ref _effectiveSamplerAnisotropyBits));

        internal bool FailReadbackFenceWait { get; set; }

        internal byte[]? LastBufferBytes => _lastBufferBytes;

        internal int ReadbackBeginCount => Volatile.Read(ref _readbackBeginCount);

        internal int ReadbackBeginThreadId => Volatile.Read(ref _readbackBeginThreadId);

        internal int ReadbackCopyThreadId => Volatile.Read(ref _readbackCopyThreadId);

        internal int ReadbackWaitThreadId => Volatile.Read(ref _readbackWaitThreadId);

        internal int SamplerCreationCount => Volatile.Read(ref _samplerCreationCount);

        internal int SamplerDisposalCount => Volatile.Read(ref _samplerDisposalCount);

        internal int TextureAllocationCreationCount => Volatile.Read(ref _textureAllocationCreationCount);

        internal int TextureAllocationDisposalCount => Volatile.Read(ref _textureAllocationDisposalCount);

        internal int TextureCreationCount => Volatile.Read(ref _textureCreationCount);

        internal int TextureDisposalCount => Volatile.Read(ref _textureDisposalCount);

        private static FakeBackendBuffer GetBuffer(IGraphicsBackendBuffer buffer)
            => buffer as FakeBackendBuffer
                ?? throw new ArgumentException("The buffer belongs to a different fake backend.", nameof(buffer));

        private static FakeBackendTexture GetTexture(IGraphicsBackendTexture texture)
            => texture as FakeBackendTexture
                ?? throw new ArgumentException("The texture belongs to a different fake backend.", nameof(texture));

        public IGraphicsBackendBufferReadback BeginBufferReadback(
            IGraphicsBackendBuffer source,
            GraphResourceState sourceState,
            long sourceOffset,
            nint byteLength)
        {
            var buffer = GetBuffer(source);
            var bytes = buffer.Bytes.AsSpan(checked((int)sourceOffset), checked((int)byteLength)).ToArray();
            _ = Interlocked.Increment(ref _readbackBeginCount);
            Volatile.Write(ref _readbackBeginThreadId, Environment.CurrentManagedThreadId);
            return new FakeBackendBufferReadback(
                bytes,
                () => Volatile.Write(ref _readbackCopyThreadId, Environment.CurrentManagedThreadId));
        }

        public IGraphicsBackendBuffer CreateBuffer(
            BufferDescription description,
            IGraphicsBackendBufferAllocation allocation)
        {
            ArgumentNullException.ThrowIfNull(allocation);

            if (allocation is not FakeBackendBufferAllocation)
            {
                throw new ArgumentException("The allocation belongs to a different fake backend.", nameof(allocation));
            }

            return CreateBufferCore(description);
        }

        public IGraphicsBackendBuffer CreateBuffer(BufferDescription description)
            => CreateBufferCore(description);

        public IGraphicsBackendBufferAllocation CreateBufferAllocation(
            BufferDescription description,
            GraphicsResourceAllocationInfo allocationInfo)
        {
            _ = Interlocked.Increment(ref _bufferAllocationCreationCount);

            if (Interlocked.Decrement(ref _remainingBufferAllocationCreationFailures) >= 0)
            {
                throw new InvalidOperationException("The fake backend rejected a transient buffer allocation attempt.");
            }

            return new FakeBackendBufferAllocation(this);
        }

        public IGraphicsBackendPipeline CreatePipeline(
            PipelineDescription description,
            ReadOnlySpan<BufferReadLease<byte>> bytecodeLeases)
            => throw new NotSupportedException();

        public IGraphicsBackendSampler CreateSampler(SamplerDescription description, float effectiveMaximumAnisotropy)
        {
            _ = Interlocked.Increment(ref _samplerCreationCount);
            _ = Interlocked.Exchange(ref _effectiveSamplerAnisotropyBits, BitConverter.SingleToInt32Bits(effectiveMaximumAnisotropy));
            _samplerCreationEntered?.Set();

            if ((_continueSamplerCreation is ManualResetEventSlim continueSamplerCreation)
                && !continueSamplerCreation.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("The fake sampler creation gate was not released.");
            }

            return new FakeBackendSampler(this);
        }

        public IGraphicsBackendTexture CreateTexture(
            TextureDescription description,
            IGraphicsBackendTextureAllocation allocation)
        {
            ArgumentNullException.ThrowIfNull(allocation);

            if (allocation is not FakeBackendTextureAllocation)
            {
                throw new ArgumentException("The allocation belongs to a different fake backend.", nameof(allocation));
            }

            return CreateTextureCore(description);
        }

        public IGraphicsBackendTexture CreateTexture(TextureDescription description)
            => CreateTextureCore(description);

        public IGraphicsBackendTextureAllocation CreateTextureAllocation(
            TextureDescription description,
            GraphicsResourceAllocationInfo allocationInfo)
        {
            _ = Interlocked.Increment(ref _textureAllocationCreationCount);

            if (Interlocked.Decrement(ref _remainingTextureAllocationCreationFailures) >= 0)
            {
                throw new InvalidOperationException("The fake backend rejected a transient texture allocation attempt.");
            }

            return new FakeBackendTextureAllocation(this);
        }

        public void Dispose()
        {
            _ = Interlocked.Increment(ref _deviceDisposalCount);

            if (_throwOnDispose)
            {
                throw new InvalidOperationException("The fake backend disposal failed.");
            }
        }

        public GraphicsResourceAllocationInfo GetBufferAllocationInfo(BufferDescription description)
            => new(
                _bufferAllocationSizeBytes ?? description.ByteLength,
                alignment: 1,
                heapIndex: description.MemoryKind == GpuMemoryKind.Upload ? 1 : 0,
                budgetBytes: _bufferBudgetBytes);

        public GraphicsResourceAllocationInfo GetTextureAllocationInfo(TextureDescription description)
        {
            long sizeBytes = 0;

            for (var layer = 0; layer < description.ArrayLayers; layer++)
            {
                for (var mipLevel = 0; mipLevel < description.MipLevels; mipLevel++)
                {
                    var width = Math.Max(description.Width >> mipLevel, 1);
                    var height = Math.Max(description.Height >> mipLevel, 1);
                    var depth = description.Dimension == TextureDimension.Dimension2D
                        ? 1
                        : Math.Max(description.Depth >> mipLevel, 1);
                    sizeBytes = checked(
                        sizeBytes
                        + ((long)width * height * depth * TextureReadback.GetBytesPerTexel(description.Format)));
                }
            }

            return new GraphicsResourceAllocationInfo(
                _textureAllocationSizeBytes ?? sizeBytes,
                alignment: 1,
                heapIndex: 0,
                budgetBytes: _textureBudgetBytes);
        }

        public TextureReadback ReadbackTexture(
            IGraphicsBackendTexture source,
            GraphResourceState sourceState,
            TextureSubresource subresource)
        {
            var texture = GetTexture(source);
            var width = Math.Max(texture.Description.Width >> subresource.MipLevel, 1);
            var height = Math.Max(texture.Description.Height >> subresource.MipLevel, 1);
            var depth = texture.Description.Dimension == TextureDimension.Dimension2D
                ? 1
                : Math.Max(texture.Description.Depth >> subresource.MipLevel, 1);
            var rowByteLength = checked(width * TextureReadback.GetBytesPerTexel(texture.Description.Format));
            var rowPitch = checked((nint)(rowByteLength + 4));
            var slicePitch = checked(rowPitch * height);
            var byteLength = checked(slicePitch * depth);
            var storage = UnmanagedBuffer<byte>.Allocate(byteLength, new BufferAllocationOptions(byteLength, 1));
            var transferred = false;

            try
            {
                var sourceBytes = texture.GetSubresource(subresource);

                for (var depthIndex = 0; depthIndex < depth; depthIndex++)
                {
                    for (var row = 0; row < height; row++)
                    {
                        var sourceOffset = checked((depthIndex * height * rowByteLength) + (row * rowByteLength));
                        var destinationOffset = checked((depthIndex * slicePitch) + (row * rowPitch));
                        storage.CopyFrom(sourceBytes.AsSpan(sourceOffset, rowByteLength), destinationOffset);
                    }
                }

                var ownedStorage = storage.TransferToOwnedBuffer();
                transferred = true;

                try
                {
                    return new TextureReadback(
                        texture.Description.Format,
                        subresource,
                        width,
                        height,
                        depth,
                        rowPitch,
                        slicePitch,
                        ownedStorage);
                }
                catch
                {
                    ownedStorage.Dispose();
                    throw;
                }
            }
            finally
            {
                if (!transferred)
                {
                    storage.Dispose();
                }
            }
        }

        public long SubmitGraph(GraphicsGraphSubmission submission)
            => throw new NotSupportedException();

        public long UploadBuffer(
            IGraphicsBackendBuffer destination,
            GraphResourceState destinationState,
            long destinationOffset,
            BufferReadLease<byte> source,
            nint sourceOffset,
            nint byteLength)
        {
            WaitForUpload();
            var destinationBuffer = GetBuffer(destination);
            nint copiedBytes = 0;

            while (copiedBytes < byteLength)
            {
                var remainingBytes = byteLength - copiedBytes;
                var copyLength = remainingBytes > int.MaxValue
                    ? int.MaxValue
                    : checked((int)remainingBytes);
                var action = new BufferUploadCopyAction(
                    destinationBuffer.Bytes,
                    checked((int)(destinationOffset + copiedBytes)));
                source.UseWindow(checked(sourceOffset + copiedBytes), copyLength, ref action);
                copiedBytes = checked(copiedBytes + copyLength);
            }

            return Interlocked.Increment(ref _completedFence);
        }

        public long UploadBuffer(
            IGraphicsBackendBuffer destination,
            GraphResourceState destinationState,
            long destinationOffset,
            ReadOnlySpan<byte> source)
        {
            WaitForUpload();
            source.CopyTo(GetBuffer(destination).Bytes.AsSpan(checked((int)destinationOffset), source.Length));
            return Interlocked.Increment(ref _completedFence);
        }

        public long UploadTexture(
            IGraphicsBackendTexture destination,
            GraphResourceState destinationState,
            TextureSubresource subresource,
            BufferReadLease<byte> source,
            nint sourceOffset,
            TextureDataLayout sourceLayout)
        {
            WaitForUpload();
            var texture = GetTexture(destination);
            var width = Math.Max(texture.Description.Width >> subresource.MipLevel, 1);
            var height = Math.Max(texture.Description.Height >> subresource.MipLevel, 1);
            var depth = texture.Description.Dimension == TextureDimension.Dimension2D
                ? 1
                : Math.Max(texture.Description.Depth >> subresource.MipLevel, 1);
            var rowByteLength = checked(width * TextureReadback.GetBytesPerTexel(texture.Description.Format));
            var destinationBytes = texture.GetSubresource(subresource);

            for (var depthIndex = 0; depthIndex < depth; depthIndex++)
            {
                for (var row = 0; row < height; row++)
                {
                    var sourceRowOffset = checked(
                        sourceOffset
                        + (checked((nint)depthIndex) * sourceLayout.SlicePitch)
                        + (checked((nint)row) * sourceLayout.RowPitch));
                    var destinationRowOffset = checked((depthIndex * height * rowByteLength) + (row * rowByteLength));
                    var action = new BufferUploadCopyAction(destinationBytes, destinationRowOffset);
                    source.UseWindow(sourceRowOffset, rowByteLength, ref action);
                }
            }

            return Interlocked.Increment(ref _completedFence);
        }

        public void WaitForGraphFence(long fenceValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fenceValue);

            var readbackWaitEntered = _readbackWaitEntered;

            if (readbackWaitEntered is not null)
            {
                Volatile.Write(ref _readbackWaitThreadId, Environment.CurrentManagedThreadId);
                readbackWaitEntered.Set();
                (_continueReadback
                    ?? throw new InvalidOperationException("The fake readback wait has no release signal."))
                    .Wait();
            }

            if (FailReadbackFenceWait)
            {
                throw new InvalidOperationException("The controlled readback fence wait failed.");
            }
        }

        public void WriteUploadBuffer(
            IGraphicsBackendBuffer destination,
            long destinationOffset,
            ReadOnlySpan<byte> source)
        {
            source.CopyTo(GetBuffer(destination).Bytes.AsSpan(checked((int)destinationOffset), source.Length));
        }

        internal void BufferAllocationDisposed()
            => _ = Interlocked.Increment(ref _bufferAllocationDisposalCount);

        internal void BufferDisposed(FakeBackendBuffer buffer)
        {
            _ = Interlocked.Increment(ref _bufferDisposalCount);

            if (!_buffers.Contains(buffer.Bytes))
            {
                throw new InvalidOperationException("The test backend disposed an unknown buffer.");
            }
        }

        internal void ConfigureSamplerCreationGate(
            ManualResetEventSlim samplerCreationEntered,
            ManualResetEventSlim continueSamplerCreation,
            ManualResetEventSlim samplerDisposed)
        {
            _samplerCreationEntered = samplerCreationEntered;
            _continueSamplerCreation = continueSamplerCreation;
            _samplerDisposed = samplerDisposed;
        }

        internal void SamplerDisposed()
        {
            _ = Interlocked.Increment(ref _samplerDisposalCount);
            _samplerDisposed?.Set();
        }

        internal void TextureAllocationDisposed()
            => _ = Interlocked.Increment(ref _textureAllocationDisposalCount);

        internal void TextureDisposed(FakeBackendTexture texture)
            => _ = Interlocked.Increment(ref _textureDisposalCount);

        private FakeBackendBuffer CreateBufferCore(BufferDescription description)
        {
            var creationIndex = Interlocked.Increment(ref _bufferCreationCount);

            if ((creationIndex == _bufferCreationFailureAt)
                || (Interlocked.Decrement(ref _remainingBufferCreationFailures) >= 0))
            {
                throw new InvalidOperationException("The fake buffer backend rejected a buffer creation attempt.");
            }

            var buffer = new FakeBackendBuffer(this, description);
            _buffers.Add(buffer.Bytes);
            _lastBufferBytes = buffer.Bytes;
            return buffer;
        }

        private FakeBackendTexture CreateTextureCore(TextureDescription description)
        {
            var creationIndex = Interlocked.Increment(ref _textureCreationCount);

            if ((creationIndex == _textureCreationFailureAt)
                || (Interlocked.Decrement(ref _remainingTextureCreationFailures) >= 0))
            {
                throw new InvalidOperationException("The fake texture backend rejected a texture creation attempt.");
            }

            return new FakeBackendTexture(this, description);
        }

        private void WaitForUpload()
        {
            if (_uploadEntered is null)
            {
                return;
            }

            _uploadEntered.Set();

            if (!_continueUpload!.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("The buffer transfer test did not release its upload gate.");
            }
        }

        private readonly struct BufferUploadCopyAction : IAction<ReadOnlySpan<byte>>
        {
            private readonly byte[] _destination;
            private readonly int _destinationOffset;

            internal BufferUploadCopyAction(byte[] destination, int destinationOffset)
            {
                _destination = destination;
                _destinationOffset = destinationOffset;
            }

            public readonly void Invoke(scoped ReadOnlySpan<byte> argument)
                => argument.CopyTo(_destination.AsSpan(_destinationOffset, argument.Length));
        }
    }

    private sealed class FakeBackendFactory : IGraphicsBackendFactory
    {
        private readonly ManualResetEventSlim? _continueCreate;
        private readonly ManualResetEventSlim? _createEntered;
        private readonly FakeBackendDevice _device;

        internal FakeBackendFactory(
            FakeBackendDevice device,
            ManualResetEventSlim? createEntered = null,
            ManualResetEventSlim? continueCreate = null)
        {
            _continueCreate = continueCreate;
            _createEntered = createEntered;
            _device = device;
        }

        public GraphicsBackend Backend => GraphicsBackend.D3D12;

        public IGraphicsBackendDevice Create(
            uint ownerId,
            GraphicsDeviceProfile profile,
            GraphicsPlatformKind platformKind,
            int frameSlotCount,
            CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfZero(ownerId);
            cancellationToken.ThrowIfCancellationRequested();

            if ((profile != GraphicsDeviceProfile.OffscreenNative) || (platformKind != GraphicsPlatformKind.None))
            {
                throw new ArgumentException("The test backend supports only OffscreenNative without a platform.");
            }

            _createEntered?.Set();

            if ((_continueCreate is ManualResetEventSlim continueCreate)
                && !continueCreate.Wait(TimeSpan.FromSeconds(10), CancellationToken.None))
            {
                throw new TimeoutException("The GraphicsDevice startup test did not release its factory gate.");
            }

            return _device;
        }
    }

    private sealed class FakeBackendSampler : IGraphicsBackendSampler
    {
        private readonly FakeBackendDevice _device;
        private int _isDisposed;

        internal FakeBackendSampler(FakeBackendDevice device)
        {
            _device = device;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                _device.SamplerDisposed();
            }
        }
    }

    private sealed class FakeBackendTexture : IGraphicsBackendTexture
    {
        private readonly FakeBackendDevice _device;
        private int _isDisposed;
        private readonly byte[][] _subresources;

        internal FakeBackendTexture(FakeBackendDevice device, TextureDescription description)
        {
            _device = device;
            Description = description;
            _subresources = new byte[checked(description.ArrayLayers * description.MipLevels)][];

            for (var layer = 0; layer < description.ArrayLayers; layer++)
            {
                for (var mipLevel = 0; mipLevel < description.MipLevels; mipLevel++)
                {
                    var width = GetMipExtent(description.Width, mipLevel);
                    var height = GetMipExtent(description.Height, mipLevel);
                    var depth = description.Dimension == TextureDimension.Dimension2D
                        ? 1
                        : GetMipExtent(description.Depth, mipLevel);
                    var byteLength = checked(width * height * depth * TextureReadback.GetBytesPerTexel(description.Format));
                    _subresources[(layer * description.MipLevels) + mipLevel] = new byte[byteLength];
                }
            }
        }

        internal TextureDescription Description { get; }

        private static int GetMipExtent(int extent, int mipLevel)
            => Math.Max(extent >> mipLevel, 1);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                _device.TextureDisposed(this);
            }
        }

        internal byte[] GetSubresource(TextureSubresource subresource)
            => _subresources[(subresource.ArrayLayer * Description.MipLevels) + subresource.MipLevel];
    }

    private sealed class FakeBackendTextureAllocation : IGraphicsBackendTextureAllocation
    {
        private readonly FakeBackendDevice _device;
        private int _isDisposed;

        internal FakeBackendTextureAllocation(FakeBackendDevice device)
        {
            _device = device;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                _device.TextureAllocationDisposed();
            }
        }
    }

    private readonly struct ReadbackCopyAction : IAction<ReadOnlySpan<byte>>
    {
        private readonly byte[] _destination;

        internal ReadbackCopyAction(byte[] destination)
        {
            _destination = destination;
        }

        public readonly void Invoke(scoped ReadOnlySpan<byte> argument)
            => argument.CopyTo(_destination);
    }
}
