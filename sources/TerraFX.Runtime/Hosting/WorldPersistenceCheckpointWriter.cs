// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using TerraFX;

namespace TerraFX.Hosting;

internal static class WorldPersistenceCheckpointWriter
{
    private static readonly Guid s_canonicalSchemaId = new ContentSchemaIdentityAssignment("state/canonical").SchemaId;
    private static readonly Guid s_checkpointSchemaId = new ContentSchemaIdentityAssignment("state/checkpoint").SchemaId;
    private static readonly Guid s_fileContentSchemaId = new ContentSchemaIdentityAssignment("state/file-content").SchemaId;
    private static readonly Guid s_fileSchemaId = new ContentSchemaIdentityAssignment("state/file").SchemaId;

    internal static (
        byte[] CheckpointRecord,
        byte[] FileRecord,
        byte[] CanonicalHash,
        byte[] CheckpointDigest,
        byte[] FileContentDigest) Create(
        ReadOnlyMemory<byte> headerRecord,
        ReadOnlyMemory<byte> worldRecord,
        long transactionGeneration,
        EbinReadLimits limits)
    {
        var (checkpointRecord, canonicalHash, checkpointDigest) = CreateCheckpoint(headerRecord, worldRecord, limits);
        var (fileRecord, fileContentDigest) = CreateFileRecord(transactionGeneration, checkpointRecord, limits);
        return (
            checkpointRecord,
            fileRecord,
            canonicalHash,
            checkpointDigest,
            fileContentDigest);
    }

    internal static (
        byte[] CheckpointRecord,
        byte[] CanonicalHash,
        byte[] CheckpointDigest) CreateCheckpoint(
        ReadOnlyMemory<byte> headerRecord,
        ReadOnlyMemory<byte> worldRecord,
        EbinReadLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ValidateRecord(
            headerRecord.Span,
            new ContentSchemaIdentityAssignment("state/header").SchemaId,
            major: 1,
            minor: 0,
            limits,
            nameof(headerRecord));
        ValidateRecord(
            worldRecord.Span,
            new ContentSchemaIdentityAssignment("state/world").SchemaId,
            major: 1,
            minor: 0,
            limits,
            nameof(worldRecord));

        var projection = ReadHeaderProjection(headerRecord.Span, limits);
        ValidateRecord(
            projection.ApplicationRecord,
            new ContentSchemaIdentityAssignment("state/application").SchemaId,
            major: 1,
            minor: 0,
            limits,
            nameof(headerRecord));
        ValidateRecord(
            projection.ProfileRecord,
            new ContentSchemaIdentityAssignment("foundation/profile").SchemaId,
            major: 2,
            minor: 0,
            limits,
            nameof(headerRecord));
        ValidateRecord(
            projection.RuntimeProfileRecord,
            new ContentSchemaIdentityAssignment("state/runtime-profile").SchemaId,
            major: 1,
            minor: 0,
            limits,
            nameof(headerRecord));
        var canonicalHash = ComputeCanonicalHash(projection, worldRecord.Span, limits);
        var checkpointRecord = CreateCheckpointRecord(headerRecord, worldRecord, canonicalHash, limits);
        var checkpointDigest = SHA256.HashData(checkpointRecord);
        return (checkpointRecord, canonicalHash, checkpointDigest);
    }

    internal static (byte[] FileRecord, byte[] FileContentDigest) CreateFileRecord(
        long transactionGeneration,
        ReadOnlyMemory<byte> checkpointRecord,
        EbinReadLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(transactionGeneration);
        ValidateRecord(checkpointRecord.Span, s_checkpointSchemaId, major: 1, minor: 0, limits, nameof(checkpointRecord));
        var checkpointDigest = SHA256.HashData(checkpointRecord.Span);
        var fileContentDigest = ComputeFileContentDigest(transactionGeneration, checkpointRecord.Span, limits);
        var fileRecord = CreateFileRecord(
            transactionGeneration,
            checkpointRecord,
            checkpointDigest,
            fileContentDigest,
            limits);
        return (fileRecord, fileContentDigest);
    }

