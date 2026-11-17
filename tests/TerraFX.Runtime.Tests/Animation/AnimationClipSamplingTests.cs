// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using NUnit.Framework;
using TerraFX.Assets;
using TerraFX.Animation;
using TerraFX.Numerics;

namespace TerraFX.Runtime.Tests.Animation;

internal sealed class AnimationClipSamplingTests
{
    private static AnimationClipAsset CreateClip(
        long durationTicks,
        AnimationClipAsset.AnimationTrack track)
        => new(
            new AssetId(1),
            durationTicks,
            loopMode: 0,
            skeletonAssetId: null,
            sceneAssetId: null,
            rootMotion: null,
            bounds: null,
            boundsQualification: 0,
            boundsMargin: 0.0f,
            [track],
            [],
            [(byte)1]);

    [Test]
    public void CubicQuaternionSampleNormalizesAndRejectsNearZero()
    {
        var targetId = Guid.NewGuid();
        var clip = CreateClip(
            1_000,
            new AnimationClipAsset.AnimationTrack(
                targetId,
                1,
                1,
                2,
                [],
                [
                    new AnimationClipAsset.QuaternionKey(
                        0,
                        Quaternion.Identity,
                        Vector4.Zero,
                        Vector4.Zero),
                    new AnimationClipAsset.QuaternionKey(
                        1_000,
                        -Quaternion.Identity,
                        Vector4.Zero,
                        Vector4.Zero),
                ],
                []));

        _ = Assert.Throws<InvalidDataException>(
            () => AnimationClipSamplingKernels.SampleQuaternion(clip, clip.Tracks[0], 500));
    }

    [Test]
    [TestCase(-4.0f, 4.0f, true)]
    [TestCase(-10000.0f, 10000.0f, true)]
    [TestCase(-8.0f, 8.0f, false)]
    public void CubicScaleSampleValidatesComponentMagnitudes(
        float firstOutTangentX,
        float secondInTangentX,
        bool shouldReject)
    {
        var targetId = Guid.NewGuid();
        var clip = CreateClip(
            TimeSpan.TicksPerSecond,
            new AnimationClipAsset.AnimationTrack(
                targetId,
                0,
                2,
                2,
                [
                    new AnimationClipAsset.Vector3Key(
                        0,
                        Vector3.One,
                        Vector3.Zero,
                        new Vector3(firstOutTangentX, 0.0f, 0.0f)),
                    new AnimationClipAsset.Vector3Key(
                        TimeSpan.TicksPerSecond,
                        Vector3.One,
                        new Vector3(secondInTangentX, 0.0f, 0.0f),
                        Vector3.Zero),
                ],
                [],
                []));

        if (shouldReject)
        {
            _ = Assert.Throws<InvalidDataException>(
                () => AnimationClipSamplingKernels.SampleVector3(clip, clip.Tracks[0], TimeSpan.TicksPerSecond / 2));
        }
        else
        {
            Assert.That(
                AnimationClipSamplingKernels.SampleVector3(clip, clip.Tracks[0], TimeSpan.TicksPerSecond / 2),
                Is.EqualTo(new Vector3(-1.0f, 1.0f, 1.0f)));
        }
    }

    [Test]
    public void CubicVectorSampleScalesTangentsBySegmentSeconds()
    {
        var targetId = Guid.NewGuid();
        var clip = CreateClip(
            TimeSpan.TicksPerSecond,
            new AnimationClipAsset.AnimationTrack(
                targetId,
                0,
                0,
                2,
                [
                    new AnimationClipAsset.Vector3Key(
                        0,
                        Vector3.Zero,
                        Vector3.Zero,
                        new Vector3(2.0f, 0.0f, 0.0f)),
                    new AnimationClipAsset.Vector3Key(
                        TimeSpan.TicksPerSecond,
                        Vector3.UnitX,
                        Vector3.Zero,
                        Vector3.Zero),
                ],
                [],
                []));

        var sampled = AnimationClipSamplingKernels.SampleVector3(clip, clip.Tracks[0], TimeSpan.TicksPerSecond / 2);

        Assert.That(sampled, Is.EqualTo(new Vector3(0.75f, 0.0f, 0.0f)));
    }

