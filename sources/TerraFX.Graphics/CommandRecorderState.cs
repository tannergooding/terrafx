// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace TerraFX.Graphics;

internal sealed class CommandRecorderState : IDisposable
{
    private readonly ImmutableArray<TextureFormat> _colorTargetFormats;
    private readonly List<RecordedGraphicsCommand> _commands = [];
    private readonly ImmutableArray<GraphResourceAccess> _declaredAccesses;
    private readonly TextureFormat? _depthTargetFormat;
    private readonly GraphicsDevice _device;
    private readonly int _ownerThreadId;
    private readonly RenderPassId _passId;
    private readonly CommandRecorderPassKind _passKind;
    private readonly List<GraphicsResourceOwner.UseLease> _useLeases = [];
    private IndexBufferBinding? _indexBuffer;
    private bool _isCompleted;
    private bool _isDisposed;
    private bool _isShaderBindingsSet;
    private PipelineBindingLayout? _pipelineLayout;
    private VertexBufferBinding? _vertexBuffer;

    internal CommandRecorderState(
        GraphicsDevice device,
        RenderPassId passId,
        CommandRecorderPassKind passKind,
        ImmutableArray<GraphResourceAccess> declaredAccesses,
        ImmutableArray<TextureFormat> colorTargetFormats,
        TextureFormat? depthTargetFormat)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (!passId.IsValid)
        {
            throw new ArgumentException("The command recorder requires a valid pass ID.", nameof(passId));
        }

        if (!Enum.IsDefined(passKind) || (passKind == CommandRecorderPassKind.Presentation))
        {
            throw new ArgumentOutOfRangeException(nameof(passKind));
        }

        if (declaredAccesses.IsDefault || colorTargetFormats.IsDefault)
        {
            throw new ArgumentException("The command recorder requires initialized pass metadata.");
        }

        if (device.Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice is not ready for command recording.");
        }

        if (!device.IsOwnerThread)
        {
            throw new InvalidOperationException("A command recorder must be created on the graphics owner thread.");
        }

        foreach (var access in declaredAccesses)
        {
            if ((access.PassId != passId) || (access.DeviceEpoch != device.DeviceEpoch))
            {
                throw new ArgumentException("Every recorder access must belong to this pass and device generation.", nameof(declaredAccesses));
            }
        }

        if ((passKind != CommandRecorderPassKind.Raster)
            && (!colorTargetFormats.IsEmpty || depthTargetFormat.HasValue))
        {
            throw new ArgumentException("Only a raster pass can declare attachment target formats.", nameof(colorTargetFormats));
        }

