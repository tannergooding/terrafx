// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceEcsProviderTests
{
    private static void AssertEmptyArray(EbinFieldReadView field, uint tag)
    {
        Assert.That(field.Tag, Is.EqualTo(tag));
        Assert.That(field.IsRequired, Is.True);
        Assert.That(field.Kind, Is.EqualTo(24));
        Assert.That(field.Count, Is.Zero);
        Assert.That(field.Payload.Length, Is.EqualTo(16));
        Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(field.Payload), Is.Zero);
        Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(field.Payload[8..]), Is.EqualTo(16));
    }

    private static World CreateAttachmentWorld(out Guid attachmentSchemaId)
    {
        var assignment = new ContentSchemaIdentityAssignment("application/attachment-state/ecs-test/v1");
        var schema = new ContentSchemaReference(
            assignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var field = new ContentFieldDescriptor(
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
        var metadata = new DataSchema(
            schema,
            assignment,
            ContentSchemaRole.AttachmentState,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            [field]);
        var schemas = new CompiledContentSchemaRegistry([metadata]);
        var provider = new EcsAttachmentStateProvider(schema);
        var fieldBinding = WorldAttachmentFieldBinding<EcsAttachmentValue, EcsAttachmentAuthor>.Value<
            long,
            EcsAttachmentValueReader,
            EcsAttachmentAuthorReader>(
                schemas,
                schema,
                new ComponentFieldPath([ComponentPropertySegment.FieldTag(1)]),
                default,
                default);
        var binding = WorldAttachmentFactoryBinding<EcsAttachmentValue, EcsAttachmentAuthor>.Create(
            Guid.Parse("40000000-0000-0000-0000-000000000003"),
            1,
            schemas,
            schema,
            [fieldBinding],
            provider);
        var builder = new WorldRegistrationBuilder(schemas);
        _ = builder.RegisterAttachment(AttachmentScope.World, binding);
        attachmentSchemaId = schema.SchemaId;
        var registration = builder.Build();
        return World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.Parse("30000000-0000-0000-0000-000000000003"),
            MaximumPersistenceAdmissionBytes = 1_048_576,
            WorldGuid = Guid.Parse("10000000-0000-0000-0000-000000000001"),
        });
    }

    private static World CreateResourceWorld(out Guid resourceId, out Guid stateSchemaId)
    {
        var assignment = new ContentSchemaIdentityAssignment("application/resource-state/ecs-test/v1");
        var schemaReference = new ContentSchemaReference(
            assignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var field = new ContentFieldDescriptor(
            1,
            23,
            4,
            true,
            1,
            1,
            [],
            0,
            ContentUnit.None,
            ContentReferenceKind.None,
            ContentAuthoring.None,
            null);
        var metadata = new DataSchema(
            schemaReference,
            assignment,
            ContentSchemaRole.ResourceState,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            [field]);
        var registry = new CompiledContentSchemaRegistry([metadata]);
        var schema = WorldResourceSchema<ulong>.FromRegistry(registry, schemaReference);
        var fieldDescriptor = schema.GetField(new ComponentFieldPath([ComponentPropertySegment.FieldTag(1)]));
        var storagePath = new ComponentStoragePath([ComponentStorageSegment.Member("Value")]);
        var fieldBinding = WorldResourceFieldBinding<ulong, ImmutableArray<ulong>>
            .PartitionArray<ulong, EcsResourcePartitionReader, EcsResourceAuthorReader>(
                fieldDescriptor,
                [storagePath],
                default,
                default);
        var partitionStorage = new ComponentStorageDeclaration(
            sizeof(ulong),
            [
                new ComponentStorageLeaf(
                    storagePath,
                    ComponentStorageKind.Value,
                    0,
                    sizeof(ulong),
                    0,
                    0,
                    ComponentStoragePurpose.SchemaValue),
            ],
            []);
        var factory = WorldResourceFactoryBinding<ulong, ImmutableArray<ulong>>.Create(
            Guid.Parse("40000000-0000-0000-0000-000000000002"),
            1,
            schema,
            partitionStorage,
            [fieldBinding],
            new EcsResourceDefaults());
        var codec = new EcsResourceStateCodec(factory, fieldDescriptor);
        resourceId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        stateSchemaId = schemaReference.SchemaId;
        var builder = new WorldRegistrationBuilder(registry);
        _ = builder.RegisterResource(
            new SystemResourceDescription(resourceId, 1, allowParallelAccess: true),
            new EcsResourceFactoryProvider(factory.RegistrationBinding),
            codec);
        var registration = builder.Build();
        return World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.Parse("30000000-0000-0000-0000-000000000003"),
            MaximumPersistenceAdmissionBytes = 1_048_576,
            WorldGuid = Guid.Parse("10000000-0000-0000-0000-000000000001"),
        });
    }

    private static World CreateStatefulSystemWorld(out Guid systemId, out ContentSchemaReference stateSchema)
    {
        var assignment = new ContentSchemaIdentityAssignment("application/system-state/ecs-test/v1");
        stateSchema = new ContentSchemaReference(
            assignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var field = new ContentFieldDescriptor(
            1,
            7,
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
        var metadata = new DataSchema(
            stateSchema,
            assignment,
            ContentSchemaRole.SystemState,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            [field]);
        var schemas = new CompiledContentSchemaRegistry([metadata]);
        var schema = WorldSystemSchema<EcsTestSystem>.FromRegistry(schemas, stateSchema);
        var runtimePath = new WorldSystemSourcePath(["Counter"]);
        var authorPath = new WorldSystemSourcePath(["Counter"]);
        var binding = WorldSystemFactoryBinding.Stateful<
            EcsTestSystem,
            EcsTestAuthor,
            EcsTestCreate,
            EcsTestDefaults,
            EcsTestRelease>(
                Guid.Parse("40000000-0000-0000-0000-000000000001"),
                1,
                schema,
                new WorldSystemSourceStateDeclaration(
                    typeof(EcsTestSystem),
                    [new WorldSystemSourceLeaf(runtimePath, typeof(int), WorldSystemSourcePurpose.Authoritative)]),
                [
                    WorldSystemFieldBinding<EcsTestSystem, EcsTestAuthor>.Scalar<
                        int,
                        EcsTestSystemCounterReader,
                        EcsTestAuthorCounterReader>(
                            schema.GetField(new ComponentFieldPath([ComponentPropertySegment.FieldTag(1)])),
                            [runtimePath],
                            [authorPath],
                            default,
                            default),
                ],
                default,
                default,
                new EcsTestStateCodec(stateSchema),
                default);
        systemId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var builder = new WorldRegistrationBuilder(schemas);
        _ = builder.RegisterSystem(
            new SystemDescription(
                systemId,
                0,
                SimulationStage.Input,
                [],
                [],
                [],
                SystemThreadMode.OwnerThread),
            new EcsTestSystemFactory(binding));
        var registration = builder.Build();
        return World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.Parse("30000000-0000-0000-0000-000000000003"),
            MaximumPersistenceAdmissionBytes = 1_048_576,
            WorldGuid = Guid.Parse("10000000-0000-0000-0000-000000000001"),
        });
    }

    [Test]
    public void CapturesResourceRegistrationInTheWorldRegistrationRoot()
    {
        using var world = CreateResourceWorld(out _, out _);
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
        var assertions = new ResourceRegistrationRootAssertions();
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
    public void WritesAttachmentPayloadAsACompleteNestedRecord()
    {
        using var world = CreateAttachmentWorld(out var attachmentSchemaId);
        world.SetAttachment(new EcsAttachmentValue { Counter = 23 }, AttachmentOwnership.Transferred);
        _ = world.Step();
        using var lease = world.AcquirePersistenceCaptureLease();
        var assertions = new EcsAttachmentAssertions(world.WorldGuid, attachmentSchemaId, expectedCounter: 23);

        _ = WorldPersistenceEcsTestHelper.CaptureEcsRecord(world, in lease, ref assertions);
    }

    [Test]
    public void WritesResourcePayloadAsACompleteNestedRecord()
    {
        using var world = CreateResourceWorld(out var resourceId, out var stateSchemaId);
        _ = world.Step();
        using var lease = world.AcquirePersistenceCaptureLease();
        var assertions = new EcsResourceAssertions(world.WorldGuid, resourceId, stateSchemaId, expectedValue: 17);

        _ = WorldPersistenceEcsTestHelper.CaptureEcsRecord(world, in lease, ref assertions);
    }

    [Test]
    public void WritesStatefulSystemPayloadAsACompleteNestedRecord()
    {
        using var world = CreateStatefulSystemWorld(out var systemId, out var stateSchema);
        _ = world.Step();
        using var lease = world.AcquirePersistenceCaptureLease();
        var assertions = new EcsSystemAssertions(world.WorldGuid, systemId, stateSchema.SchemaId, expectedCounter: 42);

        _ = WorldPersistenceEcsTestHelper.CaptureEcsRecord(world, in lease, ref assertions);
    }

    private readonly struct EcsAttachmentAssertions : IAction<EbinRecordReadView>
    {
        private readonly long _expectedCounter;
        private readonly Guid _expectedSchemaId;
        private readonly Guid _worldGuid;

        internal EcsAttachmentAssertions(Guid worldGuid, Guid expectedSchemaId, long expectedCounter)
        {
            _expectedCounter = expectedCounter;
            _expectedSchemaId = expectedSchemaId;
            _worldGuid = worldGuid;
        }

        private static Guid ReadGuid(EbinFieldReadView field)
        {
            Assert.That(field.Kind, Is.EqualTo(11));
            Assert.That(field.Payload.Length, Is.EqualTo(16));
            return new Guid(field.Payload, bigEndian: true);
        }

        public readonly void Invoke(scoped EbinRecordReadView record)
        {
            Assert.That(record.Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/ecs").SchemaId));
            AssertEmptyArray(record.GetField(0), 1);
            AssertEmptyArray(record.GetField(1), 2);
            AssertEmptyArray(record.GetField(2), 3);
            var attachments = record.GetField(3);
            Assert.That(attachments.Tag, Is.EqualTo(4));
            Assert.That(attachments.Count, Is.EqualTo(1));
            var attachment = record.GetRecordArrayElement(4, 0);
            Assert.That(
                attachment.Header.SchemaId,
                Is.EqualTo(new ContentSchemaIdentityAssignment("state/attachment").SchemaId));
            Assert.That(attachment.FieldCount, Is.EqualTo(2));
            Assert.That(attachment.TryGetField(2, out _), Is.False);
            var schema = attachment.GetRecord(1);
            Assert.That(
                schema.Header.SchemaId,
                Is.EqualTo(new ContentSchemaIdentityAssignment("content/schema-ref").SchemaId));
            Assert.That(ReadGuid(schema.GetField(0)), Is.EqualTo(_expectedSchemaId));
            Assert.That(attachment.GetRecord(3).Header.SchemaId, Is.EqualTo(_expectedSchemaId));
            Assert.That(
                BinaryPrimitives.ReadInt64LittleEndian(attachment.GetRecord(3).GetField(0).Payload),
                Is.EqualTo(_expectedCounter));
            Assert.That(ReadGuid(record.GetRecord(5).GetField(0)), Is.EqualTo(_worldGuid));
        }
    }

    private readonly record struct EcsAttachmentAuthor(long Counter);

    private readonly struct EcsAttachmentAuthorReader : IComponentValueReader<EcsAttachmentAuthor, long>
    {
        public long Get(in EcsAttachmentAuthor value)
            => value.Counter;
    }

    private sealed class EcsAttachmentStateProvider : IWorldAttachmentStateProvider<EcsAttachmentValue, EcsAttachmentAuthor>
    {
        private readonly ContentSchemaReference _schema;

        internal EcsAttachmentStateProvider(ContentSchemaReference schema)
        {
            _schema = schema;
        }

        public nint Count(
            EcsAttachmentValue value,
            WorldAttachmentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return MeasureRecord(value.Counter);
        }

        public WorldAttachmentReadResult<EcsAttachmentAuthor> Read(
            scoped ReadOnlySpan<byte> source,
            WorldAttachmentReadBinding<EcsAttachmentAuthor> binding,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public void Release(EcsAttachmentValue value, WorldAttachmentReleaseBinding binding)
        {
        }

        public EcsAttachmentValue Restore(
            in EcsAttachmentAuthor author,
            WorldAttachmentRestoreBinding binding,
            CancellationToken cancellationToken)
            => new() { Counter = author.Counter };

        public ImmutableArray<Diagnostic> Validate(
            EcsAttachmentValue value,
            WorldAttachmentValidationBinding binding,
            CancellationToken cancellationToken)
            => [];

        public nint Write(
            EcsAttachmentValue value,
            scoped Span<byte> destination,
            WorldAttachmentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Span<byte> payloadStorage = stackalloc byte[sizeof(long)];
            var payloadStatus = EbinPayloadWriter.Write(value.Counter, payloadStorage, out var payloadBytes, out var diagnostic);

            if (payloadStatus is not EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException(diagnostic?.Category);
            }

            Span<EbinFieldSource> fields = [new EbinFieldSource(1, 8, 1, 1, new EbinPayloadSlice(0, payloadBytes))];
            var source = new EbinRecordWriteView(
                _schema.SchemaId,
                _schema.Major,
                _schema.Minor,
                fields,
                payloadStorage[..payloadBytes]);
            var limits = EbinReadLimits.Foundation;
            var status = EbinStructuralWriter.Measure(in source, limits, out var recordBytes, out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException(diagnostic?.Category);
            }

            Span<byte> candidateStorage = stackalloc byte[recordBytes];
            status = EbinStructuralWriter.Write(
                in source,
                limits,
                candidateStorage,
                destination,
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
            Span<byte> payloadStorage = stackalloc byte[sizeof(long)];
            var status = EbinPayloadWriter.Write(counter, payloadStorage, out var payloadBytes, out var diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException(diagnostic?.Category);
            }

            Span<EbinFieldSource> fields = [new EbinFieldSource(1, 8, 1, 1, new EbinPayloadSlice(0, payloadBytes))];
            var source = new EbinRecordWriteView(
                _schema.SchemaId,
                _schema.Major,
                _schema.Minor,
                fields,
                payloadStorage[..payloadBytes]);
            status = EbinStructuralWriter.Measure(
                in source,
                EbinReadLimits.Foundation,
                out var recordBytes,
                out diagnostic);

            return (status is EbinWriteStatus.Complete)
                ? recordBytes
                : throw new InvalidOperationException(diagnostic?.Category);
        }
    }

    private sealed class EcsAttachmentValue
    {
        internal long Counter { get; set; }
    }

    private readonly struct EcsAttachmentValueReader : IComponentValueReader<EcsAttachmentValue, long>
    {
        public long Get(in EcsAttachmentValue value)
            => value.Counter;
    }

    private readonly struct EcsResourceAssertions : IAction<EbinRecordReadView>
    {
        private readonly Guid _expectedResourceId;
        private readonly Guid _expectedSchemaId;
        private readonly ulong _expectedValue;
        private readonly Guid _worldGuid;

        internal EcsResourceAssertions(Guid worldGuid, Guid expectedResourceId, Guid expectedSchemaId, ulong expectedValue)
        {
            _expectedResourceId = expectedResourceId;
            _expectedSchemaId = expectedSchemaId;
            _expectedValue = expectedValue;
            _worldGuid = worldGuid;
        }

        private static Guid ReadGuid(EbinFieldReadView field)
        {
            Assert.That(field.Kind, Is.EqualTo(11));
            Assert.That(field.Payload.Length, Is.EqualTo(16));
            return new Guid(field.Payload, bigEndian: true);
        }

        public readonly void Invoke(scoped EbinRecordReadView record)
        {
            Assert.That(record.Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/ecs").SchemaId));
            var resources = record.GetField(2);
            Assert.That(resources.Tag, Is.EqualTo(3));
            Assert.That(resources.Count, Is.EqualTo(1));
            var resource = record.GetRecordArrayElement(3, 0);
            Assert.That(
                resource.Header.SchemaId,
                Is.EqualTo(new ContentSchemaIdentityAssignment("state/resource-state").SchemaId));
            Assert.That(ReadGuid(resource.GetField(0)), Is.EqualTo(_expectedResourceId));
            var state = resource.GetRecord(2);
            Assert.That(state.Header.SchemaId, Is.EqualTo(_expectedSchemaId));
            var values = state.GetField(0);
            Assert.That(values.Kind, Is.EqualTo(23));
            Assert.That(values.Count, Is.EqualTo(1));
            Assert.That(values.Payload.Length, Is.EqualTo(40));
            Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(values.Payload[32..]), Is.EqualTo(_expectedValue));
            Assert.That(ReadGuid(record.GetRecord(5).GetField(0)), Is.EqualTo(_worldGuid));
        }
    }

    private readonly struct EcsResourceAuthorReader : IComponentArrayReader<ImmutableArray<ulong>, ulong>
    {
        public int GetCount(in ImmutableArray<ulong> value) => value.Length;

        public ulong GetElement(in ImmutableArray<ulong> value, int index) => value[index];
    }

    private readonly struct EcsResourceDefaults : IWorldResourceDefaultProvider<ulong>
    {
        public ulong Create(int partitionIndex, WorldResourceDefaultBinding binding)
            => checked(17UL + (ulong)partitionIndex);
    }

    private sealed class EcsResourceFactoryProvider : IWorldResourceFactory<ulong>
    {
        internal EcsResourceFactoryProvider(WorldResourceFactoryBinding<ulong> binding)
        {
            Binding = binding;
        }

        public WorldResourceFactoryBinding<ulong> Binding { get; }
    }

    private readonly struct EcsResourcePartitionReader : IComponentValueReader<ulong, ulong>
    {
        public ulong Get(in ulong value) => value;
    }

    private sealed class EcsResourceStateCodec : IWorldResourceStateCodec<ulong, ImmutableArray<ulong>>
    {
        private WorldResourceCodecBinding<ulong>? _binding;
        private readonly WorldResourceFactoryBinding<ulong, ImmutableArray<ulong>> _factory;
        private readonly WorldResourceFieldDescriptor _field;

        internal EcsResourceStateCodec(
            WorldResourceFactoryBinding<ulong, ImmutableArray<ulong>> factory,
            WorldResourceFieldDescriptor field)
        {
            _factory = factory;
            _field = field;
        }

        public WorldResourceCodecBinding<ulong> Binding
            => _binding ??= WorldResourceCodecBinding<ulong>.Create(_factory, this);

        public nint Count(
            scoped WorldResourceStateView<ulong> value,
            WorldResourceCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return MeasureRecord(binding);
        }

        public WorldResourceReadResult<ImmutableArray<ulong>> Read(
            scoped ReadOnlySpan<byte> source,
            WorldResourceReadBinding<ImmutableArray<ulong>> binding,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public void Restore(
            in ImmutableArray<ulong> author,
            WorldResourceRestoreBinding<ulong> binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (var index = 0; index < author.Length; index++)
            {
                var partition = author[index];
                binding.SetPartition(index, in partition);
            }
        }

        public ImmutableArray<Diagnostic> Validate(
            scoped WorldResourceStateView<ulong> value,
            WorldResourceValidationBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return binding.ValidateFields(value);
        }

        public nint Write(
            scoped WorldResourceStateView<ulong> value,
            scoped Span<byte> destination,
            WorldResourceCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Span<ulong> values = stackalloc ulong[64];
            Span<byte> arrayPayloadStorage = stackalloc byte[32 + (64 * sizeof(ulong))];
            Span<EbinFieldSource> fields = [default];
            InitializeArrayPayload(binding, values, arrayPayloadStorage, fields, out _, out var payloadBytes);
            var schema = _factory.Descriptor.Schema;
            var source = new EbinRecordWriteView(
                schema.SchemaId,
                schema.Major,
                schema.Minor,
                fields,
                arrayPayloadStorage[..payloadBytes]);
            var limits = EbinReadLimits.Foundation;
            var status = EbinStructuralWriter.Measure(in source, limits, out var recordBytes, out var diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException(diagnostic?.Category);
            }

            Span<byte> candidateStorage = stackalloc byte[recordBytes];
            status = EbinStructuralWriter.Write(
                in source,
                limits,
                candidateStorage,
                destination,
                out var bytesWritten,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException(diagnostic?.Category);
            }

            return bytesWritten;
        }

        private void InitializeArrayPayload(
            WorldResourceCaptureBinding binding,
            Span<ulong> values,
            Span<byte> payloadStorage,
            Span<EbinFieldSource> fields,
            out int count,
            out int payloadBytes)
        {
            count = binding.GetCount(_field);

            if (count is < 1 or > 64)
            {
                throw new InvalidOperationException("The resource-state field count is outside its registered FixedArray bounds.");
            }

            for (var index = 0; index < count; index++)
            {
                values[index] = binding.GetElement<ulong>(_field, index);
            }

            var status = EbinPayloadWriter.WriteFixedArray(
                values[..count],
                EbinReadLimits.Foundation,
                payloadStorage,
                out payloadBytes,
                out var diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException(diagnostic?.Category);
            }

            fields[0] = new EbinFieldSource(1, 23, 1, checked((ulong)count), new EbinPayloadSlice(0, payloadBytes));
        }

        private int MeasureRecord(WorldResourceCaptureBinding binding)
        {
            Span<ulong> values = stackalloc ulong[64];
            Span<byte> arrayPayloadStorage = stackalloc byte[32 + (64 * sizeof(ulong))];
            Span<EbinFieldSource> fields = [default];
            InitializeArrayPayload(binding, values, arrayPayloadStorage, fields, out _, out var payloadBytes);
            var schema = _factory.Descriptor.Schema;
            var source = new EbinRecordWriteView(
                schema.SchemaId,
                schema.Major,
                schema.Minor,
                fields,
                arrayPayloadStorage[..payloadBytes]);
            var status = EbinStructuralWriter.Measure(
                in source,
                EbinReadLimits.Foundation,
                out var recordBytes,
                out var diagnostic);

            return (status is EbinWriteStatus.Complete)
                ? recordBytes
                : throw new InvalidOperationException(diagnostic?.Category);
        }
    }

    private readonly struct EcsSystemAssertions : IAction<EbinRecordReadView>
    {
        private readonly int _expectedCounter;
        private readonly Guid _expectedStateSchemaId;
        private readonly Guid _expectedSystemId;
        private readonly Guid _worldGuid;

        internal EcsSystemAssertions(
            Guid worldGuid,
            Guid expectedSystemId,
            Guid expectedStateSchemaId,
            int expectedCounter)
        {
            _expectedCounter = expectedCounter;
            _expectedStateSchemaId = expectedStateSchemaId;
            _expectedSystemId = expectedSystemId;
            _worldGuid = worldGuid;
        }

        private static Guid ReadGuid(EbinFieldReadView field)
        {
            Assert.That(field.Kind, Is.EqualTo(11));
            Assert.That(field.Payload.Length, Is.EqualTo(16));
            return new Guid(field.Payload, bigEndian: true);
        }

        public readonly void Invoke(scoped EbinRecordReadView record)
        {
            Assert.That(record.Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/ecs").SchemaId));
            Assert.That(record.GetField(1).Count, Is.EqualTo(1));
            var systemRow = record.GetRecordArrayElement(2, 0);
            Assert.That(
                systemRow.Header.SchemaId,
                Is.EqualTo(new ContentSchemaIdentityAssignment("state/system-state").SchemaId));
            Assert.That(ReadGuid(systemRow.GetField(0)), Is.EqualTo(_expectedSystemId));
            var state = systemRow.GetRecord(2);
            Assert.That(state.Header.SchemaId, Is.EqualTo(_expectedStateSchemaId));
            Assert.That(state.FieldCount, Is.EqualTo(1));
            var counterField = state.GetField(0);
            Assert.That(counterField.Tag, Is.EqualTo(1));
            Assert.That(counterField.Kind, Is.EqualTo(7));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(counterField.Payload), Is.EqualTo(_expectedCounter));
            Assert.That(record.GetRecord(5).Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/identity-history").SchemaId));
            Assert.That(ReadGuid(record.GetRecord(5).GetField(0)), Is.EqualTo(_worldGuid));
        }
    }

    private readonly record struct EcsTestAuthor(int Counter);

    private readonly struct EcsTestAuthorCounterReader : IWorldSystemValueReader<EcsTestAuthor, int>
    {
        public int Invoke(in EcsTestAuthor source)
            => source.Counter;
    }

    private readonly struct EcsTestCreate : IWorldSystemCreate<EcsTestSystem>
    {
        public EcsTestSystem Invoke()
            => new();
    }

    private readonly struct EcsTestDefaults : IWorldSystemDefaults<EcsTestSystem>
    {
        public void Invoke(EcsTestSystem system)
            => system.Counter = 42;
    }

    private readonly struct EcsTestRelease : IWorldSystemRelease<EcsTestSystem>
    {
        public void Invoke(EcsTestSystem system)
        {
        }
    }

    private sealed class EcsTestStateCodec : IWorldSystemStateCodec<EcsTestSystem, EcsTestAuthor>
    {
        private readonly ContentSchemaReference _schema;

        internal EcsTestStateCodec(ContentSchemaReference schema)
        {
            _schema = schema;
        }

        private static void InitializePayload(
            int counter,
            Span<byte> payloadStorage,
            Span<EbinFieldSource> fields,
            out int payloadBytes)
        {
            var status = EbinPayloadWriter.Write(counter, payloadStorage, out payloadBytes, out var diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException(diagnostic?.Category);
            }

            fields[0] = new EbinFieldSource(1, 7, 1, 1, new EbinPayloadSlice(0, payloadBytes));
        }

        public nint Count(EcsTestSystem system, WorldSystemCaptureBinding binding, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return MeasureRecord(system.Counter);
        }

        public WorldSystemReadResult<EcsTestAuthor> Read(
            scoped ReadOnlySpan<byte> source,
            WorldSystemReadBinding<EcsTestAuthor> binding,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public void Restore(
            EcsTestSystem system,
            in EcsTestAuthor author,
            WorldStateRestoreBinding binding,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ImmutableArray<Diagnostic> Validate(
            EcsTestSystem system,
            WorldSystemValidationBinding binding,
            CancellationToken cancellationToken)
            => [];

        public nint Write(
            EcsTestSystem system,
            scoped Span<byte> destination,
            WorldSystemCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Span<byte> payloadStorage = stackalloc byte[sizeof(int)];
            Span<EbinFieldSource> fields = stackalloc EbinFieldSource[1];
            InitializePayload(system.Counter, payloadStorage, fields, out var payloadBytes);
            var source = new EbinRecordWriteView(
                _schema.SchemaId,
                _schema.Major,
                _schema.Minor,
                fields,
                payloadStorage[..payloadBytes]);
            var limits = EbinReadLimits.Foundation;
            var status = EbinStructuralWriter.Measure(in source, limits, out var recordBytes, out var diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException(diagnostic?.Category);
            }

            Span<byte> candidateStorage = stackalloc byte[recordBytes];
            status = EbinStructuralWriter.Write(
                in source,
                limits,
                candidateStorage,
                destination,
                out var bytesWritten,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException(diagnostic?.Category);
            }

            return bytesWritten;
        }

        private int MeasureRecord(int counter)
        {
            Span<byte> payloadStorage = stackalloc byte[sizeof(int)];
            Span<EbinFieldSource> fields = stackalloc EbinFieldSource[1];
            InitializePayload(counter, payloadStorage, fields, out var payloadBytes);
            var source = new EbinRecordWriteView(
                _schema.SchemaId,
                _schema.Major,
                _schema.Minor,
                fields,
                payloadStorage[..payloadBytes]);
            var status = EbinStructuralWriter.Measure(
                in source,
                EbinReadLimits.Foundation,
                out var recordBytes,
                out var diagnostic);

            return (status is EbinWriteStatus.Complete)
                ? recordBytes
                : throw new InvalidOperationException(diagnostic?.Category);
        }
    }

    private sealed class EcsTestSystem : IWorldSystem
    {
        internal int Counter { get; set; }

        public void Execute(WorldSystemContext context)
        {
        }
    }

    private readonly struct EcsTestSystemCounterReader : IWorldSystemValueReader<EcsTestSystem, int>
    {
        public int Invoke(in EcsTestSystem source)
            => source.Counter;
    }

    private sealed class EcsTestSystemFactory : IWorldSystemFactory
    {
        internal EcsTestSystemFactory(WorldSystemFactoryBinding binding)
        {
            Binding = binding;
        }

        public WorldSystemFactoryBinding Binding { get; }
    }

    private struct ResourceRegistrationRootAssertions : IAction<EbinRecordReadView>
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

            var resources = record.GetField(3);
            IsMatch = (resources.Tag == 4)
                && (resources.Kind == 24)
                && (resources.Count == 1)
                && (BinaryPrimitives.ReadUInt64LittleEndian(resources.Payload) == 1);
        }
    }
}
