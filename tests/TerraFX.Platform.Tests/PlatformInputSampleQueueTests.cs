// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Input;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class PlatformInputSampleQueueTests
{
    [Test]
    public void RejectsDefaultSample()
    {
        var queue = new PlatformInputSampleQueue(Environment.CurrentManagedThreadId);

        Assert.That(() => queue.TryEnqueue(default), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ReportsCapacityLossAndPreservesFifoOrder()
    {
        var queue = new PlatformInputSampleQueue(Environment.CurrentManagedThreadId, capacity: 2);
        var first = new PlatformInputSample(
            PhysicalInputEventKind.DeviceAdded,
            null,
            (nint)1,
            PlatformInputDeviceKind.Unknown);
        var second = first with {
            NativeDeviceIdentity = (nint)2,
        };
        var discarded = first with {
            NativeDeviceIdentity = (nint)3,
        };

        Assert.That(queue.TryEnqueue(in first), Is.True);
        Assert.That(queue.TryEnqueue(in second), Is.True);
        Assert.That(queue.TryEnqueue(in discarded), Is.False);

        var batch = queue.Drain();
        Assert.That(batch.Samples, Is.EqualTo([first, second]));
        Assert.That(batch.DiscardedSampleCount, Is.EqualTo(1));
        Assert.That(batch.Overflowed, Is.True);

        var emptyBatch = queue.Drain();
        Assert.That(emptyBatch.Samples, Is.Empty);
        Assert.That(emptyBatch.DiscardedSampleCount, Is.Zero);
        Assert.That(emptyBatch.Overflowed, Is.False);
    }
}
