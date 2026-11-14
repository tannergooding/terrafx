// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

internal static class RenderMeshC0ShaderReflection
{
    private static readonly ShaderCompilerIdentity s_compilerIdentity = new(
        "Microsoft.Direct3D.DXC",
        "v1.8.2505.1",
        "1.8.2505.32",
        "win-x64",
        [.. Convert.FromHexString("C6E82B70C14552F1DD58E4A79C93EEAB1567EEB0A9EE63A51564C410429BCE3E")]);

    internal static ShaderReflection Pixel { get; } = CreatePixel();

    internal static ShaderReflection TexturedPixel { get; } = CreatePixel(textured: true);

    internal static ShaderReflection Vertex { get; } = CreateVertex();

    private static ShaderReflection CreatePixel()
        => CreatePixel(textured: false);

    private static ShaderReflection CreatePixel(bool textured)
    {
        var inputs = ImmutableArray.CreateBuilder<ShaderIoSignature>(textured ? 4 : 3);
        inputs.Add(new ShaderIoSignature("SV_Position", 0, null, ShaderValueType.Float4, ShaderInterpolation.NoPerspective));
        inputs.Add(new ShaderIoSignature("TEXCOORD", 0, 0, ShaderValueType.Float3, ShaderInterpolation.Smooth));
        inputs.Add(new ShaderIoSignature("TEXCOORD", 1, 1, ShaderValueType.Float3, ShaderInterpolation.Smooth));

        if (textured)
        {
            inputs.Add(new ShaderIoSignature("TEXCOORD", 2, 2, ShaderValueType.Float2, ShaderInterpolation.Smooth));
        }

        var resources = ImmutableArray.CreateBuilder<ShaderResourceSignature>(textured ? 8 : 2);
        resources.Add(new ShaderResourceSignature(ShaderBindingGroup.Frame, 0, ShaderResourceKind.UniformBuffer, 1));
        resources.Add(new ShaderResourceSignature(ShaderBindingGroup.Material, 0, ShaderResourceKind.UniformBuffer, 1));

        if (textured)
        {
            resources.Add(new ShaderResourceSignature(ShaderBindingGroup.Material, 0, ShaderResourceKind.SampledImage, 1));
            resources.Add(new ShaderResourceSignature(ShaderBindingGroup.Material, 1, ShaderResourceKind.SampledImage, 1));
            resources.Add(new ShaderResourceSignature(ShaderBindingGroup.Material, 2, ShaderResourceKind.SampledImage, 1));
            resources.Add(new ShaderResourceSignature(ShaderBindingGroup.Material, 0, ShaderResourceKind.Sampler, 1));
            resources.Add(new ShaderResourceSignature(ShaderBindingGroup.Material, 1, ShaderResourceKind.Sampler, 1));
            resources.Add(new ShaderResourceSignature(ShaderBindingGroup.Material, 2, ShaderResourceKind.Sampler, 1));
        }

        return new ShaderReflection(
            ShaderStage.Pixel,
            "PSMain",
            s_compilerIdentity,
            inputs.MoveToImmutable(),
            [new ShaderIoSignature("SV_Target", 0, null, ShaderValueType.Float4, ShaderInterpolation.Smooth)],
            resources.MoveToImmutable(),
            [
                new ShaderRecordLayout(
                    "FrameData",
                    RenderMeshC0FrameData.SizeInBytes,
                    [
                        new ShaderFieldLayout("ViewProjection", ShaderValueType.Float4x4, 0, 64, 1, 0),
                        new ShaderFieldLayout("RenderArea", ShaderValueType.Float4, 64, 16, 1, 0),
                        new ShaderFieldLayout("CameraPosition", ShaderValueType.Float4, 80, 16, 1, 0),
                        new ShaderFieldLayout("AmbientIrradiance", ShaderValueType.Float4, 96, 16, 1, 0),
                        new ShaderFieldLayout("LightDirection", ShaderValueType.Float4, 112, 16, 1, 0),
                        new ShaderFieldLayout("LightRadiance", ShaderValueType.Float4, 128, 16, 1, 0),
                        new ShaderFieldLayout("LightParameters", ShaderValueType.Float4, 144, 16, 1, 0),
                    ]),
                new ShaderRecordLayout(
                    "MaterialData",
                    RenderMeshC0MaterialData.SizeInBytes,
                    [
                        new ShaderFieldLayout("BaseColor", ShaderValueType.Float4, 0, 16, 1, 0),
                        new ShaderFieldLayout("Factors", ShaderValueType.Float4, 16, 16, 1, 0),
                        new ShaderFieldLayout("Emissive", ShaderValueType.Float4, 32, 16, 1, 0),
                        new ShaderFieldLayout("RenderOptions", ShaderValueType.Float4, 48, 16, 1, 0),
                    ]),
            ],
            [],
            bindingLayoutVersion: 1,
            groupSizeX: 0,
            groupSizeY: 0,
            groupSizeZ: 0);
    }

