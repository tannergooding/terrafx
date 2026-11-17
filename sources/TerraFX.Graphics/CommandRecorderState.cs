// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace TerraFX.Graphics;

internal sealed class CommandRecorderState : IDisposable
{
    private readonly HashSet<(ShaderBindingGroup Group, ShaderResourceKind Kind, int Binding)> _actualBindingsScratch = [];
    private readonly List<(bool IsTexture, ulong ResourceId)> _barrierResourcesScratch = [];
    private int _boundStorageUseCount;
    private StorageResourceUse[] _boundStorageUses;
    private readonly ImmutableArray<TextureFormat> _colorTargetFormats;
    private readonly List<RecordedGraphicsCommand> _commands = [];
    private readonly ImmutableArray<GraphResourceAccess> _declaredAccesses;
    private readonly TextureFormat? _depthTargetFormat;
    private readonly GraphicsDevice _device;
    private readonly SkinnedNormalDiagnosticRecordingState? _diagnosticRecordingState;
    private PipelineBindingLayout? _expectedBindingsLayout;
    private readonly Dictionary<(ShaderBindingGroup Group, ShaderResourceKind Kind, int Binding), ShaderResourceAccess> _expectedBindingsScratch = [];
    private IndexBufferBinding? _indexBuffer;
    private bool _isCompleted;
    private bool _isDisposed;
    private bool _isShaderBindingsSet;
    private readonly int _ownerThreadId;
    private readonly RenderPassId _passId;
    private readonly CommandRecorderPassKind _passKind;
    private int _pendingStorageWriteHighWater;
    private readonly List<StorageResourceUse> _pendingStorageWrites = [];
    private PipelineBindingLayout? _pipelineLayout;
    private readonly CommandRecorderRecordingCapacity _recordingCapacity;
    private readonly List<GraphicsResourceOwner.UseLease> _useLeases = [];
    private VertexBufferBinding? _vertexBuffer;

    internal CommandRecorderState(
        GraphicsDevice device,
        RenderPassId passId,
        CommandRecorderPassKind passKind,
        ImmutableArray<GraphResourceAccess> declaredAccesses,
        ImmutableArray<TextureFormat> colorTargetFormats,
        TextureFormat? depthTargetFormat,
        CommandRecorderRecordingCapacity recordingCapacity = default,
        SkinnedNormalDiagnosticRecordingRecipe? diagnosticRecipe = null)
    {
        ValidatePassInputs(device, passId, passKind, declaredAccesses, colorTargetFormats, depthTargetFormat, requireOwnerThread: true);
        recordingCapacity.ValidateKnownBackingArrayMinimum();

        _colorTargetFormats = [.. colorTargetFormats];
        _ = _commands.EnsureCapacity(recordingCapacity.CommandBackingCapacity);
        _declaredAccesses = (diagnosticRecipe is null) ? [.. declaredAccesses] : declaredAccesses;
        _device = device;
        _depthTargetFormat = depthTargetFormat;

        if (diagnosticRecipe is not null)
        {
            if ((passKind != CommandRecorderPassKind.Compute) || !recordingCapacity.IsBounded)
            {
                throw new ArgumentException("A diagnostic recording recipe requires a bounded compute pass.", nameof(diagnosticRecipe));
            }

            diagnosticRecipe.ValidateRecordingInputs(
                passId,
                device.DeviceEpoch,
                declaredAccesses,
                recordingCapacity);
            _diagnosticRecordingState = new SkinnedNormalDiagnosticRecordingState(diagnosticRecipe);
        }

        _ownerThreadId = Environment.CurrentManagedThreadId;
        _passId = passId;
        _passKind = passKind;
        _recordingCapacity = recordingCapacity;
        _ = _useLeases.EnsureCapacity(recordingCapacity.UseLeaseBackingCapacity);

        if (diagnosticRecipe is null)
        {
            _ = _pendingStorageWrites.EnsureCapacity(recordingCapacity.PendingStorageWriteCount);
        }

        _boundStorageUses = recordingCapacity.IsBounded
            ? new StorageResourceUse[recordingCapacity.BindingCount]
            : [];
        _ = _expectedBindingsScratch.EnsureCapacity(recordingCapacity.BindingCount);
        _ = _actualBindingsScratch.EnsureCapacity(recordingCapacity.BindingCount);
        _ = _barrierResourcesScratch.EnsureCapacity(recordingCapacity.BindingCount);
    }

