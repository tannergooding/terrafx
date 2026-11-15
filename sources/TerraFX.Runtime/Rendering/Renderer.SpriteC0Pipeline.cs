// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Buffers;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

public sealed partial class Renderer
{
    private static OwnedBuffer<byte> LoadShaderBytecode(string resourcePrefix, ShaderBinaryFormat format)
    {
        var extension = format switch {
            ShaderBinaryFormat.Dxil => "dxil",
            ShaderBinaryFormat.SpirV => "spv",
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
        var resourceName = string.Concat(resourcePrefix, extension);

        using var stream = typeof(Renderer).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The Runtime assembly has no embedded Sprite shader '{resourceName}'.");
        var byteLength = checked((int)stream.Length);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLength);
        var bytes = new byte[byteLength];
        stream.ReadExactly(bytes);
        var storage = UnmanagedBuffer<byte>.Allocate(
            byteLength,
            new BufferAllocationOptions(byteLength, alignment: 1));
        storage.CopyFrom(bytes.AsSpan(), destinationOffset: 0);
        return storage.TransferToOwnedBuffer();
    }

    internal Task<GpuPipeline> CreateMeshC0AlphaMaskPipelineAsync(
        TextureFormat colorTargetFormat,
        CancellationToken cancellationToken = default)
        => CreateMeshC0PipelineAsync(
            colorTargetFormat,
            textured: false,
            alphaMask: true,
            alphaBlend: false,
            normalMapped: false,
            cancellationToken);

    internal Task<GpuPipeline> CreateMeshC0BlendPipelineAsync(
        TextureFormat colorTargetFormat,
        CancellationToken cancellationToken = default)
        => CreateMeshC0PipelineAsync(
            colorTargetFormat,
            textured: false,
            alphaMask: false,
            alphaBlend: true,
            normalMapped: false,
            cancellationToken);

    internal Task<GpuPipeline> CreateMeshC0PipelineAsync(
        TextureFormat colorTargetFormat,
        CancellationToken cancellationToken = default)
        => CreateMeshC0PipelineAsync(
            colorTargetFormat,
            textured: false,
            alphaMask: false,
            alphaBlend: false,
            normalMapped: false,
            cancellationToken);

