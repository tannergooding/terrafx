// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using TerraFX.Animation;
using TerraFX.Assets;
using TerraFX.Graphics;
using TerraFX.Numerics;
using TerraFX.Rendering;
using TerraFX.Scenes;

namespace TerraFX.Content.Tools.Tests;

internal static class NativeMeshTestData
{
    internal const uint MaterialId = 101;
    internal const uint MeshId = 100;
    internal const uint ParticleAtlasId = 301;
    internal const uint ParticleEmitterId = 300;
    internal const uint ParticleMaterialId = 303;
    internal const uint ParticlePageId = 302;
    internal const uint SkeletonId = 102;
    internal const uint TextureId = 103;

    private static readonly EbinReadLimits s_authorLimits = new(268_435_456, 64, 64, 2_097_152, 1_048_576, 2_097_152, 4_194_304, 4_194_304);
    private static readonly byte[] s_profileRecord = Convert.FromHexString("4542494E0100000040000000090000003B9760CE468A8AABBB89F38DB9E3BC0020020000000000002001000000000000020000000000000000000000000000000100000003000100600100000000000004000000000000000100000000000000020000000300010070010000000000000400000000000000010000000000000003000000030001008001000000000000040000000000000001000000000000000500000001000100900100000000000001000000000000000100000000000000060000000D000100A00100000000000020000000000000002000000000000000070000000D000100C00100000000000020000000000000002000000000000000080000000D000100E00100000000000020000000000000002000000000000000090000000B0001000002000000000000100000000000000001000000000000000A000000070001001002000000000000040000000000000001000000000000000100000000000000000000000000000002000000000000000000000000000000020000000000000000000000000000000C0000000000000000000000000000009EF6534033621925D39355058D26782EB3B1A41A20690EFD829EF6D3EFE0EB1CF21833596ED8BEDEAA7E366505A2ADE8AD349AB35F7BF0554AD2D8CE5AEDA43F494F69B9559DE1C609B2C51A11454FCDC3A0BE8B108FDC91F242D89B38D18105C2DDC5D89EE68676BDEFDFFD36A56D8900000000000000000000000000000000");

    internal static Guid ParticleFrameId => new("80000000-0000-4000-8000-000000000001");

    internal static string[] CookArguments(string directory, params string[] dependencies)
        => [
            "--source", Path.Combine(directory, "mesh.ebin"),
            "--profile-record", Path.Combine(directory, "profile.ebin"),
            "--mesh-id", "100",
            .. dependencies.SelectMany(static value => new[] { "--asset", value }),
            "--output", Path.Combine(directory, "mesh.epkg"),
        ];

    internal static byte[] CreateAssetReference(uint assetId, string name, int major = 1)
        => Record("native/asset-reference", 1, [
            UInt32(1, assetId),
            GuidField(2, name),
            UInt32(3, checked((uint)major)),
        ]);

    internal static byte[] CreateBounds(float maximumX = 2.0f, float radius = 3.0f)
        => Record("native/bounds3", 1, [
            new Field(1, 15, ContentPackageWriter.EncodeVector3(new Vector3(-1, -1, 0))),
            new Field(2, 15, ContentPackageWriter.EncodeVector3(new Vector3(maximumX, 1, 0))),
            new Field(3, 15, ContentPackageWriter.EncodeVector3(Vector3.Zero)),
            new Field(4, 10, ContentPackageWriter.EncodeSingle(radius)),
        ]);

    internal static string CreateDirectory()
    {
        var path = Path.Combine(FindRepositoryRoot(), "artifacts", "tst", string.Concat("native-mesh-", Guid.NewGuid().ToString("N")));
        _ = Directory.CreateDirectory(path);
        return path;
    }

    internal static byte[] CreateGroups(int count = 1, uint materialSlot = 0, uint indexCount = 3)
    {
        var records = new byte[count][];

        for (var index = 0; index < records.Length; index++)
        {
            records[index] = Record("native/mesh-group", 1, [
                UInt32(1, checked((uint)index * indexCount)),
                UInt32(2, indexCount),
                UInt32(3, materialSlot),
            ]);
        }

        return ContentPackageWriter.EncodeRecordArray(records);
    }

