// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;

namespace TerraFX.Assets.Tests;

[TestFixture]
internal sealed class AssetStoreTests
{
    private static readonly ContentSchemaIdentityAssignment s_assetIdentity = new ContentSchemaIdentityAssignment("test/asset");
    private static readonly ContentSchemaReference s_assetSchema = new ContentSchemaReference(
        s_assetIdentity.SchemaId,
        1,
        0,
        ContentSchemaScope.Content);
    private static readonly DataSchema s_assetSchemaDescriptor = CreateAssetSchemaDescriptor();
    private static readonly CompiledContentSchemaRegistry s_schemas = new CompiledContentSchemaRegistry([
        .. NativeAssetSchemaCatalogue.Schemas,
        s_assetSchemaDescriptor,
    ]);

    private static ulong Align16(ulong value) => (value + 15) & ~15UL;

    private static byte[] CreateAssetEnvelope(uint assetId, byte[] payload)
    {
        var envelopeSchema = FindEnvelopeSchema();
        var envelope = new byte[checked((int)Align16(144UL + checked((ulong)payload.Length)))];
        WriteHeader(envelope, envelopeSchema, 2);
        WriteFieldHeader(envelope.AsSpan(64, 32), 1, 3, 128, 4, 1);
        WriteFieldHeader(envelope.AsSpan(96, 32), 2, 22, 144, checked((ulong)payload.Length), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(envelope.AsSpan(128), assetId);
        payload.CopyTo(envelope.AsSpan(144));
        return envelope;
    }

    private static byte[] CreateAssetEnvelope(uint assetId, uint marker)
        => CreateAssetEnvelope(assetId, CreateAssetRecord(marker));

    private static byte[] CreateAssetRecord(uint marker)
    {
        var record = new byte[112];
        WriteHeader(record, s_assetSchema, 1);
        WriteFieldHeader(record.AsSpan(64, 32), 1, 3, 96, 4, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(96), marker);
        return record;
    }

    private static byte[] CreateAssetRecordWithReference(
        uint marker,
        uint targetAssetId,
        Guid targetSchemaId,
        uint targetSchemaMajor)
    {
        var reference = CreateAssetReferenceRecord(targetAssetId, targetSchemaId, targetSchemaMajor);
        var record = new byte[352];
        WriteHeader(record, s_assetSchema, 2);
        WriteFieldHeader(record.AsSpan(64, 32), 1, 3, 128, 4, 1);
        WriteFieldHeader(record.AsSpan(96, 32), 2, 22, 144, checked((ulong)reference.Length), 1, flags: 0);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(128), marker);
        reference.CopyTo(record.AsSpan(144));
        return record;
    }

