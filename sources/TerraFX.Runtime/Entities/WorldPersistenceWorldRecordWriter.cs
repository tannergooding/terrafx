// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceWorldRecordWriter
{
    private const int FieldCapacity = 12;
    private const int MaximumArrayCount = 65_536;
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("state/world").SchemaId;

    internal static EbinWriteStatus Measure(
        WorldPersistenceWorldRecordSources sources,
        EbinReadLimits limits,
        scoped Span<byte> rootPayloadStorage,
        out WorldPersistenceWorldRecordWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        workspace = default;
        var rootPayloadStatus = MeasureRootPayloadStorage(sources, limits, out var rootPayloadBytes, out diagnostic);

        if (rootPayloadStatus is not EbinWriteStatus.Complete)
        {
            return rootPayloadStatus;
        }

        if (rootPayloadStorage.Length != rootPayloadBytes)
        {
            throw new ArgumentException("The state/world root-payload workspace must match its measured extent.", nameof(rootPayloadStorage));
        }

        ValidateNonoverlappingPayloads(sources, rootPayloadStorage);
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[FieldCapacity];
        var buildStatus = BuildFields(sources, limits, fields, rootPayloadStorage, out var fieldCount, out var payloadBytes, out diagnostic);

        if (buildStatus is not EbinWriteStatus.Complete)
        {
            return buildStatus;
        }

        if (payloadBytes != rootPayloadBytes)
        {
            throw new InvalidOperationException("The state/world root-payload extent changed during measurement.");
        }

        var source = new EbinRecordWriteView(
            s_schemaId,
            major: 1,
            minor: 0,
            fields[..fieldCount],
            rootPayloadStorage[..payloadBytes]);
        var status = EbinStructuralWriter.Measure(in source, limits, out var recordBytes, out diagnostic);

        if (status is EbinWriteStatus.Complete)
        {
            workspace = new WorldPersistenceWorldRecordWorkspaceRequirements(
                sources.AssetIds.Length,
                sources.ControlRecordCount,
                !sources.Frame2Record.IsEmpty,
                !sources.StrictLayoutRecord.IsEmpty,
                sources.RandomStreamCount,
                rootPayloadBytes,
                recordBytes);
        }

        return status;
    }

    internal static EbinWriteStatus MeasureRootPayloadStorage(
        WorldPersistenceWorldRecordSources sources,
        EbinReadLimits limits,
        out int rootPayloadStorageBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        rootPayloadStorageBytes = 0;
        diagnostic = null;
        var hasFrame2 = !sources.Frame2Record.IsEmpty;
        var hasStrictLayout = !sources.StrictLayoutRecord.IsEmpty;
        var fieldCount = 10 + (hasFrame2 ? 1 : 0) + (hasStrictLayout ? 1 : 0);

        if (limits.MaximumFieldsPerRecord < fieldCount)
        {
            diagnostic = CreateDiagnostic("field-count-budget", checked((ulong)fieldCount), checked((ulong)limits.MaximumFieldsPerRecord));
            return EbinWriteStatus.InvalidRecord;
        }

        var status = ValidateRecord(
            sources.ConfigurationRecord,
            "state/configuration",
            expectedFieldCount: 16,
            limits,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = ValidateRecord(
            sources.ClockRecord,
            "state/clock",
            expectedFieldCount: hasFrame2 ? 10 : 9,
            limits,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (hasFrame2)
        {
            status = ValidateRecord(sources.Frame2Record, "foundation/frame2", expectedFieldCount: 6, limits, out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }
        }

        status = ValidateRecord(sources.Frame3Record, "foundation/frame3", expectedFieldCount: 6, limits, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = ValidateRecord(sources.EcsRecord, "state/ecs", expectedFieldCount: 5, limits, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = ValidateRecord(sources.ScenesRecord, "state/scenes", expectedFieldCount: 2, limits, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = ValidateRecord(sources.CursorsRecord, "state/cursors", expectedFieldCount: 4, limits, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = ReadNextControlSequence(sources.CursorsRecord, limits, out var nextControlSequence, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var expectedNextControlSequence = checked((long)sources.ControlRecordCount + 1);

        if (nextControlSequence != expectedNextControlSequence)
        {
            diagnostic = CreateDiagnostic(
                "next-control-sequence-mismatch",
                checked((ulong)nextControlSequence),
                checked((ulong)expectedNextControlSequence));
            return EbinWriteStatus.InvalidRecord;
        }

        status = ValidateRecord(sources.RegistrationRecord, "state/registration", expectedFieldCount: 4, limits, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (hasStrictLayout)
        {
            status = ValidateRecord(sources.StrictLayoutRecord, "state/strict-layout", expectedFieldCount: 0, limits, out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }
        }

        status = ValidateArrayPayload(
            sources.ControlsArrayPayload,
            sources.ControlRecordCount,
            limits,
            "control-array",
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = ValidateArrayPayload(
            sources.RandomArrayPayload,
            sources.RandomStreamCount,
            limits,
            "random-array",
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (sources.ControlRecordCount > MaximumArrayCount)
        {
            diagnostic = CreateDiagnostic(
                "control-array-count",
                checked((ulong)sources.ControlRecordCount),
                MaximumArrayCount);
            return EbinWriteStatus.InvalidRecord;
        }

        if (sources.RandomStreamCount > MaximumArrayCount)
        {
            diagnostic = CreateDiagnostic(
                "random-array-count",
                checked((ulong)sources.RandomStreamCount),
                MaximumArrayCount);
            return EbinWriteStatus.InvalidRecord;
        }

        var maximumArrayElements = Math.Min(limits.MaximumArrayElements, limits.MaximumAggregateArrayElements);

        if (sources.AssetIds.Length > maximumArrayElements)
        {
            diagnostic = CreateDiagnostic(
                "asset-id-array-count",
                checked((ulong)sources.AssetIds.Length),
                checked((ulong)maximumArrayElements));
            return EbinWriteStatus.InvalidRecord;
        }

        for (var index = 0; index < sources.AssetIds.Length; index++)
        {
            var assetId = sources.AssetIds[index];

            if (assetId is 0 or > int.MaxValue)
            {
                diagnostic = CreateDiagnostic("asset-id-array-value", checked((ulong)assetId), int.MaxValue);
                return EbinWriteStatus.InvalidRecord;
            }

            if ((index > 0) && (sources.AssetIds[index - 1] >= assetId))
            {
                diagnostic = CreateDiagnostic("asset-id-array-order", checked((ulong)assetId), sources.AssetIds[index - 1]);
                return EbinWriteStatus.InvalidRecord;
            }
        }

        var assetIdPayloadBytes = checked(32L + (4L * sources.AssetIds.Length));
        var rootPayloadBytes = checked(
            sources.ConfigurationRecord.Length
            + (long)sources.ClockRecord.Length
            + (hasFrame2 ? sources.Frame2Record.Length : 0)
            + sources.Frame3Record.Length
            + sources.EcsRecord.Length
            + sources.ScenesRecord.Length
            + assetIdPayloadBytes
            + sources.ControlsArrayPayload.Length
            + sources.CursorsRecord.Length
            + sources.RandomArrayPayload.Length
            + (hasStrictLayout ? sources.StrictLayoutRecord.Length : 0)
            + sources.RegistrationRecord.Length);

        if ((rootPayloadBytes > int.MaxValue) || (rootPayloadBytes > limits.MaximumRecordBytes))
        {
            diagnostic = CreateDiagnostic(
                "record-byte-budget",
                checked((ulong)rootPayloadBytes),
                checked((ulong)Math.Min(limits.MaximumRecordBytes, int.MaxValue)));
            return EbinWriteStatus.InvalidRecord;
        }

        rootPayloadStorageBytes = checked((int)rootPayloadBytes);
        return EbinWriteStatus.Complete;
    }

    internal static EbinWriteStatus Write(
        WorldPersistenceWorldRecordSources sources,
        WorldPersistenceWorldRecordWorkspaceRequirements workspace,
        EbinReadLimits limits,
        scoped Span<byte> rootPayloadStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        bytesWritten = 0;
        ValidateNonoverlappingOutputs(sources, candidateStorage, destination);
        var status = Measure(sources, limits, rootPayloadStorage, out var measuredWorkspace, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (!workspace.Matches(measuredWorkspace))
        {
            diagnostic = CreateDiagnostic("world-workspace-mismatch", checked((ulong)workspace.RecordBytes), checked((ulong)measuredWorkspace.RecordBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        if ((candidateStorage.Length != workspace.RecordBytes)
            || (destination.Length != workspace.RecordBytes))
        {
            throw new ArgumentException("The state/world record workspaces must match their measured extent.");
        }

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[FieldCapacity];
        status = BuildFields(sources, limits, fields, rootPayloadStorage, out var fieldCount, out var payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var source = new EbinRecordWriteView(
            s_schemaId,
            major: 1,
            minor: 0,
            fields[..fieldCount],
            rootPayloadStorage[..payloadBytes]);
        return EbinStructuralWriter.Write(
            in source,
            limits,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    private static void AddArrayField(
        uint tag,
        int count,
        scoped ReadOnlySpan<byte> payload,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> rootPayloadStorage,
        ref int payloadBytes)
    {
        payload.CopyTo(rootPayloadStorage[payloadBytes..]);
        fields[fieldCount++] = new EbinFieldSource(
            tag,
            24,
            1,
            checked((ulong)count),
            new EbinPayloadSlice(payloadBytes, payload.Length));
        payloadBytes = checked(payloadBytes + payload.Length);
    }

    private static void AddRecordField(
        uint tag,
        scoped ReadOnlySpan<byte> record,
        bool isRequired,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> rootPayloadStorage,
        ref int payloadBytes)
    {
        record.CopyTo(rootPayloadStorage[payloadBytes..]);
        fields[fieldCount++] = new EbinFieldSource(
            tag,
            22,
            isRequired ? (ushort)1 : (ushort)0,
            1,
            new EbinPayloadSlice(payloadBytes, record.Length));
        payloadBytes = checked(payloadBytes + record.Length);
    }

    private static EbinWriteStatus BuildFields(
        WorldPersistenceWorldRecordSources sources,
        EbinReadLimits limits,
        Span<EbinFieldSource> fields,
        Span<byte> rootPayloadStorage,
        out int fieldCount,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        fields.Clear();
        rootPayloadStorage.Clear();
        fieldCount = 0;
        payloadBytes = 0;
        AddRecordField(1, sources.ConfigurationRecord, isRequired: true, fields, ref fieldCount, rootPayloadStorage, ref payloadBytes);
        AddRecordField(2, sources.ClockRecord, isRequired: true, fields, ref fieldCount, rootPayloadStorage, ref payloadBytes);

        if (!sources.Frame2Record.IsEmpty)
        {
            AddRecordField(3, sources.Frame2Record, isRequired: false, fields, ref fieldCount, rootPayloadStorage, ref payloadBytes);
        }

        AddRecordField(4, sources.Frame3Record, isRequired: true, fields, ref fieldCount, rootPayloadStorage, ref payloadBytes);
        AddRecordField(5, sources.EcsRecord, isRequired: true, fields, ref fieldCount, rootPayloadStorage, ref payloadBytes);
        AddRecordField(6, sources.ScenesRecord, isRequired: true, fields, ref fieldCount, rootPayloadStorage, ref payloadBytes);
        var assetIdStatus = EbinPayloadWriter.WriteFixedArray(
            sources.AssetIds,
            limits,
            rootPayloadStorage[payloadBytes..],
            out var assetIdPayloadBytes,
            out diagnostic);

        if (assetIdStatus is not EbinWriteStatus.Complete)
        {
            return assetIdStatus;
        }

        fields[fieldCount++] = new EbinFieldSource(
            7,
            23,
            1,
            checked((ulong)sources.AssetIds.Length),
            new EbinPayloadSlice(payloadBytes, assetIdPayloadBytes));
        payloadBytes = checked(payloadBytes + assetIdPayloadBytes);
        AddArrayField(8, sources.ControlRecordCount, sources.ControlsArrayPayload, fields, ref fieldCount, rootPayloadStorage, ref payloadBytes);
        AddRecordField(9, sources.CursorsRecord, isRequired: true, fields, ref fieldCount, rootPayloadStorage, ref payloadBytes);
        AddArrayField(10, sources.RandomStreamCount, sources.RandomArrayPayload, fields, ref fieldCount, rootPayloadStorage, ref payloadBytes);

        if (!sources.StrictLayoutRecord.IsEmpty)
        {
            AddRecordField(11, sources.StrictLayoutRecord, isRequired: false, fields, ref fieldCount, rootPayloadStorage, ref payloadBytes);
        }

        AddRecordField(12, sources.RegistrationRecord, isRequired: true, fields, ref fieldCount, rootPayloadStorage, ref payloadBytes);
        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    private static EbinStructuralDiagnostic CreateDiagnostic(string category, ulong claim = 0, ulong limit = 1)
        => new EbinStructuralDiagnostic(
            s_schemaId,
            0,
            category,
            null,
            claim,
            limit,
            EbinStructuralDiagnosticSource.Construction);

    private static EbinWriteStatus ReadNextControlSequence(
        scoped ReadOnlySpan<byte> cursorsRecord,
        EbinReadLimits limits,
        out long nextControlSequence,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var assertions = new CursorSequenceReader();
        var status = EbinStructuralReader.Read(
            cursorsRecord,
            limits,
            EbinReadPolicy.Canonical,
            ref assertions,
            out var bytesConsumed,
            out var readDiagnostic);

        if ((status is not EbinReadStatus.Complete) || (bytesConsumed != cursorsRecord.Length))
        {
            nextControlSequence = 0;
            diagnostic = readDiagnostic is { } value
                ? new EbinStructuralDiagnostic(
                    value.SchemaId,
                    value.Tag,
                    value.Category ?? throw new InvalidOperationException("The EBIN reader omitted a failure category."),
                    (value.Offset == 0) ? null : value.Offset,
                    value.Claim,
                    value.Limit,
                    EbinStructuralDiagnosticSource.EncodedInput)
                : CreateDiagnostic("cursor-record-incomplete");
            return (status is EbinReadStatus.UnsupportedSchema)
                ? EbinWriteStatus.UnsupportedSchema
                : EbinWriteStatus.InvalidRecord;
        }

        if (!assertions.IsValid)
        {
            nextControlSequence = 0;
            diagnostic = CreateDiagnostic("cursor-next-control-sequence");
            return EbinWriteStatus.InvalidRecord;
        }

        nextControlSequence = assertions.NextControlSequence;
        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus ValidateArrayPayload(
        scoped ReadOnlySpan<byte> payload,
        int count,
        EbinReadLimits limits,
        string category,
        out EbinStructuralDiagnostic? diagnostic)
    {
        if (payload.Length < 16)
        {
            diagnostic = CreateDiagnostic($"{category}-extent", checked((ulong)payload.Length), 16);
            return EbinWriteStatus.InvalidRecord;
        }

        var actualCount = BinaryPrimitives.ReadUInt64LittleEndian(payload);

        if (actualCount != checked((ulong)count))
        {
            diagnostic = CreateDiagnostic($"{category}-count", actualCount, checked((ulong)count));
            return EbinWriteStatus.InvalidRecord;
        }

        return WorldPersistenceRecordArrayLayout.MeasurePayloadBytes(
            count,
            0,
            limits,
            out _,
            out diagnostic);
    }

    private static void ValidateNonoverlappingOutputs(
        WorldPersistenceWorldRecordSources sources,
        scoped ReadOnlySpan<byte> candidateStorage,
        scoped ReadOnlySpan<byte> destination)
    {
        var assetIds = MemoryMarshal.AsBytes(sources.AssetIds);

        if (candidateStorage.Overlaps(destination)
            || candidateStorage.Overlaps(assetIds)
            || destination.Overlaps(assetIds)
            || candidateStorage.Overlaps(sources.ConfigurationRecord)
            || candidateStorage.Overlaps(sources.ClockRecord)
            || candidateStorage.Overlaps(sources.ControlsArrayPayload)
            || candidateStorage.Overlaps(sources.CursorsRecord)
            || candidateStorage.Overlaps(sources.EcsRecord)
            || candidateStorage.Overlaps(sources.Frame2Record)
            || candidateStorage.Overlaps(sources.Frame3Record)
            || candidateStorage.Overlaps(sources.RandomArrayPayload)
            || candidateStorage.Overlaps(sources.RegistrationRecord)
            || candidateStorage.Overlaps(sources.ScenesRecord)
            || candidateStorage.Overlaps(sources.StrictLayoutRecord)
            || destination.Overlaps(sources.ConfigurationRecord)
            || destination.Overlaps(sources.ClockRecord)
            || destination.Overlaps(sources.ControlsArrayPayload)
            || destination.Overlaps(sources.CursorsRecord)
            || destination.Overlaps(sources.EcsRecord)
            || destination.Overlaps(sources.Frame2Record)
            || destination.Overlaps(sources.Frame3Record)
            || destination.Overlaps(sources.RandomArrayPayload)
            || destination.Overlaps(sources.RegistrationRecord)
            || destination.Overlaps(sources.ScenesRecord)
            || destination.Overlaps(sources.StrictLayoutRecord))
        {
            throw new ArgumentException("The state/world output buffers must not overlap their sources or each other.");
        }
    }

    private static void ValidateNonoverlappingPayloads(
        WorldPersistenceWorldRecordSources sources,
        scoped ReadOnlySpan<byte> rootPayloadStorage)
    {
        var assetIds = MemoryMarshal.AsBytes(sources.AssetIds);

        if (rootPayloadStorage.Overlaps(assetIds)
            || rootPayloadStorage.Overlaps(sources.ConfigurationRecord)
            || rootPayloadStorage.Overlaps(sources.ClockRecord)
            || rootPayloadStorage.Overlaps(sources.ControlsArrayPayload)
            || rootPayloadStorage.Overlaps(sources.CursorsRecord)
            || rootPayloadStorage.Overlaps(sources.EcsRecord)
            || rootPayloadStorage.Overlaps(sources.Frame2Record)
            || rootPayloadStorage.Overlaps(sources.Frame3Record)
            || rootPayloadStorage.Overlaps(sources.RandomArrayPayload)
            || rootPayloadStorage.Overlaps(sources.RegistrationRecord)
            || rootPayloadStorage.Overlaps(sources.ScenesRecord)
            || rootPayloadStorage.Overlaps(sources.StrictLayoutRecord))
        {
            throw new ArgumentException("The state/world root-payload workspace must not overlap its sources.", nameof(rootPayloadStorage));
        }
    }

    private static EbinWriteStatus ValidateRecord(
        scoped ReadOnlySpan<byte> record,
        string schemaName,
        int expectedFieldCount,
        EbinReadLimits limits,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var readStatus = EbinStructuralReader.Validate(
            record,
            limits,
            EbinReadPolicy.Canonical,
            out var bytesConsumed,
            out var readDiagnostic);

        if ((readStatus is not EbinReadStatus.Complete) || (bytesConsumed != record.Length))
        {
            diagnostic = readDiagnostic is { } value
                ? new EbinStructuralDiagnostic(
                    value.SchemaId,
                    value.Tag,
                    value.Category ?? throw new InvalidOperationException("The EBIN reader omitted a failure category."),
                    (value.Offset == 0) ? null : value.Offset,
                    value.Claim,
                    value.Limit,
                    EbinStructuralDiagnosticSource.EncodedInput)
                : CreateDiagnostic("nested-record-incomplete");
            return (readStatus is EbinReadStatus.UnsupportedSchema)
                ? EbinWriteStatus.UnsupportedSchema
                : EbinWriteStatus.InvalidRecord;
        }

        readStatus = EbinStructuralReader.ReadHeader(record, limits, out var header, out _, out readDiagnostic);

        if (readStatus is not EbinReadStatus.Complete)
        {
            diagnostic = readDiagnostic is { } value
                ? new EbinStructuralDiagnostic(
                    value.SchemaId,
                    value.Tag,
                    value.Category ?? throw new InvalidOperationException("The EBIN reader omitted a failure category."),
                    (value.Offset == 0) ? null : value.Offset,
                    value.Claim,
                    value.Limit,
                    EbinStructuralDiagnosticSource.EncodedInput)
                : CreateDiagnostic("nested-record-header");
            return (readStatus is EbinReadStatus.UnsupportedSchema)
                ? EbinWriteStatus.UnsupportedSchema
                : EbinWriteStatus.InvalidRecord;
        }

        var expectedSchemaId = new ContentSchemaIdentityAssignment(schemaName).SchemaId;

        if ((header.SchemaId != expectedSchemaId)
            || (header.Major != 1)
            || (header.Minor != 0)
            || ((expectedFieldCount > 0) && (header.FieldCount != expectedFieldCount))
            || (header.FieldCount == 0))
        {
            diagnostic = CreateDiagnostic("nested-record-schema", checked((ulong)header.FieldCount), checked((ulong)Math.Max(expectedFieldCount, 1)));
            return EbinWriteStatus.InvalidRecord;
        }

        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    private struct CursorSequenceReader : IAction<EbinRecordReadView>
    {
        internal bool IsValid { get; private set; }

        internal long NextControlSequence { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
        {
            var field = record.GetField(0);

            if ((field.Tag != 1)
                || (field.Kind != 8)
                || !field.IsRequired
                || (field.Payload.Length != sizeof(long)))
            {
                return;
            }

            NextControlSequence = BinaryPrimitives.ReadInt64LittleEndian(field.Payload);
            IsValid = true;
        }
    }
}
