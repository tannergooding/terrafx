// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using TerraFX.Graphics;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using D3D12Api = TerraFX.Interop.DirectX.D3D12;

namespace TerraFX.Graphics.D3D12;

/// <summary>Provides the explicitly selected Direct3D 12 graphics backend.</summary>
public static unsafe class D3D12GraphicsBackend
{
    /// <summary>Creates immutable factory metadata without creating a native device.</summary>
    public static GraphicsBackendRegistration CreateRegistration()
        => new(new D3D12BackendFactory());

    private sealed unsafe class D3D12BackendFactory : IGraphicsBackendFactory
    {
        private const int DxgiErrorDeviceHung = unchecked((int)0x887A0006);
        private const int DxgiErrorDeviceRemoved = unchecked((int)0x887A0005);
        private const int DxgiErrorDeviceReset = unchecked((int)0x887A0007);
        private const int DxgiErrorDriverInternalError = unchecked((int)0x887A0020);
        private const int DxgiErrorNotFound = unchecked((int)0x887A0002);

        internal D3D12BackendFactory()
        {
        }

        public GraphicsBackend Backend => GraphicsBackend.D3D12;

        private static GraphicsCapabilities CreateCapabilities(
            IDXGIAdapter1* adapter,
            ID3D12Device* device,
            GraphicsDeviceProfile profile)
        {
            var description = default(DXGI_ADAPTER_DESC1);
            ThrowIfFailed(adapter->GetDesc1(&description));

            Span<byte> nativeIdentifier = stackalloc byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(nativeIdentifier, description.AdapterLuid.LowPart);
            BinaryPrimitives.WriteUInt32LittleEndian(nativeIdentifier[sizeof(uint)..], unchecked((uint)description.AdapterLuid.HighPart));

            var hardwareFeatures = ImmutableArray.CreateBuilder<GraphicsFeature>(3);
            var implementedFeatures = ImmutableArray.CreateBuilder<GraphicsFeature>(2);

            var featureLevel = GetFeatureLevel(device);
            var shaderModel = GetShaderModel(device);

            if (featureLevel >= D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_12_0)
            {
                hardwareFeatures.Add(GraphicsFeature.Direct3DFeatureLevel12);
                implementedFeatures.Add(GraphicsFeature.Direct3DFeatureLevel12);
            }

            if ((uint)shaderModel >= (uint)D3D_SHADER_MODEL.D3D_SHADER_MODEL_6_0)
            {
                hardwareFeatures.Add(GraphicsFeature.ShaderModel6);
                implementedFeatures.Add(GraphicsFeature.ShaderModel6);
            }

            hardwareFeatures.Add(GraphicsFeature.SamplerAnisotropy);

            var addressSupport = default(D3D12_FEATURE_DATA_GPU_VIRTUAL_ADDRESS_SUPPORT);
            ThrowIfFailed(device->CheckFeatureSupport(
                D3D12_FEATURE.D3D12_FEATURE_GPU_VIRTUAL_ADDRESS_SUPPORT,
                &addressSupport,
                (uint)sizeof(D3D12_FEATURE_DATA_GPU_VIRTUAL_ADDRESS_SUPPORT)));

            if (addressSupport.MaxGPUVirtualAddressBitsPerResource is 0 or >= 63)
            {
                throw new NotSupportedException("The D3D12 adapter reported an unsupported per-resource GPU virtual address width.");
            }

            var limits = new GraphicsLimits(
                maximumTextureDimension2D: (int)D3D12Api.D3D12_REQ_TEXTURE2D_U_OR_V_DIMENSION,
                maximumTextureDimension3D: (int)D3D12Api.D3D12_REQ_TEXTURE3D_U_V_OR_W_DIMENSION,
                maximumArrayLayers: (int)D3D12Api.D3D12_REQ_TEXTURE2D_ARRAY_AXIS_DIMENSION,
                maximumColorAttachments: (int)D3D12Api.D3D12_SIMULTANEOUS_RENDER_TARGET_COUNT,
                maximumSampledImagesPerStage: (int)D3D12Api.D3D12_COMMONSHADER_INPUT_RESOURCE_SLOT_COUNT,
                maximumSamplersPerStage: (int)D3D12Api.D3D12_COMMONSHADER_SAMPLER_SLOT_COUNT,
                maximumSamplerAnisotropy: 1.0f,
                maximumUniformBuffersPerStage: (int)D3D12Api.D3D12_COMMONSHADER_CONSTANT_BUFFER_API_SLOT_COUNT,
                maximumUniformBufferRangeBytes: 65536,
                uniformBufferOffsetAlignment: D3D12Api.D3D12_CONSTANT_BUFFER_DATA_PLACEMENT_ALIGNMENT,
                storageBufferOffsetAlignment: D3D12Api.D3D12_RAW_UAV_SRV_BYTE_ALIGNMENT,
                bufferCopyOffsetAlignment: sizeof(uint),
                textureRowPitchAlignment: D3D12Api.D3D12_TEXTURE_DATA_PITCH_ALIGNMENT,
                texturePlacementAlignment: D3D12Api.D3D12_TEXTURE_DATA_PLACEMENT_ALIGNMENT,
                maximumComputeWorkGroupSizeX: 1024,
                maximumComputeWorkGroupSizeY: 1024,
                maximumComputeWorkGroupSizeZ: 64,
                maximumComputeWorkGroupInvocations: 1024,
                maximumVertexInputAttributes: 32,
                maximumVertexInputBindings: 32,
                maximumBufferByteLength: 1L << (int)addressSupport.MaxGPUVirtualAddressBitsPerResource);

            var heapCapabilities = GetMemoryHeaps(adapter, &description);

            return new GraphicsCapabilities(
                GraphicsBackend.D3D12,
                profile,
                new GraphicsAdapterId(GraphicsBackend.D3D12, nativeIdentifier),
                description.VendorId,
                description.DeviceId,
                GetAdapterName(&description),
                "D3D12",
                GetDriverVersion(adapter),
                new GraphicsApiVersion(12, 0, 0, 0),
                hardwareFeatures.ToImmutable(),
                implementedFeatures.ToImmutable(),
                limits,
                GetFormatCapabilities(device),
                heapCapabilities);
        }

