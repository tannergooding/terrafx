// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX.Graphics;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class GraphicsOwnerRequestQueueTests
{
    [Test]
    public void DiagnosticRequestEntryHasFixedBackingAndNoPerWriteAllocation()
    {
        using var queue = new GraphicsOwnerRequestQueue();
        var work = new TestWork();
        Assert.That(queue.RequestStorageCapacity, Is.EqualTo(256));
        Assert.That(queue.RequestBackingByteCharge, Is.EqualTo(64 + (256 * IntPtr.Size)));
        Assert.That(queue.RequestBackingByteCharge, Is.EqualTo(GraphicsOwnerRequestQueue.KnownRequestBackingByteCharge));
        Assert.That(queue.TryWriteDiagnosticRequest(work, queue.RequestBackingByteCharge - 1), Is.False);
        Assert.That(queue.TryReadRequest(out _), Is.False);
        Assert.That(queue.TryWriteDiagnosticRequest(work, queue.RequestBackingByteCharge), Is.True);
        Assert.That(queue.TryReadRequest(out _), Is.True);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var accepted = queue.TryWriteDiagnosticRequest(work, queue.RequestBackingByteCharge);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(accepted, Is.True);
        Assert.That(allocated, Is.Zero);
        Assert.That(queue.TryReadRequest(out var observed), Is.True);
        Assert.That(observed, Is.SameAs(work));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task DiagnosticRequestRefusalAllocatesNoWaiterOrQueueStorage(bool full, bool closed)
    {
        using var queue = new GraphicsOwnerRequestQueue();
        var retained = new TestWork[full ? 256 : 0];

        for (var index = 0; index < retained.Length; index++)
        {
            retained[index] = new TestWork();
            await queue.WriteRequestAsync(retained[index], CancellationToken.None).ConfigureAwait(false);
        }

        if (closed)
        {
            queue.CompleteRequests();
        }

        var work = new TestWork();
        var maximumBackingBytes = (full || closed) ? queue.RequestBackingByteCharge : queue.RequestBackingByteCharge - 1;
        Assert.That(queue.TryWriteDiagnosticRequest(work, maximumBackingBytes), Is.False);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var accepted = queue.TryWriteDiagnosticRequest(work, maximumBackingBytes);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(accepted, Is.False);
        Assert.That(allocated, Is.Zero);

        foreach (var name in new[] { "_pendingRequestHead", "_pendingRequestTail" })
        {
            var field = typeof(GraphicsOwnerRequestQueue).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("The queue's pending-writer owner field was not found.");
            Assert.That(field.GetValue(queue), Is.Null);
        }

        for (var index = 0; index < retained.Length; index++)
        {
            Assert.That(queue.TryReadRequest(out var observed), Is.True);
            Assert.That(observed, Is.SameAs(retained[index]));
        }

        Assert.That(queue.TryReadRequest(out _), Is.False);
    }

    [Test]
    public async Task DiagnosticRequestRefusalCannotOvertakePendingOrdinaryWriters()
    {
        using var queue = new GraphicsOwnerRequestQueue();
        var entries = new TestWork[258];

        for (var index = 0; index < entries.Length; index++)
        {
            entries[index] = new TestWork();
        }

        for (var index = 0; index < 256; index++)
        {
            await queue.WriteRequestAsync(entries[index], CancellationToken.None).ConfigureAwait(false);
        }

        var first = queue.WriteRequestAsync(entries[256], CancellationToken.None).AsTask();
        var second = queue.WriteRequestAsync(entries[257], CancellationToken.None).AsTask();
        var diagnostic = new TestWork();
        Assert.That(queue.TryWriteDiagnosticRequest(diagnostic, queue.RequestBackingByteCharge), Is.False);
        Assert.That(queue.TryReadRequest(out var firstRead), Is.True);
        Assert.That(firstRead, Is.SameAs(entries[0]));
        await first.ConfigureAwait(false);
        Assert.That(second.IsCompleted, Is.False);
        Assert.That(queue.TryWriteDiagnosticRequest(diagnostic, queue.RequestBackingByteCharge), Is.False);
        Assert.That(queue.TryReadRequest(out var secondRead), Is.True);
        Assert.That(secondRead, Is.SameAs(entries[1]));
        await second.ConfigureAwait(false);

        for (var index = 2; index < entries.Length; index++)
        {
            Assert.That(queue.TryReadRequest(out var observed), Is.True);
            Assert.That(observed, Is.SameAs(entries[index]));
        }

        Assert.That(queue.TryWriteDiagnosticRequest(diagnostic, queue.RequestBackingByteCharge), Is.True);
        Assert.That(queue.TryReadRequest(out var last), Is.True);
        Assert.That(last, Is.SameAs(diagnostic));
    }

    [Test]
    public void DiagnosticRequestRefusesAContendedQueueGate()
    {
        using var queue = new GraphicsOwnerRequestQueue();
        var field = typeof(GraphicsOwnerRequestQueue).GetField("_retirementGate", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The queue's existing owner gate was not found.");
        var gate = field.GetValue(queue) as System.Threading.Lock
            ?? throw new InvalidOperationException("The queue owner gate has an unexpected type.");
        var work = new TestWork();
        gate.Enter();

        try
        {
            var attempt = Task.Run(() => {
                var firstAccepted = queue.TryWriteDiagnosticRequest(work, queue.RequestBackingByteCharge);
                var before = GC.GetAllocatedBytesForCurrentThread();
                var accepted = queue.TryWriteDiagnosticRequest(work, queue.RequestBackingByteCharge);
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                return (FirstAccepted: firstAccepted, Accepted: accepted, Allocated: allocated);
            });
            Assert.That(attempt.Wait(TimeSpan.FromSeconds(5)), Is.True);
            var (firstResult, acceptedResult, allocatedBytes) = attempt.GetAwaiter().GetResult();
            Assert.That(firstResult, Is.False);
            Assert.That(acceptedResult, Is.False);
            Assert.That(allocatedBytes, Is.Zero);
        }
        finally
        {
            gate.Exit();
        }

        Assert.That(queue.TryReadRequest(out _), Is.False);
    }

    [Test]
    public async Task RequestAdmissionWaitsAtCapacityAndHonorsCancellation()
    {
        using var queue = new GraphicsOwnerRequestQueue();

        for (var index = 0; index < 256; index++)
        {
            await queue.WriteRequestAsync(new TestWork(), CancellationToken.None).ConfigureAwait(false);
        }

        using var cancellationSource = new CancellationTokenSource();
        var pendingAdmission = queue.WriteRequestAsync(new TestWork(), cancellationSource.Token).AsTask();

        Assert.That(pendingAdmission.IsCompleted, Is.False);

        await cancellationSource.CancelAsync().ConfigureAwait(false);
        _ = Assert.CatchAsync<OperationCanceledException>(async () => await pendingAdmission.ConfigureAwait(false));

        Assert.That(queue.TryReadRequest(out _), Is.True);
        await queue.WriteRequestAsync(new TestWork(), CancellationToken.None).ConfigureAwait(false);
    }

    [Test]
    public async Task RequestCancellationRaceNeverDuplicatesOrWithdrawsAcceptedWork()
    {
        for (var iteration = 0; iteration < 32; iteration++)
        {
            using var queue = new GraphicsOwnerRequestQueue();

            for (var index = 0; index < 256; index++)
            {
                await queue.WriteRequestAsync(new TestWork(), CancellationToken.None).ConfigureAwait(false);
            }

            using var cancellation = new CancellationTokenSource();
            var candidate = new TestWork();
            var pending = queue.WriteRequestAsync(candidate, cancellation.Token).AsTask();
            var cancel = Task.Run(cancellation.Cancel);
            Assert.That(queue.TryReadRequest(out _), Is.True);
            await cancel.ConfigureAwait(false);
            var accepted = true;

            try
            {
                await pending.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                accepted = false;
            }

            var candidateCount = 0;
            var remainingCount = 0;

            while (queue.TryReadRequest(out var work))
            {
                remainingCount++;

                if (ReferenceEquals(work, candidate))
                {
                    candidateCount++;
                }
            }

            Assert.That(candidateCount, Is.EqualTo(accepted ? 1 : 0));
            Assert.That(remainingCount, Is.EqualTo(accepted ? 256 : 255));
        }
    }

    [Test]
    public async Task RequestCancellationRemovesTheWriterWithoutConsumingTheNextSlot()
    {
        using var queue = new GraphicsOwnerRequestQueue();

        for (var index = 0; index < 256; index++)
        {
            await queue.WriteRequestAsync(new TestWork(), CancellationToken.None).ConfigureAwait(false);
        }

        using var cancellation = new CancellationTokenSource();
        var canceled = new TestWork();
        var pending = queue.WriteRequestAsync(canceled, cancellation.Token).AsTask();
        await cancellation.CancelAsync().ConfigureAwait(false);
        _ = Assert.CatchAsync<OperationCanceledException>(async () => await pending.ConfigureAwait(false));
        Assert.That(queue.TryReadRequest(out _), Is.True);
        var diagnostic = new TestWork();
        Assert.That(queue.TryWriteDiagnosticRequest(diagnostic, queue.RequestBackingByteCharge), Is.True);

        for (var index = 0; index < 255; index++)
        {
            Assert.That(queue.TryReadRequest(out var work), Is.True);
            Assert.That(work, Is.Not.SameAs(canceled));
            Assert.That(work, Is.Not.SameAs(diagnostic));
        }

        Assert.That(queue.TryReadRequest(out var last), Is.True);
        Assert.That(last, Is.SameAs(diagnostic));
    }

    [Test]
    public async Task RequestClosureRejectsPendingWritersAndDrainsOnlyAcceptedWork()
    {
        using var queue = new GraphicsOwnerRequestQueue();

        for (var index = 0; index < 256; index++)
        {
            await queue.WriteRequestAsync(new TestWork(), CancellationToken.None).ConfigureAwait(false);
        }

        var pending = queue.WriteRequestAsync(new TestWork(), CancellationToken.None).AsTask();
        queue.CompleteRequests();
        _ = Assert.ThrowsAsync<ChannelClosedException>(async () => await pending.ConfigureAwait(false));
        Assert.That(queue.RequestsCompleted, Is.False);
        Assert.That(queue.TryWriteDiagnosticRequest(new TestWork(), queue.RequestBackingByteCharge), Is.False);

        for (var index = 0; index < 256; index++)
        {
            Assert.That(queue.TryReadRequest(out _), Is.True);
        }

        Assert.That(queue.RequestsCompleted, Is.True);
        Assert.That(queue.TryReadRequest(out _), Is.False);
    }

    [Test]
    public async Task RequestPreCancellationDoesNotAcceptWork()
    {
        using var queue = new GraphicsOwnerRequestQueue();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(false);
        var admission = queue.WriteRequestAsync(new TestWork(), cancellation.Token).AsTask();
        _ = Assert.CatchAsync<OperationCanceledException>(async () => await admission.ConfigureAwait(false));
        Assert.That(queue.TryReadRequest(out _), Is.False);
    }

    [Test]
    public async Task RetirementAdmissionClosesWithoutClosingReservedRetirements()
    {
        using var queue = new GraphicsOwnerRequestQueue();
        var reservations = new List<GraphicsOwnerRequestQueue.GraphicsRetirementReservation>();

        try
        {
            for (var index = 0; index < 256; index++)
            {
                reservations.Add(await queue.ReserveRetirementAsync(CancellationToken.None).ConfigureAwait(false));
            }

            var pendingReservation = queue.ReserveRetirementAsync(CancellationToken.None).AsTask();
            queue.CompleteRetirementReservations();
            _ = Assert.ThrowsAsync<ObjectDisposedException>(async () => await pendingReservation.ConfigureAwait(false));

            Assert.That(queue.TryEnqueueRetirement(new TestWork(), reservations[0]), Is.True);
            Assert.That(queue.TryReadRetirement(out _), Is.True);
        }
        finally
        {
            foreach (var reservation in reservations)
            {
                reservation.Dispose();
            }
        }
    }

    [Test]
    public async Task RetirementCapacityRemainsReservedUntilRetirementCompletes()
    {
        using var queue = new GraphicsOwnerRequestQueue();
        var reservations = new List<GraphicsOwnerRequestQueue.GraphicsRetirementReservation>();
        GraphicsOwnerRequestQueue.GraphicsRetirementReservation? replacement = null;

        try
        {
            for (var index = 0; index < 256; index++)
            {
                var reservation = await queue.ReserveRetirementAsync(CancellationToken.None).ConfigureAwait(false);
                reservations.Add(reservation);
                Assert.That(queue.TryEnqueueRetirement(new TestWork(), reservation), Is.True);
            }

            using var cancellationSource = new CancellationTokenSource();
            var pendingReservation = queue.ReserveRetirementAsync(cancellationSource.Token).AsTask();
            Assert.That(pendingReservation.IsCompleted, Is.False);
            await cancellationSource.CancelAsync().ConfigureAwait(false);
            _ = Assert.CatchAsync<OperationCanceledException>(async () => await pendingReservation.ConfigureAwait(false));

            Assert.That(queue.TryReadRetirement(out _), Is.True);
            var pendingAfterDequeue = queue.ReserveRetirementAsync(CancellationToken.None).AsTask();
            Assert.That(pendingAfterDequeue.IsCompleted, Is.False);
            reservations[0].Dispose();
            replacement = await pendingAfterDequeue.ConfigureAwait(false);
            Assert.That(replacement, Is.Not.Null);
        }
        finally
        {
            foreach (var reservation in reservations)
            {
                reservation.Dispose();
            }

            replacement?.Dispose();
        }
    }

    private sealed class TestWork : IGraphicsDeviceWork
    {
        public void Execute(GraphicsDevice device)
        {
        }

        public void Fail(Exception exception)
        {
            throw exception;
        }
    }
}
