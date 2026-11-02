// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Graphics;
using TerraFX.Interop.DirectX;

namespace TerraFX.Graphics.D3D12;

internal static class D3D12GraphResourceStateMapper
{
    internal static D3D12_RESOURCE_STATES GetBufferState(
        GraphResourceState state,
        BufferDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);

        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        if (state is GraphResourceState.Undefined or GraphResourceState.General)
        {
            return GetInitialState(description.MemoryKind);
        }

        var requiredUsage = GetRequiredBufferUsage(state);

        if ((description.Usage & requiredUsage) == 0)
        {
            throw new ArgumentException("The buffer description does not permit the selected graph state.", nameof(state));
        }

        return description.MemoryKind switch {
            GpuMemoryKind.DeviceLocal => state switch {
                GraphResourceState.VertexRead or GraphResourceState.UniformRead
                    => D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_VERTEX_AND_CONSTANT_BUFFER,
                GraphResourceState.IndexRead
                    => D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_INDEX_BUFFER,
                GraphResourceState.StorageRead or GraphResourceState.StorageWrite
                    => D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS,
                GraphResourceState.CopySource
                    => D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_SOURCE,
                GraphResourceState.CopyDestination
                    => D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST,
                _ => throw new ArgumentOutOfRangeException(nameof(state)),
            },
            GpuMemoryKind.Upload => D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ,
            GpuMemoryKind.Readback => D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST,
            _ => throw new ArgumentOutOfRangeException(nameof(description)),
        };
    }

    internal static D3D12_RESOURCE_STATES GetInitialState(GpuMemoryKind memoryKind)
        => memoryKind switch {
            GpuMemoryKind.DeviceLocal => D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON,
            GpuMemoryKind.Upload => D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ,
            GpuMemoryKind.Readback => D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST,
            _ => throw new ArgumentOutOfRangeException(nameof(memoryKind)),
        };

    internal static D3D12_RESOURCE_STATES GetTextureState(
        GraphResourceState state,
        TextureDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);

        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        if (state is GraphResourceState.Undefined or GraphResourceState.General)
        {
            return D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON;
        }

        var requiredUsage = GetRequiredTextureUsage(state);

        if ((description.Usage & requiredUsage) == 0)
        {
            throw new ArgumentException("The texture description does not permit the selected graph state.", nameof(state));
        }

        if ((state is GraphResourceState.DepthRead or GraphResourceState.DepthWrite)
            != (description.Format == TextureFormat.D32Float))
        {
            throw new ArgumentException("The selected graph depth state does not match the texture format.", nameof(state));
        }

        return state switch {
            GraphResourceState.CopySource => D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_SOURCE,
            GraphResourceState.CopyDestination => D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST,
            GraphResourceState.ShaderSampledRead
                => D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE
                    | D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE,
            GraphResourceState.StorageRead or GraphResourceState.StorageWrite
                => D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS,
            GraphResourceState.ColorAttachment => D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RENDER_TARGET,
            GraphResourceState.DepthRead => D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_DEPTH_READ,
            GraphResourceState.DepthWrite => D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_DEPTH_WRITE,
            GraphResourceState.Present => D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PRESENT,
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
    }

    private static BufferUsage GetRequiredBufferUsage(GraphResourceState state)
        => state switch {
            GraphResourceState.VertexRead => BufferUsage.Vertex,
            GraphResourceState.IndexRead => BufferUsage.Index,
            GraphResourceState.UniformRead => BufferUsage.Uniform,
            GraphResourceState.StorageRead or GraphResourceState.StorageWrite => BufferUsage.Storage,
            GraphResourceState.CopySource => BufferUsage.CopySource,
            GraphResourceState.CopyDestination => BufferUsage.CopyDestination,
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

    private static TextureUsage GetRequiredTextureUsage(GraphResourceState state)
        => state switch {
            GraphResourceState.CopySource => TextureUsage.CopySource,
            GraphResourceState.CopyDestination => TextureUsage.CopyDestination,
            GraphResourceState.ShaderSampledRead => TextureUsage.Sampled,
            GraphResourceState.StorageRead or GraphResourceState.StorageWrite => TextureUsage.Storage,
            GraphResourceState.ColorAttachment => TextureUsage.ColorAttachment,
            GraphResourceState.DepthRead or GraphResourceState.DepthWrite => TextureUsage.DepthAttachment,
            GraphResourceState.Present => TextureUsage.ColorAttachment,
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
}
