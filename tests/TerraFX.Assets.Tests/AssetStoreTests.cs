// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
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

            Assert.Multiple(() => {
                Assert.That(descriptor.AssetId, Is.EqualTo(new AssetId(1)));
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
            Assert.That(
                packageLease.TryDescribeOwnedRead<TestAssetValue>(new AssetId(1), new object(), out _),
                Is.False);
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
}
