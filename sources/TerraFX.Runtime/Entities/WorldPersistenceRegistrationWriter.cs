// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceRegistrationWriter
{
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("state/registration").SchemaId;

    internal static EbinWriteStatus Measure(
        WorldRegistration registration,
        scoped ReadOnlySpan<byte> componentsArrayPayload,
        scoped ReadOnlySpan<byte> tagsArrayPayload,
        scoped ReadOnlySpan<byte> systemsArrayPayload,
        scoped ReadOnlySpan<byte> resourcesArrayPayload,
        EbinReadLimits limits,
        scoped Span<byte> rootPayloadStorage,
        out int rootPayloadBytes,
        out int recordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(limits);
        var status = MeasureRootPayload(
            registration,
            componentsArrayPayload,
            tagsArrayPayload,
            systemsArrayPayload,
            resourcesArrayPayload,
            limits,
            out rootPayloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            recordBytes = 0;
            return status;
        }

        if (rootPayloadStorage.Length != rootPayloadBytes)
        {
            throw new ArgumentException("The state/registration root-payload workspace must match its measured extent.", nameof(rootPayloadStorage));
        }

        ValidateNonoverlappingPayloads(
            componentsArrayPayload,
            tagsArrayPayload,
            systemsArrayPayload,
            resourcesArrayPayload,
            rootPayloadStorage);
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[4];
        BuildFields(
            registration,
            componentsArrayPayload,
            tagsArrayPayload,
            systemsArrayPayload,
            resourcesArrayPayload,
            fields,
            rootPayloadStorage);
        var source = new EbinRecordWriteView(s_schemaId, 1, 0, fields, rootPayloadStorage);
        return EbinStructuralWriter.Measure(in source, limits, out recordBytes, out diagnostic);
    }

    internal static EbinWriteStatus MeasureWorkspace(
        WorldRegistration registration,
        scoped ReadOnlySpan<byte> componentsArrayPayload,
        scoped ReadOnlySpan<byte> tagsArrayPayload,
        scoped ReadOnlySpan<byte> systemsArrayPayload,
        scoped ReadOnlySpan<byte> resourcesArrayPayload,
        EbinReadLimits limits,
        scoped Span<byte> rootPayloadStorage,
        out WorldPersistenceRegistrationWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = Measure(
            registration,
            componentsArrayPayload,
            tagsArrayPayload,
            systemsArrayPayload,
            resourcesArrayPayload,
            limits,
            rootPayloadStorage,
            out var rootPayloadBytes,
            out var recordBytes,
            out diagnostic);
        workspace = default;

        if (status is EbinWriteStatus.Complete)
        {
            workspace = new WorldPersistenceRegistrationWorkspaceRequirements(
                componentsArrayPayload.Length,
                tagsArrayPayload.Length,
                systemsArrayPayload.Length,
                resourcesArrayPayload.Length,
                rootPayloadBytes,
                recordBytes);
        }

        return status;
    }

    internal static EbinWriteStatus Write(
        WorldRegistration registration,
        scoped ReadOnlySpan<byte> componentsArrayPayload,
        scoped ReadOnlySpan<byte> tagsArrayPayload,
        scoped ReadOnlySpan<byte> systemsArrayPayload,
        scoped ReadOnlySpan<byte> resourcesArrayPayload,
        EbinReadLimits limits,
        scoped Span<byte> rootPayloadStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = Measure(
            registration,
            componentsArrayPayload,
            tagsArrayPayload,
            systemsArrayPayload,
            resourcesArrayPayload,
            limits,
            rootPayloadStorage,
            out _,
            out var recordBytes,
            out diagnostic);
        bytesWritten = 0;

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if ((candidateStorage.Length != recordBytes) || (destination.Length != recordBytes))
        {
            throw new ArgumentException("The state/registration root record workspaces must match the measured extent.");
        }

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[4];
        BuildFields(
            registration,
            componentsArrayPayload,
            tagsArrayPayload,
            systemsArrayPayload,
            resourcesArrayPayload,
            fields,
            rootPayloadStorage);
        var source = new EbinRecordWriteView(s_schemaId, 1, 0, fields, rootPayloadStorage);
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
        int fieldIndex,
        Span<byte> rootPayloadStorage,
        ref int payloadOffset)
    {
        payload.CopyTo(rootPayloadStorage[payloadOffset..]);
        fields[fieldIndex] = new EbinFieldSource(
            tag,
            24,
            1,
            checked((ulong)count),
            new EbinPayloadSlice(payloadOffset, payload.Length));
        payloadOffset = checked(payloadOffset + payload.Length);
    }

    private static void BuildFields(
        WorldRegistration registration,
        scoped ReadOnlySpan<byte> componentsArrayPayload,
        scoped ReadOnlySpan<byte> tagsArrayPayload,
        scoped ReadOnlySpan<byte> systemsArrayPayload,
        scoped ReadOnlySpan<byte> resourcesArrayPayload,
        Span<EbinFieldSource> fields,
        Span<byte> rootPayloadStorage)
    {
        fields.Clear();
        rootPayloadStorage.Clear();
        var payloadOffset = 0;
        AddArrayField(1, registration.Components.Length, componentsArrayPayload, fields, 0, rootPayloadStorage, ref payloadOffset);
        AddArrayField(2, registration.Tags.Length, tagsArrayPayload, fields, 1, rootPayloadStorage, ref payloadOffset);
        AddArrayField(3, registration.Systems.Length, systemsArrayPayload, fields, 2, rootPayloadStorage, ref payloadOffset);
        AddArrayField(4, registration.Resources.Length, resourcesArrayPayload, fields, 3, rootPayloadStorage, ref payloadOffset);
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

    private static EbinWriteStatus MeasureRootPayload(
        WorldRegistration registration,
        scoped ReadOnlySpan<byte> componentsArrayPayload,
        scoped ReadOnlySpan<byte> tagsArrayPayload,
        scoped ReadOnlySpan<byte> systemsArrayPayload,
        scoped ReadOnlySpan<byte> resourcesArrayPayload,
        EbinReadLimits limits,
        out int rootPayloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        rootPayloadBytes = 0;
        diagnostic = null;
        var status = ValidateArrayCount(
            componentsArrayPayload,
            registration.Components.Length,
            "component-registration-array",
            limits,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = ValidateArrayCount(
            tagsArrayPayload,
            registration.Tags.Length,
            "tag-registration-array",
            limits,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = ValidateArrayCount(
            systemsArrayPayload,
            registration.Systems.Length,
            "system-registration-array",
            limits,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = ValidateArrayCount(
            resourcesArrayPayload,
            registration.Resources.Length,
            "resource-registration-array",
            limits,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var totalBytes = checked(
            (long)componentsArrayPayload.Length
            + tagsArrayPayload.Length
            + systemsArrayPayload.Length
            + resourcesArrayPayload.Length);

        if ((totalBytes > int.MaxValue) || (totalBytes > limits.MaximumRecordBytes))
        {
            diagnostic = CreateDiagnostic(
                "record-byte-budget",
                checked((ulong)totalBytes),
                checked((ulong)limits.MaximumRecordBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        rootPayloadBytes = checked((int)totalBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus ValidateArrayCount(
        scoped ReadOnlySpan<byte> payload,
        int expectedCount,
        string category,
        EbinReadLimits limits,
        out EbinStructuralDiagnostic? diagnostic)
    {
        if ((payload.Length < 16)
            || (BinaryPrimitives.ReadUInt64LittleEndian(payload) != checked((ulong)expectedCount)))
        {
            diagnostic = CreateDiagnostic(category, payload.Length >= sizeof(ulong)
                ? BinaryPrimitives.ReadUInt64LittleEndian(payload)
                : 0, checked((ulong)expectedCount));
            return EbinWriteStatus.InvalidRecord;
        }

        if ((expectedCount > limits.MaximumArrayElements)
            || (expectedCount > limits.MaximumAggregateArrayElements))
        {
            diagnostic = CreateDiagnostic(
                "array-count-budget",
                checked((ulong)expectedCount),
                checked((ulong)Math.Min(limits.MaximumArrayElements, limits.MaximumAggregateArrayElements)));
            return EbinWriteStatus.InvalidRecord;
        }

        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    private static void ValidateNonoverlappingPayloads(
        scoped ReadOnlySpan<byte> componentsArrayPayload,
        scoped ReadOnlySpan<byte> tagsArrayPayload,
        scoped ReadOnlySpan<byte> systemsArrayPayload,
        scoped ReadOnlySpan<byte> resourcesArrayPayload,
        scoped Span<byte> rootPayloadStorage)
    {
        if (rootPayloadStorage.Overlaps(componentsArrayPayload)
            || rootPayloadStorage.Overlaps(tagsArrayPayload)
            || rootPayloadStorage.Overlaps(systemsArrayPayload)
            || rootPayloadStorage.Overlaps(resourcesArrayPayload))
        {
            throw new ArgumentException("The state/registration root payload cannot overlap its source arrays.", nameof(rootPayloadStorage));
        }
    }
}
