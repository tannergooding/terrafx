// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX.Assets;
using TerraFX.Content.Tools;
using TerraFX.Content.Tools.Tests;
using TerraFX.Rendering;

namespace TerraFX.Runtime.Tests.Rendering;

[TestFixture]
internal sealed class RenderMeshAsset2CodecTests
{
    private string _directory = string.Empty;

    private static byte[] InvalidMesh(string failure)
    {
        var mesh = NativeMeshTestData.CreateMesh();
        var invalidLevel = failure switch {
            "coarse-index" => NativeMeshTestData.CreateLevel([0, 1, 5]),
            "coarse-triangle" => NativeMeshTestData.CreateLevel([0, 0, 1]),
            "coarse-count" => NativeMeshTestData.CreateLevel([0, 1, 2, 0]),
            "coarse-groups" => NativeMeshTestData.Rewrite(
                NativeMeshTestData.CreateLevel(),
                2,
                new ContentRecordField(2, 24, 1, NativeMeshTestData.CreateGroups(indexCount: 6))),
            "group-count" => NativeMeshTestData.CreateLevel(groupCount: 2),
            "group-mapping" => NativeMeshTestData.CreateLevel(materialSlot: 1),
            _ => null,
        };

        if (invalidLevel is not null)
        {
            if (failure == "group-mapping")
            {
                var reference = NativeMeshTestData.CreateAssetReference(NativeMeshTestData.MaterialId, "native/material");
                mesh = NativeMeshTestData.Rewrite(mesh, 5, new ContentRecordField(
                    5, 24, 2, ContentPackageWriter.EncodeRecordArray([reference, reference])));
            }

            return WithAdditionalLevel(mesh, invalidLevel);
        }

        return failure switch {
            "missing-levels" => NativeMeshTestData.Rewrite(NativeMeshTestData.CreateMesh(1), 8),
            "missing-thresholds" => NativeMeshTestData.Rewrite(NativeMeshTestData.CreateMesh(1), 9),
            "optional-levels" => NativeMeshTestData.Rewrite(
                mesh, 8, new ContentRecordField(8, 24, 1, NativeMeshTestData.GetFieldPayload(mesh, 8)), required: false),
            "level-limit" => NativeMeshTestData.CreateMesh(9),
            "all-position-bounds" => NativeMeshTestData.Rewrite(mesh, 6, new ContentRecordField(6, 22, 1, NativeMeshTestData.CreateBounds(1))),
            "sphere-bounds" => NativeMeshTestData.Rewrite(mesh, 6, new ContentRecordField(6, 22, 1, NativeMeshTestData.CreateBounds(radius: 2))),
            "skin-without-skeleton" => NativeMeshTestData.Rewrite(NativeMeshTestData.CreateMesh(skinned: true), 7),
            "skeleton-without-skin" => NativeMeshTestData.Rewrite(
                mesh, 7, new ContentRecordField(7, 22, 1, NativeMeshTestData.CreateAssetReference(NativeMeshTestData.SkeletonId, "native/skeleton")), required: false),
            "material-major" => NativeMeshTestData.Rewrite(
                mesh, 5, new ContentRecordField(5, 24, 1, ContentPackageWriter.EncodeRecordArray([
                    NativeMeshTestData.CreateAssetReference(NativeMeshTestData.MaterialId, "native/material", 3),
                ]))),
            "threshold-count" => NativeMeshTestData.Rewrite(
                mesh, 9, new ContentRecordField(9, 23, 2, NativeMeshTestData.EncodeThresholds([0.75f, 0.5f]))),
            "threshold-zero" => NativeMeshTestData.Rewrite(
                mesh, 9, new ContentRecordField(9, 23, 1, NativeMeshTestData.EncodeThresholds([0.0f]))),
            "threshold-negative" => NativeMeshTestData.Rewrite(
                mesh, 9, new ContentRecordField(9, 23, 1, NativeMeshTestData.EncodeThresholds([-0.5f]))),
            "threshold-increasing" => NativeMeshTestData.Rewrite(
                NativeMeshTestData.CreateMesh(3),
                9,
                new ContentRecordField(9, 23, 2, NativeMeshTestData.EncodeThresholds([0.5f, 0.75f]))),
            "threshold-duplicate" => NativeMeshTestData.Rewrite(
                NativeMeshTestData.CreateMesh(3),
                9,
                new ContentRecordField(9, 23, 2, NativeMeshTestData.EncodeThresholds([0.5f, 0.5f]))),
            "threshold-nan" => NativeMeshTestData.Rewrite(
                mesh, 9, new ContentRecordField(9, 23, 1, NativeMeshTestData.EncodeThresholds([float.NaN]))),
            "threshold-infinity" => NativeMeshTestData.Rewrite(
                mesh, 9, new ContentRecordField(9, 23, 1, NativeMeshTestData.EncodeThresholds([float.PositiveInfinity]))),
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };
    }

