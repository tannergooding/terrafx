// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
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
using TerraFX.Numerics;
using TerraFX.Platform;
using TerraFX.Rendering;
using TerraFX.Runtime.Tests.Scenes;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Hosting;

internal sealed partial class EngineHostOptionsTests
{
    [TestCase(GraphicsBackend.D3D12, false, false, false, false, false)]
    [TestCase(GraphicsBackend.Vulkan, false, false, false, false, false)]
    [TestCase(GraphicsBackend.D3D12, true, false, false, false, false)]
    [TestCase(GraphicsBackend.Vulkan, true, false, false, false, false)]
    [TestCase(GraphicsBackend.D3D12, false, true, false, false, false)]
    [TestCase(GraphicsBackend.Vulkan, false, true, false, false, false)]
    [TestCase(GraphicsBackend.D3D12, true, true, false, false, false)]
    [TestCase(GraphicsBackend.Vulkan, true, true, false, false, false)]
    [TestCase(GraphicsBackend.D3D12, true, true, true, false, false)]
    [TestCase(GraphicsBackend.Vulkan, true, true, true, false, false)]
    [TestCase(GraphicsBackend.D3D12, false, false, false, true, false)]
    [TestCase(GraphicsBackend.Vulkan, false, false, false, true, false)]
    [TestCase(GraphicsBackend.D3D12, false, false, false, false, true)]
    [TestCase(GraphicsBackend.Vulkan, false, false, false, false, true)]
    public async Task SubmitsOffscreenClearGraphAndReadsBackAsync(
        GraphicsBackend backend,
        bool useUniformBuffer,
        bool useSampledTexture,
        bool useStorageResources,
        bool useBufferCopy,
        bool useCompute)
    {
        if ((backend == GraphicsBackend.D3D12) && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 offscreen graph path targets Windows 11.");
            return;
        }

        var shaderFixtureDirectory = Environment.GetEnvironmentVariable("TERRAFX_M04_SHADER_FIXTURE_DIRECTORY");

        if ((useUniformBuffer || useSampledTexture || useStorageResources || useCompute)
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
        var packageBytes = SceneAssetStoreTests.Package.CreatePackage();
        var packagePath = SceneAssetStoreTests.Package.WritePackage(packageBytes);
        var assetStoreRegistration = new AssetStoreRegistrationBuilder(schemas).Build();
        var graphicsRegistration = backend switch {
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
        };
        EngineHost? host = null;
        GpuTexture? target = null;
        GpuTexture? sampledTexture = null;
        GpuTexture? storageImage = null;
        GpuBuffer? frameUniformBuffer = null;
        GpuBuffer? storageBuffer = null;
        GpuBuffer? copySourceBuffer = null;
        GpuBuffer? copyDestinationBuffer = null;
        byte[]? expectedCopiedBytes = null;
        GpuPipeline? pipeline = null;
        GpuPipeline? computePipeline = null;
        GpuSampler? frameSampler = null;
        RenderSnapshot? snapshot = null;

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
            var cameraId = Guid.Empty;
            var targetExtent = new PixelSize(32, 32);

            await engineHost.InvokeAsync(_ => {
                world = _.CreateWorld(new WorldOptions(worldRegistration) {
                    CpuWorkerCount = 0,
                    InitialReplayLineageId = Guid.NewGuid(),
                    WorldGuid = Guid.NewGuid(),
                });
                var camera = new SceneComponentDefinitions.CameraComponent(
                    (byte)CameraProjectionKind.Perspective,
                    float.Pi / 3.0f,
                    10.0f,
                    0.1f,
                    (byte)CameraFarMode.Finite,
                    new SceneComponentDefinitions.OptionalSingle(1000.0f),
                    default,
                    new Vector4(0.0f, 0.0f, 1.0f, 1.0f),
                    (byte)(CameraClearFlags.Color | CameraClearFlags.Depth),
                    new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
                    ulong.MaxValue,
                    order: 0);
                var transform = new SceneComponentDefinitions.LocalTransformComponent(
                    Vector3.Zero,
                    Quaternion.Identity,
                    Vector3.One);
                var rootPosition = new SceneComponentDefinitions.RootPositionComponent(default);
                var cameraEntity = world.CreateEntity(new EntityDescription(components: [
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
                    TextureFormat.Rgba8Unorm,
                    targetUsage,
                    $"m04-{backend}-offscreen-clear"))
                .ConfigureAwait(false);

            if (backend == GraphicsBackend.Vulkan)
            {
                var initialTargetBytes = new byte[checked(targetExtent.Width * targetExtent.Height * 4)];
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
                    new TextureDataLayout(targetExtent.Width * 4, initialTargetBytes.Length))
                    .ConfigureAwait(false);
                Assert.That(uploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));
            }

            if (useSampledTexture)
            {
                var sampleBytes = new byte[] { 32, 128, 224, 255 };
                sampledTexture = await graphicsDevice.CreateTextureAsync(
                    new TextureDescription(
                        TextureDimension.Dimension2D,
                        1,
                        1,
                        depth: 1,
                        arrayLayers: 1,
                        mipLevels: 1,
                        TextureFormat.Rgba8Unorm,
                        TextureUsage.Sampled | TextureUsage.CopyDestination,
                        $"m04-{backend}-frame-sampled-texture"))
                    .ConfigureAwait(false);
                var sampleStorage = UnmanagedBuffer<byte>.Allocate(
                    sampleBytes.Length,
                    new BufferAllocationOptions(sampleBytes.Length, alignment: 1));
                sampleStorage.CopyFrom(sampleBytes.AsSpan(), 0);

                using var ownedSampleBytes = sampleStorage.TransferToOwnedBuffer();
                using var sampleLease = ownedSampleBytes.AcquireReadLease(0, sampleBytes.Length);
                var sampleUploadReceipt = await graphicsDevice.UploadTextureAsync(
                    sampledTexture,
                    new TextureSubresource(0, 0, TextureAspect.Color),
                    sampleLease,
                    0,
                    new TextureDataLayout(rowPitch: 4, slicePitch: 4))
                    .ConfigureAwait(false);
                Assert.That(sampleUploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));
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

            if (!string.IsNullOrWhiteSpace(shaderFixtureDirectory))
            {
                var shaderExtension = backend == GraphicsBackend.D3D12 ? "dxil" : "spv";
                var pixelStage = useStorageResources
                    ? "pixel-sampled-frame-storage"
                    : useSampledTexture
                        ? (useUniformBuffer ? "pixel-sampled-frame" : "pixel-sampled")
                        : (useUniformBuffer ? "pixel" : "pixel-graph");
                using var vertexBytecodeStorage = CreateBytecodeStorage(Path.Combine(
                    shaderFixtureDirectory,
                    $"M04OffscreenTriangle.vertex.{shaderExtension}"));
                using var pixelBytecodeStorage = CreateBytecodeStorage(Path.Combine(
                    shaderFixtureDirectory,
                    $"M04OffscreenTriangle.{pixelStage}.{shaderExtension}"));
                using var vertexBytecode = vertexBytecodeStorage.AcquireReadLease(0, vertexBytecodeStorage.Length);
                using var pixelBytecode = pixelBytecodeStorage.AcquireReadLease(0, pixelBytecodeStorage.Length);
                var vertexReflection = M04ShaderFixtureCompiler.ReadTypedReflection(shaderFixtureDirectory, "vertex");
                var pixelReflection = M04ShaderFixtureCompiler.ReadTypedReflection(shaderFixtureDirectory, pixelStage);
                var binaryFormat = backend == GraphicsBackend.D3D12
                    ? ShaderBinaryFormat.Dxil
                    : ShaderBinaryFormat.SpirV;
                var pipelineDescription = new GraphicsPipelineDescription(
                    [
                        new ShaderBytecodeInput(ShaderStage.Vertex, binaryFormat, vertexBytecode, vertexReflection),
                        new ShaderBytecodeInput(ShaderStage.Pixel, binaryFormat, pixelBytecode, pixelReflection),
                    ],
                    [],
                    vertexStride: 0,
                    [TextureFormat.Rgba8Unorm],
                    depthTargetFormat: null,
                    PrimitiveTopology.TriangleList,
                    PipelineBlendMode.Opaque,
                    PipelineDepthMode.Disabled,
                    PipelineCullMode.None,
                    frontFaceClockwise: false,
                    bindingLayoutVersion: 1);
                pipeline = await graphicsDevice.CreatePipelineAsync(pipelineDescription, CancellationToken.None)
                    .ConfigureAwait(false);

                if (useCompute)
                {
                    using var computeBytecodeStorage = CreateBytecodeStorage(Path.Combine(
                        shaderFixtureDirectory,
                        $"M04OffscreenTriangle.compute-storage.{shaderExtension}"));
                    using var computeBytecode = computeBytecodeStorage.AcquireReadLease(0, computeBytecodeStorage.Length);
                    var computeReflection = M04ShaderFixtureCompiler.ReadTypedReflection(shaderFixtureDirectory, "compute-storage");
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
                var frameUniformBytes = new byte[256];
                _ = BitConverter.TryWriteBytes(
                    frameUniformBytes.AsSpan(0, sizeof(float)),
                    useSampledTexture ? 1.0f : 0.0f);
                _ = BitConverter.TryWriteBytes(frameUniformBytes.AsSpan(sizeof(float), sizeof(float)), 1.0f);
                _ = BitConverter.TryWriteBytes(
                    frameUniformBytes.AsSpan(sizeof(float) * 2, sizeof(float)),
                    useSampledTexture ? 1.0f : 0.0f);
                _ = BitConverter.TryWriteBytes(frameUniformBytes.AsSpan(sizeof(float) * 3, sizeof(float)), 1.0f);
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
            }

            if (useStorageResources || useCompute)
            {
                storageBuffer = await graphicsDevice.CreateBufferAsync(
                    new BufferDescription(
                        sizeof(uint),
                        BufferUsage.Storage | BufferUsage.CopySource | BufferUsage.CopyDestination,
                        GpuMemoryKind.DeviceLocal,
                        structuredStride: sizeof(uint),
                        debugName: $"m04-{backend}-frame-storage-buffer"))
                    .ConfigureAwait(false);
                var storageBufferInitialBytes = new byte[sizeof(uint)];
                var storageBufferUploadReceipt = await graphicsDevice.UploadBufferAsync(
                    storageBuffer,
                    0,
                    storageBufferInitialBytes.AsSpan())
                    .ConfigureAwait(false);
                Assert.That(storageBufferUploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));
            }

            if (useStorageResources || useCompute)
            {
                storageImage = await graphicsDevice.CreateTextureAsync(
                    new TextureDescription(
                        TextureDimension.Dimension2D,
                        1,
                        1,
                        depth: 1,
                        arrayLayers: 1,
                        mipLevels: 1,
                        TextureFormat.R32UInt,
                        TextureUsage.Storage | TextureUsage.CopySource | TextureUsage.CopyDestination,
                        $"m04-{backend}-frame-storage-image"))
                    .ConfigureAwait(false);
                var storageImageInitialBytes = new byte[sizeof(uint)];
                var storageImageInitialStorage = UnmanagedBuffer<byte>.Allocate(
                    storageImageInitialBytes.Length,
                    new BufferAllocationOptions(storageImageInitialBytes.Length, alignment: 1));
                storageImageInitialStorage.CopyFrom(storageImageInitialBytes.AsSpan(), 0);

                using var ownedStorageImageInitialBytes = storageImageInitialStorage.TransferToOwnedBuffer();
                using var storageImageInitialLease = ownedStorageImageInitialBytes.AcquireReadLease(0, storageImageInitialBytes.Length);
                var storageImageUploadReceipt = await graphicsDevice.UploadTextureAsync(
                    storageImage,
                    new TextureSubresource(0, 0, TextureAspect.Color),
                    storageImageInitialLease,
                    0,
                    new TextureDataLayout(rowPitch: sizeof(uint), slicePitch: sizeof(uint)))
                    .ConfigureAwait(false);
                Assert.That(storageImageUploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));
            }

            if (useBufferCopy || useCompute)
            {
                expectedCopiedBytes = useCompute
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

                if (useBufferCopy)
                {
                    copySourceBuffer = await graphicsDevice.CreateBufferAsync(
                        new BufferDescription(
                            expectedCopiedBytes.Length,
                            BufferUsage.CopySource | BufferUsage.CopyDestination,
                            GpuMemoryKind.DeviceLocal,
                            structuredStride: null,
                            debugName: $"m04-{backend}-copy-source"))
                        .ConfigureAwait(false);
                }

                copyDestinationBuffer = await graphicsDevice.CreateBufferAsync(
                    new BufferDescription(
                        expectedCopiedBytes.Length,
                        BufferUsage.CopySource | BufferUsage.CopyDestination,
                        GpuMemoryKind.DeviceLocal,
                        structuredStride: null,
                        debugName: $"m04-{backend}-copy-destination"))
                    .ConfigureAwait(false);

                if (copySourceBuffer is GpuBuffer copyUploadSourceBuffer)
                {
                    var copyUploadReceipt = await graphicsDevice.UploadBufferAsync(
                        copyUploadSourceBuffer,
                        0,
                        expectedCopiedBytes.AsSpan())
                        .ConfigureAwait(false);
                    Assert.That(copyUploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));
                }
            }

            var graph = renderer.BuildGraph(
                renderSnapshot,
                target);
            var sampleRange = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);
            GraphBufferVersion? frameUniformVersion = null;
            GraphBufferVersion? storageBufferInitialVersion = null;
            GraphBufferVersion? storageBufferWriteVersion = null;
            GraphBufferVersion? computeStorageWriteVersion = null;
            GraphBufferVersion? copySourceVersion = null;
            GraphBufferVersion? copyDestinationInitialVersion = null;
            GraphBufferVersion? copyDestinationWriteVersion = null;
            GraphTextureVersion? sampledTextureVersion = null;
            GraphTextureVersion? storageImageInitialVersion = null;
            GraphTextureVersion? storageImageWriteVersion = null;
            GraphTextureVersion? computeStorageImageWriteVersion = null;
            BindingSet? frameBindingSet = null;
            BindingSet? computeBindingSet = null;
            var bufferBindings = ImmutableArray.CreateBuilder<BufferBinding>(useUniformBuffer ? 1 : 0);
            var storageBufferBindings = ImmutableArray.CreateBuilder<BufferBinding>(useStorageResources ? 1 : 0);
            var textureBindings = ImmutableArray.CreateBuilder<TextureBinding>(
                (useSampledTexture ? 1 : 0) + (useStorageResources ? 1 : 0));
            var samplerBindings = ImmutableArray.CreateBuilder<SamplerBinding>(useSampledTexture ? 1 : 0);

