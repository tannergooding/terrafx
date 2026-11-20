// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<AssetPackageBacking> CreateBackingWithFailedClose(string path)
    {
        using var stream = new ThrowingFileStream(path);
        var backing = new AssetPackageBacking(
            stream,
            [],
            default,
            default,
            CreateOptions(path, [], CreateRegistration()),
            packageByteLength: 0);
        var weakBacking = new WeakReference<AssetPackageBacking>(backing);
        _ = Assert.Throws<IOException>(backing.ReleaseOwner);
        return weakBacking;
    }

    private static byte[] CreateOpaqueAssetRecord(int utf8ByteLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(utf8ByteLength);
        const int payloadOffset = 96;
        var recordLength = checked((int)Align16(checked((ulong)(payloadOffset + utf8ByteLength))));
        var record = new byte[recordLength];
        WriteHeader(record, s_assetSchema, fieldCount: 1);
        WriteFieldHeader(
            record.AsSpan(64, 32),
            tag: 1,
            kind: 12,
            payloadOffset,
            checked((ulong)utf8ByteLength),
            checked((ulong)utf8ByteLength));
        record.AsSpan(payloadOffset, utf8ByteLength).Fill((byte)'A');
        return record;
    }

    private static ImmutableArray<ContentFieldDescriptor> CreateOptionalAssetReferenceFields(
        ContentSchemaReference assetReferenceSchema,
        int fieldCount)
    {
        var fields = ImmutableArray.CreateBuilder<ContentFieldDescriptor>(fieldCount);

        for (var index = 0; index < fieldCount; index++)
        {
            fields.Add(new ContentFieldDescriptor(
                checked((uint)index + 2),
                22,
                0,
                false,
                1,
                1,
                [assetReferenceSchema],
                0,
                ContentUnit.None,
                ContentReferenceKind.Asset,
                ContentAuthoring.None,
                null));
        }

        return fields.MoveToImmutable();
    }

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

    private static byte[] CreateReferenceRootRecord(
        ContentSchemaReference schema,
        byte[][] references)
    {
        var fieldCount = checked(1 + references.Length);
        var directoryEnd = checked(64 + (fieldCount * 32));
        var markerOffset = checked((int)Align16(checked((ulong)directoryEnd)));
        var cursor = checked(markerOffset + sizeof(uint));
        var referenceOffsets = new int[references.Length];

        for (var index = 0; index < references.Length; index++)
        {
            cursor = checked((int)Align16(checked((ulong)cursor)));
            referenceOffsets[index] = cursor;
            cursor = checked(cursor + references[index].Length);
        }

        var encodedLength = checked((int)Align16(checked((ulong)cursor)));
        var encodedRecord = new byte[encodedLength];
        WriteHeader(encodedRecord, schema, checked((uint)fieldCount));
        WriteFieldHeader(
            encodedRecord.AsSpan(64, 32),
            1,
            3,
            checked((ulong)markerOffset),
            sizeof(uint),
            1);
        BinaryPrimitives.WriteUInt32LittleEndian(encodedRecord.AsSpan(markerOffset), 1);

        for (var index = 0; index < references.Length; index++)
        {
            WriteFieldHeader(
                encodedRecord.AsSpan(64 + ((index + 1) * 32), 32),
                checked((uint)index + 2),
                22,
                checked((ulong)referenceOffsets[index]),
                checked((ulong)references[index].Length),
                1,
                flags: 0);
            references[index].CopyTo(encodedRecord.AsSpan(referenceOffsets[index]));
        }

        return encodedRecord;
    }

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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(
        AssetPackageBacking Backing,
        WeakReference<AssetStoreOptions> Options,
        AssetStoreRegistration Registration,
        byte[] PackageSha256)> OpenBackingWithoutRetainingOptions(
        string path,
        byte[] package)
    {
        var registration = CreateRegistration();
        var packageSha256 = SHA256.HashData(package);
        var options = new AssetStoreOptions(path, packageSha256, registration) {
            CpuAssetBudgetBytes = 345,
            MaximumDecodedAssetBytes = 123,
            MaximumConcurrentDecodeBytes = 234,
            MaximumLiveAssetLeases = 12,
        };
        var weakOptions = new WeakReference<AssetStoreOptions>(options);
        var backing = await AssetPackageReader.OpenAsync(options, CancellationToken.None).ConfigureAwait(false);
        return (backing, weakOptions, registration, packageSha256);
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
    public void AssetEnvelopeDecoderBoundedDecodeRefusesInsufficientReferenceStorage()
    {
        var registration = CreateRegistration();

        if (!registration.TryGetCodec<TestAssetValue>(out var codec))
        {
            throw new InvalidOperationException("The selected TestAsset codec is unavailable.");
        }

        var context = new AssetCodecContext(
            new AssetId(1),
            s_schemas,
            SpatialProfileId.Default,
            SpatialSettings.Default,
            EbinReadLimits.Foundation);
        var envelope = CreateAssetEnvelope(
            1,
            CreateAssetRecordWithReference(123, 2, s_assetSchema.SchemaId, checked((uint)s_assetSchema.Major)));
        var failure = AssetEnvelopeDecoder.DecodeBounded(
            envelope,
            new AssetId(1),
            codec,
            context,
            EbinReadLimits.Foundation,
            [],
            [],
            [],
            AssetBoundedFileDisposition.NotFinal,
            out var value,
            out var referenceCount);

        Assert.Multiple(() => {
            Assert.That(failure.Stage, Is.EqualTo(AssetBoundedFailureStage.Reference));
            Assert.That(failure.Kind, Is.EqualTo(AssetBoundedFailureKind.Invariant));
            Assert.That(value.Marker, Is.Zero);
            Assert.That(referenceCount, Is.Zero);
        });
    }

    [Test]
    public void AssetEnvelopeDecoderBoundedDecodeRejectsContextRootMismatch()
    {
        var registration = CreateRegistration();

        if (!registration.TryGetCodec<TestAssetValue>(out var codec))
        {
            throw new InvalidOperationException("The selected TestAsset codec is unavailable.");
        }

        var context = new AssetCodecContext(
            new AssetId(2),
            s_schemas,
            SpatialProfileId.Default,
            SpatialSettings.Default,
            EbinReadLimits.Foundation);
        var envelope = CreateAssetEnvelope(1, CreateAssetRecord(123));
        var failure = AssetEnvelopeDecoder.DecodeBounded(
            envelope,
            new AssetId(1),
            codec,
            context,
            EbinReadLimits.Foundation,
            [],
            [],
            [],
            AssetBoundedFileDisposition.NotFinal,
            out var value,
            out var referenceCount);

        Assert.Multiple(() => {
            Assert.That(failure.Stage, Is.EqualTo(AssetBoundedFailureStage.Header));
            Assert.That(failure.Kind, Is.EqualTo(AssetBoundedFailureKind.Invariant));
            Assert.That(value.Marker, Is.Zero);
            Assert.That(referenceCount, Is.Zero);
        });
    }

    [Test]
    public void AssetEnvelopeDecoderBoundedDecodeUsesCallerReferenceStorage()
    {
        var registration = CreateRegistration();

        if (!registration.TryGetCodec<TestAssetValue>(out var codec))
        {
            throw new InvalidOperationException("The selected TestAsset codec is unavailable.");
        }

        var context = new AssetCodecContext(
            new AssetId(1),
            s_schemas,
            SpatialProfileId.Default,
            SpatialSettings.Default,
            EbinReadLimits.Foundation);
        var envelope = CreateAssetEnvelope(
            1,
            CreateAssetRecordWithReference(123, 2, s_assetSchema.SchemaId, checked((uint)s_assetSchema.Major)));
        var referenceWorkspace = new AssetReference[1];
        var referenceIndexWorkspace = new int[1];
        var uniqueReferences = new AssetReference[1];
        var failure = AssetEnvelopeDecoder.DecodeBounded(
            envelope,
            new AssetId(1),
            codec,
            context,
            EbinReadLimits.Foundation,
            referenceWorkspace,
            referenceIndexWorkspace,
            uniqueReferences,
            AssetBoundedFileDisposition.NotFinal,
            out var value,
            out var referenceCount);

        Assert.Multiple(() => {
            Assert.That(failure.Kind, Is.EqualTo(AssetBoundedFailureKind.None));
            Assert.That(value.Marker, Is.EqualTo(123));
            Assert.That(referenceCount, Is.EqualTo(1));
            Assert.That(uniqueReferences[0], Is.EqualTo(new AssetReference(new AssetId(2), s_assetSchema.SchemaId, 1)));
        });
    }

    [Test]
    public async Task AssetLeasePackageLoadPreCancellationReturnsTemporaryBackingReference()
    {
        var profileOrigin = typeof(ProfiledTestAssetOrigin);
        var registration = new AssetStoreRegistrationBuilder(s_schemas)
            .RegisterProfiledCodec(
                new ContextualTestAssetCodec(),
                profileOrigin,
                AssetPackagePayloadCodec.None)
            .Build();
        var package = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 10)),
            new AssetPackageReaderTests.TestEntry(2, CreateAssetEnvelope(2, 20)));
        var path = AssetPackageReaderTests.WritePackage(package);
        var store = await AssetStore.CreateAsync(
            CreateOptions(path, package, registration),
            CancellationToken.None).ConfigureAwait(false);
        var backingField = typeof(AssetStore).GetField("_currentBacking", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The selected package backing field is missing.");
        var referenceCountField = typeof(AssetPackageBacking).GetField("_referenceCount", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The package backing reference count field is missing.");
        var liveLeaseCountField = typeof(AssetStore).GetField("_liveAssetLeases", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The live lease count field is missing.");

        try
        {
            using var source = await store.LoadAsync<TestAssetValue>(new AssetId(1), CancellationToken.None).ConfigureAwait(false);
            var descriptor = source.DescribeOwnedRead<TestAssetValue>(new AssetId(2), profileOrigin);
            var backing = backingField.GetValue(store) as AssetPackageBacking
                ?? throw new InvalidOperationException("The selected package backing is missing.");
            var referenceCountBefore = (int)(referenceCountField.GetValue(backing)
                ?? throw new InvalidOperationException("The package backing reference count is unavailable."));
            using var cancellationSource = new CancellationTokenSource();
            await cancellationSource.CancelAsync().ConfigureAwait(false);

            var cancellationException = Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await source.LoadFromPackageAsync<TestAssetValue>(
                    new AssetId(2),
                    cancellationSource.Token).ConfigureAwait(false));

            Assert.That(cancellationException?.CancellationToken, Is.EqualTo(cancellationSource.Token));
            Assert.That(referenceCountField.GetValue(backing), Is.EqualTo(referenceCountBefore));

            using var packageLease = store.AcquirePackageLease();
            var referenceCountBeforePackageRead = (int)(referenceCountField.GetValue(backing)
                ?? throw new InvalidOperationException("The package backing reference count is unavailable."));
            var packageCancellationException = Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await packageLease.LoadFromPackageAsync<TestAssetValue>(
                    new AssetId(2),
                    cancellationSource.Token).ConfigureAwait(false));

            Assert.That(packageCancellationException?.CancellationToken, Is.EqualTo(cancellationSource.Token));
            Assert.That(referenceCountField.GetValue(backing), Is.EqualTo(referenceCountBeforePackageRead));

            var leaseDescriptorCancellation = Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await source.LoadDescribedFromPackageAsync<TestAssetValue>(
                    descriptor,
                    cancellationSource.Token).ConfigureAwait(false));
            Assert.That(leaseDescriptorCancellation?.CancellationToken, Is.EqualTo(cancellationSource.Token));
            Assert.That(referenceCountField.GetValue(backing), Is.EqualTo(referenceCountBeforePackageRead));

            var packageDescriptorCancellation = Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await packageLease.LoadDescribedFromPackageAsync<TestAssetValue>(
                    descriptor,
                    cancellationSource.Token).ConfigureAwait(false));
            Assert.That(packageDescriptorCancellation?.CancellationToken, Is.EqualTo(cancellationSource.Token));
            Assert.That(referenceCountField.GetValue(backing), Is.EqualTo(referenceCountBeforePackageRead));

            var liveLeaseCountBeforeMissingLoads = liveLeaseCountField.GetValue(store);
            _ = Assert.ThrowsAsync<KeyNotFoundException>(async () =>
                await source.LoadFromPackageAsync<TestAssetValue>(
                    new AssetId(3),
                    CancellationToken.None).ConfigureAwait(false));
            Assert.That(referenceCountField.GetValue(backing), Is.EqualTo(referenceCountBeforePackageRead));
            Assert.That(liveLeaseCountField.GetValue(store), Is.EqualTo(liveLeaseCountBeforeMissingLoads));

            _ = Assert.ThrowsAsync<KeyNotFoundException>(async () =>
                await packageLease.LoadFromPackageAsync<TestAssetValue>(
                    new AssetId(3),
                    CancellationToken.None).ConfigureAwait(false));
            Assert.That(referenceCountField.GetValue(backing), Is.EqualTo(referenceCountBeforePackageRead));
            Assert.That(liveLeaseCountField.GetValue(store), Is.EqualTo(liveLeaseCountBeforeMissingLoads));

            using var child = await source.LoadFromPackageAsync<TestAssetValue>(
                new AssetId(2),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(child.Value.Marker, Is.EqualTo(20));
            Assert.That(referenceCountField.GetValue(backing), Is.EqualTo(referenceCountBeforePackageRead + 1));
            child.Dispose();
            Assert.That(referenceCountField.GetValue(backing), Is.EqualTo(referenceCountBeforePackageRead));

            var liveLeaseCountBeforeStoreClose = liveLeaseCountField.GetValue(store);
            await store.DisposeAsync().ConfigureAwait(false);
            var referenceCountAfterStoreClose = (int)(referenceCountField.GetValue(backing)
                ?? throw new InvalidOperationException("The package backing reference count is unavailable."));

            Assert.That(referenceCountAfterStoreClose, Is.EqualTo(referenceCountBeforePackageRead - 1));

            _ = Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await source.LoadFromPackageAsync<TestAssetValue>(
                    new AssetId(2),
                    CancellationToken.None).ConfigureAwait(false));
            Assert.That(referenceCountField.GetValue(backing), Is.EqualTo(referenceCountAfterStoreClose));
            Assert.That(liveLeaseCountField.GetValue(store), Is.EqualTo(liveLeaseCountBeforeStoreClose));

            _ = Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await packageLease.LoadFromPackageAsync<TestAssetValue>(
                    new AssetId(2),
                    CancellationToken.None).ConfigureAwait(false));
            Assert.That(referenceCountField.GetValue(backing), Is.EqualTo(referenceCountAfterStoreClose));
            Assert.That(liveLeaseCountField.GetValue(store), Is.EqualTo(liveLeaseCountBeforeStoreClose));

            _ = Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await source.LoadDescribedFromPackageAsync<TestAssetValue>(
                    descriptor,
                    CancellationToken.None).ConfigureAwait(false));
            Assert.That(referenceCountField.GetValue(backing), Is.EqualTo(referenceCountAfterStoreClose));
            Assert.That(liveLeaseCountField.GetValue(store), Is.EqualTo(liveLeaseCountBeforeStoreClose));

            _ = Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await packageLease.LoadDescribedFromPackageAsync<TestAssetValue>(
                    descriptor,
                    CancellationToken.None).ConfigureAwait(false));
            Assert.That(referenceCountField.GetValue(backing), Is.EqualTo(referenceCountAfterStoreClose));
            Assert.That(liveLeaseCountField.GetValue(store), Is.EqualTo(liveLeaseCountBeforeStoreClose));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(path);
        }
    }

    [Test]
    public async Task AssetLeaseRetainsAndLoadsFromItsAnchoredPackageAcrossReplacement()
    {
        var registration = CreateRegistration();
        var firstPackage = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 10)),
            new AssetPackageReaderTests.TestEntry(2, CreateAssetEnvelope(2, 20)));
        var secondPackage = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 100)),
            new AssetPackageReaderTests.TestEntry(2, CreateAssetEnvelope(2, 200)));
        var firstPath = AssetPackageReaderTests.WritePackage(firstPackage);
        var secondPath = AssetPackageReaderTests.WritePackage(secondPackage);
        var store = await AssetStore.CreateAsync(
            CreateOptions(firstPath, firstPackage, registration),
            CancellationToken.None).ConfigureAwait(false);

        try
        {
            using var source = await store.LoadAsync<TestAssetValue>(new AssetId(1), CancellationToken.None).ConfigureAwait(false);
            var sourceBackingToken = source.BackingToken;
            using var retained = source.Retain();
            await store.ReplacePackageAsync(
                CreateOptions(secondPath, secondPackage, registration),
                CancellationToken.None).ConfigureAwait(false);

            using var anchoredChild = await source.LoadFromPackageAsync<TestAssetValue>(new AssetId(2), CancellationToken.None).ConfigureAwait(false);
            using var currentChild = await store.LoadAsync<TestAssetValue>(new AssetId(2), CancellationToken.None).ConfigureAwait(false);

            Assert.That(retained.Value.Marker, Is.EqualTo(10));
            Assert.That(anchoredChild.Value.Marker, Is.EqualTo(20));
            Assert.That(currentChild.Value.Marker, Is.EqualTo(200));
            Assert.That(retained.BackingToken, Is.SameAs(sourceBackingToken));
            Assert.That(anchoredChild.BackingToken, Is.SameAs(sourceBackingToken));
            Assert.That(currentChild.BackingToken, Is.Not.SameAs(sourceBackingToken));
            Assert.That(retained.ProfileId, Is.EqualTo(source.ProfileId));
            Assert.That(retained.SpatialSettings, Is.EqualTo(source.SpatialSettings));

            await store.DisposeAsync().ConfigureAwait(false);

            _ = Assert.Throws<ObjectDisposedException>(() => retained.Retain());
            _ = Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await source.LoadFromPackageAsync<TestAssetValue>(new AssetId(2), CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }

    [Test]
    public void AssetPackageBackingFailedFinalStreamReleaseRootsBackingForProcessLifetime()
    {
        var path = Path.GetTempFileName();

        try
        {
            var weakBacking = CreateBackingWithFailedClose(path);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.That(weakBacking.TryGetTarget(out _), Is.True);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task AssetPackageLeaseDescribesProfiledReadWithoutAcquiringPackageLease()
    {
        var profileOrigin = typeof(ProfiledTestAssetOrigin);
        var registration = new AssetStoreRegistrationBuilder(s_schemas)
            .RegisterProfiledCodec(
                new ContextualTestAssetCodec(),
                profileOrigin,
                AssetPackagePayloadCodec.None)
            .Build();
        var package = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 17)));
        var path = AssetPackageReaderTests.WritePackage(package);
        var options = CreateOptions(
            path,
            package,
            registration,
            maximumLiveAssetLeases: 1);
        var store = await AssetStore.CreateAsync(options, CancellationToken.None).ConfigureAwait(false);
        AssetPackageLease? packageLease = null;

        try
        {
            packageLease = store.AcquirePackageLease();
            Assert.That(
                packageLease.TryDescribeOwnedRead<TestAssetValue>(new AssetId(1), profileOrigin, out var descriptor),
                Is.True);
            Assert.That(
                registration.TryGetProfiledCodecReceipt<TestAssetValue>(profileOrigin, out var registeredReceipt),
                Is.True);
            var storeDescriptor = store.DescribeOwnedRead<TestAssetValue>(new AssetId(1), profileOrigin);
            var packageDescriptor = packageLease.DescribeOwnedRead<TestAssetValue>(new AssetId(1), profileOrigin);

            Assert.Multiple(() => {
                Assert.That(descriptor.AssetId, Is.EqualTo(new AssetId(1)));
                Assert.That(storeDescriptor.BackingToken, Is.SameAs(descriptor.BackingToken));
                Assert.That(packageDescriptor.BackingToken, Is.SameAs(descriptor.BackingToken));
                Assert.That(descriptor.BackingToken, Is.Not.Null);
                Assert.That(descriptor.CodecReceipt.IsInitialized, Is.True);
                Assert.That(descriptor.CodecReceipt.IsSameCodec(registeredReceipt), Is.True);
                Assert.That(descriptor.CodecReceipt.ProfileOrigin, Is.SameAs(profileOrigin));
                Assert.That(
                    descriptor.CodecReceipt.RequiredPackageCodec,
                    Is.EqualTo(AssetPackagePayloadCodec.None));
                Assert.That(descriptor.CodecReceipt.Schema, Is.EqualTo(s_assetSchema));
                Assert.That(descriptor.RequiredPackageCodec, Is.EqualTo(AssetPackagePayloadCodec.None));
                Assert.That(descriptor.CodecReceipt.ValueType, Is.EqualTo(typeof(TestAssetValue)));
                Assert.That(descriptor.CpuAssetBudgetBytes, Is.EqualTo(options.CpuAssetBudgetBytes));
                Assert.That(descriptor.DecodedLength, Is.GreaterThan(0));
                Assert.That(descriptor.EntryCodec, Is.Zero);
                Assert.That(descriptor.MaximumConcurrentDecodeBytes, Is.EqualTo(options.MaximumConcurrentDecodeBytes));
                Assert.That(descriptor.MaximumDecodedAssetBytes, Is.EqualTo(options.MaximumDecodedAssetBytes));
                Assert.That(descriptor.MaximumLiveAssetLeases, Is.EqualTo(options.MaximumLiveAssetLeases));
                Assert.That(descriptor.PackageByteLength, Is.EqualTo(new FileInfo(path).Length));
                Assert.That(descriptor.Registration, Is.SameAs(registration));
                Assert.That(descriptor.Schemas, Is.SameAs(s_schemas));
                Assert.That(descriptor.Store, Is.SameAs(store));
                Assert.That(descriptor.StoredLength, Is.GreaterThan(0));
            });

            Assert.That(
                packageLease.TryDescribeOwnedRead<TestAssetValue>(new AssetId(2), profileOrigin, out _),
                Is.False);
            _ = Assert.Throws<KeyNotFoundException>(
                () => store.DescribeOwnedRead<TestAssetValue>(new AssetId(2), profileOrigin));
            Assert.That(
                packageLease.TryDescribeOwnedRead<TestAssetValue>(new AssetId(1), new object(), out _),
                Is.False);
            _ = Assert.Throws<NotSupportedException>(
                () => packageLease.DescribeOwnedRead<TestAssetValue>(new AssetId(1), new object()));
            _ = Assert.Throws<InvalidOperationException>(() => store.AcquirePackageLease());

            packageLease.Dispose();
            packageLease = null;
            using var replacementLease = store.AcquirePackageLease();
        }
        finally
        {
            packageLease?.Dispose();
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(path);
        }
    }

    [Test]
    public async Task AssetPackageLeaseLoadsFromItsPinnedPackageAfterReplacement()
    {
        var registration = CreateRegistration();
        var originalPackage = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 17)));
        var replacementPackage = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 23)));
        var originalPath = AssetPackageReaderTests.WritePackage(originalPackage);
        var replacementPath = AssetPackageReaderTests.WritePackage(replacementPackage);
        var store = await AssetStore.CreateAsync(
            CreateOptions(originalPath, originalPackage, registration),
            CancellationToken.None).ConfigureAwait(false);

        try
        {
            using var packageLease = store.AcquirePackageLease();
            await store.ReplacePackageAsync(
                CreateOptions(replacementPath, replacementPackage, registration),
                CancellationToken.None).ConfigureAwait(false);

            using var pinnedAsset = await packageLease.LoadFromPackageAsync<TestAssetValue>(new AssetId(1), CancellationToken.None).ConfigureAwait(false);
            using var currentAsset = await store.LoadAsync<TestAssetValue>(new AssetId(1), CancellationToken.None).ConfigureAwait(false);

            Assert.That(pinnedAsset.Value.Marker, Is.EqualTo(17));
            Assert.That(currentAsset.Value.Marker, Is.EqualTo(23));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(originalPath);
            File.Delete(replacementPath);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AssetPackageLeaseReferenceInspectionHonorsConcurrentDecodeBudget(bool explicitMinor)
    {
        var registration = CreateRegistration();
        var payload = CreateAssetEnvelope(1, 52);
        var package = AssetPackageReaderTests.CreatePackage(new AssetPackageReaderTests.TestEntry(1, payload));
        var path = AssetPackageReaderTests.WritePackage(package);

        try
        {
            var store = await AssetStore.CreateAsync(
                CreateOptions(path, package, registration, maximumConcurrentDecodeBytes: payload.Length - 1),
                CancellationToken.None).ConfigureAwait(false);

            try
            {
                using var packageLease = store.AcquirePackageLease();
                var reference = new AssetReference(new AssetId(1), s_assetSchema.SchemaId, s_assetSchema.Major);

                _ = Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await (explicitMinor
                        ? packageLease.MatchesAssetReferenceAsync(reference, requiredMinor: 0, CancellationToken.None)
                        : packageLease.MatchesAssetReferenceAsync(reference, CancellationToken.None)).ConfigureAwait(false));
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AssetPackageLeaseReferenceMinorInspectionKeepsTheExactBackingAndCodecChecks(bool registerCodec)
    {
        var registration = registerCodec ? CreateRegistration() : new AssetStoreRegistrationBuilder(s_schemas).Build();
        var payload = CreateAssetEnvelope(1, 52);
        var originalPackage = AssetPackageReaderTests.CreatePackage(new AssetPackageReaderTests.TestEntry(1, payload));
        var replacementPackage = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(2, CreateAssetEnvelope(2, 73)));
        var directory = Path.GetFullPath(Path.Combine("artifacts", "tst", "asset-reference-minors", Guid.NewGuid().ToString("N")));
        _ = Directory.CreateDirectory(directory);
        var originalPath = Path.Combine(directory, "original.epkg");
        var replacementPath = Path.Combine(directory, "replacement.epkg");
        await File.WriteAllBytesAsync(originalPath, originalPackage).ConfigureAwait(false);
        await File.WriteAllBytesAsync(replacementPath, replacementPackage).ConfigureAwait(false);

        try
        {
            var store = await AssetStore.CreateAsync(
                CreateOptions(originalPath, originalPackage, registration, maximumConcurrentDecodeBytes: payload.Length)).ConfigureAwait(false);
            await using (store.ConfigureAwait(false))
            {
                using var packageLease = store.AcquirePackageLease();
                var reference = new AssetReference(new AssetId(1), s_assetSchema.SchemaId, s_assetSchema.Major);
                _ = Assert.Throws<ArgumentOutOfRangeException>(
                    () => packageLease.MatchesAssetReferenceAsync(reference, requiredMinor: -1, CancellationToken.None));

                using var cancellation = new CancellationTokenSource();
                await cancellation.CancelAsync().ConfigureAwait(false);
                _ = Assert.ThrowsAsync<OperationCanceledException>(
                    async () => await packageLease.MatchesAssetReferenceAsync(reference, requiredMinor: 0, cancellation.Token).ConfigureAwait(false));
                Assert.That(await packageLease.MatchesAssetReferenceAsync(
                    reference, requiredMinor: 1, CancellationToken.None).ConfigureAwait(false), Is.False);
                Assert.That(await packageLease.MatchesAssetReferenceAsync(
                    new AssetReference(reference.AssetId, Guid.NewGuid(), reference.SchemaMajor), requiredMinor: 0, CancellationToken.None).ConfigureAwait(false), Is.False);
                Assert.That(await packageLease.MatchesAssetReferenceAsync(
                    new AssetReference(reference.AssetId, reference.SchemaId, 2), requiredMinor: 0, CancellationToken.None).ConfigureAwait(false), Is.False);

                await store.ReplacePackageAsync(CreateOptions(
                    replacementPath, replacementPackage, registration, maximumConcurrentDecodeBytes: payload.Length)).ConfigureAwait(false);
                using var currentPackage = store.AcquirePackageLease();
                Assert.That(await currentPackage.MatchesAssetReferenceAsync(
                    reference, requiredMinor: 0, CancellationToken.None).ConfigureAwait(false), Is.False);
                Assert.That(await packageLease.MatchesAssetReferenceAsync(
                    reference, CancellationToken.None).ConfigureAwait(false), Is.EqualTo(registerCodec));

                var pending = packageLease.MatchesAssetReferenceAsync(reference, requiredMinor: 0, CancellationToken.None);
                packageLease.Dispose();
                await store.DisposeAsync().ConfigureAwait(false);
                Assert.That(await pending.ConfigureAwait(false), Is.EqualTo(registerCodec));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void AssetReferenceScannerCopyUniqueReferencesRejectsDestinationOverlappingIndexWorkspace()
    {
        var overlapBytes = new byte[Unsafe.SizeOf<AssetReference>()];

        _ = Assert.Throws<ArgumentException>(
            () => AssetReferenceScanTestHelpers.CopyUniqueReferencesWithOverlappingIndexWorkspace(overlapBytes));
    }

    [Test]
    public void AssetReferenceScannerCopyUniqueReferencesRejectsDestinationOverlappingReferenceWorkspace()
    {
        AssetReference[] references = [
            new(new AssetId(2), new Guid("ffffffff-0000-4000-8000-000000000001"), 1),
        ];
        int[] uniqueIndices = [0];

        _ = Assert.Throws<ArgumentException>(
            () => AssetReferenceScanner.CopyUniqueReferences(
                references,
                uniqueIndices,
                references));
    }

    [Test]
    public void AssetReferenceScannerCopyUniqueReferencesRejectsOverlappingReferenceAndIndexWorkspaces()
    {
        var overlapBytes = new byte[Unsafe.SizeOf<AssetReference>()];

        _ = Assert.Throws<ArgumentException>(
            () => AssetReferenceScanTestHelpers.CopyUniqueReferencesWithOverlappingWorkspaces(overlapBytes));
    }

    [Test]
    public void AssetReferenceScannerCopyUniqueReferencesRejectsShortDestination()
    {
        AssetReference[] references = [
            new(new AssetId(2), new Guid("ffffffff-0000-4000-8000-000000000001"), 1),
            new(new AssetId(2), new Guid("11111111-0000-4000-8000-000000000002"), 1),
        ];
        int[] uniqueIndices = [0, 1];

        _ = Assert.Throws<ArgumentException>(
            () => AssetReferenceScanner.CopyUniqueReferences(
                references,
                uniqueIndices,
                new AssetReference[1]));
    }

    [Test]
    [TestCase(0, 0, 128, 64)]
    [TestCase(1, 1, 176, 96)]
    [TestCase(3, 2, 224, 112)]
    [TestCase(4, 4, 240, 160)]
    [TestCase(257, 257, 7344, 6240)]
    public void AssetReferenceScannerMeasuresFixedScanDataBytes(
        int referenceCount,
        int uniqueCount,
        long expectedWorkspaceBytes,
        long expectedResultBytes)
    {
        var (workspaceBytes, resultBytes) = AssetReferenceScanner.GetFixedScanDataBytes(referenceCount, uniqueCount);

        Assert.Multiple(() => {
            Assert.That(workspaceBytes, Is.EqualTo(expectedWorkspaceBytes));
            Assert.That(resultBytes, Is.EqualTo(expectedResultBytes));
            Assert.That(checked(workspaceBytes + resultBytes), Is.GreaterThan(workspaceBytes));
        });
    }

    [Test]
    [TestCase(0, 0)]
    [TestCase(1, 1)]
    [TestCase(2, 2)]
    [TestCase(3, 2)]
    [TestCase(4, 3)]
    [TestCase(5, 4)]
    [TestCase(6, 4)]
    public void AssetReferenceScannerPreservesFirstOccurrenceOrderForDistinctTriples(
        int referenceCount,
        int expectedUniqueCount)
    {
        var fixture = AssetReferenceScanTestHelpers.CreateFixture(referenceCount);
        var referenceWorkspace = new AssetReference[referenceCount];
        var indexWorkspace = new int[referenceCount];
        var action = new AssetReferenceScanTestHelpers.AssetReferenceScanAction(
            fixture.RootSchema,
            fixture.Schemas,
            referenceWorkspace,
            indexWorkspace);

        Assert.That(
            EbinStructuralReader.Read(
                fixture.EncodedRecord,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref action,
                out var bytesConsumed,
                out var diagnostic),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(bytesConsumed, Is.EqualTo(fixture.EncodedRecord.Length));
        Assert.That(diagnostic, Is.Null);
        Assert.That(action.UniqueCount, Is.EqualTo(expectedUniqueCount));

        var references = new AssetReference[action.UniqueCount];
        AssetReferenceScanner.CopyUniqueReferences(
            referenceWorkspace,
            indexWorkspace.AsSpan(0, action.UniqueCount),
            references);
        AssetReference[] expectedReferences = referenceCount switch {
            0 => [],
            1 => [new AssetReference(new AssetId(3), fixture.SecondSchemaId, 2)],
            2 or 3 => [
                new AssetReference(new AssetId(3), fixture.SecondSchemaId, 2),
                new AssetReference(new AssetId(2), fixture.FirstSchemaId, 1),
            ],
            4 => [
                new AssetReference(new AssetId(3), fixture.SecondSchemaId, 2),
                new AssetReference(new AssetId(2), fixture.FirstSchemaId, 1),
                new AssetReference(new AssetId(2), fixture.FirstSchemaId, 2),
            ],
            _ => [
                new AssetReference(new AssetId(3), fixture.SecondSchemaId, 2),
                new AssetReference(new AssetId(2), fixture.FirstSchemaId, 1),
                new AssetReference(new AssetId(2), fixture.FirstSchemaId, 2),
                new AssetReference(new AssetId(2), fixture.SecondSchemaId, 1),
            ],
        };
        Assert.That(
            references,
            Is.EqualTo(expectedReferences));
    }

    [Test]
    [TestCase(-1, 0)]
    [TestCase(0, 1)]
    [TestCase(1, 1_048_577)]
    public void AssetReferenceScannerRejectsInvalidFixedScanByteCounts(
        int referenceCount,
        int uniqueCount)
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => AssetReferenceScanner.GetFixedScanDataBytes(referenceCount, uniqueCount));
    }

    [Test]
    public void AssetReferenceScannerRejectsOverlappingReferenceAndIndexWorkspaces()
    {
        var fixture = AssetReferenceScanTestHelpers.CreateFixture(3);
        var overlapBytes = new byte[checked(3 * Unsafe.SizeOf<AssetReference>())];

        _ = Assert.Throws<ArgumentException>(
            () => AssetReferenceScanTestHelpers.ExecuteWithWorkspaceOverlap(fixture, overlapBytes));
    }

    [Test]
    public void AssetReferenceScannerRejectsShortIndexWorkspace()
    {
        var fixture = AssetReferenceScanTestHelpers.CreateFixture(3);

        _ = Assert.Throws<ArgumentException>(
            () => AssetReferenceScanTestHelpers.Execute(
                fixture,
                new AssetReference[3],
                new int[2]));
    }

    [Test]
    public void AssetReferenceScannerRejectsShortReferenceWorkspace()
    {
        var fixture = AssetReferenceScanTestHelpers.CreateFixture(3);

        _ = Assert.Throws<ArgumentException>(
            () => AssetReferenceScanTestHelpers.Execute(
                fixture,
                new AssetReference[2],
                new int[3]));
    }

    [Test]
    public void AssetReferenceScannerRejectsWorkspaceOverlappingEncodedRecord()
    {
        var fixture = AssetReferenceScanTestHelpers.CreateFixture(3);

        _ = Assert.Throws<ArgumentException>(
            () => AssetReferenceScanTestHelpers.ExecuteWithRecordOverlap(fixture));
    }

    [Test]
    public async Task AssetStoreConcurrentLeaseDisposeReleasesCapacityOnce()
    {
        var registration = CreateRegistration();
        var package = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 15)));
        var path = AssetPackageReaderTests.WritePackage(package);
        var store = await AssetStore.CreateAsync(
            CreateOptions(path, package, registration, maximumLiveAssetLeases: 1),
            CancellationToken.None).ConfigureAwait(false);

        try
        {
            var lease = await store.LoadAsync<TestAssetValue>(new AssetId(1), CancellationToken.None).ConfigureAwait(false);
            using var ready = new CountdownEvent(4);
            using var start = new ManualResetEventSlim();
            var disposers = new Task[4];

            for (var index = 0; index < disposers.Length; index++)
            {
                disposers[index] = Task.Run(() => {
                    _ = ready.Signal();
                    start.Wait();
                    lease.Dispose();
                });
            }

            var allDisposersReady = ready.Wait(TimeSpan.FromSeconds(10));
            start.Set();
            await Task.WhenAll(disposers).ConfigureAwait(false);
            Assert.That(allDisposersReady, Is.True);

            using var replacementLease = await store.LoadAsync<TestAssetValue>(
                new AssetId(1),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(replacementLease.Value.Marker, Is.EqualTo(15));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(path);
        }
    }

    [Test]
    public async Task AssetStoreConcurrentPackageReplacementKeepsOneSelectedPackage()
    {
        var registration = CreateRegistration();
        var initialPackage = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 15)));
        var initialPath = AssetPackageReaderTests.WritePackage(initialPackage);
        byte[][] replacementPackages = [
            AssetPackageReaderTests.CreatePackage(new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 21))),
            AssetPackageReaderTests.CreatePackage(new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 22))),
            AssetPackageReaderTests.CreatePackage(new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 23))),
            AssetPackageReaderTests.CreatePackage(new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 24))),
        ];
        var replacementPaths = replacementPackages
            .Select(AssetPackageReaderTests.WritePackage)
            .ToArray();
        var store = await AssetStore.CreateAsync(
            CreateOptions(initialPath, initialPackage, registration),
            CancellationToken.None).ConfigureAwait(false);

        try
        {
            using var ready = new CountdownEvent(replacementPaths.Length);
            using var start = new ManualResetEventSlim();
            var replacements = new Task<bool>[replacementPaths.Length];

            for (var index = 0; index < replacements.Length; index++)
            {
                var replacementIndex = index;
                replacements[index] = Task.Run(async () => {
                    _ = ready.Signal();
                    start.Wait();

                    try
                    {
                        await store.ReplacePackageAsync(
                            CreateOptions(
                                replacementPaths[replacementIndex],
                                replacementPackages[replacementIndex],
                                registration),
                            CancellationToken.None).ConfigureAwait(false);
                        return true;
                    }
                    catch (InvalidOperationException)
                    {
                        return false;
                    }
                });
            }

            var allReplacementsReady = ready.Wait(TimeSpan.FromSeconds(10));
            start.Set();
            var replacementResults = await Task.WhenAll(replacements).ConfigureAwait(false);
            Assert.That(allReplacementsReady, Is.True);
            Assert.That(replacementResults, Does.Contain(true));

            using var selected = await store.LoadAsync<TestAssetValue>(
                new AssetId(1),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(selected.Value.Marker, Is.AnyOf((byte)15, 21, 22, 23, 24));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(initialPath);

            foreach (var path in replacementPaths)
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    public async Task AssetStoreCreateLeaseKeepsCallerBackingUntilHandleConstructionSucceeds()
    {
        var registration = CreateRegistration();
        var package = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 15)));
        var path = AssetPackageReaderTests.WritePackage(package);
        var store = await AssetStore.CreateAsync(
            CreateOptions(path, package, registration),
            CancellationToken.None).ConfigureAwait(false);
        var backingField = typeof(AssetStore).GetField("_currentBacking", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The selected package backing field is missing.");
        var backing = backingField.GetValue(store) as AssetPackageBacking
            ?? throw new InvalidOperationException("The selected package backing is missing.");
        var callerReferenceAcquired = false;
        var storeDisposed = false;

        try
        {
            Assert.That(backing.TryAcquireReference(), Is.True);
            callerReferenceAcquired = true;

            var createLeaseMethod = typeof(AssetStore).GetMethod(
                "CreateLease",
                BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("The package lease factory is missing.");
            var createLease = createLeaseMethod.MakeGenericMethod(typeof(TestAssetValue));
            object?[] arguments = [
                store,
                default(AssetId),
                default(TestAssetValue),
                backing,
                0L,
                false,
            ];
            var invocation = Assert.Throws<TargetInvocationException>(() => createLease.Invoke(null, arguments));

            Assert.That(invocation!.InnerException, Is.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(arguments[5], Is.EqualTo(false));

            await store.DisposeAsync().ConfigureAwait(false);
            storeDisposed = true;
            var callerReferenceStillExists = backing.TryAcquireReference();
            Assert.That(callerReferenceStillExists, Is.True);

            if (callerReferenceStillExists)
            {
                backing.ReleaseReference();
                backing.ReleaseReference();
                callerReferenceAcquired = false;
            }
        }
        finally
        {
            if (!storeDisposed)
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }

            if (callerReferenceAcquired && backing.TryAcquireReference())
            {
                backing.ReleaseReference();
                backing.ReleaseReference();
            }

            File.Delete(path);
        }
    }

    [Test]
    public async Task AssetStoreDescribedReadRejectsBrotliReferenceBeforePayloadRead()
    {
        var profileOrigin = typeof(ProfiledTestAssetOrigin);
        var registration = new AssetStoreRegistrationBuilder(s_schemas)
            .RegisterProfiledCodec(
                new ContextualTestAssetCodec(),
                profileOrigin,
                AssetPackagePayloadCodec.None)
            .Build();
        var rootPayload = CreateAssetEnvelope(
            1,
            CreateAssetRecordWithReference(17, 2, s_assetSchema.SchemaId, checked((uint)s_assetSchema.Major)));
        var package = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, rootPayload),
            new AssetPackageReaderTests.TestEntry(
                2,
                [0xFF],
                Codec: (byte)AssetPackagePayloadCodec.Brotli,
                DecodedLength: 1));
        var path = AssetPackageReaderTests.WritePackage(package);
        var store = await AssetStore.CreateAsync(
            CreateOptions(path, package, registration),
            CancellationToken.None).ConfigureAwait(false);

        try
        {
            Assert.That(
                store.TryDescribeOwnedRead<TestAssetValue>(new AssetId(1), profileOrigin, out var descriptor),
                Is.True);
            _ = Assert.ThrowsAsync<NotSupportedException>(
                async () => await store.LoadDescribedAsync<TestAssetValue>(
                    descriptor,
                    CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(path);
        }
    }

    [Test]
    public async Task AssetStoreDescribedReadsRejectReplacementAndRetainPinnedSource()
    {
        var profileOrigin = typeof(ProfiledTestAssetOrigin);
        var registration = new AssetStoreRegistrationBuilder(s_schemas)
            .RegisterProfiledCodec(
                new ContextualTestAssetCodec(),
                profileOrigin,
                AssetPackagePayloadCodec.None)
            .Build();
        var originalPackage = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 17)),
            new AssetPackageReaderTests.TestEntry(2, CreateAssetEnvelope(2, 18)));
        var replacementPackage = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 23)),
            new AssetPackageReaderTests.TestEntry(2, CreateAssetEnvelope(2, 24)));
        var originalPath = AssetPackageReaderTests.WritePackage(originalPackage);
        var replacementPath = AssetPackageReaderTests.WritePackage(replacementPackage);
        var originalOptions = CreateOptions(
            originalPath,
            originalPackage,
            registration,
            maximumLiveAssetLeases: 2);
        var replacementOptions = CreateOptions(
            replacementPath,
            replacementPackage,
            registration,
            maximumLiveAssetLeases: 2);
        var store = await AssetStore.CreateAsync(originalOptions, CancellationToken.None).ConfigureAwait(false);
        AssetPackageLease? packageLease = null;

        try
        {
            packageLease = store.AcquirePackageLease();
            Assert.That(
                store.TryDescribeOwnedRead<TestAssetValue>(new AssetId(1), profileOrigin, out var originalDescriptor),
                Is.True);
            using (var sourceAsset = await store.LoadAsync<TestAssetValue>(
                new AssetId(1),
                CancellationToken.None).ConfigureAwait(false))
            {
                var assetDescriptor = sourceAsset.DescribeOwnedRead<TestAssetValue>(
                    new AssetId(2),
                    profileOrigin);
                Assert.That(assetDescriptor.BackingToken, Is.SameAs(sourceAsset.BackingToken));
            }

            await store.ReplacePackageAsync(replacementOptions, CancellationToken.None).ConfigureAwait(false);
            _ = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await store.LoadDescribedAsync<TestAssetValue>(
                    originalDescriptor,
                    CancellationToken.None).ConfigureAwait(false));

            using (var currentAsset = await store.LoadAsync<TestAssetValue>(
                new AssetId(1),
                CancellationToken.None).ConfigureAwait(false))
            {
                Assert.That(currentAsset.Value.Marker, Is.EqualTo(23));
            }

            Assert.That(
                store.TryDescribeOwnedRead<TestAssetValue>(new AssetId(1), profileOrigin, out var replacementDescriptor),
                Is.True);
            using (var currentDescribedAsset = await store.LoadDescribedAsync<TestAssetValue>(
                replacementDescriptor,
                CancellationToken.None).ConfigureAwait(false))
            {
                Assert.That(currentDescribedAsset.Value.Marker, Is.EqualTo(23));
            }

            using var originalAsset = await packageLease.LoadDescribedFromPackageAsync<TestAssetValue>(
                originalDescriptor,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(originalAsset.Value.Marker, Is.EqualTo(17));
            Assert.That(
                originalAsset.TryDescribeOwnedRead<TestAssetValue>(
                    new AssetId(2),
                    profileOrigin,
                    out var originalChildDescriptor),
                Is.True);

            packageLease.Dispose();
            packageLease = null;

            using var originalChild = await originalAsset.LoadDescribedFromPackageAsync<TestAssetValue>(
                originalChildDescriptor,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(originalChild.Value.Marker, Is.EqualTo(18));
        }
        finally
        {
            packageLease?.Dispose();
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(originalPath);
            File.Delete(replacementPath);
        }
    }

    [Test]
    public async Task AssetStoreDisposeAsyncJoinsActivePackageReplacement()
    {
        var registration = CreateRegistration();
        var originalPackage = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 15)));
        var replacementPackage = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 16)));
        var originalPath = AssetPackageReaderTests.WritePackage(originalPackage);
        var replacementPath = AssetPackageReaderTests.WritePackage(replacementPackage);
        var store = await AssetStore.CreateAsync(
            CreateOptions(originalPath, originalPackage, registration),
            CancellationToken.None).ConfigureAwait(false);
        Task? replacementTask = null;
        Task? disposeTask = null;
        Task? backingGateTask = null;

        try
        {
            var currentBackingField = typeof(AssetStore).GetField(
                "_currentBacking",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("The selected package backing field was not found.");
            var currentBacking = currentBackingField.GetValue(store)
                ?? throw new InvalidOperationException("The selected package backing was not initialized.");
            var backingGateField = typeof(AssetPackageBacking).GetField(
                "_gate",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("The package backing gate was not found.");
            var backingGate = backingGateField.GetValue(currentBacking) as Lock
                ?? throw new InvalidOperationException("The package backing gate has an unexpected type.");
            var pendingBackingField = typeof(AssetStore).GetField(
                "_pendingReleaseBacking",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("The pending package backing field was not found.");
            using var backingGateHeld = new ManualResetEventSlim();
            using var releaseBackingGate = new ManualResetEventSlim();
            backingGateTask = Task.Run(() => {
                using var backingGateScope = backingGate.EnterScope();
                backingGateHeld.Set();
                releaseBackingGate.Wait();
            });

            try
            {
                Assert.That(backingGateHeld.Wait(TimeSpan.FromSeconds(10)), Is.True);
                replacementTask = store.ReplacePackageAsync(
                    CreateOptions(replacementPath, replacementPackage, registration),
                    CancellationToken.None);

                Assert.That(
                    SpinWait.SpinUntil(() => pendingBackingField.GetValue(store) is not null, TimeSpan.FromSeconds(10)),
                    Is.True);
                disposeTask = store.DisposeAsync().AsTask();
                Assert.That(disposeTask.IsCompleted, Is.False);
            }
            finally
            {
                releaseBackingGate.Set();
            }

            await backingGateTask.ConfigureAwait(false);
            await Task.WhenAll(replacementTask, disposeTask).ConfigureAwait(false);
            replacementTask = null;
            disposeTask = null;
        }
        finally
        {
            try
            {
                if (replacementTask is not null)
                {
                    await replacementTask.ConfigureAwait(false);
                }

                if (disposeTask is not null)
                {
                    await disposeTask.ConfigureAwait(false);
                }
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
                File.Delete(originalPath);
                File.Delete(replacementPath);
            }
        }
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
    public async Task AssetStoreLoadsAssetFromPackageLeaseAfterPackageReplacement()
    {
        var registration = CreateRegistration();
        var originalPackage = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(
                1,
                CreateAssetEnvelope(1, CreateAssetRecordWithReference(10, 2, s_assetSchema.SchemaId, 1))),
            new AssetPackageReaderTests.TestEntry(2, CreateAssetEnvelope(2, 20)));
        var replacementPackage = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 11)),
            new AssetPackageReaderTests.TestEntry(2, CreateAssetEnvelope(2, 21)));
        var originalPath = AssetPackageReaderTests.WritePackage(originalPackage);
        var replacementPath = AssetPackageReaderTests.WritePackage(replacementPackage);
        var store = await AssetStore.CreateAsync(
            CreateOptions(originalPath, originalPackage, registration),
            CancellationToken.None).ConfigureAwait(false);
        AssetLease<TestAssetValue>? sourceLease = null;
        AssetLease<TestAssetValue>? referencedLease = null;

        try
        {
            var source = await store.LoadAsync<TestAssetValue>(new AssetId(1), CancellationToken.None).ConfigureAwait(false);
            sourceLease = source;
            Assert.That(source.Value.Marker, Is.EqualTo(10));

            await store.ReplacePackageAsync(
                CreateOptions(replacementPath, replacementPackage, registration),
                CancellationToken.None).ConfigureAwait(false);

            using (var currentLease = await store.LoadAsync<TestAssetValue>(new AssetId(2), CancellationToken.None).ConfigureAwait(false))
            {
                Assert.That(currentLease.Value.Marker, Is.EqualTo(21));
            }

            var referenced = await source.LoadFromPackageAsync<TestAssetValue>(
                new AssetId(2),
                CancellationToken.None).ConfigureAwait(false);
            referencedLease = referenced;
            Assert.That(referenced.Value.Marker, Is.EqualTo(20));

            await store.DisposeAsync().ConfigureAwait(false);
            Assert.That(referenced.Value.Marker, Is.EqualTo(20));
            _ = Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await source.LoadFromPackageAsync<TestAssetValue>(
                    new AssetId(2),
                    CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            sourceLease?.Dispose();
            referencedLease?.Dispose();
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(originalPath);
            File.Delete(replacementPath);
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
    public async Task AssetStoreLoadsProfiledRegistrationBrotliPayloadsWithoutChangingOrdinaryLoad()
    {
        var profileOrigin = typeof(ProfiledTestAssetOrigin);
        var registration = new AssetStoreRegistrationBuilder(s_schemas)
            .RegisterProfiledCodec(
                new ContextualTestAssetCodec(),
                profileOrigin,
                AssetPackagePayloadCodec.None)
            .Build();
        var encoded = CreateAssetEnvelope(1, 23);
        byte[] compressed;

        using (var output = new MemoryStream())
        {
            using (var compressor = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                compressor.Write(encoded);
            }

            compressed = output.ToArray();
        }

        var package = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(
                1,
                compressed,
                Codec: (byte)AssetPackagePayloadCodec.Brotli,
                DecodedLength: encoded.Length));
        var path = AssetPackageReaderTests.WritePackage(package);
        var store = await AssetStore.CreateAsync(
            CreateOptions(path, package, registration),
            CancellationToken.None).ConfigureAwait(false);

        try
        {
            Assert.That(
                store.TryDescribeOwnedRead<TestAssetValue>(new AssetId(1), profileOrigin, out _),
                Is.False);
            _ = Assert.Throws<NotSupportedException>(
                () => store.DescribeOwnedRead<TestAssetValue>(new AssetId(1), profileOrigin));
            using var lease = await store.LoadAsync<TestAssetValue>(
                new AssetId(1),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(lease.Value.Marker, Is.EqualTo(23));
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
    public async Task AssetStoreReferenceValidationAccountsForTargetLargerThanParent()
    {
        var registration = CreateRegistration();
        var parentEnvelope = CreateAssetEnvelope(
            1,
            CreateAssetRecordWithReference(52, 2, s_assetSchema.SchemaId, checked((uint)s_assetSchema.Major)));
        var targetEnvelope = CreateAssetEnvelope(2, CreateOpaqueAssetRecord(4096));
        Assert.That(targetEnvelope.Length, Is.GreaterThan(parentEnvelope.Length));
        var package = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, parentEnvelope),
            new AssetPackageReaderTests.TestEntry(2, targetEnvelope));
        var path = AssetPackageReaderTests.WritePackage(package);

        try
        {
            var undersizedStore = await AssetStore.CreateAsync(
                CreateOptions(
                    path,
                    package,
                    registration,
                    maximumConcurrentDecodeBytes: parentEnvelope.Length),
                CancellationToken.None).ConfigureAwait(false);

            try
            {
                _ = Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await undersizedStore.LoadAsync<TestAssetValue>(
                        new AssetId(1),
                        CancellationToken.None).ConfigureAwait(false));
            }
            finally
            {
                await undersizedStore.DisposeAsync().ConfigureAwait(false);
            }

            var sufficientStore = await AssetStore.CreateAsync(
                CreateOptions(
                    path,
                    package,
                    registration,
                    maximumConcurrentDecodeBytes: targetEnvelope.Length),
                CancellationToken.None).ConfigureAwait(false);

            try
            {
                using var lease = await sufficientStore.LoadAsync<TestAssetValue>(
                    new AssetId(1),
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(lease.Value.Marker, Is.EqualTo(52));
            }
            finally
            {
                await sufficientStore.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
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
            _ = Assert.Throws<InvalidOperationException>(() => firstLease.Retain());

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
    public async Task AssetStoreRetainHonorsLiveLeaseCapacity()
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
            using var lease = await store.LoadAsync<TestAssetValue>(new AssetId(1), CancellationToken.None).ConfigureAwait(false);
            _ = Assert.Throws<InvalidOperationException>(() => lease.Retain().Dispose());
            Assert.That(lease.Value.Marker, Is.EqualTo(90));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(path);
        }
    }

    [Test]
    public async Task AssetStoreRetainsDecodedValueAndPackageBackingUntilFinalHandleDisposal()
    {
        var registration = CreateRegistration();
        var firstPackage = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 10)));
        var secondPackage = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 20)));
        var firstPath = AssetPackageReaderTests.WritePackage(firstPackage);
        var secondPath = AssetPackageReaderTests.WritePackage(secondPackage);
        var store = await AssetStore.CreateAsync(
            CreateOptions(firstPath, firstPackage, registration, maximumLiveAssetLeases: 2),
            CancellationToken.None).ConfigureAwait(false);
        AssetLease<TestAssetValue>? originalLease = null;
        AssetLease<TestAssetValue>? retainedLease = null;

        try
        {
            var original = await store.LoadAsync<TestAssetValue>(new AssetId(1), CancellationToken.None).ConfigureAwait(false);
            originalLease = original;
            var retained = original.Retain();
            retainedLease = retained;
            Assert.That(retained.Value, Is.EqualTo(original.Value));
            original.Dispose();
            _ = Assert.Throws<ObjectDisposedException>(() => _ = original.Value);
            Assert.That(retained.Value.Marker, Is.EqualTo(10));

            await store.ReplacePackageAsync(
                CreateOptions(secondPath, secondPackage, registration),
                CancellationToken.None).ConfigureAwait(false);
            await store.DisposeAsync().ConfigureAwait(false);

            Assert.That(retained.Value.Marker, Is.EqualTo(10));
            _ = Assert.Throws<ObjectDisposedException>(() => retained.Retain());
        }
        finally
        {
            originalLease?.Dispose();
            retainedLease?.Dispose();
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(firstPath);
            File.Delete(secondPath);
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

    [Test]
    public async Task PackageBackingDoesNotRetainOpenedOptions()
    {
        var package = AssetPackageReaderTests.CreatePackage();
        var path = AssetPackageReaderTests.WritePackage(package);

        try
        {
            var (backing, weakOptions, registration, packageSha256) = await OpenBackingWithoutRetainingOptions(path, package).ConfigureAwait(false);

            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                Assert.That(weakOptions.TryGetTarget(out _), Is.False);
                Assert.That(backing.CpuAssetBudgetBytes, Is.EqualTo(345));
                Assert.That(backing.ExpectedPackageSha256.AsSpan().SequenceEqual(packageSha256), Is.True);
                Assert.That(backing.MaximumConcurrentDecodeBytes, Is.EqualTo(234));
                Assert.That(backing.MaximumDecodedAssetBytes, Is.EqualTo(123));
                Assert.That(backing.MaximumLiveAssetLeases, Is.EqualTo(12));
                Assert.That(backing.Registration, Is.SameAs(registration));
            }
            finally
            {
                backing.ReleaseOwner();
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ReplacePackageValidationPrecedesBusyReplacementAdmission()
    {
        var registration = CreateRegistration();
        var package = AssetPackageReaderTests.CreatePackage(
            new AssetPackageReaderTests.TestEntry(1, CreateAssetEnvelope(1, 15)));
        var path = AssetPackageReaderTests.WritePackage(package);
        var store = await AssetStore.CreateAsync(
            CreateOptions(path, package, registration),
            CancellationToken.None).ConfigureAwait(false);
        var replacementStateField = typeof(AssetStore).GetField(
            "_replacementInProgress",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The AssetStore replacement-admission state was not found.");

        try
        {
            replacementStateField.SetValue(store, 1);
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            _ = Assert.ThrowsAsync<OperationCanceledException>(
                async () => await store.ReplacePackageAsync(
                    CreateOptions(path, package, registration),
                    cancellation.Token).ConfigureAwait(false));
            _ = Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                async () => await store.ReplacePackageAsync(
                    CreateOptions(path, package, registration, maximumLiveAssetLeases: -1),
                    CancellationToken.None).ConfigureAwait(false));
            _ = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await store.ReplacePackageAsync(
                    CreateOptions(path, package, registration),
                    CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            replacementStateField.SetValue(store, 0);
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(path);
        }
    }

    private static class AssetReferenceScanTestHelpers
    {
        internal static void CopyUniqueReferencesWithOverlappingIndexWorkspace(byte[] workspaceBytes)
        {
            AssetReference[] referenceWorkspace = [
                new(new AssetId(2), new Guid("ffffffff-0000-4000-8000-000000000001"), 1),
            ];
            var indexWorkspace = MemoryMarshal.Cast<byte, int>(workspaceBytes.AsSpan(0, sizeof(int)));
            var destination = MemoryMarshal.Cast<byte, AssetReference>(
                workspaceBytes.AsSpan(0, Unsafe.SizeOf<AssetReference>()));
            AssetReferenceScanner.CopyUniqueReferences(
                referenceWorkspace,
                indexWorkspace,
                destination);
        }

        internal static void CopyUniqueReferencesWithOverlappingWorkspaces(byte[] workspaceBytes)
        {
            var referenceWorkspace = MemoryMarshal.Cast<byte, AssetReference>(
                workspaceBytes.AsSpan(0, Unsafe.SizeOf<AssetReference>()));
            var indexWorkspace = MemoryMarshal.Cast<byte, int>(workspaceBytes.AsSpan(0, sizeof(int)));
            AssetReferenceScanner.CopyUniqueReferences(
                referenceWorkspace,
                indexWorkspace,
                new AssetReference[1]);
        }

        internal static AssetReferenceScanFixture CreateFixture(int referenceCount)
        {
            var rootIdentity = new ContentSchemaIdentityAssignment("test/asset-reference-scan-root");
            var assetReferenceSchema = FindAssetReferenceSchema();
            var rootSchema = new DataSchema(
                new ContentSchemaReference(rootIdentity.SchemaId, 1, 0, ContentSchemaScope.Content),
                rootIdentity,
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
                    .. CreateOptionalAssetReferenceFields(assetReferenceSchema, referenceCount),
                ]);
            var schemas = new CompiledContentSchemaRegistry([
                .. NativeAssetSchemaCatalogue.Schemas,
                rootSchema,
            ]);
            var firstSchemaId = new Guid("ffffffff-0000-4000-8000-000000000001");
            var secondSchemaId = new Guid("11111111-0000-4000-8000-000000000002");
            byte[][] allEncodedReferences = [
                CreateAssetReferenceRecord(3, secondSchemaId, 2),
                CreateAssetReferenceRecord(2, firstSchemaId, 1),
                CreateAssetReferenceRecord(3, secondSchemaId, 2),
                CreateAssetReferenceRecord(2, firstSchemaId, 2),
                CreateAssetReferenceRecord(2, secondSchemaId, 1),
                CreateAssetReferenceRecord(2, firstSchemaId, 1),
            ];
            var encodedReferences = allEncodedReferences[..referenceCount];
            return new AssetReferenceScanFixture(
                rootSchema,
                schemas,
                CreateReferenceRootRecord(rootSchema.Schema, encodedReferences),
                firstSchemaId,
                secondSchemaId);
        }

        internal static void Execute(
            AssetReferenceScanFixture fixture,
            AssetReference[] referenceWorkspace,
            int[] indexWorkspace)
        {
            var action = new AssetReferenceScanAction(
                fixture.RootSchema,
                fixture.Schemas,
                referenceWorkspace,
                indexWorkspace);
            _ = EbinStructuralReader.Read(
                fixture.EncodedRecord,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref action,
                out _,
                out _);
        }

        internal static void ExecuteWithRecordOverlap(AssetReferenceScanFixture fixture)
        {
            var action = new AssetReferenceScanOverlapAction(
                fixture.RootSchema,
                fixture.Schemas,
                fixture.EncodedRecord,
                3,
                true,
                new int[3],
                null);
            _ = EbinStructuralReader.Read(
                fixture.EncodedRecord,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref action,
                out _,
                out _);
        }

        internal static void ExecuteWithWorkspaceOverlap(
            AssetReferenceScanFixture fixture,
            byte[] workspaceBytes)
        {
            var action = new AssetReferenceScanOverlapAction(
                fixture.RootSchema,
                fixture.Schemas,
                fixture.EncodedRecord,
                3,
                false,
                null,
                workspaceBytes);
            _ = EbinStructuralReader.Read(
                fixture.EncodedRecord,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref action,
                out _,
                out _);
        }

        internal struct AssetReferenceScanAction : IAction<EbinRecordReadView>
        {
            private readonly int[] _indexWorkspace;
            private readonly AssetReference[] _referenceWorkspace;
            private readonly DataSchema _rootSchema;
            private readonly CompiledContentSchemaRegistry _schemas;

            internal AssetReferenceScanAction(
                DataSchema rootSchema,
                CompiledContentSchemaRegistry schemas,
                AssetReference[] referenceWorkspace,
                int[] indexWorkspace)
            {
                _rootSchema = rootSchema;
                _schemas = schemas;
                _referenceWorkspace = referenceWorkspace;
                _indexWorkspace = indexWorkspace;
            }

            internal int UniqueCount { get; private set; }

            public void Invoke(scoped EbinRecordReadView argument)
            {
                UniqueCount = AssetReferenceScanner.ScanInto(
                    argument,
                    _rootSchema,
                    _schemas,
                    _referenceWorkspace,
                    _indexWorkspace);
            }
        }

        internal readonly record struct AssetReferenceScanFixture(
            DataSchema RootSchema,
            CompiledContentSchemaRegistry Schemas,
            byte[] EncodedRecord,
            Guid FirstSchemaId,
            Guid SecondSchemaId);

        private readonly struct AssetReferenceScanOverlapAction : IAction<EbinRecordReadView>
        {
            private readonly byte[] _encodedRecord;
            private readonly int[]? _indexWorkspace;
            private readonly int _occurrenceCount;
            private readonly bool _overlapRecord;
            private readonly DataSchema _rootSchema;
            private readonly CompiledContentSchemaRegistry _schemas;
            private readonly byte[]? _workspaceBytes;

            internal AssetReferenceScanOverlapAction(
                DataSchema rootSchema,
                CompiledContentSchemaRegistry schemas,
                byte[] encodedRecord,
                int occurrenceCount,
                bool overlapRecord,
                int[]? indexWorkspace,
                byte[]? workspaceBytes)
            {
                _rootSchema = rootSchema;
                _schemas = schemas;
                _encodedRecord = encodedRecord;
                _occurrenceCount = occurrenceCount;
                _overlapRecord = overlapRecord;
                _indexWorkspace = indexWorkspace;
                _workspaceBytes = workspaceBytes;
            }

            public readonly void Invoke(scoped EbinRecordReadView argument)
            {
                var referenceByteLength = checked(_occurrenceCount * Unsafe.SizeOf<AssetReference>());

                if (_overlapRecord)
                {
                    var referenceWorkspace = MemoryMarshal.Cast<byte, AssetReference>(
                        _encodedRecord.AsSpan(0, referenceByteLength));
                    _ = AssetReferenceScanner.ScanInto(
                        argument,
                        _rootSchema,
                        _schemas,
                        referenceWorkspace,
                        _indexWorkspace
                            ?? throw new InvalidOperationException("The index workspace is missing."));
                    return;
                }

                var workspaceBytes = _workspaceBytes
                    ?? throw new InvalidOperationException("The overlapping workspace is missing.");
                var referenceWorkspaceFromSharedBytes = MemoryMarshal.Cast<byte, AssetReference>(
                    workspaceBytes.AsSpan(0, referenceByteLength));
                var indexWorkspaceFromSharedBytes = MemoryMarshal.Cast<byte, int>(
                    workspaceBytes.AsSpan(0, checked(_occurrenceCount * sizeof(int))));
                _ = AssetReferenceScanner.ScanInto(
                    argument,
                    _rootSchema,
                    _schemas,
                    referenceWorkspaceFromSharedBytes,
                    indexWorkspaceFromSharedBytes);
            }
        }
    }

    private sealed class ContextualTestAssetCodec : IContextualBinarySchemaCodec<TestAssetValue>
    {
        private readonly TestAssetCodec _codec = new TestAssetCodec();

        public Guid SchemaId => _codec.SchemaId;

        public int SchemaMajor => _codec.SchemaMajor;

        public int SchemaMinor => _codec.SchemaMinor;

        public BinaryRecordStatus Read(
            scoped ReadOnlySpan<byte> source,
            AssetCodecContext context,
            [MaybeNull] out TestAssetValue value,
            out int bytesConsumed,
            out BinarySchemaDiagnostic? diagnostic)
        {
            ArgumentNullException.ThrowIfNull(context);
            diagnostic = null;
            return _codec.Read(source, out value, out bytesConsumed);
        }

        public BinaryRecordStatus Write(
            in TestAssetValue value,
            AssetCodecContext context,
            scoped Span<byte> destination,
            out int bytesWritten,
            out BinarySchemaDiagnostic? diagnostic)
        {
            ArgumentNullException.ThrowIfNull(context);
            diagnostic = null;
            return _codec.Write(value, destination, out bytesWritten);
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

    private sealed class ProfiledTestAssetOrigin
    {
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

    private sealed class ThrowingFileStream : FileStream
    {
        private int _throwOnDispose = 1;

        internal ThrowingFileStream(string path)
            : base(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete)
        {
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && (Interlocked.Exchange(ref _throwOnDispose, 0) != 0))
            {
                throw new IOException("The test stream failed during disposal.");
            }

            base.Dispose(disposing);
        }
    }
}
