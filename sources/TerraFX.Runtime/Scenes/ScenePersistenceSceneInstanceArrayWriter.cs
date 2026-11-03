// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using TerraFX;

namespace TerraFX.Scenes;

internal static class ScenePersistenceSceneInstanceArrayWriter
{
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("state/scene-instance").SchemaId;

    internal static EbinWriteStatus Measure(
        scoped ReadOnlySpan<SceneInstance> instances,
        scoped ReadOnlySpan<EbinPayloadSlice> recordsByInstance,
        scoped ReadOnlySpan<byte> recordStorage,
        EbinReadLimits limits,
        scoped Span<int> orderScratch,
        scoped Span<EbinPayloadSlice> orderedRecords,
        out ScenePersistenceSceneInstanceArrayWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        workspace = default;
        diagnostic = null;
        ValidateWorkspaceLengths(instances, recordsByInstance, orderScratch, orderedRecords);
        ValidateMeasurementBuffers(recordsByInstance, recordStorage, orderScratch, orderedRecords);

        if ((instances.Length > 65_536)
            || (instances.Length > limits.MaximumArrayElements)
            || (instances.Length > limits.MaximumAggregateArrayElements))
        {
            diagnostic = CreateDiagnostic(
                "array-count-budget",
                checked((ulong)instances.Length),
                checked((ulong)Math.Min(65_536, Math.Min(limits.MaximumArrayElements, limits.MaximumAggregateArrayElements))));
            return EbinWriteStatus.InvalidRecord;
        }

        var storageOffset = 0;

        for (var index = 0; index < instances.Length; index++)
        {
            if (instances[index] is not { } instance)
            {
                return InvalidRecord("scene-instance-array-null", out diagnostic);
            }

            var sceneInstanceId = instance.SceneInstanceId;
            var slice = recordsByInstance[index];

            if ((sceneInstanceId == Guid.Empty)
                || (slice.ByteLength <= 0)
                || (slice.Offset != storageOffset)
                || (slice.Offset > recordStorage.Length)
                || (slice.ByteLength > (recordStorage.Length - slice.Offset)))
            {
                return InvalidRecord("scene-instance-array-member", out diagnostic);
            }

            var encodedRecord = recordStorage.Slice(slice.Offset, slice.ByteLength);
            var assertions = new SceneInstanceIdentityAssertions(sceneInstanceId);
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
                if (readStatus is EbinReadStatus.Complete)
                {
                    return InvalidRecord("scene-instance-array-record-size", out diagnostic);
                }

                return MapReadFailure(readStatus, readDiagnostic, out diagnostic);
            }

            if (!assertions.IsMatch)
            {
                return InvalidRecord("scene-instance-array-identity", out diagnostic);
            }

            orderScratch[index] = index;
            storageOffset = checked(storageOffset + slice.ByteLength);
        }

        if (storageOffset != recordStorage.Length)
        {
            return InvalidRecord("scene-instance-array-record-storage", out diagnostic);
        }

        for (var index = 1; index < orderScratch.Length; index++)
        {
            var instanceIndex = orderScratch[index];
            var instanceId = instances[instanceIndex].SceneInstanceId;
            var insertionIndex = index;

            while ((insertionIndex > 0)
                && (FoundationCatalogue.CompareRfc(
                    instances[orderScratch[insertionIndex - 1]].SceneInstanceId,
                    instanceId) > 0))
            {
                orderScratch[insertionIndex] = orderScratch[insertionIndex - 1];
                insertionIndex--;
            }

            orderScratch[insertionIndex] = instanceIndex;
        }

        for (var index = 0; index < orderedRecords.Length; index++)
        {
            if ((index > 0)
                && (FoundationCatalogue.CompareRfc(
                    instances[orderScratch[index - 1]].SceneInstanceId,
                    instances[orderScratch[index]].SceneInstanceId) >= 0))
            {
                return InvalidRecord("scene-instance-array-order", out diagnostic);
            }

            orderedRecords[index] = recordsByInstance[orderScratch[index]];
        }

