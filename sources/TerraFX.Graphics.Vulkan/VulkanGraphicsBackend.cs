// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using TerraFX.Graphics;

namespace TerraFX.Graphics.Vulkan;

/// <summary>Provides the explicitly selected Vulkan graphics backend.</summary>
public static class VulkanGraphicsBackend
{
    /// <summary>Creates immutable factory metadata without creating a native device.</summary>
    public static GraphicsBackendRegistration CreateRegistration()
        => new(new VulkanBackendFactory());

    private sealed class VulkanBackendFactory : IGraphicsBackendFactory
    {
        public GraphicsBackend Backend => GraphicsBackend.Vulkan;

        public IGraphicsBackendDevice Create(
            uint ownerId,
            GraphicsDeviceProfile profile,
            GraphicsPlatformKind platformKind,
            CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfZero(ownerId);
            cancellationToken.ThrowIfCancellationRequested();
            return VulkanBackendDevice.Create(profile, platformKind, cancellationToken);
        }
    }
}
