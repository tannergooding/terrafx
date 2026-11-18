// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;

namespace TerraFX.Graphics;

internal sealed class GraphicsGraphSubmission
{
    internal const int MaximumNativeColorAttachmentCount = 8;

    private const ushort BufferCopyDestinationImportFlag = 1 << 11;
    private const byte BufferCopyDestinationPassFlag = 1 << 0;
    private const ushort BufferCopyDestinationResourceFlag = 1 << 0;
    private const ushort BufferCopySourceImportFlag = 1 << 10;
    private const byte BufferCopySourcePassFlag = 1 << 1;
    private const ushort BufferCopySourceResourceFlag = 1 << 1;
    private const byte BufferIndexBindingFlag = 1 << 3;
    private const ushort BufferIndexImportFlag = 1 << 7;
    private const ushort BufferIndexResourceFlag = 1 << 3;
    private const byte BufferStorageBindingFlag = 1 << 5;
    private const ushort BufferStorageImportFlag = 1 << 9;
    private const ushort BufferStorageResourceFlag = 1 << 5;
    private const byte BufferUniformBindingFlag = 1 << 4;
    private const ushort BufferUniformImportFlag = 1 << 8;
    private const ushort BufferUniformResourceFlag = 1 << 4;
    private const byte BufferVertexBindingFlag = 1 << 2;
    private const ushort BufferVertexImportFlag = 1 << 6;
    private const ushort BufferVertexResourceFlag = 1 << 2;
    private const ushort TextureColorImportFlag = 1 << 6;
    private const ushort TextureColorResourceFlag = 1 << 0;
    private const ushort TextureCopyDestinationImportFlag = 1 << 11;
    private const byte TextureCopyDestinationPassFlag = 1 << 2;
    private const ushort TextureCopyDestinationResourceFlag = 1 << 2;
    private const ushort TextureCopySourceImportFlag = 1 << 10;
    private const byte TextureCopySourcePassFlag = 1 << 3;
    private const ushort TextureCopySourceResourceFlag = 1 << 3;
    private const ushort TextureDepthImportFlag = 1 << 7;
    private const ushort TextureDepthResourceFlag = 1 << 1;
    private const byte TextureSampledBindingFlag = 1 << 4;
    private const ushort TextureSampledImportFlag = 1 << 9;
    private const ushort TextureSampledResourceFlag = 1 << 4;
    private const byte TextureStorageBindingFlag = 1 << 5;
    private const ushort TextureStorageImportFlag = 1 << 8;
    private const ushort TextureStorageResourceFlag = 1 << 5;

    private readonly int[] _accessPairIndex;
    private readonly int[] _bufferAliasingAfterIndex;
    private readonly int[] _bufferImportIndex;
    private readonly ColorAttachmentResourceIndexEntry[] _colorAttachmentResourceIndex;
    private readonly int[] _colorAttachmentResourceOrder;
    private readonly int[] _orderedPassIndex;
    private readonly int[] _passValidationIndex;
    private int _recordingOwnershipState;
    private readonly int[] _textureAliasingAfterIndex;
    private readonly int[] _textureImportIndex;

    internal GraphicsGraphSubmission(
        uint deviceEpoch,
        GpuTexture outputColorTarget,
        Surface? surfaceTarget,
        GraphTextureVersion presentationSource,
        uint surfaceEpoch,
        uint swapchainEpoch,
        ImmutableArray<RenderPassId> orderedPasses,
        ImmutableArray<GraphicsGraphSubmissionPass> passes,
        ImmutableArray<GraphResourceStateTransition> stateTransitions,
        ImmutableArray<GraphResourceAliasingBarrier> aliasingBarriers,
        ImmutableArray<GraphTextureAliasingBarrier> textureAliasingBarriers,
        ImmutableArray<GraphResourceAccess> accesses,
        ImmutableArray<GraphBufferImportRecord> bufferImports,
        ImmutableArray<GraphTextureImportRecord> textureImports,
        ImmutableArray<RecordedPassCommandList> recordedPassCommandLists,
        long maximumPreflightWorkspaceBytes = long.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfZero(deviceEpoch);
        ArgumentNullException.ThrowIfNull(outputColorTarget);
        ArgumentNullException.ThrowIfNull(presentationSource);

        if (orderedPasses.IsDefault
            || passes.IsDefault
            || stateTransitions.IsDefault
            || aliasingBarriers.IsDefault
            || textureAliasingBarriers.IsDefault
            || accesses.IsDefault
            || bufferImports.IsDefault
            || textureImports.IsDefault
            || recordedPassCommandLists.IsDefault)
        {
            throw new ArgumentException("A graphics graph submission requires initialized immutable data.");
        }

        if (outputColorTarget.DeviceEpoch != deviceEpoch)
        {
            throw new ArgumentException("The graph output target belongs to another device epoch.", nameof(outputColorTarget));
        }

        if (presentationSource.DeviceEpoch != deviceEpoch)
        {
            throw new ArgumentException("The presentation source belongs to another device epoch.", nameof(presentationSource));
        }

        if (surfaceTarget is null)
        {
            if ((surfaceEpoch != 0) || (swapchainEpoch != 0))
            {
                throw new ArgumentException("An offscreen graph cannot carry Surface or swapchain epochs.");
            }
        }
        else
        {
            ArgumentOutOfRangeException.ThrowIfZero(surfaceEpoch);
            ArgumentOutOfRangeException.ThrowIfZero(swapchainEpoch);
            ObjectDisposedException.ThrowIf(!surfaceTarget.IsLive, surfaceTarget);
            var surfaceSnapshot = surfaceTarget.CaptureSnapshot();

            if (!ReferenceEquals(surfaceTarget.Device, outputColorTarget.ResourceOwner.Device)
                || (surfaceSnapshot.Capabilities.SurfaceEpoch != surfaceEpoch)
                || (surfaceSnapshot.Capabilities.SwapchainEpoch != swapchainEpoch)
                || (surfaceSnapshot.ClientPixelSize.Width != outputColorTarget.Description.Width)
                || (surfaceSnapshot.ClientPixelSize.Height != outputColorTarget.Description.Height)
                || !SurfaceFormatCompatibility.IsSrgbPresentationSourceCompatible(
                    outputColorTarget.Description.Format,
                    surfaceSnapshot.ColorFormat))
            {
                throw new ArgumentException("The Surface presentation target does not match the output texture and captured epochs.", nameof(surfaceTarget));
            }
        }

        if (textureImports.IsDefaultOrEmpty
            || !ReferenceEquals(textureImports[0].Texture, outputColorTarget)
            || (textureImports[0].Version.ResourceId != presentationSource.ResourceId))
        {
            throw new ArgumentException("The graph output texture must be the first texture import and contain the selected presentation source.");
        }

        foreach (var pass in passes)
        {
            if (pass.CommandList?.HasDiagnosticRecipe == true)
            {
                maximumPreflightWorkspaceBytes = Math.Min(maximumPreflightWorkspaceBytes, 32L * 1024 * 1024);
                break;
            }
        }

        var colorAttachmentIndexCapacity = 0;

        foreach (var pass in passes)
        {
            foreach (var attachment in pass.Attachments)
            {
                if (!attachment.IsDepth && (attachment.Version is not null))
                {
                    colorAttachmentIndexCapacity = checked(colorAttachmentIndexCapacity + 1);
                }
            }
        }

        ValidatePreflightWorkspaceAllowance(
            bufferImports.Length,
            textureImports.Length,
            orderedPasses.Length,
            aliasingBarriers.Length,
            textureAliasingBarriers.Length,
            accesses.Length,
            colorAttachmentIndexCapacity,
            bufferImports.Length,
            textureImports.Length,
            passes.Length,
            maximumPreflightWorkspaceBytes);
        DeviceEpoch = deviceEpoch;
        OutputColorTarget = outputColorTarget;
        SurfaceTarget = surfaceTarget;
        PresentationSource = presentationSource;
        SurfaceEpoch = surfaceEpoch;
        SwapchainEpoch = swapchainEpoch;
        OrderedPasses = [.. orderedPasses];
        StateTransitions = [.. stateTransitions];
        AliasingBarriers = [.. aliasingBarriers];
        _bufferAliasingAfterIndex = CreateBufferAliasingAfterIndex(AliasingBarriers);
        TextureAliasingBarriers = [.. textureAliasingBarriers];
        _textureAliasingAfterIndex = CreateTextureAliasingAfterIndex(TextureAliasingBarriers);
        Accesses = [.. accesses];
        _accessPairIndex = CreateAccessPairIndex(Accesses);
        Passes = [.. passes];
        (_colorAttachmentResourceIndex, _colorAttachmentResourceOrder, ColorAttachmentResourceCount) =
            CreateColorAttachmentResourceIndex(Passes, colorAttachmentIndexCapacity);
        _passValidationIndex = CreatePassValidationIndex(Passes);
        BufferImports = [.. bufferImports];
        _bufferImportIndex = CreateBufferImportIndex(BufferImports);
        TextureImports = [.. textureImports];
        _textureImportIndex = CreateTextureImportIndex(TextureImports);
        _orderedPassIndex = CreateOrderedPassIndex(OrderedPasses);
        RecordedPassCommandLists = [.. recordedPassCommandLists];
    }

    internal long AccessPairIndexBackingByteCharge
        => CalculateAccessPairIndexBackingByteCharge(_accessPairIndex.LongLength);

    internal int AccessPairIndexStorageCapacity => _accessPairIndex.Length;

    internal ImmutableArray<GraphResourceAccess> Accesses { get; }

    internal ImmutableArray<GraphResourceAliasingBarrier> AliasingBarriers { get; }

    internal long BufferAliasingAfterIndexBackingByteCharge
        => CalculateBufferAliasingAfterIndexBackingByteCharge(_bufferAliasingAfterIndex.LongLength);

    internal int BufferAliasingAfterIndexStorageCapacity => _bufferAliasingAfterIndex.Length;

    internal long BufferImportIndexBackingByteCharge
        => CalculateBufferImportIndexBackingByteCharge(_bufferImportIndex.LongLength);

    internal int BufferImportIndexStorageCapacity => _bufferImportIndex.Length;

    internal ImmutableArray<GraphBufferImportRecord> BufferImports { get; }

    internal int ColorAttachmentResourceCount { get; private set; }

    internal long ColorAttachmentResourceIndexBackingByteCharge
        => CalculateColorAttachmentResourceIndexBackingByteCharge(_colorAttachmentResourceIndex.LongLength);

    internal int ColorAttachmentResourceIndexStorageCapacity => _colorAttachmentResourceIndex.Length;

    internal int ColorAttachmentResourceOrderStorageCapacity => _colorAttachmentResourceOrder.Length;

    internal uint DeviceEpoch { get; }

    internal long OrderedPassIndexBackingByteCharge
        => CalculateOrderedPassIndexBackingByteCharge(_orderedPassIndex.LongLength);

    internal int OrderedPassIndexStorageCapacity => _orderedPassIndex.Length;

    internal ImmutableArray<RenderPassId> OrderedPasses { get; }

    internal GpuTexture OutputColorTarget { get; }

    internal long PassValidationIndexBackingByteCharge
        => CalculatePassValidationIndexBackingByteCharge(_passValidationIndex.LongLength);

    internal int PassValidationIndexStorageCapacity => _passValidationIndex.Length;

    internal ImmutableArray<GraphicsGraphSubmissionPass> Passes { get; private set; }

    internal long? PresentationSequence { get; private set; }

    internal GraphTextureVersion PresentationSource { get; }

    internal ImmutableArray<RecordedPassCommandList> RecordedPassCommandLists { get; private set; }

