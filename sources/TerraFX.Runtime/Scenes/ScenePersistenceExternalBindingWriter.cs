// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Scenes;

internal static class ScenePersistenceExternalBindingWriter
{
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("state/external-scene-binding").SchemaId;

    internal static EbinWriteStatus Measure(
        SceneExternalBindingCaptureRow row,
        EbinReadLimits limits,
        out int recordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var status = Validate(row, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            recordBytes = 0;
            return status;
        }

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[2];
        Span<byte> payloadStorage = stackalloc byte[32];
        status = BuildFields(row, fields, payloadStorage, out var fieldCount, out var payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            recordBytes = 0;
            return status;
        }

        var source = new EbinRecordWriteView(
            s_schemaId,
            major: 1,
            minor: 0,
            fields[..fieldCount],
            payloadStorage[..payloadBytes]);
        return EbinStructuralWriter.Measure(in source, limits, out recordBytes, out diagnostic);
    }

    internal static EbinWriteStatus Write(
        SceneExternalBindingCaptureRow row,
        EbinReadLimits limits,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = Measure(row, limits, out var recordBytes, out diagnostic);
        bytesWritten = 0;

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if ((candidateStorage.Length != recordBytes) || (destination.Length != recordBytes))
        {
            throw new ArgumentException("The external-scene-binding workspaces must match the measured extent.");
        }

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[2];
        Span<byte> payloadStorage = stackalloc byte[32];
        status = BuildFields(row, fields, payloadStorage, out var fieldCount, out var payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var source = new EbinRecordWriteView(
            s_schemaId,
            major: 1,
            minor: 0,
            fields[..fieldCount],
            payloadStorage[..payloadBytes]);
        return EbinStructuralWriter.Write(
            in source,
            limits,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    private static EbinWriteStatus BuildFields(
        SceneExternalBindingCaptureRow row,
        Span<EbinFieldSource> fields,
        Span<byte> payloadStorage,
        out int fieldCount,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        fields.Clear();
        payloadStorage.Clear();
        fieldCount = 0;
        payloadBytes = 0;
        var status = EbinPayloadWriter.Write(row.BindingId, payloadStorage, out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(1, 11, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);

        if (row.TargetEntityId is Guid targetEntityId)
        {
            status = EbinPayloadWriter.Write(targetEntityId, payloadStorage[payloadBytes..], out fieldBytes, out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            fields[fieldCount++] = new EbinFieldSource(2, 11, 0, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
            payloadBytes = checked(payloadBytes + fieldBytes);
        }

        diagnostic = null;
        return EbinWriteStatus.Complete;
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

    private static EbinWriteStatus Validate(SceneExternalBindingCaptureRow row, out EbinStructuralDiagnostic? diagnostic)
    {
        if ((row.BindingId == Guid.Empty)
            || (row.TargetEntityId is Guid targetEntityId && (targetEntityId == Guid.Empty)))
        {
            diagnostic = CreateDiagnostic("external-scene-binding-metadata");
            return EbinWriteStatus.InvalidRecord;
        }

        diagnostic = null;
        return EbinWriteStatus.Complete;
    }
}
