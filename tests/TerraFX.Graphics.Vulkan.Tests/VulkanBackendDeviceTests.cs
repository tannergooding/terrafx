// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using NUnit.Framework;
using TerraFX.Buffers;
using TerraFX.Content.Tools;
using TerraFX.Graphics;
using TerraFX.Graphics.Vulkan;
using TerraFX.Interop.Vulkan;

namespace TerraFX.Graphics.Vulkan.Tests;

[TestFixture]
internal sealed class VulkanBackendDeviceTests
{
    private static readonly ConcurrentBag<VulkanBackendDevice> s_quarantinedDevices = [];

    private static void AppendSpirvInstruction(List<uint> words, ushort opcode, params uint[] operands)
    {
        words.Add((checked((uint)operands.Length + 1) << 16) | opcode);
        words.AddRange(operands);
    }

    private static void AssertReadback(OwnedBuffer<byte> readback, byte[] expected)
    {
        using var lease = readback.AcquireReadLease(0, expected.Length);
        var actual = new byte[expected.Length];
        var copyAction = new ReadbackCopyAction(actual);
        lease.UseWindow(0, expected.Length, ref copyAction);
        Assert.That(actual, Is.EqualTo(expected));
    }

    private static void AssertTextureReadback(TextureReadback readback, byte[] expected)
    {
        using var lease = readback.AcquireReadLease();
        var actual = new byte[expected.Length];
        var copyAction = new ReadbackCopyAction(actual);
        lease.UseWindow(0, actual.Length, ref copyAction);
        Assert.That(actual, Is.EqualTo(expected));
    }

    private static OwnedBuffer<byte> CreateBytecodeStorage(byte[] bytecode)
    {
        var storage = UnmanagedBuffer<byte>.Allocate(
            bytecode.Length,
            new BufferAllocationOptions(bytecode.Length, alignment: 1));
        storage.CopyFrom(bytecode.AsSpan(), 0);
        return storage.TransferToOwnedBuffer();
    }

    private static OwnedBuffer<byte> CreateBytecodeStorage(string path)
        => CreateBytecodeStorage(File.ReadAllBytes(path));

