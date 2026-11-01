// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Numerics;
using TerraFX.Rendering;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

internal sealed partial class SceneAssetStoreTests
{
    private static byte[] CreateRenderSpriteAssetReferenceRecord(int assetId, Guid schemaId)
        => SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(1, checked((uint)assetId)),
            SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(2, schemaId),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(3, 1));

    private static SceneHierarchyCodecTests.FixtureBuilder.FixtureField CreateRenderSpriteInt32Field(uint tag, int value)
    {
        var payload = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(payload, value);
        return new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(tag, 7, 1, 1, payload);
    }

    private static byte[] CreateRenderSpriteRecord(
        int atlasAssetId,
        Guid frameId,
        int? materialAssetId,
        byte flipMask = 0,
        bool pixelSnap = false)
    {
        var fields = new System.Collections.Generic.List<SceneHierarchyCodecTests.FixtureBuilder.FixtureField> {
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(
                1,
                CreateRenderSpriteAssetReferenceRecord(
                    atlasAssetId,
                    new ContentSchemaIdentityAssignment("native/atlas").SchemaId)),
            SceneHierarchyCodecTests.FixtureBuilder.CreateGuidField(2, frameId),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(3, 14, 0.25f, 0.75f),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(4, 14, 2.0f, 3.0f),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(5, 16, 1.0f, 0.5f, 0.25f, 1.0f),
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt8Field(6, flipMask),
            CreateRenderSpriteInt32Field(7, 2),
            CreateRenderSpriteInt32Field(8, -3),
        };

        if (materialAssetId is int materialId)
        {
            fields.Add(SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(
                9,
                CreateRenderSpriteAssetReferenceRecord(
                    materialId,
                    new ContentSchemaIdentityAssignment("native/material").SchemaId),
                isRequired: false));
        }

        fields.Add(new SceneHierarchyCodecTests.FixtureBuilder.FixtureField(10, 9, 1, 1, [pixelSnap ? (byte)1 : (byte)0]));
        return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("render/sprite").SchemaId,
            1,
            0,
            [.. fields]);
    }

    [Test]
    public async Task SceneActivationRestoresSpriteAssetReferencesAndRejectsInvalidMutation()
    {
        var schemas = CreateRendererRegistry();
        var frameId = new Guid("30000000-0000-4000-8000-000000000001");
        var textureRecord = CreateRenderTextureAssetRecord(
            1,
            1,
            RenderTextureColorSpace.Linear,
            RenderTextureAlphaMode.Premultiplied,
            RenderTextureSemantic.Color,
            RenderTextureMipPolicy.BaseOnly,
            CreateRenderTextureMipRecord(0, 1, 1, [17, 18, 19, 255]));
        var atlasRecord = CreateRenderAtlasRecord(
            1,
            CreateRenderAtlasFrameRecord(frameId, page: 0, x: 4, y: 4, width: 4, height: 4));
        var materialRecord = CreateRenderMaterialAssetRecord(
            isSprite: true,
            CreateRenderMaterialSpriteParametersRecord(),
            alphaMode: 2,
            doubleSided: false,
            depthTest: false,
            depthWrite: false,
            cullMode: 0,
            outputAlpha: 1,
            requiredCapabilities: 0x009);
        var spriteWithMaterial = CreateRenderSpriteRecord(2, frameId, materialAssetId: 3, flipMask: 1, pixelSnap: true);
        var spriteWithoutMaterial = CreateRenderSpriteRecord(2, frameId, materialAssetId: null);
        var sceneWithMaterial = SceneAssetReaderTests.Fixtures.CreateScene(
            new AssetId(4),
            additionalEntityComponents: [spriteWithMaterial]);
        var sceneWithoutMaterial = SceneAssetReaderTests.Fixtures.CreateScene(
            new AssetId(5),
            additionalEntityComponents: [spriteWithoutMaterial]);
        var package = Package.CreatePackage(
            new Package.Entry(1, Package.CreateEnvelope(1, textureRecord)),
            new Package.Entry(2, Package.CreateEnvelope(2, atlasRecord)),
            new Package.Entry(3, Package.CreateEnvelope(3, materialRecord)),
            new Package.Entry(4, Package.CreateEnvelope(4, sceneWithMaterial)),
            new Package.Entry(5, Package.CreateEnvelope(5, sceneWithoutMaterial)));
        var packagePath = Package.WritePackage(package);
        var assetRegistrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = SceneSupport.RegisterAssetCodecs(assetRegistrationBuilder);
        _ = Renderer.RegisterAssetCodecs(assetRegistrationBuilder);
        var assets = await AssetStore.CreateAsync(
            new AssetStoreOptions(packagePath, SHA256.HashData(package), assetRegistrationBuilder.Build())).ConfigureAwait(false);

        try
        {
            var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
            _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
            _ = worldRegistrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration());
            using var world = World.Create(new WorldOptions(worldRegistrationBuilder.Build()));
            var loader = new SceneLoader(world, assets);
            var withMaterial = ActivateScene(loader, world, new AssetId(4));
            var withoutMaterial = ActivateScene(loader, world, new AssetId(5));

            Assert.That(withMaterial.TryGetEntity(SceneAssetReaderTests.Fixtures.RootEntityId, out var firstEntity), Is.True);
            Assert.That(withoutMaterial.TryGetEntity(SceneAssetReaderTests.Fixtures.RootEntityId, out var secondEntity), Is.True);
            Assert.That(world.TryGet<RenderComponentDefinitions.SpriteComponent>(firstEntity, out var first), Is.True);
            Assert.That(first.AtlasAssetId, Is.EqualTo(2));
            Assert.That(first.FrameId, Is.EqualTo(frameId));
            Assert.That(first.Pivot, Is.EqualTo(new Vector2(0.25f, 0.75f)));
            Assert.That(first.Size, Is.EqualTo(new Vector2(2.0f, 3.0f)));
            Assert.That(first.Tint, Is.EqualTo(new Vector4(1.0f, 0.5f, 0.25f, 1.0f)));
            Assert.That(first.FlipMask, Is.EqualTo(1));
            Assert.That(first.SortingLayer, Is.EqualTo(2));
            Assert.That(first.SortingOrder, Is.EqualTo(-3));
            Assert.That(first.MaterialAssetId.HasValue, Is.True);
            Assert.That(first.MaterialAssetId.Value, Is.EqualTo(3));
            Assert.That(first.PixelSnap, Is.True);
            Assert.That(world.TryGet<RenderComponentDefinitions.SpriteComponent>(secondEntity, out var second), Is.True);
            Assert.That(second.MaterialAssetId.HasValue, Is.False);

            var entityWithoutSprite = world.CreateEntity(new EntityDescription());
            Assert.That(
                () => world.Add<RenderComponentDefinitions.SpriteComponent>(entityWithoutSprite),
                Throws.InvalidOperationException);
            Assert.That(
                world.TryGet<RenderComponentDefinitions.SpriteComponent>(entityWithoutSprite, out _),
                Is.False);

            RenderComponentDefinitions.SpriteComponent CreateSprite(
                int atlasAssetId = 2,
                Vector2? pivotValue = null,
                Vector2? sizeValue = null,
                Vector4? tintValue = null,
                byte flipMaskValue = 0)
            {
                return new RenderComponentDefinitions.SpriteComponent(
                    atlasAssetId,
                    frameId,
                    pivotValue ?? new Vector2(0.5f, 0.5f),
                    sizeValue ?? Vector2.One,
                    tintValue ?? Vector4.One,
                    flipMaskValue,
                    sortingLayer: 0,
                    sortingOrder: 0,
                    materialAssetId: default,
                    pixelSnap: false);
            }

            var invalidSprites = new[] {
                CreateSprite(atlasAssetId: 0),
                CreateSprite(pivotValue: new Vector2(-0.1f, 0.5f)),
                CreateSprite(sizeValue: new Vector2(0.0f, 1.0f)),
                CreateSprite(tintValue: new Vector4(float.NaN, 1.0f, 1.0f, 1.0f)),
                CreateSprite(flipMaskValue: 4),
            };

            foreach (var invalidSprite in invalidSprites)
            {
                Assert.That(() => world.Set(firstEntity, invalidSprite), Throws.ArgumentException);
            }

            Assert.That(
                world.TryGet<RenderComponentDefinitions.SpriteComponent>(firstEntity, out first),
                Is.True);
            Assert.That(first.AtlasAssetId, Is.EqualTo(2));
        }
        finally
        {
            await assets.DisposeAsync().ConfigureAwait(false);
            File.Delete(packagePath);
        }
    }
}
