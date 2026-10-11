// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX.Pooling;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class ObjectPoolTests
{
    private static void AssertAccounting(PoolSnapshot snapshot)
    {
        Assert.That(snapshot.CreatedCount, Is.EqualTo(snapshot.AvailableCount + snapshot.RentedCount + snapshot.ReturningCount + snapshot.QuarantinedCount));
    }

    private static void SetAvailableGeneration(ObjectPool<ObjectPoolPublicConsumer.PoolPayload> pool, uint generation)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var slotsField = typeof(ObjectPool<ObjectPoolPublicConsumer.PoolPayload>).GetField("_slots", flags)
            ?? throw new InvalidOperationException("The pool slots were not found.");
        var slots = (Array)(slotsField.GetValue(pool) ?? throw new InvalidOperationException("The pool slots were null."));
        var slotType = slots.GetType().GetElementType() ?? throw new InvalidOperationException("The pool slot type was not found.");
        var slot = slots.GetValue(0) ?? throw new InvalidOperationException("The pool slot was null.");
        var generationField = slotType.GetField("_generation", flags)
            ?? throw new InvalidOperationException("The generation field was not found.");
        generationField.SetValue(slot, generation);
        slots.SetValue(slot, 0);
    }

    [Test]
    public void CallbackFailurePreservesRentalAndRefStateUntilExplicitReturn()
    {
        var policy = new RecordingPolicy();
        using var pool = new ObjectPool<ObjectPoolPublicConsumer.PoolPayload>(1, policy);
        pool.Prewarm(1);
        var rental = pool.Rent();
        var action = new ObjectPoolPublicConsumer.ThrowingAction();

        var exception = Assert.Throws<InvalidOperationException>(() => ObjectPoolPublicConsumer.Use(pool, rental, ref action));

        Assert.That(exception?.Message, Is.EqualTo("callback"));
        Assert.That(action.CallCount, Is.EqualTo(1));
        Assert.That(policy.ResetCount, Is.Zero);
        Assert.That(pool.Snapshot.RentedCount, Is.EqualTo(1));
        Assert.That(policy.LastCreated?.Value, Is.EqualTo(1));

        pool.Return(rental);
        Assert.That(policy.ResetCount, Is.EqualTo(1));
        Assert.That(pool.Snapshot.AvailableCount, Is.EqualTo(1));
    }

    [Test]
    public void CloseAttemptsEveryAvailableDestroyAndRetryCleanupDrainsFailures()
    {
        var policy = new RecordingPolicy { ThrowOnDestroy = true };
        using var pool = new ObjectPool<ObjectPoolPublicConsumer.PoolPayload>(2, policy);
        pool.Prewarm(2);

        var exception = Assert.Throws<AggregateException>(pool.Close);

        Assert.That(exception?.InnerExceptions.Count, Is.EqualTo(2));
        Assert.That(policy.DestroyCount, Is.EqualTo(2));
        Assert.That(pool.Snapshot.QuarantinedCount, Is.EqualTo(2));
        Assert.That(pool.IsDrained, Is.False);
        AssertAccounting(pool.Snapshot);

        pool.Close();
        Assert.That(policy.DestroyCount, Is.EqualTo(2));

        policy.ThrowOnDestroy = false;
        pool.RetryCleanup();
        Assert.That(policy.DestroyCount, Is.EqualTo(4));
        Assert.That(pool.Snapshot.CreatedCount, Is.Zero);
        Assert.That(pool.IsDrained, Is.True);
        AssertAccounting(pool.Snapshot);
    }

    [Test]
    public void CloseDestroysAvailablePayloadsButAllowsOutstandingReturns()
    {
        var policy = new RecordingPolicy();
        using var pool = new ObjectPool<ObjectPoolPublicConsumer.PoolPayload>(2, policy);
        pool.Prewarm(2);
        var rental = pool.Rent();

        pool.Close();
        Assert.That(pool.IsClosed, Is.True);
        Assert.That(pool.IsDrained, Is.False);
        Assert.That(policy.DestroyCount, Is.EqualTo(1));
        Assert.That(policy.ResetCount, Is.Zero);
        _ = Assert.Throws<ObjectDisposedException>(() => pool.Rent());
        _ = Assert.Throws<ObjectDisposedException>(() => pool.TryRent(out _));
        _ = Assert.Throws<ObjectDisposedException>(() => pool.Prewarm(0));
        _ = Assert.Throws<ObjectDisposedException>(() => pool.Trim(0));

        pool.Return(rental);
        Assert.That(policy.DestroyCount, Is.EqualTo(2));
        Assert.That(policy.ResetCount, Is.Zero);
        Assert.That(pool.IsDrained, Is.True);
        pool.Close();
        Assert.That(policy.DestroyCount, Is.EqualTo(2));
    }

    [Test]
    public void MaximumGenerationDestroysAndRetiresRatherThanWrapping()
    {
        var policy = new RecordingPolicy();
        using var pool = new ObjectPool<ObjectPoolPublicConsumer.PoolPayload>(1, policy);
        pool.Prewarm(1);

        SetAvailableGeneration(pool, uint.MaxValue - 1);

        var rental = pool.Rent();
        pool.Return(rental);

        Assert.That(policy.ResetCount, Is.Zero);
        Assert.That(policy.DestroyCount, Is.EqualTo(1));
        Assert.That(pool.Snapshot.RetiredCount, Is.EqualTo(1));
        Assert.That(pool.Snapshot.CreatedCount, Is.Zero);
        var createCount = policy.CreateCount;
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => pool.Prewarm(1));
        Assert.That(policy.CreateCount, Is.EqualTo(createCount));
    }

    [Test]
    public void PolicyReentrancyAndWrongThreadAccessFailBeforeMutation()
    {
        var policy = new RecordingPolicy();
        using var pool = new ObjectPool<ObjectPoolPublicConsumer.PoolPayload>(1, policy);

        policy.DuringCreate = () => _ = pool.Snapshot;
        _ = Assert.Throws<InvalidOperationException>(() => pool.Prewarm(1));
        Assert.That(pool.Snapshot.CreatedCount, Is.Zero);

        policy.DuringCreate = null;
        pool.Prewarm(1);

        policy.DuringReset = () => _ = pool.Snapshot;
        var rental = pool.Rent();
        _ = Assert.Throws<InvalidOperationException>(() => pool.Return(rental));
        Assert.That(pool.Snapshot.CreatedCount, Is.Zero);

        policy.DuringReset = null;
        pool.Prewarm(1);

        policy.DuringDestroy = () => _ = pool.Snapshot;
        _ = Assert.Throws<InvalidOperationException>(pool.Close);
        Assert.That(pool.Snapshot.QuarantinedCount, Is.EqualTo(1));
        policy.DuringDestroy = null;
        pool.RetryCleanup();
        Assert.That(pool.IsDrained, Is.True);

        var wrongThreadRejected = Task.Run(() => {
            try
            {
                _ = pool.Snapshot;
                return false;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }).GetAwaiter().GetResult();

        Assert.That(wrongThreadRejected, Is.True);
        Assert.That(pool.Snapshot.AvailableCount, Is.Zero);
    }

    [Test]
    public void PrewarmCreatesOnlyWithinTheRetainedCapacityAndRentNeverCreates()
    {
        var policy = new RecordingPolicy();
        using var pool = new ObjectPool<ObjectPoolPublicConsumer.PoolPayload>(2, policy);

        Assert.That(policy.CreateCount, Is.Zero);
        Assert.That(pool.Snapshot.CreatedCount, Is.Zero);

        Assert.That(pool.TryRent(out var missing), Is.False);
        Assert.That(missing, Is.EqualTo(default(PoolRental<ObjectPoolPublicConsumer.PoolPayload>)));
        _ = Assert.Throws<InvalidOperationException>(() => pool.Return(missing));
        _ = Assert.Throws<InvalidOperationException>(() => pool.Rent());
        Assert.That(pool.Snapshot.Misses, Is.EqualTo(2));

        pool.Prewarm(2);
        Assert.That(policy.CreateCount, Is.EqualTo(2));
        Assert.That(pool.Snapshot.AvailableCount, Is.EqualTo(2));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => pool.Prewarm(3));
        Assert.That(policy.CreateCount, Is.EqualTo(2));

        var rental = pool.Rent();
        Assert.That(pool.Snapshot.RentalHighWater, Is.EqualTo(1));
        pool.Return(rental);
        AssertAccounting(pool.Snapshot);
    }

    [Test]
    public void PrewarmIsIncrementalAndRejectsCurrentlyOwnedDuplicates()
    {
        var policy = new RecordingPolicy { ThrowOnCreateAt = 2 };
        using var pool = new ObjectPool<ObjectPoolPublicConsumer.PoolPayload>(3, policy);

        _ = Assert.Throws<InvalidOperationException>(() => pool.Prewarm(3));
        Assert.That(policy.CreateCount, Is.EqualTo(2));
        Assert.That(pool.Snapshot.AvailableCount, Is.EqualTo(1));

        policy.ThrowOnCreateAt = 0;
        policy.ReturnDuplicate = true;
        _ = Assert.Throws<InvalidOperationException>(() => pool.Prewarm(2));
        Assert.That(pool.Snapshot.AvailableCount, Is.EqualTo(1));
        Assert.That(policy.DestroyCount, Is.Zero);

        policy.ReturnDuplicate = false;
        pool.Prewarm(2);
        Assert.That(pool.Snapshot.AvailableCount, Is.EqualTo(2));
        AssertAccounting(pool.Snapshot);
    }

    [Test]
    public void PublicUsePreservesOrdinaryAndRefStructCallableState()
    {
        var policy = new RecordingPolicy();
        using var pool = new ObjectPool<ObjectPoolPublicConsumer.PoolPayload>(1, policy);
        pool.Prewarm(1);
        var rental = pool.Rent();
        var action = new ObjectPoolPublicConsumer.IncrementAction(4);

        ObjectPoolPublicConsumer.Use(pool, rental, ref action);
        Assert.That(action.CallCount, Is.EqualTo(1));
        Assert.That(policy.LastCreated?.Value, Is.EqualTo(4));

        Span<int> callCount = [0];
        var borrowedAction = new ObjectPoolPublicConsumer.BorrowedAction(callCount);
        ObjectPoolPublicConsumer.Use(pool, rental, ref borrowedAction);
        Assert.That(callCount[0], Is.EqualTo(1));
        Assert.That(policy.LastCreated?.Value, Is.EqualTo(5));
        pool.Return(rental);
    }

    [Test]
    public void ResetAndDestroyFailuresQuarantineOwnershipAndRetryOnlyDestruction()
    {
        var policy = new RecordingPolicy { ThrowOnReset = true, ThrowOnDestroy = true };
        using var pool = new ObjectPool<ObjectPoolPublicConsumer.PoolPayload>(1, policy);
        pool.Prewarm(1);
        var rental = pool.Rent();

        var exception = Assert.Throws<AggregateException>(() => pool.Return(rental));

        Assert.That(exception?.InnerExceptions.Count, Is.EqualTo(2));
        Assert.That(exception?.InnerExceptions[0].Message, Is.EqualTo("reset"));
        Assert.That(exception?.InnerExceptions[1].Message, Is.EqualTo("destroy"));
        Assert.That(pool.Snapshot.CreatedCount, Is.EqualTo(1));
        Assert.That(pool.Snapshot.QuarantinedCount, Is.EqualTo(1));
        Assert.That(pool.Snapshot.IsDrained, Is.False);
        AssertAccounting(pool.Snapshot);

        var createCount = policy.CreateCount;
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => pool.Prewarm(1));
        Assert.That(policy.CreateCount, Is.EqualTo(createCount));

        policy.ThrowOnDestroy = false;
        pool.RetryCleanup();
        Assert.That(policy.ResetCount, Is.EqualTo(1));
        Assert.That(policy.DestroyCount, Is.EqualTo(2));
        Assert.That(pool.Snapshot.CreatedCount, Is.Zero);
        Assert.That(pool.Snapshot.QuarantinedCount, Is.Zero);
        AssertAccounting(pool.Snapshot);

        pool.Close();
    }

    [Test]
    public void ReturnDuringUseDefersCleanupUntilTheCallbackExits()
    {
        var policy = new RecordingPolicy();
        using var pool = new ObjectPool<ObjectPoolPublicConsumer.PoolPayload>(1, policy);
        pool.Prewarm(1);
        var rental = pool.Rent();
        var action = new ObjectPoolPublicConsumer.ReturnDuringUseAction(pool, rental);

        ObjectPoolPublicConsumer.Use(pool, rental, ref action);

        Assert.That(action.ObservedReturning, Is.True);
        Assert.That(policy.ResetCount, Is.EqualTo(1));
        Assert.That(pool.Snapshot.AvailableCount, Is.EqualTo(1));
        Assert.That(pool.Snapshot.ReturningCount, Is.Zero);
        Assert.That(policy.LastCreated?.Value, Is.Zero);
        _ = Assert.Throws<InvalidOperationException>(() => pool.Return(rental));
    }

    [Test]
    public void StaleAndWrongOwnerTokensRejectBeforeCallableInvocation()
    {
        var firstPolicy = new RecordingPolicy();
        var secondPolicy = new RecordingPolicy();
        using var first = new ObjectPool<ObjectPoolPublicConsumer.PoolPayload>(1, firstPolicy);
        using var second = new ObjectPool<ObjectPoolPublicConsumer.PoolPayload>(1, secondPolicy);
        first.Prewarm(1);
        second.Prewarm(1);
        var stale = first.Rent();
        first.Return(stale);
        var current = first.Rent();
        var action = new ObjectPoolPublicConsumer.IncrementAction(1);

        _ = Assert.Throws<InvalidOperationException>(() => ObjectPoolPublicConsumer.Use(second, stale, ref action));
        _ = Assert.Throws<InvalidOperationException>(() => ObjectPoolPublicConsumer.Use(first, stale, ref action));
        Assert.That(action.CallCount, Is.Zero);
        Assert.That(secondPolicy.LastCreated?.Value, Is.Zero);

        ObjectPoolPublicConsumer.Use(first, current, ref action);
        Assert.That(action.CallCount, Is.EqualTo(1));
        first.Return(current);
    }

    [Test]
    public void SuccessfulRentUseReturnCyclesDoNotAllocateAfterWarmup()
    {
        var policy = new RecordingPolicy();
        using var pool = new ObjectPool<ObjectPoolPublicConsumer.PoolPayload>(1, policy);
        pool.Prewarm(1);
        var action = new ObjectPoolPublicConsumer.IncrementAction(1);

        for (var index = 0; index < 64; index++)
        {
            var warmRental = pool.Rent();
            ObjectPoolPublicConsumer.Use(pool, warmRental, ref action);
            pool.Return(warmRental);
        }

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 256; index++)
        {
            var rental = pool.Rent();
            ObjectPoolPublicConsumer.Use(pool, rental, ref action);
            pool.Return(rental);
        }

        var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
        Assert.That(action.CallCount, Is.EqualTo(320));
        Assert.That(allocatedAfter - allocatedBefore, Is.Zero);
    }

    [Test]
    public void TrimAttemptsEveryExcessPayloadAndRetainsTheRequestedAvailableCount()
    {
        var policy = new RecordingPolicy { ThrowOnDestroy = true };
        using var pool = new ObjectPool<ObjectPoolPublicConsumer.PoolPayload>(3, policy);
        pool.Prewarm(3);

        var exception = Assert.Throws<AggregateException>(() => pool.Trim(1));

        Assert.That(exception?.InnerExceptions.Count, Is.EqualTo(2));
        Assert.That(pool.Snapshot.AvailableCount, Is.EqualTo(1));
        Assert.That(pool.Snapshot.QuarantinedCount, Is.EqualTo(2));
        AssertAccounting(pool.Snapshot);

        policy.ThrowOnDestroy = false;
        pool.RetryCleanup();
        pool.Trim(0);
        Assert.That(pool.Snapshot.CreatedCount, Is.Zero);
        AssertAccounting(pool.Snapshot);
    }

    private sealed class RecordingPolicy : IPoolPolicy<ObjectPoolPublicConsumer.PoolPayload>
    {
        public int CreateCount { get; private set; }

        public int DestroyCount { get; private set; }

        public Action? DuringCreate { get; set; }

        public Action? DuringDestroy { get; set; }

        public Action? DuringReset { get; set; }

        public ObjectPoolPublicConsumer.PoolPayload? LastCreated { get; private set; }

        public int ResetCount { get; private set; }

        public bool ReturnDuplicate { get; set; }

        public int ThrowOnCreateAt { get; set; }

        public bool ThrowOnDestroy { get; set; }

        public bool ThrowOnReset { get; set; }

        public ObjectPoolPublicConsumer.PoolPayload Create()
        {
            CreateCount++;
            DuringCreate?.Invoke();

            if (CreateCount == ThrowOnCreateAt)
            {
                throw new InvalidOperationException("create");
            }

            if (ReturnDuplicate && LastCreated is not null)
            {
                return LastCreated;
            }

            LastCreated = new ObjectPoolPublicConsumer.PoolPayload();
            return LastCreated;
        }

        public void Destroy(ObjectPoolPublicConsumer.PoolPayload value)
        {
            DestroyCount++;
            DuringDestroy?.Invoke();

            if (ThrowOnDestroy)
            {
                throw new InvalidOperationException("destroy");
            }
        }

        public void Reset(ObjectPoolPublicConsumer.PoolPayload value)
        {
            ResetCount++;
            DuringReset?.Invoke();

            if (ThrowOnReset)
            {
                throw new InvalidOperationException("reset");
            }

            value.Value = 0;
        }
    }
}
