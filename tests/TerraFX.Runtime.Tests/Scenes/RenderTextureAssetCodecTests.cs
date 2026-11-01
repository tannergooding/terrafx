// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
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
    private static byte[] CreateRenderTextureAssetRecord(
        int width,
        int height,
        RenderTextureColorSpace colorSpace,
        RenderTextureAlphaMode alphaMode,
        RenderTextureSemantic semantic,
        RenderTextureMipPolicy mipPolicy,
        params byte[][] mips)
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/texture").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(1, checked((uint)width)),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(2, checked((uint)height)),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(3, 0),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(4, (byte)colorSpace),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(5, (byte)alphaMode),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(6, (byte)semantic),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(7, (byte)mipPolicy),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(8, mips));

    private static byte[] CreateRenderTextureMipRecord(int level, int width, int height, byte[] pixels)
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/texture-mip").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(1, checked((uint)level)),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(2, checked((uint)width)),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(3, checked((uint)height)),
            new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(
                4,
                13,
                1,
                checked((ulong)pixels.Length),
                pixels));

    [Test]
    public async Task TextureAssetCodecReadsExactBaseOnlyAndFullMipChains()
    {
        var schemas = new CompiledContentSchemaRegistry([
            .. SceneAssetReaderTests.Fixtures.CreateRegistry().Schemas,
            .. RenderingSchemaCatalogue.Schemas,
        ]);
        var registrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = Renderer.RegisterAssetCodecs(registrationBuilder);
        var registration = registrationBuilder.Build();
        var basePixels = new byte[32];
        basePixels[0] = 17;
        basePixels[^1] = 29;
        var baseOnlyPixels = new byte[4];
        baseOnlyPixels[0] = 43;
        var package = Package.CreatePackage(
            new Package.Entry(
                1,
                Package.CreateEnvelope(
                    1,
                    CreateRenderTextureAssetRecord(
                        4,
                        2,
                        RenderTextureColorSpace.Srgb,
                        RenderTextureAlphaMode.Premultiplied,
                        RenderTextureSemantic.Color,
                        RenderTextureMipPolicy.FullChain,
                        CreateRenderTextureMipRecord(0, 4, 2, basePixels),
                        CreateRenderTextureMipRecord(1, 2, 1, new byte[8]),
                        CreateRenderTextureMipRecord(2, 1, 1, new byte[4])))),
            new Package.Entry(
                2,
                Package.CreateEnvelope(
                    2,
                    CreateRenderTextureAssetRecord(
                        1,
                        1,
                        RenderTextureColorSpace.Linear,
                        RenderTextureAlphaMode.Straight,
                        RenderTextureSemantic.Color,
                        RenderTextureMipPolicy.BaseOnly,
                        CreateRenderTextureMipRecord(0, 1, 1, baseOnlyPixels)))));
        var packagePath = Package.WritePackage(package);
        var store = await AssetStore.CreateAsync(
            new AssetStoreOptions(packagePath, SHA256.HashData(package), registration)).ConfigureAwait(false);

        try
        {
            using var lease = await store.LoadAsync<RenderTextureAsset>(new AssetId(1)).ConfigureAwait(false);
            var texture = lease.Value;

            Assert.That(texture.Width, Is.EqualTo(4));
            Assert.That(texture.Height, Is.EqualTo(2));
            Assert.That(texture.Format, Is.Zero);
            Assert.That(texture.ColorSpace, Is.EqualTo(RenderTextureColorSpace.Srgb));
            Assert.That(texture.AlphaMode, Is.EqualTo(RenderTextureAlphaMode.Premultiplied));
            Assert.That(texture.Semantic, Is.EqualTo(RenderTextureSemantic.Color));
            Assert.That(texture.MipPolicy, Is.EqualTo(RenderTextureMipPolicy.FullChain));
            Assert.That(texture.Mips, Has.Length.EqualTo(3));
            Assert.That(texture.Mips[0].Pixels, Has.Length.EqualTo(32));
            Assert.That(texture.Mips[0].Pixels[0], Is.EqualTo(17));
            Assert.That(texture.Mips[0].Pixels[^1], Is.EqualTo(29));
            Assert.That(texture.Mips[1].Pixels, Has.Length.EqualTo(8));
            Assert.That(texture.Mips[2].Pixels, Has.Length.EqualTo(4));

            using var baseOnlyLease = await store.LoadAsync<RenderTextureAsset>(new AssetId(2)).ConfigureAwait(false);
            Assert.That(baseOnlyLease.Value.MipPolicy, Is.EqualTo(RenderTextureMipPolicy.BaseOnly));
            Assert.That(baseOnlyLease.Value.Mips, Has.Length.EqualTo(1));
            Assert.That(baseOnlyLease.Value.Mips[0].Pixels[0], Is.EqualTo(43));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            System.IO.File.Delete(packagePath);
        }
    }

    [Test]
    public async Task TextureAssetCodecRejectsInvalidMipDimensionsAndNormalColorState()
    {
        var schemas = new CompiledContentSchemaRegistry([
            .. SceneAssetReaderTests.Fixtures.CreateRegistry().Schemas,
            .. RenderingSchemaCatalogue.Schemas,
        ]);
        var registrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = Renderer.RegisterAssetCodecs(registrationBuilder);
        var registration = registrationBuilder.Build();
        var badMipTexture = CreateRenderTextureAssetRecord(
            4,
            2,
            RenderTextureColorSpace.Linear,
            RenderTextureAlphaMode.Straight,
            RenderTextureSemantic.Color,
            RenderTextureMipPolicy.FullChain,
            CreateRenderTextureMipRecord(0, 4, 2, new byte[32]),
            CreateRenderTextureMipRecord(1, 3, 1, new byte[12]),
            CreateRenderTextureMipRecord(2, 1, 1, new byte[4]));
        var badNormalTexture = CreateRenderTextureAssetRecord(
            1,
            1,
            RenderTextureColorSpace.Srgb,
            RenderTextureAlphaMode.Straight,
            RenderTextureSemantic.Normal,
            RenderTextureMipPolicy.BaseOnly,
            CreateRenderTextureMipRecord(0, 1, 1, new byte[4]));
        var package = Package.CreatePackage(
            new Package.Entry(1, Package.CreateEnvelope(1, badMipTexture)),
            new Package.Entry(2, Package.CreateEnvelope(2, badNormalTexture)));
        var packagePath = Package.WritePackage(package);
        var store = await AssetStore.CreateAsync(
            new AssetStoreOptions(packagePath, SHA256.HashData(package), registration)).ConfigureAwait(false);

        try
        {
            _ = Assert.ThrowsAsync<InvalidDataException>(
                async () => await store.LoadAsync<RenderTextureAsset>(new AssetId(1)).ConfigureAwait(false));
            _ = Assert.ThrowsAsync<InvalidDataException>(
                async () => await store.LoadAsync<RenderTextureAsset>(new AssetId(2)).ConfigureAwait(false));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            System.IO.File.Delete(packagePath);
        }
    }
}