    internal static void ValidatePassInputs(
        GraphicsDevice device,
        RenderPassId passId,
        CommandRecorderPassKind passKind,
        ImmutableArray<GraphResourceAccess> declaredAccesses,
        ImmutableArray<TextureFormat> colorTargetFormats,
        TextureFormat? depthTargetFormat,
        bool requireOwnerThread)
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

        if (requireOwnerThread && !device.IsOwnerThread)
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
    }

    private static GraphBufferUsage GetBufferUsage(ShaderResourceKind kind, ShaderResourceAccess access)
        => (kind, access) switch {
            (ShaderResourceKind.UniformBuffer, ShaderResourceAccess.Read) => GraphBufferUsage.UniformRead,
            (ShaderResourceKind.StorageBuffer, ShaderResourceAccess.Read) => GraphBufferUsage.StorageRead,
            (ShaderResourceKind.StorageBuffer, ShaderResourceAccess.Write) => GraphBufferUsage.StorageWrite,
            (ShaderResourceKind.StorageBuffer, ShaderResourceAccess.ReadWrite)
                => GraphBufferUsage.StorageWrite,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static Dictionary<(ShaderBindingGroup Group, ShaderResourceKind Kind, int Binding), ShaderResourceAccess> GetExpectedBindings(
        PipelineBindingLayout pipelineLayout)
    {
        var expectedBindings =
            new Dictionary<(ShaderBindingGroup Group, ShaderResourceKind Kind, int Binding), ShaderResourceAccess>();
        PopulateExpectedBindings(pipelineLayout, expectedBindings);
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
                => GraphTextureUsage.StorageWrite,
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

    private static void PopulateExpectedBindings(
        PipelineBindingLayout pipelineLayout,
        Dictionary<(ShaderBindingGroup Group, ShaderResourceKind Kind, int Binding), ShaderResourceAccess> expectedBindings)
    {
        expectedBindings.Clear();

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
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        if (!_isCompleted)
        {
            _diagnosticRecordingState?.Release();
        }

        ReleaseUseLeases();
        _commands.Clear();
        _commands.Capacity = 0;
        _useLeases.Capacity = 0;
    }

    internal void AddCommand(RecordedGraphicsCommand command)
    {
        if (_recordingCapacity.IsBounded && (_commands.Count >= _recordingCapacity.CommandCount))
        {
            throw new InvalidOperationException("The command recorder exceeded its preclaimed command capacity.");
        }

        _commands.Add(command);
    }

    internal RecordedPassCommandList Complete()
    {
        EnsureActive();

        if (_recordingCapacity.IsBounded
            && ((_commands.Capacity != _recordingCapacity.CommandBackingCapacity)
                || (_useLeases.Capacity != _recordingCapacity.UseLeaseBackingCapacity)))
        {
            throw new InvalidOperationException("The recorder's mutable backing capacities changed after their array footprint was established.");
        }

        ImmutableArray<RecordedGraphicsCommand> immutableCommands = default;

        if (_diagnosticRecordingState != null)
        {
            immutableCommands = [.. _commands];
            _diagnosticRecordingState.ValidateComplete(
                _passId,
                _device.DeviceEpoch,
                _commands.Count,
                _useLeases.Count,
                _pendingStorageWrites.Count,
                immutableCommands);
        }
        else if (_recordingCapacity.IsBounded
            && ((_commands.Count != _recordingCapacity.CommandCount)
                || (_useLeases.Count != _recordingCapacity.UseLeaseCount)
                || (_pendingStorageWriteHighWater != _recordingCapacity.PendingStorageWriteCount)
                || (_pendingStorageWrites.Count != _recordingCapacity.PendingStorageWriteCount)))
        {
            throw new InvalidOperationException("The diagnostic recording did not match its preclaimed command and storage capacities.");
        }

        if (immutableCommands.IsDefault)
        {
            immutableCommands = [.. _commands];
        }

        var commandList = new RecordedPassCommandList(
            _passId,
            _passKind,
            immutableCommands,
            [.. _useLeases],
            _diagnosticRecordingState?.Recipe);
        _isCompleted = true;
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

        AddCommand(new RecordedGraphicsCommand(
            GraphicsCommandKind.CopyBuffer,
            copySource: source,
            copyDestination: destination,
            sourceOffset: sourceOffset,
            destinationOffset: destinationOffset,
            byteLength: byteLength));
    }

    internal void CopyTexture(
        GraphTextureVersion source,
        GraphTextureVersion destination,
        TextureCopyRegion? region = null)
    {
        EnsureActive();
        EnsurePassKind(CommandRecorderPassKind.Copy);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNotEqual(source.DeviceEpoch, _device.DeviceEpoch, nameof(source));
        ArgumentOutOfRangeException.ThrowIfNotEqual(destination.DeviceEpoch, _device.DeviceEpoch, nameof(destination));

        if ((source.ResourceId == destination.ResourceId)
            || (source.Description.Dimension != TextureDimension.Dimension2D)
            || (destination.Description.Dimension != TextureDimension.Dimension2D)
            || (source.Range.MipCount != 1)
            || (source.Range.LayerCount != 1)
            || (destination.Range.MipCount != 1)
            || (destination.Range.LayerCount != 1)
            || (source.Range.Aspect != TextureAspect.Color)
            || (destination.Range.Aspect != TextureAspect.Color))
        {
            throw new NotSupportedException("A graph texture copy requires distinct 2D color subresources with one mip and layer.");
        }

        if ((source.Description.Usage & TextureUsage.CopySource) == 0)
        {
            throw new ArgumentException("The source graph texture does not permit copy-source use.", nameof(source));
        }

        if ((destination.Description.Usage & TextureUsage.CopyDestination) == 0)
        {
            throw new ArgumentException("The destination graph texture does not permit copy-destination use.", nameof(destination));
        }

        if (source.Description.Format != destination.Description.Format)
        {
            throw new ArgumentException("Graph texture copies require matching formats.", nameof(destination));
        }

        var sourceSubresource = new TextureSubresource(
            source.Range.FirstMip,
            source.Range.FirstLayer,
            source.Range.Aspect);
        var destinationSubresource = new TextureSubresource(
            destination.Range.FirstMip,
            destination.Range.FirstLayer,
            destination.Range.Aspect);

        var sourceExtent = GraphicsDevice.GetTextureSubresourceExtent(source.Description, sourceSubresource);
        var destinationExtent = GraphicsDevice.GetTextureSubresourceExtent(destination.Description, destinationSubresource);
        TextureCopyRegion copyRegion;

        if (region is TextureCopyRegion selectedRegion)
        {
            if (!selectedRegion.IsWithin(
                    sourceExtent.Width,
                    sourceExtent.Height,
                    destinationExtent.Width,
                    destinationExtent.Height))
            {
                throw new ArgumentOutOfRangeException(nameof(region), "The source rectangle must fit its subresource and fill the destination subresource.");
            }

            copyRegion = selectedRegion;
        }
        else
        {
            if (sourceExtent != destinationExtent)
            {
                throw new ArgumentException("Complete graph texture copies require matching mip extents.", nameof(destination));
            }

            copyRegion = new TextureCopyRegion(0, 0, sourceExtent.Width, sourceExtent.Height);
        }

        if (!HasTextureAccess(source, source.Range, GraphTextureUsage.CopySource)
            || !HasTextureAccess(destination, destination.Range, GraphTextureUsage.CopyDestination))
        {
            throw new InvalidOperationException("A texture copy must match exact source and destination ranges declared by this pass.");
        }

        AddCommand(new RecordedGraphicsCommand(
            GraphicsCommandKind.CopyTexture,
            copySourceTexture: source,
            copyDestinationTexture: destination,
            copyTextureRegion: copyRegion));
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
        _diagnosticRecordingState?.ValidateDispatch(size);

        if ((size.GroupCountX != 0) && (size.GroupCountY != 0) && (size.GroupCountZ != 0))
        {
            RecordStorageBarriers();
        }

        AddCommand(new RecordedGraphicsCommand(GraphicsCommandKind.Dispatch, dispatch: size));
        _diagnosticRecordingState?.MarkDispatchRecorded();
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

        if ((arguments.VertexCount > 0) && (arguments.InstanceCount > 0))
        {
            RecordStorageBarriers();
        }

        AddCommand(new RecordedGraphicsCommand(GraphicsCommandKind.Draw, draw: arguments));
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

        if ((arguments.IndexCount > 0) && (arguments.InstanceCount > 0))
        {
            RecordStorageBarriers();
        }

        AddCommand(new RecordedGraphicsCommand(GraphicsCommandKind.DrawIndexed, drawIndexed: arguments));
    }

    internal void SetBindings(BindingSet bindings)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(bindings);
        var pipelineLayout = RequirePipelineLayout();
        _diagnosticRecordingState?.BeginBindingSet(bindings);
        var expectedBindings = _recordingCapacity.IsBounded
            ? _expectedBindingsScratch
            : GetExpectedBindings(pipelineLayout);
        var actualBindings = _actualBindingsScratch;
        actualBindings.Clear();
        var storageUseCount = 0;

        foreach (var binding in bindings.Buffers)
        {
            var key = (binding.Group, binding.Kind, binding.Binding);

            if (!expectedBindings.TryGetValue(key, out var access) || !actualBindings.Add(key))
            {
                throw new ArgumentException("The binding set does not match the current pipeline reflection.", nameof(bindings));
            }

            var usage = GetBufferUsage(binding.Kind, access);
            var diagnosticEntryIndex = _diagnosticRecordingState?.ValidateBufferBinding(binding, access) ?? -1;

            if ((_diagnosticRecordingState is null)
                && !HasBufferAccess(
                    binding.Resource,
                    binding.Range,
                    usage,
                    requiresStorageReadWritePair: access == ShaderResourceAccess.ReadWrite))
            {
                var requiredAccess = access == ShaderResourceAccess.ReadWrite
                    ? "an explicit storage read-write version pair"
                    : $"a {usage} range";
                throw new InvalidOperationException($"A {binding.Kind} binding must fit {requiredAccess} declared by this pass.");
            }

            if (binding.Kind == ShaderResourceKind.StorageBuffer)
            {
                var storageUseIndex = storageUseCount++;
                SetBoundStorageUse(storageUseIndex, new StorageResourceUse(binding, access));
                _diagnosticRecordingState?.SetBoundStorageUse(storageUseIndex, diagnosticEntryIndex);
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

            if (!HasTextureAccess(
                binding.Resource,
                binding.Range,
                usage,
                requiresStorageReadWritePair: access == ShaderResourceAccess.ReadWrite))
            {
                var requiredAccess = access == ShaderResourceAccess.ReadWrite
                    ? "an explicit storage read-write version pair"
                    : $"a {usage} range";
                throw new InvalidOperationException($"A {binding.Kind} binding must fit {requiredAccess} declared by this pass.");
            }

            if (binding.Kind == ShaderResourceKind.StorageImage)
            {
                SetBoundStorageUse(storageUseCount++, new StorageResourceUse(binding, access));
            }
        }

        var newUseLeases = (bindings.Samplers.Length != 0)
            ? new List<GraphicsResourceOwner.UseLease>(bindings.Samplers.Length)
            : null;

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

                (newUseLeases
                    ?? throw new InvalidOperationException("A sampler binding has no staged resource-use list."))
                    .Add(binding.Sampler.ResourceOwner.AcquireUse(nameof(bindings)));
            }

            if (expectedBindings.Count != actualBindings.Count)
            {
                throw new ArgumentException("The binding set must provide every reflected resource exactly once.", nameof(bindings));
            }

            _diagnosticRecordingState?.CompleteBindingSet(storageUseCount);

            if (newUseLeases is not null)
            {
                AddUseLeases(newUseLeases);
                newUseLeases.Clear();
            }

            if (storageUseCount < _boundStorageUseCount)
            {
                Array.Clear(
                    _boundStorageUses,
                    storageUseCount,
                    _boundStorageUseCount - storageUseCount);
            }

            _boundStorageUseCount = storageUseCount;
            _isShaderBindingsSet = true;
            AddCommand(new RecordedGraphicsCommand(GraphicsCommandKind.SetBindings, bindings: bindings));
        }
        finally
        {
            if (newUseLeases is not null)
            {
                foreach (var useLease in newUseLeases)
                {
                    useLease.Dispose();
                }
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
        AddCommand(new RecordedGraphicsCommand(GraphicsCommandKind.SetIndexBuffer, indexBuffer: binding));
    }

    internal void SetPipeline(GpuPipelineHandle pipeline)
    {
        EnsureActive();
        EnsurePassCanBindPipeline();
        var (pipelineOwner, layout, useLease) = _device.AcquirePipelineForRecording(
            pipeline,
            _diagnosticRecordingState?.Recipe.RecordingUseAdmission);

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

            if (_recordingCapacity.IsBounded && !ReferenceEquals(_expectedBindingsLayout, layout))
            {
                var expectedBindings = _expectedBindingsScratch;
                PopulateExpectedBindings(layout, expectedBindings);
                _expectedBindingsLayout = layout;
            }

            _diagnosticRecordingState?.SetPipeline(pipeline, layout, _expectedBindingsScratch);

            AddUseLease(useLease);
            _pipelineLayout = layout;
            Array.Clear(_boundStorageUses, 0, _boundStorageUseCount);
            _boundStorageUseCount = 0;
            _isShaderBindingsSet = false;
            _indexBuffer = null;
            _vertexBuffer = null;
            AddCommand(new RecordedGraphicsCommand(
                GraphicsCommandKind.SetPipeline,
                pipeline: pipeline,
                pipelineOwner: pipelineOwner));
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
        AddCommand(new RecordedGraphicsCommand(GraphicsCommandKind.SetVertexBuffer, vertexBuffer: binding));
    }

    private void AddPendingStorageWrite(StorageResourceUse storageUse)
    {
        if (_recordingCapacity.IsBounded
            && (_pendingStorageWrites.Count >= _recordingCapacity.PendingStorageWriteCount))
        {
            throw new InvalidOperationException("The command recorder exceeded its preclaimed storage-write capacity.");
        }

        _pendingStorageWrites.Add(storageUse);
        _pendingStorageWriteHighWater = Math.Max(_pendingStorageWriteHighWater, _pendingStorageWrites.Count);
    }

    private void AddUseLease(GraphicsResourceOwner.UseLease useLease)
    {
        if (_recordingCapacity.IsBounded && (_useLeases.Count >= _recordingCapacity.UseLeaseCount))
        {
            throw new InvalidOperationException("The command recorder exceeded its preclaimed resource-use capacity.");
        }

        _useLeases.Add(useLease);
    }

    private void AddUseLeases(List<GraphicsResourceOwner.UseLease> useLeases)
    {
        if (_recordingCapacity.IsBounded
            && (useLeases.Count > (_recordingCapacity.UseLeaseCount - _useLeases.Count)))
        {
            throw new InvalidOperationException("The command recorder exceeded its preclaimed resource-use capacity.");
        }

        _useLeases.AddRange(useLeases);
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

    private bool HasBufferAccess(
        GraphBufferVersion version,
        GraphBufferRange range,
        GraphBufferUsage usage,
        bool requiresStorageReadWritePair = false)
    {
        foreach (var access in _declaredAccesses)
        {
            if (!access.IsTexture
                && (access.BufferUsage == usage)
                && (access.IsStorageReadWritePair == requiresStorageReadWritePair)
                && ReferenceEquals(access.BufferVersion, version)
                && range.IsWithin(access.BufferRange))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasTextureAccess(
        GraphTextureVersion version,
        GraphTextureRange range,
        GraphTextureUsage usage,
        bool requiresStorageReadWritePair = false)
    {
        foreach (var access in _declaredAccesses)
        {
            if (access.IsTexture
                && (access.TextureUsage == usage)
                && (access.IsStorageReadWritePair == requiresStorageReadWritePair)
                && ReferenceEquals(access.TextureVersion, version)
                && range.IsWithin(access.TextureRange))
            {
                return true;
            }
        }

        return false;
    }

    private void RecordStorageBarriers()
    {
        if (_diagnosticRecordingState is not null)
        {
            _diagnosticRecordingState.RecordStorageBarriers(this, _boundStorageUses.AsSpan(0, _boundStorageUseCount));
            return;
        }

        if (_boundStorageUseCount == 0)
        {
            return;
        }

        var barrierResources = _barrierResourcesScratch;
        barrierResources.Clear();

        for (var currentUseIndex = 0; currentUseIndex < _boundStorageUseCount; currentUseIndex++)
        {
            var currentUse = _boundStorageUses[currentUseIndex];

            foreach (var pendingWrite in _pendingStorageWrites)
            {
                if (!pendingWrite.HasHazardWith(currentUse))
                {
                    continue;
                }

                var key = (pendingWrite.IsTexture, pendingWrite.ResourceId);

                if (!barrierResources.Contains(key))
                {
                    barrierResources.Add(key);
                }
            }
        }

        foreach (var (isTexture, resourceId) in barrierResources)
        {
            var sourceReads = false;
            var sourceWrites = false;
            var destinationReads = false;
            var destinationWrites = false;

            foreach (var pendingWrite in _pendingStorageWrites)
            {
                if ((pendingWrite.IsTexture != isTexture) || (pendingWrite.ResourceId != resourceId))
                {
                    continue;
                }

                sourceReads |= pendingWrite.Reads;
                sourceWrites |= pendingWrite.Writes;
            }

            for (var currentUseIndex = 0; currentUseIndex < _boundStorageUseCount; currentUseIndex++)
            {
                var currentUse = _boundStorageUses[currentUseIndex];

                if ((currentUse.IsTexture != isTexture) || (currentUse.ResourceId != resourceId))
                {
                    continue;
                }

                destinationReads |= currentUse.Reads;
                destinationWrites |= currentUse.Writes;
            }

            foreach (var pendingWrite in _pendingStorageWrites)
            {
                if ((pendingWrite.IsTexture != isTexture) || (pendingWrite.ResourceId != resourceId))
                {
                    continue;
                }

                if (pendingWrite.TextureVersion is GraphTextureVersion textureVersion)
                {
                    AddCommand(new RecordedGraphicsCommand(
                        GraphicsCommandKind.StorageBarrier,
                        storageBarrierTexture: textureVersion,
                        storageBarrierTextureRange: textureVersion.Range,
                        storageBarrierDestinationReads: destinationReads,
                        storageBarrierDestinationWrites: destinationWrites,
                        storageBarrierSourceReads: sourceReads,
                        storageBarrierSourceWrites: sourceWrites));
                }
                else if (pendingWrite.BufferVersion is GraphBufferVersion bufferVersion)
                {
                    AddCommand(new RecordedGraphicsCommand(
                        GraphicsCommandKind.StorageBarrier,
                        storageBarrierBuffer: bufferVersion,
                        storageBarrierBufferRange: bufferVersion.Range,
                        storageBarrierDestinationReads: destinationReads,
                        storageBarrierDestinationWrites: destinationWrites,
                        storageBarrierSourceReads: sourceReads,
                        storageBarrierSourceWrites: sourceWrites));
                }
                else
                {
                    throw new InvalidOperationException("A storage use lost its graph resource version.");
                }

                break;
            }

            for (var pendingIndex = _pendingStorageWrites.Count - 1; pendingIndex >= 0; pendingIndex--)
            {
                var pendingWrite = _pendingStorageWrites[pendingIndex];

                if ((pendingWrite.IsTexture == isTexture) && (pendingWrite.ResourceId == resourceId))
                {
                    _pendingStorageWrites.RemoveAt(pendingIndex);
                }
            }
        }

        for (var currentUseIndex = 0; currentUseIndex < _boundStorageUseCount; currentUseIndex++)
        {
            var currentUse = _boundStorageUses[currentUseIndex];

            if (currentUse.Writes)
            {
                AddPendingStorageWrite(currentUse);
            }
        }
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

    private void SetBoundStorageUse(int index, StorageResourceUse storageUse)
    {
        if (index >= _boundStorageUses.Length)
        {
            if (_recordingCapacity.IsBounded)
            {
                throw new InvalidOperationException("The command recorder exceeded its preclaimed storage-binding capacity.");
            }

            var capacity = Math.Max(index + 1, checked(_boundStorageUses.Length * 2));
            Array.Resize(ref _boundStorageUses, capacity);
        }

        _boundStorageUses[index] = storageUse;
    }

    private void ValidateShaderBindings(PipelineBindingLayout pipelineLayout)
    {
        if (HasReflectedResources(pipelineLayout) && !_isShaderBindingsSet)
        {
            throw new InvalidOperationException("The current pipeline's reflected resources must be bound before shader work.");
        }
    }
}