    private static ShaderReflection CreateVertex()
        => new(
            ShaderStage.Vertex,
            "VSMain",
            s_compilerIdentity,
            [
                new ShaderIoSignature("POSITION", 0, 0, ShaderValueType.Float3, ShaderInterpolation.Smooth),
                new ShaderIoSignature("NORMAL", 0, 1, ShaderValueType.Float3, ShaderInterpolation.Smooth),
                new ShaderIoSignature("TEXCOORD", 0, 2, ShaderValueType.Float2, ShaderInterpolation.Smooth),
            ],
            [
                new ShaderIoSignature("SV_Position", 0, null, ShaderValueType.Float4, ShaderInterpolation.NoPerspective),
                new ShaderIoSignature("TEXCOORD", 0, 0, ShaderValueType.Float3, ShaderInterpolation.Smooth),
                new ShaderIoSignature("TEXCOORD", 1, 1, ShaderValueType.Float3, ShaderInterpolation.Smooth),
                new ShaderIoSignature("TEXCOORD", 2, 2, ShaderValueType.Float2, ShaderInterpolation.Smooth),
            ],
            [
                new ShaderResourceSignature(ShaderBindingGroup.Frame, 0, ShaderResourceKind.UniformBuffer, 1),
                new ShaderResourceSignature(ShaderBindingGroup.ObjectData, 0, ShaderResourceKind.UniformBuffer, 1),
            ],
            [
                new ShaderRecordLayout(
                    "FrameData",
                    RenderMeshC0FrameData.SizeInBytes,
                    [
                        new ShaderFieldLayout("ViewProjection", ShaderValueType.Float4x4, 0, 64, 1, 0),
                        new ShaderFieldLayout("RenderArea", ShaderValueType.Float4, 64, 16, 1, 0),
                        new ShaderFieldLayout("CameraPosition", ShaderValueType.Float4, 80, 16, 1, 0),
                        new ShaderFieldLayout("AmbientIrradiance", ShaderValueType.Float4, 96, 16, 1, 0),
                        new ShaderFieldLayout("LightDirection", ShaderValueType.Float4, 112, 16, 1, 0),
                        new ShaderFieldLayout("LightRadiance", ShaderValueType.Float4, 128, 16, 1, 0),
                        new ShaderFieldLayout("LightParameters", ShaderValueType.Float4, 144, 16, 1, 0),
                    ]),
                new ShaderRecordLayout(
                    "ObjectData",
                    192,
                    [
                        new ShaderFieldLayout("World", ShaderValueType.Float4x4, 0, 64, 1, 0),
                        new ShaderFieldLayout("PreviousWorld", ShaderValueType.Float4x4, 64, 64, 1, 0),
                        new ShaderFieldLayout("NormalRow0", ShaderValueType.Float4, 128, 16, 1, 0),
                        new ShaderFieldLayout("NormalRow1", ShaderValueType.Float4, 144, 16, 1, 0),
                        new ShaderFieldLayout("NormalRow2", ShaderValueType.Float4, 160, 16, 1, 0),
                        new ShaderFieldLayout("Metadata", ShaderValueType.UInt4, 176, 16, 1, 0),
                    ]),
            ],
            [],
            bindingLayoutVersion: 1,
            groupSizeX: 0,
            groupSizeY: 0,
            groupSizeZ: 0);
}
