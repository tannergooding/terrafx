// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using TerraFX;
using TerraFX.Buffers;
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

        private static D3D12_SAMPLER_DESC CreateNativeSamplerDescription(
            SamplerDescription description,
            float effectiveMaximumAnisotropy)
        {
            ArgumentNullException.ThrowIfNull(description);

            if (effectiveMaximumAnisotropy > 1.0f)
            {
                throw new NotSupportedException("D3D12 sampler anisotropy is not implemented.");
            }

            return new D3D12_SAMPLER_DESC {
                Filter = GetNativeSamplerFilter(description),
                AddressU = GetNativeSamplerAddressMode(description.AddressU),
                AddressV = GetNativeSamplerAddressMode(description.AddressV),
                AddressW = GetNativeSamplerAddressMode(description.AddressW),
                MipLODBias = 0.0f,
                MaxAnisotropy = 1,
                ComparisonFunc = D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_ALWAYS,
                MinLOD = 0.0f,
                MaxLOD = description.Mip == SamplerFilter.None ? 0.0f : float.MaxValue,
            };
        }

        private static D3D12_RESOURCE_DESC CreateNativeTextureDescription(TextureDescription description)
        {
            ArgumentNullException.ThrowIfNull(description);

            var flags = D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_NONE;

            if ((description.Usage & TextureUsage.Storage) != 0)
            {
                flags |= D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;
            }

            if ((description.Usage & TextureUsage.ColorAttachment) != 0)
            {
                flags |= D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET;
            }

            if ((description.Usage & TextureUsage.DepthAttachment) != 0)
            {
                flags |= D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL;
            }

            return new D3D12_RESOURCE_DESC {
                Dimension = description.Dimension == TextureDimension.Dimension2D
                    ? D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_TEXTURE2D
                    : D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_TEXTURE3D,
                Alignment = 0,
                Width = checked((ulong)description.Width),
                Height = checked((uint)description.Height),
                DepthOrArraySize = checked((ushort)((description.Dimension == TextureDimension.Dimension2D)
                    ? description.ArrayLayers
                    : description.Depth)),
                MipLevels = checked((ushort)description.MipLevels),
                Format = GetNativeFormat(description.Format),
                SampleDesc = new DXGI_SAMPLE_DESC {
                    Count = 1,
                    Quality = 0,
                },
                Layout = D3D12_TEXTURE_LAYOUT.D3D12_TEXTURE_LAYOUT_UNKNOWN,
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
            => D3D12GraphResourceStateMapper.GetInitialState(memoryKind);

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

        private static GraphicsResourceAllocationInfo GetNativeAllocationInfo(
            ID3D12Device* device,
            D3D12_RESOURCE_DESC* description,
            IDXGIAdapter3* adapter,
            int heapIndex)
        {
            var allocationInfo = device->GetResourceAllocationInfo(0, 1, description);

            if (allocationInfo.SizeInBytes == ulong.MaxValue)
            {
                ThrowIfFailed(device->GetDeviceRemovedReason());
                throw new InvalidOperationException("D3D12 returned an invalid resource allocation size.");
            }

            if (allocationInfo.SizeInBytes == 0)
            {
                throw new InvalidOperationException("D3D12 returned an invalid resource allocation size.");
            }

            long? budgetBytes = null;

            if (adapter is not null)
            {
                var memoryInfo = default(DXGI_QUERY_VIDEO_MEMORY_INFO);
                var memorySegment = (DXGI_MEMORY_SEGMENT_GROUP)heapIndex;
                ThrowIfFailed(adapter->QueryVideoMemoryInfo(0, memorySegment, &memoryInfo));
                budgetBytes = checked((long)memoryInfo.Budget);
            }

            return new GraphicsResourceAllocationInfo(
                checked((long)allocationInfo.SizeInBytes),
                checked((long)allocationInfo.Alignment),
                heapIndex,
                budgetBytes);
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

        private static D3D12_TEXTURE_ADDRESS_MODE GetNativeSamplerAddressMode(SamplerAddressMode addressMode)
            => addressMode switch {
                SamplerAddressMode.Clamp => D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_CLAMP,
                SamplerAddressMode.Repeat => D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_WRAP,
                _ => throw new ArgumentOutOfRangeException(nameof(addressMode)),
            };

        private static D3D12_FILTER GetNativeSamplerFilter(SamplerDescription description)
        {
            var minificationLinear = description.Minification == SamplerFilter.Linear;
            var magnificationLinear = description.Magnification == SamplerFilter.Linear;
            var mipLinear = description.Mip == SamplerFilter.Linear;

            return (minificationLinear, magnificationLinear, mipLinear) switch {
                (false, false, false) => D3D12_FILTER.D3D12_FILTER_MIN_MAG_MIP_POINT,
                (false, false, true) => D3D12_FILTER.D3D12_FILTER_MIN_MAG_POINT_MIP_LINEAR,
                (false, true, false) => D3D12_FILTER.D3D12_FILTER_MIN_POINT_MAG_LINEAR_MIP_POINT,
                (false, true, true) => D3D12_FILTER.D3D12_FILTER_MIN_POINT_MAG_MIP_LINEAR,
                (true, false, false) => D3D12_FILTER.D3D12_FILTER_MIN_LINEAR_MAG_MIP_POINT,
                (true, false, true) => D3D12_FILTER.D3D12_FILTER_MIN_LINEAR_MAG_POINT_MIP_LINEAR,
                (true, true, false) => D3D12_FILTER.D3D12_FILTER_MIN_MAG_LINEAR_MIP_POINT,
                (true, true, true) => D3D12_FILTER.D3D12_FILTER_MIN_MAG_MIP_LINEAR,
            };
        }

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

        private static int GetTextureHeapIndex()
            => (int)DXGI_MEMORY_SEGMENT_GROUP.DXGI_MEMORY_SEGMENT_GROUP_LOCAL;

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

        private static void ValidateTextureDescriptionSupport(ID3D12Device* device, TextureDescription description)
        {
            var support = new D3D12_FEATURE_DATA_FORMAT_SUPPORT {
                Format = GetNativeFormat(description.Format),
            };
            ThrowIfFailed(device->CheckFeatureSupport(
                D3D12_FEATURE.D3D12_FEATURE_FORMAT_SUPPORT,
                &support,
                (uint)sizeof(D3D12_FEATURE_DATA_FORMAT_SUPPORT)));

            var dimensionSupport = description.Dimension == TextureDimension.Dimension2D
                ? D3D12_FORMAT_SUPPORT1.D3D12_FORMAT_SUPPORT1_TEXTURE2D
                : D3D12_FORMAT_SUPPORT1.D3D12_FORMAT_SUPPORT1_TEXTURE3D;

            if ((support.Support1 & dimensionSupport) == 0)
            {
                throw new NotSupportedException($"D3D12 does not support {description.Dimension} textures in format {description.Format}.");
            }

            var requiredSupport = default(D3D12_FORMAT_SUPPORT1);

            if ((description.Usage & TextureUsage.Sampled) != 0)
            {
                requiredSupport |= D3D12_FORMAT_SUPPORT1.D3D12_FORMAT_SUPPORT1_SHADER_SAMPLE;
            }

            if ((description.Usage & TextureUsage.Storage) != 0)
            {
                requiredSupport |= D3D12_FORMAT_SUPPORT1.D3D12_FORMAT_SUPPORT1_TYPED_UNORDERED_ACCESS_VIEW;
            }

            if ((description.Usage & TextureUsage.ColorAttachment) != 0)
            {
                requiredSupport |= D3D12_FORMAT_SUPPORT1.D3D12_FORMAT_SUPPORT1_RENDER_TARGET;
            }

            if ((description.Usage & TextureUsage.DepthAttachment) != 0)
            {
                requiredSupport |= D3D12_FORMAT_SUPPORT1.D3D12_FORMAT_SUPPORT1_DEPTH_STENCIL;
            }

            if ((support.Support1 & requiredSupport) != requiredSupport)
            {
                throw new NotSupportedException($"D3D12 does not support texture usages {description.Usage} for {description.Format}.");
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
            private const long MaximumDedicatedStagingAllocationBytes = 512L * 1024 * 1024;
            private const int SamplerDescriptorCapacity = 256;
            private const long StagingRingCapacityBytes = 64L * 1024 * 1024;

            private IDXGIAdapter3* _adapter;
            private ID3D12Device* _device;
            private Stack<int>? _freeSamplerDescriptors;
            private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
            private ID3D12Resource* _quarantinedStagingResource;
            private ID3D12Resource* _readbackStagingResource;
            private ID3D12DescriptorHeap* _samplerDescriptorHeap;
            private uint _samplerDescriptorIncrementSize;
            private D3D12BackendFactory.D3D12TransferQueue? _transferQueue;
            private byte* _uploadStagingMapped;
            private ID3D12Resource* _uploadStagingResource;

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

            private static OwnedBuffer<byte> CopyMappedStorageToOwnedBuffer(byte* source, nint byteLength)
            {
                var storage = UnmanagedBuffer<byte>.Allocate(
                    byteLength,
                    new BufferAllocationOptions(byteLength, alignment: 1));
                var transferred = false;

                try
                {
                    nint copiedBytes = 0;

                    while (copiedBytes < byteLength)
                    {
                        var remainingBytes = byteLength - copiedBytes;
                        var copyLength = remainingBytes > int.MaxValue
                            ? int.MaxValue
                            : checked((int)remainingBytes);
                        storage.CopyFrom(new ReadOnlySpan<byte>(source + copiedBytes, copyLength), copiedBytes);
                        copiedBytes = checked(copiedBytes + copyLength);
                    }

                    var ownedBuffer = storage.TransferToOwnedBuffer();
                    transferred = true;
                    return ownedBuffer;
                }
                finally
                {
                    if (!transferred)
                    {
                        storage.Dispose();
                    }
                }
            }

            private static void CopySourceLeaseToMappedStorage(
                BufferReadLease<byte> source,
                nint sourceOffset,
                nint byteLength,
                byte* destination)
            {
                nint copiedBytes = 0;

                while (copiedBytes < byteLength)
                {
                    var remainingBytes = byteLength - copiedBytes;
                    var copyLength = remainingBytes > int.MaxValue
                        ? int.MaxValue
                        : checked((int)remainingBytes);
                    var copyAction = new BufferUploadCopyAction(destination + copiedBytes);
                    source.UseWindow(checked(sourceOffset + copiedBytes), copyLength, ref copyAction);
                    copiedBytes = checked(copiedBytes + copyLength);
                }
            }

            private static TextureReadback CopyTextureFootprintToReadback(
                byte* source,
                TextureFormat format,
                TextureSubresource subresource,
                (int Width, int Height, int Depth) extent,
                (D3D12_PLACED_SUBRESOURCE_FOOTPRINT Layout, uint RowCount, ulong RowSizeBytes, ulong TotalBytes) footprint)
            {
                var rowByteLength = checked((int)footprint.RowSizeBytes);
                var rowPitch = checked((nint)footprint.Layout.Footprint.RowPitch);
                var slicePitch = checked(rowPitch * extent.Height);
                var byteLength = checked(slicePitch * extent.Depth);
                var nativeSlicePitch = checked(rowPitch * extent.Height);
                var footprintOffset = checked((nint)footprint.Layout.Offset);
                var storage = UnmanagedBuffer<byte>.Allocate(byteLength, new BufferAllocationOptions(byteLength, alignment: 1));
                var transferred = false;

                try
                {
                    for (var depth = 0; depth < extent.Depth; depth++)
                    {
                        for (var row = 0; row < extent.Height; row++)
                        {
                            var sourceRowOffset = checked(
                                footprintOffset
                                + (checked((nint)depth) * nativeSlicePitch)
                                + (checked((nint)row) * rowPitch));
                            var destinationRowOffset = checked(
                                (checked((nint)depth) * slicePitch)
                                + (checked((nint)row) * rowPitch));
                            storage.CopyFrom(new ReadOnlySpan<byte>(source + sourceRowOffset, rowByteLength), destinationRowOffset);
                        }
                    }

                    var ownedStorage = storage.TransferToOwnedBuffer();
                    transferred = true;

                    try
                    {
                        return new TextureReadback(
                            format,
                            subresource,
                            extent.Width,
                            extent.Height,
                            extent.Depth,
                            rowPitch,
                            slicePitch,
                            ownedStorage);
                    }
                    catch
                    {
                        ownedStorage.Dispose();
                        throw;
                    }
                }
                finally
                {
                    if (!transferred)
                    {
                        storage.Dispose();
                    }
                }
            }

            private static void CopyTextureSourceToFootprint(
                BufferReadLease<byte> source,
                nint sourceOffset,
                TextureDataLayout sourceLayout,
                (int Width, int Height, int Depth) extent,
                (D3D12_PLACED_SUBRESOURCE_FOOTPRINT Layout, uint RowCount, ulong RowSizeBytes, ulong TotalBytes) footprint,
                byte* destination)
            {
                var rowByteLength = checked((int)footprint.RowSizeBytes);
                var nativeRowPitch = checked((nint)footprint.Layout.Footprint.RowPitch);
                var nativeSlicePitch = checked(nativeRowPitch * extent.Height);
                var footprintOffset = checked((nint)footprint.Layout.Offset);

                for (var depth = 0; depth < extent.Depth; depth++)
                {
                    for (var row = 0; row < extent.Height; row++)
                    {
                        var sourceRowOffset = checked(
                            sourceOffset
                            + (checked((nint)depth) * sourceLayout.SlicePitch)
                            + (checked((nint)row) * sourceLayout.RowPitch));
                        var destinationRowOffset = checked(
                            footprintOffset
                            + (checked((nint)depth) * nativeSlicePitch)
                            + (checked((nint)row) * nativeRowPitch));
                        var copyAction = new BufferUploadCopyAction(destination + destinationRowOffset);
                        source.UseWindow(sourceRowOffset, rowByteLength, ref copyAction);
                    }
                }
            }

            private static D3D12_TEXTURE_COPY_LOCATION CreateFootprintLocation(
                ID3D12Resource* resource,
                D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprint)
            {
                var location = default(D3D12_TEXTURE_COPY_LOCATION);
                location.pResource = resource;
                location.Type = D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
                location.Anonymous.PlacedFootprint = footprint;
                return location;
            }

            private static D3D12_TEXTURE_COPY_LOCATION CreateSubresourceLocation(ID3D12Resource* resource, uint subresourceIndex)
            {
                var location = default(D3D12_TEXTURE_COPY_LOCATION);
                location.pResource = resource;
                location.Type = D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
                location.Anonymous.SubresourceIndex = subresourceIndex;
                return location;
            }

            private static uint GetTextureSubresourceIndex(TextureDescription description, TextureSubresource subresource)
                => checked((uint)(subresource.MipLevel + (subresource.ArrayLayer * description.MipLevels)));

            private static D3D12BackendBuffer GetTransferBuffer(
                IGraphicsBackendBuffer buffer,
                BufferUsage requiredUsage,
                string parameterName)
            {
                ArgumentNullException.ThrowIfNull(buffer, parameterName);

                if (buffer is not D3D12BackendBuffer selectedBuffer)
                {
                    throw new ArgumentException("The buffer belongs to a different graphics backend.", parameterName);
                }

                if ((selectedBuffer.Description.Usage & requiredUsage) != requiredUsage)
                {
                    throw new ArgumentException($"The buffer does not declare {requiredUsage} usage.", parameterName);
                }

                return selectedBuffer;
            }

            private static D3D12BackendTexture GetTransferTexture(
                IGraphicsBackendTexture texture,
                TextureUsage requiredUsage,
                string parameterName)
            {
                ArgumentNullException.ThrowIfNull(texture, parameterName);

                if (texture is not D3D12BackendTexture selectedTexture)
                {
                    throw new ArgumentException("The texture belongs to a different graphics backend.", parameterName);
                }

                if ((selectedTexture.Description.Usage & requiredUsage) != requiredUsage)
                {
                    throw new ArgumentException($"The texture does not declare {requiredUsage} usage.", parameterName);
                }

                return selectedTexture;
            }

            private static void ValidateTransferRange(
                long bufferLength,
                long offset,
                nint byteLength,
                string offsetName,
                string lengthName)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(offset, offsetName);
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLength, lengthName);
                var checkedLength = checked((long)byteLength);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, bufferLength, offsetName);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(checkedLength, bufferLength - offset, lengthName);
            }

            public IGraphicsBackendBuffer CreateBuffer(BufferDescription description)
            {
                ArgumentNullException.ThrowIfNull(description);
                var resource = CreateCommittedResource(
                    CreateNativeBufferDescription(description),
                    GetHeapProperties(description.MemoryKind),
                    GetInitialResourceState(description.MemoryKind),
                    description.DebugName);

                try
                {
                    return new D3D12BackendBuffer(resource, description);
                }
                catch
                {
                    _ = resource->Release();
                    throw;
                }
            }

            public IGraphicsBackendPipeline CreatePipeline(
                PipelineDescription description,
                ReadOnlySpan<BufferReadLease<byte>> bytecodeLeases)
                => D3D12BackendFactory.D3D12PipelineBuilder.Create(_device, description, bytecodeLeases);

            public IGraphicsBackendSampler CreateSampler(SamplerDescription description, float effectiveMaximumAnisotropy)
            {
                ArgumentNullException.ThrowIfNull(description);
                EnsureOwnerThread();

                if (!float.IsFinite(effectiveMaximumAnisotropy)
                    || (effectiveMaximumAnisotropy < 1.0f)
                    || (effectiveMaximumAnisotropy > Capabilities.Limits.MaximumSamplerAnisotropy))
                {
                    throw new ArgumentOutOfRangeException(nameof(effectiveMaximumAnisotropy));
                }

                EnsureSamplerDescriptorHeap();
                var freeDescriptors = _freeSamplerDescriptors
                    ?? throw new InvalidOperationException("The D3D12 sampler descriptor heap has no free-slot owner.");

                if (freeDescriptors.Count == 0)
                {
                    throw new InvalidOperationException("The D3D12 sampler descriptor capacity is exhausted.");
                }

                var slot = freeDescriptors.Pop();

                try
                {
                    var nativeDescription = CreateNativeSamplerDescription(description, effectiveMaximumAnisotropy);
                    var cpuHandle = _samplerDescriptorHeap->GetCPUDescriptorHandleForHeapStart();
                    cpuHandle.ptr = checked(cpuHandle.ptr + (checked((nuint)slot) * _samplerDescriptorIncrementSize));
                    _device->CreateSampler(&nativeDescription, cpuHandle);
                    return new D3D12BackendSampler(this, slot);
                }
                catch
                {
                    freeDescriptors.Push(slot);
                    throw;
                }
            }

            public IGraphicsBackendTexture CreateTexture(TextureDescription description)
            {
                ArgumentNullException.ThrowIfNull(description);
                ValidateTextureDescriptionSupport(_device, description);
                var resource = CreateCommittedResource(
                    CreateNativeTextureDescription(description),
                    GetHeapProperties(GpuMemoryKind.DeviceLocal),
                    D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON,
                    description.DebugName);

                try
                {
                    return new D3D12BackendTexture(resource, description);
                }
                catch
                {
                    _ = resource->Release();
                    throw;
                }
            }

            public void Dispose()
            {
                var transferQueue = _transferQueue;

                if ((transferQueue is not null) && transferQueue.HasUncertainSubmission)
                {
                    throw new InvalidOperationException("The D3D12 backend cannot release storage while transfer completion is uncertain.");
                }

                if (_quarantinedStagingResource is not null)
                {
                    throw new InvalidOperationException("The D3D12 backend has a staging resource quarantined after uncertain transfer completion.");
                }

                if ((_samplerDescriptorHeap is not null)
                    && (_freeSamplerDescriptors?.Count != SamplerDescriptorCapacity))
                {
                    throw new InvalidOperationException("The D3D12 backend still has live sampler descriptor owners.");
                }

                transferQueue?.Dispose();
                _transferQueue = null;

                if (_uploadStagingResource is not null)
                {
                    var writtenRange = new D3D12_RANGE {
                        Begin = 0,
                        End = checked((nuint)StagingRingCapacityBytes),
                    };
                    _uploadStagingResource->Unmap(0, &writtenRange);
                    _ = _uploadStagingResource->Release();
                    _uploadStagingResource = null;
                    _uploadStagingMapped = null;
                }

                if (_readbackStagingResource is not null)
                {
                    _ = _readbackStagingResource->Release();
                    _readbackStagingResource = null;
                }

                if (_samplerDescriptorHeap is not null)
                {
                    _ = _samplerDescriptorHeap->Release();
                    _samplerDescriptorHeap = null;
                    _samplerDescriptorIncrementSize = 0;
                    _freeSamplerDescriptors = null;
                }

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
                return GetNativeAllocationInfo(
                    _device,
                    &nativeDescription,
                    _adapter,
                    GetBufferHeapIndex(description.MemoryKind));
            }

            public GraphicsResourceAllocationInfo GetTextureAllocationInfo(TextureDescription description)
            {
                ArgumentNullException.ThrowIfNull(description);
                ValidateTextureDescriptionSupport(_device, description);
                var nativeDescription = CreateNativeTextureDescription(description);
                return GetNativeAllocationInfo(_device, &nativeDescription, _adapter, GetTextureHeapIndex());
            }

            [SuppressMessage(
                "Usage",
                "CA1031:Do not catch general exception types",
                Justification = "A failed readback closes an unsubmitted command list before releasing its staging owner.")]
            public OwnedBuffer<byte> ReadbackBuffer(IGraphicsBackendBuffer source, long sourceOffset, nint byteLength)
            {
                var sourceBuffer = GetTransferBuffer(source, BufferUsage.CopySource, nameof(source));
                ValidateTransferRange(sourceBuffer.Description.ByteLength, sourceOffset, byteLength, nameof(sourceOffset), nameof(byteLength));
                var transferQueue = GetTransferQueue();
                var stagingResource = GetReadbackStaging(byteLength, out var isDedicated);
                var isMapped = false;

                try
                {
                    var commandList = transferQueue.BeginRecording();
                    var sourceResource = sourceBuffer.GetResource();
                    var requiresTransition = sourceBuffer.Description.MemoryKind == GpuMemoryKind.DeviceLocal;

                    if (requiresTransition)
                    {
                        var toCopySource = D3D12_RESOURCE_BARRIER.InitTransition(
                            sourceResource,
                            D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON,
                            D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_SOURCE,
                            0,
                            D3D12_RESOURCE_BARRIER_FLAGS.D3D12_RESOURCE_BARRIER_FLAG_NONE);
                        commandList->ResourceBarrier(1, &toCopySource);
                    }

                    commandList->CopyBufferRegion(
                        stagingResource,
                        0,
                        sourceResource,
                        checked((ulong)sourceOffset),
                        checked((ulong)byteLength));

                    if (requiresTransition)
                    {
                        var toCommon = D3D12_RESOURCE_BARRIER.InitTransition(
                            sourceResource,
                            D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_SOURCE,
                            D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON,
                            0,
                            D3D12_RESOURCE_BARRIER_FLAGS.D3D12_RESOURCE_BARRIER_FLAG_NONE);
                        commandList->ResourceBarrier(1, &toCommon);
                    }

                    _ = transferQueue.SubmitAndWait();
                    var readRange = new D3D12_RANGE {
                        Begin = 0,
                        End = checked((nuint)byteLength),
                    };
                    void* mapped = null;
                    ThrowIfFailed(stagingResource->Map(0, &readRange, &mapped));

                    if (mapped is null)
                    {
                        throw new InvalidOperationException("D3D12 readback staging returned no mapped storage.");
                    }

                    isMapped = true;
                    return CopyMappedStorageToOwnedBuffer((byte*)mapped, byteLength);
                }
                catch (Exception exception)
                {
                    try
                    {
                        transferQueue.StopRecording();
                    }
                    catch (Exception stopException)
                    {
                        throw new AggregateException(exception, stopException);
                    }

                    throw;
                }
                finally
                {
                    if (isMapped)
                    {
                        var readRange = new D3D12_RANGE {
                            Begin = 0,
                            End = 0,
                        };
                        stagingResource->Unmap(0, &readRange);
                        isMapped = false;
                    }

                    if (isDedicated)
                    {
                        ReleaseDedicatedStagingResource(
                            stagingResource,
                            isMapped: false,
                            writtenByteLength: 0,
                            transferQueue);
                    }
                }
            }

            [SuppressMessage(
                "Usage",
                "CA1031:Do not catch general exception types",
                Justification = "A failed texture readback closes an unsubmitted command list before releasing its staging owner.")]
            public TextureReadback ReadbackTexture(IGraphicsBackendTexture source, TextureSubresource subresource)
            {
                var sourceTexture = GetTransferTexture(source, TextureUsage.CopySource, nameof(source));
                var extent = GraphicsDevice.GetTextureSubresourceExtent(sourceTexture.Description, subresource);
                var footprint = GetTextureCopyFootprint(sourceTexture.Description, subresource, extent);
                var byteLength = checked((nint)footprint.TotalBytes);
                var transferQueue = GetTransferQueue();
                var stagingResource = GetReadbackStaging(byteLength, out var isDedicated);
                var isMapped = false;

                try
                {
                    var commandList = transferQueue.BeginRecording();
                    var sourceResource = sourceTexture.GetResource();
                    var subresourceIndex = GetTextureSubresourceIndex(sourceTexture.Description, subresource);
                    var toCopySource = D3D12_RESOURCE_BARRIER.InitTransition(
                        sourceResource,
                        D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON,
                        D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_SOURCE,
                        subresourceIndex,
                        D3D12_RESOURCE_BARRIER_FLAGS.D3D12_RESOURCE_BARRIER_FLAG_NONE);
                    commandList->ResourceBarrier(1, &toCopySource);

                    var sourceLocation = CreateSubresourceLocation(sourceResource, subresourceIndex);
                    var destinationLocation = CreateFootprintLocation(stagingResource, footprint.Layout);
                    commandList->CopyTextureRegion(&destinationLocation, 0, 0, 0, &sourceLocation, null);

                    var toCommon = D3D12_RESOURCE_BARRIER.InitTransition(
                        sourceResource,
                        D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_SOURCE,
                        D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON,
                        subresourceIndex,
                        D3D12_RESOURCE_BARRIER_FLAGS.D3D12_RESOURCE_BARRIER_FLAG_NONE);
                    commandList->ResourceBarrier(1, &toCommon);
                    _ = transferQueue.SubmitAndWait();

                    var readRange = new D3D12_RANGE {
                        Begin = checked((nuint)footprint.Layout.Offset),
                        End = checked((nuint)footprint.TotalBytes),
                    };
                    void* mapped = null;
                    ThrowIfFailed(stagingResource->Map(0, &readRange, &mapped));

                    if (mapped is null)
                    {
                        throw new InvalidOperationException("D3D12 texture readback staging returned no mapped storage.");
                    }

                    isMapped = true;
                    return CopyTextureFootprintToReadback(
                        (byte*)mapped,
                        sourceTexture.Description.Format,
                        subresource,
                        extent,
                        footprint);
                }
                catch (Exception exception)
                {
                    try
                    {
                        transferQueue.StopRecording();
                    }
                    catch (Exception stopException)
                    {
                        throw new AggregateException(exception, stopException);
                    }

                    throw;
                }
                finally
                {
                    if (isMapped)
                    {
                        var readRange = new D3D12_RANGE {
                            Begin = 0,
                            End = 0,
                        };
                        stagingResource->Unmap(0, &readRange);
                    }

                    if (isDedicated)
                    {
                        ReleaseDedicatedStagingResource(
                            stagingResource,
                            isMapped: false,
                            writtenByteLength: 0,
                            transferQueue);
                    }
                }
            }

            [SuppressMessage(
                "Usage",
                "CA1031:Do not catch general exception types",
                Justification = "A failed upload closes an unsubmitted command list before releasing its staging owner.")]
            public long UploadBuffer(
                IGraphicsBackendBuffer destination,
                long destinationOffset,
                BufferReadLease<byte> source,
                nint sourceOffset,
                nint byteLength)
            {
                ArgumentNullException.ThrowIfNull(source);
                ArgumentOutOfRangeException.ThrowIfNegative(sourceOffset);
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLength);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(sourceOffset, source.Length);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(byteLength, source.Length - sourceOffset);
                var destinationBuffer = GetTransferBuffer(destination, BufferUsage.CopyDestination, nameof(destination));

                if (destinationBuffer.Description.MemoryKind != GpuMemoryKind.DeviceLocal)
                {
                    throw new NotSupportedException("D3D12 buffer uploads require a device-local destination.");
                }

                ValidateTransferRange(
                    destinationBuffer.Description.ByteLength,
                    destinationOffset,
                    byteLength,
                    nameof(destinationOffset),
                    nameof(byteLength));
                var transferQueue = GetTransferQueue();
                var mapped = GetUploadStaging(byteLength, out var stagingResource, out var isDedicated);
                var isMapped = true;

                try
                {
                    CopySourceLeaseToMappedStorage(source, sourceOffset, byteLength, mapped);
                    var commandList = transferQueue.BeginRecording();
                    var destinationResource = destinationBuffer.GetResource();
                    var toCopyDestination = D3D12_RESOURCE_BARRIER.InitTransition(
                        destinationResource,
                        D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON,
                        D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST,
                        0,
                        D3D12_RESOURCE_BARRIER_FLAGS.D3D12_RESOURCE_BARRIER_FLAG_NONE);
                    commandList->ResourceBarrier(1, &toCopyDestination);
                    commandList->CopyBufferRegion(
                        destinationResource,
                        checked((ulong)destinationOffset),
                        stagingResource,
                        0,
                        checked((ulong)byteLength));
                    var toCommon = D3D12_RESOURCE_BARRIER.InitTransition(
                        destinationResource,
                        D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST,
                        D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON,
                        0,
                        D3D12_RESOURCE_BARRIER_FLAGS.D3D12_RESOURCE_BARRIER_FLAG_NONE);
                    commandList->ResourceBarrier(1, &toCommon);
                    return transferQueue.SubmitAndWait();
                }
                catch (Exception exception)
                {
                    try
                    {
                        transferQueue.StopRecording();
                    }
                    catch (Exception stopException)
                    {
                        throw new AggregateException(exception, stopException);
                    }

                    throw;
                }
                finally
                {
                    if (isDedicated)
                    {
                        ReleaseDedicatedStagingResource(
                            stagingResource,
                            isMapped,
                            byteLength,
                            transferQueue);
                    }
                }
            }

            [SuppressMessage(
                "Usage",
                "CA1031:Do not catch general exception types",
                Justification = "A failed upload closes an unsubmitted command list before releasing its staging owner.")]
            public long UploadBuffer(IGraphicsBackendBuffer destination, long destinationOffset, ReadOnlySpan<byte> source)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(source.Length);
                var destinationBuffer = GetTransferBuffer(destination, BufferUsage.CopyDestination, nameof(destination));

                if (destinationBuffer.Description.MemoryKind != GpuMemoryKind.DeviceLocal)
                {
                    throw new NotSupportedException("D3D12 buffer uploads require a device-local destination.");
                }

                ValidateTransferRange(
                    destinationBuffer.Description.ByteLength,
                    destinationOffset,
                    source.Length,
                    nameof(destinationOffset),
                    nameof(source));
                var byteLength = (nint)source.Length;
                var transferQueue = GetTransferQueue();
                var mapped = GetUploadStaging(byteLength, out var stagingResource, out var isDedicated);

                try
                {
                    source.CopyTo(new Span<byte>(mapped, source.Length));
                    var commandList = transferQueue.BeginRecording();
                    var destinationResource = destinationBuffer.GetResource();
                    var toCopyDestination = D3D12_RESOURCE_BARRIER.InitTransition(
                        destinationResource,
                        D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON,
                        D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST,
                        0,
                        D3D12_RESOURCE_BARRIER_FLAGS.D3D12_RESOURCE_BARRIER_FLAG_NONE);
                    commandList->ResourceBarrier(1, &toCopyDestination);
                    commandList->CopyBufferRegion(
                        destinationResource,
                        checked((ulong)destinationOffset),
                        stagingResource,
                        0,
                        checked((ulong)byteLength));
                    var toCommon = D3D12_RESOURCE_BARRIER.InitTransition(
                        destinationResource,
                        D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST,
                        D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON,
                        0,
                        D3D12_RESOURCE_BARRIER_FLAGS.D3D12_RESOURCE_BARRIER_FLAG_NONE);
                    commandList->ResourceBarrier(1, &toCommon);
                    return transferQueue.SubmitAndWait();
                }
                catch (Exception exception)
                {
                    try
                    {
                        transferQueue.StopRecording();
                    }
                    catch (Exception stopException)
                    {
                        throw new AggregateException(exception, stopException);
                    }

                    throw;
                }
                finally
                {
                    if (isDedicated)
                    {
                        ReleaseDedicatedStagingResource(
                            stagingResource,
                            isMapped: true,
                            byteLength,
                            transferQueue);
                    }
                }
            }

            [SuppressMessage(
                "Usage",
                "CA1031:Do not catch general exception types",
                Justification = "A failed texture upload closes an unsubmitted command list before releasing its staging owner.")]
            public long UploadTexture(
                IGraphicsBackendTexture destination,
                TextureSubresource subresource,
                BufferReadLease<byte> source,
                nint sourceOffset,
                TextureDataLayout sourceLayout)
            {
                ArgumentNullException.ThrowIfNull(source);
                var destinationTexture = GetTransferTexture(destination, TextureUsage.CopyDestination, nameof(destination));
                var extent = GraphicsDevice.GetTextureSubresourceExtent(destinationTexture.Description, subresource);
                _ = GraphicsDevice.ValidateTextureUploadSource(
                    source,
                    sourceOffset,
                    sourceLayout,
                    extent.Width,
                    extent.Height,
                    extent.Depth,
                    destinationTexture.Description.Format);
                var footprint = GetTextureCopyFootprint(destinationTexture.Description, subresource, extent);
                var byteLength = checked((nint)footprint.TotalBytes);
                var transferQueue = GetTransferQueue();
                var mapped = GetUploadStaging(byteLength, out var stagingResource, out var isDedicated);

                try
                {
                    new Span<byte>(mapped, checked((int)byteLength)).Clear();
                    CopyTextureSourceToFootprint(
                        source,
                        sourceOffset,
                        sourceLayout,
                        extent,
                        footprint,
                        mapped);

                    var commandList = transferQueue.BeginRecording();
                    var destinationResource = destinationTexture.GetResource();
                    var subresourceIndex = GetTextureSubresourceIndex(destinationTexture.Description, subresource);
                    var toCopyDestination = D3D12_RESOURCE_BARRIER.InitTransition(
                        destinationResource,
                        D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON,
                        D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST,
                        subresourceIndex,
                        D3D12_RESOURCE_BARRIER_FLAGS.D3D12_RESOURCE_BARRIER_FLAG_NONE);
                    commandList->ResourceBarrier(1, &toCopyDestination);

                    var sourceLocation = CreateFootprintLocation(stagingResource, footprint.Layout);
                    var destinationLocation = CreateSubresourceLocation(destinationResource, subresourceIndex);
                    commandList->CopyTextureRegion(&destinationLocation, 0, 0, 0, &sourceLocation, null);

                    var toCommon = D3D12_RESOURCE_BARRIER.InitTransition(
                        destinationResource,
                        D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST,
                        D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON,
                        subresourceIndex,
                        D3D12_RESOURCE_BARRIER_FLAGS.D3D12_RESOURCE_BARRIER_FLAG_NONE);
                    commandList->ResourceBarrier(1, &toCommon);
                    return transferQueue.SubmitAndWait();
                }
                catch (Exception exception)
                {
                    try
                    {
                        transferQueue.StopRecording();
                    }
                    catch (Exception stopException)
                    {
                        throw new AggregateException(exception, stopException);
                    }

                    throw;
                }
                finally
                {
                    if (isDedicated)
                    {
                        ReleaseDedicatedStagingResource(
                            stagingResource,
                            isMapped: true,
                            byteLength,
                            transferQueue);
                    }
                }
            }

            private ID3D12Resource* CreateCommittedResource(
                D3D12_RESOURCE_DESC nativeDescription,
                D3D12_HEAP_PROPERTIES heapProperties,
                D3D12_RESOURCE_STATES initialState,
                string debugName)
            {
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
                        throw new InvalidOperationException("D3D12 committed resource creation returned no resource.");
                    }

                    if (debugName.Length > 0)
                    {
                        fixed (char* name = debugName)
                        {
                            ThrowIfFailed(resource->SetName(name));
                        }
                    }

                    return resource;
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

            [SuppressMessage(
                "Usage",
                "CA2201:Do not raise reserved exception types",
                Justification = "Staging admission reports exhaustion of the selected adapter's host-visible memory budget.")]
            private ID3D12Resource* CreateStagingResource(GpuMemoryKind memoryKind, long byteLength, string debugName)
            {
                var usage = memoryKind == GpuMemoryKind.Upload
                    ? BufferUsage.CopySource
                    : BufferUsage.CopyDestination;
                var description = new BufferDescription(byteLength, usage, memoryKind, structuredStride: null, debugName);
                var allocationInfo = GetBufferAllocationInfo(description);

                if (allocationInfo.SizeBytes > MaximumDedicatedStagingAllocationBytes)
                {
                    throw new NotSupportedException(
                        $"D3D12 staging allocations larger than {MaximumDedicatedStagingAllocationBytes} bytes are not supported.");
                }

                var availableBudgetBytes = GetAvailableBudgetBytes(memoryKind);

                if (availableBudgetBytes is long available && allocationInfo.SizeBytes > available)
                {
                    throw new OutOfMemoryException("The D3D12 adapter's host-visible staging budget is exhausted.");
                }

                var nativeDescription = CreateNativeBufferDescription(description);
                return CreateCommittedResource(
                    nativeDescription,
                    GetHeapProperties(memoryKind),
                    GetInitialResourceState(memoryKind),
                    debugName);
            }

            private void EnsureOwnerThread()
            {
                if (_ownerThreadId != Environment.CurrentManagedThreadId)
                {
                    throw new InvalidOperationException("D3D12 sampler ownership must remain on the device owner thread.");
                }
            }

            private void EnsureReadbackStaging()
            {
                if (_readbackStagingResource is null)
                {
                    _readbackStagingResource = CreateStagingResource(
                        GpuMemoryKind.Readback,
                        StagingRingCapacityBytes,
                        "TerraFX D3D12 readback staging ring");
                }
            }

            private void EnsureSamplerDescriptorHeap()
            {
                if (_samplerDescriptorHeap is not null)
                {
                    return;
                }

                EnsureOwnerThread();
                var freeDescriptors = new Stack<int>(SamplerDescriptorCapacity);

                for (var slot = 0; slot < SamplerDescriptorCapacity; slot++)
                {
                    freeDescriptors.Push(slot);
                }

                var heapDescription = new D3D12_DESCRIPTOR_HEAP_DESC {
                    Type = D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_SAMPLER,
                    NumDescriptors = SamplerDescriptorCapacity,
                    Flags = D3D12_DESCRIPTOR_HEAP_FLAGS.D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE,
                    NodeMask = 0,
                };
                var heapId = typeof(ID3D12DescriptorHeap).GUID;
                ID3D12DescriptorHeap* descriptorHeap = null;
                ThrowIfFailed(_device->CreateDescriptorHeap(&heapDescription, &heapId, (void**)&descriptorHeap));

                if (descriptorHeap is null)
                {
                    throw new InvalidOperationException("D3D12 sampler descriptor heap creation returned no heap.");
                }

                try
                {
                    var incrementSize = _device->GetDescriptorHandleIncrementSize(
                        D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_SAMPLER);

                    if (incrementSize == 0)
                    {
                        throw new InvalidOperationException("D3D12 reported a zero sampler descriptor increment.");
                    }

                    _samplerDescriptorHeap = descriptorHeap;
                    _samplerDescriptorIncrementSize = incrementSize;
                    _freeSamplerDescriptors = freeDescriptors;
                }
                catch
                {
                    _ = descriptorHeap->Release();
                    throw;
                }
            }

            private void EnsureUploadStaging()
            {
                if (_uploadStagingResource is null)
                {
                    var stagingResource = CreateStagingResource(
                        GpuMemoryKind.Upload,
                        StagingRingCapacityBytes,
                        "TerraFX D3D12 upload staging ring");
                    var writtenRange = new D3D12_RANGE {
                        Begin = 0,
                        End = 0,
                    };
                    void* mapped = null;

                    try
                    {
                        ThrowIfFailed(stagingResource->Map(0, &writtenRange, &mapped));

                        if (mapped is null)
                        {
                            throw new InvalidOperationException("D3D12 upload staging returned no mapped storage.");
                        }

                        _uploadStagingResource = stagingResource;
                        _uploadStagingMapped = (byte*)mapped;
                    }
                    catch
                    {
                        _ = stagingResource->Release();
                        throw;
                    }
                }
            }

            private long? GetAvailableBudgetBytes(GpuMemoryKind memoryKind)
            {
                if (_adapter is null)
                {
                    return null;
                }

                var memoryInfo = default(DXGI_QUERY_VIDEO_MEMORY_INFO);
                var memorySegment = (DXGI_MEMORY_SEGMENT_GROUP)GetBufferHeapIndex(memoryKind);
                ThrowIfFailed(_adapter->QueryVideoMemoryInfo(0, memorySegment, &memoryInfo));
                var availableBytes = memoryInfo.Budget > memoryInfo.CurrentUsage
                    ? memoryInfo.Budget - memoryInfo.CurrentUsage
                    : 0;
                return checked((long)availableBytes);
            }

            private ID3D12Resource* GetReadbackStaging(nint byteLength, out bool isDedicated)
            {
                if (byteLength <= StagingRingCapacityBytes)
                {
                    EnsureReadbackStaging();
                    isDedicated = false;
                    return _readbackStagingResource;
                }

                isDedicated = true;
                return CreateStagingResource(GpuMemoryKind.Readback, checked((long)byteLength), "TerraFX D3D12 dedicated readback");
            }

            private (D3D12_PLACED_SUBRESOURCE_FOOTPRINT Layout, uint RowCount, ulong RowSizeBytes, ulong TotalBytes) GetTextureCopyFootprint(
                TextureDescription description,
                TextureSubresource subresource,
                (int Width, int Height, int Depth) extent)
            {
                var nativeDescription = CreateNativeTextureDescription(description);
                var subresourceIndex = GetTextureSubresourceIndex(description, subresource);
                var layout = default(D3D12_PLACED_SUBRESOURCE_FOOTPRINT);
                uint rowCount = 0;
                ulong rowSizeBytes = 0;
                ulong totalBytes = 0;
                _device->GetCopyableFootprints(
                    &nativeDescription,
                    subresourceIndex,
                    1,
                    0,
                    &layout,
                    &rowCount,
                    &rowSizeBytes,
                    &totalBytes);

                var expectedRowSizeBytes = checked((ulong)extent.Width * (ulong)TextureReadback.GetBytesPerTexel(description.Format));
                var footprint = layout.Footprint;

                if ((rowCount != (uint)extent.Height)
                    || (rowSizeBytes != expectedRowSizeBytes)
                    || (footprint.Width != (uint)extent.Width)
                    || (footprint.Height != (uint)extent.Height)
                    || (footprint.Depth != (uint)extent.Depth)
                    || (footprint.RowPitch < rowSizeBytes)
                    || (totalBytes == 0)
                    || (totalBytes == ulong.MaxValue))
                {
                    throw new NotSupportedException("D3D12 returned an unsupported texture-copy footprint.");
                }

                var footprintByteLength = checked((ulong)footprint.RowPitch * rowCount * footprint.Depth);
                var footprintEnd = checked(layout.Offset + footprintByteLength);

                if (footprintEnd > totalBytes)
                {
                    throw new InvalidOperationException("D3D12 returned an incomplete texture-copy footprint extent.");
                }

                return (layout, rowCount, rowSizeBytes, totalBytes);
            }

            private D3D12BackendFactory.D3D12TransferQueue GetTransferQueue()
            {
                _transferQueue ??= new D3D12BackendFactory.D3D12TransferQueue(_device);

                return _transferQueue;
            }

            private byte* GetUploadStaging(nint byteLength, out ID3D12Resource* stagingResource, out bool isDedicated)
            {
                if (byteLength <= StagingRingCapacityBytes)
                {
                    EnsureUploadStaging();
                    isDedicated = false;
                    stagingResource = _uploadStagingResource;
                    return _uploadStagingMapped;
                }

                isDedicated = true;
                stagingResource = CreateStagingResource(
                    GpuMemoryKind.Upload,
                    checked((long)byteLength),
                    "TerraFX D3D12 dedicated upload");
                var writtenRange = new D3D12_RANGE {
                    Begin = 0,
                    End = 0,
                };
                void* mapped = null;

                try
                {
                    ThrowIfFailed(stagingResource->Map(0, &writtenRange, &mapped));

                    if (mapped is null)
                    {
                        throw new InvalidOperationException("D3D12 dedicated upload staging returned no mapped storage.");
                    }

                    return (byte*)mapped;
                }
                catch
                {
                    _ = stagingResource->Release();
                    stagingResource = null;
                    throw;
                }
            }

            private void ReleaseDedicatedStagingResource(
                ID3D12Resource* stagingResource,
                bool isMapped,
                nint writtenByteLength,
                D3D12BackendFactory.D3D12TransferQueue transferQueue)
            {
                if (transferQueue.HasUncertainSubmission)
                {
                    _quarantinedStagingResource = stagingResource;
                    return;
                }

                if (isMapped)
                {
                    var writtenRange = new D3D12_RANGE {
                        Begin = 0,
                        End = checked((nuint)writtenByteLength),
                    };
                    stagingResource->Unmap(0, &writtenRange);
                }

                _ = stagingResource->Release();
            }

            private void ReleaseSamplerDescriptor(int slot)
            {
                EnsureOwnerThread();

                if ((uint)slot >= SamplerDescriptorCapacity)
                {
                    throw new ArgumentOutOfRangeException(nameof(slot));
                }

                var freeDescriptors = _freeSamplerDescriptors
                    ?? throw new InvalidOperationException("The D3D12 sampler descriptor heap has no free-slot owner.");

                if (freeDescriptors.Contains(slot))
                {
                    throw new InvalidOperationException("The D3D12 sampler descriptor was released more than once.");
                }

                freeDescriptors.Push(slot);
            }

            private readonly unsafe struct BufferUploadCopyAction : IAction<ReadOnlySpan<byte>>
            {
                private readonly byte* _destination;

                internal BufferUploadCopyAction(byte* destination)
                {
                    _destination = destination;
                }

                public readonly void Invoke(scoped ReadOnlySpan<byte> argument)
                    => argument.CopyTo(new Span<byte>(_destination, argument.Length));
            }

            private sealed class D3D12BackendBuffer : D3D12BackendResource, IGraphicsBackendBuffer
            {
                internal D3D12BackendBuffer(ID3D12Resource* resource, BufferDescription description)
                    : base(resource)
                {
                    ArgumentNullException.ThrowIfNull(description);
                    Description = description;
                }

                internal BufferDescription Description { get; }
            }

            private abstract unsafe class D3D12BackendResource : IGraphicsBackendResource
            {
                private ID3D12Resource* _resource;

                protected D3D12BackendResource(ID3D12Resource* resource)
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

                internal ID3D12Resource* GetResource()
                {
                    ObjectDisposedException.ThrowIf(_resource is null, this);
                    return _resource;
                }
            }

            private sealed class D3D12BackendSampler : IGraphicsBackendSampler
            {
                private readonly int _descriptorSlot;
                private D3D12BackendDevice? _device;

                internal D3D12BackendSampler(D3D12BackendDevice device, int descriptorSlot)
                {
                    ArgumentNullException.ThrowIfNull(device);
                    _device = device;
                    _descriptorSlot = descriptorSlot;
                }

                public void Dispose()
                {
                    var device = _device;

                    if (device is null)
                    {
                        return;
                    }

                    device.ReleaseSamplerDescriptor(_descriptorSlot);
                    _device = null;
                }
            }

            private sealed class D3D12BackendTexture : D3D12BackendResource, IGraphicsBackendTexture
            {
                internal D3D12BackendTexture(ID3D12Resource* resource, TextureDescription description)
                    : base(resource)
                {
                    ArgumentNullException.ThrowIfNull(description);
                    Description = description;
                }

                internal TextureDescription Description { get; }
            }
        }

        private unsafe class D3D12PipelineBuilder
        {
            internal static IGraphicsBackendPipeline Create(
                ID3D12Device* device,
                PipelineDescription description,
                ReadOnlySpan<BufferReadLease<byte>> bytecodeLeases)
            {
                if (device is null)
                {
                    throw new ArgumentNullException(nameof(device));
                }

                ArgumentNullException.ThrowIfNull(description);

                var shaderInputs = description switch {
                    GraphicsPipelineDescription graphicsDescription => graphicsDescription.Shaders,
                    ComputePipelineDescription computeDescription => [computeDescription.Shader],
                    _ => throw new ArgumentOutOfRangeException(nameof(description)),
                };

                if (bytecodeLeases.Length != shaderInputs.Length)
                {
                    throw new ArgumentException("The retained bytecode leases must match the pipeline shader stages.", nameof(bytecodeLeases));
                }

                for (var index = 0; index < shaderInputs.Length; index++)
                {
                    if (shaderInputs[index].Format != ShaderBinaryFormat.Dxil)
                    {
                        throw new NotSupportedException("The D3D12 backend requires DXIL shader bytecode.");
                    }

                    if (!shaderInputs[index].Reflection.Resources.IsEmpty)
                    {
                        throw new NotSupportedException("D3D12 pipelines with shader resource bindings are not implemented.");
                    }

                    if (!shaderInputs[index].Reflection.RequiredCapabilities.IsEmpty)
                    {
                        throw new NotSupportedException("D3D12 shader capability requirements are not yet qualified.");
                    }
                }

                var shaderBytecode = stackalloc void*[bytecodeLeases.Length];
                var shaderBytecodeLengths = stackalloc nuint[bytecodeLeases.Length];
                NativeMemory.Clear(shaderBytecode, checked((nuint)bytecodeLeases.Length * (nuint)sizeof(void*)));

                ID3D12RootSignature* rootSignature = null;
                ID3D12PipelineState* pipelineState = null;

                try
                {
                    for (var index = 0; index < bytecodeLeases.Length; index++)
                    {
                        shaderBytecode[index] = CopyShaderBytecode(bytecodeLeases[index], out shaderBytecodeLengths[index]);
                    }

                    rootSignature = CreateEmptyRootSignature(device);

                    if (description is GraphicsPipelineDescription selectedGraphicsDescription)
                    {
                        pipelineState = CreateGraphicsPipelineState(
                            device,
                            rootSignature,
                            selectedGraphicsDescription,
                            shaderBytecode[0],
                            shaderBytecodeLengths[0],
                            shaderBytecode[1],
                            shaderBytecodeLengths[1]);
                    }
                    else if (description is ComputePipelineDescription)
                    {
                        pipelineState = CreateComputePipelineState(
                            device,
                            rootSignature,
                            shaderBytecode[0],
                            shaderBytecodeLengths[0]);
                    }
                    else
                    {
                        throw new ArgumentOutOfRangeException(nameof(description));
                    }

                    if ((rootSignature is null) || (pipelineState is null))
                    {
                        throw new InvalidOperationException("D3D12 pipeline creation returned an incomplete native owner.");
                    }

                    var result = new D3D12BackendPipeline(pipelineState, rootSignature);
                    pipelineState = null;
                    rootSignature = null;
                    return result;
                }
                catch
                {
                    if (pipelineState is not null)
                    {
                        _ = pipelineState->Release();
                    }

                    if (rootSignature is not null)
                    {
                        _ = rootSignature->Release();
                    }

                    throw;
                }
                finally
                {
                    for (var index = 0; index < bytecodeLeases.Length; index++)
                    {
                        NativeMemory.Free(shaderBytecode[index]);
                    }
                }
            }

            [SuppressMessage(
                "Usage",
                "CA2201:Do not raise reserved exception types",
                Justification = "Native staging memory allocation failure is reported as an allocation failure, not successful shader creation.")]
            private static void* CopyShaderBytecode(BufferReadLease<byte> bytecode, out nuint byteLength)
            {
                ArgumentNullException.ThrowIfNull(bytecode);
                var sourceLength = bytecode.Length;
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceLength);
                byteLength = checked((nuint)sourceLength);

                var destination = (byte*)NativeMemory.Alloc(byteLength);

                if (destination is null)
                {
                    throw new OutOfMemoryException("D3D12 shader-bytecode copy storage could not be allocated.");
                }

                try
                {
                    nint sourceOffset = 0;

                    while (sourceOffset < sourceLength)
                    {
                        var remaining = sourceLength - sourceOffset;
                        var copyLength = remaining > int.MaxValue
                            ? int.MaxValue
                            : checked((int)remaining);
                        var copyAction = new BytecodeCopyAction(destination + sourceOffset);
                        bytecode.UseWindow(sourceOffset, copyLength, ref copyAction);
                        sourceOffset = checked(sourceOffset + copyLength);
                    }

                    return destination;
                }
                catch
                {
                    NativeMemory.Free(destination);
                    throw;
                }
            }

            private static D3D12_BLEND_DESC CreateBlendState(PipelineBlendMode blendMode)
            {
                var blendEnable = blendMode == PipelineBlendMode.PremultipliedAlpha;
                var renderTarget = new D3D12_RENDER_TARGET_BLEND_DESC {
                    BlendEnable = blendEnable,
                    LogicOpEnable = false,
                    SrcBlend = D3D12_BLEND.D3D12_BLEND_ONE,
                    DestBlend = blendEnable
                        ? D3D12_BLEND.D3D12_BLEND_INV_SRC_ALPHA
                        : D3D12_BLEND.D3D12_BLEND_ZERO,
                    BlendOp = D3D12_BLEND_OP.D3D12_BLEND_OP_ADD,
                    SrcBlendAlpha = D3D12_BLEND.D3D12_BLEND_ONE,
                    DestBlendAlpha = blendEnable
                        ? D3D12_BLEND.D3D12_BLEND_INV_SRC_ALPHA
                        : D3D12_BLEND.D3D12_BLEND_ZERO,
                    BlendOpAlpha = D3D12_BLEND_OP.D3D12_BLEND_OP_ADD,
                    LogicOp = D3D12_LOGIC_OP.D3D12_LOGIC_OP_NOOP,
                    RenderTargetWriteMask = (byte)D3D12_COLOR_WRITE_ENABLE.D3D12_COLOR_WRITE_ENABLE_ALL,
                };
                var blendState = new D3D12_BLEND_DESC {
                    AlphaToCoverageEnable = false,
                    IndependentBlendEnable = false,
                };
                blendState.RenderTarget.e0 = renderTarget;
                return blendState;
            }

            private static ID3D12PipelineState* CreateComputePipelineState(
                ID3D12Device* device,
                ID3D12RootSignature* rootSignature,
                void* bytecode,
                nuint bytecodeLength)
            {
                var nativeDescription = new D3D12_COMPUTE_PIPELINE_STATE_DESC {
                    pRootSignature = rootSignature,
                    CS = new D3D12_SHADER_BYTECODE {
                        pShaderBytecode = bytecode,
                        BytecodeLength = bytecodeLength,
                    },
                    Flags = D3D12_PIPELINE_STATE_FLAGS.D3D12_PIPELINE_STATE_FLAG_NONE,
                };
                var pipelineId = typeof(ID3D12PipelineState).GUID;
                ID3D12PipelineState* pipelineState = null;

                ThrowIfFailed(device->CreateComputePipelineState(&nativeDescription, &pipelineId, (void**)&pipelineState));

                if (pipelineState is null)
                {
                    throw new InvalidOperationException("D3D12 compute-pipeline creation returned no pipeline state.");
                }

                return pipelineState;
            }

            private static D3D12_DEPTH_STENCIL_DESC CreateDepthStencilState(GraphicsPipelineDescription description)
            {
                var depthEnabled = description.DepthMode != PipelineDepthMode.Disabled;
                var stencilOperation = new D3D12_DEPTH_STENCILOP_DESC {
                    StencilFailOp = D3D12_STENCIL_OP.D3D12_STENCIL_OP_KEEP,
                    StencilDepthFailOp = D3D12_STENCIL_OP.D3D12_STENCIL_OP_KEEP,
                    StencilPassOp = D3D12_STENCIL_OP.D3D12_STENCIL_OP_KEEP,
                    StencilFunc = D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_ALWAYS,
                };

                return new D3D12_DEPTH_STENCIL_DESC {
                    DepthEnable = depthEnabled,
                    DepthWriteMask = description.DepthMode == PipelineDepthMode.ReversedGreaterEqualWrite
                        ? D3D12_DEPTH_WRITE_MASK.D3D12_DEPTH_WRITE_MASK_ALL
                        : D3D12_DEPTH_WRITE_MASK.D3D12_DEPTH_WRITE_MASK_ZERO,
                    DepthFunc = depthEnabled
                        ? D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_GREATER_EQUAL
                        : D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_ALWAYS,
                    StencilEnable = false,
                    StencilReadMask = byte.MaxValue,
                    StencilWriteMask = byte.MaxValue,
                    FrontFace = stencilOperation,
                    BackFace = stencilOperation,
                };
            }

            private static ID3D12RootSignature* CreateEmptyRootSignature(ID3D12Device* device)
            {
                var rootSignatureDescription = new D3D12_ROOT_SIGNATURE_DESC {
                    Flags = D3D12_ROOT_SIGNATURE_FLAGS.D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT,
                };
                ID3DBlob* signatureBlob = null;
                ID3DBlob* errorBlob = null;
                ID3D12RootSignature* rootSignature = null;

                try
                {
                    ThrowIfFailed(DirectX.D3D12SerializeRootSignature(
                        &rootSignatureDescription,
                        D3D_ROOT_SIGNATURE_VERSION.D3D_ROOT_SIGNATURE_VERSION_1,
                        &signatureBlob,
                        &errorBlob));

                    if (signatureBlob is null)
                    {
                        throw new InvalidOperationException("D3D12 root-signature serialization returned no signature blob.");
                    }

                    var rootSignatureId = typeof(ID3D12RootSignature).GUID;
                    ThrowIfFailed(device->CreateRootSignature(
                        0,
                        signatureBlob->GetBufferPointer(),
                        signatureBlob->GetBufferSize(),
                        &rootSignatureId,
                        (void**)&rootSignature));

                    if (rootSignature is null)
                    {
                        throw new InvalidOperationException("D3D12 root-signature creation returned no root signature.");
                    }

                    return rootSignature;
                }
                catch
                {
                    if (rootSignature is not null)
                    {
                        _ = rootSignature->Release();
                    }

                    throw;
                }
                finally
                {
                    if (errorBlob is not null)
                    {
                        _ = errorBlob->Release();
                    }

                    if (signatureBlob is not null)
                    {
                        _ = signatureBlob->Release();
                    }
                }
            }

            [SuppressMessage(
                "Usage",
                "CA2201:Do not raise reserved exception types",
                Justification = "Native vertex-input allocation exhaustion is reported as an allocation failure, not successful pipeline creation.")]
            private static ID3D12PipelineState* CreateGraphicsPipelineState(
                ID3D12Device* device,
                ID3D12RootSignature* rootSignature,
                GraphicsPipelineDescription description,
                void* vertexBytecode,
                nuint vertexBytecodeLength,
                void* pixelBytecode,
                nuint pixelBytecodeLength)
            {
                var inputElementCount = description.VertexElements.Length;
                var semanticNamePointers = new IntPtr[inputElementCount];
                var inputElements = inputElementCount == 0
                    ? null
                    : (D3D12_INPUT_ELEMENT_DESC*)NativeMemory.Alloc(
                        checked((nuint)inputElementCount),
                        (nuint)sizeof(D3D12_INPUT_ELEMENT_DESC));

                if ((inputElementCount != 0) && (inputElements is null))
                {
                    throw new OutOfMemoryException("D3D12 vertex-input descriptor storage could not be allocated.");
                }

                try
                {
                    var vertexInputs = description.Shaders[0].Reflection.Inputs;
                    var elementIndex = 0;

                    for (var inputIndex = 0; inputIndex < vertexInputs.Length; inputIndex++)
                    {
                        var input = vertexInputs[inputIndex];

                        if (input.Location is null)
                        {
                            continue;
                        }

                        var element = description.VertexElements[elementIndex];
                        var semanticName = Marshal.StringToCoTaskMemUTF8(input.SemanticName);
                        semanticNamePointers[elementIndex] = semanticName;
                        inputElements[elementIndex] = new D3D12_INPUT_ELEMENT_DESC {
                            SemanticName = (sbyte*)semanticName,
                            SemanticIndex = checked((uint)input.SemanticIndex),
                            Format = GetNativeVertexFormat(element.Format),
                            InputSlot = 0,
                            AlignedByteOffset = checked((uint)element.ByteOffset),
                            InputSlotClass = D3D12_INPUT_CLASSIFICATION.D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,
                            InstanceDataStepRate = 0,
                        };
                        elementIndex++;
                    }

                    var nativeDescription = new D3D12_GRAPHICS_PIPELINE_STATE_DESC {
                        pRootSignature = rootSignature,
                        VS = new D3D12_SHADER_BYTECODE {
                            pShaderBytecode = vertexBytecode,
                            BytecodeLength = vertexBytecodeLength,
                        },
                        PS = new D3D12_SHADER_BYTECODE {
                            pShaderBytecode = pixelBytecode,
                            BytecodeLength = pixelBytecodeLength,
                        },
                        BlendState = CreateBlendState(description.BlendMode),
                        SampleMask = uint.MaxValue,
                        RasterizerState = CreateRasterizerState(description),
                        DepthStencilState = CreateDepthStencilState(description),
                        InputLayout = new D3D12_INPUT_LAYOUT_DESC {
                            pInputElementDescs = inputElements,
                            NumElements = checked((uint)inputElementCount),
                        },
                        PrimitiveTopologyType = D3D12_PRIMITIVE_TOPOLOGY_TYPE.D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE,
                        NumRenderTargets = checked((uint)description.ColorTargetFormats.Length),
                        DSVFormat = description.DepthTargetFormat is TextureFormat depthFormat
                            ? GetNativeFormat(depthFormat)
                            : DXGI_FORMAT.DXGI_FORMAT_UNKNOWN,
                        SampleDesc = new DXGI_SAMPLE_DESC {
                            Count = 1,
                            Quality = 0,
                        },
                        Flags = D3D12_PIPELINE_STATE_FLAGS.D3D12_PIPELINE_STATE_FLAG_NONE,
                    };
                    var nativeTargetFormats = &nativeDescription.RTVFormats.e0;

                    for (var targetIndex = 0; targetIndex < description.ColorTargetFormats.Length; targetIndex++)
                    {
                        nativeTargetFormats[targetIndex] = GetNativeFormat(description.ColorTargetFormats[targetIndex]);
                    }

                    var pipelineId = typeof(ID3D12PipelineState).GUID;
                    ID3D12PipelineState* pipelineState = null;
                    ThrowIfFailed(device->CreateGraphicsPipelineState(
                        &nativeDescription,
                        &pipelineId,
                        (void**)&pipelineState));

                    if (pipelineState is null)
                    {
                        throw new InvalidOperationException("D3D12 graphics-pipeline creation returned no pipeline state.");
                    }

                    return pipelineState;
                }
                finally
                {
                    for (var index = 0; index < semanticNamePointers.Length; index++)
                    {
                        if (semanticNamePointers[index] != IntPtr.Zero)
                        {
                            Marshal.FreeCoTaskMem(semanticNamePointers[index]);
                        }
                    }

                    NativeMemory.Free(inputElements);
                }
            }

            private static D3D12_RASTERIZER_DESC CreateRasterizerState(GraphicsPipelineDescription description)
                => new() {
                    FillMode = D3D12_FILL_MODE.D3D12_FILL_MODE_SOLID,
                    CullMode = description.CullMode switch {
                        PipelineCullMode.None => D3D12_CULL_MODE.D3D12_CULL_MODE_NONE,
                        PipelineCullMode.Front => D3D12_CULL_MODE.D3D12_CULL_MODE_FRONT,
                        PipelineCullMode.Back => D3D12_CULL_MODE.D3D12_CULL_MODE_BACK,
                        _ => throw new ArgumentOutOfRangeException(nameof(description)),
                    },
                    FrontCounterClockwise = !description.FrontFaceClockwise,
                    DepthBias = 0,
                    DepthBiasClamp = 0.0f,
                    SlopeScaledDepthBias = 0.0f,
                    DepthClipEnable = true,
                    MultisampleEnable = false,
                    AntialiasedLineEnable = false,
                    ForcedSampleCount = 0,
                    ConservativeRaster = D3D12_CONSERVATIVE_RASTERIZATION_MODE.D3D12_CONSERVATIVE_RASTERIZATION_MODE_OFF,
                };

            private static DXGI_FORMAT GetNativeVertexFormat(VertexElementFormat format)
                => format switch {
                    VertexElementFormat.Float2 => DXGI_FORMAT.DXGI_FORMAT_R32G32_FLOAT,
                    VertexElementFormat.Float3 => DXGI_FORMAT.DXGI_FORMAT_R32G32B32_FLOAT,
                    VertexElementFormat.Float4 => DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT,
                    VertexElementFormat.Half2 => DXGI_FORMAT.DXGI_FORMAT_R16G16_FLOAT,
                    VertexElementFormat.Half4 => DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT,
                    VertexElementFormat.UNorm8x4 => DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM,
                    _ => throw new ArgumentOutOfRangeException(nameof(format)),
                };

            private readonly unsafe struct BytecodeCopyAction : IAction<ReadOnlySpan<byte>>
            {
                private readonly byte* _destination;

                internal BytecodeCopyAction(byte* destination)
                {
                    _destination = destination;
                }

                public readonly void Invoke(scoped ReadOnlySpan<byte> argument)
                    => argument.CopyTo(new Span<byte>(_destination, argument.Length));
            }

            private sealed unsafe class D3D12BackendPipeline : IGraphicsBackendPipeline
            {
                private ID3D12PipelineState* _pipelineState;
                private ID3D12RootSignature* _rootSignature;

                internal D3D12BackendPipeline(
                    ID3D12PipelineState* pipelineState,
                    ID3D12RootSignature* rootSignature)
                {
                    if (pipelineState is null)
                    {
                        throw new ArgumentNullException(nameof(pipelineState));
                    }

                    if (rootSignature is null)
                    {
                        throw new ArgumentNullException(nameof(rootSignature));
                    }

                    _pipelineState = pipelineState;
                    _rootSignature = rootSignature;
                }

                public void Dispose()
                {
                    var pipelineState = _pipelineState;
                    var rootSignature = _rootSignature;
                    _pipelineState = null;
                    _rootSignature = null;

                    if (pipelineState is not null)
                    {
                        _ = pipelineState->Release();
                    }

                    if (rootSignature is not null)
                    {
                        _ = rootSignature->Release();
                    }
                }
            }
        }

        private sealed unsafe class D3D12TransferQueue : IDisposable
        {
            private const uint TransferFenceTimeoutMilliseconds = 30_000;

            private ID3D12CommandAllocator* _commandAllocator;
            private ID3D12GraphicsCommandList* _commandList;
            private ID3D12CommandQueue* _commandQueue;
            private ID3D12Device* _device;
            private ID3D12Fence* _fence;
            private HANDLE _fenceEvent;
            private bool _hasUncertainSubmission;
            private bool _isDisposed;
            private bool _isRecording;
            private ulong _nextFenceValue;
            private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;

            internal D3D12TransferQueue(ID3D12Device* device)
            {
                if (device is null)
                {
                    throw new ArgumentNullException(nameof(device));
                }

                var queueDescription = new D3D12_COMMAND_QUEUE_DESC {
                    Type = D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT,
                    Priority = (int)D3D12_COMMAND_QUEUE_PRIORITY.D3D12_COMMAND_QUEUE_PRIORITY_NORMAL,
                    Flags = D3D12_COMMAND_QUEUE_FLAGS.D3D12_COMMAND_QUEUE_FLAG_NONE,
                    NodeMask = 0,
                };
                var queueId = typeof(ID3D12CommandQueue).GUID;
                var allocatorId = typeof(ID3D12CommandAllocator).GUID;
                var commandListId = typeof(ID3D12GraphicsCommandList).GUID;
                var fenceId = typeof(ID3D12Fence).GUID;
                ID3D12CommandQueue* commandQueue = null;
                ID3D12CommandAllocator* commandAllocator = null;
                ID3D12GraphicsCommandList* commandList = null;
                ID3D12Fence* fence = null;

                try
                {
                    ThrowIfFailed(device->CreateCommandQueue(&queueDescription, &queueId, (void**)&commandQueue));
                    ThrowIfFailed(device->CreateCommandAllocator(queueDescription.Type, &allocatorId, (void**)&commandAllocator));
                    ThrowIfFailed(device->CreateCommandList(
                        0,
                        queueDescription.Type,
                        commandAllocator,
                        null,
                        &commandListId,
                        (void**)&commandList));

                    if ((commandQueue is null) || (commandAllocator is null) || (commandList is null))
                    {
                        throw new InvalidOperationException("D3D12 transfer queue creation returned an incomplete command owner.");
                    }

                    ThrowIfFailed(commandList->Close());
                    ThrowIfFailed(device->CreateFence(0, D3D12_FENCE_FLAGS.D3D12_FENCE_FLAG_NONE, &fenceId, (void**)&fence));

                    if (fence is null)
                    {
                        throw new InvalidOperationException("D3D12 transfer fence creation returned no fence.");
                    }

                    var fenceEvent = Windows.CreateEventW(null, false, false, null);

                    if (fenceEvent.Value is null)
                    {
                        ThrowLastWin32Error("Creating the D3D12 transfer fence event failed.");
                    }

                    _commandQueue = commandQueue;
                    _commandAllocator = commandAllocator;
                    _commandList = commandList;
                    _fence = fence;
                    _fenceEvent = fenceEvent;
                    _device = device;
                    commandQueue = null;
                    commandAllocator = null;
                    commandList = null;
                    fence = null;
                }
                catch
                {
                    if (fence is not null)
                    {
                        _ = fence->Release();
                    }

                    if (commandList is not null)
                    {
                        _ = commandList->Release();
                    }

                    if (commandAllocator is not null)
                    {
                        _ = commandAllocator->Release();
                    }

                    if (commandQueue is not null)
                    {
                        _ = commandQueue->Release();
                    }

                    throw;
                }
            }

            internal bool HasUncertainSubmission => _hasUncertainSubmission;

            private static void ThrowLastWin32Error(string message)
            {
                var error = Marshal.GetHRForLastWin32Error();

                if (error != 0)
                {
                    Marshal.ThrowExceptionForHR(error);
                }

                throw new InvalidOperationException(message);
            }

            public void Dispose()
            {
                if (_isDisposed)
                {
                    return;
                }

                if (_hasUncertainSubmission)
                {
                    throw new InvalidOperationException("The D3D12 transfer queue cannot be released while submitted work is uncertain.");
                }

                EnsureOwnerThread();

                if ((_fenceEvent.Value is not null) && !Windows.CloseHandle(_fenceEvent))
                {
                    ThrowLastWin32Error("Closing the D3D12 transfer fence event failed.");
                }

                _fenceEvent = default;

                var fence = _fence;
                var commandList = _commandList;
                var commandAllocator = _commandAllocator;
                var commandQueue = _commandQueue;
                _fence = null;
                _commandList = null;
                _commandAllocator = null;
                _commandQueue = null;
                _device = null;
                _isDisposed = true;

                if (fence is not null)
                {
                    _ = fence->Release();
                }

                if (commandList is not null)
                {
                    _ = commandList->Release();
                }

                if (commandAllocator is not null)
                {
                    _ = commandAllocator->Release();
                }

                if (commandQueue is not null)
                {
                    _ = commandQueue->Release();
                }
            }

            internal ID3D12GraphicsCommandList* BeginRecording()
            {
                EnsureOwnerThread();

                if (_hasUncertainSubmission)
                {
                    throw new InvalidOperationException("The D3D12 transfer queue is quarantined after an uncertain submission.");
                }

                if (_isRecording)
                {
                    throw new InvalidOperationException("A D3D12 transfer command list is already recording.");
                }

                ThrowIfFailed(_commandAllocator->Reset());
                ThrowIfFailed(_commandList->Reset(_commandAllocator, null));
                _isRecording = true;
                return _commandList;
            }

            internal void StopRecording()
            {
                EnsureOwnerThread();

                if (_isRecording)
                {
                    _isRecording = false;
                    ThrowIfFailed(_commandList->Close());
                }
            }

            internal long SubmitAndWait()
            {
                EnsureOwnerThread();

                if (!_isRecording)
                {
                    throw new InvalidOperationException("The D3D12 transfer command list is not recording.");
                }

                ThrowIfFailed(_commandList->Close());
                _isRecording = false;

                if (_nextFenceValue >= long.MaxValue)
                {
                    throw new InvalidOperationException("The D3D12 transfer fence value space is exhausted.");
                }

                var fenceValue = ++_nextFenceValue;
                _hasUncertainSubmission = true;
                var commandLists = stackalloc ID3D12CommandList*[1];
                commandLists[0] = (ID3D12CommandList*)_commandList;
                _commandQueue->ExecuteCommandLists(1, commandLists);
                var signalResult = _commandQueue->Signal(_fence, fenceValue);

                if (signalResult.FAILED)
                {
                    throw new GraphicsDeviceLostException(signalResult.Value);
                }

                var eventResult = _fence->SetEventOnCompletion(fenceValue, _fenceEvent);

                if (eventResult.FAILED)
                {
                    throw new GraphicsDeviceLostException(eventResult.Value);
                }

                var waitResult = Windows.WaitForSingleObject(_fenceEvent, TransferFenceTimeoutMilliseconds);

                if (waitResult == 0xFFFFFFFF)
                {
                    var error = Marshal.GetHRForLastWin32Error();
                    throw new GraphicsDeviceLostException(error != 0 ? error : unchecked((int)0x80004005));
                }

                if (waitResult == 0x00000102)
                {
                    throw new GraphicsDeviceLostException(unchecked((int)0x800705B4));
                }

                if (waitResult != 0)
                {
                    throw new GraphicsDeviceLostException(unchecked((int)0x80004005));
                }

                var completedFenceValue = _fence->GetCompletedValue();

                if (completedFenceValue == ulong.MaxValue)
                {
                    var deviceRemovedReason = _device->GetDeviceRemovedReason();
                    throw new GraphicsDeviceLostException(
                        deviceRemovedReason.FAILED
                            ? deviceRemovedReason.Value
                            : DxgiErrorDeviceRemoved);
                }

                if (completedFenceValue < fenceValue)
                {
                    throw new GraphicsDeviceLostException(unchecked((int)0x80004005));
                }

                _hasUncertainSubmission = false;
                return checked((long)fenceValue);
            }

            private void EnsureOwnerThread()
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);

                if (Environment.CurrentManagedThreadId != _ownerThreadId)
                {
                    throw new InvalidOperationException("D3D12 transfer commands must execute on their owning thread.");
                }
            }
        }
    }
}
