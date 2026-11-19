// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
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

    internal sealed unsafe class D3D12BackendFactory : IGraphicsBackendFactory
    {
        private const int DxgiErrorDeviceHung = unchecked((int)0x887A0006);
        private const int DxgiErrorDeviceRemoved = unchecked((int)0x887A0005);
        private const int DxgiErrorDeviceReset = unchecked((int)0x887A0007);
        private const int DxgiErrorDriverInternalError = unchecked((int)0x887A0020);
        private const int DxgiErrorNotFound = unchecked((int)0x887A0002);
        private const uint MaximumD3D12MultisampleCount = 32;
        private readonly Func<long, long>? _completedFenceValueOverride;
        private readonly Action<GraphicsGraphSubmission>? _graphSubmissionObserver;
        private readonly Func<nint, uint, uint>? _graphWaitForSingleObject;
        private readonly Func<GpuMemoryKind, long?>? _stagingBudgetBytesOverride;
        private readonly Func<int?>? _surfacePresentResultOverride;
        private readonly Func<nint, uint, uint>? _waitForSingleObject;

        internal D3D12BackendFactory(
            Func<nint, uint, uint>? waitForSingleObject = null,
            Func<nint, uint, uint>? graphWaitForSingleObject = null,
            Func<long, long>? completedFenceValueOverride = null,
            Action<GraphicsGraphSubmission>? graphSubmissionObserver = null,
            Func<int?>? surfacePresentResultOverride = null,
            Func<GpuMemoryKind, long?>? stagingBudgetBytesOverride = null)
        {
            _waitForSingleObject = waitForSingleObject;
            _graphWaitForSingleObject = graphWaitForSingleObject;
            _completedFenceValueOverride = completedFenceValueOverride;
            _graphSubmissionObserver = graphSubmissionObserver;
            _surfacePresentResultOverride = surfacePresentResultOverride;
            _stagingBudgetBytesOverride = stagingBudgetBytesOverride;
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
                maximumSampledImagesPerPipeline: 64,
                maximumSamplersPerStage: (int)D3D12Api.D3D12_COMMONSHADER_SAMPLER_SLOT_COUNT,
                maximumSamplersPerPipeline: 32,
                maximumSamplerAnisotropy: 1.0f,
                maximumUniformBuffersPerStage: (int)D3D12Api.D3D12_COMMONSHADER_CONSTANT_BUFFER_API_SLOT_COUNT,
                maximumUniformBuffersPerPipeline: 16,
                maximumStorageBuffersPerStage: 32,
                maximumStorageImagesPerStage: 32,
                maximumStorageBuffersPerPipeline: 32,
                maximumStorageImagesPerPipeline: 32,
                maximumStorageResourcesPerPipeline: 32,
                maximumResourcesPerStage: 144,
                maximumUniformBufferRangeBytes: 65536,
                uniformBufferOffsetAlignment: D3D12Api.D3D12_CONSTANT_BUFFER_DATA_PLACEMENT_ALIGNMENT,
                storageBufferOffsetAlignment: D3D12Api.D3D12_RAW_UAV_SRV_BYTE_ALIGNMENT,
                bufferCopyOffsetAlignment: sizeof(uint),
                textureRowPitchAlignment: D3D12Api.D3D12_TEXTURE_DATA_PITCH_ALIGNMENT,
                texturePlacementAlignment: D3D12Api.D3D12_TEXTURE_DATA_PLACEMENT_ALIGNMENT,
                maximumComputeWorkGroupSizeX: 1024,
                maximumComputeWorkGroupSizeY: 1024,
                maximumComputeWorkGroupSizeZ: 64,
                maximumComputeWorkGroupCountX: 65535,
                maximumComputeWorkGroupCountY: 65535,
                maximumComputeWorkGroupCountZ: 65535,
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
                Format = GetNativeTextureResourceFormat(description),
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
            var deviceId = typeof(IDXGIDevice).GUID;
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
                var formatSupport = GetFormatSupport(device, nativeFormat);

                if ((formatSupport.Support1 & D3D12_FORMAT_SUPPORT1.D3D12_FORMAT_SUPPORT1_TEXTURE2D) == 0)
                {
                    continue;
                }

                var usages = TextureUsage.CopySource | TextureUsage.CopyDestination;

                if (format == TextureFormat.D32Float)
                {
                    var typelessSupport = GetFormatSupport(device, DXGI_FORMAT.DXGI_FORMAT_R32_TYPELESS);
                    var sampledViewSupport = GetFormatSupport(device, DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT);
                    const D3D12_FORMAT_SUPPORT1 requiredSampledViewSupport =
                        D3D12_FORMAT_SUPPORT1.D3D12_FORMAT_SUPPORT1_TEXTURE2D
                        | D3D12_FORMAT_SUPPORT1.D3D12_FORMAT_SUPPORT1_SHADER_SAMPLE;

                    if (((typelessSupport.Support1 & D3D12_FORMAT_SUPPORT1.D3D12_FORMAT_SUPPORT1_TEXTURE2D) != 0)
                        && ((sampledViewSupport.Support1 & requiredSampledViewSupport) == requiredSampledViewSupport))
                    {
                        usages |= TextureUsage.Sampled;
                    }
                }
                else if ((formatSupport.Support1 & D3D12_FORMAT_SUPPORT1.D3D12_FORMAT_SUPPORT1_SHADER_SAMPLE) != 0)
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

        private static D3D12_FEATURE_DATA_FORMAT_SUPPORT GetFormatSupport(ID3D12Device* device, DXGI_FORMAT format)
        {
            var support = new D3D12_FEATURE_DATA_FORMAT_SUPPORT {
                Format = format,
            };
            ThrowIfFailed(device->CheckFeatureSupport(
                D3D12_FEATURE.D3D12_FEATURE_FORMAT_SUPPORT,
                &support,
                (uint)sizeof(D3D12_FEATURE_DATA_FORMAT_SUPPORT)));
            return support;
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

        private static DXGI_FORMAT GetNativeTextureResourceFormat(TextureDescription description)
            => (description.Format == TextureFormat.D32Float)
                && ((description.Usage & TextureUsage.Sampled) != 0)
                ? DXGI_FORMAT.DXGI_FORMAT_R32_TYPELESS
                : GetNativeFormat(description.Format);

        private static ImmutableArray<int> GetSampleCounts(ID3D12Device* device, DXGI_FORMAT format)
        {
            var sampleCounts = ImmutableArray.CreateBuilder<int>();

            for (uint sampleCount = 1; sampleCount <= MaximumD3D12MultisampleCount; sampleCount *= 2)
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
            var support = GetFormatSupport(device, GetNativeTextureResourceFormat(description));

            var dimensionSupport = description.Dimension == TextureDimension.Dimension2D
                ? D3D12_FORMAT_SUPPORT1.D3D12_FORMAT_SUPPORT1_TEXTURE2D
                : D3D12_FORMAT_SUPPORT1.D3D12_FORMAT_SUPPORT1_TEXTURE3D;

            if ((support.Support1 & dimensionSupport) == 0)
            {
                throw new NotSupportedException($"D3D12 does not support {description.Dimension} textures in format {description.Format}.");
            }

            if (description.Format == TextureFormat.D32Float)
            {
                if ((description.Usage & TextureUsage.Sampled) != 0)
                {
                    var sampledViewSupport = GetFormatSupport(device, DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT);
                    const D3D12_FORMAT_SUPPORT1 requiredSampledViewSupport =
                        D3D12_FORMAT_SUPPORT1.D3D12_FORMAT_SUPPORT1_TEXTURE2D
                        | D3D12_FORMAT_SUPPORT1.D3D12_FORMAT_SUPPORT1_SHADER_SAMPLE;

                    if ((sampledViewSupport.Support1 & requiredSampledViewSupport) != requiredSampledViewSupport)
                    {
                        throw new NotSupportedException("D3D12 does not support sampling D32Float through an R32Float view.");
                    }
                }

                if ((description.Usage & TextureUsage.DepthAttachment) != 0)
                {
                    var depthViewSupport = GetFormatSupport(device, DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT);
                    const D3D12_FORMAT_SUPPORT1 requiredDepthViewSupport =
                        D3D12_FORMAT_SUPPORT1.D3D12_FORMAT_SUPPORT1_TEXTURE2D
                        | D3D12_FORMAT_SUPPORT1.D3D12_FORMAT_SUPPORT1_DEPTH_STENCIL;

                    if ((depthViewSupport.Support1 & requiredDepthViewSupport) != requiredDepthViewSupport)
                    {
                        throw new NotSupportedException("D3D12 does not support D32Float depth-stencil views.");
                    }
                }

                return;
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
            int frameSlotCount,
            CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfZero(ownerId);
            ArgumentOutOfRangeException.ThrowIfLessThan(frameSlotCount, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(frameSlotCount, 4);
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
                                failures.Add(
                                    $"{capabilities.AdapterName} "
                                    + $"(vendor {capabilities.VendorId:X4}, device {capabilities.DeviceId:X4}, "
                                    + $"driver {capabilities.DriverVersion}): {string.Join("; ", missingRequirements)}");
                                continue;
                            }

                            var backendDevice = new D3D12BackendDevice(
                                device,
                                adapter3,
                                capabilities,
                                frameSlotCount,
                                _waitForSingleObject,
                                _graphWaitForSingleObject,
                                _completedFenceValueOverride,
                                _graphSubmissionObserver,
                                _surfacePresentResultOverride,
                                _stagingBudgetBytesOverride);
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

        internal sealed unsafe class D3D12BackendDevice : IGraphicsBackendDevice
        {
            private const uint DefaultShader4ComponentMapping = 0x1688;
            private const int MaximumActiveBufferReadbackCount = 256;
            private const long MaximumDedicatedStagingAllocationBytes = 512L * 1024 * 1024;
            private const int ResourceDescriptorCapacity = 1024;
            private const int SamplerDescriptorCapacity = 256;
            private const long StagingRingCapacityBytes = 64L * 1024 * 1024;

            private readonly List<D3D12BufferReadback> _activeBufferReadbacks = [];
            private IDXGIAdapter3* _adapter;
            private readonly Func<long, long>? _completedFenceValueOverride;
            private ID3D12Device* _device;
            private readonly int[] _graphFrameDescriptorSetCounts;
            private readonly D3D12BackendDescriptorSet[]?[] _graphFrameDescriptorSets;
            private readonly long[] _graphFrameFenceValues;
            private readonly nint[] _graphFrameRenderTargetHeaps;
            private readonly int _graphFrameSlotCount;
            private readonly Action<GraphicsGraphSubmission>? _graphSubmissionObserver;
            private readonly Func<nint, uint, uint>? _graphWaitForSingleObject;
            private int _liveDescriptorSetCount;
            private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
            private ID3D12Resource* _quarantinedStagingResource;
            private readonly GraphicsStagingRingAllocator _readbackStagingAllocator;
            private ID3D12Resource* _readbackStagingResource;
            private D3D12DescriptorRangeAllocator? _resourceDescriptorAllocator;
            private ID3D12DescriptorHeap* _resourceDescriptorHeap;
            private uint _resourceDescriptorIncrementSize;
            private D3D12DescriptorRangeAllocator? _samplerDescriptorAllocator;
            private ID3D12DescriptorHeap* _samplerDescriptorHeap;
            private uint _samplerDescriptorIncrementSize;
            private readonly Func<GpuMemoryKind, long?>? _stagingBudgetBytesOverride;
            private readonly Func<int?>? _surfacePresentResultOverride;
            private D3D12BackendFactory.D3D12TransferQueue? _transferQueue;
            private readonly List<D3D12BackendDescriptorSet> _uncertainGraphDescriptorSets = [];
            private readonly List<nint> _uncertainGraphRenderTargetHeaps = [];
            private readonly GraphicsStagingRingAllocator _uploadStagingAllocator;
            private byte* _uploadStagingMapped;
            private ID3D12Resource* _uploadStagingResource;
            private readonly Func<nint, uint, uint>? _waitForSingleObject;

            internal D3D12BackendDevice(
                ID3D12Device* device,
                IDXGIAdapter3* adapter,
                GraphicsCapabilities capabilities,
                int frameSlotCount,
                Func<nint, uint, uint>? waitForSingleObject,
                Func<nint, uint, uint>? graphWaitForSingleObject,
                Func<long, long>? completedFenceValueOverride,
                Action<GraphicsGraphSubmission>? graphSubmissionObserver,
                Func<int?>? surfacePresentResultOverride,
                Func<GpuMemoryKind, long?>? stagingBudgetBytesOverride)
            {
                ArgumentNullException.ThrowIfNull(capabilities);

                if (device is null)
                {
                    throw new ArgumentNullException(nameof(device));
                }

                _adapter = adapter;
                _device = device;
                Capabilities = capabilities;
                _graphFrameSlotCount = frameSlotCount;
                _graphFrameDescriptorSetCounts = new int[frameSlotCount];
                _graphFrameDescriptorSets = new D3D12BackendDescriptorSet[]?[frameSlotCount];
                _graphFrameFenceValues = new long[frameSlotCount];
                _graphFrameRenderTargetHeaps = new nint[frameSlotCount];
                _completedFenceValueOverride = completedFenceValueOverride;
                _graphSubmissionObserver = graphSubmissionObserver;
                _graphWaitForSingleObject = graphWaitForSingleObject;
                _waitForSingleObject = waitForSingleObject;
                _readbackStagingAllocator = new GraphicsStagingRingAllocator(StagingRingCapacityBytes);
                _surfacePresentResultOverride = surfacePresentResultOverride;
                _stagingBudgetBytesOverride = stagingBudgetBytesOverride;
                _uploadStagingAllocator = new GraphicsStagingRingAllocator(StagingRingCapacityBytes);
            }

            public GraphicsCapabilities Capabilities { get; }

            internal long? AvailableUploadBudgetBytes
            {
                get
                {
                    EnsureOwnerThread();
                    return GetAvailableBudgetBytes(GpuMemoryKind.Upload);
                }
            }

            internal bool HasUncertainTransferSubmission
                => _transferQueue?.HasUncertainSubmission == true;

            internal bool HasUploadStagingRing
                => _uploadStagingResource is not null;

            bool IGraphicsBackendDevice.HasUncertainSubmission
                => _transferQueue?.HasUncertainSubmission == true;

            private static void CopyMappedStorageToLease(byte* source, nint byteLength, BufferWriteLease<byte> destination)
            {
                nint copiedBytes = 0;

                while (copiedBytes < byteLength)
                {
                    var remainingBytes = byteLength - copiedBytes;
                    var copyLength = remainingBytes > int.MaxValue
                        ? int.MaxValue
                        : checked((int)remainingBytes);
                    var copyAction = new ReadbackCopyAction(source + copiedBytes);
                    destination.UseWindow(copiedBytes, copyLength, ref copyAction);
                    copiedBytes = checked(copiedBytes + copyLength);
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

            private static bool MatchesReflectedResource(
                ImmutableArray<ShaderResourceSignature> resources,
                ShaderBindingGroup group,
                int binding,
                ShaderResourceKind kind)
            {
                for (var index = 0; index < resources.Length; index++)
                {
                    var resource = resources[index];

                    if ((resource.Group == group)
                        && (resource.Binding == binding)
                        && (resource.Kind == kind)
                        && (resource.ArrayCount == 1))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static void ReleaseStagingRange(
                GraphicsStagingRingAllocator allocator,
                GraphicsStagingRingAllocator.Reservation reservation,
                long? completedFenceValue,
                bool hasUncertainSubmission)
            {
                if (!reservation.IsValid || hasUncertainSubmission)
                {
                    return;
                }

                if (completedFenceValue is long fenceValue)
                {
                    allocator.RetireCompleted(reservation, fenceValue);
                }
                else
                {
                    allocator.Cancel(reservation);
                }
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

            [SuppressMessage(
                "Usage",
                "CA1031:Do not catch general exception types",
                Justification = "A failed readback closes an unsubmitted command list before releasing its staging owner.")]
            public IGraphicsBackendBufferReadback BeginBufferReadback(
                IGraphicsBackendBuffer source,
                GraphResourceState sourceState,
                long sourceOffset,
                nint byteLength)
            {
                EnsureOwnerThread();
                var sourceBuffer = GetTransferBuffer(source, BufferUsage.CopySource, nameof(source));
                ValidateTransferRange(sourceBuffer.Description.ByteLength, sourceOffset, byteLength, nameof(sourceOffset), nameof(byteLength));
                var transferQueue = GetTransferQueue();
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
                    _activeBufferReadbacks.Count,
                    MaximumActiveBufferReadbackCount);
                _ = _activeBufferReadbacks.EnsureCapacity(_activeBufferReadbacks.Count + 1);
                var stagingResource = GetReadbackStaging(
                    byteLength,
                    alignment: sizeof(uint),
                    out var isDedicated,
                    out var stagingOffset,
                    out var stagingReservation);
                ID3D12CommandAllocator* commandAllocator = null;
                ID3D12GraphicsCommandList* commandList = null;
                D3D12BufferReadback? readback = null;
                var readbackRegistered = false;
                var submittedFence = 0L;

                try
                {
                    readback = new D3D12BufferReadback(
                        this,
                        transferQueue,
                        stagingResource,
                        isDedicated,
                        stagingOffset,
                        byteLength,
                        stagingReservation);
                    _activeBufferReadbacks.Add(readback);
                    readbackRegistered = true;
                    commandList = transferQueue.BeginReadbackRecording(out commandAllocator);
                    readback.SetCommandStorage(commandAllocator, commandList);
                    var sourceResource = sourceBuffer.GetResource();
                    var initialState = D3D12GraphResourceStateMapper.GetBufferState(sourceState, sourceBuffer.Description);
                    var copySourceState = D3D12GraphResourceStateMapper.GetBufferState(
                        GraphResourceState.CopySource,
                        sourceBuffer.Description);
                    var generalState = D3D12GraphResourceStateMapper.GetBufferState(
                        GraphResourceState.General,
                        sourceBuffer.Description);
                    var requiresTransition = initialState != copySourceState;

                    if (requiresTransition)
                    {
                        var toCopySource = D3D12_RESOURCE_BARRIER.InitTransition(
                            sourceResource,
                            initialState,
                            copySourceState,
                            0,
                            D3D12_RESOURCE_BARRIER_FLAGS.D3D12_RESOURCE_BARRIER_FLAG_NONE);
                        commandList->ResourceBarrier(1, &toCopySource);
                    }

                    commandList->CopyBufferRegion(
                        stagingResource,
                        stagingOffset,
                        sourceResource,
                        checked((ulong)sourceOffset),
                        checked((ulong)byteLength));

                    if (copySourceState != generalState)
                    {
                        var toCommon = D3D12_RESOURCE_BARRIER.InitTransition(
                            sourceResource,
                            copySourceState,
                            generalState,
                            0,
                            D3D12_RESOURCE_BARRIER_FLAGS.D3D12_RESOURCE_BARRIER_FLAG_NONE);
                        commandList->ResourceBarrier(1, &toCommon);
                    }

                    submittedFence = transferQueue.SubmitReadbackWithoutWait(
                        stagingReservation.IsValid
                            ? fenceValue => _readbackStagingAllocator.MarkSubmitted(stagingReservation, fenceValue)
                            : null);
                    readback.MarkSubmitted(submittedFence);
                    return readback;
                }
                catch (Exception exception)
                {
                    if (transferQueue.IsRecording)
                    {
                        try
                        {
                            transferQueue.StopRecording();
                        }
                        catch (Exception stopException)
                        {
                            transferQueue.MarkUncertainSubmission();
                            exception = new AggregateException(exception, stopException);
                        }
                    }

                    if (transferQueue.HasUncertainSubmission || (submittedFence > 0))
                    {
                        transferQueue.MarkUncertainSubmission();

                        if ((readback is null)
                            && isDedicated
                            && (_quarantinedStagingResource is null))
                        {
                            _quarantinedStagingResource = stagingResource;
                        }
                    }
                    else if (readback is not null)
                    {
                        readback.AbortUnsubmitted(readbackRegistered);
                    }
                    else
                    {
                        if (isDedicated)
                        {
                            ReleaseDedicatedStagingResource(
                                stagingResource,
                                isMapped: false,
                                writtenByteLength: 0,
                                transferQueue);
                        }

                        ReleaseStagingRange(
                            _readbackStagingAllocator,
                            stagingReservation,
                            completedFenceValue: null,
                            hasUncertainSubmission: false);

                        if (commandList is not null)
                        {
                            _ = commandList->Release();
                        }

                        if (commandAllocator is not null)
                        {
                            _ = commandAllocator->Release();
                        }
                    }

                    throw exception;
                }
            }

            public IGraphicsBackendBuffer CreateBuffer(
                BufferDescription description,
                IGraphicsBackendBufferAllocation allocation)
            {
                ArgumentNullException.ThrowIfNull(description);
                ArgumentNullException.ThrowIfNull(allocation);

                if (allocation is not D3D12BackendResourceAllocation selectedAllocation)
                {
                    throw new ArgumentException("The transient buffer allocation belongs to a different graphics backend.", nameof(allocation));
                }

                var resourceDescription = CreateNativeBufferDescription(description);
                var resourceId = typeof(ID3D12Resource).GUID;
                ID3D12Resource* resource = null;

                try
                {
                    ThrowIfFailed(_device->CreatePlacedResource(
                        selectedAllocation.GetHeap(this),
                        0,
                        &resourceDescription,
                        GetInitialResourceState(description.MemoryKind),
                        null,
                        &resourceId,
                        (void**)&resource));

                    if (resource is null)
                    {
                        throw new InvalidOperationException("D3D12 placed buffer creation returned no resource.");
                    }

                    if (description.DebugName.Length > 0)
                    {
                        fixed (char* name = description.DebugName)
                        {
                            ThrowIfFailed(resource->SetName(name));
                        }
                    }

                    var result = new D3D12BackendBuffer(this, resource, description);
                    resource = null;
                    return result;
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
                    return new D3D12BackendBuffer(this, resource, description);
                }
                catch
                {
                    _ = resource->Release();
                    throw;
                }
            }

            public IGraphicsBackendBufferAllocation CreateBufferAllocation(
                BufferDescription description,
                GraphicsResourceAllocationInfo allocationInfo)
            {
                ArgumentNullException.ThrowIfNull(description);
                EnsureOwnerThread();

                if (description.MemoryKind != GpuMemoryKind.DeviceLocal)
                {
                    throw new NotSupportedException("D3D12 transient alias heaps currently support only device-local buffers.");
                }

                var heapDescription = new D3D12_HEAP_DESC {
                    SizeInBytes = checked((ulong)allocationInfo.SizeBytes),
                    Alignment = 0,
                    Properties = GetHeapProperties(description.MemoryKind),
                    Flags = D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE,
                };
                var heapId = typeof(ID3D12Heap).GUID;
                ID3D12Heap* heap = null;

                try
                {
                    ThrowIfFailed(_device->CreateHeap(&heapDescription, &heapId, (void**)&heap));

                    if (heap is null)
                    {
                        throw new InvalidOperationException("D3D12 transient heap creation returned no heap.");
                    }

                    return new D3D12BackendResourceAllocation(this, heap);
                }
                catch
                {
                    if (heap is not null)
                    {
                        _ = heap->Release();
                    }

                    throw;
                }
            }

            public IGraphicsBackendPipeline CreatePipeline(
                PipelineDescription description,
                ReadOnlySpan<BufferReadLease<byte>> bytecodeLeases)
                => D3D12BackendFactory.D3D12PipelineBuilder.Create(this, _device, description, bytecodeLeases);

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
                var descriptorAllocator = _samplerDescriptorAllocator
                    ?? throw new InvalidOperationException("The D3D12 sampler descriptor heap has no free-slot owner.");
                var slot = descriptorAllocator.Allocate(1);

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
                    descriptorAllocator.Release(slot, 1);
                    throw;
                }
            }

            public IGraphicsBackendSurface CreateSurface(GraphicsNativeSurfaceBinding binding, PixelSize clientPixelSize)
            {
                EnsureOwnerThread();

                if (Capabilities.Profile != GraphicsDeviceProfile.Interactive)
                {
                    throw new InvalidOperationException("OffscreenNative devices do not create presentation surfaces.");
                }

                if (binding.PlatformKind != GraphicsPlatformKind.Win32)
                {
                    throw new PlatformNotSupportedException("The D3D12 backend requires a Win32 presentation surface.");
                }

                var transferQueue = GetTransferQueue();
                return D3D12BackendSurface.Create(
                    _device,
                    transferQueue.CommandQueue,
                    binding,
                    clientPixelSize,
                    Capabilities,
                    _surfacePresentResultOverride);
            }

            public IGraphicsBackendTexture CreateTexture(
                TextureDescription description,
                IGraphicsBackendTextureAllocation allocation)
            {
                ArgumentNullException.ThrowIfNull(description);
                ArgumentNullException.ThrowIfNull(allocation);
                ValidateTextureDescriptionSupport(_device, description);

                if (allocation is not D3D12BackendResourceAllocation selectedAllocation)
                {
                    throw new ArgumentException("The transient texture allocation belongs to a different graphics backend.", nameof(allocation));
                }

                var resourceDescription = CreateNativeTextureDescription(description);
                var resourceId = typeof(ID3D12Resource).GUID;
                ID3D12Resource* resource = null;

                try
                {
                    ThrowIfFailed(_device->CreatePlacedResource(
                        selectedAllocation.GetHeap(this),
                        0,
                        &resourceDescription,
                        D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON,
                        null,
                        &resourceId,
                        (void**)&resource));

                    if (resource is null)
                    {
                        throw new InvalidOperationException("D3D12 placed texture creation returned no resource.");
                    }

                    if (description.DebugName.Length > 0)
                    {
                        fixed (char* name = description.DebugName)
                        {
                            ThrowIfFailed(resource->SetName(name));
                        }
                    }

                    var result = new D3D12BackendTexture(this, resource, description);
                    resource = null;
                    return result;
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
                    return new D3D12BackendTexture(this, resource, description);
                }
                catch
                {
                    _ = resource->Release();
                    throw;
                }
            }

            public IGraphicsBackendTextureAllocation CreateTextureAllocation(
                TextureDescription description,
                GraphicsResourceAllocationInfo allocationInfo)
            {
                ArgumentNullException.ThrowIfNull(description);
                EnsureOwnerThread();
                ValidateTextureDescriptionSupport(_device, description);
                var heapDescription = new D3D12_HEAP_DESC {
                    SizeInBytes = checked((ulong)allocationInfo.SizeBytes),
                    Alignment = checked((ulong)allocationInfo.Alignment),
                    Properties = GetHeapProperties(GpuMemoryKind.DeviceLocal),
                    Flags = D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE,
                };
                var heapId = typeof(ID3D12Heap).GUID;
                ID3D12Heap* heap = null;

                try
                {
                    ThrowIfFailed(_device->CreateHeap(&heapDescription, &heapId, (void**)&heap));

                    if (heap is null)
                    {
                        throw new InvalidOperationException("D3D12 transient texture heap creation returned no heap.");
                    }

                    return new D3D12BackendResourceAllocation(this, heap);
                }
                catch
                {
                    if (heap is not null)
                    {
                        _ = heap->Release();
                    }

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

                if (_activeBufferReadbacks.Count != 0)
                {
                    throw new InvalidOperationException("The D3D12 backend still has active buffer-readback owners.");
                }

                if (_quarantinedStagingResource is not null)
                {
                    throw new InvalidOperationException("The D3D12 backend has a staging resource quarantined after uncertain transfer completion.");
                }

                if (_liveDescriptorSetCount != 0)
                {
                    throw new InvalidOperationException("The D3D12 backend still has live descriptor sets.");
                }

                if ((_samplerDescriptorHeap is not null)
                    && (_samplerDescriptorAllocator?.AllocatedCount != 0))
                {
                    throw new InvalidOperationException("The D3D12 backend still has live sampler descriptor owners.");
                }

                if ((_resourceDescriptorHeap is not null)
                    && (_resourceDescriptorAllocator?.AllocatedCount != 0))
                {
                    throw new InvalidOperationException("The D3D12 backend still has live resource descriptor ranges.");
                }

                _ = GetCompletedGraphFence();
                transferQueue?.Dispose();
                _transferQueue = null;

                foreach (var descriptorHeap in _uncertainGraphRenderTargetHeaps)
                {
                    _ = ((ID3D12DescriptorHeap*)descriptorHeap)->Release();
                }

                _uncertainGraphRenderTargetHeaps.Clear();

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
                    _samplerDescriptorAllocator = null;
                }

                if (_resourceDescriptorHeap is not null)
                {
                    _ = _resourceDescriptorHeap->Release();
                    _resourceDescriptorHeap = null;
                    _resourceDescriptorIncrementSize = 0;
                    _resourceDescriptorAllocator = null;
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

            public long GetCompletedGraphFence()
            {
                var completedFence = _transferQueue?.GetCompletedFence() ?? 0;

                for (var index = 0; index < _graphFrameFenceValues.Length; index++)
                {
                    if ((_graphFrameFenceValues[index] == 0) || (_graphFrameFenceValues[index] > completedFence))
                    {
                        continue;
                    }

                    if (_graphFrameDescriptorSets[index] is D3D12BackendDescriptorSet[] descriptorSets)
                    {
                        for (var descriptorIndex = _graphFrameDescriptorSetCounts[index] - 1; descriptorIndex >= 0; descriptorIndex--)
                        {
                            descriptorSets[descriptorIndex].Dispose();
                        }

                        Array.Clear(descriptorSets, 0, _graphFrameDescriptorSetCounts[index]);
                        _graphFrameDescriptorSets[index] = null;
                        _graphFrameDescriptorSetCounts[index] = 0;
                    }

                    if (_graphFrameRenderTargetHeaps[index] != 0)
                    {
                        _ = ((ID3D12DescriptorHeap*)_graphFrameRenderTargetHeaps[index])->Release();
                        _graphFrameRenderTargetHeaps[index] = 0;
                    }

                    _graphFrameFenceValues[index] = 0;
                }

                return completedFence;
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
                Justification = "A failed texture readback closes an unsubmitted command list before releasing its staging owner.")]
            public TextureReadback ReadbackTexture(
                IGraphicsBackendTexture source,
                GraphResourceState sourceState,
                TextureSubresource subresource)
            {
                var sourceTexture = GetTransferTexture(source, TextureUsage.CopySource, nameof(source));
                var extent = GraphicsDevice.GetTextureSubresourceExtent(sourceTexture.Description, subresource);
                var footprint = GetTextureCopyFootprint(sourceTexture.Description, subresource, extent);
                var byteLength = checked((nint)footprint.TotalBytes);
                var transferQueue = GetTransferQueue();
                var stagingResource = GetReadbackStaging(
                    byteLength,
                    D3D12Api.D3D12_TEXTURE_DATA_PLACEMENT_ALIGNMENT,
                    out var isDedicated,
                    out var stagingOffset,
                    out var stagingReservation);
                var placedFootprint = footprint.Layout;
                placedFootprint.Offset = stagingOffset;
                footprint.Layout = placedFootprint;
                var isMapped = false;
                long? completedFenceValue = null;

                try
                {
                    var commandList = transferQueue.BeginRecording();
                    var sourceResource = sourceTexture.GetResource();
                    var subresourceIndex = GetTextureSubresourceIndex(sourceTexture.Description, subresource);
                    var initialState = D3D12GraphResourceStateMapper.GetTextureState(sourceState, sourceTexture.Description);
                    var copySourceState = D3D12GraphResourceStateMapper.GetTextureState(
                        GraphResourceState.CopySource,
                        sourceTexture.Description);
                    var generalState = D3D12GraphResourceStateMapper.GetTextureState(
                        GraphResourceState.General,
                        sourceTexture.Description);

                    if (initialState != copySourceState)
                    {
                        var toCopySource = D3D12_RESOURCE_BARRIER.InitTransition(
                            sourceResource,
                            initialState,
                            copySourceState,
                            subresourceIndex,
                            D3D12_RESOURCE_BARRIER_FLAGS.D3D12_RESOURCE_BARRIER_FLAG_NONE);
                        commandList->ResourceBarrier(1, &toCopySource);
                    }

                    var sourceLocation = CreateSubresourceLocation(sourceResource, subresourceIndex);
                    var destinationLocation = CreateFootprintLocation(stagingResource, footprint.Layout);
                    commandList->CopyTextureRegion(&destinationLocation, 0, 0, 0, &sourceLocation, null);

                    if (copySourceState != generalState)
                    {
                        var toCommon = D3D12_RESOURCE_BARRIER.InitTransition(
                            sourceResource,
                            copySourceState,
                            generalState,
                            subresourceIndex,
                            D3D12_RESOURCE_BARRIER_FLAGS.D3D12_RESOURCE_BARRIER_FLAG_NONE);
                        commandList->ResourceBarrier(1, &toCommon);
                    }

                    completedFenceValue = transferQueue.SubmitAndWait(
                        stagingReservation.IsValid
                            ? fenceValue => _readbackStagingAllocator.MarkSubmitted(stagingReservation, fenceValue)
                            : null);

                    var readRange = new D3D12_RANGE {
                        Begin = checked((nuint)stagingOffset),
                        End = checked((nuint)(stagingOffset + footprint.TotalBytes)),
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

                    ReleaseStagingRange(
                        _readbackStagingAllocator,
                        stagingReservation,
                        completedFenceValue,
                        transferQueue.HasUncertainSubmission);
                }
            }

            [SuppressMessage(
                "Usage",
                "CA1031:Do not catch general exception types",
                Justification = "A failed graph recording must close the unsubmitted command list before releasing its render-target view.")]
            public long SubmitGraph(GraphicsGraphSubmission submission)
            {
                ArgumentNullException.ThrowIfNull(submission);
                submission.ValidateNativeRasterSubmission();
                EnsureOwnerThread();
                _graphSubmissionObserver?.Invoke(submission);
                var target = submission.OutputColorTarget.ResourceOwner.BackendResource as D3D12BackendTexture
                    ?? throw new ArgumentException("The graph output target is not a D3D12 texture.", nameof(submission));
                ObjectDisposedException.ThrowIf(target.IsDisposed, nameof(submission));

                if (!ReferenceEquals(target.Owner, this)
                    || (target.Description.Dimension != TextureDimension.Dimension2D)
                    || ((target.Description.Usage & TextureUsage.ColorAttachment) == 0))
                {
                    throw new NotSupportedException("D3D12 graph submission requires a live 2D color-attachment texture owned by this device.");
                }

                D3D12BackendSurface? surface = null;

                if (submission.SurfaceTarget is Surface surfaceTarget)
                {
                    surface = surfaceTarget.BackendSurface as D3D12BackendSurface
                        ?? throw new NotSupportedException("D3D12 graph submission requires a D3D12-backed Surface.");
                    _ = surface.WaitForFrameLatency(submission.DeviceEpoch);
                }

                var transferQueue = GetTransferQueue();
                _ = GetCompletedGraphFence();
                var descriptorSetCapacity = 0;

                foreach (var pass in submission.Passes)
                {
                    var commandReadState = 0;
                    using var commandRead = pass.ReadCommands(ref commandReadState);

                    foreach (var command in commandRead.Commands)
                    {
                        if (command.Kind == GraphicsCommandKind.SetBindings)
                        {
                            descriptorSetCapacity = checked(descriptorSetCapacity + 1);
                        }
                    }
                }

                var descriptorSets = new D3D12BackendDescriptorSet[descriptorSetCapacity];
                var descriptorSetCount = 0;
                _ = _uncertainGraphDescriptorSets.EnsureCapacity(
                    checked(_uncertainGraphDescriptorSets.Count + descriptorSetCapacity));
                _ = _uncertainGraphRenderTargetHeaps.EnsureCapacity(
                    checked(_uncertainGraphRenderTargetHeaps.Count + 1));
                var renderTargetResourceCount = submission.ColorAttachmentResourceCount;

                var heapDescription = new D3D12_DESCRIPTOR_HEAP_DESC {
                    Type = D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_RTV,
                    NumDescriptors = checked((uint)renderTargetResourceCount),
                    Flags = D3D12_DESCRIPTOR_HEAP_FLAGS.D3D12_DESCRIPTOR_HEAP_FLAG_NONE,
                    NodeMask = 0,
                };
                var heapId = typeof(ID3D12DescriptorHeap).GUID;
                ID3D12DescriptorHeap* renderTargetHeap = null;
                var renderTargetViews = new D3D12_CPU_DESCRIPTOR_HANDLE[renderTargetResourceCount];
                var graphSubmissionAccepted = false;
                var graphFrameSlotIndex = -1;

                try
                {
                    ThrowIfFailed(_device->CreateDescriptorHeap(&heapDescription, &heapId, (void**)&renderTargetHeap));

                    if (renderTargetHeap is null)
                    {
                        throw new InvalidOperationException("D3D12 render-target descriptor heap creation returned no heap.");
                    }

                    var renderTargetView = renderTargetHeap->GetCPUDescriptorHandleForHeapStart();
                    var renderTargetViewIncrementSize = _device->GetDescriptorHandleIncrementSize(
                        D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_RTV);

                    for (var index = 0; index < renderTargetResourceCount; index++)
                    {
                        var resourceId = submission.GetColorAttachmentResourceId(index);
                        var colorTexture = submission.GetTexture(resourceId) as D3D12BackendTexture
                            ?? throw new InvalidOperationException("A graph color attachment is not a D3D12 texture.");
                        var renderTargetViewDescription = new D3D12_RENDER_TARGET_VIEW_DESC {
                            Format = GetNativeFormat(colorTexture.Description.Format),
                            ViewDimension = D3D12_RTV_DIMENSION.D3D12_RTV_DIMENSION_TEXTURE2D,
                            Texture2D = new D3D12_TEX2D_RTV {
                                MipSlice = 0,
                                PlaneSlice = 0,
                            },
                        };
                        _device->CreateRenderTargetView(
                            colorTexture.GetResource(),
                            &renderTargetViewDescription,
                            renderTargetView);
                        renderTargetViews[index] = renderTargetView;
                        renderTargetView.ptr = checked(
                            renderTargetView.ptr + checked((nuint)renderTargetViewIncrementSize));
                    }

                    var commandList = transferQueue.BeginGraphRecording();
                    graphFrameSlotIndex = transferQueue.ActiveGraphFrameSlotIndex;
                    var clearColor = stackalloc float[4];
                    var renderTargetHandles = stackalloc D3D12_CPU_DESCRIPTOR_HANDLE[
                        GraphicsGraphSubmission.MaximumNativeColorAttachmentCount];
                    var viewport = new D3D12_VIEWPORT {
                        TopLeftX = 0.0f,
                        TopLeftY = 0.0f,
                        Width = target.Description.Width,
                        Height = target.Description.Height,
                        MinDepth = 0.0f,
                        MaxDepth = 1.0f,
                    };
                    var scissor = new RECT {
                        left = 0,
                        top = 0,
                        right = checked((int)target.Description.Width),
                        bottom = checked((int)target.Description.Height),
                    };
                    var clearRect = stackalloc RECT[1];

                    foreach (var pass in submission.Passes)
                    {
                        var commandReadState = 0;
                        using var commandRead = pass.ReadCommands(ref commandReadState);
                        var commands = commandRead.Commands;
                        viewport.TopLeftX = 0.0f;
                        viewport.TopLeftY = 0.0f;
                        viewport.Width = target.Description.Width;
                        viewport.Height = target.Description.Height;
                        scissor.left = 0;
                        scissor.top = 0;
                        scissor.right = checked((int)target.Description.Width);
                        scissor.bottom = checked((int)target.Description.Height);
                        RECT* clearRects = null;
                        uint clearRectCount = 0;

                        if (pass.RenderArea is RenderArea renderArea)
                        {
                            viewport.TopLeftX = renderArea.X;
                            viewport.TopLeftY = renderArea.Y;
                            viewport.Width = renderArea.Width;
                            viewport.Height = renderArea.Height;
                            scissor.left = renderArea.X;
                            scissor.top = renderArea.Y;
                            scissor.right = checked(renderArea.X + renderArea.Width);
                            scissor.bottom = checked(renderArea.Y + renderArea.Height);
                            clearRect[0] = scissor;
                            clearRects = clearRect;
                            clearRectCount = 1;
                        }

                        foreach (var aliasingBarrier in submission.AliasingBarriers)
                        {
                            if (aliasingBarrier.PassId != pass.PassId)
                            {
                                continue;
                            }

                            var beforeBuffer = submission.GetBuffer(aliasingBarrier.BeforeResourceId) as D3D12BackendBuffer
                                ?? throw new InvalidOperationException("The graph alias barrier lost its prior D3D12 buffer.");
                            var afterBuffer = submission.GetBuffer(aliasingBarrier.AfterResourceId) as D3D12BackendBuffer
                                ?? throw new InvalidOperationException("The graph alias barrier lost its next D3D12 buffer.");
                            var barrier = D3D12_RESOURCE_BARRIER.InitAliasing(
                                beforeBuffer.GetResource(),
                                afterBuffer.GetResource());
                            commandList->ResourceBarrier(1, &barrier);
                        }

                        foreach (var aliasingBarrier in submission.TextureAliasingBarriers)
                        {
                            if (aliasingBarrier.PassId != pass.PassId)
                            {
                                continue;
                            }

                            var beforeTexture = submission.GetTexture(aliasingBarrier.BeforeResourceId) as D3D12BackendTexture
                                ?? throw new InvalidOperationException("The graph alias barrier lost its prior D3D12 texture.");
                            var afterTexture = submission.GetTexture(aliasingBarrier.AfterResourceId) as D3D12BackendTexture
                                ?? throw new InvalidOperationException("The graph alias barrier lost its next D3D12 texture.");
                            var barrier = D3D12_RESOURCE_BARRIER.InitAliasing(
                                beforeTexture.GetResource(),
                                afterTexture.GetResource());
                            commandList->ResourceBarrier(1, &barrier);
                        }

                        foreach (var import in submission.BufferImports)
                        {
                            if ((import.Version.Description.Usage & BufferUsage.Storage) == 0)
                            {
                                continue;
                            }

                            var resourceId = import.Version.ResourceId;
                            var initialUseNeedsBarrier = (import.InitialState == GraphResourceState.StorageReadWrite)
                                && submission.HasFirstStorageUse(pass.PassId, resourceId);
                            var priorPassNeedsBarrier = submission.HasPriorStorageWriteHazard(pass.PassId, resourceId);

                            if (!initialUseNeedsBarrier && !priorPassNeedsBarrier)
                            {
                                continue;
                            }

                            var buffer = submission.GetBuffer(import.Version.ResourceId) as D3D12BackendBuffer
                                ?? throw new InvalidOperationException("The graph storage hazard lost its D3D12 buffer.");
                            var barrier = D3D12_RESOURCE_BARRIER.InitUAV(buffer.GetResource());
                            commandList->ResourceBarrier(1, &barrier);
                        }

                        for (var importIndex = 1; importIndex < submission.TextureImports.Length; importIndex++)
                        {
                            var import = submission.TextureImports[importIndex];

                            if ((import.Version.Description.Usage & TextureUsage.Storage) == 0)
                            {
                                continue;
                            }

                            var resourceId = import.Version.ResourceId;
                            var initialUseNeedsBarrier = (import.InitialState == GraphResourceState.StorageReadWrite)
                                && submission.HasFirstStorageUse(pass.PassId, resourceId);
                            var priorPassNeedsBarrier = submission.HasPriorStorageWriteHazard(pass.PassId, resourceId);

                            if (!initialUseNeedsBarrier && !priorPassNeedsBarrier)
                            {
                                continue;
                            }

                            var texture = submission.GetTexture(import.Version.ResourceId) as D3D12BackendTexture
                                ?? throw new InvalidOperationException("The graph storage hazard lost its D3D12 texture.");
                            var barrier = D3D12_RESOURCE_BARRIER.InitUAV(texture.GetResource());
                            commandList->ResourceBarrier(1, &barrier);
                        }

                        foreach (var transition in submission.StateTransitions)
                        {
                            if (transition.PassId != pass.PassId)
                            {
                                continue;
                            }

                            if (transition.IsTexture)
                            {
                                var texture = submission.GetTexture(transition.ResourceId) as D3D12BackendTexture
                                    ?? throw new InvalidOperationException("The graph transition lost its D3D12 texture.");
                                var beforeState = D3D12GraphResourceStateMapper.GetTextureState(
                                    transition.BeforeState,
                                    texture.Description);
                                var afterState = D3D12GraphResourceStateMapper.GetTextureState(
                                    transition.AfterState,
                                    texture.Description);

                                if (beforeState != afterState)
                                {
                                    var subresource = checked(
                                        (uint)(transition.TextureRange.FirstMip
                                            + (transition.TextureRange.FirstLayer * texture.Description.MipLevels)));
                                    var barrier = D3D12_RESOURCE_BARRIER.InitTransition(
                                        texture.GetResource(),
                                        beforeState,
                                        afterState,
                                        subresource,
                                        D3D12_RESOURCE_BARRIER_FLAGS.D3D12_RESOURCE_BARRIER_FLAG_NONE);
                                    commandList->ResourceBarrier(1, &barrier);
                                }
                            }
                            else
                            {
                                var buffer = submission.GetBuffer(transition.ResourceId) as D3D12BackendBuffer
                                    ?? throw new InvalidOperationException("The graph transition lost its D3D12 buffer.");
                                var beforeState = D3D12GraphResourceStateMapper.GetBufferState(
                                    transition.BeforeState,
                                    buffer.Description);
                                var afterState = D3D12GraphResourceStateMapper.GetBufferState(
                                    transition.AfterState,
                                    buffer.Description);

                                if (beforeState != afterState)
                                {
                                    var barrier = D3D12_RESOURCE_BARRIER.InitTransition(
                                        buffer.GetResource(),
                                        beforeState,
                                        afterState,
                                        0,
                                        D3D12_RESOURCE_BARRIER_FLAGS.D3D12_RESOURCE_BARRIER_FLAG_NONE);
                                    commandList->ResourceBarrier(1, &barrier);
                                }
                            }
                        }

                        var depthStencilView = default(D3D12_CPU_DESCRIPTOR_HANDLE);
                        var hasDepthAttachment = false;

                        foreach (var attachment in pass.Attachments)
                        {
                            if (attachment.IsDepth)
                            {
                                var depthTexture = submission.GetTexture(attachment.Version.ResourceId) as D3D12BackendTexture
                                    ?? throw new InvalidOperationException("The graph depth attachment is not a D3D12 texture.");
                                depthStencilView = depthTexture.GetDepthStencilView();
                                hasDepthAttachment = true;

                                if (attachment.LoadAction == AttachmentLoadAction.Clear)
                                {
                                    commandList->ClearDepthStencilView(
                                        depthStencilView,
                                        D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_DEPTH,
                                        attachment.ClearDepth,
                                        0,
                                        clearRectCount,
                                        clearRects);
                                }
                            }
                            else
                            {
                                if (attachment.LoadAction == AttachmentLoadAction.Clear)
                                {
                                    var attachmentRenderTargetView =
                                        renderTargetViews[submission.GetColorAttachmentResourceIndex(attachment.Version.ResourceId)];
                                    clearColor[0] = attachment.ClearColor.X;
                                    clearColor[1] = attachment.ClearColor.Y;
                                    clearColor[2] = attachment.ClearColor.Z;
                                    clearColor[3] = attachment.ClearColor.W;
                                    commandList->ClearRenderTargetView(
                                        attachmentRenderTargetView,
                                        clearColor,
                                        clearRectCount,
                                        clearRects);
                                }
                            }
                        }

                        if (pass.Kind == CommandRecorderPassKind.Presentation)
                        {
                            surface?.RecordPresentationCopy(commandList, target.GetResource());
                            continue;
                        }

                        if (pass.Kind == CommandRecorderPassKind.Copy)
                        {
                            foreach (var command in commands)
                            {
                                if (command.Kind == GraphicsCommandKind.CopyBuffer)
                                {
                                    var sourceVersion = command.CopySource
                                        ?? throw new InvalidOperationException("The graph copy command has no source buffer version.");
                                    var destinationVersion = command.CopyDestination
                                        ?? throw new InvalidOperationException("The graph copy command has no destination buffer version.");
                                    var sourceBuffer = submission.GetBuffer(sourceVersion.ResourceId) as D3D12BackendBuffer
                                        ?? throw new InvalidOperationException("The graph copy source is not a D3D12 buffer.");
                                    var destinationBuffer = submission.GetBuffer(destinationVersion.ResourceId) as D3D12BackendBuffer
                                        ?? throw new InvalidOperationException("The graph copy destination is not a D3D12 buffer.");
                                    commandList->CopyBufferRegion(
                                        destinationBuffer.GetResource(),
                                        checked((ulong)command.DestinationOffset),
                                        sourceBuffer.GetResource(),
                                        checked((ulong)command.SourceOffset),
                                        checked((ulong)command.ByteLength));
                                }
                                else if (command.Kind == GraphicsCommandKind.CopyTexture)
                                {
                                    var sourceVersion = command.CopySourceTexture
                                        ?? throw new InvalidOperationException("The graph copy command has no source texture version.");
                                    var destinationVersion = command.CopyDestinationTexture
                                        ?? throw new InvalidOperationException("The graph copy command has no destination texture version.");
                                    var sourceTexture = submission.GetTexture(sourceVersion.ResourceId) as D3D12BackendTexture
                                        ?? throw new InvalidOperationException("The graph copy source is not a D3D12 texture.");
                                    var destinationTexture = submission.GetTexture(destinationVersion.ResourceId) as D3D12BackendTexture
                                        ?? throw new InvalidOperationException("The graph copy destination is not a D3D12 texture.");
                                    var sourceSubresource = new TextureSubresource(
                                        sourceVersion.Range.FirstMip,
                                        sourceVersion.Range.FirstLayer,
                                        sourceVersion.Range.Aspect);
                                    var destinationSubresource = new TextureSubresource(
                                        destinationVersion.Range.FirstMip,
                                        destinationVersion.Range.FirstLayer,
                                        destinationVersion.Range.Aspect);
                                    var sourceLocation = CreateSubresourceLocation(
                                        sourceTexture.GetResource(),
                                        GetTextureSubresourceIndex(sourceTexture.Description, sourceSubresource));
                                    var destinationLocation = CreateSubresourceLocation(
                                        destinationTexture.GetResource(),
                                        GetTextureSubresourceIndex(destinationTexture.Description, destinationSubresource));
                                    var copyRegion = command.CopyTextureRegion;
                                    var sourceBox = new D3D12_BOX {
                                        left = checked((uint)copyRegion.SourceX),
                                        top = checked((uint)copyRegion.SourceY),
                                        front = 0,
                                        right = checked((uint)(copyRegion.SourceX + copyRegion.Width)),
                                        bottom = checked((uint)(copyRegion.SourceY + copyRegion.Height)),
                                        back = 1,
                                    };
                                    commandList->CopyTextureRegion(
                                        &destinationLocation,
                                        0,
                                        0,
                                        0,
                                        &sourceLocation,
                                        &sourceBox);
                                }
                                else
                                {
                                    throw new NotSupportedException(
                                        $"Native D3D12 graph copy pass does not support {command.Kind} commands.");
                                }
                            }

                            continue;
                        }

                        if (pass.Kind == CommandRecorderPassKind.Compute)
                        {
                            ID3D12BackendPipelineDescriptorOwner? currentPipeline = null;

                            foreach (var command in commands)
                            {
                                switch (command.Kind)
                                {
                                    case GraphicsCommandKind.SetPipeline:
                                    {
                                        var pipeline = GraphicsGraphSubmission.GetPipeline(command) as ID3D12BackendPipelineDescriptorOwner
                                            ?? throw new InvalidOperationException("The graph compute pipeline is not a D3D12 pipeline.");

                                        if (!ReferenceEquals(pipeline.Owner, this)
                                            || pipeline.IsDisposed)
                                        {
                                            throw new InvalidOperationException("The graph D3D12 compute pipeline is not live on this device.");
                                        }

                                        commandList->SetPipelineState(pipeline.PipelineState);
                                        commandList->SetComputeRootSignature(pipeline.RootSignature);
                                        currentPipeline = pipeline;
                                        break;
                                    }

                                    case GraphicsCommandKind.SetBindings:
                                    {
                                        var pipeline = currentPipeline
                                            ?? throw new InvalidOperationException("A recorded binding set has no preceding D3D12 compute pipeline.");
                                        var bindings = command.Bindings
                                            ?? throw new InvalidOperationException("A recorded binding command lost its binding set.");
                                        var storageBufferBindings =
                                            ImmutableArray.CreateBuilder<D3D12BackendStorageBufferBinding>(bindings.Buffers.Length);
                                        var storageImageBindings =
                                            ImmutableArray.CreateBuilder<D3D12BackendStorageImageBinding>(bindings.Textures.Length);

                                        foreach (var binding in bindings.Buffers)
                                        {
                                            if (binding.Kind != ShaderResourceKind.StorageBuffer)
                                            {
                                                throw new NotSupportedException("Native D3D12 compute graphs currently support storage-buffer bindings only.");
                                            }

                                            storageBufferBindings.Add(new D3D12BackendStorageBufferBinding(
                                                binding.Group,
                                                binding.Binding,
                                                submission.GetBuffer(binding.Resource.ResourceId),
                                                binding.Range.ByteOffset,
                                                binding.Range.ByteLength));
                                        }

                                        foreach (var binding in bindings.Textures)
                                        {
                                            if (binding.Kind != ShaderResourceKind.StorageImage)
                                            {
                                                throw new NotSupportedException("Native D3D12 compute graphs currently support storage-image bindings only.");
                                            }

                                            storageImageBindings.Add(new D3D12BackendStorageImageBinding(
                                                binding.Group,
                                                binding.Binding,
                                                submission.GetTexture(binding.Resource.ResourceId),
                                                binding.Range));
                                        }

                                        if (descriptorSetCount >= descriptorSets.Length)
                                        {
                                            throw new InvalidOperationException("The graph descriptor-set preflight capacity was exceeded.");
                                        }

                                        var descriptorSet = CreateDescriptorSet(
                                            pipeline,
                                            [],
                                            [],
                                            storageBufferBindings.ToImmutable(),
                                            storageImageBindings.ToImmutable(),
                                            []);
                                        descriptorSets[descriptorSetCount] = descriptorSet;
                                        descriptorSetCount++;

                                        if ((descriptorSet.DescriptorTableCount == 0)
                                            || !descriptorSet.AreDescriptorTableHandlesValid)
                                        {
                                            throw new InvalidOperationException("The graph compute descriptor set has an invalid root table.");
                                        }

                                        var resourceHeap = _resourceDescriptorHeap;

                                        if (resourceHeap is null)
                                        {
                                            throw new InvalidOperationException("The D3D12 resource descriptor heap is unavailable.");
                                        }

                                        var descriptorHeap = resourceHeap;
                                        commandList->SetDescriptorHeaps(1, &descriptorHeap);

                                        for (var rootParameterIndex = 0;
                                            rootParameterIndex < descriptorSet.DescriptorTableCount;
                                            rootParameterIndex++)
                                        {
                                            commandList->SetComputeRootDescriptorTable(
                                                checked((uint)rootParameterIndex),
                                                descriptorSet._descriptorTableHandles[rootParameterIndex]);
                                        }

                                        break;
                                    }

                                    case GraphicsCommandKind.StorageBarrier:
                                    {
                                        ID3D12Resource* resource;

                                        if (command.StorageBarrierBuffer is GraphBufferVersion barrierBuffer)
                                        {
                                            var buffer = submission.GetBuffer(barrierBuffer.ResourceId) as D3D12BackendBuffer
                                                ?? throw new InvalidOperationException("The compute storage barrier lost its D3D12 buffer.");
                                            resource = buffer.GetResource();
                                        }
                                        else if (command.StorageBarrierTexture is GraphTextureVersion barrierTexture)
                                        {
                                            var texture = submission.GetTexture(barrierTexture.ResourceId) as D3D12BackendTexture
                                                ?? throw new InvalidOperationException("The compute storage barrier lost its D3D12 texture.");
                                            resource = texture.GetResource();
                                        }
                                        else
                                        {
                                            throw new InvalidOperationException("The compute storage barrier has no D3D12 resource.");
                                        }

                                        var barrier = D3D12_RESOURCE_BARRIER.InitUAV(resource);
                                        commandList->ResourceBarrier(1, &barrier);
                                        break;
                                    }

                                    case GraphicsCommandKind.Dispatch:
                                    {
                                        var dispatch = command.Dispatch;
                                        commandList->Dispatch(
                                            checked((uint)dispatch.GroupCountX),
                                            checked((uint)dispatch.GroupCountY),
                                            checked((uint)dispatch.GroupCountZ));
                                        break;
                                    }

                                    default:
                                    {
                                        throw new NotSupportedException($"Native D3D12 graph compute passes do not support {command.Kind} commands.");
                                    }
                                }
                            }

                            continue;
                        }

                        if (pass.Kind == CommandRecorderPassKind.Raster)
                        {
                            var renderTargetCount = 0;

                            foreach (var attachment in pass.Attachments)
                            {
                                if (!attachment.IsDepth)
                                {
                                    renderTargetHandles[renderTargetCount++] =
                                        renderTargetViews[submission.GetColorAttachmentResourceIndex(attachment.Version.ResourceId)];
                                }
                            }

                            commandList->OMSetRenderTargets(
                                checked((uint)renderTargetCount),
                                renderTargetHandles,
                                false,
                                hasDepthAttachment ? &depthStencilView : null);
                            ID3D12BackendPipelineDescriptorOwner? currentPipeline = null;

                            foreach (var command in commands)
                            {
                                switch (command.Kind)
                                {
                                    case GraphicsCommandKind.SetPipeline:
                                    {
                                        var pipeline = GraphicsGraphSubmission.GetPipeline(command) as ID3D12BackendPipelineDescriptorOwner
                                            ?? throw new InvalidOperationException("The graph pipeline is not a D3D12 pipeline.");

                                        if (!ReferenceEquals(pipeline.Owner, this) || pipeline.IsDisposed)
                                        {
                                            throw new InvalidOperationException("The graph D3D12 pipeline is not live on this device.");
                                        }

                                        commandList->SetPipelineState(pipeline.PipelineState);
                                        commandList->SetGraphicsRootSignature(pipeline.RootSignature);
                                        currentPipeline = pipeline;
                                        commandList->IASetPrimitiveTopology(
                                            D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
                                        commandList->RSSetViewports(1, &viewport);
                                        commandList->RSSetScissorRects(1, &scissor);
                                        break;
                                    }

                                    case GraphicsCommandKind.SetBindings:
                                    {
                                        var pipeline = currentPipeline
                                            ?? throw new InvalidOperationException("A recorded binding set has no preceding D3D12 pipeline.");
                                        var bindings = command.Bindings
                                            ?? throw new InvalidOperationException("A recorded binding command lost its binding set.");
                                        var uniformBufferBindings =
                                            ImmutableArray.CreateBuilder<D3D12BackendUniformBufferBinding>(bindings.Buffers.Length);
                                        var storageBufferBindings =
                                            ImmutableArray.CreateBuilder<D3D12BackendStorageBufferBinding>(bindings.Buffers.Length);

                                        foreach (var binding in bindings.Buffers)
                                        {
                                            var buffer = submission.GetBuffer(binding.Resource.ResourceId);

                                            if (binding.Kind == ShaderResourceKind.UniformBuffer)
                                            {
                                                uniformBufferBindings.Add(new D3D12BackendUniformBufferBinding(
                                                    binding.Group,
                                                    binding.Binding,
                                                    buffer,
                                                    binding.Range.ByteOffset,
                                                    binding.Range.ByteLength));
                                            }
                                            else
                                            {
                                                storageBufferBindings.Add(new D3D12BackendStorageBufferBinding(
                                                    binding.Group,
                                                    binding.Binding,
                                                    buffer,
                                                    binding.Range.ByteOffset,
                                                    binding.Range.ByteLength));
                                            }
                                        }

                                        var sampledImageBindings =
                                            ImmutableArray.CreateBuilder<D3D12BackendSampledImageBinding>(bindings.Textures.Length);
                                        var storageImageBindings =
                                            ImmutableArray.CreateBuilder<D3D12BackendStorageImageBinding>(bindings.Textures.Length);

                                        foreach (var binding in bindings.Textures)
                                        {
                                            var texture = submission.GetTexture(binding.Resource.ResourceId);

                                            if (binding.Kind == ShaderResourceKind.SampledImage)
                                            {
                                                sampledImageBindings.Add(new D3D12BackendSampledImageBinding(
                                                    binding.Group,
                                                    binding.Binding,
                                                    texture,
                                                    binding.Range,
                                                    pipeline.Resources.Single(resource =>
                                                        (resource.Kind == ShaderResourceKind.SampledImage)
                                                        && (resource.Group == binding.Group)
                                                        && (resource.Binding == binding.Binding)).ImageArrayed));
                                            }
                                            else
                                            {
                                                storageImageBindings.Add(new D3D12BackendStorageImageBinding(
                                                    binding.Group,
                                                    binding.Binding,
                                                    texture,
                                                    binding.Range));
                                            }
                                        }

                                        var samplerBindings =
                                            ImmutableArray.CreateBuilder<D3D12BackendSamplerBinding>(bindings.Samplers.Length);

                                        foreach (var binding in bindings.Samplers)
                                        {
                                            var sampler = binding.Sampler.ResourceOwner.BackendResource as IGraphicsBackendSampler
                                                ?? throw new InvalidOperationException("The graph sampler lost its D3D12 backend resource.");
                                            samplerBindings.Add(new D3D12BackendSamplerBinding(
                                                binding.Group,
                                                binding.Binding,
                                                sampler));
                                        }

                                        if (descriptorSetCount >= descriptorSets.Length)
                                        {
                                            throw new InvalidOperationException("The graph descriptor-set preflight capacity was exceeded.");
                                        }

                                        var descriptorSet = CreateDescriptorSet(
                                            pipeline,
                                            uniformBufferBindings.ToImmutable(),
                                            sampledImageBindings.ToImmutable(),
                                            storageBufferBindings.ToImmutable(),
                                            storageImageBindings.ToImmutable(),
                                            samplerBindings.ToImmutable());
                                        descriptorSets[descriptorSetCount] = descriptorSet;
                                        descriptorSetCount++;

                                        if ((descriptorSet.DescriptorTableCount == 0)
                                            || !descriptorSet.AreDescriptorTableHandlesValid)
                                        {
                                            throw new InvalidOperationException("The graph descriptor set has an invalid root table.");
                                        }

                                        var descriptorHeaps = stackalloc ID3D12DescriptorHeap*[2];
                                        var descriptorHeapCount = 0;

                                        if ((descriptorSet.BufferCount + descriptorSet.TextureCount) > 0)
                                        {
                                            var resourceHeap = _resourceDescriptorHeap;

                                            if (resourceHeap is null)
                                            {
                                                throw new InvalidOperationException("The D3D12 resource descriptor heap is unavailable.");
                                            }

                                            descriptorHeaps[descriptorHeapCount++] = resourceHeap;
                                        }

                                        if (descriptorSet.SamplerCount > 0)
                                        {
                                            var samplerHeap = _samplerDescriptorHeap;

                                            if (samplerHeap is null)
                                            {
                                                throw new InvalidOperationException("The D3D12 sampler descriptor heap is unavailable.");
                                            }

                                            descriptorHeaps[descriptorHeapCount++] = samplerHeap;
                                        }

                                        commandList->SetDescriptorHeaps(
                                            checked((uint)descriptorHeapCount),
                                            descriptorHeaps);

                                        for (var rootParameterIndex = 0;
                                            rootParameterIndex < descriptorSet.DescriptorTableCount;
                                            rootParameterIndex++)
                                        {
                                            commandList->SetGraphicsRootDescriptorTable(
                                                checked((uint)rootParameterIndex),
                                                descriptorSet._descriptorTableHandles[rootParameterIndex]);
                                        }

                                        break;
                                    }

                                    case GraphicsCommandKind.StorageBarrier:
                                    {
                                        ID3D12Resource* resource;

                                        if (command.StorageBarrierBuffer is GraphBufferVersion barrierBuffer)
                                        {
                                            var buffer = submission.GetBuffer(barrierBuffer.ResourceId) as D3D12BackendBuffer
                                                ?? throw new InvalidOperationException("The storage barrier lost its D3D12 buffer.");
                                            resource = buffer.GetResource();
                                        }
                                        else if (command.StorageBarrierTexture is GraphTextureVersion barrierTexture)
                                        {
                                            var texture = submission.GetTexture(barrierTexture.ResourceId) as D3D12BackendTexture
                                                ?? throw new InvalidOperationException("The storage barrier lost its D3D12 texture.");
                                            resource = texture.GetResource();
                                        }
                                        else
                                        {
                                            throw new InvalidOperationException("The storage barrier has no D3D12 resource.");
                                        }

                                        var barrier = D3D12_RESOURCE_BARRIER.InitUAV(resource);
                                        commandList->ResourceBarrier(1, &barrier);
                                        break;
                                    }

                                    case GraphicsCommandKind.SetVertexBuffer:
                                    {
                                        var binding = command.VertexBuffer;
                                        var buffer = submission.GetBuffer(binding.Buffer.ResourceId) as D3D12BackendBuffer
                                            ?? throw new InvalidOperationException("The graph vertex binding lost its D3D12 buffer.");
                                        var resource = buffer.GetResource();
                                        var view = new D3D12_VERTEX_BUFFER_VIEW {
                                            BufferLocation = checked(resource->GetGPUVirtualAddress() + checked((ulong)binding.Range.ByteOffset)),
                                            SizeInBytes = checked((uint)binding.Range.ByteLength),
                                            StrideInBytes = checked((uint)binding.Stride),
                                        };
                                        commandList->IASetVertexBuffers(checked((uint)binding.Slot), 1, &view);
                                        break;
                                    }

                                    case GraphicsCommandKind.SetIndexBuffer:
                                    {
                                        var binding = command.IndexBuffer;
                                        var buffer = submission.GetBuffer(binding.Buffer.ResourceId) as D3D12BackendBuffer
                                            ?? throw new InvalidOperationException("The graph index binding lost its D3D12 buffer.");
                                        var resource = buffer.GetResource();
                                        var view = new D3D12_INDEX_BUFFER_VIEW {
                                            BufferLocation = checked(resource->GetGPUVirtualAddress() + checked((ulong)binding.Range.ByteOffset)),
                                            SizeInBytes = checked((uint)binding.Range.ByteLength),
                                            Format = binding.Format switch {
                                                IndexFormat.UInt16 => DXGI_FORMAT.DXGI_FORMAT_R16_UINT,
                                                IndexFormat.UInt32 => DXGI_FORMAT.DXGI_FORMAT_R32_UINT,
                                                _ => throw new InvalidOperationException("The graph index binding has an invalid element format."),
                                            },
                                        };
                                        commandList->IASetIndexBuffer(&view);
                                        break;
                                    }

                                    case GraphicsCommandKind.Draw:
                                    {
                                        var draw = command.Draw;
                                        commandList->DrawInstanced(
                                            checked((uint)draw.VertexCount),
                                            checked((uint)draw.InstanceCount),
                                            checked((uint)draw.FirstVertex),
                                            checked((uint)draw.FirstInstance));
                                        break;
                                    }

                                    case GraphicsCommandKind.DrawIndexed:
                                    {
                                        var draw = command.DrawIndexed;
                                        commandList->DrawIndexedInstanced(
                                            checked((uint)draw.IndexCount),
                                            checked((uint)draw.InstanceCount),
                                            checked((uint)draw.FirstIndex),
                                            draw.BaseVertex,
                                            checked((uint)draw.FirstInstance));
                                        break;
                                    }

                                    default:
                                    {
                                        throw new NotSupportedException($"Native D3D12 graph submission does not support {command.Kind} commands.");
                                    }
                                }
                            }
                        }
                    }

                    if (descriptorSetCount != descriptorSetCapacity)
                    {
                        throw new InvalidOperationException("The graph descriptor-set preflight count did not match binding emission.");
                    }

                    var submittedFence = transferQueue.SubmitGraphAndSignal();
                    _graphFrameFenceValues[graphFrameSlotIndex] = submittedFence;
                    _graphFrameDescriptorSets[graphFrameSlotIndex] = descriptorSets;
                    _graphFrameDescriptorSetCounts[graphFrameSlotIndex] = descriptorSetCount;
                    _graphFrameRenderTargetHeaps[graphFrameSlotIndex] = (nint)renderTargetHeap;
                    renderTargetHeap = null;
                    graphSubmissionAccepted = true;

                    if (surface is not null)
                    {
                        try
                        {
                            submission.SetPresentationSequence(surface.Present());
                        }
                        catch (Exception presentationException)
                        {
                            try
                            {
                                transferQueue.WaitForFence(submittedFence);
                            }
                            catch (Exception fenceWaitException)
                            {
                                transferQueue.MarkUncertainSubmission();
                                throw new AggregateException(presentationException, fenceWaitException);
                            }

                            transferQueue.MarkUncertainSubmission();
                            throw;
                        }
                    }

                    return submittedFence;
                }
                catch (Exception exception)
                {
                    if (graphSubmissionAccepted)
                    {
                        throw;
                    }

                    try
                    {
                        transferQueue.StopRecording();
                    }
                    catch (Exception stopException)
                    {
                        if (transferQueue.HasUncertainSubmission && (renderTargetHeap is not null))
                        {
                            _uncertainGraphRenderTargetHeaps.Add((nint)renderTargetHeap);
                            renderTargetHeap = null;
                        }

                        throw new AggregateException(exception, stopException);
                    }

                    if (transferQueue.HasUncertainSubmission)
                    {
                        _uncertainGraphRenderTargetHeaps.Add((nint)renderTargetHeap);
                        renderTargetHeap = null;
                    }

                    throw;
                }
                finally
                {
                    try
                    {
                        if (transferQueue.HasUncertainSubmission)
                        {
                            for (var index = 0; index < descriptorSetCount; index++)
                            {
                                _uncertainGraphDescriptorSets.Add(descriptorSets[index]);
                            }
                        }
                        else if (!graphSubmissionAccepted)
                        {
                            for (var index = descriptorSetCount - 1; index >= 0; index--)
                            {
                                descriptorSets[index].Dispose();
                            }
                        }
                    }
                    finally
                    {
                        if (renderTargetHeap is not null)
                        {
                            _ = renderTargetHeap->Release();
                        }
                    }
                }
            }

            [SuppressMessage(
                "Usage",
                "CA1031:Do not catch general exception types",
                Justification = "A failed upload closes an unsubmitted command list before releasing its staging owner.")]
            public long UploadBuffer(
                IGraphicsBackendBuffer destination,
                GraphResourceState destinationState,
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
                var mapped = GetUploadStaging(
                    byteLength,
                    alignment: sizeof(uint),
                    out var stagingResource,
                    out var isDedicated,
                    out var stagingOffset,
                    out var stagingReservation);
                var isMapped = true;
                long? completedFenceValue = null;

                try
                {
                    CopySourceLeaseToMappedStorage(
                        source,
                        sourceOffset,
                        byteLength,
                        mapped + checked((nint)stagingOffset));
                    var commandList = transferQueue.BeginRecording();
                    var destinationResource = destinationBuffer.GetResource();
                    var initialState = D3D12GraphResourceStateMapper.GetBufferState(
                        destinationState,
                        destinationBuffer.Description);
                    var copyDestinationState = D3D12GraphResourceStateMapper.GetBufferState(
                        GraphResourceState.CopyDestination,
                        destinationBuffer.Description);
                    var generalState = D3D12GraphResourceStateMapper.GetBufferState(
                        GraphResourceState.General,
                        destinationBuffer.Description);

                    if (initialState != copyDestinationState)
                    {
                        var toCopyDestination = D3D12_RESOURCE_BARRIER.InitTransition(
                            destinationResource,
                            initialState,
                            copyDestinationState,
                            0,
                            D3D12_RESOURCE_BARRIER_FLAGS.D3D12_RESOURCE_BARRIER_FLAG_NONE);
                        commandList->ResourceBarrier(1, &toCopyDestination);
                    }

                    commandList->CopyBufferRegion(
                        destinationResource,
                        checked((ulong)destinationOffset),
                        stagingResource,
                        stagingOffset,
                        checked((ulong)byteLength));

                    if (copyDestinationState != generalState)
                    {
                        var toCommon = D3D12_RESOURCE_BARRIER.InitTransition(
                            destinationResource,
                            copyDestinationState,
                            generalState,
                            0,
                            D3D12_RESOURCE_BARRIER_FLAGS.D3D12_RESOURCE_BARRIER_FLAG_NONE);
                        commandList->ResourceBarrier(1, &toCommon);
                    }

                    var completedFence = transferQueue.SubmitAndWait(
                        stagingReservation.IsValid
                            ? fenceValue => _uploadStagingAllocator.MarkSubmitted(stagingReservation, fenceValue)
                            : null);
                    completedFenceValue = completedFence;
                    return completedFence;
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

                    ReleaseStagingRange(
                        _uploadStagingAllocator,
                        stagingReservation,
                        completedFenceValue,
                        transferQueue.HasUncertainSubmission);
                }
            }

            [SuppressMessage(
                "Usage",
                "CA1031:Do not catch general exception types",
                Justification = "A failed upload closes an unsubmitted command list before releasing its staging owner.")]
            public long UploadBuffer(
                IGraphicsBackendBuffer destination,
                GraphResourceState destinationState,
                long destinationOffset,
                ReadOnlySpan<byte> source)
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
                var mapped = GetUploadStaging(
                    byteLength,
                    alignment: sizeof(uint),
                    out var stagingResource,
                    out var isDedicated,
                    out var stagingOffset,
                    out var stagingReservation);
                long? completedFenceValue = null;

                try
                {
                    source.CopyTo(new Span<byte>(
                        mapped + checked((nint)stagingOffset),
                        source.Length));
                    var commandList = transferQueue.BeginRecording();
                    var destinationResource = destinationBuffer.GetResource();
                    var initialState = D3D12GraphResourceStateMapper.GetBufferState(
                        destinationState,
                        destinationBuffer.Description);
                    var copyDestinationState = D3D12GraphResourceStateMapper.GetBufferState(
                        GraphResourceState.CopyDestination,
                        destinationBuffer.Description);
                    var generalState = D3D12GraphResourceStateMapper.GetBufferState(
                        GraphResourceState.General,
                        destinationBuffer.Description);

                    if (initialState != copyDestinationState)
                    {
                        var toCopyDestination = D3D12_RESOURCE_BARRIER.InitTransition(
                            destinationResource,
                            initialState,
                            copyDestinationState,
                            0,
                            D3D12_RESOURCE_BARRIER_FLAGS.D3D12_RESOURCE_BARRIER_FLAG_NONE);
                        commandList->ResourceBarrier(1, &toCopyDestination);
                    }

                    commandList->CopyBufferRegion(
                        destinationResource,
                        checked((ulong)destinationOffset),
                        stagingResource,
                        stagingOffset,
                        checked((ulong)byteLength));

                    if (copyDestinationState != generalState)
                    {
                        var toCommon = D3D12_RESOURCE_BARRIER.InitTransition(
                            destinationResource,
                            copyDestinationState,
                            generalState,
                            0,
                            D3D12_RESOURCE_BARRIER_FLAGS.D3D12_RESOURCE_BARRIER_FLAG_NONE);
                        commandList->ResourceBarrier(1, &toCommon);
                    }

                    var completedFence = transferQueue.SubmitAndWait(
                        stagingReservation.IsValid
                            ? fenceValue => _uploadStagingAllocator.MarkSubmitted(stagingReservation, fenceValue)
                            : null);
                    completedFenceValue = completedFence;
                    return completedFence;
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

                    ReleaseStagingRange(
                        _uploadStagingAllocator,
                        stagingReservation,
                        completedFenceValue,
                        transferQueue.HasUncertainSubmission);
                }
            }

            [SuppressMessage(
                "Usage",
                "CA1031:Do not catch general exception types",
                Justification = "A failed texture upload closes an unsubmitted command list before releasing its staging owner.")]
            public long UploadTexture(
                IGraphicsBackendTexture destination,
                GraphResourceState destinationState,
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
                var mapped = GetUploadStaging(
                    byteLength,
                    D3D12Api.D3D12_TEXTURE_DATA_PLACEMENT_ALIGNMENT,
                    out var stagingResource,
                    out var isDedicated,
                    out var stagingOffset,
                    out var stagingReservation);
                var placedFootprint = footprint.Layout;
                placedFootprint.Offset = stagingOffset;
                footprint.Layout = placedFootprint;
                long? completedFenceValue = null;

                try
                {
                    new Span<byte>(
                        mapped + checked((nint)stagingOffset),
                        checked((int)byteLength)).Clear();
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
                    var initialState = D3D12GraphResourceStateMapper.GetTextureState(
                        destinationState,
                        destinationTexture.Description);
                    var copyDestinationState = D3D12GraphResourceStateMapper.GetTextureState(
                        GraphResourceState.CopyDestination,
                        destinationTexture.Description);
                    var generalState = D3D12GraphResourceStateMapper.GetTextureState(
                        GraphResourceState.General,
                        destinationTexture.Description);

                    if (initialState != copyDestinationState)
                    {
                        var toCopyDestination = D3D12_RESOURCE_BARRIER.InitTransition(
                            destinationResource,
                            initialState,
                            copyDestinationState,
                            subresourceIndex,
                            D3D12_RESOURCE_BARRIER_FLAGS.D3D12_RESOURCE_BARRIER_FLAG_NONE);
                        commandList->ResourceBarrier(1, &toCopyDestination);
                    }

                    var sourceLocation = CreateFootprintLocation(stagingResource, footprint.Layout);
                    var destinationLocation = CreateSubresourceLocation(destinationResource, subresourceIndex);
                    commandList->CopyTextureRegion(&destinationLocation, 0, 0, 0, &sourceLocation, null);

                    if (copyDestinationState != generalState)
                    {
                        var toCommon = D3D12_RESOURCE_BARRIER.InitTransition(
                            destinationResource,
                            copyDestinationState,
                            generalState,
                            subresourceIndex,
                            D3D12_RESOURCE_BARRIER_FLAGS.D3D12_RESOURCE_BARRIER_FLAG_NONE);
                        commandList->ResourceBarrier(1, &toCommon);
                    }

                    var completedFence = transferQueue.SubmitAndWait(
                        stagingReservation.IsValid
                            ? fenceValue => _uploadStagingAllocator.MarkSubmitted(stagingReservation, fenceValue)
                            : null);
                    completedFenceValue = completedFence;
                    return completedFence;
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

                    ReleaseStagingRange(
                        _uploadStagingAllocator,
                        stagingReservation,
                        completedFenceValue,
                        transferQueue.HasUncertainSubmission);
                }
            }

            public void WaitForGraphFence(long fenceValue)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fenceValue);
                var transferQueue = _transferQueue
                    ?? throw new InvalidOperationException("The D3D12 graph fence has no submitted transfer queue.");
                transferQueue.WaitForFence(fenceValue);
            }

            public void WriteUploadBuffer(
                IGraphicsBackendBuffer destination,
                long destinationOffset,
                ReadOnlySpan<byte> source)
            {
                EnsureOwnerThread();
                ArgumentNullException.ThrowIfNull(destination);

                if (destination is not D3D12BackendBuffer buffer || !ReferenceEquals(buffer.Owner, this))
                {
                    throw new ArgumentException("The buffer belongs to a different D3D12 device.", nameof(destination));
                }

                if (buffer.Description.MemoryKind != GpuMemoryKind.Upload)
                {
                    throw new ArgumentException("The buffer does not use host-visible upload memory.", nameof(destination));
                }

                ValidateTransferRange(
                    buffer.Description.ByteLength,
                    destinationOffset,
                    checked((nint)source.Length),
                    nameof(destinationOffset),
                    nameof(source));
                var resource = buffer.GetResource();
                var readRange = default(D3D12_RANGE);
                void* mapped = null;
                ThrowIfFailed(resource->Map(0, &readRange, &mapped));

                if (mapped is null)
                {
                    resource->Unmap(0, &readRange);
                    throw new InvalidOperationException("D3D12 upload buffer mapping returned no address.");
                }

                try
                {
                    source.CopyTo(new Span<byte>((byte*)mapped + checked((nint)destinationOffset), source.Length));
                }
                finally
                {
                    var writtenRange = new D3D12_RANGE {
                        Begin = checked((nuint)destinationOffset),
                        End = checked((nuint)(destinationOffset + source.Length)),
                    };
                    resource->Unmap(0, &writtenRange);
                }
            }

            internal D3D12BackendDescriptorSet CreateDescriptorSet(
                IGraphicsBackendPipeline pipeline,
                ImmutableArray<D3D12BackendUniformBufferBinding> uniformBufferBindings,
                ImmutableArray<D3D12BackendSampledImageBinding> sampledImageBindings,
                ImmutableArray<D3D12BackendStorageBufferBinding> storageBufferBindings,
                ImmutableArray<D3D12BackendStorageImageBinding> storageImageBindings,
                ImmutableArray<D3D12BackendSamplerBinding> samplerBindings)
            {
                EnsureOwnerThread();
                ArgumentNullException.ThrowIfNull(pipeline);

                if (pipeline is not ID3D12BackendPipelineDescriptorOwner selectedPipeline
                    || !ReferenceEquals(selectedPipeline.Owner, this)
                    || selectedPipeline.IsDisposed)
                {
                    throw new ArgumentException("The pipeline is not live on this D3D12 device.", nameof(pipeline));
                }

                if (uniformBufferBindings.IsDefault
                    || sampledImageBindings.IsDefault
                    || storageBufferBindings.IsDefault
                    || storageImageBindings.IsDefault
                    || samplerBindings.IsDefault)
                {
                    throw new ArgumentException("D3D12 descriptor binding arrays must be initialized.");
                }

                var resources = selectedPipeline.Resources;
                var tableCounts = new int[D3D12DescriptorLayout.TableCount];
                var expectedUniformBufferCount = 0;
                var expectedSampledImageCount = 0;
                var expectedStorageBufferCount = 0;
                var expectedStorageImageCount = 0;
                var expectedSamplerCount = 0;

                for (var index = 0; index < resources.Length; index++)
                {
                    var resource = resources[index];

                    if (resource.ArrayCount != 1)
                    {
                        throw new NotSupportedException("D3D12 descriptor arrays are not supported by this descriptor-set path.");
                    }

                    switch (resource.Kind)
                    {
                        case ShaderResourceKind.UniformBuffer:
                        {
                            expectedUniformBufferCount++;
                            break;
                        }

                        case ShaderResourceKind.SampledImage:
                        {
                            expectedSampledImageCount++;
                            break;
                        }

                        case ShaderResourceKind.StorageBuffer:
                        {
                            expectedStorageBufferCount++;
                            break;
                        }

                        case ShaderResourceKind.StorageImage:
                        {
                            expectedStorageImageCount++;
                            break;
                        }

                        case ShaderResourceKind.Sampler:
                        {
                            expectedSamplerCount++;
                            break;
                        }

                        default:
                        {
                            throw new NotSupportedException("This D3D12 descriptor-set path supports uniform buffers, sampled images, and samplers.");
                        }
                    }

                    tableCounts[D3D12DescriptorLayout.GetTableIndex(resource)] = checked(
                        tableCounts[D3D12DescriptorLayout.GetTableIndex(resource)] + resource.ArrayCount);
                }

                if (resources.IsEmpty
                    || expectedUniformBufferCount != uniformBufferBindings.Length
                    || expectedSampledImageCount != sampledImageBindings.Length
                    || expectedStorageBufferCount != storageBufferBindings.Length
                    || expectedStorageImageCount != storageImageBindings.Length
                    || expectedSamplerCount != samplerBindings.Length)
                {
                    throw new ArgumentException("The pipeline does not have a complete supported descriptor set.", nameof(pipeline));
                }

                var sortedUniformBufferBindings = uniformBufferBindings.ToArray();
                Array.Sort(sortedUniformBufferBindings, static (left, right) => {
                    var groupComparison = left.Group.CompareTo(right.Group);
                    return (groupComparison != 0)
                        ? groupComparison
                        : left.Binding.CompareTo(right.Binding);
                });
                var retainedUniformBuffers = new D3D12BackendBuffer[sortedUniformBufferBindings.Length];
                var uniformBufferRoundedRanges = new long[sortedUniformBufferBindings.Length];

                for (var index = 0; index < sortedUniformBufferBindings.Length; index++)
                {
                    var binding = sortedUniformBufferBindings[index];
                    ArgumentNullException.ThrowIfNull(binding.Buffer);

                    if ((binding.Group == ShaderBindingGroup.None)
                        || (binding.Binding < 0)
                        || (binding.Buffer is not D3D12BackendBuffer buffer)
                        || !ReferenceEquals(buffer.Owner, this)
                        || buffer.IsDisposed)
                    {
                        throw new ArgumentException("A uniform-buffer descriptor binding has an invalid group, binding, or buffer owner.", nameof(uniformBufferBindings));
                    }

                    if (!MatchesReflectedResource(
                            resources,
                            binding.Group,
                            binding.Binding,
                            ShaderResourceKind.UniformBuffer)
                        || ((index != 0)
                            && (sortedUniformBufferBindings[index - 1].Group == binding.Group)
                            && (sortedUniformBufferBindings[index - 1].Binding == binding.Binding)))
                    {
                        throw new ArgumentException("Uniform-buffer descriptors must match the pipeline's unique reflected bindings.", nameof(uniformBufferBindings));
                    }

                    if ((buffer.Description.Usage & BufferUsage.Uniform) == 0)
                    {
                        throw new ArgumentException("A bound buffer does not declare uniform-buffer usage.", nameof(uniformBufferBindings));
                    }

                    ArgumentOutOfRangeException.ThrowIfNegative(binding.Offset);
                    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(binding.Range);

                    var rangeEnd = checked(binding.Offset + binding.Range);

                    if (rangeEnd > buffer.Description.ByteLength)
                    {
                        throw new ArgumentException("The uniform-buffer descriptor range exceeds its buffer.", nameof(uniformBufferBindings));
                    }

                    if ((binding.Offset % Capabilities.Limits.UniformBufferOffsetAlignment) != 0)
                    {
                        throw new ArgumentException("The uniform-buffer descriptor offset does not satisfy device alignment.", nameof(uniformBufferBindings));
                    }

                    ArgumentOutOfRangeException.ThrowIfGreaterThan(
                        binding.Range,
                        Capabilities.Limits.MaximumUniformBufferRangeBytes,
                        nameof(uniformBufferBindings));

                    var roundedRange = checked((binding.Range + 255L) & ~255L);

                    if (checked(binding.Offset + roundedRange) > buffer.Description.ByteLength)
                    {
                        throw new ArgumentException("The aligned uniform-buffer descriptor range exceeds its buffer.", nameof(uniformBufferBindings));
                    }

                    retainedUniformBuffers[index] = buffer;
                    uniformBufferRoundedRanges[index] = roundedRange;
                }

                var sortedStorageBufferBindings = storageBufferBindings.ToArray();
                Array.Sort(sortedStorageBufferBindings, static (left, right) => {
                    var groupComparison = left.Group.CompareTo(right.Group);
                    return (groupComparison != 0)
                        ? groupComparison
                        : left.Binding.CompareTo(right.Binding);
                });
                var retainedStorageBuffers = new D3D12BackendBuffer[sortedStorageBufferBindings.Length];

                for (var index = 0; index < sortedStorageBufferBindings.Length; index++)
                {
                    var binding = sortedStorageBufferBindings[index];
                    ArgumentNullException.ThrowIfNull(binding.Buffer);

                    if ((binding.Group == ShaderBindingGroup.None)
                        || (binding.Binding < 0)
                        || (binding.Buffer is not D3D12BackendBuffer buffer)
                        || !ReferenceEquals(buffer.Owner, this)
                        || buffer.IsDisposed)
                    {
                        throw new ArgumentException("A storage-buffer descriptor binding has an invalid group, binding, or buffer owner.", nameof(storageBufferBindings));
                    }

                    if (!MatchesReflectedResource(
                            resources,
                            binding.Group,
                            binding.Binding,
                            ShaderResourceKind.StorageBuffer)
                        || ((index != 0)
                            && (sortedStorageBufferBindings[index - 1].Group == binding.Group)
                            && (sortedStorageBufferBindings[index - 1].Binding == binding.Binding)))
                    {
                        throw new ArgumentException("Storage-buffer descriptors must match the pipeline's unique reflected bindings.", nameof(storageBufferBindings));
                    }

                    if ((buffer.Description.Usage & BufferUsage.Storage) == 0)
                    {
                        throw new ArgumentException("A bound buffer does not declare storage-buffer usage.", nameof(storageBufferBindings));
                    }

                    ArgumentOutOfRangeException.ThrowIfNegative(binding.Offset);
                    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(binding.Range);

                    var rangeEnd = checked(binding.Offset + binding.Range);

                    if (rangeEnd > buffer.Description.ByteLength)
                    {
                        throw new ArgumentException("The storage-buffer descriptor range exceeds its buffer.", nameof(storageBufferBindings));
                    }

                    if ((binding.Offset % Capabilities.Limits.StorageBufferOffsetAlignment) != 0)
                    {
                        throw new ArgumentException("The storage-buffer descriptor offset does not satisfy device alignment.", nameof(storageBufferBindings));
                    }

                    ArgumentOutOfRangeException.ThrowIfGreaterThan(
                        binding.Range,
                        Capabilities.Limits.MaximumBufferByteLength,
                        nameof(storageBufferBindings));

                    var structuredStride = buffer.Description.StructuredStride
                        ?? throw new NotSupportedException("This D3D12 descriptor-set path requires structured storage buffers.");

                    if (((binding.Offset % structuredStride) != 0)
                        || ((binding.Range % structuredStride) != 0))
                    {
                        throw new ArgumentException("The storage-buffer descriptor range must align to its structured element stride.", nameof(storageBufferBindings));
                    }

                    if ((binding.Range / structuredStride) > uint.MaxValue)
                    {
                        throw new ArgumentOutOfRangeException(nameof(storageBufferBindings));
                    }

                    retainedStorageBuffers[index] = buffer;
                }

                var sortedSampledImageBindings = sampledImageBindings.ToArray();
                Array.Sort(sortedSampledImageBindings, static (left, right) => {
                    var groupComparison = left.Group.CompareTo(right.Group);
                    return (groupComparison != 0)
                        ? groupComparison
                        : left.Binding.CompareTo(right.Binding);
                });
                var retainedSampledTextures = new D3D12BackendTexture[sortedSampledImageBindings.Length];

                for (var index = 0; index < sortedSampledImageBindings.Length; index++)
                {
                    var binding = sortedSampledImageBindings[index];
                    ArgumentNullException.ThrowIfNull(binding.Texture);

                    if ((binding.Group == ShaderBindingGroup.None)
                        || (binding.Binding < 0)
                        || (binding.Texture is not D3D12BackendTexture texture)
                        || !ReferenceEquals(texture.Owner, this)
                        || texture.IsDisposed)
                    {
                        throw new ArgumentException("A sampled-image descriptor binding has an invalid group, binding, or texture owner.", nameof(sampledImageBindings));
                    }

                    if (!MatchesReflectedResource(
                            resources,
                            binding.Group,
                            binding.Binding,
                            ShaderResourceKind.SampledImage)
                        || ((index != 0)
                            && (sortedSampledImageBindings[index - 1].Group == binding.Group)
                            && (sortedSampledImageBindings[index - 1].Binding == binding.Binding)))
                    {
                        throw new ArgumentException("Sampled-image descriptors must match the pipeline's unique reflected bindings.", nameof(sampledImageBindings));
                    }

                    if ((texture.Description.Usage & TextureUsage.Sampled) == 0)
                    {
                        throw new ArgumentException("A bound texture does not declare sampled usage.", nameof(sampledImageBindings));
                    }

                    if ((texture.Description.Dimension != TextureDimension.Dimension2D)
                        || (!binding.ImageArrayed && (texture.Description.ArrayLayers != 1)))
                    {
                        throw new NotSupportedException("This D3D12 descriptor-set path requires a reflected 2D or 2D-array sampled texture.");
                    }

                    retainedSampledTextures[index] = texture;
                }

                var sortedStorageImageBindings = storageImageBindings.ToArray();
                Array.Sort(sortedStorageImageBindings, static (left, right) => {
                    var groupComparison = left.Group.CompareTo(right.Group);
                    return (groupComparison != 0)
                        ? groupComparison
                        : left.Binding.CompareTo(right.Binding);
                });
                var retainedStorageTextures = new D3D12BackendTexture[sortedStorageImageBindings.Length];

                for (var index = 0; index < sortedStorageImageBindings.Length; index++)
                {
                    var binding = sortedStorageImageBindings[index];
                    ArgumentNullException.ThrowIfNull(binding.Texture);

                    if ((binding.Group == ShaderBindingGroup.None)
                        || (binding.Binding < 0)
                        || (binding.Texture is not D3D12BackendTexture texture)
                        || !ReferenceEquals(texture.Owner, this)
                        || texture.IsDisposed)
                    {
                        throw new ArgumentException("A storage-image descriptor binding has an invalid group, binding, or texture owner.", nameof(storageImageBindings));
                    }

                    if (!MatchesReflectedResource(
                            resources,
                            binding.Group,
                            binding.Binding,
                            ShaderResourceKind.StorageImage)
                        || ((index != 0)
                            && (sortedStorageImageBindings[index - 1].Group == binding.Group)
                            && (sortedStorageImageBindings[index - 1].Binding == binding.Binding)))
                    {
                        throw new ArgumentException("Storage-image descriptors must match the pipeline's unique reflected bindings.", nameof(storageImageBindings));
                    }

                    if ((texture.Description.Usage & TextureUsage.Storage) == 0)
                    {
                        throw new ArgumentException("A bound texture does not declare storage-image usage.", nameof(storageImageBindings));
                    }

                    if (texture.Description.Dimension is not (TextureDimension.Dimension2D or TextureDimension.Dimension3D)
                        || (texture.Description.ArrayLayers != 1))
                    {
                        throw new NotSupportedException("This D3D12 descriptor-set path supports single-layer 2D and 3D storage textures.");
                    }

                    retainedStorageTextures[index] = texture;
                }

                var sortedSamplerBindings = samplerBindings.ToArray();
                Array.Sort(sortedSamplerBindings, static (left, right) => {
                    var groupComparison = left.Group.CompareTo(right.Group);
                    return (groupComparison != 0)
                        ? groupComparison
                        : left.Binding.CompareTo(right.Binding);
                });
                var retainedSamplers = new D3D12BackendSampler[sortedSamplerBindings.Length];

                for (var index = 0; index < sortedSamplerBindings.Length; index++)
                {
                    var binding = sortedSamplerBindings[index];
                    ArgumentNullException.ThrowIfNull(binding.Sampler);

                    if ((binding.Group == ShaderBindingGroup.None)
                        || (binding.Binding < 0)
                        || (binding.Sampler is not D3D12BackendSampler sampler)
                        || !ReferenceEquals(sampler.Owner, this)
                        || sampler.IsDisposed)
                    {
                        throw new ArgumentException("A sampler descriptor binding has an invalid group, binding, or sampler owner.", nameof(samplerBindings));
                    }

                    if (!MatchesReflectedResource(
                            resources,
                            binding.Group,
                            binding.Binding,
                            ShaderResourceKind.Sampler)
                        || ((index != 0)
                            && (sortedSamplerBindings[index - 1].Group == binding.Group)
                            && (sortedSamplerBindings[index - 1].Binding == binding.Binding)))
                    {
                        throw new ArgumentException("Sampler descriptors must match the pipeline's unique reflected bindings.", nameof(samplerBindings));
                    }

                    retainedSamplers[index] = sampler;
                }

                var retainedBuffers = new D3D12BackendBuffer[
                    checked(retainedUniformBuffers.Length + retainedStorageBuffers.Length)];
                Array.Copy(retainedUniformBuffers, 0, retainedBuffers, 0, retainedUniformBuffers.Length);
                Array.Copy(
                    retainedStorageBuffers,
                    0,
                    retainedBuffers,
                    retainedUniformBuffers.Length,
                    retainedStorageBuffers.Length);
                var retainedTextures = new D3D12BackendTexture[
                    checked(retainedSampledTextures.Length + retainedStorageTextures.Length)];
                Array.Copy(retainedSampledTextures, 0, retainedTextures, 0, retainedSampledTextures.Length);
                Array.Copy(
                    retainedStorageTextures,
                    0,
                    retainedTextures,
                    retainedSampledTextures.Length,
                    retainedStorageTextures.Length);
                var descriptorTableCount = 0;
                var rootParameterIndices = new int[D3D12DescriptorLayout.TableCount];
                Array.Fill(rootParameterIndices, -1);

                for (var tableIndex = 0; tableIndex < D3D12DescriptorLayout.TableCount; tableIndex++)
                {
                    if (tableCounts[tableIndex] != 0)
                    {
                        rootParameterIndices[tableIndex] = descriptorTableCount++;
                    }
                }

                var hasResourceTable = false;
                var hasSamplerTable = false;

                for (var tableIndex = 0; tableIndex < D3D12DescriptorLayout.TableCount; tableIndex++)
                {
                    if (tableCounts[tableIndex] == 0)
                    {
                        continue;
                    }

                    hasSamplerTable |= (tableIndex % 4) == 3;
                    hasResourceTable |= (tableIndex % 4) != 3;
                }

                if (hasResourceTable)
                {
                    EnsureResourceDescriptorHeap();
                }

                if (hasSamplerTable)
                {
                    EnsureSamplerDescriptorHeap();
                }

                var descriptorRangeStarts = new int[D3D12DescriptorLayout.TableCount];
                Array.Fill(descriptorRangeStarts, -1);
                var ownsDescriptorRanges = true;

                try
                {
                    for (var tableIndex = 0; tableIndex < D3D12DescriptorLayout.TableCount; tableIndex++)
                    {
                        var descriptorCount = tableCounts[tableIndex];

                        if (descriptorCount == 0)
                        {
                            continue;
                        }

                        var allocator = ((tableIndex % 4) == 3)
                            ? _samplerDescriptorAllocator
                            : _resourceDescriptorAllocator;
                        descriptorRangeStarts[tableIndex] = (allocator
                            ?? throw new InvalidOperationException("The D3D12 descriptor heap has no range allocator."))
                            .Allocate(descriptorCount);
                    }

                    var tableOffsets = new int[D3D12DescriptorLayout.TableCount];

                    for (var index = 0; index < sortedUniformBufferBindings.Length; index++)
                    {
                        var binding = sortedUniformBufferBindings[index];
                        var tableIndex = D3D12DescriptorLayout.GetTableIndex(binding.Group, ShaderResourceKind.UniformBuffer);
                        var descriptorIndex = checked(descriptorRangeStarts[tableIndex] + tableOffsets[tableIndex]++);
                        var cpuHandle = _resourceDescriptorHeap->GetCPUDescriptorHandleForHeapStart();
                        cpuHandle.ptr = checked(
                            cpuHandle.ptr
                            + (checked((nuint)descriptorIndex) * _resourceDescriptorIncrementSize));
                        var constantBufferView = new D3D12_CONSTANT_BUFFER_VIEW_DESC {
                            BufferLocation = checked(retainedUniformBuffers[index].GetResource()->GetGPUVirtualAddress() + checked((ulong)binding.Offset)),
                            SizeInBytes = checked((uint)uniformBufferRoundedRanges[index]),
                        };
                        _device->CreateConstantBufferView(&constantBufferView, cpuHandle);
                    }

                    for (var index = 0; index < sortedSampledImageBindings.Length; index++)
                    {
                        var binding = sortedSampledImageBindings[index];
                        var tableIndex = D3D12DescriptorLayout.GetTableIndex(binding.Group, ShaderResourceKind.SampledImage);
                        var descriptorIndex = checked(descriptorRangeStarts[tableIndex] + tableOffsets[tableIndex]++);
                        var cpuHandle = _resourceDescriptorHeap->GetCPUDescriptorHandleForHeapStart();
                        cpuHandle.ptr = checked(
                            cpuHandle.ptr
                            + (checked((nuint)descriptorIndex) * _resourceDescriptorIncrementSize));
                        CreateSampledImageView(retainedSampledTextures[index], binding.Range, binding.ImageArrayed, cpuHandle);
                    }

                    var storageBufferIndex = 0;
                    var storageImageIndex = 0;

                    while ((storageBufferIndex < sortedStorageBufferBindings.Length)
                        || (storageImageIndex < sortedStorageImageBindings.Length))
                    {
                        var useStorageBuffer = storageImageIndex >= sortedStorageImageBindings.Length;

                        if (!useStorageBuffer && (storageBufferIndex < sortedStorageBufferBindings.Length))
                        {
                            var bufferBinding = sortedStorageBufferBindings[storageBufferIndex];
                            var imageBinding = sortedStorageImageBindings[storageImageIndex];
                            var groupComparison = bufferBinding.Group.CompareTo(imageBinding.Group);
                            var bindingComparison = (groupComparison == 0)
                                ? bufferBinding.Binding.CompareTo(imageBinding.Binding)
                                : groupComparison;

                            if (bindingComparison == 0)
                            {
                                throw new InvalidOperationException("D3D12 storage-buffer and storage-image bindings share one UAV register.");
                            }

                            useStorageBuffer = bindingComparison < 0;
                        }

                        var group = useStorageBuffer
                            ? sortedStorageBufferBindings[storageBufferIndex].Group
                            : sortedStorageImageBindings[storageImageIndex].Group;
                        var kind = useStorageBuffer
                            ? ShaderResourceKind.StorageBuffer
                            : ShaderResourceKind.StorageImage;
                        var storageTableIndex = D3D12DescriptorLayout.GetTableIndex(group, kind);
                        var storageDescriptorIndex = checked(
                            descriptorRangeStarts[storageTableIndex] + tableOffsets[storageTableIndex]++);
                        var storageCpuHandle = _resourceDescriptorHeap->GetCPUDescriptorHandleForHeapStart();
                        storageCpuHandle.ptr = checked(
                            storageCpuHandle.ptr
                            + (checked((nuint)storageDescriptorIndex) * _resourceDescriptorIncrementSize));

                        if (useStorageBuffer)
                        {
                            var binding = sortedStorageBufferBindings[storageBufferIndex];
                            CreateStorageBufferView(
                                retainedStorageBuffers[storageBufferIndex],
                                binding.Offset,
                                binding.Range,
                                storageCpuHandle);
                            storageBufferIndex++;
                        }
                        else
                        {
                            CreateStorageImageView(
                                retainedStorageTextures[storageImageIndex],
                                sortedStorageImageBindings[storageImageIndex].Range,
                                storageCpuHandle);
                            storageImageIndex++;
                        }
                    }

                    for (var index = 0; index < sortedSamplerBindings.Length; index++)
                    {
                        var binding = sortedSamplerBindings[index];
                        var tableIndex = D3D12DescriptorLayout.GetTableIndex(binding.Group, ShaderResourceKind.Sampler);
                        var descriptorIndex = checked(descriptorRangeStarts[tableIndex] + tableOffsets[tableIndex]++);
                        var destination = _samplerDescriptorHeap->GetCPUDescriptorHandleForHeapStart();
                        destination.ptr = checked(
                            destination.ptr
                            + (checked((nuint)descriptorIndex) * _samplerDescriptorIncrementSize));
                        var source = _samplerDescriptorHeap->GetCPUDescriptorHandleForHeapStart();
                        source.ptr = checked(
                            source.ptr
                            + (checked((nuint)retainedSamplers[index].DescriptorSlot) * _samplerDescriptorIncrementSize));
                        _device->CopyDescriptorsSimple(
                            1,
                            destination,
                            source,
                            D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_SAMPLER);
                    }

                    var descriptorTableHandles = new D3D12_GPU_DESCRIPTOR_HANDLE[descriptorTableCount];

                    for (var tableIndex = 0; tableIndex < D3D12DescriptorLayout.TableCount; tableIndex++)
                    {
                        if (tableCounts[tableIndex] == 0)
                        {
                            continue;
                        }

                        var isSamplerTable = (tableIndex % 4) == 3;
                        var heap = isSamplerTable
                            ? _samplerDescriptorHeap
                            : _resourceDescriptorHeap;
                        var increment = isSamplerTable
                            ? _samplerDescriptorIncrementSize
                            : _resourceDescriptorIncrementSize;
                        var gpuHandle = heap->GetGPUDescriptorHandleForHeapStart();
                        gpuHandle.ptr = checked(
                            gpuHandle.ptr
                            + (checked((ulong)descriptorRangeStarts[tableIndex]) * increment));
                        descriptorTableHandles[rootParameterIndices[tableIndex]] = gpuHandle;
                    }

                    var result = new D3D12BackendDescriptorSet(
                        this,
                        selectedPipeline,
                        retainedBuffers,
                        retainedTextures,
                        retainedSamplers,
                        descriptorRangeStarts,
                        tableCounts,
                        descriptorTableHandles);
                    selectedPipeline.RetainDescriptorSet();
                    var retainedBufferCount = 0;
                    var retainedTextureCount = 0;
                    var retainedSamplerCount = 0;

                    try
                    {
                        for (; retainedBufferCount < retainedBuffers.Length; retainedBufferCount++)
                        {
                            retainedBuffers[retainedBufferCount].RetainDescriptorSet();
                        }

                        for (; retainedTextureCount < retainedTextures.Length; retainedTextureCount++)
                        {
                            retainedTextures[retainedTextureCount].RetainDescriptorSet();
                        }

                        for (; retainedSamplerCount < retainedSamplers.Length; retainedSamplerCount++)
                        {
                            retainedSamplers[retainedSamplerCount].RetainDescriptorSet();
                        }

                        _liveDescriptorSetCount = checked(_liveDescriptorSetCount + 1);
                    }
                    catch
                    {
                        for (var index = retainedSamplerCount - 1; index >= 0; index--)
                        {
                            retainedSamplers[index].ReleaseDescriptorSet();
                        }

                        for (var index = retainedTextureCount - 1; index >= 0; index--)
                        {
                            retainedTextures[index].ReleaseDescriptorSet();
                        }

                        for (var index = retainedBufferCount - 1; index >= 0; index--)
                        {
                            retainedBuffers[index].ReleaseDescriptorSet();
                        }

                        selectedPipeline.ReleaseDescriptorSet();
                        throw;
                    }

                    ownsDescriptorRanges = false;
                    return result;
                }
                finally
                {
                    if (ownsDescriptorRanges)
                    {
                        ReleaseDescriptorRanges(descriptorRangeStarts, tableCounts);
                    }
                }
            }

            internal void DestroyDescriptorSet(D3D12BackendDescriptorSet descriptorSet)
            {
                ArgumentNullException.ThrowIfNull(descriptorSet);
                EnsureOwnerThread();

                if (!ReferenceEquals(descriptorSet._owner, this))
                {
                    throw new ArgumentException("The descriptor set belongs to a different D3D12 device.", nameof(descriptorSet));
                }

                if (descriptorSet._isDisposed)
                {
                    return;
                }

                ObjectDisposedException.ThrowIf(_device is null, this);
                ReleaseDescriptorRanges(descriptorSet._rangeStarts, descriptorSet._rangeCounts);

                foreach (var backendSampler in descriptorSet._samplers)
                {
                    ((D3D12BackendSampler)backendSampler).ReleaseDescriptorSet();
                }

                foreach (var backendTexture in descriptorSet._textures)
                {
                    ((D3D12BackendTexture)backendTexture).ReleaseDescriptorSet();
                }

                foreach (var backendBuffer in descriptorSet._buffers)
                {
                    ((D3D12BackendBuffer)backendBuffer).ReleaseDescriptorSet();
                }

                var pipeline = descriptorSet._pipeline
                    ?? throw new InvalidOperationException("The D3D12 descriptor set has no pipeline owner.");
                pipeline.ReleaseDescriptorSet();
                descriptorSet.MarkDisposed();
                _liveDescriptorSetCount = checked(_liveDescriptorSetCount - 1);
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

            private void CreateSampledImageView(
                D3D12BackendTexture texture,
                GraphTextureRange range,
                bool imageArrayed,
                D3D12_CPU_DESCRIPTOR_HANDLE cpuHandle)
            {
                var description = texture.Description;
                var viewDescription = default(D3D12_SHADER_RESOURCE_VIEW_DESC);
                viewDescription.Format = (description.Format == TextureFormat.D32Float)
                    ? DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT
                    : GetNativeFormat(description.Format);
                viewDescription.ViewDimension = imageArrayed
                    ? D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2DARRAY
                    : D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2D;
                viewDescription.Shader4ComponentMapping = DefaultShader4ComponentMapping;
                var anonymous = viewDescription.Anonymous;

                if (imageArrayed)
                {
                    var textureView = anonymous.Texture2DArray;
                    textureView.MostDetailedMip = checked((uint)range.FirstMip);
                    textureView.MipLevels = checked((uint)range.MipCount);
                    textureView.FirstArraySlice = checked((uint)range.FirstLayer);
                    textureView.ArraySize = checked((uint)range.LayerCount);
                    textureView.PlaneSlice = 0;
                    textureView.ResourceMinLODClamp = 0.0f;
                    anonymous.Texture2DArray = textureView;
                }
                else
                {
                    var textureView = anonymous.Texture2D;
                    textureView.MostDetailedMip = checked((uint)range.FirstMip);
                    textureView.MipLevels = checked((uint)range.MipCount);
                    textureView.PlaneSlice = 0;
                    textureView.ResourceMinLODClamp = 0.0f;
                    anonymous.Texture2D = textureView;
                }

                viewDescription.Anonymous = anonymous;
                _device->CreateShaderResourceView(texture.GetResource(), &viewDescription, cpuHandle);
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
                    throw new OutOfMemoryException(
                        $"The D3D12 adapter's host-visible staging budget ({available} bytes) cannot cover the {allocationInfo.SizeBytes}-byte staging allocation.");
                }

                var nativeDescription = CreateNativeBufferDescription(description);
                return CreateCommittedResource(
                    nativeDescription,
                    GetHeapProperties(memoryKind),
                    GetInitialResourceState(memoryKind),
                    debugName);
            }

            private void CreateStorageBufferView(
                D3D12BackendBuffer buffer,
                long offset,
                long range,
                D3D12_CPU_DESCRIPTOR_HANDLE cpuHandle)
            {
                var structuredStride = buffer.Description.StructuredStride
                    ?? throw new NotSupportedException("This D3D12 descriptor-set path requires structured storage buffers.");
                var viewDescription = default(D3D12_UNORDERED_ACCESS_VIEW_DESC);
                viewDescription.Format = DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;
                viewDescription.ViewDimension = D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_BUFFER;
                var anonymous = viewDescription.Anonymous;
                var bufferView = anonymous.Buffer;
                bufferView.FirstElement = checked((ulong)(offset / structuredStride));
                bufferView.NumElements = checked((uint)(range / structuredStride));
                bufferView.StructureByteStride = checked((uint)structuredStride);
                bufferView.CounterOffsetInBytes = 0;
                bufferView.Flags = D3D12_BUFFER_UAV_FLAGS.D3D12_BUFFER_UAV_FLAG_NONE;
                anonymous.Buffer = bufferView;
                viewDescription.Anonymous = anonymous;
                _device->CreateUnorderedAccessView(buffer.GetResource(), null, &viewDescription, cpuHandle);
            }

            private void CreateStorageImageView(
                D3D12BackendTexture texture,
                GraphTextureRange range,
                D3D12_CPU_DESCRIPTOR_HANDLE cpuHandle)
            {
                var viewDescription = default(D3D12_UNORDERED_ACCESS_VIEW_DESC);
                viewDescription.Format = GetNativeFormat(texture.Description.Format);
                var anonymous = viewDescription.Anonymous;

                switch (texture.Description.Dimension)
                {
                    case TextureDimension.Dimension2D:
                    {
                        viewDescription.ViewDimension = D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_TEXTURE2D;
                        var textureView = anonymous.Texture2D;
                        textureView.MipSlice = checked((uint)range.FirstMip);
                        textureView.PlaneSlice = 0;
                        anonymous.Texture2D = textureView;
                        break;
                    }

                    case TextureDimension.Dimension3D:
                    {
                        viewDescription.ViewDimension = D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_TEXTURE3D;
                        var textureView = anonymous.Texture3D;
                        textureView.MipSlice = checked((uint)range.FirstMip);
                        textureView.FirstWSlice = 0;
                        textureView.WSize = checked((uint)Math.Max(1, texture.Description.Depth >> range.FirstMip));
                        anonymous.Texture3D = textureView;
                        break;
                    }

                    default:
                    {
                        throw new ArgumentOutOfRangeException(nameof(texture));
                    }
                }

                viewDescription.Anonymous = anonymous;
                _device->CreateUnorderedAccessView(texture.GetResource(), null, &viewDescription, cpuHandle);
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

            private void EnsureResourceDescriptorHeap()
            {
                if (_resourceDescriptorHeap is not null)
                {
                    return;
                }

                EnsureOwnerThread();
                var descriptorAllocator = new D3D12DescriptorRangeAllocator(ResourceDescriptorCapacity);
                var heapDescription = new D3D12_DESCRIPTOR_HEAP_DESC {
                    Type = D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV,
                    NumDescriptors = ResourceDescriptorCapacity,
                    Flags = D3D12_DESCRIPTOR_HEAP_FLAGS.D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE,
                    NodeMask = 0,
                };
                var heapId = typeof(ID3D12DescriptorHeap).GUID;
                ID3D12DescriptorHeap* descriptorHeap = null;
                ThrowIfFailed(_device->CreateDescriptorHeap(&heapDescription, &heapId, (void**)&descriptorHeap));

                if (descriptorHeap is null)
                {
                    throw new InvalidOperationException("D3D12 resource descriptor heap creation returned no heap.");
                }

                try
                {
                    var incrementSize = _device->GetDescriptorHandleIncrementSize(
                        D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);

                    if (incrementSize == 0)
                    {
                        throw new InvalidOperationException("D3D12 reported a zero resource descriptor increment.");
                    }

                    _resourceDescriptorHeap = descriptorHeap;
                    _resourceDescriptorIncrementSize = incrementSize;
                    _resourceDescriptorAllocator = descriptorAllocator;
                }
                catch
                {
                    _ = descriptorHeap->Release();
                    throw;
                }
            }

            private void EnsureSamplerDescriptorHeap()
            {
                if (_samplerDescriptorHeap is not null)
                {
                    return;
                }

                EnsureOwnerThread();
                var descriptorAllocator = new D3D12DescriptorRangeAllocator(SamplerDescriptorCapacity);

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
                    _samplerDescriptorAllocator = descriptorAllocator;
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
                if (_stagingBudgetBytesOverride is { } stagingBudgetBytesOverride)
                {
                    var availableBudgetBytes = stagingBudgetBytesOverride(memoryKind);
                    ArgumentOutOfRangeException.ThrowIfNegative(availableBudgetBytes.GetValueOrDefault());
                    return availableBudgetBytes;
                }

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

            private ID3D12Resource* GetReadbackStaging(
                nint byteLength,
                long alignment,
                out bool isDedicated,
                out ulong stagingOffset,
                out GraphicsStagingRingAllocator.Reservation reservation)
            {
                stagingOffset = 0;
                reservation = default;
                var existingRing = _readbackStagingResource is not null;
                var availableBudget = !existingRing && (byteLength <= StagingRingCapacityBytes)
                    ? GetAvailableBudgetBytes(GpuMemoryKind.Readback)
                    : null;
                isDedicated = D3D12StagingAdmission.ShouldUseDedicatedStaging(
                    checked((long)byteLength),
                    StagingRingCapacityBytes,
                    availableBudget,
                    existingRing);

                if (!isDedicated)
                {
                    EnsureReadbackStaging();
                    reservation = ReserveStagingRange(_readbackStagingAllocator, checked((long)byteLength), alignment);
                    stagingOffset = checked((ulong)reservation.Offset);
                    return _readbackStagingResource;
                }

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

                var rowCountAcrossSlices = checked((ulong)rowCount * footprint.Depth);
                var footprintByteLength = checked(
                    ((rowCountAcrossSlices - 1) * footprint.RowPitch)
                    + rowSizeBytes);
                var footprintEnd = checked(layout.Offset + footprintByteLength);

                if (footprintEnd > totalBytes)
                {
                    throw new InvalidOperationException("D3D12 returned an incomplete texture-copy footprint extent.");
                }

                return (layout, rowCount, rowSizeBytes, totalBytes);
            }

            private D3D12BackendFactory.D3D12TransferQueue GetTransferQueue()
            {
                _transferQueue ??= new D3D12BackendFactory.D3D12TransferQueue(
                    _device,
                    _graphFrameSlotCount,
                    _waitForSingleObject,
                    _graphWaitForSingleObject,
                    _completedFenceValueOverride);

                return _transferQueue;
            }

            private byte* GetUploadStaging(
                nint byteLength,
                long alignment,
                out ID3D12Resource* stagingResource,
                out bool isDedicated,
                out ulong stagingOffset,
                out GraphicsStagingRingAllocator.Reservation reservation)
            {
                stagingOffset = 0;
                reservation = default;
                var existingRing = _uploadStagingResource is not null;
                var availableBudget = !existingRing && (byteLength <= StagingRingCapacityBytes)
                    ? GetAvailableBudgetBytes(GpuMemoryKind.Upload)
                    : null;
                isDedicated = D3D12StagingAdmission.ShouldUseDedicatedStaging(
                    checked((long)byteLength),
                    StagingRingCapacityBytes,
                    availableBudget,
                    existingRing);

                if (!isDedicated)
                {
                    EnsureUploadStaging();
                    stagingResource = _uploadStagingResource;
                    reservation = ReserveStagingRange(_uploadStagingAllocator, checked((long)byteLength), alignment);
                    stagingOffset = checked((ulong)reservation.Offset);
                    return _uploadStagingMapped;
                }

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

            private void ReleaseDescriptorRanges(int[] rangeStarts, int[] rangeCounts)
            {
                for (var tableIndex = D3D12DescriptorLayout.TableCount - 1; tableIndex >= 0; tableIndex--)
                {
                    if ((rangeCounts[tableIndex] == 0) || (rangeStarts[tableIndex] < 0))
                    {
                        continue;
                    }

                    var allocator = ((tableIndex % 4) == 3)
                        ? _samplerDescriptorAllocator
                        : _resourceDescriptorAllocator;
                    (allocator
                        ?? throw new InvalidOperationException("The D3D12 descriptor heap range allocator is unavailable."))
                        .Release(rangeStarts[tableIndex], rangeCounts[tableIndex]);
                    rangeStarts[tableIndex] = -1;
                }
            }

            private void ReleaseSamplerDescriptor(int slot)
            {
                EnsureOwnerThread();

                if ((uint)slot >= SamplerDescriptorCapacity)
                {
                    throw new ArgumentOutOfRangeException(nameof(slot));
                }

                var descriptorAllocator = _samplerDescriptorAllocator
                    ?? throw new InvalidOperationException("The D3D12 sampler descriptor heap has no free-slot owner.");
                descriptorAllocator.Release(slot, 1);
            }

            private GraphicsStagingRingAllocator.Reservation ReserveStagingRange(
                GraphicsStagingRingAllocator allocator,
                long byteLength,
                long alignment)
            {
                var transferQueue = GetTransferQueue();
                allocator.RetireCompleted(transferQueue.GetCompletedFence());

                if (allocator.TryReserve(byteLength, alignment, out var reservation))
                {
                    return reservation;
                }

                if (allocator.OldestFenceValue is long oldestFenceValue)
                {
                    transferQueue.WaitForFence(oldestFenceValue);
                    allocator.RetireCompleted(transferQueue.GetCompletedFence());

                    if (allocator.TryReserve(byteLength, alignment, out reservation))
                    {
                        return reservation;
                    }
                }

                throw new InvalidOperationException("A D3D12 staging ring is exhausted by live ranges.");
            }

            internal sealed class D3D12BackendDescriptorSet : IDisposable
            {
                internal readonly IGraphicsBackendBuffer[] _buffers;
                internal readonly D3D12_GPU_DESCRIPTOR_HANDLE[] _descriptorTableHandles;
                internal bool _isDisposed;
                internal readonly D3D12BackendDevice _owner;
                internal ID3D12BackendPipelineDescriptorOwner? _pipeline;
                internal readonly int[] _rangeCounts;
                internal readonly int[] _rangeStarts;
                internal readonly IGraphicsBackendSampler[] _samplers;
                internal readonly IGraphicsBackendTexture[] _textures;

                internal D3D12BackendDescriptorSet(
                    D3D12BackendDevice owner,
                    ID3D12BackendPipelineDescriptorOwner pipeline,
                    IGraphicsBackendBuffer[] buffers,
                    IGraphicsBackendTexture[] textures,
                    IGraphicsBackendSampler[] samplers,
                    int[] rangeStarts,
                    int[] rangeCounts,
                    D3D12_GPU_DESCRIPTOR_HANDLE[] descriptorTableHandles)
                {
                    ArgumentNullException.ThrowIfNull(owner);
                    ArgumentNullException.ThrowIfNull(pipeline);
                    ArgumentNullException.ThrowIfNull(buffers);
                    ArgumentNullException.ThrowIfNull(textures);
                    ArgumentNullException.ThrowIfNull(samplers);
                    ArgumentNullException.ThrowIfNull(rangeStarts);
                    ArgumentNullException.ThrowIfNull(rangeCounts);
                    ArgumentNullException.ThrowIfNull(descriptorTableHandles);
                    _owner = owner;
                    _pipeline = pipeline;
                    _buffers = buffers;
                    _textures = textures;
                    _samplers = samplers;
                    _rangeStarts = rangeStarts;
                    _rangeCounts = rangeCounts;
                    _descriptorTableHandles = descriptorTableHandles;
                }

                internal bool AreDescriptorTableHandlesValid
                {
                    get
                    {
                        for (var index = 0; index < _descriptorTableHandles.Length; index++)
                        {
                            if (_descriptorTableHandles[index].ptr == 0)
                            {
                                return false;
                            }
                        }

                        return true;
                    }
                }

                internal int BufferCount => _buffers.Length;

                internal int DescriptorTableCount => _descriptorTableHandles.Length;

                internal bool IsDisposed => _isDisposed;

                internal int SamplerCount => _samplers.Length;

                internal int TextureCount => _textures.Length;

                public void Dispose()
                {
                    if (!_isDisposed)
                    {
                        _owner.DestroyDescriptorSet(this);
                    }
                }

                internal void MarkDisposed()
                {
                    Array.Clear(_buffers);
                    Array.Clear(_textures);
                    Array.Clear(_samplers);
                    Array.Clear(_descriptorTableHandles);
                    Array.Clear(_rangeStarts);
                    Array.Clear(_rangeCounts);
                    _pipeline = null;
                    _isDisposed = true;
                }
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
                internal D3D12BackendBuffer(D3D12BackendDevice owner, ID3D12Resource* resource, BufferDescription description)
                    : base(owner, resource)
                {
                    ArgumentNullException.ThrowIfNull(description);
                    Description = description;
                }

                internal BufferDescription Description { get; }
            }

            private abstract unsafe class D3D12BackendResource : IGraphicsBackendResource
            {
                private int _descriptorSetReferenceCount;
                private readonly D3D12BackendDevice _owner;
                private ID3D12Resource* _resource;

                protected D3D12BackendResource(D3D12BackendDevice owner, ID3D12Resource* resource)
                {
                    ArgumentNullException.ThrowIfNull(owner);

                    if (resource is null)
                    {
                        throw new ArgumentNullException(nameof(resource));
                    }

                    _owner = owner;
                    _resource = resource;
                }

                internal bool IsDisposed => _resource is null;

                internal D3D12BackendDevice Owner => _owner;

                public virtual void Dispose()
                {
                    if (_descriptorSetReferenceCount != 0)
                    {
                        throw new InvalidOperationException("The D3D12 resource cannot be retired while descriptor sets remain live.");
                    }

                    if (_owner.HasUncertainTransferSubmission)
                    {
                        throw new InvalidOperationException("D3D12 resources cannot be retired while transfer completion is uncertain.");
                    }

                    var resource = _resource;

                    if (resource is not null)
                    {
                        ReleaseAdditionalNativeState();
                        _resource = null;
                        _ = resource->Release();
                    }
                }

                internal ID3D12Resource* GetResource()
                {
                    ObjectDisposedException.ThrowIf(_resource is null, this);
                    return _resource;
                }

                internal void ReleaseDescriptorSet()
                {
                    if (_descriptorSetReferenceCount <= 0)
                    {
                        throw new InvalidOperationException("The D3D12 resource descriptor-set reference count is invalid.");
                    }

                    _descriptorSetReferenceCount--;
                }

                internal void RetainDescriptorSet()
                {
                    ObjectDisposedException.ThrowIf(_resource is null, this);
                    _descriptorSetReferenceCount = checked(_descriptorSetReferenceCount + 1);
                }

                protected virtual void ReleaseAdditionalNativeState()
                {
                }
            }

            private sealed unsafe class D3D12BackendResourceAllocation :
                IGraphicsBackendBufferAllocation,
                IGraphicsBackendTextureAllocation
            {
                private ID3D12Heap* _heap;
                private readonly D3D12BackendDevice _owner;

                internal D3D12BackendResourceAllocation(D3D12BackendDevice owner, ID3D12Heap* heap)
                {
                    ArgumentNullException.ThrowIfNull(owner);

                    if (heap is null)
                    {
                        throw new ArgumentNullException(nameof(heap));
                    }

                    _owner = owner;
                    _heap = heap;
                }

                public void Dispose()
                {
                    _owner.EnsureOwnerThread();
                    var heap = _heap;
                    _heap = null;

                    if (heap is not null)
                    {
                        _ = heap->Release();
                    }
                }

                internal ID3D12Heap* GetHeap(D3D12BackendDevice owner)
                {
                    ArgumentNullException.ThrowIfNull(owner);
                    owner.EnsureOwnerThread();

                    if (!ReferenceEquals(owner, _owner))
                    {
                        throw new ArgumentException("The transient resource allocation belongs to a different D3D12 device.", nameof(owner));
                    }

                    ObjectDisposedException.ThrowIf(_heap is null, this);
                    return _heap;
                }
            }

            private sealed class D3D12BackendSampler : IGraphicsBackendSampler
            {
                private int _descriptorSetReferenceCount;
                private readonly int _descriptorSlot;
                private D3D12BackendDevice? _device;

                internal D3D12BackendSampler(D3D12BackendDevice device, int descriptorSlot)
                {
                    ArgumentNullException.ThrowIfNull(device);
                    _device = device;
                    _descriptorSlot = descriptorSlot;
                }

                internal int DescriptorSlot => _descriptorSlot;

                internal bool IsDisposed => _device is null;

                internal D3D12BackendDevice? Owner => _device;

                public void Dispose()
                {
                    var device = _device;

                    if (device is null)
                    {
                        return;
                    }

                    if (_descriptorSetReferenceCount != 0)
                    {
                        throw new InvalidOperationException("The D3D12 sampler cannot be retired while descriptor sets remain live.");
                    }

                    device.ReleaseSamplerDescriptor(_descriptorSlot);
                    _device = null;
                }

                internal void ReleaseDescriptorSet()
                {
                    if (_descriptorSetReferenceCount <= 0)
                    {
                        throw new InvalidOperationException("The D3D12 sampler descriptor-set reference count is invalid.");
                    }

                    _descriptorSetReferenceCount--;
                }

                internal void RetainDescriptorSet()
                {
                    ObjectDisposedException.ThrowIf(_device is null, this);
                    _descriptorSetReferenceCount = checked(_descriptorSetReferenceCount + 1);
                }
            }

            private sealed class D3D12BackendTexture : D3D12BackendResource, IGraphicsBackendTexture
            {
                private ID3D12DescriptorHeap* _depthStencilViewHeap;

                internal D3D12BackendTexture(D3D12BackendDevice owner, ID3D12Resource* resource, TextureDescription description)
                    : base(owner, resource)
                {
                    ArgumentNullException.ThrowIfNull(description);
                    Description = description;
                }

                internal TextureDescription Description { get; }

                internal D3D12_CPU_DESCRIPTOR_HANDLE GetDepthStencilView()
                {
                    if ((Description.Format != TextureFormat.D32Float)
                        || ((Description.Usage & TextureUsage.DepthAttachment) == 0))
                    {
                        throw new InvalidOperationException("The D3D12 texture is not a D32Float depth attachment.");
                    }

                    Owner.EnsureOwnerThread();
                    var resource = GetResource();

                    if (_depthStencilViewHeap is null)
                    {
                        var heapDescription = new D3D12_DESCRIPTOR_HEAP_DESC {
                            Type = D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_DSV,
                            NumDescriptors = 1,
                            Flags = D3D12_DESCRIPTOR_HEAP_FLAGS.D3D12_DESCRIPTOR_HEAP_FLAG_NONE,
                            NodeMask = 0,
                        };
                        var heapId = typeof(ID3D12DescriptorHeap).GUID;
                        ID3D12DescriptorHeap* heap = null;

                        try
                        {
                            ThrowIfFailed(Owner._device->CreateDescriptorHeap(&heapDescription, &heapId, (void**)&heap));

                            if (heap is null)
                            {
                                throw new InvalidOperationException("D3D12 depth-stencil descriptor heap creation returned no heap.");
                            }

                            var viewDescription = new D3D12_DEPTH_STENCIL_VIEW_DESC {
                                Format = GetNativeFormat(TextureFormat.D32Float),
                                ViewDimension = D3D12_DSV_DIMENSION.D3D12_DSV_DIMENSION_TEXTURE2D,
                                Texture2D = new D3D12_TEX2D_DSV {
                                    MipSlice = 0,
                                },
                            };
                            var view = heap->GetCPUDescriptorHandleForHeapStart();
                            Owner._device->CreateDepthStencilView(resource, &viewDescription, view);
                            _depthStencilViewHeap = heap;
                            heap = null;
                        }
                        finally
                        {
                            if (heap is not null)
                            {
                                _ = heap->Release();
                            }
                        }
                    }

                    return _depthStencilViewHeap->GetCPUDescriptorHandleForHeapStart();
                }

                protected override void ReleaseAdditionalNativeState()
                {
                    if (_depthStencilViewHeap is not null)
                    {
                        _ = _depthStencilViewHeap->Release();
                        _depthStencilViewHeap = null;
                    }
                }
            }

            private sealed unsafe class D3D12BufferReadback : IGraphicsBackendBufferReadback
            {
                private readonly nint _byteLength;
                private ID3D12CommandAllocator* _commandAllocator;
                private ID3D12GraphicsCommandList* _commandList;
                private long _completionFence;
                private readonly D3D12BackendDevice _device;
                private readonly bool _isDedicated;
                private readonly ulong _stagingOffset;
                private readonly GraphicsStagingRingAllocator.Reservation _stagingReservation;
                private ID3D12Resource* _stagingResource;
                private int _state;
                private readonly D3D12BackendFactory.D3D12TransferQueue _transferQueue;

                internal D3D12BufferReadback(
                    D3D12BackendDevice device,
                    D3D12BackendFactory.D3D12TransferQueue transferQueue,
                    ID3D12Resource* stagingResource,
                    bool isDedicated,
                    ulong stagingOffset,
                    nint byteLength,
                    GraphicsStagingRingAllocator.Reservation stagingReservation)
                {
                    _byteLength = byteLength;
                    _device = device;
                    _isDedicated = isDedicated;
                    _stagingOffset = stagingOffset;
                    _stagingReservation = stagingReservation;
                    _stagingResource = stagingResource;
                    _transferQueue = transferQueue;
                }

                public long CompletionFence => Volatile.Read(ref _completionFence);

                public void CopyCompleted(BufferWriteLease<byte> destination)
                {
                    ArgumentNullException.ThrowIfNull(destination);
                    _device.EnsureOwnerThread();
                    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(CompletionFence);

                    if (destination.Length != _byteLength)
                    {
                        throw new ArgumentException("The readback destination has an unexpected byte extent.", nameof(destination));
                    }

                    if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
                    {
                        throw new InvalidOperationException("The D3D12 buffer readback has already been copied or disposed.");
                    }

                    var readRange = new D3D12_RANGE {
                        Begin = checked((nuint)_stagingOffset),
                        End = checked((nuint)(_stagingOffset + checked((ulong)_byteLength))),
                    };
                    void* mapped = null;
                    var stagingResource = _stagingResource;
                    ObjectDisposedException.ThrowIf(stagingResource is null, this);
                    ThrowIfFailed(stagingResource->Map(0, &readRange, &mapped));

                    if (mapped is null)
                    {
                        throw new InvalidOperationException("D3D12 readback staging returned no mapped storage.");
                    }

                    try
                    {
                        CopyMappedStorageToLease(
                            (byte*)mapped + checked((nint)_stagingOffset),
                            _byteLength,
                            destination);
                    }
                    finally
                    {
                        var writtenRange = new D3D12_RANGE {
                            Begin = 0,
                            End = 0,
                        };
                        stagingResource->Unmap(0, &writtenRange);
                    }
                }

                public void Dispose()
                {
                    _device.EnsureOwnerThread();

                    if (Volatile.Read(ref _state) == 2)
                    {
                        return;
                    }

                    if (Volatile.Read(ref _state) != 1)
                    {
                        throw new InvalidOperationException("The D3D12 buffer readback cannot be disposed before completion is copied.");
                    }

                    ReleaseCommandStorage();

                    var stagingResource = _stagingResource;

                    if (stagingResource is not null)
                    {
                        if (_isDedicated)
                        {
                            _device.ReleaseDedicatedStagingResource(
                                stagingResource,
                                isMapped: false,
                                writtenByteLength: 0,
                                _transferQueue);
                        }

                        D3D12BackendDevice.ReleaseStagingRange(
                            _device._readbackStagingAllocator,
                            _stagingReservation,
                            CompletionFence,
                            _transferQueue.HasUncertainSubmission);
                        _stagingResource = null;
                    }

                    if (!_device._activeBufferReadbacks.Remove(this))
                    {
                        throw new InvalidOperationException("The D3D12 buffer-readback owner was not registered.");
                    }

                    Volatile.Write(ref _state, 2);
                }

                internal void AbortUnsubmitted(bool registered)
                {
                    _device.EnsureOwnerThread();

                    if ((Volatile.Read(ref _state) != 0) || (CompletionFence != 0))
                    {
                        throw new InvalidOperationException("Only an unsubmitted D3D12 buffer readback can be aborted.");
                    }

                    try
                    {
                        ReleaseCommandStorage();
                        var stagingResource = _stagingResource;

                        if (stagingResource is not null)
                        {
                            if (_isDedicated)
                            {
                                _device.ReleaseDedicatedStagingResource(
                                    stagingResource,
                                    isMapped: false,
                                    writtenByteLength: 0,
                                    _transferQueue);
                            }

                            D3D12BackendDevice.ReleaseStagingRange(
                                _device._readbackStagingAllocator,
                                _stagingReservation,
                                completedFenceValue: null,
                                hasUncertainSubmission: false);
                            _stagingResource = null;
                        }

                        if (registered && !_device._activeBufferReadbacks.Remove(this))
                        {
                            throw new InvalidOperationException("The D3D12 buffer-readback owner was not registered.");
                        }

                        Volatile.Write(ref _state, 2);
                    }
                    catch
                    {
                        _transferQueue.MarkUncertainSubmission();
                        throw;
                    }
                }

                internal void MarkSubmitted(long completionFence)
                {
                    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(completionFence);
                    Volatile.Write(ref _completionFence, completionFence);
                }

                internal void SetCommandStorage(
                    ID3D12CommandAllocator* commandAllocator,
                    ID3D12GraphicsCommandList* commandList)
                {
                    _commandAllocator = commandAllocator;
                    _commandList = commandList;
                }

                private void ReleaseCommandStorage()
                {
                    var commandList = _commandList;
                    _commandList = null;

                    if (commandList is not null)
                    {
                        _ = commandList->Release();
                    }

                    var commandAllocator = _commandAllocator;
                    _commandAllocator = null;

                    if (commandAllocator is not null)
                    {
                        _ = commandAllocator->Release();
                    }
                }
            }

            private readonly struct ReadbackCopyAction(byte* source) : IAction<Span<byte>>
            {
                private readonly byte* _source = source;

                public void Invoke(scoped Span<byte> argument)
                    => new ReadOnlySpan<byte>(_source, argument.Length).CopyTo(argument);
            }
        }

        private unsafe class D3D12PipelineBuilder
        {
            internal static IGraphicsBackendPipeline Create(
                D3D12BackendDevice owner,
                ID3D12Device* device,
                PipelineDescription description,
                ReadOnlySpan<BufferReadLease<byte>> bytecodeLeases)
            {
                ArgumentNullException.ThrowIfNull(owner);

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

                    rootSignature = CreateDescriptorRootSignature(
                        device,
                        shaderInputs,
                        out var pipelineResources);

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

                    var result = new D3D12BackendPipeline(
                        owner,
                        pipelineState,
                        rootSignature,
                        pipelineResources);
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

            private static ID3D12RootSignature* CreateDescriptorRootSignature(
                ID3D12Device* device,
                ImmutableArray<ShaderBytecodeInput> shaderInputs,
                out ImmutableArray<ShaderResourceSignature> resourceSignatures)
            {
                var resources = new List<ShaderResourceSignature>();

                for (var shaderIndex = 0; shaderIndex < shaderInputs.Length; shaderIndex++)
                {
                    var shaderResources = shaderInputs[shaderIndex].Reflection.Resources;

                    for (var resourceIndex = 0; resourceIndex < shaderResources.Length; resourceIndex++)
                    {
                        var resource = shaderResources[resourceIndex];
                        var resourceTableIndex = D3D12DescriptorLayout.GetTableIndex(resource);
                        var existingResourceFound = false;

                        for (var existingIndex = 0; existingIndex < resources.Count; existingIndex++)
                        {
                            var existingResource = resources[existingIndex];

                            if ((D3D12DescriptorLayout.GetTableIndex(existingResource) == resourceTableIndex)
                                && (existingResource.Binding == resource.Binding))
                            {
                                if ((existingResource.Kind != resource.Kind)
                                    || (existingResource.ArrayCount != resource.ArrayCount))
                                {
                                    throw new ArgumentException("Shader stages disagree on a D3D12 register binding.", nameof(shaderInputs));
                                }

                                existingResourceFound = true;
                                break;
                            }
                        }

                        if (!existingResourceFound)
                        {
                            resources.Add(resource);
                        }
                    }
                }

                resources.Sort(D3D12DescriptorLayout.Compare);
                resourceSignatures = [.. resources];
                var descriptorRanges = stackalloc D3D12_DESCRIPTOR_RANGE[Math.Max(1, resources.Count)];
                var rangeStarts = stackalloc int[D3D12DescriptorLayout.TableCount];
                var rangeCounts = stackalloc uint[D3D12DescriptorLayout.TableCount];
                NativeMemory.Clear(rangeStarts, checked((nuint)D3D12DescriptorLayout.TableCount * (nuint)sizeof(int)));
                NativeMemory.Clear(rangeCounts, checked((nuint)D3D12DescriptorLayout.TableCount * (nuint)sizeof(uint)));
                var descriptorRangeCount = 0;

                for (var index = 0; index < resources.Count; index++)
                {
                    var resource = resources[index];
                    var tableIndex = D3D12DescriptorLayout.GetTableIndex(resource);

                    if (rangeCounts[tableIndex] == 0)
                    {
                        rangeStarts[tableIndex] = descriptorRangeCount;
                    }

                    var descriptorRange = default(D3D12_DESCRIPTOR_RANGE);
                    descriptorRange.RangeType = D3D12DescriptorLayout.GetRangeType(resource.Kind);
                    descriptorRange.NumDescriptors = checked((uint)resource.ArrayCount);
                    descriptorRange.BaseShaderRegister = checked((uint)resource.Binding);
                    descriptorRange.RegisterSpace = checked((uint)D3D12DescriptorLayout.GetRegisterSpace(resource.Group));
                    descriptorRange.OffsetInDescriptorsFromTableStart = uint.MaxValue;
                    descriptorRanges[descriptorRangeCount++] = descriptorRange;
                    rangeCounts[tableIndex] = checked(rangeCounts[tableIndex] + 1);
                }

                var rootParameters = stackalloc D3D12_ROOT_PARAMETER[D3D12DescriptorLayout.TableCount];
                NativeMemory.Clear(rootParameters, checked((nuint)D3D12DescriptorLayout.TableCount * (nuint)sizeof(D3D12_ROOT_PARAMETER)));
                var rootParameterCount = 0;

                for (var tableIndex = 0; tableIndex < D3D12DescriptorLayout.TableCount; tableIndex++)
                {
                    if (rangeCounts[tableIndex] == 0)
                    {
                        continue;
                    }

                    var rootParameter = default(D3D12_ROOT_PARAMETER);
                    rootParameter.ParameterType = D3D12_ROOT_PARAMETER_TYPE.D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
                    rootParameter.ShaderVisibility = D3D12_SHADER_VISIBILITY.D3D12_SHADER_VISIBILITY_ALL;
                    var descriptorTable = rootParameter.Anonymous.DescriptorTable;
                    descriptorTable.NumDescriptorRanges = rangeCounts[tableIndex];
                    descriptorTable.pDescriptorRanges = descriptorRanges + rangeStarts[tableIndex];
                    var anonymous = rootParameter.Anonymous;
                    anonymous.DescriptorTable = descriptorTable;
                    rootParameter.Anonymous = anonymous;
                    rootParameters[rootParameterCount++] = rootParameter;
                }

                var rootSignatureDescription = new D3D12_ROOT_SIGNATURE_DESC {
                    NumParameters = checked((uint)rootParameterCount),
                    pParameters = rootParameterCount == 0 ? null : rootParameters,
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

            private sealed unsafe class D3D12BackendPipeline : ID3D12BackendPipelineDescriptorOwner
            {
                private int _descriptorSetReferenceCount;
                private readonly D3D12BackendDevice _owner;
                private ID3D12PipelineState* _pipelineState;
                private readonly ImmutableArray<ShaderResourceSignature> _resources;
                private ID3D12RootSignature* _rootSignature;

                internal D3D12BackendPipeline(
                    D3D12BackendDevice owner,
                    ID3D12PipelineState* pipelineState,
                    ID3D12RootSignature* rootSignature,
                    ImmutableArray<ShaderResourceSignature> resources)
                {
                    ArgumentNullException.ThrowIfNull(owner);

                    if (pipelineState is null)
                    {
                        throw new ArgumentNullException(nameof(pipelineState));
                    }

                    if (rootSignature is null)
                    {
                        throw new ArgumentNullException(nameof(rootSignature));
                    }

                    if (resources.IsDefault)
                    {
                        throw new ArgumentException("D3D12 pipeline resource signatures must be initialized.", nameof(resources));
                    }

                    _owner = owner;
                    _pipelineState = pipelineState;
                    _resources = resources;
                    _rootSignature = rootSignature;
                }

                public bool IsDisposed => _pipelineState is null;

                public D3D12BackendDevice Owner => _owner;

                public ImmutableArray<ShaderResourceSignature> Resources => _resources;

                ID3D12PipelineState* ID3D12BackendPipelineDescriptorOwner.PipelineState => _pipelineState;

                ID3D12RootSignature* ID3D12BackendPipelineDescriptorOwner.RootSignature => _rootSignature;

                public void Dispose()
                {
                    if (_descriptorSetReferenceCount != 0)
                    {
                        throw new InvalidOperationException("The D3D12 pipeline cannot be retired while descriptor sets remain live.");
                    }

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

                public void ReleaseDescriptorSet()
                {
                    if (_descriptorSetReferenceCount <= 0)
                    {
                        throw new InvalidOperationException("The D3D12 pipeline descriptor-set reference count is invalid.");
                    }

                    _descriptorSetReferenceCount--;
                }

                public void RetainDescriptorSet()
                {
                    ObjectDisposedException.ThrowIf(_pipelineState is null, this);
                    _descriptorSetReferenceCount = checked(_descriptorSetReferenceCount + 1);
                }
            }
        }

        private sealed unsafe class D3D12TransferQueue : IDisposable
        {
            private const uint TransferFenceTimeoutMilliseconds = 30_000;

            private ID3D12GraphicsCommandList* _activeCommandList;
            private int _activeGraphFrameSlotIndex = -1;
            private ID3D12CommandAllocator* _activeReadbackAllocator;
            private ID3D12CommandAllocator* _commandAllocator;
            private ID3D12GraphicsCommandList* _commandList;
            private ID3D12CommandQueue* _commandQueue;
            private readonly Func<long, long>? _completedFenceValueOverride;
            private ID3D12Device* _device;
            private ID3D12Fence* _fence;
            private HANDLE _fenceEvent;
            private readonly int _graphFrameSlotCount;
            private readonly GraphFrameSlot[] _graphFrameSlots;
            private readonly Func<nint, uint, uint>? _graphWaitForSingleObject;
            private bool _hasUncertainSubmission;
            private bool _isDisposed;
            private bool _isRecording;
            private ulong _nextFenceValue;
            private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
            private readonly Func<nint, uint, uint>? _waitForSingleObject;

            internal D3D12TransferQueue(
                ID3D12Device* device,
                int graphFrameSlotCount,
                Func<nint, uint, uint>? waitForSingleObject,
                Func<nint, uint, uint>? graphWaitForSingleObject,
                Func<long, long>? completedFenceValueOverride)
            {
                if (device is null)
                {
                    throw new ArgumentNullException(nameof(device));
                }

                _waitForSingleObject = waitForSingleObject;
                _graphWaitForSingleObject = graphWaitForSingleObject;
                _completedFenceValueOverride = completedFenceValueOverride;
                _graphFrameSlotCount = graphFrameSlotCount;
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
                var graphFrameSlots = new GraphFrameSlot[graphFrameSlotCount];
                var createdGraphFrameSlotCount = 0;
                var graphFrameSlotsTransferred = false;
                HANDLE fenceEvent = default;
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

                    for (var index = 0; index < graphFrameSlotCount; index++)
                    {
                        ID3D12CommandAllocator* graphAllocator = null;
                        ID3D12GraphicsCommandList* graphCommandList = null;

                        try
                        {
                            ThrowIfFailed(device->CreateCommandAllocator(
                                queueDescription.Type,
                                &allocatorId,
                                (void**)&graphAllocator));
                            ThrowIfFailed(device->CreateCommandList(
                                0,
                                queueDescription.Type,
                                graphAllocator,
                                null,
                                &commandListId,
                                (void**)&graphCommandList));

                            if ((graphAllocator is null) || (graphCommandList is null))
                            {
                                throw new InvalidOperationException("D3D12 graph frame-slot creation returned incomplete command storage.");
                            }

                            ThrowIfFailed(graphCommandList->Close());
                            graphFrameSlots[index] = new GraphFrameSlot(graphAllocator, graphCommandList);
                            graphAllocator = null;
                            graphCommandList = null;
                            createdGraphFrameSlotCount++;
                        }
                        finally
                        {
                            if (graphCommandList is not null)
                            {
                                _ = graphCommandList->Release();
                            }

                            if (graphAllocator is not null)
                            {
                                _ = graphAllocator->Release();
                            }
                        }
                    }

                    fenceEvent = Windows.CreateEventW(null, false, false, null);

                    if (fenceEvent.Value is null)
                    {
                        ThrowLastWin32Error("Creating the D3D12 transfer fence event failed.");
                    }

                    _commandQueue = commandQueue;
                    _commandAllocator = commandAllocator;
                    _commandList = commandList;
                    _graphFrameSlots = graphFrameSlots;
                    graphFrameSlotsTransferred = true;
                    _fence = fence;
                    _fenceEvent = fenceEvent;
                    _device = device;
                    commandQueue = null;
                    commandAllocator = null;
                    commandList = null;
                    fenceEvent = default;
                    fence = null;
                }
                catch
                {
                    if ((fenceEvent.Value is not null) && !Windows.CloseHandle(fenceEvent))
                    {
                        ThrowLastWin32Error("Closing the failed D3D12 transfer fence event failed.");
                    }

                    if (!graphFrameSlotsTransferred)
                    {
                        for (var index = createdGraphFrameSlotCount - 1; index >= 0; index--)
                        {
                            graphFrameSlots[index].Dispose();
                        }
                    }

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

            internal int ActiveGraphFrameSlotIndex => _activeGraphFrameSlotIndex;
            internal ID3D12CommandQueue* CommandQueue => _commandQueue;
            internal bool HasUncertainSubmission => _hasUncertainSubmission;
            internal bool IsRecording => _isRecording;

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
                var completedFence = checked((ulong)GetCompletedFence());

                foreach (var graphFrameSlot in _graphFrameSlots)
                {
                    if (graphFrameSlot.FenceValue > completedFence)
                    {
                        throw new InvalidOperationException("The D3D12 graph frame slot still has pending GPU work.");
                    }
                }

                if ((_fenceEvent.Value is not null) && !Windows.CloseHandle(_fenceEvent))
                {
                    ThrowLastWin32Error("Closing the D3D12 transfer fence event failed.");
                }

                _fenceEvent = default;

                var fence = _fence;
                var commandList = _commandList;
                var commandAllocator = _commandAllocator;
                var commandQueue = _commandQueue;

                for (var index = _graphFrameSlots.Length - 1; index >= 0; index--)
                {
                    _graphFrameSlots[index].Dispose();
                }

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

            internal ID3D12GraphicsCommandList* BeginGraphRecording()
            {
                EnsureOwnerThread();

                if (_hasUncertainSubmission)
                {
                    throw new InvalidOperationException("The D3D12 transfer queue is quarantined after an uncertain submission.");
                }

                if (_isRecording)
                {
                    throw new InvalidOperationException("A D3D12 command list is already recording.");
                }

                var completedFence = checked((ulong)GetCompletedFence());

                for (var index = 0; index < _graphFrameSlots.Length; index++)
                {
                    var graphFrameSlot = _graphFrameSlots[index];

                    if (graphFrameSlot.FenceValue > completedFence)
                    {
                        continue;
                    }

                    ThrowIfFailed(graphFrameSlot.CommandAllocator->Reset());
                    ThrowIfFailed(graphFrameSlot.CommandList->Reset(graphFrameSlot.CommandAllocator, null));
                    graphFrameSlot.FenceValue = 0;
                    _activeCommandList = graphFrameSlot.CommandList;
                    _activeGraphFrameSlotIndex = index;
                    _isRecording = true;
                    return _activeCommandList;
                }

                throw new InvalidOperationException("The configured D3D12 graph frame slots are all in flight.");
            }

            internal ID3D12GraphicsCommandList* BeginReadbackRecording(
                out ID3D12CommandAllocator* commandAllocator)
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

                var allocatorId = typeof(ID3D12CommandAllocator).GUID;
                var commandListId = typeof(ID3D12GraphicsCommandList).GUID;
                ID3D12CommandAllocator* allocator = null;
                ID3D12GraphicsCommandList* commandList = null;

                try
                {
                    ThrowIfFailed(_device->CreateCommandAllocator(
                        D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT,
                        &allocatorId,
                        (void**)&allocator));
                    ThrowIfFailed(_device->CreateCommandList(
                        0,
                        D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT,
                        allocator,
                        null,
                        &commandListId,
                        (void**)&commandList));

                    if ((allocator is null) || (commandList is null))
                    {
                        throw new InvalidOperationException("D3D12 readback command storage was incomplete.");
                    }

                    _activeCommandList = commandList;
                    _activeReadbackAllocator = allocator;
                    _activeGraphFrameSlotIndex = -1;
                    _isRecording = true;
                    commandAllocator = allocator;
                    return commandList;
                }
                catch
                {
                    if (commandList is not null)
                    {
                        _ = commandList->Release();
                    }

                    if (allocator is not null)
                    {
                        _ = allocator->Release();
                    }

                    commandAllocator = null;
                    throw;
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
                _activeCommandList = _commandList;
                _activeGraphFrameSlotIndex = -1;
                _isRecording = true;
                return _activeCommandList;
            }

            internal long GetCompletedFence()
            {
                EnsureOwnerThread();
                var completedFenceValue = _fence->GetCompletedValue();

                if (completedFenceValue == ulong.MaxValue)
                {
                    var deviceRemovedReason = _device->GetDeviceRemovedReason();
                    throw new GraphicsDeviceLostException(
                        deviceRemovedReason.FAILED
                            ? deviceRemovedReason.Value
                            : DxgiErrorDeviceRemoved);
                }

                var completedFence = checked((long)completedFenceValue);

                if (_completedFenceValueOverride is Func<long, long> completedFenceValueOverride)
                {
                    var overriddenCompletedFence = completedFenceValueOverride(completedFence);

                    if ((overriddenCompletedFence < 0) || (overriddenCompletedFence > completedFence))
                    {
                        throw new InvalidOperationException("The D3D12 graph-fence test override reported an invalid value.");
                    }

                    return overriddenCompletedFence;
                }

                return completedFence;
            }

            internal void MarkUncertainSubmission()
            {
                EnsureOwnerThread();
                _hasUncertainSubmission = true;
            }

            internal void StopRecording()
            {
                EnsureOwnerThread();

                if (_isRecording)
                {
                    _isRecording = false;
                    ThrowIfFailed(_activeCommandList->Close());
                    _activeCommandList = null;
                    _activeReadbackAllocator = null;
                    _activeGraphFrameSlotIndex = -1;
                }
            }

            internal long SubmitAndWait(Action<long>? onSubmitted = null)
            {
                EnsureOwnerThread();

                if (!_isRecording)
                {
                    throw new InvalidOperationException("The D3D12 transfer command list is not recording.");
                }

                var commandList = _activeCommandList;
                ThrowIfFailed(commandList->Close());
                _isRecording = false;
                _activeCommandList = null;
                _activeGraphFrameSlotIndex = -1;

                if (_nextFenceValue >= long.MaxValue)
                {
                    throw new InvalidOperationException("The D3D12 transfer fence value space is exhausted.");
                }

                var fenceValue = ++_nextFenceValue;
                _hasUncertainSubmission = true;
                var commandLists = stackalloc ID3D12CommandList*[1];
                commandLists[0] = (ID3D12CommandList*)commandList;
                _commandQueue->ExecuteCommandLists(1, commandLists);
                var signalResult = _commandQueue->Signal(_fence, fenceValue);

                if (signalResult.FAILED)
                {
                    throw new GraphicsDeviceLostException(signalResult.Value);
                }

                onSubmitted?.Invoke(checked((long)fenceValue));
                var eventResult = _fence->SetEventOnCompletion(fenceValue, _fenceEvent);

                if (eventResult.FAILED)
                {
                    throw new GraphicsDeviceLostException(eventResult.Value);
                }

                var waitResult = _waitForSingleObject is Func<nint, uint, uint> waitForSingleObject
                    ? waitForSingleObject(_fenceEvent, TransferFenceTimeoutMilliseconds)
                    : Windows.WaitForSingleObject(_fenceEvent, TransferFenceTimeoutMilliseconds);

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

            internal long SubmitGraphAndSignal()
            {
                EnsureOwnerThread();

                if (!_isRecording || (_activeGraphFrameSlotIndex < 0))
                {
                    throw new InvalidOperationException("A D3D12 graph frame slot is not recording.");
                }

                var graphFrameSlot = _graphFrameSlots[_activeGraphFrameSlotIndex];
                var commandList = _activeCommandList;
                ThrowIfFailed(commandList->Close());
                _isRecording = false;
                _activeCommandList = null;
                _activeGraphFrameSlotIndex = -1;

                if (_nextFenceValue >= long.MaxValue)
                {
                    throw new InvalidOperationException("The D3D12 transfer fence value space is exhausted.");
                }

                var fenceValue = ++_nextFenceValue;
                _hasUncertainSubmission = true;
                var commandLists = stackalloc ID3D12CommandList*[1];
                commandLists[0] = (ID3D12CommandList*)commandList;
                _commandQueue->ExecuteCommandLists(1, commandLists);
                var signalResult = _commandQueue->Signal(_fence, fenceValue);

                if (signalResult.FAILED)
                {
                    throw new GraphicsDeviceLostException(signalResult.Value);
                }

                graphFrameSlot.FenceValue = fenceValue;
                _hasUncertainSubmission = false;
                return checked((long)fenceValue);
            }

            internal long SubmitReadbackWithoutWait(Action<long>? onSubmitted = null)
            {
                EnsureOwnerThread();

                if (!_isRecording || (_activeReadbackAllocator is null))
                {
                    throw new InvalidOperationException("A D3D12 buffer-readback command list is not recording.");
                }

                var commandList = _activeCommandList;
                ThrowIfFailed(commandList->Close());
                _isRecording = false;
                _activeCommandList = null;
                _activeReadbackAllocator = null;
                _activeGraphFrameSlotIndex = -1;

                if (_nextFenceValue >= long.MaxValue)
                {
                    throw new InvalidOperationException("The D3D12 transfer fence value space is exhausted.");
                }

                var fenceValue = ++_nextFenceValue;
                _hasUncertainSubmission = true;
                var commandLists = stackalloc ID3D12CommandList*[1];
                commandLists[0] = (ID3D12CommandList*)commandList;
                _commandQueue->ExecuteCommandLists(1, commandLists);
                var signalResult = _commandQueue->Signal(_fence, fenceValue);

                if (signalResult.FAILED)
                {
                    throw new GraphicsDeviceLostException(signalResult.Value);
                }

                onSubmitted?.Invoke(checked((long)fenceValue));
                _hasUncertainSubmission = false;
                return checked((long)fenceValue);
            }

            internal void WaitForFence(long fenceValue)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fenceValue);
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                var nativeFenceValue = checked((ulong)fenceValue);
                var completedFenceValue = _fence->GetCompletedValue();

                if (completedFenceValue == ulong.MaxValue)
                {
                    var deviceRemovedReason = _device->GetDeviceRemovedReason();
                    throw new GraphicsDeviceLostException(
                        deviceRemovedReason.FAILED
                            ? deviceRemovedReason.Value
                            : DxgiErrorDeviceRemoved);
                }

                if ((completedFenceValue >= nativeFenceValue) && (_graphWaitForSingleObject is null))
                {
                    return;
                }

                var fenceEvent = Windows.CreateEventW(null, false, false, null);

                if (fenceEvent.Value is null)
                {
                    ThrowLastWin32Error("Creating the D3D12 graph-fence wait event failed.");
                }

                try
                {
                    var eventResult = _fence->SetEventOnCompletion(nativeFenceValue, fenceEvent);

                    if (eventResult.FAILED)
                    {
                        throw new GraphicsDeviceLostException(eventResult.Value);
                    }

                    var waitResult = _graphWaitForSingleObject is Func<nint, uint, uint> waitForSingleObject
                        ? waitForSingleObject(fenceEvent, TransferFenceTimeoutMilliseconds)
                        : Windows.WaitForSingleObject(fenceEvent, TransferFenceTimeoutMilliseconds);

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

                    completedFenceValue = _fence->GetCompletedValue();

                    if ((completedFenceValue == ulong.MaxValue) || (completedFenceValue < nativeFenceValue))
                    {
                        var deviceRemovedReason = _device->GetDeviceRemovedReason();
                        throw new GraphicsDeviceLostException(
                            deviceRemovedReason.FAILED
                                ? deviceRemovedReason.Value
                                : unchecked((int)0x80004005));
                    }
                }
                finally
                {
                    if (!Windows.CloseHandle(fenceEvent))
                    {
                        ThrowLastWin32Error("Closing the D3D12 graph-fence wait event failed.");
                    }
                }
            }

            private void EnsureOwnerThread()
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);

                if (Environment.CurrentManagedThreadId != _ownerThreadId)
                {
                    throw new InvalidOperationException("D3D12 transfer commands must execute on their owning thread.");
                }
            }

            private sealed unsafe class GraphFrameSlot
            {
                internal ID3D12CommandAllocator* CommandAllocator;
                internal ID3D12GraphicsCommandList* CommandList;
                internal ulong FenceValue;

                internal GraphFrameSlot(
                    ID3D12CommandAllocator* commandAllocator,
                    ID3D12GraphicsCommandList* commandList)
                {
                    CommandAllocator = commandAllocator;
                    CommandList = commandList;
                }

                internal void Dispose()
                {
                    var commandList = CommandList;
                    var commandAllocator = CommandAllocator;
                    CommandList = null;
                    CommandAllocator = null;

                    if (commandList is not null)
                    {
                        _ = commandList->Release();
                    }

                    if (commandAllocator is not null)
                    {
                        _ = commandAllocator->Release();
                    }
                }
            }
        }
    }
}