    internal static byte[] CreateIndices(uint[] indices, bool wide)
        => Record(wide ? "native/mesh-indices32" : "native/mesh-indices16", 1, [
            wide
                ? new Field(1, 23, ContentPackageWriter.EncodeFixedArray(indices), checked((ulong)indices.Length))
                : new Field(1, 23, ContentPackageWriter.EncodeFixedArray(indices.Select(static value => checked((ushort)value)).ToArray()), checked((ulong)indices.Length)),
        ]);

    internal static byte[] CreateLevel(uint[]? indices = null, uint materialSlot = 0, int groupCount = 1)
    {
        indices ??= groupCount == 1 ? [0, 1, 2] : RepeatTriangle(groupCount);
        return Record("native/mesh-lod-level", 1, [
            new Field(1, 22, CreateIndices(indices, wide: true)),
            new Field(2, 24, CreateGroups(groupCount, materialSlot, checked((uint)indices.Length / (uint)groupCount)), checked((ulong)groupCount)),
        ]);
    }

    internal static byte[] CreateMaterial(int major = 1, uint? textureId = null, float pickAlphaCutoff = 0.25f)
    {
        var parameterFields = new List<Field> {
            new(1, 16, EncodeVector4(Vector4.One)),
            new(2, 10, ContentPackageWriter.EncodeSingle(0)),
            new(3, 10, ContentPackageWriter.EncodeSingle(1)),
            new(4, 10, ContentPackageWriter.EncodeSingle(1)),
            new(5, 15, ContentPackageWriter.EncodeVector3(Vector3.Zero)),
        };

        if (textureId is { } id)
        {
            var sampler = Record("native/sampler", 1, [
                UInt8(1, 1), UInt8(2, 1), UInt8(3, 0), UInt8(4, 0), UInt8(5, 0),
            ]);
            var binding = Record("native/texture-binding", 1, [
                new Field(1, 22, CreateAssetReference(id, "native/texture")),
                new Field(2, 22, sampler),
                UInt8(3, 0),
            ]);
            parameterFields.Add(new Field(6, 22, binding, Flags: 0));
        }

        var capabilities = new byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(capabilities, 0x191);
        var fields = new List<Field> {
            GuidField(1, "material/profile/pbr-metallic-roughness/v1"),
            GuidField(2, "material/shader/pbr-metallic-roughness/v1"),
            GuidField(3, "material/pass/pbr/v1"),
            GuidField(4, "material/layout/pbr/v1"),
            new(5, 22, Record("native/material-pbr-parameters", 1, parameterFields)),
            UInt8(6, 0),
            new(7, 10, ContentPackageWriter.EncodeSingle(0.5f)),
            new(8, 9, [0]),
            new(9, 9, [1]),
            new(10, 9, [1]),
            UInt8(11, 1),
            UInt8(12, 0),
            new(13, 4, capabilities),
        };

        if (major == 2)
        {
            fields.Add(new Field(14, 10, ContentPackageWriter.EncodeSingle(pickAlphaCutoff)));
        }

        return Record("native/material", major, fields);
    }