    internal async Task<GpuPipeline> CreateMeshC0ShadowPipelineAsync(
        bool textured,
        bool skinned,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var device = _graphicsDevice
            ?? throw new NotSupportedException("Headless rendering does not create Mesh shadow pipelines.");
        var binaryFormat = device.Capabilities.Backend switch {
            GraphicsBackend.D3D12 => ShaderBinaryFormat.Dxil,
            GraphicsBackend.Vulkan => ShaderBinaryFormat.SpirV,
            _ => throw new NotSupportedException("The selected GraphicsDevice has no Mesh shadow shader artifact."),
        };
        using var vertexStorage = LoadShaderBytecode(
            (skinned, textured) switch {
                (false, false) => "TerraFX.Rendering.Shaders.MeshC0ShadowVertex.vertex.",
                (false, true) => "TerraFX.Rendering.Shaders.MeshC0TexturedShadowVertex.vertex.",
                (true, false) => "TerraFX.Rendering.Shaders.MeshC0SkinnedShadowVertex.vertex.",
                (true, true) => "TerraFX.Rendering.Shaders.MeshC0SkinnedTexturedShadowVertex.vertex.",
            },
            binaryFormat);
        using var pixelStorage = LoadShaderBytecode(
            textured
                ? "TerraFX.Rendering.Shaders.MeshC0TexturedShadowPixel.pixel."
                : "TerraFX.Rendering.Shaders.MeshC0ShadowPixel.pixel.",
            binaryFormat);
        using var vertexBytecode = vertexStorage.AcquireReadLease(0, vertexStorage.Length);
        using var pixelBytecode = pixelStorage.AcquireReadLease(0, pixelStorage.Length);
        ImmutableArray<VertexElementDescription> vertexElements = (skinned, textured) switch {
            (true, true) => [
                new VertexElementDescription(0, VertexElementFormat.Float3, 0),
                new VertexElementDescription(1, VertexElementFormat.Float2, 6 * sizeof(float)),
                new VertexElementDescription(2, VertexElementFormat.Float4, 12 * sizeof(float)),
                new VertexElementDescription(3, VertexElementFormat.Float4, 16 * sizeof(float)),
                new VertexElementDescription(4, VertexElementFormat.Float4, 20 * sizeof(float)),
            ],
            (true, false) => [
                new VertexElementDescription(0, VertexElementFormat.Float3, 0),
                new VertexElementDescription(1, VertexElementFormat.Float4, 12 * sizeof(float)),
                new VertexElementDescription(2, VertexElementFormat.Float4, 16 * sizeof(float)),
                new VertexElementDescription(3, VertexElementFormat.Float4, 20 * sizeof(float)),
            ],
            (false, true) => [
                new VertexElementDescription(0, VertexElementFormat.Float3, 0),
                new VertexElementDescription(1, VertexElementFormat.Float2, 6 * sizeof(float)),
                new VertexElementDescription(2, VertexElementFormat.Float4, 12 * sizeof(float)),
            ],
            _ => [
                new VertexElementDescription(0, VertexElementFormat.Float3, 0),
                new VertexElementDescription(1, VertexElementFormat.Float4, 12 * sizeof(float)),
            ],
        };
        var vertexReflection = (skinned, textured) switch {
            (false, false) => RenderMeshC0ShadowShaderReflection.Vertex,
            (false, true) => RenderMeshC0ShadowShaderReflection.TexturedVertex,
            (true, false) => RenderMeshC0ShadowShaderReflection.SkinnedVertex,
            (true, true) => RenderMeshC0ShadowShaderReflection.SkinnedTexturedVertex,
        };
        return await device.CreatePipelineAsync(
            new GraphicsPipelineDescription(
                [
                    new ShaderBytecodeInput(
                        ShaderStage.Vertex,
                        binaryFormat,
                        vertexBytecode,
                        vertexReflection),
                    new ShaderBytecodeInput(
                        ShaderStage.Pixel,
                        binaryFormat,
                        pixelBytecode,
                        textured
                            ? RenderMeshC0ShadowShaderReflection.TexturedPixel
                            : RenderMeshC0ShadowShaderReflection.Pixel),
                ],
                vertexElements,
                vertexStride: RenderMeshC0VertexBufferBuilder.VertexStride,
                [],
                depthTargetFormat: TextureFormat.D32Float,
                PrimitiveTopology.TriangleList,
                PipelineBlendMode.Opaque,
                PipelineDepthMode.ReversedGreaterEqualWrite,
                PipelineCullMode.None,
                frontFaceClockwise: false,
                bindingLayoutVersion: 1),
            cancellationToken).ConfigureAwait(false);
    }

    internal Task<GpuPipeline> CreateSkinnedMeshC0AlphaMaskPipelineAsync(
        TextureFormat colorTargetFormat,
        CancellationToken cancellationToken = default)
        => CreateMeshC0PipelineAsync(
            colorTargetFormat,
            textured: false,
            alphaMask: true,
            alphaBlend: false,
            normalMapped: false,
            cancellationToken,
            skinned: true);

    internal Task<GpuPipeline> CreateSkinnedMeshC0BlendPipelineAsync(
        TextureFormat colorTargetFormat,
        CancellationToken cancellationToken = default)
        => CreateMeshC0PipelineAsync(
            colorTargetFormat,
            textured: false,
            alphaMask: false,
            alphaBlend: true,
            normalMapped: false,
            cancellationToken,
            skinned: true);

    internal Task<GpuPipeline> CreateSkinnedMeshC0PipelineAsync(
        TextureFormat colorTargetFormat,
        CancellationToken cancellationToken = default)
        => CreateMeshC0PipelineAsync(
            colorTargetFormat,
            textured: false,
            alphaMask: false,
            alphaBlend: false,
            normalMapped: false,
            cancellationToken,
            skinned: true);

    internal Task<GpuPipeline> CreateSkinnedTexturedMeshC0AlphaMaskPipelineAsync(
        TextureFormat colorTargetFormat,
        CancellationToken cancellationToken = default)
        => CreateMeshC0PipelineAsync(
            colorTargetFormat,
            textured: true,
            alphaMask: true,
            alphaBlend: false,
            normalMapped: false,
            cancellationToken,
            skinned: true);

