// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using NUnit.Framework;
using TerraFX.Graphics;
using TerraFX.Rendering;

namespace TerraFX.Runtime.Tests.Scenes;

[TestFixture]
internal sealed class RenderSpriteBatcherTests
{
    [Test]
    public void GroupsOnlyAdjacentDrawsWithMatchingScenePageAndMaterial()
    {
        var cameraId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();
        var area = new RenderArea(2, 3, 10, 12);
        var first = new RenderSpriteBatchKey(sceneId, TextureAssetId: 1, MaterialAssetId: 3);
        var secondPage = new RenderSpriteBatchKey(sceneId, TextureAssetId: 2, MaterialAssetId: 3);
        var secondMaterial = new RenderSpriteBatchKey(sceneId, TextureAssetId: 1, MaterialAssetId: 4);
        RenderSpriteBatchKey[] keys = [first, first, secondPage, first, secondMaterial];
        var batches = ImmutableArray.CreateBuilder<RenderSpriteBatch>();

        RenderSpriteBatcher.GroupAdjacent(
            cameraId,
            area,
            [6, 2, 9, 1, 4],
            keys,
            batches);

        Assert.That(batches, Has.Count.EqualTo(4));
        Assert.Multiple(() => {
            Assert.That(batches[0].CameraId, Is.EqualTo(cameraId));
            Assert.That(batches[0].Area, Is.EqualTo(area));
            Assert.That(batches[0].Key, Is.EqualTo(first));
            Assert.That(batches[0].DrawIndices, Is.EqualTo([6, 2]));
            Assert.That(batches[1].Key, Is.EqualTo(secondPage));
            Assert.That(batches[1].DrawIndices, Is.EqualTo([9]));
            Assert.That(batches[2].Key, Is.EqualTo(first));
            Assert.That(batches[2].DrawIndices, Is.EqualTo([1]));
            Assert.That(batches[3].Key, Is.EqualTo(secondMaterial));
            Assert.That(batches[3].DrawIndices, Is.EqualTo([4]));
        });
    }

    [Test]
    public void KeepsCameraOrderAsIndependentBatchRuns()
    {
        var firstCamera = Guid.NewGuid();
        var secondCamera = Guid.NewGuid();
        var firstArea = new RenderArea(0, 0, 16, 16);
        var secondArea = new RenderArea(8, 0, 8, 16);
        var key = new RenderSpriteBatchKey(Guid.NewGuid(), TextureAssetId: 1, MaterialAssetId: null);
        var batches = ImmutableArray.CreateBuilder<RenderSpriteBatch>();

        RenderSpriteBatcher.GroupAdjacent(firstCamera, firstArea, [0], [key], batches);
        RenderSpriteBatcher.GroupAdjacent(secondCamera, secondArea, [1], [key], batches);

        Assert.That(batches, Has.Count.EqualTo(2));
        Assert.That(batches[0].CameraId, Is.EqualTo(firstCamera));
        Assert.That(batches[0].Area, Is.EqualTo(firstArea));
        Assert.That(batches[0].DrawIndices, Is.EqualTo([0]));
        Assert.That(batches[1].CameraId, Is.EqualTo(secondCamera));
        Assert.That(batches[1].Area, Is.EqualTo(secondArea));
        Assert.That(batches[1].DrawIndices, Is.EqualTo([1]));
    }
}