        _colorTargetFormats = [.. colorTargetFormats];
        _declaredAccesses = [.. declaredAccesses];
        _device = device;
        _depthTargetFormat = depthTargetFormat;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _passId = passId;
        _passKind = passKind;
    }

    private static GraphBufferUsage GetBufferUsage(ShaderResourceKind kind, ShaderResourceAccess access)
        => (kind, access) switch {
            (ShaderResourceKind.UniformBuffer, ShaderResourceAccess.Read) => GraphBufferUsage.UniformRead,
            (ShaderResourceKind.StorageBuffer, ShaderResourceAccess.Read) => GraphBufferUsage.StorageRead,
            (ShaderResourceKind.StorageBuffer, ShaderResourceAccess.Write) => GraphBufferUsage.StorageWrite,
            (ShaderResourceKind.StorageBuffer, ShaderResourceAccess.ReadWrite)
                => throw new NotSupportedException("The graph has no explicit in-pass storage read-write declaration."),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static Dictionary<(ShaderBindingGroup Group, ShaderResourceKind Kind, int Binding), ShaderResourceAccess> GetExpectedBindings(
        PipelineBindingLayout pipelineLayout)
    {
        var expectedBindings =
            new Dictionary<(ShaderBindingGroup Group, ShaderResourceKind Kind, int Binding), ShaderResourceAccess>();

        foreach (var reflection in pipelineLayout.ShaderReflections)
        {
            foreach (var resource in reflection.Resources)
            {
                if (resource.ArrayCount != 1)
                {
                    throw new NotSupportedException("The command recorder supports only single-descriptor shader bindings.");
                }

                var key = (resource.Group, resource.Kind, resource.Binding);

                if (expectedBindings.TryGetValue(key, out var existingAccess))
                {
                    if (existingAccess != resource.Access)
                    {
                        throw new InvalidOperationException("Shader stages disagree on a shared resource access contract.");
                    }
                }
                else
                {
                    expectedBindings.Add(key, resource.Access);
                }
            }
        }

        return expectedBindings;
    }

    private static int GetIndexElementSize(IndexFormat format)
        => format switch {
            IndexFormat.UInt16 => sizeof(ushort),
            IndexFormat.UInt32 => sizeof(uint),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

    private static GraphTextureUsage GetTextureUsage(ShaderResourceKind kind, ShaderResourceAccess access)
        => (kind, access) switch {
            (ShaderResourceKind.SampledImage, ShaderResourceAccess.Read) => GraphTextureUsage.SampledRead,
            (ShaderResourceKind.StorageImage, ShaderResourceAccess.Read) => GraphTextureUsage.StorageRead,
            (ShaderResourceKind.StorageImage, ShaderResourceAccess.Write) => GraphTextureUsage.StorageWrite,
            (ShaderResourceKind.StorageImage, ShaderResourceAccess.ReadWrite)
                => throw new NotSupportedException("The graph has no explicit in-pass storage read-write declaration."),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static bool HasReflectedResources(PipelineBindingLayout pipelineLayout)
    {
        foreach (var reflection in pipelineLayout.ShaderReflections)
        {
            if (!reflection.Resources.IsEmpty)
            {
                return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        ReleaseUseLeases();
    }

    internal RecordedPassCommandList Complete()
    {
        EnsureActive();
        _isCompleted = true;

        var commandList = new RecordedPassCommandList(
            [.. _commands],
            [.. _useLeases]);
        _useLeases.Clear();
        return commandList;
    }

    internal void CopyBuffer(
        GraphBufferVersion source,
        long sourceOffset,
        GraphBufferVersion destination,
        long destinationOffset,
        long byteLength)
    {
        EnsureActive();
        EnsurePassKind(CommandRecorderPassKind.Copy);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(destinationOffset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLength);
        ArgumentOutOfRangeException.ThrowIfNotEqual(source.DeviceEpoch, _device.DeviceEpoch, nameof(source));
        ArgumentOutOfRangeException.ThrowIfNotEqual(destination.DeviceEpoch, _device.DeviceEpoch, nameof(destination));

        if ((source.Description.Usage & BufferUsage.CopySource) == 0)
        {
            throw new ArgumentException("The source graph buffer does not permit copy-source use.", nameof(source));
        }

        if ((destination.Description.Usage & BufferUsage.CopyDestination) == 0)
        {
            throw new ArgumentException("The destination graph buffer does not permit copy-destination use.", nameof(destination));
        }

        var sourceRange = new GraphBufferRange(sourceOffset, byteLength);
        var destinationRange = new GraphBufferRange(destinationOffset, byteLength);

        if (!HasBufferAccess(source, sourceRange, GraphBufferUsage.CopySource)
            || !HasBufferAccess(destination, destinationRange, GraphBufferUsage.CopyDestination))
        {
            throw new InvalidOperationException("A buffer copy must fit the exact source and destination ranges declared by this pass.");
        }

        if ((source.ResourceId == destination.ResourceId) && sourceRange.Overlaps(destinationRange))
        {
            throw new ArgumentException("A buffer copy cannot overlap its source and destination ranges.", nameof(destination));
        }

        _commands.Add(new RecordedGraphicsCommand(
            GraphicsCommandKind.CopyBuffer,
            copySource: source,
            copyDestination: destination,
            sourceOffset: sourceOffset,
            destinationOffset: destinationOffset,
            byteLength: byteLength));
    }

    internal void Dispatch(DispatchSize size)
    {
        EnsureActive();
        EnsurePassKind(CommandRecorderPassKind.Compute);
        var pipelineLayout = RequirePipelineLayout();

        if (!pipelineLayout.IsCompute)
        {
            throw new InvalidOperationException("A compute dispatch requires a compute pipeline.");
        }

        var limits = _device.Capabilities.Limits;

        if ((size.GroupCountX > limits.MaximumComputeWorkGroupCountX)
            || (size.GroupCountY > limits.MaximumComputeWorkGroupCountY)
            || (size.GroupCountZ > limits.MaximumComputeWorkGroupCountZ))
        {
            throw new ArgumentOutOfRangeException(nameof(size), "The compute dispatch exceeds a device work-group-count limit.");
        }

        ValidateShaderBindings(pipelineLayout);
        _commands.Add(new RecordedGraphicsCommand(GraphicsCommandKind.Dispatch, dispatch: size));
    }

    internal void Draw(DrawArguments arguments)
    {
        EnsureActive();
        EnsurePassKind(CommandRecorderPassKind.Raster);
        var pipelineLayout = RequireRasterPipeline();
        ValidateShaderBindings(pipelineLayout);

        if ((arguments.VertexCount > 0) && (arguments.InstanceCount > 0) && (pipelineLayout.VertexStride > 0))
        {
            var vertexBuffer = _vertexBuffer
                ?? throw new InvalidOperationException("The graphics pipeline requires a declared vertex-buffer binding.");
            var endVertex = checked((long)arguments.FirstVertex + arguments.VertexCount);
            var byteEnd = checked(endVertex * pipelineLayout.VertexStride);

            if (byteEnd > vertexBuffer.Range.ByteLength)
            {
                throw new ArgumentOutOfRangeException(nameof(arguments), "The draw reads beyond the bound vertex-buffer range.");
            }
        }

        _commands.Add(new RecordedGraphicsCommand(GraphicsCommandKind.Draw, draw: arguments));
    }

    internal void DrawIndexed(DrawIndexedArguments arguments)
    {
        EnsureActive();
        EnsurePassKind(CommandRecorderPassKind.Raster);
        var pipelineLayout = RequireRasterPipeline();
        ValidateShaderBindings(pipelineLayout);

        if ((arguments.IndexCount > 0) && (arguments.InstanceCount > 0))
        {
            var indexBuffer = _indexBuffer
                ?? throw new InvalidOperationException("An indexed draw requires a declared index-buffer binding.");
            var elementSize = GetIndexElementSize(indexBuffer.Format);
            var endIndex = checked((long)arguments.FirstIndex + arguments.IndexCount);
            var byteEnd = checked(endIndex * elementSize);

            if (byteEnd > indexBuffer.Range.ByteLength)
            {
                throw new ArgumentOutOfRangeException(nameof(arguments), "The indexed draw reads beyond the bound index-buffer range.");
            }

            if ((pipelineLayout.VertexStride > 0) && !_vertexBuffer.HasValue)
            {
                throw new InvalidOperationException("The graphics pipeline requires a declared vertex-buffer binding.");
            }
        }

        _commands.Add(new RecordedGraphicsCommand(GraphicsCommandKind.DrawIndexed, drawIndexed: arguments));
    }

    internal void SetBindings(BindingSet bindings)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(bindings);
        var pipelineLayout = RequirePipelineLayout();
        var expectedBindings = GetExpectedBindings(pipelineLayout);
        var actualBindings = new HashSet<(ShaderBindingGroup Group, ShaderResourceKind Kind, int Binding)>();

        foreach (var binding in bindings.Buffers)
        {
            var key = (binding.Group, binding.Kind, binding.Binding);

            if (!expectedBindings.TryGetValue(key, out var access) || !actualBindings.Add(key))
            {
                throw new ArgumentException("The binding set does not match the current pipeline reflection.", nameof(bindings));
            }

            var usage = GetBufferUsage(binding.Kind, access);

            if (!HasBufferAccess(binding.Resource, binding.Range, usage))
            {
                throw new InvalidOperationException($"A {binding.Kind} binding must fit a {usage} range declared by this pass.");
            }
        }

        foreach (var binding in bindings.Textures)
        {
            var key = (binding.Group, binding.Kind, binding.Binding);

            if (!expectedBindings.TryGetValue(key, out var access) || !actualBindings.Add(key))
            {
                throw new ArgumentException("The binding set does not match the current pipeline reflection.", nameof(bindings));
            }

            var usage = GetTextureUsage(binding.Kind, access);

            if (!HasTextureAccess(binding.Resource, binding.Range, usage))
            {
                throw new InvalidOperationException($"A {binding.Kind} binding must fit a {usage} range declared by this pass.");
            }
        }

        var newUseLeases = new List<GraphicsResourceOwner.UseLease>(bindings.Samplers.Length);

        try
        {
            foreach (var binding in bindings.Samplers)
            {
                var key = (binding.Group, ShaderResourceKind.Sampler, binding.Binding);

                if (!expectedBindings.TryGetValue(key, out var access) || !actualBindings.Add(key))
                {
                    throw new ArgumentException("The binding set does not match the current pipeline reflection.", nameof(bindings));
                }

                if (access != ShaderResourceAccess.Read)
                {
                    throw new ArgumentException("Sampler bindings must be read-only.", nameof(bindings));
                }

                if (!ReferenceEquals(binding.Sampler.ResourceOwner.Device, _device)
                    || (binding.Sampler.DeviceEpoch != _device.DeviceEpoch))
                {
                    throw new ArgumentException("A sampler binding must belong to this GraphicsDevice generation.", nameof(bindings));
                }

                newUseLeases.Add(binding.Sampler.ResourceOwner.AcquireUse(nameof(bindings)));
            }

            if (expectedBindings.Count != actualBindings.Count)
            {
                throw new ArgumentException("The binding set must provide every reflected resource exactly once.", nameof(bindings));
            }

            _useLeases.AddRange(newUseLeases);
            newUseLeases.Clear();
            _isShaderBindingsSet = true;
            _commands.Add(new RecordedGraphicsCommand(GraphicsCommandKind.SetBindings, bindings: bindings));
        }
        finally
        {
            foreach (var useLease in newUseLeases)
            {
                useLease.Dispose();
            }
        }
    }

    internal void SetIndexBuffer(IndexBufferBinding binding)
    {
        EnsureActive();
        EnsurePassKind(CommandRecorderPassKind.Raster);
        _ = RequireRasterPipeline();

        if (!HasBufferAccess(binding.Buffer, binding.Range, GraphBufferUsage.IndexRead))
        {
            throw new InvalidOperationException("The index-buffer binding must fit an IndexRead range declared by this pass.");
        }

        _indexBuffer = binding;
        _commands.Add(new RecordedGraphicsCommand(GraphicsCommandKind.SetIndexBuffer, indexBuffer: binding));
    }

    internal void SetPipeline(GpuPipelineHandle pipeline)
    {
        EnsureActive();
        EnsurePassCanBindPipeline();
        var (layout, useLease) = _device.AcquirePipelineForRecording(pipeline);

        try
        {
            if (layout.IsCompute != (_passKind == CommandRecorderPassKind.Compute))
            {
                throw new InvalidOperationException("The pipeline kind is incompatible with the current pass.");
            }

            if (!layout.IsCompute
                && (!layout.ColorTargetFormats.AsSpan().SequenceEqual(_colorTargetFormats.AsSpan())
                    || (layout.DepthTargetFormat != _depthTargetFormat)))
            {
                throw new InvalidOperationException("The graphics pipeline target formats do not match the current raster pass.");
            }

            _useLeases.Add(useLease);
            _pipelineLayout = layout;
            _isShaderBindingsSet = false;
            _indexBuffer = null;
            _vertexBuffer = null;
            _commands.Add(new RecordedGraphicsCommand(GraphicsCommandKind.SetPipeline, pipeline: pipeline));
        }
        catch
        {
            useLease.Dispose();
            throw;
        }
    }

    internal void SetVertexBuffer(VertexBufferBinding binding)
    {
        EnsureActive();
        EnsurePassKind(CommandRecorderPassKind.Raster);
        var pipelineLayout = RequireRasterPipeline();

        if ((binding.Slot != 0)
            || (pipelineLayout.VertexStride == 0)
            || (binding.Stride != pipelineLayout.VertexStride)
            || ((binding.Range.ByteLength % binding.Stride) != 0))
        {
            throw new ArgumentException("The vertex-buffer binding does not match the pipeline's single vertex input layout.", nameof(binding));
        }

        if (!HasBufferAccess(binding.Buffer, binding.Range, GraphBufferUsage.VertexRead))
        {
            throw new InvalidOperationException("The vertex-buffer binding must fit a VertexRead range declared by this pass.");
        }

        _vertexBuffer = binding;
        _commands.Add(new RecordedGraphicsCommand(GraphicsCommandKind.SetVertexBuffer, vertexBuffer: binding));
    }

    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(_isDisposed || _isCompleted, this);

        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("A command recorder is valid only on its creating graphics-owner thread.");
        }

        if (_device.Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice stopped accepting recording work.");
        }
    }

    private void EnsurePassCanBindPipeline()
    {
        if (_passKind is not (CommandRecorderPassKind.Compute or CommandRecorderPassKind.Raster))
        {
            throw new InvalidOperationException("Pipelines can be bound only by compute and raster passes.");
        }
    }

    private void EnsurePassKind(CommandRecorderPassKind expectedKind)
    {
        if (_passKind != expectedKind)
        {
            throw new InvalidOperationException($"The operation is not legal in a {_passKind} pass.");
        }
    }

    private bool HasBufferAccess(GraphBufferVersion version, GraphBufferRange range, GraphBufferUsage usage)
    {
        foreach (var access in _declaredAccesses)
        {
            if (!access.IsTexture
                && (access.BufferUsage == usage)
                && ReferenceEquals(access.BufferVersion, version)
                && range.IsWithin(access.BufferRange))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasTextureAccess(GraphTextureVersion version, GraphTextureRange range, GraphTextureUsage usage)
    {
        foreach (var access in _declaredAccesses)
        {
            if (access.IsTexture
                && (access.TextureUsage == usage)
                && ReferenceEquals(access.TextureVersion, version)
                && range.IsWithin(access.TextureRange))
            {
                return true;
            }
        }

        return false;
    }

    private void ReleaseUseLeases()
    {
        for (var index = _useLeases.Count - 1; index >= 0; index--)
        {
            _useLeases[index].Dispose();
        }

        _useLeases.Clear();
    }

    private PipelineBindingLayout RequirePipelineLayout()
        => _pipelineLayout
            ?? throw new InvalidOperationException("A pipeline must be bound before recording this command.");

    private PipelineBindingLayout RequireRasterPipeline()
    {
        var pipelineLayout = RequirePipelineLayout();

        if (pipelineLayout.IsCompute)
        {
            throw new InvalidOperationException("A raster command requires a graphics pipeline.");
        }

        return pipelineLayout;
    }

    private void ValidateShaderBindings(PipelineBindingLayout pipelineLayout)
    {
        if (HasReflectedResources(pipelineLayout) && !_isShaderBindingsSet)
        {
            throw new InvalidOperationException("The current pipeline's reflected resources must be bound before shader work.");
        }
    }
}
