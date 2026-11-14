// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
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
}
