// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using NUnit.Framework;
using TerraFX.Graphics;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class GraphicsCapabilityAdmissionTests
{
    private static GraphicsCapabilities CreateCapabilities(
        GraphicsBackend backend = GraphicsBackend.D3D12,
        GraphicsDeviceProfile profile = GraphicsDeviceProfile.OffscreenNative,
        ImmutableArray<GraphicsFeature>? implementedFeatures = null,
        GraphicsApiVersion? apiVersion = null,
        GraphicsLimits? limits = null,
        ImmutableArray<GraphicsFormatCapabilities>? formats = null)
    {
        var features = implementedFeatures ?? (backend, profile) switch {
            (GraphicsBackend.D3D12, _) => [GraphicsFeature.Direct3DFeatureLevel12, GraphicsFeature.ShaderModel6],
            (GraphicsBackend.Vulkan, GraphicsDeviceProfile.OffscreenNative) => [GraphicsFeature.TimelineSemaphore, GraphicsFeature.Synchronization2, GraphicsFeature.DynamicRendering],
            (GraphicsBackend.Vulkan, GraphicsDeviceProfile.Interactive) => [GraphicsFeature.TimelineSemaphore, GraphicsFeature.Synchronization2, GraphicsFeature.DynamicRendering, GraphicsFeature.SwapchainMaintenance1],
            _ => throw new ArgumentOutOfRangeException(nameof(backend)),
        };

        return new GraphicsCapabilities(
            backend,
            profile,
            new Guid("00112233-4455-6677-8899-aabbccddeeff"),
            vendorId: 0x1002,
            deviceId: 0x744C,
            adapterName: "Admission test adapter",
            driverName: "Admission test driver",
            driverVersion: "1.2.3",
            apiVersion ?? (backend == GraphicsBackend.D3D12
                ? new GraphicsApiVersion(12, 0, 0, 0)
                : new GraphicsApiVersion(1, 3, 0, 0)),
            features,
            features,
            limits ?? CreateLimits(),
            formats ?? CreateFormats(),
            [new GraphicsMemoryHeapCapabilities(
                0,
                GraphicsMemoryHeapProperties.DeviceLocal,
                4L * 1024 * 1024 * 1024,
                3L * 1024 * 1024 * 1024)]);
    }

    private static ImmutableArray<GraphicsFormatCapabilities> CreateFormats()
        => [
            new GraphicsFormatCapabilities(TextureFormat.Rgba8Unorm, TextureUsage.Sampled | TextureUsage.ColorAttachment, [1]),
            new GraphicsFormatCapabilities(TextureFormat.Rgba8Srgb, TextureUsage.Sampled | TextureUsage.ColorAttachment, [1]),
            new GraphicsFormatCapabilities(TextureFormat.Rgba16Float, TextureUsage.Sampled | TextureUsage.ColorAttachment | TextureUsage.Storage, [1]),
            new GraphicsFormatCapabilities(TextureFormat.R8Unorm, TextureUsage.Sampled, [1]),
            new GraphicsFormatCapabilities(TextureFormat.R32Float, TextureUsage.Sampled | TextureUsage.Storage, [1]),
            new GraphicsFormatCapabilities(TextureFormat.R32UInt, TextureUsage.Storage | TextureUsage.ColorAttachment, [1]),
            new GraphicsFormatCapabilities(TextureFormat.D32Float, TextureUsage.DepthAttachment | TextureUsage.Sampled, [1]),
        ];

    private static GraphicsLimits CreateLimits(
        int maximumTextureDimension2D = 16384,
        int maximumTextureDimension3D = 2048,
        int maximumArrayLayers = 2048,
        int maximumColorAttachments = 8,
        int maximumSampledImagesPerStage = 128,
        int maximumSamplersPerStage = 32,
        float maximumSamplerAnisotropy = 1.0f,
        int maximumUniformBuffersPerStage = 16,
        long maximumUniformBufferRangeBytes = 65536)
        => new(
            maximumTextureDimension2D,
            maximumTextureDimension3D,
            maximumArrayLayers,
            maximumColorAttachments,
            maximumSampledImagesPerStage,
            maximumSamplersPerStage,
            maximumSamplerAnisotropy,
            maximumUniformBuffersPerStage,
            maximumUniformBufferRangeBytes,
            uniformBufferOffsetAlignment: 256,
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
            maximumBufferByteLength: 1L << 40);

    [Test]
    public void GetMissingRequirementsRejectsNoneProfile()
    {
        var capabilities = CreateCapabilities();

        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => GraphicsCapabilityAdmission.GetMissingRequirements(capabilities, GraphicsDeviceProfile.None));
    }

    [Test]
    public void GetMissingRequirementsReportsEveryProfileDeficiency()
    {
        var capabilities = CreateCapabilities(
            GraphicsBackend.Vulkan,
            GraphicsDeviceProfile.Interactive,
            implementedFeatures: [
                GraphicsFeature.TimelineSemaphore,
                GraphicsFeature.DynamicRendering,
            ],
            apiVersion: new GraphicsApiVersion(1, 2, 0, 0),
            limits: CreateLimits(
                maximumTextureDimension2D: 4096,
                maximumTextureDimension3D: 128,
                maximumArrayLayers: 64,
                maximumColorAttachments: 2,
                maximumSampledImagesPerStage: 8,
                maximumSamplersPerStage: 4,
                maximumUniformBuffersPerStage: 4,
                maximumUniformBufferRangeBytes: 8192),
            formats: [
                new GraphicsFormatCapabilities(TextureFormat.Rgba8Unorm, TextureUsage.Sampled, [2]),
            ]);

        var missing = GraphicsCapabilityAdmission.GetMissingRequirements(capabilities, GraphicsDeviceProfile.Interactive);

        Assert.That(missing, Does.Contain("Vulkan API 1.3 or later (reported 1.2)"));
        Assert.That(missing, Does.Contain("Implemented feature Synchronization2"));
        Assert.That(missing, Does.Contain("Implemented feature SwapchainMaintenance1"));
        Assert.That(missing, Does.Contain("MaximumTextureDimension2D >= 8192 (reported 4096)"));
        Assert.That(missing, Does.Contain("MaximumTextureDimension3D >= 256 (reported 128)"));
        Assert.That(missing, Does.Contain("MaximumUniformBufferRangeBytes >= 16384 (reported 8192)"));
        Assert.That(missing, Does.Contain("Texture format Rgba8Unorm usages ColorAttachment"));
        Assert.That(missing, Does.Contain("Texture format Rgba8Unorm sample count 1"));
        Assert.That(missing, Does.Contain("Texture format D32Float"));
        Assert.That(missing.Length, Is.GreaterThan(10));
    }

    [Test]
    public void GetMissingRequirementsReportsProfileMismatch()
    {
        var capabilities = CreateCapabilities();
        var missing = GraphicsCapabilityAdmission.GetMissingRequirements(capabilities, GraphicsDeviceProfile.Interactive);

        Assert.That(missing, Is.EqualTo(["Profile Interactive (reported OffscreenNative)"]));
    }

    [Test]
    public void GetMissingRequirementsReturnsEmptyForAdmittedProfiles()
    {
        var d3d12Offscreen = GraphicsCapabilityAdmission.GetMissingRequirements(
            CreateCapabilities(),
            GraphicsDeviceProfile.OffscreenNative);
        var d3d12Interactive = GraphicsCapabilityAdmission.GetMissingRequirements(
            CreateCapabilities(profile: GraphicsDeviceProfile.Interactive),
            GraphicsDeviceProfile.Interactive);
        var vulkanOffscreen = GraphicsCapabilityAdmission.GetMissingRequirements(
            CreateCapabilities(GraphicsBackend.Vulkan),
            GraphicsDeviceProfile.OffscreenNative);
        var vulkanInteractive = GraphicsCapabilityAdmission.GetMissingRequirements(
            CreateCapabilities(GraphicsBackend.Vulkan, GraphicsDeviceProfile.Interactive),
            GraphicsDeviceProfile.Interactive);

        Assert.That(d3d12Offscreen, Is.Empty);
        Assert.That(d3d12Interactive, Is.Empty);
        Assert.That(vulkanOffscreen, Is.Empty);
        Assert.That(vulkanInteractive, Is.Empty);
    }
}
