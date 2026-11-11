// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Graphics;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class GraphicsStagingRingAllocatorTests
{
    [Test]
    public void CancelReleasesOnlyUnsubmittedReservations()
    {
        var allocator = new GraphicsStagingRingAllocator(capacityBytes: 8);
        Assert.That(allocator.TryReserve(byteLength: 8, alignment: 1, out var reservation), Is.True);
        allocator.Cancel(reservation);

        Assert.That(allocator.TryReserve(byteLength: 8, alignment: 1, out var replacement), Is.True);
        allocator.MarkSubmitted(replacement, fenceValue: 1);
        _ = Assert.Throws<InvalidOperationException>(() => allocator.Cancel(replacement));
    }

    [Test]
    public void FindsAvailableGapAfterCancelingMiddleRange()
    {
        var allocator = new GraphicsStagingRingAllocator(capacityBytes: 16);
        Assert.That(allocator.TryReserve(byteLength: 4, alignment: 1, out var first), Is.True);
        allocator.MarkSubmitted(first, fenceValue: 1);
        Assert.That(allocator.TryReserve(byteLength: 4, alignment: 1, out var canceled), Is.True);
        allocator.Cancel(canceled);
        Assert.That(allocator.TryReserve(byteLength: 4, alignment: 1, out var third), Is.True);
        allocator.MarkSubmitted(third, fenceValue: 2);
        Assert.That(allocator.TryReserve(byteLength: 4, alignment: 1, out var fourth), Is.True);
        allocator.MarkSubmitted(fourth, fenceValue: 3);

        Assert.That(allocator.TryReserve(byteLength: 4, alignment: 1, out var gap), Is.True);
        Assert.That(gap.Offset, Is.EqualTo(4));
    }

    [Test]
    public void RejectsRangesLargerThanTheRingAndInvalidArguments()
    {
        var allocator = new GraphicsStagingRingAllocator(capacityBytes: 8);

        Assert.That(allocator.TryReserve(byteLength: 9, alignment: 1, out _), Is.False);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => allocator.TryReserve(byteLength: 0, alignment: 1, out _));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => allocator.TryReserve(byteLength: 1, alignment: 0, out _));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => allocator.RetireCompleted(completedFenceValue: -1));
    }

    [Test]
    public void ReservesAlignedRangesAndWrapsAroundLiveFencesWithoutOverlap()
    {
        var allocator = new GraphicsStagingRingAllocator(capacityBytes: 16);
        Assert.That(allocator.TryReserve(byteLength: 6, alignment: 4, out var first), Is.True);
        Assert.That(first.Offset, Is.Zero);
        allocator.MarkSubmitted(first, fenceValue: 1);

        Assert.That(allocator.TryReserve(byteLength: 6, alignment: 2, out var second), Is.True);
        Assert.That(second.Offset, Is.EqualTo(6));
        allocator.MarkSubmitted(second, fenceValue: 2);

        Assert.That(allocator.TryReserve(byteLength: 4, alignment: 1, out var third), Is.True);
        Assert.That(third.Offset, Is.EqualTo(12));
        allocator.MarkSubmitted(third, fenceValue: 3);

        Assert.That(allocator.TryReserve(byteLength: 4, alignment: 1, out _), Is.False);
        Assert.That(allocator.OldestFenceValue, Is.EqualTo(1));

        allocator.RetireCompleted(completedFenceValue: 1);
        Assert.That(allocator.TryReserve(byteLength: 4, alignment: 4, out var wrapped), Is.True);
        Assert.That(wrapped.Offset, Is.Zero);
        allocator.MarkSubmitted(wrapped, fenceValue: 4);

        Assert.That(allocator.TryReserve(byteLength: 1, alignment: 1, out var beforeLiveRange), Is.True);
        Assert.That(beforeLiveRange.Offset, Is.EqualTo(4));
        Assert.That(allocator.TryReserve(byteLength: 1, alignment: 1, out var beforeWrap), Is.True);
        Assert.That(beforeWrap.Offset, Is.EqualTo(5));
        Assert.That(allocator.TryReserve(byteLength: 1, alignment: 1, out _), Is.False);
        Assert.That(allocator.OldestFenceValue, Is.EqualTo(2));
        allocator.RetireCompleted(completedFenceValue: 4);
        Assert.That(allocator.OldestFenceValue, Is.Null);
    }

    [Test]
    public void RetiresSubmittedReservationOnlyAfterItsFenceCompletes()
    {
        var allocator = new GraphicsStagingRingAllocator(capacityBytes: 8);
        Assert.That(allocator.TryReserve(byteLength: 8, alignment: 1, out var reservation), Is.True);
        allocator.MarkSubmitted(reservation, fenceValue: 2);

        _ = Assert.Throws<InvalidOperationException>(
            () => allocator.RetireCompleted(reservation, completedFenceValue: 1));
        Assert.That(allocator.TryReserve(byteLength: 1, alignment: 1, out _), Is.False);

        allocator.RetireCompleted(reservation, completedFenceValue: 2);
        Assert.That(allocator.TryReserve(byteLength: 8, alignment: 1, out _), Is.True);
    }
}
