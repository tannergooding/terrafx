// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class HostEventSequenceAllocatorTests
{
    [Test]
    public void AllocatesPositiveUniqueSequencesConcurrentlyAndNeverWraps()
    {
        Assert.That(HostEventSequenceAllocator.GetNext(0), Is.EqualTo(1));
        Assert.That(HostEventSequenceAllocator.GetNext(long.MaxValue - 1), Is.EqualTo(long.MaxValue));
        _ = Assert.Throws<InvalidOperationException>(() => HostEventSequenceAllocator.GetNext(long.MaxValue));
        _ = Assert.Throws<InvalidOperationException>(() => HostEventSequenceAllocator.GetNext(-1));

        var sequences = new long[2048];

        _ = Parallel.For(0, sequences.Length, index => sequences[index] = HostEventSequenceAllocator.Allocate());
        Assert.That(sequences, Has.All.GreaterThan(0));
        Assert.That(new HashSet<long>(sequences).Count, Is.EqualTo(sequences.Length));
    }
}
