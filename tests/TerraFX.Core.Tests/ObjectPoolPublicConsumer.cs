// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Pooling;

namespace TerraFX.Core.Tests;

internal static class ObjectPoolPublicConsumer
{
    public static void Use<TAction>(ObjectPool<PoolPayload> pool, PoolRental<PoolPayload> rental, scoped ref TAction action)
        where TAction : struct, IAction<PoolPayload>, allows ref struct
    {
        pool.Use(rental, ref action);
    }

    internal ref struct BorrowedAction : IAction<PoolPayload>
    {
        private Span<int> _callCount;

        public BorrowedAction(Span<int> callCount)
        {
            _callCount = callCount;
        }

        public void Invoke(PoolPayload argument)
        {
            argument.Value++;
            _callCount[0]++;
        }
    }

    internal struct IncrementAction(int amount) : IAction<PoolPayload>
    {
        public int CallCount { get; private set; }

        public void Invoke(PoolPayload argument)
        {
            argument.Value += amount;
            CallCount++;
        }
    }

    internal sealed class PoolPayload
    {
        public int Value { get; set; }
    }

    internal struct ReturnDuringUseAction(ObjectPool<PoolPayload> pool, PoolRental<PoolPayload> rental) : IAction<PoolPayload>
    {
        public bool ObservedReturning { get; private set; }

        public void Invoke(PoolPayload argument)
        {
            pool.Return(rental);
            ObservedReturning = pool.Snapshot.ReturningCount == 1;
            argument.Value++;
        }
    }

    internal struct ThrowingAction : IAction<PoolPayload>
    {
        public int CallCount { get; private set; }

        public void Invoke(PoolPayload argument)
        {
            argument.Value++;
            CallCount++;
            throw new InvalidOperationException("callback");
        }
    }
}
