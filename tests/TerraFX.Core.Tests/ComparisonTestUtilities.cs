// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Numerics;
using NUnit.Framework;

namespace TerraFX.Core.Tests;

internal static class ComparisonTestUtilities
{
    internal static void AssertOrderingLaws<T>(T[] values)
        where T : IComparable<T>, IComparisonOperators<T, T, bool>
    {
        foreach (var first in values)
        {
            foreach (var second in values)
            {
                var comparison = Math.Sign(first.CompareTo(second));

                Assert.That(Math.Sign(second.CompareTo(first)), Is.EqualTo(-comparison));
                Assert.That(first < second, Is.EqualTo(comparison < 0));
                Assert.That(first <= second, Is.EqualTo(comparison <= 0));
                Assert.That(first > second, Is.EqualTo(comparison > 0));
                Assert.That(first >= second, Is.EqualTo(comparison >= 0));
            }
        }

        foreach (var first in values)
        {
            foreach (var second in values)
            {
                foreach (var third in values)
                {
                    if ((first.CompareTo(second) <= 0) && (second.CompareTo(third) <= 0))
                    {
                        Assert.That(first.CompareTo(third), Is.LessThanOrEqualTo(0));
                    }
                }
            }
        }
    }
}
