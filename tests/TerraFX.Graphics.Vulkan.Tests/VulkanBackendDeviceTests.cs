// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using NUnit.Framework;
using TerraFX.Graphics;
using TerraFX.Graphics.Vulkan;

namespace TerraFX.Graphics.Vulkan.Tests;

[TestFixture]
internal sealed class VulkanBackendDeviceTests
{
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
}
