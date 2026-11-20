// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using NUnit.Framework;
using TerraFX;
using TerraFX.Animation;
using TerraFX.Assets;
using TerraFX.Graphics;
using TerraFX.Rendering;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

[TestFixture]
internal sealed class RendererAssetCodecProfileTests
{
    [Test]
    public void BuiltInStaticAndSkeletonCodecProfilesRequireFactoryRegistration()
    {
        var schemas = new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
            .. RenderingSchemaCatalogue.Schemas,
            .. AnimationSchemaCatalogue.Schemas,
            .. AnimationSchemaCatalogue.PlaybackSchemas,
        ]);
        var profiledBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = Renderer.RegisterAssetCodecs(profiledBuilder);
        _ = AnimationSupport.RegisterAssetCodecs(profiledBuilder);
        var profiledRegistration = profiledBuilder.Build();

        Assert.That(RendererAssetCodecProfiles.HasBuiltInStaticAssetCodecReceipts(profiledRegistration), Is.True);
        Assert.That(AnimationSupport.HasBuiltInSkeletonAssetCodecReceipt(profiledRegistration), Is.True);

        var ordinaryBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = ordinaryBuilder.RegisterCodec(new RenderMeshAssetCodec());
        _ = ordinaryBuilder.RegisterCodec(new RenderTextureAssetCodec());
        _ = ordinaryBuilder.RegisterCodec(new RenderAtlasAssetCodec());
        _ = ordinaryBuilder.RegisterCodec(new RenderMaterialAssetCodec());
        _ = ordinaryBuilder.RegisterCodec(new SkeletonAssetCodec());
        _ = ordinaryBuilder.RegisterCodec(new AnimationClipAssetCodec());
        var ordinaryRegistration = ordinaryBuilder.Build();

        Assert.That(RendererAssetCodecProfiles.HasBuiltInStaticAssetCodecReceipts(ordinaryRegistration), Is.False);
        Assert.That(AnimationSupport.HasBuiltInSkeletonAssetCodecReceipt(ordinaryRegistration), Is.False);
    }
}
