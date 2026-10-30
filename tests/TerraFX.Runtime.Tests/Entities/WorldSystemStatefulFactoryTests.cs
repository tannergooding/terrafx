// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldSystemStatefulFactoryTests
{
    [Test]
    public void StatefulFactoryAcceptsClosedNestedRecordFields()
    {
        var nestedAssignment = new ContentSchemaIdentityAssignment("application/system-state/counter-nested/v1");
        var nestedReference = new ContentSchemaReference(
            nestedAssignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var nestedField = new ContentFieldDescriptor(
            7,
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
        var nestedMetadata = new DataSchema(
            nestedReference,
            nestedAssignment,
            ContentSchemaRole.Descriptor,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            [nestedField]);
        var stateAssignment = new ContentSchemaIdentityAssignment("application/system-state/counter-root/v1");
        var stateReference = new ContentSchemaReference(
            stateAssignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var rootField = new ContentFieldDescriptor(
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
        var stateMetadata = new DataSchema(
            stateReference,
            stateAssignment,
            ContentSchemaRole.SystemState,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            [rootField]);
        var registry = new CompiledContentSchemaRegistry([nestedMetadata, stateMetadata]);
        var schema = WorldSystemSchema<ProbeSystem>.FromRegistry(registry, stateReference);
        var fieldPath = new ComponentFieldPath([
            ComponentPropertySegment.FieldTag(1),
            ComponentPropertySegment.FieldTag(7),
        ]);
        var runtimePath = new WorldSystemSourcePath(["Counter"]);
        var authorPath = new WorldSystemSourcePath(["Counter"]);
        var fieldBinding = WorldSystemFieldBinding<ProbeSystem, ProbeAuthor>.Scalar<
            int,
            SystemCounterReader,
            AuthorCounterReader>(
                schema.GetField(fieldPath),
                [runtimePath],
                [authorPath],
                default,
                default);
        var sourceState = new WorldSystemSourceStateDeclaration(
            typeof(ProbeSystem),
            [
                new WorldSystemSourceLeaf(
                    runtimePath,
                    typeof(int),
                    WorldSystemSourcePurpose.Authoritative),
            ]);
        var binding = WorldSystemFactoryBinding.Stateful<
            ProbeSystem,
            ProbeAuthor,
            StatefulCreate,
            StatefulDefaults,
            StatefulRelease>(
            Guid.NewGuid(),
            1,
            schema,
            sourceState,
            [fieldBinding],
            default,
            default,
            new StatefulStateCodec(new CallbackCounts()),
            default);

        Assert.That(binding.Descriptor.Fields, Has.Length.EqualTo(1));
        Assert.That(binding.Descriptor.Fields[0].Path.Segments, Has.Length.EqualTo(2));
    }

    [Test]
    public void StatefulFactoryRunsDefaultsAndValidationForClaimedInstance()
    {
        var assignment = new ContentSchemaIdentityAssignment("application/system-state/counter/v1");
        var schemaReference = new ContentSchemaReference(
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
            schemaReference,
            assignment,
            ContentSchemaRole.SystemState,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            [field]);
        var schemas = new CompiledContentSchemaRegistry([metadata]);
        var schema = WorldSystemSchema<ProbeSystem>.FromRegistry(schemas, schemaReference);
        var runtimePath = new WorldSystemSourcePath(["Counter"]);
        var authorPath = new WorldSystemSourcePath(["Counter"]);
        var sourceState = new WorldSystemSourceStateDeclaration(
            typeof(ProbeSystem),
            [
                new WorldSystemSourceLeaf(
                    runtimePath,
                    typeof(int),
                    WorldSystemSourcePurpose.Authoritative),
            ]);
        var counts = new CallbackCounts();
        var binding = WorldSystemFactoryBinding.Stateful<
            ProbeSystem,
            ProbeAuthor,
            StatefulCreate,
            StatefulDefaults,
            StatefulRelease>(
            Guid.NewGuid(),
            1,
            schema,
            sourceState,
            [
                WorldSystemFieldBinding<ProbeSystem, ProbeAuthor>.Scalar<int, SystemCounterReader, AuthorCounterReader>(
                    schema.Fields[0],
                    [runtimePath],
                    [authorPath],
                    new SystemCounterReader(counts),
                    default),
            ],
            new StatefulCreate(counts),
            new StatefulDefaults(counts),
            new StatefulStateCodec(counts),
            new StatefulRelease(counts));
        var builder = new WorldRegistrationBuilder(schemas);
        _ = builder.RegisterSystem(
            new SystemDescription(
                Guid.NewGuid(),
                0,
                SimulationStage.Input,
                [],
                [],
                [],
                SystemThreadMode.OwnerThread),
            new StatefulFactory(binding));
        var registration = builder.Build();
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        var world = World.Create(new WorldOptions(registration) {
            AdmissionDomain = domain,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });

        Assert.That(counts.Events, Is.EqualTo(["Create", "Defaults", "Validate"]));
        Assert.That(counts.ValidatedCounter, Is.EqualTo(42));
        Assert.That(counts.RuntimeReadCount, Is.EqualTo(1));
        Assert.That(counts.ValidateFieldsCount, Is.EqualTo(1));
        world.Dispose();
        Assert.That(counts.Events, Is.EqualTo(["Create", "Defaults", "Validate", "Release"]));

        var failedWorldGuid = new Guid("90112233-4455-4677-8899-aabbccddeeff");
        var failedLineageId = new Guid("a0112233-4455-4677-8899-aabbccddeeff");
        counts.FailValidation = true;
        var constructionFailure = Assert.Throws<InvalidOperationException>(() => World.Create(new WorldOptions(registration) {
            AdmissionDomain = domain,
            InitialReplayLineageId = failedLineageId,
            WorldGuid = failedWorldGuid,
        }));
        Assert.That(constructionFailure, Is.SameAs(counts.ValidationFailure));
        Assert.That(counts.Events, Is.EqualTo([
            "Create", "Defaults", "Validate", "Release",
            "Create", "Defaults", "Validate", "Release",
        ]));

        counts.FailValidation = false;
        using var retryWorld = World.Create(new WorldOptions(registration) {
            AdmissionDomain = domain,
            InitialReplayLineageId = failedLineageId,
            WorldGuid = failedWorldGuid,
        });
        retryWorld.Dispose();
        Assert.That(counts.Events, Is.EqualTo([
            "Create", "Defaults", "Validate", "Release",
            "Create", "Defaults", "Validate", "Release",
            "Create", "Defaults", "Validate", "Release",
        ]));
        Assert.That(counts.RuntimeReadCount, Is.EqualTo(2));
        Assert.That(counts.ValidateFieldsCount, Is.EqualTo(2));
    }

    private readonly struct AuthorCounterReader : IWorldSystemValueReader<ProbeAuthor, int>
    {
        public int Invoke(in ProbeAuthor source) => source.Counter;
    }

    private sealed class CallbackCounts
    {
        internal List<string> Events { get; } = [];

        internal bool FailValidation { get; set; }

        internal int RuntimeReadCount { get; set; }

        internal int ValidateFieldsCount { get; set; }

        internal int ValidatedCounter { get; set; }

        internal InvalidOperationException ValidationFailure { get; } = new("The stateful system validation failed.");
    }

    private readonly record struct ProbeAuthor(int Counter);

    private sealed class ProbeSystem : IWorldSystem
    {
        internal int Counter { get; set; }

        public void Execute(WorldSystemContext context)
        {
        }
    }

    private readonly struct StatefulCreate : IWorldSystemCreate<ProbeSystem>
    {
        private readonly CallbackCounts _counts;

        internal StatefulCreate(CallbackCounts counts)
        {
            _counts = counts;
        }

        public ProbeSystem Invoke()
        {
            _counts.Events.Add("Create");
            return new ProbeSystem();
        }
    }

    private readonly struct StatefulDefaults : IWorldSystemDefaults<ProbeSystem>
    {
        private readonly CallbackCounts _counts;

        internal StatefulDefaults(CallbackCounts counts)
        {
            _counts = counts;
        }

        public void Invoke(ProbeSystem system)
        {
            _counts.Events.Add("Defaults");
            system.Counter = 42;
        }
    }

    private sealed class StatefulFactory : IWorldSystemFactory
    {
        internal StatefulFactory(WorldSystemFactoryBinding binding)
        {
            Binding = binding;
        }

        public WorldSystemFactoryBinding Binding { get; }
    }

    private readonly struct StatefulRelease : IWorldSystemRelease<ProbeSystem>
    {
        private readonly CallbackCounts _counts;

        internal StatefulRelease(CallbackCounts counts)
        {
            _counts = counts;
        }

        public void Invoke(ProbeSystem system)
            => _counts.Events.Add("Release");
    }

    private sealed class StatefulStateCodec : IWorldSystemStateCodec<ProbeSystem, ProbeAuthor>
    {
        private readonly CallbackCounts _counts;

        internal StatefulStateCodec(CallbackCounts counts)
        {
            _counts = counts;
        }

        public nint Count(ProbeSystem system, WorldSystemCaptureBinding binding, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public WorldSystemReadResult<ProbeAuthor> Read(
            scoped ReadOnlySpan<byte> source,
            WorldSystemReadBinding<ProbeAuthor> binding,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public void Restore(
            ProbeSystem system,
            in ProbeAuthor author,
            WorldStateRestoreBinding binding,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ImmutableArray<Diagnostic> Validate(
            ProbeSystem system,
            WorldSystemValidationBinding binding,
            CancellationToken cancellationToken)
        {
            _counts.Events.Add("Validate");
            _counts.ValidatedCounter = system.Counter;

            if (_counts.FailValidation)
            {
                throw _counts.ValidationFailure;
            }

            _ = binding.ValidateFields(system);
            _counts.ValidateFieldsCount++;
            return [];
        }

        public nint Write(
            ProbeSystem system,
            scoped Span<byte> destination,
            WorldSystemCaptureBinding binding,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private readonly struct SystemCounterReader : IWorldSystemValueReader<ProbeSystem, int>
    {
        private readonly CallbackCounts? _counts;

        internal SystemCounterReader(CallbackCounts counts)
        {
            _counts = counts;
        }

        public int Invoke(in ProbeSystem source)
        {
            if (_counts is not null)
            {
                _counts.RuntimeReadCount++;
            }

            return source.Counter;
        }
    }
}
