// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using TerraFX.Numerics;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class M02FoundationCodecTests
{
    private static readonly SpatialProfileId s_defaultProfileId = new SpatialProfileId(new Guid("c2ddc5d8-9ee6-8676-bdef-dffd36a56d89"));
    private static readonly Guid s_worldGuid = new Guid("00112233-4455-4677-8899-aabbccddeeff");

    private readonly FoundationCodecContext _context;
    private readonly FoundationProfileBinding _defaultProfile;
    private readonly ImmutableArray<FoundationProfileBinding> _profiles;

    public M02FoundationCodecTests()
    {
        _defaultProfile = new FoundationProfileBinding(s_defaultProfileId, SpatialSettings.Default);
        SpatialProfiles.Register(_defaultProfile.Id, _defaultProfile.Settings);
        _profiles = [_defaultProfile];
        _context = new FoundationCodecContext(_profiles, new FoundationPersistenceBinding(
            s_worldGuid, _defaultProfile, 1, 1));
    }

    private static EbinRecord ExtensionRecord(long id, byte[] payload, ushort flags = 1) =>
        new(EbinCodec.ExtensionRecordSchemaId, 1, 0, [EbinPrimitives.Int64(1, id), new EbinField(2, 13, flags, checked((ulong)payload.Length), payload)]);

    private static EbinField ExtensionSet(params EbinRecord[] extensions)
    {
        var field = EbinPrimitives.RecordArray(EbinCodec.ExtensionFieldTag, extensions);
        return new EbinField(field.Tag, field.Kind, 0, field.Count, field.Payload);
    }

    private static EbinField GeometryField(ushort kind, byte[] bytes) => kind switch {
        14 => EbinPrimitives.Geometry(1, EbinPrimitives.Vector2(bytes)),
        15 => EbinPrimitives.Geometry(1, EbinPrimitives.Vector3(bytes)),
        16 => EbinPrimitives.Geometry(1, EbinPrimitives.Vector4(bytes)),
        17 => EbinPrimitives.Geometry(1, new Quaternion(EbinPrimitives.Single(bytes), EbinPrimitives.Single(bytes.AsSpan(4)), EbinPrimitives.Single(bytes.AsSpan(8)), EbinPrimitives.Single(bytes.AsSpan(12)))),
        18 => EbinPrimitives.Geometry(1, new Matrix3x2(EbinPrimitives.Vector2(bytes), EbinPrimitives.Vector2(bytes.AsSpan(8)), EbinPrimitives.Vector2(bytes.AsSpan(16)))),
        19 => EbinPrimitives.Geometry(1, new Matrix4x4(EbinPrimitives.Vector4(bytes), EbinPrimitives.Vector4(bytes.AsSpan(16)), EbinPrimitives.Vector4(bytes.AsSpan(32)), EbinPrimitives.Vector4(bytes.AsSpan(48)))),
        _ => throw new AssertionException("Uncovered geometry kind."),
    };

    private static EbinRecord InputRecord(byte[] bytes)
    {
        Assert.That(EbinCodec.Read(bytes, out var record, out var consumed, out var diagnostic), Is.EqualTo(EbinStatus.Complete), diagnostic.ToString());
        Assert.That(consumed, Is.EqualTo(bytes.Length));
        return record ?? throw new AssertionException("No decoded record.");
    }

    private static EbinRecord[] RecordArrayItems(ReadOnlySpan<byte> bytes)
    {
        var count = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes));
        var records = new EbinRecord[count];

        for (var index = 0; index < records.Length; index++)
        {
            var start = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes[(8 + (index * 8))..]));
            var end = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes[(16 + (index * 8))..]));
            records[index] = InputRecord(bytes[start..end].ToArray());
        }

        return records;
    }

    private static byte[] Sentinel(int length)
    {
        var bytes = new byte[length];
        bytes.AsSpan().Fill(0xA5);
        return bytes;
    }

    [TestCase((byte)0xC2, 1)]
    [TestCase((byte)0xE0, 2)]
    [TestCase((byte)0xF0, 3)]
    public void AnIncompleteUtf8SequenceMustFitItsDeclaredRemainingExtent(byte leadingByte, int declaredLength)
    {
        byte[] bytes = [leadingByte];
        Assert.That(EbinCodec.ReadPayload(12, checked((ulong)declaredLength), declaredLength, bytes, out var value, out var consumed, out var diagnostic), Is.EqualTo(EbinStatus.InvalidRecord));
        Assert.That(value, Is.Null);
        Assert.That(consumed, Is.Zero);
        Assert.That(diagnostic.Category, Is.EqualTo("utf8-invalid"));
        Assert.That(EbinCodec.ReadPayload(12, checked((ulong)declaredLength + 1), declaredLength + 1, bytes, out value, out consumed, out _), Is.EqualTo(EbinStatus.NeedMoreData));
        Assert.That(value, Is.Null);
        Assert.That(consumed, Is.Zero);
    }

    [Test]
    public void CanonicalConstructionMatchesG01ThroughG05AndExactDigests()
    {
        Assert.That(FoundationCatalogue.Schemas.Length, Is.EqualTo(8));
        Assert.That(FoundationCatalogue.Schemas.Sum(static schema => schema.Fields.Length), Is.EqualTo(38));
        Assert.That(FoundationCatalogue.Recipes.Length, Is.EqualTo(9));
        var registry = FoundationCatalogue.RegistryBytes;
        var kernel = FoundationCatalogue.KernelBytes;
        var profile = ProfileBytes();
        var frame2 = Frame2Bytes();
        var frame3 = Frame3Bytes();

        Assert.That(registry.Length, Is.EqualTo(14_112));
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(registry)), Is.EqualTo("f21833596ed8bedeaa7e366505a2ade8ad349ab35f7bf0554ad2d8ce5aeda43f"));
        Assert.That(kernel.Length, Is.EqualTo(2_192));
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(kernel)), Is.EqualTo("494f69b9559de1c609b2c51a11454fcdc3a0be8b108fdc91f242d89b38d18105"));
        Assert.That(profile.Length, Is.EqualTo(544));
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(profile)), Is.EqualTo("5fe3ae0fbae08e4a21b064b1449934a25911176e50def5c6f3bac8239595652f"));
        Assert.That(frame2.Length, Is.EqualTo(880));
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(frame2)), Is.EqualTo("ef8d0cf21410ebcd8926dfb52155f437a7324ff5f0dce507bb3fa565989551fe"));
        Assert.That(frame3.Length, Is.EqualTo(896));
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(frame3)), Is.EqualTo("b72d5e5735a6586a9774ef608426d443ebf135bf678a44f2d42ff2290d72f357"));

        foreach (var (dimension, bytes) in new[] { (2, frame2), (3, frame3) })
        {
            var binding = new FoundationPersistenceBinding(s_worldGuid, _defaultProfile, OwnerIdAllocator.Allocate(), 1);
            EbinRecord captured;

            if (dimension == 2)
            {
                Assert.That(FoundationFrameCodec.Read2(bytes, _context, out var staged, out _, out _), Is.EqualTo(EbinStatus.Complete));
                var frame = (staged ?? throw new AssertionException("No staged frame.")).Project(binding);
                captured = FoundationFrameCodec.Capture(frame, binding, binding.OwnerId, 1);
                Assert.That(BitConverter.SingleToInt32Bits(frame.Minimum.Y), Is.EqualTo(int.MinValue));
                Assert.That(BitConverter.SingleToInt32Bits(frame.Origin.Offset.Y), Is.EqualTo(1));
            }
            else
            {
                Assert.That(FoundationFrameCodec.Read3(bytes, _context, out var staged, out _, out _), Is.EqualTo(EbinStatus.Complete));
                var frame = (staged ?? throw new AssertionException("No staged frame.")).Project(binding);
                captured = FoundationFrameCodec.Capture(frame, binding, binding.OwnerId, 1);
                Assert.That(frame.Revision, Is.EqualTo(long.MaxValue));
                Assert.That(frame.Origin.Chunk.Z, Is.EqualTo(int.MaxValue));
            }

            Assert.That(EbinPrimitives.Encode(captured), Is.EqualTo(bytes));
        }
    }

    [Test]
    public void CaptureRejectsForeignOwnersAndStaleBindings()
    {
        var frame = Frame2Value();
        var binding = new FoundationPersistenceBinding(s_worldGuid, _defaultProfile, 1, 1);

        _ = Assert.Throws<InvalidOperationException>(() => FoundationFrameCodec.Capture(frame, binding, 2, 1));
        _ = Assert.Throws<InvalidOperationException>(() => FoundationFrameCodec.Capture(frame, binding, 1, 2));
    }

    [Test]
    public void CheckedExtentBudgetsAreEnforcedBeforeWaitingOrWriting()
    {
        Assert.That(EbinCodec.TryAdd(ulong.MaxValue, 1, out _), Is.False);
        Assert.That(EbinCodec.TryMultiply(ulong.MaxValue, 64, out _), Is.False);
        Assert.That(EbinCodec.TryAlign(ulong.MaxValue - 14, out _), Is.False);
        Assert.That(EbinCodec.TryAlign(ulong.MaxValue - 15, out var aligned), Is.True);
        Assert.That(aligned, Is.EqualTo(ulong.MaxValue - 15));

        var id = new Guid("01234567-89ab-4cde-8f01-23456789abcd");
        var exact = new EbinRecord(id, 1, 0, [EbinPrimitives.Bytes(1, new byte[65_440])]);
        var destination = Sentinel(65_537);
        Assert.That(EbinCodec.Write(exact, destination, out var written, out _), Is.EqualTo(EbinStatus.Complete));
        Assert.That(written, Is.EqualTo(65_536));
        Assert.That(destination[written], Is.EqualTo(0xA5));
        Assert.That(EbinCodec.Read(destination, out var record, out var consumed, out _), Is.EqualTo(EbinStatus.Complete));
        Assert.That(record, Is.Not.Null);
        Assert.That(consumed, Is.EqualTo(65_536));
        var over = new EbinRecord(id, 1, 0, [EbinPrimitives.Bytes(1, new byte[65_441])]);
        destination = Sentinel(32);
        Assert.That(EbinCodec.Write(over, destination, out written, out _), Is.EqualTo(EbinStatus.InvalidRecord));
        Assert.That(written, Is.Zero);
        Assert.That(destination, Is.All.EqualTo(0xA5));
        var tinyBudget = new FoundationCodecContext(_profiles, _context.Persistence, storageBudget: 1);
        Assert.That(FoundationCodec.Write(_defaultProfile.CreateRecord(), tinyBudget, destination, out written, out var diagnostic), Is.EqualTo(EbinStatus.InvalidRecord));
        Assert.That(written, Is.Zero);
        Assert.That(diagnostic.Category, Is.EqualTo("retained-storage-budget"));
        Assert.That(destination, Is.All.EqualTo(0xA5));
        var profileBytes = ProfileBytes();
        Assert.That(FoundationCodec.Read(profileBytes, tinyBudget, out record, out consumed, out _), Is.EqualTo(EbinStatus.InvalidRecord));
        Assert.That(record, Is.Null);
        Assert.That(consumed, Is.Zero);
        Assert.That(FoundationCodec.Read(profileBytes.AsSpan(0, 40), tinyBudget, out record, out consumed, out diagnostic), Is.EqualTo(EbinStatus.InvalidRecord));
        Assert.That(diagnostic.Category, Is.EqualTo("retained-storage-budget"));
        Assert.That(record, Is.Null);
        Assert.That(consumed, Is.Zero);
        var overBudgetRecord = profileBytes.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(overBudgetRecord.AsSpan(32), checked((ulong)EbinCodec.MaximumRecordBytes + 16));
        Assert.That(FoundationCodec.Read(overBudgetRecord, _context, out record, out consumed, out diagnostic), Is.EqualTo(EbinStatus.InvalidRecord));
        Assert.That(diagnostic.Category, Is.EqualTo("record-byte-budget"));
        Assert.That(record, Is.Null);
        Assert.That(consumed, Is.Zero);
    }

    [TestCase(6U, 416)]
    [TestCase(7U, 448)]
    [TestCase(8U, 480)]
    public void DigestRejectionsIdentifyTheOffendingField(uint tag, int offset)
    {
        var bytes = ProfileBytes();
        bytes[offset] ^= 1;
        Assert.That(FoundationCodec.Read(bytes, _context, out var value, out var consumed, out var diagnostic), Is.EqualTo(EbinStatus.InvalidRecord));
        Assert.That(value, Is.Null);
        Assert.That(consumed, Is.Zero);
        Assert.That(diagnostic.Tag, Is.EqualTo(tag));
        Assert.That(diagnostic.Offset, Is.EqualTo(checked((ulong)offset)));
        Assert.That(diagnostic.Category, Is.EqualTo("profile-digest-binding"));
    }

    [Test]
    public void EmptyArrayPayloadsMatchLiteralLayouts()
    {
        var fixedArray = Sentinel(32);
        var expectedFixedArray = Convert.FromHexString("000000000000000000000000000000000f0000000c0000000000000000000000");
        Assert.That(EbinPrimitives.WriteFixedArray(15, 0, [], fixedArray, out var written, out var diagnostic), Is.EqualTo(EbinStatus.Complete), diagnostic.ToString());
        Assert.That(written, Is.EqualTo(32));
        Assert.That(fixedArray, Is.EqualTo(expectedFixedArray));

        var recordArray = Sentinel(16);
        var expectedRecordArray = Convert.FromHexString("00000000000000001000000000000000");
        Assert.That(EbinPrimitives.WriteRecordArray([], recordArray, out written, out diagnostic), Is.EqualTo(EbinStatus.Complete), diagnostic.ToString());
        Assert.That(written, Is.EqualTo(16));
        Assert.That(recordArray, Is.EqualTo(expectedRecordArray));

        var wrongStride = new byte[48];
        BinaryPrimitives.WriteUInt16LittleEndian(wrongStride.AsSpan(16), 15);
        BinaryPrimitives.WriteUInt32LittleEndian(wrongStride.AsSpan(20), 16);
        Assert.That(EbinCodec.ReadPayload(23, 1, wrongStride.Length, wrongStride, out var value, out written, out diagnostic), Is.EqualTo(EbinStatus.InvalidRecord));
        Assert.That(value, Is.Null);
        Assert.That(written, Is.Zero);
        Assert.That(diagnostic.Category, Is.EqualTo("fixedarray-stride"));
    }

    [Test]
    public void EveryFixedElementKindHasItsPermanentStrideAndAtomicArrayWriter()
    {
        (ushort Kind, int Stride)[] elements = [(1, 1), (2, 2), (3, 4), (4, 8), (5, 1), (6, 2), (7, 4), (8, 8), (9, 1), (10, 4), (11, 16),
            (14, 8), (15, 12), (16, 16), (17, 16), (18, 24), (19, 64), (20, 16), (21, 24)];

        foreach (var (kind, stride) in elements)
        {
            Assert.That(EbinCodec.Stride(kind), Is.EqualTo(stride));
            var data = new byte[stride * 2];

            for (var index = 0; index < data.Length; index++)
            {
                data[index] = kind == 9 ? (byte)(index & 1) : checked((byte)index);
            }

            var expected = new byte[32 + data.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(16), kind);
            BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(20), checked((uint)stride));
            data.CopyTo(expected, 32);

            foreach (var capacity in new[] { 0, expected.Length - 1, expected.Length, expected.Length + 16 })
            {
                var destination = Sentinel(capacity);
                var status = EbinPrimitives.WriteFixedArray(kind, 2, data, destination, out var written, out _);
                Assert.That(status, Is.EqualTo(capacity < expected.Length ? EbinStatus.DestinationTooSmall : EbinStatus.Complete));

                if (status == EbinStatus.Complete)
                {
                    Assert.That(written, Is.EqualTo(expected.Length));
                    Assert.That(destination.AsSpan(0, written).ToArray(), Is.EqualTo(expected));
                    Assert.That(destination.AsSpan(written).ToArray(), Is.All.EqualTo(0xA5));
                }
                else
                {
                    Assert.That(written, Is.Zero);
                    Assert.That(destination, Is.All.EqualTo(0xA5));
                }
            }

            Assert.That(EbinCodec.ReadPayload(23, 2, expected.Length, expected, out var value, out var consumed, out _), Is.EqualTo(EbinStatus.Complete));
            Assert.That(value, Is.EqualTo(expected));
            Assert.That(consumed, Is.EqualTo(expected.Length));
        }

        foreach (var capacity in new[] { 0, 1, 64, 128 })
        {
            var destination = Sentinel(capacity);
            Assert.That(EbinPrimitives.WriteFixedArray(9, 1, [2], destination, out var written, out var diagnostic), Is.EqualTo(EbinStatus.InvalidRecord));
            Assert.That(written, Is.Zero);
            Assert.That(diagnostic.Category, Is.EqualTo("boolean-domain"));
            Assert.That(destination, Is.All.EqualTo(0xA5));
            Assert.That(EbinPrimitives.WriteFixedArray(15, ulong.MaxValue, [], destination, out written, out diagnostic), Is.EqualTo(EbinStatus.InvalidRecord));
            Assert.That(written, Is.Zero);
            Assert.That(diagnostic.Category, Is.EqualTo("array-product-overflow"));
            Assert.That(destination, Is.All.EqualTo(0xA5));
            var invalid = new EbinRecord(FoundationCatalogue.ProfileId, 0, 0, []);
            Assert.That(EbinPrimitives.WriteRecordArray([invalid], destination, out written, out diagnostic), Is.EqualTo(EbinStatus.InvalidRecord));
            Assert.That(diagnostic.Category, Is.EqualTo("signed-projection-range"));
            Assert.That(written, Is.Zero);
            Assert.That(destination, Is.All.EqualTo(0xA5));
        }
    }

    [Test]
    public void FoundationAdmissionRejectsUnregisteredFixedTagsEvenWhenOptional()
    {
        var profile = _defaultProfile.CreateRecord();
        var input = new EbinRecord(profile.SchemaId, profile.Major, profile.Minor,
            [.. profile.Fields, new EbinField(11, 13, 0, 1, [0x12])]);
        var bytes = EbinPrimitives.Encode(input);

        Assert.That(FoundationCodec.Read(bytes, _context, out var value, out var consumed, out var diagnostic), Is.EqualTo(EbinStatus.UnsupportedSchema));
        Assert.That(value, Is.Null);
        Assert.That(consumed, Is.Zero);
        Assert.That(diagnostic.Category, Is.EqualTo("unsupported-fixed-tag"));
        var destination = Sentinel(bytes.Length);
        Assert.That(FoundationCodec.Write(input, _context, destination, out var written, out diagnostic), Is.EqualTo(EbinStatus.UnsupportedSchema));
        Assert.That(written, Is.Zero);
        Assert.That(destination, Is.All.EqualTo(0xA5));
        Assert.That(diagnostic.Category, Is.EqualTo("unsupported-fixed-tag"));
    }

    [Test]
    public void FoundationWritesAreAtomicWhenTheDestinationIsTooSmall()
    {
        var bytes = ProfileBytes();
        var destination = Sentinel(bytes.Length - 1);

        Assert.That(FoundationCodec.Write(_defaultProfile.CreateRecord(), _context, destination, out var written, out var diagnostic), Is.EqualTo(EbinStatus.DestinationTooSmall));
        Assert.That(written, Is.Zero);
        Assert.That(diagnostic.Category, Is.EqualTo("destination-too-small"));
        Assert.That(destination, Is.All.EqualTo(0xA5));
    }

    [TestCase(12, 65, 13)]
    [TestCase(34, 2, 35)]
    [TestCase(32, 1, 33)]
    [TestCase(40, 0, 41)]
    [TestCase(80, 1, 81)]
    [TestCase(88, 2, 89)]
    public void ImpossiblePartialUnsignedHeaderAndScalarClaimsRejectImmediately(int offset, byte replacement, int length)
    {
        var bytes = ProfileBytes();
        bytes[offset] = replacement;
        Assert.That(FoundationCodec.Read(bytes.AsSpan(0, length), _context, out var value, out var consumed, out _), Is.EqualTo(EbinStatus.InvalidRecord));
        Assert.That(value, Is.Null);
        Assert.That(consumed, Is.Zero);
    }

    [Test]
    public void InvalidHeaderAndUnknownSchemaPreemptIncompleteInput()
    {
        var invalidHeader = ProfileBytes();
        invalidHeader[0] = 0;
        Assert.That(FoundationCodec.Read(invalidHeader.AsSpan(0, 4), _context, out var value, out var consumed, out _), Is.EqualTo(EbinStatus.InvalidRecord));
        Assert.That(value, Is.Null);
        Assert.That(consumed, Is.Zero);

        var unknownSchema = EbinPrimitives.Encode(new EbinRecord(new Guid("01234567-89ab-4cde-8f01-23456789abcd"), 1, 0, []));
        Assert.That(FoundationCodec.Read(unknownSchema.AsSpan(0, 32), _context, out value, out consumed, out var diagnostic), Is.EqualTo(EbinStatus.UnsupportedSchema));
        Assert.That(value, Is.Null);
        Assert.That(consumed, Is.Zero);
        Assert.That(diagnostic.Category, Is.EqualTo("unsupported-schema-id"));
    }

    [Test]
    [TestCase("registry/manifest", "a842b722-20a3-8e3e-9824-39b812becd31")]
    [TestCase("registry/schema", "07902135-1633-8b5e-86e4-436850ca1149")]
    [TestCase("ebin/extension", "f57cb3ae-6f40-81d5-b6b1-80dc840cd30c")]
    [TestCase("foundation/profile", "3b9760ce-468a-8aab-bb89-f38db9e3bc00")]
    [TestCase("foundation/frame2", "ae928678-285e-82be-b250-cea4bb2cb7c3")]
    [TestCase("foundation/frame3", "42626aff-fc5c-864d-867c-43c666699777")]
    [TestCase("kernel/manifest", "b4bbf3fb-503c-8a6d-8687-878cbc3cc9d6")]
    [TestCase("kernel/recipe", "f7a613f1-1c19-8c64-9448-31be7d694db2")]
    public void NameUuidMatchesTheFoundationCatalogue(string name, string expected)
    {
        var namespaceId = new Guid("2ed8b750-e1a0-4b46-9000-000000000001");
        var id = FoundationNameUuid.Create(namespaceId, name);
        Assert.That(id, Is.EqualTo(new Guid(expected)));
        var bytes = new byte[16];
        _ = id.TryWriteBytes(bytes, bigEndian: true, out _);
        Assert.That(bytes[6] >> 4, Is.EqualTo(8));
        Assert.That(bytes[8] >> 6, Is.EqualTo(2));
        FoundationNameUuid.ValidateAssignments([new FoundationNameAssignment(id, namespaceId, name, FoundationNameUuid.Recipe)]);
    }

    [TestCase("")]
    [TestCase("spatial/é")]
    public void NameUuidRejectsEmptyOrNonAsciiNames(string name)
    {
        _ = Assert.Throws<ArgumentException>(() => FoundationNameUuid.Create(new Guid("2ed8b750-e1a0-4b46-9000-000000000001"), name));
    }

    [Test]
    public void NameUuidRejectsNamesLongerThanItsBound()
    {
        _ = Assert.Throws<ArgumentException>(() => FoundationNameUuid.Create(
            new Guid("2ed8b750-e1a0-4b46-9000-000000000001"),
            new string('a', 1025)));
        Assert.That(
            FoundationNameUuid.Create(new Guid("00000000-0000-0000-0000-000000000001"), new string('a', 1024)),
            Is.EqualTo(new Guid("0e4d358b-9161-8149-a813-0a0f19b65606")));
    }

    [Test]
    public void OptionalDataRemainsOwnedInertAndSurvivesCaptureWithANewOwner()
    {
        var expectedProfileBytes = EbinPrimitives.Encode(OptionalProfileRecord());
        var bytes = expectedProfileBytes.ToArray();
        Assert.That(FoundationCodec.Read(bytes, _context, out var profile, out _, out _), Is.EqualTo(EbinStatus.Complete));
        var admitted = profile ?? throw new AssertionException("No admitted profile.");
        bytes.AsSpan().Fill(0xFF);

        var destination = Sentinel(expectedProfileBytes.Length);
        Assert.That(FoundationCodec.Write(admitted, _context, destination, out var written, out _), Is.EqualTo(EbinStatus.Complete));
        Assert.That(destination.AsSpan(0, written).ToArray(), Is.EqualTo(expectedProfileBytes));
        var frame = Frame2Record();
        var fields = frame.Fields.Where(static field => field.Tag != 2)
            .Append(EbinPrimitives.Record(2, admitted))
            .Append(ExtensionSet(ExtensionRecord(-3, [9, 8, 7])))
            .ToArray();
        var extended = new EbinRecord(frame.SchemaId, 1, 0, fields);
        var encoded = EbinPrimitives.Encode(extended);
        Assert.That(FoundationFrameCodec.Read2(encoded, _context, out var staged, out _, out _), Is.EqualTo(EbinStatus.Complete));
        var value = staged ?? throw new AssertionException("No staged frame.");
        var target = new FoundationPersistenceBinding(value.WorldGuid, value.Profile, OwnerIdAllocator.Allocate(), 1);
        var projected = value.Project(target);
        var captured = FoundationFrameCodec.Capture(projected, target, target.OwnerId, 1, value);
        Assert.That(EbinPrimitives.Encode(captured), Is.EqualTo(encoded));
        var extension = RecordArrayItems(captured.GetField(EbinCodec.ExtensionFieldTag).Payload)[0];
        Assert.That(extension.GetField(2).Payload.ToArray(), Is.EqualTo(new byte[] { 9, 8, 7 }));
        var authored = FoundationFrameCodec.Capture(projected, target, target.OwnerId, 1);
        Assert.That(authored.Fields.Length, Is.EqualTo(6));
        Assert.That(EbinCodec.Stage(authored.GetField(2).Payload).Fields.Length, Is.EqualTo(9));
    }

    [Test]
    public void OptionalExtensionsRejectEmptySetsAndClosedSchemas()
    {
        var profile = _defaultProfile.CreateRecord();
        var empty = new EbinRecord(profile.SchemaId, profile.Major, profile.Minor, [
            .. profile.Fields,
            ExtensionSet(),
        ]);

        Assert.That(FoundationCodec.Read(EbinPrimitives.Encode(empty), _context, out var value, out var consumed, out var diagnostic), Is.EqualTo(EbinStatus.InvalidRecord));
        Assert.That(value, Is.Null);
        Assert.That(consumed, Is.Zero);
        Assert.That(diagnostic.Category, Is.EqualTo("extension-array-count"));

        var registry = new EbinRecord(FoundationCatalogue.Registry.SchemaId, FoundationCatalogue.Registry.Major, FoundationCatalogue.Registry.Minor, [
            .. FoundationCatalogue.Registry.Fields,
            ExtensionSet(ExtensionRecord(-1, [0xA1])),
        ]);
        Assert.That(FoundationCodec.Read(EbinPrimitives.Encode(registry), _context, out value, out consumed, out diagnostic), Is.EqualTo(EbinStatus.InvalidRecord));
        Assert.That(value, Is.Null);
        Assert.That(consumed, Is.Zero);
        Assert.That(diagnostic.Category, Is.EqualTo("registry-extra-field"));
    }

    [Test]
    public void OptionalExtensionsRequireTheFixedEntrySchema()
    {
        var profile = _defaultProfile.CreateRecord();
        var invalidEntry = new EbinRecord(new Guid("01234567-89ab-4cde-8f01-23456789abcd"), 1, 0,
            [EbinPrimitives.Int64(1, -7), EbinPrimitives.Bytes(2, [0xA1])]);
        var input = new EbinRecord(profile.SchemaId, profile.Major, profile.Minor, [
            .. profile.Fields,
            ExtensionSet(invalidEntry),
        ]);

        Assert.That(FoundationCodec.Read(EbinPrimitives.Encode(input), _context, out var value, out var consumed, out var diagnostic), Is.EqualTo(EbinStatus.InvalidRecord));
        Assert.That(value, Is.Null);
        Assert.That(consumed, Is.Zero);
        Assert.That(diagnostic.Category, Is.EqualTo("extension-record-schema"));
    }

    [TestCase(0, "extension-id-zero")]
    [TestCase(-1, "extension-id-duplicate")]
    public void OptionalExtensionsRequireUniqueNonzeroSignedIdentifiers(long secondId, string category)
    {
        var profile = _defaultProfile.CreateRecord();
        var extensions = ExtensionSet(
            ExtensionRecord(-1, [1]),
            ExtensionRecord(secondId, [2]));
        var input = new EbinRecord(profile.SchemaId, profile.Major, profile.Minor, [.. profile.Fields, extensions]);

        Assert.That(FoundationCodec.Read(EbinPrimitives.Encode(input), _context, out var value, out var consumed, out var diagnostic), Is.EqualTo(EbinStatus.InvalidRecord));
        Assert.That(value, Is.Null);
        Assert.That(consumed, Is.Zero);
        Assert.That(diagnostic.Category, Is.EqualTo(category));
    }

    [Test]
    public void OptionalExtensionsSupportTheFullSignedIdentifierRange()
    {
        var profile = _defaultProfile.CreateRecord();
        var input = new EbinRecord(profile.SchemaId, profile.Major, profile.Minor, [
            .. profile.Fields,
            ExtensionSet(
                ExtensionRecord(long.MinValue, [0xA1]),
                ExtensionRecord(long.MaxValue, [0xB2])),
        ]);

        Assert.That(FoundationCodec.Read(EbinPrimitives.Encode(input), _context, out var value, out _, out _), Is.EqualTo(EbinStatus.Complete));
        Assert.That(EbinPrimitives.Encode(value ?? throw new AssertionException("No admitted profile.")), Is.EqualTo(EbinPrimitives.Encode(input)));
    }

    [Test]
    public void OptionalExtensionsUseSignedOrderAndPreserveUnknownPayloads()
    {
        var profile = _defaultProfile.CreateRecord();
        var extensions = ExtensionSet(
            ExtensionRecord(-7, [0xA1]),
            ExtensionRecord(1, [0xB2]));
        var input = new EbinRecord(profile.SchemaId, profile.Major, profile.Minor, [.. profile.Fields, extensions]);
        var bytes = EbinPrimitives.Encode(input);

        Assert.That(FoundationCodec.Read(bytes, _context, out var admitted, out _, out _), Is.EqualTo(EbinStatus.Complete));
        Assert.That(EbinPrimitives.Encode(admitted ?? throw new AssertionException("No admitted profile.")), Is.EqualTo(bytes));

        var unsorted = new EbinRecord(profile.SchemaId, profile.Major, profile.Minor, [
            .. profile.Fields,
            ExtensionSet(
                ExtensionRecord(1, [0xB2]),
                ExtensionRecord(-7, [0xA1])),
        ]);
        Assert.That(FoundationCodec.Read(EbinPrimitives.Encode(unsorted), _context, out var rejected, out var consumed, out var diagnostic), Is.EqualTo(EbinStatus.InvalidRecord));
        Assert.That(rejected, Is.Null);
        Assert.That(consumed, Is.Zero);
        Assert.That(diagnostic.Category, Is.EqualTo("extension-id-order"));
    }

    [Test]
    public void PackageTuplesAndOwnerExtentPolicyAreExplicitAdmissionInputs()
    {
        var profile = _defaultProfile;
        var altered = profile.KernelDigest.ToArray();
        altered[0] ^= 1;
        var package = new FoundationProfileTuple(1, 2, 2, profile.Settings, profile.SettingsDigest, profile.RegistryDigest, altered);
        var context = new FoundationCodecContext(_profiles, _context.Persistence, package);

        Assert.That(FoundationCodec.Read(ProfileBytes(), context, out var value, out var consumed, out var diagnostic), Is.EqualTo(EbinStatus.InvalidRecord));
        Assert.That(value, Is.Null);
        Assert.That(consumed, Is.Zero);
        Assert.That(diagnostic.Category, Is.EqualTo("profile-package-binding"));
        var binding = new FoundationPersistenceBinding(s_worldGuid, profile, 1, 1, static (minimum, maximum) => minimum.X >= -1 && maximum.X <= 1);
        context = new FoundationCodecContext(_profiles, binding);
        Assert.That(FoundationCodec.Read(Frame2Bytes(), context, out value, out consumed, out diagnostic), Is.EqualTo(EbinStatus.InvalidRecord));
        Assert.That(diagnostic.Category, Is.EqualTo("frame-owner-extent-policy"));
        Assert.That(value, Is.Null);
        Assert.That(consumed, Is.Zero);
    }

    [Test]
    public void ProfileReadersAcceptZeroGapsButWritersCanonicalizeWithoutDiscardingExtensions()
    {
        var original = EbinPrimitives.Encode(OptionalProfileRecord());
        var count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(12)));
        var payloadStart = 64 + (count * 32);
        var bytes = new byte[original.Length + 16];
        original.AsSpan(0, payloadStart).CopyTo(bytes);
        original.AsSpan(payloadStart).CopyTo(bytes.AsSpan(payloadStart + 16));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), checked((ulong)bytes.Length));

        for (var index = 0; index < count; index++)
        {
            var entry = bytes.AsSpan(64 + (index * 32), 32);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[8..], BinaryPrimitives.ReadUInt64LittleEndian(entry[8..]) + 16);
        }

        Assert.That(FoundationCodec.Read(bytes, _context, out var value, out var consumed, out _), Is.EqualTo(EbinStatus.Complete));
        Assert.That(consumed, Is.EqualTo(bytes.Length));
        var destination = Sentinel(bytes.Length);
        Assert.That(FoundationCodec.Write(value ?? throw new AssertionException("No admitted profile."), _context, destination, out var written, out _), Is.EqualTo(EbinStatus.Complete));
        Assert.That(destination.AsSpan(0, written).ToArray(), Is.EqualTo(original));
        Assert.That(destination.AsSpan(written).ToArray(), Is.All.EqualTo(0xA5));
    }

    [Test]
    public void RawGeometryPackersPreserveEverySpecialBitInEveryLogicalLane()
    {
        uint[] patterns = [0, 0x80000000, 1, 0x7F7FFFFF, 0x7F800000, 0xFF800000, 0x7FC12345, 0x7FC54321, 0x7F812345];

        foreach (var kind in new ushort[] { 14, 15, 16, 17, 18, 19 })
        {
            foreach (var pattern in patterns)
            {
                var bytes = new byte[EbinCodec.Stride(kind)];

                for (var offset = 0; offset < bytes.Length; offset += 4)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), pattern);
                }

                Assert.That(GeometryField(kind, bytes).Payload.ToArray(), Is.EqualTo(bytes), $"{kind}, {pattern:X8}");
            }
        }
    }

    [TestCase((ushort)22)]
    [TestCase((ushort)24)]
    public void RawRecordPayloadsRejectRequiredUnknownKindsBeforeCapacity(ushort kind)
    {
        var profile = _defaultProfile.CreateRecord();
        var invalidChild = new EbinRecord(profile.SchemaId, profile.Major, profile.Minor, [
            .. profile.Fields,
            new EbinField(11, 65000, 1, 1, [0x12]),
        ]);
        var child = EbinPrimitives.Encode(invalidChild);
        var bytes = child;

        if (kind == 24)
        {
            bytes = new byte[32 + child.Length];
            BinaryPrimitives.WriteUInt64LittleEndian(bytes, 1);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), 32);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), checked((ulong)bytes.Length));
            child.CopyTo(bytes, 32);
        }

        foreach (var capacity in new[] { 0, 1, bytes.Length, bytes.Length + 17 })
        {
            var destination = Sentinel(capacity);
            Assert.That(EbinCodec.WritePayload(kind, 1, bytes, destination, out var written, out var diagnostic), Is.EqualTo(EbinStatus.UnsupportedSchema));
            Assert.That(written, Is.Zero);
            Assert.That(diagnostic.Category, Is.EqualTo("unsupported-required-kind"));
            Assert.That(destination, Is.All.EqualTo(0xA5));

            if (kind == 24)
            {
                Assert.That(EbinPrimitives.WriteRecordArray([invalidChild], destination, out written, out diagnostic), Is.EqualTo(EbinStatus.UnsupportedSchema));
                Assert.That(written, Is.Zero);
                Assert.That(diagnostic.Category, Is.EqualTo("unsupported-required-kind"));
                Assert.That(destination, Is.All.EqualTo(0xA5));
            }
        }

        foreach (var length in new[] { kind == 22 ? 360 : 392, bytes.Length - 1, bytes.Length })
        {
            Assert.That(EbinCodec.ReadPayload(kind, 1, bytes.Length, bytes.AsSpan(0, length), out var value, out var consumed, out var diagnostic), Is.EqualTo(EbinStatus.UnsupportedSchema));
            Assert.That(value, Is.Null);
            Assert.That(consumed, Is.Zero);
            Assert.That(diagnostic.Category, Is.EqualTo("unsupported-required-kind"));
        }

        Assert.That(FoundationCodec.Read(child.AsSpan(0, 360), _context, out var record, out var recordConsumed, out _), Is.EqualTo(EbinStatus.UnsupportedSchema));
        Assert.That(record, Is.Null);
        Assert.That(recordConsumed, Is.Zero);
    }

    [Test]
    public void ReadsDoNotAllocateOwnersOrRegisterProfilesAndBindingMisuseThrows()
    {
        var before = OwnerIdAllocator.Allocate();
        var unknownId = new SpatialProfileId(new Guid("11111111-2222-4333-8444-555555555555"));
        var knownProfile = _defaultProfile.CreateRecord();
        var unknownProfile = new EbinRecord(knownProfile.SchemaId, knownProfile.Major, knownProfile.Minor, [
            .. knownProfile.Fields.Where(static field => field.Tag != 9),
            EbinPrimitives.Guid(9, unknownId.Value),
        ]);
        var frameRecord = Frame2Record();
        var fields = frameRecord.Fields.Where(static field => field.Tag != 2)
            .Append(EbinPrimitives.Record(2, unknownProfile)).ToArray();
        var unknown = EbinPrimitives.Encode(new EbinRecord(frameRecord.SchemaId, frameRecord.Major, frameRecord.Minor, fields));

        Assert.That(SpatialProfiles.TryGetSettings(unknownId, out _), Is.False);
        Assert.That(FoundationCodec.Read(unknown, _context, out var rejected, out var consumed, out _), Is.EqualTo(EbinStatus.UnsupportedSchema));
        Assert.That(rejected, Is.Null);
        Assert.That(consumed, Is.Zero);
        var frame = Frame2Bytes();
        Assert.That(FoundationCodec.Read(frame, _context, out _, out _, out _), Is.EqualTo(EbinStatus.Complete));
        Assert.That(SpatialProfiles.TryGetSettings(unknownId, out _), Is.False);
        Assert.That(OwnerIdAllocator.Allocate(), Is.EqualTo(before + 1));
        var noWorld = new FoundationCodecContext(_profiles);
        _ = Assert.Throws<ArgumentException>(() => FoundationCodec.Read(frame.AsSpan(0, 32), noWorld, out _, out _, out _));
        _ = Assert.Throws<ArgumentException>(() => FoundationCodec.Read(frame, noWorld, out _, out _, out _));
        _ = Assert.Throws<ArgumentException>(() => new FoundationPersistenceBinding(Guid.Empty, _defaultProfile, 1, 1));
        Assert.That(FoundationFrameCodec.Read3(frame, _context, out var wrongDimension, out consumed, out _), Is.EqualTo(EbinStatus.InvalidRecord));
        Assert.That(wrongDimension, Is.Null);
        Assert.That(consumed, Is.Zero);
        Assert.That(typeof(EbinRecord).IsPublic || typeof(FoundationCodec).IsPublic || typeof(FoundationPersistenceBinding).IsPublic, Is.False);
        Assert.That(typeof(SpatialFrame2).GetProperty("WorldGuid"), Is.Null);
    }

    [Test]
    public void RecordArrayOffsetsMatchLiteralChildRecords()
    {
        var firstChild = new EbinRecord(new Guid("00010203-0405-0607-0809-0a0b0c0d0e0f"), 1, 0, []);
        var secondChild = new EbinRecord(new Guid("10111213-1415-1617-1819-1a1b1c1d1e1f"), 1, 0, []);
        var firstBytes = Convert.FromHexString(
            "4542494e010000004000000000000000"
            + "000102030405060708090a0b0c0d0e0f"
            + "4000000000000000"
            + "0000000000000000"
            + "01000000000000000000000000000000");
        var secondBytes = Convert.FromHexString(
            "4542494e010000004000000000000000"
            + "101112131415161718191a1b1c1d1e1f"
            + "4000000000000000"
            + "0000000000000000"
            + "01000000000000000000000000000000");
        var destination = Sentinel(160);

        Assert.That(EbinPrimitives.WriteRecordArray([firstChild, secondChild], destination, out var written, out var diagnostic), Is.EqualTo(EbinStatus.Complete), diagnostic.ToString());
        Assert.That(written, Is.EqualTo(160));
        Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(destination), Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(destination.AsSpan(8)), Is.EqualTo(32));
        Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(destination.AsSpan(16)), Is.EqualTo(96));
        Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(destination.AsSpan(24)), Is.EqualTo(160));
        Assert.That(destination.AsSpan(32, 64).ToArray(), Is.EqualTo(firstBytes));
        Assert.That(destination.AsSpan(96, 64).ToArray(), Is.EqualTo(secondBytes));
    }

    [Test]
    public void RegistryAndKernelAdmissionRejectChangedCatalogueValues()
    {
        var kernel = FoundationCatalogue.Kernel;
        var changedKnownCode = new EbinRecord(kernel.SchemaId, kernel.Major, kernel.Minor, [
            EbinPrimitives.UInt32(2, 1),
            .. kernel.Fields.Where(static field => field.Tag != 2),
        ]);
        var status = FoundationCodec.Read(EbinPrimitives.Encode(changedKnownCode), _context, out var value, out var consumed, out var diagnostic);
        Assert.That(status, Is.EqualTo(EbinStatus.InvalidRecord));
        Assert.That(value, Is.Null);
        Assert.That(consumed, Is.Zero);
        Assert.That(diagnostic.Category, Is.EqualTo("kernel-code-mismatch"));

        var unknownCode = new EbinRecord(kernel.SchemaId, kernel.Major, kernel.Minor, [
            EbinPrimitives.UInt32(1, 99),
            .. kernel.Fields.Where(static field => field.Tag != 1),
        ]);
        status = FoundationCodec.Read(EbinPrimitives.Encode(unknownCode), _context, out value, out consumed, out diagnostic);
        Assert.That(status, Is.EqualTo(EbinStatus.UnsupportedSchema));
        Assert.That(value, Is.Null);
        Assert.That(consumed, Is.Zero);
        Assert.That(diagnostic.Category, Is.EqualTo("unsupported-kernel-code"));

        var registry = FoundationCatalogue.Registry;
        var schemaRows = RecordArrayItems(registry.GetField(1).Payload);
        var incompleteRegistry = new EbinRecord(registry.SchemaId, registry.Major, registry.Minor, [
            EbinPrimitives.RecordArray(1, schemaRows.AsSpan(0, schemaRows.Length - 1)),
        ]);
        status = FoundationCodec.Read(EbinPrimitives.Encode(incompleteRegistry), _context, out value, out consumed, out diagnostic);
        Assert.That(status, Is.EqualTo(EbinStatus.InvalidRecord));
        Assert.That(value, Is.Null);
        Assert.That(consumed, Is.Zero);
        Assert.That(diagnostic.Category, Is.EqualTo("registry-schema-set"));
    }

    [Test]
    public void RepresentativeValidPrefixesRemainIncomplete()
    {
        var records = new[] {
            FoundationCatalogue.RegistryBytes.ToArray(),
            FoundationCatalogue.KernelBytes.ToArray(),
            ProfileBytes(),
            Frame2Bytes(),
            Frame3Bytes(),
        };

        foreach (var bytes in records)
        {
            foreach (var length in new[] { 0, 4, 32, 63, bytes.Length - 1 })
            {
                var status = FoundationCodec.Read(bytes.AsSpan(0, length), _context, out var value, out var consumed, out var diagnostic);
                Assert.That(status, Is.EqualTo(EbinStatus.NeedMoreData), $"{bytes.Length}, prefix {length}: {diagnostic}");
                Assert.That(value, Is.Null);
                Assert.That(consumed, Is.Zero);
            }
        }
    }

    [TestCase(0, 12, "9ef6534033621925d39355058d26782eb3b1a41a20690efd829ef6d3efe0eb1c")]
    [TestCase(-30, 8, "febbf219a80134979f4354b10f9f4a61128b9c8699fcb2be3517045f244fad28")]
    [TestCase(30, 16, "79d049ac28992fa062a4f69de24da8fbbe7a4c20a67bc8f6cc521372101ecf2e")]
    [TestCase(-3, 12, "312fabe2899058422d5d1baa3e547ec7c9b3604dc535f0e8417a3b8451a391a1")]
    [TestCase(3, 12, "578519731fdb24483bd4dc4f505215c897e894ad86b8abf311856b0962e1640e")]
    public void SettingsDigestUsesTheLockedDecimalPreimage(int meters, int chunk, string digest)
    {
        Assert.That(Convert.ToHexStringLower(FoundationProfileBinding.ComputeSettingsDigest(new SpatialSettings(meters, chunk))), Is.EqualTo(digest));
    }

    [Test]
    public void StructuralReaderRejectsLiteralDescendingFieldTags()
    {
        var descendingTags = Convert.FromHexString(
            "4542494e010000004000000002000000"
            + "000102030405060708090a0b0c0d0e0f"
            + "a000000000000000"
            + "4000000000000000"
            + "01000000000000000000000000000000"
            + "0200000009000100800000000000000001000000000000000100000000000000"
            + "0100000009000100900000000000000001000000000000000100000000000000"
            + "01"
            + "000000000000000000000000000000"
            + "00"
            + "000000000000000000000000000000");

        Assert.That(EbinCodec.Read(descendingTags, out var value, out var consumed, out var diagnostic), Is.EqualTo(EbinStatus.InvalidRecord));
        Assert.That(value, Is.Null);
        Assert.That(consumed, Is.Zero);
        Assert.That(diagnostic.Category, Is.EqualTo("field-tag-order"));
    }

    [Test]
    public void StructuralRecordsEnforceNestingFieldsAndUnknownRequiredKinds()
    {
        var id = new Guid("01234567-89ab-4cde-8f01-23456789abcd");
        var record = new EbinRecord(id, 1, 0, []);

        for (var depth = 2; depth <= 64; depth++)
        {
            record = new EbinRecord(id, 1, 0, [EbinPrimitives.Record(1, record)]);
        }

        var bytes = EbinPrimitives.Encode(record);
        Assert.That(EbinCodec.Read(bytes, out var value, out var consumed, out _), Is.EqualTo(EbinStatus.Complete));
        Assert.That(value, Is.Not.Null);
        Assert.That(consumed, Is.EqualTo(bytes.Length));
        var overflow = new EbinRecord(id, 1, 0, [EbinPrimitives.Record(1, record)]);
        var destination = Sentinel(16);
        Assert.That(EbinCodec.Write(overflow, destination, out var written, out var diagnostic), Is.EqualTo(EbinStatus.InvalidRecord));
        Assert.That(diagnostic.Category, Is.EqualTo("nesting-depth-budget"));
        Assert.That(written, Is.Zero);
        Assert.That(destination, Is.All.EqualTo(0xA5));
        var fields = Enumerable.Range(1, 64).Select(static tag => EbinPrimitives.Bytes(checked((uint)tag), [])).ToArray();
        var maximumFields = new EbinRecord(id, 1, 0, fields);
        Assert.That(EbinCodec.Read(EbinPrimitives.Encode(maximumFields), out value, out consumed, out _), Is.EqualTo(EbinStatus.Complete));
        Assert.That(value?.Fields.Length, Is.EqualTo(64));
        Assert.That(EbinCodec.Write(new EbinRecord(id, 1, 0, [.. fields, EbinPrimitives.Bytes(65, [])]), destination, out written, out diagnostic), Is.EqualTo(EbinStatus.InvalidRecord));
        Assert.That(written, Is.Zero);
        Assert.That(diagnostic.Category, Is.EqualTo("field-count-budget"));
        var unknown = new EbinRecord(id, 1, 0, [new EbinField(1, 65000, 1, 2, [0x12, 0x34])]);
        Assert.That(EbinCodec.Write(unknown, destination, out written, out diagnostic), Is.EqualTo(EbinStatus.UnsupportedSchema));
        Assert.That(written, Is.Zero);
        Assert.That(destination, Is.All.EqualTo(0xA5));
        Assert.That(EbinCodec.Read(EbinPrimitives.Encode(unknown), out value, out consumed, out diagnostic), Is.EqualTo(EbinStatus.UnsupportedSchema));
        Assert.That(value, Is.Null);
        Assert.That(consumed, Is.Zero);
        Assert.That(diagnostic.Category, Is.EqualTo("unsupported-required-kind"));
        var shortArray = new byte[64];
        BinaryPrimitives.WriteUInt64LittleEndian(shortArray, 1);
        BinaryPrimitives.WriteUInt64LittleEndian(shortArray.AsSpan(8), 32);
        BinaryPrimitives.WriteUInt64LittleEndian(shortArray.AsSpan(16), 64);
        ProfileBytes().AsSpan(0, 32).CopyTo(shortArray.AsSpan(32));
        Assert.That(EbinCodec.ReadPayload(24, 1, shortArray.Length, shortArray, out var payload, out consumed, out diagnostic), Is.EqualTo(EbinStatus.InvalidRecord));
        Assert.That(payload, Is.Null);
        Assert.That(consumed, Is.Zero);
        Assert.That(diagnostic.Category, Is.EqualTo("recordarray-nested-total"));
    }

    [Test]
    public void StructuralWriterMatchesLiteralEbinRecords()
    {
        var schemaId = new Guid("00010203-0405-0607-0809-0a0b0c0d0e0f");
        var booleanRecord = new EbinRecord(schemaId, 1, 0, [EbinPrimitives.Boolean(1, true)]);
        var expectedBoolean = Convert.FromHexString(
            "4542494e010000004000000001000000"
            + "000102030405060708090a0b0c0d0e0f"
            + "7000000000000000"
            + "2000000000000000"
            + "01000000000000000000000000000000"
            + "0100000009000100600000000000000001000000000000000100000000000000"
            + "01"
            + "000000000000000000000000000000");
        var booleanDestination = new byte[expectedBoolean.Length];

        Assert.That(EbinCodec.Write(booleanRecord, booleanDestination, out var written, out var diagnostic), Is.EqualTo(EbinStatus.Complete), diagnostic.ToString());
        Assert.That(written, Is.EqualTo(expectedBoolean.Length));
        Assert.That(booleanDestination, Is.EqualTo(expectedBoolean));
        var decodedBoolean = InputRecord(expectedBoolean);
        Assert.That(decodedBoolean.Fields, Has.Length.EqualTo(1));
        Assert.That(decodedBoolean.Fields[0].Tag, Is.EqualTo(1));
        Assert.That(decodedBoolean.Fields[0].Kind, Is.EqualTo(9));
        Assert.That(decodedBoolean.Fields[0].Flags, Is.EqualTo(1));
        Assert.That(decodedBoolean.Fields[0].Payload.ToArray(), Is.EqualTo(new byte[] { 1 }));

        var emptyRecord = new EbinRecord(schemaId, 1, 0, []);
        var expectedEmpty = Convert.FromHexString(
            "4542494e010000004000000000000000"
            + "000102030405060708090a0b0c0d0e0f"
            + "4000000000000000"
            + "0000000000000000"
            + "01000000000000000000000000000000");
        var emptyDestination = new byte[expectedEmpty.Length];

        Assert.That(EbinCodec.Write(emptyRecord, emptyDestination, out written, out diagnostic), Is.EqualTo(EbinStatus.Complete), diagnostic.ToString());
        Assert.That(written, Is.EqualTo(expectedEmpty.Length));
        Assert.That(emptyDestination, Is.EqualTo(expectedEmpty));
        Assert.That(InputRecord(expectedEmpty).Fields, Is.Empty);
    }

    [Test]
    public void StructuralWriterRejectsInvalidFlagsBeforeDestinationCapacity()
    {
        var schemaId = new Guid("00010203-0405-0607-0809-0a0b0c0d0e0f");
        var invalidRecord = new EbinRecord(schemaId, 1, 0, [new EbinField(1, 9, 2, 1, [1])]);
        var destination = Sentinel(1);
        Assert.That(EbinCodec.Write(invalidRecord, destination, out var written, out var diagnostic), Is.EqualTo(EbinStatus.InvalidRecord));
        Assert.That(written, Is.Zero);
        Assert.That(diagnostic.Category, Is.EqualTo("field-flags"));
        Assert.That(destination, Is.All.EqualTo(0xA5));
    }

    [Test]
    public void StructuralWriterRetainsPresentEmptyBytes()
    {
        var record = new EbinRecord(
            new Guid("00010203-0405-0607-0809-0a0b0c0d0e0f"),
            1,
            0,
            [new EbinField(1, 13, 0, 0, [])]);
        var expected = Convert.FromHexString(
            "4542494e010000004000000001000000"
            + "000102030405060708090a0b0c0d0e0f"
            + "6000000000000000"
            + "2000000000000000"
            + "01000000000000000000000000000000"
            + "010000000d000000600000000000000000000000000000000000000000000000");
        var destination = new byte[expected.Length];

        Assert.That(EbinCodec.Write(record, destination, out var written, out var diagnostic), Is.EqualTo(EbinStatus.Complete), diagnostic.ToString());
        Assert.That(written, Is.EqualTo(expected.Length));
        Assert.That(destination, Is.EqualTo(expected));
        var decoded = InputRecord(destination);
        Assert.That(decoded.Fields, Has.Length.EqualTo(1));
        Assert.That(decoded.Fields[0].Flags, Is.Zero);
        Assert.That(decoded.Fields[0].Count, Is.Zero);
        Assert.That(decoded.Fields[0].Payload.Length, Is.Zero);
    }

    [Test]
    public void VariablePayloadKindsRoundTripTheirDeclaredBytes()
    {
        var nestedRecord = new EbinRecord(new Guid("01234567-89ab-4cde-8f01-23456789abcd"), 1, 0, []);
        var nested = EbinPrimitives.Encode(nestedRecord);
        var recordArray = new byte[32 + nested.Length];
        Assert.That(EbinPrimitives.WriteRecordArray([nestedRecord], recordArray, out var arrayLength, out var arrayDiagnostic), Is.EqualTo(EbinStatus.Complete), arrayDiagnostic.ToString());
        recordArray = recordArray.AsSpan(0, arrayLength).ToArray();
        var cases = new (ushort Kind, ulong Count, byte[] Bytes)[] {
            (11, 1, new Guid("00112233-4455-6677-8899-aabbccddeeff").ToByteArray(bigEndian: true)),
            (12, 7, "terrafx"u8.ToArray()),
            (13, 3, [0x12, 0x34, 0x56]),
            (22, 1, nested),
            (24, 1, recordArray),
        };

        foreach (var (kind, count, bytes) in cases)
        {
            Assert.That(EbinCodec.ReadPayload(kind, count, bytes.Length, bytes, out var value, out var consumed, out var diagnostic), Is.EqualTo(EbinStatus.Complete), diagnostic.ToString());
            Assert.That(value, Is.EqualTo(bytes));
            Assert.That(consumed, Is.EqualTo(bytes.Length));
            var destination = Sentinel(bytes.Length);
            Assert.That(EbinCodec.WritePayload(kind, count, bytes, destination, out var written, out diagnostic), Is.EqualTo(EbinStatus.Complete), diagnostic.ToString());
            Assert.That(written, Is.EqualTo(bytes.Length));
            Assert.That(destination, Is.EqualTo(bytes));
        }
    }

    [TestCase("registry/manifest")]
    [TestCase("registry/schema")]
    [TestCase("kernel/manifest")]
    [TestCase("foundation/frame2")]
    public void WrongKnownNestedSchemasAreDataErrorsRatherThanContextMisuse(string schema)
    {
        var root = InputRecord(schema switch {
            "registry/manifest" or "registry/schema" => FoundationCatalogue.RegistryBytes.ToArray(),
            "kernel/manifest" => FoundationCatalogue.KernelBytes.ToArray(),
            "foundation/frame2" => Frame2Bytes(),
            _ => throw new AssertionException("Unknown nested schema test."),
        });

        if (schema == "registry/schema")
        {
            var array = root.GetField(1).Payload;
            var start = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(array[8..]));
            var end = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(array[16..]));
            root = InputRecord(array[start..end].ToArray());
        }

        var tag = schema == "registry/manifest" ? 1U : schema == "foundation/frame2" ? 2U : 4U;
        var context = schema == "foundation/frame2" ? _context : new FoundationCodecContext(_profiles);

        foreach (var known in new[] { true, false })
        {
            var nested = known ? InputRecord(Frame2Bytes())
                : new EbinRecord(new Guid("01234567-89ab-4cde-8f01-23456789abcd"), 1, 0, []);
            var field = schema == "foundation/frame2" ? EbinPrimitives.Record(tag, nested) : EbinPrimitives.RecordArray(tag, [nested]);
            var input = new EbinRecord(root.SchemaId, root.Major, root.Minor, [.. root.Fields.Where(current => current.Tag != tag), field]);
            var bytes = EbinPrimitives.Encode(input);
            var expected = known ? EbinStatus.InvalidRecord : EbinStatus.UnsupportedSchema;

            foreach (var length in new[] { bytes.Length - 1, bytes.Length })
            {
                Assert.That(FoundationCodec.Read(bytes.AsSpan(0, length), context, out var value, out var consumed, out var diagnostic), Is.EqualTo(expected));
                Assert.That(value, Is.Null);
                Assert.That(consumed, Is.Zero);
                Assert.That(diagnostic.Category, Is.EqualTo(known ? "nested-schema-mismatch" : "unsupported-schema-id"));
            }

            foreach (var capacity in new[] { 0, bytes.Length + 1 })
            {
                var destination = Sentinel(capacity);
                Assert.That(FoundationCodec.Write(input, context, destination, out var written, out var diagnostic), Is.EqualTo(expected));
                Assert.That(written, Is.Zero);
                Assert.That(destination, Is.All.EqualTo(0xA5));
                Assert.That(diagnostic.Category, Is.EqualTo(known ? "nested-schema-mismatch" : "unsupported-schema-id"));
            }
        }
    }

    private byte[] Frame2Bytes() => EbinPrimitives.Encode(Frame2Record());

    private EbinRecord Frame2Record()
    {
        var frame = Frame2Value();
        var binding = new FoundationPersistenceBinding(s_worldGuid, _defaultProfile, 1, 1);
        return FoundationFrameCodec.Capture(frame, binding, 1, 1);
    }

    private SpatialFrame2 Frame2Value() => new SpatialFrame2(
            1,
            _defaultProfile.Id,
            _defaultProfile.Settings,
            new SpatialPosition2(new ChunkIndex2(-1, 2), new Vector2(0.0f, BitConverter.Int32BitsToSingle(1))),
            1,
            new Vector2(-4096.0f, BitConverter.Int32BitsToSingle(int.MinValue)),
            new Vector2(4096.0f, 0.0f));

    private byte[] Frame3Bytes() => EbinPrimitives.Encode(Frame3Record());

    private EbinRecord Frame3Record()
    {
        var frame = new SpatialFrame3(
            1,
            _defaultProfile.Id,
            _defaultProfile.Settings,
            new SpatialPosition3(
                new ChunkIndex3(int.MinValue, 0, int.MaxValue),
                new Vector3(0.0f, 0.5f, BitConverter.Int32BitsToSingle(0x457FFFFF))),
            long.MaxValue,
            new Vector3(-4096.0f),
            new Vector3(4096.0f));
        var binding = new FoundationPersistenceBinding(s_worldGuid, _defaultProfile, 1, 1);
        return FoundationFrameCodec.Capture(frame, binding, 1, 1);
    }

    private EbinRecord OptionalProfileRecord()
    {
        var profile = _defaultProfile.CreateRecord();
        return new EbinRecord(profile.SchemaId, profile.Major, profile.Minor, [
            .. profile.Fields,
            ExtensionSet(ExtensionRecord(-7, [0x12, 0x34])),
        ]);
    }

    private byte[] ProfileBytes() => EbinPrimitives.Encode(_defaultProfile.CreateRecord());
}
