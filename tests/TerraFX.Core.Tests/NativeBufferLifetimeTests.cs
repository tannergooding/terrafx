// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX.Buffers;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class NativeBufferLifetimeTests
{
    private static readonly BufferAllocationOptions s_allocationOptions = new BufferAllocationOptions(4, 4);

    private static void CollectFinalizers()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Reliability", "CA2000", Justification = "This acceptance case deliberately abandons the owner to exercise backing-storage finalization.")]
    private static WeakReference<OwnedBuffer<int>> CreateAbandonedOwnedBuffer(FakeNativeMemoryOperations nativeMemory)
    {
        var storage = UnmanagedBuffer<int>.Allocate(1, s_allocationOptions, nativeMemory);
        var ownedBuffer = new OwnedBuffer<int>(storage);
        _ = ownedBuffer.AcquireReadLease(0, 1);
        return new WeakReference<OwnedBuffer<int>>(ownedBuffer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<UnmanagedBuffer<int>> CreateAbandonedOwner(FakeNativeMemoryOperations nativeMemory, bool closeOwner)
    {
        var buffer = UnmanagedBuffer<int>.Allocate(1, s_allocationOptions, nativeMemory);
        _ = buffer.AcquireRead(0, 1);

        if (closeOwner)
        {
            buffer.Dispose();
        }

        return new WeakReference<UnmanagedBuffer<int>>(buffer);
    }

    [Test]
    public void DisposedLeaseCannotAccessReusedNativeAddressAndDisposalReleasesOnce()
    {
        using var nativeMemory = new FakeNativeMemoryOperations();
        var first = UnmanagedBuffer<int>.Allocate(1, s_allocationOptions, nativeMemory);
        first.CopyFrom([17], 0);
        var staleLease = first.AcquireRead(0, 1);

        first.Dispose();
        first.Dispose();
        Assert.That(nativeMemory.ReleaseCount, Is.Zero);

        staleLease.Dispose();
        Assert.That(nativeMemory.ReleaseCount, Is.EqualTo(1));
        var second = UnmanagedBuffer<int>.Allocate(1, s_allocationOptions, nativeMemory);
        Assert.That(nativeMemory.GetAddress(0), Is.EqualTo(nativeMemory.GetAddress(1)));

        second.CopyFrom([29], 0);
        var staleAction = new ReadFirstAction();
        _ = Assert.Throws<ObjectDisposedException>(() => staleLease.UseWindow(0, 1, ref staleAction));

        Unsafe.SkipInit(out InlineArray2<int> resultStorage); // CopyTo writes the one exposed element before assertion.
        Span<int> result = resultStorage;

        second.CopyTo(0, result[..1]);
        Assert.That(result[0], Is.EqualTo(29));

        staleLease.Dispose();
        second.Dispose();
        Assert.That(nativeMemory.ReleaseCount, Is.EqualTo(2));
    }

    [Test]
    public void FailedInitializationAfterNativeAcquisitionReleasesTheAllocation()
    {
        using var nativeMemory = new FakeNativeMemoryOperations {
            ThrowAfterClear = true,
        };

        _ = Assert.Throws<InvalidOperationException>(
            () => UnmanagedBuffer<int>.Allocate(1, s_allocationOptions, nativeMemory));

        Assert.That(nativeMemory.AllocationCount, Is.EqualTo(1));
        Assert.That(nativeMemory.ClearCount, Is.EqualTo(1));
        Assert.That(nativeMemory.ReleaseCount, Is.EqualTo(1));
        Assert.That(nativeMemory.ActiveAllocationCount, Is.Zero);
    }

    [Test]
    public void FinalizableStorageReclaimsAnAbandonedOwnedBuffer()
    {
        using var nativeMemory = new FakeNativeMemoryOperations();
        var weakOwner = CreateAbandonedOwnedBuffer(nativeMemory);

        CollectFinalizers();

        Assert.That(weakOwner.TryGetTarget(out _), Is.False);
        Assert.That(nativeMemory.ActiveAllocationCount, Is.Zero);
        Assert.That(nativeMemory.ReleaseCount, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FinalizerReleasesAbandonedNativeOwnerAndLeaseCredits(bool closeOwner)
    {
        using var nativeMemory = new FakeNativeMemoryOperations();
        var weakOwner = CreateAbandonedOwner(nativeMemory, closeOwner);

        CollectFinalizers();

        Assert.That(weakOwner.TryGetTarget(out _), Is.False);
        Assert.That(nativeMemory.ActiveAllocationCount, Is.Zero);
        Assert.That(nativeMemory.ReleaseCount, Is.EqualTo(1));
    }

    [Test]
    public void NativeMemoryIsRetainedUntilAdmittedCallbackReturns()
    {
        using var nativeMemory = new FakeNativeMemoryOperations();
        var buffer = UnmanagedBuffer<int>.Allocate(1, s_allocationOptions, nativeMemory);
        buffer.CopyFrom([42], 0);
        var lease = buffer.AcquireRead(0, 1);
        using var entered = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        var action = new BlockingReadAction(entered, resume);
        var operation = Task.Run(() => lease.UseWindow(0, 1, ref action));

        var callbackEntered = entered.Wait(TimeSpan.FromSeconds(10));
        buffer.Dispose();
        lease.Dispose();
        var releaseCountBeforeCallbackExit = nativeMemory.ReleaseCount;
        resume.Set();
        var operationCompleted = operation.Wait(TimeSpan.FromSeconds(10));

        if (operationCompleted)
        {
            operation.GetAwaiter().GetResult();
        }

        Assert.That(callbackEntered, Is.True);
        Assert.That(operationCompleted, Is.True);
        Assert.That(action.ObservedValue, Is.EqualTo(42));
        Assert.That(releaseCountBeforeCallbackExit, Is.Zero);
        Assert.That(nativeMemory.ReleaseCount, Is.EqualTo(1));
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

    private sealed class FakeNativeMemoryOperations : INativeMemoryOperations, IDisposable
    {
        private readonly Lock _gate = new Lock();
        private readonly INativeMemoryOperations _nativeMemory = NativeMemoryOperations.Instance;
        private readonly Dictionary<nuint, bool> _allocations = [];
        private readonly HashSet<nuint> _activeAllocations = [];
        private readonly Stack<nuint> _availableAllocations = [];
        private readonly List<nuint> _allocationHistory = [];
        private int _clearCount;
        private int _releaseCount;

        internal int ActiveAllocationCount
        {
            get
            {
                lock (_gate)
                {
                    return _activeAllocations.Count;
                }
            }
        }

        internal int AllocationCount
        {
            get
            {
                lock (_gate)
                {
                    return _allocationHistory.Count;
                }
            }
        }

        internal int ClearCount
        {
            get
            {
                lock (_gate)
                {
                    return _clearCount;
                }
            }
        }

        internal int ReleaseCount
        {
            get
            {
                lock (_gate)
                {
                    return _releaseCount;
                }
            }
        }

        internal bool ThrowAfterClear { get; set; }

        public void Dispose()
        {
            KeyValuePair<nuint, bool>[] allocations;
            lock (_gate)
            {
                if (_activeAllocations.Count != 0)
                {
                    ThrowHelper.ThrowInvalidOperationException("The fake native allocator cannot be disposed while an allocation is active.");
                }

                allocations = new KeyValuePair<nuint, bool>[_allocations.Count];
                var index = 0;

                foreach (var allocation in _allocations)
                {
                    allocations[index++] = allocation;
                }
            }

            foreach (var allocation in allocations)
            {
                _nativeMemory.Free(allocation.Key, allocation.Value);
            }
        }

        internal nuint GetAddress(int index)
        {
            lock (_gate)
            {
                return _allocationHistory[index];
            }
        }

        nuint INativeMemoryOperations.Allocate(nuint byteLength, nuint alignment, bool useAlignedAllocator)
        {
            lock (_gate)
            {
                nuint address;

                if (_availableAllocations.TryPop(out var availableAddress))
                {
                    address = availableAddress;
                }
                else
                {
                    address = _nativeMemory.Allocate(byteLength, alignment, useAlignedAllocator);

                    if (address is 0)
                    {
                        return 0;
                    }

                    _allocations.Add(address, useAlignedAllocator);
                }

                if (_allocations[address] != useAlignedAllocator)
                {
                    ThrowHelper.ThrowInvalidOperationException("A reused allocation requires a different allocator.");
                }

                if (!_activeAllocations.Add(address))
                {
                    ThrowHelper.ThrowInvalidOperationException("The fake native allocator cannot admit an active address twice.");
                }

                _allocationHistory.Add(address);
                return address;
            }
        }

        void INativeMemoryOperations.Clear(nuint address, nuint byteLength)
        {
            _nativeMemory.Clear(address, byteLength);

            lock (_gate)
            {
                _clearCount++;
            }

            if (ThrowAfterClear)
            {
                ThrowAfterClear = false;
                throw new InvalidOperationException("The fake native allocator failed after acquiring storage.");
            }
        }

        void INativeMemoryOperations.Free(nuint address, bool useAlignedAllocator)
        {
            lock (_gate)
            {
                if (!_activeAllocations.Contains(address))
                {
                    ThrowHelper.ThrowInvalidOperationException("The fake native allocator received a duplicate or unknown release.");
                }

                if (_allocations[address] != useAlignedAllocator)
                {
                    ThrowHelper.ThrowInvalidOperationException("An allocation must be released by its matching allocator.");
                }

                _ = _activeAllocations.Remove(address);
                _releaseCount++;
                _availableAllocations.Push(address);
            }
        }
    }

    private readonly struct ReadFirstAction : IAction<ReadOnlySpan<int>>
    {
        public void Invoke(scoped ReadOnlySpan<int> window)
        {
            _ = window[0];
        }
    }
}
