// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using TerraFX;
using TerraFX.Buffers;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

internal readonly struct RenderMeshC0NormalDiagnosticGraphPass
{
    private readonly BindingSet[] _bindings;
    private readonly RenderMeshC0NormalDiagnosticDraw[] _draws;
    private readonly GraphBufferVersion[] _geometryBuffers;
    private readonly int _vertexBufferCount;

    internal RenderMeshC0NormalDiagnosticGraphPass(
        Guid snapshotId,
        Guid worldGuid,
        long tickIndex,
        RenderMeshC0NormalDiagnosticDraw[] draws,
        GraphBufferVersion[] vertexBuffers,
        GraphBufferVersion bonePaletteBuffer,
        GraphBufferVersion workBuffer,
        GraphBufferVersion resultBuffer,
        int expectedVertexBufferCount,
        long storageBufferOffsetAlignment)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(snapshotId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(worldGuid, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(tickIndex);
        ArgumentNullException.ThrowIfNull(draws);
        ArgumentNullException.ThrowIfNull(vertexBuffers);
        ArgumentNullException.ThrowIfNull(bonePaletteBuffer);
        ArgumentNullException.ThrowIfNull(workBuffer);
        ArgumentNullException.ThrowIfNull(resultBuffer);
        ArgumentOutOfRangeException.ThrowIfZero(storageBufferOffsetAlignment);

        if (draws.Length == 0)
        {
            throw new ArgumentException("A skinned-normal diagnostic pass requires at least one draw.", nameof(draws));
        }

        foreach (var draw in draws)
        {
            if ((draw.MeshIndex < 0)
                || (draw.MeshIndex >= vertexBuffers.Length)
                || (draw.TestedVertexCount <= 0)
                || (draw.WorkByteLength <= 0)
                || (draw.ResultRow < 0))
            {
                throw new ArgumentException("A skinned-normal diagnostic draw is incomplete.", nameof(draws));
            }
        }

        SnapshotId = snapshotId;
        WorldGuid = worldGuid;
        TickIndex = tickIndex;
        _draws = draws;
        BonePaletteBuffer = bonePaletteBuffer;
        WorkBuffer = workBuffer;
        ResultBuffer = resultBuffer;
        var vertexBindings = new BufferBinding?[vertexBuffers.Length];

        foreach (var draw in draws)
        {
            if (vertexBindings[draw.MeshIndex] is null)
            {
                var vertexBuffer = vertexBuffers[draw.MeshIndex];
                vertexBindings[draw.MeshIndex] = new BufferBinding(
                    ShaderBindingGroup.Frame,
                    0,
                    ShaderResourceKind.StorageBuffer,
                    vertexBuffer,
                    new GraphBufferRange(0, vertexBuffer.Description.ByteLength));
            }
        }

        var paletteBinding = new BufferBinding(
            ShaderBindingGroup.Frame,
            1,
            ShaderResourceKind.StorageBuffer,
            bonePaletteBuffer,
            new GraphBufferRange(0, bonePaletteBuffer.Description.ByteLength));
        var resultBinding = new BufferBinding(
            ShaderBindingGroup.Frame,
            3,
            ShaderResourceKind.StorageBuffer,
            resultBuffer,
            new GraphBufferRange(0, resultBuffer.Description.ByteLength));
        _bindings = new BindingSet[draws.Length];

        for (var index = 0; index < draws.Length; index++)
        {
            var draw = draws[index];
            var workBinding = new BufferBinding(
                ShaderBindingGroup.Frame,
                2,
                ShaderResourceKind.StorageBuffer,
                workBuffer,
                new GraphBufferRange(draw.WorkOffset, draw.WorkByteLength));
            _bindings[index] = new BindingSet(
                [
                    vertexBindings[draw.MeshIndex]
                        ?? throw new InvalidOperationException("A diagnostic draw has no cached vertex-buffer binding."),
                    paletteBinding,
                    workBinding,
                    resultBinding,
                ],
                [],
                []);
        }

        var vertexBufferCount = 0;

        foreach (var vertexBinding in vertexBindings)
        {
            if (vertexBinding is not null)
            {
                vertexBufferCount++;
            }
        }

        if (vertexBufferCount != expectedVertexBufferCount)
        {
            throw new InvalidOperationException("The diagnostic geometry count changed after its plan was frozen.");
        }

        _geometryBuffers = new GraphBufferVersion[vertexBufferCount];
        var geometryBufferIndex = 0;

        foreach (var vertexBinding in vertexBindings)
        {
            if (vertexBinding is not null)
            {
                _geometryBuffers[geometryBufferIndex++] = vertexBinding.Resource;
            }
        }

        Array.Sort(_geometryBuffers, static (left, right) => left.ResourceId.CompareTo(right.ResourceId));
        _vertexBufferCount = vertexBufferCount;
        StorageBufferOffsetAlignment = storageBufferOffsetAlignment;
    }

    internal GraphBufferVersion BonePaletteBuffer { get; }

    internal int DrawCount => _draws.Length;

    internal ReadOnlySpan<RenderMeshC0NormalDiagnosticDraw> Draws => _draws;

    internal int ExpectedStorageBarrierCount
        => checked((3 * DrawCount) - _vertexBufferCount - 2);

    internal int PendingStorageWriteCapacity
        => checked(DrawCount + _vertexBufferCount + 2);

    internal int PipelineUseCount => DrawCount;

    internal BufferCompletionFootprint ReadbackFootprint
        => new(checked((nint)DrawCount * 16), alignment: 1);

    internal CommandRecorderRecordingCapacity RecordingCapacity
        => new(RecordingCommandCount, PipelineUseCount, PendingStorageWriteCapacity, bindingCount: 4);

    internal int RecordingCommandCount
        => checked((6 * DrawCount) - _vertexBufferCount - 2);

    internal GraphBufferVersion ResultBuffer { get; }

    internal Guid SnapshotId { get; }

    internal long StorageBufferOffsetAlignment { get; }

    internal long TickIndex { get; }

    internal int VertexBufferCount => _vertexBufferCount;

    internal GraphBufferVersion WorkBuffer { get; }

    internal Guid WorldGuid { get; }

    internal SkinnedNormalDiagnosticRecordingRecipe CreateRecordingRecipe(
        RenderPassId passId,
        uint deviceEpoch,
        ImmutableArray<GraphResourceAccess> declaredAccesses,
        GpuPipelineHandle pipeline)
        => new(
            passId,
            deviceEpoch,
            pipeline,
            _bindings,
            _geometryBuffers,
            BonePaletteBuffer,
            WorkBuffer,
            ResultBuffer,
            StorageBufferOffsetAlignment,
            declaredAccesses,
            RecordingCapacity);

    internal ImmutableArray<RenderSkinnedNormalDiagnostic> CreateResults(BufferReadLease<byte> readback)
    {
        ArgumentNullException.ThrowIfNull(readback);
        var expectedByteLength = checked((long)_draws.Length * 16);

        if ((expectedByteLength != ResultBuffer.Description.ByteLength)
            || (readback.Length != expectedByteLength))
        {
            throw new InvalidOperationException("The skinned-normal diagnostic readback has an unexpected byte extent.");
        }

        var decoder = new ReadbackDecodeAction(this);
        readback.UseWindow(0, checked((int)expectedByteLength), ref decoder);
        return decoder.Results;
    }

    internal void Record(scoped ref CommandRecorder recorder, RenderBuiltInSubmissionResources resources)
    {
        var pipeline = resources.SkinnedNormalDiagnosticComputePipeline
            ?? throw new InvalidOperationException("The built-in Mesh graph has no skinned-normal diagnostic pipeline.");

        for (var index = 0; index < _bindings.Length; index++)
        {
            recorder.SetPipeline(pipeline.Handle);
            recorder.SetBindings(_bindings[index]);
            recorder.Dispatch(new DispatchSize(1, 1, 1));
        }
    }

    private ImmutableArray<RenderSkinnedNormalDiagnostic> DecodeResults(ReadOnlySpan<byte> readbackBytes)
    {
        var expectedByteLength = checked((long)_draws.Length * 16);

        if (readbackBytes.Length != expectedByteLength)
        {
            throw new InvalidOperationException("The skinned-normal diagnostic readback has an unexpected byte extent.");
        }

        var results = ImmutableArray.CreateBuilder<RenderSkinnedNormalDiagnostic>(_draws.Length);

        for (var index = 0; index < _draws.Length; index++)
        {
            var draw = _draws[index];

            if ((draw.ResultRow != index) || (draw.TestedVertexCount <= 0))
            {
                throw new InvalidOperationException("The frozen skinned-normal diagnostic row mapping is invalid.");
            }

            var row = readbackBytes.Slice(checked(index * 16), 16);
            var degenerateCount = BinaryPrimitives.ReadUInt32LittleEndian(row);
            var testedCount = BinaryPrimitives.ReadUInt32LittleEndian(row[sizeof(uint)..]);
            var invalidEvaluation = BinaryPrimitives.ReadUInt32LittleEndian(row[(2 * sizeof(uint))..]);
            var reserved = BinaryPrimitives.ReadUInt32LittleEndian(row[(3 * sizeof(uint))..]);

            if ((degenerateCount > int.MaxValue)
                || (testedCount != (uint)draw.TestedVertexCount)
                || (degenerateCount > testedCount)
                || (invalidEvaluation != 0)
                || (reserved != 0))
            {
                throw new InvalidOperationException("A skinned-normal diagnostic row is incomplete or invalid.");
            }

            results.Add(new RenderSkinnedNormalDiagnostic(
                SnapshotId,
                WorldGuid,
                TickIndex,
                draw.DrawId,
                draw.TestedVertexCount,
                checked((int)degenerateCount)));
        }

        return results.MoveToImmutable();
    }

    private struct ReadbackDecodeAction(RenderMeshC0NormalDiagnosticGraphPass pass) : IAction<ReadOnlySpan<byte>>
    {
        private readonly RenderMeshC0NormalDiagnosticGraphPass _pass = pass;

        internal ImmutableArray<RenderSkinnedNormalDiagnostic> Results { get; private set; }

        public void Invoke(scoped ReadOnlySpan<byte> argument)
            => Results = _pass.DecodeResults(argument);
    }
}
