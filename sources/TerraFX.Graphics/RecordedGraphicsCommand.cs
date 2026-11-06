// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

internal readonly struct RecordedGraphicsCommand
{
    internal RecordedGraphicsCommand(
        GraphicsCommandKind kind,
        GpuPipelineHandle pipeline = default,
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
        long byteLength = 0)
    {
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
        SourceOffset = sourceOffset;
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

    internal long SourceOffset { get; }

    internal VertexBufferBinding VertexBuffer { get; }
}
