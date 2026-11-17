// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using NUnit.Framework;
using TerraFX.Animation;
using TerraFX.Entities;
using TerraFX.Numerics;

namespace TerraFX.Runtime.Tests.Animation;

internal sealed class RootMotionAuthorTests
{
    private static RootMotionSlotAuthor CreateAbsentSlot()
        => new(
            present: false,
            playerId: null,
            playerCreationLineageId: null,
            playerCreationBranchId: null,
            actorId: null,
            actorCreationLineageId: null,
            actorCreationBranchId: null,
            tickIndex: null,
            translation: null,
            rotation: null,
            sources: null,
            extractionPolicy: null,
            controller: null);

    private static RootMotionSlotAuthor CreatePresentSlot(
        ImmutableArray<RootMotionSourceAuthor> sources,
        Vector3 translation,
        Quaternion rotation)
        => new(
            present: true,
            playerId: new Guid("60000000-0000-4000-8000-000000000001"),
            playerCreationLineageId: new Guid("60000000-0000-4000-8000-000000000002"),
            playerCreationBranchId: new Guid("60000000-0000-4000-8000-000000000003"),
            actorId: new Guid("60000000-0000-4000-8000-000000000004"),
            actorCreationLineageId: new Guid("60000000-0000-4000-8000-000000000005"),
            actorCreationBranchId: new Guid("60000000-0000-4000-8000-000000000006"),
            tickIndex: 7,
            translation,
            rotation,
            sources,
            extractionPolicy: (byte)RootMotionExtractionPolicy.Extract,
            controller: new WorldAssetReference(assetId: 9, schemaMajor: 1));

    private static RootMotionSourceAuthor CreateSource(
        Guid clipSlotId,
        int clipAssetId,
        long playSequence,
        long loopOrdinal,
        float weight)
        => new(
            clipSlotId,
            new WorldAssetReference(clipAssetId, schemaMajor: 1),
            playSequence,
            loopOrdinal,
            weight);

    [Test]
    public void AbsentAndIdentityPresentSlotsUseDistinctShapes()
    {
        var absent = CreateAbsentSlot();
        var identity = CreatePresentSlot([], Vector3.Zero, Quaternion.Identity);

        Assert.Multiple(() => {
            Assert.That(absent.Present, Is.False);
            Assert.That(absent.PlayerId, Is.Null);
            Assert.That(absent.Sources, Is.Null);
            Assert.That(identity.Present, Is.True);
            Assert.That(identity.PlayerId, Is.Not.Null);
            Assert.That(identity.Sources, Is.Not.Null);
            Assert.That(identity.Sources!.Value, Is.Empty);
            Assert.That(identity.Translation, Is.EqualTo(Vector3.Zero));
            Assert.That(identity.Rotation, Is.EqualTo(Quaternion.Identity));
        });

        _ = Assert.Throws<ArgumentException>(() => new RootMotionSlotAuthor(
            present: false,
            playerId: Guid.NewGuid(),
            playerCreationLineageId: null,
            playerCreationBranchId: null,
            actorId: null,
            actorCreationLineageId: null,
            actorCreationBranchId: null,
            tickIndex: null,
            translation: null,
            rotation: null,
            sources: null,
            extractionPolicy: null,
            controller: null));
    }

    [Test]
    public void PresentSlotCopiesItsOrderedSourceArray()
    {
        var firstSlotId = new Guid("61000000-0000-4000-8000-000000000001");
        var secondSlotId = new Guid("61000000-0000-4000-8000-000000000002");
        RootMotionSourceAuthor[] mutableSources = [
            CreateSource(firstSlotId, clipAssetId: 11, playSequence: 4, loopOrdinal: -1, weight: 0.25f),
            CreateSource(secondSlotId, clipAssetId: 12, playSequence: 5, loopOrdinal: 2, weight: 0.75f),
        ];
        var borrowedArray = ImmutableCollectionsMarshal.AsImmutableArray(mutableSources);
        var slot = CreatePresentSlot(borrowedArray, Vector3.UnitX, Quaternion.Identity);
        mutableSources[0] = CreateSource(secondSlotId, clipAssetId: 99, playSequence: 6, loopOrdinal: 0, weight: 1.0f);

        Assert.Multiple(() => {
            Assert.That(slot.Sources!.Value.Length, Is.EqualTo(2));
            Assert.That(slot.Sources.Value[0].ClipSlotId, Is.EqualTo(firstSlotId));
            Assert.That(slot.Sources.Value[0].Clip.AssetId, Is.EqualTo(11));
            Assert.That(slot.Sources.Value[0].LoopOrdinal, Is.EqualTo(-1));
            Assert.That(slot.Sources.Value[0].Weight, Is.EqualTo(0.25f));
            Assert.That(slot.Sources.Value[1].ClipSlotId, Is.EqualTo(secondSlotId));
        });

        _ = Assert.Throws<ArgumentException>(() => CreatePresentSlot(
            [
                CreateSource(firstSlotId, 11, 4, 0, 0.25f),
                CreateSource(firstSlotId, 12, 5, 0, 0.75f),
            ],
            Vector3.UnitX,
            Quaternion.Identity));
    }

    [Test]
    public void ResourceAuthorRequires4096SlotsAndCopiesItsInput()
    {
        var absent = CreateAbsentSlot();
        var mutableSlots = new RootMotionSlotAuthor[4096];
        Array.Fill(mutableSlots, absent);
        var borrowedArray = ImmutableCollectionsMarshal.AsImmutableArray(mutableSlots);
        var author = new RootMotionResourceAuthor(borrowedArray);
        var replacement = CreateAbsentSlot();
        mutableSlots[0] = replacement;
        var shortValues = ImmutableArray.CreateBuilder<RootMotionSlotAuthor>(4095);

        for (var index = 0; index < shortValues.Capacity; index++)
        {
            shortValues.Add(absent);
        }

        var shortValuesArray = shortValues.MoveToImmutable();

        Assert.Multiple(() => {
            Assert.That(author.Values.Length, Is.EqualTo(4096));
            Assert.That(author.Values[0], Is.SameAs(absent));
            Assert.That(author.Values[0], Is.Not.SameAs(replacement));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new RootMotionResourceAuthor(shortValuesArray));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                new RootMotionResourceAuthor(default));
        });
    }

    [Test]
    public void SourceAuthorRequiresPositiveMajorOneClipAndContribution()
    {
        var source = CreateSource(
            new Guid("62000000-0000-4000-8000-000000000001"),
            clipAssetId: 17,
            playSequence: 2,
            loopOrdinal: -3,
            weight: 0.5f);

        Assert.Multiple(() => {
            Assert.That(source.IsValid, Is.True);
            Assert.That(source.Clip.AssetId, Is.EqualTo(17));
            Assert.That(source.Clip.SchemaMajor, Is.EqualTo(1));
            Assert.That(source.PlaySequence, Is.EqualTo(2));
            Assert.That(source.LoopOrdinal, Is.EqualTo(-3));
            Assert.That(source.Weight, Is.EqualTo(0.5f));
            Assert.That(default(RootMotionSourceAuthor).IsValid, Is.False);
            _ = Assert.Throws<ArgumentException>(() => new RootMotionSourceAuthor(
                new Guid("62000000-0000-4000-8000-000000000002"),
                new WorldAssetReference(17, schemaMajor: 2),
                playSequence: 2,
                loopOrdinal: 0,
                weight: 1.0f));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => CreateSource(
                new Guid("62000000-0000-4000-8000-000000000003"),
                clipAssetId: 17,
                playSequence: 2,
                loopOrdinal: 0,
                weight: 0.0f));
        });
    }
}
