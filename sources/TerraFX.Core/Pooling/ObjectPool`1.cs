// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

namespace TerraFX.Pooling;

/// <summary>Owns a bounded set of reusable managed objects on one owner thread.</summary>
/// <typeparam name="T">The reusable reference type.</typeparam>
public sealed class ObjectPool<T> : IDisposable
    where T : class
{
    private int _activeUseCount;
    private int _availableCount;
    private readonly int[] _availableSlots;
    private int _availableStackCount;
    private int _createdCount;
    private bool _isClosed;
    private long _misses;
    private readonly int _ownerThreadId;
    private readonly IPoolPolicy<T> _policy;
    private bool _policyActive;
    private int _quarantinedCount;
    private int _rentalHighWater;
    private int _rentedCount;
    private int _retiredCount;
    private int _returningCount;
    private readonly Slot[] _slots;

    /// <summary>Initializes a pool with fixed bookkeeping capacity and no created payloads.</summary>
    /// <param name="maximumCapacity">The positive maximum number of slots.</param>
    /// <param name="policy">The policy used to create, reset, and destroy payloads.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumCapacity"/> is not positive.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="policy"/> is <see langword="null"/>.</exception>
    public ObjectPool(int maximumCapacity, IPoolPolicy<T> policy)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCapacity);
        ArgumentNullException.ThrowIfNull(policy);

        _slots = new Slot[maximumCapacity];
        _availableSlots = new int[maximumCapacity];
        _policy = policy;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    /// <summary>Gets a value indicating whether the pool is permanently closed to new work.</summary>
    public bool IsClosed
    {
        get
        {
            VerifyAccess();
            return _isClosed;
        }
    }

    /// <summary>Gets a value indicating whether a closed pool has released all payloads and active callbacks.</summary>
    public bool IsDrained
    {
        get
        {
            VerifyAccess();
            return _isClosed && _createdCount == 0 && _activeUseCount == 0;
        }
    }

    /// <summary>Gets a copied observation of the pool's current state.</summary>
    public PoolSnapshot Snapshot
    {
        get
        {
            VerifyAccess();
            return new PoolSnapshot(_slots.Length, _createdCount, _availableCount, _rentedCount, _returningCount, _quarantinedCount, _retiredCount, _rentalHighWater, _misses, _isClosed, _isClosed && _createdCount == 0 && _activeUseCount == 0);
        }
    }

    private static bool IsOwnedState(SlotState state)
    {
        return state is SlotState.Available or SlotState.Rented or SlotState.Returning or SlotState.Quarantined;
    }

    private static void RecordFailure(Exception exception, ref Exception? firstFailure, ref List<Exception>? failures)
    {
        if (firstFailure is null)
        {
            firstFailure = exception;
        }
        else if (failures is null)
        {
            failures = [firstFailure, exception];
        }
        else
        {
            failures.Add(exception);
        }
    }

    private static void ThrowFailures(Exception? firstFailure, List<Exception>? failures)
    {
        if (firstFailure is null)
        {
            return;
        }

        if (failures is not null)
        {
            ThrowHelper.ThrowAggregateException(failures);
        }

        ExceptionDispatchInfo.Capture(firstFailure).Throw();
    }

    /// <summary>Permanently closes admission and attempts to destroy every available payload.</summary>
    /// <exception cref="InvalidOperationException">A cleanup policy fails or reenters the pool.</exception>
    [SuppressMessage("Design", "CA1031", Justification = "Every available payload must be attempted before cleanup failures are reported.")]
    public void Close()
    {
        VerifyAccess();

        if (_isClosed)
        {
            return;
        }

        _isClosed = true;
        Exception? firstFailure = null;
        List<Exception>? failures = null;

        while (_availableStackCount > 0)
        {
            var slotIndex = PopAvailable();
            Transition(slotIndex, SlotState.Returning);

            try
            {
                DestroySlot(slotIndex, retire: false);
            }
            catch (Exception exception)
            {
                RecordFailure(exception, ref firstFailure, ref failures);
            }
        }

        ThrowFailures(firstFailure, failures);
    }

    /// <summary>Closes the pool without forcing outstanding rentals to return.</summary>
    public void Dispose()
    {
        Close();
    }

    /// <summary>Creates available payloads until the requested available count is reached.</summary>
    /// <param name="availableCount">The nonnegative target number of available payloads.</param>
    /// <exception cref="ArgumentOutOfRangeException">The target is negative or exceeds capacity remaining after retained slots.</exception>
    /// <exception cref="InvalidOperationException">The policy reenters the pool or returns a null/duplicate owned object.</exception>
    /// <exception cref="ObjectDisposedException">The pool is closed.</exception>
    public void Prewarm(int availableCount)
    {
        VerifyAccess();
        ThrowIfClosed();
        ArgumentOutOfRangeException.ThrowIfNegative(availableCount);

        var retainedCount = _rentedCount + _returningCount + _quarantinedCount + _retiredCount;
        ArgumentOutOfRangeException.ThrowIfGreaterThan(availableCount, _slots.Length - retainedCount);

        while (_availableCount < availableCount)
        {
            var slotIndex = FindEmptySlot();
            var value = InvokeCreate();

            if (value is null)
            {
                ThrowHelper.ThrowInvalidOperationException("The pool policy returned a null payload.");
            }

            if (IsCurrentlyOwned(value))
            {
                ThrowHelper.ThrowInvalidOperationException("The pool policy returned a payload already owned by this pool.");
            }

            ref var slot = ref _slots[slotIndex];
            slot._value = value;
            Transition(slotIndex, SlotState.Available);
            PushAvailable(slotIndex);
        }
    }

    /// <summary>Rents an available payload without creating one.</summary>
    /// <returns>A token identifying the rental.</returns>
    /// <exception cref="InvalidOperationException">No payload is available.</exception>
    /// <exception cref="ObjectDisposedException">The pool is closed.</exception>
    public PoolRental<T> Rent()
    {
        VerifyAccess();
        ThrowIfClosed();

        if (_availableStackCount == 0)
        {
            RecordMiss();
            ThrowHelper.ThrowInvalidOperationException("The pool has no available payload.");
        }

        return RentAvailable();
    }

    /// <summary>Retries terminal destruction for every quarantined payload.</summary>
    /// <exception cref="InvalidOperationException">A cleanup policy fails or reenters the pool.</exception>
    [SuppressMessage("Design", "CA1031", Justification = "Every quarantined payload must be attempted before cleanup failures are reported.")]
    public void RetryCleanup()
    {
        VerifyAccess();
        Exception? firstFailure = null;
        List<Exception>? failures = null;

        for (var slotIndex = 0; slotIndex < _slots.Length; slotIndex++)
        {
            ref var slot = ref _slots[slotIndex];

            if (slot._state == SlotState.Quarantined)
            {
                try
                {
                    DestroySlot(slotIndex, slot._retireAfterCleanup);
                }
                catch (Exception exception)
                {
                    RecordFailure(exception, ref firstFailure, ref failures);
                }
            }
        }

        ThrowFailures(firstFailure, failures);
    }

    /// <summary>Returns a rental, invalidating all copies of its token before cleanup begins.</summary>
    /// <param name="rental">The active rental token.</param>
    /// <exception cref="InvalidOperationException">The token is default, stale, duplicated, or owned by another pool.</exception>
    public void Return(PoolRental<T> rental)
    {
        VerifyAccess();
        var slotIndex = ValidateRental(rental, SlotState.Rented);
        ref var slot = ref _slots[slotIndex];
        slot._retireAfterCleanup = slot._generation == uint.MaxValue;
        Transition(slotIndex, SlotState.Returning);

        if (!slot._isInUse)
        {
            CompleteReturn(slotIndex);
        }
    }

    /// <summary>Destroys available payloads in excess of the requested retained count.</summary>
    /// <param name="availableCount">The nonnegative maximum number of available payloads to retain.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="availableCount"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">A cleanup policy fails or reenters the pool.</exception>
    /// <exception cref="ObjectDisposedException">The pool is closed.</exception>
    [SuppressMessage("Design", "CA1031", Justification = "Every selected payload must be cleaned up and all policy failures reported.")]
    public void Trim(int availableCount)
    {
        VerifyAccess();
        ThrowIfClosed();
        ArgumentOutOfRangeException.ThrowIfNegative(availableCount);

        Exception? firstFailure = null;
        List<Exception>? failures = null;

        while (_availableCount > availableCount)
        {
            var slotIndex = PopAvailable();
            Transition(slotIndex, SlotState.Returning);

            try
            {
                DestroySlot(slotIndex, retire: false);
            }
            catch (Exception exception)
            {
                RecordFailure(exception, ref firstFailure, ref failures);
            }
        }

        ThrowFailures(firstFailure, failures);
    }

    /// <summary>Attempts to rent an available payload without creating one.</summary>
    /// <param name="rental">The rental token, or the default token when no payload is available.</param>
    /// <returns><see langword="true"/> when a payload was rented; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ObjectDisposedException">The pool is closed.</exception>
    public bool TryRent(out PoolRental<T> rental)
    {
        VerifyAccess();
        ThrowIfClosed();

        if (_availableStackCount == 0)
        {
            RecordMiss();
            rental = default;
            return false;
        }

        rental = RentAvailable();
        return true;
    }

    /// <summary>Invokes a synchronous callback with the rented payload.</summary>
    /// <typeparam name="TAction">The concrete callback type, which may be a ref struct.</typeparam>
    /// <param name="rental">The active rental token.</param>
    /// <param name="action">The borrowed callback state.</param>
    /// <exception cref="InvalidOperationException">The token is invalid or the slot is already in use.</exception>
    /// <exception cref="ObjectDisposedException">The pool is closed.</exception>
    [SuppressMessage("Design", "CA1031", Justification = "Callback exceptions must be preserved while deferred ownership cleanup completes.")]
    public void Use<TAction>(PoolRental<T> rental, scoped ref TAction action)
        where TAction : struct, IAction<T>, allows ref struct
    {
        VerifyAccess();
        ThrowIfClosed();
        var slotIndex = ValidateRental(rental, SlotState.Rented);
        ref var slot = ref _slots[slotIndex];

        if (slot._isInUse)
        {
            ThrowHelper.ThrowInvalidOperationException("The rental is already in use.");
        }

        var value = slot._value;

        if (value is null)
        {
            ThrowHelper.ThrowInvalidOperationException("The rented slot has no payload.");
        }

        slot._isInUse = true;
        _activeUseCount++;
        Exception? callbackFailure = null;
        Exception? cleanupFailure = null;

        try
        {
            action.Invoke(value);
        }
        catch (Exception exception)
        {
            callbackFailure = exception;
        }
        finally
        {
            slot._isInUse = false;
            _activeUseCount--;

            if (slot._state == SlotState.Returning)
            {
                try
                {
                    CompleteReturn(slotIndex);
                }
                catch (Exception exception)
                {
                    cleanupFailure = exception;
                }
            }
        }

        if (callbackFailure is not null)
        {
            if (cleanupFailure is not null)
            {
                ThrowHelper.ThrowAggregateException(callbackFailure, cleanupFailure);
            }

            ExceptionDispatchInfo.Capture(callbackFailure).Throw();
        }

        if (cleanupFailure is not null)
        {
            ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A reset failure requires terminal destruction while preserving both policy failures.")]
    private void CompleteReturn(int slotIndex)
    {
        ref var slot = ref _slots[slotIndex];

        if (slot._state != SlotState.Returning || slot._isInUse)
        {
            ThrowHelper.ThrowInvalidOperationException("The returning-slot state is inconsistent.");
        }

        var retire = slot._retireAfterCleanup;

        if (_isClosed || retire)
        {
            DestroySlot(slotIndex, retire);
            return;
        }

        var value = slot._value;

        if (value is null)
        {
            ThrowHelper.ThrowInvalidOperationException("The returning slot has no payload.");
        }

        try
        {
            InvokeReset(value);
        }
        catch (Exception resetFailure)
        {
            Transition(slotIndex, SlotState.Quarantined);

            try
            {
                DestroySlot(slotIndex, retire);
            }
            catch (Exception destroyFailure)
            {
                ThrowHelper.ThrowAggregateException(resetFailure, destroyFailure);
            }

            ExceptionDispatchInfo.Capture(resetFailure).Throw();
            return;
        }

        Transition(slotIndex, SlotState.Available);
        PushAvailable(slotIndex);
    }

    private void DestroySlot(int slotIndex, bool retire)
    {
        ref var slot = ref _slots[slotIndex];
        var value = slot._value;

        if (value is null)
        {
            ThrowHelper.ThrowInvalidOperationException("The cleanup slot has no payload.");
        }

        slot._retireAfterCleanup = retire;

        try
        {
            InvokeDestroy(value);
        }
        catch
        {
            Transition(slotIndex, SlotState.Quarantined);
            throw;
        }

        slot._value = null;
        slot._retireAfterCleanup = false;
        Transition(slotIndex, retire ? SlotState.Retired : SlotState.Empty);
    }

    private int FindEmptySlot()
    {
        for (var index = 0; index < _slots.Length; index++)
        {
            if (_slots[index]._state == SlotState.Empty)
            {
                return index;
            }
        }

        throw new InvalidOperationException("No empty slot remains within the validated capacity.");
    }

    private T InvokeCreate()
    {
        _policyActive = true;

        try
        {
            return _policy.Create();
        }
        finally
        {
            _policyActive = false;
        }
    }

    private void InvokeDestroy(T value)
    {
        _policyActive = true;

        try
        {
            _policy.Destroy(value);
        }
        finally
        {
            _policyActive = false;
        }
    }

    private void InvokeReset(T value)
    {
        _policyActive = true;

        try
        {
            _policy.Reset(value);
        }
        finally
        {
            _policyActive = false;
        }
    }

    private bool IsCurrentlyOwned(T value)
    {
        foreach (var slot in _slots)
        {
            if (slot._value is not null && ReferenceEquals(slot._value, value))
            {
                return true;
            }
        }

        return false;
    }

    private int PopAvailable()
    {
        return _availableSlots[--_availableStackCount];
    }

    private void PushAvailable(int slotIndex)
    {
        _availableSlots[_availableStackCount++] = slotIndex;
    }

    private void RecordMiss()
    {
        if (_misses < long.MaxValue)
        {
            _misses++;
        }
    }

    private PoolRental<T> RentAvailable()
    {
        var slotIndex = PopAvailable();
        ref var slot = ref _slots[slotIndex];

        if (slot._state != SlotState.Available || slot._value is null || slot._generation == uint.MaxValue)
        {
            ThrowHelper.ThrowInvalidOperationException("The available-slot bookkeeping is inconsistent.");
        }

        slot._generation++;
        Transition(slotIndex, SlotState.Rented);
        var activeRentals = _rentedCount + _returningCount;

        if (activeRentals > _rentalHighWater)
        {
            _rentalHighWater = activeRentals;
        }

        return new PoolRental<T>(this, slotIndex, slot._generation);
    }

    private void ThrowIfClosed()
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
    }

    private void Transition(int slotIndex, SlotState state)
    {
        ref var slot = ref _slots[slotIndex];
        var previous = slot._state;

        if (previous == state)
        {
            return;
        }

        UpdateStateCount(previous, -1);
        UpdateStateCount(state, 1);

        if (IsOwnedState(previous) != IsOwnedState(state))
        {
            _createdCount += IsOwnedState(state) ? 1 : -1;
        }

        slot._state = state;
    }

    private void UpdateStateCount(SlotState state, int delta)
    {
        switch (state)
        {
            case SlotState.Available:
            {
                _availableCount += delta;
                break;
            }

            case SlotState.Rented:
            {
                _rentedCount += delta;
                break;
            }

            case SlotState.Returning:
            {
                _returningCount += delta;
                break;
            }

            case SlotState.Quarantined:
            {
                _quarantinedCount += delta;
                break;
            }

            case SlotState.Retired:
            {
                _retiredCount += delta;
                break;
            }

            case SlotState.Empty:
            {
                break;
            }

            default:
            {
                throw new ArgumentOutOfRangeException(nameof(state));
            }
        }
    }

    private int ValidateRental(PoolRental<T> rental, SlotState requiredState)
    {
        var slotIndex = rental.Slot;

        if (!ReferenceEquals(rental.Owner, this) || (uint)slotIndex >= (uint)_slots.Length)
        {
            ThrowHelper.ThrowInvalidOperationException("The rental token does not belong to this pool.");
        }

        ref var slot = ref _slots[slotIndex];

        if (slot._generation != rental.Generation || slot._state != requiredState)
        {
            ThrowHelper.ThrowInvalidOperationException("The rental token is stale or no longer active.");
        }

        return slotIndex;
    }

    private void VerifyAccess()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            ThrowHelper.ThrowInvalidOperationException("The pool may only be used from its owner thread.");
        }

        if (_policyActive)
        {
            ThrowHelper.ThrowInvalidOperationException("The pool policy cannot reenter pool operations.");
        }
    }

    private struct Slot
    {
        internal T? _value;
        internal SlotState _state;
        internal uint _generation;
        internal bool _isInUse;
        internal bool _retireAfterCleanup;
    }

    private enum SlotState : byte
    {
        Empty,
        Available,
        Rented,
        Returning,
        Quarantined,
        Retired,
    }
}