            if (frameUniformBuffer is GpuBuffer selectedBuffer)
            {
                var range = new GraphBufferRange(0, selectedBuffer.ByteLength);
                frameUniformVersion = graph.ImportBuffer(
                    selectedBuffer,
                    range,
                    new GraphImportOptions(
                        GraphResourceState.General,
                        contentsValid: true,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                bufferBindings.Add(new BufferBinding(
                    ShaderBindingGroup.Frame,
                    0,
                    ShaderResourceKind.UniformBuffer,
                    frameUniformVersion,
                    range));
            }

            if (storageBuffer is GpuBuffer selectedStorageBuffer)
            {
                var range = new GraphBufferRange(0, selectedStorageBuffer.ByteLength);
                storageBufferInitialVersion = graph.ImportBuffer(
                    selectedStorageBuffer,
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
            }

            if (copyDestinationBuffer is GpuBuffer selectedCopyDestinationBuffer)
            {
                var range = new GraphBufferRange(0, selectedCopyDestinationBuffer.ByteLength);
                copyDestinationInitialVersion = graph.ImportBuffer(
                    selectedCopyDestinationBuffer,
                    range,
                    new GraphImportOptions(
                        GraphResourceState.Undefined,
                        contentsValid: false,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
            }

            if (sampledTexture is GpuTexture selectedSampledTexture)
            {
                sampledTextureVersion = graph.ImportTexture(
                    selectedSampledTexture,
                    sampleRange,
                    new GraphImportOptions(
                        GraphResourceState.General,
                        contentsValid: true,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                textureBindings.Add(new TextureBinding(
                    ShaderBindingGroup.Frame,
                    0,
                    ShaderResourceKind.SampledImage,
                    sampledTextureVersion,
                    sampleRange));
                samplerBindings.Add(new SamplerBinding(
                    ShaderBindingGroup.Frame,
                    0,
                    frameSampler
                        ?? throw new InvalidOperationException("The sampled Frame pipeline has no sampler.")));
            }

            if (storageImage is GpuTexture selectedStorageImage)
            {
                storageImageInitialVersion = graph.ImportTexture(
                    selectedStorageImage,
                    sampleRange,
                    new GraphImportOptions(
                        GraphResourceState.General,
                        contentsValid: true,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
            }

            GraphBufferVersion? secondFrameUniformVersion = null;
            GraphTextureVersion? secondSampledTextureVersion = null;
            BindingSet? secondFrameBindingSet = null;

            void RecordClear(scoped ref CommandRecorder recorder)
            {
                if (pipeline is GpuPipeline selectedPipeline)
                {
                    recorder.SetPipeline(selectedPipeline.Handle);

                    if (frameBindingSet is BindingSet selectedBindings)
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

            void RecordCopy(scoped ref CommandRecorder recorder)
            {
                if ((copySourceVersion is GraphBufferVersion selectedSource)
                    && (copyDestinationWriteVersion is GraphBufferVersion selectedDestination))
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

            if (useCompute)
            {
                var computePass = graph.AddComputePass(
                    new RenderPassDescription("offscreen-compute-storage", 0, [], hasExternalSideEffects: false),
                    RecordCompute);
                computePassId = computePass;
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
                    sampleRange);
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
                            sampleRange),
                    ],
                    []);
            }

            RenderPassId? copyPassId = null;

            if (useBufferCopy || useCompute)
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
                var selectedCopySource = copySourceVersion
                    ?? throw new InvalidOperationException("The graph copy test has no source buffer version.");
                var selectedCopyDestination = copyDestinationInitialVersion
                    ?? throw new InvalidOperationException("The graph copy test has no destination buffer version.");
                graph.ReadBuffer(
                    copyPass,
                    selectedCopySource,
                    GraphBufferUsage.CopySource,
                    selectedCopySource.Range);
                copyDestinationWriteVersion = graph.WriteBuffer(
                    copyPass,
                    selectedCopyDestination,
                    GraphBufferUsage.CopyDestination,
                    selectedCopyDestination.Range);
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
                graph.ReadBuffer(
                    pass,
                    frameUniformVersion,
                    GraphBufferUsage.UniformRead,
                    frameUniformVersion.Range);
            }

            if (sampledTextureVersion is not null)
            {
                graph.ReadTexture(
                    pass,
                    sampledTextureVersion,
                    GraphTextureUsage.SampledRead,
                    sampledTextureVersion.Range);
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
                    sampleRange);
                textureBindings.Add(new TextureBinding(
                    ShaderBindingGroup.Frame,
                    1,
                    ShaderResourceKind.StorageImage,
                    storageImageWriteVersion,
                    sampleRange));
            }

            if (useUniformBuffer || useSampledTexture || useStorageResources)
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
                AttachmentLoadAction.Clear,
                AttachmentStoreAction.Store,
                new Vector4(0.25f, 0.5f, 0.75f, 1.0f));

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
                        0,
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
                        secondSampledVersion.Range);
                    secondTextureBindings.Add(new TextureBinding(
                        ShaderBindingGroup.Frame,
                        0,
                        ShaderResourceKind.SampledImage,
                        secondSampledVersion,
                        secondSampledVersion.Range));
                    secondSamplerBindings.Add(new SamplerBinding(
                        ShaderBindingGroup.Frame,
                        0,
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
                    sampleRange);
                secondTextureBindings.Add(new TextureBinding(
                    ShaderBindingGroup.Frame,
                    1,
                    ShaderResourceKind.StorageImage,
                    secondStorageImageVersion,
                    sampleRange));
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
                    new Vector4(0.25f, 0.5f, 0.75f, 1.0f));
                graph.SetPresentationSource(secondOutputVersion);
            }
            else
            {
                graph.SetPresentationSource(outputVersion);
            }

            var compileResponse = graph.Compile();
            Assert.That(compileResponse.Status, Is.EqualTo(RenderGraphCompileStatus.Compiled));
            Assert.That(compileResponse.Diagnostics, Is.Empty);
            var plan = compileResponse.Plan
                ?? throw new InvalidOperationException("The compiled graph response omitted its plan.");
            var receipt = await renderer.SubmitAsync(renderSnapshot, plan).ConfigureAwait(false);
            RenderSubmissionReceipt? secondReceipt = null;

            {
                var secondGraph = renderer.BuildGraph(renderSnapshot, target);
                var secondBufferBindings = ImmutableArray.CreateBuilder<BufferBinding>(
                    (useUniformBuffer ? 1 : 0) + (useStorageResources ? 1 : 0));
                var secondTextureBindings = ImmutableArray.CreateBuilder<TextureBinding>(
                    (useSampledTexture ? 1 : 0) + (useStorageResources ? 1 : 0));
                var secondSamplerBindings = ImmutableArray.CreateBuilder<SamplerBinding>(useSampledTexture ? 1 : 0);
                GraphBufferVersion? secondStorageBufferInitialVersion = null;
                GraphTextureVersion? secondStorageImageInitialVersion = null;

                if (frameUniformBuffer is GpuBuffer secondSelectedBuffer)
                {
                    var range = new GraphBufferRange(0, secondSelectedBuffer.ByteLength);
                    secondFrameUniformVersion = secondGraph.ImportBuffer(
                        secondSelectedBuffer,
                        range,
                        new GraphImportOptions(
                            GraphResourceState.UniformRead,
                            contentsValid: true,
                            completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                    secondBufferBindings.Add(new BufferBinding(
                        ShaderBindingGroup.Frame,
                        0,
                        ShaderResourceKind.UniformBuffer,
                        secondFrameUniformVersion,
                        range));
                }

                if (sampledTexture is GpuTexture secondSampledTexture)
                {
                    secondSampledTextureVersion = secondGraph.ImportTexture(
                        secondSampledTexture,
                        sampleRange,
                        new GraphImportOptions(
                            GraphResourceState.ShaderSampledRead,
                            contentsValid: true,
                            completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                    secondTextureBindings.Add(new TextureBinding(
                        ShaderBindingGroup.Frame,
                        0,
                        ShaderResourceKind.SampledImage,
                        secondSampledTextureVersion,
                        sampleRange));
                    secondSamplerBindings.Add(new SamplerBinding(
                        ShaderBindingGroup.Frame,
                        0,
                        frameSampler
                            ?? throw new InvalidOperationException("The sampled Frame pipeline has no sampler.")));
                }

                if (useStorageResources && (storageBuffer is GpuBuffer secondStorageBuffer))
                {
                    var range = new GraphBufferRange(0, secondStorageBuffer.ByteLength);
                    secondStorageBufferInitialVersion = secondGraph.ImportBuffer(
                        secondStorageBuffer,
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
                        sampleRange,
                        new GraphImportOptions(
                            GraphResourceState.StorageReadWrite,
                            contentsValid: true,
                            completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                }

                var secondPass = secondGraph.AddRasterPass(
                    new RenderPassDescription("offscreen-clear-again", 0, [], hasExternalSideEffects: false),
                    RecordSecondClear);

                if (secondFrameUniformVersion is not null)
                {
                    secondGraph.ReadBuffer(
                        secondPass,
                        secondFrameUniformVersion,
                        GraphBufferUsage.UniformRead,
                        secondFrameUniformVersion.Range);
                }

                if (secondSampledTextureVersion is not null)
                {
                    secondGraph.ReadTexture(
                        secondPass,
                        secondSampledTextureVersion,
                        GraphTextureUsage.SampledRead,
                        secondSampledTextureVersion.Range);
                }

                if (secondStorageBufferInitialVersion is GraphBufferVersion storageBufferVersion)
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
                        sampleRange);
                    secondTextureBindings.Add(new TextureBinding(
                        ShaderBindingGroup.Frame,
                        1,
                        ShaderResourceKind.StorageImage,
                        secondStorageImageWriteVersion,
                        sampleRange));
                }

                if (useUniformBuffer || useSampledTexture || useStorageResources)
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
                    new Vector4(0.75f, 0.25f, 0.5f, 1.0f));
                secondGraph.SetPresentationSource(secondOutputVersion);
                var secondCompileResponse = secondGraph.Compile();
                Assert.That(secondCompileResponse.Status, Is.EqualTo(RenderGraphCompileStatus.Compiled));
                var secondPlan = secondCompileResponse.Plan
                    ?? throw new InvalidOperationException("The second compiled graph response omitted its plan.");
                secondReceipt = await renderer.SubmitAsync(renderSnapshot, secondPlan).ConfigureAwait(false);
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
                target,
                new TextureSubresource(0, 0, TextureAspect.Color))
                .ConfigureAwait(false);
            var padded = new byte[checked((int)readback.ByteLength)];

            using (var readLease = readback.AcquireReadLease())
            {
                var copyAction = new ReadbackCopyAction(padded);
                readLease.UseWindow(0, padded.Length, ref copyAction);
            }

            var centerPixelOffset = checked(
                (int)((readback.RowPitch * (targetExtent.Height / 2)) + (targetExtent.Width / 2 * 4)));
            var drawPixelCount = 0;
            var expectedRed = useStorageResources
                ? (byte)255
                : (useSampledTexture ? (byte)32 : (useUniformBuffer ? (byte)0 : (byte)255));
            var expectedGreen = useStorageResources
                ? (byte)255
                : (useSampledTexture ? (byte)128 : (useUniformBuffer ? (byte)255 : (byte)0));
            var expectedBlue = useStorageResources
                ? (byte)255
                : (useSampledTexture ? (byte)224 : (useUniformBuffer ? (byte)0 : (byte)255));
            var expectedBackgroundRed = 191;
            var expectedBackgroundGreen = 64;
            var expectedBackgroundBlue = 128;

            for (var y = 0; y < readback.Height; y++)
            {
                for (var x = 0; x < readback.Width; x++)
                {
                    var pixelOffset = checked((int)((readback.RowPitch * y) + (x * 4)));

                    if ((padded[pixelOffset] == expectedRed)
                        && (padded[pixelOffset + 1] == expectedGreen)
                        && (padded[pixelOffset + 2] == expectedBlue)
                        && (padded[pixelOffset + 3] == 255))
                    {
                        drawPixelCount++;
                    }
                }
            }

            Assert.Multiple(() => {
                Assert.That(padded[0], Is.InRange(expectedBackgroundRed - 1, expectedBackgroundRed + 1));
                Assert.That(padded[1], Is.InRange(expectedBackgroundGreen - 1, expectedBackgroundGreen + 1));
                Assert.That(padded[2], Is.InRange(expectedBackgroundBlue - 1, expectedBackgroundBlue + 1));
                Assert.That(padded[3], Is.EqualTo(255));

                if (pipeline is not null)
                {
                    Assert.That(drawPixelCount, Is.GreaterThan(0));
                }
                else
                {
                    Assert.That(padded[centerPixelOffset], Is.InRange(190, 192));
                    Assert.That(padded[centerPixelOffset + 1], Is.InRange(63, 65));
                    Assert.That(padded[centerPixelOffset + 2], Is.InRange(127, 129));
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
                    new TextureSubresource(0, 0, TextureAspect.Color))
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

                Assert.That(BitConverter.ToUInt32(computeStorageBytes, 0), Is.EqualTo(2u));

                var computeStorageImage = storageImage
                    ?? throw new InvalidOperationException("The graph compute test has no storage image.");
                using var computeImageReadback = await graphicsDevice.ReadbackTextureAsync(
                    computeStorageImage,
                    new TextureSubresource(0, 0, TextureAspect.Color))
                    .ConfigureAwait(false);
                var computeStorageImageBytes = new byte[checked((int)computeImageReadback.ByteLength)];

                using (var computeImageReadLease = computeImageReadback.AcquireReadLease())
                {
                    var copyAction = new ReadbackCopyAction(computeStorageImageBytes);
                    computeImageReadLease.UseWindow(0, computeStorageImageBytes.Length, ref copyAction);
                }

                Assert.That(BitConverter.ToUInt32(computeStorageImageBytes, 0), Is.EqualTo(2u));
            }

            if (useBufferCopy || useCompute)
            {
                var destinationBufferForReadback = copyDestinationBuffer
                    ?? throw new InvalidOperationException("The graph copy test has no destination buffer.");
                var expectedBytes = expectedCopiedBytes
                    ?? throw new InvalidOperationException("The graph copy test has no expected bytes.");
                using var copyReadback = await graphicsDevice.ReadbackBufferAsync(
                    destinationBufferForReadback,
                    0,
                    expectedBytes.Length)
                    .ConfigureAwait(false);
                var copiedBytes = new byte[expectedBytes.Length];

                using (var copyReadLease = copyReadback.AcquireReadLease(0, copiedBytes.Length))
                {
                    var copyAction = new ReadbackCopyAction(copiedBytes);
                    copyReadLease.UseWindow(0, copiedBytes.Length, ref copyAction);
                }

                Assert.That(copiedBytes, Is.EqualTo(expectedBytes));
            }

            var finalReceipt = secondReceipt ?? receipt;
            TestContext.WriteLine(
                $"Backend={backend}; Adapter={graphicsDevice.Capabilities.AdapterName}; "
                + $"Submissions={receipt.SubmissionSequence}/{finalReceipt.SubmissionSequence}; "
                + $"Fence={finalReceipt.CompletedFence}; "
                + $"UniformBuffer={useUniformBuffer}; "
                + $"SampledTexture={useSampledTexture}; "
                + $"StorageResources={useStorageResources}; "
                + $"BufferCopy={useBufferCopy || useCompute}; "
                + $"Compute={useCompute}; "
                + $"Pixel={padded[centerPixelOffset]:X2}{padded[centerPixelOffset + 1]:X2}"
                + $"{padded[centerPixelOffset + 2]:X2}{padded[centerPixelOffset + 3]:X2}; "
                + $"DrawPixels={drawPixelCount}");
        }
        finally
        {
            snapshot?.Dispose();
            pipeline?.Dispose();
            computePipeline?.Dispose();
            frameSampler?.Dispose();
            frameUniformBuffer?.Dispose();
            storageBuffer?.Dispose();
            copySourceBuffer?.Dispose();
            copyDestinationBuffer?.Dispose();
            sampledTexture?.Dispose();
            storageImage?.Dispose();
            target?.Dispose();

            if (host is not null)
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }

            File.Delete(packagePath);
        }
    }
}
