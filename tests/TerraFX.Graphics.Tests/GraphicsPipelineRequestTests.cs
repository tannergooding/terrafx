// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
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
        var bufferOwner = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(64, 1));
        var callerBytecodeLease = bufferOwner.AcquireRead(0, 4);

        try
        {
            var description = CreateComputePipelineDescription(callerBytecodeLease, []);
            _ = Assert.ThrowsAsync<GraphicsDeviceLostException>(
                async () => await graphicsDevice.CreatePipelineAsync(description)
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));

            Assert.That(graphicsDevice.Status, Is.EqualTo(GraphicsDeviceStatus.Lost));
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
            var (layout, useLease) = graphicsDevice.AcquirePipelineForRecording(handle);

            try
            {
                Assert.That(layout.IsCompute, Is.True);
                Assert.That(layout.BindingLayoutVersion, Is.EqualTo(1));
                Assert.That(layout.ShaderReflections[0].Stage, Is.EqualTo(ShaderStage.Compute));
            }
            finally
            {
                useLease.Dispose();
            }

            pipeline.Dispose();
            pipeline = null;
            _ = Assert.Throws<ObjectDisposedException>(
                () => graphicsDevice.AcquirePipelineForRecording(handle));
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

        public OwnedBuffer<byte> ReadbackBuffer(IGraphicsBackendBuffer source, long sourceOffset, nint byteLength)
            => throw new NotSupportedException();

        public TextureReadback ReadbackTexture(IGraphicsBackendTexture source, TextureSubresource subresource)
            => throw new NotSupportedException();

        public long UploadBuffer(
            IGraphicsBackendBuffer destination,
            long destinationOffset,
            BufferReadLease<byte> source,
            nint sourceOffset,
            nint byteLength)
            => throw new NotSupportedException();

        public long UploadBuffer(IGraphicsBackendBuffer destination, long destinationOffset, ReadOnlySpan<byte> source)
            => throw new NotSupportedException();

        public long UploadTexture(
            IGraphicsBackendTexture destination,
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
