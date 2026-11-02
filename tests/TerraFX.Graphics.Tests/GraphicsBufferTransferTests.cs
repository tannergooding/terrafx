// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
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

    private static GraphicsCapabilities CreateCapabilities()
    {
        var features = ImmutableArray.Create(
            GraphicsFeature.Direct3DFeatureLevel12,
            GraphicsFeature.ShaderModel6);
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
            maximumSamplersPerStage: 32,
            maximumSamplerAnisotropy: 1.0f,
            maximumUniformBuffersPerStage: 16,
            maximumUniformBufferRangeBytes: 65536,
            uniformBufferOffsetAlignment: 256,
            storageBufferOffsetAlignment: 16,
            bufferCopyOffsetAlignment: 16,
            textureRowPitchAlignment: 256,
            texturePlacementAlignment: 512,
            maximumComputeWorkGroupSizeX: 1024,
            maximumComputeWorkGroupSizeY: 1024,
            maximumComputeWorkGroupSizeZ: 64,
            maximumComputeWorkGroupInvocations: 1024,
            maximumVertexInputAttributes: 32,
            maximumVertexInputBindings: 32,
            maximumBufferByteLength: 1L << 40);

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
            [new GraphicsMemoryHeapCapabilities(
                0,
                GraphicsMemoryHeapProperties.DeviceLocal,
                4L * 1024 * 1024 * 1024,
                3L * 1024 * 1024 * 1024)]);
    }

    private static async Task<GraphicsDevice> CreateGraphicsDeviceAsync(FakeBackendDevice backendDevice)
        => await GraphicsDevice.CreateAsync(
            ownerId: 31,
            new GraphicsBackendRegistration(new FakeBackendFactory(backendDevice)),
            GraphicsDeviceProfile.OffscreenNative,
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
            Assert.That(registry.ValidateCompilation().ContentsValid, Is.False);
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
            Assert.That(result.ImportsValid, Is.True);
            Assert.That(result.ContentsValid, Is.True);
            Assert.That(result.DependenciesValid, Is.True);
            Assert.That(result.OrderedPasses, Is.EqualTo(expectedOrder));
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

    private sealed class FakeBackendDevice : IGraphicsBackendDevice
    {
        private ManualResetEventSlim? _continueSamplerCreation;
        private readonly ManualResetEventSlim? _continueUpload;
        private ManualResetEventSlim? _samplerCreationEntered;
        private ManualResetEventSlim? _samplerDisposed;
        private readonly ManualResetEventSlim? _uploadEntered;
        private readonly GraphicsCapabilities _capabilities = CreateCapabilities();
        private readonly List<byte[]> _buffers = [];
        private int _bufferDisposalCount;
        private long _completedFence;
        private int _effectiveSamplerAnisotropyBits;
        private byte[]? _lastBufferBytes;
        private int _remainingTextureCreationFailures;
        private int _samplerCreationCount;
        private int _samplerDisposalCount;
        private readonly long? _textureAllocationSizeBytes;
        private readonly long _textureBudgetBytes;
        private int _textureCreationCount;
        private int _textureDisposalCount;

        internal FakeBackendDevice(
            long textureBudgetBytes = 3L * 1024 * 1024 * 1024,
            long? textureAllocationSizeBytes = null,
            int textureCreationFailureCount = 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(textureBudgetBytes);
            ArgumentOutOfRangeException.ThrowIfNegative(textureCreationFailureCount);

            if (textureAllocationSizeBytes is <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(textureAllocationSizeBytes));
            }

            _textureBudgetBytes = textureBudgetBytes;
            _textureAllocationSizeBytes = textureAllocationSizeBytes;
            _remainingTextureCreationFailures = textureCreationFailureCount;
        }

        internal FakeBackendDevice(ManualResetEventSlim uploadEntered, ManualResetEventSlim continueUpload)
        {
            _uploadEntered = uploadEntered;
            _continueUpload = continueUpload;
            _textureBudgetBytes = 3L * 1024 * 1024 * 1024;
        }

        public GraphicsCapabilities Capabilities => _capabilities;

        internal int BufferDisposalCount => Volatile.Read(ref _bufferDisposalCount);

        internal float EffectiveSamplerAnisotropy
            => BitConverter.Int32BitsToSingle(Volatile.Read(ref _effectiveSamplerAnisotropyBits));

        internal byte[]? LastBufferBytes => _lastBufferBytes;

        internal int SamplerCreationCount => Volatile.Read(ref _samplerCreationCount);

        internal int SamplerDisposalCount => Volatile.Read(ref _samplerDisposalCount);

        internal int TextureCreationCount => Volatile.Read(ref _textureCreationCount);

        internal int TextureDisposalCount => Volatile.Read(ref _textureDisposalCount);

        private static FakeBackendBuffer GetBuffer(IGraphicsBackendBuffer buffer)
            => buffer as FakeBackendBuffer
                ?? throw new ArgumentException("The buffer belongs to a different fake backend.", nameof(buffer));

        private static FakeBackendTexture GetTexture(IGraphicsBackendTexture texture)
            => texture as FakeBackendTexture
                ?? throw new ArgumentException("The texture belongs to a different fake backend.", nameof(texture));

        public IGraphicsBackendBuffer CreateBuffer(BufferDescription description)
        {
            var buffer = new FakeBackendBuffer(this, description);
            _buffers.Add(buffer.Bytes);
            _lastBufferBytes = buffer.Bytes;
            return buffer;
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

        public IGraphicsBackendTexture CreateTexture(TextureDescription description)
        {
            _ = Interlocked.Increment(ref _textureCreationCount);

            if (Interlocked.Decrement(ref _remainingTextureCreationFailures) >= 0)
            {
                throw new InvalidOperationException("The fake texture backend rejected a texture creation attempt.");
            }

            return new FakeBackendTexture(this, description);
        }

        public void Dispose()
        {
        }

        public GraphicsResourceAllocationInfo GetBufferAllocationInfo(BufferDescription description)
            => new(description.ByteLength, alignment: 1, heapIndex: 0, budgetBytes: 3L * 1024 * 1024 * 1024);

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

        public OwnedBuffer<byte> ReadbackBuffer(IGraphicsBackendBuffer source, long sourceOffset, nint byteLength)
        {
            var buffer = GetBuffer(source);
            var storage = UnmanagedBuffer<byte>.Allocate(byteLength, new BufferAllocationOptions(byteLength, 1));
            storage.CopyFrom(buffer.Bytes.AsSpan(checked((int)sourceOffset), checked((int)byteLength)), 0);
            return storage.TransferToOwnedBuffer();
        }

        public TextureReadback ReadbackTexture(IGraphicsBackendTexture source, TextureSubresource subresource)
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

        public long UploadBuffer(
            IGraphicsBackendBuffer destination,
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

        public long UploadBuffer(IGraphicsBackendBuffer destination, long destinationOffset, ReadOnlySpan<byte> source)
        {
            WaitForUpload();
            source.CopyTo(GetBuffer(destination).Bytes.AsSpan(checked((int)destinationOffset), source.Length));
            return Interlocked.Increment(ref _completedFence);
        }

        public long UploadTexture(
            IGraphicsBackendTexture destination,
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

        internal void TextureDisposed(FakeBackendTexture texture)
            => _ = Interlocked.Increment(ref _textureDisposalCount);

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
        private readonly FakeBackendDevice _device;

        internal FakeBackendFactory(FakeBackendDevice device)
        {
            _device = device;
        }

        public GraphicsBackend Backend => GraphicsBackend.D3D12;

        public IGraphicsBackendDevice Create(
            uint ownerId,
            GraphicsDeviceProfile profile,
            GraphicsPlatformKind platformKind,
            CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfZero(ownerId);
            cancellationToken.ThrowIfCancellationRequested();

            if ((profile != GraphicsDeviceProfile.OffscreenNative) || (platformKind != GraphicsPlatformKind.None))
            {
                throw new ArgumentException("The test backend supports only OffscreenNative without a platform.");
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
