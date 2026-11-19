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

    internal static ShaderReflection SkinnedNormalDiagnosticCompute { get; } = CreateSkinnedNormalDiagnosticCompute();

    internal static ShaderReflection SkinnedTexturedVertex { get; } = CreateVertex(skinned: true, textured: true);

    internal static ShaderReflection SkinnedVertex { get; } = CreateVertex(skinned: true, textured: false);

    internal static ShaderReflection TexturedPixel { get; } = CreatePixel(textured: true);

    internal static ShaderReflection TexturedVertex { get; } = CreateVertex(skinned: false, textured: true);

    internal static ShaderReflection Vertex { get; } = CreateVertex(skinned: false, textured: false);

    private static ShaderReflection CreatePixel()
        => CreatePixel(textured: false);

    private static ShaderReflection CreatePixel(bool textured)
    {
        var inputs = ImmutableArray.CreateBuilder<ShaderIoSignature>(textured ? 6 : 4);
        inputs.Add(new ShaderIoSignature("SV_Position", 0, null, ShaderValueType.Float4, ShaderInterpolation.NoPerspective));
        inputs.Add(new ShaderIoSignature("TEXCOORD", 0, 0, ShaderValueType.Float3, ShaderInterpolation.Smooth));
        inputs.Add(new ShaderIoSignature("TEXCOORD", 1, 1, ShaderValueType.Float3, ShaderInterpolation.Smooth));

        if (textured)
        {
            inputs.Add(new ShaderIoSignature("TEXCOORD", 2, 2, ShaderValueType.Float2, ShaderInterpolation.Smooth));
            inputs.Add(new ShaderIoSignature("TEXCOORD", 3, 3, ShaderValueType.Float4, ShaderInterpolation.Smooth));
        }

        inputs.Add(textured
            ? new ShaderIoSignature("TEXCOORD", 4, 4, ShaderValueType.Float4, ShaderInterpolation.Smooth)
            : new ShaderIoSignature("TEXCOORD", 2, 2, ShaderValueType.Float4, ShaderInterpolation.Smooth));

        var resources = ImmutableArray.CreateBuilder<ShaderResourceSignature>(textured ? 15 : 7);
        resources.Add(new ShaderResourceSignature(ShaderBindingGroup.Frame, 0, ShaderResourceKind.UniformBuffer, 1));
        resources.Add(new ShaderResourceSignature(ShaderBindingGroup.Frame, 1, ShaderResourceKind.UniformBuffer, 1));
        resources.Add(new ShaderResourceSignature(ShaderBindingGroup.Frame, 2, ShaderResourceKind.UniformBuffer, 1));
        resources.Add(new ShaderResourceSignature(ShaderBindingGroup.Frame, 3, ShaderResourceKind.UniformBuffer, 1));
        resources.Add(new ShaderResourceSignature(ShaderBindingGroup.Frame, 0, ShaderResourceKind.SampledImage, 1));
        resources.Add(new ShaderResourceSignature(ShaderBindingGroup.Frame, 0, ShaderResourceKind.Sampler, 1));
        resources.Add(new ShaderResourceSignature(ShaderBindingGroup.Material, 0, ShaderResourceKind.UniformBuffer, 1));

        if (textured)
        {
            resources.Add(new ShaderResourceSignature(ShaderBindingGroup.Material, 0, ShaderResourceKind.SampledImage, 1));
            resources.Add(new ShaderResourceSignature(ShaderBindingGroup.Material, 1, ShaderResourceKind.SampledImage, 1));
            resources.Add(new ShaderResourceSignature(ShaderBindingGroup.Material, 2, ShaderResourceKind.SampledImage, 1));
            resources.Add(new ShaderResourceSignature(ShaderBindingGroup.Material, 3, ShaderResourceKind.SampledImage, 1));
            resources.Add(new ShaderResourceSignature(ShaderBindingGroup.Material, 0, ShaderResourceKind.Sampler, 1));
            resources.Add(new ShaderResourceSignature(ShaderBindingGroup.Material, 1, ShaderResourceKind.Sampler, 1));
            resources.Add(new ShaderResourceSignature(ShaderBindingGroup.Material, 2, ShaderResourceKind.Sampler, 1));
            resources.Add(new ShaderResourceSignature(ShaderBindingGroup.Material, 3, ShaderResourceKind.Sampler, 1));
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
                        new ShaderFieldLayout("PointLightCount", ShaderValueType.UInt4, 112, 16, 1, 0),
                        new ShaderFieldLayout("CameraForward", ShaderValueType.Float4, 128, 16, 1, 0),
                        new ShaderFieldLayout("CascadeEnds", ShaderValueType.Float4, 144, 16, 1, 0),
                        new ShaderFieldLayout(
                            "ShadowViewProjectionRows",
                            ShaderValueType.Float4,
                            160,
                            16,
                            16,
                            16),
                        new ShaderFieldLayout(
                            "SpotShadowViewProjectionRows",
                            ShaderValueType.Float4,
                            416,
                            16,
                            16,
                            16),
                    ]),
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
                new ShaderRecordLayout(
                    "PointLightData",
                    RenderMeshC0PointLightBufferBuilder.SizeInBytes,
                    [
                        new ShaderFieldLayout(
                            "PointLightPositionRange",
                            ShaderValueType.Float4,
                            RenderMeshC0PointLightBufferBuilder.PositionRangeOffset,
                            16,
                            RenderMeshC0PointLightBufferBuilder.Capacity,
                            16),
                        new ShaderFieldLayout(
                            "PointLightRadiance",
                            ShaderValueType.Float4,
                            RenderMeshC0PointLightBufferBuilder.RadianceOffset,
                            16,
                            RenderMeshC0PointLightBufferBuilder.Capacity,
                            16),
                    ]),
                new ShaderRecordLayout(
                    "DirectionalLightData",
                    RenderDirectionalLightBufferBuilder.SizeInBytes,
                    [
                        new ShaderFieldLayout("DirectionalLightCount", ShaderValueType.UInt4, 0, 16, 1, 0),
                        new ShaderFieldLayout(
                            "DirectionalLightDirectionAndShadow",
                            ShaderValueType.Float4,
                            RenderDirectionalLightBufferBuilder.DirectionOffset,
                            16,
                            RenderDirectionalLightBufferBuilder.Capacity,
                            16),
                        new ShaderFieldLayout(
                            "DirectionalLightRadiance",
                            ShaderValueType.Float4,
                            RenderDirectionalLightBufferBuilder.RadianceOffset,
                            16,
                            RenderDirectionalLightBufferBuilder.Capacity,
                            16),
                    ]),
                new ShaderRecordLayout(
                    "SpotLightData",
                    RenderMeshC0SpotLightBufferBuilder.SizeInBytes,
                    [
                        new ShaderFieldLayout("SpotLightCount", ShaderValueType.UInt4, RenderMeshC0SpotLightBufferBuilder.CountOffset, 16, 1, 0),
                        new ShaderFieldLayout(
                            "SpotLightPositionRange",
                            ShaderValueType.Float4,
                            RenderMeshC0SpotLightBufferBuilder.PositionRangeOffset,
                            16,
                            RenderMeshC0SpotLightBufferBuilder.Capacity,
                            16),
                        new ShaderFieldLayout(
                            "SpotLightDirectionOuterCosine",
                            ShaderValueType.Float4,
                            RenderMeshC0SpotLightBufferBuilder.DirectionOuterCosineOffset,
                            16,
                            RenderMeshC0SpotLightBufferBuilder.Capacity,
                            16),
                        new ShaderFieldLayout(
                            "SpotLightRadiance",
                            ShaderValueType.Float4,
                            RenderMeshC0SpotLightBufferBuilder.RadianceOffset,
                            16,
                            RenderMeshC0SpotLightBufferBuilder.Capacity,
                            16),
                        new ShaderFieldLayout(
                            "SpotLightInnerCosineShadowSlot",
                            ShaderValueType.Float4,
                            RenderMeshC0SpotLightBufferBuilder.InnerCosineShadowSlotOffset,
                            16,
                            RenderMeshC0SpotLightBufferBuilder.Capacity,
                            16),
                    ]),
            ],
            [],
            bindingLayoutVersion: 1,
            groupSizeX: 0,
            groupSizeY: 0,
            groupSizeZ: 0);
    }

    private static ShaderReflection CreateSkinnedNormalDiagnosticCompute()
        => new(
            ShaderStage.Compute,
            "CSMain",
            s_compilerIdentity,
            [],
            [],
            [
                new ShaderResourceSignature(
                    ShaderBindingGroup.Frame,
                    0,
                    ShaderResourceKind.StorageBuffer,
                    arrayCount: 1,
                    access: ShaderResourceAccess.ReadWrite),
                new ShaderResourceSignature(
                    ShaderBindingGroup.Frame,
                    1,
                    ShaderResourceKind.StorageBuffer,
                    arrayCount: 1,
                    access: ShaderResourceAccess.ReadWrite),
                new ShaderResourceSignature(
                    ShaderBindingGroup.Frame,
                    2,
                    ShaderResourceKind.StorageBuffer,
                    arrayCount: 1,
                    access: ShaderResourceAccess.ReadWrite),
                new ShaderResourceSignature(
                    ShaderBindingGroup.Frame,
                    3,
                    ShaderResourceKind.StorageBuffer,
                    arrayCount: 1,
                    access: ShaderResourceAccess.ReadWrite),
            ],
            [
                new ShaderRecordLayout(
                    "BonePaletteEntry",
                    RenderMeshC0BoneBufferBuilder.Stride,
                    [
                        new ShaderFieldLayout("SkinRow0", ShaderValueType.Float4, 0, 16, 1, 0),
                        new ShaderFieldLayout("SkinRow1", ShaderValueType.Float4, 16, 16, 1, 0),
                        new ShaderFieldLayout("SkinRow2", ShaderValueType.Float4, 32, 16, 1, 0),
                        new ShaderFieldLayout("SkinRow3", ShaderValueType.Float4, 48, 16, 1, 0),
                        new ShaderFieldLayout("NormalRow0", ShaderValueType.Float4, 64, 16, 1, 0),
                        new ShaderFieldLayout("NormalRow1", ShaderValueType.Float4, 80, 16, 1, 0),
                        new ShaderFieldLayout("NormalRow2", ShaderValueType.Float4, 96, 16, 1, 0),
                    ]),
                new ShaderRecordLayout(
                    "DiagnosticResultElement",
                    16,
                    [
                        new ShaderFieldLayout("Value", ShaderValueType.UInt4, 0, 16, 1, 0),
                    ]),
                new ShaderRecordLayout(
                    "DiagnosticVertex",
                    RenderMeshC0VertexBufferBuilder.VertexStride,
                    [
                        new ShaderFieldLayout("PositionNormalX", ShaderValueType.Float4, 0, 16, 1, 0),
                        new ShaderFieldLayout("NormalYzUv", ShaderValueType.Float4, 16, 16, 1, 0),
                        new ShaderFieldLayout("Tangent", ShaderValueType.Float4, 32, 16, 1, 0),
                        new ShaderFieldLayout("Color", ShaderValueType.Float4, 48, 16, 1, 0),
                        new ShaderFieldLayout("JointIndices", ShaderValueType.Float4, 64, 16, 1, 0),
                        new ShaderFieldLayout("JointWeights", ShaderValueType.Float4, 80, 16, 1, 0),
                    ]),
                new ShaderRecordLayout(
                    "DiagnosticWorkElement",
                    16,
                    [
                        new ShaderFieldLayout("Value", ShaderValueType.UInt4, 0, 16, 1, 0),
                    ]),
            ],
            [],
            bindingLayoutVersion: 1,
            groupSizeX: 64,
            groupSizeY: 1,
            groupSizeZ: 1);

    private static ShaderReflection CreateVertex(bool skinned, bool textured)
    {
        var inputs = ImmutableArray.CreateBuilder<ShaderIoSignature>(skinned ? textured ? 7 : 5 : textured ? 5 : 3);
        inputs.Add(new ShaderIoSignature("POSITION", 0, 0, ShaderValueType.Float3, ShaderInterpolation.Smooth));
        inputs.Add(new ShaderIoSignature("NORMAL", 0, 1, ShaderValueType.Float3, ShaderInterpolation.Smooth));

        if (textured)
        {
            inputs.Add(new ShaderIoSignature("TEXCOORD", 0, 2, ShaderValueType.Float2, ShaderInterpolation.Smooth));
            inputs.Add(new ShaderIoSignature("TANGENT", 0, 3, ShaderValueType.Float4, ShaderInterpolation.Smooth));
        }

        inputs.Add(new ShaderIoSignature(
            "COLOR",
            0,
            textured ? 4 : 2,
            ShaderValueType.Float4,
            ShaderInterpolation.Smooth));

        if (skinned)
        {
            inputs.Add(new ShaderIoSignature(
                "BLENDINDICES",
                0,
                textured ? 5 : 3,
                ShaderValueType.Float4,
                ShaderInterpolation.Smooth));
            inputs.Add(new ShaderIoSignature(
                "BLENDWEIGHT",
                0,
                textured ? 6 : 4,
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

        return new ShaderReflection(
            ShaderStage.Vertex,
            "VSMain",
            s_compilerIdentity,
            inputs.MoveToImmutable(),
            CreateVertexOutputs(textured),
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
                        new ShaderFieldLayout("PointLightCount", ShaderValueType.UInt4, 112, 16, 1, 0),
                        new ShaderFieldLayout("CameraForward", ShaderValueType.Float4, 128, 16, 1, 0),
                        new ShaderFieldLayout("CascadeEnds", ShaderValueType.Float4, 144, 16, 1, 0),
                        new ShaderFieldLayout(
                            "ShadowViewProjectionRows",
                            ShaderValueType.Float4,
                            160,
                            16,
                            16,
                            16),
                        new ShaderFieldLayout(
                            "SpotShadowViewProjectionRows",
                            ShaderValueType.Float4,
                            416,
                            16,
                            16,
                            16),
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

    private static ImmutableArray<ShaderIoSignature> CreateVertexOutputs(bool textured)
    {
        var outputs = ImmutableArray.CreateBuilder<ShaderIoSignature>(textured ? 6 : 4);
        outputs.Add(new ShaderIoSignature("SV_Position", 0, null, ShaderValueType.Float4, ShaderInterpolation.NoPerspective));
        outputs.Add(new ShaderIoSignature("TEXCOORD", 0, 0, ShaderValueType.Float3, ShaderInterpolation.Smooth));
        outputs.Add(new ShaderIoSignature("TEXCOORD", 1, 1, ShaderValueType.Float3, ShaderInterpolation.Smooth));

        if (textured)
        {
            outputs.Add(new ShaderIoSignature("TEXCOORD", 2, 2, ShaderValueType.Float2, ShaderInterpolation.Smooth));
            outputs.Add(new ShaderIoSignature("TEXCOORD", 3, 3, ShaderValueType.Float4, ShaderInterpolation.Smooth));
            outputs.Add(new ShaderIoSignature("TEXCOORD", 4, 4, ShaderValueType.Float4, ShaderInterpolation.Smooth));
        }
        else
        {
            outputs.Add(new ShaderIoSignature("TEXCOORD", 2, 2, ShaderValueType.Float4, ShaderInterpolation.Smooth));
        }

        return outputs.MoveToImmutable();
    }
}