        private static D3D12_RESOURCE_DESC CreateNativeBufferDescription(BufferDescription description)
        {
            ArgumentNullException.ThrowIfNull(description);

            var flags = (description.Usage & BufferUsage.Storage) != 0
                ? D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS
                : D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_NONE;

            return new D3D12_RESOURCE_DESC {
                Dimension = D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_BUFFER,
                Alignment = 0,
                Width = checked((ulong)description.ByteLength),
                Height = 1,
                DepthOrArraySize = 1,
                MipLevels = 1,
                Format = DXGI_FORMAT.DXGI_FORMAT_UNKNOWN,
                SampleDesc = new DXGI_SAMPLE_DESC {
                    Count = 1,
                    Quality = 0,
                },
                Layout = D3D12_TEXTURE_LAYOUT.D3D12_TEXTURE_LAYOUT_ROW_MAJOR,
                Flags = flags,
            };
        }

        private static string GetAdapterName(DXGI_ADAPTER_DESC1* description)
        {
            var name = (char*)&description->Description;
            var length = 0;

            while ((length < 128) && (name[length] != '\0'))
            {
                length++;
            }

            var adapterName = new string(name, 0, length);

            if (string.IsNullOrWhiteSpace(adapterName))
            {
                throw new InvalidOperationException("The D3D12 adapter did not report a nonempty name.");
            }

            return adapterName;
        }

        private static long? GetBudget(IDXGIAdapter3* adapter, DXGI_MEMORY_SEGMENT_GROUP segmentGroup)
        {
            if (adapter is null)
            {
                return null;
            }

            var memoryInfo = default(DXGI_QUERY_VIDEO_MEMORY_INFO);
            var result = adapter->QueryVideoMemoryInfo(0, segmentGroup, &memoryInfo);
            ThrowIfFailed(result);
            return checked((long)memoryInfo.Budget);
        }

        private static int GetBufferHeapIndex(GpuMemoryKind memoryKind)
            => memoryKind switch {
                GpuMemoryKind.DeviceLocal => (int)DXGI_MEMORY_SEGMENT_GROUP.DXGI_MEMORY_SEGMENT_GROUP_LOCAL,
                GpuMemoryKind.Upload or GpuMemoryKind.Readback => (int)DXGI_MEMORY_SEGMENT_GROUP.DXGI_MEMORY_SEGMENT_GROUP_NON_LOCAL,
                _ => throw new ArgumentOutOfRangeException(nameof(memoryKind)),
            };

