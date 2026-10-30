// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using TerraFX;
using TerraFX.Entities;
using TerraFX.Generators;

namespace TerraFX.Runtime.GeneratedComponentConsumer;

internal static partial class Program
{
    [SuppressMessage("Globalization", "CA1303", Justification = "This is a machine-checked public-consumer completion marker.")]
    public static int Main()
    {
        var assignment = new ContentSchemaIdentityAssignment("application/generated-component-consumer/v1");
        var schemaReference = new ContentSchemaReference(assignment.SchemaId, 1, 0, ContentSchemaScope.Content);
        var metadata = new DataSchema(
            schemaReference,
            assignment,
            ContentSchemaRole.Component,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.Persistent,
            ContentTransientRestore.NotApplicable,
            [
                new ContentFieldDescriptor(
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
                    null),
            ]);
        var registry = new CompiledContentSchemaRegistry([metadata]);
        var layout = new ComponentLayout(
            new Guid("40112233-4455-4677-8899-aabbccddeeff"),
            Unsafe.SizeOf<GeneratedComponent>());
        var schema = ComponentSchema<GeneratedComponent>.FromRegistry(registry, schemaReference, layout);
        var field = schema.GetField(GeneratedComponent.Field1FieldPath);
        var fieldBinding = ComponentFieldBinding<GeneratedComponent, GeneratedAuthor>.Scalar<
            int,
            GeneratedComponent.Field1Accessor,
            GeneratedComponent.Field1Reader>(
                field,
                [GeneratedComponent.Field1StoragePath],
                default,
                default);
        var storage = new ComponentStorageDeclaration(
            layout.LogicalBytes,
            [
                new ComponentStorageLeaf(
                    GeneratedComponent.Field1StoragePath,
                    ComponentStorageKind.Value,
                    0,
                    sizeof(int),
                    0,
                    0,
                    ComponentStoragePurpose.SchemaValue),
            ],
            []);
        var factoryBinding = ComponentFactoryBinding<GeneratedComponent>.Create(
            new Guid("50112233-4455-4677-8899-aabbccddeeff"),
            1,
            schema,
            storage,
            ComponentTransientRestore.None,
            [],
            [fieldBinding],
            new GeneratedDefaultProvider(),
            new GeneratedRestoreProvider(),
            new GeneratedStateCodec(schemaReference));
        var registrationBuilder = new WorldRegistrationBuilder(registry);
        _ = registrationBuilder.RegisterComponent(schema, new GeneratedComponentFactory(factoryBinding));
        var registration = registrationBuilder.Build();

        using var world = World.Create(new WorldOptions(registration) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var initial = new GeneratedComponent(17);
        var entity = world.CreateEntity(new EntityDescription(
            components: [ComponentInitialization.Value(in initial)]));
        Check(world.TryGet<GeneratedComponent>(entity, out var value));
        Check(new GeneratedComponent.Field1Accessor().Get(in value) == 17);

        var edited = value;
        var replacement = 42;
        new GeneratedComponent.Field1Accessor().Set(ref edited, in replacement);
        world.Set(entity, in edited);
        Check(world.TryGet<GeneratedComponent>(entity, out value));
        Check(new GeneratedComponent.Field1Accessor().Get(in value) == replacement);

        var query = new EntityQueryDescription(
            [schemaReference.SchemaId],
            [],
            [new SystemResourceAccess(
                SystemResourceKind.Component,
                schemaReference.SchemaId,
                SystemAccessMode.Read)]);
        var queryProbe = new GeneratedQueryProbe();
        world.ForEachChunk(query, ref queryProbe);
        Check(queryProbe.EntityCount == 1 && queryProbe.FirstValue == replacement);

        var defaultEntity = world.CreateEntity(new EntityDescription());
        world.Add<GeneratedComponent>(defaultEntity);
        Check(world.TryGet<GeneratedComponent>(defaultEntity, out value));
        Check(new GeneratedComponent.Field1Accessor().Get(in value) == 0);

        var tick = world.Step();
        Check(tick.CompletedTick == 0 && tick.Frame3.Revision == world.CurrentFrame3.Revision);
        Console.WriteLine("Generated component consumer passed: explicit schema-tag accessors, field paths, defaults, World mutation, and scoped queries.");
        return 0;
    }

    private static void Check(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("The generated component public-consumer witness failed.");
        }
    }

    private static EbinFieldSource[] CreateFields()
        => [new EbinFieldSource(1, 7, 1, 1, new EbinPayloadSlice(0, sizeof(int)))];

    private readonly record struct GeneratedAuthor(int Value);

    [GenerateComponentBindings(typeof(GeneratedAuthor))]
    private partial struct GeneratedComponent
    {
        [GenerateComponentFieldAccessor(1, nameof(GeneratedAuthor.Value), "Value")]
        private int _value;

        internal GeneratedComponent(int value)
        {
            _value = value;
        }
    }

    private sealed class GeneratedComponentFactory : IComponentFactory<GeneratedComponent>
    {
        private readonly ComponentFactoryBinding<GeneratedComponent> _binding;

