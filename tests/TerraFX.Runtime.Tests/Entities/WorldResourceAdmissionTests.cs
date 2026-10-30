// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldResourceAdmissionTests
{
    [Test]
    public void ResourceStateSubbudgetRejectsBeforeDefaultOrValidationCallbacks()
    {
        var assignment = new ContentSchemaIdentityAssignment("application/resource-state/admission-budget-test/v1");
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
        var schema = WorldResourceSchema<PaddedResource>.FromRegistry(registry, schemaReference);
        var descriptor = schema.GetField(new ComponentFieldPath([ComponentPropertySegment.FieldTag(1)]));
        var storagePath = new ComponentStoragePath([ComponentStorageSegment.Member("Value")]);
        var fieldBinding = WorldResourceFieldBinding<PaddedResource, ImmutableArray<ulong>>
            .PartitionArray<ulong, PaddedResourceReader, PaddedResourceAuthorReader>(
                descriptor,
                [storagePath],
                default,
                default);
        var storage = new ComponentStorageDeclaration(
            Marshal.SizeOf<PaddedResource>(),
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
            [new ComponentPaddingInterval(sizeof(ulong), Marshal.SizeOf<PaddedResource>() - sizeof(ulong))]);
        var defaults = new PaddedResourceDefaults();
        var factory = WorldResourceFactoryBinding<PaddedResource, ImmutableArray<ulong>>.Create(
            Guid.NewGuid(),
            1,
            schema,
            storage,
            [fieldBinding],
            defaults);
        var codec = new PaddedResourceCodec(factory);
        var factoryProvider = new PaddedResourceFactoryProvider(factory.RegistrationBinding);
        var resourceId = Guid.NewGuid();
        var builder = new WorldRegistrationBuilder(registry);
        _ = builder.RegisterResource(
            new SystemResourceDescription(resourceId, 64, allowParallelAccess: false),
            factoryProvider,
            codec);
        var registration = builder.Build();
        var limits = new WorldSchedulingLimits(4096, 4096, 4096, 4096, 4096, 4096, 0);
        var worldGuid = Guid.NewGuid();
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);

        _ = Assert.Throws<InvalidOperationException>(() => World.Create(new WorldOptions(registration) {
            AdmissionDomain = domain,
            InitialReplayLineageId = Guid.NewGuid(),
            SchedulingLimits = limits,
            WorldGuid = worldGuid,
        }));

        Assert.That(defaults.DefaultCallCount, Is.Zero);
        Assert.That(codec.ValidationCallCount, Is.Zero);

        var emptyRegistration = new WorldRegistrationBuilder(registry).Build();
        using var replacement = World.Create(new WorldOptions(emptyRegistration) {
            AdmissionDomain = domain,
            InitialReplayLineageId = Guid.NewGuid(),
            SchedulingLimits = limits,
            WorldGuid = worldGuid,
        });
        Assert.That(replacement.WorldGuid, Is.EqualTo(worldGuid));
    }

    [StructLayout(LayoutKind.Sequential, Size = 4096)]
    private readonly struct PaddedResource
    {
        private readonly ulong _value;

        internal PaddedResource(ulong value)
        {
            _value = value;
        }

        internal ulong Value => _value;
    }

    private readonly struct PaddedResourceAuthorReader : IComponentArrayReader<ImmutableArray<ulong>, ulong>
    {
        public int GetCount(in ImmutableArray<ulong> value) => value.Length;

        public ulong GetElement(in ImmutableArray<ulong> value, int index) => value[index];
    }

    private sealed class PaddedResourceCodec : IWorldResourceStateCodec<PaddedResource, ImmutableArray<ulong>>
    {
        private WorldResourceCodecBinding<PaddedResource>? _binding;
        private readonly WorldResourceFactoryBinding<PaddedResource, ImmutableArray<ulong>> _factory;

        internal PaddedResourceCodec(WorldResourceFactoryBinding<PaddedResource, ImmutableArray<ulong>> factory)
        {
            _factory = factory;
        }

        public WorldResourceCodecBinding<PaddedResource> Binding
            => _binding ??= WorldResourceCodecBinding<PaddedResource>.Create(_factory, this);

        internal int ValidationCallCount { get; private set; }

        public nint Count(
            scoped WorldResourceStateView<PaddedResource> value,
            WorldResourceCaptureBinding binding,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("The budget rejection must precede every resource state callback.");

        public WorldResourceReadResult<ImmutableArray<ulong>> Read(
            scoped ReadOnlySpan<byte> source,
            WorldResourceReadBinding<ImmutableArray<ulong>> binding,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("The budget rejection must precede every resource state callback.");

        public void Restore(
            in ImmutableArray<ulong> author,
            WorldResourceRestoreBinding<PaddedResource> binding,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("The budget rejection must precede every resource state callback.");

        public ImmutableArray<Diagnostic> Validate(
            scoped WorldResourceStateView<PaddedResource> value,
            WorldResourceValidationBinding binding,
            CancellationToken cancellationToken)
        {
            ValidationCallCount++;
            throw new InvalidOperationException("The budget rejection must precede resource validation.");
        }

        public nint Write(
            scoped WorldResourceStateView<PaddedResource> value,
            scoped Span<byte> destination,
            WorldResourceCaptureBinding binding,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("The budget rejection must precede every resource state callback.");
    }

    private sealed class PaddedResourceDefaults : IWorldResourceDefaultProvider<PaddedResource>
    {
        private int _defaultCallCount;

        internal int DefaultCallCount => _defaultCallCount;

        public PaddedResource Create(int partitionIndex, WorldResourceDefaultBinding binding)
        {
            _defaultCallCount++;
            var value = checked((ulong)partitionIndex + (ulong)binding.PartitionCount);
            return new PaddedResource(value);
        }
    }

    private sealed class PaddedResourceFactoryProvider : IWorldResourceFactory<PaddedResource>
    {
        private readonly WorldResourceFactoryBinding<PaddedResource> _binding;

        internal PaddedResourceFactoryProvider(WorldResourceFactoryBinding<PaddedResource> binding)
        {
            _binding = binding;
        }

        public WorldResourceFactoryBinding<PaddedResource> Binding => _binding;
    }

    private readonly struct PaddedResourceReader : IComponentValueReader<PaddedResource, ulong>
    {
        public ulong Get(in PaddedResource value) => value.Value;
    }
}