        private static string GetDriverVersion(IDXGIAdapter1* adapter)
        {
            var deviceId = typeof(ID3D12Device).GUID;
            var driverVersion = default(LARGE_INTEGER);
            ThrowIfFailed(adapter->CheckInterfaceSupport(&deviceId, &driverVersion));
            return unchecked((ulong)driverVersion.QuadPart).ToString("X16", CultureInfo.InvariantCulture);
        }

        private static D3D_FEATURE_LEVEL GetFeatureLevel(ID3D12Device* device)
        {
            var requestedLevels = stackalloc D3D_FEATURE_LEVEL[] {
            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_12_1,
            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_12_0,
        };
            var featureLevels = new D3D12_FEATURE_DATA_FEATURE_LEVELS {
                NumFeatureLevels = 2,
                pFeatureLevelsRequested = requestedLevels,
            };

            ThrowIfFailed(device->CheckFeatureSupport(
                D3D12_FEATURE.D3D12_FEATURE_FEATURE_LEVELS,
                &featureLevels,
                (uint)sizeof(D3D12_FEATURE_DATA_FEATURE_LEVELS)));
            return featureLevels.MaxSupportedFeatureLevel;
        }

        private static ImmutableArray<GraphicsFormatCapabilities> GetFormatCapabilities(ID3D12Device* device)
        {
            var formats = ImmutableArray.CreateBuilder<GraphicsFormatCapabilities>();

            for (var value = (int)TextureFormat.R8Unorm; value <= (int)TextureFormat.D32Float; value++)
            {
                var format = (TextureFormat)value;
                var nativeFormat = GetNativeFormat(format);
                var formatSupport = new D3D12_FEATURE_DATA_FORMAT_SUPPORT {
                    Format = nativeFormat,
                };

                var result = device->CheckFeatureSupport(
                    D3D12_FEATURE.D3D12_FEATURE_FORMAT_SUPPORT,
                    &formatSupport,
                    (uint)sizeof(D3D12_FEATURE_DATA_FORMAT_SUPPORT));

                ThrowIfFailed(result);

                if ((formatSupport.Support1 & D3D12_FORMAT_SUPPORT1.D3D12_FORMAT_SUPPORT1_TEXTURE2D) == 0)
                {
                    continue;
                }

                var usages = TextureUsage.CopySource | TextureUsage.CopyDestination;

                if ((formatSupport.Support1 & D3D12_FORMAT_SUPPORT1.D3D12_FORMAT_SUPPORT1_SHADER_SAMPLE) != 0)
                {
                    usages |= TextureUsage.Sampled;
                }

                if ((formatSupport.Support1 & D3D12_FORMAT_SUPPORT1.D3D12_FORMAT_SUPPORT1_TYPED_UNORDERED_ACCESS_VIEW) != 0)
                {
                    usages |= TextureUsage.Storage;
                }

                if ((formatSupport.Support1 & D3D12_FORMAT_SUPPORT1.D3D12_FORMAT_SUPPORT1_RENDER_TARGET) != 0)
                {
                    usages |= TextureUsage.ColorAttachment;
                }

                if ((formatSupport.Support1 & D3D12_FORMAT_SUPPORT1.D3D12_FORMAT_SUPPORT1_DEPTH_STENCIL) != 0)
                {
                    usages |= TextureUsage.DepthAttachment;
                }

                var sampleCounts = GetSampleCounts(device, nativeFormat);

                if (!sampleCounts.IsDefaultOrEmpty)
                {
                    formats.Add(new GraphicsFormatCapabilities(format, usages, sampleCounts));
                }
            }

            return formats.ToImmutable();
        }

        private static D3D12_HEAP_PROPERTIES GetHeapProperties(GpuMemoryKind memoryKind)
        {
            var heapType = memoryKind switch {
                GpuMemoryKind.DeviceLocal => D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_DEFAULT,
                GpuMemoryKind.Upload => D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_UPLOAD,
                GpuMemoryKind.Readback => D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_READBACK,
                _ => throw new ArgumentOutOfRangeException(nameof(memoryKind)),
            };

            return new D3D12_HEAP_PROPERTIES {
                Type = heapType,
                CPUPageProperty = D3D12_CPU_PAGE_PROPERTY.D3D12_CPU_PAGE_PROPERTY_UNKNOWN,
                MemoryPoolPreference = D3D12_MEMORY_POOL.D3D12_MEMORY_POOL_UNKNOWN,
                CreationNodeMask = 1,
                VisibleNodeMask = 1,
            };
        }

