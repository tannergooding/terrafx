// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX.Buffers;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class BufferTests
{
    private static OwnedBuffer<T> CreateOwnedBuffer<T>(UnmanagedBuffer<T> storage)
        where T : unmanaged
    {
        var constructor = typeof(OwnedBuffer<T>).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(UnmanagedBuffer<T>)],
            modifiers: null);
        Assert.That(constructor, Is.Not.Null);

        if (constructor is null)
        {
            throw new InvalidOperationException("The accepted internal OwnedBuffer constructor was not found.");
        }

        return (OwnedBuffer<T>)constructor.Invoke([storage]);
    }

    [Test]
    public void AllocationValidatesSignedExtentsAlignmentAndRoundedBudget()
    {
        var zeroBudget = new BufferAllocationOptions(0, 4);
        using (var empty = UnmanagedBuffer<int>.Allocate(0, zeroBudget))
        {
            Assert.That(empty.Length, Is.EqualTo((nint)0));
            Assert.That(empty.ByteLength, Is.EqualTo((nint)0));
            empty.CopyTo(0, []);
        }

        var options = new BufferAllocationOptions(16, 4);
        using (var buffer = UnmanagedBuffer<int>.Allocate(3, options))
        {
            Assert.That(buffer.Length, Is.EqualTo((nint)3));
            Assert.That(buffer.ByteLength, Is.EqualTo((nint)12));
            Unsafe.SkipInit(out InlineArray4<int> valuesStorage); // CopyTo writes the entire exposed destination.
            var values = valuesStorage[..3];
            buffer.CopyTo(0, values);
            Assert.That(values.ToArray(), Is.EqualTo([0, 0, 0]));
        }

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => UnmanagedBuffer<int>.Allocate(1, zeroBudget));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => UnmanagedBuffer<int>.Allocate(5, options));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => UnmanagedBuffer<int>.Allocate(-1, options));
        _ = Assert.Throws<ArgumentException>(() => UnmanagedBuffer<int>.Allocate(1, new BufferAllocationOptions(16, 1)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new BufferAllocationOptions(1, 3));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new BufferAllocationOptions(-1, 4));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new BufferAllocationOptions(1, -4));
        _ = Assert.Throws<OverflowException>(() => UnmanagedBuffer<int>.Allocate(nint.MaxValue, new BufferAllocationOptions(nint.MaxValue, 4)));
        _ = Assert.Throws<OverflowException>(() => UnmanagedBuffer<long>.Allocate(nint.MaxValue / 8, new BufferAllocationOptions(nint.MaxValue, 16)));

        using var endEmptyBuffer = UnmanagedBuffer<int>.Allocate(3, new BufferAllocationOptions(12, 4));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => endEmptyBuffer.AcquireRead(-1, 0));
        using var endEmptyLease = endEmptyBuffer.AcquireRead(endEmptyBuffer.Length, 0);
        var emptyAction = new BufferPublicConsumer.SumReadAction();
        BufferPublicConsumer.UseRead(endEmptyLease, 0, 0, ref emptyAction);
        Assert.That(emptyAction.Sum, Is.Zero);
    }

    [Test]
    public void BufferChainProtectsAllSegmentsWhenVisitorDisposesItself()
    {
        var first = UnmanagedBuffer<int>.Allocate(1, new BufferAllocationOptions(4, 4));
        var second = UnmanagedBuffer<int>.Allocate(1, new BufferAllocationOptions(4, 4));
        first.CopyFrom([17], 0);
        second.CopyFrom([29], 0);
        using var firstLease = first.AcquireRead(0, 1);
        using var secondLease = second.AcquireRead(0, 1);
        using var chain = new BufferChain<int>([firstLease, secondLease]);
        first.Dispose();
        second.Dispose();
        var visitor = new DisposeChainVisitor(chain);

        Assert.That(chain.VisitWindows(ref visitor), Is.True);
        Assert.That(visitor.Sum, Is.EqualTo(46));
        _ = Assert.Throws<ObjectDisposedException>(() => chain.CopyTo(0, []));
    }

    [Test]
    public void BufferChainRejectsExcessRawSegmentCountBeforeInspectingEntries()
    {
        var segments = ImmutableArray.CreateRange(new BufferReadLease<int>[65_537]);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new BufferChain<int>(segments));

        Assert.That(exception!.ParamName, Is.EqualTo("segments"));
    }

    [Test]
    public void BufferChainRetainsIndependentSegmentsAfterDisposal()
    {
        using var first = UnmanagedBuffer<int>.Allocate(2, new BufferAllocationOptions(8, 4));
        using var empty = UnmanagedBuffer<int>.Allocate(0, new BufferAllocationOptions(0, 4));
        using var second = UnmanagedBuffer<int>.Allocate(1, new BufferAllocationOptions(4, 4));
        first.CopyFrom([3, 5], 0);
        second.CopyFrom([7], 0);
        using var firstLease = first.AcquireRead(0, 2);
        using var emptyLease = empty.AcquireRead(0, 0);
        using var secondLease = second.AcquireRead(0, 1);
        using var chain = new BufferChain<int>([firstLease, emptyLease, secondLease]);
        using var retained = chain.Retain();

        Assert.That(chain.SegmentCount, Is.EqualTo(2));
        Assert.That(retained.SegmentCount, Is.EqualTo(2));

        chain.Dispose();
        _ = Assert.Throws<ObjectDisposedException>(() => chain.Retain());
        first.Dispose();
        empty.Dispose();
        second.Dispose();

        InlineArray2<int> stateStorage = default;
        Span<int> state = stateStorage;
        var visitor = new BufferPublicConsumer.ChainStateVisitor(state);
        Assert.That(retained.VisitWindows(ref visitor), Is.True);
        Assert.That(state[0], Is.EqualTo(15));
        Assert.That(state[1], Is.EqualTo(2));
        Assert.That(chain.SegmentCount, Is.EqualTo(2));
    }

    [Test]
    public void BufferChainRetainsSegmentsCopiesAcrossBoundariesAndVisitsRefStructState()
    {
        using var first = UnmanagedBuffer<int>.Allocate(3, new BufferAllocationOptions(12, 4));
        using var second = UnmanagedBuffer<int>.Allocate(2, new BufferAllocationOptions(8, 4));
        using var empty = UnmanagedBuffer<int>.Allocate(0, new BufferAllocationOptions(0, 4));
        first.CopyFrom([1, 2, 3], 0);
        second.CopyFrom([4, 5], 0);

        var firstLease = first.AcquireRead(0, 3);
        var emptyLease = empty.AcquireRead(0, 0);
        var secondLease = second.AcquireRead(0, 2);
        using var chain = new BufferChain<int>([firstLease, emptyLease, secondLease]);
        firstLease.Dispose();
        emptyLease.Dispose();
        secondLease.Dispose();
        first.Dispose();
        second.Dispose();
        empty.Dispose();

        Assert.That(chain.Length, Is.EqualTo(5));
        Assert.That(chain.SegmentCount, Is.EqualTo(2));
        Unsafe.SkipInit(out InlineArray4<int> copyStorage); // CopyTo writes the entire destination before assertion.
        Span<int> copy = copyStorage;
        chain.CopyTo(1, copy);
        Assert.That(copy.ToArray(), Is.EqualTo([2, 3, 4, 5]));

        InlineArray2<int> stateStorage = default; // Visitor accumulates sum/count; both must start at zero.
        Span<int> state = stateStorage;
        var refStructVisitor = new BufferPublicConsumer.ChainStateVisitor(state);
        Assert.That(BufferPublicConsumer.Visit(chain, ref refStructVisitor), Is.True);
        Assert.That(state[0], Is.EqualTo(15));
        Assert.That(state[1], Is.EqualTo(2));

        var stopVisitor = new StopVisitor();
        Assert.That(chain.VisitWindows(ref stopVisitor), Is.False);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => chain.CopyTo(4, new int[4]));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => chain.CopyTo(-1, []));
    }

    [Test]
    public void BufferChainStopOnLastWindowAndVisitorExceptionPreserveResultsAndState()
    {
        var first = UnmanagedBuffer<int>.Allocate(2, new BufferAllocationOptions(8, 4));
        var second = UnmanagedBuffer<int>.Allocate(2, new BufferAllocationOptions(8, 4));
        first.CopyFrom([1, 2], 0);
        second.CopyFrom([3, 4], 0);
        using var firstLease = first.AcquireRead(0, 2);
        using var secondLease = second.AcquireRead(0, 2);
        using var chain = new BufferChain<int>([firstLease, secondLease]);
        first.Dispose();
        second.Dispose();

        var stopOnLast = new StopOnLastWindowVisitor();
        Assert.That(chain.VisitWindows(ref stopOnLast), Is.False);
        Assert.That(stopOnLast.CallCount, Is.EqualTo(2));
        Assert.That(stopOnLast.Sum, Is.EqualTo(10));

        var throwing = new ThrowOnSecondWindowVisitor();
        var observedException = false;

        try
        {
            _ = chain.VisitWindows(ref throwing);
        }
        catch (InvalidOperationException)
        {
            observedException = true;
        }

        Assert.That(observedException, Is.True);
        Assert.That(throwing.CallCount, Is.EqualTo(2));
        Assert.That(throwing.Sum, Is.EqualTo(10));

        InlineArray2<int> stateStorage = default; // Visitor accumulates into state[0], which must start at zero.
        Span<int> state = stateStorage;
        var afterException = new BufferPublicConsumer.ChainStateVisitor(state);
        Assert.That(BufferPublicConsumer.Visit(chain, ref afterException), Is.True);
        Assert.That(state[0], Is.EqualTo(10));
    }

    [Test]
    public void CopyAndScopedWindowsPreserveValuesAndRefStructState()
    {
        using var buffer = UnmanagedBuffer<int>.Allocate(4, new BufferAllocationOptions(32, 4));
        int[] source = [1, 2, 3, 4];
        buffer.CopyFrom(source, 0);

        using var write = buffer.AcquireWrite(0, 4);
        var action = new BufferPublicConsumer.AddValueAction(5);

        BufferPublicConsumer.UseWrite(write, 0, 4, ref action);
        Assert.That(action.CallCount, Is.EqualTo(1));

        InlineArray2<int> stateStorage = default; // Counter increments state[0], so it must start at zero.
        var state = stateStorage[..1];
        var refStructAction = new BufferPublicConsumer.SpanCounterAction(state);
        BufferPublicConsumer.UseWrite(write, 1, 2, ref refStructAction);
        Assert.That(state[0], Is.EqualTo(2));

        write.Dispose();
        using var read = buffer.AcquireRead(0, 4);
        var sum = new BufferPublicConsumer.SumReadAction();
        BufferPublicConsumer.UseRead(read, 0, 4, ref sum);
        Assert.That(sum.Sum, Is.EqualTo(32));

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => read.UseWindow(4, 1, ref sum));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => read.UseWindow(-1, 0, ref sum));
    }

    [Test]
    public void EmptyBufferChainVisitsAsCompletedAndDisposalIsIdempotent()
    {
        var chain = new BufferChain<int>([]);
        var visitor = new StopVisitor();

        Assert.That(chain.VisitWindows(ref visitor), Is.True);
        chain.Dispose();
        chain.Dispose();
        _ = Assert.Throws<ObjectDisposedException>(() => chain.VisitWindows(ref visitor));
    }

    [Test]
    public void FinalizedOwnerTransfersOnlyAfterWritableLeasesAreReleased()
    {
        var buffer = UnmanagedBuffer<int>.Allocate(2, new BufferAllocationOptions(8, 4));
        using var writeLease = buffer.AcquireWrite(0, 2);
        _ = Assert.Throws<TargetInvocationException>(() => CreateOwnedBuffer(buffer));
        writeLease.Dispose();
        buffer.CopyFrom([7, 11], 0);
        using var priorReadLease = buffer.AcquireRead(0, 2);
        var owned = CreateOwnedBuffer(buffer);

        _ = Assert.Throws<ObjectDisposedException>(() => buffer.AcquireRead(0, 1));
        var priorRead = new BufferPublicConsumer.SumReadAction();
        BufferPublicConsumer.UseRead(priorReadLease, 0, 2, ref priorRead);
        Assert.That(priorRead.Sum, Is.EqualTo(18));

        var ownedLease = owned.AcquireReadLease(0, 2);
        owned.Dispose();
        _ = Assert.Throws<ObjectDisposedException>(() => owned.AcquireReadLease(0, 1));
        var ownedRead = new BufferPublicConsumer.SumReadAction();
        BufferPublicConsumer.UseRead(ownedLease, 0, 2, ref ownedRead);
        Assert.That(ownedRead.Sum, Is.EqualTo(18));

        ownedLease.Dispose();
    }

    [Test]
    public void LeaseAdmissionAllowsOverlappingReadsAndRejectsConflictingWrites()
    {
        using var buffer = UnmanagedBuffer<int>.Allocate(8, new BufferAllocationOptions(64, 4));
        using var firstRead = buffer.AcquireRead(0, 4);
        using var secondRead = buffer.AcquireRead(2, 4);
        using var disjointWrite = buffer.AcquireWrite(6, 2);

        _ = Assert.Throws<InvalidOperationException>(() => buffer.AcquireWrite(3, 1));
        _ = Assert.Throws<InvalidOperationException>(() => buffer.AcquireRead(6, 1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => buffer.AcquireRead(8, 1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => buffer.AcquireWrite(0, -1));
    }

    [Test]
    public void OwnerAndLeaseCanCloseConcurrentlyWithoutFreeingAnActiveWindow()
    {
        var buffer = UnmanagedBuffer<int>.Allocate(1, new BufferAllocationOptions(4, 4));
        buffer.CopyFrom([42], 0);
        var lease = buffer.AcquireRead(0, 1);
        using var entered = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        var action = new BlockingReadAction(entered, resume);
        var operation = Task.Run(() => lease.UseWindow(0, 1, ref action));

        var callbackEntered = entered.Wait(TimeSpan.FromSeconds(10));
        buffer.Dispose();
        lease.Dispose();
        resume.Set();

        Assert.That(callbackEntered, Is.True);
        Assert.That(operation.Wait(TimeSpan.FromSeconds(10)), Is.True);
        operation.GetAwaiter().GetResult();
        Assert.That(action.ObservedValue, Is.EqualTo(42));
        _ = Assert.Throws<ObjectDisposedException>(() => buffer.AcquireRead(0, 1));
    }

    [Test]
    public void OwnerAndLeaseDisposalDuringReadCallbackDeferFreeUntilCallbackReturns()
    {
        var buffer = UnmanagedBuffer<int>.Allocate(1, new BufferAllocationOptions(4, 4));
        buffer.CopyFrom([42], 0);
        var lease = buffer.AcquireRead(0, 1);
        var action = new DisposeDuringReadAction(buffer, lease);

        lease.UseWindow(0, 1, ref action);

        Assert.That(action.ObservedValue, Is.EqualTo(42));
        _ = Assert.Throws<ObjectDisposedException>(() => buffer.AcquireRead(0, 1));
        _ = Assert.Throws<ObjectDisposedException>(() => lease.UseWindow(0, 1, ref action));
    }

    [Test]
    public void ReadLeaseRetainSurvivesSourceLeaseAndOwnerDisposal()
    {
        using var buffer = UnmanagedBuffer<int>.Allocate(1, new BufferAllocationOptions(4, 4));
        buffer.CopyFrom([42], 0);
        using var sourceLease = buffer.AcquireRead(0, 1);
        using var retainedLease = sourceLease.Retain();

        sourceLease.Dispose();
        buffer.Dispose();
        _ = Assert.Throws<ObjectDisposedException>(() => sourceLease.Retain());

        var sum = new BufferPublicConsumer.SumReadAction();
        BufferPublicConsumer.UseRead(retainedLease, 0, 1, ref sum);

        Assert.That(sum.Sum, Is.EqualTo(42));
    }

    [Test]
    public void WriteLeaseRejectsReentrantWindowAdmission()
    {
        using var buffer = UnmanagedBuffer<int>.Allocate(2, new BufferAllocationOptions(8, 4));
        using var lease = buffer.AcquireWrite(0, 2);
        var action = new ReentrantWriteAction(lease);

        _ = Assert.Throws<InvalidOperationException>(() => lease.UseWindow(0, 2, ref action));
        Assert.That(action.CallCount, Is.EqualTo(1));
    }

    private struct BlockingReadAction(ManualResetEventSlim entered, ManualResetEventSlim resume) : IAction<ReadOnlySpan<int>>
    {
        public int ObservedValue { get; private set; }

        public void Invoke(scoped ReadOnlySpan<int> window)
        {
            entered.Set();
            resume.Wait();
            ObservedValue = window[0];
        }
    }

    private struct DisposeChainVisitor(BufferChain<int> chain) : IFunc<ReadOnlySpan<int>, bool>
    {
        public int Sum { get; private set; }

        public bool Invoke(scoped ReadOnlySpan<int> window)
        {
            chain.Dispose();

            foreach (var value in window)
            {
                Sum += value;
            }

            return true;
        }
    }

    private struct DisposeDuringReadAction(UnmanagedBuffer<int> owner, BufferReadLease<int> lease) : IAction<ReadOnlySpan<int>>
    {
        private int _observedValue;
        public readonly int ObservedValue => _observedValue;

        public void Invoke(scoped ReadOnlySpan<int> window)
        {
            owner.Dispose();
            lease.Dispose();
            _observedValue = window[0];
        }
    }

    private struct ReentrantWriteAction(BufferWriteLease<int> lease) : IAction<Span<int>>
    {
        public int CallCount { get; private set; }

        public void Invoke(scoped Span<int> window)
        {
            CallCount++;
            var nestedAction = new BufferPublicConsumer.AddValueAction(1);
            lease.UseWindow(0, 1, ref nestedAction);
        }
    }

    private struct StopOnLastWindowVisitor : IFunc<ReadOnlySpan<int>, bool>
    {
        public int CallCount { get; private set; }

        public int Sum { get; private set; }

        public bool Invoke(scoped ReadOnlySpan<int> window)
        {
            CallCount++;

            foreach (var value in window)
            {
                Sum += value;
            }

            return CallCount < 2;
        }
    }

    private struct StopVisitor : IFunc<ReadOnlySpan<int>, bool>
    {
        public readonly bool Invoke(scoped ReadOnlySpan<int> window)
        {
            return false;
        }
    }

    private struct ThrowOnSecondWindowVisitor : IFunc<ReadOnlySpan<int>, bool>
    {
        public int CallCount { get; private set; }

        public int Sum { get; private set; }

        public bool Invoke(scoped ReadOnlySpan<int> window)
        {
            CallCount++;

            foreach (var value in window)
            {
                Sum += value;
            }

            if (CallCount == 2)
            {
                throw new InvalidOperationException("The visitor deliberately failed.");
            }

            return true;
        }
    }
}
