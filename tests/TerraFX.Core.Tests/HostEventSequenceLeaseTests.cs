// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class HostEventSequenceLeaseTests
{
    [Test]
    public void LeasesShareTheProcessSequenceAndSupportConcurrentAllocation()
    {
        var firstLease = new HostEventSequenceLease();
        var secondLease = new HostEventSequenceLease();
        var firstSequence = firstLease.Allocate();
        var secondSequence = secondLease.Allocate();

        Assert.That(secondSequence, Is.GreaterThan(firstSequence));

        var sequences = new long[2048];
        _ = Parallel.For(0, sequences.Length, index => sequences[index] = secondLease.Allocate());

        Assert.That(sequences, Has.All.GreaterThan(secondSequence));
        Assert.That(new HashSet<long>(sequences).Count, Is.EqualTo(sequences.Length));
    }

    [Test]
    public void RevokedLeaseRejectsFurtherAllocation()
    {
        var lease = new HostEventSequenceLease();

        Assert.That(lease.Allocate(), Is.GreaterThan(0));
        lease.Revoke();
        _ = Assert.Throws<ObjectDisposedException>(() => lease.Allocate());
    }
}
