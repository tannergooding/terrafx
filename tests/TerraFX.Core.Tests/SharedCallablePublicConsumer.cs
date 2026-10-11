// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Core.Tests;

internal static class SharedCallablePublicConsumer
{
    public static void InvokeAction<TArgument, TAction>(scoped TArgument argument, scoped ref TAction action)
        where TArgument : allows ref struct
        where TAction : IAction<TArgument>, allows ref struct
    {
        action.Invoke(argument);
    }

    public static TResult InvokeFunc<TArgument, TResult, TFunction>(scoped TArgument argument, scoped ref TFunction function)
        where TArgument : allows ref struct
        where TFunction : IFunc<TArgument, TResult>, allows ref struct
    {
        return function.Invoke(argument);
    }

    public static TResult InvokeReadOnlyRefFunc<TArgument, TResult, TFunction>(scoped in TArgument argument, scoped ref TFunction function)
        where TArgument : allows ref struct
        where TFunction : IReadOnlyRefFunc<TArgument, TResult>, allows ref struct
    {
        return function.Invoke(in argument);
    }

    internal struct DoubleFunc : IFunc<int, int>
    {
        public readonly int Invoke(int argument)
        {
            return argument * 2;
        }
    }

    internal ref struct SpanIncrementAction(int amount) : IAction<Span<int>>
    {
        public readonly void Invoke(scoped Span<int> argument)
        {
            argument[0] += amount;
        }
    }

    internal struct SpanLengthFunc : IReadOnlyRefFunc<ReadOnlySpan<int>, int>
    {
        public readonly int Invoke(scoped in ReadOnlySpan<int> argument)
        {
            return argument.Length;
        }
    }

    internal struct SumAction(int initialValue) : IAction<int>
    {
        private int _sum = initialValue;

        public readonly int Sum => _sum;

        public void Invoke(int argument)
        {
            _sum += argument;
        }
    }
}
