// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using NUnit.Framework;
using TerraFX;
using TerraFX.Buffers;
using TerraFX.Graphics;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class GraphicsPipelineDescriptionTests
{
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

    private static GraphicsPipelineDescription CreatePipelineDescription(
        BufferReadLease<byte> bytecode,
        ShaderReflection? vertexReflection = null,
        ShaderReflection? pixelReflection = null,
        ImmutableArray<ShaderBytecodeInput>? shaderInputs = null,
        ImmutableArray<VertexElementDescription>? vertexElements = null,
        int vertexStride = 0,
        ImmutableArray<TextureFormat>? colorTargetFormats = null,
        TextureFormat? depthTargetFormat = null,
        PrimitiveTopology topology = PrimitiveTopology.TriangleList,
        PipelineBlendMode blendMode = PipelineBlendMode.Opaque,
        PipelineDepthMode depthMode = PipelineDepthMode.Disabled,
        PipelineCullMode cullMode = PipelineCullMode.None,
        int bindingLayoutVersion = 1)
    {
        vertexReflection ??= CreateSystemVertexReflection();
        pixelReflection ??= CreateReflection(
            ShaderStage.Pixel,
            inputs: ImmutableArray.Create(new ShaderIoSignature(
                "SV_Position",
                0,
                null,
                ShaderValueType.Float4,
                ShaderInterpolation.Smooth)),
            outputs: ImmutableArray.Create(new ShaderIoSignature(
                "SV_Target",
                0,
                null,
                ShaderValueType.Float4,
                ShaderInterpolation.Smooth)));

        var vertexShader = new ShaderBytecodeInput(
            ShaderStage.Vertex,
            ShaderBinaryFormat.Dxil,
            bytecode,
            vertexReflection);
        var pixelShader = new ShaderBytecodeInput(
            ShaderStage.Pixel,
            ShaderBinaryFormat.Dxil,
            bytecode,
            pixelReflection);

        return new GraphicsPipelineDescription(
            shaderInputs ?? [pixelShader, vertexShader],
            vertexElements ?? [],
            vertexStride,
            colorTargetFormats ?? [TextureFormat.Rgba8Unorm],
            depthTargetFormat,
            topology,
            blendMode,
            depthMode,
            cullMode,
            frontFaceClockwise: false,
            bindingLayoutVersion);
    }

    private static ShaderReflection CreateReflection(
        ShaderStage stage,
        ImmutableArray<ShaderIoSignature>? inputs = null,
        ImmutableArray<ShaderIoSignature>? outputs = null,
        int bindingLayoutVersion = 1,
        int groupSizeX = 0,
        int groupSizeY = 0,
        int groupSizeZ = 0)
        => new(
            stage,
            "main",
            CreateCompilerIdentity(),
            inputs ?? [],
            outputs ?? [],
            [],
            [],
            [],
            bindingLayoutVersion,
            groupSizeX,
            groupSizeY,
            groupSizeZ);

    private static ShaderReflection CreateSystemVertexReflection()
        => CreateReflection(
            ShaderStage.Vertex,
            inputs: ImmutableArray.Create(new ShaderIoSignature(
                "SV_VertexID",
                0,
                null,
                ShaderValueType.UInt1,
                ShaderInterpolation.Flat)),
            outputs: ImmutableArray.Create(new ShaderIoSignature(
                "SV_Position",
                0,
                null,
                ShaderValueType.Float4,
                ShaderInterpolation.Smooth)));

    [Test]
    public void GraphicsPipelineAllowsSystemValueOnlyVertexInputWithoutVertexBuffer()
    {
        using var owner = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(64, 1));
        using var bytecode = owner.AcquireRead(0, 4);
        var vertexReflection = CreateReflection(
            ShaderStage.Vertex,
            inputs: ImmutableArray.Create(new ShaderIoSignature(
                "SV_VertexID",
                0,
                null,
                ShaderValueType.UInt1,
                ShaderInterpolation.Flat)),
            outputs: ImmutableArray.Create(new ShaderIoSignature(
                "SV_Position",
                0,
                null,
                ShaderValueType.Float4,
                ShaderInterpolation.Smooth)));

        var description = CreatePipelineDescription(bytecode, vertexReflection: vertexReflection);

        Assert.That(description.VertexElements, Is.Empty);
        Assert.That(description.VertexStride, Is.Zero);

        var depthDescription = CreatePipelineDescription(
            bytecode,
            depthTargetFormat: TextureFormat.D32Float,
            depthMode: PipelineDepthMode.ReversedGreaterEqualReadOnly);

        Assert.That(depthDescription.DepthMode, Is.EqualTo(PipelineDepthMode.ReversedGreaterEqualReadOnly));
        Assert.That(depthDescription.DepthTargetFormat, Is.EqualTo(TextureFormat.D32Float));
    }

    [Test]
    public void GraphicsPipelineAndComputePipelineRejectDefaultArraysAndInvalidStride()
    {
        using var owner = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(64, 1));
        using var bytecode = owner.AcquireRead(0, 4);

        _ = Assert.Throws<ArgumentException>(() => CreatePipelineDescription(
            bytecode,
            vertexElements: default(ImmutableArray<VertexElementDescription>)));
        _ = Assert.Throws<ArgumentException>(() => CreatePipelineDescription(
            bytecode,
            colorTargetFormats: default(ImmutableArray<TextureFormat>)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => CreatePipelineDescription(
            bytecode,
            vertexStride: -1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => CreatePipelineDescription(
            bytecode,
            vertexStride: 1));
        _ = Assert.Throws<ArgumentException>(() => CreatePipelineDescription(
            bytecode,
            vertexReflection: CreateReflection(ShaderStage.Vertex)));

        var computeReflection = CreateReflection(ShaderStage.Compute, groupSizeX: 8, groupSizeY: 8, groupSizeZ: 1);
        var computeShader = new ShaderBytecodeInput(ShaderStage.Compute, ShaderBinaryFormat.Dxil, bytecode, computeReflection);
        var computeDescription = new ComputePipelineDescription(computeShader, bindingLayoutVersion: 1);

        Assert.That(computeDescription.Shader, Is.SameAs(computeShader));
        Assert.That(computeDescription.BindingLayoutVersion, Is.EqualTo(1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new ComputePipelineDescription(computeShader, bindingLayoutVersion: 0));
    }

    [Test]
    public void GraphicsPipelineCopiesAndCanonicalizesItsInputs()
    {
        using var owner = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(64, 1));
        using var bytecode = owner.AcquireRead(0, 4);

        var vertexReflection = CreateReflection(
            ShaderStage.Vertex,
            inputs: ImmutableArray.Create(new ShaderIoSignature(
                "POSITION",
                0,
                0,
                ShaderValueType.Float3,
                ShaderInterpolation.Smooth)),
            outputs: ImmutableArray.Create(new ShaderIoSignature(
                "SV_Position",
                0,
                null,
                ShaderValueType.Float4,
                ShaderInterpolation.Smooth)));
        var pixelReflection = CreateReflection(
            ShaderStage.Pixel,
            outputs: ImmutableArray.Create(new ShaderIoSignature(
                "SV_Target",
                0,
                null,
                ShaderValueType.Float4,
                ShaderInterpolation.Smooth)));
        var vertexShader = new ShaderBytecodeInput(ShaderStage.Vertex, ShaderBinaryFormat.Dxil, bytecode, vertexReflection);
        var pixelShader = new ShaderBytecodeInput(ShaderStage.Pixel, ShaderBinaryFormat.Dxil, bytecode, pixelReflection);
        var shaders = new[] { pixelShader, vertexShader };
        var vertexElements = new[] { new VertexElementDescription(0, VertexElementFormat.Float3, 0) };
        var colorTargetFormats = new[] { TextureFormat.Rgba8Unorm };

        var description = new GraphicsPipelineDescription(
            ImmutableCollectionsMarshal.AsImmutableArray(shaders),
            ImmutableCollectionsMarshal.AsImmutableArray(vertexElements),
            vertexStride: 12,
            ImmutableCollectionsMarshal.AsImmutableArray(colorTargetFormats),
            depthTargetFormat: null,
            PrimitiveTopology.TriangleList,
            PipelineBlendMode.PremultipliedAlpha,
            PipelineDepthMode.Disabled,
            PipelineCullMode.Back,
            frontFaceClockwise: true,
            bindingLayoutVersion: 1);

        shaders[0] = vertexShader;
        vertexElements[0] = new VertexElementDescription(5, VertexElementFormat.Float4, 4);
        colorTargetFormats[0] = TextureFormat.Bgra8Unorm;

        Assert.That(description.Shaders[0].Stage, Is.EqualTo(ShaderStage.Vertex));
        Assert.That(description.Shaders[1].Stage, Is.EqualTo(ShaderStage.Pixel));
        Assert.That(description.VertexElements, Has.Length.EqualTo(1));
        Assert.That(description.VertexElements[0], Is.EqualTo(new VertexElementDescription(0, VertexElementFormat.Float3, 0)));
        Assert.That(description.ColorTargetFormats, Has.Length.EqualTo(1));
        Assert.That(description.ColorTargetFormats[0], Is.EqualTo(TextureFormat.Rgba8Unorm));
        Assert.That(description.VertexStride, Is.EqualTo(12));
        Assert.That(description.BlendMode, Is.EqualTo(PipelineBlendMode.PremultipliedAlpha));
        Assert.That(description.DepthMode, Is.EqualTo(PipelineDepthMode.Disabled));
        Assert.That(description.CullMode, Is.EqualTo(PipelineCullMode.Back));
        Assert.That(description.FrontFaceClockwise, Is.True);
    }

    [Test]
    public void GraphicsPipelineRejectsInvalidStagesAndBindingVersions()
    {
        using var owner = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(64, 1));
        using var bytecode = owner.AcquireRead(0, 4);
        var vertexReflection = CreateSystemVertexReflection();
        var pixelReflection = CreateReflection(
            ShaderStage.Pixel,
            outputs: ImmutableArray.Create(new ShaderIoSignature(
                "SV_Target",
                0,
                null,
                ShaderValueType.Float4,
                ShaderInterpolation.Smooth)));
        var vertexShader = new ShaderBytecodeInput(ShaderStage.Vertex, ShaderBinaryFormat.Dxil, bytecode, vertexReflection);
        var pixelShader = new ShaderBytecodeInput(ShaderStage.Pixel, ShaderBinaryFormat.Dxil, bytecode, pixelReflection);
        var computeReflection = CreateReflection(ShaderStage.Compute, groupSizeX: 1, groupSizeY: 1, groupSizeZ: 1);
        var computeShader = new ShaderBytecodeInput(ShaderStage.Compute, ShaderBinaryFormat.Dxil, bytecode, computeReflection);

        _ = Assert.Throws<ArgumentException>(() => CreatePipelineDescription(
            bytecode,
            shaderInputs: ImmutableArray.Create(vertexShader)));
        _ = Assert.Throws<ArgumentException>(() => CreatePipelineDescription(
            bytecode,
            shaderInputs: ImmutableArray.Create(vertexShader, vertexShader)));
        _ = Assert.Throws<ArgumentException>(() => CreatePipelineDescription(
            bytecode,
            shaderInputs: ImmutableArray.Create(vertexShader, computeShader)));

        var mismatchedVersionPixelReflection = CreateReflection(
            ShaderStage.Pixel,
            outputs: pixelReflection.Outputs,
            bindingLayoutVersion: 2);
        var mismatchedVersionPixel = new ShaderBytecodeInput(
            ShaderStage.Pixel,
            ShaderBinaryFormat.Dxil,
            bytecode,
            mismatchedVersionPixelReflection);

        _ = Assert.Throws<ArgumentException>(() => CreatePipelineDescription(
            bytecode,
            shaderInputs: ImmutableArray.Create(vertexShader, mismatchedVersionPixel)));
        _ = Assert.Throws<ArgumentException>(() => new ComputePipelineDescription(vertexShader, bindingLayoutVersion: 1));
        _ = Assert.Throws<ArgumentException>(() => new ComputePipelineDescription(computeShader, bindingLayoutVersion: 2));
    }

    [Test]
    public void GraphicsPipelineRejectsRenderTargetDepthAndEnumMismatches()
    {
        using var owner = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(64, 1));
        using var bytecode = owner.AcquireRead(0, 4);
        var vertexReflection = CreateSystemVertexReflection();
        var pixelReflection = CreateReflection(
            ShaderStage.Pixel,
            outputs: ImmutableArray.Create(new ShaderIoSignature(
                "SV_Target",
                1,
                null,
                ShaderValueType.Float4,
                ShaderInterpolation.Smooth)));

        _ = Assert.Throws<ArgumentException>(() => CreatePipelineDescription(
            bytecode,
            vertexReflection,
            pixelReflection));
        _ = Assert.Throws<ArgumentException>(() => CreatePipelineDescription(
            bytecode,
            vertexReflection,
            CreateReflection(
                ShaderStage.Pixel,
                outputs: ImmutableArray.Create(new ShaderIoSignature(
                    "SV_Target",
                    0,
                    null,
                    ShaderValueType.UInt4,
                    ShaderInterpolation.Smooth)))));
        _ = Assert.Throws<ArgumentException>(() => CreatePipelineDescription(
            bytecode,
            vertexReflection,
            CreateReflection(
                ShaderStage.Pixel,
                outputs: ImmutableArray.Create(
                    new ShaderIoSignature("SV_Target", 0, 0, ShaderValueType.Float4, ShaderInterpolation.Smooth),
                    new ShaderIoSignature("SV_Target", 0, 1, ShaderValueType.Float4, ShaderInterpolation.Smooth)))));
        _ = Assert.Throws<ArgumentException>(() => CreatePipelineDescription(
            bytecode,
            vertexReflection,
            CreateReflection(
                ShaderStage.Pixel,
                outputs: ImmutableArray.Create(new ShaderIoSignature(
                    "SV_Depth",
                    0,
                    null,
                    ShaderValueType.Float4,
                    ShaderInterpolation.Smooth))),
            depthTargetFormat: TextureFormat.D32Float,
            depthMode: PipelineDepthMode.ReversedGreaterEqualWrite));
        _ = Assert.Throws<ArgumentException>(() => CreatePipelineDescription(
            bytecode,
            vertexReflection,
            depthTargetFormat: TextureFormat.D32Float));
        _ = Assert.Throws<ArgumentException>(() => CreatePipelineDescription(
            bytecode,
            vertexReflection,
            depthMode: PipelineDepthMode.ReversedGreaterEqualWrite));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => CreatePipelineDescription(
            bytecode,
            vertexReflection,
            colorTargetFormats: ImmutableArray.Create(TextureFormat.D32Float)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => CreatePipelineDescription(
            bytecode,
            vertexReflection,
            topology: (PrimitiveTopology)99));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new VertexElementDescription(
            0,
            (VertexElementFormat)99,
            0));
    }

    [Test]
    public void GraphicsPipelineRejectsVertexLayoutAndStageLinkageMismatches()
    {
        using var owner = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(64, 1));
        using var bytecode = owner.AcquireRead(0, 4);
        var vertexReflection = CreateReflection(
            ShaderStage.Vertex,
            inputs: ImmutableArray.Create(new ShaderIoSignature(
                "POSITION",
                0,
                0,
                ShaderValueType.Float3,
                ShaderInterpolation.Smooth)),
            outputs: ImmutableArray.Create(new ShaderIoSignature(
                "TEXCOORD",
                0,
                0,
                ShaderValueType.Float2,
                ShaderInterpolation.Smooth)));
        var pixelReflection = CreateReflection(
            ShaderStage.Pixel,
            inputs: ImmutableArray.Create(new ShaderIoSignature(
                "TEXCOORD",
                0,
                0,
                ShaderValueType.Float2,
                ShaderInterpolation.Smooth)),
            outputs: ImmutableArray.Create(new ShaderIoSignature(
                "SV_Target",
                0,
                null,
                ShaderValueType.Float4,
                ShaderInterpolation.Smooth)));

        _ = Assert.Throws<ArgumentException>(() => CreatePipelineDescription(
            bytecode,
            vertexReflection,
            pixelReflection));
        _ = Assert.Throws<ArgumentException>(() => CreatePipelineDescription(
            bytecode,
            vertexReflection,
            pixelReflection,
            vertexElements: ImmutableArray.Create(new VertexElementDescription(0, VertexElementFormat.Float2, 0)),
            vertexStride: 8));
        _ = Assert.Throws<ArgumentException>(() => CreatePipelineDescription(
            bytecode,
            vertexReflection,
            pixelReflection,
            vertexElements: ImmutableArray.Create(new VertexElementDescription(0, VertexElementFormat.Float3, 4)),
            vertexStride: 12));
        _ = Assert.Throws<ArgumentException>(() => CreatePipelineDescription(
            bytecode,
            vertexReflection,
            vertexElements: ImmutableArray.Create(new VertexElementDescription(0, VertexElementFormat.Float3, 0)),
            vertexStride: 16,
            pixelReflection: CreateReflection(
                ShaderStage.Pixel,
                inputs: ImmutableArray.Create(new ShaderIoSignature(
                    "TEXCOORD",
                    0,
                    1,
                    ShaderValueType.Float2,
                    ShaderInterpolation.Smooth)),
                outputs: pixelReflection.Outputs)));
    }
}
