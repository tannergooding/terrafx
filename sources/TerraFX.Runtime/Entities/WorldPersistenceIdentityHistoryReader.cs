// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Globalization;
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

    internal static int MeasureAssignmentCount(
        EbinRecordReadView record,
        int maximumIdentityAssignments,
        string parameterName)
    {
        if ((record.Header.SchemaId != s_identityHistorySchemaId)
            || (record.Header.Major != 1)
            || (record.Header.Minor != 0)
            || (record.FieldCount != RootFieldCount))
        {
            throw new ArgumentException("The state/identity-history record has the wrong schema identity, version, or field count.", parameterName);
        }

        var nextAssignmentOrdinal = ReadInt64(record, 5, parameterName);

        if ((nextAssignmentOrdinal < 1)
            || (nextAssignmentOrdinal > checked((long)maximumIdentityAssignments + 1)))
        {
            throw new ArgumentException("The state/identity-history cursor is outside its supported range.", parameterName);
        }

        return checked((int)(nextAssignmentOrdinal - 1));
    }

    internal static int MeasureLineageMembershipScratchCapacity(
        scoped ReadOnlySpan<byte> historyRecord,
        EbinReadLimits limits,
        WorldPersistenceConfiguration configuration,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(configuration);
        return MeasureLineageMembershipScratchCapacity(
            historyRecord,
            limits,
            configuration.MaximumIdentityAssignments,
            configuration.MaximumIdentityBranches,
            parameterName);
    }

    internal static int MeasureLineageMembershipScratchCapacity(
        scoped ReadOnlySpan<byte> historyRecord,
        EbinReadLimits limits,
        int maximumIdentityAssignments,
        int maximumIdentityBranches,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(parameterName);
        var status = EbinStructuralReader.Validate(
            historyRecord,
            limits,
            EbinReadPolicy.Canonical,
            out var bytesConsumed,
            out _);

        if ((status is not EbinReadStatus.Complete) || (bytesConsumed != historyRecord.Length))
        {
            throw new ArgumentException("The state/identity-history record is not a complete canonical EBIN record.", parameterName);
        }

        var record = new EbinRecordReadView(historyRecord);
        _ = MeasureAssignmentCount(record, maximumIdentityAssignments, parameterName);
        var branchCount = ReadArrayCount(
            record,
            6,
            maximumIdentityBranches,
            minimumCount: 1,
            limits,
            parameterName);
        return GetLineageMembershipScratchCapacity(branchCount, limits);
    }

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
            scoped ReadOnlySpan<byte> historyRecord,
            EbinReadLimits limits,
            WorldPersistenceConfiguration configuration,
            WorldPersistenceLiveIdentitySet liveIdentities,
            scoped Span<WorldPersistenceIdentityScratchEntry> identityScratch,
            long nextTick,
            string parameterName)
        => Read(
            historyRecord,
            limits,
            configuration,
            liveIdentities,
            identityScratch,
            nextTick,
            parameterName,
            [],
            out _);

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
            scoped ReadOnlySpan<byte> historyRecord,
            EbinReadLimits limits,
            WorldPersistenceConfiguration configuration,
            WorldPersistenceLiveIdentitySet liveIdentities,
            scoped Span<WorldPersistenceIdentityScratchEntry> identityScratch,
            long nextTick,
            string parameterName,
            scoped Span<Guid> lineageMembershipScratch,
            out int lineageMembershipCount)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(liveIdentities);
        ArgumentNullException.ThrowIfNull(parameterName);
        lineageMembershipCount = 0;

        var record = new EbinRecordReadView(historyRecord);
        var worldGuid = ReadGuid(record, 1, parameterName);
        var replayLineageId = ReadGuid(record, 2, parameterName);
        var activeBranchId = ReadGuid(record, 3, parameterName);
        var issuanceNamespace = ReadGuid(record, 4, parameterName);
        var nextAssignmentOrdinal = ReadInt64(record, 5, parameterName);
        var assignmentCount = MeasureAssignmentCount(
            record,
            configuration.MaximumIdentityAssignments,
            parameterName);

        if (nextTick < 1)
        {
            throw new ArgumentException("The state/identity-history cursor is outside its supported range.", parameterName);
        }

        if (identityScratch.Length != assignmentCount)
        {
            throw new ArgumentException("The identity-history scratch span does not match the bounded assignment count.", parameterName);
        }

        var branchCount = ReadArrayCount(record, 6, configuration.MaximumIdentityBranches, minimumCount: 1, limits, parameterName);
        var pageCount = ReadArrayCount(record, 7, MaximumPageCount, minimumCount: 0, limits, parameterName);

        if (!lineageMembershipScratch.IsEmpty)
        {
            var requiredCapacity = GetLineageMembershipScratchCapacity(branchCount, limits);

            if (lineageMembershipScratch.Length < requiredCapacity)
            {
                throw new ArgumentException("The lineage-membership scratch span is smaller than its bounded capacity.", nameof(lineageMembershipScratch));
            }

            AppendLineageMembership(
                replayLineageId,
                lineageMembershipScratch,
                ref lineageMembershipCount,
                parameterName);
        }

        var expectedPageCount = checked((int)((assignmentCount + MaximumAssignmentsPerPage - 1) / MaximumAssignmentsPerPage));

        if (pageCount != expectedPageCount)
        {
            throw new ArgumentException("The identity-page count does not cover the complete assignment history.", parameterName);
        }

        Span<Guid> branchIds = stackalloc Guid[branchCount];
        Span<Guid> branchNamespaces = stackalloc Guid[branchCount];
        Span<Guid> branchSourceNamespaces = stackalloc Guid[branchCount];
        Span<byte> branchHasForkSource = stackalloc byte[branchCount];
        var activeBranchFound = false;
        var previousFirstOrdinal = 0L;
        var previousCreatedAtNextTick = 0L;

        for (var index = 0; index < branchCount; index++)
        {
            var branch = record.GetRecordArrayElement(6, index);
            var branchData = ReadBranch(
                branch,
                index,
                nextAssignmentOrdinal,
                nextTick,
                worldGuid,
                limits,
                lineageMembershipScratch,
                ref lineageMembershipCount,
                parameterName);

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
            branchNamespaces[index] = branchData.issuanceNamespace;
            branchSourceNamespaces[index] = branchData.sourceIdentityNamespace;
            branchHasForkSource[index] = branchData.hasForkSource ? (byte)1 : (byte)0;
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
                worldGuid,
                branchNamespaces,
                branchSourceNamespaces,
                branchHasForkSource,
                liveIdentities,
                identityScratch,
                parameterName);
            liveEntityCount = checked(liveEntityCount + liveCounts.liveEntityCount);
            liveSceneInstanceCount = checked(liveSceneInstanceCount + liveCounts.liveSceneInstanceCount);
        }

        identityScratch.Sort();

        for (var index = 1; index < identityScratch.Length; index++)
        {
            if (identityScratch[index - 1].StableId == identityScratch[index].StableId)
            {
                throw new ArgumentException("The identity history contains duplicate assigned identities.", parameterName);
            }
        }

        if (!lineageMembershipScratch.IsEmpty)
        {
            lineageMembershipScratch[..lineageMembershipCount].Sort();
            var uniqueCount = 1;

            for (var index = 1; index < lineageMembershipCount; index++)
            {
                if (lineageMembershipScratch[index] != lineageMembershipScratch[uniqueCount - 1])
                {
                    lineageMembershipScratch[uniqueCount++] = lineageMembershipScratch[index];
                }
            }

            lineageMembershipCount = uniqueCount;
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

    internal static (
        Guid worldGuid,
        Guid replayLineageId,
        Guid activeBranchId,
        Guid issuanceNamespace,
        long nextAssignmentOrdinal,
        int branchCount,
        int pageCount,
        int liveEntityCount,
        int liveSceneInstanceCount) ReadAndIssueLineageMembership(
        scoped ReadOnlySpan<byte> historyRecord,
        EbinReadLimits limits,
        WorldPersistenceConfiguration configuration,
        WorldPersistenceLiveIdentitySet liveIdentities,
        scoped Span<WorldPersistenceIdentityScratchEntry> identityScratch,
        long nextTick,
        string parameterName,
        scoped Span<Guid> lineageMembershipScratch,
        out ValidatedLineageMembership? validatedLineageMembership)
    {
        var contents = Read(
            historyRecord,
            limits,
            configuration,
            liveIdentities,
            identityScratch,
            nextTick,
            parameterName,
            lineageMembershipScratch,
            out var lineageMembershipCount);
        validatedLineageMembership = (lineageMembershipCount == 0)
            ? null
            : ValidatedLineageMembership.CreateValidated(
                contents.worldGuid,
                contents.replayLineageId,
                contents.activeBranchId,
                contents.issuanceNamespace,
                lineageMembershipScratch[..lineageMembershipCount]);
        return contents;
    }

    internal static (
        Guid sourceWorldGuid,
        Guid sourceBranchId,
        Guid sourceCreationLineageId,
        Guid sourceNamespace)
        ReadForkSource(
        scoped ReadOnlySpan<byte> forkSourceRecord,
        EbinReadLimits limits,
        string parameterName)
    {
        var membershipCount = 0;
        return ReadForkSourceWithLineageMembership(
            forkSourceRecord,
            limits,
            [],
            ref membershipCount,
            parameterName);
    }

    internal static (
        Guid sourceWorldGuid,
        Guid sourceBranchId,
        Guid sourceCreationLineageId,
        Guid sourceNamespace)
        ReadForkSourceWithLineageMembership(
        scoped ReadOnlySpan<byte> forkSourceRecord,
        EbinReadLimits limits,
        scoped Span<Guid> lineageMembershipScratch,
        ref int lineageMembershipCount,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(parameterName);

        var current = new EbinRecordReadView(forkSourceRecord);
        var depth = 0;
        var immediateSourceWorldGuid = Guid.Empty;
        var immediateSourceBranchId = Guid.Empty;
        var immediateSourceCreationLineageId = Guid.Empty;

        while (true)
        {
            if ((depth >= limits.MaximumDepth)
                || (current.Header.SchemaId != s_forkSourceSchemaId)
                || (current.Header.Major != 1)
                || (current.Header.Minor != 0))
            {
                throw new ArgumentException("A state/fork-source has the wrong schema identity, version, or nesting depth.", parameterName);
            }

            var sourceWorldGuid = ReadGuid(current, 1, parameterName);
            var sourceBranchId = ReadGuid(current, 2, parameterName);
            var sourceCreationLineageId = ReadGuid(current, 3, parameterName);
            var sourceNamespace = ReadGuid(current, 4, parameterName);
            ValidateDigest(current, 5, parameterName);
            ValidateDigest(current, 6, parameterName);
            var hasPrior = current.TryGetField(7, out var priorField);
            var expectedFieldCount = hasPrior ? 7 : 6;

            if (current.FieldCount != expectedFieldCount)
            {
                throw new ArgumentException("A state/fork-source has an invalid field set.", parameterName);
            }

            if (depth == 0)
            {
                immediateSourceWorldGuid = sourceWorldGuid;
                immediateSourceBranchId = sourceBranchId;
                immediateSourceCreationLineageId = sourceCreationLineageId;
            }

            AppendLineageMembership(
                sourceCreationLineageId,
                lineageMembershipScratch,
                ref lineageMembershipCount,
                parameterName);

            if (!hasPrior)
            {
                return (
                    immediateSourceWorldGuid,
                    immediateSourceBranchId,
                    immediateSourceCreationLineageId,
                    sourceNamespace);
            }

            if ((priorField.Kind != RecordKind)
                || priorField.IsRequired
                || (priorField.Count != 1))
            {
                throw new ArgumentException("A state/fork-source prior record has an invalid wire shape.", parameterName);
            }

            current = new EbinRecordReadView(priorField.Payload);
            depth++;
        }
    }

    private static void AppendLineageMembership(
        Guid lineageId,
        scoped Span<Guid> lineageMembershipScratch,
        ref int lineageMembershipCount,
        string parameterName)
    {
        if (lineageMembershipScratch.IsEmpty)
        {
            return;
        }

        if ((lineageId == Guid.Empty) || (lineageMembershipCount >= lineageMembershipScratch.Length))
        {
            throw new ArgumentException("The lineage-membership scratch span is invalid or too small.", parameterName);
        }

        lineageMembershipScratch[lineageMembershipCount++] = lineageId;
    }

    private static int GetLineageMembershipScratchCapacity(int branchCount, EbinReadLimits limits)
        => checked(1 + (branchCount * checked(limits.MaximumDepth + 1)));

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

    private static (
        Guid branchId,
        Guid creationLineageId,
        Guid issuanceNamespace,
        Guid sourceIdentityNamespace,
        Guid parentBranchId,
        long createdAtNextTick,
        long firstAssignmentOrdinal,
        bool hasForkSource)
        ReadBranch(
            EbinRecordReadView branch,
            int expectedIndex,
            long nextAssignmentOrdinal,
            long nextTick,
            Guid worldGuid,
            EbinReadLimits limits,
            scoped Span<Guid> lineageMembershipScratch,
            ref int lineageMembershipCount,
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
        var sourceIdentityNamespace = issuanceNamespace;

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

        AppendLineageMembership(
            creationLineageId,
            lineageMembershipScratch,
            ref lineageMembershipCount,
            parameterName);

        if (hasForkSource)
        {
            if ((forkSourceField.Kind != RecordKind)
                || forkSourceField.IsRequired
                || (forkSourceField.Count != 1))
            {
                throw new ArgumentException("An optional identity-branch fork source has an invalid wire shape.", parameterName);
            }

            var (sourceWorldGuid, sourceBranchId, sourceCreationLineageId, sourceNamespace) = ReadForkSourceWithLineageMembership(
                forkSourceField.Payload,
                limits,
                lineageMembershipScratch,
                ref lineageMembershipCount,
                parameterName);

            if ((sourceWorldGuid == worldGuid)
                || (sourceCreationLineageId != creationLineageId)
                || (branchId != StableIdentityLedger.CreateForkBranchId(worldGuid, sourceBranchId))
                || (issuanceNamespace != branchId))
            {
                throw new ArgumentException("An imported identity branch does not preserve its source identity transform.", parameterName);
            }

            sourceIdentityNamespace = sourceNamespace;
        }

        var parentBranchId = hasParent
            ? new Guid(parentField.Payload, bigEndian: true)
            : Guid.Empty;

        return (
            branchId,
            creationLineageId,
            issuanceNamespace,
            sourceIdentityNamespace,
            parentBranchId,
            createdAtNextTick,
            firstAssignmentOrdinal,
            hasForkSource);
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

    private static void ValidateAssignmentRecipe(
        Guid identity,
        uint identityKind,
        uint originValue,
        uint branchIndex,
        long assignmentOrdinal,
        scoped ReadOnlySpan<Guid> branchNamespaces,
        string parameterName)
    {
        var kind = (IdentityKind)identityKind;
        var origin = (IdentityOrigin)originValue;

        if (origin == IdentityOrigin.Automatic)
        {
            var prefix = kind == IdentityKind.Entity ? "world/entity/v1/" : "world/scene-instance/v1/";
            var name = string.Create(CultureInfo.InvariantCulture, $"{prefix}{assignmentOrdinal:x16}");
            var expectedIdentity = FoundationNameUuid.Create(branchNamespaces[checked((int)branchIndex)], name);

            if (identity != expectedIdentity)
            {
                throw new ArgumentException("An automatic identity does not match its branch namespace and assignment ordinal recipe.", parameterName);
            }
        }
        else if (origin == IdentityOrigin.Explicit)
        {
            Span<byte> identityBytes = stackalloc byte[16];
            _ = identity.TryWriteBytes(identityBytes, bigEndian: true, out _);

            if ((identityBytes[6] & 0xF0) == 0x80 && (identityBytes[8] & 0xC0) == 0x80)
            {
                throw new ArgumentException("An explicit identity uses the engine-reserved RFC UUIDv8 domain.", parameterName);
            }
        }
    }

    private static void ValidateDigest(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, BlobKind, expectedCount: 32, parameterName);

        if (field.Payload.Length != 32)
        {
            throw new ArgumentException("A state/fork-source digest has an invalid payload length.", parameterName);
        }
    }

    private static (int liveEntityCount, int liveSceneInstanceCount) ValidatePage(
        EbinRecordReadView page,
        int pageIndex,
        long assignmentCount,
        int branchCount,
        Guid worldGuid,
        scoped ReadOnlySpan<Guid> branchNamespaces,
        scoped ReadOnlySpan<Guid> branchSourceNamespaces,
        scoped ReadOnlySpan<byte> branchHasForkSource,
        WorldPersistenceLiveIdentitySet liveIdentities,
        scoped Span<WorldPersistenceIdentityScratchEntry> identityScratch,
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

            var assignmentOrdinal = checked(firstOrdinal + index);
            ValidateAssignmentRecipe(id, identityKind, origin, branchIndex, assignmentOrdinal, branchNamespaces, parameterName);
            var sourceNamespace = branchNamespaces[checked((int)branchIndex)];

            if (origin == (uint)IdentityOrigin.ForkImported)
            {
                var expectedIdentity = StableIdentityLedger.CreateAutomaticIdentity(
                    worldGuid,
                    (IdentityKind)identityKind,
                    checked((uint)assignmentOrdinal));

                if (branchHasForkSource[checked((int)branchIndex)] == 0)
                {
                    throw new ArgumentException("A fork-imported identity has no retained fork-source lineage.", parameterName);
                }

                if (id != expectedIdentity)
                {
                    throw new ArgumentException("A fork-imported identity does not match its target World kind and ordinal recipe.", parameterName);
                }

                sourceNamespace = branchSourceNamespaces[checked((int)branchIndex)];
            }

            identityScratch[checked((int)assignmentOrdinal - 1)] = new WorldPersistenceIdentityScratchEntry(
                id,
                checked((byte)identityKind),
                checked((uint)assignmentOrdinal),
                checked((ushort)branchIndex),
                sourceNamespace,
                checked((byte)origin),
                (value & (1U << 14)) != 0);

            if ((value & (1U << 14)) != 0)
            {
                liveIdentities.Add(id, identityKind, assignmentOrdinal, parameterName);

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

    internal sealed class ValidatedLineageMembership
    {
        private readonly Guid[] _lineageIds;

        private ValidatedLineageMembership(
            Guid worldGuid,
            Guid replayLineageId,
            Guid activeBranchId,
            Guid issuanceNamespace,
            Guid[] lineageIds)
        {
            WorldGuid = worldGuid;
            ReplayLineageId = replayLineageId;
            ActiveBranchId = activeBranchId;
            IssuanceNamespace = issuanceNamespace;
            _lineageIds = lineageIds;
        }

        internal Guid ActiveBranchId { get; }

        internal int Count => _lineageIds.Length;

        internal Guid IssuanceNamespace { get; }

        internal ReadOnlySpan<Guid> LineageIds => _lineageIds;

        internal Guid ReplayLineageId { get; }

        internal Guid WorldGuid { get; }

        internal static ValidatedLineageMembership CreateValidated(
            Guid worldGuid,
            Guid replayLineageId,
            Guid activeBranchId,
            Guid issuanceNamespace,
            scoped ReadOnlySpan<Guid> lineageIds)
        {
            ArgumentOutOfRangeException.ThrowIfEqual(worldGuid, Guid.Empty);
            ArgumentOutOfRangeException.ThrowIfEqual(replayLineageId, Guid.Empty);
            ArgumentOutOfRangeException.ThrowIfEqual(activeBranchId, Guid.Empty);
            ArgumentOutOfRangeException.ThrowIfEqual(issuanceNamespace, Guid.Empty);

            var containsReplayLineage = false;

            for (var index = 0; index < lineageIds.Length; index++)
            {
                var lineageId = lineageIds[index];

                if ((lineageId == Guid.Empty)
                    || ((index > 0) && (lineageIds[index - 1].CompareTo(lineageId) >= 0)))
                {
                    throw new ArgumentException("The validated lineage membership must be nonempty-keyed, RFC-ordered, and unique.", nameof(lineageIds));
                }

                containsReplayLineage |= lineageId == replayLineageId;
            }

            if (lineageIds.IsEmpty || !containsReplayLineage)
            {
                throw new ArgumentException("The validated lineage membership must contain its replay lineage.", nameof(lineageIds));
            }

            return new ValidatedLineageMembership(
                worldGuid,
                replayLineageId,
                activeBranchId,
                issuanceNamespace,
                lineageIds.ToArray());
        }
    }
}