    internal Task<GpuPipeline> CreateSkinnedTexturedMeshC0BlendPipelineAsync(
        TextureFormat colorTargetFormat,
        CancellationToken cancellationToken = default)
        => CreateMeshC0PipelineAsync(
            colorTargetFormat,
            textured: true,
            alphaMask: false,
            alphaBlend: true,
            normalMapped: false,
            cancellationToken,
            skinned: true);

    internal Task<GpuPipeline> CreateSkinnedTexturedMeshC0NormalMappedPipelineAsync(
        TextureFormat colorTargetFormat,
        CancellationToken cancellationToken = default)
        => CreateMeshC0PipelineAsync(
            colorTargetFormat,
            textured: true,
            alphaMask: false,
            alphaBlend: false,
            normalMapped: true,
            cancellationToken,
            skinned: true);

    internal Task<GpuPipeline> CreateSkinnedTexturedMeshC0PipelineAsync(
        TextureFormat colorTargetFormat,
        CancellationToken cancellationToken = default)
        => CreateMeshC0PipelineAsync(
            colorTargetFormat,
            textured: true,
            alphaMask: false,
            alphaBlend: false,
            normalMapped: false,
            cancellationToken,
            skinned: true);

    internal async Task<GpuPipeline> CreateSpriteC0PipelineAsync(
        TextureFormat colorTargetFormat,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var device = _graphicsDevice
            ?? throw new NotSupportedException("Headless rendering does not create Sprite pipelines.");
        var binaryFormat = device.Capabilities.Backend switch {
            GraphicsBackend.D3D12 => ShaderBinaryFormat.Dxil,
            GraphicsBackend.Vulkan => ShaderBinaryFormat.SpirV,
            _ => throw new NotSupportedException("The selected GraphicsDevice has no Sprite C0 shader artifact."),
        };
        using var vertexStorage = LoadShaderBytecode(
            "TerraFX.Rendering.Shaders.SpriteC0Vertex.vertex.",
            binaryFormat);
        using var pixelStorage = LoadShaderBytecode(
            "TerraFX.Rendering.Shaders.SpriteC0Pixel.pixel.",
            binaryFormat);
        using var vertexBytecode = vertexStorage.AcquireReadLease(0, vertexStorage.Length);
        using var pixelBytecode = pixelStorage.AcquireReadLease(0, pixelStorage.Length);
        return await device.CreatePipelineAsync(
            new GraphicsPipelineDescription(
                [
                    new ShaderBytecodeInput(
                        ShaderStage.Vertex,
                        binaryFormat,
                        vertexBytecode,
                        SpriteC0ShaderReflection.Vertex),
                    new ShaderBytecodeInput(
                        ShaderStage.Pixel,
                        binaryFormat,
                        pixelBytecode,
                        SpriteC0ShaderReflection.Pixel),
                ],
                [
                    new VertexElementDescription(0, VertexElementFormat.Float3, 0),
                    new VertexElementDescription(1, VertexElementFormat.Float2, 3 * sizeof(float)),
                ],
                vertexStride: 5 * sizeof(float),
                [colorTargetFormat],
                depthTargetFormat: null,
                PrimitiveTopology.TriangleList,
                PipelineBlendMode.PremultipliedAlpha,
                PipelineDepthMode.Disabled,
                PipelineCullMode.None,
                frontFaceClockwise: false,
                bindingLayoutVersion: 1),
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task<GpuPipeline> CreateSpriteToneMapPipelineAsync(
        TextureFormat colorTargetFormat,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var device = _graphicsDevice
            ?? throw new NotSupportedException("Headless rendering does not create Sprite tone-map pipelines.");
        var binaryFormat = device.Capabilities.Backend switch {
            GraphicsBackend.D3D12 => ShaderBinaryFormat.Dxil,
            GraphicsBackend.Vulkan => ShaderBinaryFormat.SpirV,
            _ => throw new NotSupportedException("The selected GraphicsDevice has no Sprite tone-map shader artifact."),
        };
        using var vertexStorage = LoadShaderBytecode(
            "TerraFX.Rendering.Shaders.SpriteC0Vertex.vertex.",
            binaryFormat);
        using var pixelStorage = LoadShaderBytecode(
            "TerraFX.Rendering.Shaders.SpriteToneMapPixel.pixel.",
            binaryFormat);
        using var vertexBytecode = vertexStorage.AcquireReadLease(0, vertexStorage.Length);
        using var pixelBytecode = pixelStorage.AcquireReadLease(0, pixelStorage.Length);
        return await device.CreatePipelineAsync(
            new GraphicsPipelineDescription(
                [
                    new ShaderBytecodeInput(
                        ShaderStage.Vertex,
                        binaryFormat,
                        vertexBytecode,
                        SpriteC0ShaderReflection.Vertex),
                    new ShaderBytecodeInput(
                        ShaderStage.Pixel,
                        binaryFormat,
                        pixelBytecode,
                        SpriteToneMapShaderReflection.Pixel),
                ],
                [
                    new VertexElementDescription(0, VertexElementFormat.Float3, 0),
                    new VertexElementDescription(1, VertexElementFormat.Float2, 3 * sizeof(float)),
                ],
                vertexStride: RenderSpriteVertexBufferBuilder.VertexStride,
                [colorTargetFormat],
                depthTargetFormat: null,
                PrimitiveTopology.TriangleList,
                PipelineBlendMode.Opaque,
                PipelineDepthMode.Disabled,
                PipelineCullMode.None,
                frontFaceClockwise: false,
                bindingLayoutVersion: 1),
            cancellationToken).ConfigureAwait(false);
    }

    internal Task<GpuPipeline> CreateTexturedMeshC0AlphaMaskPipelineAsync(
        TextureFormat colorTargetFormat,
        CancellationToken cancellationToken = default)
        => CreateMeshC0PipelineAsync(
            colorTargetFormat,
            textured: true,
            alphaMask: true,
            alphaBlend: false,
            normalMapped: false,
            cancellationToken);

    internal Task<GpuPipeline> CreateTexturedMeshC0BlendPipelineAsync(
        TextureFormat colorTargetFormat,
        CancellationToken cancellationToken = default)
        => CreateMeshC0PipelineAsync(
            colorTargetFormat,
            textured: true,
            alphaMask: false,
            alphaBlend: true,
            normalMapped: false,
            cancellationToken);

    internal Task<GpuPipeline> CreateTexturedMeshC0NormalMappedPipelineAsync(
        TextureFormat colorTargetFormat,
        CancellationToken cancellationToken = default)
        => CreateMeshC0PipelineAsync(
            colorTargetFormat,
            textured: true,
            alphaMask: false,
            alphaBlend: false,
            normalMapped: true,
            cancellationToken);

    internal Task<GpuPipeline> CreateTexturedMeshC0PipelineAsync(
        TextureFormat colorTargetFormat,
        CancellationToken cancellationToken = default)
        => CreateMeshC0PipelineAsync(
            colorTargetFormat,
            textured: true,
            alphaMask: false,
            alphaBlend: false,
            normalMapped: false,
            cancellationToken);

    private async Task<GpuPipeline> CreateMeshC0PipelineAsync(
        TextureFormat colorTargetFormat,
        bool textured,
        bool alphaMask,
        bool alphaBlend,
        bool normalMapped,
        CancellationToken cancellationToken,
        bool skinned = false)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (normalMapped && (!textured || alphaMask || alphaBlend))
        {
            throw new InvalidOperationException("The built-in normal-map pipeline requires an opaque textured material.");
        }

        var device = _graphicsDevice
            ?? throw new NotSupportedException("Headless rendering does not create Mesh pipelines.");
        var binaryFormat = device.Capabilities.Backend switch {
            GraphicsBackend.D3D12 => ShaderBinaryFormat.Dxil,
            GraphicsBackend.Vulkan => ShaderBinaryFormat.SpirV,
            _ => throw new NotSupportedException("The selected GraphicsDevice has no Mesh C0 shader artifact."),
        };
        using var vertexStorage = LoadShaderBytecode(
            (skinned, textured) switch {
                (false, false) => "TerraFX.Rendering.Shaders.MeshC0Vertex.vertex.",
                (false, true) => "TerraFX.Rendering.Shaders.MeshC0TexturedVertex.vertex.",
                (true, false) => "TerraFX.Rendering.Shaders.MeshC0SkinnedVertex.vertex.",
                (true, true) => "TerraFX.Rendering.Shaders.MeshC0SkinnedTexturedVertex.vertex.",
            },
            binaryFormat);
        using var pixelStorage = LoadShaderBytecode(
            (textured, alphaMask) switch {
                (false, false) => "TerraFX.Rendering.Shaders.MeshC0Pixel.pixel.",
                (false, true) => "TerraFX.Rendering.Shaders.MeshC0MaskPixel.pixel.",
                (true, false) => "TerraFX.Rendering.Shaders.MeshC0TexturedPixel.pixel.",
                (true, true) => "TerraFX.Rendering.Shaders.MeshC0TexturedMaskPixel.pixel.",
            },
            binaryFormat);
        using var vertexBytecode = vertexStorage.AcquireReadLease(0, vertexStorage.Length);
        using var pixelBytecode = pixelStorage.AcquireReadLease(0, pixelStorage.Length);
        ImmutableArray<VertexElementDescription> vertexElements = (skinned, textured) switch {
            (true, true) => [
                new VertexElementDescription(0, VertexElementFormat.Float3, 0),
                new VertexElementDescription(1, VertexElementFormat.Float3, 3 * sizeof(float)),
                new VertexElementDescription(2, VertexElementFormat.Float2, 6 * sizeof(float)),
                new VertexElementDescription(3, VertexElementFormat.Float4, 8 * sizeof(float)),
                new VertexElementDescription(4, VertexElementFormat.Float4, 12 * sizeof(float)),
                new VertexElementDescription(5, VertexElementFormat.Float4, 16 * sizeof(float)),
                new VertexElementDescription(6, VertexElementFormat.Float4, 20 * sizeof(float)),
            ],
            (true, false) => [
                new VertexElementDescription(0, VertexElementFormat.Float3, 0),
                new VertexElementDescription(1, VertexElementFormat.Float3, 3 * sizeof(float)),
                new VertexElementDescription(2, VertexElementFormat.Float4, 12 * sizeof(float)),
                new VertexElementDescription(3, VertexElementFormat.Float4, 16 * sizeof(float)),
                new VertexElementDescription(4, VertexElementFormat.Float4, 20 * sizeof(float)),
            ],
            (false, true) => [
                new VertexElementDescription(0, VertexElementFormat.Float3, 0),
                new VertexElementDescription(1, VertexElementFormat.Float3, 3 * sizeof(float)),
                new VertexElementDescription(2, VertexElementFormat.Float2, 6 * sizeof(float)),
                new VertexElementDescription(3, VertexElementFormat.Float4, 8 * sizeof(float)),
                new VertexElementDescription(4, VertexElementFormat.Float4, 12 * sizeof(float)),
            ],
            _ => [
                new VertexElementDescription(0, VertexElementFormat.Float3, 0),
                new VertexElementDescription(1, VertexElementFormat.Float3, 3 * sizeof(float)),
                new VertexElementDescription(2, VertexElementFormat.Float4, 12 * sizeof(float)),
            ],
        };
        var vertexReflection = (skinned, textured) switch {
            (false, false) => RenderMeshC0ShaderReflection.Vertex,
            (false, true) => RenderMeshC0ShaderReflection.TexturedVertex,
            (true, false) => RenderMeshC0ShaderReflection.SkinnedVertex,
            (true, true) => RenderMeshC0ShaderReflection.SkinnedTexturedVertex,
        };
        return await device.CreatePipelineAsync(
            new GraphicsPipelineDescription(
                [
                    new ShaderBytecodeInput(
                        ShaderStage.Vertex,
                        binaryFormat,
                        vertexBytecode,
                        vertexReflection),
                    new ShaderBytecodeInput(
                        ShaderStage.Pixel,
                        binaryFormat,
                        pixelBytecode,
                        textured ? RenderMeshC0ShaderReflection.TexturedPixel : RenderMeshC0ShaderReflection.Pixel),
                ],
                vertexElements,
                vertexStride: RenderMeshC0VertexBufferBuilder.VertexStride,
                [colorTargetFormat],
                depthTargetFormat: TextureFormat.D32Float,
                PrimitiveTopology.TriangleList,
                alphaBlend ? PipelineBlendMode.PremultipliedAlpha : PipelineBlendMode.Opaque,
                alphaBlend
                    ? PipelineDepthMode.ReversedGreaterEqualReadOnly
                    : PipelineDepthMode.ReversedGreaterEqualWrite,
                normalMapped ? PipelineCullMode.Back : PipelineCullMode.None,
                frontFaceClockwise: normalMapped,
                bindingLayoutVersion: 1),
            cancellationToken).ConfigureAwait(false);
    }
}
