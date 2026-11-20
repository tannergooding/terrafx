// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;

namespace TerraFX.Assets.Tests;

[TestFixture]
internal sealed class AssetPackageReaderTests
{
    private static readonly byte[] s_profileRecord = Convert.FromHexString("4542494E0100000040000000090000003B9760CE468A8AABBB89F38DB9E3BC0020020000000000002001000000000000020000000000000000000000000000000100000003000100600100000000000004000000000000000100000000000000020000000300010070010000000000000400000000000000010000000000000003000000030001008001000000000000040000000000000001000000000000000500000001000100900100000000000001000000000000000100000000000000060000000D000100A00100000000000020000000000000002000000000000000070000000D000100C00100000000000020000000000000002000000000000000080000000D000100E00100000000000020000000000000002000000000000000090000000B0001000002000000000000100000000000000001000000000000000A000000070001001002000000000000040000000000000001000000000000000100000000000000000000000000000002000000000000000000000000000000020000000000000000000000000000000C0000000000000000000000000000009EF6534033621925D39355058D26782EB3B1A41A20690EFD829EF6D3EFE0EB1CF21833596ED8BEDEAA7E366505A2ADE8AD349AB35F7BF0554AD2D8CE5AEDA43F494F69B9559DE1C609B2C51A11454FCDC3A0BE8B108FDC91F242D89B38D18105C2DDC5D89EE68676BDEFDFFD36A56D8900000000000000000000000000000000");

    internal static AssetStoreOptions CreateOptions(
        string path,
        byte[] package,
        AssetStoreRegistration registration,
        byte[]? expectedDigest = null)
        => new AssetStoreOptions(path, expectedDigest ?? SHA256.HashData(package), registration);

    internal static AssetStoreOptions CreateOptions(string path, byte[] package, byte[]? expectedDigest = null)
        => new AssetStoreOptions(path, expectedDigest ?? SHA256.HashData(package), CreateRegistration());