    internal static long ReadTransactionGeneration(
        scoped ReadOnlySpan<byte> fileRecord,
        EbinReadLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var reader = new FileRecordReader(limits);
        var status = EbinStructuralReader.Read(
            fileRecord,
            limits,
            EbinReadPolicy.Canonical,
            ref reader,
            out var bytesConsumed,
            out _);

        if ((status is not EbinReadStatus.Complete) || (bytesConsumed != fileRecord.Length))
        {
            throw new ArgumentException("The State2 file is not a complete canonical record.", nameof(fileRecord));
        }

        return reader.TransactionGeneration;
    }

    private static ulong Align16(ulong value)
    {
        if (value > (ulong.MaxValue - 15))
        {
            throw new InvalidOperationException("The State2 record alignment overflowed.");
        }

        return (value + 15) & ~15UL;
    }

    private static void AppendPadding(IncrementalHash hash, ref ulong cursor, ulong target)
    {
        Span<byte> padding = stackalloc byte[16];
        padding.Clear();

        while (cursor < target)
        {
            var count = checked((int)Math.Min((ulong)padding.Length, target - cursor));
            hash.AppendData(padding[..count]);
            cursor = checked(cursor + checked((ulong)count));
        }
    }

    private static CanonicalField[] BuildCanonicalFields(HeaderProjection projection)
    {
        var strictLocal = projection.Mode == (byte)WorldPersistenceMode.StrictLocal;
        var fields = new CanonicalField[strictLocal ? 7 : (projection.PackageSha256.Length == 0 ? 5 : 6)];
        var index = 0;
        byte[] mode = [projection.Mode];
        fields[index++] = new CanonicalField(1, 1, 1, mode);

        if (projection.PackageSha256.Length > 0)
        {
            fields[index++] = new CanonicalField(2, 13, checked((ulong)projection.PackageSha256.Length), projection.PackageSha256);
        }

        fields[index++] = new CanonicalField(3, 22, 1, projection.ApplicationRecord);
        var worldGuidBytes = new byte[16];
        _ = projection.WorldGuid.TryWriteBytes(worldGuidBytes, bigEndian: true, out _);
        fields[index++] = new CanonicalField(4, 11, 1, worldGuidBytes);
        fields[index++] = new CanonicalField(5, 22, 1, projection.ProfileRecord);
        fields[index++] = new CanonicalField(6, 22, 1, default, PayloadSource.WorldRecord);

        if (strictLocal)
        {
            fields[index++] = new CanonicalField(7, 22, 1, projection.RuntimeProfileRecord);
        }

        if (index != fields.Length)
        {
            throw new InvalidOperationException("The State2 canonical projection field count changed during construction.");
        }

        return fields;
    }

    private static byte[] ComputeCanonicalHash(
        HeaderProjection projection,
        scoped ReadOnlySpan<byte> worldRecord,
        EbinReadLimits limits)
    {
        var fields = BuildCanonicalFields(projection);
        return HashRecord(
            "TerraFX.WorldState.v1\0"u8,
            s_canonicalSchemaId,
            major: 1,
            minor: 0,
            fields,
            limits,
            worldRecord,
            checkpointRecord: []);
    }

    private static byte[] ComputeFileContentDigest(
        long transactionGeneration,
        scoped ReadOnlySpan<byte> checkpointRecord,
        EbinReadLimits limits)
    {
        Span<byte> generation = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(generation, transactionGeneration);
        CanonicalField[] fields = [
            new(1, 8, 1, generation.ToArray()),
            new(2, 22, 1, default, PayloadSource.CheckpointRecord),
        ];
        return HashRecord(
            "TerraFX.WorldFile.v1\0"u8,
            s_fileContentSchemaId,
            major: 1,
            minor: 0,
            fields,
            limits,
            worldRecord: [],
            checkpointRecord: checkpointRecord);
    }

