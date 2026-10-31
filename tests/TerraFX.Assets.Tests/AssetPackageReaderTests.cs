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

    private static ulong Align16(ulong value) => (value + 15) & ~15UL;

    private static AssetStoreOptions CreateOptions(string path, byte[] package, byte[]? expectedDigest = null)
        => new AssetStoreOptions(path, expectedDigest ?? SHA256.HashData(package), CreateRegistration());

    private static byte[] CreatePackage(params TestEntry[] entries)
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
            BinaryPrimitives.WriteUInt64LittleEndian(encodedEntry[24..], checked((ulong)entry.Payload.Length));
            entry.Payload.CopyTo(package.AsSpan(checked((int)payloadOffsets[index])));
        }

        return package;
    }

    private static AssetStoreRegistration CreateRegistration()
        => new AssetStoreRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();

    private static string WritePackage(byte[] package)
    {
        var path = Path.Combine(Path.GetTempPath(), $"terrafx-{Guid.NewGuid():N}.epkg");
        File.WriteAllBytes(path, package);
        return path;
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

            await using (backing.ConfigureAwait(false))
            {
                Assert.That(backing.Entries, Is.Empty);
                Assert.That(backing.PackageByteLength, Is.EqualTo(package.Length));
                Assert.That(backing.ProfileId, Is.EqualTo(SpatialProfileId.Default));
                Assert.That(backing.SpatialSettings, Is.EqualTo(SpatialSettings.Default));
                Assert.That(backing.Registration, Is.SameAs(options.Registration));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    private readonly record struct TestEntry(uint AssetId, byte[] Payload, byte Codec = 0);
}
