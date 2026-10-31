// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;

namespace TerraFX.Assets.Tests;

[TestFixture]
internal sealed class AssetStoreRegistrationTests
{
    private static readonly ContentSchemaIdentityAssignment s_identityAssignment = new ContentSchemaIdentityAssignment("test/asset");
    private static readonly ContentSchemaReference s_schema = new ContentSchemaReference(
        s_identityAssignment.SchemaId,
        1,
        0,
        ContentSchemaScope.Content);

    private static CompiledContentSchemaRegistry CreateSchemaRegistry(ContentSchemaRole role = ContentSchemaRole.NativeAsset)
        => new CompiledContentSchemaRegistry([
            new DataSchema(
                s_schema,
                s_identityAssignment,
                role,
                ContentSchemaOwner.Application,
                ContentSchemaStorage.NotComponent,
                ContentTransientRestore.NotApplicable,
                []),
        ]);

    [Test]
    public void AssetIdRequiresPositiveValues()
    {
        var maximum = new AssetId(int.MaxValue);
        Assert.That(maximum.Value, Is.EqualTo(int.MaxValue));
        Assert.That(maximum.IsValid, Is.True);
        Assert.That(default(AssetId).IsValid, Is.False);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AssetId(0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AssetId(-1));
    }

    [Test]
    public void AssetStoreOptionsCopyDigestAndRequireAbsolutePackagePath()
    {
        var registration = new AssetStoreRegistrationBuilder(CreateSchemaRegistry())
            .RegisterCodec(new IntegerCodec())
            .Build();
        var digest = new byte[32];
        digest[0] = 0xA5;
        var options = new AssetStoreOptions(Path.GetFullPath("content.epkg"), digest, registration);

        digest[0] = 0;
        Assert.That(options.AbsolutePackagePath, Is.EqualTo(Path.GetFullPath("content.epkg")));
        Assert.That(options.ExpectedPackageSha256.Length, Is.EqualTo(32));
        Assert.That(options.ExpectedPackageSha256[0], Is.EqualTo(0xA5));
        Assert.That(options.Registration, Is.SameAs(registration));
        Assert.That(options.CpuAssetBudgetBytes, Is.EqualTo(536_870_912));
        Assert.That(options.MaximumDecodedAssetBytes, Is.EqualTo(268_435_456));
        Assert.That(options.MaximumConcurrentDecodeBytes, Is.EqualTo(268_435_456));
        Assert.That(options.MaximumLiveAssetLeases, Is.EqualTo(4096));

        _ = Assert.Throws<ArgumentException>(() => new AssetStoreOptions("content.epkg", new byte[32], registration));
        _ = Assert.Throws<ArgumentException>(() => new AssetStoreOptions(Path.GetFullPath("content.epkg"), new byte[31], registration));
        _ = Assert.Throws<ArgumentNullException>(() => new AssetStoreOptions(Path.GetFullPath("content.epkg"), new byte[32], null!));

        var invalidBudget = new AssetStoreOptions(Path.GetFullPath("content.epkg"), new byte[32], registration) {
            CpuAssetBudgetBytes = -1,
        };
        _ = Assert.Throws<ArgumentOutOfRangeException>(invalidBudget.Validate);
    }

    [Test]
    public void RegistrationRejectsDuplicateSchemaAndValueType()
    {
        var builder = new AssetStoreRegistrationBuilder(CreateSchemaRegistry());
        _ = builder.RegisterCodec(new IntegerCodec());

        _ = Assert.Throws<ArgumentException>(() => builder.RegisterCodec(new IntegerCodec()));
    }

    [Test]
    public void RegistrationRejectsNonAssetRootSchemas()
    {
        var builder = new AssetStoreRegistrationBuilder(CreateSchemaRegistry(ContentSchemaRole.Descriptor));

        _ = Assert.Throws<ArgumentException>(() => builder.RegisterCodec(new IntegerCodec()));
    }

    [Test]
    public void RegistrationRequiresExactCompiledSchemaTuple()
    {
        var builder = new AssetStoreRegistrationBuilder(new CompiledContentSchemaRegistry([]));

        _ = Assert.Throws<ArgumentException>(() => builder.RegisterCodec(new IntegerCodec()));
    }

    [Test]
    public void RegistrationResolvesTypedCodecByValueTypeAndSchema()
    {
        var registration = new AssetStoreRegistrationBuilder(CreateSchemaRegistry())
            .RegisterCodec(new IntegerCodec())
            .Build();

        Assert.That(registration.Schemas.TryGetSchema(s_schema, out _), Is.True);
        Assert.That(registration.TryGetCodec<int>(out var codec), Is.True);
        Assert.That(codec!.Schema, Is.EqualTo(s_schema));
        Assert.That(registration.TryGetCodec<string>(out _), Is.False);
        Assert.That(registration.TryGetCodec(new ContentSchemaReference(
            Guid.NewGuid(),
            1,
            0,
            ContentSchemaScope.Content), out _), Is.False);
    }

    private sealed class IntegerCodec : IBinarySchemaCodec<int>
    {
        public Guid SchemaId => s_schema.SchemaId;

        public int SchemaMajor => s_schema.Major;

        public int SchemaMinor => s_schema.Minor;

        public BinaryRecordStatus Read(scoped ReadOnlySpan<byte> source, out int value, out int bytesConsumed)
        {
            value = 0;
            bytesConsumed = 0;

            if (source.Length < sizeof(int))
            {
                return BinaryRecordStatus.NeedMoreData;
            }

            value = BinaryPrimitives.ReadInt32LittleEndian(source);
            bytesConsumed = sizeof(int);
            return BinaryRecordStatus.Complete;
        }

        public BinaryRecordStatus Write(in int value, scoped Span<byte> destination, out int bytesWritten)
        {
            bytesWritten = 0;

            if (destination.Length < sizeof(int))
            {
                return BinaryRecordStatus.DestinationTooSmall;
            }

            BinaryPrimitives.WriteInt32LittleEndian(destination, value);
            bytesWritten = sizeof(int);
            return BinaryRecordStatus.Complete;
        }
    }
}
