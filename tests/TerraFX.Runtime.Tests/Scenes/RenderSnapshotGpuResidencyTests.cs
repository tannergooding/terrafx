// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.InteropServices;
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
using TerraFX.Runtime.Tests.Hosting;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

internal sealed partial class SceneAssetStoreTests
{
    private static byte[] CreatePixelSnapGradientAtlasPageTextureRecord()
    {
        var pixels = new byte[16 * 16 * 4];
        byte[] redValues = [0, 80, 176, byte.MaxValue];

        for (var y = 2; y < 10; y++)
        {
            for (var x = 2; x < 10; x++)
            {
                var red = redValues[Math.Clamp(x, 4, 7) - 4];
                var offset = ((y * 16) + x) * 4;
                pixels[offset] = red;
                pixels[offset + 1] = 0;
                pixels[offset + 2] = checked((byte)(byte.MaxValue - red));
                pixels[offset + 3] = byte.MaxValue;
            }
        }

        return CreateRenderTextureAssetRecord(
            16,
            16,
            RenderTextureColorSpace.Linear,
            RenderTextureAlphaMode.Premultiplied,
            RenderTextureSemantic.Color,
            RenderTextureMipPolicy.BaseOnly,
            CreateRenderTextureMipRecord(0, 16, 16, pixels));
    }

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInSpriteC0GraphCoversSharedDiagonalOnceAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotAtlasPageResidencyAsync(
            renderThroughNativeGraph: true,
            graphicsBackend: graphicsBackend,
            useBuiltInSpriteGraph: true,
            targetExtentOverride: new PixelSize(31, 31));

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public async Task SnapshotBuiltInSpriteC0GraphPixelSnapChangesSamplingAlignmentAsync(GraphicsBackend graphicsBackend)
    {
        var snapped = await RunSnapshotAtlasPageResidencyAsync(
            renderThroughNativeGraph: true,
            graphicsBackend: graphicsBackend,
            useBuiltInSpriteGraph: true,
            usePixelSnapGradient: true,
            spriteRootPositionX: 0.21f,
            pixelSnap: true).ConfigureAwait(false);
        var unsnapped = await RunSnapshotAtlasPageResidencyAsync(
            renderThroughNativeGraph: true,
            graphicsBackend: graphicsBackend,
            useBuiltInSpriteGraph: true,
            usePixelSnapGradient: true,
            spriteRootPositionX: 0.21f,
            pixelSnap: false).ConfigureAwait(false);

        Assert.That(snapped, Is.Not.Null);
        Assert.That(unsnapped, Is.Not.Null);

        var snappedPixels = snapped
            ?? throw new InvalidOperationException("The pixel-snapped Sprite graph omitted its readback.");
        var unsnappedPixels = unsnapped
            ?? throw new InvalidOperationException("The unsnapped Sprite graph omitted its readback.");
        var differingPixels = 0;

        for (var index = 0; index < snappedPixels.Length; index += 4)
        {
            if (!snappedPixels.AsSpan(index, 4).SequenceEqual(unsnappedPixels.AsSpan(index, 4)))
            {
                differingPixels++;
            }
        }

        Assert.That(differingPixels, Is.GreaterThan(0));
    }

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInSpriteC0GraphRejectsMaterialTextureOverrideAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotAtlasPageResidencyAsync(
            renderThroughNativeGraph: true,
            graphicsBackend: graphicsBackend,
            useBuiltInSpriteGraph: true,
            useUnsupportedMaterialTexture: true);
    [Test]
    public Task SnapshotBuiltInSpriteC0GraphRejectsMissingInitialClearAsync()
        => RunSnapshotAtlasPageResidencyAsync(
            renderThroughNativeGraph: true,
            graphicsBackend: GraphicsBackend.Vulkan,
            useBuiltInSpriteGraph: true,
            cameraClearFlags: CameraClearFlags.Depth,
            expectUninitializedTargetRejection: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInSpriteC0GraphRejectsNegativeHdrClearColorAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotAtlasPageResidencyAsync(
            renderThroughNativeGraph: true,
            graphicsBackend: graphicsBackend,
            useBuiltInSpriteGraph: true,
            cameraClearColor: new Vector4(-0.01f, 0.0f, 0.0f, 1.0f),
            expectUnsupportedHdrClearRange: true);

    [Test]
    public Task SnapshotBuiltInSpriteC0GraphRejectsPartialInitialViewportAsync()
        => RunSnapshotAtlasPageResidencyAsync(
            renderThroughNativeGraph: true,
            graphicsBackend: GraphicsBackend.Vulkan,
            useBuiltInSpriteGraph: true,
            cameraViewport: new Vector4(0.0f, 0.0f, 0.5f, 1.0f),
            expectUninitializedTargetRejection: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInSpriteC0GraphRendersAtlasPageAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotAtlasPageResidencyAsync(
            renderThroughNativeGraph: true,
            graphicsBackend: graphicsBackend,
            useBuiltInSpriteGraph: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInSpriteC0GraphSharesAtlasPageAcrossSceneInstancesAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotAtlasPageResidencyAsync(
            renderThroughNativeGraph: true,
            graphicsBackend: graphicsBackend,
            useBuiltInSpriteGraph: true,
            activateSecondSceneInstance: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInSpriteC0GraphToneMapsHdrClearColorOnceAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotAtlasPageResidencyAsync(
            renderThroughNativeGraph: true,
            graphicsBackend: graphicsBackend,
            useBuiltInSpriteGraph: true,
            cameraClearColor: new Vector4(2.0f, 0.5f, 0.0f, 1.0f),
            expectHdrClearColorReadback: true);

    [Test]
    public Task SnapshotGraphCancellationReleasesImportedResourcePinsAsync()
        => RunSnapshotAtlasPageResidencyAsync(renderThroughNativeGraph: true, cancelGraphAdmission: true);

    [Test]
    public Task SnapshotGraphRecordingFailureReleasesImportedResourcePinsAsync()
        => RunSnapshotAtlasPageResidencyAsync(renderThroughNativeGraph: true, failGraphRecording: true);

    [Test]
    public Task SnapshotPinnedAtlasPageBecomesResidentAfterSceneAndSnapshotRetireAsync()
        => RunSnapshotAtlasPageResidencyAsync(renderThroughNativeGraph: false);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotPinnedAtlasPageSamplesThroughNativeGraphAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotAtlasPageResidencyAsync(
            renderThroughNativeGraph: true,
            graphicsBackend: graphicsBackend);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotPinnedAtlasPageSubmitsObjectAndMaterialGroupsAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotAtlasPageResidencyAsync(
            renderThroughNativeGraph: true,
            graphicsBackend: graphicsBackend,
            useM05SpritePipeline: true);

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "SubmitAsync retains snapshot and imported-resource pins before its first asynchronous wait; this verifies they survive disposal until GPU completion.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "The original graph failure is logged before owner cleanup so a secondary shutdown timeout cannot obscure its cause.")]
    private async Task<byte[]?> RunSnapshotAtlasPageResidencyAsync(
        bool renderThroughNativeGraph,
        bool failGraphRecording = false,
        bool cancelGraphAdmission = false,
        GraphicsBackend graphicsBackend = GraphicsBackend.Vulkan,
        bool useM05SpritePipeline = false,
        bool useBuiltInSpriteGraph = false,
        bool useUnsupportedMaterialTexture = false,
        bool usePixelSnapGradient = false,
        float spriteRootPositionX = 0.0f,
        bool pixelSnap = true,
        Vector4? cameraViewport = null,
        Vector4? cameraClearColor = null,
        CameraClearFlags? cameraClearFlags = null,
        bool expectUninitializedTargetRejection = false,
        bool expectUnsupportedHdrClearRange = false,
        bool expectHdrClearColorReadback = false,
        bool activateSecondSceneInstance = false,
        PixelSize? targetExtentOverride = null)
    {
        using var submissionCancellation = new CancellationTokenSource();

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

        if (renderThroughNativeGraph
            && !useM05SpritePipeline
            && !useBuiltInSpriteGraph
            && string.IsNullOrWhiteSpace(shaderFixtureDirectory))
        {
            Assert.Ignore("The M04 shader fixture is required for package-backed Sprite graph submission.");
            return null;
        }

        if ((graphicsBackend == GraphicsBackend.D3D12) && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 Sprite graph requires Windows 11.");
            return null;
        }

        const int TextureAssetId = 1;
        const int AtlasAssetId = 2;
        const int MaterialAssetId = 3;
        const int SceneAssetId = 4;
        var schemas = CreateRendererRegistry();
        var frameId = new Guid("30000000-0000-4000-8000-000000000001");
        var textureRecord = usePixelSnapGradient
            ? CreatePixelSnapGradientAtlasPageTextureRecord()
            : CreateRenderAtlasPageTextureRecord(17);
        var atlasRecord = CreateRenderAtlasRecord(
            TextureAssetId,
            CreateRenderAtlasFrameRecord(frameId, page: 0, x: 4, y: 4, width: 4, height: 4));
        var materialColor = useBuiltInSpriteGraph
            ? new Vector4(0.5f, 1.0f, 0.5f, 1.0f)
            : Vector4.One;
        var materialRecord = CreateRenderMaterialAssetRecord(
            isSprite: true,
            CreateRenderMaterialSpriteParametersRecord(
                textureAssetId: useUnsupportedMaterialTexture ? TextureAssetId : null,
                color: materialColor),
            alphaMode: 2,
            doubleSided: false,
            depthTest: false,
            depthWrite: false,
            cullMode: 0,
            outputAlpha: 1,
            requiredCapabilities: 0x009);
        var sceneBytes = SceneAssetReaderTests.Fixtures.CreateScene(
            new AssetId(SceneAssetId),
            rootPositionX: spriteRootPositionX,
            additionalEntityComponents: [
                CreateCameraRecord(order: 0),
                CreateRenderSpriteRecord(
                    AtlasAssetId,
                    frameId,
                    materialAssetId: MaterialAssetId,
                    pixelSnap: pixelSnap,
                    tint: useBuiltInSpriteGraph
                        ? new Vector4(1.0f, 0.5f, 0.25f, 0.5f)
                        : null,
                    pivot: useBuiltInSpriteGraph ? new Vector2(0.5f, 0.5f) : null),
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
        var graphicsBackendRegistration = graphicsBackend switch {
            GraphicsBackend.D3D12 => D3D12GraphicsBackend.CreateRegistration(),
            GraphicsBackend.Vulkan => VulkanGraphicsBackend.CreateRegistration(),
            _ => throw new ArgumentOutOfRangeException(nameof(graphicsBackend)),
        };
        var hostOptions = new EngineHostOptions(
            HostProfile.OffscreenNative,
            PlatformBackend.Headless,
            graphicsBackend,
            graphicsBackendRegistration,
            new AssetStoreOptions(
                packagePath,
                SHA256.HashData(packageBytes),
                assetRegistrationBuilder.Build())) {
            CpuWorkerCount = 0,
            PersistenceMetadata = WorldPersistenceHeaderWriterTests.CreateMetadata(),
        };
        EngineHost? host = null;
        RenderSnapshot3D? snapshot = null;
        RenderSnapshot3D? repeatedSnapshot = null;
        GpuResidencyLease? canonicalResidencyLease = null;
        GpuResidencyLease? residencyLease = null;
        GpuTexture? targetTexture = null;
        GpuTexture? repeatedBuiltInTargetTexture = null;
        GpuBuffer? vertexBuffer = null;
        GpuBuffer? indexBuffer = null;
        GpuBuffer? frameBuffer = null;
        GpuBuffer? objectDataBuffer = null;
        GpuSampler? sampler = null;
        GpuPipeline? pipeline = null;
        byte[]? builtInGraphReadback = null;

        try
        {
            try
            {
                host = await EngineHost.StartAsync(hostOptions).ConfigureAwait(false);
            }
            catch (DllNotFoundException exception)
            {
                Assert.Ignore($"{graphicsBackend} system library is unavailable: {exception.Message}");
                return null;
            }
            catch (EntryPointNotFoundException exception)
            {
                Assert.Ignore($"{graphicsBackend} system entry point is unavailable: {exception.Message}");
                return null;
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No {graphicsBackend} device satisfies OffscreenNative: {exception.Message}");
                return null;
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
            SceneInstance? secondSceneInstance = null;

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

            if (activateSecondSceneInstance)
            {
                using var secondPrepared = await loader!.PrepareAsync(
                    new AssetId(SceneAssetId),
                    new SceneLoadOptions()).ConfigureAwait(false);
                var secondActivation = await loader.ActivateAsync(secondPrepared)
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(secondActivation.Status, Is.EqualTo(SceneActivationStatus.Activated));
                secondSceneInstance = secondActivation.Instance
                    ?? throw new InvalidOperationException("The second activated Sprite Scene omitted its instance.");
            }

            var activeSceneInstance = sceneInstance
                ?? throw new InvalidOperationException("The activated Sprite Scene omitted its instance.");
            var cameraId = Guid.Empty;
            var targetExtent = targetExtentOverride ?? new PixelSize(32, 32);
            SpatialFrame3 frame3 = default;

            await engineHost.InvokeAsync(_ => {
                var cameraEntity = world!.CreateEntity(new EntityDescription(Guid.NewGuid()));
                var localTransform = SceneComponentDefinitions.CreateAssignedLocalTransform(
                    Vector3.Zero,
                    Quaternion.Identity,
                    Vector3.One);
                var rootPosition = new SceneComponentDefinitions.RootPositionComponent(
                    new SpatialPosition3(default, new Vector3(0.0f, 0.0f, -5.0f)));
                var camera = new SceneComponentDefinitions.CameraComponent(
                    (byte)CameraProjectionKind.Perspective,
                    float.Pi / 3.0f,
                    10.0f,
                    0.1f,
                    (byte)CameraFarMode.Finite,
                    new SceneComponentDefinitions.OptionalSingle(1000.0f),
                    default,
                    cameraViewport ?? new Vector4(0.0f, 0.0f, 1.0f, 1.0f),
                    (byte)(cameraClearFlags ?? (CameraClearFlags.Color | CameraClearFlags.Depth)),
                    cameraClearColor ?? new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
                    ulong.MaxValue,
                    0);
                world.Add(cameraEntity, in localTransform);
                world.Add(cameraEntity, in rootPosition);
                world.Add(cameraEntity, in camera);
                cameraId = world!.GetEntityId(cameraEntity);
                frame3 = world.Step().Frame3;
            }).ConfigureAwait(false);

            await engineHost.InvokeAsync(_ => {
                var extraction = engineHost.Renderer.Extract(
                    world!,
                    new RenderExtractionOptions3D(
                        frame3,
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
            Assert.That(spriteSnapshot.SpriteDraws, Has.Length.EqualTo(activateSecondSceneInstance ? 2 : 1));
            Assert.That(spriteSnapshot.AssetPins, Is.Not.Null);
            canonicalResidencyLease = await engineHost.Renderer.EnsureSpritePageResidencyAsync(
                spriteSnapshot,
                spriteDrawIndex: 0,
                new ResidencyOptions(TextureUsage.Sampled)).ConfigureAwait(false);
            residencyLease = await engineHost.Renderer.EnsureSpritePageResidencyAsync(
                spriteSnapshot,
                spriteDrawIndex: 0,
                new ResidencyOptions(TextureUsage.Sampled | TextureUsage.CopySource)).ConfigureAwait(false);
            Assert.That(residencyLease.Texture, Is.Not.SameAs(canonicalResidencyLease.Texture));
            Assert.That(
                canonicalResidencyLease.Texture.Description.Usage,
                Is.EqualTo(TextureUsage.Sampled | TextureUsage.CopyDestination));
            Assert.That(
                residencyLease.Texture.Description.Usage,
                Is.EqualTo(TextureUsage.Sampled | TextureUsage.CopySource | TextureUsage.CopyDestination));

            using (var repeatedSampledLease = await engineHost.Renderer.EnsureSpritePageResidencyAsync(
                       spriteSnapshot,
                       spriteDrawIndex: 0,
                       new ResidencyOptions(TextureUsage.Sampled)).ConfigureAwait(false))
            {
                Assert.That(repeatedSampledLease.Texture, Is.SameAs(canonicalResidencyLease.Texture));
            }

            if (renderThroughNativeGraph && useBuiltInSpriteGraph)
            {
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
                var selectedTarget = targetTexture
                        ?? throw new InvalidOperationException("The built-in Sprite graph has no output target.");

                if (useUnsupportedMaterialTexture)
                {
                    _ = Assert.Throws<NotSupportedException>(() =>
                        engineHost.Renderer.BuildGraph(spriteSnapshot, selectedTarget));
                }
                else if (expectUninitializedTargetRejection)
                {
                    _ = Assert.Throws<NotSupportedException>(() =>
                        engineHost.Renderer.BuildGraph(spriteSnapshot, selectedTarget));
                }
                else if (expectUnsupportedHdrClearRange)
                {
                    _ = Assert.Throws<NotSupportedException>(() =>
                        engineHost.Renderer.BuildGraph(spriteSnapshot, selectedTarget));
                }
                else
                {
                    var graph = engineHost.Renderer.BuildGraph(spriteSnapshot, selectedTarget);
                    var compileResponse = graph.Compile();
                    Assert.That(compileResponse.Status, Is.EqualTo(RenderGraphCompileStatus.Compiled));
                    var plan = compileResponse.Plan
                        ?? throw new InvalidOperationException("The built-in Sprite graph compile omitted its plan.");

                    if (activateSecondSceneInstance)
                    {
                        var spriteState = plan.BuiltInSpriteState
                            ?? throw new InvalidOperationException("The built-in Sprite graph omitted its state.");
                        Assert.That(spriteState.Passes.Count, Is.EqualTo(2));
                        Assert.That(spriteState.DeferredTextures, Has.Length.EqualTo(1));
                        Assert.That(
                            spriteSnapshot.SpriteDraws[0].SceneInstanceId,
                            Is.Not.EqualTo(spriteSnapshot.SpriteDraws[1].SceneInstanceId));
                    }

                    var spriteObjectBytes = default(byte[]?);

                    foreach (var upload in plan.BuiltInSpriteState?.BufferUploads
                        ?? throw new InvalidOperationException("The built-in Sprite graph omitted prepared buffer data."))
                    {
                        if (upload.Version.Description.DebugName == "sprite-object-data")
                        {
                            spriteObjectBytes = upload.Bytes;
                            break;
                        }
                    }

                    Assert.That(spriteObjectBytes, Is.Not.Null);
                    var preparedSpriteObjectBytes = spriteObjectBytes
                        ?? throw new InvalidOperationException("The built-in Sprite graph omitted Sprite object data.");

                    if (activateSecondSceneInstance)
                    {
                        var spriteState = plan.BuiltInSpriteState
                            ?? throw new InvalidOperationException("The built-in Sprite graph omitted its state.");
                        var spriteObjectStride = 0;

                        foreach (var spritePass in spriteState.Passes.Values)
                        {
                            spriteObjectStride = spritePass.ObjectStride;
                            break;
                        }

                        Assert.Multiple(() => {
                            Assert.That(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(preparedSpriteObjectBytes.AsSpan(spriteObjectStride + 64))), Is.EqualTo(0.5f));
                            Assert.That(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(preparedSpriteObjectBytes.AsSpan(spriteObjectStride + 68))), Is.EqualTo(0.5f));
                            Assert.That(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(preparedSpriteObjectBytes.AsSpan(spriteObjectStride + 72))), Is.EqualTo(0.125f));
                            Assert.That(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(preparedSpriteObjectBytes.AsSpan(spriteObjectStride + 76))), Is.EqualTo(0.5f));
                        });
                    }

                    Assert.Multiple(() => {
                        Assert.That(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(preparedSpriteObjectBytes.AsSpan(64))), Is.EqualTo(0.5f));
                        Assert.That(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(preparedSpriteObjectBytes.AsSpan(68))), Is.EqualTo(0.5f));
                        Assert.That(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(preparedSpriteObjectBytes.AsSpan(72))), Is.EqualTo(0.125f));
                        Assert.That(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(preparedSpriteObjectBytes.AsSpan(76))), Is.EqualTo(0.5f));
                    });
                    var submissionTask = engineHost.Renderer.SubmitAsync(
                        spriteSnapshot,
                        plan,
                        submissionCancellation.Token);
                    spriteSnapshot.Dispose();
                    Assert.That(spriteSnapshot.AssetPins, Is.Null);
                    var submission = await submissionTask.ConfigureAwait(false);
                    Assert.That(submission.CompletedFence, Is.GreaterThan(0));
                    var (atlasPageState, atlasPageContentsValid) =
                        canonicalResidencyLease.Texture.ResourceOwner.GetGraphImportState(
                            new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color));
                    Assert.That(atlasPageContentsValid, Is.True);
                    Assert.That(atlasPageState, Is.EqualTo(GraphResourceState.ShaderSampledRead));
                    repeatedBuiltInTargetTexture = await graphicsDevice.CreateTextureAsync(
                            new TextureDescription(
                                TextureDimension.Dimension2D,
                                targetExtent.Width,
                                targetExtent.Height,
                                depth: 1,
                                arrayLayers: 1,
                                mipLevels: 1,
                                TextureFormat.Rgba8Unorm,
                                TextureUsage.ColorAttachment | TextureUsage.CopySource,
                                "snapshot-atlas-residency-repeat-target")).ConfigureAwait(false);
                    await engineHost.InvokeAsync(_ => {
                        var repeatedExtraction = engineHost.Renderer.Extract(
                            world!,
                            new RenderExtractionOptions3D(
                                frame3,
                                targetExtent,
                                [cameraId]));

                        if (repeatedExtraction.Status != RenderExtractionStatus.Extracted)
                        {
                            throw new InvalidOperationException(
                                $"The repeated Sprite snapshot extraction failed: {string.Join("; ", repeatedExtraction.Diagnostics)}");
                        }

                        repeatedSnapshot = repeatedExtraction.Snapshot as RenderSnapshot3D
                            ?? throw new InvalidOperationException("The successful repeated Sprite extraction omitted its 3D snapshot.");
                    }).ConfigureAwait(false);
                    var selectedRepeatedSnapshot = repeatedSnapshot
                        ?? throw new InvalidOperationException("The host renderer omitted its repeated Sprite snapshot.");
                    var repeatedGraph = engineHost.Renderer.BuildGraph(selectedRepeatedSnapshot, repeatedBuiltInTargetTexture);
                    var repeatedCompile = repeatedGraph.Compile();
                    Assert.That(repeatedCompile.Status, Is.EqualTo(RenderGraphCompileStatus.Compiled));
                    var repeatedPlan = repeatedCompile.Plan
                        ?? throw new InvalidOperationException("The repeated built-in Sprite graph compile omitted its plan.");
                    var repeatedSubmission = await engineHost.Renderer.SubmitAsync(
                        selectedRepeatedSnapshot,
                        repeatedPlan,
                        submissionCancellation.Token).ConfigureAwait(false);
                    Assert.That(repeatedSubmission.CompletedFence, Is.GreaterThan(submission.CompletedFence));
                    selectedRepeatedSnapshot.Dispose();
                    Assert.That(selectedRepeatedSnapshot.AssetPins, Is.Null);
                    repeatedSnapshot = null;

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
                    byte[] expectedClearPixel = expectHdrClearColorReadback
                        ? [170, 85, 0, byte.MaxValue]
                        : [0, 0, 0, byte.MaxValue];
                    Assert.Multiple(() => {
                        if (!usePixelSnapGradient && !expectHdrClearColorReadback)
                        {
                            byte[] expectedCenterPixel = activateSecondSceneInstance
                                ? [8, 8, 2, byte.MaxValue]
                                : [4, 4, 1, byte.MaxValue];

                            for (var channel = 0; channel < expectedCenterPixel.Length; channel++)
                            {
                                Assert.That(
                                    graphReadbackBytes[centerPixelOffset + channel],
                                    Is.EqualTo(expectedCenterPixel[channel]));
                            }
                        }

                        Assert.That(graphReadbackBytes.AsSpan(0, 4).ToArray(), Is.EqualTo(expectedClearPixel));
                    });
                    builtInGraphReadback = graphReadbackBytes;
                }
            }
            else if (renderThroughNativeGraph)
            {
                var vertexElements = new[] {
                    new VertexElementDescription(0, VertexElementFormat.Float3, 0),
                    new VertexElementDescription(1, VertexElementFormat.Float2, 3 * sizeof(float)),
                };
                var vertexLayout = ImmutableArray.Create(vertexElements);
                var shaderBinaryFormat = graphicsBackend == GraphicsBackend.D3D12
                    ? ShaderBinaryFormat.Dxil
                    : ShaderBinaryFormat.SpirV;

                if (useM05SpritePipeline)
                {
                    pipeline = await engineHost.Renderer.CreateSpriteC0PipelineAsync(TextureFormat.Rgba8Unorm)
                        .ConfigureAwait(false);
                }
                else
                {
                    var fixtureDirectory = shaderFixtureDirectory
                        ?? throw new InvalidOperationException("The package-backed Sprite graph has no shader fixture directory.");
                    var shaderExtension = graphicsBackend == GraphicsBackend.D3D12 ? "dxil" : "spv";
                    using var vertexBytecodeStorage = CreateBytecodeStorage(Path.Combine(
                        fixtureDirectory,
                        $"M04OffscreenTriangle.vertex-sprite.{shaderExtension}"));
                    using var pixelBytecodeStorage = CreateBytecodeStorage(Path.Combine(
                        fixtureDirectory,
                        $"M04OffscreenTriangle.pixel-sprite.{shaderExtension}"));
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
                                shaderBinaryFormat,
                                vertexBytecode,
                                vertexReflection),
                            new ShaderBytecodeInput(
                                ShaderStage.Pixel,
                                shaderBinaryFormat,
                                pixelBytecode,
                                pixelReflection),
                        ],
                        vertexLayout,
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
                }

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
                GraphBufferRange vertexRange;

                if (useM05SpritePipeline)
                {
                    vertexBuffer = await engineHost.Renderer.CreateSpriteVertexBufferAsync(spriteSnapshot)
                        .ConfigureAwait(false)
                        ?? throw new InvalidOperationException("The Sprite snapshot has no vertex buffer.");
                    vertexRange = new GraphBufferRange(0, vertexBuffer.ByteLength);
                }
                else
                {
                    var sentinelVertices = new float[checked(spriteVertices.Length * 5)];

                    for (var vertexIndex = 0; vertexIndex < spriteVertices.Length; vertexIndex++)
                    {
                        var vertexOffset = checked(vertexIndex * 5);
                        sentinelVertices[vertexOffset] = 2.0f;
                        sentinelVertices[vertexOffset + 1] = 2.0f;
                    }

                    var vertexUploadBytes = new byte[checked(vertexBytes.Length * 2)];
                    MemoryMarshal.AsBytes(sentinelVertices.AsSpan()).CopyTo(vertexUploadBytes);
                    vertexBytes.CopyTo(vertexUploadBytes.AsSpan(vertexBytes.Length));
                    vertexBuffer = await graphicsDevice.CreateBufferAsync(
                        new BufferDescription(
                            vertexUploadBytes.Length,
                            BufferUsage.Vertex | BufferUsage.CopyDestination,
                            GpuMemoryKind.DeviceLocal,
                            structuredStride: null,
                            "snapshot-atlas-sprite-vertices")).ConfigureAwait(false);
                    var vertexUpload = await graphicsDevice.UploadBufferAsync(vertexBuffer, 0, vertexUploadBytes).ConfigureAwait(false);
                    Assert.That(vertexUpload.Status, Is.EqualTo(UploadStatus.Ready));
                    vertexRange = new GraphBufferRange(vertexBytes.Length, vertexBytes.Length);
                }

                var indexBytes = MemoryMarshal.AsBytes(RenderSpriteGeometry.Indices.AsSpan()).ToArray();
                var indexUploadBytes = new byte[checked(indexBytes.Length * 2)];
                indexBytes.CopyTo(indexUploadBytes.AsSpan(indexBytes.Length));
                indexBuffer = await graphicsDevice.CreateBufferAsync(
                    new BufferDescription(
                        indexUploadBytes.Length,
                        BufferUsage.Index | BufferUsage.CopyDestination,
                        GpuMemoryKind.DeviceLocal,
                        structuredStride: null,
                        "snapshot-atlas-sprite-indices")).ConfigureAwait(false);
                var indexUpload = await graphicsDevice.UploadBufferAsync(indexBuffer, 0, indexUploadBytes).ConfigureAwait(false);
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

                byte[] frameDataBytes = [];

                if (useM05SpritePipeline)
                {
                    frameDataBytes = new byte[256];
                    var frameMatrix = Matrix4x4.Identity;

                    for (var row = 0; row < 4; row++)
                    {
                        for (var column = 0; column < 4; column++)
                        {
                            BinaryPrimitives.WriteInt32LittleEndian(
                                frameDataBytes.AsSpan(((row * 4) + column) * sizeof(float)),
                                BitConverter.SingleToInt32Bits(frameMatrix[row, column]));
                        }
                    }

                    var frameArea = RenderCameraViewport.CreateArea(
                            spriteSnapshot.Cameras[0].Viewport,
                            targetExtent);
                    var frameAreaValues = new float[] { frameArea.X, frameArea.Y, frameArea.Width, frameArea.Height };

                    for (var index = 0; index < frameAreaValues.Length; index++)
                    {
                        BinaryPrimitives.WriteInt32LittleEndian(
                            frameDataBytes.AsSpan(64 + (index * sizeof(float))),
                            BitConverter.SingleToInt32Bits(frameAreaValues[index]));
                    }

                    frameBuffer = await graphicsDevice.CreateBufferAsync(
                        new BufferDescription(
                            frameDataBytes.Length,
                            BufferUsage.Uniform | BufferUsage.CopySource,
                            GpuMemoryKind.Upload,
                            structuredStride: null,
                            "m05-snapshot-sprite-frame-data")).ConfigureAwait(false);
                    await graphicsDevice.WriteUploadBufferAsync(
                        frameBuffer,
                        0,
                        frameDataBytes).ConfigureAwait(false);

                    objectDataBuffer = await engineHost.Renderer.CreateSpriteObjectBufferAsync(spriteSnapshot)
                        .ConfigureAwait(false)
                        ?? throw new InvalidOperationException("The Sprite snapshot has no object-data buffer.");
                }

                var graph = new RenderGraph(new RenderGraphOptions(
                    spriteSnapshot.SnapshotId,
                    spriteSnapshot.WorldGuid,
                    spriteSnapshot.TickIndex,
                    spriteSnapshot.FrameDimension,
                    spriteSnapshot.ProfileId,
                    spriteSnapshot.FrameRevision,
                    spriteSnapshot.CameraIds,
                    spriteSnapshot.TargetExtent,
                    targetTexture));
                var sampleRange = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);
                var sampledPage = graph.ImportTexture(
                    residencyLease.Texture,
                    sampleRange,
                    new GraphImportOptions(
                        GraphResourceState.General,
                        contentsValid: true,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                var graphBufferReads = new List<(GraphBufferVersion Version, GraphBufferRange Range)>();
                var uniformBufferResourceOwners = new List<GraphicsResourceOwner>();
                BindingSet graphBindingSet;

                if (useM05SpritePipeline)
                {
                    var selectedFrameBuffer = frameBuffer
                        ?? throw new InvalidOperationException("The Sprite graph has no Frame uniform buffer.");
                    var frameRange = new GraphBufferRange(0, frameDataBytes.Length);
                    var (frameState, frameContentsValid) = selectedFrameBuffer.ResourceOwner.GetGraphImportState(frameRange);
                    var frameVersion = graph.ImportBuffer(
                        selectedFrameBuffer,
                        frameRange,
                        new GraphImportOptions(
                            frameState,
                            frameContentsValid,
                            GraphImportCompletionPolicy.WaitForLastUse));
                    graphBufferReads.Add((frameVersion, frameRange));
                    uniformBufferResourceOwners.Add(selectedFrameBuffer.ResourceOwner);

                    var selectedObjectDataBuffer = objectDataBuffer
                        ?? throw new InvalidOperationException("The Sprite graph has no ObjectData uniform buffer.");
                    var objectDataRange = new GraphBufferRange(0, selectedObjectDataBuffer.ByteLength);
                    var (objectDataState, objectDataContentsValid) =
                        selectedObjectDataBuffer.ResourceOwner.GetGraphImportState(objectDataRange);
                    var objectDataVersion = graph.ImportBuffer(
                        selectedObjectDataBuffer,
                        objectDataRange,
                        new GraphImportOptions(
                            objectDataState,
                            objectDataContentsValid,
                            GraphImportCompletionPolicy.WaitForLastUse));
                    graphBufferReads.Add((objectDataVersion, objectDataRange));
                    uniformBufferResourceOwners.Add(selectedObjectDataBuffer.ResourceOwner);

                    graphBindingSet = new BindingSet(
                        [
                            new BufferBinding(
                                ShaderBindingGroup.Frame,
                                0,
                                ShaderResourceKind.UniformBuffer,
                                frameVersion,
                                frameRange),
                            new BufferBinding(
                                ShaderBindingGroup.ObjectData,
                                0,
                                ShaderResourceKind.UniformBuffer,
                                objectDataVersion,
                                objectDataRange),
                        ],
                        [
                            new TextureBinding(
                                ShaderBindingGroup.Material,
                                0,
                                ShaderResourceKind.SampledImage,
                                sampledPage,
                                sampleRange),
                        ],
                        [
                            new SamplerBinding(
                                ShaderBindingGroup.Material,
                                0,
                                sampler
                                    ?? throw new InvalidOperationException("The snapshot-atlas graph has no sampler.")),
                        ]);
                }
                else
                {
                    graphBindingSet = new BindingSet(
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
                }

                var vertexImportRange = new GraphBufferRange(0, vertexBuffer.ByteLength);
                var vertexVersion = graph.ImportBuffer(
                    vertexBuffer,
                    vertexImportRange,
                    new GraphImportOptions(
                        GraphResourceState.General,
                        contentsValid: true,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                var vertexBinding = new VertexBufferBinding(0, vertexVersion, vertexRange, 5 * sizeof(float));
                var indexImportRange = new GraphBufferRange(0, indexBuffer.ByteLength);
                var indexVersion = graph.ImportBuffer(
                    indexBuffer,
                    indexImportRange,
                    new GraphImportOptions(
                        GraphResourceState.General,
                        contentsValid: true,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
                var indexRange = new GraphBufferRange(indexBytes.Length, indexBytes.Length);
                var indexBinding = new IndexBufferBinding(indexVersion, indexRange, IndexFormat.UInt16);
                var selectedPipeline = pipeline
                    ?? throw new InvalidOperationException("The snapshot-atlas graph has no graphics pipeline.");
                var vertexResourceOwner = vertexBuffer.ResourceOwner;
                var indexResourceOwner = indexBuffer.ResourceOwner;
                var targetResourceOwner = targetTexture.ResourceOwner;
                var pageResourceOwner = residencyLease.Texture.ResourceOwner;
                var resourcePinsObservedDuringRecording = false;

                void RecordPackageSample(scoped ref CommandRecorder recorder)
                {
                    if (failGraphRecording || cancelGraphAdmission)
                    {
                        resourcePinsObservedDuringRecording = vertexResourceOwner.HasActiveUses
                            && indexResourceOwner.HasActiveUses
                            && targetResourceOwner.HasActiveUses
                            && pageResourceOwner.HasActiveUses;

                        foreach (var resourceOwner in uniformBufferResourceOwners)
                        {
                            resourcePinsObservedDuringRecording &= resourceOwner.HasActiveUses;
                        }
                    }

                    if (cancelGraphAdmission)
                    {
                        submissionCancellation.Cancel();
                    }

                    if (failGraphRecording)
                    {
                        throw new InvalidOperationException("Injected Sprite graph recording failure.");
                    }

                    recorder.SetPipeline(selectedPipeline.Handle);
                    recorder.SetVertexBuffer(vertexBinding);
                    recorder.SetIndexBuffer(indexBinding);
                    recorder.SetBindings(graphBindingSet);
                    recorder.DrawIndexed(new DrawIndexedArguments(RenderSpriteGeometry.Indices.Length, 1, 0, 0, 0));
                }

                var rasterPass = graph.AddRasterPass(
                    new RenderPassDescription(
                        "snapshot-atlas-page-sample",
                        0,
                        [],
                        hasExternalSideEffects: false,
                        renderArea: useM05SpritePipeline
                            ? RenderCameraViewport.CreateArea(spriteSnapshot.Cameras[0].Viewport, targetExtent)
                            : null),
                    RecordPackageSample);
                graph.ReadBuffer(rasterPass, vertexVersion, GraphBufferUsage.VertexRead, vertexRange);
                graph.ReadBuffer(rasterPass, indexVersion, GraphBufferUsage.IndexRead, indexRange);

                foreach (var bufferRead in graphBufferReads)
                {
                    graph.ReadBuffer(rasterPass, bufferRead.Version, GraphBufferUsage.UniformRead, bufferRead.Range);
                }

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
                var submissionTask = engineHost.Renderer.SubmitAsync(
                    spriteSnapshot,
                    plan,
                    submissionCancellation.Token);
                spriteSnapshot.Dispose();
                Assert.That(spriteSnapshot.AssetPins, Is.Null);
                vertexBuffer.Dispose();
                vertexBuffer = null;
                indexBuffer.Dispose();
                indexBuffer = null;
                frameBuffer?.Dispose();
                frameBuffer = null;
                objectDataBuffer?.Dispose();
                objectDataBuffer = null;

                if (failGraphRecording)
                {
                    _ = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                        await submissionTask.ConfigureAwait(false));
                    Assert.Multiple(() => {
                        Assert.That(resourcePinsObservedDuringRecording, Is.True);
                        Assert.That(vertexResourceOwner.HasActiveUses, Is.False);
                        Assert.That(indexResourceOwner.HasActiveUses, Is.False);
                        Assert.That(targetResourceOwner.HasActiveUses, Is.False);
                        Assert.That(pageResourceOwner.HasActiveUses, Is.False);

                        foreach (var resourceOwner in uniformBufferResourceOwners)
                        {
                            Assert.That(resourceOwner.HasActiveUses, Is.False);
                        }
                    });
                }
                else if (cancelGraphAdmission)
                {
                    _ = Assert.ThrowsAsync<OperationCanceledException>(async () =>
                        await submissionTask.ConfigureAwait(false));
                    Assert.Multiple(() => {
                        Assert.That(resourcePinsObservedDuringRecording, Is.True);
                        Assert.That(vertexResourceOwner.HasActiveUses, Is.False);
                        Assert.That(indexResourceOwner.HasActiveUses, Is.False);
                        Assert.That(targetResourceOwner.HasActiveUses, Is.False);
                        Assert.That(pageResourceOwner.HasActiveUses, Is.False);

                        foreach (var resourceOwner in uniformBufferResourceOwners)
                        {
                            Assert.That(resourceOwner.HasActiveUses, Is.False);
                        }
                    });
                }
                else
                {
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
                    byte[] expectedCenterPixel = useM05SpritePipeline
                        ? [17, 8, 4, byte.MaxValue]
                        : [17, 17, 17, 17];
                    Assert.Multiple(() => {
                        for (var channel = 0; channel < expectedCenterPixel.Length; channel++)
                        {
                            Assert.That(
                                graphReadbackBytes[centerPixelOffset + channel],
                                Is.EqualTo(expectedCenterPixel[channel]));
                        }

                        Assert.That(graphReadbackBytes[0], Is.Not.EqualTo(17));
                    });
                }
            }

            if (secondSceneInstance is not null)
            {
                var secondUnload = await secondSceneInstance.UnloadAsync(HierarchyDestroyMode.DestroySubtree)
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(secondUnload.Status, Is.EqualTo(SceneUnloadStatus.Unloaded));
            }

            var unload = await activeSceneInstance.UnloadAsync(HierarchyDestroyMode.DestroySubtree)
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(unload.Status, Is.EqualTo(SceneUnloadStatus.Unloaded));
            spriteSnapshot.Dispose();
            Assert.That(spriteSnapshot.AssetPins, Is.Null);
            snapshot = null;

            var residentTexture = residencyLease.Texture;
            var expectedMip = residencyLease.Asset.Mips[0];
            Assert.That(expectedMip.Pixels[0], Is.EqualTo(usePixelSnapGradient ? 0 : 17));
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
            canonicalResidencyLease.Dispose();
            canonicalResidencyLease = null;
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
            objectDataBuffer?.Dispose();
            frameBuffer?.Dispose();
            targetTexture?.Dispose();
            repeatedBuiltInTargetTexture?.Dispose();
            snapshot?.Dispose();
            repeatedSnapshot?.Dispose();
            canonicalResidencyLease?.Dispose();
            residencyLease?.Dispose();

            if (host is not null)
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }

            File.Delete(packagePath);
        }

        return builtInGraphReadback;
    }
}