        var status = EbinPayloadWriter.MeasureRecordArray(
            orderedRecords,
            recordStorage,
            limits,
            out var arrayPayloadBytes,
            out diagnostic);

        if (status is EbinWriteStatus.Complete)
        {
            workspace = new ScenePersistenceSceneInstanceArrayWorkspaceRequirements(
                instances.Length,
                recordStorage.Length,
                arrayPayloadBytes);
        }

        return status;
    }

    internal static EbinWriteStatus Write(
        scoped ReadOnlySpan<SceneInstance> instances,
        scoped ReadOnlySpan<EbinPayloadSlice> recordsByInstance,
        scoped ReadOnlySpan<byte> recordStorage,
        EbinReadLimits limits,
        ScenePersistenceSceneInstanceArrayWorkspaceRequirements workspace,
        scoped Span<int> orderScratch,
        scoped Span<EbinPayloadSlice> orderedRecords,
        scoped Span<byte> arrayPayloadDestination,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ValidateNonoverlappingBuffers(
            recordsByInstance,
            recordStorage,
            orderScratch,
            orderedRecords,
            arrayPayloadDestination);
        var status = Measure(
            instances,
            recordsByInstance,
            recordStorage,
            limits,
            orderScratch,
            orderedRecords,
            out var measuredWorkspace,
            out diagnostic);
        payloadBytes = 0;

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (!workspace.Matches(measuredWorkspace))
        {
            diagnostic = CreateDiagnostic("scene-instance-array-workspace-mismatch");
            return EbinWriteStatus.InvalidRecord;
        }

        if (arrayPayloadDestination.Length != workspace.ArrayPayloadBytes)
        {
            throw new ArgumentException("The state/scene-instance array destination must match its measured extent.", nameof(arrayPayloadDestination));
        }

        status = EbinPayloadWriter.WriteRecordArray(
            orderedRecords,
            recordStorage,
            limits,
            arrayPayloadDestination,
            out payloadBytes,
            out diagnostic);

        if ((status is EbinWriteStatus.Complete) && (payloadBytes != workspace.ArrayPayloadBytes))
        {
            diagnostic = CreateDiagnostic("scene-instance-array-size-mismatch");
            payloadBytes = 0;
            return EbinWriteStatus.InvalidRecord;
        }

        return status;
    }

    private static EbinStructuralDiagnostic CreateDiagnostic(string category, ulong claim = 0, ulong limit = 1)
        => new EbinStructuralDiagnostic(
            s_schemaId,
            1,
            category,
            null,
            claim,
            limit,
            EbinStructuralDiagnosticSource.Construction);

    private static EbinWriteStatus InvalidRecord(string category, out EbinStructuralDiagnostic? diagnostic)
    {
        diagnostic = CreateDiagnostic(category);
        return EbinWriteStatus.InvalidRecord;
    }

    private static EbinWriteStatus MapReadFailure(
        EbinReadStatus status,
        EbinReadDiagnostic? readDiagnostic,
        out EbinStructuralDiagnostic? diagnostic)
    {
        diagnostic = readDiagnostic is EbinReadDiagnostic value
            ? new EbinStructuralDiagnostic(
                value.SchemaId,
                value.Tag,
                value.Category,
                (value.Offset == 0) ? null : value.Offset,
                value.Claim,
                value.Limit,
                EbinStructuralDiagnosticSource.EncodedInput)
            : CreateDiagnostic("scene-instance-array-record");
        return (status is EbinReadStatus.UnsupportedSchema)
            ? EbinWriteStatus.UnsupportedSchema
            : EbinWriteStatus.InvalidRecord;
    }

    private static void ValidateMeasurementBuffers(
        scoped ReadOnlySpan<EbinPayloadSlice> recordsByInstance,
        scoped ReadOnlySpan<byte> recordStorage,
        scoped ReadOnlySpan<int> orderScratch,
        scoped ReadOnlySpan<EbinPayloadSlice> orderedRecords)
    {
        var inputSlices = MemoryMarshal.AsBytes(recordsByInstance);
        var orderedSliceBytes = MemoryMarshal.AsBytes(orderedRecords);
        var orderBytes = MemoryMarshal.AsBytes(orderScratch);

        if (inputSlices.Overlaps(orderedSliceBytes)
            || inputSlices.Overlaps(orderBytes)
            || inputSlices.Overlaps(recordStorage)
            || orderedSliceBytes.Overlaps(orderBytes)
            || orderedSliceBytes.Overlaps(recordStorage)
            || orderBytes.Overlaps(recordStorage))
        {
            throw new ArgumentException("The state/scene-instance array measurement workspaces must not overlap.");
        }
    }

    private static void ValidateNonoverlappingBuffers(
        scoped ReadOnlySpan<EbinPayloadSlice> recordsByInstance,
        scoped ReadOnlySpan<byte> recordStorage,
        scoped ReadOnlySpan<int> orderScratch,
        scoped ReadOnlySpan<EbinPayloadSlice> orderedRecords,
        scoped ReadOnlySpan<byte> arrayPayloadDestination)
    {
        var inputSlices = MemoryMarshal.AsBytes(recordsByInstance);
        var orderedSliceBytes = MemoryMarshal.AsBytes(orderedRecords);
        var orderBytes = MemoryMarshal.AsBytes(orderScratch);

        if (inputSlices.Overlaps(orderedSliceBytes)
            || inputSlices.Overlaps(orderBytes)
            || inputSlices.Overlaps(recordStorage)
            || inputSlices.Overlaps(arrayPayloadDestination)
            || orderedSliceBytes.Overlaps(orderBytes)
            || orderedSliceBytes.Overlaps(recordStorage)
            || orderedSliceBytes.Overlaps(arrayPayloadDestination)
            || orderBytes.Overlaps(recordStorage)
            || orderBytes.Overlaps(arrayPayloadDestination)
            || recordStorage.Overlaps(arrayPayloadDestination))
        {
            throw new ArgumentException("The state/scene-instance array workspaces must not overlap.");
        }
    }

    private static void ValidateWorkspaceLengths(
        scoped ReadOnlySpan<SceneInstance> instances,
        scoped ReadOnlySpan<EbinPayloadSlice> recordsByInstance,
        scoped ReadOnlySpan<int> orderScratch,
        scoped ReadOnlySpan<EbinPayloadSlice> orderedRecords)
    {
        if ((recordsByInstance.Length != instances.Length)
            || (orderScratch.Length != instances.Length)
            || (orderedRecords.Length != instances.Length))
        {
            throw new ArgumentException("The state/scene-instance array workspaces must match the exact active instance count.");
        }
    }

    private struct SceneInstanceIdentityAssertions : IAction<EbinRecordReadView>
    {
        private readonly Guid _expectedSceneInstanceId;

        internal SceneInstanceIdentityAssertions(Guid expectedSceneInstanceId)
        {
            _expectedSceneInstanceId = expectedSceneInstanceId;
            IsMatch = false;
        }

        internal bool IsMatch { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
        {
            IsMatch = (record.Header.SchemaId == s_schemaId)
                && (record.Header.Major == 1)
                && (record.Header.Minor == 0)
                && (record.FieldCount == 7);

            if (!IsMatch)
            {
                return;
            }

            var sceneInstanceId = record.GetField(0);
            IsMatch = (sceneInstanceId.Tag == 1)
                && (sceneInstanceId.Kind == 11)
                && sceneInstanceId.IsRequired
                && (sceneInstanceId.Count == 1)
                && (sceneInstanceId.Payload.Length == 16)
                && (new Guid(sceneInstanceId.Payload, bigEndian: true) == _expectedSceneInstanceId);
        }
    }
}
