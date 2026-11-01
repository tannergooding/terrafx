// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using TerraFX;
using TerraFX.Graphics;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class GraphicsCapabilitiesTests
{
    private static GraphicsCapabilities CreateCapabilities(
        ImmutableArray<GraphicsFeature>? hardwareFeatures = null,
        ImmutableArray<GraphicsFeature>? implementedFeatures = null,
        ImmutableArray<GraphicsFormatCapabilities>? formats = null,
        ImmutableArray<GraphicsMemoryHeapCapabilities>? memoryHeaps = null,
        float maximumSamplerAnisotropy = 1.0f,
        GraphicsAdapterId? adapterId = null)
    {
        return new GraphicsCapabilities(
            GraphicsBackend.D3D12,
            GraphicsDeviceProfile.OffscreenNative,
            adapterId ?? new GraphicsAdapterId(GraphicsBackend.D3D12, [0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77]),
            vendorId: 0x1002,
            deviceId: 0x744C,
            adapterName: "Test adapter",
            driverName: "Test driver",
            driverVersion: "1.2.3",
            new GraphicsApiVersion(12, 0, 0, 0),
            hardwareFeatures ?? [GraphicsFeature.Direct3DFeatureLevel12, GraphicsFeature.ShaderModel6],
            implementedFeatures ?? [GraphicsFeature.Direct3DFeatureLevel12, GraphicsFeature.ShaderModel6],
            CreateLimits(maximumSamplerAnisotropy: maximumSamplerAnisotropy),
            formats ??
            [
                new GraphicsFormatCapabilities(
                            TextureFormat.Rgba8Unorm,
                            TextureUsage.Sampled | TextureUsage.ColorAttachment,
                            [1                              ]),
                new GraphicsFormatCapabilities(
                    TextureFormat.Rgba16Float,
                    TextureUsage.Sampled | TextureUsage.Storage | TextureUsage.ColorAttachment,
                    [1                      ]),
            ],
            memoryHeaps ?? [new GraphicsMemoryHeapCapabilities(
                    index: 0,
                    GraphicsMemoryHeapProperties.DeviceLocal,
                    sizeBytes: 4L * 1024 * 1024 * 1024,
                    budgetBytes: 3L * 1024 * 1024 * 1024)]);
    }

    private static GraphicsLimits CreateLimits(
        long uniformBufferOffsetAlignment = 256,
        long maximumBufferByteLength = 1L << 40,
        float maximumSamplerAnisotropy = 1.0f)
        => new(
            maximumTextureDimension2D: 16384,
            maximumTextureDimension3D: 2048,
            maximumArrayLayers: 2048,
            maximumColorAttachments: 8,
            maximumSampledImagesPerStage: 128,
            maximumSamplersPerStage: 32,
            maximumSamplerAnisotropy: maximumSamplerAnisotropy,
            maximumUniformBuffersPerStage: 16,
            maximumUniformBufferRangeBytes: 65536,
            uniformBufferOffsetAlignment,
            storageBufferOffsetAlignment: 16,
            bufferCopyOffsetAlignment: 16,
            textureRowPitchAlignment: 256,
            texturePlacementAlignment: 512,
            maximumComputeWorkGroupSizeX: 1024,
            maximumComputeWorkGroupSizeY: 1024,
            maximumComputeWorkGroupSizeZ: 64,
            maximumComputeWorkGroupInvocations: 1024,
            maximumVertexInputAttributes: 32,
            maximumVertexInputBindings: 32,
            maximumBufferByteLength);

    private static SurfaceCapabilities CreateSurfaceCapabilities(
        ImmutableArray<SurfaceFormat>? formats = null,
        ImmutableArray<GraphicsPresentMode>? presentModes = null,
        PixelSize? minimumExtent = null,
        PixelSize? maximumExtent = null,
        int minimumImageCount = 2,
        int? maximumImageCount = null,
        uint surfaceEpoch = 1,
        uint swapchainEpoch = 1)
        => new(
            formats ??
            [
                new SurfaceFormat(TextureFormat.Rgba8Srgb, SurfaceColorSpace.SrgbNonlinear),
                new SurfaceFormat(TextureFormat.Bgra8Unorm, SurfaceColorSpace.SrgbNonlinear),
            ],
            presentModes ?? [GraphicsPresentMode.Fifo, GraphicsPresentMode.Mailbox],
            minimumExtent ?? new PixelSize(640, 480),
            maximumExtent ?? new PixelSize(3840, 2160),
            minimumImageCount,
            maximumImageCount,
            supportsTearing: false,
            surfaceEpoch,
            swapchainEpoch);

    [Test]
    public void GraphicsAdapterIdCopiesAndComparesNativeIdentityBytes()
    {
        var source = new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77 };
        var adapterId = new GraphicsAdapterId(GraphicsBackend.D3D12, source);
        source[0] = 0xFF;
        var equalAdapterId = new GraphicsAdapterId(
            GraphicsBackend.D3D12,
            [0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77]);
        var differentAdapterId = new GraphicsAdapterId(
            GraphicsBackend.D3D12,
            [0x01, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77]);
        var vulkanAdapterId = new GraphicsAdapterId(
            GraphicsBackend.Vulkan,
            [0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF]);

        Assert.That(adapterId.IsValid, Is.True);
        Assert.That(adapterId.NativeIdentifier, Is.EqualTo([0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77]));
        Assert.That(adapterId, Is.EqualTo(equalAdapterId));
        Assert.That(adapterId.GetHashCode(), Is.EqualTo(equalAdapterId.GetHashCode()));
        Assert.That(adapterId != differentAdapterId, Is.True);
        Assert.That(adapterId != vulkanAdapterId, Is.True);
        Assert.That(default(GraphicsAdapterId).IsValid, Is.False);
        _ = Assert.Throws<ArgumentException>(() => new GraphicsAdapterId(GraphicsBackend.D3D12, [0x00]));
        _ = Assert.Throws<ArgumentException>(() => CreateCapabilities(adapterId: default(GraphicsAdapterId)));
        _ = Assert.Throws<ArgumentException>(() => CreateCapabilities(adapterId: new GraphicsAdapterId(
            GraphicsBackend.Vulkan,
            [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F])));
    }

    [Test]
    public void GraphicsCapabilitiesCopiesAndOrdersImmutableValues()
    {
        var hardwareFeatures = new[] {
            GraphicsFeature.SamplerAnisotropy,
            GraphicsFeature.ShaderModel6,
            GraphicsFeature.Direct3DFeatureLevel12,
        };
        var implementedFeatures = new[] {
            GraphicsFeature.ShaderModel6,
            GraphicsFeature.Direct3DFeatureLevel12,
        };
        var formats = new[] {
            new GraphicsFormatCapabilities(TextureFormat.Rgba8Srgb, TextureUsage.Sampled, [1]),
            new GraphicsFormatCapabilities(TextureFormat.R8Unorm, TextureUsage.Sampled, [1]),
        };
        var memoryHeaps = new[] {
            new GraphicsMemoryHeapCapabilities(2, GraphicsMemoryHeapProperties.HostVisible, 1024, 512),
            new GraphicsMemoryHeapCapabilities(0, GraphicsMemoryHeapProperties.DeviceLocal, 4096, 3072),
        };

        var capabilities = CreateCapabilities(
            ImmutableCollectionsMarshal.AsImmutableArray(hardwareFeatures),
            ImmutableCollectionsMarshal.AsImmutableArray(implementedFeatures),
            ImmutableCollectionsMarshal.AsImmutableArray(formats),
            ImmutableCollectionsMarshal.AsImmutableArray(memoryHeaps));

        hardwareFeatures[0] = GraphicsFeature.None;
        implementedFeatures[0] = GraphicsFeature.None;
        formats[0] = new GraphicsFormatCapabilities(TextureFormat.D32Float, TextureUsage.DepthAttachment, [1]);
        memoryHeaps[0] = new GraphicsMemoryHeapCapabilities(9, GraphicsMemoryHeapProperties.DeviceLocal, 1024, 512);

        Assert.That(capabilities.HardwareFeatures, Is.EqualTo([
            GraphicsFeature.Direct3DFeatureLevel12,
            GraphicsFeature.ShaderModel6,
            GraphicsFeature.SamplerAnisotropy,
        ]));
        Assert.That(capabilities.ImplementedFeatures, Is.EqualTo([
            GraphicsFeature.Direct3DFeatureLevel12,
            GraphicsFeature.ShaderModel6,
        ]));
        Assert.That(capabilities.Formats.Select(static format => format.Format), Is.EqualTo([
            TextureFormat.R8Unorm,
            TextureFormat.Rgba8Srgb,
        ]));
        Assert.That(capabilities.MemoryHeaps.Select(static heap => heap.Index), Is.EqualTo([0, 2]));
    }

    [Test]
    public void GraphicsCapabilitiesRejectsDefaultAndInvalidMembership()
    {
        _ = Assert.Throws<ArgumentException>(() => CreateCapabilities(hardwareFeatures: default(ImmutableArray<GraphicsFeature>)));
        _ = Assert.Throws<ArgumentException>(() => CreateCapabilities(
            implementedFeatures: ImmutableArray.Create(GraphicsFeature.SamplerAnisotropy)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => CreateCapabilities(
            hardwareFeatures: ImmutableArray.Create(GraphicsFeature.None)));
    }

    [Test]
    public void GraphicsCapabilitiesRequiresAnisotropyLimitToMatchImplementation()
    {
        var features = ImmutableArray.Create(
            GraphicsFeature.Direct3DFeatureLevel12,
            GraphicsFeature.ShaderModel6,
            GraphicsFeature.SamplerAnisotropy);

        _ = Assert.Throws<ArgumentException>(() => CreateCapabilities(
            hardwareFeatures: features,
            implementedFeatures: features));

        var capabilities = CreateCapabilities(
            hardwareFeatures: features,
            implementedFeatures: features,
            maximumSamplerAnisotropy: 4.0f);

        Assert.That(capabilities.Limits.MaximumSamplerAnisotropy, Is.EqualTo(4.0f));
        _ = Assert.Throws<ArgumentException>(() => CreateCapabilities(maximumSamplerAnisotropy: 4.0f));
    }

    [Test]
    public void GraphicsFormatCapabilitiesSortsCountsAndRejectsUnsupportedCounts()
    {
        var sampleCounts = new[] { 4, 1, 2 };
        var format = new GraphicsFormatCapabilities(
            TextureFormat.Rgba8Unorm,
            TextureUsage.Sampled | TextureUsage.CopySource,
            ImmutableCollectionsMarshal.AsImmutableArray(sampleCounts));
        sampleCounts[0] = 16;

        Assert.That(format.SampleCounts, Is.EqualTo([1, 2, 4]));
        _ = Assert.Throws<ArgumentException>(() => new GraphicsFormatCapabilities(
            TextureFormat.Rgba8Unorm,
            TextureUsage.Sampled,
            [1, 1]));
        _ = Assert.Throws<ArgumentException>(() => new GraphicsFormatCapabilities(
            TextureFormat.Rgba8Unorm,
            TextureUsage.Sampled,
            [1, 3]));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new GraphicsFormatCapabilities(
            TextureFormat.Rgba8Unorm,
            TextureUsage.Sampled,
            [128]));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new GraphicsFormatCapabilities(
            TextureFormat.Rgba8Unorm,
            TextureUsage.None,
            [1]));
    }

    [Test]
    public void GraphicsLimitsValidatesAnisotropyAlignmentAndWideRanges()
    {
        Assert.That(CreateLimits(maximumBufferByteLength: long.MaxValue).MaximumBufferByteLength, Is.EqualTo(long.MaxValue));
        Assert.That(CreateLimits(maximumSamplerAnisotropy: 4.5f).MaximumSamplerAnisotropy, Is.EqualTo(4.5f));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => CreateLimits(uniformBufferOffsetAlignment: 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => CreateLimits(maximumSamplerAnisotropy: 0.5f));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => CreateLimits(maximumSamplerAnisotropy: float.NaN));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => CreateLimits(maximumSamplerAnisotropy: float.PositiveInfinity));
    }

    [Test]
    public void GraphicsMemoryHeapCapabilitiesValidatesPropertiesAndBudget()
    {
        _ = Assert.Throws<ArgumentException>(() => new GraphicsMemoryHeapCapabilities(
            0,
            GraphicsMemoryHeapProperties.HostCached,
            1024,
            512));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new GraphicsMemoryHeapCapabilities(
            0,
            GraphicsMemoryHeapProperties.DeviceLocal,
            1024,
            -1));
    }

    [Test]
    public void SurfaceCapabilitiesCopiesAndCanonicalizesSurfaceValues()
    {
        var formats = new[] {
            new SurfaceFormat(TextureFormat.Bgra8Unorm, SurfaceColorSpace.SrgbNonlinear),
            new SurfaceFormat(TextureFormat.Rgba8Srgb, SurfaceColorSpace.SrgbNonlinear),
        };
        var presentModes = new[] { GraphicsPresentMode.Mailbox, GraphicsPresentMode.Fifo };

        var capabilities = CreateSurfaceCapabilities(
            ImmutableCollectionsMarshal.AsImmutableArray(formats),
            ImmutableCollectionsMarshal.AsImmutableArray(presentModes),
            maximumImageCount: null,
            surfaceEpoch: 5,
            swapchainEpoch: 0);

        formats[0] = new SurfaceFormat(TextureFormat.Rgba8Unorm, SurfaceColorSpace.SrgbNonlinear);
        presentModes[0] = GraphicsPresentMode.Immediate;

        Assert.That(capabilities.Formats.Select(static format => format.Format), Is.EqualTo([
            TextureFormat.Rgba8Srgb,
            TextureFormat.Bgra8Unorm,
        ]));
        Assert.That(capabilities.PresentModes, Is.EqualTo([
            GraphicsPresentMode.Fifo,
            GraphicsPresentMode.Mailbox,
        ]));
        Assert.That(capabilities.MaximumImageCount, Is.Null);
        Assert.That(capabilities.SurfaceEpoch, Is.EqualTo(5));
        Assert.That(capabilities.SwapchainEpoch, Is.Zero);
    }

    [Test]
    public void SurfaceCapabilitiesRejectsInvalidBoundsEpochsAndPresentationModes()
    {
        _ = Assert.Throws<ArgumentException>(() => CreateSurfaceCapabilities(
            presentModes: ImmutableArray.Create(GraphicsPresentMode.Mailbox)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => CreateSurfaceCapabilities(
            maximumImageCount: 1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => CreateSurfaceCapabilities(
            minimumExtent: new PixelSize(0, 480)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => CreateSurfaceCapabilities(
            surfaceEpoch: 0));
        _ = Assert.Throws<ArgumentException>(() => CreateSurfaceCapabilities(
            formats: ImmutableArray.Create(
                new SurfaceFormat(TextureFormat.Rgba8Srgb, SurfaceColorSpace.SrgbNonlinear),
                new SurfaceFormat(TextureFormat.Rgba8Srgb, SurfaceColorSpace.SrgbNonlinear))));
    }

    [Test]
    public void SurfaceFormatAndGraphicsApiVersionHaveValueEquality()
    {
        var surfaceFormat = new SurfaceFormat(TextureFormat.Rgba8Srgb, SurfaceColorSpace.SrgbNonlinear);
        var equalSurfaceFormat = new SurfaceFormat(TextureFormat.Rgba8Srgb, SurfaceColorSpace.SrgbNonlinear);
        var apiVersion = new GraphicsApiVersion(1, 3, 0, 0);

        Assert.That(surfaceFormat == equalSurfaceFormat, Is.True);
        Assert.That(surfaceFormat != new SurfaceFormat(TextureFormat.Bgra8Srgb, SurfaceColorSpace.SrgbNonlinear), Is.True);
        Assert.That(apiVersion, Is.EqualTo(new GraphicsApiVersion(1, 3, 0, 0)));
        Assert.That(apiVersion.GetHashCode(), Is.EqualTo(new GraphicsApiVersion(1, 3, 0, 0).GetHashCode()));
    }
}
