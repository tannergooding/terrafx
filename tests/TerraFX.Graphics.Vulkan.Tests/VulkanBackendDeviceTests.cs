// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using NUnit.Framework;
using TerraFX.Buffers;
using TerraFX.Graphics;
using TerraFX.Graphics.Vulkan;
using TerraFX.Interop.Vulkan;

namespace TerraFX.Graphics.Vulkan.Tests;

[TestFixture]
internal sealed class VulkanBackendDeviceTests
{
    private static void AssertReadback(OwnedBuffer<byte> readback, byte[] expected)
    {
        using var lease = readback.AcquireReadLease(0, expected.Length);
        var actual = new byte[expected.Length];
        var copyAction = new ReadbackCopyAction(actual);
        lease.UseWindow(0, expected.Length, ref copyAction);
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void OffscreenNativeCreationReportsAndReleasesARealDevice()
    {
        try
        {
            using var device = VulkanBackendDevice.Create(
                GraphicsDeviceProfile.OffscreenNative,
                CancellationToken.None);

            Assert.That(device.Capabilities.Backend, Is.EqualTo(GraphicsBackend.Vulkan));
            Assert.That(device.Capabilities.Profile, Is.EqualTo(GraphicsDeviceProfile.OffscreenNative));
            Assert.That(device.Capabilities.AdapterId.NativeIdentifier.Length, Is.EqualTo(16));
            Assert.That(device.Capabilities.AdapterName, Is.Not.Empty);
            Assert.That(device.Capabilities.DriverName, Is.Not.Empty);
            Assert.That(device.Capabilities.ApiVersion.Major, Is.GreaterThanOrEqualTo(1));
            Assert.That(
                device.Capabilities.ApiVersion.Major != 1 || device.Capabilities.ApiVersion.Minor >= 3,
                Is.True);
            Assert.That(
                device.Capabilities.ImplementedFeatures,
                Does.Contain(GraphicsFeature.TimelineSemaphore)
                    .And.Contain(GraphicsFeature.Synchronization2)
                    .And.Contain(GraphicsFeature.DynamicRendering));
            TestContext.WriteLine(
                $"Adapter={device.Capabilities.AdapterName}; Driver={device.Capabilities.DriverName} "
                + $"{device.Capabilities.DriverVersion}; API={device.Capabilities.ApiVersion.Major}.{device.Capabilities.ApiVersion.Minor}; "
                + $"UUID={Convert.ToHexString(device.Capabilities.AdapterId.NativeIdentifier.AsSpan())}");
        }
        catch (DllNotFoundException exception)
        {
            Assert.Ignore($"No Vulkan loader is installed: {exception.Message}");
            return;
        }
        catch (EntryPointNotFoundException exception)
        {
            Assert.Ignore($"The installed Vulkan loader lacks a required entry point: {exception.Message}");
            return;
        }
        catch (NotSupportedException exception)
        {
            Assert.Ignore($"No available Vulkan device satisfies OffscreenNative: {exception.Message}");
            return;
        }
    }

    [Test]
    public void OffscreenNativeDeviceAllocatesEachBufferMemoryKindAndRetainsLiveOwners()
    {
        try
        {
            using var device = VulkanBackendDevice.Create(
                GraphicsDeviceProfile.OffscreenNative,
                CancellationToken.None);
            using (var deviceLocalBuffer = device.CreateBuffer(
                new BufferDescription(
                    4096,
                    BufferUsage.CopySource | BufferUsage.CopyDestination,
                    GpuMemoryKind.DeviceLocal,
                    structuredStride: null,
                    debugName: "device-local")))
            using (var uploadBuffer = device.CreateBuffer(
                new BufferDescription(
                    4096,
                    BufferUsage.CopySource,
                    GpuMemoryKind.Upload,
                    structuredStride: null,
                    debugName: "upload")))
            using (var readbackBuffer = device.CreateBuffer(
                new BufferDescription(
                    4096,
                    BufferUsage.CopyDestination,
                    GpuMemoryKind.Readback,
                    structuredStride: null,
                    debugName: "readback")))
            {
                Assert.That(deviceLocalBuffer.Description.ByteLength, Is.EqualTo(4096));
                Assert.That(
                    (uploadBuffer.MemoryProperties & VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT) != 0,
                    Is.True);
                Assert.That(
                    (readbackBuffer.MemoryProperties & VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT) != 0,
                    Is.True);
                Assert.That(
                    device.Dispose,
                    Throws.InvalidOperationException.With.Message.Contains("live buffer owners"));
            }

            device.Dispose();
        }
        catch (DllNotFoundException exception)
        {
            Assert.Ignore($"No Vulkan loader is installed: {exception.Message}");
        }
        catch (EntryPointNotFoundException exception)
        {
            Assert.Ignore($"The installed Vulkan loader lacks a required entry point: {exception.Message}");
        }
        catch (NotSupportedException exception)
        {
            Assert.Ignore($"No available Vulkan device satisfies the buffer allocation smoke test: {exception.Message}");
        }
    }

    [Test]
    public void OffscreenNativeDeviceUploadsAndReadsBackDeviceLocalBufferBytes()
    {
        try
        {
            using var device = VulkanBackendDevice.Create(
                GraphicsDeviceProfile.OffscreenNative,
                CancellationToken.None);
            using var buffer = device.CreateBuffer(
                new BufferDescription(
                    1024,
                    BufferUsage.CopySource | BufferUsage.CopyDestination,
                    GpuMemoryKind.DeviceLocal,
                    structuredStride: null,
                    debugName: "transfer"));
            var payload = new byte[257];

            for (var index = 0; index < payload.Length; index++)
            {
                payload[index] = unchecked((byte)(index * 31));
            }

            Assert.That(device.UploadBuffer(buffer, 0, payload), Is.GreaterThan(0));

            var sourceStorage = UnmanagedBuffer<byte>.Allocate(
                payload.Length,
                new BufferAllocationOptions(payload.Length, alignment: 1));
            sourceStorage.CopyFrom(payload.AsSpan(), 0);

            using var ownedSource = sourceStorage.TransferToOwnedBuffer();
            using var sourceLease = ownedSource.AcquireReadLease(0, payload.Length);
            Assert.That(
                device.UploadBuffer(buffer, 513, sourceLease, 0, payload.Length),
                Is.GreaterThan(0));

            using var firstReadback = device.ReadbackBuffer(buffer, 0, payload.Length);
            AssertReadback(firstReadback, payload);
            using var secondReadback = device.ReadbackBuffer(buffer, 513, payload.Length);
            AssertReadback(secondReadback, payload);
        }
        catch (DllNotFoundException exception)
        {
            Assert.Ignore($"No Vulkan loader is installed: {exception.Message}");
        }
        catch (EntryPointNotFoundException exception)
        {
            Assert.Ignore($"The installed Vulkan loader lacks a required entry point: {exception.Message}");
        }
        catch (NotSupportedException exception)
        {
            Assert.Ignore($"No available Vulkan device satisfies the buffer transfer smoke test: {exception.Message}");
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
