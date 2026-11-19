// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Graphics;
using TerraFX.Interop.DirectX;

namespace TerraFX.Graphics.D3D12;

internal sealed class D3D12GraphSubmissionStorage : IGraphicsBackendGraphSubmissionStorage
{
    internal D3D12GraphSubmissionStorage(int descriptorSetCapacity, int renderTargetViewCapacity)
    {
        DescriptorSets =
            new D3D12GraphicsBackend.D3D12BackendFactory.D3D12BackendDevice.D3D12BackendDescriptorSet[descriptorSetCapacity];
        RenderTargetViews = new D3D12_CPU_DESCRIPTOR_HANDLE[renderTargetViewCapacity];
    }

    internal D3D12GraphicsBackend.D3D12BackendFactory.D3D12BackendDevice.D3D12BackendDescriptorSet[] DescriptorSets { get; }

    internal D3D12_CPU_DESCRIPTOR_HANDLE[] RenderTargetViews { get; }

    public void Dispose()
    {
        Array.Clear(DescriptorSets);
        Array.Clear(RenderTargetViews);
    }
}
