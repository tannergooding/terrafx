// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using TerraFX;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

internal readonly struct RenderMeshC0NormalDiagnosticWork
{
    // Warm .NET 11 RC3 x64 allocation probes, rechecked in Debug and Release.
    private const long BindingSetWarmConstructionTrafficBytes = 440;
    private const long BufferBindingWarmAllocationBytes = 56;
    private const long MaximumCpuBytes = 32 * 1024 * 1024;

    private RenderMeshC0NormalDiagnosticWork(
        Guid snapshotId,
        Guid worldGuid,
        long tickIndex,
        RenderMeshC0NormalDiagnosticDraw[] draws,
        bool[] meshBuffersUsed,
        int meshBufferCount,
        long storageBufferOffsetAlignment,
        byte[] workBytes,
        byte[] resultBytes)
    {
        SnapshotId = snapshotId;
        WorldGuid = worldGuid;
        TickIndex = tickIndex;
        Draws = draws;
        MeshBuffersUsed = meshBuffersUsed;
        MeshBufferCount = meshBufferCount;
        StorageBufferOffsetAlignment = storageBufferOffsetAlignment;
        WorkBytes = workBytes;
        ResultBytes = resultBytes;
    }

    internal RenderMeshC0NormalDiagnosticDraw[] Draws { get; }

    internal int MeshBufferCount { get; }

    internal bool[] MeshBuffersUsed { get; }

    internal byte[] ResultBytes { get; }

    internal Guid SnapshotId { get; }

    internal long StorageBufferOffsetAlignment { get; }

    internal long TickIndex { get; }

    internal byte[] WorkBytes { get; }

    internal Guid WorldGuid { get; }

    internal static long CalculateKnownBindingPlanConstructionBytes(int drawCount, int meshAssetCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(drawCount);
        ArgumentOutOfRangeException.ThrowIfNegative(meshAssetCount);

        if (drawCount == 0)
        {
            return 0;
        }

        if (!GraphicsRecordingUseAdmission.IsSupportedProfile)
        {
            GraphicsRecordingUseAdmission.ThrowUnavailable();
        }

        var maximumGeometryCount = Math.Min(drawCount, meshAssetCount);
        var bindingObjectCount = checked((long)drawCount + maximumGeometryCount + 2);
        var bindingSetAndInputArrayBytes = checked(
            BindingSetWarmConstructionTrafficBytes
            + CommandRecorderRecordingCapacity.CalculateArrayCharge(4, IntPtr.Size));
        var bindingObjectBytes = checked(bindingObjectCount * BufferBindingWarmAllocationBytes);
        var backingArrayBytes = checked(
            CommandRecorderRecordingCapacity.CalculateArrayCharge(meshAssetCount, IntPtr.Size)
            + CommandRecorderRecordingCapacity.CalculateArrayCharge(drawCount, IntPtr.Size)
            + CommandRecorderRecordingCapacity.CalculateArrayCharge(maximumGeometryCount, IntPtr.Size));

        return checked(((long)drawCount * bindingSetAndInputArrayBytes) + bindingObjectBytes + backingArrayBytes);
    }

    [SuppressMessage(
        "Usage",
        "CA2201:Do not raise reserved exception types",
        Justification = "A checked diagnostic byte estimate that cannot be represented is non-admissible.")]
    internal static long CalculateKnownMinimumCpuBytesForWorstCaseGeometry(
        int drawCount,
        int maximumIndexCount,
        int meshAssetCount,
        long maximumWorkBytes,
        long resultByteLength,
        long pendingGraphSubmissionBackingByteCharge)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(drawCount);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumIndexCount);
        ArgumentOutOfRangeException.ThrowIfNegative(meshAssetCount);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumWorkBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(resultByteLength);
        ArgumentOutOfRangeException.ThrowIfNegative(pendingGraphSubmissionBackingByteCharge);

        if (drawCount == 0)
        {
            return 0;
        }

        try
        {
            // M is at least one, so this bounds the command count before geometry identities are indexed.
            var maximumCommandCount = checked((6L * drawCount) - 3);
            var maximumGeometryCount = Math.Min(drawCount, meshAssetCount);
            var bindingPlanConstructionBytes = CalculateKnownBindingPlanConstructionBytes(drawCount, meshAssetCount);
            var recordingRecipeArrayBytes = SkinnedNormalDiagnosticRecordingRecipe.CalculateKnownBackingArrayBytes(
                drawCount,
                maximumGeometryCount);
            var recordingStateArrayBytes = SkinnedNormalDiagnosticRecordingState.CalculateKnownBackingArrayBytes(
                drawCount,
                maximumGeometryCount);
            return checked(
                CommandRecorderRecordingCapacity.CalculateArrayCharge(
                    drawCount,
                    Unsafe.SizeOf<RenderMeshC0NormalDiagnosticDraw>())
                + CommandRecorderRecordingCapacity.CalculateArrayCharge(maximumIndexCount, sizeof(int))
                + CommandRecorderRecordingCapacity.CalculateArrayCharge(meshAssetCount, sizeof(byte))
                + CommandRecorderRecordingCapacity.CalculateArrayCharge(maximumWorkBytes, sizeof(byte))
                + CommandRecorderRecordingCapacity.CalculateArrayCharge(resultByteLength, sizeof(byte))
                + CommandRecorderRecordingCapacity.CalculateKnownBackingArrayBytes(maximumCommandCount, drawCount)
                + GraphicsRecordingUseAdmission.CalculateByteAllowance(drawCount)
                + GraphicsOwnerRequestQueue.KnownRequestBackingByteCharge
                + pendingGraphSubmissionBackingByteCharge
                + bindingPlanConstructionBytes
                + CommandRecorderRecordingCapacity.CalculateKnownFourBindingScratchArrayBytes()
                + SkinnedNormalDiagnosticRecordingRecipe.KnownObjectByteCharge
                + SkinnedNormalDiagnosticRecordingState.CarrierByteCharge
                + recordingRecipeArrayBytes
                + recordingStateArrayBytes);
        }
        catch (OverflowException exception)
        {
            throw new OutOfMemoryException(
                "The skinned-normal diagnostic minimum byte estimate exceeds its representable CPU allowance.",
                exception);
        }
    }

    [SuppressMessage(
        "Usage",
        "CA2201:Do not raise reserved exception types",
        Justification = "A diagnostic request whose checked minimum preparation and recording arrays exceed its fixed CPU allowance is refused before allocation.")]
    internal static RenderMeshC0NormalDiagnosticWork? Create(
        RenderSnapshot3D snapshot,
        bool[] selectedDraws,
        int[] meshGeometryIndices,
        System.Collections.Generic.List<RenderMeshAsset> meshAssets,
        int[] bonePaletteOffsets,
        long storageBufferOffsetAlignment,
        long pendingGraphSubmissionBackingByteCharge)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(selectedDraws);
        ArgumentNullException.ThrowIfNull(meshGeometryIndices);
        ArgumentNullException.ThrowIfNull(meshAssets);
        ArgumentNullException.ThrowIfNull(bonePaletteOffsets);
        ArgumentOutOfRangeException.ThrowIfZero(storageBufferOffsetAlignment);
        ArgumentOutOfRangeException.ThrowIfNegative(pendingGraphSubmissionBackingByteCharge);

        if ((selectedDraws.Length != snapshot.MeshDraws.Length)
            || (meshGeometryIndices.Length != snapshot.MeshDraws.Length)
            || (bonePaletteOffsets.Length != snapshot.MeshPalettes.Length))
        {
            throw new ArgumentException("Normal-diagnostic preparation inputs must match the captured Mesh snapshot.");
        }

        var storageAlignment = LeastCommonMultiple(16, storageBufferOffsetAlignment);
        var drawCount = 0;
        var maximumIndexCount = 0;
        long maximumWorkBytes = 0;

        for (var drawIndex = 0; drawIndex < snapshot.MeshDraws.Length; drawIndex++)
        {
            var draw = snapshot.MeshDraws[drawIndex];

            if (!selectedDraws[drawIndex] || (draw.PaletteIndex < 0))
            {
                continue;
            }

            if ((draw.GroupIndex < 0) || (draw.MeshAssetId <= 0))
            {
                throw new InvalidOperationException("A selected skinned Mesh draw has an invalid group or asset identity.");
            }

            var meshIndex = meshGeometryIndices[drawIndex];

            if ((meshIndex < 0) || (meshIndex >= meshAssets.Count))
            {
                throw new InvalidOperationException("A selected skinned Mesh draw has no prepared geometry.");
            }

            var mesh = meshAssets[meshIndex];

            if ((draw.GroupIndex >= mesh.Groups.Length)
                || mesh.JointIndices.IsDefault
                || mesh.Weights.IsDefault
                || (draw.PaletteIndex >= bonePaletteOffsets.Length))
            {
                throw new InvalidOperationException("A selected skinned Mesh draw does not match its prepared streams or palette.");
            }

            var group = mesh.Groups[draw.GroupIndex];

            if ((group.FirstIndex < 0)
                || (group.IndexCount <= 0)
                || (checked((long)group.FirstIndex + group.IndexCount) > mesh.Indices.Length))
            {
                throw new InvalidOperationException("A selected skinned Mesh group has an invalid index range.");
            }

            drawCount = checked(drawCount + 1);
            maximumIndexCount = Math.Max(maximumIndexCount, group.IndexCount);
            var maximumPacketBytes = checked(16L * (1 + (((long)group.IndexCount + 3) / 4)));

            try
            {
                maximumWorkBytes = checked(maximumWorkBytes + maximumPacketBytes);
            }
            catch (OverflowException exception)
            {
                throw new OutOfMemoryException(
                    "The skinned-normal diagnostic work-byte estimate exceeds its representable CPU allowance.",
                    exception);
            }
        }

        if (drawCount == 0)
        {
            return null;
        }

        if (drawCount > Renderer.MaximumDrawInstanceCount)
        {
            throw new InvalidOperationException("The skinned-normal diagnostic row count exceeds the renderer draw limit.");
        }

        long resultByteLength;

        try
        {
            maximumWorkBytes = checked(
                maximumWorkBytes
                + (checked((long)drawCount - 1) * (storageAlignment - 1)));
            maximumWorkBytes = AlignUp(maximumWorkBytes, 16);
            resultByteLength = checked((long)drawCount * 16);
        }
        catch (OverflowException exception)
        {
            throw new OutOfMemoryException(
                "The skinned-normal diagnostic work-byte estimate exceeds its representable CPU allowance.",
                exception);
        }

        var knownMinimumCpuBytes = CalculateKnownMinimumCpuBytesForWorstCaseGeometry(
            drawCount,
            maximumIndexCount,
            meshAssets.Count,
            maximumWorkBytes,
            resultByteLength,
            pendingGraphSubmissionBackingByteCharge);

        if ((maximumWorkBytes > int.MaxValue)
            || (resultByteLength > int.MaxValue)
            || (knownMinimumCpuBytes > MaximumCpuBytes))
        {
            throw new OutOfMemoryException("The skinned-normal diagnostic managed-byte requirement exceeds its fixed CPU allowance or a backing's representable length.");
        }

        var draws = new RenderMeshC0NormalDiagnosticDraw[drawCount];
        var meshBuffersUsed = new bool[meshAssets.Count];
        var rowIndex = 0;

        for (var drawIndex = 0; drawIndex < snapshot.MeshDraws.Length; drawIndex++)
        {
            if (!selectedDraws[drawIndex])
            {
                continue;
            }

            var draw = snapshot.MeshDraws[drawIndex];

            if (draw.PaletteIndex < 0)
            {
                continue;
            }

            var meshIndex = meshGeometryIndices[drawIndex];
            var paletteBase = bonePaletteOffsets[draw.PaletteIndex];

            if (paletteBase < 0)
            {
                throw new InvalidOperationException("A selected skinned Mesh draw has an invalid palette base.");
            }

            draws[rowIndex] = new RenderMeshC0NormalDiagnosticDraw(
                draw.DrawId,
                drawIndex,
                meshIndex,
                paletteBase,
                rowIndex,
                TestedVertexCount: 0,
                WorkOffset: 0,
                WorkByteLength: 0);
            meshBuffersUsed[meshIndex] = true;
            rowIndex++;
        }

        Array.Sort(draws);

        for (var index = 1; index < draws.Length; index++)
        {
            if (draws[index - 1].DrawId == draws[index].DrawId)
            {
                throw new InvalidOperationException("A skinned-normal diagnostic plan contains a duplicate draw identity.");
            }
        }

        var scratch = new int[maximumIndexCount];
        var workBytes = new byte[checked((int)maximumWorkBytes)];
        var resultBytes = new byte[checked((int)resultByteLength)];
        var previousPacketEnd = 0L;

        for (var index = 0; index < draws.Length; index++)
        {
            var diagnosticDraw = draws[index] with { ResultRow = index };
            var snapshotDraw = snapshot.MeshDraws[diagnosticDraw.DrawIndex];
            var mesh = meshAssets[diagnosticDraw.MeshIndex];
            var group = mesh.Groups[snapshotDraw.GroupIndex];
            var groupIndices = mesh.Indices.AsSpan(group.FirstIndex, group.IndexCount);

            for (var memberIndex = 0; memberIndex < groupIndices.Length; memberIndex++)
            {
                var vertexIndex = groupIndices[memberIndex];

                if (vertexIndex >= (uint)mesh.VertexCount)
                {
                    throw new InvalidOperationException("A selected skinned Mesh index is outside its retained vertex stream.");
                }

                scratch[memberIndex] = checked((int)vertexIndex);
            }

            Array.Sort(scratch, 0, group.IndexCount);
            var uniqueCount = 0;

            for (var memberIndex = 0; memberIndex < group.IndexCount; memberIndex++)
            {
                var vertexIndex = scratch[memberIndex];

                if ((uniqueCount == 0) || (scratch[uniqueCount - 1] != vertexIndex))
                {
                    scratch[uniqueCount] = vertexIndex;
                    uniqueCount++;
                }
            }

            var packetByteLength = checked(16L * (1 + (((long)uniqueCount + 3) / 4)));
            var packetOffset = AlignUp(previousPacketEnd, storageAlignment);
            var packetEnd = checked(packetOffset + packetByteLength);

            if ((packetEnd > workBytes.Length) || (uniqueCount <= 0))
            {
                throw new InvalidOperationException("A skinned-normal diagnostic packet exceeds its preclaimed work buffer.");
            }

            var packetStart = checked((int)packetOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(workBytes.AsSpan(packetStart), checked((uint)index));
            BinaryPrimitives.WriteUInt32LittleEndian(workBytes.AsSpan(packetStart + sizeof(uint)), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(workBytes.AsSpan(packetStart + (2 * sizeof(uint))), checked((uint)uniqueCount));
            BinaryPrimitives.WriteUInt32LittleEndian(workBytes.AsSpan(packetStart + (3 * sizeof(uint))), checked((uint)diagnosticDraw.PaletteBase));

            for (var memberIndex = 0; memberIndex < uniqueCount; memberIndex++)
            {
                var memberOffset = checked(packetStart + 16 + (memberIndex * sizeof(uint)));
                BinaryPrimitives.WriteUInt32LittleEndian(workBytes.AsSpan(memberOffset), checked((uint)scratch[memberIndex]));
            }

            draws[index] = diagnosticDraw with {
                TestedVertexCount = uniqueCount,
                WorkOffset = packetOffset,
                WorkByteLength = packetByteLength,
            };
            previousPacketEnd = packetEnd;
        }

        var meshBufferCount = 0;

        foreach (var meshBufferUsed in meshBuffersUsed)
        {
            if (meshBufferUsed)
            {
                meshBufferCount++;
            }
        }

        return new RenderMeshC0NormalDiagnosticWork(
            snapshot.SnapshotId,
            snapshot.WorldGuid,
            snapshot.TickIndex,
            draws,
            meshBuffersUsed,
            meshBufferCount,
            storageAlignment,
            workBytes,
            resultBytes);
    }

    private static long AlignUp(long value, long alignment)
    {
        var remainder = value % alignment;
        return (remainder == 0) ? value : checked(value + alignment - remainder);
    }

    private static long GreatestCommonDivisor(long left, long right)
    {
        while (right != 0)
        {
            (left, right) = (right, left % right);
        }

        return left;
    }

    private static long LeastCommonMultiple(long left, long right)
    {
        var greatestCommonDivisor = GreatestCommonDivisor(left, right);
        return checked(left / greatestCommonDivisor * right);
    }
}
