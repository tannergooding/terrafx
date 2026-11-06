// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

/// <summary>Contains one immutable graph compiled for an exact render snapshot.</summary>
public sealed class RenderGraphPlan
{
    internal RenderGraphPlan(
        RenderGraphOptions options,
        uint deviceEpoch,
        ImmutableArray<RenderPassId> orderedPasses,
        ImmutableArray<RenderGraphPassKind> orderedPassKinds,
        ImmutableArray<RenderGraphPassNode> passes,
        ImmutableArray<GraphResourceStateTransition> stateTransitions,
        ImmutableArray<GraphResourceAccess> accesses,
        ImmutableArray<GraphBufferImportRecord> bufferImports,
        ImmutableArray<GraphTextureImportRecord> textureImports,
        ImmutableArray<RenderGraphAttachmentWrite> attachmentWrites)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfZero(deviceEpoch);

        if (orderedPasses.IsDefault
            || orderedPassKinds.IsDefault
            || passes.IsDefault
            || stateTransitions.IsDefault
            || accesses.IsDefault
            || bufferImports.IsDefault
            || textureImports.IsDefault
            || attachmentWrites.IsDefault)
        {
            throw new ArgumentException("A compiled graph plan requires initialized immutable data.");
        }

        if ((orderedPasses.Length != orderedPassKinds.Length) || (orderedPasses.Length != passes.Length))
        {
            throw new ArgumentException("A compiled graph plan must preserve pass identities, kinds, and descriptions.");
        }

        Accesses = [.. accesses];
        AttachmentWrites = [.. attachmentWrites];
        BufferImports = [.. bufferImports];
        CameraIds = options.CameraIds;
        DeviceEpoch = deviceEpoch;
        FrameDimension = options.FrameDimension;
        FrameRevision = options.FrameRevision;
        OffscreenColorTarget = options.OffscreenColorTarget;
        OrderedPassKinds = [.. orderedPassKinds];
        OrderedPasses = [.. orderedPasses];
        Passes = [.. passes];
        ProfileId = options.ProfileId;
        SnapshotId = options.SnapshotId;
        StateTransitions = [.. stateTransitions];
        SwapchainEpoch = 0;
        SurfaceEpoch = 0;
        TargetExtent = options.TargetExtent;
        TextureImports = [.. textureImports];
        TickIndex = options.TickIndex;
        WorldGuid = options.WorldGuid;
    }

    /// <summary>Gets the camera identities in committed order.</summary>
    public ImmutableArray<Guid> CameraIds { get; }

    /// <summary>Gets the graphics device generation used for compilation.</summary>
    public uint DeviceEpoch { get; }

    /// <summary>Gets the snapshot's spatial dimension.</summary>
    public RenderFrameDimension FrameDimension { get; }

    /// <summary>Gets the snapshot's frame revision.</summary>
    public long FrameRevision { get; }

    /// <summary>Gets the offscreen color target, or null for a surface target.</summary>
    public GpuTexture? OffscreenColorTarget { get; }

    /// <summary>Gets the spatial profile identity.</summary>
    public SpatialProfileId ProfileId { get; }

    /// <summary>Gets the render snapshot identity.</summary>
    public Guid SnapshotId { get; }

    /// <summary>Gets the surface epoch, or zero for an offscreen plan.</summary>
    public uint SurfaceEpoch { get; }

    /// <summary>Gets the swapchain epoch, or zero for an offscreen plan.</summary>
    public uint SwapchainEpoch { get; }

    /// <summary>Gets the positive physical target extent.</summary>
    public PixelSize TargetExtent { get; }

    /// <summary>Gets the snapshot's committed tick.</summary>
    public long TickIndex { get; }

    /// <summary>Gets the source World identity.</summary>
    public Guid WorldGuid { get; }

    internal ImmutableArray<GraphResourceAccess> Accesses { get; }

    internal ImmutableArray<RenderGraphAttachmentWrite> AttachmentWrites { get; }

    internal ImmutableArray<GraphBufferImportRecord> BufferImports { get; }

    internal ImmutableArray<RenderGraphPassKind> OrderedPassKinds { get; }

    internal ImmutableArray<RenderPassId> OrderedPasses { get; }

    internal ImmutableArray<RenderGraphPassNode> Passes { get; }

    internal ImmutableArray<GraphResourceStateTransition> StateTransitions { get; }

    internal ImmutableArray<GraphTextureImportRecord> TextureImports { get; }
}