    internal static byte[] CreatePackage(params TestEntry[] entries)
    {
        var directoryOffset = checked((ulong)(64 + s_profileRecord.Length));
        var directoryLength = checked(64UL + (checked((ulong)entries.Length) * 32));
        var directoryEnd = directoryOffset + directoryLength;
        var payloadEnd = Align16(directoryEnd);
        var payloadOffsets = new ulong[entries.Length];

        for (var index = 0; index < entries.Length; index++)
        {
            payloadOffsets[index] = Align16(payloadEnd);
            payloadEnd = payloadOffsets[index] + checked((ulong)entries[index].Payload.Length);
        }

        var packageLength = checked((int)Align16(payloadEnd));
        var package = new byte[packageLength];
        "EPKG"u8.CopyTo(package);
        BinaryPrimitives.WriteUInt16LittleEndian(package.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt64LittleEndian(package.AsSpan(8), checked((ulong)package.Length));
        BinaryPrimitives.WriteUInt64LittleEndian(package.AsSpan(16), 64);
        BinaryPrimitives.WriteUInt64LittleEndian(package.AsSpan(24), checked((ulong)s_profileRecord.Length));
        BinaryPrimitives.WriteUInt64LittleEndian(package.AsSpan(32), directoryOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(package.AsSpan(40), directoryLength);
        BinaryPrimitives.WriteUInt32LittleEndian(package.AsSpan(48), checked((uint)entries.Length));
        s_profileRecord.CopyTo(package.AsSpan(64));

        var directoryStart = checked((int)directoryOffset);
        var directory = package.AsSpan(directoryStart, checked((int)directoryLength));
        "EIDX"u8.CopyTo(directory);
        BinaryPrimitives.WriteUInt16LittleEndian(directory[4..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(directory[6..], 16);
        BinaryPrimitives.WriteUInt32LittleEndian(directory[8..], checked((uint)entries.Length));
        BinaryPrimitives.WriteUInt64LittleEndian(directory[16..], 64);
        BinaryPrimitives.WriteUInt64LittleEndian(directory[24..], checked((ulong)entries.Length * 32));
        BinaryPrimitives.WriteUInt64LittleEndian(directory[32..], directoryLength);

        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var encodedEntry = directory.Slice(64 + (index * 32), 32);
            BinaryPrimitives.WriteUInt32LittleEndian(encodedEntry, entry.AssetId);
            encodedEntry[4] = entry.Codec;
            BinaryPrimitives.WriteUInt64LittleEndian(encodedEntry[8..], payloadOffsets[index]);
            BinaryPrimitives.WriteUInt64LittleEndian(encodedEntry[16..], checked((ulong)entry.Payload.Length));
            BinaryPrimitives.WriteUInt64LittleEndian(
                encodedEntry[24..],
                checked((ulong)(entry.DecodedLength ?? entry.Payload.Length)));
            entry.Payload.CopyTo(package.AsSpan(checked((int)payloadOffsets[index])));
        }

        return package;
    }

    internal static AssetStoreRegistration CreateRegistration()
    {
        var assignment = new ContentSchemaIdentityAssignment("test/package-root");
        var schema = new DataSchema(
            new ContentSchemaReference(assignment.SchemaId, 1, 0, ContentSchemaScope.Content),
            assignment,
            ContentSchemaRole.NativeAsset,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            []);
        var schemas = new CompiledContentSchemaRegistry([.. NativeAssetSchemaCatalogue.Schemas, schema]);
        return new AssetStoreRegistrationBuilder(schemas).Build();
    }

    internal static string WritePackage(byte[] package)
    {
        var path = Path.Combine(Path.GetTempPath(), $"terrafx-{Guid.NewGuid():N}.epkg");
        File.WriteAllBytes(path, package);
        return path;
    }

    private static ulong Align16(ulong value) => (value + 15) & ~15UL;

    private static AssetBoundedFailureFact ParseBoundedMetadata(
        byte[] package,
        out AssetPackageEntry firstEntry,
        out AssetPackageEntry secondEntry,
        out SpatialProfileId profileId,
        out SpatialSettings spatialSettings)
    {
        var profileLength = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(package.AsSpan(24)));
        var directoryOffset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(package.AsSpan(32)));
        var directoryLength = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(package.AsSpan(40)));
        var directory = package.AsSpan(directoryOffset, directoryLength).ToArray();

        return AssetPackageReader.ParseBoundedMetadata(
            package.Length,
            1_048_576,
            package.AsSpan(0, 64),
            package.AsSpan(64, profileLength),
            directory,
            AssetBoundedFileDisposition.NotFinal,
            out firstEntry,
            out secondEntry,
            out profileId,
            out spatialSettings);
    }

    [Test]
    public void BoundedMetadataParserAdmitsTwoNoneEntriesWithoutAllocatingAnEntryTable()
    {
        var package = CreatePackage(
            new TestEntry(3, [0xA5, 0xC3]),
            new TestEntry(11, [0x5A]));
        var failure = ParseBoundedMetadata(
            package,
            out var firstEntry,
            out var secondEntry,
            out var profileId,
            out var spatialSettings);

        Assert.Multiple(() => {
            Assert.That(failure.Kind, Is.EqualTo(AssetBoundedFailureKind.None));
            Assert.That(firstEntry.AssetId.Value, Is.EqualTo(3));
            Assert.That(firstEntry.Codec, Is.Zero);
            Assert.That(firstEntry.StoredLength, Is.EqualTo(2));
            Assert.That(firstEntry.DecodedLength, Is.EqualTo(2));
            Assert.That(secondEntry.AssetId.Value, Is.EqualTo(11));
            Assert.That(secondEntry.Codec, Is.Zero);
            Assert.That(secondEntry.StoredLength, Is.EqualTo(1));
            Assert.That(secondEntry.DecodedLength, Is.EqualTo(1));
            Assert.That(profileId, Is.EqualTo(SpatialProfileId.Default));
            Assert.That(spatialSettings, Is.EqualTo(SpatialSettings.Default));
        });
    }

    [Test]
    public void BoundedMetadataParserRejectsBrotliEntries()
    {
        var package = CreatePackage(
            new TestEntry(3, [0xA5], Codec: 1),
            new TestEntry(11, [0x5A]));
        var failure = ParseBoundedMetadata(
            package,
            out _,
            out _,
            out _,
            out _);

        Assert.That(failure.Stage, Is.EqualTo(AssetBoundedFailureStage.Directory));
        Assert.That(failure.Kind, Is.EqualTo(AssetBoundedFailureKind.UnsupportedContent));
    }

