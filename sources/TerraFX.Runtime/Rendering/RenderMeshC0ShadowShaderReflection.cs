// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

internal static class RenderMeshC0ShadowShaderReflection
{
    private const int Matrix4x4SizeInBytes = 16 * sizeof(float);
    private static readonly ShaderCompilerIdentity s_compilerIdentity = new(
        "Microsoft.Direct3D.DXC",
        "v1.8.2505.1",
        "1.8.2505.32",
        "win-x64",
        [.. Convert.FromHexString("C6E82B70C14552F1DD58E4A79C93EEAB1567EEB0A9EE63A51564C410429BCE3E")]);

    internal static ShaderReflection Pixel { get; } = CreatePixel(textured: false);

    internal static ShaderReflection SkinnedTexturedVertex { get; } = CreateVertex(skinned: true, textured: true);

    internal static ShaderReflection SkinnedVertex { get; } = CreateVertex(skinned: true, textured: false);

    internal static ShaderReflection TexturedPixel { get; } = CreatePixel(textured: true);

    internal static ShaderReflection TexturedVertex { get; } = CreateVertex(skinned: false, textured: true);

    internal static ShaderReflection Vertex { get; } = CreateVertex(skinned: false, textured: false);

    private static ShaderReflection CreatePixel(bool textured)
    {
        var inputs = ImmutableArray.CreateBuilder<ShaderIoSignature>(textured ? 3 : 2);
        inputs.Add(new ShaderIoSignature("SV_Position", 0, null, ShaderValueType.Float4, ShaderInterpolation.NoPerspective));

        if (textured)
        {
            inputs.Add(new ShaderIoSignature("TEXCOORD", 0, 0, ShaderValueType.Float2, ShaderInterpolation.Smooth));
        }

        inputs.Add(new ShaderIoSignature(
            "COLOR",
            0,
            textured ? 1 : 0,
            ShaderValueType.Float4,
            ShaderInterpolation.Smooth));
        var resources = ImmutableArray.CreateBuilder<ShaderResourceSignature>(textured ? 3 : 1);
        resources.Add(new ShaderResourceSignature(ShaderBindingGroup.Material, 0, ShaderResourceKind.UniformBuffer, 1));

        if (textured)
        {
            resources.Add(new ShaderResourceSignature(ShaderBindingGroup.Material, 0, ShaderResourceKind.SampledImage, 1));
            resources.Add(new ShaderResourceSignature(ShaderBindingGroup.Material, 0, ShaderResourceKind.Sampler, 1));
        }

        return new ShaderReflection(
            ShaderStage.Pixel,
            "PSMain",
            s_compilerIdentity,
            inputs.MoveToImmutable(),
            [],
            resources.MoveToImmutable(),
            [
                new ShaderRecordLayout(
                    "MaterialData",
                    RenderMeshC0MaterialData.SizeInBytes,
                    [
                        new ShaderFieldLayout("BaseColor", ShaderValueType.Float4, 0, 16, 1, 0),
                        new ShaderFieldLayout("Factors", ShaderValueType.Float4, 16, 16, 1, 0),
                        new ShaderFieldLayout("Emissive", ShaderValueType.Float4, 32, 16, 1, 0),
                        new ShaderFieldLayout("RenderOptions", ShaderValueType.Float4, 48, 16, 1, 0),
                        new ShaderFieldLayout("NormalOptions", ShaderValueType.Float4, 64, 16, 1, 0),
                    ]),
            ],
            [],
            bindingLayoutVersion: 1,
            groupSizeX: 0,
            groupSizeY: 0,
            groupSizeZ: 0);
    }

    private static ShaderReflection CreateVertex(bool skinned, bool textured)
    {
        var inputs = ImmutableArray.CreateBuilder<ShaderIoSignature>(skinned ? textured ? 5 : 4 : textured ? 3 : 2);
        inputs.Add(new ShaderIoSignature("POSITION", 0, 0, ShaderValueType.Float3, ShaderInterpolation.Smooth));

        if (textured)
        {
            inputs.Add(new ShaderIoSignature("TEXCOORD", 0, 1, ShaderValueType.Float2, ShaderInterpolation.Smooth));
        }

        var colorLocation = textured ? 2 : 1;
        inputs.Add(new ShaderIoSignature("COLOR", 0, colorLocation, ShaderValueType.Float4, ShaderInterpolation.Smooth));

        if (skinned)
        {
            inputs.Add(new ShaderIoSignature(
                "BLENDINDICES",
                0,
                textured ? 3 : 2,
                ShaderValueType.Float4,
                ShaderInterpolation.Smooth));
            inputs.Add(new ShaderIoSignature(
                "BLENDWEIGHT",
                0,
                textured ? 4 : 3,
                ShaderValueType.Float4,
                ShaderInterpolation.Smooth));
        }

        var resources = ImmutableArray.CreateBuilder<ShaderResourceSignature>(skinned ? 3 : 2);
        resources.Add(new ShaderResourceSignature(ShaderBindingGroup.Frame, 0, ShaderResourceKind.UniformBuffer, 1));
        resources.Add(new ShaderResourceSignature(ShaderBindingGroup.ObjectData, 0, ShaderResourceKind.UniformBuffer, 1));

        if (skinned)
        {
            resources.Add(new ShaderResourceSignature(
                ShaderBindingGroup.ObjectData,
                0,
                ShaderResourceKind.StorageBuffer,
                arrayCount: 1,
                access: ShaderResourceAccess.ReadWrite));
        }

        var outputs = ImmutableArray.CreateBuilder<ShaderIoSignature>(textured ? 3 : 2);
        outputs.Add(new ShaderIoSignature("SV_Position", 0, null, ShaderValueType.Float4, ShaderInterpolation.NoPerspective));

        if (textured)
        {
            outputs.Add(new ShaderIoSignature("TEXCOORD", 0, 0, ShaderValueType.Float2, ShaderInterpolation.Smooth));
        }

        outputs.Add(new ShaderIoSignature(
            "COLOR",
            0,
            textured ? 1 : 0,
            ShaderValueType.Float4,
            ShaderInterpolation.Smooth));

        return new ShaderReflection(
            ShaderStage.Vertex,
            "VSMain",
            s_compilerIdentity,
            inputs.MoveToImmutable(),
            outputs.MoveToImmutable(),
            resources.MoveToImmutable(),
            [
                new ShaderRecordLayout(
                    "FrameData",
                    Matrix4x4SizeInBytes,
                    [new ShaderFieldLayout("ViewProjectionRows", ShaderValueType.Float4, 0, 16, 4, 16)]),
                new ShaderRecordLayout(
                    "ObjectData",
                    ObjectGpuData.SizeInBytes,
                    [
                        new ShaderFieldLayout("WorldRows", ShaderValueType.Float4, 0, 16, 4, 16),
                        new ShaderFieldLayout("PreviousWorldRows", ShaderValueType.Float4, 64, 16, 4, 16),
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
}
