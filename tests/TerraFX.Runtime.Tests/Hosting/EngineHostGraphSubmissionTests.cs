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
    [Test]
    public Task D3D12GraphSubmissionRetainsTargetUntilFenceCompletion()
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
            graphicsFrameSlotCount: 2);

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
        int graphicsFrameSlotCount = 3)
    {
        if ((backend == GraphicsBackend.D3D12) && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 offscreen graph path targets Windows 11.");
            return;
        }

        var shaderFixtureDirectory = Environment.GetEnvironmentVariable("TERRAFX_M04_SHADER_FIXTURE_DIRECTORY");

        if ((useUniformBuffer
                || useSampledTexture
                || useStorageResources
                || useCompute
                || useTransientStorage
                || useMultipleComputeStorageBuffers)
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
        var sampleBytes = new byte[] { 32, 128, 224, 255 };
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
        var enteredFenceWaitCount = 0;
        var completedGraphFenceWaitCount = 0;

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

            return result;
        }

        long OverrideCompletedFence(long completedFence)
        {
            return continueFenceWait.IsSet ? completedFence : 0;
        }

        var graphicsRegistration = backend switch {
            GraphicsBackend.D3D12 when holdGraphCompletion => new GraphicsBackendRegistration(
                new D3D12GraphicsBackend.D3D12BackendFactory(
                    graphWaitForSingleObject: WaitForGraphFence,
                    completedFenceValueOverride: OverrideCompletedFence)),
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
        GpuTexture? copySourceTexture = null;
        GpuTexture? copyDestinationTexture = null;
        GpuTexture? secondCopySourceTexture = null;
        GpuTexture? secondCopyDestinationTexture = null;
        TextureDescription? textureCopyDescription = null;
        byte[]? sourceTextureCopyBytes = null;
        byte[]? expectedTextureCopyBytes = null;
        byte[]? expectedSecondTextureCopyBytes = null;
        GpuTexture? sampledTexture = null;
        GpuResidencyLease? sampledTextureResidencyLease = null;
        GpuTexture? storageImage = null;
        GpuBuffer? frameUniformBuffer = null;
        GpuBuffer? storageBuffer = null;
        GpuBuffer? secondStorageBuffer = null;
        GpuBuffer? copySourceBuffer = null;
        GpuBuffer? secondCopySourceBuffer = null;
        GpuBuffer? copyDestinationBuffer = null;
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
                var camera = CreateCameraComponent(new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
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
                    secondCopySourceTexture = await graphicsDevice.CreateTextureAsync(copyTextureDescription).ConfigureAwait(false);
                    secondCopyDestinationTexture = await graphicsDevice.CreateTextureAsync(copyTextureDescription).ConfigureAwait(false);
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
                            new TextureSubresource(1, 1, TextureAspect.Color),
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

                if (useCompute || useTransientStorage || useMultipleComputeStorageBuffers)
                {
                    var computeStage = useMultipleComputeStorageBuffers
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

            if (useStorageResources || useCompute || useMultipleComputeStorageBuffers)
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

            if (useMultipleComputeStorageBuffers)
            {
                secondStorageBuffer = await graphicsDevice.CreateBufferAsync(
                    new BufferDescription(
                        sizeof(uint),
                        BufferUsage.Storage | BufferUsage.CopySource | BufferUsage.CopyDestination,
                        GpuMemoryKind.DeviceLocal,
                        structuredStride: sizeof(uint),
                        debugName: $"m04-{backend}-frame-second-storage-buffer"))
                    .ConfigureAwait(false);
                var secondStorageBufferInitialBytes = new byte[sizeof(uint)];
                var secondStorageBufferUploadReceipt = await graphicsDevice.UploadBufferAsync(
                    secondStorageBuffer,
                    0,
                    secondStorageBufferInitialBytes.AsSpan())
                    .ConfigureAwait(false);
                Assert.That(secondStorageBufferUploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));
            }

            if (useStorageResources || useCompute || useTransientStorage)
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

            if (useBufferCopy || useCompute || useTransientStorage || useMultipleComputeStorageBuffers || useTransientBufferAliasing)
            {
                expectedCopiedBytes = useCompute || useTransientStorage || useMultipleComputeStorageBuffers
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
                    secondCopySourceBuffer = await graphicsDevice.CreateBufferAsync(
                        new BufferDescription(
                            secondSourceBytes.Length,
                            BufferUsage.CopySource | BufferUsage.CopyDestination,
                            GpuMemoryKind.DeviceLocal,
                            structuredStride: null,
                            debugName: $"m04-{backend}-second-copy-source"))
                        .ConfigureAwait(false);
                    var secondSourceUploadReceipt = await graphicsDevice.UploadBufferAsync(
                        secondCopySourceBuffer,
                        0,
                        secondSourceBytes.AsSpan())
                        .ConfigureAwait(false);
                    Assert.That(secondSourceUploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));
                }

                if (copySourceBuffer is GpuBuffer copyUploadSourceBuffer)
                {
                    var sourceBytes = useTransientStorage
                        ? new byte[expectedCopiedBytes.Length]
                        : expectedCopiedBytes;
                    var copyUploadReceipt = await graphicsDevice.UploadBufferAsync(
                        copyUploadSourceBuffer,
                        0,
                        sourceBytes.AsSpan())
                        .ConfigureAwait(false);
                    Assert.That(copyUploadReceipt.Status, Is.EqualTo(UploadStatus.Ready));
                }
            }

            var graph = renderer.BuildGraph(
                renderSnapshot,
                target);
            var sampleRange = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);
            var copyTextureRange = (useTransientTextureCopy || useTransientTextureAliasing || usePartialTextureCopy)
                ? new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color)
                : new GraphTextureRange(1, 1, 1, 1, TextureAspect.Color);
            GraphBufferVersion? frameUniformVersion = null;
            GraphBufferVersion? storageBufferInitialVersion = null;
            GraphBufferVersion? secondStorageBufferInitialVersion = null;
            GraphBufferVersion? storageBufferWriteVersion = null;
            GraphBufferVersion? computeStorageWriteVersion = null;
            GraphBufferVersion? secondComputeStorageWriteVersion = null;
            GraphBufferVersion? copySourceVersion = null;
            GraphBufferVersion? secondCopySourceVersion = null;
            GraphBufferVersion? copyDestinationInitialVersion = null;
            GraphBufferVersion? copyDestinationWriteVersion = null;
            GraphBufferVersion? secondCopyDestinationInitialVersion = null;
            GraphBufferVersion? secondCopyDestinationWriteVersion = null;
            GraphBufferVersion? transientBufferCopyWriteVersion = null;
            GraphBufferVersion? transientSecondBufferCopyWriteVersion = null;
            GraphBufferVersion? transientInitializationSourceVersion = null;
            GraphBufferVersion? transientSecondInitializationSourceVersion = null;
            GraphTextureVersion? sampledTextureVersion = null;
            GraphTextureVersion? copySourceTextureVersion = null;
            GraphTextureVersion? copyDestinationTextureInitialVersion = null;
            GraphTextureVersion? copyDestinationTextureWriteVersion = null;
            GraphTextureVersion? secondCopySourceTextureVersion = null;
            GraphTextureVersion? secondCopyDestinationTextureInitialVersion = null;
            GraphTextureVersion? secondCopyDestinationTextureWriteVersion = null;
            GraphTextureVersion? transientTextureInitialVersion = null;
            GraphTextureVersion? transientTextureWriteVersion = null;
            GraphTextureVersion? secondTransientTextureInitialVersion = null;
            GraphTextureVersion? secondTransientTextureWriteVersion = null;
            GraphTextureVersion? firstTransientTextureDestinationVersion = null;
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

            if (secondStorageBuffer is GpuBuffer selectedSecondStorageBuffer)
            {
                var range = new GraphBufferRange(0, selectedSecondStorageBuffer.ByteLength);
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
                var range = new GraphBufferRange(0, selectedCopyDestinationBuffer.ByteLength);
                copyDestinationInitialVersion = graph.ImportBuffer(
                    selectedCopyDestinationBuffer,
                    range,
                    new GraphImportOptions(
                        GraphResourceState.Undefined,
                        contentsValid: false,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
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
                    sampleRange,
                    new GraphImportOptions(
                        GraphResourceState.General,
                        contentsValid: true,
                        completionPolicy: GraphImportCompletionPolicy.WaitForLastUse));
            }

            GraphBufferVersion? secondFrameUniformVersion = null;
            GraphTextureVersion? secondSampledTextureVersion = null;
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

                if (useMultipleComputeStorageBuffers
                    && (secondCopySourceVersion is GraphBufferVersion selectedSecondSource)
                    && (secondCopyDestinationWriteVersion is GraphBufferVersion selectedSecondDestination))
                {
                    recorder.CopyBuffer(
                        selectedSecondSource,
                        0,
                        selectedSecondDestination,
                        0,
                        selectedSecondSource.Range.ByteLength);
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

            if (useCompute || useTransientStorage || useMultipleComputeStorageBuffers)
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

                if (useMultipleComputeStorageBuffers)
                {
                    var initialStorageBuffer = storageBufferInitialVersion
                        ?? throw new InvalidOperationException("The graph multi-buffer compute test has no first imported storage buffer.");
                    var initialSecondStorageBuffer = secondStorageBufferInitialVersion
                        ?? throw new InvalidOperationException("The graph multi-buffer compute test has no second imported storage buffer.");
                    computeStorageWriteVersion = graph.ReadWriteBuffer(
                        computePass,
                        initialStorageBuffer,
                        initialStorageBuffer.Range);
                    secondComputeStorageWriteVersion = graph.ReadWriteBuffer(
                        computePass,
                        initialSecondStorageBuffer,
                        initialSecondStorageBuffer.Range);
                    copySourceVersion = computeStorageWriteVersion;
                    secondCopySourceVersion = secondComputeStorageWriteVersion;
                    computeBindingSet = new BindingSet(
                        [
                            new BufferBinding(
                                ShaderBindingGroup.Frame,
                                0,
                                ShaderResourceKind.StorageBuffer,
                                computeStorageWriteVersion,
                                initialStorageBuffer.Range),
                            new BufferBinding(
                                ShaderBindingGroup.Frame,
                                2,
                                ShaderResourceKind.StorageBuffer,
                                secondComputeStorageWriteVersion,
                                initialSecondStorageBuffer.Range),
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
                        sampleRange);
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
                                sampleRange),
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
                    graph.ReadBuffer(
                        copyPass,
                        selectedSecondCopySource,
                        GraphBufferUsage.CopySource,
                        selectedSecondCopySource.Range);
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
                renderSnapshot.Cameras[0].ClearColor);

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
                    renderSnapshot.Cameras[0].ClearColor);
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
                var submissionTask = renderer.SubmitAsync(renderSnapshot, plan);
                var secondSubmissionTask = renderer.SubmitAsync(renderSnapshot, plan);

                var twoGraphWaitsStarted = twoFenceWaitsEntered.Wait(TimeSpan.FromSeconds(5));
                Assert.That(
                    twoGraphWaitsStarted,
                    Is.True,
                    $"Graph waiters={Volatile.Read(ref enteredFenceWaitCount)}; first={submissionTask.Status}; "
                    + $"second={secondSubmissionTask.Status}; second exception={secondSubmissionTask.Exception}");
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
                _ = Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await cancelledSubmissionTask.ConfigureAwait(false));

                var submittedTarget = target
                    ?? throw new InvalidOperationException("The delayed graph test has no imported color target.");
                var submittedTargetOwner = submittedTarget.ResourceOwner;
                submittedTarget.Dispose();
                target = null;
                Assert.That(submittedTargetOwner.HasActiveUses, Is.True);
                Assert.That(submittedTargetOwner.IsRetirementRequested, Is.True);

                continueFenceWait.Set();
                receipt = await submissionTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                var queuedReceipt = await secondSubmissionTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(receipt.CompletedFence, Is.GreaterThan(0));
                Assert.That(queuedReceipt.CompletedFence, Is.GreaterThan(0));
                Assert.That(queuedReceipt.CompletedFence, Is.Not.EqualTo(receipt.CompletedFence));
                Assert.That(Volatile.Read(ref completedGraphFenceWaitCount), Is.EqualTo(2));
                Assert.That(submittedTargetOwner.HasActiveUses, Is.False);
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
                var firstPixel = new byte[sizeof(uint)];

                using (var readLease = firstReadback.AcquireReadLease())
                {
                    var copyAction = new ReadbackCopyAction(firstPixel);
                    readLease.UseWindow(0, firstPixel.Length, ref copyAction);
                }

                Assert.Multiple(() => {
                    Assert.That(firstPixel[0], Is.InRange(127, 129));
                    Assert.That(firstPixel[1], Is.InRange(127, 129));
                    Assert.That(firstPixel[2], Is.InRange(127, 129));
                    Assert.That(firstPixel[3], Is.EqualTo(255));
                    Assert.That(renderSnapshot.Cameras[0].ClearColor, Is.EqualTo(new Vector4(0.5f, 0.5f, 0.5f, 1.0f)));
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

                if (useMultipleTextureCopies)
                {
                    var secondCopiedTexture = secondCopyDestinationTexture
                        ?? throw new InvalidOperationException("The graph multi-texture-copy test has no second destination.");
                    var secondExpectedBytes = expectedSecondTextureCopyBytes
                        ?? throw new InvalidOperationException("The graph multi-texture-copy test has no second expected output.");
                    using var secondTextureReadback = await graphicsDevice.ReadbackTextureAsync(
                        secondCopiedTexture,
                        new TextureSubresource(1, 1, TextureAspect.Color))
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
            Assert.That(renderSnapshot.Cameras[0].ClearColor, Is.EqualTo(new Vector4(0.5f, 0.5f, 0.5f, 1.0f)));
            Assert.That(updatedSnapshot.Cameras[0].ClearColor, Is.EqualTo(new Vector4(0.0f, 0.0f, 0.0f, 1.0f)));

            {
                var secondGraph = renderer.BuildGraph(updatedSnapshot, activeTarget);
                var secondBufferBindings = ImmutableArray.CreateBuilder<BufferBinding>(
                    (useUniformBuffer ? 1 : 0) + (useStorageResources ? 1 : 0));
                var secondTextureBindings = ImmutableArray.CreateBuilder<TextureBinding>(
                    (useSampledTexture ? 1 : 0) + (useStorageResources ? 1 : 0));
                var secondSamplerBindings = ImmutableArray.CreateBuilder<SamplerBinding>(useSampledTexture ? 1 : 0);
                GraphBufferVersion? secondGraphStorageBufferInitialVersion = null;
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
                    updatedSnapshot.Cameras[0].ClearColor);
                secondGraph.SetPresentationSource(secondOutputVersion);
                var secondCompileResponse = secondGraph.Compile();
                Assert.That(secondCompileResponse.Status, Is.EqualTo(RenderGraphCompileStatus.Compiled));
                var secondPlan = secondCompileResponse.Plan
                    ?? throw new InvalidOperationException("The second compiled graph response omitted its plan.");
                secondReceipt = await renderer.SubmitAsync(updatedSnapshot, secondPlan).ConfigureAwait(false);
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
            var expectedBackgroundRed = 0;
            var expectedBackgroundGreen = 0;
            var expectedBackgroundBlue = 0;

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

            if (useMultipleComputeStorageBuffers)
            {
                var computeStorageBuffer = storageBuffer
                    ?? throw new InvalidOperationException("The graph multi-buffer compute test has no first storage buffer.");
                using var firstComputeReadback = await graphicsDevice.ReadbackBufferAsync(
                    computeStorageBuffer,
                    0,
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
                    0,
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
                || useTransientBufferAliasing)
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
                + $"StorageResources={useStorageResources}; "
                + $"BufferCopy={useBufferCopy || useCompute || useMultipleComputeStorageBuffers || useTransientBufferAliasing}; "
                + $"TextureCopy={useTextureCopy}; "
                + $"TransientTextureCopy={useTransientTextureCopy}; "
                + $"TransientTextureAliasing={useTransientTextureAliasing}; "
                + $"MultipleTextureCopies={useMultipleTextureCopies}; "
                + $"Compute={useCompute}; "
                + $"MultipleComputeStorageBuffers={useMultipleComputeStorageBuffers}; "
                + $"TransientBufferAliasing={useTransientBufferAliasing}; "
                + $"OverlappingTransientLifetimes={overlapTransientBufferLifetimes}; "
                + $"CameraClear={renderSnapshot.Cameras[0].ClearColor}->{updatedSnapshot.Cameras[0].ClearColor}; "
                + $"Pixel={padded[centerPixelOffset]:X2}{padded[centerPixelOffset + 1]:X2}"
                + $"{padded[centerPixelOffset + 2]:X2}{padded[centerPixelOffset + 3]:X2}; "
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
            storageBuffer?.Dispose();
            secondStorageBuffer?.Dispose();
            copySourceBuffer?.Dispose();
            secondCopySourceBuffer?.Dispose();
            copyDestinationBuffer?.Dispose();
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
