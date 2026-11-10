// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using TerraFX.Graphics;
using TerraFX.Interop.Vulkan;

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
        Action<GraphicsGraphSubmission>? graphSubmissionObserver,
        Action? surfaceImageAcquiredObserver = null,
        Func<VkResult?>? surfaceAcquireResultOverride = null,
        Func<VkResult, VkResult>? surfacePresentResultOverride = null,
        Func<ulong, VkResult, VkResult>? surfaceFenceWaitResultOverride = null,
        ulong surfaceFenceWaitTimeoutNanoseconds = 5_000_000_000,
        Func<long, VkResult, VkResult>? graphFenceWaitResultOverride = null)
        => new(new VulkanBackendFactory(
            graphFenceWaitObserver,
            completedGraphFenceValueOverride,
            graphSubmissionObserver,
            surfaceImageAcquiredObserver,
            surfaceAcquireResultOverride,
            surfacePresentResultOverride,
            surfaceFenceWaitResultOverride,
            surfaceFenceWaitTimeoutNanoseconds,
            graphFenceWaitResultOverride));

    internal sealed class VulkanBackendFactory : IGraphicsBackendFactory
    {
        private readonly Func<long, long>? _completedGraphFenceValueOverride;
        private readonly Action<long>? _graphFenceWaitObserver;
        private readonly Func<long, VkResult, VkResult>? _graphFenceWaitResultOverride;
        private readonly Action<GraphicsGraphSubmission>? _graphSubmissionObserver;
        private readonly Func<VkResult?>? _surfaceAcquireResultOverride;
        private readonly Func<ulong, VkResult, VkResult>? _surfaceFenceWaitResultOverride;
        private readonly ulong _surfaceFenceWaitTimeoutNanoseconds;
        private readonly Action? _surfaceImageAcquiredObserver;
        private readonly Func<VkResult, VkResult>? _surfacePresentResultOverride;

        internal VulkanBackendFactory(
            Action<long>? graphFenceWaitObserver = null,
            Func<long, long>? completedGraphFenceValueOverride = null,
            Action<GraphicsGraphSubmission>? graphSubmissionObserver = null,
            Action? surfaceImageAcquiredObserver = null,
            Func<VkResult?>? surfaceAcquireResultOverride = null,
            Func<VkResult, VkResult>? surfacePresentResultOverride = null,
            Func<ulong, VkResult, VkResult>? surfaceFenceWaitResultOverride = null,
            ulong surfaceFenceWaitTimeoutNanoseconds = 5_000_000_000,
            Func<long, VkResult, VkResult>? graphFenceWaitResultOverride = null)
        {
            _graphFenceWaitObserver = graphFenceWaitObserver;
            _completedGraphFenceValueOverride = completedGraphFenceValueOverride;
            _graphSubmissionObserver = graphSubmissionObserver;
            _surfaceImageAcquiredObserver = surfaceImageAcquiredObserver;
            _surfaceAcquireResultOverride = surfaceAcquireResultOverride;
            _surfacePresentResultOverride = surfacePresentResultOverride;
            _surfaceFenceWaitResultOverride = surfaceFenceWaitResultOverride;
            _surfaceFenceWaitTimeoutNanoseconds = surfaceFenceWaitTimeoutNanoseconds;
            _graphFenceWaitResultOverride = graphFenceWaitResultOverride;
        }

        public GraphicsBackend Backend => GraphicsBackend.Vulkan;

        public IGraphicsBackendDevice Create(
            uint ownerId,
            GraphicsDeviceProfile profile,
            GraphicsPlatformKind platformKind,
            int frameSlotCount,
            CancellationToken cancellationToken)
            => Create(
                ownerId,
                profile,
                platformKind,
                frameSlotCount,
                presentationProbeBinding: null,
                cancellationToken);

        public IGraphicsBackendDevice Create(
            uint ownerId,
            GraphicsDeviceProfile profile,
            GraphicsPlatformKind platformKind,
            int frameSlotCount,
            GraphicsNativeSurfaceBinding? presentationProbeBinding,
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
                graphSubmissionObserver: _graphSubmissionObserver,
                presentationProbeBinding: presentationProbeBinding,
                surfaceAcquireResultOverride: _surfaceAcquireResultOverride,
                surfaceImageAcquiredObserver: _surfaceImageAcquiredObserver,
                surfacePresentResultOverride: _surfacePresentResultOverride,
                surfaceFenceWaitResultOverride: _surfaceFenceWaitResultOverride,
                surfaceFenceWaitTimeoutNanoseconds: _surfaceFenceWaitTimeoutNanoseconds,
                graphFenceWaitResultOverride: _graphFenceWaitResultOverride);
        }
    }
}
