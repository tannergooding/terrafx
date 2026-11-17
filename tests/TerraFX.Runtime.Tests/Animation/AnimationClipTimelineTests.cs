// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Animation;
using TerraFX.Assets;

namespace TerraFX.Runtime.Tests.Animation;

internal sealed class AnimationClipTimelineTests
{
    private static AnimationClipAsset CreateTimelineClip(long durationTicks, byte loopMode)
        => new(
            new AssetId(1),
            durationTicks,
            loopMode,
            skeletonAssetId: null,
            sceneAssetId: null,
            rootMotion: null,
            bounds: null,
            boundsQualification: 0,
            boundsMargin: 0.0f,
            [],
            [],
            [0x01]);

    [Test]
    public void ClampTimelineCompletesOncePerPlay()
    {
        var timeline = new AnimationClipTimeline(CreateTimelineClip(durationTicks: 100, loopMode: 0));

        Assert.That(timeline.Advance(speed: 4.0f, ticksPerSecond: 1), Is.True);
        Assert.That(timeline.CurrentTimeTicks, Is.EqualTo(100));
        Assert.That(timeline.IsCompleted, Is.True);
        Assert.That(timeline.Advance(speed: 4.0f, ticksPerSecond: 1), Is.False);

        timeline.Seek(0);
        Assert.That(timeline.Advance(speed: 4.0f, ticksPerSecond: 1), Is.False);

        timeline.ResetForPlay();
        Assert.That(timeline.IsCompleted, Is.False);
        Assert.That(timeline.CurrentTimeTicks, Is.Zero);
        Assert.That(timeline.Advance(speed: 4.0f, ticksPerSecond: 1), Is.True);
    }

    [Test]
    public void DefaultTimelineRejectsUse()
    {
        var timeline = default(AnimationClipTimeline);

        Assert.That(timeline.IsValid, Is.False);
        _ = Assert.Throws<InvalidOperationException>(() => timeline.Advance(speed: 1.0f, ticksPerSecond: 60));
        _ = Assert.Throws<InvalidOperationException>(() => timeline.ResetForPlay());
        _ = Assert.Throws<InvalidOperationException>(() => timeline.Seek(0));
    }

    [Test]
    public void LoopTimelineCarriesExactFractionAndReversesAcrossZero()
    {
        var clip = CreateTimelineClip(durationTicks: 100, loopMode: 1);
        var forward = new AnimationClipTimeline(clip);
        var reverse = new AnimationClipTimeline(clip);

        for (var index = 0; index < 60; index++)
        {
            Assert.That(forward.Advance(speed: 1.0f, ticksPerSecond: 60), Is.False);
            Assert.That(reverse.Advance(speed: -1.0f, ticksPerSecond: 60), Is.False);
        }

        Assert.Multiple(() => {
            Assert.That(forward.CurrentLoopOrdinal, Is.EqualTo(100_000));
            Assert.That(forward.CurrentTimeTicks, Is.Zero);
            Assert.That(forward.Remainder, Is.EqualTo(default(AnimationTickRemainder192)));
            Assert.That(forward.LastDirection, Is.EqualTo((sbyte)1));
            Assert.That(reverse.CurrentLoopOrdinal, Is.EqualTo(-100_000));
            Assert.That(reverse.CurrentTimeTicks, Is.Zero);
            Assert.That(reverse.Remainder, Is.EqualTo(default(AnimationTickRemainder192)));
            Assert.That(reverse.LastDirection, Is.EqualTo((sbyte)-1));
        });
    }

    [Test]
    public void SeekUsesQuotientRemainderAndResetsFraction()
    {
        var timeline = new AnimationClipTimeline(CreateTimelineClip(durationTicks: 100, loopMode: 1));
        _ = timeline.Advance(speed: 1.0f, ticksPerSecond: 60);
        var previousLoop = timeline.CurrentLoopOrdinal;
        var previousTicks = timeline.CurrentTimeTicks;

        timeline.Seek(long.MaxValue);

        Assert.Multiple(() => {
            Assert.That(timeline.CurrentLoopOrdinal, Is.EqualTo(long.MaxValue / 100));
            Assert.That(timeline.CurrentTimeTicks, Is.EqualTo(long.MaxValue % 100));
            Assert.That(timeline.PreviousLoopOrdinal, Is.EqualTo(previousLoop));
            Assert.That(timeline.PreviousTimeTicks, Is.EqualTo(previousTicks));
            Assert.That(timeline.Remainder, Is.EqualTo(default(AnimationTickRemainder192)));
        });
    }
}