    internal bool RequiresDiagnosticQueueAdmission
    {
        get
        {
            foreach (var pass in Passes)
            {
                if (pass.CommandList?.HasDiagnosticRecipe == true)
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal ImmutableArray<GraphResourceStateTransition> StateTransitions { get; }

    internal uint SurfaceEpoch { get; }

    internal Surface? SurfaceTarget { get; }

    internal uint SwapchainEpoch { get; }

    internal long TextureAliasingAfterIndexBackingByteCharge
        => CalculateTextureAliasingAfterIndexBackingByteCharge(_textureAliasingAfterIndex.LongLength);

    internal int TextureAliasingAfterIndexStorageCapacity => _textureAliasingAfterIndex.Length;

    internal ImmutableArray<GraphTextureAliasingBarrier> TextureAliasingBarriers { get; }

    internal long TextureImportIndexBackingByteCharge
        => CalculateTextureImportIndexBackingByteCharge(_textureImportIndex.LongLength);

    internal int TextureImportIndexStorageCapacity => _textureImportIndex.Length;

    internal ImmutableArray<GraphTextureImportRecord> TextureImports { get; }

    internal static long CalculateAccessPairIndexBackingByteCharge(long capacity)
        => CalculateIndexBackingByteCharge(capacity);

    internal static long CalculateAccessValidationFlagsBackingByteCharge(long capacity)
        => CalculatePassValidationFlagsBackingByteCharge(capacity);

    internal static long CalculateAliasingIndexBackingByteCharge(
        long bufferAliasingCapacity,
        long textureAliasingCapacity)
        => checked(
            CalculateBufferAliasingAfterIndexBackingByteCharge(bufferAliasingCapacity)
            + CalculateTextureAliasingAfterIndexBackingByteCharge(textureAliasingCapacity));

    internal static long CalculateBufferAliasingAfterIndexBackingByteCharge(long capacity)
        => CalculateIndexBackingByteCharge(capacity);

    internal static long CalculateBufferImportFlagsBackingByteCharge(long capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        return (capacity == 0) ? 0 : checked(64 + ((checked(capacity * sizeof(ushort)) + 15) & ~15L));
    }

    internal static long CalculateBufferImportIndexBackingByteCharge(long capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        return CalculateIndexBackingByteCharge(capacity);
    }

    internal static long CalculateColorAttachmentResourceIndexBackingByteCharge(long capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        return checked(
            (capacity == 0
                ? 0
                : 64 + ((checked(capacity * Unsafe.SizeOf<ColorAttachmentResourceIndexEntry>()) + 15) & ~15L))
            + CalculateIndexBackingByteCharge(capacity));
    }

    internal static long CalculateImportIndexBackingByteCharge(
        long bufferImportCapacity,
        long textureImportCapacity,
        long orderedPassCapacity)
        => checked(
            CalculateBufferImportIndexBackingByteCharge(bufferImportCapacity)
            + CalculateTextureImportIndexBackingByteCharge(textureImportCapacity)
            + CalculateOrderedPassIndexBackingByteCharge(orderedPassCapacity));

    internal static long CalculateOrderedPassIndexBackingByteCharge(long capacity)
        => CalculateIndexBackingByteCharge(capacity);

    internal static long CalculatePassValidationFlagsBackingByteCharge(long capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        return (capacity == 0) ? 0 : checked(64 + ((checked(capacity * sizeof(byte)) + 15) & ~15L));
    }

    internal static long CalculatePassValidationIndexBackingByteCharge(long capacity)
        => CalculateIndexBackingByteCharge(capacity);

    internal static long CalculatePreflightIndexBackingByteCharge(
        long bufferImportCapacity,
        long textureImportCapacity,
        long orderedPassCapacity,
        long bufferAliasingCapacity,
        long textureAliasingCapacity)
        => checked(
            CalculateImportIndexBackingByteCharge(
                bufferImportCapacity,
                textureImportCapacity,
                orderedPassCapacity)
            + CalculateAliasingIndexBackingByteCharge(bufferAliasingCapacity, textureAliasingCapacity));

    internal static long CalculatePreflightWorkspaceBackingByteCharge(
        long bufferImportCapacity,
        long textureImportCapacity,
        long orderedPassCapacity,
        long bufferAliasingCapacity,
        long textureAliasingCapacity,
        long bufferImportFlagCapacity,
        long textureImportFlagCapacity,
        long passValidationCapacity,
        long accessPairCapacity = 0,
        long colorAttachmentIndexCapacity = 0)
        => checked(
            CalculatePreflightIndexBackingByteCharge(
                bufferImportCapacity,
                textureImportCapacity,
                orderedPassCapacity,
                bufferAliasingCapacity,
                textureAliasingCapacity)
            + CalculateAccessPairIndexBackingByteCharge(accessPairCapacity)
            + CalculateAccessValidationFlagsBackingByteCharge(accessPairCapacity)
            + CalculateColorAttachmentResourceIndexBackingByteCharge(colorAttachmentIndexCapacity)
            + CalculateBufferImportFlagsBackingByteCharge(bufferImportFlagCapacity)
            + CalculateTextureImportFlagsBackingByteCharge(textureImportFlagCapacity)
            + CalculatePassValidationIndexBackingByteCharge(passValidationCapacity)
            + CalculatePassValidationFlagsBackingByteCharge(passValidationCapacity));

    internal static long CalculateTextureAliasingAfterIndexBackingByteCharge(long capacity)
        => CalculateIndexBackingByteCharge(capacity);

    internal static long CalculateTextureImportFlagsBackingByteCharge(long capacity)
        => CalculateBufferImportFlagsBackingByteCharge(capacity);

    internal static long CalculateTextureImportIndexBackingByteCharge(long capacity)
        => CalculateIndexBackingByteCharge(capacity);

    internal static IGraphicsBackendPipeline GetPipeline(RecordedGraphicsCommand command)
    {
        var pipeline = command.PipelineOwner
            ?? throw new InvalidOperationException("A recorded pipeline command lost its owner.");
        return pipeline.ResourceOwner.BackendResource as IGraphicsBackendPipeline
            ?? throw new InvalidOperationException("The recorded pipeline lost its backend resource.");
    }

    internal static bool MatchesColorTargetFormats(
        ImmutableArray<TextureFormat> formats,
        ImmutableArray<GraphicsGraphSubmissionAttachment> attachments)
    {
        var colorIndex = 0;

        foreach (var attachment in attachments)
        {
            if (attachment.IsDepth)
            {
                continue;
            }

            if ((colorIndex >= formats.Length)
                || (formats[colorIndex] != attachment.Version.Description.Format))
            {
                return false;
            }

            colorIndex++;
        }

        return colorIndex == formats.Length;
    }

    private static void AddImportResourceUsageFlag(ushort[] flags, int importIndex, ushort flag, ref int usedCount)
    {
        if ((flags[importIndex] & flag) == 0)
        {
            flags[importIndex] |= flag;
            usedCount++;
        }
    }

    private static long CalculateIndexBackingByteCharge(long capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        return (capacity == 0) ? 0 : checked(64 + ((checked(capacity * sizeof(int)) + 15) & ~15L));
    }

    private static int CompareAccessPairIndices(
        ImmutableArray<GraphResourceAccess> accesses,
        int left,
        int right)
    {
        var leftAccess = accesses[left];
        var rightAccess = accesses[right];
        var comparison = CompareRenderPassIds(leftAccess.PassId, rightAccess.PassId);

        if (comparison == 0)
        {
            comparison = leftAccess.ResourceId.CompareTo(rightAccess.ResourceId);
        }

        return (comparison != 0) ? comparison : left.CompareTo(right);
    }

    private static int CompareBufferAliasingAfterIndices(
        ImmutableArray<GraphResourceAliasingBarrier> barriers,
        int left,
        int right)
    {
        var comparison = barriers[left].AfterResourceId.CompareTo(barriers[right].AfterResourceId);
        return (comparison != 0) ? comparison : left.CompareTo(right);
    }

    private static int CompareBufferImportIndices(
        ImmutableArray<GraphBufferImportRecord> imports,
        int left,
        int right)
    {
        var leftImport = imports[left];
        var rightImport = imports[right];

        // Preparation must not move malformed-entry failure ahead of the
        // original ordered validation loop.
        if (leftImport is null)
        {
            return (rightImport is null) ? left.CompareTo(right) : -1;
        }

        if (rightImport is null)
        {
            return 1;
        }

        var comparison = leftImport.Version.ResourceId.CompareTo(rightImport.Version.ResourceId);
        return (comparison != 0) ? comparison : left.CompareTo(right);
    }

    private static int CompareColorAttachmentResourceIndexEntries(
        ColorAttachmentResourceIndexEntry left,
        ColorAttachmentResourceIndexEntry right,
        bool byResourceId)
    {
        var comparison = byResourceId
            ? left.ResourceId.CompareTo(right.ResourceId)
            : left.FirstOccurrenceIndex.CompareTo(right.FirstOccurrenceIndex);
        return (comparison != 0) ? comparison : left.FirstOccurrenceIndex.CompareTo(right.FirstOccurrenceIndex);
    }

    private static int CompareOrderedPassIndices(
        ImmutableArray<RenderPassId> passes,
        int left,
        int right)
    {
        var leftPass = passes[left];
        var rightPass = passes[right];
        var comparison = CompareRenderPassIds(leftPass, rightPass);

        return (comparison != 0) ? comparison : left.CompareTo(right);
    }

    private static int ComparePassValidationIndices(
        ImmutableArray<GraphicsGraphSubmissionPass> passes,
        int left,
        int right)
    {
        var comparison = CompareRenderPassIds(passes[left].PassId, passes[right].PassId);
        return (comparison != 0) ? comparison : left.CompareTo(right);
    }

    private static int CompareRenderPassIds(RenderPassId left, RenderPassId right)
    {
        var comparison = left.GraphOwnerId.CompareTo(right.GraphOwnerId);

        if (comparison == 0)
        {
            comparison = left.Value.CompareTo(right.Value);
        }

        return comparison;
    }

    private static int CompareTextureAliasingAfterIndices(
        ImmutableArray<GraphTextureAliasingBarrier> barriers,
        int left,
        int right)
    {
        var comparison = barriers[left].AfterResourceId.CompareTo(barriers[right].AfterResourceId);
        return (comparison != 0) ? comparison : left.CompareTo(right);
    }

    private static int CompareTextureImportIndices(
        ImmutableArray<GraphTextureImportRecord> imports,
        int left,
        int right)
    {
        var leftImport = imports[left];
        var rightImport = imports[right];

        if (leftImport is null)
        {
            return (rightImport is null) ? left.CompareTo(right) : -1;
        }

        if (rightImport is null)
        {
            return 1;
        }

        var comparison = leftImport.Version.ResourceId.CompareTo(rightImport.Version.ResourceId);

        if (comparison == 0)
        {
            comparison = rightImport.IsTransient.CompareTo(leftImport.IsTransient);
        }

        return (comparison != 0) ? comparison : left.CompareTo(right);
    }

    private static int[] CreateAccessPairIndex(ImmutableArray<GraphResourceAccess> accesses)
    {
        if (accesses.IsEmpty)
        {
            return [];
        }

        var indices = new int[accesses.Length];

        for (var index = 0; index < indices.Length; index++)
        {
            indices[index] = index;
        }

        for (var root = (indices.Length / 2) - 1; root >= 0; root--)
        {
            SiftAccessPairIndex(accesses, indices, root, indices.Length);
        }

        for (var end = indices.Length - 1; end > 0; end--)
        {
            (indices[0], indices[end]) = (indices[end], indices[0]);
            SiftAccessPairIndex(accesses, indices, 0, end);
        }

        return indices;
    }

    private static int[] CreateBufferAliasingAfterIndex(ImmutableArray<GraphResourceAliasingBarrier> barriers)
    {
        if (barriers.IsEmpty)
        {
            return [];
        }

        var indices = new int[barriers.Length];

        for (var index = 0; index < indices.Length; index++)
        {
            indices[index] = index;
        }

        for (var root = (indices.Length / 2) - 1; root >= 0; root--)
        {
            SiftBufferAliasingAfterIndex(barriers, indices, root, indices.Length);
        }

        for (var end = indices.Length - 1; end > 0; end--)
        {
            (indices[0], indices[end]) = (indices[end], indices[0]);
            SiftBufferAliasingAfterIndex(barriers, indices, 0, end);
        }

        return indices;
    }

    private static int[] CreateBufferImportIndex(ImmutableArray<GraphBufferImportRecord> imports)
    {
        if (imports.IsEmpty)
        {
            return [];
        }

        var indices = new int[imports.Length];

        for (var index = 0; index < indices.Length; index++)
        {
            indices[index] = index;
        }

        for (var root = (indices.Length / 2) - 1; root >= 0; root--)
        {
            SiftBufferImportIndex(imports, indices, root, indices.Length);
        }

        for (var end = indices.Length - 1; end > 0; end--)
        {
            (indices[0], indices[end]) = (indices[end], indices[0]);
            SiftBufferImportIndex(imports, indices, 0, end);
        }

        return indices;
    }

    private static (
        ColorAttachmentResourceIndexEntry[] Entries,
        int[] Order,
        int UniqueCount) CreateColorAttachmentResourceIndex(
        ImmutableArray<GraphicsGraphSubmissionPass> passes,
        int capacity)
    {
        if (capacity == 0)
        {
            return ([], [], 0);
        }

        var entries = new ColorAttachmentResourceIndexEntry[capacity];
        var order = new int[capacity];
        var resourceCount = 0;

        foreach (var pass in passes)
        {
            foreach (var attachment in pass.Attachments)
            {
                if (!attachment.IsDepth && (attachment.Version is GraphTextureVersion version))
                {
                    entries[resourceCount] = new ColorAttachmentResourceIndexEntry(version.ResourceId, resourceCount);
                    resourceCount++;
                }
            }
        }

        SortColorAttachmentResourceIndex(entries, resourceCount, byResourceId: true);
        var uniqueCount = 0;

        for (var index = 0; index < resourceCount; index++)
        {
            if ((uniqueCount == 0) || (entries[index].ResourceId != entries[uniqueCount - 1].ResourceId))
            {
                entries[uniqueCount++] = entries[index];
            }
        }

        SortColorAttachmentResourceIndex(entries, uniqueCount, byResourceId: false);

        for (var index = 0; index < uniqueCount; index++)
        {
            entries[index] = entries[index] with { FirstOccurrenceIndex = index };
        }

        SortColorAttachmentResourceIndex(entries, uniqueCount, byResourceId: true);

        for (var index = 0; index < uniqueCount; index++)
        {
            order[entries[index].FirstOccurrenceIndex] = index;
        }

        return (entries, order, uniqueCount);
    }

    private static int[] CreateOrderedPassIndex(ImmutableArray<RenderPassId> passes)
    {
        if (passes.IsEmpty)
        {
            return [];
        }

        var indices = new int[passes.Length];

        for (var index = 0; index < indices.Length; index++)
        {
            indices[index] = index;
        }

        for (var root = (indices.Length / 2) - 1; root >= 0; root--)
        {
            SiftOrderedPassIndex(passes, indices, root, indices.Length);
        }

        for (var end = indices.Length - 1; end > 0; end--)
        {
            (indices[0], indices[end]) = (indices[end], indices[0]);
            SiftOrderedPassIndex(passes, indices, 0, end);
        }

        return indices;
    }

    private static int[] CreatePassValidationIndex(ImmutableArray<GraphicsGraphSubmissionPass> passes)
    {
        if (passes.IsEmpty)
        {
            return [];
        }

        var indices = new int[passes.Length];

        for (var index = 0; index < indices.Length; index++)
        {
            indices[index] = index;
        }

        for (var root = (indices.Length / 2) - 1; root >= 0; root--)
        {
            SiftPassValidationIndex(passes, indices, root, indices.Length);
        }

        for (var end = indices.Length - 1; end > 0; end--)
        {
            (indices[0], indices[end]) = (indices[end], indices[0]);
            SiftPassValidationIndex(passes, indices, 0, end);
        }

        return indices;
    }

    private static int[] CreateTextureAliasingAfterIndex(ImmutableArray<GraphTextureAliasingBarrier> barriers)
    {
        if (barriers.IsEmpty)
        {
            return [];
        }

        var indices = new int[barriers.Length];

        for (var index = 0; index < indices.Length; index++)
        {
            indices[index] = index;
        }

        for (var root = (indices.Length / 2) - 1; root >= 0; root--)
        {
            SiftTextureAliasingAfterIndex(barriers, indices, root, indices.Length);
        }

        for (var end = indices.Length - 1; end > 0; end--)
        {
            (indices[0], indices[end]) = (indices[end], indices[0]);
            SiftTextureAliasingAfterIndex(barriers, indices, 0, end);
        }

        return indices;
    }

    private static int[] CreateTextureImportIndex(ImmutableArray<GraphTextureImportRecord> imports)
    {
        if (imports.IsEmpty)
        {
            return [];
        }

        var indices = new int[imports.Length];

        for (var index = 0; index < indices.Length; index++)
        {
            indices[index] = index;
        }

        for (var root = (indices.Length / 2) - 1; root >= 0; root--)
        {
            SiftTextureImportIndex(imports, indices, root, indices.Length);
        }

        for (var end = indices.Length - 1; end > 0; end--)
        {
            (indices[0], indices[end]) = (indices[end], indices[0]);
            SiftTextureImportIndex(imports, indices, 0, end);
        }

        return indices;
    }

    private static int FindFirstAccessPairIndex(
        ImmutableArray<GraphResourceAccess> accesses,
        int[] accessPairIndex,
        RenderPassId passId,
        ulong resourceId,
        out int accessIndex)
    {
        var first = 0;
        var last = accessPairIndex.Length;

        while (first < last)
        {
            var middle = first + ((last - first) / 2);
            var candidate = accesses[accessPairIndex[middle]];
            var comparison = CompareRenderPassIds(candidate.PassId, passId);

            if (comparison == 0)
            {
                comparison = candidate.ResourceId.CompareTo(resourceId);
            }

            if (comparison < 0)
            {
                first = middle + 1;
            }
            else
            {
                last = middle;
            }
        }

        if (first < accessPairIndex.Length)
        {
            var candidateIndex = accessPairIndex[first];
            var candidate = accesses[candidateIndex];

            if ((candidate.PassId == passId) && (candidate.ResourceId == resourceId))
            {
                accessIndex = candidateIndex;
                return first;
            }
        }

        accessIndex = -1;
        return -1;
    }

    private static int FindPassValidationIndex(
        ImmutableArray<GraphicsGraphSubmissionPass> passes,
        int[] passValidationIndex,
        RenderPassId passId,
        out int passIndex)
    {
        var first = 0;
        var last = passValidationIndex.Length;

        while (first < last)
        {
            var middle = first + ((last - first) / 2);
            var candidateIndex = passValidationIndex[middle];

            if (CompareRenderPassIds(passes[candidateIndex].PassId, passId) < 0)
            {
                first = middle + 1;
            }
            else
            {
                last = middle;
            }
        }

        if (first < passValidationIndex.Length)
        {
            var candidateIndex = passValidationIndex[first];

            if (passes[candidateIndex].PassId == passId)
            {
                passIndex = candidateIndex;
                return first;
            }
        }

        passIndex = -1;
        return -1;
    }

    private static bool HasAccessValidationFlag(byte[] accessValidationFlags, int accessIndex, byte flag)
        => (accessValidationFlags[accessIndex] & flag) != 0;

    private static bool HasAccessValidationFlagForPair(
        byte[] accessValidationFlags,
        ImmutableArray<GraphResourceAccess> accesses,
        int[] accessPairIndex,
        RenderPassId passId,
        ulong resourceId,
        byte flag)
    {
        var index = FindFirstAccessPairIndex(accesses, accessPairIndex, passId, resourceId, out var accessIndex);

        while ((index >= 0) && (index < accessPairIndex.Length))
        {
            var access = accesses[accessPairIndex[index]];

            if ((access.PassId != passId) || (access.ResourceId != resourceId))
            {
                return false;
            }

            if (HasAccessValidationFlag(accessValidationFlags, accessIndex, flag))
            {
                return true;
            }

            index++;

            if (index < accessPairIndex.Length)
            {
                accessIndex = accessPairIndex[index];
            }
        }

        return false;
    }

    private static bool HasEarlierPipelineBinding(
        PipelineBindingLayout bindingLayout,
        ShaderResourceSignature resource)
    {
        foreach (var candidate in bindingLayout.EnumerateDistinctResources())
        {
            if (candidate == resource)
            {
                return false;
            }

            if ((candidate.Kind == resource.Kind)
                && (candidate.Group == resource.Group)
                && (candidate.Binding == resource.Binding))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasPassAttachment(
        ImmutableArray<GraphicsGraphSubmissionPass> passes,
        int[] passValidationIndex,
        RenderPassId passId,
        ulong resourceId,
        bool isDepth)
    {
        var index = FindPassValidationIndex(passes, passValidationIndex, passId, out _);

        while ((index >= 0) && (index < passValidationIndex.Length))
        {
            var pass = passes[passValidationIndex[index]];

            if (pass.PassId != passId)
            {
                return false;
            }

            foreach (var attachment in pass.Attachments)
            {
                if ((attachment.IsDepth == isDepth) && (attachment.Version.ResourceId == resourceId))
                {
                    return true;
                }
            }

            index++;
        }

        return false;
    }

    private static bool HasPassValidationFlag(
        byte[] passValidationFlags,
        ImmutableArray<GraphicsGraphSubmissionPass> passes,
        int[] passValidationIndex,
        RenderPassId passId,
        byte flag)
        => (FindPassValidationIndex(passes, passValidationIndex, passId, out var passIndex) >= 0)
            && ((passValidationFlags[passIndex] & flag) != 0);

    private static void SetAccessValidationFlag(
        byte[] accessValidationFlags,
        ImmutableArray<GraphResourceAccess> accesses,
        int[] accessPairIndex,
        RenderPassId passId,
        ulong resourceId,
        byte flag)
    {
        var index = FindFirstAccessPairIndex(accesses, accessPairIndex, passId, resourceId, out var accessIndex);

        while ((index >= 0) && (index < accessPairIndex.Length))
        {
            var access = accesses[accessPairIndex[index]];

            if ((access.PassId != passId) || (access.ResourceId != resourceId))
            {
                return;
            }

            accessValidationFlags[accessIndex] |= flag;
            index++;

            if (index < accessPairIndex.Length)
            {
                accessIndex = accessPairIndex[index];
            }
        }
    }

    private static void SetPassValidationFlag(
        byte[] passValidationFlags,
        ImmutableArray<GraphicsGraphSubmissionPass> passes,
        int[] passValidationIndex,
        RenderPassId passId,
        byte flag)
    {
        if (FindPassValidationIndex(passes, passValidationIndex, passId, out var passIndex) >= 0)
        {
            passValidationFlags[passIndex] |= flag;
        }
    }

    private static void SiftAccessPairIndex(
        ImmutableArray<GraphResourceAccess> accesses,
        int[] indices,
        int root,
        int count)
    {
        while (root < (count / 2))
        {
            var child = (2 * root) + 1;

            if (((child + 1) < count)
                && (CompareAccessPairIndices(accesses, indices[child], indices[child + 1]) < 0))
            {
                child++;
            }

            if (CompareAccessPairIndices(accesses, indices[root], indices[child]) >= 0)
            {
                return;
            }

            (indices[root], indices[child]) = (indices[child], indices[root]);
            root = child;
        }
    }

    private static void SiftBufferAliasingAfterIndex(
        ImmutableArray<GraphResourceAliasingBarrier> barriers,
        int[] indices,
        int root,
        int count)
    {
        while (root < (count / 2))
        {
            var child = (2 * root) + 1;

            if (((child + 1) < count)
                && (CompareBufferAliasingAfterIndices(barriers, indices[child], indices[child + 1]) < 0))
            {
                child++;
            }

            if (CompareBufferAliasingAfterIndices(barriers, indices[root], indices[child]) >= 0)
            {
                return;
            }

            (indices[root], indices[child]) = (indices[child], indices[root]);
            root = child;
        }
    }

    private static void SiftBufferImportIndex(
        ImmutableArray<GraphBufferImportRecord> imports,
        int[] indices,
        int root,
        int count)
    {
        while (root < (count / 2))
        {
            var child = (2 * root) + 1;

            if (((child + 1) < count)
                && (CompareBufferImportIndices(imports, indices[child], indices[child + 1]) < 0))
            {
                child++;
            }

            if (CompareBufferImportIndices(imports, indices[root], indices[child]) >= 0)
            {
                return;
            }

            (indices[root], indices[child]) = (indices[child], indices[root]);
            root = child;
        }
    }

    private static void SiftColorAttachmentResourceIndex(
        ColorAttachmentResourceIndexEntry[] entries,
        int root,
        int length,
        bool byResourceId)
    {
        while (true)
        {
            var child = (root * 2) + 1;

            if (child >= length)
            {
                return;
            }

            if (((child + 1) < length)
                && (CompareColorAttachmentResourceIndexEntries(entries[child], entries[child + 1], byResourceId) < 0))
            {
                child++;
            }

            if (CompareColorAttachmentResourceIndexEntries(entries[root], entries[child], byResourceId) >= 0)
            {
                return;
            }

            (entries[root], entries[child]) = (entries[child], entries[root]);
            root = child;
        }
    }

    private static void SiftOrderedPassIndex(
        ImmutableArray<RenderPassId> passes,
        int[] indices,
        int root,
        int count)
    {
        while (root < (count / 2))
        {
            var child = (2 * root) + 1;

            if (((child + 1) < count)
                && (CompareOrderedPassIndices(passes, indices[child], indices[child + 1]) < 0))
            {
                child++;
            }

            if (CompareOrderedPassIndices(passes, indices[root], indices[child]) >= 0)
            {
                return;
            }

            (indices[root], indices[child]) = (indices[child], indices[root]);
            root = child;
        }
    }

    private static void SiftPassValidationIndex(
        ImmutableArray<GraphicsGraphSubmissionPass> passes,
        int[] indices,
        int root,
        int count)
    {
        while (root < (count / 2))
        {
            var child = (2 * root) + 1;

            if (((child + 1) < count)
                && (ComparePassValidationIndices(passes, indices[child], indices[child + 1]) < 0))
            {
                child++;
            }

            if (ComparePassValidationIndices(passes, indices[root], indices[child]) >= 0)
            {
                return;
            }

            (indices[root], indices[child]) = (indices[child], indices[root]);
            root = child;
        }
    }

    private static void SiftTextureAliasingAfterIndex(
        ImmutableArray<GraphTextureAliasingBarrier> barriers,
        int[] indices,
        int root,
        int count)
    {
        while (root < (count / 2))
        {
            var child = (2 * root) + 1;

            if (((child + 1) < count)
                && (CompareTextureAliasingAfterIndices(barriers, indices[child], indices[child + 1]) < 0))
            {
                child++;
            }

            if (CompareTextureAliasingAfterIndices(barriers, indices[root], indices[child]) >= 0)
            {
                return;
            }

            (indices[root], indices[child]) = (indices[child], indices[root]);
            root = child;
        }
    }

    private static void SiftTextureImportIndex(
        ImmutableArray<GraphTextureImportRecord> imports,
        int[] indices,
        int root,
        int count)
    {
        while (root < (count / 2))
        {
            var child = (2 * root) + 1;

            if (((child + 1) < count)
                && (CompareTextureImportIndices(imports, indices[child], indices[child + 1]) < 0))
            {
                child++;
            }

            if (CompareTextureImportIndices(imports, indices[root], indices[child]) >= 0)
            {
                return;
            }

            (indices[root], indices[child]) = (indices[child], indices[root]);
            root = child;
        }
    }

    private static void SortColorAttachmentResourceIndex(
        ColorAttachmentResourceIndexEntry[] entries,
        int length,
        bool byResourceId)
    {
        for (var root = (length / 2) - 1; root >= 0; root--)
        {
            SiftColorAttachmentResourceIndex(entries, root, length, byResourceId);
        }

        for (var end = length - 1; end > 0; end--)
        {
            (entries[0], entries[end]) = (entries[end], entries[0]);
            SiftColorAttachmentResourceIndex(entries, 0, end, byResourceId);
        }
    }

    private static bool TryGetPipelineResource(
        PipelineBindingLayout bindingLayout,
        ShaderResourceKind kind,
        ShaderBindingGroup group,
        int binding,
        out ShaderResourceSignature resource)
    {
        foreach (var candidate in bindingLayout.EnumerateDistinctResources())
        {
            if ((candidate.Kind == kind)
                && (candidate.Group == group)
                && (candidate.Binding == binding))
            {
                resource = candidate;
                return true;
            }
        }

        resource = default;
        return false;
    }

    [SuppressMessage(
        "Usage",
        "CA2201:Do not raise reserved exception types",
        Justification = "A preflight workspace exceeding its component allowance is refused before allocation.")]
    private static void ValidatePreflightWorkspaceAllowance(
        int bufferImportCapacity,
        int textureImportCapacity,
        int orderedPassCapacity,
        int bufferAliasingCapacity,
        int textureAliasingCapacity,
        int accessPairCapacity,
        int colorAttachmentIndexCapacity,
        int bufferImportFlagCapacity,
        int textureImportFlagCapacity,
        int passValidationCapacity,
        long maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);

        if (CalculatePreflightWorkspaceBackingByteCharge(
                bufferImportCapacity,
                textureImportCapacity,
                orderedPassCapacity,
                bufferAliasingCapacity,
                textureAliasingCapacity,
                bufferImportFlagCapacity,
                textureImportFlagCapacity,
                passValidationCapacity,
                accessPairCapacity,
                colorAttachmentIndexCapacity) > maximumBytes)
        {
            throw new OutOfMemoryException("The graph preflight workspace exceeds its CPU component allowance.");
        }
    }

    internal void DisposeRecordedPassCommandLists()
    {
        if (Interlocked.CompareExchange(ref _recordingOwnershipState, 1, 0) != 0)
        {
            return;
        }

        var commandLists = RecordedPassCommandLists;
        RecordedPassCommandLists = [];
        Passes = [];

        foreach (var commandList in commandLists)
        {
            commandList.Dispose();
        }
    }

    internal IGraphicsBackendBuffer GetBuffer(ulong resourceId)
    {
        var buffer = GetBufferImport(resourceId).Buffer;
        return buffer.ResourceOwner.BackendResource as IGraphicsBackendBuffer
            ?? throw new InvalidOperationException("The imported buffer lost its backend resource.");
    }

    internal ulong GetColorAttachmentResourceId(int index)
        => _colorAttachmentResourceIndex[_colorAttachmentResourceOrder[index]].ResourceId;

    internal int GetColorAttachmentResourceIndex(ulong resourceId)
    {
        var first = 0;
        var last = ColorAttachmentResourceCount;

        while (first < last)
        {
            var middle = first + ((last - first) / 2);

            if (_colorAttachmentResourceIndex[middle].ResourceId < resourceId)
            {
                first = middle + 1;
            }
            else
            {
                last = middle;
            }
        }

        if ((first < ColorAttachmentResourceCount)
            && (_colorAttachmentResourceIndex[first].ResourceId == resourceId))
        {
            return _colorAttachmentResourceIndex[first].FirstOccurrenceIndex;
        }

        throw new KeyNotFoundException("The graph color attachment is not in the color resource index.");
    }

    internal IGraphicsBackendTexture GetTexture(ulong resourceId)
    {
        var texture = GetTextureImport(resourceId).Texture;
        return texture.ResourceOwner.BackendResource as IGraphicsBackendTexture
            ?? throw new InvalidOperationException("The imported texture lost its backend resource.");
    }

    internal bool HasFirstStorageUse(RenderPassId passId, ulong resourceId)
    {
        var hasPriorStorageWrite = false;

        foreach (var orderedPass in OrderedPasses)
        {
            if (orderedPass == passId)
            {
                foreach (var access in Accesses)
                {
                    if ((access.PassId == passId)
                        && (access.ResourceId == resourceId)
                        && access.IsStorageReadWritePair)
                    {
                        return !hasPriorStorageWrite;
                    }
                }

                return false;
            }

            foreach (var access in Accesses)
            {
                if ((access.PassId == orderedPass)
                    && (access.ResourceId == resourceId)
                    && access.IsStorageReadWritePair
                    && access.IsWrite)
                {
                    hasPriorStorageWrite = true;
                }
            }
        }

        throw new ArgumentException("The pass is not part of this ordered graph.", nameof(passId));
    }

    internal bool HasPriorStorageWriteHazard(RenderPassId passId, ulong resourceId)
    {
        var priorPassCount = 0;

        while ((priorPassCount < OrderedPasses.Length) && (OrderedPasses[priorPassCount] != passId))
        {
            priorPassCount++;
        }

        if (priorPassCount == OrderedPasses.Length)
        {
            throw new ArgumentException("The pass is not part of this ordered graph.", nameof(passId));
        }

        // Both backends query this after queue acceptance; borrow the frozen
        // accesses rather than creating a new managed scratch owner.
        foreach (var currentAccess in Accesses)
        {
            if ((currentAccess.PassId != passId)
                || (currentAccess.ResourceId != resourceId)
                || !currentAccess.IsStorageReadWritePair)
            {
                continue;
            }

            for (var passIndex = 0; passIndex < priorPassCount; passIndex++)
            {
                var priorPass = OrderedPasses[passIndex];

                foreach (var priorWrite in Accesses)
                {
                    if ((priorWrite.PassId == priorPass)
                        && (priorWrite.ResourceId == resourceId)
                        && priorWrite.IsStorageReadWritePair
                        && priorWrite.IsWrite
                        && priorWrite.Overlaps(currentAccess))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    internal void RecordSubmittedOwnerStates()
    {
        foreach (var passId in OrderedPasses)
        {
            foreach (var access in Accesses)
            {
                if (access.PassId == passId)
                {
                    if (access.IsTexture)
                    {
                        GetTextureImport(access.ResourceId).Texture.ResourceOwner.RecordGraphAccess(access);
                    }
                    else
                    {
                        var import = GetBufferImport(access.ResourceId);

                        if (!import.IsTransient)
                        {
                            import.Buffer.ResourceOwner.RecordGraphAccess(access);
                        }
                    }
                }
            }
        }
    }

    internal void RecordSubmittedQueueFence(long fenceValue)
    {
        foreach (var commandList in RecordedPassCommandLists)
        {
            commandList.RecordSubmittedQueueFence(fenceValue);
        }
    }

    internal void RetainRecordingForUncertainSubmission()
    {
        var previousState = Interlocked.CompareExchange(ref _recordingOwnershipState, 2, 0);
        ObjectDisposedException.ThrowIf(previousState == 1, this);

        foreach (var commandList in RecordedPassCommandLists)
        {
            commandList.RetainForUncertainSubmission();
        }
    }

    internal void SetPresentationSequence(long presentationSequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(presentationSequence);

        if (SurfaceTarget is null)
        {
            throw new InvalidOperationException("An offscreen graph cannot report a presentation sequence.");
        }

        if (PresentationSequence.HasValue)
        {
            throw new InvalidOperationException("A Surface graph submission cannot present more than once.");
        }

        PresentationSequence = presentationSequence;
    }

    internal bool TryGetBufferAliasingAfterIndex(ulong resourceId, out int index)
    {
        index = FindFirstBufferAliasingAfterIndex(resourceId);
        return index >= 0;
    }

    internal bool TryGetIndexedBufferImport(
        ulong resourceId,
        [NotNullWhen(true)] out GraphBufferImportRecord? import)
    {
        var index = FindFirstBufferImportIndex(resourceId);

        if (index < 0)
        {
            import = null;
            return false;
        }

        import = BufferImports[index];
        return true;
    }

    internal bool TryGetIndexedTextureImport(
        ulong resourceId,
        [NotNullWhen(true)] out GraphTextureImportRecord? import)
    {
        var index = FindFirstTransientTextureImportIndex(resourceId);

        if (index < 0)
        {
            import = null;
            return false;
        }

        import = TextureImports[index];
        return true;
    }

    internal bool TryGetOrderedPassIndex(RenderPassId passId, out int index)
    {
        index = FindFirstOrderedPassIndex(passId);
        return index >= 0;
    }

    internal bool TryGetTextureAliasingAfterIndex(ulong resourceId, out int index)
    {
        index = FindFirstTextureAliasingAfterIndex(resourceId);
        return index >= 0;
    }

    internal void ValidateNativeRasterSubmission()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _recordingOwnershipState) != 0, this);
        var bufferImportFlags = new ushort[BufferImports.Length];
        var textureImportFlags = new ushort[TextureImports.Length];
        var passValidationFlags = new byte[Passes.Length];
        var accessValidationFlags = new byte[Accesses.Length];

        if ((TextureImports.Length < 1)
            || !ReferenceEquals(TextureImports[0].Texture, OutputColorTarget))
        {
            throw new NotSupportedException("Native graph submission requires one graph output target as the first texture import.");
        }

        var targetResourceId = TextureImports[0].Version.ResourceId;

        if (PresentationSource.ResourceId != targetResourceId)
        {
            throw new NotSupportedException("Native graph submission requires the selected terminal color version to share its output texture.");
        }

        if (SurfaceTarget is Surface surface)
        {
            var surfaceSnapshot = surface.CaptureSnapshot();
            var sourceDescription = PresentationSource.Description;

            if (surface.IsRetired
                || surface.IsQuarantined
                || (surface.Device.DeviceEpoch != DeviceEpoch)
                || (surface.Device.Status != GraphicsDeviceStatus.Ready)
                || (surfaceSnapshot.Capabilities.SurfaceEpoch != SurfaceEpoch)
                || (surfaceSnapshot.Capabilities.SwapchainEpoch != SwapchainEpoch)
                || (surfaceSnapshot.ClientPixelSize.Width != sourceDescription.Width)
                || (surfaceSnapshot.ClientPixelSize.Height != sourceDescription.Height)
                || !SurfaceFormatCompatibility.IsSrgbPresentationSourceCompatible(
                    sourceDescription.Format,
                    surfaceSnapshot.ColorFormat)
                || ((sourceDescription.Usage & (TextureUsage.ColorAttachment | TextureUsage.CopySource))
                    != (TextureUsage.ColorAttachment | TextureUsage.CopySource)))
            {
                throw new InvalidOperationException("The graph output texture no longer matches the active sRGB Surface generation.");
            }
        }

        var uniformBufferImportCount = 0;
        var vertexBufferImportCount = 0;
        var indexBufferImportCount = 0;
        var storageBufferImportCount = 0;
        var copySourceBufferImportCount = 0;
        var copyDestinationBufferImportCount = 0;
        var copySourceTextureImportCount = 0;
        var copyDestinationTextureImportCount = 0;
        var colorTextureImportCount = 0;
        var depthTextureImportCount = 0;
        var sampledTextureImportCount = 0;
        var storageTextureImportCount = 0;

        for (var index = 0; index < OrderedPasses.Length; index++)
        {
            if (FindFirstOrderedPassIndex(OrderedPasses[index]) < index)
            {
                throw new ArgumentException("An item with the same key has already been added.");
            }
        }

        for (var importIndex = 0; importIndex < BufferImports.Length; importIndex++)
        {
            var import = BufferImports[importIndex];

            if (FindFirstBufferImportIndex(import.Version.ResourceId) < importIndex)
            {
                throw new NotSupportedException("Native graph submission requires one import per graph buffer resource.");
            }

            var usage = import.Version.Description.Usage;
            var hasUniformUsage = (usage & BufferUsage.Uniform) != 0;
            var hasStorageUsage = (usage & BufferUsage.Storage) != 0;
            var hasVertexReadImportAccess = false;
            var hasIndexReadImportAccess = false;
            var hasCopySourceImportAccess = false;
            var hasCopyDestinationImportAccess = false;

            foreach (var access in Accesses)
            {
                if (access.IsTexture || (access.ResourceId != import.Version.ResourceId))
                {
                    continue;
                }

                hasCopySourceImportAccess |= (access.RequiredState == GraphResourceState.CopySource) && !access.IsWrite;
                hasCopyDestinationImportAccess |= (access.RequiredState == GraphResourceState.CopyDestination) && access.IsWrite;
                hasVertexReadImportAccess |= (access.RequiredState == GraphResourceState.VertexRead) && !access.IsWrite;
                hasIndexReadImportAccess |= (access.RequiredState == GraphResourceState.IndexRead) && !access.IsWrite;
            }

            if (hasVertexReadImportAccess)
            {
                if ((usage & BufferUsage.Vertex) == 0)
                {
                    throw new NotSupportedException("Native graph vertex reads require each imported vertex resource to have vertex usage and one import.");
                }

                bufferImportFlags[importIndex] |= BufferVertexImportFlag;
                vertexBufferImportCount++;
            }

            if (hasIndexReadImportAccess)
            {
                if ((usage & BufferUsage.Index) == 0)
                {
                    throw new NotSupportedException("Native graph index reads require each imported index resource to have index usage and one import.");
                }

                bufferImportFlags[importIndex] |= BufferIndexImportFlag;
                indexBufferImportCount++;
            }

            if (hasUniformUsage || hasStorageUsage)
            {
                if (hasUniformUsage == hasStorageUsage)
                {
                    throw new NotSupportedException("Native graph submission supports separately imported uniform and storage buffers.");
                }

                if (hasUniformUsage)
                {
                    bufferImportFlags[importIndex] |= BufferUniformImportFlag;
                    uniformBufferImportCount++;
                }
                else
                {
                    bufferImportFlags[importIndex] |= BufferStorageImportFlag;
                    storageBufferImportCount++;
                }

                if ((hasCopySourceImportAccess || hasCopyDestinationImportAccess) && !hasStorageUsage)
                {
                    throw new NotSupportedException("Native graph copy buffers with additional usages must also be storage buffers.");
                }
            }

            if (hasCopySourceImportAccess)
            {
                if ((usage & BufferUsage.CopySource) == 0)
                {
                    throw new NotSupportedException("Native graph submission requires each copy-source resource to have copy-source usage and one import.");
                }

                bufferImportFlags[importIndex] |= BufferCopySourceImportFlag;
                copySourceBufferImportCount++;
            }

            if (hasCopyDestinationImportAccess)
            {
                if ((usage & BufferUsage.CopyDestination) == 0)
                {
                    throw new NotSupportedException("Native graph submission requires each copy-destination resource to have copy-destination usage and one import.");
                }

                bufferImportFlags[importIndex] |= BufferCopyDestinationImportFlag;
                copyDestinationBufferImportCount++;
            }

            if (!hasUniformUsage
                && !hasStorageUsage
                && !hasVertexReadImportAccess
                && !hasIndexReadImportAccess
                && !hasCopySourceImportAccess
                && !hasCopyDestinationImportAccess)
            {
                throw new NotSupportedException("Native graph submission requires each imported buffer to have a supported declared usage.");
            }
        }

        for (var importIndex = 0; importIndex < TextureImports.Length; importIndex++)
        {
            var import = TextureImports[importIndex];

            if (import.IsTransient
                && (FindFirstTransientTextureImportIndex(import.Version.ResourceId) < importIndex))
            {
                throw new NotSupportedException("Native graph submission requires one transient import per graph texture resource.");
            }
        }

        var outputTargetCopySourceAccess = false;

        foreach (var access in Accesses)
        {
            if (access.IsTexture
                && (access.ResourceId == targetResourceId)
                && (access.RequiredState == GraphResourceState.CopySource)
                && !access.IsWrite)
            {
                if (!access.ContentsValid)
                {
                    throw new NotSupportedException("The graph output color target must have initialized contents before serving as a graph copy source.");
                }

                outputTargetCopySourceAccess = true;
            }
        }

        if (outputTargetCopySourceAccess)
        {
            var outputTargetImport = TextureImports[0];

            if (((outputTargetImport.Version.Description.Usage & TextureUsage.CopySource) == 0)
                || !TryAddTextureImportFlag(
                    textureImportFlags,
                    0,
                    targetResourceId,
                    TextureCopySourceImportFlag))
            {
                throw new NotSupportedException("The graph output color target requires copy-source usage before it can be copied.");
            }

            copySourceTextureImportCount++;
        }

        for (var aliasingBarrierIndex = 0; aliasingBarrierIndex < AliasingBarriers.Length; aliasingBarrierIndex++)
        {
            var aliasingBarrier = AliasingBarriers[aliasingBarrierIndex];

            if (!TryGetIndexedBufferImport(aliasingBarrier.BeforeResourceId, out var beforeImport)
                || !TryGetIndexedBufferImport(aliasingBarrier.AfterResourceId, out var afterImport)
                || !beforeImport.IsTransient
                || !afterImport.IsTransient
                || !TryGetOrderedPassIndex(aliasingBarrier.PassId, out var aliasPassIndex)
                || !TryGetBufferAliasingAfterIndex(aliasingBarrier.AfterResourceId, out var originalAliasingBarrierIndex)
                || (originalAliasingBarrierIndex < aliasingBarrierIndex))
            {
                throw new InvalidOperationException("A transient aliasing barrier does not match distinct materialized graph buffers and a scheduled pass.");
            }

            var beforeLastPassIndex = -1;
            var afterFirstPassIndex = int.MaxValue;

            foreach (var access in Accesses)
            {
                if (access.IsTexture)
                {
                    continue;
                }

                if (access.ResourceId == aliasingBarrier.BeforeResourceId)
                {
                    beforeLastPassIndex = Math.Max(beforeLastPassIndex, GetOrderedPassIndex(access.PassId));
                }

                if (access.ResourceId == aliasingBarrier.AfterResourceId)
                {
                    afterFirstPassIndex = Math.Min(afterFirstPassIndex, GetOrderedPassIndex(access.PassId));
                }
            }

            if ((beforeLastPassIndex >= afterFirstPassIndex)
                || (afterFirstPassIndex != aliasPassIndex))
            {
                throw new InvalidOperationException("Transient alias lifetimes overlap or the alias barrier is not at the new resource's first pass.");
            }
        }

        for (var importIndex = 0; importIndex < BufferImports.Length; importIndex++)
        {
            if ((bufferImportFlags[importIndex] & BufferUniformImportFlag) == 0)
            {
                continue;
            }

            var import = BufferImports[importIndex];

            if (!import.InitialContentsValid
                || (import.InitialState is not (GraphResourceState.General or GraphResourceState.UniformRead)))
            {
                throw new NotSupportedException("Native graph submission requires initialized uniform buffers in a supported state.");
            }
        }

        for (var index = 1; index < TextureImports.Length; index++)
        {
            var import = TextureImports[index];
            var usage = import.Version.Description.Usage;
            var hasColorAttachmentUsage = (usage & TextureUsage.ColorAttachment) != 0;
            var hasDepthAttachmentUsage = (usage & TextureUsage.DepthAttachment) != 0;
            var hasSampledUsage = (usage & TextureUsage.Sampled) != 0;
            var hasStorageUsage = (usage & TextureUsage.Storage) != 0;
            var isDepthSampledImage = hasSampledUsage
                && (import.Version.Description.Format == TextureFormat.D32Float)
                && (import.Version.Range.Aspect == TextureAspect.Depth);
            var hasColorAttachmentAccess = false;
            var hasDepthAttachmentAccess = false;
            var hasSampledImportAccess = false;
            var hasCopySourceImportAccess = false;
            var hasCopyDestinationImportAccess = false;

            foreach (var access in Accesses)
            {
                if (!access.IsTexture || (access.ResourceId != import.Version.ResourceId))
                {
                    continue;
                }

                hasColorAttachmentAccess |= (access.RequiredState == GraphResourceState.ColorAttachment) && access.IsWrite;
                hasDepthAttachmentAccess |= access.RequiredState is GraphResourceState.DepthRead or GraphResourceState.DepthWrite;
                hasSampledImportAccess |= (access.RequiredState == GraphResourceState.ShaderSampledRead) && !access.IsWrite;
                hasCopySourceImportAccess |= (access.RequiredState == GraphResourceState.CopySource) && !access.IsWrite;
                hasCopyDestinationImportAccess |= (access.RequiredState == GraphResourceState.CopyDestination) && access.IsWrite;
            }

            if (ReferenceEquals(import.Texture, OutputColorTarget)
                || (import.Version.ResourceId == targetResourceId))
            {
                throw new NotSupportedException("Native graph submission requires distinct non-target imported textures.");
            }

            if (hasColorAttachmentAccess)
            {
                var description = import.Version.Description;

                if (hasCopySourceImportAccess
                    && (((usage & TextureUsage.CopySource) == 0)
                        || (hasCopyDestinationImportAccess && !import.IsTransient)
                        || !TryAddTextureImportFlag(
                            textureImportFlags,
                            index,
                            import.Version.ResourceId,
                            TextureCopySourceImportFlag)))
                {
                    throw new NotSupportedException("Native graph color attachment copy sources require copy-source usage and a supported import.");
                }

                if (hasCopySourceImportAccess)
                {
                    copySourceTextureImportCount++;
                }

                if (hasCopyDestinationImportAccess
                    && (((usage & TextureUsage.CopyDestination) == 0)
                        || !TryAddTextureImportFlag(
                            textureImportFlags,
                            index,
                            import.Version.ResourceId,
                            TextureCopyDestinationImportFlag)))
                {
                    throw new NotSupportedException("Native graph color attachment copy destinations require copy-destination usage.");
                }

                if (hasCopyDestinationImportAccess)
                {
                    copyDestinationTextureImportCount++;
                }

                if (!hasColorAttachmentUsage
                    || hasDepthAttachmentUsage
                    || hasStorageUsage
                    || (description.Dimension != TextureDimension.Dimension2D)
                    || (description.Depth != 1)
                    || (import.Version.Range.MipCount != 1)
                    || (import.Version.Range.LayerCount != 1)
                    || (import.Version.Range.Aspect != TextureAspect.Color)
                    || (import.InitialContentsValid && (import.InitialState == GraphResourceState.Undefined))
                    || (import.InitialState is not (
                        GraphResourceState.Undefined
                        or GraphResourceState.General
                        or GraphResourceState.CopySource
                        or GraphResourceState.CopyDestination
                        or GraphResourceState.ColorAttachment))
                    || !TryAddTextureImportFlag(
                        textureImportFlags,
                        index,
                        import.Version.ResourceId,
                        TextureColorImportFlag))
                {
                    throw new NotSupportedException("Native graph color attachments require valid 2D color imports in a supported state.");
                }

                colorTextureImportCount++;

                if (hasSampledImportAccess)
                {
                    if (!hasSampledUsage
                        || hasStorageUsage
                        || isDepthSampledImage
                        || !import.IsTransient
                        || import.InitialContentsValid
                        || (import.InitialState != GraphResourceState.Undefined)
                        || !TryAddTextureImportFlag(
                            textureImportFlags,
                            index,
                            import.Version.ResourceId,
                            TextureSampledImportFlag))
                    {
                        throw new NotSupportedException("Native graph color attachments may be sampled later only as graph transients written before sampling.");
                    }

                    sampledTextureImportCount++;
                }

                continue;
            }

            if (hasDepthAttachmentUsage && hasDepthAttachmentAccess)
            {
                var description = import.Version.Description;

                if ((description.Dimension != TextureDimension.Dimension2D)
                    || (description.Depth != 1)
                    || (description.Format != TextureFormat.D32Float)
                    || (import.Version.Range.MipCount != 1)
                    || (import.Version.Range.LayerCount != 1)
                    || (import.Version.Range.Aspect != TextureAspect.Depth)
                    || hasStorageUsage
                    || hasCopySourceImportAccess
                    || hasCopyDestinationImportAccess
                    || (import.InitialContentsValid && (import.InitialState == GraphResourceState.Undefined))
                    || (import.InitialState is not (
                        GraphResourceState.Undefined
                        or GraphResourceState.General
                        or GraphResourceState.DepthRead
                        or GraphResourceState.DepthWrite
                        or GraphResourceState.ShaderSampledRead))
                    || !TryAddTextureImportFlag(
                        textureImportFlags,
                        index,
                        import.Version.ResourceId,
                        TextureDepthImportFlag))
                {
                    throw new NotSupportedException("Native graph depth attachments require D32Float mip/layer selections in a supported state.");
                }

                depthTextureImportCount++;

                if (hasSampledImportAccess)
                {
                    if (!hasSampledUsage
                        || hasStorageUsage
                        || !isDepthSampledImage
                        || !import.IsTransient
                        || import.InitialContentsValid
                        || (import.InitialState != GraphResourceState.Undefined)
                        || !TryAddTextureImportFlag(
                            textureImportFlags,
                            index,
                            import.Version.ResourceId,
                            TextureSampledImportFlag))
                    {
                        throw new NotSupportedException("Native graph depth attachments may be sampled later only as graph transients written before sampling.");
                    }

                    sampledTextureImportCount++;
                }

                continue;
            }

            if (((import.Version.Description.Dimension != TextureDimension.Dimension2D)
                    && !(import.Version.Description.Dimension == TextureDimension.Dimension3D
                        && hasStorageUsage
                        && !hasSampledUsage))
                || ((import.Version.Range.MipCount != 1) && !(hasSampledUsage || hasStorageUsage))
                || ((import.Version.Range.LayerCount != 1) && !hasSampledUsage)
                || ((import.Version.Range.Aspect != TextureAspect.Color) && !isDepthSampledImage)
                || ((import.Version.Description.Format == TextureFormat.D32Float) && !isDepthSampledImage))
            {
                throw new NotSupportedException("Native graph submission requires distinct non-target 2D color or D32 depth sampled ranges, or storage-only 3D ranges with single-layer selections.");
            }

            if (hasSampledUsage || hasStorageUsage)
            {
                var supportedSubresourceRange = hasSampledUsage
                    ? (import.Version.Description.Dimension == TextureDimension.Dimension2D)
                        && ((import.Version.Range.Aspect == TextureAspect.Color) || isDepthSampledImage)
                    : (import.Version.Description.Dimension is TextureDimension.Dimension2D or TextureDimension.Dimension3D)
                        && (import.Version.Description.ArrayLayers == 1)
                        && (import.Version.Range.Aspect == TextureAspect.Color)
                        && (import.Version.Range.LayerCount == 1);

                if (hasSampledUsage == hasStorageUsage
                    || !supportedSubresourceRange
                    || hasCopySourceImportAccess
                    || hasCopyDestinationImportAccess)
                {
                    throw new NotSupportedException("Native graph submission supports 2D sampled images, including arrayed views, and non-arrayed 2D/3D storage images.");
                }

                if (hasSampledUsage)
                {
                    if (!import.InitialContentsValid
                        || (import.InitialState is not (GraphResourceState.General or GraphResourceState.ShaderSampledRead)))
                    {
                        throw new NotSupportedException("Native graph submission requires initialized sampled images.");
                    }

                    if (!TryAddTextureImportFlag(
                            textureImportFlags,
                            index,
                            import.Version.ResourceId,
                            TextureSampledImportFlag))
                    {
                        throw new InvalidOperationException("The graph imported a duplicate sampled-image resource identity.");
                    }

                    sampledTextureImportCount++;
                }
                else
                {
                    if ((import.InitialContentsValid && import.InitialState == GraphResourceState.Undefined)
                        || (import.InitialState is not (
                            GraphResourceState.Undefined
                            or GraphResourceState.General
                            or GraphResourceState.StorageWrite
                            or GraphResourceState.StorageReadWrite)))
                    {
                        throw new NotSupportedException("Native graph submission supports storage images in an initialized or undefined state.");
                    }

                    if (!TryAddTextureImportFlag(
                            textureImportFlags,
                            index,
                            import.Version.ResourceId,
                            TextureStorageImportFlag))
                    {
                        throw new InvalidOperationException("The graph imported a duplicate storage-image resource identity.");
                    }

                    storageTextureImportCount++;
                }
            }
            else
            {
                if ((!hasCopySourceImportAccess && !hasCopyDestinationImportAccess)
                    || (hasCopySourceImportAccess && hasCopyDestinationImportAccess && !import.IsTransient))
                {
                    throw new NotSupportedException("Native graph copy textures require declared copy-source or copy-destination uses; a texture may fill both roles only when transient.");
                }

                if (hasCopySourceImportAccess)
                {
                    if (((usage & TextureUsage.CopySource) == 0)
                        || (import.IsTransient
                            ? (import.InitialContentsValid || (import.InitialState != GraphResourceState.Undefined))
                            : (!import.InitialContentsValid
                                || (import.InitialState is not (
                                    GraphResourceState.General
                                    or GraphResourceState.CopySource
                                    or GraphResourceState.CopyDestination
                                    or GraphResourceState.ColorAttachment
                                    or GraphResourceState.ShaderSampledRead
                                    or GraphResourceState.StorageRead
                                    or GraphResourceState.StorageWrite
                                    or GraphResourceState.StorageReadWrite))))
                        || !TryAddTextureImportFlag(
                            textureImportFlags,
                            index,
                            import.Version.ResourceId,
                            TextureCopySourceImportFlag))
                    {
                        throw new NotSupportedException("Native graph texture copy sources require initialized imports or transient textures written earlier in the graph.");
                    }

                    copySourceTextureImportCount++;
                }

                if (hasCopyDestinationImportAccess)
                {
                    if (((usage & TextureUsage.CopyDestination) == 0)
                        || (import.IsTransient
                            ? (import.InitialContentsValid || (import.InitialState != GraphResourceState.Undefined))
                            : ((import.InitialContentsValid && (import.InitialState == GraphResourceState.Undefined))
                                || (import.InitialState is not (
                                    GraphResourceState.Undefined
                                    or GraphResourceState.General
                                    or GraphResourceState.CopySource
                                    or GraphResourceState.CopyDestination
                                    or GraphResourceState.ColorAttachment
                                    or GraphResourceState.ShaderSampledRead
                                    or GraphResourceState.StorageRead
                                    or GraphResourceState.StorageWrite
                                    or GraphResourceState.StorageReadWrite))))
                        || !TryAddTextureImportFlag(
                            textureImportFlags,
                            index,
                            import.Version.ResourceId,
                            TextureCopyDestinationImportFlag))
                    {
                        throw new NotSupportedException("Native graph texture copy destinations require CopyDestination resources in supported initial states.");
                    }

                    copyDestinationTextureImportCount++;
                }
            }
        }

        for (var aliasingBarrierIndex = 0; aliasingBarrierIndex < TextureAliasingBarriers.Length; aliasingBarrierIndex++)
        {
            var aliasingBarrier = TextureAliasingBarriers[aliasingBarrierIndex];

            if (!TryGetIndexedTextureImport(aliasingBarrier.BeforeResourceId, out var beforeImport)
                || !TryGetIndexedTextureImport(aliasingBarrier.AfterResourceId, out var afterImport)
                || !beforeImport.IsTransient
                || !afterImport.IsTransient
                || (aliasingBarrier.DeviceEpoch != DeviceEpoch)
                || !TryGetOrderedPassIndex(aliasingBarrier.PassId, out var aliasPassIndex)
                || !TryGetTextureAliasingAfterIndex(aliasingBarrier.AfterResourceId, out var originalAliasingBarrierIndex)
                || (originalAliasingBarrierIndex < aliasingBarrierIndex))
            {
                throw new InvalidOperationException("A transient texture aliasing barrier does not match distinct materialized graph textures and a scheduled pass.");
            }

            var beforeLastPassIndex = -1;
            var afterFirstPassIndex = int.MaxValue;

            foreach (var access in Accesses)
            {
                if (!access.IsTexture)
                {
                    continue;
                }

                if (access.ResourceId == aliasingBarrier.BeforeResourceId)
                {
                    beforeLastPassIndex = Math.Max(beforeLastPassIndex, GetOrderedPassIndex(access.PassId));
                }

                if (access.ResourceId == aliasingBarrier.AfterResourceId)
                {
                    afterFirstPassIndex = Math.Min(afterFirstPassIndex, GetOrderedPassIndex(access.PassId));
                }
            }

            if ((beforeLastPassIndex >= afterFirstPassIndex)
                || (afterFirstPassIndex != aliasPassIndex))
            {
                throw new InvalidOperationException("Transient texture alias lifetimes overlap or the alias barrier is not at the new resource's first pass.");
            }
        }

        for (var importIndex = 0; importIndex < BufferImports.Length; importIndex++)
        {
            if ((bufferImportFlags[importIndex] & BufferStorageImportFlag) == 0)
            {
                continue;
            }

            var import = BufferImports[importIndex];

            if ((import.InitialContentsValid && (import.InitialState == GraphResourceState.Undefined))
                || (import.InitialState is not (
                    GraphResourceState.Undefined
                    or GraphResourceState.General
                    or GraphResourceState.CopySource
                    or GraphResourceState.CopyDestination
                    or GraphResourceState.StorageWrite
                    or GraphResourceState.StorageReadWrite)))
            {
                throw new NotSupportedException("Native graph submission requires storage buffers in supported initial states.");
            }
        }

        for (var importIndex = 0; importIndex < BufferImports.Length; importIndex++)
        {
            if ((bufferImportFlags[importIndex] & BufferVertexImportFlag) == 0)
            {
                continue;
            }

            var import = BufferImports[importIndex];

            if ((import.Version.Range != new GraphBufferRange(0, import.Buffer.ByteLength))
                || !import.InitialContentsValid
                || (import.InitialState is not (
                    GraphResourceState.General
                    or GraphResourceState.CopySource
                    or GraphResourceState.CopyDestination
                    or GraphResourceState.VertexRead)))
            {
                throw new NotSupportedException("Native graph submission requires full-range imported vertex buffers with valid contents and a supported initial state.");
            }
        }

        for (var importIndex = 0; importIndex < BufferImports.Length; importIndex++)
        {
            if ((bufferImportFlags[importIndex] & BufferIndexImportFlag) == 0)
            {
                continue;
            }

            var import = BufferImports[importIndex];

            if ((import.Version.Range != new GraphBufferRange(0, import.Buffer.ByteLength))
                || !import.InitialContentsValid
                || (import.InitialState is not (
                    GraphResourceState.General
                    or GraphResourceState.CopySource
                    or GraphResourceState.CopyDestination
                    or GraphResourceState.IndexRead)))
            {
                throw new NotSupportedException("Native graph submission requires full-range imported index buffers with valid contents and a supported initial state.");
            }
        }

        for (var importIndex = 0; importIndex < BufferImports.Length; importIndex++)
        {
            if ((bufferImportFlags[importIndex] & BufferCopySourceImportFlag) == 0)
            {
                continue;
            }

            var import = BufferImports[importIndex];

            if (import.IsTransient
                    ? (import.InitialContentsValid || (import.InitialState != GraphResourceState.Undefined))
                    : (!import.InitialContentsValid
                        || (import.InitialState is not (
                            GraphResourceState.General
                            or GraphResourceState.CopySource
                            or GraphResourceState.CopyDestination
                            or GraphResourceState.StorageRead
                            or GraphResourceState.StorageWrite
                            or GraphResourceState.StorageReadWrite))))
            {
                throw new NotSupportedException("Native graph submission requires copy-source buffers with valid imported or transient state.");
            }
        }

        for (var importIndex = 0; importIndex < BufferImports.Length; importIndex++)
        {
            if ((bufferImportFlags[importIndex] & BufferCopyDestinationImportFlag) == 0)
            {
                continue;
            }

            var import = BufferImports[importIndex];

            if ((import.InitialContentsValid && (import.InitialState == GraphResourceState.Undefined))
                || (import.InitialState is not (
                    GraphResourceState.Undefined
                    or GraphResourceState.General
                    or GraphResourceState.CopySource
                    or GraphResourceState.CopyDestination
                    or GraphResourceState.StorageRead
                    or GraphResourceState.StorageWrite
                    or GraphResourceState.StorageReadWrite)))
            {
                throw new NotSupportedException("Native graph submission requires copy-destination buffers with a supported initial state.");
            }
        }

        var hasOutputColorAttachment = false;
        var hasVertexBufferPipeline = false;
        var hasVertexBufferBinding = false;
        var hasIndexBufferBinding = false;
        var hasUniformBufferPipeline = false;
        var hasUniformBufferBinding = false;
        var hasUniformBufferAccess = false;
        var hasSampledImagePipeline = false;
        var hasSampledImageBinding = false;
        var hasSampledImageAccess = false;
        var hasSamplerPipeline = false;
        var hasSamplerBinding = false;
        var hasStorageBufferPipeline = false;
        var hasStorageBufferBinding = false;
        var hasStorageBufferAccess = false;
        var hasStorageImagePipeline = false;
        var hasStorageImageBinding = false;
        var hasStorageImageAccess = false;
        var copySourceResourcesUsedCount = 0;
        var copyDestinationResourcesUsedCount = 0;
        var vertexBufferResourcesUsedCount = 0;
        var indexBufferResourcesUsedCount = 0;
        var uniformBufferResourcesUsedCount = 0;
        var sampledTextureResourcesUsedCount = 0;
        var colorTextureResourcesUsedCount = 0;
        var depthTextureResourcesUsedCount = 0;
        var storageBufferResourcesUsedCount = 0;
        var storageTextureResourcesUsedCount = 0;
        var copySourceTextureResourcesUsedCount = 0;
        var copyDestinationTextureResourcesUsedCount = 0;

        foreach (var pass in Passes)
        {
            var commandReadState = 0;
            using var commandRead = pass.ReadCommands(ref commandReadState);
            var commands = commandRead.Commands;
            var diagnosticRecipe = commandRead.DiagnosticRecipe;

            if (diagnosticRecipe is not null)
            {
                if (pass.Kind != CommandRecorderPassKind.Compute)
                {
                    throw new InvalidOperationException("The skinned-normal diagnostic witness must belong to a compute pass.");
                }

                diagnosticRecipe.ValidateNativeSubmission(pass.PassId, DeviceEpoch, Accesses, in commandRead);
            }

            TextureFormat? passDepthTargetFormat = null;

            if (pass.Kind == CommandRecorderPassKind.Presentation)
            {
                if (!commands.IsEmpty || !pass.Attachments.IsEmpty)
                {
                    throw new InvalidOperationException("A synthetic terminal-output pass cannot record commands or attachments.");
                }

                if (SurfaceTarget is not null)
                {
                    var hasPresentationSourceRead = false;

                    foreach (var access in Accesses)
                    {
                        hasPresentationSourceRead |= (access.PassId == pass.PassId)
                            && access.IsTexture
                            && (access.ResourceId == targetResourceId)
                            && ReferenceEquals(access.TextureVersion, PresentationSource)
                            && (access.TextureRange == new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color))
                            && (access.RequiredState == GraphResourceState.CopySource)
                            && !access.IsWrite;
                    }

                    if (!hasPresentationSourceRead)
                    {
                        throw new InvalidOperationException("A Surface terminal pass must read its selected source for presentation.");
                    }

                    SetPassValidationFlag(
                        passValidationFlags,
                        Passes,
                        _passValidationIndex,
                        pass.PassId,
                        TextureCopySourcePassFlag);
                }

                continue;
            }

            if (pass.Kind == CommandRecorderPassKind.Copy)
            {
                if (!pass.Attachments.IsEmpty)
                {
                    throw new NotSupportedException("A native graph copy pass cannot declare raster attachments.");
                }
            }
            else if (pass.Kind == CommandRecorderPassKind.Compute)
            {
                if (!pass.Attachments.IsEmpty)
                {
                    throw new NotSupportedException("A native graph compute pass cannot declare raster attachments.");
                }
            }
            else if ((pass.Kind != CommandRecorderPassKind.Raster) || pass.Attachments.IsEmpty)
            {
                throw new NotSupportedException("Native graph submission requires a raster pass with at least one attachment.");
            }
            else
            {
                var colorAttachmentCount = 0;
                var depthAttachmentCount = 0;
                var passRenderArea = pass.RenderArea
                    ?? new RenderArea(
                        0,
                        0,
                        OutputColorTarget.Description.Width,
                        OutputColorTarget.Description.Height);

                for (var attachmentIndex = 0; attachmentIndex < pass.Attachments.Length; attachmentIndex++)
                {
                    var attachment = pass.Attachments[attachmentIndex];
                    var attachmentDescription = attachment.Version.Description;

                    if ((passRenderArea.Width > attachmentDescription.Width)
                        || (passRenderArea.X > (attachmentDescription.Width - passRenderArea.Width))
                        || (passRenderArea.Height > attachmentDescription.Height)
                        || (passRenderArea.Y > (attachmentDescription.Height - passRenderArea.Height)))
                    {
                        throw new NotSupportedException("Native graph render areas must fit every raster attachment.");
                    }

                    if (attachment.IsDepth)
                    {
                        depthAttachmentCount++;
                        var description = attachment.Version.Description;

                        if ((depthAttachmentCount != 1)
                            || (attachment.Version.ResourceId == targetResourceId)
                            || !HasTextureImportFlag(
                                textureImportFlags,
                                attachment.Version.ResourceId,
                                TextureDepthImportFlag)
                            || (attachment.Range != new GraphTextureRange(0, 1, 0, 1, TextureAspect.Depth))
                            || (description.Dimension != TextureDimension.Dimension2D)
                            || (description.Depth != 1)
                            || (description.Format != TextureFormat.D32Float)
                            || ((description.Usage & TextureUsage.DepthAttachment) == 0))
                        {
                            throw new NotSupportedException("Native graph submission requires a mip-0/layer-0 D32Float depth attachment in a supported state.");
                        }

                        passDepthTargetFormat = description.Format;
                    }
                    else
                    {
                        colorAttachmentCount++;
                        var description = attachment.Version.Description;
                        var resourceId = attachment.Version.ResourceId;
                        var isOutputTarget = resourceId == targetResourceId;
                        var isDuplicateColorAttachment = false;

                        for (var previousAttachmentIndex = 0;
                            previousAttachmentIndex < attachmentIndex;
                            previousAttachmentIndex++)
                        {
                            var previousAttachment = pass.Attachments[previousAttachmentIndex];
                            isDuplicateColorAttachment |= !previousAttachment.IsDepth
                                && (previousAttachment.Version.ResourceId == resourceId);
                        }

                        if ((colorAttachmentCount > Math.Min(
                                OutputColorTarget.ResourceOwner.Device.Capabilities.Limits.MaximumColorAttachments,
                                MaximumNativeColorAttachmentCount))
                            || isDuplicateColorAttachment
                            || (!isOutputTarget
                                && !HasTextureImportFlag(textureImportFlags, resourceId, TextureColorImportFlag))
                            || (attachment.Range != new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color))
                            || (description.Dimension != TextureDimension.Dimension2D)
                            || (description.Depth != 1)
                            || ((description.Usage & TextureUsage.ColorAttachment) == 0))
                        {
                            throw new NotSupportedException("Native graph color attachments must be distinct mip-0/layer-0 color textures within the device attachment limit.");
                        }

                        hasOutputColorAttachment |= isOutputTarget;
                    }
                }

                if ((colorAttachmentCount == 0) && (depthAttachmentCount == 0))
                {
                    throw new NotSupportedException("Native graph raster passes require at least one color or depth attachment.");
                }
            }

            GpuPipeline? currentPipeline = null;
            GraphBufferImportRecord? currentVertexBufferImport = null;
            GraphBufferImportRecord? currentIndexBufferImport = null;
            var currentPipelineRequiresVertexBuffer = false;
            var currentPipelineRequiresUniformBuffer = false;
            var currentPipelineUniformBufferBindingCount = 0;
            var currentPipelineRequiresSampledImage = false;
            var currentPipelineRequiresSampler = false;
            var currentPipelineSampledImageBindingCount = 0;
            var currentPipelineSamplerBindingCount = 0;
            var currentPipelineRequiresStorageBuffer = false;
            var currentPipelineStorageBufferBindingCount = 0;
            var currentPipelineRequiresStorageImage = false;
            var currentPipelineStorageImageBindingCount = 0;
            var currentPipelineBindingsSet = false;
            var hasDraw = false;
            var hasDispatch = false;
            var hasBufferCopy = false;
            var hasTextureCopy = false;

            foreach (var command in commands)
            {
                switch (command.Kind)
                {
                    case GraphicsCommandKind.SetPipeline:
                    {
                        if (pass.Kind is not (CommandRecorderPassKind.Raster or CommandRecorderPassKind.Compute))
                        {
                            throw new NotSupportedException("Native graph copy passes cannot bind pipelines.");
                        }

                        var pipeline = command.PipelineOwner
                            ?? throw new InvalidOperationException("A recorded pipeline command lost its pipeline owner.");
                        var layout = pipeline.BindingLayout;
                        var isComputePass = pass.Kind == CommandRecorderPassKind.Compute;

                        if ((pipeline.DeviceEpoch != DeviceEpoch)
                            || !ReferenceEquals(pipeline.ResourceOwner.Device, OutputColorTarget.ResourceOwner.Device)
                            || (layout.IsCompute != isComputePass)
                            || (isComputePass && ((layout.VertexStride != 0) || !layout.VertexElements.IsEmpty)))
                        {
                            throw new NotSupportedException("Native graph submission requires a same-device pipeline matching the pass kind.");
                        }

                        var colorTargetFormatsMatch = MatchesColorTargetFormats(layout.ColorTargetFormats, pass.Attachments);

                        if (isComputePass
                            ? (layout.DepthTargetFormat.HasValue || !layout.ColorTargetFormats.IsEmpty)
                            : ((layout.DepthTargetFormat != passDepthTargetFormat)
                                || (layout.Topology != PrimitiveTopology.TriangleList)
                                || !colorTargetFormatsMatch))
                        {
                            throw new NotSupportedException("Native graph submission requires attachment-free compute pipelines and triangle-list graphics pipelines matching its color and optional depth attachments.");
                        }

                        currentPipelineRequiresVertexBuffer = !isComputePass && (layout.VertexStride != 0);
                        currentVertexBufferImport = null;
                        currentIndexBufferImport = null;
                        currentPipelineUniformBufferBindingCount = 0;
                        currentPipelineSampledImageBindingCount = 0;
                        currentPipelineSamplerBindingCount = 0;
                        currentPipelineStorageBufferBindingCount = 0;
                        currentPipelineStorageImageBindingCount = 0;

                        foreach (var reflection in layout.ShaderReflections)
                        {
                            var supportedStage = isComputePass
                                ? reflection.Stage == ShaderStage.Compute
                                : reflection.Stage is ShaderStage.Vertex or ShaderStage.Pixel;

                            foreach (var resource in reflection.Resources)
                            {
                                var supportedKind = isComputePass
                                    ? resource.Kind is ShaderResourceKind.StorageBuffer or ShaderResourceKind.StorageImage
                                    : resource.Kind is ShaderResourceKind.UniformBuffer
                                        or ShaderResourceKind.SampledImage
                                        or ShaderResourceKind.Sampler
                                        or ShaderResourceKind.StorageBuffer
                                        or ShaderResourceKind.StorageImage;
                                var isWriteOnlyComputeBuffer = isComputePass
                                    && (resource.Kind == ShaderResourceKind.StorageBuffer)
                                    && (resource.Access == ShaderResourceAccess.Write);

                                if ((isComputePass && (resource.Group != ShaderBindingGroup.Frame))
                                    || !supportedKind
                                    || (resource.ArrayCount != 1)
                                    || ((resource.Kind is ShaderResourceKind.UniformBuffer
                                            or ShaderResourceKind.SampledImage
                                            or ShaderResourceKind.Sampler)
                                        && (resource.Access != ShaderResourceAccess.Read))
                                    || ((resource.Kind is ShaderResourceKind.StorageBuffer or ShaderResourceKind.StorageImage)
                                        && (resource.Access != ShaderResourceAccess.ReadWrite)
                                        && !isWriteOnlyComputeBuffer)
                                    || !supportedStage)
                                {
                                    throw new NotSupportedException(
                                        "Native graph submission supports all four groups for graphics resources and Frame compute-stage storage bindings.");
                                }
                            }
                        }

                        foreach (var resource in layout.EnumerateDistinctResources())
                        {
                            switch (resource.Kind)
                            {
                                case ShaderResourceKind.UniformBuffer:
                                {
                                    currentPipelineRequiresUniformBuffer = true;

                                    if (HasEarlierPipelineBinding(layout, resource))
                                    {
                                        throw new NotSupportedException("A graphics pipeline cannot declare conflicting uniform-buffer signatures at one binding.");
                                    }

                                    currentPipelineUniformBufferBindingCount++;

                                    break;
                                }

                                case ShaderResourceKind.SampledImage:
                                {
                                    currentPipelineRequiresSampledImage = true;

                                    if (HasEarlierPipelineBinding(layout, resource))
                                    {
                                        throw new NotSupportedException("A graphics pipeline cannot declare conflicting sampled-image signatures at one binding.");
                                    }

                                    currentPipelineSampledImageBindingCount++;

                                    break;
                                }

                                case ShaderResourceKind.Sampler:
                                {
                                    currentPipelineRequiresSampler = true;

                                    if (HasEarlierPipelineBinding(layout, resource))
                                    {
                                        throw new NotSupportedException("A graphics pipeline cannot declare conflicting sampler signatures at one binding.");
                                    }

                                    currentPipelineSamplerBindingCount++;

                                    break;
                                }

                                case ShaderResourceKind.StorageBuffer:
                                {
                                    currentPipelineRequiresStorageBuffer = true;

                                    if (HasEarlierPipelineBinding(layout, resource))
                                    {
                                        throw new NotSupportedException("A pipeline cannot declare conflicting storage-buffer signatures at one group binding.");
                                    }

                                    currentPipelineStorageBufferBindingCount++;

                                    break;
                                }

                                case ShaderResourceKind.StorageImage:
                                {
                                    if (resource.ImageArrayed)
                                    {
                                        throw new NotSupportedException("Native graph submission does not support arrayed storage-image bindings.");
                                    }

                                    currentPipelineRequiresStorageImage = true;

                                    if (HasEarlierPipelineBinding(layout, resource))
                                    {
                                        throw new NotSupportedException("A pipeline cannot declare conflicting storage-image signatures at one group binding.");
                                    }

                                    currentPipelineStorageImageBindingCount++;

                                    break;
                                }
                            }
                        }

                        if ((currentPipelineSampledImageBindingCount != 0)
                            && (currentPipelineSamplerBindingCount == 0))
                        {
                            throw new NotSupportedException("A graphics pipeline with sampled images must declare at least one sampler.");
                        }

                        currentPipeline = pipeline;
                        currentPipelineBindingsSet = false;
                        hasVertexBufferPipeline |= currentPipelineRequiresVertexBuffer;
                        hasUniformBufferPipeline |= currentPipelineRequiresUniformBuffer;
                        hasSampledImagePipeline |= currentPipelineRequiresSampledImage;
                        hasSamplerPipeline |= currentPipelineRequiresSampler;
                        hasStorageBufferPipeline |= currentPipelineRequiresStorageBuffer;
                        hasStorageImagePipeline |= currentPipelineRequiresStorageImage;
                        break;
                    }

                    case GraphicsCommandKind.SetVertexBuffer:
                    {
                        if ((pass.Kind != CommandRecorderPassKind.Raster)
                            || (currentPipeline is null)
                            || !currentPipelineRequiresVertexBuffer)
                        {
                            throw new NotSupportedException("Native graph vertex buffers require a vertex-input raster pipeline.");
                        }

                        var binding = command.VertexBuffer;

                        if ((binding.Slot != 0)
                            || (binding.Stride != currentPipeline.BindingLayout.VertexStride)
                            || !TryGetBufferImport(
                                bufferImportFlags,
                                binding.Buffer.ResourceId,
                                BufferVertexImportFlag,
                                out var vertexBufferImport)
                            || (binding.Buffer.DeviceEpoch != vertexBufferImport.Version.DeviceEpoch)
                            || !ReferenceEquals(binding.Buffer.Description, vertexBufferImport.Version.Description)
                            || !binding.Range.IsWithin(vertexBufferImport.Version.Range))
                        {
                            throw new NotSupportedException("A native graph vertex-buffer binding must match an imported vertex resource and pipeline stride.");
                        }

                        var accessDeclared = false;

                        foreach (var access in Accesses)
                        {
                            if (!access.IsTexture
                                && (access.PassId == pass.PassId)
                                && (access.ResourceId == binding.Buffer.ResourceId)
                                && (access.RequiredState == GraphResourceState.VertexRead)
                                && !access.IsWrite
                                && access.ContentsValid
                                && ReferenceEquals(access.BufferVersion, binding.Buffer)
                                && binding.Range.IsWithin(access.BufferRange))
                            {
                                accessDeclared = true;
                                break;
                            }
                        }

                        if (!accessDeclared)
                        {
                            throw new InvalidOperationException("The graph vertex-buffer binding has no matching declared VertexRead range.");
                        }

                        currentVertexBufferImport = vertexBufferImport;
                        SetAccessValidationFlag(
                            accessValidationFlags,
                            Accesses,
                            _accessPairIndex,
                            pass.PassId,
                            binding.Buffer.ResourceId,
                            BufferVertexBindingFlag);
                        hasVertexBufferBinding = true;
                        break;
                    }

                    case GraphicsCommandKind.SetIndexBuffer:
                    {
                        if ((pass.Kind != CommandRecorderPassKind.Raster)
                            || (currentPipeline is null))
                        {
                            throw new NotSupportedException("Native graph index buffers require a raster pipeline.");
                        }

                        var binding = command.IndexBuffer;
                        var elementSize = binding.Format switch {
                            IndexFormat.UInt16 => sizeof(ushort),
                            IndexFormat.UInt32 => sizeof(uint),
                            _ => throw new ArgumentOutOfRangeException(nameof(command)),
                        };

                        if (!TryGetBufferImport(
                                bufferImportFlags,
                                binding.Buffer.ResourceId,
                                BufferIndexImportFlag,
                                out var indexBufferImport)
                            || !ReferenceEquals(binding.Buffer, indexBufferImport.Version)
                            || !binding.Range.IsWithin(indexBufferImport.Version.Range)
                            || ((binding.Range.ByteOffset % elementSize) != 0)
                            || ((binding.Range.ByteLength % elementSize) != 0))
                        {
                            throw new NotSupportedException("A native graph index-buffer binding must match an imported aligned index resource.");
                        }

                        var accessDeclared = false;

                        foreach (var access in Accesses)
                        {
                            if (!access.IsTexture
                                && (access.PassId == pass.PassId)
                                && (access.ResourceId == binding.Buffer.ResourceId)
                                && (access.RequiredState == GraphResourceState.IndexRead)
                                && !access.IsWrite
                                && access.ContentsValid
                                && ReferenceEquals(access.BufferVersion, binding.Buffer)
                                && binding.Range.IsWithin(access.BufferRange))
                            {
                                accessDeclared = true;
                                break;
                            }
                        }

                        if (!accessDeclared)
                        {
                            throw new InvalidOperationException("The graph index-buffer binding has no matching declared IndexRead range.");
                        }

                        currentIndexBufferImport = indexBufferImport;
                        SetAccessValidationFlag(
                            accessValidationFlags,
                            Accesses,
                            _accessPairIndex,
                            pass.PassId,
                            binding.Buffer.ResourceId,
                            BufferIndexBindingFlag);
                        hasIndexBufferBinding = true;
                        break;
                    }

                    case GraphicsCommandKind.SetBindings:
                    {
                        if ((currentPipeline is null)
                            || (!currentPipelineRequiresUniformBuffer
                                && !currentPipelineRequiresSampledImage
                                && !currentPipelineRequiresSampler
                                && !currentPipelineRequiresStorageBuffer
                                && !currentPipelineRequiresStorageImage)
                            || currentPipelineBindingsSet)
                        {
                            throw new NotSupportedException("Native graph submission requires one complete binding set after each resource-bearing pipeline.");
                        }

                        var bindings = command.Bindings
                            ?? throw new InvalidOperationException("A recorded binding command lost its binding set.");

                        var expectedBufferBindingCount = currentPipelineUniformBufferBindingCount
                            + currentPipelineStorageBufferBindingCount;
                        var expectedTextureBindingCount = currentPipelineSampledImageBindingCount
                            + currentPipelineStorageImageBindingCount;

                        if ((bindings.Textures.Length != expectedTextureBindingCount)
                            || (bindings.Buffers.Length != expectedBufferBindingCount)
                            || (bindings.Samplers.Length != currentPipelineSamplerBindingCount))
                        {
                            throw new NotSupportedException("The graph binding set must provide every reflected uniform, sampled, storage, and sampler resource exactly once.");
                        }

                        foreach (var binding in bindings.Buffers)
                        {
                            switch (binding.Kind)
                            {
                                case ShaderResourceKind.UniformBuffer:
                                {
                                    if (!TryGetPipelineResource(
                                            currentPipeline.BindingLayout,
                                            ShaderResourceKind.UniformBuffer,
                                            binding.Group,
                                            binding.Binding,
                                            out _)
                                        || !TryGetBufferImport(
                                            bufferImportFlags,
                                            binding.Resource.ResourceId,
                                            BufferUniformImportFlag,
                                            out var uniformBufferImport)
                                        || !binding.Range.IsWithin(uniformBufferImport.Version.Range))
                                    {
                                        throw new NotSupportedException("The uniform-buffer binding must fit the imported graph buffer range.");
                                    }

                                    var accessDeclared = false;

                                    foreach (var access in Accesses)
                                    {
                                        if (!access.IsTexture
                                            && (access.PassId == pass.PassId)
                                            && (access.ResourceId == binding.Resource.ResourceId)
                                            && (access.RequiredState == GraphResourceState.UniformRead)
                                            && !access.IsWrite
                                            && binding.Range.IsWithin(access.BufferRange))
                                        {
                                            accessDeclared = true;
                                            break;
                                        }
                                    }

                                    if (!accessDeclared)
                                    {
                                        throw new InvalidOperationException("The uniform-buffer binding has no matching declared UniformRead access.");
                                    }

                                    hasUniformBufferBinding = true;
                                    SetAccessValidationFlag(
                                        accessValidationFlags,
                                        Accesses,
                                        _accessPairIndex,
                                        pass.PassId,
                                        binding.Resource.ResourceId,
                                        BufferUniformBindingFlag);
                                    break;
                                }

                                case ShaderResourceKind.StorageBuffer:
                                {
                                    if (!TryGetBufferImport(
                                            bufferImportFlags,
                                            binding.Resource.ResourceId,
                                            BufferStorageImportFlag,
                                            out var storageBufferImport)
                                        || !binding.Range.IsWithin(storageBufferImport.Version.Range))
                                    {
                                        throw new NotSupportedException("The storage-buffer binding must fit the imported graph buffer range.");
                                    }

                                    if (TryGetPipelineResource(
                                            currentPipeline.BindingLayout,
                                            ShaderResourceKind.StorageBuffer,
                                            binding.Group,
                                            binding.Binding,
                                            out var storageBufferSignature)
                                        && (storageBufferSignature.Access == ShaderResourceAccess.Write))
                                    {
                                        var writeAccessDeclared = false;

                                        foreach (var access in Accesses)
                                        {
                                            if (!access.IsTexture
                                                && (access.PassId == pass.PassId)
                                                && (access.ResourceId == binding.Resource.ResourceId)
                                                && (access.RequiredState == GraphResourceState.StorageWrite)
                                                && access.IsWrite
                                                && ReferenceEquals(access.BufferVersion, binding.Resource)
                                                && binding.Range.IsWithin(access.BufferRange))
                                            {
                                                writeAccessDeclared = true;
                                                break;
                                            }
                                        }

                                        if (!writeAccessDeclared)
                                        {
                                            throw new InvalidOperationException("The compute storage-buffer binding has no matching declared StorageWrite access.");
                                        }
                                    }
                                    else
                                    {
                                        if (diagnosticRecipe is null)
                                        {
                                            GraphResourceAccess? storageReadAccess = null;
                                            GraphResourceAccess? storageWriteAccess = null;

                                            foreach (var access in Accesses)
                                            {
                                                if (!access.IsTexture
                                                    && (access.PassId == pass.PassId)
                                                    && (access.ResourceId == binding.Resource.ResourceId)
                                                    && (access.RequiredState == GraphResourceState.StorageReadWrite)
                                                    && access.IsStorageReadWritePair
                                                    && binding.Range.IsWithin(access.BufferRange))
                                                {
                                                    if (access.IsWrite
                                                        && ReferenceEquals(access.BufferVersion, binding.Resource))
                                                    {
                                                        storageWriteAccess = access;
                                                    }
                                                    else if (!access.IsWrite)
                                                    {
                                                        storageReadAccess = access;
                                                    }
                                                }
                                            }

                                            if ((storageReadAccess is not GraphResourceAccess readAccess)
                                                || (storageWriteAccess is not GraphResourceAccess writeAccess)
                                                || !readAccess.ContentsValid
                                                || !readAccess.IsStorageReadWritePairOf(writeAccess))
                                            {
                                                throw new InvalidOperationException("The storage-buffer binding has no matching initialized StorageReadWrite version pair.");
                                            }
                                        }
                                    }

                                    hasStorageBufferBinding = true;
                                    SetAccessValidationFlag(
                                        accessValidationFlags,
                                        Accesses,
                                        _accessPairIndex,
                                        pass.PassId,
                                        binding.Resource.ResourceId,
                                        BufferStorageBindingFlag);
                                    break;
                                }

                                default:
                                {
                                    throw new NotSupportedException($"Native graph submission does not support {binding.Kind} buffer bindings.");
                                }
                            }
                        }

                        foreach (var binding in bindings.Textures)
                        {
                            switch (binding.Kind)
                            {
                                case ShaderResourceKind.SampledImage:
                                {
                                    if (!TryGetPipelineResource(
                                            currentPipeline.BindingLayout,
                                            ShaderResourceKind.SampledImage,
                                            binding.Group,
                                            binding.Binding,
                                            out var sampledImageSignature)
                                        || !TryGetTextureImport(
                                            textureImportFlags,
                                            binding.Resource.ResourceId,
                                            TextureSampledImportFlag,
                                            out var sampledTextureImport))
                                    {
                                        throw new NotSupportedException("The sampled-image binding must match its reflected 2D or 2D-array shape and fit its imported color or D32 depth range.");
                                    }

                                    var imageArrayed = sampledImageSignature.ImageArrayed;
                                    var isDepthSampledImage = (sampledTextureImport.Version.Description.Format == TextureFormat.D32Float)
                                        && (binding.Range.Aspect == TextureAspect.Depth);
                                    var sampledTextureDescription = sampledTextureImport.Version.Description;

                                    if (!binding.Range.IsWithin(sampledTextureImport.Version.Range)
                                        || ((binding.Range.Aspect != TextureAspect.Color) && !isDepthSampledImage)
                                        || ((sampledTextureImport.Version.Description.Format == TextureFormat.D32Float) && !isDepthSampledImage)
                                        || (sampledTextureDescription.Dimension != TextureDimension.Dimension2D)
                                        || (!imageArrayed
                                            && ((sampledTextureDescription.ArrayLayers != 1)
                                                || (binding.Range.LayerCount != 1))))
                                    {
                                        throw new NotSupportedException("The sampled-image binding must match its reflected 2D or 2D-array shape and fit its imported color or D32 depth range.");
                                    }

                                    var accessDeclared = false;

                                    foreach (var access in Accesses)
                                    {
                                        if (access.IsTexture
                                            && (access.PassId == pass.PassId)
                                            && (access.ResourceId == binding.Resource.ResourceId)
                                            && (access.RequiredState == GraphResourceState.ShaderSampledRead)
                                            && !access.IsWrite
                                            && binding.Range.IsWithin(access.TextureRange))
                                        {
                                            accessDeclared = true;
                                            break;
                                        }
                                    }

                                    if (!accessDeclared)
                                    {
                                        throw new InvalidOperationException("The sampled-image binding has no matching declared SampledRead access.");
                                    }

                                    hasSampledImageBinding = true;
                                    SetAccessValidationFlag(
                                        accessValidationFlags,
                                        Accesses,
                                        _accessPairIndex,
                                        pass.PassId,
                                        binding.Resource.ResourceId,
                                        TextureSampledBindingFlag);
                                    break;
                                }

                                case ShaderResourceKind.StorageImage:
                                {
                                    if (!TryGetPipelineResource(
                                            currentPipeline.BindingLayout,
                                            ShaderResourceKind.StorageImage,
                                            binding.Group,
                                            binding.Binding,
                                            out var storageImageSignature))
                                    {
                                        throw new NotSupportedException("The storage-image binding has no matching reflected pipeline signature.");
                                    }

                                    var expectedStorageImageTextureFormat = storageImageSignature.StorageImageFormat switch {
                                        ShaderStorageImageFormat.Rgba16Float => TextureFormat.Rgba16Float,
                                        ShaderStorageImageFormat.R32Float => TextureFormat.R32Float,
                                        ShaderStorageImageFormat.R32UInt => TextureFormat.R32UInt,
                                        ShaderStorageImageFormat.R32SInt
                                            => throw new NotSupportedException("Native graph submission has no texture format for an R32SInt storage image."),
                                        _ => throw new InvalidOperationException("The storage-image pipeline omitted its reflected format."),
                                    };

                                    if (!TryGetTextureImport(
                                            textureImportFlags,
                                            binding.Resource.ResourceId,
                                            TextureStorageImportFlag,
                                            out var storageTextureImport)
                                        || !binding.Range.IsWithin(storageTextureImport.Version.Range)
                                        || (binding.Range.MipCount != 1)
                                        || (binding.Range.LayerCount != 1)
                                        || (binding.Range.Aspect != TextureAspect.Color)
                                        || (binding.Resource.Description.Dimension != storageImageSignature.ImageDimension)
                                        || (binding.Resource.Description.Format != expectedStorageImageTextureFormat))
                                    {
                                        throw new NotSupportedException("The storage-image binding must match its reflected format/dimension and fit one imported color mip.");
                                    }

                                    GraphResourceAccess? storageReadAccess = null;
                                    GraphResourceAccess? storageWriteAccess = null;

                                    foreach (var access in Accesses)
                                    {
                                        if (access.IsTexture
                                            && (access.PassId == pass.PassId)
                                            && (access.ResourceId == binding.Resource.ResourceId)
                                            && (access.RequiredState == GraphResourceState.StorageReadWrite)
                                            && access.IsStorageReadWritePair
                                            && binding.Range.IsWithin(access.TextureRange))
                                        {
                                            if (access.IsWrite
                                                && ReferenceEquals(access.TextureVersion, binding.Resource))
                                            {
                                                storageWriteAccess = access;
                                            }
                                            else if (!access.IsWrite)
                                            {
                                                storageReadAccess = access;
                                            }
                                        }
                                    }

                                    if ((storageReadAccess is not GraphResourceAccess readAccess)
                                        || (storageWriteAccess is not GraphResourceAccess writeAccess)
                                        || !readAccess.ContentsValid
                                        || !readAccess.IsStorageReadWritePairOf(writeAccess))
                                    {
                                        throw new InvalidOperationException("The storage-image binding has no matching initialized StorageReadWrite version pair.");
                                    }

                                    hasStorageImageBinding = true;
                                    SetAccessValidationFlag(
                                        accessValidationFlags,
                                        Accesses,
                                        _accessPairIndex,
                                        pass.PassId,
                                        binding.Resource.ResourceId,
                                        TextureStorageBindingFlag);
                                    break;
                                }

                                default:
                                {
                                    throw new NotSupportedException($"Native graph submission does not support {binding.Kind} texture bindings.");
                                }
                            }
                        }

                        foreach (var binding in bindings.Samplers)
                        {
                            if (!TryGetPipelineResource(
                                    currentPipeline.BindingLayout,
                                    ShaderResourceKind.Sampler,
                                    binding.Group,
                                    binding.Binding,
                                    out _)
                                || !ReferenceEquals(binding.Sampler.ResourceOwner.Device, OutputColorTarget.ResourceOwner.Device)
                                || (binding.Sampler.DeviceEpoch != DeviceEpoch))
                            {
                                throw new NotSupportedException("Every sampler binding must identify a live same-device sampler at its reflected group binding.");
                            }

                            hasSamplerBinding = true;
                        }

                        currentPipelineBindingsSet = true;
                        break;
                    }

                    case GraphicsCommandKind.StorageBarrier:
                    {
                        if ((currentPipeline is null)
                            || !currentPipelineBindingsSet
                            || !command.StorageBarrierSourceWrites
                            || (!command.StorageBarrierDestinationReads
                                && !command.StorageBarrierDestinationWrites))
                        {
                            throw new InvalidOperationException("A storage barrier requires a bound storage pipeline and a prior write hazard.");
                        }

                        if (command.StorageBarrierBuffer is GraphBufferVersion barrierBuffer)
                        {
                            if (!TryGetBufferImport(
                                    bufferImportFlags,
                                    barrierBuffer.ResourceId,
                                    BufferStorageImportFlag,
                                    out var storageBufferImport)
                                || (command.StorageBarrierBufferRange != storageBufferImport.Version.Range)
                                || !HasAccessValidationFlagForPair(
                                    accessValidationFlags,
                                    Accesses,
                                    _accessPairIndex,
                                    pass.PassId,
                                    barrierBuffer.ResourceId,
                                    BufferStorageBindingFlag))
                            {
                                throw new NotSupportedException("A storage-buffer barrier must target the bound graph storage buffer range.");
                            }
                        }
                        else if (command.StorageBarrierTexture is GraphTextureVersion barrierTexture)
                        {
                            if (!TryGetTextureImport(
                                    textureImportFlags,
                                    barrierTexture.ResourceId,
                                    TextureStorageImportFlag,
                                    out var storageTextureImport)
                                || !command.StorageBarrierTextureRange.IsWithin(storageTextureImport.Version.Range)
                                || !HasAccessValidationFlagForPair(
                                    accessValidationFlags,
                                    Accesses,
                                    _accessPairIndex,
                                    pass.PassId,
                                    barrierTexture.ResourceId,
                                    TextureStorageBindingFlag))
                            {
                                throw new NotSupportedException("A storage-image barrier must target the bound graph storage image range.");
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException("A storage barrier has no graph resource.");
                        }

                        break;
                    }

                    case GraphicsCommandKind.Draw:
                    {
                        if (pass.Kind != CommandRecorderPassKind.Raster)
                        {
                            throw new NotSupportedException("Native graph non-raster passes cannot record draws.");
                        }

                        if (currentPipeline is null)
                        {
                            throw new InvalidOperationException("A recorded draw has no preceding graphics pipeline.");
                        }

                        if ((currentPipelineRequiresUniformBuffer
                                || currentPipelineRequiresSampledImage
                                || currentPipelineRequiresSampler
                                || currentPipelineRequiresStorageBuffer
                                || currentPipelineRequiresStorageImage)
                            && !currentPipelineBindingsSet)
                        {
                            throw new InvalidOperationException("A resource-bearing graph draw has no recorded binding set.");
                        }

                        if (currentPipelineRequiresVertexBuffer != (currentVertexBufferImport is not null))
                        {
                            throw new InvalidOperationException("A graph draw must bind a vertex buffer exactly when its pipeline has vertex inputs.");
                        }

                        if (currentVertexBufferImport is not null)
                        {
                            RecordBufferResourceUsage(
                                bufferImportFlags,
                                currentVertexBufferImport.Version.ResourceId,
                                BufferVertexResourceFlag,
                                ref vertexBufferResourcesUsedCount);
                        }

                        hasDraw |= (command.Draw.VertexCount > 0) && (command.Draw.InstanceCount > 0);
                        break;
                    }

                    case GraphicsCommandKind.DrawIndexed:
                    {
                        if ((pass.Kind != CommandRecorderPassKind.Raster)
                            || (currentPipeline is null))
                        {
                            throw new NotSupportedException("Native graph indexed draws require a raster pipeline.");
                        }

                        if ((currentPipelineRequiresUniformBuffer
                                || currentPipelineRequiresSampledImage
                                || currentPipelineRequiresSampler
                                || currentPipelineRequiresStorageBuffer
                                || currentPipelineRequiresStorageImage)
                            && !currentPipelineBindingsSet)
                        {
                            throw new InvalidOperationException("A resource-bearing graph indexed draw has no recorded binding set.");
                        }

                        if ((command.DrawIndexed.IndexCount > 0) && (command.DrawIndexed.InstanceCount > 0))
                        {
                            if (currentIndexBufferImport is null)
                            {
                                throw new InvalidOperationException("An indexed graph draw has no preceding index-buffer binding.");
                            }

                            if (currentPipelineRequiresVertexBuffer != (currentVertexBufferImport is not null))
                            {
                                throw new InvalidOperationException("An indexed graph draw must bind a vertex buffer exactly when its pipeline has vertex inputs.");
                            }

                            RecordBufferResourceUsage(
                                bufferImportFlags,
                                currentIndexBufferImport.Version.ResourceId,
                                BufferIndexResourceFlag,
                                ref indexBufferResourcesUsedCount);

                            if (currentVertexBufferImport is not null)
                            {
                                RecordBufferResourceUsage(
                                    bufferImportFlags,
                                    currentVertexBufferImport.Version.ResourceId,
                                    BufferVertexResourceFlag,
                                    ref vertexBufferResourcesUsedCount);
                            }
                        }

                        hasDraw |= (command.DrawIndexed.IndexCount > 0) && (command.DrawIndexed.InstanceCount > 0);
                        break;
                    }

                    case GraphicsCommandKind.Dispatch:
                    {
                        if (pass.Kind != CommandRecorderPassKind.Compute)
                        {
                            throw new NotSupportedException("Native graph dispatches must be recorded by a compute pass.");
                        }

                        if ((currentPipeline is null) || !currentPipeline.BindingLayout.IsCompute)
                        {
                            throw new InvalidOperationException("A recorded dispatch has no preceding compute pipeline.");
                        }

                        if (currentPipelineRequiresStorageBuffer && !currentPipelineBindingsSet)
                        {
                            throw new InvalidOperationException("A resource-bearing graph dispatch has no recorded binding set.");
                        }

                        hasDispatch |= (command.Dispatch.GroupCountX > 0)
                            && (command.Dispatch.GroupCountY > 0)
                            && (command.Dispatch.GroupCountZ > 0);
                        break;
                    }

                    case GraphicsCommandKind.CopyBuffer:
                    {
                        if (pass.Kind != CommandRecorderPassKind.Copy)
                        {
                            throw new NotSupportedException("Native graph buffer copies must be recorded by a copy pass.");
                        }

                        var source = command.CopySource
                            ?? throw new InvalidOperationException("The graph copy command has no source buffer version.");
                        var destination = command.CopyDestination
                            ?? throw new InvalidOperationException("The graph copy command has no destination buffer version.");

                        if (!TryGetBufferImport(
                                bufferImportFlags,
                                source.ResourceId,
                                BufferCopySourceImportFlag,
                                out var copySourceBufferImport)
                            || !TryGetBufferImport(
                                bufferImportFlags,
                                destination.ResourceId,
                                BufferCopyDestinationImportFlag,
                                out var copyDestinationBufferImport)
                            || (source.ResourceId == destination.ResourceId))
                        {
                            throw new NotSupportedException("Native graph buffer copies require distinct imported copy-source and copy-destination buffers.");
                        }

                        var sourceRange = new GraphBufferRange(command.SourceOffset, command.ByteLength);
                        var destinationRange = new GraphBufferRange(command.DestinationOffset, command.ByteLength);

                        if (!sourceRange.IsWithin(copySourceBufferImport.Version.Range)
                            || !destinationRange.IsWithin(copyDestinationBufferImport.Version.Range))
                        {
                            throw new ArgumentOutOfRangeException(nameof(command), "The graph copy range exceeds an imported buffer.");
                        }

                        var sourceAccessDeclared = false;
                        var destinationAccessDeclared = false;

                        foreach (var access in Accesses)
                        {
                            if (access.IsTexture || (access.PassId != pass.PassId))
                            {
                                continue;
                            }

                            sourceAccessDeclared |= (access.ResourceId == source.ResourceId)
                                && (access.RequiredState == GraphResourceState.CopySource)
                                && !access.IsWrite
                                && access.ContentsValid
                                && ReferenceEquals(access.BufferVersion, source)
                                && sourceRange.IsWithin(access.BufferRange);
                            destinationAccessDeclared |= (access.ResourceId == destination.ResourceId)
                                && (access.RequiredState == GraphResourceState.CopyDestination)
                                && access.IsWrite
                                && ReferenceEquals(access.BufferVersion, destination)
                                && destinationRange.IsWithin(access.BufferRange);
                        }

                        if (!sourceAccessDeclared || !destinationAccessDeclared)
                        {
                            throw new InvalidOperationException("A native graph buffer copy has no matching declared source and destination ranges.");
                        }

                        SetPassValidationFlag(
                            passValidationFlags,
                            Passes,
                            _passValidationIndex,
                            pass.PassId,
                            BufferCopySourcePassFlag | BufferCopyDestinationPassFlag);
                        hasBufferCopy = true;
                        break;
                    }

                    case GraphicsCommandKind.CopyTexture:
                    {
                        if (pass.Kind != CommandRecorderPassKind.Copy)
                        {
                            throw new NotSupportedException("Native graph texture copies must be recorded by a copy pass.");
                        }

                        var source = command.CopySourceTexture
                            ?? throw new InvalidOperationException("The graph copy command has no source texture version.");
                        var destination = command.CopyDestinationTexture
                            ?? throw new InvalidOperationException("The graph copy command has no destination texture version.");

                        if (!TryGetTextureImport(
                                textureImportFlags,
                                source.ResourceId,
                                TextureCopySourceImportFlag,
                                out var copySourceTextureImport)
                            || !TryGetTextureImport(
                                textureImportFlags,
                                destination.ResourceId,
                                TextureCopyDestinationImportFlag,
                                out var copyDestinationTextureImport)
                            || (source.ResourceId == destination.ResourceId)
                            || (source.Description != copySourceTextureImport.Version.Description)
                            || (destination.Description != copyDestinationTextureImport.Version.Description)
                            || (source.Range != copySourceTextureImport.Version.Range)
                            || (destination.Range != copyDestinationTextureImport.Version.Range))
                        {
                            throw new NotSupportedException("Native graph texture copies require distinct imported copy-source and copy-destination subresources.");
                        }

                        var sourceSubresource = new TextureSubresource(source.Range.FirstMip, source.Range.FirstLayer, source.Range.Aspect);
                        var destinationSubresource = new TextureSubresource(destination.Range.FirstMip, destination.Range.FirstLayer, destination.Range.Aspect);
                        var (sourceWidth, sourceHeight, _) = GraphicsDevice.GetTextureSubresourceExtent(source.Description, sourceSubresource);
                        var (destinationWidth, destinationHeight, _) = GraphicsDevice.GetTextureSubresourceExtent(destination.Description, destinationSubresource);

                        if ((source.Description.Format != destination.Description.Format)
                            || !command.CopyTextureRegion.IsWithin(
                                sourceWidth,
                                sourceHeight,
                                destinationWidth,
                                destinationHeight))
                        {
                            throw new NotSupportedException("Native graph texture copies require matching formats and a source rectangle that fills the destination subresource.");
                        }

                        var sourceAccessDeclared = false;
                        var destinationAccessDeclared = false;

                        foreach (var access in Accesses)
                        {
                            if (!access.IsTexture || (access.PassId != pass.PassId))
                            {
                                continue;
                            }

                            sourceAccessDeclared |= (access.ResourceId == source.ResourceId)
                                && (access.RequiredState == GraphResourceState.CopySource)
                                && !access.IsWrite
                                && access.ContentsValid
                                && ReferenceEquals(access.TextureVersion, source)
                                && source.Range.IsWithin(access.TextureRange);
                            destinationAccessDeclared |= (access.ResourceId == destination.ResourceId)
                                && (access.RequiredState == GraphResourceState.CopyDestination)
                                && access.IsWrite
                                && ReferenceEquals(access.TextureVersion, destination)
                                && destination.Range.IsWithin(access.TextureRange);
                        }

                        if (!sourceAccessDeclared || !destinationAccessDeclared)
                        {
                            throw new InvalidOperationException("A native graph texture copy has no matching declared source and destination ranges.");
                        }

                        SetPassValidationFlag(
                            passValidationFlags,
                            Passes,
                            _passValidationIndex,
                            pass.PassId,
                            TextureCopySourcePassFlag | TextureCopyDestinationPassFlag);
                        hasTextureCopy = true;
                        break;
                    }

                    default:
                    {
                        throw new NotSupportedException($"Native graph submission does not yet support {command.Kind} commands.");
                    }
                }
            }

            if ((pass.Kind == CommandRecorderPassKind.Raster) && !commands.IsEmpty && !hasDraw)
            {
                throw new NotSupportedException("Native graph submission requires at least one nonempty draw in a command-bearing raster pass.");
            }

            if ((pass.Kind == CommandRecorderPassKind.Compute) && !commands.IsEmpty && !hasDispatch)
            {
                throw new NotSupportedException("Native graph compute passes require a nonempty dispatch.");
            }

            if ((pass.Kind == CommandRecorderPassKind.Copy) && !commands.IsEmpty && !hasBufferCopy && !hasTextureCopy)
            {
                throw new NotSupportedException("Native graph copy passes require a supported buffer or texture copy command.");
            }
        }

        for (var accessIndex = 0; accessIndex < Accesses.Length; accessIndex++)
        {
            var access = Accesses[accessIndex];

            if (access.IsTexture
                && (access.ResourceId == targetResourceId)
                && (access.RequiredState != GraphResourceState.CopySource))
            {
                continue;
            }

            if (!access.IsTexture
                && HasBufferImportFlag(bufferImportFlags, access.ResourceId, BufferCopySourceImportFlag)
                && (access.RequiredState == GraphResourceState.CopySource)
                && !access.IsWrite)
            {
                if (!HasPassValidationFlag(
                        passValidationFlags,
                        Passes,
                        _passValidationIndex,
                        access.PassId,
                        BufferCopySourcePassFlag))
                {
                    throw new InvalidOperationException("The imported copy-source buffer is declared by a pass without a matching copy command.");
                }

                RecordBufferResourceUsage(
                    bufferImportFlags,
                    access.ResourceId,
                    BufferCopySourceResourceFlag,
                    ref copySourceResourcesUsedCount);
                continue;
            }

            if (!access.IsTexture
                && HasBufferImportFlag(bufferImportFlags, access.ResourceId, BufferCopyDestinationImportFlag)
                && (access.RequiredState == GraphResourceState.CopyDestination)
                && access.IsWrite)
            {
                if (!HasPassValidationFlag(
                        passValidationFlags,
                        Passes,
                        _passValidationIndex,
                        access.PassId,
                        BufferCopyDestinationPassFlag))
                {
                    throw new InvalidOperationException("The imported copy-destination buffer is declared by a pass without a matching copy command.");
                }

                RecordBufferResourceUsage(
                    bufferImportFlags,
                    access.ResourceId,
                    BufferCopyDestinationResourceFlag,
                    ref copyDestinationResourcesUsedCount);
                continue;
            }

            if (!access.IsTexture
                && HasBufferImportFlag(bufferImportFlags, access.ResourceId, BufferVertexImportFlag)
                && (access.RequiredState == GraphResourceState.VertexRead)
                && !access.IsWrite)
            {
                if (!HasAccessValidationFlag(accessValidationFlags, accessIndex, BufferVertexBindingFlag))
                {
                    throw new InvalidOperationException("The imported vertex buffer is declared by a pass without a matching vertex-buffer binding.");
                }

                continue;
            }

            if (!access.IsTexture
                && HasBufferImportFlag(bufferImportFlags, access.ResourceId, BufferIndexImportFlag)
                && (access.RequiredState == GraphResourceState.IndexRead)
                && !access.IsWrite)
            {
                if (!HasAccessValidationFlag(accessValidationFlags, accessIndex, BufferIndexBindingFlag))
                {
                    throw new InvalidOperationException("The imported index buffer is declared by a pass without a matching index-buffer binding.");
                }

                continue;
            }

            if (!access.IsTexture
                && HasBufferImportFlag(bufferImportFlags, access.ResourceId, BufferUniformImportFlag)
                && (access.RequiredState == GraphResourceState.UniformRead)
                && !access.IsWrite)
            {
                if (!HasAccessValidationFlag(accessValidationFlags, accessIndex, BufferUniformBindingFlag))
                {
                    throw new InvalidOperationException("The imported uniform buffer is declared by a pass without a matching binding set.");
                }

                hasUniformBufferAccess = true;
                RecordBufferResourceUsage(
                    bufferImportFlags,
                    access.ResourceId,
                    BufferUniformResourceFlag,
                    ref uniformBufferResourcesUsedCount);
                continue;
            }

            if (!access.IsTexture
                && HasBufferImportFlag(bufferImportFlags, access.ResourceId, BufferStorageImportFlag)
                && (access.RequiredState == GraphResourceState.StorageWrite)
                && access.IsWrite)
            {
                if (!HasAccessValidationFlag(accessValidationFlags, accessIndex, BufferStorageBindingFlag))
                {
                    throw new InvalidOperationException("The imported storage buffer is declared by a pass without a matching binding set.");
                }

                hasStorageBufferAccess = true;
                RecordBufferResourceUsage(
                    bufferImportFlags,
                    access.ResourceId,
                    BufferStorageResourceFlag,
                    ref storageBufferResourcesUsedCount);
                continue;
            }

            if (!access.IsTexture
                && HasBufferImportFlag(bufferImportFlags, access.ResourceId, BufferStorageImportFlag)
                && (access.RequiredState == GraphResourceState.StorageReadWrite)
                && access.IsStorageReadWritePair)
            {
                if (!HasAccessValidationFlag(accessValidationFlags, accessIndex, BufferStorageBindingFlag))
                {
                    throw new InvalidOperationException("The imported storage buffer is declared by a pass without a matching binding set.");
                }

                hasStorageBufferAccess = true;
                RecordBufferResourceUsage(
                    bufferImportFlags,
                    access.ResourceId,
                    BufferStorageResourceFlag,
                    ref storageBufferResourcesUsedCount);
                continue;
            }

            if (access.IsTexture
                && (access.RequiredState == GraphResourceState.ColorAttachment)
                && HasPassAttachment(
                    Passes,
                    _passValidationIndex,
                    access.PassId,
                    access.ResourceId,
                    isDepth: false))
            {
                if (access.IsWrite
                    && HasTextureImportFlag(textureImportFlags, access.ResourceId, TextureColorImportFlag))
                {
                    RecordTextureResourceUsage(
                        textureImportFlags,
                        access.ResourceId,
                        TextureColorResourceFlag,
                        ref colorTextureResourcesUsedCount);
                }

                continue;
            }

            if (access.IsTexture
                && HasTextureImportFlag(textureImportFlags, access.ResourceId, TextureDepthImportFlag)
                && (access.RequiredState == GraphResourceState.DepthWrite))
            {
                if (!HasPassAttachment(
                        Passes,
                        _passValidationIndex,
                        access.PassId,
                        access.ResourceId,
                        isDepth: true))
                {
                    throw new InvalidOperationException("The imported depth texture is declared by a pass without a matching depth attachment.");
                }

                if (access.IsWrite)
                {
                    RecordTextureResourceUsage(
                        textureImportFlags,
                        access.ResourceId,
                        TextureDepthResourceFlag,
                        ref depthTextureResourcesUsedCount);
                }

                continue;
            }

            if (access.IsTexture
                && HasTextureImportFlag(textureImportFlags, access.ResourceId, TextureCopySourceImportFlag)
                && (access.RequiredState == GraphResourceState.CopySource)
                && !access.IsWrite)
            {
                if (!HasPassValidationFlag(
                        passValidationFlags,
                        Passes,
                        _passValidationIndex,
                        access.PassId,
                        TextureCopySourcePassFlag))
                {
                    throw new InvalidOperationException("The imported copy-source texture is declared by a pass without a matching copy command.");
                }

                RecordTextureResourceUsage(
                    textureImportFlags,
                    access.ResourceId,
                    TextureCopySourceResourceFlag,
                    ref copySourceTextureResourcesUsedCount);
                continue;
            }

            if (access.IsTexture
                && HasTextureImportFlag(textureImportFlags, access.ResourceId, TextureCopyDestinationImportFlag)
                && (access.RequiredState == GraphResourceState.CopyDestination)
                && access.IsWrite)
            {
                if (!HasPassValidationFlag(
                        passValidationFlags,
                        Passes,
                        _passValidationIndex,
                        access.PassId,
                        TextureCopyDestinationPassFlag))
                {
                    throw new InvalidOperationException("The imported copy-destination texture is declared by a pass without a matching copy command.");
                }

                RecordTextureResourceUsage(
                    textureImportFlags,
                    access.ResourceId,
                    TextureCopyDestinationResourceFlag,
                    ref copyDestinationTextureResourcesUsedCount);
                continue;
            }

            if (access.IsTexture
                && HasTextureImportFlag(textureImportFlags, access.ResourceId, TextureSampledImportFlag)
                && (access.RequiredState == GraphResourceState.ShaderSampledRead)
                && !access.IsWrite)
            {
                if (!HasAccessValidationFlag(accessValidationFlags, accessIndex, TextureSampledBindingFlag))
                {
                    throw new InvalidOperationException("The imported sampled image is declared by a pass without a matching binding set.");
                }

                hasSampledImageAccess = true;
                RecordTextureResourceUsage(
                    textureImportFlags,
                    access.ResourceId,
                    TextureSampledResourceFlag,
                    ref sampledTextureResourcesUsedCount);
                continue;
            }

            if (access.IsTexture
                && HasTextureImportFlag(textureImportFlags, access.ResourceId, TextureStorageImportFlag)
                && (access.RequiredState == GraphResourceState.StorageReadWrite)
                && access.IsStorageReadWritePair)
            {
                if (!HasAccessValidationFlag(accessValidationFlags, accessIndex, TextureStorageBindingFlag))
                {
                    throw new InvalidOperationException("The imported storage image is declared by a pass without a matching binding set.");
                }

                hasStorageImageAccess = true;
                RecordTextureResourceUsage(
                    textureImportFlags,
                    access.ResourceId,
                    TextureStorageResourceFlag,
                    ref storageTextureResourcesUsedCount);
                continue;
            }

            if (access.IsTexture
                || !HasBufferImportFlag(bufferImportFlags, access.ResourceId, BufferUniformImportFlag))
            {
                throw new NotSupportedException("Native graph submission does not support undeclared imported resources.");
            }

            throw new NotSupportedException("Native graph submission supports only its declared resource access states.");
        }

        foreach (var transition in StateTransitions)
        {
            if (transition.IsTexture && (transition.ResourceId == targetResourceId))
            {
                continue;
            }

            if (!transition.IsTexture
                && HasBufferImportFlag(bufferImportFlags, transition.ResourceId, BufferUniformImportFlag)
                && (transition.AfterState == GraphResourceState.UniformRead)
                && (transition.BeforeState == GraphResourceState.General)
                && HasAccessValidationFlagForPair(
                    accessValidationFlags,
                    Accesses,
                    _accessPairIndex,
                    transition.PassId,
                    transition.ResourceId,
                    BufferUniformBindingFlag))
            {
                continue;
            }

            if (!transition.IsTexture
                && HasBufferImportFlag(bufferImportFlags, transition.ResourceId, BufferVertexImportFlag)
                && (transition.AfterState == GraphResourceState.VertexRead)
                && (transition.BeforeState is
                    GraphResourceState.General
                    or GraphResourceState.CopySource
                    or GraphResourceState.CopyDestination
                    or GraphResourceState.StorageReadWrite
                    or GraphResourceState.VertexRead)
                && HasAccessValidationFlagForPair(
                    accessValidationFlags,
                    Accesses,
                    _accessPairIndex,
                    transition.PassId,
                    transition.ResourceId,
                    BufferVertexBindingFlag))
            {
                continue;
            }

            if (!transition.IsTexture
                && HasBufferImportFlag(bufferImportFlags, transition.ResourceId, BufferIndexImportFlag)
                && (transition.AfterState == GraphResourceState.IndexRead)
                && (transition.BeforeState is
                    GraphResourceState.General
                    or GraphResourceState.CopySource
                    or GraphResourceState.CopyDestination
                    or GraphResourceState.VertexRead
                    or GraphResourceState.IndexRead)
                && HasAccessValidationFlagForPair(
                    accessValidationFlags,
                    Accesses,
                    _accessPairIndex,
                    transition.PassId,
                    transition.ResourceId,
                    BufferIndexBindingFlag))
            {
                continue;
            }

            if (!transition.IsTexture
                && HasBufferImportFlag(bufferImportFlags, transition.ResourceId, BufferCopySourceImportFlag)
                && (transition.AfterState == GraphResourceState.CopySource)
                && (transition.BeforeState is
                    GraphResourceState.General
                    or GraphResourceState.CopySource
                    or GraphResourceState.CopyDestination
                    or GraphResourceState.StorageRead
                    or GraphResourceState.StorageWrite
                    or GraphResourceState.StorageReadWrite)
                && HasPassValidationFlag(
                    passValidationFlags,
                    Passes,
                    _passValidationIndex,
                    transition.PassId,
                    BufferCopySourcePassFlag))
            {
                continue;
            }

            if (!transition.IsTexture
                && HasBufferImportFlag(bufferImportFlags, transition.ResourceId, BufferCopyDestinationImportFlag)
                && (transition.AfterState == GraphResourceState.CopyDestination)
                && (transition.BeforeState is
                    GraphResourceState.Undefined
                    or GraphResourceState.General
                    or GraphResourceState.CopySource
                    or GraphResourceState.CopyDestination
                    or GraphResourceState.StorageRead
                    or GraphResourceState.StorageWrite
                    or GraphResourceState.StorageReadWrite)
                && HasPassValidationFlag(
                    passValidationFlags,
                    Passes,
                    _passValidationIndex,
                    transition.PassId,
                    BufferCopyDestinationPassFlag))
            {
                continue;
            }

            if (!transition.IsTexture
                && HasBufferImportFlag(bufferImportFlags, transition.ResourceId, BufferStorageImportFlag)
                && (transition.AfterState is GraphResourceState.StorageWrite or GraphResourceState.StorageReadWrite)
                && (transition.BeforeState is
                    GraphResourceState.Undefined
                    or GraphResourceState.General
                    or GraphResourceState.CopySource
                    or GraphResourceState.CopyDestination
                    or GraphResourceState.StorageRead
                    or GraphResourceState.StorageWrite
                    or GraphResourceState.StorageReadWrite)
                && HasAccessValidationFlagForPair(
                    accessValidationFlags,
                    Accesses,
                    _accessPairIndex,
                    transition.PassId,
                    transition.ResourceId,
                    BufferStorageBindingFlag))
            {
                continue;
            }

            if (transition.IsTexture
                && HasTextureImportFlag(textureImportFlags, transition.ResourceId, TextureColorImportFlag)
                && (transition.AfterState == GraphResourceState.ColorAttachment)
                && (transition.BeforeState is
                    GraphResourceState.Undefined
                    or GraphResourceState.General
                    or GraphResourceState.ColorAttachment
                    or GraphResourceState.CopySource
                    or GraphResourceState.CopyDestination)
                && HasPassAttachment(
                    Passes,
                    _passValidationIndex,
                    transition.PassId,
                    transition.ResourceId,
                    isDepth: false))
            {
                continue;
            }

            if (transition.IsTexture
                && HasTextureImportFlag(textureImportFlags, transition.ResourceId, TextureDepthImportFlag)
                && (transition.AfterState == GraphResourceState.DepthWrite)
                && (transition.BeforeState is
                    GraphResourceState.Undefined
                    or GraphResourceState.General
                    or GraphResourceState.DepthRead
                    or GraphResourceState.DepthWrite
                    or GraphResourceState.ShaderSampledRead
                    or GraphResourceState.CopySource
                    or GraphResourceState.CopyDestination)
                && HasPassAttachment(
                    Passes,
                    _passValidationIndex,
                    transition.PassId,
                    transition.ResourceId,
                    isDepth: true))
            {
                continue;
            }

            if (transition.IsTexture
                && HasTextureImportFlag(textureImportFlags, transition.ResourceId, TextureCopySourceImportFlag)
                && (transition.AfterState == GraphResourceState.CopySource)
                && (transition.BeforeState is
                    GraphResourceState.General
                    or GraphResourceState.CopySource
                    or GraphResourceState.CopyDestination
                    or GraphResourceState.ColorAttachment
                    or GraphResourceState.ShaderSampledRead
                    or GraphResourceState.StorageRead
                    or GraphResourceState.StorageWrite
                    or GraphResourceState.StorageReadWrite)
                && HasPassValidationFlag(
                    passValidationFlags,
                    Passes,
                    _passValidationIndex,
                    transition.PassId,
                    TextureCopySourcePassFlag))
            {
                continue;
            }

            if (transition.IsTexture
                && HasTextureImportFlag(textureImportFlags, transition.ResourceId, TextureCopyDestinationImportFlag)
                && (transition.AfterState == GraphResourceState.CopyDestination)
                && (transition.BeforeState is
                    GraphResourceState.Undefined
                    or GraphResourceState.General
                    or GraphResourceState.CopySource
                    or GraphResourceState.CopyDestination
                    or GraphResourceState.ColorAttachment
                    or GraphResourceState.ShaderSampledRead
                    or GraphResourceState.StorageRead
                    or GraphResourceState.StorageWrite
                    or GraphResourceState.StorageReadWrite)
                && HasPassValidationFlag(
                    passValidationFlags,
                    Passes,
                    _passValidationIndex,
                    transition.PassId,
                    TextureCopyDestinationPassFlag))
            {
                continue;
            }

            if (transition.IsTexture
                && HasTextureImportFlag(textureImportFlags, transition.ResourceId, TextureSampledImportFlag)
                && (transition.AfterState == GraphResourceState.ShaderSampledRead)
                && (transition.BeforeState == GraphResourceState.General)
                && HasAccessValidationFlagForPair(
                    accessValidationFlags,
                    Accesses,
                    _accessPairIndex,
                    transition.PassId,
                    transition.ResourceId,
                    TextureSampledBindingFlag))
            {
                continue;
            }

            if (transition.IsTexture
                && TryGetTextureImport(
                    textureImportFlags,
                    transition.ResourceId,
                    TextureSampledImportFlag,
                    out var sampledTextureImport)
                && sampledTextureImport.IsTransient
                && (transition.AfterState == GraphResourceState.ShaderSampledRead)
                && (transition.BeforeState == GraphResourceState.ColorAttachment)
                && HasAccessValidationFlagForPair(
                    accessValidationFlags,
                    Accesses,
                    _accessPairIndex,
                    transition.PassId,
                    transition.ResourceId,
                    TextureSampledBindingFlag))
            {
                continue;
            }

            if (transition.IsTexture
                && TryGetTextureImport(
                    textureImportFlags,
                    transition.ResourceId,
                    TextureSampledImportFlag,
                    out sampledTextureImport)
                && sampledTextureImport.IsTransient
                && (transition.AfterState == GraphResourceState.ShaderSampledRead)
                && (transition.BeforeState is GraphResourceState.DepthRead or GraphResourceState.DepthWrite)
                && HasAccessValidationFlagForPair(
                    accessValidationFlags,
                    Accesses,
                    _accessPairIndex,
                    transition.PassId,
                    transition.ResourceId,
                    TextureSampledBindingFlag))
            {
                continue;
            }

            if (transition.IsTexture
                && HasTextureImportFlag(textureImportFlags, transition.ResourceId, TextureStorageImportFlag)
                && (transition.AfterState == GraphResourceState.StorageReadWrite)
                && (transition.BeforeState is
                    GraphResourceState.General
                    or GraphResourceState.StorageRead
                    or GraphResourceState.StorageWrite
                    or GraphResourceState.StorageReadWrite)
                && HasAccessValidationFlagForPair(
                    accessValidationFlags,
                    Accesses,
                    _accessPairIndex,
                    transition.PassId,
                    transition.ResourceId,
                    TextureStorageBindingFlag))
            {
                continue;
            }

            if (!HasBufferImportFlag(bufferImportFlags, transition.ResourceId, BufferUniformImportFlag)
                && !HasBufferImportFlag(bufferImportFlags, transition.ResourceId, BufferVertexImportFlag)
                && !HasBufferImportFlag(bufferImportFlags, transition.ResourceId, BufferIndexImportFlag)
                && !HasBufferImportFlag(bufferImportFlags, transition.ResourceId, BufferStorageImportFlag)
                && !HasTextureImportFlag(textureImportFlags, transition.ResourceId, TextureCopySourceImportFlag)
                && !HasTextureImportFlag(textureImportFlags, transition.ResourceId, TextureCopyDestinationImportFlag)
                && !HasTextureImportFlag(textureImportFlags, transition.ResourceId, TextureDepthImportFlag)
                && !HasTextureImportFlag(textureImportFlags, transition.ResourceId, TextureSampledImportFlag)
                && !HasTextureImportFlag(textureImportFlags, transition.ResourceId, TextureStorageImportFlag))
            {
                throw new NotSupportedException("Native graph submission does not support state transitions for other imported resources.");
            }

            throw new NotSupportedException("Native graph submission supports only declared resource transitions.");
        }

        if ((uniformBufferImportCount != 0)
            && (!hasUniformBufferPipeline
                || !hasUniformBufferBinding
                || !hasUniformBufferAccess
                || (uniformBufferResourcesUsedCount != uniformBufferImportCount)))
        {
            throw new NotSupportedException("Every imported uniform buffer must be reflected, bound, and declared by a graph pass.");
        }

        if ((vertexBufferImportCount != 0)
            && (!hasVertexBufferPipeline
                || !hasVertexBufferBinding
                || (vertexBufferResourcesUsedCount != vertexBufferImportCount)))
        {
            throw new NotSupportedException("Every imported vertex buffer must be bound by a vertex-input graphics pipeline and used by a draw.");
        }

        if ((indexBufferImportCount != 0)
            && (!hasIndexBufferBinding
                || (indexBufferResourcesUsedCount != indexBufferImportCount)))
        {
            throw new NotSupportedException("Every imported index buffer must be bound and used by an indexed draw.");
        }

        if (copySourceResourcesUsedCount != copySourceBufferImportCount)
        {
            throw new NotSupportedException("Every imported copy-source buffer must be read by a graph copy command.");
        }

        if (copyDestinationResourcesUsedCount != copyDestinationBufferImportCount)
        {
            throw new NotSupportedException("Every imported copy-destination buffer must be written by a graph copy command.");
        }

        if (copySourceTextureResourcesUsedCount != copySourceTextureImportCount)
        {
            throw new NotSupportedException(
                $"Every imported copy-source texture must be read by a graph copy command ({copySourceTextureResourcesUsedCount} of {copySourceTextureImportCount} were used).");
        }

        if (copyDestinationTextureResourcesUsedCount != copyDestinationTextureImportCount)
        {
            throw new NotSupportedException("Every imported copy-destination texture must be written by a graph copy command.");
        }

        if (depthTextureResourcesUsedCount != depthTextureImportCount)
        {
            throw new NotSupportedException("Every imported depth attachment must be written by a graph pass.");
        }

        if (colorTextureResourcesUsedCount != colorTextureImportCount)
        {
            throw new NotSupportedException("Every imported color attachment must be written by a graph pass.");
        }

        if ((sampledTextureImportCount != 0)
            && (!hasSampledImagePipeline
                || !hasSampledImageBinding
                || !hasSampledImageAccess
                || !hasSamplerPipeline
                || !hasSamplerBinding
                || (sampledTextureResourcesUsedCount != sampledTextureImportCount)))
        {
            throw new NotSupportedException("Every imported sampled image must be reflected, bound with its sampler, and declared by a graph pass.");
        }

        if ((storageBufferImportCount != 0)
            && (!hasStorageBufferPipeline
                || !hasStorageBufferBinding
                || !hasStorageBufferAccess
                || (storageBufferResourcesUsedCount != storageBufferImportCount)))
        {
            throw new NotSupportedException("Every imported storage buffer must be reflected, bound, and declared by a graph pass.");
        }

        if ((storageTextureImportCount != 0)
            && (!hasStorageImagePipeline
                || !hasStorageImageBinding
                || !hasStorageImageAccess
                || (storageTextureResourcesUsedCount != storageTextureImportCount)))
        {
            throw new NotSupportedException("Every imported storage image must be reflected, bound, and declared by a graph pass.");
        }

        if (!hasOutputColorAttachment)
        {
            throw new NotSupportedException("Native graph submission requires a graph-output color attachment.");
        }
    }

    private int FindFirstBufferAliasingAfterIndex(ulong resourceId)
    {
        var first = 0;
        var last = _bufferAliasingAfterIndex.Length;

        while (first < last)
        {
            var middle = first + ((last - first) / 2);
            var barrier = AliasingBarriers[_bufferAliasingAfterIndex[middle]];

            if (barrier.AfterResourceId < resourceId)
            {
                first = middle + 1;
            }
            else
            {
                last = middle;
            }
        }

        if (first == _bufferAliasingAfterIndex.Length)
        {
            return -1;
        }

        var index = _bufferAliasingAfterIndex[first];
        return (AliasingBarriers[index].AfterResourceId == resourceId) ? index : -1;
    }

    private int FindFirstBufferImportIndex(ulong resourceId)
    {
        var first = 0;
        var last = _bufferImportIndex.Length;

        while (first < last)
        {
            var middle = first + ((last - first) / 2);
            var import = BufferImports[_bufferImportIndex[middle]];

            if ((import is null) || (import.Version.ResourceId < resourceId))
            {
                first = middle + 1;
            }
            else
            {
                last = middle;
            }
        }

        if (first == _bufferImportIndex.Length)
        {
            return -1;
        }

        var index = _bufferImportIndex[first];
        var candidate = BufferImports[index];
        return (candidate is not null) && (candidate.Version.ResourceId == resourceId) ? index : -1;
    }

    private int FindFirstOrderedPassIndex(RenderPassId passId)
    {
        var first = 0;
        var last = _orderedPassIndex.Length;

        while (first < last)
        {
            var middle = first + ((last - first) / 2);
            var pass = OrderedPasses[_orderedPassIndex[middle]];

            if (CompareRenderPassIds(pass, passId) < 0)
            {
                first = middle + 1;
            }
            else
            {
                last = middle;
            }
        }

        if (first == _orderedPassIndex.Length)
        {
            return -1;
        }

        var index = _orderedPassIndex[first];
        return (OrderedPasses[index] == passId) ? index : -1;
    }

    private int FindFirstTextureAliasingAfterIndex(ulong resourceId)
    {
        var first = 0;
        var last = _textureAliasingAfterIndex.Length;

        while (first < last)
        {
            var middle = first + ((last - first) / 2);
            var barrier = TextureAliasingBarriers[_textureAliasingAfterIndex[middle]];

            if (barrier.AfterResourceId < resourceId)
            {
                first = middle + 1;
            }
            else
            {
                last = middle;
            }
        }

        if (first == _textureAliasingAfterIndex.Length)
        {
            return -1;
        }

        var index = _textureAliasingAfterIndex[first];
        return (TextureAliasingBarriers[index].AfterResourceId == resourceId) ? index : -1;
    }

    private int FindFirstTextureImportIndex(ulong resourceId)
    {
        var index = FindFirstTextureImportSortedIndex(resourceId);
        return (index >= 0) ? _textureImportIndex[index] : -1;
    }

    private int FindFirstTextureImportSortedIndex(ulong resourceId)
    {
        var first = 0;
        var last = _textureImportIndex.Length;

        while (first < last)
        {
            var middle = first + ((last - first) / 2);
            var import = TextureImports[_textureImportIndex[middle]];

            if ((import is null) || (import.Version.ResourceId < resourceId))
            {
                first = middle + 1;
            }
            else
            {
                last = middle;
            }
        }

        if (first < _textureImportIndex.Length)
        {
            var candidate = TextureImports[_textureImportIndex[first]];

            if ((candidate is not null) && (candidate.Version.ResourceId == resourceId))
            {
                return first;
            }
        }

        return -1;
    }

    private int FindFirstTransientTextureImportIndex(ulong resourceId)
    {
        var index = FindFirstTextureImportIndex(resourceId);
        return (index >= 0) && TextureImports[index].IsTransient ? index : -1;
    }

    private GraphBufferImportRecord GetBufferImport(ulong resourceId)
    {
        foreach (var import in BufferImports)
        {
            if (import.Version.ResourceId == resourceId)
            {
                return import;
            }
        }

        throw new NotSupportedException("Native graph submission does not yet materialize transient buffers.");
    }

    private int GetOrderedPassIndex(RenderPassId passId)
        => TryGetOrderedPassIndex(passId, out var index)
            ? index
            : throw new KeyNotFoundException("The graph pass is not in the ordered pass index.");

    private GraphTextureImportRecord GetTextureImport(ulong resourceId)
    {
        foreach (var import in TextureImports)
        {
            if (import.Version.ResourceId == resourceId)
            {
                return import;
            }
        }

        throw new NotSupportedException("Native graph submission does not yet materialize transient textures.");
    }

    private bool HasBufferImportFlag(ushort[] bufferImportFlags, ulong resourceId, ushort flag)
    {
        var index = FindFirstBufferImportIndex(resourceId);
        return (index >= 0) && ((bufferImportFlags[index] & flag) != 0);
    }

    private bool HasTextureImportFlag(ushort[] textureImportFlags, ulong resourceId, ushort flag)
    {
        var index = FindFirstTextureImportSortedIndex(resourceId);

        while ((index >= 0) && (index < _textureImportIndex.Length))
        {
            var importIndex = _textureImportIndex[index];
            var import = TextureImports[importIndex];

            if ((import is null) || (import.Version.ResourceId != resourceId))
            {
                return false;
            }

            if ((textureImportFlags[importIndex] & flag) != 0)
            {
                return true;
            }

            index++;
        }

        return false;
    }

    private void RecordBufferResourceUsage(
        ushort[] resourceUsageFlags,
        ulong resourceId,
        ushort flag,
        ref int usedCount)
    {
        var importIndex = FindFirstBufferImportIndex(resourceId);

        if (importIndex < 0)
        {
            throw new InvalidOperationException("The graph buffer usage index lost a declared import.");
        }

        AddImportResourceUsageFlag(resourceUsageFlags, importIndex, flag, ref usedCount);
    }

    private void RecordTextureResourceUsage(
        ushort[] resourceUsageFlags,
        ulong resourceId,
        ushort flag,
        ref int usedCount)
    {
        var importIndex = FindFirstTextureImportIndex(resourceId);

        if (importIndex < 0)
        {
            throw new InvalidOperationException("The graph texture usage index lost a declared import.");
        }

        AddImportResourceUsageFlag(resourceUsageFlags, importIndex, flag, ref usedCount);
    }

    private bool TryAddTextureImportFlag(
        ushort[] textureImportFlags,
        int importIndex,
        ulong resourceId,
        ushort flag)
    {
        if (HasTextureImportFlag(textureImportFlags, resourceId, flag))
        {
            return false;
        }

        textureImportFlags[importIndex] |= flag;
        return true;
    }

    private bool TryGetBufferImport(
        ushort[] bufferImportFlags,
        ulong resourceId,
        ushort flag,
        [NotNullWhen(true)] out GraphBufferImportRecord? import)
    {
        var index = FindFirstBufferImportIndex(resourceId);

        if ((index < 0) || ((bufferImportFlags[index] & flag) == 0))
        {
            import = null;
            return false;
        }

        import = BufferImports[index];
        return true;
    }

    private bool TryGetTextureImport(
        ushort[] textureImportFlags,
        ulong resourceId,
        ushort flag,
        [NotNullWhen(true)] out GraphTextureImportRecord? import)
    {
        var index = FindFirstTextureImportSortedIndex(resourceId);

        while ((index >= 0) && (index < _textureImportIndex.Length))
        {
            var importIndex = _textureImportIndex[index];
            var candidate = TextureImports[importIndex];

            if ((candidate is null) || (candidate.Version.ResourceId != resourceId))
            {
                break;
            }

            if ((textureImportFlags[importIndex] & flag) != 0)
            {
                import = candidate;
                return true;
            }

            index++;
        }

        import = null;
        return false;
    }

    private readonly record struct ColorAttachmentResourceIndexEntry(ulong ResourceId, int FirstOccurrenceIndex);
}
