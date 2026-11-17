// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Graphics;

internal sealed class SkinnedNormalDiagnosticRecordingState
{
    private readonly int[] _boundResourceEntryIndices = new int[4];
    private int _barrierCount;
    private int _boundStorageUseCount;
    private int _dispatchCount;
    private int _geometryPendingCount;
    private bool _isBindingSetOpen;
    private bool _isBindingsSet;
    private bool _isPipelineSet;
    private int _logicalPendingWriteCount;
    private bool _paletteWritePending;
    private readonly bool[] _pendingSingletonPresent;
    private readonly StorageResourceUse[] _pendingSingletonUses;
    private readonly StorageResourceUse[] _pendingWorkWrites;
    private int _pendingWriteHighWater;
    private long _previousWorkPacketEnd;
    private readonly SkinnedNormalDiagnosticRecordingRecipe _recipe;
    private bool _resultWritePending;
    private int _workWriteCount;

    internal SkinnedNormalDiagnosticRecordingState(SkinnedNormalDiagnosticRecordingRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);

        _recipe = recipe;
        _pendingSingletonUses = new StorageResourceUse[checked(recipe.GeometryCount + 2)];
        _pendingSingletonPresent = new bool[_pendingSingletonUses.Length];
        _pendingWorkWrites = new StorageResourceUse[recipe.DrawCount];
        _previousWorkPacketEnd = recipe.WorkBufferRange.ByteOffset;
    }

    internal SkinnedNormalDiagnosticRecordingRecipe Recipe => _recipe;

    private static RecordedGraphicsCommand CreateStorageBarrier(
        StorageResourceUse pendingWrite,
        StorageResourceUse currentUse)
    {
        if (pendingWrite.BufferVersion is not GraphBufferVersion bufferVersion)
        {
            throw new InvalidOperationException("A diagnostic storage write lost its graph buffer version.");
        }

        return new RecordedGraphicsCommand(
            GraphicsCommandKind.StorageBarrier,
            storageBarrierBuffer: bufferVersion,
            storageBarrierBufferRange: bufferVersion.Range,
            storageBarrierDestinationReads: currentUse.Reads,
            storageBarrierDestinationWrites: currentUse.Writes,
            storageBarrierSourceReads: pendingWrite.Reads,
            storageBarrierSourceWrites: pendingWrite.Writes);
    }

    internal void BeginBindingSet(BindingSet bindingSet)
    {
        if (!_isPipelineSet || _isBindingSetOpen || _isBindingsSet)
        {
            throw new InvalidOperationException("The diagnostic binding set is out of sequence.");
        }

        _recipe.BeginBindingSet(_dispatchCount, bindingSet);
        _boundStorageUseCount = 0;
        _isBindingSetOpen = true;
    }

    internal void CompleteBindingSet(int storageUseCount)
    {
        if (!_isBindingSetOpen || (storageUseCount != 4) || (_boundStorageUseCount != 4))
        {
            throw new InvalidOperationException("The diagnostic binding set must contain all four witnessed storage buffers.");
        }

        _isBindingSetOpen = false;
        _isBindingsSet = true;
    }

    internal void MarkDispatchRecorded()
    {
        if (!_isBindingsSet)
        {
            throw new InvalidOperationException("The diagnostic dispatch has no complete binding set.");
        }

        _dispatchCount++;
        _isPipelineSet = false;
        _isBindingsSet = false;
    }

    internal void RecordStorageBarriers(
        CommandRecorderState recorder,
        ReadOnlySpan<StorageResourceUse> boundStorageUses)
    {
        ArgumentNullException.ThrowIfNull(recorder);

        if (!_isBindingsSet
            || (_boundStorageUseCount != 4)
            || (boundStorageUses.Length < 4)
            || (_dispatchCount >= _recipe.DrawCount))
        {
            throw new InvalidOperationException("The diagnostic storage hazards are out of sequence.");
        }

        var geometryEntryIndex = _boundResourceEntryIndices[0];
        var paletteEntryIndex = _boundResourceEntryIndices[1];
        var workEntryIndex = _boundResourceEntryIndices[2];
        var resultEntryIndex = _boundResourceEntryIndices[3];

        if ((geometryEntryIndex < 0)
            || (geometryEntryIndex >= _recipe.GeometryCount)
            || (paletteEntryIndex != _recipe.PaletteEntryIndex)
            || (workEntryIndex != _recipe.WorkEntryIndex)
            || (resultEntryIndex != _recipe.ResultEntryIndex))
        {
            throw new InvalidOperationException("The diagnostic storage bindings changed their witnessed resource roles.");
        }

        var geometryUse = boundStorageUses[0];
        var paletteUse = boundStorageUses[1];
        var workUse = boundStorageUses[2];
        var resultUse = boundStorageUses[3];
        var workRange = workUse.BufferRange;

        if ((workRange.ByteOffset < _previousWorkPacketEnd)
            || !workRange.IsWithin(_recipe.WorkBufferRange))
        {
            throw new InvalidOperationException("Diagnostic work packets must remain disjoint and within the frozen work resource.");
        }

        RecordSingletonBarrier(recorder, geometryEntryIndex, geometryUse);
        RecordSingletonBarrier(recorder, _recipe.GeometryCount, paletteUse);
        RecordSingletonBarrier(recorder, _recipe.GeometryCount + 1, resultUse);

        SetSingletonWrite(geometryEntryIndex, geometryUse);
        SetSingletonWrite(_recipe.GeometryCount, paletteUse);
        SetSingletonWrite(_recipe.GeometryCount + 1, resultUse);

        if (_workWriteCount >= _pendingWorkWrites.Length)
        {
            throw new InvalidOperationException("The diagnostic recorder exceeded its exact packet-write capacity.");
        }

        _pendingWorkWrites[_workWriteCount++] = workUse;
        _previousWorkPacketEnd = workRange.EndExclusive;
        _logicalPendingWriteCount = checked(
            _workWriteCount
            + _geometryPendingCount
            + (_paletteWritePending ? 1 : 0)
            + (_resultWritePending ? 1 : 0));
        _pendingWriteHighWater = Math.Max(_pendingWriteHighWater, _logicalPendingWriteCount);
    }

    internal void Release()
        => _recipe.Release();

    internal void SetBoundStorageUse(int storageUseIndex, int resourceEntryIndex)
    {
        if (!_isBindingSetOpen
            || (storageUseIndex < 0)
            || (storageUseIndex >= _boundResourceEntryIndices.Length)
            || (resourceEntryIndex < 0)
            || (resourceEntryIndex >= (_recipe.GeometryCount + 3)))
        {
            throw new InvalidOperationException("The diagnostic binding did not match its resource index.");
        }

        _boundResourceEntryIndices[storageUseIndex] = resourceEntryIndex;
        _boundStorageUseCount = Math.Max(_boundStorageUseCount, storageUseIndex + 1);
    }

    internal void SetPipeline(
        GpuPipelineHandle pipeline,
        PipelineBindingLayout layout,
        System.Collections.Generic.Dictionary<
            (ShaderBindingGroup Group, ShaderResourceKind Kind, int Binding),
            ShaderResourceAccess> expectedBindings)
    {
        if (_isPipelineSet || _isBindingSetOpen || _isBindingsSet || (_dispatchCount >= _recipe.DrawCount))
        {
            throw new InvalidOperationException("The diagnostic pipeline command is out of sequence.");
        }

        _recipe.ValidatePipeline(pipeline, layout, expectedBindings);
        _isPipelineSet = true;
    }

    internal int ValidateBufferBinding(BufferBinding binding, ShaderResourceAccess access)
    {
        if (!_isBindingSetOpen || (access != ShaderResourceAccess.ReadWrite))
        {
            throw new InvalidOperationException("The diagnostic binding has no read-write access witness.");
        }

        return _recipe.ValidateBufferBinding(binding);
    }

    internal void ValidateComplete(
        RenderPassId passId,
        uint deviceEpoch,
        int commandCount,
        int useLeaseCount,
        int pendingWriteCount,
        ImmutableArray<RecordedGraphicsCommand> commands)
    {
        if (_isPipelineSet
            || _isBindingSetOpen
            || _isBindingsSet
            || (_dispatchCount != _recipe.DrawCount)
            || (_logicalPendingWriteCount != _recipe.ExpectedPendingWriteCount)
            || (_pendingWriteHighWater != _recipe.ExpectedPendingWriteCount)
            || (_workWriteCount != _recipe.DrawCount)
            || (pendingWriteCount != 0))
        {
            throw new InvalidOperationException("The diagnostic recorder did not complete its exact closed-shape sequence.");
        }

        _recipe.ValidateRecordedPass(
            passId,
            deviceEpoch,
            commandCount,
            useLeaseCount,
            _logicalPendingWriteCount,
            _pendingWriteHighWater,
            _workWriteCount,
            _barrierCount,
            _geometryPendingCount,
            _paletteWritePending,
            _resultWritePending,
            commands);

        for (var index = 0; index < _workWriteCount; index++)
        {
            if (!_recipe.IsExpectedWorkWrite(index, _pendingWorkWrites[index]))
            {
                throw new InvalidOperationException("A diagnostic packet write changed its frozen range or version.");
            }
        }
    }

    internal void ValidateDispatch(DispatchSize size)
    {
        if (!_isBindingsSet || (size != new DispatchSize(1, 1, 1)))
        {
            throw new InvalidOperationException("The diagnostic pass requires one positive one-group dispatch per frozen draw.");
        }
    }

    private void RecordSingletonBarrier(
        CommandRecorderState recorder,
        int pendingSlot,
        StorageResourceUse currentUse)
    {
        if (!_pendingSingletonPresent[pendingSlot])
        {
            return;
        }

        var pendingWrite = _pendingSingletonUses[pendingSlot];

        if (!pendingWrite.HasHazardWith(currentUse))
        {
            throw new InvalidOperationException("A diagnostic resource slot violated its full-range read-write hazard contract.");
        }

        recorder.AddCommand(CreateStorageBarrier(pendingWrite, currentUse));
        _pendingSingletonPresent[pendingSlot] = false;
        _logicalPendingWriteCount--;

        if (pendingSlot < _recipe.GeometryCount)
        {
            _geometryPendingCount--;
        }
        else if (pendingSlot == _recipe.GeometryCount)
        {
            _paletteWritePending = false;
        }
        else
        {
            _resultWritePending = false;
        }

        _barrierCount++;
    }

    private void SetSingletonWrite(int pendingSlot, StorageResourceUse storageUse)
    {
        if (_pendingSingletonPresent[pendingSlot])
        {
            throw new InvalidOperationException("A diagnostic singleton write was not ordered before its replacement.");
        }

        _pendingSingletonUses[pendingSlot] = storageUse;
        _pendingSingletonPresent[pendingSlot] = true;
        _logicalPendingWriteCount++;

        if (pendingSlot < _recipe.GeometryCount)
        {
            _geometryPendingCount++;
        }
        else if (pendingSlot == _recipe.GeometryCount)
        {
            _paletteWritePending = true;
        }
        else
        {
            _resultWritePending = true;
        }
    }
}
