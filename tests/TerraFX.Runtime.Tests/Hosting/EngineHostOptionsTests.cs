// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Buffers;
using TerraFX.Content.Tools;
using TerraFX.Graphics;
using TerraFX.Graphics.D3D12;
using TerraFX.Graphics.Vulkan;
using TerraFX.Hosting;
using TerraFX.Numerics;
using TerraFX.Platform;

namespace TerraFX.Runtime.Tests.Hosting;

[TestFixture]
internal sealed partial class EngineHostOptionsTests
{
    private static AssetStoreOptions CreateAssetStoreOptions()
    {
        var assignment = new ContentSchemaIdentityAssignment("test/native-root");
        var nativeRoot = new DataSchema(
            new ContentSchemaReference(assignment.SchemaId, 1, 0, ContentSchemaScope.Content),
            assignment,
            ContentSchemaRole.NativeAsset,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            []);
        var schemas = new CompiledContentSchemaRegistry([.. NativeAssetSchemaCatalogue.Schemas, nativeRoot]);
        var registration = new AssetStoreRegistrationBuilder(schemas).Build();

        return new AssetStoreOptions(Path.GetFullPath("content.epkg"), new byte[32], registration);
    }

    private static OwnedBuffer<byte> CreateBytecodeStorage(string path)
    {
        var bytecode = File.ReadAllBytes(path);
        var storage = UnmanagedBuffer<byte>.Allocate(
            bytecode.Length,
            new BufferAllocationOptions(bytecode.Length, alignment: 1));
        storage.CopyFrom(bytecode.AsSpan(), 0);
        return storage.TransferToOwnedBuffer();
    }

    private static GraphicsPipelineDescription CreateD3D12ResourcePipelineDescription(
        BufferReadLease<byte> vertexBytecode,
        BufferReadLease<byte> pixelBytecode,
        ShaderReflection vertexReflection,
        ShaderReflection pixelReflection)
    {
        var vertexShader = new ShaderBytecodeInput(
            ShaderStage.Vertex,
            ShaderBinaryFormat.Dxil,
            vertexBytecode,
            vertexReflection);
        var pixelShader = new ShaderBytecodeInput(
            ShaderStage.Pixel,
            ShaderBinaryFormat.Dxil,
            pixelBytecode,
            pixelReflection);

        return new GraphicsPipelineDescription(
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
            bindingLayoutVersion: 1);
    }

    private static EngineHostOptions CreateHeadlessOptions(
        AssetStoreOptions assetStoreOptions,
        int? cpuWorkerCount = null,
        SimulationOverrunPolicy? overrunPolicy = null,
        GraphicsFailurePolicy? graphicsFailurePolicy = null,
        ClipboardServicePolicy? clipboardServicePolicy = null,
        TimeSpan? shutdownTimeout = null)
        => new(
            HostProfile.Headless,
            PlatformBackend.Headless,
            graphicsBackend: null,
            graphicsBackendRegistration: null,
            assetStoreOptions: assetStoreOptions) {
            CpuWorkerCount = cpuWorkerCount ?? Math.Min(64, Math.Max(1, Environment.ProcessorCount - 2)),
            OverrunPolicy = overrunPolicy ?? SimulationOverrunPolicy.Pause,
            GraphicsFailurePolicy = graphicsFailurePolicy ?? GraphicsFailurePolicy.StopHost,
            ClipboardServicePolicy = clipboardServicePolicy ?? ClipboardServicePolicy.NotRequested,
            PersistenceMetadata = CreatePersistenceMetadata(),
            ShutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(5),
        };

    private static WorldPersistenceMetadata CreatePersistenceMetadata()
    {
        var digest = new byte[32];
        var application = new WorldPersistenceApplication(
            new Guid("10000000-0000-0000-0000-000000000000"),
            majorVersion: 1,
            minorVersion: 0,
            modules: []);
        var runtimeProfile = new WorldPersistenceRuntimeProfile(
            digest,
            digest,
            digest,
            WorldPersistenceOperatingSystem.Windows,
            WorldPersistenceCodeGeneration.Jit,
            WorldPersistenceInstructionSet.None,
            digest,
            WorldPersistenceFmaPolicy.Disallowed,
            maximumSimdBits: 0,
            modules: []);

        return new WorldPersistenceMetadata(application, runtimeProfile);
    }

    private static ShaderReflection CreateReflectionWithResourceBinding(
        ShaderReflection reflection,
        ShaderResourceKind kind,
        int binding)
    {
        var resources = ImmutableArray.CreateBuilder<ShaderResourceSignature>(reflection.Resources.Length);
        var hasMatchingResource = false;

        foreach (var resource in reflection.Resources)
        {
            if (resource.Kind == kind)
            {
                if (hasMatchingResource)
                {
                    throw new InvalidOperationException("The selected shader resource kind is ambiguous.");
                }

                resources.Add(new ShaderResourceSignature(resource.Group, binding, resource.Kind, resource.ArrayCount));
                hasMatchingResource = true;
            }
            else
            {
                resources.Add(resource);
            }
        }

        if (!hasMatchingResource)
        {
            throw new InvalidOperationException("The selected shader resource kind is absent.");
        }

        return new ShaderReflection(
            reflection.Stage,
            reflection.EntryPoint,
            reflection.CompilerIdentity,
            reflection.Inputs,
            reflection.Outputs,
            resources.MoveToImmutable(),
            reflection.RecordLayouts,
            reflection.RequiredCapabilities,
            reflection.BindingLayoutVersion,
            reflection.GroupSizeX,
            reflection.GroupSizeY,
            reflection.GroupSizeZ);
    }