    private static byte[] CreateCheckpointRecord(
        ReadOnlyMemory<byte> headerRecord,
        ReadOnlyMemory<byte> worldRecord,
        ReadOnlyMemory<byte> canonicalHash,
        EbinReadLimits limits)
    {
        Span<byte> canonicalHashStorage = stackalloc byte[32];
        canonicalHash.Span.CopyTo(canonicalHashStorage);
        CanonicalField[] fields = [
            new(1, 22, 1, headerRecord),
            new(2, 22, 1, worldRecord),
            new(3, 13, 32, canonicalHashStorage.ToArray()),
        ];
        return CreateRecord(s_checkpointSchemaId, fields, limits);
    }

    private static byte[] CreateFileRecord(
        long transactionGeneration,
        ReadOnlyMemory<byte> checkpointRecord,
        ReadOnlyMemory<byte> checkpointDigest,
        ReadOnlyMemory<byte> fileContentDigest,
        EbinReadLimits limits)
    {
        Span<byte> generation = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(generation, transactionGeneration);
        CanonicalField[] fields = [
            new(1, 8, 1, generation.ToArray()),
            new(2, 22, 1, checkpointRecord),
            new(3, 13, 32, checkpointDigest),
            new(4, 13, 32, fileContentDigest),
        ];
        return CreateRecord(s_fileSchemaId, fields, limits);
    }

    private static byte[] CreateRecord(
        Guid schemaId,
        CanonicalField[] fields,
        EbinReadLimits limits)
    {
        var payloadBytes = 0;

        for (var index = 0; index < fields.Length; index++)
        {
            if (fields[index].PayloadSource is not PayloadSource.Memory)
            {
                throw new InvalidOperationException("A record payload source cannot be borrowed directly from a streaming record.");
            }

            payloadBytes = checked(payloadBytes + fields[index].Payload.Length);
        }

        var payloadStorage = new byte[payloadBytes];
        var sources = new EbinFieldSource[fields.Length];
        var offset = 0;

        for (var index = 0; index < fields.Length; index++)
        {
            var field = fields[index];
            field.Payload.Span.CopyTo(payloadStorage.AsSpan(offset));
            sources[index] = new EbinFieldSource(
                field.Tag,
                field.Kind,
                1,
                field.Count,
                new EbinPayloadSlice(offset, field.Payload.Length));
            offset = checked(offset + field.Payload.Length);
        }

        var source = new EbinRecordWriteView(schemaId, 1, 0, sources, payloadStorage);
        var status = EbinStructuralWriter.Measure(in source, limits, out var recordBytes, out _);
        EnsureComplete(status, "State2 record measurement");
        var candidateStorage = new byte[recordBytes];
        var destination = new byte[recordBytes];
        status = EbinStructuralWriter.Write(
            in source,
            limits,
            candidateStorage,
            destination,
            out var bytesWritten,
            out _);
        EnsureComplete(status, "State2 record writing");

        if (bytesWritten != destination.Length)
        {
            throw new InvalidOperationException("The State2 record extent changed after measurement.");
        }

        return destination;
    }

    private static void EnsureComplete(EbinWriteStatus status, string operation)
    {
        if (status is not EbinWriteStatus.Complete)
        {
            throw new InvalidOperationException($"The {operation} failed with {status}.");
        }
    }

