// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace TerraFX.Graphics;

internal sealed class SkinnedNormalDiagnosticRecordingRecipe
{
    private int[] _accessIndexScratch;
    private ImmutableArray<BindingSet> _bindingSets;
    private ImmutableArray<GraphResourceAccess> _declaredAccesses;
    private readonly uint _deviceEpoch;
    private readonly int _drawCount;
    private readonly int _expectedBarrierCount;
    private readonly int _expectedCommandCount;
    private readonly int _expectedPendingWriteCount;
    private readonly int _geometryCount;
    private bool[] _geometrySeenScratch;
    private readonly RenderPassId _passId;
    private readonly GpuPipelineHandle _pipeline;
    private readonly GraphicsRecordingUseAdmission _recordingUseAdmission;
    private DiagnosticResourceEntry[] _resourceEntries;
    private ImmutableArray<RecordedGraphicsCommand> _verifiedCommands;

    internal SkinnedNormalDiagnosticRecordingRecipe(
        RenderPassId passId,
        uint deviceEpoch,
        GpuPipelineHandle pipeline,
        BindingSet[] bindingSets,
        GraphBufferVersion[] geometryBuffers,
        GraphBufferVersion bonePaletteBuffer,
        GraphBufferVersion workBuffer,
        GraphBufferVersion resultBuffer,
        long workPacketAlignment,
        ImmutableArray<GraphResourceAccess> declaredAccesses,
        CommandRecorderRecordingCapacity recordingCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfZero(deviceEpoch);
        ArgumentOutOfRangeException.ThrowIfZero(workPacketAlignment);
        ArgumentNullException.ThrowIfNull(bindingSets);
        ArgumentNullException.ThrowIfNull(geometryBuffers);
        ArgumentNullException.ThrowIfNull(bonePaletteBuffer);
        ArgumentNullException.ThrowIfNull(workBuffer);
        ArgumentNullException.ThrowIfNull(resultBuffer);

        if (!passId.IsValid || !pipeline.IsValid)
        {
            throw new ArgumentException("A diagnostic recording recipe requires valid pass and pipeline identities.");
        }

        if (declaredAccesses.IsDefault)
        {
            throw new ArgumentException("A diagnostic recording recipe requires initialized pass accesses.", nameof(declaredAccesses));
        }

        if ((bindingSets.Length == 0)
            || (geometryBuffers.Length == 0)
            || (geometryBuffers.Length > bindingSets.Length))
        {
            throw new ArgumentException("A diagnostic recording recipe requires draws and selected geometry.", nameof(bindingSets));
        }

        _drawCount = bindingSets.Length;
        _recordingUseAdmission = GraphicsRecordingUseAdmission.Create(_drawCount, 32L * 1024 * 1024);
        _geometryCount = geometryBuffers.Length;
        _expectedBarrierCount = checked((3 * _drawCount) - _geometryCount - 2);
        _expectedCommandCount = checked((6 * _drawCount) - _geometryCount - 2);
        _expectedPendingWriteCount = checked(_drawCount + _geometryCount + 2);
        _pipeline = pipeline;
        _passId = passId;
        _deviceEpoch = deviceEpoch;
        _bindingSets = [.. bindingSets];
        _declaredAccesses = declaredAccesses;
        _verifiedCommands = [];
        _geometrySeenScratch = new bool[_geometryCount];
        _resourceEntries = new DiagnosticResourceEntry[checked(_geometryCount + 3)];

        if ((recordingCapacity.CommandCount != _expectedCommandCount)
            || (recordingCapacity.UseLeaseCount != _drawCount)
            || (recordingCapacity.PendingStorageWriteCount != _expectedPendingWriteCount)
            || (recordingCapacity.BindingCount != 4))
        {
            throw new ArgumentException("The diagnostic recipe and its recording capacity disagree.", nameof(recordingCapacity));
        }

        if (declaredAccesses.Length != checked(2 * _resourceEntries.Length))
        {
            throw new ArgumentException("The diagnostic pass must declare one storage read-write pair per resource.", nameof(declaredAccesses));
        }

        for (var index = 0; index < _geometryCount; index++)
        {
            var geometryBuffer = geometryBuffers[index];
            ArgumentNullException.ThrowIfNull(geometryBuffer);

            if (geometryBuffer.DeviceEpoch != deviceEpoch)
            {
                throw new ArgumentException("Diagnostic geometry belongs to another device epoch.", nameof(geometryBuffers));
            }

            _resourceEntries[index] = new DiagnosticResourceEntry(geometryBuffer);
        }

        Array.Sort(_resourceEntries, 0, _geometryCount, DiagnosticResourceEntryComparer.Instance);

        for (var index = 1; index < _geometryCount; index++)
        {
            if (_resourceEntries[index - 1].Version.ResourceId == _resourceEntries[index].Version.ResourceId)
            {
                throw new ArgumentException("Diagnostic geometry resources must have distinct identities.", nameof(geometryBuffers));
            }
        }

        ValidateFixedResource(bonePaletteBuffer, deviceEpoch, nameof(bonePaletteBuffer));
        ValidateFixedResource(workBuffer, deviceEpoch, nameof(workBuffer));
        ValidateFixedResource(resultBuffer, deviceEpoch, nameof(resultBuffer));

        if ((bonePaletteBuffer.ResourceId == workBuffer.ResourceId)
            || (bonePaletteBuffer.ResourceId == resultBuffer.ResourceId)
            || (workBuffer.ResourceId == resultBuffer.ResourceId)
            || HasGeometryResource(bonePaletteBuffer.ResourceId)
            || HasGeometryResource(workBuffer.ResourceId)
            || HasGeometryResource(resultBuffer.ResourceId))
        {
            throw new ArgumentException("Diagnostic fixed resources cannot alias each other or selected geometry.");
        }

        _resourceEntries[_geometryCount] = new DiagnosticResourceEntry(bonePaletteBuffer);
        _resourceEntries[_geometryCount + 1] = new DiagnosticResourceEntry(workBuffer);
        _resourceEntries[_geometryCount + 2] = new DiagnosticResourceEntry(resultBuffer);
        _accessIndexScratch = new int[declaredAccesses.Length];

        for (var index = 0; index < _accessIndexScratch.Length; index++)
        {
            _accessIndexScratch[index] = index;
        }

        Array.Sort(_accessIndexScratch, new GraphResourceAccessIndexComparer(declaredAccesses));
        ValidateAccessPairs(
            declaredAccesses,
            _accessIndexScratch,
            requireDiagnosticPass: true,
            assignWitnesses: true);
        ValidateBindingSets(workPacketAlignment);
    }

