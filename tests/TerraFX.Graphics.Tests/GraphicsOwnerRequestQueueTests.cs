// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX.Graphics;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class GraphicsOwnerRequestQueueTests
{
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
