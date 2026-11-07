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

    internal static GraphicsBackendRegistration CreateRegistrationForTesting(
        Action<long>? graphFenceWaitObserver,
        Func<long, long>? completedGraphFenceValueOverride,
        Action<GraphicsGraphSubmission>? graphSubmissionObserver)
        => new(new VulkanBackendFactory(
            graphFenceWaitObserver,
            completedGraphFenceValueOverride,
            graphSubmissionObserver));

    internal sealed class VulkanBackendFactory : IGraphicsBackendFactory
    {
        private readonly Func<long, long>? _completedGraphFenceValueOverride;
        private readonly Action<long>? _graphFenceWaitObserver;
        private readonly Action<GraphicsGraphSubmission>? _graphSubmissionObserver;

        internal VulkanBackendFactory(
            Action<long>? graphFenceWaitObserver = null,
            Func<long, long>? completedGraphFenceValueOverride = null,
            Action<GraphicsGraphSubmission>? graphSubmissionObserver = null)
        {
            _graphFenceWaitObserver = graphFenceWaitObserver;
            _completedGraphFenceValueOverride = completedGraphFenceValueOverride;
            _graphSubmissionObserver = graphSubmissionObserver;
        }

        public GraphicsBackend Backend => GraphicsBackend.Vulkan;

        public IGraphicsBackendDevice Create(
            uint ownerId,
            GraphicsDeviceProfile profile,
            GraphicsPlatformKind platformKind,
            int frameSlotCount,
            CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfZero(ownerId);
            cancellationToken.ThrowIfCancellationRequested();
            return VulkanBackendDevice.Create(
                profile,
                platformKind,
                transferWaitResultOverride: null,
                afterNativeObjectAcquired: null,
                startupNativeResultOverride: null,
                cancellationToken: cancellationToken,
                graphFrameSlotCount: frameSlotCount,
                graphFenceWaitObserver: _graphFenceWaitObserver,
                completedGraphFenceValueOverride: _completedGraphFenceValueOverride,
                graphSubmissionObserver: _graphSubmissionObserver);
        }
    }
}
