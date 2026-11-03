// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceSystemRegistrationArrayWriter
{
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("state/system-registration").SchemaId;

    internal static EbinWriteStatus Measure(
        WorldRegistration registration,
        scoped ReadOnlySpan<EbinPayloadSlice> recordsByRegistration,
        scoped ReadOnlySpan<byte> recordStorage,
        EbinReadLimits limits,
        scoped Span<int> systemOrderScratch,
        scoped Span<EbinPayloadSlice> orderedRecords,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(limits);
        ValidateWorkspaceLengths(
            registration,
            recordsByRegistration,
            recordStorage,
            systemOrderScratch,
            orderedRecords);
        registration.CopyPersistenceSystemOrder(systemOrderScratch);

        for (var systemIndex = 0; systemIndex < recordsByRegistration.Length; systemIndex++)
        {
            var slice = recordsByRegistration[systemIndex];
            var registrationEntry = registration.Systems[systemIndex];

            if ((slice.ByteLength <= 0)
                || (slice.Offset < 0)
                || (slice.Offset > recordStorage.Length)
                || (slice.ByteLength > (recordStorage.Length - slice.Offset)))
            {
                payloadBytes = 0;
                return InvalidRecord(out diagnostic);
            }

            var encodedRecord = recordStorage.Slice(slice.Offset, slice.ByteLength);
            var assertions = new SystemRegistrationIdentityAssertions(
                registrationEntry.Description.SystemId,
                registrationEntry.Description.Order);
            var readStatus = EbinStructuralReader.Read(
                encodedRecord,
                limits,
                EbinReadPolicy.Canonical,
                ref assertions,
                out var bytesConsumed,
                out var readDiagnostic);

            if ((readStatus is not EbinReadStatus.Complete)
                || (bytesConsumed != encodedRecord.Length))
            {
                payloadBytes = 0;

                if (readStatus is EbinReadStatus.Complete)
                {
                    return InvalidRecord(out diagnostic);
                }

                diagnostic = readDiagnostic is EbinReadDiagnostic value
                    ? new EbinStructuralDiagnostic(
                        value.SchemaId,
                        value.Tag,
                        value.Category,
                        (value.Offset == 0) ? null : value.Offset,
                        value.Claim,
                        value.Limit,
                        EbinStructuralDiagnosticSource.EncodedInput)
                    : CreateDiagnostic("incomplete-system-registration-row");
                return (readStatus is EbinReadStatus.UnsupportedSchema)
                    ? EbinWriteStatus.UnsupportedSchema
                    : EbinWriteStatus.InvalidRecord;
            }

            if (!assertions.IsMatch)
            {
                payloadBytes = 0;
                return InvalidRecord(out diagnostic);
            }
        }

        for (var orderIndex = 0; orderIndex < systemOrderScratch.Length; orderIndex++)
        {
            orderedRecords[orderIndex] = recordsByRegistration[systemOrderScratch[orderIndex]];
        }

        return EbinPayloadWriter.MeasureRecordArray(
            orderedRecords,
            recordStorage,
            limits,
            out payloadBytes,
            out diagnostic);
    }

    internal static EbinWriteStatus Write(
        WorldRegistration registration,
        scoped ReadOnlySpan<EbinPayloadSlice> recordsByRegistration,
        scoped ReadOnlySpan<byte> recordStorage,
        EbinReadLimits limits,
        scoped Span<int> systemOrderScratch,
        scoped Span<EbinPayloadSlice> orderedRecords,
        scoped Span<byte> payloadDestination,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = Measure(
            registration,
            recordsByRegistration,
            recordStorage,
            limits,
            systemOrderScratch,
            orderedRecords,
            out var requiredBytes,
            out diagnostic);
        payloadBytes = 0;

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (payloadDestination.Length != requiredBytes)
        {
            throw new ArgumentException("The system-registration array destination must match its measured extent.", nameof(payloadDestination));
        }

        return EbinPayloadWriter.WriteRecordArray(
            orderedRecords,
            recordStorage,
            limits,
            payloadDestination,
            out payloadBytes,
            out diagnostic);
    }

    private static EbinStructuralDiagnostic CreateDiagnostic(string category)
        => new EbinStructuralDiagnostic(
            s_schemaId,
            1,
            category,
            null,
            0,
            1,
            EbinStructuralDiagnosticSource.Construction);

    private static EbinWriteStatus InvalidRecord(out EbinStructuralDiagnostic? diagnostic)
    {
        diagnostic = CreateDiagnostic("system-registration-array-member");
        return EbinWriteStatus.InvalidRecord;
    }

    private static void ValidateWorkspaceLengths(
        WorldRegistration registration,
        scoped ReadOnlySpan<EbinPayloadSlice> recordsByRegistration,
        scoped ReadOnlySpan<byte> recordStorage,
        scoped ReadOnlySpan<int> systemOrderScratch,
        scoped ReadOnlySpan<EbinPayloadSlice> orderedRecords)
    {
        var systemCount = registration.Systems.Length;

        if ((recordsByRegistration.Length != systemCount)
            || (systemOrderScratch.Length != systemCount)
            || (orderedRecords.Length != systemCount))
        {
            throw new ArgumentException("The system-registration array workspaces must match the registration count.");
        }

        if (recordsByRegistration.Overlaps(orderedRecords)
            || MemoryMarshal.AsBytes(recordsByRegistration).Overlaps(MemoryMarshal.AsBytes(systemOrderScratch))
            || MemoryMarshal.AsBytes(orderedRecords).Overlaps(MemoryMarshal.AsBytes(systemOrderScratch))
            || MemoryMarshal.AsBytes(recordsByRegistration).Overlaps(recordStorage)
            || MemoryMarshal.AsBytes(orderedRecords).Overlaps(recordStorage)
            || MemoryMarshal.AsBytes(systemOrderScratch).Overlaps(recordStorage))
        {
            throw new ArgumentException("The system-registration array input and workspaces must not overlap.");
        }
    }

    private struct SystemRegistrationIdentityAssertions : IAction<EbinRecordReadView>
    {
        private readonly Guid _systemId;
        private readonly int _order;

        internal SystemRegistrationIdentityAssertions(Guid systemId, int order)
        {
            _systemId = systemId;
            _order = order;
            IsMatch = false;
        }

        internal bool IsMatch { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
        {
            IsMatch = false;

            if ((record.Header.SchemaId != s_schemaId)
                || (record.Header.Major != 1)
                || (record.Header.Minor != 0)
                || (record.FieldCount < 2))
            {
                return;
            }

            var systemId = record.GetField(0);
            var order = record.GetField(1);

            if ((systemId.Tag != 1)
                || (systemId.Kind != 11)
                || !systemId.IsRequired
                || (systemId.Count != 1)
                || (systemId.Payload.Length != 16)
                || (order.Tag != 2)
                || (order.Kind != 7)
                || !order.IsRequired
                || (order.Count != 1)
                || (order.Payload.Length != sizeof(int)))
            {
                return;
            }

            IsMatch = (new Guid(systemId.Payload, bigEndian: true) == _systemId)
                && (BinaryPrimitives.ReadInt32LittleEndian(order.Payload) == _order);
        }
    }
}
