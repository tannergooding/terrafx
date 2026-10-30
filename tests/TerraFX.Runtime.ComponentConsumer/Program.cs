// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using TerraFX;
using TerraFX.Animation;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Physics;
using TerraFX.Scenes;

namespace TerraFX.Runtime.ComponentConsumer;

internal static class Program
{
    [SuppressMessage("Globalization", "CA1303", Justification = "This is a machine-checked public-consumer completion marker.")]
    public static int Main()
    {
        var assignment = new ContentSchemaIdentityAssignment("application/component/public-consumer/v1");
        var schemaReference = new ContentSchemaReference(assignment.SchemaId, 1, 0, ContentSchemaScope.Content);
        ImmutableArray<ContentFieldDescriptor> fields = [
            Field(1, 7),
            Field(2, 7),
            Field(3, 11),
        ];
        var metadata = new DataSchema(
            schemaReference,
            assignment,
            ContentSchemaRole.Component,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.Persistent,
            ContentTransientRestore.NotApplicable,
            fields);
        var referenceAssignment = new ContentSchemaIdentityAssignment("application/component/reference-public-consumer/v1");
        var referenceSchemaReference = new ContentSchemaReference(
            referenceAssignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var referenceMetadata = new DataSchema(
            referenceSchemaReference,
            referenceAssignment,
            ContentSchemaRole.Component,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.Persistent,
            ContentTransientRestore.NotApplicable,
            [
                new ContentFieldDescriptor(
                    1,
                    11,
                    0,
                    true,
                    1,
                    1,
                    [],
                    0,
                    ContentUnit.None,
                    ContentReferenceKind.Entity,
                    ContentAuthoring.None,
                    null),
            ]);
        var systemStateMetadata = CreateApplicationSchema(
            "application/system-state/public-consumer/v1",
            ContentSchemaRole.SystemState,
            [Field(1, 8)]);
        var applicationSchemas = ImmutableArray.Create(
            metadata,
            CreateApplicationSchema(
                "application/tag/public-consumer/v1",
                ContentSchemaRole.EntityTag,
                []),
            systemStateMetadata,
            CreateApplicationSchema(
                "application/resource-state/public-consumer/v1",
                ContentSchemaRole.ResourceState,
                [Field(1, 23, elementKind: 4, minimumCount: 1, maximumCount: 64)]),
            CreateApplicationSchema(
                "application/attachment-state/public-consumer/v1",
                ContentSchemaRole.AttachmentState,
                [Field(1, 7)]),
            referenceMetadata);
        var registry = new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
            .. RenderingSchemaCatalogue.Schemas,
            .. AnimationSchemaCatalogue.Schemas,
            .. PhysicsSchemaCatalogue.Schemas,
            .. StandaloneContinuationSchemaCatalogue.Schemas,
            .. applicationSchemas,
        ]);
        var attachmentSchemaReference = applicationSchemas[4].Schema;
        var attachmentField = WorldAttachmentFieldBinding<PublicConsumerSystemAttachment, int>.Value<
            int,
            FirstAttachmentReader,
            FirstAttachmentAuthorReader>(
                registry,
                attachmentSchemaReference,
                new ComponentFieldPath([ComponentPropertySegment.FieldTag(1)]),
                default,
                default);
        var attachmentProvider = new PublicConsumerSystemAttachmentProvider();
        var attachmentBinding = WorldAttachmentFactoryBinding<PublicConsumerSystemAttachment, int>.Create(
            Guid.NewGuid(),
            1,
            registry,
            attachmentSchemaReference,
            [attachmentField],
            attachmentProvider);
        var resourceAssignment = new ContentSchemaIdentityAssignment("application/resource-state/public-consumer/v1");
        var resourceSchemaReference = new ContentSchemaReference(
            resourceAssignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var resourceSchema = WorldResourceSchema<ulong>.FromRegistry(registry, resourceSchemaReference);
        var resourceField = WorldResourceFieldBinding<ulong, ImmutableArray<ulong>>
            .PartitionArray<ulong, ResourcePartitionReader, ResourceAuthorArrayReader>(
                resourceSchema.GetField(new ComponentFieldPath([ComponentPropertySegment.FieldTag(1)])),
                [MemberPath("Value")],
                default,
                default);
        var resourceStorage = new ComponentStorageDeclaration(
            sizeof(ulong),
            [
                new ComponentStorageLeaf(
                    MemberPath("Value"),
                    ComponentStorageKind.Value,
                    0,
                    sizeof(ulong),
                    0,
                    0,
                    ComponentStoragePurpose.SchemaValue),
            ],
            []);
        var resourceFactory = WorldResourceFactoryBinding<ulong, ImmutableArray<ulong>>.Create(
            Guid.NewGuid(),
            1,
            resourceSchema,
            resourceStorage,
            [resourceField],
            new ResourceDefaultProvider());
        var systemCounters = new SystemLifecycleCounters();
        var systemBinding = WorldSystemFactoryBinding.Stateless<
            PublicConsumerSystem,
            PublicConsumerSystemCreate,
            PublicConsumerSystemRelease>(
                Guid.NewGuid(),
                1,
                new WorldSystemSourceStateDeclaration(typeof(PublicConsumerSystem), []),
                new PublicConsumerSystemCreate(systemCounters),
                new PublicConsumerSystemRelease(systemCounters));
        var statefulSystemCounters = new PublicConsumerSystemStateCounters();
        var systemStateSchema = WorldSystemSchema<PublicConsumerSystemState>.FromRegistry(
            registry,
            systemStateMetadata.Schema);
        var systemStateRuntimePath = new WorldSystemSourcePath(["Counter"]);
        var systemStateAuthorPath = new WorldSystemSourcePath(["Counter"]);
        var systemStateField = WorldSystemFieldBinding<PublicConsumerSystemState, PublicConsumerSystemStateAuthor>
            .Scalar<long, PublicConsumerSystemStateRuntimeReader, PublicConsumerSystemStateAuthorReader>(
                systemStateSchema.Fields[0],
                [systemStateRuntimePath],
                [systemStateAuthorPath],
                default,
                default);
        var statefulSystemBinding = WorldSystemFactoryBinding.Stateful<
            PublicConsumerSystemState,
            PublicConsumerSystemStateAuthor,
            PublicConsumerSystemStateCreate,
            PublicConsumerSystemStateDefaults,
            PublicConsumerSystemStateRelease>(
            Guid.NewGuid(),
            1,
            systemStateSchema,
            new WorldSystemSourceStateDeclaration(
                typeof(PublicConsumerSystemState),
                [
                    new WorldSystemSourceLeaf(
                        systemStateRuntimePath,
                        typeof(long),
                        WorldSystemSourcePurpose.Authoritative),
                ]),
            [systemStateField],
            new PublicConsumerSystemStateCreate(statefulSystemCounters),
            new PublicConsumerSystemStateDefaults(statefulSystemCounters),
            new PublicConsumerSystemStateCodec(statefulSystemCounters),
            new PublicConsumerSystemStateRelease(statefulSystemCounters));
        var resourceId = new Guid("60112233-4455-4677-8899-aabbccddeeff");
        var resourceCodec = new ResourceStateCodec(resourceFactory, resourceField.Descriptor);
        Check(resourceFactory.Descriptor.Schema == resourceSchemaReference);
        Check(ReferenceEquals(resourceFactory.Descriptor, resourceFactory.RegistrationBinding.Descriptor));
        var layout = new ComponentLayout(
            new Guid("00112233-4455-4677-8899-aabbccddeeff"),
            Unsafe.SizeOf<SampleComponent>());
        var schema = ComponentSchema<SampleComponent>.FromRegistry(registry, schemaReference, layout);
        var referenceLayout = new ComponentLayout(
            new Guid("40112233-4455-4677-8899-aabbccddeeff"),
            Unsafe.SizeOf<ReferenceComponent>());
        var referenceSchema = ComponentSchema<ReferenceComponent>.FromRegistry(
            registry,
            referenceSchemaReference,
            referenceLayout);
        var firstField = schema.GetField(new ComponentFieldPath([ComponentPropertySegment.FieldTag(1)]));
        var secondField = schema.GetField(new ComponentFieldPath([ComponentPropertySegment.FieldTag(2)]));
        var opaqueGuidField = schema.GetField(new ComponentFieldPath([ComponentPropertySegment.FieldTag(3)]));
        var firstPath = MemberPath("First");
        var secondPath = MemberPath("Second");
        var guidPath = MemberPath("OpaqueId");
        var targetPath = MemberPath("Target");
        var first = ComponentFieldBinding<SampleComponent, SampleAuthor>.Scalar<int, FirstAccessor, FirstReader>(
            firstField,
            [firstPath],
            default,
            default);
        var second = ComponentFieldBinding<SampleComponent, SampleAuthor>.Scalar<int, SecondAccessor, SecondReader>(
            secondField,
            [secondPath],
            default,
            default);
        var opaqueGuid = ComponentFieldBinding<SampleComponent, SampleAuthor>.Scalar<Guid, OpaqueGuidAccessor, OpaqueGuidReader>(
            opaqueGuidField,
            [guidPath],
            default,
            default);
        var targetField = referenceSchema.GetField(new ComponentFieldPath([ComponentPropertySegment.FieldTag(1)]));
        var targetReference = ComponentFieldBinding<ReferenceComponent, ReferenceAuthor>.EntityReference<TargetAccessor, TargetReader>(
            targetField,
            [targetPath],
            default,
            default);
        var storage = new ComponentStorageDeclaration(
            layout.LogicalBytes,
            [
                new ComponentStorageLeaf(firstPath, ComponentStorageKind.Value, 0, sizeof(int), 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(secondPath, ComponentStorageKind.Value, sizeof(int), sizeof(int), 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(guidPath, ComponentStorageKind.Value, 2 * sizeof(int), Unsafe.SizeOf<Guid>(), 0, 0, ComponentStoragePurpose.SchemaValue),
            ],
            []);
        var factory = new SampleFactory(schema, storage, first, second, opaqueGuid);
        var referenceStorage = new ComponentStorageDeclaration(
            referenceLayout.LogicalBytes,
            [
                new ComponentStorageLeaf(
                    targetPath,
                    ComponentStorageKind.EntityHandle,
                    0,
                    Unsafe.SizeOf<Entity>(),
                    0,
                    0,
                    ComponentStoragePurpose.SchemaValue),
            ],
            []);
        var referenceFactory = new ReferenceFactory(referenceSchema, referenceStorage, targetReference, targetField);
        var value = new SampleComponent(17, 31, new Guid("ffeeddcc-bbaa-4988-8776-554433221100"));

        Check(first.Descriptor.Field.Tag == 1 && first.StoragePaths[0].Equals(storage.Leaves[0].Path));
        Check(second.Descriptor.Field.Tag == 2 && second.StoragePaths[0].Equals(storage.Leaves[1].Path));
        Check(new FirstAccessor().Get(in value) == 17);
        Check(new SecondAccessor().Get(in value) == 31);
        Check(new FirstAccessor().Get(in value) != new SecondAccessor().Get(in value));
        var firstReplacement = 23;
        var firstAccessor = new FirstAccessor();
        firstAccessor.Set(ref value, in firstReplacement);
        Check(firstAccessor.Get(in value) == 23 && new SecondAccessor().Get(in value) == 31);
        Check(opaqueGuid.Descriptor.Field.Reference == ContentReferenceKind.None);
        Check(opaqueGuid.StoragePaths[0].Equals(storage.Leaves[2].Path));
        Check(new OpaqueGuidAccessor().Get(in value) == value.OpaqueId);
        var guidReplacement = Guid.Empty;
        new OpaqueGuidAccessor().Set(ref value, in guidReplacement);
        Check(new OpaqueGuidAccessor().Get(in value) == Guid.Empty);
        Check(layout.LogicalBytes == 24);
        Check(factory.CapturedBinding.Descriptor.Schema == schemaReference);
        Check(factory.CapturedBinding.Descriptor.Fields.Length == 3);
        Check(registry.TryGetSchema(schemaReference, out var registeredComponent) && ReferenceEquals(registeredComponent, metadata));
        Check(registry.Schemas.Length == 25 + 8 + 4 + 18 + 26 + 11 + 10 + 60 + applicationSchemas.Length);

        var entityTag = new EntityTagSchema(applicationSchemas[1].Schema.SchemaId, applicationSchemas[1].Schema.Major);
        var registrationBuilder = new WorldRegistrationBuilder(registry);
        _ = registrationBuilder.RegisterComponent(schema, factory);
        ExpectException<ArgumentException>(() => registrationBuilder.RegisterComponent(schema, factory));
        _ = registrationBuilder.RegisterComponent(referenceSchema, referenceFactory);
        _ = registrationBuilder.RegisterTag(entityTag);
        _ = registrationBuilder.RegisterResource(
            new SystemResourceDescription(resourceId, 2, allowParallelAccess: true),
            new ResourceFactoryProvider(resourceFactory.RegistrationBinding),
            resourceCodec);
        _ = registrationBuilder.RegisterSystem(
            new SystemDescription(
                new Guid("70112233-4455-4677-8899-aabbccddeeff"),
                -7,
                SimulationStage.Input,
                [],
                [],
                [],
                SystemThreadMode.OwnerThread),
            new PublicConsumerSystemFactory(systemBinding));
        _ = registrationBuilder.RegisterSystem(
            new SystemDescription(
                new Guid("80112233-4455-4677-8899-aabbccddeeff"),
                -8,
                SimulationStage.Input,
                [],
                [],
                [],
                SystemThreadMode.OwnerThread),
            new PublicConsumerSystemStateFactory(statefulSystemBinding));
        _ = registrationBuilder.RegisterAttachment(AttachmentScope.World, attachmentBinding);
        _ = registrationBuilder.RegisterAttachment(AttachmentScope.Entity, attachmentBinding);
        var registration = registrationBuilder.Build();
        Check(registration.GetComponentLayout(schemaReference).LogicalBytes == layout.LogicalBytes);
        Check(registration.GetComponentBinding(schemaReference).FactoryId == factory.CapturedBinding.Descriptor.FactoryId);
        Check(registration.Tags.Length == 1
            && registration.Tags[0].SchemaId == entityTag.SchemaId
            && registration.Tags[0].SchemaVersion == entityTag.SchemaVersion);
        Check(factory.BindingReadCount == 1);
        Check(registration.GetResourceBinding(resourceId).PartitionCount == 2);
        ExpectException<InvalidOperationException>(() => registrationBuilder.RegisterTag(entityTag));

        using var admissionDomain = new WorldAdmissionDomain(16 * 1024 * 1024);
        var firstWorldGuid = new Guid("00112233-4455-4677-8899-aabbccddeeff");
        var firstLineageId = new Guid("10112233-4455-4677-8899-aabbccddeeff");

        using (var world = World.Create(new WorldOptions(registration) {
            AdmissionDomain = admissionDomain,
            Enable2DFrame = false,
            InitialReplayLineageId = firstLineageId,
            WorldGuid = firstWorldGuid,
        }))
        {
            Check(world.WorldGuid == firstWorldGuid);
            Check(world.ReplayLineageId == firstLineageId);
            Check(world.CurrentFrame2 is null);
            Check(world.CurrentFrame3.OwnerId != 0 && world.CurrentFrame3.Revision == 1);
            Check(world.CurrentFrame3.Minimum == new TerraFX.Numerics.Vector3(-4096, -4096, -4096));
            Check(world.CurrentFrame3.Maximum == new TerraFX.Numerics.Vector3(4096, 4096, 4096));
            _ = world.Diagnostics;
            Check(resourceCodec.ValidationCount == 1);
            Check(resourceCodec.ValidatedValues.AsSpan().SequenceEqual([2UL, 3UL]));
            Check(statefulSystemCounters.CreateCount == 1);
            Check(statefulSystemCounters.DefaultsCount == 1);
            Check(statefulSystemCounters.ValidationCount == 1);
            Check(statefulSystemCounters.ValidatedCounter == 73);
            var worldAttachment = new PublicConsumerSystemAttachment(11);
            world.SetAttachment(worldAttachment, AttachmentOwnership.Transferred);
            Check(world.TryGetAttachment<PublicConsumerSystemAttachment>(out var storedWorldAttachment)
                && ReferenceEquals(storedWorldAttachment, worldAttachment));

            var sampleValue = new SampleComponent(17, 31, new Guid("ffeeddcc-bbaa-4988-8776-554433221100"));
            var entity = world.CreateEntity(new EntityDescription(
                components: [ComponentInitialization.Value(in sampleValue)]));
            Check(world.Has<SampleComponent>(entity));
            Check(world.TryGet<SampleComponent>(entity, out var storedValue));
            Check(storedValue.First == 17 && storedValue.Second == 31 && storedValue.OpaqueId == sampleValue.OpaqueId);

            var query = new EntityQueryDescription(
                [schemaReference.SchemaId],
                [],
                [new SystemResourceAccess(SystemResourceKind.Component, schemaReference.SchemaId, SystemAccessMode.Read)]);
            var queryWitness = new ComponentQueryWitness();
            world.ForEachChunk(query, ref queryWitness);
            Check(queryWitness.EntityCount == 1 && queryWitness.FirstValue == 17);

            world.AddTag(entity, entityTag.SchemaId);
            Check(world.HasTag(entity, entityTag.SchemaId));
            world.RemoveTag(entity, entityTag.SchemaId);
            Check(!world.HasTag(entity, entityTag.SchemaId));

            var defaultEntity = world.CreateEntity(new EntityDescription());
            var entityAttachment = new PublicConsumerSystemAttachment(23);
            world.SetAttachment(defaultEntity, entityAttachment, AttachmentOwnership.Transferred);
            Check(world.TryGetAttachment<PublicConsumerSystemAttachment>(defaultEntity, out var storedEntityAttachment)
                && ReferenceEquals(storedEntityAttachment, entityAttachment));
            world.Add<SampleComponent>(defaultEntity);
            Check(world.TryGet<SampleComponent>(defaultEntity, out var defaultValue));
            Check(defaultValue.First == 0 && defaultValue.Second == 0 && defaultValue.OpaqueId == Guid.Empty);
            world.Remove<SampleComponent>(defaultEntity);
            Check(!world.Has<SampleComponent>(defaultEntity));
            ExpectException<InvalidOperationException>(() => world.Add<ReferenceComponent>(defaultEntity));
            Check(!world.Has<ReferenceComponent>(defaultEntity));
            world.DestroyEntity(defaultEntity, HierarchyDestroyMode.DetachChildrenKeepWorld);
            Check(attachmentProvider.ReleaseCount == 1);

            var replacementValue = new SampleComponent(23, 31, sampleValue.OpaqueId);
            world.Set(entity, in replacementValue);
            Check(world.TryGet<SampleComponent>(entity, out storedValue) && storedValue.First == 23);
            var invalidValue = new SampleComponent(-1, 31, sampleValue.OpaqueId);
            ExpectException<ArgumentException>(() => world.Set(entity, in invalidValue));
            Check(world.TryGet<SampleComponent>(entity, out storedValue) && storedValue.First == 23);
            ExpectException<ArgumentException>(() => world.CreateEntity(new EntityDescription(
                components: [ComponentInitialization.Value(in invalidValue)])));

            var invalidReference = new ReferenceComponent(default);
            ExpectException<ArgumentException>(() => world.CreateEntity(new EntityDescription(
                components: [ComponentInitialization.Value(in invalidReference)])));
            var targetEntity = world.CreateEntity(new EntityDescription());
            var referenceValue = new ReferenceComponent(targetEntity);
            var referenceEntity = world.CreateEntity(new EntityDescription(
                components: [ComponentInitialization.Value(in referenceValue)]));
            Check(world.TryGet<ReferenceComponent>(referenceEntity, out var storedReference));
            Check(storedReference.Target == targetEntity);

            ExpectException<ArgumentException>(() => world.Set(referenceEntity, in invalidReference));
            Check(world.TryGet<ReferenceComponent>(referenceEntity, out storedReference));
            Check(storedReference.Target == targetEntity);

            var targetEntityId = world.GetEntityId(targetEntity);
            ExpectException<InvalidOperationException>(
                () => world.DestroyEntity(targetEntity, HierarchyDestroyMode.DetachChildrenKeepWorld));
            Check(world.TryGetEntity(targetEntityId, out var stillLiveTarget) && stillLiveTarget == targetEntity);

            world.Remove<ReferenceComponent>(referenceEntity);
            world.DestroyEntity(targetEntity, HierarchyDestroyMode.DetachChildrenKeepWorld);
            Check(!world.TryGetEntity(targetEntityId, out _));

            var duplicateWorldGuid = new Guid("20112233-4455-4677-8899-aabbccddeeff");
            ExpectException<ArgumentException>(() => World.Create(new WorldOptions(registration) {
                AdmissionDomain = admissionDomain,
                Enable2DFrame = false,
                InitialReplayLineageId = firstLineageId,
                WorldGuid = duplicateWorldGuid,
            }));

        }

        Check(attachmentProvider.ReleaseCount == 2);
        Check(systemCounters.CreateCount == 1 && systemCounters.ReleaseCount == 1);
        Check(statefulSystemCounters.CreateCount == 1
            && statefulSystemCounters.DefaultsCount == 1
            && statefulSystemCounters.ValidationCount == 1
            && statefulSystemCounters.ReleaseCount == 1);
        var retryWorldGuid = new Guid("20112233-4455-4677-8899-aabbccddeeff");

        using (var retryWorld = World.Create(new WorldOptions(registration) {
            AdmissionDomain = admissionDomain,
            Enable2DFrame = false,
            InitialReplayLineageId = new Guid("30112233-4455-4677-8899-aabbccddeeff"),
            WorldGuid = retryWorldGuid,
        }))
        {
            Check(retryWorld.WorldGuid == retryWorldGuid);
            retryWorld.Dispose();
            ExpectException<ObjectDisposedException>(() => _ = retryWorld.Diagnostics);
        }

        Check(systemCounters.CreateCount == 2 && systemCounters.ReleaseCount == 2);
        Check(statefulSystemCounters.CreateCount == 2
            && statefulSystemCounters.DefaultsCount == 2
            && statefulSystemCounters.ValidationCount == 2
            && statefulSystemCounters.ReleaseCount == 2);
        Check(!admissionDomain.Retirement.IsCompleted);
        admissionDomain.Dispose();
        Check(admissionDomain.Retirement.IsCompleted);

        Console.WriteLine("Handwritten World consumer passed: component/resource bindings, stateless and stateful system lifetimes, transferred World/Entity attachments, system/component/resource defaults and validation, live Entity references, structural transitions, scoped queries, identity admission, diagnostics, and spatial frames; unsafe disabled.");
        return 0;
    }

    private static void Check(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("The handwritten component public-consumer witness failed.");
        }
    }

    private static DataSchema CreateApplicationSchema(
        string name,
        ContentSchemaRole role,
        ImmutableArray<ContentFieldDescriptor> fields)
    {
        var assignment = new ContentSchemaIdentityAssignment(name);
        return new DataSchema(
            new ContentSchemaReference(assignment.SchemaId, 1, 0, ContentSchemaScope.Content),
            assignment,
            role,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            fields);
    }

    private static void ExpectException<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"The public-consumer witness expected {typeof(TException).Name}.");
    }

    private static ContentFieldDescriptor Field(
        uint tag,
        ushort wireKind,
        ushort elementKind = 0,
        ulong minimumCount = 1,
        ulong maximumCount = 1)
        => new(
            tag,
            wireKind,
            elementKind,
            true,
            minimumCount,
            maximumCount,
            [],
            0,
            ContentUnit.None,
            ContentReferenceKind.None,
            ContentAuthoring.None,
            null);

    private static ComponentStoragePath MemberPath(string name)
        => new([ComponentStorageSegment.Member(name)]);

    private struct ComponentQueryWitness : IAction<EntityChunkView>
    {
        internal int EntityCount;

        internal int FirstValue;

        public void Invoke(scoped EntityChunkView argument)
        {
            EntityCount += argument.Entities.Length;

            foreach (var value in argument.Read<SampleComponent>())
            {
                FirstValue += value.First;
            }
        }
    }

    private readonly struct FirstAccessor : IComponentValueAccessor<SampleComponent, int>
    {
        public int Get(in SampleComponent value) => value.First;

        public void Set(ref SampleComponent value, in int fieldValue) => value.SetFirst(fieldValue);
    }

    private readonly struct FirstAttachmentAuthorReader : IComponentValueReader<int, int>
    {
        public int Get(in int value) => value;
    }

    private readonly struct FirstAttachmentReader : IComponentValueReader<PublicConsumerSystemAttachment, int>
    {
        public int Get(in PublicConsumerSystemAttachment value) => value.Value;
    }

    private readonly struct FirstReader : IComponentValueReader<SampleAuthor, int>
    {
        public int Get(in SampleAuthor value) => value.First;
    }

    private readonly struct OpaqueGuidAccessor : IComponentValueAccessor<SampleComponent, Guid>
    {
        public Guid Get(in SampleComponent value) => value.OpaqueId;

        public void Set(ref SampleComponent value, in Guid fieldValue) => value.SetOpaqueId(fieldValue);
    }

    private readonly struct OpaqueGuidReader : IComponentValueReader<SampleAuthor, Guid>
    {
        public Guid Get(in SampleAuthor value) => value.OpaqueId;
    }

    private sealed class PublicConsumerSystem : IWorldSystem
    {
        public void Execute(WorldSystemContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
        }
    }

    private sealed class PublicConsumerSystemAttachment
    {
        internal PublicConsumerSystemAttachment(int value)
        {
            Value = value;
        }

        internal int Value { get; }
    }

    private sealed class PublicConsumerSystemAttachmentProvider : IWorldAttachmentStateProvider<PublicConsumerSystemAttachment, int>
    {
        internal int ReleaseCount { get; private set; }

        public nint Count(
            PublicConsumerSystemAttachment value,
            WorldAttachmentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(value);
            ArgumentNullException.ThrowIfNull(binding);
            cancellationToken.ThrowIfCancellationRequested();
            return sizeof(int);
        }

        public WorldAttachmentReadResult<int> Read(
            scoped ReadOnlySpan<byte> source,
            WorldAttachmentReadBinding<int> binding,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(binding);
            cancellationToken.ThrowIfCancellationRequested();

            if (source.Length != sizeof(int))
            {
                return binding.Reject(
                    WorldAttachmentReadFailureKind.MalformedRecord,
                    0,
                    null,
                    "The public-consumer attachment record must contain exactly one Int32.");
            }

            var value = BinaryPrimitives.ReadInt32LittleEndian(source);
            return binding.Complete(in value, sizeof(int));
        }

        public void Release(PublicConsumerSystemAttachment value, WorldAttachmentReleaseBinding binding)
        {
            ArgumentNullException.ThrowIfNull(value);
            ArgumentNullException.ThrowIfNull(binding);
            ReleaseCount++;
        }

        public PublicConsumerSystemAttachment Restore(
            in int author,
            WorldAttachmentRestoreBinding binding,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(binding);
            cancellationToken.ThrowIfCancellationRequested();
            return new PublicConsumerSystemAttachment(author);
        }

        public ImmutableArray<Diagnostic> Validate(
            PublicConsumerSystemAttachment value,
            WorldAttachmentValidationBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return binding.ValidateFields(value);
        }

        public nint Write(
            PublicConsumerSystemAttachment value,
            scoped Span<byte> destination,
            WorldAttachmentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(value);
            ArgumentNullException.ThrowIfNull(binding);
            cancellationToken.ThrowIfCancellationRequested();

            if (destination.Length != sizeof(int))
            {
                throw new ArgumentException("The destination must match the exact attachment wire size.", nameof(destination));
            }

            BinaryPrimitives.WriteInt32LittleEndian(destination, value.Value);
            return sizeof(int);
        }
    }

    private readonly struct PublicConsumerSystemCreate : IWorldSystemCreate<PublicConsumerSystem>
    {
        private readonly SystemLifecycleCounters _counters;

        internal PublicConsumerSystemCreate(SystemLifecycleCounters counters)
        {
            _counters = counters;
        }

        public PublicConsumerSystem Invoke()
        {
            _counters.CreateCount++;
            return new PublicConsumerSystem();
        }
    }

    private sealed class PublicConsumerSystemFactory : IWorldSystemFactory
    {
        private readonly WorldSystemFactoryBinding _binding;

        internal PublicConsumerSystemFactory(WorldSystemFactoryBinding binding)
        {
            _binding = binding;
        }

        public WorldSystemFactoryBinding Binding => _binding;
    }

    private readonly struct PublicConsumerSystemRelease : IWorldSystemRelease<PublicConsumerSystem>
    {
        private readonly SystemLifecycleCounters _counters;

        internal PublicConsumerSystemRelease(SystemLifecycleCounters counters)
        {
            _counters = counters;
        }

        public void Invoke(PublicConsumerSystem system)
        {
            ArgumentNullException.ThrowIfNull(system);
            _counters.ReleaseCount++;
        }
    }

    private sealed class PublicConsumerSystemState : IWorldSystem
    {
        internal long Counter { get; set; }

        public void Execute(WorldSystemContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
        }
    }

    private readonly record struct PublicConsumerSystemStateAuthor(long Counter);

    private readonly struct PublicConsumerSystemStateAuthorReader :
        IWorldSystemValueReader<PublicConsumerSystemStateAuthor, long>
    {
        public long Invoke(in PublicConsumerSystemStateAuthor source) => source.Counter;
    }

    private sealed class PublicConsumerSystemStateCodec :
        IWorldSystemStateCodec<PublicConsumerSystemState, PublicConsumerSystemStateAuthor>
    {
        private readonly PublicConsumerSystemStateCounters _counters;

        internal PublicConsumerSystemStateCodec(PublicConsumerSystemStateCounters counters)
        {
            _counters = counters;
        }

        public nint Count(
            PublicConsumerSystemState system,
            WorldSystemCaptureBinding binding,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public WorldSystemReadResult<PublicConsumerSystemStateAuthor> Read(
            scoped ReadOnlySpan<byte> source,
            WorldSystemReadBinding<PublicConsumerSystemStateAuthor> binding,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public void Restore(
            PublicConsumerSystemState system,
            in PublicConsumerSystemStateAuthor author,
            WorldStateRestoreBinding binding,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ImmutableArray<Diagnostic> Validate(
            PublicConsumerSystemState system,
            WorldSystemValidationBinding binding,
            CancellationToken cancellationToken)
        {
            _counters.ValidationCount++;
            _counters.ValidatedCounter = system.Counter;
            _ = binding.ValidateFields(system);
            return [];
        }

        public nint Write(
            PublicConsumerSystemState system,
            scoped Span<byte> destination,
            WorldSystemCaptureBinding binding,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class PublicConsumerSystemStateCounters
    {
        internal int CreateCount;

        internal int DefaultsCount;

        internal int ReleaseCount;

        internal long ValidatedCounter;

        internal int ValidationCount;
    }

    private readonly struct PublicConsumerSystemStateCreate : IWorldSystemCreate<PublicConsumerSystemState>
    {
        private readonly PublicConsumerSystemStateCounters _counters;

        internal PublicConsumerSystemStateCreate(PublicConsumerSystemStateCounters counters)
        {
            _counters = counters;
        }

        public PublicConsumerSystemState Invoke()
        {
            _counters.CreateCount++;
            return new PublicConsumerSystemState();
        }
    }

    private readonly struct PublicConsumerSystemStateDefaults : IWorldSystemDefaults<PublicConsumerSystemState>
    {
        private readonly PublicConsumerSystemStateCounters _counters;

        internal PublicConsumerSystemStateDefaults(PublicConsumerSystemStateCounters counters)
        {
            _counters = counters;
        }

        public void Invoke(PublicConsumerSystemState system)
        {
            _counters.DefaultsCount++;
            system.Counter = 73;
        }
    }

    private sealed class PublicConsumerSystemStateFactory : IWorldSystemFactory
    {
        private readonly WorldSystemFactoryBinding _binding;

        internal PublicConsumerSystemStateFactory(WorldSystemFactoryBinding binding)
        {
            _binding = binding;
        }

        public WorldSystemFactoryBinding Binding => _binding;
    }

    private readonly struct PublicConsumerSystemStateRelease : IWorldSystemRelease<PublicConsumerSystemState>
    {
        private readonly PublicConsumerSystemStateCounters _counters;

        internal PublicConsumerSystemStateRelease(PublicConsumerSystemStateCounters counters)
        {
            _counters = counters;
        }

        public void Invoke(PublicConsumerSystemState system)
        {
            ArgumentNullException.ThrowIfNull(system);
            _counters.ReleaseCount++;
        }
    }

    private readonly struct PublicConsumerSystemStateRuntimeReader :
        IWorldSystemValueReader<PublicConsumerSystemState, long>
    {
        public long Invoke(in PublicConsumerSystemState source) => source.Counter;
    }

    private readonly record struct ReferenceAuthor(Guid TargetId);

    private struct ReferenceComponent
    {
        private Entity _target;

        internal ReferenceComponent(Entity target)
        {
            _target = target;
        }

        internal readonly Entity Target => _target;

        internal void SetTarget(Entity target) => _target = target;
    }

    private sealed class ReferenceDefaultProvider : IComponentDefaultProvider<ReferenceComponent>
    {
        public ReferenceComponent Create(ComponentDefaultBinding binding) => new(default);
    }

    private sealed class ReferenceFactory : IComponentFactory<ReferenceComponent>
    {
        private readonly ComponentFactoryBinding<ReferenceComponent> _binding;

        internal ReferenceFactory(
            ComponentSchema<ReferenceComponent> schema,
            ComponentStorageDeclaration storage,
            ComponentFieldBinding<ReferenceComponent, ReferenceAuthor> targetReference,
            ComponentFieldDescriptor targetField)
        {
            var codec = new ReferenceStateCodec(schema.Schema, targetField);
            _binding = ComponentFactoryBinding<ReferenceComponent>.Create(
                new Guid("50112233-4455-4677-8899-aabbccddeeff"),
                1,
                schema,
                storage,
                ComponentTransientRestore.None,
                [],
                [targetReference],
                new ReferenceDefaultProvider(),
                new ReferenceRestoreProvider(targetField),
                codec);
        }

        public ComponentFactoryBinding<ReferenceComponent> Binding => _binding;
    }

    private sealed class ReferenceRestoreProvider : IComponentRestoreProvider<ReferenceComponent, ReferenceAuthor>
    {
        private readonly ComponentFieldDescriptor _targetField;

        internal ReferenceRestoreProvider(ComponentFieldDescriptor targetField)
        {
            _targetField = targetField;
        }

        public ReferenceComponent RebuildDerived(WorldStateRestoreBinding binding, CancellationToken cancellationToken)
            => default;

        public ReferenceComponent Restore(
            in ReferenceAuthor author,
            WorldStateRestoreBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ReferenceComponent(binding.ResolveEntity(_targetField, author.TargetId));
        }
    }

    private sealed class ReferenceStateCodec : IComponentStateCodec<ReferenceComponent, ReferenceAuthor>
    {
        private readonly ContentSchemaReference _schema;
        private readonly ComponentFieldDescriptor _targetField;

        internal ReferenceStateCodec(ContentSchemaReference schema, ComponentFieldDescriptor targetField)
        {
            _schema = schema;
            _targetField = targetField;
        }

        private static EbinFieldSource[] CreateFields()
            => [new EbinFieldSource(1, 11, 1, 1, new EbinPayloadSlice(0, Unsafe.SizeOf<Guid>()))];

        private static void WriteTargetId(Guid targetId, scoped Span<byte> payload)
        {
            if ((EbinPayloadWriter.Write(targetId, payload, out var bytesWritten, out _) != EbinWriteStatus.Complete)
                || (bytesWritten != Unsafe.SizeOf<Guid>()))
            {
                throw new InvalidOperationException("The selected Entity reference could not be encoded.");
            }
        }

        public nint Count(in ReferenceComponent value, ComponentCaptureBinding binding, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Span<byte> payload = stackalloc byte[Unsafe.SizeOf<Guid>()];
            WriteTargetId(binding.GetEntityId(_targetField, value.Target), payload);
            var source = new EbinRecordWriteView(
                _schema.SchemaId,
                _schema.Major,
                _schema.Minor,
                CreateFields(),
                payload);

            if (EbinStructuralWriter.Measure(source, EbinReadLimits.Foundation, out var recordBytes, out _) != EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException("The selected Entity-reference record could not be measured.");
            }

            return recordBytes;
        }

        public ComponentReadResult<ReferenceAuthor> Read(
            scoped ReadOnlySpan<byte> source,
            ComponentReadBinding<ReferenceAuthor> binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var action = new ReadAction(_schema, _targetField, binding);
            var status = EbinStructuralReader.Read(
                source,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref action,
                out _,
                out var diagnostic);

            if (status != EbinReadStatus.Complete)
            {
                return binding.Reject(status, diagnostic);
            }

            return action.Result
                ?? throw new InvalidOperationException("The complete Entity-reference read did not issue an author result.");
        }

        public ImmutableArray<Diagnostic> Validate(
            in ReferenceComponent value,
            ComponentValidationBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return binding.ValidateFields(in value);
        }

        public nint Write(
            in ReferenceComponent value,
            scoped Span<byte> destination,
            ComponentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Span<byte> payload = stackalloc byte[Unsafe.SizeOf<Guid>()];
            Span<byte> candidate = stackalloc byte[256];
            WriteTargetId(binding.GetEntityId(_targetField, value.Target), payload);
            var source = new EbinRecordWriteView(
                _schema.SchemaId,
                _schema.Major,
                _schema.Minor,
                CreateFields(),
                payload);
            var status = EbinStructuralWriter.Write(
                source,
                EbinReadLimits.Foundation,
                candidate,
                destination,
                out var bytesWritten,
                out var diagnostic);

            if (status == EbinWriteStatus.DestinationTooSmall)
            {
                throw new ArgumentException($"The Entity-reference destination is too small: {diagnostic?.Category}.", nameof(destination));
            }

            if (status != EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException($"The Entity-reference record could not be written: {diagnostic?.Category}.");
            }

            return bytesWritten;
        }

        private struct ReadAction : IAction<EbinRecordReadView>
        {
            private readonly ComponentReadBinding<ReferenceAuthor> _binding;
            private readonly ContentSchemaReference _schema;
            private readonly ComponentFieldDescriptor _targetField;
            private ComponentReadResult<ReferenceAuthor>? _result;

            internal ReadAction(
                ContentSchemaReference schema,
                ComponentFieldDescriptor targetField,
                ComponentReadBinding<ReferenceAuthor> binding)
            {
                _schema = schema;
                _targetField = targetField;
                _binding = binding;
                _result = null;
            }

            internal readonly ComponentReadResult<ReferenceAuthor>? Result => _result;

            public void Invoke(scoped EbinRecordReadView argument)
            {
                var header = argument.Header;

                if ((header.SchemaId != _schema.SchemaId)
                    || (header.Major != _schema.Major)
                    || (header.Minor != _schema.Minor))
                {
                    _result = _binding.Reject(
                        ComponentReadFailureKind.SchemaMismatch,
                        0,
                        _targetField.Path,
                        "The Entity-reference record header does not match the selected schema tuple.");
                    return;
                }

                if ((argument.FieldCount != 1)
                    || !argument.TryGetField(1, out var target)
                    || (target.Kind != 11)
                    || !target.IsRequired
                    || (target.Count != 1)
                    || (target.Payload.Length != Unsafe.SizeOf<Guid>()))
                {
                    _result = _binding.Reject(
                        ComponentReadFailureKind.InvalidField,
                        0,
                        _targetField.Path,
                        "The Entity-reference record field does not match the selected schema.");
                    return;
                }

                var author = new ReferenceAuthor(new Guid(target.Payload, bigEndian: true));
                _result = _binding.Complete(in author, argument.EncodedBytes.Length);
            }
        }
    }

    private readonly struct ResourceAuthorArrayReader : IComponentArrayReader<ImmutableArray<ulong>, ulong>
    {
        public int GetCount(in ImmutableArray<ulong> value) => value.Length;

        public ulong GetElement(in ImmutableArray<ulong> value, int index) => value[index];
    }

    private readonly struct ResourceDefaultProvider : IWorldResourceDefaultProvider<ulong>
    {
        public ulong Create(int partitionIndex, WorldResourceDefaultBinding binding)
            => checked((ulong)partitionIndex + (ulong)binding.PartitionCount);
    }

    private sealed class ResourceFactoryProvider : IWorldResourceFactory<ulong>
    {
        private readonly WorldResourceFactoryBinding<ulong> _binding;

        internal ResourceFactoryProvider(WorldResourceFactoryBinding<ulong> binding)
        {
            _binding = binding;
        }

        public WorldResourceFactoryBinding<ulong> Binding => _binding;
    }

    private readonly struct ResourcePartitionReader : IComponentValueReader<ulong, ulong>
    {
        public ulong Get(in ulong value) => value;
    }

    private sealed class ResourceStateCodec : IWorldResourceStateCodec<ulong, ImmutableArray<ulong>>
    {
        private WorldResourceCodecBinding<ulong>? _binding;
        private readonly WorldResourceFactoryBinding<ulong, ImmutableArray<ulong>> _factory;
        private readonly WorldResourceFieldDescriptor _field;
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
            => _binding ??= WorldResourceCodecBinding<ulong>.Create(_factory, this);

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
            var author = ImmutableArray.Create<ulong>(0, 0);
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
            var validatedValues = ImmutableArray.CreateBuilder<ulong>(value.PartitionCount);

            for (var index = 0; index < value.PartitionCount; index++)
            {
                validatedValues.Add(value.ReadPartition(index));
            }

            _validatedValues = validatedValues.MoveToImmutable();
            _validationCount++;
            return binding.ValidateFields(value);
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

    private readonly record struct SampleAuthor(int First, int Second, Guid OpaqueId);

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct SampleComponent
    {
        private int _first;

        private int _second;

        private Guid _opaqueId;

        internal SampleComponent(int first, int second, Guid opaqueId)
        {
            _first = first;
            _second = second;
            _opaqueId = opaqueId;
        }

        internal readonly int First => _first;

        internal readonly Guid OpaqueId => _opaqueId;

        internal readonly int Second => _second;

        internal void SetFirst(int value) => _first = value;

        internal void SetOpaqueId(Guid value) => _opaqueId = value;

        internal void SetSecond(int value) => _second = value;
    }

    private sealed class SampleDefaultProvider : IComponentDefaultProvider<SampleComponent>
    {
        public SampleComponent Create(ComponentDefaultBinding binding) => new(0, 0, Guid.Empty);
    }

    private sealed class SampleFactory : IComponentFactory<SampleComponent>
    {
        private readonly ComponentFactoryBinding<SampleComponent> _binding;
        private int _bindingReadCount;

        internal SampleFactory(
            ComponentSchema<SampleComponent> schema,
            ComponentStorageDeclaration storage,
            ComponentFieldBinding<SampleComponent, SampleAuthor> first,
            ComponentFieldBinding<SampleComponent, SampleAuthor> second,
            ComponentFieldBinding<SampleComponent, SampleAuthor> opaqueGuid)
        {
            var codec = new SampleStateCodec(schema.Schema);
            _binding = ComponentFactoryBinding<SampleComponent>.Create(
                new Guid("30112233-4455-4677-8899-aabbccddeeff"),
                1,
                schema,
                storage,
                ComponentTransientRestore.None,
                [],
                [first, second, opaqueGuid],
                new SampleDefaultProvider(),
                new SampleRestoreProvider(),
                codec);
        }

        public ComponentFactoryBinding<SampleComponent> Binding
        {
            get
            {
                _bindingReadCount++;
                return _binding;
            }
        }

        internal int BindingReadCount => _bindingReadCount;

        internal ComponentFactoryBinding<SampleComponent> CapturedBinding => _binding;

    }

    private sealed class SampleRestoreProvider : IComponentRestoreProvider<SampleComponent, SampleAuthor>
    {
        public SampleComponent RebuildDerived(WorldStateRestoreBinding binding, CancellationToken cancellationToken)
            => default;

        public SampleComponent Restore(
            in SampleAuthor author,
            WorldStateRestoreBinding binding,
            CancellationToken cancellationToken)
            => new(author.First, author.Second, author.OpaqueId);
    }

    private sealed class SampleStateCodec : IComponentStateCodec<SampleComponent, SampleAuthor>
    {
        private readonly ContentSchemaReference _schema;

        internal SampleStateCodec(ContentSchemaReference schema)
        {
            _schema = schema;
        }

        private static EbinFieldSource[] CreateFields()
            => [
                new EbinFieldSource(1, 7, 1, 1, new EbinPayloadSlice(0, sizeof(int))),
                new EbinFieldSource(2, 7, 1, 1, new EbinPayloadSlice(sizeof(int), sizeof(int))),
                new EbinFieldSource(3, 11, 1, 1, new EbinPayloadSlice(2 * sizeof(int), Unsafe.SizeOf<Guid>())),
            ];

        private static void WritePayload(in SampleComponent value, scoped Span<byte> payload)
        {
            if ((EbinPayloadWriter.Write(value.First, payload, out var firstBytes, out _) != EbinWriteStatus.Complete)
                || (firstBytes != sizeof(int))
                || (EbinPayloadWriter.Write(value.Second, payload[sizeof(int)..], out var secondBytes, out _) != EbinWriteStatus.Complete)
                || (secondBytes != sizeof(int))
                || (EbinPayloadWriter.Write(value.OpaqueId, payload[(2 * sizeof(int))..], out var guidBytes, out _) != EbinWriteStatus.Complete)
                || (guidBytes != Unsafe.SizeOf<Guid>()))
            {
                throw new InvalidOperationException("The selected component payload could not be encoded.");
            }
        }

        public nint Count(in SampleComponent value, ComponentCaptureBinding binding, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Span<byte> payload = stackalloc byte[(2 * sizeof(int)) + 16];
            WritePayload(in value, payload);
            var source = new EbinRecordWriteView(_schema.SchemaId, _schema.Major, _schema.Minor, CreateFields(), payload);

            if (EbinStructuralWriter.Measure(source, EbinReadLimits.Foundation, out var recordBytes, out _) != EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException("The selected component record could not be measured.");
            }

            return recordBytes;
        }

        public ComponentReadResult<SampleAuthor> Read(
            scoped ReadOnlySpan<byte> source,
            ComponentReadBinding<SampleAuthor> binding,
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

            if (status != EbinReadStatus.Complete)
            {
                return binding.Reject(status, diagnostic);
            }

            return action.Result
                ?? throw new InvalidOperationException("The complete structural read did not issue a component result.");
        }

        public ImmutableArray<Diagnostic> Validate(
            in SampleComponent value,
            ComponentValidationBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (value.First < 0)
            {
                return [
                    new Diagnostic(
                        "PUBLIC_COMPONENT_NEGATIVE",
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
                        "Component.Validate",
                        null,
                        "The First field cannot be negative.",
                        []),
                ];
            }

            return binding.ValidateFields(in value);
        }

        public nint Write(
            in SampleComponent value,
            scoped Span<byte> destination,
            ComponentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Span<byte> payload = stackalloc byte[(2 * sizeof(int)) + 16];
            Span<byte> candidate = stackalloc byte[256];
            WritePayload(in value, payload);
            var source = new EbinRecordWriteView(_schema.SchemaId, _schema.Major, _schema.Minor, CreateFields(), payload);
            var status = EbinStructuralWriter.Write(
                source,
                EbinReadLimits.Foundation,
                candidate,
                destination,
                out var bytesWritten,
                out var diagnostic);

            if (status == EbinWriteStatus.DestinationTooSmall)
            {
                throw new ArgumentException($"The component destination is too small: {diagnostic?.Category}.", nameof(destination));
            }

            if (status != EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException($"The component record could not be written: {diagnostic?.Category}.");
            }

            return bytesWritten;
        }

        private struct ReadAction : IAction<EbinRecordReadView>
        {
            private readonly ComponentReadBinding<SampleAuthor> _binding;
            private readonly ContentSchemaReference _schema;
            private ComponentReadResult<SampleAuthor>? _result;

            internal ReadAction(ContentSchemaReference schema, ComponentReadBinding<SampleAuthor> binding)
            {
                _schema = schema;
                _binding = binding;
                _result = null;
            }

            internal readonly ComponentReadResult<SampleAuthor>? Result => _result;

            public void Invoke(scoped EbinRecordReadView argument)
            {
                var header = argument.Header;

                if ((header.SchemaId != _schema.SchemaId)
                    || (header.Major != _schema.Major)
                    || (header.Minor != _schema.Minor))
                {
                    _result = _binding.Reject(ComponentReadFailureKind.SchemaMismatch, 16, null, "The component record header does not match the selected schema tuple.");
                    return;
                }

                if ((argument.FieldCount != 3)
                    || !argument.TryGetField(1, out var first)
                    || !argument.TryGetField(2, out var second)
                    || !argument.TryGetField(3, out var opaqueGuid)
                    || (first.Kind != 7)
                    || (second.Kind != 7)
                    || (opaqueGuid.Kind != 11)
                    || (first.Count != 1)
                    || (second.Count != 1)
                    || (opaqueGuid.Count != 1)
                    || (first.Payload.Length != sizeof(int))
                    || (second.Payload.Length != sizeof(int))
                    || (opaqueGuid.Payload.Length != Unsafe.SizeOf<Guid>()))
                {
                    _result = _binding.Reject(ComponentReadFailureKind.InvalidField, 64, null, "The component record fields do not match the selected schema.");
                    return;
                }

                var author = new SampleAuthor(
                    BinaryPrimitives.ReadInt32LittleEndian(first.Payload),
                    BinaryPrimitives.ReadInt32LittleEndian(second.Payload),
                    new Guid(opaqueGuid.Payload, bigEndian: true));
                _result = _binding.Complete(in author, argument.EncodedBytes.Length);
            }
        }
    }

    private readonly struct SecondAccessor : IComponentValueAccessor<SampleComponent, int>
    {
        public int Get(in SampleComponent value) => value.Second;

        public void Set(ref SampleComponent value, in int fieldValue) => value.SetSecond(fieldValue);
    }

    private readonly struct SecondReader : IComponentValueReader<SampleAuthor, int>
    {
        public int Get(in SampleAuthor value) => value.Second;
    }

    private sealed class SystemLifecycleCounters
    {
        internal int CreateCount;

        internal int ReleaseCount;
    }

    private readonly struct TargetAccessor : IComponentValueAccessor<ReferenceComponent, Entity>
    {
        public Entity Get(in ReferenceComponent value) => value.Target;

        public void Set(ref ReferenceComponent value, in Entity fieldValue) => value.SetTarget(fieldValue);
    }

    private readonly struct TargetReader : IComponentValueReader<ReferenceAuthor, Guid>
    {
        public Guid Get(in ReferenceAuthor value) => value.TargetId;
    }
}