    internal static byte[] CreateMesh(
        int levelCount = 2,
        int materialMajor = 1,
        bool includeUv = false,
        bool skinned = false,
        ushort jointIndex = 0,
        int groupCount = 1)
    {
        Vector3[] positions = [new(-1, -1, 0), new(1, -1, 0), new(1, 1, 0), new(-1, 1, 0), new(2, 0, 0)];
        var streams = new List<byte[]> {
            CreateVector3Stream("native/mesh-position", positions),
            CreateVector3Stream("native/mesh-normal", [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ]),
        };

        if (includeUv)
        {
            Vector2[] uv = [Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY, Vector2.Zero];
            streams.Add(Record("native/mesh-uv", 1, [
                UInt8(1, 0),
                UInt32(2, 8),
                new Field(3, 23, ContentPackageWriter.EncodeFixedArray(uv), 5),
            ]));
        }

        if (skinned)
        {
            var joints = new ushort[20];
            joints[0] = jointIndex;
            streams.Add(Record("native/mesh-joints", 1, [
                UInt32(1, 8),
                new Field(2, 23, ContentPackageWriter.EncodeFixedArray(joints), 20),
            ]));
            Vector4[] weights = [new(1, 0, 0, 0), new(1, 0, 0, 0), new(1, 0, 0, 0), new(1, 0, 0, 0), new(1, 0, 0, 0)];
            streams.Add(Record("native/mesh-weights", 1, [
                UInt32(1, 16),
                new Field(2, 23, EncodeVector4Array(weights), 5),
            ]));
        }

        var additional = new byte[checked(levelCount - 1)][];
        var thresholds = new float[additional.Length];

        for (var index = 0; index < additional.Length; index++)
        {
            additional[index] = CreateLevel(groupCount: groupCount);
            thresholds[index] = 0.625f / (1 << index);
        }

        var finestIndices = groupCount == 1 ? [0, 1, 2, 0, 2, 3] : RepeatTriangle(groupCount);
        var fields = new List<Field> {
            UInt32(1, 5),
            new(2, 24, ContentPackageWriter.EncodeRecordArray([.. streams]), checked((ulong)streams.Count)),
            new(3, 22, CreateIndices(finestIndices, wide: false)),
            new(4, 24, CreateGroups(groupCount, indexCount: groupCount == 1 ? 6u : 3u), checked((ulong)groupCount)),
            new(5, 24, ContentPackageWriter.EncodeRecordArray([CreateAssetReference(MaterialId, "native/material", materialMajor)])),
            new(6, 22, CreateBounds()),
        };

        if (skinned)
        {
            fields.Add(new Field(7, 22, CreateAssetReference(SkeletonId, "native/skeleton"), Flags: 0));
        }

        fields.Add(new Field(8, 24, ContentPackageWriter.EncodeRecordArray(additional), checked((ulong)additional.Length)));
        fields.Add(new Field(9, 23, EncodeThresholds(thresholds), checked((ulong)thresholds.Length)));
        return Record("native/mesh", 2, fields);
    }

    internal static byte[] CreateParticleAtlasPageTexture(byte pixelValue = 255)
    {
        var pixels = new byte[16 * 16 * 4];
        Array.Fill(pixels, pixelValue);
        var mip = Record("native/texture-mip", 1, [
            UInt32(1, 0),
            UInt32(2, 16),
            UInt32(3, 16),
            new Field(4, 13, pixels, checked((ulong)pixels.Length)),
        ]);
        return Record("native/texture", 1, [
            UInt32(1, 16),
            UInt32(2, 16),
            UInt8(3, 0),
            UInt8(4, 0),
            UInt8(5, 1),
            UInt8(6, 0),
            UInt8(7, 0),
            new Field(8, 24, ContentPackageWriter.EncodeRecordArray([mip])),
        ]);
    }

    internal static byte[] CreateParticleAtlasRecord()
    {
        var frame = Record("native/atlas-frame", 1, [
            GuidField(1, ParticleFrameId),
            UInt32(2, 0),
            UInt32(3, 2),
            UInt32(4, 2),
            UInt32(5, 1),
            UInt32(6, 1),
            UInt8(7, 2),
        ]);
        return Record("native/atlas", 1, [
            new Field(1, 24, ContentPackageWriter.EncodeRecordArray([
                CreateAssetReference(ParticlePageId, "native/texture"),
            ])),
            new Field(2, 24, ContentPackageWriter.EncodeRecordArray([frame])),
        ]);
    }

