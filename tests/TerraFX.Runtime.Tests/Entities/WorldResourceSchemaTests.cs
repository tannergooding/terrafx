// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldResourceSchemaTests
{
    private static (
        CompiledContentSchemaRegistry Registry,
        WorldResourceFactoryBinding<ulong, ImmutableArray<ulong>> Factory,
        WorldResourceFieldDescriptor Field)
        CreateFactoryFixture()
    {
        var assignment = new ContentSchemaIdentityAssignment("application/resource-state/codec-dispatch-test/v1");
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
            64,
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
        var descriptor = schema.GetField(new ComponentFieldPath([ComponentPropertySegment.FieldTag(1)]));
        var storagePath = new ComponentStoragePath([ComponentStorageSegment.Member("Value")]);
        var binding = WorldResourceFieldBinding<ulong, ImmutableArray<ulong>>
            .PartitionArray<ulong, ResourcePartitionReader, ResourceAuthorReader>(
                descriptor,
                [storagePath],
                default,
                default);
        var storage = new ComponentStorageDeclaration(
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
            Guid.NewGuid(),
            1,
            schema,
            storage,
            [binding],
            new ResourceDefaults());
        return (registry, factory, descriptor);
    }

    [Test]
    public void FactoryBindingCapturesDefaultsAndErasedRegistrationFromOneValidatedFamily()
    {
        var assignment = new ContentSchemaIdentityAssignment("application/resource-state/factory-test/v1");
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
            64,
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
            .PartitionArray<ulong, ResourcePartitionReader, ResourceAuthorReader>(
                fieldDescriptor,
                [storagePath],
                default,
                default);
        var storage = new ComponentStorageDeclaration(
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
        var factoryId = Guid.NewGuid();
        var binding = WorldResourceFactoryBinding<ulong, ImmutableArray<ulong>>.Create(
            factoryId,
            1,
            schema,
            storage,
            [fieldBinding],
            new ResourceDefaults());

        Assert.That(binding.Descriptor.FactoryId, Is.EqualTo(factoryId));
        Assert.That(binding.Descriptor.FactoryVersion, Is.EqualTo(1));
        Assert.That(binding.Descriptor.Schema, Is.EqualTo(schemaReference));
        Assert.That(binding.Descriptor.PartitionStorage, Is.SameAs(storage));
        Assert.That(binding.Descriptor.Fields, Is.EqualTo(ImmutableArray.Create(fieldDescriptor)));
        Assert.That(binding.RegistrationBinding.Descriptor, Is.SameAs(binding.Descriptor));
        Assert.That(
            () => WorldResourceFactoryBinding<ulong, ImmutableArray<ulong>>.Create(
                Guid.NewGuid(),
                1,
                schema,
                storage,
                [],
                new ResourceDefaults()),
            Throws.ArgumentException);
    }

    [Test]
    public void FactoryBindingClosesNestedFixedArrayResourceFields()
    {
        var nestedAssignment = new ContentSchemaIdentityAssignment("application/descriptor/resource-factory-nested/v1");
        var nestedReference = new ContentSchemaReference(
            nestedAssignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var nestedField = new ContentFieldDescriptor(
            2,
            23,
            4,
            true,
            1,
            64,
            [],
            0,
            ContentUnit.None,
            ContentReferenceKind.None,
            ContentAuthoring.None,
            null);
        var nestedMetadata = new DataSchema(
            nestedReference,
            nestedAssignment,
            ContentSchemaRole.Descriptor,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            [nestedField]);
        var assignment = new ContentSchemaIdentityAssignment("application/resource-state/nested-factory-test/v1");
        var schemaReference = new ContentSchemaReference(
            assignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var recordField = new ContentFieldDescriptor(
            1,
            22,
            0,
            true,
            1,
            1,
            [nestedReference],
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
            [recordField]);
        var registry = new CompiledContentSchemaRegistry([metadata, nestedMetadata]);
        var schema = WorldResourceSchema<ulong>.FromRegistry(registry, schemaReference);
        var nestedPath = new ComponentFieldPath([
            ComponentPropertySegment.FieldTag(1),
            ComponentPropertySegment.FieldTag(2),
        ]);
        var nestedDescriptor = schema.GetField(nestedPath);
        var storagePath = new ComponentStoragePath([ComponentStorageSegment.Member("Value")]);
        var fieldBinding = WorldResourceFieldBinding<ulong, ImmutableArray<ulong>>
            .PartitionArray<ulong, ResourcePartitionReader, ResourceAuthorReader>(
                nestedDescriptor,
                [storagePath],
                default,
                default);
        var storage = new ComponentStorageDeclaration(
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
        var binding = WorldResourceFactoryBinding<ulong, ImmutableArray<ulong>>.Create(
            Guid.NewGuid(),
            1,
            schema,
            storage,
            [fieldBinding],
            new ResourceDefaults());

        Assert.That(binding.Descriptor.Fields, Is.EqualTo(ImmutableArray.Create(nestedDescriptor)));
    }

    [Test]
    public void PersistenceCaptureWritesRegisteredResourceStateFromCommittedWorld()
    {
        var (registry, factory, field) = CreateFactoryFixture();
        var resourceId = Guid.NewGuid();
        var description = new SystemResourceDescription(resourceId, 2, allowParallelAccess: true);
        var codec = new ResourceStateCodec(factory, field);
        var builder = new WorldRegistrationBuilder(registry);
        _ = builder.RegisterResource(
            description,
            new ResourceFactoryProvider(factory.RegistrationBinding),
            codec);
        var registration = builder.Build();
        Assert.That(
            WorldPersistenceResourceRegistrationArrayWriter.Measure(
                registration,
                EbinReadLimits.Foundation,
                out var registrationWorkspace,
                out var registrationDiagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            registrationDiagnostic?.Category);
        Assert.That(registrationDiagnostic, Is.Null);
        var registrationSlices = new EbinPayloadSlice[registrationWorkspace.RecordCount];
        var registrationStorage = new byte[registrationWorkspace.RecordStorageBytes];
        var registrationCandidate = new byte[registrationWorkspace.MaximumCandidateBytes];
        var registrationArrayPayload = new byte[registrationWorkspace.ArrayPayloadBytes];
        Assert.That(
            WorldPersistenceResourceRegistrationArrayWriter.Write(
                registration,
                EbinReadLimits.Foundation,
                registrationWorkspace,
                registrationSlices,
                registrationStorage,
                registrationCandidate,
                registrationArrayPayload,
                out var registrationPayloadBytes,
                out registrationDiagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            registrationDiagnostic?.Category);
        Assert.That(registrationDiagnostic, Is.Null);
        Assert.That(registrationPayloadBytes, Is.EqualTo(registrationWorkspace.ArrayPayloadBytes));
        Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(registrationArrayPayload), Is.EqualTo(1));
        var rowStart = BinaryPrimitives.ReadUInt64LittleEndian(registrationArrayPayload.AsSpan(8));
        var rowEnd = BinaryPrimitives.ReadUInt64LittleEndian(registrationArrayPayload.AsSpan(16));
        var assertions = new ResourceRegistrationArrayAssertions(
            resourceId,
            description.PartitionCount,
            description.AllowParallelAccess,
            factory.Descriptor.Schema,
            factory.Descriptor.FactoryId,
            factory.Descriptor.FactoryVersion);
        Assert.That(
            EbinStructuralReader.Read(
                registrationArrayPayload.AsSpan(checked((int)rowStart), checked((int)(rowEnd - rowStart))),
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref assertions,
                out var rowBytes,
                out var rowDiagnostic),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(rowDiagnostic, Is.Null);
        Assert.That(assertions.IsMatch, Is.True);
        Assert.That(rowBytes, Is.EqualTo(checked((int)(rowEnd - rowStart))));
        var emptyArrayPayload = new byte[16];
        Assert.That(
            EbinPayloadWriter.WriteRecordArray(
                [],
                [],
                EbinReadLimits.Foundation,
                emptyArrayPayload,
                out var emptyArrayBytes,
                out registrationDiagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            registrationDiagnostic?.Category);
        Assert.That(emptyArrayBytes, Is.EqualTo(emptyArrayPayload.Length));
        var rootPayloadStorage = new byte[checked((3 * emptyArrayPayload.Length) + registrationArrayPayload.Length)];
        using var world = World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        _ = world.Step();

        using var lease = world.AcquirePersistenceCaptureLease();
        Assert.That(
            lease.MeasureRegistrationRecord(
                emptyArrayPayload,
                emptyArrayPayload,
                emptyArrayPayload,
                registrationArrayPayload,
                rootPayloadStorage,
                out var registrationRootWorkspace,
                out registrationDiagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            registrationDiagnostic?.Category);
        Assert.That(registrationDiagnostic, Is.Null);
        Assert.That(registrationRootWorkspace.RootPayloadBytes, Is.EqualTo(rootPayloadStorage.Length));
        Assert.That(
            lease.TryReserveAdmissionBytes(registrationRootWorkspace.WorkspaceBytes, out var registrationReservation),
            Is.True);
        using var admittedRegistrationReservation = registrationReservation
            ?? throw new InvalidOperationException("The registration root admission reservation was not created.");
        var rootCandidateStorage = new byte[registrationRootWorkspace.RecordBytes];
        var rootDestination = new byte[registrationRootWorkspace.RecordBytes];
        Assert.That(
            lease.WriteRegistrationRecord(
                admittedRegistrationReservation,
                registrationRootWorkspace,
                emptyArrayPayload,
                emptyArrayPayload,
                emptyArrayPayload,
                registrationArrayPayload,
                rootPayloadStorage,
                rootCandidateStorage,
                rootDestination,
                out var rootBytesWritten,
                out registrationDiagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            registrationDiagnostic?.Category);
        Assert.That(registrationDiagnostic, Is.Null);
        Assert.That(rootBytesWritten, Is.EqualTo(registrationRootWorkspace.RecordBytes));
        _ = WorldPersistenceRegistrationReader.Read(
            rootDestination,
            EbinReadLimits.Foundation,
            maximumComponentTypes: 256,
            targetRegistration: registration);
        var enumerator = lease.GetResourceStateEnumerator();
        Assert.That(enumerator.MoveNext(), Is.True);
        Assert.That(enumerator.CurrentIndex, Is.EqualTo(0));
        Assert.That(enumerator.CurrentResourceId, Is.EqualTo(resourceId));
        Assert.That(enumerator.CurrentSchema, Is.EqualTo(factory.Descriptor.Schema));

        var byteCount = lease.CountResourceStateRecord(enumerator.CurrentIndex, CancellationToken.None);
        Assert.That(byteCount, Is.EqualTo((nint)(2 * sizeof(ulong))));
        var encoded = new byte[checked((int)byteCount)];
        var written = lease.WriteResourceStateRecord(
            enumerator.CurrentIndex,
            encoded,
            CancellationToken.None);
        Span<byte> expected = stackalloc byte[2 * sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(expected[..sizeof(ulong)], 2);
        BinaryPrimitives.WriteUInt64LittleEndian(expected[sizeof(ulong)..], 3);

        Assert.That(written, Is.EqualTo(byteCount));
        Assert.That(encoded, Is.EqualTo(expected.ToArray()));
        Assert.That(enumerator.MoveNext(), Is.False);

        var staleCurrentRejected = false;

        try
        {
            _ = enumerator.CurrentResourceId;
        }
        catch (InvalidOperationException)
        {
            staleCurrentRejected = true;
        }

        Assert.That(staleCurrentRejected, Is.True);
    }

    [Test]
    public void ResourceFieldsResolveExactRecordAndArrayPathsForPartitionBindings()
    {
        var nestedAssignment = new ContentSchemaIdentityAssignment("application/descriptor/resource-test/v1");
        var nestedReference = new ContentSchemaReference(
            nestedAssignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var nestedField = new ContentFieldDescriptor(
            1,
            4,
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
        var nestedMetadata = new DataSchema(
            nestedReference,
            nestedAssignment,
            ContentSchemaRole.Descriptor,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            [nestedField]);
        var assignment = new ContentSchemaIdentityAssignment("application/resource-state/resource-test/v1");
        var schemaReference = new ContentSchemaReference(
            assignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var arrayField = new ContentFieldDescriptor(
            1,
            23,
            4,
            true,
            1,
            64,
            [],
            0,
            ContentUnit.None,
            ContentReferenceKind.None,
            ContentAuthoring.None,
            null);
        var recordField = new ContentFieldDescriptor(
            2,
            22,
            0,
            true,
            1,
            1,
            [nestedReference],
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
            [arrayField, recordField]);
        var componentAssignment = new ContentSchemaIdentityAssignment("application/component/resource-test/v1");
        var componentReference = new ContentSchemaReference(
            componentAssignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var componentMetadata = new DataSchema(
            componentReference,
            componentAssignment,
            ContentSchemaRole.Component,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.Persistent,
            ContentTransientRestore.NotApplicable,
            [new ContentFieldDescriptor(
                1,
                4,
                0,
                true,
                1,
                1,
                [],
                0,
                ContentUnit.None,
                ContentReferenceKind.None,
                ContentAuthoring.None,
                null)]);
        var registry = new CompiledContentSchemaRegistry([metadata, nestedMetadata, componentMetadata]);
        var schema = WorldResourceSchema<ulong>.FromRegistry(registry, schemaReference);
        var arrayPath = new ComponentFieldPath([
            ComponentPropertySegment.FieldTag(1),
            ComponentPropertySegment.FixedArrayIndex(0),
        ]);
        var arrayDescriptor = schema.GetField(arrayPath);
        var recordDescriptor = schema.GetField(new ComponentFieldPath([
            ComponentPropertySegment.FieldTag(2),
            ComponentPropertySegment.FieldTag(1),
        ]));
        var storagePath = new ComponentStoragePath([ComponentStorageSegment.Member("Value")]);
        var binding = WorldResourceFieldBinding<ulong, ImmutableArray<ulong>>
            .PartitionArray<ulong, ResourcePartitionReader, ResourceAuthorReader>(
                arrayDescriptor,
                [storagePath],
                default,
                default);

        Assert.That(schema.Fields, Has.Length.EqualTo(2));
        Assert.That(arrayDescriptor.Metadata, Is.SameAs(arrayField));
        Assert.That(arrayDescriptor.Path, Is.EqualTo(arrayPath));
        Assert.That(recordDescriptor.Metadata, Is.SameAs(nestedField));
        Assert.That(recordDescriptor.Schema, Is.EqualTo(schemaReference));
        Assert.That(binding.Descriptor, Is.SameAs(arrayDescriptor));
        Assert.That(binding.PartitionStoragePaths, Is.EqualTo(ImmutableArray.Create(storagePath)));
        Assert.That(
            () => WorldResourceSchema<ulong>.FromRegistry(registry, componentReference),
            Throws.ArgumentException);
        Assert.That(
            () => WorldResourceFieldBinding<ulong, ImmutableArray<ulong>>
                .PartitionArray<ulong, ResourcePartitionReader, ResourceAuthorReader>(
                    recordDescriptor,
                    [storagePath],
                    default,
                    default),
            Throws.ArgumentException);
    }

    [Test]
    public void WorldRegistrationCreatesFreshResourcePartitionsAndRunsDefaultsAndValidation()
    {
        var (registry, factory, field) = CreateFactoryFixture();
        var resourceId = Guid.NewGuid();
        var description = new SystemResourceDescription(resourceId, 2, allowParallelAccess: true);
        var factoryProvider = new ResourceFactoryProvider(factory.RegistrationBinding);
        var codec = new ResourceStateCodec(factory, field);
        var builder = new WorldRegistrationBuilder(registry);
        _ = builder.RegisterResource(description, factoryProvider, codec);
        var registration = builder.Build();

        Assert.That(factoryProvider.BindingReadCount, Is.EqualTo(1));
        Assert.That(codec.BindingReadCount, Is.EqualTo(1));
        var resourceDescriptor = registration.GetResourceBinding(resourceId);
        Assert.That(resourceDescriptor.ResourceId, Is.EqualTo(resourceId));
        Assert.That(resourceDescriptor.PartitionCount, Is.EqualTo(2));
        Assert.That(resourceDescriptor.AllowParallelAccess, Is.True);
        Assert.That(resourceDescriptor.Factory, Is.SameAs(factory.Descriptor));
        Assert.That(() => registration.GetResourceBinding(Guid.Empty), Throws.ArgumentException);
        Assert.That(() => registration.GetResourceBinding(Guid.NewGuid()), Throws.TypeOf<KeyNotFoundException>());

        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        using var world = World.Create(new WorldOptions(registration) {
            AdmissionDomain = domain,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });

        Assert.That(codec.ValidationCount, Is.EqualTo(1));
        Assert.That(codec.ValidatedValues, Is.EqualTo(ImmutableArray.Create<ulong>(2, 3)));
    }

    [Test]
    public void WorldRegistrationFailureReleasesAdmissionBeforeRetryingSameIdentity()
    {
        var (registry, factory, field) = CreateFactoryFixture();
        var resourceId = Guid.NewGuid();
        var codec = new ResourceStateCodec(factory, field) {
            RejectValidation = true,
        };
        var builder = new WorldRegistrationBuilder(registry);
        _ = builder.RegisterResource(
            new SystemResourceDescription(resourceId, 2, allowParallelAccess: false),
            new ResourceFactoryProvider(factory.RegistrationBinding),
            codec);
        var registration = builder.Build();
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        var options = new WorldOptions(registration) {
            AdmissionDomain = domain,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        };

        _ = Assert.Throws<InvalidOperationException>(() => World.Create(options));
        codec.RejectValidation = false;

        using var world = World.Create(options);
        Assert.That(world.WorldGuid, Is.EqualTo(options.WorldGuid));
        Assert.That(codec.ValidationCount, Is.EqualTo(2));
    }

    [Test]
    public void WorldRegistrationRejectsMismatchedResourceFactoryCodecFamilies()
    {
        var (registry, firstFactory, firstField) = CreateFactoryFixture();
        var (_, secondFactory, secondField) = CreateFactoryFixture();
        var codec = new ResourceStateCodec(secondFactory, secondField);
        var builder = new WorldRegistrationBuilder(registry);
        var resourceId = Guid.NewGuid();

        var exception = Assert.Throws<ArgumentException>(() => builder.RegisterResource(
            new SystemResourceDescription(resourceId, 2, allowParallelAccess: false),
            new ResourceFactoryProvider(firstFactory.RegistrationBinding),
            codec));

        Assert.That(exception!.ParamName, Is.EqualTo("codec"));
        var registration = builder.Build();
        Assert.That(() => registration.GetResourceBinding(resourceId), Throws.TypeOf<KeyNotFoundException>());
    }

    private readonly struct ResourceAuthorReader : IComponentArrayReader<ImmutableArray<ulong>, ulong>
    {
        public int GetCount(in ImmutableArray<ulong> value) => value.Length;

        public ulong GetElement(in ImmutableArray<ulong> value, int index) => value[index];
    }

    private readonly struct ResourceDefaults : IWorldResourceDefaultProvider<ulong>
    {
        public ulong Create(int partitionIndex, WorldResourceDefaultBinding binding)
            => checked((ulong)partitionIndex + (ulong)binding.PartitionCount);
    }

    private sealed class ResourceFactoryProvider : IWorldResourceFactory<ulong>
    {
        private readonly WorldResourceFactoryBinding<ulong> _binding;
        private int _bindingReadCount;

        internal ResourceFactoryProvider(WorldResourceFactoryBinding<ulong> binding)
        {
            _binding = binding;
        }

        public WorldResourceFactoryBinding<ulong> Binding
        {
            get
            {
                _bindingReadCount++;
                return _binding;
            }
        }

        internal int BindingReadCount => _bindingReadCount;
    }

    private readonly struct ResourcePartitionReader : IComponentValueReader<ulong, ulong>
    {
        public ulong Get(in ulong value) => value;
    }

    private struct ResourceRegistrationArrayAssertions : IAction<EbinRecordReadView>
    {
        private readonly bool _allowParallelAccess;
        private readonly int _factoryVersion;
        private readonly Guid _factoryId;
        private readonly int _partitionCount;
        private readonly Guid _resourceId;
        private readonly ContentSchemaReference _schema;

        internal ResourceRegistrationArrayAssertions(
            Guid resourceId,
            int partitionCount,
            bool allowParallelAccess,
            ContentSchemaReference schema,
            Guid factoryId,
            int factoryVersion)
        {
            _allowParallelAccess = allowParallelAccess;
            _factoryVersion = factoryVersion;
            _factoryId = factoryId;
            _partitionCount = partitionCount;
            _resourceId = resourceId;
            _schema = schema;
            IsMatch = false;
        }

        internal bool IsMatch { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
        {
            IsMatch = (record.Header.SchemaId == new ContentSchemaIdentityAssignment("state/resource-registration").SchemaId)
                && (record.Header.Major == 1)
                && (record.Header.Minor == 0)
                && (record.FieldCount == 6);

            if (!IsMatch)
            {
                return;
            }

            var resourceId = record.GetField(0);
            var partitionCount = record.GetField(1);
            var allowParallelAccess = record.GetField(2);
            var stateSchema = record.GetRecord(4);
            var factoryId = record.GetField(4);
            var factoryVersion = record.GetField(5);
            IsMatch = (new Guid(resourceId.Payload, bigEndian: true) == _resourceId)
                && (BinaryPrimitives.ReadUInt32LittleEndian(partitionCount.Payload) == (uint)_partitionCount)
                && (allowParallelAccess.Payload[0] == (_allowParallelAccess ? (byte)1 : (byte)0))
                && (stateSchema.Header.SchemaId == new ContentSchemaIdentityAssignment("content/schema-ref").SchemaId)
                && (new Guid(stateSchema.GetField(0).Payload, bigEndian: true) == _schema.SchemaId)
                && (new Guid(factoryId.Payload, bigEndian: true) == _factoryId)
                && (BinaryPrimitives.ReadUInt32LittleEndian(factoryVersion.Payload) == (uint)_factoryVersion);
        }
    }

    private sealed class ResourceStateCodec : IWorldResourceStateCodec<ulong, ImmutableArray<ulong>>
    {
        private WorldResourceCodecBinding<ulong>? _binding;
        private int _bindingReadCount;
        private readonly WorldResourceFactoryBinding<ulong, ImmutableArray<ulong>> _factory;
        private readonly WorldResourceFieldDescriptor _field;
        private bool _rejectReads;
        private bool _rejectValidation;
        private ImmutableArray<ulong> _validatedValues;
        private int _validationCount;

        internal ResourceStateCodec(
            WorldResourceFactoryBinding<ulong, ImmutableArray<ulong>> factory,
            WorldResourceFieldDescriptor field)
        {
            _factory = factory;
            _field = field;
        }

        public WorldResourceCodecBinding<ulong> Binding
        {
            get
            {
                _bindingReadCount++;
                return _binding ??= WorldResourceCodecBinding<ulong>.Create(_factory, this);
            }
        }

        internal int BindingReadCount => _bindingReadCount;

        internal bool RejectReads
        {
            get
            {
                return _rejectReads;
            }
            set
            {
                _rejectReads = value;
            }
        }

        internal bool RejectValidation
        {
            get
            {
                return _rejectValidation;
            }
            set
            {
                _rejectValidation = value;
            }
        }

        internal ImmutableArray<ulong> ValidatedValues => _validatedValues;

        internal int ValidationCount => _validationCount;

        public nint Count(
            scoped WorldResourceStateView<ulong> value,
            WorldResourceCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return checked(binding.GetCount(_field) * sizeof(ulong));
        }

        public WorldResourceReadResult<ImmutableArray<ulong>> Read(
            scoped ReadOnlySpan<byte> source,
            WorldResourceReadBinding<ImmutableArray<ulong>> binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_rejectReads)
            {
                return binding.Reject(
                    WorldResourceReadFailureKind.InvalidField,
                    0,
                    _field.Path,
                    "The resource test codec rejected the field.");
            }

            var author = ImmutableArray.Create<ulong>(11, 17);
            return binding.Complete(in author, source.Length);
        }

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
            var observedValues = ImmutableArray.CreateBuilder<ulong>(value.PartitionCount);

            for (var index = 0; index < value.PartitionCount; index++)
            {
                observedValues.Add(value.ReadPartition(index));
            }

            _validatedValues = observedValues.MoveToImmutable();
            _validationCount++;
            var diagnostics = binding.ValidateFields(value);

            if (_rejectValidation)
            {
                return [
                    new Diagnostic(
                        "TEST_RESOURCE_INVALID",
                        DiagnosticSeverity.Error,
                        0,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        "WorldResourceStateCodec.Validate",
                        null,
                        "The test resource validation rejected its partitions.",
                        []),
                ];
            }

            return diagnostics;
        }

        public nint Write(
            scoped WorldResourceStateView<ulong> value,
            scoped Span<byte> destination,
            WorldResourceCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = binding.GetCount(_field);
            var bytesWritten = checked(count * sizeof(ulong));
            ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, bytesWritten);

            for (var index = 0; index < count; index++)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(
                    destination.Slice(index * sizeof(ulong), sizeof(ulong)),
                    binding.GetElement<ulong>(_field, index));
            }

            return bytesWritten;
        }
    }
}
