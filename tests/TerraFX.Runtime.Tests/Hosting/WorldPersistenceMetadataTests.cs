// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using NUnit.Framework;
using TerraFX;
using TerraFX.Hosting;

namespace TerraFX.Runtime.Tests.Hosting;

[TestFixture]
internal sealed class WorldPersistenceMetadataTests
{
    private static byte[] CreateDigest(byte firstByte)
    {
        var digest = new byte[32];
        digest[0] = firstByte;
        return digest;
    }

    private static MetadataRecordProbe ReadMetadataRecord(ImmutableArray<byte> record)
    {
        var probe = default(MetadataRecordProbe);
        var status = EbinStructuralReader.Read(
            record.AsSpan(),
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
    public void ApplicationCopiesCanonicalModuleVersions()
    {
        var firstId = new Guid("00000001-0000-0000-0000-000000000000");
        var secondId = new Guid("00000100-0000-0000-0000-000000000000");
        WorldPersistenceModuleVersion[] modules =
        [
            new WorldPersistenceModuleVersion(firstId, 3),
            new WorldPersistenceModuleVersion(secondId, 7),
        ];
        var application = new WorldPersistenceApplication(
            new Guid("10000000-0000-0000-0000-000000000000"),
            majorVersion: 2,
            minorVersion: 5,
            modules);

        modules[0] = new WorldPersistenceModuleVersion(
            new Guid("20000000-0000-0000-0000-000000000000"),
            1);

        Assert.Multiple(() => {
            Assert.That(application.ApplicationId, Is.EqualTo(new Guid("10000000-0000-0000-0000-000000000000")));
            Assert.That(application.MajorVersion, Is.EqualTo(2));
            Assert.That(application.MinorVersion, Is.EqualTo(5));
            Assert.That(application.Modules.Length, Is.EqualTo(2));
            Assert.That(application.Modules[0].ModuleId, Is.EqualTo(firstId));
            Assert.That(application.Modules[0].Version, Is.EqualTo(3));
            Assert.That(application.Modules[1].ModuleId, Is.EqualTo(secondId));
            Assert.That(application.Modules[1].Version, Is.EqualTo(7));
        });
    }

    [Test]
    public void ApplicationRejectsNoncanonicalModuleMembership()
    {
        var firstId = new Guid("00000001-0000-0000-0000-000000000000");
        var secondId = new Guid("00000100-0000-0000-0000-000000000000");

        Assert.Multiple(() => {
            _ = Assert.Throws<ArgumentException>(() => new WorldPersistenceApplication(
                new Guid("10000000-0000-0000-0000-000000000000"),
                1,
                0,
                [
                    new WorldPersistenceModuleVersion(secondId, 1),
                    new WorldPersistenceModuleVersion(firstId, 1),
                ]));

            _ = Assert.Throws<ArgumentException>(() => new WorldPersistenceApplication(
                new Guid("10000000-0000-0000-0000-000000000000"),
                1,
                0,
                [
                    new WorldPersistenceModuleVersion(firstId, 1),
                    new WorldPersistenceModuleVersion(firstId, 2),
                ]));

            _ = Assert.Throws<ArgumentException>(() => _ = new WorldPersistenceApplication(
                Guid.Empty,
                1,
                0,
                []));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new WorldPersistenceApplication(
                new Guid("10000000-0000-0000-0000-000000000000"),
                0,
                0,
                []));
        });
    }

    [Test]
    public void MetadataApplicationAndRuntimeSourcesAreRequired()
    {
        var application = new WorldPersistenceApplication(
            new Guid("10000000-0000-0000-0000-000000000000"),
            1,
            0,
            []);
        var digest = CreateDigest(1);
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

        Assert.Multiple(() => {
            _ = Assert.Throws<ArgumentNullException>(() => _ = new WorldPersistenceMetadata(null!, runtimeProfile));
            _ = Assert.Throws<ArgumentNullException>(() => _ = new WorldPersistenceMetadata(application, null!));
            Assert.That(
                new WorldPersistenceMetadata(application, runtimeProfile).RuntimeProfile,
                Is.SameAs(runtimeProfile));
        });
    }

    [Test]
    public void MetadataRecordsHaveCanonicalSchemasAndModuleArrays()
    {
        var digest = CreateDigest(7);
        var application = new WorldPersistenceApplication(
            new Guid("10000000-0000-0000-0000-000000000000"),
            1,
            2,
            [new WorldPersistenceModuleVersion(new Guid("00000001-0000-0000-0000-000000000000"), 3)]);
        var runtimeProfile = new WorldPersistenceRuntimeProfile(
            digest,
            digest,
            digest,
            WorldPersistenceOperatingSystem.Windows,
            WorldPersistenceCodeGeneration.NativeAot,
            WorldPersistenceInstructionSet.Sse2,
            digest,
            WorldPersistenceFmaPolicy.Permitted,
            128,
            [new WorldPersistenceModuleArtifact(
                new Guid("00000001-0000-0000-0000-000000000000"),
                4,
                digest)]);
        var metadata = new WorldPersistenceMetadata(application, runtimeProfile);
        var applicationRecord = ReadMetadataRecord(metadata.ApplicationRecord);
        var runtimeRecord = ReadMetadataRecord(metadata.RuntimeProfileRecord);

        Assert.Multiple(() => {
            Assert.That(applicationRecord.Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/application").SchemaId));
            Assert.That(applicationRecord.Header.FieldCount, Is.EqualTo(4));
            Assert.That(applicationRecord.ModuleCount, Is.EqualTo(1));
            Assert.That(applicationRecord.ModuleSchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/module-id").SchemaId));
            Assert.That(applicationRecord.ModuleFieldCount, Is.EqualTo(2));
            Assert.That(runtimeRecord.Header.SchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/runtime-profile").SchemaId));
            Assert.That(runtimeRecord.Header.FieldCount, Is.EqualTo(9));
            Assert.That(runtimeRecord.ModuleCount, Is.EqualTo(1));
            Assert.That(runtimeRecord.ModuleSchemaId, Is.EqualTo(new ContentSchemaIdentityAssignment("state/module").SchemaId));
            Assert.That(runtimeRecord.ModuleFieldCount, Is.EqualTo(3));
        });
    }

    [Test]
    public void RuntimeProfileCopiesDigestsAndValidatesClosedSelectors()
    {
        var engineDigest = CreateDigest(1);
        var runtimeDigest = CreateDigest(2);
        var compilerDigest = CreateDigest(3);
        var kernelDigest = CreateDigest(4);
        var profile = new WorldPersistenceRuntimeProfile(
            engineDigest,
            runtimeDigest,
            compilerDigest,
            WorldPersistenceOperatingSystem.Windows,
            WorldPersistenceCodeGeneration.Jit,
            WorldPersistenceInstructionSet.Sse2
                | WorldPersistenceInstructionSet.Avx,
            kernelDigest,
            WorldPersistenceFmaPolicy.Disallowed,
            maximumSimdBits: 128,
            modules: []);

        engineDigest[0] = 9;
        runtimeDigest[0] = 9;
        compilerDigest[0] = 9;
        kernelDigest[0] = 9;

        Assert.Multiple(() => {
            Assert.That(profile.EngineArtifactSha256[0], Is.EqualTo(1));
            Assert.That(profile.RuntimeArtifactSha256[0], Is.EqualTo(2));
            Assert.That(profile.CompilerArtifactSha256[0], Is.EqualTo(3));
            Assert.That(profile.NumericsKernelSha256[0], Is.EqualTo(4));
            Assert.That(profile.OperatingSystem, Is.EqualTo(WorldPersistenceOperatingSystem.Windows));
            Assert.That(profile.CodeGeneration, Is.EqualTo(WorldPersistenceCodeGeneration.Jit));
            Assert.That(profile.InstructionSets, Is.EqualTo(
                WorldPersistenceInstructionSet.Sse2
                | WorldPersistenceInstructionSet.Avx));
            Assert.That(profile.MaximumSimdBits, Is.EqualTo(128));
        });
    }

    [Test]
    public void RuntimeProfileRejectsInvalidDigestsFlagsAndWidths()
    {
        var digest = CreateDigest(1);

        Assert.Multiple(() => {
            _ = Assert.Throws<ArgumentException>(() => _ = new WorldPersistenceRuntimeProfile(
                digest.AsSpan(1),
                digest,
                digest,
                WorldPersistenceOperatingSystem.Windows,
                WorldPersistenceCodeGeneration.Jit,
                WorldPersistenceInstructionSet.None,
                digest,
                WorldPersistenceFmaPolicy.Disallowed,
                0,
                []));

            _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new WorldPersistenceRuntimeProfile(
                digest,
                digest,
                digest,
                (WorldPersistenceOperatingSystem)2,
                WorldPersistenceCodeGeneration.Jit,
                WorldPersistenceInstructionSet.None,
                digest,
                WorldPersistenceFmaPolicy.Disallowed,
                0,
                []));

            _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new WorldPersistenceRuntimeProfile(
                digest,
                digest,
                digest,
                WorldPersistenceOperatingSystem.Windows,
                WorldPersistenceCodeGeneration.Jit,
                (WorldPersistenceInstructionSet)16,
                digest,
                WorldPersistenceFmaPolicy.Disallowed,
                0,
                []));

            _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new WorldPersistenceRuntimeProfile(
                digest,
                digest,
                digest,
                WorldPersistenceOperatingSystem.Windows,
                WorldPersistenceCodeGeneration.Jit,
                WorldPersistenceInstructionSet.None,
                digest,
                WorldPersistenceFmaPolicy.Disallowed,
                64,
                []));
        });
    }

    private struct MetadataRecordProbe : IAction<EbinRecordReadView>
    {
        internal EbinRecordHeader Header { get; private set; }

        internal ulong ModuleCount { get; private set; }

        internal int ModuleFieldCount { get; private set; }

        internal Guid ModuleSchemaId { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
        {
            Header = record.Header;
            ModuleCount = record.GetField(record.FieldCount - 1).Count;

            if (ModuleCount > 0)
            {
                var moduleArrayTag = Header.SchemaId == new ContentSchemaIdentityAssignment("state/application").SchemaId
                    ? 4u
                    : 9u;
                var module = record.GetRecordArrayElement(moduleArrayTag, 0);
                ModuleFieldCount = module.Header.FieldCount;
                ModuleSchemaId = module.Header.SchemaId;
            }
        }
    }
}
