// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Content.Tools;

internal static partial class ContentPackageWriter
{
    private const int DirectoryEntryBytes = 32;
    private const int DirectoryHeaderBytes = 64;
    private const int MaximumPackageBytes = 2_147_483_584;
    private const int PackageHeaderBytes = 64;
    private static readonly EbinReadLimits s_assetRecordLimits = new(
        268_435_456,
        64,
        64,
        1_048_576,
        1_048_576,
        1_048_576,
        1_048_576,
        1_048_576);
    private static readonly ContentSchemaIdentityAssignment s_envelopeSchema = new("native/envelope");
    private static readonly ContentSchemaIdentityAssignment s_profileSchema = new("foundation/profile");

    internal static EbinReadLimits AssetRecordLimits => s_assetRecordLimits;

    internal static byte[] CreatePackage(
        scoped ReadOnlySpan<byte> profileRecord,
        scoped ReadOnlySpan<ContentPackageAsset> assets)
        => CreatePackageCore(profileRecord, assets, nativeAssetSchemas: null);

    internal static byte[] EncodeFixedArray(scoped ReadOnlySpan<Vector2> values)
    {
        var payload = new byte[checked(32 + checked(values.Length * 8))];
        var status = EbinPayloadWriter.WriteFixedArray(
            values,
            s_assetRecordLimits,
            payload,
            out var payloadBytes,
            out var diagnostic);
        return CompleteFixedArray(status, payload, payloadBytes, diagnostic);
    }

    internal static byte[] EncodeFixedArray(scoped ReadOnlySpan<Vector3> values)
    {
        var payload = new byte[checked(32 + checked(values.Length * 12))];
        var status = EbinPayloadWriter.WriteFixedArray(
            values,
            s_assetRecordLimits,
            payload,
            out var payloadBytes,
            out var diagnostic);
        return CompleteFixedArray(status, payload, payloadBytes, diagnostic);
    }

    internal static byte[] EncodeFixedArray(scoped ReadOnlySpan<uint> values)
    {
        var payload = new byte[checked(32 + checked(values.Length * sizeof(uint)))];
        var status = EbinPayloadWriter.WriteFixedArray(
            values,
            s_assetRecordLimits,
            payload,
            out var payloadBytes,
            out var diagnostic);
        return CompleteFixedArray(status, payload, payloadBytes, diagnostic);
    }

    internal static byte[] EncodeFixedArray(scoped ReadOnlySpan<ushort> values)
    {
        var payload = new byte[checked(32 + checked(values.Length * sizeof(ushort)))];
        var status = EbinPayloadWriter.WriteFixedArray(
            values,
            s_assetRecordLimits,
            payload,
            out var payloadBytes,
            out var diagnostic);
        return CompleteFixedArray(status, payload, payloadBytes, diagnostic);
    }

    internal static byte[] EncodeRecord(
        Guid schemaId,
        int major,
        int minor,
        scoped ReadOnlySpan<ContentRecordField> fields)
    {
        var sortedFields = fields.ToArray();
        Array.Sort(sortedFields, static (left, right) => left.Tag.CompareTo(right.Tag));
        var payloadBytes = 0;

        for (var index = 0; index < sortedFields.Length; index++)
        {
            ArgumentNullException.ThrowIfNull(sortedFields[index].Payload);

            if ((index != 0) && (sortedFields[index - 1].Tag == sortedFields[index].Tag))
            {
                throw new InvalidDataException("A generated EBIN record contains a duplicate field tag.");
            }

            payloadBytes = checked(payloadBytes + sortedFields[index].Payload.Length);
        }

        var payloadStorage = new byte[payloadBytes];
        var fieldSources = new EbinFieldSource[sortedFields.Length];
        var payloadOffset = 0;

        for (var index = 0; index < sortedFields.Length; index++)
        {
            var field = sortedFields[index];
            field.Payload.CopyTo(payloadStorage, payloadOffset);
            fieldSources[index] = new EbinFieldSource(
                field.Tag,
                field.Kind,
                flags: 1,
                field.Count,
                new EbinPayloadSlice(payloadOffset, field.Payload.Length));
            payloadOffset = checked(payloadOffset + field.Payload.Length);
        }

        return EncodeRecord(schemaId, major, minor, fieldSources, payloadStorage, s_assetRecordLimits);
    }

