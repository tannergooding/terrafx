// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldWorkerPoolTests
{
    [Test]
    public void InlineZeroWorkerBatchRunsInOrder()
    {
        using var pool = new WorldWorkerPool(0);
        var ownerThreadId = Environment.CurrentManagedThreadId;
        var invocationOrder = 0;
        var callbacks = ImmutableArray.Create<Action>(
            () => {
                Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(ownerThreadId));
                Assert.That(invocationOrder++, Is.Zero);
            },
            () => {
                Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(ownerThreadId));
                Assert.That(invocationOrder++, Is.EqualTo(1));
            });

        pool.RunBatch(callbacks);

        Assert.That(invocationOrder, Is.EqualTo(2));
    }

    [Test]
    public void MultipleConfiguredWorkersOverlapAndJoinTheBatch()
    {
        using var pool = new WorldWorkerPool(4);
        using var entered = new CountdownEvent(4);
        using var release = new ManualResetEventSlim();
        var workerThreadIds = new ConcurrentBag<int>();
        var callbacks = ImmutableArray.CreateBuilder<Action>(4);

        for (var index = 0; index < 4; index++)
        {
            callbacks.Add(() => {
                workerThreadIds.Add(Environment.CurrentManagedThreadId);
                _ = entered.Signal();

                if (!release.Wait(TimeSpan.FromSeconds(15)))
                {
                    throw new TimeoutException("The worker batch release signal was not observed.");
                }
            });
        }

        var run = Task.Run(() => pool.RunBatch(callbacks.MoveToImmutable()));

        try
        {
            Assert.That(entered.Wait(TimeSpan.FromSeconds(15)), Is.True);
            Assert.That(workerThreadIds.Distinct().Count(), Is.EqualTo(4));
        }
        finally
        {
            release.Set();
        }

        run.GetAwaiter().GetResult();
    }

    [Test]
    public void WorkerBatchPreservesTheExactSingleFailure()
    {
        using var pool = new WorldWorkerPool(1);
        var expected = new InvalidOperationException("worker failure");
        var observed = Assert.Throws<InvalidOperationException>(
            () => pool.RunBatch([() => throw expected]));

        Assert.That(observed, Is.SameAs(expected));
    }
}
