// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests;

[TestFixture]
internal sealed class SystemPartitionRangeTests
{
    [TestCase(-1, 1, "start")]
    [TestCase(0, 0, "length")]
    [TestCase(0, -1, "length")]
    [TestCase(int.MaxValue, 1, "length")]
    [TestCase(int.MaxValue - 1, 2, "length")]
    public void ConstructorRejectsInvalidRanges(int start, int length, string parameterName)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new SystemPartitionRange(start, length));

        Assert.That(exception!.ParamName, Is.EqualTo(parameterName));
    }

    [Test]
    public void DefaultRangeIsInvalid()
    {
        var range = default(SystemPartitionRange);

        Assert.That(range.IsValid, Is.False);
        Assert.That(range.Start, Is.Zero);
        Assert.That(range.Length, Is.Zero);
        Assert.That(range.EndExclusive, Is.Zero);
    }

    [Test]
    public void EqualityUsesStartAndLength()
    {
        var range = new SystemPartitionRange(3, 5);
        var sameRange = new SystemPartitionRange(3, 5);

        Assert.That(range, Is.EqualTo(sameRange));
        Assert.That(range == sameRange, Is.True);
        Assert.That(range.GetHashCode(), Is.EqualTo(sameRange.GetHashCode()));
        Assert.That(range == new SystemPartitionRange(4, 4), Is.False);
        Assert.That(range != new SystemPartitionRange(3, 4), Is.True);
    }

    [Test]
    public void ValidRangeUsesCheckedHalfOpenEndpoints()
    {
        var range = new SystemPartitionRange(int.MaxValue - 2, 2);

        Assert.That(range.IsValid, Is.True);
        Assert.That(range.Start, Is.EqualTo(int.MaxValue - 2));
        Assert.That(range.Length, Is.EqualTo(2));
        Assert.That(range.EndExclusive, Is.EqualTo(int.MaxValue));
    }
}