        private static D3D12_RESOURCE_STATES GetInitialResourceState(GpuMemoryKind memoryKind)
            => memoryKind switch {
                GpuMemoryKind.DeviceLocal => D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON,
                GpuMemoryKind.Upload => D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ,
                GpuMemoryKind.Readback => D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST,
                _ => throw new ArgumentOutOfRangeException(nameof(memoryKind)),
            };

        private static ImmutableArray<GraphicsMemoryHeapCapabilities> GetMemoryHeaps(
            IDXGIAdapter1* adapter,
            DXGI_ADAPTER_DESC1* description)
        {
            IDXGIAdapter3* adapter3 = null;
            var adapter3Id = typeof(IDXGIAdapter3).GUID;
            var queryResult = adapter->QueryInterface(&adapter3Id, (void**)&adapter3);

            if (queryResult.FAILED)
            {
                adapter3 = null;
            }

            try
            {
                var localSize = checked(description->DedicatedVideoMemory.ToUInt64() + description->DedicatedSystemMemory.ToUInt64());
                var sharedSize = description->SharedSystemMemory.ToUInt64();

                if ((localSize == 0) && (sharedSize > 0))
                {
                    localSize = sharedSize;
                }

                var heaps = ImmutableArray.CreateBuilder<GraphicsMemoryHeapCapabilities>(2);

                if (localSize > 0)
                {
                    heaps.Add(new GraphicsMemoryHeapCapabilities(
                        (int)DXGI_MEMORY_SEGMENT_GROUP.DXGI_MEMORY_SEGMENT_GROUP_LOCAL,
                        GraphicsMemoryHeapProperties.DeviceLocal,
                        checked((long)localSize),
                        GetBudget(adapter3, DXGI_MEMORY_SEGMENT_GROUP.DXGI_MEMORY_SEGMENT_GROUP_LOCAL)));
                }

                if (sharedSize > 0)
                {
                    heaps.Add(new GraphicsMemoryHeapCapabilities(
                        (int)DXGI_MEMORY_SEGMENT_GROUP.DXGI_MEMORY_SEGMENT_GROUP_NON_LOCAL,
                        GraphicsMemoryHeapProperties.HostVisible,
                        checked((long)sharedSize),
                        GetBudget(adapter3, DXGI_MEMORY_SEGMENT_GROUP.DXGI_MEMORY_SEGMENT_GROUP_NON_LOCAL)));
                }

                return heaps.ToImmutable();
            }
            finally
            {
                if (adapter3 is not null)
                {
                    _ = adapter3->Release();
                }
            }
        }

        private static DXGI_FORMAT GetNativeFormat(TextureFormat format)
            => format switch {
                TextureFormat.R8Unorm => DXGI_FORMAT.DXGI_FORMAT_R8_UNORM,
                TextureFormat.Rgba8Unorm => DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM,
                TextureFormat.Rgba8Srgb => DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM_SRGB,
                TextureFormat.Bgra8Unorm => DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                TextureFormat.Bgra8Srgb => DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM_SRGB,
                TextureFormat.Rgba16Float => DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT,
                TextureFormat.R32Float => DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT,
                TextureFormat.R32UInt => DXGI_FORMAT.DXGI_FORMAT_R32_UINT,
                TextureFormat.D32Float => DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT,
                _ => throw new ArgumentOutOfRangeException(nameof(format)),
            };

        private static ImmutableArray<int> GetSampleCounts(ID3D12Device* device, DXGI_FORMAT format)
        {
            var sampleCounts = ImmutableArray.CreateBuilder<int>();

            for (uint sampleCount = 1; sampleCount <= 64; sampleCount *= 2)
            {
                var support = new D3D12_FEATURE_DATA_MULTISAMPLE_QUALITY_LEVELS {
                    Format = format,
                    SampleCount = sampleCount,
                };

                var result = device->CheckFeatureSupport(
                    D3D12_FEATURE.D3D12_FEATURE_MULTISAMPLE_QUALITY_LEVELS,
                    &support,
                    (uint)sizeof(D3D12_FEATURE_DATA_MULTISAMPLE_QUALITY_LEVELS));

                ThrowIfFailed(result);

                if (support.NumQualityLevels > 0)
                {
                    sampleCounts.Add((int)sampleCount);
                }
            }

            return sampleCounts.ToImmutable();
        }

