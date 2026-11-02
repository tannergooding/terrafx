// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;
using NUnit.Framework;
using TerraFX.Hosting;
using TerraFX.Input;
using TerraFX.Platform;

namespace TerraFX.Runtime.Tests.Hosting;

[TestFixture]
internal sealed class PlatformInputBrokerTests
{
    [Test]
    public void IssuesAndRetiresHostDeviceGenerationsWithStableEventIdentity()
    {
        var broker = new PlatformInputBroker(17, new IncrementingTimeProvider());
        var nativeDevice = (nint)71;
        var samples = ImmutableArray.Create(
            new PlatformInputSample(
                PhysicalInputEventKind.DeviceAdded,
                null,
                nativeDevice,
                PlatformInputDeviceKind.Unknown,
                NativeTimestamp: 10,
                NativeClockDomain: "test-clock",
                MonotonicTimestamp: 2),
            new PlatformInputSample(
                PhysicalInputEventKind.DeviceRemoved,
                null,
                nativeDevice,
                PlatformInputDeviceKind.Unknown,
                NativeTimestamp: 11,
                NativeClockDomain: "test-clock",
                MonotonicTimestamp: 2),
            new PlatformInputSample(
                PhysicalInputEventKind.DeviceAdded,
                null,
                nativeDevice,
                PlatformInputDeviceKind.Unknown,
                NativeTimestamp: 12,
                NativeClockDomain: "test-clock",
                MonotonicTimestamp: 3));

        var captured = broker.Capture(new PlatformInputSampleBatch(samples, DiscardedSampleCount: 2), static _ => true);

        Assert.That(captured.Events, Has.Length.EqualTo(3));
        Assert.That(captured.DiscardedSampleCount, Is.EqualTo(2));
        Assert.That(captured.Overflowed, Is.True);

        var added = captured.Events[0];
        var removed = captured.Events[1];
        var replacement = captured.Events[2];

        Assert.That(added.Kind, Is.EqualTo(PhysicalInputEventKind.DeviceAdded));
        Assert.That(removed.Kind, Is.EqualTo(PhysicalInputEventKind.DeviceRemoved));
        Assert.That(replacement.Kind, Is.EqualTo(PhysicalInputEventKind.DeviceAdded));
        Assert.That(added.Device, Is.EqualTo(removed.Device));
        Assert.That(replacement.Device?.OwnerId, Is.EqualTo(17));
        Assert.That(replacement.Device?.Index, Is.EqualTo(added.Device?.Index));
        Assert.That(replacement.Device?.Generation, Is.EqualTo(added.Device?.Generation + 1));
        Assert.That(added.HostSequence, Is.LessThan(removed.HostSequence));
        Assert.That(removed.HostSequence, Is.LessThan(replacement.HostSequence));
        Assert.That(added.HostTimestamp, Is.EqualTo(removed.HostTimestamp));
        Assert.That(removed.HostTimestamp, Is.LessThan(replacement.HostTimestamp));
        Assert.That(added.HostTimestamp, Is.EqualTo(1));
        Assert.That(removed.HostTimestamp, Is.EqualTo(1));
        Assert.That(replacement.HostTimestamp, Is.EqualTo(2));
        Assert.That(added.NativeTimestamp, Is.EqualTo(10));
        Assert.That(removed.NativeTimestamp, Is.EqualTo(11));
        broker.Retire();
    }

    private sealed class IncrementingTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Increment(ref _timestamp);
    }
}