    internal static byte[] EncodeRecordArray(scoped ReadOnlySpan<byte[]> records)
    {
        var totalChildBytes = 0;

        for (var index = 0; index < records.Length; index++)
        {
            ArgumentNullException.ThrowIfNull(records[index]);
            totalChildBytes = checked(totalChildBytes + records[index].Length);
        }

        var childStorage = new byte[totalChildBytes];
        var slices = new EbinPayloadSlice[records.Length];
        var offset = 0;

        for (var index = 0; index < records.Length; index++)
        {
            var child = records[index];
            child.CopyTo(childStorage, offset);
            slices[index] = new EbinPayloadSlice(offset, child.Length);
            offset = checked(offset + child.Length);
        }

        var status = EbinPayloadWriter.MeasureRecordArray(
            slices,
            childStorage,
            s_assetRecordLimits,
            out var payloadBytes,
            out var diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            throw new InvalidDataException($"The generated EBIN record array is invalid: {diagnostic?.Category ?? status.ToString()}.");
        }

        var payload = new byte[payloadBytes];
        status = EbinPayloadWriter.WriteRecordArray(
            slices,
            childStorage,
            s_assetRecordLimits,
            payload,
            out var bytesWritten,
            out diagnostic);

        if ((status is not EbinWriteStatus.Complete) || (bytesWritten != payloadBytes))
        {
            throw new InvalidDataException($"The generated EBIN record array could not be written: {diagnostic?.Category ?? status.ToString()}.");
        }

        return payload;
    }

    internal static byte[] EncodeSingle(float value)
    {
        Span<byte> payload = stackalloc byte[sizeof(float)];
        WritePayload(
            static (float item, Span<byte> destination, out int bytes, out EbinStructuralDiagnostic? diagnostic)
                => EbinPayloadWriter.Write(item, destination, out bytes, out diagnostic),
            value,
            payload,
            out var payloadBytes);
        return payload[..payloadBytes].ToArray();
    }

    internal static byte[] EncodeUInt32(uint value)
    {
        Span<byte> payload = stackalloc byte[sizeof(uint)];
        WritePayload(
            static (uint item, Span<byte> destination, out int bytes, out EbinStructuralDiagnostic? diagnostic)
                => EbinPayloadWriter.Write(item, destination, out bytes, out diagnostic),
            value,
            payload,
            out var payloadBytes);
        return payload[..payloadBytes].ToArray();
    }

    internal static byte[] EncodeUInt8(byte value)
    {
        Span<byte> payload = stackalloc byte[1];
        WritePayload(
            static (byte item, Span<byte> destination, out int bytes, out EbinStructuralDiagnostic? diagnostic)
                => EbinPayloadWriter.Write(item, destination, out bytes, out diagnostic),
            value,
            payload,
            out var payloadBytes);
        return payload[..payloadBytes].ToArray();
    }

    internal static byte[] EncodeVector3(Vector3 value)
    {
        Span<byte> payload = stackalloc byte[3 * sizeof(float)];
        WritePayload(
            static (Vector3 item, Span<byte> destination, out int bytes, out EbinStructuralDiagnostic? diagnostic)
                => EbinPayloadWriter.Write(item, destination, out bytes, out diagnostic),
            value,
            payload,
            out var payloadBytes);
        return payload[..payloadBytes].ToArray();
    }

