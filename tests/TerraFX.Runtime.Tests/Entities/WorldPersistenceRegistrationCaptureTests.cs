// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Animation;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Hosting;
using TerraFX.Integration;
using TerraFX.Numerics;
using TerraFX.Rendering;
using TerraFX.Runtime.Tests.Hosting;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceRegistrationCaptureTests
{
    private static byte[] CreateExpectedFileContentDigest(
        long generation,
        ReadOnlySpan<byte> checkpointRecord,
        EbinReadLimits limits)
    {
        var payloadStorage = new byte[checked(8 + checkpointRecord.Length)];
        var status = EbinPayloadWriter.Write(generation, payloadStorage, out var generationBytes, out _);
        Assert.That(status, Is.EqualTo(EbinWriteStatus.Complete));
        checkpointRecord.CopyTo(payloadStorage.AsSpan(generationBytes));
        Span<EbinFieldSource> fields = [
            new EbinFieldSource(1, 8, 1, 1, new EbinPayloadSlice(0, generationBytes)),
            new EbinFieldSource(2, 22, 1, 1, new EbinPayloadSlice(generationBytes, checkpointRecord.Length)),
        ];
        var source = new EbinRecordWriteView(
            new ContentSchemaIdentityAssignment("state/file-content").SchemaId,
            major: 1,
            minor: 0,
            fields,
            payloadStorage);
        status = EbinStructuralWriter.Measure(in source, limits, out var recordBytes, out _);
        Assert.That(status, Is.EqualTo(EbinWriteStatus.Complete));
        var candidateStorage = new byte[recordBytes];
        var record = new byte[recordBytes];
        status = EbinStructuralWriter.Write(
            in source,
            limits,
            candidateStorage,
            record,
            out var bytesWritten,
            out _);
        Assert.That(status, Is.EqualTo(EbinWriteStatus.Complete));
        Assert.That(bytesWritten, Is.EqualTo(record.Length));
        var prefix = "TerraFX.WorldFile.v1\0"u8;
        var preimage = new byte[checked(prefix.Length + record.Length)];
        prefix.CopyTo(preimage);
        record.CopyTo(preimage.AsSpan(prefix.Length));
        return SHA256.HashData(preimage);
    }

    private static CompiledContentSchemaRegistry CreateWorldRootSchemas(params DataSchema[] additionalSchemas)
        => new([
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
            .. additionalSchemas,
        ]);

    private static int FindSequence(ReadOnlySpan<byte> source, ReadOnlySpan<byte> sequence)
    {
        for (var index = 0; index <= source.Length - sequence.Length; index++)
        {
            if (source.Slice(index, sequence.Length).SequenceEqual(sequence))
            {
                return index;
            }
        }

        return -1;
    }

    [Test]
    public void CapturesRegistrationRootFromFrozenWorldRegistration()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        using var world = World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        _ = world.Step();
        using var lease = world.AcquirePersistenceCaptureLease();
        Assert.That(
            WorldPersistenceRegistrationCaptureCoordinator.Capture(
                world,
                lease,
                out var record,
                out var reservation,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        var capturedRecord = record ?? throw new InvalidOperationException("The registration capture did not produce its record.");
        using var reservationScope = reservation
            ?? throw new InvalidOperationException("The registration capture did not retain its admission reservation.");
        var assertions = new RegistrationCaptureAssertions();
        Assert.That(
            EbinStructuralReader.Read(
                capturedRecord,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref assertions,
                out var bytesConsumed,
                out var readDiagnostic),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(readDiagnostic, Is.Null);
        Assert.That(assertions.IsMatch, Is.True);
        Assert.That(bytesConsumed, Is.EqualTo(capturedRecord.Length));
    }

    [Test]
    public void CapturesWorldRootWithoutSceneSupport()
    {
        var schemas = CreateWorldRootSchemas();
        var registration = new WorldRegistrationBuilder(schemas).Build();
        using var world = World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var sourceEntity = world.CreateEntity(new EntityDescription());
        _ = world.Step();
        using var lease = world.AcquirePersistenceCaptureLease();
        Assert.That(
            WorldPersistenceCaptureCoordinator.Capture(
                world,
                lease,
                schemas,
                out var captureReservation,
                out var record,
                out var assetIdCount,
                out var diagnostic,
                CancellationToken.None),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        using var retainedCaptureReservation = captureReservation
            ?? throw new InvalidOperationException("The successful World capture did not retain its admission reservation.");
        Assert.That(assetIdCount, Is.Zero);
        var capturedRecord = record ?? throw new InvalidOperationException("The World capture coordinator did not produce its record.");
        using var capturedRecordUse = retainedCaptureReservation.AcquireUse(world, capturedRecord.Length);
        var assertions = new WorldPersistenceWorldRecordWriterTests.WorldRecordAssertions(
            lease.Frame2.HasValue,
            lease.ControlRecordCount,
            []);
        Assert.That(
            EbinStructuralReader.Read(
                capturedRecord,
                WorldPersistenceEbinLimits.Create(world.Options),
                EbinReadPolicy.Canonical,
                ref assertions,
                out var bytesConsumed,
                out var readDiagnostic),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(readDiagnostic, Is.Null);
        Assert.That(bytesConsumed, Is.EqualTo(capturedRecord.Length));

        var metadata = WorldPersistenceHeaderWriterTests.CreateMetadata();
        var profileRecord = WorldPersistenceHeaderWriterTests.CreateFoundationProfileRecord();
        var header = WorldPersistenceHeaderWriter.Create(
            metadata,
            WorldPersistenceMode.Standard,
            [],
            lease.WorldGuid,
            world.ReplayLineageId,
            lease.NextTickIndex,
            captureWorkerCount: 0,
            profileRecord);
        var limits = WorldPersistenceEbinLimits.Create(world.Options);
        var (firstCheckpointRecord, firstFileRecord, firstCanonicalHash, firstCheckpointDigest, firstFileContentDigest) =
            WorldPersistenceCheckpointWriter.Create(header, capturedRecord, 1, limits);
        var changedWorkerHeader = WorldPersistenceHeaderWriter.Create(
            metadata,
            WorldPersistenceMode.Standard,
            [],
            lease.WorldGuid,
            world.ReplayLineageId,
            lease.NextTickIndex,
            captureWorkerCount: 1,
            profileRecord);
        var (_, _, changedWorkerCanonicalHash, changedWorkerCheckpointDigest, _) =
            WorldPersistenceCheckpointWriter.Create(changedWorkerHeader, capturedRecord, 1, limits);
        var (_, nextGenerationFileRecord, nextGenerationCanonicalHash, nextGenerationCheckpointDigest, nextGenerationFileContentDigest) =
            WorldPersistenceCheckpointWriter.Create(header, capturedRecord, 2, limits);

        Assert.Multiple(() => {
            Assert.That(firstCanonicalHash, Is.EqualTo(changedWorkerCanonicalHash));
            Assert.That(firstCheckpointDigest, Is.Not.EqualTo(changedWorkerCheckpointDigest));
            Assert.That(firstCanonicalHash, Is.EqualTo(nextGenerationCanonicalHash));
            Assert.That(firstCheckpointDigest, Is.EqualTo(nextGenerationCheckpointDigest));
            Assert.That(firstFileContentDigest, Is.Not.EqualTo(nextGenerationFileContentDigest));
            Assert.That(firstCheckpointDigest, Is.EqualTo(SHA256.HashData(firstCheckpointRecord)));
            Assert.That(firstFileContentDigest, Is.EqualTo(
                CreateExpectedFileContentDigest(1, firstCheckpointRecord, limits)));
            Assert.That(EbinStructuralReader.Validate(
                firstFileRecord,
                limits,
                EbinReadPolicy.Canonical,
                out var fileBytesConsumed,
                out _), Is.EqualTo(EbinReadStatus.Complete));
            Assert.That(fileBytesConsumed, Is.EqualTo(firstFileRecord.Length));
            Assert.That(nextGenerationFileRecord.Length, Is.EqualTo(firstFileRecord.Length));
        });

        Assert.That(WorldPersistenceCheckpointWriter.ReadTransactionGeneration(firstFileRecord, limits), Is.EqualTo(1));
        Assert.That(WorldPersistenceCheckpointWriter.ReadCheckpointRecord(firstFileRecord, limits), Is.EqualTo(firstCheckpointRecord));
        var identityAssignmentCount = WorldPersistenceCheckpointWriter.MeasureIdentityAssignmentCount(firstFileRecord, limits);
        var lineageMembershipCapacity = WorldPersistenceCheckpointWriter.MeasureLineageMembershipScratchCapacity(firstFileRecord, limits);
        var lineageMembershipScratch = new Guid[lineageMembershipCapacity];
        var readContents = WorldPersistenceCheckpointWriter.ReadContents(
            firstFileRecord,
            limits,
            registration,
            schemas,
            new uint[assetIdCount],
            new WorldPersistenceIdentityScratchEntry[identityAssignmentCount],
            lineageMembershipScratch);
        Assert.That(readContents.WorldContents.Ecs.LineageMembershipCount, Is.EqualTo(1));
        Assert.That(lineageMembershipScratch[0], Is.EqualTo(world.ReplayLineageId));
        capturedRecordUse.Dispose();
        retainedCaptureReservation.Dispose();
        lease.Dispose();

        var identityHistory = readContents.WorldContents.Ecs.ValidatedIdentityHistory
            ?? throw new InvalidOperationException("The captured checkpoint did not retain its validated identity history.");
        var branchId = Guid.NewGuid();
        var restoreState = WorldRestoreIdentityState.CreateBranch(
            identityHistory,
            branchId,
            readContents.CheckpointDigest.Span,
            readContents.NextTick,
            isStrictLocal: false,
            readContents.WorldContents.Configuration);
        Assert.That(
            world.TryPrepareLoadReplacement(
                world.WorldGuid,
                sourceBytes: 4096,
                targetBytes: 4096,
                dataBytes: 4096,
                cleanupBytes: 4096,
                out var replacementPreparation,
                out var bindingRefusal,
                out var admissionRefusal),
            Is.True);
        Assert.That(bindingRefusal.IsNone, Is.True);
        Assert.That(admissionRefusal, Is.EqualTo(WorldAdmissionRefusal.None));
        using (var preparation = replacementPreparation
            ?? throw new InvalidOperationException("The captured World did not admit its branch replacement."))
        {
            Assert.That(
                world.TryPrepareRestoreConstructionPlan(
                    restoreState,
                    readContents.WorldContents.Configuration,
                    preparation,
                    readContents.WorldContents.Clock.TickRate,
                    readContents.WorldContents.Clock.InitialTimeScale,
                    readContents.WorldContents.Clock.TimeScale,
                    out var constructionPlan,
                    out bindingRefusal,
                    out admissionRefusal),
                Is.True);
            Assert.That(bindingRefusal.IsNone, Is.True);
            Assert.That(admissionRefusal, Is.EqualTo(WorldAdmissionRefusal.None));
            var plan = constructionPlan
                ?? throw new InvalidOperationException("The captured World did not produce its restore construction plan.");

            try
            {
                using var candidate = World.CreateRestoreCandidate(plan);
                World.InitializeRestoreCandidate(candidate);
                candidate.RestoreEcsRows(
                    readContents.WorldContents.EcsRecord,
                    readContents.WorldContents.Ecs,
                    CancellationToken.None);
                var sourceEntityId = world.GetEntityId(sourceEntity);
                Assert.That(candidate.TryGetEntity(sourceEntityId, out var restoredEntity), Is.True);
                Assert.That(candidate.GetEntityId(restoredEntity), Is.EqualTo(sourceEntityId));
                Assert.That(restoredEntity.OwnerId, Is.EqualTo(candidate.OwnerId));
            }
            finally
            {
                plan.ReleaseUntransferredRestoreResources();
            }
        }

        var tamperedFileRecord = (byte[])firstFileRecord.Clone();
        var fileDigestOffset = FindSequence(tamperedFileRecord, firstFileContentDigest);
        Assert.That(fileDigestOffset, Is.GreaterThanOrEqualTo(0));
        tamperedFileRecord[fileDigestOffset] ^= 0x01;
        Assert.That(
            () => WorldPersistenceCheckpointWriter.ReadTransactionGeneration(tamperedFileRecord, limits),
            Throws.ArgumentException);

        world.SetTimeScale(0.5f);
        _ = world.Step();
        using var nextLease = world.AcquirePersistenceCaptureLease();
        var nextCaptureStatus = WorldPersistenceCaptureCoordinator.Capture(
            world,
            nextLease,
            schemas,
            out var nextCaptureReservation,
            out var nextRecord,
            out _,
            out var nextDiagnostic,
            CancellationToken.None);
        Assert.That(nextCaptureStatus, Is.EqualTo(EbinWriteStatus.Complete), nextDiagnostic?.Category);
        using var nextReservation = nextCaptureReservation
            ?? throw new InvalidOperationException("The next World capture did not retain its admission reservation.");
        var nextWorldRecord = nextRecord
            ?? throw new InvalidOperationException("The next World capture did not produce its record.");
        var nextHeader = WorldPersistenceHeaderWriter.Create(
            metadata,
            WorldPersistenceMode.Standard,
            [],
            nextLease.WorldGuid,
            world.ReplayLineageId,
            nextLease.NextTickIndex,
            captureWorkerCount: 0,
            profileRecord);
        var (_, _, nextWorldCanonicalHash, _, _) =
            WorldPersistenceCheckpointWriter.Create(nextHeader, nextWorldRecord, 3, limits);
        Assert.That(nextWorldCanonicalHash, Is.Not.EqualTo(firstCanonicalHash));
    }

    [Test]
    public void RestoresHeterogeneousEcsAndRegisteredOwnerStateThroughCandidateContext()
    {
        var derivedAssignment = new ContentSchemaIdentityAssignment("application/world-root-test/derived-light-flux/v1");
        var derivedSchemaReference = new ContentSchemaReference(
            derivedAssignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var derivedMetadata = new DataSchema(
            derivedSchemaReference,
            derivedAssignment,
            ContentSchemaRole.Component,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.Transient,
            ContentTransientRestore.ExactDerivedRehydration,
            []);
        var resetAssignment = new ContentSchemaIdentityAssignment("application/world-root-test/reset-restore/v1");
        var resetSchemaReference = new ContentSchemaReference(
            resetAssignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var resetMetadata = new DataSchema(
            resetSchemaReference,
            resetAssignment,
            ContentSchemaRole.Component,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.Transient,
            ContentTransientRestore.ResetDeclaredDefaults,
            []);
        var schemas = CreateWorldRootSchemas(derivedMetadata, resetMetadata);
        var registrationBuilder = new WorldRegistrationBuilder(schemas);
        _ = RenderComponentDefinitions.RegisterPointLight(registrationBuilder, schemas);
        var tagSchema = new EntityTagSchema(
            new ContentSchemaIdentityAssignment("application/world-root-test/tag/v1").SchemaId,
            1);
        _ = registrationBuilder.RegisterTag(tagSchema);
        var systemSchemaReference = new ContentSchemaReference(
            new ContentSchemaIdentityAssignment("application/world-root-test/system-state/v1").SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var systemSchema = WorldSystemSchema<RestoreProbeSystem>.FromRegistry(schemas, systemSchemaReference);
        var systemSourcePath = new WorldSystemSourcePath(["Counter"]);
        var systemField = WorldSystemFieldBinding<RestoreProbeSystem, RestoreProbeAuthor>.Scalar<
            int,
            RestoreProbeSystemCounterReader,
            RestoreProbeAuthorCounterReader>(
                systemSchema.GetField(new ComponentFieldPath([ComponentPropertySegment.FieldTag(1)])),
                [systemSourcePath],
                [systemSourcePath],
                default,
                default);
        var systemCodec = new RestoreProbeCodec(systemSchemaReference);
        var systemFactory = WorldSystemFactoryBinding.Stateful<
            RestoreProbeSystem,
            RestoreProbeAuthor,
            RestoreProbeCreate,
            RestoreProbeDefaults,
            RestoreProbeRelease>(
                Guid.NewGuid(),
                1,
                systemSchema,
                new WorldSystemSourceStateDeclaration(
                    typeof(RestoreProbeSystem),
                    [new WorldSystemSourceLeaf(systemSourcePath, typeof(int), WorldSystemSourcePurpose.Authoritative)]),
                [systemField],
                default,
                default,
                systemCodec,
                default);
        _ = registrationBuilder.RegisterSystem(
            new SystemDescription(
                Guid.NewGuid(),
                0,
                SimulationStage.Input,
                [],
                [],
                [],
                SystemThreadMode.OwnerThread),
            new RestoreProbeFactory(systemFactory));
        var resourceSchemaReference = new ContentSchemaReference(
            new ContentSchemaIdentityAssignment("application/world-root-test/resource-state/v1").SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var resourceRestoreCount = WorldPersistenceEcsProviderTests.RegisterResource(
            registrationBuilder,
            schemas,
            resourceSchemaReference,
            Guid.NewGuid());
        var pointLightSchemaReference = new ContentSchemaReference(
            new ContentSchemaIdentityAssignment("render/point-light").SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var pointLightSchema = ComponentSchema<RenderComponentDefinitions.PointLightComponent>.FromRegistry(
            schemas,
            pointLightSchemaReference,
            new ComponentLayout(
                new ContentSchemaIdentityAssignment("render/point-light/layout/v1").SchemaId,
                System.Runtime.CompilerServices.Unsafe.SizeOf<RenderComponentDefinitions.PointLightComponent>()));
        var derivedSchema = ComponentSchema<DerivedLightFlux>.FromRegistry(
            schemas,
            derivedSchemaReference,
            new ComponentLayout(Guid.NewGuid(), sizeof(float)));
        var derivedBinding = ComponentFactoryBinding<DerivedLightFlux>.Create(
            Guid.NewGuid(),
            1,
            derivedSchema,
            new ComponentStorageDeclaration(
                sizeof(float),
                [],
                [new ComponentPaddingInterval(0, sizeof(float))]),
            ComponentTransientRestore.Derived,
            [ComponentDerivedDependency.FromSchema(pointLightSchema)],
            [],
            new DerivedLightFluxDefaults(),
            new DerivedLightFluxRestorer(),
            new DerivedLightFluxCodec());
        _ = registrationBuilder.RegisterComponent(derivedSchema, new DerivedLightFluxFactory(derivedBinding));
        var resetSchema = ComponentSchema<ResetMarker>.FromRegistry(
            schemas,
            resetSchemaReference,
            new ComponentLayout(Guid.NewGuid(), sizeof(int)));
        var resetBinding = ComponentFactoryBinding<ResetMarker>.Create(
            Guid.NewGuid(),
            1,
            resetSchema,
            new ComponentStorageDeclaration(
                sizeof(int),
                [],
                [new ComponentPaddingInterval(0, sizeof(int))]),
            ComponentTransientRestore.Reset,
            [],
            [],
            new ResetMarkerDefaults(),
            new ResetMarkerRestorer(),
            new ResetMarkerCodec());
        _ = registrationBuilder.RegisterComponent(resetSchema, new ResetMarkerFactory(resetBinding));
        var registration = registrationBuilder.Build();
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        using var world = World.Create(new WorldOptions(registration) {
            AdmissionDomain = domain,
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var pointLight = new RenderComponentDefinitions.PointLightComponent(
            new Vector3(1.0f, 0.5f, 0.25f),
            luminousFlux: 800.0f,
            range: 10.0f,
            layerMask: ulong.MaxValue);
        var sourceDerived = new DerivedLightFlux(-1.0f);
        var sourceReset = new ResetMarker(-1);
        var entity = world.CreateEntity(new EntityDescription(components: [
            ComponentInitialization.Value(in pointLight),
            ComponentInitialization.Value(in sourceDerived),
            ComponentInitialization.Value(in sourceReset),
        ], tags: [tagSchema.SchemaId]));
        var secondPointLight = new RenderComponentDefinitions.PointLightComponent(
            new Vector3(0.25f, 0.5f, 1.0f),
            luminousFlux: 250.0f,
            range: 4.0f,
            layerMask: 0x0F);
        var secondEntity = world.CreateEntity(new EntityDescription(components: [
            ComponentInitialization.Value(in secondPointLight),
        ]));
        _ = world.Step();
        var metadata = WorldPersistenceHeaderWriterTests.CreateMetadata();
        var profileRecord = WorldPersistenceHeaderWriterTests.CreateFoundationProfileRecord();
        var limits = WorldPersistenceEbinLimits.Create(world.Options);
        WorldPersistenceCheckpointContents? checkpointContents = null;

        using (var lease = world.AcquirePersistenceCaptureLease())
        {
            Assert.That(
                WorldPersistenceCaptureCoordinator.Capture(
                    world,
                    lease,
                    schemas,
                    out var captureReservation,
                    out var worldRecord,
                    out var assetIdCount,
                    out var diagnostic,
                    CancellationToken.None),
                Is.EqualTo(EbinWriteStatus.Complete),
                diagnostic?.Category);
            Assert.That(diagnostic, Is.Null);
            using var retainedCaptureReservation = captureReservation
                ?? throw new InvalidOperationException("The successful World capture did not retain its admission reservation.");
            var capturedWorldRecord = worldRecord
                ?? throw new InvalidOperationException("The World capture coordinator did not produce its record.");
            using var capturedRecordUse = retainedCaptureReservation.AcquireUse(world, capturedWorldRecord.Length);
            var header = WorldPersistenceHeaderWriter.Create(
                metadata,
                WorldPersistenceMode.Standard,
                [],
                lease.WorldGuid,
                world.ReplayLineageId,
                lease.NextTickIndex,
                captureWorkerCount: 0,
                profileRecord);
            var (_, fileRecord, _, _, _) = WorldPersistenceCheckpointWriter.Create(header, capturedWorldRecord, 1, limits);
            var assignmentCount = WorldPersistenceCheckpointWriter.MeasureIdentityAssignmentCount(fileRecord, limits);
            var lineageScratchCapacity = WorldPersistenceCheckpointWriter.MeasureLineageMembershipScratchCapacity(fileRecord, limits);
            checkpointContents = WorldPersistenceCheckpointWriter.ReadContents(
                fileRecord,
                limits,
                registration,
                schemas,
                new uint[assetIdCount],
                new WorldPersistenceIdentityScratchEntry[assignmentCount],
                new Guid[lineageScratchCapacity]);
        }

        var checkpoint = checkpointContents
            ?? throw new InvalidOperationException("The real World capture did not produce validated checkpoint contents.");
        var capturedEcs = new EbinRecordReadView(checkpoint.WorldContents.EcsRecord.Span);
        var capturedTransientSchemas = new ContentSchemaReference[2];
        var foundTransientSignature = false;

        for (var entityIndex = 0; entityIndex < checkpoint.WorldContents.Ecs.EntityCount; entityIndex++)
        {
            var capturedEntity = capturedEcs.GetRecordArrayElement(1, entityIndex);

            if (capturedEntity.GetField(3).Count != (ulong)capturedTransientSchemas.Length)
            {
                continue;
            }

            for (var index = 0; index < capturedTransientSchemas.Length; index++)
            {
                capturedTransientSchemas[index] = WorldPersistenceEcsReader.ReadTransientComponentSchemaReference(
                    capturedEntity.GetRecordArrayElement(4, index),
                    nameof(checkpoint));
            }

            foundTransientSignature = true;
            break;
        }

        Assert.That(foundTransientSignature, Is.True);
        Assert.That(capturedTransientSchemas, Does.Contain(derivedSchemaReference));
        Assert.That(capturedTransientSchemas, Does.Contain(resetSchemaReference));
        var identityHistory = checkpoint.WorldContents.Ecs.ValidatedIdentityHistory
            ?? throw new InvalidOperationException("The captured checkpoint did not retain its validated identity history.");
        var restoreState = WorldRestoreIdentityState.CreateBranch(
            identityHistory,
            Guid.NewGuid(),
            checkpoint.CheckpointDigest.Span,
            checkpoint.NextTick,
            isStrictLocal: false,
            checkpoint.WorldContents.Configuration);
        var restoreEcsPlan = WorldPersistenceEcsReader.CreateRestorePlan(
            checkpoint.WorldContents.EcsRecord,
            checkpoint.WorldContents.Ecs,
            registration,
            checkpoint.WorldContents.Configuration);
        var targetBytes = world.MeasureRestoreWorldOwnedBytes(
            restoreState,
            checkpoint.WorldContents.Configuration,
            checkpoint.WorldContents.Clock.TickRate,
            checkpoint.WorldContents.Clock.InitialTimeScale,
            checkpoint.WorldContents.Clock.TimeScale,
            restoreEcsPlan);
        Assert.That(
            world.TryPrepareLoadReplacement(
                world.WorldGuid,
                sourceBytes: 4096,
                targetBytes: targetBytes,
                dataBytes: checked(4096 + restoreEcsPlan.OwnedByteReservation),
                cleanupBytes: 4096,
                out var replacementPreparation,
                out var bindingRefusal,
                out var admissionRefusal),
            Is.True);
        Assert.That(bindingRefusal.IsNone, Is.True);
        Assert.That(admissionRefusal, Is.EqualTo(WorldAdmissionRefusal.None));
        using var preparation = replacementPreparation
            ?? throw new InvalidOperationException("The captured World did not admit its branch replacement.");
        Assert.That(
            world.TryPrepareRestoreConstructionPlan(
                restoreState,
                checkpoint.WorldContents.Configuration,
                preparation,
                checkpoint.WorldContents.Clock.TickRate,
                checkpoint.WorldContents.Clock.InitialTimeScale,
                checkpoint.WorldContents.Clock.TimeScale,
                out var constructionPlan,
                out bindingRefusal,
                out admissionRefusal,
                restoreEcsPlan: restoreEcsPlan),
            Is.True);
        Assert.That(bindingRefusal.IsNone, Is.True);
        Assert.That(admissionRefusal, Is.EqualTo(WorldAdmissionRefusal.None));
        var plan = constructionPlan
            ?? throw new InvalidOperationException("The captured World did not produce its restore construction plan.");

        try
        {
            using var candidate = World.CreateRestoreCandidate(plan);
            World.InitializeRestoreCandidate(candidate);
            candidate.RestoreEcsRows(
                checkpoint.WorldContents.EcsRecord,
                checkpoint.WorldContents.Ecs,
                CancellationToken.None);
            Assert.That(candidate.TryGetEntity(world.GetEntityId(entity), out var restoredEntity), Is.True);
            Assert.That(candidate.TryGet(restoredEntity, out RenderComponentDefinitions.PointLightComponent restoredPointLight), Is.True);
            Assert.That(candidate.TryGet(restoredEntity, out DerivedLightFlux restoredDerived), Is.True);
            Assert.That(candidate.TryGet(restoredEntity, out ResetMarker restoredReset), Is.True);
            Assert.That(candidate.HasTag(restoredEntity, tagSchema.SchemaId), Is.True);
            Assert.That(candidate.TryGetEntity(world.GetEntityId(secondEntity), out var restoredSecondEntity), Is.True);
            Assert.That(candidate.TryGet(restoredSecondEntity, out RenderComponentDefinitions.PointLightComponent restoredSecondPointLight), Is.True);
            Assert.That(candidate.TryGet(restoredSecondEntity, out DerivedLightFlux _), Is.False);
            Assert.That(candidate.TryGet(restoredSecondEntity, out ResetMarker _), Is.False);
            Assert.That(candidate.HasTag(restoredSecondEntity, tagSchema.SchemaId), Is.False);
            Assert.Multiple(() => {
                Assert.That(restoredPointLight.Color, Is.EqualTo(pointLight.Color));
                Assert.That(restoredPointLight.LuminousFlux, Is.EqualTo(pointLight.LuminousFlux));
                Assert.That(restoredPointLight.Range, Is.EqualTo(pointLight.Range));
                Assert.That(restoredPointLight.LayerMask, Is.EqualTo(pointLight.LayerMask));
                Assert.That(restoredDerived.LuminousFlux, Is.EqualTo(pointLight.LuminousFlux * 2.0f));
                Assert.That(restoredReset.Value, Is.EqualTo(17));
                Assert.That(restoredSecondPointLight.Color, Is.EqualTo(secondPointLight.Color));
                Assert.That(restoredSecondPointLight.LuminousFlux, Is.EqualTo(secondPointLight.LuminousFlux));
                Assert.That(restoredSecondPointLight.Range, Is.EqualTo(secondPointLight.Range));
                Assert.That(restoredSecondPointLight.LayerMask, Is.EqualTo(secondPointLight.LayerMask));
                Assert.That(systemCodec.RestoreCount, Is.EqualTo(1));
                Assert.That(systemCodec.RestoredCounter, Is.EqualTo(124));
                Assert.That(resourceRestoreCount(), Is.EqualTo(1));
            });
        }
        finally
        {
            plan.ReleaseUntransferredRestoreResources();
        }
    }

    [Test]
    public void RestoresRegisteredAttachmentStateIntoAnEmptyCandidate()
    {
        var schemas = CreateWorldRootSchemas();
        var attachmentSchema = new ContentSchemaReference(
            new ContentSchemaIdentityAssignment("application/world-root-test/attachment-state/v1").SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var codec = new RestoreAttachmentCodec(attachmentSchema);
        var attachmentField = WorldAttachmentFieldBinding<RestoreAttachmentValue, RestoreAttachmentAuthor>.Value<
            long,
            RestoreAttachmentValueReader,
            RestoreAttachmentAuthorReader>(
                schemas,
                attachmentSchema,
                new ComponentFieldPath([ComponentPropertySegment.FieldTag(1)]),
                default,
                default);
        var attachmentFactory = WorldAttachmentFactoryBinding<RestoreAttachmentValue, RestoreAttachmentAuthor>.Create(
            Guid.NewGuid(),
            1,
            schemas,
            attachmentSchema,
            [attachmentField],
            codec);
        var registrationBuilder = new WorldRegistrationBuilder(schemas);
        _ = registrationBuilder.RegisterAttachment(AttachmentScope.World, attachmentFactory);
        var registration = registrationBuilder.Build();

        using var admissionDomain = new WorldAdmissionDomain(512L * 1024 * 1024);
        using var world = World.Create(new WorldOptions(registration) {
            AdmissionDomain = admissionDomain,
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        world.SetAttachment(new RestoreAttachmentValue { Counter = 124 }, AttachmentOwnership.Transferred);
        _ = world.Step();
        using var lease = world.AcquirePersistenceCaptureLease();
        var worldRecord = WorldPersistenceWorldRecordWriterTests.ComposeWorldRecord(
            world,
            in lease,
            schemas,
            []);
        var limits = WorldPersistenceEbinLimits.Create(world.Options);
        var identityScratch = new WorldPersistenceIdentityScratchEntry[
            WorldPersistenceWorldRecordReader.MeasureIdentityAssignmentCount(worldRecord, limits)];
        var lineageScratch = new Guid[
            WorldPersistenceWorldRecordReader.MeasureLineageMembershipScratchCapacity(worldRecord, limits)];
        var strictLayoutBuffers = default(WorldPersistenceStrictLayoutReadBuffers);
        var savedWorld = WorldPersistenceWorldRecordReader.Read(
            worldRecord,
            limits,
            registration,
            identityScratch,
            lineageScratch,
            ref strictLayoutBuffers);
        var identityHistory = savedWorld.Ecs.ValidatedIdentityHistory
            ?? throw new InvalidOperationException("The saved World omitted its validated identity history.");
        var restoreIdentityState = WorldRestoreIdentityState.CreateBranch(
            identityHistory,
            Guid.NewGuid(),
            new byte[32],
            savedWorld.Clock.NextTick,
            isStrictLocal: false,
            savedWorld.Configuration);
        var restoreEcsPlan = WorldPersistenceEcsReader.CreateRestorePlan(
            savedWorld.EcsRecord,
            savedWorld.Ecs,
            registration,
            savedWorld.Configuration);
        var targetBytes = world.MeasureRestoreWorldOwnedBytes(
            restoreIdentityState,
            savedWorld.Configuration,
            savedWorld.Clock.TickRate,
            savedWorld.Clock.InitialTimeScale,
            savedWorld.Clock.TimeScale,
            restoreEcsPlan,
            savedWorld.ControlRecordCount,
            savedWorld.SceneControlPayloadBytes,
            savedWorld.Ecs.AttachmentCount);
        Assert.That(savedWorld.Ecs.AttachmentCount, Is.EqualTo(1));
        Assert.That(
            world.TryPrepareLoadReplacement(
                world.WorldGuid,
                sourceBytes: worldRecord.Length,
                targetBytes,
                dataBytes: checked(worldRecord.Length + restoreEcsPlan.OwnedByteReservation + 4096L),
                cleanupBytes: targetBytes,
                out var replacementPreparation,
                out var bindingRefusal,
                out var admissionRefusal),
            Is.True);
        Assert.That(bindingRefusal.IsNone, Is.True);
        Assert.That(admissionRefusal, Is.EqualTo(WorldAdmissionRefusal.None));
        using var preparation = replacementPreparation
            ?? throw new InvalidOperationException("The attachment candidate test did not retain its replacement preparation.");
        Assert.That(
            world.TryPrepareRestoreConstructionPlan(
                restoreIdentityState,
                savedWorld.Configuration,
                preparation,
                savedWorld.Clock.TickRate,
                savedWorld.Clock.InitialTimeScale,
                savedWorld.Clock.TimeScale,
                out var constructionPlan,
                out bindingRefusal,
                out admissionRefusal,
                restoreEcsPlan,
                savedWorld.ControlRecordCount,
                savedWorld.SceneControlPayloadBytes,
                savedWorld.Ecs.AttachmentCount),
            Is.True);
        Assert.That(bindingRefusal.IsNone, Is.True);
        Assert.That(admissionRefusal, Is.EqualTo(WorldAdmissionRefusal.None));
        var candidatePlan = constructionPlan
            ?? throw new InvalidOperationException("The attachment candidate test did not produce its candidate plan.");

        try
        {
            using (var candidate = World.CreateRestoreCandidate(candidatePlan))
            {
                World.InitializeRestoreCandidate(candidate);
                candidate.RestoreEcsRows(savedWorld.EcsRecord, savedWorld.Ecs, CancellationToken.None);
                candidate.RestoreHierarchyLinks([], []);
                candidate.RestoreCheckpointClockAndControls(
                    worldRecord,
                    savedWorld.Clock,
                    savedWorld.Cursors,
                    savedWorld.ControlRecordCount,
                    savedWorld.SceneControlPayloadBytes,
                    CancellationToken.None);

                Assert.That(candidate.TryGetAttachment<RestoreAttachmentValue>(out var restoredAttachment), Is.True);
                Assert.That(restoredAttachment?.Counter, Is.EqualTo(124));
            }

            Assert.That(codec.RestoreCount, Is.EqualTo(1));
            Assert.That(codec.ReleaseCount, Is.EqualTo(1));
        }
        finally
        {
            candidatePlan.ReleaseUntransferredRestoreResources();
        }
    }

    [Test]
    public void RestoresRegisteredSystemStateIntoAnEmptyCandidate()
    {
        var schemas = CreateWorldRootSchemas();
        var registrationBuilder = new WorldRegistrationBuilder(schemas);
        var systemSchemaReference = new ContentSchemaReference(
            new ContentSchemaIdentityAssignment("application/world-root-test/system-state/v1").SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var systemSchema = WorldSystemSchema<RestoreProbeSystem>.FromRegistry(schemas, systemSchemaReference);
        var systemSourcePath = new WorldSystemSourcePath(["Counter"]);
        var systemField = WorldSystemFieldBinding<RestoreProbeSystem, RestoreProbeAuthor>.Scalar<
            int,
            RestoreProbeSystemCounterReader,
            RestoreProbeAuthorCounterReader>(
                systemSchema.GetField(new ComponentFieldPath([ComponentPropertySegment.FieldTag(1)])),
                [systemSourcePath],
                [systemSourcePath],
                default,
                default);
        var systemCodec = new RestoreProbeCodec(systemSchemaReference);
        var systemFactory = WorldSystemFactoryBinding.Stateful<
            RestoreProbeSystem,
            RestoreProbeAuthor,
            RestoreProbeCreate,
            RestoreProbeDefaults,
            RestoreProbeRelease>(
                Guid.NewGuid(),
                1,
                systemSchema,
                new WorldSystemSourceStateDeclaration(
                    typeof(RestoreProbeSystem),
                    [new WorldSystemSourceLeaf(systemSourcePath, typeof(int), WorldSystemSourcePurpose.Authoritative)]),
                [systemField],
                default,
                default,
                systemCodec,
                default);
        _ = registrationBuilder.RegisterSystem(
            new SystemDescription(
                Guid.NewGuid(),
                0,
                SimulationStage.Input,
                [],
                [],
                [],
                SystemThreadMode.OwnerThread),
            new RestoreProbeFactory(systemFactory));
        var registration = registrationBuilder.Build();

        using var admissionDomain = new WorldAdmissionDomain(512L * 1024 * 1024);
        using var world = World.Create(new WorldOptions(registration) {
            AdmissionDomain = admissionDomain,
            CpuWorkerCount = 0,
            Enable2DFrame = true,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        world.SetTimeScale(0.5f);
        var sourceFrame2 = world.CurrentFrame2
            ?? throw new InvalidOperationException("The empty-candidate clock test omitted its admitted Frame2.");
        var sourceFrame3 = world.CurrentFrame3;
        _ = world.SetFrame(world.CreateNextFrame2(
            sourceFrame2.Origin,
            sourceFrame2.Minimum,
            sourceFrame2.Maximum));
        _ = world.SetFrame(world.CreateNextFrame3(
            sourceFrame3.Origin,
            sourceFrame3.Minimum,
            sourceFrame3.Maximum));
        _ = world.Step();
        using var lease = world.AcquirePersistenceCaptureLease();
        var worldRecord = WorldPersistenceWorldRecordWriterTests.ComposeWorldRecord(
            world,
            in lease,
            schemas,
            []);
        var limits = WorldPersistenceEbinLimits.Create(world.Options);
        var identityScratch = new WorldPersistenceIdentityScratchEntry[
            WorldPersistenceWorldRecordReader.MeasureIdentityAssignmentCount(worldRecord, limits)];
        var lineageScratch = new Guid[
            WorldPersistenceWorldRecordReader.MeasureLineageMembershipScratchCapacity(worldRecord, limits)];
        var strictLayoutBuffers = default(WorldPersistenceStrictLayoutReadBuffers);
        var savedWorld = WorldPersistenceWorldRecordReader.Read(
            worldRecord,
            limits,
            registration,
            identityScratch,
            lineageScratch,
            ref strictLayoutBuffers);
        var identityHistory = savedWorld.Ecs.ValidatedIdentityHistory
            ?? throw new InvalidOperationException("The saved World omitted its validated identity history.");
        var restoreIdentityState = WorldRestoreIdentityState.CreateBranch(
            identityHistory,
            Guid.NewGuid(),
            new byte[32],
            savedWorld.Clock.NextTick,
            isStrictLocal: false,
            savedWorld.Configuration);
        var restoreEcsPlan = WorldPersistenceEcsReader.CreateRestorePlan(
            savedWorld.EcsRecord,
            savedWorld.Ecs,
            registration,
            savedWorld.Configuration);
        Assert.That(restoreEcsPlan.EntityCount, Is.Zero);
        var targetBytes = world.MeasureRestoreWorldOwnedBytes(
            restoreIdentityState,
            savedWorld.Configuration,
            savedWorld.Clock.TickRate,
            savedWorld.Clock.InitialTimeScale,
            savedWorld.Clock.TimeScale,
            restoreEcsPlan,
            savedWorld.ControlRecordCount,
            savedWorld.SceneControlPayloadBytes);
        Assert.That(
            world.TryPrepareLoadReplacement(
                world.WorldGuid,
                sourceBytes: worldRecord.Length,
                targetBytes,
                dataBytes: checked(worldRecord.Length + restoreEcsPlan.OwnedByteReservation + 4096L),
                cleanupBytes: targetBytes,
                out var replacementPreparation,
                out var bindingRefusal,
                out var admissionRefusal),
            Is.True);
        Assert.That(bindingRefusal.IsNone, Is.True);
        Assert.That(admissionRefusal, Is.EqualTo(WorldAdmissionRefusal.None));
        using var preparation = replacementPreparation
            ?? throw new InvalidOperationException("The empty-candidate test did not retain its replacement preparation.");
        Assert.That(
            world.TryPrepareRestoreConstructionPlan(
                restoreIdentityState,
                savedWorld.Configuration,
                preparation,
                savedWorld.Clock.TickRate,
                savedWorld.Clock.InitialTimeScale,
                savedWorld.Clock.TimeScale,
                out var constructionPlan,
                out bindingRefusal,
                out admissionRefusal,
                restoreEcsPlan,
                savedWorld.ControlRecordCount,
                savedWorld.SceneControlPayloadBytes),
            Is.True);
        Assert.That(bindingRefusal.IsNone, Is.True);
        Assert.That(admissionRefusal, Is.EqualTo(WorldAdmissionRefusal.None));
        var candidatePlan = constructionPlan
            ?? throw new InvalidOperationException("The empty-candidate test did not produce its candidate plan.");

        try
        {
            using var candidate = World.CreateRestoreCandidate(candidatePlan);
            World.InitializeRestoreCandidate(candidate);
            candidate.RestoreEcsRows(savedWorld.EcsRecord, savedWorld.Ecs, CancellationToken.None);
            candidate.RestoreHierarchyLinks([], []);
            candidate.RestoreCheckpointClockAndControls(
                worldRecord,
                savedWorld.Clock,
                savedWorld.Cursors,
                savedWorld.ControlRecordCount,
                savedWorld.SceneControlPayloadBytes,
                CancellationToken.None);
            Assert.Multiple(() => {
                Assert.That(savedWorld.Ecs.EntityCount, Is.Zero);
                Assert.That(systemCodec.RestoreCount, Is.EqualTo(1));
                Assert.That(systemCodec.RestoredCounter, Is.EqualTo(124));
                Assert.That(candidate.InitialTimeScale, Is.EqualTo(1.0f));
                Assert.That(candidate.CurrentTimeScale, Is.EqualTo(0.5f));
                Assert.That(candidate.TimeScaleSegmentSequence, Is.EqualTo(2));
                Assert.That(candidate.NextTickIndex, Is.EqualTo(savedWorld.Clock.NextTick));
                Assert.That(candidate.CurrentFrame2?.Revision, Is.EqualTo(world.CurrentFrame2?.Revision));
                Assert.That(candidate.CurrentFrame3.Revision, Is.EqualTo(world.CurrentFrame3.Revision));
            });
        }
        finally
        {
            candidatePlan.ReleaseUntransferredRestoreResources();
        }
    }

    [Test]
    public void WritesRegistrationRootUnderItsExactWorldReservation()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        using var world = World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        _ = world.Step();
        using var lease = world.AcquirePersistenceCaptureLease();
        var emptyArrayPayload = new byte[16];
        Assert.That(
            EbinPayloadWriter.WriteRecordArray(
                [],
                [],
                EbinReadLimits.Foundation,
                emptyArrayPayload,
                out var emptyArrayBytes,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(emptyArrayBytes, Is.EqualTo(emptyArrayPayload.Length));
        var rootPayloadStorage = new byte[64];
        Assert.That(
            lease.MeasureRegistrationRecord(
                emptyArrayPayload,
                emptyArrayPayload,
                emptyArrayPayload,
                emptyArrayPayload,
                rootPayloadStorage,
                out var workspace,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(workspace.WorkspaceBytes, Is.GreaterThan(rootPayloadStorage.Length));
        Assert.That(lease.TryReserveAdmissionBytes(workspace.WorkspaceBytes, out var reservation), Is.True);
        using var admittedReservation = reservation ?? throw new InvalidOperationException("The registration reservation was not created.");
        var candidateStorage = new byte[workspace.RecordBytes];
        var destination = new byte[workspace.RecordBytes];
        Assert.That(
            lease.WriteRegistrationRecord(
                admittedReservation,
                workspace,
                emptyArrayPayload,
                emptyArrayPayload,
                emptyArrayPayload,
                emptyArrayPayload,
                rootPayloadStorage,
                candidateStorage,
                destination,
                out var bytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(bytesWritten, Is.EqualTo(workspace.RecordBytes));
        var assertions = new RegistrationCaptureAssertions();
        Assert.That(
            EbinStructuralReader.Read(
                destination,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref assertions,
                out var bytesConsumed,
                out var readDiagnostic),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(readDiagnostic, Is.Null);
        Assert.That(assertions.IsMatch, Is.True);
        Assert.That(bytesConsumed, Is.EqualTo(destination.Length));
    }

    private readonly struct DerivedLightFlux
    {
        internal DerivedLightFlux(float luminousFlux)
        {
            LuminousFlux = luminousFlux;
        }

        internal float LuminousFlux { get; }
    }

    private sealed class DerivedLightFluxCodec : IComponentStateCodec<DerivedLightFlux, byte>
    {
        public nint Count(in DerivedLightFlux value, ComponentCaptureBinding binding, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ComponentReadResult<byte> Read(
            scoped ReadOnlySpan<byte> source,
            ComponentReadBinding<byte> binding,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ImmutableArray<Diagnostic> Validate(
            in DerivedLightFlux value,
            ComponentValidationBinding binding,
            CancellationToken cancellationToken)
            => [];

        public nint Write(
            in DerivedLightFlux value,
            scoped Span<byte> destination,
            ComponentCaptureBinding binding,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class DerivedLightFluxDefaults : IComponentDefaultProvider<DerivedLightFlux>
    {
        public DerivedLightFlux Create(ComponentDefaultBinding binding)
            => throw new NotSupportedException();
    }

    private sealed class DerivedLightFluxFactory : IComponentFactory<DerivedLightFlux>
    {
        internal DerivedLightFluxFactory(ComponentFactoryBinding<DerivedLightFlux> binding)
        {
            ArgumentNullException.ThrowIfNull(binding);
            Binding = binding;
        }

        public ComponentFactoryBinding<DerivedLightFlux> Binding { get; }
    }

    private sealed class DerivedLightFluxRestorer : IComponentRestoreProvider<DerivedLightFlux, byte>
    {
        public DerivedLightFlux RebuildDerived(WorldStateRestoreBinding binding, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pointLight = binding.ReadSameEntity<RenderComponentDefinitions.PointLightComponent>();
            return new DerivedLightFlux(pointLight.LuminousFlux * 2.0f);
        }

        public DerivedLightFlux Restore(in byte author, WorldStateRestoreBinding binding, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private struct RegistrationCaptureAssertions : IAction<EbinRecordReadView>
    {
        internal bool IsMatch { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
        {
            IsMatch = (record.Header.SchemaId == new ContentSchemaIdentityAssignment("state/registration").SchemaId)
                && (record.Header.Major == 1)
                && (record.Header.Minor == 0)
                && (record.FieldCount == 4);

            if (!IsMatch)
            {
                return;
            }

            for (var index = 0; index < record.FieldCount; index++)
            {
                var field = record.GetField(index);

                if ((field.Tag != (uint)(index + 1))
                    || (field.Kind != 24)
                    || !field.IsRequired
                    || (field.Count != 0)
                    || (field.Payload.Length != 16)
                    || (BinaryPrimitives.ReadUInt64LittleEndian(field.Payload) != 0))
                {
                    IsMatch = false;
                    return;
                }
            }
        }
    }

    private readonly struct ResetMarker
    {
        internal ResetMarker(int value)
        {
            Value = value;
        }

        internal int Value { get; }
    }

    private sealed class ResetMarkerCodec : IComponentStateCodec<ResetMarker, byte>
    {
        public nint Count(in ResetMarker value, ComponentCaptureBinding binding, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ComponentReadResult<byte> Read(
            scoped ReadOnlySpan<byte> source,
            ComponentReadBinding<byte> binding,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ImmutableArray<Diagnostic> Validate(
            in ResetMarker value,
            ComponentValidationBinding binding,
            CancellationToken cancellationToken)
            => [];

        public nint Write(
            in ResetMarker value,
            scoped Span<byte> destination,
            ComponentCaptureBinding binding,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class ResetMarkerDefaults : IComponentDefaultProvider<ResetMarker>
    {
        public ResetMarker Create(ComponentDefaultBinding binding)
            => new(17);
    }

    private sealed class ResetMarkerFactory : IComponentFactory<ResetMarker>
    {
        internal ResetMarkerFactory(ComponentFactoryBinding<ResetMarker> binding)
        {
            ArgumentNullException.ThrowIfNull(binding);
            Binding = binding;
        }

        public ComponentFactoryBinding<ResetMarker> Binding { get; }
    }

    private sealed class ResetMarkerRestorer : IComponentRestoreProvider<ResetMarker, byte>
    {
        public ResetMarker RebuildDerived(WorldStateRestoreBinding binding, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ResetMarker Restore(in byte author, WorldStateRestoreBinding binding, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private readonly record struct RestoreAttachmentAuthor(long Counter);

    private readonly struct RestoreAttachmentAuthorReader : IComponentValueReader<RestoreAttachmentAuthor, long>
    {
        public long Get(in RestoreAttachmentAuthor value)
            => value.Counter;
    }

    private sealed class RestoreAttachmentCodec : IWorldAttachmentStateProvider<RestoreAttachmentValue, RestoreAttachmentAuthor>
    {
        private int _releaseCount;
        private int _restoreCount;
        private readonly ContentSchemaReference _schema;

        internal RestoreAttachmentCodec(ContentSchemaReference schema)
        {
            _schema = schema;
        }

        internal int ReleaseCount => Volatile.Read(ref _releaseCount);

        internal int RestoreCount => Volatile.Read(ref _restoreCount);

        public nint Count(
            RestoreAttachmentValue value,
            WorldAttachmentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return MeasureRecord(value.Counter);
        }

        public WorldAttachmentReadResult<RestoreAttachmentAuthor> Read(
            scoped ReadOnlySpan<byte> source,
            WorldAttachmentReadBinding<RestoreAttachmentAuthor> binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = new EbinRecordReadView(source);
            var header = record.Header;

            if ((header.SchemaId != _schema.SchemaId)
                || (header.Major != _schema.Major)
                || (header.Minor != _schema.Minor)
                || (record.FieldCount != 1)
                || !record.TryGetField(1, out var field)
                || (field.Kind != 8)
                || !field.IsRequired
                || (field.Count != 1)
                || (field.Payload.Length != sizeof(long)))
            {
                throw new InvalidOperationException("The restored attachment record does not match its registered schema.");
            }

            var author = new RestoreAttachmentAuthor(BinaryPrimitives.ReadInt64LittleEndian(field.Payload));
            return binding.Complete(in author, source.Length);
        }

        public void Release(RestoreAttachmentValue value, WorldAttachmentReleaseBinding binding)
            => _ = Interlocked.Increment(ref _releaseCount);

        public RestoreAttachmentValue Restore(
            in RestoreAttachmentAuthor author,
            WorldAttachmentRestoreBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = Interlocked.Increment(ref _restoreCount);
            return new RestoreAttachmentValue { Counter = author.Counter };
        }

        public ImmutableArray<Diagnostic> Validate(
            RestoreAttachmentValue value,
            WorldAttachmentValidationBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return [];
        }

        public nint Write(
            RestoreAttachmentValue value,
            scoped Span<byte> destination,
            WorldAttachmentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Span<byte> payload = stackalloc byte[sizeof(long)];

            if (EbinPayloadWriter.Write(value.Counter, payload, out var payloadBytes, out var diagnostic)
                is not EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException(diagnostic?.Category);
            }

            Span<EbinFieldSource> fields = [new EbinFieldSource(1, 8, 1, 1, new EbinPayloadSlice(0, payloadBytes))];
            var source = new EbinRecordWriteView(
                _schema.SchemaId,
                _schema.Major,
                _schema.Minor,
                fields,
                payload[..payloadBytes]);
            var recordBytes = MeasureRecord(value.Counter);
            Span<byte> candidate = stackalloc byte[recordBytes];
            var status = EbinStructuralWriter.Write(
                in source,
                EbinReadLimits.Foundation,
                candidate,
                destination[..recordBytes],
                out var bytesWritten,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException(diagnostic?.Category);
            }

            return bytesWritten;
        }

        private int MeasureRecord(long counter)
        {
            Span<byte> payload = stackalloc byte[sizeof(long)];

            if (EbinPayloadWriter.Write(counter, payload, out var payloadBytes, out var diagnostic)
                is not EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException(diagnostic?.Category);
            }

            Span<EbinFieldSource> fields = [new EbinFieldSource(1, 8, 1, 1, new EbinPayloadSlice(0, payloadBytes))];
            var source = new EbinRecordWriteView(
                _schema.SchemaId,
                _schema.Major,
                _schema.Minor,
                fields,
                payload[..payloadBytes]);
            var status = EbinStructuralWriter.Measure(
                in source,
                EbinReadLimits.Foundation,
                out var recordBytes,
                out diagnostic);
            return (status is EbinWriteStatus.Complete)
                ? recordBytes
                : throw new InvalidOperationException(diagnostic?.Category);
        }
    }

    private sealed class RestoreAttachmentValue
    {
        internal long Counter { get; init; }
    }

    private readonly struct RestoreAttachmentValueReader : IComponentValueReader<RestoreAttachmentValue, long>
    {
        public long Get(in RestoreAttachmentValue value)
            => value.Counter;
    }

    private readonly record struct RestoreProbeAuthor(int Counter);

    private readonly struct RestoreProbeAuthorCounterReader : IWorldSystemValueReader<RestoreProbeAuthor, int>
    {
        public int Invoke(in RestoreProbeAuthor source)
            => source.Counter;
    }

    private sealed class RestoreProbeCodec : IWorldSystemStateCodec<RestoreProbeSystem, RestoreProbeAuthor>
    {
        private int _restoreCount;
        private int _restoredCounter;
        private readonly ContentSchemaReference _schema;

        internal RestoreProbeCodec(ContentSchemaReference schema)
        {
            _schema = schema;
        }

        internal int RestoreCount => Volatile.Read(ref _restoreCount);

        internal int RestoredCounter => Volatile.Read(ref _restoredCounter);

        private static void WritePayload(int counter, scoped Span<byte> payload, scoped Span<EbinFieldSource> fields)
        {
            if ((EbinPayloadWriter.Write(counter, payload, out var bytesWritten, out _) != EbinWriteStatus.Complete)
                || (bytesWritten != sizeof(int)))
            {
                throw new InvalidOperationException("The restore-probe system payload could not be written.");
            }

            fields[0] = new EbinFieldSource(1, 7, 1, 1, new EbinPayloadSlice(0, bytesWritten));
        }

        public nint Count(RestoreProbeSystem system, WorldSystemCaptureBinding binding, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return MeasureRecord(system.Counter);
        }

        public WorldSystemReadResult<RestoreProbeAuthor> Read(
            scoped ReadOnlySpan<byte> source,
            WorldSystemReadBinding<RestoreProbeAuthor> binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var action = new ReadAction(_schema, binding);
            var status = EbinStructuralReader.Read(
                source,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref action,
                out _,
                out var diagnostic);

            if (status is not EbinReadStatus.Complete)
            {
                return binding.Reject(status, diagnostic);
            }

            return action.Result
                ?? throw new InvalidOperationException("The system codec read did not issue an author result.");
        }

        public void Restore(
            RestoreProbeSystem system,
            in RestoreProbeAuthor author,
            WorldStateRestoreBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            system.Counter = author.Counter;
            Volatile.Write(ref _restoredCounter, author.Counter);
            _ = Interlocked.Increment(ref _restoreCount);
        }

        public ImmutableArray<Diagnostic> Validate(
            RestoreProbeSystem system,
            WorldSystemValidationBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return [];
        }

        public nint Write(
            RestoreProbeSystem system,
            scoped Span<byte> destination,
            WorldSystemCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Span<byte> payload = stackalloc byte[sizeof(int)];
            Span<byte> candidate = stackalloc byte[128];
            Span<EbinFieldSource> fields = stackalloc EbinFieldSource[1];
            WritePayload(system.Counter, payload, fields);
            var source = new EbinRecordWriteView(
                _schema.SchemaId,
                _schema.Major,
                _schema.Minor,
                fields,
                payload);
            var status = EbinStructuralWriter.Write(
                source,
                EbinReadLimits.Foundation,
                candidate,
                destination,
                out var bytesWritten,
                out var diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException(diagnostic?.Category);
            }

            return bytesWritten;
        }

        private nint MeasureRecord(int counter)
        {
            Span<byte> payload = stackalloc byte[sizeof(int)];
            Span<EbinFieldSource> fields = stackalloc EbinFieldSource[1];
            WritePayload(counter, payload, fields);
            var source = new EbinRecordWriteView(
                _schema.SchemaId,
                _schema.Major,
                _schema.Minor,
                fields,
                payload);
            var status = EbinStructuralWriter.Measure(
                source,
                EbinReadLimits.Foundation,
                out var recordBytes,
                out var diagnostic);
            return (status is EbinWriteStatus.Complete)
                ? recordBytes
                : throw new InvalidOperationException(diagnostic?.Category);
        }

        private struct ReadAction : IAction<EbinRecordReadView>
        {
            private readonly WorldSystemReadBinding<RestoreProbeAuthor> _binding;
            private WorldSystemReadResult<RestoreProbeAuthor>? _result;
            private readonly ContentSchemaReference _schema;

            internal ReadAction(
                ContentSchemaReference schema,
                WorldSystemReadBinding<RestoreProbeAuthor> binding)
            {
                _schema = schema;
                _binding = binding;
                _result = null;
            }

            internal readonly WorldSystemReadResult<RestoreProbeAuthor>? Result => _result;

            public void Invoke(scoped EbinRecordReadView record)
            {
                var header = record.Header;

                if ((header.SchemaId != _schema.SchemaId)
                    || (header.Major != _schema.Major)
                    || (header.Minor != _schema.Minor))
                {
                    _result = _binding.Reject(
                        WorldSystemReadFailureKind.SchemaMismatch,
                        16,
                        null,
                        "The restore-probe system record does not match the selected schema.");
                    return;
                }

                if ((record.FieldCount != 1)
                    || !record.TryGetField(1, out var field)
                    || (field.Kind != 7)
                    || !field.IsRequired
                    || (field.Count != 1)
                    || (field.Payload.Length != sizeof(int)))
                {
                    _result = _binding.Reject(
                        WorldSystemReadFailureKind.InvalidField,
                        64,
                        null,
                        "The restore-probe system record does not match its one-field schema.");
                    return;
                }

                var author = new RestoreProbeAuthor(BinaryPrimitives.ReadInt32LittleEndian(field.Payload));
                _result = _binding.Complete(in author, record.EncodedBytes.Length);
            }
        }
    }

    private readonly struct RestoreProbeCreate : IWorldSystemCreate<RestoreProbeSystem>
    {
        public RestoreProbeSystem Invoke()
            => new();
    }

    private readonly struct RestoreProbeDefaults : IWorldSystemDefaults<RestoreProbeSystem>
    {
        public void Invoke(RestoreProbeSystem system)
            => system.Counter = 123;
    }

    private sealed class RestoreProbeFactory : IWorldSystemFactory
    {
        internal RestoreProbeFactory(WorldSystemFactoryBinding binding)
        {
            ArgumentNullException.ThrowIfNull(binding);
            Binding = binding;
        }

        public WorldSystemFactoryBinding Binding { get; }
    }

    private readonly struct RestoreProbeRelease : IWorldSystemRelease<RestoreProbeSystem>
    {
        public void Invoke(RestoreProbeSystem system)
        {
        }
    }

    private sealed class RestoreProbeSystem : IWorldSystem
    {
        internal int Counter { get; set; }

        public void Execute(WorldSystemContext context)
            => Counter++;
    }

    private readonly struct RestoreProbeSystemCounterReader : IWorldSystemValueReader<RestoreProbeSystem, int>
    {
        public int Invoke(in RestoreProbeSystem source)
            => source.Counter;
    }
}
