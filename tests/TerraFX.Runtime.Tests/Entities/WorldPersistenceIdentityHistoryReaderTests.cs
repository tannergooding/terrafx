// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Animation;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Hosting;
using TerraFX.Integration;
using TerraFX.Rendering;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceIdentityHistoryReaderTests
{
    private static WorldPersistenceConfiguration CreateConfiguration()
        => new(
            maximumLiveEntities: 1,
            maximumEntitySlots: 1,
            maximumArchetypes: 1,
            maximumDeferredCommands: 1,
            maximumComponentTypes: 1,
            maximumPendingOwnerRequests: 1,
            maximumRecordedControls: 1,
            maximumIdentityAssignments: 1,
            schedulingLimits: WorldSchedulingLimits.Default,
            maximumIdentityBranches: 1,
            maximumEntityReferencePositions: 1,
            maximumActiveSceneInstances: 1,
            maximumSceneOccurrences: 1,
            maximumSnapshotBytes: 4096,
            maximumPersistenceAdmissionBytes: 4096);

    private static byte[] CreateForkSourceRecord(
        Guid sourceWorldGuid,
        Guid sourceBranchId,
        Guid sourceCreationLineageId,
        Guid sourceNamespace,
        byte[]? priorForkSource = null,
        int checkpointDigestLength = 32)
    {
        var fieldCount = priorForkSource is null ? 6 : 7;
        var payloadStorage = new byte[checked((4 * 16) + checkpointDigestLength + 32 + (priorForkSource?.Length ?? 0))];
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[fieldCount];
        var offset = 0;
        var values = new[] { sourceWorldGuid, sourceBranchId, sourceCreationLineageId, sourceNamespace };

        for (var index = 0; index < values.Length; index++)
        {
            _ = values[index].TryWriteBytes(payloadStorage.AsSpan(offset, 16), bigEndian: true, out _);
            fields[index] = new EbinFieldSource(
                checked((uint)index + 1),
                11,
                1,
                1,
                new EbinPayloadSlice(offset, 16));
            offset = checked(offset + 16);
        }

        payloadStorage.AsSpan(offset, checkpointDigestLength).Fill(0x5A);
        fields[4] = new EbinFieldSource(5, 13, 1, checked((ulong)checkpointDigestLength), new EbinPayloadSlice(offset, checkpointDigestLength));
        offset = checked(offset + checkpointDigestLength);
        payloadStorage.AsSpan(offset, 32).Fill(0xA5);
        fields[5] = new EbinFieldSource(6, 13, 1, 32, new EbinPayloadSlice(offset, 32));
        offset = checked(offset + 32);

        if (priorForkSource is not null)
        {
            priorForkSource.CopyTo(payloadStorage, offset);
            fields[6] = new EbinFieldSource(7, 22, 0, 1, new EbinPayloadSlice(offset, priorForkSource.Length));
            offset = checked(offset + priorForkSource.Length);
        }

        return WriteRecord(
            new ContentSchemaIdentityAssignment("state/fork-source").SchemaId,
            fields,
            payloadStorage.AsSpan(0, offset));
    }

    private static byte[] CreateIdentityBranchRecord(
        Guid branchId,
        Guid creationLineageId,
        Guid issuanceNamespace,
        byte[]? forkSourceRecord)
    {
        var fieldCount = forkSourceRecord is null ? 7 : 8;
        var payloadStorage = new byte[checked(2 + (3 * 16) + 8 + (forkSourceRecord?.Length ?? 0) + 1 + 8)];
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[fieldCount];
        var fieldIndex = 0;
        var offset = 0;

        Assert.That(EbinPayloadWriter.Write((ushort)0, payloadStorage.AsSpan(offset), out var bytesWritten, out var diagnostic), Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.ToString());
        fields[fieldIndex++] = new EbinFieldSource(1, 2, 1, 1, new EbinPayloadSlice(offset, bytesWritten));
        offset = checked(offset + bytesWritten);

        foreach (var value in new[] { branchId, creationLineageId, issuanceNamespace })
        {
            Assert.That(EbinPayloadWriter.Write(value, payloadStorage.AsSpan(offset), out bytesWritten, out diagnostic), Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.ToString());
            fields[fieldIndex] = new EbinFieldSource(checked((uint)fieldIndex + 1), 11, 1, 1, new EbinPayloadSlice(offset, bytesWritten));
            fieldIndex++;
            offset = checked(offset + bytesWritten);
        }

        Assert.That(EbinPayloadWriter.Write(0L, payloadStorage.AsSpan(offset), out bytesWritten, out diagnostic), Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.ToString());
        fields[fieldIndex++] = new EbinFieldSource(6, 8, 1, 1, new EbinPayloadSlice(offset, bytesWritten));
        offset = checked(offset + bytesWritten);

        if (forkSourceRecord is not null)
        {
            forkSourceRecord.CopyTo(payloadStorage, offset);
            fields[fieldIndex++] = new EbinFieldSource(8, 22, 0, 1, new EbinPayloadSlice(offset, forkSourceRecord.Length));
            offset = checked(offset + forkSourceRecord.Length);
        }

        Assert.That(EbinPayloadWriter.Write((byte)1, payloadStorage.AsSpan(offset), out bytesWritten, out diagnostic), Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.ToString());
        fields[fieldIndex++] = new EbinFieldSource(9, 1, 1, 1, new EbinPayloadSlice(offset, bytesWritten));
        offset = checked(offset + bytesWritten);
        Assert.That(EbinPayloadWriter.Write(1L, payloadStorage.AsSpan(offset), out bytesWritten, out diagnostic), Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.ToString());
        fields[fieldIndex++] = new EbinFieldSource(10, 8, 1, 1, new EbinPayloadSlice(offset, bytesWritten));
        offset = checked(offset + bytesWritten);
        Assert.That(fieldIndex, Is.EqualTo(fields.Length));

        return WriteRecord(
            new ContentSchemaIdentityAssignment("state/identity-branch").SchemaId,
            fields,
            payloadStorage.AsSpan(0, offset));
    }

    private static byte[] CreateIdentityHistoryRecord(
        Guid worldGuid,
        Guid replayLineageId,
        Guid branchId,
        Guid issuanceNamespace,
        byte[] branchRecord,
        byte[] pageRecord)
    {
        var branchArray = CreateRecordArrayPayload(branchRecord);
        var pageArray = CreateRecordArrayPayload(pageRecord);
        var payloadStorage = new byte[checked((4 * 16) + 8 + branchArray.Length + pageArray.Length)];
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[7];
        var offset = 0;
        var identities = new[] { worldGuid, replayLineageId, branchId, issuanceNamespace };

        for (var index = 0; index < identities.Length; index++)
        {
            _ = identities[index].TryWriteBytes(payloadStorage.AsSpan(offset, 16), bigEndian: true, out _);
            fields[index] = new EbinFieldSource(checked((uint)index + 1), 11, 1, 1, new EbinPayloadSlice(offset, 16));
            offset = checked(offset + 16);
        }

        Assert.That(EbinPayloadWriter.Write(2L, payloadStorage.AsSpan(offset), out var bytesWritten, out var diagnostic), Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.ToString());
        fields[4] = new EbinFieldSource(5, 8, 1, 1, new EbinPayloadSlice(offset, bytesWritten));
        offset = checked(offset + bytesWritten);
        branchArray.CopyTo(payloadStorage, offset);
        fields[5] = new EbinFieldSource(6, 24, 1, 1, new EbinPayloadSlice(offset, branchArray.Length));
        offset = checked(offset + branchArray.Length);
        pageArray.CopyTo(payloadStorage, offset);
        fields[6] = new EbinFieldSource(7, 24, 1, 1, new EbinPayloadSlice(offset, pageArray.Length));
        offset = checked(offset + pageArray.Length);

        return WriteRecord(
            new ContentSchemaIdentityAssignment("state/identity-history").SchemaId,
            fields,
            payloadStorage.AsSpan(0, offset));
    }

    private static byte[] CreateIdentityPageRecord(Guid identity, uint metadata)
    {
        Guid[] identities = [identity];
        uint[] metadataValues = [metadata];
        var identityPayload = new byte[48];
        var metadataPayload = new byte[36];
        Assert.That(EbinPayloadWriter.WriteFixedArray(identities.AsSpan(), EbinReadLimits.Foundation, identityPayload, out var identityBytes, out var diagnostic), Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.ToString());
        Assert.That(identityBytes, Is.EqualTo(identityPayload.Length));
        Assert.That(EbinPayloadWriter.WriteFixedArray(metadataValues.AsSpan(), EbinReadLimits.Foundation, metadataPayload, out var metadataBytes, out diagnostic), Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.ToString());
        Assert.That(metadataBytes, Is.EqualTo(metadataPayload.Length));
        var payloadStorage = new byte[checked(8 + identityPayload.Length + metadataPayload.Length)];
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[3];
        Assert.That(EbinPayloadWriter.Write(1L, payloadStorage, out var bytesWritten, out diagnostic), Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.ToString());
        fields[0] = new EbinFieldSource(1, 8, 1, 1, new EbinPayloadSlice(0, bytesWritten));
        identityPayload.CopyTo(payloadStorage, bytesWritten);
        fields[1] = new EbinFieldSource(2, 23, 1, 1, new EbinPayloadSlice(bytesWritten, identityPayload.Length));
        var offset = checked(bytesWritten + identityPayload.Length);
        metadataPayload.CopyTo(payloadStorage, offset);
        fields[2] = new EbinFieldSource(3, 23, 1, 1, new EbinPayloadSlice(offset, metadataPayload.Length));

        return WriteRecord(
            new ContentSchemaIdentityAssignment("state/identity-page").SchemaId,
            fields,
            payloadStorage);
    }

    private static byte[] CreateRecordArrayPayload(params byte[][] records)
    {
        var recordStorageBytes = 0;

        foreach (var record in records)
        {
            recordStorageBytes = checked(recordStorageBytes + record.Length);
        }

        var recordStorage = new byte[recordStorageBytes];
        var recordSlices = new EbinPayloadSlice[records.Length];
        var offset = 0;

        for (var index = 0; index < records.Length; index++)
        {
            records[index].CopyTo(recordStorage, offset);
            recordSlices[index] = new EbinPayloadSlice(offset, records[index].Length);
            offset = checked(offset + records[index].Length);
        }

        Assert.That(EbinPayloadWriter.MeasureRecordArray(recordSlices, recordStorage, EbinReadLimits.Foundation, out var payloadBytes, out var diagnostic), Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.ToString());
        var payload = new byte[payloadBytes];
        Assert.That(EbinPayloadWriter.WriteRecordArray(recordSlices, recordStorage, EbinReadLimits.Foundation, payload, out var written, out diagnostic), Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.ToString());
        Assert.That(written, Is.EqualTo(payload.Length));
        return payload;
    }

    private static byte[] WriteRecord(
        Guid schemaId,
        scoped ReadOnlySpan<EbinFieldSource> fields,
        scoped ReadOnlySpan<byte> payloadStorage)
    {
        var source = new EbinRecordWriteView(schemaId, 1, 0, fields, payloadStorage);
        Assert.That(EbinStructuralWriter.Measure(in source, EbinReadLimits.Foundation, out var recordBytes, out var diagnostic), Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.ToString());
        var candidate = new byte[recordBytes];
        var record = new byte[recordBytes];
        Assert.That(EbinStructuralWriter.Write(in source, EbinReadLimits.Foundation, candidate, record, out var bytesWritten, out diagnostic), Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.ToString());
        Assert.That(bytesWritten, Is.EqualTo(record.Length));
        return record;
    }

    [Test]
    public void ForkImportedIdentityRequiresValidForkSource()
    {
        var worldGuid = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var creationLineageId = Guid.NewGuid();
        var issuanceNamespace = Guid.NewGuid();
        var targetIdentity = StableIdentityLedger.CreateAutomaticIdentity(
            issuanceNamespace,
            IdentityKind.SceneInstance,
            ordinal: 1);
        var metadata = (1U << 14) | (3U << 12) | (2U << 10);
        var pageRecord = CreateIdentityPageRecord(targetIdentity, metadata);
        var branchRecord = CreateIdentityBranchRecord(branchId, creationLineageId, issuanceNamespace, forkSourceRecord: null);
        var historyRecord = CreateIdentityHistoryRecord(
            worldGuid,
            creationLineageId,
            branchId,
            issuanceNamespace,
            branchRecord,
            pageRecord);

        _ = Assert.Throws<ArgumentException>(() => WorldPersistenceIdentityHistoryReader.Read(
            historyRecord,
            EbinReadLimits.Foundation,
            CreateConfiguration(),
            new WorldPersistenceLiveIdentitySet(maximumCount: 1),
            new WorldPersistenceIdentityScratchEntry[1],
            nextTick: 1,
            parameterName: nameof(historyRecord)));

        var malformedForkSource = CreateForkSourceRecord(
            Guid.NewGuid(),
            Guid.NewGuid(),
            creationLineageId,
            Guid.NewGuid(),
            checkpointDigestLength: 31);
        _ = Assert.Throws<ArgumentException>(() => WorldPersistenceIdentityHistoryReader.ReadForkSource(
            malformedForkSource,
            EbinReadLimits.Foundation,
            nameof(malformedForkSource)));

        var targetWorldGuid = Guid.NewGuid();
        var sourceBranchId = Guid.NewGuid();
        var invalidBranchId = Guid.NewGuid();
        var importedLineageId = Guid.NewGuid();
        var importedForkSource = CreateForkSourceRecord(
            Guid.NewGuid(),
            sourceBranchId,
            importedLineageId,
            Guid.NewGuid());
        var invalidBranchRecord = CreateIdentityBranchRecord(
            invalidBranchId,
            importedLineageId,
            invalidBranchId,
            importedForkSource);
        var targetSceneInstanceId = StableIdentityLedger.CreateAutomaticIdentity(
            targetWorldGuid,
            IdentityKind.SceneInstance,
            ordinal: 1);
        var importedPageRecord = CreateIdentityPageRecord(targetSceneInstanceId, metadata);
        var invalidForkHistoryRecord = CreateIdentityHistoryRecord(
            targetWorldGuid,
            importedLineageId,
            invalidBranchId,
            invalidBranchId,
            invalidBranchRecord,
            importedPageRecord);
        _ = Assert.Throws<ArgumentException>(() => WorldPersistenceIdentityHistoryReader.Read(
            invalidForkHistoryRecord,
            EbinReadLimits.Foundation,
            CreateConfiguration(),
            new WorldPersistenceLiveIdentitySet(maximumCount: 1),
            new WorldPersistenceIdentityScratchEntry[1],
            nextTick: 1,
            parameterName: nameof(invalidForkHistoryRecord)));

        var validBranchId = StableIdentityLedger.CreateForkBranchId(
            targetWorldGuid,
            sourceBranchId);
        var validForkBranchRecord = CreateIdentityBranchRecord(
            validBranchId,
            importedLineageId,
            validBranchId,
            importedForkSource);
        var wrongTargetIdentity = StableIdentityLedger.CreateAutomaticIdentity(
            validBranchId,
            IdentityKind.SceneInstance,
            ordinal: 1);
        var wrongTargetPageRecord = CreateIdentityPageRecord(wrongTargetIdentity, metadata);
        var wrongTargetHistoryRecord = CreateIdentityHistoryRecord(
            targetWorldGuid,
            importedLineageId,
            validBranchId,
            validBranchId,
            validForkBranchRecord,
            wrongTargetPageRecord);
        _ = Assert.Throws<ArgumentException>(() => WorldPersistenceIdentityHistoryReader.Read(
            wrongTargetHistoryRecord,
            EbinReadLimits.Foundation,
            CreateConfiguration(),
            new WorldPersistenceLiveIdentitySet(maximumCount: 1),
            new WorldPersistenceIdentityScratchEntry[1],
            nextTick: 1,
            parameterName: nameof(wrongTargetHistoryRecord)));
    }

    [Test]
    public void ForkImportedIdentityUsesDeepestRetainedSourceNamespace()
    {
        var worldGuid = new Guid("11111111-2222-4333-8444-555555555555");
        var sourceBranchId = new Guid("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");
        var branchId = new Guid("30049d4a-d08c-8ce7-89a3-79d51c84b186");
        var creationLineageId = new Guid("33333333-4444-4555-8666-777777777777");
        var issuanceNamespace = branchId;
        var immediateNamespace = new Guid("66666666-7777-4888-8999-aaaaaaaaaaaa");
        var deepestNamespace = new Guid("bbbbbbbb-cccc-4ddd-8eee-ffffffffffff");
        var priorLineageId = new Guid("44444444-5555-4666-8777-888888888888");
        var priorForkSource = CreateForkSourceRecord(
            new Guid("22222222-3333-4444-8555-666666666666"),
            new Guid("77777777-8888-4999-8aaa-bbbbbbbbbbbb"),
            priorLineageId,
            deepestNamespace);
        var forkSource = CreateForkSourceRecord(
            new Guid("99999999-8888-4777-8666-555555555555"),
            sourceBranchId,
            creationLineageId,
            immediateNamespace,
            priorForkSource);
        var branchRecord = CreateIdentityBranchRecord(branchId, creationLineageId, issuanceNamespace, forkSource);
        var targetIdentity = new Guid("20aeced5-8d92-8423-8f43-4eba3673e317");
        Assert.That(StableIdentityLedger.CreateForkBranchId(worldGuid, sourceBranchId), Is.EqualTo(branchId));
        Assert.That(
            StableIdentityLedger.CreateAutomaticIdentity(worldGuid, IdentityKind.SceneInstance, ordinal: 1),
            Is.EqualTo(targetIdentity));
        Assert.That(
            StableIdentityLedger.CreateAutomaticIdentity(deepestNamespace, IdentityKind.SceneInstance, ordinal: 1),
            Is.EqualTo(new Guid("a4852d11-abef-8919-8df7-dcc083c07d61")));
        var metadata = (1U << 14) | (3U << 12) | (2U << 10);
        var pageRecord = CreateIdentityPageRecord(targetIdentity, metadata);
        var historyRecord = CreateIdentityHistoryRecord(
            worldGuid,
            creationLineageId,
            branchId,
            issuanceNamespace,
            branchRecord,
            pageRecord);
        var identityScratch = new WorldPersistenceIdentityScratchEntry[1];
        var liveIdentities = new WorldPersistenceLiveIdentitySet(maximumCount: 1);
        var configuration = CreateConfiguration();
        var lineageMembershipScratch = new Guid[
            WorldPersistenceIdentityHistoryReader.MeasureLineageMembershipScratchCapacity(
                historyRecord,
                EbinReadLimits.Foundation,
                configuration,
                nameof(historyRecord))];

        _ = WorldPersistenceIdentityHistoryReader.ReadAndIssueLineageMembership(
            historyRecord,
            EbinReadLimits.Foundation,
            configuration,
            liveIdentities,
            identityScratch,
            1,
            nameof(historyRecord),
            lineageMembershipScratch,
            out var validatedLineageMembership);

        Assert.That(identityScratch[0].Origin, Is.EqualTo((byte)IdentityOrigin.ForkImported));
        Assert.That(identityScratch[0].SourceIdentityNamespace, Is.EqualTo(deepestNamespace));
        Assert.That(validatedLineageMembership, Is.Not.Null);
        Assert.That(validatedLineageMembership!.Count, Is.EqualTo(2));
        Assert.That(validatedLineageMembership.WorldGuid, Is.EqualTo(worldGuid));
        Assert.That(validatedLineageMembership.ReplayLineageId, Is.EqualTo(creationLineageId));
        Assert.That(validatedLineageMembership.ActiveBranchId, Is.EqualTo(branchId));
        Assert.That(validatedLineageMembership.IssuanceNamespace, Is.EqualTo(issuanceNamespace));
        Guid[] expectedLineageMembership = [creationLineageId, priorLineageId];
        Array.Sort(expectedLineageMembership);
        Assert.That(
            validatedLineageMembership.LineageIds.ToArray(),
            Is.EqualTo(expectedLineageMembership));
        Array.Fill(lineageMembershipScratch, Guid.Empty);
        Assert.That(validatedLineageMembership.LineageIds.ToArray(), Is.EqualTo(expectedLineageMembership));

        using var admissionDomain = new WorldAdmissionDomain(8 * 1024 * 1024);
        Assert.That(admissionDomain.TryReserveWorld(4096, out var admissionReservation), Is.True);
        using var admissionWorld = admissionReservation!;
        Assert.That(
            admissionDomain.TryReserveFreshLineageHistory(
                admissionWorld,
                validatedLineageMembership!,
                out var lineageGrant,
                out _),
            Is.True);
        Assert.That(lineageGrant.IssuanceNamespace, Is.EqualTo(issuanceNamespace));
        Assert.That(admissionDomain.TryCommitFreshLineage(lineageGrant), Is.True);
        Assert.That(admissionDomain.TryRollbackFreshLineage(lineageGrant), Is.True);

        var undersizedLineageScratch = new Guid[lineageMembershipScratch.Length - 1];
        _ = Assert.Throws<ArgumentException>(() => WorldPersistenceIdentityHistoryReader.Read(
            historyRecord,
            EbinReadLimits.Foundation,
            configuration,
            new WorldPersistenceLiveIdentitySet(maximumCount: 1),
            new WorldPersistenceIdentityScratchEntry[1],
            1,
            nameof(historyRecord),
            undersizedLineageScratch,
            out _));
        Assert.That(undersizedLineageScratch, Is.All.EqualTo(Guid.Empty));
    }

    [Test]
    public void RejectsDuplicateIdentityAcrossRetiredAndLiveAssignments()
    {
        DataSchema[] schemaRows = [
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
            .. AnimationSchemaCatalogue.Schemas,
            .. RenderingSchemaCatalogue.Schemas,
            .. StandaloneContinuationSchemaCatalogue.Schemas,
            WorldPersistenceWorldRecordWriterTests.CreateApplicationSchema(
                "application/world-root-test/tag/v1",
                ContentSchemaRole.EntityTag,
                0),
            WorldPersistenceWorldRecordWriterTests.CreateApplicationSchema(
                "application/world-root-test/system-state/v1",
                ContentSchemaRole.SystemState,
                7),
            WorldPersistenceWorldRecordWriterTests.CreateApplicationSchema(
                "application/world-root-test/resource-state/v1",
                ContentSchemaRole.ResourceState,
                23,
                4),
            WorldPersistenceWorldRecordWriterTests.CreateApplicationSchema(
                "application/world-root-test/attachment-state/v1",
                ContentSchemaRole.AttachmentState,
                8),
        ];
        var schemas = new CompiledContentSchemaRegistry([.. schemaRows]);
        var registration = new WorldRegistrationBuilder(schemas).Build();
        var firstEntityId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var secondEntityId = Guid.Parse("30000000-0000-0000-0000-000000000002");
        var replayLineageId = Guid.NewGuid();
        using var world = World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = replayLineageId,
            WorldGuid = Guid.NewGuid(),
        });
        var firstEntity = world.CreateEntity(new EntityDescription(firstEntityId));
        _ = world.Step();
        world.DestroyEntity(firstEntity, HierarchyDestroyMode.DetachChildrenKeepWorld);
        _ = world.Step();
        _ = world.CreateEntity(new EntityDescription(secondEntityId));
        _ = world.Step();

        using var lease = world.AcquirePersistenceCaptureLease();
        var limits = WorldPersistenceEbinLimits.Create(world.Options);
        var worldRecord = WorldPersistenceWorldRecordWriterTests.ComposeWorldRecord(
            world,
            in lease,
            schemas,
            []);
        var assignmentCount = WorldPersistenceWorldRecordReader.MeasureIdentityAssignmentCount(worldRecord, limits);
        Assert.That(assignmentCount, Is.EqualTo(2));

        var identityScratch = new WorldPersistenceIdentityScratchEntry[assignmentCount];
        var lineageMembershipScratch = new Guid[
            WorldPersistenceWorldRecordReader.MeasureLineageMembershipScratchCapacity(worldRecord, limits)];
        var contents = WorldPersistenceWorldRecordReader.Read(
            worldRecord,
            limits,
            null,
            schemas,
            [],
            identityScratch,
            lineageMembershipScratch);
        Assert.That(contents.Ecs.EntityCount, Is.EqualTo(1));
        Assert.That(contents.Ecs.LineageMembershipCount, Is.EqualTo(1));
        Assert.That(lineageMembershipScratch[0], Is.EqualTo(replayLineageId));
        Assert.That(identityScratch[0].IsLive, Is.False);
        Assert.That(identityScratch[1].IsLive, Is.True);

        var duplicateRecord = (byte[])worldRecord.Clone();
        Span<byte> firstEntityIdBytes = stackalloc byte[16];
        Span<byte> secondEntityIdBytes = stackalloc byte[16];
        _ = firstEntityId.TryWriteBytes(firstEntityIdBytes, bigEndian: true, out _);
        _ = secondEntityId.TryWriteBytes(secondEntityIdBytes, bigEndian: true, out _);
        var firstIdentityOffset = duplicateRecord.AsSpan().IndexOf(firstEntityIdBytes);
        Assert.That(firstIdentityOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(
            duplicateRecord.AsSpan(firstIdentityOffset + firstEntityIdBytes.Length).IndexOf(firstEntityIdBytes),
            Is.EqualTo(-1));
        secondEntityIdBytes.CopyTo(duplicateRecord.AsSpan(firstIdentityOffset, secondEntityIdBytes.Length));

        _ = Assert.Throws<ArgumentException>(() => WorldPersistenceWorldRecordReader.Read(
            duplicateRecord,
            limits,
            targetRegistration: null,
            identityScratch: identityScratch));
        _ = Assert.Throws<ArgumentException>(() => WorldPersistenceWorldRecordReader.Read(
            worldRecord,
            limits,
            targetRegistration: null,
            identityScratch: new WorldPersistenceIdentityScratchEntry[assignmentCount + 1]));
    }
}