    [Test]
    public void AcceptsExplicitD3D12RegistrationsWithoutActivatingDevices()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 registration package targets Windows 11.");
            return;
        }

        var registration = D3D12GraphicsBackend.CreateRegistration();
        var assetStoreOptions = CreateAssetStoreOptions();
        var offscreen = new EngineHostOptions(
            HostProfile.OffscreenNative,
            PlatformBackend.Headless,
            GraphicsBackend.D3D12,
            registration,
            assetStoreOptions) {
            PersistenceMetadata = CreatePersistenceMetadata(),
        };
        var interactive = new EngineHostOptions(
            HostProfile.Interactive,
            PlatformBackend.WindowsNative,
            GraphicsBackend.D3D12,
            registration,
            assetStoreOptions) {
            PersistenceMetadata = CreatePersistenceMetadata(),
        };

        Assert.That(offscreen.GraphicsBackendRegistration, Is.SameAs(registration));
        Assert.That(interactive.Platform, Is.EqualTo(PlatformBackend.WindowsNative));
        Assert.That(registration.Backend, Is.EqualTo(GraphicsBackend.D3D12));
    }

    [Test]
    public void CreatesAndRetiresHeadlessPlatformOwnerUsingCoreIssuedIdentity()
    {
        var options = CreateHeadlessOptions(CreateAssetStoreOptions());
        var owner = PlatformHostOwner.Create(options);

        try
        {
            Assert.That(owner.OwnerId, Is.GreaterThan(0));
            Assert.That(owner.Platform.ClipboardStatus, Is.EqualTo(ClipboardServiceStatus.NotRequested));
            Assert.That(owner.Platform.GetMonitorHandles(), Is.Empty);
            _ = owner.Pump();

            _ = Assert.Throws<InvalidOperationException>(
                () => Task.Run(() => _ = owner.Pump()).GetAwaiter().GetResult());
            _ = Assert.Throws<InvalidOperationException>(
                () => Task.Run(owner.DisposeFromOwner).GetAwaiter().GetResult());

            owner.DisposeFromOwner();
            owner.DisposeFromOwner();
            _ = Assert.Throws<ObjectDisposedException>(() => _ = owner.Pump());
        }
        finally
        {
            owner.DisposeFromOwner();
        }
    }

    [Test]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The synchronous waits complete native requests before resource disposal, and the PlatformHostOwner must be used on its creating test thread.")]
    public void CreatesAndShutsDownD3D12GraphicsDeviceUsingHostIssuedIdentity()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 backend targets Windows 11.");
            return;
        }

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
            var capabilities = graphicsDevice.Capabilities;
            Assert.That(graphicsDevice.OwnerId, Is.EqualTo(platformOwner.OwnerId));
            Assert.That(graphicsDevice.Status, Is.EqualTo(GraphicsDeviceStatus.Ready));
            Assert.That(capabilities.Backend, Is.EqualTo(GraphicsBackend.D3D12));
            Assert.That(capabilities.Profile, Is.EqualTo(GraphicsDeviceProfile.OffscreenNative));
            Assert.That(capabilities.AdapterName, Is.Not.Empty);
            Assert.That(capabilities.DriverName, Is.Not.Empty);
            Assert.That(capabilities.DriverVersion, Is.Not.Empty);
            Assert.That(capabilities.ApiVersion.Major, Is.GreaterThan(0));

            var shaderFixtureDirectory = Environment.GetEnvironmentVariable("TERRAFX_M04_SHADER_FIXTURE_DIRECTORY");

            if (!string.IsNullOrWhiteSpace(shaderFixtureDirectory))
            {
                using var vertexBytecodeStorage = CreateBytecodeStorage(Path.Combine(
                    shaderFixtureDirectory,
                    "M04OffscreenTriangle.vertex.dxil"));
                using var pixelBytecodeStorage = CreateBytecodeStorage(Path.Combine(
                    shaderFixtureDirectory,
                    "M04OffscreenTriangle.pixel-sampled-frame-storage.dxil"));
                using var vertexBytecode = vertexBytecodeStorage.AcquireReadLease(0, vertexBytecodeStorage.Length);
                using var pixelBytecode = pixelBytecodeStorage.AcquireReadLease(0, pixelBytecodeStorage.Length);
                var vertexReflection = M04ShaderFixtureCompiler.ReadTypedReflection(shaderFixtureDirectory, "vertex");
                var pixelReflection = M04ShaderFixtureCompiler.ReadTypedReflection(
                    shaderFixtureDirectory,
                    "pixel-sampled-frame-storage");
                using var pipeline = graphicsDevice.CreatePipelineAsync(
                    CreateD3D12ResourcePipelineDescription(vertexBytecode, pixelBytecode, vertexReflection, pixelReflection),
                    CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                Assert.That(pipeline.Handle.IsValid, Is.True);
                Assert.That(pipeline.Handle.OwnerId, Is.EqualTo(platformOwner.OwnerId));
                Assert.That(pipeline.Handle.DeviceEpoch, Is.EqualTo(graphicsDevice.DeviceEpoch));
                TestContext.WriteLine("D3D12 created a resource-bearing PSO and root signature from compiled DXIL.");
            }

            using var depthTexture = graphicsDevice.CreateTextureAsync(
                new TextureDescription(
                    TextureDimension.Dimension2D,
                    4,
                    4,
                    depth: 1,
                    arrayLayers: 1,
                    mipLevels: 1,
                    TextureFormat.D32Float,
                    TextureUsage.DepthAttachment | TextureUsage.Sampled | TextureUsage.CopySource | TextureUsage.CopyDestination,
                    "d32-sampled-startup"))
                .GetAwaiter()
                .GetResult();
            Assert.That(depthTexture.Handle.IsValid, Is.True);
            Assert.That(depthTexture.Handle.OwnerId, Is.EqualTo(platformOwner.OwnerId));
            Assert.That(depthTexture.Handle.DeviceEpoch, Is.EqualTo(graphicsDevice.DeviceEpoch));

            var expected = new byte[64];

            for (var index = 0; index < expected.Length; index++)
            {
                expected[index] = unchecked((byte)((index * 37) + 11));
            }

            var sourceStorage = UnmanagedBuffer<byte>.Allocate(
                expected.Length,
                new BufferAllocationOptions(expected.Length, alignment: 1));
            sourceStorage.CopyFrom(expected.AsSpan(), 0);

            using var ownedSource = sourceStorage.TransferToOwnedBuffer();
            using var sourceLease = ownedSource.AcquireReadLease(0, expected.Length);
            var subresource = new TextureSubresource(0, 0, TextureAspect.Depth);
            var upload = graphicsDevice.UploadTextureAsync(
                depthTexture,
                subresource,
                sourceLease,
                0,
                new TextureDataLayout(rowPitch: 16, slicePitch: 64),
                CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            Assert.That(upload.Status, Is.EqualTo(UploadStatus.Ready));
            Assert.That(upload.DeviceEpoch, Is.EqualTo(graphicsDevice.DeviceEpoch));
            var completedFence = upload.CompletedFence
                ?? throw new InvalidOperationException("A ready D3D12 texture upload has no completed fence.");
            Assert.That(completedFence, Is.GreaterThan(0));

            using var readback = graphicsDevice.ReadbackTextureAsync(
                depthTexture,
                subresource,
                CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            Assert.That(readback.Format, Is.EqualTo(TextureFormat.D32Float));
            var padded = new byte[checked((int)readback.ByteLength)];
            using (var readLease = readback.AcquireReadLease())
            {
                var copyAction = new ReadbackCopyAction(padded);
                readLease.UseWindow(0, padded.Length, ref copyAction);
            }

            var actual = new byte[expected.Length];

            for (var row = 0; row < readback.Height; row++)
            {
                padded.AsSpan(
                    checked(row * (int)readback.RowPitch),
                    16)
                    .CopyTo(actual.AsSpan(row * 16, 16));
            }

            Assert.That(actual, Is.EqualTo(expected));

            var volumeDescription = new TextureDescription(
                TextureDimension.Dimension3D,
                4,
                4,
                depth: 2,
                arrayLayers: 1,
                mipLevels: 1,
                TextureFormat.Rgba8Unorm,
                TextureUsage.CopySource | TextureUsage.CopyDestination,
                "volume-copy-footprint");
            using var volumeTexture = graphicsDevice.CreateTextureAsync(volumeDescription).GetAwaiter().GetResult();
            var volumeExpected = new byte[128];

            for (var index = 0; index < volumeExpected.Length; index++)
            {
                volumeExpected[index] = unchecked((byte)((index * 23) + 5));
            }

            var volumeSourceStorage = UnmanagedBuffer<byte>.Allocate(
                volumeExpected.Length,
                new BufferAllocationOptions(volumeExpected.Length, alignment: 1));
            volumeSourceStorage.CopyFrom(volumeExpected.AsSpan(), 0);

            using var ownedVolumeSource = volumeSourceStorage.TransferToOwnedBuffer();
            using var volumeSourceLease = ownedVolumeSource.AcquireReadLease(0, volumeExpected.Length);
            var volumeSubresource = new TextureSubresource(0, 0, TextureAspect.Color);
            var volumeUpload = graphicsDevice.UploadTextureAsync(
                volumeTexture,
                volumeSubresource,
                volumeSourceLease,
                0,
                new TextureDataLayout(rowPitch: 16, slicePitch: 64),
                CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            Assert.That(volumeUpload.Status, Is.EqualTo(UploadStatus.Ready));

            using var volumeReadback = graphicsDevice.ReadbackTextureAsync(
                volumeTexture,
                volumeSubresource,
                CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            var volumePadded = new byte[checked((int)volumeReadback.ByteLength)];

            using (var volumeReadLease = volumeReadback.AcquireReadLease())
            {
                var copyAction = new ReadbackCopyAction(volumePadded);
                volumeReadLease.UseWindow(0, volumePadded.Length, ref copyAction);
            }

            var volumeActual = new byte[volumeExpected.Length];

            for (var slice = 0; slice < volumeReadback.Depth; slice++)
            {
                for (var row = 0; row < volumeReadback.Height; row++)
                {
                    var sourceOffset = checked(
                        (slice * (int)volumeReadback.SlicePitch)
                        + (row * (int)volumeReadback.RowPitch));
                    var destinationOffset = checked((slice * 64) + (row * 16));
                    volumePadded.AsSpan(sourceOffset, 16).CopyTo(volumeActual.AsSpan(destinationOffset, 16));
                }
            }

            Assert.That(volumeActual, Is.EqualTo(volumeExpected));

            const int stagingRingByteLength = 64 * 1024 * 1024;
            const int dedicatedStagingByteLength = stagingRingByteLength + 1;

            if (capabilities.Limits.MaximumBufferByteLength < dedicatedStagingByteLength)
            {
                Assert.Ignore("The selected D3D12 device cannot allocate the destination required by the staging-boundary test.");
                return;
            }

            var stagingPayload = new byte[dedicatedStagingByteLength];
            stagingPayload[0] = 0x42;
            stagingPayload[stagingRingByteLength - 1] = 0x17;
            stagingPayload[stagingRingByteLength] = 0x9C;

            using var stagingBuffer = graphicsDevice.CreateBufferAsync(
                new BufferDescription(
                    dedicatedStagingByteLength,
                    BufferUsage.CopySource | BufferUsage.CopyDestination,
                    GpuMemoryKind.DeviceLocal,
                    structuredStride: null,
                    debugName: "exact staging-ring boundary destination"))
                .GetAwaiter()
                .GetResult();
            var ringUpload = graphicsDevice.UploadBufferAsync(
                stagingBuffer,
                0,
                stagingPayload.AsSpan(0, stagingRingByteLength))
                .GetAwaiter()
                .GetResult();
            Assert.That(ringUpload.Status, Is.EqualTo(UploadStatus.Ready));
            var ringCompletedFence = ringUpload.CompletedFence
                ?? throw new InvalidOperationException("A ready D3D12 staging-ring upload has no completed fence.");
            Assert.That(ringCompletedFence, Is.GreaterThan(0));

            using (var ringReadback = graphicsDevice.ReadbackBufferAsync(
                stagingBuffer,
                0,
                stagingRingByteLength)
                .GetAwaiter()
                .GetResult())
            using (var ringReadLease = ringReadback.AcquireReadLease(0, stagingRingByteLength))
            {
                var ringActual = new byte[1];
                var copyAction = new ReadbackCopyAction(ringActual);
                ringReadLease.UseWindow(stagingRingByteLength - 1, 1, ref copyAction);
                Assert.That(ringActual, Is.EqualTo(new byte[] { 0x17 }));
            }

            var dedicatedUpload = graphicsDevice.UploadBufferAsync(
                stagingBuffer,
                0,
                stagingPayload)
                .GetAwaiter()
                .GetResult();
            Assert.That(dedicatedUpload.Status, Is.EqualTo(UploadStatus.Ready));
            var dedicatedCompletedFence = dedicatedUpload.CompletedFence
                ?? throw new InvalidOperationException("A ready D3D12 dedicated-staging upload has no completed fence.");
            Assert.That(dedicatedCompletedFence, Is.GreaterThan(0));

            using (var dedicatedReadback = graphicsDevice.ReadbackBufferAsync(
                stagingBuffer,
                0,
                dedicatedStagingByteLength)
                .GetAwaiter()
                .GetResult())
            using (var dedicatedReadLease = dedicatedReadback.AcquireReadLease(0, dedicatedStagingByteLength))
            {
                var dedicatedActual = new byte[1];
                var copyAction = new ReadbackCopyAction(dedicatedActual);
                dedicatedReadLease.UseWindow(stagingRingByteLength, 1, ref copyAction);
                Assert.That(dedicatedActual, Is.EqualTo(new byte[] { 0x9C }));
            }

            TestContext.WriteLine(
                $"Backend={capabilities.Backend}; Profile={capabilities.Profile}; Adapter={capabilities.AdapterName}; "
                + $"Vendor={capabilities.VendorId:X4}; Device={capabilities.DeviceId:X4}; "
                + $"Driver={capabilities.DriverName} {capabilities.DriverVersion}; "
                + $"API={capabilities.ApiVersion.Major}.{capabilities.ApiVersion.Minor}.{capabilities.ApiVersion.Patch}");
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
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The synchronous waits complete native requests before resource disposal, and the PlatformHostOwner must be used on its creating test thread.")]
    public void CreatesAndShutsDownVulkanGraphicsDeviceUsingHostIssuedIdentity()
    {
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
            var capabilities = graphicsDevice.Capabilities;
            Assert.That(graphicsDevice.OwnerId, Is.EqualTo(platformOwner.OwnerId));
            Assert.That(graphicsDevice.Status, Is.EqualTo(GraphicsDeviceStatus.Ready));
            Assert.That(capabilities.Backend, Is.EqualTo(GraphicsBackend.Vulkan));
            Assert.That(capabilities.Profile, Is.EqualTo(GraphicsDeviceProfile.OffscreenNative));
            Assert.That(capabilities.AdapterName, Is.Not.Empty);
            Assert.That(capabilities.DriverName, Is.Not.Empty);
            Assert.That(capabilities.DriverVersion, Is.Not.Empty);
            Assert.That(capabilities.ApiVersion.Major, Is.EqualTo(1));
            Assert.That(capabilities.ApiVersion.Minor, Is.GreaterThanOrEqualTo(3));
            TestContext.WriteLine(
                $"Backend={capabilities.Backend}; Profile={capabilities.Profile}; Adapter={capabilities.AdapterName}; "
                + $"Vendor={capabilities.VendorId:X4}; Device={capabilities.DeviceId:X4}; "
                + $"Driver={capabilities.DriverName} {capabilities.DriverVersion}; "
                + $"API={capabilities.ApiVersion.Major}.{capabilities.ApiVersion.Minor}.{capabilities.ApiVersion.Patch}; "
                + $"Features={string.Join(",", capabilities.ImplementedFeatures)}");
            TestContext.WriteLine(
                $"VULKAN_SDK={Environment.GetEnvironmentVariable("VULKAN_SDK") ?? "(unset)"}; "
                + $"VK_INSTANCE_LAYERS={Environment.GetEnvironmentVariable("VK_INSTANCE_LAYERS") ?? "(unset)"}");

            GpuBuffer? buffer = null;

            try
            {
                buffer = graphicsDevice.CreateBufferAsync(
                    new BufferDescription(
                        256,
                        BufferUsage.CopySource | BufferUsage.CopyDestination,
                        GpuMemoryKind.DeviceLocal,
                        structuredStride: null,
                        debugName: "graphics-device-startup-roundtrip"))
                    .GetAwaiter()
                    .GetResult();
                Assert.That(buffer.Handle.IsValid, Is.True);
                Assert.That(buffer.Handle.OwnerId, Is.EqualTo(platformOwner.OwnerId));
                Assert.That(buffer.Handle.DeviceEpoch, Is.EqualTo(graphicsDevice.DeviceEpoch));
                var payload = new byte[32];

                for (var index = 0; index < payload.Length; index++)
                {
                    payload[index] = unchecked((byte)((index * 29) + 7));
                }

                var upload = graphicsDevice.UploadBufferAsync(buffer, 64, payload).GetAwaiter().GetResult();
                Assert.That(upload.Status, Is.EqualTo(UploadStatus.Ready));
                Assert.That(upload.DeviceEpoch, Is.EqualTo(graphicsDevice.DeviceEpoch));
                var completedFence = upload.CompletedFence
                    ?? throw new InvalidOperationException("A ready Vulkan upload has no completed fence.");
                Assert.That(completedFence, Is.GreaterThan(0));

                using var readback = graphicsDevice.ReadbackBufferAsync(buffer, 64, payload.Length).GetAwaiter().GetResult();
                using var readLease = readback.AcquireReadLease(0, payload.Length);
                var actual = new byte[payload.Length];
                var copyAction = new ReadbackCopyAction(actual);
                readLease.UseWindow(0, payload.Length, ref copyAction);
                Assert.That(actual, Is.EqualTo(payload));
            }
            finally
            {
                buffer?.Dispose();
            }
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
    [System.Runtime.Versioning.SupportedOSPlatform("windows10.0.22000.0")]
    public void CreatesD3D12DescriptorSetsAndRetainsNativeOwners()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 backend targets Windows 11.");
            return;
        }

        var shaderFixtureDirectory = Environment.GetEnvironmentVariable("TERRAFX_M04_SHADER_FIXTURE_DIRECTORY");

        if (string.IsNullOrWhiteSpace(shaderFixtureDirectory))
        {
            Assert.Ignore("Set TERRAFX_M04_SHADER_FIXTURE_DIRECTORY to run the D3D12 descriptor-set witness.");
            return;
        }

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

        try
        {
            D3D12GraphicsBackend.D3D12BackendFactory.D3D12BackendDevice device;

            try
            {
                device = (D3D12GraphicsBackend.D3D12BackendFactory.D3D12BackendDevice)
                    new D3D12GraphicsBackend.D3D12BackendFactory().Create(
                        platformOwner.OwnerId,
                        GraphicsDeviceProfile.OffscreenNative,
                        GraphicsPlatformKind.None,
                        CancellationToken.None);
            }
            catch (DllNotFoundException exception)
            {
                Assert.Ignore($"A required D3D12 system library is unavailable: {exception.Message}");
                return;
            }
            catch (EntryPointNotFoundException exception)
            {
                Assert.Ignore($"A required D3D12 system entry point is unavailable: {exception.Message}");
                return;
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No available D3D12 device satisfies OffscreenNative: {exception.Message}");
                return;
            }

            using (device)
            {
                using var vertexBytecodeStorage = CreateBytecodeStorage(Path.Combine(
                    shaderFixtureDirectory,
                    "M04OffscreenTriangle.vertex.dxil"));
                using var pixelBytecodeStorage = CreateBytecodeStorage(Path.Combine(
                    shaderFixtureDirectory,
                    "M04OffscreenTriangle.pixel-sampled-frame.dxil"));
                using var vertexBytecode = vertexBytecodeStorage.AcquireReadLease(0, vertexBytecodeStorage.Length);
                using var pixelBytecode = pixelBytecodeStorage.AcquireReadLease(0, pixelBytecodeStorage.Length);
                var vertexReflection = M04ShaderFixtureCompiler.ReadTypedReflection(shaderFixtureDirectory, "vertex");
                var pixelReflection = M04ShaderFixtureCompiler.ReadTypedReflection(
                    shaderFixtureDirectory,
                    "pixel-sampled-frame");
                var mismatchedPixelReflection = CreateReflectionWithResourceBinding(
                    pixelReflection,
                    ShaderResourceKind.SampledImage,
                    1);
                _ = Assert.Throws<ArgumentException>(() => {
                    using var mismatchedPipeline = device.CreatePipeline(
                        CreateD3D12ResourcePipelineDescription(
                            vertexBytecode,
                            pixelBytecode,
                            vertexReflection,
                            mismatchedPixelReflection),
                        [vertexBytecode, pixelBytecode]);
                });
                using var pipeline = device.CreatePipeline(
                    CreateD3D12ResourcePipelineDescription(
                        vertexBytecode,
                        pixelBytecode,
                        vertexReflection,
                        pixelReflection),
                    [vertexBytecode, pixelBytecode]);
                using var uniformBuffer = device.CreateBuffer(
                    new BufferDescription(
                        512,
                        BufferUsage.Uniform,
                        GpuMemoryKind.DeviceLocal,
                        structuredStride: null,
                        debugName: "d3d12-descriptor-set-frame-data"));
                using var sampledTexture = device.CreateTexture(
                    new TextureDescription(
                        TextureDimension.Dimension2D,
                        1,
                        1,
                        depth: 1,
                        arrayLayers: 1,
                        mipLevels: 1,
                        TextureFormat.Rgba8Unorm,
                        TextureUsage.Sampled,
                        "d3d12-descriptor-set-sampled-texture"));
                using var sampler = device.CreateSampler(
                    new SamplerDescription(
                        SamplerFilter.Linear,
                        SamplerFilter.Linear,
                        SamplerFilter.None,
                        SamplerAddressMode.Clamp,
                        SamplerAddressMode.Clamp,
                        SamplerAddressMode.Clamp,
                        maximumAnisotropy: 1.0f),
                    effectiveMaximumAnisotropy: 1.0f);
                var uniformBufferBindings = ImmutableArray.Create(new D3D12BackendUniformBufferBinding(
                    ShaderBindingGroup.Frame,
                    0,
                    uniformBuffer,
                    0,
                    16));
                var sampledImageBindings = ImmutableArray.Create(new D3D12BackendSampledImageBinding(
                    ShaderBindingGroup.Frame,
                    0,
                    sampledTexture));
                var samplerBindings = ImmutableArray.Create(new D3D12BackendSamplerBinding(
                    ShaderBindingGroup.Frame,
                    0,
                    sampler));

                _ = Assert.Throws<ArgumentException>(() => device.CreateDescriptorSet(
                    pipeline,
                    uniformBufferBindings,
                    sampledImageBindings,
                    [],
                    [],
                    []));

                var misalignedUniformBufferBindings = ImmutableArray.Create(new D3D12BackendUniformBufferBinding(
                    ShaderBindingGroup.Frame,
                    0,
                    uniformBuffer,
                    1,
                    16));
                _ = Assert.Throws<ArgumentException>(() => device.CreateDescriptorSet(
                    pipeline,
                    misalignedUniformBufferBindings,
                    sampledImageBindings,
                    [],
                    [],
                    samplerBindings));

                using var descriptorSet = device.CreateDescriptorSet(
                    pipeline,
                    uniformBufferBindings,
                    sampledImageBindings,
                    [],
                    [],
                    samplerBindings);
                Assert.That(descriptorSet.BufferCount, Is.EqualTo(1));
                Assert.That(descriptorSet.TextureCount, Is.EqualTo(1));
                Assert.That(descriptorSet.SamplerCount, Is.EqualTo(1));
                Assert.That(descriptorSet.DescriptorTableCount, Is.EqualTo(3));
                Assert.That(descriptorSet.AreDescriptorTableHandlesValid, Is.True);
                _ = Assert.Throws<InvalidOperationException>(uniformBuffer.Dispose);
                _ = Assert.Throws<InvalidOperationException>(sampledTexture.Dispose);
                _ = Assert.Throws<InvalidOperationException>(sampler.Dispose);
                _ = Assert.Throws<InvalidOperationException>(pipeline.Dispose);
                _ = Assert.Throws<InvalidOperationException>(device.Dispose);

                descriptorSet.Dispose();
                Assert.That(descriptorSet.IsDisposed, Is.True);
                Assert.DoesNotThrow(uniformBuffer.Dispose);
                Assert.DoesNotThrow(sampledTexture.Dispose);
                Assert.DoesNotThrow(sampler.Dispose);
                Assert.DoesNotThrow(pipeline.Dispose);
                TestContext.WriteLine("D3D12 wrote CBV, SRV, and sampler tables and retained every native owner until descriptor-set disposal.");

                using var storagePixelBytecodeStorage = CreateBytecodeStorage(Path.Combine(
                    shaderFixtureDirectory,
                    "M04OffscreenTriangle.pixel-sampled-frame-storage.dxil"));
                using var storagePixelBytecode = storagePixelBytecodeStorage.AcquireReadLease(0, storagePixelBytecodeStorage.Length);
                var storagePixelReflection = M04ShaderFixtureCompiler.ReadTypedReflection(
                    shaderFixtureDirectory,
                    "pixel-sampled-frame-storage");
                using var storagePipeline = device.CreatePipeline(
                    CreateD3D12ResourcePipelineDescription(
                        vertexBytecode,
                        storagePixelBytecode,
                        vertexReflection,
                        storagePixelReflection),
                    [vertexBytecode, storagePixelBytecode]);
                using var storageUniformBuffer = device.CreateBuffer(
                    new BufferDescription(
                        512,
                        BufferUsage.Uniform,
                        GpuMemoryKind.DeviceLocal,
                        structuredStride: null,
                        debugName: "d3d12-storage-descriptor-frame-data"));
                using var storageSampledTexture = device.CreateTexture(
                    new TextureDescription(
                        TextureDimension.Dimension2D,
                        1,
                        1,
                        depth: 1,
                        arrayLayers: 1,
                        mipLevels: 1,
                        TextureFormat.Rgba8Unorm,
                        TextureUsage.Sampled,
                        "d3d12-storage-descriptor-sampled-texture"));
                using var storageBuffer = device.CreateBuffer(
                    new BufferDescription(
                        16,
                        BufferUsage.Storage,
                        GpuMemoryKind.DeviceLocal,
                        structuredStride: 4,
                        debugName: "d3d12-storage-descriptor-buffer"));
                using var storageTexture = device.CreateTexture(
                    new TextureDescription(
                        TextureDimension.Dimension2D,
                        1,
                        1,
                        depth: 1,
                        arrayLayers: 1,
                        mipLevels: 1,
                        TextureFormat.R32UInt,
                        TextureUsage.Storage,
                        "d3d12-storage-descriptor-texture"));
                var storageSamplerDescription = new SamplerDescription(
                    SamplerFilter.Linear,
                    SamplerFilter.Linear,
                    SamplerFilter.None,
                    SamplerAddressMode.Clamp,
                    SamplerAddressMode.Clamp,
                    SamplerAddressMode.Clamp,
                    maximumAnisotropy: 1.0f);
                using var storageSampler = device.CreateSampler(
                    storageSamplerDescription,
                    effectiveMaximumAnisotropy: 1.0f);
                var storageUniformBufferBindings = ImmutableArray.Create(new D3D12BackendUniformBufferBinding(
                    ShaderBindingGroup.Frame,
                    0,
                    storageUniformBuffer,
                    0,
                    16));
                var storageSampledImageBindings = ImmutableArray.Create(new D3D12BackendSampledImageBinding(
                    ShaderBindingGroup.Frame,
                    0,
                    storageSampledTexture));
                var storageBufferBindings = ImmutableArray.Create(new D3D12BackendStorageBufferBinding(
                    ShaderBindingGroup.Frame,
                    0,
                    storageBuffer,
                    0,
                    16));
                var storageImageBindings = ImmutableArray.Create(new D3D12BackendStorageImageBinding(
                    ShaderBindingGroup.Frame,
                    1,
                    storageTexture));
                var storageSamplerBindings = ImmutableArray.Create(new D3D12BackendSamplerBinding(
                    ShaderBindingGroup.Frame,
                    0,
                    storageSampler));

                _ = Assert.Throws<ArgumentException>(() => device.CreateDescriptorSet(
                    storagePipeline,
                    storageUniformBufferBindings,
                    storageSampledImageBindings,
                    storageBufferBindings,
                    [],
                    storageSamplerBindings));

                var unalignedStorageBufferBindings = ImmutableArray.Create(new D3D12BackendStorageBufferBinding(
                    ShaderBindingGroup.Frame,
                    0,
                    storageBuffer,
                    1,
                    16));
                _ = Assert.Throws<ArgumentException>(() => device.CreateDescriptorSet(
                    storagePipeline,
                    storageUniformBufferBindings,
                    storageSampledImageBindings,
                    unalignedStorageBufferBindings,
                    storageImageBindings,
                    storageSamplerBindings));

                var invalidStructuredRangeBindings = ImmutableArray.Create(new D3D12BackendStorageBufferBinding(
                    ShaderBindingGroup.Frame,
                    0,
                    storageBuffer,
                    0,
                    5));
                _ = Assert.Throws<ArgumentException>(() => device.CreateDescriptorSet(
                    storagePipeline,
                    storageUniformBufferBindings,
                    storageSampledImageBindings,
                    invalidStructuredRangeBindings,
                    storageImageBindings,
                    storageSamplerBindings));

                using var invalidStorageBuffer = device.CreateBuffer(
                    new BufferDescription(
                        16,
                        BufferUsage.CopySource,
                        GpuMemoryKind.DeviceLocal,
                        structuredStride: 4,
                        debugName: "d3d12-storage-descriptor-invalid-usage"));
                var invalidStorageUsageBindings = ImmutableArray.Create(new D3D12BackendStorageBufferBinding(
                    ShaderBindingGroup.Frame,
                    0,
                    invalidStorageBuffer,
                    0,
                    16));
                _ = Assert.Throws<ArgumentException>(() => device.CreateDescriptorSet(
                    storagePipeline,
                    storageUniformBufferBindings,
                    storageSampledImageBindings,
                    invalidStorageUsageBindings,
                    storageImageBindings,
                    storageSamplerBindings));

                using var storageDescriptorSet = device.CreateDescriptorSet(
                    storagePipeline,
                    storageUniformBufferBindings,
                    storageSampledImageBindings,
                    storageBufferBindings,
                    storageImageBindings,
                    storageSamplerBindings);
                Assert.That(storageDescriptorSet.BufferCount, Is.EqualTo(2));
                Assert.That(storageDescriptorSet.TextureCount, Is.EqualTo(2));
                Assert.That(storageDescriptorSet.SamplerCount, Is.EqualTo(1));
                Assert.That(storageDescriptorSet.DescriptorTableCount, Is.EqualTo(4));
                Assert.That(storageDescriptorSet.AreDescriptorTableHandlesValid, Is.True);
                _ = Assert.Throws<InvalidOperationException>(storageUniformBuffer.Dispose);
                _ = Assert.Throws<InvalidOperationException>(storageSampledTexture.Dispose);
                _ = Assert.Throws<InvalidOperationException>(storageBuffer.Dispose);
                _ = Assert.Throws<InvalidOperationException>(storageTexture.Dispose);
                _ = Assert.Throws<InvalidOperationException>(storageSampler.Dispose);
                _ = Assert.Throws<InvalidOperationException>(storagePipeline.Dispose);
                _ = Assert.Throws<InvalidOperationException>(device.Dispose);

                const int SamplerHeapCapacity = 256;
                const int SamplerDescriptorsPerSet = 2;
                var descriptorSets = new D3D12GraphicsBackend.D3D12BackendFactory.D3D12BackendDevice.D3D12BackendDescriptorSet?[
                    SamplerHeapCapacity / SamplerDescriptorsPerSet];
                var samplerOwners = new IGraphicsBackendSampler?[descriptorSets.Length];
                descriptorSets[0] = storageDescriptorSet;
                samplerOwners[0] = storageSampler;

                try
                {
                    for (var index = 1; index < descriptorSets.Length; index++)
                    {
                        var samplerOwner = device.CreateSampler(
                            storageSamplerDescription,
                            effectiveMaximumAnisotropy: 1.0f);
                        samplerOwners[index] = samplerOwner;
                        var samplerBinding = ImmutableArray.Create(new D3D12BackendSamplerBinding(
                            ShaderBindingGroup.Frame,
                            0,
                            samplerOwner));
                        descriptorSets[index] = device.CreateDescriptorSet(
                            storagePipeline,
                            storageUniformBufferBindings,
                            storageSampledImageBindings,
                            storageBufferBindings,
                            storageImageBindings,
                            samplerBinding);
                    }

                    _ = Assert.Throws<InvalidOperationException>(() => device.CreateSampler(
                        storageSamplerDescription,
                        effectiveMaximumAnisotropy: 1.0f));

                    var firstDescriptorSet = descriptorSets[0]
                        ?? throw new InvalidOperationException("The initial D3D12 descriptor set is missing.");
                    firstDescriptorSet.Dispose();
                    descriptorSets[0] = null;
                    var firstSampler = samplerOwners[0]
                        ?? throw new InvalidOperationException("The initial D3D12 sampler is missing.");
                    firstSampler.Dispose();
                    samplerOwners[0] = null;

                    var replacementSampler = device.CreateSampler(
                        storageSamplerDescription,
                        effectiveMaximumAnisotropy: 1.0f);
                    samplerOwners[0] = replacementSampler;
                    var replacementSamplerBinding = ImmutableArray.Create(new D3D12BackendSamplerBinding(
                        ShaderBindingGroup.Frame,
                        0,
                        replacementSampler));
                    descriptorSets[0] = device.CreateDescriptorSet(
                        storagePipeline,
                        storageUniformBufferBindings,
                        storageSampledImageBindings,
                        storageBufferBindings,
                        storageImageBindings,
                        replacementSamplerBinding);
                    Assert.That(descriptorSets[0]?.AreDescriptorTableHandlesValid, Is.True);
                    _ = Assert.Throws<InvalidOperationException>(() => device.CreateSampler(
                        storageSamplerDescription,
                        effectiveMaximumAnisotropy: 1.0f));
                }
                finally
                {
                    for (var index = descriptorSets.Length - 1; index >= 0; index--)
                    {
                        descriptorSets[index]?.Dispose();
                    }

                    for (var index = samplerOwners.Length - 1; index >= 0; index--)
                    {
                        samplerOwners[index]?.Dispose();
                    }
                }

                Assert.That(storageDescriptorSet.IsDisposed, Is.True);
                Assert.DoesNotThrow(storageUniformBuffer.Dispose);
                Assert.DoesNotThrow(storageSampledTexture.Dispose);
                Assert.DoesNotThrow(storageBuffer.Dispose);
                Assert.DoesNotThrow(storageTexture.Dispose);
                Assert.DoesNotThrow(storageSampler.Dispose);
                Assert.DoesNotThrow(storagePipeline.Dispose);
                TestContext.WriteLine("D3D12 wrote scalar storage-buffer and storage-image UAV descriptors and retained their owners until descriptor-set disposal.");
            }
        }
        finally
        {
            platformOwner.DisposeFromOwner();
        }
    }

    [Test]
    [System.Runtime.Versioning.SupportedOSPlatform("windows10.0.22000.0")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "This test retires caller owners while an admitted D3D12 transfer waits for native fence observation.")]
    public void D3D12BufferTransferRetainsOwnersUntilFenceCompletionIsObserved()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 transfer fence witness targets Windows 11.");
            return;
        }

        using var fenceWaitEntered = new ManualResetEventSlim(initialState: false);
        using var continueFenceWait = new ManualResetEventSlim(initialState: false);
        var nativeWaitResult = -1;

        uint WaitForFence(nint fenceEvent, uint timeout)
        {
            var result = WindowsTestNativeMethods.WaitForSingleObject(fenceEvent, timeout);
            Volatile.Write(ref nativeWaitResult, unchecked((int)result));
            fenceWaitEntered.Set();
            return continueFenceWait.Wait(TimeSpan.FromSeconds(10), CancellationToken.None)
                ? result
                : 0xFFFFFFFF;
        }

        var registration = new GraphicsBackendRegistration(
            new D3D12GraphicsBackend.D3D12BackendFactory(WaitForFence));
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
        GpuBuffer? destination = null;
        OwnedBuffer<byte>? sourceOwner = null;
        BufferReadLease<byte>? sourceLease = null;

        try
        {
            try
            {
                device = GraphicsDevice.CreateAsync(
                    platformOwner.OwnerId,
                    registration,
                    GraphicsDeviceProfile.OffscreenNative,
                    GraphicsPlatformKind.None,
                    CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(30))
                    .GetAwaiter()
                    .GetResult();
            }
            catch (DllNotFoundException exception)
            {
                Assert.Ignore($"A required D3D12 system library is unavailable: {exception.Message}");
                return;
            }
            catch (EntryPointNotFoundException exception)
            {
                Assert.Ignore($"A required D3D12 system entry point is unavailable: {exception.Message}");
                return;
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No available D3D12 device satisfies OffscreenNative: {exception.Message}");
                return;
            }

            var graphicsDevice = device
                ?? throw new InvalidOperationException("D3D12 GraphicsDevice startup returned no device.");
            destination = graphicsDevice.CreateBufferAsync(
                new BufferDescription(
                    4,
                    BufferUsage.CopySource | BufferUsage.CopyDestination,
                    GpuMemoryKind.DeviceLocal,
                    structuredStride: null,
                    debugName: "native fence wait ownership test"))
                .WaitAsync(TimeSpan.FromSeconds(5))
                .GetAwaiter()
                .GetResult();
            var sourceStorage = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(4, alignment: 1));
            sourceStorage.CopyFrom([0x42, 0x17, 0x9C, 0xA5], 0);
            sourceOwner = sourceStorage.TransferToOwnedBuffer();
            sourceLease = sourceOwner.AcquireReadLease(0, 4);
            var uploadTask = graphicsDevice.UploadBufferAsync(
                destination,
                destinationOffset: 0,
                sourceLease,
                sourceOffset: 0,
                byteLength: 4,
                CancellationToken.None);

            Assert.That(fenceWaitEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(Volatile.Read(ref nativeWaitResult), Is.EqualTo(0));
            sourceLease.Dispose();
            sourceLease = null;
            sourceOwner.Dispose();
            sourceOwner = null;
            var destinationOwner = destination.ResourceOwner;
            destination.Dispose();
            destination = null;
            Assert.That(destinationOwner.HasActiveUses, Is.True);

            var shutdownTask = graphicsDevice.ShutdownAsync();
            Assert.That(uploadTask.IsCompleted, Is.False);
            Assert.That(shutdownTask.IsCompleted, Is.False);
            continueFenceWait.Set();

            var upload = uploadTask.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Assert.That(upload.Status, Is.EqualTo(UploadStatus.Ready));
            var completedFence = upload.CompletedFence
                ?? throw new InvalidOperationException("A ready D3D12 upload has no completed fence.");
            Assert.That(completedFence, Is.GreaterThan(0));
            Assert.That(destinationOwner.HasActiveUses, Is.False);
            shutdownTask.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Assert.That(graphicsDevice.Status, Is.EqualTo(GraphicsDeviceStatus.Disposed));
        }
        finally
        {
            continueFenceWait.Set();
            sourceLease?.Dispose();
            sourceOwner?.Dispose();
            destination?.Dispose();

            try
            {
                device?.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
            finally
            {
                platformOwner.DisposeFromOwner();
            }
        }
    }

    [Test]
    [System.Runtime.Versioning.SupportedOSPlatform("windows10.0.22000.0")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The injected D3D12 wait result forces conservative resource/backend quarantine after native submission.")]
    public void D3D12TimeoutAfterNativeFenceSignalQuarantinesResource()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 transfer fence witness targets Windows 11.");
            return;
        }

        var nativeWaitResult = -1;

        uint ReportTimeoutAfterSignal(nint fenceEvent, uint timeout)
        {
            var result = WindowsTestNativeMethods.WaitForSingleObject(fenceEvent, timeout);
            Volatile.Write(ref nativeWaitResult, unchecked((int)result));
            return result == 0 ? 0x00000102 : result;
        }

        var registration = new GraphicsBackendRegistration(
            new D3D12GraphicsBackend.D3D12BackendFactory(ReportTimeoutAfterSignal));
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
        GpuBuffer? destination = null;
        OwnedBuffer<byte>? sourceOwner = null;
        BufferReadLease<byte>? sourceLease = null;
        Task? shutdownTask = null;
        var shutdownFailureObserved = false;

        try
        {
            try
            {
                device = GraphicsDevice.CreateAsync(
                    platformOwner.OwnerId,
                    registration,
                    GraphicsDeviceProfile.OffscreenNative,
                    GraphicsPlatformKind.None,
                    CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(30))
                    .GetAwaiter()
                    .GetResult();
            }
            catch (DllNotFoundException exception)
            {
                Assert.Ignore($"A required D3D12 system library is unavailable: {exception.Message}");
                return;
            }
            catch (EntryPointNotFoundException exception)
            {
                Assert.Ignore($"A required D3D12 system entry point is unavailable: {exception.Message}");
                return;
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No available D3D12 device satisfies OffscreenNative: {exception.Message}");
                return;
            }

            var graphicsDevice = device
                ?? throw new InvalidOperationException("D3D12 GraphicsDevice startup returned no device.");
            destination = graphicsDevice.CreateBufferAsync(
                new BufferDescription(
                    4,
                    BufferUsage.CopySource | BufferUsage.CopyDestination,
                    GpuMemoryKind.DeviceLocal,
                    structuredStride: null,
                    debugName: "uncertain D3D12 transfer destination"))
                .WaitAsync(TimeSpan.FromSeconds(5))
                .GetAwaiter()
                .GetResult();
            var sourceStorage = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(4, alignment: 1));
            sourceStorage.CopyFrom([0x42, 0x17, 0x9C, 0xA5], 0);
            sourceOwner = sourceStorage.TransferToOwnedBuffer();
            sourceLease = sourceOwner.AcquireReadLease(0, 4);
            var uploadTask = graphicsDevice.UploadBufferAsync(
                destination,
                destinationOffset: 0,
                sourceLease,
                sourceOffset: 0,
                byteLength: 4,
                CancellationToken.None);
            _ = Assert.ThrowsAsync<GraphicsDeviceLostException>(
                async () => await uploadTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                ?? throw new InvalidOperationException("The simulated D3D12 fence timeout returned no device-loss exception.");

            Assert.That(Volatile.Read(ref nativeWaitResult), Is.EqualTo(0));
            sourceLease.Dispose();
            sourceLease = null;
            sourceOwner.Dispose();
            sourceOwner = null;
            destination.Dispose();
            destination = null;
            shutdownTask = graphicsDevice.ShutdownAsync();
            var shutdownException = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await shutdownTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                ?? throw new InvalidOperationException("Uncertain D3D12 transfer completion returned no shutdown failure.");
            shutdownFailureObserved = true;

            Assert.That(shutdownException.Message, Is.EqualTo("D3D12 resources cannot be retired while transfer completion is uncertain."));
            Assert.That(graphicsDevice.Status, Is.EqualTo(GraphicsDeviceStatus.Stopping));
        }
        finally
        {
            sourceLease?.Dispose();
            sourceOwner?.Dispose();
            destination?.Dispose();

            try
            {
                if ((device is not null) && !shutdownFailureObserved)
                {
                    (shutdownTask ?? device.ShutdownAsync())
                        .WaitAsync(TimeSpan.FromSeconds(5))
                        .GetAwaiter()
                        .GetResult();
                }
            }
            finally
            {
                platformOwner.DisposeFromOwner();
            }
        }
    }

    [Test]
    public void DefaultsBoundedHostPoliciesAndRetainsAssetOptions()
    {
        var assetStoreOptions = CreateAssetStoreOptions();
        var options = new EngineHostOptions(
            HostProfile.Headless,
            PlatformBackend.Headless,
            graphicsBackend: null,
            graphicsBackendRegistration: null,
            assetStoreOptions: assetStoreOptions) {
            PersistenceMetadata = CreatePersistenceMetadata(),
        };

        Assert.That(options.AssetStoreOptions, Is.SameAs(assetStoreOptions));
        Assert.That(options.CpuWorkerCount, Is.EqualTo(Math.Min(64, Math.Max(1, Environment.ProcessorCount - 2))));
        Assert.That(options.OverrunPolicy, Is.EqualTo(SimulationOverrunPolicy.Pause));
        Assert.That(options.GraphicsFailurePolicy, Is.EqualTo(GraphicsFailurePolicy.StopHost));
        Assert.That(options.ClipboardServicePolicy, Is.EqualTo(ClipboardServicePolicy.NotRequested));
        Assert.That(options.ShutdownTimeout, Is.EqualTo(TimeSpan.FromSeconds(5)));
        Assert.That((int)HostProfile.None, Is.EqualTo(0));
        Assert.That((int)HostProfile.Headless, Is.EqualTo(1));
        Assert.That((int)HostProfile.OffscreenNative, Is.EqualTo(2));
        Assert.That((int)HostProfile.Interactive, Is.EqualTo(3));
        Assert.That((int)SimulationOverrunPolicy.Pause, Is.EqualTo(0));
        Assert.That((int)SimulationOverrunPolicy.DropDebt, Is.EqualTo(1));
        Assert.That((int)GraphicsFailurePolicy.StopHost, Is.EqualTo(0));
        Assert.That(CreateHeadlessOptions(assetStoreOptions, cpuWorkerCount: 0).CpuWorkerCount, Is.Zero);
    }

    [Test]
    public void RejectsInvalidInitOnlyOptionValues()
    {
        var assetStoreOptions = CreateAssetStoreOptions();

        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = CreateHeadlessOptions(assetStoreOptions, cpuWorkerCount: -1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = CreateHeadlessOptions(assetStoreOptions, cpuWorkerCount: 65));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = CreateHeadlessOptions(assetStoreOptions, overrunPolicy: (SimulationOverrunPolicy)2));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = CreateHeadlessOptions(assetStoreOptions, graphicsFailurePolicy: (GraphicsFailurePolicy)1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = CreateHeadlessOptions(assetStoreOptions, clipboardServicePolicy: (ClipboardServicePolicy)3));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = CreateHeadlessOptions(assetStoreOptions, shutdownTimeout: TimeSpan.Zero));
    }

    [Test]
    public void RejectsInvalidProfileAndPlatformCombinations()
    {
        var assetStoreOptions = CreateAssetStoreOptions();

        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new EngineHostOptions(
                (HostProfile)int.MaxValue,
                PlatformBackend.Headless,
                graphicsBackend: null,
                graphicsBackendRegistration: null,
                assetStoreOptions: assetStoreOptions) {
                PersistenceMetadata = CreatePersistenceMetadata(),
            });
        _ = Assert.Throws<ArgumentException>(
            () => _ = new EngineHostOptions(
                HostProfile.Headless,
                PlatformBackend.WindowsNative,
                graphicsBackend: null,
                graphicsBackendRegistration: null,
                assetStoreOptions: assetStoreOptions) {
                PersistenceMetadata = CreatePersistenceMetadata(),
            });
        _ = Assert.Throws<ArgumentException>(
            () => _ = new EngineHostOptions(
                HostProfile.Headless,
                PlatformBackend.Headless,
                GraphicsBackend.D3D12,
                graphicsBackendRegistration: null,
                assetStoreOptions: assetStoreOptions) {
                PersistenceMetadata = CreatePersistenceMetadata(),
            });
        _ = Assert.Throws<ArgumentException>(
            () => _ = new EngineHostOptions(
                HostProfile.OffscreenNative,
                PlatformBackend.WaylandOnly,
                graphicsBackend: null,
                graphicsBackendRegistration: null,
                assetStoreOptions: assetStoreOptions) {
                PersistenceMetadata = CreatePersistenceMetadata(),
            });
        _ = Assert.Throws<ArgumentException>(
            () => _ = new EngineHostOptions(
                HostProfile.Interactive,
                PlatformBackend.Headless,
                graphicsBackend: null,
                graphicsBackendRegistration: null,
                assetStoreOptions: assetStoreOptions) {
                PersistenceMetadata = CreatePersistenceMetadata(),
            });
    }

    [Test]
    public void RejectsMismatchedRegistrationAndD3D12WaylandPairing()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 registration package targets Windows 11.");
            return;
        }

        var registration = D3D12GraphicsBackend.CreateRegistration();
        var assetStoreOptions = CreateAssetStoreOptions();

        _ = Assert.Throws<ArgumentException>(
            () => _ = new EngineHostOptions(
                HostProfile.OffscreenNative,
                PlatformBackend.Headless,
                GraphicsBackend.Vulkan,
                registration,
                assetStoreOptions) {
                PersistenceMetadata = CreatePersistenceMetadata(),
            });
        _ = Assert.Throws<ArgumentException>(
            () => _ = new EngineHostOptions(
                HostProfile.Interactive,
                PlatformBackend.WaylandOnly,
                GraphicsBackend.D3D12,
                registration,
                assetStoreOptions) {
                PersistenceMetadata = CreatePersistenceMetadata(),
            });
    }

    [Test]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "The dedicated STA test thread must transfer native failures after owner cleanup.")]
    public async Task WindowsPlatformOwnerIssuesHandlesForEnumeratedMonitors()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
            || (RuntimeInformation.ProcessArchitecture != Architecture.X64))
        {
            Assert.Ignore("The Windows Platform backend requires Windows 11 x64.");
            return;
        }

        var options = new EngineHostOptions(
            HostProfile.Interactive,
            PlatformBackend.WindowsNative,
            GraphicsBackend.D3D12,
            D3D12GraphicsBackend.CreateRegistration(),
            CreateAssetStoreOptions()) {
            PersistenceMetadata = CreatePersistenceMetadata(),
        };
        var completion = new TaskCompletionSource<(uint OwnerId, ImmutableArray<MonitorHandle> Monitors)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => {
            PlatformHostOwner? owner = null;
            var ownerId = 0U;
            var monitors = ImmutableArray<MonitorHandle>.Empty;
            Exception? failure = null;

            try
            {
                owner = PlatformHostOwner.Create(options);
                ownerId = owner.OwnerId;
                monitors = owner.Platform.GetMonitorHandles();
                _ = owner.Pump();

                foreach (var monitor in monitors)
                {
                    _ = owner.GetNativeMonitorHandle(monitor);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            try
            {
                owner?.DisposeFromOwner();
            }
            catch (Exception cleanupException)
            {
                failure = failure is null
                    ? cleanupException
                    : new AggregateException(failure, cleanupException);
            }

            if (failure is null)
            {
                _ = completion.TrySetResult((ownerId, monitors));
            }
            else
            {
                _ = completion.TrySetException(failure);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var (completedOwnerId, monitorHandles) = await completion.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        Assert.That(thread.Join(TimeSpan.FromSeconds(5)), Is.True);

        if (monitorHandles.IsDefaultOrEmpty)
        {
            Assert.Ignore("The Windows test host has no active display monitors.");
            return;
        }

        Assert.That(completedOwnerId, Is.GreaterThan(0));
        var indices = new HashSet<uint>();

        foreach (var monitor in monitorHandles)
        {
            Assert.That(monitor.IsValid, Is.True);
            Assert.That(monitor.OwnerId, Is.EqualTo(completedOwnerId));
            Assert.That(indices.Add(monitor.Index), Is.True);
        }
    }

    [Test]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "The dedicated STA test thread must transfer native failures after owner cleanup.")]
    public async Task WindowsPlatformOwnerWitnessesHiddenWindowCreationAndRetirement()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
            || (RuntimeInformation.ProcessArchitecture != Architecture.X64))
        {
            Assert.Ignore("The Windows Platform backend requires Windows 11 x64.");
            return;
        }

        var options = new EngineHostOptions(
            HostProfile.Interactive,
            PlatformBackend.WindowsNative,
            GraphicsBackend.D3D12,
            D3D12GraphicsBackend.CreateRegistration(),
            CreateAssetStoreOptions()) {
            PersistenceMetadata = CreatePersistenceMetadata(),
        };
        var completion = new TaskCompletionSource<(
            WindowHandle Window,
            uint OwnerId,
            bool StaleCloseRejected,
            bool StaleMetricsRejected,
            WindowMetrics? InitialMetrics,
            WindowMetrics? ResizedMetrics)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => {
            PlatformHostOwner? owner = null;
            var window = default(WindowHandle);
            var ownerId = 0U;
            var staleCloseRejected = false;
            var staleMetricsRejected = false;
            WindowMetrics? initialMetrics = null;
            WindowMetrics? resizedMetrics = null;
            Exception? failure = null;

            try
            {
                owner = PlatformHostOwner.Create(options);
                ownerId = owner.OwnerId;
                window = owner.CreateWindow(new WindowOptions("TerraFX hidden window witness") {
                    ClientSize = new Vector2(640.0f, 480.0f),
                    Visible = false,
                });
                _ = owner.Pump();
                initialMetrics = owner.Platform.GetMetrics(window);

                var binding = owner.GetNativeWindowBinding(window);
                var initialClientPixels = initialMetrics.ClientPixelSize;
                var requestedOuterWidth = checked(initialClientPixels.Width + 256);
                var requestedOuterHeight = checked(initialClientPixels.Height + 256);

                if (!WindowsTestNativeMethods.SetWindowPos(
                    binding.NativeWindowOrSurface,
                    insertAfter: 0,
                    x: 0,
                    y: 0,
                    width: requestedOuterWidth,
                    height: requestedOuterHeight,
                    flags: WindowsTestNativeMethods.WindowPositionNoMove
                        | WindowsTestNativeMethods.WindowPositionNoZOrder
                        | WindowsTestNativeMethods.WindowPositionNoActivate))
                {
                    throw new Win32Exception(
                        Marshal.GetLastPInvokeError(),
                        "The Windows metrics witness could not resize its hidden HWND.");
                }

                _ = owner.Pump();
                resizedMetrics = owner.Platform.GetMetrics(window);
                owner.CloseWindow(window);
                _ = owner.Pump();

                try
                {
                    _ = owner.Platform.GetMetrics(window);
                }
                catch (InvalidOperationException)
                {
                    staleMetricsRejected = true;
                }

                try
                {
                    owner.CloseWindow(window);
                }
                catch (InvalidOperationException)
                {
                    staleCloseRejected = true;
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            try
            {
                owner?.DisposeFromOwner();
            }
            catch (Exception cleanupException)
            {
                failure = failure is null
                    ? cleanupException
                    : new AggregateException(failure, cleanupException);
            }

            if (failure is null)
            {
                _ = completion.TrySetResult((
                    window,
                    ownerId,
                    staleCloseRejected,
                    staleMetricsRejected,
                    initialMetrics,
                    resizedMetrics));
            }
            else
            {
                _ = completion.TrySetException(failure);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var (window, ownerId, staleCloseRejected, staleMetricsRejected, initialMetrics, resizedMetrics) =
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        Assert.That(thread.Join(TimeSpan.FromSeconds(5)), Is.True);
        Assert.That(ownerId, Is.GreaterThan(0));
        Assert.That(window.IsValid, Is.True);
        Assert.That(window.OwnerId, Is.EqualTo(ownerId));
        Assert.That(staleCloseRejected, Is.True);
        Assert.That(staleMetricsRejected, Is.True);

        var actualInitialMetrics = initialMetrics
            ?? throw new InvalidOperationException("The Windows Platform owner did not publish the initial metrics snapshot.");
        var actualResizedMetrics = resizedMetrics
            ?? throw new InvalidOperationException("The Windows Platform owner did not publish the resized metrics snapshot.");
        var effectiveDpi = actualResizedMetrics.EffectiveDpi
            ?? throw new InvalidOperationException("The Windows Platform owner did not report the effective window DPI.");
        var expectedScale = effectiveDpi / 96.0f;
        Assert.That(actualInitialMetrics.ClientPixelSize.Width, Is.GreaterThan(0));
        Assert.That(actualInitialMetrics.ClientPixelSize.Height, Is.GreaterThan(0));
        Assert.That(actualInitialMetrics.DpiSource, Is.EqualTo(DpiSource.WindowsPerMonitorV2));
        Assert.That(actualInitialMetrics.DpiConfidence, Is.EqualTo(DpiConfidence.Reported));
        Assert.That(actualInitialMetrics.LogicalClientSize.X, Is.EqualTo(640.0f).Within(0.5f));
        Assert.That(actualInitialMetrics.LogicalClientSize.Y, Is.EqualTo(480.0f).Within(0.5f));
        Assert.That(actualResizedMetrics, Is.Not.SameAs(actualInitialMetrics));
        Assert.That(actualResizedMetrics.ClientPixelSize.Width, Is.GreaterThan(actualInitialMetrics.ClientPixelSize.Width));
        Assert.That(actualResizedMetrics.ClientPixelSize.Height, Is.GreaterThan(actualInitialMetrics.ClientPixelSize.Height));
        Assert.That(actualResizedMetrics.DpiSource, Is.EqualTo(DpiSource.WindowsPerMonitorV2));
        Assert.That(actualResizedMetrics.DpiConfidence, Is.EqualTo(DpiConfidence.Reported));
        Assert.That(actualResizedMetrics.ContentScale.X, Is.EqualTo(expectedScale).Within(0.0001f));
        Assert.That(actualResizedMetrics.ContentScale.Y, Is.EqualTo(expectedScale).Within(0.0001f));
        Assert.That(actualResizedMetrics.LogicalClientSize.X * actualResizedMetrics.ContentScale.X,
            Is.EqualTo(actualResizedMetrics.ClientPixelSize.Width).Within(0.01f));
        Assert.That(actualResizedMetrics.LogicalClientSize.Y * actualResizedMetrics.ContentScale.Y,
            Is.EqualTo(actualResizedMetrics.ClientPixelSize.Height).Within(0.01f));
        Assert.That(actualResizedMetrics.SurfaceEpoch, Is.Zero);
        Assert.That(actualResizedMetrics.SwapchainEpoch, Is.Zero);
    }

    [Test]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "The dedicated STA test thread must transfer native failures after owner cleanup.")]
    public async Task WindowsPlatformOwnerWitnessesPerMonitorDpiTransition()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
            || (RuntimeInformation.ProcessArchitecture != Architecture.X64))
        {
            Assert.Ignore("The Windows Platform backend requires Windows 11 x64.");
            return;
        }

        var options = new EngineHostOptions(
            HostProfile.Interactive,
            PlatformBackend.WindowsNative,
            GraphicsBackend.D3D12,
            D3D12GraphicsBackend.CreateRegistration(),
            CreateAssetStoreOptions()) {
            PersistenceMetadata = CreatePersistenceMetadata(),
        };
        var completion = new TaskCompletionSource<(
            uint OwnerId,
            ImmutableArray<float> ObservedDpis,
            float? SourceDpi,
            float? TargetDpi,
            nint InitialNativeMonitor,
            nint SourceNativeMonitor,
            nint TargetNativeMonitor,
            nint FinalNativeMonitor,
            WindowMetrics? InitialMetrics,
            WindowMetrics? MovedMetrics)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => {
            PlatformHostOwner? owner = null;
            var ownerId = 0U;
            var observedDpis = ImmutableArray<float>.Empty;
            float? sourceDpi = null;
            float? targetDpi = null;
            nint initialNativeMonitor = 0;
            nint sourceNativeMonitor = 0;
            nint targetNativeMonitor = 0;
            nint finalNativeMonitor = 0;
            WindowMetrics? initialMetrics = null;
            WindowMetrics? movedMetrics = null;
            Exception? failure = null;

            try
            {
                owner = PlatformHostOwner.Create(options);
                ownerId = owner.OwnerId;
                var monitors = owner.Platform.GetMonitorHandles();
                var monitorDpiBuilder = ImmutableArray.CreateBuilder<(MonitorHandle Monitor, float Dpi)>(monitors.Length);

                foreach (var monitor in monitors)
                {
                    var probeWindow = owner.CreateWindow(new WindowOptions("TerraFX monitor DPI probe") {
                        ClientSize = new Vector2(160.0f, 120.0f),
                        Monitor = monitor,
                        Visible = false,
                    });
                    _ = owner.Pump();
                    var effectiveDpi = owner.Platform.GetMetrics(probeWindow).EffectiveDpi
                        ?? throw new InvalidOperationException("Windows did not report effective DPI for a monitor probe window.");
                    monitorDpiBuilder.Add((monitor, effectiveDpi));
                    owner.CloseWindow(probeWindow);
                    _ = owner.Pump();
                }

                var observedDpiBuilder = ImmutableArray.CreateBuilder<float>(monitorDpiBuilder.Count);

                foreach (var monitorDpi in monitorDpiBuilder)
                {
                    observedDpiBuilder.Add(monitorDpi.Dpi);
                }

                observedDpis = observedDpiBuilder.MoveToImmutable();

                var sourceMonitor = default(MonitorHandle);
                var targetMonitor = default(MonitorHandle);

                for (var sourceIndex = 0; sourceIndex < monitorDpiBuilder.Count && !sourceMonitor.IsValid; sourceIndex++)
                {
                    for (var targetIndex = sourceIndex + 1; targetIndex < monitorDpiBuilder.Count; targetIndex++)
                    {
                        var source = monitorDpiBuilder[sourceIndex];
                        var target = monitorDpiBuilder[targetIndex];

                        if (source.Dpi != target.Dpi)
                        {
                            sourceMonitor = source.Monitor;
                            targetMonitor = target.Monitor;
                            sourceDpi = source.Dpi;
                            targetDpi = target.Dpi;
                            break;
                        }
                    }
                }

                if (sourceMonitor.IsValid)
                {
                    var window = owner.CreateWindow(new WindowOptions("TerraFX per-monitor DPI witness") {
                        ClientSize = new Vector2(160.0f, 120.0f),
                        Monitor = sourceMonitor,
                        Visible = false,
                    });
                    _ = owner.Pump();
                    initialMetrics = owner.Platform.GetMetrics(window);

                    var binding = owner.GetNativeWindowBinding(window);
                    initialNativeMonitor = WindowsTestNativeMethods.MonitorFromWindow(
                        binding.NativeWindowOrSurface,
                        WindowsTestNativeMethods.MonitorDefaultToNearest);
                    sourceNativeMonitor = owner.GetNativeMonitorHandle(sourceMonitor);
                    targetNativeMonitor = owner.GetNativeMonitorHandle(targetMonitor);
                    var targetBounds = new WindowsTestNativeMethods.MonitorInfo {
                        Size = checked((uint)Marshal.SizeOf<WindowsTestNativeMethods.MonitorInfo>()),
                    };

                    if (!WindowsTestNativeMethods.GetMonitorInfo(targetNativeMonitor, ref targetBounds))
                    {
                        throw new Win32Exception(
                            Marshal.GetLastPInvokeError(),
                            "The Windows DPI witness could not read the target monitor bounds.");
                    }

                    if (!WindowsTestNativeMethods.GetWindowRect(binding.NativeWindowOrSurface, out var windowBounds))
                    {
                        throw new Win32Exception(
                            Marshal.GetLastPInvokeError(),
                            "The Windows DPI witness could not read the hidden HWND bounds.");
                    }

                    var targetWidth = targetBounds.Monitor.Right - targetBounds.Monitor.Left;
                    var targetHeight = targetBounds.Monitor.Bottom - targetBounds.Monitor.Top;

                    if ((targetWidth <= 2) || (targetHeight <= 2))
                    {
                        throw new InvalidOperationException("Windows returned invalid bounds for the target monitor.");
                    }

                    var width = windowBounds.Right - windowBounds.Left;
                    var height = windowBounds.Bottom - windowBounds.Top;

                    if ((width <= 0) || (height <= 0))
                    {
                        throw new InvalidOperationException("Windows returned invalid bounds for the DPI witness window.");
                    }

                    var x = targetBounds.Monitor.Left + ((targetWidth - width) / 2);
                    var y = targetBounds.Monitor.Top + ((targetHeight - height) / 2);

                    if (!WindowsTestNativeMethods.SetWindowPos(
                        binding.NativeWindowOrSurface,
                        insertAfter: 0,
                        x,
                        y,
                        width,
                        height,
                        WindowsTestNativeMethods.WindowPositionNoZOrder
                            | WindowsTestNativeMethods.WindowPositionNoActivate))
                    {
                        throw new Win32Exception(
                            Marshal.GetLastPInvokeError(),
                            "The Windows DPI witness could not move its hidden HWND to the target monitor.");
                    }

                    _ = owner.Pump();
                    movedMetrics = owner.Platform.GetMetrics(window);
                    finalNativeMonitor = WindowsTestNativeMethods.MonitorFromWindow(
                        binding.NativeWindowOrSurface,
                        WindowsTestNativeMethods.MonitorDefaultToNearest);
                    owner.CloseWindow(window);
                    _ = owner.Pump();
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            try
            {
                owner?.DisposeFromOwner();
            }
            catch (Exception cleanupException)
            {
                failure = failure is null
                    ? cleanupException
                    : new AggregateException(failure, cleanupException);
            }

            if (failure is null)
            {
                _ = completion.TrySetResult((
                    ownerId,
                    observedDpis,
                    sourceDpi,
                    targetDpi,
                    initialNativeMonitor,
                    sourceNativeMonitor,
                    targetNativeMonitor,
                    finalNativeMonitor,
                    initialMetrics,
                    movedMetrics));
            }
            else
            {
                _ = completion.TrySetException(failure);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var (
            ownerId,
            observedDpis,
            sourceDpi,
            targetDpi,
            initialNativeMonitor,
            sourceNativeMonitor,
            targetNativeMonitor,
            finalNativeMonitor,
            initialMetrics,
            movedMetrics) = await completion.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        Assert.That(thread.Join(TimeSpan.FromSeconds(5)), Is.True);
        Assert.That(ownerId, Is.GreaterThan(0));

        if ((sourceDpi is not float expectedSourceDpi)
            || (targetDpi is not float expectedTargetDpi))
        {
            Assert.Ignore(
                $"The Windows test host has no pair of monitors with distinct effective DPI values "
                    + $"(observed: {string.Join(", ", observedDpis)}).");
            return;
        }

        var actualInitialMetrics = initialMetrics
            ?? throw new InvalidOperationException("The Windows DPI witness did not publish its initial metrics snapshot.");
        var actualMovedMetrics = movedMetrics
            ?? throw new InvalidOperationException("The Windows DPI witness did not publish its moved metrics snapshot.");
        Assert.That(expectedSourceDpi, Is.Not.EqualTo(expectedTargetDpi));
        Assert.That(initialNativeMonitor, Is.Not.Zero);
        Assert.That(initialNativeMonitor, Is.EqualTo(sourceNativeMonitor));
        Assert.That(initialNativeMonitor, Is.Not.EqualTo(finalNativeMonitor));
        Assert.That(finalNativeMonitor, Is.EqualTo(targetNativeMonitor));
        Assert.That(actualInitialMetrics.EffectiveDpi, Is.EqualTo(expectedSourceDpi));
        Assert.That(actualMovedMetrics.EffectiveDpi, Is.EqualTo(expectedTargetDpi));
        Assert.That(actualMovedMetrics, Is.Not.SameAs(actualInitialMetrics));
        Assert.That(actualInitialMetrics.DpiSource, Is.EqualTo(DpiSource.WindowsPerMonitorV2));
        Assert.That(actualMovedMetrics.DpiSource, Is.EqualTo(DpiSource.WindowsPerMonitorV2));
        Assert.That(actualInitialMetrics.DpiConfidence, Is.EqualTo(DpiConfidence.Reported));
        Assert.That(actualMovedMetrics.DpiConfidence, Is.EqualTo(DpiConfidence.Reported));
        Assert.That(actualMovedMetrics.LogicalClientSize.X, Is.EqualTo(actualInitialMetrics.LogicalClientSize.X).Within(0.5f));
        Assert.That(actualMovedMetrics.LogicalClientSize.Y, Is.EqualTo(actualInitialMetrics.LogicalClientSize.Y).Within(0.5f));
        Assert.That(actualMovedMetrics.ContentScale.X, Is.EqualTo(expectedTargetDpi / 96.0f).Within(0.0001f));
        Assert.That(actualMovedMetrics.ContentScale.Y, Is.EqualTo(expectedTargetDpi / 96.0f).Within(0.0001f));
        Assert.That(actualMovedMetrics.LogicalClientSize.X * actualMovedMetrics.ContentScale.X,
            Is.EqualTo(actualMovedMetrics.ClientPixelSize.Width).Within(0.01f));
        Assert.That(actualMovedMetrics.LogicalClientSize.Y * actualMovedMetrics.ContentScale.Y,
            Is.EqualTo(actualMovedMetrics.ClientPixelSize.Height).Within(0.01f));
    }

    private static partial class WindowsTestNativeMethods
    {
        internal const uint MonitorDefaultToNearest = 0x00000002;
        internal const uint WindowPositionNoActivate = 0x0010;
        internal const uint WindowPositionNoMove = 0x0002;
        internal const uint WindowPositionNoZOrder = 0x0004;

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetWindowRect(nint window, out NativeRect rect);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("user32.dll", EntryPoint = "MonitorFromWindow")]
        internal static partial nint MonitorFromWindow(nint window, uint flags);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetWindowPos(
            nint window,
            nint insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("kernel32.dll", EntryPoint = "WaitForSingleObject", SetLastError = true)]
        internal static partial uint WaitForSingleObject(nint waitHandle, uint milliseconds);

        [StructLayout(LayoutKind.Sequential)]
        internal struct MonitorInfo
        {
            internal uint Size;
            internal NativeRect Monitor;
            internal NativeRect Work;
            internal uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeRect
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
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
