// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using NUnit.Framework;
using TerraFX;
using TerraFX.Animation;
using TerraFX.Entities;
using TerraFX.Numerics;

namespace TerraFX.Runtime.Tests.Animation;

internal sealed class AnimationRuntimeAuthorTests
{
    private static AnimationActiveClipAuthor CreateClip(Guid clipSlotId, long playSequence)
        => new(
            clipSlotId,
            playSequence,
            0,
            0,
            default,
            0,
            0,
            1.0f,
            0,
            0,
            startDelivered: false,
            completed: false,
            lastDirection: 1);

    private static AnimationPlayerStateAuthor CreatePlayer(Guid actorEntityId)
        => new(
            actorEntityId,
            Guid.NewGuid(),
            new WorldAssetReference(37, 1),
            speed: 1.0f,
            nextPlaySequence: 1,
            lastSampleTick: null,
            [],
            [new AnimationLayerStateAuthor(0, 1.0f, [], transition: null)],
            motionActorId: null);

    private static AnimationLocalPoseAuthor CreatePose()
        => new(Vector3.Zero, Quaternion.Identity, Vector3.One);

    [Test]
    public void AuthorRetainsOrderedImmutablePlaybackStateAndExactFractionBits()
    {
        var fraction = new AnimationTickRemainder192(0x0123456789ABCDEF, 0xFEDCBA9876543210, 0x8000000000000001);
        var clip = new AnimationActiveClipAuthor(
            Guid.NewGuid(),
            playSequence: 2,
            localTicks: 7,
            previousTicks: 5,
            fraction,
            loopOrdinal: -3,
            previousLoopOrdinal: -2,
            weight: BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)),
            forwardCursor: 4,
            reverseCursor: 3,
            startDelivered: true,
            completed: false,
            lastDirection: -1);
        var layer = new AnimationLayerStateAuthor(0, 1.0f, [clip], transition: null);
        var playerWithClip = new AnimationPlayerStateAuthor(
            new Guid("10000000-0000-4000-8000-000000000001"),
            Guid.NewGuid(),
            new WorldAssetReference(37, 1),
            speed: -0.0f,
            nextPlaySequence: 3,
            lastSampleTick: 11,
            [],
            [layer],
            motionActorId: null);
        var pose = CreatePose();
        var target = new AnimationTargetBindingAuthor(
            domain: 0,
            Guid.NewGuid(),
            Guid.NewGuid(),
            pose,
            rootBaseline: null);
        var transitionedLayer = new AnimationLayerStateAuthor(
            1,
            0.5f,
            [],
            new AnimationTransitionStateAuthor(
                Guid.NewGuid(),
                elapsedTicks: 9,
                fraction: 17,
                [0.25f, 0.75f],
                Guid.NewGuid()));
        var transitionPlayer = new AnimationPlayerStateAuthor(
            new Guid("20000000-0000-4000-8000-000000000002"),
            Guid.NewGuid(),
            new WorldAssetReference(38, 1),
            speed: 0.0f,
            nextPlaySequence: 1,
            lastSampleTick: null,
            [target],
            [new AnimationLayerStateAuthor(0, 1.0f, [], transition: null), transitionedLayer],
            motionActorId: null);
        var orderedStatePlayers = ImmutableArray.Create(playerWithClip, transitionPlayer);
        var state = new AnimationRuntimeState(new AnimationRuntimeAuthor(1, orderedStatePlayers));
        Span<byte> fractionBytes = stackalloc byte[AnimationTickRemainder192.ByteLength];
        fraction.WriteLittleEndian(fractionBytes);
        var fractionLastByte = fractionBytes[23];

        Assert.Multiple(() => {
            Assert.That(state.Value.NextCommandSequence, Is.EqualTo(1));
            Assert.That(state.Value.Players, Is.EqualTo(orderedStatePlayers));
            Assert.That(state.Value.Players[0].Layers[0].Clips[0].Fraction, Is.EqualTo(fraction));
            Assert.That(BitConverter.SingleToInt32Bits(state.Value.Players[0].Speed), Is.EqualTo(unchecked((int)0x80000000)));
            Assert.That(BitConverter.SingleToInt32Bits(state.Value.Players[0].Layers[0].Clips[0].Weight), Is.EqualTo(unchecked((int)0x80000000)));
            Assert.That(fractionLastByte, Is.EqualTo(0x80));
            Assert.That(state.Value.Players[1].Bindings[0].Baseline, Is.SameAs(pose));
            Assert.That(state.Value.Players[1].Layers[1].Transition?.SourceWeights, Is.EqualTo(ImmutableArray.Create(0.25f, 0.75f)));
        });
    }

    [Test]
    public void ConstructorsRejectInvalidBoundsAndNoncanonicalPlayerMembership()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AnimationRuntimeAuthor(0, []));
        _ = Assert.Throws<ArgumentException>(() => new AnimationRuntimeAuthor(1, default));
        var duplicateId = Guid.NewGuid();
        _ = Assert.Throws<ArgumentException>(() => new AnimationRuntimeAuthor(
            1,
            [CreatePlayer(duplicateId), CreatePlayer(duplicateId)]));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AnimationPlayerStateAuthor(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new WorldAssetReference(37, 1),
            speed: float.NaN,
            nextPlaySequence: 1,
            lastSampleTick: null,
            [],
            [new AnimationLayerStateAuthor(0, 1.0f, [], transition: null)],
            motionActorId: null));
        _ = Assert.Throws<ArgumentException>(() => new AnimationTargetBindingAuthor(
            0,
            Guid.NewGuid(),
            entityId: null,
            CreatePose(),
            rootBaseline: null));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AnimationActiveClipAuthor(
            Guid.NewGuid(),
            playSequence: 1,
            localTicks: 0,
            previousTicks: 0,
            default,
            loopOrdinal: 0,
            previousLoopOrdinal: 0,
            weight: 1.0f,
            forwardCursor: 1_048_577,
            reverseCursor: 0,
            startDelivered: false,
            completed: false,
            lastDirection: 1));
        _ = Assert.Throws<ArgumentException>(() => new AnimationLayerStateAuthor(
            0,
            1.0f,
            [CreateClip(duplicateId, 1), CreateClip(duplicateId, 2)],
            transition: null));
    }

    [Test]
    public void LocalPoseRejectsInvalidGeometryWithoutNormalizingAcceptedValues()
    {
        var rotation = new Quaternion(0.0f, 0.0f, 0.5f, 0.5f);
        var pose = new AnimationLocalPoseAuthor(new Vector3(1.0f, 2.0f, 3.0f), rotation, Vector3.One);

        Assert.Multiple(() => {
            Assert.That(pose.Position, Is.EqualTo(new Vector3(1.0f, 2.0f, 3.0f)));
            Assert.That(pose.Rotation, Is.EqualTo(rotation));
        });

        _ = Assert.Throws<ArgumentException>(() => new AnimationLocalPoseAuthor(
            Vector3.Zero,
            default,
            Vector3.One));
        _ = Assert.Throws<ArgumentException>(() => new AnimationLocalPoseAuthor(
            Vector3.Zero,
            Quaternion.Identity,
            new Vector3(0.00001f, 1.0f, 1.0f)));
    }
}
