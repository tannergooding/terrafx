// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Graphics;
using TerraFX.Rendering;

namespace TerraFX.Runtime.Tests.Scenes;

internal sealed partial class SceneAssetStoreTests
{
    private static SceneHierarchyCodecTests.FixtureBuilder.FixtureField CreateRenderBooleanField(uint tag, bool value)
        => new(tag, 9, 1, 1, [value ? (byte)1 : (byte)0]);

    private static byte[] CreateRenderMaterialAssetRecord(
        bool isSprite,
        byte[] parameters,
        byte alphaMode,
        bool doubleSided,
        bool depthTest,
        bool depthWrite,
        byte cullMode,
        byte outputAlpha,
        ulong requiredCapabilities)
    {
        var profileKey = isSprite
            ? "material/profile/sprite-premul/v1"
            : "material/profile/pbr-metallic-roughness/v1";
        var shaderKey = isSprite
            ? "material/shader/sprite-unlit/v1"
            : "material/shader/pbr-metallic-roughness/v1";
        var passKey = isSprite ? "material/pass/sprite/v1" : "material/pass/pbr/v1";
        var layoutKey = isSprite ? "material/layout/sprite/v1" : "material/layout/pbr/v1";
        var requiredCapabilitiesPayload = new byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(requiredCapabilitiesPayload, requiredCapabilities);
        return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/material").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(
                1,
                new ContentSchemaIdentityAssignment(profileKey).SchemaId),
            SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(
                2,
                new ContentSchemaIdentityAssignment(shaderKey).SchemaId),
            SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(
                3,
                new ContentSchemaIdentityAssignment(passKey).SchemaId),
            SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(
                4,
                new ContentSchemaIdentityAssignment(layoutKey).SchemaId),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(5, parameters),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(6, alphaMode),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(7, 10, 0.5f),
            CreateRenderBooleanField(8, doubleSided),
            CreateRenderBooleanField(9, depthTest),
            CreateRenderBooleanField(10, depthWrite),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(11, cullMode),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(12, outputAlpha),
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(13, 4, 1, 1, requiredCapabilitiesPayload));
    }

    private static byte[] CreateRenderMaterialPbrParametersRecord(
        int? baseTextureAssetId = null,
        int? packedTextureAssetId = null,
        int? normalTextureAssetId = null,
        int? emissiveTextureAssetId = null,
        float roughness = 1.0f,
        byte baseTextureMip = 0)
    {
        var fields = new System.Collections.Generic.List<SceneHierarchyCodecTests.FixtureBuilder.FixtureField> {
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(1, 16, 1.0f, 1.0f, 1.0f, 1.0f),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(2, 10, 0.0f),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(3, 10, roughness),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(4, 10, 1.0f),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(5, 15, 0.0f, 0.0f, 0.0f),
        };

        if (baseTextureAssetId is int baseTexture)
        {
            fields.Add(SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(
                6,
                CreateRenderMaterialTextureBindingRecord(baseTexture, mip: baseTextureMip),
                isRequired: false));
        }

        if (packedTextureAssetId is int packedTexture)
        {
            fields.Add(SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(
                7,
                CreateRenderMaterialTextureBindingRecord(packedTexture),
                isRequired: false));
        }

        if (normalTextureAssetId is int normalTexture)
        {
            fields.Add(SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(
                8,
                CreateRenderMaterialTextureBindingRecord(normalTexture),
                isRequired: false));
        }

        if (emissiveTextureAssetId is int emissiveTexture)
        {
            fields.Add(SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(
                9,
                CreateRenderMaterialTextureBindingRecord(emissiveTexture),
                isRequired: false));
        }

        return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/material-pbr-parameters").SchemaId,
            1,
            0,
            [.. fields]);
    }

    private static byte[] CreateRenderMaterialSpriteParametersRecord(int? textureAssetId = null, byte uvSet = 0)
    {
        var fields = new System.Collections.Generic.List<SceneHierarchyCodecTests.FixtureBuilder.FixtureField> {
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(1, 16, 1.0f, 1.0f, 1.0f, 1.0f),
        };

        if (textureAssetId is int texture)
        {
            fields.Add(SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(
                2,
                CreateRenderMaterialTextureBindingRecord(texture, uvSet: uvSet),
                isRequired: false));
        }

        return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/material-sprite-parameters").SchemaId,
            1,
            0,
            [.. fields]);
    }

    private static byte[] CreateRenderMaterialTextureBindingRecord(int textureAssetId, byte mip = 0, byte uvSet = 0)
    {
        var textureReference = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(1, checked((uint)textureAssetId)),
            SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(
                2,
                new ContentSchemaIdentityAssignment("native/texture").SchemaId),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(3, 1));
        var sampler = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/sampler").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(1, 1),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(2, 1),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(3, mip),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(4, 0),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(5, 0));
        return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/texture-binding").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(1, textureReference),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(2, sampler),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(3, uvSet));
    }

    [Test]
    public async Task MaterialAssetCodecLoadsSpriteAndPbrProfilesWithPackageTextures()
    {
        var schemas = new CompiledContentSchemaRegistry([
            .. SceneAssetReaderTests.Fixtures.CreateRegistry().Schemas,
            .. RenderingSchemaCatalogue.Schemas,
        ]);
        var registrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = Renderer.RegisterAssetCodecs(registrationBuilder);
        var registration = registrationBuilder.Build();
        var package = Package.CreatePackage(
            new Package.Entry(
                1,
                Package.CreateEnvelope(
                    1,
                    CreateRenderTextureAssetRecord(
                        1,
                        1,
                        RenderTextureColorSpace.Linear,
                        RenderTextureAlphaMode.Premultiplied,
                        RenderTextureSemantic.Color,
                        RenderTextureMipPolicy.BaseOnly,
                        CreateRenderTextureMipRecord(0, 1, 1, [11, 12, 13, 255])))),
            new Package.Entry(
                2,
                Package.CreateEnvelope(
                    2,
                    CreateRenderTextureAssetRecord(
                        1,
                        1,
                        RenderTextureColorSpace.Srgb,
                        RenderTextureAlphaMode.Straight,
                        RenderTextureSemantic.Color,
                        RenderTextureMipPolicy.BaseOnly,
                        CreateRenderTextureMipRecord(0, 1, 1, [21, 22, 23, 255])))),
            new Package.Entry(
                3,
                Package.CreateEnvelope(
                    3,
                    CreateRenderTextureAssetRecord(
                        1,
                        1,
                        RenderTextureColorSpace.Linear,
                        RenderTextureAlphaMode.Straight,
                        RenderTextureSemantic.Data,
                        RenderTextureMipPolicy.BaseOnly,
                        CreateRenderTextureMipRecord(0, 1, 1, [31, 32, 33, 255])))),
            new Package.Entry(
                4,
                Package.CreateEnvelope(
                    4,
                    CreateRenderTextureAssetRecord(
                        1,
                        1,
                        RenderTextureColorSpace.Linear,
                        RenderTextureAlphaMode.Straight,
                        RenderTextureSemantic.Normal,
                        RenderTextureMipPolicy.BaseOnly,
                        CreateRenderTextureMipRecord(0, 1, 1, [41, 42, 43, 255])))),
            new Package.Entry(
                5,
                Package.CreateEnvelope(
                    5,
                    CreateRenderTextureAssetRecord(
                        1,
                        1,
                        RenderTextureColorSpace.Srgb,
                        RenderTextureAlphaMode.Straight,
                        RenderTextureSemantic.Color,
                        RenderTextureMipPolicy.BaseOnly,
                        CreateRenderTextureMipRecord(0, 1, 1, [51, 52, 53, 255])))),
            new Package.Entry(
                10,
                Package.CreateEnvelope(
                    10,
                    CreateRenderMaterialAssetRecord(
                        isSprite: true,
                        CreateRenderMaterialSpriteParametersRecord(textureAssetId: 1),
                        alphaMode: 2,
                        doubleSided: false,
                        depthTest: false,
                        depthWrite: false,
                        cullMode: 0,
                        outputAlpha: 1,
                        requiredCapabilities: 0x009))),
            new Package.Entry(
                11,
                Package.CreateEnvelope(
                    11,
                    CreateRenderMaterialAssetRecord(
                        isSprite: false,
                        CreateRenderMaterialPbrParametersRecord(
                            baseTextureAssetId: 2,
                            packedTextureAssetId: 3,
                            normalTextureAssetId: 4,
                            emissiveTextureAssetId: 5),
                        alphaMode: 0,
                        doubleSided: false,
                        depthTest: true,
                        depthWrite: true,
                        cullMode: 1,
                        outputAlpha: 0,
                        requiredCapabilities: 0x1D1))));
        var packagePath = Package.WritePackage(package);
        var store = await AssetStore.CreateAsync(
            new AssetStoreOptions(packagePath, SHA256.HashData(package), registration)).ConfigureAwait(false);

        try
        {
            using var sprite = await RenderMaterialTextureLoader.LoadAsync(store, new AssetId(10)).ConfigureAwait(false);
            Assert.That(sprite.Material.IsSprite, Is.True);
            Assert.That(sprite.GetTexture(new AssetId(1)).Mips[0].Pixels[0], Is.EqualTo(11));

            using var pbr = await RenderMaterialTextureLoader.LoadAsync(store, new AssetId(11)).ConfigureAwait(false);
            Assert.That(pbr.Material.IsSprite, Is.False);
            Assert.That(pbr.Material.Parameters.Metallic, Is.Zero);
            Assert.That(pbr.Material.Parameters.Roughness, Is.EqualTo(1.0f));
            Assert.That(pbr.Material.Parameters.NormalScale, Is.EqualTo(1.0f));
            Assert.That(pbr.GetTexture(new AssetId(2)).Semantic, Is.EqualTo(RenderTextureSemantic.Color));
            Assert.That(pbr.GetTexture(new AssetId(3)).Semantic, Is.EqualTo(RenderTextureSemantic.Data));
            Assert.That(pbr.GetTexture(new AssetId(4)).Semantic, Is.EqualTo(RenderTextureSemantic.Normal));
            Assert.That(pbr.GetTexture(new AssetId(5)).Semantic, Is.EqualTo(RenderTextureSemantic.Color));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task MaterialAssetCodecRejectsInvalidStateAndTextureSlotMetadata()
    {
        var schemas = new CompiledContentSchemaRegistry([
            .. SceneAssetReaderTests.Fixtures.CreateRegistry().Schemas,
            .. RenderingSchemaCatalogue.Schemas,
        ]);
        var registrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = Renderer.RegisterAssetCodecs(registrationBuilder);
        var registration = registrationBuilder.Build();
        var validPbrParameters = CreateRenderMaterialPbrParametersRecord();
        var invalidDepthState = CreateRenderMaterialAssetRecord(
            isSprite: false,
            validPbrParameters,
            alphaMode: 2,
            doubleSided: false,
            depthTest: true,
            depthWrite: true,
            cullMode: 1,
            outputAlpha: 1,
            requiredCapabilities: 0x199);
        var invalidCapabilities = CreateRenderMaterialAssetRecord(
            isSprite: false,
            validPbrParameters,
            alphaMode: 0,
            doubleSided: false,
            depthTest: true,
            depthWrite: true,
            cullMode: 1,
            outputAlpha: 0,
            requiredCapabilities: 0x191 | 0x040);
        var invalidDoubleSidedCull = CreateRenderMaterialAssetRecord(
            isSprite: false,
            validPbrParameters,
            alphaMode: 0,
            doubleSided: true,
            depthTest: true,
            depthWrite: true,
            cullMode: 1,
            outputAlpha: 0,
            requiredCapabilities: 0x191);
        var invalidSpriteUv = CreateRenderMaterialAssetRecord(
            isSprite: true,
            CreateRenderMaterialSpriteParametersRecord(textureAssetId: 1, uvSet: 1),
            alphaMode: 2,
            doubleSided: false,
            depthTest: false,
            depthWrite: false,
            cullMode: 0,
            outputAlpha: 1,
            requiredCapabilities: 0x009);
        var invalidPbrSlot = CreateRenderMaterialAssetRecord(
            isSprite: false,
            CreateRenderMaterialPbrParametersRecord(baseTextureAssetId: 1),
            alphaMode: 0,
            doubleSided: false,
            depthTest: true,
            depthWrite: true,
            cullMode: 1,
            outputAlpha: 0,
            requiredCapabilities: 0x191);
        var invalidRoughness = CreateRenderMaterialAssetRecord(
            isSprite: false,
            CreateRenderMaterialPbrParametersRecord(roughness: 0.01f),
            alphaMode: 0,
            doubleSided: false,
            depthTest: true,
            depthWrite: true,
            cullMode: 1,
            outputAlpha: 0,
            requiredCapabilities: 0x191);
        var invalidSampler = CreateRenderMaterialAssetRecord(
            isSprite: false,
            CreateRenderMaterialPbrParametersRecord(baseTextureAssetId: 1, baseTextureMip: 3),
            alphaMode: 0,
            doubleSided: false,
            depthTest: true,
            depthWrite: true,
            cullMode: 1,
            outputAlpha: 0,
            requiredCapabilities: 0x191);
        var dataTexture = CreateRenderTextureAssetRecord(
            1,
            1,
            RenderTextureColorSpace.Linear,
            RenderTextureAlphaMode.Straight,
            RenderTextureSemantic.Data,
            RenderTextureMipPolicy.BaseOnly,
            CreateRenderTextureMipRecord(0, 1, 1, [61, 62, 63, 255]));
        var package = Package.CreatePackage(
            new Package.Entry(1, Package.CreateEnvelope(1, dataTexture)),
            new Package.Entry(10, Package.CreateEnvelope(10, invalidDepthState)),
            new Package.Entry(11, Package.CreateEnvelope(11, invalidCapabilities)),
            new Package.Entry(12, Package.CreateEnvelope(12, invalidDoubleSidedCull)),
            new Package.Entry(13, Package.CreateEnvelope(13, invalidSpriteUv)),
            new Package.Entry(14, Package.CreateEnvelope(14, invalidPbrSlot)),
            new Package.Entry(15, Package.CreateEnvelope(15, invalidRoughness)),
            new Package.Entry(16, Package.CreateEnvelope(16, invalidSampler)));
        var packagePath = Package.WritePackage(package);
        var store = await AssetStore.CreateAsync(
            new AssetStoreOptions(packagePath, SHA256.HashData(package), registration)).ConfigureAwait(false);
        AssetLease<RenderMaterialAsset>? pbrLease = null;

        try
        {
            foreach (var assetId in new[] { 10, 11, 12, 13, 15, 16 })
            {
                _ = Assert.ThrowsAsync<InvalidDataException>(
                    async () => await store.LoadAsync<RenderMaterialAsset>(new AssetId(assetId)).ConfigureAwait(false));
            }

            pbrLease = await store.LoadAsync<RenderMaterialAsset>(new AssetId(14)).ConfigureAwait(false);
            _ = Assert.ThrowsAsync<InvalidDataException>(
                async () => await RenderMaterialTextureLoader.RetainAsync(pbrLease).ConfigureAwait(false));
        }
        finally
        {
            pbrLease?.Dispose();
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task MaterialTextureLoaderResolvesDependenciesFromPinnedPackage()
    {
        var schemas = new CompiledContentSchemaRegistry([
            .. SceneAssetReaderTests.Fixtures.CreateRegistry().Schemas,
            .. RenderingSchemaCatalogue.Schemas,
        ]);
        var registrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = Renderer.RegisterAssetCodecs(registrationBuilder);
        var registration = registrationBuilder.Build();
        var originalPackage = Package.CreatePackage(
            new Package.Entry(
                1,
                Package.CreateEnvelope(
                    1,
                    CreateRenderTextureAssetRecord(
                        1,
                        1,
                        RenderTextureColorSpace.Linear,
                        RenderTextureAlphaMode.Premultiplied,
                        RenderTextureSemantic.Color,
                        RenderTextureMipPolicy.BaseOnly,
                        CreateRenderTextureMipRecord(0, 1, 1, [71, 72, 73, 255])))),
            new Package.Entry(
                2,
                Package.CreateEnvelope(
                    2,
                    CreateRenderMaterialAssetRecord(
                        isSprite: true,
                        CreateRenderMaterialSpriteParametersRecord(textureAssetId: 1),
                        alphaMode: 2,
                        doubleSided: false,
                        depthTest: false,
                        depthWrite: false,
                        cullMode: 0,
                        outputAlpha: 1,
                        requiredCapabilities: 0x009))));
        var replacementPackage = Package.CreatePackage(
            new Package.Entry(
                1,
                Package.CreateEnvelope(
                    1,
                    CreateRenderTextureAssetRecord(
                        1,
                        1,
                        RenderTextureColorSpace.Linear,
                        RenderTextureAlphaMode.Premultiplied,
                        RenderTextureSemantic.Color,
                        RenderTextureMipPolicy.BaseOnly,
                        CreateRenderTextureMipRecord(0, 1, 1, [91, 92, 93, 255])))));
        var originalPath = Package.WritePackage(originalPackage);
        var replacementPath = Package.WritePackage(replacementPackage);
        var store = await AssetStore.CreateAsync(
            new AssetStoreOptions(originalPath, SHA256.HashData(originalPackage), registration)).ConfigureAwait(false);
        AssetLease<RenderMaterialAsset>? materialLease = null;
        RenderMaterialTextureSet? textureSet = null;

        try
        {
            materialLease = await store.LoadAsync<RenderMaterialAsset>(new AssetId(2)).ConfigureAwait(false);
            await store.ReplacePackageAsync(
                new AssetStoreOptions(replacementPath, SHA256.HashData(replacementPackage), registration),
                default).ConfigureAwait(false);
            textureSet = await RenderMaterialTextureLoader.RetainAsync(materialLease).ConfigureAwait(false);
            Assert.That(textureSet.GetTexture(new AssetId(1)).Mips[0].Pixels[0], Is.EqualTo(71));

            using var currentTexture = await store.LoadAsync<RenderTextureAsset>(new AssetId(1)).ConfigureAwait(false);
            Assert.That(currentTexture.Value.Mips[0].Pixels[0], Is.EqualTo(91));
        }
        finally
        {
            textureSet?.Dispose();
            materialLease?.Dispose();
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(originalPath);
            File.Delete(replacementPath);
        }
    }
}