    internal int DrawCount => _drawCount;

    internal int ExpectedBarrierCount => _expectedBarrierCount;

    internal int ExpectedCommandCount => _expectedCommandCount;

    internal int ExpectedPendingWriteCount => _expectedPendingWriteCount;

    internal int GeometryCount => _geometryCount;

    internal int PaletteEntryIndex => _geometryCount;

    internal RenderPassId PassId => _passId;

    internal GpuPipelineHandle Pipeline => _pipeline;

    internal GraphicsRecordingUseAdmission RecordingUseAdmission => _recordingUseAdmission;

    internal int ResultEntryIndex => _geometryCount + 2;

    internal GraphBufferRange WorkBufferRange
    {
        get
        {
            EnsureAvailable();
            return _resourceEntries[WorkEntryIndex].Range;
        }
    }

    internal int WorkEntryIndex => _geometryCount + 1;

    private static void ValidateBarrier(RecordedGraphicsCommand command, DiagnosticResourceEntry entry)
    {
        if ((command.Kind != GraphicsCommandKind.StorageBarrier)
            || !ReferenceEquals(command.StorageBarrierBuffer, entry.Version)
            || (command.StorageBarrierBufferRange != entry.Version.Range)
            || (command.StorageBarrierTexture is not null)
            || !command.StorageBarrierSourceReads
            || !command.StorageBarrierSourceWrites
            || !command.StorageBarrierDestinationReads
            || !command.StorageBarrierDestinationWrites)
        {
            throw new InvalidOperationException("The diagnostic storage barrier changed its resource, range, order, or access flags.");
        }
    }

    private static void ValidateFixedResource(GraphBufferVersion version, uint deviceEpoch, string parameterName)
    {
        if ((version.DeviceEpoch != deviceEpoch)
            || (version.Range != new GraphBufferRange(0, version.Description.ByteLength)))
        {
            throw new ArgumentException("A diagnostic fixed resource must use its full buffer version on the selected device.", parameterName);
        }
    }

