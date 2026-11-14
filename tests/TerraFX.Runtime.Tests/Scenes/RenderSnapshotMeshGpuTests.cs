// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
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
    private static EngineHostOptions CreateMeshC0HostOptions(
        GraphicsBackend graphicsBackend,
        string packagePath,
        byte[] packageBytes,
        CompiledContentSchemaRegistry schemas)
    {
        if ((graphicsBackend == GraphicsBackend.D3D12)
            && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            throw new PlatformNotSupportedException("The D3D12 Mesh graph requires Windows 11.");
        }

        var registrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = SceneSupport.RegisterAssetCodecs(registrationBuilder);
        _ = Renderer.RegisterAssetCodecs(registrationBuilder);
        var backendRegistration = graphicsBackend switch {
            GraphicsBackend.D3D12 => D3D12GraphicsBackend.CreateRegistration(),
            GraphicsBackend.Vulkan => VulkanGraphicsBackend.CreateRegistration(),
            _ => throw new ArgumentOutOfRangeException(nameof(graphicsBackend)),
        };
        return new EngineHostOptions(
            HostProfile.OffscreenNative,
            PlatformBackend.Headless,
            graphicsBackend,
            backendRegistration,
            new AssetStoreOptions(
                packagePath,
                SHA256.HashData(packageBytes),
                registrationBuilder.Build())) {
            CpuWorkerCount = 0,
            PersistenceMetadata = WorldPersistenceHeaderWriterTests.CreateMetadata(),
        };
    }

    private static async Task RunSnapshotBuiltInMeshC0GraphAsync(
        GraphicsBackend graphicsBackend,
        bool unsupportedAlphaMode = false,
        bool unsupportedActiveLight = false,
        bool useBaseColorTexture = false,
        bool useEmissiveTexture = false,
        bool unsupportedTextureSampler = false,
        bool missingTextureUv0 = false)
    {
        if ((graphicsBackend == GraphicsBackend.D3D12) && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 Mesh graph requires Windows 11.");
            return;
        }

        const uint MeshAssetId = 1;
        const uint MaterialAssetId = 2;
        const uint BaseColorTextureAssetId = 4;
        const uint EmissiveTextureAssetId = 5;
        const int SceneAssetId = 3;
        var sceneAssetId = new AssetId(SceneAssetId);
        var materialRecord = CreateRenderMaterialAssetRecord(
            isSprite: false,
            CreateRenderMaterialPbrParametersRecord(
                baseTextureAssetId: useBaseColorTexture ? checked((int)BaseColorTextureAssetId) : null,
                emissiveTextureAssetId: useEmissiveTexture ? checked((int)EmissiveTextureAssetId) : null,
                baseTextureAddressU: unsupportedTextureSampler ? (byte)1 : (byte)0,
                emissiveTextureAddressU: unsupportedTextureSampler ? (byte)1 : (byte)0,
                emissiveFactor: useEmissiveTexture ? Vector3.UnitY : Vector3.Zero),
            alphaMode: (byte)(unsupportedAlphaMode ? 2 : 0),
            doubleSided: true,
            depthTest: true,
            depthWrite: !unsupportedAlphaMode,
            cullMode: 0,
            outputAlpha: unsupportedAlphaMode ? (byte)1 : (byte)0,
            requiredCapabilities: unsupportedAlphaMode ? 0x199UL : 0x191UL);
        byte[][] meshComponents = [CreateMeshComponentRecord(checked((int)MeshAssetId), layerMask: ulong.MaxValue)];

        if (unsupportedActiveLight)
        {
            meshComponents = [.. meshComponents, CreateDirectionalLightRecord(ulong.MaxValue, enabled: true, priority: 0)];
        }

        var sceneRecord = SceneAssetReaderTests.Fixtures.CreateScene(
            sceneAssetId,
            additionalEntityComponents: meshComponents);
        Package.Entry[] packageEntries = [
            new Package.Entry(
                MeshAssetId,
                Package.CreateEnvelope(MeshAssetId, EbinPrimitives.Encode(
                    CreateMeshRecord(
                        MaterialAssetId,
                        skeletonAssetId: null,
                        groupCount: 2,
                        reverseWinding: true,
                        includeUv0: (useBaseColorTexture || useEmissiveTexture) && !missingTextureUv0)))),
            new Package.Entry(
                MaterialAssetId,
                Package.CreateEnvelope(MaterialAssetId, materialRecord)),
            new Package.Entry(
                checked((uint)sceneAssetId.Value),
                Package.CreateEnvelope(checked((uint)sceneAssetId.Value), sceneRecord)),
        ];

        if (useBaseColorTexture)
        {
            packageEntries = [
                .. packageEntries,
                new Package.Entry(
                    BaseColorTextureAssetId,
                    Package.CreateEnvelope(
                        BaseColorTextureAssetId,
                        CreateRenderTextureAssetRecord(
                            1,
                            1,
                            RenderTextureColorSpace.Srgb,
                            RenderTextureAlphaMode.Straight,
                            RenderTextureSemantic.Color,
                            RenderTextureMipPolicy.BaseOnly,
                            CreateRenderTextureMipRecord(0, 1, 1, [byte.MaxValue, 0, 0, byte.MaxValue])))),
            ];
        }

        if (useEmissiveTexture)
        {
            packageEntries = [
                .. packageEntries,
                new Package.Entry(
                    EmissiveTextureAssetId,
                    Package.CreateEnvelope(
                        EmissiveTextureAssetId,
                        CreateRenderTextureAssetRecord(
                            1,
                            1,
                            RenderTextureColorSpace.Srgb,
                            RenderTextureAlphaMode.Straight,
                            RenderTextureSemantic.Color,
                            RenderTextureMipPolicy.BaseOnly,
                            CreateRenderTextureMipRecord(0, 1, 1, [0, byte.MaxValue, 0, byte.MaxValue])))),
            ];
        }

        var packageBytes = Package.CreatePackage(packageEntries);
        var packagePath = Package.WritePackage(packageBytes);
        var schemas = CreateRendererRegistry();
        EngineHost? host = null;
        RenderSnapshot3D? snapshot = null;
        GpuTexture? targetTexture = null;

        try
        {
            try
            {
                host = await EngineHost.StartAsync(
                    CreateMeshC0HostOptions(graphicsBackend, packagePath, packageBytes, schemas)).ConfigureAwait(false);
            }
            catch (DllNotFoundException exception)
            {
                Assert.Ignore($"{graphicsBackend} system library is unavailable: {exception.Message}");
                return;
            }
            catch (EntryPointNotFoundException exception)
            {
                Assert.Ignore($"{graphicsBackend} system entry point is unavailable: {exception.Message}");
                return;
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No {graphicsBackend} device satisfies OffscreenNative: {exception.Message}");
                return;
            }

            var engineHost = host
                ?? throw new InvalidOperationException("Native EngineHost startup returned no host.");
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

            if (missingTextureUv0)
            {
                _ = Assert.ThrowsAsync<InvalidDataException>(
                    () => loader!.PrepareAsync(sceneAssetId, new SceneLoadOptions()));
                return;
            }

            using (var prepared = await loader!.PrepareAsync(
                       sceneAssetId,
                       new SceneLoadOptions()).ConfigureAwait(false))
            {
                var activation = await loader.ActivateAsync(prepared)
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(activation.Status, Is.EqualTo(SceneActivationStatus.Activated));
            }

            var targetExtent = new PixelSize(64, 64);
            var cameraId = Guid.Empty;
            SpatialFrame3 frame3 = default;
            await engineHost.InvokeAsync(_ => {
                var cameraEntity = world!.CreateEntity(new EntityDescription(Guid.NewGuid()));
                var localTransform = SceneComponentDefinitions.CreateAssignedLocalTransform(
                    Vector3.Zero,
                    Quaternion.Identity,
                    Vector3.One);
                var rootPosition = new SceneComponentDefinitions.RootPositionComponent(
                    new SpatialPosition3(default, new Vector3(0.25f, 0.25f, -5.0f)));
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
                    0);
                world.Add(cameraEntity, in localTransform);
                world.Add(cameraEntity, in rootPosition);
                world.Add(cameraEntity, in camera);
                cameraId = world.GetEntityId(cameraEntity);
                frame3 = world.Step().Frame3;
            }).ConfigureAwait(false);

            await engineHost.InvokeAsync(_ => {
                var extraction = engineHost.Renderer.Extract(
                    world!,
                    new RenderExtractionOptions3D(frame3, targetExtent, [cameraId]));

                if (extraction.Status != RenderExtractionStatus.Extracted)
                {
                    throw new InvalidOperationException(
                        $"The Mesh snapshot extraction failed: {string.Join("; ", extraction.Diagnostics)}");
                }

                snapshot = extraction.Snapshot as RenderSnapshot3D
                    ?? throw new InvalidOperationException("The successful Mesh extraction omitted its 3D snapshot.");
            }).ConfigureAwait(false);

            var graphicsDevice = engineHost.GraphicsDevice
                ?? throw new InvalidOperationException("OffscreenNative startup omitted its GraphicsDevice.");
            var meshSnapshot = snapshot
                ?? throw new InvalidOperationException("The host renderer omitted its Mesh snapshot.");
            Assert.That(meshSnapshot.MeshDraws, Has.Length.EqualTo(2));
            Assert.That(meshSnapshot.SpriteDraws, Is.Empty);
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
                    "mesh-c0-readback-target")).ConfigureAwait(false);

            if (unsupportedAlphaMode
                || unsupportedActiveLight
                || unsupportedTextureSampler
                || missingTextureUv0)
            {
                _ = Assert.Throws<NotSupportedException>(() => engineHost.Renderer.BuildGraph(meshSnapshot, targetTexture));
                return;
            }

            var graph = engineHost.Renderer.BuildGraph(meshSnapshot, targetTexture);
            var compileResponse = graph.Compile();
            Assert.That(compileResponse.Status, Is.EqualTo(RenderGraphCompileStatus.Compiled));
            var plan = compileResponse.Plan
                ?? throw new InvalidOperationException("The Mesh graph compile omitted its plan.");
            Assert.That(plan.BuiltInSpriteState?.MeshPasses, Has.Count.EqualTo(2));
            Assert.That(
                plan.BuiltInSpriteState?.MeshDeferredTextures,
                Has.Length.EqualTo((useBaseColorTexture ? 1 : 0) + (useEmissiveTexture ? 1 : 0)));
            var meshVertexBufferCount = 0;
            var meshIndexBufferCount = 0;

            foreach (var transientBuffer in plan.TransientBuffers)
            {
                if (transientBuffer.Description.DebugName.StartsWith("mesh-vertices-", StringComparison.Ordinal))
                {
                    meshVertexBufferCount++;
                }
                else if (transientBuffer.Description.DebugName.StartsWith("mesh-indices-", StringComparison.Ordinal))
                {
                    meshIndexBufferCount++;
                }
            }

            Assert.Multiple(() => {
                Assert.That(meshVertexBufferCount, Is.EqualTo(1));
                Assert.That(meshIndexBufferCount, Is.EqualTo(1));
            });

            var submission = await engineHost.Renderer.SubmitAsync(meshSnapshot, plan).ConfigureAwait(false);
            Assert.That(submission.CompletedFence, Is.GreaterThan(0));
            using var readback = await graphicsDevice.ReadbackTextureAsync(
                targetTexture,
                new TextureSubresource(0, 0, TextureAspect.Color)).ConfigureAwait(false);
            var readbackBytes = new byte[checked((int)readback.ByteLength)];
            using (var readLease = readback.AcquireReadLease())
            {
                readLease.CopyWindow(0, readbackBytes);
            }

            var coloredPixelCount = 0;
            var minimumX = targetExtent.Width;
            var minimumY = targetExtent.Height;
            var maximumX = -1;
            var maximumY = -1;
            var firstColoredPixelOffset = -1;

            for (var y = 0; y < targetExtent.Height; y++)
            {
                for (var x = 0; x < targetExtent.Width; x++)
                {
                    var pixelOffset = checked((int)((readback.RowPitch * y) + (x * 4)));

                    if ((readbackBytes[pixelOffset] == 0)
                        && (readbackBytes[pixelOffset + 1] == 0)
                        && (readbackBytes[pixelOffset + 2] == 0))
                    {
                        continue;
                    }

                    firstColoredPixelOffset = (firstColoredPixelOffset < 0) ? pixelOffset : firstColoredPixelOffset;
                    coloredPixelCount++;
                    minimumX = Math.Min(minimumX, x);
                    minimumY = Math.Min(minimumY, y);
                    maximumX = Math.Max(maximumX, x);
                    maximumY = Math.Max(maximumY, y);
                }
            }

            Assert.Multiple(() => {
                Assert.That(coloredPixelCount, Is.GreaterThan(0), $"Mesh C0 emitted no lit pixels; raster bounds were [{minimumX},{minimumY}]-[{maximumX},{maximumY}].");
                Assert.That(firstColoredPixelOffset, Is.GreaterThanOrEqualTo(0));
                Assert.That(readbackBytes[firstColoredPixelOffset], Is.EqualTo(2));
                Assert.That(
                    readbackBytes[firstColoredPixelOffset + 1],
                    Is.EqualTo(useEmissiveTexture ? 128 : useBaseColorTexture ? 0 : 2));
                Assert.That(readbackBytes[firstColoredPixelOffset + 2], Is.EqualTo(useBaseColorTexture ? 0 : 2));
                Assert.That(readbackBytes[firstColoredPixelOffset + 3], Is.EqualTo(byte.MaxValue));
                Assert.That(readbackBytes.AsSpan(0, 4).ToArray(), Is.EqualTo(new byte[] { 0, 0, 0, byte.MaxValue }));
            });
        }
        finally
        {
            targetTexture?.Dispose();
            snapshot?.Dispose();

            if (host is not null)
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }

            File.Delete(packagePath);
        }
    }

    [Test]
    public Task SnapshotBuiltInMeshC0GraphRejectsActiveLightWithoutShadowPassAsync()
        => RunSnapshotBuiltInMeshC0GraphAsync(GraphicsBackend.Vulkan, unsupportedActiveLight: true);

    [Test]
    public Task SnapshotBuiltInMeshC0GraphRejectsBaseTextureWithoutUv0Async()
        => RunSnapshotBuiltInMeshC0GraphAsync(
            GraphicsBackend.Vulkan,
            useBaseColorTexture: true,
            missingTextureUv0: true);

    [Test]
    public Task SnapshotBuiltInMeshC0GraphRejectsBlendMaterialAsync()
        => RunSnapshotBuiltInMeshC0GraphAsync(GraphicsBackend.Vulkan, unsupportedAlphaMode: true);

    [Test]
    public Task SnapshotBuiltInMeshC0GraphRejectsUnsupportedTextureSamplerAsync()
        => RunSnapshotBuiltInMeshC0GraphAsync(
            GraphicsBackend.Vulkan,
            useBaseColorTexture: true,
            unsupportedTextureSampler: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphRendersBaseAndEmissiveTexturesAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            useBaseColorTexture: true,
            useEmissiveTexture: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphRendersBaseColorTextureAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(graphicsBackend, useBaseColorTexture: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphRendersEmissiveTextureAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(graphicsBackend, useEmissiveTexture: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphRendersUntexturedOpaquePbrAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(graphicsBackend);
}
