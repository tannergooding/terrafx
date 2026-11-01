// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;

namespace TerraFX.Graphics;

internal static class GraphicsCapabilityAdmission
{
    internal static ImmutableArray<string> GetMissingRequirements(
        GraphicsCapabilities capabilities,
        GraphicsDeviceProfile requestedProfile)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        if ((requestedProfile == GraphicsDeviceProfile.None) || !Enum.IsDefined(requestedProfile))
        {
            throw new ArgumentOutOfRangeException(nameof(requestedProfile));
        }

        var missing = ImmutableArray.CreateBuilder<string>();

        if (capabilities.Profile != requestedProfile)
        {
            missing.Add($"Profile {requestedProfile} (reported {capabilities.Profile})");
        }

        switch (capabilities.Backend)
        {
            case GraphicsBackend.D3D12:
            {
                AddMissingFeature(missing, capabilities, GraphicsFeature.Direct3DFeatureLevel12);
                AddMissingFeature(missing, capabilities, GraphicsFeature.ShaderModel6);
                break;
            }

            case GraphicsBackend.Vulkan:
            {
                if ((capabilities.ApiVersion.Major < 1)
                    || ((capabilities.ApiVersion.Major == 1) && (capabilities.ApiVersion.Minor < 3)))
                {
                    missing.Add($"Vulkan API 1.3 or later (reported {capabilities.ApiVersion.Major}.{capabilities.ApiVersion.Minor})");
                }

                AddMissingFeature(missing, capabilities, GraphicsFeature.TimelineSemaphore);
                AddMissingFeature(missing, capabilities, GraphicsFeature.Synchronization2);
                AddMissingFeature(missing, capabilities, GraphicsFeature.DynamicRendering);

                if (requestedProfile == GraphicsDeviceProfile.Interactive)
                {
                    AddMissingFeature(missing, capabilities, GraphicsFeature.SwapchainMaintenance1);
                }

                break;
            }

            default:
            {
                throw new ArgumentOutOfRangeException(nameof(capabilities));
            }
        }

        var limits = capabilities.Limits;
        AddMissingLimit(missing, nameof(GraphicsLimits.MaximumTextureDimension2D), limits.MaximumTextureDimension2D, 8192);
        AddMissingLimit(missing, nameof(GraphicsLimits.MaximumTextureDimension3D), limits.MaximumTextureDimension3D, 256);
        AddMissingLimit(missing, nameof(GraphicsLimits.MaximumArrayLayers), limits.MaximumArrayLayers, 256);
        AddMissingLimit(missing, nameof(GraphicsLimits.MaximumColorAttachments), limits.MaximumColorAttachments, 4);
        AddMissingLimit(missing, nameof(GraphicsLimits.MaximumSampledImagesPerStage), limits.MaximumSampledImagesPerStage, 16);
        AddMissingLimit(missing, nameof(GraphicsLimits.MaximumSamplersPerStage), limits.MaximumSamplersPerStage, 8);
        AddMissingLimit(missing, nameof(GraphicsLimits.MaximumUniformBuffersPerStage), limits.MaximumUniformBuffersPerStage, 8);
        AddMissingLimit(missing, nameof(GraphicsLimits.MaximumUniformBufferRangeBytes), limits.MaximumUniformBufferRangeBytes, 16384);

        AddMissingFormat(missing, capabilities, TextureFormat.Rgba8Unorm, TextureUsage.Sampled | TextureUsage.ColorAttachment);
        AddMissingFormat(missing, capabilities, TextureFormat.Rgba8Srgb, TextureUsage.Sampled | TextureUsage.ColorAttachment);
        AddMissingFormat(missing, capabilities, TextureFormat.Rgba16Float, TextureUsage.Sampled | TextureUsage.ColorAttachment | TextureUsage.Storage);
        AddMissingFormat(missing, capabilities, TextureFormat.R8Unorm, TextureUsage.Sampled);
        AddMissingFormat(missing, capabilities, TextureFormat.R32Float, TextureUsage.Sampled | TextureUsage.Storage);
        AddMissingFormat(missing, capabilities, TextureFormat.R32UInt, TextureUsage.Storage | TextureUsage.ColorAttachment);
        AddMissingFormat(missing, capabilities, TextureFormat.D32Float, TextureUsage.DepthAttachment | TextureUsage.Sampled);

        return missing.ToImmutable();
    }

    private static void AddMissingFeature(
        ImmutableArray<string>.Builder missing,
        GraphicsCapabilities capabilities,
        GraphicsFeature feature)
    {
        if (!capabilities.ImplementedFeatures.Contains(feature))
        {
            missing.Add($"Implemented feature {feature}");
        }
    }

    private static void AddMissingFormat(
        ImmutableArray<string>.Builder missing,
        GraphicsCapabilities capabilities,
        TextureFormat format,
        TextureUsage requiredUsages)
    {
        GraphicsFormatCapabilities? formatCapabilities = null;

        for (var index = 0; index < capabilities.Formats.Length; index++)
        {
            if (capabilities.Formats[index].Format == format)
            {
                formatCapabilities = capabilities.Formats[index];
                break;
            }
        }

        if (formatCapabilities is null)
        {
            missing.Add($"Texture format {format}");
            return;
        }

        var missingUsages = requiredUsages & ~formatCapabilities.SupportedUsages;

        if (missingUsages != TextureUsage.None)
        {
            missing.Add($"Texture format {format} usages {missingUsages}");
        }

        if (!formatCapabilities.SampleCounts.Contains(1))
        {
            missing.Add($"Texture format {format} sample count 1");
        }
    }

    private static void AddMissingLimit(
        ImmutableArray<string>.Builder missing,
        string name,
        long actual,
        long minimum)
    {
        if (actual < minimum)
        {
            missing.Add($"{name} >= {minimum} (reported {actual})");
        }
    }
}
