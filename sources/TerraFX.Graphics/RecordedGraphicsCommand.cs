// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

internal readonly struct RecordedGraphicsCommand
{
    internal RecordedGraphicsCommand(
        GraphicsCommandKind kind,
        GpuPipelineHandle pipeline = default,
        GpuPipeline? pipelineOwner = null,
        BindingSet? bindings = null,
        VertexBufferBinding vertexBuffer = default,
        IndexBufferBinding indexBuffer = default,
        DrawArguments draw = default,
        DrawIndexedArguments drawIndexed = default,
        DispatchSize dispatch = default,
        GraphBufferVersion? copySource = null,
        GraphBufferVersion? copyDestination = null,
        long sourceOffset = 0,
        long destinationOffset = 0,
        long byteLength = 0,
        GraphBufferVersion? storageBarrierBuffer = null,
        GraphBufferRange storageBarrierBufferRange = default,
        bool storageBarrierDestinationReads = false,
        bool storageBarrierDestinationWrites = false,
        bool storageBarrierSourceReads = false,
        bool storageBarrierSourceWrites = false,
        GraphTextureVersion? storageBarrierTexture = null,
        GraphTextureRange storageBarrierTextureRange = default)
    {
        if (kind == GraphicsCommandKind.SetPipeline)
        {
            ArgumentNullException.ThrowIfNull(pipelineOwner);

            if (!pipeline.IsValid || (pipelineOwner.Handle != pipeline))
            {
                throw new ArgumentException("A pipeline command must retain the matching live pipeline owner.", nameof(pipelineOwner));
            }
        }
        else if (pipeline.IsValid || (pipelineOwner is not null))
        {
            throw new ArgumentException("Only a pipeline command can identify a pipeline.", nameof(pipelineOwner));
        }

        var hasStorageBarrierBuffer = storageBarrierBuffer is not null;
        var hasStorageBarrierTexture = storageBarrierTexture is not null;

        if (kind == GraphicsCommandKind.StorageBarrier)
        {
            if (hasStorageBarrierBuffer == hasStorageBarrierTexture)
            {
                throw new ArgumentException("A storage barrier must target exactly one graph resource.");
            }

            if (!storageBarrierSourceWrites
                || (!storageBarrierDestinationReads && !storageBarrierDestinationWrites))
            {
                throw new ArgumentException("A storage barrier must order prior writes before a later shader-storage access.");
            }

            if (storageBarrierBuffer is GraphBufferVersion selectedBuffer
                && !storageBarrierBufferRange.IsWithin(selectedBuffer.Range))
            {
                throw new ArgumentException("A storage-barrier buffer range must fit its graph version.", nameof(storageBarrierBufferRange));
            }

            if (storageBarrierTexture is GraphTextureVersion selectedTexture
                && !storageBarrierTextureRange.IsWithin(selectedTexture.Range))
            {
                throw new ArgumentException("A storage-barrier texture range must fit its graph version.", nameof(storageBarrierTextureRange));
            }
        }
        else if (hasStorageBarrierBuffer
            || hasStorageBarrierTexture
            || storageBarrierBufferRange != default
            || storageBarrierDestinationReads
            || storageBarrierDestinationWrites
            || storageBarrierSourceReads
            || storageBarrierSourceWrites
            || storageBarrierTextureRange != default)
        {
            throw new ArgumentException("Only a storage-barrier command can identify storage-barrier resources.");
        }

        Bindings = bindings;
        ByteLength = byteLength;
        CopyDestination = copyDestination;
        CopySource = copySource;
        DestinationOffset = destinationOffset;
        Dispatch = dispatch;
        Draw = draw;
        DrawIndexed = drawIndexed;
        IndexBuffer = indexBuffer;
        Kind = kind;
        Pipeline = pipeline;
        PipelineOwner = pipelineOwner;
        SourceOffset = sourceOffset;
        StorageBarrierBuffer = storageBarrierBuffer;
        StorageBarrierBufferRange = storageBarrierBufferRange;
        StorageBarrierDestinationReads = storageBarrierDestinationReads;
        StorageBarrierDestinationWrites = storageBarrierDestinationWrites;
        StorageBarrierSourceReads = storageBarrierSourceReads;
        StorageBarrierSourceWrites = storageBarrierSourceWrites;
        StorageBarrierTexture = storageBarrierTexture;
        StorageBarrierTextureRange = storageBarrierTextureRange;
        VertexBuffer = vertexBuffer;
    }

    internal BindingSet? Bindings { get; }

    internal long ByteLength { get; }

    internal GraphBufferVersion? CopyDestination { get; }

    internal GraphBufferVersion? CopySource { get; }

    internal long DestinationOffset { get; }

    internal DispatchSize Dispatch { get; }

    internal DrawArguments Draw { get; }

    internal DrawIndexedArguments DrawIndexed { get; }

    internal IndexBufferBinding IndexBuffer { get; }

    internal GraphicsCommandKind Kind { get; }

    internal GpuPipelineHandle Pipeline { get; }

    internal GpuPipeline? PipelineOwner { get; }

    internal long SourceOffset { get; }

    internal GraphBufferVersion? StorageBarrierBuffer { get; }

    internal GraphBufferRange StorageBarrierBufferRange { get; }

    internal bool StorageBarrierDestinationReads { get; }

    internal bool StorageBarrierDestinationWrites { get; }

    internal bool StorageBarrierSourceReads { get; }

    internal bool StorageBarrierSourceWrites { get; }

    internal GraphTextureVersion? StorageBarrierTexture { get; }

    internal GraphTextureRange StorageBarrierTextureRange { get; }

    internal VertexBufferBinding VertexBuffer { get; }
}
