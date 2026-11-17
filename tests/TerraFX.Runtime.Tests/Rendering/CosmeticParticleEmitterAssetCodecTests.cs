// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Content.Tools;
using TerraFX.Content.Tools.Tests;
using TerraFX.Rendering;

namespace TerraFX.Runtime.Tests.Rendering;

[TestFixture]
internal sealed class CosmeticParticleEmitterAssetCodecTests
{
    private string _directory = string.Empty;

    private static async Task AssertEmitterRejectedAsync(string directory, byte[] emitter)
    {
        var path = await NativeMeshTestData.WriteParticlePackageAsync(directory, emitter).ConfigureAwait(false);
        var registration = NativeMeshTestData.CreateRegistration(includeParticles: true);
        var store = await NativeMeshTestData.OpenStoreAsync(path, registration).ConfigureAwait(false);
        await using var storeLifetime = store.ConfigureAwait(false);

        _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
            await store.LoadAsync<CosmeticParticleEmitterAsset>(new AssetId(checked((int)NativeMeshTestData.ParticleEmitterId))).ConfigureAwait(false));
    }

    [TestCase("space")]
    [TestCase("spawn")]
    [TestCase("lifetime")]
    [TestCase("curve-start")]
    [TestCase("curve-order")]
    [TestCase("curve-end")]
    [TestCase("color")]
    [TestCase("size")]
    [TestCase("velocity-order")]
    [TestCase("plane-z")]
    [TestCase("stream")]
    [TestCase("empty-frame")]
    public async Task EmitterCodecRejectsMalformedAuthoredValues(string failure)
    {
        var emitter = failure switch {
            "space" => NativeMeshTestData.CreateParticleEmitterRecord(space: 2),
            "spawn" => NativeMeshTestData.CreateParticleEmitterRecord(spawnPerTick: 131_073),
            "lifetime" => NativeMeshTestData.CreateParticleEmitterRecord(lifetimeTicks: 0),
            "curve-start" => NativeMeshTestData.CreateParticleEmitterRecord(colorStartAge: -0.0f),
            "curve-order" => NativeMeshTestData.CreateParticleEmitterRecord(colorEndAge: 0.0f),
            "curve-end" => NativeMeshTestData.CreateParticleEmitterRecord(sizeEndAge: 0.75f),
            "color" => NativeMeshTestData.CreateParticleEmitterRecord(colorStart: new TerraFX.Numerics.Vector4(-0.1f, 1, 1, 1)),
            "size" => NativeMeshTestData.CreateParticleEmitterRecord(sizeEnd: new TerraFX.Numerics.Vector2(0, 1)),
            "velocity-order" => NativeMeshTestData.CreateParticleEmitterRecord(
                velocityMinimum: new TerraFX.Numerics.Vector3(1, 0, 0),
                velocityMaximum: new TerraFX.Numerics.Vector3(0, 0, 0)),
            "plane-z" => NativeMeshTestData.CreateParticleEmitterRecord(
                velocityMinimum: new TerraFX.Numerics.Vector3(0, 0, 1),
                velocityMaximum: new TerraFX.Numerics.Vector3(0, 0, 1)),
            "stream" => NativeMeshTestData.CreateParticleEmitterRecord(stream: (ulong)long.MaxValue + 1),
            "empty-frame" => NativeMeshTestData.CreateParticleEmitterRecord(frameId: Guid.Empty),
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };

        await AssertEmitterRejectedAsync(_directory, emitter).ConfigureAwait(false);
    }

    [Test]
    public async Task EmitterCodecRetainsAuthoredCurvesAndExactAtlasClosure()
    {
        var path = await NativeMeshTestData.WriteParticlePackageAsync(
            _directory,
            NativeMeshTestData.CreateParticleEmitterRecord()).ConfigureAwait(false);
        var registration = NativeMeshTestData.CreateRegistration(includeParticles: true);
        var store = await NativeMeshTestData.OpenStoreAsync(path, registration).ConfigureAwait(false);
        await using var storeLifetime = store.ConfigureAwait(false);
        using var emitter = await store.LoadAsync<CosmeticParticleEmitterAsset>(
            new AssetId(checked((int)NativeMeshTestData.ParticleEmitterId))).ConfigureAwait(false);
        var membership = await CosmeticParticleEmitterAssetBinding.ValidatePackageBindingsAsync(emitter).ConfigureAwait(false);

        Assert.Multiple(() => {
            Assert.That(emitter.Value.AssetId.Value, Is.EqualTo(NativeMeshTestData.ParticleEmitterId));
            Assert.That(emitter.Value.Space, Is.EqualTo(CosmeticParticleSpace.PlaneXY));
            Assert.That(emitter.Value.SpawnPerTick, Is.EqualTo(2));
            Assert.That(emitter.Value.LifetimeTicks, Is.EqualTo(10_000_000));
            Assert.That(emitter.Value.VelocityMinimum, Is.EqualTo(new TerraFX.Numerics.Vector3(-1, -1, 0)));
            Assert.That(emitter.Value.VelocityMaximum, Is.EqualTo(new TerraFX.Numerics.Vector3(1, 1, 0)));
            Assert.That(emitter.Value.ColorKeys.Select(static key => key.Age), Is.EqualTo([0.0f, 1.0f]));
            Assert.That(emitter.Value.ColorKeys[1].Color, Is.EqualTo(new TerraFX.Numerics.Vector4(1, 0, 0, 0)));
            Assert.That(emitter.Value.SizeKeys.Select(static key => key.Size), Is.EqualTo([
                TerraFX.Numerics.Vector2.One,
                new TerraFX.Numerics.Vector2(2, 2),
            ]));
            Assert.That(emitter.Value.AtlasAssetId.Value, Is.EqualTo(NativeMeshTestData.ParticleAtlasId));
            Assert.That(emitter.Value.FrameId, Is.EqualTo(NativeMeshTestData.ParticleFrameId));
            Assert.That(emitter.Value.Seed, Is.EqualTo(42));
            Assert.That(emitter.Value.Stream, Is.EqualTo(54));
            Assert.That(emitter.Value.Material, Is.Null);
            Assert.That(membership.Select(static id => id.Value), Is.EqualTo([
                NativeMeshTestData.ParticleEmitterId,
                NativeMeshTestData.ParticleAtlasId,
                NativeMeshTestData.ParticlePageId,
            ]));
        });
    }

    [TestCase(1)]
    [TestCase(2)]
    public async Task ExplicitMaterialVersionsRetainTheSelectedAtlasPage(int materialMajor)
    {
        var path = await NativeMeshTestData.WriteParticlePackageAsync(
            _directory,
            NativeMeshTestData.CreateParticleEmitterRecord(
                materialId: checked((int)NativeMeshTestData.ParticleMaterialId),
                materialMajor: materialMajor),
            new ContentPackageAsset(
                NativeMeshTestData.ParticleMaterialId,
                NativeMeshTestData.CreateParticleMaterialRecord(materialMajor))).ConfigureAwait(false);
        var registration = NativeMeshTestData.CreateRegistration(includeParticles: true);
        var store = await NativeMeshTestData.OpenStoreAsync(path, registration).ConfigureAwait(false);
        await using var storeLifetime = store.ConfigureAwait(false);
        using var emitter = await store.LoadAsync<CosmeticParticleEmitterAsset>(
            new AssetId(checked((int)NativeMeshTestData.ParticleEmitterId))).ConfigureAwait(false);
        var membership = await CosmeticParticleEmitterAssetBinding.ValidatePackageBindingsAsync(emitter).ConfigureAwait(false);

        Assert.That(emitter.Value.Material?.SchemaMajor, Is.EqualTo(materialMajor));
        Assert.That(membership.Select(static id => id.Value), Is.EqualTo([
            NativeMeshTestData.ParticleEmitterId,
            NativeMeshTestData.ParticleAtlasId,
            NativeMeshTestData.ParticlePageId,
            NativeMeshTestData.ParticleMaterialId,
        ]));
    }

    [Test]
    public void M05CodecRegistrationRejectsUnknownBitsAndPreservesTheStaticBuilder()
    {
        var builder = new AssetStoreRegistrationBuilder(new CompiledContentSchemaRegistry([]));

        Assert.That(
            Renderer.RegisterM05AssetCodecs(builder, RendererCpuFeatures.Static),
            Is.SameAs(builder));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Renderer.RegisterM05AssetCodecs(builder, (RendererCpuFeatures)16));
    }

    [TestCase("missing-frame")]
    [TestCase("wrong-page")]
    [TestCase("wrong-profile")]
    public async Task PackageBindingsRejectMissingOrIncompatibleSpriteResources(string failure)
    {
        var emitter = NativeMeshTestData.CreateParticleEmitterRecord(
            materialId: checked((int)NativeMeshTestData.ParticleMaterialId));
        var material = failure switch {
            "wrong-page" => NativeMeshTestData.CreateParticleMaterialRecord(textureId: 999),
            "wrong-profile" => NativeMeshTestData.CreateMaterial(),
            _ => NativeMeshTestData.CreateParticleMaterialRecord(),
        };

        if (failure == "missing-frame")
        {
            emitter = NativeMeshTestData.CreateParticleEmitterRecord(
                frameId: new Guid("80000000-0000-4000-8000-000000000002"));
        }

        var path = await NativeMeshTestData.WriteParticlePackageAsync(
            _directory,
            emitter,
            new ContentPackageAsset(NativeMeshTestData.ParticleMaterialId, material)).ConfigureAwait(false);
        var registration = NativeMeshTestData.CreateRegistration(includeParticles: true);
        var store = await NativeMeshTestData.OpenStoreAsync(path, registration).ConfigureAwait(false);
        await using var storeLifetime = store.ConfigureAwait(false);
        using var lease = await store.LoadAsync<CosmeticParticleEmitterAsset>(
            new AssetId(checked((int)NativeMeshTestData.ParticleEmitterId))).ConfigureAwait(false);

        _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
            await CosmeticParticleEmitterAssetBinding.ValidatePackageBindingsAsync(lease).ConfigureAwait(false));
    }

    [SetUp]
    public void SetUp() => _directory = NativeMeshTestData.CreateDirectory();

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);
}