    private static byte[] CreateAssetReferenceRecord(uint targetAssetId, Guid targetSchemaId, uint targetSchemaMajor)
    {
        var record = new byte[208];
        WriteHeader(record, FindAssetReferenceSchema(), 3);
        WriteFieldHeader(record.AsSpan(64, 32), 1, 3, 160, 4, 1);
        WriteFieldHeader(record.AsSpan(96, 32), 2, 11, 176, 16, 1);
        WriteFieldHeader(record.AsSpan(128, 32), 3, 3, 192, 4, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(160), targetAssetId);
        _ = targetSchemaId.TryWriteBytes(record.AsSpan(176, 16), bigEndian: true, out _);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(192), targetSchemaMajor);
        return record;
    }

    private static DataSchema CreateAssetSchemaDescriptor()
        => new DataSchema(
            s_assetSchema,
            s_assetIdentity,
            ContentSchemaRole.NativeAsset,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            [
                new ContentFieldDescriptor(
                    1,
                    3,
                    0,
                    true,
                    1,
                    1,
                    [],
                    0,
                    ContentUnit.None,
                    ContentReferenceKind.None,
                    ContentAuthoring.None,
                    null),
                new ContentFieldDescriptor(
                    2,
                    22,
                    0,
                    false,
                    1,
                    1,
                    [FindAssetReferenceSchema()],
                    0,
                    ContentUnit.None,
                    ContentReferenceKind.Asset,
                    ContentAuthoring.None,
                    null),
            ]);

    private static AssetStoreOptions CreateOptions(
        string path,
        byte[] package,
        AssetStoreRegistration registration,
        long cpuAssetBudgetBytes = 536_870_912,
        int maximumConcurrentDecodeBytes = 268_435_456,
        int maximumLiveAssetLeases = 4096)
        => new AssetStoreOptions(path, SHA256.HashData(package), registration) {
            CpuAssetBudgetBytes = cpuAssetBudgetBytes,
            MaximumConcurrentDecodeBytes = maximumConcurrentDecodeBytes,
            MaximumLiveAssetLeases = maximumLiveAssetLeases,
        };

    private static AssetStoreRegistration CreateRegistration(IBinarySchemaCodec<TestAssetValue>? codec = null)
        => new AssetStoreRegistrationBuilder(s_schemas)
            .RegisterCodec(codec ?? new TestAssetCodec())
            .Build();

    private static ContentSchemaReference FindAssetReferenceSchema()
    {
        foreach (var schema in NativeAssetSchemaCatalogue.Schemas)
        {
            if (StringComparer.Ordinal.Equals(schema.IdentityAssignment.Name, "native/asset-reference"))
            {
                return schema.Schema;
            }
        }

        throw new AssertionException("The native asset-reference schema is missing.");
    }

    private static ContentSchemaReference FindEnvelopeSchema()
    {
        foreach (var schema in NativeAssetSchemaCatalogue.Schemas)
        {
            if (StringComparer.Ordinal.Equals(schema.IdentityAssignment.Name, "native/envelope"))
            {
                return schema.Schema;
            }
        }

        throw new AssertionException("The native envelope schema is missing.");
    }

    private static void WriteFieldHeader(
        Span<byte> destination,
        uint tag,
        ushort kind,
        ulong payloadOffset,
        ulong payloadLength,
        ulong count,
        ushort flags = 1)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination, tag);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], kind);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], flags);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..], payloadOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[16..], payloadLength);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[24..], count);
    }

    private static void WriteHeader(byte[] destination, ContentSchemaReference schema, uint fieldCount)
    {
        "EBIN"u8.CopyTo(destination);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(8), 64);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(12), fieldCount);
        _ = schema.SchemaId.TryWriteBytes(destination.AsSpan(16, 16), bigEndian: true, out _);
        BinaryPrimitives.WriteUInt64LittleEndian(destination.AsSpan(32), checked((ulong)destination.Length));
        BinaryPrimitives.WriteUInt64LittleEndian(destination.AsSpan(40), checked((ulong)fieldCount * 32));
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(48), checked((uint)schema.Major));
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(52), checked((uint)schema.Minor));
    }

    [Test]
    public async Task AssetStoreHonorsCancellationWithoutLeakingLoadReservations()
    {
        var registration = CreateRegistration();
        var package = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 15)));
        var path = AssetPackageReaderTests.WritePackage(package);
        var store = await AssetStore.CreateAsync(CreateOptions(path, package, registration), CancellationToken.None).ConfigureAwait(false);

        try
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            _ = Assert.ThrowsAsync<OperationCanceledException>(
                async () => await store.LoadAsync<TestAssetValue>(new AssetId(1), cancellation.Token).ConfigureAwait(false));

            var lease = await store.LoadAsync<TestAssetValue>(new AssetId(1), CancellationToken.None).ConfigureAwait(false);
            using (lease)
            {
                Assert.That(lease.Value.Marker, Is.EqualTo(15));
            }
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(path);
        }
    }

    [Test]
    public async Task AssetStoreLoadsBrotliPayloadsWithinDeclaredDecodedLength()
    {
        var registration = CreateRegistration();
        var envelope = CreateAssetEnvelope(1, 75);
        var compressed = new byte[BrotliEncoder.GetMaxCompressedLength(envelope.Length)];
        Assert.That(BrotliEncoder.TryCompress(envelope, compressed, out var compressedLength, quality: 5, window: 22), Is.True);
        Array.Resize(ref compressed, compressedLength);
        var package = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, compressed, 1, envelope.Length));
        var path = AssetPackageReaderTests.WritePackage(package);
        var store = await AssetStore.CreateAsync(CreateOptions(path, package, registration), CancellationToken.None).ConfigureAwait(false);

        try
        {
            var lease = await store.LoadAsync<TestAssetValue>(new AssetId(1), CancellationToken.None).ConfigureAwait(false);
            using (lease)
            {
                Assert.That(lease.Value.Marker, Is.EqualTo(75));
            }
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(path);
        }
    }

    [Test]
    public async Task AssetStoreLoadsTypedAssetAndInvalidatesDisposedLease()
    {
        var registration = CreateRegistration();
        var package = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 42)));
        var path = AssetPackageReaderTests.WritePackage(package);
        var store = await AssetStore.CreateAsync(CreateOptions(path, package, registration), CancellationToken.None).ConfigureAwait(false);

        try
        {
            var lease = await store.LoadAsync<TestAssetValue>(new AssetId(1), CancellationToken.None).ConfigureAwait(false);

            using (lease)
            {
                Assert.That(lease.AssetId, Is.EqualTo(new AssetId(1)));
                Assert.That(lease.Value.Marker, Is.EqualTo(42));
            }

            _ = Assert.Throws<ObjectDisposedException>(() => _ = lease.Value);
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(path);
        }
    }

    [Test]
    public async Task AssetStoreRejectsCodecThatDoesNotConsumeTheCompletePayload()
    {
        var registration = CreateRegistration(new IncompleteAssetCodec());
        var package = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 41)));
        var path = AssetPackageReaderTests.WritePackage(package);
        var store = await AssetStore.CreateAsync(CreateOptions(path, package, registration), CancellationToken.None).ConfigureAwait(false);

        try
        {
            _ = Assert.ThrowsAsync<InvalidDataException>(
                async () => await store.LoadAsync<TestAssetValue>(new AssetId(1), CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(path);
        }
    }

    [Test]
    public async Task AssetStoreRejectsFailedReplacementAndKeepsCurrentPackage()
    {
        var registration = CreateRegistration();
        var currentPackage = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 23)));
        var candidatePackage = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 99)));
        var currentPath = AssetPackageReaderTests.WritePackage(currentPackage);
        var candidatePath = AssetPackageReaderTests.WritePackage(candidatePackage);
        var store = await AssetStore.CreateAsync(
            CreateOptions(currentPath, currentPackage, registration),
            CancellationToken.None).ConfigureAwait(false);

        try
        {
            var rejectedOptions = new AssetStoreOptions(candidatePath, new byte[32], registration);
            _ = Assert.ThrowsAsync<InvalidDataException>(
                async () => await store.ReplacePackageAsync(rejectedOptions, CancellationToken.None).ConfigureAwait(false));

            var lease = await store.LoadAsync<TestAssetValue>(new AssetId(1), CancellationToken.None).ConfigureAwait(false);
            using (lease)
            {
                Assert.That(lease.Value.Marker, Is.EqualTo(23));
            }
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(currentPath);
            File.Delete(candidatePath);
        }
    }

    [Test]
    public async Task AssetStoreRejectsLoadsThatExceedCpuOrConcurrentDecodeBudgets()
    {
        var registration = CreateRegistration();
        var payload = CreateAssetEnvelope(1, 52);
        var package = AssetPackageReaderTests.CreatePackage(new AssetPackageReaderTests.TestEntry(1, payload));
        var path = AssetPackageReaderTests.WritePackage(package);

        try
        {
            var cpuLimitedStore = await AssetStore.CreateAsync(
                CreateOptions(path, package, registration, cpuAssetBudgetBytes: payload.Length - 1),
                CancellationToken.None).ConfigureAwait(false);

            try
            {
                _ = Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await cpuLimitedStore.LoadAsync<TestAssetValue>(new AssetId(1), CancellationToken.None).ConfigureAwait(false));
            }
            finally
            {
                await cpuLimitedStore.DisposeAsync().ConfigureAwait(false);
            }

            var decodeLimitedStore = await AssetStore.CreateAsync(
                CreateOptions(path, package, registration, maximumConcurrentDecodeBytes: payload.Length - 1),
                CancellationToken.None).ConfigureAwait(false);

            try
            {
                _ = Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await decodeLimitedStore.LoadAsync<TestAssetValue>(new AssetId(1), CancellationToken.None).ConfigureAwait(false));
            }
            finally
            {
                await decodeLimitedStore.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task AssetStoreRejectsMissingAndWrongTypedAssetReferences()
    {
        var registration = CreateRegistration();
        var missingTargetPackage = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(
                1,
                CreateAssetEnvelope(1, CreateAssetRecordWithReference(11, 2, s_assetSchema.SchemaId, 1))));
        var mismatchedSchemaPackage = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(
                1,
                CreateAssetEnvelope(1, CreateAssetRecordWithReference(
                    11,
                    2,
                    new Guid("11111111-2222-4333-8444-555555555555"),
                    1))),
            new AssetPackageReaderTests.TestEntry(2, CreateAssetEnvelope(2, CreateAssetRecord(22))));
        var mismatchedMajorPackage = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(
                1,
                CreateAssetEnvelope(1, CreateAssetRecordWithReference(11, 2, s_assetSchema.SchemaId, 2))),
            new AssetPackageReaderTests.TestEntry(2, CreateAssetEnvelope(2, CreateAssetRecord(22))));

        foreach (var package in new[] { missingTargetPackage, mismatchedSchemaPackage, mismatchedMajorPackage })
        {
            var path = AssetPackageReaderTests.WritePackage(package);
            var store = await AssetStore.CreateAsync(
                CreateOptions(path, package, registration),
                CancellationToken.None).ConfigureAwait(false);

            try
            {
                _ = Assert.ThrowsAsync<InvalidDataException>(
                    async () => await store.LoadAsync<TestAssetValue>(new AssetId(1), CancellationToken.None).ConfigureAwait(false));
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
                File.Delete(path);
            }
        }
    }

    [Test]
    public async Task AssetStoreRejectsMissingIdsWrongTypesAndMismatchedEnvelopeIds()
    {
        var registration = CreateRegistration();
        var package = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(2, 5)));
        var path = AssetPackageReaderTests.WritePackage(package);
        var store = await AssetStore.CreateAsync(CreateOptions(path, package, registration), CancellationToken.None).ConfigureAwait(false);

        try
        {
            _ = Assert.ThrowsAsync<KeyNotFoundException>(
                async () => await store.LoadAsync<TestAssetValue>(new AssetId(2), CancellationToken.None).ConfigureAwait(false));
            _ = Assert.ThrowsAsync<NotSupportedException>(
                async () => await store.LoadAsync<string>(new AssetId(1), CancellationToken.None).ConfigureAwait(false));
            _ = Assert.ThrowsAsync<InvalidDataException>(
                async () => await store.LoadAsync<TestAssetValue>(new AssetId(1), CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(path);
        }
    }

    [Test]
    public async Task AssetStoreRejectsTrailingBrotliInputAndReleasesDecodeBudget()
    {
        var registration = CreateRegistration();
        var envelope = CreateAssetEnvelope(1, 31);
        var compressed = new byte[BrotliEncoder.GetMaxCompressedLength(envelope.Length)];
        Assert.That(BrotliEncoder.TryCompress(envelope, compressed, out var compressedLength, quality: 5, window: 22), Is.True);
        Array.Resize(ref compressed, compressedLength + 1);
        compressed[^1] = 0xA5;
        var package = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, compressed, 1, envelope.Length));
        var path = AssetPackageReaderTests.WritePackage(package);
        var store = await AssetStore.CreateAsync(CreateOptions(path, package, registration), CancellationToken.None).ConfigureAwait(false);

        try
        {
            _ = Assert.ThrowsAsync<InvalidDataException>(
                async () => await store.LoadAsync<TestAssetValue>(new AssetId(1), CancellationToken.None).ConfigureAwait(false));
            _ = Assert.ThrowsAsync<InvalidDataException>(
                async () => await store.LoadAsync<TestAssetValue>(new AssetId(1), CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(path);
        }
    }

    [Test]
    public async Task AssetStoreReleasesLeaseBudgetsWhenLeasesAreDisposed()
    {
        var registration = CreateRegistration();
        var package = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 90)));
        var path = AssetPackageReaderTests.WritePackage(package);
        var store = await AssetStore.CreateAsync(
            CreateOptions(path, package, registration, maximumLiveAssetLeases: 1),
            CancellationToken.None).ConfigureAwait(false);

        try
        {
            var firstLease = await store.LoadAsync<TestAssetValue>(new AssetId(1), CancellationToken.None).ConfigureAwait(false);
            _ = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await store.LoadAsync<TestAssetValue>(new AssetId(1), CancellationToken.None).ConfigureAwait(false));

            firstLease.Dispose();
            var secondLease = await store.LoadAsync<TestAssetValue>(new AssetId(1), CancellationToken.None).ConfigureAwait(false);
            secondLease.Dispose();
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(path);
        }
    }

    [Test]
    public async Task AssetStoreResolvesDeclaredTypedAssetReferences()
    {
        var registration = CreateRegistration();
        var package = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(
                1,
                CreateAssetEnvelope(1, CreateAssetRecordWithReference(11, 2, s_assetSchema.SchemaId, 1))),
            new AssetPackageReaderTests.TestEntry(2, CreateAssetEnvelope(2, 22)));
        var path = AssetPackageReaderTests.WritePackage(package);
        var store = await AssetStore.CreateAsync(CreateOptions(path, package, registration), CancellationToken.None).ConfigureAwait(false);

        try
        {
            var lease = await store.LoadAsync<TestAssetValue>(new AssetId(1), CancellationToken.None).ConfigureAwait(false);

            using (lease)
            {
                Assert.That(lease.Value.Marker, Is.EqualTo(11));
            }
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(path);
        }
    }

    [Test]
    public async Task AssetStoreRetainsOldPackageAcrossReplacementAndDisposal()
    {
        var registration = CreateRegistration();
        var firstPackage = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 10)));
        var secondPackage = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 20)));
        var firstPath = AssetPackageReaderTests.WritePackage(firstPackage);
        var secondPath = AssetPackageReaderTests.WritePackage(secondPackage);
        var store = await AssetStore.CreateAsync(
            CreateOptions(firstPath, firstPackage, registration),
            CancellationToken.None).ConfigureAwait(false);
        AssetLease<TestAssetValue>? oldLease = null;

        try
        {
            oldLease = await store.LoadAsync<TestAssetValue>(new AssetId(1), CancellationToken.None).ConfigureAwait(false);
            await store.ReplacePackageAsync(
                CreateOptions(secondPath, secondPackage, registration),
                CancellationToken.None).ConfigureAwait(false);
            var newLease = await store.LoadAsync<TestAssetValue>(new AssetId(1), CancellationToken.None).ConfigureAwait(false);

            using (newLease)
            {
                Assert.That(newLease.Value.Marker, Is.EqualTo(20));
            }

            Assert.That(oldLease.Value.Marker, Is.EqualTo(10));
            await store.DisposeAsync().ConfigureAwait(false);
            Assert.That(oldLease.Value.Marker, Is.EqualTo(10));
            oldLease.Dispose();
            _ = Assert.Throws<ObjectDisposedException>(() => _ = oldLease.Value);
        }
        finally
        {
            oldLease?.Dispose();
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }

    private sealed class IncompleteAssetCodec : IBinarySchemaCodec<TestAssetValue>
    {
        private readonly TestAssetCodec _codec = new TestAssetCodec();

        public Guid SchemaId => _codec.SchemaId;

        public int SchemaMajor => _codec.SchemaMajor;

        public int SchemaMinor => _codec.SchemaMinor;

        public BinaryRecordStatus Read(scoped ReadOnlySpan<byte> source, out TestAssetValue value, out int bytesConsumed)
        {
            var status = _codec.Read(source, out value, out bytesConsumed);

            if (status is BinaryRecordStatus.Complete)
            {
                bytesConsumed--;
            }

            return status;
        }

        public BinaryRecordStatus Write(in TestAssetValue value, scoped Span<byte> destination, out int bytesWritten)
            => _codec.Write(value, destination, out bytesWritten);
    }

    private sealed class TestAssetCodec : IBinarySchemaCodec<TestAssetValue>
    {
        public Guid SchemaId => s_assetSchema.SchemaId;

        public int SchemaMajor => s_assetSchema.Major;

        public int SchemaMinor => s_assetSchema.Minor;

        public BinaryRecordStatus Read(scoped ReadOnlySpan<byte> source, out TestAssetValue value, out int bytesConsumed)
        {
            value = default;
            bytesConsumed = 0;
            var status = EbinStructuralReader.Validate(
                source,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                out var consumed,
                out _);

            if (status is not EbinReadStatus.Complete)
            {
                return status switch {
                    EbinReadStatus.NeedMoreData => BinaryRecordStatus.NeedMoreData,
                    EbinReadStatus.UnsupportedSchema => BinaryRecordStatus.UnsupportedSchema,
                    _ => BinaryRecordStatus.InvalidRecord,
                };
            }

            var fieldCount = BinaryPrimitives.ReadUInt32LittleEndian(source[12..]);

            if ((consumed != source.Length)
                || (new Guid(source.Slice(16, 16), bigEndian: true) != SchemaId)
                || (BinaryPrimitives.ReadUInt32LittleEndian(source[48..]) != SchemaMajor)
                || (BinaryPrimitives.ReadUInt32LittleEndian(source[52..]) != SchemaMinor)
                || (fieldCount is not 1 and not 2))
            {
                return BinaryRecordStatus.InvalidRecord;
            }

            var valueField = source.Slice(64, 32);
            var valueOffset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(valueField[8..]));

            if ((BinaryPrimitives.ReadUInt32LittleEndian(valueField) != 1)
                || (BinaryPrimitives.ReadUInt16LittleEndian(valueField[4..]) != 3)
                || (BinaryPrimitives.ReadUInt64LittleEndian(valueField[16..]) != sizeof(uint))
                || (BinaryPrimitives.ReadUInt64LittleEndian(valueField[24..]) != 1))
            {
                return BinaryRecordStatus.InvalidRecord;
            }

            value = new TestAssetValue(BinaryPrimitives.ReadUInt32LittleEndian(source[valueOffset..]));
            bytesConsumed = consumed;
            return BinaryRecordStatus.Complete;
        }

        public BinaryRecordStatus Write(in TestAssetValue value, scoped Span<byte> destination, out int bytesWritten)
        {
            bytesWritten = 0;
            var record = CreateAssetRecord(value.Marker);

            if (destination.Length < record.Length)
            {
                return BinaryRecordStatus.DestinationTooSmall;
            }

            record.CopyTo(destination);
            bytesWritten = record.Length;
            return BinaryRecordStatus.Complete;
        }
    }

    private readonly record struct TestAssetValue(uint Marker);
}
