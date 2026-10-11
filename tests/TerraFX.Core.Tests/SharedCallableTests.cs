// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class SharedCallableTests
{
    [Test]
    public void OrdinaryStructCallablesDispatchThroughPublicInterfaces()
    {
        var action = new SharedCallablePublicConsumer.SumAction(10);

        SharedCallablePublicConsumer.InvokeAction(5, ref action);
        Assert.That(action.Sum, Is.EqualTo(15));

        var function = new SharedCallablePublicConsumer.DoubleFunc();
        Assert.That(SharedCallablePublicConsumer.InvokeFunc<int, int, SharedCallablePublicConsumer.DoubleFunc>(21, ref function), Is.EqualTo(42));
    }

    [Test]
    public void RefStructCallablesAndArgumentsDispatchWithoutEscaping()
    {
        Span<int> values = [1, 2, 3];
        var action = new SharedCallablePublicConsumer.SpanIncrementAction(4);

        SharedCallablePublicConsumer.InvokeAction(values, ref action);
        Assert.That(values[0], Is.EqualTo(5));

        ReadOnlySpan<int> readOnlyValues = values;
        var function = new SharedCallablePublicConsumer.SpanLengthFunc();

        Assert.That(SharedCallablePublicConsumer.InvokeReadOnlyRefFunc<ReadOnlySpan<int>, int, SharedCallablePublicConsumer.SpanLengthFunc>(readOnlyValues, ref function), Is.EqualTo(3));
    }
}
