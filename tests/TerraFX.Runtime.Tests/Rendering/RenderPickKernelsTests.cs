// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using DrawIdentity = TerraFX.Rendering.RenderPickKernels.DrawIdentity;
using NUnit.Framework;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Numerics;
using TerraFX.Rendering;

namespace TerraFX.Runtime.Tests.Rendering;

[TestFixture]
internal sealed class RenderPickKernelsTests
{
    private const uint SentinelId = 0xDEADBEEF;

    private static Guid CreateDrawId(int value)
        => new(value, 0, 0x4000, 0x80, 0, 0, 0, 0, 0, 0, 1);

    private static WorldEntityIdentity CreateIdentity(World world)
        => world.GetEntityIdentity(world.CreateEntity(new EntityDescription()));

    private static World CreateWorld()
        => World.Create(new WorldOptions(new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build()) {
            CpuWorkerCount = 0,
        });

    [TestCase(0, 0)]
    [TestCase(0, 2)]
    [TestCase(2, 0)]
    public void EmptyInputKindsLeaveSpareStorageUntouched(int meshCount, int spriteCount)
    {
        using var world = CreateWorld();
        var identity = CreateIdentity(world);
        DrawIdentity[] draws = [new(identity, CreateDrawId(1)), new(identity, CreateDrawId(2))];
        var table = new DrawIdentity[3];
        var sentinel = new DrawIdentity(identity, CreateDrawId(99));
        table.AsSpan().Fill(sentinel);
        uint[] meshIds = [SentinelId, SentinelId, SentinelId];
        uint[] spriteIds = [SentinelId, SentinelId, SentinelId];

        var count = RenderPickKernels.BuildMembership(
            world.WorldGuid,
            draws.AsSpan(0, meshCount),
            draws.AsSpan(0, spriteCount),
            table,
            meshIds,
            spriteIds);

        Assert.That(count, Is.EqualTo(meshCount + spriteCount));
        Assert.That(table.AsSpan(0, count).ToArray(), Is.EqualTo(draws.AsSpan(0, count).ToArray()));
        Assert.That(table.AsSpan(count).ToArray(), Is.All.EqualTo(sentinel));
        Assert.That(meshIds, Is.EqualTo(meshCount == 0 ? new[] { SentinelId, SentinelId, SentinelId } : [1u, 2u, SentinelId]));
        Assert.That(spriteIds, Is.EqualTo(spriteCount == 0 ? new[] { SentinelId, SentinelId, SentinelId } : [1u, 2u, SentinelId]));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void InsufficientOutputStorageLeavesEveryOutputUnchanged(int shortOutput)
    {
        using var world = CreateWorld();
        var identity = CreateIdentity(world);
        DrawIdentity[] meshes = [new(identity, CreateDrawId(1)), new(identity, CreateDrawId(3))];
        DrawIdentity[] sprites = [new(identity, CreateDrawId(2)), new(identity, CreateDrawId(4))];
        var sentinel = new DrawIdentity(identity, CreateDrawId(99));
        var table = new DrawIdentity[shortOutput == 0 ? 3 : 4];
        table.AsSpan().Fill(sentinel);
        var meshIds = new uint[shortOutput == 1 ? 1 : 2];
        meshIds.AsSpan().Fill(SentinelId);
        var spriteIds = new uint[shortOutput == 2 ? 1 : 2];
        spriteIds.AsSpan().Fill(SentinelId);

        _ = Assert.Throws<ArgumentException>(() =>
            RenderPickKernels.BuildMembership(world.WorldGuid, meshes, sprites, table, meshIds, spriteIds));

        Assert.Multiple(() => {
            Assert.That(table, Is.All.EqualTo(sentinel));
            Assert.That(meshIds, Is.All.EqualTo(SentinelId));
            Assert.That(spriteIds, Is.All.EqualTo(SentinelId));
        });
    }

    [TestCase("default-identity")]
    [TestCase("foreign-world")]
    [TestCase("empty-draw")]
    [TestCase("mesh-order")]
    [TestCase("sprite-order")]
    [TestCase("mesh-duplicate")]
    [TestCase("sprite-duplicate")]
    [TestCase("cross-kind-duplicate")]
    [TestCase("empty-world")]
    public void InvalidMembershipLeavesEveryOutputUnchanged(string invalid)
    {
        using var world = CreateWorld();
        using var foreignWorld = CreateWorld();
        var identity = CreateIdentity(world);
        var otherIdentity = CreateIdentity(world);
        DrawIdentity[] meshes = [new(identity, CreateDrawId(1)), new(identity, CreateDrawId(4))];
        DrawIdentity[] sprites = [new(otherIdentity, CreateDrawId(2)), new(otherIdentity, CreateDrawId(5))];
        var worldGuid = world.WorldGuid;

        switch (invalid)
        {
            case "default-identity":
            {
                meshes[1] = meshes[1] with { Identity = default };
                break;
            }

            case "foreign-world":
            {
                meshes[1] = meshes[1] with { Identity = CreateIdentity(foreignWorld) };
                break;
            }

            case "empty-draw":
            {
                meshes[1] = meshes[1] with { DrawId = Guid.Empty };
                break;
            }

            case "mesh-order":
            {
                Array.Reverse(meshes);
                break;
            }

            case "sprite-order":
            {
                Array.Reverse(sprites);
                break;
            }

            case "mesh-duplicate":
            {
                meshes[1] = new DrawIdentity(otherIdentity, meshes[0].DrawId);
                break;
            }

            case "sprite-duplicate":
            {
                sprites[1] = new DrawIdentity(identity, sprites[0].DrawId);
                break;
            }

            case "cross-kind-duplicate":
            {
                sprites[1] = new DrawIdentity(otherIdentity, meshes[1].DrawId);
                break;
            }

            case "empty-world":
            {
                worldGuid = Guid.Empty;
                break;
            }

            default:
            {
                throw new ArgumentOutOfRangeException(nameof(invalid));
            }
        }

        var sentinel = new DrawIdentity(identity, CreateDrawId(99));
        DrawIdentity[] table = [sentinel, sentinel, sentinel, sentinel, sentinel];
        uint[] meshIds = [SentinelId, SentinelId, SentinelId];
        uint[] spriteIds = [SentinelId, SentinelId, SentinelId];

        _ = Assert.Throws<ArgumentException>(() =>
            RenderPickKernels.BuildMembership(worldGuid, meshes, sprites, table, meshIds, spriteIds));

        Assert.Multiple(() => {
            Assert.That(table, Is.All.EqualTo(sentinel));
            Assert.That(meshIds, Is.All.EqualTo(SentinelId));
            Assert.That(spriteIds, Is.All.EqualTo(SentinelId));
        });
    }

    [Test]
    public void MembershipMergesRfcOrderAcrossKindsAndPreservesQualifiedIdentities()
    {
        using var world = CreateWorld();
        var firstIdentity = CreateIdentity(world);
        var secondIdentity = CreateIdentity(world);
        DrawIdentity[] meshes = [
            new(firstIdentity, new Guid("00000001-0000-4000-8000-000000000001")),
            new(firstIdentity, new Guid("01000000-0000-4000-8000-000000000001")),
        ];
        DrawIdentity[] sprites = [
            new(secondIdentity, new Guid("00000100-0000-4000-8000-000000000001")),
            new(firstIdentity, new Guid("00010000-0000-4000-8000-000000000001")),
        ];
        var sentinel = new DrawIdentity(secondIdentity, CreateDrawId(99));
        DrawIdentity[] table = [sentinel, sentinel, sentinel, sentinel, sentinel];
        uint[] meshIds = [SentinelId, SentinelId, SentinelId];
        uint[] spriteIds = [SentinelId, SentinelId, SentinelId];

        var count = RenderPickKernels.BuildMembership(world.WorldGuid, meshes, sprites, table, meshIds, spriteIds);

        Assert.Multiple(() => {
            Assert.That(count, Is.EqualTo(4));
            Assert.That(table, Is.EqualTo([meshes[0], sprites[0], sprites[1], meshes[1], sentinel]));
            Assert.That(meshIds, Is.EqualTo([1u, 4u, SentinelId]));
            Assert.That(spriteIds, Is.EqualTo([2u, 3u, SentinelId]));
        });

        var selected = sentinel;
        Assert.That(RenderPickKernels.TryLookup(table.AsSpan(0, count), 0, out selected), Is.False);
        Assert.That(selected, Is.EqualTo(default(DrawIdentity)));

        for (var index = 0; index < count; index++)
        {
            Assert.That(RenderPickKernels.TryLookup(table.AsSpan(0, count), checked((uint)index + 1), out selected), Is.True);
            Assert.That(selected, Is.EqualTo(table[index]));
        }
    }

    [Test]
    public void MembershipSupportsTheExactCeilingAndRefusesExcessWithoutWrites()
    {
        using var world = CreateWorld();
        var identity = CreateIdentity(world);
        var meshes = new DrawIdentity[65_536];
        var sprites = new DrawIdentity[65_536];

        for (var index = 0; index < meshes.Length; index++)
        {
            meshes[index] = new DrawIdentity(identity, CreateDrawId((2 * index) + 1));
            sprites[index] = new DrawIdentity(identity, CreateDrawId((2 * index) + 2));
        }

        var (drawCount, elementBytes) = RenderPickKernels.GetOutputRequirements(meshes.Length, sprites.Length);
        var table = new DrawIdentity[drawCount + 1];
        var meshIds = new uint[meshes.Length];
        var spriteIds = new uint[sprites.Length + 1];
        var written = RenderPickKernels.BuildMembership(world.WorldGuid, meshes, sprites, table, meshIds, spriteIds);

        Assert.That(written, Is.EqualTo(131_072));
        Assert.That(elementBytes, Is.EqualTo(11_010_048));

        for (var index = 0; index < meshes.Length; index++)
        {
            if ((meshIds[index] != (2 * index) + 1)
                || (spriteIds[index] != (2 * index) + 2)
                || (table[2 * index] != meshes[index])
                || (table[(2 * index) + 1] != sprites[index]))
            {
                Assert.Fail($"The complete ceiling membership/maps differ at source index {index}.");
            }
        }

        var sentinel = new DrawIdentity(identity, CreateDrawId(999_999));
        table.AsSpan().Fill(sentinel);
        meshIds.AsSpan().Fill(SentinelId);
        spriteIds.AsSpan().Fill(SentinelId);
        var extraSprites = new DrawIdentity[sprites.Length + 1];
        sprites.CopyTo(extraSprites, 0);
        extraSprites[^1] = new DrawIdentity(identity, CreateDrawId(131_073));

        _ = Assert.Throws<InvalidOperationException>(() =>
            RenderPickKernels.BuildMembership(world.WorldGuid, meshes, extraSprites, table, meshIds, spriteIds));

        Assert.Multiple(() => {
            Assert.That(table, Is.All.EqualTo(sentinel));
            Assert.That(meshIds, Is.All.EqualTo(SentinelId));
            Assert.That(spriteIds, Is.All.EqualTo(SentinelId));
        });
    }

    [Test]
    public void OutputRequirementsRejectNegativeExcessAndOverflowingCounts()
    {
        Assert.Multiple(() => {
            Assert.That(RenderPickKernels.GetOutputRequirements(0, 0), Is.EqualTo((0, 0L)));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => RenderPickKernels.GetOutputRequirements(-1, 0));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => RenderPickKernels.GetOutputRequirements(0, -1));
            _ = Assert.Throws<InvalidOperationException>(() => RenderPickKernels.GetOutputRequirements(131_072, 1));
            _ = Assert.Throws<InvalidOperationException>(() => RenderPickKernels.GetOutputRequirements(int.MaxValue, 0));
            _ = Assert.Throws<OverflowException>(() => RenderPickKernels.GetOutputRequirements(int.MaxValue, 1));
            _ = Assert.Throws<OverflowException>(() => RenderPickKernels.GetOutputRequirements(int.MaxValue, int.MaxValue));
        });
    }

    [Test]
    public void OverlappingOutputRangesRefuseBeforeMutation()
    {
        using var world = CreateWorld();
        var identity = CreateIdentity(world);
        DrawIdentity[] meshes = [new(identity, CreateDrawId(1)), new(identity, CreateDrawId(3))];
        DrawIdentity[] sprites = [new(identity, CreateDrawId(2))];
        var sentinel = new DrawIdentity(identity, CreateDrawId(99));
        DrawIdentity[] table = [sentinel, sentinel, sentinel];
        DrawIdentity[] sharedTable = [meshes[0], meshes[1], sentinel];
        uint[] map = [SentinelId, SentinelId, SentinelId];
        DrawIdentity[] originalMeshes = [.. meshes];

        _ = Assert.Throws<ArgumentException>(() =>
            RenderPickKernels.BuildMembership(world.WorldGuid, sharedTable.AsSpan(0, 2), sprites, sharedTable, map, map.AsSpan(2)));
        Assert.That(sharedTable, Is.EqualTo([meshes[0], meshes[1], sentinel]));
        Assert.That(map, Is.All.EqualTo(SentinelId));

        _ = Assert.Throws<ArgumentException>(() =>
            RenderPickKernels.BuildMembership(world.WorldGuid, meshes, sprites, table, map.AsSpan(0, 2), map.AsSpan(1, 1)));
        Assert.That(table, Is.All.EqualTo(sentinel));
        Assert.That(map, Is.All.EqualTo(SentinelId));

        _ = Assert.Throws<ArgumentException>(() =>
            RenderPickKernels.BuildMembership(
                world.WorldGuid,
                meshes,
                sprites,
                table,
                MemoryMarshal.Cast<DrawIdentity, uint>(meshes.AsSpan())[..2],
                map.AsSpan(2, 1)));
        Assert.That(meshes, Is.EqualTo(originalMeshes));
        Assert.That(table, Is.All.EqualTo(sentinel));
        Assert.That(map, Is.All.EqualTo(SentinelId));
    }

    [Test]
    public void PixelValidationRejectsInvalidOrCollapsedViewportAndDefaultTarget()
    {
        Assert.Multiple(() => {
            _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                RenderPickKernels.ValidatePixel(new Vector4(float.NaN, 0, 1, 1), new PixelSize(10, 10), 0, 0));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                RenderPickKernels.ValidatePixel(new Vector4(0, 0, float.PositiveInfinity, 1), new PixelSize(10, 10), 0, 0));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                RenderPickKernels.ValidatePixel(new Vector4(0, 0, float.Epsilon, float.Epsilon), new PixelSize(10, 10), 0, 0));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                RenderPickKernels.ValidatePixel(new Vector4(0.75f, 0, 0.5f, 1), new PixelSize(10, 10), 0, 0));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                RenderPickKernels.ValidatePixel(new Vector4(0, 0, 1, 1), default, 0, 0));
        });
    }

    [TestCase(-1, 2)]
    [TestCase(1, 2)]
    [TestCase(8, 2)]
    [TestCase(10, 2)]
    [TestCase(2, 1)]
    [TestCase(2, 8)]
    [TestCase(2, 10)]
    [TestCase(int.MinValue, 2)]
    [TestCase(int.MaxValue, 2)]
    [TestCase(2, int.MinValue)]
    [TestCase(2, int.MaxValue)]
    public void PixelValidationRejectsOutsideViewportOrTargetWithoutClamping(int pixelX, int pixelY)
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            RenderPickKernels.ValidatePixel(new Vector4(0.25f, 0.25f, 0.5f, 0.5f), new PixelSize(10, 10), pixelX, pixelY));
    }

    [TestCase(2, 2)]
    [TestCase(7, 7)]
    public void PixelValidationUsesNearestEvenEdgesAndAHalfOpenArea(int pixelX, int pixelY)
    {
        var area = RenderPickKernels.ValidatePixel(new Vector4(0.25f, 0.25f, 0.5f, 0.5f), new PixelSize(10, 10), pixelX, pixelY);

        Assert.That(area, Is.EqualTo(new RenderArea(2, 2, 6, 6)));
    }

    [Test]
    public void ZeroIsBackgroundButNonzeroOutOfTableIdsAreInvalidData()
    {
        using var world = CreateWorld();
        var identity = CreateIdentity(world);
        DrawIdentity[] meshes = [new(identity, CreateDrawId(1))];
        DrawIdentity[] table = [default];
        uint[] meshIds = [SentinelId];
        _ = RenderPickKernels.BuildMembership(world.WorldGuid, meshes, [], table, meshIds, []);
        var selected = table[0];

        Assert.That(RenderPickKernels.TryLookup([], 0, out var background), Is.False);
        Assert.That(background, Is.EqualTo(default(DrawIdentity)));
        _ = Assert.Throws<InvalidDataException>(() => RenderPickKernels.TryLookup(table, 2, out selected));
        Assert.That(selected, Is.EqualTo(table[0]));
        _ = Assert.Throws<InvalidDataException>(() => RenderPickKernels.TryLookup(table, uint.MaxValue, out selected));
        Assert.That(selected, Is.EqualTo(table[0]));
        _ = Assert.Throws<InvalidDataException>(() => RenderPickKernels.TryLookup([], 1, out selected));
        Assert.That(selected, Is.EqualTo(table[0]));
    }
}
