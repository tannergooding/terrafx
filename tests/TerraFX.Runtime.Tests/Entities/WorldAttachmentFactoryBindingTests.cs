// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;
using TerraFX.Runtime.Tests.Scenes;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldAttachmentFactoryBindingTests
{
    [Test]
    public void FactoryBindingCapturesCompleteAttachmentStateFamily()
    {
        var assignment = new ContentSchemaIdentityAssignment("application/attachment-state/counter-test/v1");
        var schema = new ContentSchemaReference(assignment.SchemaId, 1, 0, ContentSchemaScope.Content);
        var valueField = new ContentFieldDescriptor(
            1,
            8,
            0,
            true,
            1,
            1,
            [],
            0,
            ContentUnit.None,
            ContentReferenceKind.None,
            ContentAuthoring.None,
            null);
        var targetField = new ContentFieldDescriptor(
            2,
            11,
            0,
            false,
            1,
            1,
            [],
            0,
            ContentUnit.None,
            ContentReferenceKind.Entity,
            ContentAuthoring.None,
            null);
        var metadata = new DataSchema(
            schema,
            assignment,
            ContentSchemaRole.AttachmentState,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            [valueField, targetField]);
        var registry = new CompiledContentSchemaRegistry([metadata]);
        var valueBinding = WorldAttachmentFieldBinding<ProbeAttachment, ProbeAuthor>.Value<
            long,
            AttachmentCounterReader,
            AuthorCounterReader>(
                registry,
                schema,
                new ComponentFieldPath([ComponentPropertySegment.FieldTag(1)]),
                default,
                default);
        var targetBinding = WorldAttachmentFieldBinding<ProbeAttachment, ProbeAuthor>.OptionalEntityReference<
            AttachmentTargetReader,
            AuthorTargetReader>(
                registry,
                schema,
                new ComponentFieldPath([ComponentPropertySegment.FieldTag(2)]),
                default,
                default);
        var provider = new AttachmentStateProvider();
        var binding = WorldAttachmentFactoryBinding<ProbeAttachment, ProbeAuthor>.Create(
            Guid.NewGuid(),
            1,
            registry,
            schema,
            [valueBinding, targetBinding],
            provider);
        provider.TargetField = binding.Descriptor.Fields[1];

        Assert.That(binding.Descriptor.Schema, Is.EqualTo(schema));
        Assert.That(binding.Descriptor.FactoryVersion, Is.EqualTo(1));
        Assert.That(binding.Descriptor.Fields, Has.Length.EqualTo(2));
        Assert.That(binding.Descriptor.Fields[0].Field.Tag, Is.EqualTo(1));
        Assert.That(binding.Descriptor.Fields[1].Field.Tag, Is.EqualTo(2));
        Assert.That(binding.Provider, Is.SameAs(provider));
        Assert.That(
            () => WorldAttachmentFactoryBinding<ProbeAttachment, ProbeAuthor>.Create(
                Guid.NewGuid(),
                1,
                registry,
                schema,
                [valueBinding],
                provider),
            Throws.ArgumentException);

        var builder = new WorldRegistrationBuilder(registry);
        _ = builder.RegisterAttachment(AttachmentScope.World, binding);
        _ = builder.RegisterAttachment(AttachmentScope.Entity, binding);
        var destroySystemDescription = new SystemDescription(
            Guid.NewGuid(),
            0,
            SimulationStage.Structural,
            [],
            [],
            [],
            SystemThreadMode.OwnerThread);
        var destroyProbe = new DeferredAttachmentDestroyProbe();
        var destroyFactoryBinding = WorldSystemFactoryBinding.Stateless<
            DeferredAttachmentDestroySystem,
            DeferredAttachmentDestroySystemCreate,
            DeferredAttachmentDestroySystemRelease>(
                Guid.NewGuid(),
                1,
                new WorldSystemSourceStateDeclaration(typeof(DeferredAttachmentDestroySystem), []),
                new DeferredAttachmentDestroySystemCreate(destroyProbe),
                default);
        var destroyFactory = new DeferredAttachmentDestroySystemFactory(destroyFactoryBinding);
        _ = builder.RegisterSystem(destroySystemDescription, destroyFactory);
        var registration = builder.Build();
        Assert.That(registration.GetAttachmentBinding(AttachmentScope.World, schema), Is.SameAs(binding.Descriptor));
        Assert.That(registration.GetAttachmentBinding(AttachmentScope.Entity, schema), Is.SameAs(binding.Descriptor));
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        using var world = World.Create(new WorldOptions(registration) {
            AdmissionDomain = domain,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var target = world.CreateEntity(new EntityDescription());
        var captureOwnerId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var lowerCaptureOwnerId = Guid.Parse("00000001-0000-0000-0000-000000000001");
        var captureOwner = world.CreateEntity(new EntityDescription(persistentId: captureOwnerId));
        var lowerCaptureOwner = world.CreateEntity(new EntityDescription(persistentId: lowerCaptureOwnerId));
        Assert.That(
            () => world.SetAttachment(new ProbeAttachment { Target = target }, AttachmentOwnership.CallerOwned),
            Throws.ArgumentException);
        Assert.That(
            () => world.SetAttachment(new ProbeAttachment { Target = default(Entity) }, AttachmentOwnership.Transferred),
            Throws.ArgumentException);
        Assert.That(world.TryGetAttachment<ProbeAttachment>(out _), Is.False);

        provider.ReentrantWorld = world;
        var caughtReentrancy = Assert.Throws<InvalidOperationException>(
            () => world.SetAttachment(
                new ProbeAttachment { Counter = 11, Target = target },
                AttachmentOwnership.Transferred));
        Assert.That(provider.CaughtReentrancyFailure, Is.SameAs(caughtReentrancy));
        provider.ReentrantWorld = null;
        provider.CaughtReentrancyFailure = null;
        Assert.That(world.TryGetAttachment<ProbeAttachment>(out _), Is.False);

        var entityScoped = new ProbeAttachment { Counter = 11, Target = target };
        world.SetAttachment(captureOwner, entityScoped, AttachmentOwnership.Transferred);
        var first = new ProbeAttachment { Counter = 12, Target = target };
        world.SetAttachment(first, AttachmentOwnership.Transferred);
        world.SetAttachment(
            lowerCaptureOwner,
            new ProbeAttachment { Counter = 10, Target = target },
            AttachmentOwnership.Transferred);
        Assert.That(world.TryGetAttachment<ProbeAttachment>(out var stored), Is.True);
        Assert.That(stored, Is.SameAs(first));
        Assert.That(provider.ReleaseCount, Is.Zero);

        var targetId = world.GetEntityId(target);
        _ = world.Step();

        using (var lease = world.AcquirePersistenceCaptureLease())
        {
            Assert.That(lease.GetAttachmentStateCount(), Is.EqualTo(3));
            var captureRows = new World.AttachmentStateCaptureRow[3];
            var enumerator = lease.GetCanonicalAttachmentStateEnumerator(captureRows);
            var capturedRecordCount = 0;
            var capturedWorldScope = false;
            var capturedEntityScope = false;
            Span<byte> targetIdBytes = stackalloc byte[16];
            Span<byte> unknownTargetIdBytes = stackalloc byte[16];

            while (enumerator.MoveNext())
            {
                var scope = enumerator.CurrentScope;
                var ownerEntityId = enumerator.CurrentOwnerEntityId;
                var expectedCounter = capturedRecordCount switch {
                    0 => 12L,
                    1 => 10L,
                    2 => 11L,
                    _ => throw new InvalidOperationException("The canonical attachment cursor returned an extra record."),
                };
                var expectedOwnerEntityId = capturedRecordCount switch {
                    0 => (Guid?)null,
                    1 => lowerCaptureOwnerId,
                    2 => captureOwnerId,
                    _ => throw new InvalidOperationException("The canonical attachment cursor returned an extra record."),
                };
                var expectedTableIndex = capturedRecordCount switch {
                    0 => 1,
                    1 => 2,
                    2 => 0,
                    _ => throw new InvalidOperationException("The canonical attachment cursor returned an extra record."),
                };
                var expectedScope = expectedOwnerEntityId.HasValue ? AttachmentScope.Entity : AttachmentScope.World;

                Assert.That(enumerator.CurrentIndex, Is.EqualTo(expectedTableIndex));
                Assert.That(ownerEntityId, Is.EqualTo(expectedOwnerEntityId));
                Assert.That(scope, Is.EqualTo(expectedScope));
                capturedWorldScope |= scope == AttachmentScope.World;
                capturedEntityScope |= scope == AttachmentScope.Entity;
                Assert.That(enumerator.CurrentSchema, Is.EqualTo(schema));
                var byteCount = lease.CountAttachmentStateRecord(enumerator.CurrentIndex, CancellationToken.None);
                Assert.That(byteCount, Is.EqualTo((nint)(sizeof(long) + 1 + 16)));
                var encoded = new byte[checked((int)byteCount)];
                var written = lease.WriteAttachmentStateRecord(
                    enumerator.CurrentIndex,
                    encoded,
                    CancellationToken.None);
                var expected = new byte[sizeof(long) + 1 + 16];
                BinaryPrimitives.WriteInt64LittleEndian(expected, expectedCounter);
                expected[sizeof(long)] = 1;
                _ = targetId.TryWriteBytes(expected.AsSpan(sizeof(long) + 1), bigEndian: true, out var guidBytesWritten);

                Assert.That(guidBytesWritten, Is.EqualTo(16));
                Assert.That(written, Is.EqualTo(byteCount));
                Assert.That(encoded, Is.EqualTo(expected));

                if (scope == AttachmentScope.Entity)
                {
                    var attachmentOwnerId = ownerEntityId
                        ?? throw new InvalidOperationException("The selected Entity attachment has no owner.");
                    var valuePayload = new byte[sizeof(long)];
                    BinaryPrimitives.WriteInt64LittleEndian(valuePayload, expectedCounter);
                    var payloadRecord = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                        schema.SchemaId,
                        schema.Major,
                        schema.Minor,
                        new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(1, 8, 1, 1, valuePayload),
                        SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(2, targetId, isRequired: false));
                    var attachmentLimits = EbinReadLimits.Foundation;
                    Assert.That(
                        WorldPersistenceStateRowWriter.MeasureAttachment(
                            schema,
                            hasOwner: true,
                            payloadRecordBytes: payloadRecord.Length,
                            limits: attachmentLimits,
                            schemaReferenceRecordBytes: out var schemaReferenceBytes,
                            rowRecordBytes: out var attachmentRecordBytes,
                            rowPayloadBytes: out var attachmentPayloadBytes,
                            diagnostic: out var attachmentDiagnostic),
                        Is.EqualTo(EbinWriteStatus.Complete),
                        attachmentDiagnostic?.Category);
                    Assert.That(attachmentDiagnostic, Is.Null);
                    Assert.That(schemaReferenceBytes, Is.GreaterThan(0));
                    var attachmentPayload = new byte[attachmentPayloadBytes];
                    var attachmentCandidate = new byte[attachmentRecordBytes];
                    var attachmentRecord = new byte[attachmentRecordBytes];
                    Assert.That(
                        WorldPersistenceStateRowWriter.WriteAttachment(
                            schema,
                            attachmentOwnerId,
                            payloadRecord,
                            attachmentLimits,
                            attachmentPayload,
                            attachmentCandidate,
                            attachmentRecord,
                            out var attachmentBytesWritten,
                            out attachmentDiagnostic),
                        Is.EqualTo(EbinWriteStatus.Complete),
                        attachmentDiagnostic?.Category);
                    Assert.That(attachmentDiagnostic, Is.Null);
                    Assert.That(attachmentBytesWritten, Is.EqualTo(attachmentRecordBytes));

                    var liveIdentities = new WorldPersistenceLiveIdentitySet(maximumCount: 2);
                    liveIdentities.Add(targetId, identityKind: 1, assignmentOrdinal: 1, nameof(targetId));
                    liveIdentities.MatchEntity(targetId, nameof(targetId));
                    liveIdentities.Add(attachmentOwnerId, identityKind: 1, assignmentOrdinal: 2, nameof(ownerEntityId));
                    liveIdentities.MatchEntity(attachmentOwnerId, nameof(ownerEntityId));
                    WorldPersistenceStateRowWriterTests.AssertAttachmentOwnerRow(
                        attachmentRecord,
                        attachmentLimits,
                        liveIdentities,
                        registration);

                    var invalidPayload = (byte[])payloadRecord.Clone();
                    _ = targetId.TryWriteBytes(targetIdBytes, bigEndian: true, out _);
                    _ = Guid.Parse("50000000-0000-0000-0000-000000000001").TryWriteBytes(
                        unknownTargetIdBytes,
                        bigEndian: true,
                        out _);
                    var targetOffset = invalidPayload.AsSpan().IndexOf(targetIdBytes);
                    Assert.That(targetOffset, Is.GreaterThanOrEqualTo(0));
                    Assert.That(
                        invalidPayload.AsSpan(targetOffset + targetIdBytes.Length).IndexOf(targetIdBytes),
                        Is.EqualTo(-1));
                    unknownTargetIdBytes.CopyTo(invalidPayload.AsSpan(targetOffset, unknownTargetIdBytes.Length));
                    Assert.That(
                        WorldPersistenceStateRowWriter.WriteAttachment(
                            schema,
                            attachmentOwnerId,
                            invalidPayload,
                            attachmentLimits,
                            attachmentPayload,
                            attachmentCandidate,
                            attachmentRecord,
                            out attachmentBytesWritten,
                            out attachmentDiagnostic),
                        Is.EqualTo(EbinWriteStatus.Complete),
                        attachmentDiagnostic?.Category);
                    Assert.That(attachmentDiagnostic, Is.Null);
                    _ = Assert.Throws<ArgumentException>(() => WorldPersistenceStateRowWriterTests.AssertAttachmentOwnerRow(
                        attachmentRecord,
                        attachmentLimits,
                        liveIdentities,
                        registration));
                }

                capturedRecordCount++;
            }

            Assert.That(capturedRecordCount, Is.EqualTo(3));
            Assert.That(capturedWorldScope, Is.True);
            Assert.That(capturedEntityScope, Is.True);
        }

        Assert.That(world.RemoveAttachment<ProbeAttachment>(captureOwner), Is.True);
        Assert.That(provider.ReleaseCount, Is.EqualTo(1));
        Assert.That(world.RemoveAttachment<ProbeAttachment>(lowerCaptureOwner), Is.True);
        Assert.That(provider.ReleaseCount, Is.EqualTo(2));

        var second = new ProbeAttachment { Counter = 13, Target = target };
        world.SetAttachment(second, AttachmentOwnership.Transferred);
        Assert.That(provider.ReleaseCount, Is.EqualTo(3));
        Assert.That(provider.LastReleasedValue, Is.SameAs(first));
        Assert.That(provider.LastReleaseBinding!.WorldGuid, Is.EqualTo(world.WorldGuid));
        Assert.That(provider.LastReleaseBinding.ReplacementInstallationId, Is.GreaterThan(0));
        Assert.That(
            provider.LastReleaseBinding.ReplacementInstallationId,
            Is.GreaterThan(provider.LastReleaseBinding.ReleasedInstallationId));
        Assert.That(world.TryGetAttachment<ProbeAttachment>(out stored), Is.True);
        Assert.That(stored, Is.SameAs(second));

        var third = new ProbeAttachment { Counter = 14, Target = target };
        var replacementFailure = new InvalidOperationException("replacement release failure");
        provider.ReleaseFailure = replacementFailure;
        var replacementException = Assert.Throws<WorldAttachmentReleaseException>(
            () => world.SetAttachment(third, AttachmentOwnership.Transferred))
            ?? throw new InvalidOperationException("The committed replacement did not report its Release failure.");
        Assert.That(replacementException.InnerException, Is.SameAs(replacementFailure));
        Assert.That(replacementException.StorageCommitted, Is.True);
        Assert.That(replacementException.PrimaryFailure.ReplacementInstallationId, Is.GreaterThan(0));
        Assert.That(world.TryGetAttachment<ProbeAttachment>(out stored), Is.True);
        Assert.That(stored, Is.SameAs(third));
        provider.ReleaseFailure = null;

        Assert.That(world.RemoveAttachment<ProbeAttachment>(), Is.True);
        Assert.That(world.RemoveAttachment<ProbeAttachment>(), Is.False);
        Assert.That(provider.ReleaseCount, Is.EqualTo(5));
        Assert.That(provider.LastReleasedValue, Is.SameAs(third));
        Assert.That(provider.LastReleaseBinding!.ReplacementInstallationId, Is.Null);

        var entityOwner = world.CreateEntity(new EntityDescription());
        var entityOwnerId = world.GetEntityId(entityOwner);
        var entityOwnerIdentity = world.GetEntityIdentity(entityOwner);
        var entityValue = new ProbeAttachment { Counter = 14 };
        world.SetAttachment(entityOwner, entityValue, AttachmentOwnership.Transferred);
        destroyProbe.Entity = entityOwner;
        destroyProbe.Enabled = true;
        var destroyPublication = world.Step().Publication;

        Assert.That(
            destroyPublication.CommandReceipts[0].DestroyedEntityIdentities,
            Is.EqualTo(ImmutableArray.Create(entityOwnerIdentity)));
        Assert.That(world.TryGetEntity(entityOwnerId, out _), Is.False);
        Assert.That(provider.LastReleasedValue, Is.SameAs(entityValue));
        Assert.That(provider.LastReleaseBinding!.Scope, Is.EqualTo(AttachmentScope.Entity));
        Assert.That(provider.LastReleaseBinding.OwnerEntityId, Is.EqualTo(entityOwnerId));
        Assert.That(provider.ReleaseCount, Is.EqualTo(6));

        var referencedTarget = world.CreateEntity(new EntityDescription());
        var referencedTargetId = world.GetEntityId(referencedTarget);
        world.SetAttachment(
            new ProbeAttachment {
                Counter = 15,
                Target = referencedTarget,
            },
            AttachmentOwnership.Transferred);
        Assert.That(
            () => world.DestroyEntity(referencedTarget, HierarchyDestroyMode.DetachChildrenKeepWorld),
            Throws.InvalidOperationException);
        Assert.That(world.TryGetEntity(referencedTargetId, out var stillLiveTarget) && stillLiveTarget == referencedTarget);
        Assert.That(world.RemoveAttachment<ProbeAttachment>(), Is.True);
        world.DestroyEntity(referencedTarget, HierarchyDestroyMode.DetachChildrenKeepWorld);

        var quarantined = new ProbeAttachment { Counter = 16, Target = target };
        world.SetAttachment(quarantined, AttachmentOwnership.Transferred);
        var releaseFailure = new InvalidOperationException("attachment release failure");
        provider.ReleaseFailure = releaseFailure;
        var observedFailure = Assert.Throws<WorldAttachmentReleaseException>(() => world.RemoveAttachment<ProbeAttachment>())
            ?? throw new InvalidOperationException("The transferred attachment release did not report its failure.");
        Assert.That(observedFailure.InnerException, Is.SameAs(releaseFailure));
        Assert.That(observedFailure.PrimaryFailure.OriginalException, Is.SameAs(releaseFailure));
        Assert.That(observedFailure.StorageCommitted, Is.True);
        Assert.That(observedFailure.FailureCount, Is.EqualTo(1));
        Assert.That(observedFailure.AdditionalFailures, Is.Empty);
        Assert.That(world.TryGetAttachment<ProbeAttachment>(out _), Is.False);

        var disposed = new ProbeAttachment { Counter = 17, Target = target };
        world.SetAttachment(disposed, AttachmentOwnership.Transferred);

        for (var index = 0; index < 65; index++)
        {
            var attachmentOwner = world.CreateEntity(new EntityDescription());
            world.SetAttachment(attachmentOwner, new ProbeAttachment { Counter = 18 + index }, AttachmentOwnership.Transferred);
        }

        var disposalFailure = new InvalidOperationException("attachment disposal failure");
        provider.ReleaseFailure = disposalFailure;
        var disposalException = Assert.Throws<WorldAttachmentReleaseException>(world.Dispose)
            ?? throw new InvalidOperationException("World disposal did not report its attachment release failures.");
        Assert.That(disposalException.InnerException, Is.SameAs(disposalFailure));
        Assert.That(disposalException.StorageCommitted, Is.True);
        Assert.That(disposalException.FailureCount, Is.EqualTo(66));
        Assert.That(disposalException.AdditionalFailures, Has.Length.EqualTo(63));
        Assert.That(disposalException.OmittedFailureCount, Is.EqualTo(2));
        Assert.That(disposalException.PrimaryFailure.Scope, Is.EqualTo(AttachmentScope.World));
        Assert.That(disposalException.PrimaryFailure.OwnerEntityId, Is.Null);
        Assert.That(provider.LastReleaseBinding!.ReplacementInstallationId, Is.Null);
        Assert.That(provider.ReleaseCount, Is.EqualTo(74));
    }

    [Test]
    public void ReleaseReportFinalizationFailureUsesPreparedCarrier()
    {
        var worldGuid = Guid.NewGuid();
        var schema = new ContentSchemaReference(Guid.NewGuid(), 1, 0, ContentSchemaScope.Content);
        var releaseBinding = new WorldAttachmentReleaseBinding(
            worldGuid,
            AttachmentScope.World,
            schema,
            null,
            7,
            null);
        var originalReleaseException = new InvalidOperationException("release");
        var finalizationException = new InvalidOperationException("report");
        var preparation = new WorldAttachmentReleasePreparation(worldGuid, 1, 23);
        preparation.RecordFailure(releaseBinding, originalReleaseException);

        var result = preparation.CreateException(
            null,
            (_, _, _, _) => throw finalizationException);
        var fallback = result as WorldAttachmentReleaseReportFinalizationException
            ?? throw new InvalidOperationException("The prepared report-finalization carrier was not returned.");

        Assert.That(fallback.WorldGuid, Is.EqualTo(worldGuid));
        Assert.That(fallback.Scope, Is.EqualTo(AttachmentScope.World));
        Assert.That(fallback.OwnerEntityId, Is.Null);
        Assert.That(fallback.Schema, Is.EqualTo(schema));
        Assert.That(fallback.ReleasedInstallationId, Is.EqualTo(7));
        Assert.That(fallback.ReplacementInstallationId, Is.Null);
        Assert.That(fallback.CommittedMutationRevision, Is.EqualTo(23));
        Assert.That(fallback.ReleaseFailureCount, Is.EqualTo(1));
        Assert.That(fallback.StorageCommitted, Is.True);
        Assert.That(fallback.OriginalReleaseException, Is.SameAs(originalReleaseException));
        Assert.That(fallback.ReportFinalizationException, Is.SameAs(finalizationException));
        Assert.That(fallback.InnerException, Is.Null);
    }

    private readonly struct AttachmentCounterReader : IComponentValueReader<ProbeAttachment, long>
    {
        public long Get(in ProbeAttachment value) => value.Counter;
    }

    private sealed class AttachmentStateProvider : IWorldAttachmentStateProvider<ProbeAttachment, ProbeAuthor>
    {
        internal Exception? CaughtReentrancyFailure { get; set; }

        internal WorldAttachmentReleaseBinding? LastReleaseBinding { get; private set; }

        internal ProbeAttachment? LastReleasedValue { get; private set; }

        internal World? ReentrantWorld { get; set; }

        internal int ReleaseCount { get; private set; }

        internal Exception? ReleaseFailure { get; set; }

        internal WorldAttachmentFieldDescriptor? TargetField { get; set; }

        public nint Count(
            ProbeAttachment value,
            WorldAttachmentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var byteCount = sizeof(long) + 1;

            if (value.Target is { } target)
            {
                _ = binding.GetEntityId(
                    TargetField ?? throw new InvalidOperationException("The attachment target field was not captured."),
                    target);
                byteCount += 16;
            }

            return byteCount;
        }

        public WorldAttachmentReadResult<ProbeAuthor> Read(
            scoped ReadOnlySpan<byte> source,
            WorldAttachmentReadBinding<ProbeAuthor> binding,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public void Release(ProbeAttachment value, WorldAttachmentReleaseBinding binding)
        {
            ReleaseCount++;
            LastReleasedValue = value;
            LastReleaseBinding = binding;

            if (ReleaseFailure is { } exception)
            {
                throw exception;
            }
        }

        public ProbeAttachment Restore(
            in ProbeAuthor author,
            WorldAttachmentRestoreBinding binding,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ImmutableArray<Diagnostic> Validate(
            ProbeAttachment value,
            WorldAttachmentValidationBinding binding,
            CancellationToken cancellationToken)
        {
            if (ReentrantWorld is { } world)
            {
                try
                {
                    _ = world.TryGetAttachment<ProbeAttachment>(out _);
                }
                catch (InvalidOperationException exception)
                {
                    CaughtReentrancyFailure = exception;
                }
            }

            return [];
        }

        public nint Write(
            ProbeAttachment value,
            scoped Span<byte> destination,
            WorldAttachmentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Guid? targetId = (value.Target is { } target)
                ? binding.GetEntityId(
                    TargetField ?? throw new InvalidOperationException("The attachment target field was not captured."),
                    target)
                : null;
            var byteCount = sizeof(long) + 1 + (targetId is null ? 0 : 16);
            ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, byteCount);
            BinaryPrimitives.WriteInt64LittleEndian(destination, value.Counter);
            destination[sizeof(long)] = targetId is null ? (byte)0 : (byte)1;

            if (targetId is { } entityId)
            {
                _ = entityId.TryWriteBytes(destination.Slice(sizeof(long) + 1, 16), bigEndian: true, out _);
            }

            return byteCount;
        }
    }

    private readonly struct AttachmentTargetReader : IComponentOptionalReader<ProbeAttachment, Entity>
    {
        public Entity GetPresent(in ProbeAttachment value)
            => value.Target ?? throw new InvalidOperationException();

        public bool IsPresent(in ProbeAttachment value) => value.Target.HasValue;
    }

    private readonly struct AuthorCounterReader : IComponentValueReader<ProbeAuthor, long>
    {
        public long Get(in ProbeAuthor value) => value.Counter;
    }

    private readonly struct AuthorTargetReader : IComponentOptionalReader<ProbeAuthor, Guid>
    {
        public Guid GetPresent(in ProbeAuthor value)
            => value.Target ?? throw new InvalidOperationException();

        public bool IsPresent(in ProbeAuthor value) => value.Target.HasValue;
    }

    private sealed class DeferredAttachmentDestroyProbe
    {
        internal bool Enabled { get; set; }

        internal Entity Entity { get; set; }
    }

    private sealed class DeferredAttachmentDestroySystem : IWorldSystem
    {
        private readonly DeferredAttachmentDestroyProbe _probe;

        internal DeferredAttachmentDestroySystem(DeferredAttachmentDestroyProbe probe)
        {
            ArgumentNullException.ThrowIfNull(probe);
            _probe = probe;
        }

        public void Execute(WorldSystemContext context)
        {
            if (_probe.Enabled)
            {
                context.Commands.Destroy(_probe.Entity, HierarchyDestroyMode.DetachChildrenKeepWorld);
            }
        }
    }

    private readonly struct DeferredAttachmentDestroySystemCreate : IWorldSystemCreate<DeferredAttachmentDestroySystem>
    {
        private readonly DeferredAttachmentDestroyProbe _probe;

        internal DeferredAttachmentDestroySystemCreate(DeferredAttachmentDestroyProbe probe)
        {
            _probe = probe;
        }

        public DeferredAttachmentDestroySystem Invoke() => new(_probe);
    }

    private sealed class DeferredAttachmentDestroySystemFactory : IWorldSystemFactory
    {
        internal DeferredAttachmentDestroySystemFactory(WorldSystemFactoryBinding binding)
        {
            ArgumentNullException.ThrowIfNull(binding);
            Binding = binding;
        }

        public WorldSystemFactoryBinding Binding { get; }
    }

    private readonly struct DeferredAttachmentDestroySystemRelease : IWorldSystemRelease<DeferredAttachmentDestroySystem>
    {
        public void Invoke(DeferredAttachmentDestroySystem system)
            => ArgumentNullException.ThrowIfNull(system);
    }

    private sealed class ProbeAttachment
    {
        internal long Counter { get; set; }

        internal Entity? Target { get; set; }
    }

    private readonly record struct ProbeAuthor(long Counter, Guid? Target);
}
