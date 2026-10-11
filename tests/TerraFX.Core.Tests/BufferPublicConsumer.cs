// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Buffers;

namespace TerraFX.Core.Tests;

internal static class BufferPublicConsumer
{
    public static void UseRead<TAction>(BufferReadLease<int> lease, nint offset, int length, scoped ref TAction action)
        where TAction : struct, IAction<ReadOnlySpan<int>>, allows ref struct
    {
        lease.UseWindow(offset, length, ref action);
    }

    public static void UseWrite<TAction>(BufferWriteLease<int> lease, nint offset, int length, scoped ref TAction action)
        where TAction : struct, IAction<Span<int>>, allows ref struct
    {
        lease.UseWindow(offset, length, ref action);
    }

    public static bool Visit<TVisitor>(BufferChain<int> chain, scoped ref TVisitor visitor)
        where TVisitor : struct, IFunc<ReadOnlySpan<int>, bool>, allows ref struct
    {
        return chain.VisitWindows(ref visitor);
    }

    internal struct AddValueAction(int value) : IAction<Span<int>>
    {
        private int _callCount;

        public readonly int CallCount => _callCount;

        public void Invoke(scoped Span<int> window)
        {
            for (var index = 0; index < window.Length; index++)
            {
                window[index] += value;
            }

            _callCount++;
        }
    }

    internal ref struct ChainStateVisitor : IFunc<ReadOnlySpan<int>, bool>
    {
        private Span<int> _state;

        public ChainStateVisitor(Span<int> state)
        {
            _state = state;
        }

        public bool Invoke(scoped ReadOnlySpan<int> window)
        {
            foreach (var value in window)
            {
                _state[0] += value;
            }

            _state[1]++;
            return true;
        }
    }

    internal struct ChainSumVisitor : IFunc<ReadOnlySpan<int>, bool>
    {
        public int Sum { get; private set; }

        public int WindowCount { get; private set; }

        public bool Invoke(scoped ReadOnlySpan<int> window)
        {
            foreach (var value in window)
            {
                Sum += value;
            }

            WindowCount++;
            return true;
        }
    }

    internal ref struct SpanCounterAction : IAction<Span<int>>
    {
        private Span<int> _state;

        public SpanCounterAction(Span<int> state)
        {
            _state = state;
        }

        public void Invoke(scoped Span<int> window)
        {
            for (var index = 0; index < window.Length; index++)
            {
                window[index] += 1;
            }

            _state[0] += window.Length;
        }
    }

    internal struct SumReadAction : IAction<ReadOnlySpan<int>>
    {
        public int Sum { get; private set; }

        public void Invoke(scoped ReadOnlySpan<int> window)
        {
            foreach (var value in window)
            {
                Sum += value;
            }
        }
    }
}