    internal static byte[] CreateParticleEmitterRecord(
        byte space = 0,
        uint spawnPerTick = 2,
        long lifetimeTicks = 10_000_000,
        Vector3? velocityMinimum = null,
        Vector3? velocityMaximum = null,
        int? materialId = null,
        int materialMajor = 1,
        uint? atlasId = null,
        Guid? frameId = null,
        ulong stream = 54,
        float colorStartAge = 0.0f,
        float colorEndAge = 1.0f,
        Vector4? colorStart = null,
        Vector4? colorEnd = null,
        float sizeStartAge = 0.0f,
        float sizeEndAge = 1.0f,
        Vector2? sizeStart = null,
        Vector2? sizeEnd = null)
    {
        var fields = new List<Field> {
            UInt8(1, space),
            UInt32(2, spawnPerTick),
            new(3, 8, EncodeInt64(lifetimeTicks)),
            new(4, 15, ContentPackageWriter.EncodeVector3(velocityMinimum ?? new Vector3(-1, -1, 0))),
            new(5, 15, ContentPackageWriter.EncodeVector3(velocityMaximum ?? new Vector3(1, 1, 0))),
            new(6, 24, ContentPackageWriter.EncodeRecordArray([
                CreateParticleColorKey(colorStartAge, colorStart ?? Vector4.One),
                CreateParticleColorKey(colorEndAge, colorEnd ?? new Vector4(1, 0, 0, 0)),
            ]), 2),
            new(7, 24, ContentPackageWriter.EncodeRecordArray([
                CreateParticleSizeKey(sizeStartAge, sizeStart ?? Vector2.One),
                CreateParticleSizeKey(sizeEndAge, sizeEnd ?? new Vector2(2, 2)),
            ]), 2),
            new(8, 22, CreateAssetReference(
                atlasId ?? ParticleAtlasId,
                "native/atlas")),
            GuidField(9, frameId ?? ParticleFrameId),
            UInt64(11, 42),
            UInt64(12, stream),
            Int32(13, 0),
            Int32(14, 0),
            UInt64(15, ulong.MaxValue),
        };

        if (materialId is int selectedMaterialId)
        {
            fields.Add(new Field(
                10,
                22,
                CreateAssetReference(
                    checked((uint)selectedMaterialId),
                    "native/material",
                    materialMajor),
                Flags: 0));
        }

        return Record("native/cosmetic-particle-emitter", 1, fields);
    }

    internal static byte[] CreateParticleMaterialRecord(int major = 1, uint textureId = ParticlePageId, float pickAlphaCutoff = 0.5f)
    {
        var sampler = Record("native/sampler", 1, [
            UInt8(1, 1), UInt8(2, 1), UInt8(3, 0), UInt8(4, 0), UInt8(5, 0),
        ]);
        var binding = Record("native/texture-binding", 1, [
            new Field(1, 22, CreateAssetReference(textureId, "native/texture")),
            new Field(2, 22, sampler),
            UInt8(3, 0),
        ]);
        var parameters = Record("native/material-sprite-parameters", 1, [
            new Field(1, 16, EncodeVector4(Vector4.One)),
            new Field(2, 22, binding, Flags: 0),
        ]);
        var capabilities = new byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(capabilities, 0x009);
        var fields = new List<Field> {
            GuidField(1, "material/profile/sprite-premul/v1"),
            GuidField(2, "material/shader/sprite-unlit/v1"),
            GuidField(3, "material/pass/sprite/v1"),
            GuidField(4, "material/layout/sprite/v1"),
            new(5, 22, parameters),
            UInt8(6, 2),
            new(7, 10, ContentPackageWriter.EncodeSingle(0.5f)),
            new(8, 9, [0]),
            new(9, 9, [0]),
            new(10, 9, [0]),
            UInt8(11, 0),
            UInt8(12, 1),
            new(13, 4, capabilities),
        };

        if (major == 2)
        {
            fields.Add(new Field(14, 10, ContentPackageWriter.EncodeSingle(pickAlphaCutoff)));
        }

        return Record("native/material", major, fields);
    }

