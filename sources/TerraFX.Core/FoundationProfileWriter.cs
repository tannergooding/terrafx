// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

internal static class FoundationProfileWriter
{
    private const int FieldCount = 9;
    private static readonly Guid s_schemaId = FoundationCatalogue.ProfileId;

    internal static EbinWriteStatus Measure(
        SpatialProfileId id,
        SpatialSettings settings,
        EbinReadLimits limits,
        out int recordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[FieldCount];
        Span<byte> payloadStorage = stackalloc byte[160];
        var status = BuildFields(id, settings, fields, payloadStorage, out var payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            recordBytes = 0;
            return status;
        }

        var source = new EbinRecordWriteView(
            s_schemaId,
            major: 2,
            minor: 0,
            fields,
            payloadStorage[..payloadBytes]);
        return EbinStructuralWriter.Measure(in source, limits, out recordBytes, out diagnostic);
    }

    internal static EbinWriteStatus Write(
        SpatialProfileId id,
        SpatialSettings settings,
        EbinReadLimits limits,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[FieldCount];
        Span<byte> payloadStorage = stackalloc byte[160];
        var status = BuildFields(id, settings, fields, payloadStorage, out var payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            bytesWritten = 0;
            return status;
        }

        var source = new EbinRecordWriteView(
            s_schemaId,
            major: 2,
            minor: 0,
            fields,
            payloadStorage[..payloadBytes]);
        return EbinStructuralWriter.Write(
            in source,
            limits,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    private static EbinWriteStatus AddByteField(
        uint tag,
        byte value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadBytes..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(tag, 1, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus AddBytesField(
        uint tag,
        scoped ReadOnlySpan<byte> value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        if (value.Length > payloadStorage.Length - payloadBytes)
        {
            throw new InvalidOperationException("The Foundation profile payload workspace is too small.");
        }

        value.CopyTo(payloadStorage[payloadBytes..]);
        fields[fieldCount++] = new EbinFieldSource(tag, 13, 1, checked((ulong)value.Length), new EbinPayloadSlice(payloadBytes, value.Length));
        payloadBytes = checked(payloadBytes + value.Length);
        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus AddGuidField(
        uint tag,
        Guid value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadBytes..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(tag, 11, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus AddInt32Field(
        uint tag,
        int value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadBytes..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(tag, 7, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus AddUInt32Field(
        uint tag,
        uint value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadBytes..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(tag, 3, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus BuildFields(
        SpatialProfileId id,
        SpatialSettings settings,
        Span<EbinFieldSource> fields,
        Span<byte> payloadStorage,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        SpatialKernel.ValidateProfile(id, settings);
        fields.Clear();
        payloadStorage.Clear();
        payloadBytes = 0;
        Span<byte> settingsDigest = stackalloc byte[32];
        FoundationProfileBinding.ComputeSettingsDigest(settings, settingsDigest);
        var fieldCount = 0;

        var status = AddUInt32Field(1, 1, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddUInt32Field(2, 2, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddUInt32Field(3, 2, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddByteField(5, checked((byte)settings.ChunkSizeExponent), fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddBytesField(6, settingsDigest, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddBytesField(7, FoundationCatalogue.RegistryDigest, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddBytesField(8, FoundationCatalogue.KernelDigest, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddGuidField(9, id.Value, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        return AddInt32Field(
            10,
            settings.MetersPerUnitExponent,
            fields,
            ref fieldCount,
            payloadStorage,
            ref payloadBytes,
            out diagnostic);
    }
}
