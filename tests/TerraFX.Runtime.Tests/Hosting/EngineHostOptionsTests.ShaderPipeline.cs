// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Buffers;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Content.Tools;
using TerraFX.Graphics;
using TerraFX.Graphics.D3D12;
using TerraFX.Graphics.Vulkan;
using TerraFX.Hosting;
using TerraFX.Numerics;
using TerraFX.Platform;
using TerraFX.Rendering;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Hosting;

internal sealed partial class EngineHostOptionsTests
{
    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the graphics device is shut down",
        Justification = "The created pipeline and shader leases are disposed before the device shutdown boundary.")]
    public void CreatesD3D12PipelineFromCompiledObjectGpuDataShaders()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 backend targets Windows 11.");
            return;
        }

        var testDirectory = TestContext.CurrentContext.TestDirectory;
        var shaderDirectory = Path.Combine(testDirectory, "ShaderArtifacts", "ObjectGpuDataPipeline");
        var manifest = ShaderToolchainManifest.Load(Path.Combine(testDirectory, "ShaderToolchainManifest.json"));
        var vertexReflection = M04ShaderReflectionReader.Read(
            File.ReadAllText(Path.Combine(shaderDirectory, "ObjectGpuData.dxil.reflection.txt")),
            File.ReadAllText(Path.Combine(shaderDirectory, "ObjectGpuData.spirv.reflection.json")),
            File.ReadAllBytes(Path.Combine(shaderDirectory, "ObjectGpuData.vertex.spv")),
            manifest.CompilerIdentity);
        var pixelReflection = M04ShaderReflectionReader.Read(
            File.ReadAllText(Path.Combine(shaderDirectory, "ObjectDataPipelinePixel.dxil.reflection.txt")),
            File.ReadAllText(Path.Combine(shaderDirectory, "ObjectDataPipelinePixel.spirv.reflection.json")),
            File.ReadAllBytes(Path.Combine(shaderDirectory, "ObjectDataPipelinePixel.pixel.spv")),
            manifest.CompilerIdentity);
        Assert.That(vertexReflection.Resources, Has.Length.EqualTo(1));
        Assert.That(vertexReflection.Resources[0].Group, Is.EqualTo(ShaderBindingGroup.ObjectData));
        Assert.That(vertexReflection.Resources[0].Kind, Is.EqualTo(ShaderResourceKind.UniformBuffer));
        Assert.That(vertexReflection.RecordLayouts, Has.Length.EqualTo(1));
        Assert.That(vertexReflection.RecordLayouts[0].Fields, Has.Length.EqualTo(6));
        Assert.That(
            vertexReflection.RecordLayouts[0].Fields.Select(static field => field.Name),
            Is.EqualTo(["World", "PreviousWorld", "NormalRow0", "NormalRow1", "NormalRow2", "Metadata"]));
        Assert.That(
            vertexReflection.RecordLayouts[0].Fields.Select(static field => field.ByteOffset),
            Is.EqualTo([0, 64, 128, 144, 160, 176]));
        Assert.That(vertexReflection.RecordLayouts[0].ByteLength, Is.EqualTo(192));
        var registration = D3D12GraphicsBackend.CreateRegistration();
        var options = new EngineHostOptions(
            HostProfile.OffscreenNative,
            PlatformBackend.Headless,
            GraphicsBackend.D3D12,
            registration,
            CreateAssetStoreOptions()) {
            PersistenceMetadata = CreatePersistenceMetadata(),
        };
        var platformOwner = PlatformHostOwner.Create(options);
        GraphicsDevice? device = null;

        try
        {
            try
            {
                device = GraphicsDevice.CreateAsync(
                    platformOwner.OwnerId,
                    registration,
                    GraphicsDeviceProfile.OffscreenNative,
                    GraphicsPlatformKind.None,
                    CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (DllNotFoundException exception)
            {
                Assert.Ignore($"A required D3D12 system library is unavailable: {exception.Message}");
            }
            catch (EntryPointNotFoundException exception)
            {
                Assert.Ignore($"A required D3D12 system entry point is unavailable: {exception.Message}");
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No available D3D12 device satisfies OffscreenNative: {exception.Message}");
            }

            var graphicsDevice = device
                ?? throw new InvalidOperationException("D3D12 GraphicsDevice startup returned no device.");
            using var vertexBytecodeStorage = CreateBytecodeStorage(Path.Combine(
                shaderDirectory,
                "ObjectGpuData.vertex.dxil"));
            using var pixelBytecodeStorage = CreateBytecodeStorage(Path.Combine(
                shaderDirectory,
                "ObjectDataPipelinePixel.pixel.dxil"));
            using var vertexBytecode = vertexBytecodeStorage.AcquireReadLease(0, vertexBytecodeStorage.Length);
            using var pixelBytecode = pixelBytecodeStorage.AcquireReadLease(0, pixelBytecodeStorage.Length);
            using var pipeline = graphicsDevice.CreatePipelineAsync(
                CreateD3D12ResourcePipelineDescription(
                    vertexBytecode,
                    pixelBytecode,
                    vertexReflection,
                    pixelReflection),
                CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(pipeline.Handle.IsValid, Is.True);
            Assert.That(pipeline.Handle.OwnerId, Is.EqualTo(platformOwner.OwnerId));
            Assert.That(pipeline.Handle.DeviceEpoch, Is.EqualTo(graphicsDevice.DeviceEpoch));
        }
        finally
        {
            try
            {
                if (device is not null)
                {
                    device.ShutdownAsync().GetAwaiter().GetResult();
                    Assert.That(device.Status, Is.EqualTo(GraphicsDeviceStatus.Disposed));
                }
            }
            finally
            {
                platformOwner.DisposeFromOwner();
            }
        }
    }

    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the graphics device is shut down",
        Justification = "The created pipeline and shader leases are disposed before the device shutdown boundary.")]
    public void CreatesVulkanPipelineFromCompiledObjectGpuDataShaders()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("The M05 test fixture uses the Windows-only pinned DXC package.");
            return;
        }

        var testDirectory = TestContext.CurrentContext.TestDirectory;
        var shaderDirectory = Path.Combine(testDirectory, "ShaderArtifacts", "ObjectGpuDataPipeline");
        var manifest = ShaderToolchainManifest.Load(Path.Combine(testDirectory, "ShaderToolchainManifest.json"));
        var vertexReflection = M04ShaderReflectionReader.Read(
            File.ReadAllText(Path.Combine(shaderDirectory, "ObjectGpuData.dxil.reflection.txt")),
            File.ReadAllText(Path.Combine(shaderDirectory, "ObjectGpuData.spirv.reflection.json")),
            File.ReadAllBytes(Path.Combine(shaderDirectory, "ObjectGpuData.vertex.spv")),
            manifest.CompilerIdentity);
        var pixelReflection = M04ShaderReflectionReader.Read(
            File.ReadAllText(Path.Combine(shaderDirectory, "ObjectDataPipelinePixel.dxil.reflection.txt")),
            File.ReadAllText(Path.Combine(shaderDirectory, "ObjectDataPipelinePixel.spirv.reflection.json")),
            File.ReadAllBytes(Path.Combine(shaderDirectory, "ObjectDataPipelinePixel.pixel.spv")),
            manifest.CompilerIdentity);
        Assert.That(
            vertexReflection.Resources,
            Is.EqualTo([
                new ShaderResourceSignature(
                    ShaderBindingGroup.ObjectData,
                    0,
                    ShaderResourceKind.UniformBuffer,
                    1,
                    ShaderResourceAccess.Read),
            ]));
        var registration = VulkanGraphicsBackend.CreateRegistration();
        var options = new EngineHostOptions(
            HostProfile.OffscreenNative,
            PlatformBackend.Headless,
            GraphicsBackend.Vulkan,
            registration,
            CreateAssetStoreOptions()) {
            PersistenceMetadata = CreatePersistenceMetadata(),
        };
        var platformOwner = PlatformHostOwner.Create(options);
        GraphicsDevice? device = null;

        try
        {
            try
            {
                device = GraphicsDevice.CreateAsync(
                    platformOwner.OwnerId,
                    registration,
                    GraphicsDeviceProfile.OffscreenNative,
                    GraphicsPlatformKind.None,
                    CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (DllNotFoundException exception)
            {
                Assert.Ignore($"No Vulkan loader is installed: {exception.Message}");
            }
            catch (EntryPointNotFoundException exception)
            {
                Assert.Ignore($"The installed Vulkan loader lacks a required entry point: {exception.Message}");
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No available Vulkan device satisfies OffscreenNative: {exception.Message}");
            }

            var graphicsDevice = device
                ?? throw new InvalidOperationException("Vulkan GraphicsDevice startup returned no device.");
            using var vertexBytecodeStorage = CreateBytecodeStorage(Path.Combine(
                shaderDirectory,
                "ObjectGpuData.vertex.spv"));
            using var pixelBytecodeStorage = CreateBytecodeStorage(Path.Combine(
                shaderDirectory,
                "ObjectDataPipelinePixel.pixel.spv"));
            using var vertexBytecode = vertexBytecodeStorage.AcquireReadLease(0, vertexBytecodeStorage.Length);
            using var pixelBytecode = pixelBytecodeStorage.AcquireReadLease(0, pixelBytecodeStorage.Length);
            var vertexShader = new ShaderBytecodeInput(
                ShaderStage.Vertex,
                ShaderBinaryFormat.SpirV,
                vertexBytecode,
                vertexReflection);
            var pixelShader = new ShaderBytecodeInput(
                ShaderStage.Pixel,
                ShaderBinaryFormat.SpirV,
                pixelBytecode,
                pixelReflection);
            using var pipeline = graphicsDevice.CreatePipelineAsync(
                new GraphicsPipelineDescription(
                    [vertexShader, pixelShader],
                    [],
                    vertexStride: 0,
                    [TextureFormat.Rgba8Unorm],
                    depthTargetFormat: null,
                    PrimitiveTopology.TriangleList,
                    PipelineBlendMode.Opaque,
                    PipelineDepthMode.Disabled,
                    PipelineCullMode.None,
                    frontFaceClockwise: false,
                    bindingLayoutVersion: 1),
                CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(pipeline.Handle.IsValid, Is.True);
            Assert.That(pipeline.Handle.OwnerId, Is.EqualTo(platformOwner.OwnerId));
            Assert.That(pipeline.Handle.DeviceEpoch, Is.EqualTo(graphicsDevice.DeviceEpoch));
        }
        finally
        {
            try
            {
                if (device is not null)
                {
                    device.ShutdownAsync().GetAwaiter().GetResult();
                    Assert.That(device.Status, Is.EqualTo(GraphicsDeviceStatus.Disposed));
                }
            }
            finally
            {
                platformOwner.DisposeFromOwner();
            }
        }
    }

    [TestCase(GraphicsBackend.D3D12)]
    [TestCase(GraphicsBackend.Vulkan)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before their owners are disposed",
        Justification = "The World snapshot and Graphics resources are disposed before the device and platform owners shut down.")]
    public void SubmitsSnapshotBoundRasterPassWithSemanticGroupsAndReadsBackPixels(GraphicsBackend graphicsBackend)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("The M05 shader fixture uses the Windows-only pinned DXC package.");
            return;
        }

        if ((graphicsBackend == GraphicsBackend.D3D12)
            && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 graph witness requires Windows 11.");
            return;
        }

        var testDirectory = TestContext.CurrentContext.TestDirectory;
        var shaderDirectory = Path.Combine(testDirectory, "ShaderArtifacts", "ObjectGpuDataPipeline");
        var manifest = ShaderToolchainManifest.Load(Path.Combine(testDirectory, "ShaderToolchainManifest.json"));
        var shaderExtension = graphicsBackend == GraphicsBackend.D3D12 ? "dxil" : "spv";
        var shaderBinaryFormat = graphicsBackend == GraphicsBackend.D3D12
            ? ShaderBinaryFormat.Dxil
            : ShaderBinaryFormat.SpirV;
        var vertexReflection = M04ShaderReflectionReader.Read(
            File.ReadAllText(Path.Combine(shaderDirectory, "SnapshotGraphVertex.dxil.reflection.txt")),
            File.ReadAllText(Path.Combine(shaderDirectory, "SnapshotGraphVertex.spirv.reflection.json")),
            File.ReadAllBytes(Path.Combine(shaderDirectory, "SnapshotGraphVertex.vertex.spv")),
            manifest.CompilerIdentity);
        var pixelReflection = M04ShaderReflectionReader.Read(
            File.ReadAllText(Path.Combine(shaderDirectory, "SnapshotGraphPixel.dxil.reflection.txt")),
            File.ReadAllText(Path.Combine(shaderDirectory, "SnapshotGraphPixel.spirv.reflection.json")),
            File.ReadAllBytes(Path.Combine(shaderDirectory, "SnapshotGraphPixel.pixel.spv")),
            manifest.CompilerIdentity);
        Assert.That(
            vertexReflection.Resources,
            Does.Contain(new ShaderResourceSignature(
                ShaderBindingGroup.ObjectData,
                0,
                ShaderResourceKind.UniformBuffer,
                1)));
        Assert.That(
            pixelReflection.Resources,
            Does.Contain(new ShaderResourceSignature(
                ShaderBindingGroup.Frame,
                0,
                ShaderResourceKind.UniformBuffer,
                1)));
        Assert.That(
            pixelReflection.Resources,
            Does.Contain(new ShaderResourceSignature(
                ShaderBindingGroup.Material,
                0,
                ShaderResourceKind.SampledImage,
                1)));
        Assert.That(
            pixelReflection.Resources,
            Does.Contain(new ShaderResourceSignature(
                ShaderBindingGroup.Material,
                0,
                ShaderResourceKind.Sampler,
                1)));
        var registration = graphicsBackend switch {
            GraphicsBackend.D3D12 => D3D12GraphicsBackend.CreateRegistration(),
            GraphicsBackend.Vulkan => VulkanGraphicsBackend.CreateRegistration(),
            _ => throw new ArgumentOutOfRangeException(nameof(graphicsBackend)),
        };
        var options = new EngineHostOptions(
            HostProfile.OffscreenNative,
            PlatformBackend.Headless,
            graphicsBackend,
            registration,
            CreateAssetStoreOptions()) {
            CpuWorkerCount = 0,
            PersistenceMetadata = CreatePersistenceMetadata(),
        };
        var platformOwner = PlatformHostOwner.Create(options);
        GraphicsDevice? device = null;
        GpuResidencyService? residencyService = null;

        try
        {
            try
            {
                device = GraphicsDevice.CreateAsync(
                    platformOwner.OwnerId,
                    registration,
                    GraphicsDeviceProfile.OffscreenNative,
                    GraphicsPlatformKind.None,
                    CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (DllNotFoundException exception)
            {
                Assert.Ignore($"A required {graphicsBackend} loader is unavailable: {exception.Message}");
            }
            catch (EntryPointNotFoundException exception)
            {
                Assert.Ignore($"The installed {graphicsBackend} loader lacks a required entry point: {exception.Message}");
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No available {graphicsBackend} device satisfies OffscreenNative: {exception.Message}");
            }

            var graphicsDevice = device
                ?? throw new InvalidOperationException($"{graphicsBackend} GraphicsDevice startup returned no device.");
            residencyService = new GpuResidencyService(graphicsDevice);
            var renderer = Renderer.Create(graphicsDevice, residencyService);
            var schemas = new CompiledContentSchemaRegistry([
                .. CoreContentSchemaCatalogue.PrimitiveSchemas,
                    .. CoreContentSchemaCatalogue.FoundationSchemas,
                    .. NativeAssetSchemaCatalogue.Schemas,
                    .. SceneSchemaCatalogue.Schemas,
                    .. RenderingSchemaCatalogue.Schemas,
                ]);
            var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
            _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
            _ = worldRegistrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration());
            using var world = World.Create(new WorldOptions(worldRegistrationBuilder.Build()) {
                CpuWorkerCount = 0,
                InitialReplayLineageId = Guid.NewGuid(),
                WorldGuid = Guid.NewGuid(),
            });
            var cameraId = Guid.Parse("60000000-0000-4000-8000-000000000001");
            var cameraEntity = world.CreateEntity(new EntityDescription(cameraId));
            var cameraTransform = SceneComponentDefinitions.CreateAssignedLocalTransform(
                Vector3.Zero,
                Quaternion.Identity,
                Vector3.One);
            var cameraRootPosition = new SceneComponentDefinitions.RootPositionComponent(
                new SpatialPosition3(default, new Vector3(0, 0, -5)));
            var camera = new SceneComponentDefinitions.CameraComponent(
                (byte)CameraProjectionKind.Perspective,
                float.Pi / 3.0f,
                1.0f,
                0.01f,
                (byte)CameraFarMode.Finite,
                new SceneComponentDefinitions.OptionalSingle(100.0f),
                default,
                new Vector4(0, 0, 1, 1),
                (byte)(CameraClearFlags.Color | CameraClearFlags.Depth),
                new Vector4(0, 0, 0, 1),
                ulong.MaxValue,
                0);
            world.Add(cameraEntity, in cameraTransform);
            world.Add(cameraEntity, in cameraRootPosition);
            world.Add(cameraEntity, in camera);
            _ = world.Step();

            var targetExtent = new PixelSize(16, 16);
            var extraction = renderer.Extract(
                world,
                new RenderExtractionOptions3D(
                    world.CurrentFrame3,
                    targetExtent,
                    [cameraId]));
            Assert.That(extraction.Status, Is.EqualTo(RenderExtractionStatus.Extracted));
            using var snapshot = extraction.Snapshot as RenderSnapshot3D
                ?? throw new InvalidOperationException("The committed graph World omitted its 3D snapshot.");
            using var vertexBytecodeStorage = CreateBytecodeStorage(Path.Combine(
                shaderDirectory,
                $"SnapshotGraphVertex.vertex.{shaderExtension}"));
            using var pixelBytecodeStorage = CreateBytecodeStorage(Path.Combine(
                shaderDirectory,
                $"SnapshotGraphPixel.pixel.{shaderExtension}"));
            using var vertexBytecode = vertexBytecodeStorage.AcquireReadLease(0, vertexBytecodeStorage.Length);
            using var pixelBytecode = pixelBytecodeStorage.AcquireReadLease(0, pixelBytecodeStorage.Length);
            var vertexShader = new ShaderBytecodeInput(
                ShaderStage.Vertex,
                shaderBinaryFormat,
                vertexBytecode,
                vertexReflection);
            var pixelShader = new ShaderBytecodeInput(
                ShaderStage.Pixel,
                shaderBinaryFormat,
                pixelBytecode,
                pixelReflection);
            using var pipeline = graphicsDevice.CreatePipelineAsync(
                new GraphicsPipelineDescription(
                    [vertexShader, pixelShader],
                    [],
                    vertexStride: 0,
                    [TextureFormat.Rgba8Unorm],
                    depthTargetFormat: null,
                    PrimitiveTopology.TriangleList,
                    PipelineBlendMode.Opaque,
                    PipelineDepthMode.Disabled,
                    PipelineCullMode.None,
                    frontFaceClockwise: false,
                    bindingLayoutVersion: 1),
                CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            var uniformBytes = new byte[256];
            BinaryPrimitives.WriteInt32LittleEndian(uniformBytes, BitConverter.SingleToInt32Bits(1.0f));
            BinaryPrimitives.WriteInt32LittleEndian(uniformBytes.AsSpan(sizeof(float)), BitConverter.SingleToInt32Bits(0.0f));
            BinaryPrimitives.WriteInt32LittleEndian(uniformBytes.AsSpan(2 * sizeof(float)), BitConverter.SingleToInt32Bits(1.0f));
            BinaryPrimitives.WriteInt32LittleEndian(uniformBytes.AsSpan(3 * sizeof(float)), BitConverter.SingleToInt32Bits(1.0f));
            using var uniformBuffer = graphicsDevice.CreateBufferAsync(
                new BufferDescription(
                    uniformBytes.Length,
                    BufferUsage.Uniform | BufferUsage.CopySource,
                    GpuMemoryKind.Upload,
                    structuredStride: null,
                    "m05-snapshot-graph-frame-data"))
                .GetAwaiter()
                .GetResult();
            graphicsDevice.WriteUploadBufferAsync(uniformBuffer, 0, uniformBytes)
                .GetAwaiter()
                .GetResult();
            var objectDataBytes = new byte[256];

            for (var index = 0; index < 4; index++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    objectDataBytes.AsSpan(((index * 4) + index) * sizeof(float)),
                    BitConverter.SingleToInt32Bits(1.0f));
            }

            using var objectDataBuffer = graphicsDevice.CreateBufferAsync(
                new BufferDescription(
                    objectDataBytes.Length,
                    BufferUsage.Uniform | BufferUsage.CopySource,
                    GpuMemoryKind.Upload,
                    structuredStride: null,
                    "m05-snapshot-graph-object-data"))
                .GetAwaiter()
                .GetResult();
            graphicsDevice.WriteUploadBufferAsync(objectDataBuffer, 0, objectDataBytes)
                .GetAwaiter()
                .GetResult();
            using var sampledTexture = graphicsDevice.CreateTextureAsync(
                new TextureDescription(
                    TextureDimension.Dimension2D,
                    1,
                    1,
                    depth: 1,
                    arrayLayers: 1,
                    mipLevels: 1,
                    TextureFormat.Rgba8Unorm,
                    TextureUsage.Sampled | TextureUsage.CopyDestination,
                    "m05-snapshot-graph-material-texture"))
                .GetAwaiter()
                .GetResult();
            byte[] sampledPixel = [byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue];
            var sampledPixelStorage = UnmanagedBuffer<byte>.Allocate(
                sampledPixel.Length,
                new BufferAllocationOptions(sampledPixel.Length, alignment: 1));
            sampledPixelStorage.CopyFrom(sampledPixel.AsSpan(), 0);

            using (var ownedSampledPixel = sampledPixelStorage.TransferToOwnedBuffer())
            using (var sampledPixelLease = ownedSampledPixel.AcquireReadLease(0, sampledPixel.Length))
            {
                var upload = graphicsDevice.UploadTextureAsync(
                    sampledTexture,
                    new TextureSubresource(0, 0, TextureAspect.Color),
                    sampledPixelLease,
                    0,
                    new TextureDataLayout(sampledPixel.Length, sampledPixel.Length))
                    .GetAwaiter()
                    .GetResult();
                Assert.That(upload.Status, Is.EqualTo(UploadStatus.Ready));
            }

            using var sampler = graphicsDevice.CreateSamplerAsync(
                new SamplerDescription(
                    SamplerFilter.Linear,
                    SamplerFilter.Linear,
                    SamplerFilter.None,
                    SamplerAddressMode.Clamp,
                    SamplerAddressMode.Clamp,
                    SamplerAddressMode.Clamp,
                    maximumAnisotropy: 1.0f))
                .GetAwaiter()
                .GetResult();

            using var target = graphicsDevice.CreateTextureAsync(
                new TextureDescription(
                    TextureDimension.Dimension2D,
                    targetExtent.Width,
                    targetExtent.Height,
                    depth: 1,
                    arrayLayers: 1,
                    mipLevels: 1,
                    TextureFormat.Rgba8Unorm,
                    TextureUsage.ColorAttachment | TextureUsage.CopySource,
                    "m05-object-gpu-data-readback"))
                .GetAwaiter()
                .GetResult();
            var graph = renderer.BuildGraph(snapshot, target);
            var uniformRange = new GraphBufferRange(0, uniformBytes.Length);
            var (uniformState, uniformContentsValid) = uniformBuffer.ResourceOwner.GetGraphImportState(uniformRange);
            var uniformVersion = graph.ImportBuffer(
                uniformBuffer,
                uniformRange,
                new GraphImportOptions(
                    uniformState,
                    uniformContentsValid,
                    GraphImportCompletionPolicy.WaitForLastUse));
            var objectDataRange = new GraphBufferRange(0, objectDataBytes.Length);
            var (objectDataState, objectDataContentsValid) = objectDataBuffer.ResourceOwner.GetGraphImportState(objectDataRange);
            var objectDataVersion = graph.ImportBuffer(
                objectDataBuffer,
                objectDataRange,
                new GraphImportOptions(
                        objectDataState,
                        objectDataContentsValid,
                        GraphImportCompletionPolicy.WaitForLastUse));
            var sampledTextureRange = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);
            var (sampledTextureState, sampledTextureContentsValid) = sampledTexture.ResourceOwner.GetGraphImportState(sampledTextureRange);
            var sampledTextureVersion = graph.ImportTexture(
                sampledTexture,
                sampledTextureRange,
                new GraphImportOptions(
                        sampledTextureState,
                        sampledTextureContentsValid,
                        GraphImportCompletionPolicy.WaitForLastUse));
            var outputRange = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);
            void RecordObjectDraw(scoped ref CommandRecorder recorder)
            {
                recorder.SetPipeline(pipeline.Handle);
                recorder.SetBindings(new BindingSet(
                    [
                        new BufferBinding(
                            ShaderBindingGroup.Frame,
                            0,
                            ShaderResourceKind.UniformBuffer,
                            uniformVersion,
                            uniformRange),
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
                                sampledTextureVersion,
                                sampledTextureRange),
                        ],
                        [
                            new SamplerBinding(
                                ShaderBindingGroup.Material,
                                0,
                                sampler),
                        ]));
                recorder.Draw(new DrawArguments(3, 1, 0, 0));
            }

            var pass = graph.AddRasterPass(
                new RenderPassDescription("m05-snapshot-raster-pass", 0, [], hasExternalSideEffects: false),
                RecordObjectDraw);
            graph.ReadBuffer(pass, uniformVersion, GraphBufferUsage.UniformRead, uniformRange);
            graph.ReadBuffer(pass, objectDataVersion, GraphBufferUsage.UniformRead, objectDataRange);
            graph.ReadTexture(pass, sampledTextureVersion, GraphTextureUsage.SampledRead, sampledTextureRange);
            var output = graph.WriteColorAttachment(
                pass,
                graph.OffscreenColorTargetVersion,
                outputRange,
                AttachmentLoadAction.Clear,
                AttachmentStoreAction.Store,
                new Vector4(0, 0, 0, 1));
            graph.SetPresentationSource(output);

            var compileResponse = graph.Compile();
            Assert.That(compileResponse.Status, Is.EqualTo(RenderGraphCompileStatus.Compiled));
            var graphPlan = compileResponse.Plan
                ?? throw new InvalidOperationException("The ObjectGpuData graph compiled without a plan.");
            var submission = renderer.SubmitAsync(snapshot, graphPlan, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            Assert.That(submission.SubmissionSequence, Is.GreaterThan(0));
            Assert.That(submission.CompletedFence, Is.GreaterThan(0));
            Assert.That(submission.PresentationSequence, Is.Null);

            using var readback = graphicsDevice.ReadbackTextureAsync(
                target,
                new TextureSubresource(0, 0, TextureAspect.Color),
                CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            var readbackBytes = new byte[checked((int)readback.ByteLength)];

            using (var readLease = readback.AcquireReadLease())
            {
                var copyAction = new ReadbackCopyAction(readbackBytes);
                readLease.UseWindow(0, readbackBytes.Length, ref copyAction);
            }

            var centerOffset = checked((int)(
                (readback.RowPitch * (targetExtent.Height / 2))
                + (targetExtent.Width / 2 * 4)));
            Assert.That(readbackBytes.AsSpan(centerOffset, 4).ToArray(), Is.EqualTo(new byte[] { 255, 0, 255, 255 }));
            Assert.That(readbackBytes.AsSpan(0, 4).ToArray(), Is.EqualTo(new byte[] { 0, 0, 0, 255 }));
        }
        finally
        {
            try
            {
                residencyService?.StopAsync().GetAwaiter().GetResult();

                device?.ShutdownAsync().GetAwaiter().GetResult();
            }
            finally
            {
                platformOwner.DisposeFromOwner();
            }
        }
    }
}
