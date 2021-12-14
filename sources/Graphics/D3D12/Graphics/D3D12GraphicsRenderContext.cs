// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Threading;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using TerraFX.Numerics;
using TerraFX.Threading;
using static TerraFX.Interop.DirectX.D3D_PRIMITIVE_TOPOLOGY;
using static TerraFX.Interop.DirectX.D3D12_COMMAND_LIST_TYPE;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_STATES;
using static TerraFX.Interop.DirectX.DXGI_FORMAT;
using static TerraFX.Interop.Windows.Windows;
using static TerraFX.Runtime.Configuration;
using static TerraFX.Threading.VolatileState;
using static TerraFX.Utilities.AssertionUtilities;
using static TerraFX.Utilities.D3D12Utilities;
using static TerraFX.Utilities.ExceptionUtilities;
using static TerraFX.Utilities.MemoryUtilities;
using static TerraFX.Utilities.UnsafeUtilities;

namespace TerraFX.Graphics;

/// <inheritdoc />
public sealed unsafe class D3D12GraphicsRenderContext : GraphicsRenderContext
{
    private readonly ID3D12CommandAllocator* _d3d12CommandAllocator;
    private readonly ID3D12GraphicsCommandList* _d3d12GraphicsCommandList;
    private readonly D3D12GraphicsFence _fence;

    private D3D12GraphicsRenderPass? _renderPass;

    private VolatileState _state;

    internal D3D12GraphicsRenderContext(D3D12GraphicsDevice device)
        : base(device)
    {
        var d3d12CommandAllocator = CreateD3D12CommandAllocator(device);
        _d3d12CommandAllocator = d3d12CommandAllocator;

        _d3d12GraphicsCommandList = CreateD3D12GraphicsCommandList(device, d3d12CommandAllocator);
        _fence = device.CreateFence(isSignalled: true);

        _ = _state.Transition(to: Initialized);

        static ID3D12CommandAllocator* CreateD3D12CommandAllocator(D3D12GraphicsDevice device)
        {
            ID3D12CommandAllocator* d3d12CommandAllocator;
            ThrowExternalExceptionIfFailed(device.D3D12Device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, __uuidof<ID3D12CommandAllocator>(), (void**)&d3d12CommandAllocator));
            return d3d12CommandAllocator;
        }

