// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Buffers;
using TerraFX.Content.Tools;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Graphics.Vulkan;
using TerraFX.Hosting;
using TerraFX.Platform;
using TerraFX.Rendering;
using TerraFX.Runtime.Tests.Hosting;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

internal sealed partial class SceneAssetStoreTests
{
    [Test]
    public Task SnapshotPinnedAtlasPageBecomesResidentAfterSceneAndSnapshotRetireAsync()
        => RunSnapshotAtlasPageResidencyAsync(renderThroughNativeGraph: false);

    [Test]
    public Task SnapshotPinnedAtlasPageSamplesThroughNativeGraphAsync()
        => RunSnapshotAtlasPageResidencyAsync(renderThroughNativeGraph: true);

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "SubmitAsync retains snapshot and imported-resource pins before its first asynchronous wait; this verifies they survive disposal until GPU completion.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "The original graph failure is logged before owner cleanup so a secondary shutdown timeout cannot obscure its cause.")]
    private async Task RunSnapshotAtlasPageResidencyAsync(bool renderThroughNativeGraph)
    {
        static OwnedBuffer<byte> CreateBytecodeStorage(string path)
        {
            var bytecode = File.ReadAllBytes(path);
            var storage = UnmanagedBuffer<byte>.Allocate(
                bytecode.Length,
                new BufferAllocationOptions(bytecode.Length, alignment: 1));
            storage.CopyFrom(bytecode.AsSpan(), destinationOffset: 0);
            return storage.TransferToOwnedBuffer();
        }

        var shaderFixtureDirectory = Environment.GetEnvironmentVariable("TERRAFX_M04_SHADER_FIXTURE_DIRECTORY");

        if (renderThroughNativeGraph && string.IsNullOrWhiteSpace(shaderFixtureDirectory))
        {
            Assert.Ignore("The M04 shader fixture is required for package-backed Sprite graph submission.");
            return;
        }

        const int TextureAssetId = 1;
        const int AtlasAssetId = 2;
        const int MaterialAssetId = 3;
        const int SceneAssetId = 4;
        var schemas = CreateRendererRegistry();
        var frameId = new Guid("30000000-0000-4000-8000-000000000001");
        var textureRecord = CreateRenderAtlasPageTextureRecord(17);
        var atlasRecord = CreateRenderAtlasRecord(
            TextureAssetId,
            CreateRenderAtlasFrameRecord(frameId, page: 0, x: 4, y: 4, width: 4, height: 4));
        var materialRecord = CreateRenderMaterialAssetRecord(
            isSprite: true,
            CreateRenderMaterialSpriteParametersRecord(),
            alphaMode: 2,
            doubleSided: false,
            depthTest: false,
            depthWrite: false,
            cullMode: 0,
            outputAlpha: 1,
            requiredCapabilities: 0x009);
        var sceneBytes = SceneAssetReaderTests.Fixtures.CreateScene(
            new AssetId(SceneAssetId),
            additionalEntityComponents: [
                CreateCameraRecord(order: 0),
                CreateRenderSpriteRecord(AtlasAssetId, frameId, materialAssetId: MaterialAssetId),
            ]);
        var packageBytes = Package.CreatePackage(
            new Package.Entry(TextureAssetId, Package.CreateEnvelope(TextureAssetId, textureRecord)),
            new Package.Entry(AtlasAssetId, Package.CreateEnvelope(AtlasAssetId, atlasRecord)),
            new Package.Entry(MaterialAssetId, Package.CreateEnvelope(MaterialAssetId, materialRecord)),
            new Package.Entry(SceneAssetId, Package.CreateEnvelope(SceneAssetId, sceneBytes)));
        var packagePath = Package.WritePackage(packageBytes);
        var assetRegistrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = SceneSupport.RegisterAssetCodecs(assetRegistrationBuilder);
        _ = Renderer.RegisterAssetCodecs(assetRegistrationBuilder);
        var hostOptions = new EngineHostOptions(
            HostProfile.OffscreenNative,
            PlatformBackend.Headless,
            GraphicsBackend.Vulkan,
            VulkanGraphicsBackend.CreateRegistration(),
            new AssetStoreOptions(
                packagePath,
                SHA256.HashData(packageBytes),
                assetRegistrationBuilder.Build())) {
            CpuWorkerCount = 0,
            PersistenceMetadata = WorldPersistenceHeaderWriterTests.CreateMetadata(),
        };
        EngineHost? host = null;
        RenderSnapshot3D? snapshot = null;
        GpuResidencyLease? residencyLease = null;
        GpuTexture? targetTexture = null;
        GpuBuffer? vertexBuffer = null;
        GpuBuffer? indexBuffer = null;
        GpuSampler? sampler = null;
        GpuPipeline? pipeline = null;

        try
        {
            try
            {
                host = await EngineHost.StartAsync(hostOptions).ConfigureAwait(false);
            }
            catch (DllNotFoundException exception)
            {
                Assert.Ignore($"Vulkan system library is unavailable: {exception.Message}");
                return;
            }
            catch (EntryPointNotFoundException exception)
            {
                Assert.Ignore($"Vulkan system entry point is unavailable: {exception.Message}");
                return;
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No Vulkan device satisfies OffscreenNative: {exception.Message}");
                return;
            }

            var engineHost = host
                ?? throw new InvalidOperationException("Vulkan EngineHost startup returned no host.");
            var graphicsDevice = engineHost.GraphicsDevice
                ?? throw new InvalidOperationException("OffscreenNative Vulkan startup returned no GraphicsDevice.");
            var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
            _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
            _ = worldRegistrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration());
            var worldRegistration = worldRegistrationBuilder.Build();
            World? world = null;
            SceneLoader? loader = null;

            await engineHost.InvokeAsync(context => {
                world = context.CreateWorld(new WorldOptions(worldRegistration) {
                    CpuWorkerCount = 0,
                    InitialReplayLineageId = Guid.NewGuid(),
                    WorldGuid = Guid.NewGuid(),
                });
                loader = new SceneLoader(world, engineHost.Assets);
            }).ConfigureAwait(false);

            SceneInstance? sceneInstance = null;

            using (var prepared = await loader!.PrepareAsync(
                    new AssetId(SceneAssetId),
                    new SceneLoadOptions()).ConfigureAwait(false))
            {
                var activation = await loader.ActivateAsync(prepared)
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(activation.Status, Is.EqualTo(SceneActivationStatus.Activated));
                sceneInstance = activation.Instance
                    ?? throw new InvalidOperationException("The activated Sprite Scene omitted its instance.");
            }

            var activeSceneInstance = sceneInstance
                ?? throw new InvalidOperationException("The activated Sprite Scene omitted its instance.");
            var cameraId = Guid.Empty;
            var targetExtent = new PixelSize(32, 32);

            await engineHost.InvokeAsync(_ => {
                if (!activeSceneInstance.TryGetEntity(SceneAssetReaderTests.Fixtures.RootEntityId, out var cameraEntity))
                {
                    throw new InvalidOperationException("The activated Sprite Scene did not retain its camera Entity.");
                }

                cameraId = world!.GetEntityId(cameraEntity);
                var extraction = engineHost.Renderer.Extract(
                    world,
                    new RenderExtractionOptions3D(
                        world.CurrentFrame3,
                        targetExtent,
                        [cameraId]));

                if (extraction.Status != RenderExtractionStatus.Extracted)
                {
                    throw new InvalidOperationException(
                        $"The Sprite snapshot extraction failed: {string.Join("; ", extraction.Diagnostics)}");
                }

                snapshot = extraction.Snapshot as RenderSnapshot3D
                    ?? throw new InvalidOperationException("The successful Sprite extraction omitted its 3D snapshot.");
            }).ConfigureAwait(false);

            var spriteSnapshot = snapshot
                ?? throw new InvalidOperationException("The host renderer omitted its Sprite snapshot.");
            Assert.That(spriteSnapshot.SpriteDraws, Has.Length.EqualTo(1));
            Assert.That(spriteSnapshot.AssetPins, Is.Not.Null);
            residencyLease = await engineHost.Renderer.EnsureSpritePageResidencyAsync(
                spriteSnapshot,
                spriteDrawIndex: 0,
                new ResidencyOptions(TextureUsage.Sampled | TextureUsage.CopySource)).ConfigureAwait(false);

            if (renderThroughNativeGraph)
            {
                var fixtureDirectory = shaderFixtureDirectory
                    ?? throw new InvalidOperationException("The package-backed Sprite graph has no shader fixture directory.");
                using var vertexBytecodeStorage = CreateBytecodeStorage(Path.Combine(
                    fixtureDirectory,
                    "M04OffscreenTriangle.vertex-sprite.spv"));
                using var pixelBytecodeStorage = CreateBytecodeStorage(Path.Combine(
                    fixtureDirectory,
                    "M04OffscreenTriangle.pixel-sprite.spv"));
                using var vertexBytecode = vertexBytecodeStorage.AcquireReadLease(
                    0,
                    vertexBytecodeStorage.Length);
                using var pixelBytecode = pixelBytecodeStorage.AcquireReadLease(
                    0,
                    pixelBytecodeStorage.Length);
                var vertexReflection = M04ShaderFixtureCompiler.ReadTypedReflection(fixtureDirectory, "vertex-sprite");
                var pixelReflection = M04ShaderFixtureCompiler.ReadTypedReflection(fixtureDirectory, "pixel-sprite");
                var pipelineDescription = new GraphicsPipelineDescription(
                    [
                        new ShaderBytecodeInput(
                            ShaderStage.Vertex,
                            ShaderBinaryFormat.SpirV,
                            vertexBytecode,
                            vertexReflection),
                        new ShaderBytecodeInput(
                            ShaderStage.Pixel,
                            ShaderBinaryFormat.SpirV,
                            pixelBytecode,
                            pixelReflection),
                    ],
                    [
                        new VertexElementDescription(0, VertexElementFormat.Float3, 0),
                        new VertexElementDescription(1, VertexElementFormat.Float2, 3 * sizeof(float)),
                    ],
                    vertexStride: 5 * sizeof(float),
                    [TextureFormat.Rgba8Unorm],
                    depthTargetFormat: null,
                    PrimitiveTopology.TriangleList,
                    PipelineBlendMode.Opaque,
                    PipelineDepthMode.Disabled,
                    PipelineCullMode.None,
                    frontFaceClockwise: false,
                    bindingLayoutVersion: 1);
                pipeline = await graphicsDevice.CreatePipelineAsync(pipelineDescription).ConfigureAwait(false);
                sampler = await graphicsDevice.CreateSamplerAsync(
                    new SamplerDescription(
                        SamplerFilter.Linear,
                        SamplerFilter.Linear,
                        SamplerFilter.None,
                        SamplerAddressMode.Clamp,
                        SamplerAddressMode.Clamp,
                        SamplerAddressMode.Clamp,
                        maximumAnisotropy: 1.0f)).ConfigureAwait(false);
                var quad = spriteSnapshot.SpriteDraws[0].Quad;
                var spriteVertices = new[] {
                    quad.Vertex0,
                    quad.Vertex1,
                    quad.Vertex2,
                    quad.Vertex3,
                };
                var vertexBytes = MemoryMarshal.AsBytes(spriteVertices.AsSpan()).ToArray();
                vertexBuffer = await graphicsDevice.CreateBufferAsync(
                    new BufferDescription(
                        vertexBytes.Length,
                        BufferUsage.Vertex | BufferUsage.CopyDestination,
                        GpuMemoryKind.DeviceLocal,
                        structuredStride: null,
                        "snapshot-atlas-sprite-vertices")).ConfigureAwait(false);
                var vertexUpload = await graphicsDevice.UploadBufferAsync(vertexBuffer, 0, vertexBytes).ConfigureAwait(false);
                Assert.That(vertexUpload.Status, Is.EqualTo(UploadStatus.Ready));
                var indexBytes = MemoryMarshal.AsBytes(RenderSpriteGeometry.Indices.AsSpan()).ToArray();
                indexBuffer = await graphicsDevice.CreateBufferAsync(
                    new BufferDescription(
                        indexBytes.Length,
                        BufferUsage.Index | BufferUsage.CopyDestination,
                        GpuMemoryKind.DeviceLocal,
                        structuredStride: null,
                        "snapshot-atlas-sprite-indices")).ConfigureAwait(false);
                var indexUpload = await graphicsDevice.UploadBufferAsync(indexBuffer, 0, indexBytes).ConfigureAwait(false);
                Assert.That(indexUpload.Status, Is.EqualTo(UploadStatus.Ready));
                targetTexture = await graphicsDevice.CreateTextureAsync(
                    new TextureDescription(
                        TextureDimension.Dimension2D,
                        targetExtent.Width,
                        targetExtent.Height,
                        depth: 1,
                        arrayLayers: 1,
                        mipLevels: 1,
                        TextureFormat.Rgba8Unorm,
                        TextureUsage.ColorAttachment | TextureUsage.CopySource,
                        "snapshot-atlas-residency-target")).ConfigureAwait(false);

                var graph = engineHost.Renderer.BuildGraph(spriteSnapshot, targetTexture);
                var sampleRange = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);
                var sampledPage = graph.ImportTexture(
                    residencyLease.Texture,
                    sampleRange,
                    new GraphImportOptions(
                        GraphResourceState.General,
                        contentsValid: true,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                var graphBindingSet = new BindingSet(
                    [],
                    [
                        new TextureBinding(
                            ShaderBindingGroup.Frame,
                            0,
                            ShaderResourceKind.SampledImage,
                            sampledPage,
                            sampleRange),
                    ],
                    [
                        new SamplerBinding(
                            ShaderBindingGroup.Frame,
                            0,
                            sampler
                                ?? throw new InvalidOperationException("The snapshot-atlas graph has no sampler.")),
                    ]);
                var vertexRange = new GraphBufferRange(0, vertexBuffer.ByteLength);
                var vertexVersion = graph.ImportBuffer(
                    vertexBuffer,
                    vertexRange,
                    new GraphImportOptions(
                        GraphResourceState.General,
                        contentsValid: true,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                var vertexBinding = new VertexBufferBinding(0, vertexVersion, vertexRange, 5 * sizeof(float));
                var indexRange = new GraphBufferRange(0, indexBuffer.ByteLength);
                var indexVersion = graph.ImportBuffer(
                    indexBuffer,
                    indexRange,
                    new GraphImportOptions(
                        GraphResourceState.General,
                        contentsValid: true,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                var indexBinding = new IndexBufferBinding(indexVersion, indexRange, IndexFormat.UInt16);
                var selectedPipeline = pipeline
                    ?? throw new InvalidOperationException("The snapshot-atlas graph has no graphics pipeline.");

                void RecordPackageSample(scoped ref CommandRecorder recorder)
                {
                    recorder.SetPipeline(selectedPipeline.Handle);
                    recorder.SetVertexBuffer(vertexBinding);
                    recorder.SetIndexBuffer(indexBinding);
                    recorder.SetBindings(graphBindingSet);
                    recorder.DrawIndexed(new DrawIndexedArguments(RenderSpriteGeometry.Indices.Length, 1, 0, 0, 0));
                }

                var rasterPass = graph.AddRasterPass(
                    new RenderPassDescription("snapshot-atlas-page-sample", 0, [], hasExternalSideEffects: false),
                    RecordPackageSample);
                graph.ReadBuffer(rasterPass, vertexVersion, GraphBufferUsage.VertexRead, vertexRange);
                graph.ReadBuffer(rasterPass, indexVersion, GraphBufferUsage.IndexRead, indexRange);
                graph.ReadTexture(rasterPass, sampledPage, GraphTextureUsage.SampledRead, sampleRange);
                var targetRange = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);
                var output = graph.WriteColorAttachment(
                    rasterPass,
                    graph.OffscreenColorTargetVersion,
                    targetRange,
                    AttachmentLoadAction.Clear,
                    AttachmentStoreAction.Store,
                    spriteSnapshot.Cameras[0].ClearColor);
                graph.SetPresentationSource(output);

                var compileResponse = graph.Compile();
                Assert.That(compileResponse.Status, Is.EqualTo(RenderGraphCompileStatus.Compiled));
                var plan = compileResponse.Plan
                    ?? throw new InvalidOperationException("The snapshot-atlas graph compile omitted its plan.");
                var submissionTask = engineHost.Renderer.SubmitAsync(spriteSnapshot, plan);
                spriteSnapshot.Dispose();
                Assert.That(spriteSnapshot.AssetPins, Is.Null);
                vertexBuffer.Dispose();
                vertexBuffer = null;
                indexBuffer.Dispose();
                indexBuffer = null;
                var submission = await submissionTask.ConfigureAwait(false);
                Assert.That(submission.CompletedFence, Is.GreaterThan(0));

                using var graphReadback = await graphicsDevice.ReadbackTextureAsync(
                    targetTexture,
                    new TextureSubresource(0, 0, TextureAspect.Color)).ConfigureAwait(false);
                var graphReadbackBytes = new byte[checked((int)graphReadback.ByteLength)];

                using (var readLease = graphReadback.AcquireReadLease())
                {
                    readLease.CopyWindow(0, graphReadbackBytes);
                }

                var centerPixelOffset = checked(
                    (int)((graphReadback.RowPitch * (targetExtent.Height / 2)) + (targetExtent.Width / 2 * 4)));
                Assert.Multiple(() => {
                    Assert.That(graphReadbackBytes[centerPixelOffset], Is.EqualTo(17));
                    Assert.That(graphReadbackBytes[centerPixelOffset + 1], Is.EqualTo(17));
                    Assert.That(graphReadbackBytes[centerPixelOffset + 2], Is.EqualTo(17));
                    Assert.That(graphReadbackBytes[centerPixelOffset + 3], Is.EqualTo(17));
                    Assert.That(graphReadbackBytes[0], Is.Not.EqualTo(17));
                });
            }

            var unload = await activeSceneInstance.UnloadAsync(HierarchyDestroyMode.DestroySubtree)
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(unload.Status, Is.EqualTo(SceneUnloadStatus.Unloaded));
            spriteSnapshot.Dispose();
            Assert.That(spriteSnapshot.AssetPins, Is.Null);
            snapshot = null;

            var residentTexture = residencyLease.Texture;
            var expectedMip = residencyLease.Asset.Mips[0];
            Assert.That(expectedMip.Pixels[0], Is.EqualTo(17));
            Assert.That(residencyLease.DeviceEpoch, Is.EqualTo(graphicsDevice.DeviceEpoch));
            using var readback = await graphicsDevice.ReadbackTextureAsync(
                residentTexture,
                new TextureSubresource(0, 0, TextureAspect.Color)).ConfigureAwait(false);
            Assert.That(readback.Format, Is.EqualTo(residentTexture.Description.Format));
            Assert.That(readback.Width, Is.EqualTo(expectedMip.Width));
            Assert.That(readback.Height, Is.EqualTo(expectedMip.Height));

            var expected = new byte[expectedMip.Pixels.Length];
            var actual = new byte[expected.Length];

            for (var index = 0; index < expected.Length; index++)
            {
                expected[index] = expectedMip.Pixels[index];
            }

            using (var readLease = readback.AcquireReadLease())
            {
                var rowLength = checked(readback.Width * 4);

                for (var row = 0; row < readback.Height; row++)
                {
                    readLease.CopyWindow(
                        checked((nint)row * readback.RowPitch),
                        actual.AsSpan(checked(row * rowLength), rowLength));
                }
            }

            Assert.That(actual, Is.EqualTo(expected));
            residencyLease.Dispose();
            residencyLease = null;
            await engineHost.StopAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await TestContext.Progress.WriteLineAsync($"Sprite residency operation failed before cleanup: {exception}")
                .ConfigureAwait(false);
            throw;
        }
        finally
        {
            pipeline?.Dispose();
            sampler?.Dispose();
            indexBuffer?.Dispose();
            vertexBuffer?.Dispose();
            targetTexture?.Dispose();
            snapshot?.Dispose();
            residencyLease?.Dispose();

            if (host is not null)
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }

            File.Delete(packagePath);
        }
    }
}
