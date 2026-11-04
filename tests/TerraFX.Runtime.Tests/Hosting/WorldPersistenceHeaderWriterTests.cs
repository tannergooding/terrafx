// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;
using TerraFX.Hosting;
using TerraFX.Numerics;

namespace TerraFX.Runtime.Tests.Hosting;

[TestFixture]
internal sealed class WorldPersistenceHeaderWriterTests
{
    internal static byte[] CreateFoundationProfileRecord()
    {
        var worldGuid = new Guid("20000000-0000-0000-0000-000000000000");
        var frame = new SpatialFrame3(
            ownerId: 1,
            SpatialProfileId.Default,
            SpatialSettings.Default,
            new SpatialPosition3(default, new Vector3(0, 0, 0)),
            revision: 1,
            new Vector3(-1, -1, -1),
            new Vector3(1, 1, 1));
        var measureStatus = WorldPersistenceSpatialFrameWriter.MeasureFrame3(
            worldGuid,
            ownerId: 1,
            frame,
            EbinReadLimits.Foundation,
            out var workspace,
            out _);
        Assert.That(measureStatus, Is.EqualTo(EbinWriteStatus.Complete));

        var buffers = new WorldPersistenceSpatialFrameBuffers {
            Destination = new byte[workspace.RecordBytes],
            FrameCandidateStorage = new byte[workspace.RecordBytes],
            FramePayloadStorage = new byte[workspace.FramePayloadStorageBytes],
            ProfileCandidateStorage = new byte[workspace.ProfileRecordBytes],
            ProfileStorage = new byte[workspace.ProfileRecordBytes],
        };
        var writeStatus = WorldPersistenceSpatialFrameWriter.WriteFrame3(
            worldGuid,
            ownerId: 1,
            frame,
            workspace,
            EbinReadLimits.Foundation,
            ref buffers,
            out var bytesWritten,
            out _);
        Assert.That(writeStatus, Is.EqualTo(EbinWriteStatus.Complete));
        Assert.That(bytesWritten, Is.EqualTo(workspace.RecordBytes));
        return buffers.ProfileStorage[..workspace.ProfileRecordBytes].ToArray();
    }

    internal static WorldPersistenceMetadata CreateMetadata()
    {
        var digest = CreateDigest(1);
        var application = new WorldPersistenceApplication(
            new Guid("10000000-0000-0000-0000-000000000000"),
            1,
            0,
            []);
        var runtimeProfile = new WorldPersistenceRuntimeProfile(
            digest,
            digest,
            digest,
            WorldPersistenceOperatingSystem.Windows,
            WorldPersistenceCodeGeneration.Jit,
            WorldPersistenceInstructionSet.None,
            digest,
            WorldPersistenceFmaPolicy.Disallowed,
            0,
            []);
        return new WorldPersistenceMetadata(application, runtimeProfile);
    }

    private static byte[] CreateDigest(byte firstByte)
    {
        var digest = new byte[32];
        digest[0] = firstByte;
        return digest;
    }

    private static HeaderProbe ReadHeader(ReadOnlySpan<byte> record)
    {
        var probe = default(HeaderProbe);
        var status = EbinStructuralReader.Read(
            record,
            EbinReadLimits.Foundation,
            EbinReadPolicy.Canonical,
            ref probe,
            out var bytesConsumed,
            out _);

        Assert.That(status, Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(bytesConsumed, Is.EqualTo(record.Length));
        return probe;
    }

    [Test]
    public void HeaderWriterIncludesPackageDigestOnlyForStrictLocal()
    {
        var metadata = CreateMetadata();
        var profileRecord = CreateFoundationProfileRecord();
        var worldGuid = new Guid("30000000-0000-0000-0000-000000000000");
        var lineageGuid = new Guid("40000000-0000-0000-0000-000000000000");
        var packageDigest = CreateDigest(0xAB);
        var standardRecord = WorldPersistenceHeaderWriter.Create(
            metadata,
            WorldPersistenceMode.Standard,
            [],
            worldGuid,
            lineageGuid,
            nextTick: 12,
            captureWorkerCount: 4,
            profileRecord);
        var strictRecord = WorldPersistenceHeaderWriter.Create(
            metadata,
            WorldPersistenceMode.StrictLocal,
            packageDigest,
            worldGuid,
            lineageGuid,
            nextTick: 12,
            captureWorkerCount: 4,
            profileRecord);
        var standard = ReadHeader(standardRecord);
        var strictLocal = ReadHeader(strictRecord);

        Assert.Multiple(() => {
            Assert.That(standard.Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/header").SchemaId));
            Assert.That(standard.Header.FieldCount, Is.EqualTo(10));
            Assert.That(standard.HasPackageDigest, Is.False);
            Assert.That(standard.Mode, Is.EqualTo((byte)WorldPersistenceMode.Standard));
            Assert.That(strictLocal.Header.FieldCount, Is.EqualTo(11));
            Assert.That(strictLocal.HasPackageDigest, Is.True);
            Assert.That(strictLocal.Mode, Is.EqualTo((byte)WorldPersistenceMode.StrictLocal));
            Assert.That(strictLocal.PackageDigest.AsSpan().ToArray(), Is.EqualTo(packageDigest));
        });
    }

    [Test]
    public void HeaderWriterRejectsModeAndDigestDisagreement()
    {
        var metadata = CreateMetadata();
        var profileRecord = CreateFoundationProfileRecord();
        var worldGuid = new Guid("30000000-0000-0000-0000-000000000000");
        var lineageGuid = new Guid("40000000-0000-0000-0000-000000000000");

        Assert.Multiple(() => {
            _ = Assert.Throws<ArgumentException>(() => WorldPersistenceHeaderWriter.Create(
                metadata,
                WorldPersistenceMode.Standard,
                CreateDigest(0xAB),
                worldGuid,
                lineageGuid,
                1,
                0,
                profileRecord));
            _ = Assert.Throws<ArgumentException>(() => WorldPersistenceHeaderWriter.Create(
                metadata,
                WorldPersistenceMode.StrictLocal,
                [],
                worldGuid,
                lineageGuid,
                1,
                0,
                profileRecord));
        });
    }

    private struct HeaderProbe : IAction<EbinRecordReadView>
    {
        internal bool HasPackageDigest { get; private set; }

        internal EbinRecordHeader Header { get; private set; }

        internal byte Mode { get; private set; }

        internal ImmutableArray<byte> PackageDigest { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
        {
            Header = record.Header;
            HasPackageDigest = record.TryGetField(1, out var digest);
            PackageDigest = HasPackageDigest ? [.. digest.Payload] : [];

            if (!record.TryGetField(6, out var mode))
            {
                throw new InvalidOperationException("The State2 header omitted its mode.");
            }

            Mode = mode.Payload[0];
        }
    }
}