        private static D3D_SHADER_MODEL GetShaderModel(ID3D12Device* device)
        {
            var shaderModel = new D3D12_FEATURE_DATA_SHADER_MODEL {
                HighestShaderModel = D3D_SHADER_MODEL.D3D_SHADER_MODEL_6_0,
            };
            var result = device->CheckFeatureSupport(
                D3D12_FEATURE.D3D12_FEATURE_SHADER_MODEL,
                &shaderModel,
                (uint)sizeof(D3D12_FEATURE_DATA_SHADER_MODEL));
            ThrowIfFailed(result);
            return shaderModel.HighestShaderModel;
        }

        private static void ThrowIfFailed(HRESULT result)
        {
            if (result.FAILED)
            {
                if (result.Value is DxgiErrorDeviceHung or DxgiErrorDeviceRemoved or DxgiErrorDeviceReset or DxgiErrorDriverInternalError)
                {
                    throw new GraphicsDeviceLostException(result.Value);
                }

                Marshal.ThrowExceptionForHR(result.Value);
            }
        }

        public IGraphicsBackendDevice Create(
            uint ownerId,
            GraphicsDeviceProfile profile,
            GraphicsPlatformKind platformKind,
            CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfZero(ownerId);
            cancellationToken.ThrowIfCancellationRequested();

            IDXGIFactory4* factory = null;

            try
            {
                var factoryId = typeof(IDXGIFactory4).GUID;
                ThrowIfFailed(DirectX.CreateDXGIFactory2(0, &factoryId, (void**)&factory));
                var failures = new List<string>();

                for (uint index = 0; ; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    IDXGIAdapter1* adapter = null;
                    var enumerationResult = factory->EnumAdapters1(index, &adapter);

                    if (enumerationResult.Value == DxgiErrorNotFound)
                    {
                        break;
                    }

                    ThrowIfFailed(enumerationResult);

                    try
                    {
                        var description = default(DXGI_ADAPTER_DESC1);
                        ThrowIfFailed(adapter->GetDesc1(&description));

                        if ((description.Flags & (uint)DXGI_ADAPTER_FLAG.DXGI_ADAPTER_FLAG_SOFTWARE) != 0)
                        {
                            continue;
                        }

                        ID3D12Device* device = null;
                        IDXGIAdapter3* adapter3 = null;

                        try
                        {
                            var deviceId = typeof(ID3D12Device).GUID;
                            var createResult = DirectX.D3D12CreateDevice(
                                (IUnknown*)adapter,
                                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_12_0,
                                &deviceId,
                                (void**)&device);

                            if (createResult.FAILED)
                            {
                                failures.Add($"{GetAdapterName(&description)}: D3D12CreateDevice failed with HRESULT 0x{createResult.Value:X8}.");
                                continue;
                            }

                            var adapter3Id = typeof(IDXGIAdapter3).GUID;

                            if (adapter->QueryInterface(&adapter3Id, (void**)&adapter3).FAILED)
                            {
                                adapter3 = null;
                            }

                            cancellationToken.ThrowIfCancellationRequested();
                            var capabilities = CreateCapabilities(adapter, device, profile);
                            var missingRequirements = GraphicsCapabilityAdmission.GetMissingRequirements(capabilities, profile);

                            if (!missingRequirements.IsEmpty)
                            {
                                failures.Add($"{capabilities.AdapterName}: {string.Join("; ", missingRequirements)}");
                                continue;
                            }

                            var backendDevice = new D3D12BackendDevice(device, adapter3, capabilities);
                            device = null;
                            adapter3 = null;
                            return backendDevice;
                        }
                        finally
                        {
                            if (adapter3 is not null)
                            {
                                _ = adapter3->Release();
                            }

                            if (device is not null)
                            {
                                _ = device->Release();
                            }
                        }
                    }
                    finally
                    {
                        if (adapter is not null)
                        {
                            _ = adapter->Release();
                        }
                    }
                }

                throw new NotSupportedException(
                    failures.Count == 0
                        ? "No hardware D3D12 adapters were enumerated."
                        : $"No hardware D3D12 adapter satisfies the {profile} profile: {string.Join(" | ", failures)}");
            }
            finally
            {
                if (factory is not null)
                {
                    _ = factory->Release();
                }
            }
        }