    private static byte[] CreateMatrixUniformSpirv(
        uint matrixMajor,
        uint matrixStride,
        uint rowCount,
        uint columnCount)
    {
        var words = new List<uint> {
            0x07230203,
            0x00010600,
            0,
            10,
            0,
        };
        AppendSpirvInstruction(words, 17, 1);
        AppendSpirvInstruction(words, 15, 4, 9, 0x6E69616D, 0);
        AppendSpirvInstruction(words, 22, 2, 32);
        AppendSpirvInstruction(words, 23, 3, 2, rowCount);
        AppendSpirvInstruction(words, 24, 4, 3, columnCount);
        AppendSpirvInstruction(words, 30, 5, 4);
        AppendSpirvInstruction(words, 32, 6, 2, 5);
        AppendSpirvInstruction(words, 59, 6, 8, 2);
        AppendSpirvInstruction(words, 71, 5, 2);
        AppendSpirvInstruction(words, 71, 8, 34, 0);
        AppendSpirvInstruction(words, 71, 8, 33, 0);
        AppendSpirvInstruction(words, 72, 5, 0, 35, 0);
        AppendSpirvInstruction(words, 72, 5, 0, matrixMajor);
        AppendSpirvInstruction(words, 72, 5, 0, 7, matrixStride);
        return MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(words)).ToArray();
    }

    private static ShaderCompilerIdentity CreateShaderCompilerIdentity()
    {
        var digest = Convert.FromHexString("C6E82B70C14552F1DD58E4A79C93EEAB1567EEB0A9EE63A51564C410429BCE3E");
        return new ShaderCompilerIdentity(
            "Microsoft.Direct3D.DXC",
            "1.8.2505.1",
            "1.8.2505.32",
            "win-x64",
            ImmutableCollectionsMarshal.AsImmutableArray(digest));
    }

    private static GraphicsPipelineDescription CreateShaderFixtureDescription(
        BufferReadLease<byte> vertexBytecode,
        BufferReadLease<byte> pixelBytecode,
        PipelineBlendMode blendMode = PipelineBlendMode.Opaque,
        bool usesSampledResources = false,
        bool includesFrameUniformBuffer = true,
        bool includesStorageResources = false,
        int sampledImageBinding = 0,
        int sampledImageArrayCount = 1,
        int frameColorByteOffset = 0,
        int frameRecordByteLength = 16)
    {
        var compilerIdentity = CreateShaderCompilerIdentity();
        var pixelResources = ImmutableArray.CreateBuilder<ShaderResourceSignature>();

        if (includesFrameUniformBuffer)
        {
            pixelResources.Add(new ShaderResourceSignature(ShaderBindingGroup.Frame, 0, ShaderResourceKind.UniformBuffer, 1));
        }

        if (usesSampledResources)
        {
            pixelResources.Add(new ShaderResourceSignature(
                ShaderBindingGroup.Frame,
                sampledImageBinding,
                ShaderResourceKind.SampledImage,
                sampledImageArrayCount));
            pixelResources.Add(new ShaderResourceSignature(ShaderBindingGroup.Frame, 0, ShaderResourceKind.Sampler, 1));
        }

        if (includesStorageResources)
        {
            pixelResources.Add(new ShaderResourceSignature(ShaderBindingGroup.Frame, 0, ShaderResourceKind.StorageBuffer, 1));
            pixelResources.Add(new ShaderResourceSignature(ShaderBindingGroup.Frame, 1, ShaderResourceKind.StorageImage, 1));
        }

        ImmutableArray<ShaderRecordLayout> pixelRecordLayouts = includesFrameUniformBuffer
            ? [
                new ShaderRecordLayout(
                    "FrameData",
                    frameRecordByteLength,
                    [new ShaderFieldLayout(
                        "color",
                        ShaderValueType.Float4,
                        frameColorByteOffset,
                        16,
                        1,
                        0)]),
            ]
            : [];
        var pixelEntryPoint = (usesSampledResources, includesFrameUniformBuffer, includesStorageResources) switch {
            (false, true, false) => "PSMain",
            (true, false, false) => "PSSampledMain",
            (true, true, false) => "PSSampledFrameMain",
            (true, true, true) => "PSSampledFrameStorageMain",
            _ => throw new ArgumentException("A pixel shader fixture must declare a Frame UBO or sampled image and sampler."),
        };
        var vertexReflection = new ShaderReflection(
            ShaderStage.Vertex,
            "VSMain",
            compilerIdentity,
            [new ShaderIoSignature("SV_VertexID", 0, null, ShaderValueType.UInt1, ShaderInterpolation.Flat)],
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
            pixelEntryPoint,
            compilerIdentity,
            [],
            [new ShaderIoSignature("SV_Target", 0, null, ShaderValueType.Float4, ShaderInterpolation.Smooth)],
            pixelResources.ToImmutable(),
            pixelRecordLayouts,
            [],
            bindingLayoutVersion: 1,
            groupSizeX: 0,
            groupSizeY: 0,
            groupSizeZ: 0);
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

        return new GraphicsPipelineDescription(
            [vertexShader, pixelShader],
            [],
            vertexStride: 0,
            [TextureFormat.Rgba8Unorm],
            depthTargetFormat: null,
            PrimitiveTopology.TriangleList,
            blendMode,
            PipelineDepthMode.Disabled,
            PipelineCullMode.None,
            frontFaceClockwise: false,
            bindingLayoutVersion: 1);
    }

    private static GraphicsPipelineDescription CreateShaderPipelineDescription(
        BufferReadLease<byte> vertexBytecode,
        ShaderReflection vertexReflection,
        BufferReadLease<byte> pixelBytecode,
        ShaderReflection pixelReflection,
        PipelineBlendMode blendMode)
        => new(
            [
                new ShaderBytecodeInput(ShaderStage.Vertex, ShaderBinaryFormat.SpirV, vertexBytecode, vertexReflection),
                new ShaderBytecodeInput(ShaderStage.Pixel, ShaderBinaryFormat.SpirV, pixelBytecode, pixelReflection),
            ],
            [],
            vertexStride: 0,
            [TextureFormat.Rgba8Unorm],
            depthTargetFormat: null,
            PrimitiveTopology.TriangleList,
            blendMode,
            PipelineDepthMode.Disabled,
            PipelineCullMode.None,
            frontFaceClockwise: false,
            bindingLayoutVersion: 1);

    private static ShaderReflection CreateShaderReflectionWithStageIo(
        ShaderReflection reflection,
        ImmutableArray<ShaderIoSignature> inputs,
        ImmutableArray<ShaderIoSignature> outputs)
        => new(
            reflection.Stage,
            reflection.EntryPoint,
            reflection.CompilerIdentity,
            inputs,
            outputs,
            reflection.Resources,
            reflection.RecordLayouts,
            reflection.RequiredCapabilities,
            reflection.BindingLayoutVersion,
            reflection.GroupSizeX,
            reflection.GroupSizeY,
            reflection.GroupSizeZ);

    private static GraphicsPipelineDescription CreateTypedShaderFixtureDescription(
        string fixtureDirectory,
        string pixelStageName,
        BufferReadLease<byte> vertexBytecode,
        BufferReadLease<byte> pixelBytecode,
        PipelineBlendMode blendMode = PipelineBlendMode.Opaque)
        => CreateShaderPipelineDescription(
            vertexBytecode,
            M04ShaderFixtureCompiler.ReadTypedReflection(fixtureDirectory, "vertex"),
            pixelBytecode,
            M04ShaderFixtureCompiler.ReadTypedReflection(fixtureDirectory, pixelStageName),
            blendMode);

    private static VulkanBackendDevice CreateVulkanDeviceForNativeSmokeTest(
        Func<VkResult, VkResult>? transferWaitResultOverride = null,
        Action? afterNativeStartupObjectsCreated = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return VulkanBackendDevice.Create(
                GraphicsDeviceProfile.OffscreenNative,
                transferWaitResultOverride,
                afterNativeStartupObjectsCreated,
                cancellationToken);
        }
        catch (DllNotFoundException exception)
        {
            Assert.Ignore($"No Vulkan loader is installed: {exception.Message}");
            throw;
        }
        catch (EntryPointNotFoundException exception)
        {
            Assert.Ignore($"The installed Vulkan loader lacks a required entry point: {exception.Message}");
            throw;
        }
        catch (NotSupportedException exception)
        {
            Assert.Ignore($"No available Vulkan device satisfies the pipeline smoke test: {exception.Message}");
            throw;
        }
    }

    [Test]
    public void OffscreenNativeCreationReportsAndReleasesARealDevice()
    {
        try
        {
            using var device = VulkanBackendDevice.Create(
                GraphicsDeviceProfile.OffscreenNative,
                CancellationToken.None);

            Assert.That(device.Capabilities.Backend, Is.EqualTo(GraphicsBackend.Vulkan));
            Assert.That(device.Capabilities.Profile, Is.EqualTo(GraphicsDeviceProfile.OffscreenNative));
            Assert.That(device.Capabilities.AdapterId.NativeIdentifier.Length, Is.EqualTo(16));
            Assert.That(device.Capabilities.AdapterName, Is.Not.Empty);
            Assert.That(device.Capabilities.DriverName, Is.Not.Empty);
            Assert.That(device.Capabilities.ApiVersion.Major, Is.GreaterThanOrEqualTo(1));
            Assert.That(
                device.Capabilities.ApiVersion.Major != 1 || device.Capabilities.ApiVersion.Minor >= 3,
                Is.True);
            Assert.That(
                device.Capabilities.ImplementedFeatures,
                Does.Contain(GraphicsFeature.TimelineSemaphore)
                    .And.Contain(GraphicsFeature.Synchronization2)
                    .And.Contain(GraphicsFeature.DynamicRendering));
            TestContext.WriteLine(
                $"Adapter={device.Capabilities.AdapterName}; Driver={device.Capabilities.DriverName} "
                + $"{device.Capabilities.DriverVersion}; API={device.Capabilities.ApiVersion.Major}.{device.Capabilities.ApiVersion.Minor}; "
                + $"UUID={Convert.ToHexString(device.Capabilities.AdapterId.NativeIdentifier.AsSpan())}");
        }
        catch (DllNotFoundException exception)
        {
            Assert.Ignore($"No Vulkan loader is installed: {exception.Message}");
            return;
        }
        catch (EntryPointNotFoundException exception)
        {
            Assert.Ignore($"The installed Vulkan loader lacks a required entry point: {exception.Message}");
            return;
        }
        catch (NotSupportedException exception)
        {
            Assert.Ignore($"No available Vulkan device satisfies OffscreenNative: {exception.Message}");
            return;
        }
    }

    [Test]
    public void OffscreenNativeDeviceAllocatesAndPopulatesUniformBufferDescriptorSet()
    {
        var fixtureDirectory = Environment.GetEnvironmentVariable("TERRAFX_M04_SHADER_FIXTURE_DIRECTORY");

        if (string.IsNullOrWhiteSpace(fixtureDirectory))
        {
            Assert.Ignore("The compiled M04 shader fixture is not selected through TERRAFX_M04_SHADER_FIXTURE_DIRECTORY.");
            return;
        }

        using var device = CreateVulkanDeviceForNativeSmokeTest();
        using var vertexStorage = CreateBytecodeStorage(Path.Combine(
            fixtureDirectory,
            "M04OffscreenTriangle.vertex.spv"));
        using var pixelStorage = CreateBytecodeStorage(Path.Combine(
            fixtureDirectory,
            "M04OffscreenTriangle.pixel.spv"));
        using var vertexLease = vertexStorage.AcquireReadLease(0, vertexStorage.Length);
        using var pixelLease = pixelStorage.AcquireReadLease(0, pixelStorage.Length);
        using var pipeline = device.CreatePipeline(
            CreateTypedShaderFixtureDescription(fixtureDirectory, "pixel", vertexLease, pixelLease),
            [vertexLease, pixelLease]);
        using var buffer = device.CreateBuffer(
            new BufferDescription(
                16,
                BufferUsage.Uniform,
                GpuMemoryKind.DeviceLocal,
                structuredStride: null,
                debugName: "frame-data"));
        Assert.That(
            () => device.CreateUniformBufferDescriptorSet(
                pipeline,
                [new VulkanBackendUniformBufferBinding(
                    ShaderBindingGroup.Frame,
                    1,
                    buffer,
                    0,
                    16)]),
            Throws.ArgumentException);
        Assert.That(
            () => device.CreateUniformBufferDescriptorSet(
                pipeline,
                [new VulkanBackendUniformBufferBinding(
                    ShaderBindingGroup.Frame,
                    0,
                    buffer,
                    0,
                    17)]),
            Throws.ArgumentException);
        using var descriptorSet = device.CreateUniformBufferDescriptorSet(
            pipeline,
            [new VulkanBackendUniformBufferBinding(
                ShaderBindingGroup.Frame,
                0,
                buffer,
                0,
                16)]);

        Assert.That(descriptorSet.IsDisposed, Is.False);
        Assert.That(
            device.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("live descriptor sets"));
        Assert.That(
            pipeline.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("descriptor sets remain live"));
        Assert.That(
            buffer.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("descriptor sets remain live"));

        descriptorSet.Dispose();
        buffer.Dispose();
        pipeline.Dispose();
        device.Dispose();
    }

    [Test]
    public void OffscreenNativeDeviceAllocatesAndPopulatesVulkanImageSamplerDescriptorSet()
    {
        var fixtureDirectory = Environment.GetEnvironmentVariable("TERRAFX_M04_SHADER_FIXTURE_DIRECTORY");

        if (string.IsNullOrWhiteSpace(fixtureDirectory))
        {
            Assert.Ignore("The compiled M04 shader fixture is not selected through TERRAFX_M04_SHADER_FIXTURE_DIRECTORY.");
            return;
        }

        using var device = CreateVulkanDeviceForNativeSmokeTest();
        using var vertexStorage = CreateBytecodeStorage(Path.Combine(
            fixtureDirectory,
            "M04OffscreenTriangle.vertex.spv"));
        using var pixelStorage = CreateBytecodeStorage(Path.Combine(
            fixtureDirectory,
            "M04OffscreenTriangle.pixel-sampled.spv"));
        using var vertexLease = vertexStorage.AcquireReadLease(0, vertexStorage.Length);
        using var pixelLease = pixelStorage.AcquireReadLease(0, pixelStorage.Length);
        using var pipeline = device.CreatePipeline(
            CreateTypedShaderFixtureDescription(
                fixtureDirectory,
                "pixel-sampled",
                vertexLease,
                pixelLease),
            [vertexLease, pixelLease]);
        using var texture = device.CreateTexture(
            new TextureDescription(
                TextureDimension.Dimension2D,
                1,
                1,
                depth: 1,
                arrayLayers: 1,
                mipLevels: 1,
                TextureFormat.Rgba8Unorm,
                TextureUsage.Sampled | TextureUsage.CopySource | TextureUsage.CopyDestination,
                "sampled-descriptor-texture"));
        var sourceBytes = new byte[] { 0x11, 0x32, 0x53, 0xF4 };
        var sourceStorage = UnmanagedBuffer<byte>.Allocate(
            sourceBytes.Length,
            new BufferAllocationOptions(sourceBytes.Length, alignment: 1));
        sourceStorage.CopyFrom(sourceBytes.AsSpan(), 0);

        using var ownedSource = sourceStorage.TransferToOwnedBuffer();
        using var sourceLease = ownedSource.AcquireReadLease(0, sourceBytes.Length);
        var subresource = new TextureSubresource(0, 0, TextureAspect.Color);
        Assert.That(
            device.UploadTexture(
                texture,
                subresource,
                sourceLease,
                0,
                new TextureDataLayout(rowPitch: 4, slicePitch: 4)),
            Is.GreaterThan(0));
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

        Assert.That(
            () => device.CreateSampledImageSamplerDescriptorSet(
                pipeline,
                [new VulkanBackendSampledImageBinding(ShaderBindingGroup.Frame, 1, texture)],
                [new VulkanBackendSamplerBinding(ShaderBindingGroup.Frame, 0, sampler)]),
            Throws.ArgumentException);
        Assert.That(
            () => device.CreateSampledImageSamplerDescriptorSet(
                pipeline,
                [new VulkanBackendSampledImageBinding(ShaderBindingGroup.Frame, 0, texture)],
                []),
            Throws.ArgumentException);

        using var descriptorSet = device.CreateSampledImageSamplerDescriptorSet(
            pipeline,
            [new VulkanBackendSampledImageBinding(ShaderBindingGroup.Frame, 0, texture)],
            [new VulkanBackendSamplerBinding(ShaderBindingGroup.Frame, 0, sampler)]);
        Assert.That(descriptorSet.IsDisposed, Is.False);
        Assert.That(
            device.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("live descriptor sets"));
        Assert.That(
            pipeline.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("descriptor sets remain live"));
        Assert.That(
            texture.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("descriptor sets remain live"));
        Assert.That(
            sampler.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("descriptor sets remain live"));

        descriptorSet.Dispose();
        texture.Dispose();
        sampler.Dispose();
        pipeline.Dispose();
        device.Dispose();
    }

    [Test]
    public void OffscreenNativeDeviceAllocatesAndPopulatesVulkanMixedFrameDescriptorSet()
    {
        var fixtureDirectory = Environment.GetEnvironmentVariable("TERRAFX_M04_SHADER_FIXTURE_DIRECTORY");

        if (string.IsNullOrWhiteSpace(fixtureDirectory))
        {
            Assert.Ignore("The compiled M04 shader fixture is not selected through TERRAFX_M04_SHADER_FIXTURE_DIRECTORY.");
            return;
        }

        using var device = CreateVulkanDeviceForNativeSmokeTest();
        using var vertexStorage = CreateBytecodeStorage(Path.Combine(
            fixtureDirectory,
            "M04OffscreenTriangle.vertex.spv"));
        using var pixelStorage = CreateBytecodeStorage(Path.Combine(
            fixtureDirectory,
            "M04OffscreenTriangle.pixel-sampled-frame.spv"));
        using var vertexLease = vertexStorage.AcquireReadLease(0, vertexStorage.Length);
        using var pixelLease = pixelStorage.AcquireReadLease(0, pixelStorage.Length);
        using var pipeline = device.CreatePipeline(
            CreateTypedShaderFixtureDescription(
                fixtureDirectory,
                "pixel-sampled-frame",
                vertexLease,
                pixelLease),
            [vertexLease, pixelLease]);
        using var buffer = device.CreateBuffer(
            new BufferDescription(
                16,
                BufferUsage.Uniform,
                GpuMemoryKind.DeviceLocal,
                structuredStride: null,
                debugName: "mixed-frame-data"));
        using var texture = device.CreateTexture(
            new TextureDescription(
                TextureDimension.Dimension2D,
                1,
                1,
                depth: 1,
                arrayLayers: 1,
                mipLevels: 1,
                TextureFormat.Rgba8Unorm,
                TextureUsage.Sampled | TextureUsage.CopySource | TextureUsage.CopyDestination,
                "mixed-frame-texture"));
        var sourceBytes = new byte[] { 0x11, 0x32, 0x53, 0xF4 };
        var sourceStorage = UnmanagedBuffer<byte>.Allocate(
            sourceBytes.Length,
            new BufferAllocationOptions(sourceBytes.Length, alignment: 1));
        sourceStorage.CopyFrom(sourceBytes.AsSpan(), 0);

        using var ownedSource = sourceStorage.TransferToOwnedBuffer();
        using var sourceLease = ownedSource.AcquireReadLease(0, sourceBytes.Length);
        var subresource = new TextureSubresource(0, 0, TextureAspect.Color);
        Assert.That(
            device.UploadTexture(
                texture,
                subresource,
                sourceLease,
                0,
                new TextureDataLayout(rowPitch: 4, slicePitch: 4)),
            Is.GreaterThan(0));
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
        var imageBindings = ImmutableArray.Create(new VulkanBackendSampledImageBinding(
            ShaderBindingGroup.Frame,
            0,
            texture));
        var samplerBindings = ImmutableArray.Create(new VulkanBackendSamplerBinding(
            ShaderBindingGroup.Frame,
            0,
            sampler));

        Assert.That(
            () => device.CreateFrameDescriptorSet(
                pipeline,
                [],
                [],
                imageBindings,
                [],
                samplerBindings),
            Throws.ArgumentException);

        using var descriptorSet = device.CreateFrameDescriptorSet(
            pipeline,
            [new VulkanBackendUniformBufferBinding(
                ShaderBindingGroup.Frame,
                0,
                buffer,
                0,
                16)],
            [],
            imageBindings,
            [],
            samplerBindings);
        Assert.That(descriptorSet.IsDisposed, Is.False);
        Assert.That(descriptorSet.Buffers.Length, Is.EqualTo(1));
        Assert.That(descriptorSet.Textures.Length, Is.EqualTo(1));
        Assert.That(descriptorSet.Samplers.Length, Is.EqualTo(1));
        Assert.That(
            device.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("live descriptor sets"));
        Assert.That(
            pipeline.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("descriptor sets remain live"));
        Assert.That(
            buffer.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("descriptor sets remain live"));
        Assert.That(
            texture.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("descriptor sets remain live"));
        Assert.That(
            sampler.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("descriptor sets remain live"));

        descriptorSet.Dispose();
        buffer.Dispose();
        texture.Dispose();
        sampler.Dispose();
        pipeline.Dispose();
        device.Dispose();
    }

    [Test]
    public void OffscreenNativeDeviceAllocatesAndPopulatesVulkanStorageDescriptorSet()
    {
        var fixtureDirectory = Environment.GetEnvironmentVariable("TERRAFX_M04_SHADER_FIXTURE_DIRECTORY");

        if (string.IsNullOrWhiteSpace(fixtureDirectory))
        {
            Assert.Ignore("The compiled M04 shader fixture is not selected through TERRAFX_M04_SHADER_FIXTURE_DIRECTORY.");
            return;
        }

        using var device = CreateVulkanDeviceForNativeSmokeTest();
        using var vertexStorage = CreateBytecodeStorage(Path.Combine(
            fixtureDirectory,
            "M04OffscreenTriangle.vertex.spv"));
        using var pixelStorage = CreateBytecodeStorage(Path.Combine(
            fixtureDirectory,
            "M04OffscreenTriangle.pixel-sampled-frame-storage.spv"));
        using var vertexLease = vertexStorage.AcquireReadLease(0, vertexStorage.Length);
        using var pixelLease = pixelStorage.AcquireReadLease(0, pixelStorage.Length);
        using var pipeline = device.CreatePipeline(
            CreateTypedShaderFixtureDescription(
                fixtureDirectory,
                "pixel-sampled-frame-storage",
                vertexLease,
                pixelLease),
            [vertexLease, pixelLease]);
        using var uniformBuffer = device.CreateBuffer(
            new BufferDescription(
                16,
                BufferUsage.Uniform,
                GpuMemoryKind.DeviceLocal,
                structuredStride: null,
                debugName: "storage-fixture-frame-data"));
        using var storageBuffer = device.CreateBuffer(
            new BufferDescription(
                16,
                BufferUsage.Storage,
                GpuMemoryKind.DeviceLocal,
                structuredStride: 4,
                debugName: "storage-fixture-buffer"));
        using var sampledTexture = device.CreateTexture(
            new TextureDescription(
                TextureDimension.Dimension2D,
                1,
                1,
                depth: 1,
                arrayLayers: 1,
                mipLevels: 1,
                TextureFormat.Rgba8Unorm,
                TextureUsage.Sampled | TextureUsage.CopySource | TextureUsage.CopyDestination,
                "storage-fixture-sampled-texture"));
        using var storageTexture = device.CreateTexture(
            new TextureDescription(
                TextureDimension.Dimension2D,
                1,
                1,
                depth: 1,
                arrayLayers: 1,
                mipLevels: 1,
                TextureFormat.R32UInt,
                TextureUsage.Storage | TextureUsage.CopySource | TextureUsage.CopyDestination,
                "storage-fixture-storage-texture"));
        var sourceBytes = new byte[] { 0x11, 0x32, 0x53, 0xF4 };
        var sourceStorage = UnmanagedBuffer<byte>.Allocate(
            sourceBytes.Length,
            new BufferAllocationOptions(sourceBytes.Length, alignment: 1));
        sourceStorage.CopyFrom(sourceBytes.AsSpan(), 0);

        using var ownedSource = sourceStorage.TransferToOwnedBuffer();
        using var sourceLease = ownedSource.AcquireReadLease(0, sourceBytes.Length);
        var subresource = new TextureSubresource(0, 0, TextureAspect.Color);
        var layout = new TextureDataLayout(rowPitch: 4, slicePitch: 4);
        Assert.That(device.UploadTexture(sampledTexture, subresource, sourceLease, 0, layout), Is.GreaterThan(0));
        Assert.That(device.UploadTexture(storageTexture, subresource, sourceLease, 0, layout), Is.GreaterThan(0));
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
        var uniformBufferBindings = ImmutableArray.Create(new VulkanBackendUniformBufferBinding(
            ShaderBindingGroup.Frame,
            0,
            uniformBuffer,
            0,
            16));
        var storageBufferBindings = ImmutableArray.Create(new VulkanBackendStorageBufferBinding(
            ShaderBindingGroup.Frame,
            0,
            storageBuffer,
            0,
            16));
        var imageBindings = ImmutableArray.Create(new VulkanBackendSampledImageBinding(
            ShaderBindingGroup.Frame,
            0,
            sampledTexture));
        var storageImageBindings = ImmutableArray.Create(new VulkanBackendStorageImageBinding(
            ShaderBindingGroup.Frame,
            1,
            storageTexture));
        var samplerBindings = ImmutableArray.Create(new VulkanBackendSamplerBinding(
            ShaderBindingGroup.Frame,
            0,
            sampler));

        Assert.That(
            () => device.CreateFrameDescriptorSet(
                pipeline,
                uniformBufferBindings,
                storageBufferBindings,
                imageBindings,
                [],
                samplerBindings),
            Throws.ArgumentException);
        Assert.That(
            () => device.CreateFrameDescriptorSet(
                pipeline,
                uniformBufferBindings,
                [new VulkanBackendStorageBufferBinding(
                    ShaderBindingGroup.Frame,
                    0,
                    uniformBuffer,
                    0,
                    16)],
                imageBindings,
                storageImageBindings,
                samplerBindings),
            Throws.ArgumentException);

        using var descriptorSet = device.CreateFrameDescriptorSet(
            pipeline,
            uniformBufferBindings,
            storageBufferBindings,
            imageBindings,
            storageImageBindings,
            samplerBindings);
        Assert.That(pipeline.Resources.Length, Is.EqualTo(5));
        Assert.That(descriptorSet.IsDisposed, Is.False);
        Assert.That(descriptorSet.Buffers.Length, Is.EqualTo(2));
        Assert.That(descriptorSet.Textures.Length, Is.EqualTo(2));
        Assert.That(descriptorSet.Samplers.Length, Is.EqualTo(1));
        Assert.That(descriptorSet.ImageViews.Length, Is.EqualTo(2));
        Assert.That(
            device.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("live descriptor sets"));
        Assert.That(
            pipeline.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("descriptor sets remain live"));
        Assert.That(
            uniformBuffer.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("descriptor sets remain live"));
        Assert.That(
            storageBuffer.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("descriptor sets remain live"));
        Assert.That(
            sampledTexture.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("descriptor sets remain live"));
        Assert.That(
            storageTexture.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("descriptor sets remain live"));
        Assert.That(
            sampler.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("descriptor sets remain live"));

        descriptorSet.Dispose();
        uniformBuffer.Dispose();
        storageBuffer.Dispose();
        sampledTexture.Dispose();
        storageTexture.Dispose();
        sampler.Dispose();
        pipeline.Dispose();
        device.Dispose();
    }

    [Test]
    public void OffscreenNativeDeviceAllocatesAndReleasesATexture()
    {
        try
        {
            using var device = VulkanBackendDevice.Create(
                GraphicsDeviceProfile.OffscreenNative,
                CancellationToken.None);
            using (var texture = device.CreateTexture(
                new TextureDescription(
                    TextureDimension.Dimension2D,
                    64,
                    32,
                    depth: 1,
                    arrayLayers: 2,
                    mipLevels: 2,
                    format: TextureFormat.Rgba8Unorm,
                    usage: TextureUsage.CopySource | TextureUsage.CopyDestination,
                    debugName: "texture-allocation")))
            {
                Assert.That(texture.Description.Width, Is.EqualTo(64));
                Assert.That(texture.Description.ArrayLayers, Is.EqualTo(2));
                Assert.That(
                    texture.GetLayout(new TextureSubresource(1, 1, TextureAspect.Color)),
                    Is.EqualTo(VkImageLayout.VK_IMAGE_LAYOUT_UNDEFINED));
                Assert.That(
                    device.Dispose,
                    Throws.InvalidOperationException.With.Message.Contains("live texture owners"));
            }

            device.Dispose();
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
            Assert.Ignore($"No available Vulkan device satisfies the texture allocation smoke test: {exception.Message}");
        }
    }

    [Test]
    public void OffscreenNativeDeviceAllocatesEachBufferMemoryKindAndRetainsLiveOwners()
    {
        try
        {
            using var device = VulkanBackendDevice.Create(
                GraphicsDeviceProfile.OffscreenNative,
                CancellationToken.None);
            using (var deviceLocalBuffer = device.CreateBuffer(
                new BufferDescription(
                    4096,
                    BufferUsage.CopySource | BufferUsage.CopyDestination,
                    GpuMemoryKind.DeviceLocal,
                    structuredStride: null,
                    debugName: "device-local")))
            using (var uploadBuffer = device.CreateBuffer(
                new BufferDescription(
                    4096,
                    BufferUsage.CopySource,
                    GpuMemoryKind.Upload,
                    structuredStride: null,
                    debugName: "upload")))
            using (var readbackBuffer = device.CreateBuffer(
                new BufferDescription(
                    4096,
                    BufferUsage.CopyDestination,
                    GpuMemoryKind.Readback,
                    structuredStride: null,
                    debugName: "readback")))
            {
                Assert.That(deviceLocalBuffer.Description.ByteLength, Is.EqualTo(4096));
                Assert.That(
                    (uploadBuffer.MemoryProperties & VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT) != 0,
                    Is.True);
                Assert.That(
                    (readbackBuffer.MemoryProperties & VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT) != 0,
                    Is.True);
                Assert.That(
                    device.Dispose,
                    Throws.InvalidOperationException.With.Message.Contains("live buffer owners"));
            }

            device.Dispose();
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
            Assert.Ignore($"No available Vulkan device satisfies the buffer allocation smoke test: {exception.Message}");
        }
    }

    [Test]
    public void OffscreenNativeDeviceCreatesAndRetainsASampler()
    {
        try
        {
            using var device = VulkanBackendDevice.Create(
                GraphicsDeviceProfile.OffscreenNative,
                CancellationToken.None);
            var description = new SamplerDescription(
                SamplerFilter.Linear,
                SamplerFilter.Nearest,
                SamplerFilter.None,
                SamplerAddressMode.Clamp,
                SamplerAddressMode.Repeat,
                SamplerAddressMode.Clamp,
                maximumAnisotropy: 16.0f);
            using (var sampler = device.CreateSampler(description, effectiveMaximumAnisotropy: 1.0f))
            {
                Assert.That(device.Capabilities.Limits.MaximumSamplerAnisotropy, Is.EqualTo(1.0f));
                Assert.That(
                    device.Dispose,
                    Throws.InvalidOperationException.With.Message.Contains("live sampler owners"));
            }

            device.Dispose();
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
            Assert.Ignore($"No available Vulkan device satisfies the sampler smoke test: {exception.Message}");
        }
    }

    [Test]
    public void OffscreenNativeDeviceCreatesPipelineFromCompiledShaderFixture()
    {
        var fixtureDirectory = Environment.GetEnvironmentVariable("TERRAFX_M04_SHADER_FIXTURE_DIRECTORY");

        if (string.IsNullOrWhiteSpace(fixtureDirectory))
        {
            Assert.Ignore("The compiled M04 shader fixture is not selected through TERRAFX_M04_SHADER_FIXTURE_DIRECTORY.");
            return;
        }

        using var device = CreateVulkanDeviceForNativeSmokeTest();
        using var vertexStorage = CreateBytecodeStorage(Path.Combine(
            fixtureDirectory,
            "M04OffscreenTriangle.vertex.spv"));
        using var pixelStorage = CreateBytecodeStorage(Path.Combine(
            fixtureDirectory,
            "M04OffscreenTriangle.pixel.spv"));
        using var vertexLease = vertexStorage.AcquireReadLease(0, vertexStorage.Length);
        using var pixelLease = pixelStorage.AcquireReadLease(0, pixelStorage.Length);
        using var pipeline = device.CreatePipeline(
            CreateTypedShaderFixtureDescription(
                fixtureDirectory,
                "pixel",
                vertexLease,
                pixelLease,
                PipelineBlendMode.PremultipliedAlpha),
            [vertexLease, pixelLease]);
        Assert.That(pipeline.IsDisposed, Is.False);
        Assert.That(pipeline.DescriptorSetLayouts.Length, Is.EqualTo(1));
        Assert.That(
            device.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("live pipeline owners"));
    }

    [Test]
    public void OffscreenNativeDeviceCreatesPipelineFromTypedCompiledReflection()
    {
        var fixtureDirectory = Environment.GetEnvironmentVariable("TERRAFX_M04_SHADER_FIXTURE_DIRECTORY");

        if (string.IsNullOrWhiteSpace(fixtureDirectory))
        {
            Assert.Ignore("The compiled M04 shader fixture is not selected through TERRAFX_M04_SHADER_FIXTURE_DIRECTORY.");
            return;
        }

        using var device = CreateVulkanDeviceForNativeSmokeTest();
        using var vertexStorage = CreateBytecodeStorage(Path.Combine(
            fixtureDirectory,
            "M04OffscreenTriangle.vertex.spv"));
        using var pixelStorage = CreateBytecodeStorage(Path.Combine(
            fixtureDirectory,
            "M04OffscreenTriangle.pixel-sampled-frame-storage.spv"));
        using var vertexLease = vertexStorage.AcquireReadLease(0, vertexStorage.Length);
        using var pixelLease = pixelStorage.AcquireReadLease(0, pixelStorage.Length);

        using var pipeline = device.CreatePipeline(
            CreateTypedShaderFixtureDescription(
                fixtureDirectory,
                "pixel-sampled-frame-storage",
                vertexLease,
                pixelLease),
            [vertexLease, pixelLease]);

        Assert.That(pipeline.IsDisposed, Is.False);
        Assert.That(pipeline.DescriptorSetLayouts.Length, Is.EqualTo(1));
    }

    [Test]
    public void OffscreenNativeDeviceCreatesSampledImageAndSamplerPipelineLayoutFromFixture()
    {
        var fixtureDirectory = Environment.GetEnvironmentVariable("TERRAFX_M04_SHADER_FIXTURE_DIRECTORY");

        if (string.IsNullOrWhiteSpace(fixtureDirectory))
        {
            Assert.Ignore("The compiled M04 shader fixture is not selected through TERRAFX_M04_SHADER_FIXTURE_DIRECTORY.");
            return;
        }

        using var device = CreateVulkanDeviceForNativeSmokeTest();
        using var vertexStorage = CreateBytecodeStorage(Path.Combine(
            fixtureDirectory,
            "M04OffscreenTriangle.vertex.spv"));
        using var pixelStorage = CreateBytecodeStorage(Path.Combine(
            fixtureDirectory,
            "M04OffscreenTriangle.pixel-sampled.spv"));
        using var vertexLease = vertexStorage.AcquireReadLease(0, vertexStorage.Length);
        using var pixelLease = pixelStorage.AcquireReadLease(0, pixelStorage.Length);
        using var pipeline = device.CreatePipeline(
            CreateTypedShaderFixtureDescription(
                fixtureDirectory,
                "pixel-sampled",
                vertexLease,
                pixelLease),
            [vertexLease, pixelLease]);

        Assert.That(pipeline.IsDisposed, Is.False);
        Assert.That(pipeline.DescriptorSetLayouts.Length, Is.EqualTo(1));
        Assert.That(pipeline.Resources.Length, Is.EqualTo(2));

        var hasSampledImage = false;
        var hasSampler = false;

        for (var index = 0; index < pipeline.Resources.Length; index++)
        {
            var resource = pipeline.Resources[index];

            if ((resource.Group != ShaderBindingGroup.Frame)
                || (resource.Binding != 0)
                || (resource.ArrayCount != 1))
            {
                continue;
            }

            switch (resource.Kind)
            {
                case ShaderResourceKind.SampledImage:
                {
                    hasSampledImage = true;
                    break;
                }

                case ShaderResourceKind.Sampler:
                {
                    hasSampler = true;
                    break;
                }
            }
        }

        Assert.That(hasSampledImage, Is.True);
        Assert.That(hasSampler, Is.True);
    }

    [Test]
    public void OffscreenNativeDevicePipelineCreationFailureReleasesPartialObjects()
    {
        var fixtureDirectory = Environment.GetEnvironmentVariable("TERRAFX_M04_SHADER_FIXTURE_DIRECTORY");

        if (string.IsNullOrWhiteSpace(fixtureDirectory))
        {
            Assert.Ignore("The compiled M04 shader fixture is not selected through TERRAFX_M04_SHADER_FIXTURE_DIRECTORY.");
            return;
        }

        using var device = CreateVulkanDeviceForNativeSmokeTest();
        using var vertexStorage = CreateBytecodeStorage(Path.Combine(
            fixtureDirectory,
            "M04OffscreenTriangle.vertex.spv"));
        var invalidPixelBytecode = File.ReadAllBytes(Path.Combine(
            fixtureDirectory,
            "M04OffscreenTriangle.pixel.spv"));
        invalidPixelBytecode[0] = 0;
        using var pixelStorage = CreateBytecodeStorage(invalidPixelBytecode);
        using var vertexLease = vertexStorage.AcquireReadLease(0, vertexStorage.Length);
        using var pixelLease = pixelStorage.AcquireReadLease(0, pixelStorage.Length);
        var description = CreateShaderFixtureDescription(vertexLease, pixelLease);

        Assert.That(
            () => device.CreatePipeline(description, [vertexLease, pixelLease]),
            Throws.ArgumentException);
        device.Dispose();
    }

    [Test]
    public void OffscreenNativeDevicePipelineCreationRejectsMismatchedSampledImageBinding()
    {
        var fixtureDirectory = Environment.GetEnvironmentVariable("TERRAFX_M04_SHADER_FIXTURE_DIRECTORY");

        if (string.IsNullOrWhiteSpace(fixtureDirectory))
        {
            Assert.Ignore("The compiled M04 shader fixture is not selected through TERRAFX_M04_SHADER_FIXTURE_DIRECTORY.");
            return;
        }

        using var device = CreateVulkanDeviceForNativeSmokeTest();
        using var vertexStorage = CreateBytecodeStorage(Path.Combine(
            fixtureDirectory,
            "M04OffscreenTriangle.vertex.spv"));
        using var pixelStorage = CreateBytecodeStorage(Path.Combine(
            fixtureDirectory,
            "M04OffscreenTriangle.pixel-sampled.spv"));
        using var vertexLease = vertexStorage.AcquireReadLease(0, vertexStorage.Length);
        using var pixelLease = pixelStorage.AcquireReadLease(0, pixelStorage.Length);
        var description = CreateShaderFixtureDescription(
            vertexLease,
            pixelLease,
            usesSampledResources: true,
            includesFrameUniformBuffer: false,
            sampledImageBinding: 1);

        Assert.That(
            () => device.CreatePipeline(description, [vertexLease, pixelLease]),
            Throws.ArgumentException);

        var mismatchedArrayDescription = CreateShaderFixtureDescription(
            vertexLease,
            pixelLease,
            usesSampledResources: true,
            includesFrameUniformBuffer: false,
            sampledImageArrayCount: 2);
        Assert.That(
            () => device.CreatePipeline(mismatchedArrayDescription, [vertexLease, pixelLease]),
            Throws.ArgumentException);
    }

    [Test]
    public void OffscreenNativeDeviceStagingBudgetAdmitsExactDedicatedBoundary()
    {
        const int exactBudgetByteLength = 128 * 1024 * 1024;
        const int overBudgetByteLength = exactBudgetByteLength + 1;
        var payload = new byte[overBudgetByteLength];
        payload[0] = 0x42;
        payload[exactBudgetByteLength - 1] = 0x17;
        payload[exactBudgetByteLength] = 0x9C;

        using var device = CreateVulkanDeviceForNativeSmokeTest();

        if (device.Capabilities.Limits.MaximumBufferByteLength < overBudgetByteLength)
        {
            Assert.Ignore("The selected Vulkan device cannot allocate the destination required by the staging-budget boundary test.");
            return;
        }

        using var destination = device.CreateBuffer(
            new BufferDescription(
                overBudgetByteLength,
                BufferUsage.CopySource | BufferUsage.CopyDestination,
                GpuMemoryKind.DeviceLocal,
                structuredStride: null,
                debugName: "exact staging-budget boundary destination"));
        Assert.That(
            device.UploadBuffer(destination, 0, payload.AsSpan(0, exactBudgetByteLength)),
            Is.GreaterThan(0));

        using (var exactReadback = device.ReadbackBuffer(destination, 0, exactBudgetByteLength))
        using (var exactReadLease = exactReadback.AcquireReadLease(0, exactBudgetByteLength))
        {
            var prefix = new byte[1];
            var prefixCopyAction = new ReadbackCopyAction(prefix);
            exactReadLease.UseWindow(0, 1, ref prefixCopyAction);
            Assert.That(prefix[0], Is.EqualTo(0x42));

            var boundary = new byte[1];
            var boundaryCopyAction = new ReadbackCopyAction(boundary);
            exactReadLease.UseWindow(exactBudgetByteLength - 1, 1, ref boundaryCopyAction);
            Assert.That(boundary[0], Is.EqualTo(0x17));
        }

        Assert.That(
            () => device.ReadbackBuffer(destination, 0, overBudgetByteLength),
            Throws.InvalidOperationException.With.Message.Contains("staging budget is exhausted"));
        using (var recoveryReadback = device.ReadbackBuffer(destination, 0, 1))
        {
            AssertReadback(recoveryReadback, [0x42]);
        }

        Assert.That(
            () => device.UploadBuffer(destination, 0, payload),
            Throws.InvalidOperationException.With.Message.Contains("staging budget is exhausted"));
    }

    [Test]
    public void OffscreenNativeDeviceStagingBudgetRejectsOversizedUploadAndRecovers()
    {
        const int uploadByteLength = 65 * 1024 * 1024;
        var oversizedBytes = new byte[uploadByteLength];
        oversizedBytes[0] = 0x42;
        oversizedBytes[1] = 0x17;
        oversizedBytes[2] = 0x9C;

        using (var device = CreateVulkanDeviceForNativeSmokeTest())
        {
            if (device.Capabilities.Limits.MaximumBufferByteLength < uploadByteLength)
            {
                Assert.Ignore("The selected Vulkan device cannot allocate the 65-MiB destination required by the staging-budget test.");
                return;
            }

            using var destination = device.CreateBuffer(
                new BufferDescription(
                    uploadByteLength,
                    BufferUsage.CopySource | BufferUsage.CopyDestination,
                    GpuMemoryKind.DeviceLocal,
                    structuredStride: null,
                    debugName: "staging-budget destination"));
            var initialBytes = new byte[] { 0x11, 0x22, 0x33 };
            Assert.That(device.UploadBuffer(destination, 0, initialBytes), Is.GreaterThan(0));

            Assert.That(
                () => device.UploadBuffer(destination, 0, oversizedBytes),
                Throws.InvalidOperationException.With.Message.Contains("staging budget is exhausted"));

            var recoveryBytes = new byte[] { 0xA5, 0x5A, 0xC3 };
            Assert.That(device.UploadBuffer(destination, 7, recoveryBytes), Is.GreaterThan(0));
            using var readback = device.ReadbackBuffer(destination, 7, recoveryBytes.Length);
            AssertReadback(readback, recoveryBytes);
        }

        using (var device = CreateVulkanDeviceForNativeSmokeTest())
        using (var destination = device.CreateBuffer(
            new BufferDescription(
                uploadByteLength,
                BufferUsage.CopySource | BufferUsage.CopyDestination,
                GpuMemoryKind.DeviceLocal,
                structuredStride: null,
                debugName: "dedicated-staging destination")))
        {
            Assert.That(device.UploadBuffer(destination, 0, oversizedBytes), Is.GreaterThan(0));
            using var readback = device.ReadbackBuffer(destination, 0, 3);
            AssertReadback(readback, [0x42, 0x17, 0x9C]);
        }
    }

    [Test]
    public void OffscreenNativeDeviceUploadsAndReadsBackDeviceLocalBufferBytes()
    {
        try
        {
            using var device = VulkanBackendDevice.Create(
                GraphicsDeviceProfile.OffscreenNative,
                CancellationToken.None);
            using var buffer = device.CreateBuffer(
                new BufferDescription(
                    1024,
                    BufferUsage.CopySource | BufferUsage.CopyDestination,
                    GpuMemoryKind.DeviceLocal,
                    structuredStride: null,
                    debugName: "transfer"));
            var payload = new byte[257];

            for (var index = 0; index < payload.Length; index++)
            {
                payload[index] = unchecked((byte)(index * 31));
            }

            Assert.That(device.UploadBuffer(buffer, 0, payload), Is.GreaterThan(0));

            var sourceStorage = UnmanagedBuffer<byte>.Allocate(
                payload.Length,
                new BufferAllocationOptions(payload.Length, alignment: 1));
            sourceStorage.CopyFrom(payload.AsSpan(), 0);

            using var ownedSource = sourceStorage.TransferToOwnedBuffer();
            using var sourceLease = ownedSource.AcquireReadLease(0, payload.Length);
            Assert.That(
                device.UploadBuffer(buffer, 513, sourceLease, 0, payload.Length),
                Is.GreaterThan(0));

            using var firstReadback = device.ReadbackBuffer(buffer, 0, payload.Length);
            AssertReadback(firstReadback, payload);
            using var secondReadback = device.ReadbackBuffer(buffer, 513, payload.Length);
            AssertReadback(secondReadback, payload);
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
            Assert.Ignore($"No available Vulkan device satisfies the buffer transfer smoke test: {exception.Message}");
        }
    }

    [Test]
    public void OffscreenNativeDeviceUploadsAndReadsBackPaddedTextureRows()
    {
        try
        {
            using var device = VulkanBackendDevice.Create(
                GraphicsDeviceProfile.OffscreenNative,
                CancellationToken.None);
            using var texture = device.CreateTexture(
                new TextureDescription(
                    TextureDimension.Dimension2D,
                    64,
                    32,
                    depth: 1,
                    arrayLayers: 2,
                    mipLevels: 2,
                    format: TextureFormat.Rgba8Unorm,
                    usage: TextureUsage.CopySource | TextureUsage.CopyDestination,
                    debugName: "texture-transfer"));
            var subresource = new TextureSubresource(1, 1, TextureAspect.Color);
            var width = 32;
            var height = 16;
            var rowByteLength = width * 4;
            var sourceRowPitch = rowByteLength + 8;
            var sourceSlicePitch = sourceRowPitch * height;
            var sourceByteLength = (sourceRowPitch * (height - 1)) + rowByteLength;
            var sourceBytes = new byte[sourceByteLength];
            var expected = new byte[rowByteLength * height];

            for (var index = 0; index < expected.Length; index++)
            {
                expected[index] = unchecked((byte)((index * 17) + 3));
            }

            Array.Fill(sourceBytes, (byte)0xCD);

            for (var row = 0; row < height; row++)
            {
                Array.Copy(expected, row * rowByteLength, sourceBytes, row * sourceRowPitch, rowByteLength);
            }

            Assert.That(
                () => device.ReadbackTexture(texture, subresource),
                Throws.InvalidOperationException.With.Message.Contains("uninitialized"));

            var sourceStorage = UnmanagedBuffer<byte>.Allocate(
                sourceByteLength,
                new BufferAllocationOptions(sourceByteLength, alignment: 1));
            sourceStorage.CopyFrom(sourceBytes.AsSpan(), 0);

            using var ownedSource = sourceStorage.TransferToOwnedBuffer();
            using var sourceLease = ownedSource.AcquireReadLease(0, sourceByteLength);
            Assert.That(
                device.UploadTexture(
                    texture,
                    subresource,
                    sourceLease,
                    0,
                    new TextureDataLayout(sourceRowPitch, sourceSlicePitch)),
                Is.GreaterThan(0));
            Assert.That(texture.GetLayout(subresource), Is.EqualTo(VkImageLayout.VK_IMAGE_LAYOUT_GENERAL));
            Assert.That(texture.IsInitialized(subresource), Is.True);

            using var readback = device.ReadbackTexture(texture, subresource);
            Assert.That(readback.RowPitch, Is.EqualTo(checked((nint)rowByteLength)));
            Assert.That(readback.SlicePitch, Is.EqualTo(checked((nint)(rowByteLength * height))));
            Assert.That(readback.ByteLength, Is.EqualTo(checked((nint)expected.Length)));
            AssertTextureReadback(readback, expected);
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
            Assert.Ignore($"No available Vulkan device satisfies the texture transfer smoke test: {exception.Message}");
        }
    }

    [Test]
    public void RegistrationSelectsVulkanWithoutCreatingDevice()
        => Assert.That(VulkanGraphicsBackend.CreateRegistration().Backend, Is.EqualTo(GraphicsBackend.Vulkan));

    [Test]
    public void SpirvPreflightAcceptsContractualRowMajorMatrixRecordWithoutCreatingDevice()
    {
        using var storage = CreateBytecodeStorage(CreateMatrixUniformSpirv(
            matrixMajor: 4,
            matrixStride: 16,
            rowCount: 4,
            columnCount: 4));
        using var lease = storage.AcquireReadLease(0, storage.Length);
        var reflection = new ShaderReflection(
            ShaderStage.Pixel,
            "main",
            CreateShaderCompilerIdentity(),
            [],
            [],
            [new ShaderResourceSignature(ShaderBindingGroup.Frame, 0, ShaderResourceKind.UniformBuffer, 1)],
            [new ShaderRecordLayout(
                "FrameData",
                64,
                [new ShaderFieldLayout("matrix", ShaderValueType.Float4x4, 0, 64, 1, 0)])],
            [],
            bindingLayoutVersion: 1,
            groupSizeX: 0,
            groupSizeY: 0,
            groupSizeZ: 0);
        var shader = new ShaderBytecodeInput(ShaderStage.Pixel, ShaderBinaryFormat.SpirV, lease, reflection);

        Assert.DoesNotThrow(() => SpirvReflectionValidator.Validate(shader, lease, index: 0));
    }

    [Test]
    public void SpirvPreflightRejectsMissingShaderCapabilityWithoutCreatingDevice()
    {
        var fixtureDirectory = Environment.GetEnvironmentVariable("TERRAFX_M04_SHADER_FIXTURE_DIRECTORY");

        if (string.IsNullOrWhiteSpace(fixtureDirectory))
        {
            Assert.Ignore("The compiled M04 shader fixture is not selected through TERRAFX_M04_SHADER_FIXTURE_DIRECTORY.");
            return;
        }

        var pixelBytecode = File.ReadAllBytes(Path.Combine(
            fixtureDirectory,
            "M04OffscreenTriangle.pixel-sampled.spv"));
        var words = MemoryMarshal.Cast<byte, uint>(pixelBytecode.AsSpan());
        var capabilityReplaced = false;

        for (var cursor = 5; cursor < words.Length;)
        {
            var instruction = words[cursor];
            var wordCount = checked((int)(instruction >> 16));
            var opcode = checked((ushort)(instruction & 0xFFFF));

            if ((wordCount == 0) || (wordCount > words.Length - cursor))
            {
                throw new InvalidDataException("The selected SPIR-V fixture contains an invalid instruction.");
            }

            if ((opcode == 17) && (wordCount == 2) && (words[cursor + 1] == 1))
            {
                words[cursor + 1] = 0;
                capabilityReplaced = true;
                break;
            }

            cursor += wordCount;
        }

        Assert.That(capabilityReplaced, Is.True);

        using var vertexStorage = CreateBytecodeStorage(Path.Combine(
            fixtureDirectory,
            "M04OffscreenTriangle.vertex.spv"));
        using var pixelStorage = CreateBytecodeStorage(pixelBytecode);
        using var vertexLease = vertexStorage.AcquireReadLease(0, vertexStorage.Length);
        using var pixelLease = pixelStorage.AcquireReadLease(0, pixelStorage.Length);
        var description = CreateShaderFixtureDescription(
            vertexLease,
            pixelLease,
            usesSampledResources: true,
            includesFrameUniformBuffer: false);

        Assert.That(
            () => SpirvReflectionValidator.Validate(description.Shaders[1], pixelLease, index: 1),
            Throws.ArgumentException);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void SpirvPreflightRejectsStageIoMetadataMismatchesWithoutCreatingDevice(int mismatch)
    {
        var fixtureDirectory = Environment.GetEnvironmentVariable("TERRAFX_M04_SHADER_FIXTURE_DIRECTORY");

        if (string.IsNullOrWhiteSpace(fixtureDirectory))
        {
            Assert.Ignore("The compiled M04 shader fixture is not selected through TERRAFX_M04_SHADER_FIXTURE_DIRECTORY.");
            return;
        }

        using var vertexStorage = CreateBytecodeStorage(Path.Combine(
            fixtureDirectory,
            "M04OffscreenTriangle.vertex.spv"));
        using var pixelStorage = CreateBytecodeStorage(Path.Combine(
            fixtureDirectory,
            "M04OffscreenTriangle.pixel-sampled-frame.spv"));
        using var vertexLease = vertexStorage.AcquireReadLease(0, vertexStorage.Length);
        using var pixelLease = pixelStorage.AcquireReadLease(0, pixelStorage.Length);
        var description = CreateShaderFixtureDescription(
            vertexLease,
            pixelLease,
            usesSampledResources: true);
        var shaderIndex = mismatch == 0 ? 0 : 1;
        var original = description.Shaders[shaderIndex].Reflection;
        var inputs = original.Inputs;
        var outputs = original.Outputs;

        switch (mismatch)
        {
            case 0:
            {
                inputs = [new ShaderIoSignature("SV_InstanceID", 0, null, ShaderValueType.UInt1, ShaderInterpolation.Flat)];
                break;
            }

            case 1:
            {
                outputs = [new ShaderIoSignature("SV_Target", 1, null, ShaderValueType.Float4, ShaderInterpolation.Smooth)];
                break;
            }

            case 2:
            {
                outputs = [new ShaderIoSignature("SV_Target", 0, null, ShaderValueType.Float3, ShaderInterpolation.Smooth)];
                break;
            }

            case 3:
            {
                outputs = [new ShaderIoSignature("SV_Target", 0, null, ShaderValueType.Float4, ShaderInterpolation.Flat)];
                break;
            }

            case 4:
            {
                outputs = [];
                break;
            }

            default:
            {
                throw new ArgumentOutOfRangeException(nameof(mismatch));
            }
        }

        var reflection = CreateShaderReflectionWithStageIo(original, inputs, outputs);
        var lease = (shaderIndex == 0) ? vertexLease : pixelLease;
        var shader = new ShaderBytecodeInput(original.Stage, ShaderBinaryFormat.SpirV, lease, reflection);

        _ = Assert.Throws<ArgumentException>(
            () => SpirvReflectionValidator.Validate(shader, lease, shaderIndex));
    }

    [Test]
    public void SpirvPreflightRejectsUniformRecordOffsetMismatchWithoutCreatingDevice()
    {
        var fixtureDirectory = Environment.GetEnvironmentVariable("TERRAFX_M04_SHADER_FIXTURE_DIRECTORY");

        if (string.IsNullOrWhiteSpace(fixtureDirectory))
        {
            Assert.Ignore("The compiled M04 shader fixture is not selected through TERRAFX_M04_SHADER_FIXTURE_DIRECTORY.");
            return;
        }

        using var vertexStorage = CreateBytecodeStorage(Path.Combine(
            fixtureDirectory,
            "M04OffscreenTriangle.vertex.spv"));
        using var pixelStorage = CreateBytecodeStorage(Path.Combine(
            fixtureDirectory,
            "M04OffscreenTriangle.pixel-sampled-frame.spv"));
        using var vertexLease = vertexStorage.AcquireReadLease(0, vertexStorage.Length);
        using var pixelLease = pixelStorage.AcquireReadLease(0, pixelStorage.Length);
        var description = CreateShaderFixtureDescription(
            vertexLease,
            pixelLease,
            usesSampledResources: true,
            frameColorByteOffset: 4,
            frameRecordByteLength: 32);

        Assert.That(
            () => SpirvReflectionValidator.Validate(description.Shaders[1], pixelLease, index: 1),
            Throws.ArgumentException);
    }

    [TestCase(5u, 16u, 4u, 4u)]
    [TestCase(4u, 32u, 4u, 4u)]
    [TestCase(4u, 16u, 3u, 4u)]
    [TestCase(4u, 16u, 4u, 3u)]
    public void SpirvPreflightRejectsUnsupportedMatrixRecordWithoutCreatingDevice(
        uint matrixMajor,
        uint matrixStride,
        uint rowCount,
        uint columnCount)
    {
        using var storage = CreateBytecodeStorage(CreateMatrixUniformSpirv(
            matrixMajor,
            matrixStride,
            rowCount,
            columnCount));
        using var lease = storage.AcquireReadLease(0, storage.Length);
        var reflection = new ShaderReflection(
            ShaderStage.Pixel,
            "main",
            CreateShaderCompilerIdentity(),
            [],
            [],
            [new ShaderResourceSignature(ShaderBindingGroup.Frame, 0, ShaderResourceKind.UniformBuffer, 1)],
            [new ShaderRecordLayout(
                "FrameData",
                64,
                [new ShaderFieldLayout("matrix", ShaderValueType.Float4x4, 0, 64, 1, 0)])],
            [],
            bindingLayoutVersion: 1,
            groupSizeX: 0,
            groupSizeY: 0,
            groupSizeZ: 0);
        var shader = new ShaderBytecodeInput(ShaderStage.Pixel, ShaderBinaryFormat.SpirV, lease, reflection);

        _ = Assert.Throws<NotSupportedException>(() => SpirvReflectionValidator.Validate(shader, lease, index: 0));
    }

    [Test]
    [Order(int.MaxValue - 1)]
    public void VulkanStartupCancellationAfterNativeObjectsReleasesAcquiredHandles()
    {
        using var cancellationSource = new CancellationTokenSource();
        _ = Assert.Throws<OperationCanceledException>(
            () => CreateVulkanDeviceForNativeSmokeTest(
                afterNativeStartupObjectsCreated: cancellationSource.Cancel,
                cancellationToken: cancellationSource.Token));

        using var device = CreateVulkanDeviceForNativeSmokeTest();
        Assert.That(device.Capabilities.Backend, Is.EqualTo(GraphicsBackend.Vulkan));
        Assert.That(device.Capabilities.AdapterName, Is.Not.Empty);
    }

    [Test]
    [Order(int.MaxValue)]
    public void VulkanWaitTimeoutAfterNativeSignalQuarantinesBufferAndDevice()
    {
        var observedWaitResult = default(VkResult);
        var device = CreateVulkanDeviceForNativeSmokeTest(waitResult => {
            observedWaitResult = waitResult;
            return waitResult == VkResult.VK_SUCCESS ? VkResult.VK_TIMEOUT : waitResult;
        });
        s_quarantinedDevices.Add(device);

        var buffer = device.CreateBuffer(
            new BufferDescription(
                16,
                BufferUsage.CopyDestination,
                GpuMemoryKind.DeviceLocal,
                structuredStride: null,
                debugName: "uncertain-transfer"));
        byte[] payload = [0x42, 0x17, 0x9C, 0xA5];

        Assert.That(
            () => device.UploadBuffer(buffer, 0, payload),
            Throws.InvalidOperationException.With.Message.Contains("vkWaitSemaphores"));
        Assert.That(observedWaitResult, Is.EqualTo(VkResult.VK_SUCCESS));
        Assert.That(buffer.IsDisposed, Is.False);
        Assert.That(
            buffer.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("submission completion is uncertain"));
        Assert.That(buffer.IsDisposed, Is.False);
        Assert.That(
            device.Dispose,
            Throws.InvalidOperationException.With.Message.Contains("submission completion is uncertain"));
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
