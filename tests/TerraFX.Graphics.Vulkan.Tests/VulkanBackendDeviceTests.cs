// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using NUnit.Framework;
using TerraFX.Buffers;
using TerraFX.Graphics;
using TerraFX.Graphics.Vulkan;
using TerraFX.Interop.Vulkan;

namespace TerraFX.Graphics.Vulkan.Tests;

[TestFixture]
internal sealed class VulkanBackendDeviceTests
{
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
        bool usesSampledResources = false)
    {
        var compilerIdentity = CreateShaderCompilerIdentity();
        ImmutableArray<ShaderResourceSignature> pixelResources = usesSampledResources
            ? [
                new ShaderResourceSignature(ShaderBindingGroup.Frame, 0, ShaderResourceKind.UniformBuffer, 1),
                new ShaderResourceSignature(ShaderBindingGroup.Frame, 0, ShaderResourceKind.SampledImage, 1),
                new ShaderResourceSignature(ShaderBindingGroup.Frame, 0, ShaderResourceKind.Sampler, 1),
            ]
            : [
                new ShaderResourceSignature(ShaderBindingGroup.Frame, 0, ShaderResourceKind.UniformBuffer, 1),
            ];
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
            usesSampledResources ? "PSSampledMain" : "PSMain",
            compilerIdentity,
            [],
            [new ShaderIoSignature("SV_Target", 0, null, ShaderValueType.Float4, ShaderInterpolation.Smooth)],
            pixelResources,
            [new ShaderRecordLayout(
                "FrameData",
                16,
                [new ShaderFieldLayout(
                    "color",
                    ShaderValueType.Float4,
                    0,
                    16,
                    1,
                    0)])],
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

    private static VulkanBackendDevice CreateVulkanDeviceForNativeSmokeTest()
    {
        try
        {
            return VulkanBackendDevice.Create(
                GraphicsDeviceProfile.OffscreenNative,
                CancellationToken.None);
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
            CreateShaderFixtureDescription(vertexLease, pixelLease),
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
            CreateShaderFixtureDescription(
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
            CreateShaderFixtureDescription(
                vertexLease,
                pixelLease,
                usesSampledResources: true),
            [vertexLease, pixelLease]);

        Assert.That(pipeline.IsDisposed, Is.False);
        Assert.That(pipeline.DescriptorSetLayouts.Length, Is.EqualTo(1));
        Assert.That(pipeline.Resources.Length, Is.EqualTo(3));

        var hasFrameUniformBuffer = false;
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
                case ShaderResourceKind.UniformBuffer:
                {
                    hasFrameUniformBuffer = true;
                    break;
                }

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

        Assert.That(hasFrameUniformBuffer, Is.True);
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
        using (var prefixReadback = device.ReadbackBuffer(destination, 0, 1))
        {
            AssertReadback(prefixReadback, [0x42]);
        }

        using (var boundaryReadback = device.ReadbackBuffer(destination, exactBudgetByteLength - 1, 1))
        {
            AssertReadback(boundaryReadback, [0x17]);
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
