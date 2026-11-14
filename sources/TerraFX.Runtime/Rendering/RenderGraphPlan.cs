// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TerraFX;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

/// <summary>Contains one immutable graph compiled for an exact render snapshot.</summary>
public sealed partial class RenderGraphPlan
{
    internal RenderGraphPlan(
        RenderGraphOptions options,
        uint deviceEpoch,
        ImmutableArray<RenderPassId> orderedPasses,
        ImmutableArray<RenderGraphPassKind> orderedPassKinds,
        ImmutableArray<RenderPassCallback?> orderedPassCallbacks,
        ImmutableArray<RenderGraphPassNode> passes,
        ImmutableArray<GraphResourceStateTransition> stateTransitions,
        GraphTextureVersion presentationSource,
        ImmutableArray<GraphResourceAccess> accesses,
        ImmutableArray<GraphBufferImportRecord> bufferImports,
        ImmutableArray<GraphTextureImportRecord> textureImports,
        ImmutableArray<RenderGraphAttachmentWrite> attachmentWrites,
        ImmutableArray<ulong> nonAliasableTransientBufferIds,
        ImmutableArray<ulong> nonAliasableTransientTextureIds,
        RenderSpriteGraphState? builtInSpriteState)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(presentationSource);
        ArgumentOutOfRangeException.ThrowIfZero(deviceEpoch);

        if (orderedPasses.IsDefault
            || orderedPassKinds.IsDefault
            || orderedPassCallbacks.IsDefault
            || passes.IsDefault
            || stateTransitions.IsDefault
            || accesses.IsDefault
            || bufferImports.IsDefault
            || textureImports.IsDefault
            || attachmentWrites.IsDefault
            || nonAliasableTransientBufferIds.IsDefault
            || nonAliasableTransientTextureIds.IsDefault)
        {
            throw new ArgumentException("A compiled graph plan requires initialized immutable data.");
        }

        if ((orderedPasses.Length != orderedPassKinds.Length)
            || (orderedPasses.Length != orderedPassCallbacks.Length)
            || (orderedPasses.Length != passes.Length))
        {
            throw new ArgumentException("A compiled graph plan must preserve pass identities, kinds, and descriptions.");
        }

        var importedBufferResourceIds = new HashSet<ulong>();
        var importedTextureResourceIds = new HashSet<ulong>();

        foreach (var import in bufferImports)
        {
            _ = importedBufferResourceIds.Add(import.Version.ResourceId);
        }

        foreach (var import in textureImports)
        {
            _ = importedTextureResourceIds.Add(import.Version.ResourceId);
        }

        var orderedPassIndices = new Dictionary<RenderPassId, int>(orderedPasses.Length);

        for (var index = 0; index < orderedPasses.Length; index++)
        {
            orderedPassIndices.Add(orderedPasses[index], index);
        }

        var transientBuffers = new SortedDictionary<ulong, GraphBufferVersion>();
        var transientLifetimes = new Dictionary<ulong, (GraphBufferVersion Version, int FirstPassIndex, int LastPassIndex)>();
        var transientTextures = new SortedDictionary<ulong, GraphTextureVersion>();
        var transientTextureLifetimes = new Dictionary<ulong, (GraphTextureVersion Version, int FirstPassIndex, int LastPassIndex)>();

        foreach (var access in accesses)
        {
            if (!access.IsTexture
                && (access.BufferVersion is GraphBufferVersion version)
                && !importedBufferResourceIds.Contains(version.ResourceId))
            {
                var passIndex = orderedPassIndices[access.PassId];

                if (transientLifetimes.TryGetValue(version.ResourceId, out var lifetime))
                {
                    transientLifetimes[version.ResourceId] = (
                        lifetime.Version,
                        Math.Min(lifetime.FirstPassIndex, passIndex),
                        Math.Max(lifetime.LastPassIndex, passIndex));
                }
                else
                {
                    transientLifetimes.Add(version.ResourceId, (version, passIndex, passIndex));
                }
            }
            else if (access.IsTexture
                && (access.TextureVersion is GraphTextureVersion textureVersion)
                && !importedTextureResourceIds.Contains(textureVersion.ResourceId))
            {
                var passIndex = orderedPassIndices[access.PassId];

                if (transientTextureLifetimes.TryGetValue(textureVersion.ResourceId, out var lifetime))
                {
                    transientTextureLifetimes[textureVersion.ResourceId] = (
                        lifetime.Version,
                        Math.Min(lifetime.FirstPassIndex, passIndex),
                        Math.Max(lifetime.LastPassIndex, passIndex));
                }
                else
                {
                    transientTextureLifetimes.Add(textureVersion.ResourceId, (textureVersion, passIndex, passIndex));
                }

                _ = transientTextures.TryAdd(textureVersion.ResourceId, textureVersion);
            }
        }

