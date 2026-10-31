// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

[TestFixture]
internal sealed class SceneAssetStoreTests
{
    [Test]
    public async Task LoadsSceneAndPrefabThroughRegisteredCodecs()
    {
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var registration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
        var placementId = new Guid("20000000-0000-4000-8000-000000000003");
        var rootEntityId = SceneAssetReaderTests.Fixtures.RootEntityId;
        var prefabRecord = SceneAssetReaderTests.Fixtures.CreatePrefab(new AssetId(1), entityId: rootEntityId);
        var sceneRecord = SceneAssetReaderTests.Fixtures.CreateAssetReferenceScene(
            new AssetId(3),
            placementId,
            SceneAssetReaderTests.Fixtures.CreateRootPose(rootEntityId));
        var package = Package.CreatePackage(
            new Package.Entry(1, Package.CreateEnvelope(1, prefabRecord)),
            new Package.Entry(3, Package.CreateEnvelope(3, sceneRecord)));
        var packagePath = Package.WritePackage(package);

        try
        {
            var store = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), registration)).ConfigureAwait(false);

            try
            {
                using var prefabLease = await store.LoadAsync<ScenePrefabAsset>(new AssetId(1)).ConfigureAwait(false);
                using var sceneLease = await store.LoadAsync<SceneAsset>(new AssetId(3)).ConfigureAwait(false);

                Assert.That(prefabLease.Value.AssetId, Is.EqualTo(new AssetId(1)));
                Assert.That(sceneLease.Value.AssetId, Is.EqualTo(new AssetId(3)));
                Assert.That(sceneLease.Value.Placements, Has.Length.EqualTo(1));
                Assert.That(sceneLease.Value.Placements[0].PrefabAssetId, Is.EqualTo(prefabLease.AssetId));
                Assert.That(sceneLease.Value.Placements[0].RootPoses, Has.Length.EqualTo(1));
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    private static class Package
    {
        private static readonly byte[] s_profileRecord = Convert.FromHexString("4542494E0100000040000000090000003B9760CE468A8AABBB89F38DB9E3BC0020020000000000002001000000000000020000000000000000000000000000000100000003000100600100000000000004000000000000000100000000000000020000000300010070010000000000000400000000000000010000000000000003000000030001008001000000000000040000000000000001000000000000000500000001000100900100000000000001000000000000000100000000000000060000000D000100A00100000000000020000000000000002000000000000000070000000D000100C00100000000000020000000000000002000000000000000080000000D000100E00100000000000020000000000000002000000000000000090000000B0001000002000000000000100000000000000001000000000000000A000000070001001002000000000000040000000000000001000000000000000100000000000000000000000000000002000000000000000000000000000000020000000000000000000000000000000C0000000000000000000000000000009EF6534033621925D39355058D26782EB3B1A41A20690EFD829EF6D3EFE0EB1CF21833596ED8BEDEAA7E366505A2ADE8AD349AB35F7BF0554AD2D8CE5AEDA43F494F69B9559DE1C609B2C51A11454FCDC3A0BE8B108FDC91F242D89B38D18105C2DDC5D89EE68676BDEFDFFD36A56D8900000000000000000000000000000000");

        internal static byte[] CreateEnvelope(uint assetId, byte[] payload)
        {
            var envelope = new byte[checked((int)Align16(144UL + checked((ulong)payload.Length)))];
            WriteRecordHeader(
                envelope,
                new ContentSchemaIdentityAssignment("native/envelope").SchemaId,
                major: 2,
                minor: 0,
                fieldCount: 2);
            WriteFieldHeader(envelope.AsSpan(64, 32), tag: 1, kind: 3, payloadOffset: 128, payloadLength: sizeof(uint), count: 1);
            WriteFieldHeader(
                envelope.AsSpan(96, 32),
                tag: 2,
                kind: 22,
                payloadOffset: 144,
                payloadLength: checked((ulong)payload.Length),
                count: 1);
            BinaryPrimitives.WriteUInt32LittleEndian(envelope.AsSpan(128), assetId);
            payload.CopyTo(envelope.AsSpan(144));
            return envelope;
        }

        internal static byte[] CreatePackage(params Entry[] entries)
        {
            var directoryOffset = Align16(checked((ulong)(64 + s_profileRecord.Length)));
            var directoryLength = checked(64UL + (checked((ulong)entries.Length) * 32));
            var payloadEnd = Align16(directoryOffset + directoryLength);
            var payloadOffsets = new ulong[entries.Length];

            for (var index = 0; index < entries.Length; index++)
            {
                payloadOffsets[index] = Align16(payloadEnd);
                payloadEnd = checked(payloadOffsets[index] + checked((ulong)entries[index].Payload.Length));
            }

            var package = new byte[checked((int)Align16(payloadEnd))];
            "EPKG"u8.CopyTo(package);
            BinaryPrimitives.WriteUInt16LittleEndian(package.AsSpan(4), 2);
            BinaryPrimitives.WriteUInt64LittleEndian(package.AsSpan(8), checked((ulong)package.Length));
            BinaryPrimitives.WriteUInt64LittleEndian(package.AsSpan(16), 64);
            BinaryPrimitives.WriteUInt64LittleEndian(package.AsSpan(24), checked((ulong)s_profileRecord.Length));
            BinaryPrimitives.WriteUInt64LittleEndian(package.AsSpan(32), directoryOffset);
            BinaryPrimitives.WriteUInt64LittleEndian(package.AsSpan(40), directoryLength);
            BinaryPrimitives.WriteUInt32LittleEndian(package.AsSpan(48), checked((uint)entries.Length));
            s_profileRecord.CopyTo(package.AsSpan(64));

            var directory = package.AsSpan(checked((int)directoryOffset), checked((int)directoryLength));
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
                BinaryPrimitives.WriteUInt64LittleEndian(encodedEntry[8..], payloadOffsets[index]);
                BinaryPrimitives.WriteUInt64LittleEndian(encodedEntry[16..], checked((ulong)entry.Payload.Length));
                BinaryPrimitives.WriteUInt64LittleEndian(encodedEntry[24..], checked((ulong)entry.Payload.Length));
                entry.Payload.CopyTo(package.AsSpan(checked((int)payloadOffsets[index])));
            }

            return package;
        }

        internal static string WritePackage(byte[] package)
        {
            var packagePath = Path.Combine(Path.GetTempPath(), $"terrafx-{Guid.NewGuid():N}.epkg");
            File.WriteAllBytes(packagePath, package);
            return packagePath;
        }

        private static ulong Align16(ulong value) => (value + 15) & ~15UL;

        private static void WriteFieldHeader(
            Span<byte> destination,
            uint tag,
            ushort kind,
            ulong payloadOffset,
            ulong payloadLength,
            ulong count)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination, tag);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], kind);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], 1);
            BinaryPrimitives.WriteUInt64LittleEndian(destination[8..], payloadOffset);
            BinaryPrimitives.WriteUInt64LittleEndian(destination[16..], payloadLength);
            BinaryPrimitives.WriteUInt64LittleEndian(destination[24..], count);
        }

        private static void WriteRecordHeader(
            byte[] destination,
            Guid schemaId,
            int major,
            int minor,
            uint fieldCount)
        {
            "EBIN"u8.CopyTo(destination);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.AsSpan(4), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(8), 64);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(12), fieldCount);
            _ = schemaId.TryWriteBytes(destination.AsSpan(16, 16), bigEndian: true, out _);
            BinaryPrimitives.WriteUInt64LittleEndian(destination.AsSpan(32), checked((ulong)destination.Length));
            BinaryPrimitives.WriteUInt64LittleEndian(destination.AsSpan(40), checked((ulong)fieldCount * 32));
            BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(48), checked((uint)major));
            BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(52), checked((uint)minor));
        }

        internal readonly record struct Entry(uint AssetId, byte[] Payload);
    }
}