    private static byte[] WithAdditionalLevel(byte[] mesh, byte[] level)
        => NativeMeshTestData.Rewrite(mesh, 8, new ContentRecordField(8, 24, 1, ContentPackageWriter.EncodeRecordArray([level])));

    [TestCase(32_768, true)]
    [TestCase(32_769, false)]
    public async Task AggregateGroupLimitIncludesFinestAndCoarseLevels(int groupsPerLevel, bool admitted)
    {
        var path = await NativeMeshTestData.WritePackageAsync(
            _directory,
            NativeMeshTestData.CreateMesh(groupCount: groupsPerLevel)).ConfigureAwait(false);
        var store = await NativeMeshTestData.OpenStoreAsync(path).ConfigureAwait(false);
        await using var storeLifetime = store.ConfigureAwait(false);

        if (admitted)
        {
            using var mesh = await store.LoadAsync<RenderMeshAsset2>(new AssetId(100)).ConfigureAwait(false);
            Assert.That(mesh.Value.Levels.Sum(static level => level.Groups.Length), Is.EqualTo(65_536));
        }
        else
        {
            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.LoadAsync<RenderMeshAsset2>(new AssetId(100)).ConfigureAwait(false));
        }
    }

    [Test]
    public async Task AggregateIndexPreflightAdmitsACompleteNearLimitPackage()
    {
        var indices = NativeMeshTestData.RepeatTriangle(174_758);
        var mesh = NativeMeshTestData.CreateMesh();
        mesh = NativeMeshTestData.Rewrite(mesh, 3, new ContentRecordField(3, 22, 1, NativeMeshTestData.CreateIndices(indices, wide: false)));
        mesh = NativeMeshTestData.Rewrite(mesh, 4, new ContentRecordField(
            4, 24, 1, NativeMeshTestData.CreateGroups(indexCount: checked((uint)indices.Length))));
        mesh = WithAdditionalLevel(mesh, NativeMeshTestData.CreateLevel(indices));
        var path = await NativeMeshTestData.WritePackageAsync(_directory, mesh).ConfigureAwait(false);
        var store = await NativeMeshTestData.OpenStoreAsync(path).ConfigureAwait(false);
        await using var storeLifetime = store.ConfigureAwait(false);
        using var lease = await store.LoadAsync<RenderMeshAsset2>(new AssetId(100)).ConfigureAwait(false);

        Assert.That(lease.Value.Levels.Sum(static level => level.Indices.Length), Is.EqualTo(1_048_548));
        Assert.That(lease.Value.Levels[0].Indices[524_273], Is.EqualTo(2));
        Assert.That(lease.Value.Levels[1].Indices[524_273], Is.EqualTo(2));
    }

    [TestCase("missing-levels")]
    [TestCase("missing-thresholds")]
    [TestCase("optional-levels")]
    [TestCase("level-limit")]
    [TestCase("coarse-index")]
    [TestCase("coarse-triangle")]
    [TestCase("coarse-count")]
    [TestCase("coarse-groups")]
    [TestCase("group-count")]
    [TestCase("group-mapping")]
    [TestCase("all-position-bounds")]
    [TestCase("sphere-bounds")]
    [TestCase("skin-without-skeleton")]
    [TestCase("skeleton-without-skin")]
    [TestCase("material-major")]
    [TestCase("threshold-count")]
    [TestCase("threshold-zero")]
    [TestCase("threshold-negative")]
    [TestCase("threshold-increasing")]
    [TestCase("threshold-duplicate")]
    [TestCase("threshold-nan")]
    [TestCase("threshold-infinity")]
    public async Task ExactDecoderRejectsInvalidLevelAndSharedData(string failure)
    {
        var path = await NativeMeshTestData.WritePackageAsync(_directory, InvalidMesh(failure)).ConfigureAwait(false);
        var store = await NativeMeshTestData.OpenStoreAsync(path).ConfigureAwait(false);
        await using var storeLifetime = store.ConfigureAwait(false);
        _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
            await store.LoadAsync<RenderMeshAsset2>(new AssetId(100)).ConfigureAwait(false));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(8)]
    public async Task ExactDecoderRetainsAuthoredLevelsWidthsGroupsReferencesAndUnusedPositions(int levelCount)
    {
        var path = await NativeMeshTestData.WritePackageAsync(_directory, NativeMeshTestData.CreateMesh(levelCount)).ConfigureAwait(false);
        var store = await NativeMeshTestData.OpenStoreAsync(path).ConfigureAwait(false);
        await using var storeLifetime = store.ConfigureAwait(false);
        using var lease = await store.LoadAsync<RenderMeshAsset2>(new AssetId(100)).ConfigureAwait(false);
        var mesh = lease.Value;
        var membership = await RenderMeshAsset2Binding.ValidatePackageBindingsAsync(lease).ConfigureAwait(false);

        Assert.Multiple(() => {
            Assert.That(mesh.VertexCount, Is.EqualTo(5));
            Assert.That(mesh.Vertices.Positions[4].X, Is.EqualTo(2));
            Assert.That(mesh.Bounds.Maximum.X, Is.EqualTo(2));
            Assert.That(mesh.Levels, Has.Length.EqualTo(levelCount));
            Assert.That(mesh.Thresholds, Has.Length.EqualTo(levelCount - 1));
            Assert.That(mesh.Levels[0].IndexFormat, Is.EqualTo(RenderMeshIndexFormat.UInt16));
            Assert.That(mesh.Levels[0].Indices, Is.EqualTo([0u, 1u, 2u, 0u, 2u, 3u]));
            Assert.That(mesh.Levels[0].Groups, Is.EqualTo([new RenderMeshGroup(0, 6, 0)]));
            Assert.That(mesh.MaterialSlots, Is.EqualTo([new RenderMeshMaterialSlot(new AssetId(101), 1)]));
            Assert.That(membership, Is.EqualTo([new AssetId(100), new AssetId(101)]));
            Assert.That(mesh.SkeletonAssetId, Is.Null);
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(mesh.EncodedRecord.AsSpan()[48..]), Is.EqualTo(2));
        });

        for (var index = 1; index < levelCount; index++)
        {
            Assert.That(mesh.Levels[index].IndexFormat, Is.EqualTo(RenderMeshIndexFormat.UInt32));
            Assert.That(mesh.Levels[index].Indices, Is.EqualTo([0u, 1u, 2u]));
            Assert.That(mesh.Levels[index].Groups, Is.EqualTo([new RenderMeshGroup(0, 3, 0)]));
            Assert.That(mesh.Thresholds[index - 1], Is.EqualTo(0.625f / (1 << (index - 1))));
        }
    }

    [Test]
    public async Task LevelsAreNotSortedOrSimplifiedByTriangleCount()
    {
        var mesh = WithAdditionalLevel(
            NativeMeshTestData.CreateMesh(),
            NativeMeshTestData.CreateLevel([0, 1, 2, 0, 2, 3, 0, 1, 2]));
        var path = await NativeMeshTestData.WritePackageAsync(_directory, mesh).ConfigureAwait(false);
        var store = await NativeMeshTestData.OpenStoreAsync(path).ConfigureAwait(false);
        await using var storeLifetime = store.ConfigureAwait(false);
        using var lease = await store.LoadAsync<RenderMeshAsset2>(new AssetId(100)).ConfigureAwait(false);
        Assert.That(lease.Value.Levels.Select(static level => level.Indices.Length), Is.EqualTo([6, 9]));
    }

    [TestCase(1, 2)]
    [TestCase(2, 1)]
    public async Task MaterialReferenceMajorIsCheckedAgainstTheRetainedPackage(int referenceMajor, int payloadMajor)
    {
        var path = await NativeMeshTestData.WritePackageAsync(
            _directory,
            NativeMeshTestData.CreateMesh(materialMajor: referenceMajor),
            NativeMeshTestData.CreateMaterial(payloadMajor)).ConfigureAwait(false);
        var store = await NativeMeshTestData.OpenStoreAsync(path).ConfigureAwait(false);
        await using var storeLifetime = store.ConfigureAwait(false);
        _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
            await store.LoadAsync<RenderMeshAsset2>(new AssetId(100)).ConfigureAwait(false));
    }

    [TestCase(0.0f)]
    [TestCase(1.0f)]
    [TestCase(0.25f)]
    public async Task MaterialTwoPreservesItsRequiredIndependentPickCutoff(float cutoff)
    {
        var path = await NativeMeshTestData.WritePackageAsync(
            _directory,
            NativeMeshTestData.CreateMesh(materialMajor: 2),
            NativeMeshTestData.CreateMaterial(2, pickAlphaCutoff: cutoff)).ConfigureAwait(false);
        var store = await NativeMeshTestData.OpenStoreAsync(path).ConfigureAwait(false);
        await using var storeLifetime = store.ConfigureAwait(false);
        using var mesh = await store.LoadAsync<RenderMeshAsset2>(new AssetId(100)).ConfigureAwait(false);
        using var material = await mesh.LoadFromPackageAsync<RenderMaterialAsset2>(new AssetId(101)).ConfigureAwait(false);
        _ = await RenderMeshAsset2Binding.ValidatePackageBindingsAsync(mesh).ConfigureAwait(false);

        Assert.That(mesh.Value.MaterialSlots[0].SchemaMajor, Is.EqualTo(2));
        Assert.That(material.Value.PickAlphaCutoff, Is.EqualTo(cutoff));
        Assert.That(material.Value.Data.AlphaCutoff, Is.EqualTo(0.5f));
        _ = Assert.ThrowsAsync<NotSupportedException>(async () =>
            await store.LoadAsync<RenderMaterialAsset>(new AssetId(101)).ConfigureAwait(false));
    }

    [TestCase(-0.1f)]
    [TestCase(1.1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public async Task MaterialTwoRejectsInvalidPickCutoffs(float cutoff)
    {
        var path = await NativeMeshTestData.WritePackageAsync(
            _directory,
            NativeMeshTestData.CreateMesh(materialMajor: 2),
            NativeMeshTestData.CreateMaterial(2, pickAlphaCutoff: cutoff)).ConfigureAwait(false);
        var store = await NativeMeshTestData.OpenStoreAsync(path).ConfigureAwait(false);
        await using var storeLifetime = store.ConfigureAwait(false);
        _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
            await store.LoadAsync<RenderMaterialAsset2>(new AssetId(101)).ConfigureAwait(false));
    }

    [Test]
    public async Task MaterialTwoRequiresTheEncodedPickCutoffDespiteItsAuthorDefault()
    {
        var material = NativeMeshTestData.Rewrite(NativeMeshTestData.CreateMaterial(2), 14);
        var path = await NativeMeshTestData.WritePackageAsync(
            _directory,
            NativeMeshTestData.CreateMesh(materialMajor: 2),
            material).ConfigureAwait(false);
        var store = await NativeMeshTestData.OpenStoreAsync(path).ConfigureAwait(false);
        await using var storeLifetime = store.ConfigureAwait(false);
        _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
            await store.LoadAsync<RenderMaterialAsset2>(new AssetId(101)).ConfigureAwait(false));
    }

    [Test]
    public async Task NewVersionsRequireExplicitCodecRegistrationAndDoNotAliasOnePointZero()
    {
        var path = await NativeMeshTestData.WritePackageAsync(_directory, NativeMeshTestData.CreateMesh()).ConfigureAwait(false);
        var store = await NativeMeshTestData.OpenStoreAsync(path, NativeMeshTestData.CreateRegistration(includeLod: false)).ConfigureAwait(false);
        await using var storeLifetime = store.ConfigureAwait(false);

        _ = Assert.ThrowsAsync<NotSupportedException>(async () =>
            await store.LoadAsync<RenderMeshAsset2>(new AssetId(100)).ConfigureAwait(false));
        _ = Assert.ThrowsAsync<NotSupportedException>(async () =>
            await store.LoadAsync<RenderMeshAsset>(new AssetId(100)).ConfigureAwait(false));
    }

    [Test]
    public async Task OnePointZeroMeshAndMaterialRemainExactAndRejectLodAndPickFields()
    {
        var mesh = NativeMeshTestData.Rewrite(NativeMeshTestData.CreateMesh(), major: 1);
        var material = NativeMeshTestData.Rewrite(
            NativeMeshTestData.CreateMaterial(),
            14,
            new ContentRecordField(14, 10, 1, ContentPackageWriter.EncodeSingle(0.25f)));
        var package = ContentPackageWriter.CreatePackage(
            NativeMeshTestData.GetProfileRecord(),
            [new ContentPackageAsset(100, mesh), new ContentPackageAsset(101, material)]);
        var path = Path.Combine(_directory, "one-point-zero.epkg");
        await File.WriteAllBytesAsync(path, package).ConfigureAwait(false);
        var store = await NativeMeshTestData.OpenStoreAsync(path).ConfigureAwait(false);
        await using var storeLifetime = store.ConfigureAwait(false);

        _ = Assert.ThrowsAsync<NotSupportedException>(async () =>
            await store.LoadAsync<RenderMeshAsset>(new AssetId(100)).ConfigureAwait(false));
        _ = Assert.ThrowsAsync<NotSupportedException>(async () =>
            await store.LoadAsync<RenderMaterialAsset>(new AssetId(101)).ConfigureAwait(false));
        _ = Assert.ThrowsAsync<NotSupportedException>(async () =>
            await store.LoadAsync<RenderMeshAsset2>(new AssetId(100)).ConfigureAwait(false));
    }

    [Test]
    public async Task RetainedLodGeometryAndReferenceMajorsSurvivePackageReplacement()
    {
        var originalPath = await NativeMeshTestData.WritePackageAsync(_directory, NativeMeshTestData.CreateMesh()).ConfigureAwait(false);
        var replacementPath = await NativeMeshTestData.WritePackageAsync(
            _directory,
            NativeMeshTestData.CreateMesh(1, materialMajor: 2),
            NativeMeshTestData.CreateMaterial(2)).ConfigureAwait(false);
        var registration = NativeMeshTestData.CreateRegistration();
        var store = await NativeMeshTestData.OpenStoreAsync(originalPath, registration).ConfigureAwait(false);
        await using var storeLifetime = store.ConfigureAwait(false);
        using var oldMesh = await store.LoadAsync<RenderMeshAsset2>(new AssetId(100)).ConfigureAwait(false);
        var replacementBytes = await File.ReadAllBytesAsync(replacementPath).ConfigureAwait(false);
        await store.ReplacePackageAsync(new AssetStoreOptions(
            replacementPath,
            SHA256.HashData(replacementBytes),
            registration)).ConfigureAwait(false);
        using var currentMesh = await store.LoadAsync<RenderMeshAsset2>(new AssetId(100)).ConfigureAwait(false);
        _ = await RenderMeshAsset2Binding.ValidatePackageBindingsAsync(oldMesh).ConfigureAwait(false);
        _ = await RenderMeshAsset2Binding.ValidatePackageBindingsAsync(currentMesh).ConfigureAwait(false);
        using var oldMaterial = await oldMesh.LoadFromPackageAsync<RenderMaterialAsset>(new AssetId(101)).ConfigureAwait(false);
        using var currentMaterial = await currentMesh.LoadFromPackageAsync<RenderMaterialAsset2>(new AssetId(101)).ConfigureAwait(false);

        Assert.That(oldMesh.Value.Levels, Has.Length.EqualTo(2));
        Assert.That(currentMesh.Value.Levels, Has.Length.EqualTo(1));
        Assert.That(oldMesh.Value.MaterialSlots[0].SchemaMajor, Is.EqualTo(1));
        Assert.That(currentMesh.Value.MaterialSlots[0].SchemaMajor, Is.EqualTo(2));
        Assert.That(oldMaterial.Value.AlphaCutoff, Is.EqualTo(0.5f));
        Assert.That(currentMaterial.Value.PickAlphaCutoff, Is.EqualTo(0.25f));
    }

    [SetUp]
    public void SetUp() => _directory = NativeMeshTestData.CreateDirectory();

    [TestCase(0, true)]
    [TestCase(1, false)]
    public async Task SkinBindingUsesTheActualSkeletonJointDomain(int jointIndex, bool admitted)
    {
        var path = await NativeMeshTestData.WritePackageAsync(
            _directory,
            NativeMeshTestData.CreateMesh(skinned: true, jointIndex: checked((ushort)jointIndex)),
            material: null,
            new ContentPackageAsset(NativeMeshTestData.SkeletonId, NativeMeshTestData.CreateSkeleton())).ConfigureAwait(false);
        var store = await NativeMeshTestData.OpenStoreAsync(path).ConfigureAwait(false);
        await using var storeLifetime = store.ConfigureAwait(false);
        using var mesh = await store.LoadAsync<RenderMeshAsset2>(new AssetId(100)).ConfigureAwait(false);

        if (admitted)
        {
            var membership = await RenderMeshAsset2Binding.ValidatePackageBindingsAsync(mesh).ConfigureAwait(false);
            Assert.That(membership, Is.EqualTo([new AssetId(100), new AssetId(101), new AssetId(102)]));
        }
        else
        {
            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await RenderMeshAsset2Binding.ValidatePackageBindingsAsync(mesh).ConfigureAwait(false));
        }
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [TestCase("count")]
    [TestCase("zero")]
    [TestCase("negative")]
    [TestCase("nan")]
    [TestCase("infinity")]
    [TestCase("equal")]
    [TestCase("increasing")]
    [TestCase("element-kind")]
    [TestCase("empty-element-kind")]
    public async Task ThresholdArraysRejectWrongCountsAndNonpositiveNondecreasingOrNonfiniteValues(string failure)
    {
        float[] thresholds = failure switch {
            "count" => [],
            "zero" => [0],
            "negative" => [-1],
            "nan" => [float.NaN],
            "infinity" => [float.PositiveInfinity],
            "equal" => [0.625f, 0.625f],
            "increasing" => [0.5f, 0.625f],
            "element-kind" => [0.625f],
            "empty-element-kind" => [],
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };
        var mesh = NativeMeshTestData.CreateMesh(failure == "empty-element-kind" ? 1 : thresholds.Length == 2 ? 3 : 2);
        var payload = failure switch {
            "element-kind" => ContentPackageWriter.EncodeFixedArray([0x3F20_0000u]),
            "empty-element-kind" => ContentPackageWriter.EncodeFixedArray(ReadOnlySpan<uint>.Empty),
            _ => NativeMeshTestData.EncodeThresholds(thresholds),
        };
        mesh = NativeMeshTestData.Rewrite(
            mesh,
            9,
            new ContentRecordField(9, 23, checked((ulong)thresholds.Length), payload));
        var path = await NativeMeshTestData.WritePackageAsync(_directory, mesh).ConfigureAwait(false);
        var store = await NativeMeshTestData.OpenStoreAsync(path).ConfigureAwait(false);
        await using var storeLifetime = store.ConfigureAwait(false);
        _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
            await store.LoadAsync<RenderMeshAsset2>(new AssetId(100)).ConfigureAwait(false));
    }
}
