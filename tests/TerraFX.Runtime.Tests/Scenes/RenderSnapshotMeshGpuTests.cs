// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Animation;
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
        CompiledContentSchemaRegistry schemas,
        bool includeAnimation)
    {
        if ((graphicsBackend == GraphicsBackend.D3D12)
            && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            throw new PlatformNotSupportedException("The D3D12 Mesh graph requires Windows 11.");
        }

        var registrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = SceneSupport.RegisterAssetCodecs(registrationBuilder);
        _ = Renderer.RegisterAssetCodecs(registrationBuilder);

        if (includeAnimation)
        {
            _ = AnimationSupport.RegisterAssetCodecs(registrationBuilder);
        }

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
        bool unsupportedCullMode = false,
        bool useDirectionalLight = false,
        bool useSecondDirectionalLight = false,
        int spotLightCount = 0,
        bool useShadowCaster = false,
        bool usePointLight = false,
        bool useMultiplePointLights = false,
        bool useAlphaMask = false,
        bool useAlphaBlend = false,
        bool useBaseColorTexture = false,
        bool useEmissiveTexture = false,
        bool usePackedMetalRoughOcclusionTexture = false,
        bool useSkinning = false,
        bool useVertexColor = false,
        bool useNormalMap = false,
        bool unsupportedTextureSampler = false,
        bool missingTextureUv0 = false,
        bool missingTangents = false,
        bool includeSprite = false,
        bool includeSecondCamera = false,
        byte baseColorTextureAlpha = byte.MaxValue,
        float baseColorAlpha = 1.0f,
        float vertexColorAlpha = 1.0f,
        float cameraNear = 0.1f,
        float cameraFar = 1000.0f,
        float cameraPositionX = 0.25f,
        float shadowCasterLocalPositionX = -1.0f,
        string? expectedBuildWarning = null,
        Action<ReadOnlyMemory<byte>, PixelSize, long>? readbackObserver = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(spotLightCount);

        if ((graphicsBackend == GraphicsBackend.D3D12) && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 Mesh graph requires Windows 11.");
            return;
        }

        const uint MeshAssetId = 1;
        const uint MaterialAssetId = 2;
        const uint BaseColorTextureAssetId = 4;
        const uint EmissiveTextureAssetId = 5;
        const uint PackedTextureAssetId = 6;
        const uint NormalTextureAssetId = 7;
        const uint SkeletonAssetId = 8;
        const uint SpriteTextureAssetId = 9;
        const uint SpriteAtlasAssetId = 10;
        const uint SpriteMaterialAssetId = 11;
        const uint ShadowCasterPrefabAssetId = 12;
        const int SceneAssetId = 3;
        var sceneAssetId = new AssetId(SceneAssetId);
        var spriteFrameId = new Guid("80000000-0000-4000-8000-000000000001");
        var shadowCasterEntityId = new Guid("90000000-0000-4000-8000-000000000001");
        var shadowCasterPlacementId = new Guid("90000000-0000-4000-8000-000000000002");
        var materialRecord = CreateRenderMaterialAssetRecord(
            isSprite: false,
            CreateRenderMaterialPbrParametersRecord(
                baseTextureAssetId: useBaseColorTexture ? checked((int)BaseColorTextureAssetId) : null,
                packedTextureAssetId: usePackedMetalRoughOcclusionTexture ? checked((int)PackedTextureAssetId) : null,
                normalTextureAssetId: useNormalMap ? checked((int)NormalTextureAssetId) : null,
                emissiveTextureAssetId: useEmissiveTexture ? checked((int)EmissiveTextureAssetId) : null,
                metallic: usePackedMetalRoughOcclusionTexture ? 0.5f : 0.0f,
                baseTextureAddressU: unsupportedTextureSampler ? (byte)1 : (byte)0,
                packedTextureAddressU: unsupportedTextureSampler ? (byte)1 : (byte)0,
                emissiveTextureAddressU: unsupportedTextureSampler ? (byte)1 : (byte)0,
                emissiveFactor: useAlphaBlend ? new Vector3(2.0f, 0.0f, 0.0f)
                    : useEmissiveTexture ? Vector3.UnitY : Vector3.Zero,
                baseColorAlpha: baseColorAlpha),
            alphaMode: (byte)(useAlphaBlend ? 2 : useAlphaMask ? 1 : 0),
            doubleSided: !unsupportedCullMode && !useNormalMap,
            depthTest: true,
            depthWrite: !useAlphaBlend,
            cullMode: unsupportedCullMode || useNormalMap ? (byte)1 : (byte)0,
            outputAlpha: useAlphaBlend ? (byte)1 : (byte)0,
            requiredCapabilities: 0x191UL
                | (useAlphaMask ? 0x004UL : 0UL)
                | (useAlphaBlend ? 0x008UL : 0UL)
                | (useNormalMap ? 0x040UL : 0UL));
        byte[][] meshComponents = [CreateMeshComponentRecord(checked((int)MeshAssetId), layerMask: ulong.MaxValue)];

        if (useDirectionalLight)
        {
            meshComponents = [.. meshComponents, CreateDirectionalLightRecord(
                ulong.MaxValue,
                enabled: true,
                priority: 0,
                direction: new Vector3(1.0f, 0.0f, 1.0f))];
        }

        if (usePointLight)
        {
            meshComponents = [.. meshComponents, CreatePointLightRecord(ulong.MaxValue)];
        }

        if (includeSprite)
        {
            meshComponents = [.. meshComponents, CreateRenderSpriteRecord(
                checked((int)SpriteAtlasAssetId),
                spriteFrameId,
                checked((int)SpriteMaterialAssetId),
                pixelSnap: false,
                tint: Vector4.One,
                pivot: new Vector2(0.5f, 0.5f))];
        }

        var shadowCasterPrefabRecord = useShadowCaster
            ? SceneAssetReaderTests.Fixtures.CreatePrefab(
                new AssetId(checked((int)ShadowCasterPrefabAssetId)),
                entityId: shadowCasterEntityId,
                additionalEntityComponents: [
                    CreateMeshComponentRecord(checked((int)MeshAssetId), layerMask: ulong.MaxValue),
                ])
            : null;
        var sceneRecord = useShadowCaster
            ? SceneAssetReaderTests.Fixtures.CreateSceneWithChildPlacement(
                sceneAssetId,
                SceneAssetReaderTests.Fixtures.RootEntityId,
                shadowCasterPlacementId,
                SceneAssetReaderTests.Fixtures.CreateRootPose(
                    shadowCasterEntityId,
                    includeRootPosition: false,
                    localPositionX: shadowCasterLocalPositionX,
                    localPositionZ: -1.0f),
                additionalEntityComponents: meshComponents,
                placementPrefabAssetId: new AssetId(checked((int)ShadowCasterPrefabAssetId)))
            : SceneAssetReaderTests.Fixtures.CreateScene(
                sceneAssetId,
                additionalEntityComponents: meshComponents);
        Package.Entry[] packageEntries = [
            new Package.Entry(
                MeshAssetId,
                Package.CreateEnvelope(MeshAssetId, EbinPrimitives.Encode(
                    CreateMeshRecord(
                        MaterialAssetId,
                        skeletonAssetId: useSkinning ? SkeletonAssetId : null,
                        groupCount: 2,
                        reverseWinding: true,
                        includeUv0: (useBaseColorTexture
                            || useEmissiveTexture
                            || usePackedMetalRoughOcclusionTexture
                            || useNormalMap) && !missingTextureUv0,
                        includeTangents: useNormalMap && !missingTangents,
                        positionOffset: usePointLight ? Vector3.UnitZ : null,
                        normalDirection: useNormalMap ? Vector3.UnitZ : null,
                        useMixedJointInfluences: useSkinning,
                        includeColors: useVertexColor,
                        vertexColor: new Vector4(0.5f, 1.0f, 1.0f, vertexColorAlpha))))),
            new Package.Entry(
                MaterialAssetId,
                Package.CreateEnvelope(MaterialAssetId, materialRecord)),
            new Package.Entry(
                checked((uint)sceneAssetId.Value),
                Package.CreateEnvelope(checked((uint)sceneAssetId.Value), sceneRecord)),
        ];

        if (shadowCasterPrefabRecord is not null)
        {
            packageEntries = [
                .. packageEntries,
                new Package.Entry(
                    ShadowCasterPrefabAssetId,
                    Package.CreateEnvelope(ShadowCasterPrefabAssetId, shadowCasterPrefabRecord)),
            ];
        }

        if (includeSprite)
        {
            var spriteMaterial = CreateRenderMaterialAssetRecord(
                isSprite: true,
                CreateRenderMaterialSpriteParametersRecord(color: Vector4.One),
                alphaMode: 2,
                doubleSided: false,
                depthTest: false,
                depthWrite: false,
                cullMode: 0,
                outputAlpha: 1,
                requiredCapabilities: 0x009);
            var spriteAtlas = CreateRenderAtlasRecord(
                checked((int)SpriteTextureAssetId),
                CreateRenderAtlasFrameRecord(spriteFrameId, page: 0, x: 4, y: 4, width: 4, height: 4));
            packageEntries = [
                .. packageEntries,
                new Package.Entry(
                    SpriteTextureAssetId,
                    Package.CreateEnvelope(
                        SpriteTextureAssetId,
                        CreateRenderAtlasPageTextureRecord(17))),
                new Package.Entry(
                    SpriteAtlasAssetId,
                    Package.CreateEnvelope(SpriteAtlasAssetId, spriteAtlas)),
                new Package.Entry(
                    SpriteMaterialAssetId,
                    Package.CreateEnvelope(SpriteMaterialAssetId, spriteMaterial)),
            ];
        }

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
                            CreateRenderTextureMipRecord(0, 1, 1, [byte.MaxValue, 0, 0, baseColorTextureAlpha])))),
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

        if (usePackedMetalRoughOcclusionTexture)
        {
            packageEntries = [
                .. packageEntries,
                new Package.Entry(
                    PackedTextureAssetId,
                    Package.CreateEnvelope(
                        PackedTextureAssetId,
                        CreateRenderTextureAssetRecord(
                            1,
                            1,
                            RenderTextureColorSpace.Linear,
                            RenderTextureAlphaMode.Straight,
                            RenderTextureSemantic.Data,
                            RenderTextureMipPolicy.BaseOnly,
                            CreateRenderTextureMipRecord(0, 1, 1, [byte.MaxValue, byte.MaxValue, 0, byte.MaxValue])))),
            ];
        }

        if (useNormalMap)
        {
            packageEntries = [
                .. packageEntries,
                new Package.Entry(
                    NormalTextureAssetId,
                    Package.CreateEnvelope(
                        NormalTextureAssetId,
                        CreateRenderTextureAssetRecord(
                            1,
                            1,
                            RenderTextureColorSpace.Linear,
                            RenderTextureAlphaMode.Straight,
                            RenderTextureSemantic.Normal,
                            RenderTextureMipPolicy.BaseOnly,
                            CreateRenderTextureMipRecord(0, 1, 1, [128, 128, 0, byte.MaxValue])))),
            ];
        }

        if (useSkinning)
        {
            var jointId = new Guid("70000000-0000-4000-8000-000000000001");
            packageEntries = [
                .. packageEntries,
                new Package.Entry(
                    SkeletonAssetId,
                    Package.CreateEnvelope(
                        SkeletonAssetId,
                        EbinPrimitives.Encode(CreateMeshSkeletonRecord(jointId, jointCount: 2)))),
            ];
        }

        var packageBytes = Package.CreatePackage(packageEntries);
        var packagePath = Package.WritePackage(packageBytes);
        var schemas = CreateRendererRegistry();

        if (useSkinning)
        {
            schemas = new CompiledContentSchemaRegistry([
                .. schemas.Schemas,
                .. AnimationSchemaCatalogue.Schemas,
            ]);
        }

        EngineHost? host = null;
        RenderSnapshot3D? snapshot = null;
        GpuTexture? targetTexture = null;

        try
        {
            try
            {
                host = await EngineHost.StartAsync(
                    CreateMeshC0HostOptions(
                        graphicsBackend,
                        packagePath,
                        packageBytes,
                        schemas,
                        includeAnimation: useSkinning)).ConfigureAwait(false);
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

            if (missingTangents)
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
            var secondaryCameraId = Guid.Empty;
            SpatialFrame3 frame3 = default;
            await engineHost.InvokeAsync(_ => {
                var cameraEntity = world!.CreateEntity(new EntityDescription(Guid.NewGuid()));
                var localTransform = SceneComponentDefinitions.CreateAssignedLocalTransform(
                    Vector3.Zero,
                    Quaternion.Identity,
                    Vector3.One);
                var rootPosition = new SceneComponentDefinitions.RootPositionComponent(
                    new SpatialPosition3(default, new Vector3(cameraPositionX, 0.25f, -5.0f)));
                var camera = new SceneComponentDefinitions.CameraComponent(
                    (byte)CameraProjectionKind.Perspective,
                    float.Pi / 3.0f,
                    10.0f,
                    cameraNear,
                    (byte)CameraFarMode.Finite,
                    new SceneComponentDefinitions.OptionalSingle(cameraFar),
                    default,
                    new Vector4(0.0f, 0.0f, 1.0f, 1.0f),
                    (byte)(CameraClearFlags.Color | CameraClearFlags.Depth),
                    new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
                    ulong.MaxValue,
                    0);
                world.Add(cameraEntity, in localTransform);
                world.Add(cameraEntity, in rootPosition);
                world.Add(cameraEntity, in camera);

                for (var spotIndex = 0; spotIndex < spotLightCount; spotIndex++)
                {
                    var spotEntity = world.CreateEntity(new EntityDescription(Guid.NewGuid()));
                    var spotTransform = SceneComponentDefinitions.CreateAssignedLocalTransform(
                        Vector3.Zero,
                        Quaternion.Identity,
                        Vector3.One);
                    var spotRootPosition = new SceneComponentDefinitions.RootPositionComponent(
                        new SpatialPosition3(default, new Vector3(-2.0f, 0.0f, -5.0f)));
                    var spotLight = new RenderComponentDefinitions.SpotLightComponent(
                        Vector3.One,
                        luminousFlux: 5000.0f,
                        range: 100.0f,
                        direction: new Vector3(2.0f, 0.0f, 5.0f),
                        innerConeAngle: 0.4f,
                        outerConeAngle: 0.7f,
                        layerMask: ulong.MaxValue,
                        enabled: true,
                        priority: spotIndex);
                    world.Add(spotEntity, in spotTransform);
                    world.Add(spotEntity, in spotRootPosition);
                    world.Add(spotEntity, in spotLight);
                }

                if (useSecondDirectionalLight)
                {
                    var secondDirectionalLightEntity = world.CreateEntity(new EntityDescription(Guid.NewGuid()));
                    var secondDirectionalLight = new RenderComponentDefinitions.DirectionalLightComponent(
                        Vector3.One,
                        5000.0f,
                        new Vector3(-1.0f, 0.0f, 1.0f),
                        ulong.MaxValue,
                        enabled: true,
                        priority: 1);
                    world.Add(secondDirectionalLightEntity, in localTransform);
                    world.Add(secondDirectionalLightEntity, in rootPosition);
                    world.Add(secondDirectionalLightEntity, in secondDirectionalLight);
                }

                if (includeSecondCamera)
                {
                    var secondaryCameraEntity = world.CreateEntity(new EntityDescription(Guid.NewGuid()));
                    var secondaryCamera = new SceneComponentDefinitions.CameraComponent(
                        (byte)CameraProjectionKind.Perspective,
                        float.Pi / 3.0f,
                        10.0f,
                        cameraNear,
                        (byte)CameraFarMode.Finite,
                        new SceneComponentDefinitions.OptionalSingle(cameraFar),
                        default,
                        new Vector4(0.0f, 0.0f, 1.0f, 1.0f),
                        (byte)(CameraClearFlags.Color | CameraClearFlags.Depth),
                        new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
                        ulong.MaxValue,
                        1);
                    world.Add(secondaryCameraEntity, in localTransform);
                    world.Add(secondaryCameraEntity, in rootPosition);
                    world.Add(secondaryCameraEntity, in secondaryCamera);
                    secondaryCameraId = world.GetEntityId(secondaryCameraEntity);
                }

                if (useMultiplePointLights)
                {
                    var secondPointLight = new RenderComponentDefinitions.PointLightComponent(
                        new Vector3(0.25f, 0.5f, 1.0f),
                        luminousFlux: 1.0f,
                        range: 12.0f,
                        layerMask: ulong.MaxValue);
                    world.Add(cameraEntity, in secondPointLight);
                }

                cameraId = world.GetEntityId(cameraEntity);
                frame3 = world.Step().Frame3;
            }).ConfigureAwait(false);

            await engineHost.InvokeAsync(_ => {
                ImmutableArray<Guid> cameraIds = includeSecondCamera
                    ? [cameraId, secondaryCameraId]
                    : [cameraId];
                var extraction = engineHost.Renderer.Extract(
                    world!,
                    new RenderExtractionOptions3D(frame3, targetExtent, cameraIds));

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
            var meshDrawCount = useShadowCaster ? 4 : 2;
            Assert.That(meshSnapshot.MeshDraws, Has.Length.EqualTo(meshDrawCount));

            if (useShadowCaster)
            {
                var uniqueMeshPositions = new System.Collections.Generic.HashSet<Vector3>();

                foreach (var draw in meshSnapshot.MeshDraws)
                {
                    if (!SpatialMath.TryToFrameRelative(draw.Position, draw.Frame3, out var relativePosition))
                    {
                        throw new InvalidOperationException("A shadow test Mesh position is outside its frame.");
                    }

                    _ = uniqueMeshPositions.Add(relativePosition);
                }

                Assert.That(uniqueMeshPositions, Has.Count.EqualTo(2));
                Assert.That(uniqueMeshPositions, Does.Contain(Vector3.Zero));
                Assert.That(uniqueMeshPositions, Does.Contain(new Vector3(shadowCasterLocalPositionX, 0.0f, -1.0f)));
            }

            Assert.That(meshSnapshot.SpriteDraws, Has.Length.EqualTo(includeSprite ? 1 : 0));

            if (usePointLight)
            {
                Assert.That(meshSnapshot.PointLights, Has.Length.EqualTo(useMultiplePointLights ? 2 : 1));
            }

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

            if (unsupportedCullMode
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

            if (expectedBuildWarning is not null)
            {
                Assert.That(
                    compileResponse.Diagnostics.Select(static diagnostic => diagnostic.Code),
                    Is.EqualTo([expectedBuildWarning]));
                return;
            }

            var cameraCount = includeSecondCamera ? 2 : 1;
            Assert.That(plan.BuiltInSpriteState?.MeshPasses, Has.Count.EqualTo(meshDrawCount * cameraCount));

            if (useDirectionalLight)
            {
                var builtInState = plan.BuiltInSpriteState
                    ?? throw new InvalidOperationException("The directional-light graph omitted its built-in state.");
                var frameUpload = builtInState.BufferUploads.Single(
                    static upload => StringComparer.Ordinal.Equals(upload.Version.Description.DebugName, "mesh-frame-data"));
                var directionalLightUpload = builtInState.BufferUploads.Single(
                    static upload => upload.Version.Description.DebugName.StartsWith("mesh-directional-lights-", StringComparison.Ordinal));
                Assert.Multiple(() => {
                    Assert.That(
                        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(frameUpload.Bytes.AsSpan(156))),
                        Is.EqualTo(RenderingKernels.DefaultDirectionalShadowDistance));
                    Assert.That(
                        BinaryPrimitives.ReadUInt32LittleEndian(directionalLightUpload.Bytes),
                        Is.EqualTo(1u));
                    Assert.That(
                        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(
                            directionalLightUpload.Bytes.AsSpan(RenderDirectionalLightBufferBuilder.DirectionOffset + 12))),
                        Is.EqualTo(1.0f));
                });
            }

            if (spotLightCount != 0)
            {
                var builtInState = plan.BuiltInSpriteState
                    ?? throw new InvalidOperationException("The spot-light graph omitted its built-in state.");
                var spotLightUpload = builtInState.BufferUploads.Single(
                    static upload => StringComparer.Ordinal.Equals(upload.Version.Description.DebugName, "mesh-spot-lights-0"));
                var spotFrameUpload = builtInState.BufferUploads.Single(
                    static upload => StringComparer.Ordinal.Equals(upload.Version.Description.DebugName, "mesh-frame-data"));
                var spotShadowFrameUpload = builtInState.BufferUploads.Single(
                    static upload => StringComparer.Ordinal.Equals(upload.Version.Description.DebugName, "mesh-shadow-frame-data"));
                var shadowFrameStride = spotShadowFrameUpload.Bytes.Length
                    / (RenderDirectionalShadowCameraData.CascadeCount * 2);
                var firstSpotShadowPass = builtInState.MeshShadowPasses
                    .First(pair => plan.Passes.Single(pass => pass.Id == pair.Key).Description.Name.Contains(".Shadow4.", StringComparison.Ordinal))
                    .Value;
                Assert.Multiple(() => {
                    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(spotLightUpload.Bytes), Is.EqualTo((uint)spotLightCount));

                    for (var spotIndex = 0; spotIndex < spotLightCount; spotIndex++)
                    {
                        var light = meshSnapshot.SpotLights[spotIndex];
                        var firstShadowPriority = Math.Max(0, spotLightCount - RenderSpotShadowData.Capacity);
                        var expectedShadowSlot = light.Priority < firstShadowPriority
                            ? 0
                            : spotLightCount - light.Priority;
                        var shadowSlot = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(
                            spotLightUpload.Bytes.AsSpan(
                                RenderMeshC0SpotLightBufferBuilder.InnerCosineShadowSlotOffset
                                + (spotIndex * 4 * sizeof(float))
                                + sizeof(float))));
                        Assert.That(shadowSlot, Is.EqualTo((float)expectedShadowSlot));
                    }

                    Assert.That(
                        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(
                            spotFrameUpload.Bytes.AsSpan(416))),
                        Is.Not.EqualTo(1.0f));
                    Assert.That(
                        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(
                            spotShadowFrameUpload.Bytes.AsSpan(checked(shadowFrameStride * RenderDirectionalShadowCameraData.CascadeCount)))),
                        Is.Not.EqualTo(1.0f));
                    Assert.That(firstSpotShadowPass.FrameOffset, Is.EqualTo(checked((long)shadowFrameStride * RenderDirectionalShadowCameraData.CascadeCount)));
                    Assert.That(
                        spotFrameUpload.Bytes.AsSpan(416, RenderSpotShadowData.SizeInBytes).ToArray(),
                        Is.EqualTo(spotShadowFrameUpload.Bytes.AsSpan(
                            checked(shadowFrameStride * RenderDirectionalShadowCameraData.CascadeCount),
                            RenderSpotShadowData.SizeInBytes).ToArray()));
                });
            }

            Assert.That(
                plan.BuiltInSpriteState?.MeshShadowPasses,
                (useDirectionalLight || (spotLightCount != 0)) ? Has.Count.GreaterThan(0) : Has.Count.Zero);

            if (useShadowCaster)
            {
                var shadowedDrawIndices = plan.BuiltInSpriteState!.MeshShadowPasses.Values
                    .Select(static pass => pass.DrawIndex)
                    .Distinct()
                    .ToArray();
                Assert.That(shadowedDrawIndices, Has.Length.EqualTo(meshDrawCount));

                if (useDirectionalLight)
                {
                    var cascadeZeroDrawIndices = plan.BuiltInSpriteState.MeshShadowPasses
                        .Where(pair => plan.Passes.Single(pass => pass.Id == pair.Key).Description.Name.Contains(".Shadow0.", StringComparison.Ordinal))
                        .Select(static pair => pair.Value.DrawIndex)
                        .Distinct()
                        .ToArray();
                    Assert.That(cascadeZeroDrawIndices, Has.Length.EqualTo(meshDrawCount));
                }

                if (spotLightCount != 0)
                {
                    var firstSpotDrawIndices = plan.BuiltInSpriteState.MeshShadowPasses
                        .Where(pair => plan.Passes.Single(pass => pass.Id == pair.Key).Description.Name.Contains(".Shadow4.", StringComparison.Ordinal))
                        .Select(static pair => pair.Value.DrawIndex)
                        .Distinct()
                        .ToArray();
                    Assert.That(firstSpotDrawIndices, Has.Length.EqualTo(meshDrawCount));
                }
            }

            Assert.That(plan.BuiltInSpriteState?.Passes, Has.Count.EqualTo(includeSprite ? cameraCount : 0));
            Assert.That(plan.BuiltInSpriteState?.ToneMapPasses, Has.Count.EqualTo(1));

            if (includeSprite)
            {
                var builtInState = plan.BuiltInSpriteState
                    ?? throw new InvalidOperationException("The mixed graph omitted its built-in submission state.");
                var firstMeshPassId = builtInState.MeshPasses.Keys.First();
                var firstSpritePassId = builtInState.Passes.Keys.First();
                var lastSpritePassIndex = -1;
                var toneMapPassId = builtInState.ToneMapPasses.Keys.Single();
                var toneMapPassIndex = plan.OrderedPasses.IndexOf(toneMapPassId);
                var meshColorResourceId = plan.AttachmentWrites
                    .First(write => !write.IsDepth && (write.PassId == firstMeshPassId))
                    .Version.ResourceId;
                var spriteColorResourceId = plan.AttachmentWrites
                    .First(write => !write.IsDepth && (write.PassId == firstSpritePassId))
                    .Version.ResourceId;

                for (var cameraIndex = 0; cameraIndex < cameraCount; cameraIndex++)
                {
                    var meshPassIndices = builtInState.MeshPasses
                        .Where(pass => pass.Value.CameraIndex == cameraIndex)
                        .Select(pass => plan.OrderedPasses.IndexOf(pass.Key))
                        .ToArray();
                    var spritePass = builtInState.Passes
                        .Single(pass => pass.Value.CameraIndex == cameraIndex);
                    var spritePassIndex = plan.OrderedPasses.IndexOf(spritePass.Key);
                    Assert.That(meshPassIndices, Has.Length.EqualTo(2));
                    Assert.That(meshPassIndices.Max(), Is.LessThan(spritePassIndex));
                    lastSpritePassIndex = Math.Max(lastSpritePassIndex, spritePassIndex);

                    if (cameraIndex + 1 < cameraCount)
                    {
                        var nextCameraFirstMeshPassIndex = builtInState.MeshPasses
                            .Where(pass => pass.Value.CameraIndex == cameraIndex + 1)
                            .Select(pass => plan.OrderedPasses.IndexOf(pass.Key))
                            .Min();
                        Assert.That(spritePassIndex, Is.LessThan(nextCameraFirstMeshPassIndex));
                    }
                }

                Assert.Multiple(() => {
                    Assert.That(lastSpritePassIndex, Is.LessThan(toneMapPassIndex));
                    Assert.That(spriteColorResourceId, Is.EqualTo(meshColorResourceId));
                });
            }

            Assert.That(
                plan.BuiltInSpriteState?.MeshDeferredTextures,
                Has.Length.EqualTo(
                    (useBaseColorTexture ? 1 : 0)
                    + (useEmissiveTexture ? 1 : 0)
                    + (usePackedMetalRoughOcclusionTexture ? 1 : 0)
                    + (useNormalMap ? 1 : 0)));
            var meshVertexBufferCount = 0;
            var meshIndexBufferCount = 0;
            var meshBoneBufferCount = 0;

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
                else if (StringComparer.Ordinal.Equals(transientBuffer.Description.DebugName, "mesh-bone-palette-data"))
                {
                    meshBoneBufferCount++;
                    Assert.That(transientBuffer.Description.StructuredStride, Is.EqualTo(RenderMeshC0BoneBufferBuilder.Stride));
                }
            }

            Assert.Multiple(() => {
                Assert.That(meshVertexBufferCount, Is.EqualTo(1));
                Assert.That(meshIndexBufferCount, Is.EqualTo(1));
                Assert.That(meshBoneBufferCount, Is.EqualTo(useSkinning ? 1 : 0));
            });

            if (useSkinning)
            {
                var builtInState = plan.BuiltInSpriteState
                    ?? throw new InvalidOperationException("The Mesh graph omitted its built-in submission state.");
                var objectUpload = builtInState.BufferUploads.Single(
                    static upload => StringComparer.Ordinal.Equals(upload.Version.Description.DebugName, "mesh-object-data"));
                var vertexUpload = builtInState.BufferUploads.Single(
                    static upload => upload.Version.Description.DebugName.StartsWith("mesh-vertices-", StringComparison.Ordinal));
                var bonePaletteUpload = builtInState.BufferUploads.Single(
                    static upload => StringComparer.Ordinal.Equals(upload.Version.Description.DebugName, "mesh-bone-palette-data"));
                Assert.Multiple(() => {
                    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(objectUpload.Bytes.AsSpan(176)), Is.EqualTo(1u));
                    Assert.That(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(vertexUpload.Bytes.AsSpan(48))), Is.EqualTo(useVertexColor ? 0.5f : 1.0f));
                    Assert.That(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(vertexUpload.Bytes.AsSpan(64))), Is.EqualTo(0.0f));
                    Assert.That(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(vertexUpload.Bytes.AsSpan(68))), Is.EqualTo(1.0f));
                    Assert.That(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(vertexUpload.Bytes.AsSpan(72))), Is.EqualTo(1.0f));
                    Assert.That(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(vertexUpload.Bytes.AsSpan(76))), Is.EqualTo(0.0f));
                    Assert.That(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(vertexUpload.Bytes.AsSpan(80))), Is.EqualTo(0.25f));
                    Assert.That(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(vertexUpload.Bytes.AsSpan(92))), Is.EqualTo(0.25f));
                    Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bonePaletteUpload.Bytes), Is.EqualTo(BitConverter.SingleToInt32Bits(1.0f)));
                    Assert.That(meshSnapshot.MeshPalettes[0].SkinMatrices, Has.Length.EqualTo(2));
                });
            }

            RenderSubmissionReceipt submission;

            try
            {
                submission = await engineHost.Renderer.SubmitAsync(meshSnapshot, plan).ConfigureAwait(false);
            }
            catch (NotSupportedException exception) when (
                useSkinning
                && (graphicsBackend is GraphicsBackend.Vulkan)
                && exception.Message.Contains("vertex-stage storage writes", StringComparison.Ordinal))
            {
                Assert.Ignore($"Vulkan device does not advertise vertexPipelineStoresAndAtomics for static skinning: {exception.Message}");
                return;
            }

            Assert.That(submission.CompletedFence, Is.GreaterThan(0));
            using var readback = await graphicsDevice.ReadbackTextureAsync(
                targetTexture,
                new TextureSubresource(0, 0, TextureAspect.Color)).ConfigureAwait(false);
            var readbackBytes = new byte[checked((int)readback.ByteLength)];
            using (var readLease = readback.AcquireReadLease())
            {
                readLease.CopyWindow(0, readbackBytes);
            }

            readbackObserver?.Invoke(readbackBytes, targetExtent, readback.RowPitch);
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

            var alpha = baseColorAlpha
                * (useBaseColorTexture ? (float)baseColorTextureAlpha / byte.MaxValue : 1.0f)
                * (useVertexColor ? vertexColorAlpha : 1.0f);

            if (useAlphaMask && (alpha < 0.5f))
            {
                Assert.That(coloredPixelCount, Is.Zero);
                Assert.That(readbackBytes.AsSpan(0, 4).ToArray(), Is.EqualTo(new byte[] { 0, 0, 0, byte.MaxValue }));
                return;
            }

            if (includeSprite)
            {
                var centerPixelOffset = checked(
                    (int)((readback.RowPitch * (targetExtent.Height / 2)) + (targetExtent.Width / 2 * 4)));
                Assert.Multiple(() => {
                    Assert.That(coloredPixelCount, Is.GreaterThan(0));
                    Assert.That(readbackBytes[centerPixelOffset], Is.GreaterThan(8));
                    Assert.That(readbackBytes[centerPixelOffset + 1], Is.GreaterThan(4));
                    Assert.That(readbackBytes[centerPixelOffset + 2], Is.GreaterThan(2));
                    Assert.That(readbackBytes[centerPixelOffset + 3], Is.EqualTo(byte.MaxValue));
                    Assert.That(readbackBytes.AsSpan(0, 4).ToArray(), Is.EqualTo(new byte[] { 0, 0, 0, byte.MaxValue }));
                });
                return;
            }

            Assert.Multiple(() => {
                Assert.That(coloredPixelCount, Is.GreaterThan(0), $"Mesh C0 emitted no lit pixels; raster bounds were [{minimumX},{minimumY}]-[{maximumX},{maximumY}].");
                Assert.That(firstColoredPixelOffset, Is.GreaterThanOrEqualTo(0));

                if (useDirectionalLight || (spotLightCount != 0))
                {
                    Assert.That(readbackBytes[firstColoredPixelOffset], Is.GreaterThan(100));
                    Assert.That(readbackBytes[firstColoredPixelOffset + 1], Is.GreaterThan(100));
                    Assert.That(readbackBytes[firstColoredPixelOffset + 2], Is.GreaterThan(100));
                }
                else if (usePointLight)
                {
                    Assert.That(readbackBytes[firstColoredPixelOffset], Is.GreaterThan(2));
                    Assert.That(readbackBytes[firstColoredPixelOffset + 1], Is.GreaterThan(readbackBytes[firstColoredPixelOffset]));
                    Assert.That(readbackBytes[firstColoredPixelOffset + 2], Is.GreaterThan(readbackBytes[firstColoredPixelOffset + 1]));
                }
                else
                {
                    Assert.That(
                        readbackBytes[firstColoredPixelOffset],
                        Is.EqualTo(useAlphaBlend
                            ? useBaseColorTexture ? (byte)119 : (byte)153
                            : useVertexColor ? (byte)1 : (byte)2));
                    Assert.That(
                        readbackBytes[firstColoredPixelOffset + 1],
                        Is.EqualTo(useAlphaBlend
                            ? useBaseColorTexture ? 0 : 2
                            : useEmissiveTexture ? 128 : useBaseColorTexture ? 0 : 2));
                    Assert.That(
                        readbackBytes[firstColoredPixelOffset + 2],
                        Is.EqualTo(useBaseColorTexture ? 0 : 2));
                }

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

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphAppliesLinearVertexColorsAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            useBaseColorTexture: true,
            useVertexColor: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphAppliesLinearVertexColorsWithStaticSkinningAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            useBaseColorTexture: true,
            useSkinning: true,
            useVertexColor: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphAppliesUntexturedVertexColorsAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(graphicsBackend, useVertexColor: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphAppliesUntexturedVertexColorsWithStaticSkinningAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            useSkinning: true,
            useVertexColor: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphComposesSpritesBeforeToneMapAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(graphicsBackend, includeSprite: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphDiscardsBaseColorBelowAlphaCutoffAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            useAlphaMask: true,
            useBaseColorTexture: true,
            baseColorTextureAlpha: 127);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphDiscardsUntexturedBaseColorBelowAlphaCutoffAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            useAlphaMask: true,
            baseColorAlpha: 0.49f);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphDiscardsVertexColorAlphaBelowCutoffAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            useAlphaMask: true,
            useVertexColor: true,
            vertexColorAlpha: 0.49f);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphEmissiveRemainsUnmodulatedByVertexColorAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            useEmissiveTexture: true,
            useVertexColor: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphKeepsAlphaMaskAtCutoffAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            useAlphaMask: true,
            useBaseColorTexture: true,
            baseColorTextureAlpha: 128);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphKeepsVertexColorAlphaAtCutoffAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            useAlphaMask: true,
            useVertexColor: true,
            vertexColorAlpha: 0.5f);

    [Test]
    public Task SnapshotBuiltInMeshC0GraphRejectsBaseTextureWithoutUv0Async()
        => RunSnapshotBuiltInMeshC0GraphAsync(
            GraphicsBackend.Vulkan,
            useBaseColorTexture: true,
            missingTextureUv0: true);

    [Test]
    public Task SnapshotBuiltInMeshC0GraphRejectsNormalMapWithoutTangentsAsync()
        => RunSnapshotBuiltInMeshC0GraphAsync(
            GraphicsBackend.Vulkan,
            useNormalMap: true,
            missingTangents: true);

    [Test]
    public Task SnapshotBuiltInMeshC0GraphRejectsSingleSidedMaterialAsync()
        => RunSnapshotBuiltInMeshC0GraphAsync(GraphicsBackend.Vulkan, unsupportedCullMode: true);

    [Test]
    public Task SnapshotBuiltInMeshC0GraphRejectsUnsupportedTextureSamplerAsync()
        => RunSnapshotBuiltInMeshC0GraphAsync(
            GraphicsBackend.Vulkan,
            useBaseColorTexture: true,
            unsupportedTextureSampler: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphRendersAlphaMaskedStaticBindPoseSkinningAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            useAlphaMask: true,
            useSkinning: true);

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

    [TestCase(GraphicsBackend.D3D12, false, false, false)]
    [TestCase(GraphicsBackend.Vulkan, false, false, false)]
    [TestCase(GraphicsBackend.D3D12, false, true, true)]
    [TestCase(GraphicsBackend.Vulkan, false, true, true)]
    [TestCase(GraphicsBackend.D3D12, true, false, false)]
    [TestCase(GraphicsBackend.Vulkan, true, false, false)]
    [TestCase(GraphicsBackend.D3D12, true, true, true)]
    [TestCase(GraphicsBackend.Vulkan, true, true, true)]
    public Task SnapshotBuiltInMeshC0GraphRendersDirectionalShadowsAsync(
        GraphicsBackend graphicsBackend,
        bool useSkinning,
        bool useBaseColorTexture,
        bool useAlphaMask)
        => RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            useDirectionalLight: true,
            useSkinning: useSkinning,
            useBaseColorTexture: useBaseColorTexture,
            useAlphaMask: useAlphaMask);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphRendersEmissiveTextureAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(graphicsBackend, useEmissiveTexture: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphRendersMultiplePointLightsAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            usePointLight: true,
            useMultiplePointLights: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphRendersNormalMapWithPointLightingAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            usePointLight: true,
            useNormalMap: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphRendersNormalMapWithStaticBindPoseSkinningAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            usePointLight: true,
            useSkinning: true,
            useNormalMap: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphRendersPackedMetalRoughOcclusionTextureAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            usePackedMetalRoughOcclusionTexture: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphRendersPremultipliedBlendStaticBindPoseSkinningAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            useAlphaBlend: true,
            useSkinning: true,
            baseColorAlpha: 0.5f);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public async Task SnapshotBuiltInMeshC0GraphRendersShadowOcclusionAsync(GraphicsBackend graphicsBackend)
    {
        byte[] unoccludedReadback = [];
        byte[] occludedReadback = [];
        long rowPitch = 0;

        await RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            useDirectionalLight: true,
            readbackObserver: (bytes, _, pitch) => {
                unoccludedReadback = bytes.ToArray();
                rowPitch = pitch;
            }).ConfigureAwait(false);
        await RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            useDirectionalLight: true,
            useShadowCaster: true,
            readbackObserver: (bytes, _, pitch) => {
                occludedReadback = bytes.ToArray();
                rowPitch = pitch;
            }).ConfigureAwait(false);

        Assert.That(unoccludedReadback, Is.Not.Empty);
        Assert.That(occludedReadback, Is.Not.Empty);
        var darkenedPixels = 0;

        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                var pixelOffset = checked((int)((rowPitch * y) + (x * 4)));
                var darkening = unoccludedReadback[pixelOffset] - occludedReadback[pixelOffset];

                if ((unoccludedReadback[pixelOffset] > 20)
                    && (darkening > 10))
                {
                    darkenedPixels++;
                }
            }
        }

        Assert.That(
            darkenedPixels,
            Is.GreaterThan(0),
            "No shadowed receiver pixels were observed.");
    }

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphRendersSinglePointLightAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(graphicsBackend, usePointLight: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphRendersSpotLightingAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(graphicsBackend, spotLightCount: 1);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public async Task SnapshotBuiltInMeshC0GraphRendersSpotShadowOcclusionAsync(GraphicsBackend graphicsBackend)
    {
        byte[] unoccludedReadback = [];
        byte[] occludedReadback = [];
        long rowPitch = 0;

        await RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            spotLightCount: 1,
            cameraPositionX: 2.5f,
            readbackObserver: (bytes, _, pitch) => {
                unoccludedReadback = bytes.ToArray();
                rowPitch = pitch;
            }).ConfigureAwait(false);
        await RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            spotLightCount: 1,
            useShadowCaster: true,
            shadowCasterLocalPositionX: -0.5f,
            cameraPositionX: 2.5f,
            readbackObserver: (bytes, _, pitch) => {
                occludedReadback = bytes.ToArray();
                rowPitch = pitch;
            }).ConfigureAwait(false);

        Assert.That(unoccludedReadback, Is.Not.Empty);
        Assert.That(occludedReadback, Is.Not.Empty);
        var darkenedPixels = 0;

        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                var pixelOffset = checked((int)((rowPitch * y) + (x * 4)));
                var darkening = unoccludedReadback[pixelOffset] - occludedReadback[pixelOffset];

                if ((unoccludedReadback[pixelOffset] > 20)
                    && (darkening > 10))
                {
                    darkenedPixels++;
                }
            }
        }

        Assert.That(darkenedPixels, Is.GreaterThan(0), "No spot-shadowed receiver pixels were observed.");
    }

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphRendersStaticBindPoseSkinningAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            usePointLight: true,
            useSkinning: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphRendersTexturedAlphaMaskedStaticBindPoseSkinningAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            useAlphaMask: true,
            useBaseColorTexture: true,
            useSkinning: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphRendersTexturedPremultipliedBlendAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            useAlphaBlend: true,
            useBaseColorTexture: true,
            baseColorTextureAlpha: 128,
            baseColorAlpha: 0.5f);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphRendersTexturedPremultipliedBlendStaticBindPoseSkinningAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            useAlphaBlend: true,
            useBaseColorTexture: true,
            useSkinning: true,
            baseColorTextureAlpha: 128,
            baseColorAlpha: 0.5f);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphRendersTexturedStaticBindPoseSkinningAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            useBaseColorTexture: true,
            useSkinning: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphRendersUntexturedOpaquePbrAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(graphicsBackend);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphRendersUntexturedPremultipliedBlendAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            useAlphaBlend: true,
            baseColorAlpha: 0.5f);

    [Test]
    public Task SnapshotBuiltInMeshC0GraphSupportsMixedSecondaryCamerasInCameraOrderAsync()
        => RunSnapshotBuiltInMeshC0GraphAsync(
            GraphicsBackend.Vulkan,
            includeSprite: true,
            includeSecondCamera: true);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphUsesVertexColorAlphaForPremultipliedBlendAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            useAlphaBlend: true,
            useVertexColor: true,
            vertexColorAlpha: 0.5f);

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphWarnsForDirectionalLightBudgetAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            useDirectionalLight: true,
            useSecondDirectionalLight: true,
            expectedBuildWarning: "ShadowBudgetExceeded");

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphWarnsForInvalidDirectionalShadowRangeAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            useDirectionalLight: true,
            cameraNear: 100.0f,
            cameraFar: 101.0f,
            expectedBuildWarning: "ShadowRangeInvalid");

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    public Task SnapshotBuiltInMeshC0GraphWarnsForSpotShadowBudgetAsync(GraphicsBackend graphicsBackend)
        => RunSnapshotBuiltInMeshC0GraphAsync(
            graphicsBackend,
            spotLightCount: 5,
            expectedBuildWarning: "ShadowBudgetExceeded");
}
