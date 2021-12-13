// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Runtime.InteropServices;
using TerraFX.Interop.DirectX;
using TerraFX.Threading;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_STATES;
using static TerraFX.Interop.DirectX.DXGI_FORMAT;
using static TerraFX.Interop.Windows.Windows;
using static TerraFX.Threading.VolatileState;
using static TerraFX.Utilities.AssertionUtilities;
using static TerraFX.Utilities.D3D12Utilities;
using static TerraFX.Utilities.ExceptionUtilities;
using static TerraFX.Utilities.UnsafeUtilities;

namespace TerraFX.Graphics;

/// <inheritdoc />
public sealed unsafe class D3D12GraphicsTexture : GraphicsTexture
{
    private readonly ID3D12Resource* _d3d12Resource;
    private readonly D3D12_RESOURCE_STATES _d3d12ResourceState;

    private VolatileState _state;

    internal D3D12GraphicsTexture(D3D12GraphicsDevice device, GraphicsTextureKind kind, in GraphicsMemoryHeapRegion heapRegion, GraphicsResourceCpuAccess cpuAccess, uint width, uint height, ushort depth)
        : base(device, kind, in heapRegion, cpuAccess, width, height, depth)
    {
        var d3d12ResourceState = GetD3D12ResourceState(cpuAccess);

        _d3d12Resource = CreateD3D12Resource(device, kind, in heapRegion, width, height, depth, d3d12ResourceState);
        _d3d12ResourceState = d3d12ResourceState;

        _ = _state.Transition(to: Initialized);

        static ID3D12Resource* CreateD3D12Resource(D3D12GraphicsDevice device, GraphicsTextureKind kind, in GraphicsMemoryHeapRegion heapRegion, uint width, uint height, ushort depth, D3D12_RESOURCE_STATES d3d12ResourceState)
        {
            ID3D12Resource* d3d12Resource;

            D3D12_RESOURCE_DESC d3d12ResourceDesc;

            switch (kind)
            {
                case GraphicsTextureKind.OneDimensional:
                {
                    d3d12ResourceDesc = D3D12_RESOURCE_DESC.Tex1D(DXGI_FORMAT_R8G8B8A8_UNORM, width, mipLevels: 1);
                    break;
                }

                case GraphicsTextureKind.TwoDimensional:
                {
                    d3d12ResourceDesc = D3D12_RESOURCE_DESC.Tex2D(DXGI_FORMAT_R8G8B8A8_UNORM, width, height, mipLevels: 1);
                    break;
                }

                case GraphicsTextureKind.ThreeDimensional:
                {
                    d3d12ResourceDesc = D3D12_RESOURCE_DESC.Tex3D(DXGI_FORMAT_R8G8B8A8_UNORM, width, height, depth, mipLevels: 1);
                    break;
                }

                default:
                {
                    ThrowForInvalidKind(kind);
                    break;
                }
            }

            var d3d12Device = device.D3D12Device;
            var d3d12Heap = heapRegion.Heap.As<D3D12GraphicsMemoryHeap>().D3D12Heap;

            ThrowExternalExceptionIfFailed(d3d12Device->CreatePlacedResource(
                d3d12Heap,
                heapRegion.Offset,
                &d3d12ResourceDesc,
                d3d12ResourceState,
                pOptimizedClearValue: null,
                __uuidof<ID3D12Resource>(),
                (void**)&d3d12Resource
            ));

            return d3d12Resource;
        }

        static D3D12_RESOURCE_STATES GetD3D12ResourceState(GraphicsResourceCpuAccess cpuAccess)
        {
            return cpuAccess switch {
                GraphicsResourceCpuAccess.Read => D3D12_RESOURCE_STATE_COPY_DEST,
                GraphicsResourceCpuAccess.Write => D3D12_RESOURCE_STATE_GENERIC_READ,
                _ => D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,
            };
        }
    }

    /// <summary>Finalizes an instance of the <see cref="D3D12GraphicsTexture" /> class.</summary>
    ~D3D12GraphicsTexture() => Dispose(isDisposing: true);

    /// <inheritdoc cref="GraphicsDeviceObject.Adapter" />
    public new D3D12GraphicsAdapter Adapter => base.Adapter.As<D3D12GraphicsAdapter>();

