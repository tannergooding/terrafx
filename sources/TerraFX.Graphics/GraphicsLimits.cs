// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Contains copied backend limits, fixed-profile admission caps, and alignments.</summary>
public sealed class GraphicsLimits
{
    internal GraphicsLimits(
        int maximumTextureDimension2D,
        int maximumTextureDimension3D,
        int maximumArrayLayers,
        int maximumColorAttachments,
        int maximumSampledImagesPerStage,
        int maximumSampledImagesPerPipeline,
        int maximumSamplersPerStage,
        int maximumSamplersPerPipeline,
        float maximumSamplerAnisotropy,
        int maximumUniformBuffersPerStage,
        int maximumUniformBuffersPerPipeline,
        int maximumStorageBuffersPerStage,
        int maximumStorageImagesPerStage,
        int maximumStorageBuffersPerPipeline,
        int maximumStorageImagesPerPipeline,
        int maximumStorageResourcesPerPipeline,
        int maximumResourcesPerStage,
        long maximumUniformBufferRangeBytes,
        long uniformBufferOffsetAlignment,
        long storageBufferOffsetAlignment,
        long bufferCopyOffsetAlignment,
        long textureRowPitchAlignment,
        long texturePlacementAlignment,
        int maximumComputeWorkGroupSizeX,
        int maximumComputeWorkGroupSizeY,
        int maximumComputeWorkGroupSizeZ,
        int maximumComputeWorkGroupCountX,
        int maximumComputeWorkGroupCountY,
        int maximumComputeWorkGroupCountZ,
        int maximumComputeWorkGroupInvocations,
        int maximumVertexInputAttributes,
        int maximumVertexInputBindings,
        long maximumBufferByteLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTextureDimension2D);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTextureDimension3D);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumArrayLayers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumColorAttachments);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSampledImagesPerStage);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSampledImagesPerPipeline);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSamplersPerStage);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSamplersPerPipeline);

        if (!float.IsFinite(maximumSamplerAnisotropy) || (maximumSamplerAnisotropy < 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSamplerAnisotropy));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumUniformBuffersPerStage);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumUniformBuffersPerPipeline);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumStorageBuffersPerStage);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumStorageImagesPerStage);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumStorageBuffersPerPipeline);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumStorageImagesPerPipeline);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumStorageResourcesPerPipeline);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumResourcesPerStage);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumUniformBufferRangeBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(uniformBufferOffsetAlignment);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(storageBufferOffsetAlignment);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferCopyOffsetAlignment);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(textureRowPitchAlignment);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(texturePlacementAlignment);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumComputeWorkGroupSizeX);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumComputeWorkGroupSizeY);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumComputeWorkGroupSizeZ);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumComputeWorkGroupCountX);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumComputeWorkGroupCountY);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumComputeWorkGroupCountZ);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumComputeWorkGroupInvocations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumVertexInputAttributes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumVertexInputBindings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBufferByteLength);

        MaximumTextureDimension2D = maximumTextureDimension2D;
        MaximumTextureDimension3D = maximumTextureDimension3D;
        MaximumArrayLayers = maximumArrayLayers;
        MaximumColorAttachments = maximumColorAttachments;
        MaximumSampledImagesPerStage = maximumSampledImagesPerStage;
        MaximumSampledImagesPerPipeline = maximumSampledImagesPerPipeline;
        MaximumSamplersPerStage = maximumSamplersPerStage;
        MaximumSamplersPerPipeline = maximumSamplersPerPipeline;
        MaximumSamplerAnisotropy = maximumSamplerAnisotropy;
        MaximumUniformBuffersPerStage = maximumUniformBuffersPerStage;
        MaximumUniformBuffersPerPipeline = maximumUniformBuffersPerPipeline;
        MaximumStorageBuffersPerStage = maximumStorageBuffersPerStage;
        MaximumStorageImagesPerStage = maximumStorageImagesPerStage;
        MaximumStorageBuffersPerPipeline = maximumStorageBuffersPerPipeline;
        MaximumStorageImagesPerPipeline = maximumStorageImagesPerPipeline;
        MaximumStorageResourcesPerPipeline = maximumStorageResourcesPerPipeline;
        MaximumResourcesPerStage = maximumResourcesPerStage;
        MaximumUniformBufferRangeBytes = maximumUniformBufferRangeBytes;
        UniformBufferOffsetAlignment = uniformBufferOffsetAlignment;
        StorageBufferOffsetAlignment = storageBufferOffsetAlignment;
        BufferCopyOffsetAlignment = bufferCopyOffsetAlignment;
        TextureRowPitchAlignment = textureRowPitchAlignment;
        TexturePlacementAlignment = texturePlacementAlignment;
        MaximumComputeWorkGroupSizeX = maximumComputeWorkGroupSizeX;
        MaximumComputeWorkGroupSizeY = maximumComputeWorkGroupSizeY;
        MaximumComputeWorkGroupSizeZ = maximumComputeWorkGroupSizeZ;
        MaximumComputeWorkGroupCountX = maximumComputeWorkGroupCountX;
        MaximumComputeWorkGroupCountY = maximumComputeWorkGroupCountY;
        MaximumComputeWorkGroupCountZ = maximumComputeWorkGroupCountZ;
        MaximumComputeWorkGroupInvocations = maximumComputeWorkGroupInvocations;
        MaximumVertexInputAttributes = maximumVertexInputAttributes;
        MaximumVertexInputBindings = maximumVertexInputBindings;
        MaximumBufferByteLength = maximumBufferByteLength;
    }

    /// <summary>Gets the required buffer-copy offset alignment in bytes.</summary>
    public long BufferCopyOffsetAlignment { get; }

    /// <summary>Gets the maximum array-layer count.</summary>
    public int MaximumArrayLayers { get; }

    /// <summary>Gets the maximum buffer byte length.</summary>
    public long MaximumBufferByteLength { get; }

    /// <summary>Gets the maximum color attachment count.</summary>
    public int MaximumColorAttachments { get; }

    /// <summary>Gets the maximum compute dispatch group count in X.</summary>
    public int MaximumComputeWorkGroupCountX { get; }

    /// <summary>Gets the maximum compute dispatch group count in Y.</summary>
    public int MaximumComputeWorkGroupCountY { get; }

    /// <summary>Gets the maximum compute dispatch group count in Z.</summary>
    public int MaximumComputeWorkGroupCountZ { get; }

    /// <summary>Gets the maximum compute work-group invocation count.</summary>
    public int MaximumComputeWorkGroupInvocations { get; }

    /// <summary>Gets the maximum compute work-group size in X.</summary>
    public int MaximumComputeWorkGroupSizeX { get; }

    /// <summary>Gets the maximum compute work-group size in Y.</summary>
    public int MaximumComputeWorkGroupSizeY { get; }

    /// <summary>Gets the maximum compute work-group size in Z.</summary>
    public int MaximumComputeWorkGroupSizeZ { get; }

    /// <summary>Gets the maximum total shader resource descriptor count per shader stage.</summary>
    public int MaximumResourcesPerStage { get; }

    /// <summary>Gets the maximum sampled-image descriptor count across one pipeline layout.</summary>
    public int MaximumSampledImagesPerPipeline { get; }

    /// <summary>Gets the maximum sampled image count per shader stage.</summary>
    public int MaximumSampledImagesPerStage { get; }

    /// <summary>Gets the maximum anisotropy factor implemented by the selected backend; one means isotropic sampling only.</summary>
    public float MaximumSamplerAnisotropy { get; }

    /// <summary>Gets the maximum sampler descriptor count across one pipeline layout.</summary>
    public int MaximumSamplersPerPipeline { get; }

    /// <summary>Gets the maximum sampler count per shader stage.</summary>
    public int MaximumSamplersPerStage { get; }

    /// <summary>Gets the maximum storage-buffer descriptor count across one pipeline layout.</summary>
    public int MaximumStorageBuffersPerPipeline { get; }

    /// <summary>Gets the maximum storage-buffer descriptor count per shader stage.</summary>
    public int MaximumStorageBuffersPerStage { get; }

    /// <summary>Gets the maximum storage-image descriptor count across one pipeline layout.</summary>
    public int MaximumStorageImagesPerPipeline { get; }

    /// <summary>Gets the maximum storage-image descriptor count per shader stage.</summary>
    public int MaximumStorageImagesPerStage { get; }

    /// <summary>Gets the maximum combined storage descriptor count across one pipeline layout.</summary>
    public int MaximumStorageResourcesPerPipeline { get; }

    /// <summary>Gets the maximum 2D texture dimension.</summary>
    public int MaximumTextureDimension2D { get; }

    /// <summary>Gets the maximum 3D texture dimension.</summary>
    public int MaximumTextureDimension3D { get; }

    /// <summary>Gets the maximum uniform-buffer range in bytes.</summary>
    public long MaximumUniformBufferRangeBytes { get; }

    /// <summary>Gets the maximum uniform-buffer descriptor count across one pipeline layout.</summary>
    public int MaximumUniformBuffersPerPipeline { get; }

    /// <summary>Gets the maximum uniform-buffer count per shader stage.</summary>
    public int MaximumUniformBuffersPerStage { get; }

    /// <summary>Gets the maximum vertex-input attribute count.</summary>
    public int MaximumVertexInputAttributes { get; }

    /// <summary>Gets the maximum vertex-input binding count.</summary>
    public int MaximumVertexInputBindings { get; }

    /// <summary>Gets the required storage-buffer offset alignment in bytes.</summary>
    public long StorageBufferOffsetAlignment { get; }

    /// <summary>Gets the required texture-placement alignment in bytes.</summary>
    public long TexturePlacementAlignment { get; }

    /// <summary>Gets the required texture row-pitch alignment in bytes.</summary>
    public long TextureRowPitchAlignment { get; }

    /// <summary>Gets the required uniform-buffer offset alignment in bytes.</summary>
    public long UniformBufferOffsetAlignment { get; }
}
