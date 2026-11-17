// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using NUnit.Framework;
using TerraFX;
using TerraFX.Animation;
using TerraFX.Assets;

namespace TerraFX.Runtime.Tests.Animation;

internal sealed class AnimationClipEventKernelsTests
{
    private static AnimationClipAsset CreateClip(
        byte loopMode,
        params (Guid EventId, long TimeTicks)[] eventKeys)
    {
        var payloadSchema = new ContentSchemaReference(
            new ContentSchemaIdentityAssignment("native/event-marker").SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var events = ImmutableArray.CreateBuilder<AnimationClipAsset.AnimationClipEvent>(eventKeys.Length);

        foreach (var (eventId, timeTicks) in eventKeys)
        {
            events.Add(new AnimationClipAsset.AnimationClipEvent(
                eventId,
                timeTicks,
                payloadSchema,
                [0x01]));
        }

        return new AnimationClipAsset(
            new AssetId(1),
            durationTicks: 1_000,
            loopMode: loopMode,
            skeletonAssetId: null,
            sceneAssetId: null,
            rootMotion: null,
            bounds: null,
            boundsQualification: 0,
            boundsMargin: 0.0f,
            [],
            events.MoveToImmutable(),
            [0x01]);
    }

    [Test]
    public void ClampIntervalsUseForwardAndReverseHalfOpenBoundaries()
    {
        var eventAtZero = new Guid("30000000-0000-4000-8000-000000000001");
        var eventAtQuarter = new Guid("30000000-0000-4000-8000-000000000002");
        var eventAtHalf = new Guid("30000000-0000-4000-8000-000000000003");
        var eventAtEnd = new Guid("30000000-0000-4000-8000-000000000004");
        var clip = CreateClip(
            loopMode: 0,
            (eventAtZero, 0),
            (eventAtQuarter, 250),
            (eventAtHalf, 500),
            (eventAtEnd, 1_000));
        var output = new AnimationClipEventKernels.Occurrence[1];

        var forwardCount = AnimationClipEventKernels.WriteCrossedEvents(
            clip,
            firstLoopOrdinal: 0,
            firstTimeTicks: 250,
            secondLoopOrdinal: 0,
            secondTimeTicks: 500,
            output);
        var forwardEventId = output[0].Event.EventId;
        var reverseCount = AnimationClipEventKernels.WriteCrossedEvents(
            clip,
            firstLoopOrdinal: 0,
            firstTimeTicks: 500,
            secondLoopOrdinal: 0,
            secondTimeTicks: 250,
            output);

        Assert.Multiple(() => {
            Assert.That(forwardCount, Is.EqualTo(1));
            Assert.That(forwardEventId, Is.EqualTo(eventAtHalf));
            Assert.That(output[0].Direction, Is.EqualTo((sbyte)-1));
            Assert.That(reverseCount, Is.EqualTo(1));
            Assert.That(output[0].Event.EventId, Is.EqualTo(eventAtQuarter));
        });
    }

    [Test]
    public void CountRefusesA4097thCrossedEventWithoutMaterializing()
    {
        var markerId = new Guid("30000000-0000-4000-8000-000000000011");
        var clip = CreateClip(loopMode: 1, (markerId, 0));

        Assert.Multiple(() => {
            Assert.That(
                AnimationClipEventKernels.TryCountCrossedEvents(
                    clip,
                    firstLoopOrdinal: 0,
                    firstTimeTicks: 0,
                    secondLoopOrdinal: 4_096,
                    secondTimeTicks: 0,
                    maximumCount: 4_096,
                    out var admittedCount),
                Is.True);
            Assert.That(admittedCount, Is.EqualTo(4_096));
            Assert.That(
                AnimationClipEventKernels.TryCountCrossedEvents(
                    clip,
                    firstLoopOrdinal: 0,
                    firstTimeTicks: 0,
                    secondLoopOrdinal: 4_097,
                    secondTimeTicks: 0,
                    maximumCount: 4_096,
                    out var refusedCount),
                Is.False);
            Assert.That(refusedCount, Is.Zero);
        });
    }

    [Test]
    public void LoopCrossingOrdersEndBeforeStartForwardAndStartBeforeEndReverse()
    {
        var eventAtZero = new Guid("30000000-0000-4000-8000-000000000021");
        var eventAtHalf = new Guid("30000000-0000-4000-8000-000000000022");
        var eventAtEnd = new Guid("30000000-0000-4000-8000-000000000023");
        var clip = CreateClip(
            loopMode: 1,
            (eventAtZero, 0),
            (eventAtHalf, 500),
            (eventAtEnd, 1_000));
        var output = new AnimationClipEventKernels.Occurrence[3];

        var forwardCount = AnimationClipEventKernels.WriteCrossedEvents(
            clip,
            firstLoopOrdinal: 0,
            firstTimeTicks: 500,
            secondLoopOrdinal: 1,
            secondTimeTicks: 500,
            output);
        var forwardEventIds = new[] {
            output[0].Event.EventId,
            output[1].Event.EventId,
            output[2].Event.EventId,
        };
        var reverseCount = AnimationClipEventKernels.WriteCrossedEvents(
            clip,
            firstLoopOrdinal: 1,
            firstTimeTicks: 500,
            secondLoopOrdinal: 0,
            secondTimeTicks: 500,
            output);
        var reverseEventIds = new[] {
            output[0].Event.EventId,
            output[1].Event.EventId,
            output[2].Event.EventId,
        };

        Assert.Multiple(() => {
            Assert.That(forwardCount, Is.EqualTo(3));
            Assert.That(forwardEventIds, Is.EqualTo([eventAtEnd, eventAtZero, eventAtHalf]));
            Assert.That(output[0].LoopOrdinal, Is.EqualTo(1));
            Assert.That(reverseCount, Is.EqualTo(3));
            Assert.That(reverseEventIds, Is.EqualTo([eventAtZero, eventAtEnd, eventAtHalf]));
            Assert.That(output[0].LoopOrdinal, Is.EqualTo(1));
            Assert.That(output[0].Direction, Is.EqualTo((sbyte)-1));
        });
    }
}
