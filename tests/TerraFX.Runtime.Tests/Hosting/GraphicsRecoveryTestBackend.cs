// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using TerraFX;
using TerraFX.Buffers;
using TerraFX.Graphics;
using TerraFX.Numerics;

namespace TerraFX.Runtime.Tests.Hosting;

internal static class GraphicsRecoveryTestBackend
{
    internal static GraphicsCapabilities CreateCapabilities(
        GraphicsDeviceProfile profile = GraphicsDeviceProfile.OffscreenNative)
    {
        var features = ImmutableArray.Create(
            GraphicsFeature.Direct3DFeatureLevel12,
            GraphicsFeature.ShaderModel6);
        var limits = new GraphicsLimits(
            maximumTextureDimension2D: 16384,
            maximumTextureDimension3D: 2048,
            maximumArrayLayers: 2048,
            maximumColorAttachments: 8,
            maximumSampledImagesPerStage: 128,
            maximumSampledImagesPerPipeline: 64,
            maximumSamplersPerStage: 32,
            maximumSamplersPerPipeline: 32,
            maximumSamplerAnisotropy: 1.0f,
            maximumUniformBuffersPerStage: 16,
            maximumUniformBuffersPerPipeline: 16,
            maximumStorageBuffersPerStage: 32,
            maximumStorageImagesPerStage: 32,
            maximumStorageBuffersPerPipeline: 32,
            maximumStorageImagesPerPipeline: 32,
            maximumStorageResourcesPerPipeline: 32,
            maximumResourcesPerStage: 144,
            maximumUniformBufferRangeBytes: 65536,
            uniformBufferOffsetAlignment: 256,
            storageBufferOffsetAlignment: 16,
            bufferCopyOffsetAlignment: 16,
            textureRowPitchAlignment: 256,
            texturePlacementAlignment: 512,
            maximumComputeWorkGroupSizeX: 1024,
            maximumComputeWorkGroupSizeY: 1024,
            maximumComputeWorkGroupSizeZ: 64,
            maximumComputeWorkGroupCountX: 65535,
            maximumComputeWorkGroupCountY: 65535,
            maximumComputeWorkGroupCountZ: 65535,
            maximumComputeWorkGroupInvocations: 1024,
            maximumVertexInputAttributes: 32,
            maximumVertexInputBindings: 32,
            maximumBufferByteLength: 1L << 40);
        var formats = ImmutableArray.Create(
            new GraphicsFormatCapabilities(TextureFormat.Rgba8Unorm, TextureUsage.Sampled | TextureUsage.ColorAttachment, [1]),
            new GraphicsFormatCapabilities(TextureFormat.Rgba8Srgb, TextureUsage.Sampled | TextureUsage.ColorAttachment, [1]),
            new GraphicsFormatCapabilities(TextureFormat.Rgba16Float, TextureUsage.Sampled | TextureUsage.ColorAttachment | TextureUsage.Storage, [1]),
            new GraphicsFormatCapabilities(TextureFormat.R8Unorm, TextureUsage.Sampled, [1]),
            new GraphicsFormatCapabilities(TextureFormat.R32Float, TextureUsage.Sampled | TextureUsage.Storage, [1]),
            new GraphicsFormatCapabilities(TextureFormat.R32UInt, TextureUsage.Storage | TextureUsage.ColorAttachment, [1]),
            new GraphicsFormatCapabilities(TextureFormat.D32Float, TextureUsage.DepthAttachment | TextureUsage.Sampled, [1]));

        return new GraphicsCapabilities(
            GraphicsBackend.D3D12,
            profile,
            new GraphicsAdapterId(GraphicsBackend.D3D12, [0, 1, 2, 3, 4, 5, 6, 7]),
            vendorId: 1,
            deviceId: 1,
            adapterName: "Graphics recovery test adapter",
            driverName: "Graphics recovery test driver",
            driverVersion: "1",
            new GraphicsApiVersion(12, 0, 0, 0),
            features,
            features,
            limits,
            formats,
            [new GraphicsMemoryHeapCapabilities(
                0,
                GraphicsMemoryHeapProperties.DeviceLocal,
                4L * 1024 * 1024 * 1024,
                3L * 1024 * 1024 * 1024)]);
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The factory takes ownership of these test devices and transfers each to GraphicsDeviceStartup.")]
    internal static GraphicsBackendRegistration CreateRegistration(
        GraphicsDeviceProfile profile,
        GraphicsPlatformKind platformKind,
        bool simulateFailedReplacement = false)
    {
        var capabilities = CreateCapabilities(profile);
        var devices = new IGraphicsBackendDevice[] {
            new RecoveryBackendDevice(capabilities, loseOnBufferCreation: true),
        };

        if (simulateFailedReplacement)
        {
            var incompatibleProfile = (profile is GraphicsDeviceProfile.OffscreenNative)
                ? GraphicsDeviceProfile.Interactive
                : GraphicsDeviceProfile.OffscreenNative;
            devices = [
                .. devices,
                new RecoveryBackendDevice(CreateCapabilities(incompatibleProfile), loseOnBufferCreation: false),
            ];
        }

        devices = [
            .. devices,
            new RecoveryBackendDevice(capabilities, loseOnBufferCreation: false),
        ];
        return new GraphicsBackendRegistration(new RecoveryBackendFactory(profile, platformKind, devices));
    }

    internal static GraphicsBackendRegistration CreateRegistration()
        => CreateRegistration(
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None,
            simulateFailedReplacement: true);

    private sealed class RecoveryBackendBuffer : IGraphicsBackendBuffer
    {
        public void Dispose()
        {
        }
    }

    private sealed class RecoveryBackendDevice : IGraphicsBackendDevice
    {
        private int _bufferCreationCount;
        private readonly GraphicsCapabilities _capabilities;
        private readonly bool _loseOnBufferCreation;

        internal RecoveryBackendDevice(GraphicsCapabilities capabilities, bool loseOnBufferCreation)
        {
            _capabilities = capabilities;
            _loseOnBufferCreation = loseOnBufferCreation;
        }

        public GraphicsCapabilities Capabilities => _capabilities;

        public IGraphicsBackendBuffer CreateBuffer(BufferDescription description)
        {
            if (_loseOnBufferCreation && (Interlocked.Increment(ref _bufferCreationCount) == 2))
            {
                throw new GraphicsDeviceLostException(unchecked((int)0x887A0005));
            }

            return new RecoveryBackendBuffer();
        }

        public IGraphicsBackendPipeline CreatePipeline(
            PipelineDescription description,
            ReadOnlySpan<BufferReadLease<byte>> bytecodeLeases)
            => throw new NotSupportedException();

        public IGraphicsBackendSampler CreateSampler(SamplerDescription description, float effectiveMaximumAnisotropy)
            => throw new NotSupportedException();

        public IGraphicsBackendSurface CreateSurface(GraphicsNativeSurfaceBinding binding, PixelSize clientPixelSize)
            => new RecoveryBackendSurface(binding.SurfaceEpoch, clientPixelSize);

        public IGraphicsBackendTexture CreateTexture(TextureDescription description)
            => throw new NotSupportedException();

        public void Dispose()
        {
        }

        public GraphicsResourceAllocationInfo GetBufferAllocationInfo(BufferDescription description)
            => new(description.ByteLength, alignment: 256, heapIndex: 0, budgetBytes: 3L * 1024 * 1024 * 1024);

        public GraphicsResourceAllocationInfo GetTextureAllocationInfo(TextureDescription description)
            => throw new NotSupportedException();

        public OwnedBuffer<byte> ReadbackBuffer(
            IGraphicsBackendBuffer source,
            GraphResourceState sourceState,
            long sourceOffset,
            nint byteLength)
            => throw new NotSupportedException();

        public TextureReadback ReadbackTexture(
            IGraphicsBackendTexture source,
            GraphResourceState sourceState,
            TextureSubresource subresource)
            => throw new NotSupportedException();

        public long SubmitGraph(GraphicsGraphSubmission submission)
            => throw new NotSupportedException();

        public long UploadBuffer(
            IGraphicsBackendBuffer destination,
            GraphResourceState destinationState,
            long destinationOffset,
            BufferReadLease<byte> source,
            nint sourceOffset,
            nint byteLength)
            => throw new NotSupportedException();

        public long UploadBuffer(
            IGraphicsBackendBuffer destination,
            GraphResourceState destinationState,
            long destinationOffset,
            ReadOnlySpan<byte> source)
            => throw new NotSupportedException();

        public long UploadTexture(
            IGraphicsBackendTexture destination,
            GraphResourceState destinationState,
            TextureSubresource subresource,
            BufferReadLease<byte> source,
            nint sourceOffset,
            TextureDataLayout sourceLayout)
            => throw new NotSupportedException();
    }

    private sealed class RecoveryBackendFactory : IGraphicsBackendFactory
    {
        private readonly Queue<IGraphicsBackendDevice> _devices;
        private readonly GraphicsPlatformKind _platformKind;
        private readonly GraphicsDeviceProfile _profile;

        internal RecoveryBackendFactory(
            GraphicsDeviceProfile profile,
            GraphicsPlatformKind platformKind,
            params IGraphicsBackendDevice[] devices)
        {
            _devices = new Queue<IGraphicsBackendDevice>(devices);
            _profile = profile;
            _platformKind = platformKind;
        }

        public GraphicsBackend Backend => GraphicsBackend.D3D12;

        public IGraphicsBackendDevice Create(
            uint ownerId,
            GraphicsDeviceProfile profile,
            GraphicsPlatformKind platformKind,
            int frameSlotCount,
            CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfZero(ownerId);
            cancellationToken.ThrowIfCancellationRequested();

            if ((profile != _profile) || (platformKind != _platformKind))
            {
                throw new ArgumentException("The recovery test backend received an unexpected device profile or platform.");
            }

            lock (_devices)
            {
                return _devices.Dequeue();
            }
        }
    }

    private sealed class RecoveryBackendSurface : IGraphicsBackendSurface
    {
        private SurfaceCapabilities _capabilities;
        private PixelSize _clientPixelSize;

        internal RecoveryBackendSurface(uint surfaceEpoch, PixelSize clientPixelSize)
        {
            _clientPixelSize = clientPixelSize;
            _capabilities = CreateSurfaceCapabilities(
                surfaceEpoch,
                ((clientPixelSize.Width == 0) || (clientPixelSize.Height == 0)) ? 0u : 1u);
        }

        public SurfaceCapabilities Capabilities => _capabilities;

        public PixelSize ClientPixelSize => _clientPixelSize;

        public TextureFormat ColorFormat => TextureFormat.Rgba8Srgb;

        private static SurfaceCapabilities CreateSurfaceCapabilities(
            uint surfaceEpoch,
            uint swapchainEpoch)
            => new(
                [new SurfaceFormat(TextureFormat.Rgba8Srgb, SurfaceColorSpace.SrgbNonlinear)],
                [GraphicsPresentMode.Fifo],
                new PixelSize(1, 1),
                new PixelSize(16384, 16384),
                minimumImageCount: 2,
                maximumImageCount: 3,
                supportsTearing: false,
                surfaceEpoch: surfaceEpoch,
                swapchainEpoch: swapchainEpoch);

        public void Dispose()
        {
        }

        public SurfaceCapabilities Resize(PixelSize clientPixelSize)
        {
            _clientPixelSize = clientPixelSize;
            var previousSwapchainEpoch = _capabilities.SwapchainEpoch;
            var isSuspended = (clientPixelSize.Width == 0) || (clientPixelSize.Height == 0);
            var swapchainEpoch = isSuspended
                ? 0u
                : (previousSwapchainEpoch == 0 ? 1u : previousSwapchainEpoch + 1);
            _capabilities = CreateSurfaceCapabilities(
                _capabilities.SurfaceEpoch,
                swapchainEpoch);
            return _capabilities;
        }

        public bool TryGetCompletedPresentation(
            long presentationSequence,
            uint deviceEpoch,
            out PresentationReceipt? receipt)
        {
            receipt = null;
            return false;
        }

        public PresentationReceipt WaitForPresentation(
            long presentationSequence,
            uint deviceEpoch,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ImmutableArray<PresentationReceipt> WaitForPresentations(uint deviceEpoch)
            => [];
    }
}