    internal void BeginBindingSet(int drawIndex, BindingSet bindingSet)
    {
        EnsureAvailable();

        if ((drawIndex < 0)
            || (drawIndex >= _drawCount)
            || !ReferenceEquals(bindingSet, _bindingSets[drawIndex]))
        {
            throw new InvalidOperationException("The diagnostic recording changed its frozen binding-set sequence.");
        }
    }

    internal bool IsExpectedWorkWrite(int drawIndex, StorageResourceUse workUse)
    {
        EnsureAvailable();

        if ((drawIndex < 0) || (drawIndex >= _drawCount))
        {
            return false;
        }

        var binding = _bindingSets[drawIndex].Buffers[2];
        return ReferenceEquals(workUse.BufferVersion, binding.Resource)
            && (workUse.BufferRange == binding.Range);
    }

    internal void Release()
    {
        _bindingSets = [];
        _declaredAccesses = [];
        _verifiedCommands = [];
        _resourceEntries = [];
        _accessIndexScratch = [];
        _geometrySeenScratch = [];
    }

    internal int ValidateBufferBinding(BufferBinding binding)
    {
        EnsureAvailable();
        ArgumentNullException.ThrowIfNull(binding);

        if ((binding.Group != ShaderBindingGroup.Frame)
            || (binding.Kind != ShaderResourceKind.StorageBuffer)
            || (binding.Binding < 0)
            || (binding.Binding > 3))
        {
            throw new InvalidOperationException("A diagnostic binding does not match the fixed Frame storage-buffer layout.");
        }

        var entryIndex = binding.Binding switch {
            0 => FindGeometryEntry(binding.Resource.ResourceId),
            1 => _geometryCount,
            2 => _geometryCount + 1,
            3 => _geometryCount + 2,
            _ => throw new InvalidOperationException("A diagnostic binding has an unsupported index."),
        };
        var entry = _resourceEntries[entryIndex];

        if (!ReferenceEquals(binding.Resource, entry.Version)
            || ((binding.Binding != 2) && (binding.Range != entry.Range))
            || ((binding.Binding == 2) && !binding.Range.IsWithin(entry.Range)))
        {
            throw new InvalidOperationException("A diagnostic binding changed its witnessed graph version or range.");
        }

        ValidateAccessWitness(entry);
        return entryIndex;
    }

    internal void ValidateNativeSubmission(
        RenderPassId passId,
        uint deviceEpoch,
        ImmutableArray<GraphResourceAccess> submissionAccesses,
        ImmutableArray<RecordedGraphicsCommand> commands)
    {
        EnsureAvailable();

        if ((passId != _passId)
            || (deviceEpoch != _deviceEpoch)
            || submissionAccesses.IsDefault
            || commands.IsDefault
            || _verifiedCommands.IsDefaultOrEmpty
            || (commands != _verifiedCommands))
        {
            throw new InvalidOperationException("The native diagnostic witness does not match its completed recording.");
        }

        var selectedAccessCount = 0;

        for (var accessIndex = 0; accessIndex < submissionAccesses.Length; accessIndex++)
        {
            if (submissionAccesses[accessIndex].PassId != passId)
            {
                continue;
            }

            if (selectedAccessCount >= _accessIndexScratch.Length)
            {
                throw new InvalidOperationException("The native diagnostic pass has unexpected graph accesses.");
            }

            _accessIndexScratch[selectedAccessCount++] = accessIndex;
        }

        if (selectedAccessCount != _accessIndexScratch.Length)
        {
            throw new InvalidOperationException("The native diagnostic pass lost one or more graph accesses.");
        }

        Array.Sort(_accessIndexScratch, new GraphResourceAccessIndexComparer(submissionAccesses));
        ValidateAccessPairs(
            submissionAccesses,
            _accessIndexScratch,
            requireDiagnosticPass: true,
            assignWitnesses: false);
        ValidateCompletedCommands(commands);
    }

    internal void ValidatePipeline(
        GpuPipelineHandle pipeline,
        PipelineBindingLayout layout,
        Dictionary<(ShaderBindingGroup Group, ShaderResourceKind Kind, int Binding), ShaderResourceAccess> expectedBindings)
    {
        EnsureAvailable();
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(expectedBindings);

        if ((pipeline != _pipeline)
            || !layout.IsCompute
            || (expectedBindings.Count != 4))
        {
            throw new InvalidOperationException("The diagnostic pipeline changed from its admitted compute layout.");
        }

        for (var bindingIndex = 0; bindingIndex < 4; bindingIndex++)
        {
            if (!expectedBindings.TryGetValue(
                (ShaderBindingGroup.Frame, ShaderResourceKind.StorageBuffer, bindingIndex),
                out var access)
                || (access != ShaderResourceAccess.ReadWrite))
            {
                throw new InvalidOperationException("The diagnostic pipeline must expose exactly four Frame storage read-write bindings.");
            }
        }
    }

