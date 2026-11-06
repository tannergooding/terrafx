// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Records synchronous commands declared by one render-graph pass.</summary>
public readonly ref struct CommandRecorder
{
    private readonly CommandRecorderState? _state;

    internal CommandRecorder(CommandRecorderState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
    }

    private readonly CommandRecorderState State
        => _state ?? throw new InvalidOperationException("The command recorder is not active.");

    /// <summary>Records one copy between exact graph-declared buffer ranges.</summary>
    /// <param name="source">The graph-owned copy-source version.</param>
    /// <param name="sourceOffset">The nonnegative source byte offset.</param>
    /// <param name="destination">The graph-owned copy-destination version.</param>
    /// <param name="destinationOffset">The nonnegative destination byte offset.</param>
    /// <param name="byteLength">The positive copied byte count.</param>
    public readonly void CopyBuffer(
        GraphBufferVersion source,
        long sourceOffset,
        GraphBufferVersion destination,
        long destinationOffset,
        long byteLength)
        => State.CopyBuffer(source, sourceOffset, destination, destinationOffset, byteLength);

    /// <summary>Records a complete single-subresource color copy between distinct graph textures.</summary>
    /// <param name="source">The graph-owned copy-source version with one color subresource.</param>
    /// <param name="destination">The graph-owned copy-destination version with the same extent and format.</param>
    public readonly void CopyTexture(GraphTextureVersion source, GraphTextureVersion destination)
        => State.CopyTexture(source, destination);

    /// <summary>Records one compute dispatch within the selected device limits.</summary>
    /// <param name="size">The nonnegative work-group counts.</param>
    public readonly void Dispatch(DispatchSize size)
        => State.Dispatch(size);

    /// <summary>Records one nonindexed draw.</summary>
    /// <param name="arguments">The validated draw arguments.</param>
    public readonly void Draw(DrawArguments arguments)
        => State.Draw(arguments);

    /// <summary>Records one indexed draw.</summary>
    /// <param name="arguments">The validated indexed-draw arguments.</param>
    public readonly void DrawIndexed(DrawIndexedArguments arguments)
        => State.DrawIndexed(arguments);

    /// <summary>Binds the exact graph-declared shader resources for the current pipeline.</summary>
    /// <param name="bindings">The immutable binding set.</param>
    public readonly void SetBindings(BindingSet bindings)
        => State.SetBindings(bindings);

    /// <summary>Binds the exact graph-declared index-buffer range.</summary>
    /// <param name="binding">The validated index-buffer binding.</param>
    public readonly void SetIndexBuffer(IndexBufferBinding binding)
        => State.SetIndexBuffer(binding);

    /// <summary>Binds one live pipeline compatible with this pass.</summary>
    /// <param name="pipeline">The live pipeline handle.</param>
    public readonly void SetPipeline(GpuPipelineHandle pipeline)
        => State.SetPipeline(pipeline);

    /// <summary>Binds the exact graph-declared vertex-buffer range.</summary>
    /// <param name="binding">The validated vertex-buffer binding.</param>
    public readonly void SetVertexBuffer(VertexBufferBinding binding)
        => State.SetVertexBuffer(binding);

}