    private static byte[] HashRecord(
        scoped ReadOnlySpan<byte> prefix,
        Guid schemaId,
        int major,
        int minor,
        CanonicalField[] fields,
        EbinReadLimits limits,
        scoped ReadOnlySpan<byte> worldRecord,
        scoped ReadOnlySpan<byte> checkpointRecord)
    {
        var fieldAreaBytes = checked(fields.Length * 32);
        var headerBytes = checked(64 + fieldAreaBytes);
        var header = new byte[headerBytes];
        "EBIN"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), 64);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), checked((uint)fields.Length));
        _ = schemaId.TryWriteBytes(header.AsSpan(16, 16), bigEndian: true, out _);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(40), checked((ulong)fieldAreaBytes));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(48), checked((uint)major));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(52), checked((uint)minor));

        var cursor = Align16(checked((ulong)headerBytes));
        var offsets = new ulong[fields.Length];

        for (var index = 0; index < fields.Length; index++)
        {
            var field = fields[index];
            var payload = field.PayloadSource switch {
                PayloadSource.Memory => field.Payload.Span,
                PayloadSource.WorldRecord => worldRecord,
                PayloadSource.CheckpointRecord => checkpointRecord,
                _ => throw new InvalidOperationException("The State2 hash field has an invalid payload source."),
            };
            var offset = Align16(cursor);
            offsets[index] = offset;
            var entry = header.AsSpan(64 + (index * 32), 32);
            BinaryPrimitives.WriteUInt32LittleEndian(entry, field.Tag);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], field.Kind);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], 1);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[8..], offset);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[16..], checked((ulong)payload.Length));
            BinaryPrimitives.WriteUInt64LittleEndian(entry[24..], field.Count);
            cursor = checked(offset + checked((ulong)payload.Length));
        }

        var recordBytes = Align16(cursor);

        if ((recordBytes > (ulong)limits.MaximumRecordBytes) || (recordBytes > int.MaxValue))
        {
            throw new InvalidOperationException("The State2 hash projection exceeds its record-byte limit.");
        }

        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(32), recordBytes);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(prefix);
        hash.AppendData(header);
        cursor = checked((ulong)headerBytes);

        for (var index = 0; index < fields.Length; index++)
        {
            AppendPadding(hash, ref cursor, offsets[index]);
            var field = fields[index];
            var payload = field.PayloadSource switch {
                PayloadSource.Memory => field.Payload.Span,
                PayloadSource.WorldRecord => worldRecord,
                PayloadSource.CheckpointRecord => checkpointRecord,
                _ => throw new InvalidOperationException("The State2 hash field has an invalid payload source."),
            };
            hash.AppendData(payload);
            cursor = checked(cursor + checked((ulong)payload.Length));
        }

        AppendPadding(hash, ref cursor, recordBytes);
        return hash.GetHashAndReset();
    }

    private static HeaderProjection ReadHeaderProjection(
        scoped ReadOnlySpan<byte> headerRecord,
        EbinReadLimits limits)
    {
        var reader = default(HeaderProjectionReader);
        var status = EbinStructuralReader.Read(
            headerRecord,
            limits,
            EbinReadPolicy.Canonical,
            ref reader,
            out var bytesConsumed,
            out _);

        if ((status is not EbinReadStatus.Complete) || (bytesConsumed != headerRecord.Length))
        {
            throw new ArgumentException("The State2 header is not a complete canonical record.", nameof(headerRecord));
        }

        var projection = reader.Projection;

        if (projection.Mode == (byte)WorldPersistenceMode.StrictLocal)
        {
            if (projection.PackageSha256.Length != 32)
            {
                throw new ArgumentException("StrictLocal requires a package digest in the State2 header.", nameof(headerRecord));
            }
        }
        else if ((projection.Mode != (byte)WorldPersistenceMode.Standard) || (projection.PackageSha256.Length != 0))
        {
            throw new ArgumentException("The State2 header mode and package digest presence disagree.", nameof(headerRecord));
        }

        if (projection.WorldGuid == Guid.Empty)
        {
            throw new ArgumentException("The State2 header World ID must not be empty.", nameof(headerRecord));
        }

        if (projection.LineageGuid == Guid.Empty)
        {
            throw new ArgumentException("The State2 header lineage ID must not be empty.", nameof(headerRecord));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(projection.NextTick, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(projection.CaptureWorkerCount, 64);

        if ((projection.SerializationMajor != 1) || (projection.SerializationMinor != 0))
        {
            throw new ArgumentException("The State2 header serialization version is unsupported.", nameof(headerRecord));
        }

        return projection;
    }

    private static void ValidateField(
        EbinFieldReadView field,
        uint tag,
        ushort kind,
        ulong count,
        int? payloadBytes,
        string parameterName)
    {
        if ((field.Tag != tag)
            || (field.Kind != kind)
            || !field.IsRequired
            || (field.Count != count)
            || ((payloadBytes is int expectedPayloadBytes) && (field.Payload.Length != expectedPayloadBytes)))
        {
            throw new ArgumentException("A State2 record field has an invalid tag, kind, count, or payload length.", parameterName);
        }
    }

    private static void ValidateHeaderFields(EbinRecordReadView header, string parameterName)
    {
        var hasPackageDigest = header.TryGetField(1, out _);
        var expectedFieldCount = hasPackageDigest ? 11 : 10;

        if (header.FieldCount != expectedFieldCount)
        {
            throw new ArgumentException("The State2 header has an invalid field count.", parameterName);
        }

        var fieldIndex = 0;

        if (hasPackageDigest)
        {
            ValidateField(header.GetField(fieldIndex++), 1, 13, 32, 32, parameterName);
        }

        ValidateField(header.GetField(fieldIndex++), 2, 22, 1, payloadBytes: null, parameterName: parameterName);
        ValidateField(header.GetField(fieldIndex++), 3, 11, 1, 16, parameterName);
        ValidateField(header.GetField(fieldIndex++), 4, 22, 1, payloadBytes: null, parameterName: parameterName);
        ValidateField(header.GetField(fieldIndex++), 5, 8, 1, 8, parameterName);
        ValidateField(header.GetField(fieldIndex++), 6, 1, 1, 1, parameterName);
        ValidateField(header.GetField(fieldIndex++), 7, 11, 1, 16, parameterName);
        ValidateField(header.GetField(fieldIndex++), 8, 22, 1, payloadBytes: null, parameterName: parameterName);
        ValidateField(header.GetField(fieldIndex++), 9, 3, 1, 4, parameterName);
        ValidateField(header.GetField(fieldIndex++), 10, 3, 1, 4, parameterName);
        ValidateField(header.GetField(fieldIndex), 11, 1, 1, 1, parameterName);
    }

    private static void ValidateRecord(
        scoped ReadOnlySpan<byte> record,
        Guid schemaId,
        int major,
        int minor,
        EbinReadLimits limits,
        string parameterName)
    {
        var status = EbinStructuralReader.ReadHeader(record, limits, out var header, out _, out _);

        if ((status is not EbinReadStatus.Complete)
            || (header.SchemaId != schemaId)
            || (header.Major != major)
            || (header.Minor != minor))
        {
            throw new ArgumentException("The nested record has the wrong State2 schema identity or version.", parameterName);
        }

        status = EbinStructuralReader.Validate(record, limits, EbinReadPolicy.Canonical, out var bytesConsumed, out _);

        if ((status is not EbinReadStatus.Complete) || (bytesConsumed != record.Length))
        {
            throw new ArgumentException("The nested record is not a complete canonical EBIN record.", parameterName);
        }
    }

    private readonly record struct CanonicalField(
        uint Tag,
        ushort Kind,
        ulong Count,
        ReadOnlyMemory<byte> Payload,
        PayloadSource PayloadSource = PayloadSource.Memory);

    private struct FileRecordReader : IAction<EbinRecordReadView>
    {
        private readonly EbinReadLimits _limits;

        internal FileRecordReader(EbinReadLimits limits)
        {
            _limits = limits;
            TransactionGeneration = 0;
        }

        internal long TransactionGeneration { get; private set; }

        public void Invoke(scoped EbinRecordReadView fileRecord)
        {
            var fileHeader = fileRecord.Header;

            if ((fileHeader.SchemaId != s_fileSchemaId) || (fileHeader.Major != 1) || (fileHeader.Minor != 0))
            {
                throw new ArgumentException("The record is not State2 state/file v1.0.", nameof(fileRecord));
            }

            if (fileRecord.FieldCount != 4)
            {
                throw new ArgumentException("The State2 file record has an invalid field count.", nameof(fileRecord));
            }

            ValidateField(fileRecord.GetField(0), 1, 8, 1, 8, nameof(fileRecord));
            ValidateField(fileRecord.GetField(1), 2, 22, 1, payloadBytes: null, parameterName: nameof(fileRecord));
            ValidateField(fileRecord.GetField(2), 3, 13, 32, 32, nameof(fileRecord));
            ValidateField(fileRecord.GetField(3), 4, 13, 32, 32, nameof(fileRecord));

            var transactionGeneration = BinaryPrimitives.ReadInt64LittleEndian(fileRecord.GetField(0).Payload);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(transactionGeneration);
            var checkpoint = fileRecord.GetRecord(2);
            ValidateRecord(checkpoint.EncodedBytes, s_checkpointSchemaId, major: 1, minor: 0, _limits, nameof(fileRecord));

            if (checkpoint.FieldCount != 3)
            {
                throw new ArgumentException("The State2 checkpoint record has an invalid field count.", nameof(fileRecord));
            }

            ValidateField(checkpoint.GetField(0), 1, 22, 1, payloadBytes: null, parameterName: nameof(fileRecord));
            ValidateField(checkpoint.GetField(1), 2, 22, 1, payloadBytes: null, parameterName: nameof(fileRecord));
            ValidateField(checkpoint.GetField(2), 3, 13, 32, 32, nameof(fileRecord));

            var header = checkpoint.GetRecord(1);
            ValidateRecord(
                header.EncodedBytes,
                new ContentSchemaIdentityAssignment("state/header").SchemaId,
                major: 1,
                minor: 0,
                _limits,
                nameof(fileRecord));
            ValidateHeaderFields(header, nameof(fileRecord));
            var world = checkpoint.GetRecord(2);
            ValidateRecord(
                world.EncodedBytes,
                new ContentSchemaIdentityAssignment("state/world").SchemaId,
                major: 1,
                minor: 0,
                _limits,
                nameof(fileRecord));
            var projection = ReadHeaderProjection(header.EncodedBytes, _limits);
            ValidateRecord(
                projection.ApplicationRecord,
                new ContentSchemaIdentityAssignment("state/application").SchemaId,
                major: 1,
                minor: 0,
                _limits,
                nameof(fileRecord));
            ValidateRecord(
                projection.ProfileRecord,
                new ContentSchemaIdentityAssignment("foundation/profile").SchemaId,
                major: 2,
                minor: 0,
                _limits,
                nameof(fileRecord));
            ValidateRecord(
                projection.RuntimeProfileRecord,
                new ContentSchemaIdentityAssignment("state/runtime-profile").SchemaId,
                major: 1,
                minor: 0,
                _limits,
                nameof(fileRecord));

            var canonicalHash = ComputeCanonicalHash(projection, world.EncodedBytes, _limits);

            if (!CryptographicOperations.FixedTimeEquals(checkpoint.GetField(2).Payload, canonicalHash))
            {
                throw new ArgumentException("The State2 checkpoint canonical hash does not match its contents.", nameof(fileRecord));
            }

            var checkpointDigest = SHA256.HashData(checkpoint.EncodedBytes);

            if (!CryptographicOperations.FixedTimeEquals(fileRecord.GetField(2).Payload, checkpointDigest))
            {
                throw new ArgumentException("The State2 checkpoint digest does not match its contents.", nameof(fileRecord));
            }

            var fileContentDigest = ComputeFileContentDigest(transactionGeneration, checkpoint.EncodedBytes, _limits);

            if (!CryptographicOperations.FixedTimeEquals(fileRecord.GetField(3).Payload, fileContentDigest))
            {
                throw new ArgumentException("The State2 file content digest does not match its contents.", nameof(fileRecord));
            }

            TransactionGeneration = transactionGeneration;
        }
    }

    private readonly record struct HeaderProjection(
        byte Mode,
        Guid WorldGuid,
        Guid LineageGuid,
        long NextTick,
        byte CaptureWorkerCount,
        uint SerializationMajor,
        uint SerializationMinor,
        byte[] PackageSha256,
        byte[] ApplicationRecord,
        byte[] ProfileRecord,
        byte[] RuntimeProfileRecord);

    private struct HeaderProjectionReader : IAction<EbinRecordReadView>
    {
        private byte[]? _applicationRecord;
        private byte[]? _packageSha256;
        private byte[]? _profileRecord;
        private byte[]? _runtimeProfileRecord;
        private byte _captureWorkerCount;
        private byte _mode;
        private Guid _lineageGuid;
        private long _nextTick;
        private uint _serializationMajor;
        private uint _serializationMinor;
        private Guid _worldGuid;

        internal readonly HeaderProjection Projection
        {
            get
            {
                if ((_applicationRecord is null) || (_profileRecord is null) || (_runtimeProfileRecord is null))
                {
                    throw new InvalidOperationException("The State2 header omitted a required canonical projection input.");
                }

                return new HeaderProjection(
                    _mode,
                    _worldGuid,
                    _lineageGuid,
                    _nextTick,
                    _captureWorkerCount,
                    _serializationMajor,
                    _serializationMinor,
                    _packageSha256 ?? [],
                    _applicationRecord,
                    _profileRecord,
                    _runtimeProfileRecord);
            }
        }

        public void Invoke(scoped EbinRecordReadView record)
        {
            var header = record.Header;

            if ((header.SchemaId != new ContentSchemaIdentityAssignment("state/header").SchemaId)
                || (header.Major != 1)
                || (header.Minor != 0))
            {
                throw new ArgumentException("The source record is not State2 state/header v1.0.");
            }

            if (!record.TryGetField(6, out var mode))
            {
                throw new ArgumentException("The State2 header omitted its mode.");
            }

            if (!record.TryGetField(3, out var worldGuid))
            {
                throw new ArgumentException("The State2 header omitted its World ID.");
            }

            if (!record.TryGetField(5, out var nextTick)
                || !record.TryGetField(7, out var lineageGuid)
                || !record.TryGetField(9, out var serializationMajor)
                || !record.TryGetField(10, out var serializationMinor)
                || !record.TryGetField(11, out var captureWorkerCount))
            {
                throw new ArgumentException("The State2 header omitted required scalar metadata.");
            }

            _mode = mode.Payload[0];
            _worldGuid = new Guid(worldGuid.Payload, bigEndian: true);
            _nextTick = BinaryPrimitives.ReadInt64LittleEndian(nextTick.Payload);
            _lineageGuid = new Guid(lineageGuid.Payload, bigEndian: true);
            _serializationMajor = BinaryPrimitives.ReadUInt32LittleEndian(serializationMajor.Payload);
            _serializationMinor = BinaryPrimitives.ReadUInt32LittleEndian(serializationMinor.Payload);
            _captureWorkerCount = captureWorkerCount.Payload[0];
            _applicationRecord = record.GetRecord(2).EncodedBytes.ToArray();
            _profileRecord = record.GetRecord(4).EncodedBytes.ToArray();
            _runtimeProfileRecord = record.GetRecord(8).EncodedBytes.ToArray();
            _packageSha256 = record.TryGetField(1, out var packageDigest)
                ? packageDigest.Payload.ToArray()
                : null;
        }
    }

    private enum PayloadSource : byte
    {
        Memory,
        WorldRecord,
        CheckpointRecord,
    }
}