    internal void ValidateRecordedPass(
        RenderPassId passId,
        uint deviceEpoch,
        int commandCount,
        int useLeaseCount,
        int logicalPendingWriteCount,
        int pendingWriteHighWater,
        int workWriteCount,
        int barrierCount,
        int occupiedGeometryCount,
        bool paletteWritePending,
        bool resultWritePending,
        ImmutableArray<RecordedGraphicsCommand> commands)
    {
        EnsureAvailable();

        if ((passId != _passId)
            || (deviceEpoch != _deviceEpoch)
            || (commandCount != _expectedCommandCount)
            || (useLeaseCount != _drawCount)
            || (logicalPendingWriteCount != _expectedPendingWriteCount)
            || (pendingWriteHighWater != _expectedPendingWriteCount)
            || (workWriteCount != _drawCount)
            || (barrierCount != _expectedBarrierCount)
            || (occupiedGeometryCount != _geometryCount)
            || !paletteWritePending
            || !resultWritePending)
        {
            throw new InvalidOperationException("The diagnostic recording did not match its frozen command, use, hazard, and resource counts.");
        }

        ValidateCompletedCommands(commands);
        _recordingUseAdmission.ValidateComplete();
        _verifiedCommands = commands;
    }

    internal void ValidateRecordingInputs(
        RenderPassId passId,
        uint deviceEpoch,
        ImmutableArray<GraphResourceAccess> declaredAccesses,
        CommandRecorderRecordingCapacity recordingCapacity)
    {
        EnsureAvailable();

        if ((passId != _passId)
            || (deviceEpoch != _deviceEpoch)
            || (declaredAccesses != _declaredAccesses)
            || (recordingCapacity.CommandCount != _expectedCommandCount)
            || (recordingCapacity.UseLeaseCount != _drawCount)
            || (recordingCapacity.PendingStorageWriteCount != _expectedPendingWriteCount)
            || (recordingCapacity.BindingCount != 4))
        {
            throw new InvalidOperationException("The diagnostic recipe changed before its recording request was admitted.");
        }
    }

    private void EnsureAvailable()
    {
        ObjectDisposedException.ThrowIf(
            _bindingSets.IsDefaultOrEmpty || _declaredAccesses.IsDefaultOrEmpty,
            this);
    }

