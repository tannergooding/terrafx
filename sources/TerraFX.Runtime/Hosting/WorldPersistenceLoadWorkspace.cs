// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Hosting;

internal static class WorldPersistenceLoadWorkspace
{
    private const ushort ArrayKind = 24;

    internal static Requirements Measure(ReadOnlyMemory<byte> fileRecord, EbinReadLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var file = new EbinRecordReadView(fileRecord.Span);
        var checkpoint = file.GetRecord(2);
        var headerRecordBytes = checkpoint.GetRecord(1).EncodedBytes.Length;
        var worldRecord = checkpoint.GetRecord(2).EncodedBytes;
        var worldRecordBytes = worldRecord.Length;
        var world = new EbinRecordReadView(worldRecord);
        var configuration = WorldPersistenceConfigurationReader.Read(world.GetRecord(1).EncodedBytes.ToArray(), limits);
        var ecs = world.GetRecord(5);
        var scenes = world.GetRecord(6);
        var history = ecs.GetRecord(5);
        var identityAssignmentCount = WorldPersistenceWorldRecordReader.MeasureIdentityAssignmentCount(
            world,
            nameof(fileRecord));
        var lineageMembershipScratchCapacity = WorldPersistenceWorldRecordReader.MeasureLineageMembershipScratchCapacity(
            world,
            limits,
            nameof(fileRecord));
        var branchCount = ReadArrayCount(
            history,
            6,
            configuration.MaximumIdentityBranches,
            minimumCount: 1,
            nameof(fileRecord));
        var liveEntityCount = ReadArrayCount(
            ecs,
            1,
            configuration.MaximumLiveEntities,
            minimumCount: 0,
            nameof(fileRecord));
        var liveSceneInstanceCount = ReadArrayCount(
            scenes,
            1,
            configuration.MaximumActiveSceneInstances,
            minimumCount: 0,
            nameof(fileRecord));
        var liveIdentityCount = checked(liveEntityCount + liveSceneInstanceCount);
        var historyRecordBytes = history.EncodedBytes.Length;
        var assignmentBytes = ArrayBytes(
            identityAssignmentCount,
            Unsafe.SizeOf<WorldPersistenceIdentityScratchEntry>());
        var lineageBytes = ArrayBytes(lineageMembershipScratchCapacity, Unsafe.SizeOf<Guid>());
        var branchBytes = ArrayBytes(branchCount, Unsafe.SizeOf<ValidatedWorldIdentityBranch>());
        var retainedIdentityHistoryBytes = checked(
            64L + historyRecordBytes + assignmentBytes + branchBytes + lineageBytes);
        var retainedContentsBytes = checked(
            512L + headerRecordBytes + worldRecordBytes + 64L + retainedIdentityHistoryBytes);
        var scratchWorkspaceBytes = checked(
            assignmentBytes
            + lineageBytes
            + WorldPersistenceLiveIdentitySet.MeasureReadWorkspaceBytes(liveIdentityCount)
            + ((liveEntityCount == 0)
                ? 0
                : ArrayBytes(
                    WorldRestoreEcsPlan.GetArchetypeHashTableLength(
                        Math.Min(liveEntityCount, configuration.MaximumArchetypes)),
                    sizeof(int))));
        return new Requirements(
            configuration,
            headerRecordBytes,
            worldRecordBytes,
            identityAssignmentCount,
            lineageMembershipScratchCapacity,
            branchCount,
            liveIdentityCount,
            retainedContentsBytes,
            scratchWorkspaceBytes);
    }

    private static long ArrayBytes(long count, int elementSize)
    {
        if (count == 0)
        {
            return 0;
        }

        var payloadBytes = checked(count * elementSize);
        return checked(64L + ((payloadBytes + 15L) & ~15L));
    }

    private static int ReadArrayCount(
        EbinRecordReadView record,
        uint tag,
        int maximumCount,
        int minimumCount,
        string parameterName)
    {
        if (!record.TryGetField(tag, out var field)
            || (field.Kind != ArrayKind)
            || (field.Count < (ulong)minimumCount)
            || (field.Count > (ulong)maximumCount)
            || (field.Count > int.MaxValue))
        {
            throw new ArgumentException("A State2 array count is outside its configured load workspace bound.", parameterName);
        }

        return checked((int)field.Count);
    }

    internal readonly record struct Requirements(
        WorldPersistenceConfiguration Configuration,
        int HeaderRecordBytes,
        int WorldRecordBytes,
        int IdentityAssignmentCount,
        int LineageMembershipScratchCapacity,
        int BranchCount,
        int LiveIdentityCount,
        long RetainedContentsBytes,
        long ScratchWorkspaceBytes)
    {
        internal long TotalWorkspaceBytes => checked(RetainedContentsBytes + ScratchWorkspaceBytes);
    }
}
