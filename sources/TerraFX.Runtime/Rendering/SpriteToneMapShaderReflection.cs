// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

internal static class SpriteToneMapShaderReflection
{
    private static readonly ShaderCompilerIdentity s_compilerIdentity = new(
        "Microsoft.Direct3D.DXC",
        "v1.8.2505.1",
        "1.8.2505.32",
        "win-x64",
        [.. Convert.FromHexString("C6E82B70C14552F1DD58E4A79C93EEAB1567EEB0A9EE63A51564C410429BCE3E")]);

    internal static ShaderReflection Pixel { get; } = CreatePixel();

    private static ShaderReflection CreatePixel()
        => new(
            ShaderStage.Pixel,
            "PSMain",
            s_compilerIdentity,
            [
                new ShaderIoSignature("SV_Position", 0, null, ShaderValueType.Float4, ShaderInterpolation.NoPerspective),
                new ShaderIoSignature("TEXCOORD", 0, 0, ShaderValueType.Float2, ShaderInterpolation.Smooth),
            ],
            [new ShaderIoSignature("SV_Target", 0, null, ShaderValueType.Float4, ShaderInterpolation.Smooth)],
            [
                new ShaderResourceSignature(ShaderBindingGroup.Material, 0, ShaderResourceKind.SampledImage, 1),
                new ShaderResourceSignature(ShaderBindingGroup.Material, 0, ShaderResourceKind.Sampler, 1),
            ],
            [],
            [],
            bindingLayoutVersion: 1,
            groupSizeX: 0,
            groupSizeY: 0,
            groupSizeZ: 0);

}