    private int FindEntryIndex(ulong resourceId)
    {
        var low = 0;
        var high = _geometryCount - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var candidateId = _resourceEntries[middle].Version.ResourceId;
            var comparison = candidateId.CompareTo(resourceId);

            if (comparison == 0)
            {
                return middle;
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (_resourceEntries[_geometryCount].Version.ResourceId == resourceId)
        {
            return _geometryCount;
        }

        if (_resourceEntries[_geometryCount + 1].Version.ResourceId == resourceId)
        {
            return _geometryCount + 1;
        }

        if (_resourceEntries[_geometryCount + 2].Version.ResourceId == resourceId)
        {
            return _geometryCount + 2;
        }

        throw new InvalidOperationException("A diagnostic access names an unselected resource.");
    }

    private int FindGeometryEntry(ulong resourceId)
    {
        var low = 0;
        var high = _geometryCount - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var candidateId = _resourceEntries[middle].Version.ResourceId;
            var comparison = candidateId.CompareTo(resourceId);

            if (comparison == 0)
            {
                return middle;
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        throw new InvalidOperationException("A diagnostic draw names geometry outside its frozen resource index.");
    }

    private bool HasGeometryResource(ulong resourceId)
    {
        var low = 0;
        var high = _geometryCount - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var candidateId = _resourceEntries[middle].Version.ResourceId;
            var comparison = candidateId.CompareTo(resourceId);

            if (comparison == 0)
            {
                return true;
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return false;
    }

    private void ValidateAccessPairs(
        ImmutableArray<GraphResourceAccess> accesses,
        int[] accessIndices,
        bool requireDiagnosticPass,
        bool assignWitnesses)
    {
        var accessOffset = 0;

        while (accessOffset < accessIndices.Length)
        {
            var readIndex = accessIndices[accessOffset];
            var writeIndex = accessIndices[accessOffset + 1];
            var readAccess = accesses[readIndex];
            var writeAccess = accesses[writeIndex];
            var entryIndex = FindEntryIndex(readAccess.ResourceId);
            var entry = _resourceEntries[entryIndex];

            if ((readAccess.ResourceId != writeAccess.ResourceId)
                || readAccess.IsWrite
                || !writeAccess.IsWrite
                || readAccess.IsTexture
                || writeAccess.IsTexture
                || (requireDiagnosticPass && ((readAccess.PassId != _passId) || (writeAccess.PassId != _passId)))
                || (readAccess.DeviceEpoch != _deviceEpoch)
                || (writeAccess.DeviceEpoch != _deviceEpoch)
                || !readAccess.ContentsValid
                || (readAccess.RequiredState != GraphResourceState.StorageReadWrite)
                || (writeAccess.RequiredState != GraphResourceState.StorageReadWrite)
                || (readAccess.BufferUsage != GraphBufferUsage.StorageRead)
                || (writeAccess.BufferUsage != GraphBufferUsage.StorageWrite)
                || !ReferenceEquals(writeAccess.BufferVersion, entry.Version)
                || (readAccess.BufferRange != entry.Range)
                || (writeAccess.BufferRange != entry.Range)
                || !readAccess.IsStorageReadWritePairOf(writeAccess)
                || (assignWitnesses && ((entry.ReadAccessIndex >= 0) || (entry.WriteAccessIndex >= 0))))
            {
                throw new InvalidOperationException("A diagnostic storage resource has no exact initialized read-write access pair.");
            }

            if (assignWitnesses)
            {
                _resourceEntries[entryIndex] = entry with {
                    ReadAccessIndex = readIndex,
                    WriteAccessIndex = writeIndex,
                };
            }

            accessOffset += 2;
        }

        foreach (var entry in _resourceEntries)
        {
            if ((entry.ReadAccessIndex < 0) || (entry.WriteAccessIndex < 0))
            {
                throw new InvalidOperationException("The diagnostic resource index is missing a storage read-write witness.");
            }
        }
    }

    private void ValidateAccessWitness(DiagnosticResourceEntry entry)
    {
        var readAccess = _declaredAccesses[entry.ReadAccessIndex];
        var writeAccess = _declaredAccesses[entry.WriteAccessIndex];

        if ((readAccess.PassId != _passId)
            || (writeAccess.PassId != _passId)
            || (readAccess.DeviceEpoch != _deviceEpoch)
            || (writeAccess.DeviceEpoch != _deviceEpoch)
            || !ReferenceEquals(writeAccess.BufferVersion, entry.Version)
            || !readAccess.ContentsValid
            || !readAccess.IsStorageReadWritePairOf(writeAccess))
        {
            throw new InvalidOperationException("A diagnostic binding lost its frozen storage access witness.");
        }
    }

    private void ValidateBindingSets(long workPacketAlignment)
    {
        var previousPacketEnd = _resourceEntries[_geometryCount + 1].Range.ByteOffset;

        for (var drawIndex = 0; drawIndex < _bindingSets.Length; drawIndex++)
        {
            var bindingSet = _bindingSets[drawIndex];

            if ((bindingSet.Buffers.Length != 4)
                || !bindingSet.Textures.IsEmpty
                || !bindingSet.Samplers.IsEmpty)
            {
                throw new ArgumentException("A diagnostic binding set must contain exactly four Frame storage buffers.");
            }

            for (var bindingIndex = 0; bindingIndex < 4; bindingIndex++)
            {
                var binding = bindingSet.Buffers[bindingIndex];

                if ((binding.Group != ShaderBindingGroup.Frame)
                    || (binding.Kind != ShaderResourceKind.StorageBuffer)
                    || (binding.Binding != bindingIndex))
                {
                    throw new ArgumentException("A diagnostic binding set does not match the fixed Frame storage-buffer layout.");
                }

                var entryIndex = ValidateBufferBinding(binding);

                if (bindingIndex == 0)
                {
                    _resourceEntries[entryIndex] = _resourceEntries[entryIndex] with { SeenInBindings = true };
                }
                else if (bindingIndex == 2)
                {
                    if ((binding.Range.ByteOffset < previousPacketEnd)
                        || ((binding.Range.ByteOffset % workPacketAlignment) != 0)
                        || ((binding.Range.ByteLength % 16) != 0))
                    {
                        throw new ArgumentException("Diagnostic work packets must be aligned, nonempty, and monotonically disjoint.");
                    }

                    previousPacketEnd = binding.Range.EndExclusive;
                }
            }
        }

        for (var geometryIndex = 0; geometryIndex < _geometryCount; geometryIndex++)
        {
            if (!_resourceEntries[geometryIndex].SeenInBindings)
            {
                throw new ArgumentException("The diagnostic geometry index contains a resource not used by any draw.");
            }
        }
    }

    private void ValidateCompletedCommands(ImmutableArray<RecordedGraphicsCommand> commands)
    {
        if (commands.Length != _expectedCommandCount)
        {
            throw new InvalidOperationException("The diagnostic command array has an unexpected length.");
        }

        Array.Clear(_geometrySeenScratch);
        var commandIndex = 0;
        var barrierCount = 0;

        for (var drawIndex = 0; drawIndex < _drawCount; drawIndex++)
        {
            var pipelineCommand = commands[commandIndex++];

            if ((pipelineCommand.Kind != GraphicsCommandKind.SetPipeline)
                || (pipelineCommand.Pipeline != _pipeline)
                || (pipelineCommand.PipelineOwner?.Handle != _pipeline))
            {
                throw new InvalidOperationException("The diagnostic command array changed its pipeline sequence.");
            }

            var bindingsCommand = commands[commandIndex++];

            if ((bindingsCommand.Kind != GraphicsCommandKind.SetBindings)
                || !ReferenceEquals(bindingsCommand.Bindings, _bindingSets[drawIndex]))
            {
                throw new InvalidOperationException("The diagnostic command array changed its binding-set sequence.");
            }

            var geometryEntryIndex = ValidateBufferBinding(_bindingSets[drawIndex].Buffers[0]);

            if (_geometrySeenScratch[geometryEntryIndex])
            {
                ValidateBarrier(commands[commandIndex++], _resourceEntries[geometryEntryIndex]);
                barrierCount++;
            }

            if (drawIndex != 0)
            {
                ValidateBarrier(commands[commandIndex++], _resourceEntries[_geometryCount]);
                ValidateBarrier(commands[commandIndex++], _resourceEntries[_geometryCount + 2]);
                barrierCount += 2;
            }

            var dispatchCommand = commands[commandIndex++];

            if ((dispatchCommand.Kind != GraphicsCommandKind.Dispatch)
                || (dispatchCommand.Dispatch != new DispatchSize(1, 1, 1)))
            {
                throw new InvalidOperationException("The diagnostic command array changed its positive one-group dispatch sequence.");
            }

            _geometrySeenScratch[geometryEntryIndex] = true;
        }

        if ((commandIndex != commands.Length) || (barrierCount != _expectedBarrierCount))
        {
            throw new InvalidOperationException("The diagnostic command array changed its storage-barrier schedule.");
        }
    }

    private readonly struct DiagnosticResourceEntry(GraphBufferVersion version)
    {
        internal int ReadAccessIndex { get; init; } = -1;

        internal bool SeenInBindings { get; init; }

        internal GraphBufferRange Range { get; } = version.Range;

        internal int WriteAccessIndex { get; init; } = -1;

        internal GraphBufferVersion Version { get; } = version;
    }

    private sealed class DiagnosticResourceEntryComparer : IComparer<DiagnosticResourceEntry>
    {
        internal static DiagnosticResourceEntryComparer Instance { get; } = new();

        public int Compare(DiagnosticResourceEntry left, DiagnosticResourceEntry right)
            => left.Version.ResourceId.CompareTo(right.Version.ResourceId);
    }

    private sealed class GraphResourceAccessIndexComparer : IComparer<int>
    {
        private readonly ImmutableArray<GraphResourceAccess> _accesses;

        internal GraphResourceAccessIndexComparer(ImmutableArray<GraphResourceAccess> accesses)
        {
            _accesses = accesses;
        }

        public int Compare(int leftIndex, int rightIndex)
        {
            var left = _accesses[leftIndex];
            var right = _accesses[rightIndex];
            var resourceComparison = left.ResourceId.CompareTo(right.ResourceId);

            if (resourceComparison != 0)
            {
                return resourceComparison;
            }

            var writeComparison = left.IsWrite.CompareTo(right.IsWrite);
            return (writeComparison != 0) ? writeComparison : leftIndex.CompareTo(rightIndex);
        }
    }
}