    internal static AssetStoreRegistration CreateRegistration(bool includeLod = true, bool includeParticles = false)
    {
        var schemas = new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
            .. RenderingSchemaCatalogue.Schemas,
            .. RenderingSchemaCatalogue.M05Schemas,
            .. AnimationSchemaCatalogue.Schemas,
        ]);
        var builder = new AssetStoreRegistrationBuilder(schemas);
        _ = Renderer.RegisterAssetCodecs(builder);
        var features = (includeLod ? RendererCpuFeatures.MeshLod : RendererCpuFeatures.Static)
            | (includeParticles ? RendererCpuFeatures.CosmeticParticles : RendererCpuFeatures.Static);
        _ = Renderer.RegisterM05AssetCodecs(builder, features);

        _ = AnimationSupport.RegisterAssetCodecs(builder);
        return builder.Build();
    }

    internal static byte[] CreateSkeleton()
    {
        var local = Record("scene/local-transform", 1, [
            new Field(1, 15, ContentPackageWriter.EncodeVector3(Vector3.Zero)),
            new Field(2, 17, EncodeVector4(new Vector4(0, 0, 0, 1))),
            new Field(3, 15, ContentPackageWriter.EncodeVector3(Vector3.One)),
        ]);
        var parent = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(parent, -1);
        var jointId = new byte[16];
        _ = new Guid("70000000-0000-4000-8000-000000000001").TryWriteBytes(jointId, bigEndian: true, out _);
        var inverseBind = new byte[64];

        for (var index = 0; index < 4; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(inverseBind.AsSpan(index * 20), BitConverter.SingleToInt32Bits(1));
        }

        var joint = Record("native/joint", 1, [
            new Field(1, 11, jointId), new Field(2, 7, parent), new Field(3, 22, local), new Field(4, 19, inverseBind),
        ]);
        return Record("native/skeleton", 1, [
            new Field(1, 24, ContentPackageWriter.EncodeRecordArray([joint])),
        ]);
    }

    internal static byte[] CreateTexture()
    {
        var mip = Record("native/texture-mip", 1, [
            UInt32(1, 0), UInt32(2, 1), UInt32(3, 1), new Field(4, 13, [255, 255, 255, 255], 4),
        ]);
        return Record("native/texture", 1, [
            UInt32(1, 1), UInt32(2, 1), UInt8(3, 0), UInt8(4, 1), UInt8(5, 0), UInt8(6, 0), UInt8(7, 0),
            new Field(8, 24, ContentPackageWriter.EncodeRecordArray([mip])),
        ]);
    }

    internal static byte[] EncodeThresholds(float[] values)
    {
        var payload = new byte[checked(32 + (values.Length * sizeof(float)))];
        var status = EbinPayloadWriter.WriteFixedArray(values, s_authorLimits, payload, out var written, out var diagnostic);

        if ((status is not EbinWriteStatus.Complete) || (written != payload.Length))
        {
            throw new InvalidDataException($"The authored thresholds could not be encoded: {diagnostic?.Category}.");
        }

        return payload;
    }

    internal static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TerraFX.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("The native Mesh test repository root could not be located.");
    }

    internal static byte[] GetFieldPayload(byte[] record, uint tag)
    {
        var action = new FieldReader(tag);
        var status = EbinStructuralReader.Read(record, s_authorLimits, EbinReadPolicy.Canonical, ref action, out _, out _);

        if ((status is not EbinReadStatus.Complete) || (action.Payload is null))
        {
            throw new InvalidDataException("The authored fixture field is absent.");
        }

        return action.Payload;
    }

    internal static byte[] GetProfileRecord() => (byte[])s_profileRecord.Clone();

    internal static async Task<AssetStore> OpenStoreAsync(string path, AssetStoreRegistration? registration = null)
    {
        var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        return await AssetStore.CreateAsync(new AssetStoreOptions(
            path,
            SHA256.HashData(bytes),
            registration ?? CreateRegistration())).ConfigureAwait(false);
    }

    internal static uint[] RepeatTriangle(int triangleCount)
    {
        var indices = new uint[checked(triangleCount * 3)];

        for (var index = 0; index < indices.Length; index++)
        {
            indices[index] = checked((uint)(index % 3));
        }

        return indices;
    }

    internal static byte[] Rewrite(
        byte[] record,
        uint tag = 0,
        ContentRecordField? replacement = null,
        bool required = true,
        int? major = null,
        int? minor = null)
    {
        var action = new RecordEditor(tag, replacement, required, major, minor);
        var status = EbinStructuralReader.Read(record, s_authorLimits, EbinReadPolicy.Canonical, ref action, out _, out _);
        return (status is EbinReadStatus.Complete) && (action.Record is { } result)
            ? result
            : throw new InvalidDataException("The authored fixture could not be rewritten.");
    }

    internal static async Task WriteInputsAsync(
        string directory,
        byte[]? mesh = null,
        byte[]? material = null)
    {
        await File.WriteAllBytesAsync(Path.Combine(directory, "profile.ebin"), GetProfileRecord()).ConfigureAwait(false);
        await File.WriteAllBytesAsync(Path.Combine(directory, "mesh.ebin"), mesh ?? CreateMesh()).ConfigureAwait(false);
        await File.WriteAllBytesAsync(Path.Combine(directory, "material.ebin"), material ?? CreateMaterial()).ConfigureAwait(false);
    }

    internal static async Task<string> WritePackageAsync(
        string directory,
        byte[] mesh,
        byte[]? material = null,
        params ContentPackageAsset[] dependencies)
    {
        var registration = CreateRegistration();
        ContentPackageAsset[] assets = [
            new(MeshId, mesh), new(MaterialId, material ?? CreateMaterial()), .. dependencies,
        ];
        var package = ContentPackageWriter.CreateNativeAssetPackage(GetProfileRecord(), assets, registration.Schemas);
        var path = Path.Combine(directory, string.Concat(Guid.NewGuid().ToString("N"), ".epkg"));
        await File.WriteAllBytesAsync(path, package).ConfigureAwait(false);
        return path;
    }

    internal static async Task<string> WriteParticlePackageAsync(
        string directory,
        byte[] emitter,
        params ContentPackageAsset[] dependencies)
    {
        var registration = CreateRegistration(includeParticles: true);
        ContentPackageAsset[] assets = [
            new(ParticleEmitterId, emitter),
            new(ParticleAtlasId, CreateParticleAtlasRecord()),
            new(ParticlePageId, CreateParticleAtlasPageTexture()),
            .. dependencies,
        ];
        var package = ContentPackageWriter.CreateNativeAssetPackage(GetProfileRecord(), assets, registration.Schemas);
        var path = Path.Combine(directory, string.Concat(Guid.NewGuid().ToString("N"), ".epkg"));
        await File.WriteAllBytesAsync(path, package).ConfigureAwait(false);
        return path;
    }

    private static byte[] CreateParticleColorKey(float age, Vector4 color)
        => Record("native/particle-color-key", 1, [
            new Field(1, 10, ContentPackageWriter.EncodeSingle(age)),
            new Field(2, 16, EncodeVector4(color)),
        ]);

    private static byte[] CreateParticleSizeKey(float age, Vector2 size)
        => Record("native/particle-size-key", 1, [
            new Field(1, 10, ContentPackageWriter.EncodeSingle(age)),
            new Field(2, 14, EncodeVector2(size)),
        ]);

    private static byte[] CreateVector3Stream(string name, Vector3[] values)
        => Record(name, 1, [
            UInt32(1, 12),
            new Field(2, 23, ContentPackageWriter.EncodeFixedArray(values), checked((ulong)values.Length)),
        ]);

    private static byte[] EncodeInt32(int value)
    {
        Span<byte> payload = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(payload, value);
        return payload.ToArray();
    }

    private static byte[] EncodeInt64(long value)
    {
        Span<byte> payload = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(payload, value);
        return payload.ToArray();
    }

    private static byte[] EncodeRecord(Guid schemaId, int major, int minor, IReadOnlyCollection<Field> fields)
    {
        var ordered = fields.OrderBy(static field => field.Tag).ToArray();
        var storage = new byte[ordered.Sum(static field => field.Payload.Length)];
        var sources = new EbinFieldSource[ordered.Length];
        var offset = 0;

        for (var index = 0; index < ordered.Length; index++)
        {
            var field = ordered[index];
            field.Payload.CopyTo(storage, offset);
            sources[index] = new EbinFieldSource(field.Tag, field.Kind, field.Flags, field.Count, new EbinPayloadSlice(offset, field.Payload.Length));
            offset += field.Payload.Length;
        }

        var record = new EbinRecordWriteView(schemaId, major, minor, sources, storage);
        var status = EbinStructuralWriter.Measure(in record, s_authorLimits, out var bytes, out var diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            throw new InvalidDataException($"The authored fixture is structurally invalid: {diagnostic?.Category}.");
        }

        var candidate = new byte[bytes];
        var encoded = new byte[bytes];
        status = EbinStructuralWriter.Write(in record, s_authorLimits, candidate, encoded, out _, out diagnostic);
        return status is EbinWriteStatus.Complete
            ? encoded
            : throw new InvalidDataException($"The authored fixture could not be encoded: {diagnostic?.Category}.");
    }

    private static byte[] EncodeVector2(Vector2 value)
    {
        Span<byte> payload = stackalloc byte[2 * sizeof(float)];
        var status = EbinPayloadWriter.Write(value, payload, out var bytesWritten, out var diagnostic);

        return (status is EbinWriteStatus.Complete) && (bytesWritten == payload.Length)
            ? payload.ToArray()
            : throw new InvalidDataException($"The authored Vector2 could not be encoded: {diagnostic?.Category}.");
    }

    private static byte[] EncodeVector4(Vector4 value)
    {
        var payload = new byte[16];
        _ = EbinPayloadWriter.Write(value, payload, out _, out _);
        return payload;
    }

    private static byte[] EncodeVector4Array(Vector4[] values)
    {
        var payload = new byte[checked(32 + (values.Length * 16))];
        _ = EbinPayloadWriter.WriteFixedArray(values, s_authorLimits, payload, out _, out _);
        return payload;
    }

    private static Field GuidField(uint tag, Guid value)
    {
        var payload = new byte[16];
        _ = value.TryWriteBytes(payload, bigEndian: true, out _);
        return new Field(tag, 11, payload);
    }

    private static Field GuidField(uint tag, string name)
    {
        var payload = new byte[16];
        _ = new ContentSchemaIdentityAssignment(name).SchemaId.TryWriteBytes(payload, bigEndian: true, out _);
        return new Field(tag, 11, payload);
    }

    private static Field Int32(uint tag, int value)
        => new(tag, 7, EncodeInt32(value));

    private static byte[] Record(string name, int major, IReadOnlyCollection<Field> fields)
        => EncodeRecord(new ContentSchemaIdentityAssignment(name).SchemaId, major, 0, fields);

    private static Field UInt32(uint tag, uint value)
        => new(tag, 3, ContentPackageWriter.EncodeUInt32(value));

    private static Field UInt64(uint tag, ulong value)
    {
        Span<byte> payload = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(payload, value);
        return new Field(tag, 4, payload.ToArray());
    }

    private static Field UInt8(uint tag, byte value) => new(tag, 1, [value]);

    private readonly record struct Field(uint Tag, ushort Kind, byte[] Payload, ulong Count = 1, ushort Flags = 1);

    private struct FieldReader : IAction<EbinRecordReadView>
    {
        private readonly uint _tag;

        internal FieldReader(uint tag)
        {
            _tag = tag;
            Payload = null;
        }

        internal byte[]? Payload { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
        {
            if (record.TryGetField(_tag, out var field))
            {
                Payload = field.Payload.ToArray();
            }
        }
    }

    private struct RecordEditor : IAction<EbinRecordReadView>
    {
        private readonly int? _major;
        private readonly int? _minor;
        private readonly ContentRecordField? _replacement;
        private readonly bool _required;
        private readonly uint _tag;

        internal RecordEditor(uint tag, ContentRecordField? replacement, bool required, int? major, int? minor)
        {
            _major = major;
            _minor = minor;
            _replacement = replacement;
            _required = required;
            _tag = tag;
            Record = null;
        }

        internal byte[]? Record { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
        {
            var fields = new List<Field>();

            for (var index = 0; index < record.FieldCount; index++)
            {
                var field = record.GetField(index);

                if (field.Tag != _tag)
                {
                    fields.Add(new Field(field.Tag, field.Kind, field.Payload.ToArray(), field.Count, field.IsRequired ? (ushort)1 : (ushort)0));
                }
            }

            if (_replacement is { } replacement)
            {
                fields.Add(new Field(replacement.Tag, replacement.Kind, replacement.Payload, replacement.Count, _required ? (ushort)1 : (ushort)0));
            }

            var header = record.Header;
            Record = EncodeRecord(header.SchemaId, _major ?? header.Major, _minor ?? header.Minor, fields);
        }
    }
}
