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
    [TestCase(GraphicsBackend.D3D12, false, false)]
    [TestCase(GraphicsBackend.Vulkan, false, false)]
    [TestCase(GraphicsBackend.D3D12, true, false)]
    [TestCase(GraphicsBackend.Vulkan, true, false)]
    [TestCase(GraphicsBackend.D3D12, false, true)]
    [TestCase(GraphicsBackend.Vulkan, false, true)]
    [TestCase(GraphicsBackend.D3D12, true, true)]
    [TestCase(GraphicsBackend.Vulkan, true, true)]
    public async Task SubmitsOffscreenClearGraphAndReadsBackAsync(
        GraphicsBackend backend,
        bool useUniformBuffer,
        bool useSampledTexture)
    {
        if ((backend == GraphicsBackend.D3D12) && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 offscreen graph path targets Windows 11.");
            return;
        }

        var shaderFixtureDirectory = Environment.GetEnvironmentVariable("TERRAFX_M04_SHADER_FIXTURE_DIRECTORY");

        if ((useUniformBuffer || useSampledTexture) && string.IsNullOrWhiteSpace(shaderFixtureDirectory))
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
        GpuBuffer? frameUniformBuffer = null;
        GpuPipeline? pipeline = null;
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
                var pixelStage = useSampledTexture
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

            var graph = renderer.BuildGraph(
                renderSnapshot,
                target);
            var sampleRange = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);
            GraphBufferVersion? frameUniformVersion = null;
            GraphTextureVersion? sampledTextureVersion = null;
            BindingSet? frameBindingSet = null;
            var bufferBindings = ImmutableArray.CreateBuilder<BufferBinding>(useUniformBuffer ? 1 : 0);
            var textureBindings = ImmutableArray.CreateBuilder<TextureBinding>(useSampledTexture ? 1 : 0);
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

            if (useUniformBuffer || useSampledTexture)
            {
                frameBindingSet = new BindingSet(
                    bufferBindings.MoveToImmutable(),
                    textureBindings.MoveToImmutable(),
                    samplerBindings.MoveToImmutable());
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
                }
            }

            var pass = graph.AddRasterPass(
                new RenderPassDescription("offscreen-clear", 0, [], hasExternalSideEffects: false),
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

            var outputVersion = graph.WriteColorAttachment(
                pass,
                graph.OffscreenColorTargetVersion,
                new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color),
                AttachmentLoadAction.Clear,
                AttachmentStoreAction.Store,
                new Vector4(0.25f, 0.5f, 0.75f, 1.0f));
            graph.SetPresentationSource(outputVersion);
            var compileResponse = graph.Compile();
            Assert.That(compileResponse.Status, Is.EqualTo(RenderGraphCompileStatus.Compiled));
            Assert.That(compileResponse.Diagnostics, Is.Empty);
            var plan = compileResponse.Plan
                ?? throw new InvalidOperationException("The compiled graph response omitted its plan.");
            var receipt = await renderer.SubmitAsync(renderSnapshot, plan).ConfigureAwait(false);

            var secondGraph = renderer.BuildGraph(renderSnapshot, target);
            var secondBufferBindings = ImmutableArray.CreateBuilder<BufferBinding>(useUniformBuffer ? 1 : 0);
            var secondTextureBindings = ImmutableArray.CreateBuilder<TextureBinding>(useSampledTexture ? 1 : 0);
            var secondSamplerBindings = ImmutableArray.CreateBuilder<SamplerBinding>(useSampledTexture ? 1 : 0);

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

            if (useUniformBuffer || useSampledTexture)
            {
                secondFrameBindingSet = new BindingSet(
                    secondBufferBindings.MoveToImmutable(),
                    secondTextureBindings.MoveToImmutable(),
                    secondSamplerBindings.MoveToImmutable());
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
            var secondReceipt = await renderer.SubmitAsync(renderSnapshot, secondPlan).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(receipt.SubmissionSequence, Is.GreaterThan(0));
                Assert.That(receipt.CompletedFence, Is.GreaterThan(0));
                Assert.That(receipt.PresentationSequence, Is.Null);
                Assert.That(receipt.DeviceEpoch, Is.EqualTo(graphicsDevice.DeviceEpoch));
                Assert.That(receipt.SurfaceEpoch, Is.Zero);
                Assert.That(receipt.SwapchainEpoch, Is.Zero);
                Assert.That(secondReceipt.SubmissionSequence, Is.EqualTo(receipt.SubmissionSequence + 1));
                Assert.That(secondReceipt.CompletedFence, Is.GreaterThan(receipt.CompletedFence));
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
            var expectedRed = useSampledTexture ? (byte)32 : (useUniformBuffer ? (byte)0 : (byte)255);
            var expectedGreen = useSampledTexture ? (byte)128 : (useUniformBuffer ? (byte)255 : (byte)0);
            var expectedBlue = useSampledTexture ? (byte)224 : (useUniformBuffer ? (byte)0 : (byte)255);

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
                Assert.That(padded[0], Is.InRange(190, 192));
                Assert.That(padded[1], Is.InRange(63, 65));
                Assert.That(padded[2], Is.InRange(127, 129));
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
            TestContext.WriteLine(
                $"Backend={backend}; Adapter={graphicsDevice.Capabilities.AdapterName}; "
                + $"Submissions={receipt.SubmissionSequence}/{secondReceipt.SubmissionSequence}; "
                + $"Fence={secondReceipt.CompletedFence}; "
                + $"UniformBuffer={useUniformBuffer}; "
                + $"SampledTexture={useSampledTexture}; "
                + $"Pixel={padded[centerPixelOffset]:X2}{padded[centerPixelOffset + 1]:X2}"
                + $"{padded[centerPixelOffset + 2]:X2}{padded[centerPixelOffset + 3]:X2}; "
                + $"DrawPixels={drawPixelCount}");
        }
        finally
        {
            snapshot?.Dispose();
            pipeline?.Dispose();
            frameSampler?.Dispose();
            frameUniformBuffer?.Dispose();
            sampledTexture?.Dispose();
            target?.Dispose();

            if (host is not null)
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }

            File.Delete(packagePath);
        }
    }
}
