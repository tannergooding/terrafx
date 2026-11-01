// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Buffers;
using TerraFX.Graphics;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class GraphicsBufferTransferTests
{
    private static BufferDescription CreateBufferDescription()
        => new(
            byteLength: 8,
            BufferUsage.CopySource | BufferUsage.CopyDestination,
            GpuMemoryKind.DeviceLocal,
            structuredStride: null,
            debugName: "Buffer transfer test");

    private static GraphicsCapabilities CreateCapabilities()
    {
        var features = ImmutableArray.Create(
            GraphicsFeature.Direct3DFeatureLevel12,
            GraphicsFeature.ShaderModel6);
        var formats = ImmutableArray.Create(
            new GraphicsFormatCapabilities(TextureFormat.Rgba8Unorm, TextureUsage.Sampled | TextureUsage.ColorAttachment, [1]),
            new GraphicsFormatCapabilities(TextureFormat.Rgba8Srgb, TextureUsage.Sampled | TextureUsage.ColorAttachment, [1]),
            new GraphicsFormatCapabilities(TextureFormat.Rgba16Float, TextureUsage.Sampled | TextureUsage.Storage | TextureUsage.ColorAttachment, [1]),
            new GraphicsFormatCapabilities(TextureFormat.R8Unorm, TextureUsage.Sampled, [1]),
            new GraphicsFormatCapabilities(TextureFormat.R32Float, TextureUsage.Sampled | TextureUsage.Storage, [1]),
            new GraphicsFormatCapabilities(TextureFormat.R32UInt, TextureUsage.Storage | TextureUsage.ColorAttachment, [1]),
            new GraphicsFormatCapabilities(TextureFormat.D32Float, TextureUsage.DepthAttachment | TextureUsage.Sampled, [1]));
        var limits = new GraphicsLimits(
            maximumTextureDimension2D: 16384,
            maximumTextureDimension3D: 2048,
            maximumArrayLayers: 2048,
            maximumColorAttachments: 8,
            maximumSampledImagesPerStage: 128,
            maximumSamplersPerStage: 32,
            maximumSamplerAnisotropy: 1.0f,
            maximumUniformBuffersPerStage: 16,
            maximumUniformBufferRangeBytes: 65536,
            uniformBufferOffsetAlignment: 256,
            storageBufferOffsetAlignment: 16,
            bufferCopyOffsetAlignment: 16,
            textureRowPitchAlignment: 256,
            texturePlacementAlignment: 512,
            maximumComputeWorkGroupSizeX: 1024,
            maximumComputeWorkGroupSizeY: 1024,
            maximumComputeWorkGroupSizeZ: 64,
            maximumComputeWorkGroupInvocations: 1024,
            maximumVertexInputAttributes: 32,
            maximumVertexInputBindings: 32,
            maximumBufferByteLength: 1L << 40);

        return new GraphicsCapabilities(
            GraphicsBackend.D3D12,
            GraphicsDeviceProfile.OffscreenNative,
            new GraphicsAdapterId(GraphicsBackend.D3D12, [0, 1, 2, 3, 4, 5, 6, 7]),
            vendorId: 1,
            deviceId: 1,
            adapterName: "Buffer transfer test adapter",
            driverName: "Buffer transfer test driver",
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

    private static async Task<GraphicsDevice> CreateGraphicsDeviceAsync(FakeBackendDevice backendDevice)
        => await GraphicsDevice.CreateAsync(
            ownerId: 31,
            new GraphicsBackendRegistration(new FakeBackendFactory(backendDevice)),
            GraphicsDeviceProfile.OffscreenNative,
            GraphicsPlatformKind.None,
            CancellationToken.None).ConfigureAwait(false);

    [Test]
    [SuppressMessage(
        "Usage",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "This test disposes caller owners while an admitted transfer is blocked to verify its independent credits.")]
    public async Task AdmittedUploadRetainsSourceAndDestinationUntilBackendCompletion()
    {
        using var continueUpload = new ManualResetEventSlim(initialState: false);
        using var uploadEntered = new ManualResetEventSlim(initialState: false);
        using var cancellationSource = new CancellationTokenSource();
        var backendDevice = new FakeBackendDevice(uploadEntered, continueUpload);
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var destination = await graphicsDevice.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);
        var sourceOwner = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(4, 1));
        sourceOwner.CopyFrom([9, 8, 7, 6], 0);
        var sourceLease = sourceOwner.AcquireRead(0, 4);
        var sourceLeaseDisposed = false;
        var sourceOwnerDisposed = false;
        var uploadTask = graphicsDevice.UploadBufferAsync(
            destination,
            destinationOffset: 0,
            sourceLease,
            sourceOffset: 0,
            byteLength: 4,
            cancellationSource.Token);

        try
        {
            Assert.That(uploadEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            sourceLease.Dispose();
            sourceLeaseDisposed = true;
            sourceOwner.Dispose();
            sourceOwnerDisposed = true;
            destination.Dispose();
            await cancellationSource.CancelAsync().ConfigureAwait(false);

            Assert.That(uploadTask.IsCompleted, Is.False);
            Assert.That(backendDevice.BufferDisposalCount, Is.Zero);

            continueUpload.Set();
            _ = Assert.ThrowsAsync<TaskCanceledException>(
                async () => await uploadTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(backendDevice.BufferDisposalCount, Is.EqualTo(1));
            Assert.That(backendDevice.LastBufferBytes, Is.EqualTo(new byte[] { 9, 8, 7, 6, 0, 0, 0, 0 }));
        }
        finally
        {
            continueUpload.Set();

            if (!sourceLeaseDisposed)
            {
                sourceLease.Dispose();
            }

            if (!sourceOwnerDisposed)
            {
                sourceOwner.Dispose();
            }

            destination.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    [SuppressMessage(
        "Usage",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "This test disposes buffers while their already-admitted transfers are queued behind a blocked owner operation.")]
    public async Task QueuedTransfersRetainBuffersWhenRetirementRunsFirst()
    {
        using var continueUpload = new ManualResetEventSlim(initialState: false);
        using var uploadEntered = new ManualResetEventSlim(initialState: false);
        var backendDevice = new FakeBackendDevice(uploadEntered, continueUpload);
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var blocker = await graphicsDevice.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);
        var uploadDestination = await graphicsDevice.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);
        var readbackSource = await graphicsDevice.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);

        try
        {
            var blockerTask = graphicsDevice.UploadBufferAsync(blocker, destinationOffset: 0, [1]);
            Assert.That(uploadEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);

            var queuedUploadTask = graphicsDevice.UploadBufferAsync(uploadDestination, destinationOffset: 0, [2]);
            var queuedReadbackTask = graphicsDevice.ReadbackBufferAsync(readbackSource, sourceOffset: 0, byteLength: 1);
            uploadDestination.Dispose();
            readbackSource.Dispose();

            Assert.That(queuedUploadTask.IsCompleted, Is.False);
            Assert.That(queuedReadbackTask.IsCompleted, Is.False);

            continueUpload.Set();
            _ = await blockerTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(
                (await queuedUploadTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false)).Status,
                Is.EqualTo(UploadStatus.Ready));
            using var readback = await queuedReadbackTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(readback.ByteLength, Is.EqualTo((nint)1));

            blocker.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(backendDevice.BufferDisposalCount, Is.EqualTo(3));
        }
        finally
        {
            continueUpload.Set();
            blocker.Dispose();
            uploadDestination.Dispose();
            readbackSource.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task SpanUploadCopiesCallerBytesAndReadbackReturnsTheRequestedRange()
    {
        var backendDevice = new FakeBackendDevice();
        var graphicsDevice = await CreateGraphicsDeviceAsync(backendDevice).ConfigureAwait(false);
        var buffer = await graphicsDevice.CreateBufferAsync(CreateBufferDescription()).ConfigureAwait(false);

        try
        {
            var source = new byte[] { 1, 2, 3, 4 };
            var uploadTask = graphicsDevice.UploadBufferAsync(buffer, destinationOffset: 2, source);
            source[0] = byte.MaxValue;

            var receipt = await uploadTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(receipt.Status, Is.EqualTo(UploadStatus.Ready));
            Assert.That(receipt.DeviceEpoch, Is.EqualTo(graphicsDevice.DeviceEpoch));
            Assert.That(receipt.CompletedFence, Is.EqualTo(1));

            using var readback = await graphicsDevice.ReadbackBufferAsync(buffer, sourceOffset: 1, byteLength: 6)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            using var readbackLease = readback.AcquireReadLease(0, 6);
            var bytes = new byte[6];
            var copyAction = new ReadbackCopyAction(bytes);
            readbackLease.UseWindow(0, bytes.Length, ref copyAction);

            Assert.That(bytes, Is.EqualTo(new byte[] { 0, 1, 2, 3, 4, 0 }));
            ArgumentOutOfRangeException? readbackException = null;

            try
            {
                _ = await graphicsDevice.ReadbackBufferAsync(buffer, sourceOffset: 8, byteLength: 1).ConfigureAwait(false);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                readbackException = exception;
            }

            Assert.That(readbackException, Is.Not.Null);
        }
        finally
        {
            buffer.Dispose();
            await graphicsDevice.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    private sealed class FakeBackendBuffer : IGraphicsBackendBuffer
    {
        private readonly FakeBackendDevice _device;
        private int _isDisposed;

        internal FakeBackendBuffer(FakeBackendDevice device, BufferDescription description)
        {
            _device = device;
            Bytes = new byte[checked((int)description.ByteLength)];
        }

        internal byte[] Bytes { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                _device.BufferDisposed(this);
            }
        }
    }

    private sealed class FakeBackendDevice : IGraphicsBackendDevice
    {
        private readonly ManualResetEventSlim? _continueUpload;
        private readonly ManualResetEventSlim? _uploadEntered;
        private readonly GraphicsCapabilities _capabilities = CreateCapabilities();
        private readonly List<byte[]> _buffers = [];
        private int _bufferDisposalCount;
        private long _completedFence;
        private byte[]? _lastBufferBytes;

        internal FakeBackendDevice()
        {
        }

        internal FakeBackendDevice(ManualResetEventSlim uploadEntered, ManualResetEventSlim continueUpload)
        {
            _uploadEntered = uploadEntered;
            _continueUpload = continueUpload;
        }

        public GraphicsCapabilities Capabilities => _capabilities;

        internal int BufferDisposalCount => Volatile.Read(ref _bufferDisposalCount);

        internal byte[]? LastBufferBytes => _lastBufferBytes;

        private static FakeBackendBuffer GetBuffer(IGraphicsBackendBuffer buffer)
            => buffer as FakeBackendBuffer
                ?? throw new ArgumentException("The buffer belongs to a different fake backend.", nameof(buffer));

        public IGraphicsBackendBuffer CreateBuffer(BufferDescription description)
        {
            var buffer = new FakeBackendBuffer(this, description);
            _buffers.Add(buffer.Bytes);
            _lastBufferBytes = buffer.Bytes;
            return buffer;
        }

        public IGraphicsBackendPipeline CreatePipeline(
            PipelineDescription description,
            ReadOnlySpan<BufferReadLease<byte>> bytecodeLeases)
            => throw new NotSupportedException();

        public IGraphicsBackendTexture CreateTexture(TextureDescription description)
            => throw new NotSupportedException();

        public void Dispose()
        {
        }

        public GraphicsResourceAllocationInfo GetBufferAllocationInfo(BufferDescription description)
            => new(description.ByteLength, alignment: 1, heapIndex: 0, budgetBytes: 3L * 1024 * 1024 * 1024);

        public GraphicsResourceAllocationInfo GetTextureAllocationInfo(TextureDescription description)
            => throw new NotSupportedException();

        public OwnedBuffer<byte> ReadbackBuffer(IGraphicsBackendBuffer source, long sourceOffset, nint byteLength)
        {
            var buffer = GetBuffer(source);
            var storage = UnmanagedBuffer<byte>.Allocate(byteLength, new BufferAllocationOptions(byteLength, 1));
            storage.CopyFrom(buffer.Bytes.AsSpan(checked((int)sourceOffset), checked((int)byteLength)), 0);
            return storage.TransferToOwnedBuffer();
        }

        public long UploadBuffer(
            IGraphicsBackendBuffer destination,
            long destinationOffset,
            BufferReadLease<byte> source,
            nint sourceOffset,
            nint byteLength)
        {
            WaitForUpload();
            var destinationBuffer = GetBuffer(destination);
            nint copiedBytes = 0;

            while (copiedBytes < byteLength)
            {
                var remainingBytes = byteLength - copiedBytes;
                var copyLength = remainingBytes > int.MaxValue
                    ? int.MaxValue
                    : checked((int)remainingBytes);
                var action = new BufferUploadCopyAction(
                    destinationBuffer.Bytes,
                    checked((int)(destinationOffset + copiedBytes)));
                source.UseWindow(checked(sourceOffset + copiedBytes), copyLength, ref action);
                copiedBytes = checked(copiedBytes + copyLength);
            }

            return Interlocked.Increment(ref _completedFence);
        }

        public long UploadBuffer(IGraphicsBackendBuffer destination, long destinationOffset, ReadOnlySpan<byte> source)
        {
            WaitForUpload();
            source.CopyTo(GetBuffer(destination).Bytes.AsSpan(checked((int)destinationOffset), source.Length));
            return Interlocked.Increment(ref _completedFence);
        }

        internal void BufferDisposed(FakeBackendBuffer buffer)
        {
            _ = Interlocked.Increment(ref _bufferDisposalCount);

            if (!_buffers.Contains(buffer.Bytes))
            {
                throw new InvalidOperationException("The test backend disposed an unknown buffer.");
            }
        }

        private void WaitForUpload()
        {
            if (_uploadEntered is null)
            {
                return;
            }

            _uploadEntered.Set();

            if (!_continueUpload!.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("The buffer transfer test did not release its upload gate.");
            }
        }

        private readonly struct BufferUploadCopyAction : IAction<ReadOnlySpan<byte>>
        {
            private readonly byte[] _destination;
            private readonly int _destinationOffset;

            internal BufferUploadCopyAction(byte[] destination, int destinationOffset)
            {
                _destination = destination;
                _destinationOffset = destinationOffset;
            }

            public readonly void Invoke(scoped ReadOnlySpan<byte> argument)
                => argument.CopyTo(_destination.AsSpan(_destinationOffset, argument.Length));
        }
    }

    private sealed class FakeBackendFactory : IGraphicsBackendFactory
    {
        private readonly FakeBackendDevice _device;

        internal FakeBackendFactory(FakeBackendDevice device)
        {
            _device = device;
        }

        public GraphicsBackend Backend => GraphicsBackend.D3D12;

        public IGraphicsBackendDevice Create(
            uint ownerId,
            GraphicsDeviceProfile profile,
            GraphicsPlatformKind platformKind,
            CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfZero(ownerId);
            cancellationToken.ThrowIfCancellationRequested();

            if ((profile != GraphicsDeviceProfile.OffscreenNative) || (platformKind != GraphicsPlatformKind.None))
            {
                throw new ArgumentException("The test backend supports only OffscreenNative without a platform.");
            }

            return _device;
        }
    }

    private readonly struct ReadbackCopyAction : IAction<ReadOnlySpan<byte>>
    {
        private readonly byte[] _destination;

        internal ReadbackCopyAction(byte[] destination)
        {
            _destination = destination;
        }

        public readonly void Invoke(scoped ReadOnlySpan<byte> argument)
            => argument.CopyTo(_destination);
    }
}
