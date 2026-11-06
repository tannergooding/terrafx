// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Immutable;
using TerraFX.Graphics;
using TerraFX.Interop.DirectX;

namespace TerraFX.Graphics.D3D12;

internal unsafe interface ID3D12BackendPipelineDescriptorOwner : IGraphicsBackendPipeline
{
    bool IsDisposed { get; }

    D3D12GraphicsBackend.D3D12BackendFactory.D3D12BackendDevice Owner { get; }

    ID3D12PipelineState* PipelineState { get; }

    ImmutableArray<ShaderResourceSignature> Resources { get; }

    ID3D12RootSignature* RootSignature { get; }

    void ReleaseDescriptorSet();

    void RetainDescriptorSet();
}
