// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Graphics;
using TerraFX.Rendering;

namespace TerraFX.Runtime.Tests.Scenes;

internal sealed partial class SceneAssetStoreTests
{
    private static byte[] CreateRenderAtlasFrameRecord(
        Guid frameId,
        int page,
        int x,
        int y,
        int width,
        int height,
        int padding = 2)
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/atlas-frame").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(1, frameId),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(2, checked((uint)page)),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(3, checked((uint)x)),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(4, checked((uint)y)),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(5, checked((uint)width)),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(6, checked((uint)height)),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(7, checked((byte)padding)));

    private static byte[] CreateRenderAtlasPageTextureRecord(byte pixelValue, bool corruptLeftBorder = false)
    {
        var pixels = new byte[16 * 16 * 4];
        Array.Fill(pixels, pixelValue);

        if (corruptLeftBorder)
        {
            pixels[((4 * 16) + 3) * 4] = checked((byte)(pixelValue + 1));
        }

        return CreateRenderTextureAssetRecord(
            16,
            16,
            RenderTextureColorSpace.Linear,
            RenderTextureAlphaMode.Premultiplied,
            RenderTextureSemantic.Color,
            RenderTextureMipPolicy.BaseOnly,
            CreateRenderTextureMipRecord(0, 16, 16, pixels));
    }

    private static byte[] CreateRenderAtlasRecord(int pageAssetId, params byte[][] frames)
        => CreateRenderAtlasRecord([pageAssetId], frames);

    private static byte[] CreateRenderAtlasRecord(int[] pageAssetIds, params byte[][] frames)
    {
        var pageReferences = new byte[pageAssetIds.Length][];

        for (var index = 0; index < pageAssetIds.Length; index++)
        {
            pageReferences[index] = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
                new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId,
                1,
                0,
                SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(
                    1,
                    checked((uint)pageAssetIds[index])),
                SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(
                    2,
                    new ContentSchemaIdentityAssignment("native/texture").SchemaId),
                SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(3, 1));
        }