    [Test]
    public void LinearQuaternionSampleUsesTheShortestArc()
    {
        var targetId = Guid.NewGuid();
        var halfAngle = MathF.Sqrt(0.5f);
        var clip = CreateClip(
            1_000,
            new AnimationClipAsset.AnimationTrack(
                targetId,
                1,
                1,
                1,
                [],
                [
                    new AnimationClipAsset.QuaternionKey(0, Quaternion.Identity, null, null),
                    new AnimationClipAsset.QuaternionKey(
                        1_000,
                        new Quaternion(0.0f, 0.0f, -halfAngle, -halfAngle),
                        null,
                        null),
                ],
                []));

        var sampled = AnimationClipSamplingKernels.SampleQuaternion(clip, clip.Tracks[0], 500);

        Assert.Multiple(() => {
            Assert.That(sampled.X, Is.EqualTo(0.0f).Within(1.0e-6f));
            Assert.That(sampled.Y, Is.EqualTo(0.0f).Within(1.0e-6f));
            Assert.That(sampled.Z, Is.EqualTo(MathF.Sin(float.Pi / 8.0f)).Within(1.0e-6f));
            Assert.That(sampled.W, Is.EqualTo(MathF.Cos(float.Pi / 8.0f)).Within(1.0e-6f));
        });
    }

    [Test]
    public void LinearVectorSampleInterpolatesAndHoldsTheFirstKeyBeforeItsTime()
    {
        var targetId = Guid.NewGuid();
        var clip = CreateClip(
            30,
            new AnimationClipAsset.AnimationTrack(
                targetId,
                0,
                0,
                1,
                [
                    new AnimationClipAsset.Vector3Key(10, new Vector3(2.0f, 0.0f, 0.0f), null, null),
                    new AnimationClipAsset.Vector3Key(20, new Vector3(4.0f, 0.0f, 0.0f), null, null),
                ],
                [],
                []));

        Assert.Multiple(() => {
            Assert.That(
                AnimationClipSamplingKernels.SampleVector3(clip, clip.Tracks[0], 0),
                Is.EqualTo(new Vector3(2.0f, 0.0f, 0.0f)));
            Assert.That(
                AnimationClipSamplingKernels.SampleVector3(clip, clip.Tracks[0], 15),
                Is.EqualTo(new Vector3(3.0f, 0.0f, 0.0f)));
        });
    }

    [Test]
    public void StepVectorAndSpriteSamplesReturnThePriorKeyAtTheExactBoundary()
    {
        var targetId = Guid.NewGuid();
        var vectorClip = CreateClip(
            20,
            new AnimationClipAsset.AnimationTrack(
                targetId,
                1,
                0,
                0,
                [
                    new AnimationClipAsset.Vector3Key(0, Vector3.Zero, null, null),
                    new AnimationClipAsset.Vector3Key(10, Vector3.UnitX, null, null),
                    new AnimationClipAsset.Vector3Key(20, Vector3.UnitY, null, null),
                ],
                [],
                []));
        var atlasAssetId = new AssetId(2);
        var firstFrameId = Guid.NewGuid();
        var secondFrameId = Guid.NewGuid();
        var spriteClip = CreateClip(
            10,
            new AnimationClipAsset.AnimationTrack(
                targetId,
                0,
                3,
                0,
                [],
                [],
                [
                    new AnimationClipAsset.SpriteKey(0, atlasAssetId, firstFrameId),
                    new AnimationClipAsset.SpriteKey(10, atlasAssetId, secondFrameId),
                ]));

        Assert.Multiple(() => {
            Assert.That(
                AnimationClipSamplingKernels.SampleVector3(vectorClip, vectorClip.Tracks[0], 10),
                Is.EqualTo(Vector3.Zero));
            Assert.That(
                AnimationClipSamplingKernels.SampleSprite(spriteClip, spriteClip.Tracks[0], 10).FrameId,
                Is.EqualTo(firstFrameId));
        });
    }
}
