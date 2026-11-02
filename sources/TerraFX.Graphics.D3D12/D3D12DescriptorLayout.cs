// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Graphics;
using TerraFX.Interop.DirectX;

namespace TerraFX.Graphics.D3D12;

internal static class D3D12DescriptorLayout
{
    internal const int TableCount = 16;

    internal static int Compare(ShaderResourceSignature left, ShaderResourceSignature right)
    {
        var tableComparison = GetTableIndex(left).CompareTo(GetTableIndex(right));
        return (tableComparison != 0)
            ? tableComparison
            : left.Binding.CompareTo(right.Binding);
    }

    internal static D3D12_DESCRIPTOR_RANGE_TYPE GetRangeType(ShaderResourceKind kind)
    {
        return kind switch {
            ShaderResourceKind.UniformBuffer => D3D12_DESCRIPTOR_RANGE_TYPE.D3D12_DESCRIPTOR_RANGE_TYPE_CBV,
            ShaderResourceKind.SampledImage => D3D12_DESCRIPTOR_RANGE_TYPE.D3D12_DESCRIPTOR_RANGE_TYPE_SRV,
            ShaderResourceKind.StorageImage or ShaderResourceKind.StorageBuffer => D3D12_DESCRIPTOR_RANGE_TYPE.D3D12_DESCRIPTOR_RANGE_TYPE_UAV,
            ShaderResourceKind.Sampler => D3D12_DESCRIPTOR_RANGE_TYPE.D3D12_DESCRIPTOR_RANGE_TYPE_SAMPLER,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    internal static int GetRegisterClassIndex(ShaderResourceKind kind)
    {
        return kind switch {
            ShaderResourceKind.UniformBuffer => 0,
            ShaderResourceKind.SampledImage => 1,
            ShaderResourceKind.StorageImage or ShaderResourceKind.StorageBuffer => 2,
            ShaderResourceKind.Sampler => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    internal static int GetRegisterSpace(ShaderBindingGroup group)
    {
        return group switch {
            ShaderBindingGroup.Frame => 0,
            ShaderBindingGroup.Material => 1,
            ShaderBindingGroup.ObjectData => 2,
            ShaderBindingGroup.Pass => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(group)),
        };
    }

    internal static int GetTableIndex(ShaderBindingGroup group, ShaderResourceKind kind)
        => checked((GetRegisterSpace(group) * 4) + GetRegisterClassIndex(kind));

    internal static int GetTableIndex(ShaderResourceSignature resource)
        => GetTableIndex(resource.Group, resource.Kind);
}