        return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/atlas").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(1, pageReferences),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(2, frames));
    }

    [Test]
    public async Task AtlasAssetCodecAccepts16PagesAndRejects17()
    {
        var schemas = new CompiledContentSchemaRegistry([
            .. SceneAssetReaderTests.Fixtures.CreateRegistry().Schemas,
            .. RenderingSchemaCatalogue.Schemas,
        ]);
        var registrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = Renderer.RegisterAssetCodecs(registrationBuilder);
        var registration = registrationBuilder.Build();
        var maximumPageIds = new int[16];
        var excessPageIds = new int[17];
        Array.Fill(maximumPageIds, 1);
        Array.Fill(excessPageIds, 1);
        var frame = CreateRenderAtlasFrameRecord(
            new Guid("00000000-0000-0000-0000-000000000001"),
            page: 0,
            x: 4,
            y: 4,
            width: 4,
            height: 4);
        var package = Package.CreatePackage(
            new Package.Entry(1, Package.CreateEnvelope(1, CreateRenderAtlasPageTextureRecord(17))),
            new Package.Entry(100, Package.CreateEnvelope(100, CreateRenderAtlasRecord(maximumPageIds, frame))),
            new Package.Entry(101, Package.CreateEnvelope(101, CreateRenderAtlasRecord(excessPageIds, frame))));
        var packagePath = Package.WritePackage(package);
        var store = await AssetStore.CreateAsync(
            new AssetStoreOptions(packagePath, SHA256.HashData(package), registration)).ConfigureAwait(false);

        try
        {
            using var atlasLease = await store.LoadAsync<RenderAtlasAsset>(new AssetId(100)).ConfigureAwait(false);
            Assert.That(atlasLease.Value.Pages, Has.Length.EqualTo(16));
            using var pageSet = await RenderAtlasPageLoader.RetainAsync(atlasLease).ConfigureAwait(false);
            Assert.That(pageSet.GetPage(15).Mips[0].Pixels[0], Is.EqualTo(17));
            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.LoadAsync<RenderAtlasAsset>(new AssetId(101)).ConfigureAwait(false));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task AtlasPageSetLoadsSamePackageTextureAfterReplacement()
    {
        var schemas = new CompiledContentSchemaRegistry([
            .. SceneAssetReaderTests.Fixtures.CreateRegistry().Schemas,
            .. RenderingSchemaCatalogue.Schemas,
        ]);
        var registrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = Renderer.RegisterAssetCodecs(registrationBuilder);
        var registration = registrationBuilder.Build();
        var frame = CreateRenderAtlasFrameRecord(
            new Guid("00000000-0000-0000-0000-000000000001"),
            page: 0,
            x: 4,
            y: 4,
            width: 4,
            height: 4);
        var originalPackage = Package.CreatePackage(
            new Package.Entry(1, Package.CreateEnvelope(1, CreateRenderAtlasPageTextureRecord(17))),
            new Package.Entry(2, Package.CreateEnvelope(2, CreateRenderAtlasRecord(1, frame))));
        var replacementPackage = Package.CreatePackage(
            new Package.Entry(1, Package.CreateEnvelope(1, CreateRenderAtlasPageTextureRecord(29))));
        var originalPath = Package.WritePackage(originalPackage);
        var replacementPath = Package.WritePackage(replacementPackage);
        var store = await AssetStore.CreateAsync(
            new AssetStoreOptions(originalPath, SHA256.HashData(originalPackage), registration)).ConfigureAwait(false);
        AssetLease<RenderAtlasAsset>? atlasLease = null;
        RenderAtlasPageSet? pageSet = null;
        using var progress = new RenderAtlasPageLoader.OwnedProgress();

        try
        {
            atlasLease = await store.LoadAsync<RenderAtlasAsset>(new AssetId(2)).ConfigureAwait(false);
            await store.ReplacePackageAsync(
                new AssetStoreOptions(replacementPath, SHA256.HashData(replacementPackage), registration),
                CancellationToken.None).ConfigureAwait(false);
            var pendingPageSet = await RenderAtlasPageLoader.RetainOwnedAsync(
                atlasLease,
                progress).ConfigureAwait(false);
            pageSet = progress.TakeResult();
            Assert.That(pageSet, Is.SameAs(pendingPageSet));
            Assert.That(pageSet.Atlas.Frames, Has.Length.EqualTo(1));
            Assert.That(pageSet.GetPage(0).Mips[0].Pixels[0], Is.EqualTo(17));

            using var currentPage = await store.LoadAsync<RenderTextureAsset>(new AssetId(1)).ConfigureAwait(false);
            Assert.That(currentPage.Value.Mips[0].Pixels[0], Is.EqualTo(29));

            await store.DisposeAsync().ConfigureAwait(false);
            Assert.That(pageSet.GetPage(0).Mips[0].Pixels[0], Is.EqualTo(17));
        }
        finally
        {
            pageSet?.Dispose();
            atlasLease?.Dispose();
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(originalPath);
            File.Delete(replacementPath);
        }
    }

    [Test]
    public async Task AtlasPageSetRejectsNonClampedBorderAndOverlappingPaddedFrames()
    {
        var schemas = new CompiledContentSchemaRegistry([
            .. SceneAssetReaderTests.Fixtures.CreateRegistry().Schemas,
            .. RenderingSchemaCatalogue.Schemas,
        ]);
        var registrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = Renderer.RegisterAssetCodecs(registrationBuilder);
        var registration = registrationBuilder.Build();
        var firstFrame = CreateRenderAtlasFrameRecord(
            new Guid("00000000-0000-0000-0000-000000000001"),
            page: 0,
            x: 4,
            y: 4,
            width: 4,
            height: 4);
        var secondFrame = CreateRenderAtlasFrameRecord(
            new Guid("00000000-0000-0000-0000-000000000002"),
            page: 0,
            x: 8,
            y: 4,
            width: 4,
            height: 4);
        var package = Package.CreatePackage(
            new Package.Entry(1, Package.CreateEnvelope(1, CreateRenderAtlasPageTextureRecord(17, corruptLeftBorder: true))),
            new Package.Entry(2, Package.CreateEnvelope(2, CreateRenderAtlasRecord(1, firstFrame))),
            new Package.Entry(3, Package.CreateEnvelope(3, CreateRenderAtlasPageTextureRecord(29))),
            new Package.Entry(4, Package.CreateEnvelope(4, CreateRenderAtlasRecord(3, firstFrame, secondFrame))));
        var packagePath = Package.WritePackage(package);
        var store = await AssetStore.CreateAsync(
            new AssetStoreOptions(packagePath, SHA256.HashData(package), registration)).ConfigureAwait(false);

        try
        {
            using var borderAtlas = await store.LoadAsync<RenderAtlasAsset>(new AssetId(2)).ConfigureAwait(false);
            using var borderProgress = new RenderAtlasPageLoader.OwnedProgress();
            _ = Assert.ThrowsAsync<InvalidDataException>(
                async () => await RenderAtlasPageLoader.RetainOwnedAsync(
                    borderAtlas,
                    borderProgress).ConfigureAwait(false));

            using var overlapAtlas = await store.LoadAsync<RenderAtlasAsset>(new AssetId(4)).ConfigureAwait(false);
            _ = Assert.ThrowsAsync<InvalidDataException>(
                async () => await RenderAtlasPageLoader.RetainAsync(overlapAtlas).ConfigureAwait(false));
        }
        finally
        {
            await store.DisposeAsync().ConfigureAwait(false);
            File.Delete(packagePath);
        }
    }
}