        var orderedTransientLifetimes = new List<(GraphBufferVersion Version, int FirstPassIndex, int LastPassIndex)>(
            transientLifetimes.Values);
        orderedTransientLifetimes.Sort(static (left, right) => {
            var firstPassComparison = left.FirstPassIndex.CompareTo(right.FirstPassIndex);
            return (firstPassComparison != 0)
                ? firstPassComparison
                : left.Version.ResourceId.CompareTo(right.Version.ResourceId);
        });

        var aliasGroups = new List<(
            BufferDescription Description,
            int LastPassIndex,
            bool CanAlias,
            ImmutableArray<GraphBufferVersion>.Builder Versions)>();
        var nonAliasableBufferIds = new HashSet<ulong>(nonAliasableTransientBufferIds);

        foreach (var (version, firstPassIndex, lastPassIndex) in orderedTransientLifetimes)
        {
            var aliasGroupIndex = -1;
            var canAlias = (version.Description.MemoryKind == GpuMemoryKind.DeviceLocal)
                && !nonAliasableBufferIds.Contains(version.ResourceId);

            for (var index = 0; canAlias && (index < aliasGroups.Count); index++)
            {
                var (aliasGroupDescription, aliasGroupLastPassIndex, aliasGroupCanAlias, _) = aliasGroups[index];

                if (aliasGroupCanAlias
                    && (aliasGroupLastPassIndex < firstPassIndex)
                    && AreTransientBufferDescriptionsCompatible(aliasGroupDescription, version.Description))
                {
                    aliasGroupIndex = index;
                    break;
                }
            }

            if (aliasGroupIndex < 0)
            {
                var versions = ImmutableArray.CreateBuilder<GraphBufferVersion>();
                versions.Add(version);
                aliasGroups.Add((version.Description, lastPassIndex, canAlias, versions));
            }
            else
            {
                var (aliasGroupDescription, _, aliasGroupCanAlias, aliasGroupVersions) = aliasGroups[aliasGroupIndex];
                aliasGroupVersions.Add(version);
                aliasGroups[aliasGroupIndex] = (
                    aliasGroupDescription,
                    lastPassIndex,
                    aliasGroupCanAlias,
                    aliasGroupVersions);
            }

            _ = transientBuffers.TryAdd(version.ResourceId, version);
        }

        var transientBufferGroups = ImmutableArray.CreateBuilder<ImmutableArray<GraphBufferVersion>>(aliasGroups.Count);
        var aliasingBarriers = ImmutableArray.CreateBuilder<GraphResourceAliasingBarrier>();

        foreach (var (_, _, _, aliasGroupVersions) in aliasGroups)
        {
            var versions = aliasGroupVersions.ToImmutable();
            transientBufferGroups.Add(versions);

            for (var index = 1; index < versions.Length; index++)
            {
                var previousVersion = versions[index - 1];
                var nextVersion = versions[index];
                var firstPassIndex = transientLifetimes[nextVersion.ResourceId].FirstPassIndex;
                aliasingBarriers.Add(new GraphResourceAliasingBarrier(
                    orderedPasses[firstPassIndex],
                    previousVersion,
                    nextVersion));
            }
        }

        var orderedTransientTextureLifetimes = new List<(
            GraphTextureVersion Version,
            int FirstPassIndex,
            int LastPassIndex)>(transientTextureLifetimes.Values);
        orderedTransientTextureLifetimes.Sort(static (left, right) => {
            var firstPassComparison = left.FirstPassIndex.CompareTo(right.FirstPassIndex);
            return (firstPassComparison != 0)
                ? firstPassComparison
                : left.Version.ResourceId.CompareTo(right.Version.ResourceId);
        });

        var textureAliasGroups = new List<(
            TextureDescription Description,
            int LastPassIndex,
            bool CanAlias,
            ImmutableArray<GraphTextureVersion>.Builder Versions)>();
        var nonAliasableTextureIds = new HashSet<ulong>(nonAliasableTransientTextureIds);

        foreach (var (version, firstPassIndex, lastPassIndex) in orderedTransientTextureLifetimes)
        {
            var aliasGroupIndex = -1;
            var canAlias = !nonAliasableTextureIds.Contains(version.ResourceId);

            for (var index = 0; canAlias && (index < textureAliasGroups.Count); index++)
            {
                var (aliasGroupDescription, aliasGroupLastPassIndex, aliasGroupCanAlias, _) = textureAliasGroups[index];

                if (aliasGroupCanAlias
                    && (aliasGroupLastPassIndex < firstPassIndex)
                    && GraphTextureAliasingBarrier.AreCompatible(aliasGroupDescription, version.Description))
                {
                    aliasGroupIndex = index;
                    break;
                }
            }

            if (aliasGroupIndex < 0)
            {
                var versions = ImmutableArray.CreateBuilder<GraphTextureVersion>();
                versions.Add(version);
                textureAliasGroups.Add((version.Description, lastPassIndex, canAlias, versions));
            }
            else
            {
                var (aliasGroupDescription, _, aliasGroupCanAlias, aliasGroupVersions) = textureAliasGroups[aliasGroupIndex];
                aliasGroupVersions.Add(version);
                textureAliasGroups[aliasGroupIndex] = (
                    aliasGroupDescription,
                    lastPassIndex,
                    aliasGroupCanAlias,
                    aliasGroupVersions);
            }
        }

