// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace TerraFX.Entities;

internal sealed class ValidatedWorldIdentityHistory
{
    private readonly byte[] _encodedRecord;

    private ValidatedWorldIdentityHistory(
        Guid worldGuid,
        Guid replayLineageId,
        Guid activeBranchId,
        Guid issuanceNamespace,
        long nextAssignmentOrdinal,
        int pageCount,
        int liveEntityCount,
        int liveSceneInstanceCount,
        byte[] encodedRecord,
        ImmutableArray<WorldPersistenceIdentityScratchEntry> assignmentsByOrdinal,
        ImmutableArray<ValidatedWorldIdentityBranch> branches,
        WorldPersistenceIdentityHistoryReader.ValidatedLineageMembership lineageMembership)
    {
        WorldGuid = worldGuid;
        ReplayLineageId = replayLineageId;
        ActiveBranchId = activeBranchId;
        IssuanceNamespace = issuanceNamespace;
        NextAssignmentOrdinal = nextAssignmentOrdinal;
        PageCount = pageCount;
        LiveEntityCount = liveEntityCount;
        LiveSceneInstanceCount = liveSceneInstanceCount;
        _encodedRecord = encodedRecord;
        AssignmentsByOrdinal = assignmentsByOrdinal;
        Branches = branches;
        LineageMembership = lineageMembership;
    }

    internal Guid ActiveBranchId { get; }

    internal ImmutableArray<WorldPersistenceIdentityScratchEntry> AssignmentsByOrdinal { get; }

    internal ImmutableArray<ValidatedWorldIdentityBranch> Branches { get; }

    internal ReadOnlyMemory<byte> EncodedRecord => _encodedRecord;

    internal Guid IssuanceNamespace { get; }

    internal WorldPersistenceIdentityHistoryReader.ValidatedLineageMembership LineageMembership { get; }

    internal int LiveEntityCount { get; }

    internal int LiveSceneInstanceCount { get; }

    internal long NextAssignmentOrdinal { get; }

    internal long OwnedByteReservation
        => checked(
            64L
            + _encodedRecord.Length
            + ArrayBytes(AssignmentsByOrdinal.Length, Unsafe.SizeOf<WorldPersistenceIdentityScratchEntry>())
            + ArrayBytes(Branches.Length, Unsafe.SizeOf<ValidatedWorldIdentityBranch>())
            + ArrayBytes(LineageMembership.Count, Unsafe.SizeOf<Guid>()));

    internal int PageCount { get; }

    internal Guid ReplayLineageId { get; }

    internal Guid WorldGuid { get; }

    internal static ValidatedWorldIdentityHistory CreateValidated(
        Guid worldGuid,
        Guid replayLineageId,
        Guid activeBranchId,
        Guid issuanceNamespace,
        long nextAssignmentOrdinal,
        int pageCount,
        int liveEntityCount,
        int liveSceneInstanceCount,
        scoped ReadOnlySpan<byte> encodedRecord,
        scoped ReadOnlySpan<WorldPersistenceIdentityScratchEntry> assignmentsByOrdinal,
        scoped ReadOnlySpan<ValidatedWorldIdentityBranch> branches,
        WorldPersistenceIdentityHistoryReader.ValidatedLineageMembership lineageMembership)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(worldGuid, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(replayLineageId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(activeBranchId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(issuanceNamespace, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfLessThan(nextAssignmentOrdinal, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(pageCount);
        ArgumentOutOfRangeException.ThrowIfNegative(liveEntityCount);
        ArgumentOutOfRangeException.ThrowIfNegative(liveSceneInstanceCount);
        ArgumentNullException.ThrowIfNull(lineageMembership);

        if (encodedRecord.IsEmpty || branches.IsEmpty)
        {
            throw new ArgumentException("A validated World identity history requires its canonical record and branch directory.");
        }

        for (var index = 0; index < assignmentsByOrdinal.Length; index++)
        {
            if (assignmentsByOrdinal[index].AssignmentOrdinal != checked((uint)(index + 1)))
            {
                throw new ArgumentException("Validated World identity assignments must be in complete ordinal order.", nameof(assignmentsByOrdinal));
            }
        }

        if ((nextAssignmentOrdinal != checked((long)assignmentsByOrdinal.Length + 1))
            || (lineageMembership.WorldGuid != worldGuid)
            || (lineageMembership.ReplayLineageId != replayLineageId)
            || (lineageMembership.ActiveBranchId != activeBranchId)
            || (lineageMembership.IssuanceNamespace != issuanceNamespace))
        {
            throw new ArgumentException("The validated identity history and lineage membership disagree.");
        }

        return new ValidatedWorldIdentityHistory(
            worldGuid,
            replayLineageId,
            activeBranchId,
            issuanceNamespace,
            nextAssignmentOrdinal,
            pageCount,
            liveEntityCount,
            liveSceneInstanceCount,
            encodedRecord.ToArray(),
            [.. assignmentsByOrdinal],
            [.. branches],
            lineageMembership);
    }

    private static long ArrayBytes(int count, int elementSize)
    {
        var payloadBytes = checked((long)count * elementSize);
        return checked(64L + ((payloadBytes + 15L) & ~15L));
    }
}
