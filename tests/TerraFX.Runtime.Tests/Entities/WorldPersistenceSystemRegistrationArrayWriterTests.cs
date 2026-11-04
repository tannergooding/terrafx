// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceSystemRegistrationArrayWriterTests
{
    private static WorldRegistration CreateRegistration()
    {
        var builder = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([]));
        RegisterSystem(builder, Guid.Parse("10000000-0000-0000-0000-000000000001"), 17);
        RegisterSystem(builder, Guid.Parse("90000000-0000-0000-0000-000000000009"), -5);
        RegisterSystem(builder, Guid.Parse("50000000-0000-0000-0000-000000000005"), 3);
        return builder.Build();
    }

    private static void RegisterSystem(WorldRegistrationBuilder builder, Guid systemId, int order)
    {
        var description = new SystemDescription(
            systemId,
            order,
            SimulationStage.Input,
            [],
            [],
            [],
            SystemThreadMode.OwnerThread);
        var sourceState = new WorldSystemSourceStateDeclaration(typeof(RegistrationArraySystem), []);
        var binding = WorldSystemFactoryBinding.Stateless<
            RegistrationArraySystem,
            RegistrationArraySystemCreate,
            RegistrationArraySystemRelease>(
            Guid.NewGuid(),
            1,
            sourceState,
            default,
            default);
        _ = builder.RegisterSystem(description, new RegistrationArraySystemFactory(binding));
    }

    private static byte[] WriteRecord(SystemRegistration registration)
    {
        var limits = EbinReadLimits.Foundation;
        var description = registration.Description;
        var factory = registration.Factory.Descriptor;
        Assert.That(
            WorldPersistenceSystemRegistrationWriter.MeasureWorkspace(
                description,
                factory,
                limits,
                out var required,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        var accessScratch = new SystemResourceAccess[required.AccessCount];
        var accessSlices = new EbinPayloadSlice[required.AccessCount];
        var accessStorage = new byte[required.AccessStorageBytes];
        var accessCandidateStorage = new byte[required.AccessCandidateStorageBytes];
        var accessArrayPayloadStorage = new byte[required.AccessArrayPayloadBytes];
        var afterArrayPayloadStorage = new byte[required.AfterArrayPayloadBytes];
        var beforeArrayPayloadStorage = new byte[required.BeforeArrayPayloadBytes];
        var dependencyScratch = new Guid[required.DependencyScratchCount];
        var rootPayloadStorage = new byte[required.RootPayloadStorageBytes];
        var schemaCandidateStorage = new byte[required.SchemaReferenceRecordBytes];
        var schemaRecordStorage = new byte[required.SchemaReferenceRecordBytes];
        var buffers = new WorldPersistenceSystemRegistrationBuffers {
            AccessScratch = accessScratch,
            AccessSlices = accessSlices,
            AccessStorage = accessStorage,
            AccessCandidateStorage = accessCandidateStorage,
            AccessArrayPayloadStorage = accessArrayPayloadStorage,
            AfterArrayPayloadStorage = afterArrayPayloadStorage,
            BeforeArrayPayloadStorage = beforeArrayPayloadStorage,
            DependencyScratch = dependencyScratch,
            RootPayloadStorage = rootPayloadStorage,
            SchemaCandidateStorage = schemaCandidateStorage,
            SchemaRecordStorage = schemaRecordStorage,
        };
        Assert.That(
            WorldPersistenceSystemRegistrationWriter.Measure(
                description,
                factory,
                limits,
                ref buffers,
                out var workspace,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        var candidateStorage = new byte[workspace.RecordBytes];
        var destination = new byte[workspace.RecordBytes];
        buffers.CandidateStorage = candidateStorage;
        buffers.Destination = destination;
        Assert.That(
            WorldPersistenceSystemRegistrationWriter.Write(
                description,
                factory,
                limits,
                workspace,
                ref buffers,
                out var bytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(bytesWritten, Is.EqualTo(workspace.RecordBytes));
        return destination;
    }

    private static (EbinPayloadSlice[] Slices, byte[] Storage) WriteRecords(WorldRegistration registration)
    {
        var rows = new byte[registration.Systems.Length][];
        var storageBytes = 0;

        for (var index = 0; index < rows.Length; index++)
        {
            rows[index] = WriteRecord(registration.Systems[index]);
            storageBytes = checked(storageBytes + rows[index].Length);
        }

        var slices = new EbinPayloadSlice[rows.Length];
        var storage = new byte[storageBytes];
        var offset = 0;

        for (var index = 0; index < rows.Length; index++)
        {
            slices[index] = new EbinPayloadSlice(offset, rows[index].Length);
            rows[index].CopyTo(storage, offset);
            offset = checked(offset + rows[index].Length);
        }

        return (slices, storage);
    }

    [Test]
    public void CapturesRegisteredSystemsInTheWorldRegistrationRoot()
    {
        var registration = CreateRegistration();
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
        var assertions = new RegistrationRootAssertions(expectedSystemCount: registration.Systems.Length);
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
    public void RejectsRowsOutsideTheCallerOwnedStorage()
    {
        var registration = CreateRegistration();
        var (slices, storage) = WriteRecords(registration);
        slices[0] = new EbinPayloadSlice(storage.Length, 1);
        var orderScratch = new int[registration.Systems.Length];
        var orderedRecords = new EbinPayloadSlice[registration.Systems.Length];
        Assert.That(
            WorldPersistenceSystemRegistrationArrayWriter.Measure(
                registration,
                slices,
                storage,
                EbinReadLimits.Foundation,
                orderScratch,
                orderedRecords,
                out var payloadBytes,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.InvalidRecord));
        Assert.That(payloadBytes, Is.Zero);
        Assert.That(diagnostic?.Category, Is.EqualTo("system-registration-array-member"));
    }

    [Test]
    public void RejectsRowsThatDoNotMatchTheirRuntimeRegistrationSlot()
    {
        var registration = CreateRegistration();
        var (slices, storage) = WriteRecords(registration);
        (slices[0], slices[1]) = (slices[1], slices[0]);
        var orderScratch = new int[registration.Systems.Length];
        var orderedRecords = new EbinPayloadSlice[registration.Systems.Length];
        Assert.That(
            WorldPersistenceSystemRegistrationArrayWriter.Measure(
                registration,
                slices,
                storage,
                EbinReadLimits.Foundation,
                orderScratch,
                orderedRecords,
                out var payloadBytes,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.InvalidRecord));
        Assert.That(payloadBytes, Is.Zero);
        Assert.That(diagnostic?.Category, Is.EqualTo("system-registration-array-member"));
    }

    [Test]
    public void WritesCanonicalEmptyArray()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        Assert.That(
            WorldPersistenceSystemRegistrationArrayWriter.Measure(
                registration,
                [],
                [],
                EbinReadLimits.Foundation,
                [],
                [],
                out var payloadBytes,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(payloadBytes, Is.EqualTo(16));
        var payload = new byte[payloadBytes];
        Assert.That(
            WorldPersistenceSystemRegistrationArrayWriter.Write(
                registration,
                [],
                [],
                EbinReadLimits.Foundation,
                [],
                [],
                payload,
                out var bytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(bytesWritten, Is.EqualTo(16));
        Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(payload), Is.Zero);
    }

    [Test]
    public void WritesSystemsInSignedOrderAndValidatesTheirRegistrationIdentity()
    {
        var registration = CreateRegistration();
        var (slices, storage) = WriteRecords(registration);
        var orderScratch = new int[registration.Systems.Length];
        var orderedRecords = new EbinPayloadSlice[registration.Systems.Length];
        Assert.That(
            WorldPersistenceSystemRegistrationArrayWriter.Measure(
                registration,
                slices,
                storage,
                EbinReadLimits.Foundation,
                orderScratch,
                orderedRecords,
                out var payloadBytes,
                out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        var payload = new byte[payloadBytes];
        Assert.That(
            WorldPersistenceSystemRegistrationArrayWriter.Write(
                registration,
                slices,
                storage,
                EbinReadLimits.Foundation,
                orderScratch,
                orderedRecords,
                payload,
                out var bytesWritten,
                out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        Assert.That(bytesWritten, Is.EqualTo(payloadBytes));
        Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(payload), Is.EqualTo(3));
        var expectedIds = new[] {
            Guid.Parse("90000000-0000-0000-0000-000000000009"),
            Guid.Parse("50000000-0000-0000-0000-000000000005"),
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
        };
        var expectedOrders = new[] { -5, 3, 17 };
        var previousOffset = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(8));

        for (var index = 0; index < expectedIds.Length; index++)
        {
            var endOffset = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(16 + (index * 8)));
            var assertions = new RegistrationArrayRowAssertions(expectedIds[index], expectedOrders[index]);
            Assert.That(
                EbinStructuralReader.Read(
                    payload.AsSpan(checked((int)previousOffset), checked((int)(endOffset - previousOffset))),
                    EbinReadLimits.Foundation,
                    EbinReadPolicy.Canonical,
                    ref assertions,
                    out var rowBytes,
                    out var readDiagnostic),
                Is.EqualTo(EbinReadStatus.Complete));
            Assert.That(readDiagnostic, Is.Null);
            Assert.That(assertions.IsMatch, Is.True);
            Assert.That(rowBytes, Is.EqualTo(checked((int)(endOffset - previousOffset))));
            previousOffset = endOffset;
        }

        Assert.That(previousOffset, Is.EqualTo((ulong)payload.Length));
    }

    private struct RegistrationArrayRowAssertions : IAction<EbinRecordReadView>
    {
        private readonly Guid _systemId;
        private readonly int _order;

        internal RegistrationArrayRowAssertions(Guid systemId, int order)
        {
            _systemId = systemId;
            _order = order;
            IsMatch = false;
        }

        internal bool IsMatch { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
        {
            IsMatch = (record.Header.SchemaId == new ContentSchemaIdentityAssignment("state/system-registration").SchemaId)
                && (new Guid(record.GetField(0).Payload, bigEndian: true) == _systemId)
                && (BinaryPrimitives.ReadInt32LittleEndian(record.GetField(1).Payload) == _order);
        }
    }

    private sealed class RegistrationArraySystem : IWorldSystem
    {
        public void Execute(WorldSystemContext context)
        {
        }
    }

    private readonly struct RegistrationArraySystemCreate : IWorldSystemCreate<RegistrationArraySystem>
    {
        public RegistrationArraySystem Invoke() => new();
    }

    private sealed class RegistrationArraySystemFactory : IWorldSystemFactory
    {
        internal RegistrationArraySystemFactory(WorldSystemFactoryBinding binding)
        {
            Binding = binding;
        }

        public WorldSystemFactoryBinding Binding { get; }
    }

    private readonly struct RegistrationArraySystemRelease : IWorldSystemRelease<RegistrationArraySystem>
    {
        public void Invoke(RegistrationArraySystem system)
            => ArgumentNullException.ThrowIfNull(system);
    }

    private struct RegistrationRootAssertions : IAction<EbinRecordReadView>
    {
        private readonly int _expectedSystemCount;

        internal RegistrationRootAssertions(int expectedSystemCount)
        {
            _expectedSystemCount = expectedSystemCount;
            IsMatch = false;
        }

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

            var systems = record.GetField(2);
            IsMatch = (systems.Tag == 3)
                && (systems.Kind == 24)
                && (systems.Count == checked((ulong)_expectedSystemCount))
                && (BinaryPrimitives.ReadUInt64LittleEndian(systems.Payload) == checked((ulong)_expectedSystemCount));
        }
    }
}
