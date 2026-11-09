// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Input;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class PlatformInputSampleQueueTests
{
    [Test]
    public void PreservesCaptureOrderAcrossPhysicalAndTextSamples()
    {
        var queue = new PlatformInputSampleQueue(Environment.CurrentManagedThreadId);
        var window = new WindowHandle(17, 0, 1);
        var physical = new PlatformInputSample(
            PhysicalInputEventKind.FocusGained,
            window,
            NativeDeviceIdentity: 0,
            PlatformInputDeviceKind.Unknown,
            MonotonicTimestamp: 1);
        var text = new PlatformTextInputSample(
            window,
            [new TextInputChange(TextInputChangeKind.CompositionStarted)],
            MonotonicTimestamp: 2);

        Assert.That(queue.TryEnqueue(in physical), Is.True);
        Assert.That(queue.TryEnqueueTextInput(in text), Is.True);

        var batch = queue.Drain();
        Assert.That(batch.Samples[0].CaptureOrder, Is.EqualTo(1));
        Assert.That(batch.TextSamples[0].CaptureOrder, Is.EqualTo(2));
    }

    [Test]
    public void RejectsDefaultSample()
    {
        var queue = new PlatformInputSampleQueue(Environment.CurrentManagedThreadId);

        Assert.That(() => queue.TryEnqueue(default), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ReportsBoundedTextOverflowWithoutLosingPhysicalInput()
    {
        var queue = new PlatformInputSampleQueue(Environment.CurrentManagedThreadId, capacity: 1);
        var window = new WindowHandle(17, 0, 1);
        var physical = new PlatformInputSample(
            PhysicalInputEventKind.DeviceAdded,
            null,
            (nint)1,
            PlatformInputDeviceKind.Unknown);
        var text = new PlatformTextInputSample(
            window,
            [new TextInputChange(TextInputChangeKind.CompositionStarted)],
            MonotonicTimestamp: 1,
            SessionId: 42,
            BaseDocumentRevision: 1);

        Assert.That(queue.TryEnqueue(in physical), Is.True);
        Assert.That(queue.TryEnqueueTextInput(in text), Is.True);
        Assert.That(queue.TryEnqueueTextInput(in text), Is.False);

        var batch = queue.Drain();
        Assert.That(batch.Samples, Has.Length.EqualTo(1));
        Assert.That(batch.DiscardedSampleCount, Is.Zero);
        Assert.That(batch.DiscardedTextSampleCount, Is.EqualTo(1));
        Assert.That(batch.TextOverflows, Is.EqualTo([new PlatformTextInputOverflow(window, 42, 1)]));
        Assert.That(batch.AllTextInputSamplesOverflowed, Is.False);
        Assert.That(batch.TextSamples, Has.Length.EqualTo(1));
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
        Assert.That(batch.Samples, Is.EqualTo([
            first with { CaptureOrder = 1 },
            second with { CaptureOrder = 2 },
        ]));
        Assert.That(batch.DiscardedSampleCount, Is.EqualTo(1));
        Assert.That(batch.Overflowed, Is.True);

        var emptyBatch = queue.Drain();
        Assert.That(emptyBatch.Samples, Is.Empty);
        Assert.That(emptyBatch.DiscardedSampleCount, Is.Zero);
        Assert.That(emptyBatch.Overflowed, Is.False);
    }

    [Test]
    public void ReportsTextPayloadBudgetOverflowWithoutTruncation()
    {
        var queue = new PlatformInputSampleQueue(Environment.CurrentManagedThreadId);
        var window = new WindowHandle(17, 0, 1);
        var text = new string('x', 4096);
        var sample = new PlatformTextInputSample(
            window,
            [new TextInputChange(TextInputChangeKind.TextCommitted, text)],
            MonotonicTimestamp: 1);

        for (var index = 0; index < 64; index++)
        {
            Assert.That(queue.TryEnqueueTextInput(in sample), Is.True);
        }

        Assert.That(queue.TryEnqueueTextInput(in sample), Is.False);

        var batch = queue.Drain();
        Assert.That(batch.TextSamples, Has.Length.EqualTo(64));
        Assert.That(batch.DiscardedTextSampleCount, Is.EqualTo(1));
        Assert.That(batch.TextOverflows, Is.EqualTo([new PlatformTextInputOverflow(window, null, 1)]));
    }
}