        var transientTextureGroups = ImmutableArray.CreateBuilder<ImmutableArray<GraphTextureVersion>>(
            textureAliasGroups.Count);
        var textureAliasingBarriers = ImmutableArray.CreateBuilder<GraphTextureAliasingBarrier>();

        foreach (var (_, _, _, aliasGroupVersions) in textureAliasGroups)
        {
            var versions = aliasGroupVersions.ToImmutable();
            transientTextureGroups.Add(versions);

            for (var index = 1; index < versions.Length; index++)
            {
                var previousVersion = versions[index - 1];
                var nextVersion = versions[index];
                var firstPassIndex = transientTextureLifetimes[nextVersion.ResourceId].FirstPassIndex;
                textureAliasingBarriers.Add(new GraphTextureAliasingBarrier(
                    orderedPasses[firstPassIndex],
                    previousVersion,
                    nextVersion));
            }
        }

        Accesses = [.. accesses];
        AttachmentWrites = [.. attachmentWrites];
        BufferImports = [.. bufferImports];
        CameraIds = options.CameraIds;
        DeviceEpoch = deviceEpoch;
        FrameDimension = options.FrameDimension;
        FrameRevision = options.FrameRevision;
        OffscreenColorTarget = options.OffscreenColorTarget;
        OrderedPassCallbacks = [.. orderedPassCallbacks];
        OrderedPassKinds = [.. orderedPassKinds];
        OrderedPasses = [.. orderedPasses];
        Passes = [.. passes];
        PresentationSource = presentationSource;
        ProfileId = options.ProfileId;
        SnapshotId = options.SnapshotId;
        StateTransitions = [.. stateTransitions];
        SurfaceEpoch = options.SurfaceEpoch;
        TransientBufferAliasingBarriers = aliasingBarriers.ToImmutable();
        TransientBufferGroups = transientBufferGroups.ToImmutable();
        TransientTextureAliasingBarriers = textureAliasingBarriers.ToImmutable();
        TransientTextureGroups = transientTextureGroups.ToImmutable();
        SwapchainEpoch = options.SwapchainEpoch;
        SurfaceTarget = options.Surface;
        TargetExtent = options.TargetExtent;
        TextureImports = [.. textureImports];
        TickIndex = options.TickIndex;
        TransientBuffers = [.. transientBuffers.Values];
        TransientTextures = [.. transientTextures.Values];
        BuiltInSpriteState = builtInSpriteState;
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

    internal RenderSpriteGraphState? BuiltInSpriteState { get; }

    internal ImmutableArray<RenderPassCallback?> OrderedPassCallbacks { get; }

    internal ImmutableArray<RenderGraphPassKind> OrderedPassKinds { get; }

    internal ImmutableArray<RenderPassId> OrderedPasses { get; }

    internal ImmutableArray<RenderGraphPassNode> Passes { get; }

    internal GraphTextureVersion PresentationSource { get; }

    internal ImmutableArray<GraphResourceStateTransition> StateTransitions { get; }

    internal Surface? SurfaceTarget { get; }

    internal ImmutableArray<GraphTextureImportRecord> TextureImports { get; }

    internal ImmutableArray<GraphResourceAliasingBarrier> TransientBufferAliasingBarriers { get; }

    internal ImmutableArray<ImmutableArray<GraphBufferVersion>> TransientBufferGroups { get; }

    internal ImmutableArray<GraphBufferVersion> TransientBuffers { get; }

    internal ImmutableArray<GraphTextureAliasingBarrier> TransientTextureAliasingBarriers { get; }

    internal ImmutableArray<ImmutableArray<GraphTextureVersion>> TransientTextureGroups { get; }

    internal ImmutableArray<GraphTextureVersion> TransientTextures { get; }

    private static bool AreTransientBufferDescriptionsCompatible(
        BufferDescription left,
        BufferDescription right)
        => (left.ByteLength == right.ByteLength)
            && (left.MemoryKind == right.MemoryKind)
            && (left.StructuredStride == right.StructuredStride)
            && (left.Usage == right.Usage);

}