    internal static Guid ValidateRecord(
        scoped ReadOnlySpan<byte> record,
        Guid? expectedSchemaId,
        int major,
        int minor,
        string kind,
        EbinReadLimits limits)
    {
        var status = EbinStructuralReader.Validate(
            record,
            limits,
            EbinReadPolicy.Canonical,
            out var bytesConsumed,
            out var diagnostic);

        if ((status is not EbinReadStatus.Complete) || (bytesConsumed != record.Length))
        {
            throw new InvalidDataException($"The {kind} record is not a complete canonical EBIN record: {diagnostic?.Category ?? status.ToString()}.");
        }

        var schemaId = new Guid(record.Slice(16, 16), bigEndian: true);
        var recordMajor = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(record[48..]));
        var recordMinor = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(record[52..]));

        if (((expectedSchemaId is Guid expected) && (schemaId != expected))
            || (recordMajor != major)
            || (recordMinor != minor))
        {
            throw new InvalidDataException($"The {kind} record has an unsupported schema identity or version.");
        }

        return schemaId;
    }

    internal static void WriteAtomically(string path, ReadOnlySpan<byte> package)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfZero(package.Length);
        var targetPath = Path.GetFullPath(path);
        var directoryPath = Path.GetDirectoryName(targetPath)
            ?? throw new ArgumentException("The package output path must have a parent directory.", nameof(path));
        _ = Directory.CreateDirectory(directoryPath);
        var temporaryPath = Path.Combine(
            directoryPath,
            string.Concat(".", Path.GetFileName(targetPath), ".", Guid.NewGuid().ToString("N"), ".tmp"));

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       65_536,
                       FileOptions.WriteThrough))
            {
                stream.Write(package);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static ulong Align16(ulong value)
    {
        if (value > (ulong.MaxValue - 15))
        {
            throw new InvalidDataException("The package layout overflows the supported offset domain.");
        }

        return (value + 15) & ~15UL;
    }

    private static byte[] CompleteFixedArray(
        EbinWriteStatus status,
        byte[] payload,
        int payloadBytes,
        EbinStructuralDiagnostic? diagnostic)
    {
        if ((status is not EbinWriteStatus.Complete) || (payloadBytes != payload.Length))
        {
            throw new InvalidDataException($"The generated EBIN fixed array is invalid: {diagnostic?.Category ?? status.ToString()}.");
        }

        return payload;
    }

    private static byte[] CreatePackageCore(
        scoped ReadOnlySpan<byte> profileRecord,
        scoped ReadOnlySpan<ContentPackageAsset> assets,
        CompiledContentSchemaRegistry? nativeAssetSchemas)
    {
        _ = ValidateRecord(profileRecord, s_profileSchema.SchemaId, 2, 0, "profile", EbinReadLimits.Foundation);
        ArgumentOutOfRangeException.ThrowIfZero(assets.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(assets.Length, 131_072);
        var sortedAssets = assets.ToArray();
        Array.Sort(sortedAssets, static (left, right) => left.AssetId.CompareTo(right.AssetId));
        var directoryOffset = Align16(checked((ulong)(PackageHeaderBytes + profileRecord.Length)));
        var directoryLength = checked((ulong)DirectoryHeaderBytes + (checked((ulong)assets.Length) * DirectoryEntryBytes));
        var packageLength = Align16(checked(directoryOffset + directoryLength));

        for (var index = 0; index < sortedAssets.Length; index++)
        {
            var asset = sortedAssets[index];

            if (asset.AssetId is 0 or > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(assets), "Package AssetIds must be in [1,2147483647].");
            }

            if ((index != 0) && (sortedAssets[index - 1].AssetId == asset.AssetId))
            {
                throw new InvalidDataException("A content package cannot contain duplicate AssetIds.");
            }

            var record = asset.Record.Span;
            var schemaId = nativeAssetSchemas is null
                ? ValidateRecord(record, expectedSchemaId: null, 1, 0, "asset", s_assetRecordLimits)
                : ValidateNativeAssetRecord(record, nativeAssetSchemas);

            if (schemaId == s_envelopeSchema.SchemaId)
            {
                throw new InvalidDataException("Package assets must be native records, not pre-encoded native/envelope records.");
            }

            var envelopeLength = Align16(checked(144UL + checked((ulong)record.Length)));

            if (envelopeLength > checked((ulong)s_assetRecordLimits.MaximumRecordBytes))
            {
                throw new InvalidDataException("A complete package envelope exceeds the asset record byte limit.");
            }

            packageLength = Align16(checked(packageLength + envelopeLength));
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(packageLength, checked((ulong)MaximumPackageBytes));

        if (nativeAssetSchemas is not null)
        {
            var lengths = new long[assets.Length];

            for (var index = 0; index < assets.Length; index++)
            {
                lengths[index] = assets[index].Record.Length;
            }

            _ = MeasureNativeAssetPackage(profileRecord.Length, lengths);
        }

        var package = new byte[checked((int)packageLength)];
        "EPKG"u8.CopyTo(package);
        BinaryPrimitives.WriteUInt16LittleEndian(package.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(package.AsSpan(6), 0);
        BinaryPrimitives.WriteUInt64LittleEndian(package.AsSpan(8), packageLength);
        BinaryPrimitives.WriteUInt64LittleEndian(package.AsSpan(16), PackageHeaderBytes);
        BinaryPrimitives.WriteUInt64LittleEndian(package.AsSpan(24), checked((ulong)profileRecord.Length));
        BinaryPrimitives.WriteUInt64LittleEndian(package.AsSpan(32), directoryOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(package.AsSpan(40), directoryLength);
        BinaryPrimitives.WriteUInt32LittleEndian(package.AsSpan(48), checked((uint)assets.Length));
        profileRecord.CopyTo(package.AsSpan(PackageHeaderBytes));
        var directory = package.AsSpan(checked((int)directoryOffset), checked((int)directoryLength));
        "EIDX"u8.CopyTo(directory);
        BinaryPrimitives.WriteUInt16LittleEndian(directory[4..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(directory[6..], 16);
        BinaryPrimitives.WriteUInt32LittleEndian(directory[8..], checked((uint)assets.Length));
        BinaryPrimitives.WriteUInt64LittleEndian(directory[16..], DirectoryHeaderBytes);
        BinaryPrimitives.WriteUInt64LittleEndian(directory[24..], checked((ulong)assets.Length * DirectoryEntryBytes));
        BinaryPrimitives.WriteUInt64LittleEndian(directory[32..], directoryLength);
        var payloadOffset = Align16(checked(directoryOffset + directoryLength));

        for (var index = 0; index < sortedAssets.Length; index++)
        {
            var asset = sortedAssets[index];
            var envelopeLength = Align16(checked(144UL + checked((ulong)asset.Record.Length)));
            var entry = directory.Slice(DirectoryHeaderBytes + (index * DirectoryEntryBytes), DirectoryEntryBytes);
            BinaryPrimitives.WriteUInt32LittleEndian(entry, asset.AssetId);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[8..], payloadOffset);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[16..], envelopeLength);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[24..], envelopeLength);
            WriteEnvelope(
                asset.AssetId,
                asset.Record.Span,
                package.AsSpan(checked((int)payloadOffset), checked((int)envelopeLength)));
            payloadOffset = Align16(checked(payloadOffset + envelopeLength));
        }

        return package;
    }

    private static byte[] EncodeRecord(
        Guid schemaId,
        int major,
        int minor,
        scoped ReadOnlySpan<EbinFieldSource> fields,
        scoped ReadOnlySpan<byte> payloadStorage,
        EbinReadLimits limits)
    {
        var record = new EbinRecordWriteView(schemaId, major, minor, fields, payloadStorage);
        var status = EbinStructuralWriter.Measure(in record, limits, out var recordBytes, out var diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            throw new InvalidDataException($"The generated EBIN record is invalid: {diagnostic?.Category ?? status.ToString()}.");
        }

        var candidate = new byte[recordBytes];
        var encoded = new byte[recordBytes];
        status = EbinStructuralWriter.Write(in record, limits, candidate, encoded, out var bytesWritten, out diagnostic);

        if ((status is not EbinWriteStatus.Complete) || (bytesWritten != recordBytes))
        {
            throw new InvalidDataException($"The generated EBIN record could not be committed: {diagnostic?.Category ?? status.ToString()}.");
        }

        return encoded;
    }

    private static void WriteEnvelope(uint assetId, scoped ReadOnlySpan<byte> record, scoped Span<byte> destination)
    {
        Span<byte> idPayload = stackalloc byte[sizeof(uint)];
        var status = EbinPayloadWriter.Write(assetId, idPayload, out var idBytes, out var diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            throw new InvalidDataException($"The package envelope AssetId field is invalid: {diagnostic?.Category ?? status.ToString()}.");
        }

        var payloadStorage = new byte[checked(idBytes + record.Length)];
        idPayload[..idBytes].CopyTo(payloadStorage);
        record.CopyTo(payloadStorage.AsSpan(idBytes));
        EbinFieldSource[] fields = [
            new EbinFieldSource(1, 3, 1, 1, new EbinPayloadSlice(0, idBytes)),
            new EbinFieldSource(2, 22, 1, 1, new EbinPayloadSlice(idBytes, record.Length)),
        ];
        var envelope = new EbinRecordWriteView(s_envelopeSchema.SchemaId, 2, 0, fields, payloadStorage);
        status = EbinStructuralWriter.Prepare(in envelope, s_assetRecordLimits, destination, out var bytesWritten, out diagnostic);

        if ((status is not EbinWriteStatus.Complete) || (bytesWritten != destination.Length))
        {
            throw new InvalidDataException($"The package envelope could not be written: {diagnostic?.Category ?? status.ToString()}.");
        }
    }

    private static void WritePayload<T>(
        PayloadWriter<T> writer,
        T value,
        scoped Span<byte> destination,
        out int payloadBytes)
    {
        var status = writer(value, destination, out payloadBytes, out var diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            throw new InvalidDataException($"The generated EBIN field is invalid: {diagnostic?.Category ?? status.ToString()}.");
        }
    }

    private delegate EbinWriteStatus PayloadWriter<T>(
        T value,
        scoped Span<byte> destination,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic);
}
