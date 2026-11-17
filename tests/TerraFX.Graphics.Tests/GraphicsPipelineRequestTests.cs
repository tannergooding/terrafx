// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Buffers;
using TerraFX.Graphics;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class GraphicsPipelineRequestTests
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference<GraphicsResourceOwner.UseLease> Use, WeakReference<GraphicsRecordingUseAdmission> Admission) AcquireUseWithoutCallerRoots(
        GraphicsResourceOwner owner)
    {
        var admission = GraphicsRecordingUseAdmission.Create(1, 272);
        var use = owner.AcquireUse("retained recording use", admission);
        return (new WeakReference<GraphicsResourceOwner.UseLease>(use), new WeakReference<GraphicsRecordingUseAdmission>(admission));
    }

    private static GraphicsCapabilities CreateCapabilities(GraphicsLimits? limits = null)
    {
        var features = ImmutableArray.Create(
            GraphicsFeature.Direct3DFeatureLevel12,
            GraphicsFeature.ShaderModel6);

        return new GraphicsCapabilities(
            GraphicsBackend.D3D12,
            GraphicsDeviceProfile.OffscreenNative,
            new GraphicsAdapterId(GraphicsBackend.D3D12, [0, 1, 2, 3, 4, 5, 6, 7]),
            vendorId: 1,
            deviceId: 1,
            adapterName: "Pipeline request test adapter",
            driverName: "Pipeline request test driver",
            driverVersion: "1",
            new GraphicsApiVersion(12, 0, 0, 0),
            features,
            features,
            limits ?? CreateLimits(),
            CreateFormats(),
            [new GraphicsMemoryHeapCapabilities(
                0,
                GraphicsMemoryHeapProperties.DeviceLocal,
                4L * 1024 * 1024 * 1024,
                3L * 1024 * 1024 * 1024)]);
    }

    private static ShaderCompilerIdentity CreateCompilerIdentity()
    {
        var digest = new byte[32];
        Array.Fill(digest, (byte)0xA5);

        return new ShaderCompilerIdentity(
            "Microsoft.Direct3D.DXC",
            "1.8.2505.1",
            "1.8.2505.32",
            "win-x64",
            ImmutableCollectionsMarshal.AsImmutableArray(digest));
    }

    private static ComputePipelineDescription CreateComputePipelineDescription(
        BufferReadLease<byte> bytecode,
        ImmutableArray<ShaderResourceSignature> resources,
        int groupSizeX = 1,
        int groupSizeY = 1,
        int groupSizeZ = 1,
        ImmutableArray<ShaderCapability> requiredCapabilities = default)
    {
        var reflection = new ShaderReflection(
            ShaderStage.Compute,
            "CSMain",
            CreateCompilerIdentity(),
            [],
            [],
            resources,
            [],
            requiredCapabilities.IsDefault ? [] : requiredCapabilities,
            bindingLayoutVersion: 1,
            groupSizeX: groupSizeX,
            groupSizeY: groupSizeY,
            groupSizeZ: groupSizeZ);
        var shader = new ShaderBytecodeInput(ShaderStage.Compute, ShaderBinaryFormat.Dxil, bytecode, reflection);
        return new ComputePipelineDescription(shader, bindingLayoutVersion: 1);
    }

    private static ImmutableArray<GraphicsFormatCapabilities> CreateFormats()
        => [
            new GraphicsFormatCapabilities(TextureFormat.Rgba8Unorm, TextureUsage.Sampled | TextureUsage.ColorAttachment, [1]),
            new GraphicsFormatCapabilities(TextureFormat.Rgba8Srgb, TextureUsage.Sampled | TextureUsage.ColorAttachment, [1]),
            new GraphicsFormatCapabilities(TextureFormat.Rgba16Float, TextureUsage.Sampled | TextureUsage.Storage | TextureUsage.ColorAttachment, [1]),
            new GraphicsFormatCapabilities(TextureFormat.R8Unorm, TextureUsage.Sampled, [1]),
            new GraphicsFormatCapabilities(TextureFormat.R32Float, TextureUsage.Sampled | TextureUsage.Storage, [1]),
            new GraphicsFormatCapabilities(TextureFormat.R32UInt, TextureUsage.Storage | TextureUsage.ColorAttachment, [1]),
            new GraphicsFormatCapabilities(TextureFormat.D32Float, TextureUsage.DepthAttachment | TextureUsage.Sampled, [1]),
        ];

    private static GraphicsLimits CreateLimits(
        int maximumComputeWorkGroupSizeX = 1024,
        int maximumComputeWorkGroupSizeY = 1024,
        int maximumComputeWorkGroupSizeZ = 64,
        int maximumComputeWorkGroupCountX = 65535,
        int maximumComputeWorkGroupCountY = 65535,
        int maximumComputeWorkGroupCountZ = 65535,
        int maximumComputeWorkGroupInvocations = 1024,
        int maximumSampledImagesPerStage = 128,
        int maximumSampledImagesPerPipeline = 64,
        int maximumSamplersPerStage = 32,
        int maximumSamplersPerPipeline = 32,
        int maximumUniformBuffersPerStage = 16,
        int maximumUniformBuffersPerPipeline = 16,
        int maximumStorageBuffersPerStage = 32,
        int maximumStorageImagesPerStage = 32,
        int maximumStorageBuffersPerPipeline = 32,
        int maximumStorageImagesPerPipeline = 32,
        int maximumStorageResourcesPerPipeline = 32,
        int maximumResourcesPerStage = 144)
        => new(
            maximumTextureDimension2D: 16384,
            maximumTextureDimension3D: 2048,
            maximumArrayLayers: 2048,
            maximumColorAttachments: 8,
            maximumSampledImagesPerStage: maximumSampledImagesPerStage,
            maximumSampledImagesPerPipeline: maximumSampledImagesPerPipeline,
            maximumSamplersPerStage: maximumSamplersPerStage,
            maximumSamplersPerPipeline: maximumSamplersPerPipeline,
            maximumSamplerAnisotropy: 1.0f,
            maximumUniformBuffersPerStage: maximumUniformBuffersPerStage,
            maximumUniformBuffersPerPipeline: maximumUniformBuffersPerPipeline,
            maximumStorageBuffersPerStage: maximumStorageBuffersPerStage,
            maximumStorageImagesPerStage: maximumStorageImagesPerStage,
            maximumStorageBuffersPerPipeline: maximumStorageBuffersPerPipeline,
            maximumStorageImagesPerPipeline: maximumStorageImagesPerPipeline,
            maximumStorageResourcesPerPipeline: maximumStorageResourcesPerPipeline,
            maximumResourcesPerStage: maximumResourcesPerStage,
            maximumUniformBufferRangeBytes: 65536,
            uniformBufferOffsetAlignment: 256,
            storageBufferOffsetAlignment: 16,
            bufferCopyOffsetAlignment: 16,
            textureRowPitchAlignment: 256,
            texturePlacementAlignment: 512,
            maximumComputeWorkGroupSizeX: maximumComputeWorkGroupSizeX,
            maximumComputeWorkGroupSizeY: maximumComputeWorkGroupSizeY,
            maximumComputeWorkGroupSizeZ: maximumComputeWorkGroupSizeZ,
            maximumComputeWorkGroupCountX: maximumComputeWorkGroupCountX,
            maximumComputeWorkGroupCountY: maximumComputeWorkGroupCountY,
            maximumComputeWorkGroupCountZ: maximumComputeWorkGroupCountZ,
            maximumComputeWorkGroupInvocations: maximumComputeWorkGroupInvocations,
            maximumVertexInputAttributes: 32,
            maximumVertexInputBindings: 32,
            maximumBufferByteLength: 1L << 40);

    private static GraphicsPipelineDescription CreatePipelineDescription(
        BufferReadLease<byte> bytecode,
        ImmutableArray<ShaderResourceSignature> vertexResources = default,
        ImmutableArray<ShaderResourceSignature> pixelResources = default)
    {
        var compilerIdentity = CreateCompilerIdentity();
        var vertexReflection = new ShaderReflection(
            ShaderStage.Vertex,
            "VSMain",
            compilerIdentity,
            [new ShaderIoSignature("SV_VertexID", 0, null, ShaderValueType.UInt1, ShaderInterpolation.Flat)],
            [new ShaderIoSignature("SV_Position", 0, null, ShaderValueType.Float4, ShaderInterpolation.Smooth)],
            vertexResources.IsDefault ? [] : vertexResources,
            [],
            [],
            bindingLayoutVersion: 1,
            groupSizeX: 0,
            groupSizeY: 0,
            groupSizeZ: 0);
        var pixelReflection = new ShaderReflection(
            ShaderStage.Pixel,
            "PSMain",
            compilerIdentity,
            [],
            [new ShaderIoSignature("SV_Target", 0, null, ShaderValueType.Float4, ShaderInterpolation.Smooth)],
            pixelResources.IsDefault ? [] : pixelResources,
            [],
            [],
            bindingLayoutVersion: 1,
            groupSizeX: 0,
            groupSizeY: 0,
            groupSizeZ: 0);
        var vertexShader = new ShaderBytecodeInput(ShaderStage.Vertex, ShaderBinaryFormat.Dxil, bytecode, vertexReflection);
        var pixelShader = new ShaderBytecodeInput(ShaderStage.Pixel, ShaderBinaryFormat.Dxil, bytecode, pixelReflection);

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

    private static PipelineDescription CreatePipelineForStage(
        BufferReadLease<byte> bytecode,
        ShaderStage stage,
        ImmutableArray<ShaderResourceSignature> resources)
        => stage switch {
            ShaderStage.Vertex => CreatePipelineDescription(bytecode, vertexResources: resources),
            ShaderStage.Pixel => CreatePipelineDescription(bytecode, pixelResources: resources),
            ShaderStage.Compute => CreateComputePipelineDescription(bytecode, resources),
            _ => throw new ArgumentOutOfRangeException(nameof(stage)),
        };

    private static ImmutableArray<ShaderResourceSignature> CreateResourceSignatures(
        ShaderResourceKind kind,
        int count)
    {
        var maximumBinding = kind switch {
            ShaderResourceKind.UniformBuffer => 3,
            ShaderResourceKind.SampledImage => 15,
            ShaderResourceKind.StorageBuffer or ShaderResourceKind.StorageImage or ShaderResourceKind.Sampler => 7,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var resources = ImmutableArray.CreateBuilder<ShaderResourceSignature>();

        foreach (var group in new[] {
            ShaderBindingGroup.Frame,
            ShaderBindingGroup.Material,
            ShaderBindingGroup.ObjectData,
            ShaderBindingGroup.Pass,
        })
        {
            for (var binding = 0; binding <= maximumBinding; binding++)
            {
                if (resources.Count == count)
                {
                    return resources.ToImmutable();
                }

                resources.Add(new ShaderResourceSignature(group, binding, kind, arrayCount: 1));
            }
        }

        return resources.ToImmutable();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (Task<RecordedPassCommandList> Task, WeakReference<byte[]> Capture) StartRecordingWithCapture(GraphicsDevice device)
    {
        var payload = new byte[4096];
        var weak = new WeakReference<byte[]>(payload);

        void Record(scoped ref CommandRecorder recorder)
        {
            GC.KeepAlive(payload);
        }

        return (device.RecordPassAsync(new RenderPassId(200, 0), CommandRecorderPassKind.Copy, [], [], null, Record), weak);
    }

    [Test]
    public async Task CompletedRecordingTaskDoesNotRetainTheCallbackCapture()
    {
        using var proceed = new ManualResetEventSlim(initialState: true);
        using var entered = new ManualResetEventSlim(initialState: false);
        var backend = new FakeBackendDevice(CreateCapabilities(), entered, proceed);
        var device = await GraphicsDevice.CreateAsync(
            31,
            new GraphicsBackendRegistration(new FakeBackendFactory(backend)),
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None,
            CancellationToken.None).ConfigureAwait(false);

        try
        {
            var recording = StartRecordingWithCapture(device);
            using var result = await recording.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            result.Dispose();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.That(recording.Capture.TryGetTarget(out _), Is.False);
            GC.KeepAlive(recording.Task);
        }
        finally
        {
            await device.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [TestCase(4, 4, 4, true)]
    [TestCase(17, 1, 1, false)]
    [TestCase(1, 17, 1, false)]
    [TestCase(1, 1, 17, false)]
    [TestCase(5, 4, 4, false)]
    public async Task ComputePipelineGroupSizeRespectsDeviceLimits(
        int groupSizeX,
        int groupSizeY,
        int groupSizeZ,
        bool expectedAdmission)
    {
        var limits = CreateLimits(
            maximumComputeWorkGroupSizeX: 16,
            maximumComputeWorkGroupSizeY: 16,
            maximumComputeWorkGroupSizeZ: 16,
            maximumComputeWorkGroupInvocations: 64);
        using var continuePipelineCreation = new ManualResetEventSlim(initialState: true);
        using var pipelineCreationEntered = new ManualResetEventSlim(initialState: false);
        var backendDevice = new FakeBackendDevice(
            CreateCapabilities(limits),
            pipelineCreationEntered,
            continuePipelineCreation);
        var registration = new GraphicsBackendRegistration(new FakeBackendFactory(backendDevice));
        var graphicsDevice = await GraphicsDevice.CreateAsync(
            ownerId: 20,
            registration,
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None,
            CancellationToken.None).ConfigureAwait(false);
        var bufferOwner = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(64, 1));
        var callerBytecodeLease = bufferOwner.AcquireRead(0, 4);
        GpuPipeline? pipeline = null;

        try
        {
            var description = CreateComputePipelineDescription(
                callerBytecodeLease,
                [],
                groupSizeX,
                groupSizeY,
                groupSizeZ);

            if (expectedAdmission)
            {
                pipeline = await graphicsDevice.CreatePipelineAsync(description)
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(pipeline.Handle.IsValid, Is.True);
                Assert.That(backendDevice.PipelineCreationCount, Is.EqualTo(1));
            }
            else
            {
                _ = Assert.ThrowsAsync<NotSupportedException>(
                    async () => _ = await graphicsDevice.CreatePipelineAsync(description)
                        .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
                Assert.That(backendDevice.PipelineCreationCount, Is.Zero);
            }
        }
        finally
        {
            pipeline?.Dispose();
            callerBytecodeLease.Dispose();
            bufferOwner.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task CreateAsyncPreservesOwnerIdAndRequestedDeviceEpoch()
    {
        using var continuePipelineCreation = new ManualResetEventSlim(initialState: true);
        using var pipelineCreationEntered = new ManualResetEventSlim(initialState: false);
        var backendDevice = new FakeBackendDevice(
            CreateCapabilities(),
            pipelineCreationEntered,
            continuePipelineCreation);
        var registration = new GraphicsBackendRegistration(new FakeBackendFactory(backendDevice));
        var graphicsDevice = await GraphicsDevice.CreateAsync(
            ownerId: 50,
            registration: registration,
            profile: GraphicsDeviceProfile.OffscreenNative,
            frameSlotCount: 3,
            platformKind: GraphicsPlatformKind.None,
            presentationProbeBinding: null,
            deviceEpoch: 4,
            cancellationToken: CancellationToken.None).ConfigureAwait(false);

        try
        {
            Assert.That(graphicsDevice.OwnerId, Is.EqualTo(50));
            Assert.That(graphicsDevice.DeviceEpoch, Is.EqualTo(4));
            Assert.That(graphicsDevice.Status, Is.EqualTo(GraphicsDeviceStatus.Ready));
        }
        finally
        {
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task DeviceLossResponseMarksOwnerLostAndRejectsFuturePipelineRequests()
    {
        using var continuePipelineCreation = new ManualResetEventSlim(initialState: true);
        using var pipelineCreationEntered = new ManualResetEventSlim(initialState: false);
        const int nativeError = unchecked((int)0x887A0005);
        var backendDevice = new FakeBackendDevice(
            CreateCapabilities(),
            pipelineCreationEntered,
            continuePipelineCreation,
            new GraphicsDeviceLostException(nativeError));
        var registration = new GraphicsBackendRegistration(new FakeBackendFactory(backendDevice));
        var graphicsDevice = await GraphicsDevice.CreateAsync(
            ownerId: 50,
            registration,
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None,
            CancellationToken.None).ConfigureAwait(false);
        int? callbackNativeError = null;
        var callbackCount = 0;
        graphicsDevice.SetDeviceLostCallback(nativeResult => {
            callbackNativeError = nativeResult;
            callbackCount++;
        });
        var bufferOwner = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(64, 1));
        var callerBytecodeLease = bufferOwner.AcquireRead(0, 4);

        try
        {
            var description = CreateComputePipelineDescription(callerBytecodeLease, []);
            _ = Assert.ThrowsAsync<GraphicsDeviceLostException>(
                async () => await graphicsDevice.CreatePipelineAsync(description)
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));

            Assert.That(graphicsDevice.Status, Is.EqualTo(GraphicsDeviceStatus.Lost));
            Assert.That(callbackNativeError, Is.EqualTo(nativeError));
            Assert.That(callbackCount, Is.EqualTo(1));
            Assert.That(backendDevice.PipelineCreationCount, Is.EqualTo(1));
            _ = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await graphicsDevice.CreatePipelineAsync(description)
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
            Assert.That(backendDevice.PipelineCreationCount, Is.EqualTo(1));
        }
        finally
        {
            callerBytecodeLease.Dispose();
            bufferOwner.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task DiagnosticRecordingQueueRefusesRatherThanWaitingBehindAFullOwner()
    {
        using var proceed = new ManualResetEventSlim(initialState: true);
        using var entered = new ManualResetEventSlim(initialState: false);
        using var callbackEntered = new ManualResetEventSlim(initialState: false);
        using var releaseCallback = new ManualResetEventSlim(initialState: false);
        var backend = new FakeBackendDevice(CreateCapabilities(), entered, proceed);
        var device = await GraphicsDevice.CreateAsync(
            31,
            new GraphicsBackendRegistration(new FakeBackendFactory(backend)),
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None,
            CancellationToken.None).ConfigureAwait(false);
        Task<RecordedPassCommandList>? blocker = null;
        var pending = new Task<RecordedPassCommandList>?[256];

        try
        {
            void Block(scoped ref CommandRecorder recorder)
            {
                callbackEntered.Set();
                releaseCallback.Wait();
            }

            blocker = device.RecordPassAsync(new RenderPassId(500, 0), CommandRecorderPassKind.Copy, [], [], null, Block);
            Assert.That(callbackEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);

            for (var index = 0; index < pending.Length; index++)
            {
                pending[index] = device.RecordPassAsync(
                    new RenderPassId(501, index),
                    CommandRecorderPassKind.Copy,
                    [],
                    [],
                    null,
                    static (scoped ref CommandRecorder recorder) => { });
            }

            var refused = device.RecordPassAsync(
                new RenderPassId(502, 0),
                CommandRecorderPassKind.Copy,
                [],
                [],
                null,
                static (scoped ref CommandRecorder recorder) => { },
                requiresDiagnosticQueueAdmission: true);
            Assert.That(refused.IsCompleted, Is.True);
            _ = Assert.ThrowsAsync<OutOfMemoryException>(async () => await refused.ConfigureAwait(false));
        }
        finally
        {
            releaseCallback.Set();

            if (blocker is not null)
            {
                using var result = await blocker.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }

            foreach (var task in pending)
            {
                if (task is not null)
                {
                    using var result = await task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
            }

            await device.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [TestCase(ShaderCapability.Float16)]
    [TestCase(ShaderCapability.DoublePrecision)]
    [TestCase(ShaderCapability.WaveOperations)]
    [TestCase(ShaderCapability.DescriptorIndexing)]
    [TestCase(ShaderCapability.GeometryShader)]
    [TestCase(ShaderCapability.Tessellation)]
    public async Task NonbaselineShaderCapabilitiesRejectBeforeBackendPipelineCreation(ShaderCapability capability)
    {
        using var continuePipelineCreation = new ManualResetEventSlim(initialState: true);
        using var pipelineCreationEntered = new ManualResetEventSlim(initialState: false);
        var backendDevice = new FakeBackendDevice(
            CreateCapabilities(),
            pipelineCreationEntered,
            continuePipelineCreation);
        var registration = new GraphicsBackendRegistration(new FakeBackendFactory(backendDevice));
        var graphicsDevice = await GraphicsDevice.CreateAsync(
            ownerId: 21,
            registration,
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None,
            CancellationToken.None).ConfigureAwait(false);
        var bufferOwner = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(64, 1));
        var callerBytecodeLease = bufferOwner.AcquireRead(0, 4);

        try
        {
            var description = CreateComputePipelineDescription(
                callerBytecodeLease,
                [],
                requiredCapabilities: [capability]);

            _ = Assert.ThrowsAsync<NotSupportedException>(
                async () => _ = await graphicsDevice.CreatePipelineAsync(description)
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
            Assert.That(backendDevice.PipelineCreationCount, Is.Zero);
        }
        finally
        {
            callerBytecodeLease.Dispose();
            bufferOwner.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [TestCase(ShaderStage.Vertex, ShaderResourceKind.SampledImage, 16)]
    [TestCase(ShaderStage.Vertex, ShaderResourceKind.Sampler, 8)]
    [TestCase(ShaderStage.Vertex, ShaderResourceKind.UniformBuffer, 8)]
    [TestCase(ShaderStage.Pixel, ShaderResourceKind.SampledImage, 16)]
    [TestCase(ShaderStage.Pixel, ShaderResourceKind.Sampler, 8)]
    [TestCase(ShaderStage.Pixel, ShaderResourceKind.UniformBuffer, 8)]
    [TestCase(ShaderStage.Compute, ShaderResourceKind.SampledImage, 16)]
    [TestCase(ShaderStage.Compute, ShaderResourceKind.Sampler, 8)]
    [TestCase(ShaderStage.Compute, ShaderResourceKind.UniformBuffer, 8)]
    [TestCase(ShaderStage.Vertex, ShaderResourceKind.StorageBuffer, 8)]
    [TestCase(ShaderStage.Vertex, ShaderResourceKind.StorageImage, 8)]
    [TestCase(ShaderStage.Pixel, ShaderResourceKind.StorageBuffer, 8)]
    [TestCase(ShaderStage.Pixel, ShaderResourceKind.StorageImage, 8)]
    [TestCase(ShaderStage.Compute, ShaderResourceKind.StorageBuffer, 8)]
    [TestCase(ShaderStage.Compute, ShaderResourceKind.StorageImage, 8)]
    public async Task PipelineBindingsRequireSupportedDescriptorCountsAndRespectStageLimits(
        ShaderStage stage,
        ShaderResourceKind kind,
        int maximumCount)
    {
        using var continuePipelineCreation = new ManualResetEventSlim(initialState: true);
        using var pipelineCreationEntered = new ManualResetEventSlim(initialState: false);
        var limits = kind switch {
            ShaderResourceKind.SampledImage => CreateLimits(
                maximumSampledImagesPerStage: maximumCount,
                maximumSampledImagesPerPipeline: maximumCount),
            ShaderResourceKind.Sampler => CreateLimits(
                maximumSamplersPerStage: maximumCount,
                maximumSamplersPerPipeline: maximumCount),
            ShaderResourceKind.UniformBuffer => CreateLimits(
                maximumUniformBuffersPerStage: maximumCount,
                maximumUniformBuffersPerPipeline: maximumCount),
            ShaderResourceKind.StorageBuffer => CreateLimits(
                maximumStorageBuffersPerStage: maximumCount,
                maximumStorageBuffersPerPipeline: maximumCount,
                maximumStorageResourcesPerPipeline: maximumCount),
            ShaderResourceKind.StorageImage => CreateLimits(
                maximumStorageImagesPerStage: maximumCount,
                maximumStorageImagesPerPipeline: maximumCount,
                maximumStorageResourcesPerPipeline: maximumCount),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var backendDevice = new FakeBackendDevice(
            CreateCapabilities(limits),
            pipelineCreationEntered,
            continuePipelineCreation);
        var registration = new GraphicsBackendRegistration(new FakeBackendFactory(backendDevice));
        var graphicsDevice = await GraphicsDevice.CreateAsync(
            ownerId: 19,
            registration,
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None,
            CancellationToken.None).ConfigureAwait(false);
        var bufferOwner = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(64, 1));
        var callerBytecodeLease = bufferOwner.AcquireRead(0, 4);
        GpuPipeline? pipeline = null;

        try
        {
            var arrayResource = new ShaderResourceSignature(
                ShaderBindingGroup.Material,
                binding: 0,
                kind,
                arrayCount: 2);
            _ = Assert.ThrowsAsync<NotSupportedException>(
                async () => _ = await graphicsDevice.CreatePipelineAsync(
                    CreatePipelineForStage(callerBytecodeLease, stage, [arrayResource]))
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
            Assert.That(backendDevice.PipelineCreationCount, Is.Zero);

            pipeline = await graphicsDevice.CreatePipelineAsync(
                CreatePipelineForStage(
                    callerBytecodeLease,
                    stage,
                    CreateResourceSignatures(kind, maximumCount)))
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(pipeline.Handle.IsValid, Is.True);
            Assert.That(backendDevice.PipelineCreationCount, Is.EqualTo(1));

            pipeline.Dispose();
            pipeline = null;

            _ = Assert.ThrowsAsync<NotSupportedException>(
                async () => _ = await graphicsDevice.CreatePipelineAsync(
                    CreatePipelineForStage(
                        callerBytecodeLease,
                        stage,
                        CreateResourceSignatures(kind, checked(maximumCount + 1))))
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
            Assert.That(backendDevice.PipelineCreationCount, Is.EqualTo(1));
        }
        finally
        {
            pipeline?.Dispose();
            callerBytecodeLease.Dispose();
            bufferOwner.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [TestCase(ShaderResourceKind.SampledImage, 16)]
    [TestCase(ShaderResourceKind.Sampler, 8)]
    [TestCase(ShaderResourceKind.UniformBuffer, 8)]
    [TestCase(ShaderResourceKind.StorageBuffer, 1)]
    [TestCase(ShaderResourceKind.StorageImage, 1)]
    public async Task PipelineBindingsRespectPipelineLayoutLimits(ShaderResourceKind kind, int maximumCount)
    {
        using var continuePipelineCreation = new ManualResetEventSlim(initialState: true);
        using var pipelineCreationEntered = new ManualResetEventSlim(initialState: false);
        var limits = kind switch {
            ShaderResourceKind.SampledImage => CreateLimits(maximumSampledImagesPerPipeline: maximumCount),
            ShaderResourceKind.Sampler => CreateLimits(maximumSamplersPerPipeline: maximumCount),
            ShaderResourceKind.UniformBuffer => CreateLimits(maximumUniformBuffersPerPipeline: maximumCount),
            ShaderResourceKind.StorageBuffer => CreateLimits(maximumStorageBuffersPerPipeline: maximumCount),
            ShaderResourceKind.StorageImage => CreateLimits(maximumStorageImagesPerPipeline: maximumCount),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var backendDevice = new FakeBackendDevice(
            CreateCapabilities(limits),
            pipelineCreationEntered,
            continuePipelineCreation);
        var registration = new GraphicsBackendRegistration(new FakeBackendFactory(backendDevice));
        var graphicsDevice = await GraphicsDevice.CreateAsync(
            ownerId: 23,
            registration,
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None,
            CancellationToken.None).ConfigureAwait(false);
        var bufferOwner = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(64, 1));
        var callerBytecodeLease = bufferOwner.AcquireRead(0, 4);

        try
        {
            var description = CreateComputePipelineDescription(
                callerBytecodeLease,
                CreateResourceSignatures(kind, checked(maximumCount + 1)));
            _ = Assert.ThrowsAsync<NotSupportedException>(
                async () => _ = await graphicsDevice.CreatePipelineAsync(description)
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
            Assert.That(backendDevice.PipelineCreationCount, Is.Zero);
        }
        finally
        {
            callerBytecodeLease.Dispose();
            bufferOwner.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task PipelineBindingsRespectStageDescriptorTotals()
    {
        using var continuePipelineCreation = new ManualResetEventSlim(initialState: true);
        using var pipelineCreationEntered = new ManualResetEventSlim(initialState: false);
        var backendDevice = new FakeBackendDevice(
            CreateCapabilities(CreateLimits(maximumResourcesPerStage: 32)),
            pipelineCreationEntered,
            continuePipelineCreation);
        var registration = new GraphicsBackendRegistration(new FakeBackendFactory(backendDevice));
        var graphicsDevice = await GraphicsDevice.CreateAsync(
            ownerId: 24,
            registration,
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None,
            CancellationToken.None).ConfigureAwait(false);
        var bufferOwner = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(64, 1));
        var callerBytecodeLease = bufferOwner.AcquireRead(0, 4);

        try
        {
            ImmutableArray<ShaderResourceSignature> resources = [
                .. CreateResourceSignatures(ShaderResourceKind.SampledImage, 16),
                .. CreateResourceSignatures(ShaderResourceKind.Sampler, 8),
                .. CreateResourceSignatures(ShaderResourceKind.UniformBuffer, 8),
                .. CreateResourceSignatures(ShaderResourceKind.StorageBuffer, 1),
            ];
            var description = CreateComputePipelineDescription(callerBytecodeLease, resources);
            _ = Assert.ThrowsAsync<NotSupportedException>(
                async () => _ = await graphicsDevice.CreatePipelineAsync(description)
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
            Assert.That(backendDevice.PipelineCreationCount, Is.Zero);
        }
        finally
        {
            callerBytecodeLease.Dispose();
            bufferOwner.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task PipelineBindingsStorageLimitsCountSharedAndDistinctStageBindingsCorrectly()
    {
        using var continuePipelineCreation = new ManualResetEventSlim(initialState: true);
        using var pipelineCreationEntered = new ManualResetEventSlim(initialState: false);
        var limits = CreateLimits(
            maximumStorageBuffersPerStage: 2,
            maximumStorageImagesPerStage: 2,
            maximumStorageBuffersPerPipeline: 2,
            maximumStorageImagesPerPipeline: 2,
            maximumStorageResourcesPerPipeline: 2);
        var backendDevice = new FakeBackendDevice(
            CreateCapabilities(limits),
            pipelineCreationEntered,
            continuePipelineCreation);
        var registration = new GraphicsBackendRegistration(new FakeBackendFactory(backendDevice));
        var graphicsDevice = await GraphicsDevice.CreateAsync(
            ownerId: 22,
            registration,
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None,
            CancellationToken.None).ConfigureAwait(false);
        var bufferOwner = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(64, 1));
        var callerBytecodeLease = bufferOwner.AcquireRead(0, 4);
        GpuPipeline? pipeline = null;
        var sharedBuffer = new ShaderResourceSignature(
            ShaderBindingGroup.Material,
            binding: 0,
            ShaderResourceKind.StorageBuffer,
            arrayCount: 1);

        try
        {
            pipeline = await graphicsDevice.CreatePipelineAsync(
                CreatePipelineDescription(
                    callerBytecodeLease,
                    vertexResources: [sharedBuffer],
                    pixelResources: [sharedBuffer]))
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(pipeline.Handle.IsValid, Is.True);
            Assert.That(backendDevice.PipelineCreationCount, Is.EqualTo(1));
            pipeline.Dispose();
            pipeline = null;

            var description = CreateComputePipelineDescription(
                callerBytecodeLease,
                [
                    new ShaderResourceSignature(
                        ShaderBindingGroup.Material,
                        binding: 0,
                        ShaderResourceKind.StorageBuffer,
                        arrayCount: 1),
                    new ShaderResourceSignature(
                        ShaderBindingGroup.Material,
                        binding: 1,
                        ShaderResourceKind.StorageImage,
                        arrayCount: 1),
                    new ShaderResourceSignature(
                        ShaderBindingGroup.Material,
                        binding: 2,
                        ShaderResourceKind.StorageImage,
                        arrayCount: 1),
                ]);
            _ = Assert.ThrowsAsync<NotSupportedException>(
                async () => _ = await graphicsDevice.CreatePipelineAsync(description)
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
            Assert.That(backendDevice.PipelineCreationCount, Is.EqualTo(1));
        }
        finally
        {
            pipeline?.Dispose();
            callerBytecodeLease.Dispose();
            bufferOwner.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task PipelineCancellationWaitsForBackendCreationAndRetiresThePipeline()
    {
        using var cancellationSource = new CancellationTokenSource();
        using var continuePipelineCreation = new ManualResetEventSlim(initialState: false);
        using var pipelineCreationEntered = new ManualResetEventSlim(initialState: false);
        var backendDevice = new FakeBackendDevice(
            CreateCapabilities(),
            pipelineCreationEntered,
            continuePipelineCreation);
        var registration = new GraphicsBackendRegistration(new FakeBackendFactory(backendDevice));
        var graphicsDevice = await GraphicsDevice.CreateAsync(
            ownerId: 18,
            registration,
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None,
            CancellationToken.None).ConfigureAwait(false);
        var bufferOwner = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(64, 1));
        var callerBytecodeLease = bufferOwner.AcquireRead(0, 4);
        var callerLeaseDisposed = false;
        var bufferOwnerDisposed = false;

        try
        {
            var description = CreatePipelineDescription(callerBytecodeLease);
            var pipelineTask = graphicsDevice.CreatePipelineAsync(description, cancellationSource.Token);

            Assert.That(pipelineCreationEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            await cancellationSource.CancelAsync().ConfigureAwait(false);
            Assert.That(pipelineTask.IsCompleted, Is.False);

            continuePipelineCreation.Set();
            _ = Assert.ThrowsAsync<TaskCanceledException>(
                async () => _ = await pipelineTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
            Assert.That(backendDevice.PipelineDisposalCount, Is.EqualTo(1));
        }
        finally
        {
            continuePipelineCreation.Set();

            if (!callerLeaseDisposed)
            {
                callerBytecodeLease.Dispose();
            }

            if (!bufferOwnerDisposed)
            {
                bufferOwner.Dispose();
            }

            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task PipelineHandleRecordingRequiresALiveDeviceOwnedPipeline()
    {
        using var continuePipelineCreation = new ManualResetEventSlim(initialState: true);
        using var pipelineCreationEntered = new ManualResetEventSlim(initialState: false);
        var backendDevice = new FakeBackendDevice(
            CreateCapabilities(),
            pipelineCreationEntered,
            continuePipelineCreation);
        var registration = new GraphicsBackendRegistration(new FakeBackendFactory(backendDevice));
        var graphicsDevice = await GraphicsDevice.CreateAsync(
            ownerId: 26,
            registration,
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None,
            CancellationToken.None).ConfigureAwait(false);
        var bufferOwner = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(64, 1));
        var callerBytecodeLease = bufferOwner.AcquireRead(0, 4);
        GpuPipeline? pipeline = null;

        try
        {
            pipeline = await graphicsDevice.CreatePipelineAsync(
                CreateComputePipelineDescription(callerBytecodeLease, []))
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            var handle = pipeline.Handle;
            var callerThreadId = Environment.CurrentManagedThreadId;
            var callbackThreadId = 0;
            void RecordCommands(scoped ref CommandRecorder recorder)
            {
                callbackThreadId = Environment.CurrentManagedThreadId;
                recorder.SetPipeline(handle);
                recorder.Dispatch(new DispatchSize(1, 1, 1));
            }

            using (var commandList = await graphicsDevice.RecordPassAsync(
                new RenderPassId(1, 0),
                CommandRecorderPassKind.Compute,
                [],
                [],
                depthTargetFormat: null,
                RecordCommands)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false))
            {
                Assert.That(callbackThreadId, Is.Not.EqualTo(callerThreadId));
                Assert.That(commandList.Commands.Length, Is.EqualTo(2));
                Assert.That(commandList.Commands[0].Kind, Is.EqualTo(GraphicsCommandKind.SetPipeline));
                Assert.That(commandList.Commands[1].Kind, Is.EqualTo(GraphicsCommandKind.Dispatch));
            }

            void DispatchBeyondLimit(scoped ref CommandRecorder recorder)
            {
                recorder.SetPipeline(handle);
                recorder.Dispatch(new DispatchSize(65536, 1, 1));
            }

            _ = Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                async () => await graphicsDevice.RecordPassAsync(
                    new RenderPassId(1, 1),
                    CommandRecorderPassKind.Compute,
                    [],
                    [],
                    depthTargetFormat: null,
                    DispatchBeyondLimit)
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false));

            void DrawInComputePass(scoped ref CommandRecorder recorder)
            {
                recorder.SetPipeline(handle);
                recorder.Draw(new DrawArguments(1, 1, 0, 0));
            }

            _ = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await graphicsDevice.RecordPassAsync(
                    new RenderPassId(1, 2),
                    CommandRecorderPassKind.Compute,
                    [],
                    [],
                    depthTargetFormat: null,
                    DrawInComputePass)
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false));

            pipeline.Dispose();
            pipeline = null;
            void BindDisposedPipeline(scoped ref CommandRecorder recorder)
            {
                recorder.SetPipeline(handle);
            }

            _ = Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await graphicsDevice.RecordPassAsync(
                    new RenderPassId(1, 1),
                    CommandRecorderPassKind.Compute,
                    [],
                    [],
                    depthTargetFormat: null,
                    BindDisposedPipeline)
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false));
        }
        finally
        {
            pipeline?.Dispose();
            callerBytecodeLease.Dispose();
            bufferOwner.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task PipelineRecorderBindsOnlyThePassDeclaredUniformRange()
    {
        using var continuePipelineCreation = new ManualResetEventSlim(initialState: true);
        using var pipelineCreationEntered = new ManualResetEventSlim(initialState: false);
        var backendDevice = new FakeBackendDevice(
            CreateCapabilities(),
            pipelineCreationEntered,
            continuePipelineCreation);
        var registration = new GraphicsBackendRegistration(new FakeBackendFactory(backendDevice));
        var graphicsDevice = await GraphicsDevice.CreateAsync(
            ownerId: 27,
            registration,
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None,
            CancellationToken.None).ConfigureAwait(false);
        var bufferOwner = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(64, 1));
        var callerBytecodeLease = bufferOwner.AcquireRead(0, 4);
        var resourceSignature = new ShaderResourceSignature(
            ShaderBindingGroup.Frame,
            0,
            ShaderResourceKind.UniformBuffer,
            arrayCount: 1);
        GpuPipeline? pipeline = null;
        GpuPipeline? storagePipeline = null;
        RecordedPassCommandList? commandList = null;
        RecordedPassCommandList? storageCommandList = null;

        try
        {
            pipeline = await graphicsDevice.CreatePipelineAsync(
                CreateComputePipelineDescription(callerBytecodeLease, [resourceSignature]))
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);

            var resources = new GraphResourceRegistry(graphicsDevice, graphOwnerId: 10);
            var writer = resources.AddPass(new RenderPassDescription("uniform-writer", 0, [], false));
            var reader = resources.AddPass(new RenderPassDescription("uniform-reader", 1, [writer], false));
            var range = new GraphBufferRange(0, 64);
            var declaredRange = new GraphBufferRange(0, 32);
            var version0 = resources.CreateTransientBuffer(new BufferDescription(
                64,
                BufferUsage.Storage | BufferUsage.Uniform,
                GpuMemoryKind.DeviceLocal,
                structuredStride: null,
                debugName: "command-recorder-uniform"));
            var version1 = resources.WriteBuffer(writer, version0, declaredRange, GraphBufferUsage.StorageWrite);
            resources.ReadBuffer(reader, version1, declaredRange, GraphBufferUsage.UniformRead);
            var readerAccesses = ImmutableArray.CreateBuilder<GraphResourceAccess>();

            foreach (var access in resources.Accesses)
            {
                if (access.PassId == reader)
                {
                    readerAccesses.Add(access);
                }
            }

            var readWritePass = resources.AddPass(new RenderPassDescription("storage-read-write", 2, [], false));
            var readWriteVersion = resources.ReadWriteBuffer(readWritePass, version1, declaredRange);
            var readWriteAccesses = ImmutableArray.CreateBuilder<GraphResourceAccess>();
            var writeOnlyAccesses = ImmutableArray.CreateBuilder<GraphResourceAccess>();

            foreach (var access in resources.Accesses)
            {
                if (access.PassId == readWritePass)
                {
                    readWriteAccesses.Add(access);
                }

                if (access.PassId == writer)
                {
                    writeOnlyAccesses.Add(access);
                }
            }

            var bindingSet = new BindingSet(
                [new BufferBinding(ShaderBindingGroup.Frame, 0, ShaderResourceKind.UniformBuffer, version1, declaredRange)],
                [],
                []);
            var pipelineHandle = pipeline.Handle;

            void RecordUniformDispatch(scoped ref CommandRecorder recorder)
            {
                recorder.SetPipeline(pipelineHandle);
                recorder.SetBindings(bindingSet);
                recorder.Dispatch(new DispatchSize(1, 1, 1));
            }

            commandList = await graphicsDevice.RecordPassAsync(
                reader,
                CommandRecorderPassKind.Compute,
                readerAccesses.ToImmutable(),
                [],
                depthTargetFormat: null,
                RecordUniformDispatch)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            Assert.That(commandList.Commands.Length, Is.EqualTo(3));
            Assert.That(commandList.Commands[1].Kind, Is.EqualTo(GraphicsCommandKind.SetBindings));
            Assert.That(commandList.Commands[2].Kind, Is.EqualTo(GraphicsCommandKind.Dispatch));

            var broadBindingSet = new BindingSet(
                [new BufferBinding(ShaderBindingGroup.Frame, 0, ShaderResourceKind.UniformBuffer, version1, range)],
                [],
                []);
            void BindBeyondDeclaredRange(scoped ref CommandRecorder recorder)
            {
                recorder.SetPipeline(pipelineHandle);
                recorder.SetBindings(broadBindingSet);
            }

            _ = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await graphicsDevice.RecordPassAsync(
                    reader,
                    CommandRecorderPassKind.Compute,
                    readerAccesses.ToImmutable(),
                    [],
                    depthTargetFormat: null,
                    BindBeyondDeclaredRange)
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false));

            var storageSignature = new ShaderResourceSignature(
                ShaderBindingGroup.Frame,
                0,
                ShaderResourceKind.StorageBuffer,
                arrayCount: 1);
            storagePipeline = await graphicsDevice.CreatePipelineAsync(
                CreateComputePipelineDescription(callerBytecodeLease, [storageSignature]))
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            var storageBindingSet = new BindingSet(
                [new BufferBinding(ShaderBindingGroup.Frame, 0, ShaderResourceKind.StorageBuffer, readWriteVersion, declaredRange)],
                [],
                []);
            var storagePipelineHandle = storagePipeline.Handle;

            void RecordStorageReadWrite(scoped ref CommandRecorder recorder)
            {
                recorder.SetPipeline(storagePipelineHandle);
                recorder.SetBindings(storageBindingSet);
                recorder.Dispatch(new DispatchSize(1, 1, 1));
                recorder.Dispatch(new DispatchSize(1, 1, 1));
            }

            storageCommandList = await graphicsDevice.RecordPassAsync(
                    readWritePass,
                    CommandRecorderPassKind.Compute,
                    readWriteAccesses.ToImmutable(),
                    [],
                    depthTargetFormat: null,
                    RecordStorageReadWrite)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            Assert.That(storageCommandList.Commands.Length, Is.EqualTo(5));
            Assert.That(storageCommandList.Commands[1].Kind, Is.EqualTo(GraphicsCommandKind.SetBindings));
            Assert.That(storageCommandList.Commands[2].Kind, Is.EqualTo(GraphicsCommandKind.Dispatch));
            Assert.That(storageCommandList.Commands[3].Kind, Is.EqualTo(GraphicsCommandKind.StorageBarrier));
            Assert.That(storageCommandList.Commands[3].StorageBarrierBuffer, Is.SameAs(readWriteVersion));
            Assert.That(storageCommandList.Commands[3].StorageBarrierBufferRange, Is.EqualTo(readWriteVersion.Range));
            Assert.That(storageCommandList.Commands[3].StorageBarrierSourceReads, Is.True);
            Assert.That(storageCommandList.Commands[3].StorageBarrierSourceWrites, Is.True);
            Assert.That(storageCommandList.Commands[3].StorageBarrierDestinationReads, Is.True);
            Assert.That(storageCommandList.Commands[3].StorageBarrierDestinationWrites, Is.True);
            Assert.That(storageCommandList.Commands[4].Kind, Is.EqualTo(GraphicsCommandKind.Dispatch));

            var writeOnlyBindingSet = new BindingSet(
                [new BufferBinding(ShaderBindingGroup.Frame, 0, ShaderResourceKind.StorageBuffer, version1, declaredRange)],
                [],
                []);
            void BindReadWritePipelineToWriteOnlyRange(scoped ref CommandRecorder recorder)
            {
                recorder.SetPipeline(storagePipelineHandle);
                recorder.SetBindings(writeOnlyBindingSet);
            }

            _ = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await graphicsDevice.RecordPassAsync(
                    writer,
                    CommandRecorderPassKind.Compute,
                    writeOnlyAccesses.ToImmutable(),
                    [],
                    depthTargetFormat: null,
                    BindReadWritePipelineToWriteOnlyRange)
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false));
        }
        finally
        {
            storageCommandList?.Dispose();
            commandList?.Dispose();
            storagePipeline?.Dispose();
            pipeline?.Dispose();
            callerBytecodeLease.Dispose();
            bufferOwner.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task PipelineRecorderBindsReadWriteStorageImagePairs()
    {
        using var continuePipelineCreation = new ManualResetEventSlim(initialState: true);
        using var pipelineCreationEntered = new ManualResetEventSlim(initialState: false);
        var backendDevice = new FakeBackendDevice(
            CreateCapabilities(),
            pipelineCreationEntered,
            continuePipelineCreation);
        var registration = new GraphicsBackendRegistration(new FakeBackendFactory(backendDevice));
        var graphicsDevice = await GraphicsDevice.CreateAsync(
            ownerId: 31,
            registration,
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None,
            CancellationToken.None).ConfigureAwait(false);
        var bufferOwner = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(64, 1));
        var callerBytecodeLease = bufferOwner.AcquireRead(0, 4);
        var resourceSignature = new ShaderResourceSignature(
            ShaderBindingGroup.Frame,
            0,
            ShaderResourceKind.StorageImage,
            1,
            ShaderResourceAccess.ReadWrite);
        GpuPipeline? pipeline = null;
        RecordedPassCommandList? commandList = null;

        try
        {
            pipeline = await graphicsDevice.CreatePipelineAsync(
                CreateComputePipelineDescription(callerBytecodeLease, [resourceSignature]))
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);

            var resources = new GraphResourceRegistry(graphicsDevice, graphOwnerId: 11);
            var seedPass = resources.AddPass(new RenderPassDescription("storage-image-seed", 0, [], false));
            var readWritePass = resources.AddPass(new RenderPassDescription("storage-image-read-write", 1, [], false));
            var range = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);
            var version0 = resources.CreateTransientTexture(new TextureDescription(
                TextureDimension.Dimension2D,
                4,
                4,
                1,
                1,
                1,
                TextureFormat.Rgba16Float,
                TextureUsage.Storage,
                "command-recorder-storage-image"));
            var version1 = resources.WriteTexture(seedPass, version0, range, GraphTextureUsage.StorageWrite);
            var version2 = resources.ReadWriteTexture(readWritePass, version1, range);
            var readWriteAccesses = ImmutableArray.CreateBuilder<GraphResourceAccess>();

            foreach (var access in resources.Accesses)
            {
                if (access.PassId == readWritePass)
                {
                    readWriteAccesses.Add(access);
                }
            }

            var bindingSet = new BindingSet(
                [],
                [new TextureBinding(ShaderBindingGroup.Frame, 0, ShaderResourceKind.StorageImage, version2, range)],
                []);
            var pipelineHandle = pipeline.Handle;

            void RecordStorageImageReadWrite(scoped ref CommandRecorder recorder)
            {
                recorder.SetPipeline(pipelineHandle);
                recorder.SetBindings(bindingSet);
                recorder.Dispatch(new DispatchSize(1, 1, 1));
                recorder.Dispatch(new DispatchSize(1, 1, 1));
            }

            commandList = await graphicsDevice.RecordPassAsync(
                    readWritePass,
                    CommandRecorderPassKind.Compute,
                    readWriteAccesses.ToImmutable(),
                    [],
                    depthTargetFormat: null,
                    RecordStorageImageReadWrite)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            Assert.That(commandList.Commands.Length, Is.EqualTo(5));
            Assert.That(commandList.Commands[1].Kind, Is.EqualTo(GraphicsCommandKind.SetBindings));
            Assert.That(commandList.Commands[2].Kind, Is.EqualTo(GraphicsCommandKind.Dispatch));
            Assert.That(commandList.Commands[3].Kind, Is.EqualTo(GraphicsCommandKind.StorageBarrier));
            Assert.That(commandList.Commands[3].StorageBarrierTexture, Is.SameAs(version2));
            Assert.That(commandList.Commands[3].StorageBarrierTextureRange, Is.EqualTo(version2.Range));
            Assert.That(commandList.Commands[3].StorageBarrierSourceReads, Is.True);
            Assert.That(commandList.Commands[3].StorageBarrierSourceWrites, Is.True);
            Assert.That(commandList.Commands[3].StorageBarrierDestinationReads, Is.True);
            Assert.That(commandList.Commands[3].StorageBarrierDestinationWrites, Is.True);
            Assert.That(commandList.Commands[4].Kind, Is.EqualTo(GraphicsCommandKind.Dispatch));
        }
        finally
        {
            commandList?.Dispose();
            pipeline?.Dispose();
            callerBytecodeLease.Dispose();
            bufferOwner.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task PipelineRecorderCopiesOnlyDeclaredNonoverlappingRanges()
    {
        using var continuePipelineCreation = new ManualResetEventSlim(initialState: true);
        using var pipelineCreationEntered = new ManualResetEventSlim(initialState: false);
        var backendDevice = new FakeBackendDevice(
            CreateCapabilities(),
            pipelineCreationEntered,
            continuePipelineCreation);
        var registration = new GraphicsBackendRegistration(new FakeBackendFactory(backendDevice));
        var graphicsDevice = await GraphicsDevice.CreateAsync(
            ownerId: 28,
            registration,
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None,
            CancellationToken.None).ConfigureAwait(false);

        try
        {
            var resources = new GraphResourceRegistry(graphicsDevice, graphOwnerId: 11);
            var initializer = resources.AddPass(new RenderPassDescription("copy-source-init", 0, [], false));
            var copyPass = resources.AddPass(new RenderPassDescription("copy-pass", 1, [initializer], false));
            var fullRange = new GraphBufferRange(0, 64);
            var overlappingRange = new GraphBufferRange(8, 16);
            var description = new BufferDescription(
                64,
                BufferUsage.CopySource | BufferUsage.CopyDestination,
                GpuMemoryKind.DeviceLocal,
                structuredStride: null,
                debugName: "command-recorder-copy");
            var source0 = resources.CreateTransientBuffer(description);
            var source1 = resources.WriteBuffer(initializer, source0, fullRange, GraphBufferUsage.CopyDestination);
            var destination0 = resources.CreateTransientBuffer(description);
            var destination1 = resources.WriteBuffer(copyPass, destination0, fullRange, GraphBufferUsage.CopyDestination);
            resources.ReadBuffer(copyPass, source1, fullRange, GraphBufferUsage.CopySource);
            var overlappingDestination = resources.WriteBuffer(
                copyPass,
                source1,
                overlappingRange,
                GraphBufferUsage.CopyDestination);
            var textureDescription = new TextureDescription(
                TextureDimension.Dimension2D,
                width: 2,
                height: 2,
                depth: 1,
                arrayLayers: 1,
                mipLevels: 1,
                TextureFormat.Rgba8Unorm,
                TextureUsage.CopySource | TextureUsage.CopyDestination,
                debugName: "command-recorder-copy-texture");
            var textureRange = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);
            var textureSourceInitialVersion = resources.CreateTransientTexture(textureDescription);
            var textureSourceVersion = resources.WriteTexture(
                initializer,
                textureSourceInitialVersion,
                textureRange,
                GraphTextureUsage.CopyDestination);
            var textureDestinationInitialVersion = resources.CreateTransientTexture(textureDescription);
            var textureDestinationVersion = resources.WriteTexture(
                copyPass,
                textureDestinationInitialVersion,
                textureRange,
                GraphTextureUsage.CopyDestination);
            resources.ReadTexture(copyPass, textureSourceVersion, textureRange, GraphTextureUsage.CopySource);

            ImmutableArray<GraphResourceAccess> GetPassAccesses(RenderPassId passId)
            {
                var accesses = ImmutableArray.CreateBuilder<GraphResourceAccess>();

                foreach (var access in resources.Accesses)
                {
                    if (access.PassId == passId)
                    {
                        accesses.Add(access);
                    }
                }

                return accesses.ToImmutable();
            }

            var copyAccesses = GetPassAccesses(copyPass);

            void RecordCopy(scoped ref CommandRecorder recorder)
            {
                recorder.CopyBuffer(source1, 0, destination1, 0, 64);
                recorder.CopyTexture(textureSourceVersion, textureDestinationVersion);
                recorder.CopyTexture(
                    textureSourceVersion,
                    textureDestinationVersion,
                    new TextureCopyRegion(
                        sourceX: 0,
                        sourceY: 0,
                        width: 2,
                        height: 2));
            }

            var formatMismatchDescription = new TextureDescription(
                TextureDimension.Dimension2D,
                width: 2,
                height: 2,
                depth: 1,
                arrayLayers: 1,
                mipLevels: 1,
                TextureFormat.R32UInt,
                TextureUsage.CopyDestination,
                debugName: "command-recorder-copy-format-mismatch");
            var formatMismatchPass = resources.AddPass(new RenderPassDescription("copy-format-mismatch", 1, [initializer], false));
            var formatMismatchInitialVersion = resources.CreateTransientTexture(formatMismatchDescription);
            var formatMismatchVersion = resources.WriteTexture(
                formatMismatchPass,
                formatMismatchInitialVersion,
                textureRange,
                GraphTextureUsage.CopyDestination);
            resources.ReadTexture(formatMismatchPass, textureSourceVersion, textureRange, GraphTextureUsage.CopySource);
            var formatMismatchAccesses = GetPassAccesses(formatMismatchPass);

            var extentMismatchDescription = new TextureDescription(
                TextureDimension.Dimension2D,
                width: 1,
                height: 4,
                depth: 1,
                arrayLayers: 1,
                mipLevels: 1,
                TextureFormat.Rgba8Unorm,
                TextureUsage.CopyDestination,
                debugName: "command-recorder-copy-extent-mismatch");
            var extentMismatchPass = resources.AddPass(new RenderPassDescription("copy-extent-mismatch", 1, [initializer], false));
            var extentMismatchInitialVersion = resources.CreateTransientTexture(extentMismatchDescription);
            var extentMismatchVersion = resources.WriteTexture(
                extentMismatchPass,
                extentMismatchInitialVersion,
                textureRange,
                GraphTextureUsage.CopyDestination);
            resources.ReadTexture(extentMismatchPass, textureSourceVersion, textureRange, GraphTextureUsage.CopySource);
            var extentMismatchAccesses = GetPassAccesses(extentMismatchPass);

            using (var commandList = await graphicsDevice.RecordPassAsync(
                copyPass,
                CommandRecorderPassKind.Copy,
                copyAccesses,
                [],
                depthTargetFormat: null,
                RecordCopy)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false))
            {
                Assert.That(commandList.Commands.Length, Is.EqualTo(3));
                Assert.That(commandList.Commands[0].Kind, Is.EqualTo(GraphicsCommandKind.CopyBuffer));
                Assert.That(commandList.Commands[1].Kind, Is.EqualTo(GraphicsCommandKind.CopyTexture));
                Assert.That(commandList.Commands[2].Kind, Is.EqualTo(GraphicsCommandKind.CopyTexture));
                Assert.That(commandList.Commands[2].CopyTextureRegion.SourceX, Is.Zero);
                Assert.That(commandList.Commands[2].CopyTextureRegion.SourceY, Is.Zero);
                Assert.That(commandList.Commands[2].CopyTextureRegion.Width, Is.EqualTo(2));
                Assert.That(commandList.Commands[2].CopyTextureRegion.Height, Is.EqualTo(2));
            }

            void RecordOutOfBoundsTextureCopy(scoped ref CommandRecorder recorder)
            {
                recorder.CopyTexture(
                    textureSourceVersion,
                    textureDestinationVersion,
                    new TextureCopyRegion(
                        sourceX: 1,
                        sourceY: 0,
                        width: 2,
                        height: 2));
            }

            _ = Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                async () => await graphicsDevice.RecordPassAsync(
                    copyPass,
                    CommandRecorderPassKind.Copy,
                    copyAccesses,
                    [],
                    depthTargetFormat: null,
                    RecordOutOfBoundsTextureCopy)
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false));

            void RecordFormatMismatch(scoped ref CommandRecorder recorder)
            {
                recorder.CopyTexture(textureSourceVersion, formatMismatchVersion);
            }

            _ = Assert.ThrowsAsync<ArgumentException>(
                async () => await graphicsDevice.RecordPassAsync(
                    formatMismatchPass,
                    CommandRecorderPassKind.Copy,
                    formatMismatchAccesses,
                    [],
                    depthTargetFormat: null,
                    RecordFormatMismatch)
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false));

            void RecordExtentMismatch(scoped ref CommandRecorder recorder)
            {
                recorder.CopyTexture(textureSourceVersion, extentMismatchVersion);
            }

            _ = Assert.ThrowsAsync<ArgumentException>(
                async () => await graphicsDevice.RecordPassAsync(
                    extentMismatchPass,
                    CommandRecorderPassKind.Copy,
                    extentMismatchAccesses,
                    [],
                    depthTargetFormat: null,
                    RecordExtentMismatch)
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false));

            void RecordOverlappingCopy(scoped ref CommandRecorder recorder)
            {
                recorder.CopyBuffer(source1, 0, overlappingDestination, 8, 16);
            }

            _ = Assert.ThrowsAsync<ArgumentException>(
                async () => await graphicsDevice.RecordPassAsync(
                    copyPass,
                    CommandRecorderPassKind.Copy,
                    copyAccesses,
                    [],
                    depthTargetFormat: null,
                    RecordOverlappingCopy)
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false));
        }
        finally
        {
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task PipelineRecorderValidatesRasterVertexAndIndexRanges()
    {
        using var continuePipelineCreation = new ManualResetEventSlim(initialState: true);
        using var pipelineCreationEntered = new ManualResetEventSlim(initialState: false);
        var backendDevice = new FakeBackendDevice(
            CreateCapabilities(),
            pipelineCreationEntered,
            continuePipelineCreation);
        var registration = new GraphicsBackendRegistration(new FakeBackendFactory(backendDevice));
        var graphicsDevice = await GraphicsDevice.CreateAsync(
            ownerId: 29,
            registration,
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None,
            CancellationToken.None).ConfigureAwait(false);
        var bufferOwner = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(64, 1));
        var callerBytecodeLease = bufferOwner.AcquireRead(0, 4);
        GpuPipeline? pipeline = null;
        RecordedPassCommandList? commandList = null;

        try
        {
            var compilerIdentity = CreateCompilerIdentity();
            var vertexReflection = new ShaderReflection(
                ShaderStage.Vertex,
                "VSMain",
                compilerIdentity,
                [new ShaderIoSignature("POSITION", 0, 0, ShaderValueType.Float4, ShaderInterpolation.Smooth)],
                [new ShaderIoSignature("SV_Position", 0, null, ShaderValueType.Float4, ShaderInterpolation.Smooth)],
                [],
                [],
                [],
                bindingLayoutVersion: 1,
                groupSizeX: 0,
                groupSizeY: 0,
                groupSizeZ: 0);
            var pixelReflection = new ShaderReflection(
                ShaderStage.Pixel,
                "PSMain",
                compilerIdentity,
                [],
                [new ShaderIoSignature("SV_Target", 0, null, ShaderValueType.Float4, ShaderInterpolation.Smooth)],
                [],
                [],
                [],
                bindingLayoutVersion: 1,
                groupSizeX: 0,
                groupSizeY: 0,
                groupSizeZ: 0);
            var description = new GraphicsPipelineDescription(
                [
                    new ShaderBytecodeInput(ShaderStage.Vertex, ShaderBinaryFormat.Dxil, callerBytecodeLease, vertexReflection),
                    new ShaderBytecodeInput(ShaderStage.Pixel, ShaderBinaryFormat.Dxil, callerBytecodeLease, pixelReflection),
                ],
                [new VertexElementDescription(0, VertexElementFormat.Float4, 0)],
                vertexStride: 16,
                [TextureFormat.Rgba8Unorm],
                depthTargetFormat: null,
                PrimitiveTopology.TriangleList,
                PipelineBlendMode.Opaque,
                PipelineDepthMode.Disabled,
                PipelineCullMode.None,
                frontFaceClockwise: false,
                bindingLayoutVersion: 1);
            pipeline = await graphicsDevice.CreatePipelineAsync(description)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);

            var resources = new GraphResourceRegistry(graphicsDevice, graphOwnerId: 12);
            var rasterPass = resources.AddPass(new RenderPassDescription("raster-pass", 0, [], false));
            var vertexRange = new GraphBufferRange(0, 48);
            var indexRange = new GraphBufferRange(0, 8);
            var vertexBuffer = resources.CreateTransientBuffer(new BufferDescription(
                48,
                BufferUsage.Vertex,
                GpuMemoryKind.DeviceLocal,
                structuredStride: null,
                debugName: "command-recorder-vertex"));
            var indexBuffer = resources.CreateTransientBuffer(new BufferDescription(
                8,
                BufferUsage.Index,
                GpuMemoryKind.DeviceLocal,
                structuredStride: null,
                debugName: "command-recorder-index"));
            resources.ReadBuffer(rasterPass, vertexBuffer, vertexRange, GraphBufferUsage.VertexRead);
            resources.ReadBuffer(rasterPass, indexBuffer, indexRange, GraphBufferUsage.IndexRead);
            var targetRange = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);
            var target = resources.CreateTransientTexture(new TextureDescription(
                TextureDimension.Dimension2D,
                4,
                4,
                depth: 1,
                arrayLayers: 1,
                mipLevels: 1,
                format: TextureFormat.Rgba8Unorm,
                usage: TextureUsage.ColorAttachment,
                debugName: "command-recorder-color"));
            _ = resources.WriteAttachment(
                rasterPass,
                target,
                targetRange,
                AttachmentLoadAction.Clear,
                AttachmentStoreAction.Store);
            var rasterAccesses = ImmutableArray.CreateBuilder<GraphResourceAccess>();

            foreach (var access in resources.Accesses)
            {
                if (access.PassId == rasterPass)
                {
                    rasterAccesses.Add(access);
                }
            }

            var pipelineHandle = pipeline.Handle;
            var vertexBinding = new VertexBufferBinding(0, vertexBuffer, vertexRange, 16);
            var indexBinding = new IndexBufferBinding(indexBuffer, indexRange, IndexFormat.UInt16);

            void RecordIndexedDraw(scoped ref CommandRecorder recorder)
            {
                recorder.SetPipeline(pipelineHandle);
                recorder.SetVertexBuffer(vertexBinding);
                recorder.SetIndexBuffer(indexBinding);
                recorder.DrawIndexed(new DrawIndexedArguments(4, 1, 0, -1, 0));
            }

            commandList = await graphicsDevice.RecordPassAsync(
                rasterPass,
                CommandRecorderPassKind.Raster,
                rasterAccesses.ToImmutable(),
                [TextureFormat.Rgba8Unorm],
                depthTargetFormat: null,
                RecordIndexedDraw)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            Assert.That(commandList.Commands.Length, Is.EqualTo(4));
            Assert.That(commandList.Commands[3].Kind, Is.EqualTo(GraphicsCommandKind.DrawIndexed));
            commandList.Dispose();
            commandList = null;

            void DrawPastVertexRange(scoped ref CommandRecorder recorder)
            {
                recorder.SetPipeline(pipelineHandle);
                recorder.SetVertexBuffer(vertexBinding);
                recorder.Draw(new DrawArguments(4, 1, 0, 0));
            }

            _ = Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                async () => await graphicsDevice.RecordPassAsync(
                    rasterPass,
                    CommandRecorderPassKind.Raster,
                    rasterAccesses.ToImmutable(),
                    [TextureFormat.Rgba8Unorm],
                    depthTargetFormat: null,
                    DrawPastVertexRange)
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false));

            void DrawPastIndexRange(scoped ref CommandRecorder recorder)
            {
                recorder.SetPipeline(pipelineHandle);
                recorder.SetVertexBuffer(vertexBinding);
                recorder.SetIndexBuffer(indexBinding);
                recorder.DrawIndexed(new DrawIndexedArguments(5, 1, 0, 0, 0));
            }

            _ = Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                async () => await graphicsDevice.RecordPassAsync(
                    rasterPass,
                    CommandRecorderPassKind.Raster,
                    rasterAccesses.ToImmutable(),
                    [TextureFormat.Rgba8Unorm],
                    depthTargetFormat: null,
                    DrawPastIndexRange)
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false));
        }
        finally
        {
            commandList?.Dispose();
            pipeline?.Dispose();
            callerBytecodeLease.Dispose();
            bufferOwner.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task PipelineRequestRetainsBytecodeUntilBackendCreationCompletes()
    {
        using var continuePipelineCreation = new ManualResetEventSlim(initialState: false);
        using var pipelineCreationEntered = new ManualResetEventSlim(initialState: false);
        var backendDevice = new FakeBackendDevice(
            CreateCapabilities(),
            pipelineCreationEntered,
            continuePipelineCreation);
        var registration = new GraphicsBackendRegistration(new FakeBackendFactory(backendDevice));
        var graphicsDevice = await GraphicsDevice.CreateAsync(
            ownerId: 17,
            registration,
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None,
            CancellationToken.None).ConfigureAwait(false);
        var bufferOwner = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(64, 1));
        var callerBytecodeLease = bufferOwner.AcquireRead(0, 4);
        var callerLeaseDisposed = false;
        var bufferOwnerDisposed = false;
        GpuPipeline? pipeline = null;

        try
        {
            var description = CreatePipelineDescription(callerBytecodeLease);
            var pipelineTask = graphicsDevice.CreatePipelineAsync(description);

            Assert.That(pipelineCreationEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            callerBytecodeLease.Dispose();
            callerLeaseDisposed = true;
            bufferOwner.Dispose();
            bufferOwnerDisposed = true;
            Assert.That(pipelineTask.IsCompleted, Is.False);

            continuePipelineCreation.Set();
            pipeline = await pipelineTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(backendDevice.ObservedBytecodeLength, Is.EqualTo(4));
            Assert.That(pipeline.Handle.IsValid, Is.True);
            Assert.That(pipeline.DeviceEpoch, Is.EqualTo(graphicsDevice.DeviceEpoch));

            pipeline.Dispose();
            pipeline = null;
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(backendDevice.PipelineDisposalCount, Is.EqualTo(1));
        }
        finally
        {
            continuePipelineCreation.Set();
            pipeline?.Dispose();

            if (!callerLeaseDisposed)
            {
                callerBytecodeLease.Dispose();
            }

            if (!bufferOwnerDisposed)
            {
                bufferOwner.Dispose();
            }

            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task PipelineStorageAccessRequiresReadWriteOnD3D12BeforeBackendCreation()
    {
        using var continuePipelineCreation = new ManualResetEventSlim(initialState: true);
        using var pipelineCreationEntered = new ManualResetEventSlim(initialState: false);
        var backendDevice = new FakeBackendDevice(
            CreateCapabilities(),
            pipelineCreationEntered,
            continuePipelineCreation);
        var registration = new GraphicsBackendRegistration(new FakeBackendFactory(backendDevice));
        var graphicsDevice = await GraphicsDevice.CreateAsync(
            ownerId: 30,
            registration,
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None,
            CancellationToken.None).ConfigureAwait(false);
        var bufferOwner = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(64, 1));
        var callerBytecodeLease = bufferOwner.AcquireRead(0, 4);

        try
        {
            var resource = new ShaderResourceSignature(
                ShaderBindingGroup.Frame,
                0,
                ShaderResourceKind.StorageBuffer,
                1,
                ShaderResourceAccess.Read);
            var description = CreateComputePipelineDescription(callerBytecodeLease, [resource]);

            _ = Assert.ThrowsAsync<NotSupportedException>(
                async () => await graphicsDevice.CreatePipelineAsync(description)
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false));
            Assert.That(backendDevice.PipelineCreationCount, Is.Zero);
        }
        finally
        {
            callerBytecodeLease.Dispose();
            bufferOwner.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task RecordingArrayMinimumRefusesWithoutWaitingForTheBlockedOwner()
    {
        using var proceed = new ManualResetEventSlim(initialState: true);
        using var entered = new ManualResetEventSlim(initialState: false);
        using var callbackEntered = new ManualResetEventSlim(initialState: false);
        using var releaseCallback = new ManualResetEventSlim(initialState: false);
        var backend = new FakeBackendDevice(CreateCapabilities(), entered, proceed);
        var device = await GraphicsDevice.CreateAsync(
            31,
            new GraphicsBackendRegistration(new FakeBackendFactory(backend)),
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None,
            CancellationToken.None).ConfigureAwait(false);
        Task<RecordedPassCommandList>? blocker = null;

        try
        {
            void Block(scoped ref CommandRecorder recorder)
            {
                callbackEntered.Set();
                releaseCallback.Wait();
            }

            blocker = device.RecordPassAsync(new RenderPassId(201, 0), CommandRecorderPassKind.Copy, [], [], null, Block);
            Assert.That(callbackEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            var refused = device.RecordPassAsync(
                new RenderPassId(201, 1),
                CommandRecorderPassKind.Copy,
                [],
                [],
                null,
                static (scoped ref CommandRecorder recorder) => { },
                new CommandRecorderRecordingCapacity(int.MaxValue, 1, 0, 0));
            Assert.That(refused.IsCompleted, Is.True);
            _ = Assert.ThrowsAsync<OutOfMemoryException>(async () => await refused.ConfigureAwait(false));
        }
        finally
        {
            releaseCallback.Set();

            if (blocker is not null)
            {
                using var result = await blocker.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }

            await device.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task RecordingUseAdmissionRefusesAContendedOwnerWithoutSpendingTheCohort()
    {
        using var proceed = new ManualResetEventSlim(initialState: true);
        using var entered = new ManualResetEventSlim(initialState: false);
        var backend = new FakeBackendDevice(CreateCapabilities(), entered, proceed);
        var device = await GraphicsDevice.CreateAsync(
            31,
            new GraphicsBackendRegistration(new FakeBackendFactory(backend)),
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None,
            CancellationToken.None).ConfigureAwait(false);
        using var bytes = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(64, 1));
        using var bytecode = bytes.AcquireRead(0, 4);
        using var pipeline = await device.CreatePipelineAsync(CreateComputePipelineDescription(bytecode, []))
            .ConfigureAwait(false);
        var admission = GraphicsRecordingUseAdmission.Create(1, 32L * 1024 * 1024);
        var gateField = typeof(GraphicsResourceOwner).GetField("_useGate", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The actual Graphics resource gate was not found.");
        var gate = gateField.GetValue(pipeline.ResourceOwner) as System.Threading.Lock
            ?? throw new InvalidOperationException("The actual Graphics resource gate has an unexpected type.");

        try
        {
            void Acquire(scoped ref CommandRecorder recorder)
            {
                using var use = device.AcquirePipelineForRecording(pipeline.Handle, admission).UseLease;
            }

            gate.Enter();

            try
            {
                _ = Assert.ThrowsAsync<OutOfMemoryException>(
                    async () => await device.RecordPassAsync(
                        new RenderPassId(401, 0), CommandRecorderPassKind.Copy, [], [], null, Acquire)
                        .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
                Assert.That(admission.ClaimedUseCount, Is.Zero);
                Assert.That(pipeline.ResourceOwner.HasActiveUses, Is.False);
            }
            finally
            {
                gate.Exit();
            }

            using var recorded = await device.RecordPassAsync(
                new RenderPassId(401, 1), CommandRecorderPassKind.Copy, [], [], null, Acquire)
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            admission.ValidateComplete();
            Assert.That(pipeline.ResourceOwner.HasActiveUses, Is.False);
            Assert.That(admission.ManagedByteCharge, Is.EqualTo(272));
        }
        finally
        {
            pipeline.Dispose();
            await device.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(64)]
    public async Task RecordingUseCohortBoundsActualOwnerAllocationsAndRejectsGrowth(int useCount)
    {
        using var proceed = new ManualResetEventSlim(initialState: true);
        using var entered = new ManualResetEventSlim(initialState: false);
        var backend = new FakeBackendDevice(CreateCapabilities(), entered, proceed);
        var device = await GraphicsDevice.CreateAsync(
            31,
            new GraphicsBackendRegistration(new FakeBackendFactory(backend)),
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None,
            CancellationToken.None).ConfigureAwait(false);
        using var bytes = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(64, 1));
        using var bytecode = bytes.AcquireRead(0, 4);
        using var pipeline = await device.CreatePipelineAsync(CreateComputePipelineDescription(bytecode, []))
            .ConfigureAwait(false);
        Assert.That(GraphicsRecordingUseAdmission.IsSupportedProfile, Is.True);
        var start = GC.GetAllocatedBytesForCurrentThread();
        var admission = GraphicsRecordingUseAdmission.Create(useCount, 80 + (192L * useCount));
        var carrierBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        var uses = new GraphicsResourceOwner.UseLease[useCount];
        var acquiredCount = 0;
        long allocatedBytes = 0;

        try
        {
            void Acquire(scoped ref CommandRecorder recorder)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();

                for (var index = 0; index < uses.Length; index++)
                {
                    uses[index] = device.AcquirePipelineForRecording(pipeline.Handle, admission).UseLease;
                    acquiredCount++;
                }

                allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            }

            using var result = await device.RecordPassAsync(
                new RenderPassId(400, 0), CommandRecorderPassKind.Copy, [], [], null, Acquire)
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            admission.ValidateComplete();
            Assert.That(carrierBytes, Is.InRange(1, 80));
            Assert.That(allocatedBytes, Is.InRange(1, 192L * useCount));
            Assert.That(pipeline.ResourceOwner.HasActiveUses, Is.True);
            _ = Assert.Throws<OutOfMemoryException>(() => pipeline.ResourceOwner.AcquireUse("exhausted", admission));

            foreach (var use in uses)
            {
                Assert.That(use.IsCoveredBy(admission), Is.True);
                use.Dispose();
            }

            Assert.That(pipeline.ResourceOwner.HasActiveUses, Is.False);
            Assert.That(admission.ManagedByteCharge, Is.EqualTo(80 + (192L * useCount)));
            TestContext.WriteLine($"D={useCount}: carrier={carrierBytes}, owner-thread use/node allocations={allocatedBytes}, allowance={admission.ManagedByteCharge}");

            if (useCount == 1)
            {
                var (retainedUseReference, retainedAdmissionReference) = AcquireUseWithoutCallerRoots(pipeline.ResourceOwner);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var hasUse = retainedUseReference.TryGetTarget(out var retainedUse);

                try
                {
                    Assert.That(hasUse, Is.True);
                    Assert.That(retainedAdmissionReference.TryGetTarget(out _), Is.True);
                    Assert.That(pipeline.ResourceOwner.HasActiveUses, Is.True);
                }
                finally
                {
                    retainedUse?.Dispose();
                }

                Assert.That(pipeline.ResourceOwner.HasActiveUses, Is.False);
            }
        }
        finally
        {
            for (var index = 0; index < acquiredCount; index++)
            {
                uses[index].Dispose();
            }

            pipeline.Dispose();
            await device.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task SkinnedNormalDiagnosticRecorderBoundsHazardsAndValidatesNativeWitness()
    {
        using var continuePipelineCreation = new ManualResetEventSlim(initialState: true);
        using var pipelineCreationEntered = new ManualResetEventSlim(initialState: false);
        var backendDevice = new FakeBackendDevice(
            CreateCapabilities(),
            pipelineCreationEntered,
            continuePipelineCreation);
        var registration = new GraphicsBackendRegistration(new FakeBackendFactory(backendDevice));
        var graphicsDevice = await GraphicsDevice.CreateAsync(
            ownerId: 31,
            registration,
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None,
            CancellationToken.None).ConfigureAwait(false);
        var bufferOwner = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(64, 1));
        var callerBytecodeLease = bufferOwner.AcquireRead(0, 4);
        GpuPipeline? pipeline = null;
        char[][] schedules = [
            ['A'],
            ['A', 'A'],
            ['A', 'B'],
            ['A', 'A', 'A'],
            ['A', 'B', 'C'],
            ['A', 'B', 'A'],
        ];

        try
        {
            ImmutableArray<ShaderResourceSignature> signatures = [
                new ShaderResourceSignature(ShaderBindingGroup.Frame, 0, ShaderResourceKind.StorageBuffer, arrayCount: 1),
                new ShaderResourceSignature(ShaderBindingGroup.Frame, 1, ShaderResourceKind.StorageBuffer, arrayCount: 1),
                new ShaderResourceSignature(ShaderBindingGroup.Frame, 2, ShaderResourceKind.StorageBuffer, arrayCount: 1),
                new ShaderResourceSignature(ShaderBindingGroup.Frame, 3, ShaderResourceKind.StorageBuffer, arrayCount: 1),
            ];
            pipeline = await graphicsDevice.CreatePipelineAsync(
                CreateComputePipelineDescription(callerBytecodeLease, signatures))
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);

            for (var scheduleIndex = 0; scheduleIndex < schedules.Length; scheduleIndex++)
            {
                var schedule = schedules[scheduleIndex];
                var geometryCount = 0;

                foreach (var geometryLabel in schedule)
                {
                    geometryCount = Math.Max(geometryCount, geometryLabel - 'A' + 1);
                }

                var resources = new GraphResourceRegistry(graphicsDevice, graphOwnerId: checked((uint)(100 + scheduleIndex)));
                var initializationPass = resources.AddPass(new RenderPassDescription("initialize", 0, [], false));
                var diagnosticPassId = resources.AddPass(new RenderPassDescription(
                    "diagnostic",
                    1,
                    [initializationPass],
                    hasExternalSideEffects: false));

                GraphBufferVersion CreateInitializedBuffer(string debugName, long byteLength)
                {
                    var initialVersion = resources.CreateTransientBuffer(new BufferDescription(
                        byteLength,
                        BufferUsage.Storage,
                        GpuMemoryKind.DeviceLocal,
                        structuredStride: null,
                        debugName));
                    return resources.WriteBuffer(
                        initializationPass,
                        initialVersion,
                        initialVersion.Range,
                        GraphBufferUsage.StorageWrite);
                }

                var unrelatedBuffer = CreateInitializedBuffer("diagnostic-unrelated", 16);
                var geometryBuffers = new GraphBufferVersion[geometryCount];

                for (var geometryIndex = 0; geometryIndex < geometryCount; geometryIndex++)
                {
                    geometryBuffers[geometryIndex] = CreateInitializedBuffer($"diagnostic-geometry-{geometryIndex}", 64);
                }

                var paletteBuffer = CreateInitializedBuffer("diagnostic-palette", 64);
                var workBufferLength = checked((long)schedule.Length * 32);
                var workBuffer = CreateInitializedBuffer("diagnostic-work", workBufferLength);
                var resultBuffer = CreateInitializedBuffer("diagnostic-results", checked((long)schedule.Length * 16));
                var diagnosticGeometryBuffers = new GraphBufferVersion[geometryCount];

                for (var geometryIndex = 0; geometryIndex < geometryCount; geometryIndex++)
                {
                    diagnosticGeometryBuffers[geometryIndex] = resources.ReadWriteBuffer(
                        diagnosticPassId,
                        geometryBuffers[geometryIndex],
                        geometryBuffers[geometryIndex].Range);
                }

                var diagnosticPaletteBuffer = resources.ReadWriteBuffer(
                    diagnosticPassId,
                    paletteBuffer,
                    paletteBuffer.Range);
                var diagnosticWorkBuffer = resources.ReadWriteBuffer(
                    diagnosticPassId,
                    workBuffer,
                    workBuffer.Range);
                var diagnosticResultBuffer = resources.ReadWriteBuffer(
                    diagnosticPassId,
                    resultBuffer,
                    resultBuffer.Range);
                var bindingSets = new BindingSet[schedule.Length];

                for (var drawIndex = 0; drawIndex < schedule.Length; drawIndex++)
                {
                    var geometryIndex = schedule[drawIndex] - 'A';
                    bindingSets[drawIndex] = new BindingSet(
                        [
                            new BufferBinding(
                                ShaderBindingGroup.Frame,
                                0,
                                ShaderResourceKind.StorageBuffer,
                                diagnosticGeometryBuffers[geometryIndex],
                                diagnosticGeometryBuffers[geometryIndex].Range),
                            new BufferBinding(
                                ShaderBindingGroup.Frame,
                                1,
                                ShaderResourceKind.StorageBuffer,
                                diagnosticPaletteBuffer,
                                diagnosticPaletteBuffer.Range),
                            new BufferBinding(
                                ShaderBindingGroup.Frame,
                                2,
                                ShaderResourceKind.StorageBuffer,
                                diagnosticWorkBuffer,
                                new GraphBufferRange(checked((long)drawIndex * 32), 16)),
                            new BufferBinding(
                                ShaderBindingGroup.Frame,
                                3,
                                ShaderResourceKind.StorageBuffer,
                                diagnosticResultBuffer,
                                diagnosticResultBuffer.Range),
                        ],
                        [],
                        []);
                }

                var declaredAccessesBuilder = ImmutableArray.CreateBuilder<GraphResourceAccess>();
                var nativeAccessesBuilder = ImmutableArray.CreateBuilder<GraphResourceAccess>();

                foreach (var access in resources.Accesses)
                {
                    nativeAccessesBuilder.Add(access);

                    if (access.PassId == diagnosticPassId)
                    {
                        declaredAccessesBuilder.Add(access);
                    }
                }

                var declaredAccesses = declaredAccessesBuilder.ToImmutable();
                var nativeAccesses = nativeAccessesBuilder.ToImmutable();
                var expectedBarrierCount = checked((3 * schedule.Length) - geometryCount - 2);
                var expectedCommandCount = checked((6 * schedule.Length) - geometryCount - 2);
                var pendingWriteCount = checked(schedule.Length + geometryCount + 2);
                var capacity = new CommandRecorderRecordingCapacity(
                    expectedCommandCount,
                    schedule.Length,
                    pendingWriteCount,
                    bindingCount: 4);
                var pipelineHandle = pipeline.Handle;
                var recipe = new SkinnedNormalDiagnosticRecordingRecipe(
                    diagnosticPassId,
                    graphicsDevice.DeviceEpoch,
                    pipelineHandle,
                    bindingSets,
                    diagnosticGeometryBuffers,
                    diagnosticPaletteBuffer,
                    diagnosticWorkBuffer,
                    diagnosticResultBuffer,
                    workPacketAlignment: 16,
                    declaredAccesses,
                    capacity);

                if (scheduleIndex == 1)
                {
                    var overlappingBindingSets = (BindingSet[])bindingSets.Clone();
                    var secondDrawBindings = overlappingBindingSets[1].Buffers;
                    overlappingBindingSets[1] = new BindingSet(
                        [
                            secondDrawBindings[0],
                            secondDrawBindings[1],
                            new BufferBinding(
                                ShaderBindingGroup.Frame,
                                2,
                                ShaderResourceKind.StorageBuffer,
                                diagnosticWorkBuffer,
                                new GraphBufferRange(0, 16)),
                            secondDrawBindings[3],
                        ],
                        [],
                        []);

                    _ = Assert.Throws<ArgumentException>(() => new SkinnedNormalDiagnosticRecordingRecipe(
                        diagnosticPassId,
                        graphicsDevice.DeviceEpoch,
                        pipelineHandle,
                        overlappingBindingSets,
                        diagnosticGeometryBuffers,
                        diagnosticPaletteBuffer,
                        diagnosticWorkBuffer,
                        diagnosticResultBuffer,
                        workPacketAlignment: 16,
                        declaredAccesses,
                        capacity));
                }

                void RecordDiagnostic(scoped ref CommandRecorder recorder)
                {
                    foreach (var bindingSet in bindingSets)
                    {
                        recorder.SetPipeline(pipelineHandle);
                        recorder.SetBindings(bindingSet);
                        recorder.Dispatch(new DispatchSize(1, 1, 1));
                    }
                }

                using var commandList = await graphicsDevice.RecordPassAsync(
                    diagnosticPassId,
                    CommandRecorderPassKind.Compute,
                    declaredAccesses,
                    [],
                    depthTargetFormat: null,
                    RecordDiagnostic,
                    capacity,
                    recipe)
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);

                var pipelineCommandCount = 0;
                Assert.That(recipe.RecordingUseAdmission.ClaimedUseCount, Is.EqualTo(schedule.Length));
                var bindingCommandCount = 0;
                var dispatchCommandCount = 0;
                var barrierCommandCount = 0;

                foreach (var command in commandList.Commands)
                {
                    switch (command.Kind)
                    {
                        case GraphicsCommandKind.SetPipeline:
                        {
                            pipelineCommandCount++;
                            break;
                        }

                        case GraphicsCommandKind.SetBindings:
                        {
                            bindingCommandCount++;
                            break;
                        }

                        case GraphicsCommandKind.Dispatch:
                        {
                            dispatchCommandCount++;
                            break;
                        }

                        case GraphicsCommandKind.StorageBarrier:
                        {
                            barrierCommandCount++;

                            if (command.StorageBarrierBuffer is not GraphBufferVersion barrierBuffer)
                            {
                                Assert.Fail("A diagnostic storage barrier must target a graph buffer.");
                                break;
                            }

                            Assert.That(command.StorageBarrierBufferRange, Is.EqualTo(barrierBuffer.Range));
                            Assert.That(command.StorageBarrierSourceReads, Is.True);
                            Assert.That(command.StorageBarrierSourceWrites, Is.True);
                            Assert.That(command.StorageBarrierDestinationReads, Is.True);
                            Assert.That(command.StorageBarrierDestinationWrites, Is.True);
                            break;
                        }

                        default:
                        {
                            Assert.Fail($"Unexpected diagnostic command kind {command.Kind}.");
                            break;
                        }
                    }
                }

                Assert.That(commandList.Commands.Length, Is.EqualTo(expectedCommandCount), $"schedule {scheduleIndex}");
                Assert.That(pipelineCommandCount, Is.EqualTo(schedule.Length), $"schedule {scheduleIndex}");
                Assert.That(bindingCommandCount, Is.EqualTo(schedule.Length), $"schedule {scheduleIndex}");
                Assert.That(dispatchCommandCount, Is.EqualTo(schedule.Length), $"schedule {scheduleIndex}");
                Assert.That(barrierCommandCount, Is.EqualTo(expectedBarrierCount), $"schedule {scheduleIndex}");
                recipe.ValidateNativeSubmission(
                    diagnosticPassId,
                    graphicsDevice.DeviceEpoch,
                    nativeAccesses,
                    commandList.Commands);
                _ = Assert.Throws<InvalidOperationException>(() => recipe.ValidateNativeSubmission(
                    diagnosticPassId,
                    checked(graphicsDevice.DeviceEpoch + 1),
                    nativeAccesses,
                    commandList.Commands));
                _ = Assert.Throws<InvalidOperationException>(() => recipe.ValidateNativeSubmission(
                    diagnosticPassId,
                    graphicsDevice.DeviceEpoch,
                    nativeAccesses,
                    []));
                commandList.Dispose();
                Assert.That(commandList.Commands.IsEmpty, Is.True);
                Assert.That(commandList.DiagnosticRecipe, Is.Null);
                Assert.That(recipe.RecordingUseAdmission.ManagedByteCharge, Is.GreaterThan(0));
            }
        }
        finally
        {
            pipeline?.Dispose();
            callerBytecodeLease.Dispose();
            bufferOwner.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    private sealed class FakeBackendDevice : IGraphicsBackendDevice
    {
        private readonly GraphicsCapabilities _capabilities;
        private readonly ManualResetEventSlim _continuePipelineCreation;
        private long _observedBytecodeLength;
        private int _pipelineCreationCount;
        private readonly ManualResetEventSlim _pipelineCreationEntered;
        private readonly Exception? _pipelineCreationException;
        private int _pipelineDisposalCount;

        internal FakeBackendDevice(
            GraphicsCapabilities capabilities,
            ManualResetEventSlim pipelineCreationEntered,
            ManualResetEventSlim continuePipelineCreation,
            Exception? pipelineCreationException = null)
        {
            _capabilities = capabilities;
            _pipelineCreationEntered = pipelineCreationEntered;
            _continuePipelineCreation = continuePipelineCreation;
            _pipelineCreationException = pipelineCreationException;
        }

        public GraphicsCapabilities Capabilities => _capabilities;

        internal long ObservedBytecodeLength => Interlocked.Read(ref _observedBytecodeLength);

        internal int PipelineCreationCount => Volatile.Read(ref _pipelineCreationCount);

        internal int PipelineDisposalCount => Volatile.Read(ref _pipelineDisposalCount);

        public IGraphicsBackendBufferReadback BeginBufferReadback(
            IGraphicsBackendBuffer source,
            GraphResourceState sourceState,
            long sourceOffset,
            nint byteLength)
            => throw new NotSupportedException();

        public IGraphicsBackendBuffer CreateBuffer(BufferDescription description)
            => throw new NotSupportedException();

        public IGraphicsBackendPipeline CreatePipeline(
            PipelineDescription description,
            ReadOnlySpan<BufferReadLease<byte>> bytecodeLeases)
        {
            if (bytecodeLeases.Length == 0)
            {
                throw new ArgumentException("Pipeline creation requires at least one shader lease.", nameof(bytecodeLeases));
            }

            _ = Interlocked.Increment(ref _pipelineCreationCount);

            if (_pipelineCreationException is Exception pipelineCreationException)
            {
                throw pipelineCreationException;
            }

            _pipelineCreationEntered.Set();

            if (!_continuePipelineCreation.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("The pipeline request test did not release its backend creation gate.");
            }

            _ = Interlocked.Exchange(ref _observedBytecodeLength, bytecodeLeases[0].Length);
            return new FakeBackendPipeline(this);
        }

        public IGraphicsBackendSampler CreateSampler(SamplerDescription description, float effectiveMaximumAnisotropy)
            => throw new NotSupportedException();

        public IGraphicsBackendTexture CreateTexture(TextureDescription description)
            => throw new NotSupportedException();

        public void Dispose()
        {
        }

        public GraphicsResourceAllocationInfo GetBufferAllocationInfo(BufferDescription description)
            => throw new NotSupportedException();

        public GraphicsResourceAllocationInfo GetTextureAllocationInfo(TextureDescription description)
            => throw new NotSupportedException();

        public TextureReadback ReadbackTexture(
            IGraphicsBackendTexture source,
            GraphResourceState sourceState,
            TextureSubresource subresource)
            => throw new NotSupportedException();

        public long SubmitGraph(GraphicsGraphSubmission submission)
            => throw new NotSupportedException();

        public long UploadBuffer(
            IGraphicsBackendBuffer destination,
            GraphResourceState destinationState,
            long destinationOffset,
            BufferReadLease<byte> source,
            nint sourceOffset,
            nint byteLength)
            => throw new NotSupportedException();

        public long UploadBuffer(
            IGraphicsBackendBuffer destination,
            GraphResourceState destinationState,
            long destinationOffset,
            ReadOnlySpan<byte> source)
            => throw new NotSupportedException();

        public long UploadTexture(
            IGraphicsBackendTexture destination,
            GraphResourceState destinationState,
            TextureSubresource subresource,
            BufferReadLease<byte> source,
            nint sourceOffset,
            TextureDataLayout sourceLayout)
            => throw new NotSupportedException();

        internal void PipelineDisposed()
            => _ = Interlocked.Increment(ref _pipelineDisposalCount);
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
            int frameSlotCount,
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

    private sealed class FakeBackendPipeline : IGraphicsBackendPipeline
    {
        private readonly FakeBackendDevice _device;
        private int _isDisposed;

        internal FakeBackendPipeline(FakeBackendDevice device)
        {
            _device = device;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                _device.PipelineDisposed();
            }
        }
    }
}