        static ID3D12GraphicsCommandList* CreateD3D12GraphicsCommandList(D3D12GraphicsDevice device, ID3D12CommandAllocator* d3d12CommandAllocator)
        {
            ID3D12GraphicsCommandList* d3d12GraphicsCommandList;
            ThrowExternalExceptionIfFailed(device.D3D12Device->CreateCommandList(nodeMask: 0, D3D12_COMMAND_LIST_TYPE_DIRECT, d3d12CommandAllocator, pInitialState: null, __uuidof<ID3D12GraphicsCommandList>(), (void**)&d3d12GraphicsCommandList));

            // Command lists are created in the recording state, but there is nothing
            // to record yet. The main loop expects it to be closed, so close it now.
            ThrowExternalExceptionIfFailed(d3d12GraphicsCommandList->Close());

            return d3d12GraphicsCommandList;
        }
    }

    /// <summary>Finalizes an instance of the <see cref="D3D12GraphicsRenderContext" /> class.</summary>
    ~D3D12GraphicsRenderContext() => Dispose(isDisposing: false);

    /// <inheritdoc cref="GraphicsDeviceObject.Adapter" />
    public new D3D12GraphicsAdapter Adapter => base.Adapter.As<D3D12GraphicsAdapter>();

    /// <summary>Gets the <see cref="ID3D12CommandAllocator" /> used by the context.</summary>
    public ID3D12CommandAllocator* D3D12CommandAllocator
    {
        get
        {
            AssertNotDisposedOrDisposing(_state);
            return _d3d12CommandAllocator;
        }
    }

    /// <summary>Gets the <see cref="ID3D12GraphicsCommandList" /> used by the context.</summary>
    public ID3D12GraphicsCommandList* D3D12GraphicsCommandList
    {
        get
        {
            AssertNotDisposedOrDisposing(_state);
            return _d3d12GraphicsCommandList;
        }
    }

    /// <inheritdoc cref="GraphicsDeviceObject.Device" />
    public new D3D12GraphicsDevice Device => base.Device.As<D3D12GraphicsDevice>();

    /// <inheritdoc />
    public override D3D12GraphicsFence Fence => _fence;

    /// <inheritdoc />
    public override D3D12GraphicsRenderPass? RenderPass => _renderPass;

    /// <inheritdoc cref="GraphicsDeviceObject.Service" />
    public new D3D12GraphicsService Service => base.Service.As<D3D12GraphicsService>();

    /// <inheritdoc />
    public override void BeginRenderPass(GraphicsRenderPass renderPass, ColorRgba renderTargetClearColor)
        => BeginRenderPass((D3D12GraphicsRenderPass)renderPass, renderTargetClearColor);

    /// <inheritdoc cref="BeginRenderPass(GraphicsRenderPass, ColorRgba)" />
    public void BeginRenderPass(D3D12GraphicsRenderPass renderPass, ColorRgba renderTargetClearColor)
    {
        ThrowIfNull(renderPass);

        if (Interlocked.CompareExchange(ref _renderPass, renderPass, null) is not null)
        {
            ThrowForInvalidState(nameof(RenderPass));
        }

        var d3d12GraphicsCommandList = D3D12GraphicsCommandList;
        var renderTarget = renderPass.Swapchain.RenderTarget;

        var d3d12RtvResourceBarrier = D3D12_RESOURCE_BARRIER.InitTransition(renderTarget.D3D12RtvResource, D3D12_RESOURCE_STATE_PRESENT, D3D12_RESOURCE_STATE_RENDER_TARGET);
        d3d12GraphicsCommandList->ResourceBarrier(1, &d3d12RtvResourceBarrier);

        var d3d12RtvDescriptorHandle = renderTarget.D3D12RtvDescriptorHandle;
        d3d12GraphicsCommandList->OMSetRenderTargets(1, &d3d12RtvDescriptorHandle, RTsSingleHandleToDescriptorRange: TRUE, pDepthStencilDescriptor: null);

        d3d12GraphicsCommandList->ClearRenderTargetView(d3d12RtvDescriptorHandle, (float*)&renderTargetClearColor, NumRects: 0, pRects: null);
        d3d12GraphicsCommandList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    }

    /// <inheritdoc />
    public override void Copy(GraphicsBuffer destination, GraphicsBuffer source)
        => Copy((D3D12GraphicsBuffer)destination, (D3D12GraphicsBuffer)source);

    /// <inheritdoc />
    public override void Copy(GraphicsTexture destination, GraphicsBuffer source)
        => Copy((D3D12GraphicsTexture)destination, (D3D12GraphicsBuffer)source);

    /// <inheritdoc cref="Copy(GraphicsBuffer, GraphicsBuffer)" />
    public void Copy(D3D12GraphicsBuffer destination, D3D12GraphicsBuffer source)
    {
        ThrowIfNull(destination);
        ThrowIfNull(source);

        var d3d12GraphicsCommandList = D3D12GraphicsCommandList;

        var destinationCpuAccess = destination.CpuAccess;
        var sourceCpuAccess = source.CpuAccess;

        var d3d12DestinationResource = destination.D3D12Resource;
        var d3d12SourceResource = source.D3D12Resource;

        var d3d12DestinationResourceState = destination.D3D12ResourceState;
        var d3d12SourceResourceState = source.D3D12ResourceState;

        BeginCopy();

        d3d12GraphicsCommandList->CopyResource(d3d12DestinationResource, d3d12SourceResource);

        EndCopy();

        void BeginCopy()
        {
            var d3d12ResourceBarriers = stackalloc D3D12_RESOURCE_BARRIER[2];
            var numD3D12ResourceBarriers = 0u;

            if (destinationCpuAccess == GraphicsResourceCpuAccess.None)
            {
                d3d12ResourceBarriers[numD3D12ResourceBarriers] = D3D12_RESOURCE_BARRIER.InitTransition(
                    d3d12DestinationResource,
                    stateBefore: d3d12DestinationResourceState,
                    stateAfter: D3D12_RESOURCE_STATE_COPY_DEST
                );
                numD3D12ResourceBarriers++;
            }

            if (sourceCpuAccess == GraphicsResourceCpuAccess.None)
            {
                d3d12ResourceBarriers[numD3D12ResourceBarriers] = D3D12_RESOURCE_BARRIER.InitTransition(
                    d3d12SourceResource,
                    stateBefore: d3d12SourceResourceState,
                    stateAfter: D3D12_RESOURCE_STATE_COPY_SOURCE
                );
                numD3D12ResourceBarriers++;
            }

            if (numD3D12ResourceBarriers != 0)
            {
                d3d12GraphicsCommandList->ResourceBarrier(numD3D12ResourceBarriers, d3d12ResourceBarriers);
            }
        }

        void EndCopy()
        {
            var d3d12ResourceBarriers = stackalloc D3D12_RESOURCE_BARRIER[2];
            var numD3D12ResourceBarriers = 0u;

            if (sourceCpuAccess == GraphicsResourceCpuAccess.None)
            {
                d3d12ResourceBarriers[numD3D12ResourceBarriers] = D3D12_RESOURCE_BARRIER.InitTransition(
                    d3d12SourceResource,
                    stateBefore: D3D12_RESOURCE_STATE_COPY_SOURCE,
                    stateAfter: d3d12SourceResourceState
                );
                numD3D12ResourceBarriers++;
            }

            if (destinationCpuAccess == GraphicsResourceCpuAccess.None)
            {
                d3d12ResourceBarriers[numD3D12ResourceBarriers] = D3D12_RESOURCE_BARRIER.InitTransition(
                    d3d12DestinationResource,
                    stateBefore: D3D12_RESOURCE_STATE_COPY_DEST,
                    stateAfter: d3d12DestinationResourceState
                );
                numD3D12ResourceBarriers++;
            }

            if (numD3D12ResourceBarriers != 0)
            {
                d3d12GraphicsCommandList->ResourceBarrier(numD3D12ResourceBarriers, d3d12ResourceBarriers);
            }
        }
    }

    /// <inheritdoc cref="Copy(GraphicsTexture, GraphicsBuffer)" />
    public void Copy(D3D12GraphicsTexture destination, D3D12GraphicsBuffer source)
    {
        ThrowIfNull(destination);
        ThrowIfNull(source);

        var d3d12Device = Device.D3D12Device;
        var d3d12GraphicsCommandList = D3D12GraphicsCommandList;

        var destinationCpuAccess = destination.CpuAccess;
        var sourceCpuAccess = source.CpuAccess;

        var d3d12DestinationResource = destination.D3D12Resource;
        var d3d12SourceResource = source.D3D12Resource;

        var d3d12DestinationResourceState = destination.D3D12ResourceState;
        var d3d12SourceResourceState = source.D3D12ResourceState;

        BeginCopy();

        D3D12_PLACED_SUBRESOURCE_FOOTPRINT sourceFootprint;

        var d3d12DestinationResourceDesc = d3d12DestinationResource->GetDesc();
        d3d12Device->GetCopyableFootprints(&d3d12DestinationResourceDesc, FirstSubresource: 0, NumSubresources: 1, BaseOffset: 0, &sourceFootprint, pNumRows: null, pRowSizeInBytes: null, pTotalBytes: null);

        var d3d12DestinationTextureCopyLocation = new D3D12_TEXTURE_COPY_LOCATION(d3d12DestinationResource, Sub: 0);
        var d3d12SourceTextureCopyLocation = new D3D12_TEXTURE_COPY_LOCATION(d3d12SourceResource, in sourceFootprint);

        d3d12GraphicsCommandList->CopyTextureRegion(&d3d12DestinationTextureCopyLocation, DstX: 0, DstY: 0, DstZ: 0, &d3d12SourceTextureCopyLocation, pSrcBox: null);

        EndCopy();

        void BeginCopy()
        {
            var d3d12ResourceBarriers = stackalloc D3D12_RESOURCE_BARRIER[2];
            var numD3D12ResourceBarriers = 0u;

            if (destinationCpuAccess == GraphicsResourceCpuAccess.None)
            {
                d3d12ResourceBarriers[numD3D12ResourceBarriers] = D3D12_RESOURCE_BARRIER.InitTransition(
                    d3d12DestinationResource,
                    stateBefore: d3d12DestinationResourceState,
                    stateAfter: D3D12_RESOURCE_STATE_COPY_DEST
                );
                numD3D12ResourceBarriers++;
            }

            if (sourceCpuAccess == GraphicsResourceCpuAccess.None)
            {
                d3d12ResourceBarriers[numD3D12ResourceBarriers] = D3D12_RESOURCE_BARRIER.InitTransition(
                    d3d12SourceResource,
                    stateBefore: d3d12SourceResourceState,
                    stateAfter: D3D12_RESOURCE_STATE_COPY_SOURCE
                );
                numD3D12ResourceBarriers++;
            }

            if (numD3D12ResourceBarriers != 0)
            {
                d3d12GraphicsCommandList->ResourceBarrier(numD3D12ResourceBarriers, d3d12ResourceBarriers);
            }
        }

        void EndCopy()
        {
            var d3d12ResourceBarriers = stackalloc D3D12_RESOURCE_BARRIER[2];
            var numD3D12ResourceBarriers = 0u;

            if (sourceCpuAccess == GraphicsResourceCpuAccess.None)
            {
                d3d12ResourceBarriers[numD3D12ResourceBarriers] = D3D12_RESOURCE_BARRIER.InitTransition(
                    d3d12SourceResource,
                    stateBefore: D3D12_RESOURCE_STATE_COPY_SOURCE,
                    stateAfter: d3d12SourceResourceState
                );
                numD3D12ResourceBarriers++;
            }

            if (destinationCpuAccess == GraphicsResourceCpuAccess.None)
            {
                d3d12ResourceBarriers[numD3D12ResourceBarriers] = D3D12_RESOURCE_BARRIER.InitTransition(
                    d3d12DestinationResource,
                    stateBefore: D3D12_RESOURCE_STATE_COPY_DEST,
                    stateAfter: d3d12DestinationResourceState
                );
                numD3D12ResourceBarriers++;
            }

            if (numD3D12ResourceBarriers != 0)
            {
                d3d12GraphicsCommandList->ResourceBarrier(numD3D12ResourceBarriers, d3d12ResourceBarriers);
            }
        }
    }

    /// <inheritdoc />
    public override void Draw(GraphicsPrimitive primitive)
        => Draw((D3D12GraphicsPrimitive)primitive);

    /// <inheritdoc cref="Draw(GraphicsPrimitive)" />
    public void Draw(D3D12GraphicsPrimitive primitive)
    {
        ThrowIfNull(primitive);

        var renderPass = RenderPass;

        if (renderPass is null)
        {
            ThrowForInvalidState(nameof(RenderPass));
        }

        var d3d12GraphicsCommandList = D3D12GraphicsCommandList;
        var pipeline = primitive.Pipeline;

        d3d12GraphicsCommandList->SetGraphicsRootSignature(pipeline.Signature.D3D12RootSignature);
        d3d12GraphicsCommandList->SetPipelineState(pipeline.D3D12PipelineState);

        var d3d12DescriptorHeaps = stackalloc ID3D12DescriptorHeap*[1] {
            primitive.D3D12CbvSrvUavDescriptorHeap,
        };
        d3d12GraphicsCommandList->SetDescriptorHeaps(1, d3d12DescriptorHeaps);

        ref readonly var vertexBufferView = ref primitive.VertexBufferView;
        var vertexBuffer = vertexBufferView.Resource.As<D3D12GraphicsBuffer>();
        AssertNotNull(vertexBuffer);

        var d3d12VertexBufferView = new D3D12_VERTEX_BUFFER_VIEW {
            BufferLocation = vertexBuffer.D3D12Resource->GetGPUVirtualAddress() + vertexBufferView.Offset,
            StrideInBytes = primitive.VertexBufferView.Stride,
            SizeInBytes = vertexBufferView.Size,
        };
        d3d12GraphicsCommandList->IASetVertexBuffers(StartSlot: 0, NumViews: 1, &d3d12VertexBufferView);

        var inputResourceViews = primitive.InputResourceViews;

        var rootDescriptorTableIndex = 0;
        var cbvSrvUavDescriptorHandleIncrementSize = Device.D3D12CbvSrvUavDescriptorHandleIncrementSize;

        for (var index = 0; index < inputResourceViews.Length; index++)
        {
            ref readonly var inputResourceView = ref inputResourceViews[index];

            if (inputResourceView.Resource is D3D12GraphicsBuffer d3d12GraphicsBuffer)
            {
                var gpuVirtualAddress = d3d12GraphicsBuffer.D3D12Resource->GetGPUVirtualAddress();
                d3d12GraphicsCommandList->SetGraphicsRootConstantBufferView(unchecked((uint)index), gpuVirtualAddress + inputResourceView.Offset);
            }
            else if (inputResourceView.Resource is D3D12GraphicsTexture d3d12GraphicsTexture)
            {
                var gpuDescriptorHandleForHeapStart = primitive.D3D12CbvSrvUavDescriptorHeap->GetGPUDescriptorHandleForHeapStart();
                d3d12GraphicsCommandList->SetGraphicsRootDescriptorTable(unchecked((uint)index), gpuDescriptorHandleForHeapStart.Offset(rootDescriptorTableIndex, cbvSrvUavDescriptorHandleIncrementSize));
                rootDescriptorTableIndex++;
            }
        }

        ref readonly var indexBufferView = ref primitive.IndexBufferView;

        if (indexBufferView.Resource is D3D12GraphicsBuffer indexBuffer)
        {
            var indexBufferStride = indexBufferView.Stride;
            var indexFormat = DXGI_FORMAT_R16_UINT;

            if (indexBufferStride != 2)
            {
                Assert(AssertionsEnabled && (indexBufferStride == 4));
                indexFormat = DXGI_FORMAT_R32_UINT;
            }

            var d3d12IndexBufferView = new D3D12_INDEX_BUFFER_VIEW {
                BufferLocation = indexBuffer.D3D12Resource->GetGPUVirtualAddress() + indexBufferView.Offset,
                SizeInBytes = (uint)indexBufferView.Size,
                Format = indexFormat,
            };
            d3d12GraphicsCommandList->IASetIndexBuffer(&d3d12IndexBufferView);

            d3d12GraphicsCommandList->DrawIndexedInstanced(IndexCountPerInstance: (uint)(indexBufferView.Size / indexBufferStride), InstanceCount: 1, StartIndexLocation: 0, BaseVertexLocation: 0, StartInstanceLocation: 0);
        }
        else
        {
            d3d12GraphicsCommandList->DrawInstanced(VertexCountPerInstance: (uint)(vertexBufferView.Size /  vertexBufferView.Stride), InstanceCount: 1, StartVertexLocation: 0, StartInstanceLocation: 0);
        }
    }

    /// <inheritdoc />
    public override void EndRenderPass()
    {
        var renderPass = Interlocked.Exchange(ref _renderPass, null);

        if (renderPass is null)
        {
            ThrowForInvalidState(nameof(RenderPass));
        }

        var d3d12RtvResourceBarrier = D3D12_RESOURCE_BARRIER.InitTransition(renderPass.Swapchain.RenderTarget.D3D12RtvResource, D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_STATE_PRESENT);
        D3D12GraphicsCommandList->ResourceBarrier(1, &d3d12RtvResourceBarrier);
    }

    /// <inheritdoc />
    public override void Flush()
    {
        var d3d12GraphicsCommandList = D3D12GraphicsCommandList;

        var d3d12CommandQueue = Device.D3D12CommandQueue;
        ThrowExternalExceptionIfFailed(d3d12GraphicsCommandList->Close());
        d3d12CommandQueue->ExecuteCommandLists(1, (ID3D12CommandList**)&d3d12GraphicsCommandList);

        var fence = Fence;
        ThrowExternalExceptionIfFailed(d3d12CommandQueue->Signal(fence.D3D12Fence, fence.D3D12FenceSignalValue));
        fence.Wait();
    }

    /// <inheritdoc />
    public override void Reset()
    {
        Fence.Reset();

        var d3d12CommandAllocator = D3D12CommandAllocator;

        ThrowExternalExceptionIfFailed(d3d12CommandAllocator->Reset());
        ThrowExternalExceptionIfFailed(D3D12GraphicsCommandList->Reset(d3d12CommandAllocator, pInitialState: null));
    }

    /// <inheritdoc />
    public override void SetScissor(BoundingRectangle scissor)
    {
        var topLeft = scissor.Location;
        var bottomRight = topLeft + scissor.Size;

        var d3d12Rect = new RECT {
            left = (int)topLeft.X,
            top = (int)topLeft.Y,
            right = (int)bottomRight.X,
            bottom = (int)bottomRight.Y,
        };
        D3D12GraphicsCommandList->RSSetScissorRects(NumRects: 1, &d3d12Rect);
    }

    /// <inheritdoc />
    public override void SetScissors(ReadOnlySpan<BoundingRectangle> scissors)
    {
        var count = (uint)scissors.Length;
        var d3d12Rects = AllocateArray<RECT>(count);

        for (var i = 0u; i < count; i++)
        {
            ref readonly var scissor = ref scissors[(int)i];

            var upperLeft = scissor.Location;
            var bottomRight = upperLeft + scissor.Size;

            d3d12Rects[i] = new RECT {
                left = (int)upperLeft.X,
                top = (int)upperLeft.Y,
                right = (int)bottomRight.X,
                bottom = (int)bottomRight.Y,
            };
        }
        D3D12GraphicsCommandList->RSSetScissorRects(count, d3d12Rects);
    }

    /// <inheritdoc />
    public override void SetViewport(BoundingBox viewport)
    {
        var location = viewport.Location;
        var size = viewport.Size;

        var d3d12Viewport = new D3D12_VIEWPORT {
            TopLeftX = location.X,
            TopLeftY = location.Y,
            Width = size.X,
            Height = size.Y,
            MinDepth = location.Z,
            MaxDepth = size.Z,
        };
        D3D12GraphicsCommandList->RSSetViewports(NumViewports: 1, &d3d12Viewport);
    }

    /// <inheritdoc />
    public override void SetViewports(ReadOnlySpan<BoundingBox> viewports)
    {
        var count = (uint)viewports.Length;
        var d3d12Viewports = AllocateArray<D3D12_VIEWPORT>(count);

        for (var i = 0u; i < count; i++)
        {
            ref readonly var viewport = ref viewports[(int)i];

            var location = viewport.Location;
            var size = viewport.Size;

            d3d12Viewports[i] = new D3D12_VIEWPORT {
                TopLeftX = location.X,
                TopLeftY = location.Y,
                Width = size.X,
                Height = size.Y,
                MinDepth = location.Z,
                MaxDepth = size.Z,
            };
        }
        D3D12GraphicsCommandList->RSSetViewports(count, d3d12Viewports);
    }

    /// <inheritdoc />
    protected override void Dispose(bool isDisposing)
    {
        var priorState = _state.BeginDispose();

        if (priorState < Disposing)
        {
            ReleaseIfNotNull(_d3d12GraphicsCommandList);
            ReleaseIfNotNull(_d3d12CommandAllocator);

            if (isDisposing)
            {
                _fence?.Dispose();
            }
        }

        _state.EndDispose();
    }
}