    /// <inheritdoc cref="GraphicsResource.Allocator" />
    public new D3D12GraphicsMemoryAllocator Allocator => base.Allocator.As<D3D12GraphicsMemoryAllocator>();

    /// <summary>Gets the underlying <see cref="ID3D12Resource" /> for the texture.</summary>
    public ID3D12Resource* D3D12Resource
    {
        get
        {
            AssertNotDisposedOrDisposing(_state);
            return _d3d12Resource;
        }
    }

    /// <summary>Gets the default state of the underlying <see cref="ID3D12Resource" /> for the texture.</summary>
    public D3D12_RESOURCE_STATES D3D12ResourceState => _d3d12ResourceState;

    /// <inheritdoc cref="GraphicsDeviceObject.Device" />
    public new D3D12GraphicsDevice Device => base.Device.As<D3D12GraphicsDevice>();

    /// <inheritdoc cref="GraphicsResource.Heap" />
    public new D3D12GraphicsMemoryHeap Heap => base.Heap.As<D3D12GraphicsMemoryHeap>();

    /// <inheritdoc cref="GraphicsDeviceObject.Service" />
    public new D3D12GraphicsService Service => base.Service.As<D3D12GraphicsService>();

    /// <inheritdoc />
    /// <exception cref="ExternalException">The call to <see cref="ID3D12Resource.Map(uint, D3D12_RANGE*, void**)" /> failed.</exception>
    public override T* Map<T>()
    {
        var readRange = default(D3D12_RANGE);

        byte* pDestination;
        ThrowExternalExceptionIfFailed(D3D12Resource->Map(Subresource: 0, &readRange, (void**)&pDestination));

        return (T*)pDestination;
    }

    /// <inheritdoc />
    /// <exception cref="ExternalException">The call to <see cref="ID3D12Resource.Map(uint, D3D12_RANGE*, void**)" /> failed.</exception>
    public override T* Map<T>(nuint rangeOffset, nuint rangeLength)
    {
        var readRange = default(D3D12_RANGE);

        byte* pDestination;
        ThrowExternalExceptionIfFailed(D3D12Resource->Map(Subresource: 0, &readRange, (void**)&pDestination));

        return (T*)(pDestination + rangeOffset);
    }

    /// <inheritdoc />
    /// <exception cref="ExternalException">The call to <see cref="ID3D12Resource.Map(uint, D3D12_RANGE*, void**)" /> failed.</exception>
    public override T* MapForRead<T>()
    {
        byte* pDestination;
        ThrowExternalExceptionIfFailed(D3D12Resource->Map(Subresource: 0, null, (void**)&pDestination));

        return (T*)pDestination;
    }

    /// <inheritdoc />
    /// <exception cref="ExternalException">The call to <see cref="ID3D12Resource.Map(uint, D3D12_RANGE*, void**)" /> failed.</exception>
    public override T* MapForRead<T>(nuint readRangeOffset, nuint readRangeLength)
    {
        var readRange = new D3D12_RANGE {
            Begin = readRangeOffset,
            End = readRangeOffset + readRangeLength,
        };

        byte* pDestination;
        ThrowExternalExceptionIfFailed(D3D12Resource->Map(Subresource: 0, &readRange, (void**)&pDestination));

        return (T*)(pDestination + readRange.Begin);
    }

    /// <inheritdoc />
    public override void Unmap()
    {
        var writtenRange = default(D3D12_RANGE);
        D3D12Resource->Unmap(Subresource: 0, &writtenRange);
    }

    /// <inheritdoc />
    public override void UnmapAndWrite() => D3D12Resource->Unmap(Subresource: 0, null);

    /// <inheritdoc />
    public override void UnmapAndWrite(nuint writtenRangeOffset, nuint writtenRangeLength)
    {
        var writtenRange = new D3D12_RANGE {
            Begin = writtenRangeOffset,
            End = writtenRangeOffset + writtenRangeLength,
        };
        D3D12Resource->Unmap(Subresource: 0, &writtenRange);
    }

    /// <inheritdoc />
    protected override void Dispose(bool isDisposing)
    {
        var priorState = _state.BeginDispose();

        if (priorState < Disposing)
        {
            ReleaseIfNotNull(_d3d12Resource);
            HeapRegion.Heap.Free(in HeapRegion);
        }

        _state.EndDispose();
    }
}