        private sealed unsafe class D3D12BackendDevice : IGraphicsBackendDevice
        {
            private IDXGIAdapter3* _adapter;
            private ID3D12Device* _device;

            internal D3D12BackendDevice(ID3D12Device* device, IDXGIAdapter3* adapter, GraphicsCapabilities capabilities)
            {
                ArgumentNullException.ThrowIfNull(capabilities);

                if (device is null)
                {
                    throw new ArgumentNullException(nameof(device));
                }

                _adapter = adapter;
                _device = device;
                Capabilities = capabilities;
            }

            public GraphicsCapabilities Capabilities { get; }

            public IGraphicsBackendBuffer CreateBuffer(BufferDescription description)
            {
                ArgumentNullException.ThrowIfNull(description);

                var nativeDescription = CreateNativeBufferDescription(description);
                var heapProperties = GetHeapProperties(description.MemoryKind);
                var initialState = GetInitialResourceState(description.MemoryKind);
                var resourceId = typeof(ID3D12Resource).GUID;
                ID3D12Resource* resource = null;

                try
                {
                    ThrowIfFailed(_device->CreateCommittedResource(
                        &heapProperties,
                        D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE,
                        &nativeDescription,
                        initialState,
                        null,
                        &resourceId,
                        (void**)&resource));

                    if (resource is null)
                    {
                        throw new InvalidOperationException("D3D12 committed buffer creation returned no resource.");
                    }

                    if (description.DebugName.Length > 0)
                    {
                        fixed (char* name = description.DebugName)
                        {
                            ThrowIfFailed(resource->SetName(name));
                        }
                    }

                    return new D3D12BackendBuffer(resource);
                }
                catch
                {
                    if (resource is not null)
                    {
                        _ = resource->Release();
                    }

                    throw;
                }
            }

            public void Dispose()
            {
                var device = _device;
                var adapter = _adapter;
                _device = null;
                _adapter = null;

                if (device is not null)
                {
                    _ = device->Release();
                }

                if (adapter is not null)
                {
                    _ = adapter->Release();
                }
            }

            public GraphicsResourceAllocationInfo GetBufferAllocationInfo(BufferDescription description)
            {
                ArgumentNullException.ThrowIfNull(description);
                var nativeDescription = CreateNativeBufferDescription(description);
                var allocationInfo = _device->GetResourceAllocationInfo(0, 1, &nativeDescription);

                if (allocationInfo.SizeInBytes == ulong.MaxValue)
                {
                    ThrowIfFailed(_device->GetDeviceRemovedReason());
                    throw new InvalidOperationException("D3D12 returned an invalid buffer allocation size.");
                }

                if (allocationInfo.SizeInBytes == 0)
                {
                    throw new InvalidOperationException("D3D12 returned an invalid buffer allocation size.");
                }

                long? budgetBytes = null;

                if (_adapter is not null)
                {
                    var memoryInfo = default(DXGI_QUERY_VIDEO_MEMORY_INFO);
                    var memorySegment = (DXGI_MEMORY_SEGMENT_GROUP)GetBufferHeapIndex(description.MemoryKind);
                    ThrowIfFailed(_adapter->QueryVideoMemoryInfo(0, memorySegment, &memoryInfo));
                    budgetBytes = checked((long)memoryInfo.Budget);
                }

                return new GraphicsResourceAllocationInfo(
                    checked((long)allocationInfo.SizeInBytes),
                    checked((long)allocationInfo.Alignment),
                    GetBufferHeapIndex(description.MemoryKind),
                    budgetBytes);
            }

            private sealed unsafe class D3D12BackendBuffer : IGraphicsBackendBuffer
            {
                private ID3D12Resource* _resource;

                internal D3D12BackendBuffer(ID3D12Resource* resource)
                {
                    if (resource is null)
                    {
                        throw new ArgumentNullException(nameof(resource));
                    }

                    _resource = resource;
                }

                public void Dispose()
                {
                    var resource = _resource;
                    _resource = null;

                    if (resource is not null)
                    {
                        _ = resource->Release();
                    }
                }
            }
        }
    }
}