    [Test]
    public void BoundedMetadataParserRejectsEntryCountsOutsideItsTwoEntryFamily()
    {
        var package = CreatePackage(new TestEntry(3, [0xA5]));
        var failure = ParseBoundedMetadata(
            package,
            out _,
            out _,
            out _,
            out _);

        Assert.That(failure.Stage, Is.EqualTo(AssetBoundedFailureStage.Directory));
        Assert.That(failure.Kind, Is.EqualTo(AssetBoundedFailureKind.UnsupportedContent));
    }

    [Test]
    public void BoundedMetadataParserRejectsMalformedDirectoryHeaders()
    {
        var package = CreatePackage(
            new TestEntry(3, [0xA5]),
            new TestEntry(11, [0x5A]));
        var directoryOffset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(package.AsSpan(32)));
        BinaryPrimitives.WriteUInt16LittleEndian(package.AsSpan(directoryOffset + 4), 2);

        var failure = ParseBoundedMetadata(
            package,
            out _,
            out _,
            out _,
            out _);

        Assert.That(failure.Stage, Is.EqualTo(AssetBoundedFailureStage.Directory));
        Assert.That(failure.Kind, Is.EqualTo(AssetBoundedFailureKind.Layout));
    }

    [Test]
    public void BoundedMetadataParserRejectsUnsortedAssetIds()
    {
        var package = CreatePackage(
            new TestEntry(11, [0xA5]),
            new TestEntry(3, [0x5A]));
        var failure = ParseBoundedMetadata(
            package,
            out _,
            out _,
            out _,
            out _);

        Assert.That(failure.Stage, Is.EqualTo(AssetBoundedFailureStage.Directory));
        Assert.That(failure.Kind, Is.EqualTo(AssetBoundedFailureKind.Layout));
    }

    [Test]
    public void BoundedPayloadRangePreflightAcceptsLargerDestination()
    {
        var entry = new AssetPackageEntry(new AssetId(3), 0, 128, 2, 2);
        var failure = AssetPackageReader.ValidateBoundedPayloadRange(
            entry,
            packageByteLength: 144,
            maximumDecodedAssetBytes: 1024,
            destinationLength: 4,
            AssetBoundedFileDisposition.NotFinal);

        Assert.That(failure.Kind, Is.EqualTo(AssetBoundedFailureKind.None));
    }

    [Test]
    public void BoundedPayloadRangePreflightRejectsInsufficientDestination()
    {
        var entry = new AssetPackageEntry(new AssetId(3), 0, 128, 2, 2);
        var failure = AssetPackageReader.ValidateBoundedPayloadRange(
            entry,
            packageByteLength: 144,
            maximumDecodedAssetBytes: 1024,
            destinationLength: 1,
            AssetBoundedFileDisposition.NotFinal);

        Assert.That(failure.Stage, Is.EqualTo(AssetBoundedFailureStage.PayloadRead));
        Assert.That(failure.Kind, Is.EqualTo(AssetBoundedFailureKind.Invariant));
    }

    [Test]
    public void BoundedPayloadRangePreflightRejectsOutOfPackageExtent()
    {
        var entry = new AssetPackageEntry(new AssetId(3), 0, 128, 2, 2);
        var failure = AssetPackageReader.ValidateBoundedPayloadRange(
            entry,
            packageByteLength: 129,
            maximumDecodedAssetBytes: 1024,
            destinationLength: 2,
            AssetBoundedFileDisposition.NotFinal);

        Assert.That(failure.Stage, Is.EqualTo(AssetBoundedFailureStage.PayloadRead));
        Assert.That(failure.Kind, Is.EqualTo(AssetBoundedFailureKind.Layout));
    }

    [Test]
    public void BoundedPayloadRangePreflightRejectsUnsupportedCodec()
    {
        var entry = new AssetPackageEntry(new AssetId(3), 1, 128, 2, 2);
        var failure = AssetPackageReader.ValidateBoundedPayloadRange(
            entry,
            packageByteLength: 144,
            maximumDecodedAssetBytes: 1024,
            destinationLength: 2,
            AssetBoundedFileDisposition.NotFinal);

        Assert.That(failure.Stage, Is.EqualTo(AssetBoundedFailureStage.PayloadRead));
        Assert.That(failure.Kind, Is.EqualTo(AssetBoundedFailureKind.UnsupportedContent));
    }

    [Test]
    public async Task PackageAdmissionRejectsChecksumMismatchBeforeParsingHeader()
    {
        var expectedPackage = CreatePackage();
        var changedPackage = (byte[])expectedPackage.Clone();
        changedPackage[0] ^= 1;
        var path = WritePackage(changedPackage);

        try
        {
            var options = CreateOptions(path, changedPackage, SHA256.HashData(expectedPackage));
            var exception = Assert.ThrowsAsync<InvalidDataException>(
                async () => await AssetPackageReader.OpenAsync(options, CancellationToken.None).ConfigureAwait(false));

            Assert.That(exception!.Message, Does.Contain("SHA-256"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task PackageAdmissionRejectsMalformedDirectoryWithValidChecksum()
    {
        var package = CreatePackage();
        var directoryOffset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(package.AsSpan(32)));
        BinaryPrimitives.WriteUInt16LittleEndian(package.AsSpan(directoryOffset + 4), 2);
        var path = WritePackage(package);

        try
        {
            var options = CreateOptions(path, package);
            var exception = Assert.ThrowsAsync<InvalidDataException>(
                async () => await AssetPackageReader.OpenAsync(options, CancellationToken.None).ConfigureAwait(false));

            Assert.That(exception!.Message, Does.Contain("EIDX1"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task PackageAdmissionRejectsNonzeroTailPadding()
    {
        var package = CreatePackage(new TestEntry(1, [0xA5]));
        package[^1] = 0x01;
        var path = WritePackage(package);

        try
        {
            var options = CreateOptions(path, package);
            var exception = Assert.ThrowsAsync<InvalidDataException>(
                async () => await AssetPackageReader.OpenAsync(options, CancellationToken.None).ConfigureAwait(false));

            Assert.That(exception!.Message, Does.Contain("nonzero alignment padding"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task PackageAdmissionRejectsUnsortedAssetIds()
    {
        var package = CreatePackage(new TestEntry(2, [0xA5]), new TestEntry(1, [0x5A]));
        var path = WritePackage(package);

        try
        {
            var options = CreateOptions(path, package);
            _ = Assert.ThrowsAsync<InvalidDataException>(
                async () => await AssetPackageReader.OpenAsync(options, CancellationToken.None).ConfigureAwait(false));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task PackageAdmissionRejectsZeroCpuBudgetBeforeOpeningThePackage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"terrafx-missing-{Guid.NewGuid():N}.epkg");
        var options = new AssetStoreOptions(path, new byte[32], CreateRegistration()) {
            CpuAssetBudgetBytes = 0,
        };
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync().ConfigureAwait(false);

        _ = Assert.ThrowsAsync<OperationCanceledException>(
            async () => await AssetPackageReader.OpenAsync(options, cancellationSource.Token).ConfigureAwait(false));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await AssetPackageReader.OpenAsync(options, CancellationToken.None).ConfigureAwait(false));

        Assert.That(exception!.Message, Is.EqualTo("The CPU asset budget cannot fund package opening."));
        Assert.That(File.Exists(path), Is.False);
    }

    [Test]
    public async Task PackageAdmissionRetainsValidatedEmptyBacking()
    {
        Assert.That(
            Convert.ToHexString(SHA256.HashData(s_profileRecord)),
            Is.EqualTo("5FE3AE0FBAE08E4A21B064B1449934A25911176E50DEF5C6F3BAC8239595652F"));

        var package = CreatePackage();
        var path = WritePackage(package);

        try
        {
            var options = CreateOptions(path, package);
            var backing = await AssetPackageReader.OpenAsync(options, CancellationToken.None).ConfigureAwait(false);

            try
            {
                Assert.That(backing.Entries, Is.Empty);
                Assert.That(backing.PackageByteLength, Is.EqualTo(package.Length));
                Assert.That(backing.ProfileId, Is.EqualTo(SpatialProfileId.Default));
                Assert.That(backing.SpatialSettings, Is.EqualTo(SpatialSettings.Default));
                Assert.That(backing.Registration, Is.SameAs(options.Registration));
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

    internal readonly record struct TestEntry(uint AssetId, byte[] Payload, byte Codec = 0, int? DecodedLength = null);
}