        internal GeneratedComponentFactory(ComponentFactoryBinding<GeneratedComponent> binding)
        {
            _binding = binding;
        }

        public ComponentFactoryBinding<GeneratedComponent> Binding => _binding;
    }

    private sealed class GeneratedDefaultProvider : IComponentDefaultProvider<GeneratedComponent>
    {
        public GeneratedComponent Create(ComponentDefaultBinding binding) => new(0);
    }

    private struct GeneratedQueryProbe : IAction<EntityChunkView>
    {
        internal int EntityCount;

        internal int FirstValue;

        public void Invoke(scoped EntityChunkView chunk)
        {
            var entities = chunk.Entities;
            var values = chunk.Read<GeneratedComponent>();
            EntityCount += entities.Length;

            for (var index = 0; index < values.Length; index++)
            {
                FirstValue += new GeneratedComponent.Field1Accessor().Get(in values[index]);
            }
        }
    }

    private sealed class GeneratedRestoreProvider : IComponentRestoreProvider<GeneratedComponent, GeneratedAuthor>
    {
        public GeneratedComponent RebuildDerived(WorldStateRestoreBinding binding, CancellationToken cancellationToken)
            => default;

        public GeneratedComponent Restore(
            in GeneratedAuthor author,
            WorldStateRestoreBinding binding,
            CancellationToken cancellationToken)
            => new(author.Value);
    }

    private sealed class GeneratedStateCodec : IComponentStateCodec<GeneratedComponent, GeneratedAuthor>
    {
        private readonly ContentSchemaReference _schema;

        internal GeneratedStateCodec(ContentSchemaReference schema)
        {
            _schema = schema;
        }

        private static void WritePayload(int value, scoped Span<byte> payload)
        {
            if ((EbinPayloadWriter.Write(value, payload, out var bytesWritten, out _) != EbinWriteStatus.Complete)
                || (bytesWritten != sizeof(int)))
            {
                throw new InvalidOperationException("The generated component payload could not be written.");
            }
        }

        public nint Count(
            in GeneratedComponent value,
            ComponentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Span<byte> payload = stackalloc byte[sizeof(int)];
            WritePayload(new GeneratedComponent.Field1Accessor().Get(in value), payload);
            var source = new EbinRecordWriteView(
                _schema.SchemaId,
                _schema.Major,
                _schema.Minor,
                CreateFields(),
                payload);

            if (EbinStructuralWriter.Measure(source, EbinReadLimits.Foundation, out var recordBytes, out _) != EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException("The generated component record could not be measured.");
            }

            return recordBytes;
        }

        public ComponentReadResult<GeneratedAuthor> Read(
            scoped ReadOnlySpan<byte> source,
            ComponentReadBinding<GeneratedAuthor> binding,
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
                ?? throw new InvalidOperationException("The generated component read did not issue an author result.");
        }

        public ImmutableArray<Diagnostic> Validate(
            in GeneratedComponent value,
            ComponentValidationBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return binding.ValidateFields(in value);
        }

        public nint Write(
            in GeneratedComponent value,
            scoped Span<byte> destination,
            ComponentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Span<byte> payload = stackalloc byte[sizeof(int)];
            Span<byte> candidate = stackalloc byte[128];
            WritePayload(new GeneratedComponent.Field1Accessor().Get(in value), payload);
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
                throw new ArgumentException($"The generated component destination is too small: {diagnostic?.Category}.", nameof(destination));
            }

            if (status != EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException($"The generated component record could not be written: {diagnostic?.Category}.");
            }

            return bytesWritten;
        }

        private struct ReadAction : IAction<EbinRecordReadView>
        {
            private readonly ComponentReadBinding<GeneratedAuthor> _binding;
            private readonly ContentSchemaReference _schema;
            private ComponentReadResult<GeneratedAuthor>? _result;

            internal ReadAction(ContentSchemaReference schema, ComponentReadBinding<GeneratedAuthor> binding)
            {
                _binding = binding;
                _schema = schema;
                _result = null;
            }

            internal readonly ComponentReadResult<GeneratedAuthor>? Result => _result;

            public void Invoke(scoped EbinRecordReadView record)
            {
                var header = record.Header;

                if ((header.SchemaId != _schema.SchemaId)
                    || (header.Major != _schema.Major)
                    || (header.Minor != _schema.Minor))
                {
                    _result = _binding.Reject(
                        ComponentReadFailureKind.SchemaMismatch,
                        16,
                        null,
                        "The generated component record does not match the selected schema tuple.");
                    return;
                }

                if ((record.FieldCount != 1)
                    || !record.TryGetField(1, out var field)
                    || (field.Kind != 7)
                    || (field.Count != 1)
                    || (field.Payload.Length != sizeof(int)))
                {
                    _result = _binding.Reject(
                        ComponentReadFailureKind.InvalidField,
                        64,
                        null,
                        "The generated component record does not match its one-field schema.");
                    return;
                }

                var author = new GeneratedAuthor(BinaryPrimitives.ReadInt32LittleEndian(field.Payload));
                _result = _binding.Complete(in author, record.EncodedBytes.Length);
            }
        }
    }
}
