// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceIdentityHistoryReader
{
    private const ushort ArrayKind = 24;
    private const ushort BlobKind = 13;
    private const ushort FixedArrayKind = 23;
    private const ushort GuidKind = 11;
    private const int GuidPayloadBytes = 16;
    private const ushort Int64Kind = 8;
    private const int MaximumAssignmentsPerPage = 4096;
    private const int MaximumFieldCount = 10;
    private const int MaximumPageCount = 8192;
    private const int MinimumBranchFieldCount = 7;
    private const ushort RecordKind = 22;
    private const int RootFieldCount = 7;
    private const ushort UInt16Kind = 2;
    private const ushort UInt32Kind = 3;
    private const ushort UInt8Kind = 1;

    private static readonly Guid s_forkSourceSchemaId = new ContentSchemaIdentityAssignment("state/fork-source").SchemaId;
    private static readonly Guid s_identityBranchSchemaId = new ContentSchemaIdentityAssignment("state/identity-branch").SchemaId;
    private static readonly Guid s_identityHistorySchemaId = new ContentSchemaIdentityAssignment("state/identity-history").SchemaId;
    private static readonly Guid s_identityPageSchemaId = new ContentSchemaIdentityAssignment("state/identity-page").SchemaId;

    internal static (
        Guid worldGuid,
        Guid replayLineageId,
        Guid activeBranchId,
        Guid issuanceNamespace,
        long nextAssignmentOrdinal,
        int branchCount,
        int pageCount,
        int liveEntityCount,
        int liveSceneInstanceCount) Read(
            EbinRecordReadView record,
            EbinReadLimits limits,
            WorldPersistenceConfiguration configuration,
            WorldPersistenceLiveIdentitySet liveIdentities,
            long nextTick,
            string parameterName)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(liveIdentities);

        if ((record.Header.SchemaId != s_identityHistorySchemaId)
            || (record.Header.Major != 1)
            || (record.Header.Minor != 0)
            || (record.FieldCount != RootFieldCount))
        {
            throw new ArgumentException("The state/identity-history record has the wrong schema identity, version, or field count.", parameterName);
        }

        var worldGuid = ReadGuid(record, 1, parameterName);
        var replayLineageId = ReadGuid(record, 2, parameterName);
        var activeBranchId = ReadGuid(record, 3, parameterName);
        var issuanceNamespace = ReadGuid(record, 4, parameterName);
        var nextAssignmentOrdinal = ReadInt64(record, 5, parameterName);

        if ((nextTick < 1)
            || (nextAssignmentOrdinal < 1)
            || (nextAssignmentOrdinal > checked((long)configuration.MaximumIdentityAssignments + 1)))
        {
            throw new ArgumentException("The state/identity-history cursor is outside its supported range.", parameterName);
        }

        var assignmentCount = nextAssignmentOrdinal - 1;
        var branchCount = ReadArrayCount(record, 6, configuration.MaximumIdentityBranches, minimumCount: 1, limits, parameterName);
        var pageCount = ReadArrayCount(record, 7, MaximumPageCount, minimumCount: 0, limits, parameterName);

        var expectedPageCount = checked((int)((assignmentCount + MaximumAssignmentsPerPage - 1) / MaximumAssignmentsPerPage));

        if (pageCount != expectedPageCount)
        {
            throw new ArgumentException("The identity-page count does not cover the complete assignment history.", parameterName);
        }

        Span<Guid> branchIds = stackalloc Guid[branchCount];
        var activeBranchFound = false;
        var previousFirstOrdinal = 0L;
        var previousCreatedAtNextTick = 0L;

        for (var index = 0; index < branchCount; index++)
        {
            var branch = record.GetRecordArrayElement(6, index);
            var branchData = ReadBranch(branch, index, nextAssignmentOrdinal, nextTick, parameterName);

            if ((index > 0) && (branchData.firstAssignmentOrdinal < previousFirstOrdinal))
            {
                throw new ArgumentException("Identity branches are not ordered by their first assignment ordinal.", parameterName);
            }

            if ((index > 0) && (branchData.createdAtNextTick < previousCreatedAtNextTick))
            {
                throw new ArgumentException("Identity branches are not ordered by creation tick.", parameterName);
            }

            for (var previousIndex = 0; previousIndex < index; previousIndex++)
            {
                if (branchIds[previousIndex] == branchData.branchId)
                {
                    throw new ArgumentException("The identity-branch directory contains duplicate branch identities.", parameterName);
                }
            }

            if (index > 0)
            {
                var parentFound = false;

                for (var parentIndex = 0; parentIndex < index; parentIndex++)
                {
                    parentFound |= branchIds[parentIndex] == branchData.parentBranchId;
                }

                if (!parentFound)
                {
                    throw new ArgumentException("An identity branch refers to a parent outside the earlier directory.", parameterName);
                }
            }

            branchIds[index] = branchData.branchId;
            previousFirstOrdinal = branchData.firstAssignmentOrdinal;
            previousCreatedAtNextTick = branchData.createdAtNextTick;

            if (branchData.branchId == activeBranchId)
            {
                if ((branchData.issuanceNamespace != issuanceNamespace)
                    || (branchData.creationLineageId != replayLineageId))
                {
                    throw new ArgumentException("The active identity branch disagrees with the history lineage or issuance namespace.", parameterName);
                }

                activeBranchFound = true;
            }
        }

        if (!activeBranchFound)
        {
            throw new ArgumentException("The active identity branch is absent from the directory.", parameterName);
        }

        var liveEntityCount = 0;
        var liveSceneInstanceCount = 0;

        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var liveCounts = ValidatePage(
                record.GetRecordArrayElement(7, pageIndex),
                pageIndex,
                assignmentCount,
                branchCount,
                liveIdentities,
                parameterName);
            liveEntityCount = checked(liveEntityCount + liveCounts.liveEntityCount);
            liveSceneInstanceCount = checked(liveSceneInstanceCount + liveCounts.liveSceneInstanceCount);
        }

        return (
            worldGuid,
            replayLineageId,
            activeBranchId,
            issuanceNamespace,
            nextAssignmentOrdinal,
            branchCount,
            pageCount,
            liveEntityCount,
            liveSceneInstanceCount);
    }

    private static EbinFieldReadView GetRequiredField(
        EbinRecordReadView record,
        uint tag,
        ushort kind,
        ulong? expectedCount,
        string parameterName)
    {
        if (!record.TryGetField(tag, out var field)
            || (field.Kind != kind)
            || !field.IsRequired
            || (expectedCount.HasValue && (field.Count != expectedCount.Value)))
        {
            throw new ArgumentException("An identity-history field has an invalid wire shape.", parameterName);
        }

        return field;
    }

    private static int ReadArrayCount(
        EbinRecordReadView record,
        uint tag,
        int maximumCount,
        int minimumCount,
        EbinReadLimits limits,
        string parameterName)
    {
        var field = GetRequiredField(record, tag, ArrayKind, expectedCount: null, parameterName);

        if ((field.Count < (ulong)minimumCount)
            || (field.Count > (ulong)maximumCount)
            || (field.Count > (ulong)limits.MaximumArrayElements)
            || (field.Count > int.MaxValue))
        {
            throw new ArgumentException("An identity-history array count is outside its supported range.", parameterName);
        }

        return checked((int)field.Count);
    }

    private static (Guid branchId, Guid creationLineageId, Guid issuanceNamespace, Guid parentBranchId, long createdAtNextTick, long firstAssignmentOrdinal)
        ReadBranch(
            EbinRecordReadView branch,
            int expectedIndex,
            long nextAssignmentOrdinal,
            long nextTick,
            string parameterName)
    {
        if ((branch.Header.SchemaId != s_identityBranchSchemaId)
            || (branch.Header.Major != 1)
            || (branch.Header.Minor != 0)
            || (branch.FieldCount is < MinimumBranchFieldCount or > MaximumFieldCount))
        {
            throw new ArgumentException("A state/identity-branch has the wrong schema identity, version, or field count.", parameterName);
        }

        var directoryIndex = ReadUInt16(branch, 1, parameterName);
        var branchId = ReadGuid(branch, 2, parameterName);
        var creationLineageId = ReadGuid(branch, 3, parameterName);
        var issuanceNamespace = ReadGuid(branch, 4, parameterName);
        var hasParent = branch.TryGetField(5, out var parentField);
        var createdAtNextTick = ReadInt64(branch, 6, parameterName);
        var hasSourceDigest = branch.TryGetField(7, out var sourceDigestField);
        var hasForkSource = branch.TryGetField(8, out var forkSourceField);
        var kind = ReadUInt8(branch, 9, parameterName);
        var firstAssignmentOrdinal = ReadInt64(branch, 10, parameterName);
        var expectedFieldCount = MinimumBranchFieldCount + (hasParent ? 1 : 0) + (hasSourceDigest ? 1 : 0) + (hasForkSource ? 1 : 0);

        if ((branch.FieldCount != expectedFieldCount)
            || (directoryIndex != expectedIndex)
            || (expectedIndex == 0 != !hasParent)
            || ((expectedIndex == 0) && ((createdAtNextTick != 0) || (firstAssignmentOrdinal != 1)))
            || (createdAtNextTick < 0)
            || (createdAtNextTick >= nextTick)
            || (firstAssignmentOrdinal < 1)
            || (firstAssignmentOrdinal > nextAssignmentOrdinal)
            || (kind != (expectedIndex == 0 ? 1 : 2)))
        {
            throw new ArgumentException("A state/identity-branch has inconsistent directory, parent, kind, tick, or assignment metadata.", parameterName);
        }

        if (hasParent)
        {
            if ((parentField.Kind != GuidKind)
                || parentField.IsRequired
                || (parentField.Count != 1)
                || (parentField.Payload.Length != 16))
            {
                throw new ArgumentException("An optional identity-branch parent has an invalid wire shape.", parameterName);
            }
        }

        if (hasSourceDigest
            && ((sourceDigestField.Kind != BlobKind)
                || sourceDigestField.IsRequired
                || (sourceDigestField.Count != 32)
                || (sourceDigestField.Payload.Length != 32)))
        {
            throw new ArgumentException("An optional identity-branch source digest has an invalid wire shape.", parameterName);
        }

        if (hasForkSource)
        {
            if ((forkSourceField.Kind != RecordKind)
                || forkSourceField.IsRequired
                || (forkSourceField.Count != 1))
            {
                throw new ArgumentException("An optional identity-branch fork source has an invalid wire shape.", parameterName);
            }

            var forkSource = new EbinRecordReadView(forkSourceField.Payload);

            if ((forkSource.Header.SchemaId != s_forkSourceSchemaId)
                || (forkSource.Header.Major != 1)
                || (forkSource.Header.Minor != 0)
                || (forkSource.FieldCount is not 6 and not 7))
            {
                throw new ArgumentException("A state/fork-source has the wrong schema identity, version, or field count.", parameterName);
            }
        }

        var parentBranchId = hasParent
            ? new Guid(parentField.Payload, bigEndian: true)
            : Guid.Empty;

        return (branchId, creationLineageId, issuanceNamespace, parentBranchId, createdAtNextTick, firstAssignmentOrdinal);
    }

    private static ReadOnlySpan<byte> ReadFixedArray(
        EbinFieldReadView field,
        ushort elementKind,
        int elementStride,
        string parameterName)
    {
        var expectedLength = checked(32L + (checked((long)field.Count) * elementStride));
        var payload = field.Payload;

        if (((long)payload.Length != expectedLength)
            || (BinaryPrimitives.ReadUInt16LittleEndian(payload[16..]) != elementKind)
            || (BinaryPrimitives.ReadUInt32LittleEndian(payload[20..]) != elementStride))
        {
            throw new ArgumentException("An identity-page fixed array has an invalid payload header.", parameterName);
        }

        return payload[32..];
    }

    private static Guid ReadGuid(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, GuidKind, expectedCount: 1, parameterName);

        if (field.Payload.Length != 16)
        {
            throw new ArgumentException("An identity-history GUID field has an invalid payload length.", parameterName);
        }

        var value = new Guid(field.Payload, bigEndian: true);

        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identity-history GUID cannot be empty.", parameterName);
        }

        return value;
    }

    private static long ReadInt64(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, Int64Kind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(long))
        {
            throw new ArgumentException("An identity-history Int64 field has an invalid payload length.", parameterName);
        }

        return BinaryPrimitives.ReadInt64LittleEndian(field.Payload);
    }

    private static ushort ReadUInt16(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, UInt16Kind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(ushort))
        {
            throw new ArgumentException("An identity-branch UInt16 field has an invalid payload length.", parameterName);
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(field.Payload);
    }

    private static byte ReadUInt8(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, UInt8Kind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(byte))
        {
            throw new ArgumentException("An identity-branch UInt8 field has an invalid payload length.", parameterName);
        }

        return field.Payload[0];
    }

    private static (int liveEntityCount, int liveSceneInstanceCount) ValidatePage(
        EbinRecordReadView page,
        int pageIndex,
        long assignmentCount,
        int branchCount,
        WorldPersistenceLiveIdentitySet liveIdentities,
        string parameterName)
    {
        if ((page.Header.SchemaId != s_identityPageSchemaId)
            || (page.Header.Major != 1)
            || (page.Header.Minor != 0)
            || (page.FieldCount != 3))
        {
            throw new ArgumentException("A state/identity-page has the wrong schema identity, version, or field count.", parameterName);
        }

        var expectedFirstOrdinal = checked(((long)pageIndex * MaximumAssignmentsPerPage) + 1);
        var expectedRecordCount = checked((int)Math.Min(MaximumAssignmentsPerPage, assignmentCount - (expectedFirstOrdinal - 1)));
        var firstOrdinal = ReadInt64(page, 1, parameterName);
        var idsField = GetRequiredField(page, 2, FixedArrayKind, checked((ulong)expectedRecordCount), parameterName);
        var metadataField = GetRequiredField(page, 3, FixedArrayKind, checked((ulong)expectedRecordCount), parameterName);
        var ids = ReadFixedArray(idsField, GuidKind, GuidPayloadBytes, parameterName);
        var metadata = ReadFixedArray(metadataField, UInt32Kind, sizeof(uint), parameterName);
        var liveEntityCount = 0;
        var liveSceneInstanceCount = 0;

        if ((firstOrdinal != expectedFirstOrdinal)
            || (ids.Length != checked(expectedRecordCount * GuidPayloadBytes))
            || (metadata.Length != checked(expectedRecordCount * sizeof(uint))))
        {
            throw new ArgumentException("An identity-page does not cover its exact contiguous assignment range.", parameterName);
        }

        for (var index = 0; index < expectedRecordCount; index++)
        {
            var id = new Guid(ids.Slice(index * GuidPayloadBytes, GuidPayloadBytes), bigEndian: true);
            var value = BinaryPrimitives.ReadUInt32LittleEndian(metadata[(index * sizeof(uint))..]);
            var identityKind = (value >> 10) & 3;
            var origin = (value >> 12) & 3;
            var branchIndex = value & 0x3FF;

            if ((id == Guid.Empty)
                || ((value & ~0x7FFFU) != 0)
                || (identityKind is not 1 and not 2)
                || (origin is < 1 or > 3)
                || (branchIndex >= (uint)branchCount))
            {
                throw new ArgumentException("An identity-page assignment has invalid identity metadata.", parameterName);
            }

            if ((value & (1U << 14)) != 0)
            {
                liveIdentities.Add(id, identityKind, checked(firstOrdinal + index), parameterName);

                if (identityKind == 1)
                {
                    liveEntityCount = checked(liveEntityCount + 1);
                }
                else
                {
                    liveSceneInstanceCount = checked(liveSceneInstanceCount + 1);
                }
            }
        }

        return (liveEntityCount, liveSceneInstanceCount);
    }
}
